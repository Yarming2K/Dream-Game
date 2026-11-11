using System;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Time of day and weather on the range. A sky of its own (gradient, sun glow, clouds that drift, stars at night,
/// the far sea below the horizon); the sun (or moon) moves and changes colour; fog, wind, wet ground and the sea
/// follow the weather. F6 steps through the times of day, F7 through the weather; changes blend over a few
/// seconds. The ground, paint, trees and sea read three shared settings (wetness, wind, sea_state).
/// </summary>
public partial class Atmosphere : Node3D
{
    public enum Time { Morning, Noon, Evening, Night }
    public enum Weather { Clear, Overcast, Rain, Fog }

    public Time Hour { get; private set; } = Time.Noon;
    public Weather Conditions { get; private set; } = Weather.Clear;

    /// <summary>The haze the range should use above water (it thins with height there).</summary>
    public float FogDensity { get; private set; } = 0.0012f;

    /// <summary>How wet everything is (0 dry to 1 soaked), for game code; the shaders read their own copy.</summary>
    public static float Wetness { get; private set; }
    public Color FogColor { get; private set; } = new(0.66f, 0.7f, 0.73f);

    private Godot.Environment _env;
    private DirectionalLight3D _sun;
    private ShaderMaterial _sky;
    private GpuParticles3D _rain;
    private float _wet, _cloudShift, _cloudTimer;
    private Look _now, _target;

    /// <summary>Everything a time and a weather set, blended between.</summary>
    private struct Look
    {
        public float Elevation, Azimuth, SunEnergy, Ambient, Cover, Fog, Wind, Sea, Stars;
        public Color SunColor, Zenith, Horizon, FogColor, Cloud;

        public static Look Lerp(Look a, Look b, float t) => new()
        {
            Elevation = Mathf.Lerp(a.Elevation, b.Elevation, t), Azimuth = Mathf.LerpAngle(a.Azimuth, b.Azimuth, t),
            SunEnergy = Mathf.Lerp(a.SunEnergy, b.SunEnergy, t), Ambient = Mathf.Lerp(a.Ambient, b.Ambient, t),
            Cover = Mathf.Lerp(a.Cover, b.Cover, t), Fog = Mathf.Lerp(a.Fog, b.Fog, t), Wind = Mathf.Lerp(a.Wind, b.Wind, t),
            Sea = Mathf.Lerp(a.Sea, b.Sea, t), Stars = Mathf.Lerp(a.Stars, b.Stars, t),
            SunColor = a.SunColor.Lerp(b.SunColor, t), Zenith = a.Zenith.Lerp(b.Zenith, t), Horizon = a.Horizon.Lerp(b.Horizon, t),
            FogColor = a.FogColor.Lerp(b.FogColor, t), Cloud = a.Cloud.Lerp(b.Cloud, t),
        };
    }

    private const string SkyCode = @"
shader_type sky;
render_mode use_debanding;

uniform vec3 zenith : source_color = vec3(0.32, 0.45, 0.62);
uniform vec3 horizon : source_color = vec3(0.68, 0.72, 0.74);
uniform vec3 sea_far : source_color = vec3(0.6, 0.66, 0.7);
uniform vec3 sea_deep : source_color = vec3(0.1, 0.2, 0.27);
uniform vec3 cloud_color : source_color = vec3(0.9, 0.9, 0.9);
uniform float cover = 0.3;
uniform float shift = 0.0;
uniform float stars = 0.0;
uniform sampler2D clouds : filter_linear_mipmap, repeat_enable;

void sky() {
	vec3 d = EYEDIR;
	if (d.y < 0.0) {
		// Below the horizon: the far sea in haze.
		COLOR = mix(sea_far, sea_deep, sqrt(min(-d.y * 6.0, 1.0)));
	} else {
		vec3 col = mix(zenith, horizon, pow(1.0 - d.y, 3.0));
		float sa = max(dot(LIGHT0_DIRECTION, d), 0.0);
		vec3 sun = LIGHT0_COLOR * LIGHT0_ENERGY;
		col += sun * pow(sa, 8.0) * 0.18;
		// Clouds on a dome overhead, lit from the sun's side.
		vec2 uv = d.xz / (d.y + 0.1) * 0.22 + vec2(shift, shift * 0.4);
		float c = texture(clouds, uv).r * 0.65 + texture(clouds, uv * 2.7 + vec2(0.3, 0.7)).r * 0.35;
		float cov = smoothstep(1.0 - cover, 1.0 - cover + 0.28, c) * smoothstep(0.0, 0.12, d.y);
		vec3 cc = cloud_color * (0.7 + 0.4 * c) + sun * pow(sa, 5.0) * 0.25;
		col = mix(col, cc, cov);
		if (!AT_CUBEMAP_PASS) {
			col += sun * smoothstep(0.9993, 0.9997, sa) * 4.0 * (1.0 - cov);
			if (stars > 0.0) {
				vec3 q = floor(d * 420.0);
				float h = fract(sin(dot(q, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
				col += vec3(smoothstep(0.9975, 1.0, h)) * stars * (1.0 - cov);
			}
		}
		COLOR = col;
	}
}
";

    public void Setup(Godot.Environment env, DirectionalLight3D sun)
    {
        _env = env;
        _sun = sun;
        _sky = new ShaderMaterial { Shader = new Shader { Code = SkyCode } };
        _sky.SetShaderParameter("clouds", new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.01f, FractalOctaves = 6, Seed = 21 },
        });
        _env.Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size64, ProcessMode = Sky.ProcessModeEnum.Incremental };
        _env.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;
        BuildRain();
        _now = _target = Compose(Hour, Conditions);
        Apply(_now);
    }

    public void Set(Time? time = null, Weather? weather = null)
    {
        Hour = time ?? Hour;
        Conditions = weather ?? Conditions;
        _target = Compose(Hour, Conditions);
    }

    public void NextTime() => Set(time: (Time)(((int)Hour + 1) % 4));
    public void NextWeather() => Set(weather: (Weather)(((int)Conditions + 1) % 4));

    public string Describe() => $"{Hour} · {Conditions}";

    private static Look Compose(Time t, Weather w)
    {
        Look l = t switch
        {
            Time.Morning => new Look
            {
                Elevation = 18, Azimuth = 100, SunEnergy = 1.0f, Ambient = 0.55f, SunColor = new Color(1f, 0.82f, 0.62f),
                Zenith = new Color(0.27f, 0.4f, 0.6f), Horizon = new Color(0.86f, 0.72f, 0.6f), FogColor = new Color(0.76f, 0.69f, 0.62f),
            },
            Time.Evening => new Look
            {
                Elevation = 9, Azimuth = 255, SunEnergy = 0.9f, Ambient = 0.45f, SunColor = new Color(1f, 0.6f, 0.36f),
                Zenith = new Color(0.2f, 0.25f, 0.42f), Horizon = new Color(0.95f, 0.56f, 0.36f), FogColor = new Color(0.7f, 0.55f, 0.46f),
            },
            Time.Night => new Look
            {
                Elevation = 40, Azimuth = 200, SunEnergy = 0.16f, Ambient = 0.3f, SunColor = new Color(0.55f, 0.65f, 0.9f), Stars = 1f,
                Zenith = new Color(0.008f, 0.012f, 0.03f), Horizon = new Color(0.04f, 0.05f, 0.08f), FogColor = new Color(0.04f, 0.05f, 0.07f),
            },
            _ => new Look
            {
                Elevation = 48, Azimuth = -35, SunEnergy = 1.25f, Ambient = 0.7f, SunColor = new Color(1f, 0.96f, 0.88f),
                Zenith = new Color(0.32f, 0.45f, 0.62f), Horizon = new Color(0.68f, 0.72f, 0.74f), FogColor = new Color(0.66f, 0.7f, 0.73f),
            },
        };
        l.Cloud = l.Horizon.Lerp(Colors.White, 0.55f) * (t == Time.Night ? 0.15f : 1f);
        l.Fog = 1f;
        l.Wind = 0.6f;
        l.Sea = 1f;
        l.Cover = 0.28f;
        Color grey = new Color(0.55f, 0.57f, 0.6f) * (t == Time.Night ? 0.08f : t == Time.Noon ? 1f : 0.75f);
        switch (w)
        {
            case Weather.Overcast:
                l.Cover = 0.88f;
                l.SunEnergy *= 0.35f;
                l.Fog = 1.6f;
                l.Wind = 1f;
                l.Sea = 1.4f;
                l.Zenith = l.Zenith.Lerp(grey, 0.7f);
                l.Horizon = l.Horizon.Lerp(grey, 0.6f);
                l.Cloud = grey * 1.1f;
                l.Stars *= 0.1f;
                break;
            case Weather.Rain:
                l.Cover = 1f;
                l.SunEnergy *= 0.2f;
                l.Ambient *= 0.8f;
                l.Fog = 2.6f;
                l.Wind = 1.7f;
                l.Sea = 2.1f;
                l.Zenith = l.Zenith.Lerp(grey * 0.6f, 0.85f);
                l.Horizon = l.Horizon.Lerp(grey * 0.75f, 0.8f);
                l.FogColor = l.FogColor.Lerp(grey * 0.8f, 0.6f);
                l.Cloud = grey * 0.7f;
                l.Stars = 0f;
                break;
            case Weather.Fog:
                l.Cover = 0.6f;
                l.SunEnergy *= 0.55f;
                l.Fog = 6f;
                l.Wind = 0.25f;
                l.Sea = 0.7f;
                l.Horizon = l.Horizon.Lerp(l.FogColor, 0.8f);
                l.Stars *= 0.2f;
                break;
        }

        return l;
    }

    public override void _Process(double delta)
    {
        if (_sky == null) return;
        float dt = (float)delta;
        _now = Look.Lerp(_now, _target, Mathf.Min(1f, dt * 0.8f));
        // Rain soaks things in about twenty seconds; they dry in about a minute.
        _wet = Mathf.MoveToward(_wet, Conditions == Weather.Rain ? 1f : 0f, dt * (Conditions == Weather.Rain ? 0.05f : 0.016f));
        // Clouds drift; the sky's lighting is redone in steps, not every frame.
        _cloudTimer += dt;
        if (_cloudTimer > 0.5f)
        {
            _cloudShift += _cloudTimer * 0.0015f * (0.4f + _now.Wind);
            _cloudTimer = 0;
        }

        Apply(_now);
        if (_rain != null)
        {
            _rain.Emitting = Conditions == Weather.Rain;
            Camera3D cam = GetViewport().GetCamera3D();
            if (cam != null) _rain.GlobalPosition = cam.GlobalPosition + Vector3.Up * 14f;
        }
    }

    private void Apply(Look l)
    {
        float el = Mathf.DegToRad(l.Elevation), az = Mathf.DegToRad(l.Azimuth);
        _sun.Rotation = new Vector3(-el, az, 0);
        _sun.LightColor = l.SunColor;
        _sun.LightEnergy = l.SunEnergy;
        _env.AmbientLightEnergy = l.Ambient;
        // The night sky is too dark to light anything by itself: a cool moonlit fill makes up most of the
        // ambient at night, so a tank's shadowed side reads as dark blue-grey, not a black cut-out.
        _env.AmbientLightColor = new Color(0.36f, 0.42f, 0.58f);
        _env.AmbientLightSkyContribution = Mathf.Lerp(1f, 0.3f, l.Stars);
        _sky.SetShaderParameter("zenith", l.Zenith);
        _sky.SetShaderParameter("horizon", l.Horizon);
        _sky.SetShaderParameter("sea_far", l.FogColor.Lerp(l.Horizon, 0.3f) * 0.95f);
        _sky.SetShaderParameter("sea_deep", l.FogColor * 0.25f + new Color(0.03f, 0.08f, 0.12f) * (Mathf.Lerp(l.Ambient, 0.14f, l.Stars) / 0.7f));
        _sky.SetShaderParameter("cloud_color", l.Cloud);
        _sky.SetShaderParameter("cover", l.Cover);
        _sky.SetShaderParameter("shift", _cloudShift);
        _sky.SetShaderParameter("stars", l.Stars);
        FogDensity = 0.0012f * l.Fog;
        FogColor = l.FogColor;
        RenderingServer.GlobalShaderParameterSet("wetness", _wet);
        Wetness = _wet;
        RenderingServer.GlobalShaderParameterSet("wind", l.Wind);
        RenderingServer.GlobalShaderParameterSet("sea_state", l.Sea);
    }

    /// <summary>Rain: streaks falling round the camera, slanted by the wind.</summary>
    private void BuildRain()
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(28f, 1f, 28f),
            Direction = new Vector3(0.15f, -1f, 0.05f),
            Spread = 2f,
            InitialVelocityMin = 22f, InitialVelocityMax = 26f,
            Gravity = new Vector3(0, -9.8f, 0),
        };
        var drop = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.75f, 0.8f, 0.85f, 0.32f),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
        };
        _rain = new GpuParticles3D
        {
            Amount = 7000, Lifetime = 1.4f, Emitting = false, LocalCoords = false, ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.018f, 0.7f), Material = drop },
            VisibilityAabb = new Aabb(new Vector3(-40, -40, -40), new Vector3(80, 60, 80)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_rain);
    }
}
