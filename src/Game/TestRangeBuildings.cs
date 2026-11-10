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
    private static readonly Vector3 BuildSite = new(-42, Ground, 108);

    private static readonly Vector3 OutpostAt = new(46, Ground, 116);

    private (IGridHost host, BlockInstance bunk)? _bunk;
    private string _serviceNote = "";

    /// <summary>
    /// Yours: the forward base by the depot, a siege battery behind it, and a coastal fort above the east
    /// beach. The enemy's: a pillbox and a flak nest down the range.
    /// </summary>
    private void AddBuildings()
    {
        VehicleBody outpost = AddDummy(ExampleDesigns.Outpost(), OutpostAt, 90f);
        outpost.Faction = 0;
        outpost.AddChild(Sign("OUTPOST\nsupplies, repairs, bunks", new Vector3(0, 5.2f, 0), 60));
        VehicleBody battery = AddDummy(ExampleDesigns.SiegeBattery(), OutpostAt + new Vector3(4, 0, 36), 180f);
        battery.Faction = 0;
        battery.AddChild(Sign("SIEGE BATTERY\n420 mm: E at the gunner seat", new Vector3(0, 4.2f, 0), 60));
        VehicleBody fort = AddDummy(ExampleDesigns.CoastalFort(), new Vector3(Terrain.CoastX - 28f, Ground, 60), -90f);
        fort.Faction = 0;
        fort.AddChild(Sign("COASTAL FORT\nheavy railgun, laser battery", new Vector3(0, 7f, 0), 60));
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
