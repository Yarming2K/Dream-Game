using System.Collections.Generic;
using Godot;

namespace DreamGame.Game;

/// <summary>Something a shell or bullet can hit directly that isn't made of blocks (people, targets).</summary>
public interface IHittable
{
    void TakeHit(float damage, Vector3 point, Vector3 dir, bool byPlayer);
}

/// <summary>Something explosions hurt (people, targets). Damage is already scaled for distance.</summary>
public interface IBlastable
{
    Vector3 BlastCenter { get; }
    void TakeBlast(float damage, Vector3 from, bool byPlayer);
}

/// <summary>A person on the battlefield that crews can target.</summary>
public interface ICharacter : IHittable, IBlastable
{
    int Faction { get; }
    bool Alive { get; }

    /// <summary>Out in the open where a gunner could aim at them.</summary>
    bool Targetable { get; }
    Vector3 AimCenter { get; }
    Vector3 Velocity { get; }
    Rid BodyRid { get; }
}

public static class Hittables
{
    public static readonly List<IBlastable> Blastables = new();
    public static readonly List<ICharacter> Characters = new();

    public static void Add(IBlastable b)
    {
        if (!Blastables.Contains(b)) Blastables.Add(b);
        if (b is ICharacter c && !Characters.Contains(c)) Characters.Add(c);
    }

    public static void Remove(IBlastable b)
    {
        Blastables.Remove(b);
        if (b is ICharacter c) Characters.Remove(c);
    }
}
