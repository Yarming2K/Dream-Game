using System;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>
/// A grid of ground heights (the island), with exact ray casts against its triangles. Shells, wheels and
/// gunners' line-of-sight checks test the ground here instead of asking the physics engine, which is many
/// times faster when thousands of shells are in the air.
/// <para>Samples are <see cref="Spacing"/> apart, sample (0, 0) is at <see cref="Origin"/> (x, z). Each square
/// is split into two triangles along the diagonal from (x, z) to (x+1, z+1), the same way the physics engine
/// splits its height fields. Beyond the edges the ground is a flat floor at <see cref="Floor"/>.</para>
/// </summary>
public sealed class HeightField
{
    public readonly int Width, Depth;
    public readonly float Spacing;
    public readonly Vector2 Origin;
    public readonly float[] Heights;
    public readonly float Floor;

    /// <summary>The highest point anywhere (it rises when earth is piled up; see <see cref="NoteHeight"/>).</summary>
    public float MaxHeight { get; private set; }

    public HeightField(int width, int depth, float spacing, Vector2 origin, float[] heights, float floor)
    {
        if (heights.Length != width * depth) throw new ArgumentException("heights must be width * depth");
        Width = width;
        Depth = depth;
        Spacing = spacing;
        Origin = origin;
        Heights = heights;
        Floor = floor;
        float max = floor;
        foreach (float h in heights) max = MathF.Max(max, h);
        MaxHeight = max;
    }

    private float H(int x, int z) => Heights[z * Width + x];

    /// <summary>Call after raising a sample, so ray casts still know how high the ground can reach.</summary>
    public void NoteHeight(float h)
    {
        if (h > MaxHeight) MaxHeight = h;
    }

    /// <summary>Ground height straight below or above a point (the floor outside the grid).</summary>
    public float HeightAt(float x, float z) => Sample(x, z, out _);

    /// <summary>Ground height and the upward normal of the triangle there.</summary>
    public float Sample(float x, float z, out Vector3 normal)
    {
        normal = Vector3.UnitY;
        float gx = (x - Origin.X) / Spacing, gz = (z - Origin.Y) / Spacing;
        if (gx < 0 || gz < 0 || gx > Width - 1 || gz > Depth - 1) return Floor;
        int cx = Math.Min((int)gx, Width - 2), cz = Math.Min((int)gz, Depth - 2);
        float fx = gx - cx, fz = gz - cz;
        float a = H(cx, cz), b = H(cx + 1, cz), c = H(cx, cz + 1), d = H(cx + 1, cz + 1);
        float h, dx, dz;
        if (fz >= fx)
        {
            // Triangle a, c, d.
            dx = d - c;
            dz = c - a;
            h = a + dz * fz + dx * fx;
        }
        else
        {
            // Triangle a, d, b.
            dx = b - a;
            dz = d - b;
            h = a + dx * fx + dz * fz;
        }

        normal = Vector3.Normalize(new Vector3(-dx / Spacing, 1f, -dz / Spacing));
        return h;
    }

    /// <summary>
    /// The first point where the segment from <paramref name="from"/> to <paramref name="to"/> meets the
    /// ground. <paramref name="dist"/> is in meters from <paramref name="from"/>.
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out float dist, out Vector3 normal)
    {
        dist = float.PositiveInfinity;
        normal = Vector3.UnitY;
        Vector3 seg = to - from;
        float len = seg.Length();
        if (len < 1e-6f) return false;

        // The floor beyond (and under) the grid.
        float best = float.PositiveInfinity;
        if (from.Y >= Floor && to.Y < Floor) best = (from.Y - Floor) / (from.Y - to.Y);

        // Only the part of the segment low enough to touch a hill, and over the grid, needs walking.
        float u0 = 0f, u1 = MathF.Min(1f, best);
        if (seg.Y < 0)
        {
            if (to.Y > MaxHeight) u1 = -1f;
            else if (from.Y > MaxHeight) u0 = (MaxHeight - from.Y) / seg.Y;
        }
        else if (from.Y > MaxHeight) u1 = -1f;
        else if (seg.Y > 0 && to.Y > MaxHeight) u1 = MathF.Min(u1, (MaxHeight - from.Y) / seg.Y);

        float ox = (from.X - Origin.X) / Spacing, oz = (from.Z - Origin.Y) / Spacing;
        float sx = seg.X / Spacing, sz = seg.Z / Spacing;
        if (u0 <= u1 && Clip(ox, sx, Width - 1, ref u0, ref u1) && Clip(oz, sz, Depth - 1, ref u0, ref u1))
        {
            float hit = Walk(from, seg, ox, oz, sx, sz, u0, u1, ref normal);
            if (hit < best) best = hit;
            else if (best < float.PositiveInfinity) normal = Vector3.UnitY;
        }

        if (best > 1f) return false;
        dist = best * len;
        return true;
    }

    private static bool Clip(float o, float d, float max, ref float u0, ref float u1)
    {
        if (MathF.Abs(d) < 1e-9f) return o >= 0 && o <= max;
        float a = (0 - o) / d, b = (max - o) / d;
        if (a > b) (a, b) = (b, a);
        u0 = MathF.Max(u0, a);
        u1 = MathF.Min(u1, b);
        return u0 <= u1;
    }

    /// <summary>Steps through the grid squares the segment crosses (in order), testing each one's two triangles.</summary>
    private float Walk(Vector3 from, Vector3 seg, float ox, float oz, float sx, float sz, float u0, float u1, ref Vector3 normal)
    {
        float px = ox + sx * u0, pz = oz + sz * u0;
        int cx = Math.Clamp((int)MathF.Floor(px), 0, Width - 2);
        int cz = Math.Clamp((int)MathF.Floor(pz), 0, Depth - 2);
        int stepX = sx > 0 ? 1 : sx < 0 ? -1 : 0, stepZ = sz > 0 ? 1 : sz < 0 ? -1 : 0;
        float dux = stepX != 0 ? MathF.Abs(1f / sx) : float.PositiveInfinity;
        float duz = stepZ != 0 ? MathF.Abs(1f / sz) : float.PositiveInfinity;
        float nextX = stepX > 0 ? (cx + 1 - ox) / sx : stepX < 0 ? (cx - ox) / sx : float.PositiveInfinity;
        float nextZ = stepZ > 0 ? (cz + 1 - oz) / sz : stepZ < 0 ? (cz - oz) / sz : float.PositiveInfinity;

        float u = u0;
        int guard = Width + Depth + 4;
        while (u <= u1 && guard-- > 0)
        {
            float uOut = MathF.Min(MathF.Min(nextX, nextZ), u1);
            // The lowest the segment gets over this square; if that's above every corner, it can't touch.
            float yLow = from.Y + seg.Y * (seg.Y >= 0 ? u : uOut);
            float a = H(cx, cz), b = H(cx + 1, cz), c = H(cx, cz + 1), d = H(cx + 1, cz + 1);
            float top = MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
            if (yLow <= top + 1e-4f)
            {
                float x0 = Origin.X + cx * Spacing, z0 = Origin.Y + cz * Spacing;
                var pa = new Vector3(x0, a, z0);
                var pb = new Vector3(x0 + Spacing, b, z0);
                var pc = new Vector3(x0, c, z0 + Spacing);
                var pd = new Vector3(x0 + Spacing, d, z0 + Spacing);
                float best = float.PositiveInfinity;
                Vector3 n = Vector3.UnitY;
                if (Triangle(from, seg, pa, pc, pd, out float t1) && t1 < best)
                {
                    best = t1;
                    n = Vector3.Cross(pc - pa, pd - pa);
                }

                if (Triangle(from, seg, pa, pd, pb, out float t2) && t2 < best)
                {
                    best = t2;
                    n = Vector3.Cross(pd - pa, pb - pa);
                }

                if (best <= u1 + 1e-6f && best >= u0 - 1e-6f)
                {
                    if (n.Y < 0) n = -n;
                    normal = Vector3.Normalize(n);
                    return best;
                }
            }

            if (uOut >= u1) break;
            if (nextX < nextZ)
            {
                cx += stepX;
                nextX += dux;
                if (cx < 0 || cx > Width - 2) break;
            }
            else
            {
                cz += stepZ;
                nextZ += duz;
                if (cz < 0 || cz > Depth - 2) break;
            }

            u = uOut;
        }

        return float.PositiveInfinity;
    }

    /// <summary>Segment against a triangle, both sides (Möller–Trumbore). <paramref name="u"/> is the fraction along the segment.</summary>
    private static bool Triangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float u)
    {
        u = 0;
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        Vector3 s = o - a;
        float bu = Vector3.Dot(s, p) * inv;
        if (bu < -1e-5f || bu > 1f + 1e-5f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float bv = Vector3.Dot(d, q) * inv;
        if (bv < -1e-5f || bu + bv > 1f + 1e-5f) return false;
        u = Vector3.Dot(e2, q) * inv;
        return u >= 0f && u <= 1f;
    }
}
