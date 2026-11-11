using Godot;

namespace DreamGame.Game;

/// <summary>
/// A jet's exhaust: a soft flame and the heat shimmer behind it. The flame fades out at its edges and toward its tip,
/// runs from blue-white at the nozzle to orange, and flickers. How much shows depends on the throttle: at normal
/// power hardly any (a faint glow and the shimmer), at full power a short pale cone, on afterburner a long yellow
/// plume with shock diamonds pulsing in its core. VehicleAir sets each engine's throttle and afterburner on it.
/// </summary>
public static class Exhaust
{
    private const string FlameCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform vec3 hot : source_color = vec3(0.75, 0.86, 1.0);
uniform vec3 warm : source_color = vec3(1.0, 0.5, 0.17);
uniform float length_m = 1.0;
uniform bool core = false;
instance uniform float throttle = 1.0;
instance uniform float burner = 0.0;

varying float t;
varying vec2 around;
varying vec3 axis_v;

float h(vec3 p) {
	return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

// Smooth value noise, 0 to 1.
float n3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	vec2 o = vec2(0.0, 1.0);
	return mix(mix(mix(h(i), h(i + o.yxx), f.x), mix(h(i + o.xyx), h(i + o.yyx), f.x), f.y),
		mix(mix(h(i + o.xxy), h(i + o.yxy), f.x), mix(h(i + o.xyy), h(i + o.yyy), f.x), f.y), f.z);
}

void vertex() {
	// 0 at the nozzle, 1 at the tip (the cone's wide end is its bottom).
	t = clamp(VERTEX.y / length_m + 0.5, 0.0, 1.0);
	// Which way round the flame (unit length, no seam where an angle would wrap).
	around = VERTEX.xz / max(length(VERTEX.xz), 0.0001);
	// The flame's axis as the camera sees it.
	axis_v = normalize((MODELVIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
}

void fragment() {
	vec3 view = normalize(VIEW);
	float facing = abs(dot(normalize(NORMAL), view));
	// Soft at its silhouette seen from the side; seen along its length (from behind the jet, the usual view),
	// the camera looks through the whole flame, so it shows in full.
	float endon = abs(dot(axis_v, view));
	float edge = mix(pow(facing, 0.9), 1.0, endon * endon);
	float along = pow(1.0 - t, core ? 0.7 : 1.3);
	float flick = n3(vec3(t * 7.0 - TIME * 9.0, around * 1.2)) * 0.6 + n3(vec3(t * 15.0 - TIME * 17.0, around * 2.4 + 4.0)) * 0.4;
	float diamonds = pow(0.5 + 0.5 * cos(t * 6.2832 * 4.5), 6.0) * (1.0 - smoothstep(0.0, 0.85, t));
	vec3 col;
	float strength;
	if (core) {
		col = hot;
		// On afterburner the core dims between the shock diamonds so they stand out.
		strength = mix(0.4, 1.6, throttle) * (1.0 - 0.5 * burner) + burner * (0.8 + 5.0 * diamonds);
	} else {
		col = mix(hot, warm, smoothstep(0.05, 0.45, t));
		col = mix(col, warm * vec3(1.0, 0.9, 0.55), burner * 0.7);
		strength = mix(0.05, 0.6, throttle * throttle) + burner * 2.5;
	}
	ALBEDO = col * strength;
	ALPHA = clamp(edge * along * (0.55 + 0.6 * flick), 0.0, 1.0);
}
";

    private const string HazeCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
uniform float length_m = 2.4;
instance uniform float throttle = 1.0;
instance uniform float burner = 0.0;

varying float t;
varying vec3 axis_v;

void vertex() {
	t = clamp(VERTEX.y / length_m + 0.5, 0.0, 1.0);
	axis_v = normalize((MODELVIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
}

void fragment() {
	// Bends what's behind it, most in the middle of the plume and near the nozzle, fading to nothing at its edges.
	vec3 view = normalize(VIEW);
	float facing = abs(dot(normalize(NORMAL), view));
	float endon = abs(dot(axis_v, view));
	float amount = (0.002 + 0.004 * throttle + 0.006 * burner) * mix(pow(facing, 2.0), 1.0, endon * endon) * (1.0 - t);
	vec2 wob = vec2(sin(SCREEN_UV.y * 95.0 + TIME * 23.0 + t * 20.0), cos(SCREEN_UV.x * 85.0 - TIME * 19.0));
	ALBEDO = texture(screen, SCREEN_UV + wob * amount).rgb;
	ALPHA = 1.0;
}
";

    private static Shader _flame, _haze;
    private static ShaderMaterial _outer, _core, _hazeMat;

    /// <summary>A flame cone's material: the outer flame, or its blue-white core.</summary>
    public static ShaderMaterial Flame(bool core, float lengthM)
    {
        _flame ??= new Shader { Code = FlameCode };
        ref ShaderMaterial m = ref core ? ref _core : ref _outer;
        if (m != null) return m;
        m = new ShaderMaterial { Shader = _flame };
        m.SetShaderParameter("core", core);
        m.SetShaderParameter("length_m", lengthM);
        return m;
    }

    /// <summary>The shimmer behind a jet: a long faint cone that bends the picture behind it.</summary>
    public static MeshInstance3D Haze()
    {
        _haze ??= new Shader { Code = HazeCode };
        if (_hazeMat == null)
        {
            // Drawn before other see-through things, so the flame lands on top of the shimmer, not under it.
            _hazeMat = new ShaderMaterial { Shader = _haze, RenderPriority = -1 };
            _hazeMat.SetShaderParameter("length_m", 2.4f);
        }

        return new MeshInstance3D
        {
            Name = "Haze",
            Mesh = new CylinderMesh { BottomRadius = 0.15f, TopRadius = 0.4f, Height = 2.4f, RadialSegments = 16, Rings = 1 },
            MaterialOverride = _hazeMat,
            // Along the flame (the cone's bottom at the nozzle, pointing back).
            Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), new Vector3(0, 0, 1.2f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Tells a jet's flame (and its shimmer) how hard the engine's working.</summary>
    public static void Drive(Node3D flame, float throttle, float burner)
    {
        foreach (Node c in flame.GetChildren())
            if (c is GeometryInstance3D g)
            {
                g.SetInstanceShaderParameter("throttle", throttle);
                g.SetInstanceShaderParameter("burner", burner);
            }
    }
}
