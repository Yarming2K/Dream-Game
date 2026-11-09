using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class HullMesherTests
{
    private const float Cs = Blueprint.CellSize;

    /// <summary>A grid of plain armor cells (material 0), each its own block, plus optional other occupied cells.</summary>
    private sealed class Grid : IHullGrid
    {
        public readonly HashSet<Int3> Armor = new();
        public readonly HashSet<Int3> Other = new();
        public readonly Dictionary<Int3, Int3> CoveredFaces = new();
        public int SurfaceAt(Int3 c) => Armor.Contains(c) ? 0 : -1;
        public int PanelAt(Int3 c) => c.GetHashCode();
        public bool CoversFace(Int3 c, Int3 dir) => Armor.Contains(c) || (CoveredFaces.TryGetValue(c, out Int3 d) && d == dir);
        public bool Occupied(Int3 c) => Armor.Contains(c) || Other.Contains(c);
        public float BevelOf(int material) => 0.022f;
    }

    private static HullMesher.Surface Mesh(Grid g) => HullMesher.Build(g.Armor, g, Cs, weathering: false)[0];

    /// <summary>
    /// The surface is closed: after splitting every triangle edge at any vertex lying on it (T-junctions
    /// are fine), each piece of edge is crossed the same number of times in each direction.
    /// </summary>
    private static void AssertWatertight(HullMesher.Surface s)
    {
        static (int, int, int) K(Vector3 p) => ((int)System.MathF.Round(p.X * 10000), (int)System.MathF.Round(p.Y * 10000), (int)System.MathF.Round(p.Z * 10000));
        var verts = s.Positions.Select(K).Distinct().Select(k => new Vector3(k.Item1, k.Item2, k.Item3) / 10000f).ToList();
        var edges = new Dictionary<((int, int, int), (int, int, int)), int>();
        for (int t = 0; t < s.Indices.Count; t += 3)
        {
            Vector3 n = s.Normals[s.Indices[t]];
            Vector3 p0 = s.Positions[s.Indices[t]], p1 = s.Positions[s.Indices[t + 1]], p2 = s.Positions[s.Indices[t + 2]];
            if (Vector3.Cross(p1 - p0, p2 - p0).Length() < 1e-7f) continue;
            for (int i = 0; i < 3; i++)
            {
                Vector3 a = s.Positions[s.Indices[t + i]], b = s.Positions[s.Indices[t + (i + 1) % 3]];
                Vector3 d = b - a;
                float len2 = d.LengthSquared();
                var cuts = new List<float> { 0f, 1f };
                foreach (Vector3 q in verts)
                {
                    float f = Vector3.Dot(q - a, d) / len2;
                    if (f <= 1e-4f || f >= 1 - 1e-4f) continue;
                    if ((a + d * f - q).LengthSquared() < 1e-8f) cuts.Add(f);
                }

                cuts.Sort();
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    var key = (K(a + d * cuts[k]), K(a + d * cuts[k + 1]));
                    if (key.Item1 == key.Item2) continue;
                    edges[key] = edges.GetValueOrDefault(key) + 1;
                }
            }
        }

        foreach (((var a, var b), int n) in edges)
            Assert.True(n == edges.GetValueOrDefault((b, a)), $"open edge {a} -> {b}: {n} vs {edges.GetValueOrDefault((b, a))}");
    }

    private static float Volume(HullMesher.Surface s)
    {
        float v = 0;
        for (int t = 0; t < s.Indices.Count; t += 3)
            v += Vector3.Dot(s.Positions[s.Indices[t]], Vector3.Cross(s.Positions[s.Indices[t + 1]], s.Positions[s.Indices[t + 2]])) / 6f;
        return v;
    }

    [Fact]
    public void ALoneBlockIsAChamferedCube()
    {
        var g = new Grid();
        g.Armor.Add(Int3.Zero);
        HullMesher.Surface s = Mesh(g);
        Assert.Equal(6 * 2 + 12 * 2 + 8, s.Triangles);
        AssertWatertight(s);
        float vol = Volume(s);
        Assert.InRange(vol, Cs * Cs * Cs * 0.97f, Cs * Cs * Cs);
    }

    [Fact]
    public void NeighborsMergeIntoOnePlate()
    {
        var g = new Grid();
        for (int x = 0; x < 4; x++)
        for (int z = 0; z < 3; z++)
            g.Armor.Add(new Int3(x, 0, z));
        HullMesher.Surface s = Mesh(g);
        AssertWatertight(s);
        Assert.InRange(Volume(s), 12 * Cs * Cs * Cs * 0.97f, 12 * Cs * Cs * Cs);
        // No faces inside the plate: every triangle lies on the outside of its 2 m × 1.5 m × 0.5 m box.
        foreach (Vector3 p in s.Positions)
        {
            bool onSkin = p.Y > Cs * 0.5f - 0.03f || p.Y < -Cs * 0.5f + 0.03f || p.X < -Cs * 0.5f + 0.03f || p.X > 3.5f * Cs - 0.03f ||
                          p.Z < -Cs * 0.5f + 0.03f || p.Z > 2.5f * Cs - 0.03f;
            Assert.True(onSkin, $"vertex {p} is inside the plate");
        }

        // A lone block per cell would have taken 12 × 44 triangles.
        Assert.True(s.Triangles < 12 * 44 / 2);
    }

    [Fact]
    public void AnLShapeAndAStepStayClosed()
    {
        var g = new Grid();
        foreach (Int3 c in new[] { new Int3(0, 0, 0), new Int3(1, 0, 0), new Int3(1, 1, 0), new Int3(0, 0, 1), new Int3(2, 1, 0), new Int3(2, 2, 1) })
            g.Armor.Add(c);
        AssertWatertight(Mesh(g));
    }

    [Fact]
    public void TrianglesFaceOutward()
    {
        var g = new Grid();
        g.Armor.Add(Int3.Zero);
        g.Armor.Add(new Int3(0, 1, 0));
        HullMesher.Surface s = Mesh(g);
        for (int t = 0; t < s.Indices.Count; t += 3)
        {
            Vector3 a = s.Positions[s.Indices[t]], b = s.Positions[s.Indices[t + 1]], c = s.Positions[s.Indices[t + 2]];
            Vector3 n = s.Normals[s.Indices[t]];
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0);
            // Normals point away from the middle of the two-block column.
            Assert.True(Vector3.Dot((a + b + c) / 3f - new Vector3(0, Cs * 0.5f, 0), n) > 0);
        }
    }

    [Fact]
    public void AFaceAgainstAFullNeighborFaceIsDropped()
    {
        var g = new Grid();
        g.Armor.Add(Int3.Zero);
        g.Other.Add(new Int3(0, 1, 0));
        g.CoveredFaces[new Int3(0, 1, 0)] = new Int3(0, -1, 0);
        HullMesher.Surface s = Mesh(g);
        Assert.DoesNotContain(s.Positions.Select((p, i) => (p, n: s.Normals[i])), x => x.n == Vector3.UnitY);
    }

    [Fact]
    public void CornersCrowdedByNeighborsAreDarker()
    {
        var g = new Grid();
        g.Armor.Add(Int3.Zero);
        g.Other.Add(new Int3(1, 0, 0));
        g.Other.Add(new Int3(1, 1, 0));
        HullMesher.Surface s = Mesh(g);
        // The top face's corners next to the occupied cell above-right are shaded; the far ones aren't.
        var top = Enumerable.Range(0, s.Positions.Count).Where(i => s.Normals[i] == Vector3.UnitY).ToList();
        float near = top.Where(i => s.Positions[i].X > 0).Max(i => s.Shade[i]);
        float far = top.Where(i => s.Positions[i].X < 0).Min(i => s.Shade[i]);
        Assert.True(near < far);
    }

    [Fact]
    public void EveryExampleDesignMeshesClosedAtItsSurfaceBlocks()
    {
        foreach (Blueprint bp in ExampleDesigns.All.Where(b => !b.Name.StartsWith("Colossus")))
        {
            VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
            var surface = new HashSet<Int3>(a.Defs.Where(kv => BlockModels.IsSurface(kv.Value)).Select(kv => kv.Key));
            var g = new Grid();
            foreach (Int3 c in surface) g.Armor.Add(c);
            foreach (Int3 c in a.Defs.Keys) if (!surface.Contains(c)) g.Other.Add(c);
            if (g.Armor.Count == 0) continue;
            HullMesher.Surface s = Mesh(g);
            Assert.True(s.Triangles > 0, bp.Name);
            AssertWatertight(s);
        }
    }

    [Fact]
    public void RandomClumpsAreAlwaysClosed()
    {
        var rng = new System.Random(7);
        for (int trial = 0; trial < 300; trial++)
        {
            var g = new Grid();
            int n = rng.Next(1, 30);
            for (int i = 0; i < n; i++) g.Armor.Add(new Int3(rng.Next(0, 4), rng.Next(0, 4), rng.Next(0, 4)));
            // Some trials have other blocks about that don't merge, and some slope faces.
            for (int i = 0; i < rng.Next(0, 6); i++)
            {
                var c = new Int3(rng.Next(-1, 5), rng.Next(-1, 5), rng.Next(-1, 5));
                if (g.Armor.Contains(c)) continue;
                g.Other.Add(c);
            }

            AssertWatertight(Mesh(g));
        }
    }
}
