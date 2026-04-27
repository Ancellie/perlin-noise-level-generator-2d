using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Burst-compiled parallel job that fills three noise maps simultaneously:
///   • Elevation   — primary terrain shape
///   • Moisture    — drives desert ↔ forest biome axis
///   • Temperature — drives tropical ↔ arctic biome axis
///
/// WHY THREE SEPARATE OFFSET SEEDS?
///   Using different PRNG offsets for each map keeps the channels statistically
///   independent even though they share lacunarity/persistence. Correlated
///   elevation + moisture would produce boring worlds (all high ground = dry).
///
/// BURST RESTRICTIONS observed in this job:
///   • No managed types (string, List, class) — only value types / NativeArrays.
///   • math.* (Unity.Mathematics) is used instead of Mathf.* for vectorization.
///   • IJobParallelFor: each tile index is processed independently → safe.
///
/// THREAD SAFETY:
///   All reads are from read-only NativeArrays; writes go to separate output
///   NativeArrays that are not accessed by any other job simultaneously.
///   The ChunkStreamer schedules jobs with no data hazards.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public struct NoiseJob : IJobParallelFor
{
    // ── Inputs (read-only, passed from ChunkStreamer) ─────────────────────────

    [ReadOnly] public int    ChunkSize;
    [ReadOnly] public int    ChunkX;        // chunk coord, not world tile
    [ReadOnly] public int    ChunkY;
    [ReadOnly] public float  Scale;
    [ReadOnly] public int    Octaves;
    [ReadOnly] public float  Persistence;
    [ReadOnly] public float  Lacunarity;
    [ReadOnly] public int    Seed;

    // Per-octave random offsets (pre-computed on main thread, passed in)
    [ReadOnly] public NativeArray<float2> OctaveOffsets;       // elevation
    [ReadOnly] public NativeArray<float2> MoistureOffsets;
    [ReadOnly] public NativeArray<float2> TempOffsets;

    // ── Outputs ───────────────────────────────────────────────────────────────

    [WriteOnly] public NativeArray<float> HeightMap;
    [WriteOnly] public NativeArray<float> MoistureMap;
    [WriteOnly] public NativeArray<float> TemperatureMap;

    // ── IJobParallelFor ───────────────────────────────────────────────────────

    /// <summary>index = lx + ly * ChunkSize (tile-local, row-major).</summary>
    public void Execute(int index)
    {
        int lx = index % ChunkSize;
        int ly = index / ChunkSize;

        // World-tile position of this texel
        int wx = ChunkX * ChunkSize + lx;
        int wy = ChunkY * ChunkSize + ly;

        HeightMap[index]      = FbmNoise(wx, wy, OctaveOffsets);
        MoistureMap[index]    = FbmNoise(wx, wy, MoistureOffsets);
        TemperatureMap[index] = FbmNoise(wx, wy, TempOffsets);
    }

    // ── fBm core ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fractional Brownian Motion using Unity.Mathematics.noise.snoise
    /// (Simplex noise — smoother and faster than classic Perlin for Burst).
    ///
    /// NOTE: snoise returns [-1, 1], so we don't need the [0,1]→[-1,1] shift
    /// that the CPU Mathf.PerlinNoise path requires.  The raw accumulated value
    /// is normalized post-hoc (see ChunkStreamer.NormalizeInPlace).
    /// </summary>
    private float FbmNoise(int wx, int wy, NativeArray<float2> offsets)
    {
        float amplitude  = 1f;
        float frequency  = 1f;
        float value      = 0f;
        float maxValue   = 0f;   // used for in-job normalization (approximate)

        float halfScale = Scale * 0.5f;

        for (int o = 0; o < Octaves; o++)
        {
            float sx = (wx + offsets[o].x) / Scale * frequency;
            float sy = (wy + offsets[o].y) / Scale * frequency;

            value    += noise.snoise(new float2(sx, sy)) * amplitude;
            maxValue += amplitude;

            amplitude *= Persistence;
            frequency *= Lacunarity;
        }

        // Normalize to [0, 1] within the job to avoid a separate normalization pass.
        // maxValue is the theoretical maximum (all octaves at +1), so dividing gives
        // a conservative normalization; ChunkStreamer may apply a global remap pass.
        return math.saturate((value / maxValue) * 0.5f + 0.5f);
    }
}

/// <summary>
/// Helper: pre-computes per-octave random offsets on the main thread using
/// a seeded System.Random, then schedules NoiseJob on the worker threads.
///
/// Returned JobHandle must be .Complete()'d before reading the NativeArrays.
/// Caller is responsible for calling Dispose() on all NativeArrays after use.
/// </summary>
public static class NoiseJobScheduler
{
    public static JobHandle Schedule(
        ChunkCoord coord,
        int        chunkSize,
        int        seed,
        float      scale,
        int        octaves,
        float      persistence,
        float      lacunarity,
        out NativeArray<float> heightOut,
        out NativeArray<float> moistureOut,
        out NativeArray<float> tempOut,
        out NativeArray<float2> elevOff,
        out NativeArray<float2> moistOff,
        out NativeArray<float2> tmpOff)
    {
        int tileCount = chunkSize * chunkSize;

        heightOut  = new NativeArray<float>(tileCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        moistureOut = new NativeArray<float>(tileCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        tempOut    = new NativeArray<float>(tileCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        elevOff  = BuildOffsets(seed,          octaves);
        moistOff = BuildOffsets(seed + 31337,  octaves);   // different seed per channel
        tmpOff   = BuildOffsets(seed + 99991,  octaves);

        var job = new NoiseJob
        {
            ChunkSize      = chunkSize,
            ChunkX         = coord.X,
            ChunkY         = coord.Y,
            Scale          = scale,
            Octaves        = octaves,
            Persistence    = persistence,
            Lacunarity     = lacunarity,
            Seed           = seed,
            OctaveOffsets  = elevOff,
            MoistureOffsets = moistOff,
            TempOffsets    = tmpOff,
            HeightMap      = heightOut,
            MoistureMap    = moistureOut,
            TemperatureMap = tempOut,
        };

        // innerloopBatchCount = chunkSize: one batch per row → good cache locality
        return job.Schedule(tileCount, chunkSize);
    }

    private static NativeArray<float2> BuildOffsets(int seed, int octaves)
    {
        var rng = new System.Random(seed);
        var arr = new NativeArray<float2>(octaves, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < octaves; i++)
            arr[i] = new float2(
                rng.Next(-100_000, 100_000),
                rng.Next(-100_000, 100_000));
        return arr;
    }
}
