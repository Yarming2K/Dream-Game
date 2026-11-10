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
    public float SightM => Sensors.SightM(Analysis.Mode, RadarAlive, ConsoleAlive);

    /// <summary>A short name for the map: "Warden", "Corsair".</summary>
    public string ShortName => (Blueprint?.Name ?? "Vehicle").Split(" (")[0];

    /// <summary>Recounts what's left aboard that matters to command (after damage).</summary>
    private void RefreshCommandParts()
    {
        bool console = false, radar = false;
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive) continue;
            if (b.Def.Kind == BlockKind.CommandConsole) console = true;
            else if (b.Def.Kind == BlockKind.Radar) radar = true;
        }

        ConsoleAlive = console;
        RadarAlive = radar;
        if (StartBlocks == 0) StartBlocks = Model.Blocks.Count;
    }

    /// <summary>Health for the map: the share of its cells still filled.</summary>
    public float Health => StartBlocks > 0 ? Mathf.Clamp(Model.Blocks.Count / (float)StartBlocks, 0f, 1f) : 1f;

    /// <summary>Called when a shell or blast from <paramref name="shooter"/> hits this vehicle.</summary>
    public void NoteAttack(VehicleBody shooter)
    {
        if (shooter == null || shooter == this || shooter.Faction == Faction) return;
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
        Order = kind;
        OrderPoint = point;
        OrderTarget = target;
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

    /// <summary>How close it closes in on an attack target before stopping to shoot.</summary>
    private float AttackRange => Guns.Exists(g => g.Alive && g.Stats.CaliberMm >= 150f) ? 2400f : IsNaval ? 900f : 450f;

    /// <summary>Turns the current order into a goal for the AI driver (and, for aircraft, a patrol circle).</summary>
    private void OrdersTick()
    {
        if (Order == OrderKind.None || PlayerSeat != null) return;
        bool targetOk = OrderTarget != null && IsInstanceValid(OrderTarget) && OrderTarget.IsInsideTree() && OrderTarget.Model.Blocks.Count > 0;
        Vector3 goal = OrderPoint;
        float stop = 0f, radius = IsNaval ? 40f : 15f;
        switch (Order)
        {
            case OrderKind.Attack:
                if (!targetOk)
                {
                    GiveOrder(OrderKind.Hold, GlobalPosition);
                    return;
                }

                goal = OrderTarget.GlobalPosition;
                stop = AttackRange;
                break;
            case OrderKind.Escort:
                if (!targetOk)
                {
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
    }
}
