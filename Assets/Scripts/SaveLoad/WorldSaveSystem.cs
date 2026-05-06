using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Chunk-based delta save/load system.
///
/// SAVE STRATEGY (delta encoding):
///   • Only MODIFIED chunks are persisted (player-edited tile overrides).
///   • Unmodified chunks are always regeneratable from seed — no disk cost.
///   • A world manifest (JSON) stores the seed + settings + list of saved chunks.
///   • Each chunk's override dictionary is serialized to a separate .chunk file.
///
/// FILE LAYOUT:
///   {persistentDataPath}/Worlds/{worldName}/
///       manifest.json        ← WorldManifest (seed, dimensions, chunk list)
///       chunks/
///           -3_2.chunk       ← ChunkSaveData for chunk (-3, 2) — only if modified
///           0_0.chunk
///           ...
///
/// WHY NOT ONE BIG FILE?
///   Chunk files allow partial saves (only dirty chunks written) and avoid
///   loading the entire world into memory when streaming a large save.
///
/// THREAD SAFETY:
///   File I/O is synchronous here. For production, wrap Save/Load in
///   System.Threading.Tasks.Task.Run(() => ...) and marshal results back
///   to the main thread. Left synchronous to keep this demo self-contained.
/// </summary>
public class WorldSaveSystem : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────────

    [Header("Save Settings")]
    [Tooltip("Sub-folder under Application.persistentDataPath/Worlds/")]
    [SerializeField] private string worldName = "DefaultWorld";

    [Header("References")]
    [SerializeField] private ChunkStreamer chunkStreamer;

    // ── Path Helpers ──────────────────────────────────────────────────────────────

    private string WorldRoot    => Path.Combine(Application.persistentDataPath, "Worlds", worldName);
    private string ManifestPath => Path.Combine(WorldRoot, "manifest.json");
    private string ChunkDir     => Path.Combine(WorldRoot, "chunks");
    private string ChunkPath(ChunkCoord c) => Path.Combine(ChunkDir, $"{c.X}_{c.Y}.chunk");

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves all dirty (modified) active chunks and the world manifest.
    /// Returns number of chunk files written.
    /// </summary>
    public int SaveWorld(GenerationSettings settings)
    {
        Directory.CreateDirectory(ChunkDir);

        // Build manifest
        var manifest = new WorldManifest
        {
            worldName   = worldName,
            seed        = settings.seed,
            savedAt     = DateTime.UtcNow.ToString("o"),
            scale       = settings.scale,
            octaves     = settings.octaves,
            persistence = settings.persistence,
            lacunarity  = settings.lacunarity,
            width         = settings.width,
            height        = settings.height,
            infiniteWorld = settings.infiniteWorld,
            savedChunks = new List<string>()
        };

        int written = 0;

        foreach (var kvp in chunkStreamer.ActiveChunks)
        {
            var chunk = kvp.Value;
            if (!chunk.IsDirty) continue;

            var chunkSave = new ChunkSaveData
            {
                chunkX    = chunk.Coord.X,
                chunkY    = chunk.Coord.Y,
                overrides = new List<TileOverrideEntry>()
            };

            foreach (var ov in chunk.TileOverrides)
            {
                chunkSave.overrides.Add(new TileOverrideEntry
                {
                    lx         = ov.Key.x,
                    ly         = ov.Key.y,
                    biomeIndex = ov.Value
                });
            }

            string json = JsonUtility.ToJson(chunkSave, true);
            File.WriteAllText(ChunkPath(chunk.Coord), json, Encoding.UTF8);

            manifest.savedChunks.Add($"{chunk.Coord.X}_{chunk.Coord.Y}");
            written++;
        }

        // Write (or overwrite) manifest
        File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);

        Debug.Log($"[WorldSaveSystem] Saved world '{worldName}' — {written} modified chunks written.");
        return written;
    }

    /// <summary>
    /// Loads the world manifest. Returns null if no save exists.
    /// Chunk overrides are lazily loaded by LoadChunkOverrides() during streaming.
    /// </summary>
    public WorldManifest LoadManifest()
    {
        if (!File.Exists(ManifestPath))
        {
            Debug.Log($"[WorldSaveSystem] No save found at '{ManifestPath}'.");
            return null;
        }

        string json     = File.ReadAllText(ManifestPath, Encoding.UTF8);
        var manifest    = JsonUtility.FromJson<WorldManifest>(json);
        Debug.Log($"[WorldSaveSystem] Loaded manifest for '{manifest.worldName}' (seed {manifest.seed}).");
        return manifest;
    }

    /// <summary>
    /// Applies saved tile overrides to a ChunkData object.
    /// Called by ChunkStreamer when a chunk is being flushed, if a .chunk file exists.
    /// </summary>
    public bool LoadChunkOverrides(ChunkData chunk)
    {
        string path = ChunkPath(chunk.Coord);
        if (!File.Exists(path)) return false;

        string json        = File.ReadAllText(path, Encoding.UTF8);
        var chunkSave      = JsonUtility.FromJson<ChunkSaveData>(json);

        chunk.TileOverrides.Clear();
        foreach (var entry in chunkSave.overrides)
            chunk.TileOverrides[new Vector2Int(entry.lx, entry.ly)] = entry.biomeIndex;

        if (chunk.TileOverrides.Count > 0)
            chunk.MarkModified();

        return true;
    }

    /// <summary>
    /// Deletes the save for this world.
    /// </summary>
    public void DeleteSave()
    {
        if (Directory.Exists(WorldRoot))
        {
            Directory.Delete(WorldRoot, recursive: true);
            Debug.Log($"[WorldSaveSystem] Deleted save for '{worldName}'.");
        }
    }

    public bool SaveExists => File.Exists(ManifestPath);

    /// <summary>Converts a saved manifest back into GenerationSettings.</summary>
    public static GenerationSettings ManifestToSettings(WorldManifest m) => new GenerationSettings
    {
        seed        = m.seed,
        scale       = m.scale,
        octaves     = m.octaves,
        persistence = m.persistence,
        lacunarity  = m.lacunarity,
        width         = m.width,
        height        = m.height,
        infiniteWorld = m.infiniteWorld
    };
}

// ── Serializable data structures (JSON-friendly) ─────────────────────────────────

/// <summary>Top-level save file — world metadata.</summary>
[Serializable]
public class WorldManifest
{
    public string       worldName;
    public int          seed;
    public string       savedAt;
    public float        scale;
    public int          octaves;
    public float        persistence;
    public float        lacunarity;
    public int          width;
    public int          height;
    public bool         infiniteWorld;
    
    public List<string> savedChunks;   // "x_y" strings for fast existence checks
}

/// <summary>Per-chunk file — sparse list of tile overrides.</summary>
[Serializable]
public class ChunkSaveData
{
    public int chunkX;
    public int chunkY;
    public List<TileOverrideEntry> overrides;
}

/// <summary>One tile that was modified after generation.</summary>
[Serializable]
public class TileOverrideEntry
{
    public int lx;          // tile-local x [0…chunkSize-1]
    public int ly;          // tile-local y
    public int biomeIndex;  // new biome
}
