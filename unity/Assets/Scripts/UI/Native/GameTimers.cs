using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Timers on the game loop (unity/NATIVE_PLAN.md 2.7, S26): what setTimeout, setInterval and requestAnimationFrame did in
/// the browser, run once a frame from the UI's tick, on the test clock (TestClock), so a fixed-step run fires them on the
/// same frames every time. The page's window.setTimeout and friends hand their work here until the DOM layer goes (S33).
/// </summary>
public static class GameTimers
{
    private sealed class Task
    {
        public int id;
        public Action fn;
        public Action<double> frameFn;
        public double nextFireMs;
        public double intervalMs;
        public bool isInterval;
        public bool isFrame;
        public bool cancelled;
    }

    private static int nextId = 1;
    private static readonly List<Task> tasks = new List<Task>();

    private static double NowMs() => TestClock.Ms;

    /// <summary>Runs `fn` once, `ms` milliseconds from now. Returns an id for <see cref="Cancel"/>.</summary>
    public static int After(double ms, Action fn)
    {
        if (fn == null) return 0;
        int id = nextId++;
        tasks.Add(new Task { id = id, fn = fn, nextFireMs = NowMs() + Math.Max(0.0, ms) });
        return id;
    }

    /// <summary>Runs `fn` every `ms` milliseconds (at least 1).</summary>
    public static int Every(double ms, Action fn)
    {
        if (fn == null) return 0;
        int id = nextId++;
        double period = Math.Max(1.0, ms);
        tasks.Add(new Task { id = id, fn = fn, nextFireMs = NowMs() + period, intervalMs = period, isInterval = true });
        return id;
    }

    /// <summary>Runs `fn` on the next frame with the time in milliseconds (requestAnimationFrame).</summary>
    public static int NextFrame(Action<double> fn)
    {
        if (fn == null) return 0;
        int id = nextId++;
        tasks.Add(new Task { id = id, frameFn = fn, isFrame = true });
        return id;
    }

    /// <summary>Stops a timer that has not fired yet (any kind).</summary>
    public static void Cancel(int id)
    {
        for (int i = 0; i < tasks.Count; i++)
            if (tasks[i].id == id) tasks[i].cancelled = true;
    }

    /// <summary>Forgets every timer (a reload).</summary>
    public static void Clear() => tasks.Clear();

    /// <summary>Fires what is due; called once a frame. Timers added while running wait for the next frame.</summary>
    public static void Run()
    {
        if (tasks.Count == 0) return;
        double now = NowMs();
        int count = tasks.Count;
        for (int i = 0; i < count && i < tasks.Count; i++)
        {
            var t = tasks[i];
            if (t == null || t.cancelled) continue;
            if (t.isFrame)
            {
                t.cancelled = true;
                try { t.frameFn?.Invoke(now); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            else if (now >= t.nextFireMs)
            {
                if (t.isInterval) t.nextFireMs = now + t.intervalMs;
                else t.cancelled = true;
                try { t.fn?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
        for (int i = tasks.Count - 1; i >= 0; i--)
            if (tasks[i] == null || tasks[i].cancelled) tasks.RemoveAt(i);
    }
}
