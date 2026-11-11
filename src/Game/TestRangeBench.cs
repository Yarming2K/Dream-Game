using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// A benchmark run (see <see cref="Bench"/>): sets the scene up, flies or turns the camera by itself, and once a
/// second writes the frame rate, the renderer's counts and the milliseconds per frame spent in each part of the
/// game to bench.csv. Screenshots at the start, the middle, the end and the slowest moments; a summary.txt at the
/// end (also printed); then it quits.
/// </summary>
public partial class TestRange
{
    private const float BenchWarmup = 4f;

    private string _benchDir;
    private float _benchT, _benchSecond;
    private int _benchFrames, _benchShots;
    private bool _benchLive, _benchDone;
    private float _benchWorstFps = float.MaxValue;
    private StringBuilder _benchCsv;
    private readonly List<(float t, double fps, double[] perf, string heavy)> _benchRows = new();
    private readonly HashSet<int> _benchShotAt = new();

    private void BenchStart()
    {
        _benchDir = Bench.Folder();
        _benchCsv = new StringBuilder("t,fps,frame_ms,scripts_ms,physics_ms,draw_calls,objects,triangles_m,vehicles,awake_bodies,shells,tracers,effects,lights,nodes,screenshot," +
                                      string.Join(",", Perf.Names.Select(n => n.Replace(' ', '_') + "_ms")) + "\n");
        for (int i = 0; i < Perf.Names.Length; i++) Perf.Take(i, out _);
        foreach (int s in new[] { 3, (int)(Bench.Seconds / 2), (int)Bench.Seconds - 2 }) _benchShotAt.Add(s);
        GD.Print($"[bench] {Bench.Scene} for {Bench.Seconds:0} s · window {GetWindow().Size.X}×{GetWindow().Size.Y} · {Display.Describe()} · results in {_benchDir}");

        if (Bench.Scene is "drill" or "bigdrill")
        {
            ToggleDrill();
            if (Bench.Scene == "bigdrill") ToggleDrill();
        }

        if (Bench.Scene == "jet" && Mounted) _mount.SetAirThrottle(1f);
    }

    /// <summary>Runs before the camera is placed each frame: steers the scene, then measures.</summary>
    private void BenchTick(float dt)
    {
        if (_benchDone) return;
        _benchT += dt;
        // Keep the game from waiting on a click to capture the mouse.
        _holdFire = true;

        switch (Bench.Scene)
        {
            case "deck":
                if (!_benchLive && _benchT > 5f)
                {
                    _benchLive = true;
                    if (!_liveFire) ToggleLiveFire();
                }

                _yaw += dt * 0.12f;
                _pitch = -0.3f;
                break;
            case "jet" when Mounted:
                // Take off down the runway, climb to about 400 m, then circle.
                float alt = _mount.GlobalPosition.Y;
                _mount.SetAirThrottle(1f);
                if (_benchT < 6f) _pitch = 0.02f;
                else if (alt < 400f) _pitch = 0.22f;
                else _pitch = 0.0f;
                if (alt > 150f) _yaw -= dt * 0.12f;
                break;
            case "drill" or "bigdrill" or "idle":
                _yaw += dt * 0.08f;
                _pitch = -0.12f;
                break;
        }

        _benchFrames++;
        _benchSecond += dt;
        if (_benchSecond < 1f) return;
        float span = _benchSecond;
        _benchSecond = 0;
        int frames = Mathf.Max(_benchFrames, 1);
        _benchFrames = 0;

        double fps = frames / span;
        var perf = new double[Perf.Names.Length];
        for (int i = 0; i < perf.Length; i++) perf[i] = Perf.Take(i, out _) / frames;
        if (_benchT < BenchWarmup) return;

        int vehicles = 0;
        var heavy = new List<(string name, int batches, int shapes)>();
        foreach (IGridHost h in GridRegistry.Hosts)
        {
            if (h is VehicleBody) vehicles++;
            if (h.Body is Node3D hb && IsInstanceValid(hb) && h.View != null && hb.GlobalPosition.DistanceTo(WorldQuery.Viewer) < 400f)
            {
                (int batches, int shapes, _) = h.View.Stats();
                heavy.Add((h is VehicleBody hv ? hv.ShortName : hb.Name, batches, shapes));
            }
        }

        string heavyText = string.Join(" · ", heavy.OrderByDescending(x => x.batches).Take(3).Select(x => $"{x.name} {x.batches} batches {x.shapes} shapes"));

        // Screenshots: at set times, and the first few times it hits a new low after warming up.
        int second = (int)_benchT;
        string shot = "";
        bool worst = fps < _benchWorstFps * 0.8f && _benchT > BenchWarmup + 2f && _benchShots < 6;
        if (fps < _benchWorstFps) _benchWorstFps = (float)fps;
        if (_benchShotAt.Remove(second) || worst)
        {
            shot = $"shot_{second:000}s{(worst ? "_slow" : "")}.png";
            Image img = GetViewport().GetTexture().GetImage();
            if (img != null)
            {
                if (img.GetWidth() > 1920) img.Resize(1920, img.GetHeight() * 1920 / img.GetWidth(), Image.Interpolation.Bilinear);
                img.SavePng(Path.Combine(_benchDir, shot));
                _benchShots++;
            }
        }

        Combat c = Combat.Instance;
        string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        _benchCsv.Append(string.Join(",",
            F(_benchT), F(fps), F(1000.0 * span / frames),
            F(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0),
            F(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0),
            Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame).ToString("0", CultureInfo.InvariantCulture),
            Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame).ToString("0", CultureInfo.InvariantCulture),
            F(Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1e6),
            vehicles.ToString(CultureInfo.InvariantCulture),
            Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects).ToString("0", CultureInfo.InvariantCulture),
            (c?.ShellCount ?? 0).ToString(CultureInfo.InvariantCulture),
            (c?.TracersDrawn ?? 0).ToString(CultureInfo.InvariantCulture),
            FxNode.Live.ToString(CultureInfo.InvariantCulture),
            Effects.LightsLive.ToString(CultureInfo.InvariantCulture),
            Performance.GetMonitor(Performance.Monitor.ObjectNodeCount).ToString("0", CultureInfo.InvariantCulture),
            shot) + "," + string.Join(",", perf.Select(F)) + "\n");
        if (shot == "") _benchRows.Add((_benchT, fps, perf, heavyText));

        if (_benchT >= Bench.Seconds) BenchFinish();
    }

    private void BenchFinish()
    {
        _benchDone = true;
        File.WriteAllText(Path.Combine(_benchDir, "bench.csv"), _benchCsv.ToString());
        var s = new StringBuilder();
        s.AppendLine($"Scene: {Bench.Scene}, {Bench.Seconds:0} s, window {GetWindow().Size.X}x{GetWindow().Size.Y}");
        s.AppendLine(Display.Describe());
        s.AppendLine($"Graphics: {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()}), {OS.GetProcessorName()} x{OS.GetProcessorCount()}");
        if (_benchRows.Count > 0)
        {
            List<double> sorted = _benchRows.Select(r => r.fps).OrderBy(f => f).ToList();
            s.AppendLine($"FPS: average {sorted.Average():0.0}, median {sorted[sorted.Count / 2]:0.0}, worst second {sorted[0]:0.0}, best {sorted[^1]:0.0}");
            (float t, double fps, double[] perf, string heavy) worst = _benchRows.OrderBy(r => r.fps).First();
            s.AppendLine($"Slowest second at {worst.t:0} s ({worst.fps:0.0} FPS). Per frame: " +
                         string.Join(" · ", Perf.Names.Select((n, i) => $"{n} {worst.perf[i]:0.00} ms")));
            s.AppendLine("Average per frame: " + string.Join(" · ", Perf.Names.Select((n, i) => $"{n} {_benchRows.Average(r => r.perf[i]):0.00} ms")));
            if (worst.heavy != "") s.AppendLine($"Heaviest near the camera then: {worst.heavy}");
        }

        File.WriteAllText(Path.Combine(_benchDir, "summary.txt"), s.ToString());
        GD.Print("[bench] done\n" + s);
        GetTree().Quit();
    }
}
