using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Runtime data container for one 32×32 (or configurable) chunk.
///
/// LIFECYCLE:
///   Pending   → job is queued/running (heightmap not yet ready)
///   Ready     → heightmap computed, tiles placed on Tilemap
///   Modified  → player changed tiles — must be serialized before unload
///   Unloading → scheduled for removal (one-frame grace for in-flight jobs)
///
/// The chunk does NOT own a separate Tilemap — all chunks share the single
/// scene Tilemap for draw-call efficiency. Tile positions are offset by the
/// chunk's world origin so they land in the correct screen location.
///
/// Spawned scene objects (trees, rocks) ARE tracked per-chunk so they can be
/// destroyed on unload and respawned correctly on reload.
/// </summary>
public class ChunkData
{
    // ── Identity ────────────────────────────────────────────────────────────────

    public readonly ChunkCoord Coord;
    public readonly int        ChunkSize;

    // ── State Machine ───────────────────────────────────────────────────────────

    public enum ChunkState { Pending, Ready, Modified, Unloading }
    public ChunkState State { get; private set; } = ChunkState.Pending;

    // Thread-safe flag: the Burst/Job noise result is ready for main-thread pickup
    public volatile bool HeightMapReady;

    // ── Terrain Data ─────────────────────────────────────────────────────────────

    /// <summary>Normalized [0,1] elevation map — set by the noise job.</summary>
    public float[] HeightMap;       // flat [x + y*ChunkSize] layout for NativeArray compat

    /// <summary>Normalized [0,1] moisture — second noise channel for biome blending.</summary>
    public float[] MoistureMap;

    /// <summary>Normalized [0,1] temperature — third noise channel.</summary>
    public float[] TemperatureMap;

    /// <summary>Resolved biome index per tile — set by BiomeResolver after jobs complete.</summary>
    public int[] BiomeIndices;

    // ── Override Layer ────────────────────────────────────────────────────────────
    // Sparse dictionary of tiles that were modified after initial generation.
    // Only these need to be saved (delta encoding — saves huge amounts of disk space).

    public Dictionary<Vector2Int, int> TileOverrides;   // tile-local pos → biome index

    // ── Spawned Objects ───────────────────────────────────────────────────────────

    /// <summary>All GameObjects spawned into this chunk (trees, rocks, etc.).</summary>
    public List<GameObject> SpawnedObjects;

    // ── Timestamps ────────────────────────────────────────────────────────────────

    public float LastAccessTime { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────────

    public ChunkData(ChunkCoord coord, int chunkSize)
    {
        Coord         = coord;
        ChunkSize     = chunkSize;
        SpawnedObjects = new List<GameObject>();
        TileOverrides  = new Dictionary<Vector2Int, int>();
        Touch();
    }

    // ── State Transitions ──────────────────────────────────────────────────────

    public void MarkReady()     { State = ChunkState.Ready;     Touch(); }
    public void MarkModified()  { State = ChunkState.Modified;  Touch(); }
    public void MarkUnloading() { State = ChunkState.Unloading;            }

    public void Touch() => LastAccessTime = Time.realtimeSinceStartup;

    // ── Accessors ─────────────────────────────────────────────────────────────

    /// <summary>Read height at tile-local coordinates (0…ChunkSize-1).</summary>
    public float GetHeight(int lx, int ly) =>
        HeightMap != null ? HeightMap[lx + ly * ChunkSize] : 0f;

    public float GetMoisture(int lx, int ly) =>
        MoistureMap != null ? MoistureMap[lx + ly * ChunkSize] : 0f;

    public float GetTemperature(int lx, int ly) =>
        TemperatureMap != null ? TemperatureMap[lx + ly * ChunkSize] : 0f;

    /// <summary>World-tile origin of this chunk.</summary>
    public Vector2Int WorldOrigin => Coord.WorldOrigin(ChunkSize);

    /// <summary>True only when all three noise maps have been filled by the job.</summary>
    public bool IsDataReady => HeightMap != null && MoistureMap != null && TemperatureMap != null;

    /// <summary>Has any player modification been applied since last save?</summary>
    public bool IsDirty => State == ChunkState.Modified && TileOverrides.Count > 0;

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Release managed arrays so GC can collect them.
    /// Called just before the chunk is evicted from the active dictionary.
    /// </summary>
    public void Dispose()
    {
        HeightMap      = null;
        MoistureMap    = null;
        TemperatureMap = null;
        BiomeIndices   = null;

        foreach (var go in SpawnedObjects)
            if (go != null) UnityEngine.Object.Destroy(go);

        SpawnedObjects.Clear();
    }
}
