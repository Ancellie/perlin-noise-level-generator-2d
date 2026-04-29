using UnityEngine;

public class WorldManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────────

    public static WorldManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────────

    [Header("Core Systems")]
    [SerializeField] private ChunkStreamer   chunkStreamer;
    [SerializeField] private ObjectSpawner   objectSpawner;
    [SerializeField] private WorldSaveSystem saveSystem;

    [Header("Configuration")]
    [SerializeField] private TerrainConfigSO terrainConfig;
    [SerializeField] private Camera          mainCamera;

    [Header("Default Settings")]
    [SerializeField] private GenerationSettings defaultSettings = new GenerationSettings();

    // ── Public State ──────────────────────────────────────────────────────────────

    public GenerationSettings CurrentSettings => _settings;
    public bool               IsGenerating    { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────────

    public event System.Action<GenerationSettings> OnWorldGenerated;
    public event System.Action                     OnWorldCleared;
    public event System.Action<string>             OnWorldLoaded;

    // ── Private ───────────────────────────────────────────────────────────────────

    private GenerationSettings _settings;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _settings = CloneSettings(defaultSettings);
    }

    private void Start()
    {
        // Auto-load if a save exists; otherwise generate fresh
        if (saveSystem != null && saveSystem.SaveExists)
        {
            LoadWorld();
        }
        else
        {
            GenerateWorld();
        }
    }

    // ── Generation ────────────────────────────────────────────────────────────────
    public void GenerateWorld()
    {
        if (IsGenerating) return;
        IsGenerating = true;

        _settings.Validate();
        Debug.Log($"[WorldManager] Starting world generation {_settings}");

        if (chunkStreamer == null)
        {
            Debug.LogError("[WorldManager] ChunkStreamer not assigned!"); IsGenerating = false; return;
        }

        chunkStreamer.Initialize(_settings, terrainConfig);
        CentreCamera();

        IsGenerating = false;
        OnWorldGenerated?.Invoke(_settings);
    }

    public void ApplySettingsAndGenerate(GenerationSettings newSettings)
    {
        _settings = newSettings;
        GenerateWorld();
    }

    public void RandomizeSeedAndGenerate()
    {
        _settings.seed = Random.Range(0, int.MaxValue);
        GenerateWorld();
    }

    public void ClearWorld()
    {
        chunkStreamer?.Initialize(_settings, terrainConfig); // re-init clears all chunks
        OnWorldCleared?.Invoke();
    }

    // ── Save / Load ───────────────────────────────────────────────────────────────

    /// <summary>Persists all dirty chunks and the manifest.</summary>
    public void SaveWorld()
    {
        if (saveSystem == null) { Debug.LogWarning("[WorldManager] No WorldSaveSystem assigned."); return; }
        int written = saveSystem.SaveWorld(_settings);
        Debug.Log($"[WorldManager] World saved ({written} chunk files written).");
    }

    /// <summary>Loads world manifest and restores settings; streamer re-applies overrides per chunk.</summary>
    public void LoadWorld()
    {
        if (saveSystem == null) return;
        var manifest = saveSystem.LoadManifest();
        if (manifest == null) { GenerateWorld(); return; }

        _settings = WorldSaveSystem.ManifestToSettings(manifest);
        GenerateWorld();

        OnWorldLoaded?.Invoke(manifest.worldName);
        Debug.Log($"[WorldManager] World '{manifest.worldName}' loaded (seed {_settings.seed}).");
    }

    public void DeleteSave()
    {
        saveSystem?.DeleteSave();
    }

    public int GetCurrentSeed() => _settings.seed;

    // ── Private Helpers ───────────────────────────────────────────────────────────

    private void CentreCamera()
    {
        if (mainCamera == null) return;
        // In infinite mode, start centred at origin
        mainCamera.transform.position = new Vector3(0f, 0f, -10f);
        mainCamera.orthographicSize   = chunkStreamer.chunkSize * 2f;
    }

    private static GenerationSettings CloneSettings(GenerationSettings src) => new GenerationSettings
    {
        width       = src.width,
        height      = src.height,
        seed        = src.seed,
        scale       = src.scale,
        octaves     = src.octaves,
        persistence = src.persistence,
        lacunarity  = src.lacunarity,
        offset      = src.offset
    };
}
