using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The UI's page: the root every screen hangs from, and the screen-wide state the UI needs.
/// <list type="bullet">
/// <item>the page's root (`.page` &gt; `#scene`, `#ui`, `#boot`) and its classes (`diving`, `menu-open`, `scope-on`,
/// ...) for the stylesheet's rules that depend on the game's state;</item>
/// <item>pointer lock and its change, the screen's resize, the page's size and pixel ratios (UiRoot), and the
/// once-a-frame tick (metrics, timers, canvases);</item>
/// <item>whether the pointer is over the UI and whether the player is typing.</item>
/// </list>
/// </summary>
public static class Page
{
    private static UiNode page, scene, ui;
    private static bool wasLocked;

    /// <summary>The page's root (its classes carry the game's state for the stylesheet).</summary>
    public static UiNode Body => page;

    /// <summary>The page's #ui, where every screen hangs.</summary>
    public static UiNode Ui => ui;

    /// <summary>The page's #scene (the 3D picture is drawn by the cameras behind it).</summary>
    public static UiNode Scene => scene;

    /// <summary>Set while a text box has the keyboard (UiText).</summary>
    internal static bool typing;

    /// <summary>Runs when the mouse is captured or let go.</summary>
    public static event Action OnPointerLockChange;

    /// <summary>Runs when the screen's size or dpi changes.</summary>
    public static event Action OnResize;

    /// <summary>Makes the page once (again after a scene reload).</summary>
    public static void Ensure()
    {
        if (UiRoot.Ready && page != null && page.ve.panel != null) return;
        GameTimers.Clear();
        OnPointerLockChange = null;
        OnResize = null;
        UiAnim.Clear();
        UiCanvas.Clear();
        MenuNav.ForgetNative();
        typing = false;

        UiRoot.Ensure();
        var root = UiRoot.Root;
        page = new UiNode("div", "page");
        if (root != null)
        {
            root.Clear();
            root.Add(page.ve);
        }
        // <canvas id="scene"></canvas> and <div id="ui"></div>
        scene = new UiNode("canvas");
        scene.ve.name = "scene";
        ui = new UiNode("div");
        ui.ve.name = "ui";
        page.Add(scene, ui);
        scene.ve.pickingMode = PickingMode.Ignore;
        ui.ve.pickingMode = PickingMode.Ignore;
        page.ve.pickingMode = PickingMode.Ignore;
        // the loading screen, on top of the page
        page.ve.Add(LoadingScreen.Build());
        wasLocked = PointerLocked;
    }

    // ------------------------------------------------------------------ metrics

    /// <summary>The page's width and height in its own pixels (window.innerWidth, innerHeight).</summary>
    public static double Width
    {
        get
        {
            UiRoot.RefreshIfNeeded();
            return UiRoot.PageWidth;
        }
    }

    public static double Height
    {
        get
        {
            UiRoot.RefreshIfNeeded();
            return UiRoot.PageHeight;
        }
    }

    /// <summary>The page's pixels per screen pixel (window.devicePixelRatio).</summary>
    public static double PixelRatio
    {
        get
        {
            UiRoot.RefreshIfNeeded();
            return UiRoot.Scale;
        }
    }

    /// <summary>The operating system's display scale.</summary>
    public static double OsRatio
    {
        get
        {
            UiRoot.RefreshIfNeeded();
            return UiRoot.OsRatio;
        }
    }

    // ------------------------------------------------------------------ pointer lock

    public static bool PointerLocked => UnityEngine.Cursor.lockState == CursorLockMode.Locked;

    /// <summary>Lets the mouse go (document.exitPointerLock()).</summary>
    public static void ExitPointerLock()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    /// <summary>Quits the game (window.close()).</summary>
    public static void Close() => Application.Quit();

    // ------------------------------------------------------------------ the frame

    /// <summary>Once a frame: the screen's size, the pointer lock's change, the timers and the canvases.</summary>
    public static void Tick()
    {
        if (UiRoot.Document == null) return;
        if (UiRoot.Tick()) OnResize?.Invoke();
        bool locked = PointerLocked;
        if (locked != wasLocked)
        {
            wasLocked = locked;
            OnPointerLockChange?.Invoke();
        }
        GameTimers.Run();
        UiCanvas.SyncAll();
    }

    /// <summary>Forgets the page's timers and listeners (a reload).</summary>
    public static void Clear()
    {
        GameTimers.Clear();
        OnPointerLockChange = null;
        OnResize = null;
    }

    // ------------------------------------------------------------------ the pointer and the keyboard

    /// <summary>True when the pointer is over a part of the UI that takes it (not the bare page around the screens).</summary>
    public static bool IsPointerOverUI()
    {
        var root = UiRoot.Root;
        if (root == null || PointerLocked) return false;
        var panel = root.panel;
        var mouse = Mouse.current;
        if (panel == null || mouse == null) return false;
        Vector2 screenPos = mouse.position.ReadValue();
        // Unity's screen has (0, 0) at the bottom left; the panel's is at the top left
        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
        for (var picked = panel.Pick(panelPos); picked != null; picked = picked.parent)
        {
            if (picked.ClassListContains(LoadingScreen.NATIVE)) return true;
            if (picked.userData is UiNode n && n.ve == picked)
                return n != page && n != ui && n != scene;
        }
        return false;
    }

    /// <summary>True while a text box has the keyboard.</summary>
    public static bool IsTyping() => typing;
}
