using Newtonsoft.Json;
using UnityEngine;

// Port of src/core/Settings.ts

public class GameSettings
{
    /// <summary>0..1 overall volume</summary>
    public double master = 1;
    /// <summary>0..1 the radio</summary>
    public double music = 0.4;
    /// <summary>0..1 sea and wind</summary>
    public double ambience = 1;
    /// <summary>mouse-look speed multiplier</summary>
    public double sensitivity = 1;
    /// <summary>pushing the mouse forward looks down instead of up</summary>
    public bool invertMouse = false;
    /// <summary>pushing the right stick forward looks down instead of up</summary>
    public bool invertStick = false;
    /// <summary>how wide the view is: the camera's vertical angle in degrees, FOV_MIN..FOV_MAX</summary>
    public double fov = 50;
    public const double FOV_MIN = 40, FOV_MAX = 70, FOV_DEFAULT = 50;
    public bool shadows = true;
    /// <summary>render at the screen's full pixel density (sharper, heavier)</summary>
    public bool sharp = true;
    /// <summary>0.5..1 how much of that resolution is actually drawn; the picture is stretched back up to fill the screen</summary>
    public double renderScale = 1;
    /// <summary>graphics quality: 'handheld' | 'low' | 'medium' | 'high' (antialiasing, shadow detail, reach and softness)</summary>
    public string quality = "high";
    /// <summary>wait for the screen's refresh before showing each picture (no tearing)</summary>
    public bool vsync = true;
    /// <summary>most pictures a second when VSync is off; 0 for no limit</summary>
    public int fpsLimit = 0;
    /// <summary>how large the menus, HUD and text are drawn: 1, 1.15 or 1.3 (the Steam Deck's small screen starts at 1.3)</summary>
    public double uiScale = 1;
    /// <summary>how the game window is shown: 'fullscreen' | 'windowed' | 'borderless'</summary>
    public string displayMode = "fullscreen";
    /// <summary>window size in windowed mode, "widthxheight"</summary>
    public string windowSize = "1280x720";

    public GameSettings Clone() => (GameSettings)MemberwiseClone();
}

public static class Settings
{
    private const string KEY = "cozy-treasure-hunter-settings-v1";

    public static GameSettings DEFAULT_SETTINGS => Defaults();

    /// <summary>
    /// What a player who has never changed anything gets. On a Steam Deck: the handheld graphics preset, a borderless window
    /// at the screen's own size, and VSync (the Deck's own frame limiter sits on top). Saved settings are never replaced.
    /// </summary>
    private static GameSettings Defaults()
    {
        var s = new GameSettings();
        if (Platform.IsSteamDeck)
        {
            s.quality = "handheld";
            s.uiScale = 1.3;
            s.displayMode = "borderless";
            s.windowSize = "1280x800";
            s.vsync = true;
            s.fpsLimit = 0;
        }
        return s;
    }

    public static GameSettings loadSettings()
    {
        try
        {
            var raw = PlayerPrefs.GetString(KEY, null);
            if (!string.IsNullOrEmpty(raw))
            {
                var s = Defaults();
                JsonConvert.PopulateObject(raw, s);
                return s;
            }
        }
        catch
        {
            /* ignore */
        }
        return Defaults();
    }

    public static void saveSettings(GameSettings s)
    {
        try
        {
            PlayerPrefs.SetString(KEY, JsonConvert.SerializeObject(s));
            PlayerPrefs.Save();
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>Flag so that "New game" from the pause menu starts a new game instead of returning to the main menu.</summary>
    private static bool autostart;
    /// <summary>...and whether it goes straight onto the beach, without the opening (only the smoke test asks for that).</summary>
    private static bool autostartSkipOpening;

    public static void requestAutostart(bool skipOpening = false)
    {
        autostart = true;
        autostartSkipOpening = skipOpening;
    }

    public static bool takeAutostart()
    {
        var v = autostart;
        autostart = false;
        return v;
    }

    public static bool takeAutostartSkipOpening()
    {
        var v = autostartSkipOpening;
        autostartSkipOpening = false;
        return v;
    }
}
