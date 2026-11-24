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
