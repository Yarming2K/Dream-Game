using System;
using System.Linq;
using DreamGame.Core;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// Settings for a scripted benchmark run, read from the command line after <c>--</c>:
/// <code>godot --path . --maximized -- --bench=deck --seconds=45 --out=C:\somewhere --fullres</code>
/// The game skips the builder, sets up the scene on the test range, plays it by itself, writes what each second
/// cost to a CSV with a summary and screenshots, and quits. Scenes:
/// <list type="bullet">
/// <item>idle: the Warden at the depot, nothing happening (the baseline).</item>
/// <item>deck: the Colossus at the anchorage with the camera circling it; live fire from 5 s.</item>
/// <item>jet: the Falcon takes off, climbs to about 400 m and circles over the sea.</item>
/// <item>drill / bigdrill: the battle drill (about 100 / 200 vehicles), watched from the depot.</item>
/// <item>look: the camera circles one machine close up (--design=Warden by default) and takes screenshots all
/// round, for checking how things look.</item>
/// </list>
/// </summary>
public static class Bench
{
    public static string Scene;
    public static float Seconds = 40f;
    public static string Out;
    public static bool FullRes;
    public static string DesignName;
    public static string Time, Weather;

    public static bool Active => Scene != null;

    public static readonly string[] Scenes = { "idle", "deck", "jet", "drill", "bigdrill", "look" };

    public static void Parse(string[] args)
    {
        foreach (string a in args)
        {
            string[] kv = a.TrimStart('-').Split('=', 2);
            string key = kv[0].ToLowerInvariant(), value = kv.Length > 1 ? kv[1] : "";
            switch (key)
            {
                case "bench":
                    Scene = Scenes.Contains(value.ToLowerInvariant()) ? value.ToLowerInvariant() : "idle";
                    break;
                case "seconds" when float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s):
                    Seconds = Math.Clamp(s, 5f, 600f);
                    break;
                case "out":
                    Out = value;
                    break;
                case "fullres":
                    FullRes = true;
                    break;
                case "design":
                    DesignName = value;
                    break;
                case "time":
                    Time = value;
                    break;
                case "weather":
                    Weather = value;
                    break;
            }
        }
    }

    /// <summary>The design the scene starts in.</summary>
    public static Blueprint Design()
    {
        string name = DesignName ?? Scene switch { "deck" => "Colossus", "jet" => "Falcon", _ => "Warden" };
        return ExampleDesigns.All.First(b => b.Name.StartsWith(name, StringComparison.Ordinal));
    }

    /// <summary>Where results go: the folder given, or a "bench" folder in the game's user data.</summary>
    public static string Folder()
    {
        string dir = string.IsNullOrEmpty(Out) ? ProjectSettings.GlobalizePath("user://bench") : Out;
        dir = System.IO.Path.Combine(dir, $"{Scene}{(DesignName != null ? "-" + DesignName : "")}-{DateTime.Now:yyyyMMdd-HHmmss}");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }
}
