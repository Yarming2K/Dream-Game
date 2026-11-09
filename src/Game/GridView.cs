using System;
using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Puts a <see cref="GridModel"/> on screen and into physics: one node per section (so turrets and guns
/// can turn), one visual per block, and one collider per solid block as a direct child of the body.
/// </summary>
public sealed class GridView
{
    private struct BlockNodes
    {
        public Node3D Visual;
        public CollisionShape3D Collider;
    }

    public readonly PhysicsBody3D Body;
    public readonly GridModel Model;
    private readonly Node3D[] _sections;
    private readonly float[] _syncedAngle;
    private readonly Dictionary<BlockInstance, BlockNodes> _nodes = new();
    private readonly Func<Int3, float> _caliberOf;

    public GridView(PhysicsBody3D body, GridModel model, Func<Int3, float> caliberOf = null)
    {
        Body = body;
        Model = model;
        _caliberOf = caliberOf;
        _sections = new Node3D[model.Sections.Count];
        _syncedAngle = new float[model.Sections.Count];
        foreach (Section s in model.Sections)
        {
            var node = new Node3D { Name = $"{s.Kind}{s.Id}" };
            Node parent = s.Parent < 0 ? body : _sections[s.Parent];
            parent.AddChild(node);
            node.Transform = Conv.SectionLocal(s);
            _sections[s.Id] = node;
        }

        foreach (BlockInstance b in model.Blocks.Values) AddNodes(b);
    }

    private void AddNodes(BlockInstance b)
    {
        float caliber = _caliberOf?.Invoke(b.Cell) ?? 76f;
        Node3D visual = BlockVisuals.Create(b.Def, caliber);
        visual.Transform = Conv.BlockTransform(b.Cell, b.Orientation);
        _sections[b.Section].AddChild(visual);

        CollisionShape3D col = null;
        if (b.Def.HasCollider)
        {
            col = new CollisionShape3D { Shape = BlockVisuals.ShapeOf(b.Def) };
            col.Transform = SectionToBody(b.Section) * Conv.BlockTransform(b.Cell, b.Orientation);
            Body.AddChild(col);
        }

        _nodes[b] = new BlockNodes { Visual = visual, Collider = col };
    }

    public Node3D SectionNode(int id) => _sections[id];

    public Node3D VisualOf(BlockInstance b) => _nodes.TryGetValue(b, out BlockNodes n) ? n.Visual : null;

    /// <summary>Where a section is in the world right now.</summary>
    public Transform3D SectionGlobal(int id) => _sections[id].GlobalTransform;

    /// <summary>A section's placement relative to the body, combining every turn above it.</summary>
    public Transform3D SectionToBody(int id)
    {
        Transform3D t = Transform3D.Identity;
        for (int s = id; s >= 0; s = Model.Sections[s].Parent)
            t = Conv.SectionLocal(Model.Sections[s]) * t;
        return t;
    }

    public void SetAngle(int sectionId, float angle)
    {
        Section s = Model.Sections[sectionId];
        s.Angle = angle;
        _sections[sectionId].Transform = Conv.SectionLocal(s);
    }

    /// <summary>
    /// Moves turret colliders to match the visuals. Physics engines rebuild a body's shape when a collider
    /// moves, so this only happens once a section has turned a few degrees.
    /// </summary>
    public void SyncColliders(bool force = false)
    {
        var dirty = new bool[Model.Sections.Count];
        bool any = false;
        foreach (Section s in Model.Sections)
        {
            bool parentDirty = s.Parent >= 0 && dirty[s.Parent];
            if (s.Kind == SectionKind.Hull && !force) continue;
            if (!force && !parentDirty && MathF.Abs(s.Angle - _syncedAngle[s.Id]) < Mathf.DegToRad(3f)) continue;
            dirty[s.Id] = true;
            _syncedAngle[s.Id] = s.Angle;
            any = true;
        }

        if (!any) return;
        foreach (KeyValuePair<BlockInstance, BlockNodes> kv in _nodes)
        {
            if (kv.Value.Collider == null || !dirty[kv.Key.Section]) continue;
            kv.Value.Collider.Transform = SectionToBody(kv.Key.Section) * Conv.BlockTransform(kv.Key.Cell, kv.Key.Orientation);
        }
    }

    public void RemoveBlock(BlockInstance b)
    {
        if (!_nodes.TryGetValue(b, out BlockNodes n)) return;
        _nodes.Remove(b);
        if (n.Visual != null && GodotObject.IsInstanceValid(n.Visual)) n.Visual.QueueFree();
        if (n.Collider != null && GodotObject.IsInstanceValid(n.Collider))
        {
            Body.RemoveChild(n.Collider);
            n.Collider.QueueFree();
        }
    }

    /// <summary>World-space center of a block, following any turret or gun turn.</summary>
    public Vector3 BlockWorldPos(BlockInstance b) => SectionGlobal(b.Section) * Conv.CellPos(b.Cell);

    /// <summary>Radius around the body origin that contains every block, however its turrets are turned.</summary>
    public float BoundingRadius()
    {
        float r = 0, pivots = 0;
        foreach (BlockInstance b in Model.Blocks.Values) r = MathF.Max(r, Conv.CellPos(b.Cell).Length());
        foreach (Section s in Model.Sections) pivots = MathF.Max(pivots, s.Pivot.Length() * Conv.CS);
        return r + 2f * pivots + Conv.CS * 2f;
    }
}
