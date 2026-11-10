using System;
using System.Collections.Generic;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Draws every shell tracer in the world in two batches: one for the glowing streaks and the fading
/// trails they leave, one for the bright heads. Before, each shell had its own nodes and every shell
/// left a new trail node behind it every physics step, which with a broadside in the air meant
/// thousands of nodes made and freed each second. Now it's two draw calls, refilled each step.
/// <para>Far away, tracers are drawn a little thicker than they really are so they stay visible as a line
/// of light instead of flickering out.</para>
/// </summary>
public partial class TracerBatch : Node3D
{
    private const float TrailLife = 0.18f;
    private const float StreakLength = 28f;
    private const int Stride = 16;

    /// <summary>Most trail pieces kept at once; beyond this new shells don't leave trails.</summary>
    private const int MaxTrails = 12000;

    /// <summary>Trails are only left within this distance of the camera.</summary>
    private const float TrailRange = 700f;

    private struct Trail
    {
        public Transform3D Xf;
        public Color Color;
        public float Born;
    }

    private sealed class Batch
    {
        public MultiMesh Mesh;
        public float[] Buffer = Array.Empty<float>();
        public int Count;

        public void Put(in Transform3D t, Color c)
        {
            if ((Count + 1) * Stride > Buffer.Length) Grow(Count + 1);
            int o = Count++ * Stride;
            Basis b = t.Basis;
            float[] f = Buffer;
            f[o] = b.X.X;
            f[o + 1] = b.Y.X;
            f[o + 2] = b.Z.X;
            f[o + 3] = t.Origin.X;
            f[o + 4] = b.X.Y;
            f[o + 5] = b.Y.Y;
            f[o + 6] = b.Z.Y;
            f[o + 7] = t.Origin.Y;
            f[o + 8] = b.X.Z;
            f[o + 9] = b.Y.Z;
            f[o + 10] = b.Z.Z;
            f[o + 11] = t.Origin.Z;
            f[o + 12] = c.R;
            f[o + 13] = c.G;
            f[o + 14] = c.B;
            f[o + 15] = c.A;
        }

        private void Grow(int need)
        {
            int cap = Math.Max(256, Buffer.Length / Stride);
            while (cap < need) cap *= 2;
            Array.Resize(ref Buffer, cap * Stride);
        }

        public void Upload()
        {
            int cap = Buffer.Length / Stride;
            if (Mesh.InstanceCount != cap) Mesh.InstanceCount = cap;
            if (cap > 0) Mesh.Buffer = Buffer;
            Mesh.VisibleInstanceCount = Count;
        }
    }

    private readonly Batch _lines = new(), _heads = new();
    private readonly List<Trail> _trails = new();
    private float _time;
    private Vector3 _viewer;

    public int Drawn => _lines.Count + _heads.Count;

    public override void _Ready()
    {
        var everywhere = new Aabb(new Vector3(-1e5f, -1e5f, -1e5f), new Vector3(2e5f, 2e5f, 2e5f));
        _lines.Mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 6, Rings = 1 },
            CustomAabb = everywhere,
        };
        _heads.Mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One }, CustomAabb = everywhere,
        };
        AddChild(new MultiMeshInstance3D
        {
            Name = "TracerLines", Multimesh = _lines.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(3f, 3f, 3f),
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        });
        AddChild(new MultiMeshInstance3D
        {
            Name = "TracerHeads", Multimesh = _heads.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                VertexColorUseAsAlbedo = true,
                AlbedoTexture = FxTextures.Flare,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        });
    }

    /// <summary>Starts a new physics step's worth of tracers.</summary>
    public void Begin(float dt)
    {
        _time += dt;
        _lines.Count = 0;
        _heads.Count = 0;
        _viewer = WorldQuery.Viewer;
    }

    /// <summary>A shell that moved from <paramref name="from"/> to <paramref name="to"/> this step.</summary>
    public void Shell(Vector3 from, Vector3 to, float width, Color tint, float headSize)
    {
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 0.01f) return;
        Vector3 dir = d / len;
        float dist = _viewer.DistanceTo(to);
        float w = Mathf.Max(width, dist * 0.0011f);
        float streak = Mathf.Min(len, StreakLength);
        _lines.Put(Segment(to - dir * streak, to, w), new Color(tint.R, tint.G, tint.B, 1f));
        float head = Mathf.Max(headSize, dist * 0.0035f);
        _heads.Put(new Transform3D(Basis.FromScale(Vector3.One * head), to), tint);
        if (dist < TrailRange && _trails.Count < MaxTrails)
            _trails.Add(new Trail
            {
                Xf = Segment(from, to, w * 0.55f), Born = _time,
                Color = new Color(tint.R / 3f, tint.G * 0.85f / 3f, tint.B * 0.7f / 3f, 0.5f),
            });
    }

    /// <summary>Adds the fading trails and sends everything to the GPU.</summary>
    public void End()
    {
        int keep = 0;
        for (int i = 0; i < _trails.Count; i++)
        {
            Trail t = _trails[i];
            float age = (_time - t.Born) / TrailLife;
            if (age >= 1f) continue;
            _trails[keep++] = t;
            _lines.Put(t.Xf, new Color(t.Color.R, t.Color.G, t.Color.B, t.Color.A * (1f - age)));
        }

        _trails.RemoveRange(keep, _trails.Count - keep);
        _lines.Upload();
        _heads.Upload();
    }

    public void Clear()
    {
        _trails.Clear();
        _lines.Count = _heads.Count = 0;
        if (_lines.Mesh != null) _lines.Upload();
        if (_heads.Mesh != null) _heads.Upload();
    }

    /// <summary>A unit cylinder (long axis Y) stretched to lie along a segment.</summary>
    public static Transform3D Segment(Vector3 from, Vector3 to, float width)
    {
        Vector3 d = to - from;
        float len = Mathf.Max(d.Length(), 0.01f);
        Vector3 y = d / len;
        Vector3 helper = Mathf.Abs(y.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 x = helper.Cross(y).Normalized();
        Vector3 z = x.Cross(y);
        return new Transform3D(new Basis(x * width, y * len, z * width), (from + to) * 0.5f);
    }
}
