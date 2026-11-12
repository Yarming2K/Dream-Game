using System;
using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

public enum Occupant
{
    Empty,
    Ai,
    Player,
}

/// <summary>A crew seat at runtime: which block it is and who's sitting in it.</summary>
public sealed class SeatRt
{
    public SeatInfo Info;
    public BlockInstance Block;
    public Occupant Occupant;
    public SeatRole Role => Info.Role;
}

/// <summary>What the local player is doing this tick while aboard. Kept as plain data so it can later come over the network.</summary>
public struct VehicleControls
{
    public float Throttle;
    public float Steer;
    public bool Brake;
    public bool HasAim;
    public Vector3 Aim;
    public bool Fire;

    /// <summary>Aircraft: climb (+1) or descend (-1), and the direction the player wants to fly (from the camera).</summary>
    public float Lift;
    public Vector3 AimDir;

    /// <summary>
    /// Planes flown directly: the stick (x rolls right, y pulls the nose up, each -1 to 1) and the rudder (-1 left to 1
    /// right). When <see cref="Direct"/> is off, the instructor flies toward <see cref="AimDir"/> instead.
    /// </summary>
    public bool Direct;
    public Vector2 Stick;
    public float Rudder;
}

public enum WeldState
{
    Nothing,
    Repairing,
    AlreadyFixed,
    Rebuilding,
    Rebuilt,
    CantAttach,

    /// <summary>Playing the torch on a fire to put it out.</summary>
    Extinguishing,
    PutOut,
}

/// <summary>The block (or missing block) a welding torch is pointed at.</summary>
public struct WeldTarget
{
    public bool Missing;
    public BlockInstance Block;
    public Int3 Cell;
    public BlockDef Def;
    public Vector3 Point;
    public Vector3 Normal;
    public float Distance;
}

/// <summary>Crew seats, AI gunners, field repairs and resupply for a vehicle.</summary>
public partial class VehicleBody
{
    public readonly List<SeatRt> Seats = new();
    public SeatRt PlayerSeat { get; private set; }
    public VehicleControls PlayerInput;

    /// <summary>When false the AI crew holds fire unless the player tells them where to shoot.</summary>
    public bool AiCrewActive = true;

    /// <summary>
    /// With the player aboard, AI gunners aim where the player aims and fire when the player fires.
    /// Turn it off ("weapons free") and they pick their own targets.
    /// </summary>
    public bool GunnerFollowsPlayer
    {
        get => _gunnerFollows;
        set
        {
            _gunnerFollows = value;
            _crewStamp++;
        }
    }

    private bool _gunnerFollows = true;

    // Who controls each gun is looked up once per physics step (and again whenever seats change), not
    // every one of the dozen times a step the code asks.
    private int _crewStamp;

    /// <summary>Raised when the seat the player is in gets destroyed.</summary>
    public event Action PlayerSeatDestroyed;

    private VehicleBody _aiVehicleTarget;
    private ICharacter _aiCharacterTarget;
    private float _aiRetarget;
    private float _aiOnTarget;

    /// <summary>The AI's target is one the rest of its side spotted, not one its own crew can see.</summary>
    private bool _aiIndirect;
    private Vector3 _aiOffset;
    private Vector3 _aiAim;
    private bool _aiHasAim;

    // A second watch for anti-aircraft guns: while enemy aircraft are up, guns with flak go after them
    // and the rest keep on ships and vehicles.
    private VehicleBody _aaTarget;
    private float _aaOnTarget;
    private Vector3 _aaOffset, _aaAim;
    private bool _aaHasAim;

    private readonly Dictionary<Int3, Node3D> _repairGhosts = new();
    private readonly Dictionary<Int3, float> _rebuildProgress = new();
    private static Material _ghostCan, _ghostCant;

    private readonly Dictionary<Int3, SeatRt> _seatAt = new();

    private void SetupSeats()
    {
        foreach (SeatInfo si in Analysis.Seats)
        {
            if (!Model.Blocks.TryGetValue(si.Cell, out BlockInstance b)) continue;
            var seat = new SeatRt { Info = si, Block = b, Occupant = Occupant.Ai };
            Seats.Add(seat);
            _seatAt[si.Cell] = seat;
        }
    }

    public bool SeatAlive(SeatRt s) =>
        s.Block.Alive && Model.Blocks.TryGetValue(s.Info.Cell, out BlockInstance b) && b == s.Block;

    public SeatRt DriverSeat() => Seats.FirstOrDefault(s => s.Role == SeatRole.Driver && SeatAlive(s));

    public bool CommanderAlive => Seats.Any(s => s.Role == SeatRole.Commander && SeatAlive(s) && s.Occupant != Occupant.Empty);

    public bool HasFreeSeat => Seats.Any(s => SeatAlive(s) && s.Occupant != Occupant.Player);

    /// <summary>Gets the player into a seat (their preferred role if it's free, else the driver's, else any).</summary>
    public SeatRt Enter(SeatRole? preferred = null, SeatRt exact = null)
    {
        List<SeatRt> free = Seats.Where(s => SeatAlive(s) && s.Occupant != Occupant.Player).ToList();
        if (free.Count == 0) return null;
        SeatRt seat = (exact != null && free.Contains(exact) ? exact : null) ??
                      (preferred.HasValue ? free.FirstOrDefault(s => s.Role == preferred.Value) : null) ??
                      free.FirstOrDefault(s => s.Role == SeatRole.Driver) ?? free[0];
        seat.Occupant = Occupant.Player;
        PlayerSeat = seat;
        _crewStamp++;
        return seat;
    }

    /// <summary>The working seat nearest a point, within <paramref name="range"/> m (walking up to a crew station).</summary>
    public SeatRt SeatNear(Vector3 point, float range)
    {
        SeatRt best = null;
        float bestD = range;
        foreach (SeatRt s in Seats)
        {
            if (!SeatAlive(s) || s.Info.Remote) continue;
            float d = SeatWorldPos(s).DistanceTo(point);
            if (d >= bestD) continue;
            bestD = d;
            best = s;
        }

        return best;
    }

    /// <summary>The player gets out; the AI crew member who'd stepped aside takes the seat back.</summary>
    public void Exit()
    {
        if (PlayerSeat == null) return;
        if (SeatAlive(PlayerSeat)) PlayerSeat.Occupant = Occupant.Ai;
        PlayerSeat = null;
        PlayerInput = default;
        _crewStamp++;
    }

    /// <summary>Moves the player to another seat of this role, if there's a working one.</summary>
    public SeatRt SwitchSeat(SeatRole role)
    {
        if (PlayerSeat == null) return null;
        SeatRt target = Seats.FirstOrDefault(s => s.Role == role && SeatAlive(s) && s != PlayerSeat);
        if (target == null) return null;
        PlayerSeat.Occupant = Occupant.Ai;
        target.Occupant = Occupant.Player;
        PlayerSeat = target;
        _crewStamp++;
        return target;
    }

    public Vector3 SeatWorldPos(SeatRt s) => View.BlockWorldPos(s.Block);

    private void CheckPlayerSeat()
    {
        if (PlayerSeat == null || SeatAlive(PlayerSeat)) return;
        PlayerSeat = null;
        PlayerInput = default;
        _crewStamp++;
        PlayerSeatDestroyed?.Invoke();
    }

    /// <summary>
    /// Who's firing this gun: its assigned seat if someone's in it, otherwise another manned gunner seat,
    /// otherwise the driver. Null if nobody can.
    /// </summary>
    public SeatRt ControllerOf(GunRt g)
    {
        if (g.CrewStamp == _crewStamp) return g.Controller;
        g.CrewStamp = _crewStamp;
        g.Controller = FindController(g);
        SeatRt c = g.Controller;
        // Point defense is aimed by its own radar: it only follows the player when they sit in its mount.
        g.Directed = c != null && (g.Info.BreechDef.Breech.PointDefense
            ? c.Occupant == Occupant.Player && c.Block.Section == g.Breech.Section
            : c.Occupant == Occupant.Player || (c.Occupant == Occupant.Ai && PlayerSeat != null && GunnerFollowsPlayer));
        return g.Controller;
    }

    private SeatRt FindController(GunRt g)
    {
        bool Manned(SeatRt s) => s != null && SeatAlive(s) && s.Occupant != Occupant.Empty;
        if (Analysis.GunController.TryGetValue(g.Index, out Int3 cell) && _seatAt.TryGetValue(cell, out SeatRt assigned) && Manned(assigned))
            return assigned;

        SeatRt gunner = Seats.FirstOrDefault(s => s.Role == SeatRole.Gunner && Manned(s));
        if (gunner != null) return gunner;
        SeatRt driver = DriverSeat();
        return Manned(driver) ? driver : null;
    }

    /// <summary>True when this gun goes where the player points: they're its gunner, or their AI gunner is following orders.</summary>
    public bool PlayerDirects(GunRt g)
    {
        ControllerOf(g);
        return g.Directed;
    }

    private bool AimFor(GunRt g, out Vector3 aim)
    {
        aim = default;
        SeatRt c = ControllerOf(g);
        if (c == null) return false;
        if (PlayerDirects(g))
        {
            aim = PlayerInput.Aim;
            return PlayerInput.HasAim;
        }

        if (OnPdWatch(g))
        {
            aim = _pdAim;
            return true;
        }

        if (OnAirWatch(g))
        {
            aim = _aaAim;
            return _aaHasAim;
        }

        // Heavy flak is for aircraft only: with none up, its crew keep it raised, watching the sky.
        if (g.Info.BreechDef.Breech.AntiAir)
        {
            (Vector3 muzzle, Vector3 dir) = Muzzle(g);
            var flat = new Vector3(dir.X, 0, dir.Z);
            flat = flat.LengthSquared() > 0.01f ? flat.Normalized() : -GlobalTransform.Basis.Z;
            aim = muzzle + flat * 60f + Vector3.Up * 90f;
            return true;
        }

        aim = _aiAim;
        return _aiHasAim;
    }

    private int _aaStamp = -1;
    private bool _aaValid;

    private bool AaTargetValid()
    {
        if (_aaStamp == _crewStamp) return _aaValid;
        _aaStamp = _crewStamp;
        return _aaValid = _aaTarget != null && IsInstanceValid(_aaTarget) && _aaTarget.IsInsideTree() && _aaTarget.Model.Blocks.Count > 0 &&
                          !_aaTarget.OnGround;
    }

    /// <summary>An AI-crewed gun on the anti-aircraft watch: it has flak (or there's nothing else to shoot) and there's an aircraft up.</summary>
    private bool OnAirWatch(GunRt g) =>
        !PlayerDirects(g) && AaTargetValid() && (g.Info.FlakRacks.Count > 0 || !AiTargetValid());

    /// <summary>The shell a gun fires: the player's pick for guns they direct, otherwise the right one for the gunner's target.</summary>
    public AmmoType AmmoFor(GunRt g)
    {
        if (PlayerDirects(g)) return SelectedAmmo;
        if (OnPdWatch(g)) return g.Info.FlakRacks.Count > 0 ? AmmoType.Flak : AmmoType.HE;
        if (OnAirWatch(g)) return g.Info.FlakRacks.Count > 0 ? AmmoType.Flak : AmmoType.HE;
        if (_aiVehicleTarget == null) return AmmoType.HE;
        return _aiVehicleTarget.IsAir && !_aiVehicleTarget.OnGround && g.Info.FlakRacks.Count > 0 ? AmmoType.Flak : AmmoType.AP;
    }

    // ---------- AI gunner ----------

    private void CrewTick(float dt)
    {
        bool aiGuns = AiCrewActive && Roe != Engagement.HoldFire && Guns.Any(g => g.Alive && !PlayerDirects(g) &&
            (ControllerOf(g)?.Occupant == Occupant.Ai || (g.Info.BreechDef.Breech.PointDefense && ControllerOf(g) != null)));
        // A crew with only missiles (a missile truck) still looks for targets for them.
        bool aiMissiles = AiCrewActive && Roe != Engagement.HoldFire && PlayerSeat == null && CrewAboardAny && MissilesReady > 0;
        if (!aiGuns && !aiMissiles)
        {
            _aiHasAim = _aaHasAim = false;
            _aiVehicleTarget = null;
            _aiCharacterTarget = null;
            _aaTarget = null;
            return;
        }

        _aiRetarget -= dt;
        if (_aiRetarget <= 0 || !AiTargetValid())
        {
            // Spread out so a hundred vehicles spawned together don't all look round on the same step.
            _aiRetarget = 0.7f + System.Random.Shared.NextSingle() * 0.3f;
            PickAiTarget();
            _crewStamp++;
        }

        AirWatch(dt);
        PointDefenseWatch();
        AiSmoke();
        if (!AiTargetValid())
        {
            _aiHasAim = false;
            return;
        }

        Vector3 targetPos, targetVel;
        if (_aiVehicleTarget != null)
        {
            targetPos = _aiVehicleTarget.GlobalTransform * _aiVehicleTarget.CenterOfMass;
            targetVel = _aiVehicleTarget.LinearVelocity;
        }
        else
        {
            targetPos = _aiCharacterTarget.AimCenter;
            targetVel = _aiCharacterTarget.Velocity;
        }

        GunRt lead = Guns.FirstOrDefault(g => g.Alive && g.Info.GunSection >= 0 && ControllerOf(g)?.Occupant == Occupant.Ai && !PlayerDirects(g) &&
                                              !OnAirWatch(g) && !OnPdWatch(g) && !g.Info.BreechDef.Breech.AntiAir);
        if (lead == null)
        {
            _aiHasAim = false;
            return;
        }

        // Pick the right shell: armor-piercing for vehicles, high explosive for people.
        bool playerHasGuns = Guns.Any(PlayerDirects);
        if (!playerHasGuns)
            SelectedAmmo = _aiVehicleTarget == null ? AmmoType.HE
                : _aiVehicleTarget.IsAir && !_aiVehicleTarget.OnGround && HasFlak ? AmmoType.Flak : AmmoType.AP;

        (Vector3 muzzle, _) = Muzzle(lead);
        float speed = ShellSpeed(lead, AmmoFor(lead));
        Vector3 aim = GunMath.LeadTarget(muzzle.N(), targetPos.N(), targetVel.N(), speed).G();

        // Gunners start a little off and settle in; a commander makes them quicker and steadier.
        bool commander = CommanderAlive;
        _aiOnTarget += dt;
        float settle = commander ? 2.5f : 5f;
        float k = Mathf.Clamp(_aiOnTarget / settle, 0, 1);
        // Fire called in by someone else (indirect) lands much less tightly than at a target the crew can see.
        float radius = Mathf.Lerp(commander ? 1.6f : 3f, 0.3f, k) * (_aiIndirect ? 4f : 1f);
        _aiAim = aim + _aiOffset * radius;
        _aiHasAim = true;

        float react = commander ? 0.5f : 1.2f;
        if (_aiOnTarget < react) return;
        float tolerance = _aiVehicleTarget != null ? 1.2f : 2f;
        foreach (GunRt g in Guns)
        {
            if (!g.Alive || g.ReloadLeft > 0 || g.AimError > tolerance) continue;
            if (ControllerOf(g)?.Occupant != Occupant.Ai || PlayerDirects(g) || OnAirWatch(g) || OnPdWatch(g)) continue;
            if (g.Info.BreechDef.Breech.AntiAir) continue;
            FireGun(g, false, AmmoFor(g));
        }
    }

    /// <summary>Anti-aircraft guns: lead the aircraft, settle in, and fire flak bursts at it.</summary>
    private void AirWatch(float dt)
    {
        GunRt lead = AaTargetValid()
            ? Guns.FirstOrDefault(g => g.Alive && g.Info.GunSection >= 0 && ControllerOf(g)?.Occupant == Occupant.Ai && OnAirWatch(g))
            : null;
        if (lead == null)
        {
            _aaHasAim = false;
            return;
        }

        Vector3 targetPos = _aaTarget.GlobalTransform * _aaTarget.CenterOfMass;
        (Vector3 muzzle, _) = Muzzle(lead);
        Vector3 aim = GunMath.LeadTarget(muzzle.N(), targetPos.N(), _aaTarget.LinearVelocity.N(), ShellSpeed(lead, AmmoFor(lead))).G();
        bool commander = CommanderAlive;
        _aaOnTarget += dt;
        float k = Mathf.Clamp(_aaOnTarget / (commander ? 2f : 4f), 0, 1);
        _aaAim = aim + _aaOffset * Mathf.Lerp(commander ? 6f : 12f, 2f, k);
        _aaHasAim = true;
        if (_aaOnTarget < (commander ? 0.4f : 0.9f)) return;
        foreach (GunRt g in Guns)
        {
            if (!g.Alive || g.ReloadLeft > 0 || g.AimError > 2f) continue;
            if (ControllerOf(g)?.Occupant != Occupant.Ai || !OnAirWatch(g) || OnPdWatch(g)) continue;
            FireGun(g, false, AmmoFor(g));
        }
    }

    // ---------- Point defense ----------

    private bool _pdHasAim;
    private Vector3 _pdAim;

    /// <summary>A point-defense gun (CIWS or laser) with a missile or bomb coming in: that comes before any other target.</summary>
    private bool OnPdWatch(GunRt g) => _pdHasAim && g.Info.BreechDef.Breech.PointDefense && !PlayerDirects(g);

    /// <summary>
    /// Point-defense gunners watch for missiles and bombs coming at their own vehicle, lead the nearest, and
    /// fire as soon as their gun bears on it. No settling in: these guns are aimed by radar.
    /// </summary>
    private void PointDefenseWatch()
    {
        _pdHasAim = false;
        if (Combat.Instance == null) return;
        GunRt lead = null;
        float range = 0;
        foreach (GunRt g in Guns)
        {
            if (!g.Alive || g.Info.GunSection < 0 || !g.Info.BreechDef.Breech.PointDefense || ControllerOf(g) == null || PlayerDirects(g)) continue;
            lead ??= g;
            range = Mathf.Max(range, g.Info.BreechDef.Breech.Beam ? g.Stats.BeamRangeM * 1.5f : 1500f);
        }

        if (lead == null || !Combat.Instance.NearestThreat(this, range, out Vector3 pos, out Vector3 vel)) return;
        (Vector3 muzzle, _) = Muzzle(lead);
        float speed = lead.Info.BreechDef.Breech.Beam ? 1e5f : ShellSpeed(lead, AmmoType.HE);
        // Its shells leave with the ship's own speed, so lead the threat as it moves relative to the ship.
        _pdAim = GunMath.LeadTarget(muzzle.N(), pos.N(), (vel - LinearVelocity).N(), speed).G();
        _pdHasAim = true;
        foreach (GunRt g in Guns)
        {
            if (!g.Alive || g.ReloadLeft > 0 || !OnPdWatch(g) || ControllerOf(g) == null) continue;
            if (g.AimError > (g.Info.BreechDef.Breech.Beam ? 1.2f : 3f)) continue;
            FireGun(g, false, AmmoFor(g));
        }
    }

    private int _aiStamp = -1;
    private bool _aiValid;

    private bool AiTargetValid()
    {
        if (_aiStamp == _crewStamp) return _aiValid;
        _aiStamp = _crewStamp;
        return _aiValid = AiTargetStillThere();
    }

    private bool AiTargetStillThere()
    {
        if (_aiVehicleTarget != null)
            return IsInstanceValid(_aiVehicleTarget) && _aiVehicleTarget.IsInsideTree() && _aiVehicleTarget.Model.Blocks.Count > 0;
        return _aiCharacterTarget != null && _aiCharacterTarget.Targetable;
    }

    private readonly List<IGridHost> _candidates = new();
    private readonly List<(float d, VehicleBody v, Vector3 p)> _sorted = new();

    private void PickAiTarget()
    {
        Vector3 eye = GlobalTransform * CenterOfMass + Vector3.Up * 1.5f;
        float best = float.MaxValue;
        VehicleBody bestVehicle = null;
        ICharacter bestCharacter = null;

        // Its crew sees no further than its sight allows (night, fog and rain shorten it; radar doesn't care).
        float sight = SightM;
        bool Visible(Vector3 point, IGridHost target) => eye.DistanceTo(point) <= sight && WorldQuery.LineClear(this, eye, point, this, target);
        // Mortars and rocket artillery can fire at what the rest of their side has spotted, out of their own sight.
        bool indirect = Guns.Exists(g => g.Alive && (g.Info.BreechDef.Breech.HighArc || g.Info.BreechDef.Breech.Rocket));

        // Only vehicles in reach are considered (found through the world's grid, not by checking every
        // vehicle), nearest first, and only the first few get a line-of-sight check.
        // Air-defense missiles watch the sky as flak guns do, and further out.
        bool flak = HasFlak || HasSams;
        // Heavy flak reaches highest of all: its crews pick out bombers 7 km away.
        float skyReach = HasHeavyFlak ? 7000f : HasSams ? 6000f : 2500f;
        // Big naval guns reach much further than a tank's: their gunners look out to 3 km.
        // So do railguns and rocket artillery; lasers reach as far as their lenses focus. Mortar crews
        // look out to 4.5 km. Heavy flak doesn't look at the ground at all.
        float reach = Guns.Exists(g => g.Alive && g.Info.BreechDef.Breech.HighArc) ? 4500f
            : Guns.Exists(g => g.Alive && !g.Info.BreechDef.Breech.AntiAir &&
                               (g.Stats.CaliberMm >= 150f || g.Info.BreechDef.Breech.Rail || g.Info.BreechDef.Breech.Rocket))
            ? 3000f
            : Mathf.Max(800f, Guns.Where(g => g.Alive).Select(g => g.Stats.BeamRangeM * 1.3f).DefaultIfEmpty(0).Max());
        _candidates.Clear();
        WorldQuery.HostsNear(eye, Mathf.Max(flak ? skyReach : 0f, reach), _candidates);
        _sorted.Clear();
        // Rules of engagement: returning fire only, the gunners look at what's shooting at them and
        // what they were told to attack.
        bool returnOnly = Roe == Engagement.ReturnFire;
        VehicleBody attacker = UnderFire ? LastAttacker : null;
        VehicleBody ordered = Order == OrderKind.Attack && OrderTarget != null && IsInstanceValid(OrderTarget) ? OrderTarget : null;
        foreach (IGridHost h in _candidates)
        {
            if (h is not VehicleBody v || v == this || v.Faction == Faction || v.Model.Blocks.Count == 0) continue;
            if (returnOnly && v != attacker && v != ordered) continue;
            Vector3 p = v.GlobalTransform * v.CenterOfMass;
            _sorted.Add((eye.DistanceTo(p), v, p));
        }

        _sorted.Sort((a, b) => a.d.CompareTo(b.d));
        // An attack order puts its target first in line.
        int oi = ordered != null ? _sorted.FindIndex(x => x.v == ordered) : -1;
        if (oi > 0)
        {
            var o = _sorted[oi];
            _sorted.RemoveAt(oi);
            _sorted.Insert(0, o);
        }
        VehicleBody bestAir = null;
        int looks = 0, airLooks = 0;
        foreach ((float d, VehicleBody v, Vector3 p) in _sorted)
        {
            if (flak && v.IsAir && !v.OnGround)
            {
                // Aircraft are for the flak guns, which reach much further.
                // Aircraft stand out against the sky much further off than vehicles against the ground.
                float skySight = Mathf.Max(sight, skyReach * (RadarAlive ? 1f : Atmosphere.SightFactor));
                skySight = Stealth.SpotRangeM(skySight, v.Analysis.Signature);
                if (bestAir == null && d < Mathf.Min(skyReach, skySight) && airLooks++ < 4 && WorldQuery.LineClear(this, eye, p, this, v)) bestAir = v;
                continue;
            }

            if (bestVehicle != null || d > reach || looks++ >= 6) continue;
            if (!Visible(p, v)) continue;
            best = d;
            bestVehicle = v;
        }

        // Nothing in sight: an artillery crew takes the nearest enemy someone else has spotted.
        _aiIndirect = false;
        if (bestVehicle == null && indirect && !returnOnly)
            foreach ((float d, VehicleBody v, Vector3 _) in _sorted)
            {
                if (d > reach || v.IsAir && !v.OnGround || !Spotting.IsSpotted(Faction, v)) continue;
                best = d;
                bestVehicle = v;
                _aiIndirect = true;
                break;
            }

        if (bestAir != _aaTarget)
        {
            _aaTarget = bestAir;
            _aaOnTarget = 0;
            _aaOffset = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(-1, 1)).Normalized();
        }

        foreach (ICharacter c in Hittables.Characters)
        {
            if (!c.Targetable || c.Faction == Faction || returnOnly || ordered != null) continue;
            float d = eye.DistanceTo(c.AimCenter);
            // People are a lower priority than vehicles unless they're much closer.
            if (d > 350f || d * 1.6f >= best || !Visible(c.AimCenter, null)) continue;
            best = d * 1.6f;
            bestCharacter = c;
            bestVehicle = null;
        }

        bool changed = bestVehicle != _aiVehicleTarget || bestCharacter != _aiCharacterTarget;
        _aiVehicleTarget = bestVehicle;
        _aiCharacterTarget = bestCharacter;
        if (!changed) return;
        _aiOnTarget = 0;
        _aiOffset = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-0.4f, 0.6f), _rng.RandfRange(-1, 1)).Normalized();
    }

    // ---------- Field repairs ----------

    private sealed class MissingLookup : ICellShapeLookup
    {
        private readonly VehicleBody _v;
        private readonly HashSet<Int3> _cells;

        public MissingLookup(VehicleBody v, HashSet<Int3> cells)
        {
            _v = v;
            _cells = cells;
        }

        public bool TryGetShape(Int3 cell, out BlockShape shape, out int orientation)
        {
            if (_cells.Contains(cell) && _v.Analysis.Cells.TryGetValue(cell, out BlueprintBlock b))
            {
                shape = BlockCatalog.Get(b.Id).Shape;
                orientation = b.O;
                return true;
            }

            shape = BlockShape.Cube;
            orientation = 0;
            return false;
        }
    }

    private Dictionary<int, HashSet<Int3>> MissingBySection()
    {
        // Every cell a missing block used to fill (big blocks fill several), by section.
        var map = new Dictionary<int, HashSet<Int3>>();
        foreach (Int3 anchor in Repair.Missing(Analysis, Model))
        {
            int s = Analysis.SectionOf[anchor];
            if (!map.TryGetValue(s, out HashSet<Int3> set)) map[s] = set = new HashSet<Int3>();
            BlueprintBlock bp = Analysis.Cells[anchor];
            foreach (Int3 c in Footprint.Cells(Analysis.Defs[anchor], anchor, bp.O))
                if (!Model.Blocks.ContainsKey(c)) set.Add(c);
        }

        return map;
    }

    /// <summary>Finds the nearest block, or missing block, along a ray (for the welding torch).</summary>
    public bool WeldPick(Vector3 from, Vector3 dir, float maxLen, out WeldTarget target)
    {
        target = default;
        float best = maxLen;
        bool found = false;
        Dictionary<int, HashSet<Int3>> missing = MissingBySection();
        var hits = new List<RayCellHit>(1);
        foreach (Section s in Model.Sections)
        {
            Transform3D g = View.SectionGlobal(s.Id);
            Transform3D inv = g.AffineInverse();
            System.Numerics.Vector3 o = (inv * from / Conv.CS).N();
            System.Numerics.Vector3 d = (inv.Basis * dir).Normalized().N();

            hits.Clear();
            if (s.Cells.Count > 0) GridRaycast.Cast(s, s.Min, s.Max, o, d, maxLen / Conv.CS, hits, firstOnly: true);
            if (hits.Count > 0 && hits[0].TEnter * Conv.CS < best && s.Cells.TryGetValue(hits[0].Cell, out BlockInstance b))
            {
                best = hits[0].TEnter * Conv.CS;
                target = new WeldTarget
                {
                    Block = b, Cell = b.Cell, Def = b.Def, Distance = best, Point = from + dir * best,
                    Normal = (g.Basis * hits[0].Normal.G()).Normalized(),
                };
                found = true;
            }

            if (!missing.TryGetValue(s.Id, out HashSet<Int3> gone)) continue;
            hits.Clear();
            GridRaycast.Cast(new MissingLookup(this, gone), s.Min, s.Max, o, d, maxLen / Conv.CS, hits, firstOnly: true);
            if (hits.Count == 0 || hits[0].TEnter * Conv.CS >= best) continue;
            best = hits[0].TEnter * Conv.CS;
            target = new WeldTarget
            {
                Missing = true, Cell = Analysis.Cells[hits[0].Cell].Cell, Def = BlockCatalog.Get(Analysis.Cells[hits[0].Cell].Id), Distance = best,
                Point = from + dir * best, Normal = (g.Basis * hits[0].Normal.G()).Normalized(),
            };
            found = true;
        }

        return found;
    }

    /// <summary>One tick of welding. Returns what happened and how far along it is (0 to 1).</summary>
    public (WeldState state, float progress) Weld(WeldTarget t, float dt)
    {
        // A fire within a meter of the torch comes first: the torch's flame-retardant spray puts it out.
        if (FireNear(t.Point, out BlockInstance burning))
        {
            bool outNow = _fires.Douse(burning.Cell, dt);
            if (outNow) RemoveFireFx(burning.Cell);
            return outNow ? (WeldState.PutOut, 1) : (WeldState.Extinguishing, 0.5f);
        }

        if (!t.Missing)
        {
            BlockInstance b = t.Block;
            if (b == null || !b.Alive) return (WeldState.Nothing, 0);
            if (b.Hp >= b.Def.Hp) return (WeldState.AlreadyFixed, 1);
            b.Hp = Mathf.Min(b.Def.Hp, b.Hp + Repair.HpPerSecond * dt);
            return (WeldState.Repairing, b.Hp / b.Def.Hp);
        }

        if (!Repair.CanRebuild(Model, Analysis, t.Cell)) return (WeldState.CantAttach, 0);
        float done = (_rebuildProgress.TryGetValue(t.Cell, out float p) ? p : 0) + dt;
        float need = Repair.RebuildSeconds(t.Def);
        if (done < need)
        {
            _rebuildProgress[t.Cell] = done;
            return (WeldState.Rebuilding, done / need);
        }

        _rebuildProgress.Remove(t.Cell);
        return TryRebuild(t.Cell) ? (WeldState.Rebuilt, 1) : (WeldState.CantAttach, 0);
    }

    /// <summary>Welds a destroyed block (by its anchor cell) back into place, the way the design had it.</summary>
    public bool TryRebuild(Int3 cell)
    {
        if (!Analysis.Cells.TryGetValue(cell, out BlueprintBlock bp) || bp.Cell != cell || Model.Blocks.ContainsKey(cell)) return false;
        int section = Analysis.SectionOf[cell];
        if (!Repair.CanRebuild(Model, Analysis, cell)) return false;

        BlockDef def = BlockCatalog.Get(bp.Id);
        BlockInstance b = Model.Add(def, cell, bp.O, section);
        b.AmmoUnits = 0; // a rebuilt rack is empty until resupplied
        View.AddBlock(b);

        if (def.IsWheelLike)
        {
            Wheels.Clear();
            SetupWheels();
        }

        foreach (SeatRt s in Seats.Where(s => s.Info.Cell == cell))
        {
            s.Block = b;
            s.Occupant = Occupant.Ai; // a fresh crew member climbs in
        }

        foreach (GunRt g in Guns.Where(g => g.Info.Breech == cell)) g.Breech = b;
        foreach (TubeRt t in Tubes.Where(t => t.Info.Cell == cell)) t.Block = b;
        if (_repairGhosts.Remove(cell, out Node3D ghost)) ghost.QueueFree();
        Radius = Mathf.Max(Radius, View.BoundingRadius());
        RefreshStats();
        return true;
    }

    /// <summary>Shows see-through outlines of missing blocks while the player holds a welder nearby.</summary>
    public void SetRepairPreview(bool on)
    {
        if (!on)
        {
            foreach (Node3D g in _repairGhosts.Values)
                if (IsInstanceValid(g)) g.QueueFree();
            _repairGhosts.Clear();
            return;
        }

        _ghostCan ??= Shaders.Hologram(new Color(0.35f, 0.75f, 1f), 0.9f);
        _ghostCant ??= Shaders.Hologram(new Color(0.45f, 0.5f, 0.6f), 0.45f);
        var missing = new HashSet<Int3>(Repair.Missing(Analysis, Model));
        foreach (Int3 c in _repairGhosts.Keys.Where(c => !missing.Contains(c)).ToList())
        {
            if (IsInstanceValid(_repairGhosts[c])) _repairGhosts[c].QueueFree();
            _repairGhosts.Remove(c);
        }

        foreach (Int3 c in missing)
        {
            BlueprintBlock bp = Analysis.Cells[c];
            int section = Analysis.SectionOf[c];
            if (!_repairGhosts.TryGetValue(c, out Node3D ghost))
            {
                float caliber = Analysis.Guns.FirstOrDefault(g => g.Barrels.Contains(c) || g.Brake == c)?.Stats.CaliberMm ?? 76f;
                ghost = BlockVisuals.Create(BlockCatalog.Get(bp.Id), caliber);
                ghost.Transform = Conv.BlockTransform(c, bp.O);
                View.SectionNode(section).AddChild(ghost);
                _repairGhosts[c] = ghost;
            }

            BlockVisuals.SetOverride(ghost, Repair.CanRebuild(Model, Analysis, c) ? _ghostCan : _ghostCant);
        }
    }

    /// <summary>Destroyed blocks waiting to be welded back (fired missiles don't count: supply points rebuild those).</summary>
    public int MissingCount => Repair.Missing(Analysis, Model).Count(c => !Analysis.Defs[c].IsMissilePart);

    /// <summary>Refills ammo racks and fuel a little at a time (while parked at a supply point).</summary>
    public bool Resupply(float dt)
    {
        bool changed = false;
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive || b.Def.Kind != BlockKind.AmmoRack || b.AmmoUnits >= b.Def.AmmoUnits) continue;
            b.AmmoUnits = Mathf.Min(b.Def.AmmoUnits, b.AmmoUnits + 8f * dt);
            changed = true;
        }

        if (FuelL < FuelCapacityL)
        {
            FuelL = Mathf.Min(FuelCapacityL, FuelL + 30f * dt);
            changed = true;
        }

        ReloadSmoke(dt);
        return ResupplyOrdnance(dt) || changed;
    }
}
