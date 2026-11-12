using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamGame.Core;

/// <summary>
/// Aircraft across the eras at real scale, each drawn from the look of real aircraft of its time: a Second World War
/// propeller fighter, an early swept-wing jet, a Cold War delta interceptor, a stealth fighter, and two experimental
/// fighters of the future. Plus the helpers that lay out fuselages and wings.
/// </summary>
public static partial class ExampleDesigns
{
    // ------------------------------------------------------------------ airframe helpers

    /// <summary>
    /// The orientation that lays a long-edge wing piece (a Wing Panel: long edge or its tip) so the pair runs from
    /// tip to wide part along <paramref name="run"/> with its cut facing <paramref name="cutSide"/>.
    /// </summary>
    public static int Taper(Int3 run, Int3 cutSide)
    {
        for (int o = 0; o < Orientations.Count; o++)
            if (Orientations.Apply(o, new Int3(0, 0, 1)) == run && Orientations.Apply(o, new Int3(-1, 0, 0)) == cutSide) return o;
        throw new ArgumentException($"No long edge runs {run} with its cut to {cutSide}.");
    }

    /// <summary>Where a wing panel's edge is on one column of a planform: the first and last full panel (chord-wise z).</summary>
    private readonly record struct Chord(int Column, int Lead, int Trail);

    /// <summary>
    /// A wing, tailplane or fin laid out from its root column at <paramref name="root"/> out along <paramref name="outDir"/>
    /// (±X for a wing, +Y for a fin), with its leading and trailing edges described one step per letter, root outward:
    /// 'F' straight; 'S' swept back 45° (one column out, one cell back); 'T' swept back 27° (two columns out, one back);
    /// 'D' swept back 63° (one column out, two back); lower case the same swept forward. The root's first and last full
    /// panels are at chord-wise z <paramref name="lead"/> and <paramref name="trail"/>. Both edges must cover the same
    /// number of columns. Skips cells already used (so put guns and booms first) and returns each column's full panels.
    /// </summary>
    private static List<Chord> Planform(Draft d, Int3 root, Int3 outDir, int lead, int trail, string leSteps, string teSteps, string panel = "wing")
    {
        static List<char> Expand(string steps) =>
            steps.SelectMany(c => char.ToUpperInvariant(c) == 'T' ? new[] { c, c == 'T' ? '2' : '3' } : new[] { c }).ToList();

        List<char> le = Expand(leSteps), te = Expand(teSteps);
        if (le.Count != te.Count) throw new ArgumentException($"Leading edge covers {le.Count} columns, trailing edge {te.Count}.");
        var back = new Int3(0, 0, 1);
        Int3 normal = outDir.Y != 0 ? Int3.Right : Int3.Up;
        int flat = normal == Int3.Up ? 0 : Orientations.RotateAroundZ(0, 1);
        var chords = new List<Chord>();
        int lf = lead, lt = trail;
        for (int i = 0; i < le.Count; i++)
        {
            Int3 col = root + outDir * i;
            void Put(int z, string id, int o) => d.SetIfFree(col.X, col.Y, z, id, o);
            int Edge(Int3 cut) => ExampleDesigns.Edge(cut, normal);

            // Leading edge.
            int first = lf;
            switch (le[i])
            {
                case 'S': Put(lf, "wing_swept", Edge(outDir - back)); first = lf + 1; lf++; break;
                case 's': Put(lf - 1, "wing_swept", Edge(-outDir - back)); lf--; break;
                case 'T': Put(lf, "wing_taper", Taper(-outDir, -back)); first = lf + 1; break;
                case '2': Put(lf, "wing_taper_tip", Taper(-outDir, -back)); first = lf + 1; lf++; break;
                case 't': Put(lf - 1, "wing_taper_tip", Taper(outDir, -back)); break;
                case '3': Put(lf - 1, "wing_taper", Taper(outDir, -back)); lf--; break;
                case 'D': Put(lf, "wing_taper_tip", Taper(back, outDir)); Put(lf + 1, "wing_taper", Taper(back, outDir)); first = lf + 2; lf += 2; break;
                case 'd': Put(lf - 2, "wing_taper_tip", Taper(back, -outDir)); Put(lf - 1, "wing_taper", Taper(back, -outDir)); lf -= 2; break;
            }

            // Trailing edge.
            int last = lt;
            switch (te[i])
            {
                case 'S': Put(lt + 1, "wing_swept", Edge(-outDir + back)); lt++; break;
                case 's': Put(lt, "wing_swept", Edge(outDir + back)); last = lt - 1; lt--; break;
                case 'T': Put(lt + 1, "wing_taper_tip", Taper(outDir, back)); break;
                case '2': Put(lt + 1, "wing_taper", Taper(outDir, back)); lt++; break;
                case 't': Put(lt, "wing_taper", Taper(-outDir, back)); last = lt - 1; break;
                case '3': Put(lt, "wing_taper_tip", Taper(-outDir, back)); last = lt - 1; lt--; break;
                case 'D': Put(lt + 1, "wing_taper", Taper(-back, -outDir)); Put(lt + 2, "wing_taper_tip", Taper(-back, -outDir)); lt += 2; break;
                case 'd': Put(lt, "wing_taper_tip", Taper(-back, outDir)); Put(lt - 1, "wing_taper", Taper(-back, outDir)); last = lt - 2; lt -= 2; break;
            }

            for (int z = first; z <= last; z++) Put(z, panel, flat);
            chords.Add(new Chord(i, first, last));
        }

        return chords;
    }

    /// <summary>A wing panel's flat orientation in a planform going out along <paramref name="outDir"/>.</summary>
    private static int FlatFor(Int3 outDir) => outDir.Y != 0 ? Orientations.RotateAroundZ(0, 1) : 0;

    /// <summary>
    /// A fuselage lofted from cross-sections: at each station z from <paramref name="z0"/> to <paramref name="z1"/>,
    /// the cells from −hw to hw across and from bottom to top (none where hw is below 0). <paramref name="hollow"/> cells
    /// are left empty (ducts, bays) but count as inside. Fills only empty cells, so put engines, tanks and seats in
    /// first; then shapes the skin (see <see cref="Streamline"/>).
    /// </summary>
    private static HashSet<Int3> Fuselage(Draft d, int z0, int z1, Func<int, (int hw, int bottom, int top)> section,
        string mat = "airframe", Func<Int3, bool> hollow = null, bool faceted = false) =>
        Fuselage(d, z0, z1, -64, 64, (z, y) => section(z) is var (hw, b, t) && y >= b && y <= t ? hw : -1, mat, hollow, faceted);

    /// <summary>
    /// The same, with a half-width for every row of every station (−1 for none), so a section can be narrower on top
    /// (a spine) or underneath (a keel) than across its middle. Rows from <paramref name="y0"/> to <paramref name="y1"/>.
    /// </summary>
    private static HashSet<Int3> Fuselage(Draft d, int z0, int z1, int y0, int y1, Func<int, int, int> halfWidth,
        string mat = "airframe", Func<Int3, bool> hollow = null, bool faceted = false)
    {
        var body = new HashSet<Int3>();
        for (int z = z0; z <= z1; z++)
        for (int y = y0; y <= y1; y++)
        {
            int hw = halfWidth(z, y);
            for (int x = -hw; x <= hw; x++)
            {
                var c = new Int3(x, y, z);
                if (hollow != null && hollow(c)) continue;
                if (d.SetIfFree(x, y, z, mat)) body.Add(c);
            }
        }

        Streamline(d, body, mat, hollow, faceted);
        return body;
    }

    /// <summary>
    /// Shapes a body's skin where it meets the air: long edges (open on two sides across) get a rounded piece, edges
    /// at the nose and tail (open ahead or behind, and on one side) a slope, and corners open three ways a corner
    /// piece. A cell open on both sides of one axis (a thin spine) stays square across it. Wing panels, folded gear and
    /// small fittings count as open air; anything else beside the skin counts as covering it. A
    /// <paramref name="faceted"/> skin takes flat slopes along its long edges too: the sharp chines of a stealth aircraft.
    /// </summary>
    private static void Streamline(Draft d, ICollection<Int3> body, string mat, Func<Int3, bool> hollow = null, bool faceted = false)
    {
        bool Open(Int3 n)
        {
            if (body.Contains(n) || (hollow != null && hollow(n))) return false;
            string id = d.IdAt(n.X, n.Y, n.Z);
            if (id == null) return true;
            BlockDef def = BlockCatalog.Get(id);
            return def.IsLiftingSurface || def.Retracts || def.Kind == BlockKind.Detail;
        }

        var shapes = new List<(Int3 c, string id, int o)>();
        foreach (Int3 c in body)
        {
            if (d.IdAt(c.X, c.Y, c.Z) != mat) continue;
            var open = Int3.Directions.Where(dir => Open(c + dir)).ToList();
            open.RemoveAll(dir => open.Contains(-dir));
            Int3 sum = open.Aggregate(new Int3(0, 0, 0), (a, b) => a + b);
            if (open.Count == 2)
            {
                bool lengthwise = sum.Z == 0;
                shapes.Add((c, mat + (lengthwise && !faceted ? "_round" : "_slope"), Facing(sum)));
            }
            else if (open.Count == 3)
            {
                shapes.Add((c, mat + "_corner", CornerFacing(new Int3(sum.X, 0, sum.Z), new Int3(0, sum.Y, sum.Z))));
            }
        }

        foreach ((Int3 c, string id, int o) in shapes) d.Set(c.X, c.Y, c.Z, id, o);
    }

    // ------------------------------------------------------------------ the aircraft

    /// <summary>
    /// Shrike: a Second World War fighter at full size, 10 m long with an 11 m span, after the long-nosed, low-winged
    /// single-seaters of the last years of the war. A four-blade propeller on a long cowling with exhaust stacks down
    /// its sides and a 40 mm cannon firing through the nose, a framed bubble canopy on a narrow spine, a belly radiator
    /// scoop, straight tapered wings with round tips, a round-tipped tail with a dorsal fillet, and two main wheels and
    /// a tail wheel that all fold away. Bare polished metal.
    /// </summary>
    public static Blueprint Shrike()
    {
        var d = new Draft();

        // The propeller at the top of the nose (the engine's thrust line). A 40 mm cannon fires through the nose below
        // the spinner, its breech and rack of rounds behind the barrel, and the engine sits behind the cannon under
        // the cockpit floor, driving the propeller on a long shaft (two Aero Engines together: 1,600 kW, about a
        // late-war fighter's).
        d.Set(0, 4, -10, "air_propeller_large");
        for (int z = -9; z <= -7; z++) d.Set(0, 3, z, "barrel");
        d.Set(0, 3, -6, "breech_40ac");
        d.Set(0, 3, -5, "ammo_ap");
        d.Set(0, 3, -4, "aero_engine");
        d.Set(0, 3, -3, "aero_engine");

        // The pilot under a framed bubble canopy, fuel tanks behind them.
        d.Set(0, 4, -2, "pilot_seat");
        d.Set(0, 5, -1, "canopy_framed");
        for (int z = 1; z <= 3; z++)
        {
            d.Set(0, 3, z, "fuel_tank");
            d.Set(0, 2, z, "fuel_tank");
        }

        // The radiator scoop under the belly, behind the wing.
        for (int z = 0; z <= 4; z++) d.Set(0, 1, z, "airframe");
        d.Slope(0, 1, 0, "airframe_slope", new Int3(0, -1, -1));
        d.Slope(0, 1, 4, "airframe_slope", new Int3(0, -1, 1));

        // Wings: straight leading edge stepping back once out near mid-span, the trailing edge tapering forward, round
        // tips, ailerons on the outer trailing edge.
        foreach (int s in new[] { -1, 1 })
        {
            var outDir = new Int3(s, 0, 0);
            List<Chord> wing = Planform(d, new Int3(2 * s, 2, 0), outDir, -5, 0, "FFFTFTF", "FFFtFtF");
            foreach (Chord c in wing.Where(c => c.Column >= 6))
                d.Set(s * (2 + c.Column), 2, c.Trail, "control_surface");
            // A round tip: the front and back corners rounded off.
            d.Set(11 * s, 2, -3, "wing_round", Edge(new Int3(s, 0, -1), Int3.Up));
            d.Set(11 * s, 2, -2, "wing_round", Edge(new Int3(s, 0, 1), Int3.Up));
        }

        // Fuselage: the cowling tapering in to the spinner, the body three cells across and deep with its edges
        // rounded, the belly rising behind the scoop, and the tail a single cell across.
        Fuselage(d, -9, 9, 2, 4, (z, y) => z switch
        {
            -9 => y >= 3 ? 0 : -1,
            <= 5 => 1,
            <= 7 => y >= 3 ? 1 : -1,
            _ => y >= 3 ? 0 : -1,
        });

        // The cockpit sill either side of the canopy, rounded off so the canopy sits on a narrow back.
        for (int z = -3; z <= 1; z++)
            foreach (int s in new[] { -1, 1 })
                d.Set(s, 4, z, "airframe_round", Facing(new Int3(s, 1, 0)));

        // Exhaust stacks along each side of the cowling.
        foreach (int s in new[] { -1, 1 }) d.Set(2 * s, 3, -8, "exhaust_stacks", Orient(new Int3(-s, 0, 0), new Int3(0, 0, -1)));

        // Tailplane with elevators and round tips.
        foreach (int s in new[] { -1, 1 })
        {
            // (Big elevators, two rows deep: the long heavy nose needs them to lift off.)
            for (int x = 1; x <= 3; x++)
            {
                d.Set(x * s, 3, 7, "wing");
                d.Set(x * s, 3, 8, "control_surface");
                d.Set(x * s, 3, 9, "control_surface");
            }

            d.Set(4 * s, 3, 7, "wing_round", Edge(new Int3(s, 0, -1), Int3.Up));
            d.Set(4 * s, 3, 8, "wing_round", Edge(new Int3(s, 0, 1), Int3.Up));
        }

        // Fin and rudder: a long dorsal fillet rising from the spine, a 45° leading edge and a round top.
        int upright = Orientations.RotateAroundZ(0, 1);
        d.Set(0, 5, 3, "wing_taper_tip", Taper(new Int3(0, 0, 1), Int3.Up));
        d.Set(0, 5, 4, "wing_taper", Taper(new Int3(0, 0, 1), Int3.Up));
        for (int y = 5; y <= 8; y++)
        {
            int lead = Math.Max(5, y);
            if (y >= 6 && y <= 7) d.Set(0, y, lead, "wing_swept", Edge(new Int3(0, 1, -1), Int3.Right));
            for (int z = y >= 6 ? lead + 1 : lead; z <= 8; z++) d.Set(0, y, z, "wing", upright);
            d.Set(0, y, 9, "control_surface", upright);
        }

        d.Set(0, 8, 8, "wing_round", Edge(new Int3(0, 1, -1), Int3.Right));

        // Landing gear: main legs under the wings ahead of the center of mass, a tail wheel at the back.
        foreach (int s in new[] { -1, 1 }) d.Set(3 * s, 1, -4, "landing_gear");
        d.Set(0, 2, 8, "landing_gear_tail");
        return d.Build("Shrike (WWII fighter)", "natural_metal");
    }

    /// <summary>
    /// Swallow: an early swept-wing jet fighter at full size, 11.5 m long with a 10.5 m span, after the first jet
    /// fighters to fight each other: a round nose intake with the duct running under the cockpit to one jet behind it,
    /// a framed bubble canopy, wings, tailplane and fin all swept back about 34°, two cannon beside the intake, and
    /// folding gear. One built jet with four compressor stages and a plain nozzle (no afterburner yet). Bare metal.
    /// </summary>
    public static Blueprint Swallow()
    {
        var d = new Draft();
        bool Duct(Int3 c) => c.X == 0 && c.Y == 2 && c.Z <= 2;

        // The jet behind the cockpit, fed by the duct from the nose: intake, four compressors, core, two turbines, nozzle.
        d.Set(0, 2, 3, "jet_intake");
        for (int z = 4; z <= 7; z++) d.Set(0, 2, z, "jet_compressor");
        d.Set(0, 2, 8, "jet_core");
        d.Set(0, 2, 9, "jet_turbine");
        d.Set(0, 2, 10, "jet_turbine");
        d.Set(0, 2, 11, "jet_nozzle");

        // Cockpit over the duct, and fuel down the spine behind it.
        d.Set(0, 3, -6, "pilot_seat");
        d.Set(0, 4, -5, "canopy_framed");
        for (int z = -2; z <= 8; z++) d.Set(0, 3, z, "fuel_tank");

        // Two cannon low beside the intake, their muzzles at the nose, sharing a rack of rounds between them.
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(s, 1, -8, "breech_40ac");
            for (int z = -11; z <= -9; z++) d.Set(s, 1, z, "barrel");
        }

        d.Set(0, 1, -8, "ammo_ap");

        // Fuselage: round-edged, three cells across, with a narrow spine from the canopy back to the fin.
        Fuselage(d, -11, 11, 1, 4, (z, y) => y <= 3 ? 1 : z >= -2 && z <= 10 ? 0 : -1, hollow: Duct);
        for (int z = -7; z <= -3; z++)
            foreach (int s in new[] { -1, 1 })
                d.Set(s, 3, z, "airframe_round", Facing(new Int3(s, 1, 0)));

        // Wings swept back about 34° (alternating long and 45° edge pieces), ailerons on the outer trailing edge.
        foreach (int s in new[] { -1, 1 })
        {
            List<Chord> wing = Planform(d, new Int3(2 * s, 1, 0), new Int3(s, 0, 0), -4, 1, "TSTSTS", "TFTFTF");
            foreach (Chord c in wing.Where(c => c.Column >= 5)) d.Set(s * (2 + c.Column), 1, c.Trail, "control_surface");

            // Tailplane, swept the same: all of it swings (an all-moving "flying tail", for control near the speed of sound).
            List<Chord> tail = Planform(d, new Int3(2 * s, 2, 0), new Int3(s, 0, 0), 8, 10, "TS", "FFF");
            foreach (Chord c in tail)
                for (int z = c.Lead; z <= c.Trail; z++) d.Set(s * (2 + c.Column), 2, z, "control_surface");
        }

        // Fin and rudder.
        int upright = Orientations.RotateAroundZ(0, 1);
        foreach (Chord c in Planform(d, new Int3(0, 5, 0), Int3.Up, 5, 10, "TSF", "FFFF"))
            d.Set(0, 5 + c.Column, c.Trail, "control_surface", upright);

        // Folding gear: a steering nose leg and main legs under the wing roots.
        d.Set(0, 0, -7, "landing_gear_steer");
        foreach (int s in new[] { -1, 1 }) d.Set(3 * s, 0, 1, "landing_gear");
        return d.Build("Swallow (early jet fighter)", "natural_metal");
    }

    /// <summary>
    /// Tern: a Cold War delta-winged interceptor at full size, 13 m long with an 8.5 m span, after the Mach 2 deltas
    /// of the 1960s: a long radar nose, a framed canopy, side intakes with half-cone shock bodies, a 63° delta wing
    /// with elevons along its whole trailing edge and no tailplane, a tall swept fin, a radar missile under each wing
    /// and a cannon under the right intake. One built jet with a ramp intake, five compressors, an afterburner and a
    /// full-size variable nozzle. Jungle camouflage over white.
    /// </summary>
    public static Blueprint Tern()
    {
        var d = new Draft();
        bool Duct(Int3 c) => c.Y == 2 && ((Math.Abs(c.X) == 2 && c.Z >= -8 && c.Z <= -3) || (Math.Abs(c.X) <= 1 && c.Z == -3));

        d.Set(0, 2, -14, "radome");
        d.Set(0, 3, -9, "pilot_seat");
        d.Set(0, 4, -9, "canopy_highback");

        // The jet down the middle: the side intakes' ducts meet in front of its ramp intake.
        d.Set(0, 2, -2, "jet_intake_ramp");
        for (int z = -1; z <= 3; z++) d.Set(0, 2, z, "jet_compressor");
        d.Set(0, 2, 4, "jet_core");
        for (int z = 5; z <= 7; z++) d.Set(0, 2, z, "jet_turbine");
        d.Set(0, 2, 8, "jet_afterburner");
        d.Set(0, 2, 9, "jet_afterburner");
        d.Set(0, 2, 10, "jet_nozzle_large");
        for (int z = 0; z <= 9; z++) d.Set(0, 3, z, "fuel_tank");

        // The cannon under the right intake.
        d.Set(3, 1, -7, "breech_40ac");
        d.Set(3, 1, -8, "barrel");
        d.Set(3, 1, -6, "ammo_ap");

        // Fuselage three cells across with a spine behind the canopy, the intake trunks either side.
        Fuselage(d, -11, 9, 1, 4, (z, y) =>
            y == 4 ? (z >= -6 ? 0 : -1)
            : z >= -8 && z <= -1 ? 3
            : 1, hollow: Duct);
        for (int z = -11; z <= -7; z++)
            foreach (int s in new[] { -1, 1 })
                d.Set(s, 3, z, "airframe_round", Facing(new Int3(s, 1, 0)));

        // Half-cone shock bodies in the intake mouths.
        foreach (int s in new[] { -1, 1 }) d.Set(2 * s, 2, -9, "nose_cone", Pointing(new Int3(0, 0, -1)));

        // The delta: a 63° leading edge from the intakes to the tips, a straight trailing edge of elevons.
        foreach (int s in new[] { -1, 1 })
            foreach (Chord c in Planform(d, new Int3(2 * s, 1, 0), new Int3(s, 0, 0), -9, 5, "DDDDDDD", "FFFFFFF"))
                d.Set(s * (2 + c.Column), 1, c.Trail, "control_surface");

        // Tall fin with its rudder.
        int upright = Orientations.RotateAroundZ(0, 1);
        foreach (Chord c in Planform(d, new Int3(0, 5, 0), Int3.Up, 3, 9, "SSSSS", "FFFFF"))
            d.Set(0, 5 + c.Column, c.Trail, "control_surface", upright);

        // A radar missile under each wing.
        foreach (int s in new[] { -1, 1 })
        {
            int x = 5 * s;
            d.Set(x, 0, 5, "missile_rail");
            d.Set(x, 0, 4, "missile_motor");
            d.Set(x, 0, 3, "missile_fins");
            d.Set(x, 0, 2, "missile_warhead");
            d.Set(x, 0, 1, "seeker_radar");
        }

        d.Set(0, 0, -9, "landing_gear_steer");
        foreach (int s in new[] { -1, 1 }) d.Set(3 * s, 0, 3, "landing_gear");
        return d.Build("Tern (delta interceptor)", "sea_camo");
    }

    /// <summary>
    /// A weapons bay: two missiles side by side inside the belly at x = ±1 on row 1, rails at <paramref name="rail"/>
    /// and noses toward −Z, in a keel one row deeper than the body (row 0) whose floor under them is bay doors.
    /// </summary>
    private static void WeaponsBay(Draft d, int rail, string skin, string seeker = "seeker_radar")
    {
        foreach (int s in new[] { -1, 1 })
        {
            d.Set(s, 1, rail, "missile_rail");
            d.Set(s, 1, rail - 1, "missile_motor");
            d.Set(s, 1, rail - 2, "missile_fins");
            d.Set(s, 1, rail - 3, "missile_warhead");
            d.Set(s, 1, rail - 4, seeker);
            for (int z = rail - 4; z <= rail; z++) d.Set(s, 0, z, "bay_door", s < 0 ? Orientations.RotateAroundY(0, 2) : 0);
        }

        for (int z = rail - 4; z <= rail; z++) d.Set(0, 0, z, skin);
    }

    /// <summary>
    /// Nightjar: a stealth fighter at full size, 17 m long with a 13.5 m span, drawn from the look of today's stealth
    /// fighters: radar-absorbent skin all over in flat facets with sharp chines, a flat nose, a bubble canopy, intakes
    /// either side of the cockpit, a diamond wing (leading edge swept back, trailing edge forward), all-moving
    /// tailplanes, twin fins, two radar missiles in a belly weapons bay behind doors, and two built jets with afterburners
    /// and flat vectoring nozzles that hide their heat. Spotted from about a third of the usual range. Low-vis grey.
    /// </summary>
    public static Blueprint Nightjar()
    {
        var d = new Draft();
        const string skin = "airframe_stealth";
        bool Duct(Int3 c) => Math.Abs(c.X) == 2 && c.Y == 2 && c.Z >= -10 && c.Z <= 2;

        d.Set(0, 2, -17, "nose_cone", Pointing(new Int3(0, 0, -1)));
        d.Set(0, 2, -14, "radar");
        d.Set(0, 3, -11, "pilot_seat");
        d.Set(0, 4, -11, "canopy_stealth");
        WeaponsBay(d, 1, skin);

        // Two jets fed by the ducts from the intakes: ramp intake, five compressors, core, three turbines, two
        // afterburner stages and a flat vectoring nozzle.
        foreach (int s in new[] { -1, 1 })
        {
            int x = 2 * s;
            d.Set(x, 2, 3, "jet_intake_ramp");
            for (int z = 4; z <= 8; z++) d.Set(x, 2, z, "jet_compressor");
            d.Set(x, 2, 9, "jet_core");
            for (int z = 10; z <= 12; z++) d.Set(x, 2, z, "jet_turbine");
            d.Set(x, 2, 13, "jet_afterburner");
            d.Set(x, 2, 14, "jet_afterburner");
            d.Set(x, 2, 15, "jet_nozzle_2d");
        }

        for (int z = -8; z <= 12; z++) d.Set(0, 2, z, "fuel_tank");
        for (int z = 3; z <= 12; z++)
            foreach (int s in new[] { -1, 1 })
                d.Set(s, 2, z, "fuel_tank");

        // The body: a flat chined nose, the cockpit section, then seven cells across over the intakes and engines,
        // a keel round the weapons bay, and a flat tail between the nozzles. All faceted.
        Fuselage(d, -16, 16, 0, 3, (z, y) =>
            y == 0 ? (z >= -4 && z <= 2 ? 1 : -1)
            : z == -16 ? (y == 2 ? 1 : -1)
            : z <= -11 ? 1
            : z <= 14 ? 3
            : y == 2 ? 0 : -1, skin, Duct, faceted: true);

        foreach (int s in new[] { -1, 1 })
        {
            // Diamond wing: leading edge swept back 45°, trailing edge forward 27°, ailerons outboard.
            List<Chord> wing = Planform(d, new Int3(4 * s, 2, 0), new Int3(s, 0, 0), -4, 10, "SSSSSSSSS", "ttttF");
            foreach (Chord c in wing.Where(c => c.Column >= 5)) d.Set(s * (4 + c.Column), 2, c.Trail, "control_surface");

            // All-moving tailplanes.
            foreach (Chord c in Planform(d, new Int3(4 * s, 2, 0), new Int3(s, 0, 0), 12, 15, "SSS", "FFs"))
                for (int z = c.Lead; z <= c.Trail; z++) d.Set(s * (4 + c.Column), 2, z, "control_surface");

            // Fins on the intake trunks, both edges swept, rudders behind.
            int upright = Orientations.RotateAroundZ(0, 1);
            foreach (Chord c in Planform(d, new Int3(2 * s, 4, 0), Int3.Up, 6, 12, "SSSS", "FFsF"))
                if (c.Column >= 1) d.Set(2 * s, 4 + c.Column, c.Trail, "control_surface", upright);
        }

        d.Set(0, 0, -11, "landing_gear_steer");
        foreach (int s in new[] { -1, 1 }) d.Set(3 * s, 0, 6, "landing_gear");
        return d.Build("Nightjar (stealth fighter)", "lowvis");
    }

    /// <summary>
    /// Halcyon: an experimental fighter of the near future, 16.5 m long with a 12.5 m span, drawn from the real
    /// forward-swept research aircraft and the look of fighters in near-future fiction: a needle nose with a laser in its
    /// tip, canards beside the cockpit, wings swept forward, intakes under the canards, two built jets with vectoring
    /// nozzles that swing up, down and sideways, twin fins, a tail sting, two radar missiles in a belly bay, and green
    /// formation lights. White prototype paint with black slashes.
    /// </summary>
    public static Blueprint Halcyon()
    {
        var d = new Draft();
        bool Duct(Int3 c) => Math.Abs(c.X) == 2 && c.Y == 2 && c.Z >= -9 && c.Z <= 1;

        // The laser in the nose (its lens is the tip), the generator and capacitors behind the cockpit to feed it.
        d.Set(0, 2, -17, "laser_lens");
        d.Set(0, 2, -16, "laser_emitter");
        d.Set(0, 2, -14, "radar");
        d.Set(0, 3, -10, "pilot_seat");
        d.Set(0, 4, -10, "canopy_stealth");
        d.Set(0, 2, -7, "generator");
        d.Set(0, 2, -6, "capacitor");
        d.Set(0, 2, -5, "capacitor");
        WeaponsBay(d, 0, "airframe");

        foreach (int s in new[] { -1, 1 })
        {
            int x = 2 * s;
            d.Set(x, 2, 2, "jet_intake_ramp");
            for (int z = 3; z <= 7; z++) d.Set(x, 2, z, "jet_compressor");
            d.Set(x, 2, 8, "jet_core");
            for (int z = 9; z <= 11; z++) d.Set(x, 2, z, "jet_turbine");
            d.Set(x, 2, 12, "jet_afterburner");
            d.Set(x, 2, 13, "jet_afterburner");
            d.Set(x, 2, 14, "jet_nozzle_vectoring");
        }

        for (int z = -13; z <= 7; z++)
            if (d.IdAt(0, 2, z) == null) d.Set(0, 2, z, "fuel_tank");

        // Body: a needle nose widening to the cockpit, intake trunks under the canards, the keel round the bay.
        Fuselage(d, -16, 13, 0, 3, (z, y) =>
            y == 0 ? (z >= -5 && z <= 1 ? 1 : -1)
            : z <= -14 ? (y == 2 ? 1 : 0)
            : z <= -10 ? 1
            : 3, hollow: Duct);
        // A sting between the nozzles.
        d.Set(0, 2, 14, "airframe");
        d.Set(0, 2, 15, "nose_cone", Pointing(new Int3(0, 0, 1)));

        int upright = Orientations.RotateAroundZ(0, 1);
        foreach (int s in new[] { -1, 1 })
        {
            // All-moving canards beside the cockpit.
            foreach (Chord c in Planform(d, new Int3(4 * s, 2, 0), new Int3(s, 0, 0), -10, -7, "SSS", "FFs"))
                for (int z = c.Lead; z <= c.Trail; z++) d.Set(s * (4 + c.Column), 2, z, "control_surface");

            // Forward-swept wings: the leading edge forward 27°, the trailing edge forward steeply, flaperons along it.
            List<Chord> wing = Planform(d, new Int3(4 * s, 2, 0), new Int3(s, 0, 0), 2, 13, "ttttF", "ddddsssss");
            foreach (Chord c in wing.Where(c => c.Column >= 3)) d.Set(s * (4 + c.Column), 2, c.Trail, "control_surface");

            // Twin fins on the trunks with rudders.
            foreach (Chord c in Planform(d, new Int3(2 * s, 4, 0), Int3.Up, 7, 12, "SSS", "FFF"))
                d.Set(2 * s, 4 + c.Column, c.Trail, "control_surface", upright);

            // Formation lights down the trunk sides behind the wing.
            for (int z = 10; z <= 12; z++) d.Set(4 * s, 2, z, "formation_light", Orient(new Int3(-s, 0, 0), new Int3(0, 0, -1)));
        }

        // And a light strip down the spine behind the canopy.
        for (int z = -6; z <= -3; z++) d.Set(0, 4, z, "formation_light");

        d.Set(0, 0, -11, "landing_gear_steer");
        foreach (int s in new[] { -1, 1 }) d.Set(3 * s, 0, 5, "landing_gear");
        return d.Build("Halcyon (experimental fighter)", "prototype");
    }

    /// <summary>
    /// Seraph: a tailless experimental fighter of the near future, 15 m long with a 16 m span, drawn from the real
    /// tailless research aircraft: no fins at all, a blended body and a lambda wing (leading edge swept back 45°, the
    /// trailing edge cranked), elevons along the whole trailing edge, and two built jets whose vectoring nozzles swing
    /// sideways to turn it. Stealth skin in flat facets, two radar missiles in a belly bay. Stealth grey.
    /// </summary>
    public static Blueprint Seraph()
    {
        var d = new Draft();
        const string skin = "airframe_stealth";
        bool Duct(Int3 c) => Math.Abs(c.X) == 2 && c.Y == 2 && c.Z >= -7 && c.Z <= 0;

        d.Set(0, 2, -15, "nose_cone", Pointing(new Int3(0, 0, -1)));
        d.Set(0, 2, -10, "radar");
        // Two seats: the pilot, and a weapons officer behind under the same long canopy.
        d.Set(0, 3, -9, "pilot_seat");
        d.Set(0, 3, -6, "gunner_seat");
        d.Set(0, 4, -8, "canopy_tandem");
        WeaponsBay(d, 1, skin);

        foreach (int s in new[] { -1, 1 })
        {
            int x = 2 * s;
            d.Set(x, 2, 1, "jet_intake_ramp");
            for (int z = 2; z <= 6; z++) d.Set(x, 2, z, "jet_compressor");
            d.Set(x, 2, 7, "jet_core");
            for (int z = 8; z <= 10; z++) d.Set(x, 2, z, "jet_turbine");
            d.Set(x, 2, 11, "jet_afterburner");
            d.Set(x, 2, 12, "jet_nozzle_vectoring");
        }

        for (int z = -6; z <= 11; z++) d.Set(0, 2, z, "fuel_tank");
        for (int z = 2; z <= 11; z++)
            foreach (int s in new[] { -1, 1 })
                d.Set(s, 2, z, "fuel_tank");

        Fuselage(d, -14, 11, 0, 3, (z, y) =>
            y == 0 ? (z >= -4 && z <= 2 ? 1 : -1)
            : z <= -12 ? (y == 2 ? 1 : -1)
            : z <= -8 ? 1
            : 3, skin, Duct, faceted: true);

        // The lambda wing, elevons along all of its trailing edge.
        foreach (int s in new[] { -1, 1 })
            foreach (Chord c in Planform(d, new Int3(4 * s, 2, 0), new Int3(s, 0, 0), -6, 11, "SSSSSSSSSSSS", "sssTTTTF"))
                if (c.Trail >= c.Lead) d.Set(s * (4 + c.Column), 2, c.Trail, "control_surface");

        d.Set(0, 0, -9, "landing_gear_steer");
        foreach (int s in new[] { -1, 1 }) d.Set(3 * s, 0, 4, "landing_gear");
        return d.Build("Seraph (tailless experimental)", "stealth");
    }
}
