using System;
using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>
/// Fires aboard. Fuel, engines, generators, ammunition, propellant and missile motors can catch fire when
/// they're hit and survive it. A fire burns its block down a little every second, spreads to flammable
/// neighbors now and then, and burns itself out after a while (faster with crew aboard fighting it).
/// Water puts it out, and so does someone with a welding torch. If the burning block is destroyed, it
/// goes up the way it would if it were shot: fuel bursts, ammunition cooks off.
/// </summary>
public static class Fire
{
    /// <summary>How readily a block burns, 0 (never) to 1.</summary>
    public static float Flammability(BlockDef d) => d.Kind switch
    {
        BlockKind.FuelTank => 0.7f,
        BlockKind.Propellant => 0.6f,
        BlockKind.MissileMotor => 0.5f,
        BlockKind.Engine => 0.4f,
        BlockKind.Generator => 0.35f,
        BlockKind.AmmoRack => d.Ammo == AmmoType.Torpedo ? 0.15f : 0.3f,
        BlockKind.BombRack => 0.25f,
        BlockKind.Capacitor => 0.2f,
        BlockKind.CommandConsole or BlockKind.Radar => 0.15f,
        BlockKind.DriverSeat or BlockKind.GunnerSeat or BlockKind.CommanderSeat => 0.1f,
        BlockKind.GasCell => 0.5f,
        _ => 0f,
    };

    public static bool Burns(BlockDef d) => Flammability(d) > 0;

    /// <summary>Damage a fire does to its own block each second.</summary>
    public const float BurnDps = 9f;

    /// <summary>How long a fire lasts if nothing puts it out (seconds), and with crew aboard fighting it.</summary>
    public const float BurnS = 32f, CrewedBurnS = 18f;

    /// <summary>Chance per second, times the neighbor's flammability, that a fire spreads to a neighbor.</summary>
    public const float SpreadPerS = 0.2f;

    /// <summary>Seconds of welding torch on a fire to put it out.</summary>
    public const float ExtinguishS = 1.2f;

    /// <summary>Chance a hit sets a block alight: its flammability, scaled by how hard it was hit (a share of its health).</summary>
    public static float IgniteChance(BlockDef d, float damage) =>
        d.Hp <= 0 ? 0 : Flammability(d) * Math.Clamp(damage / d.Hp * 1.5f, 0.15f, 1f);
}

/// <summary>The fires burning on one vehicle, by block (anchor cell). Deterministic for a given seed.</summary>
public sealed class FireSim
{
    private readonly Dictionary<Int3, float> _left = new();
    private readonly Dictionary<Int3, float> _douse = new();
    private readonly Random _rng;
    private readonly List<Int3> _scratch = new();

    public FireSim(int seed = 1) => _rng = new Random(seed);

    /// <summary>The blocks burning right now (anchor cells).</summary>
    public IReadOnlyCollection<Int3> Burning => _left.Keys;

    public int Count => _left.Count;

    public bool IsBurning(Int3 anchor) => _left.ContainsKey(anchor);

    /// <summary>Sets a block alight (if it can burn and isn't already). Returns true if it caught.</summary>
    public bool Ignite(Int3 anchor, BlockDef def, bool crewed = false)
    {
        if (def == null || !Fire.Burns(def) || _left.ContainsKey(anchor)) return false;
        _left[anchor] = crewed ? Fire.CrewedBurnS : Fire.BurnS;
        return true;
    }

    /// <summary>Rolls whether a hit sets the block alight.</summary>
    public bool MaybeIgnite(Int3 anchor, BlockDef def, float damage, bool crewed = false) =>
        def != null && _rng.NextDouble() < Fire.IgniteChance(def, damage) && Ignite(anchor, def, crewed);

    public void Extinguish(Int3 anchor)
    {
        _left.Remove(anchor);
        _douse.Remove(anchor);
    }

    /// <summary>Someone's playing a torch on the fire. Returns true once it's out.</summary>
    public bool Douse(Int3 anchor, float dt)
    {
        if (!_left.ContainsKey(anchor)) return true;
        _douse.TryGetValue(anchor, out float t);
        t += dt;
        if (t < Fire.ExtinguishS)
        {
            _douse[anchor] = t;
            return false;
        }

        Extinguish(anchor);
        return true;
    }

    /// <summary>
    /// Burns for <paramref name="dt"/> seconds. <paramref name="at"/> gives the living block at a cell (null
    /// if none) and its anchor; <paramref name="underWater"/> says whether a cell is flooded. Fires on blocks
    /// that are gone or under water go out. Each burning block's damage goes in <paramref name="damage"/>,
    /// and newly lit blocks in <paramref name="lit"/>.
    /// </summary>
    public void Step(float dt, Func<Int3, (BlockDef def, Int3 anchor)?> at, Func<Int3, bool> underWater, bool crewed,
        List<(Int3 anchor, float damage)> damage, List<Int3> lit)
    {
        _scratch.Clear();
        _scratch.AddRange(_left.Keys);
        foreach (Int3 c in _scratch)
        {
            if (at(c) is not { } here || here.anchor != c || underWater(c))
            {
                Extinguish(c);
                continue;
            }

            float left = _left[c] - dt;
            if (left <= 0)
            {
                Extinguish(c);
                continue;
            }

            _left[c] = left;
            damage.Add((c, Fire.BurnDps * dt));
            foreach (Int3 d in Int3.Directions)
            {
                if (at(c + d) is not { } n || n.anchor == c || _left.ContainsKey(n.anchor) || underWater(n.anchor)) continue;
                if (_rng.NextDouble() >= Fire.SpreadPerS * Fire.Flammability(n.def) * dt) continue;
                if (Ignite(n.anchor, n.def, crewed)) lit.Add(n.anchor);
            }
        }
    }

    public void Clear()
    {
        _left.Clear();
        _douse.Clear();
    }
}
