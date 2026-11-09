using Godot;

namespace DreamGame.Game;

/// <summary>Sky, sun and post-processing shared by the builder and the test range.</summary>
public static class Scenery
{
    public static void AddSkyAndSun(Node3D parent, bool outdoors)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.32f, 0.45f, 0.62f),
            SkyHorizonColor = new Color(0.68f, 0.72f, 0.74f),
            GroundHorizonColor = new Color(0.55f, 0.55f, 0.52f),
            GroundBottomColor = new Color(0.2f, 0.2f, 0.19f),
            SunAngleMax = 20f,
        };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = outdoors ? 0.7f : 0.9f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 0.95f,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.6f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.05f,
            FogEnabled = outdoors,
            FogLightColor = new Color(0.66f, 0.7f, 0.73f),
            FogDensity = 0.0012f,
            FogSkyAffect = 0.3f,
        };
        parent.AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D
        {
            LightEnergy = 1.25f,
            LightColor = new Color(1f, 0.96f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = outdoors ? 220f : 60f,
            RotationDegrees = new Vector3(-48f, -35f, 0f),
        };
        parent.AddChild(sun);
    }
}
