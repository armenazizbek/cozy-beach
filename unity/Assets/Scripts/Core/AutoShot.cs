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
