using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class EmplacementTests
{
    [Fact]
    public void BuildingsFireHeavyWeaponsAtFullRateAndSteadier()
    {
        Mount b = Emplacement.MountOf(building: true, naval: false, air: false, stabilizers: 0, speedMs: 0);
        Assert.Equal(Mount.Building, b);
        Assert.True(Emplacement.CanFire(true, b));
        Assert.Equal(1f, Emplacement.ReloadFactor(true, b));
        Assert.True(Emplacement.SpreadFactor(false, b) < 1f);
    }

    [Fact]
    public void LandVehiclesMustStopAndBrace()
    {
        Mount moving = Emplacement.MountOf(false, false, false, 4, 5f);
        Mount stopped = Emplacement.MountOf(false, false, false, 4, 0.1f);
        Mount noLegs = Emplacement.MountOf(false, false, false, 1, 0f);
        Assert.False(Emplacement.CanFire(true, moving));
        Assert.True(Emplacement.CanFire(true, stopped));
        Assert.False(Emplacement.CanFire(true, noLegs));
        Assert.True(Emplacement.CanFire(false, moving));
        Assert.Equal(Emplacement.VehicleHeavyReload, Emplacement.ReloadFactor(true, stopped));
        Assert.Equal(1f, Emplacement.ReloadFactor(false, stopped));
        Assert.True(Emplacement.SpreadFactor(true, stopped) > 1f);
    }

    [Fact]
    public void ShipsAreBracedButAircraftNeverAre()
    {
        Assert.Equal(Mount.Braced, Emplacement.MountOf(false, true, false, 0, 15f));
        Assert.Equal(Mount.Loose, Emplacement.MountOf(false, false, true, 6, 0f));
    }

    [Fact]
    public void AMortarLobsHigh()
    {
        Assert.True(GunMath.SolveElevation(366, 4000, 0, out float low));
        Assert.True(GunMath.SolveElevationHigh(366, 4000, 0, out float high));
        Assert.True(high > 0.8f && low < 0.6f);
        Assert.True(BlockCatalog.SiegeMortar420.Heavy && BlockCatalog.SiegeMortar420.HighArc);
        Assert.True(BlockCatalog.SiegeMortar420.HeRadiusM >= 7f);
    }

    [Fact]
    public void AHeavyWeaponOnATruckWithoutStabilizersWarns()
    {
        var bp = Blueprint.FromCellMap("x", new List<BlueprintBlock>
        {
            new("siege_mortar", new Int3(0, 2, 0), 0), new("heavy_barrel", new Int3(0, 2, -2), 0), new("magazine_he", new Int3(2, 1, 0), 0),
        });
        VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
        Assert.Contains(a.Warnings, w => w.Text.Contains("Stabilizers"));
        Assert.Equal(4, VehicleAnalysis.Analyze(ExampleDesigns.Juggernaut()).Kit.Stabilizers);
    }

    [Fact]
    public void HeavyFlakFiresOnlyFlak()
    {
        BreechSpec flak = BlockCatalog.HeavyFlak128;
        Assert.True(flak.AntiAir && flak.Heavy && !flak.HighArc);
        Assert.True(flak.MaxElevationDeg >= 85f && flak.HeRadiusM > BlockCatalog.Get("breech_40ac").Breech.HeRadiusM);

        // Fed from AP, HE and flak racks alike, it counts only the flak.
        var blocks = new List<BlueprintBlock>
        {
            new("foundation", new Int3(0, 0, 0), 0), new("breech_128flak", new Int3(0, 1, 0), 0), new("gunner_seat", new Int3(1, 1, 1), 0),
            new("ammo_ap", new Int3(-1, 1, 0), 0), new("ammo_he", new Int3(1, 1, 0), 0), new("ammo_flak", new Int3(0, 1, 1), 0),
        };
        for (int z = -1; z >= -8; z--) blocks.Add(new BlueprintBlock("heavy_barrel", new Int3(0, 1, z), 0));
        VehicleAnalysis a = VehicleAnalysis.Analyze(Blueprint.FromCellMap("flak", blocks));
        GunInfo g = Assert.Single(a.Guns);
        Assert.True(g.FlakRounds > 0);
        Assert.Equal(0, g.ApRounds + g.HeRounds);

        // Without flak it has nothing to fire, and says so.
        blocks.RemoveAll(b => b.Id == "ammo_flak");
        VehicleAnalysis none = VehicleAnalysis.Analyze(Blueprint.FromCellMap("flak", blocks));
        Assert.Contains(none.Warnings, w => w.Text.Contains("has no flak"));
    }

    [Fact]
    public void TheHeavyMortarLobsHighAndFar()
    {
        BreechSpec m = BlockCatalog.Mortar160;
        Assert.True(m.HighArc && m.Heavy && !m.AntiAir);
        Assert.True(m.MinElevationDeg >= 45f);
        GunStats s = GunMath.Compute(m, 2, false, 0, 0);
        // It reaches 4 km at a steep angle.
        Assert.True(GunMath.SolveElevationHigh(s.HeVelocity, 4000, 0, out float high));
        Assert.True(high * 180f / System.MathF.PI >= 45f);

        // Close in, the crew loads a smaller charge and the bomb still comes down steeply onto the target.
        float near = GunMath.LobSpeed(s.HeVelocity, 300, 0);
        Assert.True(near < s.HeVelocity * 0.5f);
        Assert.True(GunMath.SolveElevationHigh(near, 300, 0, out float steep));
        Assert.InRange(steep * 180f / System.MathF.PI, 65f, 75f);
        Assert.Equal(s.HeVelocity, GunMath.LobSpeed(s.HeVelocity, 8000, 0));
    }

    [Fact]
    public void TheFlakTowerAndMortarBatteryCarryTheirGuns()
    {
        VehicleAnalysis tower = VehicleAnalysis.Analyze(ExampleDesigns.FlakTower());
        Assert.Equal(8, tower.Guns.Count(g => g.BreechDef.Breech.AntiAir));
        Assert.True(tower.IsBuilding);
        // Each twin turret turns as one, with both its guns, and the hoist feeds them from the magazines below.
        Assert.Equal(4, tower.Guns.Where(g => g.BreechDef.Breech.AntiAir).Select(g => g.Section).Distinct().Count());
        Assert.All(tower.Guns.Where(g => g.BreechDef.Breech.AntiAir), g => Assert.True(g.FlakRacks.Count >= 2));

        VehicleAnalysis battery = VehicleAnalysis.Analyze(ExampleDesigns.MortarBattery());
        Assert.Equal(4, battery.Guns.Count(g => g.BreechDef.Breech.HighArc));
        // The ammunition line ties every mortar to the bunker's magazines.
        Assert.All(battery.Guns, g => Assert.True(g.HeRacks.Count >= 6));
    }

    [Fact]
    public void BuildingTurretsDontPickUpTheWalls()
    {
        // A turret is everything joined to the block on its ring: no stray wall or battlement should be part of one.
        foreach (Blueprint bp in ExampleDesigns.Buildings)
        {
            VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
            var ids = bp.Blocks.ToDictionary(b => b.Cell, b => b.Id);
            foreach (TurretInfo t in a.Turrets)
                Assert.DoesNotContain(a.SectionOf.Where(kv => kv.Value == t.Section),
                    kv => ids.TryGetValue(kv.Key, out string id) && id is "concrete" or "foundation");
        }
    }
}
