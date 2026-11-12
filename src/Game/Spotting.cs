using System.Collections.Generic;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// What each side has seen lately. A unit spots enemies within its sight (<see cref="VehicleBody.SightM"/>: aircraft
/// see furthest, radar further still and through the weather) when no hill or smoke screen is in the way; whatever
/// any unit of a side spots, its whole side knows about for a few seconds. Mortars and rocket artillery fire at
/// spotted enemies they can't see themselves (less accurately), so planes, radar and forward units are the
/// artillery's eyes.
/// </summary>
public static class Spotting
{
    /// <summary>How long a side keeps knowing where something is after losing sight of it.</summary>
    public const double KeepS = 4.0;

    private static readonly Dictionary<(int faction, VehicleBody seen), double> Seen = new();
    private static readonly List<VehicleBody> All = new();
    private static readonly List<IGridHost> Near = new();
    private static int _cursor;
    private static ulong _frame;

    /// <summary>Whether <paramref name="faction"/> has seen <paramref name="v"/> in the last few seconds.</summary>
    public static bool IsSpotted(int faction, VehicleBody v)
    {
        Tick();
        return Seen.TryGetValue((faction, v), out double t) && Now - t < KeepS;
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Looks again for a slice of the world's machines each frame (all of them about twice a second).</summary>
    private static void Tick()
    {
        ulong f = Engine.GetProcessFrames();
        if (f == _frame) return;
        _frame = f;
        if (_cursor == 0)
        {
            All.Clear();
            foreach (IGridHost h in GridRegistry.Hosts)
                if (h is VehicleBody v && GodotObject.IsInstanceValid(v) && v.Model.Blocks.Count > 0) All.Add(v);
        }

        int slice = Mathf.Max(8, All.Count / 30);
        double now = Now;
        for (int n = 0; n < slice && _cursor < All.Count; n++, _cursor++)
        {
            VehicleBody target = All[_cursor];
            if (!GodotObject.IsInstanceValid(target)) continue;
            Vector3 tp = target.GlobalPosition + Vector3.Up * 2f;
            Near.Clear();
            WorldQuery.HostsNear(tp, 6000f, Near);
            var looked = new HashSet<int>();
            int rays = 0;
            foreach (IGridHost h in Near)
            {
                if (h is not VehicleBody eye || eye.Faction == target.Faction || looked.Contains(eye.Faction) || !eye.CrewAboardAny) continue;
                Vector3 ep = eye.GlobalPosition + Vector3.Up * 3f;
                if (ep.DistanceTo(tp) > eye.SightM || rays++ >= 6) continue;
                if (WorldQuery.ThroughSmoke(ep, tp) || WorldQuery.GroundRay(null, ep, tp, out _, out _)) continue;
                Seen[(eye.Faction, target)] = now;
                looked.Add(eye.Faction);
            }
        }

        if (_cursor >= All.Count)
        {
            _cursor = 0;
            var stale = new List<(int, VehicleBody)>();
            foreach (var kv in Seen)
                if (now - kv.Value > KeepS * 3 || !GodotObject.IsInstanceValid(kv.Key.seen)) stale.Add(kv.Key);
            foreach (var k in stale) Seen.Remove(k);
        }
    }
}
