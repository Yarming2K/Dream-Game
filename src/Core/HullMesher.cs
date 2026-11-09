using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>What the hull mesher needs to know about a grid (one section of it).</summary>
public interface IHullGrid
{
    /// <summary>The surface material of a cell (0 or more), or -1 if it isn't a plain surface block.</summary>
    int SurfaceAt(Int3 cell);

    /// <summary>Which block a cell belongs to: any number that is the same for every cell of one block.</summary>
    int PanelAt(Int3 cell);

    /// <summary>Whether the block in <paramref name="cell"/> fills its whole face on side <paramref name="dir"/> (a unit step).</summary>
    bool CoversFace(Int3 cell, Int3 dir);

    /// <summary>Whether anything is in the cell (used to darken corners).</summary>
    bool Occupied(Int3 cell);

    /// <summary>The chamfer on a material's outer edges, in meters.</summary>
    float BevelOf(int material);
}

/// <summary>
/// Turns plain armor into continuous hull surfaces. Faces between neighbors are dropped, so a wall of
/// blocks reads as one plate; only the edges where the surface turns outward get a chamfer to catch the
/// light; corners and creases are shaded darker where neighbors crowd them (ambient occlusion); and each
/// block's panel is tinted very slightly differently so plating still shows. Triangles wind counter-clockwise
/// seen from outside.
/// </summary>
public static class HullMesher
{
    public sealed class Surface
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();

        /// <summary>Per-vertex brightness (corner shading times panel tint), around 0.5 to 1.05.</summary>
        public readonly List<float> Shade = new();
        public readonly List<int> Indices = new();
        public int Triangles => Indices.Count / 3;
    }

    private static readonly Int3[] Dirs =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private static int DirIndex(Int3 d) => d.X != 0 ? (d.X > 0 ? 0 : 1) : d.Y != 0 ? (d.Y > 0 ? 2 : 3) : d.Z > 0 ? 4 : 5;

    private static Vector3 V(Int3 c) => new(c.X, c.Y, c.Z);

    /// <summary>How light a block's panel is: a stable, slight variation per block, so plating shows.</summary>
    public static float PanelTint(int panel)
    {
        uint h = (uint)panel * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return 0.9f + (h & 1023) / 1023f * 0.1f;
    }

    private static float Hash01(int x, int y, int z)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791);
        h ^= h >> 13;
        h *= 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }

    /// <summary>
    /// Weathering: soft, large blotches of lighter and darker paint a few meters across, smooth over the
    /// surface wherever it is (value noise on the vertex position).
    /// </summary>
    public static float Weathering(Vector3 p)
    {
        Vector3 q = p * 0.33f;
        int x = (int)MathF.Floor(q.X), y = (int)MathF.Floor(q.Y), z = (int)MathF.Floor(q.Z);
        float fx = q.X - x, fy = q.Y - y, fz = q.Z - z;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        fz = fz * fz * (3 - 2 * fz);
        float L(float a, float b, float t) => a + (b - a) * t;
        float n = L(L(L(Hash01(x, y, z), Hash01(x + 1, y, z), fx), L(Hash01(x, y + 1, z), Hash01(x + 1, y + 1, z), fx), fy),
            L(L(Hash01(x, y, z + 1), Hash01(x + 1, y, z + 1), fx), L(Hash01(x, y + 1, z + 1), Hash01(x + 1, y + 1, z + 1), fx), fy), fz);
        return 0.86f + 0.14f * n;
    }

    /// <summary>
    /// Shading stays at or under 1: engines store vertex colors as 8-bit, so brighter would be clipped.
    /// Materials for hull surfaces are a touch lighter to make up for it (see <see cref="ShadeCompensation"/>).
    /// </summary>
    private static float Clamp1(float k) => MathF.Min(1f, k);

    /// <summary>How much lighter a hull surface's material should be than the block's color, to balance the average shading.</summary>
    public const float ShadeCompensation = 1.1f;

    /// <summary>Builds the surfaces for <paramref name="cells"/> (only these emit faces; neighbors outside are still looked at).</summary>
    public static Dictionary<int, Surface> Build(IEnumerable<Int3> cells, IHullGrid grid, float cellSize, bool weathering = true)
    {
        var result = new Dictionary<int, Surface>();
        float h = cellSize * 0.5f;
        bool S(Int3 c) => grid.SurfaceAt(c) >= 0;
        bool Exposed(Int3 c, Int3 d) => !grid.CoversFace(c + d, -d);

        // Whether the edge of cell c between its faces d1 and d2 is chamfered: the surface turns outward
        // there (nothing solid beside or diagonally beyond it) and at least one of the two faces shows.
        bool Edge(Int3 c, Int3 d1, Int3 d2) =>
            !S(c + d1) && !S(c + d2) && !S(c + d1 + d2) && (Exposed(c, d1) || Exposed(c, d2));

        // How much the corner of face n toward d1 and d2 is crowded by neighbors in front of it.
        float Ao(Int3 c, Int3 n, Int3 d1, Int3 d2)
        {
            Int3 f = c + n;
            bool s1 = grid.Occupied(f + d1), s2 = grid.Occupied(f + d2);
            int occ = (s1 ? 1 : 0) + (s2 ? 1 : 0) + (s1 && s2 ? 1 : grid.Occupied(f + d1 + d2) ? 1 : 0);
            return 1f - occ * 0.16f;
        }

        foreach (Int3 c in cells)
        {
            int mat = grid.SurfaceAt(c);
            if (mat < 0) continue;
            if (!result.TryGetValue(mat, out Surface surf)) result[mat] = surf = new Surface();
            float b = MathF.Min(grid.BevelOf(mat), h * 0.45f);
            float tint = PanelTint(grid.PanelAt(c));
            Vector3 center = V(c) * cellSize;
            float Inset(Int3 n, Int3 e) => Edge(c, n, e) ? b : 0f;

            // The corner of face n toward d1 and d2, pulled in along any chamfered edge.
            Vector3 Corner(Int3 n, Int3 d1, Int3 d2) => center + V(n) * h + V(d1) * (h - Inset(n, d1)) + V(d2) * (h - Inset(n, d2));

            foreach (Int3 n in Dirs)
            {
                if (!Exposed(c, n)) continue;
                (Int3 u, Int3 v) = Axes(n);
                Vector3 nv = V(n);

                // The face: a square, its corners pulled in along chamfered edges. Where a chamfer running
                // straight into this face meets it, that corner is cut off along the chamfer.
                var poly = new List<(Vector3 p, float ao)>(8);
                bool clipped = false;
                foreach ((Int3 d1, Int3 d2, bool uFirst) in new[] { (-u, -v, true), (u, -v, false), (u, v, true), (-u, v, false) })
                {
                    float ao = Ao(c, n, d1, d2);
                    if (Inset(n, d1) == 0 && Inset(n, d2) == 0 && Edge(c, d1, d2))
                    {
                        Vector3 baseP = center + nv * h;
                        Vector3 onU = baseP + V(d1) * h + V(d2) * (h - b), onV = baseP + V(d1) * (h - b) + V(d2) * h;
                        poly.Add((uFirst ? onU : onV, ao));
                        poly.Add((uFirst ? onV : onU, ao));
                        clipped = true;
                    }
                    else
                    {
                        poly.Add((Corner(n, d1, d2), ao));
                    }
                }

                if (clipped) Fan(surf, poly, nv, tint, weathering);
                else Quad(surf, new[] { poly[0].p, poly[1].p, poly[2].p, poly[3].p }, nv, new[] { poly[0].ao, poly[1].ao, poly[2].ao, poly[3].ao }, tint, weathering);

                // Chamfer strips along chamfered edges, each made once: by the face with the lower direction
                // index, or by this face if the other one is hidden.
                foreach (Int3 e in new[] { u, -u, v, -v })
                {
                    if (!Edge(c, n, e) || (DirIndex(n) > DirIndex(e) && Exposed(c, e))) continue;
                    Int3 a = Cross(n, e);
                    Vector3 ev = V(e);
                    Vector3 Pn(Int3 s) => Corner(n, e, s);
                    Vector3 Pe(Int3 s) => Corner(e, n, s);
                    var strip = new[] { Pn(-a), Pn(a), Pe(a), Pe(-a) };
                    var sao = new[] { Ao(c, n, e, -a), Ao(c, n, e, a), Ao(c, e, n, a), Ao(c, e, n, -a) };
                    // Chamfers are where paint wears first: a little lighter.
                    Quad(surf, strip, Vector3.Normalize(nv + ev), sao, tint * EdgeWear, weathering);

                    foreach (Int3 s in new[] { -a, a })
                    {
                        Vector3 sv = V(s);
                        if (Edge(c, n, s) && Edge(c, e, s))
                        {
                            // An outside corner where three chamfers meet.
                            if (DirIndex(e) < DirIndex(s) && DirIndex(n) < DirIndex(s))
                                Triangle(surf, new[] { Pn(s), Pe(s), Corner(s, n, e) }, Vector3.Normalize(nv + ev + sv), tint * EdgeWear, weathering);
                        }
                        else if (S(c + s) && !Edge(c + s, n, e))
                        {
                            // The chamfer stops against a solid block that doesn't carry it on: cap its end.
                            Triangle(surf, new[] { Pn(s), center + (nv + ev + sv) * h, Pe(s) }, -sv, tint, weathering);
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Two axes across a face, ordered so that u × v points along the face's normal.</summary>
    private static (Int3 u, Int3 v) Axes(Int3 n)
    {
        Int3 u = n.X != 0 ? new Int3(0, 1, 0) : n.Y != 0 ? new Int3(0, 0, 1) : new Int3(1, 0, 0);
        Int3 v = Cross(n, u);
        return (u, v);
    }

    private static Int3 Cross(Int3 a, Int3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>How much lighter chamfered edges are than the faces beside them.</summary>
    private const float EdgeWear = 1.1f;

    private static void Quad(Surface s, Vector3[] p, Vector3 n, float[] ao, float tint, bool weathering)
    {
        int i0 = s.Positions.Count;
        for (int i = 0; i < 4; i++)
        {
            s.Positions.Add(p[i]);
            s.Normals.Add(n);
            s.Shade.Add(Clamp1(ao[i] * tint * (weathering ? Weathering(p[i]) : 1f)));
        }

        // Split along the diagonal that keeps corner shading smooth.
        bool flip = ao[0] + ao[2] < ao[1] + ao[3];
        int[] order = flip ? new[] { 1, 2, 3, 1, 3, 0 } : new[] { 0, 1, 2, 0, 2, 3 };
        for (int t = 0; t < 6; t += 3)
            AddTri(s, i0 + order[t], i0 + order[t + 1], i0 + order[t + 2], n);
    }

    /// <summary>A convex polygon, as a fan round its middle.</summary>
    private static void Fan(Surface s, List<(Vector3 p, float ao)> poly, Vector3 n, float tint, bool weathering)
    {
        Vector3 mid = Vector3.Zero;
        float ao = 0;
        foreach ((Vector3 p, float a) in poly)
        {
            mid += p;
            ao += a;
        }

        int i0 = s.Positions.Count;
        s.Positions.Add(mid / poly.Count);
        s.Normals.Add(n);
        s.Shade.Add(Clamp1(ao / poly.Count * tint * (weathering ? Weathering(mid / poly.Count) : 1f)));
        foreach ((Vector3 p, float a) in poly)
        {
            s.Positions.Add(p);
            s.Normals.Add(n);
            s.Shade.Add(Clamp1(a * tint * (weathering ? Weathering(p) : 1f)));
        }

        for (int i = 0; i < poly.Count; i++)
            AddTri(s, i0, i0 + 1 + i, i0 + 1 + (i + 1) % poly.Count, n);
    }

    private static void Triangle(Surface s, Vector3[] p, Vector3 n, float tint, bool weathering)
    {
        int i0 = s.Positions.Count;
        for (int i = 0; i < 3; i++)
        {
            s.Positions.Add(p[i]);
            s.Normals.Add(n);
            s.Shade.Add(Clamp1(tint * (weathering ? Weathering(p[i]) : 1f)));
        }

        AddTri(s, i0, i0 + 1, i0 + 2, n);
    }

    /// <summary>Adds a triangle, wound counter-clockwise when seen from the side its normal points to.</summary>
    private static void AddTri(Surface s, int a, int b, int c, Vector3 n)
    {
        Vector3 cross = Vector3.Cross(s.Positions[b] - s.Positions[a], s.Positions[c] - s.Positions[a]);
        if (Vector3.Dot(cross, n) < 0) (b, c) = (c, b);
        s.Indices.Add(a);
        s.Indices.Add(b);
        s.Indices.Add(c);
    }
}
