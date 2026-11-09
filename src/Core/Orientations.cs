using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>
/// The 24 axis-aligned rotations a block can have, stored by index (0 = no rotation).
/// Each is an integer matrix M with grid = M * local, so its columns are where the block's
/// local X, Y and Z axes point in the grid.
/// </summary>
public static class Orientations
{
    public const int Count = 24;

    // Row-major 3x3 matrices.
    private static readonly int[][] Matrices;
    private static readonly Dictionary<long, int> Lookup = new();

    private static readonly int[] RotX90 = { 1, 0, 0, 0, 0, -1, 0, 1, 0 };
    private static readonly int[] RotY90 = { 0, 0, 1, 0, 1, 0, -1, 0, 0 };
    private static readonly int[] RotZ90 = { 0, -1, 0, 1, 0, 0, 0, 0, 1 };

    static Orientations()
    {
        var list = new List<int[]>();
        var queue = new Queue<int[]>();
        int[] identity = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        Add(identity);
        queue.Enqueue(identity);
        while (queue.Count > 0)
        {
            int[] m = queue.Dequeue();
            foreach (int[] r in new[] { RotX90, RotY90, RotZ90 })
            {
                int[] next = Multiply(r, m);
                if (Lookup.ContainsKey(Key(next))) continue;
                Add(next);
                queue.Enqueue(next);
            }
        }

        Matrices = list.ToArray();
        if (Matrices.Length != Count) throw new InvalidOperationException("Expected 24 orientations.");

        void Add(int[] m)
        {
            Lookup[Key(m)] = list.Count;
            list.Add(m);
        }
    }

    private static long Key(int[] m)
    {
        long key = 0;
        foreach (int v in m) key = key * 3 + (v + 1);
        return key;
    }

    private static int[] Multiply(int[] a, int[] b)
    {
        var r = new int[9];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            int sum = 0;
            for (int k = 0; k < 3; k++) sum += a[i * 3 + k] * b[k * 3 + j];
            r[i * 3 + j] = sum;
        }
        return r;
    }

    private static int IndexOf(int[] m) => Lookup[Key(m)];

    public static bool IsValid(int o) => o >= 0 && o < Count;

    /// <summary>Row-major copy of the matrix.</summary>
    public static int[] Matrix(int o) => (int[])Matrices[o].Clone();

    public static Int3 Apply(int o, Int3 v)
    {
        int[] m = Matrices[o];
        return new Int3(
            m[0] * v.X + m[1] * v.Y + m[2] * v.Z,
            m[3] * v.X + m[4] * v.Y + m[5] * v.Z,
            m[6] * v.X + m[7] * v.Y + m[8] * v.Z);
    }

    public static Vector3 Apply(int o, Vector3 v)
    {
        int[] m = Matrices[o];
        return new Vector3(
            m[0] * v.X + m[1] * v.Y + m[2] * v.Z,
            m[3] * v.X + m[4] * v.Y + m[5] * v.Z,
            m[6] * v.X + m[7] * v.Y + m[8] * v.Z);
    }

    /// <summary>Grid direction to block-local direction (transpose of the matrix).</summary>
    public static Vector3 ApplyInverse(int o, Vector3 v)
    {
        int[] m = Matrices[o];
        return new Vector3(
            m[0] * v.X + m[3] * v.Y + m[6] * v.Z,
            m[1] * v.X + m[4] * v.Y + m[7] * v.Z,
            m[2] * v.X + m[5] * v.Y + m[8] * v.Z);
    }

    /// <summary>Where the block's local axis points in the grid.</summary>
    public static Int3 Forward(int o) => Apply(o, Int3.Forward);
    public static Int3 Up(int o) => Apply(o, Int3.Up);
    public static Int3 Right(int o) => Apply(o, Int3.Right);

    /// <summary>Index of the rotation "first a, then b" applied on top: M_b * M_a.</summary>
    public static int Then(int a, int b) => IndexOf(Multiply(Matrices[b], Matrices[a]));

    public static int RotateAroundX(int o, int quarterTurns) => Rotate(o, RotX90, quarterTurns);
    public static int RotateAroundY(int o, int quarterTurns) => Rotate(o, RotY90, quarterTurns);
    public static int RotateAroundZ(int o, int quarterTurns) => Rotate(o, RotZ90, quarterTurns);

    private static int Rotate(int o, int[] r, int quarterTurns)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        int[] m = Matrices[o];
        for (int i = 0; i < turns; i++) m = Multiply(r, m);
        return IndexOf(m);
    }

    /// <summary>The orientation of the block's mirror image across the X = 0 plane.</summary>
    public static int MirrorX(int o)
    {
        int[] m = Matrices[o];
        var r = new int[9];
        int[] s = { -1, 1, 1 };
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            r[i * 3 + j] = s[i] * m[i * 3 + j] * s[j];
        return IndexOf(r);
    }
}
