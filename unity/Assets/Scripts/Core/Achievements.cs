using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// Port of src/core/Achievements.ts

public class AchievementDef
{
    /// <summary>also the name to create the achievement under in Steamworks</summary>
    public string id;
    public string name, desc, icon;
    /// <summary>shown as "?" until it is earned</summary>
    public bool secret;
    /// <summary>the journal page whose discovery earns it, when it comes from something that happens once</summary>
    public string page;
}

/// <summary>The player's achievements. They belong to the player, not to one saved game, so they have their own file.</summary>
public class Achievements
{
    public static readonly AchievementDef[] ACHIEVEMENTS = new AchievementDef[]
    {
        new AchievementDef { id = "FIRST_DIG", name = "First Dig", desc = "Dig your first hole in the sand.", icon = "⛏️", page = "dig_first" },
        new AchievementDef { id = "FINDS_10", name = "Beachcomber", desc = "Find 10 things.", icon = "🐚" },
        new AchievementDef { id = "FINDS_50", name = "Treasure Hunter", desc = "Find 50 things.", icon = "🧭" },
        new AchievementDef { id = "FINDS_150", name = "Pack Rat", desc = "Find 150 things.", icon = "🎒" },
        new AchievementDef { id = "QUEST_FIRST", name = "Finders, Returners", desc = "Return a lost item to its owner.", icon = "💌" },
        new AchievementDef { id = "QUEST_ALL", name = "Everything Back Home", desc = "Return every lost item on the board.", icon = "🏡" },
        new AchievementDef { id = "ASSEMBLED", name = "Restorer", desc = "Piece both antiques back together.", icon = "🏺" },
        new AchievementDef { id = "RICH", name = "Nest Egg", desc = "Hold 2000 coins at once.", icon = "💰" },
        new AchievementDef
        {
            id = "CHEST_BURIED",
            name = "X Marks the Spot",
            desc = "Dig up a sealed chest from the treasure map.",
            icon = "🗺️",
            page = "x_marks",
        },
        new AchievementDef
        {
            id = "CHEST_SEA",
            name = "Chest at Sea",
            desc = "Haul in a treasure chest from the water.",
            icon = "⚓",
            page = "chest_first",
        },
        new AchievementDef { id = "MAIL", name = "You’ve Got Mail", desc = "Receive a letter at the camp post.", icon = "📬", page = "mail_first" },
        new AchievementDef { id = "HAMMOCK", name = "Naptime", desc = "Sleep in the hammock until morning.", icon = "🛏️", page = "hammock_first" },
        new AchievementDef { id = "RADIO", name = "Tune In", desc = "Turn on the radio.", icon = "📻", page = "radio_on" },
        new AchievementDef { id = "DECOR", name = "Home Sweet Camp", desc = "Buy every camp decoration.", icon = "🏮" },
        new AchievementDef { id = "WEEK", name = "A Week by the Sea", desc = "Reach day 7.", icon = "📅" },
        new AchievementDef { id = "NIGHT", name = "Night Owl", desc = "Be out on the beach after dark.", icon = "🌙", page = "night_first" },
        new AchievementDef { id = "RAIN", name = "Rainy Day", desc = "Be caught out in the rain.", icon = "🌧️", page = "rain_first" },
        new AchievementDef { id = "CASTLE", name = "Sandy Builder", desc = "Build a sandcastle.", icon = "🏰", page = "castle_first" },
        new AchievementDef { id = "DOG_PET", name = "Good Dog", desc = "Give the dog some love.", icon = "🐕", page = "dog_pet" },
        new AchievementDef { id = "DOG_FIND", name = "Fetch!", desc = "Let the dog sniff out a find.", icon = "🦴", page = "dog_found" },
        new AchievementDef { id = "BOAT", name = "Captain", desc = "Buy the boat key.", icon = "🚤" },
        new AchievementDef { id = "ISLAND", name = "Landfall", desc = "Set foot on the island.", icon = "🏝️", page = "island_first" },
        new AchievementDef { id = "RUINS", name = "Old Stones", desc = "Find the ruins on the island.", icon = "🗿", page = "island_ruins" },
        new AchievementDef { id = "MAP", name = "An Old Map", desc = "Take the map from the island’s altar.", icon = "📜", page = "map_taken" },
        new AchievementDef
        {
            id = "LIGHTHOUSE",
            name = "The Keeper’s View",
            desc = "Climb to the top of the lighthouse on the black rock.",
            icon = "💡",
            page = "ember_spring",
        },
        new AchievementDef { id = "DIVE", name = "Into the Blue", desc = "Dive for the first time.", icon = "🤿", page = "dive_first" },
        new AchievementDef { id = "WRECK", name = "The Sunken Ship", desc = "Find the shipwreck.", icon = "⚓", page = "wreck_found" },
        new AchievementDef { id = "HARPOON", name = "First Catch", desc = "Spear your first fish.", icon = "🐟", page = "harpoon_first" },
        new AchievementDef
        {
            id = "CREATURE",
            name = "Strange Neighbours",
            desc = "Spear an octopus, seahorse, sea snake or crab.",
            icon = "🐙",
            page = "sea_creature",
        },
        new AchievementDef
        {
            id = "BOTTLE",
            name = "Message from the Deep",
            desc = "Find the bottle with the map inside.",
            icon = "🍾",
            page = "bottle_map",
        },
        new AchievementDef { id = "HATCH", name = "A Door Under the Sand", desc = "Dig up the hidden hatch.", icon = "🚪", page = "captain_hatch" },
        new AchievementDef
        {
            id = "UNDERGROUND",
            name = "Under the Dunes",
            desc = "Climb down into the stone room.",
            icon = "🔥",
            page = "underground_first",
        },
        new AchievementDef
        {
            id = "JOURNAL",
            name = "The Captain’s Story",
            desc = "Read the captain’s journal.",
            icon = "📖",
            page = "captain_journal",
        },
        new AchievementDef { id = "DOOR", name = "Knock Knock", desc = "Try the great door.", icon = "✨", secret = true, page = "great_door" },
        new AchievementDef { id = "DOOR_OPEN", name = "High Noon", desc = "Open the great door.", icon = "☀️", secret = true, page = "door_open" },
        new AchievementDef
        {
            id = "HOARD",
            name = "The Marigold’s Gold",
            desc = "Find the lost gold.",
            icon = "💰",
            secret = true,
            page = "hoard_found",
        },
    };

    private const string KEY = "cozy-treasure-hunter-achievements-v1";

    private readonly HashSet<string> done = new HashSet<string>();
    private readonly Dictionary<string, string> byPage = ACHIEVEMENTS.Where(a => a.page != null).ToDictionary(a => a.page, a => a.id);
    /// <summary>called with an achievement the moment it is earned</summary>
    public System.Action<AchievementDef> onUnlock = _ => { };

    public Achievements()
    {
        try
        {
            var raw = Storage.getItem(KEY);
            if (raw != null) foreach (var id in JsonConvert.DeserializeObject<string[]>(raw)) done.Add(id);
        }
        catch
        {
            /* an unreadable file just starts again */
        }
        // anything earned before Steam was running is handed over now
        foreach (var id in done) SteamBridge.Unlock(id);
    }

    public bool has(string id) => done.Contains(id);

    public int count => done.Count;

    public void unlock(string id)
    {
        if (done.Contains(id)) return;
        var def = ACHIEVEMENTS.FirstOrDefault(a => a.id == id);
        if (def == null) return;
        done.Add(id);
        Storage.setItemLater(KEY, JsonConvert.SerializeObject(done.ToArray()));
        SteamBridge.Unlock(id);
        onUnlock(def);
    }

    /// <summary>Earns the achievement tied to a journal page, if there is one.</summary>
    public void page(string pageId)
    {
        if (byPage.TryGetValue(pageId, out var id)) unlock(id);
    }
}
