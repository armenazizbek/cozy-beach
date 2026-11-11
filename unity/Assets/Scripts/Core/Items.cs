using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/core/items.ts

public class ItemDef
{
    public string id;
    public string name;
    /// <summary>'trash' | 'coin' | 'jewelry' | 'quest' | 'shard'</summary>
    public string category;
    public double value;
    public string lore;
    /// <summary>'trash' | 'coin' | 'deep'</summary>
    public string signature;
    public double[] depth;
    public double weight;
    /// <summary>'wet' | 'dry' | 'any'</summary>
    public string zone;
    public string shardSet;
    public int? shardIndex;

    public ItemDef Clone() => (ItemDef)MemberwiseClone();
}

public class ShardSet
{
    public string id, name, lore;
    public double payout;
}

public class QuestNeed
{
    public string defId;
    public string category;
    public int count;
}

public class QuestDef
{
    public string id, giver, text;
    public double reward;
    public QuestNeed need;
}

public static class Items
{
    public static readonly ShardSet[] SHARD_SETS =
    {
        new ShardSet { id = "astrolabe", name = "Ancient Astrolabe", payout = 250, lore = "A navigator’s instrument, glued whole again. The stars it once measured still gleam on its rings." },
        new ShardSet { id = "medallion", name = "Pirate Medallion", payout = 250, lore = "A swaggering old medallion. Somewhere, a very smug skull is smiling at you." },
    };

    private static ItemDef shard(string set, int idx, string name) => new ItemDef
    {
        id = $"{set}_{idx + 1}",
        name = $"{name} Shard ({idx + 1}/3)",
        category = "shard",
        value = 27,
        lore = "A curved fragment of something far older than the tide. Two more pieces are out there…",
        signature = "deep",
        depth = new[] { 0.45, 0.8 },
        weight = 0,
        zone = "any",
        shardSet = set,
        shardIndex = idx,
    };

    /// <summary>
    /// How much a find sells for, as a share of the list prices below. The beach holds about 6000g of sellable finds in all, so scaling
    /// them down keeps the camper shop's upgrades (several hundred gold each) a real goal across many hours instead of a one-hour errand.
    /// </summary>
    private const double VALUE_SCALE = 0.55;

    private static ItemDef D(string id, string name, string category, double value, string signature, string lore, double d0, double d1, double weight, string zone) =>
        new ItemDef { id = id, name = name, category = category, value = value, signature = signature, lore = lore, depth = new[] { d0, d1 }, weight = weight, zone = zone };

    private static readonly ItemDef[] RAW_DEFS =
    {
        D("pulltab", "Soda Pull-Tab", "trash", 5, "trash", "Somebody’s summer, long since flat. Perfectly recyclable.", 0.12, 0.3, 5, "any"),
        D("nail", "Bent Rusty Nail", "trash", 6, "trash", "Pried from a pier plank decades ago. It has opinions about rust.", 0.12, 0.3, 4, "any"),
        D("tincan", "Crushed Tin Can", "trash", 8, "trash", "Once held beans. Now it holds a small piece of the shoreline.", 0.15, 0.35, 4, "any"),
        D("penny", "Old Copper Penny", "coin", 15, "coin", "Worn smooth by a thousand tides and a thousand pockets.", 0.22, 0.45, 5, "any"),
        D("quarter", "Silver Quarter", "coin", 25, "coin", "Heads, you find it. Tails, you also find it.", 0.25, 0.5, 3, "any"),
        D("doubloon", "1700s Gold Doubloon", "coin", 35, "coin", "Minted for a king, lost by a sailor, found by you.", 0.3, 0.55, 1.5, "any"),
        D("signet", "Signet Ring", "jewelry", 60, "deep", "A crest nobody remembers, worn proudly by somebody once.", 0.4, 0.65, 1.4, "any"),
        D("ruby_band", "Ruby Band", "jewelry", 120, "deep", "The ruby catches the morning light like it never left your hand.", 0.45, 0.7, 0.8, "any"),
        D("compass", "Grandfather’s Silver Compass", "quest", 150, "deep", "The needle still points home. Someone has been missing this.", 0.45, 0.75, 0, "wet"),
        D("brass_key", "Captain’s Brass Key", "quest", 180, "deep", "Ornate, heavy, and clearly meant to open something important.", 0.45, 0.75, 0, "dry"),
        D("bottle_cap", "Bottle Cap", "trash", 5, "trash", "Popped open on a happy afternoon, long ago.", 0.12, 0.35, 3.5, "any"),
        D("glass_bottle", "Glass Bottle", "trash", 9, "trash", "No message inside, sadly. Just a lot of sand.", 0.12, 0.35, 3.5, "any"),
        D("fishing_hook", "Rusty Fishing Hook", "trash", 6, "trash", "The one that got away. It got away with the line, too.", 0.12, 0.35, 3.5, "any"),
        D("flip_flop", "Lost Flip-Flop", "trash", 8, "trash", "Its partner is probably somewhere on another beach.", 0.12, 0.35, 3.5, "any"),
        D("rusty_bolt", "Rusty Bolt", "trash", 6, "trash", "Once held something important together.", 0.12, 0.35, 3.5, "any"),
        D("plastic_ring", "Six-Pack Ring", "trash", 5, "trash", "Best taken to the recycling bin before the gulls find it.", 0.12, 0.35, 3.5, "any"),
        D("dime", "Silver Dime", "coin", 18, "coin", "Small, bright and surprisingly heavy with stories.", 0.22, 0.5, 4, "any"),
        D("nickel", "Old Nickel", "coin", 20, "coin", "It has been through a great many pockets.", 0.22, 0.5, 4, "any"),
        D("silver_dollar", "Silver Dollar", "coin", 45, "coin", "A proper, weighty coin. Someone would have missed this one.", 0.3, 0.55, 1.2, "any"),
        D("real", "Spanish Real", "coin", 40, "coin", "Cut from a bar of silver on a ship that never arrived.", 0.3, 0.55, 1, "any"),
        D("denarius", "Roman Denarius", "coin", 60, "coin", "An emperor’s face, worn soft by two thousand years.", 0.35, 0.6, 0.6, "any"),
        D("sovereign", "Gold Sovereign", "coin", 75, "coin", "It still shines as if it were minted this morning.", 0.35, 0.6, 0.4, "any"),
        D("pocket_watch", "Silver Pocket Watch", "quest", 130, "deep", "It stopped ticking a long time ago, but the engraving is still readable.", 0.45, 0.8, 0, "wet"),
        D("pearl_necklace", "Pearl Necklace", "quest", 170, "deep", "Every pearl is still on its string. Someone will be overjoyed.", 0.45, 0.8, 0, "any"),
        D("sunglasses", "Vintage Sunglasses", "quest", 80, "deep", "Round, dark, and impossibly stylish.", 0.45, 0.8, 0, "dry"),
        D("harmonica", "Silver Harmonica", "quest", 100, "deep", "It still plays one wheezy, lovely note.", 0.45, 0.8, 0, "any"),
        D("wedding_band", "Wedding Band", "quest", 160, "deep", "A small diamond and a very big reason to look for it.", 0.45, 0.8, 0, "wet"),
        D("toy_boat", "Wooden Toy Sailboat", "quest", 70, "deep", "Hand-carved. Somebody’s whole summer was spent on this.", 0.45, 0.8, 0, "any"),
        // finds that lie out in the open on the island, and nowhere else
        D("piece_eight", "Piece of Eight", "coin", 55, "coin", "Hacked from a silver bar by a hurried hand, and spent by a pirate who never came back for the change.", 0.2, 0.4, 0, "any"),
        D("black_pearl", "Black Pearl", "jewelry", 130, "deep", "It drinks the light and gives back a green shimmer. The island does not give these up easily.", 0.3, 0.5, 0, "any"),
        D("stone_idol", "Carved Stone Idol", "jewelry", 100, "deep", "A small, solemn face carved long before anyone drew a map of this place.", 0.3, 0.5, 0, "any"),
        D("conch", "Pink Conch Shell", "jewelry", 60, "deep", "Hold it to your ear and the whole sea is still in there.", 0.2, 0.4, 0, "any"),
        // the lagoon cay's own finds: left at the castaway's camp, and washed up on its two shores
        D("spyglass", "Brass Spyglass", "jewelry", 190, "deep", "Three brass tubes that still slide, and a lens with one small chip. Whoever camped here watched the horizon through it every day.", 0.2, 0.4, 0, "any"),
        D("nautilus", "Nautilus Shell", "jewelry", 70, "deep", "Cream, with tiger stripes that fade toward the mouth. Each chamber inside is a little bigger than the last: a whole life, kept in order.", 0.2, 0.4, 0, "any"),
        D("red_coral", "Red Coral Branch", "jewelry", 90, "deep", "A small tree the colour of a sunset, grown a hair’s width a year. The lagoon broke it off in a storm and left it on the sand.", 0.2, 0.4, 0, "any"),
        // the ember rock's own finds: by the cairn on the rim, on the cone and on the black sand
        D("sun_disc", "Golden Sun Disc", "jewelry", 240, "deep", "A beaten gold sun with a calm face. Someone carried it up a volcano and left it beside a pile of stones, and never said why.", 0.3, 0.5, 0, "any"),
        D("fire_opal", "Fire Opal", "jewelry", 150, "deep", "A coal that never goes out, still sitting in the black rock it grew in. Turn it and the orange runs to red and back.", 0.3, 0.5, 0, "any"),
        D("obsidian_blade", "Obsidian Blade", "jewelry", 85, "deep", "Black volcanic glass, chipped to an edge finer than steel, with the cord still wound on its grip. Mind your fingers.", 0.2, 0.4, 0, "any"),
        // finds that lie on the sea floor, for a diver
        D("starfish", "Orange Starfish", "jewelry", 12, "deep", "Five arms, a rough sunset-orange back and a very calm opinion of the tide.", 0.1, 0.2, 0, "any"),
        D("porcelain_plate", "Painted Porcelain Plate", "jewelry", 50, "deep", "Blue flowers on white, and not a chip on it. Somebody once ate their dinner off this.", 0.1, 0.2, 0, "any"),
        D("ship_bell", "Brass Ship’s Bell", "jewelry", 110, "deep", "It rang through fog for a hundred years, and now it rings only for the fish.", 0.1, 0.2, 0, "any"),
        D("sea_oyster", "Pearl Oyster", "jewelry", 80, "deep", "The shell is clamped shut. Inside, something round and quietly lovely.", 0.1, 0.2, 0, "any"),
        D("gold_bar", "Gold Bar", "jewelry", 150, "deep", "Stamped with a crown and a number nobody has used in two hundred years. It is much heavier than it looks.", 0.1, 0.2, 0, "any"),
        // a bottle on the sea floor with a map inside, and the strongbox that map leads to
        D("message_bottle", "Message in a Bottle", "quest", 0, "deep", "The cork is sealed with wax, and something is rolled up inside: a map, drawn in a hurry by someone who did not expect to be found.", 0.1, 0.2, 0, "any"),
        D("captain_chest", "Hidden Hatch", "quest", 0, "deep", "A trapdoor buried flat in the dune. It is not something you can pick up: it is a way down.", 0.3, 0.45, 0, "any"),
        // a buried chest that only shows up for someone who holds the treasure map
        D("treasure_cache", "Sealed Treasure Chest", "jewelry", 280, "deep", "Heavy, salt-stained and still locked. Whoever buried it meant it to stay buried, and drew a map so they could find it again.", 0.3, 0.45, 0, "any"),
        shard("astrolabe", 0, "Astrolabe"),
        shard("astrolabe", 1, "Astrolabe"),
        shard("astrolabe", 2, "Astrolabe"),
        shard("medallion", 0, "Medallion"),
        shard("medallion", 1, "Medallion"),
        shard("medallion", 2, "Medallion"),
    };

    public static readonly ItemDef[] ITEM_DEFS = RAW_DEFS.Select(d =>
    {
        var c = d.Clone();
        c.value = Math.Max(3, JS.Round(d.value * VALUE_SCALE));
        return c;
    }).ToArray();

    /// <summary>The finds that lie on the sea floor.</summary>
    public static readonly HashSet<string> DIVE_ITEM_IDS = new HashSet<string> { "message_bottle", "starfish", "porcelain_plate", "ship_bell", "sea_oyster", "gold_bar" };

    /// <summary>The finds that belong to the islands: each island has its own, and they lie nowhere else.</summary>
    public static readonly HashSet<string> ISLAND_ITEM_IDS = new HashSet<string>
    {
        "piece_eight", "black_pearl", "stone_idol", "conch",
        "spyglass", "nautilus", "red_coral",
        "sun_disc", "fire_opal", "obsidian_blade",
    };

    public static readonly Dictionary<string, ItemDef> DEF_BY_ID = ITEM_DEFS.ToDictionary(d => d.id);

    private static QuestDef Q(string id, string giver, string text, double reward, string defId, int count) =>
        new QuestDef { id = id, giver = giver, text = text, reward = reward, need = new QuestNeed { defId = defId, count = count } };

    public static readonly QuestDef[] QUEST_DEFS =
    {
        Q("q_compass", "Marta", "Lost my grandfather’s silver compass near the wet shoreline.", 85, "compass", 1),
        Q("q_key", "Old Pim", "Dropped the captain’s brass key while walking the dunes. Please look!", 125, "brass_key", 1),
        Q("q_ruby", "Juno", "My ruby band slipped off while I was clamming. I’d be so grateful.", 70, "ruby_band", 1),
        Q("q_quarters", "The Collector", "Seeking two shiny silver quarters for my jar. Not picky, just shiny.", 65, "quarter", 2),
        Q("q_watch", "Grandma Ines", "My silver pocket watch fell out near the wet shoreline. It belonged to my father.", 85, "pocket_watch", 1),
        Q("q_pearls", "Celeste", "I lost my pearl necklace while paddling. Every pearl was a birthday present.", 110, "pearl_necklace", 1),
        Q("q_glasses", "Rico", "My lucky sunglasses blew off somewhere up near the dunes. I cannot see without them. Okay, I can, but I look worse.", 65, "sunglasses", 1),
        Q("q_harmonica", "Busker Bo", "I dropped my harmonica on the way to the pier. The music has gone out of my day.", 75, "harmonica", 1),
        Q("q_wedding", "Dana", "I took my wedding band off to swim and… well. Please look near the water.", 105, "wedding_band", 1),
        Q("q_boat", "Little Mo", "My wooden sailboat drifted away from me. It has a red sail and a very brave captain.", 55, "toy_boat", 1),
        Q("q_pennies", "The Jar Lady", "I am filling a jar with old copper pennies. Bring me three and I will pay well.", 40, "penny", 3),
        Q("q_doubloon", "Captain Brine", "A real pirate doubloon! If you find one I will take it off your hands for a fair price.", 100, "doubloon", 1),
        Q("q_denarius", "Professor Plum", "Romans once sailed this coast. A denarius would make my whole year, truly.", 105, "denarius", 1),
    };
}
