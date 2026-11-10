using System;
using System.Collections.Generic;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>
/// Pieces that show how parts hold together, worked out from what's next to each block, so a design
/// reads as one machine however odd it is: an axle from every wheel to whatever it hangs from, and a
/// collar where every gun barrel leaves its breech or armor. Parts are in the block's own frame (centered,
/// unturned), drawn with the block's own parts.
/// </summary>
public static class Joints
{
    private const float H = Blueprint.CellSize * 0.5f;
    private static readonly Quaternion AlongX = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
    private static readonly Quaternion AlongZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    private static ModelPart Cyl(float r, float h, Paint p, Vector3 pos, Quaternion rot, int sides = 12) => new()
    {
        Prim = Prim.Cylinder, Size = new Vector3(r, h, r), Detail = sides, Paint = p, Node = -1,
        Transform = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos),
    };

    private static ModelPart Torus(float ring, float tube, Paint p, Vector3 pos, Quaternion rot) => new()
    {
        Prim = Prim.Torus, Size = new Vector3(ring, tube, 0), Paint = p, Node = -1,
        Transform = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos),
    };

    /// <summary>Whether a block is something a part can hang from (not another wheel or a cosmetic bit).</summary>
    private static bool Holds(BlockDef d) => d != null && !d.IsWheelLike && d.Kind != BlockKind.Detail;

    /// <summary>
    /// The joining pieces for one block. <paramref name="at"/> says what's in a grid cell (null for nothing);
    /// <paramref name="orientation"/> is how the block is turned.
    /// </summary>
    public static List<ModelPart> For(BlockDef def, Int3 cell, int orientation, float caliberMm, Func<Int3, BlockDef> at)
    {
        var parts = new List<ModelPart>();
        switch (def.Kind)
        {
            case BlockKind.Wheel:
            case BlockKind.SteerWheel:
            {
                // The axle runs along the wheel's X: a stub out to each side that has something to hang from.
                float r = def.WheelRadiusM;
                foreach (int side in new[] { -1, 1 })
                {
                    Int3 dir = Orientations.Apply(orientation, new Int3(side, 0, 0));
                    if (!Holds(at(cell + dir))) continue;
                    parts.Add(Cyl(r * 0.17f, 0.11f, Paint.Steel, new Vector3(side * (H - 0.045f), 0, 0), AlongX, 10));
                    parts.Add(Cyl(r * 0.36f, 0.025f, Paint.Dark, new Vector3(side * (H - 0.012f), 0, 0), AlongX, 12));
                    parts.Add(Cyl(r * 0.24f, 0.03f, Paint.Dark, new Vector3(side * 0.19f, 0, 0), AlongX, 10));
                }

                break;
            }

            case BlockKind.Barrel:
            {
                // The first barrel of a gun (nothing barrel-like behind it) gets a collar at its root.
                Int3 back = Orientations.Apply(orientation, new Int3(0, 0, 1));
                BlockDef behind = at(cell + back);
                if (behind != null && behind.Kind is BlockKind.Barrel or BlockKind.MuzzleBrake) break;
                float br = BlockModels.BarrelRadius(caliberMm);
                parts.Add(Cyl(MathF.Min(br * 1.9f, H * 0.95f), 0.07f, Paint.SelfDark, new Vector3(0, 0, H - 0.035f), AlongZ, 20));
                parts.Add(Torus(MathF.Min(br * 1.9f, H * 0.95f), 0.012f, Paint.Steel, new Vector3(0, 0, H - 0.075f), AlongZ));
                parts.Add(Cyl(br * 1.25f, 0.06f, Paint.Self, new Vector3(0, 0, H - 0.1f), AlongZ, 20));
                break;
            }
        }

        return parts;
    }
}
