using System.Linq;
using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Orders from fleet command, carried out by the AI driver and gunners: move (to a formation slot), attack
/// one target, escort a friendly, patrol between two points, or hold. Rules of engagement say when the
/// gunners may shoot. Units keep their last orders if the command console goes, or they leave its range.
/// </summary>
public partial class VehicleBody
{
    public OrderKind Order { get; private set; }
    public Vector3 OrderPoint { get; private set; }
    public VehicleBody OrderTarget { get; private set; }
    public Engagement Roe = Engagement.FireAtWill;
    private Vector3 _patrolA, _patrolB, _escortOffset;
    private bool _patrolOut;

    /// <summary>Who shot at this vehicle last, and when (for return fire).</summary>
    public VehicleBody LastAttacker { get; private set; }
    private double _lastAttackedAt = -999;

    /// <summary>How many blocks it started with (for health bars).</summary>
    public int StartBlocks { get; private set; }

    public bool ConsoleAlive { get; private set; }
    public bool RadarAlive { get; private set; }

    /// <summary>How far this unit spots enemies, in meters.</summary>
    /// <summary>How far it spots enemies right now: night, fog and rain shorten it, unless it has radar.</summary>
    public float SightM => Sensors.SightM(Analysis.Mode, RadarAlive, ConsoleAlive) * Fittings.SightFactor(_periscopes) *
                           (RadarAlive ? 1f : Atmosphere.SightFactor);

    /// <summary>A short name for the map: "Warden", "Corsair".</summary>
    public string ShortName => (Blueprint?.Name ?? "Vehicle").Split(" (")[0];

    /// <summary>Recounts what's left aboard that matters to command (after damage).</summary>
    private void RefreshCommandParts()
    {
        ConsoleAlive = Model.CountOf(BlockKind.CommandConsole) > 0;
        RadarAlive = Model.CountOf(BlockKind.Radar) > 0;
        _radarsDirty = true;
        if (StartBlocks == 0) StartBlocks = Model.Blocks.Count;
    }

    /// <summary>Seconds for a turning radar antenna to sweep once round.</summary>
    private const float RadarSweepS = 2.5f;

    private readonly List<Node3D> _radarSpinners = new();
    private bool _radarsDirty = true;

    /// <summary>Turning radar antennas sweep round and round while they're in one piece.</summary>
    private void AnimateRadars(float dt)
    {
        if (_radarsDirty)
        {
            _radarsDirty = false;
            _radarSpinners.Clear();
            foreach (BlockInstance b in Model.Instances)
                if (b.Alive && BlockModels.Turns(b.Def) && View.VisualOf(b)?.GetNodeOrNull<Node3D>("Spinner") is Node3D n)
                    _radarSpinners.Add(n);
        }

        foreach (Node3D n in _radarSpinners)
            if (IsInstanceValid(n)) n.RotateY(dt * Mathf.Tau / RadarSweepS);
    }

    /// <summary>Health for the map: the share of its cells still filled.</summary>
    public float Health => StartBlocks > 0 ? Mathf.Clamp(Model.Blocks.Count / (float)StartBlocks, 0f, 1f) : 1f;

    /// <summary>Called when a shell or blast from <paramref name="shooter"/> hits this vehicle.</summary>
    public void NoteAttack(VehicleBody shooter)
    {
        if (shooter == null || shooter == this || shooter.Faction == Faction) return;
        if (!UnderFire) ReportUnderFire(shooter);
        LastAttacker = shooter;
        _lastAttackedAt = Time.GetTicksMsec() / 1000.0;
    }

    /// <summary>True while it's been shot at in the last 20 seconds by something still alive.</summary>
    public bool UnderFire => LastAttacker != null && IsInstanceValid(LastAttacker) && Time.GetTicksMsec() / 1000.0 - _lastAttackedAt < 20.0;

    /// <summary>
    /// Gives an order. <paramref name="point"/> is the place (move, patrol, hold), <paramref name="target"/> the unit
    /// (attack, escort). An escort keeps its current offset from the unit it guards.
    /// </summary>
    public void GiveOrder(OrderKind kind, Vector3 point, VehicleBody target = null)
    {
        // Told to attack something it can't hit: it says why and holds, instead of driving at it uselessly.
        if (kind == OrderKind.Attack && target != null && CantAttack(target) is string why)
        {
            ReportGrouped("cant-attack", why + ": holding", CommandLog.Warn);
            kind = OrderKind.Hold;
            point = GlobalPosition;
            target = null;
        }

        Order = kind;
        OrderPoint = point;
        OrderTarget = target;
        ResetProgress();
        Unpark();
        switch (kind)
        {
            case OrderKind.Patrol:
                _patrolA = GlobalPosition;
                _patrolB = point;
                _patrolOut = true;
                break;
            case OrderKind.Escort when target != null:
            {
                Vector3 off = GlobalPosition - target.GlobalPosition;
                off.Y = 0;
                float keep = Mathf.Clamp(off.Length(), target.Radius + Radius + 6f, target.Radius + Radius + 40f);
                _escortOffset = off.LengthSquared() > 1f ? off.Normalized() * keep : Vector3.Right * keep;
                break;
            }
        }
    }

    /// <summary>
    /// Why this unit can't attack <paramref name="t"/>, or null if it can: no weapons, nothing that reaches aircraft (for an
    /// aircraft target), only anti-aircraft guns (for anything on the ground), or (a building, which can't move) too far.
    /// </summary>
    private string CantAttack(VehicleBody t)
    {
        bool air = t.IsAir && !t.OnGround;
        bool missiles = Analysis.Missiles.Count > 0;
        if (!Guns.Exists(g => g.Alive) && !missiles && BombsLeft == 0) return "has no weapons";
        if (air && !IsAir)
        {
            bool aa = HasFlak || HasSams || Guns.Exists(g => g.Alive && (g.Info.BreechDef.Breech.AntiAir ||
                                                                          g.Info.BreechDef.Breech.PointDefense || g.Info.BreechDef.Breech.MaxElevationDeg >= 60f));
            if (!aa) return "can't hit aircraft: no anti-aircraft weapons";
        }
        else if (!air)
        {
            bool ground = BombsLeft > 0 || Analysis.Missiles.Any(m => m.Stats.Seeker != SeekerKind.Radar) ||
                          Guns.Exists(g => g.Alive && !g.Info.BreechDef.Breech.AntiAir);
            if (!ground) return "its guns only fire at aircraft";
        }

        if (Analysis.IsBuilding)
        {
            float d = GlobalPosition.DistanceTo(t.GlobalPosition);
            float reach = air ? (HasHeavyFlak ? 7000f : HasSams ? 6000f : 2500f) : Mathf.Max(AttackRange * 1.25f, missiles ? 4000f : 0f);
            if (d > reach) return $"out of reach: {d / 1000f:0.0} km away, its weapons reach {reach / 1000f:0.0} km";
        }

        return null;
    }

    /// <summary>How close it closes in on an attack target before stopping to shoot.</summary>
    private float AttackRange =>
        Guns.Exists(g => g.Alive && g.Info.BreechDef.Breech.HighArc) ? 4500f
        : Guns.Exists(g => g.Alive && g.Info.BreechDef.Breech.Rocket) ? 2500f
        : Guns.Exists(g => g.Alive && (g.Stats.CaliberMm >= 150f || g.Info.BreechDef.Breech.Rail)) ? 2400f
        : IsNaval ? 900f : 450f;

    /// <summary>Turns the current order into a goal for the AI driver (and, for aircraft, a patrol circle).</summary>
    private void OrdersTick()
    {
        // A building doesn't go anywhere: its orders only say what its gunners shoot at.
        if (Order == OrderKind.None || PlayerSeat != null || Analysis.IsBuilding) return;
        bool targetOk = OrderTarget != null && IsInstanceValid(OrderTarget) && OrderTarget.IsInsideTree() && OrderTarget.Model.Blocks.Count > 0;
        Vector3 goal = OrderPoint;
        float stop = 0f, radius = IsNaval ? 40f : 15f;
        switch (Order)
        {
            case OrderKind.Attack:
                if (!targetOk)
                {
                    ReportGrouped("target-done", "has no target left: holding", CommandLog.Good);
                    GiveOrder(OrderKind.Hold, GlobalPosition);
                    return;
                }

                goal = OrderTarget.GlobalPosition;
                stop = AttackRange;
                break;
            case OrderKind.Escort:
                if (!targetOk)
                {
                    ReportGrouped("escort-lost", "lost the unit it was escorting: holding", CommandLog.Warn);
                    GiveOrder(OrderKind.Hold, GlobalPosition);
                    return;
                }

                goal = OrderTarget.GlobalPosition + _escortOffset;
                radius = 8f;
                break;
            case OrderKind.Patrol:
                goal = _patrolOut ? _patrolB : _patrolA;
                if (AtGoal && GlobalPosition.DistanceTo(goal) < radius * 1.5f)
                {
                    _patrolOut = !_patrolOut;
                    goal = _patrolOut ? _patrolB : _patrolA;
                }

                break;
            case OrderKind.Move:
                if (AtGoal && GlobalPosition.DistanceTo(OrderPoint) < radius * 1.5f)
                {
                    ReportGrouped("arrived", "in position", CommandLog.Good);
                    GiveOrder(OrderKind.Hold, OrderPoint);
                    return;
                }

                break;
        }

        if (IsAir)
        {
            // Aircraft circle over the point they're sent to (tighter over an attack target).
            Patrols = true;
            PatrolCenter = goal;
            PatrolRadius = Order == OrderKind.Attack ? 180f : 260f;
            return;
        }

        AiDriving = true;
        MoveGoal = goal;
        StopToShoot = stop;
        GoalRadius = radius;
        if (Order != OrderKind.Hold) TrackProgress(goal, radius, stop);
    }
}
