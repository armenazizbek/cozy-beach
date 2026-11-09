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
        THREE.Scene.ResetLayers();
        DomHost.Ensure();
        AutoShot.Trace("page ready");

        // let the loading screen reach the screen before the heavy work of building the world begins
        yield return null;
        yield return null;

        var canvas = document.getElementById("scene");
        var ui = document.getElementById("ui");
        game = new Game(canvas, ui);
        AutoShot.Trace("world built");
        yield return StartCoroutine(game.startCoroutine());
        AutoShot.Trace("ready to play");
    }

    private void Update()
    {
        game?.input?.poll();
        DomHost.Tick();
        THREE.WebGLRenderer.Main?.Tick();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Rebuilds the world from scratch (like location.reload() in the browser for Load Game / New Game / Main Menu).</summary>
    public static void Reload()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // a page reload wipes everything; here the old game has to be stopped and forgotten by hand, or it keeps running
        // its frame and timers inside the new scene (Load Game froze or closed the game)
        if (Instance != null) Instance.game = null;
        window.ClearAllTimersAndListeners();
        THREE.WebGLRenderer.Teardown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
