using System.Collections.Generic;
using System.Linq;

/// <summary>One pattern of stars hidden in the telescope's sky.</summary>
public class ConstellationDef
{
    public string id, name, lore;
    /// <summary>where its middle is in the sky: degrees round from north, and degrees up from the horizon</summary>
    public double az, alt;
    /// <summary>its stars, in degrees from its middle (x to the right, y up), and how bright each is (0..1)</summary>
    public double[][] stars;
    /// <summary>the lines between its stars, as pairs of indices into stars</summary>
    public int[][] lines;
    /// <summary>how far its farthest star is from its middle, in degrees (worked out from the stars)</summary>
    public double extent;
}

/// <summary>
/// The constellations the telescope on Lagoon Cay can find: the old familiar ones and two that only the people of these
/// islands know. The journal has a page for each one (<see cref="Journal"/>), written in when it is found.
/// </summary>
public static class Constellations
{
    /// <summary>coins for each constellation found, and a little more for the last of them</summary>
    public const int REWARD = 30, ALL_BONUS = 120;

    private static ConstellationDef C(string id, string name, double az, double alt, string lore, double[][] stars, int[][] lines)
    {
        // the given shapes are drawn about any point; they are moved so their middle is the middle of their stars
        double mx = stars.Average(s => s[0]), my = stars.Average(s => s[1]);
        var moved = stars.Select(s => new[] { s[0] - mx, s[1] - my, s.Length > 2 ? s[2] : 0.8 }).ToArray();
        double ext = moved.Max(s => System.Math.Sqrt(s[0] * s[0] + s[1] * s[1]));
        return new ConstellationDef { id = id, name = name, az = az, alt = alt, lore = lore, stars = moved, lines = lines, extent = ext };
    }

    private static double[] S(double x, double y, double b = 0.8) => new[] { x, y, b };
    private static int[] L(int a, int b) => new[] { a, b };

    public static readonly ConstellationDef[] ALL = new ConstellationDef[]
    {
        C("great_bear", "The Great Bear", 28, 46,
            "Seven bright stars make a ladle; the old sailors followed the two at the end of its bowl to find the north.",
            new[] { S(0, 0, 0.95), S(0, -5, 0.85), S(-7, -6, 0.8), S(-7.5, -1.5, 0.62), S(-12, -0.5, 0.92), S(-16.5, -1, 0.86), S(-22, -4, 0.9) },
            new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 0), L(3, 4), L(4, 5), L(5, 6) }),
        C("little_bear", "The Little Bear", 352, 60,
            "The smaller ladle hangs from the Pole Star, the one star that never moves while all the others wheel around it.",
            new[] { S(0, 0, 0.95), S(-3, -3.5, 0.55), S(-5, -6.5, 0.6), S(-6, -10, 0.62), S(-11, -11, 0.9), S(-11, -14.5, 0.78), S(-6.5, -13.5, 0.58) },
            new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 4), L(4, 5), L(5, 6), L(6, 3) }),
        C("cassiopeia", "Cassiopeia", 318, 44,
            "A queen on her throne, drawn as a crooked W. Every night she turns slowly upside down, and never seems to mind.",
            new[] { S(0, 0, 0.85), S(4, -4, 0.9), S(8, -1, 0.92), S(12, -5, 0.8), S(16, -1, 0.82) },
            new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 4) }),
        C("swan", "The Swan", 62, 50,
            "A great bird flying down the river of the Milky Way, with Deneb bright in its tail and a coloured double star for an eye.",
            new[] { S(0, 10, 0.95), S(0, 3, 0.85), S(0, -12, 0.82), S(-8, 1, 0.75), S(8, 6, 0.72), S(-13, -2, 0.6), S(12, 9, 0.58) },
            new[] { L(0, 1), L(1, 2), L(1, 3), L(1, 4), L(3, 5), L(4, 6) }),
        C("lyre", "The Lyre", 96, 38,
            "A little harp hung beside the Swan, with blue-white Vega at its tip. It is said to play whenever the sea is calm.",
            new[] { S(0, 0, 1.0), S(1.8, 1.6, 0.5), S(2, -2.5, 0.62), S(4.5, -3.2, 0.6), S(3.6, -7.5, 0.68), S(1, -6.6, 0.66) },
            new[] { L(0, 1), L(0, 2), L(2, 3), L(3, 4), L(4, 5), L(5, 2) }),
        C("crown", "The Northern Crown", 128, 32,
            "A small half-circle of stars, like a crown left on a table. Its brightest jewel is called the Pearl.",
            new[] { S(-6, 1, 0.62), S(-4.5, -1.8, 0.6), S(-1.8, -3.2, 0.7), S(1, -3.4, 0.92), S(3.6, -2.4, 0.64), S(5.6, -0.4, 0.6), S(6.4, 2.2, 0.58) },
            new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 4), L(4, 5), L(5, 6) }),
        C("scorpion", "The Scorpion", 168, 22,
            "Low over the southern sea, its red heart Antares glows like a coal. Its curled tail almost touches the water.",
            new[] { S(6, 9, 0.7), S(7, 5.5, 0.78), S(7, 2, 0.66), S(2.5, 2, 0.7), S(0, 0, 1.0), S(-1, -3, 0.72), S(-2, -6, 0.68), S(-2, -10, 0.7), S(-1, -13, 0.72), S(2, -15, 0.75), S(5, -14.2, 0.78), S(7, -12, 0.85), S(6, -9.6, 0.7) },
            new[] { L(0, 1), L(1, 2), L(1, 3), L(3, 4), L(4, 5), L(5, 6), L(6, 7), L(7, 8), L(8, 9), L(9, 10), L(10, 11), L(11, 12) }),
        C("orion", "Orion", 214, 30,
            "The hunter strides across the winter sky with three stars in a row for his belt, red Betelgeuse on one shoulder and blue Rigel at his foot.",
            new[] { S(-6, 8, 1.0), S(5, 7, 0.85), S(-2, 0, 0.82), S(0, 0.6, 0.84), S(2, 1.2, 0.8), S(-5, -9, 0.78), S(6, -8, 1.0), S(0, 12, 0.6) },
            new[] { L(0, 2), L(1, 4), L(2, 3), L(3, 4), L(2, 5), L(4, 6), L(0, 7), L(1, 7) }),
        C("marigold", "The Marigold", 262, 24,
            "The village’s own: a little ship under full sail, named after Captain Holt’s Marigold, so that she sails on up there while she lies on the reef down here.",
            new[] { S(-9, 1, 0.72), S(-5, -2, 0.78), S(5, -2, 0.78), S(9, 1, 0.72), S(0, 11, 0.9), S(-4.5, 3, 0.66), S(5, 4, 0.66) },
            new[] { L(0, 1), L(1, 2), L(2, 3), L(4, 5), L(4, 6), L(5, 6) }),
        C("golden_dog", "The Golden Dog", 292, 34,
            "Another the islanders named: a dog curled up asleep by a fire that is not there. Fishermen say it keeps watch while the camp sleeps.",
            new[] { S(0, 4, 0.88), S(1.5, 6.5, 0.6), S(4, 3, 0.74), S(8.5, 3.2, 0.74), S(11.5, 6.4, 0.66), S(8.4, -1, 0.62), S(3.4, -1, 0.62) },
            new[] { L(1, 0), L(0, 2), L(2, 3), L(3, 4), L(3, 5), L(2, 6) }),
        C("eagle", "The Eagle", 70, 22,
            "Bright Altair between two smaller stars, like a bird holding its wings out over the sea, gliding below the Swan.",
            new[] { S(0, 0, 1.0), S(-0.8, 2, 0.72), S(0.8, -2, 0.62), S(-7, 3, 0.6), S(6, -5, 0.62), S(-2, -8, 0.58) },
            new[] { L(1, 0), L(0, 2), L(0, 3), L(0, 4), L(2, 5) }),
        C("dolphin", "The Dolphin", 100, 17,
            "A small diamond with a tail, leaping just above the waves. The boatmen say that when it rises the real ones come to play at the bow.",
            new[] { S(0, 0, 0.7), S(2, 1.2, 0.72), S(3.2, -0.8, 0.68), S(1.2, -2, 0.66), S(-2.2, -4, 0.6) },
            new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 0), L(3, 4) }),
        C("lion", "The Lion", 150, 52,
            "A great cat lying down, with a backwards question mark for its mane and Regulus shining at its chest.",
            new[] { S(0, 0, 1.0), S(0.5, 3, 0.62), S(-0.5, 6, 0.85), S(1.5, 8, 0.6), S(4, 9, 0.6), S(5, 7, 0.64), S(-9, 4, 0.72), S(-14, 1, 0.82), S(-9, 0, 0.62) },
            new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 4), L(4, 5), L(2, 6), L(6, 7), L(7, 8), L(8, 0) }),
        C("southern_cross", "The Southern Cross", 190, 12,
            "Small but bright, low over the southern horizon. Sailors far from home have steered by it for as long as there have been ships.",
            new[] { S(0, 4, 0.88), S(0, -4, 1.0), S(-3, 0, 0.85), S(3, 0.5, 0.76), S(1.2, -1.6, 0.52) },
            new[] { L(0, 1), L(2, 3) }),
        C("twins", "The Twins", 236, 50,
            "Two brothers standing side by side, Castor and Pollux, each with a line of stars for his body. They never go anywhere without each other.",
            new[] { S(0, 0, 0.9), S(3, -1, 0.95), S(-1, -4, 0.6), S(-2, -8, 0.64), S(-3, -11, 0.6), S(2, -5, 0.62), S(1.5, -9, 0.66), S(0.5, -12, 0.6) },
            new[] { L(0, 1), L(0, 2), L(2, 3), L(3, 4), L(1, 5), L(5, 6), L(6, 7) }),
        C("seven_sisters", "The Seven Sisters", 241, 30,
            "A tiny misty cluster, easy to miss without the glass. Most people count six of the sisters; the seventh is said to be shy.",
            new[] { S(0, 0, 0.78), S(1.2, 0.6, 0.68), S(2.1, -0.4, 0.64), S(0.6, -1.4, 0.68), S(-0.9, -0.8, 0.6), S(-0.5, 1.2, 0.56), S(1.5, 1.8, 0.54) },
            new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 0), L(0, 4), L(4, 5), L(1, 6) }),
        C("lighthouse", "The Lighthouse", 330, 20,
            "Another of the islands’ own: a tall tower with a bright lamp at its top, drawn by the first keeper of the light on the black rock.",
            new[] { S(-2, -6, 0.62), S(2, -6, 0.62), S(-1.2, 0, 0.64), S(1.2, 0, 0.64), S(0, 4, 1.0), S(-6, 5, 0.58), S(6, 5, 0.58) },
            new[] { L(0, 1), L(0, 2), L(1, 3), L(2, 3), L(2, 4), L(3, 4), L(4, 5), L(4, 6) }),
        C("sea_turtle", "The Sea Turtle", 5, 25,
            "The islands’ oldest story: a turtle that swims slowly across the sky all year, carrying the islands on its back so they never sink.",
            new[] { S(7, 0.5, 0.82), S(3, 3, 0.66), S(-3, 3, 0.64), S(-3, -2, 0.64), S(3, -2, 0.66), S(-6, 0.5, 0.58), S(6, 5, 0.56), S(6, -4, 0.56) },
            new[] { L(0, 1), L(0, 4), L(1, 2), L(2, 3), L(3, 4), L(2, 5), L(3, 5), L(1, 6), L(4, 7) }),
    };

    public static readonly Dictionary<string, ConstellationDef> BY_ID = ALL.ToDictionary(c => c.id);

    /// <summary>the journal page written in when this constellation is found</summary>
    public static string pageId(string id) => "star_" + id;
}
