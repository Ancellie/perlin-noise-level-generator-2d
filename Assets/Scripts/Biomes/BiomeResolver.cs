using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BiomeDefinition
{
    [Header("Identity")]
    public string name = "Unnamed";
    public int    index;

    [Header("Colour")]
    public Color primaryColor   = Color.white;
    public Color secondaryColor = Color.white;

    [Header("Thresholds — elevation overrides")]
    [Range(0f, 1f)] public float oceanMaxHeight    = 0.35f;
    [Range(0f, 1f)] public float mountainMinHeight = 0.72f;

    [Header("Climate Centre (temp, moisture)")]
    [Range(0f, 1f)] public float idealTemperature = 0.5f;
    [Range(0f, 1f)] public float idealMoisture    = 0.5f;
    [Range(0.05f, 1f)] public float influence = 0.5f;

    [Header("Object Spawning")]
    public BiomeObjectRule[] spawnRules = Array.Empty<BiomeObjectRule>();

    public float GetWeight(float temperature, float moisture)
    {
        float dt = temperature - idealTemperature;
        float dm = moisture    - idealMoisture;
        float distSq = dt * dt + dm * dm;
        return Mathf.Exp(-distSq / (2f * influence * influence));
    }
}

[Serializable]
public class BiomeObjectRule
{
    public GameObject prefab;
    [Range(0f, 1f)] public float noiseThreshold = 0.7f;
    [Range(0f, 1f)] public float density = 0.4f;
    public int seedOffset = 1000;
    [Range(0f, 360f)] public float yRotationRange = 360f;
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
}

public readonly struct BiomeBlendSample
{
    public readonly int     DominantIndex;
    public readonly float[] Weights;
    public readonly Color   BlendedColor;

    public BiomeBlendSample(int dominant, float[] weights, Color blended)
    {
        DominantIndex = dominant;
        Weights       = weights;
        BlendedColor  = blended;
    }
}

public class BiomeResolver
{
    private readonly BiomeDefinition[] _biomes;
    private readonly int               _oceanIndex;
    private readonly int               _mountainIndex;

    public int BiomeCount => _biomes.Length;

    public BiomeResolver(BiomeDefinition[] biomes)
    {
        _biomes = biomes;
        _oceanIndex    = FindIndex("Ocean");
        _mountainIndex = FindIndex("Mountain");

        if (_oceanIndex < 0)    Debug.LogWarning("[BiomeResolver] No 'Ocean' biome found.");
        if (_mountainIndex < 0) Debug.LogWarning("[BiomeResolver] No 'Mountain' biome found.");
    }

    /// <summary>Allocates weights; use for editor/preview. Hot path should call <see cref="ResolveDominantIndex"/>.</summary>
    public BiomeBlendSample Resolve(float height, float moisture, float temperature)
    {
        using (GenerationProfilerMarkers.BiomeResolve.Auto())
        {
            float[] weights = new float[_biomes.Length];
            FillWeights(height, moisture, temperature, weights);

            int   dominant = 0;
            float maxW     = -1f;
            Color blended  = Color.black;

            for (int i = 0; i < _biomes.Length; i++)
            {
                blended += _biomes[i].primaryColor * weights[i];
                if (weights[i] > maxW) { maxW = weights[i]; dominant = i; }
            }

            return new BiomeBlendSample(dominant, weights, blended);
        }
    }

    /// <summary>
    /// Dominant biome only — reuses <paramref name="weightsScratch"/> (length ≥ biome count). No allocations.
    /// </summary>
    public int ResolveDominantIndex(float height, float moisture, float temperature, float[] weightsScratch)
    {
        if (weightsScratch == null || weightsScratch.Length < _biomes.Length)
            throw new ArgumentException("weightsScratch must be at least BiomeCount long.", nameof(weightsScratch));

        using (GenerationProfilerMarkers.BiomeResolve.Auto())
        {
            FillWeights(height, moisture, temperature, weightsScratch);

            int   dominant = 0;
            float maxW     = -1f;
            for (int i = 0; i < _biomes.Length; i++)
            {
                if (weightsScratch[i] > maxW) { maxW = weightsScratch[i]; dominant = i; }
            }

            return dominant;
        }
    }

    /// <summary>Writes full biome weight vector; buffer must have length ≥ <see cref="BiomeCount"/>.</summary>
    private void FillWeights(float height, float moisture, float temperature, float[] weights)
    {
        float oceanWeight    = 0f;
        float mountainWeight = 0f;

        if (_oceanIndex >= 0 && _biomes[_oceanIndex].oceanMaxHeight > 0f)
        {
            float ot = _biomes[_oceanIndex].oceanMaxHeight;
            oceanWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ot, ot * 0.5f, height));
        }

        if (_mountainIndex >= 0 && _biomes[_mountainIndex].mountainMinHeight < 1f)
        {
            float mt = _biomes[_mountainIndex].mountainMinHeight;
            mountainWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mt, Mathf.Min(mt + 0.15f, 1f), height));
        }

        float overrideTotal = Mathf.Clamp01(oceanWeight + mountainWeight);
        float climateWeight = 1f - overrideTotal;

        float climateSum = 0f;
        for (int i = 0; i < _biomes.Length; i++)
        {
            if (i == _oceanIndex || i == _mountainIndex) continue;
            weights[i] = _biomes[i].GetWeight(temperature, moisture);
            climateSum += weights[i];
        }

        if (climateSum > 0f)
            for (int i = 0; i < _biomes.Length; i++)
            {
                if (i == _oceanIndex || i == _mountainIndex) continue;
                weights[i] = (weights[i] / climateSum) * climateWeight;
            }

        if (_oceanIndex    >= 0) weights[_oceanIndex]    = oceanWeight;
        if (_mountainIndex >= 0) weights[_mountainIndex] = mountainWeight;
    }

    public BiomeDefinition GetBiome(int index) => _biomes[index];

    private int FindIndex(string biomeName)
    {
        for (int i = 0; i < _biomes.Length; i++)
            if (_biomes[i].name == biomeName) return i;
        return -1;
    }
}