# Cozy Treasure Hunter: Game Document

A cozy, first-person treasure-hunting game on a procedurally built beach. You sweep the sand with a metal detector, dig, clean what you find, return lost belongings to the people who dropped them, and live at a little camp with a golden retriever and a radio.

Made by **Armen Azizbekyan**, solo developer and creator.

---

## 1. At a glance

| | |
|---|---|
| **Genre** | Relaxed first-person exploration and collecting |
| **Pace** | Slow, calm, no fail state, no timer pressure |
| **Goal** | Find every item on the Lost & Found list and return it. The game then ends with a thank-you card |
| **Length (target)** | Several hours; the economy is tuned so upgrades come over hours, not minutes (see section 8) |
| **Platforms** | Windows, macOS and Linux desktop app (Electron); also runs in a browser |
| **Input** | Keyboard and mouse, or a game controller |
| **Art and sound** | 100% procedural: every model, texture and sound is generated in code. No external asset files |
| **Tech** | Vite, TypeScript (strict), Three.js, Web Audio API |

---

## 2. Controls

### Keyboard and mouse
| Action | Input |
|---|---|
| Walk | W A S D |
| Hurry | Hold Shift |
| Look around | Mouse (the pointer is hidden while playing) |
| Use the tool in your hands | Left click |
| Switch tool | Scroll wheel, or number keys 1 (detector), 2 (trowel), 4 (gun), 5 (binoculars), 6 (bow), 7 (flags) |
| Swap detector and trowel | Space |
| Interact with what is next to you | F |
| Second action (change radio song, pet the dog, step off the boat) | E |
| Open the island map | M |
| Mute | N |
| Pause | Esc |
| Binoculars | Click to raise and change zoom, right-click to lower |
| Bow | Hold click to draw, release to shoot, right-click to let the string down |
| Driving the cart or boat | W / S throttle and reverse, A / D steer, Space brakes, F gets out (boat: E steps off at the beach) |
| Journal | Left and right arrows (or A / D) to turn pages, Esc to close |
| Cleaning a find | Hold left mouse and scrub; Q / E or arrow keys rotate the item |

### Controller (Xbox, PlayStation and most other pads)
| Action | Input |
|---|---|
| Walk | Left stick (press it in to hurry) |
| Look | Right stick (uses the mouse sensitivity setting) |
| Use tool | Right trigger (RT) |
| Aim, lower binoculars, let the bow string down | Left trigger (LT) |
| Interact | A |
| Second action | X |
| Map | Y |
| Pause | Start |
| Mute | Back |
| Tools | LB / RB cycle; d-pad up detector, right trowel, down binoculars, left cycles flags / bow / gun |
| Driving | RT throttle, LT reverse, left stick steers, B brakes |
| Menus | d-pad or left stick moves a glowing focus ring; A selects; B goes back; left and right adjust sliders and dropdowns |
| Journal | LB / RB turn pages, B closes |
| Map | Left stick moves a cursor, A sets a destination |
| Cleaning a find | Left stick turns the find, right stick moves the brush, RT scrubs |

The game switches to controller wording the moment you touch the pad: prompts show A and X, hints say RT and LT, and the pause card lists pad buttons. Move the mouse and it switches back. A d-pad tool picker replaces the tool badge, showing a tool on each side with the one in hand lit up.

---

## 3. The core loop

1. **Sweep** the sand with the detector and listen for the beep. It gets faster and brighter the closer you are.
2. **Dig** with the trowel where the signal is strongest. Digging costs Warmth.
3. **Pick up** the find and take it to the workbench view to **clean** it by scrubbing.
4. **Keep, sell or deliver.** Coins and jewelry sell; trash is recycled; shards combine into antiques; Lost & Found items go back to their owners for a reward.
5. **Spend** coins at the camper shop on upgrades, vehicles, weapons and more.
6. **Rest** at the fire or drink coffee to refill Warmth, and carry on.
7. **Finish** by returning every Lost & Found item.

---

## 4. The world

### The beach
A large procedural beach (140 m wide) with dunes behind, wet sand and shoreline in front, and the sea beyond. Terrain is a detailed heightfield: holes you dig stay, with realistic sand colouring, wet-sand darkening and crust textures. Footprints and tyre tracks press into the sand and fade after a while.

### Scenery
- Palm trees (about 100) lining the beach and scattered inland, with swaying crowns and coconuts.
- Distant mountains, clouds and a visible sun and moon.
- A few big boulders placed at random spots across the beach each game. Items never spawn near them, so a boulder can never hide a find. Larger boulders block walking and driving.
- A very small amount of swaying beach grass in a few clumps, mostly near the dunes, also kept clear of finds.

### Day and night
- A full day lasts **5 minutes**. A day counter and a time icon sit at the top right.
- Smooth cycle: dawn, golden morning, midday, sunset, night with stars and moon.
- Fire and lantern light grow at night; camper windows glow.
- The lantern (top gear tier) can freeze the sky at any time of day or let the cycle run.

### Weather
- **Rain** is rare: about one day in seven, never on day 1, lasting about a quarter of the day.
- Rain greys the sky, thickens fog, dims the sun, draws rain streaks around you and plays a soft rain sound. The clock shows a rain icon.

### Wildlife (all roaming on their own)
- **Crabs** scuttle on the sand.
- **Seagulls** (and other birds) wheel overhead.
- **Sharks** and **fish** swim in the sea, with whitecaps and waves.

---

## 5. Tools

| Tool | What it does |
|---|---|
| **Metal detector** | A sweeping ring on the ground shows the scan area. Beeps get faster as you near a buried find. Tier 2 lets the waveform and ring colour tell trash, coins and deep finds apart; tier 3 draws a glowing heat map on the sand |
| **Trowel** | Digs a hole where you aim. Bigger and cheaper with upgrades |
| **Binoculars** | First-person view; click to zoom, with a range readout. Right-click to lower |
| **Toy pistol** (shop) | Visible tracer bullets. Hits crabs, gulls, sharks, fish and coconuts |
| **Hunting bow** (shop) | Hold to draw, release to shoot. Unlimited arrows; arrows stick in trunks and sand |
| **Marker flags** (shop) | Plant flags on dry sand; they appear on the minimap and island map. Press F beside a flag to pick it up |

Locator rings use a light core with a thin dark outline so they show on pale sand and at night.

---

## 6. Finds, cleaning and the Lost & Found list

### What is buried
About **360 finds** are placed on the beach at the start of each game and never respawn:
- **Trash** (about 190): pull-tabs, nails, tin cans, bottle caps, bottles, hooks, flip-flops, bolts, six-pack rings.
- **Coins** (about 120): penny, dime, nickel, quarter, silver dollar, Spanish real, Roman denarius, doubloon, gold sovereign.
- **Jewelry** (about 18): signet ring, ruby band.
- **Lost & Found items** (8 guaranteed buried, plus matching coins and rings): grandfather's compass, captain's brass key, pocket watch, pearl necklace, vintage sunglasses, silver harmonica, wedding band, wooden toy sailboat.
- **Antique shards**: two sets of three (Ancient Astrolabe, Pirate Medallion).

Items sit at set depths, in dry, wet or any sand, so some are only near the dunes and some only near the water.

### Cleaning
Picking a find opens a close-up view. Scrub the crust away with the mouse (or the right stick and RT). Better cleaning gear gives a wider brush; the top tier adds an Instant Clean button.

### What you can do with a find
- **Sell** coins and jewelry at the workbench.
- **Recycle** trash at the bin for a few coins and Ecology points.
- **Assemble** three shards of a set into an antique that pays out (250g each) and sits on the workbench shelf.
- **Deliver** Lost & Found items automatically when cleaned.

### The Lost & Found board (13 notes)
| Owner | Wants | Reward |
|---|---|---|
| Marta | Silver compass (wet shoreline) | 85g |
| Old Pim | Brass key (dunes) | 125g |
| Juno | Ruby band | 70g |
| The Collector | 2 silver quarters | 65g |
| Grandma Ines | Silver pocket watch (wet shoreline) | 85g |
| Celeste | Pearl necklace | 110g |
| Rico | Vintage sunglasses (dunes) | 65g |
| Busker Bo | Silver harmonica | 75g |
| Dana | Wedding band (near the water) | 105g |
| Little Mo | Wooden toy sailboat | 55g |
| The Jar Lady | 3 old pennies | 40g |
| Captain Brine | Gold doubloon | 100g |
| Professor Plum | Roman denarius | 105g |

### The ending
When the last note is delivered and you close the result card, the game shows **The End**, thanks the player, credits **Armen Azizbekyan** as a solo developer and creator, and returns to the main menu (by button or automatically after 20 seconds).

---

## 7. The camp and the shop

The camp sits at the back of the beach, around a campfire.

| Object | What it does |
|---|---|
| **Campfire** | Rest to refill Warmth |
| **Camper (house on wheels)** | The shop: upgrades, keys, weapons, flags, bones. Has a striped awning, glowing windows and a rounded roof |
| **Workbench** | Sell finds and assemble shards. Holds the radio, the journal and a shelf for antiques |
| **Radio** | Plays music; louder the closer you stand. Turn on and off, and change station |
| **Journal (book on the workbench)** | See section 12 |
| **Recycling bin** | Turn trash into coins and Ecology |
| **Lost & Found board** | Read the 13 notes and your progress |
| **Coffee on a stump** | Drink to refill Warmth |
| **Lantern** | Unlocked with the top gear tier; sets or frees the time of day |
| **Golf cart** | Parked near camp (needs a key) |
| **Dog** | See section 11 |

### Warmth
Digging uses Warmth (6 per dig at the start, down to 2 with upgrades); wet sand drains more until you buy waterproof boots. When it runs out you are too chilly to dig; rest at the fire or drink coffee.

### Upgrades (four tracks, three tiers each)
| Track | Tier 1 | Tier 2 (250g) | Tier 3 (800g) |
|---|---|---|---|
| **Detector** | Rusty Wand, 2.0 m radius | Dual-Frequency Coil, 3.8 m, tells trash from gold | Ground Penetrating Radar, 5.5 m, heat map |
| **Digging** | Plastic Scoop, 0.7 m | Trench Trowel, 1.2 m, cheaper dig | Heavy Sand Sifter, 1.8 m, finds in 1 to 2 clicks |
| **Cleaning** | Old Toothbrush | Brass brush, wider, +25% sale value | Ultrasonic Bath, widest, Instant Clean |
| **Gear** | Tin Mug, 100 Warmth | Thermos, 180 Warmth, boots | Espresso Flask, 300 Warmth, lantern |

### Shop items
| Item | Price | Notes |
|---|---|---|
| Golf cart key | 300g | Drive the cart |
| Boat key | 350g | Drive the boat |
| Toy pistol | 150g | For fun, small bounties |
| Hunting bow | 220g | Unlimited arrows |
| Marker flags | 15g for 5 | Buy as often as you like |
| Dog bone | 8g | Only 8 in the shop for the whole game |

---

## 8. Economy and pacing

- You start with **0 coins**.
- Finds sell for about 55% of list prices (for example a penny 8g, a ruby band 66g). All sellable finds together are worth roughly 3,200g; with Lost & Found rewards, shard antiques and sea chests the whole game offers roughly 5,300g.
- Buying every upgrade, key and weapon costs about the same, so affording everything means clearing most of the beach.
- Designed so the first upgrade comes after roughly 20 to 30 minutes of digging and the top tiers after several hours. Prices and the value multiplier are easy constants to tune (`VALUE_SCALE` in `items.ts`, costs in `StateManager.ts`).
- **Small bounties** (hunting): crab 1g, gull 2g, fish 1g, coconut 0.5g, shark 5g (two hits).
- **Sea treasure chests**: three chests out at sea, 175g each, hauled in with the boat.

---

## 9. Vehicles

### Golf cart
Teal cart with a roof flag and headlights that switch on at night. Needs the key. Steering wheel turns with the front wheels; it leaves tyre tracks. Blocked by palms, camp props and boulders. Press F to get out.

### Boat
Motorboat with a helm, wake and bow spray; it bobs on the waves. Needs the key.
- Leave the wheel with F to stand on deck and use weapons or binoculars.
- You can only step off (E) when parked close to the beach, so you can always climb back aboard.
- The sea is the way to the three treasure chests.

---

## 10. Wildlife and rewards

- **Crabs** scuttle away. Shooting one pays 1g, with a burst of sand.
- **Gulls** fly overhead. Hitting one pays 2g and sends feathers.
- **Fish** swim near the shore. 1g each.
- **Sharks** need two hits. 5g.
- **Coconuts** hang in the palms. Shooting one drops it to the sand (it fades and regrows after about two minutes). 0.5g each.

Each of these also writes a story into the journal the first time.

---

## 11. The dog

A **golden retriever** lives at the camp.

- **At rest** it lies by the fire. When you come within about 5 m it sits up, turns to face you and watches. When you leave it lies down again.
- **Petting:** press E (X on a controller) beside it. It shakes its head, wags its tail, opens its mouth, yips, and little hearts float up. A friendly line appears each time.
- **Bones:** buy bones at the camper shop (8g each, 8 for the whole game). Press F (A) beside the dog to give one.
- **A trip:** with a bone the dog runs off, sniffing its way along a winding path with its nose down, stopping to sniff the ground now and then, and leaving paw prints in the sand. It goes to a still-buried coin or piece of trash, stands about a metre short of it facing it, digs and **barks** (louder the closer you are). A paw print marks its position on the minimap and island map.
- **Finding it:** dig where the dog is. Once you collect the item, the dog yips and trots home. If you take more than five minutes it gives up and goes home.
- **Rarely** (about 6% of trips) the dog's nose finds a wanted Lost & Found item instead, and the game says it has caught "a very special scent".
- A trip in progress is saved with your game.

---

## 12. The camp journal

A book lies on the workbench next to the radio. Walk up and press F (A) to read it. It glows softly while there are unread pages.

- Opens as a two-page spread (one page on a narrow screen), parchment style, with Previous, Next and Close.
- **36 short pages** in groups: Camp, Dog, Beach, Finds, Creatures, Journeys, Sky.
- **8 pages are there from the start**: the camper, the fire, the radio, the workbench, the dog, the dog's bones, the beach, Lost and Found.
- **The rest are written in automatically** the first time you do things: your first dig, finding trash, a coin, jewelry or a shard, delivering a lost item, shooting a crab, gull, shark, fish or coconut, driving the cart or boat, hauling a chest, opening the map, using binoculars, planting a flag, firing the gun or bow, turning on the radio, changing the song, drinking coffee, giving the dog a bone, the dog finding something, a special find, petting the dog, the first rain, the first night.
- **It doubles as a guide**: every page is shown, and pages you have not earned yet are tagged "To discover". New pages are tagged "New", and a toast announces each one.
- `BOOK_SHOW_ALL` in `Journal.ts` switches to showing only discovered pages.

---

## 13. The radio

A retro radio on the workbench with **six stations**, each a generated loop with its own mood:

| Station | Mood |
|---|---|
| Morning Tide | Soft piano, pads and bells |
| Lazy Sunset | Warm pads with plucked strings |
| Starlit Night | Sparse, deep pads and high bells |
| Beach Bossa | Light swaying beat, chord stabs, shaker |
| Rainy Window | Slow electric piano under rain hiss |
| Campfire Strum | Acoustic strum with a soft thump |

- F (A) turns it on or off; the prompt shows the current station.
- **E (X) changes to the next station** and turns the radio on if it was off.
- It gets louder the closer you stand and muffled with distance. The station is saved with your game.

---

## 14. Maps and navigation

- **Minimap**: a round map above your tool that turns so forward is up. Shows holes, the camp, cart, boat, flags, the dog's paw print (while it is out) and your destination.
- **Island map** (M): full-island view with the same markers. Click (or use the cursor on a controller) to set a **destination** with a dashed line and a glowing beam in the world; click the pin to remove it. You get a message on arrival.
- Flags show on both maps.

---

## 15. Menus, saves and settings

### Main menu
A live 3D background (a slow camera drifting between the camp, a crab and a shark with gulls overhead) with four buttons: **New Game**, **Load Game**, **Settings**, **Exit Game**. New Game shows a short controls card, then you begin.

### Pause (Esc)
Resume, Save, Load, Settings, New game, Main menu. A card lists the controls. The mouse pointer returns on all menus.

### Saves
Unlimited named save slots stored on the player's machine (in the desktop app they live in the app's data folder and persist between runs). A save keeps your coins, upgrades, inventory, holes, finds, flags, vehicles' positions, the dog's current trip, the radio station, journal pages, bones bought, time of day and the day count.

### Settings
- **Sound**: master volume, radio music, sea and wind.
- **Controls**: mouse look speed (also used by the right stick).
- **Display**: screen mode (Fullscreen, Windowed, Windowed with no border) and window size (up to your screen size). A change of border takes effect after the next launch.
- **Graphics**: shadows on or off, sharp picture (full pixel density) on or off.

---

## 16. Interface notes

- A small panel shows coins and Warmth; chips at the top right show the day and time, backpack count, Ecology score and, while you have them, bones.
- Floating prompts show F and E actions near objects; hints at the bottom explain the current tool.
- Toasts announce rewards, new journal pages and events.
- Binocular view has two lenses, a zoom and range readout, and a legend.
- The mouse pointer is hidden during play and shown on every menu, the pause card, the map, the journal, the camp and the cleaning view.

---

## 17. Technical overview

- **Engine**: Three.js scene with a custom heightfield terrain (520 segments) and shader tweaks for damp sand, instanced decals for footprints, paw prints, tyre tracks and whitecaps, a CPU particle system, and instanced swaying grass.
- **Audio**: all Web Audio synthesis, including ocean, wind, rain, detector, footsteps, fire, engines, the dog's bark and the six radio stations.
- **State**: a reactive state manager drives the interface; saves are JSON snapshots.
- **Desktop**: Electron wrapper (Windows, macOS, Linux) with fullscreen, windowed and borderless support, a real Exit button, and the remembered display mode.

### Project layout (src/)
- `core/`: Game (the hub), state, save, settings, audio, input, gamepad, journal text, item and quest data
- `world/`: beach, sky, ocean, camp, scenery, stones, grass, wildlife, vehicles, dog, rain, tracks, flags, loot spawner
- `mechanics/`: detector, trowel, bullets, arrows, first-person view model, cleaning station
- `ui/`: HUD, menus, camp modal, maps, controller wording helper

### Commands
```
npm install          install dependencies
npm run dev          develop in a browser (http://localhost:5173)
npm run build        type-check and build the web version
npm run electron:start   build and run the desktop app
npm run dist:win     Windows installer and portable exe
npm run dist:mac     macOS dmg
npm run dist:linux   Linux AppImage and deb
```
A GitHub Actions workflow builds all three platforms.

### Estimated requirements (not benchmarked)
- **Minimum**: Windows 10 / macOS 11 / recent 64-bit Linux, dual-core 2.5 GHz, 4 GB RAM, integrated graphics with WebGL2, about 400 MB free. Turn shadows and sharp picture off.
- **Recommended**: quad-core 3 GHz, 8 GB RAM, a GTX 1050 / RX 560 class card or Apple M1, about 400 MB free.

---

## 18. Known limits and ideas

- Pacing and prices are tuned on paper; real playtesting will refine them.
- Mac and Linux builds are not yet tested on those systems; the exe is unsigned.
- Ideas under consideration: fishing, tides, camp decoration, visitors, photo mode, achievements and Steam integration, more dog tricks, rainy-day events, a collection book of all creatures and finds.
