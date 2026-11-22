# Parity harness

These scripts check that the native rewrite still looks and plays like the reference build (`unity/NATIVE_PLAN.md`,
section 4). You need PowerShell, plus Python 3 with Pillow and numpy.

| File | What it does |
|---|---|
| `suites.json` | Named sets of smoke-test runs, one launch each. |
| `run_suites.ps1` | Runs suites against one build, seeded and with a fixed step, writing PNGs, a `.scene.json` per shot and a `Player.log` per run. |
| `run_perf.ps1` | Runs the `-autoshot-perf` tour with VSync off, taking turns between reference and branch. |
| `compare.py` | Compares pictures, log facts, new errors and scene dumps, and writes an HTML report. `--perf` compares frame times instead. |
| `thresholds.json` | Per-shot limits, written by `compare.py --calibrate` from two runs of the reference build. |

## Harness flags in the game (`Core/AutoShot.cs`)

- `-autoshot-seed <n>` seeds `JS.Random`. Each piece of the world built on the worker thread gets a sequence of its
  own, and the main sequence restarts once the big pieces are made.
- `-autoshot-fixedstep` moves the game's clock (`TestClock`: `JS.Now`, the page's timers and animations, the smoke
  test's waits) on by exactly 1/60 s per frame. It also sets `Time.captureDeltaTime`.
- `-autoshot-scenedump` writes every active renderer, light and Cozy shader global next to each screenshot, as
  `<shot>.scene.json` (`Core/SceneDump.cs`).

None of these flags do anything without `-autoshot`. Perf runs use real time and never `-autoshot-fixedstep`.

## Typical use

1. Run the reference build:

   ```powershell
   powershell -File unity\Tools\Parity\run_suites.ps1 -Exe C:\TidefinderParity\ref\Tidefinder.exe -Out C:\TidefinderParity\shots\ref -Suites all
   ```

2. Run the branch build:

   ```powershell
   powershell -File unity\Tools\Parity\run_suites.ps1 -Exe release\unity-win-unpacked\Tidefinder.exe -Out C:\TidefinderParity\shots\S9 -Suites core,world
   ```

3. Compare them:

   ```powershell
   python unity\Tools\Parity\compare.py C:\TidefinderParity\shots\ref C:\TidefinderParity\shots\S9 --thresholds unity\Tools\Parity\thresholds.json
   ```

The report is written to `<branch dir>\_report\report.html`. Click a branch picture there to flicker it against the
reference.

Keep the mouse pointer off the game window during runs, because the menus light up whatever it rests on.
