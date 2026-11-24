using System;

/// <summary>
/// Work native objects do at the moment the frame is drawn (unity/NATIVE_PLAN.md S11), after all of the game's updates. A
/// module that changes a mesh several times in a frame (footprints laid after the tracks' own update) bakes it once here,
/// so what is drawn is what the bridge drew: the state at render time. Called by the renderer before it draws.
/// </summary>
public static class NativeFrame
{
    public static event Action BeforeDraw;

    internal static void Run()
    {
        // a new frame for the material cache (the bridge's scene copy used to start it; the main world no longer goes through it)
        NativeRender.MaterialCache.NextFrame();
        var all = BeforeDraw;
        if (all == null) return;
        // each hook timed under its own name (FrameStats: the perf runs list the slowest)
        foreach (var d in all.GetInvocationList())
        {
            if (!names.TryGetValue(d, out var name))
                names[d] = name = "draw:" + (d.Target is Tracked t ? "tree " + t.Name : d.Method.DeclaringType?.Name ?? "?");
            using (NativeRender.FrameStats.Time(name)) ((Action)d)();
        }
    }

    private static readonly System.Collections.Generic.Dictionary<Delegate, string> names = new System.Collections.Generic.Dictionary<Delegate, string>();

    /// <summary>Forgets every hook (a reload).</summary>
    internal static void Clear()
    {
        BeforeDraw = null;
        names.Clear();
    }
}
