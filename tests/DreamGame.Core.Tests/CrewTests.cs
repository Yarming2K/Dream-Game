using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace DreamGame.Core.Tests;

public class CrewTests
{
    [Fact]
    public void WardenGunnerFiresTheTurretGun()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Warden());
        Assert.Equal(3, a.Seats.Count);
        Assert.True(a.HasCommander);
        Int3 controller = a.GunController[0];
        Assert.Equal(SeatRole.Gunner, a.Seats.Single(s => s.Cell == controller).Role);
        Assert.Equal(a.Turrets[0].Section, a.Seats.Single(s => s.Cell == controller).Section);
    }

    [Fact]
    public void WithoutAGunnerTheDriverAims()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Jackal());
        Assert.Equal(SeatRole.Driver, a.Seats.Single(s => s.Cell == a.GunController[0]).Role);
        Assert.Contains(a.Warnings, w => w.Severity == Severity.Info && w.Text.Contains("No gunner seat"));
    }

    [Fact]
    public void DestroyedBlocksCanBeWeldedBackOnlyWhereTheyAttach()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Warden());
        GridModel g = GridModel.FromAnalysis(a);
        var plate = new Int3(3, 1, 0);
        g.Remove(g.Blocks[plate]);
        Assert.Contains(plate, Repair.Missing(a, g));
        BlockDef armor = BlockCatalog.Get("armor_light");
        Assert.True(Repair.CanRebuild(g, a, plate));
        Assert.False(Repair.CanRebuild(g, a, new Int3(3, 1, 1))); // still there
        Assert.False(Repair.CanRebuild(g, armor, new Int3(9, 9, 9), 0, 0)); // floating in the air

        // The turret ring can be welded back onto the hull, which reattaches the turret's root.
        var ring = new Int3(0, 2, -1);
        g.Remove(g.Blocks[ring]);
        Assert.True(Repair.CanRebuild(g, a, ring));
    }

    [Fact]
    public void LeadAimsWhereTheTargetWillBe()
    {
        Vector3 aim = GunMath.LeadTarget(Vector3.Zero, new Vector3(0, 0, -100), new Vector3(10, 0, 0), 100f);
        Assert.InRange(aim.X, 9.5f, 10.6f);
        Assert.Equal(new Vector3(0, 0, -50), GunMath.LeadTarget(Vector3.Zero, new Vector3(0, 0, -50), Vector3.Zero, 500f));
    }

    [Fact]
    public void BlastsHurtPeopleNearbyAndNotFarAway()
    {
        Assert.Equal(0f, InfantryDamage.FromBlast(10f, 2.2f, 280f));
        Assert.True(InfantryDamage.FromBlast(0.5f, 3.2f, 540f) >= InfantryDamage.MaxHealth);
        Assert.True(InfantryDamage.FromBlast(1f, 2.2f, 280f) > InfantryDamage.FromBlast(3f, 2.2f, 280f));
    }

    [Fact]
    public void RocketsBeatTankArmorAndRiflesDoNot()
    {
        float heavySloped = DamageModel.EffectiveArmor(80, 45);
        Assert.True(InfantryWeapons.Rocket.Stats.HePenMm > heavySloped);
        Assert.True(InfantryWeapons.Rifle.Stats.ApPenMm < BlockCatalog.Get("armor_light").ArmorMm);
    }
}
