using System.Linq;
using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Smoke test for builds, off unless asked for:
///   Tidefinder.exe -autoshot "C:\some\folder" -screen-width 1280 -screen-height 720 -screen-fullscreen 0
/// It waits for the loading screen, saves 1-title.png and 1b-achievements.png, starts a game as Begin would, saves 2-camp.png and 3-sea.png,
/// writes the frame rate to the player log and quits. Saves go to a "saves" folder inside the shot folder, so real saves are
/// never read or written.
/// </summary>
public class AutoShot : MonoBehaviour
{
    private string dir;

    /// <summary>-autoshot-scenedump: every screenshot also gets a `.scene.json` of what was drawn (Core/SceneDump.cs).</summary>
    private static bool dumpScenes;

    /// <summary>The clock the test's waits and timings follow: real time, or frames x 1/60 s with -autoshot-fixedstep.</summary>
    private static float Now => (float)TestClock.Seconds;

    /// <summary>True while the smoke test runs; start-up checkpoints are only logged then.</summary>
    internal static bool Active { get; private set; }

    internal static void Trace(string what)
    {
        if (Active) Debug.Log($"[AutoShot] {Time.realtimeSinceStartup:0.00}s {what}");
    }

    /// <summary>The same from a worker thread (Unity's clock may only be read on the main thread).</summary>
    internal static void TraceOff(string what)
    {
        if (Active) Debug.Log($"[AutoShot] (worker) {what}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-autoshot");
        if (i < 0) return;
        Active = true;
        string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-")
            ? args[i + 1]
            : Path.Combine(Application.persistentDataPath, "autoshot");
        Directory.CreateDirectory(dir);
        Storage.UseFolder(Path.Combine(dir, "saves"));
        Photos.exportRoot = Path.Combine(dir, "pictures");

        // the parity harness (unity/NATIVE_PLAN.md section 4): runs that can be compared picture by picture and object by object
        int si = Array.IndexOf(args, "-autoshot-seed");
        if (si >= 0 && si + 1 < args.Length && int.TryParse(args[si + 1], out int seed)) JS.UseTestSeed(seed);
        if (Array.IndexOf(args, "-autoshot-fixedstep") >= 0) TestClock.UseFixedStep();
        dumpScenes = Array.IndexOf(args, "-autoshot-scenedump") >= 0;
        Debug.Log($"[AutoShot] harness: seed {(JS.TestSeed?.ToString() ?? "none")}, clock {(TestClock.Fixed ? "fixed 1/60 s per frame" : "real time")}, scene dumps {(dumpScenes ? "on" : "off")}");

        var go = new GameObject("AutoShot");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoShot>().dir = dir;
    }

    private IEnumerator Start()
    {
        Debug.Log($"[AutoShot] writing to {dir}");
        float t0 = Time.realtimeSinceStartup;
        // the loading screen is photographed part way through, and its frames are counted: the sea should keep moving
        bool loadingShot = false;
        int loadingFrames = 0, loadingSlow = 0;
        float longestFrame = 0;
        // real frame times, measured here: with -autoshot-fixedstep Unity's own delta time is the fixed step
        float lastFrame = Time.realtimeSinceStartup;
        while ((GameHost.Instance == null || GameHost.Instance.game == null || !GameHost.Instance.game.loaded)
               && Time.realtimeSinceStartup - t0 < 180)
        {
            float frameNow = Time.realtimeSinceStartup, frameDt = frameNow - lastFrame;
            lastFrame = frameNow;
            if (GameHost.Instance != null)
            {
                loadingFrames++;
                longestFrame = Math.Max(longestFrame, frameDt);
                if (frameDt > 1f / 30) loadingSlow++;
                if (frameDt > 0.1f) Debug.Log($"[AutoShot] {Time.realtimeSinceStartup:0.00}s slow loading frame: {frameDt * 1000:0} ms");
            }
            if (!loadingShot && GameHost.Instance != null && GameHost.Instance.game == null && Time.realtimeSinceStartup - t0 > 1.2f)
            {
                loadingShot = true;
                yield return Shot("0-loading.png");
                continue;
            }
            yield return null;
        }
        Debug.Log($"[AutoShot] loading screen: {loadingFrames} frames, longest {longestFrame * 1000:0} ms");
        Debug.Log($"[AutoShot] loading pacing: worst frame {longestFrame * 1000:0} ms, {loadingSlow} of {loadingFrames} frames over 33 ms, ready after {Time.realtimeSinceStartup:0.00} s");
        var game = GameHost.Instance != null ? GameHost.Instance.game : null;
        if (game == null || !game.loaded)
        {
            Debug.LogError("[AutoShot] the game did not finish loading within 180 s");
            yield return Shot("0-stuck.png");
            Quit(1);
            yield break;
        }
        Debug.Log($"[AutoShot] loaded after {Time.realtimeSinceStartup - t0:0.0} s");
        Debug.Log("[AutoShot] context: " + Context());
        // -autoshot-selfcheck: the native building blocks checked against the Three code they replace (Native/SelfCheck.cs)
        if (SelfCheck.Requested)
        {
            yield return Wait(1);
            Quit(SelfCheck.Run() == 0 ? 0 : 2);
            yield break;
        }
        // -autoshot-novsync: measure with VSync off and no frame limit, whatever the player's settings say
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-novsync") >= 0) FramePacing.Apply(false, 0);

        // let the boot screen fade and the title camera settle
        yield return Wait(6);
        yield return Shot("1-title.png");

        // -autoshot-load <id>: load that save (from this test's own saves folder) the way Load Game does, and photograph it
        var args = Environment.GetCommandLineArgs();
        int li = Array.IndexOf(args, "-autoshot-load");
        if (li >= 0 && li + 1 < args.Length)
        {
            string id = args[li + 1];
            var snap = SaveGame.readSave(id);
            Debug.Log(snap != null
                ? $"[AutoShot] save {id} reads: coins {snap.data.coins}, day {snap.data.day}"
                : $"[AutoShot] save {id} does NOT read");
            SaveGame.requestLoad(id);
            var old = game;
            GameHost.Reload();
            t0 = Time.realtimeSinceStartup;
            while ((GameHost.Instance == null || GameHost.Instance.game == null || GameHost.Instance.game == old || !GameHost.Instance.game.loaded)
                   && Time.realtimeSinceStartup - t0 < 180)
                yield return null;
            game = GameHost.Instance != null ? GameHost.Instance.game : null;
            if (game == null || game == old) { Debug.LogError("[AutoShot] the game did not come back after loading"); Quit(1); yield break; }
            Debug.Log($"[AutoShot] reloaded with the save after {Time.realtimeSinceStartup - t0:0.0} s; load failed flag: {SaveGame.lastLoadFailed}; mouse {(Cursor.lockState == CursorLockMode.Locked ? "captured" : "free, cursor showing")}");
            yield return Wait(5);
            yield return Shot("1c-loaded.png");
            // the same measurements in a played save, which has more on the beach than a new game
            yield return Measure("save standing");
            game.input.held.Add("KeyW");
            yield return Wait(1);
            yield return Measure("save walking");
            game.input.held.Clear();
            Quit(0);
            yield break;
        }

        // -autoshot-pad: drive the title menu with a virtual controller (D-pad down from New Game, then A) and check Settings opens
        if (Array.IndexOf(args, "-autoshot-pad") >= 0)
        {
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            IEnumerator Press(UnityEngine.InputSystem.LowLevel.GamepadButton b)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(b));
                yield return Wait(0.15f);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                yield return Wait(0.35f);
                Debug.Log("[AutoShot] pad " + b + " -> focused: " + (MenuNav.Current.IsNull ? "(nothing)" : MenuNav.Current.Text));
                Debug.Log("[AutoShot] pad sees " + game.smokeTestPadInfo());
            }
            Debug.Log("[AutoShot] pad sees " + game.smokeTestPadInfo());
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
            yield return Wait(0.8f);
            Debug.Log("[AutoShot] pad: settings open = " + (MenuNav.Find(UiRoot.Root, v => v.ClassListContains("settings-card")).Count > 0));
            yield return Shot("1p-pad.png");
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.East);
            yield return Wait(0.5f);
            Debug.Log("[AutoShot] pad: settings closed by B = " + (MenuNav.Find(UiRoot.Root, v => v.ClassListContains("settings-card")).Count == 0));
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }

        // -autoshot-mouse: the title menu and settings driven by a virtual mouse through the real input path (Input System, then
        // the panel, picking, hover and click), which the other runs never use; each check logs ok or FAIL (unity/UI_PLAN.md 3)
        if (Array.IndexOf(args, "-autoshot-mouse") >= 0)
        {
            // by default the Input System turns keyboards and mice off while the window is not in front, which would switch
            // this virtual one off too whenever someone uses the PC during a test run; the run's devices ignore focus
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("AutoShotMouse");
            mouse.MakeCurrent();
            int failures = 0;
            var at = new Vector2(Screen.width / 2f, Screen.height / 2f);
            void Check(string what, bool ok)
            {
                Debug.Log($"[AutoShot] mouse: {what}: {(ok ? "ok" : "FAIL")}");
                if (!ok) failures++;
            }
            // a point on the panel, as the Input System's screen position (origin at the bottom left)
            Vector2 ToScreen(Vector2 panel)
            {
                float s = Screen.width / Mathf.Max(1f, UiRoot.Root.layout.width);
                return new Vector2(panel.x * s, Screen.height - panel.y * s);
            }
            IEnumerator MoveTo(Vector2 screen, bool held = false)
            {
                var from = at;
                for (int i = 1; i <= 6; i++)
                {
                    var p = Vector2.Lerp(from, screen, i / 6f);
                    var st = new UnityEngine.InputSystem.LowLevel.MouseState { position = p, delta = p - at };
                    if (held) st = st.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, st);
                    at = p;
                    yield return null;
                }
            }
            IEnumerator Button(bool down)
            {
                var st = new UnityEngine.InputSystem.LowLevel.MouseState { position = at };
                if (down) st = st.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, st);
                yield return null;
                yield return null;
            }
            IEnumerator Click(Vector2 panel)
            {
                yield return MoveTo(ToScreen(panel));
                yield return Button(true);
                yield return Button(false);
            }
            NavEl Find(string cls, string text = null) =>
                MenuNav.Find(UiRoot.Root, v => v.ClassListContains(cls)).FirstOrDefault(e => text == null || (e.Text ?? "").Contains(text));
            bool SettingsOpen() => MenuNav.Find(UiRoot.Root, v => v.ClassListContains("settings-card")).Count > 0;

            yield return Wait(0.5f);
            var settings = Find("mm-btn", "Settings");
            Check("the main menu has a Settings button", !settings.IsNull);
            if (!settings.IsNull)
            {
                yield return MoveTo(ToScreen(settings.Box.center));
                yield return Wait(0.3f);
                Check("hovering Settings chooses it", MenuNav.Current == settings);
                yield return Click(settings.Box.center);
                yield return Wait(0.8f);
                Check("clicking Settings opens it", SettingsOpen());
            }
            var range = Find("set-range");
            if (SettingsOpen() && !range.IsNull)
            {
                var row = range.ve.parent;
                string Shown() => UnityEngine.UIElements.UQueryExtensions.Q(row, className: "set-val")?.userData is UiNode n ? n.Text : "";
                string before = Shown();
                var box = range.Box;
                yield return MoveTo(ToScreen(new Vector2(box.xMin + box.width * 0.9f, box.center.y)));
                yield return Button(true);
                yield return MoveTo(ToScreen(new Vector2(box.xMin + box.width * 0.3f, box.center.y)), held: true);
                yield return Button(false);
                yield return Wait(0.3f);
                Check($"dragging the first slider changes it ({before} -> {Shown()})", Shown() != before);
                yield return Shot("1m-mouse-settings.png");
                var rows = MenuNav.Find(UiRoot.Root, v => v.ClassListContains("set-rows")).FirstOrDefault();
                if (!rows.IsNull)
                {
                    double top = rows.ScrollTop;
                    yield return MoveTo(ToScreen(rows.Box.center));
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = at, scroll = new Vector2(0, -360) });
                    yield return null;
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = at });
                    yield return Wait(0.4f);
                    Check($"the wheel scrolls the settings ({top:0} -> {rows.ScrollTop:0})", rows.ScrollTop > top);
                }
                var done = MenuNav.Find(UiRoot.Root, v => v.ClassListContains("t-button") && v.ClassListContains("btn")).FirstOrDefault(e => (e.Text ?? "").Trim() == "Done");
                Check("settings has a Done button", !done.IsNull);
                if (!done.IsNull)
                {
                    yield return Click(done.Box.center);
                    yield return Wait(0.6f);
                    Check("clicking Done closes settings", !SettingsOpen());
                }
            }
            Debug.Log($"[AutoShot] mouse: {failures} check(s) failed");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            Quit(failures == 0 ? 0 : 2);
            yield break;
        }

        // -autoshot-keys: the title menu, settings and play driven by a virtual keyboard through the real input path (Input
        // System, then InputManager.poll and the game's key handling). The other runs press keys through InputManager.held,
        // below that path, so only this run sees a broken keyboard; each check logs ok or FAIL (unity/UI_PLAN.md B3)
        if (Array.IndexOf(args, "-autoshot-keys") >= 0)
        {
            // by default the Input System turns keyboards and mice off while the window is not in front, which would switch
            // this virtual one off too whenever someone uses the PC during a test run; the run's devices ignore focus
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            var kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("AutoShotKeyboard");
            kb.MakeCurrent();
            int failures = 0;
            void Check(string what, bool ok)
            {
                Debug.Log($"[AutoShot] keys: {what}: {(ok ? "ok" : "FAIL")}");
                if (!ok) failures++;
            }
            void Hold(params UnityEngine.InputSystem.Key[] down) =>
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(down));
            IEnumerator Tap(UnityEngine.InputSystem.Key k)
            {
                Hold(k);
                yield return null;
                yield return null;
                Hold();
                yield return null;
                yield return null;
            }
            bool Open(string cls) => MenuNav.Find(UiRoot.Root, v => v.ClassListContains(cls)).Count > 0;
            string Chosen() => MenuNav.Current.IsNull ? "nothing" : (MenuNav.Current.Text ?? "").Trim();

            // the title menu: arrows move the highlight, Enter presses it, Esc backs out of settings
            yield return Wait(0.5f);
            string first = Chosen();
            yield return Tap(UnityEngine.InputSystem.Key.DownArrow);
            yield return Wait(0.2f);
            string second = Chosen();
            Check($"Down moves the highlight ({first} -> {second})", second != first);
            yield return Tap(UnityEngine.InputSystem.Key.UpArrow);
            yield return Wait(0.2f);
            Check($"Up moves it back ({second} -> {Chosen()})", Chosen() == first);
            for (int i = 0; i < 8 && !Chosen().Contains("Settings"); i++)
            {
                yield return Tap(UnityEngine.InputSystem.Key.DownArrow);
                yield return Wait(0.15f);
            }
            Check("Down reaches Settings", Chosen().Contains("Settings"));
            yield return Tap(UnityEngine.InputSystem.Key.Enter);
            yield return Wait(0.8f);
            Check("Enter opens settings", Open("settings-card"));
            yield return Shot("1k-keys-settings.png");
            // the keys, in their own window, as a player uses it: down to a row, Enter, then the new key (Esc keeps the old one)
            IEnumerator DownTo(string label)
            {
                for (int i = 0; i < 60 && !Chosen().StartsWith(label); i++)
                {
                    yield return Tap(UnityEngine.InputSystem.Key.DownArrow);
                    yield return Wait(0.05f);
                }
            }
            Check("Settings lists no keys of its own", MenuNav.Find(UiRoot.Root, v => v.ClassListContains("key-row")).Count == 0);
            yield return DownTo("Keyboard keys");
            Check($"Down reaches Keyboard keys ({Chosen()})", Chosen().StartsWith("Keyboard keys"));
            yield return Tap(UnityEngine.InputSystem.Key.Enter);
            yield return Wait(0.6f);
            Check("Enter opens the keys window", Open("keys-card"));
            yield return Shot("1k-keys-window.png");
            yield return DownTo("Walk forward");
            Check($"Down reaches the Walk forward key ({Chosen()})", Chosen().StartsWith("Walk forward"));
            yield return Tap(UnityEngine.InputSystem.Key.Enter);
            yield return Wait(0.3f);
            Check($"Enter waits for the new key ({Chosen()})", Chosen().Contains("Press a key"));
            yield return Shot("1k-keys-listening.png");
            yield return Tap(UnityEngine.InputSystem.Key.I);
            yield return Wait(0.3f);
            Check($"pressing I binds Walk forward to it ({Controls.KeyName(Act.Forward)}; row: {Chosen()})",
                Controls.KeyName(Act.Forward) == "I" && Chosen().EndsWith("I"));
            Check("that I did not move the highlight", Chosen().StartsWith("Walk forward"));
            yield return DownTo("Use");
            yield return Tap(UnityEngine.InputSystem.Key.Enter);
            yield return Wait(0.3f);
            yield return Tap(UnityEngine.InputSystem.Key.Escape);
            yield return Wait(0.4f);
            Check($"Esc keeps the old Use key ({Controls.KeyName(Act.Use)}) and the keys window stays open",
                Controls.KeyName(Act.Use) == "F" && Open("keys-card"));
            yield return DownTo("Put every key back");
            yield return Tap(UnityEngine.InputSystem.Key.Enter);
            yield return Wait(0.3f);
            Check($"Put every key back gives Walk forward W again ({Controls.KeyName(Act.Forward)})", Controls.KeyName(Act.Forward) == "W");
            yield return Shot("1k-keys-reset.png");
            yield return Tap(UnityEngine.InputSystem.Key.Escape);
            yield return Wait(0.6f);
            Check($"Esc goes back to Settings, on Keyboard keys ({Chosen()})", !Open("keys-card") && Open("settings-card") && Chosen().StartsWith("Keyboard keys"));
            yield return Tap(UnityEngine.InputSystem.Key.Escape);
            yield return Wait(0.6f);
            Check("Esc closes settings", !Open("settings-card"));

            // play: walking, the tools, mute, pause and the map
            game.smokeTestBegin();
            yield return Wait(2f);
            var s0 = game.smokeTestKeyState();
            Hold(UnityEngine.InputSystem.Key.W);
            yield return Wait(1f);
            Hold();
            yield return Wait(0.3f);
            var s1 = game.smokeTestKeyState();
            double walked = JS.Hypot(s1.x - s0.x, s1.z - s0.z);
            Check($"holding W walks ({walked:0.00} m)", walked > 0.5);
            yield return Tap(UnityEngine.InputSystem.Key.Digit2);
            yield return Wait(0.4f);
            Check($"2 takes the trowel (tool {game.smokeTestKeyState().tool})", game.smokeTestKeyState().tool == 2);
            yield return Tap(UnityEngine.InputSystem.Key.Space);
            yield return Wait(0.4f);
            Check($"Space swaps back to the detector (tool {game.smokeTestKeyState().tool})", game.smokeTestKeyState().tool == 1);
            bool wasMuted = game.smokeTestKeyState().muted;
            yield return Tap(UnityEngine.InputSystem.Key.N);
            yield return Wait(0.2f);
            Check("N mutes", game.smokeTestKeyState().muted != wasMuted);
            yield return Tap(UnityEngine.InputSystem.Key.N);
            yield return Wait(0.2f);
            Check("N again unmutes", game.smokeTestKeyState().muted == wasMuted);
            yield return Tap(UnityEngine.InputSystem.Key.Escape);
            yield return Wait(0.6f);
            Check("Esc pauses", game.smokeTestKeyState().paused);
            yield return Tap(UnityEngine.InputSystem.Key.Escape);
            yield return Wait(0.6f);
            Check("Esc again resumes", !game.smokeTestKeyState().paused);
            yield return Tap(UnityEngine.InputSystem.Key.M);
            yield return Wait(0.6f);
            Check("M opens the map", game.smokeTestKeyState().map);
            yield return Shot("1k-keys-map.png");
            yield return Tap(UnityEngine.InputSystem.Key.M);
            yield return Wait(0.6f);
            Check("M again closes it", !game.smokeTestKeyState().map);
            // rebinding (Core/Controls.cs): Forward moved to I walks on I and no longer on W; then the defaults come back
            UnityEngine.InputSystem.InputActionRebindingExtensions.ApplyBindingOverride(Controls.Map["Forward"], 0, "<Keyboard>/i");
            Controls.Rebound();
            var r0 = game.smokeTestKeyState();
            Hold(UnityEngine.InputSystem.Key.W);
            yield return Wait(0.6f);
            Hold();
            yield return Wait(0.3f);
            var r1 = game.smokeTestKeyState();
            Hold(UnityEngine.InputSystem.Key.I);
            yield return Wait(0.6f);
            Hold();
            yield return Wait(0.3f);
            var r2 = game.smokeTestKeyState();
            double onW = JS.Hypot(r1.x - r0.x, r1.z - r0.z), onI = JS.Hypot(r2.x - r1.x, r2.z - r1.z);
            Check($"Forward rebound to I: W no longer walks ({onW:0.00} m)", onW < 0.05);
            Check($"Forward rebound to I: I walks ({onI:0.00} m)", onI > 0.3);
            // the words that name keys follow the bindings, only for the actions they are about (Controls.Say)
            UnityEngine.InputSystem.InputActionRebindingExtensions.ApplyBindingOverride(Controls.Map["Use"], 0, "<Keyboard>/g");
            Controls.Rebound();
            string said = Controls.Say("W / S drive and reverse · A / D steer · Space brakes · F gets out", Act.Use, Act.Forward);
            Check($"hints name the rebound keys ({said})", said == "I / S drive and reverse · A / D steer · Space brakes · G gets out");
            string other = Controls.Say("Q swims back up · F picks up a find", Act.Surface);
            Check($"a hint keeps keys it is not about ({other})", other == "Q swims back up · F picks up a find");
            string walk = Controls.Say("WASD swim · Space up", Act.Forward, Act.Left, Act.Backward, Act.Right, Act.Ascend);
            Check($"WASD names the four keys as bound ({walk})", walk == "I / A / S / D swim · Space up");
            game.ui.setPadMode(false);
            Check($"the prompt's key picture follows the binding ({game.ui.smokeKeyF})", game.ui.smokeKeyF == "G");
            UnityEngine.InputSystem.InputActionRebindingExtensions.ApplyBindingOverride(Controls.Map["Map"], 0, "<Keyboard>/g");
            Controls.Rebound();
            Check($"a key two actions now share is named ({string.Join(", ", Controls.SharedWith(Act.Use))})", Controls.SharedWith(Act.Use).Contains("Map"));
            Check($"keys shared from the start are not ({string.Join(", ", Controls.SharedWith(Act.ToolSwap))})", Controls.SharedWith(Act.ToolSwap).Count == 0);
            Controls.Load(defaultsOnly: true);
            Check($"with the game's own keys the words are as written ({Controls.Say("F gets out", Act.Use)})", Controls.Say("F gets out", Act.Use) == "F gets out");
            Debug.Log($"[AutoShot] keys: {failures} check(s) failed");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(kb);
            Quit(failures == 0 ? 0 : 2);
            yield break;
        }

        // -autoshot-opening: New Game, then the opening at a moment in each of its four shots, and the card that follows it
        if (Array.IndexOf(args, "-autoshot-opening") >= 0)
        {
            Debug.Log("[AutoShot] " + game.smokeTestOpening(true));
            float began = Now;
            // the deck from above, and her waterline from the side, before the shot goes on as made
            while (Now - began < 1.5f) yield return null;
            game.smokeTestOpeningView(1);
            yield return Wait(0.6f);
            yield return Shot("24-opening-deck.png");
            game.smokeTestOpeningView(2);
            yield return Wait(0.6f);
            yield return Shot("24-opening-waterline.png");
            game.smokeTestOpeningView(0);
            foreach (var (at, name) in new[] { (4.0f, "storm"), (11.0f, "storm-2"), (16.5f, "reef"), (20.5f, "reef-2"), (26.0f, "dawn"), (37.0f, "camp"), (47.5f, "card") })
            {
                while (Now - began < at) yield return null;
                Debug.Log("[AutoShot] " + game.smokeTestOpening(false));
                // in the storm the line above calls lightning, so the picture is taken in the first flash of it
                if (name.StartsWith("storm") || name.StartsWith("reef")) yield return Wait(0.04f);
                yield return Shot($"24-opening-{name}.png");
                // the opening's storm is staged: the dog must not go off to the old house in it (and walk back after)
                if (name == "storm-2" || name == "card")
                {
                    string dog = game.smokeTestDogRain("status");
                    Debug.Log($"[AutoShot] opening {name} dog: {dog} " + (dog.Contains("dog rest ") ? "ok" : "FAILED"));
                }
            }
            game.smokeTestClose("intro");
            game.ui.skipIntro();
        }

        game.smokeTestAchievements();
        yield return Wait(1);
        yield return Shot("1b-achievements.png");
        UIManager.removeFirst("achievements");

        game.smokeTestBegin();
        yield return Wait(4);
        yield return Measure("camp");
        yield return Shot("2-camp.png");

        if (Array.IndexOf(args, "-autoshot-walk") >= 0)
        {
            // stand still facing along the beach, then walk the same way, to compare the two
            game.smokeTestLook(Math.PI / 2, -0.05);
            yield return Wait(2);
            yield return Measure("standing");
            Debug.Log("[AutoShot] before walking: " + game.smokeTestWhere());
            game.input.held.Add("KeyW");
            yield return Wait(1);
            yield return Measure("walking");
            yield return Shot("2b-walking.png");
            game.input.held.Add("ShiftLeft");
            yield return Measure("running");
            game.input.held.Clear();
            Debug.Log("[AutoShot] after walking: " + game.smokeTestWhere());
        }

        // -autoshot-perf: the performance tour for the Steam Deck (STEAM_DECK_PLAN.md): the frame rate, the main, render and
        // graphics chip times and the slowest parts in each of the heavy places, then quit. Use it with -autoshot-novsync for headroom.
        if (Array.IndexOf(args, "-autoshot-perf") >= 0)
        {
            // by day: standing, walking, running along the beach
            game.smokeTestLook(Math.PI / 2, -0.05);
            yield return Wait(2);
            yield return Measure("perf: camp by day, standing");
            game.input.held.Add("KeyW");
            yield return Wait(1);
            yield return Measure("perf: beach, walking");
            game.input.held.Add("ShiftLeft");
            yield return Measure("perf: beach, running");
            game.input.held.Clear();
            // looking along the beach through the binoculars: just raised (the range finder's first look), then held
            game.smokeTestLook(Math.PI / 2, -0.05);
            game.smokeTestBinoculars(true);
            yield return Measure("perf: binoculars, just raised");
            yield return Wait(2);
            yield return Measure("perf: binoculars, held");
            game.smokeTestBinoculars(false);
            // rain at the camp
            game.smokeTestDogRain("rain");
            yield return Wait(12);
            yield return Measure("perf: rain at the camp");
            game.smokeTestDogRain("dry");
            // night at the camp, with the fire and the lanterns
            game.smokeTestTower("night");
            yield return Wait(3);
            yield return Measure("perf: camp at night");
            game.smokeTestTower("day");
            // out on the open sea at full speed
            game.smokeTestBoatAt(0, 150, 0);
            yield return Wait(2);
            game.input.held.Add("KeyW");
            yield return Wait(3);
            yield return Measure("perf: boat at full speed");
            game.input.held.Clear();
            // the lighthouse at night with its beams, once cleaned, and the ships on the horizon
            game.smokeTestTower("clean");
            yield return Wait((float)Lighthouse.INTRO_LENGTH + 2);
            game.smokeTestTower("night");
            yield return Wait(3);
            game.smokeTestBoatAt(Beach.EMBER.x + 40, Beach.EMBER.z - 70, Math.Atan2(40, -70));
            yield return Wait(3);
            yield return Measure("perf: lighthouse and ships at night");
            game.smokeTestTower("day");
            // under the sea, and in the room under the dunes
            game.smokeTestGo("dive");
            yield return Wait(3);
            yield return Measure("perf: diving");
            game.smokeTestGo("surface");
            yield return Wait(2);
            game.smokeTestGo("underground");
            yield return Wait(3);
            yield return Measure("perf: underground");
            game.smokeTestGo("surface");
            Quit(0);
            yield break;
        }

        // -autoshot-gear: the drink on the stump by the fire, for each Thermos & Gear tier (it must match the shop's picture)
        if (Array.IndexOf(args, "-autoshot-gear") >= 0)
        {
            for (int tier = 1; tier <= 3; tier++)
            {
                Debug.Log("[AutoShot] " + game.smokeTestGear(tier));
                yield return Wait(1.2f);
                yield return Shot($"28-gear-{tier}.png");
            }
        }

        // -autoshot-shop: the pictures beside everything the camp sells
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-shop") >= 0)
        {
            game.smokeTestOpen("camp:upgrades");
            yield return Wait(1.5f);
            yield return Shot("28-shop-upgrades.png");
            for (int i = 1; i <= 4; i++)
            {
                game.smokeTestCampScroll(i * 520);
                yield return Wait(0.8f);
                yield return Shot($"28-shop-upgrades-{i}.png");
            }
            game.smokeTestClose("camp:upgrades");
            yield return Wait(0.5f);
            game.smokeTestRich();
            game.smokeTestOpen("camp:luxury");
            yield return Wait(1.5f);
            yield return Shot("28-shop-luxury.png");
            game.smokeTestCampScroll(600);
            yield return Wait(0.8f);
            yield return Shot("28-shop-luxury-2.png");
            game.smokeTestClose("camp:luxury");
        }

        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-tour") >= 0)
        {
            // the screens with the most text, for checking wrapping and scrolling
            foreach (var what in new[] { "camp:board", "camp:sell", "camp:upgrades", "camp:rest", "book", "mail", "help", "credits" })
            {
                game.smokeTestOpen(what);
                yield return Wait(1.5f);
                yield return Shot("tour-" + what.Replace(':', '-') + ".png");
                game.smokeTestClose(what);
                yield return Wait(0.8f);
            }
        }

        // -autoshot-birds: a bullet at a standing gull and one at a flying gull must both pay; then the long line of keys
        // shown while swimming, which must stay clear of the tool in the corner
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-birds") >= 0)
        {
            foreach (var flying in new[] { false, true })
            {
                Debug.Log("[AutoShot] " + game.smokeTestShootBird(flying));
                yield return Wait(1);
                Debug.Log($"[AutoShot] coins after: {game.smokeTestCoins}");
            }
            game.smokeTestGo("dive");
            yield return Wait(3.5f);
            yield return Shot("10-dive-hint.png");
            game.smokeTestPad(true);
            yield return Wait(1.5f);
            yield return Shot("10-dive-hint-pad.png");
            game.smokeTestPad(false);
            game.smokeTestGo("out");
            yield return Wait(3);
        }

        // -autoshot-shadow: the player's own shadow, on the island's grass and over its stones
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-shadow") >= 0)
        {
            int k = 0;
            foreach (var (dist, angle) in new[] { (12.0, 0.4), (15.0, 2.2), (10.0, -2.4), (17.0, -1.6), (8.0, 1.2) })
            {
                game.smokeTestOwnShadow(dist, angle);
                yield return Wait(2);
                yield return Shot($"16-shadow-{k++}.png");
            }
            for (int n = 0; n < 3; n++)
            {
                Debug.Log("[AutoShot] " + game.smokeTestShadowOnRock(n, 1.6));
                yield return Wait(2);
                yield return Shot($"16-shadow-rock-{n}.png");
            }
        }

        // -autoshot-island: the island from its beach, from the slope and from across the water on the near side
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-island") >= 0)
        {
            foreach (var (name, dist, angle, pitch) in new[] { ("shore", 17.0, -1.6, -0.12), ("slope", 9.0, 0.6, -0.2), ("west", 14.0, 3.0, -0.1), ("near", 30.0, -1.57, -0.05) })
            {
                game.smokeTestIsland(dist, angle, pitch);
                yield return Wait(2.5f);
                yield return Shot($"15-island-{name}.png");
            }
            // the big map from there: the camp's badge across the water, and the player on the island
            game.smokeTestOpen("map");
            yield return Wait(1.3f);
            yield return Shot("15-island-map.png");
            game.smokeTestClose("map");
        }

        // -autoshot-dive: the sea floor from a handful of places: the way in, the reef, the kelp, the wreck and the deep
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-dive") >= 0)
        {
            game.smokeTestGo("dive");
            yield return Wait(3);
            var views = new (string name, double x, double z, double up, double yaw, double pitch)[]
            {
                ("entry", 0, 46, 1.6, 0, -0.15), ("shallows", 8, 30, 1.6, 0.6, -0.2), ("reef", -10, 10, 1.7, -0.4, -0.25),
                ("mid", 0, 0, 3.5, 0, -0.1), ("wreck", 14, -6, 2.2, 0, -0.2), ("wreck-near", 20, -14, 1.6, 1.0, -0.15),
                ("deep", -15, -32, 2.0, 0.5, -0.1), ("up", 0, 5, 2.0, 0, 0.9), ("edge", 50, -40, 2.5, -1.57, -0.1),
            };
            foreach (var v in views)
            {
                Debug.Log("[AutoShot] " + game.smokeTestDiveAt(v.x, v.z, v.up, v.yaw, v.pitch));
                yield return Wait(1.6f);
                yield return Shot($"22-dive-{v.name}.png");
            }
            yield return Measure("diving");
            game.smokeTestGo("out");
            yield return Wait(3);
        }

        // -autoshot-leap: a shark's whole leap, frame by frame: no frame may jump in height or pose. Then a shark sent straight at
        // the island, which must turn away before it reaches the shore
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-leap") >= 0)
        {
            game.smokeTestStandAt(-12, 58, Math.PI, -0.02);
            game.sea.smokeShark(-12, 92, Math.PI / 2, true);
            float began = Now;
            double lastY = double.NaN, lastP = 0, worstY = 0, worstP = 0, top = double.NegativeInfinity, bottom = double.PositiveInfinity;
            int shot = 0;
            var shots = new[] { 0.6f, 1.7f, 2.6f, 3.3f, 4.0f, 5.0f };
            while (Now - began < 5.6f)
            {
                var pose = game.sea.smokeSharkPose();
                if (!double.IsNaN(lastY))
                {
                    worstY = Math.Max(worstY, Math.Abs(pose.y - lastY));
                    worstP = Math.Max(worstP, Math.Abs(pose.pitch - lastP));
                }
                lastY = pose.y;
                lastP = pose.pitch;
                top = Math.Max(top, pose.y);
                bottom = Math.Min(bottom, pose.y);
                if (shot < shots.Length && Now - began >= shots[shot])
                {
                    yield return Shot($"28-leap-{shot}.png");
                    shot++;
                }
                yield return null;
            }
            Debug.Log($"[AutoShot] leap: highest {top:0.00}, lowest {bottom:0.00}, biggest step in a frame {worstY:0.000} m and {worstP:0.000} rad");
            // at the island, from forty metres out, heading straight for its middle
            var isl = Beach.ISLAND;
            game.sea.smokeShark(isl.x, isl.z - isl.R * 1.25 - 40, Math.PI, false);
            double closest = double.PositiveInfinity;
            began = Now;
            while (Now - began < 25)
            {
                var pose = game.sea.smokeSharkPose();
                closest = Math.Min(closest, JS.Hypot(pose.x - isl.x, pose.z - isl.z));
                yield return null;
            }
            Debug.Log($"[AutoShot] shark sent at the island: came no closer than {closest:0.0} m to its middle (its shore is about {isl.R:0} m out, the limit {isl.R * 1.25:0.0} m)");
        }

        // -autoshot-cove: the nets and their banners, from inside and from outside, and the hint out past them
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-cove") >= 0)
        {
            var views = new (string name, double x, double z, double yaw, double pitch)[]
            {
                ("top-from-camp", 0, -66, 0, 0.0), ("top-gap", 0, -74, 0, 0.12), ("camp-in", 0, -30, Math.PI, 0.0), ("side-in", 50, -10, -Math.PI / 2, 0.0),
                ("side-out", 80, -29, Math.PI / 2, 0.08), ("along", 66, 30, 0.0, -0.05), ("corner", -40, -30, 2.4, 0.0),
            };
            foreach (var v in views)
            {
                game.smokeTestStandAt(v.x, v.z, v.yaw, v.pitch);
                yield return Wait(1.6f);
                Debug.Log($"[AutoShot] cove {v.name}: " + game.smokeTestWhereLand());
                yield return Shot($"27-cove-{v.name}.png");
            }
            game.smokeTestOpen("map");
            yield return Wait(1.3f);
            yield return Shot("27-cove-map.png");
            game.smokeTestClose("map");
            // walk out through a side gap: the player gets through, and is told
            game.smokeTestStandAt(60, -29, -Math.PI / 2, 0);
            game.input.held.Add("KeyW");
            yield return Wait(4);
            game.input.held.Clear();
            Debug.Log("[AutoShot] cove walked out of the east gap: " + game.smokeTestWhereLand());
            yield return Shot("27-cove-walked-out.png");
            // and into the net where there is no gap: the player stops at it
            game.smokeTestStandAt(60, -15, -Math.PI / 2, 0);
            game.input.held.Add("KeyW");
            yield return Wait(4);
            game.input.held.Clear();
            Debug.Log("[AutoShot] cove walked into the net: " + game.smokeTestWhereLand());
        }

        // -autoshot-land: out to the east, the west and inland, walking until the mountains stop the player, then looking back
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-land") >= 0)
        {
            var walks = new (string name, double x, double z, double yaw)[]
            {
                ("east", 90, -10, -Math.PI / 2), ("west", -90, 0, Math.PI / 2), ("inland", 10, -90, 0), ("north-east", 80, -80, -Math.PI * 0.75),
            };
            foreach (var w in walks)
            {
                game.smokeTestStandAt(w.x, w.z, w.yaw, 0.02);
                yield return Wait(1.5f);
                yield return Shot($"26-land-{w.name}-start.png");
                game.input.held.Add("KeyW");
                game.input.held.Add("ShiftLeft");
                yield return Wait(30);
                game.input.held.Clear();
                Debug.Log($"[AutoShot] walked {w.name}: " + game.smokeTestWhereLand());
                yield return Wait(1);
                yield return Shot($"26-land-{w.name}-end.png");
                game.smokeTestLook(w.yaw + Math.PI, 0.0);
                yield return Wait(1.5f);
                yield return Shot($"26-land-{w.name}-back.png");
            }
            yield return Measure("far out");
            game.smokeTestOpen("map");
            yield return Wait(1.3f);
            yield return Shot("26-land-map.png");
            game.smokeTestClose("map");
        }

        // -autoshot-lighthouse: to the lighthouse door, open it, walk in and up the stair to the deck and round it, then the
        // lamp at night from the deck and from the sea
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-lighthouse") >= 0)
        {
            Debug.Log("[AutoShot] " + game.smokeTestTower("door"));
            yield return Wait(2.5f);
            Debug.Log("[AutoShot] " + game.smokeTestPrompt());
            yield return Shot("25-lighthouse-door.png");
            game.smokeTestTower("far");
            game.smokeTestTower("night");
            yield return Wait(2.5f);
            Debug.Log("[AutoShot] dirty at night: " + game.smokeTestTower("status"));
            yield return Shot("25-lighthouse-dirty-night.png");
            game.smokeTestTower("day");
            Debug.Log("[AutoShot] " + game.smokeTestTower("door"));
            yield return Wait(1.5f);
            Debug.Log("[AutoShot] " + game.smokeTestTower("open"));
            yield return Wait(1.5f);
            yield return Shot("25-lighthouse-door-open.png");
            game.input.held.Add("KeyW");
            float began = Now;
            string last = "", stage = "outside";
            float topSince = -1, logT = 0;
            while (Now - began < 60)
            {
                string s = game.smokeTestTower("steer");
                if (Now - logT > 2) { logT = Now; Debug.Log("[AutoShot] tower: " + s); }
                if (stage == "outside" && s.StartsWith("inside")) { stage = "in"; yield return Shot("25-lighthouse-inside.png"); }
                if (stage == "in" && s.Contains("feet 4.")) { stage = "low"; game.smokeTestTower("out"); yield return null; yield return null; yield return Shot("25-lighthouse-window-low.png"); }
                if (stage == "low" && s.Contains("feet 6")) { stage = "mid"; yield return Shot("25-lighthouse-stair.png"); }
                if (stage == "mid" && s.Contains("feet 10")) { stage = "high"; game.smokeTestTower("out"); yield return null; yield return null; yield return Shot("25-lighthouse-window-high.png"); }
                if ((stage == "mid" || stage == "high") && s.StartsWith("on the deck")) { stage = "top"; topSince = Now; yield return Shot("25-lighthouse-top.png"); }
                if (stage == "top" && Now - topSince > 5) { stage = "round"; yield return Shot("25-lighthouse-deck.png"); }
                if (stage == "round" && Now - topSince > 12) break;
                last = s;
                yield return null;
            }
            game.input.held.Clear();
            Debug.Log("[AutoShot] tower at the end: " + game.smokeTestTower("lamp") + " (" + stage + ")");
            yield return Wait(1);
            yield return Shot("25-lighthouse-lamp.png");
            Debug.Log("[AutoShot] entering inspect: " + game.smokeTestTower("inspect"));
            yield return Wait(1.2f);
            Debug.Log("[AutoShot] scrubbing: " + game.smokeTestTower("scrub"));
            yield return Wait(0.5f);
            yield return Shot("25-lighthouse-cleaning.png");
            Debug.Log("[AutoShot] finishing clean: " + game.smokeTestTower("clean"));
            yield return Wait(3.8f);
            Debug.Log("[AutoShot] intro shot 1: " + game.smokeTestTower("status"));
            yield return Shot("25-lighthouse-intro-1.png");
            yield return Wait(3.2f);
            Debug.Log("[AutoShot] intro shot 2 (horizon): " + game.smokeTestTower("status"));
            yield return Shot("25-lighthouse-intro-2.png");
            yield return Wait(2.6f);
            Debug.Log("[AutoShot] after intro: " + game.smokeTestTower("lamp"));
            yield return Shot("25-lighthouse-lamp-night.png");
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(Beach.EMBER.x + 40, Beach.EMBER.z - 70, Math.Atan2(40, -70)));
            yield return Wait(3);
            yield return Shot("25-lighthouse-night-sea.png");
            // switched off: no light and no ships at night, until it is switched back on
            game.smokeTestTower("lampoff");
            yield return Wait(3);
            Debug.Log("[AutoShot] lamp switched off: " + game.smokeTestTower("status"));
            yield return Shot("25-lighthouse-night-sea-off.png");
            game.smokeTestTower("lampon");
            yield return Wait(1);
            Debug.Log("[AutoShot] lamp switched on: " + game.smokeTestTower("status"));
            game.smokeTestTower("day");
            yield return Wait(3);
        }

        // -autoshot-gullshadow: a gull held high over the beach: its shadow on the sand, then the bird itself
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-gullshadow") >= 0)
        {
            Debug.Log("[AutoShot] " + game.smokeTestGull("hold"));
            yield return Wait(2.5f);
            Debug.Log("[AutoShot] " + game.smokeTestGull("look"));
            yield return Wait(0.5f);
            yield return Shot("32-gull-shadow.png");
            game.smokeTestGull("up");
            yield return Wait(0.4f);
            yield return Shot("32-gull-up.png");
        }

        // -autoshot-ships: each of the four horizon ships held still at night, near and far, broadside and from the bow, with the
        // lighthouse beam on her and without
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-ships") >= 0)
        {
            string[] names = { "cruise", "container", "tanker", "bulk" };
            for (int i = 0; i < names.Length; i++)
            {
                Debug.Log("[AutoShot] " + game.smokeTestShips(i, 85, Math.PI / 2, 0));
                yield return Wait(2.5f);
                yield return Shot($"31-ship-{names[i]}-side.png");
                game.smokeTestShips(i, 75, 0.8, 1);
                yield return Wait(0.6f);
                yield return Shot($"31-ship-{names[i]}-quarter-lit.png");
                game.smokeTestShips(i, 300, 1.2, 0);
                yield return Wait(0.6f);
                yield return Shot($"31-ship-{names[i]}-far.png");
            }
        }

        // -autoshot-telescope: the telescope on Lagoon Cay, then the view through it at night: the sky as it opens, a few stars of
        // the Great Bear lit, the Great Bear found, Orion brought into focus by framing it, a hint, the view by day, and every
        // constellation found at last (the log says the coins, the save and the achievement)
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-telescope") >= 0)
        {
            // first it is caked in sand: it goes to the workbench once, and is clean for good after that
            Debug.Log("[AutoShot] " + game.smokeTestScope("walk"));
            yield return Wait(3);
            yield return Shot("27-telescope-dirty.png");
            Debug.Log("[AutoShot] to the bench: " + game.smokeTestScope("brush"));
            yield return Wait(1.2f);
            game.smokeTestScope("scrub");
            yield return Wait(0.5f);
            yield return Shot("27-telescope-cleaning.png");
            game.smokeTestScope("cleaned");
            yield return Wait(3);
            Debug.Log("[AutoShot] brushed: " + game.smokeTestScope("status"));
            yield return Shot("27-telescope-cleaned.png");
            game.smokeTestScope("done");
            yield return Wait(1.5f);
            Debug.Log("[AutoShot] back: " + game.smokeTestScope("walk"));
            yield return Wait(1);
            yield return Shot("27-telescope-prop.png");
            game.smokeTestScope("night");
            yield return Wait(1);
            Debug.Log("[AutoShot] opening: " + game.smokeTestScope("open"));
            yield return Wait(1.5f);
            Debug.Log("[AutoShot] eyepiece: " + game.smokeTestScope("status"));
            yield return Shot("27-telescope-eyepiece.png");
            game.smokeTestScope("pan");
            yield return Wait(0.6f);
            yield return Shot("27-telescope-wide.png");
            game.smokeTestScope("look:great_bear");
            Debug.Log("[AutoShot] three stars: " + game.smokeTestScope("part:great_bear"));
            yield return Wait(0.8f);
            yield return Shot("27-telescope-lit.png");
            // the rest of its stars, each turned into the aiming ring and lit with A, as a player does now
            for (int k = 3; k < 12; k++)
            {
                if (game.smokeTestScope($"aim:great_bear:{k}") == "no such star") break;
                yield return Wait(0.3f);
                if (k == 3) yield return Shot("27-telescope-aim.png");
            }
            Debug.Log("[AutoShot] all its stars, aimed and lit: " + game.smokeTestScope("status"));
            yield return Wait(3);
            yield return Shot("27-telescope-found.png");
            game.smokeTestScope("look:orion");
            yield return Wait(1.5f);
            Debug.Log("[AutoShot] framing Orion: " + game.smokeTestScope("status"));
            yield return Shot("27-telescope-focusing.png");
            yield return Wait(3);
            Debug.Log("[AutoShot] after framing: " + game.smokeTestScope("status"));
            yield return Shot("27-telescope-focused.png");
            Debug.Log("[AutoShot] hint: " + game.smokeTestScope("hint"));
            yield return Wait(1.5f);
            yield return Shot("27-telescope-hint.png");
            game.smokeTestScope("close");
            yield return Wait(1.2f);
            // by day there is nothing to see: the telescope stays shut and says to come back after dusk
            game.smokeTestScope("day");
            yield return Wait(1.5f);
            game.smokeTestScope("open");
            yield return Wait(0.8f);
            Debug.Log("[AutoShot] by day (must stay shut): " + game.smokeTestScope("status"));
            yield return Shot("27-telescope-day-refused.png");
            game.smokeTestScope("night");
            yield return Wait(1.5f);
            game.smokeTestScope("open");
            yield return Wait(1.5f);
            foreach (var c in Constellations.ALL)
            {
                game.smokeTestScope("look:" + c.id);
                game.smokeTestScope("click:" + c.id);
                yield return Wait(0.3f);
            }
            game.smokeTestScope("look:marigold");
            yield return Wait(2.5f);
            Debug.Log("[AutoShot] all found: " + game.smokeTestScope("status"));
            yield return Shot("27-telescope-all.png");
            game.smokeTestScope("close");
            yield return Wait(1.5f);
            Debug.Log("[AutoShot] closed: " + game.smokeTestScope("status"));
            yield return Shot("27-telescope-after.png");
        }

        // -autoshot-dolphins: a run across open water with the pod called up: arriving, alongside, and left behind when the boat stops
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-dolphins") >= 0)
        {
            Debug.Log("[AutoShot] " + game.smokeTestDolphins(0));
            game.input.held.Add("KeyW");
            yield return Wait(4);
            Debug.Log("[AutoShot] " + game.smokeTestDolphins(0));
            yield return Shot("21-dolphins-arriving.png");
            for (int i = 0; i < 5; i++)
            {
                yield return Wait(i == 0 ? 2.2f : 0.55f);
                Debug.Log("[AutoShot] " + game.smokeTestDolphins(i < 3 ? 0 : i == 3 ? 0.7 : -0.7));
                yield return Wait(0.25f);
                yield return Shot($"21-dolphins-{i}.png");
            }
            yield return Measure("dolphins");
            game.input.held.Clear();
            yield return Wait(7);
            Debug.Log("[AutoShot] " + game.smokeTestDolphins(0));
            yield return Shot("21-dolphins-gone.png");
            game.smokeTestOpen("book");
            yield return Wait(1.2f);
            yield return Shot("21-dolphins-journal.png");
            game.smokeTestClose("book");
        }

        // -autoshot-islands: the two far islands. Each from the boat, from its shore and at its landmark; the lagoon from inside,
        // with the step ashore; then the big map with all three on it
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-islands") >= 0)
        {
            var lag = Beach.LAGOON;
            var emb = Beach.EMBER;
            // heading: the boat's forward is (-sin, -cos), so this one looks from the gap side in at the cay
            double gx = Math.Cos(Beach.LAGOON_GAP), gz = Math.Sin(Beach.LAGOON_GAP);
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(lag.x + gx * 62, lag.z + gz * 62, Math.Atan2(gx, gz)));
            yield return Wait(3);
            yield return Shot("17-lagoon-from-sea.png");
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(lag.x + gx * lag.R * 0.66, lag.z + gz * lag.R * 0.66, Math.Atan2(gx, gz)));
            yield return Wait(2);
            yield return Shot("17-lagoon-gap.png");
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(lag.x + 3, lag.z - 2, Math.Atan2(gx, gz)));
            yield return Wait(2);
            yield return Shot("17-lagoon-inside.png");
            // across the lagoon, close enough in to the far shore to step off
            for (double d = 6; d < 18; d += 1)
            {
                string msg = game.smokeTestBoatAt(lag.x - gx * d, lag.z - gz * d, Math.Atan2(gx, gz));
                if (!msg.EndsWith("near True")) continue;
                Debug.Log("[AutoShot] " + msg);
                break;
            }
            yield return Wait(1);
            Debug.Log("[AutoShot] " + game.smokeTestPrompt());
            Debug.Log("[AutoShot] " + game.smokeTestLeaveBoat());
            yield return Wait(2.5f);
            yield return Shot("17-lagoon-ashore.png");
            Debug.Log("[AutoShot] " + game.smokeTestLandmark(1, gx * 7.5, gz * 7.5, -0.12));
            yield return Wait(2.5f);
            yield return Shot("17-lagoon-camp.png");
            Debug.Log("[AutoShot] " + game.smokeTestLandmark(1, -gz * 5 + gx * 3, gx * 5 + gz * 3, -0.3));
            yield return Wait(2.5f);
            yield return Shot("17-lagoon-camp-side.png");
            // frame rate while walking on the cay and on the rock, which are the heaviest places in the world
            game.smokeTestLandmark(1, gx * 7.5, gz * 7.5, -0.12);
            game.input.held.Add("KeyW");
            yield return Wait(1);
            yield return Measure("walking on the cay");
            game.input.held.Clear();
            game.smokeTestIsland(lag.R * 0.86, Beach.LAGOON_GAP + 1.9, -0.1, 1);
            yield return Wait(2.5f);
            yield return Shot("17-lagoon-shore.png");
            Debug.Log("[AutoShot] " + game.smokeTestWhere());

            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(emb.x + 30, emb.z - 52, Math.Atan2(30, -52)));
            yield return Wait(3);
            yield return Shot("18-ember-from-sea.png");
            for (double d = emb.R * 1.2; d > emb.R * 0.5; d -= 1)
            {
                string msg = game.smokeTestBoatAt(emb.x + 0.406 * d, emb.z - 0.914 * d, Math.Atan2(0.406, -0.914));
                if (!msg.EndsWith("near True")) continue;
                Debug.Log("[AutoShot] " + msg);
                break;
            }
            yield return Wait(1);
            Debug.Log("[AutoShot] " + game.smokeTestPrompt());
            Debug.Log("[AutoShot] " + game.smokeTestLeaveBoat());
            yield return Wait(2.5f);
            yield return Shot("18-ember-ashore.png");
            game.smokeTestIsland(emb.R * 0.8, -1.15, -0.02, 2);
            game.input.held.Add("KeyW");
            yield return Wait(1);
            yield return Measure("walking on the rock");
            game.input.held.Clear();
            yield return Wait(2.5f);
            yield return Shot("18-ember-columns.png");
            game.smokeTestIsland(emb.R * 0.72, 1.3, 0.1, 2);
            yield return Wait(2.5f);
            yield return Shot("18-ember-cone.png");
            Debug.Log("[AutoShot] " + game.smokeTestLandmark(2, 13, 8, 0.32));
            yield return Wait(2.5f);
            yield return Shot("18-ember-lighthouse.png");
            Debug.Log("[AutoShot] " + game.smokeTestLandmark(2, -1.4, -5.0, -0.3));
            yield return Wait(2.5f);
            yield return Shot("18-ember-cairn.png");
            game.smokeTestOpen("map");
            yield return Wait(1.3f);
            yield return Shot("19-islands-map.png");
            game.smokeTestClose("map");
            // the first island and the beach from the open sea between them, to see how the far ones sit on the horizon
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(0, 120, Math.Atan2(-196, -172)));
            yield return Wait(3);
            yield return Shot("19-horizon-east.png");
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(0, 120, Math.Atan2(204, -192)));
            yield return Wait(3);
            yield return Shot("19-horizon-west.png");
            game.smokeTestShowItems(new[] { "spyglass", "nautilus", "red_coral", "sun_disc", "fire_opal", "obsidian_blade" });
            yield return Wait(1);
            yield return Shot("20-far-finds.png");
            // the edge of the sea: the boat must stop there, and the sky must still be overhead
            Debug.Log("[AutoShot] " + game.smokeTestBoatAt(280, 410, Math.Atan2(-1, -1)));
            yield return Wait(2);
            yield return Shot("20-sea-corner.png");
        }

        // -autoshot-crust: finds of different shapes on the workbench, still in their sand; the sand must follow each shape
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-crust") >= 0)
        {
            var crustIds = Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-islands") >= 0
                ? new[] { "spyglass", "nautilus", "red_coral", "sun_disc", "fire_opal", "obsidian_blade" }
                : new[] { "doubloon", "brass_key", "signet", "glass_bottle", "flip_flop", "pocket_watch" };
            foreach (var id in crustIds)
            {
                game.smokeTestCrust(id);
                yield return Wait(1.6f);
                yield return Shot($"14-crust-{id}.png");
                if (id == "doubloon")
                {
                    Debug.Log($"[AutoShot] brushed a band across the doubloon: {game.smokeTestScrub():0.00} clean");
                    yield return Wait(0.5f);
                    yield return Shot("14-crust-doubloon-brushed.png");
                }
                game.smokeTestCrust(null);
                yield return Wait(1);
            }
        }

        // -autoshot-saves: three autosaves in one game, then a new game and two more; each game must keep its own two
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-saves") >= 0)
        {
            for (int i = 0; i < 3; i++)
            {
                game.smokeTestAutosave();
                yield return Wait(0.3f);
            }
            Debug.Log("[AutoShot] game one: " + game.smokeTestSaves());
            game.smokeTestNewGame();
            float w0 = Time.realtimeSinceStartup;
            while ((GameHost.Instance == null || GameHost.Instance.game == null || GameHost.Instance.game == game || !GameHost.Instance.game.loaded)
                   && Time.realtimeSinceStartup - w0 < 180)
                yield return null;
            game = GameHost.Instance.game;
            yield return Wait(3);
            if (!game.smokeTestStarted) game.smokeTestBegin();
            yield return Wait(2);
            for (int i = 0; i < 2; i++)
            {
                game.smokeTestAutosave();
                yield return Wait(0.3f);
            }
            Debug.Log("[AutoShot] game two: " + game.smokeTestSaves());
            game.smokeTestOpen("saves-load");
            yield return Wait(1.3f);
            yield return Shot("13-load-list.png");
            game.smokeTestClose("saves");
        }

        // -autoshot-rain: a deep hole and a sandcastle in a shower; the rain must fill the one and wash the other away
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-rain") >= 0)
        {
            Debug.Log("[AutoShot] before the rain: " + game.smokeTestRain(true));
            yield return Wait(4);
            yield return Shot("12-rain-start.png");
            foreach (var at in new[] { 20, 45, 70 })
            {
                yield return Wait(at == 20 ? 16 : 25);
                Debug.Log($"[AutoShot] after {at} s of rain: " + game.smokeTestRainStatus());
                yield return Shot($"12-rain-{at}s.png");
            }
            game.smokeTestRainStop();
        }

        // -autoshot-pier: the walk down to the pier and along it, which must be free of palms
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-pier") >= 0)
        {
            foreach (var back in new[] { 14.0, 4.0 })
            {
                game.smokeTestOnPier(Pier.Z0 - back, Math.PI, -0.04);
                yield return Wait(2);
                yield return Shot($"11-pier-from-{back:0}m.png");
            }
        }

        // -autoshot-holes: deep holes up the beach, on the wet sand and at the water's edge (none may show water), then the
        // cart parked across one
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-holes") >= 0)
        {
            foreach (var z in new[] { 5.0, 33.0, 37.5 })
            {
                Debug.Log("[AutoShot] " + game.smokeTestHole(6, z));
                yield return Wait(2);
                yield return Shot($"9-hole-z{z:0}.png");
            }
            Debug.Log("[AutoShot] " + game.smokeTestCartOnHole(6, 5));
            yield return Wait(2);
            yield return Shot("9-cart-on-hole.png");
        }

        // -autoshot-smash: a castle burst by the golf cart, watched from the side at moments through it, then the cart driven
        // through two castles in a line (the log says whether it got through and how many still stand)
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-smash") >= 0)
        {
            Debug.Log("[AutoShot] " + game.smokeTestSmash("watch", -8, 8));
            yield return Wait(2.5f);
            yield return Shot("29-smash-0.png");
            Debug.Log("[AutoShot] " + game.smokeTestSmash("burst", -8, 8));
            float began = Now;
            foreach (var (at, name) in new[] { (0.12f, "1"), (0.35f, "2"), (0.7f, "3"), (1.3f, "4"), (2.6f, "5") })
            {
                while (Now - began < at) yield return null;
                yield return Shot($"29-smash-{name}.png");
            }
            Debug.Log("[AutoShot] " + game.smokeTestSmash("drive", -20, 0));
            yield return Wait(0.5f);
            game.input.held.Add("KeyW");
            for (int i = 0; i < 4; i++)
            {
                yield return Wait(0.6f);
                Debug.Log("[AutoShot] " + game.smokeTestSmash("status", 0, 0));
                yield return Shot($"29-drive-{i}.png");
            }
            game.input.held.Clear();
        }

        // -autoshot-newgame: New Game from the pause menu must play the opening, as New Game on the title screen does
        if (Array.IndexOf(args, "-autoshot-newgame") >= 0)
        {
            var before = game;
            game.smokeTestPauseNewGame();
            float t1 = Time.realtimeSinceStartup;
            while ((GameHost.Instance == null || GameHost.Instance.game == null || GameHost.Instance.game == before || !GameHost.Instance.game.loaded)
                   && Time.realtimeSinceStartup - t1 < 180)
                yield return null;
            game = GameHost.Instance != null ? GameHost.Instance.game : null;
            if (game == null || game == before) { Debug.LogError("[AutoShot] the new game did not come up"); Quit(1); yield break; }
            yield return Wait(3);
            Debug.Log("[AutoShot] new game from the pause menu, after 3 s: " + game.smokeTestStage());
            yield return Shot("30-newgame-opening.png");
            Quit(0);
            yield break;
        }

        // -autoshot-sweep: where the detector sweeps looking ahead and looking down; then, running, the spot it sweeps must
        // move smoothly (the log gives the biggest jump from one frame to the next, ahead and sideways)
        if (Array.IndexOf(args, "-autoshot-sweep") >= 0)
        {
            game.smokeTestStandAt(-10, 0, Math.PI, -0.02);
            yield return Wait(1);
            var a0 = game.smokeTestAim();
            Debug.Log(a0 != null ? $"[AutoShot] sweep looking ahead: {a0[0]:0.00} m ahead, {a0[1]:0.00} m aside" : "[AutoShot] sweep looking ahead: NOTHING swept");
            yield return Shot("31-sweep-ahead.png");
            game.smokeTestStandAt(-10, 0, Math.PI, -0.75);
            yield return Wait(1);
            var a1 = game.smokeTestAim();
            Debug.Log(a1 != null ? $"[AutoShot] sweep looking down: {a1[0]:0.00} m ahead, {a1[1]:0.00} m aside" : "[AutoShot] sweep looking down: NOTHING swept");
            foreach (var pitch in new[] { -0.02, -0.45 })
            {
                game.smokeTestStandAt(-10, -20, Math.PI, pitch);
                yield return Wait(0.5f);
                game.input.held.Add("KeyW");
                game.input.held.Add("ShiftLeft");
                yield return Wait(0.8f);
                double[] prev = null;
                double jumpAhead = 0, jumpSide = 0;
                int frames = 0, none = 0;
                float until = Now + 2;
                while (Now < until)
                {
                    yield return null;
                    var a = game.smokeTestAim();
                    frames++;
                    if (a == null) { none++; prev = null; continue; }
                    if (prev != null)
                    {
                        jumpAhead = Math.Max(jumpAhead, Math.Abs(a[0] - prev[0]));
                        jumpSide = Math.Max(jumpSide, Math.Abs(a[1] - prev[1]));
                    }
                    prev = a;
                }
                game.input.held.Clear();
                Debug.Log($"[AutoShot] sweep while running, pitch {pitch:0.00}: biggest jump in a frame {jumpAhead * 100:0.0} cm ahead, {jumpSide * 100:0.0} cm aside; nothing swept in {none} of {frames} frames");
                yield return Shot($"31-sweep-run{(pitch < -0.1 ? "-down" : "")}.png");
            }
        }

        // -autoshot-house: the old house past the nets, from the beach side, close to its porch, and from behind
        if (Array.IndexOf(args, "-autoshot-house") >= 0)
        {
            double hx = OldHouse.X, hz = OldHouse.Z;
            foreach (var (dx, dz, pitch, name) in new[] { (13.0, 8.0, -0.06, "beach"), (6.5, 1.5, 0.02, "porch"), (-9.0, 8.0, 0.0, "back"), (2.0, -11.0, 0.0, "side") })
            {
                double px = hx + dx, pz = hz + dz;
                game.smokeTestStandAt(px, pz, Math.Atan2(-(hx - px), -(hz - pz)), pitch);
                yield return Wait(1.2f);
                Debug.Log($"[AutoShot] house from the {name}: {game.smokeTestWhere()}");
                yield return Shot($"32-house-{name}.png");
            }
            // the pictures on the back wall: the captain and his ship, in their frames
            game.smokeTestStandAt(hx + 1.0, hz, Math.PI / 2, 0.02);
            yield return Wait(1);
            yield return Shot("32-house-pictures.png");
            game.smokeTestStandAt(hx - 0.5, hz + 0.6, Math.PI / 2, 0.05);
            yield return Wait(1);
            yield return Shot("32-house-frames.png");
            // the captain's table and chair under the left window: chart, sealing wax and green bottle
            game.smokeTestStandAt(hx - 0.1, hz + 0.3, Math.Atan2(-1.88, -1.5), -0.32);
            yield return Wait(1);
            yield return Shot("32-house-table.png");
            // and the chart itself, from close above the table
            game.smokeTestStandAt(hx + 1.0, hz + 1.5, Math.Atan2(-0.76, -0.42), -0.77);
            yield return Wait(0.8f);
            yield return Shot("32-house-chart.png");
            // the house on the minimap and the big map, seen from up the beach so the player's arrow is clear of it
            game.smokeTestStandAt(hx + 22, hz, Math.PI / 2, -0.05);
            yield return Wait(1.5f);
            yield return Shot("32-house-minimap.png");
            game.smokeTestOpen("map");
            yield return Wait(1.2f);
            yield return Shot("32-house-map.png");
            game.smokeTestClose("map");
            yield return Wait(0.5f);
            // walking in: from the sand in front of the steps, up them, across the porch, in at the door, to the back wall
            game.smokeTestStandAt(hx + 7.5, hz, Math.PI / 2, -0.05);
            yield return Wait(0.8f);
            game.input.held.Add("KeyW");
            for (int i = 0; i < 8; i++)
            {
                yield return Wait(0.5f);
                Debug.Log($"[AutoShot] walking in, {(i + 1) * 0.5:0.0} s: {game.smokeTestHouse()}");
                if (i == 2) yield return Shot("32-house-steps.png");
                if (i == 3) yield return Shot("32-house-room.png");
            }
            game.input.held.Clear();
            yield return Shot("32-house-inside.png");
            // looking back out of the door
            game.smokeTestLook(-Math.PI / 2, -0.05);
            yield return Wait(0.8f);
            yield return Shot("32-house-door-out.png");
            // and into a side wall from outside: it must stop you on the sand
            game.smokeTestStandAt(hx, hz - 7, Math.PI, -0.05);
            yield return Wait(0.5f);
            game.input.held.Add("KeyW");
            yield return Wait(3f);
            game.input.held.Clear();
            Debug.Log($"[AutoShot] walked into the end wall from outside: {game.smokeTestHouse()}");
        }

                // -autoshot-dograin: rain sends the dog to the old house, where it lies until the rain stops; no bone hunts meanwhile
        if (Array.IndexOf(args, "-autoshot-dograin") >= 0)
        {
            Debug.Log("[AutoShot] before: " + game.smokeTestDogRain("status"));
            game.smokeTestDogRain("rain");
            for (int i = 0; i < 16; i++)
            {
                yield return Wait(3);
                string st = game.smokeTestDogRain("status");
                Debug.Log($"[AutoShot] raining, {(i + 1) * 3} s: {st}");
                if (st.Contains("dog sheltered")) break;
            }
            Debug.Log("[AutoShot] a bone in the rain: " + game.smokeTestDogRain("bone"));
            game.smokeTestDogRain("watch");
            yield return Wait(1.5f);
            yield return Shot("33-dog-sheltered.png");
            // water through the hole in the roof: at the start of a rain, and after a long one; the dog's corner must stay dry
            game.smokeTestDogRain("leak");
            yield return Wait(1.0f);
            yield return Shot("33-house-leak-start.png");
            game.smokeTestDogRain("wet");
            yield return Wait(1.0f);
            yield return Shot("33-house-leak-wet.png");
            // a moment later: every drop must be lower than it was
            yield return Wait(0.06f);
            yield return Shot("33-house-leak-wet-later.png");
            game.smokeTestDogRain("watch");
            yield return Wait(1.0f);
            yield return Shot("33-house-wet-dog.png");
            // from indoors the rain still falls outside the door, and none inside the room
            game.smokeTestDogRain("lookout");
            yield return Wait(1.0f);
            yield return Shot("33-house-lookout.png");
            // the d-pad picker with four tools behind its left slot
            game.smokeTestPad(true);
            game.smokeTestDogRain("tools");
            yield return Wait(1.0f);
            yield return Shot("33-pad-tools.png");
            game.smokeTestPad(false);
            game.smokeTestDogRain("dry");
            for (int i = 0; i < 16; i++)
            {
                yield return Wait(3);
                string st = game.smokeTestDogRain("status");
                Debug.Log($"[AutoShot] dry, {(i + 1) * 3} s: {st}");
                if (st.Contains("dog rest")) break;
            }
        }

        // -autoshot-layout: where the finds land, over many fresh layouts of the beach (lost things spread over the cove)
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-layout") >= 0)
            Debug.Log("[AutoShot] layout: " + game.smokeTestLayout(200));

        // -autoshot-ui: every menu and the HUD, as they look with the keyboard and then with a controller
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-ui") >= 0)
        {
            foreach (var mode in new[] { "kb", "pad" })
            {
                game.smokeTestPad(mode == "pad");
                yield return Wait(0.5f);
                foreach (var what in new[] { "toast", "pause", "settings", "saves", "achievements", "help", "map", "camp:upgrades", "camp:sell", "result", "inspect", "intro" })
                {
                    game.smokeTestOpen(what);
                    yield return Wait(1.3f);
                    yield return Shot($"ui-{mode}-{what.Replace(':', '-')}.png");
                    game.smokeTestClose(what);
                    yield return Wait(0.6f);
                }
            }
            game.smokeTestPad(false);
        }

        // -autoshot-photo: raise the camera, take three photos, look at the board and the album, then save, load and look again
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-photo") >= 0)
        {
            // the dog from a distance: lying down until the camera comes up, then sitting and posing
            game.smokeTestFaceDog(9);
            yield return Wait(3);
            yield return Shot("4a-dog-far.png");
            game.smokeTestRaiseCamera();
            yield return Wait(3);
            yield return Shot("4a-dog-far-pose.png");
            game.smokeTestFaceDog(3);
            yield return Wait(2.5f);
            game.smokeTestTakePhoto();
            yield return Wait(0.6f);
            yield return Shot("4a-dog-close-pose.png");
            // right beside the dog its "Pet the dog" prompt is due: it must not show through the viewfinder, and must come
            // back when the camera is lowered
            game.smokeTestFaceDog(1.5);
            yield return Wait(1.2f);
            Debug.Log("[AutoShot] in reach while the camera is up (its prompt is hidden): " + game.smokeTestPrompt());
            yield return Shot("4a-prompt-hidden.png");
            game.smokeTestLowerCamera();
            yield return Wait(1);
            yield return Shot("4a-prompt-back.png");
            game.smokeTestRaiseCamera();
            yield return Wait(1.5f);
            Debug.Log("[AutoShot] photo copied to: " + (game.smokeTestExportPhoto() ?? "(failed)"));
            game.smokeTestLook(0, -0.12);
            yield return Wait(1.5f);
            yield return Shot("4-viewfinder.png");
            foreach (var yaw in new[] { 0.0, Math.PI, Math.PI / 2 })
            {
                game.smokeTestLook(yaw, -0.05);
                yield return Wait(1.2f);
                game.smokeTestTakePhoto();
                yield return Wait(1.2f);
            }
            Debug.Log($"[AutoShot] photos in the album: {game.smokeTestPhotoCount}");
            game.smokeTestLowerCamera();
            game.smokeTestFacePhotoBoard();
            yield return Wait(2);
            yield return Shot("4b-board.png");
            game.smokeTestOpen("camp:photos");
            yield return Wait(1.5f);
            yield return Shot("4c-album.png");
            game.smokeTestClose("camp:photos");
            yield return Wait(0.5f);
            Debug.Log("[AutoShot] photo save written: " + game.smokeTestSave("phototest"));
            Photos.clearCache();
            SaveGame.requestLoad("phototest");
            var before = game;
            GameHost.Reload();
            yield return ReloadFrameCheck();
            t0 = Time.realtimeSinceStartup;
            while ((GameHost.Instance == null || GameHost.Instance.game == null || GameHost.Instance.game == before || !GameHost.Instance.game.loaded)
                   && Time.realtimeSinceStartup - t0 < 180)
                yield return null;
            game = GameHost.Instance.game;
            yield return Wait(4);
            Debug.Log($"[AutoShot] photos after loading: {game.smokeTestPhotoCount}");
            game.smokeTestFacePhotoBoard();
            yield return Wait(2);
            yield return Shot("4d-board-loaded.png");
        }

        // -autoshot-sealife: a shark at the top of its leap, another cruising, and fish at the surface and in the air, close up
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-sealife") >= 0)
        {
            game.smokeTestSeaLife(0, 0.05, 9);
            yield return Wait(2.5f);
            yield return Shot("8-sea-wide.png");
            game.smokeTestSeaLife(0.12, -0.02, 6);
            yield return Wait(1.5f);
            yield return Shot("8-shark-close.png");
            game.smokeTestSeaLife(-0.42, -0.28, 6);
            yield return Wait(1.5f);
            yield return Shot("8-fish-close.png");
        }

        // -autoshot-fish: the pier and the moored boat, fishing from the bench, the net underwater, and the aquarium
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-fish") >= 0)
        {
            game.smokeTestSeePier();
            yield return Wait(2);
            yield return Shot("7-pier-from-beach.png");
            game.smokeTestOnPier(41, Math.PI, -0.1);
            yield return Wait(2);
            yield return Shot("7-pier-walk.png");
            game.smokeTestOnPier(58, Math.PI, -0.25);
            yield return Wait(2);
            yield return Shot("7-pier-end.png");
            Debug.Log("[AutoShot] on the pier: " + game.smokeTestWhere());
            game.smokeTestSit();
            yield return Wait(1.5f);
            yield return Shot("7-fish-1-seated.png");
            // fish as a player would until two are in the bucket, or a minute has gone by
            float fishT0 = Now;
            string last = "";
            bool shotWait = false, shotReel = false;
            while (Now - fishT0 < 70)
            {
                string now = game.smokeTestFish();
                if (now.Substring(0, 4) != (last.Length >= 4 ? last.Substring(0, 4) : "")) Debug.Log("[AutoShot] fishing: " + now);
                last = now;
                if (!shotWait && now.StartsWith("wait"))
                {
                    shotWait = true;
                    yield return Wait(1.2f);
                    yield return Shot("7-fish-2-waiting.png");
                }
                if (!shotReel && now.StartsWith("reel"))
                {
                    shotReel = true;
                    yield return Wait(0.8f);
                    game.smokeTestFish();
                    yield return Shot("7-fish-3-reeling.png");
                }
                if (now.Contains("caught 2")) break;
                yield return Wait(0.1f);
            }
            Debug.Log("[AutoShot] fishing ended: " + game.smokeTestFish());
            game.smokeTestLook(Math.PI - 0.9, -0.8);
            yield return Wait(1.5f);
            yield return Shot("7-fish-4-bucket.png");
            game.smokeTestStand();
            yield return Wait(1);
            Debug.Log("[AutoShot] stood up: " + game.smokeTestWhere());
            // the net, underwater
            game.smokeTestGo("dive");
            yield return Wait(3);
            Debug.Log("[AutoShot] " + game.smokeTestNet());
            yield return Wait(1.2f);
            yield return Shot("7-net-in-hand.png");
            for (int i = 0; i < 4; i++) Debug.Log("[AutoShot] " + game.smokeTestNet());
            Debug.Log("[AutoShot] " + game.smokeTestGold());
            yield return Wait(0.5f);
            Debug.Log("[AutoShot] after: " + game.smokeTestGold());
            game.smokeTestGo("out");
            yield return Wait(3);
            // the aquarium with what was caught, then with everything
            game.smokeTestAquarium(false);
            yield return Wait(2.5f);
            yield return Shot("7-aquarium-some.png");
            game.smokeTestAquarium(true);
            yield return Wait(3);
            yield return Shot("7-aquarium-full.png");
            // close up, with nothing in hand: straight on and from a corner
            game.smokeTestAquariumClose(1.45);
            yield return Wait(1);
            yield return Shot("7-aquarium-close.png");
            game.smokeTestAquariumClose(1.5, 0.6);
            yield return Wait(1);
            yield return Shot("7-aquarium-corner.png");
            game.smokeTestAquariumClose(null);
            game.smokeTestAquarium(true);
            yield return Wait(0.5f);
            game.smokeTestOpen("camp:aquarium");
            yield return Wait(1.5f);
            yield return Shot("7-aquarium-page.png");
            game.smokeTestClose("camp:aquarium");
            yield return Wait(0.5f);
            game.smokeTestOpen("camp:upgrades");
            yield return Wait(1.5f);
            yield return Shot("7-shop.png");
            game.smokeTestClose("camp:upgrades");
            yield return Wait(0.5f);
        }

        // -autoshot-settings: the settings screen, then the same view at the narrowest, the usual and the widest field of view
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-settings") >= 0)
        {
            game.smokeTestSettings(true);
            yield return Wait(1.5f);
            yield return Shot("6-settings.png");
            game.smokeTestSettings(false);
            Debug.Log("[AutoShot] invert look: " + game.smokeTestInvert());
            foreach (var fov in new[] { 40.0, 50.0, 70.0 })
            {
                game.smokeTestFov(fov);
                yield return Wait(1.5f);
                yield return Shot($"6-fov-{fov:0}.png");
            }
            game.smokeTestFov(50);
        }

        // -autoshot-photo-alt: the camera under the sea and in the room under the dunes
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-photo-alt") >= 0)
        {
            foreach (var place in new[] { "dive", "underground" })
            {
                game.smokeTestGo(place);
                yield return Wait(3);
                yield return Shot($"5-{place}-1-arrived.png");
                game.smokeTestAltCamera(true);
                yield return Wait(0.3f);
                game.smokeTestLowerCamera();
                yield return Wait(1.2f);
                yield return Shot($"5-{place}-2-camera-in-hand.png");
                game.smokeTestAltCamera(false);
                game.smokeTestAltCamera(true);
                yield return Wait(1.2f);
                yield return Shot($"5-{place}-3-viewfinder.png");
                int had = game.smokeTestPhotoCount;
                game.smokeTestTakePhoto();
                yield return Wait(1.2f);
                Debug.Log($"[AutoShot] {place}: photos {had} -> {game.smokeTestPhotoCount}, copied to {game.smokeTestExportPhoto() ?? "(failed)"}");
                // under the dunes the camera only comes down from the eye (it is never put away); underground the picture
                // shows it lowered, with the hint along the bottom
                if (place == "underground") game.smokeTestLowerCamera();
                else game.smokeTestAltCamera(false);
                yield return Wait(1);
                yield return Shot($"5-{place}-4-put-away.png");
                if (place == "underground") Debug.Log("[AutoShot] underground hint with the camera lowered: " + game.smokeTestHint() + (game.smokeTestHint().Contains("puts it away") ? " FAILED" : " ok"));
                if (place == "underground")
                {
                    // the hint never offers to put the camera away down here, and the ladder stops below the shaft's rim
                    yield return Wait(0.5f);
                    game.smokeTestLadderTop();
                    yield return Wait(1.5f);
                    string top = game.smokeTestLadderTop();
                    Debug.Log("[AutoShot] underground ladder top: " + top + (top.Contains("puts it away") ? " FAILED (put-away hint)" : "") + (top.Contains("Climb out") ? "" : " FAILED (no way out)"));
                    yield return Shot("5-underground-5-ladder-top.png");
                }
                game.smokeTestGo("out");
                yield return Wait(3);
                Debug.Log($"[AutoShot] back from {place}: {game.smokeTestWhere()}");
            }
            yield return Shot("5-back-on-the-beach.png");
        }

        // three.js yaw: 0 looks toward -z (the camp), PI toward +z (the sea)
        game.smokeTestLook(Math.PI, -0.05);
        yield return Wait(3);
        yield return Measure("sea");
        yield return Shot("3-sea.png");

        Quit(0);
    }

    /// <summary>What this run is on and with which graphics settings, so a log line can be read without knowing the machine.</summary>
    private static string Context()
    {
        var a = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        string urp = a == null ? "no URP asset"
            : $"render scale {a.renderScale:0.00}, MSAA {a.msaaSampleCount}x, shadows {a.mainLightShadowmapResolution} px / {a.shadowCascadeCount} cascade(s) / {a.shadowDistance:0} m, soft {NativeRender.UrpSettings.SoftShadowQualityName()}";
        var g = GameHost.Instance?.game;
        return $"{Platform.Describe()}; quality {g?.settings?.quality}; {urp}; UI ratio {Page.PixelRatio:0.000}, OS ratio {Page.OsRatio:0.00}; " +
               $"scripting {(IsIl2Cpp() ? "IL2CPP" : "Mono")}, development build {Debug.isDebugBuild}";
    }

    private static bool IsIl2Cpp()
    {
#if ENABLE_IL2CPP
        return true;
#else
        return false;
#endif
    }

    private static IEnumerator Wait(float seconds)
    {
        float end = Now + seconds;
        while (Now < end) yield return null;
    }

    private static IEnumerator Measure(string label)
    {
        int frames = 0, timed = 0;
        float start = Now, worst = 0;
        double mainMs = 0, renderMs = 0, gpuMs = 0;
        var ft = new FrameTiming[1];
        NativeRender.FrameStats.Reset();
        // the memory the frame threw away (the garbage collector's work later), and how uneven the frames were: a frame
        // longer than a screen refresh misses it, which with VSync on costs a whole refresh
        var gcRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
        double gcSum = 0;
        bool gcValid = false;
        int gc0 = GC.CollectionCount(0);
        long allocLast = NativeRender.FrameStats.Allocated(), allocBytes = 0;
        var times = new System.Collections.Generic.List<float>(4096);
        double refreshMs = 1000.0 / Math.Max(30, Screen.currentResolution.refreshRateRatio.value);
        while (Now - start < 3)
        {
            yield return null;
            frames++;
            long allocNow = NativeRender.FrameStats.Allocated();
            if (allocNow > allocLast) allocBytes += allocNow - allocLast;
            allocLast = allocNow;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            times.Add(Time.unscaledDeltaTime * 1000f);
            gcValid |= gcRec.Valid && gcRec.Count > 0;
            gcSum += gcRec.LastValue;
            // where Unity's frame went: the main thread, the render thread, the GPU (needs Frame Timing Stats, see ProjectSetup)
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
            {
                timed++;
                mainMs += ft[0].cpuMainThreadFrameTime - ft[0].cpuMainThreadPresentWaitTime;
                renderMs += ft[0].cpuRenderThreadFrameTime;
                gpuMs += ft[0].gpuFrameTime;
            }
        }
        float secs = Now - start;
        int n = Math.Max(1, timed);
        Debug.Log($"[AutoShot] {label}: {frames / secs:0.0} fps average, slowest frame {worst * 1000:0} ms; " +
                  $"Unity main thread {mainMs / n:0.00} ms, render thread {renderMs / n:0.00} ms, GPU {gpuMs / n:0.00} ms; " +
                  NativeRender.FrameStats.Report());
        Debug.Log($"[AutoShot] {label} slowest parts (ms/frame): {NativeRender.FrameStats.SectionReport()}");
        // the garbage the main thread made, by .NET's own count (in any build), and which parts made it
        Debug.Log($"[AutoShot] {label} garbage: {(allocBytes > 0 ? $"{allocBytes / Math.Max(1, frames)} bytes/frame on the main thread" : "not counted by this runtime")}; by part (bytes/frame): {NativeRender.FrameStats.GarbageReport()}");
        times.Sort();
        int fr = Math.Max(1, frames);
        float P(double q) => times.Count > 0 ? times[Math.Min(times.Count - 1, (int)(q * times.Count))] : 0;
        int late = 0;
        foreach (var t in times) if (t > refreshMs * 1.5) late++;
        Debug.Log($"[AutoShot] {label} pacing: frame ms median {P(0.5):0.00}, 90% {P(0.9):0.00}, 99% {P(0.99):0.00}; {late} of {times.Count} frames took over 1.5 refreshes ({refreshMs:0.00} ms); " +
                  // Unity records the garbage a frame makes only in development players: in a release build the collection count is the signal
                  $"garbage {(gcValid ? $"{gcSum / fr / 1024:0.0} KB/frame" : "not recorded in this build")}, {GC.CollectionCount(0) - gc0} collections; vsync {QualitySettings.vSyncCount}, target fps {Application.targetFrameRate}");
        gcRec.Dispose();
    }

    /// <summary>
    /// The frame drawn right after a reload is asked for stays on screen while the new world is built (about a second):
    /// the old world must still look itself in it, not magenta (materials destroyed before the old scene went).
    /// </summary>
    private IEnumerator ReloadFrameCheck()
    {
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        var px = tex.GetPixels32();
        int pink = 0;
        foreach (var c in px) if (c.r > 200 && c.g < 60 && c.b > 200) pink++;
        Destroy(tex);
        double share = px.Length > 0 ? 100.0 * pink / px.Length : 0;
        Debug.Log($"[AutoShot] reload frame: {pink} magenta pixels ({share:0.00}%) " + (share < 0.5 ? "ok" : "FAILED"));
    }

    private IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var path = Path.Combine(dir, name);
        ScreenCapture.CaptureScreenshot(path);
        if (dumpScenes)
        {
            try { SceneDump.Write(Path.ChangeExtension(path, ".scene.json")); }
            catch (Exception e) { Debug.LogWarning($"[AutoShot] scene dump for {name} failed: {e.Message}"); }
        }
        yield return null;
        yield return null;
        Debug.Log($"[AutoShot] saved {path}");
    }

    private static void Quit(int code)
    {
        Debug.Log("[AutoShot] done");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(code);
#endif
    }
}
