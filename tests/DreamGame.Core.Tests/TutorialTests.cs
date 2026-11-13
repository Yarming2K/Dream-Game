using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DreamGame.Core.Tests;

/// <summary>The boot camp's steps and car, and the first-time tips.</summary>
public class TutorialTests
{
    /// <summary>The boot camp car with every part the boot camp suggests, at the spots it highlights.</summary>
    private static Blueprint Finished(params string[] steps) => Finished(Courses.Land, steps);

    private static Blueprint Finished(TutorialCourse course, params string[] steps)
    {
        Blueprint bp = course.Starter();
        var cells = bp.ToCellMap().Values.Distinct().ToList();
        foreach (TutorialStep s in course.Steps.Where(s => steps.Length == 0 || steps.Contains(s.Id)))
        foreach (TutorialSpot spot in s.Spots)
            cells.Add(new BlueprintBlock(spot.Part, spot.Cell, spot.O));
        return Blueprint.FromCellMap(bp.Name, cells);
    }

    [Fact]
    public void The_car_starts_unable_to_drive_so_the_steps_mean_something()
    {
        VehicleAnalysis a = VehicleAnalysis.Analyze(BootCamp.Car());
        Assert.False(a.HasDriver);
        Assert.Equal(0f, a.PowerKw);
        Assert.Equal(0f, a.FuelL);
        Assert.False(a.CanDrive);
        Assert.Empty(a.Guns);
        Assert.Empty(a.Disconnected);
    }

    [Fact]
    public void The_suggested_spots_are_free_and_make_a_car_that_drives_and_shoots()
    {
        var chassis = BootCamp.Car().ToCellMap();
        foreach (TutorialSpot spot in Courses.Land.Steps.SelectMany(s => s.Spots))
        {
            Assert.True(BlockCatalog.TryGet(spot.Part, out _), spot.Part);
            Assert.False(chassis.ContainsKey(spot.Cell), $"{spot.Part} at {spot.Cell} is already taken");
        }

        VehicleAnalysis a = VehicleAnalysis.Analyze(Finished("driver", "engine", "fuel", "gun"));
        Assert.True(a.CanDrive, string.Join("; ", a.Warnings));
        Assert.Equal(0, a.ErrorCount);
        Assert.Empty(a.Disconnected);
        GunInfo gun = Assert.Single(a.Guns);
        Assert.True(TutorialFacts.Works(gun), $"valid {gun.Valid}, blocked {gun.MuzzleBlocked}, AP {gun.ApRounds}");
        Assert.Equal(4, gun.Barrels.Count);
    }

    [Fact]
    public void Each_build_step_is_done_by_fitting_its_part()
    {
        TutorialFacts f = TutorialFacts.FromDesign(VehicleAnalysis.Analyze(Finished("driver")), false);
        Assert.True(f.HasDriver);
        Assert.False(f.HasEngine);
        f = TutorialFacts.FromDesign(VehicleAnalysis.Analyze(Finished("driver", "engine", "fuel")), true);
        Assert.True(f.HasEngine && f.HasFuel && f.CanDrive);
        Assert.Equal(0, f.GunsWorking);
        f = TutorialFacts.FromDesign(VehicleAnalysis.Analyze(Finished("driver", "engine", "fuel", "gun")), true);
        Assert.Equal(1, f.GunsWorking);
    }

    [Fact]
    public void The_boot_camp_goes_one_step_at_a_time_and_only_when_each_is_done()
    {
        var camp = new BootCamp();
        Assert.Equal("look", camp.Current.Id);
        // Everything done at once still moves one step per check.
        var all = new TutorialFacts
        {
            InBuilder = true, CameraMoved = true, HasDriver = true, HasEngine = true, HasFuel = true, CanDrive = true, GunsWorking = 1,
            OnRange = true, DrivenM = 100, TargetsDown = 1, AmmoSwitches = 1, OptionsOpened = 1, OnFoot = true,
        };
        Assert.Equal("look", camp.Update(all)?.Id);
        Assert.Equal("driver", camp.Current.Id);

        // Nothing done: it stays put.
        Assert.Null(camp.Update(new TutorialFacts { InBuilder = true }));
        Assert.Equal("driver", camp.Current.Id);

        var seen = new List<string>();
        while (!camp.Finished) seen.Add(camp.Update(all).Id);
        Assert.Equal(Courses.Land.Steps.Skip(1).Select(s => s.Id), seen);
        Assert.Null(camp.Current);
        Assert.Null(camp.Update(all));
    }

    [Fact]
    public void Range_steps_count_what_happened_on_the_range()
    {
        var camp = new BootCamp();
        while (camp.Current.Id != "move") camp.Skip();
        Assert.Null(camp.Update(new TutorialFacts { OnRange = true, DrivenM = 20 }));
        Assert.Equal("move", camp.Update(new TutorialFacts { OnRange = true, DrivenM = 55 })?.Id);
        Assert.Null(camp.Update(new TutorialFacts { OnRange = true, DrivenM = 55 }));
        Assert.Equal("shoot", camp.Update(new TutorialFacts { OnRange = true, TargetsDown = 1 })?.Id);
        Assert.Equal(TutorialPlace.Range, camp.Current.Place);
    }

    public static IEnumerable<object[]> AllCourses => Courses.All.Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(AllCourses))]
    public void Every_step_says_what_to_do_and_with_which_keys(string id)
    {
        TutorialCourse c = Courses.Get(id);
        Assert.False(string.IsNullOrWhiteSpace(c.Name + c.Blurb + c.Finale));
        Assert.Equal(c.Steps.Count, c.Steps.Select(s => s.Id).Distinct().Count());
        foreach (TutorialStep s in c.Steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Text));
            Assert.False(string.IsNullOrWhiteSpace(s.Keys));
            if (s.Part != null) Assert.True(BlockCatalog.TryGet(s.Part, out _), s.Part);
            foreach (TutorialSpot spot in s.Spots) Assert.True(BlockCatalog.TryGet(spot.Part, out _), spot.Part);
        }

        // The builder steps come first (ending with taking it out), then the range (ending back in the builder).
        int firstRange = c.Steps.ToList().FindIndex(s => s.Place == TutorialPlace.Range);
        Assert.True(firstRange > 0);
        Assert.Equal("drive", c.Steps[firstRange - 1].Id);
        Assert.All(c.Steps.Skip(firstRange), s => Assert.Equal(TutorialPlace.Range, s.Place));
        Assert.Equal("back", c.Steps[^1].Id);
    }

    [Theory]
    [MemberData(nameof(AllCourses))]
    public void Each_course_starts_unfinished_and_its_spots_finish_it(string id)
    {
        TutorialCourse c = Courses.Get(id);
        Blueprint start = c.Starter();
        VehicleAnalysis before = VehicleAnalysis.Analyze(start);
        Assert.Empty(before.Disconnected);
        var cells = start.ToCellMap();
        foreach (TutorialSpot spot in c.Steps.SelectMany(s => s.Spots))
            Assert.False(cells.ContainsKey(spot.Cell), $"{spot.Part} at {spot.Cell} is already taken");

        VehicleAnalysis after = VehicleAnalysis.Analyze(Finished(c));
        Assert.Empty(after.Disconnected);
        Assert.Equal(0, after.ErrorCount);
        TutorialFacts f = TutorialFacts.FromDesign(after, true, cell => after.Defs.TryGetValue(cell, out BlockDef d) ? d.Id : null, 20);
        // Every builder step but taking it out is done by the parts at its spots (the box tool's walls aside).
        foreach (TutorialStep s in c.Steps.Where(s => s.Place == TutorialPlace.Builder && s.Id != "drive"))
            Assert.True(s.Done(f), $"{id}: step {s.Id} isn't done by its spots");
        if (before.IsBuilding)
        {
            Assert.True(after.IsBuilding);
            Assert.Equal(0, TutorialFacts.FromDesign(before, true).GunsWorking);
            Assert.True(f.HasGunner && f.GunsWorking > 0);
        }
        else
        {
            Assert.False(before.CanDrive);
            Assert.True(after.CanDrive, string.Join("; ", after.Warnings));
        }
    }

    [Fact]
    public void The_sea_and_air_courses_are_their_examples_with_parts_taken_out()
    {
        Assert.Equal(ExampleDesigns.Corsair().Blocks.Count, Finished(Courses.Sea).Blocks.Count);
        Assert.Equal(ExampleDesigns.Kestrel().Blocks.Count, Finished(Courses.Air).Blocks.Count);
        Assert.Equal(VehicleAnalysis.Analyze(ExampleDesigns.Corsair()).Mode, VehicleAnalysis.Analyze(Finished(Courses.Sea)).Mode);
        Assert.Equal(MobilityMode.Plane, VehicleAnalysis.Analyze(Finished(Courses.Air)).Mode);
        Assert.Equal(4, Courses.All.Count);
        Assert.Same(Courses.Land, new BootCamp().Course);
        Assert.Same(Courses.Air, new BootCamp(Courses.Air).Course);
    }

    [Fact]
    public void Tips_show_once_and_not_at_all_when_turned_off()
    {
        var p = new TutorialProgress();
        Assert.True(p.ShouldShow("builder"));
        Assert.False(p.ShouldShow("no-such-tip"));
        p.MarkSeen("builder");
        p.MarkSeen("builder");
        Assert.False(p.ShouldShow("builder"));
        Assert.Single(p.Seen);
        Assert.True(p.ShouldShow("map"));
        p.TipsOff = true;
        Assert.False(p.ShouldShow("map"));
        Assert.All(Tips.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Title + t.Text)));
        Assert.Equal(Tips.All.Count, Tips.All.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void Progress_is_kept_between_games_and_a_damaged_file_starts_afresh()
    {
        var p = new TutorialProgress { Welcomed = true, BootCampDone = true };
        p.MarkSeen("vehicle");
        p.CoursesDone.Add("sea");
        TutorialProgress back = TutorialProgress.FromJson(p.ToJson());
        Assert.True(back.Welcomed);
        Assert.True(back.BootCampDone);
        Assert.False(back.TipsOff);
        Assert.Equal(new[] { "vehicle" }, back.Seen);
        Assert.Equal(new[] { "sea" }, back.CoursesDone);

        TutorialProgress broken = TutorialProgress.FromJson("{ not json");
        Assert.False(broken.Welcomed);
        Assert.Empty(broken.Seen);
        Assert.False(TutorialProgress.FromJson("").Welcomed);
        Assert.NotNull(TutorialProgress.FromJson("{\"Seen\":null}").Seen);
    }
}
