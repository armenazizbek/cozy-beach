# UI rebuild log

Step by step, as `UI_PLAN.md` orders it. Branch `claude/ui-rebuild`, from `main` at 0af08c1. Every decision (D1 to D7)
is the plan's recommendation, accepted by the owner on 2026-11-24.

## B4: docs and comments that described deleted code (2026-11-24)

- Code comments that still spoke of the bridge or the DOM layer as running (Atmosphere, LiveTree's claim, Realizer,
  World, the tools in hand in Game, UiAnim, UiRoot, CleaningStation, Draw, UrpSettings, SelfCheckMeshKit) now say what
  happens today. Notes on where a rule came from say "the old page's HTMLElement.X".
- `Style.uss`: the header describes the native UI (UiRoot, UiNode, UiAnim) and the stale notes in its rules name today's
  code.
- `STEAM_DECK_PLAN.md` and `PORT_STATUS.md` are historical records with line references: each got a note at the top that
  maps the old paths to the new ones, instead of rewriting their history.
- Comments and docs only: no code changed, so no parity pass is needed for this step.

## U0: the harness first (2026-11-24)

- `Tools/Parity/TestSettings.ps1`: every run swaps the player's saved settings (`HKCU\Software\Cozy Beach\Tidefinder`)
  for fixed, silent test settings (all volumes 0, 1280x720 windowed, high quality, vsync) and puts the player's own back
  afterwards, even when a run fails. `run_suites.ps1` and `run_perf.ps1` use it. Tests make no sound now.
- `compare.py` checks the environment each run logged (screen, Hz, mode, dpi, vsync) and stops with exit 3 when the two
  runs differ (`--any-environment` to compare anyway). The drift seen before came from these, not from the code.
- New suite `mouse` (`-autoshot-mouse`): a virtual Input System mouse ("AutoShotMouse", left alone by `TestClock.Isolate`)
  hovers, clicks, drags the slider and scrolls the wheel in Settings through the real input path; it quits with 2 on any
  failed check.
- Fresh reference `refU` from main (Mono); `refBase` vs `refU` showed no FAIL and identical logs.

## U1: foundation (2026-11-24)

- The icon baker (`Editor/IconBaker.cs`, run by the build): the 6 tool icons and the 36 shop pictures are rasterised at
  build time into `Resources/UI/Icons.bytes`; `IconAtlas` serves the same pixels the runtime rasteriser made, keyed by
  the markup's hash, and the rasteriser stays as the fallback.
- `Ux` and the `El` UXML element: a screen is UXML in `Resources/UI/Screens`; `Ux.Load` instantiates it and adopts the
  tree into `UiNode`, so the classes, tags, behaviour and look stay exactly what they were. Code finds parts by name
  (`Must`), and canvases or code-made parts take a named slot (`Ux.Replace`).
- **Deviation:** `Root.uxml`, `ScreenStack` and `UiScreen` were not built. The layering stays as today (`UiNode.Reorder`);
  adopting UXML into `UiNode` gave exact parity at no risk, and a new stack would only change the order of the same
  elements. `Common.uss` was not split out either (no visible gain; `Style.uss` keeps the rules).

## U2 to U9: screens to UXML (2026-11-24)

- U2 camp (`Camp.uxml`, `CampRow.uxml`); U3 settings and saves (`Settings`, `SettingRow`, `Saves`, `SaveRow`); U4 main
  menu, pause, Help, Achievements, Credits, farewell (`MainMenu`, `MenuButton`, `Pause`, `Help`, `Achievements`,
  `AchievementRow`, `Credits`, `Farewell`); U5 journal, letters and story pages, ending, sleep (`Book`, `Letters`,
  `Ending`, `Sleep`); U6 the HUD and its cards (`Hud.uxml`, found by name in UIManager's constructor); U7 intro, result
  and opening caption (`Intro`, `ResultCard`, `Caption`); U8 the big map (`Map.uxml`); U9 the telescope (`Telescope`,
  `GlassLabel`).
- Each pass: no FAIL against `refU`, logs and dumps identical, the mouse suite at 0 failed.
- **Deviations:** the overlays (binocular lenses, viewfinder, flash, waveform, minimap markers) stay canvas pictures
  rather than moving to `Painter2D`: they already draw through the native canvas, and moving them changes pixels with no
  gain the player can see. Small parts made per row from data (tab buttons, toasts, the step control's arrows, page heads)
  stay made in code. `UiNode` stays as the element every screen is adopted into, so U10's deletion of `UiNode`, `UiX` and
  `TextMarkup` is not done.

## B2 (part): photos are encoded off the main thread (2026-11-24)

- `Photos.save` copies the pixels and makes the JPG on a worker (`ImageConversion.EncodeArrayToJPG`), where before only
  the disk write was off the main thread. The texture is in the album at once, as before.
- Checked: the items suite (photo, photo-alt and the rest) has no FAIL, logs and dumps identical; the saved JPGs decode to
  the same pixels as the reference's (mean difference 0.001 of 255 or less).
- Not done: `AsyncGPUReadback` for the read itself (the underwater tint is applied on the CPU to the read pixels, so it
  would need a shader first), and the rest of B2 (the renderer front). B3 and B1 are not started: see the note below.

## Where this stops (2026-11-24)

- Part A's screens are all UXML (U0 to U9). U10's deletions are not done because `UiNode` is what the UXML is adopted
  into; deleting it means rebuilding every screen on raw VisualElements, which D2 allows but which needs your sign-off
  on shots, not an overnight run.
- B3 (input actions) is not started on purpose: the smoke tests press keys through `InputManager.held`, below the input
  path B3 replaces, so no suite would see a broken keyboard. It needs a virtual keyboard in AutoShot first, as the mouse
  got in U0.
- D3 (bundled fonts on PC) is the one visible change and is left for you to see first.

## Before B3: a virtual keyboard test (2026-11-24)

- New run `keyboard` in the `ui` suite (`-autoshot-keys`): a virtual Input System keyboard ("AutoShotKeyboard", left on
  by `TestClock.Isolate` like the mouse) presses keys through the real path, Input System, `InputManager.poll`, then the
  game's key handling. The other runs press keys through `InputManager.held`, below that path.
- 14 checks: on the title menu Down and Up move the highlight, Down reaches Settings, Enter opens it, Esc closes it; in
  play holding W walks, 2 takes the trowel, Space swaps back, N mutes and unmutes, Esc pauses and resumes, M opens and
  closes the map. It quits with 2 if any fails. Shots `1k-keys-settings.png` and `1k-keys-map.png`.
- Game code gained read-only readouts for it (`Game.smokeTestKeyState`, `AudioManager.isMuted`); nothing else changed.
- The run is named `keyboard`, not `keys`: run_suites keeps its runs in a PowerShell table, where a run called "keys"
  hides the table's own `.Keys`.
- Checked: 0 of 14 failed; the `ui` suite against `refU` has no FAIL, logs and dumps identical, the mouse run 0 failed.
  B3 can now be gated on this run.

## B3: the keys are actions (2026-11-24)

- `Core/Controls.cs`: every key the game reads is an action with a meaning (`Act`: Forward, Use, Pause, Confirm, ZoomIn,
  ...) in an Input System action map, `Controls.Map`, bound by default to the keys the game always used. One key carries
  several actions, as before (E is Use 2, next tab, zoom in and rotate right).
- A key press is still handled once, as a `Press` carrying every action of its key, so the game's checks run in the same
  order; `Game.onKey` and `menuKey` ask `p.Is(Act.X)` instead of comparing key codes, and held keys are
  `input.Held(Act.X)`. The key switch at the end of `onKey` became an if chain in the same order.
- The controller and the smoke tests still hold the game's virtual keys ("KeyW"); those mean the default bindings, so a
  rebound keyboard does not change the controller.
- Rebinding: `Controls.Load` reads the player's overrides from PlayerPrefs (`tidefinder-controls-v1`, apart from the
  settings), `Controls.Save` keeps them. The smoke tests always use the defaults. There is no rebinding screen yet, and the
  key pictures in prompts still show the default keys.
- Kept as it was: NumpadAdd and NumpadSubtract were checked for zooming but never reported by the key reader, so they did
  nothing; they are still not bound.
- The keyboard test gained 2 checks: Forward rebound to I walks on I and no longer on W.
- A mistake on the way: a static table was declared after the field whose initialiser fills it, so the first build
  stopped on the loading screen. The build-and-test commands now stop when the build fails or did not replace the game.
- Checked: the display changed from the night before (360 Hz at 100% instead of 60 Hz at 150%), so a new reference was
  taken from the same main build on this display (`refU360`). Against it: no FAIL apart from the `keyboard` and `mouse`
  runs, which the reference build does not have; logs and dumps identical; keyboard 16 of 16, mouse 0 failed, selfcheck
  8 of 8.

## The Keys section in Settings (2026-11-24)

- At the end of Settings: one row per key a player would want to move (`Controls.Rebindable`: walking, running, Use,
  Second use, the tool swap, map, mute, swimming, the surface, braking). Pressing a row waits for the next key
  (`Controls.Rebind`, the Input System's interactive rebinding, keyboard only); Esc keeps the old key, and so does
  waiting eight seconds or closing Settings. "Put every key back" removes every override. The choice is kept at once.
- The key pressed for a rebind is the binding, not a command: `Controls.Quiet` makes InputManager treat it as typing.
- The F and E key pictures in the HUD prompts follow the bindings (`Controls.KeyName`, `Controls.Changed`). Hints that name
  a key in their words, and the other menus' key pictures, still show the defaults.
- No automatic swapping when two actions share a key: several share one on purpose (Space swaps tools, swims up, brakes
  and confirms).
- Fixed on the way: Esc in a menu presses the first button whose name starts with "back", "done", "leave" and so on, and
  the row "Leave the water" matched it. Settings rows are never taken for the way back now, and that row is "Swim to the
  surface".
- Tests: the keyboard run now uses the screen as a player would (down to the row, Enter, I; Esc keeps a key; the reset)
  and then checks play still works: 22 of 22. A rebind in a test run never reaches the player's own saved keys.
- Harness: the Input System turns keyboards and mice off while the window is not in front, the virtual ones too, so the
  mouse and keyboard runs failed whenever someone used the PC during a pass. The runs' devices ignore focus now
  (test runs only).
- Checked against `refU360`: no FAIL apart from the runs the reference build does not have; the pad log differs only in
  the settings rows it can reach (32 instead of 17); logs and dumps otherwise identical; selfcheck 8 of 8, mouse 0 failed,
  keyboard 0 failed.

## Key names in words follow the bindings; test windows stay behind (2026-11-24)

- `Controls.Say(text, acts)`: a text that names keys reads with the keys as bound, for the listed actions only (Q swims
  up in the sea but turns the page in the camp, so only the dive hints list Surface). With the game's own keys, or on a
  controller, the text is returned as written, so nothing on screen moves. Used by the in-play hints (cart, boat, diving,
  under the dunes, telescope, lighthouse, flags, fishing), the shop's words, two toasts and Help (its paragraphs name
  Use, Second use, the map and the surface; its key table goes by each row's key).
- `Glyphs`: rebound key names are drawn as key caps in hints too ("G gets out", "I / A / S / D swim"); with the game's
  own keys the pattern is the old one.
- The keyboard test checks the words (a rebound hint, a hint that keeps a key it is not about, WASD, the prompt's key
  picture, and the words as written once the keys are back): 0 failed.
- Harness: each test window opens behind the other windows and the focus goes back to where it was (`-Front` to keep it
  in front). Same monitor, so the display the run reports and the reference stay the same; checked on 10 runs.
- Checked against `refU360`: nothing new apart from `crust/14-crust-pocket_watch.png`, where every pixel is off by 1 or 2
  of 255. That is noise between runs of the same build: a run in front differs as much.

## D3: the bundled fonts everywhere (2026-11-24)

- The UI (`UiRoot.BuildTextSettings`) and the text drawn into the world (`CanvasFonts.Bundled`: signs, the map) use the
  game's own faces on every machine: Fira Sans for body text, PT Serif for headings and serif text (both SIL OFL). Before,
  a Windows PC used its own Trebuchet MS and Georgia and only a Steam Deck the bundled ones. Now PC and Deck look alike.
- `-osfonts` brings back Windows' fonts where installed (the look before), for comparison and as an escape hatch.
- Not done: the plan's font assets made at build time; the faces are still made from the .ttf files at start-up.
- Before and after: `unity/Docs/fonts-d3/` (left the old fonts, right the new), taken from one build with and without
  `-osfonts`. Body text is a little lighter and wider, so some cards grow by a few pixels.
- Checked: a full pass, against `refU360` by structure only, since nearly every picture changes by design: all 39 runs
  exit cleanly, scene dumps identical, logs as before (the pad log differs only in the settings rows it reaches),
  selfcheck 8 of 8, mouse and keyboard 0 failed, the layout run without complaints.

## B2 (part): photos come back from the GPU without a stall (2026-11-24)

- `CapturePhoto` resolves the camera's picture into a plain texture and asks for it with `AsyncGPUReadback`, instead of
  `ReadPixels`, which made the game wait for the GPU. The photo arrives a frame or two later; the shutter, the flash and
  the album entry come with it, and the caption is written as the shutter goes. In the smoke tests it waits at once, so
  a run is the same every time.
- Comparing pictures after D3: the tests run with `-osfonts`, which gives the old look, so `refU360` still applies.
- Checked (items and under suites, with `-osfonts`, against `refU360`): shots, logs and dumps all pass; the saved photos
  decode to the reference's pixels (mean difference 0.001 of 255 or less, not flipped, the underwater tint as before).
- Still not done in B2: the renderer front itself (`renderer.render(scene, camera)` is what copies the Three-shaped scene
  onto the GameObjects, so it goes with B1).

## B1 begins: Dolphins makes and moves its own GameObjects (2026-11-24)

- `Dolphins` keeps no description: `Place` builds each dolphin as GameObjects with `Draw` (body, flukes in their own
  group, eyes, in the description's order, so the object tree and its names are as the Realizer made them), and `Swim`
  sets their positions and rotations directly. The per-frame `Realized.Apply` for the pod is gone. The splashes stay a
  particle pool (LiveTree) until the particles are native. 19 `THREE.` uses down to 13 (the shapes are still made with
  the Three-shaped generators, which is fine: MeshKit turns them into meshes once).
- Checked with `-osfonts` against `refU360` (base, dolphins, ships, island, islands): logs and all 68 scene dumps
  identical. One shot, `dolphins/21-dolphins-2.png`, differs only in the toast at the top: toasts fade with a UI Toolkit
  transition, which runs on real time, not on the test's fixed clock, so its fade at the moment of the shot varies from
  run to run (two repeats differed from each other too). The dolphins are pixel for pixel the same.
- Note on the rest of B1: the S34 perf pass already found the game faster than the reference everywhere but
  underground, so the remaining modules are mostly a code clean-up; the moving ones (Birds, Rain, Ecology, Boat,
  SeaTreasure, CoveNets, Scenery) are next, then the static ones.

## Steady UI pictures in tests; the Keys section names shared keys (2026-11-24)

- UI Toolkit's transitions (toasts, cards) run on real time, not on the test's fixed clock, so a picture taken during one
  depended on how long frames really took. In a test run (`TestClock.Fixed`) every element is made with a transition
  time of 0 (`UiNode`), so transitions end at once. Two runs of the same build now give the same pictures (ui, keyboard,
  dolphins: only the review-only loading shots differ). Players are not affected.
- The Keys section names a key that another row now shares ("G (also Map)", in red), unless both rows still have the
  game's own keys: Space swaps tools, swims up and brakes on purpose. `Controls.SharedWith`; two keyboard-test checks.

## B1: CoveNets and Rain move their own GameObjects; fonts made at build time dropped (2026-11-24)

- `CoveNets` and `Rain` are realised once, then `update` turns the banners and moves the rain group and its streaks
  itself; no `Realized.Follow`/`Apply` copy each frame. Checked against a fresh reference from this branch (`refB`, from
  03b6041, build kept in `C:\TidefinderParity\ref-branch`): core, world and saves suites, no FAIL; the rain draws pixel
  for pixel the same. The rain's dump hash differs because the vertex positions are rounded to a millimetre and the
  rotation is now worked out by `GameSpace.Euler`, not by the description's quaternion.
- Fonts made at build time (the rest of D3): tried and dropped. Baking the glyphs up front also brings in the faces'
  spacing pairs, so text came out a little wider and wrapped differently (the camp's upgrade cards, Help): a layout change
  on many screens for no measured gain (the runtime fills the atlas as text appears). The faces stay made at start-up.
- The rest of B1 is the hub (the binoculars' range check and the bow and pistol aim at description trees of the camp,
  scenery, boat, cart, sea, birds, ecology and sea treasure); those modules keep their per-frame copy until picking hits
  GameObjects.
- Testing from here (the owner's choice): a quick check per change (the one or two suites it touches), one full pass at
  the end.

## B1 hub, step 1: the bow and the pistol aim at Targets (2026-11-24)

- `Core/Target.cs`: a thing a shot can hit is a `Target` (where it is, its hit radius, its kind, the module's own handle).
  `Arrows`, `Bullets` and `Game.onBulletHit` only see Targets and dispatch by kind (castle, sea life, coconut, gull,
  crab) instead of reading flags off Three objects. The five modules still make their Targets from their hit meshes
  (`Target.Of`, made once per mesh, the same radius and position as before), so each can now drop its descriptions
  without the shooting code changing.
- Quick check against `refB` (birds, smash, fish, sealife, gear): logs and dumps identical, no FAIL.

## B1 hub, step 2: the binoculars' range reads the GameObjects (2026-11-24)

- `Picking.NearestDrawn`: the nearest drawn mesh along a ray under some GameObject roots, with a MeshCollider per shown
  mesh on the pick layer (under its own root, posed as its renderer each time, so nothing is added inside the game's
  objects). Back faces count, as most surfaces are double-sided. `Game.updateRange` uses it on the eight modules' roots
  (`Root` on Camp, Scenery, Boat, Birds, Ecology, SeaLife, GolfCart, SeaTreasure), so the range no longer needs their
  descriptions.
- New self-check (9 now): 400 rays, the range in whole metres from the GameObjects against the descriptions as before.
  2 rays differ: the old check hit a mesh inside a hidden group (not drawn), the new one does not. It caught two bugs on
  the way: ray directions not mirrored like their origins (`TransformDirection` ignores the root's -1 z scale;
  `TransformVector` does not) and back faces skipped.

## B1: gulls keep their state in numbers (2026-11-24)

- `Birds`: the gulls are still built as descriptions and realised once, but after that each gull's place, turn (with
  Three's rotation order: XYZ until it first flies, then YXZ), wing angle and visibility are fields on the Gull, and
  `Pose` sets its GameObjects from them (a hidden gull is left where it was, as the scene copy left it). The gull's
  `Target` is its own position (radius 1). No `Realized.Follow`, no description posed any more; `standing` and `shoot`
  take the gull itself. The binoculars' self-check leaves the gulls out of its description comparison.
- Quick check against `refB` (birds, gullshadow, selfcheck): logs pass, shots pass, selfcheck 9 of 9; five dumps differ
  only in the fifth decimal of some bounds (27.37288 against 27.37287 m: the rotation worked out by `GameSpace.Euler`),
  vertex hashes identical.

## B1: crabs keep their state in numbers (2026-11-24)

- `Ecology`: a crab's place, turn, body rock and visibility are fields on the Crab; `Pose` sets its GameObjects (the body
  rocks even while the crab is hidden, as before). Its `Target` is 0.12 up its middle, scaled with it. `shoot` takes the
  crab. The title menu's camera follows a position source (`menuTarget`) instead of a description, so it follows the
  crab's numbers (and still the shark's description). The binoculars' self-check leaves crabs out of its description
  comparison too.
- Quick check against `refB` (base, ui, gear, selfcheck, dograin): logs and shots pass, selfcheck 9 of 9; 2 to 4
  renderers per dump differ by 0.00001 m in their bounds (vertex hashes rounded to a millimetre flip with them).

## B1: the sea chests keep their state in numbers (2026-11-24)

- `SeaTreasure`: each chest's bob, roll and flag swing are fields on the Chest, and `Pose` sets its GameObjects; a taken
  chest is hidden. No per-frame copy. Quick check against `refB` (islands, ships, saves, selfcheck): logs and shots
  pass, selfcheck 9 of 9, dumps differ only by 0.00001 m in some bounds.

## B1: the boat poses itself from its numbers (2026-11-24)

- `Boat`: the hull's height, the wheel's and outboard's turn and the propeller's spin are fields; `Pose` sets the hull,
  wheel, outboard, propeller and nameplate GameObjects from them and from the boat's own place, heading, pitch and roll.
  `seat` (the helmsman's eyes) is worked out from the same numbers (before the boat first moves it stands at the origin,
  as the description did). No per-frame copy, no description posed.
- Quick check against `refB` (dolphins, pier, ships, saves, selfcheck): logs pass, selfcheck 9 of 9, dumps differ only
  by 0.00001 m in some bounds, three sea shots flagged with 0% of pixels changed.

## B1: palms and coconuts move their GameObjects directly (2026-11-24)

- `Scenery`: realised once; a coconut's GameObject is moved directly (shot: it leaves its palm for the scenery's group
  and falls; it shrinks away on the sand and grows back on its palm), and each palm's distance detail switches its
  coconut group and far-off fronds on the GameObjects. A coconut's `Target` reads where its GameObject is
  (`GameSpace.WorldPos`), so the aim needs no description. Quick check against `refB` (base, island, shadow, smash,
  gear): logs and shots pass, dumps differ by 0.00001 m at most.
- The binoculars' self-check: with the palms, boat, gulls, crabs and chests posing themselves, random rays rarely met
  what still has descriptions (the camp, the cart, the sea creatures), so its rays are now aimed at those. That showed
  two ways the old range differed from what is drawn: it measured to the invisible hit spheres round the sharks and fish
  (made for the aim) and to parts inside hidden groups. The range now reads what is drawn; the check compares drawn
  parts only: 9 of 9, 0 of 400 rays differ.

## Milestone pass (2026-11-24, at a1b3169)

- Full pass against `refB`: all 39 runs exit cleanly, selfcheck 9 of 9, mouse and keyboard 0 failed; no FAIL; 38 logs
  pass; the 38 flagged shots are all loading screens; 150 dumps flagged, all by 0.00001 m at most in some bounds (and the
  millimetre-rounded vertex hashes with them), from rotations now worked out by `GameSpace.Euler`.
- The golf cart is left as it is: it already poses its moving parts straight from its description with `Realizer.Pose`
  each frame (no generic copy), and a test reads its height there.
- Still copied every frame: the sea creatures and the camp (LiveTree, with lights, redrawn boards, shader values and the
  fish schools' mesh data), the underwater world, the cleaning bench, the tools in hand, and the dug-up items (picked by
  their descriptions).

## B1: sea life without the whole-tree copy (2026-11-24)

- `SeaLife` is realised once; at draw time only what moves is brought in line: the sea group, each shark and its tail,
  each school, each leaping fish (posed from their descriptions with `Realizer.Pose`, as the golf cart does), the shader
  values (the sharks' wave, the schools' clock) and a school's mesh when its fish data changes. LiveTree's whole-tree
  comparison is gone for it. Its state stays in its descriptions for now, which the aim and the binoculars' self-check use.
- Quick check against `refB` (ui, pier, dolphins, sealife, fish, selfcheck): logs pass, selfcheck 9 of 9, dumps within
  0.00001 m, one sea shot flagged with 0% of pixels changed.

## B1: the camp without the whole-tree copy (2026-11-24)

- `Camp` is realised once; at draw time only what changes is brought in line, each by name: the fire and tiki flames,
  the mail flag, the hammock and the radio's aerial (posed); the fire's, lantern's and camper's lights (strength, reach,
  shadows); what switches on and off (rug, string lights, sign, the extras, the drink on the stump, pinned photos, the
  lights near or far); and the materials (glows, the sign's text, the photos). What is added after it is placed (the
  aquarium, the skull, trophies) is realised under its parent's GameObject then (`Attach`). Parts welded into merged
  meshes have no GameObject of their own and are skipped. No LiveTree.
- Quick check against `refB` (base, ui, tour, shop, photo, rain, house, selfcheck): logs pass, selfcheck 9 of 9, dumps
  within 0.00001 m; two shots differ in 0.02 to 0.03% of pixels, both on the camp panel's highlighted tab outline (UI,
  not the camp's objects); to watch in the final pass.

## B1: the dog poses only what moves (2026-11-24)

- `Dog` is realised once; at draw time it poses itself, its body and head, jaw, tongue (shown or not), ears and every leg
  and tail bone by name. The Golden Bone is realised in the jaw's GameObject when bought. No LiveTree. Quick check
  against `refB` (base, dograin, house, tour): logs pass, dumps within 0.00001 m.

## B1: the islands, lighthouse and telescope without the whole-tree copy (2026-11-24)

- `Island` is realised once; at draw time it brings in line, by name, the palms' crowns, the map on the altar, the
  pennant, the foam, the lighthouse's moving parts (`Lighthouse.Moving`: door, dirt, beams, the night ships, each ship's
  hull and its pulsing lamps) and the telescope's dirt (`Telescope.Dirt`), and pushes every material of the island (the
  haze, the foam, the lamp, the beams, the ships' lights). One listed part on Lagoon Cay and one on Ember Rock were welded
  into merged meshes and have no GameObject of their own; the whole-tree copy could not move them either.
- Found on the way: the first try left out the ships' lamps, and the night-ship dumps moved by up to 8 cm. With them in:
  quick check against `refB` (islands, island, lighthouse, ships, telescope) logs pass, dumps within 0.00001 m.

## B1: the old house and its rain without the whole-tree copy (2026-11-24)

- `OldHouse`: the house and its rain are realised once. The house does not move (its materials are pushed, as the
  paintings' texture can come later); the rain brings in line, by name, its group (switched off while hidden), the
  puddle, the damp patch, the rings and the drip lines, refills the drips' mesh as they are rewritten, and pushes the
  fading. Quick check against `refB` (base, house, dograin, rain): logs pass, dumps within 0.00001 m.

## B1: the ocean without the whole-tree copy (2026-11-24)

- `Ocean` is realised once; at draw time the surface grid is moved under the viewer and shown or hidden, the foam band
  likewise, both materials pushed (the clocks, the foam's values) and the sea-bed depth channel rewritten when it changed.
  Quick check against `refB` (base, pier, island, dive): logs pass, dumps within 0.00001 m.

## B1: the sky without the whole-tree copy (2026-11-24)

- `Sky` is realised once under the world root; at draw time (in `Shine`, before the sun and the hemisphere light) the
  heavens, dome, stars, sun and moon discs and each cloud are shown or hidden and posed, and every sky material pushed
  (the dome's colours, the stars' fading, the clouds' tint). Quick check against `refB` (base, lighthouse, rain, opening,
  dive): logs pass, dumps within 0.00001 m.

## B1: sandcastles without the whole-tree copy (2026-11-24)

- `Sandcastles`: each castle is realised once (its parts are all built up front as hidden pivots); at draw time the
  castle and every part's pivot are shown or hidden and posed (rising, flying off, sliding, sinking, squashing as it
  washes away) and its sand's colour pushed. A removed castle's GameObjects are destroyed. Quick check against `refB`
  (smash, rain, holes, saves): logs pass, dumps within 0.00001 m.

## B1: dug-up items without the whole-tree copy (2026-11-24)

- `LootSpawner`: a find's GameObjects are made when it is uncovered (or swapped for the open hatch) and destroyed when it
  is collected, swapped or cleared; at draw time the group follows its visibility and each find is shown or hidden (land
  or sea floor) and posed (popping up, swelling when looked at). The group is realised at the first draw, as the
  whole-tree copy did, so its GameObjects keep their place among the world root's children: realised earlier, every dump
  path under it moved (`Group[2]` for `Group[5]`), which the first try showed.
- Quick check against `refB` (base, crust, gear, sweep, dive, holes, cove, saves): logs pass, dumps within 0.00001 m.

## B1: the cellar and the sea cave without the whole-tree copy (2026-11-24)

- `Underground` is realised at the first draw and again only when the tree's shape changed (`LiveTree.Shape`, now
  internal; the cave's shark, sea and clouds come after it is placed); otherwise it brings in line, by name, the torches'
  flames and lights, the beam, the door's leaves and seal, and the cave's moving parts (`SeaCave.Moving`: clouds, the
  shark in the bay, the hoard's heap and bell, the lights), with the lights' strength, reach and shadows, and pushes every
  material. Hidden (above ground) it is only switched off.
- Accepted difference: the dumps' paths under the world root are numbered one lower or higher from here (items
  `Group[4]` for `Group[5]`, the camera `PerspectiveCamera[6]` for `[5]` later on). The whole-tree copy rebuilt the
  cellar's root once more during loading, which moved it among the world root's children; it is now made once. The same
  objects, renderer counts and poses (bounds within 0.00001 m); only sibling numbers differ. Quick check against `refB`
  (base, photo-alt, saves): logs and shots pass.

## Fix: the camp's aquarium (2026-11-24)

- The camp conversion (efb9517) left the aquarium's catches hidden and still: the tank shows a fish or creature by
  switching it on when it is caught (and adds new ones at runtime), and it swims them each frame; the camp listed neither.
  The camp quick check had not run the fish suite, where the aquarium shots lost 72 renderers. Now the camp is rebuilt
  whenever its shape changes (as the whole-tree copy did; this replaces `Attach`), and other modules can hand it moving
  parts to keep in line (`Camp.follow`; the aquarium gives its fish and tails and each creature's wrapper and parts,
  `Aquarium.Moving`). Checked against `refB` (fish, dive, shop, tour): renderer counts equal, logs pass, dumps within
  0.00001 m once the world root's top-level groups are paired by what they hold.

## B1: the world under the sea without the whole-tree copy (2026-11-24)

- `Underwater` is realised at the first draw and again only on a change of shape; otherwise it brings in line, by name,
  the surface, the kelp, its fish and tails, the spears, the decoration's moving parts (`UnderwaterDecor.Moving`:
  shimmer, sun patch, light rays, the shoals' fish, the jellyfish) and every creature's own tree (`SeaCreatures.Moving`:
  an animal's tick only moves parts of its own tree), and pushes every material. Hidden (on land) it is only switched off.
- Accepted difference: the world root's top-level groups are numbered differently in the dumps (each is made at a
  different moment from before). Paired by what they hold, every renderer matches, within 0.00001 m (dive, sealife,
  fish, photo-alt, base). `dumpdiff2.py` in the scratchpad does that pairing; the final pass uses it.

## B1: the whole-tree copy is gone (2026-11-24)

- `Native/Tracked.cs` replaces `LiveTree`: a tree realised when it is made (as LiveTree was), made again only on a change
  of shape, switched off while hidden, and each frame only the parts its owner names are posed and shown, their point
  lights set, the named geometry refilled when rewritten, and the tree's materials pushed. `Tracked.PlaceAll` names every
  part, for small trees whose parts all move or change: the detector's overlays, the dig rings and particles, arrows,
  bullets, hearts, the shadow proxy, the binoculars' beam, the tools in hand (claimed, on the camera's rig), fishing,
  the opening, the grate and the modules' particle pools. The cleaning bench names its pieces (the pivot turning the find,
  the dust and sparkles refilled, the brush), the scene view (`NativeView`) tracks a small scene's loose parts (a find set
  up for its photo). `LiveTree.cs` is deleted; its shape hash is `Tracked.Shape`.
- Found on the way: LiveTree realised in its constructor, not at the first draw; realising later moved GameObjects among
  their siblings and left the item-photo scene empty on its first frame. Tracked does as LiveTree did.
- Checked against `refB` (base, gear, birds, smash, sweep, crust, photo, opening, fish, dolphins, dive, selfcheck): logs
  pass, selfcheck 9 of 9, every top-level group pairs up, dumps within 0.00001 m.

## Full pass after LiveTree's removal; fix: the bench's crust (2026-11-24)

- Full pass against `refB` at e78a067: all 39 runs clean, selfcheck 9 of 9, mouse and keyboard 0 failed, 38 logs pass,
  no shot FAIL. Dumps paired by top-level group (`dumpdiff2.py`, now splitting every scene's root, not only Main): all
  within 0.00001 m except one group, the cleaning bench's pivot after the telescope is cleaned, with one renderer too
  many: the find's crust, which is switched off once clean, stayed on, because the pivot's tree was kept in line by its
  root only. The pivot is now tracked whole (`Tracked.PlaceAll`). Rechecked (telescope, crust, islands, lighthouse): all
  groups pair, within 0.00001 m.

## B2: the renderer front is native (2026-11-24)

- `THREE.WebGLRenderer` is `NativeRender.GameRenderer` (`Native/Render/GameRenderer.cs`): made with `onScreen` instead of
  the browser's parameters; the canvas wrapper (`domElement.toDataURL`) is `Snapshot()`; the browser's no-ops are gone
  (`compile`, `compileAsync`, `clear`, `forceContextLoss`, `setClearColor`, `getSize`, `getRenderTarget`,
  `outputColorSpace`). What it still has is used: render, render targets, photos, pixel ratio, shadows, tone mapping and
  exposure (read by the shaders), the clear colour.
- The frame loop: no more `setAnimationLoop`. The host (`GameHost.Update`) runs `Game.frame` through the renderer's
  `Tick(frame)` once the game has started (`Game.running`), and only with the game's own renderer, so a reloading game's
  old frame never runs in the new scene (which `Teardown` used to ensure by clearing the loop).
- `MaterialCache`, `TextureCache`, `UrpSettings` and `FrameStats` moved from `THREE.Render` to `NativeRender` (with
  aliases in those files so their names keep meaning what they meant inside THREE); the materials' `bridgeEntry`,
  `bridgeKind` and `bridgeGen` are `nativeEntry`, `nativeKind` and `nativeGen`.
- Quick check against `refB` (base, ui, saves, newgame, photo, crust, keyboard, selfcheck): all runs clean, logs pass,
  selfcheck 9 of 9, keyboard 0 failed, dumps paired within 0.00001 m.

## U10, step 1: a UiNode is a VisualElement (2026-11-24)

- `UiNode` derives from `VisualElement` instead of wrapping one: one object per element, not two. `ve` stays as a
  property returning the element itself, so calls through it reach UI Toolkit's own members (`ve.Add`, `ve.Clear`,
  `ve.Focus`) and the same calls on the node reach the node's (`Clear`, `Focus`, `Blur` and `Children` are marked `new`;
  C# prefers a derived class's method that fits, so `node.Add(child)` stays the node's). The constructor's body is
  `Init`.
- UXML: `El` is a `UiNode` now; its `tag` and `text` attributes (`elTag`, `elText` in code) are applied by
  `Ux.Adopt` through `El.Adopted`, so a screen's elements are the nodes themselves, not copies adopted into wrappers.
- Quick check against `refB` (base, ui, tour, settings, saves, pad, layout, mouse, keyboard, shop): all clean, logs and
  shots pass (one shot flagged with 0.03% changed on the camp tab's outline, as before), mouse and keyboard 0 failed.

## U10 step 2: the SVG rasteriser leaves the game; UiX goes

- `SvgRasterizer` moved to `Assets/Editor` (only `IconBaker` uses it now). At run time `UiNode.Svg` asks the new
  `SvgIcon.Get`, which only works out the drawing's display size and returns the baked picture from `IconAtlas`
  (warning once for a drawing that was not baked). `TexturePainter.ToUnityRGBA` is public so the editor can reach it.
- `UiX` deleted: its last user, `LoadingScreen`, builds its boxes with a small helper of its own.
- Quick check against `refB` (base, newgame, ui, shop, gear, pad): logs pass, every scene dump pairs exactly
  (dumpdiff2: 0 differing), shots pass except the animated loading screen (review-only) and the known camp-tab outline.

## U10 step 3: TextMarkup goes; one page root; the DOM-era notes

- `TextMarkup` deleted: no game text uses markup or entities any more (checked in code, UXML and data), so `UiNode.Markup`
  sets plain words and the toast no longer decodes entities.
- `Page`: the `html` > `body` pair is one root, `.page`, which carries the game's state classes. `.t-html` and `.t-body` in
  Style.uss became `.page` (same specificity, so no rule changes winner).
- Notes: the Style.uss header, UiNode, UiAnim, Page, UiRoot and the docs (MIGRATION.md, PORT_STATUS.md) no longer describe a
  DOM layer, a bridge or files that are gone.
- Kept, on purpose: the `.t-<tag>` classes are the text roles (h1, h2, p, b, small...) the type scale is written against;
  renaming them would change no picture and touch every screen. `UiAnim` stays as the clock-driven keyframe animations
  (USS transitions run on real time, which the test pictures cannot follow); only its DOM-era wording went.
- Quick check against `refB` (base, ui, tour, settings, saves, pad, layout, mouse, keyboard, shop, newgame, photo, dive,
  land, fish, house): every scene dump pairs exactly, logs pass, mouse and keyboard 0 failed; shots flagged only for the
  animated loading screen and the known 0.02-0.04% tab-outline flicker.
