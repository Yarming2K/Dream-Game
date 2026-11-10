using System.Collections.Generic;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class FireTests
{
    private static readonly BlockDef Fuel = BlockCatalog.Get("fuel_tank"), Armor = BlockCatalog.Get("armor_light"), Ammo = BlockCatalog.Get("ammo_he");

    /// <summary>A row of blocks along x, as the fire sees them.</summary>
    private static System.Func<Int3, (BlockDef, Int3)?> Row(params BlockDef[] defs) => c =>
        c.Y == 0 && c.Z == 0 && c.X >= 0 && c.X < defs.Length && defs[c.X] != null ? (defs[c.X], c) : null;

    [Fact]
    public void OnlyThingsThatBurnCatchFire()
    {
        Assert.True(Fire.Burns(Fuel) && Fire.Burns(Ammo) && Fire.Burns(BlockCatalog.Get("engine_large")));
        Assert.False(Fire.Burns(Armor) || Fire.Burns(BlockCatalog.Get("barrel")));
        var sim = new FireSim();
        Assert.False(sim.Ignite(new Int3(0, 0, 0), Armor));
        Assert.True(sim.Ignite(new Int3(0, 0, 0), Fuel));
        Assert.False(sim.Ignite(new Int3(0, 0, 0), Fuel));
        Assert.True(Fire.IgniteChance(Fuel, Fuel.Hp) > Fire.IgniteChance(Fuel, 1f));
    }

    [Fact]
    public void AFireBurnsItsBlockAndThenBurnsOut()
    {
        var sim = new FireSim();
        sim.Ignite(new Int3(0, 0, 0), Fuel);
        var damage = new List<(Int3, float)>();
        var lit = new List<Int3>();
        float total = 0;
        for (int i = 0; i < 400 && sim.Count > 0; i++)
        {
            damage.Clear();
            sim.Step(0.1f, Row(Fuel), _ => false, false, damage, lit);
            foreach ((_, float d) in damage) total += d;
        }

        Assert.Equal(0, sim.Count);
        Assert.InRange(total, Fire.BurnDps * Fire.BurnS * 0.95f, Fire.BurnDps * Fire.BurnS * 1.05f);
    }

    [Fact]
    public void CrewPutFiresOutSooner()
    {
        var crewed = new FireSim();
        var alone = new FireSim();
        crewed.Ignite(new Int3(0, 0, 0), Fuel, crewed: true);
        alone.Ignite(new Int3(0, 0, 0), Fuel);
        var damage = new List<(Int3, float)>();
        var lit = new List<Int3>();
        for (int i = 0; i < 200; i++)
        {
            crewed.Step(0.1f, Row(Fuel), _ => false, true, damage, lit);
            alone.Step(0.1f, Row(Fuel), _ => false, false, damage, lit);
        }

        Assert.Equal(0, crewed.Count);
        Assert.Equal(1, alone.Count);
    }

    [Fact]
    public void FiresSpreadToFlammableNeighborsButNotThroughArmor()
    {
        var sim = new FireSim(7);
        sim.Ignite(new Int3(0, 0, 0), Fuel);
        var damage = new List<(Int3, float)>();
        var lit = new List<Int3>();
        for (int i = 0; i < 300; i++) sim.Step(0.1f, Row(Fuel, Fuel, Armor, Fuel), _ => false, false, damage, lit);
        Assert.Contains(new Int3(1, 0, 0), lit);
        Assert.DoesNotContain(new Int3(2, 0, 0), lit);
        Assert.DoesNotContain(new Int3(3, 0, 0), lit);
    }

    [Fact]
    public void WaterAndTheTorchPutFiresOut()
    {
        var sim = new FireSim();
        sim.Ignite(new Int3(0, 0, 0), Fuel);
        sim.Ignite(new Int3(1, 0, 0), Ammo);
        var damage = new List<(Int3, float)>();
        var lit = new List<Int3>();
        sim.Step(0.1f, Row(Fuel, Ammo), c => c.X == 0, false, damage, lit);
        Assert.False(sim.IsBurning(new Int3(0, 0, 0)));
        Assert.True(sim.IsBurning(new Int3(1, 0, 0)));
        Assert.False(sim.Douse(new Int3(1, 0, 0), Fire.ExtinguishS * 0.5f));
        Assert.True(sim.Douse(new Int3(1, 0, 0), Fire.ExtinguishS * 0.6f));
        Assert.Equal(0, sim.Count);
    }

    [Fact]
    public void AFireOnABlockThatsGoneGoesOut()
    {
        var sim = new FireSim();
        sim.Ignite(new Int3(0, 0, 0), Fuel);
        sim.Step(0.1f, _ => null, _ => false, false, new List<(Int3, float)>(), new List<Int3>());
        Assert.Equal(0, sim.Count);
    }
}

public class FloodSurfaceTests
{
    [Fact]
    public void WaterFillsTheLowestLayerFirst()
    {
        var c = new Compartment { CapacityM3 = 8 * 0.125f };
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
        for (int z = 0; z < 2; z++)
            c.Cells.Add(new Int3(x, y, z));
        Assert.Equal(float.NegativeInfinity, Flood.SurfaceY(c));
        c.Water = c.CapacityM3 * 0.25f;
        Assert.Equal(0f, Flood.SurfaceY(c), 3);
        c.Water = c.CapacityM3 * 0.5f;
        Assert.Equal(0.5f, Flood.SurfaceY(c), 3);
        c.Water = c.CapacityM3 * 0.75f;
        Assert.Equal(1f, Flood.SurfaceY(c), 3);
        c.Water = c.CapacityM3;
        Assert.Equal(1.5f, Flood.SurfaceY(c), 3);
    }
}
