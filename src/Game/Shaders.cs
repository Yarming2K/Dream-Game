using Godot;

namespace DreamGame.Game;

/// <summary>Small custom shaders: the hologram look for the placement preview, and the builder's grid floor.</summary>
public static class Shaders
{
    private const string HologramCode = @"
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never, blend_mix;

uniform vec4 tint : source_color = vec4(0.35, 1.0, 0.5, 1.0);
uniform float strength = 1.0;

void fragment() {
	float facing = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float rim = pow(1.0 - facing, 2.0);
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float scan = 0.5 + 0.5 * sin(world.y * 42.0 - TIME * 5.0);
	float pulse = 0.82 + 0.18 * sin(TIME * 6.0);
	ALBEDO = tint.rgb * (0.75 + rim * 0.9);
	ALPHA = clamp((0.17 + rim * 0.55 + scan * 0.07) * pulse * strength, 0.0, 1.0);
}
";

    private const string GridCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 base_color : source_color = vec4(0.13, 0.14, 0.15, 1.0);
uniform vec4 line_color : source_color = vec4(0.27, 0.3, 0.33, 1.0);
uniform vec4 axis_color : source_color = vec4(0.3, 0.6, 1.0, 1.0);
uniform float cell = 0.5;
uniform float fade_start = 7.0;
uniform float fade_end = 22.0;

float grid_line(vec2 c) {
	vec2 w = max(fwidth(c), vec2(0.0001));
	vec2 g = abs(fract(c) - 0.5) / w;
	return 1.0 - clamp(min(g.x, g.y), 0.0, 1.0);
}

void fragment() {
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 c = world.xz / cell;
	float minor = grid_line(c);
	float major = grid_line((c - 0.5) / 10.0 + 0.5);
	float axis = 1.0 - clamp(abs(world.x) / max(fwidth(world.x), 0.0001) - 0.5, 0.0, 1.0);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(world.xz));
	vec3 col = base_color.rgb;
	col = mix(col, line_color.rgb, minor * 0.55 * fade);
	col = mix(col, line_color.rgb * 1.5, major * 0.75 * fade);
	col = mix(col, axis_color.rgb, axis * 0.85 * fade);
	ALBEDO = col;
	ROUGHNESS = 0.92;
	METALLIC = 0.0;
}
";

    private static Shader _hologram, _grid;

    public static ShaderMaterial Hologram(Color tint, float strength = 1f)
    {
        _hologram ??= new Shader { Code = HologramCode };
        var m = new ShaderMaterial { Shader = _hologram, RenderPriority = 2 };
        m.SetShaderParameter("tint", tint);
        m.SetShaderParameter("strength", strength);
        return m;
    }

    public static ShaderMaterial GridFloor()
    {
        _grid ??= new Shader { Code = GridCode };
        return new ShaderMaterial { Shader = _grid };
    }
}
