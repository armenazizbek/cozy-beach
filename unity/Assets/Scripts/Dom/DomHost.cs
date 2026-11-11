using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

// DomHost: sets up UIDocument, PanelSettings, PanelTextSettings, Style.uss, index.html DOM tree, and per-frame Tick().

public static class DomHost
{
    private static GameObject _hostGo;
    private static UIDocument _uiDocument;
    private static PanelSettings _panelSettings;
    private static PanelTextSettings _textSettings;

    private static int _lastScreenW = -1;
    private static int _lastScreenH = -1;
    private static float _lastDpi = -1f;
    private static bool _wasPointerLocked = false;
    internal static bool _isTyping = false;
    private static float _mouseMovedAt = -10f;

    /// <summary>
    /// True just after the mouse really moved. A button under a resting pointer must not take the highlight back from the
    /// keyboard or a controller every time a menu redraws itself.
    /// </summary>
    public static bool MouseActive => Time.unscaledTime - _mouseMovedAt < 0.3f;

    public static UIDocument UIDocument => _uiDocument;

    public static void Ensure()
    {
        if (_uiDocument != null && _hostGo != null && document.body != null && document.getElementById("ui") != null)
        {
            return;
        }

        // Clean up any stale state from a previous scene load
        window.ClearAllTimersAndListeners();
        document.ClearListeners();
        document.activeElement = null;
        HTMLElement.ElementByVe.Clear();
        _isTyping = false;

        RefreshMetricsInternal(out float scale);

        _hostGo = new GameObject("DomHost");
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.name = "DomPanelSettings";
        _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
        _panelSettings.scale = scale;
        _panelSettings.sortingOrder = 100;
        // Unity's default runtime theme styles the built-in controls the DOM layer uses (ScrollView scrollers,
        // TextField caret and selection); without one UI Toolkit warns that the UI will not render properly.
        var theme = Resources.Load<ThemeStyleSheet>("UI/CozyRuntimeTheme");
        if (theme != null) _panelSettings.themeStyleSheet = theme;

        _textSettings = BuildTextSettings();
        if (_textSettings != null)
        {
            _panelSettings.textSettings = _textSettings;
        }

        _uiDocument = _hostGo.AddComponent<UIDocument>();
        _uiDocument.panelSettings = _panelSettings;
        _uiDocument.sortingOrder = 100;

        VisualElement rootVe = _uiDocument.rootVisualElement;
        if (rootVe != null)
        {
            rootVe.style.width = Length.Percent(100);
            rootVe.style.height = Length.Percent(100);
            rootVe.pickingMode = PickingMode.Ignore;
            // UI Toolkit moves focus by itself on controller and keyboard navigation (stick, D-pad, Tab, Submit). The game
            // does its own, as in the browser (Game.padNav, padButton), so the panel's built-in navigation is switched off:
            // with both running, the highlighted button and the one A pressed drifted apart.
            EventCallback<NavigationMoveEvent> noMove = e => { e.StopPropagation(); rootVe.focusController?.IgnoreEvent(e); };
            EventCallback<NavigationSubmitEvent> noSubmit = e => { e.StopPropagation(); rootVe.focusController?.IgnoreEvent(e); };
            EventCallback<NavigationCancelEvent> noCancel = e => { e.StopPropagation(); rootVe.focusController?.IgnoreEvent(e); };
            rootVe.RegisterCallback(noMove, TrickleDown.TrickleDown);
            rootVe.RegisterCallback(noSubmit, TrickleDown.TrickleDown);
            rootVe.RegisterCallback(noCancel, TrickleDown.TrickleDown);
            // the theme brings Unity's own default font; keep the game's font for the whole page
            if (_textSettings != null && _textSettings.defaultFontAsset != null)
                rootVe.style.unityFontDefinition = FontDefinition.FromSDFFont(_textSettings.defaultFontAsset);

            StyleSheet styleSheet = Resources.Load<StyleSheet>("UI/Style");
            if (styleSheet != null)
            {
                rootVe.styleSheets.Add(styleSheet);
            }
        }

        BuildIndexHtmlTree(rootVe);

        _lastScreenW = Screen.width;
        _lastScreenH = Screen.height;
        _lastDpi = Screen.dpi;
        _wasPointerLocked = document.pointerLocked;
    }

    private static PanelTextSettings BuildTextSettings()
    {
        try
        {
            var pts = ScriptableObject.CreateInstance<PanelTextSettings>();
            pts.name = "DomPanelTextSettings";

            // Body text: a friendly sans. Headings and the title: a bold serif (see DisplayFont). A font file dropped into
            // Resources/UI/Fonts (Body, BodyBold, Display) is used ahead of the system's, which is what a console will need.
            FontAsset mainFont = LoadFontAsset("UI/Fonts/Body")
                              ?? CreateOsFontAsset("Trebuchet MS", "Regular")
                              ?? CreateOsFontAsset("Segoe UI", "Regular")
                              ?? CreateOsFontAsset("Arial", "Regular");
            FontAsset boldFont = LoadFontAsset("UI/Fonts/BodyBold")
                              ?? CreateOsFontAsset("Trebuchet MS", "Bold")
                              ?? CreateOsFontAsset("Segoe UI", "Bold");
            DisplayFont = LoadFontAsset("UI/Fonts/Display")
                       ?? CreateOsFontAsset("Georgia", "Bold")
                       ?? boldFont;

            if (mainFont != null)
            {
                var fallbacks = new List<FontAsset>();
                // Segoe UI for general glyphs, Segoe UI Symbol for plain symbols such as ✓ ▾ ◆
                foreach (var name in new[] { "Segoe UI", "Segoe UI Symbol" })
                {
                    FontAsset fb = CreateOsFontAsset(name, "Regular");
                    if (fb != null && fb != mainFont) fallbacks.Add(fb);
                }
                foreach (var f in new[] { mainFont, boldFont, DisplayFont })
                {
                    if (f == null) continue;
                    if (f.fallbackFontAssetTable == null) f.fallbackFontAssetTable = new List<FontAsset>();
                    foreach (var fb in fallbacks)
                        if (fb != f && !f.fallbackFontAssetTable.Contains(fb)) f.fallbackFontAssetTable.Add(fb);
                }
                // bold text is drawn with the real bold face, not a thickened regular one
                try
                {
                    if (boldFont != null && boldFont != mainFont && mainFont.fontWeightTable != null && mainFont.fontWeightTable.Length > 7)
                        mainFont.fontWeightTable[7].regularTypeface = boldFont;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"DomHost: could not set the bold face: {ex.Message}");
                }

                pts.defaultFontAsset = mainFont;
            }

            // Colour emoji: Unity's text engine cannot draw colour emoji fonts (Segoe UI Emoji came out as black outlines or
            // empty boxes), so emoji come from a sprite atlas of Microsoft's Fluent Emoji, the same design Windows shows in the
            // browser build. See Assets/Editor/EmojiSpriteAssetBuilder.cs and unity/Tools/Emoji.
            var emoji = Resources.Load<SpriteAsset>("UI/Emoji");
            EmojiText.Use(emoji);
            if (emoji != null)
            {
                pts.enableEmojiSupport = true;
                pts.emojiFallbackTextAssets = new List<UnityEngine.TextCore.Text.TextAsset> { emoji };
                pts.defaultSpriteAsset = emoji;
            }
            else
            {
                Debug.LogWarning("DomHost: Resources/UI/Emoji is missing; emoji will not draw in colour.");
            }

            return pts;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"DomHost: Could not create OS FontAssets: {ex.Message}");
            return null;
        }
    }

    /// <summary>The heading face (card titles, the game's name). Null until the text settings are built.</summary>
    public static FontAsset DisplayFont { get; private set; }

    private static FontAsset LoadFontAsset(string resource)
    {
        var font = Resources.Load<Font>(resource);
        if (font == null) return null;
        try
        {
            var fa = FontAsset.CreateFontAsset(font);
            if (fa != null) fa.name = font.name;
            return fa;
        }
        catch
        {
            return null;
        }
    }

    private static FontAsset CreateOsFontAsset(string familyName, string styleName)
    {
        try
        {
            // Unity 6 API: FontAsset.CreateFontAsset(string familyName, string styleName, int pointSize = 90)
            FontAsset fa = FontAsset.CreateFontAsset(familyName, styleName);
            if (fa != null)
            {
                fa.name = familyName + "-" + styleName;
                fa.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
                return fa;
            }
        }
        catch
        {
            // Fallback below
        }

        try
        {
            Font osFont = Font.CreateDynamicFontFromOSFont(familyName, 18);
            if (osFont != null)
            {
                FontAsset fa = FontAsset.CreateFontAsset(osFont);
                if (fa != null)
                {
                    fa.name = familyName;
                    return fa;
                }
            }
        }
        catch
        {
            // Ignore if font is unavailable on current OS
        }

        return null;
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

        // <div id="boot">
        //   <h1>Cozy Treasure Hunter</h1>
        //   <p>Getting the beach ready…</p>
        //   <div id="boot-track"><div id="boot-bar"></div></div>
        // </div>
        var bootEl = new HTMLDivElement();
        bootEl.id = "boot";
        var bootH1 = new HTMLHeadingElement("H1") { textContent = "Cozy Treasure Hunter" };
        var bootP = new HTMLParagraphElement { textContent = "Getting the beach ready…" };
        var bootTrack = new HTMLDivElement { id = "boot-track" };
        var bootBar = new HTMLDivElement { id = "boot-bar" };
        bootTrack.appendChild(bootBar);
        bootEl.append(bootH1, bootP, bootTrack);

        bodyEl.append(sceneEl, uiEl, bootEl);
    }

    internal static void RefreshMetricsIfNeeded()
    {
        if (Screen.width != _lastScreenW || Screen.height != _lastScreenH || Mathf.Abs(Screen.dpi - _lastDpi) > 0.1f)
        {
            RefreshMetricsInternal(out float scale);
            if (_panelSettings != null)
            {
                _panelSettings.scale = scale;
            }
        }
    }

    /// <summary>The page the UI is laid out on. The screen shows at least this much, more along its longer side.</summary>
    public const double PAGE_WIDTH = 1920, PAGE_HEIGHT = 1080;

    private static void RefreshMetricsInternal(out float scale)
    {
        float dpi = Screen.dpi;
        double os = (dpi > 0f && !float.IsNaN(dpi)) ? (dpi / 96.0) : 1.0;
        if (os < 0.5) os = 1.0;

        int sw = Screen.width > 0 ? Screen.width : 1280;
        int sh = Screen.height > 0 ? Screen.height : 720;

        // A game's menus and HUD take up the same share of the screen at every resolution. The browser page was a fixed size
        // in pixels instead, so on a 1440p or 4K screen everything shrank to a corner. The page is 1920 x 1080 and is scaled
        // to fit: a wider or taller screen sees more page, never less.
        double dpr = Math.Min(sw / PAGE_WIDTH, sh / PAGE_HEIGHT);
        if (dpr < 0.3) dpr = 0.3;
        scale = (float)dpr;

        window._osPixelRatio = os;
        window._devicePixelRatio = dpr;
        window._innerWidth = Math.Max(1.0, Math.Round(sw / dpr));
        window._innerHeight = Math.Max(1.0, Math.Round(sh / dpr));

        if (_uiDocument != null && _uiDocument.rootVisualElement != null)
        {
            Rect r = _uiDocument.rootVisualElement.worldBound;
            if (!float.IsNaN(r.width) && !float.IsNaN(r.height) && r.width > 1f && r.height > 1f)
            {
                window._innerWidth = r.width;
                window._innerHeight = r.height;
            }
        }
    }

    public static void Tick()
    {
        if (_uiDocument == null) return;

        // 1. Check screen resize / DPI change
        bool sizeChanged = Screen.width != _lastScreenW || Screen.height != _lastScreenH || Mathf.Abs(Screen.dpi - _lastDpi) > 0.1f;
        RefreshMetricsInternal(out float scale);
        if (_panelSettings != null && Mathf.Abs(_panelSettings.scale - scale) > 0.001f)
        {
            _panelSettings.scale = scale;
        }
        if (sizeChanged)
        {
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            _lastDpi = Screen.dpi;
            window.DispatchEvent(new Event("resize"));
        }

        // 2. Check pointer lock state change
        bool nowLocked = document.pointerLocked;
        if (nowLocked != _wasPointerLocked)
        {
            _wasPointerLocked = nowLocked;
            document.DispatchEvent(new Event("pointerlockchange"));
        }

        var mouseNow = Mouse.current;
        if (mouseNow != null && mouseNow.delta.ReadValue().sqrMagnitude > 0.5f) _mouseMovedAt = Time.unscaledTime;

        // 3. Run scheduled timers (setTimeout, setInterval, requestAnimationFrame)
        window.RunTimers();

        // 4. Re-upload dirty HTMLCanvasElement textures attached to the UI
        if (document.body != null)
        {
            SyncConnectedCanvases(document.body);
        }
    }

    private static void SyncConnectedCanvases(HTMLElement root)
    {
        if (root is HTMLCanvasElement canvas && canvas.id != "scene")
        {
            canvas.SyncUITexture();
        }
        var ch = root.children;
        for (int i = 0; i < ch.Count; i++)
        {
            SyncConnectedCanvases(ch[i]);
        }
    }

    public static bool IsPointerOverUI()
    {
        if (_uiDocument == null || _uiDocument.rootVisualElement == null) return false;
        if (document.pointerLocked) return false;

        var panel = _uiDocument.rootVisualElement.panel;
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
