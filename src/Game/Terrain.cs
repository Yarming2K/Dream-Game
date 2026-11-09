using System;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The test range ground: a flat firing lane down the middle with rolling hills on both sides for testing
/// climbing and tipping. Collision is a heightmap with 1 m spacing, sized 2^n + 1 so the physics engine
/// can use it directly.
/// </summary>
public static class Terrain
{
    public const int Samples = 513;
    public const float LaneHalfWidth = 55f;

    public static float HeightAt(FastNoiseLite noise, float x, float z)
    {
        float half = (Samples - 1) * 0.5f;
        float lane = Smooth(LaneHalfWidth, LaneHalfWidth + 45f, MathF.Abs(x));
        float edge = 1f - Smooth(half - 60f, half - 8f, MathF.Max(MathF.Abs(x), MathF.Abs(z)));
        float hills = (noise.GetNoise2D(x, z) * 0.5f + 0.5f) * 16f + noise.GetNoise2D(x * 3.1f, z * 3.1f) * 1.5f;
        return MathF.Max(0f, hills * lane * edge);
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

        // An endless flat floor beyond the heightmap.
        body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });

        body.AddChild(new MeshInstance3D { Mesh = BuildMesh(heights, n), MaterialOverride = GroundMaterial() });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(6000, 6000) },
            Position = new Vector3(0, -0.03f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.38f, 0.27f), Roughness = 1f },
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
            Color c = lane ? dirt.Lerp(grass, 0.35f) : grass.Lerp(dirt, Math.Clamp(y / 20f, 0, 0.4f));
            colors[i] = c.Lerp(rock, Math.Clamp(slope * 6f, 0, 1));
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
