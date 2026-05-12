using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    [SerializeField] private Slider widthSlider;
    [SerializeField] private Slider heightSlider;
    
    // ── Inspector: Infinite World Toggle ─────────────────────────────────────────
    [Header("Infinite World")]
    [Tooltip("When on, the world generates chunks in all directions without bounds.")]
    [SerializeField] private Toggle infiniteToggle;

    [Header("Noise")]
    [Tooltip("Optional. If unset, WorldManager keeps its current noise backend when applying UI settings.")]
    [SerializeField] private TMP_Dropdown noiseBackendDropdown;

    // ── Inspector: Labels ─────────────────────────────────────────────────────────

    [Header("Value Labels")]
    [SerializeField] private TMP_Text scaleValueLabel;
    [SerializeField] private TMP_Text octavesValueLabel;
    [SerializeField] private TMP_Text persistenceValueLabel;
    [SerializeField] private TMP_Text lacunarityValueLabel;
    [SerializeField] private TMP_Text widthValueLabel;
    [SerializeField] private TMP_Text heightValueLabel;

    [Header("Status / Stats")]
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_Text streamingStatsLabel;    

    // ── Private ───────────────────────────────────────────────────────────────────

    private ChunkStreamer _streamer;
    private Coroutine     _regenerateCoroutine;
    private const float   RegenerateDelay = 0.4f;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────────

    private void Start()
    {
        _streamer = FindAnyObjectByType<ChunkStreamer>();

        InitSliders();
        InitSeedField();
        InitNoiseBackendDropdown();
        RegisterButtonListeners();
        RegisterSliderListeners();
        SubscribeToEvents();
        RefreshAllLabels();
        InitInfiniteToggle();

        SetStatus("Ready. Press Generate or scroll to explore.");
    }

    private void Update()
    {
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
        ConfigureSlider(widthSlider,       10f,  500f, true,  s.width);
        ConfigureSlider(heightSlider,      10f,  500f, true,  s.height);
    }

    private void InitSeedField()
    {
        if (seedInputField == null) return;
        seedInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
        seedInputField.text        = WorldManager.Instance.CurrentSettings.seed.ToString();
    }

    private void InitNoiseBackendDropdown()
    {
        if (noiseBackendDropdown == null || WorldManager.Instance == null) return;
        noiseBackendDropdown.ClearOptions();
        noiseBackendDropdown.AddOptions(new List<string>
        {
            "Simplex fBM",
            "Classic Perlin fBM"
        });
        noiseBackendDropdown.SetValueWithoutNotify((int)WorldManager.Instance.CurrentSettings.noiseBackend);
    }
    
    private void InitInfiniteToggle()
    {
        if (infiniteToggle == null) return;
        infiniteToggle.isOn = WorldManager.Instance.CurrentSettings.infiniteWorld;
        ApplyInfiniteToggleState(infiniteToggle.isOn);
        infiniteToggle.onValueChanged.AddListener(OnInfiniteToggleChanged);
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
        if (scaleSlider       != null) scaleSlider.onValueChanged.AddListener(_       => OnSliderChanged());
        if (octavesSlider     != null) octavesSlider.onValueChanged.AddListener(_     => OnSliderChanged());
        if (persistenceSlider != null) persistenceSlider.onValueChanged.AddListener(_ => OnSliderChanged());
        if (lacunaritySlider  != null) lacunaritySlider.onValueChanged.AddListener(_  => OnSliderChanged());
        if (widthSlider       != null) widthSlider.onValueChanged.AddListener(_       => OnSliderChanged());
        if (heightSlider      != null) heightSlider.onValueChanged.AddListener(_      => OnSliderChanged());
        if (noiseBackendDropdown != null)
            noiseBackendDropdown.onValueChanged.AddListener(_ => OnSliderChanged());
    }

    private void OnSliderChanged()
    {
        RefreshAllLabels();
        if (_regenerateCoroutine != null) StopCoroutine(_regenerateCoroutine);
        _regenerateCoroutine = StartCoroutine(RegenerateAfterDelay());
    }

    private void OnInfiniteToggleChanged(bool isInfinite)
    {
        ApplyInfiniteToggleState(isInfinite);
        RefreshAllLabels();
        
        if (_regenerateCoroutine != null) StopCoroutine(_regenerateCoroutine);
        _regenerateCoroutine = StartCoroutine(RegenerateAfterDelay());
    }
    
    private void ApplyInfiniteToggleState(bool isInfinite)
    {
        if (widthSlider  != null) widthSlider.interactable  = !isInfinite;
        if (heightSlider != null) heightSlider.interactable = !isInfinite;
    }
    private System.Collections.IEnumerator RegenerateAfterDelay()
    {
        yield return new WaitForSeconds(RegenerateDelay);
        SetStatus("Generating…");
        WorldManager.Instance.ApplySettingsAndGenerate(BuildSettingsFromUI());
        _regenerateCoroutine = null;
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
        if (infiniteToggle != null && infiniteToggle.isOn != s.infiniteWorld)
        {
            infiniteToggle.isOn = s.infiniteWorld;
            ApplyInfiniteToggleState(s.infiniteWorld);
        }
        
        if (scaleSlider != null) scaleSlider.SetValueWithoutNotify(s.scale);
        if (octavesSlider != null) octavesSlider.SetValueWithoutNotify(s.octaves);
        if (persistenceSlider != null) persistenceSlider.SetValueWithoutNotify(s.persistence);
        if (lacunaritySlider != null) lacunaritySlider.SetValueWithoutNotify(s.lacunarity);
        if (widthSlider != null) widthSlider.SetValueWithoutNotify(s.width);
        if (heightSlider != null) heightSlider.SetValueWithoutNotify(s.height);
        if (noiseBackendDropdown != null)
            noiseBackendDropdown.SetValueWithoutNotify((int)s.noiseBackend);

        RefreshAllLabels();
        
        string modeLabel = s.infiniteWorld ? "Infinite" : $"{s.width}×{s.height}";
        SetStatus($"World streaming — Seed: {s.seed} | Mode: {modeLabel}");
    }

    private void HandleWorldCleared()  => SetStatus("World cleared.");
    private void HandleWorldLoaded(string name) => SetStatus($"World '{name}' loaded.");

    // ── Settings Builder ──────────────────────────────────────────────────────────

    private GenerationSettings BuildSettingsFromUI()
    {
        if (!int.TryParse(seedInputField != null ? seedInputField.text : "0", out int seed))
            seed = 0;

        bool infinite = infiniteToggle != null && infiniteToggle.isOn;
        var wm = WorldManager.Instance;
        var backend = wm != null ? wm.CurrentSettings.noiseBackend : NoiseBackend.SimplexFbm;
        if (noiseBackendDropdown != null)
            backend = (NoiseBackend)noiseBackendDropdown.value;

        return new GenerationSettings
        {
            noiseBackend  = backend,
            seed          = seed,
            scale         = SliderFloatValue(scaleSlider,       40f),
            octaves       = SliderIntValue(octavesSlider,       4),
            persistence   = SliderFloatValue(persistenceSlider, 0.5f),
            lacunarity    = SliderFloatValue(lacunaritySlider,  2f),
            width         = SliderIntValue(widthSlider,         120),
            height        = SliderIntValue(heightSlider,        80),
            infiniteWorld = infinite,
        };
    }

    // ── Label Updates ─────────────────────────────────────────────────────────────

    private void RefreshAllLabels()
    {
        bool infinite = infiniteToggle != null && infiniteToggle.isOn;
        
        SetLabel(scaleValueLabel,       $"Scale: {SliderFloatValue(scaleSlider, 40f):F1}");
        SetLabel(octavesValueLabel,     $"Octaves: {SliderIntValue(octavesSlider, 4)}");
        SetLabel(persistenceValueLabel, $"Persistence: {SliderFloatValue(persistenceSlider, 0.5f):F2}");
        SetLabel(lacunarityValueLabel,  $"Lacunarity: {SliderFloatValue(lacunaritySlider, 2f):F2}");
        SetLabel(widthValueLabel,       infinite ? "Width: ∞" : $"Width: {SliderIntValue(widthSlider, 120)} tiles");
        SetLabel(heightValueLabel,      infinite ? "Height: ∞" : $"Height: {SliderIntValue(heightSlider, 80)} tiles");
    }

    // ── Utilities ─────────────────────────────────────────────────────────────────

    private static void ConfigureSlider(Slider s, float min, float max, bool whole, float val)
    {
        if (s == null) return;
        s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = val;
        s.interactable = true;
    }

    private static int   SliderIntValue(Slider s,   int   fb) => s != null ? Mathf.RoundToInt(s.value) : fb;
    private static float SliderFloatValue(Slider s, float fb) => s != null ? s.value : fb;
    private static void  SetLabel(TMP_Text t,  string msg)    { if (t != null) t.text = msg; }

    private void SetStatus(string msg)     { if (statusLabel    != null) statusLabel.text = msg; }
    private void UpdateSeedField(int seed) { if (seedInputField != null) seedInputField.SetTextWithoutNotify(seed.ToString()); }
}
