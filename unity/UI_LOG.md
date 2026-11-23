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
