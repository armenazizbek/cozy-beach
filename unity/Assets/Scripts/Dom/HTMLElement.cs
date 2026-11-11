using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

// HTMLElement and subclasses (Section 3 of PORT_SPEC.md)

public class HTMLElement
{
    internal static readonly Dictionary<VisualElement, HTMLElement> ElementByVe = new Dictionary<VisualElement, HTMLElement>();

    private static readonly HashSet<string> ScrollableClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "camp-body",
        "save-list",
        "ach-list",
        "help-body",
        "set-rows",
    };

    /// <summary>Classes drawn in the heading face (DomHost.DisplayFont), as h1 and h2 are.</summary>
    private static readonly HashSet<string> DisplayFontClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "mm-title",
        "display",
    };

    private static readonly HashSet<string> PointerNoneSet = new HashSet<string>(
        CssAnimations.PointerEventsNone ?? Array.Empty<string>(),
        StringComparer.Ordinal);

    private static readonly HashSet<string> InteractiveContainerClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "camp",
        "intro-card",
        "card",
        "inspect",
        "chip",
        "intro",
        "pause",
        "mainmenu",
        "set-options",
    };

    public readonly string tagName;
    public readonly DOMTokenList classList;
    public readonly DOMStringMap dataset;
    public readonly CSSStyleDeclaration style;
    public VisualElement ve;

    private string _id = "";
    private string _title = "";
    private string _directText = "";
    private bool _hidden = false;
    private int _tabIndex = -1;
    protected bool _disabled = false;

    internal Label _textLabel;
    internal ScrollView _scrollView;
    private double _scrollTop = 0;

    internal readonly List<HTMLElement> _children = new List<HTMLElement>();
    private readonly Dictionary<string, string> _attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<Event>>> _listeners = new Dictionary<string, List<Action<Event>>>(StringComparer.OrdinalIgnoreCase);

    public HTMLElement parentElement { get; internal set; }
    public IReadOnlyList<HTMLElement> children => _children;

    public Action onclick;
    /// <summary>Left and right on a focused control that has a value to change (a slider, a switch, a list of choices): -1 or 1.</summary>
    public Action<int> onstep;
    public Action<Event> onpointerdown, onpointerenter, onpointerleave, onkeydown, onwheel, oninput, onchange, onscroll;

    private const string PILL = "t-pill";

    /// <summary>
    /// The page's text-transform: uppercase rules (USS has no text-transform): the main menu's small line above the title,
    /// card categories, settings headings and journal page headings.
    /// </summary>
    private const string UPPERCASE = ".mm-kicker, .card .cat, .set-rows .t-h3, .bp-head, .caps";

    /// <summary>The text as it is shown: uppercased where the page says so, emoji swapped for the colour sprites.</summary>
    private string DisplayText()
    {
        string t = _directText ?? "";
        if (t.Length > 0 && closest(UPPERCASE) != null) t = t.ToUpperInvariant();
        return EmojiText.Apply(t);
    }

    /// <summary>
    /// CSS shrinks an oversized corner radius (the usual border-radius: 999px for pill shapes) to half the box's shorter
    /// side, so a wide chip gets round ends. UI Toolkit clamps it per axis instead, which turned the HUD chips, prompts and
    /// hints into ovals. An element whose radius is more than half its shorter side is given exactly that, and keeps it as
    /// its size changes.
    /// </summary>
    private static void FitPillRadius(GeometryChangedEvent e)
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

    public HTMLElement(string tag = "DIV")
    {
        tagName = string.IsNullOrWhiteSpace(tag) ? "DIV" : tag.Trim().ToUpperInvariant();
        ve = new VisualElement();
        ElementByVe[ve] = this;

        string tagClass = "t-" + tagName.ToLowerInvariant();
        ve.AddToClassList(tagClass);
        ve.RegisterCallback<GeometryChangedEvent>(FitPillRadius);

        classList = new DOMTokenList(this);
        dataset = new DOMStringMap(this);
        style = new CSSStyleDeclaration(this);

        if (tagName == "BUTTON" || tagName == "INPUT" || tagName == "SELECT")
        {
            ve.focusable = true;
            _tabIndex = 0;
        }

        if (tagName == "H1" || tagName == "H2") UseDisplayFont();

        RegisterBaseEvents();
        UpdatePickingMode();
    }

    private bool _displayFont;

    private void UseDisplayFont()
    {
        if (_displayFont || ve == null || DomHost.DisplayFont == null) return;
        _displayFont = true;
        ve.style.unityFontDefinition = FontDefinition.FromSDFFont(DomHost.DisplayFont);
    }

    /// <summary>
    /// Takes the element out of the reach of the keyboard and the controller (and of a mouse press), for a control that sits
    /// inside a row which is itself the thing that is chosen.
    /// </summary>
    public void noFocus()
    {
        _tabIndex = -1;
        if (ve != null)
        {
            ve.focusable = false;
            ve.tabIndex = -1;
        }
        classList.add("nofocus");
    }

    /// <summary>The mouse moving onto a button chooses it, so the mouse, the keyboard and a controller share one highlight.</summary>
    private void HoverFocus()
    {
        if (_disabled || ve == null || !ve.focusable || document.activeElement == this) return;
        if (tagName != "BUTTON" || !DomHost.MouseActive) return;
        focus();
    }

    public string id
    {
        get => _id;
        set
        {
            _id = value ?? "";
            if (ve != null) ve.name = _id;
            SetRawAttribute("id", string.IsNullOrEmpty(_id) ? null : _id);
            UpdatePickingMode();
            parentElement?.ReorderChildrenByZIndex();
        }
    }

    public string className
    {
        get => classList.ToString();
        set
        {
            classList.SetFromClassNameString(value ?? "");
            SetRawAttribute("class", string.IsNullOrEmpty(value) ? null : value);
        }
    }

    public string title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            if (ve != null) ve.tooltip = _title;
            SetRawAttribute("title", string.IsNullOrEmpty(_title) ? null : _title);
        }
    }

    public virtual string textContent
    {
        get
        {
            if (_children.Count == 0) return _directText ?? "";
            var sb = new StringBuilder(_directText ?? "");
            for (int i = 0; i < _children.Count; i++)
            {
                sb.Append(_children[i].textContent);
            }
            return sb.ToString();
        }
        set
        {
            ClearChildrenOnly();
            SetDirectText(value ?? "", isRich: false);
        }
    }

    public string innerText
    {
        get => textContent;
        set => textContent = value;
    }

    public string innerHTML
    {
        get => textContent;
        set => HtmlParser.ApplyInnerHtml(this, value);
    }

    public bool hidden
    {
        get => _hidden;
        set
        {
            _hidden = value;
            ApplyDisplay();
            UpdatePickingMode();
        }
    }

    public int tabIndex
    {
        get => _tabIndex;
        set
        {
            _tabIndex = value;
            if (ve != null)
            {
                ve.tabIndex = value;
                if (value >= 0) ve.focusable = true;
            }
        }
    }

    public virtual bool disabled
    {
        get => _disabled;
        set
        {
            if (_disabled == value) return;
            _disabled = value;
            if (ve != null)
            {
                ve.SetEnabled(!_disabled);
                ve.EnableInClassList("disabled", _disabled);
            }
            SetRawAttribute("disabled", _disabled ? "" : null);
            if (_disabled && document.activeElement == this)
            {
                blur();
            }
        }
    }

    public HTMLElement firstElementChild => _children.Count > 0 ? _children[0] : null;
    public HTMLElement lastElementChild => _children.Count > 0 ? _children[_children.Count - 1] : null;

    public HTMLElement nextElementSibling
    {
        get
        {
            if (parentElement == null) return null;
            var list = parentElement._children;
            int idx = list.IndexOf(this);
            return (idx >= 0 && idx + 1 < list.Count) ? list[idx + 1] : null;
        }
    }

    public HTMLElement previousElementSibling
    {
        get
        {
            if (parentElement == null) return null;
            var list = parentElement._children;
            int idx = list.IndexOf(this);
            return (idx > 0) ? list[idx - 1] : null;
        }
    }

    public bool isConnected
    {
        get
        {
            var cur = this;
            while (cur != null)
            {
                if (cur == document.body || cur == document.documentElement) return true;
                cur = cur.parentElement;
            }
            return false;
        }
    }

    internal VisualElement ChildContainer => _scrollView != null ? _scrollView.contentContainer : ve;

    internal void SetDirectText(string text, bool isRich)
    {
        _directText = text ?? "";
        if (string.IsNullOrEmpty(_directText))
        {
            if (_textLabel != null)
            {
                _textLabel.text = "";
                _textLabel.RemoveFromHierarchy();
            }
            return;
        }

        if (_textLabel == null)
        {
            _textLabel = new Label
            {
                pickingMode = PickingMode.Ignore,
                enableRichText = true,
            };
            _textLabel.style.marginTop = 0;
            _textLabel.style.marginBottom = 0;
            _textLabel.style.marginLeft = 0;
            _textLabel.style.marginRight = 0;
            _textLabel.style.paddingTop = 0;
            _textLabel.style.paddingBottom = 0;
            _textLabel.style.paddingLeft = 0;
            _textLabel.style.paddingRight = 0;
            ElementByVe[_textLabel] = this;
            // uppercase can depend on where the element ends up, so the text is worked out again once it is in the page
            _textLabel.RegisterCallback<AttachToPanelEvent>(_ => { if (_textLabel != null) _textLabel.text = DisplayText(); });
        }

        _textLabel.enableRichText = true;
        _textLabel.text = DisplayText();

        var container = ChildContainer;
        if (container != null && _textLabel.parent != container)
        {
            container.Insert(0, _textLabel);
        }
    }

    internal void ClearChildrenOnly()
    {
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            var c = _children[i];
            c.parentElement = null;
            c.ve?.RemoveFromHierarchy();
        }
        _children.Clear();
    }

    internal void ClearAllContent()
    {
        ClearChildrenOnly();
        SetDirectText("", isRich: false);
    }

    internal void EnsureScrollView()
    {
        if (_scrollView != null || ve == null) return;
        _scrollView = new ScrollView(ScrollViewMode.Vertical);
        // the page's scroll boxes only ever scroll up and down (text wraps to their width)
        _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        _scrollView.style.flexGrow = 1;
        _scrollView.style.flexShrink = 1;
        _scrollView.style.width = Length.Percent(100);
        _scrollView.style.height = Length.Percent(100);
        ElementByVe[_scrollView] = this;
        ElementByVe[_scrollView.contentContainer] = this;

        if (_scrollView.verticalScroller != null)
        {
            _scrollView.verticalScroller.valueChanged += v =>
            {
                _scrollTop = v;
                var ev = new Event { type = "scroll", target = this, currentTarget = this };
                onscroll?.Invoke(ev);
                DispatchToListeners("scroll", ev);
            };
        }

        _scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            if (_scrollTop > 0 && Mathf.Abs(_scrollView.scrollOffset.y - (float)_scrollTop) > 0.5f)
            {
                _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, (float)_scrollTop);
            }
        });

        // Move existing children into _scrollView.contentContainer
        if (_textLabel != null && _textLabel.parent == ve)
        {
            _textLabel.RemoveFromHierarchy();
            _scrollView.contentContainer.Add(_textLabel);
        }
        for (int i = 0; i < _children.Count; i++)
        {
            var c = _children[i];
            if (c.ve != null && c.ve.parent == ve)
            {
                c.ve.RemoveFromHierarchy();
                _scrollView.contentContainer.Add(c.ve);
            }
        }
        ve.Add(_scrollView);
    }

    internal void OnClassTokensModified()
    {
        var tokens = classList.Tokens;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (ScrollableClasses.Contains(tokens[i])) EnsureScrollView();
            if (DisplayFontClasses.Contains(tokens[i])) UseDisplayFont();
        }

        ApplyDisplay();
        ApplyDirectChildFixups();
        UpdatePickingMode();
        parentElement?.ReorderChildrenByZIndex();
        CssAnimations.OnClassesChanged(this);
    }

    /// <summary>
    /// Works out the inline display the way a browser ranks it. The page's <c>.hidden { display: none !important; }</c> beats
    /// everything, but USS has no !important, so a later rule such as <c>.camp { display: flex }</c> would win over the class;
    /// inline styles beat every USS selector, so the class is applied here instead. Next comes an inline style.display set by
    /// the game, then the hidden attribute, then the stylesheet.
    /// </summary>
    internal void ApplyDisplay()
    {
        if (ve == null) return;
        string d = style?.getPropertyValue("display");
        if (classList != null && classList.contains("hidden")) ve.style.display = DisplayStyle.None;
        else if (!string.IsNullOrEmpty(d))
            ve.style.display = string.Equals(d, "none", StringComparison.OrdinalIgnoreCase) ? DisplayStyle.None : DisplayStyle.Flex;
        else ve.style.display = _hidden ? DisplayStyle.None : StyleKeyword.Null;
    }

    private void ApplyDirectChildFixups()
    {
        if (parentElement != null && parentElement.classList.contains("camp-body") && classList.contains("btn"))
        {
            if (ve != null) ve.style.alignSelf = Align.FlexStart;
        }
    }

    internal void UpdatePickingMode()
    {
        if (ve == null) return;
        bool pickable = ComputeIsPickable();
        ve.pickingMode = pickable ? PickingMode.Position : PickingMode.Ignore;
        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].UpdatePickingMode();
        }
    }

    private bool ComputeIsPickable()
    {
        if (_id == "scene" || tagName == "HTML" || tagName == "BODY")
            return false;

        // If self or any ancestor has .gone, hidden, or pointer-events: none -> not pickable
        var cur = this;
        while (cur != null)
        {
            if (cur._hidden || cur.classList.contains("gone") || cur.classList.contains("hidden"))
                return false;
            string pe = cur.style?.pointerEvents;
            if (string.Equals(pe, "none", StringComparison.OrdinalIgnoreCase))
                return false;
            cur = cur.parentElement;
        }

        if (string.Equals(style?.pointerEvents, "auto", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if this element itself is in PointerEventsNone (#ui, .hud, .prompt, .result, .fade, .binoc, .map-cursor, .padtools, .sleepveil, .dive-tint)
        if (PointerNoneSet.Contains(_id))
            return false;
        var tokens = classList.Tokens;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (PointerNoneSet.Contains(tokens[i]))
                return false;
        }

        // Interactive element types or elements with click listeners
        if (tagName == "BUTTON" || tagName == "INPUT" || tagName == "SELECT" || tagName == "OPTION")
            return true;
        if (onclick != null || HasPointerListeners())
            return true;

        // Check if this element or any ancestor is an interactive container (.camp, .intro-card, .card, .inspect, .chip, .intro, .pause, .mainmenu, #boot)
        var anc = this;
        while (anc != null)
        {
            if (anc._id == "boot") return true;
            var ancTokens = anc.classList.Tokens;
            for (int i = 0; i < ancTokens.Count; i++)
            {
                if (InteractiveContainerClasses.Contains(ancTokens[i]))
                    return true;
            }
            anc = anc.parentElement;
        }

        return false;
    }

    private bool HasPointerListeners()
    {
        if (onpointerdown != null || onpointerenter != null || onpointerleave != null || onwheel != null)
            return true;
        return _listeners.ContainsKey("click") ||
               _listeners.ContainsKey("pointerdown") ||
               _listeners.ContainsKey("pointerup") ||
               _listeners.ContainsKey("pointermove") ||
               _listeners.ContainsKey("wheel");
    }

    internal int GetEffectiveZIndex()
    {
        if (!string.IsNullOrEmpty(style?.zIndex) &&
            int.TryParse(style.zIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inlineZ))
        {
            return inlineZ;
        }
        int best = 0;
        if (!string.IsNullOrEmpty(_id) && CssAnimations.ZIndex.TryGetValue(_id, out int idZ))
        {
            if (idZ > best) best = idZ;
        }
        var tokens = classList.Tokens;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (CssAnimations.ZIndex.TryGetValue(tokens[i], out int clsZ) && clsZ > best)
            {
                best = clsZ;
            }
        }
        return best;
    }

    internal void ReorderChildrenByZIndex()
    {
        var container = ChildContainer;
        if (container == null || _children.Count <= 1) return;

        bool anyNonZero = false;
        for (int i = 0; i < _children.Count; i++)
        {
            if (_children[i].GetEffectiveZIndex() != 0)
            {
                anyNonZero = true;
                break;
            }
        }
        if (!anyNonZero) return;

        // Stable sort by (zIndex, domIndex)
        var ordered = new List<(HTMLElement el, int z, int idx)>(_children.Count);
        for (int i = 0; i < _children.Count; i++)
        {
            var c = _children[i];
            if (c is HTMLOptionElement) continue;
            ordered.Add((c, c.GetEffectiveZIndex(), i));
        }
        ordered.Sort((a, b) =>
        {
            int cmp = a.z.CompareTo(b.z);
            return cmp != 0 ? cmp : a.idx.CompareTo(b.idx);
        });

        for (int i = 0; i < ordered.Count; i++)
        {
            var cve = ordered[i].el.ve;
            if (cve != null && cve.parent == container)
            {
                cve.BringToFront();
            }
        }
    }

    internal float GetEffectiveFontSize()
    {
        var cur = this;
        while (cur != null)
        {
            if (cur.classList.contains("tb-ico")) return 24f;
            if (cur.classList.contains("pt")) return 22f;
            if (cur.classList.contains("mm-ico")) return 22f;
            if (cur.classList.contains("intro-first")) return 20f;
            if (cur.ve != null && cur.ve.panel != null)
            {
                float fs = cur.ve.resolvedStyle.fontSize;
                if (!float.IsNaN(fs) && fs > 1f) return fs;
            }
            cur = cur.parentElement;
        }
        return 16.36f;
    }

    // ------------------------------------------------------------------ Tree manipulation

    public void append(params object[] nodes)
    {
        if (nodes == null) return;
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            if (n == null) continue;
            if (n is HTMLElement el)
            {
                appendChild(el);
            }
            else if (n is string s)
            {
                if (_children.Count == 0 && string.IsNullOrEmpty(_directText))
                {
                    SetDirectText(s, isRich: false);
                }
                else
                {
                    var textNode = new HTMLElement("#text");
                    if (textNode.ve != null) textNode.ve.style.flexShrink = 1;
                    textNode.SetDirectText(s, isRich: false);
                    appendChild(textNode);
                }
            }
        }
    }

    public void prepend(params object[] nodes)
    {
        if (nodes == null || nodes.Length == 0) return;
        HTMLElement first = firstElementChild;
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            if (n == null) continue;
            HTMLElement toInsert = n as HTMLElement;
            if (toInsert == null && n is string s)
            {
                toInsert = new HTMLElement("#text");
                if (toInsert.ve != null) toInsert.ve.style.flexShrink = 1;
                toInsert.SetDirectText(s, isRich: false);
            }
            if (toInsert != null)
            {
                if (first != null) insertBefore(toInsert, first);
                else appendChild(toInsert);
            }
        }
    }

    public virtual HTMLElement appendChild(HTMLElement c)
    {
        if (c == null || c == this) return c;
        c.remove();
        c.parentElement = this;
        _children.Add(c);

        if (!(c is HTMLOptionElement))
        {
            var container = ChildContainer;
            if (container != null && c.ve != null)
            {
                container.Add(c.ve);
            }
        }

        OnChildAdded(c);
        return c;
    }

    public virtual void insertBefore(HTMLElement c, HTMLElement before)
    {
        if (c == null || c == this) return;
        if (before == null || before.parentElement != this)
        {
            appendChild(c);
            return;
        }
        c.remove();
        int idx = _children.IndexOf(before);
        if (idx < 0)
        {
            appendChild(c);
            return;
        }
        c.parentElement = this;
        _children.Insert(idx, c);

        if (!(c is HTMLOptionElement))
        {
            var container = ChildContainer;
            if (container != null && c.ve != null)
            {
                int veIdx = before.ve != null && before.ve.parent == container ? container.IndexOf(before.ve) : -1;
                if (veIdx >= 0) container.Insert(veIdx, c.ve);
                else container.Add(c.ve);
            }
        }

        OnChildAdded(c);
    }

    public void after(params object[] nodes)
    {
        if (parentElement == null || nodes == null) return;
        var next = nextElementSibling;
        for (int i = 0; i < nodes.Length; i++)
        {
            var el = nodes[i] as HTMLElement;
            if (el == null && nodes[i] is string s)
            {
                el = document.createElement("span");
                el.SetDirectText(s, isRich: false);
            }
            if (el != null)
            {
                if (next != null) parentElement.insertBefore(el, next);
                else parentElement.appendChild(el);
            }
        }
    }

    public void before(params object[] nodes)
    {
        if (parentElement == null || nodes == null) return;
        for (int i = 0; i < nodes.Length; i++)
        {
            var el = nodes[i] as HTMLElement;
            if (el == null && nodes[i] is string s)
            {
                el = document.createElement("span");
                el.SetDirectText(s, isRich: false);
            }
            if (el != null)
            {
                parentElement.insertBefore(el, this);
            }
        }
    }

    public void replaceChildren(params object[] nodes)
    {
        ClearAllContent();
        if (nodes != null && nodes.Length > 0)
        {
            append(nodes);
        }
    }

    public void remove()
    {
        parentElement?.removeChild(this);
    }

    public virtual HTMLElement removeChild(HTMLElement c)
    {
        if (c == null || c.parentElement != this) return c;
        if (document.activeElement != null && c.contains(document.activeElement))
        {
            document.activeElement = null;
        }
        _children.Remove(c);
        c.parentElement = null;
        c.ve?.RemoveFromHierarchy();
        return c;
    }

    protected virtual void OnChildAdded(HTMLElement c)
    {
        c.ApplyDirectChildFixups();
        c.UpdatePickingMode();
        ReorderChildrenByZIndex();
        NotifyAttachedSubtree(c);
    }

    private static void NotifyAttachedSubtree(HTMLElement root)
    {
        if (root == null) return;
        CssAnimations.OnAttached(root);
        for (int i = 0; i < root._children.Count; i++)
        {
            NotifyAttachedSubtree(root._children[i]);
        }
    }

    // ------------------------------------------------------------------ Selectors & hierarchy

    public T querySelector<T>(string selector) where T : HTMLElement => SelectorEngine.QuerySelector<T>(this, selector);
    public HTMLElement querySelector(string selector) => SelectorEngine.QuerySelector<HTMLElement>(this, selector);
    public List<T> querySelectorAll<T>(string selector) where T : HTMLElement => SelectorEngine.QuerySelectorAll<T>(this, selector);
    public List<HTMLElement> querySelectorAll(string selector) => SelectorEngine.QuerySelectorAll<HTMLElement>(this, selector);
    public HTMLElement closest(string selector) => SelectorEngine.Closest(this, selector);
    public T closest<T>(string selector) where T : HTMLElement => SelectorEngine.Closest(this, selector) as T;
    public bool matches(string selector) => SelectorEngine.Matches(this, selector);

    public bool contains(HTMLElement other)
    {
        var cur = other;
        while (cur != null)
        {
            if (cur == this) return true;
            cur = cur.parentElement;
        }
        return false;
    }

    public int compareDocumentPosition(HTMLElement other)
    {
        if (other == null || other == this) return 0;
        if (contains(other)) return Node.DOCUMENT_POSITION_CONTAINED_BY | Node.DOCUMENT_POSITION_FOLLOWING;
        if (other.contains(this)) return Node.DOCUMENT_POSITION_CONTAINS | Node.DOCUMENT_POSITION_PRECEDING;

        var aAnc = new List<HTMLElement>();
        for (var p = this; p != null; p = p.parentElement) aAnc.Add(p);
        var bAnc = new List<HTMLElement>();
        for (var p = other; p != null; p = p.parentElement) bAnc.Add(p);

        if (aAnc[aAnc.Count - 1] != bAnc[bAnc.Count - 1])
            return Node.DOCUMENT_POSITION_DISCONNECTED;

        int ia = aAnc.Count - 1, ib = bAnc.Count - 1;
        while (ia > 0 && ib > 0 && aAnc[ia - 1] == bAnc[ib - 1])
        {
            ia--;
            ib--;
        }
        var common = aAnc[ia];
        int idxA = common._children.IndexOf(aAnc[ia - 1]);
        int idxB = common._children.IndexOf(bAnc[ib - 1]);
        return idxA < idxB ? Node.DOCUMENT_POSITION_FOLLOWING : Node.DOCUMENT_POSITION_PRECEDING;
    }

    // ------------------------------------------------------------------ Focus & Click

    public virtual void focus()
    {
        if (_disabled) return;
        document.activeElement = this;
        ve?.Focus();
    }

    public void focus(object opts) => focus();

    public virtual void blur()
    {
        if (document.activeElement == this)
        {
            document.activeElement = null;
        }
        ve?.Blur();
    }

    public virtual void click()
    {
        if (_disabled) return;
        OnDefaultClickAction();
        onclick?.Invoke();
        var ev = new MouseEvent("click") { target = this, currentTarget = this, bubbles = true };
        DispatchToListeners("click", ev);
        if (ev.bubbles && !ev.propagationStopped && parentElement != null)
        {
            parentElement.BubbleEvent(ev);
        }
    }

    protected virtual void OnDefaultClickAction()
    {
        // <label> forwards click to contained input or select if not already clicked directly
        if (tagName == "LABEL")
        {
            var ctrl = querySelector<HTMLElement>("input, select");
            if (ctrl != null && !ctrl.disabled)
            {
                ctrl.focus();
                ctrl.click();
            }
        }
    }

    // ------------------------------------------------------------------ Geometry & Scroll

    internal bool IsHiddenInHierarchy()
    {
        var cur = this;
        while (cur != null)
        {
            if (cur._hidden || cur.classList.contains("hidden"))
                return true;
            if (string.Equals(cur.style?.display, "none", StringComparison.OrdinalIgnoreCase))
                return true;
            if (cur.ve != null && cur.ve.style.display.keyword == StyleKeyword.Undefined && cur.ve.style.display.value == DisplayStyle.None)
                return true;
            cur = cur.parentElement;
        }
        return false;
    }

    public DOMRect getBoundingClientRect()
    {
        if (!isConnected || IsHiddenInHierarchy())
        {
            return new DOMRect();
        }

        if (ve != null)
        {
            Rect wb = ve.worldBound;
            if (!float.IsNaN(wb.width) && !float.IsNaN(wb.height) && wb.width > 0.01f && wb.height > 0.01f)
            {
                return new DOMRect
                {
                    left = wb.xMin,
                    top = wb.yMin,
                    right = wb.xMax,
                    bottom = wb.yMax,
                    width = wb.width,
                    height = wb.height,
                    x = wb.xMin,
                    y = wb.yMin,
                };
            }
        }

        // Pre-layout fallback when element was just attached this frame and UI Toolkit layout has not run yet
        double fallbackW = TryGetFallbackSize(style?.width, this is HTMLCanvasElement hc ? hc.width : 120.0);
        double fallbackH = TryGetFallbackSize(style?.height, this is HTMLCanvasElement hc2 ? hc2.height : 32.0);
        int domOrder = ComputePreOrderOrdinal();
        double fallbackLeft = classList.contains("bigmap-canvas")
            ? Math.Max(0, (window.innerWidth - fallbackW) * 0.5)
            : 40.0 + (domOrder % 4) * 4.0;
        double fallbackTop = classList.contains("bigmap-canvas")
            ? Math.Max(0, (window.innerHeight - fallbackH) * 0.5)
            : 40.0 + domOrder * 36.0;

        return new DOMRect
        {
            left = fallbackLeft,
            top = fallbackTop,
            right = fallbackLeft + fallbackW,
            bottom = fallbackTop + fallbackH,
            width = fallbackW,
            height = fallbackH,
            x = fallbackLeft,
            y = fallbackTop,
        };
    }

    private static double TryGetFallbackSize(string cssVal, double defaultSize)
    {
        if (!string.IsNullOrWhiteSpace(cssVal))
        {
            string s = cssVal.Trim();
            if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 2).Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
                return v;
        }
        return defaultSize;
    }

    private int ComputePreOrderOrdinal()
    {
        int ord = 0;
        var cur = this;
        int depth = 0;
        while (cur?.parentElement != null && depth < 16)
        {
            int idx = cur.parentElement._children.IndexOf(cur);
            if (idx > 0) ord += idx * (depth == 0 ? 1 : 8);
            cur = cur.parentElement;
            depth++;
        }
        return ord;
    }

    public double clientWidth => getBoundingClientRect().width;
    public double clientHeight => getBoundingClientRect().height;
    public double offsetWidth => getBoundingClientRect().width;
    public double offsetHeight => getBoundingClientRect().height;

    public double scrollTop
    {
        get
        {
            if (_scrollView != null)
            {
                float wy = _scrollView.contentContainer.worldBound.height;
                if (!float.IsNaN(wy) && wy > 1f)
                    return _scrollView.scrollOffset.y;
            }
            return _scrollTop;
        }
        set
        {
            _scrollTop = Math.Max(0.0, value);
            if (_scrollView != null)
            {
                _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, (float)_scrollTop);
            }
        }
    }

    public double scrollHeight
    {
        get
        {
            if (_scrollView != null)
            {
                float ch = _scrollView.contentContainer.worldBound.height;
                if (!float.IsNaN(ch) && ch > 1f)
                    return ch;
                return Math.Max(clientHeight, _children.Count * 40.0);
            }
            return clientHeight;
        }
    }

    public void scrollBy(double x, double y)
    {
        scrollTop = Math.Max(0.0, scrollTop + y);
    }

    public void scrollTo(double x, double y)
    {
        scrollTop = Math.Max(0.0, y);
    }

    public void scrollIntoView(object opts = null)
    {
        var scroller = parentElement;
        while (scroller != null && scroller._scrollView == null)
        {
            scroller = scroller.parentElement;
        }
        if (scroller?._scrollView != null && ve != null)
        {
            try
            {
                scroller._scrollView.ScrollTo(ve);
            }
            catch
            {
                // Ignore if layout is not yet ready
            }
        }
    }

    // ------------------------------------------------------------------ Attributes

    internal void SetRawAttribute(string n, string v)
    {
        if (string.IsNullOrEmpty(n)) return;
        if (v == null) _attrs.Remove(n);
        else _attrs[n] = v;
    }

    public virtual void setAttribute(string n, string v)
    {
        if (string.IsNullOrEmpty(n)) return;
        string val = v ?? "";
        _attrs[n] = val;

        if (n.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
        {
            dataset.SetFromDataAttribute(n, val);
            return;
        }

        switch (n.ToLowerInvariant())
        {
            case "id": id = val; break;
            case "class": className = val; break;
            case "style": style.cssText = val; break;
            case "title": title = val; break;
            case "hidden": hidden = true; break;
            case "disabled": disabled = !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase); break;
            case "tabindex":
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ti))
                    tabIndex = ti;
                break;
        }
    }

    public virtual string getAttribute(string n)
    {
        if (string.IsNullOrEmpty(n)) return null;
        if (_attrs.TryGetValue(n, out var v)) return v;
        return null;
    }

    public virtual void removeAttribute(string n)
    {
        if (string.IsNullOrEmpty(n)) return;
        _attrs.Remove(n);
        if (n.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
        {
            dataset.SetFromDataAttribute(n, null);
            return;
        }
        switch (n.ToLowerInvariant())
        {
            case "id": id = ""; break;
            case "class": className = ""; break;
            case "style": style.cssText = ""; break;
            case "title": title = ""; break;
            case "hidden": hidden = false; break;
            case "disabled": disabled = false; break;
        }
    }

    // ------------------------------------------------------------------ Events

    public void addEventListener(string type, Action<Event> fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        if (!_listeners.TryGetValue(type, out var list))
        {
            list = new List<Action<Event>>();
            _listeners[type] = list;
        }
        if (!list.Contains(fn)) list.Add(fn);
        UpdatePickingMode();
    }

    public void addEventListener(string type, Action fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        addEventListener(type, _ => fn());
    }

    public void removeEventListener(string type, Action<Event> fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        if (_listeners.TryGetValue(type, out var list))
        {
            list.Remove(fn);
        }
    }

    public bool dispatchEvent(Event e)
    {
        if (e == null || string.IsNullOrEmpty(e.type)) return true;
        if (e.target == null) e.target = this;
        e.currentTarget = this;

        if (string.Equals(e.type, "click", StringComparison.OrdinalIgnoreCase))
        {
            if (!_disabled)
            {
                OnDefaultClickAction();
                onclick?.Invoke();
            }
        }
        else if (string.Equals(e.type, "input", StringComparison.OrdinalIgnoreCase))
        {
            oninput?.Invoke(e);
        }
        else if (string.Equals(e.type, "change", StringComparison.OrdinalIgnoreCase))
        {
            onchange?.Invoke(e);
        }
        else if (string.Equals(e.type, "keydown", StringComparison.OrdinalIgnoreCase))
        {
            onkeydown?.Invoke(e);
        }
        else if (string.Equals(e.type, "pointerdown", StringComparison.OrdinalIgnoreCase))
        {
            onpointerdown?.Invoke(e);
        }
        else if (string.Equals(e.type, "wheel", StringComparison.OrdinalIgnoreCase))
        {
            onwheel?.Invoke(e);
        }

        DispatchToListeners(e.type, e);

        if (e.bubbles && !e.propagationStopped && parentElement != null)
        {
            parentElement.BubbleEvent(e);
        }
        return !e.defaultPrevented;
    }

    internal void BubbleEvent(Event e)
    {
        var cur = this;
        while (cur != null && !e.propagationStopped)
        {
            e.currentTarget = cur;
            if (string.Equals(e.type, "click", StringComparison.OrdinalIgnoreCase) && !cur._disabled)
            {
                if (cur.tagName == "LABEL" && e.target != cur)
                {
                    var ctrl = cur.querySelector<HTMLElement>("input, select");
                    if (ctrl != null && e.target != ctrl && !ctrl.contains(e.target) && !ctrl.disabled)
                    {
                        ctrl.focus();
                        ctrl.click();
                    }
                }
                cur.onclick?.Invoke();
            }
            else if (string.Equals(e.type, "keydown", StringComparison.OrdinalIgnoreCase))
            {
                cur.onkeydown?.Invoke(e);
            }
            cur.DispatchToListeners(e.type, e);
            cur = cur.parentElement;
        }
    }

    internal void DispatchToListeners(string type, Event e)
    {
        if (_listeners.TryGetValue(type, out var list) && list.Count > 0)
        {
            var copy = list.ToArray();
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i]?.Invoke(e);
                if (e.propagationStopped) break;
            }
        }
    }

    private void RegisterBaseEvents()
    {
        if (ve == null) return;

        ve.RegisterCallback<ClickEvent>(evt =>
        {
            if (_disabled) return;
            evt.StopPropagation();
            var domEvt = new MouseEvent("click")
            {
                target = this,
                currentTarget = this,
                clientX = evt.position.x,
                clientY = evt.position.y,
                button = evt.button,
                shiftKey = evt.shiftKey,
                ctrlKey = evt.ctrlKey,
                altKey = evt.altKey,
                bubbles = true,
            };
            OnDefaultClickAction();
            onclick?.Invoke();
            DispatchToListeners("click", domEvt);
            if (!domEvt.propagationStopped && parentElement != null)
            {
                parentElement.BubbleEvent(domEvt);
            }
        });

        ve.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (_disabled) return;
            if (ve.focusable) focus();
            var domEvt = new PointerEvent("pointerdown")
            {
                target = this,
                currentTarget = this,
                clientX = evt.position.x,
                clientY = evt.position.y,
                button = evt.button,
                buttons = evt.pressedButtons,
                shiftKey = evt.shiftKey,
                ctrlKey = evt.ctrlKey,
                altKey = evt.altKey,
            };
            onpointerdown?.Invoke(domEvt);
            DispatchToListeners("pointerdown", domEvt);
        });

        ve.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (_disabled) return;
            var domEvt = new PointerEvent("pointerup")
            {
                target = this,
                currentTarget = this,
                clientX = evt.position.x,
                clientY = evt.position.y,
                button = evt.button,
                buttons = evt.pressedButtons,
                shiftKey = evt.shiftKey,
                ctrlKey = evt.ctrlKey,
                altKey = evt.altKey,
            };
            DispatchToListeners("pointerup", domEvt);
        });

        ve.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (_disabled) return;
            HoverFocus();
            if (!_listeners.ContainsKey("pointermove")) return;
            var domEvt = new PointerEvent("pointermove")
            {
                target = this,
                currentTarget = this,
                clientX = evt.position.x,
                clientY = evt.position.y,
                buttons = evt.pressedButtons,
                movementX = evt.deltaPosition.x,
                movementY = evt.deltaPosition.y,
            };
            DispatchToListeners("pointermove", domEvt);
        });

        ve.RegisterCallback<PointerEnterEvent>(evt =>
        {
            HoverFocus();
            if (onpointerenter == null && !_listeners.ContainsKey("pointerenter")) return;
            var domEvt = new PointerEvent("pointerenter")
            {
                target = this,
                currentTarget = this,
                clientX = evt.position.x,
                clientY = evt.position.y,
            };
            onpointerenter?.Invoke(domEvt);
            DispatchToListeners("pointerenter", domEvt);
        });

        ve.RegisterCallback<PointerLeaveEvent>(evt =>
        {
            if (onpointerleave == null && !_listeners.ContainsKey("pointerleave")) return;
            var domEvt = new PointerEvent("pointerleave")
            {
                target = this,
                currentTarget = this,
                clientX = evt.position.x,
                clientY = evt.position.y,
            };
            onpointerleave?.Invoke(domEvt);
            DispatchToListeners("pointerleave", domEvt);
        });

        ve.RegisterCallback<UnityEngine.UIElements.WheelEvent>(evt =>
        {
            if (onwheel == null && !_listeners.ContainsKey("wheel")) return;
            var domEvt = new WheelEvent("wheel")
            {
                target = this,
                currentTarget = this,
                clientX = evt.mousePosition.x,
                clientY = evt.mousePosition.y,
                deltaY = evt.delta.y * 40.0,
            };
            onwheel?.Invoke(domEvt);
            DispatchToListeners("wheel", domEvt);
        });

        ve.RegisterCallback<KeyDownEvent>(evt =>
        {
            var domEvt = new KeyboardEvent("keydown")
            {
                target = this,
                currentTarget = this,
                key = evt.character != '\0' ? evt.character.ToString() : evt.keyCode.ToString(),
                code = evt.keyCode.ToString(),
                shiftKey = evt.shiftKey,
                ctrlKey = evt.ctrlKey,
                altKey = evt.altKey,
                bubbles = true,
            };
            onkeydown?.Invoke(domEvt);
            DispatchToListeners("keydown", domEvt);
            if (domEvt.propagationStopped)
            {
                evt.StopPropagation();
            }
        });

        // focus events bubble in UI Toolkit: only the element that itself gained or lost focus may claim it, or a container
        // with this handler took the focus from the button inside it (a controller then could not move past the first button)
        ve.RegisterCallback<FocusInEvent>(e =>
        {
            if (e.target == ve) document.activeElement = this;
        });

        ve.RegisterCallback<FocusOutEvent>(e =>
        {
            if (e.target == ve && document.activeElement == this)
            {
                document.activeElement = null;
            }
        });
    }

    public void requestPointerLock()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
    }

    public void requestFullscreen()
    {
        document.requestFullscreen();
    }
}

public class HTMLButtonElement : HTMLElement
{
    public string type = "button";

    public HTMLButtonElement() : base("BUTTON") { }

    public new bool disabled
    {
        get => base.disabled;
        set => base.disabled = value;
    }

    public override void setAttribute(string n, string v)
    {
        base.setAttribute(n, v);
        if (string.Equals(n, "type", StringComparison.OrdinalIgnoreCase))
            type = v ?? "button";
    }
}

public class HTMLInputElement : HTMLElement
{
    private string _type = "text";
    private string _value = "";
    private string _min = "0";
    private string _max = "100";
    private string _step = "1";
    private string _placeholder = "";
    private bool _checked = false;
    private int _maxLength = -1;

    // Range UI
    private VisualElement _rangeTrack;
    private VisualElement _rangeFill;
    private VisualElement _rangeThumb;
    private bool _draggingRange;

    // Checkbox UI
    private Label _checkMark;

    // Text UI
    private TextField _textField;
    private Label _placeholderLabel;

    public HTMLInputElement() : base("INPUT")
    {
        RebuildControlForType();
    }

    public string type
    {
        get => _type;
        set
        {
            string norm = string.IsNullOrWhiteSpace(value) ? "text" : value.Trim().ToLowerInvariant();
            if (_type == norm && (_rangeTrack != null || _checkMark != null || _textField != null)) return;
            _type = norm;
            RebuildControlForType();
        }
    }

    public string value
    {
        get => _value;
        set
        {
            _value = value ?? "";
            SyncControlVisuals();
        }
    }

    public string min
    {
        get => _min;
        set { _min = value ?? "0"; SyncControlVisuals(); }
    }

    public string max
    {
        get => _max;
        set { _max = value ?? "100"; SyncControlVisuals(); }
    }

    public string step
    {
        get => _step;
        set { _step = value ?? "1"; SyncControlVisuals(); }
    }

    public string placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value ?? "";
            SyncControlVisuals();
        }
    }

    public bool @checked
    {
        get => _checked;
        set
        {
            _checked = value;
            ve?.EnableInClassList("checked", _checked);
            SyncControlVisuals();
        }
    }

    public new bool disabled
    {
        get => base.disabled;
        set => base.disabled = value;
    }

    public int maxLength
    {
        get => _maxLength;
        set
        {
            _maxLength = value;
            if (_textField != null) _textField.maxLength = value > 0 ? value : -1;
        }
    }

    public double valueAsNumber
    {
        get => double.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN;
        set => this.value = JS.Str(value);
    }

    public void select()
    {
        focus();
        _textField?.SelectAll();
    }

    public void stepUp(int n = 1) => AdjustStep(n);
    public void stepDown(int n = 1) => AdjustStep(-n);

    private void AdjustStep(int dir)
    {
        double mn = ParseDouble(_min, 0);
        double mx = ParseDouble(_max, 100);
        double st = ParseDouble(_step, 1);
        if (st <= 0) st = 1;
        double cur = ParseDouble(_value, mn);
        double next = Math.Max(mn, Math.Min(mx, cur + dir * st));
        value = JS.Str(next);
    }

    public override void focus()
    {
        base.focus();
        if (_type == "text" && _textField != null)
        {
            _textField.Focus();
        }
    }

    protected override void OnDefaultClickAction()
    {
        if (_type == "checkbox" && !_disabled)
        {
            @checked = !@checked;
            var inEv = new Event("input") { target = this, currentTarget = this, bubbles = true };
            oninput?.Invoke(inEv);
            DispatchToListeners("input", inEv);
            var chEv = new Event("change") { target = this, currentTarget = this, bubbles = true };
            onchange?.Invoke(chEv);
            DispatchToListeners("change", chEv);
        }
    }

    public override void setAttribute(string n, string v)
    {
        base.setAttribute(n, v);
        if (string.IsNullOrEmpty(n)) return;
        switch (n.ToLowerInvariant())
        {
            case "type": type = v; break;
            case "value": value = v; break;
            case "min": min = v; break;
            case "max": max = v; break;
            case "step": step = v; break;
            case "placeholder": placeholder = v; break;
            case "checked": @checked = !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase); break;
            case "maxlength":
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ml))
                    maxLength = ml;
                break;
        }
    }

    private void RebuildControlForType()
    {
        if (ve == null) return;
        ve.Clear();
        _rangeTrack = null;
        _rangeFill = null;
        _rangeThumb = null;
        _checkMark = null;
        _textField = null;
        _placeholderLabel = null;

        if (_type == "range")
        {
            // the look (track, fill and knob) is in Style.uss: .range-track, .range-fill, .range-thumb
            ve.AddToClassList("range");
            _rangeTrack = new VisualElement { pickingMode = PickingMode.Ignore };
            _rangeTrack.AddToClassList("range-track");
            _rangeFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _rangeFill.AddToClassList("range-fill");
            _rangeThumb = new VisualElement { pickingMode = PickingMode.Ignore };
            _rangeThumb.AddToClassList("range-thumb");

            _rangeTrack.Add(_rangeFill);
            _rangeTrack.Add(_rangeThumb);
            ve.Add(_rangeTrack);
            ElementByVe[_rangeTrack] = this;

            ve.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_type != "range" || _disabled) return;
                _draggingRange = true;
                ve.CapturePointer(evt.pointerId);
                UpdateRangeFromPointer(evt.position.x, fireInput: true, fireChange: false);
                evt.StopPropagation();
            });
            ve.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_type != "range" || !_draggingRange || _disabled) return;
                if (ve.HasPointerCapture(evt.pointerId))
                {
                    UpdateRangeFromPointer(evt.position.x, fireInput: true, fireChange: false);
                    evt.StopPropagation();
                }
            });
            ve.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_type != "range" || !_draggingRange) return;
                _draggingRange = false;
                if (ve.HasPointerCapture(evt.pointerId))
                {
                    ve.ReleasePointer(evt.pointerId);
                }
                UpdateRangeFromPointer(evt.position.x, fireInput: false, fireChange: true);
                evt.StopPropagation();
            });
        }
        else if (_type == "checkbox")
        {
            ve.style.alignItems = Align.Center;
            ve.style.justifyContent = Justify.Center;
            _checkMark = new Label("✓")
            {
                pickingMode = PickingMode.Ignore,
            };
            _checkMark.style.color = Color.white;
            _checkMark.style.fontSize = 14;
            _checkMark.style.unityFontStyleAndWeight = FontStyle.Bold;
            _checkMark.style.unityTextAlign = TextAnchor.MiddleCenter;
            _checkMark.style.marginTop = 0;
            _checkMark.style.marginBottom = 0;
            _checkMark.style.marginLeft = 0;
            _checkMark.style.marginRight = 0;
            _checkMark.style.paddingTop = 0;
            _checkMark.style.paddingBottom = 0;
            _checkMark.style.paddingLeft = 0;
            _checkMark.style.paddingRight = 0;
            ve.Add(_checkMark);
            ElementByVe[_checkMark] = this;
        }
        else // "text"
        {
            _textField = new TextField
            {
                label = "",
                maxLength = _maxLength > 0 ? _maxLength : -1,
                value = _value ?? "",
            };
            _textField.style.flexGrow = 1;
            _textField.style.marginTop = 0;
            _textField.style.marginBottom = 0;
            _textField.style.marginLeft = 0;
            _textField.style.marginRight = 0;
            _textField.style.backgroundColor = Color.clear;
            _textField.style.borderTopWidth = 0;
            _textField.style.borderRightWidth = 0;
            _textField.style.borderBottomWidth = 0;
            _textField.style.borderLeftWidth = 0;

            var innerInput = _textField.Q("unity-text-input");
            if (innerInput != null)
            {
                innerInput.style.backgroundColor = Color.clear;
                innerInput.style.borderTopWidth = 0;
                innerInput.style.borderRightWidth = 0;
                innerInput.style.borderBottomWidth = 0;
                innerInput.style.borderLeftWidth = 0;
                innerInput.style.paddingTop = 0;
                innerInput.style.paddingBottom = 0;
                innerInput.style.paddingLeft = 0;
                innerInput.style.paddingRight = 0;
                innerInput.style.color = new Color(0.23f, 0.16f, 0.12f, 1f);
                ElementByVe[innerInput] = this;
            }

            _placeholderLabel = new Label(_placeholder ?? "")
            {
                pickingMode = PickingMode.Ignore,
            };
            _placeholderLabel.style.position = Position.Absolute;
            _placeholderLabel.style.left = 12;
            _placeholderLabel.style.top = 10;
            _placeholderLabel.style.opacity = 0.45f;
            _placeholderLabel.style.color = new Color(0.23f, 0.16f, 0.12f, 1f);

            _textField.RegisterValueChangedCallback(evt =>
            {
                _value = evt.newValue ?? "";
                if (_placeholderLabel != null)
                    _placeholderLabel.style.display = string.IsNullOrEmpty(_value) ? DisplayStyle.Flex : DisplayStyle.None;
                var inEv = new Event("input") { target = this, currentTarget = this, bubbles = true };
                oninput?.Invoke(inEv);
                DispatchToListeners("input", inEv);
                var chEv = new Event("change") { target = this, currentTarget = this, bubbles = true };
                onchange?.Invoke(chEv);
                DispatchToListeners("change", chEv);
            });

            _textField.RegisterCallback<FocusInEvent>(_ =>
            {
                document.activeElement = this;
                DomHost._isTyping = true;
            });

            _textField.RegisterCallback<FocusOutEvent>(_ =>
            {
                DomHost._isTyping = false;
            });

            ElementByVe[_textField] = this;
            ve.Add(_placeholderLabel);
            ve.Add(_textField);
        }

        SyncControlVisuals();
    }

    private void UpdateRangeFromPointer(float panelX, bool fireInput, bool fireChange)
    {
        if (ve == null) return;
        Rect wb = ve.worldBound;
        float w = wb.width > 1f ? wb.width : 160f;
        float u = Mathf.Clamp01((panelX - wb.xMin) / w);

        double mn = ParseDouble(_min, 0);
        double mx = ParseDouble(_max, 100);
        double st = ParseDouble(_step, 0);
        double raw = mn + u * (mx - mn);
        if (st > 0)
        {
            raw = mn + Math.Round((raw - mn) / st) * st;
        }
        raw = Math.Max(mn, Math.Min(mx, raw));
        string nextStr = JS.Str(raw);
        bool changed = _value != nextStr;
        _value = nextStr;
        SyncControlVisuals();

        if (fireInput && changed)
        {
            var inEv = new Event("input") { target = this, currentTarget = this, bubbles = true };
            oninput?.Invoke(inEv);
            DispatchToListeners("input", inEv);
        }
        if (fireChange)
        {
            var chEv = new Event("change") { target = this, currentTarget = this, bubbles = true };
            onchange?.Invoke(chEv);
            DispatchToListeners("change", chEv);
        }
    }

    private void SyncControlVisuals()
    {
        if (_type == "range" && _rangeFill != null && _rangeThumb != null)
        {
            double mn = ParseDouble(_min, 0);
            double mx = ParseDouble(_max, 100);
            double cur = ParseDouble(_value, mn);
            float pct = mx > mn ? Mathf.Clamp01((float)((cur - mn) / (mx - mn))) * 100f : 0f;
            _rangeFill.style.width = Length.Percent(pct);
            _rangeThumb.style.left = Length.Percent(pct);
        }
        else if (_type == "checkbox" && _checkMark != null)
        {
            ve?.EnableInClassList("checked", _checked);
            _checkMark.style.display = _checked ? DisplayStyle.Flex : DisplayStyle.None;
        }
        else if (_type == "text")
        {
            if (_textField != null && _textField.value != (_value ?? ""))
            {
                _textField.SetValueWithoutNotify(_value ?? "");
            }
            if (_placeholderLabel != null)
            {
                _placeholderLabel.text = _placeholder ?? "";
                _placeholderLabel.style.display = string.IsNullOrEmpty(_value) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }

    private static double ParseDouble(string s, double def)
    {
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : def;
    }
}

public class HTMLOptionElement : HTMLElement
{
    private string _value;
    private string _text;
    private bool _selected;

    public HTMLOptionElement() : base("OPTION") { }

    public string value
    {
        get => _value ?? text;
        set
        {
            _value = value;
            if (parentElement is HTMLSelectElement sel) sel.SyncFromOptions();
        }
    }

    public string text
    {
        get => !string.IsNullOrEmpty(_text) ? _text : base.textContent;
        set
        {
            _text = value;
            base.textContent = value;
            if (parentElement is HTMLSelectElement sel) sel.SyncFromOptions();
        }
    }

    public override string textContent
    {
        get => base.textContent;
        set
        {
            _text = value;
            base.textContent = value;
            if (parentElement is HTMLSelectElement sel) sel.SyncFromOptions();
        }
    }

    public bool selected
    {
        get => _selected;
        set
        {
            _selected = value;
            if (value && parentElement is HTMLSelectElement sel)
            {
                int idx = sel.options.IndexOf(this);
                if (idx >= 0) sel.selectedIndex = idx;
            }
        }
    }

    public override void setAttribute(string n, string v)
    {
        base.setAttribute(n, v);
        if (string.Equals(n, "value", StringComparison.OrdinalIgnoreCase)) value = v;
        else if (string.Equals(n, "selected", StringComparison.OrdinalIgnoreCase)) selected = true;
    }
}

public class HTMLSelectElement : HTMLElement
{
    public List<HTMLOptionElement> options = new List<HTMLOptionElement>();
    private int _selectedIndex = -1;
    private readonly Label _valueLabel;
    private readonly Label _arrowLabel;
    private HTMLElement _openDropdown;

    public HTMLSelectElement() : base("SELECT")
    {
        ve.style.flexDirection = FlexDirection.Row;
        ve.style.alignItems = Align.Center;
        ve.style.justifyContent = Justify.SpaceBetween;

        _valueLabel = new Label("") { pickingMode = PickingMode.Ignore };
        _valueLabel.style.marginTop = 0;
        _valueLabel.style.marginBottom = 0;
        _valueLabel.style.marginLeft = 0;
        _valueLabel.style.marginRight = 0;
        _valueLabel.style.paddingTop = 0;
        _valueLabel.style.paddingBottom = 0;
        _valueLabel.style.paddingLeft = 0;
        _valueLabel.style.paddingRight = 0;

        _arrowLabel = new Label("▾") { pickingMode = PickingMode.Ignore };
        _arrowLabel.style.marginTop = 0;
        _arrowLabel.style.marginBottom = 0;
        _arrowLabel.style.marginLeft = 8;
        _arrowLabel.style.marginRight = 0;
        _arrowLabel.style.opacity = 0.7f;

        ve.Add(_valueLabel);
        ve.Add(_arrowLabel);
        ElementByVe[_valueLabel] = this;
        ElementByVe[_arrowLabel] = this;
    }

    public new bool disabled
    {
        get => base.disabled;
        set => base.disabled = value;
    }

    public int selectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = (options.Count == 0) ? -1 : Math.Max(-1, Math.Min(options.Count - 1, value));
            _selectedIndex = clamped;
            for (int i = 0; i < options.Count; i++)
            {
                options[i].SetSelectedInternal(i == _selectedIndex);
            }
            UpdateDisplayedOption();
        }
    }

    public string value
    {
        get
        {
            if (_selectedIndex >= 0 && _selectedIndex < options.Count)
                return options[_selectedIndex].value ?? "";
            return "";
        }
        set
        {
            int found = -1;
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].value, value, StringComparison.Ordinal))
                {
                    found = i;
                    break;
                }
            }
            if (found >= 0) selectedIndex = found;
            else if (options.Count > 0 && _selectedIndex < 0) selectedIndex = 0;
            UpdateDisplayedOption();
        }
    }

    protected override void OnChildAdded(HTMLElement c)
    {
        base.OnChildAdded(c);
        SyncFromOptions();
    }

    public override HTMLElement removeChild(HTMLElement c)
    {
        var r = base.removeChild(c);
        SyncFromOptions();
        return r;
    }

    internal void SyncFromOptions()
    {
        options.Clear();
        for (int i = 0; i < _children.Count; i++)
        {
            if (_children[i] is HTMLOptionElement opt)
            {
                options.Add(opt);
            }
        }
        if (_selectedIndex < 0 && options.Count > 0)
        {
            _selectedIndex = 0;
        }
        else if (_selectedIndex >= options.Count)
        {
            _selectedIndex = options.Count - 1;
        }
        UpdateDisplayedOption();
    }

    private void UpdateDisplayedOption()
    {
        if (_valueLabel == null) return;
        if (_selectedIndex >= 0 && _selectedIndex < options.Count)
        {
            var opt = options[_selectedIndex];
            _valueLabel.text = EmojiText.Apply(opt.text ?? opt.value ?? "");
        }
        else
        {
            _valueLabel.text = "";
        }
    }

    protected override void OnDefaultClickAction()
    {
        if (_disabled || options.Count == 0) return;

        // Toggle dropdown list below the row (matching .set-options / .set-option in Style.uss)
        var anchorRow = closest(".set-row") ?? this;
        var parent = anchorRow.parentElement;
        if (parent == null) return;

        var existing = parent.querySelector<HTMLElement>(".set-options");
        if (existing != null)
        {
            bool wasOurs = existing == _openDropdown;
            existing.remove();
            _openDropdown = null;
            if (wasOurs) return;
        }

        var list = document.createElement("div");
        list.className = "set-options";
        for (int idx = 0; idx < options.Count; idx++)
        {
            int i = idx;
            var o = options[i];
            var b = document.createElement<HTMLButtonElement>("button");
            b.className = i == _selectedIndex ? "set-option on" : "set-option";
            b.textContent = o.text ?? o.textContent;
            b.onclick = () =>
            {
                bool changed = i != _selectedIndex;
                selectedIndex = i;
                list.remove();
                _openDropdown = null;
                if (changed)
                {
                    var ev = new Event("change") { target = this, currentTarget = this, bubbles = true };
                    oninput?.Invoke(ev);
                    onchange?.Invoke(ev);
                    DispatchToListeners("input", ev);
                    DispatchToListeners("change", ev);
                }
            };
            list.append(b);
        }
        _openDropdown = list;
        anchorRow.after(list);
    }
}

internal static class HTMLOptionExtensions
{
    public static void SetSelectedInternal(this HTMLOptionElement opt, bool sel)
    {
        // Avoid recursive selectedIndex setter
        typeof(HTMLOptionElement)
            .GetField("_selected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(opt, sel);
    }
}

public class HTMLImageElement : HTMLElement
{
    private string _src = "";
    private string _alt = "";
    private Texture _texture;

    public HTMLImageElement() : base("IMG") { }

    public string src
    {
        get => _src;
        set
        {
            _src = value ?? "";
            if (!string.IsNullOrEmpty(_src) && _texture == null)
            {
                var loaded = Resources.Load<Texture>(_src);
                if (loaded != null) texture = loaded;
            }
        }
    }

    public string alt
    {
        get => _alt;
        set => _alt = value ?? "";
    }

    public Texture texture
    {
        get => _texture;
        set
        {
            _texture = value;
            ApplyTexture();
        }
    }

    private void ApplyTexture()
    {
        if (ve == null) return;
        if (_texture == null)
        {
            ve.style.backgroundImage = StyleKeyword.Null;
        }
        else if (_texture is Texture2D t2d)
        {
            ve.style.backgroundImage = new StyleBackground(t2d);
        }
        else if (_texture is RenderTexture rt)
        {
            ve.style.backgroundImage = Background.FromRenderTexture(rt);
        }
    }

    public override void setAttribute(string n, string v)
    {
        base.setAttribute(n, v);
        if (string.Equals(n, "src", StringComparison.OrdinalIgnoreCase)) src = v;
        else if (string.Equals(n, "alt", StringComparison.OrdinalIgnoreCase)) alt = v;
    }
}

public class HTMLParagraphElement : HTMLElement
{
    public HTMLParagraphElement() : base("P") { }
}

public class HTMLDivElement : HTMLElement
{
    public HTMLDivElement() : base("DIV") { }
}

public class HTMLSpanElement : HTMLElement
{
    public HTMLSpanElement() : base("SPAN") { }
}

public class HTMLLabelElement : HTMLElement
{
    public HTMLLabelElement() : base("LABEL") { }
}

public class HTMLHeadingElement : HTMLElement
{
    public HTMLHeadingElement(string tag = "H1") : base(tag) { }
}

public class HTMLUListElement : HTMLElement
{
    public HTMLUListElement() : base("UL") { }
}

public class HTMLLIElement : HTMLElement
{
    public HTMLLIElement() : base("LI") { }
}
