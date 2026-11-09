using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// A drivable vehicle built from a blueprint. Suspension, grip and steering are simulated per wheel with
/// raycasts (so wheels and tracks behave the same whatever the design); turrets traverse toward the aim
/// point and guns elevate with automatic drop compensation.
/// </summary>
public partial class VehicleBody : RigidBody3D, IGridHost
{
    public sealed class WheelRt
    {
        public BlockInstance Block;
        public Node3D Visual;
        public Vector3 Mount;
        public float Radius;
        public float Rest;
        public BlockKind Kind;
        public bool LeftSide;
        public float Offset;
        public float Compression;
        public float Spin;
        public float SteerAngle;
        public bool Contact;
        public Vector3 HitPos;
        public Vector3 HitNormal;
    }

    public sealed class GunRt
    {
        public GunInfo Info;

        /// <summary>Position in the design's gun list (used to find who fires it).</summary>
        public int Index;
        public BlockInstance Breech;
        public GunStats Stats;
        public int Barrels;
        public bool HasBrake;
        public bool Alive = true;
        public float ReloadLeft;
        public float Pitch;
        public float ElevSpeedRad;
        public float AimError;
        public string Label => Info.Label;
        public float MuzzleDistanceCells => Barrels + (HasBrake ? 1 : 0) + 0.5f;
    }

    public sealed class TurretRt
    {
        public int Section;
        public Vector3 PivotLocal;
        public float Angle;
        public float SpeedRad;
        public GunRt Lead;
    }

    public GridModel Model { get; private set; }
    public GridView View { get; private set; }
    public PhysicsBody3D Body => this;
    public KeepRule Keep => KeepRule.DriverSeat;
    public float Radius { get; private set; }

    public Blueprint Blueprint { get; private set; }
    public VehicleAnalysis Analysis { get; private set; }
    public string DisplayName => Blueprint.Name;
    /// <summary>0 is the player's side; anything else is hostile to it.</summary>
    public int Faction;

    public bool IsPlayers => Faction == 0;

    // What the driver is doing this tick (from the player's input, or held still).
    public float Throttle;
    public float Steer;
    public bool Braking = true;

    public float PowerKw { get; private set; }
    public float FuelL { get; private set; }
    public float FuelCapacityL { get; private set; }
    public bool DriverAlive { get; private set; } = true;
    public float SpeedKmh { get; private set; }
    public AmmoType SelectedAmmo { get; private set; } = AmmoType.AP;
    public bool CanDrive => DriverAlive && FuelL > 0 && (IsAir
        ? UsablePowerKw > 0 || AirThrusters.Any(t => t.Alive && t.Info.Kind == BlockKind.JetEngine)
        : UsablePowerKw > 0 && (Wheels.Count > 0 || PropsAlive > 0));
    public bool HasLiveEngine => PowerKw > 0;

    public readonly List<WheelRt> Wheels = new();
    public readonly List<GunRt> Guns = new();
    public readonly List<TurretRt> Turrets = new();

    /// <summary>Raised for big moments: "Driver knocked out!", "Engine destroyed", ...</summary>
    public event Action<string> Status;

    /// <summary>Raised each time a gun fires (camera kick, HUD).</summary>
    public event Action<GunRt> Fired;

    private float _crr;
    private float _governor;
    private float _k;
    private float _c;
    private float _yawInertia;
    private bool _hadEngine = true;
    private readonly RandomNumberGenerator _rng = new();
    private AudioStreamPlayer3D _engineSound;

    public static VehicleBody Create(Blueprint bp, Transform3D where, bool player, VehicleAnalysis analysis = null)
    {
        // Each vehicle gets its own analysis (it keeps flood water in its compartments), so a shared one is only reused once.
        VehicleAnalysis a = analysis ?? VehicleAnalysis.Analyze(bp);
        var v = new VehicleBody { Name = player ? "PlayerVehicle" : "Vehicle", Blueprint = bp, Analysis = a, Faction = player ? 0 : 1 };
        v.Model = GridModel.FromAnalysis(a);
        foreach (Int3 c in a.Disconnected)
            if (v.Model.Blocks.TryGetValue(c, out BlockInstance b)) v.Model.Remove(b);
        v.Model.RecomputeBounds();

        var calibers = new Dictionary<Int3, float>();
        foreach (GunInfo g in a.Guns)
        {
            foreach (Int3 c in g.Barrels) calibers[c] = g.Stats.CaliberMm;
            if (g.Brake.HasValue) calibers[g.Brake.Value] = g.Stats.CaliberMm;
        }

        v.View = new GridView(v, v.Model, c => calibers.TryGetValue(c, out float cal) ? cal : 76f);
        v.Radius = v.View.BoundingRadius();
        v.CollisionLayer = GridRegistry.Layers.Vehicles;
        v.CollisionMask = GridRegistry.Layers.All;
        v.Transform = where;
        v.CanSleep = false;
        v.LinearDamp = 0.02f;
        v.AngularDamp = 0.8f;
        v.CenterOfMassMode = CenterOfMassModeEnum.Custom;

        float cs = Conv.CS;
        float len = (a.Max.Z - a.Min.Z + 1) * cs, wid = (a.Max.X - a.Min.X + 1) * cs;
        v._yawInertia = Mathf.Max(a.MassKg, 100f) * (len * len + wid * wid) / 12f;

        v.SetupWheels();
        v.SetupGuns();
        v.SetupSeats();
        v.SetupNaval();
        v.SetupAir();
        v.RefreshStats();
        v.FuelL = v.FuelCapacityL;
        return v;
    }

    /// <summary>How high above the ground the body origin should spawn so the wheels just touch down.</summary>
    public static float SpawnHeight(VehicleAnalysis a)
    {
        float lowest = (a.Min.Y - 0.5f) * Conv.CS;
        foreach (WheelInfo w in a.Wheels)
            lowest = Mathf.Min(lowest, w.Cell.Y * Conv.CS - w.RadiusM - Mobility.RestLength(w.Kind));
        return -lowest + 0.15f;
    }

    public override void _EnterTree() => GridRegistry.Add(this);

    public override void _Ready()
    {
        _engineSound = Sfx.Loop(this, SoundId.Engine, -14f);
        BlockInstance engine = Model.Instances.FirstOrDefault(b => b.Def.Kind == BlockKind.Engine);
        if (engine != null) _engineSound.Position = Conv.CellPos(engine.Cell);
    }

    /// <summary>Engine note rises with speed and load, and cuts out when the engine can't run.</summary>
    private void UpdateEngineSound()
    {
        if (_engineSound == null) return;
        if (!CanDrive)
        {
            if (_engineSound.Playing) _engineSound.Stop();
            return;
        }

        if (!_engineSound.Playing) _engineSound.Play();
        float load = IsAir ? _soundLoad : Mathf.Max(Mathf.Abs(Throttle), Mathf.Abs(Steer) * 0.6f);
        float rev = IsAir ? _soundLoad : Mathf.Clamp(Mathf.Abs(SpeedKmh / 3.6f) / Mathf.Max(_governor, 1f), 0, 1);
        float pitch = 0.62f + 0.85f * rev + 0.28f * load;
        _engineSound.PitchScale = Mathf.Lerp(_engineSound.PitchScale, pitch, 0.08f);
        float quieter = PlayerSeat != null ? 0f : -5f;
        _engineSound.VolumeDb = Mathf.Lerp(_engineSound.VolumeDb, -15f + 9f * load + 3f * rev + quieter, 0.1f);
    }
    public override void _ExitTree() => GridRegistry.Remove(this);

    private void SetupWheels()
    {
        float comX = Analysis.CenterOfMass.X;
        foreach (WheelInfo w in Analysis.Wheels)
        {
            if (w.Blocked || !Model.Blocks.TryGetValue(w.Cell, out BlockInstance b)) continue;
            Wheels.Add(new WheelRt
            {
                Block = b, Visual = View.VisualOf(b), Mount = Conv.CellPos(w.Cell), Radius = w.RadiusM,
                Rest = Mobility.RestLength(w.Kind), Kind = w.Kind, LeftSide = w.Cell.X * Conv.CS < comX,
                Offset = Mobility.RestLength(w.Kind),
            });
        }
    }

    private void SetupGuns()
    {
        for (int i = 0; i < Analysis.Guns.Count; i++)
        {
            GunInfo g = Analysis.Guns[i];
            if (!g.Valid || !Model.Blocks.TryGetValue(g.Breech, out BlockInstance breech)) continue;
            Guns.Add(new GunRt { Info = g, Breech = breech, Index = i });
        }

        foreach (TurretInfo t in Analysis.Turrets)
        {
            Turrets.Add(new TurretRt
            {
                Section = t.Section, PivotLocal = Conv.CellPos(t.Ring), SpeedRad = Mathf.DegToRad(t.TraverseDegPerSec),
            });
        }
    }

    private bool Has(Int3 cell, BlockKind kind) =>
        Model.Blocks.TryGetValue(cell, out BlockInstance b) && b.Alive && b.Def.Kind == kind;

    /// <summary>Recomputes everything that depends on which blocks are left.</summary>
    public void RefreshStats()
    {
        float mass = Mathf.Max(Model.TotalMassKg(), 50f);
        Mass = mass;
        CenterOfMass = Model.CenterOfMass().G();

        PowerKw = Model.Instances.Where(b => b.Alive).Sum(b => b.Def.PowerKw);
        FuelCapacityL = Model.Instances.Where(b => b.Alive).Sum(b => b.Def.FuelL);
        FuelL = Mathf.Min(FuelL, FuelCapacityL);
        bool driverWas = DriverAlive;
        DriverAlive = Model.Instances.Any(b => b.Alive && b.Def.Kind is BlockKind.DriverSeat or BlockKind.DroneCore);
        if (driverWas && !DriverAlive) Status?.Invoke("Driver knocked out!");
        if (_hadEngine && PowerKw <= 0) Status?.Invoke("Engine destroyed");
        _hadEngine = PowerKw > 0;

        CheckPlayerSeat();
        Wheels.RemoveAll(w => !w.Block.Alive || !Model.Blocks.ContainsKey(w.Block.Cell));
        int tracks = Wheels.Count(w => w.Kind == BlockKind.Track);
        float trackFraction = Wheels.Count > 0 ? tracks / (float)Wheels.Count : 0;
        _crr = Mobility.RollingResistance(trackFraction);
        _governor = Mobility.GovernorKmh(trackFraction) / 3.6f;
        if (Wheels.Count > 0)
        {
            float share = mass / Wheels.Count;
            float rest = Wheels.Average(w => w.Rest);
            _k = share * GunMath.Gravity / (0.35f * rest);
            _c = 2f * 0.6f * Mathf.Sqrt(_k * share);
        }

        foreach (GunRt g in Guns)
        {
            g.Alive = Model.Blocks.TryGetValue(g.Info.Breech, out BlockInstance b) && b == g.Breech && b.Alive;
            g.Barrels = 0;
            foreach (Int3 c in g.Info.Barrels)
            {
                if (!Has(c, BlockKind.Barrel)) break;
                g.Barrels++;
            }

            g.HasBrake = g.Barrels == g.Info.Barrels.Count && g.Info.Brake.HasValue && Has(g.Info.Brake.Value, BlockKind.MuzzleBrake);
            int loaders = 0, props = 0;
            foreach (Int3 d in Int3.Directions)
            {
                if (d == g.Info.Forward || !Model.Blocks.TryGetValue(g.Info.Breech + d, out BlockInstance n) || !n.Alive ||
                    n.Section != g.Info.Section) continue;
                if (n.Def.Kind == BlockKind.Loader) loaders++;
                else if (n.Def.Kind == BlockKind.Propellant) props++;
            }

            g.Stats = GunMath.Compute(g.Info.BreechDef.Breech, g.Barrels, g.HasBrake, loaders, props);
            float barrelMass = g.Barrels * 90f + (g.HasBrake ? 70f : 0f);
            g.ElevSpeedRad = Mathf.DegToRad(Mobility.ElevationDegPerSec(barrelMass));
            if (g.Barrels == 0) g.Alive = false;
        }

        foreach (TurretRt t in Turrets)
        {
            t.Lead = Guns.FirstOrDefault(g => g.Alive && g.Info.Section == t.Section);
            float turretMass = Model.Instances
                .Where(b => b.Section == t.Section || Model.Sections[b.Section].Parent == t.Section)
                .Sum(b => b.Def.MassKg);
            t.SpeedRad = Mathf.DegToRad(Mobility.TraverseDegPerSec(turretMass));
        }

        RefreshNaval();
        RefreshAir();
    }

    private static List<Int3> RacksFor(GunRt g, AmmoType type) => type switch
    {
        AmmoType.AP => g.Info.ApRacks,
        AmmoType.HE => g.Info.HeRacks,
        _ => g.Info.FlakRacks,
    };

    public int Rounds(GunRt g, AmmoType type)
    {
        float units = 0;
        foreach (Int3 c in RacksFor(g, type))
            if (Model.Blocks.TryGetValue(c, out BlockInstance b) && b.Alive) units += b.AmmoUnits;
        return (int)(units / g.Stats.ShellUnits);
    }

    private bool TakeRound(GunRt g, AmmoType type)
    {
        int units = g.Stats.ShellUnits;
        foreach (Int3 c in RacksFor(g, type))
        {
            if (!Model.Blocks.TryGetValue(c, out BlockInstance b) || !b.Alive || b.AmmoUnits < units) continue;
            b.AmmoUnits -= units;
            return true;
        }

        return false;
    }

    private static readonly AmmoType[] ShellTypes = { AmmoType.AP, AmmoType.HE, AmmoType.Flak };

    /// <summary>Cycles AP → HE → Flak, skipping any this vehicle carries none of.</summary>
    public void ToggleAmmo()
    {
        int at = System.Array.IndexOf(ShellTypes, SelectedAmmo);
        for (int i = 1; i <= ShellTypes.Length; i++)
        {
            AmmoType next = ShellTypes[(at + i) % ShellTypes.Length];
            if (next == AmmoType.Flak && !Guns.Any(g => g.Info.FlakRacks.Count > 0)) continue;
            SelectedAmmo = next;
            return;
        }
    }

    public bool HasFlak => Guns.Any(g => g.Info.FlakRacks.Count > 0);

    public static float ShellSpeed(GunRt g, AmmoType type) => type == AmmoType.AP ? g.Stats.MuzzleVelocity : g.Stats.HeVelocity;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        SeatRt driver = DriverSeat();
        bool playerDrives = driver != null && driver.Occupant == Occupant.Player;
        if (playerDrives)
        {
            Throttle = PlayerInput.Throttle;
            Steer = PlayerInput.Steer;
            Braking = PlayerInput.Brake;
        }
        else
        {
            Throttle = 0;
            Steer = 0;
            Braking = true;
        }

        // Ships hold the telegraph's speed whoever is at the wheel, as long as someone is.
        if (IsNaval) Throttle = driver != null && driver.Occupant != Occupant.Empty ? TelegraphThrottle[Telegraph] : 0f;

        Drive(dt);
        if (IsNaval || PropsAlive > 0) NavalDrive(dt);
        WaterTick(dt);
        AirTick(dt, playerDrives);
        CrewTick(dt);
        TorpedoTick(dt);
        AiMissiles(dt);
        Aim(dt);
        if (PlayerInput.Fire) FirePlayerGuns();
        foreach (GunRt g in Guns)
        {
            if (g.ReloadLeft <= 0) continue;
            g.ReloadLeft = Mathf.Max(0, g.ReloadLeft - dt);
            if (g.ReloadLeft <= 0 && g.Alive && PlayerSeat != null && g.Info.GunSection >= 0)
                Sfx.At(GetParent<Node3D>(), View.SectionGlobal(g.Info.Section) * Conv.CellPos(g.Info.Breech), SoundId.Reload,
                    -3f, _rng.RandfRange(0.95f, 1.05f), 6f, delayByDistance: false);
        }

        UpdateEngineSound();
        View.SyncColliders();
    }

    private void Drive(float dt)
    {
        Transform3D xf = GlobalTransform;
        Vector3 up = xf.Basis.Y.Normalized();
        Vector3 fwd = -xf.Basis.Z.Normalized();
        Vector3 comW = xf * CenterOfMass;
        Vector3 vel = LinearVelocity;
        Vector3 ang = AngularVelocity;
        float speed = vel.Dot(fwd);
        SpeedKmh = speed * 3.6f;
        if (Wheels.Count == 0) return;

        float mass = Mass;
        float share = mass / Wheels.Count;
        bool canDrive = CanDrive;
        // Aircraft wheels roll free: the propellers do the pulling.
        float throttle = canDrive && !IsAir ? Throttle : 0f;
        float steer = canDrive || IsAir ? Steer : 0f;

        // Engine force: constant power, capped by grip at low speed and by the governor at the top.
        float force = 0;
        if (canDrive && (Mathf.Abs(throttle) > 0.01f || Mathf.Abs(steer) > 0.01f))
        {
            float p = UsablePowerKw * 1000f * Mobility.DrivetrainEfficiency;
            force = Mathf.Min(p / Mathf.Max(Mathf.Abs(speed), 2.5f), mass * GunMath.Gravity * 0.5f);
            float limit = throttle >= 0 ? _governor : _governor * 0.4f;
            if (Mathf.Abs(throttle) > 0.01f && speed * Mathf.Sign(throttle) > limit) force = 0;
            float burn = PowerKw * Mathf.Max(Mathf.Abs(throttle), Mathf.Abs(steer) * 0.5f) * Mobility.FuelPerKwSecond * dt;
            FuelL = Mathf.Max(0, FuelL - burn);
        }

        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { GetRid() };
        int contacts = 0, leftTracks = 0, rightTracks = 0, trackContacts = 0;
        foreach (WheelRt w in Wheels)
        {
            Vector3 mount = xf * w.Mount;
            Vector3 end = mount - up * (w.Rest + w.Radius);
            var query = PhysicsRayQueryParameters3D.Create(mount, end, GridRegistry.Layers.All, exclude);
            Godot.Collections.Dictionary hit = space.IntersectRay(query);
            w.Contact = hit.Count > 0;
            if (w.Contact)
            {
                w.HitPos = hit["position"].AsVector3();
                w.HitNormal = hit["normal"].AsVector3();
                float dist = mount.DistanceTo(w.HitPos);
                w.Offset = Mathf.Clamp(dist - w.Radius, 0, w.Rest);
                w.Compression = w.Rest - w.Offset;
                contacts++;
                if (w.Kind == BlockKind.Track)
                {
                    trackContacts++;
                    if (w.LeftSide) leftTracks++;
                    else rightTracks++;
                }
            }
            else
            {
                w.Offset = w.Rest;
                w.Compression = 0;
            }
        }

        bool coasting = !IsAir && Mathf.Abs(throttle) < 0.01f && (trackContacts == 0 || Mathf.Abs(steer) < 0.01f);
        foreach (WheelRt w in Wheels)
        {
            if (w.Kind == BlockKind.SteerWheel)
            {
                float max = Mathf.DegToRad(Mobility.SteerAngleDeg) * (1f - 0.55f * Mathf.Clamp(Mathf.Abs(speed) / 25f, 0, 1));
                w.SteerAngle = -steer * max;
            }

            if (!w.Contact) continue;
            Vector3 p = w.HitPos;
            Vector3 n = w.HitNormal;
            Vector3 vp = vel + ang.Cross(p - comW);

            float fs = _k * w.Compression - _c * vp.Dot(up);
            fs = Mathf.Clamp(fs, 0, share * GunMath.Gravity * 6f);
            ApplyForce(up * fs, p - xf.Origin);

            Vector3 wf = w.Kind == BlockKind.SteerWheel ? fwd.Rotated(up, w.SteerAngle) : fwd;
            wf = (wf - n * wf.Dot(n)).Normalized();
            Vector3 wr = wf.Cross(n).Normalized();
            float vLong = vp.Dot(wf);
            float vLat = vp.Dot(wr);
            float grip = Mobility.Grip(w.Kind) * fs;

            float latLimit = grip * (w.Kind == BlockKind.Track && Mathf.Abs(steer) > 0.1f ? 0.45f : 1f);
            float fLat = Mathf.Clamp(-vLat * share / dt * 0.35f, -latLimit, latLimit);

            float fLong;
            if (w.Kind == BlockKind.Track)
            {
                float cmd = Mathf.Clamp(throttle + (w.LeftSide ? steer : -steer), -1f, 1f);
                int side = w.LeftSide ? leftTracks : rightTracks;
                fLong = force * 0.5f * cmd / Mathf.Max(side, 1);
            }
            else
            {
                fLong = force * throttle / Mathf.Max(contacts - trackContacts, 1);
            }

            if (Braking || coasting)
            {
                float bf = Braking ? 0.9f : 0.25f;
                fLong += Mathf.Clamp(-vLong * share / dt * 0.5f, -grip * bf, grip * bf);
            }

            if (Mathf.Abs(vLong) > 0.05f)
                fLong -= Mathf.Sign(vLong) * Mathf.Min(_crr * fs, Mathf.Abs(vLong) * share / dt * 0.5f);

            float total = Mathf.Sqrt(fLong * fLong + fLat * fLat);
            if (total > grip && total > 0)
            {
                fLong *= grip / total;
                fLat *= grip / total;
            }

            ApplyForce(wf * fLong + wr * fLat, p - xf.Origin);
            if (w.Kind != BlockKind.Track) w.Spin += vLong / w.Radius * dt;
        }

        // Tracks skid-steer; a little assist keeps turning predictable.
        if (trackContacts > 0 && canDrive && Mathf.Abs(steer) > 0.01f)
        {
            float yawRate = ang.Dot(up);
            float target = -steer * 0.9f * (1f - 0.5f * Mathf.Clamp(Mathf.Abs(speed) / 15f, 0, 1));
            float torque = Mathf.Clamp((target - yawRate) * _yawInertia * 3f, -_yawInertia * 2f, _yawInertia * 2f);
            ApplyTorque(up * torque * (trackContacts / (float)Mathf.Max(Wheels.Count(w => w.Kind == BlockKind.Track), 1)));
        }

        if (!IsAir) ApplyCentralForce(-vel * vel.Length() * 0.5f * Mobility.AirDensity * Analysis.DragArea);

        foreach (WheelRt w in Wheels)
        {
            if (w.Visual == null || !IsInstanceValid(w.Visual)) continue;
            Basis b = new Basis(Vector3.Up, w.SteerAngle) * new Basis(Vector3.Right, -w.Spin);
            w.Visual.Transform = new Transform3D(b, w.Mount - Vector3.Up * w.Offset);
        }
    }

    private static float Wrap(float a)
    {
        while (a > Mathf.Pi) a -= Mathf.Tau;
        while (a < -Mathf.Pi) a += Mathf.Tau;
        return a;
    }

    /// <summary>
    /// Turns turrets and elevates guns toward whatever the person controlling each one is aiming at
    /// (the player's crosshair or the AI gunner's target), with automatic drop compensation.
    /// </summary>
    private void Aim(float dt)
    {
        Transform3D hull = View.SectionGlobal(0);
        Transform3D toHull = hull.AffineInverse();
        foreach (TurretRt t in Turrets)
        {
            if (t.Lead == null || !Model.Sections[t.Section].Cells.Any()) continue;
            if (!AimFor(t.Lead, out Vector3 aimWorld)) continue;
            Vector3 rel = toHull * aimWorld - t.PivotLocal;
            rel.Y = 0;
            if (rel.LengthSquared() < 0.01f) continue;
            Vector3 f0 = t.Lead.Info.Forward.G();
            float desired = Mathf.Atan2(Vector3.Up.Dot(f0.Cross(rel)), f0.Dot(rel));
            float delta = Wrap(desired - t.Angle);
            float step = t.SpeedRad * dt;
            t.Angle = Wrap(t.Angle + Mathf.Clamp(delta, -step, step));
            View.SetAngle(t.Section, t.Angle);
        }

        foreach (GunRt g in Guns)
        {
            if (!g.Alive || g.Info.GunSection < 0 || !AimFor(g, out Vector3 aim)) continue;
            Transform3D parent = View.SectionGlobal(g.Info.Section);
            Vector3 pivot = parent * Conv.CellPos(g.Info.Breech);
            Vector3 to = aim - pivot;
            var flat = new Vector3(to.X, 0, to.Z);
            float dist = flat.Length();
            float v = ShellSpeed(g, SelectedAmmo);
            if (!GunMath.SolveElevation(v, dist, to.Y, out float elev)) elev = Mathf.Pi / 4f;
            Vector3 dirW = dist > 0.01f ? flat / dist * Mathf.Cos(elev) + Vector3.Up * Mathf.Sin(elev) : Vector3.Up;
            Vector3 dirL = parent.Basis.Inverse() * dirW;
            Vector3 f0 = g.Info.Forward.G();
            Vector3 axis = g.Info.Right.G();
            float desired = Mathf.Atan2(axis.Dot(f0.Cross(dirL)), f0.Dot(dirL));
            desired = Mathf.Clamp(desired, Mathf.DegToRad(Mobility.MinElevationDeg), Mathf.DegToRad(g.Info.BreechDef.Breech.MaxElevationDeg));
            float step = g.ElevSpeedRad * dt;
            g.Pitch += Mathf.Clamp(desired - g.Pitch, -step, step);
            View.SetAngle(g.Info.GunSection, g.Pitch);

            Vector3 actual = (View.SectionGlobal(g.Info.GunSection).Basis * f0).Normalized();
            g.AimError = Mathf.RadToDeg(actual.AngleTo(dirW));
        }
    }

    /// <summary>World position and direction of a gun's muzzle right now.</summary>
    public (Vector3 pos, Vector3 dir) Muzzle(GunRt g)
    {
        Transform3D gs = View.SectionGlobal(g.Info.GunSection);
        Vector3 f = g.Info.Forward.G();
        Vector3 pos = gs * (Conv.CellPos(g.Info.Breech) + f * Conv.CS * g.MuzzleDistanceCells);
        return (pos, (gs.Basis * f).Normalized());
    }

    /// <summary>Fires every gun the player is directing that's loaded and has ammo.</summary>
    public int FirePlayerGuns()
    {
        int fired = 0;
        foreach (GunRt g in Guns)
            if (PlayerDirects(g) && FireGun(g, true)) fired++;
        return fired;
    }

    /// <summary>Fires one gun if it's loaded and has ammo.</summary>
    public bool FireGun(GunRt g, bool byPlayer)
    {
        if (Combat.Instance == null || !g.Alive || g.ReloadLeft > 0 || g.Info.GunSection < 0) return false;
        AmmoType type = SelectedAmmo;
        if (Rounds(g, type) == 0) type = ShellTypes.FirstOrDefault(t => Rounds(g, t) > 0, type);
        if (!TakeRound(g, type)) return false;

        (Vector3 muzzle, Vector3 dir) = Muzzle(g);
        float spread = Mathf.DegToRad(g.Stats.SpreadDeg) * Mathf.Sqrt(_rng.Randf());
        Vector3 side = dir.Cross(Mathf.Abs(dir.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        dir = dir.Rotated(side, spread).Rotated(dir, _rng.RandfRange(0, Mathf.Tau)).Normalized();
        float speed = ShellSpeed(g, type);
        // Flak is fused to burst at the range of whatever the gun is aimed at.
        float fuse = type == AmmoType.Flak && AimFor(g, out Vector3 aimAt) ? Mathf.Clamp(muzzle.DistanceTo(aimAt), 40f, 4000f) : 0f;
        Combat.Instance.Fire(this, muzzle, dir * speed + LinearVelocity, type, g.Stats, g.Info.BreechDef.Breech, byPlayer, fuseM: fuse);

        Vector3 breech = View.SectionGlobal(g.Info.GunSection) * Conv.CellPos(g.Info.Breech);
        ApplyImpulse(-dir * g.Stats.RecoilImpulse, breech - GlobalPosition);
        g.ReloadLeft = g.Stats.ReloadS;
        Effects.MuzzleFlash(GetParent<Node3D>(), muzzle, dir, g.Stats.CaliberMm, g.HasBrake);
        Fired?.Invoke(g);
        return true;
    }

    public bool IsUpsideDownOrStuck => GlobalTransform.Basis.Y.Normalized().Dot(Vector3.Up) < 0.5f;

    /// <summary>Puts the vehicle back on its wheels where it is ("forgiving" recovery).</summary>
    public void Recover()
    {
        if (IsAir && !OnGround && HeightAboveGroundM > 3f)
        {
            RecoverInAir();
            return;
        }

        if (IsNaval && Submerged > 0.05f)
        {
            RecoverAfloat();
            return;
        }

        Vector3 f = -GlobalTransform.Basis.Z;
        f.Y = 0;
        float yaw = f.LengthSquared() > 0.001f ? Mathf.Atan2(-f.X, -f.Z) : 0;
        GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), GlobalPosition + Vector3.Up * 2.5f);
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
    }

    public Vector3 VelocityAt(Vector3 worldPoint) =>
        LinearVelocity + AngularVelocity.Cross(worldPoint - GlobalTransform * CenterOfMass);

    public void OnBlocksRemoved()
    {
        if (Model.Blocks.Count == 0)
        {
            Vanish();
            return;
        }

        RefreshStats();
    }

    public void Vanish()
    {
        GridRegistry.Remove(this);
        if (IsInstanceValid(this)) QueueFree();
    }
}
