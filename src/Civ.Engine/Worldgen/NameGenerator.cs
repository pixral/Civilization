using Civ.Engine.Random;

namespace Civ.Engine.Worldgen;

/// <summary>
/// Syllable-assembly name generator.
/// </summary>
/// <remarks>
/// Placeholder quality on purpose. Names are the most visible part of a world with no map, so this
/// will eventually be per-culture with phoneme inventories that drift over centuries and leave
/// related languages looking related. None of that changes the interface, so it is not worth
/// building before there are cultures to hang it on.
/// </remarks>
public static class NameGenerator
{
    private static readonly string[] Onsets =
        ["Ar", "Bel", "Cor", "Dan", "El", "Fen", "Gal", "Hal", "Ith", "Kor", "Lor", "Mar",
         "Nor", "Oss", "Per", "Quel", "Ras", "Sar", "Tav", "Ul", "Ver", "Wyn", "Xan", "Zor"];

    private static readonly string[] Middles =
        ["a", "e", "i", "o", "u", "ae", "ia", "au", "ei", "ou"];

    private static readonly string[] Codas =
        ["dor", "ren", "mar", "th", "lis", "gard", "vek", "nia", "sk", "tel", "run", "mor",
         "ath", "wyn", "dis", "kal"];

    private static readonly string[] PolityForms =
        ["Kingdom of {0}", "{0}", "Realm of {0}", "Dominion of {0}", "Free State of {0}", "{0} Confederacy"];

    private static readonly string[] GivenPrefixes =
        ["Ald", "Bren", "Cass", "Dor", "Eir", "Fald", "Ger", "Hald", "Isen", "Jor", "Kir", "Lys",
         "Mor", "Nael", "Orin", "Ped", "Rael", "Sev", "Tor", "Ulf", "Ver", "Wald", "Yar", "Zed"];

    private static readonly string[] GivenSuffixes =
        ["ric", "an", "ia", "ur", "wyn", "os", "eth", "ar", "in", "ys", "ald", "or"];

    /// <summary>A personal name. Dynastic naming needs dynasties, which do not exist yet.</summary>
    public static string Person(ref Rng rng) =>
        rng.Pick(GivenPrefixes) + rng.Pick(GivenSuffixes);

    public static string Region(ref Rng rng) =>
        rng.Pick(Onsets) + rng.Pick(Middles) + rng.Pick(Codas);

    public static string Polity(ref Rng rng)
    {
        string root = rng.Pick(Onsets) + rng.Pick(Middles) + rng.Pick(Codas);
        return string.Format(rng.Pick(PolityForms), root);
    }
}
