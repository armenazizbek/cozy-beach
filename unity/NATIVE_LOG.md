# Native rewrite log

One entry per step of `NATIVE_PLAN.md`: what changed, the hours spent by activity (for the re-projection in section 6),
the comparison results, and any differences accepted. Newest last.

## S0: parity harness and baseline (2026-11-23)

**What changed (test-only; nothing differs without `-autoshot`)**

- `Core/TestClock.cs`, new: the clock the game reads (`JS.Now`, the page's timers and CSS animations, the smoke test's
  waits).
  - Real time by default.
  - With `-autoshot-fixedstep` it moves on 1/60 s per frame and sets `Time.captureDeltaTime`.
  - It stands still while `Prebuilt` waits for its worker thread. Counting those frames put every later moment of the
    run at a different time: the title camera moved between runs.
  - It switches off the real mouse, keyboard, pen and touchscreen. Mouse movement over the window turned the view in one
    run and not the next.
- `-autoshot-seed <n>` (`JS.UseTestSeed`, `JS.SeedThread`):
  - Each `Prebuilt` piece draws from a sequence of its own (seed·1000 + index), whichever thread makes it.
  - The main sequence restarts (seed + 1) once the big pieces are made (`GameHost`).
- `-autoshot-scenedump` (`Core/SceneDump.cs`, new): writes `<shot>.scene.json` beside each screenshot, covering every
  active renderer (sibling-indexed path, world bounds, vertex and triangle counts, a hash of the world-space vertices
  rounded to a millimetre, shadow flags, sorting order, key material values), every light, and the Cozy shader globals.
- `AutoShot`: the waits and `Measure` follow `TestClock`. A `harness:` line says which flags are on. A
  `loading pacing:` line gives the worst loading frame, the frames over 33 ms and the time to ready, measured in real
  time.
- `GameHost`, `InputManager`: under `-autoshot-fixedstep`, losing window focus neither pauses the game nor clears the
  smoke test's held keys.
- `Tools/Parity/`: `suites.json` (36 runs in 6 suites), `run_suites.ps1`, `run_perf.ps1`, `compare.py` (pictures,
  log facts, new errors, scene dumps, perf, HTML report), `thresholds.json` (calibrated) and a README.

**References** (outside the repository, both built from this step's code)

- `C:\TidefinderParity\ref-mono`: the quick Mono build, the reference for pictures, facts and scene dumps. A quick
  build takes 21 to 26 s once the Library is warm, and 86 s the first time.
- `C:\TidefinderParity\ref-il2cpp`: the release build, the reference for perf (78 to 83 s to build).
- Reference shots: `C:\TidefinderParity\shots\ref`. The noise-floor pass is `C:\TidefinderParity\shots\ref2`.

**Results**

- **Two full passes of all 36 runs on `ref-mono`: every run exited cleanly.**
  - Shots: 400 of 438 are identical. Three differ by a few pixels (mean 0.00, SSIM 0.986 to 0.989): `dive/3-sea`,
    `fish/7-pier-from-beach`, `tour/3-sea`. 35 are `0-loading.png`, which is review-only.
  - **All 438 scene dumps and all log facts are identical.**
  - `compare.py --calibrate` wrote `thresholds.json`. Checked against it, the two passes give 403 PASS, 35 review-only
    and 0 FAIL.
- **Perf:** one round of `ref-il2cpp` against itself (`C:\TidefinderParity\perf\S0-self`) varied by up to about 8% in
  p99, by 1 to 3 late frames, and by 0.01 ms on a 0.13 ms scene. `compare.py --perf` now has absolute slack (game
  0.05 ms, p99 0.5 ms, 3 late frames, ready 0.3 s), so that run reports no regressions. Dev PC, 1280x720, novsync, IL2CPP:
  - camp: 495 fps, game 0.73 ms
  - beach, running: 471 fps, 0.88 ms
  - boat: 434 fps, 0.91 ms
  - diving: 730 fps
  - lighthouse at night: 639 fps
  - underground: 1,332 fps
  - ready after 5.4 s
- **Fixed along the way:** the first full pair of passes differed badly at the camp, in the lighthouse sea view and on
  every UI shot. The real mouse had turned the view; the cause and fix are in TestClock above.

**Hours by activity** (for calibration)

- Harness code (clock, seed, dump, smoke-test hooks): about 1 h.
- Tools (`compare.py`, run scripts): about 1 h.
- Builds, runs and finding the nondeterminism (loading frames, then real mouse input): about 1.5 h, mostly waiting on
  runs.

## S1: spikes (2026-11-23)

**Question 1: does option A (a mirrored world root with Three's numbers unconverted) draw the same picture?
Yes.**

The spike is `-spike-mirror` in the bridge (`Three/Bridge/Mirror.cs`). In this mode:

- every scene root is scaled (1, 1, −1);
- local transforms and mesh positions and normals go to Unity unconverted, with the original triangle winding;
- baked instance origins and bind poses (`Matrix4.ToUnityRaw`) are unconverted too;
- the camera renders from an "eye" outside the root, placed in Unity's axes;
- the sun and spot lights are lifted out of the root and placed in Unity's axes;
- point lights stay under the root.

So the whole game ran the way the native code will, and was compared with `ref-mono` over all 36 runs:

- **First pass:** placement was exact (every renderer's world bounds and world-vertex hash matched), but shading
  differed wherever a shader did maths in Three's object axes. That was the palms first, then whitecaps, grass, the
  shark, fish schools and ocean foam.
- **The shader audit (plan 2.4), done:** every hand-written object-space z flip now reads one global, `_CozyThreeZ`,
  in `CozyCore.hlsl`. The bridge sets it to −1 (meshes mirrored, as before) or +1 (unconverted under a mirrored root).
  - Shared passes: `objectPos` in `CozyStandardPass`, `CozyBasicPass` and `CozyShadowPass` (x2).
  - `CozyPalmSway` (sway), `CozyFishSchool` (swim), `CozyOceanFoam`, `CozySharkBody` (bend and its normal).
  - `CozyWhitecaps` (patch shape and placement), `CozyGrass` (instance phase and sway).
  - World-space flips (`CozyBeach` duv, `CozyOcean` sea position and normal) are correct in both modes and stay as
    they are.
- **Mirror mode after the audit:** 398 PASS and 40 REVIEW of 438 shots. The 40 are the 35 loading shots and 5 shots
  within noise (mean 0.00, SSIM 0.977 to 0.990). Facts all match. The scene dumps match except that the lifted lights
  now sit at the top level under another path, which is expected.
  - This covers shadows (including `-autoshot-shadow` and `-autoshot-gullshadow`), the skinned dog, sprites, points
    and particles, baked instances (grass, tracks, whitecaps, the cave hoard), the lifted sun and the golf cart's spot
    light, the camp's point lights, the tools in hand, the workbench scene and the item photos.
- **Normal mode after the audit** (regression check of the shader edits): 401 PASS and 37 REVIEW. The 37 are the 35
  loading shots and 2 within noise. All 438 scene dumps are identical, so today's game is unchanged.

**Question 2: what does placing the world cost on the main thread?** Dev PC, Mono, three repeats, throwaway probe
removed afterwards:

- 3,000 GameObjects, each with a MeshFilter, MeshRenderer and shared material, under a mirrored root: 6.9 to 7.4 ms,
  about 2.3 µs each. The whole world (about 3,600 objects) is about 8.5 ms.
- 300 meshes of 600 vertices through `Mesh.AllocateWritableMeshData`: filling takes 8.9 ms, which a worker can do;
  applying on the main thread takes 0.3 to 0.5 ms (about 1 µs each).
- A 1024x1024 RGBA texture upload with mipmaps takes 1.6 ms; a 256x256 one takes 0.08 ms.

Conclusion: the loader's default budget of 4 ms per frame (plan 2.11) places the whole world in 3 or 4 frames. Placing
on the main thread is not a risk to the loading screen. On a Deck, at about 3 times slower, it is still only about 10
frames.

**Question 3: `Graphics.RenderMeshInstanced` with mirrored matrices.** Not needed on the parity path, because instanced
meshes stay baked (plan 2.2). Deferred to S34, where track and whitecap instancing would be tried.

**Decisions from the spike:**

- **Option A is confirmed** (decision 1 holds).
- **A simpler path than the plan's mounts.** In S2 the bridge switches to mirror mode for good (the reference shots
  are unchanged, as shown above). Native objects can then hang **directly** under bridge objects in game numbers, so
  the `(1, 1, −1)` junctions of `NativeMount`/`ThreeMount` (plan 2.12, S5) are no longer needed. A native subtree is
  simply parented to its Three parent's GameObject, and a Three subtree to a native Transform. S5 shrinks to the
  `Proto`/`Realizer` loader.

**Hours:** about 2.5 h, most of it waiting on three pairs of full passes (about 13 minutes each).

## S2 to S5: native kit beside Three (2026-11-23)

**What changed.** Everything lives in `Assets/Scripts/Native/` unless noted.

- **S2:**
  - The bridge draws the option A way **for good** (`Three/Bridge/Mirror.cs`; `-bridge-legacy` brings back the old
    conversion for comparison).
  - `World` (the main scene's root, scaled (1, 1, −1); `World.Of(object3D)` gives the bridge's GameObject to hang
    native children from).
  - `GameSpace`: `ToUnity` and `FromUnity` for points and rotations, `Euler(x, y, z, order)` in Three's orders,
    `LookAt` and `LookAtAsCamera`, `WorldPos`, `ToLocal`, `Walk`.
  - `ColorUtil` (hex to linear exactly as Three, and `ToSRGB`), `GameClock` (Three's Clock).
  - `JS.cs` moved from `Three/` to `Core/`.
  - `SelfCheck` (`-autoshot-selfcheck`, suite `kit`) checks the kit against the Three code it replaces.
  - Bridge fix along the way: lights lifted out of the root are switched off only by their own scene's sync (the item
    photos' sun had gone dark).
- **S3, `MeshKit`. Scope changed from the plan.** The generators (box, cylinder, lathe, tube, extrude, merge, ...) are
  plain C# maths and stay where they are; they move under the kit by a rename in S25. `MeshKit` turns their geometry
  into Unity meshes with the bridge's exact layout:
  - `ToMesh` (with groups), `ToLines`, `Refill`, `BakeInstances`/`Bake`, `PointQuads`/`RefillPoints`;
  - `Write`, which fills a `Mesh.MeshData` and is safe on a worker thread.

  This saves porting about 2,800 lines and makes "the same vertices" true by construction.
- **S4.**
  - `MaterialKit`: the same shortcut. The material description classes are plain data and serve as the specs; `Get`
    and `Push` return the Unity material through the bridge's exact rules and share it with bridge objects.
  - `Draw`: native GameObjects made as the bridge made them, with the same names and renderer settings.
  - `ShadowPolicy`: the small-caster rule, re-decided when the preset changes (`GameHost` ticks it).
  - `Atmosphere`: the hemisphere, ambient and environment globals are owned per scene; native shares are summed with the
    bridge's Three lights in `SceneSync.ApplyGlobals`, and a native sun is used when the scene's Three lights have none.
  - `Lights`: `Make`, `Set`, `Shadows`, `Aim`, `Cone`, with the bridge's light settings.
- **S5.**
  - `Realizer`: the built description tree (Three objects, plain C#, which `Prebuilt` can still make off-thread) is the
    plan's "Proto". `Realize` and `RealizeSliced` turn it into native GameObjects (budget 4 ms per frame) and return
    the map from description to Transform.
  - `Object3D.native`: marks a realized description, so `SceneSync` leaves it and everything under it alone, apart from
    hanging it under its parent's GameObject.
  - **The plan's junction mounts are not needed** (see S1). Native objects hang under bridge GameObjects directly, and
    bridge objects under native ones, in the same numbers.

**Checks** (`-autoshot-selfcheck`, 11 of 11 ok):

- `GameSpace.Euler` against `Quaternion.setFromEuler` (all 6 orders, 1,200 random angles).
- `LookAt` and `LookAtAsCamera` against `Object3D.lookAt`.
- `ColorUtil.Linear` against `Color(hex)` (2,000 colours).
- The world root keeps game numbers.
- `MeshKit` against the bridge's meshes, array for array: 21 generator cases, groups, colours and extra attributes, draw
  range, lines and strips, baked instances, point quads, and the off-thread `Write`.
- **The whole live world realized as a hidden twin: 6,982 objects, and all 2,648 drawn renderers match the bridge's**
  (mesh, materials, shadows, draw order, layer, world bounds).

**Two bridge quirks the twin found (the native world keeps the look):**

- The warm-up (`Game.sendEverythingToGpu`) switches culling off on everything and back on, but the bridge never
  re-reads the inside of a static group. **About 1,570 static objects stay unculled for the whole game**, always drawn
  and shadow-cast even off-screen. The pictures are the same, but it costs time; realized natively they are culled
  again. Expect this to show as a perf gain, and watch for it on the Deck.
- Skinned meshes (the dog) always received shadows: the bridge never overwrote Unity's default. The native dog keeps
  receiving them.

**Hours:** about 3 h of code plus build and run waits.

## S9: pilot (Stones, Flags, Pier, Whitecaps), plus the golf cart from S10 (2026-11-24)

**Three ways of going native, now tried:**

1. **Direct native code (Stones, Flags).** The module makes GameObjects itself with `Draw.Mesh`, `MeshKit` meshes and
   `MaterialKit` materials. Placement maths, seeds and shapes are unchanged. Flags plants and removes GameObjects as the
   player does, and waves its cloth through `GameSpace.Euler`.
2. **Built description, placed by the `Realizer` (Pier).** The building code is unchanged (merges included);
   `Pier.Place()` realizes it once. Nothing on it moves.
3. **Description plus `Realizer`, with the module moving the native Transforms (golf cart).** The update code keeps
   its maths. `Sync()` copies the moving parts' poses (body, wheels, steering wheel) onto the realized Transforms
   (`Realizer.Pose`). The headlight is a native spot light outside the world root, aimed from the cart each frame. The
   description is still updated because the binoculars' range finder raycasts against it until S7.
   - Whitecaps is a native mesh refilled when its patch data changes (at most 10 Hz); its uniforms are pushed through
     `MaterialKit.Push` every frame.

**Found and fixed along the way:**

- **Native objects need the world's layer.** The main camera only draws layer 8, so the first native stones were
  invisible. The scene dump caught it (`layer 8 -> 0`). `Draw` now puts objects on `World.LayerUnder(parent)`.
- **`Game.hideLand` (diving) hid the scene's children only.** It now also hides native land objects under the world
  root, and puts them back. Caught by the dump of `fish/7-net-in-hand` (49 stones too many).
- **Lights outside the root don't follow their object's hiding.** The cart's `Refresh()` runs after `hideLand`.
- **`compare.py` pairs renderers by content first** (world-vertex hash plus vertex count), then by path. Native
  objects change sibling order, and path-first matching paired the wrong objects.

**Results, full pass (all 37 runs):** every run exits cleanly; self-check 11 of 11; **438 of 438 scene dumps identical
to the reference**; 397 shots PASS and 42 REVIEW, all of them loading shots or within noise (mean 0.00). Golf cart
(base, holes, newgame, dive, house): 47 PASS, 6 REVIEW (loading shots), every dump identical, the `holes` facts
unchanged.

**Calibration (plan 6.4).** The pilot took about 1.5 h for 567 lines, verification included; the golf cart (225 lines)
took about 30 minutes. The plan budgeted 1.5 to 2.5 days for the pilot. The measured rate is far above the fast end of
section 6, mostly because:

- the kits reuse the generators and material rules, so nothing in the look has to be re-tuned;
- the `Realizer` covers static content in one call;
- the scene dump says exactly what differs.

**Re-projection:** the world and mechanics steps (S10 to S24) look like **days rather than weeks**, about 1,500 to
2,500 lines a day. The UI track keeps its estimate until its first screen is measured. The fixed cost per step is now
the verification passes (about 14 minutes for all 37 runs).

## S10: small props (2026-11-24)

- **Native:** golf cart (committed with S9), SeaTreasure, Dolphins, CoveNets.
  - Each realizes its description (`Place`) and names its moving parts: `Realized.Follow(...)`, with `Apply()` once per
    update. That copies only those parts' pose and visibility: chests and flags; the pod and each dolphin's flukes; the
    cove's banners.
  - Particle pools (treasure sparkles, dolphin splashes) stay drawn by the bridge, hung straight in the scene, until
    S24. Their parent groups never move; the splashes show and hide with the pod by hand.
- **Not converted:**
  - `SeaModels` and `ShipKit` are geometry helpers that other modules call; they stay with the generators.
  - **Moved:** Telescope to S16 (it hangs inside the island, built off-thread), Aquarium to S15 (a clickable camp prop).
- **Check** (selfcheck, base, dolphins, islands, cove, dive, land): every run clean, 81 PASS and 7 REVIEW (the loading
  shots), **87 of 87 scene dumps identical**.
- **Hours:** about 45 minutes.

## S11: weather, tracks, critters (2026-11-24)

- **Native:**
  - **Rain:** open and sheltered rain realized; the leaning, sliding parts are followed; opacity pushed each frame.
  - **Tracks:** the three decal sets are baked instanced meshes. A set re-bakes **at draw time** when a mark was laid,
    through the new `NativeFrame.BeforeDraw` hook: the dump had caught a one-frame lag when baking in `update`.
  - **Grass:** baked when scattered, re-baked when digging clears tufts, real culling bounds; sway time pushed.
  - **Ecology:** tufts and crabs are realized as they are added; crabs follow body and wobble.
  - **Birds:** the flock is realized; each gull follows its body, wings, folded wings and legs.
  - **Sandcastles:** a `LiveTree` per castle (the new helper for small trees that change shape). At draw time it
    re-realizes when parts are added or removed, copies every part's pose and visibility, and pushes its materials.
- **`compare.py`:** reference renderers with the 100 km "never culled" bounds (the bridge's static-group warm-up
  quirk, S2-S5) are compared without their bounds.
- **Full pass:** every run clean; 392 PASS and 47 REVIEW (loading shots and within-noise); 428 of 438 dumps identical.
  - The 10 differing dumps are at the cleaning bench. While it is open the game is paused and the bridge stops copying
    the main world, so its crab copies stay frozen, while the native crabs had already been hidden as too far away. The
    main world isn't drawn there; every bench picture passes.
- **Hours:** about 1.5 h.

## S12: boat and dog (2026-11-24)

- **Native boat:** realized; it follows its hull, wheel, outboard, propeller and nameplate; the marigold colours are
  pushed when bought. The wake stays a particle pool in the scene until S24.
- **Native dog:** the whole tree (skinned legs and tail with their bones, head, jaw, tongue, ears) is a `LiveTree`
  synced at draw time, and re-made when the Golden Bone is added.
- **`World.Land`:** every native module now hangs under one `Land` group beneath the world root. Diving
  (`Game.hideLand`) switches it off as one. The dog's `LiveTree` copies its description's visibility (on) every frame,
  which had undone the dive's hiding: 18 dog renderers showed underwater in the dump. A module's own visibility can no
  longer override the dive.
- **Check** (selfcheck, base, newgame, islands, dolphins, dograin, photo, photo-alt, house, leap, fish, dive): every
  run clean, 132 PASS and 16 REVIEW (loading shots and within noise), **147 of 147 dumps identical**.
- `ModernShips` and `OldShip` are builders the lighthouse and the opening call; they convert with S17 and S24.

## S13 and S14: terrain, atmosphere, scenery (2026-11-24)

- **Sky** (committed separately, 56ff807): a `LiveTree` under the world root, plus a native sun and hemisphere light
  through `Atmosphere`.
- **Ocean:** a `LiveTree` under `World.Land`, synced at draw time. A refresh of the sea-floor depth rewrites only that
  vertex channel (`MeshKit.RefillExtra`), as the bridge re-sent one attribute.
- **Beach:** the ~100 chunks are realized once. At draw time a chunk whose geometry content version changed (dug,
  smoothed by rain, new detail level) has its Unity mesh refilled. The surround and skirt are realized once.
- **Scenery (S14):** realized once. It follows only what changes: the coconuts, which fall, roll, shrink and grow back
  on their palm (`Realized.Apply` now moves a followed part when its description changes parent), each palm's coconut
  group and far half of its fronds. Mountain haze colours and the frond sway clock are pushed as they change.
- **A rule found the hard way.** The first S13 pass failed widely (18 FAIL, 4 logs): the find layouts, the catches and
  a gull's height had changed. Moving `createSurround()`/`createSkirt()` earlier in the build changed when they draw
  their random numbers, and so every later draw of the seeded sequence. They now run exactly where the scene list
  called them. **Rule: a conversion must never change when anything draws from `JS.Random`.** The harness's facts
  check is what caught it.
- **Full pass:** every run clean, 11 of 11 self-checks, **all 35 logs identical**, no FAIL, 390 PASS and 49 REVIEW
  (loading shots and within noise), 428 of 438 dumps identical (the 10 others are frozen bridge copies while the bench
  is open; those pictures pass).

## S20: underground, with the sea cave (2026-11-24)

- **Native underground:** the cellar, the passage and the sea cave behind the door (`SeaCave` hangs inside the
  underground tree) are one `LiveTree` under the world root (not `World.Land`: it stays while diving), synced at draw
  time.
- `Realized.Apply` now carries a followed point light's colour, intensity, range and shadows, so the torches flicker
  and the side switching works.
- **Check** (selfcheck, base, land, house, dograin, photo-alt, which goes into the underground room): every run clean,
  **68 of 68 dumps identical**, pictures pass or are within noise.

## S19: under the sea (2026-11-24)

- **Native:**
  - **Underwater:** reef, kelp, wreck, fish schools, plus SeaCreatures and UnderwaterDecor inside it (SeaFlora is its
    coral builder). One `LiveTree` under the world root, synced at draw time. While the tree is hidden (on land) a sync
    only switches its root off.
  - **SeaLife:** sharks, schools, the pier's fish; a `LiveTree` under `World.Land`.
- **Particle pools left in the scene now sit in a wrapper group per module**, and only the wrapper mirrors the module's
  visibility (and `World.Land` for modules under it). Pools switch themselves off while they have no live particles, and
  mirroring onto the pools themselves had overridden that. The dolphins' splashes from S10 had the same bug and are
  fixed too. The underwater wrapper is on the dive's keep list, as the underwater group was.
- **Check** (selfcheck, base, dive, sealife, fish, photo-alt, cove, islands, dolphins, opening, leap): every run clean,
  **133 of 133 dumps identical**, pictures pass or within noise (mean difference 0.06 at most).

## S15: camp, with the aquarium (2026-11-24)

- **Native camp:** camper, fire, shop, boards, lanterns, radio, every prop placed in it, and the aquarium (a camp prop).
  One `LiveTree` under `World.Land`, synced at draw time: lantern light and flicker, windows, redrawn boards, props added
  later. The descriptions stay current for the clickables' raycasts.
- The fire's embers stay a particle pool until S24, in a wrapper group standing where the camp stands, because they
  are emitted in the camp's frame.
- **Check** (selfcheck, base, newgame, shop, photo, tour, ui, dograin, rain, fish, gear): every run clean, **124 of 124
  dumps identical**, pictures pass or within noise.

## S16 and S17: islands, telescope, lighthouse and night ships (2026-11-24)

- **Native islands:** each island with its landmark is one `LiveTree` under `World.Land`, synced at draw time. That
  covers Lagoon Cay's telescope and Ember Rock's lighthouse with its lamp, beams and the ships on the night horizon
  (built by `ModernShips`, added into the island). It follows the palms' sway, the haze, the beams turning, the ships
  sailing, the door, and the dirt being cleaned off.
- **`compare.py`:** "never culled" bounds are recognised from 5 km, not 10 km. Under a scaled parent the bridge's
  100 km box shrank to about ±9.7 km, and no real object reaches 5 km (the largest is the beach surround, 1.3 km).
- **Check** (selfcheck, base, islands, island, telescope, lighthouse, ships, cove, crust): every run clean, pictures
  pass or within noise, 112 of 122 dumps identical. The 10 others are the paused cleaning-bench views (frozen bridge
  copies); their pictures pass.

## S18, S21 and S22: old house, finds, tools in hand (2026-11-24)

- **Old house (S18):** the house (paintings painted by `OldPictures` into one texture) and its indoor rain are
  `LiveTree`s under `World.Land`.
- **`LiveTree` refills meshes whose geometry changed** (the drip lines in the house), by the geometry's content version
  and each part's own topology (triangles, lines, strips, point quads; `MeshKit.RefillLines`).
- **`LiveTree` no longer claims its descriptions** (`Object3D.native` stays null), because a part may leave the tree for
  a scene the bridge still draws. With `claim: true` it does claim them: for trees that stay inside a bridge scene,
  giving claims up when parts leave.
- **Finds (S21):** `LootSpawner`'s buried and sunken finds (models from `LootGenerator`) are a `LiveTree` under the world
  root; the dive keeps them, as before. The cleaning bench builds its own copies.
- **Tools in hand (S22):** the view model stays the camera's child in the description and is a *claimed* `LiveTree`. The
  bridge keeps its native root hung under the camera's GameObject and skips the rest. The detector's overlays, the dig
  rings, the arrows, the shadow proxy and the fishing line are `LiveTree`s under `World.Land`
  (`LiveTree.Place(desc, parent)`). Bullets (a trail particle pool) and the dig particles stay with the bridge until S24.
- **Full pass:** every run clean; 11 of 11 self-checks; **all 35 logs identical**; no FAIL; the largest non-loading
  picture difference is a mean of 0.04/255; 428 of 438 dumps identical (the others are paused bench views).
- **Where this leads (S25):** the description classes (Object3D, Mesh, Group, geometries, materials) stay as the game's
  plain-C# model of the world. Builders, raycasts and update maths use them, and every frame of drawing is native. S25
  moves them out of `Three` and deletes the bridge. Turning update code into direct Transform writes can follow module
  by module, with no effect on the look.

## S23 and S24: the cleaning bench, particles, the opening (2026-11-24)

- **Particles (S24):** every particle pool is native. `LiveTree` refills point meshes when their geometry changes, so
  pools and their wrapper groups are simply `LiveTree`s:
  - the camp's embers, the dolphins', sea life's and treasure's sparkles and splashes, the boat's wake, the dig
    particles, bullets and hearts under `World.Land`;
  - the bubbles and motes under the world root.
- **Also native (S24):** the opening (placed when it starts, removed after its fade with the new `LiveTree.Remove`),
  the hatch's grate, the lighthouse-climb beam, the smoke test's display items. **The main scene now holds only the
  camera**, whose tools in hand are drawn natively.
- **Cleaning bench (S23):** what it draws (bench, cloth, the find on its pivot, particles, sparkles, the brush) are
  claimed `LiveTree`s on the bench's own layer (`LiveTree` gained a layer option). The bench's three lights stay in its
  bridge scene for now.
- **`compare.py`:** renderers in other scenes (the bench, the item photos), extra or missing, are not counted, because
  only those scenes' cameras draw them. The bridge made them on first use and kept a find's objects after the bench
  closed; the native bench exists from the start and lets them go.
- **Full passes (S24, and S23 on top):** every run clean, all logs identical, no FAIL, the largest non-loading picture
  difference 0.02/255, 428 of 438 dumps identical (the 10 others are paused bench views).

## S8: the main world's frame without the bridge (2026-11-24)

- **`NativeView`:** the renderer hands the main scene to `NativeView.Prepare` instead of `SceneSync.Sync`. It sets the
  Cozy shaders' globals from the scene's `Atmosphere` (hemisphere, ambient, environment, fog, tone mapping, screen,
  `_CozyThreeZ`, `RenderSettings.sun`), poses a camera rig under the world root (what hangs from the camera hangs there),
  and places a Unity camera on an eye outside the root in Unity's axes. Photos use `NativeView.CameraFor`. The cleaning
  bench and the item photos still go through the bridge until S25.
- **The material cache's frame:** only `SceneSync.Sync` advanced it, so with the main scene skipped `MaterialKit.Push`
  stopped applying (first pass: 62 FAIL, stale sky sprite colours). `NativeFrame.Run` now starts the cache's frame, and
  `Push` forces an update (`MaterialCache.Get(..., force: true)`).
- **Full pass:** every run clean, 11 of 11 self-checks, all 35 logs identical, no FAIL, the largest non-loading picture
  difference 0.02/255, 428 of 438 dumps identical (the 10 others are paused bench views).

## S6: TexturePainter (2026-11-24)

- **`TexturePainter`** (in `Three/Textures/Canvas2D.cs` until S25 moves the files): the CPU pixels, size, version and
  Canvas2D-style context, with no DOM type. `CanvasRenderingContext2D.canvas`, `drawImage`, `CanvasTexture` and the
  texture upload take a painter. Every world and mechanics texture (sky, sea, camp signs, crusts, tracks, the detector's
  heat map and waveform background, ...) is now a plain `TexturePainter`.
- **`HTMLCanvasElement`** is the UI's page element wrapping a painter (`painter`, an implicit conversion, and
  `HTMLCanvasElement.Of(painter)` for the two UI spots that need the element: the waveform host and the minimap). It goes
  with the DOM in the UI track.
- **Left for later:** pixels are painted where they were before (loader thread for Prebuilt pieces); the editor-only icon
  baker belongs with the UI foundation (S26), where the icons are used.
- **Full pass:** every run clean, 11 of 11 self-checks, all 35 logs identical, no FAIL, 428 of 438 dumps identical (the 10
  others are paused bench views), the largest non-loading picture difference 0.08/255 with no pixel over the threshold.

## S7: Picking (2026-11-24)

- **`Picking.Intersect(raycaster, trees)`** replaces `intersectObjects` for the finds and the camp's clickables (hover)
  and for the binoculars' rangefinder. Every plain mesh in the asked trees gets a MeshCollider on layer 30 (`Pick`),
  made from the triangles Three walks (draw range or material groups), posed from its `matrixWorld` after updating it as
  `Mesh.raycast` does. The colliders stand outside the mirrored root, in Unity's axes with mirrored vertices: under the
  (1, 1, -1) root PhysX put them in the wrong place.
- **PhysX finds, Three decides:** a batch raycast (back faces on) gives the meshes the ray passes through; Three's own
  triangle test then runs on just those, so sides, groups and near/far follow Three exactly. (PhysX's own hits were
  off: a batch query returns any hit on a mesh collider, not the nearest, and its face culling did not follow the
  materials' sides.) Points, lines, sprites, instanced and skinned meshes keep their CPU test. Colliders of meshes not
  asked about in 256 queries are dropped.
- **Self-check:** 800 rays at the camp's clickables and the rangefinder's targets on the loaded world give the same
  nearest hit and distance as `THREE.Raycaster` (0 different).
- The crust fitting (`LootGenerator`) and brushing (`CleaningStation`) keep the exact CPU ray, as planned; it moves into
  the kit in S25.
- **Full pass:** every run clean, 13 of 13 self-checks, all 35 logs identical, no FAIL, 428 of 438 dumps identical,
  non-loading shots within 0.02/255.

## S25a and S25b: every scene native; the bridge's scene copy deleted (2026-11-24)

- **S25a, the small scenes:** `NativeView` draws every scene. For the cleaning bench and the item photos it keeps a
  `SceneParts`: each part hung straight in the scene that nobody draws yet becomes a claimed `LiveTree` (a find posed
  for its photo, the bench's rim light), directional lights become Unity lights outside the mirrored root (aimed, coloured
  and shadowed as the bridge did, the scene's sun chosen by the bridge's rule), and ambient and hemisphere lights go
  into the scene's `Atmosphere`. Parts that leave the scene lose their GameObjects.
- **Bug found and fixed:** the item photos take one find after another in a single frame, and `Object.Destroy` waits
  for the end of the frame, so every polaroid showed all the earlier finds piled up (`tour-camp-board`).
  `LiveTree.Destroy` now switches the root off first.
- **S25b, deleted:** `SceneSync`, `MeshCache`, `Mirror` (and the `-bridge-legacy` mode), the camera-node path in
  `WebGLRenderer`, `Object3D.node`, `skipHidden`, the geometry's bridge cache fields. The scene roots (scaled
  (1, 1, -1)) now live in `World.RootOf`, and the shader globals' ids in `NativeView`. The self-checks that compared
  with the bridge (MeshKit against `MeshCache`, the Realizer twin) went with it; 7 self-checks remain.
- **Kept:** the descriptions (`THREE.Object3D` and friends) as the game's plain-C# model of the world, `MaterialCache`
  and `TextureCache` behind `MaterialKit`, and `WebGLRenderer` as the thin front the game calls `render` on.
- **Full pass:** every run clean, 7 of 7 self-checks, all 35 logs identical, no FAIL, 428 of 438 dumps identical (the 10
  others are paused bench views), non-loading shots within 0.02/255.

## S25c: the rest of the bridge moves into the kit (2026-11-24)

- `Three/Bridge` is gone. `MaterialCache`, `TextureCache`, `UrpSettings` and `WebGLRenderer` moved to `Native/Render`
  (namespace `THREE.Render`); `BridgeStats` became `FrameStats` with the same log text, so perf logs still compare.
  `PORT_STATUS.md` says so.
- **The display changed under the harness:** between S25b and S25c this machine's display went from 240 Hz / 96 dpi
  (OS ratio 1.0) to 144 Hz / 144 dpi (1.5). That alone moved six shots against the old reference (the lighthouse lamp
  at night by a mean of 13/255); today's rebuild of the S25b commit shows the same, so the code was not the cause.
  **New reference:** `C:\TidefinderParity\shots\ref144`, taken from the same reference build on this display (its
  `selfcheck` folder removed, as the reference build has no self-check mode). Use it from now on. The lamp shot also
  varies a little from run to run at 144 Hz (0.005 to 1.4/255 between runs).
- **Full pass against `ref144`:** every run clean, 7 of 7 self-checks, all logs identical, no FAIL, non-loading shots
  within 0.02/255, the dump differences are the paused bench views.

## S26 and S27: the UI's foundation and the native loading screen (2026-11-24)

- **`UiRoot`** (`UI/Native`): the one UIDocument, PanelSettings, the fonts (body, bold, heading, title, the emoji
  sprites and fallbacks) and the 1920 x 1080 page scaled to the screen with the UI size setting, moved out of
  `DomHost` unchanged. `DomHost` now builds only the page's tree under `UiRoot.Root` and copies the metrics into
  `window`. Native screens hang in the same panel.
- **`GameTimers`**: setTimeout, setInterval and requestAnimationFrame on the test clock, moved out of `window`
  (which hands its calls over). **`UiX`**: small helpers for native screens (elements with classes, text, buttons,
  show and hide, class toggles).
- **S27, `LoadingScreen`**: the loading screen is native, built with the page's element structure and class names
  (`#boot`, `.t-h1.game-title`, `.t-p.title-face`, `#boot-track`, `#boot-bar`; text in a label with no margins, the
  title face as the page gave it), over `LoadingSea`. `GameHost` and `Game` move its bar and lift it
  (`LoadingScreen.Progress`, `Lift`: the 0.5 s fade, then gone after 0.7 s). The pointer over any native screen counts
  as over the UI (class `ui-native`).
- **Not yet:** `UiAnim`, `MenuNav` and the icon baker come with the screens that first need them (S28, S29).
- **Full passes (S26, S27):** every run clean, 7 of 7 self-checks, all logs identical, no FAIL, the loading shots
  within their usual noise (mean at most 0.16/255) and the same picture side by side, 428 of 438 dumps identical.

## S28 (first part): UiNode, UiAnim and the native HUD (2026-11-24)

- **`UiNode`** (`UI/Native`): a native element built exactly as the DOM layer built one, without the page emulation:
  the tag class (`t-div`, ...), its own words in a label with no margins (emoji sprites, the uppercase rules), the
  heading and title faces by tag and class, pill corners kept at half the shorter side, `.hidden` over every other
  display rule, the page's picking rules, tooltips, click, inline SVG drawings sized in em as the page sized them
  (`Svg`), and `MountIn` to hang under a page element while both kinds coexist.
- **`UiAnim`**: the page's animations (key bob, card pop, camp slide, menu and sleep fades, the z's) and the helper
  classes for selectors USS lacks, moved out of `CssAnimations` and matched by the element's classes, so page and
  native elements animate alike.
- **Mixed siblings:** the DOM's z-index pass now sorts every VisualElement in a container (a stable sort by z, native
  ones by the same class and id table), so native siblings keep their place instead of falling behind the page's.
- **Native now:** the HUD's stats (coins, the warmth bar and mug), the chips (clock, backpack, ecology, bones, mute),
  the tool badge with its drawings, the controller's d-pad tools (slots, stack, next), the two prompts with their key
  pictures, the hint with its keys (`Glyphs.Make/Set/Fill`), the toasts, the crosshair, the draw bar, the dive tint and
  the fade. `addLayer` puts a page layer just under the native toasts, as before.
- **Still page elements:** the minimap and detector canvases, the binocular and viewfinder canvases (S32), inspect and
  result (they have buttons the menu navigation drives: with `MenuNav`, S29), the intro, the menu bar.
- **Full pass:** every run clean, 7 of 7 self-checks, all logs identical, no FAIL, non-loading shots within 0.02/255,
  428 of 438 dumps identical.

## S28 (rest) and S29 (first part): MenuNav, the cards, the menus (2026-11-24)

- **`MenuNav` and `NavEl`**: the game's own menu navigation (`padNav`, `menuAccept`, `menuKey`, `menuTick`, the back
  button, the scrolling lists, the controller's scroll) now walks VisualElements on the panel and a `NavEl` that is a
  page element or a native one: focus, click, step, box, text, scroll. The highlight is the page's activeElement or a
  native element, never both. The logic is unchanged: with every menu still on the page the full pass gave identical
  logs (the controller tests log what is chosen and every reachable button's box).
- **`UiNode` grew** buttons that behave as the page's (a click stops there, pressing chooses, the moving mouse
  chooses), `Disabled`, `Focus`, `Click`, `OnStep`, `FullText`, the page's scroll boxes (`.help-body`, `.ach-list`, ...:
  a ScrollView with the page's settings, `ScrollTop`, `ScrollHeight`), `Markup` (text set as innerHTML set it: entities,
  collapsed whitespace, inline formatting as rich text, via `HtmlParser.TryText`), `MountBefore`, and mounting under a
  page element runs that element's z-index order (the menu bar stays on top).
- **Native now:** inspect (with the Instant Clean button) and the result card, the intro card (built element for
  element as the page parsed it, the detector drawing sized as the page sized it), the opening's caption, skip note and
  lightning flash, the menu bar, the pause screen, the main menu, Help, Achievements, Credits and the farewell.
  `Glyphs.KeyNode` follows the controller as `Glyphs.key` did. AutoShot and Game close native cards through
  `UIManager.removeFirst` and `skipIntro`.
- **Full passes (after each part):** every run clean, 7 of 7 self-checks, all logs identical, no FAIL, non-loading
  shots within 0.02/255, 428 of 438 dumps identical.

## S29 (rest) and S30: settings, saves, the journal and letters (2026-11-24)

- **`UiRange`**: the page's range input as it was built (`.range` with `.range-track`, `.range-fill`, `.range-thumb`,
  dragged with pointer capture, the value snapped to its step and written as JS writes numbers).
- **`UiText`**: the page's text input as it was built (a borderless TextField on a clear background with the faint
  placeholder label over it); while it has the keyboard the game takes no keys. `NavEl.IsTextBox` keeps a controller
  off it unless Steam's keyboard can type.
- **Native now:** Settings (rows, sliders, choices with their delayed apply, switches; the arrows' clicks stop at
  them), Saves (the name box, Steam's keyboard on A, overwrite, the two-press delete), the journal, the post, story
  pages, the ending and the sleep veil with its z's.
- **Full passes (S29 settings, S29 saves, S30):** every run clean, 7 of 7 self-checks, all logs identical, no FAIL,
  non-loading shots within 0.02/255, 428 of 438 dumps identical.

## S31 and S32: the camp, the map and the overlays (2026-11-24)

- **S31, the camp:** `CampModal` builds native elements (tabs with their keys, every page: rest, recycle, sell and
  the workbench, the Lost & Found board with its tilted polaroids, upgrades and the shop with `ShopArt` drawings,
  luxury, the shelf, the photo album, the aquarium). Images show their texture as the page's `<img>` did
  (`UiNode.Image`), the polaroid's `--tilt` became the rotation it set.
- **Two differences the pass caught, fixed:** the page made a `.btn` straight inside `.camp-body` keep its own width
  (`align-self: flex-start`, ApplyDirectChildFixups); and the page let go of the highlight when an element lost UI
  Toolkit's focus (the camp hiding), so the next visit opened on its current tab. Native elements now do both
  (`FocusIn`/`FocusOut` follow the highlight).
- **S32:** `UiCanvas` (a TexturePainter shown on an element, uploaded once a frame when it changed, sized to the
  picture) for the minimap, the detector's waveform, the binocular lenses and readout, the viewfinder and the big map
  (its click picks a place from the element's box). The minimap's base is a plain painter and `MiniMapGpu` hangs on the
  native canvas. The HUD's container, the scope, the photo flash, the controller's map cursor and the telescope's view
  (its labels placed from the same rounded numbers the page parsed) are native. `addLayer` takes native layers.
- **Full passes (S31 before the fixes, then S31 with S32):** every run clean, 7 of 7 self-checks, all logs identical,
  no FAIL; after the fixes non-loading shots within 0.02/255, 428 of 438 dumps identical.

## S33: the DOM layer is gone (2026-11-24)

- **`Page`** (`UI/Native`): what the game still asked of document and window. It builds index.html's tree natively
  (`.t-html` > `.t-body` > `#scene`, `#ui`, the loading screen), so every Style.uss selector still matches, and keeps the
  game's state classes on the body (`diving`, `underground`, `menu-open`, `scope-on`, `viewfinder`, `usingpad`,
  `nolock`). It also gives pointer lock and its change, the screen's resize, the page's size and ratios (`UiRoot`),
  whether the pointer is over the UI, typing, and the once-a-frame tick (metrics, `GameTimers`, `UiCanvas`).
- **`UiNode`** keeps the page's pointer-events and z-index tables and orders siblings itself (a stable sort by z, as
  the page did). **Moved, unchanged:** `SvgRasterizer` and `EmojiText` (into `UI/Native`), the HTML fragment parser
  as `TextMarkup` (only the text path: entities, whitespace, inline formatting). **Removed:** the controller's choice
  list for a page `<select>` (no screen has one) and the page-element halves of `Glyphs`, `NavEl` and `MenuNav`.
- **Deleted:** `Dom/` (DomHost, HTMLElement, DomTypes, DomGlobals, HtmlParser, SelectorEngine, CSSStyleDeclaration,
  CssAnimations) and `HTMLCanvasElement`. `PORT_STATUS.md` and `MIGRATION.md` say so.
- **Not done (left as is on purpose):** cleaning `Style.uss` of its DOM-era workarounds. The native elements are built
  to match them, so removing them would change the look with nothing to gain until someone edits those screens.
  Icons stay drawn by `SvgRasterizer` at runtime (cached); baking them in the editor is a load-time optimisation for
  S34 if it shows up in the measurements.
- **Full pass:** every run clean, 7 of 7 self-checks, all 35 logs identical, no FAIL, non-loading shots within
  0.02/255, 428 of 438 dumps identical.
