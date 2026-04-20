using UnityEngine;

public static class NoiseGenerator
{
    public static float[,] GenerateHeightMap(
        int     width,
        int     height,
        int     seed,
        float   scale,
        int     octaves,
        float   persistence,
        float   lacunarity,
        Vector2 offset)
    {
        if (width  <= 0) width  = 1;
        if (height <= 0) height = 1;
        if (scale  <= 0) scale  = 0.0001f;

        float[,] heightMap = new float[width, height];

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];

        for (int i = 0; i < octaves; i++)
        {
            float ox = prng.Next(-100_000, 100_000) + offset.x;
            float oy = prng.Next(-100_000, 100_000) + offset.y;
            octaveOffsets[i] = new Vector2(ox, oy);
        }

        float maxNoise = float.MinValue;
        float minNoise = float.MaxValue;

        float halfW = width  * 0.5f;
        float halfH = height * 0.5f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float amplitude  = 1f;
                float frequency  = 1f;
                float noiseValue = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float sampleX = (x - halfW + octaveOffsets[o].x) / scale * frequency;
                    float sampleY = (y - halfH + octaveOffsets[o].y) / scale * frequency;

                    float perlin = Mathf.PerlinNoise(sampleX, sampleY) * 2f - 1f;
                    noiseValue += perlin * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                heightMap[x, y] = noiseValue;

                if (noiseValue > maxNoise) maxNoise = noiseValue;
                if (noiseValue < minNoise) minNoise = noiseValue;
            }
        }

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                heightMap[x, y] = Mathf.InverseLerp(minNoise, maxNoise, heightMap[x, y]);

        return heightMap;
    }
}