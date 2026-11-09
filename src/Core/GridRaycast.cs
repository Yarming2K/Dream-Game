using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

public struct RayCellHit
{
    public Int3 Cell;

    /// <summary>Distance along the ray, in cell units, where the ray enters and leaves the block's solid part.</summary>
    public float TEnter;
    public float TExit;

    /// <summary>Outward surface normal where the ray entered, in grid space.</summary>
    public Vector3 Normal;
}

/// <summary>
/// Walks a ray through a block grid cell by cell (Amanatides–Woo) and finds exactly where it enters each
/// block's shape. Coordinates are in cell units with cell centers on integers, so cell (0,0,0) spans -0.5..0.5.
/// </summary>
public static class GridRaycast
{
    private static readonly float InvSqrt2 = 1f / MathF.Sqrt(2f);

    /// <summary>
    /// Collects every block the ray passes through, nearest first.
    /// <paramref name="dir"/> must be normalized; <paramref name="maxT"/> is in cell units.
    /// </summary>
    public static void Cast(ICellShapeLookup grid, Int3 min, Int3 max, Vector3 origin, Vector3 dir, float maxT,
        List<RayCellHit> hits, bool firstOnly = false)
    {
        Vector3 q = origin + new Vector3(0.5f);
        float t0 = 0f, t1 = maxT;
        if (!ClipAxis(q.X, dir.X, min.X, max.X + 1, ref t0, ref t1)) return;
        if (!ClipAxis(q.Y, dir.Y, min.Y, max.Y + 1, ref t0, ref t1)) return;
        if (!ClipAxis(q.Z, dir.Z, min.Z, max.Z + 1, ref t0, ref t1)) return;

        Vector3 p = q + dir * (t0 + 1e-5f);
        int cx = Math.Clamp((int)MathF.Floor(p.X), min.X, max.X);
        int cy = Math.Clamp((int)MathF.Floor(p.Y), min.Y, max.Y);
        int cz = Math.Clamp((int)MathF.Floor(p.Z), min.Z, max.Z);

        int sx = Math.Sign(dir.X), sy = Math.Sign(dir.Y), sz = Math.Sign(dir.Z);
        float tMaxX = NextBoundary(q.X, dir.X, cx);
        float tMaxY = NextBoundary(q.Y, dir.Y, cy);
        float tMaxZ = NextBoundary(q.Z, dir.Z, cz);
        float dX = sx != 0 ? MathF.Abs(1f / dir.X) : float.PositiveInfinity;
        float dY = sy != 0 ? MathF.Abs(1f / dir.Y) : float.PositiveInfinity;
        float dZ = sz != 0 ? MathF.Abs(1f / dir.Z) : float.PositiveInfinity;

        float tCell = t0;
        int guard = 0;
        while (tCell <= t1 && guard++ < 4096)
        {
            var cell = new Int3(cx, cy, cz);
            if (grid.TryGetShape(cell, out BlockShape shape, out int orientation) &&
                IntersectBlock(shape, orientation, cell, origin, dir, out float tIn, out float tOut, out Vector3 n) &&
                tIn <= t1)
            {
                hits.Add(new RayCellHit { Cell = cell, TEnter = tIn, TExit = tOut, Normal = n });
                if (firstOnly) return;
            }

            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                tCell = tMaxX;
                tMaxX += dX;
                cx += sx;
                if (cx < min.X || cx > max.X) break;
            }
            else if (tMaxY < tMaxZ)
            {
                tCell = tMaxY;
                tMaxY += dY;
                cy += sy;
                if (cy < min.Y || cy > max.Y) break;
            }
            else
            {
                tCell = tMaxZ;
                tMaxZ += dZ;
                cz += sz;
                if (cz < min.Z || cz > max.Z) break;
            }
        }
    }

    private static bool ClipAxis(float q, float d, float lo, float hi, ref float t0, ref float t1)
    {
        if (MathF.Abs(d) < 1e-9f) return q >= lo && q <= hi;
        float ta = (lo - q) / d;
        float tb = (hi - q) / d;
        if (ta > tb) (ta, tb) = (tb, ta);
        t0 = MathF.Max(t0, ta);
        t1 = MathF.Min(t1, tb);
        return t0 <= t1;
    }

    private static float NextBoundary(float q, float d, int cell)
    {
        if (d > 0) return (cell + 1 - q) / d;
        if (d < 0) return (cell - q) / d;
        return float.PositiveInfinity;
    }

    /// <summary>
    /// Clips the ray against one block's convex shape. Returns where it enters and leaves, and the entry
    /// normal in grid space. A ray that starts inside reports TEnter = 0 and a normal facing back along the ray.
    /// </summary>
    public static bool IntersectBlock(BlockShape shape, int orientation, Int3 cell, Vector3 origin, Vector3 dir,
        out float tEnter, out float tExit, out Vector3 normal)
    {
        Vector3 o = Orientations.ApplyInverse(orientation, origin - cell.ToVector3());
        Vector3 d = Orientations.ApplyInverse(orientation, dir);

        tEnter = float.NegativeInfinity;
        tExit = float.PositiveInfinity;
        Vector3 nEnter = Vector3.Zero;

        // Cube faces: inside when n . x <= 0.5.
        if (!Clip(Vector3.UnitX, 0.5f, o, d, ref tEnter, ref tExit, ref nEnter) ||
            !Clip(-Vector3.UnitX, 0.5f, o, d, ref tEnter, ref tExit, ref nEnter) ||
            !Clip(Vector3.UnitY, 0.5f, o, d, ref tEnter, ref tExit, ref nEnter) ||
            !Clip(-Vector3.UnitY, 0.5f, o, d, ref tEnter, ref tExit, ref nEnter) ||
            !Clip(Vector3.UnitZ, 0.5f, o, d, ref tEnter, ref tExit, ref nEnter) ||
            !Clip(-Vector3.UnitZ, 0.5f, o, d, ref tEnter, ref tExit, ref nEnter))
        {
            normal = Vector3.Zero;
            return false;
        }

        // Wedge: also inside when y - z <= 0.
        if (shape == BlockShape.Wedge &&
            !Clip(new Vector3(0, InvSqrt2, -InvSqrt2), 0f, o, d, ref tEnter, ref tExit, ref nEnter))
        {
            normal = Vector3.Zero;
            return false;
        }

        if (tExit < 0)
        {
            normal = Vector3.Zero;
            return false;
        }

        if (tEnter < 0)
        {
            tEnter = 0;
            normal = -dir;
            return true;
        }

        normal = Orientations.Apply(orientation, nEnter);
        return true;
    }

    private static bool Clip(Vector3 n, float dist, Vector3 o, Vector3 d, ref float tEnter, ref float tExit,
        ref Vector3 nEnter)
    {
        float denom = Vector3.Dot(n, d);
        float num = dist - Vector3.Dot(n, o);
        if (MathF.Abs(denom) < 1e-9f) return num >= 0;
        float t = num / denom;
        if (denom < 0)
        {
            if (t > tEnter)
            {
                tEnter = t;
                nEnter = n;
            }
        }
        else if (t < tExit)
        {
            tExit = t;
        }

        return tEnter <= tExit;
    }
}
