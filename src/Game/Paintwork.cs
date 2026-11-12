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
// Weathering (the game's look: worn, chipped, dirty). finish: 0 paint, 1 bare metal, 2 rubber, concrete and the like.
uniform sampler2D wear_tex : filter_linear_mipmap, repeat_enable;
uniform int finish = 0;
// Building materials drawn into the surface: 1 brick courses, 2 timber boards, 3 clay roof tiles, 4 board-formed
// concrete, 5 earth grassed over on top, 6 corrugated sheet steel.
uniform int masonry = 0;
instance uniform float wear_amount = 0.7;
instance uniform float mud_amount = 0.5;
global uniform float wetness;

varying vec3 lpos;
varying vec3 lnrm;
varying vec3 ppos;
// The surface's facing in the vehicle's frame (a part's own normal turned the way its block is turned).
varying vec3 vnrm;

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

// A noise texture wrapped round a point from all three sides, blended by the surface's facing.
float tri(vec3 p, vec3 n, float s) {
	vec3 w = n * n;
	w *= w;
	w /= (w.x + w.y + w.z);
	return texture(wear_tex, p.zy * s).r * w.x + texture(wear_tex, p.xz * s).r * w.y + texture(wear_tex, p.xy * s).r * w.z;
}

// Wear and tear on top of the paint: chipped edges down to primer and bare steel, dirt in the creases, rust
// running down the sides, sun-faded tops and mud thrown up low down. The chamfered edges (marked by the
// meshes, 1 - vertex alpha) are where it chips first.
void weather(inout vec3 col, inout float m, inout float r, vec3 p, vec3 n, float edge, float wear, float mud, float shade) {
	float fine = tri(p, n, 2.3);
	float coarse = tri(p + vec3(5.3, 1.7, 9.1), n, 0.37);
	if (finish == 0) {
		float chip = edge * smoothstep(0.66 - wear * 0.3, 0.7 - wear * 0.24, fine)
			+ smoothstep(0.9 - wear * 0.05, 0.93 - wear * 0.04, fine * 0.5 + coarse * 0.5);
		chip = clamp(chip, 0.0, 1.0);
		float deep = smoothstep(0.72, 0.84, fine);
		col = mix(col, mix(vec3(0.3, 0.19, 0.13), vec3(0.4, 0.39, 0.37), deep), chip);
		m = mix(m, 0.8, chip * deep);
		r = mix(r, 0.4, chip * deep);
		// Paint bleached by the sun on top.
		float top = smoothstep(0.6, 0.95, n.y) * wear * 0.22;
		col = mix(col, vec3(dot(col, vec3(0.333))) * 1.1, top);
	}
	// Uneven grime, heavier in the creases.
	col *= mix(1.0, 0.8 + 0.32 * coarse, wear * 0.6);
	col *= mix(1.0, smoothstep(0.5, 0.95, shade), wear * 0.45);
	if (finish <= 1) {
		// Rust streaks running down from edges and fittings.
		float streak = vnoise(vec3(p.x * 8.0, p.y * 0.55, p.z * 8.0)) * vnoise(vec3(p.x * 2.1, p.y * 0.22 + 3.0, p.z * 2.1));
		float rust = smoothstep(0.3, 0.48, streak) * (1.0 - abs(n.y)) * wear;
		col = mix(col, vec3(0.29, 0.14, 0.06), rust * 0.55);
		r = mix(r, 0.85, rust * 0.5);
	}
	if (mud > 0.0) {
		// Thick at the bottom, splashed higher up.
		float low = clamp((0.95 - p.y) / 0.95, 0.0, 1.0);
		float caked = smoothstep(0.38, 0.62, low + (coarse - 0.5) * 0.9 + (fine - 0.5) * 0.25) * mud;
		col = mix(col, vec3(0.15, 0.12, 0.08), caked * 0.9);
		r = mix(r, 0.96, caked);
		m = mix(m, 0.0, caked);
	}
}

float fbm(vec3 q) {
	return vnoise(q) * 0.6 + vnoise(q * 2.03 + vec3(17.0, 3.0, 9.0)) * 0.3 + vnoise(q * 4.07 + vec3(-7.0, 11.0, 5.0)) * 0.1;
}

// Brick laid in courses with mortar (each brick its own tone), boards with grain and seams, or rows of rounded
// roof tiles, each row laid over the joints of the one below.
// w: how much surface (m) one pixel covers. Fine patterns fade to their average as it grows, so distant walls
// and roofs don't shimmer with rings (moiré).
vec3 masonry_color(vec3 col, vec3 p, vec3 n, vec2 uv, float w) {
	vec3 an = abs(n);
	if (masonry == 1) {
		if (an.y > 0.7) {
			return col * (0.9 + 0.15 * vnoise(p * 9.0));
		}
		float row = floor(uv.y / 0.075);
		float u = uv.x / 0.225 + mod(row, 2.0) * 0.5;
		vec2 f = vec2(fract(u) * 0.225, fract(uv.y / 0.075) * 0.075);
		float fade = smoothstep(0.008, 0.03, w);
		float mortar = (1.0 - smoothstep(0.006 - 0.5 * w, 0.012 + 0.5 * w, min(f.x, f.y))) * (1.0 - fade);
		float tone = mix(h01(ivec3(int(floor(u)), int(row), 7)), 0.5, fade);
		col *= 0.78 + 0.34 * tone;
		return mix(col, vec3(0.6, 0.58, 0.53), mortar * 0.8 + 0.13 * fade);
	}
	if (masonry == 2) {
		float b = uv.x / 0.15;
		float seam = (1.0 - smoothstep(0.02, 0.06, min(fract(b), 1.0 - fract(b)))) * (1.0 - smoothstep(0.006, 0.02, w));
		float grain = mix(vnoise(vec3(uv.x * 60.0, uv.y * 3.0, floor(b) * 7.0)), 0.5, smoothstep(0.003, 0.012, w));
		col *= (0.78 + 0.3 * grain) * (0.9 + 0.2 * h01(ivec3(int(floor(b)), 3, 11)));
		return col * (1.0 - seam * 0.45);
	}
	if (masonry == 4) {
		// Concrete poured against timber boards: the planks' faint marks and seams, a darker line where each pour
		// stopped, and the holes the formwork ties left. Tops were trowelled smooth.
		if (an.y > 0.7) {
			return col * (0.93 + 0.12 * vnoise(p * 3.0));
		}
		float fade = smoothstep(0.006, 0.025, w);
		float board = floor(p.y / 0.15);
		float fb = fract(p.y / 0.15);
		float seam = (1.0 - smoothstep(0.0, 0.07, min(fb, 1.0 - fb))) * (1.0 - fade);
		col *= 0.94 + 0.1 * mix(h01(ivec3(int(board), int(floor(uv.x / 2.4)), 13)), 0.5, fade);
		col *= 1.0 - 0.1 * seam;
		float lift = 1.0 - smoothstep(0.006, 0.016 + w, abs(fract(p.y / 1.2 + 0.5) - 0.5) * 1.2);
		vec2 t = abs(fract(vec2(uv.x / 0.6, p.y / 0.6 + 0.25)) - 0.5) * 0.6;
		float hole = 1.0 - smoothstep(0.011, 0.018 + w, length(t));
		return col * (1.0 - (0.14 * lift + 0.35 * hole) * (1.0 - fade));
	}
	if (masonry == 5) {
		// Packed earth: grass over the tops and gentle slopes, bare soil down the steep sides, both patchy.
		float n1 = fbm(p * 1.3);
		float n2 = vnoise(p * 7.0);
		vec3 soil = col * (0.8 + 0.35 * n2);
		vec3 grass = mix(vec3(0.2, 0.28, 0.11), vec3(0.32, 0.35, 0.16), n1) * (0.85 + 0.25 * n2);
		return mix(soil, grass, smoothstep(0.35, 0.7, n.y + (n1 - 0.5) * 0.4));
	}
	if (masonry == 6) {
		// Corrugated sheet: ridges 7.6 cm apart running up the walls and down the roofs, sheets lapped every 0.9 m.
		float across = an.x > an.z ? p.z : p.x;
		float fade = smoothstep(0.004, 0.02, w);
		col *= 1.0 + 0.16 * sin(across / 0.076 * 6.2831853) * (1.0 - fade);
		float lap = 1.0 - smoothstep(0.004, 0.012 + w, abs(fract(across / 0.9 + 0.5) - 0.5) * 0.9);
		return col * (1.0 - 0.2 * lap * (1.0 - fade));
	}
	float u = an.x > an.z ? p.z : p.x;
	float row = floor(p.y / 0.11);
	float cu = u / 0.2 + mod(row, 2.0) * 0.5;
	float curve = sin(fract(cu) * 3.14159);
	col *= (0.7 + 0.35 * curve) * (0.85 + 0.25 * h01(ivec3(int(floor(cu)), int(row), 5)));
	float band = mix(1.0 - smoothstep(0.0, 0.12, fract(p.y / 0.11)), 0.06, smoothstep(0.01, 0.035, w));
	return col * (1.0 - 0.3 * band);
}

vec3 pattern_color(vec3 p, vec3 nv) {
	if (pattern <= 1) {
		return color_a;
	}
	if (pattern == 5) {
		// Countershading: pale on whatever faces the ground, soft patches of two colors elsewhere.
		if (nv.y < -0.3) {
			return color_c;
		}
		return fbm(p / pattern_scale) >= 0.55 ? color_b : color_a;
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
	vnrm = per_part ? orient_basis(int(INSTANCE_CUSTOM.w + 0.5)) * NORMAL : NORMAL;
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
		GLASS_ALPHA
	} else {
		vec3 p = per_part ? ppos : lpos;
		col = pattern_color(p, normalize(vnrm));
		if (ship) {
			if (p.y < waterline - boot_half) {
				col = antifouling;
			} else if (p.y < waterline + boot_half) {
				col = boot_top;
			} else if (!per_part && n.y > 0.75) {
				col = deck_color;
			}
		}
		if (masonry > 0) {
			col = masonry_color(col, p, n, uv, max(max(fwidth(uv.x), fwidth(uv.y)), fwidth(p.y)));
		}
		if (panel_lines > 0.0) {
			// Seams between the plates every meter, and streaks of grime running down the sides.
			vec2 f = fract(uv - 0.25);
			vec2 e = min(f, 1.0 - f);
			float seam = (1.0 - smoothstep(0.004, 0.011, min(e.x, e.y))) * (1.0 - smoothstep(0.008, 0.03, fwidth(uv.x) + fwidth(uv.y)));
			col *= 1.0 - seam * 0.3 * panel_lines;
			float streak = vnoise(vec3(lpos.x * 7.0, lpos.y * 0.6, lpos.z * 7.0));
			col *= mix(1.0, 0.86 + 0.14 * streak, (1.0 - an.y) * panel_lines);
		}
		col *= darken;
		if (wear_amount > 0.0) {
			float shade = use_shade ? COLOR.r : 1.0;
			weather(col, m, r, p, n, 1.0 - COLOR.a, wear_amount, mud_amount, shade);
		}
	}
	if (!glass) {
		// Rain: darker, glossier paint.
		col *= 1.0 - 0.18 * wetness;
		r = mix(r, 0.22, wetness * 0.6);
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

    private static Shader _oneSided, _twoSided, _glassOne, _glassTwo;
    private static readonly Dictionary<string, ShaderMaterial> Cache = new();

    private static Shader ShaderFor(bool twoSided, bool glass = false)
    {
        if (glass)
            return twoSided ? _glassTwo ??= new Shader { Code = Source("cull_disabled", true) } : _glassOne ??= new Shader { Code = Source("cull_back", true) };
        if (twoSided) return _twoSided ??= new Shader { Code = Source("cull_disabled") };
        return _oneSided ??= new Shader { Code = Source("cull_back") };
    }

    /// <summary>
    /// The shader with its cull mode and the table of block turns filled in (columns of each turn's matrix).
    /// Glass gets its own copy whose panes let light through (see-through windows); everything else stays
    /// opaque, which is cheaper and sorts properly.
    /// </summary>
    private static string Source(string cull, bool glass = false)
    {
        var mats = new List<string>();
        for (int o = 0; o < Orientations.Count; o++)
        {
            int[] m = Orientations.Matrix(o);
            string Col(int j) => $"vec3({m[j]}.0, {m[3 + j]}.0, {m[6 + j]}.0)";
            mats.Add($"mat3({Col(0)}, {Col(1)}, {Col(2)})");
        }

        return Code.Replace("CULL", glass ? cull + ", depth_draw_opaque" : cull).Replace("ORIENT_TABLE", string.Join(", ", mats))
            .Replace("GLASS_ALPHA", glass ? "ALPHA = mix(0.24, 1.0, bar); m = mix(0.08, 0.5, bar); r = mix(0.05, 0.55, bar);" : "");
    }

    /// <summary>What a painted part's batch carries per instance: where it is and how its block is turned.</summary>
    public static Color PartData(in Transform3D xf, int orientation) => new(xf.Origin.X, xf.Origin.Y, xf.Origin.Z, orientation);

    private static Color C(NVec v) => new(v.X, v.Y, v.Z);

    private static int PatternIndex(Livery l) => l.Pattern switch
    {
        LiveryPattern.Blotches => 2,
        LiveryPattern.Digital => 3,
        LiveryPattern.Stripes => 4,
        LiveryPattern.Countershade => 5,
        _ => 1,
    };

    private static NoiseTexture2D _wear;

    /// <summary>The blotchy noise the weathering cuts its chips, grime and mud from (seamless, made at start-up).</summary>
    private static NoiseTexture2D WearTexture => _wear ??= new NoiseTexture2D
    {
        Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
        Noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.012f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 6, FractalGain = 0.55f, Seed = 7,
        },
    };

    /// <summary>What a surface is, for weathering: paint chips, bare metal only rusts, rubber and concrete just get dirty.</summary>
    public enum Finish { Paint = 0, Metal = 1, Other = 2 }

    private static ShaderMaterial Make(string key, bool twoSided, System.Action<ShaderMaterial> setup, bool glass = false)
    {
        if (Cache.TryGetValue(key, out ShaderMaterial m)) return m;
        m = new ShaderMaterial { Shader = ShaderFor(twoSided, glass) };
        m.SetShaderParameter("grit", BlockVisuals.GritTexture);
        m.SetShaderParameter("wear_tex", WearTexture);
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
        // Paint is less shiny than bare metal; a bare-metal finish is the metal itself.
        m.SetShaderParameter("metal", l.IsBareMetal ? l.Metal : Mathf.Min(def.Metallic, 0.45f));
        m.SetShaderParameter("rough", l.IsBareMetal ? l.Rough : Mathf.Max(def.Roughness, 0.55f));
        m.SetShaderParameter("finish", (int)(l.IsBareMetal ? Finish.Metal : Finish.Paint));
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
            m.SetShaderParameter("panel_lines", def.Kind == BlockKind.Armor && MasonryOf(def) == 0 ? 1f : 0f);
            m.SetShaderParameter("masonry", MasonryOf(def));
            m.SetShaderParameter("finish", (int)(l.IsBareMetal && FinishOf(def) == Finish.Paint ? Finish.Metal : FinishOf(def)));
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

    private static string BaseOf(BlockDef def) => def.BaseId ?? def.Id;

    /// <summary>Concrete, brick, timber, tiles and earth don't chip like paint: they only get dirty.</summary>
    private static Finish FinishOf(BlockDef def) => BaseOf(def) is "concrete" or "brick" or "timber" or "roof_tile" or "earth" ? Finish.Other : Finish.Paint;

    private static int MasonryOf(BlockDef def) => BaseOf(def) switch
    {
        "brick" => 1, "timber" => 2, "roof_tile" => 3, "concrete" => 4, "earth" => 5, "corrugated" => 6, _ => 0,
    };

    /// <summary>Unpainted hull plating: the block's own color, worn like everything else.</summary>
    public static ShaderMaterial PlainPlating(BlockDef def) =>
        Make($"plainplate:{def.Id}", false, m =>
        {
            m.SetShaderParameter("color_a", new Color(def.ColorR, def.ColorG, def.ColorB));
            m.SetShaderParameter("pattern", 1);
            m.SetShaderParameter("metal", def.Metallic);
            m.SetShaderParameter("rough", def.Roughness);
            m.SetShaderParameter("use_shade", true);
            m.SetShaderParameter("panel_lines", def.Kind == BlockKind.Armor && FinishOf(def) == Finish.Paint && MasonryOf(def) == 0 ? 0.6f : 0f);
            m.SetShaderParameter("darken", HullMesher.ShadeCompensation);
            m.SetShaderParameter("finish", (int)FinishOf(def));
            m.SetShaderParameter("masonry", MasonryOf(def));
        });

    /// <summary>An unpainted part: a color and a finish (painted, bare metal, or rubber and the like).</summary>
    public static ShaderMaterial PlainPart(string key, Color color, float metallic, float roughness, Finish finish) =>
        Make($"plainpart:{key}", true, m =>
        {
            m.SetShaderParameter("color_a", new Color(color.R, color.G, color.B));
            m.SetShaderParameter("pattern", 1);
            m.SetShaderParameter("metal", metallic);
            m.SetShaderParameter("rough", roughness);
            m.SetShaderParameter("per_part", true);
            m.SetShaderParameter("finish", (int)finish);
        });

    /// <summary>A block's own unpainted look for its parts.</summary>
    public static ShaderMaterial PlainPart(BlockDef def, bool dark) =>
        PlainPart((dark ? "dark:" : "self:") + def.Id, new Color(def.ColorR, def.ColorG, def.ColorB) * (dark ? 0.6f : 1f), def.Metallic,
            def.Roughness + (dark ? 0.05f : 0f), FinishOf(def));

    /// <summary>Glazing: dark glass in frames, the frames in the paint job's main color (or dark steel unpainted).</summary>
    public static ShaderMaterial Glass(BlockDef def, Livery l, bool plating) =>
        Make($"glass:{def.Id}:{l?.Id}:{plating}", !plating, m =>
        {
            m.SetShaderParameter("glass", true);
            m.SetShaderParameter("use_shade", plating);
            m.SetShaderParameter("glass_color", new Color(def.ColorR, def.ColorG, def.ColorB));
            m.SetShaderParameter("frame_color", l == null || l.IsFactory ? new Color(0.2f, 0.21f, 0.22f) : C(l.Base));
        }, glass: true);

    /// <summary>The far model's color for a point of plating or a part.</summary>
    public static Color FarColor(BlockDef def, Livery l, float? waterline, NVec p, NVec normal, bool plating, Color own)
    {
        if (def.Glass) return new Color(def.ColorR, def.ColorG, def.ColorB);
        if (l == null || l.IsFactory) return own;
        NVec c = waterline.HasValue ? Liveries.ShipColorAt(l, p, normal, waterline.Value, plating) : Liveries.ColorAt(l, p, normal);
        return new Color(c.X, c.Y, c.Z);
    }
}
