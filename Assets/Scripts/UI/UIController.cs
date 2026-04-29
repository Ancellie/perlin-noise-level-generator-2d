using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// MODIFIED from v1 — adds Save, Load, and Delete Save buttons.
/// All v1 slider + seed controls are preserved and unchanged.
///
/// New additions:
///   • Save / Load / Delete buttons wired to WorldManager
///   • Streaming stats label (active chunks, pending jobs) updated each frame
///   • OnWorldLoaded callback subscribed from WorldManager
///
/// The streaming stats update happens in Update() rather than via event to give
/// a smooth real-time readout as chunks stream in.
/// </summary>
public class UIController : MonoBehaviour
{
    // ── Inspector: Buttons ────────────────────────────────────────────────────────

    [Header("Buttons — Generation")]
    [SerializeField] private Button generateButton;
    [SerializeField] private Button randomSeedButton;
    [SerializeField] private Button clearButton;

    [Header("Buttons — Save / Load")]
    [SerializeField] private Button saveButton;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button deleteSaveButton;

    // ── Inspector: Seed ───────────────────────────────────────────────────────────

    [Header("Seed")]
    [SerializeField] private TMP_InputField seedInputField;

    // ── Inspector: Sliders ────────────────────────────────────────────────────────

    [Header("Sliders")]
    [SerializeField] private Slider scaleSlider;
    [SerializeField] private Slider octavesSlider;
    [SerializeField] private Slider persistenceSlider;
    [SerializeField] private Slider lacunaritySlider;

    // ── Inspector: Labels ─────────────────────────────────────────────────────────

    [Header("Value Labels")]
    [SerializeField] private TMP_Text scaleValueLabel;
    [SerializeField] private TMP_Text octavesValueLabel;
    [SerializeField] private TMP_Text persistenceValueLabel;
    [SerializeField] private TMP_Text lacunarityValueLabel;

    [Header("Status / Stats")]
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_Text streamingStatsLabel;    // NEW: live chunk counter

    // ── Private ───────────────────────────────────────────────────────────────────

    private ChunkStreamer _streamer;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────────

    private void Start()
    {
        _streamer = FindAnyObjectByType<ChunkStreamer>();

        InitSliders();
        InitSeedField();
        RegisterButtonListeners();
        RegisterSliderListeners();
        SubscribeToEvents();
        RefreshAllLabels();

        SetStatus("Ready. Press Generate or scroll to explore.");
    }

    private void Update()
    {
        // Update streaming stats every frame (cheap text update)
        if (_streamer != null && streamingStatsLabel != null)
        {
            streamingStatsLabel.text =
                $"Chunks: {_streamer.ActiveChunkCount} active | {_streamer.PendingJobCount} pending";
        }
    }

    private void OnDestroy()
    {
        if (WorldManager.Instance == null) return;
        WorldManager.Instance.OnWorldGenerated -= HandleWorldGenerated;
        WorldManager.Instance.OnWorldCleared   -= HandleWorldCleared;
        WorldManager.Instance.OnWorldLoaded    -= HandleWorldLoaded;
    }

    // ── Initialization ────────────────────────────────────────────────────────────

    private void InitSliders()
    {
        var s = WorldManager.Instance.CurrentSettings;
        ConfigureSlider(scaleSlider,       1f,   200f, false, s.scale);
        ConfigureSlider(octavesSlider,     1f,   8f,   true,  s.octaves);
        ConfigureSlider(persistenceSlider, 0.1f, 1f,   false, s.persistence);
        ConfigureSlider(lacunaritySlider,  1f,   4f,   false, s.lacunarity);
    }

    private void InitSeedField()
    {
        if (seedInputField == null) return;
        seedInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
        seedInputField.text        = WorldManager.Instance.CurrentSettings.seed.ToString();
    }

    private void RegisterButtonListeners()
    {
        if (generateButton   != null) generateButton.onClick.AddListener(OnGenerateClicked);
        if (randomSeedButton != null) randomSeedButton.onClick.AddListener(OnRandomSeedClicked);
        if (clearButton      != null) clearButton.onClick.AddListener(OnClearClicked);
        if (saveButton       != null) saveButton.onClick.AddListener(OnSaveClicked);
        if (loadButton       != null) loadButton.onClick.AddListener(OnLoadClicked);
        if (deleteSaveButton != null) deleteSaveButton.onClick.AddListener(OnDeleteSaveClicked);
    }

    private void RegisterSliderListeners()
    {
        if (scaleSlider       != null) scaleSlider.onValueChanged.AddListener(_       => RefreshAllLabels());
        if (octavesSlider     != null) octavesSlider.onValueChanged.AddListener(_     => RefreshAllLabels());
        if (persistenceSlider != null) persistenceSlider.onValueChanged.AddListener(_ => RefreshAllLabels());
        if (lacunaritySlider  != null) lacunaritySlider.onValueChanged.AddListener(_  => RefreshAllLabels());
    }

    private void SubscribeToEvents()
    {
        if (WorldManager.Instance == null) return;
        WorldManager.Instance.OnWorldGenerated += HandleWorldGenerated;
        WorldManager.Instance.OnWorldCleared   += HandleWorldCleared;
        WorldManager.Instance.OnWorldLoaded    += HandleWorldLoaded;
    }

    // ── Button Handlers ───────────────────────────────────────────────────────────

    private void OnGenerateClicked()
    {
        SetStatus("Generating…");
        WorldManager.Instance.ApplySettingsAndGenerate(BuildSettingsFromUI());
    }

    private void OnRandomSeedClicked()
    {
        SetStatus("Generating random world…");
        WorldManager.Instance.RandomizeSeedAndGenerate();
    }

    private void OnClearClicked()
    {
        WorldManager.Instance.ClearWorld();
    }

    private void OnSaveClicked()
    {
        SetStatus("Saving world…");
        WorldManager.Instance.SaveWorld();
        SetStatus("World saved!");
    }

    private void OnLoadClicked()
    {
        SetStatus("Loading world…");
        WorldManager.Instance.LoadWorld();
    }

    private void OnDeleteSaveClicked()
    {
        WorldManager.Instance.DeleteSave();
        SetStatus("Save deleted.");
    }

    // ── Event Callbacks ───────────────────────────────────────────────────────────

    private void HandleWorldGenerated(GenerationSettings s)
    {
        UpdateSeedField(s.seed);
        SetStatus($"World streaming — Seed: {s.seed}");
    }

    private void HandleWorldCleared()  => SetStatus("World cleared.");
    private void HandleWorldLoaded(string name) => SetStatus($"World '{name}' loaded.");

    // ── Settings Builder ──────────────────────────────────────────────────────────

    private GenerationSettings BuildSettingsFromUI()
    {
        if (!int.TryParse(seedInputField != null ? seedInputField.text : "0", out int seed))
            seed = 0;

        return new GenerationSettings
        {
            seed        = seed,
            scale       = SliderFloatValue(scaleSlider,       40f),
            octaves     = SliderIntValue(octavesSlider,       4),
            persistence = SliderFloatValue(persistenceSlider, 0.5f),
            lacunarity  = SliderFloatValue(lacunaritySlider,  2f),
        };
    }

    // ── Label Updates ─────────────────────────────────────────────────────────────

    private void RefreshAllLabels()
    {
        SetLabel(scaleValueLabel,       $"Scale: {SliderFloatValue(scaleSlider, 40f):F1}");
        SetLabel(octavesValueLabel,     $"Octaves: {SliderIntValue(octavesSlider, 4)}");
        SetLabel(persistenceValueLabel, $"Persistence: {SliderFloatValue(persistenceSlider, 0.5f):F2}");
        SetLabel(lacunarityValueLabel,  $"Lacunarity: {SliderFloatValue(lacunaritySlider, 2f):F2}");
    }

    // ── Utilities ─────────────────────────────────────────────────────────────────

    private static void ConfigureSlider(Slider s, float min, float max, bool whole, float val)
    {
        if (s == null) return;
        s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = val;
    }

    private static int   SliderIntValue(Slider s,   int   fb) => s != null ? Mathf.RoundToInt(s.value) : fb;
    private static float SliderFloatValue(Slider s, float fb) => s != null ? s.value : fb;
    private static void  SetLabel(TMP_Text t,  string msg)    { if (t != null) t.text = msg; }

    private void SetStatus(string msg)     { if (statusLabel    != null) statusLabel.text = msg; }
    private void UpdateSeedField(int seed) { if (seedInputField != null) seedInputField.text = seed.ToString(); }
}
