using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One element the menu navigation can reach (unity/NATIVE_PLAN.md 2.7, S28): the game's own navigation (Game.padNav,
/// menuAccept, menuTick) walks these. Two wrappers of the same element are equal.
/// </summary>
public readonly struct NavEl : IEquatable<NavEl>
{
    public readonly UiNode node;

    public NavEl(UiNode node) { this.node = node; }

    public bool IsNull => node == null;
    public VisualElement ve => node?.ve;

    /// <summary>The element a VisualElement stands for (null for a part of one, such as its text).</summary>
    public static NavEl Of(VisualElement v)
    {
        if (v?.userData is UiNode n && n.ve == v) return new NavEl(n);
        return default;
    }

    /// <summary>A box to type in (a controller does not walk onto it unless Steam's keyboard can type for it).</summary>
    public bool IsTextBox => node is UiText;

    public bool Disabled => node != null && !node.ve.enabledSelf;
    public bool Has(string cls) => ve != null && ve.ClassListContains(cls);
    /// <summary>Its words and its children's (textContent).</summary>
    public string Text => node?.FullText ?? "";
    /// <summary>Left and right change it (a setting): -1 or 1.</summary>
    public Action<int> OnStep => node?.OnStep;

    public void Focus() => node?.Focus();

    public void Click() => node?.Click();

    /// <summary>Its box on the panel, empty when it is not shown (getBoundingClientRect).</summary>
    public Rect Box
    {
        get
        {
            if (node == null || node.ve.panel == null) return default;
            for (var p = node.ve; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return default;
            var wb = node.ve.worldBound;
            return float.IsNaN(wb.width) || wb.width <= 0.01f || wb.height <= 0.01f ? default : wb;
        }
    }

    /// <summary>How far it is scrolled down (scrollTop).</summary>
    public double ScrollTop
    {
        get => node?.ScrollTop ?? 0;
        set
        {
            if (node != null) node.ScrollTop = value;
        }
    }

    /// <summary>The height of all it holds, and the height shown (scrollHeight, clientHeight).</summary>
    public double ScrollHeight => node?.ScrollHeight ?? 0;
    public double ClientHeight => node?.ClientHeight ?? 0;

    /// <summary>Scrolls the list it is in so it shows.</summary>
    public void ScrollIntoView()
    {
        for (var p = node?.ve.parent; p != null; p = p.parent)
            if (p is ScrollView sv) { try { sv.ScrollTo(node.ve); } catch { } return; }
    }

    /// <summary>True when `other` is this element or inside it.</summary>
    public bool Contains(NavEl other)
    {
        for (var v = other.ve; v != null; v = v.parent) if (v == ve) return true;
        return false;
    }

    public bool Equals(NavEl o) => ReferenceEquals(ve, o.ve);
    public override bool Equals(object obj) => obj is NavEl o && Equals(o);
    public override int GetHashCode() => ve != null ? ve.GetHashCode() : 0;
    public static bool operator ==(NavEl a, NavEl b) => a.Equals(b);
    public static bool operator !=(NavEl a, NavEl b) => !a.Equals(b);
}

/// <summary>
/// The menu highlight: the chosen element. Queries on the panel's VisualElements, in panel order.
/// </summary>
public static class MenuNav
{
    private static UiNode nativeFocus;

    /// <summary>The chosen element (page or native), or nothing.</summary>
    public static NavEl Current
    {
        get
        {
            if (nativeFocus != null && nativeFocus.ve.panel != null) return new NavEl(nativeFocus);
            return default;
        }
    }

    /// <summary>A native element takes the highlight (UiNode.Focus).</summary>
    internal static void FocusNative(UiNode n) => nativeFocus = n;

    /// <summary>A page element took the highlight, or nothing has it.</summary>
    internal static void ForgetNative() => nativeFocus = null;

    /// <summary>The elements under `root` that carry any of these classes, in panel order (querySelectorAll on classes).</summary>
    public static List<NavEl> Find(VisualElement root, Func<VisualElement, bool> match)
    {
        var list = new List<NavEl>();
        if (root == null) return list;
        void walk(VisualElement v)
        {
            foreach (var c in v.Children())
            {
                if (match(c))
                {
                    var e = NavEl.Of(c);
                    if (!e.IsNull) list.Add(e);
                }
                walk(c);
            }
        }
        walk(root);
        return list;
    }
}
