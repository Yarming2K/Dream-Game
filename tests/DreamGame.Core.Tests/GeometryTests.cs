using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace DreamGame.Core.Tests;

public class OrientationTests
{
    [Fact]
    public void ThereAre24DistinctRotations()
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < Orientations.Count; i++) seen.Add(string.Join(",", Orientations.Matrix(i)));
        Assert.Equal(24, seen.Count);
        Assert.Equal(new[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, Orientations.Matrix(0));
    }

    [Fact]
    public void FourQuarterTurnsComeBackAround()
    {
        for (int o = 0; o < Orientations.Count; o++)
        {
            Assert.Equal(o, Orientations.RotateAroundY(o, 4));
            Assert.Equal(o, Orientations.RotateAroundX(Orientations.RotateAroundX(o, 1), 3));
            Assert.Equal(o, Orientations.RotateAroundZ(o, -4));
        }
    }

    [Fact]
    public void QuarterTurnAroundYPointsForwardSideways()
    {
        int o = Orientations.RotateAroundY(0, 1);
        Int3 f = Orientations.Forward(o);
        Assert.Equal(0, f.Y);
        Assert.Equal(1, f.ManhattanLength);
        Assert.NotEqual(Int3.Forward, f);
        Assert.Equal(Int3.Up, Orientations.Up(o));
    }

    [Fact]
    public void MirrorFlipsSidewaysFacingBlocks()
    {
        Assert.Equal(0, Orientations.MirrorX(0));
        int facingRight = Orientations.RotateAroundY(0, -1);
        Assert.Equal(Int3.Right, Orientations.Forward(facingRight));
        Assert.Equal(Int3.Left, Orientations.Forward(Orientations.MirrorX(facingRight)));
        for (int o = 0; o < Orientations.Count; o++) Assert.Equal(o, Orientations.MirrorX(Orientations.MirrorX(o)));
    }
}

public class RaycastTests
{
    private static List<RayCellHit> Cast(Dictionary<Int3, BlueprintBlock> cells, Vector3 origin, Vector3 dir)
    {
        var hits = new List<RayCellHit>();
        Int3 min = cells.Keys.Aggregate(Int3.Min), max = cells.Keys.Aggregate(Int3.Max);
        GridRaycast.Cast(new BlueprintShapeLookup(cells), min, max, origin, Vector3.Normalize(dir), 100, hits);
        return hits;
    }

    private static Dictionary<Int3, BlueprintBlock> One(string id, int o = 0) =>
        new() { [Int3.Zero] = new BlueprintBlock(id, Int3.Zero, o) };

    [Fact]
    public void CubeIsHitOnTheNearFace()
    {
        List<RayCellHit> hits = Cast(One("armor_light"), new Vector3(0, 0, -5), Vector3.UnitZ);
        Assert.Single(hits);
        Assert.Equal(4.5f, hits[0].TEnter, 3);
        Assert.Equal(5.5f, hits[0].TExit, 3);
        Assert.Equal(-Vector3.UnitZ, hits[0].Normal);
    }

    [Fact]
    public void SlopeFaceIsAt45DegreesFromTheFront()
    {
        List<RayCellHit> hits = Cast(One("armor_heavy_slope"), new Vector3(0, 0, -5), Vector3.UnitZ);
        Assert.Single(hits);
        Assert.Equal(5f, hits[0].TEnter, 3);
        Assert.Equal(45f, DamageModel.ImpactAngleDeg(Vector3.UnitZ, hits[0].Normal), 1);
        Assert.True(hits[0].Normal.Y > 0.5f && hits[0].Normal.Z < -0.5f);
    }

    [Fact]
    public void RayThroughTheEmptyHalfOfASlopeMisses()
    {
        // Above the slope face near the front edge: the wedge is empty there.
        List<RayCellHit> hits = Cast(One("armor_light_slope"), new Vector3(-5, 0.3f, -0.4f), Vector3.UnitX);
        Assert.Empty(hits);
        hits = Cast(One("armor_light_slope"), new Vector3(-5, -0.3f, 0.4f), Vector3.UnitX);
        Assert.Single(hits);
    }

    [Fact]
    public void RotatedSlopeFacesTheSide()
    {
        int facingRight = Orientations.RotateAroundY(0, -1);
        List<RayCellHit> hits = Cast(One("armor_light_slope", facingRight), new Vector3(5, 0, 0), -Vector3.UnitX);
        Assert.Single(hits);
        Assert.Equal(45f, DamageModel.ImpactAngleDeg(-Vector3.UnitX, hits[0].Normal), 1);
    }

    [Fact]
    public void HitsComeBackInOrderAlongTheRay()
    {
        var cells = new Dictionary<Int3, BlueprintBlock>();
        for (int z = 0; z < 4; z++) cells[new Int3(0, 0, z)] = new BlueprintBlock("frame", new Int3(0, 0, z));
        cells[new Int3(1, 0, 1)] = new BlueprintBlock("frame", new Int3(1, 0, 1));
        List<RayCellHit> hits = Cast(cells, new Vector3(0.1f, 0.05f, 10), -Vector3.UnitZ);
        Assert.Equal(new[] { 3, 2, 1, 0 }, hits.Select(h => h.Cell.Z));
        Assert.True(hits.Zip(hits.Skip(1)).All(p => p.First.TEnter < p.Second.TEnter));
    }

    [Fact]
    public void DiagonalRayVisitsOnlyCellsItCrosses()
    {
        var cells = new Dictionary<Int3, BlueprintBlock>();
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 3; y++)
            cells[new Int3(x, y, 0)] = new BlueprintBlock("frame", new Int3(x, y, 0));
        List<RayCellHit> hits = Cast(cells, new Vector3(-1, -0.9f, 0), new Vector3(1, 1, 0));
        Assert.Contains(hits, h => h.Cell == new Int3(0, 0, 0));
        Assert.Contains(hits, h => h.Cell == new Int3(2, 2, 0));
        Assert.DoesNotContain(hits, h => h.Cell == new Int3(2, 0, 0));
    }
}
