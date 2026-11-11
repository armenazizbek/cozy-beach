using System.Collections.Generic;
using System.Linq;

// Port of src/core/Journal.ts: the camp journal, a book of tiny stories. A few pages are there from the start (the camp, the dog,
// the beach) and the rest are written down automatically the first time the player does something.

public class JournalPage
{
    public string id, group, icon, title, text;
    /// <summary>on the first page of a new game</summary>
    public bool initial;
}

public static class Journal
{
    /// <summary>
    /// The book shows every page from the start, so it doubles as a guide to what there is to do on the beach. Pages the player has
    /// not earned yet are marked "To discover"; the rest are written in as they happen. Set this to false to hide undiscovered pages.
    /// </summary>
    public const bool BOOK_SHOW_ALL = true;

    public static readonly JournalPage[] JOURNAL_PAGES = new JournalPage[]
    {
        new JournalPage
        {
            id = "camp_home",
            group = "Camp",
            icon = "🏕️",
            title = "Home on Wheels",
            initial = true,
            text = "A small camper with a crooked chimney and a very good awning. It creaks when the wind comes up and drips a little when it rains, and you would not swap it for anything.",
        },
        new JournalPage
        {
            id = "camp_fire",
            group = "Camp",
            icon = "🔥",
            title = "The Fire",
            initial = true,
            text = "It has not gone out since the first evening. Everything on this beach drifts toward it sooner or later: crabs, gulls, and you, with cold hands around a warm mug.",
        },
        new JournalPage
        {
            id = "camp_radio",
            group = "Camp",
            icon = "📻",
            title = "The Radio",
            initial = true,
            text = "It only gets one station, but the music is always soft, and louder the closer you stand. You suspect the beach chooses the songs.",
        },
        new JournalPage
        {
            id = "camp_bench",
            group = "Camp",
            icon = "🔧",
            title = "The Workbench",
            initial = true,
            text = "This is where the sand comes off the finds: a brush, a lot of patience, and sometimes a small gasp when the dirt falls away and something shines.",
        },
        new JournalPage
        {
            id = "camp_post",
            group = "Camp",
            icon = "📬",
            title = "The Post",
            initial = true,
            text = "A leaning post with a little blue box on top, set up by somebody long ago. Nobody ever delivers to it, but when you return something precious, a thank-you tends to find its way there. When the red flag is up, there is mail.",
        },
        new JournalPage
        {
            id = "dog_intro",
            group = "Dog",
            icon = "🐕",
            title = "A Golden Visitor",
            initial = true,
            text = "Nobody remembers who the dog belongs to. It turned up one morning, lay down by the fire as though it had always been there, and has not left since. It answers to almost anything, especially the rustle of a bone.",
        },
        new JournalPage
        {
            id = "dog_bones",
            group = "Dog",
            icon = "🦴",
            title = "Bones for the Dog",
            initial = true,
            text = "A bone from the camper shop buys one trip. The dog sniffs along the sand, finds something buried, and barks until you come. The shop only has a few, so choose your moments.",
        },
        new JournalPage
        {
            id = "beach_intro",
            group = "Beach",
            icon = "🏝️",
            title = "The Beach",
            initial = true,
            text = "A long curve of pale sand between the dunes and the sea. Treasure hunters have come here for centuries, though most of them just wanted a quiet afternoon.",
        },
        new JournalPage
        {
            id = "beach_lost",
            group = "Beach",
            icon = "📜",
            title = "Lost and Found",
            initial = true,
            text = "The board by the camper fills with notes from people who lost something precious here. Every one of them is hoping that someone patient will come along with a brush and a detector.",
        },
        new JournalPage
        {
            id = "dig_first",
            group = "Finds",
            icon = "⛏️",
            title = "The First Hole",
            text = "The sand gives way slowly, then all at once. The best part of digging is the second before you know what is down there.",
        },
        new JournalPage
        {
            id = "find_trash",
            group = "Finds",
            icon = "♻️",
            title = "Beautiful Junk",
            text = "Bottle caps, rusty nails, a single flip-flop. The beach keeps everything people drop, and the bin by the camp turns it into something good for the shore.",
        },
        new JournalPage
        {
            id = "find_coin",
            group = "Finds",
            icon = "🪙",
            title = "Old Coins",
            text = "Worn faces of kings and captains nobody remembers. You turn one over in your fingers and wonder which pocket it fell from.",
        },
        new JournalPage
        {
            id = "find_jewel",
            group = "Finds",
            icon = "💍",
            title = "Something Shiny",
            text = "A ring that once meant something to somebody. You rinse it gently and set it where it cannot get lost again.",
        },
        new JournalPage
        {
            id = "find_shard",
            group = "Finds",
            icon = "🏺",
            title = "Pieces of Something",
            text = "Three curved fragments, and a feeling that they belong together. Some patient evening at the workbench they will become something you can hold up to the fire.",
        },
        new JournalPage
        {
            id = "quest_first",
            group = "Finds",
            icon = "📬",
            title = "A Thank-You",
            text = "Handing a lost thing back is the best kind of treasure hunting. You remember the look on their face much longer than you remember the reward.",
        },
        new JournalPage
        {
            id = "crab_shot",
            group = "Creatures",
            icon = "🦀",
            title = "Small Crab, Big Attitude",
            text = "A crack, a puff of sand, and the crab is gone. The beach goes quiet for a moment, then the gulls start up again. You feel faintly bad about it, but your pockets are a little heavier.",
        },
        new JournalPage
        {
            id = "bird_shot",
            group = "Creatures",
            icon = "🕊️",
            title = "A Flurry of Feathers",
            text = "White feathers drift down over the sand, and every other gull on the beach has something rude to say about it. They will remember this.",
        },
        new JournalPage
        {
            id = "shark_shot",
            group = "Creatures",
            icon = "🦈",
            title = "The Grey Fin",
            text = "It looked much bigger from the boat. Everyone knows the sea has teeth, and now you have the story to prove it.",
        },
        new JournalPage
        {
            id = "fish_caught",
            group = "Creatures",
            icon = "🐟",
            title = "Silver in the Water",
            text = "A flash of silver, a splash, and the smell of the sea on your hands. Not a bad catch for someone who came here to dig.",
        },
        new JournalPage
        {
            id = "coconut",
            group = "Creatures",
            icon = "🥥",
            title = "Coconut Season",
            text = "The palms hold their nuts high and drop them whenever they like. A good shot brings one down, and the sand takes the thump.",
        },
        new JournalPage
        {
            id = "cart_drive",
            group = "Journeys",
            icon = "🛺",
            title = "The Golf Cart",
            text = "A teal golf cart with a flag on the roof. It hums along the sand and leaves tyre tracks that the wind smooths away by noon.",
        },
        new JournalPage
        {
            id = "boat_drive",
            group = "Journeys",
            icon = "🚤",
            title = "On the Water",
            text = "Spray on your face and the whole beach shrinking behind you. Out here the horizon seems to belong to you for a while.",
        },
        new JournalPage
        {
            id = "chest_first",
            group = "Journeys",
            icon = "🧰",
            title = "Treasure Chest",
            text = "Hauled out of the waves, heavy with salt and old promises. Some of the sea's secrets are only kept for those who bring a boat.",
        },
        new JournalPage
        {
            id = "map_first",
            group = "Journeys",
            icon = "🗺️",
            title = "The Island Map",
            text = "Drawn by someone with a steady hand and clearly no idea how big the beach really is. You are the little red arrow.",
        },
        new JournalPage
        {
            id = "flag_first",
            group = "Journeys",
            icon = "🚩",
            title = "Planting a Flag",
            text = "A small red flag in the sand says \"I was here, and I will be back\". On the map it looks like a promise.",
        },
        new JournalPage
        {
            id = "binocs_first",
            group = "Journeys",
            icon = "🔭",
            title = "Far Away",
            text = "Through the binoculars the horizon sharpens: a boat, a fin, a gull working the wind. The world is bigger than the beach.",
        },
        new JournalPage
        {
            id = "pier_first",
            group = "Journeys",
            icon = "🌊",
            title = "The Pier",
            text = "The boards creak and the water slaps the posts underneath. Out at the end there is a bench, a bucket and nothing at all that needs doing.",
        },
        new JournalPage
        {
            id = "fish_first",
            group = "Creatures",
            icon = "🎣",
            title = "First Catch",
            text = "The float sat still for a long while. Then it was simply gone, and the rod was alive in your hands. Into the bucket it went, flapping.",
        },
        new JournalPage
        {
            id = "net_first",
            group = "Creatures",
            icon = "🐠",
            title = "The Hand Net",
            text = "A slow sweep through the water and something small is looking back at you through the mesh, more puzzled than afraid.",
        },
        new JournalPage
        {
            id = "aquarium_first",
            group = "Camp",
            icon = "🐟",
            title = "The Aquarium",
            text = "The tank at camp has its first lodger. It has already found the best corner and is behaving as if it has always lived there.",
        },
        new JournalPage
        {
            id = "photo_first",
            group = "Journeys",
            icon = "📷",
            title = "Say Cheese",
            text = "Click, and a moment stays put. The light, the sand, whatever the dog was doing. It is pinned on the board by the fire now, curling a little at the corners already.",
        },
        new JournalPage
        {
            id = "bow_first",
            group = "Journeys",
            icon = "🏹",
            title = "String and Arrow",
            text = "A quiet sort of weapon. You hold your breath at the top of the draw, and let go.",
        },
        new JournalPage
        {
            id = "gun_first",
            group = "Journeys",
            icon = "🔫",
            title = "A Toy Pistol",
            text = "More noise than danger, and more fun than either. The crabs disagree.",
        },
        new JournalPage
        {
            id = "dog_bone",
            group = "Dog",
            icon = "🐾",
            title = "Off on a Trip",
            text = "The moment it swallows a bone the dog changes: ears up, nose down, gone in a blur. All you can do is wait for the barking.",
        },
        new JournalPage
        {
            id = "dog_found",
            group = "Dog",
            icon = "📍",
            title = "The Barking Trail",
            text = "A winding line of small paw prints leads across the sand, and at the end of it is a dog who is very, very sure you should dig here.",
        },
        new JournalPage
        {
            id = "dog_lost",
            group = "Dog",
            icon = "✨",
            title = "A Special Scent",
            text = "Once in a long while the dog's nose finds something the whole beach has been missing. You do not know how it knows, but you have stopped questioning it.",
        },
        new JournalPage
        {
            id = "dog_pet",
            group = "Dog",
            icon = "💛",
            title = "Good Dog",
            text = "It leans into your hand, shakes its head, and wags until the whole back half of it wiggles. There is no better payment.",
        },
        new JournalPage
        {
            id = "hammock_first",
            group = "Camp",
            icon = "🛏️",
            title = "Between Two Posts",
            text = "A striped hammock sways between two posts, even when there is no wind. Lie back, watch the palm leaves against the sky, and the whole day slips by. You wake with warm hands and the fire still glowing.",
        },
        new JournalPage
        {
            id = "castle_first",
            group = "Journeys",
            icon = "🏰",
            title = "A Castle of Sand",
            text = "Pack the bucket, flip it over, tap it three times. Somewhere between the first mound and the last tiny tower, you stop being a treasure hunter and are just a kid on a beach.",
        },
        new JournalPage
        {
            id = "castle_grand",
            group = "Journeys",
            icon = "🚩",
            title = "A Proper Castle",
            text = "Four towers, a wall with battlements, a dark little gate and a flag on top. It will not last, but nobody ever built a sandcastle to keep.",
        },
        new JournalPage
        {
            id = "castle_shot",
            group = "Journeys",
            icon = "💥",
            title = "Target Practice",
            text = "A sandcastle takes a hit like a sandcastle: a puff, a tumbling tower, and then the long quiet slump. It is the most forgiving target on the beach, and it never holds a grudge.",
        },
        new JournalPage
        {
            id = "castle_washed",
            group = "Sky",
            icon = "🌊",
            title = "Washed Away",
            text = "The rain softens the walls and the shore takes the rest. A sandcastle is a promise to the tide, and the tide always collects. Build another one tomorrow.",
        },
        new JournalPage
        {
            id = "island_first",
            group = "Journeys",
            icon = "🏝️",
            title = "The Far Island",
            text = "A green hump of land far out past the whitecaps, reachable only by boat. From the camp it is a smudge on the horizon; from its shore it is a whole other place, with its own palms, its own quiet and its own secrets.",
        },
        new JournalPage
        {
            id = "island_ruins",
            group = "Journeys",
            icon = "🗿",
            title = "Stones That Remember",
            text = "On the summit stands a ring of old pillars, two of them still joined by a stone beam. Nobody knows who raised them. The grass grows long around the altar, and something small has been left at its foot.",
        },
        new JournalPage
        {
            id = "island_find",
            group = "Finds",
            icon = "🐚",
            title = "Treasure in the Open",
            text = "Nothing on the island is buried. The treasures simply lie where they fell: in the grass, under the cliff, along the tide line. A pearl, a shell, an idol. The sea keeps most of its secrets, but not all of them.",
        },
        new JournalPage
        {
            id = "lagoon_first",
            group = "Journeys",
            icon = "🏝️",
            title = "The Ring of Sand",
            text = "Far to the east the sea turns pale, and there it is: a ring of white sand and palms with still, green water in the middle. On the side that faces home the ring is broken, and a boat can slip through the gap and float in the lagoon as if in a cupped hand.",
        },
        new JournalPage
        {
            id = "lagoon_camp",
            group = "Journeys",
            icon = "🏝️",
            title = "Somebody Lived Here",
            text = "A lean-to of driftwood and palm leaves on the highest dune. A bedroll, a sea chest, a ring of stones round a fire that went out long ago. And a tall mast with a red rag tied to it, put up by someone who looked at the horizon every single day.",
        },
        new JournalPage
        {
            id = "ember_first",
            group = "Journeys",
            icon = "🏝️",
            title = "The Black Rock",
            text = "Out to the west a dark cone stands up out of the sea. Its sand is black and its stones are black, and at its foot the rock has cooled into tall six-sided pillars, packed together like the pipes of an organ. Nothing here is soft, and it is beautiful anyway.",
        },
        new JournalPage
        {
            id = "ember_spring",
            group = "Journeys",
            icon = "🗿",
            title = "The Warm Heart",
            text = "The top of the cone is hollow, and in the hollow lies a pool the colour of milk and turquoise, steaming gently into the wind. The mountain is asleep, not gone. On the rim someone long ago piled flat stones into a little tower, to say: I was here, and I climbed it.",
        },
        new JournalPage
        {
            id = "map_taken",
            group = "Journeys",
            icon = "📜",
            title = "An Old Map",
            text = "It lay on the altar as if someone had put it down a moment ago: brown paper, a coastline you almost recognise, and a handful of small red crosses. Press T on the map screen to read it.",
        },
        new JournalPage
        {
            id = "x_marks",
            group = "Finds",
            icon = "❌",
            title = "X Marks the Spot",
            text = "Dig where the cross is drawn and the spade rings on something hard. A chest, still locked, after all these years. Somebody trusted the sand with it and a hand-drawn map.",
        },
        new JournalPage
        {
            id = "camp_decor",
            group = "Camp",
            icon = "🏮",
            title = "Making It Home",
            text = "A striped rug by the fire, a painted sign by the path and a strand of little lights for the evenings. The camp has started to look like somebody lives here.",
        },
        new JournalPage
        {
            id = "sea_creature",
            group = "Creatures",
            icon = "🐙",
            title = "Strange Neighbours",
            text = "Octopuses sulking among the rocks, seahorses hanging on to nothing, sea snakes drawing S shapes in the blue, crabs marching sideways. Turtles drift through it all, and nobody wants to spear a turtle.",
        },
        new JournalPage
        {
            id = "bottle_map",
            group = "Journeys",
            icon = "🍾",
            title = "A Message from the Deep",
            text = "A green glass bottle, half buried in the sand far below the surface, and a map rolled up inside it. It shows a spot on your own beach that someone wanted kept secret.",
        },
        new JournalPage
        {
            id = "captain_hatch",
            group = "Finds",
            icon = "🚪",
            title = "A Door Under the Sand",
            text = "Where the bottle’s map pointed, the shovel struck wood: a trapdoor with an iron ring, buried flat in the dune. Under it a ladder drops into the dark, and a cold draught that smells of old stone comes up.",
        },
        new JournalPage
        {
            id = "great_door",
            group = "Journeys",
            icon = "🚪",
            title = "The Great Door",
            text = "At the far end of the room stands a door of black wood and iron, with a ring of pale blue light over it and a round, flat lock. It does not open. Not yet.",
        },
        new JournalPage
        {
            id = "door_open",
            group = "Journeys",
            icon = "☀️",
            title = "Noon at the Door",
            text = "The medallion fitted the lock and the astrolabe caught the noon sun. The great door swung back on a stair going down, and cold sea air came up it.",
        },
        new JournalPage
        {
            id = "hoard_found",
            group = "Journeys",
            icon = "💰",
            title = "The Marigold’s Gold",
            text = "A cave open to the sea, the bones of something enormous, and all the gold the town ever lost, heaped where the tide left it. The old church bell was in there too.",
        },
        new JournalPage
        {
            id = "captain_journal",
            group = "Journeys",
            icon = "📜",
            title = "The Captain’s Journal",
            text = "A shipwrecked captain’s diary, left open on the stone ring: the town’s gold lost with the Marigold, a room under the dunes, a message sealed in a bottle, and a great door the captain never opened.",
        },
        new JournalPage
        {
            id = "underground_first",
            group = "Journeys",
            icon = "🔥",
            title = "Under the Dunes",
            text = "A stone room, older than the camp, lit by torches nobody remembers lighting. Pillars, barrels, a table with a lantern still warm. At the far end the stairs climb back up to a square of sky.",
        },
        new JournalPage
        {
            id = "dive_first",
            group = "Journeys",
            icon = "🤿",
            title = "Under the Surface",
            text = "One breath, one step, and the beach is gone. Down here the light comes in long gold ribbons and the whole world is quiet except for your own bubbles.",
        },
        new JournalPage
        {
            id = "wreck_found",
            group = "Journeys",
            icon = "⚓",
            title = "The Sunken Ship",
            text = "Her ribs stick up out of the sand like a sleeping whale. Weeds have taken the deck, a crab has taken the cabin, and something in the hold is still shining.",
        },
        new JournalPage
        {
            id = "harpoon_first",
            group = "Creatures",
            icon = "🔱",
            title = "A Silver Flash",
            text = "A quick shot, a streak of bubbles, and a startled fish is yours. The rest of the school scatters like thrown coins.",
        },
        new JournalPage
        {
            id = "dive_find",
            group = "Finds",
            icon = "⭐",
            title = "Treasure on the Sea Floor",
            text = "Starfish, a painted plate, a pearl hiding in its oyster, and bars of gold lying where the ship let go of them. Nobody has touched them for two hundred years.",
        },
        new JournalPage
        {
            id = "radio_change",
            group = "Camp",
            icon = "📻",
            title = "A Dial That Turns",
            text = "Turn the knob and the static gives way to a different tune. Six stations in all, from a sleepy sunrise to a campfire strum, and not one of them is in a hurry.",
        },
        new JournalPage
        {
            id = "mail_first",
            group = "Finds",
            icon = "✉️",
            title = "Your First Letter",
            text = "It arrives a little after the good deed, addressed in careful handwriting to \"the kind person at the camp\". Reading it by the fire is better than any reward.",
        },
        new JournalPage
        {
            id = "radio_on",
            group = "Camp",
            icon = "🎵",
            title = "Music by the Fire",
            text = "A slow tune drifts across the sand and, for a moment, the camp feels exactly like a home.",
        },
        new JournalPage
        {
            id = "coffee_first",
            group = "Camp",
            icon = "☕",
            title = "Warm Again",
            text = "The thermos is always hot, somehow. One sip and your fingers remember how to dig.",
        },
        new JournalPage
        {
            id = "rain_first",
            group = "Sky",
            icon = "🌧️",
            title = "Rare Rain",
            text = "The sky turns grey and the sea turns silver. Rain on the awning, wet sand underfoot, and the whole beach smells brand new.",
        },
        new JournalPage
        {
            id = "night_first",
            group = "Sky",
            icon = "🌙",
            title = "The First Night",
            text = "The fire is the only warm thing for miles. Stars come out one by one, and the sea sounds close and enormous.",
        },
    };

    public static readonly string[] INITIAL_PAGE_IDS = JOURNAL_PAGES.Where(p => p.initial).Select(p => p.id).ToArray();
    public static readonly Dictionary<string, JournalPage> PAGE_BY_ID = JOURNAL_PAGES.ToDictionary(p => p.id);
}
