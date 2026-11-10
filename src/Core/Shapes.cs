using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>
/// The shapes a one-cell block can take, each described once as convex pieces cut out of the cell by flat
/// planes. Everything that cares about shape reads this: shells (where they strike and at what angle),
/// colliders, the hull surface mesher, the block models and the builder. Coordinates are in cell units,
/// the cell spanning -0.5 to 0.5 on each axis, unrotated (forward is -Z, up is +Y).
/// </summary>
public static class Shapes
{
    /// <summary>A half-space: points with N · p ≤ D are inside.</summary>
    public readonly struct Plane
    {
        public readonly Vector3 N;
        public readonly float D;

        public Plane(Vector3 n, float d)
        {
            float len = n.Length();
            N = n / len;
            D = d / len;
        }

        public bool Inside(Vector3 p, float eps = 1e-4f) => Vector3.Dot(N, p) <= D + eps;
    }

    /// <summary>One face of a shape's mesh: a convex polygon, wound counter-clockwise seen from outside.</summary>
    public sealed class Face
    {
        public Vector3 Normal;
        public readonly List<Vector3> Points = new();

        /// <summary>The cell side this face lies flat on (±X, ±Y, ±Z as a unit step), or zero for a slanted face.</summary>
        public Int3 Side;
    }

    private static readonly Plane[] CubePlanes =
    {
        new(Vector3.UnitX, 0.5f), new(-Vector3.UnitX, 0.5f), new(Vector3.UnitY, 0.5f),
        new(-Vector3.UnitY, 0.5f), new(Vector3.UnitZ, 0.5f), new(-Vector3.UnitZ, 0.5f),
    };

    private static readonly Dictionary<BlockShape, Plane[][]> PieceCache = new();
    private static readonly Dictionary<BlockShape, List<Face>> FaceCache = new();
    private static readonly Dictionary<BlockShape, float> VolumeCache = new();
    private static readonly Dictionary<BlockShape, bool[]> CoverCache = new();

    /// <summary>A plain name for the builder.</summary>
    public static string NameOf(BlockShape s) => s switch
    {
        BlockShape.Cube => "Cube",
        BlockShape.Wedge => "Slope",
        BlockShape.ShallowLow => "Shallow slope (low half)",
        BlockShape.ShallowHigh => "Shallow slope (high half)",
        BlockShape.Corner => "Outer corner",
        BlockShape.InnerCorner => "Inner corner",
        BlockShape.Round => "Rounded edge",
        BlockShape.Half => "Half block",
        BlockShape.Plate => "Plate",
        BlockShape.Octagon => "Octagon (round section)",
        _ => s.ToString(),
    };

    /// <summary>
    /// The convex pieces of a shape, each as the planes that cut it out of the cube (the cube's own six
    /// faces always apply too). A shape is the union of its pieces.
    /// </summary>
    public static Plane[][] PiecesOf(BlockShape s)
    {
        lock (PieceCache)
        {
            if (PieceCache.TryGetValue(s, out Plane[][] p)) return p;
            p = Build(s);
            PieceCache[s] = p;
            return p;
        }
    }

    private static Plane[][] Build(BlockShape s)
    {
        switch (s)
        {
            case BlockShape.Wedge:
                // Solid where y ≤ z: the slope faces forward and up.
                return new[] { new[] { new Plane(new Vector3(0, 1, -1), 0) } };
            case BlockShape.ShallowLow:
                // Half as steep, the bottom half of a two-block slope: rises from nothing at the front to half
                // height at the back.
                return new[] { new[] { new Plane(new Vector3(0, 1, -0.5f), -0.25f) } };
            case BlockShape.ShallowHigh:
                // The top half: half height at the front, full at the back.
                return new[] { new[] { new Plane(new Vector3(0, 1, -0.5f), 0.25f) } };
            case BlockShape.Corner:
                // Where a front slope meets a side slope going round the outside of a corner.
                return new[] { new[] { new Plane(new Vector3(0, 1, -1), 0), new Plane(new Vector3(-1, 1, 0), 0) } };
            case BlockShape.InnerCorner:
                // Where two slopes meet going round the inside of a corner.
                return new[] { new[] { new Plane(new Vector3(0, 1, -1), 0) }, new[] { new Plane(new Vector3(-1, 1, 0), 0) } };
            case BlockShape.Round:
            {
                // A quarter round: the front top edge rounded off with a radius of the whole cell, in four facets.
                var planes = new List<Plane>();
                const int facets = 4;
                for (int i = 0; i < facets; i++)
                {
                    float a0 = i * MathF.PI / 2 / facets, a1 = (i + 1) * MathF.PI / 2 / facets;
                    // Points on the arc centered on the back bottom edge (y = -0.5, z = 0.5), radius 1.
                    var p0 = new Vector2(-0.5f + MathF.Sin(a0), 0.5f - MathF.Cos(a0));
                    var p1 = new Vector2(-0.5f + MathF.Sin(a1), 0.5f - MathF.Cos(a1));
                    // Outward normal of the chord in (y, z).
                    var e = p1 - p0;
                    var n = new Vector2(-e.Y, e.X);
                    if (Vector2.Dot(n, (p0 + p1) / 2 - new Vector2(-0.5f, 0.5f)) < 0) n = -n;
                    planes.Add(new Plane(new Vector3(0, n.X, n.Y), Vector2.Dot(n, p0)));
                }

                return new[] { planes.ToArray() };
            }

            case BlockShape.Half:
                return new[] { new[] { new Plane(Vector3.UnitY, 0) } };
            case BlockShape.Plate:
                // A quarter-cell plate against the back face: skirts, fins, bulkheads, gun shields.
                return new[] { new[] { new Plane(-Vector3.UnitZ, -0.25f) } };
            case BlockShape.Octagon:
            {
                // An eight-sided section along Z: pipes, funnels, round turrets and hulls.
                // A regular octagon: the diagonal sides are as far out as the straight ones.
                float d = 0.5f * MathF.Sqrt(2);
                return new[]
                {
                    new[]
                    {
                        new Plane(new Vector3(1, 1, 0), d), new Plane(new Vector3(1, -1, 0), d),
                        new Plane(new Vector3(-1, 1, 0), d), new Plane(new Vector3(-1, -1, 0), d),
                    },
                };
            }

            default:
                return new[] { Array.Empty<Plane>() };
        }
    }

    /// <summary>Whether a point (cell units, unrotated) is inside the shape.</summary>
    public static bool Contains(BlockShape s, Vector3 p, float eps = 1e-4f)
    {
        foreach (Plane c in CubePlanes)
            if (!c.Inside(p, eps)) return false;
        foreach (Plane[] piece in PiecesOf(s))
        {
            bool inside = true;
            foreach (Plane pl in piece)
                if (!pl.Inside(p, eps))
                {
                    inside = false;
                    break;
                }

            if (inside) return true;
        }

        return false;
    }

    /// <summary>How much of the cell the shape fills (0 to 1).</summary>
    public static float Volume(BlockShape s)
    {
        lock (VolumeCache)
        {
            if (VolumeCache.TryGetValue(s, out float v)) return v;
            // Samples offset differently on each axis, so none sit exactly on a slope.
            const int n = 32;
            int inside = 0;
            for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
                if (Contains(s, new Vector3((x + 0.5f) / n - 0.5f, (y + 0.31f) / n - 0.5f, (z + 0.73f) / n - 0.5f), 0f))
                    inside++;
            v = inside / (float)(n * n * n);
            VolumeCache[s] = v;
            return v;
        }
    }

    private static readonly Int3[] Sides =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private static int SideIndex(Int3 d) => d.X != 0 ? (d.X > 0 ? 0 : 1) : d.Y != 0 ? (d.Y > 0 ? 2 : 3) : d.Z > 0 ? 4 : 5;

    /// <summary>
    /// Whether the shape fills the whole of its cell's face on side <paramref name="dir"/> (in the grid, after
    /// turning by <paramref name="orientation"/>), so whatever is next to it there can't be seen.
    /// </summary>
    public static bool CoversFace(BlockShape s, int orientation, Int3 dir)
    {
        if (s == BlockShape.Cube) return true;
        Vector3 l = Orientations.ApplyInverse(orientation, new Vector3(dir.X, dir.Y, dir.Z));
        var local = new Int3((int)MathF.Round(l.X), (int)MathF.Round(l.Y), (int)MathF.Round(l.Z));
        bool[] covers;
        lock (CoverCache)
        {
            if (!CoverCache.TryGetValue(s, out covers))
            {
                covers = new bool[6];
                for (int i = 0; i < 6; i++) covers[i] = FaceFull(s, Sides[i]);
                CoverCache[s] = covers;
            }
        }

        return covers[SideIndex(local)];
    }

    /// <summary>Samples a face of the cell finely: it's full if every sample is inside the shape.</summary>
    private static bool FaceFull(BlockShape s, Int3 side)
    {
        Vector3 n = new(side.X, side.Y, side.Z);
        Vector3 u = side.X != 0 ? Vector3.UnitY : Vector3.UnitX;
        Vector3 v = Vector3.Cross(n, u);
        const int k = 9;
        for (int i = 0; i < k; i++)
        for (int j = 0; j < k; j++)
        {
            Vector3 p = n * 0.5f + u * ((i / (float)(k - 1) - 0.5f) * 0.98f) + v * ((j / (float)(k - 1) - 0.5f) * 0.98f);
            if (!Contains(s, p, 1e-3f)) return false;
        }

        return true;
    }

    /// <summary>The corners of one convex piece (for collision shapes).</summary>
    public static List<Vector3> Corners(Plane[] piece)
    {
        var all = new List<Plane>(CubePlanes);
        all.AddRange(piece);
        var pts = new List<Vector3>();
        for (int a = 0; a < all.Count; a++)
        for (int b = a + 1; b < all.Count; b++)
        for (int c = b + 1; c < all.Count; c++)
        {
            if (!Intersect(all[a], all[b], all[c], out Vector3 p)) continue;
            bool inside = true;
            foreach (Plane pl in all)
                if (!pl.Inside(p, 1e-4f))
                {
                    inside = false;
                    break;
                }

            if (!inside) continue;
            bool dup = false;
            foreach (Vector3 q in pts)
                if (Vector3.DistanceSquared(p, q) < 1e-8f)
                {
                    dup = true;
                    break;
                }

            if (!dup) pts.Add(p);
        }

        return pts;
    }

    private static bool Intersect(Plane a, Plane b, Plane c, out Vector3 p)
    {
        p = default;
        Vector3 bc = Vector3.Cross(b.N, c.N);
        float det = Vector3.Dot(a.N, bc);
        if (MathF.Abs(det) < 1e-6f) return false;
        p = (a.D * bc + b.D * Vector3.Cross(c.N, a.N) + c.D * Vector3.Cross(a.N, b.N)) / det;
        return true;
    }

    /// <summary>
    /// The faces of a shape's surface (cell units, unrotated), each a convex polygon. Faces lying flat on a
    /// cell side say which side, so neighbors can hide them. Faces of different pieces that end up inside
    /// another piece are left out where they're wholly inside.
    /// </summary>
    public static List<Face> FacesOf(BlockShape s)
    {
        lock (FaceCache)
        {
            if (FaceCache.TryGetValue(s, out List<Face> f)) return f;
            f = new List<Face>();
            Plane[][] pieces = PiecesOf(s);
            foreach (Plane[] piece in pieces)
            {
                List<Vector3> corners = Corners(piece);
                var all = new List<Plane>(CubePlanes);
                all.AddRange(piece);
                foreach (Plane pl in all)
                {
                    var on = new List<Vector3>();
                    foreach (Vector3 c in corners)
                        if (MathF.Abs(Vector3.Dot(pl.N, c) - pl.D) < 1e-4f) on.Add(c);
                    if (on.Count < 3) continue;
                    Vector3 mid = Vector3.Zero;
                    foreach (Vector3 c in on) mid += c;
                    mid /= on.Count;

                    // Skip faces of one piece buried inside another.
                    if (pieces.Length > 1 && BuriedInOther(s, piece, mid + pl.N * 0.01f)) continue;

                    Vector3 u = Vector3.Normalize(on[0] - mid), w = Vector3.Cross(pl.N, u);
                    on.Sort((p, q) => MathF.Atan2(Vector3.Dot(p - mid, w), Vector3.Dot(p - mid, u))
                        .CompareTo(MathF.Atan2(Vector3.Dot(q - mid, w), Vector3.Dot(q - mid, u))));
                    var face = new Face { Normal = pl.N };
                    face.Points.AddRange(on);
                    foreach (Int3 side in Sides)
                        if (Vector3.Dot(pl.N, new Vector3(side.X, side.Y, side.Z)) > 0.999f && MathF.Abs(pl.D - 0.5f) < 1e-4f)
                            face.Side = side;
                    f.Add(face);
                }
            }

            FaceCache[s] = f;
            return f;
        }
    }

    private static bool BuriedInOther(BlockShape s, Plane[] own, Vector3 p)
    {
        foreach (Plane[] piece in PiecesOf(s))
        {
            if (piece == own) continue;
            bool inside = true;
            foreach (Plane c in CubePlanes)
                if (!c.Inside(p, 0)) inside = false;
            foreach (Plane pl in piece)
                if (!pl.Inside(p, 0)) inside = false;
            if (inside) return true;
        }

        return false;
    }

    /// <summary>A ray against a shape in cell units, unrotated: where it enters and leaves, and the entry normal.</summary>
    public static bool Intersect(BlockShape s, Vector3 o, Vector3 d, out float tEnter, out float tExit, out Vector3 nEnter)
    {
        tEnter = float.PositiveInfinity;
        tExit = float.NegativeInfinity;
        nEnter = Vector3.Zero;
        bool any = false;
        foreach (Plane[] piece in PiecesOf(s))
        {
            float t0 = float.NegativeInfinity, t1 = float.PositiveInfinity;
            Vector3 n0 = Vector3.Zero;
            bool ok = true;
            foreach (Plane pl in CubePlanes)
                if (!Clip(pl, o, d, ref t0, ref t1, ref n0))
                {
                    ok = false;
                    break;
                }

            if (ok)
                foreach (Plane pl in piece)
                    if (!Clip(pl, o, d, ref t0, ref t1, ref n0))
                    {
                        ok = false;
                        break;
                    }

            if (!ok || t1 < 0) continue;
            any = true;
            if (t0 < tEnter)
            {
                tEnter = t0;
                nEnter = n0;
            }

            tExit = MathF.Max(tExit, t1);
        }

        return any;
    }

    private static bool Clip(Plane pl, Vector3 o, Vector3 d, ref float t0, ref float t1, ref Vector3 n0)
    {
        float denom = Vector3.Dot(pl.N, d);
        float num = pl.D - Vector3.Dot(pl.N, o);
        if (MathF.Abs(denom) < 1e-9f) return num >= 0;
        float t = num / denom;
        if (denom < 0)
        {
            if (t > t0)
            {
                t0 = t;
                n0 = pl.N;
            }
        }
        else if (t < t1)
        {
            t1 = t;
        }

        return t0 <= t1;
    }
}
