using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Deterministic, headless noise benchmark (no Tilemap) for comparative studies.
/// Uses the same <see cref="NoiseJobScheduler"/> path as <see cref="ChunkStreamer"/>.
/// </summary>
public static class NoiseBenchmarkHarness
{
    public readonly struct Sample
    {
        /// <summary>Wall time for <see cref="JobHandle.Complete"/> (worker + sync).</summary>
        public readonly double JobCompleteMs;

        /// <summary>Three <see cref="NativeArray{T}.CopyTo"/> into reused buffers (production-equivalent).</summary>
        public readonly double CopyToManagedMs;

        /// <summary>Schedule + Complete + optional copy + dispose (main-thread wall clock).</summary>
        public readonly double TotalWallMs;

        /// <summary>
        /// Delta of <see cref="GC.GetAllocatedBytesForCurrentThread"/> over the whole sample.
        /// Worker-thread managed allocations are not included; Burst noise work allocates none.
        /// </summary>
        public readonly long GcAllocatedBytes;

        public Sample(double jobCompleteMs, double copyToManagedMs, double totalWallMs, long gcAllocatedBytes)
        {
            JobCompleteMs = jobCompleteMs;
            CopyToManagedMs = copyToManagedMs;
            TotalWallMs = totalWallMs;
            GcAllocatedBytes = gcAllocatedBytes;
        }
    }

    static float[] _benchHeight, _benchMoisture, _benchTemp;

    static void EnsureBenchCopyBuffers(int length)
    {
        if (_benchHeight == null || _benchHeight.Length != length)
        {
            _benchHeight   = new float[length];
            _benchMoisture = new float[length];
            _benchTemp     = new float[length];
        }
    }

    /// <param name="includeManagedCopy">If true, runs the same three <see cref="NativeArray{T}.CopyTo"/> copies as <see cref="ChunkStreamer.PollCompletedJobs"/>.</param>
    public static Sample MeasureOne(
        ChunkCoord coord,
        int chunkSize,
        int seed,
        float scale,
        int octaves,
        float persistence,
        float lacunarity,
        NoiseBackend backend,
        bool includeManagedCopy)
    {
        long gcBefore = TryGetThreadGcBytes();

        var totalSw = Stopwatch.StartNew();

        JobHandle handle;
        NativeArray<float> heightOut, moistureOut, tempOut;
        NativeArray<float2> elevOff, moistOff, tmpOff;

        using (GenerationProfilerMarkers.NoiseJobSchedule.Auto())
        {
            handle = NoiseJobScheduler.Schedule(
                coord, chunkSize, seed, scale, octaves, persistence, lacunarity, backend,
                out heightOut, out moistureOut, out tempOut,
                out elevOff, out moistOff, out tmpOff);
        }

        double jobMs;
        using (GenerationProfilerMarkers.NoiseJobWaitComplete.Auto())
        {
            var jobSw = Stopwatch.StartNew();
            handle.Complete();
            jobSw.Stop();
            jobMs = jobSw.Elapsed.TotalMilliseconds;
        }

        double copyMs = 0d;
        if (includeManagedCopy)
        {
            using (GenerationProfilerMarkers.NoiseJobCopyNativeToManaged.Auto())
            {
                var copySw = Stopwatch.StartNew();
                EnsureBenchCopyBuffers(heightOut.Length);
                heightOut.CopyTo(_benchHeight);
                moistureOut.CopyTo(_benchMoisture);
                tempOut.CopyTo(_benchTemp);
                copySw.Stop();
                copyMs = copySw.Elapsed.TotalMilliseconds;
            }
        }

        using (GenerationProfilerMarkers.NoiseJobNativeDispose.Auto())
        {
            heightOut.Dispose();
            moistureOut.Dispose();
            tempOut.Dispose();
            elevOff.Dispose();
            moistOff.Dispose();
            tmpOff.Dispose();
        }

        totalSw.Stop();

        long gcAfter = TryGetThreadGcBytes();
        long gcDelta = (gcBefore >= 0 && gcAfter >= 0) ? gcAfter - gcBefore : -1L;

        return new Sample(jobMs, copyMs, totalSw.Elapsed.TotalMilliseconds, gcDelta);
    }

    /// <summary>Discard a few runs so Burst/JIT stabilization does not skew first samples.</summary>
    public static void Warmup(
        ChunkCoord coord,
        int chunkSize,
        int iterationsPerBackend,
        float scale,
        int octaves,
        float persistence,
        float lacunarity)
    {
        int seed = 12345;
        for (int i = 0; i < iterationsPerBackend; i++)
        {
            MeasureOne(coord, chunkSize, seed + i, scale, octaves, persistence, lacunarity,
                NoiseBackend.SimplexFbm, includeManagedCopy: true);
            MeasureOne(coord, chunkSize, seed + i, scale, octaves, persistence, lacunarity,
                NoiseBackend.ClassicPerlinFbm, includeManagedCopy: true);
        }
    }

    /// <summary>Optional: call once before a batch so GC deltas are less noisy (adds pause).</summary>
    public static void CollectForStableGcBaseline()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static long TryGetThreadGcBytes()
    {
        try
        {
            return GC.GetAllocatedBytesForCurrentThread();
        }
        catch (NotImplementedException)
        {
            return -1L;
        }
    }
}
