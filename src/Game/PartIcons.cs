using System;
using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Renders a small 3D thumbnail of each block for the parts drawer. Each one is drawn in its own tiny
/// viewport for a moment, copied into a normal texture, and the viewport is thrown away.
/// </summary>
public static class PartIcons
{
    private static readonly Dictionary<string, Texture2D> Cache = new();
    private static readonly HashSet<string> Pending = new();
    private static Node _holder;

    /// <summary>Raised when a thumbnail has finished rendering (redraw whatever shows it).</summary>
    public static event Action Updated;

    /// <summary>Thumbnails render under this node while they're being made.</summary>
    public static void Init(Node holder) => _holder = holder;

    /// <summary>The thumbnail, or null while it's still rendering.</summary>
    public static Texture2D Get(BlockDef def)
    {
        if (Cache.TryGetValue(def.Id, out Texture2D tex)) return tex;
        if (Pending.Contains(def.Id) || _holder == null || !_holder.IsInsideTree()) return null;
        Pending.Add(def.Id);

        var vp = new SubViewport
        {
            Size = new Vector2I(128, 128),
            TransparentBg = true,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _holder.AddChild(vp);

        vp.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.58f, 0.62f),
                AmbientLightEnergy = 0.9f,
                TonemapMode = Godot.Environment.ToneMapper.Aces,
            },
        });
        vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-42, -28, 0), LightEnergy = 1.35f });
        vp.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-15, 150, 0), LightEnergy = 0.55f, LightColor = new Color(0.7f, 0.82f, 1f),
        });
        vp.AddChild(BlockVisuals.Create(def, 76f, hints: true));

        Vector3 eye = new Vector3(0.95f, 0.72f, -1.1f).Normalized() * 1.9f;
        var cam = new Camera3D { Fov = 28f, Current = true };
        cam.Transform = new Transform3D(Basis.Identity, eye).LookingAt(Vector3.Zero, Vector3.Up);
        vp.AddChild(cam);

        // Let it draw for a few frames so textures and shaders settle, then keep a copy.
        string id = def.Id;
        _holder.GetTree().CreateTimer(0.5).Timeout += () =>
        {
            Pending.Remove(id);
            if (!GodotObject.IsInstanceValid(vp)) return;
            Image img = vp.GetTexture().GetImage();
            if (img != null && !img.IsEmpty()) Cache[id] = ImageTexture.CreateFromImage(img);
            vp.QueueFree();
            Updated?.Invoke();
        };
        return null;
    }
}
