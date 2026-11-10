using System.Diagnostics;

namespace DreamGame.Game;

/// <summary>
/// Cheap timers for the performance overlay (F3): how much of each frame goes to shells, vehicles, AI
/// gunners and so on. Each counter adds up the time spent since the last time the overlay read it.
/// </summary>
public static class Perf
{
    public const int Shells = 0, Blasts = 1, Vehicles = 2, Ai = 3, Water = 4, Air = 5, Effects = 6, Visuals = 7;
    public static readonly string[] Names = { "Shells", "Explosions", "Driving", "AI gunners", "Ships", "Aircraft", "Effects", "Rebuilds" };

    private static readonly long[] Ticks = new long[Names.Length];
    private static readonly int[] Calls = new int[Names.Length];

    public static long Now => Stopwatch.GetTimestamp();

    /// <summary>Adds the time since <paramref name="started"/> (from <see cref="Now"/>) to a counter.</summary>
    public static void Add(int counter, long started)
    {
        Ticks[counter] += Stopwatch.GetTimestamp() - started;
        Calls[counter]++;
    }

    /// <summary>Milliseconds spent in a counter since it was last read, and resets it.</summary>
    public static double Take(int counter, out int calls)
    {
        double ms = Ticks[counter] * 1000.0 / Stopwatch.Frequency;
        calls = Calls[counter];
        Ticks[counter] = 0;
        Calls[counter] = 0;
        return ms;
    }
}
