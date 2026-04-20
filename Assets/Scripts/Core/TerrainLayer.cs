using UnityEngine;

[System.Serializable]
public class TerrainLayer
{
    [Tooltip("Human-readable name shown in the Inspector, e.g. 'Water', 'Grass'.")]
    public string name = "Unnamed";

    [Tooltip("Normalized height threshold [0–1]. " +
             "Tiles with heightMap[x,y] <= this value are assigned this layer.\n" +
             "Must be ordered lowest → highest across the layers array.")]
    [Range(0f, 1f)]
    public float heightThreshold = 0.5f;

    [Tooltip("Solid colour used to tint the runtime-generated tile sprite for this layer.")]
    public Color color = Color.white;
}