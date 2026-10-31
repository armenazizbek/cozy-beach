/** The text of the Help screen. Each topic is a tab; a block is a short paragraph, a list of points, or a list of key + meaning pairs. */
export interface HelpBlock {
  h?: string;
  p?: string;
  list?: string[];
  keys?: [string, string][];
}

export interface HelpTopic {
  id: string;
  icon: string;
  title: string;
  blocks: HelpBlock[];
}

export const HELP_TOPICS: HelpTopic[] = [
  {
    id: 'about',
    icon: '🏝️',
    title: 'The game',
    blocks: [
      {
        p: 'Cozy Treasure Hunter is a quiet first-person game about a long beach, a metal detector and a very good dog. There is no timer, no way to lose and nobody telling you what to do next. Walk, sweep, dig, clean what you find, and slowly make the camp yours.',
      },
      {
        h: 'What you do here',
        list: [
          'Sweep the sand with your detector, dig up whatever it finds and clean it by hand.',
          'Sell the valuable things, recycle the trash (the beach grows greener), and return lost items to the people who miss them.',
          'Spend your coins on upgrades, a golf cart, a boat, diving gear and things for your camp.',
          'Sail to a small island, dive to a shipwreck, and follow old maps to places that are not on the beach.',
          'Keep an eye on the journal: it fills in as you discover things, and it hints at what is left to find.',
        ],
      },
      { p: 'There is no right order. Do whatever sounds nice. The game saves itself, so you can stop whenever you like.' },
    ],
  },
  {
    id: 'controls',
    icon: '🎮',
    title: 'Controls',
    blocks: [
      {
        h: 'Keyboard and mouse',
        keys: [
          ['W A S D', 'Walk. Hold Shift to hurry.'],
          ['Mouse', 'Look around. Click the game to take control of the mouse.'],
          ['Click', 'Use the tool in your hands.'],
          ['Right-click', 'Let the bow string down, lower the binoculars, or knock down a sandcastle.'],
          ['F', 'Use what is next to you: pick up a find, open the camp things, drive the cart or boat, dive.'],
          ['E', 'A second action: change the radio song, pet the dog, step off the boat.'],
          ['1 and 2', 'Detector and trowel. Space swaps between them.'],
          ['5 6 7 8 4', 'Binoculars, bow, flags, bucket, toy pistol (once you own them). Scroll also changes tool.'],
          ['M', 'Open the map. T switches between the maps you have.'],
          ['N', 'Mute the sound.'],
          ['Esc', 'Pause: save, load, settings and this help.'],
        ],
      },
      {
        h: 'Controller',
        keys: [
          ['Left stick', 'Walk (press it in to hurry).'],
          ['Right stick', 'Look around.'],
          ['RT / LT', 'Use the tool / the right-click action.'],
          ['A', 'Use what is next to you (the same as F).'],
          ['X', 'The second action (the same as E).'],
          ['Y', 'Open the map. LB and RB switch maps while it is open.'],
          ['LB / RB', 'Change tool.'],
          ['D-pad', 'Up detector, right trowel, down binoculars, left the other tools.'],
          ['Start / Back', 'Pause / mute.'],
        ],
      },
      {
        h: 'Under the water',
        keys: [
          ['W A S D', 'Swim the way you are looking.'],
          ['Space / C', 'Rise / sink (on a controller: RB / LB).'],
          ['Click', 'Fire the harpoon. It is the only tool down there.'],
          ['F', 'Pick up a find you are close to.'],
          ['Q', 'Swim back up to the beach.'],
        ],
      },
    ],
  },
  {
    id: 'treasure',
    icon: '⛏️',
    title: 'Finding treasure',
    blocks: [
      {
        h: 'Sweep, dig, clean',
        list: [
          'Take out the detector (1) and sweep the sand in front of you. It beeps faster, and the screen lights up, the closer something is. Different tones mean different kinds of finds.',
          'Take out the trowel (2) and click to dig. Dig until the find appears, then pick it up with F or a click.',
          'Digging uses warmth (the bar at the top left). Warm up at the campfire or with the coffee on the stump, and the bar fills again.',
          'Cleaning: the find is encrusted with sand. Scrub it with the brush (hold click and move the mouse, or hold RT and use the right stick) until it shines.',
        ],
      },
      {
        h: 'What to do with finds',
        list: [
          'Coins and jewellery: sell them at the workbench.',
          'Trash: recycle it in the green bin at camp for a few coins. Every piece you recycle makes the beach greener.',
          'Lost items: they are matched to a request on the Lost & Found board, and are returned and paid for as soon as you clean them. The owners write to you afterwards.',
          'Antique shards: three shards make a whole antique. Put them together at the workbench for a big payout.',
          'Treasure maps and bottle maps lead to buried chests. Look for the red crosses on the map.',
        ],
      },
      { p: 'Tip: dig a little wider if a signal stays strong but nothing shows up. Holes you dig show on your map, so you do not dig the same place twice.' },
    ],
  },
  {
    id: 'camp',
    icon: '⛺',
    title: 'Your camp',
    blocks: [
      {
        h: 'What is at camp',
        list: [
          'The campfire and the coffee: rest, warm up, and let the day pass if you like.',
          'The workbench: sell coins and jewellery, and assemble antiques.',
          'The Lost & Found board: see what people are missing.',
          'The recycling bin: turn trash into coins.',
          'The camper: the shop. Buy upgrades for your detector, digging, cleaning and gear, plus a golf cart key, a boat key, a bow, a toy pistol, marker flags, a bucket and spade, diving gear, bones for the dog and decorations.',
          'The post: letters that arrive after you return lost things.',
          'The radio: once it is on, the music follows you everywhere, and the tune changes with the place (the beach by day, the camp at night, under the water, under the dunes). Use E to pick a fixed song instead.',
          'The hammock: sleep until morning.',
          'The lantern: switch it on or off.',
          'The journal: everything you have discovered.',
        ],
      },
      {
        h: 'The dog',
        p: 'A golden retriever lives at camp. Pet it with E. Buy bones in the shop (there are only a few) and give one with F, and now and then the dog will dig up something for you. It leaves paw prints on the sand and on your map.',
      },
      {
        h: 'Making it home',
        p: 'The shop sells decorations: a rug, a sign you can rewrite by clicking it, string lights, a cooler, potted palms, an umbrella, a surfboard rack and tiki torches. They are a good thing to spend coins on once you have the important things.',
      },
    ],
  },
  {
    id: 'explore',
    icon: '🧭',
    title: 'Beyond the beach',
    blocks: [
      {
        h: 'Getting around',
        list: [
          'The golf cart (needs a key) is quick on land. F gets in and out.',
          'The boat (needs a key) takes you out to sea. F takes the wheel, E steps off when you are close to shore.',
          'The map (M) shows where you are. Click it to set a destination and a glowing beam and a dotted line will lead you there.',
          'Marker flags (7) are planted on dry sand and show on both maps.',
        ],
      },
      {
        h: 'The island',
        p: 'Sail out to the small island. It has its own finds that lie in the open, some ruins, and an old altar with a map that shows where treasure is buried on your own beach.',
      },
      {
        h: 'Diving',
        list: [
          'Buy the diving gear at the camper. Stand at the water’s edge and press F to dive in. Press Q to come back up.',
          'Down there you can find starfish, plates, bells and gold. Press F near a find to clean it.',
          'The harpoon spears fish for a coin each. Turtles are friendly and cannot be hit.',
          'It gets deeper the further out you swim, and the water changes with the time of day.',
        ],
      },
      {
        h: 'Other tools',
        list: [
          'Binoculars (5): click to zoom. They show the distance to what you are looking at.',
          'Bucket and spade (8): build sandcastles. Click to add to one. The sea and the rain wash them away.',
          'Bow (6): hold click to draw, release to shoot. Toy pistol (4): click to shoot. Crabs, gulls, sharks and coconuts pay a few coins when hit.',
        ],
      },
      { p: 'There are secrets that the journal and the maps only hint at. If you find a strange door, give it some time.' },
    ],
  },
  {
    id: 'tips',
    icon: '💡',
    title: 'Tips and saving',
    blocks: [
      {
        h: 'Saving',
        list: [
          'The game saves itself about every minute and a half, and after you clean a find or leave the shop. That goes into the single "Autosave" slot.',
          'To keep a save of your own, open the pause menu (Esc) and use Save game. You can have as many as you like. Load them from the main menu or the pause menu.',
        ],
      },
      {
        h: 'Good habits',
        list: [
          'Check the journal now and then. Its pages are a gentle guide to what you have not tried.',
          'Walk the shore. Many finds are washed up near the water.',
          'Not enough coins? Sell what you can and go back to the sand. There is always more to find.',
          'Look at the achievements list (main menu or pause menu) for ideas of things to try.',
          'Turn Shadows off in Settings if the game runs slowly on your computer.',
        ],
      },
      { p: 'Take your time. The beach is not going anywhere.' },
    ],
  },
];
