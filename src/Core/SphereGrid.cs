using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>
/// A quick way to find which vehicles, structures or wrecks a shell's path or a blast could touch, without
/// testing every one of them: each thing is filed, as a sphere, under the squares of a flat map grid that it
/// covers, and a query only looks at the squares it passes over. Rebuilt every physics step, which is cheap.
/// <para>Things far bigger than a square (a battleship) are kept on a short list that every query checks.</para>
/// </summary>
public sealed class SphereGrid
{
    public readonly float CellSize;

    /// <summary>Queries may ask for anything within this much of a sphere's edge.</summary>
    public readonly float MaxPad;

    private readonly Dictionary<long, List<int>> _cells = new();
    private readonly Stack<List<int>> _pool = new();
    private readonly List<int> _large = new();
    private readonly List<Vector3> _centers = new();
    private readonly List<float> _radii = new();
    private int[] _stamp = new int[64];
    private int _query;

    public SphereGrid(float cellSize = 48f, float maxPad = 8f)
    {
        CellSize = cellSize;
        MaxPad = maxPad;
    }

    public int Count => _centers.Count;
    public Vector3 Center(int id) => _centers[id];
    public float Radius(int id) => _radii[id];

    public void Clear()
    {
        foreach (List<int> list in _cells.Values)
        {
            list.Clear();
            _pool.Push(list);
        }

        _cells.Clear();
        _large.Clear();
        _centers.Clear();
        _radii.Clear();
    }

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    private int CellOf(float v) => (int)MathF.Floor(v / CellSize);

    /// <summary>Files a sphere and returns its number (numbers count up from 0 after each <see cref="Clear"/>).</summary>
    public int Add(Vector3 center, float radius)
    {
        int id = _centers.Count;
        _centers.Add(center);
        _radii.Add(radius);
        float r = radius + MaxPad;
        int x0 = CellOf(center.X - r), x1 = CellOf(center.X + r);
        int z0 = CellOf(center.Z - r), z1 = CellOf(center.Z + r);
        if ((x1 - x0 + 1) * (z1 - z0 + 1) > 36)
        {
            _large.Add(id);
            return id;
        }

        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            long k = Key(x, z);
            if (!_cells.TryGetValue(k, out List<int> list))
            {
                list = _pool.Count > 0 ? _pool.Pop() : new List<int>(4);
                _cells[k] = list;
            }

            list.Add(id);
        }

        return id;
    }

    private void BeginQuery()
    {
        if (_stamp.Length < _centers.Count) Array.Resize(ref _stamp, Math.Max(_centers.Count, _stamp.Length * 2));
        if (++_query == int.MaxValue)
        {
            Array.Clear(_stamp);
            _query = 1;
        }
    }

    /// <summary>Distance from a point to the segment a→b.</summary>
    public static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 > 1e-12f ? Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
        return Vector3.Distance(a + ab * t, p);
    }

    private void Test(int id, Vector3 from, Vector3 to, float pad, List<int> result)
    {
        if (_stamp[id] == _query) return;
        _stamp[id] = _query;
        if (SegmentDistance(from, to, _centers[id]) <= _radii[id] + pad) result.Add(id);
    }

    /// <summary>
    /// Everything whose sphere the segment passes within <paramref name="pad"/> of (at most <see cref="MaxPad"/>).
    /// Results are added to <paramref name="result"/> in no particular order.
    /// </summary>
    public void QuerySegment(Vector3 from, Vector3 to, float pad, List<int> result)
    {
        BeginQuery();
        pad = MathF.Min(pad, MaxPad);
        foreach (int id in _large) Test(id, from, to, pad, result);
        if (_cells.Count == 0) return;

        // Walk the squares under the segment in order (a flat grid walk).
        float ox = from.X / CellSize, oz = from.Z / CellSize;
        float dx = (to.X - from.X) / CellSize, dz = (to.Z - from.Z) / CellSize;
        int cx = (int)MathF.Floor(ox), cz = (int)MathF.Floor(oz);
        int ex = (int)MathF.Floor(ox + dx), ez = (int)MathF.Floor(oz + dz);
        int stepX = Math.Sign(dx), stepZ = Math.Sign(dz);
        float tdx = stepX != 0 ? MathF.Abs(1f / dx) : float.PositiveInfinity;
        float tdz = stepZ != 0 ? MathF.Abs(1f / dz) : float.PositiveInfinity;
        float nx = stepX > 0 ? (cx + 1 - ox) / dx : stepX < 0 ? (cx - ox) / dx : float.PositiveInfinity;
        float nz = stepZ > 0 ? (cz + 1 - oz) / dz : stepZ < 0 ? (cz - oz) / dz : float.PositiveInfinity;
        int guard = Math.Abs(ex - cx) + Math.Abs(ez - cz) + 2;
        while (guard-- > 0)
        {
            if (_cells.TryGetValue(Key(cx, cz), out List<int> list))
                foreach (int id in list) Test(id, from, to, pad, result);
            if (cx == ex && cz == ez) break;
            if (nx < nz)
            {
                cx += stepX;
                nx += tdx;
            }
            else
            {
                cz += stepZ;
                nz += tdz;
            }
        }
    }

    /// <summary>Everything whose sphere comes within <paramref name="radius"/> of a point.</summary>
    public void QuerySphere(Vector3 center, float radius, List<int> result)
    {
        BeginQuery();
        foreach (int id in _large)
        {
            _stamp[id] = _query;
            if (Vector3.Distance(center, _centers[id]) <= _radii[id] + radius) result.Add(id);
        }

        if (_cells.Count == 0) return;
        float reach = MathF.Max(0f, radius - MaxPad);
        int x0 = CellOf(center.X - reach), x1 = CellOf(center.X + reach);
        int z0 = CellOf(center.Z - reach), z1 = CellOf(center.Z + reach);
        if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > _cells.Count)
        {
            // A huge radius: cheaper to check everything.
            for (int id = 0; id < _centers.Count; id++)
            {
                if (_stamp[id] == _query) continue;
                _stamp[id] = _query;
                if (Vector3.Distance(center, _centers[id]) <= _radii[id] + radius) result.Add(id);
            }

            return;
        }

        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            if (!_cells.TryGetValue(Key(x, z), out List<int> list)) continue;
            foreach (int id in list)
            {
                if (_stamp[id] == _query) continue;
                _stamp[id] = _query;
                if (Vector3.Distance(center, _centers[id]) <= _radii[id] + radius) result.Add(id);
            }
        }
    }
}
