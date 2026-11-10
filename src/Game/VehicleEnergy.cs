using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Electricity aboard: generators (burning fuel) and reactors charge the vehicle's power bank, capacitors
/// hold the charge, and railguns and lasers take it out shot by shot. Losing a generator or a capacitor
/// to damage loses what it made or held.
/// </summary>
public partial class VehicleBody
{
    /// <summary>The vehicle's electricity: what's made, what's stored, and what's left for the next shot.</summary>
    public readonly PowerBank Electric = new();

    private float _reactorMW, _generatorMW, _storeMJ;
    private bool _electricReady;

    /// <summary>True if anything aboard makes or stores electricity.</summary>
    public bool HasElectric => _storeMJ > 0;

    /// <summary>Recounts the generators, reactors and capacitors still working.</summary>
    private void RefreshPower()
    {
        float reactor = 0, gen = 0, store = 0;
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive) continue;
            switch (b.Def.Kind)
            {
                case BlockKind.Reactor:
                    reactor += b.Def.ElectricKw;
                    break;
                case BlockKind.Generator:
                    gen += b.Def.ElectricKw;
                    break;
                case BlockKind.Capacitor:
                    store += b.Def.StoreMJ;
                    break;
            }
        }

        _reactorMW = reactor / 1000f;
        _generatorMW = gen / 1000f;
        _storeMJ = DreamGame.Core.Power.CapacityMJ(store, reactor + gen);
        // A vehicle rolls out of the depot fully charged.
        ConfigureElectric(fill: !_electricReady);
        _electricReady = true;
    }

    private void ConfigureElectric(bool fill = false)
    {
        // Generators stop when the fuel runs out; a reactor doesn't need any.
        float gen = FuelL > 0 ? _generatorMW : 0f;
        Electric.Configure(_reactorMW + gen, gen, _storeMJ, fill);
    }

    private void PowerTick(float dt)
    {
        if (_storeMJ <= 0) return;
        ConfigureElectric();
        float fuelled = Electric.Charge(dt);
        if (fuelled > 0) FuelL = Mathf.Max(0, FuelL - fuelled * DreamGame.Core.Power.FuelPerMJ);
    }

    /// <summary>One laser pulse, if the capacitors have the charge for it.</summary>
    private bool FireBeam(GunRt g, bool byPlayer)
    {
        (Vector3 muzzle, Vector3 dir) = Muzzle(g);
        g.Masked = !LineOfFireClear(muzzle, dir);
        if (g.Masked) return false;
        g.Charging = !Electric.TryDraw(g.Stats.ChargeMJ);
        if (g.Charging) return false;
        Combat.Instance.FireBeam(this, muzzle, dir, g.Stats.BeamRangeM, g.Stats.ApDamage, byPlayer);
        g.ReloadLeft = ReloadFor(g);
        if (_rng.Randf() < 0.5f) Sfx.At(GetParent<Node3D>(), muzzle, SoundId.LaserZap, -4f, _rng.RandfRange(0.92f, 1.08f), 14f, delayByDistance: false);
        Fired?.Invoke(g);
        return true;
    }
}
