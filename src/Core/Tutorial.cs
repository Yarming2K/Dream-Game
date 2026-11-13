using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DreamGame.Core;

/// <summary>Where a boot camp step is done: in the builder or out on the test range.</summary>
public enum TutorialPlace
{
    Builder,
    Range,
}

/// <summary>What the game tells the boot camp about what the player has done so far.</summary>
public struct TutorialFacts
{
    // In the builder.
    public bool InBuilder;
    public bool CameraMoved;
    public bool HasDriver;
    public bool HasEngine;
    public bool HasFuel;
    public bool CanDrive;
    public int GunsWorking;

    // On the range (counted since the range opened).
    public bool OnRange;
    public float DrivenM;
    public int TargetsDown;
    public int AmmoSwitches;
    public int OptionsOpened;
    public bool OnFoot;

    /// <summary>The design facts, from the builder's analysis of what's built.</summary>
    public static TutorialFacts FromDesign(VehicleAnalysis a, bool cameraMoved) => new()
    {
        InBuilder = true,
        CameraMoved = cameraMoved,
        HasDriver = a != null && a.HasDriver,
        HasEngine = a != null && a.PowerKw > 0,
        HasFuel = a != null && a.FuelL > 0,
        CanDrive = a != null && a.CanDrive,
        GunsWorking = a?.Guns.Count(Works) ?? 0,
    };

    /// <summary>A gun that can fire: it has a barrel that isn't blocked and ammunition it can reach.</summary>
    public static bool Works(GunInfo g) => g.Valid && !g.MuzzleBlocked && (g.NeedsPower || g.ApRounds + g.HeRounds + g.FlakRounds > 0);
}

/// <summary>A part the boot camp suggests, and where it fits on the boot camp car.</summary>
public readonly record struct TutorialSpot(Int3 Cell, string Part);

/// <summary>One step of the boot camp: what to do, which keys do it, and how to tell it's done.</summary>
public sealed record TutorialStep(string Id, TutorialPlace Place, string Title, string Text, string Keys, Func<TutorialFacts, bool> Done)
{
    /// <summary>The part to pick for this step, if it's about placing one (the card offers to pick it for you).</summary>
    public string Part { get; init; }

    /// <summary>Where the parts go on the boot camp car, highlighted in the builder.</summary>
    public IReadOnlyList<TutorialSpot> Spots { get; init; } = Array.Empty<TutorialSpot>();
}

/// <summary>
/// The boot camp: about five minutes that take a new player from an empty chassis to driving and shooting what they
/// built. In the builder: look round, fit a driver seat, an engine, fuel and a gun, then take it out. On the range:
/// drive, hit a target, switch ammunition, open the vehicle's options, get out, and go back to the builder. Each step
/// is checked against what the player has actually done, so nothing has to be clicked through.
/// </summary>
public sealed class BootCamp
{
    public static readonly IReadOnlyList<TutorialStep> Steps = new[]
    {
        new TutorialStep("look", TutorialPlace.Builder, "Look around",
            "Hold the right mouse button and drag to swing round the car. The wheel zooms in and out; hold the middle button and drag to slide the view.",
            "RIGHT DRAG · WHEEL · MIDDLE DRAG", f => f.CameraMoved),
        new TutorialStep("driver", TutorialPlace.Builder, "Fit a driver seat",
            "Every vehicle needs a driver. On the parts panel (left), open CONTROL and pick the Driver Seat, then click inside the car at the front left, where the box glows. Right click takes a block off again.",
            "LEFT CLICK place · RIGHT CLICK remove", f => f.HasDriver)
        {
            Part = "driver_seat", Spots = new[] { new TutorialSpot(new Int3(-1, 1, -3), "driver_seat") },
        },
        new TutorialStep("engine", TutorialPlace.Builder, "Fit an engine",
            "Open MOBILITY and pick the Small Engine. Put it at the back of the car. The design panel (right) shows its power going up.",
            "LEFT CLICK place", f => f.HasEngine)
        {
            Part = "engine_small", Spots = new[] { new TutorialSpot(new Int3(0, 1, 2), "engine_small") },
        },
        new TutorialStep("fuel", TutorialPlace.Builder, "Add fuel",
            "Still in MOBILITY: a Fuel Tank, next to the engine. Watch the Problems on the design panel clear as you fix them.",
            "LEFT CLICK place", f => f.HasFuel)
        {
            Part = "fuel_tank", Spots = new[] { new TutorialSpot(new Int3(1, 1, 2), "fuel_tank") },
        },
        new TutorialStep("gun", TutorialPlace.Builder, "Give it a gun",
            "A gun is a breech with barrel segments in a line in front of it. From WEAPONS: a 37 mm Breech on top of the turret ring, then four Barrel Segments in a line toward the front. From AMMO: an AP Ammo Rack in front of the turret ring, under the barrel. The ring turns everything on top of it, so the gun can swing round.",
            "R T G turn a part before placing it", f => f.GunsWorking > 0)
        {
            Part = "breech_37",
            Spots = new[]
            {
                new TutorialSpot(new Int3(0, 2, 0), "breech_37"), new TutorialSpot(new Int3(0, 2, -1), "barrel"), new TutorialSpot(new Int3(0, 2, -2), "barrel"),
                new TutorialSpot(new Int3(0, 2, -3), "barrel"), new TutorialSpot(new Int3(0, 2, -4), "barrel"), new TutorialSpot(new Int3(0, 1, -1), "ammo_ap"),
            },
        },
        new TutorialStep("drive", TutorialPlace.Builder, "Take it out",
            "It can drive and shoot. Press TEST DRIVE on the top rail, or Tab, to take it to the test range.",
            "TAB test drive", f => f.OnRange),
        new TutorialStep("move", TutorialPlace.Range, "Drive",
            "W and S are the throttle, A and D steer, Space brakes. The ring round your aim point shows your speed on the left and the engine on the right. Drive about 50 m.",
            "W A S D · SPACE brake", f => f.DrivenM >= 50f),
        new TutorialStep("shoot", TutorialPlace.Range, "Hit a target",
            "Move the mouse to aim the gun and left click to fire. The pop-up targets are off to your right. The small circle shows where the gun will really hit: put it on a target before you fire.",
            "MOUSE aim · LEFT CLICK fire", f => f.TargetsDown > 0),
        new TutorialStep("ammo", TutorialPlace.Range, "Switch ammunition",
            "Q swaps the shells: AP (armor-piercing) for tanks and armor, HE (high explosive) for soft targets and buildings. What's loaded shows in the line under the ring.",
            "Q ammunition", f => f.AmmoSwitches > 0),
        new TutorialStep("options", TutorialPlace.Range, "Vehicle options",
            "O opens the vehicle's options: what its gunners may shoot at, lights and smoke, the ammunition and how the instruments look. The mouse is free while it's open; O again closes it.",
            "O options", f => f.OptionsOpened > 0),
        new TutorialStep("foot", TutorialPlace.Range, "Get out",
            "E gets you out (and back in when you walk up to a vehicle). On foot, W A S D walks and the mouse looks round.",
            "E get in / out", f => f.OnFoot),
        new TutorialStep("back", TutorialPlace.Range, "Back to the builder",
            "Tab takes you back to the builder, where your car is waiting to be improved: more armor, a bigger gun, a turret roof.",
            "TAB builder", f => f.InBuilder),
    };

    public int Index { get; private set; }
    public bool Finished => Index >= Steps.Count;
    public TutorialStep Current => Finished ? null : Steps[Index];

    /// <summary>
    /// Checks the current step against what the player has done; if it's done, moves on one step (one at a time,
    /// so each gets its moment) and returns the step just finished. Otherwise null.
    /// </summary>
    public TutorialStep Update(in TutorialFacts f)
    {
        TutorialStep step = Current;
        if (step == null || !step.Done(f)) return null;
        Index++;
        return step;
    }

    /// <summary>Moves past the current step without doing it.</summary>
    public void Skip()
    {
        if (!Finished) Index++;
    }

    /// <summary>The design the boot camp starts from.</summary>
    public static Blueprint Car() => ExampleDesigns.BootCampChassis();
}

/// <summary>A first-time tip: a few lines on a screen, shown the first time you get there.</summary>
public sealed record Tip(string Id, string Title, string Text);

/// <summary>The first-time tips, one for each screen or way of getting about.</summary>
public static class Tips
{
    public static readonly IReadOnlyList<Tip> All = new[]
    {
        new Tip("builder", "The builder",
            "Parts are on the left: pick a category, then a part, then click on the design to place it; right click takes it off. The right panel shows what the design can do and what's wrong with it. Tools run along the bottom, and Open (top) has the example designs. F1 lists every key."),
        new Tip("range", "On foot",
            "W A S D walks, the mouse looks round. E gets you into a vehicle you walk up to (and out again). The command post by the depot has the map (M). Esc frees the mouse; Tab goes back to the builder."),
        new Tip("vehicle", "Driving",
            "W / S throttle, A / D steer, the mouse aims and left click fires. The ring round your aim shows speed (left), engine (right) and heading (top); the amber mark on top is where your hull points. Q switches shells, O opens the vehicle's options, E gets out."),
        new Tip("ship", "At the helm",
            "W and S move the engine telegraph a notch at a time, from full astern to full ahead; A and D work the rudder. The ring's right side shows the telegraph. F fires torpedoes. Submarines: C dives, Space comes up."),
        new Tip("aircraft", "Flying",
            "W / S throttle. The mouse is the stick and A / D the rudder; hold C to look round. U swaps to the instructor, who flies where you look. Full throttle and lift the nose to take off. X fires missiles, B drops bombs. The ring shows speed (left) and height (right)."),
        new Tip("map", "The map",
            "Click a unit's stalk or drag a box to pick units. Right click sends them: on an enemy to attack, on a friend to escort, on the ground to move. H holds, F changes formation, J jumps into the first one picked. TABLE lays the map flat."),
        new Tip("options", "Vehicle options",
            "The crew's orders, the systems you can switch, the ammunition and the instrument style. The mouse is free while it's open; O or Esc closes it."),
        new Tip("world", "The world",
            "Ten islands, 20 km across, each with its own mountains, valleys and coast. M opens the map, with a list of places to go to."),
    };

    public static Tip Get(string id) => All.FirstOrDefault(t => t.Id == id);
}

/// <summary>What the player has seen of the tutorials, kept between games.</summary>
public sealed class TutorialProgress
{
    /// <summary>The welcome (with its offer of the boot camp) has been shown.</summary>
    public bool Welcomed { get; set; }

    /// <summary>The boot camp has been finished at least once.</summary>
    public bool BootCampDone { get; set; }

    /// <summary>The player turned first-time tips off.</summary>
    public bool TipsOff { get; set; }

    public List<string> Seen { get; set; } = new();

    /// <summary>Whether a tip should show now: tips on, and not seen before.</summary>
    public bool ShouldShow(string tipId) => !TipsOff && Tips.Get(tipId) != null && !Seen.Contains(tipId);

    public void MarkSeen(string tipId)
    {
        if (!Seen.Contains(tipId)) Seen.Add(tipId);
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads what was saved; anything unreadable starts afresh rather than stopping the game.</summary>
    public static TutorialProgress FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new TutorialProgress();
        try
        {
            TutorialProgress p = JsonSerializer.Deserialize<TutorialProgress>(json, Options) ?? new TutorialProgress();
            p.Seen ??= new List<string>();
            return p;
        }
        catch (JsonException)
        {
            return new TutorialProgress();
        }
    }
}
