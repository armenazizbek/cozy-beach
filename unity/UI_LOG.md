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
