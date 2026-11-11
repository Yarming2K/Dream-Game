using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>A static block structure standing on the ground: target walls and bunkers. Pieces that lose
/// their footing fall off as debris.</summary>
public partial class StructureBody : StaticBody3D, IGridHost
{
    public GridModel Model { get; private set; }
    public GridView View { get; private set; }
    public PhysicsBody3D Body => this;
    public KeepRule Keep => KeepRule.Anchored;
    public float Radius { get; private set; }
    public string Label = "";

    public static StructureBody Create(Blueprint bp, Transform3D where, string label)
    {
        var s = new StructureBody { Name = "Structure", Label = label };
        s.CollisionLayer = GridRegistry.Layers.Structures;
        s.CollisionMask = 0;
        s.Model = GridModel.FromBlueprint(bp);
        s.View = new GridView(s, s.Model, shareKey: bp, lod: true, wear: 0.5f, mud: 0.25f);
        s.Radius = s.View.BoundingRadius();
        s.Transform = where;
        return s;
    }

    public override void _EnterTree() => GridRegistry.Add(this);

    public override void _Ready()
    {
        // Walls and bunkers hold the ground under them, like buildings.
        Model.RecomputeBounds();
        Int3 min = new(int.MaxValue, 0, int.MaxValue), max = new(int.MinValue, 0, int.MinValue);
        foreach (Int3 c in Model.Blocks.Keys)
        {
            min = Int3.Min(min, c);
            max = Int3.Max(max, c);
        }

        if (Model.Blocks.Count > 0) Terrain.LockUnder(this, min, max);
    }
    public override void _ExitTree()
    {
        _interior?.Clear();
        GridRegistry.Remove(this);
    }

    public Vector3 VelocityAt(Vector3 worldPoint) => Vector3.Zero;

    public void OnBlocksRemoved()
    {
        if (Model.Blocks.Count == 0) Vanish();
    }

    public void Vanish()
    {
        GridRegistry.Remove(this);
        QueueFree();
    }
}
