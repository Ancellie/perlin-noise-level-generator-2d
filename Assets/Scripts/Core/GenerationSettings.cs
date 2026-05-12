using UnityEngine;

/// <summary>UNCHANGED from v1 — data class passed between UI → WorldManager → jobs.</summary>
[System.Serializable]
public class GenerationSettings
{
    [Header("Map Dimensions")]
    [Tooltip("When true, the world has no bounds — chunks are generated in all directions indefinitely.")]
    public bool infiniteWorld = false;
    [Range(10, 500)] public int width  = 120;
    [Range(10, 500)] public int height = 80;

    [Header("Noise Parameters")]
    [Tooltip("Simplex (snoise) vs classic Perlin (cnoise) — same fBM, Burst-compiled.")]
    public NoiseBackend noiseBackend = NoiseBackend.SimplexFbm;
    public int     seed        = 42;
    [Range(1f, 200f)]  public float scale       = 40f;
    [Range(1, 8)]      public int   octaves     = 4;
    [Range(0.1f, 1f)]  public float persistence = 0.5f;
    [Range(1f, 4f)]    public float lacunarity  = 2f;
    public Vector2 offset = Vector2.zero;

    public void Validate()
    {
        width       = Mathf.Max(1, width);
        height      = Mathf.Max(1, height);
        scale       = Mathf.Max(0.01f, scale);
        octaves     = Mathf.Clamp(octaves, 1, 8);
        persistence = Mathf.Clamp(persistence, 0.1f, 1f);
        lacunarity  = Mathf.Max(1f, lacunarity);
    }

    public override string ToString() =>
        $"[{noiseBackend} | Seed:{seed} | Scale:{scale:F1} | Oct:{octaves} | Persist:{persistence:F2} | Lac:{lacunarity:F2}]";
}


[System.Serializable]
public class TerrainLayer
{
    public string name           = "Unnamed";
    [Range(0f, 1f)] public float heightThreshold = 0.5f;
    public Color  color          = Color.white;
}