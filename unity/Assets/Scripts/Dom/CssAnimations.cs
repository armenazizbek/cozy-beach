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

    private enum AnimKind
    {
        None = 0,
        Kbd,
        Pop,
        Slide,
        MmIn,
        SleepIn,
        ZzUp,
    }

    private sealed class ActiveAnim
    {
        public AnimKind kind;
        public double startMs;
        public double delayMs;
        public IVisualElementScheduledItem task;
    }

    private static readonly Dictionary<VisualElement, ActiveAnim> _active = new Dictionary<VisualElement, ActiveAnim>();

    public static void OnAttached(HTMLElement el)
    {
        if (el == null || el.ve == null) return;
        ApplyStructuralPseudoClasses(el);
        UpdateAnimation(el, isAttach: true);
    }

    public static void OnClassesChanged(HTMLElement el)
    {
        if (el == null || el.ve == null) return;
        ApplyStructuralPseudoClasses(el);
        UpdateAnimation(el, isAttach: false);
    }

    private static bool HasClass(HTMLElement el, string cls)
    {
        if (el.classList != null) return el.classList.contains(cls);
        if (string.IsNullOrEmpty(el.className)) return false;
        string[] parts = el.className.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == cls) return true;
        }
        return false;
    }

    /// <summary>
    /// Applies helper classes for CSS selectors that USS does not natively support
    /// (<c>:nth-child</c>, <c>:has()</c>, <c>::before</c> pin).
    /// </summary>
    private static void ApplyStructuralPseudoClasses(HTMLElement el)
    {
        VisualElement ve = el.ve;
        if (ve == null) return;

        VisualElement parent = ve.parent;
        if (parent != null)
        {
            int idx = parent.IndexOf(ve);
            if (idx >= 0)
            {
                // .note:nth-child(even) -> 1-based index is even when 0-based idx is odd
                if (HasClass(el, "note"))
                {
                    ve.EnableInClassList("even", (idx % 2) == 1);
                }

                // .pt-center i:nth-child(1) and :nth-child(2)
                if (ve.ClassListContains("t-i") && parent.ClassListContains("pt-center"))
                {
                    ve.EnableInClassList("nth-1", idx == 0);
                    ve.EnableInClassList("nth-2", idx == 1);
                }
            }

            // .set-row:has(.set-check)
            if (HasClass(el, "set-check"))
            {
                parent.AddToClassList("has-check");
            }
        }

        // .polaroid::before red push-pin
        if (HasClass(el, "polaroid") && ve.Q(className: "polaroid-pin") == null)
        {
            VisualElement pin = new VisualElement();
            pin.AddToClassList("polaroid-pin");
            pin.pickingMode = PickingMode.Ignore;
            ve.Insert(0, pin);
        }
    }

    private static AnimKind MatchAnimation(HTMLElement el, out double delayMs)
    {
        delayMs = 0.0;
        if (el == null || el.ve == null) return AnimKind.None;

        // Hidden elements (display: none) do not run animations until shown
        if (HasClass(el, "hidden")) return AnimKind.None;

        // .prompt .kbd { animation: kbd 1.4s ease-in-out infinite; }
        if (HasClass(el, "kbd"))
        {
            return AnimKind.Kbd;
        }

        // .card { animation: pop 0.45s cubic-bezier(0.2, 1.4, 0.4, 1); }
        if (HasClass(el, "card"))
        {
            return AnimKind.Pop;
        }

        // .camp { animation: slide 0.35s ease; }
        if (HasClass(el, "camp"))
        {
            return AnimKind.Slide;
        }

        // .mainmenu { animation: mmIn 0.9s ease both; }
        if (HasClass(el, "mainmenu"))
        {
            return AnimKind.MmIn;
        }

        // .sleepveil { animation: sleepIn 1.2s ease both; }
        if (HasClass(el, "sleepveil"))
        {
            return AnimKind.SleepIn;
        }

        // .zz { animation: zzUp 3.6s ease-in-out infinite; }
        // .zz1 { animation-delay: 1.2s; } .zz2 { animation-delay: 2.4s; }
        if (HasClass(el, "zz"))
        {
            if (HasClass(el, "zz2")) delayMs = 2400.0;
            else if (HasClass(el, "zz1")) delayMs = 1200.0;
            return AnimKind.ZzUp;
        }

        return AnimKind.None;
    }

    private static void UpdateAnimation(HTMLElement el, bool isAttach)
    {
        VisualElement ve = el.ve;
        double delayMs;
        AnimKind kind = MatchAnimation(el, out delayMs);

        ActiveAnim existing;
        bool hasExisting = _active.TryGetValue(ve, out existing);

        if (kind == AnimKind.None)
        {
            if (hasExisting)
            {
                StopAnimation(ve, existing);
            }
            return;
        }

        // If already running the same animation and this is just a class tweak (not re-attach), keep running
        if (hasExisting && existing.kind == kind && Mathf.Approximately((float)existing.delayMs, (float)delayMs) && !isAttach)
        {
            return;
        }

        if (hasExisting)
        {
            StopAnimation(ve, existing);
        }

        StartAnimation(ve, kind, delayMs);
    }

    private static void StopAnimation(VisualElement ve, ActiveAnim anim)
    {
        if (anim.task != null)
        {
            anim.task.Pause();
            anim.task = null;
        }
        _active.Remove(ve);
    }

    private static double NowMs()
    {
        return Time.realtimeSinceStartupAsDouble * 1000.0;
    }

    private static void StartAnimation(VisualElement ve, AnimKind kind, double delayMs)
    {
        ActiveAnim anim = new ActiveAnim
        {
            kind = kind,
            startMs = NowMs(),
            delayMs = delayMs,
        };
        _active[ve] = anim;

        // Apply initial frame immediately (honours backwards fill-mode / 0% keyframe)
        StepAnimation(ve, anim);

        anim.task = ve.schedule.Execute(() =>
        {
            if (ve.panel == null && (NowMs() - anim.startMs) > 2000.0)
            {
                StopAnimation(ve, anim);
                return;
            }
            StepAnimation(ve, anim);
        }).Every(16);
    }

    private static void StepAnimation(VisualElement ve, ActiveAnim anim)
    {
        double elapsed = NowMs() - anim.startMs - anim.delayMs;

        switch (anim.kind)
        {
            case AnimKind.Kbd:
            {
                // @keyframes kbd { 50% { transform: translateY(-2px); } }
                // 1.4s ease-in-out infinite
                const double duration = 1400.0;
                if (elapsed < 0.0)
                {
                    ve.style.translate = new Translate(0f, 0f, 0f);
                    return;
                }
                double u = (elapsed % duration) / duration;
                double y;
                if (u < 0.5)
                {
                    double k = EaseInOut(u / 0.5);
                    y = -2.0 * k;
                }
                else
                {
                    double k = EaseInOut((u - 0.5) / 0.5);
                    y = -2.0 * (1.0 - k);
                }
                ve.style.translate = new Translate(0f, (float)y, 0f);
                break;
            }

            case AnimKind.Pop:
            {
                // @keyframes pop { from { transform: scale(0.6); opacity: 0; } to { transform: scale(1); opacity: 1; } }
                // 0.45s cubic-bezier(0.2, 1.4, 0.4, 1)
                const double duration = 450.0;
                if (elapsed <= 0.0)
                {
                    ve.style.scale = new Scale(new Vector3(0.6f, 0.6f, 1f));
                    ve.style.opacity = 0f;
                    return;
                }
                if (elapsed >= duration)
                {
                    ve.style.scale = new Scale(Vector3.one);
                    ve.style.opacity = 1f;
                    StopAnimation(ve, anim);
                    return;
                }
                double u = elapsed / duration;
                double k = CubicBezier(0.2, 1.4, 0.4, 1.0, u);
                float s = (float)(0.6 + 0.4 * k);
                float op = Mathf.Clamp01((float)k);
                ve.style.scale = new Scale(new Vector3(s, s, 1f));
                ve.style.opacity = op;
                break;
            }

            case AnimKind.Slide:
            {
                // @keyframes slide { from { transform: translateX(40px); opacity: 0; } to { transform: none; opacity: 1; } }
                // 0.35s ease
                const double duration = 350.0;
                if (elapsed <= 0.0)
                {
                    ve.style.translate = new Translate(40f, 0f, 0f);
                    ve.style.opacity = 0f;
                    return;
                }
                if (elapsed >= duration)
                {
                    ve.style.translate = new Translate(0f, 0f, 0f);
                    ve.style.opacity = 1f;
                    StopAnimation(ve, anim);
                    return;
                }
                double u = elapsed / duration;
                double k = Ease(u);
                float x = (float)(40.0 * (1.0 - k));
                ve.style.translate = new Translate(x, 0f, 0f);
                ve.style.opacity = (float)k;
                break;
            }

            case AnimKind.MmIn:
            {
                // @keyframes mmIn { from { opacity: 0; } to { opacity: 1; } }
                // 0.9s ease both
                const double duration = 900.0;
                if (elapsed <= 0.0)
                {
                    ve.style.opacity = 0f;
                    return;
                }
                if (elapsed >= duration)
                {
                    ve.style.opacity = 1f;
                    StopAnimation(ve, anim);
                    return;
                }
                double u = elapsed / duration;
                ve.style.opacity = (float)Ease(u);
                break;
            }

            case AnimKind.SleepIn:
            {
                // @keyframes sleepIn { from { opacity: 0; } to { opacity: 1; } }
                // 1.2s ease both
                const double duration = 1200.0;
                if (elapsed <= 0.0)
                {
                    ve.style.opacity = 0f;
                    return;
                }
                if (elapsed >= duration)
                {
                    ve.style.opacity = 1f;
                    StopAnimation(ve, anim);
                    return;
                }
                double u = elapsed / duration;
                ve.style.opacity = (float)Ease(u);
                break;
            }

            case AnimKind.ZzUp:
            {
                // @keyframes zzUp {
                //   0% { transform: translateY(0) scale(0.6); opacity: 0; }
                //   20% { opacity: 0.9; }
                //   100% { transform: translateY(-160px) scale(1.7); opacity: 0; }
                // }
                // 3.6s ease-in-out infinite (with delay 0s / 1.2s / 2.4s)
                const double duration = 3600.0;
                if (elapsed < 0.0)
                {
                    ve.style.opacity = 0f;
                    ve.style.translate = new Translate(0f, 0f, 0f);
                    ve.style.scale = new Scale(new Vector3(0.6f, 0.6f, 1f));
                    return;
                }
                double u = (elapsed % duration) / duration;
                double kt = EaseInOut(u);
                float y = (float)(-160.0 * kt);
                float s = (float)(0.6 + (1.7 - 0.6) * kt);

                double op;
                if (u < 0.2)
                {
                    op = 0.9 * EaseInOut(u / 0.2);
                }
                else
                {
                    op = 0.9 * (1.0 - EaseInOut((u - 0.2) / 0.8));
                }

                ve.style.translate = new Translate(0f, y, 0f);
                ve.style.scale = new Scale(new Vector3(s, s, 1f));
                ve.style.opacity = (float)op;
                break;
            }
        }
    }

    // CSS ease = cubic-bezier(0.25, 0.1, 0.25, 1.0)
    private static double Ease(double t)
    {
        return CubicBezier(0.25, 0.1, 0.25, 1.0, t);
    }

    // CSS ease-in-out = cubic-bezier(0.42, 0.0, 0.58, 1.0)
    private static double EaseInOut(double t)
    {
        return CubicBezier(0.42, 0.0, 0.58, 1.0, t);
    }

    // Solves CSS cubic-bezier(x1, y1, x2, y2) at normalized time t in [0, 1]
    private static double CubicBezier(double x1, double y1, double x2, double y2, double t)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;

        // Solve x(s) = t for parameter s using Newton-Raphson with bisection fallback
        double s = t;
        for (int i = 0; i < 8; i++)
        {
            double x = SampleCurve(x1, x2, s) - t;
            if (Math.Abs(x) < 1e-6) break;
            double dx = SampleCurveDerivative(x1, x2, s);
            if (Math.Abs(dx) < 1e-6) break;
            s -= x / dx;
        }

        if (s < 0.0 || s > 1.0)
        {
            double lo = 0.0, hi = 1.0;
            s = t;
            for (int i = 0; i < 12; i++)
            {
                double x = SampleCurve(x1, x2, s);
                if (Math.Abs(x - t) < 1e-6) break;
                if (t > x) lo = s;
                else hi = s;
                s = 0.5 * (lo + hi);
            }
        }

        return SampleCurve(y1, y2, s);
    }

    private static double SampleCurve(double p1, double p2, double s)
    {
        double c = 3.0 * p1;
        double b = 3.0 * (p2 - p1) - c;
        double a = 1.0 - c - b;
        return ((a * s + b) * s + c) * s;
    }

    private static double SampleCurveDerivative(double p1, double p2, double s)
    {
        double c = 3.0 * p1;
        double b = 3.0 * (p2 - p1) - c;
        double a = 1.0 - c - b;
        return (3.0 * a * s + 2.0 * b) * s + c;
    }
}
