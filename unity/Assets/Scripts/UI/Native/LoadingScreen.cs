using UnityEngine.UIElements;

/// <summary>
/// The loading screen, native (unity/NATIVE_PLAN.md S27): the game's name, the line under it and the bar, over the live
/// sea (LoadingSea). It is built with the elements and class names the page had (#boot, .t-h1.game-title,
/// .t-p.title-face, #boot-track, #boot-bar), so Style.uss draws it as before, and it lifts with the same fade.
/// </summary>
public static class LoadingScreen
{
    private static VisualElement root, bar;

    /// <summary>Marks a native screen's root, so the pointer over it counts as over the UI (Page.IsPointerOverUI).</summary>
    public const string NATIVE = "ui-native";

    /// <summary>Makes the screen (once per page) and returns its root, to hang in the page's body.</summary>
    public static VisualElement Build()
    {
        root = Box("t-div " + NATIVE, "boot");
        root.pickingMode = PickingMode.Position;

        var h1 = Box("t-h1 game-title");
        // an h1 is drawn in the heading face, and .game-title puts it in the title face (the old page's HTMLElement.UseTitleFont)
        var h1Font = UiRoot.TitleFont ?? UiRoot.DisplayFont;
        if (h1Font != null) h1.style.unityFontDefinition = FontDefinition.FromSDFFont(h1Font);
        h1.Add(Line("Tidefinder"));
        var p = Box("t-p title-face");
        if (UiRoot.TitleFont != null) p.style.unityFontDefinition = FontDefinition.FromSDFFont(UiRoot.TitleFont);
        p.Add(Line("Getting the beach ready…"));
        var track = Box("t-div", "boot-track");
        bar = Box("t-div", "boot-bar");
        track.Add(bar);
        foreach (var ve in new[] { h1, p, track, bar }) ve.pickingMode = PickingMode.Position;
        root.Add(h1);
        root.Add(p);
        root.Add(track);
        // behind the words and the bar: the live sea, which answers the pointer and a controller
        var sea = new LoadingSea();
        root.Insert(0, sea);
        sea.Listen(root);
        return root;
    }

    /// <summary>A line of text as the page drew an element's own text (a label with no margins or padding of its own).</summary>
    private static Label Line(string text)
    {
        var l = new Label(EmojiText.Apply(text)) { pickingMode = PickingMode.Ignore, enableRichText = true };
        l.style.marginTop = l.style.marginBottom = l.style.marginLeft = l.style.marginRight = 0;
        l.style.paddingTop = l.style.paddingBottom = l.style.paddingLeft = l.style.paddingRight = 0;
        return l;
    }

    /// <summary>How far loading has come, 0 to 1 (the bar eases there over 0.3 s, as Style.uss says).</summary>
    public static void Progress(double fraction)
    {
        if (bar == null) return;
        bar.style.width = Length.Percent((float)(fraction * 100));
        // above anything the page added to its body since (the page kept #boot on top by its z-index)
        root?.BringToFront();
    }

    /// <summary>Fades the screen out (0.5 s) and takes it away after 0.7 s.</summary>
    public static void Lift()
    {
        if (root == null) return;
        var r = root;
        r.AddToClassList("gone");
        r.pickingMode = PickingMode.Ignore;
        foreach (var c in r.Query<VisualElement>().ToList()) c.pickingMode = PickingMode.Ignore;
        GameTimers.After(700, () => r.RemoveFromHierarchy());
        root = null;
        bar = null;
    }

    /// <summary>A plain element with classes ("a b c") and an optional name: the loading screen is up before the UI's nodes are.</summary>
    private static VisualElement Box(string classes = null, string name = null)
    {
        var ve = new VisualElement();
        if (!string.IsNullOrEmpty(name)) ve.name = name;
        if (!string.IsNullOrEmpty(classes))
            foreach (var c in classes.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)) ve.AddToClassList(c);
        return ve;
    }
}
