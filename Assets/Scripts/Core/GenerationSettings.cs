using UnityEngine;

[System.Serializable]
public class GenerationSettings
{
    [Header("Map Dimensions")]
    [Tooltip("Number of tiles along the X axis.")]
    [Range(10, 500)]
    public int width = 120;

    [Tooltip("Number of tiles along the Y axis.")]
    [Range(10, 500)]
    public int height = 80;

    [Header("Noise Parameters")]
    [Tooltip("Integer seed. Same seed + same settings = identical map every time.")]
    public int seed = 42;

    [Tooltip("Controls the 'zoom' of the noise. Larger = smoother / more zoomed-out.")]
    [Range(1f, 200f)]
    public float scale = 40f;

    [Tooltip("Number of Perlin Noise layers summed together (fBm octaves). More = more detail.")]
    [Range(1, 8)]
    public int octaves = 4;

    [Tooltip("How much each successive octave contributes to the final value. " +
             "Low = smooth; High = rough.")]
    [Range(0.1f, 1f)]
    public float persistence = 0.5f;

    [Tooltip("How much the frequency increases per octave. " +
             "2.0 is the standard 'doubling' value.")]
    [Range(1f, 4f)]
    public float lacunarity = 2f;

    [Tooltip("Manual 2D scroll / pan offset for the noise sample window.")]
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
        $"[Seed:{seed} | {width}×{height} | Scale:{scale:F1} | " +
        $"Oct:{octaves} | Persist:{persistence:F2} | Lac:{lacunarity:F2}]";
}