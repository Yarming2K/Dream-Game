using System;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>A steel silhouette that drops when hit and pops back up a few seconds later.</summary>
public partial class PopupTarget : StaticBody3D, IHittable, IBlastable
{
    public event Action<PopupTarget> KnockedDown;
    public float DistanceM;
    private bool _down;
    private Node3D _plate;

    public Vector3 BlastCenter => GlobalPosition + Vector3.Up * 1f;

    public override void _Ready()
    {
        CollisionLayer = GridRegistry.Layers.Targets;
        CollisionMask = 0;
        _plate = new Node3D();
        AddChild(_plate);
        StandardMaterial3D steel = BlockVisuals.Material("target", new Color(0.62f, 0.6f, 0.55f), 0.6f, 0.5f);
        StandardMaterial3D post = BlockVisuals.Material("target-post", new Color(0.2f, 0.2f, 0.2f), 0.8f, 0.4f);
        _plate.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.75f, 0.05f) }, MaterialOverride = steel, Position = new Vector3(0, 0.95f, 0) });
        _plate.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.13f, Height = 0.26f }, MaterialOverride = steel, Position = new Vector3(0, 1.47f, 0) });
        _plate.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.6f, 0.06f) }, MaterialOverride = post, Position = new Vector3(0, 0.3f, 0) });
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.5f, 1.05f, 0.12f) }, Position = new Vector3(0, 1.05f, 0) });
        Hittables.Add(this);
    }

    public override void _ExitTree() => Hittables.Remove(this);

    public void TakeHit(float damage, Vector3 point, Vector3 dir, bool byPlayer) => Knock(point);

    public void TakeBlast(float damage, Vector3 from, bool byPlayer)
    {
        if (damage > 8f) Knock(BlastCenter);
    }

    private void Knock(Vector3 at)
    {
        Sfx.At(GetParent<Node3D>(), at, SoundId.BulletMetal, -2f, 0.7f, 10f);
        if (_down) return;
        _down = true;
        CollisionLayer = 0;
        KnockedDown?.Invoke(this);
        Tween t = CreateTween();
        t.TweenProperty(_plate, "rotation_degrees", new Vector3(-88, 0, 0), 0.25).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        t.TweenInterval(3.0);
        t.TweenProperty(_plate, "rotation_degrees", Vector3.Zero, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenCallback(Callable.From(() =>
        {
            _down = false;
            CollisionLayer = GridRegistry.Layers.Targets;
        }));
    }
}
