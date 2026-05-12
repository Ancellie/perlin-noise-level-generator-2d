using Unity.Profiling;

/// <summary>
/// Central <see cref="ProfilerMarker"/> names for thesis profiling (CPU/Memory modules).
/// Prefix <c>PW.</c> keeps them grouped in the Profiler hierarchy.
/// </summary>
public static class GenerationProfilerMarkers
{
    public static readonly ProfilerMarker NoiseJobSchedule =
        new ProfilerMarker("PW.NoiseJob.Schedule");

    /// <summary>Waits for worker threads; includes Burst execution time for the scheduled job.</summary>
    public static readonly ProfilerMarker NoiseJobWaitComplete =
        new ProfilerMarker("PW.NoiseJob.WaitComplete");

    public static readonly ProfilerMarker NoiseJobCopyNativeToManaged =
        new ProfilerMarker("PW.NoiseJob.CopyNativeToManaged");

    public static readonly ProfilerMarker NoiseJobNativeDispose =
        new ProfilerMarker("PW.NoiseJob.NativeDispose");

    public static readonly ProfilerMarker ChunkFlushTilemap =
        new ProfilerMarker("PW.Chunk.FlushTilemap");

    public static readonly ProfilerMarker ChunkFlushSetTilesBlock =
        new ProfilerMarker("PW.Chunk.SetTilesBlock");

    public static readonly ProfilerMarker BiomeResolve =
        new ProfilerMarker("PW.Biome.Resolve");
}
