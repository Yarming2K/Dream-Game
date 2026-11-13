using System;
using System.Collections.Generic;
using DreamGame.Core;
using DreamGame.Game.Audio;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The tutorials' state for the whole game, kept across the builder and the range: the boot camp, if it's running,
/// and what's been seen (saved in user://tutorial.json between games).
/// </summary>
public static class Tutorial
{
    private const string SavePath = "user://tutorial.json";
    private static TutorialProgress _progress;

    public static TutorialProgress Progress => _progress ??= Load();

    /// <summary>The boot camp, while it runs (and on its last card once finished, until closed).</summary>
    public static BootCamp Camp { get; private set; }

    public static bool Running => Camp != null && !Camp.Finished;

    public static void StartBootCamp()
    {
        Camp = new BootCamp();
        Progress.Welcomed = true;
        Save();
    }

    public static void StopBootCamp() => Camp = null;

    /// <summary>Tells the boot camp what's happened; returns the step just finished, if one was.</summary>
    public static TutorialStep Report(in TutorialFacts f)
    {
        if (!Running) return null;
        TutorialStep done = Camp.Update(f);
        if (done != null && Camp.Finished)
        {
            Progress.BootCampDone = true;
            Save();
        }

        return done;
    }

    public static void Save()
    {
        using FileAccess f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        f?.StoreString(Progress.ToJson());
    }

    private static TutorialProgress Load() =>
        FileAccess.FileExists(SavePath) ? TutorialProgress.FromJson(FileAccess.GetFileAsString(SavePath)) : new TutorialProgress();
}

/// <summary>
/// The tutorials on screen: the boot camp card (the step, what to do, the keys, progress, and buttons to pick the
/// part, skip a step or leave), first-time tips, and the welcome on the very first game. One per screen; the state
/// lives in <see cref="Tutorial"/>.
/// </summary>
public partial class TutorialOverlay : Control
{
    /// <summary>Builder: pick this part on the parts panel.</summary>
    public Action<string> PickPart;

    /// <summary>Builder: start the boot camp (loads its car).</summary>
    public Action StartCamp;

    /// <summary>On the range the mouse is usually held by the game: say how to free it for the buttons.</summary>
    public bool MouseHeld;

    private readonly PanelContainer _card, _tip, _welcome;
    private readonly Label _caption, _title, _body, _keys, _tipTitle, _tipBody, _mouseHint, _tipMouseHint;
    private readonly HBoxContainer _pips, _buttons;
    private readonly Button _pick, _skip, _leave, _close;
    private readonly Queue<string> _tips = new();
    private TutorialStep _shown;
    private BootCamp _lastCamp;
    private int _lastIndex = -1;
    private string _tipId;
    private float _doneTimer;

    /// <param name="top">How far down the screen the cards sit.</param>
    /// <param name="left">Top left corner (the range, where the middle is the aim) rather than top middle (the builder).</param>
    public TutorialOverlay(float top, bool left)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _card = Card(top, left, 560);
        VBoxContainer col = Ui.Column(6);
        _card.AddChild(col);
        HBoxContainer head = Ui.Row(10);
        col.AddChild(head);
        _caption = Ui.Caption("Boot camp", Ui.Accent);
        head.AddChild(_caption);
        head.AddChild(Ui.Spacer());
        _pips = Ui.Row(3);
        head.AddChild(_pips);
        _title = Ui.Label("", 22, Ui.Text, "Title");
        col.AddChild(_title);
        _body = Ui.Wrapped("", 15);
        col.AddChild(_body);
        _keys = Ui.Label("", 13, Ui.Accent, "Caption");
        col.AddChild(_keys);
        _buttons = Ui.Row(8);
        col.AddChild(_buttons);
        _pick = Ui.Button("Pick it for me", () => { if (_shown?.Part != null) PickPart?.Invoke(_shown.Part); }, "Picks this step's part on the parts panel");
        _skip = Ui.Button("Skip step", () => { Tutorial.Camp?.Skip(); Refresh(); }, "Move on without doing this step", "GhostButton");
        _leave = Ui.Button("Leave boot camp", () => { Tutorial.StopBootCamp(); Refresh(); }, "Stop the boot camp (Help in the builder starts it again)", "GhostButton");
        _close = Ui.Button("Close", () => { Tutorial.StopBootCamp(); Refresh(); }, null, "PrimaryButton");
        _buttons.AddChild(_pick);
        _buttons.AddChild(Ui.Spacer());
        _buttons.AddChild(_skip);
        _buttons.AddChild(_leave);
        _buttons.AddChild(_close);
        _mouseHint = Ui.Small("Esc frees the mouse for these buttons.");
        col.AddChild(_mouseHint);

        _tip = Card(top, left, 500);
        VBoxContainer tc = Ui.Column(6);
        _tip.AddChild(tc);
        tc.AddChild(Ui.Caption("Tip", Ui.Accent));
        _tipTitle = Ui.Label("", 20, Ui.Text, "Title");
        tc.AddChild(_tipTitle);
        _tipBody = Ui.Wrapped("", 15);
        tc.AddChild(_tipBody);
        HBoxContainer tb = Ui.Row(8);
        tc.AddChild(tb);
        tb.AddChild(Ui.Button("Got it · Enter", DismissTip, null, "PrimaryButton"));
        tb.AddChild(Ui.Spacer());
        tb.AddChild(Ui.Button("No more tips", () =>
        {
            Tutorial.Progress.TipsOff = true;
            Tutorial.Save();
            _tips.Clear();
            DismissTip();
        }, "Turn first-time tips off", "GhostButton"));
        _tipMouseHint = Ui.Small("Enter closes it; Esc frees the mouse for the buttons.");
        tc.AddChild(_tipMouseHint);

        _welcome = Card(top, left, 600);
        VBoxContainer wc = Ui.Column(8);
        _welcome.AddChild(wc);
        wc.AddChild(Ui.Caption("Welcome", Ui.Accent));
        wc.AddChild(Ui.Label("Build it, then fight in it", 24, Ui.Text, "Title"));
        wc.AddChild(Ui.Wrapped("Everything here is built from blocks: tanks, ships, planes, buildings. New here? The boot camp takes about five minutes: you fit the parts that make a small car drive and shoot, then take it out on the range and try it.", 15));
        HBoxContainer wb = Ui.Row(8);
        wc.AddChild(wb);
        wb.AddChild(Ui.Button("Start boot camp", () =>
        {
            _welcome.Visible = false;
            StartCamp?.Invoke();
            Refresh();
        }, null, "PrimaryButton"));
        wb.AddChild(Ui.Button("Not now", () =>
        {
            _welcome.Visible = false;
            Tutorial.Progress.Welcomed = true;
            Tutorial.Save();
            ShowTip("builder");
        }, "Help (top rail) starts the boot camp any time"));
        _welcome.Visible = false;
        Refresh();
    }

    private PanelContainer Card(float top, bool left, float width)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Visible = false, CustomMinimumSize = new Vector2(width, 0) };
        AddChild(p);
        if (left)
        {
            p.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            p.OffsetLeft = 14;
        }
        else
        {
            p.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
            p.GrowHorizontal = GrowDirection.Both;
        }

        p.OffsetTop = top;
        return p;
    }

    /// <summary>The welcome, on the very first game (the builder shows it).</summary>
    public void ShowWelcome()
    {
        _welcome.Visible = true;
        _tip.Visible = false;
    }

    /// <summary>
    /// Shows a first-time tip if it hasn't been seen (and tips are on); one at a time, the rest wait their turn.
    /// Not during the boot camp, which explains things its own way: the tip waits for another time.
    /// </summary>
    public void ShowTip(string id)
    {
        if (id == null || Tutorial.Running || !Tutorial.Progress.ShouldShow(id) || _welcome.Visible) return;
        if (_tipId == id || _tips.Contains(id)) return;
        if (_tipId != null)
        {
            _tips.Enqueue(id);
            return;
        }

        Tip t = Tips.Get(id);
        _tipId = id;
        _tipTitle.Text = t.Title;
        _tipBody.Text = t.Text;
        _tip.Visible = true;
        Tutorial.Progress.MarkSeen(id);
        Tutorial.Save();
        Sfx.Play2D(this, SoundId.Tick, -8f, 1.2f);
    }

    private void DismissTip()
    {
        _tip.Visible = false;
        _tipId = null;
        while (_tips.Count > 0)
        {
            string next = _tips.Dequeue();
            if (!Tutorial.Progress.ShouldShow(next)) continue;
            ShowTip(next);
            break;
        }
    }

    /// <summary>Each frame: tells the boot camp what's happened and keeps the card up to date.</summary>
    public void Tick(in TutorialFacts facts, float dt)
    {
        TutorialStep done = Tutorial.Report(facts);
        if (done != null)
        {
            // A moment of green on the step just done before the next one.
            _doneTimer = 1.3f;
            _caption.Text = "Done";
            _caption.AddThemeColorOverride("font_color", Ui.Good);
            _title.Text = "✓  " + done.Title;
            _title.AddThemeColorOverride("font_color", Ui.Good);
            Sfx.Play2D(this, SoundId.Reload, -6f, 1.2f);
        }

        if (_doneTimer > 0)
        {
            _doneTimer -= dt;
            if (_doneTimer <= 0) Refresh();
        }
        else if (Tutorial.Camp != _lastCamp || (Tutorial.Camp?.Index ?? -1) != _lastIndex)
            Refresh();

        _mouseHint.Visible = MouseHeld && _card.Visible;
        _tipMouseHint.Visible = MouseHeld && _tip.Visible;
        if (_tip.Visible && Tutorial.Running) _tip.Visible = false;
    }

    /// <summary>The current step on the card, or the boot camp's last card, or nothing.</summary>
    private void Refresh()
    {
        BootCamp camp = Tutorial.Camp;
        _card.Visible = camp != null;
        _shown = camp?.Current;
        _lastCamp = camp;
        _lastIndex = camp?.Index ?? -1;
        if (camp == null) return;
        _caption.RemoveThemeColorOverride("font_color");
        _caption.AddThemeColorOverride("font_color", Ui.Accent);
        _title.RemoveThemeColorOverride("font_color");
        int n = BootCamp.Steps.Count;
        foreach (Node c in _pips.GetChildren()) c.QueueFree();
        for (int i = 0; i < n; i++)
            _pips.AddChild(new ColorRect
            {
                CustomMinimumSize = new Vector2(10, 4), MouseFilter = MouseFilterEnum.Ignore,
                Color = i < camp.Index ? Ui.Accent : i == camp.Index ? Ui.AccentBright : new Color(Ui.Accent, 0.2f),
            });

        if (camp.Finished)
        {
            _caption.Text = "Boot camp · done";
            _title.Text = "That's the basics";
            _body.Text = "You built a car, drove it and hit a target with it. Next: open the examples (Open, top rail) and take them out; walk into the command post by the depot and press M to command units on the map; or fly the Kestrel from the airstrip. F1 lists every key.";
            _keys.Text = "";
            _pick.Visible = _skip.Visible = _leave.Visible = false;
            _close.Visible = true;
            return;
        }

        TutorialStep s = camp.Current;
        _caption.Text = $"Boot camp · {camp.Index + 1} of {n} · {(s.Place == TutorialPlace.Builder ? "builder" : "range")}";
        _title.Text = s.Title;
        _body.Text = s.Text;
        _keys.Text = s.Keys;
        _pick.Visible = s.Part != null && PickPart != null;
        _skip.Visible = _leave.Visible = true;
        _close.Visible = false;
    }

    public override void _Input(InputEvent e)
    {
        if (_tip.Visible && e is InputEventKey { Pressed: true, Echo: false } k && (k.PhysicalKeycode == Key.Enter || k.PhysicalKeycode == Key.KpEnter))
        {
            DismissTip();
            GetViewport().SetInputAsHandled();
        }
    }
}
