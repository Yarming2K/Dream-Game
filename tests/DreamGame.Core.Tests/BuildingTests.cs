using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Xunit;
using Xunit.Abstractions;

namespace DreamGame.Core.Tests;

public class BuildingTests
{
    private readonly ITestOutputHelper _out;
    public BuildingTests(ITestOutputHelper o) => _out = o;

    public static IEnumerable<object[]> Names() => ExampleDesigns.Buildings.Select(b => new object[] { b.Name });

    [Theory]
    [MemberData(nameof(Names))]
    public void ExampleBuildingsStandAndFight(string name)
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Buildings.First(b => b.Name == name));
        foreach (DesignWarning w in a.Warnings) _out.WriteLine(w.ToString());
        Assert.Equal(MobilityMode.Building, a.Mode);
        Assert.True(a.IsBuilding && a.CanDrive);
        Assert.DoesNotContain(a.Warnings, w => w.Severity != Severity.Info);
        Assert.Empty(a.Disconnected);
        Assert.NotEmpty(a.Guns);
        Assert.All(a.Guns, g => Assert.True(g.Valid && (g.ApRounds + g.HeRounds + g.FlakRounds > 0 || g.BreechDef.Breech.Beam)));
        Assert.Contains(a.Seats, s => s.Role == SeatRole.Gunner);
    }

    [Fact]
    public void AFoundationMakesABuildingWithoutAnyVehicleParts()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(Blueprint.FromCellMap("hut", new[]
        {
            new BlueprintBlock("foundation", new Int3(0, 0, 0), 0), new BlueprintBlock("concrete", new Int3(0, 1, 0), 0),
        }));
        Assert.True(a.IsBuilding);
        Assert.Equal(0, a.ErrorCount);

        // Up off the ground it's just a block of stone, not a building.
        VehicleAnalysis lifted = VehicleAnalysis.Analyze(Blueprint.FromCellMap("odd", new[]
        {
            new BlueprintBlock("concrete", new Int3(0, 0, 0), 0), new BlueprintBlock("foundation", new Int3(0, 1, 0), 0),
        }));
        Assert.False(lifted.IsBuilding);
    }

    [Fact]
    public void BuildingGunsNeedAGunner()
    {
        var blocks = new List<BlueprintBlock>
        {
            new("foundation", new Int3(0, 0, 0), 0), new("breech_37", new Int3(0, 1, 0), 0), new("barrel", new Int3(0, 1, -1), 0),
            new("ammo_ap", new Int3(0, 1, 1), 0),
        };
        VehicleAnalysis a = VehicleAnalysis.Analyze(Blueprint.FromCellMap("gun", blocks));
        Assert.Contains(a.Warnings, w => w.Text.Contains("Gunner Seat"));
    }

    [Fact]
    public void TheOutpostHasItsInteriorItems()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Outpost());
        Assert.Equal(2, a.Kit.Bunks);
        Assert.Equal(1, a.Kit.Lockers);
        Assert.Equal(1, a.Kit.Workbenches);
        Assert.Equal(2, a.Kit.SupplyCrates);
        Assert.Equal(1, a.Kit.FirstAid);
        Assert.True(BlockCatalog.Get("concrete").HeVulnerability < BlockCatalog.Get("armor_heavy").HeVulnerability);
        Assert.NotNull(BlockCatalog.Get("concrete_slope"));
    }
}
