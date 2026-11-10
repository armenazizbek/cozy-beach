using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>One kind of sea creature that can be caught alive: with the rod from the pier, or with the net underwater.</summary>
public class SeaSpecies
{
    public string id, name;
    /// <summary>'rod' (from the pier) or 'net' (underwater)</summary>
    public string how;
    /// <summary>'fish', or the animal's kind in SeaCreatures: 'octopus' | 'seahorse' | 'snake' | 'crab' | 'turtle'</summary>
    public string kind = "fish";
    /// <summary>shown in the aquarium list until it is caught</summary>
    public string hint;
    /// <summary>a fish's colours and pattern (see Underwater.fishModel)</summary>
    public int top, belly, accent, pattern;
    /// <summary>coins paid for one caught with the rod</summary>
    public double value;
    /// <summary>how often the rod brings it up, compared with the others; 0 for net creatures</summary>
    public double weight;
    /// <summary>'any' | 'day' | 'night' | 'rain': when the rod can bring it up</summary>
    public string when = "any";
}

/// <summary>
/// Everything that can live in the camp aquarium. A small one of each kind caught swims there. Rod fish come from the pier;
/// the reef fish and the sea-floor animals are scooped up with the net while diving.
/// </summary>
public static class Catches
{
    public const double ROD_COST = 60;
    public const double NET_COST = 120;

    /// <summary>The fish of the reef, in the order Underwater builds them.</summary>
    public static readonly SeaSpecies[] REEF =
    {
        new SeaSpecies { id = "clownfish", name = "Clownfish", how = "net", top = 0xf28a1e, belly = 0xffc880, accent = 0xf28a1e, pattern = 1, hint = "Orange with white bands. Lives on the reef." },
        new SeaSpecies { id = "bluetang", name = "Blue Tang", how = "net", top = 0x2f6fd0, belly = 0x9cc4f2, accent = 0xf2d03a, pattern = 3, hint = "Blue with a yellow tail. Lives on the reef." },
        new SeaSpecies { id = "snapper", name = "Red Snapper", how = "net", top = 0xd8484f, belly = 0xf6b8b0, accent = 0xc23a40, pattern = 0, hint = "A red fish of the reef." },
        new SeaSpecies { id = "wrasse", name = "Striped Wrasse", how = "net", top = 0x4fa862, belly = 0xe6e48a, accent = 0xf0c63a, pattern = 2, hint = "Green with stripes. Lives on the reef." },
        new SeaSpecies { id = "silverfish", name = "Silver Bream", how = "net", top = 0x8c98a6, belly = 0xf2f4f6, accent = 0x6c7886, pattern = 0, hint = "A plain silver fish of the reef." },
    };

    /// <summary>The fish the rod brings up from the pier.</summary>
    public static readonly SeaSpecies[] ROD =
    {
        new SeaSpecies { id = "sardine", name = "Blue Sardine", how = "rod", top = 0x5f93c4, belly = 0xe8f0f6, accent = 0x3f6fa0, pattern = 0, value = 1, weight = 40, hint = "Bites at any time, from the pier." },
        new SeaSpecies { id = "mackerel", name = "Green Mackerel", how = "rod", top = 0x2f8f7a, belly = 0xdfeee0, accent = 0x1f5f58, pattern = 2, value = 1, weight = 30, hint = "Bites at any time, from the pier." },
        new SeaSpecies { id = "sunbream", name = "Sun Bream", how = "rod", top = 0xf2c230, belly = 0xfff0b0, accent = 0xe88a1e, pattern = 0, value = 2, weight = 16, when = "day", hint = "Only bites in daylight, from the pier." },
        new SeaSpecies { id = "roseperch", name = "Rose Perch", how = "rod", top = 0xe07a9a, belly = 0xfde0e6, accent = 0xb04a70, pattern = 3, value = 2, weight = 9, hint = "A shy one. It takes patience, from the pier." },
        new SeaSpecies { id = "moonfish", name = "Moonfish", how = "rod", top = 0x7a6fd0, belly = 0xe6e0ff, accent = 0xc8c0ff, pattern = 0, value = 4, weight = 22, when = "night", hint = "Only bites after dark, from the pier." },
        new SeaSpecies { id = "raindarter", name = "Rain Darter", how = "rod", top = 0x3fb8c8, belly = 0xdff6f8, accent = 0x1f7f90, pattern = 2, value = 3, weight = 30, when = "rain", hint = "Only bites while it rains, from the pier." },
    };

    /// <summary>The one golden fish. There is a single one in the whole sea, and it is very fast: the net's hardest catch.</summary>
    public static readonly SeaSpecies GOLD = new SeaSpecies
    {
        id = "goldfish", name = "Golden Fish", how = "net", top = 0xf5b82e, belly = 0xffe9a0, accent = 0xff8a1e, pattern = 0,
        hint = "Only one in the whole sea, and very, very fast.",
    };

    /// <summary>The animals of the sea floor, by their kind in SeaCreatures.</summary>
    public static readonly SeaSpecies[] ANIMALS =
    {
        new SeaSpecies { id = "crab", name = "Reef Crab", how = "net", kind = "crab", hint = "Scuttles over the sea floor." },
        new SeaSpecies { id = "seahorse", name = "Seahorse", how = "net", kind = "seahorse", hint = "Drifts upright among the sea grass." },
        new SeaSpecies { id = "snake", name = "Sea Snake", how = "net", kind = "snake", hint = "Winds along just above the sand." },
        new SeaSpecies { id = "octopus", name = "Octopus", how = "net", kind = "octopus", hint = "Sits on the sea floor and jets away when startled." },
        new SeaSpecies { id = "turtle", name = "Sea Turtle", how = "net", kind = "turtle", hint = "Glides slowly through open water." },
    };

    public static readonly SeaSpecies[] ALL = ROD.Concat(REEF).Concat(new[] { GOLD }).Concat(ANIMALS).ToArray();

    public static readonly Dictionary<string, SeaSpecies> BY_ID = ALL.ToDictionary(s => s.id);

    /// <summary>The species for an animal of SeaCreatures, or null.</summary>
    public static SeaSpecies animal(string kind) => ANIMALS.FirstOrDefault(s => s.kind == kind);

    /// <summary>
    /// Which fish takes the bait. `dayTime` is the game's 0..1 clock (the sun is up for the first half or so). Fish not yet
    /// in the aquarium bite a little more readily, so a full tank does not come down to luck alone.
    /// </summary>
    public static SeaSpecies pickRod(double dayTime, bool raining, ICollection<string> have, Func<double> rnd)
    {
        bool night = dayTime >= 0.64 && dayTime <= 0.94;
        var pool = ROD.Where(s => s.when == "any" || (s.when == "night" && night) || (s.when == "day" && !night) || (s.when == "rain" && raining)).ToList();
        double w(SeaSpecies s) => s.weight * (have != null && have.Contains(s.id) ? 1 : 1.6);
        double roll = rnd() * pool.Sum(w);
        foreach (var s in pool)
        {
            roll -= w(s);
            if (roll <= 0) return s;
        }
        return pool[pool.Count - 1];
    }
}
