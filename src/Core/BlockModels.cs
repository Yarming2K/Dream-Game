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
/// <summary>
/// The detailed meshes made in Blender (tools/blender/parts_library.py, exported to models/parts.glb) that block
/// models can use in place of their simple shapes.
/// </summary>
public static class PartLibrary
{
    /// <summary>Every mesh in the library. The Blender script makes exactly these.</summary>
    public static readonly string[] Names =
    {
        "tire", "rim", "hub", "roadwheel", "track_link", "muzzle_brake",
        "hatch", "periscope", "headlight", "toolbox", "smoke_tube", "muffler", "prop_blade",
        "jet_engine", "jet_nozzle", "rotor_blade", "breech", "ogive",
    };

    /// <summary>Marks detail that a library part already has, so it's left out when the library is there.</summary>
    public const string Covered = "-";
}

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

    /// <summary>
    /// A detailed mesh from the part library (made in Blender, see <see cref="PartLibrary"/>) to draw instead of the
    /// simple shape, fitted to its size; or <see cref="PartLibrary.Covered"/> for detail a library part already has.
    /// Without the library the simple shape is drawn.
    /// </summary>
    public string Model;

    /// <summary>
    /// The box a library mesh is fitted to, in the part's own frame, when it covers more than the part's simple
    /// shape (a whole propeller blade drawn in place of its inner half). Zero size: the simple shape's own size.
    /// </summary>
    public Vector3 FitSize, FitCenter;

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
    /// and gas cells, in any of their one-cell shapes, and big armor blocks (hull plates, belt armor), every cell
    /// of them, so a battleship's hull is one painted surface with grey decks rather than a patchwork of plates.
    /// </summary>
    public static bool IsSurface(BlockDef def) =>
        def.Kind == BlockKind.Armor || (!def.IsMultiCell && def.Kind is BlockKind.Frame or BlockKind.GasCell);

    /// <summary>The block whose surface material a shaped block uses (its plain version), so a slope and the plates beside it are one surface.</summary>
    public static BlockDef SurfaceDefOf(BlockDef def) => def.BaseId != null && def.BaseId != def.Id && BlockCatalog.TryGet(def.BaseId, out BlockDef b) ? b : def;

    /// <summary>How wide the chamfer is on a surface block's outer edges.</summary>
    public static float SurfaceBevel(BlockDef def) => def.Kind == BlockKind.GasCell ? 0.11f : 0.022f;

    private static readonly Dictionary<string, bool> TurnsCache = new();

    /// <summary>Whether a block has a part that turns round by itself all the time: a radar antenna on its "Spinner" node.</summary>
    public static bool Turns(BlockDef def)
    {
        if (def.Kind != BlockKind.Radar) return false;
        lock (TurnsCache)
        {
            if (!TurnsCache.TryGetValue(def.Id, out bool turns))
                TurnsCache[def.Id] = turns = For(def).Nodes.Exists(n => n.Name == "Spinner");
            return turns;
        }
    }

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

        /// <summary>Draws the last part with a mesh from the part library when there is one.</summary>
        public void Lib(string model)
        {
            ModelPart p = M.Parts[^1];
            p.Model = model;
            M.Parts[^1] = p;
        }

        /// <summary>Like <see cref="Lib(string)"/>, fitting the mesh to a box of its own (in the part's frame).</summary>
        public void Lib(string model, Vector3 fitSize, Vector3 fitCenter)
        {
            ModelPart p = M.Parts[^1];
            p.Model = model;
            p.FitSize = fitSize;
            p.FitCenter = fitCenter;
            M.Parts[^1] = p;
        }

        /// <summary>The last part is detail a library part already has: left out when the library is there.</summary>
        public void Covered() => Lib(PartLibrary.Covered);

        /// <summary>The last <paramref name="n"/> parts are covered by a library part.</summary>
        public void CoverLast(int n)
        {
            for (int i = M.Parts.Count - n; i < M.Parts.Count; i++)
            {
                ModelPart p = M.Parts[i];
                p.Model = PartLibrary.Covered;
                M.Parts[i] = p;
            }
        }

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

        /// <summary>
        /// A flat right-angled triangle (lying in XZ) <paramref name="size"/> across, thick and long: the right angle at
        /// its +X +Z corner, the long cut edge facing −X −Z. Made from the wedge, laid on its side.
        /// </summary>
        public void Triangle(Vector3 size, Paint p, Vector3 pos, Quaternion? rot = null) =>
            Add(Prim.Wedge, Vector3.One, 0, p,
                Matrix4x4.CreateScale(size.Y / Full, size.X / Full, size.Z / Full) * Matrix4x4.CreateFromQuaternion(Rot(Vector3.UnitZ, MathF.PI / 2)) * At(pos, rot));

        /// <summary>A round rod from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public void Rod(Vector3 a, Vector3 b, float r, Paint p, int sides = 10)
        {
            Vector3 d = b - a;
            float len = d.Length();
            if (len < 1e-4f) return;
            d /= len;
            Vector3 axis = Vector3.Cross(Vector3.UnitY, d);
            Quaternion q = axis.LengthSquared() < 1e-8f
                ? (d.Y > 0 ? Quaternion.Identity : Rot(Vector3.UnitX, MathF.PI))
                : Rot(axis, MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, d), -1f, 1f)));
            Cyl(r, len, p, (a + b) * 0.5f, q, sides: sides);
        }

        /// <summary>
        /// A pointed nose (a tangent ogive, the shape of a shell's or a radome's nose) from a round base of radius
        /// <paramref name="r"/> centered on <paramref name="baseAt"/>, running <paramref name="len"/> forward (−Z) to a point.
        /// </summary>
        public void Ogive(float r, float len, Paint p, Vector3 baseAt, int steps = 6, int sides = 24)
        {
            // One smooth library nose; without the library, a stack of cones.
            Cyl(r, len, p, baseAt + new Vector3(0, 0, -len * 0.5f), NoseZ, 0f, sides);
            Lib("ogive");
            float rho = (r * r + len * len) / (2f * r);
            float RadiusAt(float fromTip) => MathF.Max(0f, MathF.Sqrt(MathF.Max(0f, rho * rho - (len - fromTip) * (len - fromTip))) + r - rho);
            for (int i = 0; i < steps; i++)
            {
                // Shorter steps toward the tip, where the curve turns fastest.
                float t0 = (float)i / steps, t1 = (float)(i + 1) / steps;
                float x0 = len * (1f - t0 * t0 * 0.35f - t0 * 0.65f), x1 = len * (1f - t1 * t1 * 0.35f - t1 * 0.65f);
                float h = x0 - x1;
                Cyl(RadiusAt(x0), h, p, baseAt + new Vector3(0, 0, -(len - (x0 + x1) * 0.5f)), NoseZ, RadiusAt(x1), sides);
                Covered();
            }
        }

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

            case BlockKind.Wheel when def.Retracts:
            case BlockKind.SteerWheel when def.Retracts:
                LandingGear(k, def);
                break;

            case BlockKind.Wheel:
            case BlockKind.SteerWheel:
            {
                // Axle along X. A treaded tire, a rim, and a bolted hub.
                float r = def.WheelRadiusM;
                k.Cyl(r * 0.98f, 0.28f, Paint.Rubber, Vector3.Zero, AlongX, sides: 28);
                k.Lib("tire");
                for (int i = 0; i < 14; i++)
                {
                    float a = i * MathF.Tau / 14;
                    var at = new Vector3(0, MathF.Cos(a) * r * 0.95f, MathF.Sin(a) * r * 0.95f);
                    k.Box(new Vector3(0.29f, r * 0.12f, r * 0.24f), Paint.Rubber, at, Rot(Vector3.UnitX, a));
                    k.Covered();
                }

                Paint rim = def.Kind == BlockKind.SteerWheel ? Paint.Brass : Paint.Self;
                k.Cyl(r * 0.6f, 0.3f, rim, Vector3.Zero, AlongX, sides: 24);
                k.Lib("rim");
                k.Cyl(r * 0.26f, 0.34f, Paint.Dark, Vector3.Zero, AlongX, sides: 12);
                k.Lib("hub");
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

            case BlockKind.Breech when def.Breech.HighArc:
                // A heavy mortar: a baseplate, a short thick breech in a cradle with recoil buffers, and an elevating screw.
                k.Bevel(new Vector3(Full, 0.08f, Full), Paint.SelfDark, new Vector3(0, -0.21f, 0), 0.015f);
                k.Cyl(0.16f, 0.36f, Paint.Self, new Vector3(0, 0.02f, 0.02f), AlongZ, sides: 22);
                for (int i = 0; i < 3; i++) k.Cyl(0.175f, 0.03f, Paint.SelfDark, new Vector3(0, 0.02f, -0.1f + i * 0.1f), AlongZ, sides: 22);
                foreach (float x in new[] { -0.2f, 0.2f })
                {
                    k.Box(new Vector3(0.05f, 0.3f, 0.3f), Paint.Self, new Vector3(x, -0.04f, 0.02f));
                    k.Cyl(0.03f, 0.3f, Paint.Steel, new Vector3(x * 0.8f, 0.13f, 0.02f), AlongZ, sides: 10);
                }

                k.Cyl(0.03f, 0.22f, Paint.Steel, new Vector3(0, -0.1f, 0.2f), sides: 10);
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
                k.Lib("breech");
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
                k.Lib("muzzle_brake");
                k.Cyl(br * 1.78f, 0.04f, Paint.SelfDark, new Vector3(0, 0, -0.18f), AlongZ, sides: 24);
                k.Covered();
                k.Box(new Vector3(br * 4f, 0.06f, 0.07f), Paint.Dark, new Vector3(0, 0, -0.06f));
                k.Covered();
                k.Box(new Vector3(br * 4f, 0.06f, 0.07f), Paint.Dark, new Vector3(0, 0, 0.05f));
                k.Covered();
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

            case BlockKind.Wing when def.LiftArea < 1f:
            {
                // Half a panel, cut corner to corner, with a darker leading edge along the cut.
                k.Triangle(new Vector3(Full, 0.06f, Full), Paint.Self, Vector3.Zero);
                Quaternion along = Rot(Vector3.UnitY, MathF.PI / 4);
                k.Box(new Vector3(Full * 1.38f, 0.065f, 0.03f), Paint.SelfDark, new Vector3(0.012f, 0, 0.012f), along);
                break;
            }

            case BlockKind.Fieldwork:
                Fieldwork(k, def);
                break;

            case BlockKind.ShipFitting:
                ShipFitting(k, def);
                break;

            case BlockKind.Fairing:
                // A nose cone: a pointed ogive from a round base that fills the back face, and a seam ring at the base.
                k.Ogive(H * 0.99f, Full * 0.98f, Paint.Self, new Vector3(0, 0, H * 0.98f), 6, 24);
                k.Cyl(H * 0.995f, 0.03f, Paint.SelfDark, new Vector3(0, 0, H * 0.95f), AlongZ, sides: 24);
                break;

            case BlockKind.Wing:
                k.Box(new Vector3(Full, 0.06f, Full), Paint.Self, Vector3.Zero);
                foreach (float x in new[] { -0.243f, 0.243f }) k.Box(new Vector3(0.012f, 0.066f, Full * 0.98f), Paint.SelfDark, new Vector3(x, 0, 0));
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
                    // One whole twisted blade from the library, in place of the two halves.
                    k.Slab(new Vector3(0.17f, rr * 0.5f, 0.035f), 0.78f, 0.85f, Paint.Self, Vector3.Transform(new Vector3(0, rr * 0.27f, 0), q), q * Rot(Vector3.UnitY, 0.32f));
                    k.Lib("prop_blade", new Vector3(0.17f, rr * 0.98f, 0.035f), new Vector3(0, rr * 0.24f, 0));
                    k.Slab(new Vector3(0.133f, rr * 0.48f, 0.03f), 0.6f, 0.8f, Paint.Self, Vector3.Transform(new Vector3(0, rr * 0.74f, 0), q), q * Rot(Vector3.UnitY, 0.2f));
                    k.Covered();
                    k.Box(new Vector3(0.075f, 0.08f, 0.026f), Paint.Brass, Vector3.Transform(new Vector3(0, rr * 0.97f, 0), q), q * Rot(Vector3.UnitY, 0.2f));
                }

                k.Node = -1;
                break;
            }

            case BlockKind.JetEngine:
                k.Cyl(0.24f, Full * 0.9f, Paint.Self, new Vector3(0, 0, 0.01f), AlongZ, sides: 24);
                k.Lib("jet_engine");
                k.Torus(0.215f, 0.035f, Paint.Steel, new Vector3(0, 0, -0.23f), AlongZ);
                k.Cyl(0.2f, 0.02f, Paint.Dark, new Vector3(0, 0, -0.215f), AlongZ, sides: 24);
                k.Cyl(0.06f, 0.08f, Paint.Steel, new Vector3(0, 0, -0.24f), NoseZ, 0.01f, 12);
                k.Cyl(0.19f, 0.12f, Paint.Dark, new Vector3(0, 0, 0.29f), TailZ, 0.15f, 20);
                k.Lib("jet_nozzle");
                k.GlowCyl(0.12f, 0.02f, new Vector3(1f, 0.55f, 0.2f), 2.5f, new Vector3(0, 0, 0.33f), AlongZ);
                // The exhaust flame, a meter long at full throttle (stretched further on afterburner).
                k.Node = k.AddNode("Flame", new Vector3(0, 0, 0.34f));
                k.GlowCyl(0.13f, 1f, new Vector3(1f, 0.5f, 0.18f), 2.2f, new Vector3(0, 0, 0.5f), TailZ, 0.015f);
                k.GlowCyl(0.075f, 0.55f, new Vector3(0.65f, 0.8f, 1f), 3f, new Vector3(0, 0, 0.275f), TailZ, 0.01f);
                k.Node = -1;
                break;

            // Jets built from parts: casings sized to meet end to end, so a line of them reads as one engine. The
            // arrow side (−Z) is the front, where the air comes in.
            case BlockKind.JetIntake when def.HighSpeed:
                // A square intake with its ramp: a wedge on top that sets the shock wave at speed.
                k.Bevel(new Vector3(0.46f, 0.44f, Full), Paint.Self, Vector3.Zero, 0.015f);
                k.Box(new Vector3(0.38f, 0.3f, 0.02f), Paint.Dark, new Vector3(0, -0.03f, -Full * 0.5f - 0.002f));
                k.Box(new Vector3(0.4f, 0.03f, 0.3f), Paint.SelfDark, new Vector3(0, 0.13f, -0.14f), Rot(Vector3.UnitX, 0.22f));
                k.Box(new Vector3(0.02f, 0.36f, 0.06f), Paint.Steel, new Vector3(-0.21f, -0.02f, -0.24f));
                k.Box(new Vector3(0.02f, 0.36f, 0.06f), Paint.Steel, new Vector3(0.21f, -0.02f, -0.24f));
                break;

            case BlockKind.JetIntake:
                k.Cyl(0.235f, Full * 0.92f, Paint.Self, new Vector3(0, 0, 0.02f), AlongZ, sides: 24);
                k.Torus(0.215f, 0.035f, Paint.Steel, new Vector3(0, 0, -0.23f), AlongZ);
                k.Cyl(0.2f, 0.02f, Paint.Dark, new Vector3(0, 0, -0.215f), AlongZ, sides: 24);
                k.Cyl(0.06f, 0.08f, Paint.Steel, new Vector3(0, 0, -0.24f), NoseZ, 0.01f, 12);
                break;

            case BlockKind.JetFan:
                // The fan's casing is the fattest part of a turbofan: a bypass duct round the core.
                k.Cyl(0.248f, Full, Paint.Self, Vector3.Zero, AlongZ, sides: 28);
                k.Cyl(0.252f, 0.04f, Paint.SelfDark, new Vector3(0, 0, -0.18f), AlongZ, sides: 28);
                k.Cyl(0.252f, 0.04f, Paint.SelfDark, new Vector3(0, 0, 0.18f), AlongZ, sides: 28);
                k.Box(new Vector3(0.1f, 0.05f, 0.3f), Paint.SelfDark, new Vector3(0, -0.255f, 0));
                break;

            case BlockKind.JetCompressor:
                k.Cyl(0.232f, Full, Paint.Self, Vector3.Zero, AlongZ, sides: 24);
                k.Cyl(0.242f, 0.025f, Paint.Dark, new Vector3(0, 0, -0.12f), AlongZ, sides: 24);
                k.Cyl(0.242f, 0.025f, Paint.Dark, new Vector3(0, 0, 0.12f), AlongZ, sides: 24);
                k.Box(new Vector3(0.07f, 0.04f, 0.08f), Paint.Steel, new Vector3(0, 0.245f, 0));
                break;

            case BlockKind.JetCore:
                // The combustion section: a fuel manifold ring with pipes running to the burners, and an igniter box.
                k.Cyl(0.236f, Full, Paint.Self, Vector3.Zero, AlongZ, sides: 24);
                k.Torus(0.25f, 0.014f, Paint.Brass, new Vector3(0, 0, -0.06f), AlongZ);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI / 4f;
                    k.Box(new Vector3(0.018f, 0.018f, 0.14f), Paint.Brass, new Vector3(MathF.Cos(a) * 0.244f, MathF.Sin(a) * 0.244f, 0.02f));
                }

                k.Bevel(new Vector3(0.1f, 0.06f, 0.12f), Paint.SelfDark, new Vector3(0.12f, -0.22f, 0.1f), 0.01f);
                break;

            case BlockKind.JetTurbine:
                // Hotter: the casing's darkened, with heavier bands.
                k.Cyl(0.228f, Full, Paint.SelfDark, Vector3.Zero, AlongZ, sides: 24);
                k.Cyl(0.24f, 0.04f, Paint.Steel, new Vector3(0, 0, -0.15f), AlongZ, sides: 24);
                k.Cyl(0.24f, 0.04f, Paint.Steel, new Vector3(0, 0, 0.15f), AlongZ, sides: 24);
                break;

            case BlockKind.JetAfterburner:
                // A long hot pipe: bare heat-stained metal with cooling rings.
                k.Cyl(0.222f, Full, Paint.Dark, Vector3.Zero, AlongZ, sides: 24);
                foreach (float z in new[] { -0.16f, 0f, 0.16f })
                    k.Cyl(0.23f, 0.02f, Paint.Steel, new Vector3(0, 0, z), AlongZ, sides: 24);
                break;

            case BlockKind.JetNozzle when def.Id == "jet_nozzle_large":
            {
                // A full-size nozzle: a casing ring as wide as the engine bay, the petals converging behind it
                // (they open for the afterburner), seams between the petals, and a flame twice as wide.
                k.Cyl(0.5f, 0.3f, Paint.SelfDark, new Vector3(0, 0, -0.1f), AlongZ, sides: 32);
                k.Node = k.AddNode("Petals", new Vector3(0, 0, 0.15f));
                k.Cyl(0.47f, 0.44f, Paint.Dark, new Vector3(0, 0, 0.07f), TailZ, 0.38f, 32);
                k.Lib("jet_nozzle");
                k.Node = -1;
                for (int i = 0; i < 12; i++)
                {
                    float a = i * MathF.PI / 6f + 0.13f;
                    k.Box(new Vector3(0.03f, 0.03f, 0.42f), Paint.Steel, new Vector3(MathF.Cos(a) * 0.45f, MathF.Sin(a) * 0.45f, 0.12f));
                }

                k.GlowCyl(0.26f, 0.02f, new Vector3(1f, 0.55f, 0.2f), 2.5f, new Vector3(0, 0, 0.3f), AlongZ);
                k.Node = k.AddNode("Flame", new Vector3(0, 0, 0.32f));
                k.GlowCyl(0.3f, 1f, new Vector3(1f, 0.5f, 0.18f), 2.2f, new Vector3(0, 0, 0.5f), TailZ, 0.03f);
                k.GlowCyl(0.17f, 0.55f, new Vector3(0.65f, 0.8f, 1f), 3f, new Vector3(0, 0, 0.275f), TailZ, 0.02f);
                k.Node = -1;
                break;
            }

            case BlockKind.JetNozzle:
            {
                k.Cyl(0.225f, 0.3f, Paint.Self, new Vector3(0, 0, -0.1f), AlongZ, sides: 24);
                // A variable nozzle's petals sit on a node of their own, so they can open for the afterburner.
                if (def.HighSpeed) k.Node = k.AddNode("Petals", new Vector3(0, 0, 0.15f));
                Vector3 at = def.HighSpeed ? Vector3.Zero : new Vector3(0, 0, 0.15f);
                k.Cyl(0.19f, 0.2f, Paint.Dark, at, TailZ, 0.15f, 20);
                k.Lib("jet_nozzle");
                k.Node = -1;
                if (def.HighSpeed)
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * MathF.PI / 3f + 0.3f;
                        k.Box(new Vector3(0.02f, 0.02f, 0.2f), Paint.Steel, new Vector3(MathF.Cos(a) * 0.205f, MathF.Sin(a) * 0.205f, 0.1f));
                    }

                k.GlowCyl(0.12f, 0.02f, new Vector3(1f, 0.55f, 0.2f), 2.5f, new Vector3(0, 0, 0.24f), AlongZ);
                k.Node = k.AddNode("Flame", new Vector3(0, 0, 0.25f));
                k.GlowCyl(0.13f, 1f, new Vector3(1f, 0.5f, 0.18f), 2.2f, new Vector3(0, 0, 0.5f), TailZ, 0.015f);
                k.GlowCyl(0.075f, 0.55f, new Vector3(0.65f, 0.8f, 1f), 3f, new Vector3(0, 0, 0.275f), TailZ, 0.01f);
                k.Node = -1;
                break;
            }

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
                    // One whole blade from the library in place of the two halves.
                    k.Box(new Vector3(0.34f, 0.05f, rr * 0.5f), Paint.Self, Vector3.Transform(new Vector3(0, 0, rr * 0.27f), q), q);
                    k.Lib("rotor_blade", new Vector3(0.34f, 0.05f, rr * 0.98f), new Vector3(0, 0, rr * 0.24f));
                    k.Box(new Vector3(0.3f, 0.045f, rr * 0.46f), Paint.Self, Vector3.Transform(new Vector3(0, 0, rr * 0.75f), q), q);
                    k.Covered();
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

            case BlockKind.Stabilizer:
                // A hinge bracket on the hull and a hydraulic leg with a spade; the leg swings down when braced.
                k.Bevel(new Vector3(0.36f, 0.2f, 0.3f), Paint.Self, new Vector3(0, 0.1f, 0), 0.015f);
                k.Cyl(0.05f, 0.42f, Paint.Steel, new Vector3(0, 0.05f, 0), AlongX, sides: 12);
                k.Node = k.AddNode("Leg", new Vector3(0, 0.05f, 0));
                k.Box(new Vector3(0.14f, 0.1f, 0.42f), Paint.SelfDark, new Vector3(0, 0, -0.2f));
                k.Cyl(0.035f, 0.36f, Paint.Steel, new Vector3(0, 0.07f, -0.18f), AlongZ, sides: 10);
                k.Box(new Vector3(0.3f, 0.04f, 0.16f), Paint.Dark, new Vector3(0, -0.05f, -0.42f));
                k.Node = -1;
                break;

            case BlockKind.Foundation:
                // A concrete footing: a block with a lip round its top and the ends of its reinforcing bars.
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero, 0.012f);
                k.Box(new Vector3(Full * 1.01f, 0.03f, Full * 1.01f), Paint.SelfDark, new Vector3(0, H - 0.04f, 0));
                break;

            case BlockKind.FirstAid:
                // A white cabinet with a red cross, on the back face.
                k.Bevel(new Vector3(0.34f, 0.3f, 0.12f), Paint.Self, new Vector3(0, 0.02f, H - 0.06f), 0.012f);
                k.Box(new Vector3(0.14f, 0.04f, 0.006f), Paint.Red, new Vector3(0, 0.04f, H - 0.123f));
                k.Box(new Vector3(0.04f, 0.14f, 0.006f), Paint.Red, new Vector3(0, 0.04f, H - 0.123f));
                k.Box(new Vector3(0.04f, 0.02f, 0.02f), Paint.Steel, new Vector3(0.12f, -0.06f, H - 0.13f));
                break;

            case BlockKind.Stairs:
                // Four treads up the slope (rising toward the back), with stringers down both sides.
                for (int i = 0; i < 4; i++)
                {
                    float z0 = -H + i * 0.125f, top = -H + (i + 0.5f) * 0.125f + 0.0625f;
                    k.Box(new Vector3(Full * 0.9f, top + H, 0.125f), Paint.Self, new Vector3(0, (top - H) * 0.5f, z0 + 0.0625f));
                    k.Box(new Vector3(Full * 0.9f, 0.012f, 0.02f), Paint.Steel, new Vector3(0, top, z0 + 0.012f));
                }

                foreach (float x in new[] { -H + 0.012f, H - 0.012f })
                    k.Box(new Vector3(0.024f, 0.08f, Full * 1.4f), Paint.SelfDark, new Vector3(x, -0.02f, 0), Rot(Vector3.UnitX, MathF.PI / 4));
                break;

            case BlockKind.Lamp:
                // A cage round a glowing bulb, hanging off the back face (down, unrotated: a ceiling light).
                k.Cyl(0.07f, 0.02f, Paint.Self, new Vector3(0, H - 0.01f, 0), sides: 14);
                k.Cyl(0.015f, 0.05f, Paint.Dark, new Vector3(0, H - 0.045f, 0), sides: 8);
                k.Sphere(0.045f, Paint.Glow, new Vector3(0, H - 0.1f, 0));
                k.M.Parts[^1] = k.M.Parts[^1] with { GlowColor = new Vector3(1f, 0.86f, 0.62f), GlowEnergy = 3f };
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.PI / 4;
                    k.Box(new Vector3(0.11f, 0.11f, 0.006f), Paint.Self, new Vector3(0, H - 0.1f, 0), Rot(Vector3.UnitY, a));
                }

                break;

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
                // The top-attack seeker's mark: a red band, and small canard fins for the climb and dive.
                if (def.Seeker == SeekerKind.TopAttack)
                {
                    k.Cyl(0.134f, 0.03f, Paint.Red, new Vector3(0, 0, 0.04f), AlongZ, sides: 24);
                    k.Box(new Vector3(0.36f, 0.015f, 0.06f), Paint.Dark, new Vector3(0, 0, 0.14f));
                    k.Box(new Vector3(0.015f, 0.36f, 0.06f), Paint.Dark, new Vector3(0, 0, 0.14f));
                }

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
                k.Covered();
                k.Cyl(0.17f, 0.04f, Paint.Self, new Vector3(0, b + 0.05f, 0), sides: 24);
                k.Lib("hatch", new Vector3(0.38f, 0.085f, 0.395f), new Vector3(0, -0.0075f, 0.0075f));
                k.Cyl(0.05f, 0.02f, Paint.SelfDark, new Vector3(0, b + 0.075f, 0), sides: 12);
                k.Covered();
                k.Cyl(0.025f, 0.14f, Paint.Steel, new Vector3(0, b + 0.05f, 0.18f), AlongX, sides: 10);
                k.Covered();
                k.Box(new Vector3(0.08f, 0.02f, 0.015f), Paint.Steel, new Vector3(0, b + 0.078f, -0.1f));
                k.Covered();
                k.Bolts(8, 0.18f, b + 0.032f, 0.01f, 0.02f, Paint.Steel);
                k.CoverLast(8);
                break;
            case "periscope":
                k.Bevel(new Vector3(0.2f, 0.06f, 0.16f), Paint.SelfDark, new Vector3(0, b + 0.03f, 0), 0.01f);
                k.Covered();
                k.Bevel(new Vector3(0.15f, 0.1f, 0.12f), Paint.Self, new Vector3(0, b + 0.11f, 0), 0.01f);
                k.Lib("periscope", new Vector3(0.2f, 0.171f, 0.175f), new Vector3(0, -0.0245f, -0.0075f));
                k.Box(new Vector3(0.11f, 0.045f, 0.01f), Paint.Glass, new Vector3(0, b + 0.115f, -0.062f));
                k.Box(new Vector3(0.16f, 0.012f, 0.05f), Paint.SelfDark, new Vector3(0, b + 0.165f, -0.07f));
                k.Covered();
                break;
            case "headlight":
                k.Box(new Vector3(0.04f, 0.12f, 0.04f), Paint.Dark, new Vector3(0, b + 0.06f, 0.04f));
                k.Cyl(0.075f, 0.12f, Paint.Self, new Vector3(0, b + 0.15f, 0), AlongZ, sides: 16);
                k.Lib("headlight");
                k.GlowCyl(0.06f, 0.01f, Lamp, 1.2f, new Vector3(0, b + 0.15f, -0.062f), AlongZ);
                k.Torus(0.08f, 0.008f, Paint.Dark, new Vector3(0, b + 0.15f, -0.075f), AlongZ);
                k.Covered();
                k.Box(new Vector3(0.012f, 0.16f, 0.012f), Paint.Dark, new Vector3(0, b + 0.15f, -0.078f));
                k.Box(new Vector3(0.16f, 0.012f, 0.012f), Paint.Dark, new Vector3(0, b + 0.15f, -0.078f));
                break;
            case "toolbox":
                k.Bevel(new Vector3(0.42f, 0.16f, 0.22f), Paint.Self, new Vector3(0, b + 0.08f, 0), 0.012f);
                k.Lib("toolbox");
                k.Box(new Vector3(0.43f, 0.012f, 0.225f), Paint.SelfDark, new Vector3(0, b + 0.125f, 0));
                k.Covered();
                foreach (float x in new[] { -0.12f, 0.12f }) k.Box(new Vector3(0.03f, 0.05f, 0.012f), Paint.Steel, new Vector3(x, b + 0.11f, -0.113f));
                foreach (float x in new[] { -0.17f, 0.17f })
                {
                    k.Box(new Vector3(0.02f, 0.165f, 0.226f), Paint.SelfDark, new Vector3(x, b + 0.08f, 0));
                    k.Covered();
                }
                break;
            case "spare_tracks":
                k.Box(new Vector3(0.46f, 0.02f, 0.03f), Paint.Dark, new Vector3(0, b + 0.045f, 0));
                for (int i = -1; i <= 1; i++)
                {
                    k.Box(new Vector3(0.42f, 0.035f, 0.12f), Paint.Self, new Vector3(0, b + 0.018f, i * 0.14f));
                    k.Lib("track_link");
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
                    k.Lib("smoke_tube");
                    k.Cyl(0.043f, 0.015f, Paint.Dark, at + Vector3.Transform(new Vector3(0, 0.1f, 0), up), up, sides: 12);
                    k.Covered();
                }

                break;
            }

            case "countermeasures":
            {
                // A low box of square cartridge cells, angled up and back.
                k.Bevel(new Vector3(0.3f, 0.05f, 0.24f), Paint.SelfDark, new Vector3(0, b + 0.025f, 0), 0.01f);
                Quaternion tilt = Rot(Vector3.UnitX, 0.5f);
                k.Bevel(new Vector3(0.26f, 0.12f, 0.16f), Paint.Self, new Vector3(0, b + 0.11f, 0.01f), 0.01f, tilt);
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 2; j++)
                    k.Box(new Vector3(0.06f, 0.06f, 0.02f), Paint.Dark,
                        new Vector3(-0.08f + i * 0.08f, b + 0.1f + j * 0.06f * MathF.Cos(0.5f), 0.1f - j * 0.06f * MathF.Sin(0.5f)), tilt);
                break;
            }

            case "exhaust":
                k.Cyl(0.08f, 0.36f, Paint.Dark, new Vector3(0, b + 0.1f, 0), AlongX, sides: 16);
                k.Lib("muffler");
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

    /// <summary>
    /// Landing gear: a narrow tyre and rim (axle along X), and on its own "Strut" node the oleo leg rising to the
    /// top of the cell. The game keeps the leg upright while the wheel turns, stretches it with the suspension
    /// and folds the whole gear away in flight.
    /// </summary>
    private static void LandingGear(Maker k, BlockDef def)
    {
        float r = def.WheelRadiusM;
        k.Cyl(r * 0.98f, 0.17f, Paint.Rubber, Vector3.Zero, AlongX, sides: 28);
        k.Lib("tire");
        k.Cyl(r * 0.62f, 0.19f, Paint.Self, Vector3.Zero, AlongX, sides: 24);
        k.Lib("rim");
        k.Cyl(r * 0.25f, 0.21f, Paint.Dark, Vector3.Zero, AlongX, sides: 12);
        k.Lib("hub");

        bool nose = def.Kind == BlockKind.SteerWheel;
        k.Node = k.AddNode("Strut", Vector3.Zero);
        // A fork round the nose wheel; a single leg beside a main wheel.
        if (nose)
        {
            foreach (float x in new[] { -0.12f, 0.12f }) k.Box(new Vector3(0.03f, r * 0.9f, 0.06f), Paint.Steel, new Vector3(x, r * 0.45f, 0));
            k.Box(new Vector3(0.27f, 0.04f, 0.07f), Paint.Steel, new Vector3(0, r * 0.9f, 0));
            k.Cyl(0.03f, H - r * 0.9f, Paint.Steel, new Vector3(0, (H + r * 0.9f) * 0.5f, 0), sides: 10);
        }
        else
        {
            k.Cyl(0.03f, 0.3f, Paint.Steel, new Vector3(0, 0, 0), AlongX, sides: 10);
            k.Cyl(0.03f, H, Paint.Steel, new Vector3(0.15f, H * 0.5f, 0), sides: 10);
        }

        float legX = nose ? 0 : 0.15f;
        k.Cyl(0.045f, H * 0.45f, Paint.Self, new Vector3(legX, H * 0.78f, 0), sides: 12);
        k.Box(new Vector3(0.025f, H * 0.35f, 0.05f), Paint.Dark, new Vector3(legX, H * 0.4f, 0.06f), Rot(Vector3.UnitX, 0.35f));
        k.Node = -1;
    }

    /// <summary>A small pseudo-random number (0 to 1) from a seed, so hand-placed clutter looks irregular but never changes.</summary>
    private static float Jitter(int seed)
    {
        uint h = unchecked((uint)seed * 2654435761u);
        h ^= h >> 15;
        h = unchecked(h * 2246822519u);
        h ^= h >> 13;
        return (h & 1023u) / 1023f;
    }

    /// <summary>Single-cell ship fittings: life raft canisters, an anchor, bollards.</summary>
    private static void ShipFitting(Maker k, BlockDef def)
    {
        switch (def.Id)
        {
            case "liferaft":
                // Two white canisters lying in a cradle, each with its strapping bands.
                foreach (float z in new[] { -0.12f, 0.12f })
                {
                    k.Cyl(0.11f, 0.44f, Paint.Self, new Vector3(0, -0.1f, z), AlongX, sides: 16);
                    foreach (float x in new[] { -0.12f, 0.12f }) k.Cyl(0.113f, 0.02f, Paint.Dark, new Vector3(x, -0.1f, z), AlongX, sides: 16);
                }

                foreach (float x in new[] { -0.17f, 0.17f }) k.Box(new Vector3(0.03f, 0.08f, 0.46f), Paint.Steel, new Vector3(x, -0.2f, 0));
                break;

            case "anchor":
                // Seen against the hull side: the hawse pipe's lip, the shank hanging from it, and the two flukes.
                k.Torus(0.13f, 0.035f, Paint.Self, new Vector3(0, -0.2f, -0.08f));
                k.Box(new Vector3(0.07f, 0.06f, 0.34f), Paint.Self, new Vector3(0, -0.18f, 0.06f));
                foreach (int s in new[] { -1, 1 })
                    k.Box(new Vector3(0.18f, 0.07f, 0.06f), Paint.Self, new Vector3(s * 0.08f, -0.18f, 0.2f), Rot(Vector3.UnitY, s * 0.5f));
                k.Box(new Vector3(0.26f, 0.05f, 0.05f), Paint.Self, new Vector3(0, -0.19f, 0.23f));
                break;

            case "bollard":
                k.Box(new Vector3(0.42f, 0.03f, 0.2f), Paint.Self, new Vector3(0, -H + 0.015f, 0));
                foreach (float x in new[] { -0.11f, 0.11f })
                {
                    k.Cyl(0.06f, 0.17f, Paint.Self, new Vector3(x, -H + 0.115f, 0), sides: 14);
                    k.Cyl(0.075f, 0.025f, Paint.Self, new Vector3(x, -H + 0.21f, 0), sides: 14);
                }

                break;

            default:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                break;
        }
    }

    /// <summary>Big ship fittings: a funnel, masts, a ship's boat on its davit, the anchor windlass.</summary>
    private static void ShipFittingBig(Maker k, BlockDef def, Vector3 ext)
    {
        switch (def.Id)
        {
            case "funnel":
            {
                // A rounded casing raked back, louvres down its sides, a dark cap and the uptakes poking out.
                Quaternion rake = Rot(Vector3.UnitX, 0.12f);
                var mid = new Vector3(0, -0.05f, 0.05f);
                Vector3 At(Vector3 local) => mid + Vector3.Transform(local, rake);
                k.Bevel(new Vector3(ext.X * 0.82f, ext.Y * 0.92f, ext.Z * 0.82f), Paint.Self, mid, 0.42f, rake);
                foreach (int s in new[] { -1, 1 })
                    for (int i = 0; i < 5; i++)
                        k.Box(new Vector3(0.025f, 0.045f, ext.Z * 0.45f), Paint.Dark, At(new Vector3(s * ext.X * 0.41f, -0.6f + i * 0.14f, 0)), rake);
                k.Bevel(new Vector3(ext.X * 0.86f, 0.24f, ext.Z * 0.86f), Paint.Dark, At(new Vector3(0, ext.Y * 0.43f, 0)), 0.4f, rake);
                foreach ((float z, float r) in new[] { (-0.45f, 0.17f), (0.05f, 0.2f), (0.5f, 0.13f) })
                    k.Cyl(r, 0.4f, Paint.Steel, At(new Vector3(0, ext.Y * 0.47f + 0.08f, z)), rake, sides: 14);
                k.Bevel(new Vector3(ext.X * 0.95f, 0.1f, ext.Z * 0.95f), Paint.SelfDark, new Vector3(0, -ext.Y * 0.5f + 0.05f, 0), 0.04f);
                break;
            }

            case "mast_lattice":
            {
                // Four legs tapering in, braced in a zigzag on every face, two platforms, a yardarm with whip
                // antennas, a pole on top, and navigation lights: white at the top, red to port and green to starboard.
                float bottom = -ext.Y * 0.5f, top = ext.Y * 0.36f;
                Vector3 Leg(int sx, int sz, float t) => new(sx * MathF.Max(0.15f, 0.65f - 0.5f * t), bottom + t * (top - bottom), sz * MathF.Max(0.15f, 0.65f - 0.5f * t));
                foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    k.Rod(Leg(sx, sz, 0), Leg(sx, sz, 1), 0.045f, Paint.Self, 8);
                for (int i = 0; i < 6; i++)
                {
                    float t0 = i / 6f, t1 = (i + 1) / 6f;
                    foreach ((int ax, int az, int bx, int bz) in new[] { (-1, -1, 1, -1), (1, -1, 1, 1), (1, 1, -1, 1), (-1, 1, -1, -1) })
                        k.Rod(Leg(ax, az, i % 2 == 0 ? t0 : t1), Leg(bx, bz, i % 2 == 0 ? t1 : t0), 0.018f, Paint.Self, 6);
                }

                k.Box(new Vector3(1.05f, 0.05f, 1.05f), Paint.SelfDark, new Vector3(0, bottom + 0.42f * (top - bottom), 0));
                k.Box(new Vector3(0.75f, 0.05f, 0.75f), Paint.SelfDark, new Vector3(0, bottom + 0.85f * (top - bottom), 0));
                k.Cyl(0.03f, ext.X * 1.1f, Paint.Steel, new Vector3(0, top - 0.3f, 0), AlongX, sides: 8);
                foreach (int s in new[] { -1, 1 })
                {
                    k.Cyl(0.008f, 0.7f, Paint.Steel, new Vector3(s * ext.X * 0.5f, top + 0.05f, 0), sides: 6);
                    k.GlowCyl(0.035f, 0.06f, s < 0 ? new Vector3(1f, 0.12f, 0.08f) : new Vector3(0.15f, 1f, 0.3f), 2f, new Vector3(s * 0.3f, top - 0.24f, 0));
                }

                k.Cyl(0.035f, ext.Y * 0.5f - top, Paint.Steel, new Vector3(0, (top + ext.Y * 0.5f) * 0.5f, 0), sides: 8);
                k.GlowCyl(0.04f, 0.07f, new Vector3(1f, 0.97f, 0.85f), 2.2f, new Vector3(0, ext.Y * 0.5f - 0.05f, 0));
                break;
            }

            case "mast_pole":
            {
                float bottom = -ext.Y * 0.5f;
                k.Cyl(0.08f, 0.06f, Paint.SelfDark, new Vector3(0, bottom + 0.03f, 0), sides: 12);
                k.Cyl(0.055f, ext.Y * 0.95f, Paint.Self, new Vector3(0, -ext.Y * 0.025f, 0), top: 0.035f, sides: 10);
                k.Cyl(0.025f, 1.2f, Paint.Steel, new Vector3(0, ext.Y * 0.3f, 0), AlongX, sides: 8);
                k.GlowCyl(0.035f, 0.06f, new Vector3(1f, 0.97f, 0.85f), 2.2f, new Vector3(0, ext.Y * 0.5f - 0.04f, 0));
                break;
            }

            case "ships_boat":
            {
                // A rigid inflatable boat in its chocks: grey hull with a pointed bow, dark inflated tubes, a
                // console with a windscreen, an outboard motor; the davit arm over it.
                float floor = -ext.Y * 0.5f;
                foreach (float z in new[] { -1.2f, 0f, 1.2f }) k.Box(new Vector3(0.9f, 0.18f, 0.12f), Paint.Steel, new Vector3(0, floor + 0.09f, z));
                k.Bevel(new Vector3(0.9f, 0.32f, 2.9f), Paint.SelfDark, new Vector3(0, floor + 0.36f, 0.3f), 0.08f);
                k.Slab(new Vector3(0.9f, 0.9f, 0.32f), 0.15f, 0.55f, Paint.SelfDark, new Vector3(0, floor + 0.36f, -1.6f), NoseZ);
                foreach (int s in new[] { -1, 1 })
                {
                    k.Cyl(0.16f, 2.9f, Paint.Rubber, new Vector3(s * 0.46f, floor + 0.52f, 0.3f), AlongZ, sides: 14);
                    k.Rod(new Vector3(s * 0.46f, floor + 0.52f, -1.15f), new Vector3(s * 0.08f, floor + 0.52f, -2.0f), 0.15f, Paint.Rubber, 14);
                    k.Sphere(0.16f, Paint.Rubber, new Vector3(s * 0.46f, floor + 0.52f, 1.75f));
                }

                k.Bevel(new Vector3(0.38f, 0.42f, 0.32f), Paint.Self, new Vector3(0, floor + 0.72f, 0.25f), 0.04f);
                k.Box(new Vector3(0.4f, 0.22f, 0.02f), Paint.Glass, new Vector3(0, floor + 1.0f, 0.08f), Rot(Vector3.UnitX, -0.4f));
                k.Bevel(new Vector3(0.24f, 0.55f, 0.26f), Paint.Dark, new Vector3(0, floor + 0.5f, 1.85f), 0.04f);
                k.Rod(new Vector3(ext.X * 0.45f, floor, 0), new Vector3(ext.X * 0.45f, ext.Y * 0.45f, 0), 0.06f, Paint.Self);
                k.Rod(new Vector3(ext.X * 0.45f, ext.Y * 0.45f, 0), new Vector3(0, ext.Y * 0.45f, 0), 0.05f, Paint.Self);
                k.Rod(new Vector3(0, ext.Y * 0.45f, 0), new Vector3(0, floor + 0.95f, 0), 0.01f, Paint.Steel, 6);
                break;
            }

            case "windlass":
            {
                float floor = -ext.Y * 0.5f;
                k.Box(new Vector3(1.3f, 0.06f, 1.0f), Paint.Self, new Vector3(0, floor + 0.03f, 0));
                k.Bevel(new Vector3(0.36f, 0.3f, 0.4f), Paint.Self, new Vector3(0, floor + 0.21f, 0.1f), 0.03f);
                foreach (int s in new[] { -1, 1 })
                {
                    k.Cyl(0.17f, 0.3f, Paint.Self, new Vector3(s * 0.38f, floor + 0.23f, 0.1f), AlongX, sides: 18);
                    k.Torus(0.16f, 0.02f, Paint.Steel, new Vector3(s * 0.56f, floor + 0.23f, 0.1f), Rot(Vector3.UnitZ, MathF.PI / 2));
                    k.Box(new Vector3(0.07f, 0.035f, 0.75f), Paint.Steel, new Vector3(s * 0.38f, floor + 0.08f, -0.42f));
                }

                break;
            }

            default:
                k.Bevel(ext, Paint.Self, Vector3.Zero, 0.05f);
                break;
        }
    }

    /// <summary>Single-cell field fortification: sandbags, a wire coil, a post, a fence panel.</summary>
    private static void Fieldwork(Maker k, BlockDef def)
    {
        switch (def.Id)
        {
            case "sandbags":
                // Three courses of bags: two laid along the wall, then two across it (headers), then along again.
                // Each bag is a soft, slightly irregular pillow, every other one a shade darker.
                for (int c = 0; c < 3; c++)
                {
                    float y = -H + 0.085f + c * 0.165f;
                    for (int i = 0; i < 2; i++)
                    {
                        float off = (i - 0.5f) * 0.245f, j = Jitter(c * 7 + i * 3 + 1);
                        bool along = c != 1;
                        var size = along ? new Vector3(0.49f - 0.03f * j, 0.15f, 0.235f) : new Vector3(0.235f, 0.15f, 0.49f - 0.03f * j);
                        var at = along ? new Vector3((j - 0.5f) * 0.02f, y, off) : new Vector3(off, y, (j - 0.5f) * 0.02f);
                        k.Bevel(size, (c + i) % 2 == 0 ? Paint.Self : Paint.SelfDark, at, 0.05f, Rot(Vector3.UnitY, (j - 0.5f) * 0.08f));
                    }
                }

                break;

            case "wire":
            {
                // Three loops of a coil standing across the line, each leaning a little, on a picket stake.
                for (int i = 0; i < 3; i++)
                {
                    Quaternion q = Rot(Vector3.UnitY, (i % 2 == 0 ? 0.32f : -0.32f)) * Rot(Vector3.UnitZ, MathF.PI / 2);
                    k.Torus(0.21f, 0.006f, Paint.Steel, new Vector3(-0.17f + i * 0.17f, -0.03f, 0), q);
                }

                k.Box(new Vector3(0.025f, 0.42f, 0.025f), Paint.Dark, new Vector3(0, -0.04f, 0.2f));
                for (int i = 0; i < 6; i++)
                    k.Box(new Vector3(0.03f, 0.008f, 0.008f), Paint.Steel, new Vector3(-0.2f + i * 0.08f, 0.17f - (i % 2) * 0.36f, (i % 3 - 1) * 0.06f),
                        Rot(Vector3.UnitZ, 0.7f));
                break;
            }

            case "post":
                k.Cyl(0.055f, Full, Paint.Self, Vector3.Zero, sides: 12);
                k.Cyl(0.07f, 0.03f, Paint.SelfDark, new Vector3(0, H - 0.015f, 0), sides: 12);
                break;

            case "fence":
            {
                // A panel across the cell (in its own XY plane) hung from a post at one edge: rails top and bottom,
                // a mesh of fine wires, and a strand of barbed wire along the top.
                k.Cyl(0.03f, Full, Paint.Steel, new Vector3(-H + 0.03f, 0, 0), sides: 10);
                k.Cyl(0.014f, Full, Paint.Steel, new Vector3(0, H - 0.03f, 0), AlongX, sides: 8);
                k.Cyl(0.012f, Full, Paint.Steel, new Vector3(0, -H + 0.04f, 0), AlongX, sides: 8);
                for (int i = 1; i < 6; i++)
                {
                    float t = -H + i * Full / 6f;
                    k.Box(new Vector3(0.005f, Full * 0.9f, 0.005f), Paint.Self, new Vector3(t, 0, 0), Rot(Vector3.UnitZ, 0.45f));
                    k.Box(new Vector3(0.005f, Full * 0.9f, 0.005f), Paint.Self, new Vector3(t, 0, 0), Rot(Vector3.UnitZ, -0.45f));
                }

                k.Cyl(0.004f, Full, Paint.Steel, new Vector3(0, H - 0.005f, 0.02f), AlongX, sides: 6);
                break;
            }

            default:
                k.Bevel(new Vector3(Full), Paint.Self, Vector3.Zero);
                break;
        }
    }

    /// <summary>Big field fortification: a barrier basket, a camouflage net, an armored cupola.</summary>
    private static void FieldworkBig(Maker k, BlockDef def, Vector3 ext)
    {
        switch (def.Id)
        {
            case "hesco":
            {
                // The fabric liner, the earth showing on top, and the welded mesh round all four sides.
                k.Box(ext * new Vector3(0.97f, 0.99f, 0.97f), Paint.Self, Vector3.Zero);
                k.Box(new Vector3(ext.X * 0.95f, 0.03f, ext.Z * 0.95f), Paint.SelfDark, new Vector3(0, ext.Y * 0.5f - 0.005f, 0));
                for (int i = 0; i < 5; i++)
                    k.Bevel(new Vector3(0.18f, 0.05f, 0.14f), Paint.SelfDark, new Vector3((Jitter(i) - 0.5f) * ext.X * 0.7f, ext.Y * 0.5f + 0.01f, (Jitter(i + 9) - 0.5f) * ext.Z * 0.7f), 0.02f);
                foreach (int side in new[] { -1, 1 })
                {
                    for (int i = 0; i <= 4; i++)
                    {
                        float t = (i / 4f - 0.5f) * ext.X;
                        k.Box(new Vector3(0.012f, ext.Y, 0.012f), Paint.Steel, new Vector3(t, 0, side * ext.Z * 0.5f));
                        k.Box(new Vector3(0.012f, ext.Y, 0.012f), Paint.Steel, new Vector3(side * ext.X * 0.5f, 0, t));
                    }

                    for (int j = 0; j <= 6; j++)
                    {
                        float y = (j / 6f - 0.5f) * ext.Y;
                        k.Box(new Vector3(ext.X, 0.012f, 0.012f), Paint.Steel, new Vector3(0, y, side * ext.Z * 0.5f));
                        k.Box(new Vector3(0.012f, 0.012f, ext.Z), Paint.Steel, new Vector3(side * ext.X * 0.5f, y, 0));
                    }
                }

                break;
            }

            case "camo_net":
            {
                // The net stretched over a ridge, hung with strips of cloth in the paint job's colors, and its edges
                // drooping over the sides.
                k.Slab(new Vector3(ext.X * 1.02f, ext.Y * 0.7f, ext.Z * 1.02f), 0.18f, 0.18f, Paint.Self, new Vector3(0, -ext.Y * 0.15f, 0));
                for (int i = 0; i < 36; i++)
                {
                    float u = Jitter(i * 3 + 1) - 0.5f, v = Jitter(i * 3 + 2) - 0.5f;
                    float edge = MathF.Max(MathF.Abs(u), MathF.Abs(v)) * 2f;
                    float y = -ext.Y * 0.5f + ext.Y * 0.7f * (1f - edge * 0.82f) + 0.02f;
                    k.Box(new Vector3(0.24f, 0.025f, 0.09f), i % 3 == 0 ? Paint.SelfDark : Paint.Self, new Vector3(u * ext.X, y, v * ext.Z),
                        Rot(Vector3.UnitY, Jitter(i * 3 + 3) * MathF.PI) * Rot(Vector3.UnitX, (Jitter(i) - 0.5f) * 0.6f));
                }

                foreach (int s in new[] { -1, 1 })
                {
                    k.Box(new Vector3(ext.X * 0.9f, 0.4f, 0.02f), Paint.Self, new Vector3(0, -ext.Y * 0.5f - 0.12f, s * ext.Z * 0.53f), Rot(Vector3.UnitX, s * 0.45f));
                    k.Box(new Vector3(0.02f, 0.4f, ext.Z * 0.9f), Paint.Self, new Vector3(s * ext.X * 0.53f, -ext.Y * 0.5f - 0.12f, 0), Rot(Vector3.UnitZ, -s * 0.45f));
                }

                break;
            }

            case "cupola":
            {
                // A cast-steel dome on a bolted ring, six vision slits round it and a periscope on top. The lower half
                // of the dome sits down in the roof below.
                float baseY = -ext.Y * 0.5f;
                k.Cyl(ext.X * 0.49f, 0.12f, Paint.SelfDark, new Vector3(0, baseY + 0.06f, 0), sides: 32);
                for (int i = 0; i < 12; i++)
                {
                    float a = i * MathF.Tau / 12;
                    k.Cyl(0.025f, 0.04f, Paint.Steel, new Vector3(MathF.Sin(a) * ext.X * 0.46f, baseY + 0.13f, MathF.Cos(a) * ext.X * 0.46f), sides: 6);
                }

                k.Sphere(1f, Paint.Self, new Vector3(0, baseY, 0), new Vector3(ext.X * 0.45f, 0.8f, ext.Z * 0.45f));
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.Tau / 6;
                    float r = ext.X * 0.45f * 0.84f;
                    k.Box(new Vector3(0.2f, 0.04f, 0.06f), Paint.Dark, new Vector3(MathF.Sin(a) * r, baseY + 0.43f, MathF.Cos(a) * r), Rot(Vector3.UnitY, a));
                }

                k.Cyl(0.045f, 0.16f, Paint.Steel, new Vector3(0, baseY + 0.86f, 0), sides: 10);
                k.Box(new Vector3(0.1f, 0.06f, 0.08f), Paint.Dark, new Vector3(0, baseY + 0.96f, -0.01f));
                break;
            }

            default:
                k.Bevel(ext, Paint.Self, Vector3.Zero, 0.05f);
                break;
        }
    }

    /// <summary>A flat radar face: a dark array of small transmitters in a frame, split into tiles by a grid.</summary>
    private static void ArrayFace(Maker k, Vector3 size, Vector3 at, Quaternion tilt, int tilesX, int tilesY)
    {
        k.Bevel(size, Paint.Self, at, 0.03f, tilt);
        Vector3 front = Vector3.Transform(new Vector3(0, 0, -size.Z * 0.5f - 0.004f), tilt);
        float w = size.X * 0.9f, h = size.Y * 0.88f;
        k.Box(new Vector3(w, h, 0.01f), Paint.Dark, at + front, tilt);
        for (int i = 1; i < tilesX; i++)
        {
            Vector3 off = Vector3.Transform(new Vector3(-w * 0.5f + w * i / tilesX, 0, -size.Z * 0.5f - 0.01f), tilt);
            k.Box(new Vector3(0.012f, h, 0.008f), Paint.SelfDark, at + off, tilt);
        }

        for (int j = 1; j < tilesY; j++)
        {
            Vector3 off = Vector3.Transform(new Vector3(0, -h * 0.5f + h * j / tilesY, -size.Z * 0.5f - 0.01f), tilt);
            k.Box(new Vector3(w, 0.012f, 0.008f), Paint.SelfDark, at + off, tilt);
        }
    }

    private static void TrackRun(Maker k, float r, float width, float length)
    {
        k.Cyl(r * 0.8f, width * 0.6f, Paint.Dark, Vector3.Zero, AlongX, sides: 24);
        k.Lib("roadwheel");
        k.Cyl(r * 0.45f, width * 0.66f, Paint.Steel, Vector3.Zero, AlongX, sides: 16);
        k.Covered();
        k.Cyl(r * 0.16f, width * 0.72f, Paint.Dark, Vector3.Zero, AlongX, sides: 10);
        k.Covered();
        int links = Math.Max(2, (int)MathF.Round(length / 0.25f));
        float pitch = length / links;
        foreach (float y in new[] { -r, r })
        {
            float outward = MathF.Sign(y);
            for (int i = 0; i < links; i++)
            {
                float z = -length * 0.5f + pitch * (i + 0.5f);
                // The library's shoe has its grousers on top: the bottom run is turned over so they face out.
                k.Box(new Vector3(width, 0.06f, pitch * 0.9f), Paint.Self, new Vector3(0, y, z), outward < 0 ? Rot(Vector3.UnitZ, MathF.PI) : null);
                k.Lib("track_link");
                k.Box(new Vector3(width * 1.02f, 0.035f, 0.04f), Paint.Self, new Vector3(0, y + outward * 0.045f, z));
                k.Covered();
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

            case BlockKind.Breech when def.Breech.HighArc:
            {
                // A siege mortar: a squat, ribbed breech in a massive trunnion cradle, with recoil cylinders
                // either side and a loading tray behind.
                float r = ext.X * 0.3f;
                k.Bevel(new Vector3(ext.X, ext.Y * 0.3f, ext.Z), Paint.SelfDark, new Vector3(0, -ext.Y * 0.35f, 0), bevel);
                foreach (float x in new[] { -ext.X * 0.42f, ext.X * 0.42f })
                {
                    k.Bevel(new Vector3(ext.X * 0.14f, ext.Y * 0.7f, ext.Z * 0.8f), Paint.Self, new Vector3(x, -ext.Y * 0.05f, 0), bevel * 0.6f);
                    k.Cyl(ext.Y * 0.14f, ext.X * 0.12f, Paint.Steel, new Vector3(x, ext.Y * 0.08f, 0), AlongX, sides: 20);
                    k.Cyl(0.09f, ext.Z * 0.85f, Paint.Steel, new Vector3(x * 0.7f, ext.Y * 0.28f, 0), AlongZ, sides: 12);
                }

                k.Cyl(r, ext.Z * 0.9f, Paint.Self, new Vector3(0, ext.Y * 0.08f, 0), AlongZ, sides: 28);
                for (int i = 0; i < 5; i++) k.Cyl(r * 1.08f, 0.07f, Paint.SelfDark, new Vector3(0, ext.Y * 0.08f, -ext.Z * 0.36f + i * ext.Z * 0.18f), AlongZ, sides: 28);
                k.Cyl(r * 0.8f, 0.2f, Paint.Steel, new Vector3(0, ext.Y * 0.08f, ext.Z * 0.5f + 0.05f), AlongZ, sides: 24);
                k.Box(new Vector3(ext.X * 0.4f, 0.06f, 0.5f), Paint.Steel, new Vector3(0, -ext.Y * 0.12f, ext.Z * 0.5f + 0.25f));
                break;
            }

            case BlockKind.Breech when def.Breech.Rail:
            {
                // A heavy railgun: a long armored magnet housing banded with glowing coils, power trunks along
                // its sides, and the two great rails leaving its face.
                k.Bevel(ext * new Vector3(0.95f, 0.8f, 1f), Paint.Self, new Vector3(0, -ext.Y * 0.05f, 0), bevel);
                for (int i = 0; i < 4; i++)
                {
                    float z = -ext.Z * 0.36f + i * ext.Z * 0.24f;
                    k.Box(new Vector3(ext.X * 0.98f, ext.Y * 0.84f, 0.06f), Paint.SelfDark, new Vector3(0, -ext.Y * 0.05f, z));
                    k.Glow(new Vector3(ext.X * 0.7f, 0.04f, 0.07f), Cyan, 1.6f, new Vector3(0, ext.Y * 0.36f, z));
                }

                foreach (float x in new[] { -ext.X * 0.5f, ext.X * 0.5f })
                    k.Cyl(0.12f, ext.Z * 0.9f, Paint.Dark, new Vector3(x, -ext.Y * 0.2f, 0), AlongZ, sides: 14);
                foreach (float x in new[] { -0.2f, 0.2f })
                    k.Box(new Vector3(0.12f, ext.Y * 0.45f, 0.3f), Paint.Steel, new Vector3(x, 0, -ext.Z * 0.5f - 0.12f));
                break;
            }

            case BlockKind.Breech when def.Breech.Beam:
            {
                // A laser battery: a turret drum with deep cooling fins, a ring of heat exchangers, and a great
                // red aperture at its face.
                float r = MathF.Min(ext.X, ext.Y) * 0.45f;
                k.Bevel(new Vector3(ext.X, ext.Y * 0.25f, ext.Z), Paint.SelfDark, new Vector3(0, -ext.Y * 0.38f, 0), bevel);
                k.Cyl(r, ext.Z * 0.95f, Paint.Self, new Vector3(0, ext.Y * 0.05f, 0), AlongZ, sides: 28);
                for (int i = 0; i < 6; i++)
                    k.Cyl(r * 1.12f, 0.035f, Paint.Steel, new Vector3(0, ext.Y * 0.05f, -ext.Z * 0.38f + i * ext.Z * 0.15f), AlongZ, sides: 28);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.Tau / 8;
                    k.Cyl(0.06f, ext.Z * 0.8f, Paint.Dark, new Vector3(MathF.Cos(a) * r * 1.2f, ext.Y * 0.05f + MathF.Sin(a) * r * 1.2f, 0), AlongZ, sides: 10);
                }

                k.Cyl(r * 0.75f, 0.06f, Paint.Dark, new Vector3(0, ext.Y * 0.05f, -ext.Z * 0.5f), AlongZ, sides: 28);
                k.GlowCyl(r * 0.5f, 0.03f, new Vector3(1f, 0.25f, 0.2f), 2.6f, new Vector3(0, ext.Y * 0.05f, -ext.Z * 0.5f - 0.02f), AlongZ);
                break;
            }

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

            case BlockKind.Bunk:
            {
                // A steel bed frame with a mattress, a folded blanket and a pillow.
                float w = ext.X, l = ext.Z, y0 = -ext.Y * 0.5f;
                foreach (float x in new[] { -w * 0.45f, w * 0.45f })
                foreach (float z in new[] { -l * 0.47f, l * 0.47f })
                    k.Box(new Vector3(0.04f, ext.Y * 0.8f, 0.04f), Paint.Steel, new Vector3(x, y0 + ext.Y * 0.4f, z));
                k.Box(new Vector3(w * 0.94f, 0.04f, l * 0.98f), Paint.Steel, new Vector3(0, y0 + ext.Y * 0.62f, 0));
                k.Bevel(new Vector3(w * 0.9f, 0.1f, l * 0.94f), Paint.Self, new Vector3(0, y0 + ext.Y * 0.74f, 0), 0.03f);
                k.Bevel(new Vector3(w * 0.92f, 0.05f, l * 0.45f), Paint.SelfDark, new Vector3(0, y0 + ext.Y * 0.82f, l * 0.2f), 0.02f);
                k.Bevel(new Vector3(w * 0.6f, 0.07f, 0.22f), Paint.Steel, new Vector3(0, y0 + ext.Y * 0.84f, -l * 0.38f), 0.03f);
                break;
            }

            case BlockKind.Locker:
            {
                // A tall steel cabinet; behind its mesh door, rifles stand in a rack and rockets lie on a shelf.
                k.Bevel(new Vector3(ext.X, ext.Y, ext.Z * 0.9f), Paint.Self, new Vector3(0, 0, ext.Z * 0.05f), 0.02f);
                k.Box(new Vector3(ext.X * 0.86f, ext.Y * 0.9f, 0.01f), Paint.Dark, new Vector3(0, 0, -ext.Z * 0.4f));
                for (int i = 0; i < 4; i++)
                {
                    float x = -ext.X * 0.3f + i * ext.X * 0.2f;
                    k.Box(new Vector3(0.04f, ext.Y * 0.5f, 0.05f), Paint.SelfDark, new Vector3(x, -ext.Y * 0.12f, -ext.Z * 0.36f));
                }

                k.Cyl(0.05f, ext.X * 0.7f, Paint.Steel, new Vector3(0, ext.Y * 0.3f, -ext.Z * 0.36f), AlongX, sides: 10);
                k.Box(new Vector3(ext.X * 0.9f, 0.02f, 0.02f), Paint.Steel, new Vector3(0, ext.Y * 0.2f, -ext.Z * 0.43f));
                k.Box(new Vector3(0.03f, 0.12f, 0.03f), Paint.Steel, new Vector3(ext.X * 0.38f, 0, -ext.Z * 0.45f));
                break;
            }

            case BlockKind.DozerBlade:
            {
                // A curved mouldboard with a steel cutting edge, end plates and ribs behind, on two push arms with
                // rams. The whole blade hangs from one node, which drops when the driver lowers it.
                k.Node = k.AddNode("Blade", Vector3.Zero);
                float hx = ext.X * 0.5f, hy = ext.Y * 0.5f;
                (float y, float z, float tilt)[] curve = { (-0.38f, -0.15f, -32f), (-0.13f, -0.05f, -9f), (0.12f, -0.02f, 10f), (0.36f, -0.1f, 36f) };
                foreach ((float y, float z, float tilt) in curve)
                    k.Bevel(new Vector3(ext.X, hy * 0.54f, 0.05f), Paint.Self, new Vector3(0, y * hy / 0.5f, z), 0.01f,
                        Quaternion.CreateFromAxisAngle(Vector3.UnitX, tilt * MathF.PI / 180f));
                k.Box(new Vector3(ext.X * 1.01f, 0.06f, 0.09f), Paint.Steel, new Vector3(0, -hy + 0.03f, -0.19f));
                foreach (float x in new[] { -hx, hx })
                    k.Box(new Vector3(0.04f, ext.Y * 0.95f, 0.3f), Paint.SelfDark, new Vector3(x, 0, -0.06f));
                foreach (float x in new[] { -hx * 0.62f, 0f, hx * 0.62f })
                    k.Box(new Vector3(0.05f, ext.Y * 0.8f, 0.12f), Paint.SelfDark, new Vector3(x, 0, 0.08f));
                foreach (float x in new[] { -hx * 0.72f, hx * 0.72f })
                {
                    k.Box(new Vector3(0.11f, 0.13f, 0.75f), Paint.Dark, new Vector3(x, -hy * 0.45f, 0.42f));
                    k.Cyl(0.045f, 0.62f, Paint.Steel, new Vector3(x * 0.55f, hy * 0.25f, 0.38f),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.25f), sides: 12);
                }

                k.Node = -1;
                break;
            }

            case BlockKind.Workbench:
            {
                // A heavy bench on legs, a vise at one end, tools hung on a board behind and a lamp over it.
                float top = ext.Y * 0.0f;
                k.Bevel(new Vector3(ext.X, 0.08f, ext.Z * 0.9f), Paint.Self, new Vector3(0, top, 0), 0.015f);
                foreach (float x in new[] { -ext.X * 0.45f, ext.X * 0.45f })
                foreach (float z in new[] { -ext.Z * 0.38f, ext.Z * 0.38f })
                    k.Box(new Vector3(0.07f, ext.Y * 0.5f - 0.04f, 0.07f), Paint.Dark, new Vector3(x, top - ext.Y * 0.25f, z));
                k.Box(new Vector3(ext.X * 0.9f, 0.04f, ext.Z * 0.8f), Paint.SelfDark, new Vector3(0, -ext.Y * 0.38f, 0));
                k.Box(new Vector3(0.16f, 0.14f, 0.14f), Paint.Steel, new Vector3(-ext.X * 0.38f, top + 0.11f, -ext.Z * 0.2f));
                k.Box(new Vector3(ext.X * 0.9f, ext.Y * 0.42f, 0.03f), Paint.SelfDark, new Vector3(0, top + ext.Y * 0.23f, ext.Z * 0.44f));
                for (int i = 0; i < 6; i++)
                    k.Box(new Vector3(0.03f, 0.18f, 0.02f), Paint.Steel, new Vector3(-ext.X * 0.3f + i * ext.X * 0.12f, top + ext.Y * 0.24f, ext.Z * 0.42f));
                k.Cyl(0.06f, 0.05f, Paint.Dark, new Vector3(ext.X * 0.25f, top + ext.Y * 0.45f, ext.Z * 0.3f), sides: 12);
                k.Glow(new Vector3(0.08f, 0.01f, 0.08f), new Vector3(1f, 0.9f, 0.7f), 2f, new Vector3(ext.X * 0.25f, top + ext.Y * 0.42f, ext.Z * 0.3f));
                break;
            }

            case BlockKind.SupplyCrate:
            {
                // Stacked ammunition boxes and a fuel drum on a pallet.
                k.Box(new Vector3(ext.X, 0.06f, ext.Z), Paint.SelfDark, new Vector3(0, -ext.Y * 0.47f, 0));
                k.Bevel(new Vector3(ext.X * 0.55f, ext.Y * 0.4f, ext.Z * 0.45f), Paint.Self, new Vector3(-ext.X * 0.2f, -ext.Y * 0.24f, -ext.Z * 0.24f), 0.02f);
                k.Bevel(new Vector3(ext.X * 0.55f, ext.Y * 0.4f, ext.Z * 0.45f), Paint.Self, new Vector3(-ext.X * 0.2f, -ext.Y * 0.24f, ext.Z * 0.24f), 0.02f);
                k.Bevel(new Vector3(ext.X * 0.5f, ext.Y * 0.35f, ext.Z * 0.42f), Paint.Self, new Vector3(-ext.X * 0.2f, ext.Y * 0.14f, 0), 0.02f);
                k.Box(new Vector3(ext.X * 0.3f, 0.06f, 0.006f), Paint.Brass, new Vector3(-ext.X * 0.2f, -ext.Y * 0.2f, -ext.Z * 0.467f));
                k.Cyl(ext.X * 0.2f, ext.Y * 0.8f, Paint.Red, new Vector3(ext.X * 0.27f, -ext.Y * 0.06f, 0), sides: 18);
                foreach (float y in new[] { -0.3f, 0.2f }) k.Cyl(ext.X * 0.205f, 0.03f, Paint.Dark, new Vector3(ext.X * 0.27f, ext.Y * y, 0), sides: 18);
                break;
            }

            case BlockKind.Door:
            {
                // A steel frame with a sliding door in it: a porthole, a handle, and warning stripes on the sill.
                float w = ext.X, h = ext.Y;
                foreach (float x in new[] { -w * 0.5f + 0.05f, w * 0.5f - 0.05f })
                    k.Bevel(new Vector3(0.1f, h, ext.Z), Paint.SelfDark, new Vector3(x, 0, 0), 0.015f);
                k.Bevel(new Vector3(w, 0.12f, ext.Z), Paint.SelfDark, new Vector3(0, h * 0.5f - 0.06f, 0), 0.015f);
                k.Box(new Vector3(w - 0.2f, 0.03f, ext.Z * 0.9f), Paint.Brass, new Vector3(0, -h * 0.5f + 0.015f, 0));
                k.Node = k.AddNode("Panel", Vector3.Zero);
                k.Bevel(new Vector3(w - 0.2f, h - 0.14f, 0.07f), Paint.Self, new Vector3(0, -0.06f, 0), 0.012f);
                k.Cyl(0.12f, 0.02f, Paint.Dark, new Vector3(0, h * 0.18f, -0.045f), AlongZ, sides: 18);
                k.Cyl(0.1f, 0.012f, Paint.Glass, new Vector3(0, h * 0.18f, -0.05f), AlongZ, sides: 18);
                k.Cyl(0.12f, 0.02f, Paint.Dark, new Vector3(0, h * 0.18f, 0.045f), AlongZ, sides: 18);
                foreach (float z in new[] { -0.05f, 0.05f })
                    k.Box(new Vector3(0.03f, 0.16f, 0.03f), Paint.Steel, new Vector3(w * 0.32f, -0.05f, z));
                k.Node = -1;
                break;
            }

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

            case BlockKind.Radar when def.Id == "radar_face":
            {
                // An eight-sided array set flush into a wall, tilted back a little: a raised rim, the dark face of
                // transmitters, and a seam round its middle.
                Quaternion q = Rot(Vector3.UnitX, 0.18f) * AlongZ * Rot(Vector3.UnitY, MathF.PI / 8);
                float r = MathF.Min(ext.X, ext.Y) * 0.49f;
                k.Cyl(r, 0.2f, Paint.Self, new Vector3(0, 0, 0.1f), q, sides: 8);
                k.Cyl(r * 0.9f, 0.02f, Paint.Dark, new Vector3(0, 0, -0.01f), q, sides: 8);
                k.Cyl(r * 0.45f, 0.025f, Paint.SelfDark, new Vector3(0, 0, -0.015f), q, sides: 8);
                break;
            }

            case BlockKind.Radar when def.Id == "radome":
            {
                // A long fibreglass nose on a seam ring, and a probe from its point.
                float r = MathF.Min(ext.X, ext.Y) * 0.495f, len = ext.Z * 0.86f;
                k.Ogive(r, len, Paint.Self, new Vector3(0, 0, ext.Z * 0.5f - 0.02f), 14, 32);
                k.Cyl(r * 1.003f, 0.04f, Paint.SelfDark, new Vector3(0, 0, ext.Z * 0.5f - 0.04f), AlongZ, sides: 32);
                float tip = ext.Z * 0.5f - 0.02f - len;
                k.Cyl(0.02f, ext.Z * 0.5f + tip + 0.06f, Paint.Steel, new Vector3(0, 0, (tip - ext.Z * 0.5f) * 0.5f + 0.03f), AlongZ, sides: 8);
                break;
            }

            case BlockKind.Radar when def.Id == "radar_array":
            {
                // A low equipment cabinet with boxes either side, and the big array face on struts above it, tilted
                // back to watch the sky (its face is the front).
                float bottom = -ext.Y * 0.5f;
                k.Bevel(new Vector3(ext.X * 0.9f, ext.Y * 0.16f, ext.Z * 0.8f), Paint.SelfDark, new Vector3(0, bottom + ext.Y * 0.08f, ext.Z * 0.05f), 0.03f);
                foreach (float x in new[] { -0.38f, 0.38f })
                    k.Bevel(new Vector3(ext.X * 0.16f, ext.Y * 0.14f, ext.Z * 0.5f), Paint.Self, new Vector3(x * ext.X, bottom + ext.Y * 0.23f, ext.Z * 0.15f), 0.02f);
                Quaternion tilt = Rot(Vector3.UnitX, 0.35f);
                var at = new Vector3(0, ext.Y * 0.08f, -ext.Z * 0.08f);
                var face = new Vector3(ext.X * 0.94f, ext.Y * 0.74f, ext.Z * 0.2f);
                ArrayFace(k, face, at, tilt, 8, 7);
                foreach (float x in new[] { -0.3f, 0.3f })
                    k.Rod(new Vector3(x * ext.X, bottom + ext.Y * 0.16f, ext.Z * 0.3f), at + Vector3.Transform(new Vector3(x * ext.X, -face.Y * 0.3f, face.Z * 0.5f), tilt), 0.06f, Paint.Steel);
                // The friend-or-foe antenna along the top.
                k.Box(new Vector3(ext.X * 0.8f, 0.07f, 0.06f), Paint.SelfDark, at + Vector3.Transform(new Vector3(0, face.Y * 0.5f + 0.06f, 0), tilt), tilt);
                break;
            }

            case BlockKind.Radar:
            {
                // A pedestal, and on the turning node a flat array tilted back on its mast.
                k.Cyl(0.26f, 0.34f, Paint.SelfDark, new Vector3(0, -ext.Y * 0.5f + 0.17f, 0), sides: 20);
                k.Cyl(0.34f, 0.07f, Paint.Dark, new Vector3(0, -ext.Y * 0.5f + 0.37f, 0), sides: 24);
                k.Node = k.AddNode("Spinner", new Vector3(0, -ext.Y * 0.5f + 0.41f, 0));
                k.Cyl(0.09f, 0.26f, Paint.Self, new Vector3(0, 0.13f, 0), sides: 12);
                Quaternion tilt = Rot(Vector3.UnitX, 0.26f);
                var at = new Vector3(0, 0.66f, 0.04f);
                ArrayFace(k, new Vector3(ext.X * 0.93f, 0.86f, 0.16f), at, tilt, 5, 3);
                k.Bevel(new Vector3(0.5f, 0.5f, 0.2f), Paint.SelfDark, at + Vector3.Transform(new Vector3(0, 0, 0.17f), tilt), 0.02f, tilt);
                k.Rod(new Vector3(0, 0.22f, 0), at + Vector3.Transform(new Vector3(0, -0.2f, 0.26f), tilt), 0.05f, Paint.Steel);
                k.Node = -1;
                break;
            }

            case BlockKind.Fieldwork:
                FieldworkBig(k, def, ext);
                break;

            case BlockKind.ShipFitting:
                ShipFittingBig(k, def, ext);
                break;


            case BlockKind.Fairing:
            {
                // A bubble canopy: two overlapping egg shapes (the tall front over the seat, a lower tail tapering
                // back) sitting on a sill. The glass dips into the fuselage below, so it meets the skin cleanly.
                float baseY = -ext.Y * 0.5f + 0.02f;
                k.Sphere(1f, Paint.Glass, new Vector3(0, baseY, -ext.Z * 0.13f), new Vector3(0.5f, 0.82f, ext.Z * 0.37f));
                k.Sphere(1f, Paint.Glass, new Vector3(0, baseY, 0), new Vector3(0.43f, 0.6f, ext.Z * 0.5f));
                foreach (float x in new[] { -0.47f, 0.47f })
                    k.Box(new Vector3(0.05f, 0.05f, ext.Z * 0.82f), Paint.Self, new Vector3(x, baseY + 0.02f, -ext.Z * 0.02f));
                k.Box(new Vector3(0.9f, 0.03f, 0.04f), Paint.SelfDark, new Vector3(0, baseY + 0.02f, -ext.Z * 0.43f));
                break;
            }

            case BlockKind.VlsCell when def.LaunchTiltDeg > 0:
            {
                // A box launcher: a turntable and cradle at the back, a ram to hold the box up, and the box of
                // canisters raised toward the front. Each canister's cover ("Cap0" to "Cap5") shows while it's loaded.
                Vector3 pivot = BoxLauncher.Pivot, axis = BoxLauncher.Axis(def), across = BoxLauncher.Across(def);
                Quaternion raise = Rot(Vector3.UnitX, BoxLauncher.Elevation(def));
                float bottom = -ext.Y * 0.5f;
                k.Cyl(0.62f, 0.16f, Paint.SelfDark, new Vector3(0, bottom + 0.08f, pivot.Z - 0.35f), sides: 24);
                k.Bevel(new Vector3(1.0f, 0.14f, 1.1f), Paint.Self, new Vector3(0, bottom + 0.22f, pivot.Z - 0.35f), 0.03f);
                foreach (float x in new[] { -0.58f, 0.58f })
                    k.Bevel(new Vector3(0.1f, pivot.Y - bottom - 0.2f, 0.34f), Paint.Self, new Vector3(x, (pivot.Y + bottom + 0.2f) * 0.5f + 0.05f, pivot.Z), 0.02f);
                k.Cyl(0.07f, BoxLauncher.WidthM + 0.06f, Paint.Steel, pivot, AlongX, sides: 12);
                float L = BoxLauncher.LengthM, D = BoxLauncher.DepthM, W = BoxLauncher.WidthM;
                Vector3 mid = pivot + axis * (L * 0.5f) + across * (D * 0.5f);
                k.Bevel(new Vector3(W, D, L), Paint.Self, mid, 0.03f, raise);
                // Stiffening bands round the box, and the ram from the base up to its belly.
                foreach (float f in new[] { 0.12f, 0.5f, 0.88f })
                    k.Box(new Vector3(W + 0.04f, D + 0.04f, 0.06f), Paint.SelfDark, pivot + axis * (L * f) + across * (D * 0.5f), raise);
                k.Rod(new Vector3(0, bottom + 0.3f, pivot.Z - 0.9f), pivot + axis * (L * 0.42f), 0.07f, Paint.Steel);
                k.Rod(new Vector3(0, bottom + 0.3f, pivot.Z - 0.9f), pivot + axis * (L * 0.25f), 0.1f, Paint.Self);
                for (int i = 0; i < BoxLauncher.Canisters; i++)
                {
                    Vector3 mouth = BoxLauncher.Mouth(def, i);
                    k.Box(new Vector3(0.36f, 0.36f, 0.02f), Paint.Dark, mouth + axis * 0.008f, raise);
                    k.Node = k.AddNode($"Cap{i}", mouth + axis * 0.012f);
                    k.Box(new Vector3(0.37f, 0.37f, 0.016f), Paint.SelfDark, Vector3.Zero, raise);
                    k.Box(new Vector3(0.24f, 0.03f, 0.018f), Paint.Brass, new Vector3(0, 0, 0), raise * Rot(Vector3.UnitZ, 0.78f));
                    k.Node = -1;
                }

                break;
            }

            case BlockKind.Wheel:
            case BlockKind.SteerWheel:
            {
                // A big off-road tyre (axle along X), a deep rim and a hub with a tyre-pressure line.
                float r = def.WheelRadiusM, w = ext.X * 0.92f;
                k.Cyl(r * 0.98f, w, Paint.Rubber, Vector3.Zero, AlongX, sides: 36);
                k.Lib("tire");
                k.Cyl(r * 0.56f, w + 0.02f, Paint.Self, Vector3.Zero, AlongX, sides: 28);
                k.Lib("rim");
                k.Cyl(r * 0.22f, w + 0.06f, Paint.Dark, Vector3.Zero, AlongX, sides: 14);
                k.Lib("hub");
                for (int i = 0; i < 10; i++)
                {
                    float a = i * MathF.Tau / 10;
                    k.Cyl(0.025f, w + 0.05f, Paint.Steel, new Vector3(0, MathF.Cos(a) * r * 0.36f, MathF.Sin(a) * r * 0.36f), AlongX, sides: 6);
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
