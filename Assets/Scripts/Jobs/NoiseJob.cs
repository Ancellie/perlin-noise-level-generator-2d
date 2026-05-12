using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

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
///   Octave offset buffers use <see cref="NativeDisableParallelForRestriction"/> because each
///   parallel iteration reads every octave (not only the iteration index).
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
    [ReadOnly] public NoiseBackend Backend;

    // Per-octave random offsets (pre-computed on main thread, passed in)
    [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float2> OctaveOffsets;
    [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float2> MoistureOffsets;
    [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float2> TempOffsets;

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

        HeightMap[index]      = TerrainFbm.SampleNormalized(wx, wy, Scale, Octaves, Persistence, Lacunarity, Backend, OctaveOffsets);
        MoistureMap[index]    = TerrainFbm.SampleNormalized(wx, wy, Scale, Octaves, Persistence, Lacunarity, Backend, MoistureOffsets);
        TemperatureMap[index] = TerrainFbm.SampleNormalized(wx, wy, Scale, Octaves, Persistence, Lacunarity, Backend, TempOffsets);
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
        NoiseBackend noiseBackend,
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

        elevOff  = CreateOctaveOffsets(seed,          octaves, Allocator.TempJob);
        moistOff = CreateOctaveOffsets(seed + 31337,  octaves, Allocator.TempJob);
        tmpOff   = CreateOctaveOffsets(seed + 99991,  octaves, Allocator.TempJob);

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
            Backend        = noiseBackend,
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

    /// <summary>Same PRNG sequence as runtime jobs — use for editor preview parity.</summary>
    public static NativeArray<float2> CreateOctaveOffsets(int seed, int octaves, Allocator allocator)
    {
        var rng = new System.Random(seed);
        var arr = new NativeArray<float2>(octaves, allocator, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < octaves; i++)
            arr[i] = new float2(
                rng.Next(-100_000, 100_000),
                rng.Next(-100_000, 100_000));
        return arr;
    }
}
