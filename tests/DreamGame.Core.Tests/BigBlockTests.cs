using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DreamGame.Core.Tests;

public class BigBlockTests
{
    private static BlockDef Def(string id) => BlockCatalog.Get(id);

    [Fact]
    public void ABigBlockFillsABoxAroundWhereItWasPlaced()
    {
        BlockDef cube = Def("armor_block");
        Int3[] cells = Footprint.CellArray(cube, new Int3(5, 0, 5), 0);
        Assert.Equal(8, cells.Length);
        Assert.Equal(8, cells.Distinct().Count());
        Assert.Contains(new Int3(5, 0, 5), cells);
        Assert.Contains(new Int3(6, 1, 6), cells);

        // Turned, it covers the turned box; a 3×3×3 is centered on its anchor.
        BlockDef gun = Def("breech_406");
        Int3[] g = Footprint.CellArray(gun, Int3.Zero, Orientations.RotateAroundY(0, 1));
        Assert.Equal(27, g.Length);
        Assert.All(g, c => Assert.True(System.Math.Abs(c.X) <= 1 && System.Math.Abs(c.Y) <= 1 && System.Math.Abs(c.Z) <= 1));
        Assert.Equal(System.Numerics.Vector3.Zero, Footprint.Center(gun, Int3.Zero, 0));
        Assert.Equal(1, Footprint.Reach(gun, 0, Int3.Forward));
    }

    [Fact]
    public void BlueprintsListEachBigBlockOnceAndDropOverlaps()
    {
        var bp = Blueprint.FromCellMap("t", new[]
        {
            new BlueprintBlock("armor_block", new Int3(0, 0, 0)),
            new BlueprintBlock("armor_light", new Int3(3, 0, 0)),
        });
        Dictionary<Int3, BlueprintBlock> map = bp.ToCellMap();
        Assert.Equal(9, map.Count);
        Assert.Same(map[new Int3(0, 0, 0)], map[new Int3(1, 1, 1)]);
        Assert.Equal(2, Blueprint.FromCellMap("again", map.Values).Blocks.Count);

        string json = "{\"format\":1,\"name\":\"x\",\"blocks\":[{\"id\":\"armor_block\",\"x\":0,\"y\":0,\"z\":0,\"o\":0},{\"id\":\"armor_light\",\"x\":1,\"y\":1,\"z\":1,\"o\":0}]}";
        var problems = new List<string>();
        Blueprint loaded = Blueprint.FromJson(json, problems);
        Assert.Single(loaded.Blocks);
        Assert.Contains(problems, p => p.Contains("overlapping"));
    }

    [Fact]
    public void ABigBlockIsOneThingInTheGrid()
    {
        var g = new GridModel();
        BlockInstance big = g.Add(Def("armor_block"), Int3.Zero, 0);
        g.Add(Def("armor_light"), new Int3(2, 0, 0), 0);
        Assert.Equal(9, g.Blocks.Count);
        Assert.Equal(2, g.InstanceCount);
        Assert.Same(big, g.Blocks[new Int3(1, 1, 1)]);
        Assert.Equal(1760 + 60, g.TotalMassKg());

        // Its mass sits at its middle, not its anchor corner.
        var solo = new GridModel();
        solo.Add(Def("armor_block"), Int3.Zero, 0);
        Assert.Equal(new System.Numerics.Vector3(0.25f, 0.25f, 0.25f), solo.CenterOfMass());

        g.Remove(big);
        Assert.Single(g.Blocks);
        Assert.Equal(1, g.InstanceCount);
    }

    [Fact]
    public void ABigBlockHoldsThingsTogetherUntilItGoes()
    {
        var g = new GridModel();
        g.Add(Def("armor_light"), new Int3(-1, 0, 0), 0);
        BlockInstance bridge = g.Add(Def("armor_block"), new Int3(0, 0, 0), 0);
        g.Add(Def("armor_light"), new Int3(2, 1, 1), 0);
        Assert.Single(Connectivity.Components(g));
        g.Remove(bridge);
        Assert.Equal(2, Connectivity.Components(g).Count);
    }

    [Fact]
    public void ABlastHitsABigBlockOnce()
    {
        var g = new GridModel();
        BlockInstance big = g.Add(Def("armor_block"), Int3.Zero, 0);
        var hurt = new List<BlockInstance>();
        DamageModel.ApplyBlast(g.Hull, new System.Numerics.Vector3(0.5f, 0.5f, 3f), 3f, 2000f, new List<BlockInstance>(), hurt);
        Assert.Single(hurt);
        Assert.Same(big, hurt[0]);
        Assert.True(big.Hp < big.Def.Hp);
    }

    [Fact]
    public void ABattleshipGunFindsItsBarrelsAndMagazines()
    {
        var blocks = new List<BlueprintBlock>
        {
            new("breech_406", Int3.Zero),
            new("magazine_ap", new Int3(-3, -1, 0)),
            new("magazine_he", new Int3(2, -1, 0)),
            new("driver_seat", new Int3(0, -2, 0)),
        };
        for (int z = -2; z >= -21; z--) blocks.Add(new BlueprintBlock("heavy_barrel", new Int3(0, 0, z)));
        VehicleAnalysis a = VehicleAnalysis.Analyze(Blueprint.FromCellMap("gun", blocks));
        GunInfo gun = Assert.Single(a.Guns);
        Assert.True(gun.Valid);
        Assert.Equal(20, gun.Barrels.Count);
        Assert.Equal(8, gun.ApRounds);
        Assert.Equal(8, gun.HeRounds);
        Assert.InRange(gun.Stats.MuzzleVelocity, 750f, 900f);
        Assert.True(gun.Stats.ApPenMm > 450f);
        // Mass counts each big block once.
        Assert.InRange(a.MassKg, blocks.Sum(b => BlockCatalog.Get(b.Id).MassKg) - 1f, blocks.Sum(b => BlockCatalog.Get(b.Id).MassKg) + 1f);
    }

    [Fact]
    public void BigBlocksAreRebuiltWhole()
    {
        var bp = Blueprint.FromCellMap("t", new[]
        {
            new BlueprintBlock("driver_seat", new Int3(0, 0, 0)),
            new BlueprintBlock("armor_block", new Int3(1, 0, 0)),
        });
        VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
        GridModel g = GridModel.FromAnalysis(a);
        g.Remove(g.Blocks[new Int3(2, 1, 1)]);
        List<Int3> missing = Repair.Missing(a, g);
        Assert.Equal(new[] { new Int3(1, 0, 0) }, missing);
        Assert.True(Repair.CanRebuild(g, a, new Int3(1, 0, 0)));
    }

    [Fact]
    public void AHoleIntoASealedNeighbourDoesNotLetTheSeaIn()
    {
        // Two boxes side by side sharing a wall: knock out the shared wall and neither floods.
        var hull = new Dictionary<Int3, BlockDef>();
        BlockDef plate = Def("armor_light");
        for (int x = 0; x < 9; x++)
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 4; z++)
        {
            bool shell = x == 0 || x == 8 || x == 4 || y == 0 || y == 3 || z == 0 || z == 3;
            if (shell) hull[new Int3(x, y, z)] = plate;
        }

        List<Compartment> comps = Hydro.FindCompartments(hull);
        Assert.Equal(2, comps.Count);
        var gone = new HashSet<Int3> { new(4, 1, 1) };
        Hydro.UpdateBreaches(comps, hull, c => hull.ContainsKey(c) && !gone.Contains(c));
        Assert.All(comps, c => Assert.False(c.Breached));

        // Now open the outer wall of one: both are open to the sea through the gap between them.
        gone.Add(new Int3(0, 1, 1));
        Hydro.UpdateBreaches(comps, hull, c => hull.ContainsKey(c) && !gone.Contains(c));
        Assert.All(comps, c => Assert.True(c.Breached));
    }
}

public class ColossusTests
{
    private static readonly VehicleAnalysis A = VehicleAnalysis.Analyze(ExampleDesigns.Colossus());

    [Fact]
    public void ItIsThreeHundredMetersOfShip()
    {
        Assert.Equal(MobilityMode.Ship, A.Mode);
        Assert.InRange((A.Max.Z - A.Min.Z + 1) * Blueprint.CellSize, 300f, 310f);
        Assert.True(A.Water.Floats);
        Assert.InRange(A.Water.DraftM, 5f, 12f);
        Assert.All(A.ShipPropellers, p => Assert.True(p.Y * Blueprint.CellSize < A.Water.WaterlineY));
        Assert.Empty(A.Disconnected);
    }

    [Fact]
    public void ItCarriesFourBattleshipGunsInTwoTurrets()
    {
        Assert.Equal(2, A.Turrets.Count);
        Assert.Equal(4, A.Guns.Count(g => g.Valid && g.Stats.CaliberMm == 406f));
        Assert.All(A.Guns, g => Assert.True(g.ApRounds > 0 && g.HeRounds > 0));
    }

    [Fact]
    public void ItSurvivesAnyOneCompartmentFlooding()
    {
        var cells = A.Defs.ToDictionary(kv => kv.Key, kv => kv.Value);
        var real = A.Compartments.Where(c => c.Cells.Count > 100).ToList();
        Assert.True(real.Count >= 14);
        foreach (Compartment c in real)
        {
            c.Water = c.CapacityM3;
            Assert.True(Hydro.Analyze(cells, A.Compartments, A.MassKg).Floats);
            c.Water = 0;
        }
    }
}
