using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Single source of truth for fBM normalization used by <see cref="NoiseJob"/> and editor preview.
/// </summary>
[BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
public static class TerrainFbm
{
    /// <summary>
    /// Fractional Brownian motion in 2D, normalized to [0, 1].
    /// Matches world-space sampling: world tile (wx, wy) plus per-octave offsets, scaled and frequency-warped.
    /// </summary>
    public static float SampleNormalized(
        int wx,
        int wy,
        float scale,
        int octaves,
        float persistence,
        float lacunarity,
        NoiseBackend backend,
        NativeArray<float2> octaveOffsets)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float value = 0f;
        float maxValue = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float sx = (wx + octaveOffsets[o].x) / scale * frequency;
            float sy = (wy + octaveOffsets[o].y) / scale * frequency;
            float2 p = new float2(sx, sy);

            float n = backend == NoiseBackend.ClassicPerlinFbm
                ? noise.cnoise(p)
                : noise.snoise(p);

            value += n * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return math.saturate((value / maxValue) * 0.5f + 0.5f);
    }
}
