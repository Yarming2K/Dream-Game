using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>A piece that broke off. It's still made of blocks, so it can be shot apart further.
/// It disappears after a while to keep the frame rate up.</summary>
public partial class DebrisBody : RigidBody3D, IGridHost
{
    public const float Lifetime = 40f;
    public const int MaxPieces = 90;
    private static readonly Queue<DebrisBody> Live = new();

    public GridModel Model { get; private set; }
    public GridView View { get; private set; }
    public PhysicsBody3D Body => this;
    public KeepRule Keep => KeepRule.Heaviest;
    public float Radius { get; private set; }
    private float _age;
    private readonly List<(Vector3 local, float volume)> _floats = new();
    private bool _wasDry = true;

    public static DebrisBody Create(GridModel model, Transform3D where, Vector3 velocity, Vector3 spin)
    {
        var d = new DebrisBody { Name = "Debris", Model = model };
        d.CollisionLayer = GridRegistry.Layers.Debris;
        d.CollisionMask = GridRegistry.Layers.All;
        d.View = new GridView(d, model);
        d.Radius = d.View.BoundingRadius();
        d.Transform = where;
        d.Mass = Mathf.Max(model.TotalMassKg(), 1f);
        d.CenterOfMassMode = CenterOfMassModeEnum.Custom;
        d.CenterOfMass = model.CenterOfMass().G();
        d.LinearVelocity = velocity;
        d.AngularVelocity = spin;
        d.AngularDamp = 0.3f;
        d.LinearDamp = 0.05f;
        d.CountFloats();
        return d;
    }

    public override void _EnterTree()
    {
        GridRegistry.Add(this);
        Live.Enqueue(this);
        while (Live.Count > MaxPieces)
        {
            DebrisBody oldest = Live.Dequeue();
            if (IsInstanceValid(oldest) && oldest != this) oldest.Vanish();
        }
    }

    public override void _ExitTree() => GridRegistry.Remove(this);

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;
        if (_age > Lifetime || GlobalPosition.Y < -60f) Vanish();
        else Float();
    }

    private void CountFloats()
    {
        _floats.Clear();
        foreach (BlockInstance b in Model.Blocks.Values)
            if (b.Alive && b.Def.Displacement > 0) _floats.Add((Conv.CellPos(b.Cell), b.Def.Displacement * Hydro.CellVolume));
    }

    /// <summary>In the sea: fuel tanks bob, armor sinks, and everything is slowed by the water.</summary>
    private void Float()
    {
        bool dry = GlobalPosition.Y - Radius > Ocean.Level;
        if (dry)
        {
            _wasDry = true;
            return;
        }

        Transform3D xf = GlobalTransform;
        Vector3 com = xf * CenterOfMass;
        float cs = Conv.CS, rhoG = Hydro.WaterDensity * GunMath.Gravity;
        Vector3 force = Vector3.Zero, torque = Vector3.Zero;
        bool any = false;
        foreach ((Vector3 local, float volume) in _floats)
        {
            Vector3 p = xf * local;
            float frac = Mathf.Clamp((Ocean.Level - (p.Y - cs * 0.5f)) / cs, 0, 1);
            if (frac <= 0) continue;
            any = true;
            Vector3 pv = LinearVelocity + AngularVelocity.Cross(p - com);
            // Drag works on the whole block, not just its steel.
            Vector3 f = Vector3.Up * rhoG * volume * frac - pv * 900f * Hydro.CellVolume * frac;
            force += f;
            torque += (p - com).Cross(f);
        }

        if (!any) return;
        if (_wasDry && LinearVelocity.Y < -4f) Effects.Splash(GetParent<Node3D>(), GlobalPosition, Mathf.Clamp(Mass / 400f, 0.3f, 2f));
        _wasDry = false;
        ApplyCentralForce(force);
        ApplyTorque(torque);
    }

    public Vector3 VelocityAt(Vector3 worldPoint) =>
        LinearVelocity + AngularVelocity.Cross(worldPoint - GlobalTransform * CenterOfMass);

    public void OnBlocksRemoved()
    {
        if (Model.Blocks.Count == 0)
        {
            Vanish();
            return;
        }

        Mass = Mathf.Max(Model.TotalMassKg(), 1f);
        CenterOfMass = Model.CenterOfMass().G();
        CountFloats();
    }

    public void Vanish()
    {
        GridRegistry.Remove(this);
        if (IsInstanceValid(this)) QueueFree();
    }
}
