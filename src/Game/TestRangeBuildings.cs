using System.Collections.Generic;
using System.Linq;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Buildings on the range, and the interior items at work: supply crates and workbenches on your buildings
/// (or trucks) are forward supply points and repair bays; first aid heals and lockers restock whoever
/// stands near them; a bunk you rest on is where you come back if you fall.
/// </summary>
public partial class TestRange
{
    /// <summary>Where a building being tried out from the builder goes: off to one side of the firing line.</summary>
    private Vector3 BuildSite = new(-42, Ground, 108);

    private static readonly Vector3 OutpostAt = new(46, Ground, 116);

    /// <summary>The flak tower and the mortar battery stand on ground levelled for them in the eastern hills.</summary>
    private static readonly Vector3 FlakTowerAt = new(Terrain.FlakTowerPad.X, Ground, Terrain.FlakTowerPad.Y),
        MortarBatteryAt = new(Terrain.MortarPad.X, Ground, Terrain.MortarPad.Y);

    private (IGridHost host, BlockInstance bunk)? _bunk;
    private string _serviceNote = "";

    /// <summary>
    /// Yours: the forward base by the depot, a siege battery behind it, a coastal fort above the east beach,
    /// and a flak tower and a mortar battery in the hills east of the lane. The enemy's: a pillbox and a flak nest
    /// down the range.
    /// </summary>
    /// <summary>
    /// The village south-east of the lane: houses facing each other across the main street behind garden walls, a
    /// chapel by the crossroads, a barn at the north end. All brick, timber and tile: shells go through them and
    /// knock them down.
    /// </summary>
    private void AddVillage()
    {
        IReadOnlyList<Blueprint> v = ExampleDesigns.Village;
        float x = Terrain.Village.X, z = Terrain.Village.Y;
        (int design, float dx, float dz, float yaw)[] layout =
        {
            (0, -13f, -26f, -90f), (1, 13f, -26f, 90f), (2, -13f, 24f, -90f), (3, 13f, 24f, 90f),
            (4, 15f, 36f, 0f), (5, -21f, 12f, 0f),
            (6, -6f, -38f, 90f), (6, 6f, -38f, 90f), (6, -6f, 32f, 90f), (6, 6f, 32f, 90f),
        };
        foreach ((int design, float dx, float dz, float yaw) in layout)
        {
            StructureBody s = StructureBody.Create(v[design], new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yaw)), new Vector3(x + dx, Ground, z + dz)), v[design].Name);
            AddChild(s);
            _targets.Add(s);
        }

        AddChild(Sign("VILLAGE", new Vector3(x - 5f, Ground + 4f, z - 6f), 72));
    }

    private void AddBuildings()
    {
        AddVillage();
        VehicleBody outpost = AddDummy(ExampleDesigns.Outpost(), OutpostAt, 90f);
        outpost.Faction = 0;
        outpost.AddChild(Sign("OUTPOST\nsupplies, repairs, bunks", new Vector3(0, 5.2f, 0), 60));
        VehicleBody fortress = AddDummy(ExampleDesigns.SiegeFortress(), OutpostAt + new Vector3(8, 0, 48), 180f);
        fortress.Faction = 0;
        fortress.AddChild(Sign("SIEGE FORTRESS\n420 mm mortar on the roof", new Vector3(0, 10.5f, 0), 70));
        VehicleBody citadel = AddDummy(ExampleDesigns.CoastalCitadel(), new Vector3(Terrain.CitadelPad.X, Ground, Terrain.CitadelPad.Y), -90f);
        citadel.Faction = 0;
        citadel.AddChild(Sign("COASTAL CITADEL\nheavy railguns, laser tower", new Vector3(0, 19f, 0), 80));
        VehicleBody tower = AddDummy(ExampleDesigns.FlakTower(), FlakTowerAt, 0f);
        tower.Faction = 0;
        tower.AddChild(Sign("FLAK TOWER\n8 × 128 mm heavy flak", new Vector3(0, 13.5f, 0), 80));
        VehicleBody battery = AddDummy(ExampleDesigns.MortarBattery(), MortarBatteryAt, 0f);
        battery.Faction = 0;
        battery.AddChild(Sign("MORTAR BATTERY\n4 × 160 mm mortars", new Vector3(0, 5f, -6f), 64));
        // An armoured dozer by the command post, to dig with.
        VehicleBody mole = AddDummy(ExampleDesigns.Mole(), new Vector3(-28, Ground, 152), 0f);
        mole.Faction = 0;
        mole.AddChild(Sign("MOLE  ·  armoured dozer\nE to drive · K lowers the blade", new Vector3(0, 4.2f, 0), 56));
        AddDummy(ExampleDesigns.Pillbox(), new Vector3(-44, Ground, SpawnPoint.Z - 190f), 0f);
        AddDummy(ExampleDesigns.FlakNest(), new Vector3(48, Ground, SpawnPoint.Z - 230f), 30f);
    }

    private readonly List<IGridHost> _serviceHosts = new();

    /// <summary>Runs every friendly building's and vehicle's interior items for this step.</summary>
    private void ServicesTick(float dt)
    {
        _serviceNote = "";
        Vector3 me = _me.GlobalPosition + Vector3.Up * 0.9f;
        bool onFoot = _me.Active && _me.Alive;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (!IsInstanceValid(h.Body) || (h is VehicleBody hv && hv.Faction != 0)) continue;
            IReadOnlyList<BlockInstance> items = h is VehicleBody v ? v.Services : h is StructureBody s ? s.Services : null;
            if (items == null || items.Count == 0) continue;
            foreach (BlockInstance b in items)
            {
                if (!b.Alive) continue;
                Vector3 at = h.View.BlockWorldPos(b);
                switch (b.Def.Kind)
                {
                    case BlockKind.FirstAid when onFoot && me.DistanceTo(at) < Fittings.FirstAidRangeM:
                        if (_me.Health < InfantryDamage.MaxHealth)
                        {
                            _me.Heal(Fittings.FirstAidHpPerS * dt);
                            _serviceNote = "First aid: patching you up";
                        }

                        break;
                    case BlockKind.Locker when onFoot && me.DistanceTo(at) < Fittings.LockerRangeM:
                        if (_me.RefillAmmo(dt)) _serviceNote = "Weapon locker: restocking";
                        break;
                    case BlockKind.Bunk when onFoot && me.DistanceTo(at) < 2f:
                        if (_serviceNote.Length == 0) _serviceNote = _bunk?.bunk == b ? "Your bunk: you'll come back here" : "E  rest here (and come back here if you fall)";
                        break;
                    case BlockKind.SupplyCrate:
                        if (onFoot && me.DistanceTo(at) < 4f && _me.RefillAmmo(dt)) _serviceNote = "Supply crates: restocking";
                        foreach (VehicleBody other in NearbyFriendly(at, Fittings.BayRangeM, h))
                            if (other.Resupply(dt) && other == _mount) _resupplying = true;
                        break;
                    case BlockKind.Workbench:
                        foreach (VehicleBody other in NearbyFriendly(at, Fittings.BayRangeM, h))
                            if (other.BayRepair(dt) && other == _mount) _serviceNote = "Repair bay: patching you up";
                        break;
                }
            }
        }
    }

    /// <summary>Your vehicles (not the one the item is on) parked within range of a point.</summary>
    private static IEnumerable<VehicleBody> NearbyFriendly(Vector3 at, float range, IGridHost except)
    {
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is not VehicleBody v || h == except || v.Faction != 0 || !IsInstanceValid(v) || v.IsBuilding) continue;
            if (v.LinearVelocity.LengthSquared() > 4f) continue;
            if ((v.GlobalTransform * v.CenterOfMass).DistanceTo(at) - v.Radius * 0.5f < range) yield return v;
        }
    }

    /// <summary>E next to a bunk: rest there. You're healed, and it's where you come back if you fall.</summary>
    private bool TryRest()
    {
        Vector3 me = _me.GlobalPosition + Vector3.Up * 0.9f;
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (!IsInstanceValid(h.Body) || (h is VehicleBody hv && hv.Faction != 0)) continue;
            IReadOnlyList<BlockInstance> items = h is VehicleBody v ? v.Services : h is StructureBody s ? s.Services : null;
            if (items == null) continue;
            BlockInstance bunk = items.FirstOrDefault(b => b.Alive && b.Def.Kind == BlockKind.Bunk && h.View.BlockWorldPos(b).DistanceTo(me) < 2f);
            if (bunk == null) continue;
            _bunk = (h, bunk);
            _me.Heal(InfantryDamage.MaxHealth);
            Big("Rested. If you fall, you'll come back here.");
            Sfx.Play2D(this, SoundId.PlaceLight, -6f, 0.7f);
            return true;
        }

        return false;
    }

    /// <summary>Where you come back after falling: your bunk if it still stands, otherwise the supply pad.</summary>
    private Vector3? BunkRespawn()
    {
        if (_bunk is not { } b || !IsInstanceValid(b.host.Body) || !b.bunk.Alive) return null;
        return b.host.View.BlockWorldPos(b.bunk) + Vector3.Up * 0.5f;
    }
}
