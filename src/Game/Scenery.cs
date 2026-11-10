using Godot;

namespace DreamGame.Game;

/// <summary>Sky, sun and post-processing shared by the builder and the test range.</summary>
public static class Scenery
{
    public static Godot.Environment AddSkyAndSun(Node3D parent, bool outdoors)
    {
        // Outdoors: a hazy daytime sky. In the builder: a dark workshop backdrop so the design stands out.
        var sky = outdoors
            ? new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.32f, 0.45f, 0.62f),
                SkyHorizonColor = new Color(0.68f, 0.72f, 0.74f),
                // Below the horizon the sky is the far sea in haze, so where the water stops (far out, or seen
                // from high up) there's no grey band.
                GroundHorizonColor = new Color(0.6f, 0.66f, 0.7f),
                GroundBottomColor = new Color(0.1f, 0.2f, 0.27f),
                SunAngleMax = 20f,
            }
            : new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.05f, 0.06f, 0.07f),
                SkyHorizonColor = new Color(0.17f, 0.19f, 0.21f),
                GroundHorizonColor = new Color(0.14f, 0.15f, 0.16f),
                GroundBottomColor = new Color(0.04f, 0.04f, 0.045f),
                SunAngleMax = 0f,
                SkyEnergyMultiplier = 1.2f,
            };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = outdoors ? 0.7f : 1.1f,
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
            // Two shadow splits instead of four: everything that casts a shadow is drawn once per split, which
            // with big machines in view was most of the frame.
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowSplit1 = 0.18f,
            DirectionalShadowMaxDistance = outdoors ? 200f : 60f,
            RotationDegrees = new Vector3(-48f, -35f, 0f),
        };
        parent.AddChild(sun);

        if (!outdoors)
        {
            // A cool rim light from behind picks out edges and silhouettes.
            parent.AddChild(new DirectionalLight3D
            {
                LightEnergy = 0.6f, LightColor = new Color(0.65f, 0.78f, 1f), ShadowEnabled = false,
                RotationDegrees = new Vector3(-20f, 150f, 0f),
            });
        }

        return env;
    }
}
