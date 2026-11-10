using System.Collections.Generic;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Fast questions about the world for shells, wheels and gunners, answered in C# instead of the physics
/// engine wherever possible (a call into the engine costs several microseconds, and a big battle asks
/// tens of thousands of these a second):
/// <list type="bullet">
/// <item>The ground is the island's height grid (<see cref="HeightField"/>), tested exactly.</item>
/// <item>Vehicles, structures and wrecks are found through a <see cref="SphereGrid"/> rebuilt every physics
/// step, then tested block by block with the core grid ray cast.</item>
/// <item>Only near other solid scenery (ramps, crates) does a question still go to the physics engine.</item>
/// </list>
/// </summary>
public static class WorldQuery
{
    /// <summary>The island's ground, when there is one (set by <see cref="Terrain.Build"/>).</summary>
    public static HeightField Ground;

    /// <summary>Where the camera is, for level of detail. Set every frame by the scene.</summary>
    public static Vector3 Viewer;

    private static readonly List<Aabb> Props = new();
    private static readonly SphereGrid Hash = new(48f, 8f);
    private static readonly List<IGridHost> HashHosts = new();
    private static readonly List<Vector3> BlastPoints = new();
    private static readonly List<int> Found = new();
    private static readonly List<RayCellHit> CellHits = new();
    private static ulong _frame = ulong.MaxValue;
    private static PhysicsRayQueryParameters3D _query;

    /// <summary>Forget the scenery (when a scene closes).</summary>
    public static void Reset()
    {
        Ground = null;
        Props.Clear();
        Hash.Clear();
        HashHosts.Clear();
        BlastPoints.Clear();
        _frame = ulong.MaxValue;
    }

    /// <summary>
    /// Notes the solid shapes of a piece of scenery already in the tree (anything on the World layer that
    /// isn't the ground), so ground questions near it go to the physics engine.
    /// </summary>
    public static void AddProps(Node root)
    {
        if (root is CollisionShape3D cs && cs.Shape != null && cs.Shape is not HeightMapShape3D && cs.Shape is not WorldBoundaryShape3D)
        {
            Aabb local = cs.Shape switch
            {
                BoxShape3D b => new Aabb(-b.Size / 2, b.Size),
                SphereShape3D s => new Aabb(-Vector3.One * s.Radius, Vector3.One * s.Radius * 2),
                _ => cs.Shape.GetDebugMesh().GetAabb(),
            };
            Props.Add(cs.GlobalTransform * local);
        }

        foreach (Node c in root.GetChildren()) AddProps(c);
    }

    /// <summary>Files every grid host for this physics step (cheap; done once per step on first use).</summary>
    public static void Refresh()
    {
        ulong f = Engine.GetPhysicsFrames();
        if (f == _frame) return;
        _frame = f;
        Hash.Clear();
        HashHosts.Clear();
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h.Body == null || !GodotObject.IsInstanceValid(h.Body) || !h.Body.IsInsideTree()) continue;
            Hash.Add(h.Body.GlobalPosition.N(), h.Radius);
            HashHosts.Add(h);
        }

        BlastPoints.Clear();
        foreach (IBlastable b in Hittables.Blastables)
            if (b is not GodotObject o || GodotObject.IsInstanceValid(o)) BlastPoints.Add(b.BlastCenter);
    }

    /// <summary>Hosts whose bounding sphere the segment passes within <paramref name="pad"/> m of.</summary>
    public static void HostsAlong(Vector3 from, Vector3 to, float pad, List<IGridHost> result)
    {
        Refresh();
        Found.Clear();
        Hash.QuerySegment(from.N(), to.N(), pad, Found);
        foreach (int i in Found) result.Add(HashHosts[i]);
    }

    /// <summary>Hosts whose bounding sphere comes within <paramref name="radius"/> of a point.</summary>
    public static void HostsNear(Vector3 center, float radius, List<IGridHost> result)
    {
        Refresh();
        Found.Clear();
        Hash.QuerySphere(center.N(), radius, Found);
        foreach (int i in Found) result.Add(HashHosts[i]);
    }

    /// <summary>Whether a person or practice target is within <paramref name="pad"/> m of the segment.</summary>
    public static bool PeopleNear(Vector3 from, Vector3 to, float pad)
    {
        Refresh();
        System.Numerics.Vector3 a = from.N(), b = to.N();
        foreach (Vector3 p in BlastPoints)
            if (SphereGrid.SegmentDistance(a, b, p.N()) <= pad) return true;
        return false;
    }

    private static bool NearProps(Vector3 from, Vector3 to, float pad)
    {
        if (Props.Count == 0) return false;
        var box = new Aabb(from, Vector3.Zero).Expand(to).Grow(pad);
        foreach (Aabb p in Props)
            if (p.Intersects(box)) return true;
        return false;
    }

    /// <summary>Whether a box (in world space) overlaps any solid scenery other than the ground.</summary>
    public static bool PropsIn(Aabb box)
    {
        foreach (Aabb p in Props)
            if (p.Intersects(box)) return true;
        return false;
    }

    /// <summary>
    /// The first point where a segment meets the ground or solid scenery (not vehicles, structures or the sea).
    /// </summary>
    public static bool GroundRay(Node3D context, Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.Up;
        if (Ground != null && !NearProps(from, to, 0.5f))
        {
            if (!Ground.Raycast(from.N(), to.N(), out float d, out System.Numerics.Vector3 n)) return false;
            point = from + (to - from).Normalized() * d;
            normal = n.G();
            return true;
        }

        if (context == null || !context.IsInsideTree()) return false;
        _query ??= new PhysicsRayQueryParameters3D();
        _query.From = from;
        _query.To = to;
        _query.CollisionMask = GridRegistry.Layers.World;
        _query.Exclude = null;
        Godot.Collections.Dictionary hit = context.GetWorld3D().DirectSpaceState.IntersectRay(_query);
        if (hit.Count == 0) return false;
        point = hit["position"].AsVector3();
        normal = hit["normal"].AsVector3();
        return true;
    }

    /// <summary>
    /// The first block of any grid (vehicle, structure, wreck) along a segment, skipping two hosts (the
    /// one asking and its target, say). Returns the distance from <paramref name="from"/>.
    /// </summary>
    public static bool FirstBlock(Vector3 from, Vector3 to, IGridHost skipA, IGridHost skipB, out IGridHost host, out float dist)
    {
        host = null;
        dist = float.PositiveInfinity;
        var near = new List<IGridHost>();
        HostsAlong(from, to, 0f, near);
        float len = from.DistanceTo(to);
        if (len < 1e-4f) return false;
        Vector3 dir = (to - from) / len;
        foreach (IGridHost h in near)
        {
            if (h == skipA || h == skipB || !GodotObject.IsInstanceValid(h.Body)) continue;
            float d = FirstBlockOf(h, from, dir, Mathf.Min(len, dist));
            if (d < dist)
            {
                dist = d;
                host = h;
            }
        }

        return host != null;
    }

    /// <summary>Distance to the first live block of one host along a ray (infinity if none).</summary>
    public static float FirstBlockOf(IGridHost h, Vector3 from, Vector3 dir, float maxLen)
    {
        float best = float.PositiveInfinity;
        foreach (Section s in h.Model.Sections)
        {
            if (s.Cells.Count == 0) continue;
            Transform3D xf = h.View.SectionWorldFast(s.Id);
            if (!h.View.RayNearSection(s, xf, from, dir, Mathf.Min(maxLen, best))) continue;
            Transform3D inv = xf.AffineInverse();
            Vector3 o = inv * from / Conv.CS;
            Vector3 d = (inv.Basis * dir).Normalized();
            CellHits.Clear();
            GridRaycast.Cast(s, s.Min, s.Max, o.N(), d.N(), Mathf.Min(maxLen, best) / Conv.CS, CellHits, firstOnly: true);
            if (CellHits.Count > 0) best = Mathf.Min(best, CellHits[0].TEnter * Conv.CS);
        }

        return best;
    }

    private readonly struct SmokeCloud
    {
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly double Ends;
        public SmokeCloud(Vector3 c, float r, double ends) { Center = c; Radius = r; Ends = ends; }
    }

    private static readonly List<SmokeCloud> Smoke = new();

    /// <summary>A smoke screen: nobody can see through it until it thins out.</summary>
    public static void AddSmoke(Vector3 center, float radius, float lifeS) =>
        Smoke.Add(new SmokeCloud(center, radius, Time.GetTicksMsec() / 1000.0 + lifeS));

    /// <summary>Does a line of sight pass through a smoke screen?</summary>
    public static bool ThroughSmoke(Vector3 from, Vector3 to)
    {
        if (Smoke.Count == 0) return false;
        double now = Time.GetTicksMsec() / 1000.0;
        Smoke.RemoveAll(s => s.Ends < now);
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 1e-3f) return false;
        d /= len;
        foreach (SmokeCloud s in Smoke)
        {
            float t = Mathf.Clamp((s.Center - from).Dot(d), 0f, len);
            // Only clouds between the two (not one you're standing in at the start or end) block the view.
            if (t < 1f || t > len - 1f) continue;
            if ((from + d * t).DistanceSquaredTo(s.Center) < s.Radius * s.Radius) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether one point can see another: no ground, scenery or other grid in between. Grids
    /// <paramref name="self"/> and <paramref name="target"/> are ignored.
    /// </summary>
    public static bool LineClear(Node3D context, Vector3 from, Vector3 to, IGridHost self, IGridHost target)
    {
        if (ThroughSmoke(from, to)) return false;
        if (GroundRay(context, from, to, out _, out _)) return false;
        return !FirstBlock(from, to, self, target, out _, out _);
    }
}
