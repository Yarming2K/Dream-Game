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
}
