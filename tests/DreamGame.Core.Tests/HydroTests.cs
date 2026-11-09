using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DreamGame.Core.Tests;

public class HydroTests
{
    /// <summary>A closed box of one block type, hollow inside.</summary>
    private static Dictionary<Int3, BlockDef> Box(string id, int sx, int sy, int sz, bool hollow = true)
    {
        var cells = new Dictionary<Int3, BlockDef>();
        BlockDef d = BlockCatalog.Get(id);
        for (int x = 0; x < sx; x++)
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        {
            bool shell = x == 0 || y == 0 || z == 0 || x == sx - 1 || y == sy - 1 || z == sz - 1;
            if (shell || !hollow) cells[new Int3(x, y, z)] = d;
        }

        return cells;
    }

    private static float Mass(Dictionary<Int3, BlockDef> cells) => cells.Values.Sum(d => d.MassKg);

    [Fact]
    public void HollowHullsFloatAndSolidHeavyBlocksSink()
    {
        var hull = Box("armor_light", 6, 5, 10);
        List<Compartment> comps = Hydro.FindCompartments(hull);
        Compartment c = Assert.Single(comps);
        Assert.Equal(4 * 3 * 8, c.Cells.Count);
        HydroStats s = Hydro.Analyze(hull, comps, Mass(hull));
        Assert.True(s.Floats);
        Assert.InRange(s.DraftM, 0.05f, 2.5f);

        var solid = Box("armor_heavy", 3, 3, 3, hollow: false);
        Assert.False(Hydro.Analyze(solid, Hydro.FindCompartments(solid), Mass(solid)).Floats);
    }

    [Fact]
    public void FramesLetWaterThroughSoAFrameHullDoesNotSeal()
    {
        var frame = Box("frame", 5, 5, 5);
        Assert.Empty(Hydro.FindCompartments(frame));
    }

    [Fact]
    public void AHoleFloodsItsCompartmentAndWeldingItShutLetsPumpsWin()
    {
        var hull = Box("armor_light", 6, 5, 10);
        List<Compartment> comps = Hydro.FindCompartments(hull);
        var alive = new HashSet<Int3>(hull.Keys);
        var hole = new Int3(0, 1, 4);
        alive.Remove(hole);
        bool Watertight(Int3 c) => alive.Contains(c) && hull[c].Watertight;
        Hydro.UpdateBreaches(comps, hull, Watertight);
        Compartment c = comps[0];
        Assert.True(c.Breached);
        Assert.Equal(1, c.Holes);

        for (int i = 0; i < 60; i++) Hydro.Step(comps, 0.1f, _ => 1.5f, pumps: 1, ballastCommand: 0);
        float flooded = c.Water;
        Assert.True(flooded > 0.5f, $"only {flooded} m³ came in");

        alive.Add(hole);
        Hydro.UpdateBreaches(comps, hull, Watertight);
        Assert.False(c.Breached);
        for (int i = 0; i < 600; i++) Hydro.Step(comps, 0.1f, _ => 1.5f, pumps: 1, ballastCommand: 0);
        Assert.True(c.Water < flooded);
    }

    [Fact]
    public void FloodedCompartmentsStopFloating()
    {
        var hull = Box("armor_light", 6, 5, 10);
        List<Compartment> comps = Hydro.FindCompartments(hull);
        float mass = Mass(hull);
        float before = Hydro.Analyze(hull, comps, mass).DraftM;
        comps[0].Water = comps[0].CapacityM3 * 0.5f;
        float after = Hydro.Analyze(hull, comps, mass).DraftM;
        Assert.True(after > before, "taking on water should make it sit lower");
    }

    [Fact]
    public void CorsairFloatsOnFourCompartmentsWithPropsUnderwater()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Corsair());
        Assert.Equal(MobilityMode.Ship, a.Mode);
        Assert.True(a.Water.Floats);
        Assert.Equal(4, a.Compartments.Count(c => c.Cells.Count > 50));
        Assert.All(a.ShipPropellers, p => Assert.True(p.Y * Blueprint.CellSize < a.Water.WaterlineY));
        Assert.Equal(2, a.TorpedoTubes.Count);
        Assert.All(a.TorpedoTubes, t => Assert.True(t.Rounds > 0));

        // Losing a whole compartment doesn't sink her.
        a.Compartments[1].Water = a.Compartments[1].CapacityM3;
        var cells = a.Defs.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(Hydro.Analyze(cells, a.Compartments, a.MassKg).Floats);
    }

    [Fact]
    public void BarracudaDivesWithTanksFloodedAndSurfacesWithThemBlown()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Barracuda());
        Assert.Equal(MobilityMode.Submarine, a.Mode);
        Assert.True(a.Water.CanDive, $"mass {a.MassKg / 1000:0.0} t, displacement {a.Water.DisplacementT:0.0} t, ballast {a.Water.BallastT:0.0} t");
        Assert.True(a.Water.CanSurface);
        Assert.Equal(3, a.Compartments.Count(c => c.IsBallast));
    }

    [Fact]
    public void BallastValvesFloodAndBlowTheirTanks()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Barracuda());
        List<Compartment> comps = a.Compartments;
        for (int i = 0; i < 400; i++) Hydro.Step(comps, 0.1f, _ => 3f, pumps: 2, ballastCommand: 1);
        Assert.All(comps.Where(c => c.IsBallast), c => Assert.Equal(c.CapacityM3, c.Water, 2));
        for (int i = 0; i < 400; i++) Hydro.Step(comps, 0.1f, _ => 3f, pumps: 2, ballastCommand: -1);
        Assert.All(comps.Where(c => c.IsBallast), c => Assert.Equal(0f, c.Water, 2));
    }

    [Fact]
    public void WaterOnlyComesInThroughHolesUnderTheSurfaceAndNeverRisesAboveTheSea()
    {
        var hull = Box("armor_light", 6, 5, 10);
        List<Compartment> comps = Hydro.FindCompartments(hull);
        var alive = new HashSet<Int3>(hull.Keys);
        alive.Remove(new Int3(0, 3, 4));
        Hydro.UpdateBreaches(comps, hull, c => alive.Contains(c) && hull[c].Watertight);
        Compartment c = comps[0];
        Assert.Contains(new Int3(0, 3, 4), c.HoleCells);

        // A hole above the waterline lets nothing in.
        for (int i = 0; i < 100; i++) Hydro.Step(comps, 0.1f, _ => -0.4f, pumps: 0, ballastCommand: 0);
        Assert.Equal(0f, c.Water);

        // Under water it floods, but only up to the level of the sea outside.
        float limit = c.CapacityM3 * 0.4f;
        for (int i = 0; i < 2000; i++) Hydro.Step(comps, 0.1f, _ => 1f, pumps: 0, ballastCommand: 0, floodLimit: _ => limit);
        Assert.Equal(limit, c.Water, 2);

        // If the hull rises, the extra drains back out of the hole.
        for (int i = 0; i < 2000; i++) Hydro.Step(comps, 0.1f, _ => 0.2f, pumps: 0, ballastCommand: 0, floodLimit: _ => limit / 2);
        Assert.Equal(limit / 2, c.Water, 2);
    }

    [Fact]
    public void FloodEveryCompartmentAndTheCorsairGoesDown()
    {
        // Ships float on their sealed air, not their steel: torpedoes have to be able to sink them.
        VehicleAnalysis a = VehicleAnalysis.Analyze(ExampleDesigns.Corsair());
        foreach (Compartment c in a.Compartments) c.Water = c.CapacityM3;
        var cells = a.Defs.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.False(Hydro.Analyze(cells, a.Compartments, a.MassKg).Floats);
    }

    [Theory]
    [InlineData("armor_light", false)]
    [InlineData("armor_heavy", false)]
    [InlineData("lead_ballast", false)]
    [InlineData("engine_large", false)]
    [InlineData("fuel_tank", true)]
    public void LooseBlocksFloatOnlyIfLighterThanTheWaterTheyPushAside(string id, bool floats)
    {
        BlockDef d = BlockCatalog.Get(id);
        float buoyancyKg = d.Displacement * Hydro.CellVolume * Hydro.WaterDensity;
        Assert.Equal(floats, buoyancyKg > d.MassKg);
    }

    [Fact]
    public void MachineryInsideAHullIsPartOfItsCompartmentNotAWall()
    {
        var hull = Box("armor_light", 6, 5, 10);
        hull[new Int3(2, 1, 4)] = BlockCatalog.Get("engine_large");
        hull[new Int3(3, 1, 4)] = BlockCatalog.Get("driver_seat");
        Compartment c = Assert.Single(Hydro.FindCompartments(hull));
        Assert.Contains(new Int3(2, 1, 4), c.Cells);
        Assert.True(c.CapacityM3 < c.Cells.Count * Hydro.CellVolume, "the engine takes up some of the air");
    }
}
