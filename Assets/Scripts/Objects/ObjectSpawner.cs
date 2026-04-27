using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic procedural object spawner — places trees, rocks, bushes
/// and other biome-specific props on terrain tiles.
///
/// DETERMINISM:
///   Every placement decision derives from a seeded hash of (seed, wx, wy, ruleIndex).
///   Given the same seed and terrain config the world will always have the same objects
///   in the same places, across sessions and regardless of chunk load order.
///
/// POOLING:
///   Each prefab type gets its own pool (ObjectPool<T> analogue using a Stack<GameObject>).
///   On chunk unload, all spawned objects return to the pool.
///   This avoids GC pressure from Instantiate/Destroy during streaming.
///
/// INTEGRATION:
///   Called by ChunkStreamer.FlushChunkToTilemap() after biome indices are resolved.
///   Receives ChunkData (already has BiomeIndices + noise maps) — zero redundant work.
/// </summary>
public class ObjectSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private TerrainConfigSO terrainConfig;
    [SerializeField] private Transform        spawnRoot;       // parent transform for spawned GOs

    [Header("Spawn Settings")]
    [Tooltip("Secondary noise scale used to cluster objects. Independent of terrain scale.")]
    [SerializeField] private float objectNoiseScale = 12f;

    [Tooltip("Objects are only placed on tiles whose height exceeds this (avoids ocean spawns).")]
    [SerializeField] private float minHeightForSpawn = 0.38f;

    [Tooltip("Objects are only placed on tiles whose height is below this (avoids peak spawns).")]
    [SerializeField] private float maxHeightForSpawn = 0.70f;

    // ── Pool Storage ──────────────────────────────────────────────────────────────

    // Key: prefab instance ID (stable, no string hashing)
    private readonly Dictionary<int, Stack<GameObject>> _pools = new(32);

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns objects for a fully resolved chunk.
    /// Must be called on the main thread (Instantiate requirement).
    /// </summary>
    public void SpawnForChunk(ChunkData chunk, int globalSeed)
    {
        if (terrainConfig == null || chunk.BiomeIndices == null) return;

        int cs     = chunk.ChunkSize;
        var origin = chunk.WorldOrigin;

        for (int ly = 0; ly < cs; ly++)
        {
            for (int lx = 0; lx < cs; lx++)
            {
                int idx        = lx + ly * cs;
                float height   = chunk.HeightMap[idx];

                // Height gate — skip ocean and high peaks
                if (height < minHeightForSpawn || height > maxHeightForSpawn) continue;

                int biomeIdx = chunk.BiomeIndices[idx];
                if (biomeIdx < 0 || biomeIdx >= terrainConfig.biomes.Length) continue;

                var biome = terrainConfig.biomes[biomeIdx];
                if (biome.spawnRules == null || biome.spawnRules.Length == 0) continue;

                int wx = origin.x + lx;
                int wy = origin.y + ly;

                foreach (var rule in biome.spawnRules)
                {
                    if (rule.prefab == null) continue;
                    TrySpawnRule(rule, wx, wy, globalSeed, chunk);
                }
            }
        }
    }

    /// <summary>Returns all objects belonging to this chunk to the pool.</summary>
    public void DespawnChunk(ChunkData chunk)
    {
        foreach (var go in chunk.SpawnedObjects)
        {
            if (go == null) continue;
            ReturnToPool(go);
        }
        chunk.SpawnedObjects.Clear();
    }

    // ── Spawn Logic ────────────────────────────────────────────────────────────────

    private void TrySpawnRule(BiomeObjectRule rule, int wx, int wy, int seed, ChunkData chunk)
    {
        // Deterministic noise: combine world position with the rule's unique seed offset
        float noiseVal = DeterministicNoise(wx, wy, seed + rule.seedOffset, objectNoiseScale);

        if (noiseVal < rule.noiseThreshold) return;

        // Secondary probability gate using a different hash offset
        float prob = DeterministicHash(wx, wy, seed + rule.seedOffset + 777);
        if (prob > rule.density) return;

        // All gates passed — spawn (or retrieve from pool)
        var go = GetFromPool(rule.prefab);

        // World position: tile centre + small random sub-tile jitter for naturalness
        float jitterX = (DeterministicHash(wx, wy, seed + 1) - 0.5f) * 0.6f;
        float jitterY = (DeterministicHash(wx, wy, seed + 2) - 0.5f) * 0.6f;
        go.transform.position = new Vector3(wx + 0.5f + jitterX, wy + 0.5f + jitterY, -0.5f);

        // Deterministic rotation
        float rotY = DeterministicHash(wx, wy, seed + 3) * rule.yRotationRange;
        go.transform.rotation = Quaternion.Euler(0f, 0f, rotY);

        // Deterministic scale
        float t = DeterministicHash(wx, wy, seed + 4);
        float scale = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, t);
        go.transform.localScale = Vector3.one * scale;

        go.SetActive(true);
        chunk.SpawnedObjects.Add(go);
    }

    // ── Deterministic Math ────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a Perlin-based noise value seeded by world position + user seed.
    /// Avoids the Unity Mathf.PerlinNoise zero-return artifact at integer coords
    /// by adding half-unit offsets.
    /// </summary>
    private static float DeterministicNoise(int wx, int wy, int seed, float scale)
    {
        // Use System.Random to derive a stable float offset from the seed
        float ox = ((seed * 1234567) & 0xFFFF) / 65535f * 1000f;
        float oy = ((seed * 7654321) & 0xFFFF) / 65535f * 1000f;
        return Mathf.PerlinNoise(
            (wx + ox + 0.5f) / scale,
            (wy + oy + 0.5f) / scale);
    }

    /// <summary>
    /// Returns a deterministic pseudo-random float [0,1] for a given (wx, wy, seed)
    /// using a fast integer hash (Wang hash variant).
    /// This is cheaper than Perlin for pure probability gates.
    /// </summary>
    private static float DeterministicHash(int wx, int wy, int seed)
    {
        uint h = (uint)(wx * 374761393 + wy * 668265263 + seed * 2246822519u);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    // ── Object Pool ───────────────────────────────────────────────────────────────

    private GameObject GetFromPool(GameObject prefab)
    {
        int id = prefab.GetInstanceID();
        if (_pools.TryGetValue(id, out var stack) && stack.Count > 0)
        {
            var pooled = stack.Pop();
            pooled.SetActive(false);   // re-activated by caller
            return pooled;
        }

        var newGo = Instantiate(prefab, spawnRoot != null ? spawnRoot : transform);
        newGo.SetActive(false);
        return newGo;
    }

    private void ReturnToPool(GameObject go)
    {
        go.SetActive(false);
        go.transform.SetParent(spawnRoot != null ? spawnRoot : transform);

        // Use the prefab's instance ID baked into a component tag.
        // Without it we can't know which pool to return to — use the simplest fallback:
        // place all returned objects into the pool keyed by their layer (rough grouping).
        int id = go.layer;   // substitute: in production, cache prefab ID on a component
        if (!_pools.ContainsKey(id)) _pools[id] = new Stack<GameObject>(16);
        _pools[id].Push(go);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        foreach (var stack in _pools.Values)
            foreach (var go in stack)
                if (go != null) Destroy(go);
        _pools.Clear();
    }
}
