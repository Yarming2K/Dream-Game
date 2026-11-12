using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamGame.Core;

/// <summary>Ready-made designs to drive, take apart and learn from, plus test-range targets.</summary>
public static class ExampleDesigns
{
    private sealed class Draft
    {
        private readonly Dictionary<Int3, BlueprintBlock> _cells = new();
        private readonly Dictionary<Int3, BlueprintBlock> _filled = new();

        /// <summary>Places a block, replacing whatever is in its way.</summary>
        public void Set(int x, int y, int z, string id, int o = 0)
        {
            var anchor = new Int3(x, y, z);
            BlockDef def = BlockCatalog.Get(id);
            foreach (Int3 c in Footprint.Cells(def, anchor, o))
                if (_filled.TryGetValue(c, out BlueprintBlock old)) Remove(old);
            Put(new BlueprintBlock(id, anchor, o), def);
        }

        private void Put(BlueprintBlock b, BlockDef def)
        {
            _cells[b.Cell] = b;
            foreach (Int3 c in Footprint.Cells(def, b.Cell, b.O)) _filled[c] = b;
        }

        private void Remove(BlueprintBlock b)
        {
            _cells.Remove(b.Cell);
            foreach (Int3 c in Footprint.Cells(BlockCatalog.Get(b.Id), b.Cell, b.O)) _filled.Remove(c);
        }

        private bool Free(BlockDef def, Int3 anchor, int o)
        {
            foreach (Int3 c in Footprint.Cells(def, anchor, o))
                if (_filled.ContainsKey(c)) return false;
            return true;
        }

        /// <summary>The id of the block filling a cell, or null if it's empty.</summary>
        public string IdAt(int x, int y, int z) => _filled.TryGetValue(new Int3(x, y, z), out BlueprintBlock b) ? b.Id : null;

        /// <summary>Places a block only if every cell it needs is empty; says whether it did.</summary>
        public bool SetIfFree(int x, int y, int z, string id, int o = 0)
        {
            var anchor = new Int3(x, y, z);
            BlockDef def = BlockCatalog.Get(id);
            if (!Free(def, anchor, o)) return false;
            Put(new BlueprintBlock(id, anchor, o), def);
            return true;
        }

        /// <summary>Fills a box, skipping cells that already have something.</summary>
        public void Fill(int x0, int x1, int y0, int y1, int z0, int z1, string id, int o = 0)
        {
            BlockDef def = BlockCatalog.Get(id);
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                var c = new Int3(x, y, z);
                if (!_filled.ContainsKey(c)) Put(new BlueprintBlock(id, c, o), def);
            }
        }

        /// <summary>
        /// Tiles a box with a big block (turned by <paramref name="o"/>) wherever it fits, then fills the gaps
        /// with <paramref name="filler"/>.
        /// </summary>
        public void Tile(int x0, int x1, int y0, int y1, int z0, int z1, string id, int o, string filler)
        {
            BlockDef def = BlockCatalog.Get(id);
            Int3 lo = default, hi = default;
            bool first = true;
            foreach (Int3 c in Footprint.Cells(def, Int3.Zero, o))
            {
                lo = first ? c : Int3.Min(lo, c);
                hi = first ? c : Int3.Max(hi, c);
                first = false;
            }

            Int3 ext = hi - lo + new Int3(1, 1, 1);
            for (int x = x0; x + ext.X - 1 <= x1; x += ext.X)
            for (int y = y0; y + ext.Y - 1 <= y1; y += ext.Y)
            for (int z = z0; z + ext.Z - 1 <= z1; z += ext.Z)
            {
                Int3 anchor = new Int3(x, y, z) - lo;
                if (Free(def, anchor, o)) Put(new BlueprintBlock(id, anchor, o), def);
            }

            if (filler != null) Fill(x0, x1, y0, y1, z0, z1, filler);
        }

        /// <summary>A slope whose sloped face points along <paramref name="facing"/> (e.g. (1,-1,0) for a bilge chamfer).</summary>
        public void Slope(int x, int y, int z, string id, Int3 facing) => Set(x, y, z, id, Facing(facing));

        public void FillSlope(int x0, int x1, int y0, int y1, int z0, int z1, string id, Int3 facing) =>
            Fill(x0, x1, y0, y1, z0, z1, id, Facing(facing));

        public void Remove(int x, int y, int z)
        {
            if (_filled.TryGetValue(new Int3(x, y, z), out BlueprintBlock b)) Remove(b);
        }

        public Blueprint Build(string name, string paint = null)
        {
            Blueprint bp = Blueprint.FromCellMap(name, _cells.Values);
            bp.Paint = paint;
            return bp;
        }
    }

    /// <summary>The orientation that sets a detail's base (its local down) against <paramref name="down"/>, its front toward <paramref name="forward"/>.</summary>
    public static int Orient(Int3 down, Int3 forward)
    {
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.Apply(o, new Int3(0, -1, 0)) == down && Orientations.Apply(o, new Int3(0, 0, -1)) == forward) return o;
        throw new System.ArgumentException($"No orientation puts down at {down} and forward at {forward}.");
    }

    /// <summary>The orientation that turns an outer corner's two slopes to face <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static int CornerFacing(Int3 a, Int3 b)
    {
        for (int o = 0; o < Orientations.Count; o++)
        {
            Int3 s1 = Orientations.Apply(o, new Int3(0, 1, -1)), s2 = Orientations.Apply(o, new Int3(-1, 1, 0));
            if ((s1 == a && s2 == b) || (s1 == b && s2 == a)) return o;
        }

        throw new System.ArgumentException($"No corner faces {a} and {b}.");
    }

    /// <summary>The orientation that turns a wedge's sloped face (local (0,1,-1)) to point along <paramref name="facing"/>.</summary>
    public static int Facing(Int3 facing)
    {
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.Apply(o, new Int3(0, 1, -1)) == facing) return o;
        throw new System.ArgumentException($"No orientation faces {facing}.");
    }

    /// <summary>
    /// Turns a swept wing edge so its cut edge faces <paramref name="cut"/> (e.g. forward and out) with the panel square to
    /// <paramref name="normal"/>: (0,1,0) for a wing, (1,0,0) for a fin.
    /// </summary>
    public static int Edge(Int3 cut, Int3 normal)
    {
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.Apply(o, new Int3(-1, 0, -1)) == cut && (Orientations.Up(o) == normal || Orientations.Up(o) == -normal)) return o;
        throw new System.ArgumentException($"No swept edge faces {cut} square to {normal}.");
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

    /// <summary>The example machines, front by front (menus list them this way).</summary>
    public static IReadOnlyList<(string Front, Blueprint[] Designs)> ByFront => new[]
    {
        ("Land", new[] { Warden(), Vanguard(), Jackal(), Juggernaut(), Bulwark(), Hailstorm(), Skyguard(), PalisadeLauncher(), PalisadeRadar(), Mole() }),
        ("Sea", new[] { Corsair(), Trident(), Barracuda(), Colossus() }),
        ("Air", new[] { Kestrel(), Falcon(), Lancer(), Starling(), Hornet(), Wasp(), Zephyr() }),
    };

    public static IReadOnlyList<Blueprint> All => ByFront.SelectMany(g => g.Designs).ToArray();

    /// <summary>
    /// Starling: a full-size twin-engine air-superiority fighter, 16 m long with a 12.5 m span, drawn from the
    /// look of today's fighters: a long radar nose, a bubble canopy over a single seat, boxy intakes either
    /// side of the cockpit feeding two built jets down long ducts, a 45° swept wing with straight edges, twin
    /// fins on tail booms, landing gear that folds away, and four radar missiles. Low-vis grey.
    /// </summary>
    public static Blueprint Starling()
    {
        var d = new Draft();

        // Nose: the radome, then the forward fuselage with rounded corners, and the cockpit under its canopy.
        d.Set(0, 2, -15, "radome");
        d.Fill(-1, 1, 1, 3, -12, 12, "airframe");
        for (int z = -12; z <= -9; z++)
            foreach (int s in new[] { -1, 1 })
            {
                d.Set(s, 1, z, "airframe_round", Facing(new Int3(s, -1, 0)));
                d.Set(s, 3, z, "airframe_round", Facing(new Int3(s, 1, 0)));
            }

        d.Set(0, 3, -9, "pilot_seat");
        d.Set(0, 4, -9, "canopy_bubble");

        // The spine behind the canopy, sloping down to the tail, with the flare launcher on top.
        d.Fill(0, 0, 4, 4, -5, 1, "airframe");
        foreach (int s in new[] { -1, 1 }) d.FillSlope(s, s, 4, 4, -5, 1, "airframe_slope", new Int3(s, 1, 0));
        d.Slope(0, 4, 2, "airframe_slope", new Int3(0, 1, 1));
        d.Set(0, 5, -1, "countermeasures");

        // Fuel down the middle.
        for (int z = 0; z <= 5; z++)
            for (int y = 1; y <= 2; y++)
                d.Set(0, y, z, "fuel_tank");

        // (Hollow inside, where nothing needs to be: it's a light skin round the fuel, the ducts and the engines.)
        for (int y = 1; y <= 2; y++)
        {
            for (int z = -8; z <= -1; z++) d.Remove(0, y, z);
            for (int z = 6; z <= 11; z++) d.Remove(0, y, z);
        }

        foreach (int s in new[] { -1, 1 })
            for (int z = 4; z <= 11; z++)
                d.Remove(s, 2, z);

        // Intake trunks either side of the cockpit: open ducts down to the engines, the outer corners rounded.
        foreach (int s in new[] { -1, 1 })
        {
            d.Fill(System.Math.Min(2 * s, 3 * s), System.Math.Max(2 * s, 3 * s), 1, 3, -8, 12, "airframe");
            for (int z = -8; z <= 12; z++)
            {
                d.Set(3 * s, 1, z, "airframe_round", Facing(new Int3(s, -1, 0)));
                d.Set(3 * s, 3, z, "airframe_round", Facing(new Int3(s, 1, 0)));
            }

            for (int z = -8; z <= 0; z++) d.Remove(2 * s, 2, z);

            // Sharp intake lips at the front, and the trunks tapering in round the nozzles at the back.
            d.Slope(2 * s, 1, -8, "airframe_slope", new Int3(0, -1, -1));
            d.Slope(2 * s, 3, -8, "airframe_slope", new Int3(0, 1, -1));
            d.Slope(3 * s, 2, -8, "airframe_slope", new Int3(s, 0, -1));
            d.Slope(2 * s, 1, 12, "airframe_slope", new Int3(0, -1, 1));
            d.Slope(2 * s, 3, 12, "airframe_slope", new Int3(0, 1, 1));
            d.Slope(3 * s, 2, 12, "airframe_slope", new Int3(s, 0, 1));
            d.Slope(s, 1, 12, "airframe_slope", new Int3(0, -1, 1));
            d.Slope(s, 3, 12, "airframe_slope", new Int3(0, 1, 1));

            // The engine, back to front: a full-size nozzle, two afterburner stages, three turbines, the core,
            // five compressor stages and a ramp intake at the end of the duct.
            int x = 2 * s;
            d.Set(x, 2, 13, "jet_nozzle_large");
            d.Set(x, 2, 12, "jet_afterburner");
            d.Set(x, 2, 11, "jet_afterburner");
            for (int z = 8; z <= 10; z++) d.Set(x, 2, z, "jet_turbine");
            d.Set(x, 2, 7, "jet_core");
            for (int z = 2; z <= 6; z++) d.Set(x, 2, z, "jet_compressor");
            d.Set(x, 2, 1, "jet_intake_ramp");
        }

        // A tail cone between the nozzles.
        d.Slope(0, 1, 12, "airframe_slope", new Int3(0, -1, 1));
        d.Slope(0, 3, 12, "airframe_slope", new Int3(0, 1, 1));
        d.Set(0, 2, 13, "airframe");
        d.Set(0, 2, 14, "nose_cone", Pointing(new Int3(0, 0, 1)));

        // The cannon in the side of the right intake trunk, its muzzle at the intake lip.
        d.Set(3, 2, -6, "breech_40ac");
        d.Set(3, 2, -7, "barrel");
        d.Set(3, 2, -8, "barrel");
        d.Set(3, 2, -5, "ammo_ap");

        // Wings: a straight leading edge swept back 45° (a swept edge on every row), a straight trailing edge,
        // ailerons on the outer half.
        foreach (int s in new[] { -1, 1 })
            for (int i = 0; i <= 8; i++)
            {
                int x = s * (4 + i), lead = -4 + i;
                d.Set(x, 2, lead, "wing_swept", Edge(new Int3(s, 0, -1), Int3.Up));
                for (int z = lead + 1; z <= 6; z++) d.Set(x, 2, z, i >= 5 && z == 6 ? "control_surface" : "wing");
            }

        // Leading-edge extensions: thin strakes running forward along the intakes, for lift at high angles.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(4 * s, 2, -8, "wing_swept", Edge(new Int3(s, 0, -1), Int3.Up));
            for (int z = -7; z <= -5; z++) d.Set(4 * s, 2, z, "wing");
            d.Set(5 * s, 2, -5, "wing_swept", Edge(new Int3(s, 0, -1), Int3.Up));
            d.Set(5 * s, 2, -4, "wing");
        }

        // Tail booms, all-moving tailplanes and twin fins with rudders.
        int upright = Orientations.RotateAroundZ(0, 1);
        foreach (int s in new[] { -1, 1 })
        {
            d.Fill(4 * s, 4 * s, 2, 2, 7, 14, "airframe");
            d.Set(4 * s, 2, 15, "nose_cone", Pointing(new Int3(0, 0, 1)));
            for (int i = 0; i <= 3; i++)
            {
                int x = s * (5 + i), lead = 11 + i;
                d.Set(x, 2, lead, "wing_swept", Edge(new Int3(s, 0, -1), Int3.Up));
                // (All-moving, like a modern fighter's: the whole tailplane swings.)
                for (int z = lead + 1; z <= 15; z++) d.Set(x, 2, z, "control_surface");
            }

            for (int j = 0; j <= 4; j++)
            {
                int y = 3 + j, lead = 8 + j;
                d.Set(4 * s, y, lead, "wing_swept", Edge(new Int3(0, 1, -1), Int3.Right));
                for (int z = lead + 1; z <= 13; z++) d.Set(4 * s, y, z, j is >= 1 and <= 3 && z == 13 ? "control_surface" : "wing", upright);
            }
        }

        // Radar missiles under each wing, two a side.
        foreach (int s in new[] { -1, 1 })
            foreach ((int x, int rail) in new[] { (7 * s, 5), (10 * s, 6) })
            {
                d.Set(x, 1, rail, "missile_rail");
                d.Set(x, 1, rail - 1, "missile_motor");
                d.Set(x, 1, rail - 2, "missile_fins");
                d.Set(x, 1, rail - 3, "missile_warhead");
                d.Set(x, 1, rail - 4, "seeker_radar");
            }

        // Landing gear: a steering nose leg under the cockpit, main legs under the engines. They fold away in flight.
        d.Set(0, 0, -11, "landing_gear_steer");
        foreach (int s in new[] { -1, 1 }) d.Set(2 * s, 0, 5, "landing_gear");
        return d.Build("Starling (air-superiority fighter)", "lowvis");
    }

    /// <summary>
    /// The heavy 8×8 truck under the Palisade air-defence vehicles: two steering axles and two driven ones on
    /// 1.4 m tyres, a cab-over cab with a windscreen and side windows, the engine behind it and a flat bed.
    /// The cab is 2.5 m wide; the whole truck about 10.5 m long.
    /// </summary>
    private static void HeavyTruck(Draft d, string rightSeat)
    {
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(2 * s, 1, -8, "wheel_heavy_steer");
            d.Set(2 * s, 1, -5, "wheel_heavy_steer");
            d.Set(2 * s, 1, 4, "wheel_heavy");
            d.Set(2 * s, 1, 7, "wheel_heavy");
        }

        // Chassis rails and the bed.
        d.Fill(-1, 1, 1, 2, -11, 9, "frame");
        d.Fill(-2, 2, 3, 3, -6, 9, "armor_light");
        d.Fill(-2, 2, 2, 2, -12, -12, "frame");
        foreach (int s in new[] { -1, 1 }) d.Set(2 * s, 3, -12, "headlight");

        // The cab: crew seats, the windscreen and side windows, the roof sloped at the front.
        d.Fill(-2, 2, 3, 5, -11, -7, "armor_light");
        for (int x = -1; x <= 1; x++)
        for (int z = -10; z <= -8; z++)
        for (int y = 3; y <= 4; y++)
            d.Remove(x, y, z);
        d.Set(-1, 3, -9, "driver_seat");
        d.Set(1, 3, -9, rightSeat);
        for (int x = -2; x <= 2; x++)
        {
            d.Set(x, 4, -11, "glazing");
            d.Slope(x, 5, -11, "armor_light_slope", new Int3(0, 1, -1));
        }

        foreach (int s in new[] { -1, 1 })
            for (int z = -10; z <= -8; z++)
                d.Set(2 * s, 4, z, "glazing");

        // The engine bay behind the cab, with its exhaust stack.
        d.Fill(-2, 2, 3, 4, -6, -5, "armor_light");
        d.Set(0, 3, -6, "engine_large");
        d.Set(-1, 3, -5, "fuel_tank");
        d.Set(1, 3, -5, "fuel_tank");
        d.Set(2, 5, -6, "exhaust");

        // Outriggers that brace it level when it stops.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(2 * s, 2, -2, "stabilizer", Pointing(new Int3(s, 0, 0)));
            d.Set(2 * s, 2, 9, "stabilizer", Pointing(new Int3(s, 0, 0)));
        }
    }

    /// <summary>
    /// Palisade launcher: a modern surface-to-air missile truck. Six missiles in a box launcher raised over the
    /// back of an 8×8 truck, the way today's air-defence batteries carry theirs, with outriggers to brace it.
    /// Pair it with the Palisade radar.
    /// </summary>
    public static Blueprint PalisadeLauncher()
    {
        var d = new Draft();
        HeavyTruck(d, "gunner_seat");
        d.Set(0, 6, 6, "sam_launcher", Pointing(new Int3(0, 0, 1)));
        d.Fill(-2, 2, 4, 4, -3, -3, "armor_light");
        d.Set(0, 5, -3, "toolbox");
        d.Set(2, 4, -4, "antenna");
        return d.Build("Palisade launcher (SAM truck)", "forest_digital");
    }

    /// <summary>
    /// Palisade radar: the battery's eyes and command post. A phased-array radar faces back over the tail of
    /// an 8×8 truck, a search radar turns on the roof of the operators' shelter, and the shelter holds a command
    /// console to direct the launchers from (M for the tactical map).
    /// </summary>
    public static Blueprint PalisadeRadar()
    {
        var d = new Draft();
        HeavyTruck(d, "commander_seat");
        d.Fill(-2, 2, 4, 5, -4, 3, "armor_light");
        d.Set(0, 4, 0, "command_console");
        d.Set(0, 7, -1, "radar_rotating");
        d.Set(0, 6, 7, "radar_array", Pointing(new Int3(0, 0, 1)));
        d.Set(2, 6, -3, "antenna");
        return d.Build("Palisade radar (radar truck)", "forest_digital");
    }

    /// <summary>
    /// Mole: an armoured dozer. A tracked hull with a blade 4.5 m wide across the front (three sections,
    /// wider than the tracks, so it can drive down into its own cut), a small 37 mm turret to look after
    /// itself, and the kit for working away from the depot: toolboxes, spare track links and a searchlight.
    /// K lowers the blade to dig ramps, trenches and firing pits; raised, it pushes earth into berms.
    /// </summary>
    public static Blueprint Mole()
    {
        var d = new Draft();
        for (int z = -5; z <= 4; z++)
        {
            d.Set(-3, 0, z, "track");
            d.Set(3, 0, z, "track");
        }

        // The blade, across the front, its bottom row level with the tracks.
        foreach (int x in new[] { -3, 0, 3 }) d.Set(x, 0, -7, "dozer_blade");

        // Inside: the driver up front, the engines and fuel behind, ammunition under the turret.
        d.Set(-1, 1, -5, "driver_seat");
        d.Set(1, 1, -5, "commander_seat");
        d.Set(-1, 1, 3, "engine_large");
        d.Set(1, 1, 3, "engine_large");
        d.Set(0, 1, 3, "fuel_tank");
        d.Set(0, 1, 4, "fuel_tank");
        d.Set(-1, 1, -1, "ammo_ap");
        d.Set(1, 1, -1, "ammo_he");
        d.Set(0, 1, -1, "ammo_feed");

        // Armour: a thick sloped front behind the blade, plain sides, a flat deck.
        d.Fill(-2, 2, 0, 0, -6, -6, "armor_heavy");
        d.Fill(-3, 3, 1, 1, -6, -6, "armor_heavy_slope");
        d.Fill(-3, 3, 2, 2, -5, -5, "armor_heavy_shallow");
        d.Fill(-2, 2, 0, 0, 5, 5, "armor_light");
        d.Fill(-3, 3, 1, 1, 5, 5, "armor_light");
        d.Fill(-3, -3, 1, 1, -5, 4, "armor_light");
        d.Fill(3, 3, 1, 1, -5, 4, "armor_light");
        d.Set(0, 2, -1, "turret_ring");
        d.Fill(-3, 3, 2, 2, -4, 5, "armor_light");
        d.Fill(-2, 2, 0, 1, -5, 4, "frame");

        // A small turret: 37 mm, its gunner, armoured round the front.
        d.Set(0, 3, -1, "breech_37");
        for (int z = -2; z >= -4; z--) d.Set(0, 3, z, "barrel");
        d.Set(1, 3, -1, "ammo_ap");
        d.Set(-1, 3, -1, "ammo_he");
        d.Set(0, 3, 0, "gunner_seat");
        d.Fill(-2, 2, 3, 3, -2, -2, "armor_heavy");
        d.Fill(-2, -2, 3, 3, -1, 1, "armor_light");
        d.Fill(2, 2, 3, 3, -1, 1, "armor_light");
        d.Fill(-1, 1, 3, 3, 1, 1, "armor_light");
        d.Fill(-2, 2, 4, 4, -2, -2, "armor_heavy_slope");
        d.Fill(-2, 2, 4, 4, -1, 1, "armor_light");

        // Kit for working in the field.
        d.Set(0, 5, 0, "hatch");
        d.Set(1, 5, -1, "periscope");
        d.Set(-2, 3, -3, "searchlight");
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(3 * s, 2, 2, "spare_tracks", Orient(new Int3(-s, 0, 0), new Int3(0, 0, -1)));
            foreach (int z in new[] { -3, 0 }) d.Set(4 * s, 1, z, "toolbox", Orient(new Int3(-s, 0, 0), new Int3(0, 0, -1)));
            d.Set(s, 1, 6, "exhaust", Orient(new Int3(0, 0, -1), new Int3(0, 1, 0)));
            d.Set(2 * s, 3, 3, "vent");
        }

        d.Set(-2, 5, 1, "antenna");
        return d.Build("Mole (armoured dozer)", "olive");
    }

    /// <summary>Medium tank: sloped front, 76 mm gun in a turret with a gunner and commander, ammo stored low in the hull.</summary>
    public static Blueprint Warden() => MediumTank(railgun: false);

    /// <summary>
    /// Railgun tank: the Warden's hull and turret carrying a railgun on eight rails instead of the 76 mm gun.
    /// Two generators in the engine bay and three capacitor banks under the turret power it; it fires slugs
    /// from the AP racks.
    /// </summary>
    public static Blueprint Vanguard() => MediumTank(railgun: true);

    private static Blueprint MediumTank(bool railgun)
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
        d.Set(-1, 1, -2, railgun ? "capacitor" : "ammo_he");
        d.Set(1, 1, -2, railgun ? "capacitor" : "ammo_he");
        if (railgun) d.Set(0, 1, -2, "capacitor");
        d.Set(-1, 1, 3, "engine_large");
        d.Set(1, 1, 3, "engine_small");
        d.Set(0, 1, 3, "fuel_tank");
        d.Set(-2, 1, 3, railgun ? "generator" : "fuel_tank");
        d.Set(2, 1, 3, railgun ? "generator" : "fuel_tank");
        d.Set(0, 1, 4, "fuel_tank");

        // Front: a steep lower plate, then one long shallow upper glacis (two blocks deep), which counts as
        // nearly twice as thick as the plate itself to a shell coming straight on.
        d.Fill(-2, 2, 0, 0, -6, -6, "armor_heavy");
        d.Fill(-3, 3, 1, 1, -6, -6, "armor_heavy_slope");
        d.Fill(-3, 3, 2, 2, -5, -5, "armor_heavy_shallow");
        d.Fill(-3, 3, 2, 2, -4, -4, "armor_heavy_shallow_high");

        // Hull shell.
        d.Fill(-2, 2, 0, 0, 5, 5, "armor_light");
        d.Fill(-3, 3, 1, 1, 5, 5, "armor_light");
        d.Fill(-3, -3, 1, 1, -5, 4, "armor_light");
        d.Fill(3, 3, 1, 1, -5, 4, "armor_light");
        d.Set(0, 2, -1, "turret_ring");
        d.Fill(-3, 3, 2, 2, -4, 5, "armor_light");
        d.Fill(-2, 2, 0, 1, -5, 4, "frame");

        // Turret: 76 mm, six barrel segments and a muzzle brake; or the railgun on its rails.
        if (railgun)
        {
            d.Set(0, 3, -1, "rail_breech");
            for (int z = -2; z >= -9; z--) d.Set(0, 3, z, "rail");
        }
        else
        {
            d.Set(0, 3, -1, "breech_76");
            for (int z = -2; z >= -7; z--) d.Set(0, 3, z, "barrel");
            d.Set(0, 3, -8, "muzzle_brake");
        }

        d.Set(-1, 3, -1, "loader");
        d.Set(1, 3, -1, "loader");
        d.Set(0, 3, 0, railgun ? "capacitor" : "propellant");
        d.Set(1, 3, 0, "gunner_seat");
        d.Set(-1, 3, 0, "commander_seat");
        d.Fill(-2, 2, 3, 3, -3, -2, "armor_heavy");
        d.Fill(-2, -2, 3, 3, -1, 1, "armor_heavy");
        d.Fill(2, 2, 3, 3, -1, 1, "armor_heavy");
        d.Fill(-1, 1, 3, 3, 1, 1, "armor_light");
        d.Fill(-1, 1, 3, 3, 0, 0, "frame");
        d.Fill(-2, 2, 4, 4, -3, -3, "armor_heavy_slope");
        d.Fill(-2, 2, 4, 4, -2, 1, "armor_light");

        // Rounded turret: rounded front cheeks and back corners, and the front top corners bevelled both ways.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(2 * s, 3, -3, "armor_heavy_round", Facing(new Int3(s, 0, -1)));
            d.Set(2 * s, 3, 1, "armor_heavy_round", Facing(new Int3(s, 0, 1)));
            d.Set(2 * s, 4, 1, "armor_light_round", Facing(new Int3(s, 0, 1)));
            d.Set(2 * s, 4, -3, "armor_heavy_corner", CornerFacing(new Int3(0, 1, -1), new Int3(s, 1, 0)));
        }

        // Kit: hatches over the gunner and commander, smoke dischargers on the turret cheeks, an antenna,
        // spare track links hung on the turret sides, stowage boxes over the tracks, louvers over the
        // engines and exhausts on the back plate.
        d.Set(-1, 5, 0, "hatch");
        d.Set(1, 5, 0, "hatch");
        d.Set(1, 5, -2, "periscope");
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(2 * s, 5, -2, "smoke_launcher");
            d.Set(3 * s, 3, 0, "spare_tracks", Orient(new Int3(-s, 0, 0), new Int3(0, 0, -1)));
            foreach (int z in new[] { -1, 2 }) d.Set(4 * s, 1, z, "toolbox", Orient(new Int3(-s, 0, 0), new Int3(0, 0, -1)));
            d.Set(s, 1, 6, "exhaust", Orient(new Int3(0, 0, -1), new Int3(0, 1, 0)));
            d.Set(s, 3, 3, "vent");
        }

        d.Set(-2, 5, 1, "antenna");
        return railgun ? d.Build("Vanguard (railgun tank)", "urban") : d.Build("Warden (medium tank)", "woodland");
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

        // Headlights on the nose, a stowage rack and an antenna at the back.
        d.Set(-2, 2, -4, "headlight");
        d.Set(2, 2, -4, "headlight");
        d.Set(0, 3, 3, "stowage_rack");
        d.Set(-2, 3, 3, "antenna");
        d.Set(-1, 5, -1, "hatch");
        return d.Build("Jackal (scout car)", "desert");
    }

    /// <summary>
    /// Juggernaut: a tracked carrier for the 420 mm siege mortar. It can bring a heavy weapon anywhere, but
    /// it has to stop and put its four stabilizers down before it can fire, and it reloads at half a
    /// building's rate.
    /// </summary>
    public static Blueprint Juggernaut()
    {
        var d = new Draft();
        for (int z = -7; z <= 7; z++)
        {
            d.Set(-4, 0, z, "track");
            d.Set(4, 0, z, "track");
        }

        d.Set(0, 1, -6, "driver_seat");
        foreach (int x in new[] { -2, 0, 2 }) d.Set(x, 1, 6, "engine_large");
        foreach (int x in new[] { -1, 1 }) d.Set(x, 1, 6, "fuel_tank");
        d.Set(0, 1, 5, "fuel_tank");
        d.Fill(-3, 3, 0, 1, -7, 7, "frame");
        d.Fill(-3, 3, 2, 2, -7, 7, "armor_light");
        d.FillSlope(-3, 3, 1, 1, -8, -8, "armor_heavy_slope", new Int3(0, 1, -1));
        d.Fill(-3, 3, 1, 1, 8, 8, "armor_light");

        // The mortar on its turntable, the magazine beside it and the gunner behind.
        d.Set(0, 3, 1, "turret_ring");
        d.Set(0, 5, 1, "siege_mortar");
        for (int z = -1; z >= -3; z--) d.Set(0, 5, z, "heavy_barrel");
        d.Set(2, 4, 1, "magazine_he");
        d.Set(0, 4, 3, "gunner_seat");

        // Stabilizers at the four corners, legs pointing out.
        foreach (int z in new[] { -6, 6 })
        {
            d.Set(-4, 1, z, "stabilizer", Pointing(new Int3(-1, 0, 0)));
            d.Set(4, 1, z, "stabilizer", Pointing(new Int3(1, 0, 0)));
        }

        d.Set(-2, 3, -6, "headlight");
        d.Set(2, 3, -6, "headlight");
        d.Set(-3, 3, 7, "antenna");
        return d.Build("Juggernaut (siege mortar carrier)", "olive");
    }

    /// <summary>
    /// Rocket artillery truck: a glazed cab up front and a launcher on a turntable over the bed, pointing aft,
    /// with four packs of tubes (eight rockets a salvo) and reloads in racks around it. Park, turn the
    /// launcher toward the target kilometers away, aim at the ground and fire.
    /// </summary>
    public static Blueprint Hailstorm()
    {
        var d = new Draft();
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x, 0, -5, "wheel_steer");
            foreach (int z in new[] { 0, 2, 4 }) d.Set(x, 0, z, "wheel");
        }

        // Cab: engine under the hood, the crew behind a windscreen.
        d.Set(0, 1, -6, "engine_large");
        d.Set(-1, 1, -6, "fuel_tank");
        d.Set(1, 1, -6, "fuel_tank");
        d.Set(-1, 1, -4, "driver_seat");
        d.Set(1, 1, -4, "gunner_seat");
        d.Fill(-1, 1, 2, 2, -5, -5, "glazing");
        d.Fill(-2, -2, 2, 2, -4, -3, "glazing");
        d.Fill(2, 2, 2, 2, -4, -3, "glazing");
        d.Fill(-2, 2, 1, 1, -5, -3, "armor_light");
        d.Fill(-2, 2, 2, 2, -5, -5, "armor_light");
        d.Fill(-1, 1, 2, 2, -3, -3, "armor_light");
        d.Fill(-2, 2, 3, 3, -5, -3, "armor_light");
        d.FillSlope(-1, 1, 2, 2, -6, -6, "armor_light_slope", new Int3(0, 1, -1));
        d.Fill(-2, 2, 1, 1, -7, -6, "armor_light");

        // Reloads in the bed, fed up through the turntable; chassis rails along the bottom, and the bed itself.
        d.Set(0, 1, 1, "ammo_feed");
        foreach (int x in new[] { -1, 1 }) d.Set(x, 1, 1, "ammo_he");
        d.Fill(-1, 1, 0, 0, -7, 5, "frame");
        d.Fill(-2, 2, 1, 1, -2, 5, "armor_light");

        // The launcher: a turntable on the bed, the pod on it facing aft, four packs of tubes behind it, and
        // ready racks on the turntable beside it.
        d.Set(0, 2, 1, "turret_ring");
        d.Set(0, 3, 1, "rocket_pod", Pointing(Int3.Back));
        for (int z = 2; z <= 5; z++) d.Set(0, 3, z, "rocket_tube", Pointing(Int3.Back));
        foreach (int x in new[] { -1, 1 }) d.Set(x, 3, 1, "ammo_he");
        d.Set(0, 3, 0, "ammo_he");
        d.Fill(-2, 2, 2, 2, -2, -1, "armor_light");

        // Lights, mirrors' worth of antenna, and a stowage rack on the cab roof.
        d.Set(-2, 1, -8, "headlight");
        d.Set(2, 1, -8, "headlight");
        d.Set(0, 4, -4, "stowage_rack");
        d.Set(-2, 4, -3, "antenna");
        return d.Build("Hailstorm (rocket artillery)", "olive");
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

        // Kit.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(2 * s, 4, -3, "smoke_launcher");
            foreach (int z in new[] { 0, 3 }) d.Set(4 * s, 1, z, "toolbox", Orient(new Int3(-s, 0, 0), new Int3(0, 0, -1)));
            d.Set(s, 1, 7, "exhaust", Orient(new Int3(0, 0, -1), new Int3(0, 1, 0)));
        }

        d.Set(-1, 4, -2, "hatch");
        d.Set(1, 4, 0, "periscope");
        d.Set(2, 4, 1, "antenna");
        d.Set(0, 3, 4, "vent");
        return d.Build("Bulwark (tank destroyer)", "olive");
    }

    /// <summary>
    /// Patrol boat: a 20 m hull with a sharp V bow and a transom stern, split into four watertight compartments;
    /// a 76 mm turret forward and a 37 mm turret aft, deck torpedo tubes, and twin propellers and rudders under the
    /// stern. A wheelhouse amidships with a raked windscreen and a mast carrying her radar, life rafts, bollards
    /// and anchors, and guard rails down both sides.
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
        d.Slope(0, deck + 2, -14, "armor_light_slope", new Int3(0, 1, -1));

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

        // The wheelhouse amidships: windows all round under a raked windscreen, the helm and the commander, and a
        // command console (she can lead a flotilla: M for the tactical map).
        d.Set(0, deck + 1, -1, "driver_seat");
        d.Set(0, deck + 2, -1, "commander_seat");
        d.Set(1, deck + 1, 0, "command_console");
        d.Fill(-2, 2, deck + 1, deck + 1, -2, 3, "armor_light");
        d.Fill(-2, -2, deck + 2, deck + 2, -1, 2, "glazing");
        d.Fill(2, 2, deck + 2, deck + 2, -1, 2, "glazing");
        d.Fill(-2, 2, deck + 2, deck + 2, -2, 3, "armor_light");
        d.FillSlope(-2, 2, deck + 2, deck + 2, -3, -3, "glazing_slope", new Int3(0, 1, -1));
        d.Fill(-2, 2, deck + 3, deck + 3, -2, 3, "armor_light");
        ChamferEdge(d, -2, 2, deck + 3, -2, 3, "armor_light");
        // The mast behind it with the radar, a searchlight and an antenna on the wheelhouse roof.
        d.Set(0, deck + 8, 3, "mast_pole");
        d.Set(0, deck + 4, 1, "radar");
        d.Set(-1, deck + 4, -1, "searchlight");
        d.Set(1, deck + 4, 2, "antenna");

        // Deck torpedo tubes, each with a rack behind it.
        foreach (int x in new[] { -3, 3 })
        {
            d.Set(x, deck + 1, -5, "torpedo_tube");
            d.Set(x, deck + 1, -4, "ammo_torpedo");
        }

        // Watertight bulkheads: four compartments, so one hole doesn't sink her.
        foreach (int z in new[] { -10, 0, 10 }) d.Fill(-3, 3, 1, deck - 1, z, z, "armor_light");

        // The hull itself, a V bow and a transom.
        Func<int, int> hw = ShipHull(d, bow, stern, 4, deck, "armor_light");

        // Propellers and rudders under the stern.
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x, 0, stern + 1, "ship_propeller");
            d.Set(x, 0, stern + 2, "rudder");
        }

        // Life rafts and bollards along the deck edges, anchors either side of the bow, guard rails.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(3 * s, deck + 1, 4, "liferaft");
            d.Set(3 * s, deck + 1, 6, "liferaft");
            d.Set(2 * s, deck + 1, bow + 5, "bollard");
            d.Set(3 * s, deck + 1, stern - 1, "bollard");
            d.Set(s * (hw(bow + 4) + 1), deck - 1, bow + 4, "anchor", Orient(new Int3(-s, 0, 0), new Int3(0, -1, 0)));
        }

        DeckRails(d, hw, bow + 6, stern - 2, deck);
        d.Set(1, deck + 1, 4, "ladder", Orient(new Int3(0, -1, 0), new Int3(0, 0, 1)));
        d.Set(1, deck + 2, 4, "ladder", Orient(new Int3(0, -1, 0), new Int3(0, 0, 1)));
        return d.Build("Corsair (patrol boat)", "naval");
    }

    /// <summary>
    /// Missile corvette: a 64 m warship drawn from the look of a modern destroyer. A raised forecastle with a
    /// sharp flared V bow carries the railgun turret and the vertical launch cells (strike missiles for targets
    /// on land, air-defense missiles for aircraft); behind them the bridge block with four flat phased-array
    /// radar faces, its windscreen raked, a lattice mast with a turning search radar on top and a close-in
    /// gatling on its roof; amidships a deckhouse with two funnels, boats in their davits and life rafts; aft
    /// another gatling, the laser turret, and a clear deck over the stern. Below decks a fusion reactor powers
    /// the guns and four marine turbines drive two great screws, behind eight watertight bulkheads.
    /// </summary>
    public static Blueprint Trident()
    {
        var d = new Draft();
        const int bow = -64, stern = 63, deck = 10, fc = deck + 2; // the main deck and the raised forecastle's deck

        // Machinery below decks: the reactor and its capacitors amidships, four turbines and their fuel aft.
        d.Set(0, 2, 0, "reactor");
        for (int z = -3; z <= 2; z++)
            foreach (int x in new[] { -5, -4 })
                d.Set(x, 1, z, "capacitor");
        foreach (int x in new[] { -3, 2 })
        {
            d.Set(x, 1, 24, "turbine");
            d.Set(x, 1, 28, "turbine");
            d.Set(x, 1, 32, "fuel_bunker");
            d.Set(x, 1, 34, "fuel_bunker");
        }

        foreach (int z in new[] { -52, -40, -28, -14, 10, 24, 40, 54 }) d.Set(0, 1, z, "bilge_pump");
        foreach (int z in new[] { -44, -32, -16, 14, 44, 50 })
            foreach (int x in new[] { -2, -1, 1, 2 }) d.Set(x, 1, z, "lead_ballast");
        foreach ((int x, int z) in new[] { (0, -40), (0, -25), (3, -5), (3, 14), (0, 30), (0, 48) }) d.Set(x, z < -20 ? fc - 1 : deck - 1, z, "lamp");

        // The railgun turret on the forecastle, slugs fed from below, under a faceted shield.
        const int rz = -46;
        d.Set(0, fc, rz, "turret_ring");
        d.Set(0, fc - 1, rz, "ammo_feed");
        foreach (int x in new[] { -1, 1 })
        {
            d.Set(x, fc - 1, rz, "ammo_ap");
            d.Set(x, fc - 1, rz + 1, "ammo_ap");
        }

        d.Set(0, fc + 1, rz, "rail_breech");
        for (int z = rz - 1; z >= rz - 8; z--) d.Set(0, fc + 1, z, "rail");
        d.Set(-1, fc + 1, rz, "loader");
        d.Set(1, fc + 1, rz, "gunner_seat");
        d.Fill(-1, 1, fc + 1, fc + 1, rz + 1, rz + 1, "armor_light");
        d.Fill(-1, 1, fc + 2, fc + 2, rz - 1, rz + 1, "armor_light");
        d.Fill(-1, 1, fc + 1, fc + 1, rz - 1, rz - 1, "armor_light");
        foreach (int x in new[] { -1, 1 }) d.Slope(x, fc + 2, rz - 2, "armor_light_slope", new Int3(0, 1, -1));
        d.Slope(-1, fc + 2, rz + 2, "armor_light_slope", new Int3(0, 1, 1));
        d.Slope(1, fc + 2, rz + 2, "armor_light_slope", new Int3(0, 1, 1));

        // The launch cells, flush with the forecastle deck: two rows of strike missiles, two of air defense.
        for (int x = -3; x <= 3; x++)
        {
            d.Set(x, fc, -38, "vls_strike");
            d.Set(x, fc, -37, "vls_strike");
            d.Set(x, fc, -36, "vls_sam");
            d.Set(x, fc, -35, "vls_sam");
        }

        // The anchor windlass on the foredeck.
        d.Set(0, fc + 1, -59, "windlass");

        // The bridge block: two levels, the bridge on top with a raked windscreen and windows down the sides, the
        // wheel, the commander and the command console at the front window, lamps, a door at the back and a
        // ladder between the levels. Four phased-array faces: two forward, one each side, one aft.
        const int b0 = -32, b1 = -20, top = fc + 9; // the block's front and back, and its roof
        d.Set(0, fc + 6, b0 + 2, "driver_seat");
        d.Set(-2, fc + 6, b0 + 2, "commander_seat");
        d.Set(2, fc + 6, b0 + 2, "command_console");
        foreach ((int x, int z) in new[] { (0, b0 + 6), (-3, b1 - 4), (3, b1 - 4) }) d.Set(x, top - 1, z, "lamp");
        d.Set(-1, fc + 2, b1, "door");
        for (int y = fc + 1; y <= fc + 6; y++) d.Set(4, y, b1 - 1, "ladder", Orient(new Int3(0, -1, 0), new Int3(0, 0, 1)));
        d.Fill(-5, 5, fc + 7, fc + 7, b0, b0, "glazing");
        d.FillSlope(-5, 5, fc + 8, fc + 8, b0, b0, "glazing_slope", new Int3(0, 1, -1));
        foreach (int s in new[] { -1, 1 }) d.Fill(6 * s, 6 * s, fc + 7, fc + 7, b0 + 1, b0 + 4, "glazing");
        d.Fill(-6, 6, fc + 1, fc + 6, b0, b0, "armor_light");
        d.Fill(-6, 6, fc + 1, top - 1, b1, b1, "armor_light");
        d.Fill(-6, -6, fc + 1, top - 1, b0 + 1, b1 - 1, "armor_light");
        d.Fill(6, 6, fc + 1, top - 1, b0 + 1, b1 - 1, "armor_light");
        foreach (int s in new[] { -1, 1 }) d.Fill(6 * s, 6 * s, fc + 7, fc + 8, b0, b0, "armor_light");
        d.Fill(-5, 5, fc + 5, fc + 5, b0 + 1, b1 - 1, "armor_light");
        d.Remove(4, fc + 5, b1 - 1);
        d.Fill(-6, 6, top, top, b0, b1, "armor_light");
        ChamferEdge(d, -6, 6, top, b0, b1, "armor_light");
        foreach (int x in new[] { -3, 3 }) d.Set(x, fc + 3, b0 - 1, "radar_face");
        d.Set(-7, fc + 3, -26, "radar_face", Pointing(new Int3(-1, 0, 0)));
        d.Set(7, fc + 3, -26, "radar_face", Pointing(new Int3(1, 0, 0)));
        d.Set(3, fc + 3, b1 + 1, "radar_face", Pointing(Int3.Back));
        // On the roof: the lattice mast with the search radar turning on top, a gatling forward, searchlights.
        d.Set(0, top + 6, -24, "mast_lattice");
        d.Set(0, top + 14, -24, "radar_rotating");
        Ciws(d, top + 1, b0 + 2, Int3.Forward);
        d.Set(-5, top + 1, b1 - 1, "searchlight");
        d.Set(5, top + 1, b1 - 1, "searchlight");
        d.Set(4, top + 1, b1 - 2, "antenna");

        // Amidships: a deckhouse with the two funnels, the boats in their davits either side, life rafts.
        d.Fill(-4, 4, deck + 1, deck + 3, -18, 16, "armor_light");
        d.Fill(-4, 4, deck + 4, deck + 4, -18, 16, "armor_light");
        ChamferEdge(d, -4, 4, deck + 4, -18, 16, "armor_light");
        d.Set(0, deck + 7, -10, "funnel");
        d.Set(0, deck + 7, 6, "funnel");
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(6 * s, deck + 2, 0, "ships_boat");
            foreach (int z in new[] { -16, -14, 10, 12 }) d.Set(7 * s, deck + 1, z, "liferaft");
        }

        Ciws(d, deck + 5, 14, Int3.Back);

        // Aft: the laser turret facing astern, and the clear deck over the stern.
        d.Set(0, deck, 30, "turret_ring");
        d.Set(0, deck + 1, 30, "laser_emitter", Pointing(Int3.Back));
        foreach (int z in new[] { 31, 32 }) d.Set(0, deck + 1, z, "laser_lens", Pointing(Int3.Back));
        d.Set(1, deck + 1, 30, "gunner_seat");
        d.Fill(-1, 1, deck + 2, deck + 2, 29, 30, "armor_light");
        d.Fill(-1, 1, deck + 1, deck + 1, 29, 29, "armor_light");
        d.Set(-1, deck + 1, 30, "armor_light");

        // The hull: the flared V bow, the raised forecastle, the transom; then the watertight bulkheads.
        Func<int, int> hw = ShipHull(d, bow, stern, 8, deck, "armor_light", -20, 2);
        foreach (int z in new[] { -48, -34, -21, -8, 8, 21, 37, 50 })
            d.Fill(-hw(z) + 1, hw(z) - 1, 1, deck - 1, z, z, "armor_light");

        // Two great screws and their rudders under the stern.
        foreach (int x in new[] { -3, 3 })
        {
            d.Set(x, 1, stern + 1, "great_screw");
            d.Set(x, 1, stern + 3, "great_rudder");
        }

        // Anchors either side of the bow, bollards, and guard rails down both sides.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(s * (hw(-57) + 1), deck, -57, "anchor", Orient(new Int3(-s, 0, 0), new Int3(0, -1, 0)));
            d.Set(2 * s, fc + 1, -55, "bollard");
            d.Set(s * (hw(-30) - 1), fc + 1, -42, "bollard");
            d.Set(s * (hw(60) - 1), deck + 1, 60, "bollard");
        }

        for (int z = bow + 8; z <= stern - 1; z++)
        {
            int y = z < -20 ? fc : deck;
            if (z >= b0 && z <= b1) continue;
            foreach (int s in new[] { -1, 1 })
            {
                int x = s * hw(z);
                d.Fill(x, x, y + 1, y + 1, z, z, "railing", Orient(new Int3(0, -1, 0), new Int3(-s, 0, 0)));
            }
        }

        return d.Build("Trident (missile corvette)", "naval");
    }

    /// <summary>A close-in gatling on its own turntable, with a ready rack behind it (on top of a deckhouse at <paramref name="y"/>).</summary>
    private static void Ciws(Draft d, int y, int z, Int3 facing)
    {
        d.Set(0, y, z, "turret_ring");
        d.Set(0, y + 1, z, "breech_20gat", Pointing(facing));
        for (int i = 1; i <= 3; i++) d.Set(0, y + 1, z + facing.Z * i, "barrel", Pointing(facing));
        d.Set(0, y + 1, z - facing.Z, "ammo_he");
        d.Set(0, y + 2, z - facing.Z, "ammo_he");
    }

    /// <summary>
    /// Submarine: a 17.5 m coastal boat with a rounded pressure hull that tapers to a blunt bow and a cone of a tail
    /// round the shaft, ballast tanks along the keel under a watertight floor, the crew space above, a tall
    /// fin with diving planes, the commander's position and the periscope, two bow torpedo tubes, and a single
    /// screw behind a cross of rudders and stern planes.
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
        d.Set(0, floor + 1, 12, "engine_large");
        d.Set(-1, floor + 1, 12, "fuel_tank");
        d.Set(1, floor + 1, 12, "fuel_tank");
        d.Set(0, floor + 1, 0, "bilge_pump");
        d.Set(0, floor + 1, 8, "bilge_pump");

        // Ballast tanks: the space between the keel and the floor, in three sections, each with a valve.
        foreach (int z in new[] { -11, 0, 11 }) d.Set(0, 0, z, "ballast_valve");
        foreach (int z in new[] { -6, 5 }) d.Fill(-2, 2, 1, floor - 1, z, z, "armor_light");
        // Trim weight along the keel keeps her upright and sets how heavy she is.
        foreach (int z in new[] { -15, -9, -3, 3, 8, 13 }) d.Set(0, 1, z, "lead_ballast");

        // The pressure hull, station by station: a rounded section, closing in to a blunt bow and tapering
        // aft to the shaft. (Half-width, then bottom and top rows.)
        (int w, int yb, int yt) Section(int z) => (z - bow) switch
        {
            0 => (1, 2, 5),
            1 => (2, 1, 6),
            _ => (stern - z) switch { 0 => (1, 1, 3), 1 => (1, 1, 4), 2 => (2, 1, 5), 3 => (2, 0, 6), _ => (3, 0, top) },
        };

        for (int z = bow; z <= stern; z++)
        {
            (int w, int yb, int yt) = Section(z);
            bool end = z == bow || z == stern;
            d.Fill(-w + 1, w - 1, floor, floor, z, z, "armor_light");
            for (int y = yb; y <= yt; y++)
            for (int x = -w; x <= w; x++)
            {
                int ax = Math.Abs(x), sx = Math.Sign(x);
                bool rim = ax == w || y == yb || y == yt;
                // (The cells just inside each rounded corner, so the shell is joined face to face all round.)
                if (w == 3 && ax == 2 && (y == yb + 1 || y == yt - 1)) d.Fill(x, x, y, y, z, z, "armor_light");
                if (!rim && !end) continue;
                if (w == 3 && ax == 3 && (y == yb || y == yt)) continue;
                if (w == 3 && ax == 3 && (y == yb + 1 || y == yt - 1))
                    d.Fill(x, x, y, y, z, z, "armor_light_round", Facing(new Int3(sx, y == yb + 1 ? -1 : 1, 0)));
                else if (ax == w - (w == 3 ? 1 : 0) && sx != 0 && (y == yb || y == yt))
                    d.Fill(x, x, y, y, z, z, "armor_light_round", Facing(new Int3(sx, y == yb ? -1 : 1, 0)));
                else d.Fill(x, x, y, y, z, z, "armor_light");
            }
        }

        // The fin: narrow and tall, rounded at the front, the commander inside, diving planes on its sides,
        // the periscope and a mast on top.
        d.Set(0, top + 1, -5, "commander_seat");
        for (int y = top + 1; y <= top + 4; y++)
        {
            foreach (int s in new[] { -1, 1 }) d.Fill(s, s, y, y, -7, -2, "armor_light");
            d.Fill(0, 0, y, y, -2, -2, "armor_light");
            d.Fill(0, 0, y, y, -8, -8, "armor_light");
            foreach (int s in new[] { -1, 1 }) d.Fill(s, s, y, y, -8, -8, "armor_light_round", Facing(new Int3(s, 0, -1)));
        }

        d.Fill(-1, 1, top + 4, top + 4, -7, -3, "armor_light");
        int plane = Orientations.RotateAroundZ(0, 1);
        foreach (int s in new[] { -1, 1 }) d.Set(2 * s, top + 3, -6, "rudder", plane);
        d.Set(0, top + 5, -6, "periscope");
        d.Set(0, top + 5, -4, "antenna");

        // Screw, a cross of rudders and stern planes ahead of it.
        d.Set(0, 2, stern + 1, "ship_propeller");
        d.Set(0, 2, stern + 2, "rudder");
        d.Set(0, 3, stern + 2, "rudder");
        d.Set(0, 1, stern + 2, "rudder");
        d.Set(-1, 2, stern + 2, "rudder", plane);
        d.Set(1, 2, stern + 2, "rudder", plane);

        return d.Build("Barracuda (submarine)", "night");
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

        d.Set(-2, 2, -4, "headlight");
        d.Set(2, 2, -4, "headlight");
        d.Set(0, 3, 3, "stowage_rack");
        return d.Build("Skyguard (flak truck)", "woodland");
    }

    /// <summary>
    /// Fighter-bomber: one aero engine pulling a nose propeller, straight wings with ailerons at the tips,
    /// a tailplane with elevators and a fin with a rudder, an autocannon in each wing, two bombs, and a
    /// nose wheel.
    /// </summary>
    public static Blueprint Kestrel()
    {
        var d = new Draft();
        d.Set(0, 1, -8, "air_propeller");
        d.Set(0, 1, -7, "aero_engine");
        d.Set(-1, 1, 1, "fuel_tank");
        d.Set(1, 1, 1, "fuel_tank");
        // An open pilot's seat under a glazed canopy: windscreen, roof, side panes and a faired-in back.
        d.Set(0, 2, -2, "pilot_seat");
        d.Set(0, 3, -2, "glazing");
        d.Set(0, 3, -3, "glazing_slope");
        d.Set(0, 3, -1, "airframe_slope", Facing(new Int3(0, 1, 1)));
        d.Set(1, 2, -2, "glazing_plate", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));
        d.Set(-1, 2, -2, "glazing_plate", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));

        // Wing guns at the leading edge, each with a rack of AP behind it, and the bombs hung under the wings.
        foreach (int x in new[] { -3, 3 })
        {
            d.Set(x, 1, -6, "breech_40ac");
            d.Set(x, 1, -7, "barrel");
            d.Set(x, 1, -5, "ammo_ap");
            d.Set(x * 5 / 3, 0, -2, "bomb_rack");
        }

        // Ailerons on the outer trailing edge, then the rest of the wing. The wing sits far enough forward
        // that it lifts just behind the center of mass: stable, but light enough on the nose to pitch.
        d.Fill(7, 10, 1, 1, -2, -2, "control_surface");
        d.Fill(-10, -7, 1, 1, -2, -2, "control_surface");
        d.Fill(2, 10, 1, 1, -6, -2, "wing");
        d.Fill(-10, -2, 1, 1, -6, -2, "wing");

        // Fuselage and tail boom.
        d.Fill(-1, 1, 1, 1, -6, 1, "airframe");
        d.Fill(-1, 1, 2, 2, -6, -4, "airframe");
        d.Set(0, 2, -3, "airframe");
        d.Fill(0, 0, 2, 2, -1, 1, "airframe");
        d.Fill(0, 0, 1, 1, 2, 7, "airframe");

        // Tail: stabilizer and elevators, and a fin with a rudder.
        d.Fill(1, 4, 1, 1, 6, 6, "wing");
        d.Fill(-4, -1, 1, 1, 6, 6, "wing");
        d.Fill(1, 4, 1, 1, 7, 7, "control_surface");
        d.Fill(-4, -1, 1, 1, 7, 7, "control_surface");
        int upright = Orientations.RotateAroundZ(0, 1);
        d.Fill(0, 0, 2, 3, 6, 6, "wing", upright);
        d.Fill(0, 0, 2, 3, 7, 7, "control_surface", upright);

        // Tricycle landing gear: a steering nose wheel, and main wheels just behind the center of mass so
        // the elevators can lift the nose for takeoff.
        d.Set(0, 0, -6, "wheel_steer");
        d.Set(-2, 0, -3, "wheel");
        d.Set(2, 0, -3, "wheel");
        d.Set(0, 3, 0, "antenna");
        return d.Build("Kestrel (fighter-bomber)", "sky");
    }

    /// <summary>
    /// Jet fighter: two jets in the tail and no propeller, swept wings, a pointed nose, a glazed canopy, an
    /// autocannon in each wing root and a radar-guided missile under each wing. Open the throttle all the way
    /// for the afterburners.
    /// </summary>
    public static Blueprint Falcon()
    {
        var d = new Draft();
        // Cockpit: pilot seat under a canopy, with a faired spine behind it.
        d.Set(0, 2, -5, "pilot_seat");
        d.Set(0, 3, -5, "glazing");
        d.Set(0, 3, -6, "glazing_slope");
        d.Set(0, 2, -6, "airframe_slope");
        d.Set(0, 3, -4, "airframe_slope", Facing(new Int3(0, 1, 1)));
        d.Set(1, 2, -5, "glazing_plate", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));
        d.Set(-1, 2, -5, "glazing_plate", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));

        // Fuel, and the two jets side by side in the tail.
        d.Set(-1, 1, -2, "fuel_tank");
        d.Set(1, 1, -2, "fuel_tank");
        d.Set(0, 1, -1, "fuel_tank");
        foreach (int x in new[] { -1, 1 }) d.Set(x, 1, JetZ, "jet_engine");

        // Wing-root cannon with their ammo, and a radar missile under each wing.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(2 * s, 1, -4, "breech_40ac");
            d.Set(2 * s, 1, -5, "barrel");
            d.Set(2 * s, 1, -3, "ammo_ap");
            int x = 5 * s;
            d.Set(x, 0, 0, "missile_rail");
            d.Set(x, 0, -1, "missile_motor");
            d.Set(x, 0, -2, "missile_fins");
            d.Set(x, 0, -3, "missile_warhead");
            d.Set(x, 0, -4, "seeker_radar");
        }

        // Swept wings: the leading edge steps back half a cell for every cell out, ailerons on the outer trailing edge.
        foreach (int s in new[] { -1, 1 })
            for (int i = 0; i <= WingCells; i++)
            {
                int x = s * (2 + i);
                int lead = WingLead + i / 2, trail = WingTrail + i / 3;
                if (i >= 4) d.Set(x, 1, trail, "control_surface");
                for (int z = lead; z <= trail; z++) d.Fill(x, x, 1, 1, z, z, "wing");
            }

        // Strakes: the wing roots run forward along the nose, for more lift at low speed.
        foreach (int s in new[] { -1, 1 })
        {
            d.Fill(3 * s, 3 * s, 1, 1, -6, -5, "wing");
            d.Set(4 * s, 1, -5, "wing");
        }

        // Fuselage: a rounded nose, the body, a spine, and the tail cone between the jets.
        d.Set(0, 1, -9, "airframe_octagon");
        d.Set(0, 1, -8, "airframe_octagon");
        foreach (int s in new[] { -1, 1 }) d.Set(s, 1, -7, "airframe_round", Facing(new Int3(s, 0, -1)));
        d.Fill(-1, 1, 1, 1, -7, JetZ - 1, "airframe");
        d.Fill(0, 0, 2, 2, -3, 1, "airframe");
        d.Fill(0, 0, 1, 1, JetZ, 6, "airframe");

        // Tail: stabilizers with elevators, and a fin with a rudder.
        d.Fill(1, 3, 1, 1, 5, 5, "wing");
        d.Fill(-3, -1, 1, 1, 5, 5, "wing");
        d.Fill(1, 3, 1, 1, 6, 6, "control_surface");
        d.Fill(-3, -1, 1, 1, 6, 6, "control_surface");
        int upright = Orientations.RotateAroundZ(0, 1);
        d.Fill(0, 0, 2, 3, 5, 5, "wing", upright);
        d.Fill(0, 0, 2, 3, 6, 6, "control_surface", upright);

        // Landing gear: a steering nose wheel, and the main wheels under the wing roots.
        d.Set(0, 0, -6, "wheel_steer");
        d.Set(-2, 0, MainGearZ, "wheel");
        d.Set(2, 0, MainGearZ, "wheel");

        d.Set(0, 3, 0, "antenna");
        d.Set(0, 2, 2, "countermeasures");
        return d.Build("Falcon (jet fighter)", "sky");
    }

    /// <summary>
    /// The Falcon's airframe on two jets built from parts: half again the push, an afterburner that pays off past
    /// the speed of sound, and less fuel per kilometre at cruise. An interceptor: fast to climb and fast to catch up.
    /// </summary>
    public static Blueprint Lancer()
    {
        var d = new Draft();
        // Cockpit: pilot seat under a canopy, with a faired spine behind it.
        d.Set(0, 2, -5, "pilot_seat");
        d.Set(0, 3, -5, "glazing");
        d.Set(0, 3, -6, "glazing_slope");
        d.Set(0, 2, -6, "airframe_slope");
        d.Set(0, 3, -4, "airframe_slope", Facing(new Int3(0, 1, 1)));
        d.Set(1, 2, -5, "glazing_plate", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));
        d.Set(-1, 2, -5, "glazing_plate", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));

        // Fuel down the middle, set back to balance the heavier engines' compressors forward.
        d.Set(0, 1, 0, "fuel_tank");
        d.Set(0, 1, 1, "fuel_tank");
        d.Set(0, 1, 2, "fuel_tank");

        // Wing-root cannon with their ammo, and a radar missile under each wing.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(2 * s, 1, -4, "breech_40ac");
            d.Set(2 * s, 1, -5, "barrel");
            d.Set(2 * s, 1, -3, "ammo_ap");
            int x = 5 * s;
            d.Set(x, 0, 0, "missile_rail");
            d.Set(x, 0, -1, "missile_motor");
            d.Set(x, 0, -2, "missile_fins");
            d.Set(x, 0, -3, "missile_warhead");
            d.Set(x, 0, -4, "seeker_radar");
        }

        // Swept wings: the leading edge steps back half a cell for every cell out, ailerons on the outer trailing edge.
        foreach (int s in new[] { -1, 1 })
            for (int i = 0; i <= WingCells; i++)
            {
                int x = s * (2 + i);
                int lead = WingLead + i / 2, trail = WingTrail + i / 3;
                if (i >= 4) d.Set(x, 1, trail, "control_surface");
                for (int z = lead; z <= trail; z++) d.Fill(x, x, 1, 1, z, z, "wing");
            }

        // Strakes: the wing roots run forward along the nose, for more lift at low speed.
        foreach (int s in new[] { -1, 1 })
        {
            d.Fill(3 * s, 3 * s, 1, 1, -6, -5, "wing");
            d.Set(4 * s, 1, -5, "wing");
        }

        // Fuselage: a rounded nose, the body, a spine, and the tail cone between the jets.
        d.Set(0, 1, -9, "airframe_octagon");
        d.Set(0, 1, -8, "airframe_octagon");
        foreach (int s in new[] { -1, 1 }) d.Set(s, 1, -7, "airframe_round", Facing(new Int3(s, 0, -1)));
        d.Fill(-1, 1, 1, 1, -7, JetZ - 1, "airframe");
        d.Fill(0, 0, 2, 2, -3, 1, "airframe");
        d.Fill(0, 0, 1, 1, JetZ, 6, "airframe");

        // Tail: stabilizers with elevators, and a fin with a rudder.
        d.Fill(1, 3, 1, 1, 5, 5, "wing");
        d.Fill(-3, -1, 1, 1, 5, 5, "wing");
        d.Fill(1, 3, 1, 1, 6, 6, "control_surface");
        d.Fill(-3, -1, 1, 1, 6, 6, "control_surface");
        int upright = Orientations.RotateAroundZ(0, 1);
        d.Fill(0, 0, 2, 3, 5, 5, "wing", upright);
        d.Fill(0, 0, 2, 3, 6, 6, "control_surface", upright);

        // Landing gear: a steering nose wheel, and the main wheels under the wing roots.
        d.Set(0, 0, -6, "wheel_steer");
        d.Set(-2, 0, MainGearZ, "wheel");
        d.Set(2, 0, MainGearZ, "wheel");

        d.Set(0, 3, 0, "antenna");
        d.Set(0, 2, 2, "countermeasures");

        // Two jets built from parts, one down each side of the fuselage (set last, over the airframe): a ramp
        // intake for speed, three compressor stages, the core, two turbines, an afterburner and a variable nozzle.
        foreach (int x in new[] { -1, 1 })
        {
            d.Set(x, 1, -4, "jet_intake_ramp");
            for (int z = -3; z <= -1; z++) d.Set(x, 1, z, "jet_compressor");
            d.Set(x, 1, 0, "jet_core");
            d.Set(x, 1, 1, "jet_turbine");
            d.Set(x, 1, 2, "jet_turbine");
            d.Set(x, 1, 3, "jet_afterburner");
            d.Set(x, 1, 4, "jet_nozzle_variable");
            // The intakes are cheeks beside the cockpit: nothing in front of them but air.
            for (int z = -7; z <= -5; z++) d.Remove(x, 1, z);
        }

        return d.Build("Lancer (interceptor)", "sky");
    }

    // Tuned with the flight harness so the wing lifts just behind the center of mass.
    private const int JetZ = 3, WingLead = -4, WingTrail = 0, MainGearZ = 0, WingCells = 7;

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

        // Stub wings with a missile rail each: motor, fins, warhead, and a seeker on the nose: optical on the right
        // (anything, close in), top attack on the left (armor, from a few hundred meters off, onto the roof).
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x, 1, 0, "airframe");
            int side = Math.Sign(x) * 3;
            d.Set(side, 1, 0, "missile_rail");
            d.Set(side, 1, -1, "missile_motor");
            d.Set(side, 1, -2, "missile_fins");
            d.Set(side, 1, -3, "missile_warhead");
            d.Set(side, 1, -4, x < 0 ? "seeker_top" : "seeker_optical");
        }

        // A glazed nose for the gunner and pilot, then the cabin, tail boom, and fins at the tail.
        d.Fill(-1, 1, 3, 3, -3, -2, "glazing");
        d.Fill(-1, -1, 2, 2, -3, -3, "glazing");
        d.Fill(1, 1, 2, 2, -3, -3, "glazing");
        d.Fill(-1, 1, 1, 3, -3, 2, "airframe");
        d.Fill(0, 0, 2, 2, 3, 8, "airframe");
        int upright = Orientations.RotateAroundZ(0, 1);
        d.Set(0, 3, 8, "wing", upright);
        d.Set(1, 2, 8, "wing");
        d.Set(-1, 2, 8, "wing");

        // Skids.
        d.Fill(-1, -1, 0, 0, -2, 2, "frame");
        d.Fill(1, 1, 0, 0, -2, 2, "frame");
        return d.Build("Hornet (helicopter)", "olive");
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
        return d.Build("Wasp (drone)", "urban");
    }

    /// <summary>
    /// Airship: a long envelope of gas cells over a gondola with the crew, two propellers on outriggers, a
    /// 37 mm gun in the nose, eight bombs, four optical missiles on the gondola's flanks, and a twin 40 mm
    /// flak turret on top of the envelope.
    /// </summary>
    public static Blueprint Zephyr()
    {
        var d = new Draft();
        // Gondola: crew, engine and gun.
        d.Set(0, 1, -3, "driver_seat");
        d.Set(0, 1, -2, "gunner_seat");
        // A flying command post: command console and radar in the gondola.
        d.Set(0, 1, 0, "command_console");
        d.Set(0, 0, 1, "radar", Orient(new Int3(0, 1, 0), new Int3(0, 0, -1)));
        d.Set(0, 1, 2, "engine_large");
        d.Set(-1, 1, 2, "fuel_tank");
        d.Set(1, 1, 2, "fuel_tank");
        d.Set(0, 1, -4, "breech_37");
        for (int z = -5; z >= -7; z--) d.Set(0, 1, z, "barrel");
        d.Set(-1, 1, -4, "ammo_he");
        d.Set(1, 1, -4, "ammo_ap");
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -3, -1, 1, 3 })
            d.Set(x, 0, z, "bomb_rack");

        // Missiles along the gondola's flanks, pointing forward.
        foreach (int x in new[] { -2, 2 })
        foreach (int y in new[] { 1, 2 })
        {
            d.Set(x, y, -1, "missile_rail");
            d.Set(x, y, -2, "missile_motor");
            d.Set(x, y, -3, "missile_fins");
            d.Set(x, y, -4, "missile_warhead");
            d.Set(x, y, -5, "seeker_optical");
        }
        d.Fill(-1, 1, 1, 2, -4, 4, "airframe");
        d.Fill(0, 0, 3, 3, -3, 3, "frame"); // struts up to the envelope

        // Outriggers with a propeller on each.
        foreach (int x in new[] { -1, 1 })
        {
            for (int i = 2; i <= 4; i++) d.Set(x * i, 1, 2, "frame");
            d.Set(x * 4, 1, 1, "air_propeller");
        }

        // The envelope: a long rounded body of gas cells.
        const int half = 13;
        for (int z = -half; z <= half; z++)
        {
            float r = 3.05f * MathF.Sqrt(1f - z * z / (float)((half + 1) * (half + 1)));
            for (int x = -3; x <= 3; x++)
            for (int y = 3; y <= 9; y++)
                if (x * x + (y - 6) * (y - 6) <= r * r + 0.3f) d.Set(x, y, z, "gas_cell");
        }

        // A flak turret on top of the envelope: twin 40 mm autocannons, a gunner, and racks.
        d.Set(0, 10, -2, "turret_ring");
        d.Set(0, 11, -2, "frame");
        d.Set(0, 11, -1, "gunner_seat");
        foreach (int x in new[] { -1, 1 })
        {
            d.Set(x, 11, -2, "breech_40ac");
            d.Set(x, 11, -3, "barrel");
            d.Set(x, 11, -4, "barrel");
            d.Set(2 * x, 11, -2, "ammo_flak");
            d.Set(x, 11, -1, "ammo_he");
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

    /// <summary>
    /// A 300 m battleship, built from big blocks: a hull of hull plates and belt armor split into
    /// compartments by a bulkhead every 20 m, a bed of lead along the keel, twelve turbines on four great
    /// screws, and a bridge tower. She bristles with guns: four triple 406 mm turrets (the inner two raised
    /// to fire over the outer ones), twelve twin 203 mm turrets down her sides, twenty-four twin 40 mm flak
    /// mounts along the deck edges and on the bridge, eight torpedo tubes at the waterline, and eight radar
    /// missiles on rails beside the bridge.
    /// </summary>
    public static Blueprint Colossus()
    {
        var d = new Draft();
        const int half = 24, deck = 25, bow = -300, stern = 299, taper = 64;
        int sideWall = Orientations.RotateAroundZ(0, 1), endWall = Orientations.RotateAroundX(0, 1), beltTurn = Orientations.RotateAroundY(0, 1);

        // The bow narrows to a point over its first 32 m; the rest of her is full beam.
        int HalfAt(int z) => z >= bow + taper ? half : (int)MathF.Round(3 + (half - 3) * MathF.Pow((z - bow) / (float)taper, 0.7f));

        // Main battery: triple 406 mm turrets. f is the way the guns face along the ship (-1 forward, +1 aft);
        // a raised turret stands on an armored barbette so it can fire over the one in front.
        void MainTurret(int zt, int f, int lift)
        {
            int ring = deck + lift, y0 = ring + 1, front = zt + f * 2, back = zt - f * 6;
            int ob = f < 0 ? 0 : Pointing(Int3.Back);
            d.Set(0, ring, zt, "turret_ring");
            foreach (int sx in new[] { -5, 0, 5 })
            {
                d.Set(sx, y0 + 2, zt, "breech_406", ob);
                for (int k = 0; k < 20; k++) d.Set(sx, y0 + 2, zt + f * (2 + k), "heavy_barrel");
                int mx = sx > 0 ? sx : sx - 1, mz = f < 0 ? zt + 2 : zt - 3;
                d.Set(mx, y0, mz, "magazine_ap");
                d.Set(mx, y0 + 2, mz, "magazine_he");
            }

            foreach (int x in new[] { -3, -2, 2, 3 }) d.Set(x, y0 + 2, zt, "loader");
            d.Set(0, y0, zt - f * 5, "gunner_seat");
            d.Fill(-8, 8, y0, y0, Math.Min(front, back), Math.Max(front, back), "armor_light");
            d.Fill(-8, -8, y0 + 1, y0 + 5, Math.Min(front, back), Math.Max(front, back), "armor_heavy");
            d.Fill(8, 8, y0 + 1, y0 + 5, Math.Min(front, back), Math.Max(front, back), "armor_heavy");
            d.Fill(-7, 7, y0 + 1, y0 + 5, front, front, "armor_heavy");
            d.Fill(-7, 7, y0 + 1, y0 + 5, back, back, "armor_heavy");
            d.Fill(-8, 8, y0 + 6, y0 + 6, Math.Min(front, back), Math.Max(front, back), "armor_heavy");
            if (lift > 0)
            {
                // The barbette: armored walls up from the deck, capped round the ring.
                d.Fill(-7, 7, ring, ring, zt - 6, zt + 6, "armor_heavy");
                d.Fill(-7, -7, deck + 1, ring - 1, zt - 6, zt + 6, "armor_heavy");
                d.Fill(7, 7, deck + 1, ring - 1, zt - 6, zt + 6, "armor_heavy");
                d.Fill(-6, 6, deck + 1, ring - 1, zt - 6, zt - 6, "armor_heavy");
                d.Fill(-6, 6, deck + 1, ring - 1, zt + 6, zt + 6, "armor_heavy");
            }
        }

        MainTurret(-240, -1, 0);
        MainTurret(-198, -1, 6);
        MainTurret(198, 1, 6);
        MainTurret(240, 1, 0);

        // Secondary battery: twin 203 mm turrets down each side, fed from magazines below deck through the ring.
        void Secondary(int x0, int z0, int sx)
        {
            int o = Pointing(new Int3(sx, 0, 0)), y = deck + 1;
            d.Set(x0, deck, z0, "turret_ring");
            d.Set(x0, deck - 1, z0, "ammo_feed");
            d.Set(x0 + 1, deck - 2, z0, "magazine_ap");
            d.Set(x0 - 2, deck - 2, z0 - 1, "magazine_he");
            d.Set(x0, y, z0, "ammo_feed");
            foreach (int dz in new[] { -1, 1 })
            {
                d.Set(x0, y, z0 + dz, "breech_203", o);
                for (int k = 1; k <= 7; k++) d.Set(x0 + sx * k, y, z0 + dz, "barrel");
                d.Set(x0 - sx, y, z0 + dz, "loader");
            }

            d.Set(x0 - sx, y, z0, "gunner_seat");
            d.Fill(x0 - 2, x0 + 2, y, y, z0 - 2, z0 - 2, "armor_heavy");
            d.Fill(x0 - 2, x0 + 2, y, y, z0 + 2, z0 + 2, "armor_heavy");
            d.Fill(x0 - 2 * sx, x0 - 2 * sx, y, y, z0 - 1, z0 + 1, "armor_heavy");
            d.Set(x0 + sx, y, z0, "armor_heavy");
            d.Fill(Math.Min(x0, x0 - 2 * sx), Math.Max(x0, x0 - 2 * sx), y + 1, y + 1, z0 - 2, z0 + 2, "armor_heavy");
        }

        foreach (int z in new[] { -150, -90, -30, 30, 90, 150 })
        {
            Secondary(16, z, 1);
            Secondary(-16, z, -1);
        }

        // Flak: twin 40 mm autocannons on small turrets, each with its own gunner and racks.
        void Flak(int x0, int ring, int z0)
        {
            d.Set(x0, ring, z0, "turret_ring");
            int y = ring + 1;
            d.Set(x0, y, z0, "frame");
            d.Set(x0, y, z0 + 1, "gunner_seat");
            foreach (int x in new[] { -1, 1 })
            {
                d.Set(x0 + x, y, z0, "breech_40ac");
                d.Set(x0 + x, y, z0 - 1, "barrel");
                d.Set(x0 + x, y, z0 - 2, "barrel");
                d.Set(x0 + 2 * x, y, z0, "ammo_flak");
                d.Set(x0 + x, y, z0 + 1, "ammo_he");
            }
        }

        foreach (int z in new[] { -270, -220, -175, -120, -60, 0, 60, 120, 175, 220, 270 })
        {
            int x = Math.Min(21, HalfAt(z - 2) - 3);
            Flak(x, deck, z);
            Flak(-x, deck, z);
        }

        Flak(-3, deck + 20, -100);
        Flak(3, deck + 20, -100);

        // Torpedo tubes in the belt at the waterline, four a side, each with two racks behind it.
        foreach (int z in new[] { -50, -44, 40, 46 })
        foreach (int sx in new[] { -1, 1 })
        {
            d.Set(sx * half, 12, z, "torpedo_tube", Pointing(new Int3(sx, 0, 0)));
            d.Set(sx * (half - 1), 12, z, "ammo_torpedo");
            d.Set(sx * (half - 2), 12, z, "ammo_torpedo");
        }

        // Radar missiles on rails either side of the bridge, pointing forward.
        foreach (int sx in new[] { -1, 1 })
        foreach (int y in new[] { deck + 3, deck + 5, deck + 7, deck + 9 })
        {
            int x = sx * 7;
            d.Set(x, y, -96, "missile_rail");
            d.Set(x, y, -97, "missile_motor");
            d.Set(x, y, -98, "missile_fins");
            d.Set(x, y, -99, "missile_warhead");
            d.Set(x, y, -100, "seeker_radar");
        }

        // A funnel aft of the bridge, its corners rounded.
        for (int y = deck + 1; y <= deck + 16; y++)
            foreach (int sx in new[] { -1, 1 })
            foreach ((int z, int sz) in new[] { (-80, -1), (-72, 1) })
                d.Set(3 * sx, y, z, "armor_light_round", Facing(new Int3(sx, 0, sz)));
        d.Fill(-3, 3, deck + 1, deck + 16, -80, -80, "armor_light");
        d.Fill(-3, 3, deck + 1, deck + 16, -72, -72, "armor_light");
        d.Fill(-3, -3, deck + 1, deck + 16, -79, -73, "armor_light");
        d.Fill(3, 3, deck + 1, deck + 16, -79, -73, "armor_light");
        d.Fill(-4, 4, deck + 17, deck + 17, -81, -80, "armor_heavy");
        d.Fill(-4, 4, deck + 17, deck + 17, -72, -71, "armor_heavy");
        d.Fill(-4, -3, deck + 17, deck + 17, -79, -73, "armor_heavy");
        d.Fill(3, 4, deck + 17, deck + 17, -79, -73, "armor_heavy");

        // Bridge tower amidships-forward, stepped in two tiers, with the helm and the commander up top.
        d.Set(0, deck + 19, -107, "driver_seat");
        d.Set(1, deck + 19, -107, "commander_seat");
        // The flagship: a command console on the bridge and a search radar on the tower roof.
        d.Set(-1, deck + 19, -106, "command_console");
        d.Set(0, deck + 21, -101, "radar");
        void Box(int x, int y0, int y1, int z0, int z1)
        {
            d.Tile(-x, x, y0, y1, z0, z0, "hull_plate", endWall, "armor_light");
            d.Tile(-x, x, y0, y1, z1, z1, "hull_plate", endWall, "armor_light");
            d.Tile(-x, -x, y0, y1, z0 + 1, z1 - 1, "hull_plate", sideWall, "armor_light");
            d.Tile(x, x, y0, y1, z0 + 1, z1 - 1, "hull_plate", sideWall, "armor_light");
            d.Tile(-x + 1, x - 1, y1, y1, z0 + 1, z1 - 1, "hull_plate", 0, "armor_light");
        }

        // A band of windows round the upper tier, then the tiers, a lattice mast on top and the mainmast aft.
        d.Fill(-3, 3, deck + 18, deck + 19, -108, -108, "glazing");
        d.Fill(-3, 3, deck + 18, deck + 19, -94, -94, "glazing");
        foreach (int sx in new[] { -4, 4 }) d.Fill(sx, sx, deck + 18, deck + 19, -107, -95, "glazing");
        Box(6, deck + 1, deck + 10, -110, -90);
        Box(4, deck + 11, deck + 20, -108, -94);
        d.SetIfFree(0, deck + 26, -95, "mast_lattice");
        d.SetIfFree(0, deck + 6, -60, "mast_lattice");

        // Boats amidships, life rafts and bollards along the deck edges.
        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int z in new[] { -56, -44, -32 }) d.SetIfFree(sx * 9, deck + 2, z, "ships_boat");
            for (int z = -200; z <= 200; z += 25)
            {
                // (Only on bare deck plating.)
                bool OnDeck(int x, int zz) => d.IdAt(x, deck, zz) is "armor_light" or "hull_plate" or "armor_heavy";
                if (OnDeck(sx * 22, z)) d.SetIfFree(sx * 22, deck + 1, z, "bollard");
                foreach (int dz in new[] { 6, 8 })
                    if (OnDeck(sx * 21, z + dz)) d.SetIfFree(sx * 21, deck + 1, z + dz, "liferaft");
            }
        }

        // Machinery aft of amidships: twelve turbines and their fuel.
        foreach (int x in new[] { -14, -8, 7, 13 })
        foreach (int z in new[] { 150, 160, 170 })
            d.Set(x, 3, z, "turbine");
        foreach (int x in new[] { -20, 19 })
        foreach (int z in new[] { 150, 158, 166, 174 })
            d.Set(x, 3, z, "fuel_bunker");

        // Pumps in every compartment, standing on the ballast.
        for (int z = bow + 22; z < stern; z += 40)
        {
            d.Set(-2, 3, z, "bilge_pump");
            d.Set(2, 3, z, "bilge_pump");
        }

        // Lead along the keel, so she sits deep and steady.
        for (int x = 1; x <= 19; x += 2)
        for (int z = bow + 2; z <= stern - 3; z += 4)
        {
            if (x + 3 > HalfAt(z)) continue;
            d.Set(x, 1, z, "ballast_block");
            d.Set(-x - 1, 1, z, "ballast_block");
        }

        // Four great screws under the stern, a great rudder behind each inner pair.
        foreach (int x in new[] { -16, -8, 8, 16 }) d.Set(x, 6, stern + 1, "great_screw");
        foreach (int x in new[] { -8, 8 }) d.Set(x, 6, stern + 3, "great_rudder");

        // The hull amidships and aft: keel and deck plates, belt armor along the waterline band, plates
        // above and below it, and a square stern.
        int mid = bow + taper;
        d.Tile(-half, half, 0, 0, mid, stern, "hull_plate", 0, "armor_light");
        d.Tile(-half, half, deck, deck, mid, stern, "hull_plate", 0, "armor_light");
        foreach (int sgn in new[] { -1, 1 })
        {
            int outer = sgn * half, inner = sgn * (half - 1);
            d.Tile(Math.Min(outer, inner), Math.Max(outer, inner), 2, 17, mid, stern, "belt_armor", beltTurn, "armor_heavy");
            d.Fill(outer, outer, 1, 1, mid, stern, "armor_light");
            d.Tile(outer, outer, 18, deck - 1, mid, stern, "hull_plate", sideWall, "armor_light");
        }

        d.Tile(-half + 1, half - 1, 1, deck - 1, stern, stern, "hull_plate", endWall, "armor_light");

        // The bow: each row narrower than the one behind it, walled in heavy plate, with slopes smoothing the
        // steps so the sides read as one sweep.
        for (int z = bow; z < mid; z++)
        {
            int w = HalfAt(z), next = HalfAt(z + 1);
            d.Fill(-w, w, 0, 0, z, z, "armor_light");
            d.Fill(-w, w, deck, deck, z, z, "armor_light");
            foreach (int sgn in new[] { -1, 1 })
            {
                int prev = z == bow ? 0 : HalfAt(z - 1);
                for (int x = Math.Min(w, prev + 1); x <= w; x++) d.Fill(sgn * x, sgn * x, 1, deck - 1, z, z, "armor_heavy");
                for (int x = w + 1; x <= next; x++)
                for (int y = 0; y <= deck; y++)
                    d.Slope(sgn * x, y, z, "armor_heavy_slope", new Int3(sgn, 0, -1));
            }
        }

        d.Fill(-HalfAt(bow), HalfAt(bow), 1, deck - 1, bow, bow, "armor_heavy");

        // Watertight bulkheads every 20 m.
        for (int z = bow + 40; z < stern; z += 40)
        {
            int w = HalfAt(z) - 1;
            if (z >= mid) d.Tile(-w, w, 1, deck - 1, z, z, "hull_plate", endWall, "armor_light");
            else d.Fill(-w, w, 1, deck - 1, z, z, "armor_light");
        }

        return d.Build("Colossus (battleship)", "dazzle");
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
    /// <summary>
    /// A command post: a heavy concrete-and-steel hut with a command console inside, slit windows, a search
    /// radar and an antenna on the roof. Stand next to it and press M to command your units.
    /// </summary>
    /// <summary>Example buildings: stationary ground units with crews and guns.</summary>
    public static IReadOnlyList<Blueprint> Buildings => new[]
        { Pillbox(), FlakNest(), Outpost(), SiegeFortress(), CoastalCitadel(), FlakTower(), MortarBattery() };

    // ------------------------------------------------------------------ ship helpers

    /// <summary>
    /// A ship's hull shell of <paramref name="mat"/>, shaped like a real one: a flat bottom with chamfered bilges,
    /// straight sides <paramref name="halfBeam"/> cells out from the centerline that close in toward the bow at one
    /// cell for every two, to a sharp stem (long half-slope pieces standing on edge, so seen from above the bow is a
    /// clean V), a transom stern, and the deck on top at <paramref name="deck"/>. Forward of
    /// <paramref name="focEnd"/> the sides and deck stand <paramref name="focRise"/> cells higher (a raised
    /// forecastle, closed by a wall at its after end). Never replaces what's already placed, so put guns, rings and
    /// machinery first. Bulkheads and the inside are the design's. Returns the hull's half-width at each station.
    /// </summary>
    private static Func<int, int> ShipHull(Draft d, int bow, int stern, int halfBeam, int deck, string mat,
        int focEnd = int.MinValue, int focRise = 0)
    {
        int Hw(int z) => z < bow ? -1 : Math.Min(halfBeam, (z - bow + 1) / 2);
        int Top(int z) => z < focEnd ? deck + focRise : deck;
        for (int z = bow; z <= stern; z++)
        {
            int hw = Hw(z), prev = Hw(z - 1), top = Top(z);
            bool widens = hw > prev, secondHalf = hw == prev && prev > Hw(z - 2) && z - 2 >= bow - 1;
            if (hw >= 2)
            {
                d.Fill(-hw + 1, hw - 1, 0, 0, z, z, mat);
                d.Fill(-hw, -hw, 0, 0, z, z, mat + "_slope", Facing(new Int3(-1, -1, 0)));
                d.Fill(hw, hw, 0, 0, z, z, mat + "_slope", Facing(new Int3(1, -1, 0)));
            }
            else d.Fill(-hw, hw, 0, 0, z, z, mat);

            for (int y = 1; y <= top; y++)
            {
                if (hw == 0)
                {
                    d.Fill(0, 0, y, y, z, z, mat);
                    continue;
                }

                foreach (int s in new[] { -1, 1 })
                {
                    // Where the side is closing in toward the bow, a long half slope across two stations: its low
                    // half on the station where the hull widens, its high half on the one behind.
                    int x = s * hw;
                    if (widens) d.Fill(x, x, y, y, z, z, mat + "_shallow", BowFacing(s));
                    else if (secondHalf) d.Fill(x, x, y, y, z, z, mat + "_shallow_high", BowFacing(s));
                    else d.Fill(x, x, y, y, z, z, mat);
                }

                // The deck (and the forecastle's after wall), and the transom.
                if (y == top || z == stern || (focRise > 0 && z == focEnd - 1 && y > deck)) d.Fill(-hw + 1, hw - 1, y, y, z, z, mat);
            }
        }

        return Hw;
    }

    /// <summary>
    /// The orientation that stands a long half slope on edge along a bow's side: rising outward toward side
    /// <paramref name="s"/> (+1 starboard, −1 port) as it runs aft.
    /// </summary>
    private static int BowFacing(int s)
    {
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.Apply(o, Int3.Up) == new Int3(s, 0, 0) && Orientations.Apply(o, new Int3(0, 0, 1)) == new Int3(0, 0, 1)) return o;
        throw new System.InvalidOperationException("No bow orientation.");
    }

    /// <summary>Guard rails along both deck edges from <paramref name="z0"/> to <paramref name="z1"/>, one row above the deck at <paramref name="deck"/>, skipping cells already used.</summary>
    private static void DeckRails(Draft d, Func<int, int> hw, int z0, int z1, int deck)
    {
        for (int z = z0; z <= z1; z++)
            foreach (int s in new[] { -1, 1 })
            {
                int x = s * hw(z);
                d.Fill(x, x, deck + 1, deck + 1, z, z, "railing", Orient(new Int3(0, -1, 0), new Int3(-s, 0, 0)));
            }
    }

    // ------------------------------------------------------------------ fortification helpers

    /// <summary>Chamfers the outer edge of a box's top layer: slopes along the sides, corner pieces at the corners.</summary>
    private static void ChamferEdge(Draft d, int x0, int x1, int y, int z0, int z1, string mat = "concrete")
    {
        for (int x = x0 + 1; x < x1; x++)
        {
            d.Slope(x, y, z0, mat + "_slope", new Int3(0, 1, -1));
            d.Slope(x, y, z1, mat + "_slope", new Int3(0, 1, 1));
        }

        for (int z = z0 + 1; z < z1; z++)
        {
            d.Slope(x0, y, z, mat + "_slope", new Int3(-1, 1, 0));
            d.Slope(x1, y, z, mat + "_slope", new Int3(1, 1, 0));
        }

        foreach (int x in new[] { x0, x1 })
        foreach (int z in new[] { z0, z1 })
            d.Set(x, y, z, mat + "_corner", CornerFacing(new Int3(x == x0 ? -1 : 1, 1, 0), new Int3(0, 1, z == z0 ? -1 : 1)));
    }

    /// <summary>
    /// Earth banked up round the box x0..x1 × z0..z1: <paramref name="h"/> cells high against its walls, sloping away
    /// at 45° with corner pieces where two banks meet, like a bunker half dug in. Never replaces what's there;
    /// <paramref name="skip"/> leaves gaps (doorways, embrasures, trenches).
    /// </summary>
    private static void Berm(Draft d, int x0, int x1, int z0, int z1, int h, Func<int, int, bool> skip = null)
    {
        for (int x = x0 - h; x <= x1 + h; x++)
        for (int z = z0 - h; z <= z1 + h; z++)
        {
            int dx = x < x0 ? x0 - x : x > x1 ? x - x1 : 0, dz = z < z0 ? z0 - z : z > z1 ? z - z1 : 0;
            int k = Math.Max(dx, dz);
            if (k == 0 || skip?.Invoke(x, z) == true) continue;
            int sx = x < x0 ? -1 : 1, sz = z < z0 ? -1 : 1;
            if (h - k >= 1) d.Fill(x, x, 1, h - k, z, z, "earth");
            int top = h - k + 1;
            if (dx == dz) d.Fill(x, x, top, top, z, z, "earth_corner", CornerFacing(new Int3(sx, 1, 0), new Int3(0, 1, sz)));
            else if (dx > dz) d.Fill(x, x, top, top, z, z, "earth_slope", Facing(new Int3(sx, 1, 0)));
            else d.Fill(x, x, top, top, z, z, "earth_slope", Facing(new Int3(0, 1, sz)));
        }
    }

    /// <summary>
    /// A guard tower centered on (x, z): four steel legs, a boarded platform 2.5 m square at <paramref name="floor"/>
    /// walled with sandbags, a corrugated roof on corner posts with a searchlight, and a ladder up the
    /// <paramref name="ladderSide"/> (+1 or -1 in z).
    /// </summary>
    private static void GuardTower(Draft d, int x, int z, int floor, int ladderSide)
    {
        foreach (int sx in new[] { -2, 2 })
        foreach (int sz in new[] { -2, 2 })
            for (int y = 1; y <= floor + 2; y++) d.Set(x + sx, y, z + sz, "post");
        d.Fill(x - 2, x + 2, floor, floor, z - 2, z + 2, "timber");
        for (int i = -1; i <= 1; i++)
        {
            d.Set(x + i, floor + 1, z - 2 * ladderSide, "sandbags");
            d.Set(x - 2, floor + 1, z + i, "sandbags");
            d.Set(x + 2, floor + 1, z + i, "sandbags");
            if (i != 0) d.Set(x + i, floor + 1, z + 2 * ladderSide, "sandbags");
        }

        d.Fill(x - 2, x + 2, floor + 3, floor + 3, z - 2, z + 2, "corrugated");
        d.Set(x, floor + 4, z, "searchlight");
        for (int y = 1; y <= floor + 1; y++) d.Set(x, y, z + 3 * ladderSide, "ladder", Orient(new Int3(0, -1, 0), new Int3(0, 0, -ladderSide)));
    }

    /// <summary>
    /// A solid parapet round the edge of a roof x0..x1 × z0..z1: a wall at <paramref name="y"/>, its top row sloped
    /// outward with corner pieces at the corners. <paramref name="notch"/> leaves gaps in the top row (embrasures
    /// for guns). Never replaces what's there.
    /// </summary>
    private static void Parapet(Draft d, int x0, int x1, int y, int z0, int z1, Func<int, int, bool> notch = null)
    {
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            bool edgeX = x == x0 || x == x1, edgeZ = z == z0 || z == z1;
            if (!edgeX && !edgeZ) continue;
            d.Fill(x, x, y, y, z, z, "concrete");
            if (notch?.Invoke(x, z) == true) continue;
            int sx = x == x0 ? -1 : 1, sz = z == z0 ? -1 : 1;
            if (edgeX && edgeZ) d.Fill(x, x, y + 1, y + 1, z, z, "concrete_corner", CornerFacing(new Int3(sx, 1, 0), new Int3(0, 1, sz)));
            else if (edgeX) d.Fill(x, x, y + 1, y + 1, z, z, "concrete_slope", Facing(new Int3(sx, 1, 0)));
            else d.Fill(x, x, y + 1, y + 1, z, z, "concrete_slope", Facing(new Int3(0, 1, sz)));
        }
    }

    /// <summary>A camouflage net 2.5 m square on four posts, its middle at (x, y, z), over whatever is below.</summary>
    private static void NetOnPosts(Draft d, int x, int y, int z)
    {
        d.Set(x, y, z, "camo_net");
        foreach (int sx in new[] { -2, 2 })
        foreach (int sz in new[] { -2, 2 })
            for (int yy = 1; yy < y; yy++) d.Fill(x + sx, x + sx, yy, yy, z + sz, z + sz, "post");
    }

    // ------------------------------------------------------------------ the village

    /// <summary>The village's buildings: houses, a barn and a chapel. Civilian structures, no guns.</summary>
    public static IReadOnlyList<Blueprint> Village => new[]
    {
        House("House (brick, two storeys)", 7, 9, 2, "brick", false),
        House("House (brick and timber)", 6, 8, 2, "brick", true),
        House("Cottage (brick)", 5, 7, 1, "brick", false),
        House("Cottage (timber)", 5, 6, 1, "timber", true),
        Barn(),
        Chapel(),
        GardenWall(12),
    };

    /// <summary>
    /// A gable roof over walls <paramref name="halfW"/> cells either side of the middle across and
    /// <paramref name="halfL"/> along, its eaves at height <paramref name="eaves"/>: tiled slopes on the long sides,
    /// overhanging the eaves by a cell, the gable ends filled with <paramref name="gable"/> right up under the tiles
    /// (flush, so each gable's edge runs straight up the roof line), tiles along the ridge.
    /// </summary>
    private static void GableRoof(Draft d, int halfW, int halfL, int eaves, string gable, string tiles = "roof_tile")
    {
        for (int k = 0; k <= halfW + 1; k++)
        {
            int y = eaves + 1 + k, left = -halfW - 1 + k, right = halfW + 1 - k;
            // (The end cells of each course are the gable's own material: a brick or timber verge up the edge.)
            if (left >= right)
            {
                foreach (int z in new[] { -halfL, halfL }) d.Fill(0, 0, y, y, z, z, gable + "_half");
                d.Fill(0, 0, y, y, -halfL, halfL, tiles + "_half");
                break;
            }

            foreach (int z in new[] { -halfL, halfL })
            {
                d.FillSlope(left, left, y, y, z, z, gable + "_slope", new Int3(-1, 1, 0));
                d.FillSlope(right, right, y, y, z, z, gable + "_slope", new Int3(1, 1, 0));
            }

            d.FillSlope(left, left, y, y, -halfL, halfL, tiles + "_slope", new Int3(-1, 1, 0));
            d.FillSlope(right, right, y, y, -halfL, halfL, tiles + "_slope", new Int3(1, 1, 0));
            if (right - left > 1)
                foreach (int z in new[] { -halfL, halfL })
                    d.Fill(left + 1, right - 1, y, y, z, z, gable);
        }
    }

    /// <summary>
    /// A house: a slab floor, walls of <paramref name="wall"/> three metres a storey, windows along the long sides,
    /// a door in the front (−z) gable end, an upper floor, a tiled gable roof, a chimney.
    /// </summary>
    public static Blueprint House(string name, int halfW, int halfL, int storeys, string wall, bool timberGable)
    {
        var d = new Draft();
        int h = storeys * 6;
        // Openings first, so the walls fill in round them.
        d.Set(0, 1, -halfL, "door");
        for (int s = 0; s < storeys; s++)
        {
            int y = s * 6 + 3;
            for (int z = -halfL + 3; z <= halfL - 4; z += 4)
                foreach (int x in new[] { -halfW, halfW })
                    d.Fill(x, x, y, y + 1, z, z + 1, "glazing");
            foreach (int x in new[] { -halfW + 2, halfW - 3 })
                d.Fill(x, x + 1, y, y + 1, halfL, halfL, "glazing");
            if (s > 0)
                foreach (int x in new[] { -halfW + 2, halfW - 3 })
                    d.Fill(x, x + 1, y, y + 1, -halfL, -halfL, "glazing");
        }

        d.Fill(-halfW, halfW, 0, 0, -halfL, halfL, "concrete");
        d.Fill(-halfW, halfW, 1, h, -halfL, -halfL, wall);
        d.Fill(-halfW, halfW, 1, h, halfL, halfL, wall);
        d.Fill(-halfW, -halfW, 1, h, -halfL + 1, halfL - 1, wall);
        d.Fill(halfW, halfW, 1, h, -halfL + 1, halfL - 1, wall);
        for (int s = 1; s < storeys; s++) d.Fill(-halfW + 1, halfW - 1, s * 6, s * 6, -halfL + 1, halfL - 1, "timber");
        d.Fill(-halfW + 1, halfW - 1, h, h, -halfL + 1, halfL - 1, "timber");
        GableRoof(d, halfW, halfL, h, timberGable ? "timber" : wall);
        // The chimney stack, up through the roof near the back.
        d.Fill(halfW - 3, halfW - 2, h + 1, h + halfW + 2, halfL - 3, halfL - 2, "brick");
        d.Set(-halfW + 1, h - 1, -halfL + 1, "lamp");
        return d.Build(name);
    }

    /// <summary>A timber barn: wide and tall, double doors at each end, a tiled roof.</summary>
    public static Blueprint Barn()
    {
        var d = new Draft();
        const int halfW = 8, halfL = 12, h = 9;
        foreach (int z in new[] { -halfL, halfL })
            foreach (int x in new[] { -2, 0 })
                d.Set(x, 1, z, "door");
        d.Fill(-halfW, halfW, 0, 0, -halfL, halfL, "concrete");
        d.Fill(-halfW, halfW, 1, h, -halfL, -halfL, "timber");
        d.Fill(-halfW, halfW, 1, h, halfL, halfL, "timber");
        d.Fill(-halfW, -halfW, 1, h, -halfL + 1, halfL - 1, "timber");
        d.Fill(halfW, halfW, 1, h, -halfL + 1, halfL - 1, "timber");
        // A brick plinth round the bottom.
        d.Fill(-halfW, -halfW, 1, 1, -halfL + 1, halfL - 1, "brick");
        d.Fill(halfW, halfW, 1, 1, -halfL + 1, halfL - 1, "brick");
        d.Fill(-halfW + 1, halfW - 1, h, h, -halfL + 1, halfL - 1, "timber");
        GableRoof(d, halfW, halfL, h, "timber");
        return d.Build("Barn (timber)");
    }

    /// <summary>A brick chapel: a tall nave with long windows, and a square tower at the front with a flat top and parapet.</summary>
    public static Blueprint Chapel()
    {
        var d = new Draft();
        const int halfW = 6, halfL = 11, h = 11;
        d.Set(0, 1, -halfL - 5, "door");
        for (int z = -halfL + 3; z <= halfL - 4; z += 5)
            foreach (int x in new[] { -halfW, halfW })
                d.Fill(x, x, 3, 8, z, z + 1, "glazing");
        d.Fill(-1, 0, 4, 8, halfL, halfL, "glazing");
        d.Fill(-halfW, halfW, 0, 0, -halfL - 5, halfL, "concrete");
        d.Fill(-halfW, halfW, 1, h, -halfL, -halfL, "brick");
        d.Fill(-halfW, halfW, 1, h, halfL, halfL, "brick");
        d.Fill(-halfW, -halfW, 1, h, -halfL + 1, halfL - 1, "brick");
        d.Fill(halfW, halfW, 1, h, -halfL + 1, halfL - 1, "brick");
        d.Fill(-halfW + 1, halfW - 1, h, h, -halfL + 1, halfL - 1, "timber");
        GableRoof(d, halfW, halfL, h, "brick");
        // The tower: 3 m square, 12 m tall, a belfry opening each side near the top, a parapet.
        const int t = 3, top = 24;
        foreach (int x in new[] { -1, 0 })
        {
            d.Fill(x, x, top - 4, top - 2, -halfL - 5, -halfL - 5, "glazing");
            d.Fill(x, x, top - 4, top - 2, -halfL - 5 + 2 * t - 1, -halfL - 5 + 2 * t - 1, "glazing");
        }

        d.Fill(-t, t - 1, 1, top, -halfL - 5, -halfL - 5, "brick");
        d.Fill(-t, t - 1, 1, top, -halfL - 5 + 2 * t - 1, -halfL - 5 + 2 * t - 1, "brick");
        d.Fill(-t, -t, 1, top, -halfL - 4, -halfL - 5 + 2 * t - 2, "brick");
        d.Fill(t - 1, t - 1, 1, top, -halfL - 4, -halfL - 5 + 2 * t - 2, "brick");
        d.Fill(-t + 1, t - 2, top, top, -halfL - 4, -halfL - 5 + 2 * t - 2, "timber");
        for (int x = -t; x <= t - 1; x += 2)
        {
            d.Set(x, top + 1, -halfL - 5, "brick");
            d.Set(x, top + 1, -halfL - 5 + 2 * t - 1, "brick");
        }

        for (int z = -halfL - 5; z <= -halfL - 5 + 2 * t - 1; z += 2)
        {
            d.Set(-t, top + 1, z, "brick");
            d.Set(t - 1, top + 1, z, "brick");
        }

        return d.Build("Chapel (brick)");
    }

    /// <summary>A garden wall of brick, 1 m high, <paramref name="length"/> cells long, capped.</summary>
    public static Blueprint GardenWall(int length)
    {
        var d = new Draft();
        d.Fill(0, length - 1, 0, 1, 0, 0, "brick");
        d.Fill(0, length - 1, 2, 2, 0, 0, "brick_half");
        return d.Build("Garden wall (brick)");
    }

    /// <summary>
    /// Flak tower: a three-storey concrete keep 12.5 m square and 10 m high, built for one job: shutting the
    /// sky. On each corner of the roof a twin 128 mm heavy flak turret stands on its own raised platform,
    /// fed by a hoist from a flak magazine on the top floor. A 40 mm flak gun on each side gallery for anything
    /// that gets in close. Inside: shelters with bunks on the ground floor, stores and a workbench on the
    /// first, the command room with windows all round on the top, stairs between the floors, a ladder up
    /// onto the roof, and a search radar and searchlights between the guns.
    /// </summary>
    public static Blueprint FlakTower()
    {
        var d = new Draft();
        const int f2 = 6, f3 = 12, roof = 18; // the floor slabs
        d.Fill(-13, 13, 0, 0, -13, 13, "foundation");

        // The twin heavy flak on the roof corners, each with its hoist down to a magazine on the top floor.
        foreach (int cx in new[] { -7, 7 })
        foreach (int cz in new[] { -7, 7 })
        {
            HeavyFlakMount(d, cx, roof + 2, cz, cz < 0 ? Int3.Forward : Int3.Back);
            for (int y = f3 + 1; y <= roof + 1; y++) d.Set(cx, y, cz, "ammo_feed");
            d.Set(cx > 0 ? cx - 2 : cx + 1, f3 + 1, cz, "magazine_flak");
            d.Fill(cx - 3, cx + 3, roof + 1, roof + 1, cz - 3, cz + 3, "concrete");
        }

        // Ground floor: the shelters. Two doors, bunks, lockers, first aid and lamps; the stairs up.
        d.Set(-1, 2, -12, "door");
        d.Set(-1, 2, 12, "door");
        foreach (int z in new[] { -8, -3, 2, 7 }) d.Set(-11, 1, z, "bunk");
        foreach (int z in new[] { -8, 7 }) d.Set(-7, 1, z, "bunk");
        d.Set(-4, 2, 11, "weapon_locker");
        d.Set(3, 2, 11, "weapon_locker");
        d.Set(11, 3, 4, "first_aid", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));
        foreach ((int x, int z) in new[] { (-8, -6), (-8, 5), (0, -6), (0, 6), (6, 0) }) d.Set(x, f2 - 1, z, "lamp");
        for (int i = 0; i <= f2 - 1; i++)
            foreach (int x in new[] { 8, 9 })
                d.Set(x, 1 + i, -8 + i, "stairs");
        for (int i = 1; i <= f2 - 1; i++) d.Fill(8, 9, 1, i, -8 + i, -8 + i, "concrete");

        // First floor: the stores. Supply crates, a workbench, a locker, lamps; the stairs on up.
        foreach ((int x, int z) in new[] { (-10, -10), (-10, -7), (-7, -10) }) d.Set(x, f2 + 1, z, "supply_crate");
        d.Set(-9, f2 + 1, 9, "workbench");
        d.Set(3, f2 + 2, -11, "weapon_locker");
        foreach ((int x, int z) in new[] { (-6, -6), (-6, 6), (2, -6), (2, 6) }) d.Set(x, f3 - 1, z, "lamp");
        for (int i = 0; i <= f3 - f2 - 1; i++)
            foreach (int x in new[] { 8, 9 })
                d.Set(x, f2 + 1 + i, 2 + i, "stairs");
        for (int i = 1; i <= f3 - f2 - 1; i++) d.Fill(8, 9, f2 + 1, f2 + i, 2 + i, 2 + i, "concrete");

        // Top floor: the command room. The console, first aid, doors out to the galleries, lamps, the ladder up.
        d.Set(0, f3 + 1, -3, "command_console");
        d.Set(-11, f3 + 3, -2, "first_aid", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));
        d.Set(-1, f3 + 2, -12, "door");
        d.Set(-1, f3 + 2, 12, "door");
        foreach ((int x, int z) in new[] { (-4, -4), (-4, 4), (4, -4), (4, 4) }) d.Set(x, roof - 1, z, "lamp");
        for (int y = f3 + 1; y <= roof + 1; y++) d.Set(-11, y, 0, "ladder", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));

        // A gallery on the front and back of the top floor, each with two 40 mm flak guns behind a solid
        // parapet, carried out from the wall on sloped corbels.
        foreach (int s in new[] { -1, 1 })
        {
            d.Fill(-7, 7, f3, f3, Math.Min(s * 13, s * 17), Math.Max(s * 13, s * 17), "concrete");
            foreach (int x in new[] { -4, 4 }) FlakMount(d, x, f3 + 1, s * 15, s < 0 ? Int3.Forward : Int3.Back);
            for (int x = -7; x <= 7; x++) d.Fill(x, x, f3 + 1, f3 + 1, s * 17, s * 17, "concrete");
            for (int z = 13; z <= 16; z++)
                foreach (int x in new[] { -7, 7 })
                    d.Fill(x, x, f3 + 1, f3 + 1, s * z, s * z, "concrete");
            for (int x = -7; x <= 7; x++)
            {
                d.Set(x, f3 - 1, s * 13, "concrete");
                d.Slope(x, f3 - 1, s * 14, "concrete_slope", new Int3(0, -1, s));
                d.Slope(x, f3 - 2, s * 13, "concrete_slope", new Int3(0, -1, s));
            }
        }

        // Walls, floors and the roof; the stairwells and the ladder hole.
        d.Fill(-12, 12, 1, roof - 1, -12, -12, "concrete");
        d.Fill(-12, 12, 1, roof - 1, 12, 12, "concrete");
        d.Fill(-12, -12, 1, roof - 1, -11, 11, "concrete");
        d.Fill(12, 12, 1, roof - 1, -11, 11, "concrete");
        foreach (int y in new[] { f2, f3, roof }) d.Fill(-11, 11, y, y, -11, 11, "concrete");
        d.Fill(-12, 12, roof, roof, -12, 12, "concrete");
        foreach (int x in new[] { 8, 9 })
        {
            for (int z = -8; z <= -4; z++) d.Remove(x, f2, z);
            for (int z = 2; z <= 6; z++) d.Remove(x, f3, z);
        }

        // Slit windows on the first floor, wide ones round the command room.
        foreach (int i in new[] { -9, -5, 5, 9 })
        {
            d.Fill(i, i, f2 + 2, f2 + 3, -12, -12, "glazing");
            d.Fill(i, i, f2 + 2, f2 + 3, 12, 12, "glazing");
            d.Fill(-12, -12, f2 + 2, f2 + 3, i, i, "glazing");
            d.Fill(12, 12, f2 + 2, f2 + 3, i, i, "glazing");
        }

        foreach (int i in new[] { -10, -7, 4, 7 })
        {
            d.Fill(i, i + 1, f3 + 2, f3 + 3, -12, -12, "glazing");
            d.Fill(i, i + 1, f3 + 2, f3 + 3, 12, 12, "glazing");
        }

        foreach (int i in new[] { -9, -5, 3, 7 })
        {
            d.Fill(-12, -12, f3 + 2, f3 + 3, i, i + 1, "glazing");
            d.Fill(12, 12, f3 + 2, f3 + 3, i, i + 1, "glazing");
        }

        // A sloped skirt round the foot (not in front of the doors), a solid sloped parapet round the roof, and
        // the radar, searchlights and antenna between the guns.
        d.FillSlope(-13, -13, 1, 1, -13, 13, "concrete_slope", new Int3(-1, 1, 0));
        d.FillSlope(13, 13, 1, 1, -13, 13, "concrete_slope", new Int3(1, 1, 0));
        d.FillSlope(-12, 12, 1, 1, -13, -13, "concrete_slope", new Int3(0, 1, -1));
        d.FillSlope(-12, 12, 1, 1, 13, 13, "concrete_slope", new Int3(0, 1, 1));
        foreach (int x in new[] { -1, 0 })
        {
            d.Remove(x, 1, -13);
            d.Remove(x, 1, 13);
        }

        Parapet(d, -12, 12, roof + 1, -12, 12);
        d.Set(0, roof + 1, 0, "concrete");
        d.Set(0, roof + 2, 0, "radar");
        d.Set(0, roof + 1, -10, "searchlight");
        d.Set(0, roof + 1, 10, "searchlight");
        d.Set(10, roof + 1, 0, "searchlight");
        d.Set(1, roof + 1, 1, "antenna");
        return d.Build("Flak tower (128 mm)", "urban");
    }

    /// <summary>
    /// Twin 128 mm heavy flak on a turntable: two breeches side by side with a feed between them, long heavy
    /// barrels, a flak magazine behind, the gunner beside it, and side shields. Open-topped, so the guns can
    /// point straight up. The ring passes ammunition up from a hoist below.
    /// </summary>
    private static void HeavyFlakMount(Draft d, int x, int y, int z, Int3 facing)
    {
        int f = facing.Z;
        d.Set(x, y, z, "turret_ring");
        d.Set(x, y + 1, z, "ammo_feed");
        d.Set(x, y + 2, z, "ammo_feed");
        foreach (int gx in new[] { x - 1, x + 1 })
        {
            d.Set(gx, y + 2, z, "breech_128flak", Pointing(facing));
            for (int i = 1; i <= 8; i++) d.Set(gx, y + 2, z + f * i, "heavy_barrel", Pointing(facing));
        }

        d.Set(x, y + 1, Math.Min(z - f, z - 2 * f), "magazine_flak");
        d.Set(x - 1, y + 2, z - f, "gunner_seat", Pointing(facing));
        d.Fill(x - 2, x + 2, y + 1, y + 1, Math.Min(z + f, z - 2 * f), Math.Max(z + f, z - 2 * f), "armor_heavy");
        foreach (int sx in new[] { x - 2, x + 2 }) d.Fill(sx, sx, y + 2, y + 3, Math.Min(z + f, z - 2 * f), Math.Max(z + f, z - 2 * f), "armor_heavy");
    }

    /// <summary>
    /// Mortar battery: four 160 mm heavy mortars on concrete plinths in a sandbagged pit, each in its own bay
    /// between sandbag revetments, with ready racks beside it and earth banked round the outside. An
    /// ammunition line under the floor runs back to the magazine bunker, where four HE magazines are stacked.
    /// Behind the pit, the bunker and the crew dugout are buried under earth: bunks, lockers, first aid, a
    /// workbench, the fire-direction console, and a periscope, radar and antenna poking out of the mound.
    /// Wire across the front.
    /// </summary>
    public static Blueprint MortarBattery()
    {
        var d = new Draft();
        const int mz = -4;
        d.Fill(-21, 21, 0, 0, -13, 17, "foundation");

        // The mortars, their ready racks and gunners, and the plinth round each ring.
        foreach (int mx in new[] { -9, -3, 3, 9 })
        {
            d.Set(mx, 2, mz, "turret_ring");
            d.Set(mx, 3, mz, "mortar_160");
            foreach (int z in new[] { mz - 1, mz - 2 }) d.Set(mx, 3, z, "heavy_barrel");
            d.Set(mx - 1, 3, mz, "ammo_he");
            d.Set(mx + 1, 3, mz, "ammo_he");
            d.Set(mx, 3, mz + 1, "gunner_seat");
            d.Fill(mx - 1, mx + 1, 1, 2, mz - 1, mz + 1, "concrete");
        }

        // The ammunition line: under each mortar, along the pit, and back to the magazine bunker.
        for (int x = -9; x <= 9; x++) d.Set(x, 1, mz, "ammo_feed");
        for (int z = mz + 1; z <= 3; z++) d.Set(0, 1, z, "ammo_feed");
        foreach (int x in new[] { -2, 0 })
        foreach (int y in new[] { 1, 3 })
            d.Set(x, y, 4, "magazine_he");

        // The magazine bunker in the middle, and the crew dugout on either side of it, entered from the pit.
        d.Set(-13, 1, 7, "bunk");
        d.Set(12, 1, 7, "bunk");
        d.Set(-8, 2, 9, "weapon_locker");
        d.Set(7, 2, 9, "weapon_locker");
        d.Set(-5, 3, 7, "first_aid", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));
        d.Set(9, 1, 5, "workbench");
        d.Set(-9, 1, 6, "command_console");
        d.Set(8, 1, 7, "supply_crate");
        foreach ((int x, int z) in new[] { (-10, 7), (-6, 7), (-1, 7), (5, 7), (10, 7) }) d.Set(x, 5, z, "lamp");

        d.Fill(-14, 14, 1, 5, 4, 4, "concrete");
        d.Fill(-14, 14, 1, 5, 10, 10, "concrete");
        d.Fill(-14, -14, 1, 5, 5, 9, "concrete");
        d.Fill(14, 14, 1, 5, 5, 9, "concrete");
        foreach (int x in new[] { -4, 3 }) d.Fill(x, x, 1, 5, 5, 9, "concrete");
        d.Fill(-14, 14, 6, 6, 4, 10, "concrete");
        foreach (int x in new[] { -8, -7, 7, 8 })
            for (int y = 1; y <= 4; y++)
                d.Remove(x, y, 4);

        // Earth over the roof and banked up the sides and back, so the bunker is a grassy mound.
        d.Fill(-14, 14, 7, 7, 4, 10, "earth");
        ChamferEdge(d, -14, 14, 7, 4, 10, "earth");
        Berm(d, -14, 14, 4, 10, 6, (x, z) => z < 4);
        d.Set(-12, 8, 7, "periscope");
        d.Set(12, 8, 7, "antenna");
        d.Set(0, 8, 7, "radar");

        // The pit: sandbag walls round the front and sides (a way in through each side), sandbag revetments
        // between the bays, and a low earth bank outside.
        for (int x = -14; x <= 14; x++) d.Fill(x, x, 1, 2, -10, -10, "sandbags");
        for (int z = -9; z <= 3; z++)
            if (z < -1 || z > 0)
                foreach (int x in new[] { -14, 14 })
                    d.Fill(x, x, 1, 2, z, z, "sandbags");
        foreach (int x in new[] { -6, 0, 6 }) d.Fill(x, x, 1, 2, mz - 4, mz + 2, "sandbags");
        Berm(d, -14, 14, -10, 3, 2, (x, z) => z > 3 || (z >= -1 && z <= 0 && Math.Abs(x) > 14));

        for (int x = -21; x <= 21; x++) d.Set(x, 1, -13, "wire");
        return d.Build("Mortar battery (160 mm)", "olive");
    }

    /// <summary>
    /// Siege fortress: a two-storey concrete bastion 12 m square, half buried in an earth glacis, with a solid
    /// sloped parapet round its terrace (notched for the guns) and armored cupolas on it. The 420 mm siege
    /// mortar stands on the chamfered top roof behind a parapet, fed by a hoist from the magazine hall on the
    /// ground floor. A flak gun on each corner of the terrace, a sandbagged way in, and wire all round. Inside: crew quarters with
    /// bunks, lockers and first aid, a workbench, stairs up to the command room (console, windows all round),
    /// lamps throughout, and a ladder from the terrace to the mortar.
    /// </summary>
    public static Blueprint SiegeFortress()
    {
        var d = new Draft();
        const int l2 = 7, top = 13; // the terrace (the first floor's roof) and the top roof
        d.Fill(-16, 16, 0, 0, -16, 16, "foundation");

        // The mortar on the top roof, and the hoist down to the magazine hall.
        d.Set(0, top + 1, 0, "turret_ring");
        d.Set(0, top + 3, 0, "siege_mortar");
        for (int z = -2; z >= -4; z--) d.Set(0, top + 3, z, "heavy_barrel");
        d.Set(0, top + 2, 2, "gunner_seat");
        for (int y = 1; y <= top; y++) d.Set(0, y, 0, "ammo_feed");
        foreach (int y in new[] { 1, 3 })
        {
            d.Set(1, y, -1, "magazine_he");
            d.Set(-2, y, -1, "magazine_he");
        }

        // Ground floor: the door, crew quarters, lockers, first aid, the workbench, lamps, and the stairs up.
        d.Set(-1, 2, -12, "door");
        foreach (int z in new[] { -8, -3, 2 }) d.Set(-11, 1, z, "bunk");
        d.Set(-6, 2, 11, "weapon_locker");
        d.Set(-3, 2, 11, "weapon_locker");
        d.Set(-11, 3, 8, "first_aid", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));
        d.Set(6, 1, 10, "workbench");
        foreach ((int x, int z) in new[] { (-6, -6), (9, -8), (-6, 6), (6, 6), (-6, 0), (9, 3) }) d.Set(x, l2 - 1, z, "lamp");
        for (int i = 0; i <= l2 - 1; i++)
            foreach (int x in new[] { 5, 6 })
                d.Set(x, 1 + i, -6 + i, "stairs");
        // A solid masonry base under the flight.
        for (int i = 1; i <= l2 - 1; i++) d.Fill(5, 6, 1, i, -6 + i, -6 + i, "concrete");

        // Command room upstairs: the console at the front windows, doors out onto the terrace, lamps.
        d.Set(2, l2 + 1, -7, "command_console");
        d.Set(3, l2 + 2, -8, "door");
        d.Set(-4, l2 + 2, 8, "door");
        foreach ((int x, int z) in new[] { (-4, -4), (-4, 4), (3, 4), (3, -4) }) d.Set(x, top - 1, z, "lamp");
        for (int i = -6; i <= 6; i += 2)
        {
            if (i < 2 || i > 4) d.Fill(i, i + 1, l2 + 3, l2 + 4, -8, -8, "glazing");
            if (i < -5 || i > -3) d.Fill(i, i + 1, l2 + 3, l2 + 4, 8, 8, "glazing");
            d.Fill(-8, -8, l2 + 3, l2 + 4, i, i + 1, "glazing");
            d.Fill(8, 8, l2 + 3, l2 + 4, i, i + 1, "glazing");
        }

        // A flak gun on each corner of the terrace.
        foreach (int x in new[] { -10, 10 })
        {
            FlakMount(d, x, l2 + 1, -10, Int3.Forward);
            FlakMount(d, x, l2 + 1, 10, Int3.Back);
        }

        // The ladder from the terrace to the top roof.
        for (int y = l2 + 1; y <= top + 1; y++) d.Set(-9, y, 0, "ladder", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));

        // Walls, floors and roofs.
        d.Fill(-12, 12, 1, l2 - 1, -12, -12, "concrete");
        d.Fill(-12, 12, 1, l2 - 1, 12, 12, "concrete");
        d.Fill(-12, -12, 1, l2 - 1, -11, 11, "concrete");
        d.Fill(12, 12, 1, l2 - 1, -11, 11, "concrete");
        d.Fill(-12, 12, l2, l2, -12, 12, "concrete");
        d.Fill(-8, 8, l2 + 1, top - 1, -8, -8, "concrete");
        d.Fill(-8, 8, l2 + 1, top - 1, 8, 8, "concrete");
        d.Fill(-8, -8, l2 + 1, top - 1, -7, 7, "concrete");
        d.Fill(8, 8, l2 + 1, top - 1, -7, 7, "concrete");
        d.Fill(-8, 8, top, top, -8, 8, "concrete");
        // The stairwell up through the terrace floor.
        foreach (int x in new[] { 5, 6 })
            for (int z = -6; z <= -1; z++)
                d.Remove(x, l2, z);

        // A sandbagged way in to the door, then the earth glacis banked up the walls; a sloped parapet round the
        // terrace, notched where the corner guns' barrels and racks reach it; a chamfered edge on the top roof.
        for (int z = -15; z <= -13; z++)
            foreach (int x in new[] { -3, 2 })
                d.Fill(x, x, 1, 2, z, z, "sandbags");
        Berm(d, -12, 12, -12, 12, 3, (x, z) => z < -12 && x >= -2 && x <= 1);
        Parapet(d, -12, 12, l2 + 1, -12, 12, (x, z) =>
            (Math.Abs(z) == 12 && Math.Abs(Math.Abs(x) - 10) <= 1) || (Math.Abs(x) == 12 && Math.Abs(Math.Abs(z) - 10) <= 1));
        ChamferEdge(d, -8, 8, top, -8, 8);
        d.Set(0, l2 + 1, -10, "cupola");
        d.Set(0, l2 + 1, 10, "cupola");
        for (int i = -16; i <= 16; i++)
        {
            if (i < -3 || i > 2) d.Set(i, 1, -16, "wire");
            d.Set(i, 1, 16, "wire");
            if (i > -16 && i < 16)
            {
                d.Set(-16, 1, i, "wire");
                d.Set(16, 1, i, "wire");
            }
        }

        // A parapet round the mortar, a radar, searchlights and antennas on the top roof.
        for (int i = -6; i <= 6; i++)
            foreach ((int x, int z) in new[] { (i, -6), (i, 6), (-6, i), (6, i) })
                d.Fill(x, x, top + 1, top + 1, z, z, "concrete");
        d.Remove(-6, top + 1, 0);
        d.Set(7, top + 1, 7, "radar");
        d.Set(-7, top + 1, -7, "searchlight");
        d.Set(7, top + 1, -7, "searchlight");
        d.Set(-7, top + 1, 7, "antenna");
        d.Set(7, top + 1, 6, "antenna");
        return d.Build("Siege fortress (420 mm)", "olive");
    }

    /// <summary>A 40 mm flak gun on its own turntable: flak and HE racks beside the breech and the gunner behind.</summary>
    private static void FlakMount(Draft d, int x, int y, int z, Int3 facing)
    {
        d.Set(x, y, z, "turret_ring");
        d.Set(x, y + 1, z, "breech_40ac", Pointing(facing));
        for (int i = 1; i <= 3; i++) d.Set(x, y + 1, z + facing.Z * i, "barrel", Pointing(facing));
        d.Set(x - 1, y + 1, z, "ammo_flak");
        d.Set(x + 1, y + 1, z, "ammo_he");
        d.Set(x, y + 1, z - facing.Z, "gunner_seat");
    }

    /// <summary>
    /// Coastal citadel: a sea fort 17 m long. Two heavy railguns in armored turret houses on its deck, behind a
    /// sloped concrete parapet notched for their barrels, face the sea; behind them a 16 m tower carries the laser battery, which burns missiles and
    /// aircraft out of the sky for kilometers around. A close-in gatling on each corner and a block of missile
    /// cells in the deck. Below: two fusion reactors, a gallery of capacitors, the command bunker, crew
    /// quarters, a workbench and supply crates, and stairs up to the deck. A ladder climbs the tower inside.
    /// </summary>
    public static Blueprint CoastalCitadel()
    {
        var d = new Draft();
        const int deck = 7, tower = 31;
        d.Fill(-17, 17, 0, 0, -13, 13, "foundation");

        // Below decks: the reactors and capacitors, the command bunker and quarters, the doors and lamps.
        d.Set(-10, 2, 6, "reactor");
        d.Set(10, 2, 6, "reactor");
        for (int x = -6; x <= 5; x++) d.Set(x, 1, 11, "capacitor");
        d.Set(0, 1, 5, "command_console");
        foreach (int z in new[] { -8, -3 }) d.Set(-14, 1, z, "bunk");
        d.Set(12, 2, -11, "weapon_locker");
        d.Set(15, 3, 0, "first_aid", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));
        d.Set(-6, 1, 9, "workbench");
        d.Set(13, 1, -3, "supply_crate");
        d.Set(-1, 2, -12, "door");
        d.Set(8, 2, 12, "door");
        foreach ((int x, int z) in new[] { (-10, -6), (-3, -6), (4, -6), (-6, 2), (6, 1), (0, 8), (-12, 10) }) d.Set(x, deck - 1, z, "lamp");
        for (int i = 0; i <= deck - 1; i++)
            foreach (int x in new[] { 12, 13 })
                d.Set(x, 1 + i, 4 + i, "stairs");
        for (int i = 1; i <= deck - 1; i++) d.Fill(12, 13, 1, i, 4 + i, 4 + i, "concrete");

        // The two heavy railguns, each in its own armored house that turns with it.
        foreach (int x in new[] { -7, 7 }) RailHouse(d, x, deck + 1, -4);

        // The laser tower: a door at its foot, a ladder up inside, the battery on a railed platform at the top.
        d.Set(-1, deck + 2, 6, "door");
        for (int y = deck + 1; y <= tower + 1; y++) d.Set(0, y, 9, "ladder", Orient(new Int3(0, -1, 0), new Int3(0, 0, -1)));
        d.Set(0, tower + 1, 8, "turret_ring");
        d.Set(0, tower + 3, 8, "laser_battery");
        foreach (int z in new[] { 7, 6 }) d.Set(0, tower + 3, z, "laser_lens");
        d.Set(0, tower + 2, 10, "gunner_seat");
        foreach (int y in new[] { 14, 20, 26 }) d.Fill(-2, 2, y, y + 1, 6, 6, "glazing");
        d.Fill(-2, 2, deck + 1, tower - 1, 6, 6, "concrete");
        d.Fill(-2, 2, deck + 1, tower - 1, 10, 10, "concrete");
        d.Fill(-2, -2, deck + 1, tower - 1, 7, 9, "concrete");
        d.Fill(2, 2, deck + 1, tower - 1, 7, 9, "concrete");
        d.Fill(-3, 3, tower, tower, 5, 11, "concrete");
        d.Remove(0, tower, 9);
        for (int i = -3; i <= 3; i++)
            foreach ((int x, int z) in new[] { (i, 5), (i, 11) })
                if (x != 0) d.Set(x, tower + 1, z, "railing", Orient(new Int3(0, -1, 0), new Int3(0, 0, z == 5 ? 1 : -1)));
        d.Set(-3, tower + 1, 8, "radar");
        d.Set(3, tower + 1, 7, "searchlight");
        d.Set(3, tower + 1, 9, "antenna");

        // Missile cells in the deck between the guns, and a close-in gatling on each corner.
        for (int x = -3; x <= 2; x++)
        {
            d.Set(x, deck, 1, "vls_strike");
            d.Set(x, deck, 2, "vls_sam");
        }

        foreach (int x in new[] { -14, 14 })
        {
            CiwsAt(d, x, deck + 1, -10, Int3.Forward);
            CiwsAt(d, x, deck + 1, 10, Int3.Back);
        }

        // Walls and the deck, the stairwell through it, the glacis, and the parapet round the deck.
        d.Fill(-16, 16, 1, deck - 1, -12, -12, "concrete");
        d.Fill(-16, 16, 1, deck - 1, 12, 12, "concrete");
        d.Fill(-16, -16, 1, deck - 1, -11, 11, "concrete");
        d.Fill(16, 16, 1, deck - 1, -11, 11, "concrete");
        d.Fill(-16, 16, deck, deck, -12, 12, "concrete");
        foreach (int x in new[] { 12, 13 })
            for (int z = 4; z <= 9; z++)
                d.Remove(x, deck, z);
        d.FillSlope(-17, -17, 1, 1, -13, 13, "concrete_slope", new Int3(-1, 1, 0));
        d.FillSlope(17, 17, 1, 1, -13, 13, "concrete_slope", new Int3(1, 1, 0));
        d.FillSlope(-16, 16, 1, 1, -13, -13, "concrete_slope", new Int3(0, 1, -1));
        d.FillSlope(-16, 16, 1, 1, 13, 13, "concrete_slope", new Int3(0, 1, 1));
        foreach (int x in new[] { -1, 0 }) d.Remove(x, 1, -13);
        foreach (int x in new[] { 8, 9 }) d.Remove(x, 1, 13);
        // A sloped parapet round the deck, notched where the railguns' and gatlings' barrels cross it.
        Parapet(d, -16, 16, deck + 1, -12, 12, (x, z) => (z == -12 && Math.Abs(x) == 7) || (Math.Abs(z) == 12 && Math.Abs(Math.Abs(x) - 14) <= 1));
        return d.Build("Coastal citadel", "urban");
    }

    /// <summary>A heavy railgun on a turntable in an armored house that turns with it, its slugs inside, open at the back for the gunner.</summary>
    private static void RailHouse(Draft d, int x, int y, int z)
    {
        d.Set(x, y, z, "turret_ring");
        d.Set(x, y + 2, z, "heavy_rail_breech");
        for (int i = 2; i <= 9; i++) d.Set(x, y + 2, z - i, "rail");
        d.Set(x + 2, y + 1, z, "magazine_ap");
        d.Set(x, y + 1, z + 2, "gunner_seat");
        d.Fill(x - 2, x + 4, y + 4, y + 4, z - 2, z + 3, "armor_heavy");
        d.Fill(x - 2, x - 2, y + 1, y + 3, z - 2, z + 3, "armor_heavy");
        d.Fill(x + 4, x + 4, y + 1, y + 3, z - 2, z + 3, "armor_heavy");
        d.Fill(x - 1, x + 3, y + 1, y + 3, z - 2, z - 2, "armor_heavy");
        foreach (int xx in new[] { x + 2, x + 3 }) d.Fill(xx, xx, y + 1, y + 3, z + 3, z + 3, "armor_heavy");
        d.FillSlope(x - 2, x + 4, y + 4, y + 4, z - 3, z - 3, "armor_heavy_slope", new Int3(0, 1, -1));
    }

    /// <summary>A close-in gatling on its own turntable, with its ready racks.</summary>
    private static void CiwsAt(Draft d, int x, int y, int z, Int3 facing)
    {
        d.Set(x, y, z, "turret_ring");
        d.Set(x, y + 1, z, "breech_20gat", Pointing(facing));
        for (int i = 1; i <= 3; i++) d.Set(x, y + 1, z + facing.Z * i, "barrel", Pointing(facing));
        d.Set(x, y + 1, z - facing.Z, "ammo_he");
        d.Set(x, y + 2, z - facing.Z, "ammo_he");
    }

    /// <summary>
    /// Pillbox: a concrete blockhouse dug into an earth bank, with a 37 mm on a turntable inside firing out
    /// through a splayed slit in the front wall. Board-marked concrete with a chamfered roof, an armored
    /// observation cupola on top, a sandbagged trench round to the door at the back, and a line of wire in
    /// front. Inside: a bunk and a weapon locker for the crew, first aid and lamps.
    /// </summary>
    public static Blueprint Pillbox()
    {
        var d = new Draft();
        d.Fill(-8, 8, 0, 0, -10, 8, "foundation");
        d.Set(0, 1, -2, "turret_ring");
        d.Set(0, 2, -2, "breech_37");
        for (int z = -3; z >= -6; z--) d.Set(0, 2, z, "barrel");
        d.Set(-1, 2, -2, "ammo_ap");
        d.Set(1, 2, -2, "ammo_he");
        d.Set(0, 2, -1, "gunner_seat");

        d.Set(-1, 2, 4, "door");
        d.Set(-3, 1, 1, "bunk");
        d.Set(2, 2, 3, "weapon_locker");
        d.Set(3, 3, 0, "first_aid", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));
        foreach (int x in new[] { -2, 2 }) d.Set(x, 5, 1, "lamp");

        d.Fill(-4, 4, 1, 5, -4, -4, "concrete");
        d.Fill(-4, 4, 1, 5, 4, 4, "concrete");
        d.Fill(-4, -4, 1, 5, -3, 3, "concrete");
        d.Fill(4, 4, 1, 5, -3, 3, "concrete");
        d.Fill(-4, 4, 6, 6, -4, 4, "concrete");
        ChamferEdge(d, -4, 4, 6, -4, 4);

        // The firing slit, its sides and top splayed outward so the gun can swing and fire low.
        for (int x = -2; x <= 2; x++)
        for (int y = 2; y <= 3; y++)
            if (x != 0 || y != 2) d.Remove(x, y, -4);
        foreach (int y in new[] { 2, 3 })
        {
            d.Slope(-3, y, -4, "concrete_slope", new Int3(1, 0, -1));
            d.Slope(3, y, -4, "concrete_slope", new Int3(-1, 0, -1));
        }

        for (int x = -2; x <= 2; x++) d.Slope(x, 4, -4, "concrete_slope", new Int3(0, -1, -1));

        // The cupola and an antenna on the roof.
        d.Set(1, 7, 1, "cupola");
        d.Set(-2, 7, 2, "antenna");

        // A sandbagged trench round to the door, then earth banked up the sides and back to the slit.
        for (int z = 5; z <= 8; z++)
            foreach (int x in new[] { -3, 2 })
                d.Fill(x, x, 1, 2, z, z, "sandbags");
        Berm(d, -4, 4, -4, 4, 4, (x, z) => (z < -4 && x >= -4 && x <= 4) || (z > 4 && x >= -2 && x <= 1));

        // Wire across the front, clear of the gun.
        for (int x = -8; x <= 8; x++) d.Set(x, 1, -10, "wire");
        return d.Build("Pillbox (bunker)", "olive");
    }

    /// <summary>
    /// Flak nest: a 40 mm autocannon on a raised turntable in an eight-sided sandbagged pit, with flak and HE at
    /// hand, earth banked round the outside and a trench in at the back. A search radar and a searchlight
    /// on the sandbags, first aid inside, and wire round the whole position.
    /// </summary>
    public static Blueprint FlakNest()
    {
        var d = new Draft();
        d.Fill(-8, 8, 0, 0, -8, 8, "foundation");
        d.Set(0, 1, 0, "concrete");
        d.Set(0, 2, 0, "turret_ring");
        d.Set(0, 3, 0, "breech_40ac");
        for (int z = -1; z >= -3; z--) d.Set(0, 3, z, "barrel");
        d.Set(-1, 3, 0, "ammo_flak");
        d.Set(1, 3, 0, "ammo_he");
        d.Set(0, 3, 1, "gunner_seat");
        d.Set(-3, 2, 0, "first_aid", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));

        // The pit: an eight-sided ring of sandbags a metre high, open at the back; outside its corners, earth.
        for (int x = -4; x <= 4; x++)
        for (int z = -4; z <= 4; z++)
        {
            float o = MathF.Max(Math.Max(Math.Abs(x), Math.Abs(z)), (Math.Abs(x) + Math.Abs(z)) * 0.72f);
            bool gate = z == 4 && x >= -1 && x <= 1;
            if (MathF.Round(o) == 4 && !gate) d.Fill(x, x, 1, 2, z, z, "sandbags");
            else if (o > 4.5f) d.Fill(x, x, 1, 2, z, z, "earth");
        }

        d.Set(3, 3, 3, "radar");
        d.Set(-3, 3, -3, "searchlight");
        Berm(d, -4, 4, -4, 4, 2, (x, z) => z > 4 && x >= -1 && x <= 1);

        // Wire all round, with a gap at the trench.
        for (int i = -8; i <= 8; i++)
        {
            d.Set(i, 1, -8, "wire");
            if (i < -2 || i > 2) d.Set(i, 1, 8, "wire");
            if (i > -8 && i < 8)
            {
                d.Set(-8, 1, i, "wire");
                d.Set(8, 1, i, "wire");
            }
        }

        return d.Build("Flak nest (AA site)", "olive");
    }

    /// <summary>
    /// Outpost: a modern forward base. Earth-filled barrier baskets wall in a compound round a concrete
    /// command building, with a gate in front between two sandbagged guard towers, wire outside, and
    /// camouflage nets over the supply crates. Inside the building: bunks (rest there and you come back
    /// there), a weapon locker, first aid, and a workbench that repairs your vehicles parked nearby. The supply
    /// crates rearm and refuel them. Stairs up the side to a 40 mm anti-aircraft gun behind sandbags on the
    /// roof, with a searchlight and an antenna; a radio mast and a floodlight in the compound.
    /// </summary>
    public static Blueprint Outpost()
    {
        var d = new Draft();
        const int roof = 6;
        d.Fill(-13, 12, 0, 0, -20, 10, "foundation");

        // Inside.
        d.Set(-1, 2, -5, "door");
        d.Set(-5, 1, 0, "bunk");
        d.Set(4, 1, 0, "bunk");
        d.Set(-2, 2, 4, "weapon_locker");
        d.Set(1, 1, 3, "workbench");
        d.Set(-5, 3, -3, "first_aid", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));
        foreach ((int x, int z) in new[] { (-3, 0), (2, 0), (0, -3) }) d.Set(x, roof - 1, z, "lamp");
        foreach (int x in new[] { -4, -3, 2, 3 }) d.Fill(x, x, 3, 4, -5, -5, "glazing");

        // Walls and roof.
        d.Fill(-6, 6, 1, roof - 1, -5, -5, "concrete");
        d.Fill(-6, 6, 1, roof - 1, 5, 5, "concrete");
        d.Fill(-6, -6, 1, roof - 1, -4, 4, "concrete");
        d.Fill(6, 6, 1, roof - 1, -4, 4, "concrete");
        d.Fill(-6, 6, roof, roof, -5, 5, "concrete");

        // Supply crates in front, under camouflage nets.
        d.Set(3, 1, -8, "supply_crate");
        d.Set(-4, 1, -8, "supply_crate");
        NetOnPosts(d, 3, 4, -8);
        NetOnPosts(d, -4, 4, -8);

        // Stairs up the right-hand side to the roof.
        for (int i = 0; i < roof; i++)
            foreach (int x in new[] { 7, 8 })
                d.Set(x, 1 + i, -4 + i, "stairs");

        // The roof gun behind a sandbag parapet (open where the stairs come up).
        d.Set(0, roof + 1, 1, "turret_ring");
        d.Set(0, roof + 2, 1, "breech_40ac");
        for (int z = 0; z >= -2; z--) d.Set(0, roof + 2, z, "barrel");
        d.Set(-1, roof + 2, 1, "ammo_flak");
        d.Set(1, roof + 2, 1, "ammo_he");
        d.Set(0, roof + 2, 2, "gunner_seat");
        d.Set(-5, roof + 1, -4, "searchlight");
        d.Set(5, roof + 1, 4, "antenna");
        for (int i = -6; i <= 6; i++)
        {
            d.Set(i, roof + 1, -5, "sandbags");
            d.Set(i, roof + 1, 5, "sandbags");
            if (i > -5 && i < 5)
            {
                d.Set(-6, roof + 1, i, "sandbags");
                if (i < 1 || i > 2) d.Set(6, roof + 1, i, "sandbags");
            }
        }

        // The compound wall: barrier baskets all round, with a gate in front.
        for (int x = -12; x <= 10; x += 2)
        {
            d.Set(x, 2, 8, "hesco");
            if (x < -4 || x > 2) d.Set(x, 2, -18, "hesco");
        }

        for (int z = -16; z <= 6; z += 2)
        {
            d.Set(-12, 2, z, "hesco");
            d.Set(10, 2, z, "hesco");
        }

        // Guard towers either side of the gate, a radio mast and a floodlight, and wire outside the front.
        GuardTower(d, -8, -13, 8, 1);
        GuardTower(d, 7, -13, 8, 1);
        for (int y = 1; y <= 8; y++) d.Set(-10, y, 6, "post");
        d.Set(-10, 9, 6, "antenna");
        for (int y = 1; y <= 5; y++) d.Set(8, y, 6, "post");
        d.Set(8, 6, 6, "searchlight");
        for (int x = -13; x <= 12; x++)
            if (x < -5 || x > 4) d.Set(x, 1, -20, "wire");
        return d.Build("Outpost (forward base)", "desert_digital");
    }

    /// <summary>
    /// The command post: a small blockhouse you can walk into. A sliding door, windows all round, lamps in
    /// the ceiling and the command console at the back; outside, stairs up one side and a ladder up the other
    /// to the roof, where the radar turns.
    /// </summary>
    public static Blueprint CommandPost()
    {
        var d = new Draft();
        const int roof = 6;
        // The door, the console, the lamps and the windows go in first, so the walls fill in round them.
        d.Set(-1, 2, -4, "door");
        d.Set(0, 1, 3, "command_console", Pointing(Int3.Back));
        foreach ((int x, int z) in new[] { (-2, 0), (2, 0), (0, -2) }) d.Set(x, roof - 1, z, "lamp");
        for (int i = -3; i <= 3; i += 2)
        {
            d.Fill(i, i + 1, 3, 4, 4, 4, "glazing");
            d.Fill(-4, -4, 3, 4, i, i + 1, "glazing");
            d.Fill(4, 4, 3, 4, i, i + 1, "glazing");
        }

        d.Fill(2, 3, 3, 4, -4, -4, "glazing");
        d.Fill(-4, -3, 3, 4, -4, -4, "glazing");

        // Floor, walls and roof.
        d.Fill(-4, 4, 0, 0, -4, 4, "armor_heavy");
        d.Fill(-4, 4, 1, roof - 1, -4, -4, "armor_heavy");
        d.Fill(-4, 4, 1, roof - 1, 4, 4, "armor_heavy");
        d.Fill(-4, -4, 1, roof - 1, -3, 3, "armor_heavy");
        d.Fill(4, 4, 1, roof - 1, -3, 3, "armor_heavy");
        d.Fill(-4, 4, roof, roof, -4, 4, "armor_heavy");

        // Stairs up the left side to the roof, a ladder up the right.
        for (int i = 0; i < roof; i++)
            foreach (int x in new[] { -6, -5 })
                d.Set(x, 1 + i, -3 + i, "stairs");
        for (int y = 1; y <= roof + 1; y++) d.Set(5, y, 0, "ladder", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));

        // On the roof: the radar, an antenna, a searchlight and a rail round the edge.
        d.Set(0, roof + 1, 0, "radar");
        d.Set(3, roof + 1, 3, "antenna");
        d.Set(-3, roof + 1, -3, "searchlight");
        for (int i = -4; i <= 4; i++)
        {
            d.Set(i, roof + 1, -4, "railing", Orient(new Int3(0, -1, 0), new Int3(0, 0, 1)));
            d.Set(i, roof + 1, 4, "railing", Orient(new Int3(0, -1, 0), new Int3(0, 0, -1)));
            if (i > -4 && i < 4 && i != 0) d.Set(4, roof + 1, i, "railing", Orient(new Int3(0, -1, 0), new Int3(-1, 0, 0)));
            if (i > -4 && i < 2) d.Set(-4, roof + 1, i, "railing", Orient(new Int3(0, -1, 0), new Int3(1, 0, 0)));
        }

        return d.Build("Command post", "olive");
    }

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
