using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Buildings are vehicles that stay put: anchored by their foundations, crewed, armed and on the tactical
/// map like anything else. And any vehicle or building with a workbench is a repair bay for the vehicles
/// parked near it.
/// </summary>
public partial class VehicleBody
{
    public bool IsBuilding => Analysis.IsBuilding;

    private float _bayRebuild;

    /// <summary>
    /// Parked at a repair bay (near a workbench): the worst-damaged block is patched up, and every few seconds
    /// a block that was shot away is put back. Returns true while there's work to do.
    /// </summary>
    public bool BayRepair(float dt)
    {
        bool working = false;
        BlockInstance worst = null;
        float worstShare = 1f;
        foreach (BlockInstance b in Model.Instances)
        {
            if (!b.Alive || b.Def.Hp <= 0) continue;
            float share = b.Hp / b.Def.Hp;
            if (share >= worstShare) continue;
            worstShare = share;
            worst = b;
        }

        if (worst != null)
        {
            worst.Hp = Mathf.Min(worst.Def.Hp, worst.Hp + Fittings.BayHpPerS * dt);
            working = true;
        }

        _bayRebuild += dt;
        if (_bayRebuild < Fittings.BayRebuildS) return working;
        _bayRebuild = 0;
        foreach (Int3 cell in Repair.Missing(Analysis, Model))
        {
            if (!Repair.CanRebuild(Model, Analysis, cell) || !TryRebuild(cell)) continue;
            return true;
        }

        return working;
    }
}
