using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DreamGame.Core;

public enum Severity
{
    Error,
    Warning,
    Info,
}

public readonly struct DesignWarning
{
    public readonly Severity Severity;
    public readonly string Text;

    public DesignWarning(Severity severity, string text)
    {
        Severity = severity;
        Text = text;
    }

    public override string ToString() => $"{Severity}: {Text}";
}

public sealed class SectionInfo
{
    public int Id;
    public SectionKind Kind;
    public int Parent = -1;
    public Int3 Root;
    public Int3 Attach;
    public Vector3 Pivot;
    public Vector3 Axis = Vector3.UnitY;
    public int TurretIndex = -1;
    public int GunIndex = -1;
}

public sealed class GunInfo
{
    public Int3 Breech;
    public BlockDef BreechDef;
    public Int3 Forward;
    public Int3 Right;
    public readonly List<Int3> Barrels = new();
    public Int3? Brake;
    public int Loaders;
    public int Propellants;
    public readonly List<Int3> ApRacks = new();
    public readonly List<Int3> HeRacks = new();

    /// <summary>The section the breech sits in (hull or a turret).</summary>
    public int Section;

    /// <summary>The section holding the barrels, which elevates. -1 if the gun has no barrel.</summary>
    public int GunSection = -1;

    public GunStats Stats;
    public int ApRounds;
    public int HeRounds;
    public bool MuzzleBlocked;
    public bool Valid;
    public string Label => $"{BreechDef.Breech.CaliberMm:0} mm";

    /// <summary>Distance from the breech center to the muzzle, in cells.</summary>
    public float MuzzleDistanceCells => Barrels.Count + (Brake.HasValue ? 1 : 0) + 0.5f;
}

/// <summary>How a design gets around, worked out from its parts.</summary>
public enum MobilityMode
{
    Ground,
    Ship,
    Submarine,
}

public sealed class LauncherInfo
{
    public Int3 Cell;
    public Int3 Forward;
    public int Section;
    public readonly List<Int3> Racks = new();
    public int Rounds;
}

public sealed class TurretInfo
{
    public int Section;
    public Int3 Ring;
    public int Blocks;
    public float MassKg;
    public float TraverseDegPerSec;
}

public sealed class WheelInfo
{
    public Int3 Cell;
    public BlockKind Kind;
    public float RadiusM;
    public bool Blocked;
}

public struct ArmorProfile
{
    public float AvgMm;
    public float MinMm;
    public int Samples;
}

/// <summary>
/// Everything the builder needs to know about a design: how it splits into hull, turrets and guns,
/// what it weighs, how fast it goes, how well it's armored, and what's wrong with it.
/// </summary>
public sealed class VehicleAnalysis
{
    public Dictionary<Int3, BlueprintBlock> Cells = new();
    public Dictionary<Int3, BlockDef> Defs = new();
    public Dictionary<Int3, int> SectionOf = new();
    public List<SectionInfo> Sections = new();
    public List<GunInfo> Guns = new();
    public List<TurretInfo> Turrets = new();
    public List<WheelInfo> Wheels = new();
    public List<SeatInfo> Seats = new();

    /// <summary>For each gun (by index), the seat whose occupant aims and fires it.</summary>
    public Dictionary<int, Int3> GunController = new();

    public bool HasCommander;
    public HashSet<Int3> Disconnected = new();
    public List<DesignWarning> Warnings = new();

    public Int3 Min;
    public Int3 Max;
    public float MassKg;
    public Vector3 CenterOfMass;
    public float PowerKw;
    public float FuelL;
    public float TrackFraction;
    public float RollingResistance;
    public float DragArea;
    public float TopSpeedKmh;
    public float PowerToWeight;
    public float TipAngleDeg = 90f;
    public ArmorProfile Front, Rear, LeftSide, RightSide, Top;
    public bool HasDriver;

    public MobilityMode Mode;
    public List<Compartment> Compartments = new();
    public HydroStats Water;
    public List<Int3> ShipPropellers = new();
    public List<Int3> Rudders = new();
    public List<LauncherInfo> TorpedoTubes = new();
    public int Pumps;

    public bool IsNaval => Mode is MobilityMode.Ship or MobilityMode.Submarine;

    public bool CanDrive => HasDriver && FuelL > 0 && Mode switch
    {
        MobilityMode.Ground => PowerKw > 0 && Wheels.Count > 0,
        _ => PowerKw > 0 && ShipPropellers.Count > 0 && Water.Floats,
    };
    public int ErrorCount => Warnings.Count(w => w.Severity == Severity.Error);

    public static VehicleAnalysis Analyze(Blueprint bp) => Analyze(bp.ToCellMap());

    public static VehicleAnalysis Analyze(IReadOnlyDictionary<Int3, BlueprintBlock> cells)
    {
        var a = new VehicleAnalysis();
        foreach (KeyValuePair<Int3, BlueprintBlock> kv in cells)
        {
            if (!BlockCatalog.TryGet(kv.Value.Id, out BlockDef def)) continue;
            a.Cells[kv.Key] = kv.Value;
            a.Defs[kv.Key] = def;
        }

        a.Sections.Add(new SectionInfo { Id = 0, Kind = SectionKind.Hull });
        if (a.Cells.Count == 0)
        {
            a.Warnings.Add(new DesignWarning(Severity.Info,
                "Place blocks to start, or load an example design to see how one is put together."));
            return a;
        }

        a.ComputeBounds();
        a.AssignTurrets();
        foreach (Int3 c in a.Cells.Keys) a.SectionOf.TryAdd(c, 0);
        a.FindGuns();
        a.FindDisconnected();
        a.ComputeMass();
        a.ComputeMobility();
        a.ComputeNaval();
        a.ComputeTurrets();
        a.ComputeArmor();
        a.AssignCrew();
        a.CollectWarnings();
        return a;
    }

    private static IEnumerable<Int3> Sorted(IEnumerable<Int3> cells) =>
        cells.OrderBy(c => c.Y).ThenBy(c => c.Z).ThenBy(c => c.X);

    private void ComputeBounds()
    {
        bool first = true;
        foreach (Int3 c in Cells.Keys)
        {
            if (first)
            {
                Min = Max = c;
                first = false;
            }
            else
            {
                Min = Int3.Min(Min, c);
                Max = Int3.Max(Max, c);
            }
        }
    }

    private bool Is(Int3 c, BlockKind kind) => Defs.TryGetValue(c, out BlockDef d) && d.Kind == kind;

    // Turrets: everything connected to the block on top of an upright ring, at or above that block's height.
    private void AssignTurrets()
    {
        foreach (Int3 ring in Sorted(Defs.Where(kv => kv.Value.Kind == BlockKind.TurretRing).Select(kv => kv.Key)))
        {
            if (SectionOf.ContainsKey(ring)) continue; // a ring inside another turret
            if (Orientations.Up(Cells[ring].O) != Int3.Up)
            {
                Warnings.Add(new DesignWarning(Severity.Warning,
                    $"The turret ring at {ring} is tilted. Rings must face up to turn (rotate it with R/T/G)."));
                continue;
            }

            Int3 root = ring + Int3.Up;
            if (!Cells.ContainsKey(root))
            {
                Warnings.Add(new DesignWarning(Severity.Info,
                    $"The turret ring at {ring} has nothing on top yet. Build the turret on it."));
                continue;
            }

            if (SectionOf.ContainsKey(root)) continue;

            int id = Sections.Count;
            var flood = new List<Int3>();
            var stack = new Stack<Int3>();
            var seen = new HashSet<Int3> { root };
            stack.Push(root);
            bool nested = false;
            while (stack.Count > 0)
            {
                Int3 c = stack.Pop();
                flood.Add(c);
                if (Is(c, BlockKind.TurretRing)) nested = true;
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 n = c + d;
                    if (n.Y < root.Y || n == ring || seen.Contains(n) || !Cells.ContainsKey(n) ||
                        SectionOf.ContainsKey(n)) continue;
                    seen.Add(n);
                    stack.Push(n);
                }
            }

            if (nested)
                Warnings.Add(new DesignWarning(Severity.Warning,
                    "A turret ring sits on top of another turret. Turrets on turrets aren't supported yet; the upper one won't turn."));

            Sections.Add(new SectionInfo
            {
                Id = id, Kind = SectionKind.Turret, Parent = 0, Root = root, Attach = ring,
                Pivot = ring.ToVector3(), Axis = Vector3.UnitY, TurretIndex = Turrets.Count,
            });
            Turrets.Add(new TurretInfo { Section = id, Ring = ring });
            foreach (Int3 c in flood) SectionOf[c] = id;
        }
    }

    private void FindGuns()
    {
        var claimed = new HashSet<Int3>();
        foreach (Int3 b in Sorted(Defs.Where(kv => kv.Value.Kind == BlockKind.Breech).Select(kv => kv.Key)))
        {
            BlockDef def = Defs[b];
            int o = Cells[b].O;
            var gun = new GunInfo
            {
                Breech = b, BreechDef = def, Forward = Orientations.Forward(o), Right = Orientations.Right(o),
                Section = SectionOf[b],
            };
            Guns.Add(gun);

            if (gun.Forward.Y != 0)
            {
                Warnings.Add(new DesignWarning(Severity.Error,
                    $"The {gun.Label} breech at {b} points up or down. Guns must face sideways; rotate it with T or G."));
                continue;
            }

            Int3 c = b + gun.Forward;
            while (Is(c, BlockKind.Barrel) && !claimed.Contains(c))
            {
                gun.Barrels.Add(c);
                claimed.Add(c);
                c += gun.Forward;
            }

            if (Is(c, BlockKind.MuzzleBrake) && !claimed.Contains(c) && gun.Barrels.Count > 0)
            {
                gun.Brake = c;
                claimed.Add(c);
                c += gun.Forward;
            }

            gun.MuzzleBlocked = Cells.ContainsKey(c);

            if (gun.Barrels.Count == 0)
            {
                Warnings.Add(new DesignWarning(Severity.Error,
                    $"The {gun.Label} breech at {b} has no barrel. Put Barrel Segments in a straight line in front of it (the arrow side)."));
            }
            else
            {
                int id = Sections.Count;
                Sections.Add(new SectionInfo
                {
                    Id = id, Kind = SectionKind.Gun, Parent = gun.Section, Root = gun.Barrels[0], Attach = b,
                    Pivot = b.ToVector3(), Axis = gun.Right.ToVector3(), GunIndex = Guns.Count - 1,
                });
                gun.GunSection = id;
                foreach (Int3 bc in gun.Barrels) SectionOf[bc] = id;
                if (gun.Brake.HasValue) SectionOf[gun.Brake.Value] = id;
            }

            // Loaders and propellant must touch the breech and turn with it.
            foreach (Int3 d in Int3.Directions)
            {
                Int3 n = b + d;
                if (d == gun.Forward || !Defs.TryGetValue(n, out BlockDef nd) || SectionOf[n] != gun.Section) continue;
                if (nd.Kind == BlockKind.Loader) gun.Loaders++;
                else if (nd.Kind == BlockKind.Propellant) gun.Propellants++;
            }

            // Ammo reaches the breech through racks, feeds and turret rings.
            var seen = new HashSet<Int3>();
            var stack = new Stack<Int3>();
            foreach (Int3 d in Int3.Directions)
            {
                Int3 n = b + d;
                if (d == gun.Forward || !Defs.TryGetValue(n, out BlockDef nd) || !nd.ConductsAmmo) continue;
                if (seen.Add(n)) stack.Push(n);
            }

            while (stack.Count > 0)
            {
                Int3 n = stack.Pop();
                BlockDef nd = Defs[n];
                if (nd.Kind == BlockKind.AmmoRack)
                {
                    if (nd.Ammo == AmmoType.AP) gun.ApRacks.Add(n);
                    else if (nd.Ammo == AmmoType.HE) gun.HeRacks.Add(n);
                }
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 m = n + d;
                    if (seen.Contains(m) || !Defs.TryGetValue(m, out BlockDef md) || !md.ConductsAmmo) continue;
                    seen.Add(m);
                    stack.Push(m);
                }
            }

            gun.Stats = GunMath.Compute(def.Breech, gun.Barrels.Count, gun.Brake.HasValue, gun.Loaders, gun.Propellants);
            int units = def.Breech.ShellUnits;
            gun.ApRounds = gun.ApRacks.Sum(r => Defs[r].AmmoUnits) / units;
            gun.HeRounds = gun.HeRacks.Sum(r => Defs[r].AmmoUnits) / units;
            gun.Valid = gun.Barrels.Count > 0;
        }
    }

    private bool Joined(Int3 a, Int3 b)
    {
        int sa = SectionOf[a], sb = SectionOf[b];
        if (sa == sb) return true;
        SectionInfo A = Sections[sa], B = Sections[sb];
        return (A.Root == a && A.Attach == b) || (B.Root == b && B.Attach == a);
    }

    private void FindDisconnected()
    {
        var comps = new List<List<Int3>>();
        var visited = new HashSet<Int3>();
        foreach (Int3 start in Sorted(Cells.Keys))
        {
            if (!visited.Add(start)) continue;
            var comp = new List<Int3>();
            var stack = new Stack<Int3>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                Int3 c = stack.Pop();
                comp.Add(c);
                foreach (Int3 d in Int3.Directions)
                {
                    Int3 n = c + d;
                    if (visited.Contains(n) || !Cells.ContainsKey(n) || !Joined(c, n)) continue;
                    visited.Add(n);
                    stack.Push(n);
                }
            }

            comps.Add(comp);
        }

        int keep = comps.FindIndex(c => c.Any(x => Defs[x].Kind == BlockKind.DriverSeat));
        if (keep < 0)
        {
            float best = -1;
            for (int i = 0; i < comps.Count; i++)
            {
                float m = comps[i].Sum(x => Defs[x].MassKg);
                if (m <= best) continue;
                best = m;
                keep = i;
            }
        }

        for (int i = 0; i < comps.Count; i++)
            if (i != keep)
                foreach (Int3 c in comps[i]) Disconnected.Add(c);
    }

    private void ComputeMass()
    {
        Vector3 sum = Vector3.Zero;
        foreach (KeyValuePair<Int3, BlockDef> kv in Defs)
        {
            MassKg += kv.Value.MassKg;
            sum += kv.Key.ToVector3() * kv.Value.MassKg;
            PowerKw += kv.Value.PowerKw;
            FuelL += kv.Value.FuelL;
            if (kv.Value.Kind == BlockKind.DriverSeat) HasDriver = true;
        }

        CenterOfMass = MassKg > 0 ? sum / MassKg * Blueprint.CellSize : Vector3.Zero;
        PowerToWeight = MassKg > 0 ? PowerKw / (MassKg / 1000f) : 0;
    }

    private void ComputeMobility()
    {
        int tracks = 0;
        foreach (Int3 c in Sorted(Defs.Where(kv => kv.Value.IsWheelLike).Select(kv => kv.Key)))
        {
            if (SectionOf[c] != 0)
            {
                Warnings.Add(new DesignWarning(Severity.Warning,
                    $"The {Defs[c].Name} at {c} is on a turret or gun, so it won't touch the ground."));
                continue;
            }

            BlockDef d = Defs[c];
            var w = new WheelInfo { Cell = c, Kind = d.Kind, RadiusM = d.WheelRadiusM, Blocked = Cells.ContainsKey(c + Int3.Down) };
            Wheels.Add(w);
            if (d.Kind == BlockKind.Track) tracks++;
        }

        TrackFraction = Wheels.Count > 0 ? tracks / (float)Wheels.Count : 0;
        RollingResistance = Mobility.RollingResistance(TrackFraction);
        float cs = Blueprint.CellSize;
        float width = (Max.X - Min.X + 1) * cs;
        float height = (Max.Y - Min.Y + 1) * cs;
        DragArea = width * height * 0.8f;
        TopSpeedKmh = FuelL > 0 && Wheels.Count > 0
            ? Mobility.TopSpeed(MassKg, PowerKw, RollingResistance, DragArea, Mobility.GovernorKmh(TrackFraction)) * 3.6f
            : 0;

        if (Wheels.Count > 0)
        {
            int minX = Wheels.Min(w => w.Cell.X), maxX = Wheels.Max(w => w.Cell.X);
            float halfWidth = ((maxX - minX) * 0.5f + 0.5f) * cs;
            WheelInfo low = Wheels.OrderBy(w => w.Cell.Y).First();
            float groundY = low.Cell.Y * cs - (low.RadiusM + Mobility.RestLength(low.Kind) * 0.6f);
            float comHeight = MathF.Max(CenterOfMass.Y - groundY, 0.05f);
            TipAngleDeg = MathF.Atan(halfWidth / comHeight) * 180f / MathF.PI;
        }
    }

    private void ComputeTurrets()
    {
        foreach (TurretInfo t in Turrets)
        {
            foreach (KeyValuePair<Int3, int> kv in SectionOf)
            {
                int s = kv.Value;
                bool inTurret = s == t.Section || (Sections[s].Kind == SectionKind.Gun && Sections[s].Parent == t.Section);
                if (!inTurret) continue;
                t.Blocks++;
                t.MassKg += Defs[kv.Key].MassKg;
            }

            t.TraverseDegPerSec = Mobility.TraverseDegPerSec(t.MassKg);
        }
    }

    // Line-of-sight armor in front of the vehicle's parts, sampled with 4 rays per cell.
    private void ComputeArmor()
    {
        var all = new BlueprintShapeLookup(Cells);
        var lookup = new FilteredShapeLookup(all, c =>
            Defs.TryGetValue(c, out BlockDef d) &&
            !(d.IsWheelLike || d.Kind == BlockKind.Barrel || d.Kind == BlockKind.MuzzleBrake));

        // Shells hitting the front travel backward (+Z), and so on.
        Front = Profile(lookup, Int3.Back);
        Rear = Profile(lookup, Int3.Forward);
        LeftSide = Profile(lookup, Int3.Right);
        RightSide = Profile(lookup, Int3.Left);
        Top = Profile(lookup, Int3.Down);
    }

    private static int Axis(Int3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static Vector3 Compose(int axis, float a, int ua, float u, int va, float v)
    {
        var r = new float[3];
        r[axis] = a;
        r[ua] = u;
        r[va] = v;
        return new Vector3(r[0], r[1], r[2]);
    }

    /// <summary>
    /// Sums the armor a shell must get through before reaching any part that isn't armor or frame, over a grid
    /// of rays (4 per cell) traveling in <paramref name="travel"/>. Rays that only meet armor are skipped.
    /// </summary>
    private ArmorProfile Profile(ICellShapeLookup lookup, Int3 travel)
    {
        int axis = travel.X != 0 ? 0 : travel.Y != 0 ? 1 : 2;
        int ua = (axis + 1) % 3, va = (axis + 2) % 3;
        bool positive = Axis(travel, axis) > 0;
        float start = positive ? Axis(Min, axis) - 2 : Axis(Max, axis) + 2;
        float span = Axis(Max, axis) - Axis(Min, axis) + 5;
        Vector3 dir = travel.ToVector3();
        float[] offsets = { -0.25f, 0.25f };

        var hits = new List<RayCellHit>();
        float total = 0, lowest = float.MaxValue;
        int samples = 0;
        for (int i = Axis(Min, ua); i <= Axis(Max, ua); i++)
        for (int j = Axis(Min, va); j <= Axis(Max, va); j++)
        foreach (float su in offsets)
        foreach (float sv in offsets)
        {
            Vector3 origin = Compose(axis, start, ua, i + su + 0.001f, va, j + sv + 0.0007f);
            hits.Clear();
            GridRaycast.Cast(lookup, Min, Max, origin, dir, span, hits);
            float sum = 0;
            bool reached = false;
            foreach (RayCellHit h in hits)
            {
                BlockDef d = Defs[h.Cell];
                if (!d.IsArmorLike)
                {
                    reached = true;
                    break;
                }

                sum += DamageModel.EffectiveArmor(d.ArmorMm, DamageModel.ImpactAngleDeg(dir, h.Normal));
            }

            if (!reached) continue;
            total += sum;
            lowest = MathF.Min(lowest, sum);
            samples++;
        }

        return samples == 0
            ? new ArmorProfile()
            : new ArmorProfile { AvgMm = total / samples, MinMm = lowest, Samples = samples };
    }

    /// <summary>
    /// Works out who fires each gun: a gunner in the same turret if there is one, otherwise the nearest
    /// gunner anywhere, otherwise the driver.
    /// </summary>
    private void AssignCrew()
    {
        foreach (Int3 c in Sorted(Defs.Keys))
        {
            SeatRole? role = Crew.RoleOf(Defs[c].Kind);
            if (role == null || Disconnected.Contains(c)) continue;
            Seats.Add(new SeatInfo { Cell = c, Role = role.Value, Section = SectionOf[c] });
        }

        HasCommander = Seats.Any(s => s.Role == SeatRole.Commander);
        SeatInfo driver = Seats.FirstOrDefault(s => s.Role == SeatRole.Driver);
        for (int i = 0; i < Guns.Count; i++)
        {
            GunInfo g = Guns[i];
            if (!g.Valid) continue;
            SeatInfo best = Seats.Where(s => s.Role == SeatRole.Gunner)
                .OrderBy(s => s.Section == g.Section ? 0 : 1)
                .ThenBy(s => (s.Cell - g.Breech).ManhattanLength)
                .FirstOrDefault() ?? driver;
            if (best != null) GunController[i] = best.Cell;
        }
    }

    /// <summary>Ships and submarines: how the hull floats, and its propellers, rudders, pumps and torpedo tubes.</summary>
    private void ComputeNaval()
    {
        foreach (Int3 c in Sorted(Defs.Keys))
        {
            switch (Defs[c].Kind)
            {
                case BlockKind.ShipPropeller: ShipPropellers.Add(c); break;
                case BlockKind.Rudder: Rudders.Add(c); break;
                case BlockKind.BilgePump: Pumps++; break;
                case BlockKind.TorpedoTube: TorpedoTubes.Add(FindLauncher(c, AmmoType.Torpedo, Torpedoes.Units)); break;
            }
        }

        bool anyBallast = Defs.Values.Any(d => d.Kind == BlockKind.BallastValve);
        bool shipParts = ShipPropellers.Count > 0 || anyBallast;
        if (Wheels.Count > 0 && !shipParts)
        {
            Mode = MobilityMode.Ground;
            return;
        }

        var connected = Defs.Where(kv => !Disconnected.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        Compartments = Hydro.FindCompartments(connected);
        Water = Hydro.Analyze(connected, Compartments, MassKg);
        if (!shipParts && !Water.Floats)
        {
            Mode = MobilityMode.Ground;
            return;
        }

        Mode = Compartments.Any(c => c.IsBallast) ? MobilityMode.Submarine : MobilityMode.Ship;
        float cs = Blueprint.CellSize;
        float beam = (Max.X - Min.X + 1) * cs, length = (Max.Z - Min.Z + 1) * cs;
        TopSpeedKmh = FuelL > 0 && ShipPropellers.Count > 0 ? Hydro.TopSpeed(PowerKw, beam, Math.Max(Water.DraftM, 0.3f), length) * 3.6f : 0;
    }

    /// <summary>A launcher and the racks of its ammo that can reach it.</summary>
    private LauncherInfo FindLauncher(Int3 cell, AmmoType ammo, int unitsPerRound)
    {
        var info = new LauncherInfo { Cell = cell, Forward = Orientations.Forward(Cells[cell].O), Section = SectionOf[cell] };
        var seen = new HashSet<Int3>();
        var stack = new Stack<Int3>();
        foreach (Int3 d in Int3.Directions)
        {
            Int3 n = cell + d;
            if (d != info.Forward && Defs.TryGetValue(n, out BlockDef nd) && nd.ConductsAmmo && seen.Add(n)) stack.Push(n);
        }

        while (stack.Count > 0)
        {
            Int3 n = stack.Pop();
            if (Defs[n].Kind == BlockKind.AmmoRack && Defs[n].Ammo == ammo) info.Racks.Add(n);
            foreach (Int3 d in Int3.Directions)
            {
                Int3 m = n + d;
                if (!seen.Contains(m) && Defs.TryGetValue(m, out BlockDef md) && md.ConductsAmmo)
                {
                    seen.Add(m);
                    stack.Push(m);
                }
            }
        }

        info.Rounds = info.Racks.Sum(r => Defs[r].AmmoUnits) / unitsPerRound;
        return info;
    }

    private void CollectWarnings()
    {
        if (!HasDriver)
            Warnings.Add(new DesignWarning(Severity.Error, "No driver seat. Every vehicle needs one to be driven."));
        if (PowerKw <= 0)
            Warnings.Add(new DesignWarning(Severity.Error, "No engine. Add a Small or Large Engine (Mobility)."));
        else if (FuelL <= 0)
            Warnings.Add(new DesignWarning(Severity.Error, "No fuel tank. The engines can't run without one."));
        if (Mode == MobilityMode.Ground && Wheels.Count == 0)
            Warnings.Add(new DesignWarning(Severity.Error,
                "No wheels or tracks. Put them on the bottom row with nothing underneath (or Ship Propellers, to make it a boat)."));
        if (IsNaval) NavalWarnings();

        int blocked = Wheels.Count(w => w.Blocked);
        if (blocked > 0)
            Warnings.Add(new DesignWarning(Severity.Warning,
                $"{blocked} wheel/track block(s) have a block underneath, so they can't reach the ground."));

        int left = Wheels.Count(w => w.Kind == BlockKind.Track && w.Cell.X * Blueprint.CellSize < CenterOfMass.X - 0.01f);
        int right = Wheels.Count(w => w.Kind == BlockKind.Track && w.Cell.X * Blueprint.CellSize > CenterOfMass.X + 0.01f);
        if ((left > 0) != (right > 0))
            Warnings.Add(new DesignWarning(Severity.Warning,
                "Tracks are only on one side. Tanks steer by driving each side separately, so put tracks on both sides."));

        foreach (GunInfo g in Guns.Where(g => g.Valid))
        {
            if (g.ApRounds + g.HeRounds == 0)
                Warnings.Add(new DesignWarning(Severity.Warning,
                    $"The {g.Label} gun has no ammo. Put an AP or HE Ammo Rack touching the breech, or connect racks to it with Ammo Feeds."));
            if (g.MuzzleBlocked)
                Warnings.Add(new DesignWarning(Severity.Warning,
                    $"The {g.Label} gun's muzzle is blocked by a block right in front of it. It will shoot itself."));
        }

        if (Guns.Any(g => g.Valid) && HasDriver && Seats.All(s => s.Role != SeatRole.Gunner))
            Warnings.Add(new DesignWarning(Severity.Info,
                "No gunner seat, so the driver aims the guns. Add a Gunner Seat and an AI gunner will shoot while you drive."));

        if (Disconnected.Count > 0)
            Warnings.Add(new DesignWarning(Severity.Warning,
                $"{Disconnected.Count} block(s) aren't attached to the vehicle (shown in red) and will fall off."));

        if (Mode == MobilityMode.Ground && Wheels.Count > 0 && TipAngleDeg < 30f)
            Warnings.Add(new DesignWarning(Severity.Warning,
                $"Top-heavy: it may tip over on slopes steeper than about {TipAngleDeg:0}°. Move heavy parts lower or make it wider."));

        if (Mode == MobilityMode.Ground && PowerKw > 0 && MassKg > 0 && PowerToWeight < 8f)
            Warnings.Add(new DesignWarning(Severity.Warning,
                $"Underpowered: {PowerToWeight:0.0} kW per tonne. It will crawl; aim for 10 or more."));
    }

    private void NavalWarnings()
    {
        float cs = Blueprint.CellSize;
        if (!Water.Floats)
            Warnings.Add(new DesignWarning(Severity.Error,
                $"It sinks: it weighs {MassKg / 1000f:0.0} t but can only keep {Water.DisplacementT:0.0} t afloat. " +
                "Seal more empty space inside the hull (closed compartments are what float), or use lighter armor."));
        else if (Water.FreeboardM < 0.5f)
            Warnings.Add(new DesignWarning(Severity.Warning,
                $"Only {Water.FreeboardM:0.0} m of hull above the waterline. Waves of damage will swamp it; make the hull taller or lighter."));

        if (ShipPropellers.Count == 0)
            Warnings.Add(new DesignWarning(Severity.Error,
                "No ship propeller. Add Ship Propellers below the waterline (Sea); they push the way their arrow points."));
        int dry = ShipPropellers.Count(c => c.Y * cs > Water.WaterlineY);
        if (dry > 0 && Mode == MobilityMode.Ship)
            Warnings.Add(new DesignWarning(Severity.Warning,
                $"{dry} propeller(s) sit above the waterline and won't push. Move them lower."));
        if (Rudders.Count == 0 && ShipPropellers.Count > 0)
            Warnings.Add(new DesignWarning(Severity.Info, "No rudder: it will turn slowly. Put Rudders behind the propellers."));
        if (Compartments.Count == 0)
            Warnings.Add(new DesignWarning(Severity.Warning,
                "No sealed compartments: one hole anywhere and it floods. Close the hull and add a few inner walls."));
        else if (Pumps == 0)
            Warnings.Add(new DesignWarning(Severity.Info, "No bilge pumps: once water gets in, it stays in."));

        if (Mode == MobilityMode.Submarine)
        {
            if (!Water.CanDive)
                Warnings.Add(new DesignWarning(Severity.Warning,
                    $"Even with the ballast flooded it stays afloat. Add {Math.Max(0.1f, Water.DisplacementT - Water.BallastT - MassKg / 1000f):0.0} t of Lead Ballast or make the ballast tanks bigger."));
            if (!Water.CanSurface)
                Warnings.Add(new DesignWarning(Severity.Error, "It can't surface even with the ballast blown out. Make it lighter."));
        }

        foreach (LauncherInfo t in TorpedoTubes.Where(t => t.Rounds == 0))
            Warnings.Add(new DesignWarning(Severity.Warning,
                $"The torpedo tube at {t.Cell} has no torpedoes. Put a Torpedo Rack touching it, or connect one with Ammo Feeds."));
    }
}
