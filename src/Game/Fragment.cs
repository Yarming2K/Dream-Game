using Godot;

namespace DreamGame.Game;

/// <summary>A small chunk of a destroyed block. It bounces around for a few seconds, then shrinks away.</summary>
public partial class Fragment : RigidBody3D
{
    public float Life = 5f;
    public MeshInstance3D Mesh;
    private float _age;

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;
        if (_age < Life) return;
        float k = 1f - (_age - Life) / 0.6f;
        if (k <= 0f)
        {
            QueueFree();
            return;
        }

        Mesh.Scale = Vector3.One * k;
    }
}
