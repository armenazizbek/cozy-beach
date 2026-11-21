# Steam Deck Verified + steady 60 fps: implementation plan

## Context

Cozy Treasure Hunter ships on Steam as the Unity 6000.3.28f1 (URP, IL2CPP, Windows x64) build in `unity/`. The goal is
**Steam Deck Verified** with a **steady 60 fps on the Deck at 1280x800 in the default Deck settings**. The owner has a Deck
to measure on. Steamworks.NET is in scope (the Unity build has no Steamworks at all today).

What the investigation found:
- **Controls are already Deck-ready in substance**: every menu and activity works on a pad with Xbox glyphs (Valve accepts
  those). What fails Verified is around it: no on-screen keyboard for the save name, small text at 1280x800, Windows-only
  fonts that Proton lacks, keyboard glyphs at boot, some keyboard wording in Help/shop/journal, cursor visible on pad.
- **Performance has never been measured on Deck-class hardware.** On the dev PC (RTX 5090, 2560x1440, a *Mono* test
  build; IL2CPP is faster) the camp runs at 330+ fps: main thread 2.83 ms, render thread 0.58 ms, GPU 0.23 ms. Of the
  main thread, 1.23 ms is the scene sync (copying ~1,500 THREE objects into Unity every frame) and ~1.2 ms is Unity's own
  culling/URP/UI work. A Deck CPU core is roughly 2.5-3x slower and its GPU ~60x weaker, so the defaults (High: 4x MSAA,
  4096 shadow map, 2 cascades over 60 m, soft shadows; Direct3D 12 picked automatically, which Proton runs through
  vkd3d-proton) are the main risk, then the CPU-side sync and ~1,000+ separate renderers (palms alone ~950).
- The project already has the measuring tool needed: `-autoshot` + `Measure()` in `unity/Assets/Scripts/Core/AutoShot.cs`
  prints fps, main/render/GPU ms, per-system ms (`BridgeStats.Time` blocks), sync counts, frame pacing and garbage.

Order of work: **Phase 0 (measure) -> 1 (Deck defaults) -> 2 (Verified compliance, independent of perf) -> 3/4 (CPU and
GPU work, each change measured) -> 5 (Deck sign-off).**

### Priority at a glance

| # | Task | Why | Expected effect on the Deck | Size |
|---|---|---|---|---|
| 0.1-0.5 | Perf tour, context line, Deck routine, baseline | Everything else is decided by these numbers | - | S |
| 1.3, 4.1, 4.2 | `handheld` preset: shadows 35 m / 2048 / soft Low, MSAA 2x, ocean not receiving shadows, intermediate texture Auto | Defaults are the heaviest settings | 3-5 ms GPU | S |
| 1.6 | Pin the graphics API (likely D3D11 via DXVK) after an A/B | D3D12 runs through vkd3d-proton today | measure | S |
| 2.1-2.2 | Steamworks.NET + on-screen keyboard for save names | Verified blocker; Steam achievements don't work at all yet | - | M |
| 2.3 | UI size setting (130% on the Deck) + 14px font floor | Verified blocker ("small text") | - | M |
| 2.4 | Bundled fonts | Proton has no Trebuchet/Georgia/Segoe | - | S |
| 2.5-2.6 | Pad mode at boot, no cursor, pad wording, pause on focus loss, autosave on quit, audio after resume | Verified polish | - | S-M |
| 3.1-3.2 | Matrix pass skips hidden/static subtrees; fewer synced objects | Biggest main-thread cost | 1-2 ms CPU | M |
| 4.3 | Merge palms (~950 renderers), gulls, sharks, tufts; sprites back in the SRP Batcher | Render thread and shadow draws on 4 cores | large, measure | M-L |
| 3.4 | Ocean depth from a baked texture | 2-3 ms spikes while moving | spikes gone | M |
| 3.7 | Autosave work off the main thread | Hitch every 90 s | tens of ms hitch gone | M |
| 3.3, 3.5, 3.6 | Partial material updates; GPU particles/rain; no per-frame DOM queries | Steady per-frame waste | 0.5-1.5 ms CPU | M |
| 3.10 | Real shader/pipeline warm-up | First-use stutter, long load | stutter gone | M |

---

## Ground rules for the implementer

- Work on `main` (check the branch and `git pull` first). Commit per task with a clear message; push only when asked.
- Change C# in `unity/Assets/Scripts` only. The old TypeScript web game (`src/`) has been removed; it is in the git history.
- Build without the editor open and without the game running (a running game locks the exe):
  - quick test build: `Start-Process "C:\Users\Armen\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe" -ArgumentList "-batchmode","-quit","-projectPath","<repo>\unity","-executeMethod","BuildScript.BuildWindowsMono","-logFile","<log>" -Wait`
    (use `Start-Process -Wait`; a bare `& Unity.exe` returns at once). Check the log for `error CS` and `Build Finished, Result: Success`.
  - release build (what gets measured on the Deck): `BuildScript.BuildWindows` (IL2CPP).
  - **A Mono build rewrites `unity/ProjectSettings/ProjectSettings.asset` (scriptingBackend -> Mono) and regenerates
    `unity/Assets/Resources/UI/Emoji.asset`. Restore both with `git checkout --` before every commit.** Never commit the
    backend flip.
- Run the smoke test: `unity/Builds/Windows/CozyTreasureHunter.exe -autoshot <outDir> -screen-width 1280 -screen-height 800 -screen-fullscreen 0 [-autoshot-* flags]`;
  screenshots land in `<outDir>`, results in `%USERPROFILE%\AppData\LocalLow\Cozy Beach\Cozy Treasure Hunter\Player.log`
  (`[AutoShot] <name>: ... fps`, `... slowest parts`, `... pacing`). Read the PNGs to check visuals.
- **Every performance change gets a before/after `Measure` line** (same scene, same flags) recorded in
  `unity/PORT_STATUS.md` under a new "Steam Deck" section, like the existing performance history there (lines 105-126).

---

## Phase 0 - Measuring tools (do first)

**0.1 Let the smoke test choose the window size.** `Game.cs:465` calls `applyDisplay(true)`, which applies the saved
display setting and overrides `-screen-width/-height`, so 1280x800 shots are impossible today. When `AutoShot.Active` and
`-screen-width` is on the command line, skip the saved display mode/size in `applyDisplay` (`Game.cs:3758-3784`). Add
`1280x800` to the window-size list (`UI/UIManager.cs:728-735`, currently 16:9 only).
*Done when:* `-autoshot <dir> -screen-width 1280 -screen-height 800 -screen-fullscreen 0` gives 1280x800 PNGs.

**0.2 One `-autoshot-perf` tour covering the heavy places.** Add a flag in `Core/AutoShot.cs` that runs `Measure("perf: ...")`
(`AutoShot.cs:1234`) in each place, reusing the existing `Game.smokeTest*` helpers, then quits:
camp by day (already measured at `AutoShot.cs:157`), camp at night with the fire and lanterns, walking and running along
the beach (copy the `-autoshot-walk` block, `AutoShot.cs:160-175`), rain at the camp and inside the old house
(`smokeTestDogRain("rain"/"wet"/"leak")`), the boat at full speed on open sea (`smokeTestBoatAt` + held `KeyW`), both far
islands (`smokeTestIsland`/`smokeTestLandmark`), under the sea at the reef and the kelp (`smokeTestGo("dive")`,
`smokeTestDiveAt`), the underground room (`smokeTestGo("underground")`), the lighthouse at night with beams and sailing
ships (`smokeTestTower("clean")` starts its short intro, `Lighthouse.INTRO_LENGTH` = 8.4 s; measure after it, at night),
and the opening storm (`smokeTestOpening(true)`, as `-autoshot-opening` does at `AutoShot.cs:124-148`). For a played world use the
existing `-autoshot-load <id>` path with a day-5+ save copied into `<outDir>\saves`.

**0.3 Make every run self-describing.** At the start of the smoke test log one `[AutoShot] context:` line:
`SystemInfo.graphicsDeviceType/graphicsDeviceName/graphicsDeviceVersion`, `SystemInfo.processorType`, screen size,
refresh rate and `Screen.fullScreenMode`, URP `renderScale`, `msaaSampleCount`, main-light shadow resolution, cascade
count, shadow distance, the quality preset, `Screen.dpi`, `window.osPixelRatio`, and `Platform.IsSteamDeck` (task 1.1).

**0.4 Document the Deck measuring routine** in `unity/MIGRATION.md` (next to the existing `-autoshot` docs):
1. Build with `BuildScript.BuildWindows` (IL2CPP). Copy `unity/Builds/Windows` to the Deck **without**
   `*_BackUpThisFolder_ButDontShipItWithYourGame` and `*_BurstDebugInformation_DoNotShip` (e.g. to `/home/deck/Games/Cozy`).
2. Desktop Mode -> Steam -> *Add a Non-Steam Game* -> `CozyTreasureHunter.exe`. Properties -> Compatibility -> force the
   current stable Proton (also try Proton Experimental once).
3. Launch options: `-autoshot Z:\home\deck\perf -autoshot-perf -logFile Z:\home\deck\perf\Player.log`
   (add `-autoshot-novsync` for a headroom run). `Z:` is the Linux root inside Proton, so results land in `/home/deck/perf`.
4. Run it from **Game Mode** (gamescope, as players do), plugged in, default TDP/refresh, Steam frame limiter off. Repeat
   one run on battery. Use the Steam performance overlay (level 4) during a manual play-through.
5. Graphics-API A/B (task 1.6): the same run with `-force-d3d11`, then `-force-d3d12` (and `-force-vulkan` if Vulkan is
   added to the API list).
Once the game has an App ID, the same can run from a private Steam beta branch instead of the non-Steam shortcut.

**0.5 Take the baseline on the Deck** before any optimisation, with the current defaults and with Low quality, and paste
the `Measure` lines into the new PORT_STATUS "Steam Deck" section. This decides how far Phases 3-4 must go.

## Phase 1 - Deck detection and Deck defaults

**1.1 `Core/Platform.cs` (new): `Platform.IsSteamDeck`.** True when `SteamUtils.IsSteamRunningOnSteamDeck()` says so
(after task 2.1), else when the environment variable `SteamDeck` is `1` (Steam sets it for games on a Deck, also under
Proton), else when `SystemInfo.graphicsDeviceName` matches the Deck APU (confirm the exact string from the 0.3 context
line on the Deck, LCD and OLED models differ). Log the result once at start.

**1.2 Deck first-run defaults.** `Core/Settings.cs:48-65` returns `new GameSettings()` when nothing is stored. On a Deck
return Deck defaults instead: quality `handheld` (1.3), `displayMode = "borderless"`, `vsync = true`, `fpsLimit = 0`,
`sharp = true`, `renderScale = 1`, `uiScale` per task 2.3. Settings the player already saved are never overwritten.

**1.3 A `handheld` quality preset** in `Three/Bridge/UrpSettings.cs:47-61` and the quality dropdown (settings UI built in
`UI/UIManager.cs`; the setting is `GameSettings.quality`, `Settings.cs:29`). Today every preset keeps the URP asset's
60 m shadow distance (`Assets/Settings/CozyURP.asset:57`; `UrpSettings.SetShadowDistance` is never called) and soft shadow
quality Medium (`:69`). Starting values for `handheld`: MSAA 2x, shadow atlas 2048, shadow distance 35 m (call
`SetShadowDistance` per preset), soft shadow quality Low, the ocean not receiving shadows (`World/Ocean.cs:233`) and
small props not casting (task 4.1). Try 1 vs 2 cascades on the Deck. The final values come from Deck measurements (0.5).

**1.4 Borderless on the Deck.** `applyDisplay` (`Game.cs:3758-3784`) uses exclusive fullscreen by default
(`Game.cs:3781`); under gamescope borderless at native 1280x800 is the safe choice (Deck default from 1.2).

**1.5 Frame pacing.** Add 40 and 45 to `FramePacing.LIMITS` (`Core/FramePacing.cs:17`) and the settings dropdown. Keep
VSync on by default on the Deck (the Deck's own Quick Access frame limiter works on top). With VSync at 60 Hz,
`maxQueuedFrames = 3` (`FramePacing.cs:44`) adds a frame of input lag; use 2 when the refresh rate is <= 90 Hz.

**1.6 Pin the graphics API.** `ProjectSettings.asset:309` has `m_BuildTargetGraphicsAPIs: []` (automatic), and the
player picked **Direct3D 12** on the dev PC; on the Deck that runs through vkd3d-proton. Run the 0.4 A/B and then set the
Windows API list explicitly in `unity/Assets/Editor/ProjectSetup.cs` (it already sets player options, e.g.
`enableFrameTimingStats` at line 55): `PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false)`
and `PlayerSettings.SetGraphicsAPIs(...)`. Expected winner is `Direct3D11` first (DXVK), with `Direct3D12` as fallback. Add `Vulkan` only if it measures clearly
better, since it adds a shader set to the build.

**1.7 Render scale must not follow screen DPI on the Deck.** `Game.cs:1473-1477` (`pixelRatio`) divides by
`window.osPixelRatio`, which comes from `Screen.dpi / 96` (`Dom/DomHost.cs:302-304`). Proton normally reports 96 DPI, but
if the Deck's real ~215 DPI ever came through, the 3D picture would silently drop to 68% (and to 45% with Sharp off). On
the Deck treat `osPixelRatio` as 1, and log the effective URP render scale (0.3).

**60 fps budget on the Deck (vsync on, 16.7 ms):** main thread <= 10 ms, GPU <= 12 ms, 99th-percentile frame <= 20 ms and
0 KB garbage/frame in every `-autoshot-perf` scene.

## Phase 2 - Steam Deck Verified compliance

**2.1 Steamworks.NET.** Add the Steamworks.NET UPM package to `unity/Packages/manifest.json`
(`"com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#<latest release tag>"`;
it brings `steam_api64.dll`; check the IL2CPP build copies it next to the exe). New `Core/SteamService.cs`, started from `Core/GameHost.cs`:
`SteamAPI.Init()` (game keeps working without Steam if it fails, as today), `SteamAPI.RunCallbacks()` every frame,
`SteamAPI.Shutdown()` on quit; `SteamAPI.RestartAppIfNecessary(appId)` only once the real App ID exists. Develop with a
`steam_appid.txt` next to the exe (480, Steam's test app, until the real ID exists); make sure `BuildScript.cs` never
ships it. Wire the existing stub `SteamBridge.unlockHandler` (`Core/Storage.cs:144-150`, called from
`Core/Achievements.cs:138,152`) to `SteamUserStats.SetAchievement(id)` + `StoreStats()`, and at start push every
achievement already earned locally (so progress made outside Steam appears). Pause the game when the Steam overlay opens
(`Callback<GameOverlayActivated_t>` -> the same path as losing focus, 2.6).

**2.2 On-screen keyboard for the save name** (the only text field: `UI/UIManager.cs:1169-1183`).
- Let a controller reach the field (`Game.cs:3236-3237` skips it on purpose today). Pressing A on it calls
  `SteamUtils.ShowGamepadTextInput(Normal, SingleLine, "Save name", 32, current)`; on
  `GamepadTextInputDismissed_t` read `SteamUtils.GetEnteredGamepadTextInput` into the field. It returns false outside
  Big Picture/Deck (or without Steam): then keep today's behaviour.
- A touch/mouse focus of the field on a Deck calls `SteamUtils.ShowFloatingGamepadTextInput` with the field's screen rect.
- Only rename a save when the name was actually edited: the box is pre-filled with "Save N" (`UIManager.cs:1182`), so
  Overwrite renames saves today (`UIManager.cs:1215-1216`).

**2.3 Legible text at 1280x800.** The UI is a 1920x1080 page scaled by `min(w/1920, h/1080)` (`Dom/DomHost.cs:298-319`),
so on the Deck every page pixel is 0.667 screen pixels: body text 18px -> 12px, and many rules go below Valve's ~9px
minimum (`Resources/UI/Style.uss`: 11px at lines ~618, 1667, 2618, 2663; 12px at ~280, 745, 839, 848; 13px at ~111, 2603,
2629; 14px at ~238, 527, 1093, 1727, 2103, 2148, 2700; canvas text: `Mechanics/DetectorTool.cs:265` 10px,
`UI/MapView.cs:362,366,723,731`, binocular legend `UI/UIManager.cs:1473-1500`).
- Add a **UI size** setting (100% / 115% / 130%) stored in `GameSettings` and applied in `DomHost.RefreshMetricsInternal`
  by dividing `PAGE_WIDTH/PAGE_HEIGHT` by it (the page then gets smaller and everything on it larger; the layout already
  should cope with smaller pages because it came from a browser, but check every screen). Default 130% on the Deck (page
  1477x923 at 1280x800, so body text ~15.6px and 14px rules ~12px on screen), 100% elsewhere; if some screen cannot fit
  at 130%, use 115% (page 1662x1038) and raise more font floors instead. `PAGE_WIDTH/HEIGHT` are only used in
  `DomHost.cs:298,312`. Check the panel rescales live (`DomHost.cs:286-294`) and the 3D render scale stays 1
  (`WebGLRenderer.cs:98-105` divides by `devicePixelRatio`, so it should cancel out; confirm in the 0.3 context line).
- Raise every font-size below 14 page-px to 14, and the canvas text above to the same floor (scaled by `devicePixelRatio`
  as those canvases already are).
- *Done when:* `-autoshot-ui` (`AutoShot.cs:~995-1012`) at 1280x800 with UI size 130% shows every menu, the HUD, the camp
  shop, the map and the settings without overflow or clipping, and no text under ~9px on screen.

**2.4 Bundle the fonts.** Menu text uses Windows fonts (`Dom/DomHost.cs:114-133`: Trebuchet MS / Segoe UI / Arial,
Georgia Bold, Segoe UI Symbol), which Proton does not ship. The loader already prefers `Resources/UI/Fonts/Body`,
`BodyBold` and `Display` (`DomHost.cs:183-197`). Add OFL fonts there (e.g. a humanist sans close to Trebuchet for
Body/BodyBold, and **Gelasio**, metric-compatible with Georgia, for Display), plus a symbol fallback (e.g. Noto Sans
Symbols 2 or DejaVu Sans, covering ✓ ▾ ◆): extend the fallback list at `DomHost.cs:129-133` to load
`Resources/UI/Fonts/Symbols` before the OS fonts. Canvas text already has Linux fallbacks
(`Three/Textures/CanvasFonts.cs:72-80`); point it at the bundled fonts too. Check the title, settings and journal
screenshots for missing glyphs.

**2.5 Controller polish.**
- Start in pad mode when a gamepad is connected at launch or on a Deck (`usingPad`, `Game.cs:178`, set on first pad input
  at `Game.cs:391`), so the title screen shows pad glyphs, not Enter/Esc.
- Hide the OS cursor while `usingPad` (`Game.cs:3139` -> `Dom/DomGlobals.cs:174-178`).
- Help shows only the active control scheme and runs its text through `padText` (`UI/UIManager.cs:794-822`,
  `Core/Help.cs:49-93`); reword the strings that slip through: `UI/CampModal.cs:757` ("Press 8 or scroll"), `:388`
  ("Click the sand"), `Core/Journal.cs:431` ("Press T on the map") via `UI/PadText.cs`.
- In the album's big-photo view, B closes the photo, not the whole camp (`Game.cs:3331-3341` picks the first "Close";
  `CampModal.cs:78` comes before `:434`).

**2.6 Suspend/resume, focus and quit.**
- Pause on focus loss for every input type (today only for mouse players, `Game.cs:520`, `Game.cs:6559`); the Deck's
  sleep and the Steam overlay both arrive as focus loss.
- Autosave on quit (`Application.quitting`; `Core/GameHost.cs:17-21` only logs now) and on focus loss (throttled), since
  the regular autosave is every 90 s (`Game.cs:51`).
- Audio after resume or docking: the synth reads the sample rate and starts its AudioSource once
  (`Core/AudioManager.cs:705, 729-734`). Handle `AudioSettings.OnAudioConfigurationChanged`: re-read the rate, rebuild
  the buffers/filters, `Play()` again. Check after sleep/resume and after plugging in headphones.

**2.7 Photos into the Steam library.** "Save to Pictures" writes to `MyPictures` (`Core/Photos.cs:138`) and shows a
Windows path (`UI/CampModal.cs:409-413`); under Proton that folder is buried in the prefix. When Steam is running, use
`SteamScreenshots.AddScreenshotToLibrary` (or `WriteScreenshot`) so photos show up in the Deck's screenshot library, and
word the message accordingly.

**2.8 Steamworks backend (no code, owner):** Steam Input default configuration "Gamepad" (so the trackpads are not a
mouse by default; the game shows Xbox glyphs); Auto-Cloud roots as in `STEAM.md:16-21` (Proton maps the Windows roots);
37 achievements (the code has `STARGAZER`, `Core/Achievements.cs:69`); then request the Steam Deck compatibility review.

**2.9 Docs.** `STEAM.md` (lines 7, 28 still describe the Electron build's steamworks.js; achievements 36 -> 37, also
`steam/STORE_PAGE.md:56`, `steam/UPLOAD_CHECKLIST.md:24`), and a Deck section in `steam/UPLOAD_CHECKLIST.md` with the
Phase 5 checks.

## Phase 3 - CPU / main-thread work

A Deck Zen 2 core is ~2.5-3x slower than the dev PC, so expect the camp at ~7-9 ms main thread before this phase. Tasks
are ordered by expected gain and independent of each other: do 3.0 first, then only what the Deck numbers call for. All
paths are under `unity/Assets/Scripts/`.

**3.0 Trustworthy numbers first.**
- `Measure` reads `GC Allocated In Frame` (`Core/AutoShot.cs:1243-1254`), which is only recorded in development players,
  and never checks `gcRec.Valid`, so "garbage 0.0 KB/frame" may be false. Use the recorder when valid, else report
  `GC.GetTotalMemory(false)` deltas plus the collection count.
- `MeshCache.GetPoints` (`Three/Bridge/MeshCache.cs:371-447`) never reports to `BridgeStats`, so particle/rain rebuilds
  are invisible; add them to the mesh-rebuild stats.
- Add `BuildScript.BuildWindowsDevelopment` (Development + profiler connection) to profile the Deck with the Unity
  Profiler over the network; ~1.2 ms of main thread outside game+sync (UI Toolkit, URP culling for camera + 2 cascades,
  `InputManager.poll` scanning ~110 keys at `Core/InputManager.cs:78-88`, `DomHost.Tick` at `Dom/DomHost.cs:332-370`) can
  only be split there.

**3.1 Matrix pass: skip what cannot have changed** (`Three/Core/Object3D.cs:309-326`). It traverses invisible subtrees
(underwater, cellar, opening) every frame, and a moving parent forces recomputes into `syncStatic` groups (the static
parts of 26 gulls, `World/Birds.cs:82-127`). Skip invisible subtrees (mark them dirty so they recompute when shown), stop
propagating `force` into static groups, and compute their world matrices lazily. Raycasts already recompute on demand
(`Three/Objects/Objects.cs:127`); check the few direct `matrixWorld` readers still work (`Mechanics/CleaningStation.cs:184`,
`World/Conform.cs:36,81`, `Three/Math/Shapes3.cs:51`, `Three/Math/Vectors.cs:153`). Move the island palm sway done on the CPU
(`World/Island.cs:1142-1146`) into the palm-sway shader the beach palms already use. Expected 0.5-1 ms on the Deck.

**3.2 Fewer objects through the scene sync** (`Three/Bridge/SceneSync.cs:187-240`; 1,517 objects/frame: sea life 249,
gulls 183, camp 147, Lagoon Cay 142 at ~350 m, sky 117, beach chunks 101). Hide far islands and sea life by distance as
the gulls already do (`World/Birds.cs:328`); mark more never-changing groups `syncStatic` (the 16 clouds x 4-7 sprites,
`World/Sky.cs:203-218`; the stones group, `World/Stones.cs:89`; grass tufts, 6-9 objects each in played saves,
`World/Ecology.cs:130-155`; the shark details); set the invisible-material hit spheres to `visible = false` as the fish
ones already are (`World/SeaLife.cs:436`; still fully synced at `World/Birds.cs:136`, `World/Ecology.cs:168`,
`World/SeaLife.cs:363`, `World/Camp.cs:136`); compile the stats-only type checks and the stats-only 10-double compare
(`SceneSync.cs:165,192`) out of release builds; replace the per-frame sum of all attribute versions
(`Three/Core/BufferGeometry.cs:433-440`) with one dirty counter per geometry. This is also the PORT_STATUS leftover
("the sync still visits every object (~1 ms) although ~3,300 never change", `unity/PORT_STATUS.md:125-126`). Later step:
fold the matrix pass (3.1) into the sync's single walk, since the bridge only reads local transforms
(`SceneSync.cs:148-185`). Expected 0.3-1 ms, more in played saves.

**3.3 Stop re-applying whole materials for a colour change** (`Three/Bridge/MaterialCache.cs:117-121,187-284`: any
hashed field change runs ~25 `Material.Set*` calls). Split the hash into render state vs colour/opacity/emissive and
push only what changed; add change thresholds to the per-frame writers: island haze (`World/Island.cs:1120-1131`, fog
colour drifts every frame at `World/Sky.cs:283`; `World/Scenery.cs:556` already has a threshold to copy), clouds
(`Sky.cs:326,349`), camp bulbs/radio LED/journal cover (`World/Camp.cs:1383-1385`, and `camp.update` runs anywhere on the
surface, `Core/Game.cs:6623`), `World/Ocean.cs:314-315`, `World/Island.cs:1152`, `Mechanics/DetectorTool.cs:140-143`,
`World/Sandcastles.cs:463`, `World/Lighthouse.cs:1052-1055`. Expected 0.1-0.4 ms.

**3.4 Ocean depth without the CPU** (`World/Ocean.cs:300-305`, grid at `:109`): every 0.5 m of camera travel all 6,561
vertices re-query `beach.heightAt` (off the grid that is `baseHeight`, ~8-15 trig/exp/pow each, `World/Beach.cs:46-58,
97-163,644`) and re-upload; ~1 ms per refresh on the PC, i.e. 2-3 ms spikes several times a second by boat or cart on the
Deck. Bake a sea-floor depth texture once at load and sample it in the ocean's vertex shader.

**3.5 Particles and rain on the GPU** (`Core/Particles.cs:97-100`: three attributes bumped every frame while any particle
lives, the whole pool rebuilt at 4 vertices each, `MeshCache.cs:121-127,379-446`). Always alive on the surface: camp
embers (`World/Camp.cs:1394-1402`), shark wakes (`World/SeaLife.cs:726-740`), chest sparks
(`World/SeaTreasure.cs:110-126`); plus boat wake, underwater bubbles (`World/Underwater.cs:78-79`) and rain (1,600 drops,
3,200 vertices/frame, `World/Rain.cs:13,72-94`). Animate particles in the shader from emit time and velocity (as the
tracks shader already does), gate pools by distance, and make rain a shader-animated static mesh around the camera.
Expected 0.2-0.6 ms plus upload bandwidth.

**3.6 No document queries every frame.** `menuTick()` runs every frame, even paused (`Core/Game.cs:6565`); `overlayRoot()`
runs a whole-document `querySelectorAll` with four `:not()` selectors (`Game.cs:3176-3180`) and `padUpdate` repeats it on a
pad, which is always the case on the Deck (`Game.cs:3624`). With a menu open, `padFocusables` adds a
`getBoundingClientRect` per button plus LINQ (`Game.cs:3225-3241`) and `setMenuBar` a `string.Join` key
(`UI/UIManager.cs:1690`). On a pad, `setHint` runs `padText()` (21 `Regex.Replace`, `UI/PadText.cs:16-46`) before checking
whether the hint changed (`UIManager.cs:1661-1671`). Keep a stack of open overlays in the open/close code, rebuild the
button bar only when focus or overlay changes, check for a changed hint first, and cache `padText` results by
(text, pad mode). The hint's DOM rebuild on each text change (`UI/Glyphs.cs:119-178`) should also skip identical text.

**3.7 Autosave without a hitch** (every 90 s, `Game.cs:51`, `4110-4120`; tens of ms on the Deck in a dug beach). On the
main thread: a disk read and parse of the save index (`Core/SaveGame.cs:115-131,152-158`), `exportDug` scanning 271k floats
and allocating a `double[2]` per dug vertex (`World/Beach.cs:920-926`), and a reflection `SerializeObject` of everything
(`SaveGame.cs:238`). Keep the index in memory; on the main thread only snapshot (`Array.Copy` of the dug array, clone of
the small state); export, serialize and write on a worker; store dug data compactly (index deltas + 16-bit depth,
base64), reading the old format too. `Core/Storage.cs:57,74` uses one lock for reads and writes; do not let a main-thread
read wait behind a background write.

**3.8 Smaller per-frame UI work.**
- Detector waveform (the default tool): Painter2D re-tessellates three round-joined strokes of ~121 points every frame,
  allocates a glow array and parses a colour string (`Mechanics/DetectorTool.cs:238-254,283-304`). One stroke of ~60
  points, updated at 30 Hz, colours parsed once.
- Minimap while moving (30 Hz, `Game.cs:6764-6776`, `UI/MapView.cs:597-689`): change key built with boxed
  `string.Format` (compare numbers instead); the CPU raster sorts with a `Comparison` per sub-scanline and allocates per fill
  (`Three/Textures/Canvas2D.cs:745-806`); draw the markers as UI elements with `UsageHints.DynamicTransform`
  (`UI/MiniMapGpu.cs:96-103`; also the bobbing key caps, `Dom/CssAnimations.cs:289-311`).

**3.9 Smaller hitches.**
- Footprints and tyre tracks re-bake the whole instanced mesh (up to 1,920 vertices x 7 channels + `mesh.Clear()`) on
  every mark (`World/Tracks.cs:75-76` -> `MeshCache.cs:261-365`); whitecaps do the same (PORT_STATUS:126). Write only the
  changed instance's vertices (`Mesh.SetVertexBufferData` with a range) or draw with `Graphics.RenderMeshInstanced`.
- Hover picking tests every triangle of the camp's clickable props at 60 Hz and allocates ~10 objects per hit plus a list,
  boxed enumerator and comparer per call (`Three/Objects/Objects.cs:68-74,123-195`, `Game.cs:6237-6240`). Use sphere/box
  proxies for clickables and reuse the result list.
- Taking a photo: an extra 1200x900 render with 4x MSAA, a `ReadPixels` stall and `EncodeToJPG` on the main thread
  (`Three/Bridge/WebGLRenderer.cs:208-256`, `Core/Photos.cs:53`), ~20-50 ms on the Deck. Use `AsyncGPUReadback` and
  encode on a worker.

**3.10 Start-up and first-use stutter.** `renderer.compile()` is a no-op (`WebGLRenderer.cs:151`), and the warm-up
renders into a 1x1 target whose format and MSAA differ from the screen (`Game.cs:1448-1456`), so the real D3D12/DXVK
pipelines are built on first sight during play. Warm up into a target matching the back buffer (format, MSAA, depth) and
record/prewarm a `GraphicsStateCollection` (Unity 6) from a full `-autoshot-perf` run. Loading takes ~6.4 s on the PC (world
build 3.6 s), so roughly 15-20 s on the Deck; cache procedural canvas textures (e.g. `World/OldPictures.cs`, 1024²) instead
of drawing them on the CPU at every start.

**3.11 Leftover garbage and audio-thread cost (low priority).** Per-frame allocations: `Core/InputManager.cs:101,129,148`,
`Mechanics/DetectorTool.cs:67`, `Game.cs:4390,4414`, the binocular zoom string even with binoculars down (`Game.cs:6449`),
`World/Sandcastles.cs:417`, a new `Interaction` + closure + label string per candidate (`Game.cs:5921-6153`),
`World/Rain.cs:33`, boxed uniform doubles (`Three/Materials/Materials.cs:23`), `classList.add/remove` allocating when nothing
changes (`Dom/DomTypes.cs:160-203`, called per frame while driving from `UIManager.cs:1393`), `Game.cs:5689`. Audio thread:
`Math.Pow` per voice per sample in `ExpRamp` (`Core/AudioManager.cs:826-840`) -> precomputed geometric step; the ocean
filter's `Pow` every 32 samples; pool `Biquad` instead of `new` on the audio thread (`AudioManager.cs:770`).

**3.12 Release compiler settings.** IL2CPP configuration is Release (`ProjectSettings.asset:599`, `Editor/BuildScript.cs:81`);
ship with **Master**. Turn off IL2CPP null and bounds checks (`[Il2CppSetOption]`) on the proven hot classes only (Matrix4,
the Canvas2D rasterizer, MeshCache, SceneSync). Keep managed stripping at Minimal (Newtonsoft needs it).

## Phase 4 - GPU work and draw calls

The Deck GPU (RDNA2, 8 CUs, 88 GB/s shared with the CPU) is the bigger unknown. Measure after each task with
`-autoshot-perf` on the Deck; stop when the budget holds.

**4.1 Shadows** (the largest GPU cost: two cascades over 60 m and soft filtering on every receiving pixel, including the
transparent ocean that is often half the screen). Apply the `handheld` values from 1.3; mark the ocean as not receiving
shadows there; stop small props from casting (coconuts, shells, coins, gull and shark parts: anything under ~0.3 m) in
`handheld` and `low`. Expected 1.5-3 ms GPU.

**4.2 Bandwidth: MSAA and the intermediate texture.** `handheld` uses 2x MSAA (1.3). `CozyURP_Renderer.asset:55` forces
the intermediate texture to *Always*; try *Auto* and check the screenshots (UI overlay, photo camera, telescope and
binocular views) still look right. Set `CozyURP.asset:94` volume updates to *Via Scripting* (no Volume is used anywhere).
Expected 1-2 ms GPU.

**4.3 Fewer draw calls** (biggest render-thread win on a 4-core CPU; most of these also draw twice more into the shadow
cascades). Nothing uses GPU instancing; only the SRP Batcher (`CozyURP.asset:72`); the static-batching flag does nothing
because nothing is marked static. Weld static parts with the existing `Merge.mergeStatic` (`World/Conform.cs:26-36`,
moving/clickable parts passed in `keep`) or by building merged geometry with vertex colours (as `World/OldHouse.cs` and
`World/ShipKit.cs` do):
- ~120 palms x (trunk, rings, 2 crowns, 3-5 coconuts) ~ 950 renderers (`World/Scenery.cs:247-300`): rings into the trunk,
  coconuts merged per palm, one shared coconut geometry; keep sway in the palm shader.
- 26 gulls x ~16 parts with per-gull geometry (`World/Birds.cs:60-127`): shared geometry, ~4 meshes per gull
  (still part, wings, folded wings, legs).
- 4 sharks x ~46 parts incl. 30 teeth (`World/SeaLife.cs:281-368`): details into one mesh; hide sharks beyond ~150 m.
- grass tufts (one mesh per beach chunk), stones, ~44 mountains, ~88 cloud sprites.
- Sprites lose the SRP Batcher because every sprite gets a MaterialPropertyBlock (`Three/Bridge/SceneSync.cs:505-513`); skip
  the block when the centre is the default one (`Resources/Shaders/CozySprite.shader:66-67` already handles it).
- Add `ProfilerRecorder` counters to `Measure` (Render category: draw calls, batches, SetPass calls, shadow casters,
  triangles; guard with `recorder.Valid`) so each step shows its effect.

**4.4 Culling.** Objects with `frustumCulled = false` get 100 km bounds (`SceneSync.cs:324-332`) and are always drawn
and shadow-cast: the ocean, the 76k-vertex grass (`World/Grass.cs:160`), fish schools (`World/SeaLife.cs:446`), particles,
rain. Give them real bounds where the content is known (grass: the beach strip; schools: their swim volume). Also fix the
stale-bounds bug: `SetCulling` only refreshes `localBounds` when the Unity mesh object changes, not when the same mesh is
re-uploaded with a new shape (`SceneSync.cs:329`). Hide the far islands' small props by distance (Lagoon Cay is drawn in
full at ~350 m). Do **not** lower the camera far plane (1000 m, `Core/Game.cs:87`) below ~900 m: the night horizon ships
sail at z 740-855 (`World/Lighthouse.cs`, `buildShips`).

**4.5 Overdraw and shading (only if the budget still is not met).**
- The sky dome is in the Geometry queue with depth writes off (`Resources/Shaders/CozySkyDome.shader:21`), so it shades the
  whole screen first; draw it after the opaques (depth test LEqual) so it only fills uncovered pixels. Check the huge sun
  and moon glow sprites (`World/Sky.cs:174-184`) and the cloud count.
- Every Cozy shader does full GGX lighting, image-based light, ACES and fog with two sRGB `pow` per pixel
  (`Resources/Shaders/CozyCore.hlsl:51-85,120-191`); the ocean adds 3 ripple reads, shadows and the light loop
  (`CozyOcean.shader:140-195`). A `COZY_LITE` global keyword for `handheld`/`low` (Lambert for rough materials, no IBL
  specular, fog in linear space) is the last resort; check the look side by side.
- Keep camp lights per-pixel: the Cozy shaders ignore per-vertex additional lights (`CozyCore.hlsl:204`); they already
  switch off beyond 30-34 m (`Game.cs:6617-6622`).

**4.6 CPU canvases and small uploads.** The detector heat map (128x128) is redrawn every frame while the aim moves,
because `moved` bypasses its 0.1 s throttle (`Mechanics/DetectorTool.cs:161`); keep the throttle. Detector rings re-upload
2x130 vertices a frame (`DetectorTool.cs:122-123`); rain refills up to 3 beach chunks of 2,809 vertices a frame
(`World/Beach.cs:765`); digging up a grass tuft re-bakes ~76k vertices (`World/Grass.cs:185`, a hitch). Spread the beach
refills (one chunk per frame) and patch grass in place.

**4.7 Memory (low).** The cave coin hoard bakes to ~333k vertices with 6 extra Vector4 streams (~40 MB,
`World/SeaCave.cs:508-509`); the bake buffers keep their capacity (`Three/Bridge/MeshCache.cs:279-281`). Trim the
buffers after baking, and give the hoard a lighter vertex format if memory shows up on the Deck (16 GB shared).

**4.8 Engine options to A/B on the Deck:** graphics jobs on vs off (the player runs `kGfxThreadingModeSplitJobs`), and
the graphics API from 1.6.

## Phase 5 - Sign-off on the Deck

Default Deck settings (fresh install, no saved settings), Game Mode, IL2CPP release build:
1. `-autoshot-perf` (with a day-5+ save too): every scene meets the 60 fps budget from Phase 1 (main <= 10 ms, GPU <= 12 ms,
   99th percentile <= 20 ms, 0 garbage); record before/after in `unity/PORT_STATUS.md`.
2. `-autoshot-ui` at 1280x800: every screen legible, nothing clipped (read the PNGs).
3. Manual play-through with the built-in controls only: title -> opening -> digging, detector, cleaning, camp shop,
   photo board, map, boat, diving, underground, lighthouse, fishing, golf cart, saving with a typed name (on-screen keyboard
   appears), loading, settings. No keyboard/mouse glyphs or wording anywhere; no cursor.
4. Suspend (power button) mid-game for a minute and resume: game paused, sound back. Open/close the Steam overlay: game
   pauses. Quit from the menu, relaunch: autosave present.
5. Steam features (with the real App ID on a beta branch): achievements unlock on Steam, cloud saves move between the PC
   and the Deck, photos appear in the Steam screenshot library.
6. Fonts: no missing glyphs (✓ ▾ ◆ and headings) under Proton.
7. Then request the Steam Deck compatibility review in Steamworks (task 2.8).

---

## Verification (per task, on the dev PC)

- Build (`BuildWindowsMono` for quick checks, `BuildWindows` for anything measured), no `error CS`.
- Run the relevant `-autoshot` flags at `-screen-width 1280 -screen-height 800` (0.1) and read both the PNGs and the
  `[AutoShot]` lines in `Player.log`. Use `-autoshot-novsync` when comparing performance.
- For performance tasks, the same `-autoshot-perf` scene before and after (dev PC first, then Deck), pasted into the
  commit message and `unity/PORT_STATUS.md`. A task that does not move the numbers is reverted.
- Existing smoke modes still pass where touched: `-autoshot-pad` (menus on a pad), `-autoshot-ui`, `-autoshot-house`,
  `-autoshot-dograin`, `-autoshot-ships`, `-autoshot-opening`, `-autoshot-photo`, `-autoshot-saves`, `-autoshot-dive`,
  `-autoshot-islands` (full list in `unity/MIGRATION.md`).
- Restore `ProjectSettings.asset` and `Emoji.asset` after Mono builds; never commit the Mono backend.
