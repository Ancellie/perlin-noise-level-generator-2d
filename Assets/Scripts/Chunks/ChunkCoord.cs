using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[Serializable]
public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    public readonly int X;
    public readonly int Y;
    public ChunkCoord(int x, int y) { X = x; Y = y; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ChunkCoord other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is ChunkCoord c && Equals(c);

    public override int GetHashCode()
    {
        uint ux = (uint)(X >= 0 ? 2 * X : -2 * X - 1);
        uint uy = (uint)(Y >= 0 ? 2 * Y : -2 * Y - 1);
        uint p  = ux >= uy ? ux * ux + ux + uy : ux + uy * uy;
        return (int)p;
    }

    public Vector2Int WorldOrigin(int chunkSize) => new Vector2Int(X * chunkSize, Y * chunkSize);
    public Vector2 WorldCentre(int chunkSize) => new Vector2((X + 0.5f) * chunkSize, (Y + 0.5f) * chunkSize);

    public static ChunkCoord FromWorldPos(Vector2 worldPos, int chunkSize) =>
        new ChunkCoord(Mathf.FloorToInt(worldPos.x / chunkSize), Mathf.FloorToInt(worldPos.y / chunkSize));

    public static ChunkCoord FromWorldTile(Vector2Int tilePos, int chunkSize) =>
        new ChunkCoord(Mathf.FloorToInt((float)tilePos.x / chunkSize), Mathf.FloorToInt((float)tilePos.y / chunkSize));

    public float DistanceTo(ChunkCoord other) =>
        Mathf.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));

    public int ChebyshevDistanceTo(ChunkCoord other) =>
        Mathf.Max(Mathf.Abs(X - other.X), Mathf.Abs(Y - other.Y));

    public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
    public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
    public override string ToString() => $"Chunk({X},{Y})";
}