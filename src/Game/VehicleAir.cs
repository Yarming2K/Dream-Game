using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Flying. Every wing panel and control surface makes its own lift and drag where it sits, so a plane's
/// stability comes from its shape and it stalls if it's slow or pitched up too far. On top of that sit
/// forgiving pilot assists: planes fly toward where the mouse points (the "instructor"), helicopters and
/// drones hold their height and stop when you let go, and airships hold the height you order.
/// </summary>
public partial class VehicleBody
{
    public sealed class SurfaceRt
    {
        public SurfaceInfo Info;
        public BlockInstance Block;
        public Node3D Flap;
        public Vector3 Local, Normal, Chord;
        public float Deflection;
        public bool Alive = true;
        public bool MainWing;
    }

    public sealed class ThrusterRt
    {
        public ThrusterInfo Info;
        public BlockInstance Block;
        public Node3D Spinner;
        public Vector3 Local, Dir;
        public bool Alive = true;
        public float Output;
        public float Spin;
    }

    public readonly List<SurfaceRt> AirSurfaces = new();
    public readonly List<ThrusterRt> AirThrusters = new();

    public bool IsAir => Analysis.IsAir;

    /// <summary>How it's flying right now. VTOL designs switch between hovering and wing-borne flight (V).</summary>
    public MobilityMode FlightMode { get; private set; }

    /// <summary>The throttle lever for planes (0 to 1). It stays where you leave it.</summary>
    public float AirThrottle { get; private set; }

    public float AirspeedKmh { get; private set; }
    public float AltitudeM { get; private set; }
    public float HeightAboveGroundM { get; private set; }
    public float ClimbRate { get; private set; }
    public bool Stalled { get; private set; }
    public float AngleOfAttackDeg { get; private set; }
    public bool OnGround { get; private set; }
    public float LiftOutput { get; private set; }

    /// <summary>Airships and hovering aircraft hold this height (meters above the sea) when nobody is climbing.</summary>
    public float HoldAltitude { get; private set; } = float.NaN;
    public int BombsLeft => Model.Blocks.Values.Count(b => b.Alive && b.Def.Kind == BlockKind.BombRack && b.AmmoUnits >= 1f);
    public int MissilesReady => Analysis.Missiles.Count(MissileReady);
    public bool CanSwitchVtol => Analysis.Mode == MobilityMode.Hover && Analysis.Air.WingAreaM2 > 4f &&
                                 AirThrusters.Any(t => t.Alive && t.Info.Direction.Y == 0 && t.Info.Kind is BlockKind.AirPropeller or BlockKind.JetEngine);

    // AI patrols (target aircraft fly circuits).
    public bool Patrols;
    public Vector3 PatrolCenter;
    public float PatrolRadius = 220f;
    public float PatrolAltitude = 150f;

    private Vector3 _inertia = Vector3.One;
    private float _authPitch, _authRoll, _authYaw;
    private float _gasLiftN;
    private Vector3 _gasCenter;
    private float _gasTrim = 1f;
    private float _sideArea, _topArea;
    private float _soundLoad;
    private float _missileWait = 4f;
    private float _rebuildTimer;
    private bool _hasAirParts;
    private bool _trimSet;
    private float _pitchTrim;

    private void SetupAir()
    {
        FlightMode = Analysis.Mode;
        foreach (SurfaceInfo s in Analysis.Surfaces)
        {
            if (!Model.Blocks.TryGetValue(s.Cell, out BlockInstance b)) continue;
            Vector3 n = Orientations.Up(s.Orientation).G();
            AirSurfaces.Add(new SurfaceRt
            {
                Info = s, Block = b, Local = Conv.CellPos(s.Cell), Normal = n, Chord = Orientations.Forward(s.Orientation).G(),
                Flap = View.VisualOf(b)?.GetNodeOrNull<Node3D>("Flap"),
                MainWing = s.Role == SurfaceRole.Wing && Mathf.Abs(n.Y) > 0.9f,
            });
        }

        foreach (ThrusterInfo t in Analysis.Thrusters)
        {
            if (!Model.Blocks.TryGetValue(t.Cell, out BlockInstance b)) continue;
            AirThrusters.Add(new ThrusterRt
            {
                Info = t, Block = b, Local = Conv.CellPos(t.Cell), Dir = t.Direction.G(),
                Spinner = View.VisualOf(b)?.GetNodeOrNull<Node3D>("Spinner"),
            });
        }

        _hasAirParts = AirSurfaces.Count > 0 || AirThrusters.Count > 0 || Analysis.GasCells > 0;
        var side = new HashSet<(int, int)>();
        var top = new HashSet<(int, int)>();
        foreach ((Int3 c, BlockDef d) in Analysis.Defs)
        {
            if (d.IsLiftingSurface) continue;
            side.Add((c.Y, c.Z));
            top.Add((c.X, c.Z));
        }

        _sideArea = side.Count * Aero.PanelArea;
        _topArea = top.Count * Aero.PanelArea;
    }

    /// <summary>After damage or repair: what still works, and how hard the controls can turn it.</summary>
    private void RefreshAir()
    {
        if (!_hasAirParts) return;
        Vector3 com = CenterOfMass;
        Vector3 inertia = Vector3.Zero;
        float gas = 0;
        Vector3 gasSum = Vector3.Zero;
        foreach (BlockInstance b in Model.Blocks.Values)
        {
            if (!b.Alive) continue;
            Vector3 r = Conv.CellPos(b.Cell) - com;
            float m = b.Def.MassKg;
            inertia += new Vector3(r.Y * r.Y + r.Z * r.Z, r.X * r.X + r.Z * r.Z, r.X * r.X + r.Y * r.Y) * m;
            if (b.Def.LiftKg > 0)
            {
                gas += b.Def.LiftKg;
                gasSum += Conv.CellPos(b.Cell) * b.Def.LiftKg;
            }
        }

        float floor = Mass * 0.5f;
        _inertia = new Vector3(Mathf.Max(inertia.X, floor), Mathf.Max(inertia.Y, floor), Mathf.Max(inertia.Z, floor));
        _gasLiftN = gas * GunMath.Gravity;
        _gasCenter = gas > 0 ? gasSum / gas : com;
        if (!_trimSet && _gasLiftN > 0)
        {
            // Start balanced, so an airship neither shoots up nor sinks the moment it appears.
            _trimSet = true;
            _gasTrim = Mathf.Clamp(Mass * GunMath.Gravity / _gasLiftN, Aero.GasTrimMin, Aero.GasTrimMax);
        }

        _authPitch = _authRoll = _authYaw = 0;
        float perQ = Aero.PanelArea * Aero.LiftSlope * Aero.ControlThrow;
        foreach (SurfaceRt s in AirSurfaces)
        {
            s.Alive = Model.Blocks.TryGetValue(s.Info.Cell, out BlockInstance b) && b == s.Block && b.Alive;
            if (!s.Alive) continue;
            Vector3 r = s.Local - com;
            switch (s.Info.Role)
            {
                case SurfaceRole.Elevator: _authPitch += perQ * Mathf.Abs(r.Z); break;
                case SurfaceRole.Aileron: _authRoll += perQ * Mathf.Abs(r.X); break;
                case SurfaceRole.Rudder: _authYaw += perQ * Mathf.Abs(r.Z); break;
            }
        }

        foreach (ThrusterRt t in AirThrusters)
            t.Alive = Model.Blocks.TryGetValue(t.Info.Cell, out BlockInstance b) && b == t.Block && b.Alive;
    }

    public void SetAirThrottle(float value) => AirThrottle = Mathf.Clamp(value, 0, 1);

    /// <summary>VTOL: switch between hovering on the lift fans and flying on the wings.</summary>
    public bool ToggleVtol()
    {
        if (!CanSwitchVtol) return false;
        FlightMode = FlightMode == MobilityMode.Plane ? MobilityMode.Hover : MobilityMode.Plane;
        if (FlightMode == MobilityMode.Plane) AirThrottle = Mathf.Max(AirThrottle, 0.8f);
        HoldAltitude = AltitudeM;
        return true;
    }

    // ---------- Physics ----------

    private void AirTick(float dt, bool playerFlies)
    {
        if (!_hasAirParts) return;
        Transform3D xf = GlobalTransform;
        Basis basis = xf.Basis.Orthonormalized();
        Vector3 fwd = -basis.Z, up = basis.Y, right = basis.X;
        Vector3 comW = xf * CenterOfMass;
        Vector3 vel = LinearVelocity, ang = AngularVelocity;
        AltitudeM = comW.Y;
        ClimbRate = vel.Y;
        AirspeedKmh = vel.Length() * 3.6f;
        MeasureHeight(comW);
        if (float.IsNaN(HoldAltitude)) HoldAltitude = AltitudeM;

        if (!IsAir)
        {
            SurfaceForces(xf, basis, comW, vel, ang, out _);
            return;
        }

        SurfaceForces(xf, basis, comW, vel, ang, out float alpha);
        AngleOfAttackDeg = Mathf.RadToDeg(alpha);
        Stalled = FlightMode == MobilityMode.Plane && !OnGround && vel.Length() > 8f && Aero.IsStalled(alpha);
        BodyDrag(basis, vel);

        // Who's flying: the player's keys, or the AI pilot holding course.
        if (!playerFlies) AiPilot(dt, comW, fwd);
        else if (FlightMode == MobilityMode.Plane) AirThrottle = Mathf.Clamp(AirThrottle + Throttle * dt * 0.6f, 0, 1);

        bool canRun = CanDrive;
        switch (FlightMode)
        {
            case MobilityMode.Plane:
                PlaneThrust(dt, xf, basis, vel, canRun);
                PlaneControl(basis, vel, alpha);
                break;
            case MobilityMode.Helicopter:
            case MobilityMode.Hover:
                HoverControl(dt, xf, basis, vel, canRun);
                break;
            case MobilityMode.Airship:
                AirshipControl(dt, xf, basis, vel, canRun);
                break;
        }

        if (_gasLiftN > 0) ApplyForce(Vector3.Up * _gasLiftN * _gasTrim, xf * _gasCenter - xf.Origin);
        AnimateAir(dt);
    }

    /// <summary>Height above whatever is underneath (ground, sea or a structure).</summary>
    private void MeasureHeight(Vector3 comW)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var q = PhysicsRayQueryParameters3D.Create(comW, comW + Vector3.Down * 600f, GridRegistry.Layers.World | GridRegistry.Layers.Structures,
            new Godot.Collections.Array<Rid> { GetRid() });
        Godot.Collections.Dictionary hit = space.IntersectRay(q);
        float ground = hit.Count > 0 ? hit["position"].AsVector3().Y : -600f;
        float bottom = CenterOfMass.Y - (Analysis.Min.Y - 0.5f) * Conv.CS;
        HeightAboveGroundM = Mathf.Max(0, comW.Y - Mathf.Max(ground, Ocean.Level) - bottom);
        OnGround = Wheels.Any(w => w.Contact) || HeightAboveGroundM < 0.35f;
    }

    /// <summary>Lift and drag on every wing panel and control surface, applied where each one is.</summary>
    private void SurfaceForces(Transform3D xf, Basis basis, Vector3 comW, Vector3 vel, Vector3 ang, out float meanAlpha)
    {
        meanAlpha = 0;
        int wings = 0;
        Vector3 force = Vector3.Zero, torque = Vector3.Zero;
        float area = Aero.PanelArea;
        foreach (SurfaceRt s in AirSurfaces)
        {
            if (!s.Alive) continue;
            Vector3 p = xf * s.Local;
            Vector3 n = basis * s.Normal, c = basis * s.Chord;
            Vector3 r = p - comW;
            Vector3 v = vel + ang.Cross(r);
            Vector3 span = n.Cross(c);
            Vector3 vp = v - span * v.Dot(span);
            float sp2 = vp.LengthSquared();
            if (sp2 < 4f) continue;
            float speed = Mathf.Sqrt(sp2);
            Vector3 vhat = vp / speed;
            float alpha = Mathf.Atan2(-v.Dot(n), v.Dot(c));
            if (s.MainWing)
            {
                meanAlpha += alpha;
                wings++;
            }

            float cl = Aero.LiftCoefficient(alpha), cd = Aero.DragCoefficient(alpha, cl);
            float q = 0.5f * Aero.AirDensity * sp2 * area;
            Vector3 liftDir = n - vhat * vhat.Dot(n);
            Vector3 f = -vhat * (q * cd);
            if (liftDir.LengthSquared() > 1e-4f) f += liftDir.Normalized() * (q * cl);
            force += f;
            torque += r.Cross(f);
        }

        if (wings > 0) meanAlpha /= wings;
        ApplyCentralForce(force);
        ApplyTorque(torque);
    }

    /// <summary>Drag on the body itself, by direction: nose-on it's sleek, side-on and flat-on it's a slab.</summary>
    private void BodyDrag(Basis basis, Vector3 vel)
    {
        Vector3 fwd = -basis.Z, up = basis.Y, right = basis.X;
        float vf = vel.Dot(fwd), vs = vel.Dot(right), vu = vel.Dot(up);
        float k = 0.5f * Aero.AirDensity;
        float topCd = FlightMode == MobilityMode.Plane ? 0.15f : 0.8f;
        ApplyCentralForce(-(fwd * (Analysis.DragArea * vf * Mathf.Abs(vf)) + right * (_sideArea * 0.6f * vs * Mathf.Abs(vs)) +
                            up * (_topArea * topCd * vu * Mathf.Abs(vu))) * k);
    }

    private IEnumerable<ThrusterRt> Pushers => AirThrusters.Where(t => t.Alive && t.Info.Direction.Y == 0 && t.Info.Kind is BlockKind.AirPropeller or BlockKind.JetEngine);

    private IEnumerable<ThrusterRt> Lifters => AirThrusters.Where(t => t.Alive &&
        (t.Info.Kind == BlockKind.Rotor || (t.Info.Direction == Int3.Up && t.Info.Kind is BlockKind.LiftFan or BlockKind.JetEngine)));

    /// <summary>Power shared between everything the engines turn that's working right now.</summary>
    private float PowerShare(int using_) => using_ > 0 ? UsablePowerKw * 1000f / using_ : 0;

    private void PlaneThrust(float dt, Transform3D xf, Basis basis, Vector3 vel, bool canRun)
    {
        float cmd = canRun ? AirThrottle : 0f;
        var pushers = Pushers.ToList();
        int props = pushers.Count(t => t.Info.Kind == BlockKind.AirPropeller);
        float share = PowerShare(props);
        float disk = Aero.DiskArea(Aero.PropRadiusM);
        foreach (ThrusterRt t in pushers)
        {
            Vector3 dir = (basis * t.Dir).Normalized();
            float thrust = t.Info.Kind == BlockKind.JetEngine
                ? Aero.JetThrustN * cmd
                : Aero.PropThrust(share * cmd, Mathf.Max(vel.Dot(dir), 0), disk);
            t.Output = cmd;
            ApplyForce(dir * thrust, xf * t.Local - xf.Origin);
        }

        foreach (ThrusterRt t in Lifters) t.Output = 0;
        BurnFuel(dt, cmd, pushers.Count(t => t.Info.Kind == BlockKind.JetEngine) * cmd);
        _soundLoad = cmd;
    }

    private void BurnFuel(float dt, float engineLoad, float jetLoad)
    {
        float burn = PowerKw * Mathf.Clamp(engineLoad, 0, 1) * Mobility.FuelPerKwSecond + jetLoad * Aero.JetFuelPerSecond;
        FuelL = Mathf.Max(0, FuelL - burn * dt);
    }

    /// <summary>Angular velocity in the body's own axes: x pitches the nose up, y yaws it left, z rolls right.</summary>
    private Vector3 BodyRates(Basis basis)
    {
        Vector3 w = basis.Inverse() * AngularVelocity;
        return new Vector3(w.X, w.Y, -w.Z);
    }

    /// <summary>
    /// Pushes the turn rate about one axis toward what's wanted, never harder than <paramref name="authority"/>.
    /// Planes use a stiff <paramref name="gain"/>: they have to hold the nose up against their own stability.
    /// </summary>
    private void Turn(Vector3 axisWorld, float wantRate, float rate, float inertia, float authority, float gain = 5f)
    {
        float torque = Mathf.Clamp((wantRate - rate) * inertia * gain, -authority, authority);
        ApplyTorque(axisWorld * torque);
    }

    /// <summary>
    /// The instructor: banks toward where you're pointing and pulls the nose round onto it, levels the wings
    /// when you're on target, and won't pull into a stall. How hard it can turn is set by the control surfaces
    /// and the airspeed.
    /// </summary>
    private void PlaneControl(Basis basis, Vector3 vel, float alpha)
    {
        Vector3 fwd = -basis.Z, up = basis.Y, right = basis.X;
        Vector3 d = PlayerInput.AimDir.LengthSquared() > 0.5f ? PlayerInput.AimDir.Normalized() : new Vector3(fwd.X, 0, fwd.Z).Normalized();
        float tx = d.Dot(right), ty = d.Dot(up), tz = d.Dot(fwd);
        float off = Mathf.Acos(Mathf.Clamp(tz, -1, 1));
        float bank = Mathf.Atan2(-right.Y, up.Y);

        // Point the flight path (not just the nose) at the target: the nose rides a little above it. A target
        // behind is always reached by pulling round, never by pushing over.
        float pitchErr = tz >= 0 ? Mathf.Atan2(ty, tz) + Mathf.Clamp(alpha, 0, 0.25f) : (ty >= -0.3f ? 1.2f : -1.2f);
        pitchErr = Mathf.Clamp(pitchErr, -1.2f, 1.2f);
        float yawErr = Mathf.Atan2(tx, Mathf.Max(tz, 0.1f));
        // Bank toward the side the target is on, more the further off it is, but only as steeply as the wings can
        // hold the weight up at this speed (so a hard turn pulls round instead of rolling into a dive). Wings
        // level when the target is close to the nose, or straight above or below it.
        float speedNow = vel.Length();
        float maxLoad = 0.5f * Aero.AirDensity * speedNow * speedNow * Analysis.Air.WingAreaM2 * Aero.MaxLift * 0.85f /
                        Mathf.Max(Mass * GunMath.Gravity, 1f);
        float bankCap = Mathf.Clamp(Mathf.Acos(Mathf.Clamp(1f / Mathf.Max(maxLoad * 0.85f, 1f), 0, 1)), 0.26f, 1.13f);
        if (tz < 0 && Mathf.Abs(tx) < 0.05f) tx = 0.3f; // straight behind: turn right
        float lateral = tz < 0 ? 1f : Mathf.Abs(tx) / Mathf.Max(Mathf.Sqrt(tx * tx + ty * ty), 1e-3f);
        float wantBank = Mathf.Clamp(bank + 2f * tx, -bankCap, bankCap) * Mathf.SmoothStep(0.1f, 0.5f, lateral);
        float w = Mathf.SmoothStep(0.03f, 0.17f, off);
        float rollErr = Mathf.Lerp(0, wantBank, w) - bank;

        float airspeed = Mathf.Max(vel.Length(), 1f);
        // In a banked turn the wings have to pull harder just to hold height.
        float turnPull = airspeed > 20f && !OnGround ? Mathf.Clamp(GunMath.Gravity / airspeed * Mathf.Abs(Mathf.Sin(bank) * Mathf.Tan(Mathf.Clamp(bank, -1.13f, 1.13f))), 0, 0.5f) : 0;
        // A slow trim takes out the last bit of error, so level means level.
        _pitchTrim = OnGround ? 0 : Mathf.Clamp(_pitchTrim + pitchErr * 0.8f * (float)GetPhysicsProcessDeltaTime(), -0.4f, 0.4f);
        float pitchRate = Mathf.Clamp(pitchErr * 2.5f, -1f, 1f) + turnPull + _pitchTrim;
        float stallEdge = Aero.StallAngle * 0.85f;
        if (!OnGround && alpha > stallEdge - 0.05f) pitchRate = Mathf.Min(pitchRate, (stallEdge - alpha) * 4f);
        float rollRate = Mathf.Clamp(rollErr * 3f, -2.2f, 2.2f) + Steer * 2f;
        float yawRate = -Mathf.Clamp(yawErr * 1.2f, -0.4f, 0.4f);

        float speed = Mathf.Max(vel.Dot(fwd), 0);
        float q = 0.5f * Aero.AirDensity * speed * speed;
        float wash = AirThrottle * 0.15f;
        Vector3 rates = BodyRates(basis);
        Turn(right, pitchRate, rates.X, _inertia.X, q * _authPitch + _inertia.X * (0.05f + wash), 20f);
        if (!OnGround) Turn(fwd, rollRate, rates.Z, _inertia.Z, q * _authRoll + _inertia.Z * 0.05f, 20f);
        Turn(up, yawRate, rates.Y, _inertia.Y, q * _authYaw + _inertia.Y * (0.05f + wash), 20f);

        // Control surfaces swing to match (for looks).
        float pitchCmd = Mathf.Clamp((pitchRate - rates.X) * 2f, -1, 1);
        float rollCmd = Mathf.Clamp((rollRate - rates.Z) * 2f, -1, 1);
        float yawCmd = Mathf.Clamp((yawRate - rates.Y) * 2f, -1, 1);
        foreach (SurfaceRt s in AirSurfaces)
        {
            Vector3 r = s.Local - CenterOfMass;
            s.Deflection = s.Info.Role switch
            {
                SurfaceRole.Elevator => -pitchCmd * Mathf.Sign(r.Z),
                SurfaceRole.Aileron => -rollCmd * Mathf.Sign(r.X),
                SurfaceRole.Rudder => yawCmd * Mathf.Sign(r.Z),
                _ => 0,
            } * Aero.ControlThrow;
        }
    }

    /// <summary>
    /// Helicopters and drones: W / S and A / D set the speed you want to go, and it tilts to get there and
    /// stops when you let go; Space and C climb and descend, otherwise it holds its height; it turns to face
    /// where you look.
    /// </summary>
    private void HoverControl(float dt, Transform3D xf, Basis basis, Vector3 vel, bool canRun)
    {
        Vector3 fwd = -basis.Z, up = basis.Y, right = basis.X;
        Vector3 hf = new Vector3(fwd.X, 0, fwd.Z);
        hf = hf.LengthSquared() > 1e-4f ? hf.Normalized() : Vector3.Forward;
        Vector3 hr = hf.Cross(Vector3.Up);

        // Lift: hold height unless told to climb or sink, and ease down near the ground.
        float maxV = Mathf.Max(Analysis.Air.TopSpeedMs * 0.9f, 15f);
        if (Mathf.Abs(PlayerInput.Lift) > 0.01f) HoldAltitude = AltitudeM;
        float wantUp = Mathf.Abs(PlayerInput.Lift) > 0.01f ? PlayerInput.Lift * 7f : Mathf.Clamp((HoldAltitude - AltitudeM) * 0.8f, -3f, 3f);
        if (HeightAboveGroundM < 4f && wantUp < -1.5f) wantUp = -1.5f;
        float accel = Mathf.Clamp((wantUp - vel.Y) * 2.5f, -6f, 6f);
        float gas = _gasLiftN * _gasTrim;
        float need = (Mass * (GunMath.Gravity + accel) - gas) / Mathf.Max(up.Y, 0.5f);

        var lifters = Lifters.ToList();
        int powered = lifters.Count(t => t.Info.Kind != BlockKind.JetEngine);
        float share = PowerShare(powered);
        float cap = 0;
        foreach (ThrusterRt t in lifters)
            cap += t.Info.Kind == BlockKind.JetEngine ? Aero.JetThrustN
                : Aero.RotorThrust(share, Aero.DiskArea(t.Info.Kind == BlockKind.Rotor ? Aero.RotorRadiusM : Aero.FanRadiusM));
        if (!canRun) cap = 0;
        float thrust = Mathf.Clamp(need, 0, cap);
        // Sat on the ground with nobody asking to climb: idle, and don't try to tilt.
        bool parked = OnGround && Mathf.Abs(PlayerInput.Lift) < 0.01f && HoldAltitude <= AltitudeM + 0.3f && vel.Y < 0.5f && HeightAboveGroundM < 0.6f;
        if (parked) thrust = Mathf.Min(thrust, Mass * GunMath.Gravity * 0.6f);
        LiftOutput = cap > 0 ? thrust / cap : 0;
        ApplyCentralForce(up * thrust);
        foreach (ThrusterRt t in lifters) t.Output = LiftOutput;
        BurnFuel(dt, LiftOutput, lifters.Count(t => t.Info.Kind == BlockKind.JetEngine) * LiftOutput);
        _soundLoad = Mathf.Max(LiftOutput, 0.3f);

        // Any forward propellers or jets help it along when moving forward.
        foreach (ThrusterRt t in Pushers)
        {
            float cmd = canRun ? Mathf.Max(Throttle, 0) : 0;
            Vector3 dir = (basis * t.Dir).Normalized();
            float push = t.Info.Kind == BlockKind.JetEngine ? Aero.JetThrustN * cmd
                : Aero.PropThrust(share * cmd, Mathf.Max(vel.Dot(dir), 0), Aero.DiskArea(Aero.PropRadiusM));
            t.Output = cmd;
            ApplyCentralForce(dir * push);
        }

        // Tilt to reach the speed asked for.
        float wantF = Throttle * maxV, wantS = Steer * Mathf.Min(maxV, 14f);
        float pitchWant = parked ? 0 : -Mathf.Clamp((wantF - vel.Dot(hf)) * 0.05f, -0.38f, 0.38f);
        float bankWant = parked ? 0 : Mathf.Clamp((wantS - vel.Dot(hr)) * 0.06f, -0.35f, 0.35f);
        float pitch = Mathf.Asin(Mathf.Clamp(fwd.Y, -1, 1));
        float bank = Mathf.Atan2(-right.Y, up.Y);

        // Turn to face where the camera looks (the AI pilot keeps its heading).
        float yawRate = 0;
        Vector3 look = PlayerInput.AimDir;
        look.Y = 0;
        if (look.LengthSquared() > 0.1f)
        {
            float err = hf.SignedAngleTo(look.Normalized(), Vector3.Up);
            yawRate = Mathf.Clamp(err * 2f, -1.1f, 1.1f);
        }

        Vector3 rates = BodyRates(basis);
        float authority = canRun ? 1f : 0.15f;
        Turn(right, Mathf.Clamp((pitchWant - pitch) * 3f, -1.2f, 1.2f), rates.X, _inertia.X, _inertia.X * 3f * authority);
        Turn(fwd, Mathf.Clamp((bankWant - bank) * 3f, -1.5f, 1.5f), rates.Z, _inertia.Z, _inertia.Z * 3f * authority);
        float yawNow = AngularVelocity.Y;
        float yawTorque = Mathf.Clamp((yawRate - yawNow) * _inertia.Y * 4f, -_inertia.Y * 1.5f * authority, _inertia.Y * 1.5f * authority);
        ApplyTorque(Vector3.Up * yawTorque);
    }

    /// <summary>Airships: the telegraph sets the propellers, A / D turn it, and the gas is vented or topped up to hold the height you order.</summary>
    private void AirshipControl(float dt, Transform3D xf, Basis basis, Vector3 vel, bool canRun)
    {
        Vector3 fwd = -basis.Z, up = basis.Y, right = basis.X;
        float cmd = canRun ? TelegraphThrottle[Telegraph] : 0f;
        var pushers = Pushers.ToList();
        float share = PowerShare(pushers.Count(t => t.Info.Kind == BlockKind.AirPropeller));
        foreach (ThrusterRt t in pushers)
        {
            Vector3 dir = (basis * t.Dir).Normalized();
            float thrust = t.Info.Kind == BlockKind.JetEngine ? Aero.JetThrustN * Mathf.Max(cmd, 0)
                : Aero.PropThrust(share * Mathf.Abs(cmd), Mathf.Max(vel.Dot(dir) * Mathf.Sign(cmd), 0), Aero.DiskArea(Aero.PropRadiusM)) * Mathf.Sign(cmd);
            t.Output = Mathf.Abs(cmd);
            ApplyForce(dir * thrust, xf * t.Local - xf.Origin);
        }

        BurnFuel(dt, Mathf.Abs(cmd), 0);
        _soundLoad = Mathf.Abs(cmd);

        if (Mathf.Abs(PlayerInput.Lift) > 0.01f) HoldAltitude += PlayerInput.Lift * 4f * dt;
        float wantUp = Mathf.Clamp((HoldAltitude - AltitudeM) * 0.25f, -3f, 3f);
        float accel = (wantUp - vel.Y) * 1.2f;
        float trim = _gasLiftN > 0 ? Mass * (GunMath.Gravity + accel) / _gasLiftN : 1f;
        _gasTrim = Mathf.MoveToward(_gasTrim, Mathf.Clamp(trim, Aero.GasTrimMin, Aero.GasTrimMax), dt * 0.25f);
        LiftOutput = _gasTrim;

        Vector3 rates = BodyRates(basis);
        float q = 0.5f * Aero.AirDensity * vel.LengthSquared();
        Turn(up, -Steer * 0.16f, rates.Y, _inertia.Y, _inertia.Y * 0.08f + q * _authYaw);
        float pitch = Mathf.Asin(Mathf.Clamp(fwd.Y, -1, 1)), bank = Mathf.Atan2(-right.Y, up.Y);
        Turn(right, -pitch * 1.5f, rates.X, _inertia.X, _inertia.X * 0.4f);
        Turn(fwd, -bank * 1.5f, rates.Z, _inertia.Z, _inertia.Z * 0.4f);
    }

    /// <summary>
    /// The AI pilot: target planes fly a circuit; anything that can hover holds its height and heading; a
    /// plane the player has left flies straight and level.
    /// </summary>
    private void AiPilot(float dt, Vector3 comW, Vector3 fwd)
    {
        var c = new VehicleControls { AimDir = new Vector3(fwd.X, 0, fwd.Z).Normalized() };
        if (FlightMode == MobilityMode.Plane)
        {
            if (Patrols)
            {
                Vector3 r = comW - PatrolCenter;
                r.Y = 0;
                float dist = Mathf.Max(r.Length(), 1f);
                Vector3 rh = r / dist;
                Vector3 tangent = Vector3.Up.Cross(rh);
                Vector3 h = (tangent - rh * Mathf.Clamp((dist - PatrolRadius) / PatrolRadius * 2f, -0.6f, 0.6f)).Normalized();
                float climb = Mathf.Clamp((PatrolAltitude - comW.Y) * 0.01f, -0.25f, 0.25f);
                c.AimDir = (h + Vector3.Up * climb).Normalized();
            }

            AirThrottle = Mathf.MoveToward(AirThrottle, Patrols ? 0.75f : Mathf.Max(AirThrottle, 0.6f), dt * 0.5f);
        }

        c.Aim = comW + c.AimDir * 500f;
        c.HasAim = PlayerInput.HasAim;
        if (PlayerSeat != null)
        {
            // The player is aboard in another seat: keep their aim for the guns.
            c.Aim = PlayerInput.Aim;
            c.Fire = PlayerInput.Fire;
        }

        PlayerInput = c;
        Throttle = 0;
        Steer = 0;
    }

    private void AnimateAir(float dt)
    {
        foreach (ThrusterRt t in AirThrusters)
        {
            if (t.Spinner == null || !IsInstanceValid(t.Spinner)) continue;
            float rate = t.Alive && CanDrive ? (t.Info.Kind == BlockKind.Rotor ? 22f : 55f) * Mathf.Max(t.Output, 0.25f) : 0f;
            t.Spin = Mathf.Wrap(t.Spin + rate * dt, 0, Mathf.Tau);
            t.Spinner.Rotation = t.Info.Kind == BlockKind.Rotor ? new Vector3(0, t.Spin, 0) : new Vector3(0, 0, t.Spin);
        }

        foreach (SurfaceRt s in AirSurfaces)
        {
            if (s.Flap == null || !IsInstanceValid(s.Flap)) continue;
            float now = s.Flap.Rotation.X;
            s.Flap.Rotation = new Vector3(Mathf.Lerp(now, s.Deflection, Mathf.Min(1f, dt * 10f)), 0, 0);
        }

        foreach (BlockInstance b in Model.Blocks.Values)
        {
            if (b.Def.Kind != BlockKind.BombRack) continue;
            Node3D payload = View.VisualOf(b)?.GetNodeOrNull<Node3D>("Payload");
            if (payload != null) payload.Visible = b.AmmoUnits >= 1f;
        }
    }

    /// <summary>Puts an aircraft level in the air, keeping its heading (and enough speed to fly, for planes).</summary>
    private void RecoverInAir()
    {
        Vector3 f = -GlobalTransform.Basis.Z;
        f.Y = 0;
        float yaw = f.LengthSquared() > 0.001f ? Mathf.Atan2(-f.X, -f.Z) : 0;
        var basis = new Basis(Vector3.Up, yaw);
        GlobalTransform = new Transform3D(basis, GlobalPosition + Vector3.Up * 2f);
        AngularVelocity = Vector3.Zero;
        LinearVelocity = FlightMode == MobilityMode.Plane
            ? -basis.Z * Mathf.Max(LinearVelocity.Length(), Analysis.Air.StallSpeedMs * 1.4f)
            : Vector3.Zero;
        HoldAltitude = AltitudeM;
    }

    // ---------- Bombs and missiles ----------

    /// <summary>Drops one bomb, from the rack nearest the middle.</summary>
    public bool DropBomb(bool byPlayer)
    {
        if (Combat.Instance == null) return false;
        BlockInstance rack = Model.Blocks.Values
            .Where(b => b.Alive && b.Def.Kind == BlockKind.BombRack && b.AmmoUnits >= 1f)
            .OrderBy(b => Mathf.Abs(b.Cell.X)).ThenBy(b => b.Cell.X).FirstOrDefault();
        if (rack == null) return false;
        rack.AmmoUnits = 0;
        Vector3 at = View.BlockWorldPos(rack) + GlobalTransform.Basis.Y * -0.3f;
        Combat.Instance.DropBomb(this, at, LinearVelocity, byPlayer);
        Sfx.At(GetParent<Node3D>(), at, SoundId.Remove, -2f, 0.6f, 8f, delayByDistance: false);
        return true;
    }

    /// <summary>Where a bomb dropped now would land (for the bombsight), or null if none are loaded.</summary>
    public Vector3? BombImpact(PhysicsDirectSpaceState3D space)
    {
        BlockInstance rack = Model.Blocks.Values.FirstOrDefault(b => b.Alive && b.Def.Kind == BlockKind.BombRack && b.AmmoUnits >= 1f);
        if (rack == null) return null;
        Vector3 p = View.BlockWorldPos(rack), v = LinearVelocity;
        var exclude = new Godot.Collections.Array<Rid> { GetRid() };
        const float step = 0.1f;
        for (int i = 0; i < 400; i++)
        {
            float speed = v.Length();
            v += Vector3.Down * GunMath.Gravity * step;
            v *= Mathf.Max(0, 1f - Aero.BombDragK * speed * step);
            Vector3 next = p + v * step;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(p, next, GridRegistry.Layers.All, exclude));
            if (hit.Count > 0) return hit["position"].AsVector3();
            if (p.Y > Ocean.Level && next.Y <= Ocean.Level) return p.Lerp(next, (p.Y - Ocean.Level) / (p.Y - next.Y));
            p = next;
        }

        return null;
    }

    private bool MissileReady(MissileInfo m) =>
        m.Valid && m.Parts.Count > 0 && Model.Blocks.TryGetValue(m.Rail, out BlockInstance rail) && rail.Alive &&
        m.Parts.All(c => Model.Blocks.TryGetValue(c, out BlockInstance b) && b.Alive);

    /// <summary>Launches one missile. Its parts leave the vehicle and fly as the missile; supply points build new ones.</summary>
    public bool FireMissile(bool byPlayer, Vector3? aim)
    {
        if (Combat.Instance == null) return false;
        MissileInfo m = Analysis.Missiles.Where(MissileReady).OrderBy(x => x.Rail.X).FirstOrDefault();
        if (m == null) return false;

        Transform3D sec = View.SectionGlobal(m.Section);
        Vector3 f = m.Forward.G();
        Vector3 dir = (sec.Basis * f).Normalized();
        Vector3 center = sec * (Conv.CellPos(m.Rail) + f * Conv.CS * (m.Parts.Count + 1) * 0.5f);

        var visual = new Node3D { Name = "Missile" };
        for (int i = 0; i < m.Parts.Count; i++)
        {
            BlockInstance b = Model.Blocks[m.Parts[i]];
            Node3D part = BlockVisuals.Create(b.Def);
            part.Position = new Vector3(0, 0, -((i + 1) - (m.Parts.Count + 1) * 0.5f) * Conv.CS);
            visual.AddChild(part);
            Model.Remove(b);
            View.RemoveBlock(b);
        }

        VehicleBody target = null;
        if (m.Stats.Seeker == SeekerKind.Radar)
        {
            Vector3 look = aim.HasValue ? (aim.Value - center).Normalized() : dir;
            target = RadarLock(center, look, Faction, 25f, 4000f);
        }

        Combat.Instance.LaunchMissile(this, center, dir, LinearVelocity, m.Stats, byPlayer, visual, target);
        RefreshStats();
        return true;
    }

    /// <summary>The enemy aircraft closest to a line of sight, within a cone.</summary>
    public static VehicleBody RadarLock(Vector3 from, Vector3 look, int faction, float coneDeg, float range)
    {
        VehicleBody best = null;
        float bestAngle = Mathf.DegToRad(coneDeg);
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || v.Faction == faction || !v.IsAir || v.Model.Blocks.Count == 0 || v.OnGround) continue;
            Vector3 to = v.GlobalTransform * v.CenterOfMass - from;
            if (to.Length() > range) continue;
            float a = look.AngleTo(to);
            if (a >= bestAngle) continue;
            bestAngle = a;
            best = v;
        }

        return best;
    }

    /// <summary>Where an optically guided missile from this vehicle should fly right now.</summary>
    public Vector3? GuidancePoint(bool byPlayer)
    {
        if (byPlayer) return PlayerSeat != null && PlayerInput.HasAim ? PlayerInput.Aim : null;
        if (_aiVehicleTarget != null && IsInstanceValid(_aiVehicleTarget)) return _aiVehicleTarget.GlobalTransform * _aiVehicleTarget.CenterOfMass;
        return _aiCharacterTarget is { Targetable: true } ch ? ch.AimCenter : null;
    }

    /// <summary>The AI crew fires missiles at what its gunner is shooting at.</summary>
    private void AiMissiles(float dt)
    {
        if (!AiCrewActive || PlayerSeat != null || MissilesReady == 0) return;
        _missileWait -= dt;
        if (_missileWait > 0) return;
        _missileWait = 7f;
        VehicleBody target = _aiVehicleTarget;
        if (target == null || !IsInstanceValid(target)) return;
        Vector3 to = target.GlobalTransform * target.CenterOfMass;
        if (GlobalPosition.DistanceTo(to) > 1500f) return;
        MissileInfo m = Analysis.Missiles.FirstOrDefault(MissileReady);
        if (m == null || (m.Stats.Seeker == SeekerKind.Radar && !target.IsAir)) return;
        FireMissile(false, to);
    }

    /// <summary>At a supply point: hang new bombs and build new missiles onto empty rails, a part at a time.</summary>
    private bool ResupplyOrdnance(float dt)
    {
        bool changed = false;
        foreach (BlockInstance b in Model.Blocks.Values)
        {
            if (!b.Alive || b.Def.Kind != BlockKind.BombRack || b.AmmoUnits >= 1f) continue;
            b.AmmoUnits = Mathf.Min(1f, b.AmmoUnits + dt / 3f);
            changed = true;
        }

        _rebuildTimer -= dt;
        if (_rebuildTimer > 0) return changed;
        foreach (MissileInfo m in Analysis.Missiles.Where(m => m.Valid))
        {
            Int3? next = m.Parts.Cast<Int3?>().FirstOrDefault(c => !Model.Blocks.ContainsKey(c.Value));
            if (next == null || !TryRebuild(next.Value)) continue;
            _rebuildTimer = 0.4f;
            return true;
        }

        return changed;
    }
}
