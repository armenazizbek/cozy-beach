using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// Port of src/core/StateManager.ts

public class InventoryItem
{
    public int uid;
    public string defId, name, category, lore;
    public double value;
}

public class QuestResult
{
    public string id, giver;
    public double reward;
    public int progress, need;
}

public class FoundResult
{
    /// <summary>the backpack entry, or null for shards and Lost & Found items</summary>
    public InventoryItem item;
    /// <summary>set when the find belongs to a Lost & Found note</summary>
    public QuestResult quest;
}

public class PendingLetter
{
    public string id;
    [JsonProperty("in")] public double inSeconds;
}

/// <summary>The saved state of a game. Field names match the TypeScript (and its JSON saves).</summary>
public class GameData
{
    /// <summary>
    /// Which playthrough this is: set when it is first saved, and kept by every save and load of it. Each game has autosaves
    /// of its own (SaveGame.autosaveId), so starting a new game never writes over another game's progress.
    /// </summary>
    public string gameId;
    /// <summary>Game 1, Game 2..., as the save list names it</summary>
    public int gameNo;
    public double coins = 0;
    public double warmth = 100;
    public List<InventoryItem> inventory = new List<InventoryItem>();
    public Dictionary<string, List<int>> shards = new Dictionary<string, List<int>>();
    public List<string> assembled = new List<string>();
    public Dictionary<string, int> tiers = new Dictionary<string, int> { { "detector", 1 }, { "digging", 1 }, { "cleaning", 1 }, { "gear", 1 } };
    public double ecology = 0;
    public List<string> questsDone = new List<string>();
    public bool ownsGun, ownsCartKey, ownsBoatKey, ownsBow;
    /// <summary>bought marker flags at least once, so the flag tool is in the hands</summary>
    public bool ownsFlags;
    /// <summary>bought the bucket and spade, so the sandcastle tool is in the hands</summary>
    public bool ownsBucket;
    /// <summary>picked up the old treasure map from the island's altar</summary>
    public bool hasTreasureMap;
    /// <summary>found the message in a bottle, which holds a second map</summary>
    public bool hasBottleMap;
    /// <summary>bought the mask, snorkel, fins and harpoon, so the player can dive</summary>
    public bool ownsDiveGear;
    /// <summary>the great door has been opened</summary>
    public bool doorOpen;
    /// <summary>the gold in the sea cave has been taken, which opens the Luxury shop</summary>
    public bool hoardTaken;
    /// <summary>luxuries bought so far (ids from LUXURY)</summary>
    public List<string> luxury = new List<string>();
    /// <summary>camp decorations bought so far (ids from DECOR)</summary>
    public List<string> decor = new List<string>();
    /// <summary>which of SIGN_TEXTS the camp sign shows</summary>
    public int signText = 0;
    /// <summary>flags in the bag, ready to plant</summary>
    public int flags = 0;
    /// <summary>dog bones in the bag; without one the dog will not fetch</summary>
    public int bones = 0;
    /// <summary>journal pages written so far</summary>
    public List<string> pages = new List<string>(Journal.INITIAL_PAGE_IDS);
    /// <summary>pages written since the book was last opened</summary>
    public List<string> unreadPages = new List<string>();
    /// <summary>thank-you letters on their way: id of the note they answer, and seconds until they arrive</summary>
    public List<PendingLetter> pendingLetters = new List<PendingLetter>();
    /// <summary>letters that have arrived at the camp post</summary>
    public List<string> letters = new List<string>();
    public List<string> unreadLetters = new List<string>();
    /// <summary>bones bought so far this game; the shop only has BONE_STOCK to sell</summary>
    public int bonesBought = 0;
    /// <summary>which day of the adventure it is</summary>
    public int day = 1;
    public Dictionary<string, int> questCounts = new Dictionary<string, int>();
    /// <summary>'cycle' | 'golden' | 'day' | 'rain' | 'sunset' | 'night'</summary>
    public string lighting = "cycle";
    public Dictionary<string, int> found = new Dictionary<string, int>();
    public int nextUid = 1;
    /// <summary>photos taken with the camera, oldest first</summary>
    public List<PhotoInfo> photos = new List<PhotoInfo>();
    /// <summary>bought the fishing rod, to fish from the bench on the pier</summary>
    public bool ownsRod;
    /// <summary>bought the hand net, to catch sea creatures alive while diving</summary>
    public bool ownsNet;
    /// <summary>ids of the sea creatures caught alive so far (see Catches); a small one of each lives in the camp aquarium</summary>
    public List<string> aquarium = new List<string>();
    /// <summary>fish landed with the rod, all told</summary>
    public int fishCaught = 0;

    public GameData DeepClone() => JsonConvert.DeserializeObject<GameData>(JsonConvert.SerializeObject(this, SaveGame.Json), SaveGame.Json);
}

public class LuxuryDef
{
    public string id, name, desc;
    public double cost;
}

public class DecorDef
{
    public string id, name, blurb;
    public double cost;
}

public class TierInfo
{
    public string label;
    public string[] names, desc;
}

public class StateManager
{
    /// <summary>What the gold can buy. These only go on sale once it has been found.</summary>
    public static readonly LuxuryDef[] LUXURY =
    {
        new LuxuryDef { id = "gold_detector", name = "Golden Detector", desc = "Your detector, finished in gold. It finds exactly what it always found, in style.", cost = 2500 },
        new LuxuryDef { id = "gold_trowel", name = "Golden Trowel", desc = "A gilded blade and a dark wooden handle, for the most elegant holes on the coast.", cost = 1500 },
        new LuxuryDef { id = "gold_cart", name = "Gilded Golf Cart", desc = "Gold paint, cream seats and no shame at all.", cost = 3000 },
        new LuxuryDef { id = "marigold_boat", name = "The Marigold II", desc = "Your boat in the colours of the captain’s ship, with her name on the stern.", cost = 4000 },
        new LuxuryDef { id = "monster_skull", name = "The Monster’s Skull", desc = "A team of very confused movers brings it up from the cave and sets it up at camp.", cost = 3000 },
        new LuxuryDef { id = "gold_bone", name = "Golden Bone", desc = "The dog carries it everywhere and will not be parted from it.", cost = 1000 },
    };

    public static readonly double[] TIER_COSTS = { 0, 250, 800 };
    public const double GUN_COST = 150;
    public const double CART_KEY_COST = 300;
    public const double BOAT_KEY_COST = 350;
    public const double BOW_COST = 220;
    public const double BONE_COST = 8;
    public const double BUCKET_COST = 40;
    public const double DIVE_COST = 1500;

    /// <summary>Things to make the camp feel like home. Each is bought once and appears at a fixed place in the camp.</summary>
    public static readonly DecorDef[] DECOR =
    {
        new DecorDef { id = "rug", name = "Woven Rug", cost = 450, blurb = "A thick striped rug to lay out in front of the fire. Soft underfoot, and it ties the whole camp together." },
        new DecorDef { id = "sign", name = "Camp Sign", cost = 300, blurb = "A painted wooden sign by the path. Click it to change what it says." },
        new DecorDef { id = "lights", name = "String Lights", cost = 700, blurb = "A strand of little bulbs strung between two poles. They glow warmly once the sun goes down." },
        new DecorDef { id = "cooler", name = "Cooler Box", cost = 150, blurb = "A red and white cooler by the workbench. Nobody knows what is in it, and nobody checks." },
        new DecorDef { id = "palms", name = "Potted Palms", cost = 250, blurb = "Two little palms in clay pots, one at each end of the camp." },
        new DecorDef { id = "umbrella", name = "Beach Umbrella", cost = 350, blurb = "A striped parasol with a folding lounger underneath it, for doing absolutely nothing." },
        new DecorDef { id = "surf", name = "Surfboard Rack", cost = 400, blurb = "A driftwood rack holding three colourful boards. You do not surf. That is not the point." },
        new DecorDef { id = "tiki", name = "Tiki Torches", cost = 500, blurb = "Two bamboo torches with dancing flames to light the way up from the water." },
    };

    public static readonly string[] SIGN_TEXTS = { "Cozy Camp", "Gone Treasure Hunting", "Treasure Hunter HQ", "Sandy Toes Welcome", "Dig Here (Maybe)", "Home Sweet Dune" };
    /// <summary>how many bones the shop will ever sell in one game, so the dog cannot be spammed</summary>
    public const int BONE_STOCK = 8;
    public const double FLAG_PACK_COST = 15;
    public const int FLAG_PACK_SIZE = 5;
    public static readonly double[] DETECTOR_RADIUS = { 2.0, 3.8, 5.5 };
    public static readonly double[] DIG_RADIUS = { 0.7, 1.2, 1.8 };
    public static readonly double[] DIG_STEP = { 0.12, 0.25, 0.45 };
    public static readonly double[] DIG_COST = { 6, 4, 2 };
    public static readonly double[] BRUSH_PX = { 16, 30, 45 };
    public static readonly double[] MAX_WARMTH = { 100, 180, 300 };

    public static readonly Dictionary<string, TierInfo> TIER_INFO = new Dictionary<string, TierInfo>
    {
        {
            "detector", new TierInfo
            {
                label = "Detector",
                names = new[] { "Rusty Wand", "Dual-Frequency Coil", "Ground Penetrating Radar" },
                desc = new[] { "Small scan radius (2.0m). Same beep for trash and treasure.", "Radius 3.8m. HUD waveform tells trash from gold.", "Radius 5.5m. Glowing heatmap projected onto the sand." },
            }
        },
        {
            "digging", new TierInfo
            {
                label = "Digging",
                names = new[] { "Garden Trowel", "Sand Scoop", "Heavy-Duty Beach Shovel" },
                desc = new[] { "Radius 0.7m, 0.12m per click, costs 6 Warmth.", "Radius 1.2m, 0.25m per click, costs 4 Warmth.", "Radius 1.8m, unearths items in 1–2 clicks, costs 2 Warmth." },
            }
        },
        {
            "cleaning", new TierInfo
            {
                label = "Cleaning",
                names = new[] { "Old Toothbrush", "Horsehair & Brass Brush", "Camp Ultrasonic Bath" },
                desc = new[] { "Small brush (16px).", "Wide brush (30px). Cleaned items sell for +25%.", "Widest brush (45px) plus an Instant Clean button for common items." },
            }
        },
        {
            "gear", new TierInfo
            {
                label = "Thermos & Gear",
                names = new[] { "Tin Mug (Coffee)", "Insulated Thermos (Spiced Chai)", "Brass Espresso Flask" },
                desc = new[] { "100 Max Warmth. Wet sand drains 1.5x Warmth.", "180 Max Warmth. Waterproof boots negate the wet sand penalty.", "300 Max Warmth. Unlocks time-of-day control: freeze the sky at any time of day, or let the day and night cycle run." },
            }
        },
    };

    public GameData data = new GameData();
    private readonly List<Action> listeners = new List<Action>();

    public StateManager(GameData initial = null)
    {
        if (initial != null) data = initial;
        else data.warmth = maxWarmth;
        data.tiers ??= new GameData().tiers;
        foreach (var k in new[] { "detector", "digging", "cleaning", "gear" }) if (!data.tiers.ContainsKey(k)) data.tiers[k] = 1;
    }

    public Action subscribe(Action fn)
    {
        listeners.Add(fn);
        return () => listeners.Remove(fn);
    }

    public void emit()
    {
        foreach (var fn in listeners.ToArray()) fn();
    }

    public int tier(string key) => data.tiers.TryGetValue(key, out var t) ? t : 1;
    public double maxWarmth => MAX_WARMTH[tier("gear") - 1];
    public double detectorRadius => DETECTOR_RADIUS[tier("detector") - 1];
    public double digRadius => DIG_RADIUS[tier("digging") - 1];
    public double digStep => DIG_STEP[tier("digging") - 1];
    public double brushPx => BRUSH_PX[tier("cleaning") - 1];

    public double digCost(bool wet)
    {
        double b = DIG_COST[tier("digging") - 1];
        return wet && tier("gear") == 1 ? b * 1.5 : b;
    }

    public bool lanternUnlocked => tier("gear") >= 3;

    public void addCoins(double n)
    {
        data.coins += n;
        emit();
    }

    public void spendWarmth(double n)
    {
        data.warmth = Math.Max(0, data.warmth - n);
        emit();
    }

    public void restoreWarmth()
    {
        data.warmth = maxWarmth;
        emit();
    }

    /// <summary>
    /// Stores a cleaned item. Shards go to the shard tray, Lost & Found items are delivered
    /// on the spot (reward paid, note ticked off), everything else to the backpack.
    /// </summary>
    public FoundResult addFound(string defId)
    {
        if (!Items.DEF_BY_ID.TryGetValue(defId, out var def)) return new FoundResult();
        data.found[defId] = (data.found.TryGetValue(defId, out var f) ? f : 0) + 1;
        if (def.category == "shard" && def.shardSet != null && def.shardIndex != null)
        {
            if (!data.shards.TryGetValue(def.shardSet, out var owned)) data.shards[def.shardSet] = owned = new List<int>();
            if (!owned.Contains(def.shardIndex.Value)) owned.Add(def.shardIndex.Value);
            emit();
            return new FoundResult();
        }
        var q = Items.QUEST_DEFS.FirstOrDefault(x => !data.questsDone.Contains(x.id) && x.need.defId == defId);
        if (q != null)
        {
            int progress = (data.questCounts.TryGetValue(q.id, out var p) ? p : 0) + 1;
            data.questCounts[q.id] = progress;
            double reward = 0;
            if (progress >= q.need.count)
            {
                data.questsDone.Add(q.id);
                data.coins += q.reward;
                reward = q.reward;
            }
            emit();
            return new FoundResult { quest = new QuestResult { id = q.id, giver = q.giver, reward = reward, progress = progress, need = q.need.count } };
        }
        double bonus = tier("cleaning") >= 2 ? 1.25 : 1;
        var item = new InventoryItem
        {
            uid = data.nextUid++,
            defId = defId,
            name = def.name,
            category = def.category,
            value = JS.Round(def.value * bonus),
            lore = def.lore,
        };
        data.inventory.Add(item);
        emit();
        return new FoundResult { item = item };
    }

    public bool hasShard(string setId, int idx) => data.shards.TryGetValue(setId, out var l) && l.Contains(idx);

    public int count(string defId = null, string category = null) =>
        data.inventory.Count(i => (defId == null || i.defId == defId) && (category == null || i.category == category));

    public double sell(int uid)
    {
        int idx = data.inventory.FindIndex(i => i.uid == uid);
        if (idx < 0) return 0;
        var item = data.inventory[idx];
        data.inventory.RemoveAt(idx);
        data.coins += item.value;
        emit();
        return item.value;
    }

    public double sellCoinsAndRings()
    {
        var sellable = data.inventory.Where(i => i.category == "coin" || i.category == "jewelry").ToList();
        double total = sellable.Sum(i => i.value);
        data.inventory = data.inventory.Where(i => !sellable.Contains(i)).ToList();
        data.coins += total;
        emit();
        return total;
    }

    /// <summary>Recycles every piece of trash. Returns coins earned and pieces recycled.</summary>
    public (double coins, int pieces) recycleAll()
    {
        var trash = data.inventory.Where(i => i.category == "trash").ToList();
        double coins = trash.Sum(i => i.value);
        data.inventory = data.inventory.Where(i => i.category != "trash").ToList();
        data.coins += coins;
        data.ecology += trash.Count;
        emit();
        return (coins, trash.Count);
    }

    public bool canAssemble(string setId) => (data.shards.TryGetValue(setId, out var l) ? l.Count : 0) >= 3 && !data.assembled.Contains(setId);

    public double assemble(string setId)
    {
        if (!canAssemble(setId)) return 0;
        var set = Items.SHARD_SETS.FirstOrDefault(s => s.id == setId);
        if (set == null) return 0;
        data.assembled.Add(setId);
        data.coins += set.payout;
        emit();
        return set.payout;
    }

    public int questProgress(QuestDef q) =>
        data.questsDone.Contains(q.id) ? q.need.count : Math.Min(q.need.count, data.questCounts.TryGetValue(q.id, out var c) ? c : 0);

    /// <summary>Defs of quest items still needed that nobody has found yet, for the spawner to guarantee.</summary>
    public List<string> activeQuestDefIds()
    {
        var ids = new List<string>();
        foreach (var q in Items.QUEST_DEFS)
        {
            if (data.questsDone.Contains(q.id) || q.need.defId == null) continue;
            int missing = q.need.count - questProgress(q);
            for (int i = 0; i < missing; i++) ids.Add(q.need.defId);
        }
        return ids;
    }

    public double? upgradeCost(string key)
    {
        int t = tier(key);
        return t >= 3 ? (double?)null : TIER_COSTS[t];
    }

    public bool buyUpgrade(string key)
    {
        var cost = upgradeCost(key);
        if (cost == null || data.coins < cost.Value) return false;
        data.coins -= cost.Value;
        data.tiers[key] = tier(key) + 1;
        if (key == "gear") data.warmth = maxWarmth;
        emit();
        return true;
    }

    /// <summary>Without the key the golf cart will not start.</summary>
    public bool buyCartKey()
    {
        if (data.ownsCartKey || data.coins < CART_KEY_COST) return false;
        data.coins -= CART_KEY_COST;
        data.ownsCartKey = true;
        emit();
        return true;
    }

    /// <summary>The bow shoots unlimited arrows.</summary>
    public bool buyBow()
    {
        if (data.ownsBow || data.coins < BOW_COST) return false;
        data.coins -= BOW_COST;
        data.ownsBow = true;
        emit();
        return true;
    }

    /// <summary>Bones the shop can still sell.</summary>
    public int bonesLeft => Math.Max(0, BONE_STOCK - data.bonesBought);

    /// <summary>Writes a page into the journal. True only the first time.</summary>
    public bool unlockPage(string id)
    {
        data.pages ??= new List<string>(Journal.INITIAL_PAGE_IDS);
        data.unreadPages ??= new List<string>();
        if (data.pages.Contains(id)) return false;
        data.pages.Add(id);
        // with the whole book open from the start there is nothing "new" to flag
        if (!Journal.BOOK_SHOW_ALL) data.unreadPages.Add(id);
        emit();
        return true;
    }

    /// <summary>An owner has written back: the letter reaches the post after `delay` seconds.</summary>
    public void queueLetter(string id, double delay)
    {
        data.pendingLetters ??= new List<PendingLetter>();
        data.letters ??= new List<string>();
        if (data.letters.Contains(id) || data.pendingLetters.Any(p => p.id == id)) return;
        data.pendingLetters.Add(new PendingLetter { id = id, inSeconds = delay });
        emit();
    }

    /// <summary>Moves time along for letters in the post. Returns the ids of any that just arrived.</summary>
    public List<string> tickMail(double dt)
    {
        var pending = data.pendingLetters;
        var arrived = new List<string>();
        if (pending == null || pending.Count == 0) return arrived;
        foreach (var p in pending)
        {
            p.inSeconds -= dt;
            if (p.inSeconds <= 0) arrived.Add(p.id);
        }
        if (arrived.Count == 0) return arrived;
        data.pendingLetters = pending.Where(p => !arrived.Contains(p.id)).ToList();
        data.letters ??= new List<string>();
        data.unreadLetters ??= new List<string>();
        foreach (var id in arrived)
        {
            data.letters.Add(id);
            data.unreadLetters.Add(id);
        }
        emit();
        return arrived;
    }

    public void markLettersRead()
    {
        if (data.unreadLetters == null || data.unreadLetters.Count == 0) return;
        data.unreadLetters = new List<string>();
        emit();
    }

    public void markPagesRead()
    {
        if (data.unreadPages == null || data.unreadPages.Count == 0) return;
        data.unreadPages = new List<string>();
        emit();
    }

    public bool buyBone()
    {
        if (data.coins < BONE_COST || bonesLeft <= 0) return false;
        data.coins -= BONE_COST;
        data.bones += 1;
        data.bonesBought += 1;
        emit();
        return true;
    }

    public bool useBone()
    {
        if (data.bones <= 0) return false;
        data.bones--;
        emit();
        return true;
    }

    public bool buyLuxury(string id)
    {
        var l = LUXURY.FirstOrDefault(x => x.id == id);
        if (l == null || !data.hoardTaken || data.luxury.Contains(id) || data.coins < l.cost) return false;
        data.coins -= l.cost;
        data.luxury = new List<string>(data.luxury) { id };
        emit();
        return true;
    }

    public bool buyDecor(string id)
    {
        var d = DECOR.FirstOrDefault(x => x.id == id);
        if (d == null || data.decor.Contains(id) || data.coins < d.cost) return false;
        data.coins -= d.cost;
        data.decor = new List<string>(data.decor) { id };
        emit();
        return true;
    }

    public void cycleSign()
    {
        data.signText = (data.signText + 1) % SIGN_TEXTS.Length;
        emit();
    }

    public bool buyDiveGear()
    {
        if (data.ownsDiveGear || data.coins < DIVE_COST) return false;
        data.coins -= DIVE_COST;
        data.ownsDiveGear = true;
        emit();
        return true;
    }

    public bool buyBucket()
    {
        if (data.ownsBucket || data.coins < BUCKET_COST) return false;
        data.coins -= BUCKET_COST;
        data.ownsBucket = true;
        emit();
        return true;
    }

    /// <summary>A pack of marker flags: pay once, get a handful. Can be bought again and again.</summary>
    public bool buyFlags()
    {
        if (data.coins < FLAG_PACK_COST) return false;
        data.coins -= FLAG_PACK_COST;
        data.flags += FLAG_PACK_SIZE;
        data.ownsFlags = true;
        emit();
        return true;
    }

    /// <summary>Takes a flag out of the bag.</summary>
    public bool useFlag()
    {
        if (data.flags <= 0) return false;
        data.flags--;
        emit();
        return true;
    }

    public void returnFlag()
    {
        data.flags += 1;
        emit();
    }

    /// <summary>Without the key the boat's motor will not start.</summary>
    public bool buyBoatKey()
    {
        if (data.ownsBoatKey || data.coins < BOAT_KEY_COST) return false;
        data.coins -= BOAT_KEY_COST;
        data.ownsBoatKey = true;
        emit();
        return true;
    }

    /// <summary>The toy pistol is a purely-for-fun purchase.</summary>
    public bool buyGun()
    {
        if (data.ownsGun || data.coins < GUN_COST) return false;
        data.coins -= GUN_COST;
        data.ownsGun = true;
        emit();
        return true;
    }

    public void setLighting(string mode)
    {
        data.lighting = mode;
        emit();
    }

    // ---------------------------------------------------------------- fishing and the aquarium

    public bool buyRod()
    {
        if (data.ownsRod || data.coins < Catches.ROD_COST) return false;
        data.coins -= Catches.ROD_COST;
        data.ownsRod = true;
        emit();
        return true;
    }

    public bool buyNet()
    {
        if (data.ownsNet || data.coins < Catches.NET_COST) return false;
        data.coins -= Catches.NET_COST;
        data.ownsNet = true;
        emit();
        return true;
    }

    public List<string> aquarium => data.aquarium ??= new List<string>();

    /// <summary>Puts a kind of sea creature in the aquarium. True when it is the first of its kind.</summary>
    public bool addToAquarium(string id)
    {
        if (aquarium.Contains(id)) return false;
        aquarium.Add(id);
        emit();
        return true;
    }

    /// <summary>A fish landed with the rod: it is counted and paid for.</summary>
    public void landFish(SeaSpecies s)
    {
        data.fishCaught += 1;
        data.coins += s.value;
        emit();
    }

    // ---------------------------------------------------------------- photos

    public List<PhotoInfo> photos => data.photos ??= new List<PhotoInfo>();
    public int pinnedCount => photos.Count(p => p.pinned);

    /// <summary>Adds a new photo to the album, pinned to the board while it has room.</summary>
    public void addPhoto(string id, string caption)
    {
        photos.Add(new PhotoInfo { id = id, caption = caption, pinned = pinnedCount < Photos.BOARD_SLOTS });
        emit();
    }

    /// <summary>Pins a photo to the board or takes it down. False when the board is already full.</summary>
    public bool togglePin(string id)
    {
        var p = photos.FirstOrDefault(x => x.id == id);
        if (p == null) return false;
        if (!p.pinned && pinnedCount >= Photos.BOARD_SLOTS) return false;
        p.pinned = !p.pinned;
        emit();
        return true;
    }

    public void removePhoto(string id)
    {
        if (photos.RemoveAll(x => x.id == id) > 0) emit();
    }

    public int totalDefs => Items.ITEM_DEFS.Length;
}
