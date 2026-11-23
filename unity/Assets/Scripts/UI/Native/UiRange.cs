using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A slider, native (unity/NATIVE_PLAN.md 2.7, S29): what the page's &lt;input type="range"&gt; built (HTMLInputElement):
/// `.range` with a track, its fill and the knob (drawn by Style.uss: .range-track, .range-fill, .range-thumb), dragged
/// with the pointer, the value snapped to its step and kept as the page kept it (a number written as JS writes it).
/// </summary>
public sealed class UiRange : UiNode
{
    private readonly VisualElement track, fill, thumb;
    private bool dragging;
    private string value = "";

    public double Min = 0, Max = 100, Step = 1;

    /// <summary>Runs while it is dragged, when the value changes.</summary>
    public Action OnInput;

    public UiRange(string classes) : base("input", classes)
    {
        ve.AddToClassList("range");
        track = new VisualElement { pickingMode = PickingMode.Ignore };
        track.AddToClassList("range-track");
        fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.AddToClassList("range-fill");
        thumb = new VisualElement { pickingMode = PickingMode.Ignore };
        thumb.AddToClassList("range-thumb");
        track.Add(fill);
        track.Add(thumb);
        ve.Add(track);

        ve.RegisterCallback<PointerDownEvent>(e =>
        {
            if (Disabled) return;
            dragging = true;
            ve.CapturePointer(e.pointerId);
            FromPointer(e.position.x, true);
            e.StopPropagation();
        });
        ve.RegisterCallback<PointerMoveEvent>(e =>
        {
            if (!dragging || Disabled) return;
            if (ve.HasPointerCapture(e.pointerId))
            {
                FromPointer(e.position.x, true);
                e.StopPropagation();
            }
        });
        ve.RegisterCallback<PointerUpEvent>(e =>
        {
            if (!dragging) return;
            dragging = false;
            if (ve.HasPointerCapture(e.pointerId)) ve.ReleasePointer(e.pointerId);
            FromPointer(e.position.x, false);
            e.StopPropagation();
        });
        Sync();
    }

    /// <summary>The value as the page kept it.</summary>
    public string Value
    {
        get => value;
        set
        {
            this.value = value ?? "";
            Sync();
        }
    }

    /// <summary>Out of the menu highlight's reach: the row it sits in is what is chosen (the page's noFocus()).</summary>
    public void NoFocus()
    {
        ve.focusable = false;
        ve.tabIndex = -1;
        AddClass("nofocus");
    }

    private void FromPointer(float panelX, bool input)
    {
        Rect wb = ve.worldBound;
        float w = wb.width > 1f ? wb.width : 160f;
        float u = Mathf.Clamp01((panelX - wb.xMin) / w);
        double raw = Min + u * (Max - Min);
        if (Step > 0) raw = Min + Math.Round((raw - Min) / Step) * Step;
        raw = Math.Max(Min, Math.Min(Max, raw));
        string next = JS.Str(raw);
        bool changed = value != next;
        value = next;
        Sync();
        if (input && changed) OnInput?.Invoke();
    }

    private void Sync()
    {
        double cur = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : Min;
        float pct = Max > Min ? Mathf.Clamp01((float)((cur - Min) / (Max - Min))) * 100f : 0f;
        fill.style.width = Length.Percent(pct);
        thumb.style.left = Length.Percent(pct);
    }
}
