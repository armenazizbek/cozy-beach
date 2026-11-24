using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A native UI element (unity/NATIVE_PLAN.md 2.7, S28): a VisualElement built exactly as the DOM layer built one for the
/// same tag and classes, so Style.uss draws it the same, without the page emulation around it (no HTML parsing, selectors,
/// CSS strings or event objects). What it keeps from the DOM layer is only what shows:
/// <list type="bullet">
/// <item>the tag class (`t-div`, `t-span`, ...) and the classes;</item>
/// <item>its own text in a label with no margins or padding, first among its children, emoji drawn as sprites and
/// uppercased under the page's uppercase rules;</item>
/// <item>the heading face for h1, h2 and `.display`, the title face for the main menu's and loading screen's classes;</item>
/// <item>pill-shaped corners kept at half the shorter side (CSS's clamping of a big radius);</item>
/// <item>`.hidden` winning over every other display rule;</item>
/// <item>picking by the page's rules (pointer-events none classes, buttons and clickable elements, interactive
/// containers);</item>
/// <item>tooltips, and the page's animations and helper classes (UiAnim).</item>
/// </list>
/// </summary>
public class UiNode : VisualElement
{
    /// <summary>
    /// The element itself (unity/UI_PLAN.md U10: a UiNode is a VisualElement now, not a wrapper holding one). Code that
    /// reaches for UI Toolkit's own members goes through it, so `ve.Add`, `ve.Clear` and `ve.Focus` are UI Toolkit's, and
    /// the same calls on the node are the node's (they keep its children, scroll box and highlight in order).
    /// </summary>
    public VisualElement ve => this;
    public string tag { get; private set; }
    public UiNode Parent { get; private set; }
    private readonly List<UiNode> kids = new List<UiNode>();
    private Label label;
    private string text = "";
    private Action onClick;

    /// <summary>The page's uppercase rules (the old page's HTMLElement.UPPERCASE): `.card .cat`, `.set-rows .t-h3`, `.bp-head`, `.caps`.</summary>
    private static bool Uppercase(UiNode n)
    {
        for (var c = n; c != null; c = c.Parent)
        {
            if (c.Has("bp-head") || c.Has("caps")) return true;
            if (c.Has("cat") && Ancestor(c, "card")) return true;
            if (c.tag == "h3" && Ancestor(c, "set-rows")) return true;
        }
        return false;
    }

    private static bool Ancestor(UiNode n, string cls)
    {
        for (var c = n.Parent; c != null; c = c.Parent) if (c.Has(cls)) return true;
        return false;
    }

    /// <summary>The page's scroll boxes (the old page's HTMLElement.ScrollableClasses): their children go in a ScrollView.</summary>
    private static readonly HashSet<string> ScrollableClasses = new HashSet<string> { "camp-body", "save-list", "ach-list", "help-body", "set-rows" };

    private ScrollView sv;
    private double scrollTop;

    /// <summary>Where the children go: the scroll box's content for a scrolling element.</summary>
    private VisualElement Container => sv != null ? sv.contentContainer : ve;

    private static readonly HashSet<string> TitleFontClasses = new HashSet<string> { "mm-title", "mm-kicker", "mm-tag", "mm-label", "mm-foot", "game-title", "title-face" };
    private static readonly HashSet<string> InteractiveContainers = new HashSet<string> { "camp", "intro-card", "card", "inspect", "chip", "intro", "pause", "mainmenu", "set-options" };
    /// <summary>The page's pointer-events: none (classes and ids): never picked.</summary>
    private static readonly HashSet<string> PointerNone = new HashSet<string>
    {
        "ui", "gone", "prompt", "hud", "result", "fade", "binoc", "map-cursor", "padtools", "padbar", "sleepveil", "dive-tint", "book-spread-crease",
    };

    /// <summary>The page's z-index (classes and ids): siblings are kept in this order, then in the order they were added.</summary>
    internal static readonly Dictionary<string, int> ZIndex = new Dictionary<string, int>
    {
        { "boot", 100 }, { "padbar", 50 }, { "map-cursor", 20 }, { "achievements", 7 }, { "helpscreen", 7 }, { "farewell", 6 },
        { "credits", 6 }, { "bookview", 6 }, { "teaser", 6 }, { "saves", 5 }, { "sleepveil", 3 },
    };

    private static int ZOf(VisualElement v)
    {
        int best = 0;
        if (!string.IsNullOrEmpty(v.name) && ZIndex.TryGetValue(v.name, out int idZ) && idZ > best) best = idZ;
        foreach (var c in v.GetClasses())
            if (ZIndex.TryGetValue(c, out int z) && z > best) best = z;
        return best;
    }

    /// <summary>Keeps the children in z-index order (a stable sort: equal ones stay in the order they were added).</summary>
    private void Reorder()
    {
        var box = Container;
        if (box.childCount <= 1) return;
        var all = new List<(VisualElement v, int z, int i)>();
        bool any = false;
        int idx = 0;
        foreach (var v in box.Children())
        {
            int z = ZOf(v);
            if (z != 0) any = true;
            all.Add((v, z, idx++));
        }
        if (!any) return;
        all.Sort((a, b) => a.z != b.z ? a.z.CompareTo(b.z) : a.i.CompareTo(b.i));
        foreach (var e in all) e.v.BringToFront();
    }

    public UiNode(string tag = "div", string classes = null, string text = null) => Init(tag, classes, text);

    /// <summary>For an element made by UXML (an <see cref="El"/>): it calls <see cref="Init"/> once its attributes are set.</summary>
    protected UiNode(bool fromUxml) { }

    /// <summary>
    /// Gives the element its tag, classes, text and behaviour. A built element does this when it is made; one made by UXML
    /// once the screen's attributes are on it (UI/Native/Ux.cs adopts whole trees), so both behave alike.
    /// </summary>
    protected void Init(string tag, string classes, string text)
    {
        this.tag = string.IsNullOrEmpty(tag) ? "div" : tag.ToLowerInvariant();
        // the element a VisualElement stands for (the menu navigation finds native elements by it, NavEl.Of)
        ve.userData = this;
        ve.AddToClassList("t-" + this.tag);
        // UI Toolkit's transitions (a toast fading in, a card sliding) run on real time, not on the smoke test's fixed clock,
        // so a picture taken during one depended on how long frames really took. In a test run they end at once.
        if (TestClock.Fixed) ve.style.transitionDuration = new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new TimeValue(0) });
        ve.RegisterCallback<GeometryChangedEvent>(FitPill);
        // the highlight follows UI Toolkit's focus, as the page's activeElement did: an element hidden or taken away while
        // chosen lets go of it (the camp closing must not keep its last button chosen for the next visit)
        ve.RegisterCallback<FocusInEvent>(e => { if (e.target == ve) MenuNav.FocusNative(this); });
        ve.RegisterCallback<FocusOutEvent>(e => { if (e.target == ve && MenuNav.Current.node == this) MenuNav.ForgetNative(); });
        if (this.tag == "button")
        {
            ve.focusable = true;
            // as the page's buttons: a click stops there, pressing one chooses it, and the mouse moving onto one chooses it
            // (so the mouse, the keyboard and a controller share one highlight)
            ve.RegisterCallback<ClickEvent>(e =>
            {
                if (!ve.enabledSelf) return;
                e.StopPropagation();
                onClick?.Invoke();
            });
            ve.RegisterCallback<PointerDownEvent>(_ => { if (ve.enabledSelf && ve.focusable) Focus(); });
            ve.RegisterCallback<PointerMoveEvent>(_ => HoverFocus());
            ve.RegisterCallback<PointerEnterEvent>(_ => HoverFocus());
        }
        if (!string.IsNullOrEmpty(classes)) foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) ve.AddToClassList(c);
        Fonts();
        Scroll();
        if (!string.IsNullOrEmpty(text)) SetText(text);
        Restyle();
    }

    /// <summary>A scroll box as the page made one (the old page's HTMLElement.EnsureScrollView): up and down only, filling the element.</summary>
    private void Scroll()
    {
        if (sv != null) return;
        bool want = false;
        foreach (var c in ve.GetClasses()) if (ScrollableClasses.Contains(c)) want = true;
        if (!want) return;
        sv = new ScrollView(ScrollViewMode.Vertical);
        sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        sv.style.flexGrow = 1;
        sv.style.flexShrink = 1;
        sv.style.width = Length.Percent(100);
        sv.style.height = Length.Percent(100);
        if (sv.verticalScroller != null) sv.verticalScroller.valueChanged += v => scrollTop = v;
        sv.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            if (scrollTop > 0 && Mathf.Abs(sv.scrollOffset.y - (float)scrollTop) > 0.5f)
                sv.scrollOffset = new Vector2(sv.scrollOffset.x, (float)scrollTop);
        });
        if (label != null && label.parent == ve)
        {
            label.RemoveFromHierarchy();
            sv.contentContainer.Add(label);
        }
        foreach (var k in kids)
        {
            if (k.ve.parent == ve)
            {
                k.ve.RemoveFromHierarchy();
                sv.contentContainer.Add(k.ve);
            }
        }
        ve.Add(sv);
    }

    /// <summary>How far the scroll box is scrolled down (the page's scrollTop).</summary>
    public double ScrollTop
    {
        get
        {
            if (sv != null)
            {
                float wy = sv.contentContainer.worldBound.height;
                if (!float.IsNaN(wy) && wy > 1f) return sv.scrollOffset.y;
            }
            return scrollTop;
        }
        set
        {
            scrollTop = Math.Max(0.0, value);
            if (sv != null) sv.scrollOffset = new Vector2(sv.scrollOffset.x, (float)scrollTop);
        }
    }

    /// <summary>The height shown (clientHeight).</summary>
    public double ClientHeight => new NavEl(this).Box.height;

    /// <summary>The height of all it holds (scrollHeight).</summary>
    public double ScrollHeight
    {
        get
        {
            if (sv != null)
            {
                float ch = sv.contentContainer.worldBound.height;
                if (!float.IsNaN(ch) && ch > 1f) return ch;
                return Math.Max(ClientHeight, kids.Count * 40.0);
            }
            return ClientHeight;
        }
    }

    // ------------------------------------------------------------------ text

    /// <summary>The element's own words (textContent): setting them takes its children away.</summary>
    public string Text
    {
        get => text;
        set
        {
            if (kids.Count == 0 && text == (value ?? "") && (label != null || string.IsNullOrEmpty(value))) return;
            Clear();
            SetText(value ?? "");
        }
    }

    /// <summary>
    /// Sets what an innerHTML of text set on the page: plain words (entities decoded) or inline formatting as rich text.
    /// </summary>
    public string Markup
    {
        set
        {
            if (!TextMarkup.TryText(value, out var t)) throw new ArgumentException("native elements take text, not markup: " + value);
            Clear();
            SetText(t);
        }
    }

    private void SetText(string t)
    {
        text = t ?? "";
        if (text.Length == 0)
        {
            if (label != null)
            {
                label.text = "";
                label.RemoveFromHierarchy();
            }
            return;
        }
        if (label == null)
        {
            label = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
            label.style.marginTop = label.style.marginBottom = label.style.marginLeft = label.style.marginRight = 0;
            label.style.paddingTop = label.style.paddingBottom = label.style.paddingLeft = label.style.paddingRight = 0;
            // uppercase can depend on where the element ends up, so the words are worked out again once it is on the panel
            label.RegisterCallback<AttachToPanelEvent>(_ => { if (label != null) label.text = Shown(); });
        }
        label.text = Shown();
        if (label.parent != Container) Container.Insert(0, label);
    }

    private string Shown()
    {
        string t = text;
        if (t.Length > 0 && Uppercase(this)) t = t.ToUpperInvariant();
        return EmojiText.Apply(t);
    }

    // ------------------------------------------------------------------ tree

    /// <summary>Adds children (strings become text: the element's own if it has none yet, else a #text child).</summary>
    public UiNode Add(params object[] items)
    {
        foreach (var it in items)
        {
            switch (it)
            {
                case null: break;
                case UiNode n: Append(n); break;
                case string s:
                    if (kids.Count == 0 && string.IsNullOrEmpty(text)) SetText(s);
                    else
                    {
                        var t = new UiNode("#text");
                        t.ve.style.flexShrink = 1;
                        t.SetText(s);
                        Append(t);
                    }
                    break;
            }
        }
        return this;
    }

    private void Append(UiNode n)
    {
        n.Parent?.kids.Remove(n);
        n.Parent = this;
        kids.Add(n);
        Container.Add(n.ve);
        // a button straight inside the camp's page keeps its own width (the old page's HTMLElement.ApplyDirectChildFixups)
        if (Has("camp-body") && n.Has("btn")) n.ve.style.alignSelf = Align.FlexStart;
        n.Restyle();
        Reorder();
        n.Attached();
    }

    /// <summary>Puts `n` just before this element in its parent (DOM before()).</summary>
    public void Before(UiNode n)
    {
        if (Parent == null) return;
        var p = Parent;
        n.Parent?.kids.Remove(n);
        n.Parent = p;
        int i = p.kids.IndexOf(this);
        p.kids.Insert(i, n);
        p.Container.Insert(p.Container.IndexOf(ve), n.ve);
        n.Restyle();
        p.Reorder();
        n.Attached();
    }

    /// <summary>Hangs this element under a VisualElement that is not a UiNode (a page element while both kinds coexist).</summary>
    public void MountIn(VisualElement parent)
    {
        if (parent.userData is UiNode p && p.ve == parent)
        {
            p.Add(this);
            return;
        }
        Remove();
        parent.Add(ve);
        Restyle();
        Attached();
    }

    /// <summary>Hangs this element just before `sibling` under the sibling's (non-UiNode) parent.</summary>
    public void MountBefore(VisualElement sibling)
    {
        var parent = sibling?.parent;
        if (parent == null) return;
        if (sibling.userData is UiNode sib && sib.ve == sibling && sib.Parent != null)
        {
            sib.Before(this);
            return;
        }
        Remove();
        parent.Insert(parent.IndexOf(sibling), ve);
        Restyle();
        Attached();
    }

    /// <summary>The font size the page used to size an inline drawing in this element (the old page's HTMLElement.GetEffectiveFontSize).</summary>
    public float FontSize()
    {
        for (var c = this; c != null; c = c.Parent)
        {
            if (c.Has("tb-ico")) return 24f;
            if (c.Has("pt")) return 22f;
            if (c.Has("mm-ico")) return 22f;
            if (c.Has("intro-first")) return 20f;
            if (c.ve.panel != null)
            {
                float fs = c.ve.resolvedStyle.fontSize;
                if (!float.IsNaN(fs) && fs > 1f) return fs;
            }
        }
        return 16.36f;
    }

    /// <summary>Shows an inline SVG drawing (sized in em, as the page sized it) in place of the element's content.</summary>
    public void Svg(string markup)
    {
        Clear();
        var tex = SvgRasterizer.Rasterize(markup, FontSize(), out float w, out float h);
        var svg = new UiNode("svg");
        if (tex != null)
        {
            svg.ve.style.backgroundImage = new StyleBackground(tex);
            svg.ve.style.width = w;
            svg.ve.style.height = h;
            svg.ve.style.flexShrink = 0;
            svg.ve.style.alignSelf = Align.Center;
        }
        Add(svg);
    }

    private void Attached()
    {
        UiAnim.OnAttached(ve);
        foreach (var k in kids) k.Attached();
        if (label != null) label.text = Shown();
    }

    /// <summary>Takes every child away and the words too (replaceChildren()).</summary>
    public new void Clear()
    {
        foreach (var k in kids)
        {
            k.Parent = null;
            k.ve.RemoveFromHierarchy();
        }
        kids.Clear();
        SetText("");
    }

    /// <summary>Takes the children away and puts these in their place.</summary>
    public void Replace(params object[] items)
    {
        Clear();
        Add(items);
    }

    /// <summary>Leaves its parent.</summary>
    public void Remove()
    {
        Parent?.kids.Remove(this);
        Parent = null;
        ve.RemoveFromHierarchy();
    }

    public new IReadOnlyList<UiNode> Children => kids;

    /// <summary>Links a child made by UXML (already in place under this element) into the tree, without moving it.</summary>
    internal void AdoptChild(UiNode n)
    {
        n.Parent = this;
        kids.Add(n);
        // a scroll box holds its children in its content
        if (n.ve.parent != Container) Container.Add(n.ve);
        if (Has("camp-body") && n.Has("btn")) n.ve.style.alignSelf = Align.FlexStart;
    }

    /// <summary>The element with this name (UXML's name attribute) under this one, or null.</summary>
    public UiNode Q(string name) => ve.Q(name)?.userData as UiNode;

    /// <summary>Like <see cref="Q"/>, but a missing element is a mistake in the screen's UXML.</summary>
    public UiNode Must(string name) => Q(name) ?? throw new InvalidOperationException($"no element named '{name}' under {ClassName}");

    // ------------------------------------------------------------------ classes

    public bool Has(string cls) => ve.ClassListContains(cls);

    public UiNode AddClass(string cls)
    {
        if (!Has(cls)) { ve.AddToClassList(cls); Changed(); }
        return this;
    }

    public UiNode RemoveClass(string cls)
    {
        if (Has(cls)) { ve.RemoveFromClassList(cls); Changed(); }
        return this;
    }

    /// <summary>classList.toggle(cls, on).</summary>
    public bool Toggle(string cls, bool on)
    {
        if (on) AddClass(cls);
        else RemoveClass(cls);
        return on;
    }

    /// <summary>className: replaces every class but the tag's.</summary>
    public string ClassName
    {
        get
        {
            var l = new List<string>();
            foreach (var c in ve.GetClasses()) if (c != "t-" + tag && c != PILL) l.Add(c);
            return string.Join(" ", l);
        }
        set
        {
            if (ClassName == (value ?? "")) return;
            bool pill = Has(PILL);
            ve.ClearClassList();
            ve.AddToClassList("t-" + tag);
            if (pill) ve.AddToClassList(PILL);
            if (!string.IsNullOrEmpty(value)) foreach (var c in value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) ve.AddToClassList(c);
            Changed();
        }
    }

    /// <summary>Shown unless `.hidden` (the page's `hidden` class).</summary>
    public bool Hidden
    {
        get => Has("hidden");
        set => Toggle("hidden", value);
    }

    private void Changed()
    {
        Fonts();
        Scroll();
        Restyle();
        Parent?.Reorder();
        UiAnim.OnClassesChanged(ve);
        if (label != null) label.text = Shown();
    }

    private bool display, title;

    private void Fonts()
    {
        foreach (var c in ve.GetClasses())
        {
            if (TitleFontClasses.Contains(c) && !title && UiRoot.TitleFont != null)
            {
                title = true;
                ve.style.unityFontDefinition = FontDefinition.FromSDFFont(UiRoot.TitleFont);
            }
        }
        if (!title && !display && (tag == "h1" || tag == "h2" || Has("display")) && UiRoot.DisplayFont != null)
        {
            display = true;
            ve.style.unityFontDefinition = FontDefinition.FromSDFFont(UiRoot.DisplayFont);
        }
    }

    // ------------------------------------------------------------------ look and picking

    /// <summary>Its words and its children's (textContent).</summary>
    public string FullText
    {
        get
        {
            if (kids.Count == 0) return text;
            var sb = new System.Text.StringBuilder(text);
            foreach (var k in kids) sb.Append(k.FullText);
            return sb.ToString();
        }
    }

    /// <summary>Left and right on it change its value (a setting): -1 or 1.</summary>
    public Action<int> OnStep;

    /// <summary>Takes the menu highlight (as a page element's focus()).</summary>
    public new virtual void Focus()
    {
        if (!ve.enabledSelf) return;
        MenuNav.FocusNative(this);
        ve.Focus();
    }

    private void HoverFocus()
    {
        if (!ve.enabledSelf || !ve.focusable || !UiRoot.MouseActive) return;
        if (MenuNav.Current.node == this) return;
        Focus();
    }

    /// <summary>Lets go of the highlight (as a page element's blur()).</summary>
    public new void Blur()
    {
        if (MenuNav.Current.node == this) MenuNav.ForgetNative();
        ve.Blur();
    }

    /// <summary>True when `other` is this element or inside it.</summary>
    public bool Contains(UiNode other)
    {
        for (var c = other; c != null; c = c.Parent) if (c == this) return true;
        return false;
    }

    /// <summary>Shows a picture as the page's &lt;img&gt; did (the texture as its background).</summary>
    public UiNode Image(Texture tex)
    {
        if (tex == null) ve.style.backgroundImage = StyleKeyword.Null;
        else if (tex is Texture2D t2d) ve.style.backgroundImage = new StyleBackground(t2d);
        else if (tex is RenderTexture rt) ve.style.backgroundImage = Background.FromRenderTexture(rt);
        return this;
    }

    /// <summary>Presses it (as a page element's click()).</summary>
    public void Click()
    {
        if (!ve.enabledSelf) return;
        onClick?.Invoke();
    }

    /// <summary>Greyed out and out of the highlight's reach (a page button's disabled).</summary>
    public bool Disabled
    {
        get => !ve.enabledSelf;
        set
        {
            if (Disabled == value) return;
            ve.SetEnabled(!value);
            ve.EnableInClassList("disabled", value);
        }
    }

    /// <summary>The words shown when the pointer rests on it.</summary>
    public string Tooltip
    {
        get => ve.tooltip;
        set => ve.tooltip = value ?? "";
    }

    /// <summary>What a click does (it also makes the element pickable, as a click listener did on the page).</summary>
    public Action OnClick
    {
        get => onClick;
        set
        {
            bool had = onClick != null;
            onClick = value;
            if (tag != "button" && !had && value != null) ve.RegisterCallback<ClickEvent>(e =>
            {
                if (!ve.enabledSelf) return;
                e.StopPropagation();
                onClick?.Invoke();
            });
            Restyle();
        }
    }

    /// <summary>The display rule and picking of this element and everything under it.</summary>
    private void Restyle()
    {
        ve.style.display = Has("hidden") ? DisplayStyle.None : StyleKeyword.Null;
        ve.pickingMode = Pickable() ? PickingMode.Position : PickingMode.Ignore;
        foreach (var k in kids) k.Restyle();
    }

    private bool Pickable()
    {
        // hidden or leaving (itself or anything above it): never picked
        for (var c = this; c != null; c = c.Parent)
            if (c.Has("gone") || c.Has("hidden")) return false;
        // the page's pointer-events: none classes (#ui, .hud, .prompt, ...) are for the element itself only: the menus and
        // chips inside #ui and .hud still take the mouse (the old page's HTMLElement.ComputeIsPickable)
        if (PointerNoneSelf()) return false;
        if (tag == "button" || tag == "input" || tag == "select") return true;
        if (onClick != null) return true;
        for (var c = this; c != null; c = c.Parent)
            foreach (var cls in c.ve.GetClasses())
                if (InteractiveContainers.Contains(cls)) return true;
        return false;
    }

    private bool PointerNoneSelf()
    {
        if (!string.IsNullOrEmpty(ve.name) && PointerNone.Contains(ve.name)) return true;
        foreach (var c in ve.GetClasses()) if (PointerNone.Contains(c)) return true;
        return false;
    }

    private const string PILL = "t-pill";

    /// <summary>A corner radius over half the box's shorter side becomes exactly that (the old page's HTMLElement.FitPillRadius).</summary>
    private static void FitPill(GeometryChangedEvent e)
    {
        var v = (VisualElement)e.currentTarget;
        float half = Mathf.Min(v.layout.width, v.layout.height) / 2f;
        if (!(half > 0)) return;
        if (!v.ClassListContains(PILL))
        {
            var rs = v.resolvedStyle;
            if (rs.borderTopLeftRadius <= half + 0.5f && rs.borderTopRightRadius <= half + 0.5f &&
                rs.borderBottomLeftRadius <= half + 0.5f && rs.borderBottomRightRadius <= half + 0.5f) return;
            v.AddToClassList(PILL);
        }
        v.style.borderTopLeftRadius = v.style.borderTopRightRadius = v.style.borderBottomLeftRadius = v.style.borderBottomRightRadius = half;
    }
}
