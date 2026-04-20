using UnityEngine;
using UnityEngine.UI;

public class MinimapRenderer : MonoBehaviour
{
    [Header("UI Target")]
    [SerializeField] private RawImage minimapImage;

    [Header("Terrain Configuration")]
    [SerializeField] private TerrainConfigSO terrainConfig;

    private Texture2D _minimapTexture;

    private void Start()
    {
        if (WorldManager.Instance != null)
            WorldManager.Instance.OnWorldGenerated += OnWorldGenerated;
    }

    private void OnDestroy()
    {
        if (WorldManager.Instance != null)
            WorldManager.Instance.OnWorldGenerated -= OnWorldGenerated;
        if (_minimapTexture != null) Destroy(_minimapTexture);
    }

    private void OnWorldGenerated(GenerationSettings _)
    {
        float[,] heightMap = WorldManager.Instance.LastHeightMap;
        if (heightMap != null) RenderMinimap(heightMap);
    }

    private void RenderMinimap(float[,] heightMap)
    {
        if (minimapImage == null || terrainConfig == null) return;

        int width  = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        if (_minimapTexture == null || _minimapTexture.width != width || _minimapTexture.height != height)
        {
            if (_minimapTexture != null) Destroy(_minimapTexture);
            _minimapTexture = new Texture2D(width, height, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };
        }

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            TerrainLayer layer = terrainConfig.GetLayer(heightMap[x, y]);
            pixels[x + y * width] = layer != null ? layer.color : Color.black;
        }

        _minimapTexture.SetPixels(pixels);
        _minimapTexture.Apply();
        minimapImage.texture = _minimapTexture;
    }
}