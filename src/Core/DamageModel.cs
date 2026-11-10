using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

public enum HitKind
{
    Penetration,
    NoPenetration,
    Ricochet,
    Explosion,
}

/// <summary>What happened at one block, for hit feedback on screen.</summary>
public struct HitReport
{
    public HitKind Kind;
    public string BlockName;
    public float PenMm;
    public float ArmorMm;
    public float AngleDeg;
    public float Damage;
    public bool Destroyed;
}

/// <summary>A shell in flight. Penetration drops as it passes through blocks.</summary>
public sealed class Shell
{
    public AmmoType Type;
    public float CaliberMm;
    public float PenMm;
    public float Damage;
    public float HeDamage;
    public float HeRadiusM;
    public float HePenMm;
    public bool Spent;

    public static Shell FromGun(in GunStats s, AmmoType type, float impactSpeed, BreechSpec breech)
    {
        return new Shell
        {
            Type = type,
            CaliberMm = s.CaliberMm,
            PenMm = type == AmmoType.AP ? GunMath.PenAtSpeed(breech, impactSpeed) : s.HePenMm,
            Damage = s.ApDamage,
            HeDamage = s.HeDamage,
            HeRadiusM = s.HeRadiusM,
            HePenMm = s.HePenMm,
        };
    }
}

/// <summary>One block along a shell's path, in the order the shell reaches them.</summary>
public struct OrderedHit
{
    public BlockInstance Block;

    /// <summary>Who owns the block (the engine side's grid object).</summary>
    public object Owner;

    /// <summary>Distance from the start of the shell's path, in meters.</summary>
    public float DistanceM;

    /// <summary>Angle between the shell's path and the surface normal, in degrees (0 = straight on).</summary>
    public float AngleDeg;
}

public static class DamageModel
{
    /// <summary>Shells hitting the outer surface at this angle or steeper bounce off.</summary>
    public const float RicochetAngleDeg = 72f;

    /// <summary>Line-of-sight thickness: armor / cos(angle), capped at 4x.</summary>
    public static float EffectiveArmor(float armorMm, float angleDeg)
    {
        float c = MathF.Cos(angleDeg * MathF.PI / 180f);
        return armorMm / MathF.Max(c, 0.25f);
    }

    public static float ImpactAngleDeg(Vector3 dir, Vector3 normal)
    {
        float c = Math.Clamp(-Vector3.Dot(Vector3.Normalize(dir), Vector3.Normalize(normal)), -1f, 1f);
        return MathF.Acos(MathF.Abs(c)) * 180f / MathF.PI;
    }

    /// <summary>Armor thickness that halves a laser's burn.</summary>
    public const float LaserArmorMm = 40f;

    /// <summary>
    /// What a laser pulse does to a block. A beam burns rather than punches, so armor slows it (thick plate
    /// takes seconds to burn through) but never stops it outright.
    /// </summary>
    public static float LaserBurn(BlockDef def, float damage) => damage * LaserArmorMm / (LaserArmorMm + def.ArmorMm);

    /// <summary>Deals damage; returns true only on the hit that destroys the block.</summary>
    public static bool ApplyDamage(BlockInstance b, float damage)
    {
        if (!b.Alive || damage <= 0) return false;
        b.Hp -= damage;
        if (b.Hp > 0) return false;
        b.Alive = false;
        return true;
    }

    /// <summary>
    /// Runs an AP shell through blocks in path order. Each block it beats takes damage and costs it that
    /// block's effective armor; it stops at the first block it can't beat, or bounces off a steep first hit.
    /// Returns the index of the hit where the shell stopped, or -1 if it went all the way through.
    /// </summary>
    public static int ResolveAp(Shell shell, IReadOnlyList<OrderedHit> hits, bool firstSurface,
        List<(OrderedHit hit, bool destroyed)> damaged, List<HitReport> reports)
    {
        for (int i = 0; i < hits.Count; i++)
        {
            OrderedHit h = hits[i];
            BlockInstance b = h.Block;
            if (!b.Alive) continue;
            // A shell crossing several cells of one big block meets its armor once.
            if (i > 0 && hits[i - 1].Block == b) continue;

            if (firstSurface && i == 0 && h.AngleDeg >= RicochetAngleDeg)
            {
                float dmg = shell.Damage * 0.1f;
                bool killed = ApplyDamage(b, dmg);
                damaged.Add((h, killed));
                reports.Add(new HitReport
                {
                    Kind = HitKind.Ricochet, BlockName = b.Def.Name, PenMm = shell.PenMm, ArmorMm = b.Def.ArmorMm,
                    AngleDeg = h.AngleDeg, Damage = dmg, Destroyed = killed,
                });
                shell.Spent = true;
                return i;
            }

            float eff = EffectiveArmor(b.Def.ArmorMm, h.AngleDeg);
            if (shell.PenMm >= eff)
            {
                float pen = shell.PenMm;
                bool killed = ApplyDamage(b, shell.Damage);
                damaged.Add((h, killed));
                reports.Add(new HitReport
                {
                    Kind = HitKind.Penetration, BlockName = b.Def.Name, PenMm = pen, ArmorMm = eff,
                    AngleDeg = h.AngleDeg, Damage = shell.Damage, Destroyed = killed,
                });
                shell.PenMm -= eff;
                shell.Damage *= 0.85f;
                firstSurface = false;
                if (shell.PenMm <= 1f)
                {
                    shell.Spent = true;
                    return i;
                }
            }
            else
            {
                float dmg = shell.Damage * 0.25f * (shell.PenMm / eff);
                bool killed = ApplyDamage(b, dmg);
                damaged.Add((h, killed));
                reports.Add(new HitReport
                {
                    Kind = HitKind.NoPenetration, BlockName = b.Def.Name, PenMm = shell.PenMm, ArmorMm = eff,
                    AngleDeg = h.AngleDeg, Damage = dmg, Destroyed = killed,
                });
                shell.Spent = true;
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Blast damage to one section. <paramref name="centerCells"/> is the explosion center in the section's
    /// cell coordinates. Damage falls off with distance, armor soaks up part of it, and composite takes extra.
    /// </summary>
    public static void ApplyBlast(Section section, Vector3 centerCells, float radiusM, float damage,
        List<BlockInstance> destroyed, List<BlockInstance> hurt = null)
    {
        float rCells = radiusM / Blueprint.CellSize;
        int reach = (int)MathF.Ceiling(rCells) + 1;
        Int3 c = Int3.Round(centerCells);
        // Iterate over whichever is smaller: the blast cube or the section.
        int cube = (2 * reach + 1) * (2 * reach + 1) * (2 * reach + 1);
        // A big block is in the blast once, however many of its cells are.
        var seen = new HashSet<BlockInstance>();
        if (cube < section.Cells.Count)
        {
            for (int x = -reach; x <= reach; x++)
            for (int y = -reach; y <= reach; y++)
            for (int z = -reach; z <= reach; z++)
            {
                if (section.Cells.TryGetValue(c + new Int3(x, y, z), out BlockInstance b) && seen.Add(b))
                    BlastOne(b, centerCells, rCells, damage, destroyed, hurt);
            }
        }
        else
        {
            var list = new List<BlockInstance>(section.Cells.Values);
            foreach (BlockInstance b in list)
                if (seen.Add(b)) BlastOne(b, centerCells, rCells, damage, destroyed, hurt);
        }
    }

    private static void BlastOne(BlockInstance b, Vector3 center, float rCells, float damage,
        List<BlockInstance> destroyed, List<BlockInstance> hurt)
    {
        if (!b.Alive) return;
        // Big blocks take the blast at their nearest cell.
        float d = float.MaxValue;
        foreach (Int3 cell in b.Cells) d = MathF.Min(d, Vector3.Distance(cell.ToVector3(), center));
        if (d >= rCells) return;
        float falloff = MathF.Pow(1f - d / rCells, 1.5f);
        float dmg = (damage * falloff - b.Def.ArmorMm * 1.5f) * b.Def.HeVulnerability;
        if (dmg <= 0) return;
        hurt?.Add(b);
        if (ApplyDamage(b, dmg)) destroyed.Add(b);
    }

    /// <summary>Explosion size when an ammo rack holding <paramref name="units"/> of shells goes up.</summary>
    public static (float damage, float radiusM) CookOff(float units)
    {
        if (units <= 0) return (0, 0);
        float damage = 120f + 55f * MathF.Sqrt(units);
        float radius = 1.2f + 0.25f * MathF.Sqrt(units);
        // A big gun's magazine going up is a different order of thing from a tank's ammo rack.
        bool magazine = units > 100;
        return (MathF.Min(damage, magazine ? 2500f : 520f), MathF.Min(radius, magazine ? 9f : 3.5f));
    }
}
