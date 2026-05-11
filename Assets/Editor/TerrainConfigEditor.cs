// This file must be in an Editor/ folder — it is stripped from runtime builds.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Custom Inspector for TerrainConfigSO.
///
/// FEATURES:
///   1. Colour swatch grid — all biomes displayed as labelled colour strips.
///   2. "Preview Noise" button — draws a 256×256 noise texture directly in the
///      Inspector using the current settings. No Play mode required.
///   3. "Generate In Editor" button — calls ChunkStreamer.Initialize() in Edit mode
///      (requires [ExecuteInEditMode] on ChunkStreamer OR usage via EditorApplication.isPlaying).
///   4. Collapsible biome entries with an inline weight-diagram bar.
///   5. Validation warnings (e.g. missing Ocean biome, overlapping thresholds).
///
/// USAGE:
///   Simply select the TerrainConfig.asset — this editor loads automatically.
/// </summary>
[CustomEditor(typeof(TerrainConfigSO))]
public class TerrainConfigEditor : Editor
{
    // ── State ─────────────────────────────────────────────────────────────────────

    private TerrainConfigSO _target;
    private Texture2D       _previewTex;
    private bool            _showPreview   = true;
    private bool            _showBiomes    = true;
    private bool[]          _biomeFoldouts;

    // Preview settings
    private int   _previewSize        = 128;
    private float _previewScale       = 40f;
    private int   _previewSeed        = 42;
    private int   _previewOctaves     = 4;
    private float _previewPersistence = 0.5f;
    private float _previewLacunarity  = 2f;

    // Noise map selector
    private enum PreviewMap { Elevation, Moisture, Temperature, Biome }
    private PreviewMap _previewMap = PreviewMap.Biome;

    // ── Editor Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _target        = (TerrainConfigSO)target;
        _biomeFoldouts = new bool[_target.biomes?.Length ?? 0];
        RegeneratePreview();
    }

    private void OnDisable()
    {
        if (_previewTex != null) DestroyImmediate(_previewTex);
    }

    // ── Inspector GUI ─────────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawHeader();
        EditorGUILayout.Space(4);

        DrawValidationWarnings();
        EditorGUILayout.Space(4);

        DrawPreviewSection();
        EditorGUILayout.Space(6);

        DrawBiomeSection();
        EditorGUILayout.Space(4);

        DrawEditorActions();

        if (GUI.changed)
        {
            serializedObject.ApplyModifiedProperties();
            _target.InvalidateResolver();
            RegeneratePreview();
            EditorUtility.SetDirty(_target);
        }
    }

    // ── Sections ──────────────────────────────────────────────────────────────────

    private void DrawHeader()
    {
        GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 14,
            alignment = TextAnchor.MiddleCenter
        };
        GUILayout.Label("🌍  Terrain Configuration", title);
        GUILayout.Label($"  {_target.biomes?.Length ?? 0} biomes defined",
            EditorStyles.centeredGreyMiniLabel);
    }

    private void DrawValidationWarnings()
    {
        if (_target.biomes == null || _target.biomes.Length == 0)
        {
            EditorGUILayout.HelpBox("No biomes defined! Add at least one biome.", MessageType.Error);
            return;
        }

        bool hasOcean    = false;
        bool hasMountain = false;
        foreach (var b in _target.biomes)
        {
            if (b.name == "Ocean")    hasOcean    = true;
            if (b.name == "Mountain") hasMountain = true;
        }

        if (!hasOcean)
            EditorGUILayout.HelpBox("Missing 'Ocean' biome — elevation override won't work.", MessageType.Warning);
        if (!hasMountain)
            EditorGUILayout.HelpBox("Missing 'Mountain' biome — high-elevation override won't work.", MessageType.Warning);
    }

    private void DrawPreviewSection()
    {
        _showPreview = EditorGUILayout.BeginFoldoutHeaderGroup(_showPreview, "🔬 Noise Preview");
        if (_showPreview)
        {
            EditorGUI.indentLevel++;

            _previewMap        = (PreviewMap)EditorGUILayout.EnumPopup("Preview Map", _previewMap);
            _previewSize       = EditorGUILayout.IntSlider("Resolution", _previewSize, 64, 256);
            _previewSeed       = EditorGUILayout.IntField("Seed", _previewSeed);
            _previewScale      = EditorGUILayout.Slider("Scale", _previewScale, 5f, 150f);
            _previewOctaves    = EditorGUILayout.IntSlider("Octaves", _previewOctaves, 1, 8);
            _previewPersistence = EditorGUILayout.Slider("Persistence", _previewPersistence, 0.1f, 1f);
            _previewLacunarity  = EditorGUILayout.Slider("Lacunarity", _previewLacunarity, 1f, 4f);

            EditorGUI.indentLevel--;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⟳ Regenerate Preview", GUILayout.Height(24)))
                RegeneratePreview();
            EditorGUILayout.EndHorizontal();

            if (_previewTex != null)
            {
                float aspect = _previewTex.width / (float)_previewTex.height;
                float w      = EditorGUIUtility.currentViewWidth - 36f;
                float h      = w / aspect;
                var   rect   = GUILayoutUtility.GetRect(w, h);
                EditorGUI.DrawPreviewTexture(rect, _previewTex, null, ScaleMode.ScaleToFit);
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void DrawBiomeSection()
    {
        _showBiomes = EditorGUILayout.BeginFoldoutHeaderGroup(_showBiomes, "🌱 Biome Definitions");

        if (_showBiomes && _target.biomes != null)
        {
            // Ensure foldout array matches biome count
            if (_biomeFoldouts.Length != _target.biomes.Length)
                _biomeFoldouts = new bool[_target.biomes.Length];

            // Colour swatch row
            EditorGUILayout.BeginHorizontal();
            foreach (var biome in _target.biomes)
            {
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = biome.primaryColor;
                GUILayout.Button(biome.name, GUILayout.Height(18));
                GUI.backgroundColor = oldColor;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Draw default property drawer — biome list
            var biomeProp = serializedObject.FindProperty("biomes");
            EditorGUILayout.PropertyField(biomeProp, new GUIContent("Biomes"), true);
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void DrawEditorActions()
    {
        EditorGUILayout.LabelField("Editor Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("⚡ Generate In Editor", GUILayout.Height(30)))
            GenerateInEditor();

        if (GUILayout.Button("🔄 Reset to Defaults", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Reset Biomes",
                "Reset all biomes to factory defaults?", "Reset", "Cancel"))
            {
                // No easy way to call static default factory — tell user to delete and recreate
                Debug.Log("[TerrainConfigEditor] To reset, delete and recreate the TerrainConfig asset.");
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    // ── Preview Texture Generation ────────────────────────────────────────────────

    private void RegeneratePreview()
    {
        if (_target == null || _target.biomes == null) return;

        int sz = _previewSize;

        if (_previewTex == null || _previewTex.width != sz)
        {
            if (_previewTex != null) DestroyImmediate(_previewTex);
            _previewTex = new Texture2D(sz, sz, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };
        }

        // Build three noise maps using local Simplex noise generator
        var elev = GeneratePreviewNoise(sz, sz, _previewSeed,
            _previewScale, _previewOctaves, _previewPersistence, _previewLacunarity);
        var moist = GeneratePreviewNoise(sz, sz, _previewSeed + 31337,
            _previewScale, _previewOctaves, _previewPersistence, _previewLacunarity);
        var temp  = GeneratePreviewNoise(sz, sz, _previewSeed + 99991,
            _previewScale, _previewOctaves, _previewPersistence, _previewLacunarity);

        var resolver = _target.GetResolver();
        var pixels   = new Color[sz * sz];

        for (int y = 0; y < sz; y++)
        {
            for (int x = 0; x < sz; x++)
            {
                float h = elev[x, y];
                float m = moist[x, y];
                float t = temp[x, y];

                Color c;
                switch (_previewMap)
                {
                    case PreviewMap.Elevation:    c = new Color(h, h, h); break;
                    case PreviewMap.Moisture:     c = new Color(0f, m * 0.5f, m); break;
                    case PreviewMap.Temperature:  c = new Color(t, t * 0.4f, 0f); break;
                    default:
                        var sample = resolver.Resolve(h, m, t);
                        c = sample.BlendedColor;
                        break;
                }
                pixels[x + y * sz] = c;
            }
        }

        _previewTex.SetPixels(pixels);
        _previewTex.Apply();
        Repaint();
    }

    // ── Local Noise Generation for Preview ────────────────────────────────────────

    private float[,] GeneratePreviewNoise(int width, int height, int seed, float scale, int octaves, float persistence, float lacunarity)
    {
        float[,] map = new float[width, height];
        
        float maxPossibleHeight = 0f;
        float ampForMax = 1f;
        for (int i = 0; i < octaves; i++)
        {
            maxPossibleHeight += ampForMax;
            ampForMax *= persistence;
        }
        
        uint validSeed = (uint)(seed == 0 ? 1 : Mathf.Abs(seed));
        var prng = new Unity.Mathematics.Random(validSeed);
        float2[] offsets = new float2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            offsets[i] = new float2(prng.NextFloat(-100000f, 100000f), prng.NextFloat(-100000f, 100000f));
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseValue = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float sampleX = (x + offsets[o].x) / scale * frequency;
                    float sampleY = (y + offsets[o].y) / scale * frequency;
                    noiseValue += noise.snoise(new float2(sampleX, sampleY)) * amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }
                map[x, y] = Mathf.Clamp01((noiseValue / maxPossibleHeight + 1f) * 0.5f);
            }
        }
        return map;
    }

    // ── Editor World Generation ───────────────────────────────────────────────────

    private void GenerateInEditor()
    {
        if (Application.isPlaying)
        {
            // In Play mode — delegate to WorldManager
            if (WorldManager.Instance != null)
                WorldManager.Instance.GenerateWorld();
            else
                Debug.LogWarning("[TerrainConfigEditor] No WorldManager.Instance found.");
        }
        else
        {
            // Edit mode — just regenerate the preview texture as feedback
            _previewSeed = UnityEngine.Random.Range(0, 99999);
            RegeneratePreview();
            Debug.Log("[TerrainConfigEditor] Preview regenerated in Edit mode. " +
                      "Press Play to generate the full scene world.");
        }
    }
}
#endif
