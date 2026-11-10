using System.Linq;
using System.Text;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// The performance readout (F3): frame rate and frame time, what the renderer drew, how many vehicles,
/// shells, effects and lights are about, and how many milliseconds of each frame went where. Refreshed
/// twice a second. For finding out what makes a big battle slow on your own PC.
/// </summary>
public partial class PerfOverlay : PanelContainer
{
    private RichTextLabel _text;
    private float _timer;
    private int _frames;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Ui.Box(new Color(0.04f, 0.05f, 0.05f, 0.82f), 6, 10));
        _text = Ui.Rich();
        _text.CustomMinimumSize = new Vector2(430, 0);
        _text.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_text);
        Visible = false;
    }

    public void Toggle()
    {
        Visible = !Visible;
        _timer = 0;
        _frames = 0;
        for (int i = 0; i < Perf.Names.Length; i++) Perf.Take(i, out _);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _frames++;
        _timer += (float)delta;
        if (_timer < 0.5f) return;

        int frames = Mathf.Max(_frames, 1);
        _timer = 0;
        _frames = 0;
        double fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        double process = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        double physics = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        double draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        double objects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        double tris = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        double nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        double awake = Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects);

        int vehicles = 0, parked = 0, structures = 0, wrecks = 0, far = 0;
        var heavy = new System.Collections.Generic.List<(string name, int batches, int shapes)>();
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            // What's costing the most to draw and to collide with, near the camera.
            if (h.Body is Node3D hb && IsInstanceValid(hb) && hb.GlobalPosition.DistanceTo(WorldQuery.Viewer) < 400f && h.View != null)
            {
                (int batches, int shapes, _) = h.View.Stats();
                heavy.Add((h is VehicleBody hv ? hv.ShortName : hb.Name, batches, shapes));
            }

            if (h is VehicleBody v)
            {
                vehicles++;
                if (v.Parked) parked++;
                if (IsInstanceValid(v) && v.GlobalPosition.DistanceTo(WorldQuery.Viewer) > v.View.LodDistance) far++;
            }
            else if (h is StructureBody) structures++;
            else wrecks++;
        }

        Combat c = Combat.Instance;
        string Good(double v, double ok, double bad) => Ui.Hex(v <= ok ? Ui.Good : v <= bad ? Ui.Warn : Ui.Bad);
        var sb = new StringBuilder();
        sb.Append($"[b]{fps:0} FPS[/b]  [color={Good(1000.0 / Mathf.Max((float)fps, 1f), 17, 34)}]{1000.0 / Mathf.Max((float)fps, 1f):0.0} ms[/color]");
        sb.Append($"   [color={Ui.Hex(Ui.Dim)}]scripts {process:0.0} · physics {physics:0.0} ms[/color]\n");
        sb.Append($"Drawn: [b]{draws:0}[/b] draw calls · {objects:0} objects · {tris / 1e6:0.00} M triangles\n");
        sb.Append($"Vehicles [b]{vehicles}[/b] ({far} far model, {parked} parked) · structures {structures} · wrecks {wrecks}\n");
        sb.Append($"Shells [b]{c?.ShellCount ?? 0}[/b] · tracer pieces {c?.TracersDrawn ?? 0} · effects {FxNode.Live} · lights {Effects.LightsLive}\n");
        sb.Append($"Nodes {nodes:0} · bodies awake {awake:0}\n");
        if (heavy.Count > 0)
            sb.Append($"[color={Ui.Hex(Ui.Dim)}]Heaviest near you: {string.Join(" · ", heavy.OrderByDescending(x => x.batches).Take(3).Select(x => $"{x.name} {x.batches} batches, {x.shapes} shapes"))}[/color]\n");
        sb.Append($"[color={Ui.Hex(Ui.Accent)}]Per frame (ms):[/color] ");
        sb.Append(string.Join(" · ", Enumerable.Range(0, Perf.Names.Length).Select(i =>
        {
            double ms = Perf.Take(i, out _) / frames;
            return $"{Perf.Names[i]} [color={Good(ms, 2, 5)}]{ms:0.00}[/color]";
        })));
        _text.Text = sb.ToString();
    }
}
