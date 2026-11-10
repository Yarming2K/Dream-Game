using System;
using Godot;

namespace DreamGame.Game;

/// <summary>A short-lived effect that animates itself over its lifetime, then frees itself.</summary>
public partial class FxNode : Node3D
{
    public float Life = 0.5f;

    /// <summary>Where the effect started (kept so budget checks don't have to ask the engine).</summary>
    public Vector3 At;
    public Action<float> Animate;
    private float _t;

    /// <summary>How many effects are alive right now, so busy scenes can skip the small ones.</summary>
    public static int Live { get; private set; }

    public override void _EnterTree() => Live++;
    public override void _ExitTree() => Live--;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float u = Mathf.Clamp(_t / Life, 0, 1);
        Animate?.Invoke(u);
        if (_t >= Life) QueueFree();
    }
}
