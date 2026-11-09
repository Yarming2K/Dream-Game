using System;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The test range: an island. A flat firing lane runs down the middle with rolling hills on both sides,
/// a beach on the east coast slopes into the sea, and the sea surrounds everything (water level is y = 0).
/// Collision is a heightmap with 1 m spacing, sized 2^n + 1 so the physics engine can use it directly.
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
        land = LandHeight + (land - LandHeight) * (1f - MathF.Max(strip, pad));
        return SeabedDepth + (land - SeabedDepth) * island;
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static StaticBody3D Build()
    {
        var noise = new FastNoiseLite { Seed = 7, Frequency = 0.012f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
        int n = Samples;
        float half = (n - 1) * 0.5f;
        var heights = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
            heights[z * n + x] = HeightAt(noise, x - half, z - half);

        var body = new StaticBody3D { Name = "Terrain", CollisionLayer = GridRegistry.Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = heights },
        });

        // An endless flat seabed beyond the heightmap.
        body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D(), Position = new Vector3(0, SeabedDepth, 0) });

        body.AddChild(new MeshInstance3D { Mesh = BuildMesh(heights, n), MaterialOverride = GroundMaterial() });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(6000, 6000) },
            Position = new Vector3(0, SeabedDepth - 0.03f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.33f, 0.31f, 0.24f), Roughness = 1f },
        });
        return body;
    }

    private static StandardMaterial3D GroundMaterial()
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.82f, 0.82f, 0.82f));
        ramp.SetColor(1, new Color(1f, 1f, 1f));
        return new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoTexture = new NoiseTexture2D
            {
                Width = 512, Height = 512, Seamless = true, ColorRamp = ramp,
                Noise = new FastNoiseLite { Frequency = 0.03f, FractalOctaves = 5 },
            },
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.08f, 0.08f, 0.08f),
        };
    }

    private static ArrayMesh BuildMesh(float[] h, int n)
    {
        float half = (n - 1) * 0.5f;
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        var grass = new Color(0.36f, 0.4f, 0.24f);
        var dirt = new Color(0.45f, 0.4f, 0.3f);
        var rock = new Color(0.45f, 0.44f, 0.42f);
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            float y = h[i];
            verts[i] = new Vector3(x - half, y, z - half);
            float hl = h[z * n + Math.Max(x - 1, 0)], hr = h[z * n + Math.Min(x + 1, n - 1)];
            float hd = h[Math.Max(z - 1, 0) * n + x], hu = h[Math.Min(z + 1, n - 1) * n + x];
            Vector3 nrm = new Vector3(hl - hr, 2f, hd - hu).Normalized();
            normals[i] = nrm;
            float slope = 1f - nrm.Y;
            bool lane = MathF.Abs(x - half) < LaneHalfWidth;
            Color c = lane ? dirt.Lerp(grass, 0.35f) : grass.Lerp(dirt, Math.Clamp((y - LandHeight) / 20f, 0, 0.4f));
            c = c.Lerp(rock, Math.Clamp(slope * 6f, 0, 1));
            // Beach sand at the waterline, darker seabed below.
            var sand = new Color(0.64f, 0.58f, 0.43f);
            var seabed = new Color(0.32f, 0.33f, 0.28f);
            if (y < LandHeight + 0.8f) c = c.Lerp(sand, Math.Clamp((LandHeight + 0.8f - y) / 1.5f, 0, 1));
            if (y < -1.5f) c = sand.Lerp(seabed, Math.Clamp((-1.5f - y) / 6f, 0, 1));
            colors[i] = c;
        }

        var idx = new int[(n - 1) * (n - 1) * 6];
        int k = 0;
        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
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
