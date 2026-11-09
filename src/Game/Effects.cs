using System;
using System.Collections.Generic;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Muzzle blasts, tracers, impacts and explosions, built from layered soft particles, flashes of light,
/// decals and sound. Everything is sized by caliber or blast radius.
/// </summary>
public static class Effects
{
    private static readonly RandomNumberGenerator Rng = new();

    // ---------- Materials ----------

    private static StandardMaterial3D _fire, _smoke, _spark;
    private static SphereMesh _sphere;

    private static StandardMaterial3D ParticleMat(Texture2D tex, bool additive, bool soft)
    {
        var m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            AlbedoTexture = tex,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (soft)
        {
            m.ProximityFadeEnabled = true;
            m.ProximityFadeDistance = 0.8f;
        }

        return m;
    }

    private static StandardMaterial3D Fire => _fire ??= ParticleMat(FxTextures.Puff, true, true);
    private static StandardMaterial3D Smoke => _smoke ??= ParticleMat(FxTextures.Puff, false, true);
    private static StandardMaterial3D Sparks => _spark ??= ParticleMat(FxTextures.Spark, true, false);
    private static SphereMesh Sphere => _sphere ??= new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 20, Rings = 10 };

    private static StandardMaterial3D SpriteMat(Texture2D tex, Color c, bool additive = true) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        AlbedoTexture = tex,
        AlbedoColor = c,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    private static StandardMaterial3D GlowLine(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        AlbedoColor = c,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // ---------- Building blocks ----------

    private sealed class Burst
    {
        public Material Mat;
        public float Size = 0.4f;
        public int Amount = 10;
        public float Life = 1f;
        public float LifeRandom = 0.3f;
        public Vector3 Dir = Vector3.Up;
        public float Spread = 45f;
        public float Flatness;
        public float VelMin;
        public float VelMax = 1f;
        public float DampMin;
        public float DampMax;
        public Vector3 Gravity = new(0, -9.8f, 0);
        public float ScaleFrom = 0.5f;
        public float ScaleTo = 1f;
        public Gradient Colors;
        public float EmitRadius;
    }

    private static Gradient Ramp(params (float at, Color c)[] stops)
    {
        var g = new Gradient();
        var offsets = new float[stops.Length];
        var colors = new Color[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            offsets[i] = stops[i].at;
            colors[i] = stops[i].c;
        }

        g.Offsets = offsets;
        g.Colors = colors;
        return g;
    }

    private static readonly Gradient FireRamp = Ramp(
        (0f, new Color(1f, 0.95f, 0.8f, 1f)), (0.3f, new Color(1f, 0.62f, 0.2f, 0.95f)),
        (0.7f, new Color(0.55f, 0.18f, 0.05f, 0.55f)), (1f, new Color(0.1f, 0.06f, 0.04f, 0f)));

    private static readonly Gradient SparkRamp = Ramp(
        (0f, new Color(1f, 0.97f, 0.85f, 1f)), (0.4f, new Color(1f, 0.7f, 0.25f, 1f)), (1f, new Color(1f, 0.3f, 0.05f, 0f)));

    private static readonly Gradient GunSmoke = Ramp(
        (0f, new Color(0.8f, 0.78f, 0.75f, 0f)), (0.08f, new Color(0.75f, 0.73f, 0.7f, 0.55f)),
        (0.6f, new Color(0.66f, 0.66f, 0.65f, 0.3f)), (1f, new Color(0.6f, 0.6f, 0.6f, 0f)));

    private static readonly Gradient BlastSmoke = Ramp(
        (0f, new Color(0.1f, 0.09f, 0.08f, 0f)), (0.1f, new Color(0.12f, 0.11f, 0.1f, 0.85f)),
        (0.6f, new Color(0.3f, 0.29f, 0.28f, 0.5f)), (1f, new Color(0.42f, 0.42f, 0.42f, 0f)));

    private static readonly Gradient DustRamp = Ramp(
        (0f, new Color(0.62f, 0.55f, 0.44f, 0f)), (0.1f, new Color(0.6f, 0.53f, 0.42f, 0.6f)), (1f, new Color(0.6f, 0.55f, 0.46f, 0f)));

    private static readonly Gradient ClodRamp = Ramp(
        (0f, new Color(0.24f, 0.19f, 0.13f, 1f)), (0.8f, new Color(0.26f, 0.21f, 0.15f, 0.9f)), (1f, new Color(0.3f, 0.25f, 0.18f, 0f)));

    private static void Emit(Node3D fx, Burst b)
    {
        var scale = new Curve();
        scale.AddPoint(new Vector2(0, Mathf.Clamp(b.ScaleFrom, 0, 1)));
        scale.AddPoint(new Vector2(1, Mathf.Clamp(b.ScaleTo, 0, 1)));
        var p = new CpuParticles3D
        {
            Amount = Math.Max(1, b.Amount),
            Lifetime = b.Life,
            LifetimeRandomness = b.LifeRandom,
            OneShot = true,
            Explosiveness = 1f,
            Direction = b.Dir.LengthSquared() > 0 ? b.Dir.Normalized() : Vector3.Up,
            Spread = b.Spread,
            Flatness = b.Flatness,
            InitialVelocityMin = b.VelMin,
            InitialVelocityMax = b.VelMax,
            DampingMin = b.DampMin,
            DampingMax = b.DampMax,
            Gravity = b.Gravity,
            AngleMin = 0f,
            AngleMax = 360f,
            ScaleAmountMin = b.Size * 0.7f,
            ScaleAmountMax = b.Size,
            ScaleAmountCurve = scale,
            ColorRamp = b.Colors,
            Mesh = new QuadMesh { Size = Vector2.One, Material = b.Mat },
            Emitting = true,
        };
        if (b.EmitRadius > 0)
        {
            p.EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere;
            p.EmissionSphereRadius = b.EmitRadius;
        }

        fx.AddChild(p);
    }

    private static FxNode Spawn(Node3D world, Vector3 pos, float life)
    {
        var fx = new FxNode { Life = life };
        world.AddChild(fx);
        fx.GlobalPosition = pos;
        return fx;
    }

    private static void Chain(FxNode fx, Action<float> step)
    {
        Action<float> prev = fx.Animate;
        fx.Animate = u =>
        {
            prev?.Invoke(u);
            step(u);
        };
    }

    /// <summary>A flash of light that dies away over <paramref name="seconds"/>.</summary>
    private static void Light(FxNode fx, Color c, float energy, float range, float seconds, bool flicker = false)
    {
        var light = new OmniLight3D { LightColor = c, LightEnergy = energy, OmniRange = range, OmniAttenuation = 1.6f };
        fx.AddChild(light);
        Chain(fx, u =>
        {
            float t = u * fx.Life / seconds;
            float k = Mathf.Max(0, 1f - t);
            if (flicker) k *= 0.8f + 0.2f * Rng.Randf();
            light.LightEnergy = energy * k * k;
            light.Visible = k > 0;
        });
    }

    /// <summary>A camera-facing glow that pops and fades.</summary>
    private static void Flash(FxNode fx, Color c, float size, float seconds, Vector3 offset = default)
    {
        StandardMaterial3D mat = SpriteMat(FxTextures.Flare, c);
        var mi = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = offset,
            Scale = Vector3.One * size * Rng.RandfRange(0.85f, 1.15f),
        };
        fx.AddChild(mi);
        Chain(fx, u =>
        {
            float t = u * fx.Life / seconds;
            float k = Mathf.Max(0, 1f - t);
            mat.AlbedoColor = new Color(c.R, c.G, c.B, k);
            mi.Visible = k > 0;
        });
    }

    private static Vector3 Reflect(Vector3 dir, Vector3 normal) => (dir - 2f * dir.Dot(normal) * normal).Normalized();

    private static bool GroundBelow(Node3D world, Vector3 from, float maxDist, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.Up;
        var q = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * 0.2f, from + Vector3.Down * maxDist, GridRegistry.Layers.World);
        Godot.Collections.Dictionary hit = world.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0) return false;
        point = hit["position"].AsVector3();
        normal = hit["normal"].AsVector3();
        return true;
    }

    // ---------- Gunfire ----------

    public static void MuzzleFlash(Node3D world, Vector3 pos, Vector3 dir, float caliber, bool brake)
    {
        if (world == null) return;
        float s = Mathf.Clamp(caliber / 76f, 0.5f, 1.8f);
        FxNode fx = Spawn(world, pos, 4f);
        Flash(fx, new Color(1f, 0.82f, 0.55f), 2.4f * s, 0.07f, dir * 0.5f * s);
        Emit(fx, new Burst
        {
            Mat = Fire, Size = 1.0f * s, Amount = 12, Life = 0.15f, Dir = dir, Spread = 9, VelMin = 18 * s, VelMax = 42 * s,
            DampMin = 140, DampMax = 180, Gravity = Vector3.Zero, ScaleFrom = 0.45f, ScaleTo = 1f, Colors = FireRamp,
        });
        if (brake)
        {
            Vector3 side = dir.Cross(Mathf.Abs(dir.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
            foreach (Vector3 d in new[] { side, -side })
                Emit(fx, new Burst
                {
                    Mat = Fire, Size = 0.7f * s, Amount = 6, Life = 0.12f, Dir = d + dir * 0.3f, Spread = 14, VelMin = 10 * s,
                    VelMax = 22 * s, DampMin = 120, DampMax = 150, Gravity = Vector3.Zero, Colors = FireRamp,
                });
        }

        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 1.8f * s, Amount = 16, Life = 3f, LifeRandom = 0.4f, Dir = dir, Spread = 28,
            VelMin = 4 * s, VelMax = 15 * s, DampMin = 4, DampMax = 7, Gravity = new Vector3(0, 0.5f, 0),
            ScaleFrom = 0.3f, ScaleTo = 1f, Colors = GunSmoke,
        });
        Light(fx, new Color(1f, 0.72f, 0.42f), 14f * s, 10f + 6f * s, 0.12f);

        // The blast wave kicks up a ring of dust when the gun is close to the ground.
        if (GroundBelow(world, pos, 4.5f, out Vector3 ground, out _))
        {
            FxNode dust = Spawn(world, ground + Vector3.Up * 0.2f, 3.5f);
            Emit(dust, new Burst
            {
                Mat = Smoke, Size = 1.5f * s, Amount = 26, Life = 2.4f, Dir = Vector3.Right, Spread = 180, Flatness = 1f,
                VelMin = 5 * s, VelMax = 14 * s, DampMin = 4, DampMax = 6, Gravity = new Vector3(0, 0.35f, 0),
                ScaleFrom = 0.35f, ScaleTo = 1f, Colors = DustRamp, EmitRadius = 0.6f,
            });
        }

        Sfx.At(world, pos, Sfx.CannonFor(caliber), 2f, Rng.RandfRange(0.94f, 1.06f), 26f, delayByDistance: false);
    }

    public static TracerFx Tracer(Node3D parent, float caliber, AmmoType type)
    {
        Color c = type == AmmoType.AP ? new Color(1f, 0.82f, 0.5f) : new Color(1f, 0.5f, 0.25f);
        float w = Mathf.Clamp(caliber / 1000f * 0.5f, 0.02f, 0.06f);
        var t = new TracerFx { Width = w, Tint = c };
        parent.AddChild(t);
        t.Head = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = SpriteMat(FxTextures.Flare, c),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Scale = Vector3.One * (0.5f + caliber / 160f), Visible = false,
        };
        t.Streak = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 6, Rings = 1 },
            MaterialOverride = BlockVisuals.Emissive(c, 6f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
        };
        parent.AddChild(t.Head);
        parent.AddChild(t.Streak);
        return t;
    }

    private static Transform3D Segment(Vector3 from, Vector3 to, float width)
    {
        Vector3 d = to - from;
        float len = Mathf.Max(d.Length(), 0.01f);
        Vector3 dir = d / len;
        Vector3 up = Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        // The cylinder's long axis is Y; turn it to lie along the segment, then stretch it.
        Basis basis = Basis.LookingAt(dir, up) * new Basis(Vector3.Right, Mathf.Pi / 2) * Basis.FromScale(new Vector3(width, len, width));
        return new Transform3D(basis, (from + to) * 0.5f);
    }

    public static void UpdateTracer(TracerFx t, Vector3 from, Vector3 to)
    {
        if (t == null || !GodotObject.IsInstanceValid(t)) return;
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 0.01f) return;
        Vector3 dir = d / len;
        float streak = Mathf.Min(len, 28f);
        t.Streak.GlobalTransform = Segment(to - dir * streak, to, t.Width);
        t.Streak.Visible = true;
        t.Head.GlobalPosition = to;
        t.Head.Visible = true;

        // A thin glowing trail that hangs in the air for a moment.
        Node parent = t.GetParent();
        if (parent is not Node3D world) return;
        StandardMaterial3D mat = GlowLine(new Color(t.Tint.R, t.Tint.G * 0.85f, t.Tint.B * 0.7f, 0.5f));
        var seg = new MeshInstance3D
        {
            Mesh = t.Streak.Mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var fx = new FxNode { Life = 0.18f };
        world.AddChild(fx);
        fx.AddChild(seg);
        seg.GlobalTransform = Segment(from, to, t.Width * 0.55f);
        fx.Animate = u => mat.AlbedoColor = new Color(mat.AlbedoColor.R, mat.AlbedoColor.G, mat.AlbedoColor.B, 0.5f * (1f - u));
    }

    public static void EndTracer(TracerFx t)
    {
        if (t == null || !GodotObject.IsInstanceValid(t)) return;
        if (GodotObject.IsInstanceValid(t.Head)) t.Head.QueueFree();
        if (GodotObject.IsInstanceValid(t.Streak)) t.Streak.QueueFree();
        t.QueueFree();
    }

    // ---------- Hits ----------

    /// <summary>
    /// A shell striking a block. <paramref name="surface"/> is the node the block hangs from, so the scorch
    /// mark moves with a turret.
    /// </summary>
    public static void Impact(Node3D world, Vector3 pos, Vector3 normal, Vector3 dir, HitKind kind, float caliber, Node3D surface)
    {
        float s = Mathf.Clamp(caliber / 76f, 0.5f, 1.8f);
        Vector3 bounce = Reflect(dir, normal);
        FxNode fx = Spawn(world, pos, 3f);
        Flash(fx, new Color(1f, 0.9f, 0.7f), 1.6f * s, 0.06f);
        Light(fx, new Color(1f, 0.75f, 0.45f), 8f * s, 7f, 0.1f);
        Node3D decalParent = surface != null && GodotObject.IsInstanceValid(surface) ? surface : world;

        switch (kind)
        {
            case HitKind.Penetration:
                Emit(fx, new Burst
                {
                    Mat = Sparks, Size = 0.1f, Amount = 44, Life = 0.6f, Dir = normal, Spread = 55, VelMin = 6, VelMax = 20,
                    DampMin = 1, DampMax = 3, Colors = SparkRamp, ScaleFrom = 1f, ScaleTo = 0.4f,
                });
                Emit(fx, new Burst
                {
                    Mat = Fire, Size = 0.16f, Amount = 14, Life = 1.3f, Dir = normal, Spread = 70, VelMin = 2, VelMax = 8,
                    Colors = FireRamp, ScaleFrom = 1f, ScaleTo = 0.5f,
                });
                Emit(fx, new Burst
                {
                    Mat = Smoke, Size = 1.0f * s, Amount = 7, Life = 2f, Dir = normal, Spread = 30, VelMin = 1, VelMax = 3,
                    DampMin = 1, DampMax = 2, Gravity = new Vector3(0, 0.5f, 0), Colors = BlastSmoke, ScaleFrom = 0.3f,
                });
                Decals.Place(decalParent, pos, normal, FxTextures.Hole, 0.3f * s, 0.3f);
                Sfx.At(world, pos, SoundId.Penetration, 0f, Rng.RandfRange(0.88f, 1.1f) / Mathf.Sqrt(s), 14f);
                break;

            case HitKind.Ricochet:
                Emit(fx, new Burst
                {
                    Mat = Sparks, Size = 0.09f, Amount = 34, Life = 0.5f, Dir = bounce, Spread = 18, VelMin = 10, VelMax = 28,
                    DampMin = 1, DampMax = 2, Colors = SparkRamp, ScaleFrom = 1f, ScaleTo = 0.4f,
                });
                Ricochet(world, pos, bounce, caliber);
                Decals.Place(decalParent, pos, normal, FxTextures.Scuff, 0.35f * s, 0.25f);
                Sfx.At(world, pos, SoundId.Ricochet, -1f, Rng.RandfRange(0.9f, 1.12f), 16f);
                break;

            default:
                Emit(fx, new Burst
                {
                    Mat = Sparks, Size = 0.1f, Amount = 60, Life = 0.55f, Dir = bounce, Spread = 38, VelMin = 8, VelMax = 24,
                    DampMin = 1, DampMax = 3, Colors = SparkRamp, ScaleFrom = 1f, ScaleTo = 0.4f,
                });
                Decals.Place(decalParent, pos, normal, FxTextures.Scuff, 0.42f * s, 0.25f);
                Sfx.At(world, pos, SoundId.Clang, 0f, Rng.RandfRange(0.9f, 1.08f) / Mathf.Sqrt(s), 15f);
                break;
        }
    }

    /// <summary>A glowing shell skipping away after a ricochet.</summary>
    private static void Ricochet(Node3D world, Vector3 pos, Vector3 dir, float caliber)
    {
        float w = Mathf.Clamp(caliber / 1000f * 0.45f, 0.02f, 0.05f);
        StandardMaterial3D mat = GlowLine(new Color(1f, 0.85f, 0.5f, 1f));
        var mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 6, Rings = 1 };
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var fx = new FxNode { Life = 0.35f };
        world.AddChild(fx);
        fx.AddChild(mi);
        Vector3 wobble = new Vector3(Rng.RandfRange(-1, 1), Rng.RandfRange(0, 1), Rng.RandfRange(-1, 1)) * 0.15f;
        Vector3 d = (dir + wobble).Normalized();
        fx.Animate = u =>
        {
            float dist = 160f * u * fx.Life;
            Vector3 head = pos + d * dist + Vector3.Down * 4.9f * (u * fx.Life) * (u * fx.Life);
            Vector3 tail = pos + d * Mathf.Max(0, dist - 14f);
            mi.GlobalTransform = Segment(tail, head, w);
            mat.AlbedoColor = new Color(1f, 0.85f, 0.5f, 1f - u);
        };
    }

    /// <summary>A shell slamming into the ground.</summary>
    public static void Dirt(Node3D world, Vector3 pos, Vector3 normal, float caliber)
    {
        float s = Mathf.Clamp(caliber / 76f, 0.5f, 1.8f);
        FxNode fx = Spawn(world, pos, 4.5f);
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 0.3f * s, Amount = 28, Life = 1.2f, Dir = normal, Spread = 26, VelMin = 7 * s, VelMax = 17 * s,
            Gravity = new Vector3(0, -16f, 0), Colors = ClodRamp, ScaleFrom = 0.8f, ScaleTo = 0.6f,
        });
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 2.4f * s, Amount = 12, Life = 3.4f, LifeRandom = 0.4f, Dir = normal, Spread = 60,
            VelMin = 1.5f, VelMax = 5f * s, DampMin = 2, DampMax = 3, Gravity = new Vector3(0, 0.25f, 0),
            Colors = DustRamp, ScaleFrom = 0.3f, ScaleTo = 1f,
        });
        Flash(fx, new Color(1f, 0.85f, 0.6f), 0.9f * s, 0.05f);
        Decals.Place(world, pos, normal, FxTextures.Scorch, 0.9f * s, 0.6f, new Color(1, 1, 1, 0.8f));
        Sfx.At(world, pos, SoundId.Dirt, -1f, Rng.RandfRange(0.85f, 1.1f) / Mathf.Sqrt(s), 14f);
    }

    public static void Explosion(Node3D world, Vector3 pos, float radius)
    {
        float r = Mathf.Clamp(radius, 0.6f, 5f);
        FxNode fx = Spawn(world, pos, 6f);
        Flash(fx, new Color(1f, 0.85f, 0.6f), r * 5f, 0.13f);

        StandardMaterial3D coreMat = SpriteMat(null, new Color(1f, 0.9f, 0.7f, 1f));
        coreMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;
        var core = new MeshInstance3D { Mesh = Sphere, MaterialOverride = coreMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        fx.AddChild(core);
        Chain(fx, u =>
        {
            float t = u * fx.Life;
            float grow = Mathf.Min(t / 0.07f, 1f);
            float fade = Mathf.Max(0, 1f - t / 0.28f);
            core.Scale = Vector3.One * Mathf.Max(r * 1.4f * grow, 0.01f);
            coreMat.AlbedoColor = new Color(1f, 0.75f + 0.2f * fade, 0.45f * fade + 0.2f, fade);
            core.Visible = fade > 0;
        });

        Emit(fx, new Burst
        {
            Mat = Fire, Size = r * 1.3f, Amount = 14 + (int)(r * 4), Life = 0.8f, LifeRandom = 0.4f, EmitRadius = r * 0.25f,
            Dir = Vector3.Up, Spread = 180, VelMin = r * 1.5f, VelMax = r * 5f, DampMin = r * 5f, DampMax = r * 8f,
            Gravity = new Vector3(0, 1.5f, 0), ScaleFrom = 0.45f, ScaleTo = 1f, Colors = FireRamp,
        });
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = r * 1.7f, Amount = 14 + (int)(r * 3), Life = 4.8f, LifeRandom = 0.35f, EmitRadius = r * 0.4f,
            Dir = Vector3.Up, Spread = 70, VelMin = r * 0.5f, VelMax = r * 1.8f, DampMin = 0.6f, DampMax = 1.2f,
            Gravity = new Vector3(0, 1.3f, 0), ScaleFrom = 0.35f, ScaleTo = 1f, Colors = BlastSmoke,
        });
        Emit(fx, new Burst
        {
            Mat = Sparks, Size = 0.13f, Amount = 45, Life = 1.6f, LifeRandom = 0.5f, Dir = Vector3.Up, Spread = 180,
            VelMin = r * 4f, VelMax = r * 11f, DampMin = 0.3f, DampMax = 0.8f, Colors = SparkRamp, ScaleFrom = 1f, ScaleTo = 0.3f,
        });
        Light(fx, new Color(1f, 0.62f, 0.32f), 24f, r * 8f, 0.6f, flicker: true);

        bool nearGround = GroundBelow(world, pos, r * 1.3f, out Vector3 ground, out Vector3 gn);
        Shockwave(world, nearGround ? ground + gn * 0.15f : pos, nearGround ? gn : Vector3.Up, r);
        if (nearGround)
        {
            Decals.Place(world, ground, gn, FxTextures.Scorch, r * 2.4f, 1.2f);
            FxNode dirt = Spawn(world, ground + gn * 0.2f, 5f);
            Emit(dirt, new Burst
            {
                Mat = Smoke, Size = 0.35f + r * 0.08f, Amount = 34, Life = 1.4f, Dir = gn, Spread = 38, VelMin = r * 3f,
                VelMax = r * 7.5f, Gravity = new Vector3(0, -15f, 0), Colors = ClodRamp, ScaleFrom = 0.8f, ScaleTo = 0.6f,
            });
            Emit(dirt, new Burst
            {
                Mat = Smoke, Size = r * 1.6f, Amount = 22, Life = 3.2f, Dir = Vector3.Right, Spread = 180, Flatness = 1f,
                VelMin = r * 2f, VelMax = r * 5.5f, DampMin = 2.5f, DampMax = 4f, Gravity = new Vector3(0, 0.3f, 0),
                Colors = DustRamp, ScaleFrom = 0.35f, ScaleTo = 1f, EmitRadius = r * 0.3f,
            });
        }

        Sfx.At(world, pos, Sfx.ExplosionFor(radius), 3f, Rng.RandfRange(0.9f, 1.08f), 40f);
    }

    private static void Shockwave(Node3D world, Vector3 pos, Vector3 normal, float r)
    {
        StandardMaterial3D mat = SpriteMat(FxTextures.Ring, new Color(1f, 0.95f, 0.85f, 0.5f), additive: false);
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled;
        var quad = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var fx = new FxNode { Life = 0.45f };
        world.AddChild(fx);
        fx.AddChild(quad);
        Vector3 t = Mathf.Abs(normal.Y) < 0.9f ? Vector3.Up.Cross(normal).Normalized() : Vector3.Right;
        var basis = new Basis(t, normal, t.Cross(normal));
        fx.Animate = u =>
        {
            float e = 1f - (1f - u) * (1f - u);
            float size = r * (0.6f + 4.6f * e);
            quad.GlobalTransform = new Transform3D(basis.Scaled(Vector3.One) * Basis.FromScale(new Vector3(size, 1, size)), pos);
            mat.AlbedoColor = new Color(1f, 0.95f, 0.85f, 0.5f * (1f - u));
        };
    }

    // ---------- Small arms ----------

    public static void RifleFlash(Node3D world, Vector3 pos, Vector3 dir)
    {
        FxNode fx = Spawn(world, pos, 1.2f);
        Flash(fx, new Color(1f, 0.8f, 0.5f), 0.55f, 0.04f, dir * 0.08f);
        Light(fx, new Color(1f, 0.75f, 0.45f), 2.5f, 4f, 0.05f);
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 0.25f, Amount = 3, Life = 0.9f, Dir = dir, Spread = 20, VelMin = 0.6f, VelMax = 1.6f,
            DampMin = 1, DampMax = 2, Gravity = new Vector3(0, 0.4f, 0), Colors = GunSmoke, ScaleFrom = 0.3f,
        });
    }

    /// <summary>A bullet striking metal (sparks, ping) or dirt (puff, thud).</summary>
    public static void BulletImpact(Node3D world, Vector3 pos, Vector3 normal, Vector3 dir, bool metal, Node3D surface)
    {
        FxNode fx = Spawn(world, pos, 1.5f);
        if (metal)
        {
            Emit(fx, new Burst
            {
                Mat = Sparks, Size = 0.06f, Amount = 10, Life = 0.35f, Dir = Reflect(dir, normal), Spread = 35,
                VelMin = 4, VelMax = 12, Colors = SparkRamp, ScaleFrom = 1f, ScaleTo = 0.3f,
            });
            Decals.Place(surface != null && GodotObject.IsInstanceValid(surface) ? surface : world, pos, normal,
                FxTextures.Scuff, 0.1f, 0.15f, null, 30f);
            Sfx.At(world, pos, SoundId.BulletMetal, -8f, Rng.RandfRange(0.85f, 1.2f), 8f);
        }
        else
        {
            Emit(fx, new Burst
            {
                Mat = Smoke, Size = 0.1f, Amount = 7, Life = 0.6f, Dir = normal, Spread = 30, VelMin = 2, VelMax = 5,
                Gravity = new Vector3(0, -12f, 0), Colors = ClodRamp, ScaleFrom = 0.8f, ScaleTo = 0.6f,
            });
            Emit(fx, new Burst
            {
                Mat = Smoke, Size = 0.5f, Amount = 3, Life = 1.2f, Dir = normal, Spread = 40, VelMin = 0.5f, VelMax = 1.5f,
                DampMin = 1, DampMax = 2, Gravity = new Vector3(0, 0.2f, 0), Colors = DustRamp, ScaleFrom = 0.3f,
            });
            Sfx.At(world, pos, SoundId.BulletDirt, -8f, Rng.RandfRange(0.85f, 1.15f), 7f);
        }
    }

    /// <summary>The blast out of the back of a rocket launcher.</summary>
    public static void Backblast(Node3D world, Vector3 pos, Vector3 forward)
    {
        FxNode fx = Spawn(world, pos, 3f);
        Emit(fx, new Burst
        {
            Mat = Fire, Size = 0.6f, Amount = 8, Life = 0.15f, Dir = -forward, Spread = 15, VelMin = 10, VelMax = 22,
            DampMin = 90, DampMax = 120, Gravity = Vector3.Zero, Colors = FireRamp,
        });
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 1.4f, Amount = 14, Life = 2.4f, Dir = -forward, Spread = 25, VelMin = 4, VelMax = 12,
            DampMin = 4, DampMax = 6, Gravity = new Vector3(0, 0.4f, 0), Colors = GunSmoke, ScaleFrom = 0.3f,
        });
        Light(fx, new Color(1f, 0.7f, 0.4f), 5f, 6f, 0.08f);
        Sfx.At(world, pos, SoundId.RocketLaunch, 0f, Rng.RandfRange(0.95f, 1.05f), 16f, delayByDistance: false);
    }

    /// <summary>A smoke trail that follows a rocket. Move it with <see cref="Follow"/>; stop it with <see cref="StopTrail"/>.</summary>
    public static CpuParticles3D RocketTrail(Node3D world, Vector3 pos)
    {
        var scale = new Curve();
        scale.AddPoint(new Vector2(0, 0.35f));
        scale.AddPoint(new Vector2(1, 1f));
        var p = new CpuParticles3D
        {
            Amount = 80, Lifetime = 1.6, Explosiveness = 0f, Direction = Vector3.Up, Spread = 180, InitialVelocityMin = 0.2f,
            InitialVelocityMax = 0.8f, Gravity = new Vector3(0, 0.35f, 0), ScaleAmountMin = 0.5f, ScaleAmountMax = 0.8f,
            ScaleAmountCurve = scale, ColorRamp = GunSmoke, AngleMin = 0, AngleMax = 360,
            Mesh = new QuadMesh { Size = Vector2.One, Material = Smoke }, Emitting = true, LocalCoords = false,
        };
        world.AddChild(p);
        p.GlobalPosition = pos;
        var flame = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = SpriteMat(FxTextures.Flare, new Color(1f, 0.7f, 0.35f)),
            Scale = Vector3.One * 0.9f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        p.AddChild(flame);
        var light = new OmniLight3D { LightColor = new Color(1f, 0.65f, 0.35f), LightEnergy = 2f, OmniRange = 5f };
        p.AddChild(light);
        return p;
    }

    public static void Follow(CpuParticles3D trail, Vector3 pos)
    {
        if (trail != null && GodotObject.IsInstanceValid(trail)) trail.GlobalPosition = pos;
    }

    public static void StopTrail(CpuParticles3D trail)
    {
        if (trail == null || !GodotObject.IsInstanceValid(trail)) return;
        trail.Emitting = false;
        foreach (Node c in trail.GetChildren()) c.QueueFree();
        trail.GetTree().CreateTimer(2.0).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(trail)) trail.QueueFree();
        };
    }

    /// <summary>A handful of welding sparks and a flicker of blue-white light. Called repeatedly while welding.</summary>
    public static void WeldSparks(Node3D world, Vector3 pos, Vector3 normal)
    {
        FxNode fx = Spawn(world, pos, 0.8f);
        Emit(fx, new Burst
        {
            Mat = Sparks, Size = 0.05f, Amount = 8, Life = 0.45f, Dir = normal, Spread = 70, VelMin = 1.5f, VelMax = 5f,
            Colors = SparkRamp, ScaleFrom = 1f, ScaleTo = 0.3f,
        });
        Flash(fx, new Color(0.7f, 0.85f, 1f), 0.35f, 0.05f);
        Light(fx, new Color(0.7f, 0.85f, 1f), 3f + Rng.Randf() * 2f, 4f, 0.08f, flicker: true);
    }

    // ---------- Water ----------

    private static readonly Gradient SprayRamp = Ramp(
        (0f, new Color(0.92f, 0.95f, 0.96f, 0.9f)), (0.6f, new Color(0.85f, 0.9f, 0.92f, 0.5f)), (1f, new Color(0.8f, 0.86f, 0.9f, 0f)));

    /// <summary>A column of spray where something hits the water. <paramref name="size"/> about 0.3 (bullet) to 3 (torpedo).</summary>
    public static void Splash(Node3D world, Vector3 pos, float size)
    {
        var at = new Vector3(pos.X, Ocean.Level + 0.05f, pos.Z);
        FxNode fx = Spawn(world, at, 4f);
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 0.8f * size, Amount = 14 + (int)(size * 8), Life = 1.6f, LifeRandom = 0.3f, Dir = Vector3.Up,
            Spread = 12 + 6 * size, VelMin = 4 * size + 2, VelMax = 9 * size + 4, Gravity = new Vector3(0, -9.8f, 0),
            Colors = SprayRamp, ScaleFrom = 0.4f, ScaleTo = 1f,
        });
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 1.2f * size, Amount = 18, Life = 1.8f, Dir = Vector3.Right, Spread = 180, Flatness = 1f,
            VelMin = 1.5f * size + 1, VelMax = 4 * size + 2, DampMin = 2, DampMax = 3, Gravity = new Vector3(0, 0.2f, 0),
            Colors = SprayRamp, ScaleFrom = 0.3f, ScaleTo = 1f, EmitRadius = 0.3f * size,
        });
        if (size >= 0.6f) Sfx.At(world, at, SoundId.Splash, -6f + size * 2f, Rng.RandfRange(0.85f, 1.1f) / Mathf.Sqrt(size), 14f + size * 6f);
        else Sfx.At(world, at, SoundId.BulletDirt, -12f, Rng.RandfRange(1.3f, 1.6f), 6f);
    }

    /// <summary>
    /// An explosion under water: a dull boom and a tall column of white water thrown up at the surface
    /// (smaller the deeper it was), with a ring of foam spreading out.
    /// </summary>
    public static void UnderwaterBlast(Node3D world, Vector3 pos, float radius)
    {
        float depth = Mathf.Max(Ocean.Level - pos.Y, 0f);
        float size = Mathf.Clamp(radius * 0.8f, 0.8f, 3.5f) * Mathf.Clamp(1.4f - depth / 12f, 0.35f, 1.2f);
        var at = new Vector3(pos.X, Ocean.Level + 0.05f, pos.Z);
        FxNode fx = Spawn(world, at, 6f);
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 1.4f * size, Amount = 40, Life = 2.6f, LifeRandom = 0.3f, Dir = Vector3.Up, Spread = 9,
            VelMin = 9 * size, VelMax = 16 * size, Gravity = new Vector3(0, -9.8f, 0), Colors = SprayRamp, ScaleFrom = 0.5f,
            ScaleTo = 1.4f, EmitRadius = 0.6f * size,
        });
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 2.2f * size, Amount = 26, Life = 3.4f, Dir = Vector3.Right, Spread = 180, Flatness = 1f,
            VelMin = 3 * size, VelMax = 7 * size, DampMin = 1.5f, DampMax = 2.5f, Gravity = new Vector3(0, 0.1f, 0),
            Colors = SprayRamp, ScaleFrom = 0.4f, ScaleTo = 1.3f, EmitRadius = 0.8f * size,
        });
        Sfx.At(world, at, Sfx.ExplosionFor(radius), 1f, Rng.RandfRange(0.55f, 0.65f), 40f);
        Sfx.At(world, at, SoundId.Splash, 2f, Rng.RandfRange(0.6f, 0.7f), 30f);
    }

    private static readonly Gradient FlakSmoke = Ramp(
        (0f, new Color(0.12f, 0.11f, 0.1f, 0.95f)), (0.5f, new Color(0.16f, 0.15f, 0.14f, 0.75f)), (1f, new Color(0.2f, 0.2f, 0.2f, 0f)));

    /// <summary>A flak shell going off in the air: a sharp flash and a hanging black puff.</summary>
    public static void FlakBurst(Node3D world, Vector3 pos, float radius)
    {
        FxNode fx = Spawn(world, pos, 7f);
        Flash(fx, new Color(1f, 0.8f, 0.5f), radius * 3f, 0.08f);
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = radius * 0.9f, Amount = 14, Life = 6f, LifeRandom = 0.3f, Dir = Vector3.Up, Spread = 180,
            VelMin = 1f, VelMax = 3.5f, DampMin = 2.5f, DampMax = 3.5f, Gravity = new Vector3(0, 0.15f, 0),
            Colors = FlakSmoke, ScaleFrom = 0.4f, ScaleTo = 1.3f, EmitRadius = radius * 0.2f,
        });
        Sfx.At(world, pos, SoundId.ExplosionSmall, 0f, Rng.RandfRange(1.15f, 1.35f), 30f);
    }

    /// <summary>A puff of foam at the surface above a running torpedo.</summary>
    public static void Wake(Node3D world, Vector3 pos)
    {
        FxNode fx = Spawn(world, new Vector3(pos.X, Ocean.Level + 0.1f, pos.Z), 3f);
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 1.1f, Amount = 3, Life = 2.6f, Dir = Vector3.Up, Spread = 60, VelMin = 0.2f, VelMax = 0.6f,
            DampMin = 1, DampMax = 2, Gravity = Vector3.Zero, Colors = SprayRamp, ScaleFrom = 0.4f, ScaleTo = 1f,
        });
    }

    /// <summary>Chunks flying off a destroyed block.</summary>
    public static void Shatter(Node3D world, Vector3 pos, BlockDef def, Vector3 velocity, Vector3 away)
    {
        Fragments.Spawn(world, pos, def, velocity, away, def.IsArmorLike ? 4 : 3);
        FxNode fx = Spawn(world, pos, 2.5f);
        Emit(fx, new Burst
        {
            Mat = Smoke, Size = 0.8f, Amount = 5, Life = 1.8f, Dir = Vector3.Up, Spread = 90, VelMin = 0.5f, VelMax = 2f,
            DampMin = 1, DampMax = 2, Gravity = new Vector3(0, 0.4f, 0), Colors = BlastSmoke, ScaleFrom = 0.3f,
        });
    }
}

/// <summary>Scorch marks and holes. Capped in number; the oldest disappear first.</summary>
public static class Decals
{
    private const int Max = 160;
    private static readonly Queue<Decal> Live = new();
    private static readonly RandomNumberGenerator Rng = new();

    public static void Place(Node3D parent, Vector3 pos, Vector3 normal, Texture2D tex, float size, float depth, Color? tint = null,
        float lifeSeconds = 60f)
    {
        if (parent == null || !GodotObject.IsInstanceValid(parent) || !parent.IsInsideTree()) return;
        Vector3 n = normal.LengthSquared() > 0.001f ? normal.Normalized() : Vector3.Up;
        var d = new Decal
        {
            TextureAlbedo = tex, Size = new Vector3(size, depth, size), Modulate = tint ?? Colors.White,
            UpperFade = 0.3f, LowerFade = 0.3f, NormalFade = 0.4f,
            DistanceFadeEnabled = true, DistanceFadeBegin = 180f, DistanceFadeLength = 40f,
        };
        parent.AddChild(d);
        Vector3 t = Mathf.Abs(n.Y) < 0.9f ? Vector3.Up.Cross(n).Normalized() : Vector3.Right;
        var basis = new Basis(n, Rng.RandfRange(0, Mathf.Tau)) * new Basis(t, n, t.Cross(n));
        d.GlobalTransform = new Transform3D(basis, pos);

        Live.Enqueue(d);
        while (Live.Count > Max)
        {
            Decal old = Live.Dequeue();
            if (GodotObject.IsInstanceValid(old)) old.QueueFree();
        }

        d.GetTree().CreateTimer(lifeSeconds).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(d)) d.QueueFree();
        };
    }
}
