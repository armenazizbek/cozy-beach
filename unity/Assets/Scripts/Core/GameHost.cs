using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Entry point (port of src/main.ts): sets up the UI document, lets the loading screen draw first, builds Game,
// and drives input, UI timers and the Three.js renderer each frame.

public class GameHost : MonoBehaviour
{
    public static GameHost Instance { get; private set; }
    public Game game { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // leave a trace of who asked the game to close, so an unexpected quit can be found in the player log
        Application.wantsToQuit += () =>
        {
            Debug.Log("Quit requested:\n" + System.Environment.StackTrace);
            return true;
        };
        // nothing in the game uses Unity's physics (the world is moved by the game's own code), so it is not stepped
        Physics.simulationMode = SimulationMode.Script;
        EnsureHost();
        // Main.unity holds no objects, so every scene load needs its host made again: Load Game, New Game and Main Menu all
        // reload the scene, and without this they left an empty scene (the last picture frozen on screen)
        SceneManager.sceneLoaded += (scene, mode) => EnsureHost();
    }

    private static void EnsureHost()
    {
        if (FindAnyObjectByType<GameHost>() == null)
        {
            var go = new GameObject("GameHost");
            go.AddComponent<GameHost>();
        }
    }

    private IEnumerator Start()
    {
        Instance = this;
        AutoShot.Trace("host start");
        // the old scene is gone now: what the last reload forgot can be destroyed (GameRenderer.Teardown)
        NativeRender.MaterialCache.DestroyRetired();
        NativeRender.TextureCache.DestroyRetired();
        THREE.Scene.ResetLayers();
        // Steam first: Deck detection and achievements already earned need it before the game is built
        SteamService.Init();
        SteamBridge.unlockHandler = SteamService.UnlockAchievement;
        SteamService.overlayChanged = open => { if (open) game?.onFocusLost(); };
        Page.Ensure();
        AutoShot.Trace("page ready");

        // let the loading screen reach the screen before the heavy work of building the world begins
        yield return null;
        yield return null;

        var canvas = Page.Scene;
        var ui = Page.Ui;
        // the world is built a piece at a time, so the loading screen keeps moving; the game is handed over once it is whole
        var pieces = Prebuilt.MakeAll();
        while (pieces.MoveNext())
        {
            LoadingScreen.Progress(Prebuilt.Progress * 0.4);
            yield return pieces.Current;
        }
        AutoShot.Trace("big pieces made");
        // -autoshot-seed: the rest of the world starts from a known point of the sequence, however many frames loading took
        if (JS.TestSeed is int seed) JS.Seed(seed + 1);
        var built = new Game(canvas, ui);
        yield return StartCoroutine(Stepped(built.build(), 0.4, 0.7, 8));
        game = built;
        AutoShot.Trace("world built");
        yield return StartCoroutine(game.startCoroutine());
        AutoShot.Trace("ready to play");
    }

    /// <summary>Runs one stage of the loading and moves the loading bar from one point to the next as its steps go by.</summary>
    private static IEnumerator Stepped(IEnumerator stage, double from, double to, int steps)
    {
        int n = 0;
        while (true)
        {
            float t0 = Time.realtimeSinceStartup;
            if (!stage.MoveNext()) break;
            n++;
            AutoShot.Trace($"loading step {from:0.00}+{n}: {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
            LoadingScreen.Progress(from + (to - from) * Math.Min(1.0, (double)n / steps));
            yield return stage.Current;
        }
    }

    private void Update()
    {
        MainThread.Pump();
        SteamService.Tick();
        game?.input?.poll();
        Page.Tick();
        // the game's frame runs once it has started, and only with its own renderer (a reloading game's old one must not
        // run inside the new scene)
        var main = NativeRender.GameRenderer.Main;
        main?.Tick(game != null && game.running && ReferenceEquals(game.renderer, main) ? game.frame : (System.Action)null);
        // native renderers follow a change of quality preset (Native/Draw.cs)
        ShadowPolicy.Tick();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) GamepadInput.StopAllRumble();
    }

    private void OnApplicationFocus(bool focus)
    {
        // a repeatable smoke run (-autoshot-fixedstep) must not pause because the window lost focus
        if (!focus && !TestClock.Fixed) game?.onFocusLost();
    }

    private void OnApplicationQuit()
    {
        GamepadInput.StopAllRumble();
        game?.onQuit();
        SteamService.Shutdown();
    }

    private void OnDestroy()
    {
        GamepadInput.StopAllRumble();
        if (Instance == this) Instance = null;
    }

    /// <summary>Rebuilds the world from scratch (like location.reload() in the browser for Load Game / New Game / Main Menu).</summary>
    public static void Reload()
    {
        GamepadInput.StopAllRumble();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // a page reload wipes everything; here the old game has to be stopped and forgotten by hand, or it keeps running
        // its frame and timers inside the new scene (Load Game froze or closed the game)
        if (Instance != null) Instance.game = null;
        Page.Clear();
        NativeRender.GameRenderer.Teardown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
