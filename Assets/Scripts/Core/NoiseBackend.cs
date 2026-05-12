/// <summary>
/// Terrain height/moisture/temperature use the same fBM stack; this selects the
/// gradient-noise primitive (Burst-compiled in <see cref="TerrainFbm"/>).
/// </summary>
public enum NoiseBackend
{
    /// <summary>Unity.Mathematics gradient Simplex noise (<c>noise.snoise</c>).</summary>
    SimplexFbm = 0,

    /// <summary>Classic Perlin-style gradient noise (<c>noise.cnoise</c>) — fair Burst baseline vs Simplex.</summary>
    ClassicPerlinFbm = 1,
}
