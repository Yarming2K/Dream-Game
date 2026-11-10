using System.Linq;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Heavy weapons and how steady the mount is: a building fires them at full rate and every gun on it is
/// steadier; a land vehicle has to stop and put its stabilizers down first, and any vehicle reloads them
/// slower. The stabilizers swing down when a land vehicle stops, and up when it moves off.
/// </summary>
public partial class VehicleBody
{
    private int _stabilizers;
    private float _legs;

    /// <summary>How steady it is right now (building, braced, or loose).</summary>
    public Mount Mount => Emplacement.MountOf(IsBuilding, IsNaval, IsAir, _stabilizers, LinearVelocity.Length());

    public bool HasHeavyWeapons => Guns.Any(g => g.Alive && g.Info.BreechDef.Breech.Heavy);

    public int Stabilizers => _stabilizers;

    private void RefreshStabilizers() => _stabilizers = Model.Instances.Count(b => b.Alive && b.Def.Kind == BlockKind.Stabilizer);

    /// <summary>Whether a gun may fire from this mount right now (heavy weapons need a steady one).</summary>
    private bool MountAllows(GunRt g)
    {
        g.NeedsBrace = !Emplacement.CanFire(g.Info.BreechDef.Breech.Heavy, Mount);
        return !g.NeedsBrace;
    }

    private float ReloadFor(GunRt g) => g.Stats.ReloadS * Emplacement.ReloadFactor(g.Info.BreechDef.Breech.Heavy, Mount);

    private float SpreadFor(GunRt g) => Emplacement.SpreadFactor(g.Info.BreechDef.Breech.Heavy, Mount);

    private void AnimateStabilizers(float dt)
    {
        if (_stabilizers == 0) return;
        bool down = !IsAir && !IsNaval && LinearVelocity.Length() < Emplacement.BraceSpeedMs;
        float was = _legs;
        _legs = Mathf.MoveToward(_legs, down ? 1f : 0f, dt * 1.5f);
        if (Mathf.IsEqualApprox(was, _legs) && _legs is 0f or 1f) return;
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive || b.Def.Kind != BlockKind.Stabilizer) continue;
            Node3D leg = View.VisualOf(b)?.GetNodeOrNull<Node3D>("Leg");
            if (leg != null) leg.Rotation = new Vector3(-1.1f * _legs * _legs * (3f - 2f * _legs), 0, 0);
        }
    }
}
