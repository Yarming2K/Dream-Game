using System.Collections.Generic;
using Godot;

namespace DreamGame.Game.Audio;

public enum SoundId
{
    Cannon37,
    Cannon76,
    Cannon120,
    ExplosionSmall,
    ExplosionMedium,
    ExplosionLarge,
    Penetration,
    Clang,
    Ricochet,
    Dirt,
    PlaceLight,
    PlaceHeavy,
    Remove,
    Tick,
    Error,
    Reload,
    Engine,
    Rifle,
    RocketLaunch,
    BulletMetal,
    BulletDirt,
    Step1,
    Step2,
    Step3,
    Hurt,
    Weld,
    Splash,
    RailShot,
    LaserZap,
    ExplosionHuge,
}

/// <summary>Generates, caches and plays sound effects, and sets up the audio buses (outdoor reverb, limiter).</summary>
public static class Sfx
{
    public const float SpeedOfSound = 343f;
    private static readonly Dictionary<SoundId, AudioStreamWav> Cache = new();
    private static bool _busesReady;

    public static void SetupBuses()
    {
        if (_busesReady) return;
        _busesReady = true;

        AudioServer.SetBusVolumeDb(0, -3f);
        AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -0.8f });

        AudioServer.AddBus();
        int world = AudioServer.BusCount - 1;
        AudioServer.SetBusName(world, "World");
        AudioServer.SetBusSend(world, "Master");
        AudioServer.AddBusEffect(world, new AudioEffectReverb
        {
            RoomSize = 0.75f, Damping = 0.55f, Spread = 0.9f, Hipass = 0.15f, Dry = 1f, Wet = 0.14f, PredelayMsec = 40f,
        });

        AudioServer.AddBus();
        int ui = AudioServer.BusCount - 1;
        AudioServer.SetBusName(ui, "UI");
        AudioServer.SetBusSend(ui, "Master");
    }

    public static AudioStreamWav Get(SoundId id)
    {
        if (Cache.TryGetValue(id, out AudioStreamWav s)) return s;
        float[] samples = id switch
        {
            SoundId.Cannon37 => Synth.Cannon(0.7f, 21),
            SoundId.Cannon76 => Synth.Cannon(1f, 22),
            SoundId.Cannon120 => Synth.Cannon(1.35f, 23),
            SoundId.ExplosionSmall => Synth.Explosion(0.6f, 31),
            SoundId.ExplosionMedium => Synth.Explosion(1f, 32),
            SoundId.ExplosionLarge => Synth.Explosion(1.6f, 33),
            SoundId.Penetration => Synth.Penetration(),
            SoundId.Clang => Synth.Clang(),
            SoundId.Ricochet => Synth.Ricochet(),
            SoundId.Dirt => Synth.Dirt(),
            SoundId.PlaceLight => Synth.Place(0.1f),
            SoundId.PlaceHeavy => Synth.Place(0.9f),
            SoundId.Remove => Synth.Remove(),
            SoundId.Tick => Synth.Tick(),
            SoundId.Error => Synth.Error(),
            SoundId.Reload => Synth.Reload(),
            SoundId.Engine => Synth.EngineLoop(),
            SoundId.Rifle => Synth.Rifle(),
            SoundId.RocketLaunch => Synth.RocketLaunch(),
            SoundId.BulletMetal => Synth.BulletMetal(),
            SoundId.BulletDirt => Synth.BulletDirt(),
            SoundId.Step1 => Synth.Footstep(41),
            SoundId.Step2 => Synth.Footstep(42),
            SoundId.Step3 => Synth.Footstep(43),
            SoundId.Hurt => Synth.Hurt(),
            SoundId.Weld => Synth.WeldLoop(),
            SoundId.Splash => Synth.Splash(),
            SoundId.RailShot => Synth.RailShot(),
            SoundId.LaserZap => Synth.LaserZap(),
            SoundId.ExplosionHuge => Synth.Explosion(2.6f, 34),
            _ => Synth.Tick(),
        };

        s = new AudioStreamWav
        {
            Data = Synth.ToPcm16(samples),
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Synth.Rate,
            Stereo = false,
        };
        if (id == SoundId.Engine || id == SoundId.Weld)
        {
            s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            s.LoopBegin = 0;
            s.LoopEnd = samples.Length;
        }

        Cache[id] = s;
        return s;
    }

    /// <summary>Builds every sound up front so the first shot doesn't stutter.</summary>
    public static void Prewarm()
    {
        foreach (SoundId id in System.Enum.GetValues<SoundId>()) Get(id);
    }

    public static SoundId CannonFor(float caliberMm) =>
        caliberMm < 55 ? SoundId.Cannon37 : caliberMm < 100 ? SoundId.Cannon76 : SoundId.Cannon120;

    public static SoundId ExplosionFor(float radiusM) =>
        radiusM < 1.8f ? SoundId.ExplosionSmall : radiusM < 2.8f ? SoundId.ExplosionMedium : SoundId.ExplosionLarge;

    private const int MaxOfOneSound = 12;
    private static readonly System.Collections.Generic.Dictionary<SoundId, int> Playing = new();

    /// <summary>
    /// Plays a sound at a point in the world. Far-away sounds arrive late (sound is slower than light) and
    /// lose their highs, which sells distance.
    /// </summary>
    public static void At(Node3D world, Vector3 pos, SoundId id, float volumeDb = 0f, float pitch = 1f, float unitSize = 12f,
        bool delayByDistance = true)
    {
        if (world == null || !GodotObject.IsInstanceValid(world) || !world.IsInsideTree()) return;
        // A broadside or a flak battery asks for dozens of the same sound at once: a handful is as loud as it gets.
        if (Playing.GetValueOrDefault(id) >= MaxOfOneSound) return;
        Playing[id] = Playing.GetValueOrDefault(id) + 1;
        float delay = 0f;
        if (delayByDistance)
        {
            Camera3D cam = world.GetViewport().GetCamera3D();
            if (cam != null) delay = cam.GlobalPosition.DistanceTo(pos) / SpeedOfSound;
        }

        void Done() => Playing[id] = System.Math.Max(0, Playing.GetValueOrDefault(id) - 1);

        void Start()
        {
            if (!GodotObject.IsInstanceValid(world) || !world.IsInsideTree())
            {
                Done();
                return;
            }

            var p = new AudioStreamPlayer3D
            {
                Stream = Get(id), VolumeDb = volumeDb, PitchScale = pitch, UnitSize = unitSize, Bus = "World",
                AttenuationFilterCutoffHz = 9000f, AttenuationFilterDb = -18f, MaxDb = 6f,
            };
            world.AddChild(p);
            p.GlobalPosition = pos;
            p.Finished += p.QueueFree;
            p.TreeExiting += Done;
            p.Play();
        }

        if (delay > 0.03f) world.GetTree().CreateTimer(delay).Timeout += Start;
        else Start();
    }

    /// <summary>A sound with no position (builder and UI).</summary>
    public static void Play2D(Node owner, SoundId id, float volumeDb = 0f, float pitch = 1f)
    {
        if (owner == null || !owner.IsInsideTree()) return;
        var p = new AudioStreamPlayer { Stream = Get(id), VolumeDb = volumeDb, PitchScale = pitch, Bus = "UI" };
        owner.AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }

    /// <summary>A looping sound that follows a node (engines).</summary>
    public static AudioStreamPlayer3D Loop(Node3D attachTo, SoundId id, float volumeDb)
    {
        var p = new AudioStreamPlayer3D { Stream = Get(id), VolumeDb = volumeDb, UnitSize = 8f, Bus = "World", Autoplay = true };
        attachTo.AddChild(p);
        return p;
    }
}
