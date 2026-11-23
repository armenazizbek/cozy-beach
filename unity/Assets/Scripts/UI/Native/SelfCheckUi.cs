using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

/// <summary>
/// Self-checks for the native UI: what the pointer would hit. The parity runs drive menus with keys and a virtual controller,
/// never a real mouse, so a menu that ignores the mouse passed them all; this asks UI Toolkit directly.
/// </summary>
internal static class SelfCheckUi
{
    internal static IEnumerable<(string, Func<string>)> Checks()
    {
        yield return ("Every button on screen takes the pointer", Buttons);
    }

    private static string Buttons()
    {
        var root = UiRoot.Root;
        if (root?.panel == null) return "no panel";
        var buttons = MenuNav.Find(root, v => v.ClassListContains("t-button"));
        int shown = 0;
        foreach (var b in buttons)
        {
            var box = b.Box;
            if (box.width <= 0 || box.height <= 0) continue;
            shown++;
            var hit = root.panel.Pick(box.center);
            bool ok = false;
            for (var v = hit; v != null; v = v.parent) if (v == b.ve) { ok = true; break; }
            if (!ok) return $"'{b.Text.Trim()}' does not take the pointer (picked: {(hit == null ? "nothing" : hit.name + " " + string.Join(".", hit.GetClasses()))})";
        }
        UnityEngine.Debug.Log($"[SelfCheck] pointer: {shown} buttons on screen, all take it");
        return shown == 0 ? "no buttons on screen to try" : null;
    }
}
