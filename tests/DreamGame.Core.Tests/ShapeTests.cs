using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class ShapeTests
{
    public static IEnumerable<object[]> AllShapes() => Enum.GetValues<BlockShape>().Select(s => new object[] { s });

    [Theory]
    [InlineData(BlockShape.Cube, 1f)]
    [InlineData(BlockShape.Wedge, 0.5f)]
    [InlineData(BlockShape.ShallowLow, 0.25f)]
    [InlineData(BlockShape.ShallowHigh, 0.75f)]
    [InlineData(BlockShape.Corner, 1f / 3f)]
    [InlineData(BlockShape.InnerCorner, 2f / 3f)]
    [InlineData(BlockShape.Half, 0.5f)]
    [InlineData(BlockShape.Plate, 0.25f)]
    [InlineData(BlockShape.Octagon, 0.8284f)]
    public void VolumesAreRight(BlockShape s, float expected) => Assert.InRange(Shapes.Volume(s), expected - 0.02f, expected + 0.02f);

    [Fact]
    public void TheRoundIsCloseToAQuarterCircle() => Assert.InRange(Shapes.Volume(BlockShape.Round), 0.74f, MathF.PI / 4f);

    [Theory]
    [InlineData(BlockShape.Wedge, "-y +z")]
    [InlineData(BlockShape.ShallowLow, "-y")]
    [InlineData(BlockShape.ShallowHigh, "-y +z")]
    [InlineData(BlockShape.Corner, "-y")]
    [InlineData(BlockShape.InnerCorner, "+x -y +z")]
    [InlineData(BlockShape.Round, "-y +z")]
    [InlineData(BlockShape.Half, "-y")]
    [InlineData(BlockShape.Plate, "+z")]
    [InlineData(BlockShape.Octagon, "")]
    [InlineData(BlockShape.Cube, "+x -x +y -y +z -z")]
    public void ShapesCoverTheFacesTheyFill(BlockShape s, string sides)
    {
        var names = new Dictionary<string, Int3>
        {
            ["+x"] = new(1, 0, 0), ["-x"] = new(-1, 0, 0), ["+y"] = new(0, 1, 0), ["-y"] = new(0, -1, 0), ["+z"] = new(0, 0, 1), ["-z"] = new(0, 0, -1),
        };
        var expected = sides.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        foreach ((string name, Int3 d) in names) Assert.Equal(expected.Contains(name), Shapes.CoversFace(s, 0, d));
    }

    [Fact]
    public void TurningAShapeTurnsTheFacesItCovers()
    {
        for (int o = 0; o < 24; o++)
        {
            Int3 down = Orientations.Apply(o, new Int3(0, -1, 0)), back = Orientations.Apply(o, new Int3(0, 0, 1));
            foreach (Int3 d in new[] { new Int3(1, 0, 0), new Int3(-1, 0, 0), new Int3(0, 1, 0), new Int3(0, -1, 0), new Int3(0, 0, 1), new Int3(0, 0, -1) })
                Assert.Equal(d == down || d == back, Shapes.CoversFace(BlockShape.Wedge, o, d));
        }
    }

    /// <summary>Each face points away from the inside, and the faces close the shape (their area-weighted normals cancel).</summary>
    [Theory]
    [MemberData(nameof(AllShapes))]
    public void MeshesAreClosedAndFaceOutward(BlockShape s)
    {
        if (s == BlockShape.InnerCorner) return; // two overlapping pieces: closed as a union, not face by face
        Vector3 sum = Vector3.Zero;
        foreach (Shapes.Face f in Shapes.FacesOf(s))
        {
            Assert.True(f.Points.Count >= 3);
            Vector3 mid = f.Points.Aggregate(Vector3.Zero, (a, b) => a + b) / f.Points.Count;
            Assert.False(Shapes.Contains(s, mid + f.Normal * 0.02f, 0f), $"{s}: face with normal {f.Normal} points inward");
            Assert.True(Shapes.Contains(s, mid - f.Normal * 0.02f, 0f), $"{s}: nothing solid behind face {f.Normal}");
            Vector3 area = Vector3.Zero;
            for (int i = 1; i + 1 < f.Points.Count; i++)
                area += Vector3.Cross(f.Points[i] - f.Points[0], f.Points[i + 1] - f.Points[0]) / 2;
            Assert.True(Vector3.Dot(area, f.Normal) > 0, $"{s}: face {f.Normal} wound clockwise");
            sum += area;
        }

        Assert.True(sum.Length() < 1e-3f, $"{s}: not closed ({sum})");
    }

    /// <summary>The general shape test gives exactly what the old slope formula did.</summary>
    [Fact]
    public void RaysAgainstASlopeMatchTheOldFormula()
    {
        var rng = new Random(4);
        var n = Vector3.Normalize(new Vector3(0, 1, -1));
        for (int i = 0; i < 3000; i++)
        {
            var o = new Vector3((float)rng.NextDouble() * 4 - 2, (float)rng.NextDouble() * 4 - 2, (float)rng.NextDouble() * 4 - 2);
            Vector3 d = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f));
            bool hit = Shapes.Intersect(BlockShape.Wedge, o, d, out float t0, out _, out Vector3 nIn);
            // Brute force: march.
            float tm = float.NaN;
            for (float t = 0; t < 6; t += 0.0005f)
                if (Shapes.Contains(BlockShape.Wedge, o + d * t, 0f))
                {
                    tm = t;
                    break;
                }

            if (float.IsNaN(tm))
            {
                Assert.True(!hit || t0 > 5.9f || t0 < 0, $"phantom hit at {t0}");
                continue;
            }

            Assert.True(hit);
            if (t0 <= 0) continue; // started inside
            Assert.InRange(t0, tm - 0.002f, tm + 0.002f);
            Vector3 p = o + d * t0;
            bool onSlope = MathF.Abs(p.Y - p.Z) < 1e-3f && Vector3.Distance(nIn, n) < 1e-3f;
            bool onCube = MathF.Abs(MathF.Max(MathF.Abs(p.X), MathF.Max(MathF.Abs(p.Y), MathF.Abs(p.Z))) - 0.5f) < 1e-3f;
            Assert.True(onSlope || onCube);
        }
    }

    [Fact]
    public void EveryArmorTypeComesInEveryShape()
    {
        foreach (string id in new[] { "armor_light", "armor_heavy", "armor_composite", "glazing" })
        {
            BlockDef cube = BlockCatalog.Get(id);
            List<BlockDef> all = BlockCatalog.VariantsOf(cube);
            Assert.Equal(1 + BlockCatalog.ShapeVariants.Length, all.Count);
            Assert.Equal(all.Count, all.Select(d => d.Shape).Distinct().Count());
            foreach (BlockDef v in all.Skip(1))
            {
                Assert.Equal(id, v.BaseId);
                Assert.True(v.MassKg < cube.MassKg);
                Assert.Equal(cube.ArmorMm, v.ArmorMm);
                Assert.Same(cube, BlockModels.SurfaceDefOf(v));
            }
        }

        // The old slope blocks kept their ids, and the builder lists only the plain versions.
        Assert.Equal(BlockShape.Wedge, BlockCatalog.Get("armor_heavy_slope").Shape);
        Assert.DoesNotContain(BlockCatalog.InCategory("Structure"), d => d.Shape != BlockShape.Cube && d.BaseId != null);
    }

    /// <summary>A shallow two-block slope hit head-on reads as about twice as thick as its plate.</summary>
    [Fact]
    public void ShallowSlopesCountAsThicker()
    {
        var g = new GridModel();
        g.Add(BlockCatalog.Get("armor_heavy_shallow"), new Int3(0, 0, 0), 0);
        var hits = new List<RayCellHit>();
        Section s = g.Sections[0];
        g.RecomputeBounds();
        GridRaycast.Cast(s, s.Min, s.Max, new Vector3(0, -0.3f, -5f), Vector3.UnitZ, 20f, hits);
        Assert.Single(hits);
        float angle = DamageModel.ImpactAngleDeg(Vector3.UnitZ, hits[0].Normal);
        Assert.InRange(angle, 60f, 66f);
        Assert.InRange(DamageModel.EffectiveArmor(80f, angle), 160f, 200f);
    }
}

public class ClipTests
{
    private static HashSet<(string, Int3)> Cells(Clip c)
    {
        var set = new HashSet<(string, Int3)>();
        foreach (BlueprintBlock b in c.Blocks)
            foreach (Int3 cell in Footprint.Cells(BlockCatalog.Get(b.Id), b.Cell, b.O)) Assert.True(set.Add((b.Id, cell)), "overlap");
        return set;
    }

    private static Clip Sample() => Clip.From(ExampleDesigns.Warden().Blocks.Concat(new[]
    {
        // A big block too, off to one side.
        new BlueprintBlock("belt_armor", new Int3(9, 0, 0), 0),
    }));

    [Fact]
    public void ACopyStartsAtTheOrigin()
    {
        Clip c = Sample();
        HashSet<(string, Int3)> cells = Cells(c);
        Assert.Equal(0, cells.Min(x => x.Item2.X));
        Assert.Equal(0, cells.Min(x => x.Item2.Y));
        Assert.Equal(0, cells.Min(x => x.Item2.Z));
        Assert.Equal(c.Size.X - 1, cells.Max(x => x.Item2.X));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FourQuarterTurnsComeBackToTheStart(int axis)
    {
        Clip c = Sample();
        Clip t = c.Turned(axis, 1);
        Assert.Equal(Cells(c).Count, Cells(t).Count);
        Clip back = t.Turned(axis, 1).Turned(axis, 1).Turned(axis, 1);
        Assert.True(Cells(c).SetEquals(Cells(back)));
        Assert.True(Cells(c).SetEquals(Cells(c.Turned(axis, 4))));
    }

    [Fact]
    public void FlippingTwiceComesBack()
    {
        Clip c = Sample();
        Assert.True(Cells(c).SetEquals(Cells(c.Flipped().Flipped())));
        Assert.Equal(Cells(c).Count, Cells(c.Flipped()).Count);
    }

    [Fact]
    public void ATurnedTankIsStillTheSameTank()
    {
        Clip c = Clip.From(ExampleDesigns.Warden().Blocks).Turned(1, 1);
        VehicleAnalysis a = VehicleAnalysis.Analyze(Blueprint.FromCellMap("turned", c.Blocks));
        VehicleAnalysis b = VehicleAnalysis.Analyze(ExampleDesigns.Warden());
        Assert.Equal(b.Guns.Count, a.Guns.Count);
        Assert.Equal(b.MassKg, a.MassKg, 1);
        Assert.Empty(a.Disconnected);
    }
}
