using System;
using System.Numerics;

namespace DreamGame.Core;

/// <summary>A rectangle of height-grid samples, inclusive (empty when X1 &lt; X0).</summary>
public readonly struct GridRect
{
    public readonly int X0, Z0, X1, Z1;

    public GridRect(int x0, int z0, int x1, int z1)
    {
        X0 = x0;
        Z0 = z0;
        X1 = x1;
        Z1 = z1;
    }

    public static readonly GridRect Empty = new(0, 0, -1, -1);
    public bool IsEmpty => X1 < X0 || Z1 < Z0;

    public GridRect Union(GridRect o) =>
        IsEmpty ? o : o.IsEmpty ? this : new GridRect(Math.Min(X0, o.X0), Math.Min(Z0, o.Z0), Math.Max(X1, o.X1), Math.Max(Z1, o.Z1));

    public GridRect Grow(int n, int width, int depth) =>
        IsEmpty ? this : new GridRect(Math.Max(0, X0 - n), Math.Max(0, Z0 - n), Math.Min(width - 1, X1 + n), Math.Min(depth - 1, Z1 + n));

    public override string ToString() => IsEmpty ? "(empty)" : $"({X0},{Z0})-({X1},{Z1})";
}

/// <summary>
/// Ground that can be changed: shell and bomb craters, digging and piling earth by hand, and machines
/// cutting and pushing it. Works on a <see cref="HeightField"/> in place, so shells, wheels and gunners'
/// sight lines see every change at once.
/// <list type="bullet">
/// <item>A <b>crater</b> is a bowl, with part of the earth it threw out (half) heaped in a rim round it;
/// the rest is blown away. How big depends on the blast and how high above the ground it went off.</item>
/// <item><b>Digging</b> takes earth out (you carry it); <b>piling</b> puts it back somewhere else.
/// Neither makes or loses earth.</item>
/// <item>Earth can't stand steeper than about 48°: after every change it <b>slumps</b> until no step
/// between neighbouring samples is more than <see cref="MaxStepM"/> per meter. Slumping moves earth, it
/// never makes or loses any.</item>
/// <item>Nothing digs deeper than <see cref="MaxDigM"/> below the ground as it was made (bedrock), or into
/// samples that are <see cref="Locked"/> (under buildings).</item>
/// <item>Each sample remembers how freshly it was turned over (<see cref="Disturbed"/>, 0 to 1), so turned
/// earth can be drawn as raw soil.</item>
/// </list>
/// Changed samples are collected in a dirty rectangle the engine side takes to redraw and re-collide.
/// Deterministic: the same changes in the same order give the same ground.
/// </summary>
public sealed class Earthworks
{
    public readonly HeightField Field;
    public readonly float[] Original;
    public readonly float[] Disturbed;

    /// <summary>Deepest anything can dig below the ground as made, in meters.</summary>
    public float MaxDigM = 8f;

    /// <summary>Steepest step between neighbouring samples, per meter of spacing, before earth slumps.</summary>
    public float MaxStepM = 1.1f;

    /// <summary>Share of a crater's earth heaped in its rim (the rest is thrown far or compacted).</summary>
    public const float RimShare = 0.5f;

    /// <summary>
    /// A dozer blade: lowered, its edge is this far below the bottom of the vehicle's tracks or wheels; raised,
    /// this far above. It takes everything above its edge in the strip it sweeps (the heap it's pushing too, so
    /// the heap goes on ahead whole), at most <see cref="BladeMaxCutM"/> deep, and drags back with this many
    /// newtons for every m³ a second it moves.
    /// </summary>
    public const float BladeDigM = 0.25f, BladeRaiseM = 0.3f, BladeMaxCutM = 4f, BladeDragNPerM3s = 12000f;

    /// <summary>Samples that must not change (under a building's foundation), by grid index. Null: none.</summary>
    public Func<int, int, bool> Locked;

    /// <summary>
    /// Samples held for the moment, as if behind a wall: no earth can be heaped or slump onto them (they can
    /// still be cut). A dozer blade holds the ground under and behind it, so its heap stays in front while it's
    /// pushed. Set it, make the change, clear it.
    /// </summary>
    public Func<int, int, bool> Hold;

    /// <summary>Goes up by one with every change.</summary>
    public int Version { get; private set; }

    private GridRect _dirty = GridRect.Empty;

    public Earthworks(HeightField field)
    {
        Field = field;
        Original = (float[])field.Heights.Clone();
        Disturbed = new float[field.Heights.Length];
    }

    /// <summary>Every sample changed since the last call (then forgets them).</summary>
    public GridRect TakeDirty()
    {
        GridRect d = _dirty;
        _dirty = GridRect.Empty;
        return d;
    }

    public bool HasDirty => !_dirty.IsEmpty;

    private int W => Field.Width;
    private int D => Field.Depth;
    private float S => Field.Spacing;
    private float CellArea => S * S;

    private Vector2 SampleWorld(int x, int z) => new(Field.Origin.X + x * S, Field.Origin.Y + z * S);

    /// <summary>The samples within <paramref name="r"/> meters of a point, clipped to the grid.</summary>
    private GridRect Around(Vector2 at, float r)
    {
        int x0 = (int)MathF.Floor((at.X - r - Field.Origin.X) / S), x1 = (int)MathF.Ceiling((at.X + r - Field.Origin.X) / S);
        int z0 = (int)MathF.Floor((at.Y - r - Field.Origin.Y) / S), z1 = (int)MathF.Ceiling((at.Y + r - Field.Origin.Y) / S);
        return new GridRect(Math.Max(0, x0), Math.Max(0, z0), Math.Min(W - 1, x1), Math.Min(D - 1, z1));
    }

    private bool IsLocked(int x, int z) => Locked != null && Locked(x, z);
    private bool IsHeld(int x, int z) => Hold != null && Hold(x, z);

    /// <summary>
    /// Holds the strip behind a blade's edge from <paramref name="a"/> to <paramref name="b"/>, <paramref name="depth"/> meters
    /// deep on the side away from <paramref name="ahead"/>, for the next change (see <see cref="Hold"/>).
    /// </summary>
    public void HoldBehind(Vector2 a, Vector2 b, Vector2 ahead, float depth)
    {
        Vector2 ab = b - a;
        float len = ab.Length();
        if (len < 1e-4f) { Hold = null; return; }
        Vector2 dir = ab / len;
        Vector2 back = -Vector2.Normalize(ahead);
        Hold = (x, z) =>
        {
            Vector2 p = SampleWorld(x, z) - a;
            float along = Vector2.Dot(p, dir), behind = Vector2.Dot(p, back);
            return along >= -0.5f && along <= len + 0.5f && behind >= -0.6f && behind <= depth;
        };
    }
    private float Bottom(int i) => MathF.Max(Original[i] - MaxDigM, Field.Floor + 0.5f);

    /// <summary>Lowers one sample by up to <paramref name="by"/> meters (not past bedrock). Returns how much it went down.</summary>
    private float Lower(int x, int z, float by)
    {
        if (by <= 0 || IsLocked(x, z)) return 0;
        int i = z * W + x;
        float h = Field.Heights[i];
        float nh = MathF.Max(h - by, Bottom(i));
        if (nh >= h) return 0;
        Field.Heights[i] = nh;
        return h - nh;
    }

    private bool Raise(int x, int z, float by)
    {
        if (by <= 0 || IsLocked(x, z) || IsHeld(x, z)) return false;
        int i = z * W + x;
        Field.Heights[i] += by;
        Field.NoteHeight(Field.Heights[i]);
        return true;
    }

    private void Touch(GridRect r)
    {
        if (r.IsEmpty) return;
        _dirty = _dirty.Union(r);
        Version++;
    }

    private void Stir(int x, int z, float amount)
    {
        int i = z * W + x;
        Disturbed[i] = MathF.Max(Disturbed[i], Math.Clamp(amount, 0f, 1f));
    }

    // ------------------------------------------------------------------ craters

    /// <summary>
    /// How big a crater a blast digs: none if it went off more than its own radius above the ground.
    /// Bigger and stronger blasts dig wider and deeper; a 76 mm shell leaves a scrape a meter or so across,
    /// a bomb a pit about 10 m across and 2 m deep, a siege mortar shell a hole 20 m across.
    /// </summary>
    public static (float radius, float depth) CraterSize(float damage, float blastRadiusM, float heightAboveGround)
    {
        if (damage <= 0 || blastRadiusM <= 0 || heightAboveGround >= blastRadiusM) return (0, 0);
        float k = 1f - MathF.Max(0f, heightAboveGround) / blastRadiusM;
        float r = Math.Clamp(blastRadiusM * 0.75f, 0.5f, 16f) * (0.4f + 0.6f * k);
        float depth = r * 0.4f * MathF.Pow(MathF.Min(1f, damage / 1500f), 0.35f) * k;
        return (r, MathF.Min(depth, 6f));
    }

    /// <summary>
    /// Blows a crater: a bowl <paramref name="radius"/> across and <paramref name="depth"/> deep at the middle, with
    /// half the earth heaped in a rim out to 1.6 times the radius. Returns the volume dug out (m³).
    /// </summary>
    public float Crater(Vector2 at, float radius, float depth)
    {
        if (radius <= 0 || depth <= 0) return 0;
        float rimOut = radius * 1.6f;
        GridRect area = Around(at, rimOut);
        if (area.IsEmpty) return 0;
        float removed = 0;
        float rimWeight = 0;
        for (int z = area.Z0; z <= area.Z1; z++)
        for (int x = area.X0; x <= area.X1; x++)
        {
            float d = Vector2.Distance(SampleWorld(x, z), at);
            if (d < radius)
            {
                float t = d / radius;
                removed += Lower(x, z, depth * (1f - t * t)) * CellArea;
                Stir(x, z, 1f);
            }
            else if (d < rimOut && !IsLocked(x, z) && !IsHeld(x, z)) rimWeight += RimShape(d, radius, rimOut);
        }

        // The rim: half the earth, heaped highest just outside the bowl.
        if (removed > 0 && rimWeight > 0)
        {
            float perWeight = removed * RimShare / CellArea / rimWeight;
            for (int z = area.Z0; z <= area.Z1; z++)
            for (int x = area.X0; x <= area.X1; x++)
            {
                float d = Vector2.Distance(SampleWorld(x, z), at);
                if (d < radius || d >= rimOut) continue;
                if (Raise(x, z, perWeight * RimShape(d, radius, rimOut))) Stir(x, z, 1f - (d - radius) / (rimOut - radius) * 0.6f);
            }
        }

        Settle(area.Grow(2, W, D));
        Touch(area.Grow(3, W, D));
        return removed;
    }

    private static float RimShape(float d, float inner, float outer)
    {
        float t = (d - inner) / (outer - inner);
        return MathF.Sin(MathF.PI * MathF.Sqrt(Math.Clamp(t, 0f, 1f)));
    }

    // ------------------------------------------------------------------ digging and piling

    /// <summary>
    /// Digs a scoop out: up to <paramref name="depth"/> at the middle, less toward the edge of <paramref name="radius"/>,
    /// and never more than <paramref name="maxVolume"/> m³ in all. Returns the earth taken out (m³).
    /// </summary>
    public float Dig(Vector2 at, float radius, float depth, float maxVolume = float.PositiveInfinity)
    {
        GridRect area = Around(at, radius);
        if (area.IsEmpty || depth <= 0 || maxVolume <= 0) return 0;
        // How much each sample would give, so a cap on the volume scales them all down evenly.
        float want = 0;
        for (int z = area.Z0; z <= area.Z1; z++)
        for (int x = area.X0; x <= area.X1; x++)
        {
            float d = Vector2.Distance(SampleWorld(x, z), at);
            if (d < radius) want += depth * Falloff(d, radius) * CellArea;
        }

        float scale = want > maxVolume ? maxVolume / want : 1f;
        float got = 0;
        for (int z = area.Z0; z <= area.Z1; z++)
        for (int x = area.X0; x <= area.X1; x++)
        {
            float d = Vector2.Distance(SampleWorld(x, z), at);
            if (d >= radius) continue;
            float down = Lower(x, z, depth * Falloff(d, radius) * scale);
            if (down > 0) Stir(x, z, 1f);
            got += down * CellArea;
        }

        Settle(area.Grow(2, W, D));
        Touch(area.Grow(3, W, D));
        return got;
    }

    /// <summary>Heaps <paramref name="volume"/> m³ of earth in a mound <paramref name="radius"/> across. Returns what was placed.</summary>
    public float Pile(Vector2 at, float radius, float volume)
    {
        GridRect area = Around(at, radius);
        if (area.IsEmpty || volume <= 0) return 0;
        float weight = 0;
        for (int z = area.Z0; z <= area.Z1; z++)
        for (int x = area.X0; x <= area.X1; x++)
        {
            float d = Vector2.Distance(SampleWorld(x, z), at);
            if (d < radius && !IsLocked(x, z) && !IsHeld(x, z)) weight += Falloff(d, radius);
        }

        if (weight <= 0) return 0;
        float perWeight = volume / CellArea / weight;
        float placed = 0;
        for (int z = area.Z0; z <= area.Z1; z++)
        for (int x = area.X0; x <= area.X1; x++)
        {
            float d = Vector2.Distance(SampleWorld(x, z), at);
            if (d >= radius) continue;
            float up = perWeight * Falloff(d, radius);
            if (!Raise(x, z, up)) continue;
            Stir(x, z, 1f);
            placed += up * CellArea;
        }

        Settle(area.Grow(2, W, D));
        Touch(area.Grow(3, W, D));
        return placed;
    }

    private static float Falloff(float d, float r)
    {
        float t = d / r;
        return 1f - t * t;
    }

    /// <summary>
    /// A blade's cut: every sample within <paramref name="halfWidth"/> of the line from <paramref name="a"/> to
    /// <paramref name="b"/> that's above <paramref name="height"/> is cut down toward it, by no more than
    /// <paramref name="maxCut"/> m this time. Returns the earth cut (m³), which the blade then pushes.
    /// </summary>
    public float Cut(Vector2 a, Vector2 b, float halfWidth, float height, float maxCut)
    {
        Vector2 ab = b - a;
        float len = ab.Length();
        if (len < 1e-4f || maxCut <= 0) return 0;
        Vector2 dir = ab / len;
        GridRect area = Around((a + b) * 0.5f, len * 0.5f + halfWidth);
        float got = 0;
        for (int z = area.Z0; z <= area.Z1; z++)
        for (int x = area.X0; x <= area.X1; x++)
        {
            Vector2 p = SampleWorld(x, z) - a;
            float along = Vector2.Dot(p, dir);
            if (along < 0 || along > len || MathF.Abs(dir.X * p.Y - dir.Y * p.X) > halfWidth) continue;
            int i = z * W + x;
            float above = Field.Heights[i] - height;
            if (above <= 0) continue;
            float down = Lower(x, z, MathF.Min(above, maxCut));
            if (down > 0) Stir(x, z, 1f);
            got += down * CellArea;
        }

        if (got > 0)
        {
            Settle(area.Grow(2, W, D));
            Touch(area.Grow(3, W, D));
        }

        return got;
    }

    // ------------------------------------------------------------------ slumping

    /// <summary>
    /// Lets earth slide down any step steeper than the limit, over a few passes, inside <paramref name="r"/>.
    /// Earth only moves between neighbours, so none is made or lost.
    /// </summary>
    public void Settle(GridRect r, int passes = 6)
    {
        if (r.IsEmpty) return;
        float max = MaxStepM * S;
        float[] h = Field.Heights;
        for (int pass = 0; pass < passes; pass++)
        {
            bool moved = false;
            for (int z = r.Z0; z <= r.Z1; z++)
            for (int x = r.X0; x <= r.X1; x++)
            {
                if (IsLocked(x, z)) continue;
                int i = z * W + x;
                // Each sample sheds to its lower neighbours across the four sides.
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= W || nz >= D || IsLocked(nx, nz) || IsHeld(nx, nz)) continue;
                    int j = nz * W + nx;
                    float step = h[i] - h[j];
                    if (step <= max) continue;
                    // Half the excess each time, so it settles evenly instead of see-sawing; never below bedrock.
                    float move = MathF.Min((step - max) * 0.5f, h[i] - Bottom(i));
                    if (move <= 1e-5f) continue;
                    h[i] -= move;
                    h[j] += move;
                    Field.NoteHeight(h[j]);
                    Disturbed[j] = MathF.Max(Disturbed[j], 0.6f);
                    moved = true;
                }
            }

            if (!moved) break;
        }
    }

    /// <summary>Total ground volume above a reference height over a rectangle (for checking that earth is kept).</summary>
    public float VolumeIn(GridRect r, float above = 0f)
    {
        float v = 0;
        for (int z = r.Z0; z <= r.Z1; z++)
        for (int x = r.X0; x <= r.X1; x++)
            v += (Field.Heights[z * W + x] - above) * CellArea;
        return v;
    }

    /// <summary>The grid index of the sample nearest a point (clamped to the grid).</summary>
    public (int x, int z) SampleAt(Vector2 at) =>
        (Math.Clamp((int)MathF.Round((at.X - Field.Origin.X) / S), 0, W - 1), Math.Clamp((int)MathF.Round((at.Y - Field.Origin.Y) / S), 0, D - 1));
}
