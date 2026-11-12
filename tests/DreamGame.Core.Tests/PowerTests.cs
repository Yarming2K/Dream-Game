using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class PowerTests
{
    private static VehicleAnalysis A(IEnumerable<BlueprintBlock> blocks) => VehicleAnalysis.Analyze(Blueprint.FromCellMap("test", blocks));

    /// <summary>A railgun on a frame base, with slugs, plus whatever power parts are given.</summary>
    private static List<BlueprintBlock> Railgun(int rails, params (string id, Int3 at)[] extra)
    {
        var b = new List<BlueprintBlock>
        {
            new("rail_breech", new Int3(0, 0, 0), 0),
            new("ammo_ap", new Int3(0, 0, 1), 0),
        };
        for (int i = 1; i <= rails; i++) b.Add(new BlueprintBlock("rail", new Int3(0, 0, -i), 0));
        foreach ((string id, Int3 at) in extra) b.Add(new BlueprintBlock(id, at, 0));
        return b;
    }

    [Fact]
    public void TheBankChargesUpToWhatItHoldsAndPaysOutWholeShots()
    {
        var bank = new PowerBank();
        bank.Configure(supplyMW: 3f, fuelledMW: 3f, capacityMJ: 25f);
        Assert.Equal(0f, bank.StoredMJ);
        float fuelled = bank.Charge(5f);
        Assert.Equal(15f, bank.StoredMJ, 3);
        Assert.Equal(15f, fuelled, 3);
        Assert.False(bank.TryDraw(20f));
        Assert.Equal(15f, bank.StoredMJ, 3);
        bank.Charge(10f);
        Assert.Equal(25f, bank.StoredMJ, 3);
        Assert.Equal(0f, bank.Charge(1f));
        Assert.True(bank.TryDraw(20f));
        Assert.Equal(5f, bank.StoredMJ, 3);
    }

    [Fact]
    public void AReactorBurnsNoFuelAndLosingCapacitorsSpillsTheExcess()
    {
        var bank = new PowerBank();
        bank.Configure(supplyMW: 40f, fuelledMW: 0f, capacityMJ: 64f, fill: true);
        Assert.True(bank.TryDraw(4f));
        Assert.Equal(0f, bank.Charge(1f));
        Assert.Equal(64f, bank.StoredMJ, 3);
        bank.Configure(40f, 0f, 40f);
        Assert.Equal(40f, bank.StoredMJ, 3);
        Assert.False(bank.Charge(1f, running: false) > 0);
    }

    [Fact]
    public void RailgunsAreFastFlatAndHitHard()
    {
        VehicleAnalysis a = A(Railgun(8, ("generator", new Int3(1, 0, 0)), ("capacitor", new Int3(-1, 0, 0)),
            ("capacitor", new Int3(-1, 0, 1)), ("fuel_tank", new Int3(1, 0, 1))));
        GunInfo rail = Assert.Single(a.Guns);
        Assert.True(rail.Valid);
        Assert.Equal("Railgun", rail.Label);
        Assert.True(rail.Stats.MuzzleVelocity > 2000f);
        Assert.Equal(8, rail.Barrels.Count);
        Assert.True(rail.ApRounds > 0);
        Assert.Equal(0, rail.HeRounds);

        GunStats heavy = GunMath.Compute(BlockCatalog.Heavy120, 8, false, 0, 0);
        Assert.True(rail.Stats.ApPenMm > heavy.ApPenMm);
        Assert.True(rail.Stats.DragK < heavy.DragK);
        Assert.DoesNotContain(a.Warnings, w => w.Severity == Severity.Error && w.Text.Contains("Railgun"));
        Assert.Contains(a.Warnings, w => w.Severity == Severity.Info && w.Text.StartsWith("Power:"));
    }

    [Fact]
    public void EnergyWeaponsNeedPowerAndRoomForAShot()
    {
        VehicleAnalysis none = A(Railgun(4));
        Assert.Contains(none.Warnings, w => w.Severity == Severity.Error && w.Text.Contains("needs electricity"));

        // A generator alone holds 1.5 MJ: not enough for a 20 MJ shot.
        VehicleAnalysis small = A(Railgun(4, ("generator", new Int3(1, 0, 0)), ("fuel_tank", new Int3(1, 0, 1))));
        Assert.Equal(1.5f, small.StorageMJ, 2);
        Assert.Contains(small.Warnings, w => w.Severity == Severity.Error && w.Text.Contains("Capacitor Banks"));

        VehicleAnalysis dry = A(Railgun(4, ("generator", new Int3(1, 0, 0)), ("capacitor", new Int3(-1, 0, 0)),
            ("capacitor", new Int3(-1, 0, 1))));
        Assert.Contains(dry.Warnings, w => w.Text.Contains("burn fuel") || w.Text.StartsWith("No fuel tank"));
    }

    [Fact]
    public void BreechesOnlyTakeTheirOwnKindOfBarrel()
    {
        VehicleAnalysis wrong = A(new[]
        {
            new BlueprintBlock("rail_breech", new Int3(0, 0, 0), 0),
            new BlueprintBlock("barrel", new Int3(0, 0, -1), 0),
        });
        GunInfo g = Assert.Single(wrong.Guns);
        Assert.False(g.Valid);
        Assert.Contains(wrong.Warnings, w => w.Text.Contains("no rails") && w.Text.Contains("doesn't fit"));

        VehicleAnalysis cannon = A(new[]
        {
            new BlueprintBlock("breech_76", new Int3(0, 0, 0), 0),
            new BlueprintBlock("rail", new Int3(0, 0, -1), 0),
        });
        Assert.False(Assert.Single(cannon.Guns).Valid);
    }

    [Fact]
    public void LasersNeedNoAmmoAndLensesReachFurther()
    {
        VehicleAnalysis one = A(new[] { new BlueprintBlock("laser_emitter", new Int3(0, 0, 0), 0), new BlueprintBlock("laser_lens", new Int3(0, 0, -1), 0) });
        VehicleAnalysis three = A(new[]
        {
            new BlueprintBlock("laser_emitter", new Int3(0, 0, 0), 0), new BlueprintBlock("laser_lens", new Int3(0, 0, -1), 0),
            new BlueprintBlock("laser_lens", new Int3(0, 0, -2), 0), new BlueprintBlock("laser_lens", new Int3(0, 0, -3), 0),
        });
        GunInfo l1 = Assert.Single(one.Guns), l3 = Assert.Single(three.Guns);
        Assert.True(l1.Valid);
        Assert.True(BlockCatalog.Laser.Beam && BlockCatalog.Laser.PointDefense);
        Assert.Equal(0, l1.ApRounds + l1.HeRounds + l1.FlakRounds);
        Assert.True(l3.Stats.BeamRangeM > l1.Stats.BeamRangeM);
        Assert.Equal(0f, l1.Stats.RecoilImpulse);
    }

    [Fact]
    public void RocketLaunchersFireTwoRocketsPerTubeAtTheirOwnSpeed()
    {
        var blocks = new List<BlueprintBlock> { new("rocket_pod", new Int3(0, 0, 0), 0), new("ammo_he", new Int3(0, 0, 1), 0), new("ammo_ap", new Int3(1, 0, 0), 0) };
        for (int i = 1; i <= 4; i++) blocks.Add(new BlueprintBlock("rocket_tube", new Int3(0, 0, -i), 0));
        GunInfo g = Assert.Single(A(blocks).Guns);
        Assert.True(g.Valid);
        Assert.Equal(8, g.Stats.Burst);
        Assert.Equal(BlockCatalog.Rocket122.BaseVelocity, g.Stats.MuzzleVelocity);
        Assert.Equal(0, g.ApRounds);
        Assert.True(g.HeRounds >= 8);
    }

    [Fact]
    public void VlsCellsMustStandUprightWithAClearHatch()
    {
        VehicleAnalysis ok = A(new[] { new BlueprintBlock("vls_strike", new Int3(0, 0, 0), 0), new BlueprintBlock("vls_sam", new Int3(1, 0, 0), 0) });
        Assert.Equal(2, ok.Missiles.Count);
        Assert.All(ok.Missiles, m => Assert.True(m.Valid && m.IsVls && m.Forward == Int3.Up));
        MissileInfo strike = ok.Missiles.First(m => m.Vls == VlsLoad.Strike), sam = ok.Missiles.First(m => m.Vls == VlsLoad.Sam);
        Assert.Equal(SeekerKind.Navigation, strike.Stats.Seeker);
        Assert.Equal(SeekerKind.Radar, sam.Stats.Seeker);
        Assert.True(strike.Stats.RangeM > 10000f);
        Assert.True(strike.Stats.Damage > sam.Stats.Damage && sam.Stats.MaxTurnAccel > strike.Stats.MaxTurnAccel);

        VehicleAnalysis covered = A(new[] { new BlueprintBlock("vls_strike", new Int3(0, 0, 0), 0), new BlueprintBlock("armor_light", new Int3(0, 1, 0), 0) });
        Assert.False(Assert.Single(covered.Missiles).Valid);
        int sideways = Orientations.RotateAroundZ(0, 1);
        VehicleAnalysis tipped = A(new[] { new BlueprintBlock("vls_sam", new Int3(0, 0, 0), sideways) });
        Assert.False(Assert.Single(tipped.Missiles).Valid);
    }

    [Fact]
    public void GatlingsFireBurstsAndHuntMissiles()
    {
        GunStats s = GunMath.Compute(BlockCatalog.Gatling20, 3, false, 0, 0);
        Assert.Equal(5, s.Burst);
        Assert.True(s.Burst / s.ReloadS >= 50f);
        Assert.True(BlockCatalog.Gatling20.PointDefense);
        Assert.False(BlockCatalog.Heavy120.PointDefense);
    }

    [Fact]
    public void Missile_launch_cells_come_loaded()
    {
        // The Trident's cells and the Palisade's box launchers hold their missiles from the start, not only after a resupply.
        foreach (Blueprint bp in new[] { ExampleDesigns.Trident(), ExampleDesigns.PalisadeLauncher() })
        {
            GridModel m = GridModel.FromBlueprint(bp);
            List<BlockInstance> cells = m.Instances.Where(b => b.Def.Kind == BlockKind.VlsCell).ToList();
            Assert.NotEmpty(cells);
            Assert.All(cells, c => Assert.Equal(c.Def.AmmoUnits, c.AmmoUnits));
        }
    }
}
