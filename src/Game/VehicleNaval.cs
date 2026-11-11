using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Water: every vehicle floats (or doesn't) on the blocks and sealed air it has, and feels water drag, so
/// tanks wade and sink. Ships and submarines also get propellers, rudders, dive planes, ballast, flooding
/// through holes, bilge pumps, and torpedo tubes.
/// </summary>
public partial class VehicleBody
{
    public sealed class TubeRt
    {
        public LauncherInfo Info;
        public BlockInstance Block;
        public bool Alive = true;
        public float ReloadLeft;
    }

    /// <summary>The engine order telegraph: ships keep the speed you set, so you can leave the wheel and man a gun.</summary>
    public static readonly string[] TelegraphNames = { "FULL ASTERN", "HALF ASTERN", "STOP", "SLOW AHEAD", "HALF AHEAD", "FULL AHEAD" };
    private static readonly float[] TelegraphThrottle = { -1f, -0.5f, 0f, 0.3f, 0.6f, 1f };
    private const int TelegraphStop = 2;

    // Water drag on each submerged piece, per m³ under water (N per m/s). Sideways and up/down are high
    // (the hull is a keel and a raft); along the hull is low (hull resistance below does the rest).
    private const float DragAlong = 2f, DragSide = 1500f, DragUp = 1500f;

    /// <summary>How far a submarine can be ordered down (meters below the surface, measured at the top of the hull).</summary>
    public const float MaxOrderedDepth = 50f;

    public readonly List<TubeRt> Tubes = new();
    public int Telegraph { get; private set; } = TelegraphStop;
    public string TelegraphName => TelegraphNames[Telegraph];
    public float TargetDepth { get; private set; }
    public int BallastCommand { get; private set; }

    /// <summary>How far the top of the hull is under the surface (negative: how far above it).</summary>
    public float DepthM { get; private set; }

    /// <summary>Share of everything that floats which is under water right now (0 to 1).</summary>
    public float Submerged { get; private set; }
    public bool IsNaval => Analysis.IsNaval;
    public bool IsSubmarine => Analysis.Mode == MobilityMode.Submarine;
    public float RudderAngle { get; private set; }
    public int PropsAlive { get; private set; }
    public int RuddersAlive { get; private set; }
    public int PlanesAlive { get; private set; }
    public int PumpsAlive { get; private set; }
    public int ValvesAlive { get; private set; }
    public bool TakingWater { get; private set; }
    public float UsablePowerKw => Mathf.Max(0, PowerKw - _drownedKw);
    public bool EngineDrowned => PowerKw > 0 && UsablePowerKw <= 0;

    /// <summary>Flood water in the hull (not counting ballast tanks), m³, and how much the hull could hold.</summary>
    public float FloodWater => Analysis.Compartments.Where(c => !c.IsBallast).Sum(c => c.Water);
    public float FloodCapacity => Analysis.Compartments.Where(c => !c.IsBallast).Sum(c => c.CapacityM3);
    public float BallastFill
    {
        get
        {
            float cap = Analysis.Compartments.Where(c => c.IsBallast).Sum(c => c.CapacityM3);
            return cap > 0 ? Analysis.Compartments.Where(c => c.IsBallast).Sum(c => c.Water) / cap : 0;
        }
    }

    public int TorpedoesLeft => Tubes.Where(t => t.Alive).Sum(TorpedoesFor);
    public bool CanFireTorpedo => Tubes.Any(t => t.Alive && t.ReloadLeft <= 0 && TorpedoesFor(t) > 0);

    private FloatModel _float;
    private readonly List<BlockInstance> _engines = new();
    private Dictionary<Int3, BlockDef> _designCells;
    private float _floatScale = 1f;
    private float _drownedKw;
    private float _hullK;
    private float _lengthM, _beamM, _heightM;

    // How hard the water resists the hull swinging round (N·m per rad/s), summed each tick from the
    // submerged pieces, so the rudder can push through it on a long hull.
    private float _yawDamp;
    private float _wakeTimer;
    private float _lastFlood;
    private bool _saidDrowned;
    private float _aiTorpedoWait = 3f;

    private void SetupNaval()
    {
        VehicleAnalysis a = Analysis;
        float cs = Conv.CS;
        _lengthM = (a.Max.Z - a.Min.Z + 1) * cs;
        _beamM = (a.Max.X - a.Min.X + 1) * cs;
        _heightM = (a.Max.Y - a.Min.Y + 1) * cs;
        float draft = a.IsNaval ? Mathf.Max(a.Water.DraftM, 0.3f) : _heightM;
        _hullK = 0.5f * Hydro.WaterDensity * (0.2f * _beamM * draft + 0.004f * _lengthM * (_beamM + 2f * draft));
        _designCells = a.Defs.Where(kv => !a.Disconnected.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        _float = new FloatModel(Solids(), a.Compartments);

        foreach (LauncherInfo t in a.TorpedoTubes)
            if (Model.Blocks.TryGetValue(t.Cell, out BlockInstance b)) Tubes.Add(new TubeRt { Info = t, Block = b });
    }

    /// <summary>Every cell of block material that pushes water aside, and how much.</summary>
    private IEnumerable<(Int3 cell, float volume)> Solids()
    {
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive) continue;
            float v = b.Def.Displacement * Hydro.CellVolume;
            if (v <= 0) continue;
            foreach (Int3 c in b.Cells) yield return (c, v);
        }
    }

    /// <summary>Recounts what floats and which sea parts still work (after damage or repairs).</summary>
    private void RefreshNaval()
    {
        float total = 0;
        if (_float != null)
        {
            _float.SetSolids(Solids());
            foreach (FloatModel.Brick b in _float.Bricks) total += b.Solid;
        }

        int props = 0, rudders = 0, planes = 0, pumps = 0, valves = 0;
        _engines.Clear();
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive) continue;
            if (b.Def.PowerKw > 0) _engines.Add(b);
            switch (b.Def.Kind)
            {
                case BlockKind.ShipPropeller: props++; break;
                case BlockKind.BilgePump: pumps++; break;
                case BlockKind.BallastValve: valves++; break;
                case BlockKind.Rudder:
                    if (Mathf.Abs(Orientations.Right(b.Orientation).Y) == 1) planes++;
                    else rudders++;
                    break;
            }
        }

        PropsAlive = props;
        RuddersAlive = rudders;
        PlanesAlive = planes;
        PumpsAlive = pumps;
        ValvesAlive = valves;

        // Land vehicles mostly sink: their blocks hold up at most about a third of their weight.
        _floatScale = IsNaval ? 1f : Mathf.Min(1f, 0.35f * Mass / Mathf.Max(total * Hydro.WaterDensity, 1f));

        foreach (TubeRt t in Tubes)
            t.Alive = Model.Blocks.TryGetValue(t.Info.Cell, out BlockInstance tb) && tb == t.Block && tb.Alive;

        if (Analysis.Compartments.Count > 0 && _designCells != null)
            Hydro.UpdateBreaches(Analysis.Compartments, _designCells,
                c => Model.Blocks.TryGetValue(c, out BlockInstance wb) && wb.Alive && wb.Def.Watertight, _float.CompartmentAt);
    }

    private int TorpedoesFor(TubeRt t)
    {
        float units = 0;
        foreach (Int3 c in t.Info.Racks)
            if (Model.Blocks.TryGetValue(c, out BlockInstance b) && b.Alive) units += b.AmmoUnits;
        return (int)(units / Torpedoes.Units);
    }

    public void StepTelegraph(int by)
    {
        int next = Mathf.Clamp(Telegraph + by, 0, TelegraphNames.Length - 1);
        if (next == Telegraph) return;
        Telegraph = next;
        Sfx.Play2D(this, SoundId.Tick, -4f, 0.7f + 0.08f * next);
    }

    public void AllStop()
    {
        if (Telegraph == TelegraphStop) return;
        Telegraph = TelegraphStop;
        Sfx.Play2D(this, SoundId.Tick, -4f, 0.75f);
    }

    /// <summary>Submarines: order the boat deeper (+) or shallower (-) at a steady rate.</summary>
    public void ChangeDepth(float metersPerSecond, float dt) =>
        TargetDepth = Mathf.Clamp(TargetDepth + metersPerSecond * dt, 0, MaxOrderedDepth);

    public void Surface() => TargetDepth = 0;

    // ---------- Physics ----------

    /// <summary>Buoyancy, water drag, flooding and drowned engines. Runs for every vehicle.</summary>
    /// <summary>The height of the lowest corner of its box, however it's tilted.</summary>
    private float LowestY(Transform3D xf)
    {
        float cs = Conv.CS;
        Vector3 lo = new Vector3(Analysis.Min.X - 0.5f, Analysis.Min.Y - 0.5f, Analysis.Min.Z - 0.5f) * cs;
        Vector3 hi = new Vector3(Analysis.Max.X + 0.5f, Analysis.Max.Y + 0.5f, Analysis.Max.Z + 0.5f) * cs;
        Basis b = xf.Basis;
        float y = xf.Origin.Y;
        y += Mathf.Min(b.X.Y * lo.X, b.X.Y * hi.X);
        y += Mathf.Min(b.Y.Y * lo.Y, b.Y.Y * hi.Y);
        y += Mathf.Min(b.Z.Y * lo.Z, b.Z.Y * hi.Z);
        return y;
    }

    private void WaterTick(float dt)
    {
        Transform3D xf = GlobalTransform;
        float cs = Conv.CS;
        float topY = (xf * new Vector3(CenterOfMass.X, (Analysis.Max.Y + 0.5f) * cs, CenterOfMass.Z)).Y;
        DepthM = Ocean.Level - topY;
        // Clear of the water (the lowest corner of its box is above the waves): nothing to float, drag or flood.
        // A tank on the island used to fall through to the per-brick sums below every step, which with a
        // hundred tanks in the battle drill was most of the frame.
        if (LowestY(xf) > Ocean.Level + 0.5f)
        {
            Submerged = 0;
            _drownedKw = 0;
            TakingWater = false;
            return;
        }

        Basis basis = xf.Basis.Orthonormalized();
        Vector3 fwd = -basis.Z, right = basis.X, up = basis.Y;
        Vector3 comW = xf * CenterOfMass;
        Vector3 vel = LinearVelocity, ang = AngularVelocity;
        float rhoG = Hydro.WaterDensity * GunMath.Gravity;
        Vector3 force = Vector3.Zero, torque = Vector3.Zero;
        float under = 0, total = 0, yawDamp = 0;

        void Piece(Vector3 local, float buoyant, float bulk, float height)
        {
            Vector3 p = xf * local;
            float frac = Mathf.Clamp((Ocean.Level - (p.Y - height * 0.5f)) / height, 0, 1);
            total += buoyant;
            if (frac <= 0) return;
            under += buoyant * frac;
            Vector3 r = p - comW;
            Vector3 pv = vel + ang.Cross(r);
            float wet = bulk * frac;
            Vector3 f = Vector3.Up * (rhoG * buoyant * frac) - (fwd * (pv.Dot(fwd) * DragAlong) + up * (pv.Dot(up) * DragUp)) * wet;
            Vector3 side = right * (-pv.Dot(right) * DragSide * wet);
            float along = r.Dot(fwd);
            yawDamp += DragSide * wet * along * along;
            force += f + side;
            // Sideways drag acts as if nearer the center of mass than it is (bilge keels, forgivingly), so
            // hard turns heel a boat over a little, not onto its beam ends.
            torque += r.Cross(f) + (r - up * (r.Dot(up) * 0.6f)).Cross(side);
        }

        // Each brick of hull: its own material, plus whatever sealed air is still dry inside it.
        _float.UpdateLevels();
        foreach (FloatModel.Brick b in _float.Bricks)
        {
            float buoyant = b.Solid * _floatScale + _float.AirIn(b);
            if (b.Bulk <= 0) continue;
            Piece(b.Center.G(), buoyant, b.Bulk, b.HeightM);
        }

        ApplyCentralForce(force);
        ApplyTorque(torque);
        Submerged = total > 0 ? under / total : 0;
        _yawDamp = yawDamp;

        // Hull resistance: streamlined for ships, a brick for anything else wading through.
        float vf = vel.Dot(fwd);
        if (Submerged > 0.001f)
        {
            float k = IsNaval
                ? _hullK * Mathf.Clamp(under * Hydro.WaterDensity / Mathf.Max(Mass, 1f), 0, 1.3f)
                : 0.5f * Hydro.WaterDensity * 0.8f * _beamM * _heightM * Mathf.Min(Submerged * 2f, 1f);
            ApplyCentralForce(-fwd * (k * vf * Mathf.Abs(vf)));
        }

        FloodTick(dt, xf);
        DrownedEngines(xf);
    }

    private void FloodTick(float dt, Transform3D xf)
    {
        List<Compartment> comps = Analysis.Compartments;
        if (comps.Count == 0)
        {
            TakingWater = false;
            return;
        }

        float cs = Conv.CS;
        float HoleDepth(Compartment c)
        {
            if (c.HoleCells.Count == 0) return Ocean.Level - (xf * Conv.CellPos(c.Cells[0])).Y + cs * 0.5f;
            float deepest = float.MinValue;
            foreach (Int3 h in c.HoleCells) deepest = Mathf.Max(deepest, Ocean.Level - (xf * Conv.CellPos(h)).Y + cs * 0.5f);
            return deepest;
        }

        float FloodLimit(Compartment c) => _float.FloodLimit(c, p => (xf * p.G()).Y, Ocean.Level);

        Hydro.Step(comps, dt, HoleDepth, PumpsAlive, ValvesAlive > 0 ? BallastCommand : 0, FloodLimit);
        float flood = FloodWater;
        TakingWater = flood > _lastFlood + 1e-4f;
        _lastFlood = flood;
    }

    /// <summary>Engines stop when water reaches them: open to the sea, or under the water inside a flooded compartment.</summary>
    private void DrownedEngines(Transform3D xf)
    {
        float drowned = 0;
        foreach (BlockInstance b in _engines)
        {
            if (!b.Alive) continue;
            Compartment comp = _float.CompartmentAt(b.Cell);
            bool wet = comp != null ? _float.Flooded(comp, b.Cell.Y) : (xf * (b.Center.G() * Conv.CS)).Y < Ocean.Level;

            if (wet) drowned += b.Def.PowerKw;
        }

        _drownedKw = drowned;
        if (EngineDrowned && !_saidDrowned) Status?.Invoke(IsNaval ? "Engine room flooded!" : "Engine flooded: it stalls under water");
        _saidDrowned = EngineDrowned;
    }

    /// <summary>
    /// The fastest a ship turns with the rudder hard over (rad/s). Short boats turn in about two of their
    /// lengths; long ships a little tighter for their size, so a 300 m battleship still comes round in
    /// about six minutes rather than a quarter of an hour.
    /// </summary>
    public static float MaxTurnRate(float lengthM)
    {
        float l = Mathf.Max(lengthM, 6f);
        if (l > 40f) l = 40f * Mathf.Pow(l / 40f, 0.7f);
        return 2.8f / l;
    }

    /// <summary>Propellers, rudders, dive planes and ballast.</summary>
    private void NavalDrive(float dt)
    {
        Transform3D xf = GlobalTransform;
        Basis basis = xf.Basis.Orthonormalized();
        Vector3 fwd = -basis.Z, up = basis.Y;
        Vector3 vel = LinearVelocity;
        float vf = vel.Dot(fwd);

        bool canDrive = CanDrive;
        float throttle = canDrive ? Throttle : 0f;
        RudderAngle = Mathf.MoveToward(RudderAngle, DriverAlive ? Steer : 0f, dt * 1.6f);

        // Propellers push along their arrows, but only under water.
        int wetProps = 0;
        foreach (Int3 c in Analysis.ShipPropellers)
            if (Has(c, BlockKind.ShipPropeller) && (xf * Conv.CellPos(c)).Y < Ocean.Level - 0.1f) wetProps++;

        if (wetProps > 0 && Mathf.Abs(throttle) > 0.01f)
        {
            float power = UsablePowerKw * 1000f * 0.6f * Mathf.Abs(throttle) * (throttle < 0 ? 0.6f : 1f);
            const float governor = 60f / 3.6f;
            foreach (Int3 c in Analysis.ShipPropellers)
            {
                if (!Has(c, BlockKind.ShipPropeller)) continue;
                Vector3 at = xf * Conv.CellPos(c);
                if (at.Y >= Ocean.Level - 0.1f) continue;
                BlockInstance b = Model.Blocks[c];
                Vector3 dir = (basis * Orientations.Forward(b.Orientation).G()).Normalized();
                float along = vel.Dot(dir) * Mathf.Sign(throttle);
                if (along > governor) continue;
                float thrust = Mathf.Min(power / wetProps / Mathf.Max(Mathf.Abs(along), 1.5f), Mass * GunMath.Gravity * 0.12f / wetProps);
                ApplyForce(dir * thrust * Mathf.Sign(throttle), at - xf.Origin);
            }

            FuelL = Mathf.Max(0, FuelL - PowerKw * Mathf.Abs(throttle) * Mobility.FuelPerKwSecond * dt);
        }

        // Steering: rudders need water flowing past them, from the boat's way or the propeller wash.
        if (Submerged > 0.05f)
        {
            float flow = Mathf.Abs(vf) + (wetProps > 0 ? Mathf.Abs(throttle) * 2.5f : 0f);
            float authority = Mathf.Clamp(flow / 6f, 0, 1) * (RuddersAlive > 0 ? 1f : 0.25f);
            float maxRate = MaxTurnRate(_lengthM);
            float target = -RudderAngle * maxRate * authority * (vf < -0.3f ? -1f : 1f);
            // About the world's vertical, so a heeled boat's steering doesn't lever it further over. The first
            // part pushes through the water's resistance to turning; the second makes up the difference.
            float yawRate = AngularVelocity.Y;
            float torque = Mathf.Clamp(target * _yawDamp + (target - yawRate) * _yawInertia * 6f, -_yawInertia * 0.6f, _yawInertia * 0.6f);
            ApplyTorque(Vector3.Up * torque * Mathf.Max(authority, 0.2f));
        }

        if (IsSubmarine) DepthControl(dt, vf, up);
        Wake(dt, xf, vf);
    }

    /// <summary>
    /// A depth-keeping helper, so diving is about where you want to be rather than juggling valves: it floods
    /// and blows the tanks and works the dive planes to reach the ordered depth.
    /// </summary>
    private void DepthControl(float dt, float vf, Vector3 up)
    {
        if (TargetDepth < 0.1f)
        {
            BallastCommand = -1;
        }
        else
        {
            float err = TargetDepth - DepthM;
            float wantDown = Mathf.Clamp(err * 0.3f, -1f, 1f);
            float e = wantDown - -LinearVelocity.Y;
            BallastCommand = e > 0.1f ? 1 : e < -0.1f ? -1 : 0;
            if (PlanesAlive > 0 && Submerged > 0.3f)
            {
                float lift = Mathf.Min(PlanesAlive * 0.5f * Hydro.WaterDensity * 0.25f * 1.5f * vf * vf, Mass * GunMath.Gravity * 0.05f);
                ApplyCentralForce(Vector3.Down * Mathf.Clamp(e * 2f, -1f, 1f) * lift);
            }
        }

        // Under water a boat has little to keep it level, so the planes and trim hold it there.
        if (Submerged > 0.9f)
        {
            Vector3 tilt = up.Cross(Vector3.Up);
            Vector3 w = AngularVelocity;
            Vector3 rollPitch = w - up * w.Dot(up);
            ApplyTorque((tilt * 1.5f - rollPitch * 1.2f) * _yawInertia * 0.5f);
        }
    }

    private void Wake(float dt, Transform3D xf, float vf)
    {
        if (!IsNaval || Mathf.Abs(vf) < 2.5f || DepthM > 1.5f || Submerged < 0.05f) return;
        _wakeTimer -= dt;
        if (_wakeTimer > 0) return;
        _wakeTimer = Mathf.Lerp(0.3f, 0.1f, Mathf.Clamp(Mathf.Abs(vf) / 10f, 0, 1));
        float cs = Conv.CS;
        Node3D world = GetParent<Node3D>();
        Effects.Wake(world, xf * new Vector3(CenterOfMass.X, 0, (Analysis.Max.Z + 1.5f) * cs));
        Effects.Wake(world, xf * new Vector3(CenterOfMass.X, 0, (Analysis.Min.Z - 0.5f) * cs));
    }

    // ---------- Torpedoes ----------

    /// <summary>Fires one loaded tube. The torpedo's gyro turns it onto the bearing of <paramref name="aim"/> after launch.</summary>
    public bool FireTorpedo(bool byPlayer, Vector3? aim)
    {
        if (Combat.Instance == null) return false;
        TubeRt t = Tubes.FirstOrDefault(t => t.Alive && t.ReloadLeft <= 0 && TorpedoesFor(t) > 0);
        if (t == null) return false;
        foreach (Int3 c in t.Info.Racks)
        {
            if (!Model.Blocks.TryGetValue(c, out BlockInstance b) || !b.Alive || b.AmmoUnits < Torpedoes.Units) continue;
            b.AmmoUnits -= Torpedoes.Units;
            break;
        }

        Transform3D sec = View.SectionGlobal(t.Info.Section);
        Vector3 f = t.Info.Forward.G();
        Vector3 dir = (sec.Basis * f).Normalized();
        Vector3 pos = sec * (Conv.CellPos(t.Info.Cell) + f * Conv.CS * 1.2f);
        Combat.Instance.LaunchTorpedo(this, pos, dir, aim, byPlayer, LinearVelocity);
        ApplyImpulse(-dir * 4000f, pos - GlobalPosition);
        t.ReloadLeft = Torpedoes.ReloadS;
        return true;
    }

    private void TorpedoTick(float dt)
    {
        foreach (TubeRt t in Tubes) t.ReloadLeft = Mathf.Max(0, t.ReloadLeft - dt);
        if (!AiCrewActive || PlayerSeat != null || !CanFireTorpedo) return;
        if (!Seats.Any(s => s.Occupant == Occupant.Ai && SeatAlive(s))) return;

        // The AI crew fires at ships in range, aiming off for the target's speed.
        _aiTorpedoWait -= dt;
        if (_aiTorpedoWait > 0) return;
        _aiTorpedoWait = 4f;
        VehicleBody target = _aiVehicleTarget != null && IsInstanceValid(_aiVehicleTarget) ? _aiVehicleTarget : NearestShipTarget();
        if (target == null || !IsInstanceValid(target) || target.Submerged < 0.05f || target.DepthM > 3f) return;
        Vector3 from = GlobalPosition, to = target.GlobalTransform * target.CenterOfMass;
        if (from.DistanceTo(to) > 900f) return;
        Vector3 lead = GunMath.LeadTarget(from.N(), to.N(), target.LinearVelocity.N(), Torpedoes.Speed).G();
        FireTorpedo(false, lead);
    }

    /// <summary>For boats with tubes but no guns (and so no gunner picking targets): the closest enemy afloat.</summary>
    private VehicleBody NearestShipTarget()
    {
        VehicleBody best = null;
        float bestD = 900f;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || v == this || v.Faction == Faction || v.Model.Blocks.Count == 0 || v.Submerged < 0.05f) continue;
            float d = GlobalPosition.DistanceTo(v.GlobalPosition);
            if (d >= bestD) continue;
            bestD = d;
            best = v;
        }

        return best;
    }

    /// <summary>Puts a boat back upright, floating where it is.</summary>
    private void RecoverAfloat()
    {
        Vector3 f = -GlobalTransform.Basis.Z;
        f.Y = 0;
        float yaw = f.LengthSquared() > 0.001f ? Mathf.Atan2(-f.X, -f.Z) : 0;
        Vector3 p = GlobalPosition;
        p.Y = Mathf.Max(p.Y, Ocean.Level - Analysis.Water.WaterlineY);
        GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), p);
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
    }

    /// <summary>Where a naval design's body origin should sit so its waterline is at the sea surface.</summary>
    public static float FloatHeight(VehicleAnalysis a) => Ocean.Level - a.Water.WaterlineY + 0.05f;
}
