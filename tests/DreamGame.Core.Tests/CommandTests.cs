using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DreamGame.Core;
using Xunit;

namespace DreamGame.Core.Tests;

public class CommandTests
{
    [Theory]
    [InlineData(Formation.Line)]
    [InlineData(Formation.Wedge)]
    [InlineData(Formation.Column)]
    [InlineData(Formation.Box)]
    public void FormationsGiveEveryUnitItsOwnRoomySlot(Formation f)
    {
        foreach (int n in new[] { 1, 2, 5, 12, 31 })
        {
            List<Vector2> slots = Formations.Slots(f, n, 20f);
            Assert.Equal(n, slots.Count);
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                Assert.True(Vector2.Distance(slots[i], slots[j]) >= 15f, $"{f}: slots {i} and {j} too close");
            Vector2 mid = slots.Aggregate(Vector2.Zero, (a, b) => a + b) / n;
            Assert.True(mid.Length() < 0.01f, "centered on the destination");
        }
    }

    [Fact]
    public void ALineFacesTheWayItGoesAndAColumnFollowsIt()
    {
        // Heading east (+x): a line spreads north-south, a column strings out east-west.
        List<Vector2> line = Formations.Place(Formation.Line, 5, 20f, new Vector2(100, 50), new Vector2(1, 0));
        Assert.All(line, p => Assert.Equal(100f, p.X, 3));
        Assert.Equal(80f, line.Max(p => p.Y) - line.Min(p => p.Y), 3);
        List<Vector2> col = Formations.Place(Formation.Column, 5, 20f, new Vector2(100, 50), new Vector2(1, 0));
        Assert.All(col, p => Assert.Equal(50f, p.Y, 3));
        // The head of the column is the slot furthest east.
        Assert.Equal(col.Max(p => p.X), col[0].X, 3);
    }

    [Fact]
    public void AWedgeHasItsLeaderInFront()
    {
        List<Vector2> w = Formations.Place(Formation.Wedge, 7, 20f, Vector2.Zero, new Vector2(0, -1));
        // Heading north (-z): the leader has the smallest z.
        Assert.Equal(w.Min(p => p.Y), w[0].Y, 3);
        Assert.Equal(0f, w[0].X, 3);
    }

    [Fact]
    public void UnitsTakeNearbySlots()
    {
        var units = new List<Vector2> { new(-100, 0), new(100, 0), new(0, 0) };
        var slots = new List<Vector2> { new(0, 10), new(-90, 10), new(90, 10) };
        int[] a = Formations.Assign(units, slots);
        Assert.Equal(1, a[0]);
        Assert.Equal(2, a[1]);
        Assert.Equal(0, a[2]);
        Assert.Equal(3, a.Distinct().Count());
    }

    [Fact]
    public void ShipsAndAircraftSeeFurtherThanTanks()
    {
        Assert.True(Sensors.SightM(MobilityMode.Ship, false, false) > Sensors.SightM(MobilityMode.Ground, false, false));
        Assert.True(Sensors.SightM(MobilityMode.Plane, false, false) > Sensors.SightM(MobilityMode.Ground, false, false));
        Assert.True(Sensors.SightM(MobilityMode.Ground, false, true) >= 2500f);
    }
}
