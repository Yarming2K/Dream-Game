using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The vehicle builder: place blocks on a grid with an orbit camera, see live stats and plain-English
/// warnings, save and load blueprints, and jump into a test drive.
/// </summary>
public partial class Builder : Node3D
{
    /// <summary>The build area: 320 m across and 80 m high, room for machines of scale.</summary>
    public const int Reach = 320;
    public const int MaxHeight = 160;

    /// <summary>Designs bigger than this are analyzed on a background thread, so editing never stalls.</summary>
    private const int AsyncAnalysisCells = 6000;

    /// <summary>The most cells one box fill or clear may touch.</summary>
    private const int MaxBoxCells = 400_000;
    private const string SaveDir = "user://blueprints";

    public event Action<Blueprint> TestDriveRequested;

    /// <summary>Every filled cell (big blocks under each of their cells), and each block by its anchor.</summary>
    private readonly Dictionary<Int3, BlueprintBlock> _cells = new();
    private readonly Dictionary<Int3, BlueprintBlock> _blocks = new();
    private readonly HashSet<Int3> _tinted = new();
    private BatchedBlocks _batch;
    private Int3 _min, _max;
    private bool _boundsDirty = true;
    private int _popsThisFrame;
    private System.Threading.Tasks.Task<VehicleAnalysis> _analysisTask;
    private bool _analysisAgain;

    // Box tool: drag out a box to fill solid, fill as a hollow shell, or clear.
    private enum BoxMode
    {
        Off,
        Fill,
        Hollow,
        Clear,
    }

    private BoxMode _boxMode;
    private bool _boxActive;
    private Int3 _boxStart, _boxEnd;
    private int _boxLift;
    private MeshInstance3D _boxPreview;
    private Label3D _boxLabel;
    private Button _boxButton;
    private string _name = "Untitled";
    private VehicleAnalysis _analysis;
    private bool _dirty = true;
    private bool _paletteDirty;

    // Camera: the goal values follow input; the current values glide toward them.
    private Camera3D _cam;
    private Vector3 _target = new(0, 0.8f, 0), _goalTarget = new(0, 0.8f, 0);
    private float _yaw = Mathf.DegToRad(140f), _goalYaw = Mathf.DegToRad(140f);
    private float _pitch = Mathf.DegToRad(28f), _goalPitch = Mathf.DegToRad(28f);
    private float _dist = 14f, _goalDist = 14f;

    // Tools
    private BlockDef _selected;
    private int _orientation;
    private bool _mirror = true;
    private int _category;
    private Node3D _ghost, _ghostMirror, _comMarker, _outline;
    private MeshInstance3D _waterline;
    private Label3D _waterlineLabel;
    private StandardMaterial3D _outlineMat;
    private string _ghostKey = "";

    // Mouse state
    private bool _rmbDown, _mmbDown, _lmbDown, _dragged;
    private Vector2 _pressPos;
    private Int3 _paintStart, _paintNormal;
    private List<Change> _stroke;

    private struct Change
    {
        public Int3 Cell;
        public BlueprintBlock Before;
        public BlueprintBlock After;
    }

    private readonly Stack<List<Change>> _undo = new();
    private readonly Stack<List<Change>> _redo = new();

    // UI
    private LineEdit _nameEdit;
    private VBoxContainer _dash;
    private HFlowContainer _categoryFlow;
    private GridContainer _tileGrid;
    private VBoxContainer _partCard;
    private PanelContainer _toast;
    private Label _toastLabel;
    private float _toastTime;
    private PanelContainer _help;
    private Button _mirrorButton;
    private MenuButton _loadButton;
    private readonly List<Action> _loadActions = new();
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Scenery.AddSkyAndSun(this, false);
        BuildFloor();
        _batch = new BatchedBlocks { Name = "Blocks", TintOverlay = BlockVisuals.LooseOverlay };
        AddChild(_batch);
        _boxPreview = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One }, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = Shaders.Hologram(new Color(0.35f, 1f, 0.5f), 0.6f),
        };
        AddChild(_boxPreview);
        _boxLabel = new Label3D
        {
            FontSize = 44, PixelSize = 0.01f, Font = Ui.BodyBoldFont, OutlineSize = 10, NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false, Modulate = new Color(0.8f, 1f, 0.85f),
        };
        AddChild(_boxLabel);
        _cam = new Camera3D { Fov = 50f, Far = 4000f };
        AddChild(_cam);
        _cam.MakeCurrent();
        _comMarker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.25f, 0.6f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true, RenderPriority = 10,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_comMarker);

        // Ships: where the sea will come up to, so you can see what's under water.
        _waterline = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.18f, 0.55f, 0.78f, 0.3f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
        };
        AddChild(_waterline);
        _waterlineLabel = new Label3D
        {
            Text = "waterline", FontSize = 40, PixelSize = 0.006f, Font = Ui.BodyBoldFont, OutlineSize = 8,
            Modulate = new Color(0.55f, 0.85f, 1f), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false,
        };
        AddChild(_waterlineLabel);
        _outline = BuildOutline();
        AddChild(_outline);

        var icons = new Node { Name = "Icons" };
        AddChild(icons);
        PartIcons.Init(icons);
        PartIcons.Updated += () => _paletteDirty = true;
        foreach (BlockDef d in BlockCatalog.All) PartIcons.Get(d);

        BuildUi();
        _selected = BlockCatalog.All[0];
        RefreshPalette();
        LoadBlueprint(ExampleDesigns.Warden(), recordUndo: false, animate: false);
        Toast("Loaded the example Warden tank. Press Tab to drive it, or start changing it.");
        SnapCamera();
    }

    public override void _EnterTree()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Callable.From(() =>
        {
            if (_cam != null && _cam.IsInsideTree()) _cam.MakeCurrent();
        }).CallDeferred();
    }

    private void BuildFloor()
    {
        float size = Reach * 2f * Conv.CS + 40f;
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            Position = new Vector3(0, -Conv.CS * 0.5f - 0.002f, 0),
            MaterialOverride = Shaders.GridFloor(),
        });
        AddChild(new Label3D
        {
            Text = "FRONT", FontSize = 110, PixelSize = 0.008f, Position = new Vector3(0, -0.24f, -8.5f), Font = Ui.HeadingBoldFont,
            RotationDegrees = new Vector3(-90, 0, 0), Modulate = new Color(0.45f, 0.7f, 1f, 0.55f), OutlineSize = 0,
        });
        AddChild(new Label3D
        {
            Text = "^", FontSize = 160, PixelSize = 0.008f, Position = new Vector3(0, -0.24f, -7.2f), Font = Ui.HeadingBoldFont,
            RotationDegrees = new Vector3(-90, 0, 0), Modulate = new Color(0.45f, 0.7f, 1f, 0.45f), OutlineSize = 0,
        });
    }

    /// <summary>A crisp box outline (twelve thin bars) that shows which block is under the cursor.</summary>
    private Node3D BuildOutline()
    {
        _outlineMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 1, 1, 0.9f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = 5,
        };
        var root = new Node3D { Name = "Outline", Visible = false };
        float h = Conv.CS * 0.52f, t = 0.012f;
        for (int axis = 0; axis < 3; axis++)
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
        {
            var size = new Vector3(t, t, t);
            size[axis] = h * 2f + t;
            var pos = Vector3.Zero;
            pos[(axis + 1) % 3] = a * h;
            pos[(axis + 2) % 3] = b * h;
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size }, MaterialOverride = _outlineMat, Position = pos,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        return root;
    }

    // ---------- Editing ----------

    private static BlockDef DefOf(BlueprintBlock b) => BlockCatalog.Get(b.Id);

    /// <summary>Puts a block at its anchor (or takes away whatever block has that anchor, with null).</summary>
    private void SetCell(Int3 anchor, BlueprintBlock b, bool animate)
    {
        if (_blocks.TryGetValue(anchor, out BlueprintBlock old))
        {
            foreach (Int3 c in Footprint.Cells(DefOf(old), old.Cell, old.O)) _cells.Remove(c);
            _blocks.Remove(anchor);
            _tinted.Remove(anchor);
            _batch.Remove(anchor);
            if (animate) Pop(old, appearing: false);
        }

        if (b != null)
        {
            BlockDef def = DefOf(b);
            foreach (Int3 c in Footprint.Cells(def, b.Cell, b.O)) _cells[c] = b;
            _blocks[anchor] = b;
            _batch.Set(anchor, def, b.O);
            if (animate) Pop(b, appearing: true);
            if (!_boundsDirty)
            {
                foreach (Int3 c in Footprint.Cells(def, b.Cell, b.O))
                {
                    _min = Int3.Min(_min, c);
                    _max = Int3.Max(_max, c);
                }
            }
        }
        else
        {
            _boundsDirty = true;
        }

        _dirty = true;
    }

    /// <summary>A quick flash where a block appears or disappears (the blocks themselves are drawn in batches).</summary>
    private void Pop(BlueprintBlock b, bool appearing)
    {
        if (_popsThisFrame++ > 24) return;
        Node3D v = BlockVisuals.Create(DefOf(b), 76f);
        BlockVisuals.SetOverride(v, appearing ? BlockVisuals.GhostOk : BlockVisuals.GhostBad);
        v.Transform = Conv.BlockTransform(b.Cell, b.O);
        AddChild(v);
        Tween tw = v.CreateTween();
        if (appearing)
            tw.TweenProperty(v, "scale", Vector3.One * 1.06f, 0.16).From(Vector3.One * 0.55f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        else
            tw.TweenProperty(v, "scale", Vector3.One * 0.05f, 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(v.QueueFree));
    }

    private bool InBounds(Int3 c) => Math.Abs(c.X) <= Reach && Math.Abs(c.Z) <= Reach && c.Y >= 0 && c.Y <= MaxHeight;

    /// <summary>Whether a block fits here: every cell it would fill is inside the build area and empty.</summary>
    private bool Fits(BlockDef def, Int3 anchor, int o)
    {
        foreach (Int3 c in Footprint.Cells(def, anchor, o))
            if (!InBounds(c) || _cells.ContainsKey(c)) return false;
        return true;
    }

    /// <summary>The anchor of the mirror image of a block (big blocks with an even size need nudging to land exactly).</summary>
    private static Int3 MirrorAnchor(BlockDef def, Int3 anchor, int o)
    {
        var m = new Int3(-anchor.X, anchor.Y, anchor.Z);
        if (!def.IsMultiCell) return m;
        int mo = Orientations.MirrorX(o);
        int want = int.MaxValue, have = int.MaxValue;
        foreach (Int3 c in Footprint.Cells(def, anchor, o)) want = Math.Min(want, -c.X);
        foreach (Int3 c in Footprint.Cells(def, m, mo)) have = Math.Min(have, c.X);
        return m + new Int3(want - have, 0, 0);
    }

    private bool Place(Int3 anchor, List<Change> into)
    {
        bool placed = PlaceOne(_selected, anchor, _orientation, into);
        if (_mirror) PlaceOne(_selected, MirrorAnchor(_selected, anchor, _orientation), Orientations.MirrorX(_orientation), into);
        if (placed)
            Sfx.Play2D(this, _selected.MassKg >= 200 ? SoundId.PlaceHeavy : SoundId.PlaceLight, -4f, _rng.RandfRange(0.92f, 1.08f));
        return placed;
    }

    private bool PlaceOne(BlockDef def, Int3 anchor, int o, List<Change> into, bool animate = true)
    {
        if (!Fits(def, anchor, o)) return false;
        var b = new BlueprintBlock(def.Id, anchor, o);
        into.Add(new Change { Cell = anchor, Before = null, After = b });
        SetCell(anchor, b, animate);
        return true;
    }

    private void Remove(Int3 c)
    {
        var changes = new List<Change>();
        if (_cells.TryGetValue(c, out BlueprintBlock b))
        {
            Int3 anchor = b.Cell;
            RemoveOne(anchor, changes, true);
            if (_mirror)
            {
                Int3 ma = MirrorAnchor(DefOf(b), anchor, b.O);
                if (_cells.TryGetValue(ma, out BlueprintBlock mb) || _cells.TryGetValue(new Int3(-c.X, c.Y, c.Z), out mb))
                    RemoveOne(mb.Cell, changes, true);
            }
        }

        if (changes.Count > 0) Sfx.Play2D(this, SoundId.Remove, -5f, _rng.RandfRange(0.92f, 1.1f));
        Commit(changes);
    }

    /// <summary>Takes away the block filling cell <paramref name="c"/> (any of its cells will do).</summary>
    private void RemoveOne(Int3 c, List<Change> into, bool animate)
    {
        if (!_cells.TryGetValue(c, out BlueprintBlock old)) return;
        into.Add(new Change { Cell = old.Cell, Before = old, After = null });
        SetCell(old.Cell, null, animate);
    }

    private void Commit(List<Change> changes)
    {
        if (changes == null || changes.Count == 0) return;
        _undo.Push(changes);
        _redo.Clear();
    }

    private void Undo(bool redo)
    {
        Stack<List<Change>> from = redo ? _redo : _undo, to = redo ? _undo : _redo;
        if (from.Count == 0)
        {
            Toast(redo ? "Nothing to redo." : "Nothing to undo.");
            Sfx.Play2D(this, SoundId.Error, -12f);
            return;
        }

        List<Change> changes = from.Pop();
        bool animate = changes.Count <= 24;
        IEnumerable<Change> order = redo ? changes : Enumerable.Reverse(changes);
        foreach (Change ch in order) SetCell(ch.Cell, redo ? ch.After : ch.Before, animate);
        to.Push(changes);
        Sfx.Play2D(this, SoundId.Tick, -6f, redo ? 1.15f : 0.9f);
    }

    private void LoadBlueprint(Blueprint bp, bool recordUndo = true, bool animate = false)
    {
        var changes = new List<Change>();
        foreach (Int3 a in _blocks.Keys.ToList()) RemoveOne(a, changes, false);
        foreach (BlueprintBlock b in bp.Blocks)
        {
            var copy = new BlueprintBlock(b.Id, b.Cell, b.O);
            if (!BlockCatalog.TryGet(b.Id, out BlockDef def) || !Fits(def, b.Cell, b.O)) continue;
            changes.Add(new Change { Cell = b.Cell, Before = null, After = copy });
            SetCell(b.Cell, copy, animate);
        }

        if (recordUndo) Commit(changes);
        _name = bp.Name;
        if (_nameEdit != null) _nameEdit.Text = _name;
        FocusCamera();
    }

    public Blueprint CurrentBlueprint() => Blueprint.FromCellMap(_name, _blocks.Values);

    /// <summary>The box around every filled cell (kept up to date as blocks are added; recounted after removals).</summary>
    private (Int3 min, Int3 max) Bounds()
    {
        if (_boundsDirty)
        {
            _boundsDirty = false;
            bool first = true;
            foreach (Int3 c in _cells.Keys)
            {
                if (first)
                {
                    _min = _max = c;
                    first = false;
                    continue;
                }

                _min = Int3.Min(_min, c);
                _max = Int3.Max(_max, c);
            }

            if (first) _min = _max = Int3.Zero;
        }

        return (_min, _max);
    }

    // ---------- Picking ----------

    private bool PickCell(out Int3 hitCell, out Int3 normal, out bool onBlock)
    {
        hitCell = default;
        normal = Int3.Up;
        onBlock = false;
        Vector2 m = GetViewport().GetMousePosition();
        Vector3 o = _cam.ProjectRayOrigin(m);
        Vector3 d = _cam.ProjectRayNormal(m);

        if (_cells.Count > 0)
        {
            (Int3 min, Int3 max) = Bounds();
            var hits = new List<RayCellHit>(1);
            GridRaycast.Cast(new BlueprintShapeLookup(_cells), min, max, (o / Conv.CS).N(), d.N(), 8000f, hits, firstOnly: true);
            if (hits.Count > 0)
            {
                hitCell = hits[0].Cell;
                normal = Int3.DominantAxis(hits[0].Normal);
                onBlock = true;
                return true;
            }
        }

        // The floor sits at the bottom of the y = 0 row.
        float floorY = -Conv.CS * 0.5f;
        if (d.Y >= -1e-4f) return false;
        float t = (floorY - o.Y) / d.Y;
        Vector3 p = o + d * t;
        hitCell = new Int3(Mathf.RoundToInt(p.X / Conv.CS), -1, Mathf.RoundToInt(p.Z / Conv.CS));
        normal = Int3.Up;
        return Math.Abs(hitCell.X) <= Reach && Math.Abs(hitCell.Z) <= Reach;
    }

    /// <summary>
    /// Where the selected block would go: against the face under the cursor. A big block sits out from the
    /// face by however far it reaches back from its anchor.
    /// </summary>
    private bool PlacementTarget(out Int3 anchor)
    {
        anchor = default;
        if (!PickCell(out Int3 hit, out Int3 n, out _)) return false;
        anchor = hit + n * (1 + Footprint.Reach(_selected, _orientation, -n));
        return Fits(_selected, anchor, _orientation);
    }

    private bool OverUi() => GetViewport().GuiGetHoveredControl() != null;

    private bool Typing() => GetViewport().GuiGetFocusOwner() is LineEdit;

    // ---------- Input ----------

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
            case InputEventMouseMotion mm:
                HandleMouseMotion(mm);
                break;
            case InputEventKey { Pressed: true } key:
                HandleKey(key);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        // Clicking the 3D view takes keyboard focus away from the name box, so shortcut keys work again.
        if (mb.Pressed) GetViewport().GuiReleaseFocus();
        switch (mb.ButtonIndex)
        {
            case MouseButton.WheelUp when mb.Pressed && _boxActive:
                _boxLift++;
                break;
            case MouseButton.WheelDown when mb.Pressed && _boxActive:
                _boxLift--;
                break;
            case MouseButton.WheelUp when mb.Pressed:
                _goalDist = Mathf.Clamp(_goalDist * 0.88f, 3f, 700f);
                break;
            case MouseButton.WheelDown when mb.Pressed:
                _goalDist = Mathf.Clamp(_goalDist * 1.12f, 3f, 700f);
                break;
            case MouseButton.Left when _boxMode != BoxMode.Off:
                if (mb.Pressed && BoxCorner(out Int3 corner))
                {
                    _boxActive = true;
                    _boxStart = _boxEnd = corner;
                    _boxLift = 0;
                }
                else if (!mb.Pressed && _boxActive)
                {
                    _boxActive = false;
                    ApplyBox();
                }

                break;
            case MouseButton.Left:
                if (mb.Pressed)
                {
                    _lmbDown = true;
                    if (PlacementTarget(out Int3 target))
                    {
                        _stroke = new List<Change>();
                        PickCell(out _, out Int3 faceNormal, out _);
                        _paintStart = target;
                        _paintNormal = faceNormal;
                        Place(target, _stroke);
                    }
                    else if (PickCell(out _, out _, out _))
                    {
                        Sfx.Play2D(this, SoundId.Error, -10f);
                    }
                }
                else
                {
                    _lmbDown = false;
                    Commit(_stroke);
                    _stroke = null;
                }

                break;
            case MouseButton.Right:
                if (mb.Pressed)
                {
                    _rmbDown = true;
                    _dragged = false;
                    _pressPos = mb.Position;
                }
                else if (_rmbDown)
                {
                    _rmbDown = false;
                    if (!_dragged && PickCell(out Int3 removeCell, out _, out bool onBlockR) && onBlockR) Remove(removeCell);
                }

                break;
            case MouseButton.Middle:
                if (mb.Pressed)
                {
                    _mmbDown = true;
                    _dragged = false;
                    _pressPos = mb.Position;
                }
                else if (_mmbDown)
                {
                    _mmbDown = false;
                    if (!_dragged && PickCell(out Int3 pickCell, out _, out bool onBlockM) && onBlockM)
                    {
                        BlueprintBlock b = _cells[pickCell];
                        Select(BlockCatalog.Get(b.Id));
                        _orientation = b.O;
                        Toast($"Copied {_selected.Name}.");
                    }
                }

                break;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        if ((_rmbDown || _mmbDown) && mm.Position.DistanceTo(_pressPos) > 4f) _dragged = true;
        if (_rmbDown && _dragged)
        {
            _goalYaw -= mm.Relative.X * 0.006f;
            _goalPitch = Mathf.Clamp(_goalPitch + mm.Relative.Y * 0.006f, Mathf.DegToRad(-10f), Mathf.DegToRad(88f));
        }
        else if (_mmbDown && _dragged)
        {
            Basis b = _cam.GlobalTransform.Basis;
            _goalTarget += (-b.X * mm.Relative.X + b.Y * mm.Relative.Y) * _dist * 0.0016f;
        }
        else if (_boxActive)
        {
            if (BoxCorner(out Int3 corner)) _boxEnd = corner;
        }
        else if (_lmbDown && _stroke != null && PlacementTarget(out Int3 c))
        {
            // Painting: stay on the layer the stroke started on.
            Int3 rel = c - _paintStart;
            int along = rel.X * _paintNormal.X + rel.Y * _paintNormal.Y + rel.Z * _paintNormal.Z;
            if (along == 0) Place(c, _stroke);
        }
    }

    private void HandleKey(InputEventKey k)
    {
        bool ctrl = k.CtrlPressed || k.MetaPressed;
        int dir = k.ShiftPressed ? -1 : 1;
        switch (k.PhysicalKeycode)
        {
            case Key.Z when ctrl:
                Undo(redo: k.ShiftPressed);
                break;
            case Key.Y when ctrl:
                Undo(redo: true);
                break;
            case Key.S when ctrl:
                Save();
                break;
            case Key.R:
                Rotate(Orientations.RotateAroundY(_orientation, dir));
                break;
            case Key.T:
                Rotate(Orientations.RotateAroundX(_orientation, dir));
                break;
            case Key.G:
                Rotate(Orientations.RotateAroundZ(_orientation, dir));
                break;
            case Key.M:
                SetMirror(!_mirror);
                Sfx.Play2D(this, SoundId.Tick, -6f);
                break;
            case Key.B:
                SetBoxMode((BoxMode)(((int)_boxMode + 1) % 4));
                break;
            case Key.F:
                FocusCamera();
                break;
            case Key.Tab:
                RequestTestDrive();
                break;
            case Key.F1:
                _help.Visible = !_help.Visible;
                break;
            case Key.Escape:
                _help.Visible = false;
                break;
            case Key.Bracketleft:
                SetCategory((_category + BlockCatalog.Categories.Length - 1) % BlockCatalog.Categories.Length);
                break;
            case Key.Bracketright:
                SetCategory((_category + 1) % BlockCatalog.Categories.Length);
                break;
            default:
                if (k.PhysicalKeycode >= Key.Key1 && k.PhysicalKeycode <= Key.Key9)
                {
                    List<BlockDef> defs = BlockCatalog.InCategory(BlockCatalog.Categories[_category]);
                    int i = (int)(k.PhysicalKeycode - Key.Key1);
                    if (i < defs.Count) Select(defs[i]);
                }

                break;
        }
    }

    private void Rotate(int orientation)
    {
        _orientation = orientation;
        Sfx.Play2D(this, SoundId.Tick, -8f, _rng.RandfRange(1f, 1.15f));
        if (_ghost != null)
        {
            _ghost.Scale = Vector3.One * 0.85f;
            _ghost.CreateTween().TweenProperty(_ghost, "scale", Vector3.One, 0.12).SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
        }
    }

    // ---------- Frame ----------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _popsThisFrame = 0;
        if (_dirty)
        {
            _dirty = false;
            // Big designs are analyzed in the background; the dashboard catches up a moment later.
            if (_cells.Count < AsyncAnalysisCells && _analysisTask == null) ApplyAnalysis(VehicleAnalysis.Analyze(_cells));
            else if (_analysisTask == null) StartAnalysis();
            else _analysisAgain = true;
        }

        if (_analysisTask is { IsCompleted: true })
        {
            VehicleAnalysis done = _analysisTask.IsFaulted ? null : _analysisTask.Result;
            _analysisTask = null;
            if (done != null) ApplyAnalysis(done);
            if (_analysisAgain)
            {
                _analysisAgain = false;
                StartAnalysis();
            }
        }

        UpdateBoxPreview();

        if (_paletteDirty)
        {
            _paletteDirty = false;
            RefreshPalette();
        }

        MoveCamera(dt);
        UpdateGhost();
        if (_toastTime > 0)
        {
            _toastTime -= dt;
            _toast.Modulate = new Color(1, 1, 1, Mathf.Clamp(_toastTime * 2f, 0, 1));
            _toast.Visible = _toastTime > 0;
        }
    }

    private void MoveCamera(float dt)
    {
        if (!Typing() && !Input.IsKeyPressed(Key.Ctrl))
        {
            var fwd = new Vector3(-Mathf.Sin(_goalYaw), 0, -Mathf.Cos(_goalYaw));
            var right = new Vector3(Mathf.Cos(_goalYaw), 0, -Mathf.Sin(_goalYaw));
            Vector3 move = Vector3.Zero;
            if (Input.IsPhysicalKeyPressed(Key.W)) move += fwd;
            if (Input.IsPhysicalKeyPressed(Key.S)) move -= fwd;
            if (Input.IsPhysicalKeyPressed(Key.D)) move += right;
            if (Input.IsPhysicalKeyPressed(Key.A)) move -= right;
            if (Input.IsPhysicalKeyPressed(Key.E)) move += Vector3.Up;
            if (Input.IsPhysicalKeyPressed(Key.Q)) move -= Vector3.Up;
            if (move != Vector3.Zero) _goalTarget += move.Normalized() * Mathf.Max(_goalDist * 0.9f, 4f) * dt;
        }

        float k = 1f - Mathf.Exp(-dt * 14f);
        _target = _target.Lerp(_goalTarget, k);
        _yaw = Mathf.Lerp(_yaw, _goalYaw, k);
        _pitch = Mathf.Lerp(_pitch, _goalPitch, k);
        _dist = Mathf.Lerp(_dist, _goalDist, k);
        ApplyCamera();
    }

    private void ApplyCamera()
    {
        var offset = new Vector3(Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw));
        _cam.Position = _target + offset * _dist;
        _cam.LookAt(_target, Vector3.Up);
    }

    private void SnapCamera()
    {
        _target = _goalTarget;
        _yaw = _goalYaw;
        _pitch = _goalPitch;
        _dist = _goalDist;
        ApplyCamera();
    }

    private void FocusCamera()
    {
        if (_cells.Count == 0)
        {
            _goalTarget = new Vector3(0, 0.8f, 0);
        }
        else
        {
            (Int3 min, Int3 max) = Bounds();
            _goalTarget = (min.G() + max.G()) * 0.5f * Conv.CS;
            _goalDist = Mathf.Clamp((max - min).ToVector3().Length() * Conv.CS * 1.7f + 4f, 6f, 600f);
        }
    }

    private void UpdateGhost()
    {
        bool picked = PickCell(out Int3 hit, out Int3 n, out bool onBlock);
        bool free = picked && !OverUi() && !_mmbDown && !(_rmbDown && _dragged);

        // Outline the block under the cursor; it turns red while right-click would remove it.
        _outline.Visible = free && onBlock && !_boxActive;
        if (_outline.Visible)
        {
            // Outline the whole block under the cursor, big ones included.
            BlueprintBlock under = _cells[hit];
            BlockDef ud = DefOf(under);
            _outline.Position = Footprint.Center(ud, under.Cell, under.O).G() * Conv.CS;
            Vector3 size = Orientations.Apply(under.O, ud.Size.ToVector3()).G().Abs();
            _outline.Scale = size;
            _outlineMat.AlbedoColor = _rmbDown ? new Color(1f, 0.35f, 0.3f, 1f) : new Color(1, 1, 1, 0.75f);
        }

        bool show = free && !_rmbDown && _boxMode == BoxMode.Off;
        Int3 cell = show ? hit + n * (1 + Footprint.Reach(_selected, _orientation, -n)) : default;
        string key = $"{_selected.Id}:{_orientation}";
        if (key != _ghostKey)
        {
            _ghost?.QueueFree();
            _ghostMirror?.QueueFree();
            _ghost = BlockVisuals.Create(_selected, 76f, hints: true);
            _ghostMirror = BlockVisuals.Create(_selected, 76f, hints: true);
            AddChild(_ghost);
            AddChild(_ghostMirror);
            _ghostKey = key;
        }

        bool ok = show && Fits(_selected, cell, _orientation);
        _ghost.Visible = show;
        if (show)
        {
            _ghost.Position = Conv.CellPos(cell);
            _ghost.Basis = Conv.BasisOf(_orientation).Scaled(_ghost.Basis.Scale);
            BlockVisuals.SetOverride(_ghost, ok ? BlockVisuals.GhostOk : BlockVisuals.GhostBad);
        }

        Int3 mc = MirrorAnchor(_selected, cell, _orientation);
        int mo = Orientations.MirrorX(_orientation);
        bool overlapsSelf = Footprint.Cells(_selected, mc, mo).Any(x => Footprint.Cells(_selected, cell, _orientation).Contains(x));
        bool mirrorShow = show && _mirror && !overlapsSelf;
        _ghostMirror.Visible = mirrorShow;
        if (mirrorShow)
        {
            _ghostMirror.Transform = Conv.BlockTransform(mc, mo);
            bool mok = Fits(_selected, mc, mo);
            BlockVisuals.SetOverride(_ghostMirror, mok ? BlockVisuals.GhostOk : BlockVisuals.GhostBad);
        }
    }

    private void StartAnalysis()
    {
        var snapshot = new Dictionary<Int3, BlueprintBlock>(_cells);
        _analysisTask = System.Threading.Tasks.Task.Run(() => VehicleAnalysis.Analyze(snapshot));
    }

    private void ApplyAnalysis(VehicleAnalysis a)
    {
        _analysis = a;

        // Barrels take the caliber of the gun they belong to.
        foreach (GunInfo g in _analysis.Guns)
        {
            foreach (Int3 c in g.Barrels)
                if (_blocks.ContainsKey(c)) _batch.SetCaliber(c, g.Stats.CaliberMm);
            if (g.Brake.HasValue && _blocks.ContainsKey(g.Brake.Value)) _batch.SetCaliber(g.Brake.Value, g.Stats.CaliberMm);
        }

        // Tint blocks that aren't attached.
        var loose = new HashSet<Int3>();
        foreach (Int3 c in _analysis.Disconnected)
            if (_cells.TryGetValue(c, out BlueprintBlock b)) loose.Add(b.Cell);
        foreach (Int3 a0 in _tinted.Where(x => !loose.Contains(x)).ToList())
        {
            _batch.SetTint(a0, false);
            _tinted.Remove(a0);
        }

        foreach (Int3 a1 in loose)
            if (_tinted.Add(a1)) _batch.SetTint(a1, true);

        _comMarker.Visible = _cells.Count > 0;
        _comMarker.Position = _analysis.CenterOfMass.G();
        UpdateWaterline(_analysis);
        RebuildDashboard(_analysis);
    }

    private void UpdateWaterline(VehicleAnalysis a)
    {
        bool show = a.IsNaval && a.Cells.Count > 0;
        _waterline.Visible = _waterlineLabel.Visible = show;
        if (!show) return;
        float cs = Conv.CS;
        float w = (a.Max.X - a.Min.X + 5) * cs, l = (a.Max.Z - a.Min.Z + 5) * cs;
        var center = new Vector3((a.Min.X + a.Max.X) * 0.5f * cs, a.Water.WaterlineY, (a.Min.Z + a.Max.Z) * 0.5f * cs);
        _waterline.Position = center;
        _waterline.Scale = new Vector3(w, 1, l);
        _waterlineLabel.Position = center + new Vector3(w * 0.5f + 0.4f, 0.15f, 0);
        _waterlineLabel.Text = a.Water.Floats ? $"waterline · {a.Water.DraftM:0.0} m draft" : "sinks";
    }

    // ---------- Box tool ----------

    private void SetBoxMode(BoxMode mode)
    {
        _boxMode = mode;
        _boxActive = false;
        if (_boxButton != null)
        {
            _boxButton.Text = mode == BoxMode.Off ? "BOX" : $"BOX: {mode.ToString().ToUpperInvariant()}";
            _boxButton.SetPressedNoSignal(mode != BoxMode.Off);
        }

        Toast(mode switch
        {
            BoxMode.Fill => "Box fill: drag out a box to fill it with the selected block. Mouse wheel while dragging makes it taller.",
            BoxMode.Hollow => "Hollow box: drag out a box to build just its walls, floor and roof (a hull in one go).",
            BoxMode.Clear => "Box clear: drag out a box to remove everything in it.",
            _ => "Box tool off.",
        });
        Sfx.Play2D(this, SoundId.Tick, -6f, 0.8f + 0.1f * (int)mode);
    }

    /// <summary>The cell under the cursor for a box corner: in front of the face for filling, the block itself for clearing.</summary>
    private bool BoxCorner(out Int3 corner)
    {
        corner = default;
        if (OverUi() || !PickCell(out Int3 hit, out Int3 n, out bool onBlock)) return false;
        corner = _boxMode == BoxMode.Clear && onBlock ? hit : hit + n;
        corner = new Int3(Math.Clamp(corner.X, -Reach, Reach), Math.Clamp(corner.Y, 0, MaxHeight), Math.Clamp(corner.Z, -Reach, Reach));
        return true;
    }

    private (Int3 lo, Int3 hi) BoxRange()
    {
        Int3 lo = Int3.Min(_boxStart, _boxEnd), hi = Int3.Max(_boxStart, _boxEnd);
        if (_boxLift > 0) hi = new Int3(hi.X, Math.Min(hi.Y + _boxLift, MaxHeight), hi.Z);
        else if (_boxLift < 0) lo = new Int3(lo.X, Math.Max(lo.Y + _boxLift, 0), lo.Z);
        return (lo, hi);
    }

    private void UpdateBoxPreview()
    {
        _boxPreview.Visible = _boxLabel.Visible = _boxActive;
        if (!_boxActive) return;
        (Int3 lo, Int3 hi) = BoxRange();
        Int3 dims = hi - lo + new Int3(1, 1, 1);
        Vector3 center = (lo.G() + hi.G()) * 0.5f * Conv.CS;
        _boxPreview.Position = center;
        _boxPreview.Scale = dims.G() * Conv.CS + Vector3.One * 0.02f;
        _boxLabel.Position = center + Vector3.Up * (dims.Y * Conv.CS * 0.5f + 0.6f);
        string what = _boxMode switch { BoxMode.Clear => "clear", BoxMode.Hollow => "hollow", _ => "fill" };
        _boxLabel.Text = $"{what}  {dims.X} × {dims.Y} × {dims.Z}  ({dims.X * Conv.CS:0.#} × {dims.Y * Conv.CS:0.#} × {dims.Z * Conv.CS:0.#} m)";
        _boxLabel.Modulate = _boxMode == BoxMode.Clear ? new Color(1f, 0.6f, 0.55f) : new Color(0.8f, 1f, 0.85f);
    }

    private void ApplyBox()
    {
        (Int3 lo, Int3 hi) = BoxRange();
        Int3 dims = hi - lo + new Int3(1, 1, 1);
        long volume = (long)dims.X * dims.Y * dims.Z;
        if (volume > MaxBoxCells)
        {
            Toast($"That box is {volume:N0} cells; the most is {MaxBoxCells:N0}. Do it in parts.");
            Sfx.Play2D(this, SoundId.Error, -8f);
            return;
        }

        var changes = new List<Change>();
        bool Inside(Int3 c, Int3 a, Int3 b) => c.X >= a.X && c.Y >= a.Y && c.Z >= a.Z && c.X <= b.X && c.Y <= b.Y && c.Z <= b.Z;
        if (_boxMode == BoxMode.Clear)
        {
            var boxes = new List<(Int3, Int3)> { (lo, hi) };
            if (_mirror) boxes.Add((new Int3(-hi.X, lo.Y, lo.Z), new Int3(-lo.X, hi.Y, hi.Z)));
            var anchors = new HashSet<Int3>();
            foreach ((Int3 a, Int3 b) in boxes)
            {
                if (volume < _cells.Count)
                {
                    for (int x = a.X; x <= b.X; x++)
                    for (int y = a.Y; y <= b.Y; y++)
                    for (int z = a.Z; z <= b.Z; z++)
                        if (_cells.TryGetValue(new Int3(x, y, z), out BlueprintBlock bb)) anchors.Add(bb.Cell);
                }
                else
                {
                    foreach ((Int3 c, BlueprintBlock bb) in _cells)
                        if (Inside(c, a, b)) anchors.Add(bb.Cell);
                }
            }

            foreach (Int3 a in anchors) RemoveOne(a, changes, false);
            if (changes.Count > 0) Sfx.Play2D(this, SoundId.Remove, -3f, 0.8f);
            Toast(changes.Count > 0 ? $"Removed {changes.Count:N0} block(s)." : "Nothing in that box.");
            Commit(changes);
            return;
        }

        // Step through the box a block's width at a time, so big blocks tile it too.
        Int3 offMin = default, offMax = default;
        bool first = true;
        foreach (Int3 c in Footprint.Cells(_selected, Int3.Zero, _orientation))
        {
            offMin = first ? c : Int3.Min(offMin, c);
            offMax = first ? c : Int3.Max(offMax, c);
            first = false;
        }

        Int3 ext = offMax - offMin + new Int3(1, 1, 1);
        int mo = Orientations.MirrorX(_orientation);
        for (int x = lo.X; x + ext.X - 1 <= hi.X; x += ext.X)
        for (int y = lo.Y; y + ext.Y - 1 <= hi.Y; y += ext.Y)
        for (int z = lo.Z; z + ext.Z - 1 <= hi.Z; z += ext.Z)
        {
            if (_boxMode == BoxMode.Hollow)
            {
                bool interior = x > lo.X && x + ext.X - 1 < hi.X && y > lo.Y && y + ext.Y - 1 < hi.Y && z > lo.Z && z + ext.Z - 1 < hi.Z;
                if (interior) continue;
            }

            Int3 anchor = new Int3(x, y, z) - offMin;
            PlaceOne(_selected, anchor, _orientation, changes, animate: false);
            if (_mirror) PlaceOne(_selected, MirrorAnchor(_selected, anchor, _orientation), mo, changes, animate: false);
        }

        if (changes.Count > 0) Sfx.Play2D(this, _selected.MassKg >= 200 ? SoundId.PlaceHeavy : SoundId.PlaceLight, -2f, 0.85f);
        Toast(changes.Count > 0 ? $"Placed {changes.Count:N0} block(s)." : "No room in that box.");
        Commit(changes);
    }

    // ---------- UI: layout ----------

    private void BuildUi()
    {
        Control root = Ui.Root(this);

        PanelContainer top = Ui.Card("TopBar");
        root.AddChild(top);
        top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        HBoxContainer bar = Ui.Row(8);
        top.AddChild(bar);
        Label title = Ui.Title("BUILDER");
        title.AddThemeColorOverride("font_color", Ui.Accent);
        bar.AddChild(title);
        bar.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
        _nameEdit = new LineEdit { Text = _name, CustomMinimumSize = new Vector2(260, 0), PlaceholderText = "Name your design" };
        _nameEdit.TextChanged += t => _name = string.IsNullOrWhiteSpace(t) ? "Untitled" : t.Trim();
        _nameEdit.TextSubmitted += _ => _nameEdit.ReleaseFocus();
        bar.AddChild(_nameEdit);
        bar.AddChild(Ui.Button("Undo", () => Undo(false), "Undo (Ctrl+Z)", "GhostButton"));
        bar.AddChild(Ui.Button("Redo", () => Undo(true), "Redo (Ctrl+Y)", "GhostButton"));
        bar.AddChild(new VSeparator());
        bar.AddChild(Ui.Button("New", NewDesign, "Start an empty design (Undo brings the old one back)"));
        bar.AddChild(Ui.Button("Save", Save, "Save to your blueprints folder (Ctrl+S)"));
        _loadButton = new MenuButton
        {
            Text = "Load", TooltipText = "Open an example or one of your saved designs", ThemeTypeVariation = "Button",
            FocusMode = Control.FocusModeEnum.None,
        };
        _loadButton.AboutToPopup += FillLoadMenu;
        _loadButton.GetPopup().IdPressed += id =>
        {
            if (id >= 0 && id < _loadActions.Count) _loadActions[(int)id]();
        };
        bar.AddChild(_loadButton);
        bar.AddChild(Ui.Button("Folder", OpenFolder, "Open the folder with your blueprint files, to share them with friends"));
        bar.AddChild(Ui.Spacer());
        _mirrorButton = Ui.Button("MIRROR", () => SetMirror(!_mirror), "Build both sides at once (M)", "Pill");
        _mirrorButton.ToggleMode = true;
        bar.AddChild(_mirrorButton);
        _boxButton = Ui.Button("BOX", () => SetBoxMode((BoxMode)(((int)_boxMode + 1) % 4)),
            "Box tool (B): drag out a box to fill it, build a hollow shell, or clear it", "Pill");
        _boxButton.ToggleMode = true;
        bar.AddChild(_boxButton);
        SetMirror(_mirror);
        bar.AddChild(Ui.Button("Help", () => _help.Visible = !_help.Visible, "Controls and how designs work (F1)", "GhostButton"));
        bar.AddChild(Ui.Button("TEST DRIVE", RequestTestDrive, "Drive this design on the test range (Tab)", "PrimaryButton"));

        // Parts drawer on the left.
        var left = new PanelContainer();
        root.AddChild(left);
        left.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        left.OffsetLeft = 12;
        left.OffsetRight = 12 + 300;
        left.OffsetTop = 72;
        left.OffsetBottom = -12;
        VBoxContainer lcol = Ui.Column(10);
        left.AddChild(lcol);
        lcol.AddChild(Ui.Section("Parts"));
        _categoryFlow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _categoryFlow.AddThemeConstantOverride("h_separation", 5);
        _categoryFlow.AddThemeConstantOverride("v_separation", 5);
        lcol.AddChild(_categoryFlow);
        _tileGrid = new GridContainer { Columns = 3, MouseFilter = Control.MouseFilterEnum.Ignore };
        _tileGrid.AddThemeConstantOverride("h_separation", 6);
        _tileGrid.AddThemeConstantOverride("v_separation", 6);
        lcol.AddChild(_tileGrid);
        lcol.AddChild(new HSeparator());
        _partCard = Ui.Column(8);
        _partCard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        lcol.AddChild(_partCard);

        // Design dashboard on the right.
        var right = new PanelContainer();
        root.AddChild(right);
        right.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        right.OffsetLeft = -352;
        right.OffsetRight = -12;
        right.OffsetTop = 72;
        right.OffsetBottom = -12;
        right.GrowHorizontal = Control.GrowDirection.Begin;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        right.AddChild(scroll);
        _dash = Ui.Column(10);
        _dash.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _dash.CustomMinimumSize = new Vector2(310, 0);
        scroll.AddChild(_dash);

        // Key hints along the bottom.
        var hints = new PanelContainer();
        root.AddChild(hints);
        hints.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        hints.GrowHorizontal = Control.GrowDirection.Both;
        hints.GrowVertical = Control.GrowDirection.Begin;
        hints.OffsetBottom = -14;
        HBoxContainer hintRow = Ui.Row(14);
        hints.AddChild(hintRow);
        foreach ((string key, string what) in new[]
                 {
                     ("LMB", "place"), ("RMB", "remove"), ("MMB", "copy"), ("R T G", "rotate"), ("WASD Q E", "move"),
                     ("M", "mirror"), ("B", "box"), ("F1", "help"),
                 })
        {
            HBoxContainer pair = Ui.Row(5);
            pair.AddChild(Ui.Chip(null, key, Ui.Accent));
            pair.AddChild(Ui.Small(what));
            hintRow.AddChild(pair);
        }

        _toast = Ui.Card("Toast");
        _toast.Visible = false;
        _toast.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_toast);
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _toast.GrowHorizontal = Control.GrowDirection.Both;
        _toast.OffsetTop = 80;
        _toastLabel = Ui.Label("", 16);
        _toast.AddChild(_toastLabel);

        _help = new PanelContainer { Visible = false };
        root.AddChild(_help);
        _help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        VBoxContainer helpCol = Ui.Column(10);
        _help.AddChild(helpCol);
        helpCol.AddChild(Ui.Title("HOW TO BUILD"));
        RichTextLabel helpText = Ui.Rich();
        helpText.CustomMinimumSize = new Vector2(600, 0);
        helpText.Text = HelpText;
        helpCol.AddChild(helpText);
        helpCol.AddChild(Ui.Button("Got it", () => _help.Visible = false, null, "PrimaryButton"));
    }

    private const string HelpText =
        "[b][color=#ffb845]Building[/color][/b]\n" +
        "[table=2]" +
        "[cell]Left click  [/cell][cell]place a block (hold and drag to paint a row)[/cell]" +
        "[cell]Right click[/cell][cell]remove a block[/cell]" +
        "[cell]Middle click[/cell][cell]copy the block under the cursor[/cell]" +
        "[cell]R / T / G[/cell][cell]rotate around the up / side / front axis (Shift turns the other way)[/cell]" +
        "[cell]M[/cell][cell]mirror mode: build both sides at once[/cell]" +
        "[cell]B[/cell][cell]box tool: drag out a box to fill it, build a hollow shell (a hull in one go), or clear it. Mouse wheel while dragging makes it taller[/cell]" +
        "[cell]1-9,  [ ][/cell][cell]pick a block / switch category[/cell]" +
        "[cell]Ctrl+Z / Ctrl+Y[/cell][cell]undo / redo[/cell]" +
        "[/table]\n" +
        "[b][color=#ffb845]Camera[/color][/b]\n" +
        "[table=2]" +
        "[cell]Right drag[/cell][cell]orbit[/cell]" +
        "[cell]Middle drag[/cell][cell]pan[/cell]" +
        "[cell]W A S D, Q / E[/cell][cell]move around, down / up[/cell]" +
        "[cell]Mouse wheel[/cell][cell]zoom[/cell]" +
        "[cell]F[/cell][cell]focus on the design[/cell]" +
        "[/table]\n" +
        "[b][color=#ffb845]How designs work[/color][/b]\n" +
        "The front of the vehicle faces the FRONT arrow. Every vehicle needs a driver seat, an engine, a fuel tank, " +
        "and wheels or tracks on the bottom row.\n" +
        "A gun is a breech with barrel segments in a line in front of it (the yellow arrow). Loaders and propellant must touch " +
        "the breech. Ammo racks must touch it too, or connect through ammo feeds, other racks or a turret ring.\n" +
        "Everything you build on top of a turret ring, connected to the block on top of it, turns as a turret.\n" +
        "The pink dot is the center of mass. Red blocks aren't attached and will fall off.\n" +
        "Big blocks (sizes in brackets, like Belt Armor 4×4×2 or the 406 mm Breech 3×3×3) fill several cells and share one health pool. " +
        "They're what make machines of scale buildable: the build area is 320 m across.";

    private void SetMirror(bool on)
    {
        _mirror = on;
        if (_mirrorButton == null) return;
        _mirrorButton.SetPressedNoSignal(on);
        _mirrorButton.Text = on ? "MIRROR ON" : "MIRROR OFF";
    }

    private void SetCategory(int i)
    {
        _category = i;
        Sfx.Play2D(this, SoundId.Tick, -9f, 1.1f);
        RefreshPalette();
    }

    private void Select(BlockDef def)
    {
        if (def != _selected) Sfx.Play2D(this, SoundId.Tick, -8f);
        _selected = def;
        _category = Array.IndexOf(BlockCatalog.Categories, def.Category);
        RefreshPalette();
    }

    private void RefreshPalette()
    {
        foreach (Node n in _categoryFlow.GetChildren()) n.QueueFree();
        foreach (Node n in _tileGrid.GetChildren()) n.QueueFree();

        var catGroup = new ButtonGroup();
        for (int i = 0; i < BlockCatalog.Categories.Length; i++)
        {
            int idx = i;
            Button b = Ui.Button(BlockCatalog.Categories[i].ToUpperInvariant(), () => SetCategory(idx), "[ and ] switch category", "Pill");
            b.ToggleMode = true;
            b.ButtonGroup = catGroup;
            b.SetPressedNoSignal(i == _category);
            _categoryFlow.AddChild(b);
        }

        var group = new ButtonGroup();
        List<BlockDef> defs = BlockCatalog.InCategory(BlockCatalog.Categories[_category]);
        for (int i = 0; i < defs.Count; i++)
        {
            BlockDef d = defs[i];
            var tile = new Button
            {
                ToggleMode = true, ButtonGroup = group, ThemeTypeVariation = "Tile", FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(86, 106), TooltipText = d.Description,
            };
            tile.SetPressedNoSignal(d == _selected);
            tile.Pressed += () => Select(d);

            var icon = new TextureRect
            {
                Texture = PartIcons.Get(d), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            tile.AddChild(icon);
            icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            icon.OffsetLeft = 6;
            icon.OffsetRight = -6;
            icon.OffsetTop = 4;
            icon.OffsetBottom = -32;

            Label name = Ui.Label(d.Name, 12, d == _selected ? Ui.AccentBright : Ui.Text);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            tile.AddChild(name);
            name.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
            name.OffsetTop = -34;
            name.OffsetBottom = -3;
            name.OffsetLeft = 3;
            name.OffsetRight = -3;

            if (i < 9)
            {
                Label num = Ui.Caption((i + 1).ToString());
                tile.AddChild(num);
                num.Position = new Vector2(7, 4);
            }

            _tileGrid.AddChild(tile);
        }

        BuildPartCard(_selected);
    }

    private void BuildPartCard(BlockDef d)
    {
        foreach (Node n in _partCard.GetChildren()) n.QueueFree();
        HBoxContainer head = Ui.Row(8);
        head.AddChild(Ui.Heading(d.Name, Ui.AccentBright));
        head.AddChild(Ui.Spacer());
        head.AddChild(Ui.Caption(d.Category));
        _partCard.AddChild(head);
        _partCard.AddChild(Ui.Wrapped(d.Description, 14, Ui.Dim));

        var chips = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        chips.AddThemeConstantOverride("h_separation", 5);
        chips.AddThemeConstantOverride("v_separation", 5);
        chips.AddChild(Ui.Chip("Mass", $"{d.MassKg:0} kg"));
        chips.AddChild(Ui.Chip("HP", $"{d.Hp:0}"));
        chips.AddChild(Ui.Chip("Armor", $"{d.ArmorMm:0} mm"));
        if (d.PowerKw > 0) chips.AddChild(Ui.Chip("Power", $"{d.PowerKw:0} kW", Ui.Accent));
        if (d.FuelL > 0) chips.AddChild(Ui.Chip("Fuel", $"{d.FuelL:0} L"));
        if (d.Breech != null)
        {
            chips.AddChild(Ui.Chip("Caliber", $"{d.Breech.CaliberMm:0} mm", Ui.Accent));
            chips.AddChild(Ui.Chip("Reload", $"{d.Breech.ReloadS:0.0} s"));
            chips.AddChild(Ui.Chip("Base pen", $"{d.Breech.BasePenMm:0} mm"));
        }

        if (d.AmmoUnits > 0) chips.AddChild(Ui.Chip("Holds", $"{d.AmmoUnits} units"));
        if (d.HeVulnerability > 1) chips.AddChild(Ui.Chip("Weak to", "HE", Ui.Warn));
        if (d.DeathExplosionDamage > 0 || d.Kind == BlockKind.AmmoRack) chips.AddChild(Ui.Chip(null, "Explodes", Ui.Bad));
        _partCard.AddChild(chips);
        if (d.AmmoUnits > 0) _partCard.AddChild(Ui.Small("One 37 mm shell takes 1 unit, 76 mm takes 4, 120 mm takes 8."));
    }

    // ---------- UI: dashboard ----------

    private static Control StatTile(string caption, string value, string unit, Color color)
    {
        PanelContainer card = Ui.Card();
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        VBoxContainer col = Ui.Column(0);
        card.AddChild(col);
        col.AddChild(Ui.Caption(caption));
        HBoxContainer row = Ui.Row(4);
        row.AddChild(Ui.Value(value, color));
        Label u = Ui.Small(unit);
        u.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        row.AddChild(u);
        col.AddChild(row);
        return card;
    }

    private static Control ArmorRow(string side, ArmorProfile p)
    {
        HBoxContainer row = Ui.Row(8);
        Label name = Ui.Small(side);
        name.CustomMinimumSize = new Vector2(48, 0);
        row.AddChild(name);
        var bar = new StatBar();
        if (p.Samples > 0) bar.Set(p.AvgMm, 200f, Ui.Grade(p.AvgMm, 40, 90), p.MinMm);
        row.AddChild(bar);
        Label v = Ui.Label(p.Samples > 0 ? $"{p.AvgMm:0} mm" : "-", 14);
        v.AddThemeFontOverride("font", Ui.BodyBoldFont);
        v.CustomMinimumSize = new Vector2(62, 0);
        v.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(v);
        return row;
    }

    private static HFlowContainer ChipFlow()
    {
        var f = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        f.AddThemeConstantOverride("h_separation", 5);
        f.AddThemeConstantOverride("v_separation", 5);
        return f;
    }

    /// <summary>What kind of aircraft it is, how it lifts, and which way each control surface steers it.</summary>
    private void AirSection(VehicleAnalysis a)
    {
        AirStats s = a.Air;
        _dash.AddChild(Ui.Section(VehicleAnalysis.ModeName(a.Mode)));
        HFlowContainer chips = ChipFlow();
        float weightT = a.MassKg / 1000f;
        switch (a.Mode)
        {
            case MobilityMode.Plane:
                _dash.AddChild(Ui.Small("Wing panels lift with speed. Control surfaces steer it: at the wingtips they roll, on the tailplane they pitch, upright on the fin they turn."));
                chips.AddChild(Ui.Chip("Wing", $"{s.WingAreaM2:0} m²"));
                chips.AddChild(Ui.Chip("Push", $"{s.StaticThrustN / 1000f:0.0} kN"));
                chips.AddChild(Ui.Chip("Roll", s.HasRoll ? "yes" : "no", s.HasRoll ? Ui.Good : Ui.Bad));
                chips.AddChild(Ui.Chip("Pitch", s.HasPitch ? "yes" : "no", s.HasPitch ? Ui.Good : Ui.Bad));
                chips.AddChild(Ui.Chip("Turn", s.HasYaw ? "yes" : "no", s.HasYaw ? Ui.Good : Ui.Warn));
                chips.AddChild(Ui.Chip("Balance", s.StaticMarginM >= 0 ? "stable" : "nose-up", s.StaticMarginM >= 0 ? Ui.Good : Ui.Warn));
                break;
            case MobilityMode.Airship:
                _dash.AddChild(Ui.Small("Gas cells hold it up; it vents or tops up gas to hold the height you order."));
                chips.AddChild(Ui.Chip("Gas lift", $"{s.GasLiftKg / 1000f:0.0} t", s.CanFly ? Ui.Good : Ui.Bad));
                chips.AddChild(Ui.Chip("Weight", $"{weightT:0.0} t"));
                chips.AddChild(Ui.Chip("Push", $"{s.StaticThrustN / 1000f:0.0} kN"));
                break;
            default:
                _dash.AddChild(Ui.Small("Rotors and lift fans hold it up; it tilts to move and stops when you let go."));
                chips.AddChild(Ui.Chip("Can lift", $"{s.HoverThrustN / GunMath.Gravity / 1000f:0.0} t", s.CanFly ? Ui.Good : Ui.Bad));
                chips.AddChild(Ui.Chip("Weight", $"{weightT:0.0} t"));
                chips.AddChild(Ui.Chip("Spare lift", $"{(s.HoverThrustN / GunMath.Gravity / Mathf.Max(a.MassKg, 1f) - 1f) * 100f:0}%",
                    s.HoverThrustN > a.MassKg * GunMath.Gravity * 1.2f ? Ui.Good : Ui.Warn));
                break;
        }

        if (s.Bombs > 0) chips.AddChild(Ui.Chip("Bombs", $"{s.Bombs}", Ui.Accent));
        if (a.Seats.Any(x => x.Remote)) chips.AddChild(Ui.Chip("Flown", "by remote", Ui.Info));
        _dash.AddChild(chips);
    }

    private void MissileSection(VehicleAnalysis a)
    {
        _dash.AddChild(Ui.Section("Missiles"));
        // Missiles built the same share a card.
        foreach (var same in a.Missiles.GroupBy(x => (x.Valid, Parts: x.Parts.Count, x.Stats.Seeker, x.Stats.BurnoutSpeed, x.Stats.Damage, x.Problem)))
        {
            MissileInfo m = same.First();
            int n = same.Count();
            PanelContainer card = Ui.Card();
            VBoxContainer col = Ui.Column(6);
            card.AddChild(col);
            HBoxContainer top = Ui.Row(8);
            string kind = !m.Valid ? "Not ready" : m.Stats.Seeker switch
            {
                SeekerKind.Optical => "Optical: flies to your crosshair",
                SeekerKind.Radar => "Radar: chases aircraft",
                _ => "Unguided rocket",
            };
            top.AddChild(Ui.Heading(n > 1 ? $"{m.Parts.Count}-part missile × {n}" : $"{m.Parts.Count}-part missile"));
            top.AddChild(Ui.Spacer());
            top.AddChild(Ui.Caption(kind, m.Valid ? Ui.Info : Ui.Bad));
            col.AddChild(top);
            if (m.Valid)
            {
                HFlowContainer chips = ChipFlow();
                chips.AddChild(Ui.Chip("Speed", $"{m.Stats.BurnoutSpeed * 3.6f:0} km/h", Ui.Accent));
                chips.AddChild(Ui.Chip("Reach", $"{m.Stats.RangeM:0} m"));
                chips.AddChild(Ui.Chip("Blast", $"{m.Stats.Damage:0} · {m.Stats.RadiusM:0.0} m"));
                chips.AddChild(Ui.Chip("Turns", m.Stats.MaxTurnAccel > 0 ? $"{m.Stats.MaxTurnAccel / GunMath.Gravity:0} g" : "no", m.Stats.MaxTurnAccel > 0 ? Ui.Text : Ui.Dim));
                col.AddChild(chips);
            }
            else if (m.Problem != null)
            {
                col.AddChild(Ui.Wrapped(m.Problem, 14, Ui.Bad));
            }

            _dash.AddChild(card);
        }
    }

    /// <summary>How the hull floats, how much flooding it can take, and (submarines) whether it can dive and come back up.</summary>
    private void HullSection(VehicleAnalysis a)
    {
        HydroStats w = a.Water;
        _dash.AddChild(Ui.Section(a.Mode == MobilityMode.Submarine ? "Hull and ballast" : "Hull"));
        _dash.AddChild(Ui.Small("Ships float on the sealed air inside their hulls. The blue plane is the waterline."));
        HFlowContainer chips = ChipFlow();
        chips.AddChild(Ui.Chip("Keeps afloat", $"{w.DisplacementT:0} t", w.Floats ? Ui.Text : Ui.Bad));
        chips.AddChild(Ui.Chip("Draft", w.Floats ? $"{w.DraftM:0.0} m" : "-"));
        var real = a.Compartments.Where(c => !c.IsBallast && c.Cells.Count > 8).ToList();
        chips.AddChild(Ui.Chip("Compartments", $"{real.Count}", real.Count > 1 ? Ui.Text : Ui.Warn));
        chips.AddChild(Ui.Chip("Pumps", $"{a.Pumps}", a.Pumps > 0 ? Ui.Text : Ui.Dim));
        if (a.Mode == MobilityMode.Submarine)
        {
            chips.AddChild(Ui.Chip("Ballast", $"{w.BallastT:0.0} t", Ui.Info));
            chips.AddChild(Ui.Chip("Dives", w.CanDive ? "yes" : "no", w.CanDive ? Ui.Good : Ui.Bad));
            chips.AddChild(Ui.Chip("Surfaces", w.CanSurface ? "yes" : "no", w.CanSurface ? Ui.Good : Ui.Bad));
        }

        if (a.TorpedoTubes.Count > 0)
            chips.AddChild(Ui.Chip("Torpedoes", $"{a.TorpedoTubes.Sum(t => t.Rounds)} in {a.TorpedoTubes.Count} tube{(a.TorpedoTubes.Count == 1 ? "" : "s")}", Ui.Accent));
        _dash.AddChild(chips);

        // Damage control: does she stay up with any one compartment flooded?
        if (!w.Floats || real.Count == 0) return;
        var cells = a.Defs.Where(kv => !a.Disconnected.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        int survives = 0;
        foreach (Compartment c in real)
        {
            c.Water = c.CapacityM3;
            if (Hydro.Analyze(cells, a.Compartments, a.MassKg).Floats) survives++;
            c.Water = 0;
        }

        string text = survives == real.Count
            ? "Stays afloat with any one compartment flooded."
            : $"Sinks if {real.Count - survives} of its {real.Count} compartments floods. Add bulkheads (inner walls) or more sealed space.";
        _dash.AddChild(Ui.Wrapped(text, 14, survives == real.Count ? Ui.Good : Ui.Warn));
    }

    private void RebuildDashboard(VehicleAnalysis a)
    {
        foreach (Node n in _dash.GetChildren()) n.QueueFree();

        HBoxContainer head = Ui.Row(8);
        Label title = Ui.Title(_name);
        title.ClipText = true;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        int errors = a.ErrorCount, warnings = a.Warnings.Count(w => w.Severity == Severity.Warning);
        if (a.Cells.Count == 0) head.AddChild(Ui.Chip(null, "EMPTY", Ui.Dim));
        else if (errors > 0) head.AddChild(Ui.Chip(null, $"{errors} TO FIX", Ui.Bad));
        else if (warnings > 0) head.AddChild(Ui.Chip(null, "DRIVABLE", Ui.Warn));
        else head.AddChild(Ui.Chip(null, "READY", Ui.Good));
        _dash.AddChild(head);

        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        grid.AddChild(StatTile("Mass", $"{a.MassKg / 1000f:0.0}", "t", Ui.Text));
        grid.AddChild(StatTile("Top speed", $"{a.TopSpeedKmh:0}", "km/h", Ui.Grade(a.TopSpeedKmh, 20, 35)));
        grid.AddChild(StatTile("Power", $"{a.PowerToWeight:0.0}", "kW/t", Ui.Grade(a.PowerToWeight, 8, 12)));
        if (a.IsAir)
            grid.AddChild(a.Mode switch
            {
                MobilityMode.Plane => StatTile("Stalls below", $"{a.Air.StallSpeedMs * 3.6f:0}", "km/h", Ui.Grade(200f - a.Air.StallSpeedMs * 3.6f, 20f, 50f)),
                MobilityMode.Airship => StatTile("Gas lift", $"{a.Air.GasLiftKg / 1000f:0.0}", "t", a.Air.CanFly ? Ui.Good : Ui.Bad),
                _ => StatTile("Can lift", $"{a.Air.HoverThrustN / GunMath.Gravity / 1000f:0.0}", "t", a.Air.CanFly ? Ui.Good : Ui.Bad),
            });
        else if (a.IsNaval)
            grid.AddChild(a.Water.Floats
                ? StatTile("Freeboard", $"{a.Water.FreeboardM:0.0}", "m", Ui.Grade(a.Water.FreeboardM, 0.5f, 1.2f))
                : StatTile("Freeboard", "sinks", "", Ui.Bad));
        else
            grid.AddChild(a.Wheels.Count > 0
                ? StatTile("Tips over at", $"{a.TipAngleDeg:0}", "°", Ui.Grade(a.TipAngleDeg, 30, 40))
                : StatTile("Tips over at", "-", "", Ui.Dim));
        grid.AddChild(StatTile("Fuel", $"{a.FuelL:0}", "L", a.FuelL > 0 ? Ui.Text : Ui.Bad));
        grid.AddChild(StatTile("Blocks", $"{a.Anchors.Count}", "", Ui.Text));
        _dash.AddChild(grid);

        if (a.IsNaval) HullSection(a);
        if (a.IsAir) AirSection(a);
        if (a.Missiles.Count > 0) MissileSection(a);

        _dash.AddChild(Ui.Section("Armor"));
        _dash.AddChild(Ui.Small("How much armor a shell must get through to reach the insides. The tick is the weakest spot."));
        VBoxContainer armor = Ui.Column(5);
        armor.AddChild(ArmorRow("Front", a.Front));
        armor.AddChild(ArmorRow("Sides", a.LeftSide.AvgMm <= a.RightSide.AvgMm ? a.LeftSide : a.RightSide));
        armor.AddChild(ArmorRow("Rear", a.Rear));
        armor.AddChild(ArmorRow("Top", a.Top));
        _dash.AddChild(armor);

        if (a.Guns.Count > 0)
        {
            _dash.AddChild(Ui.Section("Weapons"));
            // Guns that are built the same share a card ("406 mm × 12"), so a battleship's battery stays readable.
            var kinds = a.Guns.GroupBy(g => (g.Label, g.Valid, Barrels: g.Barrels.Count, Brake: g.Brake.HasValue, g.Loaders, g.Propellants,
                    Fixed: g.Section == 0, g.ApRounds, g.HeRounds, g.FlakRounds))
                .OrderByDescending(k => k.First().BreechDef.Breech.CaliberMm);
            foreach (var kind in kinds)
            {
                GunInfo g = kind.First();
                int n = kind.Count();
                PanelContainer card = Ui.Card();
                VBoxContainer col = Ui.Column(6);
                card.AddChild(col);
                HBoxContainer top = Ui.Row(8);
                top.AddChild(Ui.Heading(n > 1 ? $"{g.Label} × {n}" : g.Label));
                top.AddChild(Ui.Spacer());
                top.AddChild(Ui.Caption(g.Section == 0 ? "Fixed in hull" : n > 1 ? "Turrets" : "Turret"));
                col.AddChild(top);
                if (!g.Valid)
                {
                    col.AddChild(Ui.Wrapped("Not a working gun yet: it needs barrel segments in front of the breech.", 14, Ui.Bad));
                    _dash.AddChild(card);
                    continue;
                }

                GunStats s = g.Stats;
                float pen500 = GunMath.PenAtSpeed(g.BreechDef.Breech, GunMath.SpeedAtRange(s.MuzzleVelocity, s.DragK, 500));
                col.AddChild(Ui.Small($"{g.Barrels.Count} barrel{(g.Barrels.Count == 1 ? "" : "s")}{(g.Brake.HasValue ? " + brake" : "")} · " +
                                      $"{g.Loaders} loader{(g.Loaders == 1 ? "" : "s")} · {g.Propellants} propellant{(n > 1 ? " (each)" : "")}"));
                HFlowContainer chips = ChipFlow();
                chips.AddChild(Ui.Chip("Pen", $"{s.ApPenMm:0} mm", Ui.Accent));
                chips.AddChild(Ui.Chip("At 500 m", $"{pen500:0} mm"));
                chips.AddChild(Ui.Chip("Speed", $"{s.MuzzleVelocity:0} m/s"));
                chips.AddChild(Ui.Chip("Reload", $"{s.ReloadS:0.0} s"));
                bool flakOnly = g.FlakRounds > 0 && g.ApRounds == 0;
                if (!flakOnly) chips.AddChild(Ui.Chip("AP", $"{g.ApRounds}", g.ApRounds > 0 ? Ui.Text : Ui.Bad));
                chips.AddChild(Ui.Chip("HE", $"{g.HeRounds}", g.HeRounds > 0 ? Ui.Text : Ui.Dim));
                if (g.FlakRounds > 0) chips.AddChild(Ui.Chip("Flak", $"{g.FlakRounds}"));
                col.AddChild(chips);
                _dash.AddChild(card);
            }
        }

        if (a.Turrets.Count > 0)
        {
            _dash.AddChild(Ui.Section("Turrets"));
            if (a.Turrets.Count <= 4)
            {
                for (int i = 0; i < a.Turrets.Count; i++)
                {
                    TurretInfo t = a.Turrets[i];
                    _dash.AddChild(Ui.Small($"Turret {i + 1}: {t.Blocks} blocks · {t.MassKg / 1000f:0.0} t · turns {t.TraverseDegPerSec:0}°/s"));
                }
            }
            else
            {
                foreach (var same in a.Turrets.GroupBy(t => (t.Blocks, Tonnes: MathF.Round(t.MassKg / 100f) / 10f, Turn: MathF.Round(t.TraverseDegPerSec)))
                             .OrderByDescending(k => k.Key.Tonnes))
                    _dash.AddChild(Ui.Small($"{same.Count()} × {same.Key.Blocks} blocks · {same.Key.Tonnes:0.0} t · turn{(same.Count() == 1 ? "s" : "")} {same.Key.Turn:0}°/s"));
            }
        }

        _dash.AddChild(Ui.Section("Problems"));
        if (a.Warnings.Count == 0)
        {
            _dash.AddChild(Ui.Wrapped("None. Ready to drive: press Tab.", 14, Ui.Good));
            return;
        }

        foreach (DesignWarning w in a.Warnings.OrderBy(w => w.Severity))
        {
            Color c = w.Severity switch { Severity.Error => Ui.Bad, Severity.Warning => Ui.Warn, _ => Ui.Info };
            HBoxContainer row = Ui.Row(8);
            row.AddChild(new ColorRect { Color = c, CustomMinimumSize = new Vector2(3, 0), SizeFlagsVertical = Control.SizeFlags.Fill });
            Label text = Ui.Wrapped(w.Text, 14, w.Severity == Severity.Info ? Ui.Dim : Ui.Text);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(text);
            _dash.AddChild(row);
        }
    }

    // ---------- Actions ----------

    private void Toast(string text)
    {
        if (_toast == null) return;
        _toastLabel.Text = text;
        _toastTime = 4f;
        _toast.Visible = true;
        _toast.Modulate = Colors.White;
    }

    private void NewDesign()
    {
        LoadBlueprint(new Blueprint { Name = "New design" });
        Toast("Empty design. Undo (Ctrl+Z) brings the old one back.");
    }

    private static string FileNameFor(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.Trim())
            sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
        string s = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(s) ? "design" : s;
    }

    private void Save()
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        string path = $"{SaveDir}/{FileNameFor(_name)}.json";
        using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null)
        {
            Toast($"Couldn't save: {FileAccess.GetOpenError()}");
            Sfx.Play2D(this, SoundId.Error, -8f);
            return;
        }

        f.StoreString(CurrentBlueprint().ToJson());
        Sfx.Play2D(this, SoundId.Reload, -8f);
        Toast($"Saved \"{_name}\". The Folder button opens your blueprints.");
    }

    private void OpenFolder()
    {
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        OS.ShellOpen(ProjectSettings.GlobalizePath(SaveDir));
    }

    private void FillLoadMenu()
    {
        PopupMenu menu = _loadButton.GetPopup();
        menu.Clear();
        _loadActions.Clear();
        menu.AddSeparator("Examples");
        foreach (Blueprint ex in ExampleDesigns.All)
        {
            Blueprint copy = ex;
            menu.AddItem(ex.Name, _loadActions.Count);
            _loadActions.Add(() =>
            {
                LoadBlueprint(copy, animate: true);
                Toast($"Loaded example \"{copy.Name}\". Undo (Ctrl+Z) to go back.");
            });
        }

        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        string[] files = DirAccess.GetFilesAt(SaveDir).Where(f => f.EndsWith(".json")).OrderBy(f => f).ToArray();
        if (files.Length > 0) menu.AddSeparator("Your designs");
        foreach (string file in files)
        {
            string path = $"{SaveDir}/{file}";
            menu.AddItem(file[..^5], _loadActions.Count);
            _loadActions.Add(() => LoadFile(path));
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            var problems = new List<string>();
            Blueprint bp = Blueprint.FromJson(FileAccess.GetFileAsString(path), problems);
            LoadBlueprint(bp, animate: true);
            Toast(problems.Count > 0 ? string.Join(" ", problems) : $"Loaded \"{bp.Name}\".");
        }
        catch (FormatException e)
        {
            Toast(e.Message);
            Sfx.Play2D(this, SoundId.Error, -8f);
        }
    }

    private void RequestTestDrive()
    {
        Blueprint bp = CurrentBlueprint();
        VehicleAnalysis a = VehicleAnalysis.Analyze(bp);
        DesignWarning firstError = a.Warnings.FirstOrDefault(w => w.Severity == Severity.Error);
        if (!a.CanDrive || a.ErrorCount > 0)
        {
            Toast("Can't drive yet: " + (firstError.Text ?? "it needs a driver seat, engine, fuel and wheels."));
            Sfx.Play2D(this, SoundId.Error, -6f);
            return;
        }

        TestDriveRequested?.Invoke(bp);
    }
}
