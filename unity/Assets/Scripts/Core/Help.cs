// Port of src/core/Help.ts: the text of the Help screen. Each topic is a tab; a block is a short paragraph, a list of points, or a
// list of key + meaning pairs.

public class HelpBlock
{
    public string h, p;
    public string[] list;
    public string[][] keys;
}

public class HelpTopic
{
    public string id, icon, title;
    public HelpBlock[] blocks;
}

public static class Help
{
    public static readonly HelpTopic[] HELP_TOPICS = new HelpTopic[]
    {
        new HelpTopic
        {
            id = "about",
            icon = "🏝️",
            title = "The game",
            blocks = new HelpBlock[]
            {
                new HelpBlock
                {
                    p = "Cozy Treasure Hunter is a quiet first-person game about a long beach, a metal detector and a very good dog. There is no timer, no way to lose and nobody telling you what to do next. Walk, sweep, dig, clean what you find, and slowly make the camp yours.",
                },
                new HelpBlock
                {
                    h = "What you do here",
                    list = new string[] { "Sweep the sand with your detector, dig up whatever it finds and clean it by hand.", "Sell the valuable things, recycle the trash (the beach grows greener), and return lost items to the people who miss them.", "Spend your coins on upgrades, a golf cart, a boat, diving gear and things for your camp.", "Sail to a small island, dive to a shipwreck, and follow old maps to places that are not on the beach.", "Keep an eye on the journal: it fills in as you discover things, and it hints at what is left to find." },
                },
                new HelpBlock { p = "There is no right order. Do whatever sounds nice. The game saves itself, so you can stop whenever you like." },
            },
        },
        new HelpTopic
        {
            id = "controls",
            icon = "🎮",
            title = "Controls",
            blocks = new HelpBlock[]
            {
                new HelpBlock
                {
                    h = "Keyboard and mouse",
                    keys = new string[][]
                    {
                        new string[] { "W A S D", "Walk. Hold Shift to hurry." },
                        new string[] { "Mouse", "Look around. Click the game to take control of the mouse." },
                        new string[] { "Click", "Use the tool in your hands." },
                        new string[] { "Right-click", "Let the bow string down, lower the binoculars or the camera, or knock down a sandcastle." },
                        new string[] { "F", "Use what is next to you: pick up a find, open the camp things, drive the cart or boat, dive." },
                        new string[] { "E", "A second action: change the radio song, pet the dog, step off the boat." },
                        new string[] { "1 and 2", "Detector and trowel. Space swaps between them." },
                        new string[] { "3", "Camera. Click to raise it, click again to take a photo." },
                        new string[] { "5 6 7 8 4", "Binoculars, bow, flags, bucket, toy pistol (once you own them). Scroll also changes tool." },
                        new string[] { "M", "Open the map. T switches between the maps you have." },
                        new string[] { "N", "Mute the sound." },
                        new string[] { "Esc", "Pause: save, load, settings and this help." },
                    },
                },
                new HelpBlock
                {
                    h = "Controller",
                    keys = new string[][]
                    {
                        new string[] { "Left stick", "Walk (press it in to hurry)." },
                        new string[] { "Right stick", "Look around." },
                        new string[] { "RT / LT", "Use the tool / the right-click action." },
                        new string[] { "A", "Use what is next to you (the same as F)." },
                        new string[] { "X", "The second action (the same as E)." },
                        new string[] { "Y", "Open the map. LB and RB switch maps while it is open." },
                        new string[] { "LB / RB", "Change tool." },
                        new string[] { "D-pad", "Up detector, right trowel, down binoculars, left the camera and the other tools." },
                        new string[] { "Start / Back", "Pause / mute." },
                    },
                },
                new HelpBlock
                {
                    h = "Under the water",
                    keys = new string[][]
                    {
                        new string[] { "W A S D", "Swim the way you are looking." },
                        new string[] { "Space / C", "Rise / sink (on a controller: RB / LB)." },
                        new string[] { "Click", "Fire the harpoon. It is the only tool down there." },
                        new string[] { "F", "Pick up a find you are close to." },
                        new string[] { "Q", "Swim back up to the beach." },
                    },
                },
            },
        },
        new HelpTopic
        {
            id = "treasure",
            icon = "⛏️",
            title = "Finding treasure",
            blocks = new HelpBlock[]
            {
                new HelpBlock
                {
                    h = "The treasure cove",
                    p = "The tide leaves its finds in the cove below the camp. Old fishing nets are strung round it and round the camp, down both sides and across the top behind the camp, with gaps to walk through, and banners hang in the gaps. Everything that can be dug up lies inside the nets, on the open sand below the camp. You are free to walk out past them, all the way to the mountains, but out there the detector stays quiet: there is only sand. The map (M) shows the nets as a dashed line.",
                },
                new HelpBlock
                {
                    h = "Sweep, dig, clean",
                    list = new string[] { "Take out the detector (1) and sweep the sand in front of you. It beeps faster, and the screen lights up, the closer something is. Different tones mean different kinds of finds.", "Take out the trowel (2) and click to dig. Dig until the find appears, then pick it up with F or a click.", "Digging uses warmth (the bar at the top left). Warm up at the campfire or with the coffee on the stump, and the bar fills again.", "Cleaning: the find is encrusted with sand. Scrub it with the brush (hold click and move the mouse, or hold RT and use the right stick) until it shines." },
                },
                new HelpBlock
                {
                    h = "What to do with finds",
                    list = new string[] { "Coins and jewellery: sell them at the workbench.", "Trash: recycle it in the green bin at camp for a few coins. Every piece you recycle makes the beach greener.", "Lost items: they are matched to a request on the Lost & Found board, and are returned and paid for as soon as you clean them. The owners write to you afterwards.", "Antique shards: three shards make a whole antique. Put them together at the workbench for a big payout.", "Treasure maps and bottle maps lead to buried chests. Look for the red crosses on the map." },
                },
                new HelpBlock
                {
                    p = "Tip: dig a little wider if a signal stays strong but nothing shows up. Holes you dig show on your map, so you do not dig the same place twice.",
                },
            },
        },
        new HelpTopic
        {
            id = "camp",
            icon = "⛺",
            title = "Your camp",
            blocks = new HelpBlock[]
            {
                new HelpBlock
                {
                    h = "What is at camp",
                    list = new string[] { "The campfire and the coffee: rest, warm up, and let the day pass if you like.", "The workbench: sell coins and jewellery, and assemble antiques.", "The Lost & Found board: see what people are missing.", "The recycling bin: turn trash into coins.", "The camper: the shop. Buy upgrades for your detector, digging, cleaning and gear, plus a golf cart key, a boat key, a bow, a toy pistol, marker flags, a bucket and spade, diving gear, bones for the dog and decorations.", "The post: letters that arrive after you return lost things.", "The radio: once it is on, the music follows you everywhere, and the tune changes with the place (the beach by day, the camp at night, under the water, under the dunes). Use E to pick a fixed song instead.", "The hammock: sleep until morning.", "The lantern: switch it on or off.", "The journal: everything you have discovered." },
                },
                new HelpBlock
                {
                    h = "The dog",
                    p = "A golden retriever lives at camp. Pet it with E. Buy bones in the shop (there are only a few) and give one with F, and now and then the dog will dig up something for you. It leaves paw prints on the sand and on your map.",
                },
                new HelpBlock
                {
                    h = "Making it home",
                    p = "The shop sells decorations: a rug, a sign you can rewrite by clicking it, string lights, a cooler, potted palms, an umbrella, a surfboard rack and tiki torches. They are a good thing to spend coins on once you have the important things.",
                },
            },
        },
        new HelpTopic
        {
            id = "explore",
            icon = "🧭",
            title = "Beyond the beach",
            blocks = new HelpBlock[]
            {
                new HelpBlock
                {
                    h = "Getting around",
                    list = new string[] { "The golf cart (needs a key) is quick on land. F gets in and out.", "The boat (needs a key) takes you out to sea. F takes the wheel, E steps off when you are close to shore.", "The map (M) shows where you are. Click it to set a destination and a glowing beam and a dotted line will lead you there.", "Marker flags (7) are planted on dry sand and show on both maps." },
                },
                new HelpBlock
                {
                    h = "The islands",
                    p = "Sail out to the small island. It has its own finds that lie in the open, some ruins, and an old altar with a map that shows where treasure is buried on your own beach. Two more lie farther out: a ring of sand around a lagoon to the east, with a gap a boat can sail through, and a black volcanic rock to the west. Each has treasures found nowhere else. The map (M) shows all three.",
                },
                new HelpBlock
                {
                    h = "Diving",
                    list = new string[] { "Buy the diving gear at the camper. Stand at the water’s edge and press F to dive in. Press Q to come back up.", "Down there you can find starfish, plates, bells and gold. Press F near a find to clean it.", "The harpoon spears fish for a coin each. Turtles are friendly and cannot be hit.", "It gets deeper the further out you swim, and the water changes with the time of day." },
                },
                new HelpBlock
                {
                    h = "Other tools",
                    list = new string[] { "Fishing: buy a rod at the camper, walk out to the bench at the end of the pier and press F to sit. Click to cast, wait for the float to go under, click, then hold to reel it in. Some fish only bite by day, at night or in the rain.", "Hand net: buy it at the camper and press 2 while diving. Click to sweep it at a fish or animal close in front of you. It catches them alive.", "There is one Golden Fish in the whole sea. It is very fast and bolts in bursts when you come near. Chase it, wait for its glide, and corner it against the edge of the water.", "Aquarium: a small one of every kind of sea creature you catch alive lives in the tank at camp. Press F at the tank to see what is still missing.", "Camera (3): click to raise it, click again to take a photo. It also works underwater and in the room under the dunes. Your photos are on the board by the fire, where you can look at them, pin them, save them to your Pictures folder or throw them away.", "Binoculars (5): click to zoom. They show the distance to what you are looking at.", "Bucket and spade (8): build sandcastles. Click to add to one. The sea and the rain wash them away.", "Bow (6): hold click to draw, release to shoot. Toy pistol (4): click to shoot. Crabs, gulls, sharks and coconuts pay a few coins when hit." },
                },
                new HelpBlock
                {
                    p = "There are secrets that the journal and the maps only hint at. If you find a strange door, read what the captain left behind, and keep your antiques together.",
                },
            },
        },
        new HelpTopic
        {
            id = "tips",
            icon = "💡",
            title = "Tips and saving",
            blocks = new HelpBlock[]
            {
                new HelpBlock
                {
                    h = "Saving",
                    list = new string[] { "The game saves itself about every minute and a half, and after you clean a find or leave the shop. That goes into the single \"Autosave\" slot.", "To keep a save of your own, open the pause menu (Esc) and use Save game. You can have as many as you like. Load them from the main menu or the pause menu." },
                },
                new HelpBlock
                {
                    h = "Good habits",
                    list = new string[] { "Check the journal now and then. Its pages are a gentle guide to what you have not tried.", "Walk the shore. Many finds are washed up near the water.", "Not enough coins? Sell what you can and go back to the sand. There is always more to find.", "Look at the achievements list (main menu or pause menu) for ideas of things to try.", "Turn Shadows off in Settings if the game runs slowly on your computer." },
                },
                new HelpBlock { p = "Take your time. The beach is not going anywhere." },
            },
        },
    };

}
