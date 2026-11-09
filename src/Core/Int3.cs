using System;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>Integer grid coordinate. +X is right, +Y is up, -Z is the front of a vehicle.</summary>
public readonly struct Int3 : IEquatable<Int3>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public Int3(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Int3 Zero = new(0, 0, 0);
    public static readonly Int3 Right = new(1, 0, 0);
    public static readonly Int3 Left = new(-1, 0, 0);
    public static readonly Int3 Up = new(0, 1, 0);
    public static readonly Int3 Down = new(0, -1, 0);
    public static readonly Int3 Back = new(0, 0, 1);
    public static readonly Int3 Forward = new(0, 0, -1);

    /// <summary>The six face directions.</summary>
    public static readonly Int3[] Directions = { Right, Left, Up, Down, Back, Forward };

    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator -(Int3 a) => new(-a.X, -a.Y, -a.Z);
    public static Int3 operator *(Int3 a, int s) => new(a.X * s, a.Y * s, a.Z * s);
    public static bool operator ==(Int3 a, Int3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Int3 a, Int3 b) => !(a == b);

    public static Int3 Min(Int3 a, Int3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Int3 Max(Int3 a, Int3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    public static Int3 Round(Vector3 v) =>
        new((int)MathF.Round(v.X), (int)MathF.Round(v.Y), (int)MathF.Round(v.Z));

    /// <summary>The face direction closest to <paramref name="v"/>. Ties prefer Y, then X.</summary>
    public static Int3 DominantAxis(Vector3 v)
    {
        float ax = MathF.Abs(v.X), ay = MathF.Abs(v.Y), az = MathF.Abs(v.Z);
        const float tieSlack = 1e-3f;
        if (ay + tieSlack >= ax && ay + tieSlack >= az) return v.Y >= 0 ? Up : Down;
        if (ax + tieSlack >= az) return v.X >= 0 ? Right : Left;
        return v.Z >= 0 ? Back : Forward;
    }

    public int ManhattanLength => Math.Abs(X) + Math.Abs(Y) + Math.Abs(Z);

    public Vector3 ToVector3() => new(X, Y, Z);

    public bool Equals(Int3 other) => this == other;
    public override bool Equals(object obj) => obj is Int3 other && this == other;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X}, {Y}, {Z})";
}
