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
    public bool IsBuilding;
    public bool HasGunner;
    public int Propellers;
    public int ShipPropellers;
    public int Rudders;
    public int TorpedoTubesReady;

    /// <summary>Blocks placed with the box tool so far.</summary>
    public int BoxPlaced;

    /// <summary>Which part fills a cell of the design (null if it's empty).</summary>
    public Func<Int3, string> PartAt;

    // On the range (counted since the range opened).
    public bool OnRange;
    public float DrivenM;
    public int TargetsDown;
    public int AmmoSwitches;
    public int OptionsOpened;
    public bool OnFoot;

    /// <summary>How far the vehicle has turned while moving, in degrees, all added up.</summary>
    public float TurnDeg;

    /// <summary>The most height above the ground reached in the air.</summary>
    public float MaxHeightM;

    /// <summary>Back on the ground and nearly stopped after flying.</summary>
    public bool Landed;

    public int FlightModeSwitches;
    public int TorpedoesFired;
    public int BombsDropped;

    /// <summary>How many times you've taken a seat (the first is the one the range puts you in).</summary>
    public int Mounts;

    /// <summary>The design facts, from the builder's analysis of what's built.</summary>
    public static TutorialFacts FromDesign(VehicleAnalysis a, bool cameraMoved, Func<Int3, string> partAt = null, int boxPlaced = 0) => new()
    {
        InBuilder = true,
        CameraMoved = cameraMoved,
        HasDriver = a != null && a.HasDriver,
        HasEngine = a != null && a.PowerKw > 0,
        HasFuel = a != null && a.FuelL > 0,
        CanDrive = a != null && a.CanDrive,
        GunsWorking = a?.Guns.Count(Works) ?? 0,
        IsBuilding = a != null && a.IsBuilding,
        HasGunner = a != null && a.Seats.Any(s => s.Role == SeatRole.Gunner),
        Propellers = a?.Thrusters.Count(t => t.Kind == BlockKind.AirPropeller) ?? 0,
        ShipPropellers = a?.ShipPropellers.Count ?? 0,
        Rudders = a?.Rudders.Count ?? 0,
        TorpedoTubesReady = a?.TorpedoTubes.Count(t => t.Rounds > 0) ?? 0,
        PartAt = partAt,
        BoxPlaced = boxPlaced,
    };

    /// <summary>A gun that can fire: it has a barrel that isn't blocked and ammunition it can reach.</summary>
    public static bool Works(GunInfo g) => g.Valid && !g.MuzzleBlocked && (g.NeedsPower || g.ApRounds + g.HeRounds + g.FlakRounds > 0);
}

/// <summary>A part the boot camp suggests, where it fits on the course's design, and which way it faces there.</summary>
public readonly record struct TutorialSpot(Int3 Cell, string Part, int O = 0);

/// <summary>One step of the boot camp: what to do, which keys do it, and how to tell it's done.</summary>
public sealed record TutorialStep(string Id, TutorialPlace Place, string Title, string Text, string Keys, Func<TutorialFacts, bool> Done)
{
    /// <summary>The part to pick for this step, if it's about placing one (the card offers to pick it for you).</summary>
    public string Part { get; init; }

    /// <summary>Where the parts go on the course's design, highlighted in the builder.</summary>
    public IReadOnlyList<TutorialSpot> Spots { get; init; } = Array.Empty<TutorialSpot>();
}

/// <summary>A boot camp course: one front's design to finish, and what to try with it on the range.</summary>
public sealed class TutorialCourse
{
    public string Id { get; init; }
    public string Name { get; init; }

    /// <summary>A line for the course picker.</summary>
    public string Blurb { get; init; }

    /// <summary>The design it starts from (a fresh copy each time).</summary>
    public Func<Blueprint> Starter { get; init; }

    public IReadOnlyList<TutorialStep> Steps { get; init; }

    /// <summary>The last card: what you did, and what to try next.</summary>
    public string Finale { get; init; }
}

/// <summary>
/// The boot camp courses, one per front. Land starts from a bare scout car chassis; Sea, Air and Structures start from
/// an example design (the Corsair, the Kestrel) or a bare foundation slab, with the parts that make it work taken out
/// for the player to fit back, at the spots they came from (and turned the way they were).
/// </summary>
public static class Courses
{
    /// <summary>An example with some of its parts taken out, and where each one was.</summary>
    public static (Blueprint design, List<TutorialSpot> spots) Strip(Blueprint example, string name, Func<BlueprintBlock, bool> take)
    {
        var keep = new List<BlueprintBlock>();
        var spots = new List<TutorialSpot>();
        foreach (BlueprintBlock b in example.Blocks)
        {
            if (take(b)) spots.Add(new TutorialSpot(b.Cell, b.Id, b.O));
            else keep.Add(b);
        }

        Blueprint bp = Blueprint.FromCellMap(name, keep);
        bp.Paint = example.Paint;
        return (bp, spots);
    }

    private static TutorialStep Drive(string text) => new("drive", TutorialPlace.Builder, "Take it out", text, "TAB test drive", f => f.OnRange);

    private static TutorialStep Back(string text) => new("back", TutorialPlace.Range, "Back to the builder", text, "TAB builder", f => f.InBuilder);

    public static readonly TutorialCourse Land = new()
    {
        Id = "land", Name = "Land",
        Blurb = "Start here: fit the parts that make a scout car drive and shoot, then drive it and hit a target.",
        Starter = ExampleDesigns.BootCampChassis,
        Steps = new[]
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
            Drive("It can drive and shoot. Press TEST DRIVE on the top rail, or Tab, to take it to the test range."),
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
            Back("Tab takes you back to the builder, where your car is waiting to be improved: more armor, a bigger gun, a turret roof."),
        },
        Finale = "You built a car, drove it and hit a target with it. Next: the Sea, Air and Structures courses (Help, top rail); the example designs (Open); the map (walk into the command post by the depot and press M). F1 lists every key.",
    };

    private static readonly (Blueprint design, List<TutorialSpot> spots) SeaStrip =
        Strip(ExampleDesigns.Corsair(), "Corsair (boot camp)", b => b.Id is "ship_propeller" or "rudder" or "torpedo_tube");

    public static readonly TutorialCourse Sea = new()
    {
        Id = "sea", Name = "Sea",
        Blurb = "Fit the propellers, rudders and torpedo tubes to the Corsair patrol boat, then take her out and fire a torpedo.",
        Starter = () => Strip(ExampleDesigns.Corsair(), "Corsair (boot camp)", b => b.Id is "ship_propeller" or "rudder" or "torpedo_tube").design,
        Steps = new[]
        {
            new TutorialStep("propellers", TutorialPlace.Builder, "Fit the propellers",
                "This is the Corsair with her propellers, rudders and torpedo tubes taken off. A ship floats on its hull (the blue sheet is where the sea will come up to) and is pushed by propellers below it. From SEA, put a Ship Propeller in each glowing box under the stern. \"Pick it for me\" also turns it the right way.",
                "LEFT CLICK place · R T G turn", f => f.ShipPropellers >= 2)
            {
                Part = "ship_propeller", Spots = SeaStrip.spots.Where(s => s.Part == "ship_propeller").ToArray(),
            },
            new TutorialStep("rudders", TutorialPlace.Builder, "Fit the rudders",
                "A rudder steers. It goes behind a propeller, under water, where the propeller's wash flows past it. From SEA, a Rudder behind each propeller.",
                "LEFT CLICK place", f => f.Rudders >= 2)
            {
                Part = "rudder", Spots = SeaStrip.spots.Where(s => s.Part == "rudder").ToArray(),
            },
            new TutorialStep("torpedoes", TutorialPlace.Builder, "Torpedo tubes",
                "A torpedo tube fires the way its arrow points, fed from the Torpedo Rack behind it. Torpedoes run just under the surface and hole a ship below the waterline. From SEA, a Torpedo Tube in front of each rack on deck, pointing ahead.",
                "LEFT CLICK place · R T G turn", f => f.TorpedoTubesReady > 0)
            {
                Part = "torpedo_tube", Spots = SeaStrip.spots.Where(s => s.Part == "torpedo_tube").ToArray(),
            },
            Drive("She can sail. Tab takes her out to sea."),
            new TutorialStep("underway", TutorialPlace.Range, "Get under way",
                "Ships have an engine telegraph rather than a throttle: each tap of W rings it up a notch, from stop through slow and half to full ahead; S rings it back down (and on into astern). The ring's right side shows it. Make 150 m.",
                "W / S telegraph", f => f.DrivenM >= 150f),
            new TutorialStep("helm", TutorialPlace.Range, "Turn her round",
                "A and D put the rudder over. A ship answers slowly and swings wide, and turns better the faster she's going. Turn through about 120 degrees.",
                "A / D rudder", f => f.TurnDeg >= 120f),
            new TutorialStep("torpedo", TutorialPlace.Range, "Fire a torpedo",
                "F fires the torpedo tubes the way they point, so aim with the whole ship. The target boat is out to sea; a torpedo takes a while to get there, so aim ahead of anything moving.",
                "F torpedo", f => f.TorpedoesFired > 0),
            Back("Tab takes you back to the builder. Try a bigger ship from the examples (Open), or the Barracuda: a submarine dives with C and comes up with Space."),
        },
        Finale = "You fitted a ship's propellers, rudders and torpedo tubes, took her out, turned her and fired a torpedo. Next: the Air and Structures courses (Help), or the Trident and the Barracuda (Open).",
    };

    private static readonly Int3 KestrelCanopy = new(0, 3, -2);

    private static bool KestrelTakes(BlueprintBlock b) =>
        b.Id is "air_propeller" or "aero_engine" or "fuel_tank" or "pilot_seat" || (b.Id == "glazing" && b.Cell == KestrelCanopy);

    private static readonly (Blueprint design, List<TutorialSpot> spots) AirStrip = Strip(ExampleDesigns.Kestrel(), "Kestrel (boot camp)", KestrelTakes);

    public static readonly TutorialCourse Air = new()
    {
        Id = "air", Name = "Air",
        Blurb = "Fit the engine, propeller, fuel and pilot to the Kestrel fighter-bomber, then take off, turn, bomb and land.",
        Starter = () => Strip(ExampleDesigns.Kestrel(), "Kestrel (boot camp)", KestrelTakes).design,
        Steps = new[]
        {
            new TutorialStep("engine", TutorialPlace.Builder, "Engine and propeller",
                "This is the Kestrel without its engine, propeller, fuel, pilot or canopy roof. Its wings lift it once it's going fast enough. From AIR, an Aero Engine in the nose and an Air Propeller in front of it. \"Pick it for me\" picks whichever is still missing.",
                "LEFT CLICK place", f => f.HasEngine && f.Propellers > 0)
            {
                Part = "aero_engine", Spots = AirStrip.spots.Where(s => s.Part is "aero_engine" or "air_propeller").ToArray(),
            },
            new TutorialStep("fuel", TutorialPlace.Builder, "Fuel in the wings' roots",
                "From MOBILITY, a Fuel Tank each side of the fuselage, behind the wings.",
                "LEFT CLICK place", f => f.HasFuel)
            {
                Part = "fuel_tank", Spots = AirStrip.spots.Where(s => s.Part == "fuel_tank").ToArray(),
            },
            new TutorialStep("pilot", TutorialPlace.Builder, "The pilot",
                "From CONTROL, the Pilot Seat goes in the cockpit, under where the canopy roof will be. It works like a driver seat.",
                "LEFT CLICK place", f => f.HasDriver)
            {
                Part = "pilot_seat", Spots = AirStrip.spots.Where(s => s.Part == "pilot_seat").ToArray(),
            },
            new TutorialStep("canopy", TutorialPlace.Builder, "Close the canopy",
                "Glazing is framed glass: it keeps the wind and splinters off and you can see out. From STRUCTURE, Glazing on top of the cockpit closes the canopy. (V gives glazing other shapes: slopes, corners, plates.)",
                "LEFT CLICK place · V shape", f => f.PartAt != null && (f.PartAt(KestrelCanopy)?.StartsWith("glazing") ?? false))
            {
                Part = "glazing", Spots = AirStrip.spots.Where(s => s.Part == "glazing").ToArray(),
            },
            Drive("It can fly: the design panel shows how fast it has to go before it lifts off (\"stalls below\"). Tab takes it to the end of the airstrip."),
            new TutorialStep("takeoff", TutorialPlace.Range, "Take off",
                "Hold W for full throttle and let it roll straight down the strip. When the speed on the left of the ring passes the stall speed, ease the mouse back to lift the nose, and climb above 40 m. The right side of the ring shows your height.",
                "W throttle · MOUSE stick", f => f.MaxHeightM >= 40f),
            new TutorialStep("turn", TutorialPlace.Range, "Turn round",
                "The mouse is the stick: push it to the side to roll, pull back to turn. A and D are the rudder, for small corrections. Come round through 180 degrees and keep your height.",
                "MOUSE stick · A / D rudder", f => f.TurnDeg >= 180f),
            new TutorialStep("instructor", TutorialPlace.Range, "The instructor",
                "U swaps to the instructor: the plane flies wherever you look, and you just aim. Some find it easier for fighting. U again gives you the stick back.",
                "U flight mode", f => f.FlightModeSwitches > 0),
            new TutorialStep("bomb", TutorialPlace.Range, "Drop a bomb",
                "B drops a bomb from the racks under the wings. A diamond on the ground shows where it would land: fly so it slides over the target. The bunker down the range makes a good one.",
                "B bomb", f => f.BombsDropped > 0),
            new TutorialStep("land", TutorialPlace.Range, "Land",
                "Line up with the strip from a long way out, throttle back (S) and let it sink, keeping the nose a little up. Touch down, then Space to brake until it stops. (Skip this one if you'd rather practise later.)",
                "S throttle · SPACE brake", f => f.Landed),
            Back("Tab takes you back to the builder. Try the Falcon jet from the examples (Open): open the throttle all the way for its afterburners."),
        },
        Finale = "You fitted an aircraft's engine, propeller, fuel, pilot and canopy, took off, turned, bombed and landed. Next: the Structures course (Help), or the jets and the Hornet helicopter (Open).",
    };

    public static readonly TutorialCourse Structures = new()
    {
        Id = "structures", Name = "Structures",
        Blurb = "Raise walls with the box tool and put a gun on a foundation slab, then man it on the range.",
        Starter = ExampleDesigns.BootCampSlab,
        Steps = new[]
        {
            new TutorialStep("walls", TutorialPlace.Builder, "Walls with the box tool",
                "A building stands on Foundation blocks (the slab is down already): it never moves, so it can be as heavy as you like. From STRUCTURE, pick Concrete, press B until the bottom rail says BOX: FILL, then drag along an edge of the slab: a whole wall at once. The wheel makes it taller while you drag.",
                "B box tool · DRAG · WHEEL taller", f => f.BoxPlaced >= 5)
            {
                Part = "concrete",
            },
            new TutorialStep("gun", TutorialPlace.Builder, "A gun on a turntable",
                "The same as on a vehicle: from WEAPONS a Turret Ring in the middle of the slab, a 37 mm Breech on top of it and four Barrel Segments in front; from AMMO an AP Ammo Rack in front of the ring. Press B again until the box tool is off first.",
                "B box tool off · LEFT CLICK place", f => f.GunsWorking > 0)
            {
                Part = "turret_ring",
                Spots = new[]
                {
                    new TutorialSpot(new Int3(0, 1, 0), "turret_ring"), new TutorialSpot(new Int3(0, 2, 0), "breech_37"), new TutorialSpot(new Int3(0, 2, -1), "barrel"),
                    new TutorialSpot(new Int3(0, 2, -2), "barrel"), new TutorialSpot(new Int3(0, 2, -3), "barrel"), new TutorialSpot(new Int3(0, 2, -4), "barrel"),
                    new TutorialSpot(new Int3(0, 1, -1), "ammo_ap"),
                },
            },
            new TutorialStep("gunner", TutorialPlace.Builder, "A gunner",
                "A building has no driver: its guns need a Gunner Seat. From CONTROL, put one behind the breech, on the turntable.",
                "LEFT CLICK place", f => f.HasGunner)
            {
                Part = "gunner_seat", Spots = new[] { new TutorialSpot(new Int3(0, 2, 1), "gunner_seat") },
            },
            Drive("It's ready. Tab puts it on the range, off to one side of the firing line, with you at the gun."),
            new TutorialStep("shoot", TutorialPlace.Range, "Hit a target",
                "The mouse turns the turntable and lifts the gun; left click fires. The pop-up targets are across the firing lane to the east. A building's gun is steadier than any vehicle's.",
                "MOUSE aim · LEFT CLICK fire", f => f.TargetsDown > 0),
            new TutorialStep("foot", TutorialPlace.Range, "Get out and look round",
                "E gets you out. Walk round your emplacement: walls you can't see over yourself won't stop shells forever, but they make the gun much harder to hit.",
                "E get out · W A S D walk", f => f.OnFoot),
            new TutorialStep("remount", TutorialPlace.Range, "Man the gun again",
                "Walk up to the gunner seat and press E to take it again. On any building or ship you take the seat you're standing at.",
                "E take the seat", f => f.Mounts >= 2),
            Back("Tab takes you back to the builder. Look at the Pillbox, the Flak Tower and the Coastal citadel (Open) for bigger buildings, with doors, bunks and supply crates inside."),
        },
        Finale = "You built a gun emplacement on a foundation, raised its walls with the box tool, manned it and fired from it. Next: the example buildings (Open), or command the battle from the map at the command post (M).",
    };

    public static readonly IReadOnlyList<TutorialCourse> All = new[] { Land, Sea, Air, Structures };

    public static TutorialCourse Get(string id) => All.FirstOrDefault(c => c.Id == id);
}

/// <summary>
/// The boot camp: a course of steps that takes a new player from an unfinished design to using it on the range.
/// Each step is checked against what the player has actually done, so nothing has to be clicked through.
/// </summary>
public sealed class BootCamp
{
    public BootCamp(TutorialCourse course = null) => Course = course ?? Courses.Land;

    public TutorialCourse Course { get; }
    public IReadOnlyList<TutorialStep> Steps => Course.Steps;
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

    /// <summary>The land course's design.</summary>
    public static Blueprint Car() => Courses.Land.Starter();
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

    /// <summary>The courses finished so far (by id).</summary>
    public List<string> CoursesDone { get; set; } = new();

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
            p.CoursesDone ??= new List<string>();
            return p;
        }
        catch (JsonException)
        {
            return new TutorialProgress();
        }
    }
}
