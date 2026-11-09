using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DreamGame.Core;
using Godot;
using NVec = System.Numerics.Vector3;

namespace DreamGame.Game;

/// <summary>
/// The vehicle builder: place blocks on a grid with an orbit camera, see live stats and plain-English
/// warnings, save and load blueprints, and jump into a test drive.
/// </summary>
public partial class Builder : Node3D
{
    public const int Reach = 40;
    public const int MaxHeight = 30;
    private const string SaveDir = "user://blueprints";

    public event Action<Blueprint> TestDriveRequested;

    private readonly Dictionary<Int3, BlueprintBlock> _cells = new();
    private readonly Dictionary<Int3, Node3D> _visuals = new();
    private readonly Dictionary<Int3, float> _visualCaliber = new();
    private readonly HashSet<Int3> _tinted = new();
    private string _name = "Untitled";
    private VehicleAnalysis _analysis;
    private bool _dirty = true;

    // Camera
    private Camera3D _cam;
    private Vector3 _target = new(0, 0.8f, 0);
    private float _yaw = Mathf.DegToRad(140f);
    private float _pitch = Mathf.DegToRad(28f);
    private float _dist = 14f;

    // Tools
    private BlockDef _selected;
    private int _orientation;
    private bool _mirror = true;
    private int _category;
    private Node3D _ghost, _ghostMirror, _comMarker;
    private string _ghostKey = "";

    // Mouse state
    private bool _rmbDown, _mmbDown, _lmbDown, _dragged;
    private Vector2 _pressPos;
    private Int3 _paintStart, _paintNormal;
    private List<Change> _stroke;

    // Undo
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
    private RichTextLabel _stats;
    private HBoxContainer _blockBar;
    private HBoxContainer _categoryBar;
    private RichTextLabel _info;
    private Label _toast;
    private float _toastTime;
    private PanelContainer _help;
    private Button _mirrorButton;
    private MenuButton _loadButton;
    private readonly List<Action> _loadActions = new();

    public override void _Ready()
    {
        Scenery.AddSkyAndSun(this, false);
        BuildFloor();
        _cam = new Camera3D { Fov = 55f, Far = 800f };
        AddChild(_cam);
        _cam.MakeCurrent();
        _comMarker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.2f, 0.6f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true, RenderPriority = 10,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_comMarker);
        BuildUi();
        _selected = BlockCatalog.All[0];
        RefreshPalette();
        LoadBlueprint(ExampleDesigns.Warden(), recordUndo: false);
        Toast("Loaded the example Warden tank. Press Tab to drive it, or start changing it.");
        UpdateCamera();
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
        var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        img.Fill(new Color(0.2f, 0.21f, 0.22f));
        for (int i = 0; i < 64; i++)
        {
            img.SetPixel(i, 0, new Color(0.32f, 0.34f, 0.36f));
            img.SetPixel(0, i, new Color(0.32f, 0.34f, 0.36f));
        }

        float size = (Reach * 2 + 1) * Conv.CS;
        var floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            Position = new Vector3(0, -Conv.CS * 0.5f - 0.002f, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(img),
                Uv1Scale = new Vector3(Reach * 2 + 1, Reach * 2 + 1, 1),
                Roughness = 0.9f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            },
        };
        AddChild(floor);

        // Line along X = 0 so the mirror plane is visible.
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.005f, size) },
            Position = new Vector3(0, -Conv.CS * 0.5f, 0),
            MaterialOverride = BlockVisuals.Emissive(new Color(0.3f, 0.6f, 1f), 1f),
        });
        // Arrow at the front of the build area: vehicles face -Z.
        AddChild(new Label3D
        {
            Text = "FRONT", FontSize = 96, PixelSize = 0.01f, Position = new Vector3(0, -0.2f, -9f),
            RotationDegrees = new Vector3(-90, 0, 0), Modulate = new Color(0.4f, 0.65f, 1f, 0.6f),
        });
    }

    // ---------- Editing ----------

    private void SetCell(Int3 c, BlueprintBlock b)
    {
        if (_visuals.TryGetValue(c, out Node3D old))
        {
            old.QueueFree();
            _visuals.Remove(c);
            _visualCaliber.Remove(c);
            _tinted.Remove(c);
        }

        if (b == null)
        {
            _cells.Remove(c);
        }
        else
        {
            _cells[c] = b;
            MakeVisual(c, b, 76f);
        }

        _dirty = true;
    }

    private void MakeVisual(Int3 c, BlueprintBlock b, float caliber)
    {
        Node3D v = BlockVisuals.Create(BlockCatalog.Get(b.Id), caliber, hints: true);
        v.Transform = Conv.BlockTransform(c, b.O);
        AddChild(v);
        _visuals[c] = v;
        _visualCaliber[c] = caliber;
    }

    private bool InBounds(Int3 c) => Math.Abs(c.X) <= Reach && Math.Abs(c.Z) <= Reach && c.Y >= 0 && c.Y <= MaxHeight;

    private void Place(Int3 c, List<Change> into)
    {
        PlaceOne(c, _orientation, into);
        if (_mirror && c.X != 0) PlaceOne(new Int3(-c.X, c.Y, c.Z), Orientations.MirrorX(_orientation), into);
    }

    private void PlaceOne(Int3 c, int o, List<Change> into)
    {
        if (!InBounds(c) || _cells.ContainsKey(c)) return;
        var b = new BlueprintBlock(_selected.Id, c, o);
        into.Add(new Change { Cell = c, Before = null, After = b });
        SetCell(c, b);
    }

    private void Remove(Int3 c)
    {
        var changes = new List<Change>();
        RemoveOne(c, changes);
        if (_mirror && c.X != 0) RemoveOne(new Int3(-c.X, c.Y, c.Z), changes);
        Commit(changes);
    }

    private void RemoveOne(Int3 c, List<Change> into)
    {
        if (!_cells.TryGetValue(c, out BlueprintBlock old)) return;
        into.Add(new Change { Cell = c, Before = old, After = null });
        SetCell(c, null);
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
            return;
        }

        List<Change> changes = from.Pop();
        IEnumerable<Change> order = redo ? changes : Enumerable.Reverse(changes);
        foreach (Change ch in order) SetCell(ch.Cell, redo ? ch.After : ch.Before);
        to.Push(changes);
    }

    private void LoadBlueprint(Blueprint bp, bool recordUndo = true)
    {
        var changes = new List<Change>();
        foreach (Int3 c in _cells.Keys.ToList()) RemoveOne(c, changes);
        foreach (BlueprintBlock b in bp.Blocks)
        {
            var copy = new BlueprintBlock(b.Id, b.Cell, b.O);
            changes.Add(new Change { Cell = b.Cell, Before = null, After = copy });
            SetCell(b.Cell, copy);
        }

        if (recordUndo) Commit(changes);
        _name = bp.Name;
        if (_nameEdit != null) _nameEdit.Text = _name;
        FocusCamera();
    }

    public Blueprint CurrentBlueprint() => Blueprint.FromCellMap(_name, _cells.Values);

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
            Int3 min = _cells.Keys.Aggregate(Int3.Min), max = _cells.Keys.Aggregate(Int3.Max);
            var hits = new List<RayCellHit>(1);
            GridRaycast.Cast(new BlueprintShapeLookup(_cells), min, max, (o / Conv.CS).N(), d.N(), 2000f, hits, firstOnly: true);
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

    private bool PlacementTarget(out Int3 cell)
    {
        cell = default;
        if (!PickCell(out Int3 hit, out Int3 n, out _)) return false;
        cell = hit + n;
        return InBounds(cell) && !_cells.ContainsKey(cell);
    }

    private bool OverUi() => GetViewport().GuiGetHoveredControl() != null;

    // ---------- Input ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            HandleMouseButton(mb);
            return;
        }

        if (e is InputEventMouseMotion mm)
        {
            HandleMouseMotion(mm);
            return;
        }

        if (e is InputEventKey { Pressed: true } key) HandleKey(key);
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        // Clicking the 3D view takes keyboard focus away from the name box, so shortcut keys work again.
        if (mb.Pressed) GetViewport().GuiReleaseFocus();
        switch (mb.ButtonIndex)
        {
            case MouseButton.WheelUp when mb.Pressed:
                _dist = Mathf.Clamp(_dist * 0.9f, 3f, 90f);
                UpdateCamera();
                break;
            case MouseButton.WheelDown when mb.Pressed:
                _dist = Mathf.Clamp(_dist * 1.1f, 3f, 90f);
                UpdateCamera();
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
                        Toast($"Picked {_selected.Name}.");
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
            _yaw -= mm.Relative.X * 0.006f;
            _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.006f, Mathf.DegToRad(-10f), Mathf.DegToRad(88f));
            UpdateCamera();
        }
        else if (_mmbDown && _dragged)
        {
            Basis b = _cam.GlobalTransform.Basis;
            _target += (-b.X * mm.Relative.X + b.Y * mm.Relative.Y) * _dist * 0.0016f;
            UpdateCamera();
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
                _orientation = Orientations.RotateAroundY(_orientation, dir);
                break;
            case Key.T:
                _orientation = Orientations.RotateAroundX(_orientation, dir);
                break;
            case Key.G:
                _orientation = Orientations.RotateAroundZ(_orientation, dir);
                break;
            case Key.M:
                SetMirror(!_mirror);
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

    // ---------- Frame ----------

    public override void _Process(double delta)
    {
        if (_dirty)
        {
            _dirty = false;
            Reanalyze();
        }

        UpdateGhost();
        if (_toastTime > 0)
        {
            _toastTime -= (float)delta;
            _toast.Modulate = new Color(1, 1, 1, Mathf.Clamp(_toastTime, 0, 1));
        }
    }

    private void UpdateCamera()
    {
        var offset = new Vector3(
            Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw));
        _cam.Position = _target + offset * _dist;
        _cam.LookAt(_target, Vector3.Up);
    }

    private void FocusCamera()
    {
        if (_cells.Count == 0)
        {
            _target = new Vector3(0, 0.8f, 0);
        }
        else
        {
            Int3 min = _cells.Keys.Aggregate(Int3.Min), max = _cells.Keys.Aggregate(Int3.Max);
            _target = (min.G() + max.G()) * 0.5f * Conv.CS;
            _dist = Mathf.Clamp((max - min).ToVector3().Length() * Conv.CS * 1.6f + 4f, 6f, 60f);
        }

        if (_cam != null) UpdateCamera();
    }

    private void UpdateGhost()
    {
        bool picked = PickCell(out Int3 hit, out Int3 n, out _);
        bool show = picked && !OverUi() && !_rmbDown && !_mmbDown;
        Int3 cell = show ? hit + n : default;
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

        bool ok = show && InBounds(cell) && !_cells.ContainsKey(cell);
        _ghost.Visible = show;
        if (show)
        {
            _ghost.Transform = Conv.BlockTransform(cell, _orientation);
            BlockVisuals.SetOverride(_ghost, ok ? BlockVisuals.GhostOk : BlockVisuals.GhostBad);
        }

        var mc = new Int3(-cell.X, cell.Y, cell.Z);
        bool mirrorShow = show && _mirror && cell.X != 0;
        _ghostMirror.Visible = mirrorShow;
        if (mirrorShow)
        {
            _ghostMirror.Transform = Conv.BlockTransform(mc, Orientations.MirrorX(_orientation));
            bool mok = InBounds(mc) && !_cells.ContainsKey(mc);
            BlockVisuals.SetOverride(_ghostMirror, mok ? BlockVisuals.GhostOk : BlockVisuals.GhostBad);
        }
    }

    private void Reanalyze()
    {
        _analysis = VehicleAnalysis.Analyze(_cells);

        // Barrels take the caliber of the gun they belong to.
        var calibers = new Dictionary<Int3, float>();
        foreach (GunInfo g in _analysis.Guns)
        {
            foreach (Int3 c in g.Barrels) calibers[c] = g.Stats.CaliberMm;
            if (g.Brake.HasValue) calibers[g.Brake.Value] = g.Stats.CaliberMm;
        }

        foreach ((Int3 c, BlueprintBlock b) in _cells)
        {
            BlockKind kind = BlockCatalog.Get(b.Id).Kind;
            if (kind != BlockKind.Barrel && kind != BlockKind.MuzzleBrake) continue;
            float want = calibers.TryGetValue(c, out float cal) ? cal : 76f;
            if (_visualCaliber.TryGetValue(c, out float have) && Math.Abs(have - want) < 0.1f) continue;
            _visuals[c].QueueFree();
            MakeVisual(c, b, want);
        }

        foreach (Int3 c in _tinted.ToList())
        {
            if (_analysis.Disconnected.Contains(c)) continue;
            if (_visuals.TryGetValue(c, out Node3D v)) BlockVisuals.SetOverlay(v, null);
            _tinted.Remove(c);
        }

        foreach (Int3 c in _analysis.Disconnected)
        {
            if (_tinted.Contains(c) || !_visuals.TryGetValue(c, out Node3D v)) continue;
            BlockVisuals.SetOverlay(v, BlockVisuals.LooseOverlay);
            _tinted.Add(c);
        }

        _comMarker.Visible = _cells.Count > 0;
        _comMarker.Position = _analysis.CenterOfMass.G();
        _stats.Text = StatsText(_analysis);
    }

    // ---------- UI ----------

    private void BuildUi()
    {
        Control root = Ui.Root(this);

        var top = new PanelContainer();
        root.AddChild(top);
        top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 8);
        top.AddChild(topRow);
        topRow.AddChild(Ui.Label("BUILDER", 17, Ui.Accent));
        _nameEdit = new LineEdit { Text = _name, CustomMinimumSize = new Vector2(260, 0), PlaceholderText = "Design name" };
        _nameEdit.TextChanged += t => _name = string.IsNullOrWhiteSpace(t) ? "Untitled" : t.Trim();
        _nameEdit.TextSubmitted += _ => _nameEdit.ReleaseFocus();
        topRow.AddChild(_nameEdit);
        topRow.AddChild(Ui.Button("New", NewDesign, "Start an empty design (undo brings it back)"));
        topRow.AddChild(Ui.Button("Save", Save, "Save to your blueprints folder (Ctrl+S)"));
        _loadButton = new MenuButton { Text = "Load", TooltipText = "Open an example or one of your saved designs" };
        _loadButton.AboutToPopup += FillLoadMenu;
        _loadButton.GetPopup().IdPressed += id =>
        {
            if (id >= 0 && id < _loadActions.Count) _loadActions[(int)id]();
        };
        topRow.AddChild(_loadButton);
        topRow.AddChild(Ui.Button("Folder", OpenFolder, "Open the folder with your saved blueprint files, to share them"));
        _mirrorButton = Ui.Button("", () => SetMirror(!_mirror), "Build both sides at once (M)");
        topRow.AddChild(_mirrorButton);
        SetMirror(_mirror);
        topRow.AddChild(Ui.Spacer());
        topRow.AddChild(Ui.Button("Help (F1)", () => _help.Visible = !_help.Visible));
        Button drive = Ui.Button("  Test drive  (Tab)  ", RequestTestDrive, "Drive this design on the test range");
        drive.AddThemeColorOverride("font_color", Ui.Accent);
        topRow.AddChild(drive);

        var side = new PanelContainer();
        root.AddChild(side);
        side.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        side.OffsetLeft = -360;
        side.OffsetRight = -8;
        side.OffsetTop = 60;
        side.OffsetBottom = -190;
        side.GrowHorizontal = Control.GrowDirection.Begin;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(scroll);
        _stats = Ui.Rich();
        _stats.CustomMinimumSize = new Vector2(330, 0);
        _stats.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_stats);

        var bottom = new PanelContainer();
        root.AddChild(bottom);
        bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        bottom.GrowVertical = Control.GrowDirection.Begin;
        var col = new VBoxContainer();
        bottom.AddChild(col);
        _categoryBar = new HBoxContainer();
        col.AddChild(_categoryBar);
        _blockBar = new HBoxContainer();
        col.AddChild(_blockBar);
        _info = Ui.Rich();
        _info.CustomMinimumSize = new Vector2(0, 44);
        col.AddChild(_info);

        _toast = Ui.Label("", 16, Ui.Accent);
        root.AddChild(_toast);
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _toast.OffsetTop = 64;
        _toast.GrowHorizontal = Control.GrowDirection.Both;
        _toast.HorizontalAlignment = HorizontalAlignment.Center;

        _help = new PanelContainer { Visible = false };
        root.AddChild(_help);
        _help.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        RichTextLabel helpText = Ui.Rich();
        helpText.CustomMinimumSize = new Vector2(560, 0);
        helpText.Text = HelpText;
        _help.AddChild(helpText);
    }

    private const string HelpText =
        "[b][color=#ffc74d]Building[/color][/b]\n" +
        "[table=2]" +
        "[cell]Left click[/cell][cell]place a block (hold and drag to paint a row)[/cell]" +
        "[cell]Right click[/cell][cell]remove a block[/cell]" +
        "[cell]Middle click[/cell][cell]copy the block under the cursor[/cell]" +
        "[cell]R / T / G   [/cell][cell]rotate around the up / side / front axis (Shift turns the other way)[/cell]" +
        "[cell]M[/cell][cell]mirror mode: build both sides at once[/cell]" +
        "[cell]1-9,  [ ][/cell][cell]pick a block / switch category[/cell]" +
        "[cell]Ctrl+Z / Ctrl+Y[/cell][cell]undo / redo[/cell]" +
        "[/table]\n" +
        "[b][color=#ffc74d]Camera[/color][/b]\n" +
        "[table=2]" +
        "[cell]Right drag[/cell][cell]orbit[/cell]" +
        "[cell]Middle drag[/cell][cell]pan[/cell]" +
        "[cell]Mouse wheel[/cell][cell]zoom[/cell]" +
        "[cell]F[/cell][cell]focus on the design[/cell]" +
        "[/table]\n" +
        "[b][color=#ffc74d]How designs work[/color][/b]\n" +
        "The front of the vehicle is toward the FRONT marker (-Z). Every vehicle needs a driver seat, an engine, a fuel tank, " +
        "and wheels or tracks on the bottom row.\n" +
        "A gun is a breech with barrel segments in a line in front of it (the yellow arrow). Loaders and propellant must touch " +
        "the breech. Ammo racks must touch it too, or connect through ammo feeds, other racks or a turret ring.\n" +
        "Everything you build on top of a turret ring, connected to the block on top of it, turns as a turret.\n" +
        "The pink dot is the center of mass. Red blocks aren't attached and will fall off.\n\n" +
        "Tab: test drive    Ctrl+S: save    F1: close this help";

    private void SetMirror(bool on)
    {
        _mirror = on;
        if (_mirrorButton != null) _mirrorButton.Text = on ? "Mirror: ON (M)" : "Mirror: off (M)";
    }

    private void SetCategory(int i)
    {
        _category = i;
        RefreshPalette();
    }

    private void Select(BlockDef def)
    {
        _selected = def;
        _category = Array.IndexOf(BlockCatalog.Categories, def.Category);
        RefreshPalette();
    }

    private void RefreshPalette()
    {
        foreach (Node n in _categoryBar.GetChildren()) n.QueueFree();
        foreach (Node n in _blockBar.GetChildren()) n.QueueFree();

        var catGroup = new ButtonGroup();
        for (int i = 0; i < BlockCatalog.Categories.Length; i++)
        {
            int idx = i;
            Button b = Ui.Button(BlockCatalog.Categories[i], () => SetCategory(idx));
            b.ToggleMode = true;
            b.ButtonGroup = catGroup;
            b.ButtonPressed = i == _category;
            _categoryBar.AddChild(b);
        }

        _categoryBar.AddChild(Ui.Label("    [ and ] switch category, 1-9 pick a block", 13, Ui.Dim));

        var group = new ButtonGroup();
        List<BlockDef> defs = BlockCatalog.InCategory(BlockCatalog.Categories[_category]);
        for (int i = 0; i < defs.Count; i++)
        {
            BlockDef d = defs[i];
            Button b = Ui.Button($"{i + 1}  {d.Name}", () => Select(d), d.Description);
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.ButtonPressed = d == _selected;
            _blockBar.AddChild(b);
        }

        _info.Text = BlockInfo(_selected);
    }

    private static string BlockInfo(BlockDef d)
    {
        var sb = new StringBuilder();
        sb.Append($"[b]{d.Name}[/b]   [color={Ui.Hex(Ui.Dim)}]{d.MassKg:0} kg · {d.Hp:0} HP · {d.ArmorMm:0} mm armor");
        if (d.PowerKw > 0) sb.Append($" · {d.PowerKw:0} kW");
        if (d.FuelL > 0) sb.Append($" · {d.FuelL:0} L");
        if (d.Breech != null)
            sb.Append($" · {d.Breech.CaliberMm:0} mm, {d.Breech.ReloadS:0.0} s base reload, {d.Breech.BasePenMm:0} mm base penetration");
        if (d.AmmoUnits > 0) sb.Append($" · holds {d.AmmoUnits} units (37 mm shell = 1, 76 mm = 4, 120 mm = 8)");
        if (d.HeVulnerability > 1) sb.Append(" · weak to HE");
        sb.Append("[/color]\n").Append(d.Description);
        return sb.ToString();
    }

    private static string Line(Color c, string text) => $"[color={Ui.Hex(c)}]{text}[/color]\n";

    private static string Armor(string label, ArmorProfile p) =>
        p.Samples == 0
            ? $"{label}  -\n"
            : $"{label}  [b]{p.AvgMm:0}[/b] mm   [color={Ui.Hex(Ui.Dim)}](weakest spot {p.MinMm:0})[/color]\n";

    private string StatsText(VehicleAnalysis a)
    {
        var sb = new StringBuilder();
        sb.Append($"[b][font_size=17]{_name}[/font_size][/b]\n");
        sb.Append($"{a.Cells.Count} blocks · [b]{a.MassKg / 1000f:0.0} t[/b]\n");
        sb.Append($"Engines {a.PowerKw:0} kW · [b]{a.PowerToWeight:0.0}[/b] kW/t\n");
        sb.Append($"Top speed about [b]{a.TopSpeedKmh:0} km/h[/b] · fuel {a.FuelL:0} L\n");
        if (a.Wheels.Count > 0) sb.Append($"Tips over at about [b]{a.TipAngleDeg:0}°[/b]\n");

        sb.Append("\n[b][color=#ffc74d]Armor[/color][/b]  [color=" + Ui.Hex(Ui.Dim) + "](what a shell must get through to reach the insides)[/color]\n");
        sb.Append(Armor("Front", a.Front));
        sb.Append(Armor("Sides", a.LeftSide.AvgMm <= a.RightSide.AvgMm ? a.LeftSide : a.RightSide));
        sb.Append(Armor("Rear ", a.Rear));
        sb.Append(Armor("Top  ", a.Top));

        if (a.Guns.Count > 0)
        {
            sb.Append("\n[b][color=#ffc74d]Guns[/color][/b]\n");
            foreach (GunInfo g in a.Guns)
            {
                string mount = g.Section == 0 ? "fixed in hull" : "turret";
                if (!g.Valid)
                {
                    sb.Append(Line(Ui.Bad, $"{g.Label} breech: not a working gun yet"));
                    continue;
                }

                GunStats s = g.Stats;
                float v500 = GunMath.SpeedAtRange(s.MuzzleVelocity, s.DragK, 500);
                sb.Append($"[b]{g.Label}[/b] · {mount} · {g.Barrels.Count} barrel{(g.Barrels.Count == 1 ? "" : "s")}" +
                          $"{(g.Brake.HasValue ? " + brake" : "")}\n");
                sb.Append($"  {s.MuzzleVelocity:0} m/s · AP pen [b]{s.ApPenMm:0}[/b] mm ({GunMath.PenAtSpeed(g.BreechDef.Breech, v500):0} at 500 m)\n");
                sb.Append($"  Reload {s.ReloadS:0.0} s · {g.Loaders} loader{(g.Loaders == 1 ? "" : "s")} · {g.Propellants} propellant\n");
                sb.Append($"  Ammo: [b]{g.ApRounds}[/b] AP · [b]{g.HeRounds}[/b] HE\n");
            }
        }

        foreach (TurretInfo t in a.Turrets)
            sb.Append($"\nTurret: {t.Blocks} blocks · {t.MassKg / 1000f:0.0} t · turns {t.TraverseDegPerSec:0}°/s\n");

        if (a.Warnings.Count > 0)
        {
            sb.Append("\n[b][color=#ffc74d]Problems[/color][/b]\n");
            foreach (DesignWarning w in a.Warnings.OrderBy(w => w.Severity))
            {
                Color c = w.Severity switch { Severity.Error => Ui.Bad, Severity.Warning => Ui.Warn, _ => Ui.Dim };
                string tag = w.Severity switch { Severity.Error => "Must fix: ", Severity.Warning => "", _ => "" };
                sb.Append(Line(c, "- " + tag + w.Text));
            }
        }
        else
        {
            sb.Append("\n" + Line(Ui.Good, "Ready to drive. Press Tab."));
        }

        return sb.ToString();
    }

    private void Toast(string text)
    {
        if (_toast == null) return;
        _toast.Text = text;
        _toastTime = 4f;
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
            return;
        }

        f.StoreString(CurrentBlueprint().ToJson());
        Toast($"Saved \"{_name}\". (Folder button opens the blueprints folder.)");
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
        foreach (Blueprint ex in ExampleDesigns.All)
        {
            Blueprint copy = ex;
            menu.AddItem("Example: " + ex.Name, _loadActions.Count);
            _loadActions.Add(() =>
            {
                LoadBlueprint(copy);
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
            LoadBlueprint(bp);
            Toast(problems.Count > 0 ? string.Join(" ", problems) : $"Loaded \"{bp.Name}\".");
        }
        catch (FormatException e)
        {
            Toast(e.Message);
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
            return;
        }

        TestDriveRequested?.Invoke(bp);
    }
}
