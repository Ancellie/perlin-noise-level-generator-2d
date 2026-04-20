using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIController : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button generateButton;
    [SerializeField] private Button randomSeedButton;
    [SerializeField] private Button clearButton;

    [Header("Seed")]
    [SerializeField] private TMP_InputField seedInputField;

    [Header("Sliders")]
    [SerializeField] private Slider scaleSlider;
    [SerializeField] private Slider octavesSlider;
    [SerializeField] private Slider persistenceSlider;
    [SerializeField] private Slider lacunaritySlider;
    [SerializeField] private Slider widthSlider;
    [SerializeField] private Slider heightSlider;

    [Header("Slider Value Labels")]
    [SerializeField] private TMP_Text scaleValueLabel;
    [SerializeField] private TMP_Text octavesValueLabel;
    [SerializeField] private TMP_Text persistenceValueLabel;
    [SerializeField] private TMP_Text lacunarityValueLabel;
    [SerializeField] private TMP_Text widthValueLabel;
    [SerializeField] private TMP_Text heightValueLabel;

    [Header("Status Bar")]
    [SerializeField] private TMP_Text statusLabel;

    private void Start()
    {
        InitSliders();
        InitSeedField();
        RegisterButtonListeners();
        RegisterSliderListeners();
        SubscribeToWorldManagerEvents();
        RefreshAllLabels();
        SetStatus("Ready — press Generate or Random Seed to start.");
    }

    private void OnDestroy()
    {
        if (WorldManager.Instance != null)
        {
            WorldManager.Instance.OnWorldGenerated -= HandleWorldGenerated;
            WorldManager.Instance.OnWorldCleared   -= HandleWorldCleared;
        }
    }

    private void InitSliders()
    {
        var s = WorldManager.Instance.CurrentSettings;
        ConfigureSlider(scaleSlider,       1f,   200f, false, s.scale);
        ConfigureSlider(octavesSlider,     1f,   8f,   true,  s.octaves);
        ConfigureSlider(persistenceSlider, 0.1f, 1f,   false, s.persistence);
        ConfigureSlider(lacunaritySlider,  1f,   4f,   false, s.lacunarity);
        ConfigureSlider(widthSlider,       20f,  300f, true,  s.width);
        ConfigureSlider(heightSlider,      20f,  300f, true,  s.height);
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
    }

    private void RegisterSliderListeners()
    {
        if (scaleSlider       != null) scaleSlider.onValueChanged.AddListener(_       => RefreshAllLabels());
        if (octavesSlider     != null) octavesSlider.onValueChanged.AddListener(_     => RefreshAllLabels());
        if (persistenceSlider != null) persistenceSlider.onValueChanged.AddListener(_ => RefreshAllLabels());
        if (lacunaritySlider  != null) lacunaritySlider.onValueChanged.AddListener(_  => RefreshAllLabels());
        if (widthSlider       != null) widthSlider.onValueChanged.AddListener(_       => RefreshAllLabels());
        if (heightSlider      != null) heightSlider.onValueChanged.AddListener(_      => RefreshAllLabels());
    }

    private void SubscribeToWorldManagerEvents()
    {
        if (WorldManager.Instance == null) return;
        WorldManager.Instance.OnWorldGenerated += HandleWorldGenerated;
        WorldManager.Instance.OnWorldCleared   += HandleWorldCleared;
    }

    private void OnGenerateClicked()
    {
        SetStatus("Generating world…");
        WorldManager.Instance.ApplySettingsAndGenerate(BuildSettingsFromUI());
    }

    private void OnRandomSeedClicked()
    {
        SetStatus("Generating random world…");
        WorldManager.Instance.RandomizeSeedAndGenerate();
    }

    private void OnClearClicked() => WorldManager.Instance.ClearWorld();

    private void HandleWorldGenerated(GenerationSettings s)
    {
        UpdateSeedField(s.seed);
        SetStatus($"World generated! Seed: {s.seed} | Size: {s.width}×{s.height}");
    }

    private void HandleWorldCleared() => SetStatus("World cleared.");

    private GenerationSettings BuildSettingsFromUI()
    {
        if (!int.TryParse(seedInputField != null ? seedInputField.text : "0", out int seed)) seed = 0;
        return new GenerationSettings
        {
            width       = SliderIntValue(widthSlider, 120),
            height      = SliderIntValue(heightSlider, 80),
            seed        = seed,
            scale       = SliderFloatValue(scaleSlider, 40f),
            octaves     = SliderIntValue(octavesSlider, 4),
            persistence = SliderFloatValue(persistenceSlider, 0.5f),
            lacunarity  = SliderFloatValue(lacunaritySlider, 2f),
        };
    }

    private void RefreshAllLabels()
    {
        SetLabel(scaleValueLabel,       $"Scale: {SliderFloatValue(scaleSlider, 40f):F1}");
        SetLabel(octavesValueLabel,     $"Octaves: {SliderIntValue(octavesSlider, 4)}");
        SetLabel(persistenceValueLabel, $"Persistence: {SliderFloatValue(persistenceSlider, 0.5f):F2}");
        SetLabel(lacunarityValueLabel,  $"Lacunarity: {SliderFloatValue(lacunaritySlider, 2f):F2}");
        SetLabel(widthValueLabel,       $"Width: {SliderIntValue(widthSlider, 120)} tiles");
        SetLabel(heightValueLabel,      $"Height: {SliderIntValue(heightSlider, 80)} tiles");
    }

    private static void ConfigureSlider(Slider s, float min, float max, bool whole, float value)
    {
        if (s == null) return;
        s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = value;
    }

    private static int   SliderIntValue  (Slider s, int   fb) => s != null ? Mathf.RoundToInt(s.value) : fb;
    private static float SliderFloatValue(Slider s, float fb) => s != null ? s.value : fb;
    private static void  SetLabel(TMP_Text l, string t) { if (l != null) l.text = t; }
    private void SetStatus(string msg) { if (statusLabel != null) statusLabel.text = msg; }
    private void UpdateSeedField(int seed) { if (seedInputField != null) seedInputField.text = seed.ToString(); }
}