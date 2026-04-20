using UnityEngine;

public class WorldManager : MonoBehaviour
{
    public static WorldManager Instance { get; private set; }

    [Header("Dependencies")]
    [SerializeField] private LevelGenerator levelGenerator;

    [Header("Camera")]
    [SerializeField] private Camera mainCamera;

    [Header("Default Settings")]
    [SerializeField] private GenerationSettings defaultSettings = new GenerationSettings();

    public GenerationSettings CurrentSettings => _settings;
    public float[,] LastHeightMap { get; private set; }
    public bool IsGenerating { get; private set; }

    public event System.Action<GenerationSettings> OnWorldGenerated;
    public event System.Action OnWorldCleared;

    private GenerationSettings _settings;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance  = this;
        _settings = CloneSettings(defaultSettings);
    }

    private void Start() => GenerateWorld();

    public void GenerateWorld()
    {
        if (IsGenerating) { Debug.LogWarning("[WorldManager] Already generating — ignored."); return; }

        IsGenerating = true;
        _settings.Validate();
        Debug.Log($"[WorldManager] Generating world {_settings}");

        LastHeightMap = NoiseGenerator.GenerateHeightMap(
            _settings.width, _settings.height, _settings.seed,
            _settings.scale, _settings.octaves, _settings.persistence,
            _settings.lacunarity, _settings.offset);

        levelGenerator.GenerateLevel(LastHeightMap);
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
        levelGenerator.ClearLevel();
        LastHeightMap = null;
        OnWorldCleared?.Invoke();
        Debug.Log("[WorldManager] World cleared.");
    }

    public int GetCurrentSeed() => _settings.seed;

    private void CentreCamera()
    {
        if (mainCamera == null) return;
        float centreX = _settings.width  * 0.5f;
        float centreY = _settings.height * 0.5f;
        mainCamera.transform.position = new Vector3(centreX, centreY, -10f);
        float halfVertical   = _settings.height * 0.5f * 1.05f;
        float halfHorizontal = _settings.width  * 0.5f * 1.05f / mainCamera.aspect;
        mainCamera.orthographicSize = Mathf.Max(halfVertical, halfHorizontal);
    }

    private static GenerationSettings CloneSettings(GenerationSettings src) => new GenerationSettings
    {
        width = src.width, height = src.height, seed = src.seed,
        scale = src.scale, octaves = src.octaves, persistence = src.persistence,
        lacunarity = src.lacunarity, offset = src.offset
    };
}