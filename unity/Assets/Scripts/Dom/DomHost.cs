using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

// DomHost: sets up UIDocument, PanelSettings, PanelTextSettings, Style.uss, index.html DOM tree, and per-frame Tick().

public static class DomHost
{
    private static bool _wasPointerLocked = false;
    internal static bool _isTyping = false;

    /// <summary>
    /// True just after the mouse really moved. A button under a resting pointer must not take the highlight back from the
    /// keyboard or a controller every time a menu redraws itself.
    /// </summary>
    public static bool MouseActive => UiRoot.MouseActive;

    public static UIDocument UIDocument => UiRoot.Document;

    /// <summary>The page the UI is laid out on (UiRoot).</summary>
    public const double PAGE_WIDTH = UiRoot.PAGE_WIDTH, PAGE_HEIGHT = UiRoot.PAGE_HEIGHT;

    /// <summary>The "UI size" setting (UiRoot.UiScale).</summary>
    public static double UiScale { get => UiRoot.UiScale; set => UiRoot.UiScale = value; }

    /// <summary>The heading face (UiRoot).</summary>
    public static FontAsset DisplayFont => UiRoot.DisplayFont;

    /// <summary>The face of the game's name (UiRoot).</summary>
    public static FontAsset TitleFont => UiRoot.TitleFont;

    /// <summary>The page's metrics as the browser's window reports them.</summary>
    private static void CopyMetrics()
    {
        window._osPixelRatio = UiRoot.OsRatio;
        window._devicePixelRatio = UiRoot.Scale;
        window._innerWidth = UiRoot.PageWidth;
        window._innerHeight = UiRoot.PageHeight;
    }

    public static void Ensure()
    {
        if (UiRoot.Ready && document.body != null && document.getElementById("ui") != null)
        {
            return;
        }

        // Clean up any stale state from a previous scene load
        window.ClearAllTimersAndListeners();
        document.ClearListeners();
        document.activeElement = null;
        HTMLElement.ElementByVe.Clear();
        CssAnimations.Clear();
        HTMLCanvasElement.OnPage.Clear();
        UiCanvas.Clear();
        _isTyping = false;

        UiRoot.Ensure();
        CopyMetrics();
        VisualElement rootVe = UiRoot.Root;

        BuildIndexHtmlTree(rootVe);

        _wasPointerLocked = document.pointerLocked;
    }

    private static void BuildIndexHtmlTree(VisualElement rootVe)
    {
        var htmlEl = new HTMLElement("HTML");
        var bodyEl = new HTMLElement("BODY");
        document.documentElement = htmlEl;
        document.body = bodyEl;
        htmlEl.appendChild(bodyEl);

        if (rootVe != null)
        {
            rootVe.Clear();
            rootVe.Add(htmlEl.ve);
        }

        // <canvas id="scene"></canvas>
        var sceneEl = new HTMLCanvasElement(Math.Max(1, (int)window._innerWidth), Math.Max(1, (int)window._innerHeight));
        sceneEl.id = "scene";
        if (sceneEl.ve != null)
        {
            sceneEl.ve.pickingMode = PickingMode.Ignore;
        }

        // <div id="ui"></div>
        var uiEl = new HTMLDivElement();
        uiEl.id = "ui";
        if (uiEl.ve != null)
        {
            uiEl.ve.pickingMode = PickingMode.Ignore;
        }

        // the loading screen is native (UI/Native/LoadingScreen.cs), on top of the page
        bodyEl.append(sceneEl, uiEl);
        bodyEl.ve?.Add(LoadingScreen.Build());
    }

    internal static void RefreshMetricsIfNeeded()
    {
        UiRoot.RefreshIfNeeded();
        CopyMetrics();
    }

    public static void Tick()
    {
        if (UiRoot.Document == null) return;

        // 1. Check screen resize / DPI change
        bool sizeChanged = UiRoot.Tick();
        CopyMetrics();
        if (sizeChanged) window.DispatchEvent(new Event("resize"));

        // 2. Check pointer lock state change
        bool nowLocked = document.pointerLocked;
        if (nowLocked != _wasPointerLocked)
        {
            _wasPointerLocked = nowLocked;
            document.DispatchEvent(new Event("pointerlockchange"));
        }

        // 3. Run scheduled timers (setTimeout, setInterval, requestAnimationFrame)
        window.RunTimers();

        // 4. Re-upload dirty HTMLCanvasElement textures attached to the UI (only the canvases on the page are looked at)
        foreach (var canvas in HTMLCanvasElement.OnPage)
        {
            if (canvas.id != "scene") canvas.SyncUITexture();
        }
        // and the native ones (UI/Native/UiCanvas.cs)
        UiCanvas.SyncAll();
    }

    public static bool IsPointerOverUI()
    {
        if (UiRoot.Root == null) return false;
        if (document.pointerLocked) return false;

        var panel = UiRoot.Root.panel;
        if (panel == null) return false;

        var mouse = Mouse.current;
        if (mouse == null) return false;

        Vector2 screenPos = mouse.position.ReadValue();
        // Unity screen coordinates have (0,0) at bottom-left; RuntimePanelUtils.ScreenToPanel expects top-left Y
        Vector2 topLeftScreenPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, topLeftScreenPos);

        VisualElement picked = panel.Pick(panelPos);
        while (picked != null)
        {
            // a native screen (UI/Native) is UI as a whole
            if (picked.ClassListContains(LoadingScreen.NATIVE)) return true;
            if (HTMLElement.ElementByVe.TryGetValue(picked, out HTMLElement el))
            {
                if (el != document.documentElement &&
                    el != document.body &&
                    el.id != "ui" &&
                    el.id != "scene")
                {
                    return true;
                }
                return false;
            }
            picked = picked.parent;
        }
        return false;
    }

    public static bool IsTyping()
    {
        if (_isTyping) return true;
        if (document.activeElement is HTMLInputElement inp &&
            string.Equals(inp.type, "text", StringComparison.OrdinalIgnoreCase) &&
            inp.isConnected &&
            !inp.disabled)
        {
            return true;
        }
        return false;
    }
}
