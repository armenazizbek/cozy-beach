// Port of src/style.css @keyframes animations and CSS metadata for Unity UI Toolkit
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public static class CssAnimations
{
    /// <summary>
    /// CSS classes (and IDs) that declare <c>pointer-events: none</c> in <c>src/style.css</c> / <c>index.html</c>.
    /// The DOM layer should set <c>ve.pickingMode = PickingMode.Ignore</c> on elements with these classes.
    /// </summary>
    public static readonly string[] PointerEventsNone = new string[]
    {
        "ui",
        "gone",
        "prompt",
        "hud",
        "result",
        "fade",
        "binoc",
        "map-cursor",
        "padtools",
        "padbar",
        "sleepveil",
        "dive-tint",
        "book-spread-crease",
    };

    /// <summary>
    /// CSS classes (and IDs) that declare <c>z-index</c> in <c>src/style.css</c> / <c>index.html</c>,
    /// mapped to their CSS z-index value so the DOM layer can order siblings accordingly.
    /// </summary>
    public static readonly Dictionary<string, int> ZIndex = new Dictionary<string, int>
    {
        { "boot", 100 },
        { "padbar", 50 },
        { "map-cursor", 20 },
        { "achievements", 7 },
        { "helpscreen", 7 },
        { "farewell", 6 },
        { "credits", 6 },
        { "bookview", 6 },
        { "teaser", 6 },
        { "saves", 5 },
        { "sleepveil", 3 },
    };

    public static void Clear() => UiAnim.Clear();

    public static void OnAttached(HTMLElement el) => UiAnim.OnAttached(el?.ve);

    public static void OnClassesChanged(HTMLElement el) => UiAnim.OnClassesChanged(el?.ve);
}
