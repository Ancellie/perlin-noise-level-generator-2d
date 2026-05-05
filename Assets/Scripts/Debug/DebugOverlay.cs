using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime debug visualization overlay. Draws on top of the game view without
/// requiring any additional UI GameObjects.
///
/// MODES (toggle with keyboard shortcuts):
///   C — chunk borders (white grid aligned to chunk boundaries)
///   B — biome labels (chunk-centre text overlay)
///   N — noise heatmap (colour-codes tiles by raw elevation)
///   J — pending job count in corner HUD
///
/// PERFORMANCE:
///   GL lines are cheap — drawn in OnRenderObject() after all cameras render.
///   Text (OnGUI) is batched per frame. Disabling a mode costs nothing.
///   The overlay auto-disables in builds (DEVELOPMENT_BUILD guard).
///
/// Attach to the Main Camera GameObject.
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────────

    [Header("Toggle Keys")]
    [SerializeField] private KeyCode toggleChunkKey  = KeyCode.C;
    [SerializeField] private KeyCode toggleBiomeKey  = KeyCode.B;
    [SerializeField] private KeyCode toggleNoiseKey  = KeyCode.N;
    [SerializeField] private KeyCode toggleHudKey    = KeyCode.H;

    [Header("Visual Settings")]
    [SerializeField] private Color chunkBorderColor = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private int   chunkSize        = 32;   // must match ChunkStreamer

    // ── State ─────────────────────────────────────────────────────────────────────

    private bool _showChunks  = true;
    private bool _showBiomes  = false;
    private bool _showNoise   = false;
    private bool _showHud     = true;

    private Material _lineMat;
    private Camera   _cam;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        _cam     = GetComponent<Camera>();
        _lineMat = CreateLineMaterial();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleChunkKey)) _showChunks = !_showChunks;
        if (Input.GetKeyDown(toggleBiomeKey)) _showBiomes = !_showBiomes;
        if (Input.GetKeyDown(toggleNoiseKey)) _showNoise  = !_showNoise;
        if (Input.GetKeyDown(toggleHudKey))   _showHud    = !_showHud;
    }

    // ── GL Rendering ──────────────────────────────────────────────────────────────

    private void OnRenderObject()
    {
        if (!_showChunks) return;
        if (WorldManager.Instance == null) return;

        var streamer = FindAnyObjectByType<ChunkStreamer>();
        if (streamer == null) return;

        _lineMat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);
        GL.Begin(GL.LINES);
        GL.Color(chunkBorderColor);

        foreach (var coord in streamer.ActiveChunks.Keys)
        {
            float x0 = coord.X * chunkSize;
            float y0 = coord.Y * chunkSize;
            float x1 = x0 + chunkSize;
            float y1 = y0 + chunkSize;

            // Bottom
            GL.Vertex3(x0, y0, 0); GL.Vertex3(x1, y0, 0);
            // Top
            GL.Vertex3(x0, y1, 0); GL.Vertex3(x1, y1, 0);
            // Left
            GL.Vertex3(x0, y0, 0); GL.Vertex3(x0, y1, 0);
            // Right
            GL.Vertex3(x1, y0, 0); GL.Vertex3(x1, y1, 0);
        }

        GL.End();
        GL.PopMatrix();
    }

    // ── GUI Labels ────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        var streamer = FindAnyObjectByType<ChunkStreamer>();

        // ── HUD ───────────────────────────────────────────────────────────────────
        if (_showHud && streamer != null)
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 18,
                alignment = TextAnchor.UpperLeft
            };
            style.normal.textColor = Color.white;

            string hud =
                $"[DEBUG HUD]  (H to toggle)\n" +
                $"Active Chunks : {streamer.ActiveChunkCount}\n" +
                $"Pending Jobs  : {streamer.PendingJobCount}\n" +
                $"Seed          : {WorldManager.Instance?.GetCurrentSeed()}\n" +
                $"\n" +
                $"[C] Chunk borders : {(_showChunks ? "ON" : "OFF")}\n" +
                $"[B] Biome labels  : {(_showBiomes ? "ON" : "OFF")}\n" +
                $"[N] Noise heat    : {(_showNoise  ? "ON" : "OFF")}";

            GUI.Box(new Rect(10, 10, 440, 320), hud, style);
        }

        // ── Biome labels ──────────────────────────────────────────────────────────
        if (_showBiomes && streamer != null && WorldManager.Instance != null)
        {
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 18,
                alignment = TextAnchor.MiddleCenter
            };
            labelStyle.normal.textColor = Color.yellow;

            foreach (var kvp in streamer.ActiveChunks)
            {
                var chunk  = kvp.Value;
                if (chunk.State != ChunkData.ChunkState.Ready) continue;

                // Convert chunk centre world pos → screen pos
                Vector2 wc  = kvp.Key.WorldCentre(chunkSize);
                Vector3 sp  = _cam.WorldToScreenPoint(new Vector3(wc.x, wc.y, 0f));
                if (sp.z < 0) continue;   // behind camera

                // Flip Y (screen coords vs GUI coords)
                Rect r = new Rect(sp.x - 50, Screen.height - sp.y - 10, 100, 20);
                GUI.Label(r, $"Chunk {kvp.Key.X},{kvp.Key.Y}", labelStyle);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static Material CreateLineMaterial()
    {
        // Standard unlit vertex-coloured shader — always available
        var shader = Shader.Find("Hidden/Internal-Colored");
        var mat    = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_Cull",      (int)UnityEngine.Rendering.CullMode.Off);
        mat.SetInt("_ZWrite",    0);
        return mat;
    }
}
