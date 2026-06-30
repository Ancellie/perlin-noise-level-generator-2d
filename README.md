# Perlin Noise Level Generator 2D

A Unity 2D procedural world generator that builds terrain from fractional Brownian motion (fBM) noise, resolves multi-channel biomes, and streams the result in camera-centered chunks with optional infinite-world mode.

---

# 📋 Overview

This project is an interactive **2D procedural terrain sandbox** built in Unity. It generates tile-based worlds from seeded noise, assigns biomes from elevation, moisture, and temperature maps, and renders them through a shared `Tilemap` while chunks load and unload around the camera.

**Purpose:** Demonstrate and profile a production-style procedural generation pipeline—Burst-compiled parallel jobs, chunk streaming, biome blending, delta save/load, and comparative noise-backend benchmarking—suitable for research, prototyping, and level-generation experiments.

**Problems it addresses:**

- Generating large or unbounded 2D terrain without pre-baking entire maps into memory
- Comparing Simplex vs. classic Perlin fBM performance under the same scheduler
- Tuning noise and world parameters at runtime and persisting player modifications efficiently

**Main interaction model:** Pan and zoom an orthographic camera across a procedurally generated world. Use the UI to change seed, noise settings, world size, and infinite mode; regenerate, save, load, or clear the world.

**Target audience:** Game developers, procedural-generation researchers, and students exploring noise-based 2D worlds, Unity Jobs/Burst, and chunk streaming—not a full action/adventure game with combat or progression.

---

# ✨ Features

- **Procedural terrain generation** via 2D fractional Brownian motion (fBM)
- **Dual noise backends:** Simplex (`noise.snoise`) and classic Perlin (`noise.cnoise`), selectable at runtime
- **Three independent noise channels:** elevation, moisture, and temperature (separate PRNG offset seeds per channel)
- **12 configurable biomes** with elevation overrides (Ocean, Mountain, Snow) and climate-based blending (Beach, Desert, Savanna, Grassland, TropicalForest, TemperateForest, BorealForest, Tundra)
- **Gaussian biome weighting** from ideal temperature/moisture plus smooth elevation overrides
- **Chunk-based world streaming** with Chebyshev-radius load/unload around the camera
- **Finite or infinite world modes** (bounded by tile width/height, or unbounded chunk generation)
- **Burst-compiled `IJobParallelFor` noise jobs** scheduled per chunk on worker threads
- **Shared Tilemap rendering** with runtime solid-color tiles (one tile per biome, plus 256-step height heatmap tiles)
- **Frame-budgeted streaming:** configurable max jobs and tile flushes per frame to reduce hitches
- **Runtime UI** for seed, scale, octaves, persistence, lacunarity, width/height, infinite toggle, and noise backend
- **Debounced auto-regeneration** (0.4 s delay) when sliders change
- **World save/load** with delta encoding (only modified chunks persisted; manifest stores generation settings)
- **Auto-load on startup** when a save manifest exists
- **Orthographic camera pan and zoom** with speed scaled to zoom level
- **Debug overlay:** chunk borders, chunk coordinate labels, elevation heatmap mode, streaming HUD, Unity Profiler marker readouts
- **Custom Terrain Config editor** with in-Inspector noise/biome preview (no Play mode required for preview texture)
- **Noise benchmark tooling:** editor window (`Tools → Procedural World → Noise Benchmark`) and headless harness exporting CSV results
- **Unity Profiler integration** via `PW.*` custom markers for job schedule, completion, copy, tile flush, and biome resolve
- **ScriptableObject-driven biome configuration** (`TerrainConfigSO`) with validation warnings in the editor

**Not implemented as active gameplay systems:** player character, combat, enemy AI, inventory, quests, day/night cycle, or tile editing UI (save infrastructure for tile overrides exists in code but is not fully wired into the chunk flush path).

---

# 🎮 Gameplay

There is no controllable player avatar. The experience is **exploration and experimentation** with procedural terrain.

**Player goals (implicit):**

- Explore generated terrain by moving the camera
- Tune noise and world parameters and observe biome/layout changes
- Save, reload, or delete world state for iteration
- Use debug overlays and benchmarks to analyze generation performance

**Mechanics:**

1. On start, `WorldManager` auto-loads a saved world if one exists; otherwise it generates a fresh world from default settings.
2. `ChunkStreamer` continuously loads chunks within `loadRadius` of the camera and unloads chunks beyond `unloadRadius`.
3. Each chunk runs a Burst noise job, resolves a dominant biome per tile, and writes tiles to the scene `Tilemap`.
4. The UI reflects streaming stats (active chunks, pending jobs) and generation status.

**Progression:** Not specified in the project. No levels, scoring, or narrative progression.

---

# 🌍 Procedural Generation

## Generation pipeline

```
UI / WorldManager
       │
       ▼
GenerationSettings + TerrainConfigSO
       │
       ▼
ChunkStreamer.Initialize()
       │
       ▼
Each frame (Update):
  1. Get camera chunk coordinate
  2. ScheduleNewChunks  → NoiseJobScheduler.Schedule() per missing in-range chunk
  3. PollCompletedJobs  → Copy NativeArray results → ChunkData managed arrays
  4. FlushPendingChunks → BiomeResolver → Tilemap.SetTilesBlock() (rate-limited)
  5. UnloadDistantChunks → clear tiles, dispose ChunkData, cancel pending jobs
```

## Noise functions

All terrain channels use the same fBM stack in `TerrainFbm.SampleNormalized`:

| Parameter | Default (SampleScene) | Range (GenerationSettings) |
|-----------|----------------------|----------------------------|
| Scale | 40 | 1–200 |
| Octaves | 4 | 1–8 |
| Persistence | 0.5 | 0.1–1 |
| Lacunarity | 2 | 1–4 |
| Seed | 42 | integer |

**Sampling formula (per octave):**

- Sample coordinates: `(wx + offset.x) / scale * frequency`, same for Y
- Noise primitive: `noise.snoise` (Simplex) or `noise.cnoise` (classic Perlin)
- Accumulate with amplitude × persistence and frequency × lacunarity
- Normalize: `saturate((value / maxValue) * 0.5 + 0.5)` → **[0, 1]**

**Channel independence:** Elevation uses seed `S`; moisture uses `S + 31337`; temperature uses `S + 99991`. Octave offsets are precomputed with `System.Random` on the main thread before job scheduling.

**Legacy path:** `NoiseGenerator.cs` contains a commented-out synchronous `Mathf.PerlinNoise` height-map generator; runtime generation uses `NoiseJob` + `TerrainFbm` exclusively.

## Biome resolution

`BiomeResolver` combines:

1. **Elevation overrides** — smooth weights for Ocean (below `oceanMaxHeight`) and Mountain (above `mountainMinHeight`)
2. **Climate weights** — Gaussian falloff from each biome’s ideal temperature/moisture: `exp(-dist² / (2 × influence²))`
3. **Dominant biome** — highest weight wins; used for tile color selection

Default biomes and thresholds are defined in `TerrainConfigSO` (also serialized in `Assets/ScriptableObjects/TerrainConfig.asset`).

## Chunk system

| Setting | Default | Description |
|---------|---------|-------------|
| `chunkSize` | 32 | Square chunk in tiles (power-of-2 recommended) |
| `loadRadius` | 4 | Chebyshev radius (square) of chunks kept loaded |
| `unloadRadius` | 6 | Chunks farther than this are removed |
| `maxJobsPerFrame` | 4 | Cap on new noise jobs scheduled per frame |
| `maxTilePlacementsPerFrame` | 2 | Cap on chunks flushed to the Tilemap per frame |

**Chunk lifecycle (`ChunkData`):** `Pending` → (job completes) → `Ready` → optional `Modified` → `Unloading`

**World bounds:** In finite mode, chunks outside `[0, width) × [0, height)` tile space are skipped. In infinite mode, chunks generate in all directions with no upper bound.

**Coordinate mapping:** `ChunkCoord.FromWorldPos(cameraPosition, chunkSize)` drives streaming; world tile origin = `(chunkX × chunkSize, chunkY × chunkSize)`.

## Randomization

- World seed drives all fBM octave offsets and deterministic regeneration
- **Random Seed** UI button sets `seed = Random.Range(0, int.MaxValue)` and regenerates
- Unmodified chunks are fully reproducible from seed + settings (no chunk file needed on disk)

## Optimization techniques

- **Burst compilation** on `NoiseJob` and `TerrainFbm` (`FloatMode.Fast`)
- **Parallel for** with batch size = `chunkSize` (one row per batch for cache locality)
- **NativeArray TempJob** allocations disposed after job completion
- **No duplicate jobs** — `_pendingJobs` dictionary prevents double-scheduling
- **Rate-limited tile placement** to avoid multi-frame hitches when many chunks finish together
- **Reused scratch buffers** — `_scratchTilesBlock`, `_scratchBiomeWeights` (no per-tile allocation during flush)
- **`SetTilesBlock`** bulk tile writes instead of per-tile `SetTile`
- **Single shared Tilemap** for all chunks (draw-call efficiency)
- **Allocation-free biome resolve hot path** via `ResolveDominantIndex` + scratch weight array
- **Profiler markers** (`PW.*`) for measurable pipeline stages

## World loading

1. Camera position → chunk coordinate each frame
2. Missing in-range chunks get `ChunkData` + scheduled `NoiseJob`
3. Completed jobs copy height/moisture/temperature maps to managed arrays
4. Flush pass resolves biomes and writes tiles; optional heatmap mode maps height to grayscale tiles

## World unloading

1. Chunks with Chebyshev distance > `unloadRadius` are collected
2. Pending jobs for those chunks are completed and NativeArrays disposed
3. Tilemap region cleared via empty `SetTilesBlock`
4. `ChunkData.Dispose()` releases arrays and destroys any tracked spawned objects

## Save/load (delta encoding)

- **Manifest:** `{persistentDataPath}/Worlds/{worldName}/manifest.json` — seed, noise settings, dimensions, list of modified chunk keys
- **Chunk files:** `chunks/{x}_{y}.chunk` — sparse tile override entries only
- Unmodified chunks regenerate from seed on load; only overrides need disk storage
- `WorldSaveSystem.LoadChunkOverrides()` exists for applying overrides during streaming, but **is not currently called from `ChunkStreamer`** — manifest settings restore correctly; per-chunk override replay may require wiring that call into the flush path

---

# 🏗 Architecture

## High-level design

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   UIController  │────▶│   WorldManager   │────▶│  ChunkStreamer  │
└─────────────────┘     └────────┬─────────┘     └────────┬────────┘
                                   │                        │
                          ┌────────▼────────┐      ┌────────▼────────┐
                          │ WorldSaveSystem │      │    NoiseJob     │
                          └─────────────────┘      │   TerrainFbm    │
                                                   │  BiomeResolver  │
                                                   └────────┬────────┘
                                                            │
                                                   ┌────────▼────────┐
                                                   │  Unity Tilemap  │
                                                   └─────────────────┘

┌─────────────────┐     ┌──────────────────┐
│ CameraController│     │   DebugOverlay   │  (reads ChunkStreamer / WorldManager)
└─────────────────┘     └──────────────────┘
```

## Main systems

| System | Responsibility |
|--------|----------------|
| `WorldManager` | Singleton orchestrator: settings, generate/clear/save/load, events |
| `ChunkStreamer` | Chunk streaming, job scheduling, tile flush, unload |
| `NoiseJob` / `NoiseJobScheduler` | Burst parallel noise for three maps per chunk |
| `TerrainFbm` | Shared fBM math for jobs and editor preview |
| `BiomeResolver` | Height + climate → dominant biome index |
| `TerrainConfigSO` | Biome definitions, resolver cache, runtime tile cache |
| `ChunkData` | Per-chunk maps, state, tile overrides, spawned object list |
| `WorldSaveSystem` | JSON manifest + per-chunk override files |
| `UIController` | Sliders, buttons, status, streaming stats |
| `CameraController` | Orthographic pan/zoom |
| `DebugOverlay` | GL chunk grid, OnGUI HUD, heatmap toggle |
| `ObjectSpawner` | Deterministic prefab placement with pooling (**not invoked by `ChunkStreamer` in current code**) |
| `NoiseBenchmarkHarness` / `NoiseBenchmarkWindow` | Headless timing + CSV export |
| `TerrainConfigEditor` | Inspector preview and validation |

## Code organization

All gameplay scripts live under `Assets/Scripts/` grouped by concern. Editor-only code is in `Assets/Editor/`. There are no Assembly Definition files; everything compiles into the default `Assembly-CSharp` assembly.

## Component interactions

- `UIController` builds `GenerationSettings` and calls `WorldManager.ApplySettingsAndGenerate()`
- `WorldManager.GenerateWorld()` validates settings and calls `ChunkStreamer.Initialize()`
- `ChunkStreamer` owns active chunk dictionaries and talks to the scene `Tilemap` directly
- `DebugOverlay` subscribes to heatmap toggles and reads `ChunkStreamer.ActiveChunks`
- `WorldManager` references `ObjectSpawner` in the Inspector but does not call it in the current implementation

---

# 🛠 Technologies

| Technology | Purpose |
|------------|---------|
| Unity 6000.3.11f1 | Game engine (2D Tilemap, orthographic camera, UI) |
| C# | Application logic |
| Unity Jobs (`com.unity.jobs`) | Multithreaded chunk noise generation |
| Burst (`com.unity.burst`) | Native-speed compiled noise jobs |
| Unity Mathematics (`com.unity.mathematics`) | `float2`, `noise.snoise`, `noise.cnoise` |
| Unity Collections (`com.unity.collections`) | `NativeArray`, job-safe memory |
| Unity Tilemap module | Chunk tile rendering |
| Unity UGUI + TextMesh Pro | Runtime parameter UI |
| Unity Input (legacy) | Camera pan/zoom via `Input.GetAxis` / `Input.GetKeyDown` |
| Unity Input System package | Present (`InputSystem_Actions.inputactions`); **not used by project scripts** |
| Unity Profiler / `ProfilerRecorder` | Custom `PW.*` markers and debug HUD timings |
| JsonUtility | Save manifest and chunk override serialization |
| Unity 2D Feature package | 2D project tooling |

---

# 📁 Project Structure

```
perlin-noise-level-generator-2d/
│
├── Assets/
│   ├── BenchmarkResults/          # Exported noise benchmark CSV files
│   ├── Editor/
│   │   ├── NoiseBenchmarkWindow.cs    # Tools menu benchmark UI
│   │   └── TerrainConfigEditor.cs     # Custom TerrainConfigSO inspector
│   ├── InputSystem_Actions.inputactions  # Default Input System asset (unused by scripts)
│   ├── Scenes/
│   │   └── SampleScene.unity          # Main playable scene (in build settings)
│   ├── ScriptableObjects/
│   │   └── TerrainConfig.asset        # Biome definitions instance
│   ├── Scripts/
│   │   ├── Benchmark/
│   │   │   └── NoiseBenchmarkHarness.cs
│   │   ├── Biomes/
│   │   │   └── BiomeResolver.cs       # BiomeDefinition, BiomeObjectRule, resolver
│   │   ├── Camera/
│   │   │   └── CameraController.cs
│   │   ├── Chunks/
│   │   │   ├── ChunkCoord.cs
│   │   │   ├── ChunkData.cs
│   │   │   └── ChunkStreamer.cs
│   │   ├── Core/
│   │   │   ├── GenerationSettings.cs
│   │   │   ├── NoiseBackend.cs
│   │   │   ├── NoiseGenerator.cs      # Legacy code (fully commented out)
│   │   │   └── TerrainFbm.cs
│   │   ├── Debug/
│   │   │   ├── DebugOverlay.cs
│   │   │   └── GenerationProfilerMarkers.cs
│   │   ├── Generation/
│   │   │   ├── TerrainConfigSO.cs
│   │   │   └── WorldManager.cs
│   │   ├── Jobs/
│   │   │   └── NoiseJob.cs
│   │   ├── Objects/
│   │   │   └── ObjectSpawner.cs
│   │   ├── SaveLoad/
│   │   │   └── WorldSaveSystem.cs
│   │   └── UI/
│   │       └── UIController.cs
│   └── TextMesh Pro/              # TMP fonts, shaders, resources (Unity package content)
│
├── Packages/
│   └── manifest.json              # Unity package dependencies
│
├── ProjectSettings/               # Unity project configuration
│
└── README.md
```

### Folder notes

| Path | Role |
|------|------|
| `Assets/Scripts/` | All runtime C# systems |
| `Assets/Editor/` | Editor-only benchmark and config tools |
| `Assets/Scenes/` | Scene with WorldManager, ChunkStreamer, Tilemap, UI, camera |
| `Assets/ScriptableObjects/` | Authoring data for biomes |
| `Assets/BenchmarkResults/` | Sample CSV output from noise benchmarks |
| `Packages/` | UPM dependency manifest |
| `ProjectSettings/` | Unity version, build scenes, quality, input settings |

---

# 💿 Installation

### Prerequisites

- **Unity Hub** with **Unity 6000.3.11f1** (version recorded in `ProjectSettings/ProjectVersion.txt`)

### Clone

```bash
git clone <repository-url>
cd perlin-noise-level-generator-2d
```

### Open

1. In Unity Hub, click **Add** and select the project folder.
2. Open the project with Unity **6000.3.11f1** (or allow Hub to install the matching editor version).

### Run

1. Open `Assets/Scenes/SampleScene.unity`.
2. Press **Play**.
3. Pan with movement keys and zoom with the mouse scroll wheel; use the on-screen UI to change generation parameters.

### Build

1. **File → Build Settings**
2. Confirm `Assets/Scenes/SampleScene.unity` is in **Scenes In Build** (enabled by default).
3. Select a target platform and click **Build** or **Build And Run**.

Build output path and platform-specific settings: not specified in the project beyond default Unity build settings.

---

# 🕹 Controls

### Camera

| Input | Action |
|-------|--------|
| **A** / **Left Arrow** | Pan camera left |
| **D** / **Right Arrow** | Pan camera right |
| **W** / **Up Arrow** | Pan camera up |
| **S** / **Down Arrow** | Pan camera down |
| **Mouse Scroll Wheel** | Zoom in/out (orthographic size clamped 2–80) |

Pan speed scales with current zoom level (`panSpeed × orthographicSize / 10`).

### Debug overlay (Main Camera / `DebugOverlay`)

| Key | Action |
|-----|--------|
| **C** | Toggle chunk border grid |
| **B** | Toggle chunk coordinate labels |
| **N** | Toggle elevation noise heatmap on tiles |
| **H** | Toggle debug HUD |
| **P** | Toggle profiler timing panel in HUD |

### UI (on-screen buttons and sliders)

| Control | Action |
|---------|--------|
| **Generate** | Apply UI settings and regenerate world |
| **Random Seed** | Randomize seed and regenerate |
| **Clear** | Clear world and reset camera to origin |
| **Save** | Persist dirty chunk overrides + manifest |
| **Load** | Load manifest and regenerate with saved settings |
| **Delete Save** | Remove save folder for configured world name |
| Sliders / seed field / infinite toggle / noise dropdown | Adjust parameters (auto-regenerates after 0.4 s debounce) |

---

# 📸 Screenshots

---

## Gameplay

> 📷 INSERT SCREENSHOT HERE

---

## World Exploration



---

## Biome Overview



---

## Noise Heatmap Debug View



---

## Generation UI Panel



---

## Chunk Streaming Debug Overlay



---

## Terrain Config Editor Preview



---

## Noise Benchmark 


---

# 🎥 GIF Demonstration



---

# ⚡ Performance

The project implements several optimizations aimed at smooth streaming of large worlds:

| Technique | Where | Effect |
|-----------|-------|--------|
| Burst + `IJobParallelFor` | `NoiseJob`, `TerrainFbm` | Multi-threaded noise per chunk |
| Per-frame job cap | `maxJobsPerFrame` | Limits worker saturation |
| Per-frame flush cap | `maxTilePlacementsPerFrame` | Spreads Tilemap writes across frames |
| Chebyshev unload radius | `ChunkStreamer` | Keeps memory bounded around camera |
| Shared Tilemap + `SetTilesBlock` | `ChunkStreamer` | Bulk tile updates, single renderer |
| Scratch buffer reuse | `_scratchTilesBlock`, `_scratchBiomeWeights` | Avoids GC during chunk flush |
| Allocation-free dominant biome resolve | `BiomeResolver.ResolveDominantIndex` | No per-tile heap allocations |
| NativeArray lifecycle management | `PollCompletedJobs`, unload | Prevents native memory leaks |
| Object pooling (infrastructure) | `ObjectSpawner` | Pool stacks for prefabs when spawning is enabled |

**Benchmarking:** Use **Tools → Procedural World → Noise Benchmark** to compare `SimplexFbm` vs `ClassicPerlinFbm` across many seeds and export CSV to `Assets/BenchmarkResults/`. The harness mirrors the production `NoiseJobScheduler` path including optional managed-array copy timing.

**Profiling:** Enable Unity Profiler CPU/Memory modules and filter for `PW.*` markers, or press **P** in Play mode with the debug HUD visible.

---

# 🔮 Future Improvements

Realistic extensions based on the current architecture:

- **Wire `ObjectSpawner.SpawnForChunk`** into `ChunkStreamer.FlushChunkToTilemap` and `DespawnChunk` on unload; populate `BiomeObjectRule.spawnRules` in `TerrainConfig`
- **Call `WorldSaveSystem.LoadChunkOverrides`** when chunks become data-ready so saved tile edits restore correctly
- **Tile editing tool** that writes to `ChunkData.TileOverrides` and marks chunks modified
- **Async save/load** (`Task.Run`) as noted in `WorldSaveSystem` comments
- **Integrate Unity Input System** actions from `InputSystem_Actions.inputactions` instead of legacy `Input` API
- **Biome transition smoothing** using blended tile colors (resolver already computes blended color for preview)
- **Additional noise backends** or domain warp / erosion passes in the pipeline
- **LOD or impostor rendering** for distant chunks if object spawning is enabled
- **Unit tests** for deterministic noise parity between editor preview and runtime jobs
- **Remove or restore** commented `NoiseGenerator.cs` legacy path for clarity

---

# 📄 License

Copyright © 2026 Nazaryi Rudyi.

This project was developed as part of a bachelor's thesis. All rights reserved.

The source code, assets, and documentation may not be copied, modified, distributed, or used for commercial purposes without prior written permission from the author.

---

# 👤 Author

**Nazaryi Rudyi**

Bachelor's Degree Student in Software Engineering

- GitHub: https://github.com/Ancellie
- Email: xnazar507@gmail.com