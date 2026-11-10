using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Water standing inside a flooded ship: each flooding compartment shows its water surface, rising as it
/// fills, so from inside (or through a shell hole) you can see how bad it is.
/// </summary>
public partial class VehicleBody
{
    private sealed class FloodView
    {
        public MeshInstance3D Mesh;
        public int Layer = int.MinValue;
    }

    private readonly Dictionary<Compartment, FloodView> _floodViews = new();
    private float _floodViewClock;
    private static StandardMaterial3D _floodMat;

    private void FloodViewTick(float dt)
    {
        if (!IsNaval || Analysis.Compartments.Count == 0) return;
        _floodViewClock -= dt;
        if (_floodViewClock > 0) return;
        _floodViewClock = 0.2f;
        // Only drawn near the camera: from far off nobody can see inside.
        bool near = GlobalPosition.DistanceTo(WorldQuery.Viewer) < Radius + 90f;
        foreach (Compartment c in Analysis.Compartments)
        {
            if (c.IsBallast) continue;
            _floodViews.TryGetValue(c, out FloodView v);
            float y = near ? Flood.SurfaceY(c) : float.NegativeInfinity;
            if (float.IsNegativeInfinity(y) || c.FillFraction < 0.01f || c.FillFraction > 0.995f)
            {
                if (v != null) v.Mesh.Visible = false;
                continue;
            }

            if (v == null)
            {
                _floodMat ??= new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.1f, 0.27f, 0.3f, 0.78f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    Metallic = 0.25f, Roughness = 0.08f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
                v = new FloodView { Mesh = new MeshInstance3D { MaterialOverride = _floodMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } };
                View.SectionNode(0).AddChild(v.Mesh);
                _floodViews[c] = v;
            }

            // The surface sits in one layer of cells; its outline only changes when it moves to another layer.
            int layer = Mathf.CeilToInt(y - 0.5f);
            if (layer != v.Layer)
            {
                v.Mesh.Mesh = LayerMesh(c, layer);
                v.Layer = layer;
            }

            v.Mesh.Visible = true;
            v.Mesh.Position = new Vector3(0, y * Conv.CS, 0);
        }
    }

    /// <summary>A flat sheet over every cell of a compartment in one layer (at height 0, moved up to the water level).</summary>
    private static ArrayMesh LayerMesh(Compartment c, int layer)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float h = Conv.CS * 0.5f;
        int quads = 0;
        foreach (Int3 cell in c.Cells)
        {
            if (cell.Y != layer) continue;
            float x = cell.X * Conv.CS, z = cell.Z * Conv.CS;
            var a = new Vector3(x - h, 0, z - h);
            var b = new Vector3(x + h, 0, z - h);
            var d = new Vector3(x + h, 0, z + h);
            var e = new Vector3(x - h, 0, z + h);
            foreach (Vector3 p in new[] { a, b, d, a, d, e })
            {
                st.SetNormal(Vector3.Up);
                st.AddVertex(p);
            }

            quads++;
        }

        return quads > 0 ? st.Commit() : new ArrayMesh();
    }
}
