#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch benchmark: many seeds × both <see cref="NoiseBackend"/> values → CSV for thesis charts.
/// </summary>
public class NoiseBenchmarkWindow : EditorWindow
{
    const string MenuPath = "Tools/Procedural World/Noise Benchmark";

    int _startSeed;
    int _seedCount = 40;
    int _warmupPerBackend = 2;
    int _chunkSize = 32;
    int _chunkCoordX;
    int _chunkCoordY;
    float _scale = 40f;
    int _octaves = 4;
    float _persistence = 0.5f;
    float _lacunarity = 2f;
    bool _includeManagedCopy = true;
    string _outputFolder = "BenchmarkResults";

    [MenuItem(MenuPath)]
    static void Open() => GetWindow<NoiseBenchmarkWindow>("Noise Benchmark");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Noise job benchmark (ChunkStreamer-equivalent scheduler)", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _startSeed = EditorGUILayout.IntField("Start seed", _startSeed);
        _seedCount = EditorGUILayout.IntSlider("Seed count", _seedCount, 1, 200);
        _warmupPerBackend = EditorGUILayout.IntSlider("Warmup runs / backend", _warmupPerBackend, 0, 10);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Generation parameters", EditorStyles.boldLabel);
        _chunkSize = EditorGUILayout.IntSlider("Chunk size", _chunkSize, 8, 128);
        _chunkCoordX = EditorGUILayout.IntField("Chunk coord X", _chunkCoordX);
        _chunkCoordY = EditorGUILayout.IntField("Chunk coord Y", _chunkCoordY);
        _scale = EditorGUILayout.Slider("Scale", _scale, 1f, 200f);
        _octaves = EditorGUILayout.IntSlider("Octaves", _octaves, 1, 8);
        _persistence = EditorGUILayout.Slider("Persistence", _persistence, 0.1f, 1f);
        _lacunarity = EditorGUILayout.Slider("Lacunarity", _lacunarity, 1f, 4f);
        _includeManagedCopy = EditorGUILayout.ToggleLeft(
            "Include NativeArray → managed copy (production PollCompletedJobs path)", _includeManagedCopy);

        EditorGUILayout.Space(6);
        _outputFolder = EditorGUILayout.TextField("Output subfolder (under Assets)", _outputFolder);

        EditorGUILayout.Space(8);
        if (GUILayout.Button("Run benchmark & export CSV", GUILayout.Height(32)))
            RunAndExport();

        EditorGUILayout.HelpBox(
            "Uses Profiler markers PW.* (enable CPU + Memory in the Profiler when sampling Play Mode or deep profile). " +
            "GC column = main-thread GC.GetAllocatedBytesForCurrentThread delta per iteration.",
            MessageType.Info);
    }

    void RunAndExport()
    {
        var coord = new ChunkCoord(_chunkCoordX, _chunkCoordY);
        int totalSteps = _seedCount * 2;
        int step = 0;

        try
        {
            NoiseBenchmarkHarness.CollectForStableGcBaseline();

            if (_warmupPerBackend > 0)
            {
                EditorUtility.DisplayProgressBar("Noise benchmark", "Warmup (JIT/Burst)…", 0f);
                NoiseBenchmarkHarness.Warmup(coord, _chunkSize, _warmupPerBackend, _scale, _octaves, _persistence, _lacunarity);
            }

            NoiseBenchmarkHarness.CollectForStableGcBaseline();

            var sb = new StringBuilder(8192);
            sb.AppendLine(CsvHeader());

            double sumSimplexJob = 0d, sumPerlinJob = 0d;
            double sumSimplexTotal = 0d, sumPerlinTotal = 0d;
            long sumSimplexGc = 0L, sumPerlinGc = 0L;
            int n = 0;

            for (int i = 0; i < _seedCount; i++)
            {
                int seed = _startSeed + i;

                EditorUtility.DisplayProgressBar("Noise benchmark",
                    $"Seed {seed} — Simplex…", (float)step++ / totalSteps);

                var sSimplex = NoiseBenchmarkHarness.MeasureOne(
                    coord, _chunkSize, seed, _scale, _octaves, _persistence, _lacunarity,
                    NoiseBackend.SimplexFbm, _includeManagedCopy);
                AppendRow(sb, seed, NoiseBackend.SimplexFbm, sSimplex);
                sumSimplexJob += sSimplex.JobCompleteMs;
                sumSimplexTotal += sSimplex.TotalWallMs;
                if (sSimplex.GcAllocatedBytes >= 0) sumSimplexGc += sSimplex.GcAllocatedBytes;

                EditorUtility.DisplayProgressBar("Noise benchmark",
                    $"Seed {seed} — Classic Perlin…", (float)step++ / totalSteps);

                var sPerlin = NoiseBenchmarkHarness.MeasureOne(
                    coord, _chunkSize, seed, _scale, _octaves, _persistence, _lacunarity,
                    NoiseBackend.ClassicPerlinFbm, _includeManagedCopy);
                AppendRow(sb, seed, NoiseBackend.ClassicPerlinFbm, sPerlin);
                sumPerlinJob += sPerlin.JobCompleteMs;
                sumPerlinTotal += sPerlin.TotalWallMs;
                if (sPerlin.GcAllocatedBytes >= 0) sumPerlinGc += sPerlin.GcAllocatedBytes;

                n++;
            }

            sb.AppendLine();
            sb.AppendLine("# Summary (same seed count for each backend)");
            sb.AppendLine($"# SimplexFbm mean JobCompleteMs = {FormatMean(sumSimplexJob, n)}");
            sb.AppendLine($"# ClassicPerlinFbm mean JobCompleteMs = {FormatMean(sumPerlinJob, n)}");
            sb.AppendLine($"# SimplexFbm mean TotalWallMs = {FormatMean(sumSimplexTotal, n)}");
            sb.AppendLine($"# ClassicPerlinFbm mean TotalWallMs = {FormatMean(sumPerlinTotal, n)}");
            if (sumSimplexGc >= 0 && sumPerlinGc >= 0)
            {
                sb.AppendLine($"# SimplexFbm mean GcAllocatedBytes = {FormatMeanLong(sumSimplexGc, n)}");
                sb.AppendLine($"# ClassicPerlinFbm mean GcAllocatedBytes = {FormatMeanLong(sumPerlinGc, n)}");
            }

            string dir = Path.Combine(Application.dataPath, _outputFolder.Trim('/', '\\', ' '));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"noise_benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log($"[NoiseBenchmark] Wrote {path} ({n} seeds × 2 backends). " +
                      $"Mean JobCompleteMs: Simplex {FormatMean(sumSimplexJob, n)} ms, " +
                      $"Perlin {FormatMean(sumPerlinJob, n)} ms.");
            EditorUtility.RevealInFinder(path);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static string CsvHeader()
    {
        return string.Join(",",
            "ChunkSize",
            "ChunkCoordX",
            "ChunkCoordY",
            "Scale",
            "Octaves",
            "Persistence",
            "Lacunarity",
            "IncludeManagedCopy",
            "Seed",
            "NoiseBackend",
            "JobCompleteMs",
            "CopyToManagedMs",
            "TotalWallMs",
            "GcAllocatedBytes");
    }

    void AppendRow(StringBuilder sb, int seed, NoiseBackend backend, NoiseBenchmarkHarness.Sample s)
    {
        var inv = CultureInfo.InvariantCulture;
        string copy = s.CopyToManagedMs.ToString("F6", inv);
        string job = s.JobCompleteMs.ToString("F6", inv);
        string tot = s.TotalWallMs.ToString("F6", inv);
        string gc = s.GcAllocatedBytes < 0 ? "" : s.GcAllocatedBytes.ToString(inv);

        sb.AppendLine(string.Join(",",
            _chunkSize.ToString(inv),
            _chunkCoordX.ToString(inv),
            _chunkCoordY.ToString(inv),
            _scale.ToString("F4", inv),
            _octaves.ToString(inv),
            _persistence.ToString("F4", inv),
            _lacunarity.ToString("F4", inv),
            _includeManagedCopy ? "1" : "0",
            seed.ToString(inv),
            backend.ToString(),
            job,
            copy,
            tot,
            gc));
    }

    static string FormatMean(double sum, int count) =>
        count <= 0 ? "n/a" : (sum / count).ToString("F6", CultureInfo.InvariantCulture);

    static string FormatMeanLong(long sum, int count) =>
        count <= 0 ? "n/a" : ((double)sum / count).ToString("F2", CultureInfo.InvariantCulture);
}
#endif
