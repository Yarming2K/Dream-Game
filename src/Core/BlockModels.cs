using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>Simple shapes a block's look is made from.</summary>
public enum Prim
{
    /// <summary>A box with chamfered edges. <see cref="ModelPart.Size"/> is its size, <see cref="ModelPart.Detail"/> the chamfer.</summary>
    Bevel,
    Box,

    /// <summary>A cylinder or cone along Y: Size = (bottom radius, height, top radius), Detail = sides.</summary>
    Cylinder,

    /// <summary>A ring round Y: Size = (ring radius, tube radius, 0).</summary>
    Torus,

    /// <summary>Size.X is the radius.</summary>
    Sphere,

    /// <summary>The armor slope: half a cell, solid where local y ≤ z.</summary>
    Wedge,

    /// <summary>
    /// A flat-sided slab along Y whose far (+Y) end is narrowed: Size is its size at the near end, Detail the
    /// taper across X and Detail2 the taper through Z. Blades, fins and flaps.
    /// </summary>
    Slab,

    /// <summary>One of the block shapes from <see cref="Shapes"/> filling the cell: Detail is the <see cref="BlockShape"/> (as a number).</summary>
    Shape,
}

/// <summary>What a part is painted with: the block's own color, or one of a few shared finishes.</summary>
public enum Paint
{
    Self,

    /// <summary>The block's color, darker: for detail in the same hue.</summary>
    SelfDark,
    Dark,
    Steel,
    Brass,
    Red,
    Rubber,
    Glass,

    /// <summary>Lit from inside, never shaded: lamps, sights, exhaust glow.</summary>
    Glow,
}

/// <summary>One shape in a block's look.</summary>
public struct ModelPart
{
    public Prim Prim;
    public Vector3 Size;
    public float Detail, Detail2;

    /// <summary>Placement relative to its node (row-vector convention: v * Transform).</summary>
    public Matrix4x4 Transform;
    public Paint Paint;
    public Vector3 GlowColor;
    public float GlowEnergy;

    /// <summary>Index into <see cref="BlockModel.Nodes"/>, or -1 for the block itself.</summary>
    public int Node;

    /// <summary>A key that is the same for every part with the same mesh.</summary>
    public readonly string MeshKey => Prim switch
    {
        Prim.Bevel => $"bevel:{Size.X:0.###}:{Size.Y:0.###}:{Size.Z:0.###}:{Detail:0.###}",
        Prim.Box => $"box:{Size.X:0.###}:{Size.Y:0.###}:{Size.Z:0.###}",
        Prim.Cylinder => $"cyl:{Size.X:0.####}:{Size.Y:0.###}:{Size.Z:0.####}:{(int)Detail}",
        Prim.Torus => $"torus:{Size.X:0.###}:{Size.Y:0.####}",
        Prim.Sphere => $"sphere:{Size.X:0.###}",
        Prim.Slab => $"slab:{Size.X:0.###}:{Size.Y:0.###}:{Size.Z:0.###}:{Detail:0.##}:{Detail2:0.##}",
        Prim.Shape => $"shape:{(int)Detail}",
        _ => "wedge",
    };
}

/// <summary>
/// The look of a block: a list of simple shapes, some of them on named moving parts ("Spinner" for
/// propellers, rotors and fans, "Flap" for control surfaces, "Payload" for a bomb). Described here, away
/// from the engine, so the game and the preview pictures build exactly the same models.
/// </summary>
public sealed class BlockModel
{
    public readonly List<(string Name, Vector3 Position)> Nodes = new();
    public readonly List<ModelPart> Parts = new();

    /// <summary>The block's placement of a part, including its node's offset.</summary>
    public Matrix4x4 PlacementOf(in ModelPart p) =>
        p.Node < 0 ? p.Transform : p.Transform * Matrix4x4.CreateTranslation(Nodes[p.Node].Position);
}

/// <summary>Builds block looks. Sizes are in meters; a cell is 0.5 m, centered on the block's origin.</summary>
public static class BlockModels
{
    private const float Full = Blueprint.CellSize, H = Full * 0.5f;
    private static readonly Dictionary<string, BlockModel> Cache = new();

    public static float BarrelRadius(float caliberMm) => Math.Clamp(caliberMm / 1000f * 0.85f, 0.03f, 0.4f);

    /// <summary>
    /// Blocks whose faces merge with their neighbors into one continuous hull surface: armor, glazing, frames
    /// and gas cells, in any of their one-cell shapes.
    /// </summary>
    public static bool IsSurface(BlockDef def) =>
        !def.IsMultiCell && def.Kind is BlockKind.Armor or BlockKind.Frame or BlockKind.GasCell;

    /// <summary>The block whose surface material a shaped block uses (its plain version), so a slope and the plates beside it are one surface.</summary>
    public static BlockDef SurfaceDefOf(BlockDef def) => def.BaseId != null && def.BaseId != def.Id && BlockCatalog.TryGet(def.BaseId, out BlockDef b) ? b : def;

    /// <summary>How wide the chamfer is on a surface block's outer edges.</summary>
    public static float SurfaceBevel(BlockDef def) => def.Kind == BlockKind.GasCell ? 0.11f : 0.022f;

    public static BlockModel For(BlockDef def, float caliberMm = 76f, bool hints = false)
    {
        string key = $"{def.Id}:{MathF.Round(caliberMm)}:{hints}";
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out BlockModel m)) return m;
            m = Build(def, caliberMm, hints);
            Cache[key] = m;
            return m;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static readonly Quaternion AlongX = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
    private static readonly Quaternion AlongZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>Turns a cone's tip (its top) to point forward (−Z).</summary>
    private static readonly Quaternion NoseZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);

    /// <summary>Turns a cone's tip to point backward (+Z).</summary>
    private static readonly Quaternion TailZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    private static Quaternion Rot(Vector3 axis, float angle) => Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);

    private static Matrix4x4 At(Vector3 pos, Quaternion? rot = null, Vector3? scale = null) =>
        Matrix4x4.CreateScale(scale ?? Vector3.One) * Matrix4x4.CreateFromQuaternion(rot ?? Quaternion.Identity) * Matrix4x4.CreateTranslation(pos);

    private sealed class Maker
    {
        public readonly BlockModel M = new();
        public int Node = -1;
        public Vector3 Offset;

        public int AddNode(string name, Vector3 pos)
        {
            M.Nodes.Add((name, pos + Offset));
            return M.Nodes.Count - 1;
        }

        private void Add(Prim prim, Vector3 size, float detail, Paint paint, Matrix4x4 xf, Vector3 glow = default, float energy = 0, float detail2 = 0)
        {
            if (Node < 0) xf *= Matrix4x4.CreateTranslation(Offset);
            M.Parts.Add(new ModelPart
            {
                Prim = prim, Size = size, Detail = detail, Detail2 = detail2, Paint = paint, Transform = xf, Node = Node, GlowColor = glow, GlowEnergy = energy,
            });
        }

        public void Bevel(Vector3 size, Paint p, Vector3 pos, float bevel = 0.022f, Quaternion? rot = null) =>
            Add(Prim.Bevel, size, bevel, p, At(pos, rot));

        public void Box(Vector3 size, Paint p, Vector3 pos, Quaternion? rot = null) => Add(Prim.Box, size, 0, p, At(pos, rot));

        public void Cyl(float r, float h, Paint p, Vector3 pos, Quaternion? rot = null, float top = -1, int sides = 20) =>
            Add(Prim.Cylinder, new Vector3(r, h, top < 0 ? r : top), sides, p, At(pos, rot));

        /// <summary>A tapered slab along Y: its far end is narrowed by <paramref name="taperX"/> across and <paramref name="taperZ"/> through.</summary>
        public void Slab(Vector3 size, float taperX, float taperZ, Paint p, Vector3 pos, Quaternion? rot = null) =>
            Add(Prim.Slab, size, taperX, p, At(pos, rot), detail2: taperZ);

        public void Torus(float ring, float tube, Paint p, Vector3 pos, Quaternion? rot = null) =>
            Add(Prim.Torus, new Vector3(ring, tube, 0), 0, p, At(pos, rot));

        public void Sphere(float r, Paint p, Vector3 pos, Vector3? scale = null) => Add(Prim.Sphere, new Vector3(r, 0, 0), 0, p, At(pos, null, scale));

        public void Wedge(Paint p) => Add(Prim.Wedge, Vector3.One, 0, p, Matrix4x4.Identity);

        public void Shape(BlockShape s, Paint p) => Add(Prim.Shape, Vector3.One, (int)s, p, Matrix4x4.Identity);

        public void Glow(Vector3 size, Vector3 color, float energy, Vector3 pos) =>
            Add(Prim.Box, size, 0, Paint.Glow, At(pos), color, energy);

        public void GlowCyl(float r, float h, Vector3 color, float energy, Vector3 pos, Quaternion? rot = null, float top = -1) =>
            Add(Prim.Cylinder, new Vector3(r, h, top < 0 ? r : top), 16, Paint.Glow, At(pos, rot), color, energy);

        public void GlowTorus(float ring, float tube, Vector3 color, float energy, Vector3 pos, Quaternion? rot = null) =>
            Add(Prim.Torus, new Vector3(ring, tube, 0), 0, Paint.Glow, At(pos, rot), color, energy);

        /// <summary>A ring of small cylinders (bolts) round Y.</summary>
        public void Bolts(int n, float radius, float y, float r, float h, Paint p)
        {
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.Tau / n;
                Cyl(r, h, p, new Vector3(MathF.Sin(a) * radius, y, MathF.Cos(a) * radius), sides: 6);
            }
        }
    }

    private static readonly Vector3 Cyan = new(0.4f, 0.85f, 1f), Amber = new(1f, 0.85f, 0.2f), RedLamp = new(1f, 0.35f, 0.25f), Green = new(0.35f, 1f, 0.5f);

    // ---------------------------------------------------------------- models

    private static BlockModel Build(BlockDef def, float caliberMm, bool hints)
    {
        var k = new Maker();
        if (def.IsMultiCell)
        {
            BuildBig(k, def, hints);
            return k.M;
        }

        float br = BarrelRadius(caliberMm);
        switch (def.Kind)
        {
            case BlockKind.Armor when def.Shape != BlockShape.Cube:
                k.Shape(def.Shape, def.Glass ? Paint.Glass : Paint.Self);
                break;
            case BlockKind.Armor when def.Glass:
                k.Shape(BlockShape.Cube, Paint.Glass);
                break;
            case BlockKind.Armor:
            case BlockKind.Frame:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                break;
            case BlockKind.GasCell:
                k.Bevel(new Vector3(Full * 0.99f), Paint.Self, Vector3.Zero, 0.09f);
                break;

            case BlockKind.Engine:
                // Crankcase, a darker head with cooling fins, and two exhaust stubs.
                k.Bevel(new Vector3(Full, 0.36f, Full), Paint.Self, new Vector3(0, -0.07f, 0));
                k.Bevel(new Vector3(0.38f, 0.1f, 0.44f), Paint.SelfDark, new Vector3(0, 0.15f, 0), 0.015f);
                for (int i = 0; i < 6; i++) k.Box(new Vector3(0.42f, 0.035f, 0.02f), Paint.Dark, new Vector3(0, 0.215f, -0.175f + i * 0.07f));
                foreach (float x in new[] { -0.205f, 0.205f })
                {
                    k.Cyl(0.034f, 0.16f, Paint.Dark, new Vector3(x, 0.2f, 0.16f));
                    k.Cyl(0.042f, 0.025f, Paint.Steel, new Vector3(x, 0.28f, 0.16f));
                }

                k.Cyl(0.05f, 0.06f, Paint.Steel, new Vector3(0, 0.03f, -0.255f), AlongZ);
                break;

            case BlockKind.FuelTank:
                k.Cyl(0.22f, Full * 0.96f, Paint.Self, Vector3.Zero, AlongZ, sides: 24);
                foreach (float z in new[] { -0.13f, 0.13f }) k.Cyl(0.228f, 0.035f, Paint.Dark, new Vector3(0, 0, z), AlongZ, sides: 24);
                k.Box(new Vector3(0.46f, 0.05f, 0.46f), Paint.Dark, new Vector3(0, -0.225f, 0));
                k.Cyl(0.045f, 0.05f, Paint.Steel, new Vector3(0.08f, 0.225f, -0.04f), sides: 10);
                break;

            case BlockKind.Wheel:
            case BlockKind.SteerWheel:
            {
                // Axle along X. A treaded tire, a rim, and a bolted hub.
                float r = def.WheelRadiusM;
                k.Cyl(r * 0.92f, 0.28f, Paint.Rubber, Vector3.Zero, AlongX, sides: 28);
                for (int i = 0; i < 14; i++)
                {
                    float a = i * MathF.Tau / 14;
                    var at = new Vector3(0, MathF.Cos(a) * r * 0.95f, MathF.Sin(a) * r * 0.95f);
                    k.Box(new Vector3(0.29f, r * 0.12f, r * 0.24f), Paint.Rubber, at, Rot(Vector3.UnitX, a));
                }

                Paint rim = def.Kind == BlockKind.SteerWheel ? Paint.Brass : Paint.Self;
                k.Cyl(r * 0.6f, 0.3f, rim, Vector3.Zero, AlongX, sides: 24);
                k.Cyl(r * 0.26f, 0.34f, Paint.Dark, Vector3.Zero, AlongX, sides: 12);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.Tau / 6;
                    k.Cyl(r * 0.05f, 0.33f, Paint.Steel, new Vector3(0, MathF.Cos(a) * r * 0.4f, MathF.Sin(a) * r * 0.4f), AlongX, sides: 6);
                }

                break;
            }

            case BlockKind.Track:
                TrackRun(k, def.WheelRadiusM, 0.44f, Full);
                break;

            case BlockKind.DriverSeat when def.Id == "pilot_seat":
                PilotSeat(k);
                break;

            case BlockKind.Detail:
                Detail(k, def);
                break;

            case BlockKind.CommandConsole:
            {
                // A desk of screens and a plotting table, with a chair.
                k.Bevel(new Vector3(Full, 0.06f, Full), Paint.SelfDark, new Vector3(0, -0.22f, 0), 0.015f);
                k.Bevel(new Vector3(0.44f, 0.16f, 0.18f), Paint.Self, new Vector3(0, -0.11f, -0.14f), 0.012f);
                Quaternion tilt = Rot(Vector3.UnitX, 0.3f);
                k.Bevel(new Vector3(0.44f, 0.18f, 0.04f), Paint.Dark, new Vector3(0, 0.06f, -0.2f), 0.01f, tilt);
                foreach (float x in new[] { -0.13f, 0.13f }) k.Glow(new Vector3(0.17f, 0.12f, 0.006f), x < 0 ? Green : Cyan, 1.1f, new Vector3(x, 0.07f, -0.23f));
                k.Glow(new Vector3(0.3f, 0.005f, 0.12f), new Vector3(0.3f, 0.6f, 0.9f), 0.8f, new Vector3(0, -0.025f, -0.12f));
                k.Bevel(new Vector3(0.16f, 0.04f, 0.16f), Paint.Dark, new Vector3(0, -0.12f, 0.12f), 0.01f);
                k.Bevel(new Vector3(0.16f, 0.18f, 0.035f), Paint.Dark, new Vector3(0, -0.02f, 0.2f), 0.01f);
                k.Cyl(0.015f, 0.08f, Paint.Steel, new Vector3(0, -0.18f, 0.12f), sides: 6);
                k.Cyl(0.006f, 0.18f, Paint.Dark, new Vector3(0.2f, 0.08f, -0.2f), sides: 6);
                break;
            }

            case BlockKind.Radar:
            {
                // A mast with a turning bedstead antenna.
                k.Cyl(0.06f, 0.12f, Paint.SelfDark, new Vector3(0, -0.19f, 0), sides: 12);
                k.Cyl(0.025f, 0.32f, Paint.Steel, new Vector3(0, 0.0f, 0), sides: 8);
                k.Node = k.AddNode("Spinner", new Vector3(0, 0.17f, 0));
                k.Box(new Vector3(0.46f, 0.12f, 0.02f), Paint.Self, new Vector3(0, 0.03f, 0));
                for (int i = 0; i < 5; i++) k.Box(new Vector3(0.012f, 0.14f, 0.03f), Paint.Steel, new Vector3(-0.2f + i * 0.1f, 0.03f, 0.02f));
                k.Box(new Vector3(0.05f, 0.05f, 0.06f), Paint.Dark, new Vector3(0, -0.03f, 0.03f));
                k.Node = -1;
                break;
            }

            case BlockKind.DriverSeat:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                k.Box(new Vector3(0.32f, 0.08f, 0.05f), Paint.Glass, new Vector3(0, 0.15f, -0.25f));
                k.Glow(new Vector3(0.28f, 0.03f, 0.02f), Cyan, 1.5f, new Vector3(0, 0.15f, -0.272f));
                foreach (float x in new[] { -0.1f, 0.1f }) k.Box(new Vector3(0.08f, 0.06f, 0.08f), Paint.Dark, new Vector3(x, 0.27f, -0.16f));
                k.Cyl(0.12f, 0.03f, Paint.SelfDark, new Vector3(0, 0.26f, 0.07f), sides: 20);
                k.Box(new Vector3(0.08f, 0.03f, 0.03f), Paint.Steel, new Vector3(0, 0.27f, 0.2f));
                break;

            case BlockKind.GunnerSeat:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                k.Box(new Vector3(0.13f, 0.11f, 0.08f), Paint.Dark, new Vector3(0.09f, 0.17f, -0.25f));
                k.Glow(new Vector3(0.06f, 0.045f, 0.02f), RedLamp, 2f, new Vector3(0.09f, 0.17f, -0.292f));
                k.Cyl(0.11f, 0.03f, Paint.SelfDark, new Vector3(-0.06f, 0.26f, 0.06f), sides: 20);
                break;

            case BlockKind.CommanderSeat:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                k.Cyl(0.15f, 0.12f, Paint.SelfDark, new Vector3(0, 0.3f, 0), sides: 20);
                k.Cyl(0.155f, 0.025f, Paint.Glass, new Vector3(0, 0.33f, 0), sides: 20);
                k.Cyl(0.12f, 0.03f, Paint.SelfDark, new Vector3(0, 0.375f, 0), sides: 20);
                k.Glow(new Vector3(0.12f, 0.035f, 0.02f), Cyan, 1.5f, new Vector3(0, 0.33f, -0.155f));
                break;

            case BlockKind.TurretRing:
                k.Cyl(0.25f, Full, Paint.Self, Vector3.Zero, sides: 28);
                k.Torus(0.215f, 0.022f, Paint.Steel, new Vector3(0, 0.235f, 0));
                k.Cyl(0.19f, 0.03f, Paint.Dark, new Vector3(0, 0.25f, 0), sides: 24);
                k.Bolts(10, 0.235f, 0.12f, 0.016f, 0.05f, Paint.Steel);
                break;

            case BlockKind.Breech when def.Breech.Rail:
                // A boxy magnet housing with its coils lit, the two rails leaving the front, and the power feed behind.
                k.Bevel(new Vector3(Full, 0.42f, Full), Paint.Self, new Vector3(0, -0.04f, 0));
                foreach (float x in new[] { -0.1f, 0.1f }) k.Box(new Vector3(0.06f, 0.15f, 0.1f), Paint.Steel, new Vector3(x, 0.02f, -0.25f));
                for (int i = 0; i < 3; i++) k.Glow(new Vector3(0.4f, 0.022f, 0.035f), Cyan, 1.4f, new Vector3(0, 0.18f, -0.14f + i * 0.12f));
                k.Bevel(new Vector3(0.32f, 0.22f, 0.07f), Paint.Dark, new Vector3(0, -0.03f, 0.255f), 0.01f);
                foreach (float x in new[] { -0.08f, 0.08f }) k.Cyl(0.025f, 0.1f, Paint.Dark, new Vector3(x, -0.2f, 0.2f), sides: 8);
                break;

            case BlockKind.Breech when def.Breech.Beam:
                // A finned emitter barrel on a base, with a red aperture at the front.
                k.Bevel(new Vector3(Full, 0.24f, Full), Paint.Self, new Vector3(0, -0.12f, 0));
                k.Cyl(0.15f, 0.44f, Paint.SelfDark, new Vector3(0, 0.08f, 0.01f), AlongZ, sides: 22);
                for (int i = 0; i < 5; i++) k.Cyl(0.185f, 0.022f, Paint.Steel, new Vector3(0, 0.08f, -0.14f + i * 0.07f), AlongZ, sides: 22);
                k.GlowCyl(0.06f, 0.012f, new Vector3(1f, 0.25f, 0.2f), 2.2f, new Vector3(0, 0.08f, -0.226f), AlongZ);
                k.Box(new Vector3(0.08f, 0.06f, 0.42f), Paint.Dark, new Vector3(0.19f, -0.0f, 0.02f));
                break;

            case BlockKind.Breech when def.Breech.Rocket:
                // A traversing base and the cradle the tubes sit in, with an elevating ram.
                k.Bevel(new Vector3(Full, 0.22f, Full), Paint.Self, new Vector3(0, -0.13f, 0));
                k.Cyl(0.07f, Full * 0.96f, Paint.Steel, new Vector3(0, 0.05f, -0.12f), AlongX, sides: 14);
                k.Bevel(new Vector3(0.4f, 0.1f, 0.36f), Paint.SelfDark, new Vector3(0, 0.1f, -0.04f), 0.015f);
                k.Cyl(0.035f, 0.24f, Paint.Steel, new Vector3(0, 0.03f, 0.14f), Rot(Vector3.UnitX, -0.7f), sides: 10);
                break;

            case BlockKind.Breech when def.Breech.PointDefense:
                // The white close-in gun mount: a drum housing with its fire-control dome on top.
                k.Bevel(new Vector3(Full, 0.3f, Full), Paint.Self, new Vector3(0, -0.1f, 0), 0.04f);
                k.Cyl(0.17f, 0.16f, Paint.Self, new Vector3(0, 0.12f, 0.03f), sides: 22);
                k.Sphere(0.15f, Paint.Self, new Vector3(0, 0.15f, 0.04f), new Vector3(1f, 0.45f, 1f));
                k.Cyl(0.1f, 0.08f, Paint.Dark, new Vector3(0, 0.05f, -0.24f), AlongZ, sides: 16);
                k.Box(new Vector3(0.2f, 0.04f, 0.2f), Paint.Dark, new Vector3(0, -0.24f, 0));
                break;

            case BlockKind.Breech:
            {
                float r = BarrelRadius(def.Breech?.CaliberMm ?? caliberMm);
                k.Bevel(new Vector3(Full, 0.44f, Full), Paint.Self, new Vector3(0, -0.03f, 0));
                k.Cyl(MathF.Min(r * 1.6f, 0.24f), 0.1f, Paint.Steel, new Vector3(0, 0, -0.27f), AlongZ, sides: 24);
                float rx = MathF.Min(r * 1.25f + 0.04f, 0.2f);
                foreach (float x in new[] { -rx, rx }) k.Cyl(0.032f, 0.44f, Paint.Steel, new Vector3(x, 0.21f, -0.02f), AlongZ, sides: 10);
                k.Box(new Vector3(0.3f, 0.24f, 0.06f), Paint.Dark, new Vector3(0, 0, 0.255f));
                k.Box(new Vector3(0.06f, 0.06f, 0.08f), Paint.Steel, new Vector3(0.1f, -0.05f, 0.29f));
                break;
            }

            case BlockKind.Barrel when def.BarrelTag == "rail":
                // Two copper-faced rails held apart by an insulated housing, top and bottom.
                foreach (float y in new[] { -0.11f, 0.11f }) k.Bevel(new Vector3(0.3f, 0.09f, Full * 1.01f), Paint.Self, new Vector3(0, y, 0), 0.01f);
                foreach (float x in new[] { -0.1f, 0.1f }) k.Box(new Vector3(0.05f, 0.14f, Full * 1.01f), Paint.Steel, new Vector3(x, 0, 0));
                k.Box(new Vector3(0.34f, 0.34f, 0.04f), Paint.SelfDark, new Vector3(0, 0, 0.2f));
                k.Glow(new Vector3(0.014f, 0.012f, Full * 0.98f), Cyan, 0.9f, new Vector3(0, 0.16f, 0));
                break;

            case BlockKind.Barrel when def.BarrelTag == "lens":
                k.Cyl(0.11f, Full * 1.01f, Paint.Self, Vector3.Zero, AlongZ, sides: 20);
                foreach (float z in new[] { -0.2f, 0.2f }) k.Cyl(0.13f, 0.05f, Paint.SelfDark, new Vector3(0, 0, z), AlongZ, sides: 20);
                k.Cyl(0.09f, 0.012f, Paint.Glass, new Vector3(0, 0, -0.252f), AlongZ, sides: 20);
                break;

            case BlockKind.Barrel when def.BarrelTag == "tube":
                // A pack of two launch tubes in a box frame, the open mouths at the front.
                k.Bevel(new Vector3(Full * 0.96f, 0.3f, Full * 1.01f), Paint.Self, Vector3.Zero, 0.012f);
                foreach (float x in new[] { -0.11f, 0.11f })
                {
                    k.Cyl(0.1f, 0.02f, Paint.Dark, new Vector3(x, 0, -0.247f), AlongZ, sides: 16);
                    k.Cyl(0.06f, 0.025f, Paint.Red, new Vector3(x, 0, -0.24f), AlongZ, sides: 12);
                }

                k.Box(new Vector3(Full * 0.98f, 0.04f, 0.04f), Paint.SelfDark, new Vector3(0, 0.15f, 0.12f));
                break;

            case BlockKind.Barrel when caliberMm <= 20.5f:
                // A gatling: six thin barrels round a spindle, with clamps.
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.Tau / 6;
                    k.Cyl(0.018f, Full * 1.01f, Paint.Self, new Vector3(MathF.Cos(a) * 0.05f, MathF.Sin(a) * 0.05f, 0), AlongZ, sides: 8);
                }

                k.Cyl(0.02f, Full * 1.01f, Paint.Steel, Vector3.Zero, AlongZ, sides: 8);
                k.Cyl(0.08f, 0.035f, Paint.SelfDark, new Vector3(0, 0, 0.1f), AlongZ, sides: 16);
                break;

            case BlockKind.Barrel:
                k.Cyl(br, Full * 1.01f, Paint.Self, Vector3.Zero, AlongZ, sides: 24);
                k.Cyl(br * 1.12f, 0.035f, Paint.SelfDark, new Vector3(0, 0, 0.215f), AlongZ, sides: 24);
                break;

            case BlockKind.MuzzleBrake:
                k.Cyl(br, Full, Paint.Self, Vector3.Zero, AlongZ, sides: 24);
                k.Cyl(br * 1.7f, 0.28f, Paint.Self, new Vector3(0, 0, -0.04f), AlongZ, sides: 24);
                k.Cyl(br * 1.78f, 0.04f, Paint.SelfDark, new Vector3(0, 0, -0.18f), AlongZ, sides: 24);
                k.Box(new Vector3(br * 4f, 0.06f, 0.07f), Paint.Dark, new Vector3(0, 0, -0.06f));
                k.Box(new Vector3(br * 4f, 0.06f, 0.07f), Paint.Dark, new Vector3(0, 0, 0.05f));
                break;

            case BlockKind.Loader:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                k.Box(new Vector3(Full * 1.01f, 0.06f, 0.3f), Paint.Steel, Vector3.Zero);
                k.Cyl(0.05f, 0.46f, Paint.Steel, new Vector3(0, 0.2f, 0), AlongZ, sides: 10);
                k.Box(new Vector3(0.14f, 0.08f, 0.1f), Paint.Dark, new Vector3(0, 0.22f, 0.17f));
                break;

            case BlockKind.Propellant:
                k.Bevel(new Vector3(Full, Full * 0.6f, Full), Paint.Dark, new Vector3(0, -0.1f, 0));
                for (int i = 0; i < 4; i++)
                {
                    var p = new Vector3(i % 2 == 0 ? -0.11f : 0.11f, 0.05f, i < 2 ? -0.11f : 0.11f);
                    k.Cyl(0.08f, 0.36f, Paint.Self, p, sides: 14);
                    k.Cyl(0.082f, 0.03f, Paint.Brass, p + new Vector3(0, 0.17f, 0), sides: 14);
                }

                break;

            case BlockKind.AmmoRack when def.Ammo == AmmoType.Torpedo:
                k.Bevel(new Vector3(Full, Full * 0.4f, Full), Paint.Self, new Vector3(0, -0.15f, 0));
                k.Cyl(0.11f, Full * 0.8f, Paint.Steel, new Vector3(0, 0.15f, 0.02f), AlongZ, sides: 16);
                k.Cyl(0.11f, 0.1f, Paint.Red, new Vector3(0, 0.15f, -0.24f), NoseZ, 0.03f, 16);
                k.Box(new Vector3(0.22f, 0.02f, 0.05f), Paint.Dark, new Vector3(0, 0.15f, 0.23f));
                k.Box(new Vector3(0.02f, 0.22f, 0.05f), Paint.Dark, new Vector3(0, 0.15f, 0.23f));
                break;

            case BlockKind.AmmoRack:
            {
                // A rack of nine rounds standing on end: brass cases, tips colored by type.
                k.Bevel(new Vector3(Full, 0.26f, Full), Paint.Self, new Vector3(0, -0.12f, 0));
                k.Box(new Vector3(0.48f, 0.03f, 0.48f), Paint.SelfDark, new Vector3(0, 0.02f, 0));
                Paint tip = def.Ammo switch { AmmoType.HE => Paint.Red, AmmoType.Flak => Paint.Steel, _ => Paint.Dark };
                for (int i = 0; i < 9; i++)
                {
                    var p = new Vector3((i % 3 - 1) * 0.145f, 0, (i / 3 - 1) * 0.145f);
                    k.Cyl(0.055f, 0.16f, Paint.Brass, p + new Vector3(0, 0.1f, 0), sides: 12);
                    k.Cyl(0.055f, 0.08f, tip, p + new Vector3(0, 0.22f, 0), top: 0.012f, sides: 12);
                }

                break;
            }

            case BlockKind.AmmoFeed:
                k.Bevel(new Vector3(0.3f), Paint.Self, Vector3.Zero, 0.03f);
                k.Box(new Vector3(Full, 0.12f, 0.12f), Paint.Dark, Vector3.Zero);
                k.Box(new Vector3(0.12f, Full, 0.12f), Paint.Dark, Vector3.Zero);
                k.Box(new Vector3(0.12f, 0.12f, Full), Paint.Dark, Vector3.Zero);
                break;

            case BlockKind.ShipPropeller:
                k.Cyl(0.06f, 0.3f, Paint.Steel, new Vector3(0, 0, 0.05f), AlongZ, sides: 12);
                k.Cyl(0.1f, 0.16f, Paint.Self, new Vector3(0, 0, -0.16f), NoseZ, 0.03f, 16);
                ScrewBlades(k, 4, 0.45f, 0.14f, Paint.Self, new Vector3(0, 0, -0.12f));
                break;

            case BlockKind.Rudder:
                k.Box(new Vector3(0.06f, Full * 0.95f, Full * 0.8f), Paint.Self, new Vector3(0, 0, 0.03f));
                k.Cyl(0.03f, Full * 0.95f, Paint.Self, new Vector3(0, 0, -0.17f), sides: 10);
                k.Cyl(0.035f, Full, Paint.Steel, new Vector3(0, 0.02f, -0.2f), sides: 10);
                k.Cyl(0.06f, 0.05f, Paint.Steel, new Vector3(0, 0.235f, -0.2f), sides: 12);
                break;

            case BlockKind.BallastValve:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                k.Cyl(0.03f, 0.08f, Paint.Steel, new Vector3(0, 0.27f, 0), sides: 8);
                k.Torus(0.11f, 0.016f, Paint.Red, new Vector3(0, 0.3f, 0));
                k.Box(new Vector3(0.22f, 0.015f, 0.02f), Paint.Red, new Vector3(0, 0.3f, 0));
                k.Box(new Vector3(0.02f, 0.015f, 0.22f), Paint.Red, new Vector3(0, 0.3f, 0));
                break;

            case BlockKind.BilgePump:
                k.Bevel(new Vector3(Full, Full * 0.6f, Full), Paint.Self, new Vector3(0, -0.1f, 0));
                k.Cyl(0.1f, 0.22f, Paint.SelfDark, new Vector3(-0.08f, 0.15f, 0.04f), sides: 16);
                k.Cyl(0.05f, Full, Paint.Steel, new Vector3(0.15f, 0.08f, 0), AlongZ, sides: 10);
                foreach (float z in new[] { -0.2f, 0.2f }) k.Cyl(0.075f, 0.03f, Paint.Steel, new Vector3(0.15f, 0.08f, z), AlongZ, sides: 10);
                break;

            case BlockKind.LeadBallast:
                Ingots(k, new Vector3(Full), Paint.Self);
                break;

            case BlockKind.TorpedoTube:
                k.Bevel(new Vector3(Full, Full * 0.36f, Full), Paint.Dark, new Vector3(0, -0.16f, 0));
                k.Cyl(0.19f, Full * 1.02f, Paint.Self, new Vector3(0, 0.05f, 0), AlongZ, sides: 24);
                foreach (float z in new[] { -0.12f, 0.12f }) k.Cyl(0.2f, 0.04f, Paint.SelfDark, new Vector3(0, 0.05f, z), AlongZ, sides: 24);
                k.Torus(0.16f, 0.03f, Paint.Dark, new Vector3(0, 0.05f, -0.255f), AlongZ);
                k.Cyl(0.13f, 0.02f, Paint.Dark, new Vector3(0, 0.05f, 0.26f), AlongZ, sides: 20);
                break;

            case BlockKind.Wing:
                k.Box(new Vector3(Full, 0.06f, Full), Paint.Self, Vector3.Zero);
                foreach (float x in new[] { -0.235f, 0.235f }) k.Box(new Vector3(0.03f, 0.07f, Full * 0.98f), Paint.SelfDark, new Vector3(x, 0, 0));
                break;

            case BlockKind.ControlSurface:
            {
                // A fixed strip at the front, and the flap hinged behind it (swings in flight).
                k.Box(new Vector3(Full, 0.07f, 0.12f), Paint.Self, new Vector3(0, 0, -0.19f));
                k.Cyl(0.025f, Full, Paint.Steel, new Vector3(0, 0, -0.13f), AlongX, sides: 8);
                k.Node = k.AddNode("Flap", new Vector3(0, 0, -0.13f));
                k.Slab(new Vector3(Full * 0.98f, 0.37f, 0.05f), 1f, 0.45f, Paint.Self, new Vector3(0, 0, 0.185f), AlongZ);
                k.Node = -1;
                break;
            }

            case BlockKind.AirPropeller:
            {
                k.Cyl(0.13f, 0.26f, Paint.Dark, new Vector3(0, 0, 0.08f), AlongZ, sides: 16);
                k.Node = k.AddNode("Spinner", new Vector3(0, 0, -0.14f));
                k.Cyl(0.14f, 0.2f, Paint.Self, new Vector3(0, 0, -0.1f), NoseZ, 0.02f, 20);
                for (int i = 0; i < 4; i++)
                {
                    Quaternion q = Rot(Vector3.UnitZ, i * MathF.PI / 2);
                    float rr = Aero.PropRadiusM;
                    k.Slab(new Vector3(0.17f, rr * 0.5f, 0.035f), 0.78f, 0.85f, Paint.Self, Vector3.Transform(new Vector3(0, rr * 0.27f, 0), q), q * Rot(Vector3.UnitY, 0.32f));
                    k.Slab(new Vector3(0.133f, rr * 0.48f, 0.03f), 0.6f, 0.8f, Paint.Self, Vector3.Transform(new Vector3(0, rr * 0.74f, 0), q), q * Rot(Vector3.UnitY, 0.2f));
                    k.Box(new Vector3(0.075f, 0.08f, 0.026f), Paint.Brass, Vector3.Transform(new Vector3(0, rr * 0.97f, 0), q), q * Rot(Vector3.UnitY, 0.2f));
                }

                k.Node = -1;
                break;
            }

            case BlockKind.JetEngine:
                k.Cyl(0.24f, Full * 0.9f, Paint.Self, new Vector3(0, 0, 0.01f), AlongZ, sides: 24);
                k.Torus(0.215f, 0.035f, Paint.Steel, new Vector3(0, 0, -0.23f), AlongZ);
                k.Cyl(0.2f, 0.02f, Paint.Dark, new Vector3(0, 0, -0.215f), AlongZ, sides: 24);
                k.Cyl(0.06f, 0.08f, Paint.Steel, new Vector3(0, 0, -0.24f), NoseZ, 0.01f, 12);
                k.Cyl(0.19f, 0.12f, Paint.Dark, new Vector3(0, 0, 0.29f), TailZ, 0.15f, 20);
                k.GlowCyl(0.12f, 0.02f, new Vector3(1f, 0.55f, 0.2f), 2.5f, new Vector3(0, 0, 0.33f), AlongZ);
                // The exhaust flame, a meter long at full throttle (stretched further on afterburner).
                k.Node = k.AddNode("Flame", new Vector3(0, 0, 0.34f));
                k.GlowCyl(0.13f, 1f, new Vector3(1f, 0.5f, 0.18f), 2.2f, new Vector3(0, 0, 0.5f), TailZ, 0.015f);
                k.GlowCyl(0.075f, 0.55f, new Vector3(0.65f, 0.8f, 1f), 3f, new Vector3(0, 0, 0.275f), TailZ, 0.01f);
                k.Node = -1;
                break;

            case BlockKind.Rotor:
            {
                k.Cyl(0.07f, Full, Paint.Steel, Vector3.Zero, sides: 12);
                k.Cyl(0.16f, 0.04f, Paint.Steel, new Vector3(0, 0.12f, 0), sides: 20);
                k.Node = k.AddNode("Spinner", new Vector3(0, 0.22f, 0));
                k.Cyl(0.22f, 0.1f, Paint.Dark, Vector3.Zero, sides: 20);
                k.Cyl(0.08f, 0.1f, Paint.Steel, new Vector3(0, 0.09f, 0), top: 0.02f, sides: 12);
                float rr = Aero.RotorRadiusM;
                for (int i = 0; i < 4; i++)
                {
                    Quaternion q = Rot(Vector3.UnitY, i * MathF.PI / 2);
                    k.Box(new Vector3(0.34f, 0.05f, rr * 0.5f), Paint.Self, Vector3.Transform(new Vector3(0, 0, rr * 0.27f), q), q);
                    k.Box(new Vector3(0.3f, 0.045f, rr * 0.46f), Paint.Self, Vector3.Transform(new Vector3(0, 0, rr * 0.75f), q), q);
                    k.Box(new Vector3(0.3f, 0.05f, 0.2f), Paint.Brass, Vector3.Transform(new Vector3(0, 0, rr * 0.97f), q), q);
                }

                k.Node = -1;
                break;
            }

            case BlockKind.LiftFan:
            {
                // Its arrow (forward) is the way it blows; the duct ring sits across that axis.
                float fr = Aero.FanRadiusM;
                k.Torus(fr * 0.985f, fr * 0.07f, Paint.Self, Vector3.Zero, AlongZ);
                k.Bevel(new Vector3(0.3f, 0.3f, Full * 0.9f), Paint.Dark, Vector3.Zero, 0.04f);
                for (int i = 0; i < 3; i++)
                {
                    Quaternion q = Rot(Vector3.UnitZ, i * MathF.Tau / 3);
                    k.Box(new Vector3(0.05f, fr * 0.8f, 0.03f), Paint.Dark, Vector3.Transform(new Vector3(0, fr * 0.55f, 0.08f), q), q);
                }

                k.Node = k.AddNode("Spinner", Vector3.Zero);
                for (int i = 0; i < 6; i++)
                {
                    Quaternion q = Rot(Vector3.UnitZ, i * MathF.Tau / 6);
                    k.Slab(new Vector3(0.14f, fr * 0.75f, 0.025f), 0.7f, 1f, Paint.Steel, Vector3.Transform(new Vector3(0, fr * 0.5f, -0.03f), q), q * Rot(Vector3.UnitY, 0.45f));
                }

                k.Node = -1;
                break;
            }

            case BlockKind.Generator:
                // An alternator on a skid with cooling ribs, a terminal box with its lamp, and an exhaust stub.
                k.Bevel(new Vector3(Full, 0.07f, Full), Paint.Dark, new Vector3(0, -0.215f, 0), 0.015f);
                k.Cyl(0.17f, 0.42f, Paint.Self, new Vector3(0, -0.02f, 0), AlongZ, sides: 22);
                for (int i = 0; i < 5; i++) k.Cyl(0.185f, 0.02f, Paint.SelfDark, new Vector3(0, -0.02f, -0.16f + i * 0.08f), AlongZ, sides: 22);
                k.Bevel(new Vector3(0.14f, 0.1f, 0.12f), Paint.Dark, new Vector3(0.15f, 0.18f, 0.12f), 0.01f);
                k.Glow(new Vector3(0.06f, 0.025f, 0.006f), Cyan, 1.5f, new Vector3(0.15f, 0.19f, 0.058f));
                k.Cyl(0.022f, 0.12f, Paint.Steel, new Vector3(-0.14f, 0.19f, 0.12f), sides: 8);
                break;

            case BlockKind.Capacitor:
                // Four big cells on a tray, with terminals and a charge lamp.
                k.Bevel(new Vector3(Full, 0.06f, Full), Paint.Dark, new Vector3(0, -0.22f, 0), 0.015f);
                for (int i = 0; i < 4; i++)
                {
                    var p = new Vector3(i % 2 == 0 ? -0.115f : 0.115f, 0, i < 2 ? -0.115f : 0.115f);
                    k.Cyl(0.098f, 0.36f, Paint.Self, p + new Vector3(0, -0.01f, 0), sides: 18);
                    k.Cyl(0.102f, 0.03f, Paint.SelfDark, p + new Vector3(0, 0.17f, 0), sides: 18);
                    k.Cyl(0.025f, 0.04f, Paint.Steel, p + new Vector3(0.03f, 0.2f, 0), sides: 8);
                }

                k.Glow(new Vector3(0.018f, 0.26f, 0.018f), Cyan, 1.3f, new Vector3(0, -0.03f, -0.235f));
                break;

            case BlockKind.VlsCell:
            {
                // A flush deck box with an open well under its hatch; the hatch (with its stripe) only shows while loaded.
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                k.Box(new Vector3(0.36f, 0.02f, 0.36f), Paint.Dark, new Vector3(0, 0.243f, 0));
                k.Box(new Vector3(0.05f, 0.03f, 0.12f), Paint.Steel, new Vector3(0.2f, 0.25f, 0));
                k.Node = k.AddNode("Payload", new Vector3(0, 0.25f, 0));
                k.Bevel(new Vector3(0.36f, 0.025f, 0.36f), Paint.SelfDark, new Vector3(0, 0.004f, 0), 0.008f);
                k.Box(new Vector3(0.3f, 0.012f, 0.04f), def.Vls == VlsLoad.Sam ? Paint.Brass : Paint.Red, new Vector3(0, 0.018f, -0.13f));
                k.Node = -1;
                break;
            }

            case BlockKind.DroneCore:
                k.Bevel(new Vector3(Full, Full * 0.7f, Full), Paint.Self, new Vector3(0, -0.075f, 0));
                k.Cyl(0.015f, 0.3f, Paint.Steel, new Vector3(0.12f, 0.3f, 0.1f), sides: 6);
                k.Sphere(0.025f, Paint.Red, new Vector3(0.12f, 0.45f, 0.1f));
                k.Sphere(0.09f, Paint.Glass, new Vector3(0, 0.1f, -0.16f), new Vector3(1, 0.7f, 1));
                k.Glow(new Vector3(0.1f, 0.04f, 0.04f), Green, 2f, new Vector3(0, 0.18f, -0.2f));
                break;

            case BlockKind.BombRack:
            {
                k.Box(new Vector3(Full * 0.7f, 0.05f, Full), Paint.Dark, new Vector3(0, 0.22f, 0));
                foreach (float z in new[] { -0.12f, 0.12f }) k.Box(new Vector3(0.04f, 0.12f, 0.04f), Paint.Steel, new Vector3(0, 0.15f, z));
                k.Node = k.AddNode("Payload", Vector3.Zero);
                k.Cyl(0.17f, 0.4f, Paint.Self, new Vector3(0, -0.02f, 0), AlongZ, sides: 20);
                k.Cyl(0.17f, 0.14f, Paint.Self, new Vector3(0, -0.02f, -0.27f), NoseZ, 0.03f, 20);
                k.Cyl(0.17f, 0.1f, Paint.Self, new Vector3(0, -0.02f, 0.25f), TailZ, 0.08f, 20);
                k.Cyl(0.05f, 0.04f, Paint.Brass, new Vector3(0, -0.02f, -0.34f), AlongZ, sides: 10);
                k.Box(new Vector3(0.36f, 0.02f, 0.12f), Paint.Dark, new Vector3(0, -0.02f, 0.3f));
                k.Box(new Vector3(0.02f, 0.36f, 0.12f), Paint.Dark, new Vector3(0, -0.02f, 0.3f));
                k.Cyl(0.172f, 0.03f, Paint.Brass, new Vector3(0, -0.02f, -0.12f), AlongZ, sides: 20);
                k.Node = -1;
                break;
            }

            case BlockKind.MissileRail:
                k.Box(new Vector3(0.3f, 0.12f, 0.3f), Paint.Self, new Vector3(0, -0.12f, 0));
                k.Box(new Vector3(0.1f, 0.06f, Full), Paint.Steel, new Vector3(0, -0.03f, 0));
                k.Box(new Vector3(0.16f, 0.02f, Full * 0.9f), Paint.Dark, new Vector3(0, -0.065f, 0));
                break;

            case BlockKind.MissileSeeker when def.Seeker == SeekerKind.Radar:
                k.Cyl(0.13f, 0.36f, Paint.Self, new Vector3(0, 0, -0.06f), NoseZ, 0.008f, 24);
                k.Cyl(0.13f, 0.14f, Paint.Dark, new Vector3(0, 0, 0.18f), AlongZ, sides: 24);
                k.Cyl(0.132f, 0.02f, Paint.Steel, new Vector3(0, 0, 0.12f), AlongZ, sides: 24);
                break;

            case BlockKind.MissileSeeker:
                k.Cyl(0.13f, 0.3f, Paint.Self, new Vector3(0, 0, 0.1f), AlongZ, sides: 24);
                k.Sphere(0.12f, Paint.Glass, new Vector3(0, 0, -0.07f));
                k.Cyl(0.132f, 0.02f, Paint.Steel, new Vector3(0, 0, -0.05f), AlongZ, sides: 24);
                break;

            case BlockKind.MissileWarhead:
                k.Cyl(0.13f, Full * 1.01f, Paint.Self, Vector3.Zero, AlongZ, sides: 24);
                k.Cyl(0.135f, 0.06f, Paint.Brass, new Vector3(0, 0, -0.1f), AlongZ, sides: 24);
                k.Box(new Vector3(0.04f, 0.04f, 0.16f), Paint.Red, new Vector3(0, 0.12f, 0.08f));
                break;

            case BlockKind.MissileMotor:
                k.Cyl(0.13f, Full * 1.01f, Paint.Self, Vector3.Zero, AlongZ, sides: 24);
                k.Cyl(0.08f, 0.08f, Paint.Dark, new Vector3(0, 0, 0.27f), TailZ, 0.11f, 16);
                k.Cyl(0.133f, 0.025f, Paint.SelfDark, new Vector3(0, 0, -0.2f), AlongZ, sides: 24);
                break;

            case BlockKind.MissileFins:
                k.Cyl(0.13f, Full * 1.01f, Paint.Self, Vector3.Zero, AlongZ, sides: 24);
                for (int i = 0; i < 4; i++)
                {
                    Quaternion q = Rot(Vector3.UnitZ, MathF.PI / 4 + i * MathF.PI / 2);
                    k.Slab(new Vector3(0.025f, 0.15f, 0.26f), 1f, 0.5f, Paint.Dark, Vector3.Transform(new Vector3(0, 0.2f, 0.08f), q), q);
                }

                break;

            default:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                break;
        }

        if (hints && def.Kind is BlockKind.Breech or BlockKind.TorpedoTube or BlockKind.ShipPropeller or BlockKind.AirPropeller
                or BlockKind.JetEngine or BlockKind.LiftFan or BlockKind.MissileRail)
        {
            // Arrow showing which way the block faces (where a gun's barrels go).
            k.Node = -1;
            k.GlowCyl(0.09f, 0.2f, Amber, 2f, new Vector3(0, 0.32f, -0.12f), NoseZ, 0f);
        }

        return k.M;
    }

    /// <summary>A stretch of track: a road wheel on an axle along X, and links top and bottom with grousers.</summary>
    private static readonly Vector3 Lamp = new(1f, 0.92f, 0.75f);

    /// <summary>An open cockpit: a tub, a seat, a stick and an instrument panel, made to sit under glazing.</summary>
    private static void PilotSeat(Maker k)
    {
        k.Bevel(new Vector3(0.46f, 0.08f, 0.46f), Paint.SelfDark, new Vector3(0, -0.21f, 0), 0.015f);
        foreach (float x in new[] { -0.2f, 0.2f }) k.Box(new Vector3(0.05f, 0.08f, 0.3f), Paint.SelfDark, new Vector3(x, -0.13f, 0.02f));
        k.Bevel(new Vector3(0.22f, 0.05f, 0.2f), Paint.Dark, new Vector3(0, -0.12f, 0.06f), 0.012f);
        k.Bevel(new Vector3(0.22f, 0.26f, 0.05f), Paint.Dark, new Vector3(0, 0f, 0.17f), 0.012f, Rot(Vector3.UnitX, -0.2f));
        k.Bevel(new Vector3(0.14f, 0.07f, 0.05f), Paint.Dark, new Vector3(0, 0.16f, 0.2f), 0.01f);
        k.Cyl(0.012f, 0.16f, Paint.Steel, new Vector3(0, -0.1f, -0.06f), sides: 8);
        k.Cyl(0.02f, 0.05f, Paint.Dark, new Vector3(0, -0.005f, -0.06f), sides: 10);
        k.Cyl(0.008f, 0.08f, Paint.Steel, new Vector3(-0.2f, -0.06f, 0.02f), sides: 6);
        // The instrument panel, angled toward the pilot, with lit dials and a screen.
        Quaternion tilt = Rot(Vector3.UnitX, 0.35f);
        k.Bevel(new Vector3(0.36f, 0.13f, 0.04f), Paint.Dark, new Vector3(0, -0.04f, -0.2f), 0.01f, tilt);
        k.Glow(new Vector3(0.1f, 0.05f, 0.006f), Amber, 1.2f, new Vector3(0, -0.035f, -0.226f));
        foreach (float x in new[] { -0.12f, 0.12f }) k.GlowCyl(0.022f, 0.006f, Cyan, 1.2f, new Vector3(x, -0.04f, -0.224f), AlongZ);
        k.Box(new Vector3(0.38f, 0.02f, 0.06f), Paint.SelfDark, new Vector3(0, 0.03f, -0.21f));
    }

    /// <summary>Cosmetic parts. Each sits on the bottom of its cell (or against the back), so it can go on any face.</summary>
    private static void Detail(Maker k, BlockDef def)
    {
        const float b = -H;
        switch (def.Id)
        {
            case "hatch":
                k.Cyl(0.19f, 0.03f, Paint.SelfDark, new Vector3(0, b + 0.015f, 0), sides: 24);
                k.Cyl(0.17f, 0.04f, Paint.Self, new Vector3(0, b + 0.05f, 0), sides: 24);
                k.Cyl(0.05f, 0.02f, Paint.SelfDark, new Vector3(0, b + 0.075f, 0), sides: 12);
                k.Cyl(0.025f, 0.14f, Paint.Steel, new Vector3(0, b + 0.05f, 0.18f), AlongX, sides: 10);
                k.Box(new Vector3(0.08f, 0.02f, 0.015f), Paint.Steel, new Vector3(0, b + 0.078f, -0.1f));
                k.Bolts(8, 0.18f, b + 0.032f, 0.01f, 0.02f, Paint.Steel);
                break;
            case "periscope":
                k.Bevel(new Vector3(0.2f, 0.06f, 0.16f), Paint.SelfDark, new Vector3(0, b + 0.03f, 0), 0.01f);
                k.Bevel(new Vector3(0.15f, 0.1f, 0.12f), Paint.Self, new Vector3(0, b + 0.11f, 0), 0.01f);
                k.Box(new Vector3(0.11f, 0.045f, 0.01f), Paint.Glass, new Vector3(0, b + 0.115f, -0.062f));
                k.Box(new Vector3(0.16f, 0.012f, 0.05f), Paint.SelfDark, new Vector3(0, b + 0.165f, -0.07f));
                break;
            case "headlight":
                k.Box(new Vector3(0.04f, 0.12f, 0.04f), Paint.Dark, new Vector3(0, b + 0.06f, 0.04f));
                k.Cyl(0.075f, 0.12f, Paint.Self, new Vector3(0, b + 0.15f, 0), AlongZ, sides: 16);
                k.GlowCyl(0.06f, 0.01f, Lamp, 1.2f, new Vector3(0, b + 0.15f, -0.062f), AlongZ);
                k.Torus(0.08f, 0.008f, Paint.Dark, new Vector3(0, b + 0.15f, -0.075f), AlongZ);
                k.Box(new Vector3(0.012f, 0.16f, 0.012f), Paint.Dark, new Vector3(0, b + 0.15f, -0.078f));
                k.Box(new Vector3(0.16f, 0.012f, 0.012f), Paint.Dark, new Vector3(0, b + 0.15f, -0.078f));
                break;
            case "toolbox":
                k.Bevel(new Vector3(0.42f, 0.16f, 0.22f), Paint.Self, new Vector3(0, b + 0.08f, 0), 0.012f);
                k.Box(new Vector3(0.43f, 0.012f, 0.225f), Paint.SelfDark, new Vector3(0, b + 0.125f, 0));
                foreach (float x in new[] { -0.12f, 0.12f }) k.Box(new Vector3(0.03f, 0.05f, 0.012f), Paint.Steel, new Vector3(x, b + 0.11f, -0.113f));
                foreach (float x in new[] { -0.17f, 0.17f }) k.Box(new Vector3(0.02f, 0.165f, 0.226f), Paint.SelfDark, new Vector3(x, b + 0.08f, 0));
                break;
            case "spare_tracks":
                k.Box(new Vector3(0.46f, 0.02f, 0.03f), Paint.Dark, new Vector3(0, b + 0.045f, 0));
                for (int i = -1; i <= 1; i++)
                {
                    k.Box(new Vector3(0.42f, 0.035f, 0.12f), Paint.Self, new Vector3(0, b + 0.018f, i * 0.14f));
                    k.Box(new Vector3(0.06f, 0.05f, 0.06f), Paint.Steel, new Vector3(0, b + 0.045f, i * 0.14f));
                    k.Cyl(0.012f, 0.44f, Paint.Steel, new Vector3(0, b + 0.018f, i * 0.14f + 0.06f), AlongX, sides: 6);
                }

                break;
            case "antenna":
                k.Cyl(0.04f, 0.06f, Paint.Dark, new Vector3(0, b + 0.03f, 0), sides: 12);
                k.Cyl(0.018f, 0.08f, Paint.Steel, new Vector3(0, b + 0.1f, 0), sides: 8);
                k.Cyl(0.006f, 1.5f, Paint.Dark, new Vector3(0, b + 0.89f, 0), top: 0.003f, sides: 6);
                k.Sphere(0.012f, Paint.Dark, new Vector3(0, b + 1.64f, 0));
                break;
            case "railing":
                foreach (float x in new[] { -0.22f, 0.22f }) k.Cyl(0.014f, Full, Paint.Self, new Vector3(x, 0, 0.22f), sides: 8);
                k.Cyl(0.012f, Full, Paint.Self, new Vector3(0, 0.23f, 0.22f), AlongX, sides: 8);
                k.Cyl(0.01f, Full, Paint.Self, new Vector3(0, 0f, 0.22f), AlongX, sides: 8);
                k.Box(new Vector3(Full, 0.012f, 0.04f), Paint.SelfDark, new Vector3(0, b + 0.006f, 0.22f));
                break;
            case "ladder":
                foreach (float x in new[] { -0.15f, 0.15f })
                {
                    k.Box(new Vector3(0.02f, Full, 0.02f), Paint.Self, new Vector3(x, 0, 0.215f));
                    foreach (float y in new[] { -0.18f, 0.18f }) k.Box(new Vector3(0.02f, 0.02f, 0.03f), Paint.Dark, new Vector3(x, y, 0.235f));
                }

                for (int i = 0; i < 4; i++) k.Cyl(0.01f, 0.3f, Paint.Steel, new Vector3(0, -0.19f + i * 0.125f, 0.215f), AlongX, sides: 6);
                break;
            case "smoke_launcher":
            {
                k.Bevel(new Vector3(0.36f, 0.06f, 0.2f), Paint.SelfDark, new Vector3(0, b + 0.03f, 0.02f), 0.01f);
                Quaternion up = Rot(Vector3.UnitX, -0.6f);
                foreach (float x in new[] { -0.11f, 0f, 0.11f })
                {
                    var at = new Vector3(x, b + 0.13f, -0.02f);
                    k.Cyl(0.04f, 0.2f, Paint.Self, at, up, sides: 12);
                    k.Cyl(0.043f, 0.015f, Paint.Dark, at + Vector3.Transform(new Vector3(0, 0.1f, 0), up), up, sides: 12);
                }

                break;
            }

            case "exhaust":
                k.Cyl(0.08f, 0.36f, Paint.Dark, new Vector3(0, b + 0.1f, 0), AlongX, sides: 16);
                k.Box(new Vector3(0.38f, 0.012f, 0.18f), Paint.Self, new Vector3(0, b + 0.19f, 0));
                foreach (float x in new[] { -0.12f, 0.12f }) k.Box(new Vector3(0.02f, 0.18f, 0.18f), Paint.SelfDark, new Vector3(x, b + 0.1f, 0));
                k.Cyl(0.03f, 0.12f, Paint.Dark, new Vector3(0.2f, b + 0.1f, 0.06f), AlongX, sides: 10);
                break;
            case "searchlight":
                k.Cyl(0.12f, 0.04f, Paint.SelfDark, new Vector3(0, b + 0.02f, 0), sides: 20);
                foreach (float x in new[] { -0.15f, 0.15f }) k.Box(new Vector3(0.03f, 0.2f, 0.05f), Paint.Self, new Vector3(x, b + 0.13f, 0));
                k.Cyl(0.13f, 0.22f, Paint.Self, new Vector3(0, b + 0.2f, 0), AlongZ, sides: 20);
                k.GlowCyl(0.115f, 0.01f, Lamp, 1.4f, new Vector3(0, b + 0.2f, -0.112f), AlongZ);
                k.Torus(0.125f, 0.012f, Paint.Steel, new Vector3(0, b + 0.2f, -0.11f), AlongZ);
                k.Cyl(0.03f, 0.34f, Paint.Steel, new Vector3(0, b + 0.2f, 0), AlongX, sides: 10);
                break;
            case "vent":
                k.Bevel(new Vector3(0.44f, 0.04f, 0.44f), Paint.SelfDark, new Vector3(0, b + 0.02f, 0), 0.01f);
                for (int i = 0; i < 6; i++)
                    k.Box(new Vector3(0.38f, 0.03f, 0.025f), Paint.Self, new Vector3(0, b + 0.05f, -0.16f + i * 0.064f), Rot(Vector3.UnitX, 0.5f));
                break;
            case "grab_handles":
                foreach (float z in new[] { -0.12f, 0.12f })
                {
                    foreach (float x in new[] { -0.08f, 0.08f }) k.Cyl(0.01f, 0.07f, Paint.Self, new Vector3(x, b + 0.035f, z), sides: 6);
                    k.Cyl(0.01f, 0.17f, Paint.Self, new Vector3(0, b + 0.07f, z), AlongX, sides: 6);
                }

                break;
            default:
                // Stowage rack: a frame with a bedroll, a kit bag and a jerry can.
                foreach (float z in new[] { -0.15f, 0.15f })
                {
                    k.Box(new Vector3(0.46f, 0.015f, 0.015f), Paint.Dark, new Vector3(0, b + 0.008f, z));
                    k.Box(new Vector3(0.46f, 0.015f, 0.015f), Paint.Dark, new Vector3(0, b + 0.14f, z));
                    foreach (float x in new[] { -0.22f, 0.22f }) k.Box(new Vector3(0.015f, 0.14f, 0.015f), Paint.Dark, new Vector3(x, b + 0.07f, z));
                }

                k.Cyl(0.07f, 0.4f, Paint.Self, new Vector3(0, b + 0.08f, 0.07f), AlongX, sides: 14);
                k.Bevel(new Vector3(0.18f, 0.12f, 0.12f), Paint.SelfDark, new Vector3(-0.1f, b + 0.07f, -0.07f), 0.03f);
                k.Bevel(new Vector3(0.1f, 0.16f, 0.13f), Paint.SelfDark, new Vector3(0.13f, b + 0.08f, -0.07f), 0.012f);
                break;
        }
    }

    private static void TrackRun(Maker k, float r, float width, float length)
    {
        k.Cyl(r * 0.8f, width * 0.6f, Paint.Dark, Vector3.Zero, AlongX, sides: 24);
        k.Cyl(r * 0.45f, width * 0.66f, Paint.Steel, Vector3.Zero, AlongX, sides: 16);
        k.Cyl(r * 0.16f, width * 0.72f, Paint.Dark, Vector3.Zero, AlongX, sides: 10);
        int links = Math.Max(2, (int)MathF.Round(length / 0.25f));
        float pitch = length / links;
        foreach (float y in new[] { -r, r })
        {
            float outward = MathF.Sign(y);
            for (int i = 0; i < links; i++)
            {
                float z = -length * 0.5f + pitch * (i + 0.5f);
                k.Box(new Vector3(width, 0.06f, pitch * 0.9f), Paint.Self, new Vector3(0, y, z));
                k.Box(new Vector3(width * 1.02f, 0.035f, 0.04f), Paint.Self, new Vector3(0, y + outward * 0.045f, z));
                k.Box(new Vector3(0.07f, 0.05f, pitch * 0.5f), Paint.Steel, new Vector3(0, y - outward * 0.04f, z));
            }
        }
    }

    /// <summary>Ship screw blades round Z, each in two twisted pieces.</summary>
    private static void ScrewBlades(Maker k, int n, float radius, float width, Paint p, Vector3 at)
    {
        for (int i = 0; i < n; i++)
        {
            Quaternion q = Rot(Vector3.UnitZ, i * MathF.Tau / n);
            k.Slab(new Vector3(width, radius * 0.5f, 0.025f * radius / 0.45f), 0.9f, 1f, p, at + Vector3.Transform(new Vector3(0, radius * 0.3f, 0), q), q * Rot(Vector3.UnitY, 0.45f));
            k.Slab(new Vector3(width * 0.9f, radius * 0.5f, 0.02f * radius / 0.45f), 0.55f, 0.8f, p, at + Vector3.Transform(new Vector3(0, radius * 0.76f, 0), q), q * Rot(Vector3.UnitY, 0.75f));
        }
    }

    /// <summary>Lead cast in bars, stacked crosswise.</summary>
    private static void Ingots(Maker k, Vector3 ext, Paint p)
    {
        float lay = ext.Y / 3f;
        for (int layer = 0; layer < 3; layer++)
        {
            bool alongX = layer % 2 == 0;
            int n = 3;
            float y = -ext.Y * 0.5f + lay * (layer + 0.5f);
            for (int i = 0; i < n; i++)
            {
                float off = (i - 1) * (alongX ? ext.Z : ext.X) / n;
                Vector3 size = alongX ? new Vector3(ext.X * 0.96f, lay * 0.92f, ext.Z / n * 0.88f) : new Vector3(ext.X / n * 0.88f, lay * 0.92f, ext.Z * 0.96f);
                k.Bevel(size, p, alongX ? new Vector3(0, y, off) : new Vector3(off, y, 0), MathF.Min(0.02f, lay * 0.2f));
            }
        }
    }

    /// <summary>
    /// A big block: built to its full size, centered on the middle of its box (its anchor cell isn't the
    /// middle when a size is even).
    /// </summary>
    private static void BuildBig(Maker k, BlockDef def, bool hints)
    {
        Vector3 ext = new Vector3(def.Size.X, def.Size.Y, def.Size.Z) * Full;
        k.Offset = Footprint.LocalCenter(def) * Full;
        float bevel = MathF.Min(0.06f, MathF.Min(ext.X, MathF.Min(ext.Y, ext.Z)) * 0.1f);
        switch (def.Kind)
        {
            case BlockKind.Engine:
            {
                // A marine turbine: casing on a skid, flanged rings, and a gearbox at the back.
                float r = MathF.Min(ext.X, ext.Y) * 0.4f;
                k.Box(new Vector3(ext.X, 0.12f, ext.Z), Paint.Dark, new Vector3(0, -ext.Y * 0.5f + 0.06f, 0));
                k.Cyl(r, ext.Z * 0.78f, Paint.Self, new Vector3(0, -0.05f, -ext.Z * 0.08f), AlongZ, sides: 28);
                for (int i = 0; i < 4; i++)
                    k.Cyl(r * 1.08f, 0.07f, Paint.SelfDark, new Vector3(0, -0.05f, -ext.Z * 0.42f + i * ext.Z * 0.24f), AlongZ, sides: 28);
                k.Bevel(new Vector3(ext.X * 0.86f, ext.Y * 0.62f, ext.Z * 0.2f), Paint.SelfDark, new Vector3(0, -ext.Y * 0.12f, ext.Z * 0.39f), 0.03f);
                k.Cyl(0.16f, ext.Y * 0.5f, Paint.Dark, new Vector3(0, ext.Y * 0.2f, -ext.Z * 0.3f), sides: 16);
                foreach (float x in new[] { -ext.X * 0.38f, ext.X * 0.38f })
                    k.Cyl(0.05f, ext.Z * 0.9f, Paint.Steel, new Vector3(x, ext.Y * 0.32f, 0), AlongZ, sides: 10);
                break;
            }

            case BlockKind.FuelTank:
            {
                float r = MathF.Min(ext.X, ext.Y) * 0.46f;
                k.Cyl(r, ext.Z * 0.96f, Paint.Self, Vector3.Zero, AlongZ, sides: 28);
                foreach (float z in new[] { -ext.Z * 0.3f, 0, ext.Z * 0.3f }) k.Cyl(r * 1.03f, 0.06f, Paint.Dark, new Vector3(0, 0, z), AlongZ, sides: 28);
                k.Box(new Vector3(ext.X, 0.08f, ext.Z), Paint.Dark, new Vector3(0, -ext.Y * 0.46f, 0));
                k.Cyl(0.08f, 0.1f, Paint.Steel, new Vector3(r * 0.3f, r + 0.02f, 0), sides: 12);
                break;
            }

            case BlockKind.Track:
                TrackRun(k, def.WheelRadiusM, ext.X * 0.95f, ext.Z);
                break;

            case BlockKind.ShipPropeller:
            {
                float r = MathF.Min(ext.X, ext.Y) * 0.5f;
                k.Cyl(r * 0.12f, ext.Z * 1.6f, Paint.Steel, new Vector3(0, 0, 0.1f), AlongZ, sides: 16);
                k.Cyl(r * 0.22f, ext.Z * 0.8f, Paint.Self, new Vector3(0, 0, -ext.Z * 0.3f), NoseZ, r * 0.06f, 20);
                ScrewBlades(k, 4, r * 0.95f, r * 0.42f, Paint.Self, new Vector3(0, 0, -0.05f));
                break;
            }

            case BlockKind.Rudder:
                k.Box(new Vector3(MathF.Max(ext.X * 0.22f, 0.1f), ext.Y * 0.95f, ext.Z * 0.82f), Paint.Self, new Vector3(0, 0, ext.Z * 0.06f));
                k.Cyl(MathF.Max(ext.X * 0.11f, 0.05f), ext.Y * 0.95f, Paint.Self, new Vector3(0, 0, -ext.Z * 0.35f), sides: 14);
                k.Cyl(0.08f, ext.Y, Paint.Steel, new Vector3(0, 0.05f, -ext.Z * 0.4f), sides: 12);
                k.Cyl(0.14f, 0.08f, Paint.Steel, new Vector3(0, ext.Y * 0.48f, -ext.Z * 0.4f), sides: 16);
                break;

            case BlockKind.Breech:
            {
                float r = BarrelRadius(def.Breech.CaliberMm);
                k.Bevel(ext * new Vector3(1f, 0.86f, 1f), Paint.Self, new Vector3(0, -ext.Y * 0.07f, 0), bevel);
                k.Cyl(r * 1.5f, 0.32f, Paint.Steel, new Vector3(0, 0, -ext.Z * 0.5f - 0.1f), AlongZ, sides: 28);
                foreach (float x in new[] { -r * 1.7f, r * 1.7f })
                foreach (float y in new[] { -r * 0.9f, r * 0.9f })
                    k.Cyl(0.07f, ext.Z * 0.9f, Paint.Steel, new Vector3(x, y + ext.Y * 0.1f, -ext.Z * 0.06f), AlongZ, sides: 12);
                k.Box(new Vector3(ext.X * 1.02f, 0.1f, ext.Z * 0.3f), Paint.Dark, new Vector3(0, ext.Y * 0.2f, ext.Z * 0.2f));
                k.Bevel(new Vector3(ext.X * 0.5f, ext.Y * 0.5f, 0.16f), Paint.Dark, new Vector3(0, 0, ext.Z * 0.5f + 0.07f), 0.03f);
                k.Box(new Vector3(0.18f, 0.12f, 0.2f), Paint.Steel, new Vector3(ext.X * 0.18f, -ext.Y * 0.12f, ext.Z * 0.5f + 0.12f));
                break;
            }

            case BlockKind.AmmoRack:
            {
                k.Bevel(ext * new Vector3(1f, 0.55f, 1f), Paint.Self, new Vector3(0, -ext.Y * 0.225f, 0), bevel);
                Paint tip = def.Ammo == AmmoType.HE ? Paint.Red : Paint.Dark;
                for (int i = 0; i < 9; i++)
                {
                    var p = new Vector3((i % 3 - 1) * ext.X * 0.29f, ext.Y * 0.05f, (i / 3 - 1) * ext.Z * 0.29f);
                    k.Cyl(ext.X * 0.11f, ext.Y * 0.28f, Paint.Brass, p + new Vector3(0, ext.Y * 0.14f, 0), sides: 16);
                    k.Cyl(ext.X * 0.11f, ext.Y * 0.16f, tip, p + new Vector3(0, ext.Y * 0.36f, 0), top: ext.X * 0.02f, sides: 16);
                }

                break;
            }

            case BlockKind.LeadBallast:
                Ingots(k, ext, Paint.Self);
                break;

            case BlockKind.Reactor:
            {
                // A squat armored vessel on a plinth, banded with field coils, a domed cap with the central
                // column, blue glowing ports round its waist, and coolant pipes running in from the corners.
                float r = ext.X * 0.36f, h = ext.Y * 0.58f, y0 = -ext.Y * 0.5f + 0.16f;
                k.Bevel(new Vector3(ext.X, 0.16f, ext.Z), Paint.Dark, new Vector3(0, -ext.Y * 0.5f + 0.08f, 0), 0.03f);
                k.Cyl(r, h, Paint.Self, new Vector3(0, y0 + h * 0.5f, 0), sides: 28);
                foreach (float f in new[] { 0.12f, 0.5f, 0.88f }) k.Cyl(r * 1.06f, 0.07f, Paint.SelfDark, new Vector3(0, y0 + h * f, 0), sides: 28);
                k.Sphere(r, Paint.Self, new Vector3(0, y0 + h, 0), new Vector3(1f, 0.45f, 1f));
                k.Cyl(ext.X * 0.08f, ext.Y * 0.2f, Paint.Steel, new Vector3(0, y0 + h + r * 0.45f, 0), sides: 16);
                k.Cyl(ext.X * 0.1f, 0.05f, Paint.SelfDark, new Vector3(0, y0 + h + r * 0.45f + ext.Y * 0.1f, 0), sides: 16);
                for (int i = 0; i < 8; i++)
                {
                    float a = (i + 0.5f) * MathF.Tau / 8;
                    var at = new Vector3(MathF.Sin(a) * r * 1.005f, y0 + h * 0.31f, MathF.Cos(a) * r * 1.005f);
                    k.Box(new Vector3(0.16f, 0.2f, 0.02f), Paint.Dark, at, Rot(Vector3.UnitY, a));
                    k.Glow(new Vector3(0.1f, 0.12f, 0.01f), new Vector3(0.45f, 0.6f, 1f), 3f, at + new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * 0.012f);
                    k.Box(new Vector3(0.06f, h * 0.9f, 0.04f), Paint.SelfDark, new Vector3(MathF.Sin(a + MathF.PI / 8) * r * 1.02f, y0 + h * 0.5f, MathF.Cos(a + MathF.PI / 8) * r * 1.02f),
                        Rot(Vector3.UnitY, a + MathF.PI / 8));
                }

                foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                {
                    var corner = new Vector3(x * ext.X * 0.42f, 0, z * ext.Z * 0.42f);
                    k.Cyl(0.07f, ext.Y * 0.7f, Paint.Steel, corner + new Vector3(0, y0 + ext.Y * 0.33f, 0), sides: 12);
                    Vector3 mid = (corner + new Vector3(x, 0, z) * r * 0.7f) * 0.5f + new Vector3(0, y0 + ext.Y * 0.6f, 0);
                    Vector3 run = new Vector3(x, 0, z) * r * 0.7f - corner;
                    k.Cyl(0.06f, run.Length(), Paint.Steel, mid, Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(run.Z, 0, -run.X)), MathF.PI / 2), sides: 10);
                }

                break;
            }

            case BlockKind.GasCell:
                k.Bevel(ext * 0.99f, Paint.Self, Vector3.Zero, MathF.Min(0.35f, ext.X * 0.12f));
                k.Box(new Vector3(ext.X * 1.005f, 0.04f, ext.Z * 1.005f), Paint.Dark, Vector3.Zero);
                break;

            default:
                k.Bevel(ext, Paint.Self, Vector3.Zero, bevel);
                break;
        }

        if (hints && def.Kind is BlockKind.Breech or BlockKind.ShipPropeller)
            k.GlowCyl(0.18f, 0.4f, Amber, 2f, new Vector3(0, ext.Y * 0.5f + 0.3f, -ext.Z * 0.3f), NoseZ, 0f);
    }
}
