using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

/// <summary>How far a machine's blocks can reach from its middle, however its turrets are turned.</summary>
public class ReachTests
{
    public static IEnumerable<object[]> Examples() => ExampleDesigns.All.Select(b => new object[] { b.Name });

    private static GridModel Model(string name) =>
        GridModel.FromAnalysis(VehicleAnalysis.Analyze(ExampleDesigns.All.First(b => b.Name == name)));

    /// <summary>Where each cell ends up with the sections turned to the given angles (as the game draws them).</summary>
    private static Matrix4x4 Placed(GridModel g, Section s, Dictionary<int, Matrix4x4> memo)
    {
        if (memo.TryGetValue(s.Id, out Matrix4x4 m)) return m;
        Matrix4x4 local = Matrix4x4.Identity;
        if (s.Kind != SectionKind.Hull && s.Angle != 0f)
            local = Matrix4x4.CreateTranslation(-s.Pivot) * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(s.Axis), s.Angle) *
                    Matrix4x4.CreateTranslation(s.Pivot);
        m = s.Parent >= 0 && s.Kind != SectionKind.Hull ? local * Placed(g, g.Sections[s.Parent], memo) : local;
        memo[s.Id] = m;
        return m;
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void NoTurnSendsABlockPastTheReach(string name)
    {
        GridModel g = Model(name);
        float reach = g.MaxReach();
        var rng = new Random(7);
        for (int trial = 0; trial < 24; trial++)
        {
            foreach (Section s in g.Sections) s.Angle = s.Kind == SectionKind.Hull ? 0 : (float)(rng.NextDouble() * Math.Tau - Math.PI);
            var memo = new Dictionary<int, Matrix4x4>();
            foreach (Section s in g.Sections)
            {
                Matrix4x4 m = Placed(g, s, memo);
                foreach (Int3 c in s.Cells.Keys)
                    Assert.True(Vector3.Transform(c.ToVector3(), m).Length() <= reach + 1e-3f, $"{name}: cell {c} past {reach}");
            }
        }
    }

    [Fact]
    public void ABattleshipsReachIsAboutItsLength()
    {
        GridModel g = Model("Colossus (battleship)");
        float far = g.Blocks.Keys.Max(c => c.ToVector3().Length());
        float reach = g.MaxReach();
        Assert.True(reach >= far);
        // Its turrets sit far from the middle; the reach mustn't add their distance twice over.
        float pivots = g.Sections.Max(s => s.Pivot.Length());
        Assert.True(pivots > 40f);
        Assert.True(reach < far + 0.25f * pivots, $"reach {reach} vs furthest cell {far}");
    }
}

/// <summary>Block models only ask the part library for meshes it has.</summary>
public class PartLibraryTests
{
    [Fact]
    public void EveryTagIsInTheLibrary()
    {
        var used = new HashSet<string>();
        foreach (BlockDef def in BlockCatalog.All)
            foreach (ModelPart p in BlockModels.For(def).Parts)
                if (p.Model != null)
                {
                    used.Add(p.Model);
                    Assert.True(p.Model == PartLibrary.Covered || PartLibrary.Names.Contains(p.Model), $"{def.Id}: unknown library part {p.Model}");
                }

        // Every library part is used somewhere, and wheels, tracks and muzzle brakes use it.
        foreach (string n in PartLibrary.Names) Assert.Contains(n, used);
    }
}
