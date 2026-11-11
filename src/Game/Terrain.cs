using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The test range: an island. A flat firing lane runs down the middle with rolling hills on both sides,
/// a beach on the east coast slopes into the sea, and the sea surrounds everything (water level is y = 0).
/// Collision is a heightmap with 1 m spacing, sized 2^n + 1 so the physics engine can use it directly.
/// <para>The ground can change: blasts dig craters and people and machines dig and pile earth (see
/// <see cref="DreamGame.Core.Earthworks"/>). It's drawn and collided in 8 × 8 tiles of 64 m, so a change
/// only redoes the tiles it touches: their height maps at once, their meshes a few a frame. Fresh earth
/// is drawn dark, and anything dug below sea level fills with water. The ground under buildings is
/// locked, so a crater stops at their walls.</para>
/// </summary>
public static class Terrain
{
    public const int Samples = 513;
    public const float LaneHalfWidth = 55f;

    /// <summary>Height of the flat land above the sea.</summary>
    public const float LandHeight = 3f;

    public const float SeabedDepth = -26f;

    /// <summary>Where the east coast's beach starts sloping into the sea.</summary>
    public const float CoastX = 135f;

    /// <summary>The airstrip runs north-south through the western hills, flattened out of them.</summary>
    public const float StripX = -105f, StripHalfWidth = 14f, StripHalfLength = 205f;

    public static readonly Vector2 Helipad = new(-62f, 172f);
    public const float HelipadRadius = 11f;

    /// <summary>
    /// Level ground cut out of the eastern hills for the buildings that stand there: the coastal citadel, the
    /// mortar battery and the flak tower.
    /// </summary>
    public static readonly Vector2 CitadelPad = new(CoastX - 32f, 60f), MortarPad = new(84f, 102f), FlakTowerPad = new(82f, 142f);
    public const float PadRadius = 15f;

    private static readonly Vector2[] Pads = { CitadelPad, MortarPad, FlakTowerPad };

    /// <summary>The village: houses, a barn and a chapel along a main street, on ground levelled for them.</summary>
    public static readonly Vector2 Village = new(97f, -112f);
    public const float VillageRadius = 44f;

    /// <summary>Dirt roads: from the firing lane out to the village, and its main street.</summary>
    public static readonly (Vector2 a, Vector2 b)[] Roads =
    {
        (new Vector2(40f, -112f), new Vector2(122f, -112f)),
        (new Vector2(97f, -158f), new Vector2(97f, -66f)),
    };

    /// <summary>How far a point is from the nearest road's middle line.</summary>
    public static float RoadDistance(float x, float z)
    {
        float best = float.MaxValue;
        var p = new Vector2(x, z);
        foreach ((Vector2 a, Vector2 b) in Roads)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            best = Mathf.Min(best, p.DistanceTo(a + ab * t));
        }

        return best;
    }

    public static float HeightAt(FastNoiseLite noise, float x, float z)
    {
        float half = (Samples - 1) * 0.5f;
        float lane = Smooth(LaneHalfWidth, LaneHalfWidth + 45f, MathF.Abs(x));
        float hills = (noise.GetNoise2D(x, z) * 0.5f + 0.5f) * 16f + noise.GetNoise2D(x * 3.1f, z * 3.1f) * 1.5f;
        float land = LandHeight + MathF.Max(0f, hills * lane);
        // How much of this spot is island rather than sea: the east coast, and a ring of sea round the edge.
        float east = 1f - Smooth(CoastX, CoastX + 60f, x + noise.GetNoise2D(z * 0.4f, 7f) * 12f);
        float rim = 1f - Smooth(half - 45f, half - 5f, MathF.Max(MathF.Abs(x), MathF.Abs(z)));
        float island = MathF.Min(east, rim);
        // Hills flatten out towards the beach, and are cut away for the airstrip and the helipad.
        land = LandHeight + (land - LandHeight) * Smooth(0.6f, 1f, island);
        float strip = (1f - Smooth(StripHalfWidth + 4f, StripHalfWidth + 30f, MathF.Abs(x - StripX))) *
                      (1f - Smooth(StripHalfLength, StripHalfLength + 30f, MathF.Abs(z)));
        float pad = 1f - Smooth(HelipadRadius + 3f, HelipadRadius + 25f, MathF.Sqrt((x - Helipad.X) * (x - Helipad.X) + (z - Helipad.Y) * (z - Helipad.Y)));
        foreach (Vector2 p in Pads)
            pad = MathF.Max(pad, 1f - Smooth(PadRadius, PadRadius + 22f, MathF.Sqrt((x - p.X) * (x - p.X) + (z - p.Y) * (z - p.Y))));
        pad = MathF.Max(pad, 1f - Smooth(VillageRadius, VillageRadius + 30f, MathF.Sqrt((x - Village.X) * (x - Village.X) + (z - Village.Y) * (z - Village.Y))));
        land = LandHeight + (land - LandHeight) * (1f - MathF.Max(strip, pad));
        return SeabedDepth + (land - SeabedDepth) * island;
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>The island's ground as it changes (craters, digging), or null before the range is built.</summary>
    public static DreamGame.Core.Earthworks Earth { get; private set; }

    /// <summary>The ground changed here (in world meters, x0, z0, x1, z1): vehicles there wake up, the map redraws.</summary>
    public static event Action<Rect2> Changed;

    /// <summary>Quads per side of one tile of the ground (8 × 8 tiles make the island).</summary>
    private const int Tile = 64;
    private const int Tiles = (Samples - 1) / Tile;

    private static HeightMapShape3D[] _shapes;
    private static MeshInstance3D[] _meshes;
    private static StaticBody3D _body;
    private static readonly HashSet<int> _collideDirty = new(), _drawDirty = new();
    private static float[] _tileData;
    private static ShaderMaterial _material;

    public static StaticBody3D Build()
    {
        var noise = new FastNoiseLite { Seed = 7, Frequency = 0.012f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
        int n = Samples;
        float half = (n - 1) * 0.5f;
        var heights = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
            heights[z * n + x] = HeightAt(noise, x - half, z - half);

        // The same grid, for the fast ground tests shells, wheels and gunners use (see WorldQuery), and the
        // earthworks that change it.
        WorldQuery.Ground = new DreamGame.Core.HeightField(n, n, 1f, new System.Numerics.Vector2(-half, -half), heights, SeabedDepth);
        Earth = new DreamGame.Core.Earthworks(WorldQuery.Ground) { Locked = IsLocked };
        _locked = new bool[n * n];
        _collideDirty.Clear();
        _drawDirty.Clear();

        _body = new StaticBody3D { Name = "Terrain", CollisionLayer = GridRegistry.Layers.World, CollisionMask = 0 };
        _body.AddChild(new TerrainUpdater());

        // The ground in 8 × 8 tiles, each its own height map and mesh, so a crater only redoes the tile (or
        // the few) it's in.
        _shapes = new HeightMapShape3D[Tiles * Tiles];
        _meshes = new MeshInstance3D[Tiles * Tiles];
        _tileData = new float[(Tile + 1) * (Tile + 1)];
        _material = GroundMaterial();
        for (int tz = 0; tz < Tiles; tz++)
        for (int tx = 0; tx < Tiles; tx++)
        {
            int t = tz * Tiles + tx;
            _shapes[t] = new HeightMapShape3D { MapWidth = Tile + 1, MapDepth = Tile + 1, MapData = TileHeights(tx, tz) };
            _body.AddChild(new CollisionShape3D
            {
                Shape = _shapes[t], Position = new Vector3(tx * Tile + Tile / 2f - half, 0, tz * Tile + Tile / 2f - half),
            });
            _meshes[t] = new MeshInstance3D { Mesh = BuildTileMesh(tx, tz), MaterialOverride = _material };
            _body.AddChild(_meshes[t]);
        }

        // An endless flat seabed beyond the heightmap.
        _body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D(), Position = new Vector3(0, SeabedDepth, 0) });
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(6000, 6000) },
            Position = new Vector3(0, SeabedDepth - 0.03f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.33f, 0.31f, 0.24f), Roughness = 1f },
        });
        return _body;
    }

    private static float[] TileHeights(int tx, int tz)
    {
        float[] h = WorldQuery.Ground.Heights;
        var data = new float[(Tile + 1) * (Tile + 1)];
        for (int z = 0; z <= Tile; z++)
        for (int x = 0; x <= Tile; x++)
            data[z * (Tile + 1) + x] = h[(tz * Tile + z) * Samples + tx * Tile + x];
        return data;
    }

    // ------------------------------------------------------------------ changing the ground

    private static bool[] _locked;

    private static bool IsLocked(int x, int z) => _locked != null && _locked[z * Samples + x];

    /// <summary>
    /// Keeps the ground under a building as it is (craters stop at its walls): every sample under the
    /// footprint of its bottom row of blocks, at the given place and turn.
    /// </summary>
    public static void LockUnder(Node3D body, DreamGame.Core.Int3 min, DreamGame.Core.Int3 max)
    {
        if (_locked == null || body == null) return;
        Transform3D inv = body.GlobalTransform.AffineInverse();
        float cs = Conv.CS, half = (Samples - 1) * 0.5f;
        float r = new Vector2((max.X - min.X + 1) * cs, (max.Z - min.Z + 1) * cs).Length() * 0.5f + 1f;
        Vector3 c = body.GlobalTransform * new Vector3((min.X + max.X) * 0.5f * cs, 0, (min.Z + max.Z) * 0.5f * cs);
        for (int z = Mathf.Max(0, (int)(c.Z - r + half)); z <= Mathf.Min(Samples - 1, (int)(c.Z + r + half) + 1); z++)
        for (int x = Mathf.Max(0, (int)(c.X - r + half)); x <= Mathf.Min(Samples - 1, (int)(c.X + r + half) + 1); x++)
        {
            Vector3 local = inv * new Vector3(x - half, body.GlobalPosition.Y, z - half);
            if (local.X >= (min.X - 1f) * cs && local.X <= (max.X + 1f) * cs && local.Z >= (min.Z - 1f) * cs && local.Z <= (max.Z + 1f) * cs)
                _locked[z * Samples + x] = true;
        }
    }

    /// <summary>A blast near the ground: digs the crater it would (nothing if it went off too high).</summary>
    public static void Blast(Vector3 at, float radius, float damage)
    {
        if (Earth == null) return;
        float ground = WorldQuery.Ground.HeightAt(at.X, at.Z);
        (float r, float depth) = DreamGame.Core.Earthworks.CraterSize(damage, radius, at.Y - ground);
        if (r < 0.6f || depth < 0.05f) return;
        Earth.Crater(new System.Numerics.Vector2(at.X, at.Z), r, depth);
    }

    /// <summary>
    /// Takes what changed since the last frame: the height maps of those tiles at once (vehicles drive on
    /// them), their meshes a few a frame, and tells anyone who cares.
    /// </summary>
    internal static void Flush()
    {
        // Wear from wheels and tracks only changes the look: those tiles are just redrawn.
        if (Earth != null)
        {
            DreamGame.Core.GridRect w = Earth.TakeWornDirty();
            if (!w.IsEmpty)
                for (int tz = Math.Max(0, (w.Z0 - 1) / Tile); tz <= Math.Min(Tiles - 1, (w.Z1 + 1) / Tile); tz++)
                for (int tx = Math.Max(0, (w.X0 - 1) / Tile); tx <= Math.Min(Tiles - 1, (w.X1 + 1) / Tile); tx++)
                    _drawDirty.Add(tz * Tiles + tx);
        }

        if (Earth == null || !Earth.HasDirty) { DrawSome(); return; }
        DreamGame.Core.GridRect d = Earth.TakeDirty();
        float half = (Samples - 1) * 0.5f;
        for (int tz = Math.Max(0, (d.Z0 - 1) / Tile); tz <= Math.Min(Tiles - 1, (d.Z1 + 1) / Tile); tz++)
        for (int tx = Math.Max(0, (d.X0 - 1) / Tile); tx <= Math.Min(Tiles - 1, (d.X1 + 1) / Tile); tx++)
        {
            int t = tz * Tiles + tx;
            _collideDirty.Add(t);
            _drawDirty.Add(t);
        }

        foreach (int t in _collideDirty) _shapes[t].MapData = TileHeights(t % Tiles, t / Tiles);
        _collideDirty.Clear();
        DrawSome();
        var rect = new Rect2(d.X0 - half, d.Z0 - half, d.X1 - d.X0 + 1, d.Z1 - d.Z0 + 1);
        // Whatever stands there feels the ground go: parked vehicles wake and settle into the new shape.
        foreach (IGridHost host in GridRegistry.Hosts)
        {
            if (host.Body is not RigidBody3D rb || !GodotObject.IsInstanceValid(rb)) continue;
            Vector3 p = rb.GlobalPosition;
            if (!rect.Grow(host.Radius + 2f).HasPoint(new Vector2(p.X, p.Z))) continue;
            if (host is VehicleBody v) v.Unpark();
            rb.Sleeping = false;
        }

        Changed?.Invoke(rect);
    }

    private static void DrawSome()
    {
        int done = 0;
        foreach (int t in _drawDirty.ToArray())
        {
            if (done++ >= 4) break;
            _drawDirty.Remove(t);
            if (_meshes[t] != null && GodotObject.IsInstanceValid(_meshes[t])) _meshes[t].Mesh = BuildTileMesh(t % Tiles, t / Tiles);
        }
    }

    // The ground's look. Each point of the mesh says how much of it is dirt (r), rock (g), sand (b) and earth
    // churned up by shells or dug (a); grass is what's left. The shader paints each layer from noise in world
    // space: dry and green patches of grass with bare spots, pebbly dirt, rock with strata and cracks, sand with a
    // wet band at the waterline, dark clods of turned earth.
    private const string GroundCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform sampler2D blotch : filter_linear_mipmap, repeat_enable;
uniform sampler2D grain : filter_linear_mipmap, repeat_enable;
uniform float land = 3.0;
global uniform float wetness;

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float tri(vec3 p, vec3 n) {
	vec3 w = n * n;
	w /= (w.x + w.y + w.z);
	return texture(grain, p.zy).r * w.x + texture(grain, p.xz).r * w.y + texture(grain, p.xy).r * w.z;
}

void fragment() {
	vec2 p = wpos.xz;
	float big = texture(blotch, p * 0.0035).r;
	float mid = texture(blotch, p * 0.027 + vec2(0.37, 0.71)).r;
	float fine = texture(grain, p * 0.31).r;
	float speck = texture(grain, p * 1.9 + vec2(0.11, 0.53)).r;
	float edge = (mid - 0.5) * 0.45 + (fine - 0.5) * 0.2;

	// Grass: green and dry patches, a speckle of blades, the odd bare spot.
	vec3 grass = mix(vec3(0.21, 0.27, 0.11), vec3(0.37, 0.36, 0.17), smoothstep(0.3, 0.75, big));
	grass = mix(grass, grass * vec3(1.15, 1.08, 0.85), smoothstep(0.45, 0.8, mid) * 0.6);
	grass *= 0.82 + 0.3 * speck;
	vec3 dirt = vec3(0.37, 0.31, 0.22) * (0.78 + 0.35 * fine);
	dirt = mix(dirt, vec3(0.55, 0.52, 0.47), smoothstep(0.78, 0.86, speck) * 0.6);
	float bare = smoothstep(0.7, 0.8, mid * 0.65 + fine * 0.35);
	vec3 col = mix(grass, dirt, bare * 0.7);
	float rough = 0.92;

	float wd = smoothstep(0.25, 0.65, COLOR.r + edge);
	col = mix(col, dirt, wd);

	// Rock on the slopes: strata and cracks, wrapped round from all sides.
	float rk = tri(wpos * vec3(0.18, 0.7, 0.18), wnrm);
	vec3 rock = vec3(0.43, 0.41, 0.37) * (0.62 + 0.55 * rk);
	rock *= 1.0 - smoothstep(0.82, 0.92, 1.0 - tri(wpos * 0.9, wnrm)) * 0.45;
	float wr = smoothstep(0.3, 0.6, COLOR.g + edge * 0.6);
	col = mix(col, rock, wr);
	rough = mix(rough, 0.82, wr);

	// Sand at the shore, darker and shinier where the sea wets it, then the seabed.
	vec3 sand = vec3(0.62, 0.56, 0.42) * (0.88 + 0.18 * fine);
	float wet = 1.0 - smoothstep(0.15, 0.7, wpos.y);
	sand = mix(sand, vec3(0.4, 0.36, 0.27) * (0.9 + 0.15 * fine), wet);
	float ws = smoothstep(0.2, 0.6, COLOR.b + edge * 0.5);
	col = mix(col, sand, ws);
	rough = mix(rough, mix(0.86, 0.45, wet), ws);
	col = mix(col, vec3(0.27, 0.28, 0.23), smoothstep(-1.5, -7.0, wpos.y));

	// Churned earth: dark soil with lighter clods.
	vec3 turned = vec3(0.19, 0.15, 0.1) * (0.72 + 0.45 * fine);
	turned = mix(turned, vec3(0.33, 0.27, 0.19), smoothstep(0.72, 0.8, speck) * 0.7);
	col = mix(col, turned, smoothstep(0.1, 0.5, COLOR.a + (fine - 0.5) * 0.3));

	// Rain darkens the ground and makes it shine, most where it's flat.
	float wetnow = wetness * (0.6 + 0.4 * smoothstep(0.85, 0.98, wnrm.y));
	col *= 1.0 - 0.35 * wetnow;
	rough = mix(rough, 0.3, wetnow * 0.75);
	ALBEDO = col;
	ROUGHNESS = rough;
	METALLIC = 0.0;
}
";

    private static ShaderMaterial GroundMaterial()
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = GroundCode } };
        m.SetShaderParameter("blotch", new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.008f, FractalOctaves = 5, Seed = 3 },
        });
        m.SetShaderParameter("grain", new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.03f, FractalOctaves = 3, Seed = 11 },
        });
        m.SetShaderParameter("land", LandHeight);
        return m;
    }

    private static ArrayMesh BuildTileMesh(int tx, int tz)
    {
        float[] h = WorldQuery.Ground.Heights;
        float[] stirred = Earth?.Disturbed;
        float[] worn = Earth?.Worn;
        int n = Samples, m = Tile + 1;
        float half = (n - 1) * 0.5f;
        var verts = new Vector3[m * m];
        var normals = new Vector3[m * m];
        var colors = new Color[m * m];
        for (int lz = 0; lz < m; lz++)
        for (int lx = 0; lx < m; lx++)
        {
            int x = tx * Tile + lx, z = tz * Tile + lz;
            int i = z * n + x, li = lz * m + lx;
            float y = h[i];
            verts[li] = new Vector3(x - half, y, z - half);
            // Normals from the whole grid, so tiles meet without a seam.
            float hl = h[z * n + Math.Max(x - 1, 0)], hr = h[z * n + Math.Min(x + 1, n - 1)];
            float hd = h[Math.Max(z - 1, 0) * n + x], hu = h[Math.Min(z + 1, n - 1) * n + x];
            Vector3 nrm = new Vector3(hl - hr, 2f, hd - hu).Normalized();
            normals[li] = nrm;
            float slope = 1f - nrm.Y;
            bool lane = MathF.Abs(x - half) < LaneHalfWidth;
            // How much dirt, rock, sand and churned earth is here (the ground shader paints each).
            float dirt = lane ? 0.65f : Math.Clamp((y - LandHeight) / 20f, 0, 0.4f);
            // The roads: packed dirt, about 5 m wide.
            dirt = Math.Max(dirt, 1f - Smooth(2.2f, 3.4f, RoadDistance(x - half, z - half)));
            // Ruts: grass worn to dirt by wheels and tracks, and on to mud.
            float rut = worn != null ? worn[i] : 0f;
            dirt = Math.Max(dirt, rut * 0.9f);
            float rock = Math.Clamp(slope * 6f, 0, 1);
            float sand = y < -1.5f ? 1f : Math.Clamp((LandHeight + 0.8f - y) / 1.5f, 0, 1);
            float turned = Math.Max(stirred != null ? stirred[i] * 0.85f : 0f, (rut - 0.5f) * 0.6f);
            colors[li] = new Color(dirt, rock, sand, turned);
        }

        var idx = new int[Tile * Tile * 6];
        int k = 0;
        for (int z = 0; z < Tile; z++)
        for (int x = 0; x < Tile; x++)
        {
            int a = z * m + x, b = a + 1, c = a + m, d = c + 1;
            idx[k++] = a;
            idx[k++] = b;
            idx[k++] = c;
            idx[k++] = b;
            idx[k++] = d;
            idx[k++] = c;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}

/// <summary>Applies the ground's changes once a frame.</summary>
public partial class TerrainUpdater : Node
{
    public override void _PhysicsProcess(double delta) => Terrain.Flush();
}
