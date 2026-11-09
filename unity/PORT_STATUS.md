# Port status

Each TypeScript file and where it stands in the Unity port (`6000.3.28f1`, URP). All files are translated line-for-line to C# 9.0 (`.NET Standard 2.1`) and verified with zero compile errors via the Roslyn C# compiler.

## Foundation (new for Unity)

| Piece | File | State |
|---|---|---|
| JavaScript number helpers | `Assets/Scripts/Three/JS.cs` | done |
| Three.js maths, scene graph, geometries, curves, earcut, extrude | `Assets/Scripts/Three/**` | done |
| Software 2D canvas for `CanvasTexture` and UI canvases, with text | `Three/Textures/Canvas2D.cs`, `CanvasFonts.cs` | done |
| Scene to Unity bridge (meshes, materials, textures, lights, cameras) | `Three/Bridge/*` | done |
| Three.js lighting, tone mapping and fog in HLSL | `Resources/Shaders/Cozy*.shader`, `*.hlsl` | done (ready for Unity shader import) |
| Browser DOM / HTML / SVG / CSS compatibility layer on UI Toolkit | `Assets/Scripts/Dom/*` | done |
| UI stylesheet (`src/style.css` -> UI Toolkit USS + `@keyframes` driver) | `Resources/UI/Style.uss`, `Dom/CssAnimations.cs` | done |
| Scene bootstrap (`MonoBehaviour` entry point & scene reload) | `Core/GameHost.cs`, `Scenes/Main.unity` | done |
| Project setup and build scripts (`BuildScript.BuildWindows`) | `Assets/Editor/ProjectSetup.cs`, `Assets/Editor/BuildScript.cs` | done |

## Game code (`src/` -> `Assets/Scripts/`)

| TypeScript | C# | State |
|---|---|---|
| `core/items.ts` | `Core/Items.cs` | ported |
| `core/StateManager.ts` | `Core/StateManager.cs` | ported |
| `core/Journal.ts` | `Core/Journal.cs` | ported |
| `core/Story.ts` | `Core/Story.cs` | ported |
| `core/Mail.ts` | `Core/Mail.cs` | ported |
| `core/Help.ts` | `Core/Help.cs` | ported |
| `core/Achievements.ts` | `Core/Achievements.cs` | ported |
| `core/Storage.ts` | `Core/Storage.cs` | ported; imports the desktop app's saves on first run |
| `core/Settings.ts` | `Core/Settings.cs` | ported (`PlayerPrefs`) |
| `core/SaveGame.ts` | `Core/SaveGame.cs` | ported, same JSON format |
| `core/Particles.ts` | `Core/Particles.cs` (+ `CozyParticles` shaders) | ported |
| `core/AudioManager.ts` | `Core/AudioManager.cs` | ported (procedural WebAudio graph on `OnAudioFilterRead` + `Resources/Music` MP3s) |
| `core/InputManager.ts` | `Core/InputManager.cs` | ported (Input System polling -> DOM key/mouse events) |
| `core/GamepadInput.ts` | `Core/GamepadInput.cs` | ported |
| `core/Game.ts` | `Core/Game.cs` | ported |
| `mechanics/LootGenerator.ts` | `Mechanics/LootGenerator.cs` | ported |
| `mechanics/DetectorTool.ts` | `Mechanics/DetectorTool.cs` | ported |
| `mechanics/DigTool.ts` | `Mechanics/DigTool.cs` | ported |
| `mechanics/CleaningStation.ts` | `Mechanics/CleaningStation.cs` | ported |
| `mechanics/Hand.ts` | `Mechanics/Hand.cs` | ported |
| `mechanics/ViewModel.ts` | `Mechanics/ViewModel.cs` | ported |
| `mechanics/Bullets.ts` | `Mechanics/Bullets.cs` | ported |
| `mechanics/Arrows.ts` | `Mechanics/Arrows.cs` | ported |
| `ui/PadText.ts` | `UI/PadText.cs` | ported |
| `ui/ItemPhotos.ts` | `UI/ItemPhotos.cs` | ported |
| `ui/MapView.ts` | `UI/MapView.cs` | ported |
| `ui/CampModal.ts` | `UI/CampModal.cs` | ported |
| `ui/UIManager.ts` | `UI/UIManager.cs` | ported |
| `world/conform.ts`, `merge.ts` | `World/Conform.cs` | ported |
| `world/Beach.ts` | `World/Beach.cs` (+ `CozyBeach.shader`) | ported; chunks hold their own slice of the grid |
| `world/Sky.ts` | `World/Sky.cs` (+ `CozySkyDome.shader`) | ported |
| `world/Ocean.ts` | `World/Ocean.cs` (+ `CozyOcean`, `CozyOceanFoam`) | ported |
| `world/Stones.ts`, `Flags.ts`, `Rain.ts` | `World/*.cs` | ported |
| `world/Whitecaps.ts`, `Tracks.ts` | `World/*.cs` (+ `CozyWhitecaps`, `CozyTracks`) | ported |
| `world/Grass.ts` | `World/Grass.cs` (+ `CozyGrass.shader`) | ported |
| `world/SeaTreasure.ts`, `Scenery.ts`, `Ecology.ts`, `LootSpawner.ts` | `World/*.cs` | ported |
| `world/Birds.ts`, `GolfCart.ts`, `Boat.ts`, `Dog.ts`, `Sandcastles.ts` | `World/*.cs` | ported |
| `world/Camp.ts`, `SeaCave.ts`, `Island.ts`, `SeaFlora.ts` | `World/*.cs` | ported |
| `world/SeaCreatures.ts`, `Underwater.ts`, `Underground.ts`, `SeaLife.ts` | `World/*.cs` | ported |

## Known differences to look at in the editor

- Three.js lit materials with `scene.environment` (`RoomEnvironment`) use an analytic stand-in for the environment light
  (`SceneSync.EnvironmentTuning`). Compare against the browser build and tune, or replace with a captured cubemap.
- Transparent surfaces blend in linear space in Unity and in sRGB space in WebGL, so overlapping glass and foam can look a
  touch different.
- UI Toolkit USS does not support CSS `linear-gradient`/`radial-gradient` or `box-shadow` directly; `Resources/UI/Style.uss`
  approximates them with matching solid tones and bevel borders, while `Dom/CssAnimations.cs` drives the `@keyframes` animations at 60 fps.

## Checked in a real build (2026-11-09)

`BuildScript.BuildWindows` builds with no errors (115 MB), and the `-autoshot` smoke test (see `MIGRATION.md`) loads in about
5 s, shows the title screen, starts a game and looks at the camp and the sea, at 150-170 FPS at 2560x1440 on the dev PC, with
no errors in the player log. Fixed on the way:

- **Nothing was drawn:** `ProjectSetup` made the URP asset without a renderer ("Default Renderer is missing"). It now creates
  `Assets/Settings/CozyURP_Renderer.asset` and assigns it, and repairs an existing asset that lacks one.
- **The world was hidden behind the page:** `html`/`body` had the browser's dark background, which UI Toolkit draws over the
  camera. It is clear now.
- **The camp panel was always open:** the browser's `.hidden { display: none !important; }` lost to `.camp { display: flex }`,
  because USS has no `!important`. `HTMLElement.ApplyDisplay` now enforces the class, as `!important` would.
- **Red and green smears on many models:** the shaders multiplied in a baked InstancedMesh's colour from `TEXCOORD1` on every
  mesh; meshes without that channel got another channel's data (their UVs) instead. Only baked InstancedMeshes read it now
  (`MaterialCache.Kind.Instanced`, bit 2 of `_Flags.x`).
- **UI theme:** the panel had no theme, so Unity warned that the UI would not render properly. `Resources/UI/CozyRuntimeTheme.tss`
  imports Unity's default runtime theme, and the root keeps the game's own font.
- **Audio:** the two radio AudioSources sat on the synth's GameObject, where `OnAudioFilterRead` may only serve one source.
  They have their own child objects now.
- Errors while preparing the world behind the loading screen are now logged instead of silently swallowed.
- **Emoji drew as black outlines or empty boxes** (Unity's text engine cannot draw colour emoji fonts). The UI now uses a
  bundled atlas of Microsoft's Fluent Emoji (MIT); see "Colour emoji" in `MIGRATION.md`. Checked on the title menu, the
  achievements list and the HUD.
- **Text ran out of its boxes on one long line** (journal, letters, Lost & Found notes, Help, upgrade descriptions). Each
  element's text is a Unity Label, and the default theme's own white-space rule beat the `white-space: normal` it should
  inherit. `Style.uss` now sets labels to wrap, and the page's few nowrap rules (buttons, chips, prompts, hints, key rows,
  save names) repeat themselves for labels at the end of the file. Checked against the browser build: the title wraps to two
  lines in both.
- **Load Game, New Game and Main Menu froze the game.** They reload the scene, but `Main.unity` holds no objects and the
  `GameHost` was only made once per launch, so the reloaded scene was empty. `GameHost` is now made after every scene load,
  and `GameHost.Reload` stops the old game and clears the bridge's caches first (`WebGLRenderer.Teardown`), as a page
  reload did in the browser. Saves now record real time (`savedAt` used time since launch, so the list showed 1970), and a
  save that cannot be read says so instead of quietly showing the title screen.
- **Walking halved the frame rate.** The minimap is drawn by the C# software canvas and redraws whenever you move; it cost
  7.9 ms a frame. The canvas's hot loops are faster now (one blend mode lookup per fill, solid colours and plain images
  drawn in tight loops, an active-edge scanline rasterizer), and the waveform's fixed background is drawn once. On the dev
  PC: standing 180 fps, walking 69 -> 122 fps on Mono.
- **IL2CPP:** `BuildWindows` now compiles the C# to native code. On the dev PC: standing 180 -> 251 fps, walking 122 -> 202
  fps, start-up 4.2 -> 2.8 s; the game's own work while walking fell from 7.3 to 4.2 ms a frame. The shippable game is about
  140 MB. A second pass cut per-frame waste: the scene sync no longer allocates arrays or calls into the engine for
  objects that did not change, objects that did not move keep their matrices (Three.js recomposes them all every frame),
  and the waveform's background and label are drawn once and copied in. In a day-5 save, walking: 150 -> 178 fps (main
  thread 6.3 -> 5.5 ms; the GPU needs only 0.2 ms, so the main thread is the whole limit). What is left: walking ~3,600
  objects every frame (about 2 ms), the CPU-drawn minimap and waveform (about 1.9 ms) and Unity's own work (about 1.1 ms).
  The mini-map and the detector screen then moved to the GPU. `UI/MiniMapGpu.cs` draws the sea, the island (one texture
  turned with style transforms), the dig holes (a Painter2D layer redrawn only when you dig) and the rim; the canvas keeps
  only the small markers. The detector line is a Painter2D layer over a canvas that is redrawn only when the label changes.
  Day-5 save, walking: 178 -> 251 fps (main thread 5.5 -> 3.7 ms); new game, walking: 237 -> 344 fps.
  Then: raycasts (hover and aim, every frame) refreshed each tested mesh's whole parent chain through `updateWorldMatrix`,
  which recomposed every matrix, so 3,500 of 3,600 objects were recomposed per frame though only ~300 move; it now skips
  unchanged ones (~570 recomposed). Each geometry and material also remembers its bridge cache entry
  (`bridgeEntry`), so the sync no longer looks them up in dictionaries per object per frame. Day-5 save, walking: 251 ->
  276 fps (main thread 3.15 ms). A new game now runs at the 360 Hz VSync cap (main thread 1.8 ms).
  Left, if a handheld needs it: the sync still visits every object (~1 ms) although ~3,300 never change; skipping those
  needs a way to mark objects static. Whitecaps and footprints still re-bake their instanced meshes (~0.3 ms).
  The build folder also holds `*_BackUpThisFolder_ButDontShipItWithYourGame` and `*_BurstDebugInformation_DoNotShip`
  (debug symbols, about 1.2 GB), which stay out of the Steam depot.
- **Pills became ovals** (HUD chips, prompts, hints): CSS shrinks `border-radius: 999px` to half the shorter side, UI
  Toolkit clamped it per axis. `HTMLElement.FitPillRadius` now sets such radii to half the shorter side.
- **A controller could not move through menus:** UI Toolkit's focus-in event bubbles, and containers with the DOM layer's
  focus handler claimed the focus from the button inside them, so the game's own navigation snapped back to the first
  button. Only the element that itself gains or loses focus now claims it, and the panel's built-in controller navigation
  is switched off (the game navigates itself, as in the browser). `-autoshot-pad` tests it with a virtual controller.
- **Binoculars stuttered:** the full-screen overlay canvas was redrawn on the CPU whenever the zoom or distance changed.
  The lenses, reticle and legend are now redrawn only when the screen or controls change; the readout has its own canvas.
- **Uppercase text was lowercase** (main menu kicker, card categories, settings and journal headings): USS has no
  text-transform. `HTMLElement.DisplayText` uppercases text inside those elements (parsed selectors are now cached).
- **Shadows were hard and pixelated:** the URP asset had soft shadows off and a 2048 map shared by 2 cascades. ProjectSetup
  now turns on soft shadows (medium, like PCFSoftShadowMap) and uses 4096, so each cascade gets the browser's 2048.
- **Scrollbars** were Unity's grey default with arrow buttons and a sideways bar. They are now a slim rounded thumb in the
  page's ink colour (orange while dragging) with no arrows, and the page's scroll boxes never scroll sideways.

Not checked yet: playing by hand (mouse look, digging, cleaning, camp pages, saves), the boat, diving, the underground and the
ending, controller input, sound, and how close the colours and light are to the browser build side by side.
