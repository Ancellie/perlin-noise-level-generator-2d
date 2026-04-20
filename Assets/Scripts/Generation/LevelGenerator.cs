using UnityEngine;
using UnityEngine.Tilemaps;

public class LevelGenerator : MonoBehaviour
{
    [Header("Tilemap Target")]
    [SerializeField] private Tilemap tilemap;

    [Header("Terrain Configuration")]
    [SerializeField] private TerrainConfigSO terrainConfig;

    private Tile[] _tileCache;
    private const int TexSize = 16;

    private void Awake()
    {
        ValidateReferences();
        BuildTileCache();
    }

    public void GenerateLevel(float[,] heightMap)
    {
        if (!ValidateReferences()) return;
        if (heightMap == null) { Debug.LogError("[LevelGenerator] Received a null heightMap."); return; }

        ClearLevel();

        int width  = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        TileBase[] tiles  = new TileBase[width * height];
        BoundsInt  bounds = new BoundsInt(0, 0, 0, width, height, 1);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tiles[x + y * width] = _tileCache[terrainConfig.GetLayerIndex(heightMap[x, y])];

        tilemap.SetTilesBlock(bounds, tiles);
        tilemap.RefreshAllTiles();

        Debug.Log($"[LevelGenerator] Placed {width * height:N0} tiles ({width}×{height}).");
    }

    public void ClearLevel() => tilemap.ClearAllTiles();

    public void RebuildCache() => BuildTileCache();

    private void BuildTileCache()
    {
        if (terrainConfig == null || terrainConfig.layers == null || terrainConfig.layers.Length == 0)
        {
            Debug.LogError("[LevelGenerator] TerrainConfig is missing or has no layers.");
            return;
        }

        if (_tileCache != null)
            foreach (var t in _tileCache)
                if (t != null && t.sprite != null) Destroy(t.sprite.texture);

        _tileCache = new Tile[terrainConfig.layers.Length];
        for (int i = 0; i < terrainConfig.layers.Length; i++)
            _tileCache[i] = CreateColoredTile(terrainConfig.layers[i].color);

        Debug.Log($"[LevelGenerator] Tile cache built — {_tileCache.Length} terrain layers.");
    }

    private Tile CreateColoredTile(Color fillColor)
    {
        Texture2D tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp
        };

        Color borderColor = new Color(fillColor.r * 0.70f, fillColor.g * 0.70f, fillColor.b * 0.70f, 1f);
        Color solidFill   = new Color(fillColor.r, fillColor.g, fillColor.b, 1f);
        Color[] pixels    = new Color[TexSize * TexSize];

        for (int py = 0; py < TexSize; py++)
            for (int px = 0; px < TexSize; px++)
            {
                bool isBorder = (px == 0 || py == 0 || px == TexSize - 1 || py == TexSize - 1);
                pixels[px + py * TexSize] = isBorder ? borderColor : solidFill;
            }

        tex.SetPixels(pixels);
        tex.Apply(false, false);

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f), TexSize);

        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.color  = Color.white;
        return tile;
    }

    private bool ValidateReferences()
    {
        bool ok = true;
        if (tilemap      == null) { Debug.LogError("[LevelGenerator] 'Tilemap' not assigned!");      ok = false; }
        if (terrainConfig == null) { Debug.LogError("[LevelGenerator] 'TerrainConfig' not assigned!"); ok = false; }
        return ok;
    }
}