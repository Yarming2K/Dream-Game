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
    public readonly List<Int3> FlakRacks = new();

    /// <summary>The section the breech sits in (hull or a turret).</summary>
    public int Section;

    /// <summary>The section holding the barrels, which elevates. -1 if the gun has no barrel.</summary>
    public int GunSection = -1;

    public GunStats Stats;
    public int ApRounds;
    public int HeRounds;
    public int FlakRounds;
    public bool MuzzleBlocked;
    public bool Valid;
    public string Label => BreechDef.Breech.Label ?? $"{BreechDef.Breech.CaliberMm:0} mm";

    /// <summary>Railguns and lasers: they need the vehicle's electricity to fire.</summary>
    public bool NeedsPower => BreechDef.Breech.ChargeMJ > 0;

    /// <summary>Distance from the breech center to the muzzle, in cells.</summary>
    public float MuzzleDistanceCells => Barrels.Count + (Brake.HasValue ? 1 : 0) + 0.5f;
}

/// <summary>How a design gets around, worked out from its parts.</summary>
public enum MobilityMode
{
    Ground,
    Ship,
    Submarine,
    Plane,
    Helicopter,

    /// <summary>VTOL and drones: held up by lift fans or jets pointing down.</summary>
    Hover,
    Airship,

    /// <summary>A building: set on a foundation, it stays where it's put.</summary>
    Building,
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
    /// <summary>Every filled cell and the block filling it (a big block is listed under each of its cells).</summary>
    public Dictionary<Int3, BlueprintBlock> Cells = new();
    public Dictionary<Int3, BlockDef> Defs = new();
    public Dictionary<Int3, int> SectionOf = new();

    /// <summary>The cell each block was placed on: one per block, however big.</summary>
    public HashSet<Int3> Anchors = new();

    public bool IsAnchor(Int3 c) => Anchors.Contains(c);
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

    /// <summary>Electricity made aboard (generators and reactors), in kW, and the part of it that burns fuel.</summary>
    public float ElectricKw;
    public float GeneratorKw;

    /// <summary>Charge the vehicle can hold for energy weapons, in MJ (capacitors and the sources' own buffers).</summary>
    public float StorageMJ;

    /// <summary>The working fittings aboard: periscopes, antennas, toolboxes and so on.</summary>
    public FittingCounts Kit;
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

    public List<SurfaceInfo> Surfaces = new();
    public List<ThrusterInfo> Thrusters = new();
    public List<MissileInfo> Missiles = new();
    public List<Int3> BombRacks = new();
    public int GasCells;
    public AirStats Air;

    public bool IsNaval => Mode is MobilityMode.Ship or MobilityMode.Submarine;
    public bool IsAir => Mode is MobilityMode.Plane or MobilityMode.Helicopter or MobilityMode.Hover or MobilityMode.Airship;
    public int Jets => Thrusters.Count(t => t.Kind == BlockKind.JetEngine);

    /// <summary>A building on a foundation.</summary>
    public bool IsBuilding => Mode == MobilityMode.Building;

    public bool CanDrive => IsBuilding || HasDriver && FuelL > 0 && Mode switch
    {
        MobilityMode.Ground => PowerKw > 0 && Wheels.Count > 0,
        MobilityMode.Ship or MobilityMode.Submarine => PowerKw > 0 && ShipPropellers.Count > 0 && Water.Floats,
        _ => (PowerKw > 0 || Jets > 0) && Air.CanFly,
    };

    public static string ModeName(MobilityMode m) => m switch
    {
        MobilityMode.Ground => "Land vehicle",
        MobilityMode.Ship => "Ship",
        MobilityMode.Submarine => "Submarine",
        MobilityMode.Plane => "Aircraft",
        MobilityMode.Helicopter => "Helicopter",
        MobilityMode.Hover => "VTOL / drone",
        MobilityMode.Building => "Building",
        _ => "Airship",
    };
    public int ErrorCount => Warnings.Count(w => w.Severity == Severity.Error);

    public static VehicleAnalysis Analyze(Blueprint bp) => Analyze(bp.ToCellMap());


    public static VehicleAnalysis Analyze(IReadOnlyDictionary<Int3, BlueprintBlock> cells)
    {
        var a = new VehicleAnalysis();
        // Lay out every block's footprint (the map may list a big block under one cell or under all of them).
        var placed = new HashSet<BlueprintBlock>();
        foreach (BlueprintBlock bb in cells.Values.OrderBy(b => b.Y).ThenBy(b => b.Z).ThenBy(b => b.X))
        {
            if (!placed.Add(bb) || !BlockCatalog.TryGet(bb.Id, out BlockDef def)) continue;
            Int3[] fp = Footprint.CellArray(def, bb.Cell, bb.O);
            if (fp.Any(a.Cells.ContainsKey)) continue;
            foreach (Int3 c in fp)
            {
                a.Cells[c] = bb;
                a.Defs[c] = def;
            }

            a.Anchors.Add(bb.Cell);
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
        a.FindMissiles();
        a.FindDisconnected();
        a.Kit = Fittings.Count(a.Anchors.Where(c => !a.Disconnected.Contains(c)).Select(c => a.Defs[c]));
        a.ComputeMass();
        a.ComputeMobility();
        a.ComputeNaval();
        a.ComputeAir();
        a.ComputeTurrets();
        a.ComputeArmor();
        a.AssignCrew();
        a.CollectWarnings();
        return a;
    }

    private static IEnumerable<Int3> Sorted(IEnumerable<Int3> cells) =>
        cells.OrderBy(c => c.Y).ThenBy(c => c.Z).ThenBy(c => c.X);

    /// <summary>The anchor cells of every block of one kind, in a stable order.</summary>
    private IEnumerable<Int3> AnchorsOf(BlockKind kind) => Sorted(Anchors.Where(c => Defs[c].Kind == kind));

    /// <summary>The cells next to a block (touching any of its faces), not counting its own cells.</summary>
    private IEnumerable<(Int3 cell, Int3 dir)> Around(Int3 anchor)
    {
        BlueprintBlock self = Cells[anchor];
        foreach (Int3 c in Footprint.Cells(Defs[anchor], anchor, self.O))
        foreach (Int3 d in Int3.Directions)
        {
            Int3 n = c + d;
            if (!Cells.TryGetValue(n, out BlueprintBlock other) || other != self) yield return (n, d);
        }
    }

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
        foreach (Int3 ring in AnchorsOf(BlockKind.TurretRing))
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
        foreach (Int3 b in AnchorsOf(BlockKind.Breech))
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

            // Barrels start at the breech's front face (a big breech reaches further forward). Railguns take
            // rails, lasers lenses and rocket launchers tubes, instead of ordinary barrels.
            string tag = def.Breech.BarrelTag;
            Int3 c = b + gun.Forward * (Footprint.Reach(def, o, gun.Forward) + 1);
            Int3 first = c;
            while (Is(c, BlockKind.Barrel) && Defs[c].BarrelTag == tag && !claimed.Contains(c))
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
                string wrong = Is(first, BlockKind.Barrel) ? $" The {Defs[first].Name} in front of it doesn't fit." : "";
                string need = tag switch
                {
                    "rail" => $"The railgun at {b} has no rails. Put Rail Segments in a straight line in front of it (the arrow side).",
                    "lens" => $"The laser at {b} has no lens. Put a Focusing Lens or two in front of it (the arrow side).",
                    "tube" => $"The rocket launcher at {b} has no tubes. Put Launch Tubes in a line in front of it (the arrow side).",
                    _ => $"The {gun.Label} breech at {b} has no barrel. Put Barrel Segments in a straight line in front of it (the arrow side).",
                };
                Warnings.Add(new DesignWarning(Severity.Error, need + wrong));
            }
            else
            {
                int id = Sections.Count;
                // The barrels hang from the cell of the breech right behind the first one (its front face).
                Sections.Add(new SectionInfo
                {
                    Id = id, Kind = SectionKind.Gun, Parent = gun.Section, Root = gun.Barrels[0], Attach = gun.Barrels[0] - gun.Forward,
                    Pivot = b.ToVector3(), Axis = gun.Right.ToVector3(), GunIndex = Guns.Count - 1,
                });
                gun.GunSection = id;
                foreach (Int3 bc in gun.Barrels) SectionOf[bc] = id;
                if (gun.Brake.HasValue) SectionOf[gun.Brake.Value] = id;
            }

            // Loaders and propellant must touch the breech and turn with it.
            var counted = new HashSet<Int3>();
            foreach ((Int3 n, Int3 d) in Around(b))
            {
                if (d == gun.Forward || !Defs.TryGetValue(n, out BlockDef nd) || SectionOf[n] != gun.Section) continue;
                if (!counted.Add(Cells[n].Cell)) continue;
                if (nd.Kind == BlockKind.Loader) gun.Loaders++;
                else if (nd.Kind == BlockKind.Propellant) gun.Propellants++;
            }

            // Ammo reaches the breech through racks, feeds and turret rings.
            var seen = new HashSet<Int3>();
            var stack = new Stack<Int3>();
            foreach ((Int3 n, Int3 d) in Around(b))
            {
                if (d == gun.Forward || !Defs.TryGetValue(n, out BlockDef nd) || !nd.ConductsAmmo) continue;
                if (seen.Add(n)) stack.Push(n);
            }

            var racks = new HashSet<Int3>();
            while (stack.Count > 0)
            {
                Int3 n = stack.Pop();
                BlockDef nd = Defs[n];
                Int3 rack = Cells[n].Cell;
                if (nd.Kind == BlockKind.AmmoRack && racks.Add(rack))
                {
                    if (nd.Ammo == AmmoType.AP) gun.ApRacks.Add(rack);
                    else if (nd.Ammo == AmmoType.HE) gun.HeRacks.Add(rack);
                    else if (nd.Ammo == AmmoType.Flak) gun.FlakRacks.Add(rack);
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
            gun.FlakRounds = gun.FlakRacks.Sum(r => Defs[r].AmmoUnits) / units;
            // Railguns fire solid slugs only, rockets are all high explosive, and lasers need no ammo at all.
            if (def.Breech.Rail || def.Breech.Beam) gun.HeRounds = gun.FlakRounds = 0;
            if (def.Breech.Rocket) gun.ApRounds = gun.FlakRounds = 0;
            if (def.Breech.AntiAir) gun.ApRounds = gun.HeRounds = 0;
            if (def.Breech.Beam) gun.ApRounds = 0;
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

        int keep = comps.FindIndex(c => c.Any(x => Defs[x].Kind is BlockKind.DriverSeat or BlockKind.DroneCore));
        if (keep < 0)
        {
            float best = -1;
            for (int i = 0; i < comps.Count; i++)
            {
                float m = comps[i].Sum(x => Defs[x].MassKg / Defs[x].Volume);
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
        // A big block's mass, power and fuel are spread evenly over its cells.
        foreach (KeyValuePair<Int3, BlockDef> kv in Defs)
        {
            float share = 1f / kv.Value.Volume;
            MassKg += kv.Value.MassKg * share;
            sum += kv.Key.ToVector3() * kv.Value.MassKg * share;
            PowerKw += kv.Value.PowerKw * share;
            FuelL += kv.Value.FuelL * share;
            ElectricKw += kv.Value.ElectricKw * share;
            if (kv.Value.Kind == BlockKind.Generator) GeneratorKw += kv.Value.ElectricKw * share;
            StorageMJ += kv.Value.StoreMJ * share;
            if (kv.Value.Kind is BlockKind.DriverSeat or BlockKind.DroneCore) HasDriver = true;
        }

        StorageMJ = Power.CapacityMJ(StorageMJ, ElectricKw);
        CenterOfMass = MassKg > 0 ? sum / MassKg * Blueprint.CellSize : Vector3.Zero;
        PowerToWeight = MassKg > 0 ? PowerKw / (MassKg / 1000f) : 0;
    }

    private void ComputeMobility()
    {
        int tracks = 0;
        foreach (Int3 c in Sorted(Anchors.Where(a => Defs[a].IsWheelLike)))
        {
            if (SectionOf[c] != 0)
            {
                Warnings.Add(new DesignWarning(Severity.Warning,
                    $"The {Defs[c].Name} at {c} is on a turret or gun, so it won't touch the ground."));
                continue;
            }

            BlockDef d = Defs[c];
            Int3[] fp = Footprint.CellArray(d, c, Cells[c].O);
            int bottom = fp.Min(x => x.Y);
            bool blocked = fp.Any(x => x.Y == bottom && Cells.ContainsKey(x + Int3.Down));
            var w = new WheelInfo { Cell = c, Kind = d.Kind, RadiusM = d.WheelRadiusM, Blocked = blocked };
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
                if (IsAnchor(kv.Key)) t.Blocks++;
                t.MassKg += Defs[kv.Key].MassKg / Defs[kv.Key].Volume;
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
        // Big designs are sampled more sparsely so the estimate stays quick (a few thousand rays at most).
        int faceCells = (Axis(Max, ua) - Axis(Min, ua) + 1) * (Axis(Max, va) - Axis(Min, va) + 1);
        int stride = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(faceCells / 1500f)));
        float[] offsets = stride == 1 ? new[] { -0.25f, 0.25f } : new[] { 0.1f };

        var hits = new List<RayCellHit>();
        float total = 0, lowest = float.MaxValue;
        int samples = 0;
        for (int i = Axis(Min, ua); i <= Axis(Max, ua); i += stride)
        for (int j = Axis(Min, va); j <= Axis(Max, va); j += stride)
        foreach (float su in offsets)
        foreach (float sv in offsets)
        {
            Vector3 origin = Compose(axis, start, ua, i + su + 0.001f, va, j + sv + 0.0007f);
            hits.Clear();
            GridRaycast.Cast(lookup, Min, Max, origin, dir, span, hits);
            float sum = 0;
            bool reached = false;
            BlueprintBlock last = null;
            foreach (RayCellHit h in hits)
            {
                BlockDef d = Defs[h.Cell];
                if (!d.IsArmorLike)
                {
                    reached = true;
                    break;
                }

                // A big plate counts once along a ray, however many of its cells the ray crosses.
                if (Cells[h.Cell] == last) continue;
                last = Cells[h.Cell];
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
        foreach (Int3 c in Sorted(Anchors))
        {
            SeatRole? role = Crew.RoleOf(Defs[c].Kind);
            if (role == null || Disconnected.Contains(c)) continue;
            Seats.Add(new SeatInfo { Cell = c, Role = role.Value, Section = SectionOf[c], Remote = Defs[c].Kind == BlockKind.DroneCore });
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
        foreach (Int3 c in Sorted(Anchors))
        {
            switch (Defs[c].Kind)
            {
                case BlockKind.ShipPropeller: ShipPropellers.Add(c); break;
                case BlockKind.Rudder: Rudders.Add(c); break;
                case BlockKind.BilgePump: Pumps++; break;
                case BlockKind.TorpedoTube: TorpedoTubes.Add(FindLauncher(c, AmmoType.Torpedo, Torpedoes.Units)); break;
            }
        }

        // A foundation on the bottom row makes it a building, whatever else it has.
        if (Anchors.Any(c => Defs[c].Kind == BlockKind.Foundation && c.Y == Min.Y && !Disconnected.Contains(c)))
        {
            Mode = MobilityMode.Building;
            return;
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
        foreach ((Int3 n, Int3 d) in Around(cell))
        {
            if (d != info.Forward && Defs.TryGetValue(n, out BlockDef nd) && nd.ConductsAmmo && seen.Add(n)) stack.Push(n);
        }

        var racks = new HashSet<Int3>();
        while (stack.Count > 0)
        {
            Int3 n = stack.Pop();
            if (Defs[n].Kind == BlockKind.AmmoRack && Defs[n].Ammo == ammo && racks.Add(Cells[n].Cell)) info.Racks.Add(Cells[n].Cell);
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

    /// <summary>Missiles: the line of missile parts in front of each rail.</summary>
    private void FindMissiles()
    {
        foreach (Int3 rail in AnchorsOf(BlockKind.MissileRail))
        {
            var m = new MissileInfo { Rail = rail, Forward = Orientations.Forward(Cells[rail].O), Section = SectionOf[rail] };
            Missiles.Add(m);
            Int3 c = rail + m.Forward;
            while (Defs.TryGetValue(c, out BlockDef d) && d.IsMissilePart)
            {
                m.Parts.Add(c);
                c += m.Forward;
            }

            var defs = m.Parts.Select(p => Defs[p]).ToList();
            m.Stats = Aero.Missile(defs);
            int seekers = defs.Count(d => d.Kind == BlockKind.MissileSeeker);
            if (m.Parts.Count == 0)
                m.Problem = $"The missile rail at {rail} is empty. Line up a Rocket Motor and a Warhead in front of it (the arrow side), with a seeker on the nose.";
            else if (defs.All(d => d.Kind != BlockKind.MissileMotor))
                m.Problem = $"The missile on the rail at {rail} has no Rocket Motor.";
            else if (defs.All(d => d.Kind != BlockKind.MissileWarhead))
                m.Problem = $"The missile on the rail at {rail} has no Warhead.";
            else if (seekers > 1)
                m.Problem = $"The missile on the rail at {rail} has {seekers} seekers. It only needs one, on the nose.";
            else if (seekers == 1 && defs[^1].Kind != BlockKind.MissileSeeker)
                m.Problem = $"The seeker on the missile at {rail} must be the last part, on the nose.";
            else
                m.Valid = true;

            if (m.Valid && m.Stats.Seeker != SeekerKind.None && defs.All(d => d.Kind != BlockKind.MissileFins))
                m.Problem = $"The guided missile at {rail} has no fins, so it can't steer. Add Missile Fins.";
        }

        // Vertical launch cells: the missile is inside, and it leaves through the hatch on top.
        foreach (Int3 cell in AnchorsOf(BlockKind.VlsCell))
        {
            BlockDef d = Defs[cell];
            Int3 up = Orientations.Up(Cells[cell].O);
            var m = new MissileInfo
            {
                Rail = cell, Forward = up, Section = SectionOf[cell], Vls = d.Vls, Stats = Aero.VlsMissile(d.Vls), Valid = true,
            };
            Missiles.Add(m);
            if (up != Int3.Up)
            {
                m.Valid = false;
                m.Problem = $"The VLS cell at {cell} is on its side. It must stand upright, hatch on top (rotate it with T or G).";
            }
            else if (Cells.ContainsKey(cell + up))
            {
                m.Valid = false;
                m.Problem = $"The VLS cell at {cell} has something on its hatch, so its missile can't get out.";
            }
        }
    }

    /// <summary>
    /// Aircraft: lifting surfaces and what each control surface does, propellers, jets, rotors, fans and
    /// gas, and from those whether it's a plane, a helicopter, a VTOL or drone, or an airship.
    /// </summary>
    private void ComputeAir()
    {
        float cs = Blueprint.CellSize;
        float gasLift = 0;
        foreach (Int3 c in Sorted(Anchors))
        {
            if (Disconnected.Contains(c)) continue;
            BlockDef d = Defs[c];
            int o = Cells[c].O;
            gasLift += d.LiftKg;
            switch (d.Kind)
            {
                case BlockKind.Wing:
                case BlockKind.ControlSurface:
                {
                    Int3 n = Orientations.Up(o);
                    var info = new SurfaceInfo { Cell = c, Orientation = o, Control = d.Kind == BlockKind.ControlSurface, Role = SurfaceRole.Wing };
                    if (info.Control && Math.Abs(n.Y) == 1)
                    {
                        float dx = MathF.Abs(c.X * cs - CenterOfMass.X), dz = MathF.Abs(c.Z * cs - CenterOfMass.Z);
                        info.Role = dx > 1.2f && dx > dz * 0.8f ? SurfaceRole.Aileron : SurfaceRole.Elevator;
                    }
                    else if (info.Control && Math.Abs(n.X) == 1)
                    {
                        info.Role = SurfaceRole.Rudder;
                    }

                    Surfaces.Add(info);
                    break;
                }
                case BlockKind.AirPropeller:
                case BlockKind.JetEngine:
                case BlockKind.LiftFan:
                    Thrusters.Add(new ThrusterInfo { Cell = c, Kind = d.Kind, Orientation = o, Direction = Orientations.Forward(o) });
                    break;
                case BlockKind.Rotor:
                    Thrusters.Add(new ThrusterInfo { Cell = c, Kind = d.Kind, Orientation = o, Direction = Orientations.Up(o) });
                    break;
                case BlockKind.GasCell:
                    GasCells += d.Volume;
                    break;
                case BlockKind.BombRack:
                    BombRacks.Add(c);
                    break;
            }
        }

        if (Thrusters.Count == 0 && GasCells == 0 && Surfaces.Count == 0) return;

        var air = new AirStats { GasLiftKg = gasLift, Bombs = BombRacks.Count };
        var flat = Surfaces.Where(s => Math.Abs(Orientations.Up(s.Orientation).Y) == 1).ToList();
        air.WingAreaM2 = flat.Count * Aero.PanelArea;
        air.LiftCenterZ = flat.Count > 0 ? flat.Average(s => s.Cell.Z * cs) : CenterOfMass.Z;
        air.StaticMarginM = air.LiftCenterZ - CenterOfMass.Z;
        // Elevator authority against stability: how far it can hold the nose up.
        float elevator = Surfaces.Where(s => s.Role == SurfaceRole.Elevator)
            .Sum(s => Aero.PanelArea * Aero.LiftSlope * Aero.ControlThrow * MathF.Abs(s.Cell.Z * cs - CenterOfMass.Z));
        float stiffness = air.WingAreaM2 * Aero.LiftSlope * MathF.Max(air.StaticMarginM, 0.02f);
        air.MaxPitchDeg = stiffness > 0 ? MathF.Min(elevator / stiffness, 1f) * 180f / MathF.PI : 0;
        air.HasRoll = Surfaces.Any(s => s.Role == SurfaceRole.Aileron);
        air.HasPitch = Surfaces.Any(s => s.Role == SurfaceRole.Elevator);
        air.HasYaw = Surfaces.Any(s => s.Role == SurfaceRole.Rudder);
        air.StallSpeedMs = Aero.StallSpeed(MassKg, air.WingAreaM2);

        // Engines share their power between everything they turn.
        bool Up(ThrusterInfo t) => t.Direction == Int3.Up;
        var powered = Thrusters.Where(t => t.Kind is BlockKind.AirPropeller or BlockKind.Rotor or BlockKind.LiftFan).ToList();
        var lifters = powered.Where(t => t.Kind == BlockKind.Rotor ? Up(t) : t.Kind == BlockKind.LiftFan && Up(t)).ToList();
        var pushers = powered.Where(t => t.Kind == BlockKind.AirPropeller && t.Direction.Y == 0).ToList();
        float watts = PowerKw * 1000f;
        float liftShare = lifters.Count > 0 ? watts / lifters.Count : 0;
        float pushShare = pushers.Count > 0 ? watts / pushers.Count : 0;
        float upJets = Thrusters.Count(t => t.Kind == BlockKind.JetEngine && Up(t)) * Aero.JetThrustN;
        float fwdJets = Thrusters.Count(t => t.Kind == BlockKind.JetEngine && t.Direction.Y == 0) * Aero.JetThrustN;
        air.HoverThrustN = upJets + lifters.Sum(t =>
            Aero.RotorThrust(liftShare, Aero.DiskArea(t.Kind == BlockKind.Rotor ? Aero.RotorRadiusM : Aero.FanRadiusM)));
        float diskProp = Aero.DiskArea(Aero.PropRadiusM);
        // (Jets at full throttle, with their afterburners lit.)
        float ThrustAt(float v) => fwdJets * Aero.AfterburnerThrust + pushers.Count * Aero.PropThrust(pushShare, v, diskProp);
        air.StaticThrustN = ThrustAt(0);

        // Drag of the body: what it shows end-on, not counting wing panels (they have their own).
        var frontal = new HashSet<(int, int)>();
        foreach ((Int3 c, BlockDef d) in Defs)
            if (!d.IsLiftingSurface && !Disconnected.Contains(c)) frontal.Add((c.X, c.Y));
        // (A big design's frontal area grows with its cross-section, as it should.)
        float bodyDrag = frontal.Count * Aero.PanelArea * 0.35f;

        float weight = MassKg * GunMath.Gravity;
        int rotors = Thrusters.Count(t => t.Kind == BlockKind.Rotor);
        if (GasCells > 0 && air.GasLiftKg >= 0.85f * MassKg) Mode = MobilityMode.Airship;
        else if (rotors > 0) Mode = MobilityMode.Helicopter;
        else if (air.HoverThrustN > 0.8f * weight) Mode = MobilityMode.Hover;
        else if (air.WingAreaM2 > 0 && air.StaticThrustN > 0) Mode = MobilityMode.Plane;
        else if (air.HoverThrustN > 0) Mode = MobilityMode.Hover;
        else return;

        DragArea = bodyDrag;
        switch (Mode)
        {
            case MobilityMode.Plane:
                air.TopSpeedMs = Aero.TopSpeed(ThrustAt, MassKg, air.WingAreaM2, bodyDrag);
                air.CanFly = air.TopSpeedMs > air.StallSpeedMs * 1.05f;
                break;
            case MobilityMode.Helicopter:
            case MobilityMode.Hover:
                bodyDrag += Thrusters.Sum(t => t.Kind == BlockKind.Rotor ? Aero.RotorDragArea : t.Kind == BlockKind.LiftFan ? Aero.FanDragArea : 0f);
                DragArea = bodyDrag;
                air.CanFly = air.HoverThrustN > weight * 1.05f;
                // Flying along tipped forward about 18°, the rotors' push pulls it along against drag.
                air.TopSpeedMs = air.CanFly ? MathF.Min(MathF.Sqrt(weight * 0.32f / (0.5f * Aero.AirDensity * MathF.Max(bodyDrag, 0.5f))), 90f) : 0;
                if (fwdJets + air.StaticThrustN > 0 && air.WingAreaM2 > 0)
                    air.TopSpeedMs = MathF.Max(air.TopSpeedMs, Aero.TopSpeed(ThrustAt, MassKg, air.WingAreaM2, bodyDrag));
                break;
            case MobilityMode.Airship:
                // A fat envelope is a lot draggier than a streamlined fuselage.
                bodyDrag *= 4f;
                DragArea = bodyDrag;
                air.CanFly = air.GasLiftKg >= MassKg;
                air.TopSpeedMs = MathF.Min(Aero.TopSpeed(ThrustAt, MassKg, 0f, MathF.Max(bodyDrag, 0.5f)), 40f);
                break;
        }

        Air = air;
        TopSpeedKmh = FuelL > 0 ? air.TopSpeedMs * 3.6f : 0;
    }

    /// <summary>Blocks in the way of a propeller's or rotor's blades.</summary>
    private int BladeStrikes(ThrusterInfo t, float radiusM)
    {
        float r = radiusM / Blueprint.CellSize;
        int hits = 0;
        BlueprintBlock self = Cells[t.Cell];
        foreach (Int3 c in Cells.Keys)
        {
            if (Cells[c] == self) continue;
            Int3 d = c - t.Cell;
            int along = d.X * t.Direction.X + d.Y * t.Direction.Y + d.Z * t.Direction.Z;
            if (along != 0) continue;
            float dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (dist > 1.01f && dist <= r) hits++;
        }

        return hits;
    }

    private void AirWarnings()
    {
        AirStats a = Air;
        float t = MassKg / 1000f;
        switch (Mode)
        {
            case MobilityMode.Plane:
                if (!a.CanFly)
                    Warnings.Add(new DesignWarning(Severity.Error,
                        $"It can't stay up: it needs {a.StallSpeedMs * 3.6f:0} km/h to fly but only reaches {a.TopSpeedMs * 3.6f:0} km/h. Add wing panels, more engine or propellers, or lose weight."));
                else if (a.StallSpeedMs > 50f)
                    Warnings.Add(new DesignWarning(Severity.Warning,
                        $"Heavy for its wings: it needs {a.StallSpeedMs * 3.6f:0} km/h before it can lift off, which is a long run. Add wing panels or lose weight."));
                if (Wheels.Count == 0)
                    Warnings.Add(new DesignWarning(Severity.Warning,
                        "No landing gear. Put Wheels on the bottom row (a Steering Wheel at the nose or tail steers on the ground), or it can only start in the air."));
                else if (CenterOfMass.Z < Wheels.Min(w => w.Cell.Z) * Blueprint.CellSize + 0.1f)
                    Warnings.Add(new DesignWarning(Severity.Warning,
                        "All the wheels are behind the center of mass, so it will tip onto its nose. Move the main wheels forward of the pink dot, or add one under the nose."));
                else if (CenterOfMass.Z > Wheels.Max(w => w.Cell.Z) * Blueprint.CellSize - 0.1f)
                    Warnings.Add(new DesignWarning(Severity.Warning,
                        "All the wheels are ahead of the center of mass, so it will sit on its tail. Add a wheel at the tail."));
                if (a.StaticMarginM < -0.25f)
                    Warnings.Add(new DesignWarning(Severity.Warning,
                        "The wings lift ahead of the center of mass, so it will pitch up and flip. Move the wings back, or add a tailplane of wing panels behind them."));
                else if (a.HasPitch && a.MaxPitchDeg < 9f)
                    Warnings.Add(new DesignWarning(Severity.Warning,
                        $"Nose-heavy: the wings lift so far behind the center of mass that the elevators can only raise the nose about {a.MaxPitchDeg:0}°, too little to take off. Move the wings forward a little, or add elevators."));
                if (!a.HasRoll)
                    Warnings.Add(new DesignWarning(Severity.Warning, "No ailerons, so it can't roll into a turn. Put Control Surfaces out at the wingtips."));
                if (!a.HasPitch)
                    Warnings.Add(new DesignWarning(Severity.Warning, "No elevator, so it can't pitch. Put Control Surfaces on a tailplane behind the wings."));
                if (!a.HasYaw)
                    Warnings.Add(new DesignWarning(Severity.Info, "No rudder. Stand a Control Surface up (G) on the tail fin."));
                break;
            case MobilityMode.Helicopter:
            case MobilityMode.Hover:
                if (!a.CanFly)
                    Warnings.Add(new DesignWarning(Severity.Error,
                        $"Its {(Mode == MobilityMode.Helicopter ? "rotors" : "lift fans and jets")} can lift {a.HoverThrustN / GunMath.Gravity / 1000f:0.0} t but it weighs {t:0.0} t. Add engine power or {(Mode == MobilityMode.Helicopter ? "rotors" : "fans")}, or lose weight."));
                break;
            case MobilityMode.Airship:
                if (!a.CanFly)
                    Warnings.Add(new DesignWarning(Severity.Error,
                        $"Its gas cells hold up {a.GasLiftKg / 1000f:0.0} t but it weighs {t:0.0} t. Add {(int)MathF.Ceiling((MassKg - a.GasLiftKg) / Aero.GasCellLiftKg)} more gas cells' worth, or lose weight."));
                else if (a.GasLiftKg * Aero.GasTrimMin > MassKg)
                    Warnings.Add(new DesignWarning(Severity.Warning,
                        "So much gas it can't come back down, even after venting. Use fewer gas cells or carry more."));
                if (a.TopSpeedMs <= 0)
                    Warnings.Add(new DesignWarning(Severity.Warning, "No propellers or jets, so it can only drift. Add Air Propellers."));
                break;
        }

        foreach (ThrusterInfo th in Thrusters)
        {
            float radius = th.Kind switch { BlockKind.Rotor => Aero.RotorRadiusM, BlockKind.AirPropeller => Aero.PropRadiusM, _ => 0 };
            int hits = radius > 0 ? BladeStrikes(th, radius) : 0;
            if (hits > 0)
                Warnings.Add(new DesignWarning(Severity.Warning,
                    $"{hits} block(s) sit in the path of the {Defs[th.Cell].Name.ToLowerInvariant()} blades at {th.Cell}. Leave {radius:0.0} m clear around it."));
        }
    }

    /// <summary>Heavy weapons on a vehicle: land vehicles need stabilizers, aircraft can't fire them at all.</summary>
    /// <summary>A dozer blade has to reach the ground: its bottom row within a row of the tracks' or wheels'.</summary>
    private void BladeWarnings()
    {
        var blades = Anchors.Where(c => Defs[c].Kind == BlockKind.DozerBlade).ToList();
        if (blades.Count == 0) return;
        if (Wheels.Count == 0 || Mode != MobilityMode.Ground)
        {
            Warnings.Add(new DesignWarning(Severity.Warning, "Dozer blades only work on a land vehicle with tracks or wheels."));
            return;
        }

        int wheelRow = Wheels.Min(w => w.Cell.Y);
        foreach (Int3 b in blades.Where(b => b.Y > wheelRow + 1))
            Warnings.Add(new DesignWarning(Severity.Warning, $"The dozer blade at {b} is too high to reach the ground: put its bottom row level with the tracks or wheels."));
        Warnings.Add(new DesignWarning(Severity.Info, "Dozer blade: K lowers it to dig (25 cm below the tracks; each pass goes deeper) and raises it again to push earth without digging."));
    }

    private void HeavyWarnings()
    {
        GunInfo heavy = Guns.FirstOrDefault(g => g.Valid && g.BreechDef.Breech.Heavy);
        if (heavy == null) return;
        if (IsAir)
            Warnings.Add(new DesignWarning(Severity.Warning, $"The {heavy.Label} is a heavy weapon: an aircraft can't brace it, so it can't fire."));
        else if (!IsNaval && Kit.Stabilizers < Emplacement.StabilizersNeeded)
            Warnings.Add(new DesignWarning(Severity.Warning,
                $"The {heavy.Label} is a heavy weapon: a land vehicle can only fire it stopped and braced. Add {Emplacement.StabilizersNeeded} or more Stabilizers (Mobility)."));
        else
            Warnings.Add(new DesignWarning(Severity.Info,
                $"The {heavy.Label} is a heavy weapon: it fires {(IsNaval ? "from the ship" : "only when stopped and braced")}, and reloads at half the rate it would from a building."));
    }

    /// <summary>A building needs none of a vehicle's parts; its guns need someone to fire them.</summary>
    private void BuildingWarnings()
    {
        Warnings.Add(new DesignWarning(Severity.Info, "A building: it stays where it's put, anchored by its foundation. Its guns are steadier than on any vehicle, and heavy weapons fire at full rate."));
        if (Guns.Any(g => g.Valid) && !Seats.Any(s => s.Role is SeatRole.Gunner or SeatRole.Driver))
            Warnings.Add(new DesignWarning(Severity.Warning, "Its guns have nobody to fire them. Add a Gunner Seat (Control)."));
        if (GeneratorKw > 0 && FuelL <= 0)
            Warnings.Add(new DesignWarning(Severity.Error, "The generators burn fuel: add a Fuel Tank."));
        PowerWarnings();
        foreach (MissileInfo m in Missiles.Where(m => m.Problem != null))
            Warnings.Add(new DesignWarning(m.Valid ? Severity.Info : Severity.Warning, m.Problem));
        int loose = Disconnected.Count;
        if (loose > 0)
            Warnings.Add(new DesignWarning(Severity.Warning, $"{loose} block(s) aren't attached to the vehicle (shown in red) and will fall off."));
        foreach (GunInfo g in Guns.Where(g => g.Valid && !g.BreechDef.Breech.Beam && g.ApRounds + g.HeRounds + g.FlakRounds == 0))
            Warnings.Add(new DesignWarning(Severity.Warning, g.BreechDef.Breech.AntiAir
                ? $"The {g.Label} gun has no flak. It fires nothing else: put a Flak Rack or Flak Magazine touching the breech, or connect one with Ammo Feeds."
                : $"The {g.Label} gun has no ammo. Put a rack touching the breech, or connect racks to it with Ammo Feeds."));
    }

    /// <summary>Railguns and lasers need electricity made aboard, and room to store a shot.</summary>
    private void PowerWarnings()
    {
        bool fuelFlagged = Warnings.Any(w => w.Text.StartsWith("No fuel tank"));
        if (GeneratorKw > 0 && FuelL <= 0 && !fuelFlagged)
            Warnings.Add(new DesignWarning(Severity.Error, "The generators burn fuel: add a Fuel Tank."));

        GunInfo hungriest = Guns.Where(g => g.NeedsPower).OrderByDescending(g => g.Stats.ChargeMJ).FirstOrDefault();
        if (hungriest == null)
        {
            if (ElectricKw > 0)
                Warnings.Add(new DesignWarning(Severity.Info, $"It makes {ElectricKw / 1000f:0.#} MW of electricity, but nothing aboard uses it. Railguns and lasers do."));
            return;
        }

        if (ElectricKw <= 0)
        {
            Warnings.Add(new DesignWarning(Severity.Error,
                $"The {hungriest.Label} at {hungriest.Breech} needs electricity. Add a Generator (Power), or a Fusion Reactor for big ships."));
            return;
        }

        if (StorageMJ + 1e-3f < hungriest.Stats.ChargeMJ)
        {
            Warnings.Add(new DesignWarning(Severity.Error,
                $"Each {hungriest.Label} shot takes {hungriest.Stats.ChargeMJ:0.#} MJ, but this can only store {StorageMJ:0.#} MJ. Add Capacitor Banks (Power)."));
            return;
        }

        // How much the energy weapons want if they all fire as fast as they can, against what's made.
        float wantMW = Guns.Where(g => g.NeedsPower && g.Valid).Sum(g => g.Stats.ChargeMJ / MathF.Max(g.Stats.ReloadS, 0.05f));
        float madeMW = ElectricKw / 1000f;
        string text = $"Power: {madeMW:0.#} MW made, {StorageMJ:0} MJ stored. Firing flat out, the energy weapons want {wantMW:0.#} MW";
        text += wantMW > madeMW * 1.05f
            ? $", so once the store runs dry they fire at {madeMW / wantMW * 100f:0}% of their rate."
            : ", and get it.";
        Warnings.Add(new DesignWarning(Severity.Info, text));
    }

    private void CollectWarnings()
    {
        if (IsBuilding)
        {
            BuildingWarnings();
            return;
        }

        if (!HasDriver)
            Warnings.Add(new DesignWarning(Severity.Error, "No driver seat. Every vehicle needs one to be driven (or a Drone Core to fly it by remote)."));
        bool needsEngine = !(IsAir && Jets > 0 && Thrusters.All(t => t.Kind == BlockKind.JetEngine));
        if (PowerKw <= 0 && needsEngine)
            Warnings.Add(new DesignWarning(Severity.Error, "No engine. Add a Small or Large Engine (Mobility), or an Aero Engine (Air)."));
        else if (FuelL <= 0)
            Warnings.Add(new DesignWarning(Severity.Error, "No fuel tank. The engines can't run without one."));
        if (Mode == MobilityMode.Ground && Wheels.Count == 0)
            Warnings.Add(new DesignWarning(Severity.Error,
                "No wheels or tracks. Put them on the bottom row with nothing underneath (or Ship Propellers, to make it a boat)."));
        if (IsNaval) NavalWarnings();
        if (IsAir) AirWarnings();
        PowerWarnings();
        HeavyWarnings();
        BladeWarnings();
        foreach (MissileInfo m in Missiles.Where(m => m.Problem != null))
            Warnings.Add(new DesignWarning(m.Valid ? Severity.Info : Severity.Warning, m.Problem));

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
            BreechSpec spec = g.BreechDef.Breech;
            if (spec.Beam) continue;
            if (g.ApRounds + g.HeRounds + g.FlakRounds == 0)
                Warnings.Add(new DesignWarning(Severity.Warning, spec.Rail
                    ? $"The {g.Label} has no slugs. Put an AP Rack touching the breech, or connect AP racks to it with Ammo Feeds."
                    : spec.Rocket
                        ? $"The {g.Label} have no rockets. Put an HE Rack touching the launcher, or connect HE racks to it with Ammo Feeds."
                        : spec.AntiAir
                            ? $"The {g.Label} gun has no flak. It fires nothing else: put a Flak Rack or Flak Magazine touching the breech, or connect one with Ammo Feeds."
                            : $"The {g.Label} gun has no ammo. Put an AP, HE or Flak Rack touching the breech, or connect racks to it with Ammo Feeds."));
            if (g.MuzzleBlocked)
                Warnings.Add(new DesignWarning(Severity.Warning,
                    $"The {g.Label} gun's muzzle is blocked by a block right in front of it. It will shoot itself."));
        }

        if (Guns.Any(g => g.Valid) && HasDriver && Seats.All(s => s.Role != SeatRole.Gunner) && Mode is not (MobilityMode.Plane or MobilityMode.Hover))
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
