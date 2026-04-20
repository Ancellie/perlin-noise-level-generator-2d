using UnityEngine;

[CreateAssetMenu(
    fileName = "TerrainConfig",
    menuName  = "ProceduralWorld/Terrain Config",
    order     = 0)]
public class TerrainConfigSO : ScriptableObject
{
    public TerrainLayer[] layers = new TerrainLayer[]
    {
        new TerrainLayer { name = "Water",    heightThreshold = 0.35f, color = new Color(0.12f, 0.40f, 0.80f, 1f) },
        new TerrainLayer { name = "Sand",     heightThreshold = 0.45f, color = new Color(0.93f, 0.86f, 0.51f, 1f) },
        new TerrainLayer { name = "Grass",    heightThreshold = 0.72f, color = new Color(0.22f, 0.62f, 0.16f, 1f) },
        new TerrainLayer { name = "Mountain", heightThreshold = 1.00f, color = new Color(0.52f, 0.47f, 0.42f, 1f) },
    };

    public TerrainLayer GetLayer(float normalizedHeight)
    {
        if (layers == null || layers.Length == 0) return null;
        for (int i = 0; i < layers.Length; i++)
            if (normalizedHeight <= layers[i].heightThreshold)
                return layers[i];
        return layers[layers.Length - 1];
    }

    public int GetLayerIndex(float normalizedHeight)
    {
        if (layers == null || layers.Length == 0) return 0;
        for (int i = 0; i < layers.Length; i++)
            if (normalizedHeight <= layers[i].heightThreshold)
                return i;
        return layers.Length - 1;
    }
}