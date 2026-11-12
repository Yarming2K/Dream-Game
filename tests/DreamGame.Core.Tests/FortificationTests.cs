using System.Linq;
using Xunit;

namespace DreamGame.Core.Tests;

/// <summary>The fortification kit and the example buildings built with it.</summary>
public class FortificationTests
{
    [Fact]
    public void NaturalMaterialsAndFieldStoresNeverTakeAPaintJob()
    {
        foreach (string id in new[] { "concrete", "concrete_slope", "brick", "timber", "roof_tile", "earth", "earth_slope", "sandbags", "hesco", "wire", "post", "fence" })
            Assert.False(Liveries.Paints(BlockCatalog.Get(id), Paint.Self), id);
        foreach (string id in new[] { "armor_light", "corrugated", "corrugated_slope", "camo_net", "cupola" })
            Assert.True(Liveries.Paints(BlockCatalog.Get(id), Paint.Self), id);
    }

    [Fact]
    public void TheNewMaterialsComeInShapes()
    {
        foreach (string id in new[] { "earth_slope", "earth_corner", "corrugated_slope", "corrugated_half" })
            Assert.Equal(BlockKind.Armor, BlockCatalog.Get(id).Kind);
        Assert.True(BlockCatalog.Get("earth").HeVulnerability < BlockCatalog.Get("concrete").HeVulnerability);
    }

    [Fact]
    public void ACupolaCountsAsTwoPeriscopes()
    {
        Assert.Equal(2, Fittings.Count(new[] { BlockCatalog.Get("cupola") }).Periscopes);
        Assert.Equal(2, VehicleAnalysis.Analyze(ExampleDesigns.Pillbox()).Kit.Periscopes);
    }

    [Fact]
    public void TheFieldKitHasModels()
    {
        foreach (string id in new[] { "sandbags", "hesco", "wire", "camo_net", "cupola", "post", "fence" })
        {
            BlockModel m = BlockModels.For(BlockCatalog.Get(id));
            Assert.True(m.Parts.Count >= 2, id);
            Assert.DoesNotContain(m.Parts, p => p.Paint == Paint.Glow);
        }
    }

    [Fact]
    public void TheExampleBuildingsAreSoundAndKeepTheirGuns()
    {
        foreach (Blueprint bp in ExampleDesigns.Buildings)
        {
            VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
            Assert.True(a.IsBuilding, bp.Name);
            Assert.Empty(a.Disconnected);
            Assert.DoesNotContain(a.Warnings, w => w.Severity != Severity.Info);
            Assert.All(a.Guns, g => Assert.True(g.Valid, $"{bp.Name}: a gun isn't ready"));
        }
    }

    [Fact]
    public void TurretsDontPickUpSandbagsEarthOrWire()
    {
        string[] fixedThings = { "sandbags", "hesco", "wire", "post", "fence", "camo_net", "cupola" };
        foreach (Blueprint bp in ExampleDesigns.Buildings)
        {
            VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
            foreach (TurretInfo t in a.Turrets)
                Assert.DoesNotContain(a.SectionOf.Where(kv => kv.Value == t.Section),
                    kv => a.Defs[kv.Key] is { } d && (fixedThings.Contains(d.Id) || (d.BaseId ?? d.Id) is "earth" or "concrete"));
        }
    }

    [Fact]
    public void TheBuildingsUseTheKit()
    {
        int Count(Blueprint bp, string id) => bp.Blocks.Count(b => b.Id == id);
        Blueprint pillbox = ExampleDesigns.Pillbox(), outpost = ExampleDesigns.Outpost(), nest = ExampleDesigns.FlakNest();
        Assert.True(Count(pillbox, "earth_slope") > 20 && Count(pillbox, "earth_corner") > 0);
        Assert.Equal(1, Count(pillbox, "cupola"));
        Assert.True(Count(outpost, "hesco") >= 40);
        Assert.Equal(2, Count(outpost, "camo_net"));
        Assert.True(Count(nest, "sandbags") > 40 && Count(nest, "wire") > 50);
        // No medieval battlements left: every parapet's top row is a solid sloped run.
        foreach (Blueprint bp in new[] { ExampleDesigns.FlakTower(), ExampleDesigns.SiegeFortress(), ExampleDesigns.CoastalCitadel() })
            Assert.True(Count(bp, "concrete_corner") >= 4, bp.Name);
    }
}
