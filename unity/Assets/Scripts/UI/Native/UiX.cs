using System;
using UnityEngine.UIElements;

/// <summary>
/// Small helpers for native screens (unity/NATIVE_PLAN.md 2.7, S26). A native screen builds the same element structure
/// with the same class names as the DOM page did, so Style.uss keeps its look; these keep that short.
/// </summary>
public static class UiX
{
    /// <summary>A plain element with classes ("a b c"), as el("div", "a b c") made one.</summary>
    public static VisualElement El(string classes = null, string name = null)
    {
        var ve = new VisualElement();
        if (!string.IsNullOrEmpty(name)) ve.name = name;
        AddClasses(ve, classes);
        return ve;
    }

    /// <summary>A text element with classes.</summary>
    public static Label Text(string text, string classes = null)
    {
        var l = new Label(text ?? "");
        AddClasses(l, classes);
        return l;
    }

    /// <summary>A button with classes that runs `onClick`.</summary>
    public static Button Btn(string text, string classes, Action onClick)
    {
        var b = new Button(onClick) { text = text ?? "" };
        AddClasses(b, classes);
        return b;
    }

    /// <summary>Shown or hidden as the page's `.hidden` class did (display none, whatever other rules say).</summary>
    public static void Show(VisualElement ve, bool on)
    {
        if (ve == null) return;
        var d = on ? DisplayStyle.Flex : DisplayStyle.None;
        if (ve.style.display != d) ve.style.display = d;
    }

    /// <summary>Adds space-separated classes.</summary>
    public static void AddClasses(VisualElement ve, string classes)
    {
        if (string.IsNullOrEmpty(classes)) return;
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) ve.AddToClassList(c);
    }

    /// <summary>Adds or removes one class.</summary>
    public static void Toggle(VisualElement ve, string cls, bool on)
    {
        if (on) ve.AddToClassList(cls);
        else ve.RemoveFromClassList(cls);
    }
}
