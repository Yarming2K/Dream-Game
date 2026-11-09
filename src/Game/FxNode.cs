using System;
using Godot;

namespace DreamGame.Game;

/// <summary>A short-lived effect that animates itself over its lifetime, then frees itself.</summary>
public partial class FxNode : Node3D
{
    public float Life = 0.5f;
    public Action<float> Animate;
    private float _t;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float u = Mathf.Clamp(_t / Life, 0, 1);
        Animate?.Invoke(u);
        if (_t >= Life) QueueFree();
    }
}
