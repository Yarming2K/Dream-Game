using System;
using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>Ready-made designs to drive, take apart and learn from, plus test-range targets.</summary>
public static class ExampleDesigns
{
    private sealed class Draft
    {
        private readonly Dictionary<Int3, BlueprintBlock> _cells = new();

        /// <summary>Places a block, replacing whatever is there.</summary>
        public void Set(int x, int y, int z, string id, int o = 0) =>
            _cells[new Int3(x, y, z)] = new BlueprintBlock(id, new Int3(x, y, z), o);

        /// <summary>Fills a box, skipping cells that already have something.</summary>
        public void Fill(int x0, int x1, int y0, int y1, int z0, int z1, string id, int o = 0)
        {
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                var c = new Int3(x, y, z);
                if (!_cells.ContainsKey(c)) _cells[c] = new BlueprintBlock(id, c, o);
            }
        }

        /// <summary>A slope whose sloped face points along <paramref name="facing"/> (e.g. (1,-1,0) for a bilge chamfer).</summary>
        public void Slope(int x, int y, int z, string id, Int3 facing) => Set(x, y, z, id, Facing(facing));

        public void FillSlope(int x0, int x1, int y0, int y1, int z0, int z1, string id, Int3 facing) =>
            Fill(x0, x1, y0, y1, z0, z1, id, Facing(facing));

        public void Remove(int x, int y, int z) => _cells.Remove(new Int3(x, y, z));

        public Blueprint Build(string name) => Blueprint.FromCellMap(name, _cells.Values);
    }

    /// <summary>The orientation that turns a wedge's sloped face (local (0,1,-1)) to point along <paramref name="facing"/>.</summary>
    public static int Facing(Int3 facing)
    {
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.Apply(o, new Int3(0, 1, -1)) == facing) return o;
        throw new System.ArgumentException($"No orientation faces {facing}.");
    }

    /// <summary>Turns a block so its arrow (forward) points along <paramref name="dir"/>, keeping it upright where possible.</summary>
    public static int Pointing(Int3 dir)
    {
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.Forward(o) == dir && (dir.Y != 0 || Orientations.Up(o) == Int3.Up)) return o;
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.Forward(o) == dir) return o;
        throw new System.ArgumentException($"No orientation points {dir}.");
    }

    public static IReadOnlyList<Blueprint> All => new[]
    {
        Warden(), Jackal(), Bulwark(), Skyguard(), Corsair(), Barracuda(), Kestrel(), Hornet(), Wasp(), Zephyr(),
    };

    /// <summary>Medium tank: sloped front, 76 mm gun in a turret with a gunner and commander, ammo stored low in the hull.</summary>
    public static Blueprint Warden()
    {
        var d = new Draft();
        for (int z = -5; z <= 4; z++)
        {
            d.Set(-3, 0, z, "track");
            d.Set(3, 0, z, "track");
        }

        // Crew, ammo and engine bay (middle layer).
        d.Set(-1, 1, -5, "driver_seat");
        d.Set(0, 1, -1, "ammo_feed");
        d.Set(-1, 1, -1, "ammo_ap");
        d.Set(1, 1, -1, "ammo_ap");
        d.Set(-1, 1, 0, "ammo_ap");
        d.Set(1, 1, 0, "ammo_ap");
        d.Set(-1, 1, -2, "ammo_he");
        d.Set(1, 1, -2, "ammo_he");
        d.Set(-1, 1, 3, "engine_large");
        d.Set(1, 1, 3, "engine_small");
        d.Set(0, 1, 3, "fuel_tank");
        d.Set(-2, 1, 3, "fuel_tank");
        d.Set(2, 1, 3, "fuel_tank");
        d.Set(0, 1, 4, "fuel_tank");

        // Front: a two-step 45° glacis of heavy plate.
        d.Fill(-2, 2, 0, 0, -6, -6, "armor_heavy");
        d.Fill(-3, 3, 1, 1, -6, -6, "armor_heavy_slope");
        d.Fill(-3, 3, 2, 2, -5, -5, "armor_heavy_slope");

        // Hull shell.
        d.Fill(-2, 2, 0, 0, 5, 5, "armor_light");
        d.Fill(-3, 3, 1, 1, 5, 5, "armor_light");
        d.Fill(-3, -3, 1, 1, -5, 4, "armor_light");
        d.Fill(3, 3, 1, 1, -5, 4, "armor_light");
        d.Set(0, 2, -1, "turret_ring");
        d.Fill(-3, 3, 2, 2, -4, 5, "armor_light");
        d.Fill(-2, 2, 0, 1, -5, 4, "frame");

        // Turret: 76 mm, six barrel segments and a muzzle brake.
        d.Set(0, 3, -1, "breech_76");
        for (int z = -2; z >= -7; z--) d.Set(0, 3, z, "barrel");
        d.Set(0, 3, -8, "muzzle_brake");
        d.Set(-1, 3, -1, "loader");
        d.Set(1, 3, -1, "loader");
        d.Set(0, 3, 0, "propellant");
        d.Set(1, 3, 0, "gunner_seat");
        d.Set(-1, 3, 0, "commander_seat");
        d.Fill(-2, 2, 3, 3, -3, -2, "armor_heavy");
        d.Fill(-2, -2, 3, 3, -1, 1, "armor_heavy");
        d.Fill(2, 2, 3, 3, -1, 1, "armor_heavy");
        d.Fill(-1, 1, 3, 3, 1, 1, "armor_light");
        d.Fill(-1, 1, 3, 3, 0, 0, "frame");
        d.Fill(-2, 2, 4, 4, -3, -3, "armor_heavy_slope");
        d.Fill(-2, 2, 4, 4, -2, 1, "armor_light");
        return d.Build("Warden (medium tank)");
    }

    /// <summary>Fast wheeled scout with a small 37 mm turret. No gunner seat: the driver aims.</summary>
    public static Blueprint Jackal()
    {
        var d = new Draft();
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x, 0, -3, "wheel_steer");
            d.Set(x, 0, 0, "wheel");
            d.Set(x, 0, 2, "wheel");
        }

        d.Set(-1, 1, -3, "driver_seat");
        d.Set(1, 1, -3, "fuel_tank");
        d.Set(0, 1, -1, "ammo_feed");
        d.Set(-1, 1, -1, "ammo_ap");
        d.Set(1, 1, -1, "ammo_ap");
        d.Set(-1, 1, 0, "ammo_he");
        d.Set(-1, 1, 2, "engine_small");
        d.Set(1, 1, 2, "engine_small");
        d.Set(0, 1, 2, "fuel_tank");

        d.Fill(-2, 2, 0, 1, -4, -4, "armor_light_slope");
        d.Fill(-2, 2, 2, 2, -3, -3, "armor_light_slope");
        d.Set(0, 2, -1, "turret_ring");
        d.Fill(-2, 2, 2, 2, -2, 3, "armor_light");
        d.Fill(-2, -2, 1, 1, -3, 3, "armor_light");
        d.Fill(2, 2, 1, 1, -3, 3, "armor_light");
        d.Fill(-1, 1, 1, 1, 3, 3, "armor_light");
        d.Fill(-2, 2, 0, 0, 3, 3, "armor_light");
        d.Fill(-1, 1, 0, 1, -3, 2, "frame");

        d.Set(0, 3, -1, "breech_37");
        for (int z = -2; z >= -5; z--) d.Set(0, 3, z, "barrel");
        d.Set(-1, 3, -1, "loader");
        d.Set(1, 3, -1, "propellant");
        d.Fill(-1, 1, 3, 3, -2, -2, "armor_light");
        d.Fill(-1, 1, 3, 3, 0, 0, "armor_light");
        d.Fill(-1, 1, 4, 4, -2, 0, "armor_light");
        return d.Build("Jackal (scout car)");
    }

    /// <summary>Tank destroyer: a long 120 mm gun fixed in a heavily armored casemate. Aim by turning the hull.</summary>
    public static Blueprint Bulwark()
    {
        var d = new Draft();
        for (int z = -5; z <= 5; z++)
        {
            d.Set(-3, 0, z, "track");
            d.Set(3, 0, z, "track");
        }

        d.Set(-2, 1, -5, "driver_seat");
        d.Set(0, 1, -2, "ammo_feed");
        d.Set(-1, 1, -2, "ammo_ap");
        d.Set(1, 1, -2, "ammo_ap");
        d.Set(-1, 1, -1, "ammo_ap");
        d.Set(1, 1, -1, "ammo_ap");
        d.Set(-1, 1, -3, "ammo_he");
        d.Set(1, 1, -3, "ammo_he");
        d.Set(-1, 1, 4, "engine_large");
        d.Set(1, 1, 4, "engine_large");
        d.Set(0, 1, 4, "fuel_tank");
        d.Set(-2, 1, 4, "fuel_tank");
        d.Set(2, 1, 4, "fuel_tank");
        d.Set(0, 1, 5, "fuel_tank");

        d.Set(0, 2, -2, "breech_120");
        for (int z = -3; z >= -12; z--) d.Set(0, 2, z, "barrel");
        d.Set(0, 2, -13, "muzzle_brake");
        d.Set(-1, 2, -2, "loader");
        d.Set(1, 2, -2, "loader");
        d.Set(0, 3, -2, "loader");
        d.Set(0, 2, -1, "propellant");
        d.Set(-2, 2, -3, "gunner_seat");
        d.Set(2, 2, -3, "commander_seat");

        d.Fill(-2, 2, 0, 0, -6, -6, "armor_heavy");
        d.Fill(-3, 3, 1, 1, -6, -6, "armor_heavy_slope");
        d.Fill(-3, 3, 2, 2, -5, -5, "armor_heavy_slope");
        d.Fill(-2, 2, 3, 3, -4, -4, "armor_heavy_slope");
        d.Fill(-3, -3, 1, 2, -5, -4, "armor_heavy");
        d.Fill(3, 3, 1, 2, -5, -4, "armor_heavy");
        d.Fill(-3, -3, 1, 2, -3, 6, "armor_light");
        d.Fill(3, 3, 1, 2, -3, 6, "armor_light");
        d.Fill(-2, 2, 0, 1, 6, 6, "armor_light");
        d.Fill(-2, 2, 2, 2, 2, 6, "armor_light");
        d.Fill(-2, 2, 3, 3, -3, 1, "armor_light");
        d.Fill(-2, 2, 0, 2, -5, 5, "frame");
        return d.Build("Bulwark (tank destroyer)");
    }

    /// <summary>
    /// Patrol boat: a sealed hull split into four watertight compartments, a 76 mm turret forward, a 37 mm
    /// turret aft, deck torpedo tubes, and twin propellers and rudders at the stern.
    /// </summary>
    public static Blueprint Corsair()
    {
        var d = new Draft();
        const int bow = -20, stern = 19, deck = 5;

        // Machinery and crew inside, placed first so the hull fills around them.
        d.Set(-2, 1, 16, "engine_large");
        d.Set(2, 1, 16, "engine_large");
        d.Set(-1, 1, 16, "fuel_tank");
        d.Set(1, 1, 16, "fuel_tank");
        d.Set(0, 1, 16, "fuel_tank");
        foreach (int z in new[] { -15, -5, 5, 13 }) d.Set(0, 1, z, "bilge_pump");
        foreach (int z in new[] { -8, -3, 2, 8 })
        {
            d.Set(-1, 1, z, "lead_ballast");
            d.Set(1, 1, z, "lead_ballast");
        }

        // Hull: flat bottom, chamfered bilges, straight sides, transom stern and a raked bow.
        d.Fill(-3, 3, 0, 0, bow + 3, stern, "armor_light");
        d.FillSlope(-4, -4, 0, 0, bow + 3, stern, "armor_light_slope", new Int3(-1, -1, 0));
        d.FillSlope(4, 4, 0, 0, bow + 3, stern, "armor_light_slope", new Int3(1, -1, 0));
        d.Fill(-4, -4, 1, deck, bow + 1, stern, "armor_light");
        d.Fill(4, 4, 1, deck, bow + 1, stern, "armor_light");
        d.Fill(-3, 3, 1, deck, stern, stern, "armor_light");
        for (int y = 0; y <= 2; y++) d.FillSlope(-3, 3, y, y, bow + 2 - y, bow + 2 - y, "armor_light_slope", new Int3(0, -1, -1));
        d.FillSlope(-4, -4, 1, 1, bow + 2, bow + 2, "armor_light_slope", new Int3(0, -1, -1));
        d.FillSlope(4, 4, 1, 1, bow + 2, bow + 2, "armor_light_slope", new Int3(0, -1, -1));
        d.Fill(-3, 3, 3, deck, bow, bow, "armor_light");
        d.Fill(-4, 4, 2, deck, bow + 1, bow + 1, "armor_light");
        d.Fill(-3, 3, 1, deck - 1, bow + 1, bow + 1, "armor_light");

        // Watertight bulkheads: four compartments, so one hole doesn't sink her.
        foreach (int z in new[] { -10, 0, 10 }) d.Fill(-3, 3, 1, deck - 1, z, z, "armor_light");

        // Forward 76 mm turret, fed from racks below deck.
        d.Set(0, deck, -12, "turret_ring");
        d.Set(0, deck - 1, -12, "ammo_feed");
        d.Set(-1, deck - 1, -12, "ammo_ap");
        d.Set(1, deck - 1, -12, "ammo_ap");
        d.Set(-1, deck - 1, -13, "ammo_he");
        d.Set(1, deck - 1, -13, "ammo_he");
        d.Set(0, deck + 1, -12, "breech_76");
        for (int z = -13; z >= -18; z--) d.Set(0, deck + 1, z, "barrel");
        d.Set(0, deck + 1, -19, "muzzle_brake");
        d.Set(-1, deck + 1, -12, "loader");
        d.Set(1, deck + 1, -12, "gunner_seat");
        d.Set(0, deck + 1, -11, "propellant");
        d.Fill(-1, 1, deck + 1, deck + 1, -13, -13, "armor_light");
        d.Fill(-1, 1, deck + 1, deck + 1, -11, -11, "armor_light");
        d.Fill(-1, 1, deck + 2, deck + 2, -13, -11, "armor_light");

        // Aft 37 mm turret, facing astern.
        d.Set(0, deck, 11, "turret_ring");
        d.Set(0, deck - 1, 11, "ammo_feed");
        d.Set(-1, deck - 1, 11, "ammo_ap");
        d.Set(1, deck - 1, 11, "ammo_he");
        d.Set(0, deck + 1, 11, "breech_37", Pointing(Int3.Back));
        for (int z = 12; z <= 15; z++) d.Set(0, deck + 1, z, "barrel");
        d.Set(-1, deck + 1, 11, "loader");
        d.Set(1, deck + 1, 11, "gunner_seat");
        d.Fill(-1, 1, deck + 2, deck + 2, 10, 12, "armor_light");
        d.Fill(-1, 1, deck + 1, deck + 1, 10, 10, "armor_light");

        // Bridge amidships.
        d.Set(0, deck + 1, -1, "driver_seat");
        d.Set(0, deck + 2, -1, "commander_seat");
        d.Fill(-2, 2, deck + 1, deck + 2, -2, 3, "armor_light");
        d.Fill(-2, 2, deck + 3, deck + 3, -2, 3, "armor_light");
        d.FillSlope(-2, 2, deck + 3, deck + 3, -3, -3, "armor_light_slope", new Int3(0, 1, -1));

        // Deck torpedo tubes, each with a rack behind it.
        foreach (int x in new[] { -3, 3 })
        {
            d.Set(x, deck + 1, -5, "torpedo_tube");
            d.Set(x, deck + 1, -4, "ammo_torpedo");
        }

        // Main deck (around the rings) and the inner frames.
        d.Fill(-4, 4, deck, deck, bow + 1, stern, "armor_light");
        d.Fill(-3, 3, deck, deck, bow, bow, "armor_light");

        // Propellers and rudders under the stern.
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x, 0, stern + 1, "ship_propeller");
            d.Set(x, 0, stern + 2, "rudder");
        }

        return d.Build("Corsair (patrol boat)");
    }

    /// <summary>
    /// Submarine: a long light pressure hull with ballast tanks along the keel under a watertight floor, the
    /// crew space above it, a conning tower with the commander's periscope, two bow torpedo tubes and a
    /// single screw with a rudder and dive planes.
    /// </summary>
    public static Blueprint Barracuda()
    {
        var d = new Draft();
        const int bow = -18, stern = 17, floor = 3, top = 7;

        // Torpedo room, crew and machinery, all above the floor.
        foreach (int x in new[] { -1, 1 })
        {
            d.Set(x, floor + 1, bow, "torpedo_tube");
            d.Set(x, floor + 1, bow + 1, "ammo_torpedo");
            d.Set(x, floor + 2, bow + 1, "ammo_torpedo");
        }

        d.Set(0, floor + 1, -6, "driver_seat");
        d.Set(0, floor + 1, 13, "engine_large");
        d.Set(-1, floor + 1, 13, "fuel_tank");
        d.Set(1, floor + 1, 13, "fuel_tank");
        d.Set(0, floor + 1, 0, "bilge_pump");
        d.Set(0, floor + 1, 8, "bilge_pump");

        // Ballast tanks: the space between the keel and the floor, in three sections, each with a valve.
        foreach (int z in new[] { -11, 0, 11 }) d.Set(0, 0, z, "ballast_valve");
        foreach (int z in new[] { -6, 5 }) d.Fill(-2, 2, 1, floor - 1, z, z, "armor_light");
        // Trim weight along the keel keeps her upright and sets how heavy she is.
        foreach (int z in new[] { -15, -9, -3, 3, 8, 14 }) d.Set(0, 1, z, "lead_ballast");
        d.Fill(-2, 2, floor, floor, bow + 1, stern - 1, "armor_light");

        // Pressure hull, chamfered along all four long edges.
        d.Fill(-2, 2, 0, 0, bow, stern, "armor_light");
        d.FillSlope(-3, -3, 0, 0, bow, stern, "armor_light_slope", new Int3(-1, -1, 0));
        d.FillSlope(3, 3, 0, 0, bow, stern, "armor_light_slope", new Int3(1, -1, 0));
        d.Fill(-3, -3, 1, top - 1, bow, stern, "armor_light");
        d.Fill(3, 3, 1, top - 1, bow, stern, "armor_light");
        d.FillSlope(-3, -3, top, top, bow, stern, "armor_light_slope", new Int3(-1, 1, 0));
        d.FillSlope(3, 3, top, top, bow, stern, "armor_light_slope", new Int3(1, 1, 0));
        d.Fill(-2, 2, top, top, bow, stern, "armor_light");
        d.Fill(-2, 2, 1, top - 1, bow, bow, "armor_light");
        d.Fill(-2, 2, 1, top - 1, stern, stern, "armor_light");

        // Conning tower with the periscope.
        d.Set(0, top + 1, -2, "commander_seat");
        d.Fill(-1, 1, top + 1, top + 3, -4, -4, "armor_light");
        d.Fill(-1, 1, top + 1, top + 3, 0, 0, "armor_light");
        d.Fill(-1, -1, top + 1, top + 3, -3, -1, "armor_light");
        d.Fill(1, 1, top + 1, top + 3, -3, -1, "armor_light");
        d.Fill(0, 0, top + 3, top + 3, -3, -1, "armor_light");

        // Screw, rudder and dive planes.
        d.Set(0, 2, stern + 1, "ship_propeller");
        d.Set(0, 2, stern + 2, "rudder");
        int plane = Orientations.RotateAroundZ(0, 1);
        d.Set(-1, 2, stern + 2, "rudder", plane);
        d.Set(1, 2, stern + 2, "rudder", plane);

        return d.Build("Barracuda (submarine)");
    }

    /// <summary>Anti-aircraft truck: the scout car's chassis with a twin 40 mm autocannon turret fed with flak.</summary>
    public static Blueprint Skyguard()
    {
        var d = new Draft();
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x, 0, -3, "wheel_steer");
            d.Set(x, 0, 0, "wheel");
            d.Set(x, 0, 2, "wheel");
        }

        d.Set(-1, 1, -3, "driver_seat");
        d.Set(1, 1, -3, "fuel_tank");
        d.Set(-1, 1, 2, "engine_small");
        d.Set(1, 1, 2, "engine_small");
        d.Set(0, 1, 2, "fuel_tank");
        d.Fill(-2, 2, 0, 1, -4, -4, "armor_light_slope");
        d.Fill(-2, 2, 2, 2, -3, -3, "armor_light_slope");
        d.Set(0, 2, -1, "turret_ring");
        d.Fill(-2, 2, 2, 2, -2, 3, "armor_light");
        d.Fill(-2, -2, 1, 1, -3, 3, "armor_light");
        d.Fill(2, 2, 1, 1, -3, 3, "armor_light");
        d.Fill(-1, 1, 1, 1, 3, 3, "armor_light");
        d.Fill(-2, 2, 0, 0, 3, 3, "armor_light");
        d.Fill(-1, 1, 0, 1, -3, 2, "frame");

        // Turret: two autocannons either side of the gunner, flak racks outboard and HE behind.
        d.Set(0, 3, -1, "frame");
        d.Set(0, 3, 0, "gunner_seat");
        foreach (int x in new[] { -1, 1 })
        {
            d.Set(x, 3, -1, "breech_40ac");
            d.Set(x, 3, -2, "barrel");
            d.Set(x, 3, -3, "barrel");
            d.Set(2 * x, 3, -1, "ammo_flak");
            d.Set(x, 3, 0, "ammo_he");
        }

        return d.Build("Skyguard (flak truck)");
    }

    /// <summary>
    /// Fighter-bomber: one aero engine pulling a nose propeller, straight wings with ailerons at the tips,
    /// a tailplane with elevators and a fin with a rudder, an autocannon in each wing, two bombs and a tail wheel.
    /// </summary>
    public static Blueprint Kestrel()
    {
        var d = new Draft();
        d.Set(0, 1, -8, "air_propeller");
        d.Set(0, 1, -7, "aero_engine");
        d.Set(-1, 1, -3, "fuel_tank");
        d.Set(1, 1, -3, "fuel_tank");
        d.Set(0, 2, -2, "driver_seat");

        // Wing guns, each with a rack of AP behind it, and the bombs hung under the wings.
        foreach (int x in new[] { -3, 3 })
        {
            d.Set(x, 1, -4, "breech_40ac");
            d.Set(x, 1, -5, "barrel");
            d.Set(x, 1, -3, "ammo_ap");
            d.Set(x * 5 / 3, 0, -2, "bomb_rack");
        }

        // Ailerons on the outer trailing edge, then the rest of the wing.
        d.Fill(7, 10, 1, 1, 0, 0, "control_surface");
        d.Fill(-10, -7, 1, 1, 0, 0, "control_surface");
        d.Fill(2, 10, 1, 1, -4, 0, "wing");
        d.Fill(-10, -2, 1, 1, -4, 0, "wing");

        // Fuselage and tail boom.
        d.Fill(-1, 1, 1, 1, -6, 1, "airframe");
        d.Fill(-1, 1, 2, 2, -6, -4, "airframe");
        d.Set(0, 2, -3, "airframe");
        d.Fill(0, 0, 2, 2, -1, 1, "airframe");
        d.Fill(0, 0, 1, 1, 2, 7, "airframe");

        // Tail: stabilizer and elevators, and a fin with a rudder.
        d.Fill(1, 3, 1, 1, 6, 6, "wing");
        d.Fill(-3, -1, 1, 1, 6, 6, "wing");
        d.Fill(1, 3, 1, 1, 7, 7, "control_surface");
        d.Fill(-3, -1, 1, 1, 7, 7, "control_surface");
        int upright = Orientations.RotateAroundZ(0, 1);
        d.Fill(0, 0, 2, 3, 6, 6, "wing", upright);
        d.Fill(0, 0, 2, 3, 7, 7, "control_surface", upright);

        // Landing gear: main wheels under the wing roots, a steering tail wheel.
        d.Set(-2, 0, -2, "wheel");
        d.Set(2, 0, -2, "wheel");
        d.Set(0, 0, 6, "wheel_steer");
        return d.Build("Kestrel (fighter-bomber)");
    }

    /// <summary>Attack helicopter: one big rotor over the cabin, a chin autocannon for the gunner, and an optically guided missile on each stub wing.</summary>
    public static Blueprint Hornet()
    {
        var d = new Draft();
        d.Set(0, 4, 0, "rotor");
        d.Set(0, 3, 1, "aero_engine");
        d.Set(-1, 2, 1, "fuel_tank");
        d.Set(1, 2, 1, "fuel_tank");
        d.Set(0, 2, -2, "driver_seat");
        d.Set(0, 2, -3, "gunner_seat");

        // Chin gun.
        d.Set(0, 1, -4, "breech_40ac");
        d.Set(0, 1, -5, "barrel");
        d.Set(0, 1, -3, "ammo_he");
        d.Set(-1, 1, -4, "ammo_ap");

        // Stub wings with a missile rail each: motor, fins, warhead, optical seeker on the nose.
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x, 1, 0, "airframe");
            int side = Math.Sign(x) * 3;
            d.Set(side, 1, 0, "missile_rail");
            d.Set(side, 1, -1, "missile_motor");
            d.Set(side, 1, -2, "missile_fins");
            d.Set(side, 1, -3, "missile_warhead");
            d.Set(side, 1, -4, "seeker_optical");
        }

        // Cabin, tail boom, and fins at the tail.
        d.Fill(-1, 1, 1, 3, -3, 2, "airframe");
        d.Fill(0, 0, 2, 2, 3, 8, "airframe");
        int upright = Orientations.RotateAroundZ(0, 1);
        d.Set(0, 3, 8, "wing", upright);
        d.Set(1, 2, 8, "wing");
        d.Set(-1, 2, 8, "wing");

        // Skids.
        d.Fill(-1, -1, 0, 0, -2, 2, "frame");
        d.Fill(1, 1, 0, 0, -2, 2, "frame");
        return d.Build("Hornet (helicopter)");
    }

    /// <summary>Quadcopter drone, flown by remote: four lift fans on arms, an autocannon underneath.</summary>
    public static Blueprint Wasp()
    {
        var d = new Draft();
        d.Set(0, 1, 0, "drone_core");
        d.Set(0, 1, 1, "aero_engine");
        d.Set(0, 1, -1, "fuel_tank");
        int up = Pointing(Int3.Up);
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x / 2, 1, 0, "frame");
            d.Set(x, 1, 0, "frame");
            d.Set(x, 1, -1, "frame");
            d.Set(x, 1, 1, "frame");
            d.Set(x, 1, -2, "lift_fan", up);
            d.Set(x, 1, 2, "lift_fan", up);
            d.Set(x, 0, 0, "frame");
        }

        d.Set(0, 0, -1, "breech_40ac");
        d.Set(0, 0, -2, "barrel");
        d.Set(0, 0, 0, "ammo_flak");
        return d.Build("Wasp (drone)");
    }

    /// <summary>Airship: a long envelope of gas cells over a gondola with the crew, a 37 mm gun, four bombs, and two propellers on outriggers.</summary>
    public static Blueprint Zephyr()
    {
        var d = new Draft();
        // Gondola: crew, engine and gun.
        d.Set(0, 1, -3, "driver_seat");
        d.Set(0, 1, -2, "gunner_seat");
        d.Set(0, 1, 2, "engine_large");
        d.Set(-1, 1, 2, "fuel_tank");
        d.Set(1, 1, 2, "fuel_tank");
        d.Set(0, 1, -4, "breech_37");
        for (int z = -5; z >= -7; z--) d.Set(0, 1, z, "barrel");
        d.Set(-1, 1, -4, "ammo_he");
        d.Set(1, 1, -4, "ammo_ap");
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            d.Set(x, 0, z, "bomb_rack");
        d.Fill(-1, 1, 1, 2, -4, 4, "airframe");
        d.Fill(0, 0, 3, 3, -3, 3, "frame"); // struts up to the envelope

        // Outriggers with a propeller on each.
        foreach (int x in new[] { -1, 1 })
        {
            for (int i = 2; i <= 4; i++) d.Set(x * i, 1, 2, "frame");
            d.Set(x * 4, 1, 1, "air_propeller");
        }

        // The envelope: a long rounded body of gas cells.
        const int half = 12;
        for (int z = -half; z <= half; z++)
        {
            float r = 2.7f * MathF.Sqrt(1f - z * z / (float)((half + 1) * (half + 1)));
            for (int x = -3; x <= 3; x++)
            for (int y = 3; y <= 9; y++)
                if (x * x + (y - 6) * (y - 6) <= r * r + 0.3f) d.Set(x, y, z, "gas_cell");
        }

        // Tail surfaces.
        int upright = Orientations.RotateAroundZ(0, 1);
        d.Set(0, 6, half + 1, "control_surface", upright);
        d.Set(0, 7, half + 1, "control_surface", upright);
        d.Set(0, 5, half + 1, "wing", upright);
        d.Set(1, 6, half + 1, "control_surface");
        d.Set(-1, 6, half + 1, "control_surface");
        return d.Build("Zephyr (airship)");
    }

    /// <summary>A flat wall of one armor type, standing on the ground (y = 0).</summary>
    public static Blueprint Wall(string blockId, int width, int height)
    {
        var d = new Draft();
        int half = width / 2;
        d.Fill(-half, width - half - 1, 0, height - 1, 0, 0, blockId);
        return d.Build($"{BlockCatalog.Get(blockId).Name} wall");
    }

    /// <summary>A small bunker with an ammo store inside. Get a shell into it and watch.</summary>
    public static Blueprint Bunker()
    {
        var d = new Draft();
        d.Set(0, 0, 0, "ammo_he");
        d.Set(1, 0, 0, "ammo_he");
        d.Set(-1, 0, 0, "fuel_tank");
        d.Fill(-3, 3, 0, 1, -2, -2, "armor_heavy");
        d.Fill(-3, 3, 2, 2, -2, -2, "armor_heavy_slope");
        d.Fill(-3, 3, 0, 1, 2, 2, "armor_light");
        d.Fill(-3, -3, 0, 1, -1, 1, "armor_light");
        d.Fill(3, 3, 0, 1, -1, 1, "armor_light");
        d.Fill(-3, 3, 2, 2, -1, 2, "armor_light");
        d.Fill(-2, 2, 0, 1, -1, 1, "frame");
        return d.Build("Bunker");
    }
}
