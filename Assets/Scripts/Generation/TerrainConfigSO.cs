using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MODIFIED from v1 — now stores BiomeDefinitions instead of simple TerrainLayers.
///
/// MIGRATION NOTE:
///   If upgrading an existing project, delete the old TerrainConfig.asset and
///   recreate it via Right-click → Create → ProceduralWorld → Terrain Config V2.
///   The old TerrainLayer[] API is replaced by BiomeDefinition[].
///
/// DEFAULT BIOMES pre-populated in the constructor cover a Whittaker-style biome grid:
///   Ocean, Beach, Desert, Savanna, Grassland, Shrubland,
///   TropicalForest, TemperateForest, BorealForest, Tundra, Mountain, Snow
/// </summary>
[CreateAssetMenu(
    fileName = "TerrainConfig",
    menuName  = "ProceduralWorld/Terrain Config V2",
    order     = 0)]
public class TerrainConfigSO : ScriptableObject
{
    // ── Biome List ───────────────────────────────────────────────────────────────

    [Tooltip("All biome definitions. Order does not matter — resolver uses Gaussian weights.")]
    public BiomeDefinition[] biomes = DefaultBiomes();

    // ── Tile lookup cache (built at runtime) ─────────────────────────────────────

    // Runtime tile cache: biome index → UnityEngine.Tilemaps.Tile
    // Built by LevelGenerator on Awake.
    [System.NonSerialized]
    public UnityEngine.Tilemaps.Tile[] TileCache;

    // ── Resolver (cached singleton) ───────────────────────────────────────────────

    private BiomeResolver _resolver;

    public BiomeResolver GetResolver()
    {
        if (_resolver == null)
        {
            AssignIndices();
            _resolver = new BiomeResolver(biomes);
        }
        return _resolver;
    }

    /// <summary>Force rebuild after Inspector changes (called by custom Editor).</summary>
    public void InvalidateResolver() => _resolver = null;

    // ── Validation ────────────────────────────────────────────────────────────────

    private void OnValidate()
    {
        AssignIndices();
        _resolver = null;   // force rebuild next access
    }

    private void AssignIndices()
    {
        if (biomes == null) return;
        for (int i = 0; i < biomes.Length; i++)
            biomes[i].index = i;
    }

    // ── Default Data ─────────────────────────────────────────────────────────────

    private static BiomeDefinition[] DefaultBiomes() => new[]
    {
        // ── Elevation overrides ────────────────────────────────────────────────
        new BiomeDefinition
        {
            name = "Ocean", oceanMaxHeight = 0.35f, mountainMinHeight = 1f,
            idealTemperature = 0.5f, idealMoisture = 0.9f, influence = 0.8f,
            primaryColor = new Color(0.08f, 0.30f, 0.72f),
            secondaryColor = new Color(0.12f, 0.40f, 0.85f)
        },
        new BiomeDefinition
        {
            name = "Mountain", oceanMaxHeight = 0f, mountainMinHeight = 0.72f,
            idealTemperature = 0.2f, idealMoisture = 0.3f, influence = 0.8f,
            primaryColor = new Color(0.50f, 0.45f, 0.40f),
            secondaryColor = new Color(0.60f, 0.55f, 0.50f)
        },
        new BiomeDefinition
        {
            name = "Snow", oceanMaxHeight = 0f, mountainMinHeight = 0.88f,
            idealTemperature = 0.05f, idealMoisture = 0.5f, influence = 0.4f,
            primaryColor = new Color(0.92f, 0.95f, 1.00f),
            secondaryColor = new Color(0.85f, 0.90f, 0.97f)
        },

        // ── Climate biomes ─────────────────────────────────────────────────────
        new BiomeDefinition
        {
            name = "Beach",
            idealTemperature = 0.6f, idealMoisture = 0.3f, influence = 0.25f,
            oceanMaxHeight = 0f, mountainMinHeight = 1f,
            primaryColor = new Color(0.93f, 0.87f, 0.55f),
            secondaryColor = new Color(0.88f, 0.80f, 0.45f)
        },
        new BiomeDefinition
        {
            name = "Desert",
            idealTemperature = 0.9f, idealMoisture = 0.1f, influence = 0.35f,
            oceanMaxHeight = 0f, mountainMinHeight = 1f,
            primaryColor = new Color(0.90f, 0.78f, 0.40f),
            secondaryColor = new Color(0.82f, 0.68f, 0.30f)
        },
        new BiomeDefinition
        {
            name = "Savanna",
            idealTemperature = 0.8f, idealMoisture = 0.3f, influence = 0.30f,
            oceanMaxHeight = 0f, mountainMinHeight = 1f,
            primaryColor = new Color(0.75f, 0.72f, 0.28f),
            secondaryColor = new Color(0.65f, 0.60f, 0.22f)
        },
        new BiomeDefinition
        {
            name = "Grassland",
            idealTemperature = 0.5f, idealMoisture = 0.4f, influence = 0.35f,
            oceanMaxHeight = 0f, mountainMinHeight = 1f,
            primaryColor = new Color(0.45f, 0.75f, 0.25f),
            secondaryColor = new Color(0.38f, 0.65f, 0.20f)
        },
        new BiomeDefinition
        {
            name = "TropicalForest",
            idealTemperature = 0.9f, idealMoisture = 0.85f, influence = 0.35f,
            oceanMaxHeight = 0f, mountainMinHeight = 1f,
            primaryColor = new Color(0.10f, 0.55f, 0.10f),
            secondaryColor = new Color(0.08f, 0.45f, 0.08f)
        },
        new BiomeDefinition
        {
            name = "TemperateForest",
            idealTemperature = 0.5f, idealMoisture = 0.7f, influence = 0.35f,
            oceanMaxHeight = 0f, mountainMinHeight = 1f,
            primaryColor = new Color(0.18f, 0.55f, 0.18f),
            secondaryColor = new Color(0.22f, 0.48f, 0.15f)
        },
        new BiomeDefinition
        {
            name = "BorealForest",
            idealTemperature = 0.25f, idealMoisture = 0.6f, influence = 0.30f,
            oceanMaxHeight = 0f, mountainMinHeight = 1f,
            primaryColor = new Color(0.15f, 0.42f, 0.22f),
            secondaryColor = new Color(0.10f, 0.35f, 0.17f)
        },
        new BiomeDefinition
        {
            name = "Tundra",
            idealTemperature = 0.1f, idealMoisture = 0.4f, influence = 0.30f,
            oceanMaxHeight = 0f, mountainMinHeight = 1f,
            primaryColor = new Color(0.65f, 0.72f, 0.60f),
            secondaryColor = new Color(0.55f, 0.62f, 0.50f)
        },
    };
}
