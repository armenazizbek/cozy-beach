using System;

/// <summary>
/// Work native objects do at the moment the frame is drawn (unity/NATIVE_PLAN.md S11), after all of the game's updates. A
/// module that changes a mesh several times in a frame (footprints laid after the tracks' own update) bakes it once here,
/// so what is drawn is what the bridge drew: the state at render time. Called by the renderer before it draws.
/// </summary>
public static class NativeFrame
{
    public static event Action BeforeDraw;

    internal static void Run() => BeforeDraw?.Invoke();

    /// <summary>Forgets every hook (a reload).</summary>
    internal static void Clear() => BeforeDraw = null;
}
