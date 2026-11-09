using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>Ready-made designs to drive, take apart and learn from, plus test-range targets.</summary>
public static class ExampleDesigns
{
    private sealed class Draft
    {
        private readonly Dictionary<Int3, BlueprintBlock> _cells = new();

        /// <summary>Places a block, replacing whatever is there.</summary>
        public void Set(int x, int y, int z, string id, int o = 0) =>
            _cells[new Int3(x, y, z)] = new BlueprintBlock(id, new Int3(x, y, z), o);

        /// <summary>Fills a box, skipping cells that already have something.</summary>
        public void Fill(int x0, int x1, int y0, int y1, int z0, int z1, string id, int o = 0)
        {
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                var c = new Int3(x, y, z);
                if (!_cells.ContainsKey(c)) _cells[c] = new BlueprintBlock(id, c, o);
            }
        }

        public Blueprint Build(string name) => Blueprint.FromCellMap(name, _cells.Values);
    }

    public static IReadOnlyList<Blueprint> All => new[] { Warden(), Jackal(), Bulwark() };

    /// <summary>Medium tank: sloped front, 76 mm gun in a turret with a gunner and commander, ammo stored low in the hull.</summary>
    public static Blueprint Warden()
    {
        var d = new Draft();
        for (int z = -5; z <= 4; z++)
        {
            d.Set(-3, 0, z, "track");
            d.Set(3, 0, z, "track");
        }

        // Crew, ammo and engine bay (middle layer).
        d.Set(-1, 1, -5, "driver_seat");
        d.Set(0, 1, -1, "ammo_feed");
        d.Set(-1, 1, -1, "ammo_ap");
        d.Set(1, 1, -1, "ammo_ap");
        d.Set(-1, 1, 0, "ammo_ap");
        d.Set(1, 1, 0, "ammo_ap");
        d.Set(-1, 1, -2, "ammo_he");
        d.Set(1, 1, -2, "ammo_he");
        d.Set(-1, 1, 3, "engine_large");
        d.Set(1, 1, 3, "engine_small");
        d.Set(0, 1, 3, "fuel_tank");
        d.Set(-2, 1, 3, "fuel_tank");
        d.Set(2, 1, 3, "fuel_tank");
        d.Set(0, 1, 4, "fuel_tank");

        // Front: a two-step 45° glacis of heavy plate.
        d.Fill(-2, 2, 0, 0, -6, -6, "armor_heavy");
        d.Fill(-3, 3, 1, 1, -6, -6, "armor_heavy_slope");
        d.Fill(-3, 3, 2, 2, -5, -5, "armor_heavy_slope");

        // Hull shell.
        d.Fill(-2, 2, 0, 0, 5, 5, "armor_light");
        d.Fill(-3, 3, 1, 1, 5, 5, "armor_light");
        d.Fill(-3, -3, 1, 1, -5, 4, "armor_light");
        d.Fill(3, 3, 1, 1, -5, 4, "armor_light");
        d.Set(0, 2, -1, "turret_ring");
        d.Fill(-3, 3, 2, 2, -4, 5, "armor_light");
        d.Fill(-2, 2, 0, 1, -5, 4, "frame");

        // Turret: 76 mm, six barrel segments and a muzzle brake.
        d.Set(0, 3, -1, "breech_76");
        for (int z = -2; z >= -7; z--) d.Set(0, 3, z, "barrel");
        d.Set(0, 3, -8, "muzzle_brake");
        d.Set(-1, 3, -1, "loader");
        d.Set(1, 3, -1, "loader");
        d.Set(0, 3, 0, "propellant");
        d.Set(1, 3, 0, "gunner_seat");
        d.Set(-1, 3, 0, "commander_seat");
        d.Fill(-2, 2, 3, 3, -3, -2, "armor_heavy");
        d.Fill(-2, -2, 3, 3, -1, 1, "armor_heavy");
        d.Fill(2, 2, 3, 3, -1, 1, "armor_heavy");
        d.Fill(-1, 1, 3, 3, 1, 1, "armor_light");
        d.Fill(-1, 1, 3, 3, 0, 0, "frame");
        d.Fill(-2, 2, 4, 4, -3, -3, "armor_heavy_slope");
        d.Fill(-2, 2, 4, 4, -2, 1, "armor_light");
        return d.Build("Warden (medium tank)");
    }

    /// <summary>Fast wheeled scout with a small 37 mm turret. No gunner seat: the driver aims.</summary>
    public static Blueprint Jackal()
    {
        var d = new Draft();
        foreach (int x in new[] { -2, 2 })
        {
            d.Set(x, 0, -3, "wheel_steer");
            d.Set(x, 0, 0, "wheel");
            d.Set(x, 0, 2, "wheel");
        }

        d.Set(-1, 1, -3, "driver_seat");
        d.Set(1, 1, -3, "fuel_tank");
        d.Set(0, 1, -1, "ammo_feed");
        d.Set(-1, 1, -1, "ammo_ap");
        d.Set(1, 1, -1, "ammo_ap");
        d.Set(-1, 1, 0, "ammo_he");
        d.Set(-1, 1, 2, "engine_small");
        d.Set(1, 1, 2, "engine_small");
        d.Set(0, 1, 2, "fuel_tank");

        d.Fill(-2, 2, 0, 1, -4, -4, "armor_light_slope");
        d.Fill(-2, 2, 2, 2, -3, -3, "armor_light_slope");
        d.Set(0, 2, -1, "turret_ring");
        d.Fill(-2, 2, 2, 2, -2, 3, "armor_light");
        d.Fill(-2, -2, 1, 1, -3, 3, "armor_light");
        d.Fill(2, 2, 1, 1, -3, 3, "armor_light");
        d.Fill(-1, 1, 1, 1, 3, 3, "armor_light");
        d.Fill(-2, 2, 0, 0, 3, 3, "armor_light");
        d.Fill(-1, 1, 0, 1, -3, 2, "frame");

        d.Set(0, 3, -1, "breech_37");
        for (int z = -2; z >= -5; z--) d.Set(0, 3, z, "barrel");
        d.Set(-1, 3, -1, "loader");
        d.Set(1, 3, -1, "propellant");
        d.Fill(-1, 1, 3, 3, -2, -2, "armor_light");
        d.Fill(-1, 1, 3, 3, 0, 0, "armor_light");
        d.Fill(-1, 1, 4, 4, -2, 0, "armor_light");
        return d.Build("Jackal (scout car)");
    }

    /// <summary>Tank destroyer: a long 120 mm gun fixed in a heavily armored casemate. Aim by turning the hull.</summary>
    public static Blueprint Bulwark()
    {
        var d = new Draft();
        for (int z = -5; z <= 5; z++)
        {
            d.Set(-3, 0, z, "track");
            d.Set(3, 0, z, "track");
        }

        d.Set(-2, 1, -5, "driver_seat");
        d.Set(0, 1, -2, "ammo_feed");
        d.Set(-1, 1, -2, "ammo_ap");
        d.Set(1, 1, -2, "ammo_ap");
        d.Set(-1, 1, -1, "ammo_ap");
        d.Set(1, 1, -1, "ammo_ap");
        d.Set(-1, 1, -3, "ammo_he");
        d.Set(1, 1, -3, "ammo_he");
        d.Set(-1, 1, 4, "engine_large");
        d.Set(1, 1, 4, "engine_large");
        d.Set(0, 1, 4, "fuel_tank");
        d.Set(-2, 1, 4, "fuel_tank");
        d.Set(2, 1, 4, "fuel_tank");
        d.Set(0, 1, 5, "fuel_tank");

        d.Set(0, 2, -2, "breech_120");
        for (int z = -3; z >= -12; z--) d.Set(0, 2, z, "barrel");
        d.Set(0, 2, -13, "muzzle_brake");
        d.Set(-1, 2, -2, "loader");
        d.Set(1, 2, -2, "loader");
        d.Set(0, 3, -2, "loader");
        d.Set(0, 2, -1, "propellant");
        d.Set(-2, 2, -3, "gunner_seat");
        d.Set(2, 2, -3, "commander_seat");

        d.Fill(-2, 2, 0, 0, -6, -6, "armor_heavy");
        d.Fill(-3, 3, 1, 1, -6, -6, "armor_heavy_slope");
        d.Fill(-3, 3, 2, 2, -5, -5, "armor_heavy_slope");
        d.Fill(-2, 2, 3, 3, -4, -4, "armor_heavy_slope");
        d.Fill(-3, -3, 1, 2, -5, -4, "armor_heavy");
        d.Fill(3, 3, 1, 2, -5, -4, "armor_heavy");
        d.Fill(-3, -3, 1, 2, -3, 6, "armor_light");
        d.Fill(3, 3, 1, 2, -3, 6, "armor_light");
        d.Fill(-2, 2, 0, 1, 6, 6, "armor_light");
        d.Fill(-2, 2, 2, 2, 2, 6, "armor_light");
        d.Fill(-2, 2, 3, 3, -3, 1, "armor_light");
        d.Fill(-2, 2, 0, 2, -5, 5, "frame");
        return d.Build("Bulwark (tank destroyer)");
    }

    /// <summary>A flat wall of one armor type, standing on the ground (y = 0).</summary>
    public static Blueprint Wall(string blockId, int width, int height)
    {
        var d = new Draft();
        int half = width / 2;
        d.Fill(-half, width - half - 1, 0, height - 1, 0, 0, blockId);
        return d.Build($"{BlockCatalog.Get(blockId).Name} wall");
    }

    /// <summary>A small bunker with an ammo store inside. Get a shell into it and watch.</summary>
    public static Blueprint Bunker()
    {
        var d = new Draft();
        d.Set(0, 0, 0, "ammo_he");
        d.Set(1, 0, 0, "ammo_he");
        d.Set(-1, 0, 0, "fuel_tank");
        d.Fill(-3, 3, 0, 1, -2, -2, "armor_heavy");
        d.Fill(-3, 3, 2, 2, -2, -2, "armor_heavy_slope");
        d.Fill(-3, 3, 0, 1, 2, 2, "armor_light");
        d.Fill(-3, -3, 0, 1, -1, 1, "armor_light");
        d.Fill(3, 3, 0, 1, -1, 1, "armor_light");
        d.Fill(-3, 3, 2, 2, -1, 2, "armor_light");
        d.Fill(-2, 2, 0, 1, -1, 1, "frame");
        return d.Build("Bunker");
    }
}
