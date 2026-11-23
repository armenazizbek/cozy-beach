using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>
/// The UI's panel (unity/NATIVE_PLAN.md 2.7, S26): one UIDocument with the game's fonts, Style.uss and the 1920 x 1080 page
/// scaled to the screen (and the "UI size" setting). The DOM layer's page (DomHost) and the native screens both hang under
/// its root, so a screen can move over to native on its own and the two keep one look.
/// </summary>
public static class UiRoot
{
    private static GameObject hostGo;
    private static UIDocument doc;
    private static PanelSettings panel;
    private static PanelTextSettings text;
    private static int lastW = -1, lastH = -1;
    private static float lastDpi = -1f;
    private static float mouseMovedAt = -10f;

    /// <summary>The panel's document (null before <see cref="Ensure"/>).</summary>
    public static UIDocument Document => doc;

    /// <summary>The root every screen hangs under.</summary>
    public static VisualElement Root => doc != null ? doc.rootVisualElement : null;

    /// <summary>True while the panel exists (a scene reload destroys it).</summary>
    public static bool Ready => doc != null && hostGo != null;

    /// <summary>The page's pixels per screen pixel (screen over page, times the UI size).</summary>
    public static double Scale { get; private set; } = 1;
    /// <summary>The operating system's display scale (dpi / 96).</summary>
    public static double OsRatio { get; private set; } = 1;
    /// <summary>The page's size in its own pixels: 1920 x 1080 or more along the screen's longer side.</summary>
    public static double PageWidth { get; private set; } = 1280;
    public static double PageHeight { get; private set; } = 720;

    /// <summary>
    /// True just after the mouse really moved. A button under a resting pointer must not take the highlight back from the
    /// keyboard or a controller every time a menu redraws itself.
    /// </summary>
    public static bool MouseActive => Time.unscaledTime - mouseMovedAt < 0.3f;

    /// <summary>Makes the panel once (again after a scene reload).</summary>
    public static void Ensure()
    {
        if (Ready) return;
        Metrics(out float scale);

        hostGo = new GameObject("UiRoot");
        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.name = "UiPanelSettings";
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.scale = scale;
        panel.sortingOrder = 100;
        // Unity's default runtime theme styles the built-in controls the DOM layer uses (ScrollView scrollers,
        // TextField caret and selection); without one UI Toolkit warns that the UI will not render properly.
        var theme = Resources.Load<ThemeStyleSheet>("UI/CozyRuntimeTheme");
        if (theme != null) panel.themeStyleSheet = theme;

        text = BuildTextSettings();
        if (text != null)
        {
            panel.textSettings = text;
        }

        doc = hostGo.AddComponent<UIDocument>();
        doc.panelSettings = panel;
        doc.sortingOrder = 100;

        VisualElement rootVe = doc.rootVisualElement;
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
            if (text != null && text.defaultFontAsset != null)
                rootVe.style.unityFontDefinition = FontDefinition.FromSDFFont(text.defaultFontAsset);

            StyleSheet styleSheet = Resources.Load<StyleSheet>("UI/Style");
            if (styleSheet != null)
            {
                rootVe.styleSheets.Add(styleSheet);
            }
        }

        lastW = Screen.width;
        lastH = Screen.height;
        lastDpi = Screen.dpi;
    }

    /// <summary>Follows the screen's size and dpi and the UI size setting; true when the screen changed.</summary>
    public static bool Tick()
    {
        if (doc == null) return false;
        bool sizeChanged = Screen.width != lastW || Screen.height != lastH || Mathf.Abs(Screen.dpi - lastDpi) > 0.1f;
        Metrics(out float scale);
        if (panel != null && Mathf.Abs(panel.scale - scale) > 0.001f) panel.scale = scale;
        if (sizeChanged)
        {
            lastW = Screen.width;
            lastH = Screen.height;
            lastDpi = Screen.dpi;
        }
        var mouseNow = Mouse.current;
        if (mouseNow != null && mouseNow.delta.ReadValue().sqrMagnitude > 0.5f) mouseMovedAt = Time.unscaledTime;
        return sizeChanged;
    }

    /// <summary>Brings the metrics up to date now if the screen changed since the last tick.</summary>
    internal static void RefreshIfNeeded()
    {
        if (Screen.width != lastW || Screen.height != lastH || Mathf.Abs(Screen.dpi - lastDpi) > 0.1f)
        {
            Metrics(out float scale);
            if (panel != null) panel.scale = scale;
        }
    }

    /// <summary>The page the UI is laid out on. The screen shows at least this much, more along its longer side.</summary>
    public const double PAGE_WIDTH = 1920, PAGE_HEIGHT = 1080;

    /// <summary>How much larger than the page's own size the UI is drawn (the "UI size" setting): 1, 1.15 or 1.3. Picked up by the next Tick.</summary>
    public static double UiScale = 1;

    private static void Metrics(out float scale)
    {
        float dpi = Screen.dpi;
        double os = (dpi > 0f && !float.IsNaN(dpi)) ? (dpi / 96.0) : 1.0;
        if (os < 0.5) os = 1.0;

        int sw = Screen.width > 0 ? Screen.width : 1280;
        int sh = Screen.height > 0 ? Screen.height : 720;

        // A game's menus and HUD take up the same share of the screen at every resolution. The browser page was a fixed size
        // in pixels instead, so on a 1440p or 4K screen everything shrank to a corner. The page is 1920 x 1080 and is scaled
        // to fit: a wider or taller screen sees more page, never less.
        // The "UI size" setting (1 to 1.3) makes the page that much smaller, so everything on it is that much larger. The 3D
        // picture's resolution is unaffected: it is divided by this same ratio again (WebGLRenderer.setPixelRatio).
        double dpr = Math.Min(sw / PAGE_WIDTH, sh / PAGE_HEIGHT) * UiScale;
        if (dpr < 0.3) dpr = 0.3;
        scale = (float)dpr;

        OsRatio = os;
        Scale = dpr;
        PageWidth = Math.Max(1.0, Math.Round(sw / dpr));
        PageHeight = Math.Max(1.0, Math.Round(sh / dpr));

        if (doc != null && doc.rootVisualElement != null)
        {
            Rect r = doc.rootVisualElement.worldBound;
            if (!float.IsNaN(r.width) && !float.IsNaN(r.height) && r.width > 1f && r.height > 1f)
            {
                PageWidth = r.width;
                PageHeight = r.height;
            }
        }
    }

    private static PanelTextSettings BuildTextSettings()
    {
        try
        {
            var pts = ScriptableObject.CreateInstance<PanelTextSettings>();
            pts.name = "UiPanelTextSettings";

            // Body text: a friendly sans. Headings and the title: a bold serif (see DisplayFont). A font file dropped into
            // Resources/UI/Fonts (Body, BodyBold, Display) is used ahead of the system's, which is what a console will need.
            // The bundled faces (Fira Sans, PT Serif, both SIL OFL) are used where Windows' own fonts cannot be counted on:
            // on a Steam Deck (Proton ships no Trebuchet, Georgia or Segoe) and wherever Trebuchet MS is not installed.
            // On a normal Windows PC the look stays as it was.
            bool bundled = Platform.IsSteamDeck || !HasOsFont("Trebuchet MS");
            FontAsset mainFont = (bundled ? LoadFontAsset("UI/Fonts/Body") : null)
                              ?? CreateOsFontAsset("Trebuchet MS", "Regular")
                              ?? CreateOsFontAsset("Segoe UI", "Regular")
                              ?? CreateOsFontAsset("Arial", "Regular")
                              ?? LoadFontAsset("UI/Fonts/Body");
            FontAsset boldFont = (bundled ? LoadFontAsset("UI/Fonts/BodyBold") : null)
                              ?? CreateOsFontAsset("Trebuchet MS", "Bold")
                              ?? CreateOsFontAsset("Segoe UI", "Bold")
                              ?? LoadFontAsset("UI/Fonts/BodyBold");
            DisplayFont = (bundled ? LoadFontAsset("UI/Fonts/Display") : null)
                       ?? CreateOsFontAsset("Georgia", "Bold")
                       ?? LoadFontAsset("UI/Fonts/Display")
                       ?? boldFont;
            // The game's name on the loading screen and the main menu: Pacifico (SIL OFL), a relaxed surf-style script.
            // Always bundled, so the title looks the same on every machine.
            TitleFont = LoadFontAsset("UI/Fonts/Title") ?? DisplayFont;
            Debug.Log($"[Fonts] bundled faces first: {bundled}");

            if (mainFont != null)
            {
                var fallbacks = new List<FontAsset>();
                // Segoe UI for general glyphs, Segoe UI Symbol for plain symbols such as ✓ ▾ ◆
                foreach (var name in new[] { "Segoe UI", "Segoe UI Symbol" })
                {
                    FontAsset fb = CreateOsFontAsset(name, "Regular");
                    if (fb != null && fb != mainFont) fallbacks.Add(fb);
                }
                // the bundled symbol font last: the glyphs the system fonts do not have (it is all there is under Proton)
                var symbols = LoadFontAsset("UI/Fonts/Symbols");
                if (symbols != null) fallbacks.Add(symbols);
                foreach (var f in new[] { mainFont, boldFont, DisplayFont, TitleFont })
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
                    Debug.LogWarning($"UiRoot: could not set the bold face: {ex.Message}");
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
                Debug.LogWarning("UiRoot: Resources/UI/Emoji is missing; emoji will not draw in colour.");
            }

            return pts;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"UiRoot: Could not create OS FontAssets: {ex.Message}");
            return null;
        }
    }

    /// <summary>The heading face (card titles, the game's name). Null until the text settings are built.</summary>
    public static FontAsset DisplayFont { get; private set; }

    /// <summary>The face of the game's name (the loading screen and the main menu). Null until the text settings are built.</summary>
    public static FontAsset TitleFont { get; private set; }

    private static bool HasOsFont(string family)
    {
        try
        {
            foreach (var n in Font.GetOSInstalledFontNames())
                if (string.Equals(n, family, StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch
        {
            /* cannot tell: treat as missing */
        }
        return false;
    }

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

}
