using Godot;

namespace DreamGame.Game;

/// <summary>A shell in flight: a glowing head, a streak, and a fading trail behind it.</summary>
public partial class TracerFx : Node3D
{
    public MeshInstance3D Head;
    public MeshInstance3D Streak;
    public float Width;
    public Color Tint;
}
