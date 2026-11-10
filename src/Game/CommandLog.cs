using System;
using System.Collections.Generic;
using Godot;

namespace DreamGame.Game;

/// <summary>
/// What your units report: arriving, targets destroyed, coming under fire, heavy damage, losses, getting
/// stuck. Shown on the tactical map, and in the message feed while the map is closed. Reports of the same
/// kind that come in together ("in position") are rolled into one line naming them all.
/// </summary>
public static class CommandLog
{
    public sealed class Entry
    {
        public double Time;
        public string Text;
        public Color Color;
        public VehicleBody About;
        public string Group;
        public readonly List<string> Names = new();
        public string Suffix;
    }

    public static readonly Color Calm = new(0.62f, 0.82f, 1f), Good = new(0.55f, 0.92f, 0.6f), Warn = new(1f, 0.8f, 0.4f), Bad = new(1f, 0.45f, 0.4f);

    private static readonly List<Entry> _entries = new();
    public static IReadOnlyList<Entry> Entries => _entries;

    /// <summary>A new line, or one that changed (a group that grew).</summary>
    public static event Action<Entry> Posted;

    private static double Now => Godot.Time.GetTicksMsec() / 1000.0;

    public static void Post(string text, Color color, VehicleBody about = null)
    {
        var e = new Entry { Time = Now, Text = text, Color = color, About = about };
        Add(e);
    }

    /// <summary>
    /// A report that several units may make at once: "<paramref name="name"/> <paramref name="suffix"/>". Within a few
    /// seconds of the last one of the same <paramref name="group"/>, it joins that line instead.
    /// </summary>
    public static void PostGrouped(string group, string name, string suffix, Color color, VehicleBody about = null)
    {
        Entry last = _entries.Count > 0 ? _entries[^1] : null;
        if (last != null && last.Group == group && Now - last.Time < 4.0)
        {
            if (!last.Names.Contains(name)) last.Names.Add(name);
            last.Time = Now;
            last.Text = Join(last.Names) + " " + suffix;
            Posted?.Invoke(last);
            return;
        }

        var e = new Entry { Time = Now, Color = color, About = about, Group = group, Suffix = suffix };
        e.Names.Add(name);
        e.Text = name + " " + suffix;
        Add(e);
    }

    private static string Join(List<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        <= 4 => string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[^1],
        _ => $"{names[0]}, {names[1]} and {names.Count - 2} more",
    };

    private static void Add(Entry e)
    {
        _entries.Add(e);
        while (_entries.Count > 80) _entries.RemoveAt(0);
        Posted?.Invoke(e);
    }

    public static void Clear() => _entries.Clear();
}
