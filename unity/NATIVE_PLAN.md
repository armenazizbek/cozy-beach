# Tidefinder as a native Unity game: plan

Status: plan accepted on 2026-11-23, with the recommended choice for every open decision (section 7). Nothing in the
game has been converted yet. S0 is next.

**Goal.** Remove the two emulation layers the port was built on, and have the game use Unity directly:

- `Assets/Scripts/Three`: a C# copy of Three.js (scene graph, maths, geometries, materials, a software 2D canvas). It also
  holds the bridge (`Three/Bridge/SceneSync.cs`), which copies the whole Three scene into Unity GameObjects every frame.
- `Assets/Scripts/Dom`: an HTML, CSS and DOM emulation over UI Toolkit, which the menus and the HUD are built with.

The game should keep looking and playing exactly like `main`. Its features are done, and `main` is effectively frozen and
may ship first. So the rewrite is for long-term editability and cleanliness, not speed: today the game runs at about
360 fps on the dev PC and 60 fps on a ROG Ally at 10 W.

**Where the work happens.** On a long-lived branch (`claude/native-unity-d8a3b9` today), never on `main`. Each step below
leaves that branch buildable and playable. `main` gets merged into the branch at every phase boundary (section 3).

**Constraints carried over from `main`.** The look must match `main`. Keep the plain sand (the detailed look was
rejected). Change shadows or the map size only when asked. Gulls stay silent, with engine shadows only. No free beach
decor, since decorating is what the camp upgrades sell.

---

## 0. Summary

### Headline numbers (measured; see section 1)

| | Count |
|---|---:|
| Game code (Core, World, Mechanics, UI) | 85 files, 42,667 lines |
| `THREE.` uses in game code | **4,207** (World 2,873, Mechanics 1,111, Core 208, UI 15) |
| Scene-graph members used without the prefix (`.position.`, `.add(`, `.visible`, `castShadow`, ...) | 2,849 |
| DOM-style calls: `el(`, `classList`, `innerHTML`, `document.`, `style.` | **585** (UI 513, Core 53, Mechanics 19) |
| DOM-style calls including `window.`, `querySelector`, `createElement`, `textContent`, `onclick`, `HTML*Element` | 1,149 |
| Canvas calls: `getContext`, `fillText`, `fillRect`, `strokeRect`, `drawImage`, `measureText` | **146** |
| All 2D-context calls, in 49 canvases (31 of them textures in the 3D world) | about 989 |
| Emulation to remove: `Three` 9,817 lines plus `Dom` 6,404 lines | 16,221 lines (about 5,500 survive as native utilities, section 1.5) |
| Constructors: materials / geometries / curves and shapes / `THREE.Mesh` / `THREE.Group` | 503 / 802 / 47 / 476 / 209 |
| Custom HLSL shaders already in `Resources/Shaders` | 17 shaders plus 5 includes (all kept) |

### Estimate (section 6)

- **About 78 to 125 agent-days of work, about 95 expected.** One agent-day means one session's 6 to 8 hours of focused
  work, including builds and smoke runs.
- **Calendar: about 3.2 to 5 months with two tracks** (world and core in one, UI in parallel from step S8), about 4 months
  expected. With a single track it is about 4 to 6 months.
- The top of the "2 to 4 months" guess only holds if the measured conversion rate comes out near the fast end. Step S9 is
  the pilot: it measures that rate, and the plan is re-projected from it before the bulk of the work starts.

### Recommended order (section 3)

1. **Measure first.** S0 builds the parity harness: seeded, fixed-step smoke runs, a screenshot and perf comparer, and a
   dump of the Unity scene. S1 runs spikes for the questions the architecture depends on.
2. **Native kit next to Three** (S2 to S7): game-space maths and a world root, `MeshKit`, `MaterialKit` and the
   atmosphere service, mounts that let native and Three objects live in one scene, a loader that builds off-thread and
   places objects on the main thread, `TexturePainter` and picking. The game does not change in these steps.
3. **Loop and camera** (S8): `GameHost` drives `Game.frame` directly, and the main camera becomes a native Unity camera.
4. **World, one unit at a time** (S9 to S24), leaves first and hubs last. S9 is the measured pilot. S25 then deletes
   `Three`.
5. **UI as a second track** (S26 to S33), screen by screen, ending with S33 deleting `Dom`.
6. **Finish** (S34): an IL2CPP perf pass against the baseline, then Steam Deck sign-off.

### Decisions (accepted 2026-11-23, section 7)

1. **Handedness:** the game's right-handed numbers under one mirrored world root (option A), unless the S1 spike
   rules it out.
2. **Text and textures drawn in code:** keep the CPU painter, for exact parity.
3. **UI authoring:** UXML for fixed screen layouts, C# for lists.
4. **Two parallel tracks:** yes. A second session runs the UI track from S8 on.
5. **Visible differences:** the owner signs them off at each phase boundary, from the comparison report.
6. **If `main` changes:** the reference is rebuilt from `main` plus the harness commits. The harness stays off `main`.
7. **Branch name:** stays `claude/native-unity-d8a3b9`.

---

## 1. Inventory

### 1.1 How it was counted

`python unity/Tools/Native/inventory.py` (committed with this plan) prints every table in this section. Re-run it after
each step to see what is left (`--totals` prints only the per-unit and per-folder tables). It counts source text with
whole-line `//` comments removed:

- **`THREE.`**: every use of the C# Three.js copy by name.
- **DOM (brief)**: the calls named in the brief: `el(` (also `el<T>(`), `classList`, `innerHTML`, `document.` and `style.`.
- **DOM (wide)**: the brief's calls plus `window.`, `querySelector(All)`, `addEventListener`, `createElement`,
  `textContent`, `setAttribute`, `onclick`, `getBoundingClientRect` and `HTML*Element` type names.
- **2D (brief)**: `getContext(`, `fillText(`, `fillRect(`, `strokeRect(`, `drawImage(` and `measureText(`.
- **2D (all)**: every `CanvasRenderingContext2D` member called on a context variable (`g`, `ctx`, `c`, `x`, `bg`, ...).
  About 11 of the 989 are geometry `translate` or `scale` calls in files with no canvas, so read the column as ±1%.
- **Canvases**: `getContext("2d"` calls, one per canvas made.
- **Scene-graph members**: `Object3D` members used without the prefix: `.position.`, `.rotation.`, `.scale.`,
  `.quaternion.`, `.add(`, `.remove(`, `.visible`, `.traverse(`, `.userData`, `.getWorldPosition(`, `.lookAt(`,
  `.updateMatrixWorld(`, `castShadow`/`receiveShadow`, `renderOrder`, `frustumCulled`, `.material`, `.geometry`.

These are close to the totals in the brief (about 4,200, 560 and 160). The brief's canvas figure fits between the narrow
and the wide count.

### 1.2 By folder

| Folder | Files | Lines | `THREE.` | DOM (brief) | DOM (wide) | 2D (brief) | 2D (all) | Canvases | Scene-graph members |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Core | 26 | 13661 | 208 | 53 | 149 | 2 | 2 | 2 | 216 |
| World | 40 | 18796 | 2873 | 0 | 113 | 86 | 527 | 32 | 1931 |
| Mechanics | 9 | 4473 | 1111 | 19 | 44 | 18 | 69 | 8 | 632 |
| UI | 10 | 5737 | 15 | 513 | 843 | 40 | 391 | 7 | 70 |
| **Total** | **85** | **42667** | **4207** | **585** | **1149** | **146** | **989** | **49** | **2849** |

### 1.3 Conversion units

A unit is a set of files converted, built and verified together in one step. The units follow the dependencies in
section 3: each one is either a leaf or brings its callers along.

| Unit | Files | Lines | `THREE.` | DOM (brief) | DOM (wide) | 2D (brief) | 2D (all) | Canvases | Scene-graph members |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| C0 Harness | 1 | 1419 | 4 | 5 | 12 | 0 | 0 | 0 | 2 |
| C1 Host, loop, input | 6 | 695 | 10 | 6 | 7 | 0 | 0 | 0 | 4 |
| C2 Game hub | 1 | 6971 | 155 | 41 | 125 | 2 | 2 | 2 | 179 |
| C3 Particles, opening | 2 | 431 | 39 | 0 | 4 | 0 | 0 | 0 | 31 |
| C4 Pure logic (no change) | 16 | 4145 | 0 | 1 | 1 | 0 | 0 | 0 | 0 |
| W1 Pilot props | 4 | 567 | 90 | 0 | 5 | 2 | 11 | 1 | 52 |
| W2 Small props | 8 | 1749 | 297 | 0 | 16 | 5 | 32 | 2 | 159 |
| W3 Weather, tracks, critters | 6 | 1642 | 238 | 0 | 10 | 8 | 27 | 3 | 196 |
| W4 Vehicles, dog, ships | 4 | 2251 | 268 | 0 | 10 | 5 | 9 | 2 | 136 |
| W5 Terrain and atmosphere | 4 | 1803 | 203 | 0 | 23 | 17 | 71 | 7 | 81 |
| W6 Palms and scenery | 1 | 596 | 97 | 0 | 1 | 0 | 0 | 0 | 66 |
| W7 Camp | 1 | 1404 | 408 | 0 | 8 | 24 | 90 | 8 | 389 |
| W8 Islands | 1 | 1330 | 232 | 0 | 4 | 0 | 0 | 0 | 144 |
| W9 Lighthouse | 1 | 1077 | 120 | 0 | 3 | 0 | 0 | 0 | 86 |
| W10 Old house and pictures | 2 | 1247 | 67 | 0 | 3 | 10 | 202 | 1 | 78 |
| W11 Under the sea | 6 | 4040 | 681 | 0 | 27 | 9 | 71 | 6 | 398 |
| W12 Underground | 1 | 693 | 159 | 0 | 2 | 6 | 14 | 2 | 131 |
| W13 Loot | 2 | 1969 | 452 | 0 | 11 | 2 | 4 | 1 | 166 |
| M1 Tools in hand | 7 | 2549 | 610 | 19 | 31 | 8 | 36 | 3 | 444 |
| M2 Workbench and item photos | 2 | 408 | 77 | 0 | 3 | 8 | 29 | 4 | 46 |
| I1 Loading screen | 1 | 514 | 0 | 5 | 5 | 0 | 0 | 0 | 0 |
| I2 HUD, menus, settings | 3 | 2173 | 0 | 363 | 548 | 22 | 102 | 6 | 43 |
| I3 Camp screens | 2 | 1210 | 0 | 81 | 202 | 0 | 0 | 0 | 4 |
| I4 Map, minimap, telescope | 3 | 1784 | 0 | 64 | 88 | 18 | 289 | 1 | 14 |

`Game.cs` (C2) is not a step of its own. Every step that converts a module also changes that module's call sites in
`Game.cs`, and S25 converts what is left of it.

### 1.4 Every file

| Unit | File | Lines | `THREE.` | DOM (brief) | DOM (wide) | 2D (brief) | 2D (all) | Canvases | Scene-graph members |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| C0 | `Core/AutoShot.cs` | 1419 | 4 | 5 | 12 | 0 | 0 | 0 | 2 |
| C1 | `Core/GameHost.cs` | 128 | 3 | 6 | 7 | 0 | 0 | 0 | 1 |
| C1 | `Core/InputManager.cs` | 217 | 7 | 0 | 0 | 0 | 0 | 0 | 3 |
| C1 | `Core/FramePacing.cs` | 50 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C1 | `Core/GamepadInput.cs` | 139 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C1 | `Core/MainThread.cs` | 73 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C1 | `Core/Prebuilt.cs` | 88 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C2 | `Core/Game.cs` | 6971 | 155 | 41 | 125 | 2 | 2 | 2 | 179 |
| C3 | `Core/Opening.cs` | 309 | 22 | 0 | 1 | 0 | 0 | 0 | 25 |
| C3 | `Core/Particles.cs` | 122 | 17 | 0 | 3 | 0 | 0 | 0 | 6 |
| C4 | `Core/StateManager.cs` | 674 | 0 | 1 | 1 | 0 | 0 | 0 | 0 |
| C4 | `Core/Achievements.cs` | 161 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/AudioManager.cs` | 913 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Catches.cs` | 96 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Constellations.cs` | 119 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Help.cs` | 208 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Items.cs` | 180 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Journal.cs` | 621 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Mail.cs` | 125 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Photos.cs` | 171 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Platform.cs` | 85 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/SaveGame.cs` | 306 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Settings.cs` | 127 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/SteamService.cs` | 137 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Storage.cs` | 160 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| C4 | `Core/Story.cs` | 62 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| W1 | `World/Whitecaps.cs` | 177 | 26 | 0 | 5 | 2 | 11 | 1 | 3 |
| W1 | `World/Pier.cs` | 166 | 35 | 0 | 0 | 0 | 0 | 0 | 30 |
| W1 | `World/Flags.cs` | 95 | 20 | 0 | 0 | 0 | 0 | 0 | 12 |
| W1 | `World/Stones.cs` | 129 | 9 | 0 | 0 | 0 | 0 | 0 | 7 |
| W2 | `World/CoveNets.cs` | 286 | 39 | 0 | 4 | 5 | 28 | 2 | 9 |
| W2 | `World/ShipKit.cs` | 284 | 46 | 0 | 8 | 0 | 4 | 0 | 4 |
| W2 | `World/Telescope.cs` | 160 | 52 | 0 | 1 | 0 | 0 | 0 | 32 |
| W2 | `World/Aquarium.cs` | 245 | 49 | 0 | 0 | 0 | 0 | 0 | 53 |
| W2 | `World/GolfCart.cs` | 225 | 47 | 0 | 0 | 0 | 0 | 0 | 28 |
| W2 | `World/SeaTreasure.cs` | 129 | 28 | 0 | 0 | 0 | 0 | 0 | 14 |
| W2 | `World/Dolphins.cs` | 260 | 22 | 0 | 0 | 0 | 0 | 0 | 19 |
| W2 | `World/SeaModels.cs` | 160 | 14 | 0 | 3 | 0 | 0 | 0 | 0 |
| W3 | `World/Tracks.cs` | 190 | 35 | 0 | 4 | 8 | 25 | 3 | 3 |
| W3 | `World/Birds.cs` | 336 | 59 | 0 | 0 | 0 | 0 | 0 | 79 |
| W3 | `World/Sandcastles.cs` | 536 | 58 | 0 | 0 | 0 | 0 | 0 | 63 |
| W3 | `World/Ecology.cs` | 258 | 46 | 0 | 1 | 0 | 2 | 0 | 34 |
| W3 | `World/Grass.cs` | 189 | 24 | 0 | 3 | 0 | 0 | 0 | 4 |
| W3 | `World/Rain.cs` | 133 | 16 | 0 | 2 | 0 | 0 | 0 | 13 |
| W4 | `World/Boat.cs` | 410 | 117 | 0 | 4 | 5 | 7 | 2 | 47 |
| W4 | `World/Dog.cs` | 780 | 68 | 0 | 6 | 0 | 0 | 0 | 64 |
| W4 | `World/ModernShips.cs` | 482 | 46 | 0 | 0 | 0 | 1 | 0 | 13 |
| W4 | `World/OldShip.cs` | 579 | 37 | 0 | 0 | 0 | 1 | 0 | 12 |
| W5 | `World/Sky.cs` | 372 | 76 | 0 | 5 | 9 | 32 | 4 | 36 |
| W5 | `World/Beach.cs` | 999 | 60 | 0 | 11 | 6 | 27 | 2 | 8 |
| W5 | `World/Ocean.cs` | 341 | 49 | 0 | 7 | 2 | 12 | 1 | 17 |
| W5 | `World/Conform.cs` | 91 | 18 | 0 | 0 | 0 | 0 | 0 | 20 |
| W6 | `World/Scenery.cs` | 596 | 97 | 0 | 1 | 0 | 0 | 0 | 66 |
| W7 | `World/Camp.cs` | 1404 | 408 | 0 | 8 | 24 | 90 | 8 | 389 |
| W8 | `World/Island.cs` | 1330 | 232 | 0 | 4 | 0 | 0 | 0 | 144 |
| W9 | `World/Lighthouse.cs` | 1077 | 120 | 0 | 3 | 0 | 0 | 0 | 86 |
| W10 | `World/OldPictures.cs` | 377 | 3 | 0 | 1 | 10 | 201 | 1 | 0 |
| W10 | `World/OldHouse.cs` | 870 | 64 | 0 | 2 | 0 | 1 | 0 | 78 |
| W11 | `World/SeaCave.cs` | 742 | 141 | 0 | 3 | 4 | 31 | 2 | 92 |
| W11 | `World/Underwater.cs` | 935 | 152 | 0 | 4 | 0 | 0 | 0 | 101 |
| W11 | `World/SeaCreatures.cs` | 637 | 122 | 0 | 3 | 0 | 2 | 0 | 59 |
| W11 | `World/UnderwaterDecor.cs` | 634 | 95 | 0 | 11 | 4 | 13 | 3 | 69 |
| W11 | `World/SeaFlora.cs` | 278 | 73 | 0 | 4 | 1 | 25 | 1 | 2 |
| W11 | `World/SeaLife.cs` | 814 | 98 | 0 | 2 | 0 | 0 | 0 | 75 |
| W12 | `World/Underground.cs` | 693 | 159 | 0 | 2 | 6 | 14 | 2 | 131 |
| W13 | `Mechanics/LootGenerator.cs` | 1572 | 439 | 0 | 10 | 2 | 4 | 1 | 151 |
| W13 | `World/LootSpawner.cs` | 397 | 13 | 0 | 1 | 0 | 0 | 0 | 15 |
| M1 | `Mechanics/ViewModel.cs` | 1231 | 405 | 14 | 20 | 2 | 7 | 1 | 309 |
| M1 | `Mechanics/DetectorTool.cs` | 318 | 44 | 5 | 9 | 6 | 29 | 2 | 36 |
| M1 | `Mechanics/Fishing.cs` | 346 | 42 | 0 | 0 | 0 | 0 | 0 | 30 |
| M1 | `Mechanics/Arrows.cs` | 144 | 34 | 0 | 0 | 0 | 0 | 0 | 26 |
| M1 | `Mechanics/Hand.cs` | 306 | 29 | 0 | 2 | 0 | 0 | 0 | 0 |
| M1 | `Mechanics/DigTool.cs` | 97 | 30 | 0 | 0 | 0 | 0 | 0 | 29 |
| M1 | `Mechanics/Bullets.cs` | 107 | 26 | 0 | 0 | 0 | 0 | 0 | 14 |
| M2 | `Mechanics/CleaningStation.cs` | 352 | 62 | 0 | 3 | 8 | 29 | 4 | 37 |
| M2 | `UI/ItemPhotos.cs` | 56 | 15 | 0 | 0 | 0 | 0 | 0 | 9 |
| I1 | `UI/LoadingSea.cs` | 514 | 0 | 5 | 5 | 0 | 0 | 0 | 0 |
| I2 | `UI/UIManager.cs` | 1917 | 0 | 354 | 522 | 22 | 102 | 6 | 42 |
| I2 | `UI/Glyphs.cs` | 197 | 0 | 9 | 26 | 0 | 0 | 0 | 1 |
| I2 | `UI/PadText.cs` | 59 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| I3 | `UI/CampModal.cs` | 926 | 0 | 81 | 202 | 0 | 0 | 0 | 4 |
| I3 | `UI/ShopArt.cs` | 284 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| I4 | `UI/MapView.cs` | 751 | 0 | 0 | 2 | 18 | 289 | 1 | 0 |
| I4 | `UI/MiniMapGpu.cs` | 130 | 0 | 41 | 45 | 0 | 0 | 0 | 0 |
| I4 | `UI/TelescopeView.cs` | 903 | 0 | 23 | 41 | 0 | 0 | 0 | 14 |
| | **Total (85 files)** | **42667** | **4207** | **585** | **1149** | **146** | **989** | **49** | **2849** |

What the counts hide:

- **Many separate pieces are built in code.** Game code calls 431 `MeshStandardMaterial` constructors, 50
  `MeshBasicMaterial`, 6 `SpriteMaterial`, 5 `MeshPhysicalMaterial`, 4 `ShaderMaterial`, 2 `MeshLambertMaterial`, 2
  `LineBasicMaterial` and 1 `PointsMaterial`. Geometry (802): 236 cylinders, 141 spheres, 126 boxes, 79 tori, 41 cones,
  38 raw `BufferGeometry`, 35 planes, 24 tubes, 16 circles, 10 rings, 10 lathes, 8 shape geometries, 8 icosahedra,
  8 extrusions, 7 octahedra, 6 dodecahedra, 5 capsules and 4 rounded boxes. Curves and shapes (47): 21 Catmull-Rom
  curves, 16 shapes, 7 quadratic Béziers, 2 spline curves and 1 path. `BufferGeometryUtils.mergeGeometries` is called
  42 times.
- **Unusual objects:** 5 `InstancedMesh` (grass, footprint and tyre tracks, whitecaps, and the cave hoard's coins,
  jewels and bars), 2 `SkinnedMesh` with 6 bones (the dog and its tail), 2 `Points` (the stars and the particle pools),
  3 `LineSegments` (rain and drips), and 7 `Sprite` (sun, moon, glows, clouds, the sun patch under water).
- **Lights:** 1 directional sun and 1 hemisphere light (`Sky`), point lights at the camp (3), in the sea cave (2) and
  underground (1), 1 spot light (the golf cart), and a separate bench rig (ambient, directional, point). `ItemPhotos`
  has its own directional and hemisphere lights.
- **Three separate scenes:** the main scene, the cleaning bench (`CleaningStation.scene`, with its own camera) and the
  item photos (`ItemPhotos`, drawn off-screen).
- **Raycasts** (8 `THREE.Raycaster` uses): hover and aim in `Game.cs` and `InputManager.cs`, brushing in
  `CleaningStation.cs`, and fitting the sand crust in `LootGenerator.cs`.
- **`userData`** (40 uses): hit radii, owner links (gull, crab, item, camp-clickable kind), and animation bookkeeping
  (`s0`, `sy`, `base`).
- **Inline SVG** in 7 places: 6 tool icons in `UIManager.cs`, and the shop pictures built from parameters in
  `ShopArt.cs`. `Dom/SvgRasterizer.cs` draws them.
- **Randomness.** The world's layout is seeded and the same on every launch (`Stones.rng(7)` and the like in `Scenery`,
  `Island` and `Stones`). Some details use `JS.Random()`, which is unseeded: 18 calls in `Underwater`, 12 in
  `Whitecaps`, 10 each in `SeaLife`, `SeaCreatures` and `Grass`, and so on. Two runs of `main` therefore never give
  identical pictures, and section 4 deals with that.

### 1.5 The two layers, file by file

| File | Lines | What happens to it |
|---|---:|---|
| `Three/JS.cs` | 141 | Kept, moved to `Core/JS.cs` (595 uses of `JS.` in game code). |
| `Three/Math/Vectors.cs`, `Rotation.cs`, `Matrices.cs`, `Shapes3.cs` | 1,239 | Deleted. Replaced by `UnityEngine` `Vector2/3/4`, `Quaternion`, `Matrix4x4` and `Bounds`, plus `GameSpace` helpers (2.3). `MeshKit` keeps a private double-precision affine type, so vertices stay identical. |
| `Three/Math/Color.cs` | 253 | Kept as `ColorUtil`: hex, HSL and sRGB/linear exactly as Three computes them, because material colours depend on it. |
| `Three/Core/Object3D.cs` | 468 | Deleted (GameObject and Transform). `Clock` becomes `GameClock`. |
| `Three/Core/BufferGeometry.cs` | 538 | Becomes `MeshKit.MeshData`, a CPU mesh builder that is safe off the main thread. |
| `Three/Geometries/*` (3 files) | 1,085 | Kept, as `MeshKit` generators. They produce the same vertices in the same order. |
| `Three/Extras/Curves.cs`, `Earcut.cs` | 1,145 | Kept, inside `MeshKit`. |
| `Three/Materials/Materials.cs` | 250 | Deleted. Replaced by `MaterialKit` specs (2.4). |
| `Three/Objects/Objects.cs`, `CamerasLights.cs` | 629 | Deleted. Replaced by Unity components; the raycaster becomes `Picking` (2.9). |
| `Three/Textures/Textures.cs` | 138 | Deleted (`Texture2D`). |
| `Three/Textures/Canvas2D.cs`, `CanvasFonts.cs` | 1,715 | Kept, as `TexturePainter` (2.6), detached from `HTMLCanvasElement`. |
| `Three/Bridge/SceneSync.cs`, `MeshCache.cs`, `MaterialCache.cs`, `WebGLRenderer.cs`, `TextureCache.cs` | 1,988 | Deleted in S25. Their conversion rules move into `MeshKit`, `MaterialKit`, `Atmosphere` and `Cameras` first (S3, S4, S8). |
| `Three/Bridge/UrpSettings.cs` | 134 | Kept, moved to `Core/Rendering`. |
| `Three/Bridge/BridgeStats.cs` | 94 | Kept as `FrameStats`, with the same log format so old and new runs compare (4.4). |
| `Dom/DomHost.cs` | 464 | Replaced by `UiRoot`, keeping its font loading, 1920x1080 page scale, UI size setting and metrics. |
| `Dom/HTMLElement.cs`, `DomTypes.cs`, `DomGlobals.cs`, `HtmlParser.cs`, `SelectorEngine.cs`, `CSSStyleDeclaration.cs` | 4,612 | Deleted in S33. Timers become `GameTimers`; pointer lock is already in `InputManager`. |
| `Dom/CssAnimations.cs` | 510 | Replaced by `UiAnim`: the six `@keyframes` (`kbd`, `pop`, `slide`, `mm`, `sleep`, `zz`). Its z-index and pointer-events tables become USS and UXML. |
| `Dom/SvgRasterizer.cs` | 765 | Becomes editor-only: it bakes the icons into PNGs at build time (2.7). |
| `Dom/EmojiText.cs` | 53 | Kept, in `UI/`. |

About 5,500 lines survive as native utilities: `MeshKit`, `TexturePainter`, `ColorUtil`, `JS`, `UrpSettings`,
`FrameStats` and `EmojiText`. About 10,700 lines are deleted. The new native code (world root and `GameSpace`,
`MaterialKit`, `Atmosphere`, loader, `Picking`, `UiRoot`, `MenuNav`, `UiAnim` and the harness) should come to about
3,000 lines.

---

## 2. Architecture decisions

The rule behind all of them: **parity first, optimise later.** Each decision keeps the maths that makes today's
picture, moves it to the native place, and runs it once instead of every frame. Optimisations that change the picture
or the cost (instancing, merging, GPU particles, Forward+) wait for S34 or come after the rewrite, and each one is
measured.

### 2.1 Coordinates and handedness (the decision everything else rests on)

The game's numbers are Three.js numbers: right-handed, yaw 0 looks toward −z, and the sea is at +z. Unity is
left-handed. Today the bridge converts every local transform as M·T·M, where M mirrors z (position `(x, y, −z)`,
rotation `(−qx, −qy, qz, qw)`), and negates z on every mesh with its winding reversed. Saves, hand-placed positions,
shader uniforms and about 1,400 position, rotation and scale uses in game code all depend on those numbers.

| Option | What it means | For | Against |
|---|---|---|---|
| **A. Mirrored world root** | One GameObject `World` with `localScale = (1, 1, −1)`. Everything in the world lives under it, with local transforms and meshes in game numbers, not converted. World = M·W·v, the same as the bridge produces. Cameras, the sun and spot lights stay outside `World` and are placed through `GameSpace.ToUnity`. | Game code, saves and inspector numbers stay as they are. No mesh or rotation conversion. Unity flips the winding for negative-determinant renderers itself. Smallest risk to parity. | Under `World`, `Transform.position`, `.forward` and `LookAt` are in Unity's mirrored space, so game code must use local transforms and the `GameSpace` helpers. Shaders that do object-space maths (palm sway, shark body, fish school, sprites, baked instances) see z with the opposite sign from today and need a short audit. Instanced draws want positive-determinant matrices. |
| B. Convert at every write | Keep the bridge's conjugation, but in helpers: `GameSpace.SetPose(tf, pos, rot)`. Meshes are z-negated at build time. | Object space is exactly today's, so shaders do not change. | All of those position, rotation and scale uses go through helpers, and one missed helper is a silent bug. Inspector numbers are mirrored. |
| C. Native handedness everywhere | Negate z in every literal and every rotation about x or y. Saves are converted on load and save, including the beach grid indices in `dugPacked`. | The cleanest end state. | Thousands of edits the compiler cannot check, the highest risk to parity, a save conversion, and an estimated 3 to 5 extra weeks. |

**Recommendation: A**, as long as the S1 spike confirms these points:

1. Lit, double-sided, back-side and shadow-casting meshes under `World` render pixel-identically to the bridge.
2. The SRP Batcher still batches them.
3. A skinned mesh under `World` deforms correctly.
4. A spot light under `World` points the right way (if not, spot and directional lights go outside `World`).
5. `Graphics.RenderMeshInstanced` with mirrored matrices either works or works with flipped culling.

If the spike fails, fall back to B. C stays possible later, one module at a time, if it is ever wanted. Rules under A:

- Game code reads and writes `localPosition`, `localRotation` and `localScale` relative to `World`.
- World-space questions go through `GameSpace`: `WorldPos(t)`, `ToUnity(v)`, `FromUnity(v)`, `EulerXYZ(x, y, z)` and
  `LookAtLocal`.
- Three's default Euler order is XYZ (R = Rx·Ry·Rz), but Unity's `Quaternion.Euler` is R = Ry·Rx·Rz. Single-axis
  rotations match; anything with two or more axes goes through `GameSpace.EulerXYZ`.
- Vectors passed to shaders (the sun's direction, the camera position, uniforms) go through `GameSpace.ToUnity`, as
  `MaterialCache.SetUniform` does today.

### 2.2 Scene graph and meshes

| Option | For | Against |
|---|---|---|
| **GameObject + `MeshFilter` + `MeshRenderer`, one per Three object, same hierarchy and names** | A 1:1 mapping, so the S0 scene dump compares old and new object by object. Editable in the editor. The same cost model as today (the bridge already makes these GameObjects). | About 1,200 to 3,600 GameObjects, as today. |
| `Graphics.RenderMeshInstanced` | One draw for many copies of a mesh. | Only helps where the same mesh repeats. Palms are unique: every trunk follows its own curve, and each has its own frond set and tint (`Scenery.cs:243-300`). The Cozy shaders have no instancing variants yet, there is no culling per instance, and there are no GameObjects to pick or debug. |
| BatchRendererGroup / GPU Resident Drawer | Instances ordinary MeshRenderers with the same mesh and material by itself. | Needs Forward+ (the renderer is Forward today, `m_RenderingMode: 0`). A rendering-path change is a parity risk, so it belongs in perf work. |

**Recommendation:** plain GameObjects with renderers, same hierarchy and same names. Then:

- **Static merging stays as it is.** `Merge.mergeStatic` and `mergeGeometries` run in `MeshKit` before the objects are
  made. Groups marked `syncStatic` today become merged meshes, or are combined with `StaticBatchingUtility.Combine`
  after they are placed.
- **Instanced meshes stay baked into one mesh**, as `MeshCache.GetBaked` does now, with the same `TEXCOORD1`/`TEXCOORD3`
  channels the shaders read. Footprints, tyre tracks and whitecaps, which re-bake on every change, move to
  `RenderMeshInstanced` or to range updates (`Mesh.SetVertexBufferData`) in S34, measured (Deck plan 3.9).
- **Palms:** each palm keeps its own meshes; coconuts merge per palm (Deck plan 4.3); the sway stays in `Cozy/PalmSway`.
  `RenderMeshInstanced` is not used for them.
- **Object types:**
  - `Group` becomes an empty GameObject.
  - `Sprite` becomes a quad with `Cozy/Sprite`, which turns it to face the camera in the shader. It is not a
    `SpriteRenderer`, and its bounds are set as `SceneSync.SyncSprite` sets them.
  - `Points` becomes quads with `Cozy/Points` or `Cozy/Particles`.
  - `LineSegments` becomes a mesh with `MeshTopology.Lines`.
  - `SkinnedMesh` becomes a `SkinnedMeshRenderer` with bone Transforms and bind poses, in game space under `World`.
- **Flags:** `visible` becomes `SetActive` (it hides children too, as Three's flag does). `renderOrder` becomes
  `Renderer.sortingOrder`. `frustumCulled = false` gets the same large bounds as today (real bounds come in S34).
  Cast and receive flags carry over; layers become `GameObject.layer`.
- **No `MonoBehaviour` per object.** The modules stay plain C# classes with `update(dt)`, called in order from
  `Game.frame`. That keeps today's update order and avoids a few thousand `Update` calls.

### 2.3 Maths types and `userData`

| Three | Native | Notes |
|---|---|---|
| `THREE.Vector3/2/4` (469 / 40 / 2) | `UnityEngine.Vector3/2/4` where it touches transforms or meshes; plain `double x, z` fields where game logic already uses them (player, items, saves) | **Three's vectors are mutable classes, Unity's are structs.** Code that keeps a reference and mutates it (`var p = obj.position; p.x = 5`), or chains in-place maths (`v.add(w).multiplyScalar(2)`), changes meaning silently. Each use is converted by reading it, never by search and replace (risk 5.5). |
| `THREE.Quaternion`, `Euler`, `Matrix4` (12 / 6 / 11) | `Quaternion`, `GameSpace.EulerXYZ`, `Matrix4x4` | Euler order: see 2.1. |
| `THREE.Color` (212) | `ColorUtil` returning linear `Vector4`/`Color` | Three turns hex sRGB into linear. Today's colours depend on that exact maths (`MaterialCache` pushes linear values with `SetVector`), so `ColorUtil` keeps it. |
| `THREE.MathUtils`, `JS` | `Mathf` / `System.Math`, `JS` | `JS.Round`, `JS.Random` and the rest stay, because the numbers depend on them. |
| `Box3`, `Sphere`, `Ray` | `Bounds`, `MeshKit.Bounds`, `Ray` | |
| `Clock` | `GameClock` (unscaled real time, or the fixed step under `-autoshot-fixedstep`) | |
| `Object3D.userData` (40) | Typed fields | Hit radii and owner links (gull, crab, item, camp kind) become a small `Pickable` component (or a collider-to-owner dictionary) that the picking returns. Animation bookkeeping (`s0`, `sy`, `base`) becomes fields in the module's own lists, such as `List<(Transform t, float s0)>`. |
| `traverse` (12) | `Walk(Transform, Action<Transform>)` helper | |

**Precision:** `MeshKit` computes in double, as the Three code did, and writes floats. Then the many seeded
rejection-sampling loops (palm spots, stones, islands) make the same choices, and objects do not appear or vanish at
the edges. Game logic stays in double. Only transform writes are cast to float, as the bridge casts them today.

### 2.4 Materials and shaders

The 17 Cozy shaders are already native URP HLSL shaders and work with the SRP Batcher. They reproduce Three's lighting
(Lambert diffuse over π, GGX specular), its ACES fit done per material, and its fog mixed in sRGB. Today
`MaterialCache` hashes every Three material every frame and pushes about 30 properties when something changed.

| Option | For | Against |
|---|---|---|
| **Keep the Cozy shaders; make materials once through a typed `MaterialKit`** | The pixels stay the same. It runs once, not every frame, and is SRP-Batcher-friendly. The shaders are already in place. | The game keeps its own shader family rather than URP Lit, which is fine for a stylised game. |
| URP Lit/Unlit plus URP tone mapping and fog | Off-the-shelf. | URP's ACES is not Three's fit, URP fog is mixed in linear space, the hemisphere light would have to become URP's ambient gradient, and the room environment a probe. The whole look would need re-tuning, which breaks the parity requirement. |
| Shader Graph re-implementations | Visual editing. | The same maths again, with more files and no gain. |

**Recommendation:** keep the shaders. `MaterialKit` builds `Material`s from small specs, sharing identical specs, and
uses `MaterialCache.Apply`'s rules unchanged: cull and shadow-cull from `side`/`shadowSide`, blend modes, render queue,
`_Flags`/`_Flags2`/`_Params`, and colours through `ColorUtil`. It also offers setters for the materials that change while
the game runs (lamps, haze, opacity fades), replacing the per-frame hashing. `clone()` becomes `new Material(m)`.

| Three material (constructors in game code) | Shader today | Native |
|---|---|---|
| `MeshStandardMaterial` (431) | `Cozy/Standard`, or its `customShader` | `Mat.Standard(color, rough, metal, emissive, map, side, ...)` → `Cozy/Standard` (lit model 1) |
| `MeshPhysicalMaterial` (5: a pearl, a gem and a compass glass among the loot; two tool lenses) | `Cozy/Standard`; transmission shown as alpha `1 − 0.75·t`; clearcoat and ior ignored | `Mat.Glass(...)` with the same alpha rule |
| `MeshLambertMaterial` (2: boat glass, grass) | `Cozy/Standard` model 2; grass `Cozy/Grass` | `Mat.Lambert(...)` |
| `MeshBasicMaterial` (50) | `Cozy/Basic` | `Mat.Basic(...)` |
| `SpriteMaterial` (6) | `Cozy/Sprite` | `Mat.Sprite(...)` on a quad (`_SpriteCenter` only when not 0.5) |
| `PointsMaterial` (1: stars) | `Cozy/Points` | `Mat.Points(size, sizeAttenuation)` |
| `LineBasicMaterial` (2: rain, drips) | `Cozy/Basic` on line topology | `Mat.Basic` on a `MeshTopology.Lines` mesh |
| `ShaderMaterial` (4) | `Cozy/SkyDome`, `Cozy/Particles` and `Cozy/ParticlesHeart`, `Cozy/Tracks`, `Cozy/Whitecaps` | Same shaders. Uniforms become material properties, set once or through setters (`uTime` as a global) |
| `customShader` on a standard or lambert material | `Cozy/Beach`, `Cozy/Grass`, `Cozy/Ocean`, `Cozy/OceanFoam`, `Cozy/PalmSway`, `Cozy/CaveRock`, `Cozy/FishSchool`, `Cozy/SharkBody` | Same shaders |
| Invisible materials (hit spheres) | Renderer switched off | No renderer at all: the object is only a pick target (2.9) |

**Shader audit under option A** (part of the step that converts each shader's module):

- Object-space maths sees z with the opposite sign from today: `Cozy/PalmSway` (the sway about the crown's x and z),
  `Cozy/SharkBody` (the bend), `Cozy/FishSchool`, `Cozy/Sprite` (the centre), and the baked-instance channels in
  `Cozy/Tracks` and `Cozy/Whitecaps`.
- World-space maths (ocean waves, sky dome, fog distance, grass wind) is unchanged, because world space is still
  Unity's.

### 2.5 Lights, shadows, fog and tone mapping

Today `SceneSync.ApplyLights`/`ApplyGlobals` gathers Three's lights every frame and sets Unity lights and the Cozy
globals. Native:

- **`Atmosphere` service** (S4). It owns `_CozyHemiSky/Ground/Up`, `_CozyAmbient`, `_CozyEnv` (the stand-in for the
  room environment, with `EnvironmentTuning`), `_CozyFogColor/Params`, `_CozyTone` and `_CozyScreen`, and sets them once
  per frame. During coexistence the bridge hands its Three lights to `Atmosphere`, so native lights and Three lights add
  up in one place and neither overwrites the other.
  - The hemisphere and ambient lights are not Unity lights at all, as today.
  - Unity's `RenderSettings.ambient*` and `RenderSettings.fog` stay unused.
  - URP post-processing stays off (HDR off), and ACES stays per material through `_CozyTone`.
- **Sun:** a native directional `Light`, placed outside `World` and aimed from position and target through `GameSpace`.
  It gets the same settings `SceneSync.EnsureLight` gives: `ForcePixel`, shadow bias 0.02, normal bias 0.4, and soft or
  hard shadows from `UrpSettings`. Its colour is set as sRGB, because Unity linearises `Light.color`; its intensity is
  Three's unchanged. It is assigned to `RenderSettings.sun`.
- **Point and spot lights:** native, with `range = distance` (or 1000), and spot angles computed as `SyncSpot` does.
  They keep URP's distance attenuation, which is what `CozyCore` uses today. The camp's switch-off beyond 30 to 34 m
  stays.
- **Shadows:** the URP asset is unchanged (4096 map, 2 cascades, 60 m, soft medium, plus the `handheld` and `low`
  presets). Each renderer keeps the same cast and receive flags. The small-caster rule (`UrpSettings.SmallCasterCutoff`)
  is applied when objects are placed and when quality changes, not every frame. `shadowSide` becomes `_ShadowCull`, as
  now. **No shadow setting changes unless you ask.**
- **Layers:** World, Bench and Photo, taken from today's per-scene layers, with each camera's culling mask set to match.

### 2.6 Text and textures drawn in code

There are 49 canvases. Of those, 31 are `CanvasTexture`s in the 3D world (signs, sand, cloud puffs, glows, nets, labels,
`OldPictures`). Seven are UI canvases (the map, the minimap's markers, the detector screen and waveform, the binocular
and viewfinder overlays, the binocular readout); the rest are scratch canvases.

| Option | For | Against |
|---|---|---|
| **Keep the software painter (`Canvas2D` → `TexturePainter`) for world textures** | The same pixels. The drawing code ports almost unchanged, because the API stays close to Canvas2D. Pixels can be painted off the main thread. | A CPU raster: it is fine once at load, but slow for anything redrawn every frame. |
| TextMeshPro for signs and labels, GPU drawing for the other textures | Sharper text, and quicker loading of large textures. | It looks different (font rendering, anti-aliasing), so the parity checks fail. Most of these textures are procedural art, not text. |
| Bake to PNG at edit time | Instant at runtime. | The textures that use `JS.Random` would be frozen to one variant, and the code would no longer be the source of truth. |

**Recommendation:**

- **3D world:** `TexturePainter` (the current rasteriser, renamed and detached from `HTMLCanvasElement`). It paints
  pixel arrays on the loader's worker thread. The main thread uploads them with `Texture2D.SetPixelData` + `Apply`, a
  slice at a time. Font glyphs need the main thread, as `CanvasFonts` does now: the glyph cache is warmed for the sign
  fonts at boot, and `MainThread.Run` stays as the fallback. `OldPictures` (1024² and 201 2D calls) goes the same way;
  caching it on disk is an optional S34 speed-up.
- **UI drawings:** native UI Toolkit drawing. Dynamic overlays (the waveform, the binocular and viewfinder frames, the
  telescope view, map markers) become `generateVisualContent` + `Painter2D`, as `MiniMapGpu` already does for the
  minimap. The big map's base, which is drawn from scenery data, stays a `TexturePainter` texture so it matches pixel
  for pixel, with markers drawn on top in `Painter2D`.
- **Photos:** a native camera renders into a `RenderTexture` with `AsyncGPUReadback`, and the JPG is encoded on a worker
  thread (Deck plan 3.9). The page tint used under water is mixed as `WebGLRenderer.CapturePhoto` mixes it.
- The TextMeshPro option stays open for later (decision 2).

### 2.7 UI: replacing `Dom` screen by screen

`Style.uss` (3,051 lines, 421 class rules) is already USS. The DOM layer builds VisualElements with those class names,
so a native screen that uses **the same element structure and class names keeps the look** without touching most of
the stylesheet.

| Option | For | Against |
|---|---|---|
| **UXML for fixed layouts, with C# controllers binding by name; C# item builders (with UXML item templates) for lists** | Editable in UI Builder. The structure is visible and diffable, and the class names carry over. | Two kinds of file per screen. |
| C# only (`new VisualElement()` plus `AddToClassList`) | The closest mechanical mapping from `el("div", "cls")`. | Not editable visually, and long build methods. |

**Recommendation: UXML plus C#.** UXML for the title menu, pause, settings, HUD, loading screen, inspect, result,
camp-modal frames, map frame and help. C# builders for the journal pages, letters, achievements, saves list, shop
items, Lost & Found and the photo board.

What the DOM layer does today, and its native replacement:

| DOM layer feature | Native replacement |
|---|---|
| `.hidden` forced like `!important` (`HTMLElement.ApplyDisplay`) | `UiX.Show(ve, bool)` setting `style.display`; the USS rule order is fixed where the old workaround hid a conflict |
| Pill radii (`FitPillRadius`) | Explicit radii in USS, or a `GeometryChangedEvent` helper on the few pill classes |
| Uppercase text (`DisplayText`) | Applied in C# when the text is set, for the uppercase classes |
| Label wrapping rules | Kept in USS (labels wrap; the nowrap classes repeat for labels) |
| z-index and `ReorderChildrenByZIndex` | Child order in UXML, plus `BringToFront` |
| `pointer-events: none` | `picking-mode="Ignore"` in UXML |
| `@keyframes` (`CssAnimations`) | USS transitions plus `UiAnim` (schedule-driven) for the six animations |
| Inline SVG icons (`SvgRasterizer`) | PNG sprites baked at build time by an editor script that calls `ShopArt.svg(...)` and the tool-icon strings through the rasteriser, as `EmojiSpriteAssetBuilder` does for emoji. No SVG at runtime |
| `<select>`, range inputs, the save-name text field | `DropdownField`, `Slider` and `TextField` styled by USS to look as today; the Steam on-screen keyboard hook stays on the save-name field |
| `querySelector`, `getElementById` | Typed references from `Q<T>("name")` once per screen |
| `onclick`, `addEventListener` | `clicked`, `RegisterCallback<...>` |
| `setTimeout`, `setInterval`, `requestAnimationFrame` (20 uses) | `GameTimers` on the game loop (scaled or unscaled), or `schedule.Execute` |
| The 1920x1080 page scaled by `min(w/1920, h/1080)`, and the UI size setting | `PanelSettings.ScaleWithScreenSize`, reference 1920x1080, `ScreenMatchMode.Shrink` (that is the min); UI size divides the reference resolution |
| `HoverFocus`, `menuKey`, `padNav` (one highlight for mouse, keys and pad) | A `MenuNav` service over VisualElement focus, keeping the game's own navigation and the bottom key bar (`setMenuBar`) |
| `EmojiText` | Kept. `Tools/Emoji/update_emoji.py` also scans `*.uxml` |

**Coexistence:** native screens are added to the same `UIDocument` root as the DOM page, so each screen can switch over
on its own. The DOM layer goes away only in S33.

### 2.8 Input, camera and the game loop

- **Loop:** `GameHost.Update` calls `MainThread.Pump`, `SteamService.Tick`, `input.poll`, `UiRoot.Tick` (timers and
  animations) and `game.frame()`, in that order. `WebGLRenderer.setAnimationLoop/Tick` and `SceneSync` disappear.
  Until S25 the bridge still syncs the Three objects that remain, through one explicit `ThreeBridge.Sync()` call at the
  end of `frame`.
- **Main camera:** a native `Camera` on a top-level `MainCamera` GameObject, outside `World`. Its pose comes from the
  game's camera state through `GameSpace.ToUnity`. It keeps fov 50 with zoom applied (as `ConfigureCamera` does), near
  0.1 and far 1000, and `UrpSettings.SetupCamera`. The far plane stays at 1000, because the night ships sail at z 740
  to 855. The `AudioListener` sits on this camera.
- **Other cameras:** the bench camera is on only in `INSPECT`. Photo and item-photo cameras render on request through
  `UrpSettings.RenderTo` (the URP render request it already uses). The one-pixel warm-up (`sendEverythingToGpu`) is kept
  for now, and moves to a back-buffer-matched target in S34 (Deck plan 3.10).
- **Render settings** move out of `WebGLRenderer`: shadows on/off, tone mapping and exposure, pixel ratio and render
  scale (`UrpSettings.SetRenderScale` with the same `devicePixelRatio` division).
- **Input** is already native (Input System polling in `InputManager`). Only `THREE.Vector2 pointer` and the raycaster
  change.

### 2.9 Picking (raycasts)

| Option | For | Against |
|---|---|---|
| **PhysX queries on primitive colliders for picking in the world; an exact CPU triangle ray for the two mesh-precise cases** | Native, fast, and allocation-free with `RaycastNonAlloc`. Most targets already have hit spheres (`hitRadius`). | Hover edges may differ by a pixel or two on complex camp props (a box or convex collider instead of every triangle; Deck plan 3.9 asks for that anyway). |
| A CPU triangle raycast everywhere (port of `Raycaster`) | Exactly today's results. | Not native, and slower (Deck plan 3.9). |

**Recommendation:** colliders on a `Pick` layer, with Physics in Script mode as today and `Physics.SyncTransforms()`
before queries. `MeshKit.Raycast` (a port of the triangle test) stays only for fitting the sand crust in
`LootGenerator` and for brushing in `CleaningStation`, where the exact hit point matters and the mesh changes while it
is brushed.

### 2.10 Saves

The save format is not changed at all.

- Under option A, game numbers are unchanged: `PlayerPose x, z, yaw, pitch`, `SavedItem x, z`, vehicle poses, flags,
  castles, and the beach grid indices in `dugPacked`.
- `SaveGame`, `Storage`, `Settings` and `StateManager` are in C4 (no change).
- The checks: every step that touches saved state runs `-autoshot-saves`, and `-autoshot-load` with saves written by
  the reference build (a day-5 save among them). Saves written by the branch must also load in the reference build,
  because players may move between the two while `main` ships first. That covers the beach, loot, vehicles,
  sandcastles, dog, flags and settings.
- Steam Cloud paths and keys (`cozy-treasure-hunter-saves-v3`, the slot prefix) stay as they are.

### 2.11 Loading without stalls (`Core/Prebuilt.cs`)

Today ten big pieces are built as Three object trees on a worker thread, because a Three tree is plain C#. The main
thread only draws the loading screen and its moving sea. Native GameObjects, `Mesh`es and `Texture2D`s must be created
on the main thread.

| Option | For | Against |
|---|---|---|
| **Two phases: plan off the main thread, place on it** | The heavy work (geometry, merges, conforming, the painter's pixels, mesh buffers) stays off the main thread. The main thread only creates objects and uploads, a few ms per frame. | Builders produce a description, not live objects, so they need one extra concept (a `Proto` node). |
| Build everything on the main thread, sliced with coroutines | Simplest. | About 3.6 s of world building lands on the main thread (10 s or more on a Deck), and the loading sea stutters. |
| Jobs and Burst for mesh generation | Fastest. | The generators are managed, class-based code, so it is a rewrite; it could be a later speed-up for the beach grid. |
| Bake the world into prefabs and meshes at edit time | Instant loading, editable in the editor. | Code stops being the source of truth (YAML merges, assets to regenerate), and the `JS.Random` details freeze. |

**Recommendation: two phases.**

1. **Plan (worker thread).** A builder's constructor stays where it is and fills a tree of `Proto` nodes, plain C#
   objects that work like today's Object3D tree: name, local transform in game numbers, a `MeshData` or a mesh handle,
   a `MatSpec`, shadow and layer flags, children, and an optional `onPlaced(GameObject)`.
   - Mesh buffers are written straight into `Mesh.AllocateWritableMeshData` arrays on the worker.
   - The painter's pixels are computed there too.
   - `Prebuilt.PIECES` keeps its order, and a piece that fails still falls back to the main thread.
2. **Place (main thread, budgeted).** A `Realizer` coroutine walks the Protos. It creates the GameObjects, calls
   `Mesh.ApplyAndDisposeWritableMeshData`, creates the `Material`s from specs, and uploads textures. It stops each frame
   after a budget: 4 ms by default, settable for the Deck. Then each builder's `OnPlaced` stores the `Transform` fields
   its `update` code uses (`group`, `lid`, `lamp`, ...).
3. **Things made after loading** (dug holes, sandcastles, the photo board) use the same path with no budget.

The S1 spike measures what one GameObject with a renderer costs to create. That sets the default budget and checks
that the loading sea keeps its frame rate. The harness already logs slow loading frames; S0 adds loading pacing (worst
frame, and frames over 33 ms), compared with the reference.

### 2.12 Coexistence: how every step stays playable

Steps convert one unit at a time while the rest is still Three. Two temporary bridges make that work; S25 deletes both.

- **`NativeMount`:** a Three `Object3D` that the bridge syncs as a transform-only node. A native subtree hangs under
  its GameObject through a `(1, 1, −1)` junction. The parent's node is M·W·M, the junction adds M, so the result is
  M·W, the same convention as `World`. A converted module (the pier, say) can then sit under an unconverted Three
  parent, and the parent's `visible` (`SetActive`) still hides it.
- **`ThreeMount`:** the reverse. A native Transform under which the bridge syncs a Three subtree, for example
  unconverted loot models inside a converted camp. `SceneSync.SyncNode` already takes any parent Transform.

Converted modules give callers Transforms (`camp.group` becomes a `Transform`). Each step updates those call sites in
`Game.cs` and the other modules in the same commit. S5 tests both mounts with a synthetic module before any real
module relies on them.

---

## 3. Conversion order

Each step is one or more commits on the branch. Each ends with a build, the step's suites (section 4) passing, and an
entry in `unity/NATIVE_LOG.md`: what changed, the hours spent by activity, the comparison report, and any differences
accepted. Merge `main` into the branch at each phase boundary and re-check the baseline (4.1).

| Step | What | Depends on | Suites (4.2) |
|---|---|---|---|
| **Phase 0: measure first** | | | |
| S0 | Parity harness: `-autoshot-seed`, `-autoshot-fixedstep`, `-autoshot-scenedump`, loading pacing, `Tools/Parity/` (run, compare, perf), the reference build and its shots, a noise-floor run, the thresholds | - | all, twice on the reference |
| S1 | Spikes behind `-spike-native` (throwaway scene objects, deleted afterwards): option A against the bridge for lit, double-sided, back-side, shadow and skinned meshes, a spot light, `RenderMeshInstanced` with mirrored matrices, SRP Batcher batch counts, and the cost to create one GameObject and renderer. Ends with option A or B (2.1) and the loader's budget | S0 | spike shots |
| **Phase 1: native kit beside Three (the game does not change)** | | | |
| S2 | `World` root, `GameSpace` (`ToUnity`, `FromUnity`, `EulerXYZ`, `LookAtLocal`, `WorldPos`), `ColorUtil`, `JS` moved to Core, `GameClock`, `Walk` | S1 | core |
| S3 | `MeshKit`: `MeshData`, every generator, curves, earcut, extrude, lathe, tube, `mergeGeometries`/`mergeVertices`, `Merge.mergeStatic`, `Conform` for `MeshData`, the instance bake and point quads, `ToMesh` and the writable-mesh-data path. A self-check (`-autoshot-meshkit`) compares vertex, index and attribute buffers with the Three generators for a parameter table (tolerance 1e-6) | S2 | core + meshkit |
| S4 | `MaterialKit` (specs, sharing, setters, `MaterialCache.Apply`'s rules), `Atmosphere` (the globals; the bridge feeds its lights in), native light helpers, `ShadowPolicy` | S2 | core |
| S5 | `NativeMount`/`ThreeMount`, `Proto` + `Realizer`, `Prebuilt` holding both kinds of piece, a synthetic test module mounted in the scene (dumped, then removed) | S3, S4 | core + scenedump |
| S6 | `TexturePainter` (Canvas2D without the DOM types; pixels off-thread; glyph warm-up; upload) and the editor-only icon baker | S2 | core + ui |
| S7 | `Picking`: the `Pick` layer, colliders, a `Pickable` owner map, `MeshKit.Raycast` | S2 | core + birds |
| **Phase 2: loop and camera** | | | |
| S8 | `GameHost` drives `Game.frame`; native main camera; render settings out of `WebGLRenderer`; `ThreeBridge.Sync()` at the end of the frame; photo capture through the native camera; bench and item-photo cameras through the bridge until S23 | S4, S5 | core + photo + crust + perf |
| **Phase 3: the world, one unit per step (track A)** | | | |
| S9 | **W1 pilot**: Stones, Flags, Pier, Whitecaps (one custom shader, one instanced mesh, one canvas). **Calibration gate:** measure the hours per line and per `THREE.` use, re-project section 6 and update this plan before S10 | S8, S3 to S7 | core + pier + holes |
| S10 | W2 small props (8 files) | S9 | core + island + islands + telescope + dolphins + gear |
| S11 | W3 rain, tracks, grass, ecology, birds, sandcastles | S9 | core + rain + birds + gullshadow + holes + saves |
| S12 | W4 boat, dog (skinned), modern ships, old ship | S9 | core + dograin + ships + dolphins + saves |
| S13 | W5 beach and `Conform`, ocean, sky (sun, hemisphere, fog, dome, clouds, stars, sun and moon sprites): the hardest parity step | S9 | all world suites + saves |
| S14 | W6 palms and scenery (`Cozy/PalmSway` audit) | S13 | core + island + pier + shadow |
| S15 | W7 camp (8 canvases, 3 lights, clickables) | S13 | core + shop + photo + rain + perf |
| S16 | W8 islands (haze, CPU palm sway, landmarks) | S14 | islands + island + telescope + cove |
| S17 | W9 lighthouse (beams, night ships, intro) | S13 | lighthouse + ships |
| S18 | W10 old house and `OldPictures` (painter) | S13 | house + dograin |
| S19 | W11 under the sea, 6 files (`Cozy/FishSchool`, `Cozy/SharkBody` and `Cozy/CaveRock` audit, the hoard bake) | S13 | dive + sealife + fish + cove |
| S20 | W12 underground | S13 | core (`perf: underground`) + land |
| S21 | W13 loot: `LootGenerator` and `LootSpawner` (item models, glass, the crust raycast) | S13 | core + crust + gear + saves + load |
| S22 | M1 tools in hand: view model, hand, dig, detector (3D part), fishing, arrows, bullets | S21 | core + gear + fish + birds + smash + sweep |
| S23 | M2 workbench and item photos (their own scene, camera and lights) | S21 | crust + photo + photo-alt + shop |
| S24 | C3 particles and the opening | S13 | opening + rain + core |
| S25 | **Remove Three**: what is left of `Game.cs`, `InputManager`, `AutoShot`; delete `Three/` except the kit; `BridgeStats` becomes `FrameStats`; drop the mounts; update `MIGRATION.md` and `PORT_STATUS.md` | S10 to S24 | all |
| **Phase 4: UI (track B, can run alongside Phase 3 from S8)** | | | |
| S26 | UI foundation: `UiRoot` (scaling, fonts, UI size), `UiX`, `UiAnim`, `GameTimers`, `MenuNav`, the baked icons; native screens beside the DOM page | S6, S8 | core + ui + pad |
| S27 | I1 loading screen and `LoadingSea` | S26 | core (loading shot, review only) + loading pacing |
| S28 | I2a HUD: chips, tool badge, d-pad tools, hints with `Glyphs`, toasts, prompts, crosshair, draw bar, inspect and result | S26 | core + ui + pad + gear |
| S29 | I2b title, pause, settings, saves, achievements, help, credits | S26 | ui + tour + pad + settings + saves + layout |
| S30 | I2c journal and letters | S29 | tour + ui |
| S31 | I3 camp screens: `CampModal`, `ShopArt` | S26 | shop + tour + photo |
| S32 | I4 map, minimap, detector screen, binocular and viewfinder overlays, `TelescopeView` | S26 | islands (map shots) + telescope + gear + ui |
| S33 | **Remove Dom**: delete `Dom/`, clean up `Style.uss` (drop the DOM-era workarounds), update the docs | S27 to S32 | all |
| **Phase 5: finish** | | | |
| S34 | Performance pass (IL2CPP, the perf suite against the baseline; optional real bounds, track instancing, Forward+/GPU Resident Drawer A/B, warm-up), then Steam Deck sign-off with the owner (`STEAM_DECK_PLAN.md` Phase 5 against this build) | S25, S33 | all + deck + perf |

**Why this order:**

- The harness comes first, because nothing can be judged without it.
- The kit is built and proven while the game is still unchanged.
- The camera and loop come early, because every later step then sees the real native rendering path.
- The pilot measures the real rate before the bulk of the work.
- Terrain and atmosphere (S13) come before the places that stand on them. They own the sun, fog and environment that
  every later picture depends on. Doing them early also flushes out lighting differences while only a few modules are
  native.
- Loot comes before tools, because the tools hold items.
- UI is independent of the 3D layer (it shares only `Game.cs` and the painter), so it can run as a second track. Two
  writers never touch the same files: track A owns `World/`, `Mechanics/`, `Core/` except the menu code, and
  `Three/`; track B owns `UI/`, `Dom/`, `Style.uss` and the menu and navigation code in `Game.cs` (`menuKey`, `padNav`,
  `menuTick`, the overlay helpers).
- Optional, after S33 and outside the estimate: split `Game.cs` (6,971 lines) into partial files by concern.

---

## 4. Verification for every step

### 4.1 One-time setup (S0)

**Making two runs comparable.** Today two runs of the same build differ, because `JS.Random` is unseeded, `Prebuilt`'s
worker and the main thread share one RNG, and the clock is real time (`Clock.getDelta` uses `JS.Now`). S0 adds three
flags:

- `-autoshot-seed <n>`: seeds `JS.Random` at `BeforeSceneLoad`. It gives each thread its own stream, and each
  `Prebuilt` piece its own (seed·1000 + piece index), so the order threads run in no longer matters. It is test-only;
  without the flag nothing changes.
- `-autoshot-fixedstep`: `Time.captureDeltaTime = 1/60`, and `JS.Now` returns a virtual clock that moves on by exactly
  that each frame. The game's state at each shot then depends only on the frame count, not on how fast the PC draws.
  Visual suites use it; perf suites never do.
- `-autoshot-scenedump <file>`: writes every active Renderer and Light in the loaded scene as JSON.
  - Per renderer: the hierarchy path (with the bridge's `"name (type)"` names, which native objects keep), world AABB,
    vertex and triangle counts, a hash of the world-space vertex positions rounded to 1e-4, shader, render queue, key
    material values (`_Color`, `_Emissive`, `_Rough`, `_Metal`, `_Flags`, `_Flags2`, `_Params`, cull and blend), and
    cast, receive and sorting order.
  - Per light: type, world position and direction, colour, intensity, range, angles and shadows.
  - The Cozy globals.
  - The bridge's objects are real Unity objects, so the reference and the branch dump the same kind of thing, and
    differences show up per object and per number rather than as pixels.

S0 also adds a `[AutoShot] context:` line (Deck plan 0.3) and a loading pacing line (worst loading frame, frames over
33 ms, time to "ready to play").

**Tools** (`unity/Tools/Parity/`, plain Python 3 with Pillow and numpy, both already installed on the dev PC; PowerShell
for running):

- `suites.json`: the named flag sets (4.2).
- `run_suites.ps1 -Exe <Tidefinder.exe> -Out <dir> -Suites <list> [-Deck]`: runs each suite in its own process with
  `-autoshot <dir>\<suite> -autoshot-seed 1 -autoshot-fixedstep -screen-width 1280 -screen-height 720
  -screen-fullscreen 0 -logFile <dir>\<suite>\Player.log` and its own flags. With `-Deck` it uses 1280x800 and
  `-force-deck`. It records the exit codes.
- `run_perf.ps1 -Ref <folder> -Branch <folder> -Rounds 3 -Out <dir>`: runs `-autoshot-perf -autoshot-novsync` (and
  `-autoshot-walk`) without a fixed step. It alternates reference and branch (ABABAB), so the PC's state affects both
  the same way.
- `compare.py <ref dir> <branch dir> [--thresholds t.json] [--report report.html]` compares:
  - **Images:** each PNG against its namesake. Size mismatch, mean absolute difference (0 to 255), the share of pixels
    with any channel off by more than 16, the 99th-percentile difference, and SSIM on luminance. It writes a heat-map
    PNG per shot and an HTML page with reference, branch and diff side by side, plus a flicker toggle.
  - **Logs:** new `Exception` or error lines.
  - **Facts:** the non-timing `[AutoShot]` lines (where the boat floats, where finds lie, coins, hole depths, the save
    list, telescope finds), compared after rounding numbers to 3 significant digits.
  - **Scene dumps:** per-object differences beyond 1e-3 m or 1e-3 in material values; objects missing or extra.
  - **Perf (`--perf <dir>`):** a table of fps, `game+sync` (or the branch's `game`) ms, render thread, GPU, the median
    and 99th-percentile frame, frames over 1.5 refreshes, and time to ready. It reports medians over the rounds and the
    change against the reference.

**The reference.** Tag the S0 commit `native-baseline`; it is `main` plus the harness. Build it with IL2CPP and keep the
build outside the repository:

```powershell
git tag native-baseline
```

```powershell
npm run dist:unity
```

```powershell
robocopy release\unity-win-unpacked C:\TidefinderParity\ref /MIR
```

```powershell
powershell -File unity\Tools\Parity\run_suites.ps1 -Exe C:\TidefinderParity\ref\Tidefinder.exe -Out C:\TidefinderParity\shots\ref -Suites all
```

```powershell
powershell -File unity\Tools\Parity\run_suites.ps1 -Exe C:\TidefinderParity\ref\Tidefinder.exe -Out C:\TidefinderParity\shots\ref2 -Suites all
```

```powershell
python unity\Tools\Parity\compare.py C:\TidefinderParity\shots\ref C:\TidefinderParity\shots\ref2 --calibrate unity\Tools\Parity\thresholds.json
```

The second reference run is the **noise floor**. `--calibrate` writes per-shot thresholds of twice the measured noise,
with absolute floors: mean difference 0.75, 0.3% of pixels off by more than 16, SSIM 0.99. **S0 is done when two
reference runs agree within those floors on every shot except `0-loading.png`**, which is taken part way through
loading and is marked review-only. If they do not agree, the harness is not deterministic enough yet, and S0 is not
finished.

If `main` gets fixes, merge `main` into the branch. Then rebuild the reference from a worktree of the new `main` with the
harness commits cherry-picked on top, and re-take the reference shots (decision 6).

Before the first build in this worktree (it has no `unity/Library` yet), copy the main checkout's `unity\Library` across
to skip a full asset import (same Unity version). Close the editor and the game first, as for every build.

### 4.2 Suites

| Suite | Flags |
|---|---|
| core | (none: loading, title, achievements, camp, sea, and camp and sea measures), `-autoshot-newgame`, `-autoshot-islands`, `-autoshot-ui` |
| world | `-autoshot-island`, `-autoshot-pier`, `-autoshot-shadow`, `-autoshot-holes`, `-autoshot-rain`, `-autoshot-birds`, `-autoshot-gullshadow`, `-autoshot-dolphins`, `-autoshot-ships`, `-autoshot-lighthouse`, `-autoshot-house`, `-autoshot-dograin`, `-autoshot-cove`, `-autoshot-land`, `-autoshot-leap`, `-autoshot-telescope`, `-autoshot-opening` |
| under | `-autoshot-dive`, `-autoshot-sealife`, `-autoshot-fish` |
| items | `-autoshot-crust`, `-autoshot-gear`, `-autoshot-photo`, `-autoshot-photo-alt`, `-autoshot-smash`, `-autoshot-sweep`, `-autoshot-shop` |
| ui | `-autoshot-ui`, `-autoshot-tour`, `-autoshot-pad`, `-autoshot-settings`, `-autoshot-layout` |
| saves | `-autoshot-saves`; `-autoshot-load <id>` with the reference's saves and a day-5 save copied into `<out>\saves` first; and the reverse check (the branch's saves loaded by the reference build) |
| perf | `-autoshot-perf -autoshot-novsync`, `-autoshot-walk` (through `run_perf.ps1`, IL2CPP builds only) |
| deck | core + ui + perf at 1280x800 with `-force-deck` (and UI size 130%) |
| meshkit, scenedump | the S3 self-check; `-autoshot-scenedump` taken at the camp, at sea and on each island |

What each flag photographs is documented in `MIGRATION.md` ("Smoke test") and in `Core/AutoShot.cs`. "All" means every
suite above.

### 4.3 Per step, exactly

1. Check that the branch is the native branch, then build. Use `npm run dist:unity:quick` (Mono) for the visual suites.
   Use `npm run dist:unity` (IL2CPP) for any step with `perf` in its suites, and at least once per phase. The build
   must have no `error CS`, and its log must end with `Exiting batchmode successfully`.
2. Run the step's suites:

   ```powershell
   powershell -File unity\Tools\Parity\run_suites.ps1 -Exe release\unity-win-unpacked\Tidefinder.exe -Out C:\TidefinderParity\shots\S13 -Suites core,world,under,saves
   ```

3. Compare:

   ```powershell
   python unity\Tools\Parity\compare.py C:\TidefinderParity\shots\ref C:\TidefinderParity\shots\S13 --thresholds unity\Tools\Parity\thresholds.json --report C:\TidefinderParity\shots\S13\report.html
   ```

4. For perf steps (IL2CPP):

   ```powershell
   powershell -File unity\Tools\Parity\run_perf.ps1 -Ref C:\TidefinderParity\ref -Branch release\unity-win-unpacked -Rounds 3 -Out C:\TidefinderParity\perf\S13
   ```

   ```powershell
   python unity\Tools\Parity\compare.py --perf C:\TidefinderParity\perf\S13
   ```

5. After a Mono build, restore the two files it rewrites before committing:

   ```powershell
   git checkout -- unity/ProjectSettings/ProjectSettings.asset unity/Assets/Resources/UI/Emoji.asset
   ```

### 4.4 Pass criteria

A step passes when all of these hold:

- **Build and run:** every suite exits with 0, and there are no new exceptions or errors in any `Player.log`.
- **Pictures:** every shot is within its calibrated thresholds (PASS). A shot outside them is REVIEW. The agent then
  opens the reference, branch and diff images, and either fixes the cause or records it in `NATIVE_LOG.md` as an
  accepted difference with the reason. Any difference a player could see waits for your OK (decision 5). A missing
  shot, a size mismatch or SSIM below 0.9 is FAIL.
- **Facts:** the same as the reference after rounding. A different fact means a behaviour change and blocks the step.
- **Scene dump** (steps that convert world objects): every converted object matches its reference object within
  tolerance, and none are missing.
- **Perf** (IL2CPP, novsync, median of 3 rounds, per `perf:` scene):
  - fps no more than 5% lower;
  - game ms (`game+sync` on the reference) no more than 5% higher;
  - 99th-percentile frame no more than 10% higher;
  - no more frames over 1.5 refreshes;
  - time to "ready to play" no more than 10% longer.
  - Read the main-thread figure with care. With VSync on it can go negative (a recent run logged −0.68 ms), so compare
    only novsync runs, and use the game ms and the frame times as the main signal.
- **Saves** (where listed): saves written by the reference load on the branch with the same facts and pictures, and
  the reverse.
- **At each phase boundary** you play for about 15 minutes, by hand: digging, detector, cleaning, the camp shop, the
  boat, diving, the map, saving and loading, with a controller and with mouse and keyboard.

**Why the perf baseline holds:** `FrameStats` (S25) keeps `BridgeStats`' log format (`game+sync N ms`, the slowest
parts) and reports the game's whole frame under the same label, so `compare.py` parses the reference and the branch
the same way.

---

## 5. Risks

| # | Risk | Likelihood / impact | Mitigation |
|---|---|---|---|
| 5.1 | **Lighting and colour parity.** The shaders stay, so the risk is in what they are fed: hex to linear colour, `Light.color` handed over as sRGB, hemisphere, ambient and environment globals, fog, exposure, vertex colours, and the sRGB flags of painter textures. | Medium / high | `ColorUtil` and `MaterialKit` reuse the bridge's maths line for line. `Atmosphere` owns the globals from S4. The scene dump compares the material values and globals numerically. S13 (sun, fog, environment) comes early and runs every suite. |
| 5.2 | **Shadows:** per-renderer flags, the small-caster rule, `shadowSide`, biases, and the shadow pass's winding under a negative-scale root. | Medium / medium | The S1 spike shoots shadows both ways. `-autoshot-shadow` and `-autoshot-gullshadow` in the world suite. No changes to the shadow settings. |
| 5.3 | **Shader parity:** object-space signs under option A (palm sway, shark body, fish school, sprite centre, baked instance channels), and vectors handed to shaders in the wrong axes. | Medium / medium | The audit list in 2.4, done in the step that owns each shader. Uniforms only through `GameSpace.ToUnity`. The S1 spike includes `Cozy/PalmSway`. |
| 5.4 | **Handedness slips:** Euler order, `lookAt`, world positions under `World`, quaternions. | Medium / medium | The `GameSpace` helpers, a written rule (2.1), the per-object scene dump, and the fact lines in `-autoshot-islands`, `-autoshot-holes` and `-autoshot-birds`. |
| 5.5 | **Class-to-struct maths:** a Three `Vector3` shared by reference becomes a copy. | High / low to medium (silent behaviour changes) | Convert by reading, not by search and replace. The fact lines and scene dump catch moved objects. Review rule: every converted `THREE.Vector3` field gets a look at its aliases. |
| 5.6 | **UI layout differences:** the DOM layer's special behaviour (display enforcement, pill radii, uppercase, label wrapping, z-index, scroll views, focus) has to be rebuilt. | Medium / medium | Same class names and structure. `-autoshot-ui`, `-tour`, `-layout`, `-settings`, `-shop` and `-pad` at 1280x720, and the deck suite at 1280x800 with 130% UI. The UI track converts screen by screen beside the old page. |
| 5.7 | **Steam Deck:** the new UI has to keep controller navigation and the on-screen keyboard. Load time on the Deck (the placing budget), and Proton with D3D11 or D3D12. | Medium / high (Verified status) | The deck suite in every UI step. The loader's budget is settable and measured in S1. S34 runs `STEAM_DECK_PLAN.md` Phase 5 on the real Deck. |
| 5.8 | **Performance targets:** a steady 60 fps on the Deck at 1280x800 (main thread ≤ 10 ms, GPU ≤ 12 ms, 99th percentile ≤ 20 ms, no garbage). Removing the sync saves about 0.65 ms of the game's 0.98 ms on the dev PC at the camp. But direct transform writes, UI Toolkit changes and placing objects at load could add cost elsewhere. | Low to medium / high | The perf suite in every IL2CPP step against the reference, with regressions over 5% blocking. Object and draw counts are logged per scene. The Deck plan's GPU items (shadow preset, MSAA, draw calls) are unchanged and still apply. |
| 5.9 | **Float vs double** changes seeded placement at the edges. | Low / medium | `MeshKit` computes in double; game logic stays double; the scene dump checks object counts. |
| 5.10 | **The long-lived branch drifts from `main`.** `Game.cs` is the conflict hotspot (6,971 lines, touched by both tracks). | Medium / medium | Merge `main` at each phase boundary. Rebuild the reference after a merge. Track A and track B own different parts of `Game.cs`. |
| 5.11 | **The temporary mounts** have bugs that look like conversion bugs. | Low / medium | S5 tests them with a synthetic module and the scene dump before any real module relies on them. Both go in S25. |
| 5.12 | **The harness is not deterministic enough** (threads, GPU, timing). | Medium / high (every check gets weaker) | S0's done-when requires the noise floor first. If some shots stay noisy, mark them review-only and lean on the scene dump and the facts. |
| 5.13 | **The estimate itself** (section 6). | Medium / medium | The S9 calibration gate, hours logged by activity in `NATIVE_LOG.md`, and a re-projection at each phase boundary. |

---

## 6. Estimate

### 6.1 What was measured

- **Size:** the line and use counts in section 1, from `inventory.py`.
- **Build times**, from the build logs in the main checkout: the IL2CPP player-build phase takes 64 to 70 s
  (`PlayerBuildInfo` 64,148 and 69,983 ms), and an incremental Mono player build takes 2.6 s. Both are on top of editor
  start-up and script compilation, which the logs do not time (about a minute, estimated).
- **Smoke runs:** a launch reaches "ready to play" in 7.4 s on the dev PC (the most recent `Player.log`), and the core
  suite runs four launches.
- **One verification round** (quick build, core suite plus one module suite, compare): about 10 to 15 minutes.

### 6.2 Assumptions

- **One agent-day** is one session's 6 to 8 hours of focused work, including builds, runs and fixes. Your review time
  is not counted.
- **Conversion rate, in lines a day including verification:**

  | Class | Fast end | Slow end |
  |---|---:|---:|
  | A: builder code (mostly constructors placing meshes) | 1,200 | 800 |
  | B: animated, shader-bound or coupled code | 700 | 450 |
  | C: UI screens | 800 | 550 |

- **Overhead per step** (setup, suites, report, log, commit): 0.5 to 1 day, plus extra where noted (shader audits,
  lights, canvases).
- **Foundation steps** (S0 to S8, S25, S26, S33, S34) are new or deleting code, so they are estimated directly.
- **The slow end includes the contingency** for parity fixes found late. These are planning rates; S9 replaces them
  with measured ones.

### 6.3 Per step

| Step | Lines | Class | Days (fast to slow) | Model and effort |
|---|---:|---|---:|---|
| S0 Parity harness and baseline | - | new | 2 - 3 | Strongest, high (determinism and the compare design) |
| S1 Spikes | - | new | 1 - 2 | Strongest, high |
| S2 `GameSpace`, `World`, `ColorUtil` | - | new | 1 - 1.5 | Strongest, high (sets conventions) |
| S3 `MeshKit` and its self-check | ~2,800 moved | port | 3 - 4 | Medium (mechanical port); strongest for the check |
| S4 `MaterialKit`, `Atmosphere`, lights | - | new | 2 - 3 | Strongest, high |
| S5 Mounts, `Proto`/`Realizer`, `Prebuilt` | - | new | 2 - 3 | Strongest, high |
| S6 `TexturePainter`, icon baker | ~1,700 moved | port | 1.5 - 2 | Medium |
| S7 `Picking` | - | new | 1 - 1.5 | Medium |
| S8 Loop and camera | - | new | 2 - 3 | Strongest, high |
| S9 W1 pilot props | 567 | A + 0.5 | 1.5 - 2.5 | Strongest for the first module (sets the pattern), then medium |
| S10 W2 small props | 1,749 | A | 2 - 3 | Lower effort, mechanical |
| S11 W3 weather, tracks, critters | 1,642 | B | 3 - 4.5 | Medium |
| S12 W4 vehicles, dog, ships | 2,251 | B | 3.5 - 6 | Medium; strongest for the dog's skinning |
| S13 W5 terrain and atmosphere | 1,803 | B + 1.5 | 4.5 - 7.5 | Strongest, high |
| S14 W6 palms and scenery | 596 | B | 1.5 - 2.5 | Medium |
| S15 W7 camp | 1,404 | A + 0.5 | 2 - 3.5 | Medium |
| S16 W8 islands | 1,330 | A + 0.5 | 2 - 3.5 | Medium |
| S17 W9 lighthouse | 1,077 | B | 2 - 3.5 | Medium |
| S18 W10 old house and pictures | 1,247 | A + 0.25 | 2 - 3 | Lower effort, mechanical |
| S19 W11 under the sea | 4,040 | B + 0.5 | 7 - 10.5 | Medium; strongest for the three shader audits |
| S20 W12 underground | 693 | A | 1 - 2 | Lower effort, mechanical |
| S21 W13 loot | 1,969 | B + 0.25 | 3.5 - 6 | Medium (save checks) |
| S22 M1 tools in hand | 2,549 | B | 4 - 6.5 | Medium |
| S23 M2 workbench and item photos | 408 | B + 0.5 | 1.5 - 2.5 | Strongest, high (a second scene and camera) |
| S24 C3 particles and opening | 431 | B | 1 - 2 | Medium |
| S25 Remove Three | - | delete | 2 - 3 | Strongest, high (final integration) |
| S26 UI foundation | - | new | 3 - 4 | Strongest, high |
| S27 I1 loading screen | 514 | C | 1 - 2 | Lower effort, mechanical |
| S28 I2a HUD | ~1,050 | C + 0.25 | 2 - 3.5 | Medium |
| S29 I2b menus and settings | ~900 | C + 0.5 | 2 - 3.5 | Medium |
| S30 I2c journal and letters | ~300 | C | 1 - 1.5 | Lower effort, mechanical |
| S31 I3 camp screens | 1,210 | C | 2 - 3 | Medium |
| S32 I4 map, minimap, telescope, overlays | 1,784 | B | 3 - 5 | Medium; strongest for the `Painter2D` design |
| S33 Remove Dom | - | delete | 1.5 - 2 | Medium |
| S34 Perf pass and Deck sign-off | - | measure | 3 - 5 | Strongest, high |
| **Total** | | | **about 78 - 125** | |

### 6.4 Calendar

| Path | Agent-days | Months, at 5 working days a week |
|---|---:|---:|
| Single track (everything in sequence) | 78 - 125 | 3.9 - 6.2 |
| **Two tracks**: S0 to S8, then track A (S9 to S25) alongside track B (S26 to S33), then S34. The critical path is S0 to S8, plus track A, plus S34 (track B's 15.5 to 24.5 days fit inside track A) | **63 - 100** | **3.2 - 5.0** (about 4 expected) |

More than two tracks is possible for the world steps after S13, since S14 to S20 are mostly independent. But `Game.cs`
and the shared kit make merges costly, so it is not recommended before the S9 calibration shows the merge cost.

**Re-projection at S9:**

- Measured rate = (pilot hours − overhead hours) ÷ 567 lines.
- Multiply the class rates by the ratio of the measured rate to the class A planning rate, and recompute 6.3.
- Do it again at the end of S13, the first class B step with shaders.
- If the measured rate comes out about 1.5 times the fast end, the two-track calendar falls to about 2.5 months.

### 6.5 Which steps need the strongest model

- **Strongest model, high effort:** S0, S1, S2, S4, S5, S8, S13, S23, S25, S26, S34. Also the first module of S9, the
  shader audits in S19, the dog in S12, and the `Painter2D` design in S32. They set conventions, carry parity risk, or
  wire systems together.
- **Medium:** S3, S6, S7, S11, S14 to S17, S21, S22, S24, S28, S29, S31, S33.
- **Lower effort, mechanical:** S10, S18, S20, S27, S30, and the bulk of S19 once its shaders are done. These apply the
  pattern S9 set to builder code. S9's commit and `NATIVE_LOG.md` entry are the template to hand to those sessions.

---

## 7. Decisions

The owner accepted the recommended choice for every open decision on 2026-11-23.

| # | Decision | Chosen | Not chosen |
|---|---|---|---|
| 1 | Handedness (2.1) | **A: a mirrored world root** that keeps the game's numbers, meshes and saves as they are. S1 must confirm it; if the spike fails, fall back to B and note that here. | B, converting at every write; C, native left-handed numbers everywhere |
| 2 | Text and textures drawn in code (2.6) | **Keep the CPU painter** (`TexturePainter`) for world textures and signs, for exact parity. UI drawings move to `Painter2D`. | TextMeshPro for signs and GPU drawing for the rest (may be revisited after the rewrite) |
| 3 | UI authoring (2.7) | **UXML for fixed layouts, C# for lists**, same class names as `Style.uss` | C# only |
| 4 | Two tracks (6.4) | **Yes.** A second session runs the UI track (S26 to S33) from S8 on, with the file ownership in section 3. | A single track |
| 5 | Sign-off on visible differences (4.4) | **The owner** approves REVIEW items at each phase boundary, from the comparison report. The agent records each one in `NATIVE_LOG.md` meanwhile. | The agent decides alone |
| 6 | The baseline if `main` changes (4.1) | **Rebuild the reference** from new `main` plus the harness commits, cherry-picked in a separate worktree. The harness stays off `main`. | Harness flags on `main` too |
| 7 | Branch name | **Stays `claude/native-unity-d8a3b9`** | A rename |
