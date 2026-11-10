using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// What a unit is doing, in words, and what it reports back to command: a callsign ("Warden 2"), its
/// activity ("moving · 320 m to go", "firing at Jackal", "in position", "can't get through"), and the
/// reports in the <see cref="CommandLog"/>: in position, target destroyed, under fire, badly damaged,
/// knocked out, stuck. Units on your side also carry a label over them saying who they are and what
/// they're doing.
/// </summary>
public partial class VehicleBody
{
    private static readonly Dictionary<(int, string), int> CallsignCount = new();
    private string _callsign;

    /// <summary>The design's short name and a number, counted per side: "Warden 2".</summary>
    public string Callsign => _callsign ??= NextCallsign();

    /// <summary>Starts the numbering again (a new battle).</summary>
    public static void ResetCallsigns() => CallsignCount.Clear();

    private string NextCallsign()
    {
        var key = (Faction, ShortName);
        CallsignCount.TryGetValue(key, out int n);
        CallsignCount[key] = ++n;
        return $"{ShortName} {n}";
    }

    /// <summary>How others name it: your units by callsign, the enemy's as "enemy Warden".</summary>
    public string NameForYou => Faction == 0 ? Callsign : $"enemy {ShortName}";

    private static double Clock => Time.GetTicksMsec() / 1000.0;
    private double _lastFiredAt = -999;

    /// <summary>Its guns have fired in the last few seconds.</summary>
    public bool FiringNow => Clock - _lastFiredAt < 3.0;

    /// <summary>What its gunners are shooting at, if anything.</summary>
    public VehicleBody ShootingAt => FiringNow && _aiVehicleTarget != null && IsInstanceValid(_aiVehicleTarget) ? _aiVehicleTarget : null;

    public enum Doing { Idle, Moving, Holding, Closing, Firing, Escorting, Patrolling, Stuck, Out }

    /// <summary>No working crew left aboard: it's out of the fight.</summary>
    public bool KnockedOut => Seats.Count > 0 && !Seats.Any(SeatAlive);

    public Doing Activity
    {
        get
        {
            if (KnockedOut) return Doing.Out;
            if (Stuck) return Doing.Stuck;
            if (FiringNow) return Doing.Firing;
            return Order switch
            {
                OrderKind.Move => Doing.Moving,
                OrderKind.Attack => Doing.Closing,
                OrderKind.Escort => Doing.Escorting,
                OrderKind.Patrol => Doing.Patrolling,
                OrderKind.Hold => Doing.Holding,
                _ => Doing.Idle,
            };
        }
    }

    private static string Dist(float m) => m >= 1000f ? $"{m / 1000f:0.0} km" : $"{m:0} m";

    /// <summary>What it's doing, in a few words, for the map and its label.</summary>
    public string ActivityText
    {
        get
        {
            if (KnockedOut) return "knocked out";
            bool targetOk = OrderTarget != null && IsInstanceValid(OrderTarget) && OrderTarget.Model.Blocks.Count > 0;
            string text = Order switch
            {
                OrderKind.Move => Stuck ? "can't get through" : $"moving · {Dist(Flat(OrderPoint).DistanceTo(Flat(GlobalPosition)))} to go",
                OrderKind.Attack when targetOk => GlobalPosition.DistanceTo(OrderTarget.GlobalPosition) > AttackRange + 30f
                    ? $"closing on {OrderTarget.NameForYou} · {Dist(GlobalPosition.DistanceTo(OrderTarget.GlobalPosition))}"
                    : $"attacking {OrderTarget.NameForYou}",
                OrderKind.Escort when targetOk => $"escorting {OrderTarget.NameForYou}",
                OrderKind.Patrol => Stuck ? "can't get through" : "patrolling",
                OrderKind.Hold => Analysis.IsBuilding ? "manned" : "in position",
                _ => "waiting for orders",
            };
            VehicleBody at = ShootingAt;
            if (at != null && !(Order == OrderKind.Attack && at == OrderTarget)) text += $" · firing at {at.NameForYou}";
            else if (FiringNow && at == null) text += " · firing";
            if (UnderFire) text += " · under fire";
            return text;
        }
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    // ---------- getting stuck ----------

    private double _progressAt;
    private float _bestGoalDist = float.MaxValue, _stuckFor;

    /// <summary>It's been trying to get somewhere for a while and getting no closer.</summary>
    public bool Stuck => _stuckFor >= 9f;

    private void ResetProgress()
    {
        _bestGoalDist = float.MaxValue;
        _stuckFor = 0;
        _progressAt = Clock;
    }

    /// <summary>Every few seconds: is it any nearer where it's going? (Not counting stops to shoot.)</summary>
    private void TrackProgress(Vector3 goal, float radius, float stop)
    {
        if (Clock - _progressAt < 3.0) return;
        float step = (float)(Clock - _progressAt);
        _progressAt = Clock;
        float d = Flat(goal).DistanceTo(Flat(GlobalPosition));
        bool shooting = StopToShoot > 0 && _aiVehicleTarget != null && IsInstanceValid(_aiVehicleTarget) &&
                        _aiVehicleTarget.GlobalPosition.DistanceTo(GlobalPosition) < StopToShoot;
        if (d < _bestGoalDist - 3f || d <= radius * 2f + stop || shooting)
        {
            _bestGoalDist = Mathf.Min(_bestGoalDist, d);
            if (Stuck) Report($"is moving again", CommandLog.Calm);
            _stuckFor = 0;
            return;
        }

        bool was = Stuck;
        _stuckFor += step;
        if (!was && Stuck) Report("can't get through: give it another route", CommandLog.Warn);
    }

    // ---------- reports ----------

    private float _reportedHealth = 1f;
    private bool _reportedOut;
    private double _underFireReportedAt = -999;

    /// <summary>A line in the command log about this unit (your side only).</summary>
    private void Report(string what, Color color)
    {
        if (Faction != 0) return;
        CommandLog.Post($"{Callsign} {what}", color, this);
    }

    private void ReportGrouped(string group, string what, Color color)
    {
        if (Faction != 0) return;
        CommandLog.PostGrouped(group, Callsign, what, color, this);
    }

    /// <summary>After damage: badly hurt, knocked out, or (for the enemy) destroyed by one of yours.</summary>
    private void ReportDamage()
    {
        if (_reportedOut) return;
        if (KnockedOut || Model.Blocks.Count == 0)
        {
            _reportedOut = true;
            if (Faction == 0) Report("is knocked out", CommandLog.Bad);
            else if (LastAttacker != null && IsInstanceValid(LastAttacker) && LastAttacker.Faction == 0)
                CommandLog.Post($"{LastAttacker.Callsign} knocked out an enemy {ShortName}", CommandLog.Good, LastAttacker);
            return;
        }

        if (Faction != 0) return;
        float h = Health;
        if (h < 0.3f && _reportedHealth >= 0.3f) Report($"is badly damaged ({h * 100:0}% left)", CommandLog.Bad);
        else if (h < 0.6f && _reportedHealth >= 0.6f) Report($"is damaged ({h * 100:0}% left)", CommandLog.Warn);
        if (h < 0.6f) _reportedHealth = Mathf.Min(_reportedHealth, h < 0.3f ? 0.29f : 0.59f);
    }

    /// <summary>The first hit after a quiet spell.</summary>
    private void ReportUnderFire(VehicleBody shooter)
    {
        if (Faction != 0 || Clock - _underFireReportedAt < 25.0) return;
        _underFireReportedAt = Clock;
        Report(shooter != null ? $"is under fire from an enemy {shooter.ShortName}" : "is under fire", CommandLog.Warn);
    }

    // ---------- the label over it ----------

    /// <summary>The marker over the unit (set by whoever put it on the field), which the unit keeps up to date.</summary>
    public Label3D Marker;

    private double _markerAt;

    private void MarkerTick()
    {
        if (Marker == null || !IsInstanceValid(Marker) || Clock - _markerAt < 0.5) return;
        _markerAt = Clock;
        // Only the units you've given orders to say what they're doing (a battle drill's hundred would be a wall of text).
        Marker.Visible = Order != OrderKind.None;
        if (!Marker.Visible) return;
        if (Faction != 0)
        {
            Marker.Text = KnockedOut ? "" : "◆";
            return;
        }

        string glyph = Activity switch
        {
            Doing.Moving => "▸",
            Doing.Closing => "⌖",
            Doing.Firing => "✸",
            Doing.Escorting => "⛉",
            Doing.Patrolling => "↻",
            Doing.Stuck => "!",
            Doing.Out => "✕",
            Doing.Holding => "■",
            _ => "·",
        };
        Marker.Text = KnockedOut ? $"{Callsign}  ✕ knocked out" : $"{Callsign}  {glyph}\n{ActivityText}";
        Marker.Modulate = KnockedOut ? new Color(0.6f, 0.6f, 0.6f) : Stuck ? CommandLog.Warn : new Color(0.35f, 0.62f, 1f);
    }
}
