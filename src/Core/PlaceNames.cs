using System.Collections.Generic;

namespace DreamGame.Core;

/// <summary>
/// Made-up place names for the world's islands, peaks, valleys and canyons, built from syllables so they sound like
/// one coast's names without being anywhere real. The same seed always gives the same names, and none repeats.
/// </summary>
public sealed class PlaceNames
{
    private static readonly string[] Starts =
    {
        "Al", "Ar", "Bal", "Bran", "Cal", "Cor", "Dun", "Ed", "Fal", "Gar", "Hal", "Hel", "Ist", "Kel", "Kor", "Lan", "Mor",
        "Nar", "Os", "Ral", "Rav", "Sel", "Sor", "Tal", "Tor", "Ul", "Val", "Ven", "Wyr", "Ys", "Bel", "Drak", "Far", "Gry",
        "Hask", "Jor", "Kest", "Lor", "Mar", "Nor", "Ost", "Pell", "Quen", "Ros", "Skar", "Thal", "Vey", "Wen", "Zar", "Ash",
        "Brey", "Cairn", "Del", "Eld", "Fen", "Glas", "Har", "Ivr", "Keld", "Lyn", "Mal", "Ner", "Orm", "Pen", "Rhun", "Sten",
    };

    private static readonly string[] Middles = { "", "", "", "a", "e", "i", "o", "an", "en", "or", "ar", "el", "in", "ev", "is", "ur" };

    private static readonly string[] Ends =
    {
        "ra", "ros", "mere", "vik", "holm", "dor", "wick", "stad", "heim", "more", "ford", "gard", "ness", "by", "ton", "ley",
        "wyn", "den", "ell", "ach", "yr", "oth", "an", "ia", "ock", "mark", "fell", "dal", "rin", "set", "aig", "us",
    };

    private readonly HashSet<string> _used = new();
    private Rng _rng;

    public PlaceNames(int seed) => _rng = new Rng((ulong)(uint)seed * 2654435761ul + 77ul);

    /// <summary>A new name, different from every one handed out before.</summary>
    public string Next()
    {
        for (int tries = 0; tries < 200; tries++)
        {
            string s = Starts[_rng.Int(Starts.Length)] + Middles[_rng.Int(Middles.Length)] + Ends[_rng.Int(Ends.Length)];
            // Avoid doubled letters across the joins that read badly ("Kellley").
            if (s.Contains("lll") || s.Contains("rrr") || s.Length > 11 || s.Length < 4) continue;
            s = char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
            if (_used.Add(s)) return s;
        }

        string fallback = "Place " + _used.Count;
        _used.Add(fallback);
        return fallback;
    }

    /// <summary>One of <paramref name="choices"/>, picked by the same generator.</summary>
    public string Pick(params string[] choices) => choices[_rng.Int(choices.Length)];
}
