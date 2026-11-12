using System;
using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>
/// What a jet engine does: its push at full throttle standing still, how that changes with speed, the afterburner,
/// and its fuel. The ready-made Jet Engine block has fixed figures (<see cref="Stock"/>); a jet built from parts
/// gets them from <see cref="JetDesign.Compute"/>.
/// </summary>
public struct JetStats
{
    /// <summary>Push at full throttle without the afterburner, standing still (N).</summary>
    public float DryThrustN;

    /// <summary>The afterburner multiplies the push by this (1: it has none).</summary>
    public float AfterburnerMult;

    /// <summary>Fuel at full throttle without the afterburner (litres a second); the afterburner multiplies it by <see cref="AfterburnerFuelMult"/>.</summary>
    public float FuelLps;
    public float AfterburnerFuelMult;

    public bool Stock, Fan, RampIntake, VariableNozzle;
    public int Compressors, Turbines, TurbinesNeeded, Afterburners;

    /// <summary>Weight of all its parts (kg) and how many cells long it is.</summary>
    public float MassKg;
    public int Length;

    public readonly bool HasAfterburner => AfterburnerMult > 1.001f;

    /// <summary>The ready-made Jet Engine block: 16 kN at any speed, 60% more on afterburner for three and a half times the fuel.</summary>
    public static JetStats ReadyMade => new()
    {
        DryThrustN = Aero.JetThrustN, AfterburnerMult = Aero.AfterburnerThrust, FuelLps = Aero.JetFuelPerSecond,
        AfterburnerFuelMult = Aero.AfterburnerFuel, Stock = true, Length = 1, MassKg = 600,
    };

    /// <summary>
    /// How the push changes with airspeed (1 standing still). Built jets gain a little from the air rammed in as
    /// they speed up; a plain intake chokes past the speed of sound and a fixed nozzle can't make use of it, while
    /// a ramp intake and a variable nozzle keep going. A fan's extra push fades as the speed rises.
    /// </summary>
    public readonly float SpeedFactor(float speedMs)
    {
        if (Stock) return 1f;
        float v = MathF.Max(speedMs, 0);
        float intake = 1f + JetDesign.RamGain * MathF.Min(v / 250f, 1f);
        intake += RampIntake
            ? JetDesign.RampGain * Math.Clamp((v - 250f) / 400f, 0f, 1f)
            : -JetDesign.ChokeLoss * Math.Clamp((v - JetDesign.ChokeSpeed) / 250f, 0f, 1f);
        float nozzle = VariableNozzle ? 1f : 1f - JetDesign.FixedNozzleLoss * Math.Clamp((v - 320f) / 250f, 0f, 1f);
        float fan = Fan ? JetDesign.FanBoost - (JetDesign.FanBoost - JetDesign.FanFloor) * Math.Clamp(v / 260f, 0f, 1f) : 1f;
        return MathF.Max(intake, 0.15f) * nozzle * fan;
    }

    /// <summary>Push (N) at a throttle setting and airspeed, with the afterburner lit or not.</summary>
    public readonly float Thrust(float throttle, float speedMs, bool afterburner) =>
        DryThrustN * Math.Clamp(throttle, 0f, 1f) * SpeedFactor(speedMs) * (afterburner && HasAfterburner ? AfterburnerMult : 1f);

    /// <summary>Fuel (litres a second) at a throttle setting, with the afterburner lit or not.</summary>
    public readonly float FuelPerSecond(float throttle, bool afterburner) =>
        FuelLps * Math.Clamp(throttle, 0f, 1f) * (afterburner && HasAfterburner ? AfterburnerFuelMult : 1f);
}

/// <summary>
/// Jets built from parts, in a line through a Jet Core like a gun's barrel through its breech. The core's arrow
/// points the way it pushes; in front of it (the arrow side): compressor stages, then a fan if any, then the intake.
/// Behind it: turbine stages, then afterburner sections if any, then the nozzle.
/// <para>Compressors give more push and use fuel better, each a little less than the last. Turbines drive them, one
/// for every two compressor stages and one more for a fan; too few and the engine can't keep its compressors
/// turning, so it runs weak (and, once engines heat up, hot). A fan adds a lot of push at low speed for much less
/// fuel but fades fast, so turbofans suit bombers and transports and turbojets suit fighters.</para>
/// </summary>
public static class JetDesign
{
    public const float CoreThrustN = 10000f;
    public const float CompressorGain = 0.6f, CompressorFalloff = 0.8f;

    /// <summary>Fuel (litres a second per kN of push) of a core with no compressors, and how much compressors save at most.</summary>
    public const float BaseFuelPerKn = 0.07f, CompressorFuelSaving = 0.45f;

    /// <summary>An intake with a block right in front of it: this much of the push is left.</summary>
    public const float BlockedIntake = 0.3f;

    /// <summary>A missing turbine's effect: push falls toward this fraction.</summary>
    public const float NoTurbineThrust = 0.4f;

    /// <summary>Each turbine more than needed takes a little energy out of the gas for nothing.</summary>
    public const float SpareTurbineLoss = 0.04f;

    public const float FanBoost = 1.45f, FanFloor = 0.65f, FanFuel = 0.7f;
    public const float RamGain = 0.25f, RampGain = 0.35f, ChokeLoss = 1.5f, ChokeSpeed = 290f, FixedNozzleLoss = 0.4f;

    /// <summary>Afterburner: the first section adds 55%, each one after it less.</summary>
    public const float AfterburnerGain = 0.55f, AfterburnerFalloff = 0.6f;

    /// <summary>A fixed nozzle can't open up for the afterburner, so it gets only this share of the extra push.</summary>
    public const float FixedNozzleAfterburner = 0.5f;

    /// <summary>The sum 1 + f + f² + ... with <paramref name="n"/> terms (diminishing returns).</summary>
    private static float Series(int n, float f) => n <= 0 ? 0 : (1f - MathF.Pow(f, n)) / (1f - f);

    /// <summary>
    /// The figures of a jet built from these parts. <paramref name="front"/> runs from the core forward (compressors,
    /// then a fan, then the intake); <paramref name="back"/> from the core backward (turbines, afterburners, nozzle).
    /// <paramref name="problem"/> says what's wrong when it can't run (its push is then zero) or what holds it back.
    /// </summary>
    public static JetStats Compute(BlockDef core, IReadOnlyList<BlockDef> front, IReadOnlyList<BlockDef> back, out string problem, out bool runs,
        bool intakeBlocked = false)
    {
        problem = null;
        runs = false;
        var s = new JetStats { MassKg = core.MassKg, Length = 1 + front.Count + back.Count, AfterburnerMult = 1f, AfterburnerFuelMult = 1f };
        bool intake = false, nozzle = false;
        // Front: compressors, then a fan, then the intake, in that order.
        int stage = 0;
        foreach (BlockDef d in front)
        {
            s.MassKg += d.MassKg;
            int order = d.Kind switch { BlockKind.JetCompressor => 0, BlockKind.JetFan => 1, BlockKind.JetIntake => 2, _ => -1 };
            if (order < 0 || order < stage || intake)
            {
                problem ??= $"The {d.Name} is in the wrong place: in front of a Jet Core go compressor stages, then a fan if you want one, then the intake.";
                continue;
            }

            stage = order;
            switch (d.Kind)
            {
                case BlockKind.JetCompressor: s.Compressors++; break;
                case BlockKind.JetFan when s.Fan:
                    problem ??= "A jet only takes one fan.";
                    break;
                case BlockKind.JetFan: s.Fan = true; break;
                case BlockKind.JetIntake:
                    intake = true;
                    s.RampIntake = d.HighSpeed;
                    break;
            }
        }

        stage = 0;
        foreach (BlockDef d in back)
        {
            s.MassKg += d.MassKg;
            int order = d.Kind switch { BlockKind.JetTurbine => 0, BlockKind.JetAfterburner => 1, BlockKind.JetNozzle => 2, _ => -1 };
            if (order < 0 || order < stage || nozzle)
            {
                problem ??= $"The {d.Name} is in the wrong place: behind a Jet Core go turbine stages, then afterburner sections if you want them, then the nozzle.";
                continue;
            }

            stage = order;
            switch (d.Kind)
            {
                case BlockKind.JetTurbine: s.Turbines++; break;
                case BlockKind.JetAfterburner: s.Afterburners++; break;
                case BlockKind.JetNozzle:
                    nozzle = true;
                    s.VariableNozzle = d.HighSpeed;
                    break;
            }
        }

        s.TurbinesNeeded = Math.Max(1, (s.Compressors + 1) / 2 + (s.Fan ? 1 : 0));
        float compress = Series(s.Compressors, CompressorFalloff);
        float thrust = CoreThrustN * (1f + CompressorGain * compress);
        float perKn = BaseFuelPerKn * (1f - CompressorFuelSaving * compress / Series(40, CompressorFalloff));
        if (s.Turbines < s.TurbinesNeeded)
        {
            thrust *= NoTurbineThrust + (1f - NoTurbineThrust) * s.Turbines / s.TurbinesNeeded;
            problem ??= $"Its turbines can't keep its compressors turning: it has {s.Turbines} and needs {s.TurbinesNeeded} " +
                        "(one for every two compressor stages, and one more for a fan). Add turbine stages behind the core.";
        }
        else
        {
            thrust *= MathF.Pow(1f - SpareTurbineLoss, s.Turbines - s.TurbinesNeeded);
        }

        // An intake with a block right in front of it only gets the air that leaks round the edges.
        if (intakeBlocked && intake)
        {
            thrust *= BlockedIntake;
            problem ??= "Its intake is blocked by the block right in front of it, so it gets hardly any air. Leave the cell in front of the intake open.";
        }

        s.DryThrustN = thrust;
        s.FuelLps = perKn * thrust / 1000f * (s.Fan ? FanFuel : 1f);
        if (s.Afterburners > 0)
        {
            float gain = AfterburnerGain * Series(s.Afterburners, AfterburnerFalloff);
            s.AfterburnerMult = 1f + gain * (s.VariableNozzle ? 1f : FixedNozzleAfterburner);
            s.AfterburnerFuelMult = 1f + 2.5f * MathF.Pow(s.Afterburners, 0.7f);
        }

        if (!intake)
            problem = "It has no intake: put a Jet Intake (or Ramp Intake) at the front of the line, on the arrow side of the core.";
        else if (!nozzle)
            problem = "It has no nozzle: put a Jet Nozzle (or Variable Nozzle) at the back of the line.";
        runs = intake && nozzle;
        if (!runs) s.DryThrustN = 0;
        return s;
    }
}

/// <summary>
/// How hot a jet runs, and what that does. Run normally it settles well below its limit even at full throttle.
/// <b>Emergency power</b> gives 15% more push but heats it past its limit in about half a minute at full throttle;
/// past the limit it loses power, and further on it starts to hurt itself (and can catch fire). An engine without
/// enough turbines runs hot too. Heat is a share of the limit: 1 is the limit.
/// </summary>
public static class EngineHeat
{
    public const float EmergencyThrust = 1.15f;

    /// <summary>Past this heat the engine loses power; past <see cref="Damaging"/> it damages itself.</summary>
    public const float Limit = 1f, Damaging = 1.3f;

    /// <summary>Seconds to heat up toward where it settles, and to cool down.</summary>
    public const float HeatUpS = 25f, CoolDownS = 15f;

    /// <summary>Where the heat settles at a throttle setting (0 to 1).</summary>
    public static float Settles(float throttle, bool emergency, float turbineShortfall) =>
        Math.Clamp(throttle, 0f, 1f) * (0.55f + (emergency ? 0.95f : 0f) + 0.6f * Math.Clamp(turbineShortfall, 0f, 1f));

    /// <summary>The heat a step of <paramref name="dt"/> seconds later.</summary>
    public static float Step(float heat, float settles, float dt) =>
        heat + (settles - heat) * (1f - MathF.Exp(-dt / (settles > heat ? HeatUpS : CoolDownS)));

    /// <summary>The share of its push it still gives at this heat.</summary>
    public static float ThrustFactor(float heat) => heat <= Limit ? 1f : MathF.Max(0.5f, 1f - (heat - Limit) * 1.6f);

    /// <summary>Damage it does to its own core each second at this heat.</summary>
    public static float DamagePerSecond(float heat) => heat <= Damaging ? 0f : (heat - Damaging) * 40f;

    /// <summary>A built jet's missing turbines as a share of those it needs (0 for the ready-made jet).</summary>
    public static float Shortfall(JetStats s) =>
        s.Stock || s.TurbinesNeeded <= 0 ? 0f : MathF.Max(0f, (s.TurbinesNeeded - s.Turbines) / (float)s.TurbinesNeeded);
}
