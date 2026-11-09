using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace DreamGame.Core.Tests;

public class DesignTests
{
    private readonly ITestOutputHelper _out;

    public DesignTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Examples() => ExampleDesigns.All.Select(b => new object[] { b.Name });

    private static Blueprint Example(string name) => ExampleDesigns.All.First(b => b.Name == name);

    [Theory]
    [MemberData(nameof(Examples))]
    public void ExamplesAreDrivableAndHaveNoProblems(string name)
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(Example(name));
        foreach (DesignWarning w in a.Warnings) _out.WriteLine(w.ToString());
        _out.WriteLine($"{a.MassKg / 1000:0.0} t, {a.PowerToWeight:0} kW/t, {a.TopSpeedKmh:0} km/h, tip {a.TipAngleDeg:0}°");
        _out.WriteLine($"front {a.Front.AvgMm:0}/{a.Front.MinMm:0} side {a.LeftSide.AvgMm:0}/{a.LeftSide.MinMm:0} rear {a.Rear.AvgMm:0} top {a.Top.AvgMm:0}");
        foreach (GunInfo g in a.Guns)
            _out.WriteLine($"{g.Label}: {g.Barrels.Count} barrels, {g.Stats.MuzzleVelocity:0} m/s, {g.Stats.ApPenMm:0} mm, reload {g.Stats.ReloadS:0.0}s, AP {g.ApRounds} HE {g.HeRounds}");

        Assert.DoesNotContain(a.Warnings, w => w.Severity != Severity.Info);
        Assert.True(a.CanDrive);
        Assert.Empty(a.Disconnected);
        Assert.All(a.Guns, g => Assert.True(g.Valid && g.ApRounds > 0 && g.HeRounds > 0));
        Assert.InRange(a.TopSpeedKmh, a.IsNaval ? 15 : 25, 110);
        Assert.True(a.Front.Samples > 0);
    }

    [Fact]
    public void WardenSplitsIntoHullTurretAndGun()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Warden());
        Assert.Single(a.Turrets);
        GunInfo gun = Assert.Single(a.Guns);
        Assert.Equal(6, gun.Barrels.Count);
        Assert.NotNull(gun.Brake);
        Assert.Equal(2, gun.Loaders);
        Assert.Equal(1, gun.Propellants);
        Assert.Equal(24, gun.ApRounds);
        Assert.Equal(12, gun.HeRounds);
        Assert.Equal(a.Turrets[0].Section, gun.Section);
        Assert.Equal(SectionKind.Gun, a.Sections[gun.GunSection].Kind);
        Assert.Equal(0, a.SectionOf[new Int3(0, 2, -1)]); // the ring stays with the hull
        Assert.Equal(gun.Section, a.SectionOf[new Int3(2, 4, 1)]); // turret roof turns with the turret
        Assert.True(a.Front.AvgMm > a.LeftSide.AvgMm, "front should be the best-armored side");
    }

    [Fact]
    public void BulwarkGunIsFixedInTheHull()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Bulwark());
        Assert.Empty(a.Turrets);
        GunInfo gun = Assert.Single(a.Guns);
        Assert.Equal(0, gun.Section);
        Assert.Equal(10, gun.Barrels.Count);
        Assert.Equal(3, gun.Loaders);
    }

    [Fact]
    public void TurretOnlyTakesBlocksAboveTheRing()
    {
        var cells = new Dictionary<Int3, BlueprintBlock>();
        void Put(int x, int y, int z, string id) => cells[new Int3(x, y, z)] = new BlueprintBlock(id, new Int3(x, y, z));
        for (int x = -2; x <= 2; x++) Put(x, 0, 0, "frame");
        Put(0, 1, 0, "turret_ring");
        Put(0, 2, 0, "armor_light");
        Put(1, 2, 0, "armor_light");
        Put(1, 1, 0, "armor_light"); // hull block right under the turret: touching, but not part of it
        VehicleAnalysis a = VehicleAnalysis.Analyze(cells);
        Assert.Single(a.Turrets);
        int t = a.Turrets[0].Section;
        Assert.Equal(t, a.SectionOf[new Int3(0, 2, 0)]);
        Assert.Equal(t, a.SectionOf[new Int3(1, 2, 0)]);
        Assert.Equal(0, a.SectionOf[new Int3(1, 1, 0)]);
        Assert.Empty(a.Disconnected);
    }

    [Fact]
    public void MissingPartsProduceClearErrors()
    {
        var cells = new Dictionary<Int3, BlueprintBlock>
        {
            [Int3.Zero] = new("breech_76", Int3.Zero),
            [Int3.Right] = new("armor_light", Int3.Right),
            [new Int3(5, 0, 0)] = new("armor_light", new Int3(5, 0, 0)),
        };
        VehicleAnalysis a = VehicleAnalysis.Analyze(cells);
        string all = string.Join("\n", a.Warnings.Select(w => w.Text));
        Assert.Contains("No driver seat", all);
        Assert.Contains("No engine", all);
        Assert.Contains("No wheels or tracks", all);
        Assert.Contains("has no barrel", all);
        Assert.Contains("aren't attached", all);
        Assert.False(a.CanDrive);
    }

    [Fact]
    public void LosingTheTurretRingThrowsTheTurretOff()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Warden());
        GridModel g = GridModel.FromAnalysis(a);
        Assert.Empty(Connectivity.FindDetached(g, KeepRule.DriverSeat));

        g.Remove(g.Blocks[new Int3(0, 2, -1)]);
        List<List<BlockInstance>> off = Connectivity.FindDetached(g, KeepRule.DriverSeat);
        List<BlockInstance> turret = Assert.Single(off);
        Assert.Contains(turret, b => b.Def.Kind == BlockKind.Breech);
        Assert.Contains(turret, b => b.Def.Kind == BlockKind.Barrel);
        Assert.DoesNotContain(turret, b => b.Def.Kind == BlockKind.DriverSeat);
    }

    [Fact]
    public void ShootingOffABarrelDropsEverythingPastIt()
    {
        GridModel g = GridModel.FromAnalysis(VehicleAnalysis.Analyze(ExampleDesigns.Warden()));
        g.Remove(g.Blocks[new Int3(0, 3, -4)]);
        List<BlockInstance> piece = Assert.Single(Connectivity.FindDetached(g, KeepRule.DriverSeat));
        Assert.Equal(4, piece.Count); // barrels at z = -5, -6, -7 and the muzzle brake
    }

    [Fact]
    public void A76mmShotGoesThroughTheWardenGlacisAndWrecksTheInsides()
    {
        GridModel g = GridModel.FromAnalysis(VehicleAnalysis.Analyze(ExampleDesigns.Warden()));
        Section hull = g.Hull;
        var cells = new List<RayCellHit>();
        // Fired from straight ahead at the driver's row (x = -1, y = 1).
        var dir = System.Numerics.Vector3.UnitZ;
        GridRaycast.Cast(hull, hull.Min, hull.Max, new System.Numerics.Vector3(-1.05f, 1.1f, -20f), dir, 60f, cells);
        var hits = cells.Select(c => new OrderedHit
        {
            Block = hull.Cells[c.Cell], DistanceM = c.TEnter * Blueprint.CellSize,
            AngleDeg = DamageModel.ImpactAngleDeg(dir, c.Normal),
        }).ToList();
        Assert.Equal("armor_heavy_slope", hits[0].Block.Def.Id);
        Assert.Equal(45f, hits[0].AngleDeg, 1);

        GunStats s = GunMath.Compute(BlockCatalog.Medium76, 6, true, 2, 1);
        var shell = Shell.FromGun(s, AmmoType.AP, s.MuzzleVelocity, BlockCatalog.Medium76);
        var damaged = new List<(OrderedHit, bool)>();
        var reports = new List<HitReport>();
        DamageModel.ResolveAp(shell, hits, true, damaged, reports);
        Assert.Equal(HitKind.Penetration, reports[0].Kind);
        Assert.Contains(damaged, d => d.Item1.Block.Def.Kind == BlockKind.DriverSeat);

        // A weak shell is stopped by the same plate.
        var weak = new Shell { Type = AmmoType.AP, PenMm = 60, Damage = 70 };
        reports.Clear();
        DamageModel.ResolveAp(weak, hits, true, new List<(OrderedHit, bool)>(), reports);
        Assert.Equal(HitKind.NoPenetration, Assert.Single(reports).Kind);
    }

    [Fact]
    public void StructuresKeepWhatTouchesTheGround()
    {
        GridModel wall = GridModel.FromBlueprint(ExampleDesigns.Wall("armor_light", 3, 3));
        wall.Remove(wall.Blocks[new Int3(-1, 0, 0)]);
        wall.Remove(wall.Blocks[new Int3(0, 0, 0)]);
        wall.Remove(wall.Blocks[new Int3(1, 0, 0)]);
        List<BlockInstance> fallen = Assert.Single(Connectivity.FindDetached(wall, KeepRule.Anchored));
        Assert.Equal(6, fallen.Count);
    }

    [Fact]
    public void BlueprintsSurviveASaveAndLoad()
    {
        Blueprint bp = ExampleDesigns.Warden();
        Blueprint back = Blueprint.FromJson(bp.ToJson());
        Assert.Equal(bp.Name, back.Name);
        Assert.Equal(bp.Blocks.Count, back.Blocks.Count);
        Assert.Equal(VehicleAnalysis.Analyze(bp).MassKg, VehicleAnalysis.Analyze(back).MassKg);
    }

    [Fact]
    public void UnknownBlocksAreSkippedNotFatal()
    {
        const string json = "{\"format\":1,\"name\":\"x\",\"blocks\":[{\"id\":\"laser_9000\",\"x\":0,\"y\":0,\"z\":0,\"o\":0},{\"id\":\"frame\",\"x\":1,\"y\":0,\"z\":0,\"o\":99}]}";
        var problems = new List<string>();
        Blueprint bp = Blueprint.FromJson(json, problems);
        BlueprintBlock only = Assert.Single(bp.Blocks);
        Assert.Equal(0, only.O);
        Assert.Contains(problems, p => p.Contains("laser_9000"));
    }
}
