using UnityEngine;

/// <summary>
/// How often the game draws a picture: VSync and the frame limit from the settings, and a slow pace while the window is in
/// the background (the game keeps running there, but nobody is looking, so it need not keep the graphics chip busy).
/// </summary>
public static class FramePacing
{
    /// <summary>Pictures a second while the game window does not have the focus.</summary>
    private const int BACKGROUND_FPS = 20;

    private static bool vsync = true;
    private static int limit;
    private static bool hooked;

    /// <summary>The frame limits offered in the settings (0 is no limit).</summary>
    public static readonly int[] LIMITS = { 0, 30, 60, 90, 120, 144, 165, 240 };

    public static void Apply(bool vsyncOn, int fpsLimit)
    {
        vsync = vsyncOn;
        limit = fpsLimit > 0 ? fpsLimit : 0;
        if (!hooked)
        {
            hooked = true;
            Application.focusChanged += _ => Refresh();
        }
        Refresh();
    }

    private static void Refresh()
    {
        // the smoke test measures the frame rate and may run without the focus
        bool background = !Application.isFocused && !AutoShot.Active;
        if (background)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = BACKGROUND_FPS;
            return;
        }
        QualitySettings.vSyncCount = vsync ? 1 : 0;
        // Unity ignores the target frame rate while VSync is on: the screen's refresh is the limit then
        Application.targetFrameRate = !vsync && limit > 0 ? limit : -1;
    }
}
