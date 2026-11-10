using System.Collections.Generic;
using DreamGame.Core;
using Godot;
using NVec = System.Numerics.Vector3;

namespace DreamGame.Game;

/// <summary>
/// Paint jobs on screen: one shader that paints a vehicle's plating with its livery's pattern (the same
/// pattern math as <see cref="Liveries.ColorAt"/>), adds panel seams and rain streaks to plating, applies
/// ship rules (anti-fouling, boot-top, decks), and draws glazing as panes in frames.
/// <para>Plating is painted point by point. Small parts take the pattern's color at their own position,
/// which the batches carry in each instance's custom data.</para>
/// </summary>
public static class Paintwork
{
    private const string Code = @"
shader_type spatial;
render_mode CULL, diffuse_burley, specular_schlick_ggx;

uniform vec3 color_a : source_color = vec3(0.4);
uniform vec3 color_b : source_color = vec3(0.3);
uniform vec3 color_c : source_color = vec3(0.2);
uniform int pattern = 1;
uniform float pattern_scale = 2.5;
uniform float metal = 0.4;
uniform float rough = 0.6;
uniform float darken = 1.0;
uniform bool use_shade = false;
uniform bool per_part = false;
uniform bool ship = false;
uniform float waterline = 0.0;
uniform float boot_half = 0.25;
uniform vec3 antifouling : source_color = vec3(0.45, 0.13, 0.1);
uniform vec3 boot_top : source_color = vec3(0.08, 0.08, 0.08);
uniform vec3 deck_color : source_color = vec3(0.33, 0.33, 0.32);
uniform float panel_lines = 0.0;
uniform bool glass = false;
uniform vec3 glass_color : source_color = vec3(0.14, 0.19, 0.22);
uniform vec3 frame_color : source_color = vec3(0.2, 0.2, 0.2);
uniform sampler2D grit : source_color, filter_linear_mipmap, repeat_enable;

varying vec3 lpos;
varying vec3 lnrm;
varying vec3 ppos;

uint hash3(ivec3 p) {
	uint h = (uint(p.x) * 73856093u) ^ (uint(p.y) * 19349663u) ^ (uint(p.z) * 83492791u);
	h ^= h >> 13u;
	h *= 1274126177u;
	h ^= h >> 16u;
	return h;
}

float h01(ivec3 p) {
	return float(hash3(p) & 65535u) / 65535.0;
}

float vnoise(vec3 q) {
	vec3 fl = floor(q);
	ivec3 i = ivec3(fl);
	vec3 f = q - fl;
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(h01(i), h01(i + ivec3(1, 0, 0)), f.x), mix(h01(i + ivec3(0, 1, 0)), h01(i + ivec3(1, 1, 0)), f.x), f.y);
	float b = mix(mix(h01(i + ivec3(0, 0, 1)), h01(i + ivec3(1, 0, 1)), f.x), mix(h01(i + ivec3(0, 1, 1)), h01(i + ivec3(1, 1, 1)), f.x), f.y);
	return mix(a, b, f.z);
}

float fbm(vec3 q) {
	return vnoise(q) * 0.6 + vnoise(q * 2.03 + vec3(17.0, 3.0, 9.0)) * 0.3 + vnoise(q * 4.07 + vec3(-7.0, 11.0, 5.0)) * 0.1;
}

vec3 pattern_color(vec3 p) {
	if (pattern <= 1) {
		return color_a;
	}
	if (pattern == 4) {
		float s = sin((p.x * 0.8 + p.z * 0.45 + p.y * 0.3) / pattern_scale * 3.14159265 + fbm(p / pattern_scale * 0.7) * 4.0);
		return s > 0.35 ? color_b : (s < -0.45 ? color_c : color_a);
	}
	vec3 q = p / pattern_scale;
	if (pattern == 3) {
		q = floor(p / (pattern_scale * 0.12)) * 0.12;
	}
	float n1 = fbm(q);
	float n2 = fbm(q + vec3(31.7, 7.3, 13.1));
	vec3 c = color_a;
	if (n1 >= 0.55) {
		c = color_b;
	}
	if (n2 >= 0.62) {
		c = color_c;
	}
	return c;
}

// The 24 ways a block can be turned, so a painted part can work out where each of its points is.
mat3 orient_basis(int i) {
	const mat3 T[24] = mat3[24](ORIENT_TABLE);
	return T[clamp(i, 0, 23)];
}

void vertex() {
	lpos = VERTEX;
	lnrm = NORMAL;
	ppos = per_part ? INSTANCE_CUSTOM.xyz + orient_basis(int(INSTANCE_CUSTOM.w + 0.5)) * VERTEX : VERTEX;
}

void fragment() {
	vec3 n = normalize(lnrm);
	vec3 an = abs(n);
	vec2 uv = (an.x > an.y && an.x > an.z) ? lpos.zy : ((an.y > an.z) ? lpos.xz : lpos.xy);
	float g = texture(grit, uv * 1.6).r;
	vec3 col;
	float m = metal;
	float r = rough;
	if (glass) {
		// Panes in frames: a bar along every cell edge.
		vec2 f = fract(uv / 0.5 + 0.5);
		vec2 e = min(f, 1.0 - f) * 0.5;
		float bar = 1.0 - smoothstep(0.022, 0.03, min(e.x, e.y));
		col = mix(glass_color, frame_color, bar);
		m = mix(0.55, 0.5, bar);
		r = mix(0.12, 0.55, bar);
		g = mix(1.0, g, bar);
	} else {
		vec3 p = per_part ? ppos : lpos;
		col = pattern_color(p);
		if (ship) {
			if (p.y < waterline - boot_half) {
				col = antifouling;
			} else if (p.y < waterline + boot_half) {
				col = boot_top;
			} else if (!per_part && n.y > 0.75) {
				col = deck_color;
			}
		}
		if (panel_lines > 0.0) {
			// Seams between the plates every meter, and streaks of grime running down the sides.
			vec2 f = fract(uv - 0.25);
			vec2 e = min(f, 1.0 - f);
			float seam = 1.0 - smoothstep(0.004, 0.011, min(e.x, e.y));
			col *= 1.0 - seam * 0.3 * panel_lines;
			float streak = vnoise(vec3(lpos.x * 7.0, lpos.y * 0.6, lpos.z * 7.0));
			col *= mix(1.0, 0.86 + 0.14 * streak, (1.0 - an.y) * panel_lines);
		}
		col *= darken;
	}
	col *= g;
	if (use_shade) {
		col *= COLOR.rgb;
	}
	ALBEDO = col;
	METALLIC = m;
	ROUGHNESS = r;
}
";

    private static Shader _oneSided, _twoSided;
    private static readonly Dictionary<string, ShaderMaterial> Cache = new();

    private static Shader ShaderFor(bool twoSided)
    {
        if (twoSided) return _twoSided ??= new Shader { Code = Source("cull_disabled") };
        return _oneSided ??= new Shader { Code = Source("cull_back") };
    }

    /// <summary>The shader with its cull mode and the table of block turns filled in (columns of each turn's matrix).</summary>
    private static string Source(string cull)
    {
        var mats = new List<string>();
        for (int o = 0; o < Orientations.Count; o++)
        {
            int[] m = Orientations.Matrix(o);
            string Col(int j) => $"vec3({m[j]}.0, {m[3 + j]}.0, {m[6 + j]}.0)";
            mats.Add($"mat3({Col(0)}, {Col(1)}, {Col(2)})");
        }

        return Code.Replace("CULL", cull).Replace("ORIENT_TABLE", string.Join(", ", mats));
    }

    /// <summary>What a painted part's batch carries per instance: where it is and how its block is turned.</summary>
    public static Color PartData(in Transform3D xf, int orientation) => new(xf.Origin.X, xf.Origin.Y, xf.Origin.Z, orientation);

    private static Color C(NVec v) => new(v.X, v.Y, v.Z);

    private static int PatternIndex(Livery l) => l.Pattern switch
    {
        LiveryPattern.Blotches => 2,
        LiveryPattern.Digital => 3,
        LiveryPattern.Stripes => 4,
        _ => 1,
    };

    private static ShaderMaterial Make(string key, bool twoSided, System.Action<ShaderMaterial> setup)
    {
        if (Cache.TryGetValue(key, out ShaderMaterial m)) return m;
        m = new ShaderMaterial { Shader = ShaderFor(twoSided) };
        m.SetShaderParameter("grit", BlockVisuals.GritTexture);
        setup(m);
        return Cache[key] = m;
    }

    private static void SetLivery(ShaderMaterial m, Livery l, BlockDef def, float? waterline)
    {
        m.SetShaderParameter("color_a", C(l.Base));
        m.SetShaderParameter("color_b", C(l.Second));
        m.SetShaderParameter("color_c", C(l.Third));
        m.SetShaderParameter("pattern", PatternIndex(l));
        m.SetShaderParameter("pattern_scale", l.Scale);
        // Paint is less shiny than bare metal.
        m.SetShaderParameter("metal", Mathf.Min(def.Metallic, 0.45f));
        m.SetShaderParameter("rough", Mathf.Max(def.Roughness, 0.55f));
        m.SetShaderParameter("ship", waterline.HasValue);
        m.SetShaderParameter("waterline", waterline ?? 0f);
        m.SetShaderParameter("boot_half", Liveries.BootTopHalfM);
        m.SetShaderParameter("antifouling", C(Liveries.AntiFouling));
        m.SetShaderParameter("boot_top", C(Liveries.BootTop));
        m.SetShaderParameter("deck_color", C(Liveries.Deck));
    }

    /// <summary>Hull plating in a paint job (shaded by the surface's corner shading, with seams and grime).</summary>
    public static ShaderMaterial Plating(BlockDef def, Livery l, float? waterline) =>
        Make($"plate:{def.Id}:{l.Id}:{waterline}", false, m =>
        {
            SetLivery(m, l, def, waterline);
            m.SetShaderParameter("use_shade", true);
            m.SetShaderParameter("panel_lines", def.Kind == BlockKind.Armor ? 1f : 0f);
            // The surface's corner shading averages a bit under 1, as with the plain materials.
            m.SetShaderParameter("darken", HullMesher.ShadeCompensation);
        });

    /// <summary>A painted part of a block (each part takes the pattern at its own position).</summary>
    public static ShaderMaterial Part(BlockDef def, Livery l, bool dark, float? waterline) =>
        Make($"part:{def.Id}:{l.Id}:{dark}:{waterline}", true, m =>
        {
            SetLivery(m, l, def, waterline);
            m.SetShaderParameter("per_part", true);
            m.SetShaderParameter("darken", dark ? 0.6f : 1f);
        });

    /// <summary>Glazing: dark glass in frames, the frames in the paint job's main color (or dark steel unpainted).</summary>
    public static ShaderMaterial Glass(BlockDef def, Livery l, bool plating) =>
        Make($"glass:{def.Id}:{l?.Id}:{plating}", !plating, m =>
        {
            m.SetShaderParameter("glass", true);
            m.SetShaderParameter("use_shade", plating);
            m.SetShaderParameter("glass_color", new Color(def.ColorR, def.ColorG, def.ColorB));
            m.SetShaderParameter("frame_color", l == null || l.IsFactory ? new Color(0.2f, 0.21f, 0.22f) : C(l.Base));
        });

    /// <summary>The far model's color for a point of plating or a part.</summary>
    public static Color FarColor(BlockDef def, Livery l, float? waterline, NVec p, NVec normal, bool plating, Color own)
    {
        if (def.Glass) return new Color(def.ColorR, def.ColorG, def.ColorB);
        if (l == null || l.IsFactory) return own;
        NVec c = waterline.HasValue ? Liveries.ShipColorAt(l, p, normal, waterline.Value, plating) : Liveries.ColorAt(l, p);
        return new Color(c.X, c.Y, c.Z);
    }
}
