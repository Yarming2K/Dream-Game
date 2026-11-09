using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>Spawns block fragments, capped so big chain reactions don't tank the frame rate.</summary>
public static class Fragments
{
    private const int Max = 150;
    private static readonly Queue<Fragment> Live = new();
    private static readonly RandomNumberGenerator Rng = new();
    private static readonly float[] Sizes = { 0.08f, 0.12f, 0.17f };
    private static readonly Dictionary<float, (BoxShape3D shape, BoxMesh mesh)> Shapes = new();

    public static void Spawn(Node3D world, Vector3 pos, BlockDef def, Vector3 velocity, Vector3 away, int count)
    {
        if (world == null || !world.IsInsideTree()) return;
        Material mat = BlockVisuals.MaterialOf(def);
        for (int i = 0; i < count; i++)
        {
            float size = Sizes[Rng.RandiRange(0, Sizes.Length - 1)];
            if (!Shapes.TryGetValue(size, out (BoxShape3D shape, BoxMesh mesh) sm))
            {
                var dims = new Vector3(size, size * 0.55f, size * 1.25f);
                sm = (new BoxShape3D { Size = dims }, new BoxMesh { Size = dims });
                Shapes[size] = sm;
            }

            var mesh = new MeshInstance3D { Mesh = sm.mesh, MaterialOverride = mat };
            var f = new Fragment
            {
                Mesh = mesh,
                Life = Rng.RandfRange(3.5f, 6f),
                Mass = Mathf.Clamp(size * size * size * 3000f, 0.4f, 8f),
                CollisionLayer = GridRegistry.Layers.Fragments,
                CollisionMask = GridRegistry.Layers.World | GridRegistry.Layers.Structures | GridRegistry.Layers.Vehicles |
                                GridRegistry.Layers.Debris,
                AngularDamp = 0.6f,
                PhysicsMaterialOverride = new PhysicsMaterial { Bounce = 0.25f, Friction = 0.7f },
            };
            f.AddChild(new CollisionShape3D { Shape = sm.shape });
            f.AddChild(mesh);
            world.AddChild(f);
            var jitter = new Vector3(Rng.RandfRange(-1, 1), Rng.RandfRange(-1, 1), Rng.RandfRange(-1, 1));
            f.GlobalPosition = pos + jitter * Conv.CS * 0.3f;
            Vector3 dir = (away + jitter * 0.8f + Vector3.Up * 0.5f).Normalized();
            f.LinearVelocity = velocity + dir * Rng.RandfRange(3f, 9f);
            f.AngularVelocity = jitter * Rng.RandfRange(6f, 16f);

            Live.Enqueue(f);
            while (Live.Count > Max)
            {
                Fragment old = Live.Dequeue();
                if (GodotObject.IsInstanceValid(old)) old.QueueFree();
            }
        }
    }
}
