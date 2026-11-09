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
    }

    public void Vanish()
    {
        GridRegistry.Remove(this);
        if (IsInstanceValid(this)) QueueFree();
    }
}
