using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Central chunk streaming engine — the most performance-critical class.
///
/// RESPONSIBILITIES:
///   • Determine which chunks should be loaded (visible radius around camera).
///   • Schedule NoiseJobs for ungenerated chunks (one job per chunk, runs on workers).
///   • Poll completed jobs each frame; copy results to ChunkData; place tiles.
///   • Unload (clear tiles + destroy objects) for chunks beyond unload radius.
///   • Prevent duplicate jobs via the _pendingJobs dictionary.
///
/// FRAME BUDGET:
///   _maxTilePlacements limits how many chunk tile-sets are flushed per frame.
///   This prevents a multi-hundred-millisecond hitch when many chunks complete at once.
///
/// THREAD SAFETY:
///   NoiseJobs run on Unity worker threads. The main thread only touches NativeArrays
///   after JobHandle.IsCompleted is true — no locks needed.
///
/// CHUNK DICTIONARY:
///   Two dictionaries are maintained:
///     _activeChunks  — all loaded ChunkData objects (keyed by ChunkCoord)
///     _pendingJobs   — in-flight JobHandles + their NativeArray allocations
///
/// IMPORTANT: Attach this to the same GameObject as WorldManager.
/// </summary>
public class ChunkStreamer : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Chunk Settings")]
    [Tooltip("Width/height of each square chunk in tiles. Power-of-2 recommended.")]
    [SerializeField] public int chunkSize = 32;

    [Tooltip("Chebyshev (square) radius in chunks to keep loaded around the camera.")]
    [SerializeField] private int loadRadius = 4;

    [Tooltip("Chunks beyond this radius are unloaded. Should be > loadRadius.")]
    [SerializeField] private int unloadRadius = 6;

    [Header("Performance")]
    [Tooltip("Max chunks whose tiles are flushed to the Tilemap per frame. " +
             "Increase for faster streaming; decrease to reduce frame hitches.")]
    [SerializeField] private int maxTilePlacementsPerFrame = 2;

    [Tooltip("Max new chunk jobs submitted per frame (limits worker thread saturation).")]
    [SerializeField] private int maxJobsPerFrame = 4;

    [Header("References")]
    [SerializeField] private Tilemap tilemap;
    [SerializeField] private TerrainConfigSO terrainConfig;

    // ── Internal State ────────────────────────────────────────────────────────────

    private readonly Dictionary<ChunkCoord, ChunkData>   _activeChunks = new(256);
    private readonly Dictionary<ChunkCoord, PendingJob>  _pendingJobs  = new(64);

    private GenerationSettings _settings;
    private BiomeResolver      _resolver;
    private Tile[]             _tileCache;
    private Tile[]             _heatmapCache;
    private bool               _isHeatmapMode = false;
    
    // World bounds in chunk-space, computed from settings.width / settings.height
    private int _maxChunkX;
    private int _maxChunkY;
    private bool _infiniteWorld;

    // Scratch list for unload pass (avoids alloc each frame)
    private readonly List<ChunkCoord> _toUnload = new(32);

    /// <summary>Reused <see cref="Tilemap.SetTilesBlock"/> buffer — one chunk worth, no per-flush alloc.</summary>
    private TileBase[] _scratchTilesBlock;

    /// <summary>Reused weight vector for <see cref="BiomeResolver.ResolveDominantIndex"/> — no per-tile alloc.</summary>
    private float[] _scratchBiomeWeights;

    // ── Pending Job Bookkeeping ────────────────────────────────────────────────────

    private struct PendingJob
    {
        public JobHandle             Handle;
        public NativeArray<float>    HeightOut;
        public NativeArray<float>    MoistureOut;
        public NativeArray<float>    TempOut;
        public NativeArray<float2>   ElevOffsets;
        public NativeArray<float2>   MoistOffsets;
        public NativeArray<float2>   TempOffsets;
    }

    // ── Public API ───────────────────────────────────────────────────────────────

    /// <summary>Called by WorldManager.ApplySettingsAndGenerate() to reset the streamer.</summary>
    public void Initialize(GenerationSettings settings, TerrainConfigSO config)
    {
        CancelAllPendingJobs();
        ClearAllChunks();

        _settings    = settings;
        terrainConfig = config;
        _resolver    = config.GetResolver();
        _tileCache   = BuildTileCache(config);
        _heatmapCache = BuildHeatmapCache(); 
        _infiniteWorld = settings.infiniteWorld;
        
        var overlay = FindAnyObjectByType<DebugOverlay>();
        if (overlay != null)
        {
            overlay.OnShowNoiseChanged -= OnHeatmapToggled;
            overlay.OnShowNoiseChanged += OnHeatmapToggled;
            _isHeatmapMode = overlay.ShowNoise;
        }

        // Compute world bounds in chunk-space from tile dimensions
        // Chunks whose origin falls within [0, width) x [0, height) are in-bounds.
        _maxChunkX = Mathf.CeilToInt((float)settings.width  / chunkSize) - 1;
        _maxChunkY = Mathf.CeilToInt((float)settings.height / chunkSize) - 1;
        EnsureFlushScratchBuffers();

        Debug.Log($"[ChunkStreamer] Initialized — chunkSize={chunkSize}, " +
                  $"infinite={_infiniteWorld}, worldBounds={settings.width}x{settings.height} tiles " +
                  $"({_maxChunkX + 1}x{_maxChunkY + 1} chunks), " +
                  $"loadRadius={loadRadius}, unloadRadius={unloadRadius}");
    }

    // ── Unity Lifecycle ───────────────────────────────────────────────────────────

    private void Update()
    {
        if (_settings == null || _resolver == null) return;

        ChunkCoord cameraChunk = GetCameraChunk();

        ScheduleNewChunks(cameraChunk);
        PollCompletedJobs();
        FlushPendingChunks();
        UnloadDistantChunks(cameraChunk);
    }

    private void OnDestroy() 
    {
        CancelAllPendingJobs();
        var overlay = FindAnyObjectByType<DebugOverlay>();
        if (overlay != null)
        {
            overlay.OnShowNoiseChanged -= OnHeatmapToggled;
        }
    }
    
    private void OnHeatmapToggled(bool isHeatmap)
    {
        if (_isHeatmapMode == isHeatmap) return;
        _isHeatmapMode = isHeatmap;
        
        foreach (var data in _activeChunks.Values)
        {
            if (data.State == ChunkData.ChunkState.Ready || data.State == ChunkData.ChunkState.Modified)
            {
                ForceRefreshChunkVisuals(data);
            }
        }
    }

    // ── Step 1: Determine and schedule new chunks ─────────────────────────────────

    private void ScheduleNewChunks(ChunkCoord centre)
    {
        int jobsThisFrame = 0;

        for (int dy = -loadRadius; dy <= loadRadius; dy++)
        {
            for (int dx = -loadRadius; dx <= loadRadius; dx++)
            {
                var coord = new ChunkCoord(centre.X + dx, centre.Y + dy);
                
                // Skip chunks outside the world bounds defined by settings.width / height
                // (only enforced in finite mode)
                if (!_infiniteWorld &&
                    (coord.X < 0 || coord.X > _maxChunkX || coord.Y < 0 || coord.Y > _maxChunkY))
                    continue;

                // Skip if already loaded or already being computed
                if (_activeChunks.ContainsKey(coord) || _pendingJobs.ContainsKey(coord))
                    continue;

                ScheduleJob(coord);
                jobsThisFrame++;
                if (jobsThisFrame >= maxJobsPerFrame) return;
            }
        }
    }

        private void ScheduleJob(ChunkCoord coord)
    {
        var data = new ChunkData(coord, chunkSize);
        
        _activeChunks[coord] = data;

        JobHandle handle;
        NativeArray<float> heightOut, moistureOut, tempOut;
        NativeArray<float2> elevOff, moistOff, tmpOff;

        using (GenerationProfilerMarkers.NoiseJobSchedule.Auto())
        {
            handle = NoiseJobScheduler.Schedule(
                coord, chunkSize,
                _settings.seed, _settings.scale,
                _settings.octaves, _settings.persistence, _settings.lacunarity,
                _settings.noiseBackend,
                out heightOut, out moistureOut, out tempOut,
                out elevOff, out moistOff, out tmpOff);
        }

        _pendingJobs[coord] = new PendingJob
        {
            Handle       = handle,
            HeightOut    = heightOut,
            MoistureOut  = moistureOut,
            TempOut      = tempOut,
            ElevOffsets  = elevOff,
            MoistOffsets = moistOff,
            TempOffsets  = tmpOff,
        };
    }

    // ── Step 2: Poll and flush completed jobs ──────────────────────────────────────

    private void PollCompletedJobs()
    {
        var completed = new List<ChunkCoord>(8);

        foreach (var kvp in _pendingJobs)
        {
            if (!kvp.Value.Handle.IsCompleted) continue;
            completed.Add(kvp.Key);
        }

        foreach (var coord in completed)
        {
            var job = _pendingJobs[coord];

            using (GenerationProfilerMarkers.NoiseJobWaitComplete.Auto())
                job.Handle.Complete(); // must call Complete even on finished jobs

            // Copy from NativeArrays → managed arrays in ChunkData
            if (_activeChunks.TryGetValue(coord, out ChunkData data))
            {
                using (GenerationProfilerMarkers.NoiseJobCopyNativeToManaged.Auto())
                {
                    job.HeightOut.CopyTo(data.HeightMap);
                    job.MoistureOut.CopyTo(data.MoistureMap);
                    job.TempOut.CopyTo(data.TemperatureMap);
                }

                data.HeightMapReady = true;
            }

            DisposeJobArrays(job);

            _pendingJobs.Remove(coord);
        }
    }

    // ── Step 2b: Flush data-ready chunks to the Tilemap (rate-limited) ────────────
    private void FlushPendingChunks()
    {
        int flushedThisFrame = 0;
        foreach (var kvp in _activeChunks)
        {
            if (flushedThisFrame >= maxTilePlacementsPerFrame) break;
            ChunkData data = kvp.Value;
            if (data.State == ChunkData.ChunkState.Pending && data.IsDataReady)
            {
                FlushChunkToTilemap(data);
                flushedThisFrame++;
            }
        }
    }

    private void EnsureFlushScratchBuffers()
    {
        if (_resolver == null) return;

        int n = chunkSize * chunkSize;
        if (_scratchTilesBlock == null || _scratchTilesBlock.Length != n)
            _scratchTilesBlock = new TileBase[n];

        int bc = _resolver.BiomeCount;
        if (_scratchBiomeWeights == null || _scratchBiomeWeights.Length < bc)
            _scratchBiomeWeights = new float[bc];
    }

    // ── Step 3: Place tiles on the shared Tilemap ────────────────────────────────

    private void FlushChunkToTilemap(ChunkData data)
    {
        if (!data.IsDataReady) return;

        using (GenerationProfilerMarkers.ChunkFlushTilemap.Auto())
        {
            EnsureFlushScratchBuffers();

            int    cs         = data.ChunkSize;
            int    tileCount  = cs * cs;
            var    tiles      = _scratchTilesBlock;
            var    origin     = data.WorldOrigin;
            var    bounds     = new BoundsInt(origin.x, origin.y, 0, cs, cs, 1);
            var    biomeScratch = _scratchBiomeWeights;

            for (int ly = 0; ly < cs; ly++)
            {
                for (int lx = 0; lx < cs; lx++)
                {
                    int idx = lx + ly * cs;

                    float h = data.HeightMap[idx];
                    
                    if (_isHeatmapMode)
                    {
                        // Map height [0, 1] to a grayscale tile index (0 to 255)
                        int heatIndex = Mathf.Clamp(Mathf.FloorToInt(h * 255f), 0, 255);
                        tiles[idx] = _heatmapCache[heatIndex];
                        
                        // Still resolve biome index under the hood in case it's needed
                        float m = data.MoistureMap[idx];
                        float t = data.TemperatureMap[idx];
                        data.BiomeIndices[idx] = _resolver.ResolveDominantIndex(h, m, t, biomeScratch);
                    }
                    else
                    {
                        // Apply override if this tile was modified by the player
                        if (data.TileOverrides.TryGetValue(new Vector2Int(lx, ly), out int overrideIdx))
                        {
                            tiles[idx]  = _tileCache[overrideIdx];
                            data.BiomeIndices[idx] = overrideIdx;
                            continue;
                        }

                        float m = data.MoistureMap[idx];
                        float t = data.TemperatureMap[idx];

                        int dom = _resolver.ResolveDominantIndex(h, m, t, biomeScratch);
                        data.BiomeIndices[idx] = dom;
                        tiles[idx]   = _tileCache[dom];
                    }
                }
            }

            using (GenerationProfilerMarkers.ChunkFlushSetTilesBlock.Auto())
                tilemap.SetTilesBlock(bounds, tiles);
            
            if (data.State == ChunkData.ChunkState.Pending)
            {
                data.MarkReady();
            }
        }
    }
    
    private void ForceRefreshChunkVisuals(ChunkData data)
    {
        FlushChunkToTilemap(data);
    }


    // ── Step 4: Unload distant chunks ─────────────────────────────────────────────

    private void UnloadDistantChunks(ChunkCoord centre)
    {
        _toUnload.Clear();

        foreach (var kvp in _activeChunks)
        {
            if (kvp.Key.ChebyshevDistanceTo(centre) > unloadRadius)
                _toUnload.Add(kvp.Key);
        }

        foreach (var coord in _toUnload)
        {
            // If job is still running, complete it first (avoid NativeArray leak)
            if (_pendingJobs.TryGetValue(coord, out PendingJob job))
            {
                using (GenerationProfilerMarkers.NoiseJobWaitComplete.Auto())
                    job.Handle.Complete();
                DisposeJobArrays(job);
                _pendingJobs.Remove(coord);
            }

            // Clear tiles from the shared Tilemap
            var data   = _activeChunks[coord];
            var origin = data.WorldOrigin;
            var bounds = new BoundsInt(origin.x, origin.y, 0, chunkSize, chunkSize, 1);

            tilemap.SetTilesBlock(bounds, new TileBase[chunkSize * chunkSize]);   // clear

            data.Dispose();
            _activeChunks.Remove(coord);
        }
    }

    // ── Tile Cache ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds one solid-colour Tile per biome from the blended primary colour.
    /// Tiles are runtime ScriptableObjects — not serialized to disk.
    /// </summary>
    private static Tile[] BuildTileCache(TerrainConfigSO config)
    {
        const int TexPx = 16;
        var tiles = new Tile[config.biomes.Length];

        for (int i = 0; i < config.biomes.Length; i++)
        {
            Color fill   = config.biomes[i].primaryColor;
            Color border = new Color(fill.r * 0.72f, fill.g * 0.72f, fill.b * 0.72f, 1f);

            tiles[i] = CreateSolidTile(fill, border, TexPx);
        }

        config.TileCache = tiles;
        return tiles;
    }
    
    private static Tile[] BuildHeatmapCache()
    {
        const int TexPx = 16;
        var tiles = new Tile[256];

        for (int i = 0; i < 256; i++)
        {
            float v = i / 255f;
            Color color = new Color(v, v, v, 1f); // Grayscale based on height
            tiles[i] = CreateSolidTile(color, color, TexPx);
        }

        return tiles;
    }

    private static Tile CreateSolidTile(Color fill, Color border, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp
        };

        Color[] px = new Color[size * size];
        for (int py = 0; py < size; py++)
            for (int px2 = 0; px2 < size; px2++)
                px[px2 + py * size] =
                    (px2 == 0 || py == 0 || px2 == size - 1 || py == size - 1)
                        ? border : fill;

        tex.SetPixels(px);
        tex.Apply(false, false);

        var sprite = Sprite.Create(tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), size);

        var tile   = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.color  = Color.white;
        return tile;
    }

    // ── Utility ────────────────────────────────────────────────────────────────────

    private ChunkCoord GetCameraChunk()
    {
        var cam = Camera.main;
        if (cam == null) return default;
        return ChunkCoord.FromWorldPos(cam.transform.position, chunkSize);
    }

    private void ClearAllChunks()
    {
        foreach (var data in _activeChunks.Values)
            data.Dispose();
        _activeChunks.Clear();
        tilemap.ClearAllTiles();
    }

    private void CancelAllPendingJobs()
    {
        foreach (var job in _pendingJobs.Values)
        {
            using (GenerationProfilerMarkers.NoiseJobWaitComplete.Auto())
                job.Handle.Complete();
            DisposeJobArrays(job);
        }
        _pendingJobs.Clear();
    }

    private static void DisposeJobArrays(PendingJob job)
    {
        using (GenerationProfilerMarkers.NoiseJobNativeDispose.Auto())
        {
            if (job.HeightOut.IsCreated)    job.HeightOut.Dispose();
            if (job.MoistureOut.IsCreated)  job.MoistureOut.Dispose();
            if (job.TempOut.IsCreated)      job.TempOut.Dispose();
            if (job.ElevOffsets.IsCreated)  job.ElevOffsets.Dispose();
            if (job.MoistOffsets.IsCreated) job.MoistOffsets.Dispose();
            if (job.TempOffsets.IsCreated)  job.TempOffsets.Dispose();
        }
    }

    // ── Public Accessors (for Editor Tools & Debug) ────────────────────────────────

    public IReadOnlyDictionary<ChunkCoord, ChunkData> ActiveChunks => _activeChunks;
    public int PendingJobCount => _pendingJobs.Count;
    public int ActiveChunkCount => _activeChunks.Count;
}
