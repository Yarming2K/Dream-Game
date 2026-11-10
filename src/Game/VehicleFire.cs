using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Fires aboard: hits on fuel, engines, ammunition and the like can start them; they burn their block down,
/// spread to flammable neighbors, and burn out (sooner with crew aboard). Flooding drowns them; a welding
/// torch played on one puts it out. A block that burns away goes up the way it would if it were shot.
/// </summary>
public partial class VehicleBody
{
    private readonly FireSim _fires = new(System.Random.Shared.Next());
    private readonly Dictionary<Int3, Node3D> _fireFx = new();
    private readonly List<(Int3 anchor, float damage)> _fireDamage = new();
    private readonly List<Int3> _fireLit = new();
    private Dictionary<Int3, Compartment> _compartmentAt;
    private float _fireClock;

    /// <summary>How many blocks are burning aboard.</summary>
    public int FiresBurning => _fires.Count;

    public bool IsBurning(BlockInstance b) => b != null && _fires.IsBurning(b.Cell);

    private bool CrewAboard => Seats.Any(s => SeatAlive(s) && s.Occupant != Occupant.Empty);

    /// <summary>A block was hit and survived: it may catch fire.</summary>
    public void HitBy(BlockInstance b, float damage)
    {
        _lastHitAt = Time.GetTicksMsec() / 1000.0;
        if (b == null || !b.Alive || !Fire.Burns(b.Def)) return;
        BlockInstance anchor = Model.Blocks.TryGetValue(b.Cell, out BlockInstance a) ? a : b;
        if (!_fires.MaybeIgnite(anchor.Cell, anchor.Def, damage, CrewAboard)) return;
        if (_fires.Count == 1) Status?.Invoke("Fire aboard!");
        AddFireFx(anchor);
        Sfx.At(GetParent<Node3D>(), View.BlockWorldPos(anchor), SoundId.ExplosionSmall, -12f, 1.5f, 8f);
    }

    private void FireTick(float dt)
    {
        if (_fires.Count == 0) return;
        _fireClock += dt;
        if (_fireClock < 0.25f) return;
        float step = _fireClock;
        _fireClock = 0;
        _fireDamage.Clear();
        _fireLit.Clear();
        _fires.Step(step, c => Model.Blocks.TryGetValue(c, out BlockInstance b) && b.Alive ? (b.Def, b.Cell) : null, Flooded, CrewAboard,
            _fireDamage, _fireLit);
        foreach (Int3 c in _fireLit)
            if (Model.Blocks.TryGetValue(c, out BlockInstance b)) AddFireFx(b);
        foreach ((Int3 c, float dmg) in _fireDamage)
            if (Model.Blocks.TryGetValue(c, out BlockInstance b) && b.Alive) Combat.Instance?.DamageBlock(this, b, dmg);

        // Flames that went out (or whose block is gone) are taken away; the rest flicker.
        foreach (Int3 c in _fireFx.Keys.ToList())
        {
            if (!_fires.IsBurning(c)) RemoveFireFx(c);
            else if (_fireFx[c].GetNodeOrNull<OmniLight3D>("Light") is { } l) l.LightEnergy = 1.2f + 1.4f * _rng.Randf();
        }
    }

    /// <summary>Is this block under the water inside the hull (or under the sea, for a land vehicle)?</summary>
    private bool Flooded(Int3 cell)
    {
        if (!Model.Blocks.TryGetValue(cell, out BlockInstance b)) return false;
        if (!IsNaval) return View.BlockWorldPos(b).Y < Ocean.Level - 0.3f;
        if (_compartmentAt == null)
        {
            _compartmentAt = new Dictionary<Int3, Compartment>();
            foreach (Compartment comp in Analysis.Compartments)
                foreach (Int3 c in comp.Cells) _compartmentAt[c] = comp;
        }

        foreach (Int3 d in Int3.Directions)
            if (_compartmentAt.TryGetValue(cell + d, out Compartment comp) && Flood.SurfaceY(comp) > cell.Y) return true;
        return false;
    }

    /// <summary>A fire within about a meter of a point (where a welding torch is pointed).</summary>
    private bool FireNear(Vector3 point, out BlockInstance burning)
    {
        burning = null;
        float best = 1.3f;
        foreach (Int3 c in _fires.Burning)
        {
            if (!Model.Blocks.TryGetValue(c, out BlockInstance b) || !b.Alive) continue;
            float d = View.BlockWorldPos(b).DistanceTo(point);
            if (d >= best) continue;
            best = d;
            burning = b;
        }

        return burning != null;
    }

    private static int _fireLights;

    private void AddFireFx(BlockInstance b)
    {
        if (_fireFx.ContainsKey(b.Cell)) return;
        Node3D parent = View.SectionNode(b.Section);
        float size = Mathf.Clamp(Mathf.Pow(b.Def.Volume, 1f / 3f), 1f, 3f);
        Node3D fx = Effects.FireLoop(parent, View.BlockWorldPos(b), size, _fireLights < 10);
        if (fx.GetNodeOrNull("Light") != null)
        {
            _fireLights++;
            fx.TreeExiting += () => _fireLights--;
        }

        _fireFx[b.Cell] = fx;
    }

    private void RemoveFireFx(Int3 cell)
    {
        if (!_fireFx.Remove(cell, out Node3D fx) || !IsInstanceValid(fx)) return;
        Effects.StopFireLoop(fx);
    }
}
