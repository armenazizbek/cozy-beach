/**
 * The camp journal: a book of tiny stories. A few pages are there from the start (the camp, the dog, the beach) and the rest are
 * written down automatically the first time the player does something, like shooting a crab, driving the boat or finding a ring.
 */

/**
 * The book shows every page from the start, so it doubles as a guide to what there is to do on the beach. Pages the player has
 * not earned yet are marked "To discover"; the rest are written in as they happen. Set this to false to hide undiscovered pages.
 */
export const BOOK_SHOW_ALL = true;

export interface JournalPage {
  id: string;
  group: string;
  icon: string;
  title: string;
  text: string;
  /** on the first page of a new game */
  initial?: boolean;
}

export const JOURNAL_PAGES: JournalPage[] = [
  // ---- there from the start
  {
    id: 'camp_home', group: 'Camp', icon: '🏕️', title: 'Home on Wheels', initial: true,
    text: 'A small camper with a crooked chimney and a very good awning. It creaks when the wind comes up and drips a little when it rains, and you would not swap it for anything.',
  },
  {
    id: 'camp_fire', group: 'Camp', icon: '🔥', title: 'The Fire', initial: true,
    text: 'It has not gone out since the first evening. Everything on this beach drifts toward it sooner or later: crabs, gulls, and you, with cold hands around a warm mug.',
  },
  {
    id: 'camp_radio', group: 'Camp', icon: '📻', title: 'The Radio', initial: true,
    text: 'It only gets one station, but the music is always soft, and louder the closer you stand. You suspect the beach chooses the songs.',
  },
  {
    id: 'camp_bench', group: 'Camp', icon: '🔧', title: 'The Workbench', initial: true,
    text: 'This is where the sand comes off the finds: a brush, a lot of patience, and sometimes a small gasp when the dirt falls away and something shines.',
  },
  {
    id: 'camp_post', group: 'Camp', icon: '📬', title: 'The Post', initial: true,
    text: 'A leaning post with a little blue box on top, set up by somebody long ago. Nobody ever delivers to it, but when you return something precious, a thank-you tends to find its way there. When the red flag is up, there is mail.',
  },
  {
    id: 'dog_intro', group: 'Dog', icon: '🐕', title: 'A Golden Visitor', initial: true,
    text: 'Nobody remembers who the dog belongs to. It turned up one morning, lay down by the fire as though it had always been there, and has not left since. It answers to almost anything, especially the rustle of a bone.',
  },
  {
    id: 'dog_bones', group: 'Dog', icon: '🦴', title: 'Bones for the Dog', initial: true,
    text: 'A bone from the camper shop buys one trip. The dog sniffs along the sand, finds something buried, and barks until you come. The shop only has a few, so choose your moments.',
  },
  {
    id: 'beach_intro', group: 'Beach', icon: '🏝️', title: 'The Beach', initial: true,
    text: 'A long curve of pale sand between the dunes and the sea. Treasure hunters have come here for centuries, though most of them just wanted a quiet afternoon.',
  },
  {
    id: 'beach_lost', group: 'Beach', icon: '📜', title: 'Lost and Found', initial: true,
    text: 'The board by the camper fills with notes from people who lost something precious here. Every one of them is hoping that someone patient will come along with a brush and a detector.',
  },

  // ---- written down as you go
  {
    id: 'dig_first', group: 'Finds', icon: '⛏️', title: 'The First Hole',
    text: 'The sand gives way slowly, then all at once. The best part of digging is the second before you know what is down there.',
  },
  {
    id: 'find_trash', group: 'Finds', icon: '♻️', title: 'Beautiful Junk',
    text: 'Bottle caps, rusty nails, a single flip-flop. The beach keeps everything people drop, and the bin by the camp turns it into something good for the shore.',
  },
  {
    id: 'find_coin', group: 'Finds', icon: '🪙', title: 'Old Coins',
    text: 'Worn faces of kings and captains nobody remembers. You turn one over in your fingers and wonder which pocket it fell from.',
  },
  {
    id: 'find_jewel', group: 'Finds', icon: '💍', title: 'Something Shiny',
    text: 'A ring that once meant something to somebody. You rinse it gently and set it where it cannot get lost again.',
  },
  {
    id: 'find_shard', group: 'Finds', icon: '🏺', title: 'Pieces of Something',
    text: 'Three curved fragments, and a feeling that they belong together. Some patient evening at the workbench they will become something you can hold up to the fire.',
  },
  {
    id: 'quest_first', group: 'Finds', icon: '📬', title: 'A Thank-You',
    text: 'Handing a lost thing back is the best kind of treasure hunting. You remember the look on their face much longer than you remember the reward.',
  },
  {
    id: 'crab_shot', group: 'Creatures', icon: '🦀', title: 'Small Crab, Big Attitude',
    text: 'A crack, a puff of sand, and the crab is gone. The beach goes quiet for a moment, then the gulls start up again. You feel faintly bad about it, but your pockets are a little heavier.',
  },
  {
    id: 'bird_shot', group: 'Creatures', icon: '🕊️', title: 'A Flurry of Feathers',
    text: 'White feathers drift down over the sand, and every other gull on the beach has something rude to say about it. They will remember this.',
  },
  {
    id: 'shark_shot', group: 'Creatures', icon: '🦈', title: 'The Grey Fin',
    text: 'It looked much bigger from the boat. Everyone knows the sea has teeth, and now you have the story to prove it.',
  },
  {
    id: 'fish_caught', group: 'Creatures', icon: '🐟', title: 'Silver in the Water',
    text: 'A flash of silver, a splash, and the smell of the sea on your hands. Not a bad catch for someone who came here to dig.',
  },
  {
    id: 'coconut', group: 'Creatures', icon: '🥥', title: 'Coconut Season',
    text: 'The palms hold their nuts high and drop them whenever they like. A good shot brings one down, and the sand takes the thump.',
  },
  {
    id: 'cart_drive', group: 'Journeys', icon: '🛺', title: 'The Golf Cart',
    text: 'A teal golf cart with a flag on the roof. It hums along the sand and leaves tyre tracks that the wind smooths away by noon.',
  },
  {
    id: 'boat_drive', group: 'Journeys', icon: '🚤', title: 'On the Water',
    text: 'Spray on your face and the whole beach shrinking behind you. Out here the horizon seems to belong to you for a while.',
  },
  {
    id: 'chest_first', group: 'Journeys', icon: '🧰', title: 'Treasure Chest',
    text: 'Hauled out of the waves, heavy with salt and old promises. Some of the sea\'s secrets are only kept for those who bring a boat.',
  },
  {
    id: 'map_first', group: 'Journeys', icon: '🗺️', title: 'The Island Map',
    text: 'Drawn by someone with a steady hand and clearly no idea how big the beach really is. You are the little red arrow.',
  },
  {
    id: 'flag_first', group: 'Journeys', icon: '🚩', title: 'Planting a Flag',
    text: 'A small red flag in the sand says "I was here, and I will be back". On the map it looks like a promise.',
  },
  {
    id: 'binocs_first', group: 'Journeys', icon: '🔭', title: 'Far Away',
    text: 'Through the binoculars the horizon sharpens: a boat, a fin, a gull working the wind. The world is bigger than the beach.',
  },
  {
    id: 'bow_first', group: 'Journeys', icon: '🏹', title: 'String and Arrow',
    text: 'A quiet sort of weapon. You hold your breath at the top of the draw, and let go.',
  },
  {
    id: 'gun_first', group: 'Journeys', icon: '🔫', title: 'A Toy Pistol',
    text: 'More noise than danger, and more fun than either. The crabs disagree.',
  },
  {
    id: 'dog_bone', group: 'Dog', icon: '🐾', title: 'Off on a Trip',
    text: 'The moment it swallows a bone the dog changes: ears up, nose down, gone in a blur. All you can do is wait for the barking.',
  },
  {
    id: 'dog_found', group: 'Dog', icon: '📍', title: 'The Barking Trail',
    text: 'A winding line of small paw prints leads across the sand, and at the end of it is a dog who is very, very sure you should dig here.',
  },
  {
    id: 'dog_lost', group: 'Dog', icon: '✨', title: 'A Special Scent',
    text: 'Once in a long while the dog\'s nose finds something the whole beach has been missing. You do not know how it knows, but you have stopped questioning it.',
  },
  {
    id: 'dog_pet', group: 'Dog', icon: '💛', title: 'Good Dog',
    text: 'It leans into your hand, shakes its head, and wags until the whole back half of it wiggles. There is no better payment.',
  },
  {
    id: 'hammock_first', group: 'Camp', icon: '🛏️', title: 'Between Two Posts',
    text: 'A striped hammock sways between two posts, even when there is no wind. Lie back, watch the palm leaves against the sky, and the whole day slips by. You wake with warm hands and the fire still glowing.',
  },
  {
    id: 'castle_first', group: 'Journeys', icon: '🏰', title: 'A Castle of Sand',
    text: 'Pack the bucket, flip it over, tap it three times. Somewhere between the first mound and the last tiny tower, you stop being a treasure hunter and are just a kid on a beach.',
  },
  {
    id: 'castle_grand', group: 'Journeys', icon: '🚩', title: 'A Proper Castle',
    text: 'Four towers, a wall with battlements, a dark little gate and a flag on top. It will not last, but nobody ever built a sandcastle to keep.',
  },
  {
    id: 'castle_shot', group: 'Journeys', icon: '💥', title: 'Target Practice',
    text: 'A sandcastle takes a hit like a sandcastle: a puff, a tumbling tower, and then the long quiet slump. It is the most forgiving target on the beach, and it never holds a grudge.',
  },
  {
    id: 'castle_washed', group: 'Sky', icon: '🌊', title: 'Washed Away',
    text: 'The rain softens the walls and the shore takes the rest. A sandcastle is a promise to the tide, and the tide always collects. Build another one tomorrow.',
  },
  {
    id: 'island_first', group: 'Journeys', icon: '🏝️', title: 'The Far Island',
    text: 'A green hump of land far out past the whitecaps, reachable only by boat. From the camp it is a smudge on the horizon; from its shore it is a whole other place, with its own palms, its own quiet and its own secrets.',
  },
  {
    id: 'island_ruins', group: 'Journeys', icon: '🗿', title: 'Stones That Remember',
    text: 'On the summit stands a ring of old pillars, two of them still joined by a stone beam. Nobody knows who raised them. The grass grows long around the altar, and something small has been left at its foot.',
  },
  {
    id: 'island_find', group: 'Finds', icon: '🐚', title: 'Treasure in the Open',
    text: 'Nothing on the island is buried. The treasures simply lie where they fell: in the grass, under the cliff, along the tide line. A pearl, a shell, an idol. The sea keeps most of its secrets, but not all of them.',
  },
  {
    id: 'map_taken', group: 'Journeys', icon: '📜', title: 'An Old Map',
    text: 'It lay on the altar as if someone had put it down a moment ago: brown paper, a coastline you almost recognise, and a handful of small red crosses. Press T on the map screen to read it.',
  },
  {
    id: 'x_marks', group: 'Finds', icon: '❌', title: 'X Marks the Spot',
    text: 'Dig where the cross is drawn and the spade rings on something hard. A chest, still locked, after all these years. Somebody trusted the sand with it and a hand-drawn map.',
  },
  {
    id: 'camp_decor', group: 'Camp', icon: '🏮', title: 'Making It Home',
    text: 'A striped rug by the fire, a painted sign by the path and a strand of little lights for the evenings. The camp has started to look like somebody lives here.',
  },
  {
    id: 'sea_creature', group: 'Creatures', icon: '🐙', title: 'Strange Neighbours',
    text: 'Octopuses sulking among the rocks, seahorses hanging on to nothing, sea snakes drawing S shapes in the blue, crabs marching sideways. Turtles drift through it all, and nobody wants to spear a turtle.',
  },
  {
    id: 'bottle_map', group: 'Journeys', icon: '🍾', title: 'A Message from the Deep',
    text: 'A green glass bottle, half buried in the sand far below the surface, and a map rolled up inside it. It shows a spot on your own beach that someone wanted kept secret.',
  },
  {
    id: 'captain_hatch', group: 'Finds', icon: '🚪', title: 'A Door Under the Sand',
    text: 'Where the bottle\u2019s map pointed, the shovel struck wood: a trapdoor with an iron ring, buried flat in the dune. Under it a ladder drops into the dark, and a cold draught that smells of old stone comes up.',
  },
  {
    id: 'dive_first', group: 'Journeys', icon: '🤿', title: 'Under the Surface',
    text: 'One breath, one step, and the beach is gone. Down here the light comes in long gold ribbons and the whole world is quiet except for your own bubbles.',
  },
  {
    id: 'wreck_found', group: 'Journeys', icon: '⚓', title: 'The Sunken Ship',
    text: 'Her ribs stick up out of the sand like a sleeping whale. Weeds have taken the deck, a crab has taken the cabin, and something in the hold is still shining.',
  },
  {
    id: 'harpoon_first', group: 'Creatures', icon: '🔱', title: 'A Silver Flash',
    text: 'A quick shot, a streak of bubbles, and a startled fish is yours. The rest of the school scatters like thrown coins.',
  },
  {
    id: 'dive_find', group: 'Finds', icon: '⭐', title: 'Treasure on the Sea Floor',
    text: 'Starfish, a painted plate, a pearl hiding in its oyster, and bars of gold lying where the ship let go of them. Nobody has touched them for two hundred years.',
  },
  {
    id: 'radio_change', group: 'Camp', icon: '📻', title: 'A Dial That Turns',
    text: 'Turn the knob and the static gives way to a different tune. Six stations in all, from a sleepy sunrise to a campfire strum, and not one of them is in a hurry.',
  },
  {
    id: 'mail_first', group: 'Finds', icon: '✉️', title: 'Your First Letter',
    text: 'It arrives a little after the good deed, addressed in careful handwriting to "the kind person at the camp". Reading it by the fire is better than any reward.',
  },
  {
    id: 'radio_on', group: 'Camp', icon: '🎵', title: 'Music by the Fire',
    text: 'A slow tune drifts across the sand and, for a moment, the camp feels exactly like a home.',
  },
  {
    id: 'coffee_first', group: 'Camp', icon: '☕', title: 'Warm Again',
    text: 'The thermos is always hot, somehow. One sip and your fingers remember how to dig.',
  },
  {
    id: 'rain_first', group: 'Sky', icon: '🌧️', title: 'Rare Rain',
    text: 'The sky turns grey and the sea turns silver. Rain on the awning, wet sand underfoot, and the whole beach smells brand new.',
  },
  {
    id: 'night_first', group: 'Sky', icon: '🌙', title: 'The First Night',
    text: 'The fire is the only warm thing for miles. Stars come out one by one, and the sea sounds close and enormous.',
  },
];

export const INITIAL_PAGE_IDS: string[] = JOURNAL_PAGES.filter((p) => p.initial).map((p) => p.id);
export const PAGE_BY_ID = new Map<string, JournalPage>(JOURNAL_PAGES.map((p) => [p.id, p]));
