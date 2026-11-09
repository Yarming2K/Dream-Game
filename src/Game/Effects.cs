using System;
using DreamGame.Core;
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

/// <summary>Muzzle flashes, tracers, impacts and explosions. Simple shapes and lights, cheap to draw.</summary>
public static class Effects
{
    private static SphereMesh _sphere;
    private static SphereMesh Sphere => _sphere ??= new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 };

    private static StandardMaterial3D FadeMat(Color c, bool additive = true) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        AlbedoColor = c,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static FxNode Spawn(Node3D parent, Vector3 pos, float life)
    {
        var fx = new FxNode { Life = life };
        parent.AddChild(fx);
        fx.GlobalPosition = pos;
        return fx;
    }

    private static void Burst(FxNode fx, Color color, int amount, float speed, float size, float life, Vector3 dir,
        float spread, float gravity = -9.8f)
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        var ramp = new Gradient();
        ramp.SetColor(0, color);
        ramp.SetColor(1, new Color(color.R, color.G, color.B, 0f));
        var p = new CpuParticles3D
        {
            Amount = amount,
            Lifetime = life,
            OneShot = true,
            Explosiveness = 0.95f,
            Direction = dir,
            Spread = spread,
            InitialVelocityMin = speed * 0.4f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, gravity, 0),
            ScaleAmountMin = size * 0.5f,
            ScaleAmountMax = size,
            ColorRamp = ramp,
            Mesh = new QuadMesh { Size = new Vector2(1, 1), Material = mat },
            Emitting = true,
        };
        fx.AddChild(p);
    }

    private static void Flash(FxNode fx, Color color, float energy, float range)
    {
        var light = new OmniLight3D { LightColor = color, LightEnergy = energy, OmniRange = range, ShadowEnabled = false };
        fx.AddChild(light);
        Action<float> prev = fx.Animate;
        fx.Animate = u =>
        {
            prev?.Invoke(u);
            light.LightEnergy = energy * Mathf.Max(0, 1f - u * 3f);
        };
    }

    public static void MuzzleFlash(Node3D parent, Vector3 pos, Vector3 dir, float caliber)
    {
        if (parent == null) return;
        float s = caliber / 76f;
        FxNode fx = Spawn(parent, pos, 1.2f);
        var mat = FadeMat(new Color(1f, 0.75f, 0.35f, 1f));
        var ball = new MeshInstance3D { Mesh = Sphere, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        fx.AddChild(ball);
        ball.Scale = Vector3.One * 0.8f * s;
        fx.Animate = u =>
        {
            float k = Mathf.Max(0, 1f - u * 10f);
            ball.Scale = Vector3.One * (0.4f + 1.2f * (1 - k)) * s;
            mat.AlbedoColor = new Color(1f, 0.75f, 0.35f, k);
        };
        Flash(fx, new Color(1f, 0.7f, 0.4f), 8f * s, 6f + 4f * s);
        Burst(fx, new Color(0.55f, 0.53f, 0.5f, 0.55f), 14, 6f * s, 1.2f * s, 1.1f, dir, 25f, 0.4f);
    }

    public static MeshInstance3D Tracer(Node3D parent, float caliber, AmmoType type)
    {
        Color c = type == AmmoType.AP ? new Color(1f, 0.8f, 0.45f) : new Color(1f, 0.45f, 0.25f);
        var mi = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 6, Rings = 1 },
            MaterialOverride = BlockVisuals.Emissive(c, 4f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        mi.SetMeta("width", Mathf.Clamp(caliber / 1000f * 0.6f, 0.02f, 0.07f));
        parent.AddChild(mi);
        return mi;
    }

    public static void UpdateTracer(MeshInstance3D t, Vector3 from, Vector3 to)
    {
        if (t == null || !GodotObject.IsInstanceValid(t)) return;
        Vector3 d = to - from;
        float len = Mathf.Min(d.Length(), 12f);
        if (len < 0.01f) return;
        Vector3 dir = d.Normalized();
        float w = t.GetMeta("width").AsSingle();
        Vector3 up = Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        // The cylinder's long axis is Y; turn it to lie along the flight path, then stretch it.
        Basis basis = Basis.LookingAt(dir, up) * new Basis(Vector3.Right, Mathf.Pi / 2) * Basis.FromScale(new Vector3(w, len, w));
        t.GlobalTransform = new Transform3D(basis, to - dir * len * 0.5f);
        t.Visible = true;
    }

    public static void Impact(Node3D parent, Vector3 pos, Vector3 normal, HitKind kind)
    {
        Color c = kind switch
        {
            HitKind.Penetration => new Color(1f, 0.65f, 0.3f),
            HitKind.Ricochet => new Color(1f, 0.95f, 0.5f),
            _ => new Color(1f, 0.85f, 0.6f),
        };
        FxNode fx = Spawn(parent, pos, 0.8f);
        Burst(fx, c, kind == HitKind.Penetration ? 26 : 16, 9f, 0.12f, 0.45f, normal, 55f);
        Flash(fx, c, 4f, 4f);
    }

    public static void Dust(Node3D parent, Vector3 pos, Vector3 normal, float caliber)
    {
        float s = caliber / 76f;
        FxNode fx = Spawn(parent, pos, 2f);
        Burst(fx, new Color(0.45f, 0.38f, 0.28f, 0.8f), 18, 5f * s, 1.2f * s, 1.6f, normal, 35f, -4f);
    }

    public static void Explosion(Node3D parent, Vector3 pos, float radius)
    {
        FxNode fx = Spawn(parent, pos, 2.5f);
        var mat = FadeMat(new Color(1f, 0.6f, 0.25f, 1f));
        var ball = new MeshInstance3D { Mesh = Sphere, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        fx.AddChild(ball);
        fx.Animate = u =>
        {
            float grow = Mathf.Min(u * 8f, 1f);
            ball.Scale = Vector3.One * Mathf.Max(radius * 2f * grow, 0.01f);
            mat.AlbedoColor = new Color(1f, 0.55f + 0.3f * (1 - grow), 0.25f, Mathf.Max(0, 1f - u * 5f));
        };
        Flash(fx, new Color(1f, 0.6f, 0.3f), 14f, radius * 5f);
        Burst(fx, new Color(1f, 0.6f, 0.25f), 30, radius * 7f, 0.25f, 0.6f, Vector3.Up, 180f);
        Burst(fx, new Color(0.25f, 0.24f, 0.22f, 0.8f), 18, radius * 2f, radius * 1.5f, 2.4f, Vector3.Up, 70f, 0.6f);
    }

    /// <summary>Little chunks flying off a destroyed block.</summary>
    public static void Shatter(Node3D parent, Vector3 pos, BlockDef def)
    {
        FxNode fx = Spawn(parent, pos, 1.4f);
        Burst(fx, new Color(def.ColorR, def.ColorG, def.ColorB), 10, 6f, 0.1f, 1.2f, Vector3.Up, 120f);
    }
}
