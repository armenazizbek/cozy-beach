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
