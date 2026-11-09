using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>A physics body made of blocks that shells can hit: a vehicle, a structure or debris.</summary>
public interface IGridHost
{
    GridModel Model { get; }
    GridView View { get; }
    PhysicsBody3D Body { get; }
    KeepRule Keep { get; }

    /// <summary>Radius around the body origin that contains every block, for quick hit checks.</summary>
    float Radius { get; }

    /// <summary>Velocity of a point on the body (debris inherits it when pieces break off).</summary>
    Vector3 VelocityAt(Vector3 worldPoint);

    /// <summary>Called after blocks were destroyed or broke away, so the host can update mass, wheels, guns and so on.</summary>
    void OnBlocksRemoved();

    /// <summary>Remove the host entirely (no blocks left).</summary>
    void Vanish();
}

/// <summary>Every live grid host in the world, for shell and explosion hit tests.</summary>
public static class GridRegistry
{
    public static readonly List<IGridHost> Hosts = new();

    public static void Add(IGridHost h)
    {
        if (!Hosts.Contains(h)) Hosts.Add(h);
    }

    public static void Remove(IGridHost h) => Hosts.Remove(h);

    public static class Layers
    {
        public const uint World = 1;
        public const uint Vehicles = 2;
        public const uint Structures = 4;
        public const uint Debris = 8;

        /// <summary>Little chunks from destroyed blocks. Shells, wheels and the camera ignore them.</summary>
        public const uint Fragments = 16;

        /// <summary>People (the player, later soldiers).</summary>
        public const uint Characters = 32;

        /// <summary>Pop-up practice targets.</summary>
        public const uint Targets = 64;

        public const uint All = World | Vehicles | Structures | Debris;
    }
}
