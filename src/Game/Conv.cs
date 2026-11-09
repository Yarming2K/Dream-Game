using DreamGame.Core;
using Godot;
using NVec = System.Numerics.Vector3;

namespace DreamGame.Game;

/// <summary>Conversions between the core rules' math types and Godot's.</summary>
public static class Conv
{
    public const float CS = Blueprint.CellSize;

    public static Vector3 G(this NVec v) => new(v.X, v.Y, v.Z);
    public static NVec N(this Vector3 v) => new(v.X, v.Y, v.Z);
    public static Vector3 G(this Int3 c) => new(c.X, c.Y, c.Z);

    /// <summary>Center of a cell, in meters, in its section's frame.</summary>
    public static Vector3 CellPos(Int3 c) => new(c.X * CS, c.Y * CS, c.Z * CS);

    /// <summary>The block's rotation as a Godot basis (columns are the block's local axes in the grid).</summary>
    public static Basis BasisOf(int orientation) =>
        new(Orientations.Right(orientation).G(), Orientations.Up(orientation).G(), Orientations.Apply(orientation, Int3.Back).G());

    public static Transform3D BlockTransform(Int3 cell, int orientation) => new(BasisOf(orientation), CellPos(cell));

    /// <summary>A section's turn around its pivot, relative to its parent section.</summary>
    public static Transform3D SectionLocal(Section s)
    {
        if (s.Kind == SectionKind.Hull || s.Angle == 0f) return Transform3D.Identity;
        var basis = new Basis(s.Axis.G().Normalized(), s.Angle);
        Vector3 p = s.Pivot.G() * CS;
        return new Transform3D(basis, p - basis * p);
    }
}
