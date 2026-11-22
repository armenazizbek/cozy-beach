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
