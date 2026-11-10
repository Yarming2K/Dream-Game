using Godot;

namespace DreamGame.Game;

/// <summary>
/// The sea: one big water surface at y = 0 with gentle rolling waves (for looks; physics treats the
/// surface as flat), clear shallows and foam where it meets the shore, and deep blue further out.
/// </summary>
public static class Ocean
{
    public const float Level = 0f;

    private const string WaterCode = @"
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, specular_schlick_ggx;

uniform vec4 shallow_color : source_color = vec4(0.18, 0.46, 0.47, 1.0);
uniform vec4 deep_color : source_color = vec4(0.03, 0.13, 0.21, 1.0);
uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;

vec3 waves(vec2 p) {
	// Height, d/dx, d/dz of three rolling swells.
	vec2 d1 = normalize(vec2(1.0, 0.3));
	vec2 d2 = normalize(vec2(-0.4, 1.0));
	vec2 d3 = normalize(vec2(0.7, -0.7));
	float f1 = 0.11, f2 = 0.2, f3 = 0.45;
	float a1 = 0.22, a2 = 0.1, a3 = 0.04;
	float s1 = dot(p, d1) * f1 + TIME * 1.0;
	float s2 = dot(p, d2) * f2 + TIME * 1.6;
	float s3 = dot(p, d3) * f3 + TIME * 2.4;
	float h = sin(s1) * a1 + sin(s2) * a2 + sin(s3) * a3;
	vec2 g = d1 * cos(s1) * a1 * f1 + d2 * cos(s2) * a2 * f2 + d3 * cos(s3) * a3 * f3;
	return vec3(h, g.x, g.y);
}

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 w = waves(world.xz);
	VERTEX.y += w.x;
	NORMAL = normalize(vec3(-w.y, 1.0, -w.z));
}

void fragment() {
	float raw = texture(depth_tex, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
	behind.xyz /= behind.w;
	float thickness = clamp(VERTEX.z - behind.z, 0.0, 200.0);
	float deep = clamp(thickness / 7.0, 0.0, 1.0);
	float foam = (1.0 - smoothstep(0.0, 0.7, thickness)) * 0.65;
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float fresnel = pow(1.0 - facing, 4.0);
	vec3 col = mix(shallow_color.rgb, deep_color.rgb, deep);
	ALBEDO = mix(col, vec3(0.92, 0.95, 0.95), foam);
	ALPHA = clamp(mix(0.45, 0.93, deep) + fresnel * 0.3 + foam, 0.0, 1.0);
	ROUGHNESS = 0.05 + foam * 0.6;
	METALLIC = 0.0;
	SPECULAR = 0.7;
}
";

    public static MeshInstance3D Build()
    {
        var mat = new ShaderMaterial { Shader = new Shader { Code = WaterCode } };
        // Fine near the island, coarse far away (the far sea is mostly seen at a grazing angle).
        var root = new MeshInstance3D
        {
            Name = "Ocean",
            Mesh = new PlaneMesh { Size = new Vector2(1400, 1400), SubdivideWidth = 280, SubdivideDepth = 280 },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0, Level, 0),
        };
        // The open sea beyond: four strips around the detailed square, so no water is drawn twice.
        const float inner = 700f, outer = 12000f;
        (Vector2 size, Vector3 pos)[] strips =
        {
            (new Vector2(outer * 2, outer - inner), new Vector3(0, 0, -(inner + outer) / 2)),
            (new Vector2(outer * 2, outer - inner), new Vector3(0, 0, (inner + outer) / 2)),
            (new Vector2(outer - inner, inner * 2), new Vector3(-(inner + outer) / 2, 0, 0)),
            (new Vector2(outer - inner, inner * 2), new Vector3((inner + outer) / 2, 0, 0)),
        };
        foreach ((Vector2 size, Vector3 pos) in strips)
            root.AddChild(new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = size, SubdivideWidth = 90, SubdivideDepth = 90 },
                MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = pos,
            });
        return root;
    }
}
