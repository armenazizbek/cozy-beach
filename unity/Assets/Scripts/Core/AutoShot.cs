using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Smoke test for builds, off unless asked for:
///   CozyTreasureHunter.exe -autoshot "C:\some\folder" -screen-width 1280 -screen-height 720 -screen-fullscreen 0
/// It waits for the loading screen, saves 1-title.png and 1b-achievements.png, starts a game as Begin would, saves 2-camp.png and 3-sea.png,
/// writes the frame rate to the player log and quits. Saves go to a "saves" folder inside the shot folder, so real saves are
/// never read or written.
/// </summary>
public class AutoShot : MonoBehaviour
{
    private string dir;

    /// <summary>True while the smoke test runs; start-up checkpoints are only logged then.</summary>
    internal static bool Active { get; private set; }

    internal static void Trace(string what)
    {
        if (Active) Debug.Log($"[AutoShot] {Time.realtimeSinceStartup:0.00}s {what}");
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

        var go = new GameObject("AutoShot");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoShot>().dir = dir;
    }

    private IEnumerator Start()
    {
        Debug.Log($"[AutoShot] writing to {dir}");
        float t0 = Time.realtimeSinceStartup;
        while ((GameHost.Instance == null || GameHost.Instance.game == null || !GameHost.Instance.game.loaded)
               && Time.realtimeSinceStartup - t0 < 180)
            yield return null;
        var game = GameHost.Instance != null ? GameHost.Instance.game : null;
        if (game == null || !game.loaded)
        {
            Debug.LogError("[AutoShot] the game did not finish loading within 180 s");
            yield return Shot("0-stuck.png");
            Quit(1);
            yield break;
        }
        Debug.Log($"[AutoShot] loaded after {Time.realtimeSinceStartup - t0:0.0} s");

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
            Debug.Log($"[AutoShot] reloaded with the save after {Time.realtimeSinceStartup - t0:0.0} s; load failed flag: {SaveGame.lastLoadFailed}");
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
                Debug.Log("[AutoShot] pad " + b + " -> focused: " + (document.activeElement?.textContent ?? "(nothing)"));
                Debug.Log("[AutoShot] pad sees " + game.smokeTestPadInfo());
            }
            Debug.Log("[AutoShot] pad sees " + game.smokeTestPadInfo());
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
            yield return Wait(0.8f);
            Debug.Log("[AutoShot] pad: settings open = " + (document.querySelector(".settings-card") != null));
            yield return Shot("1p-pad.png");
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.East);
            yield return Wait(0.5f);
            Debug.Log("[AutoShot] pad: settings closed by B = " + (document.querySelector(".settings-card") == null));
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }

        game.smokeTestAchievements();
        yield return Wait(1);
        yield return Shot("1b-achievements.png");
        document.querySelector(".achievements")?.remove();

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
            float fishT0 = Time.realtimeSinceStartup;
            string last = "";
            bool shotWait = false, shotReel = false;
            while (Time.realtimeSinceStartup - fishT0 < 70)
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
                game.smokeTestAltCamera(false);
                yield return Wait(1);
                yield return Shot($"5-{place}-4-put-away.png");
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

    private static IEnumerator Wait(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    private static IEnumerator Measure(string label)
    {
        int frames = 0, timed = 0;
        float start = Time.realtimeSinceStartup, worst = 0;
        double mainMs = 0, renderMs = 0, gpuMs = 0;
        var ft = new FrameTiming[1];
        THREE.Bridge.BridgeStats.Reset();
        while (Time.realtimeSinceStartup - start < 3)
        {
            yield return null;
            frames++;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
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
        float secs = Time.realtimeSinceStartup - start;
        int n = Math.Max(1, timed);
        Debug.Log($"[AutoShot] {label}: {frames / secs:0.0} fps average, slowest frame {worst * 1000:0} ms; " +
                  $"Unity main thread {mainMs / n:0.00} ms, render thread {renderMs / n:0.00} ms, GPU {gpuMs / n:0.00} ms; " +
                  THREE.Bridge.BridgeStats.Report());
        Debug.Log($"[AutoShot] {label} slowest parts (ms/frame): {THREE.Bridge.BridgeStats.SectionReport()}");
    }

    private IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var path = Path.Combine(dir, name);
        ScreenCapture.CaptureScreenshot(path);
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
