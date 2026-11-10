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
    /// <summary>how the game window is shown: 'fullscreen' | 'windowed' | 'borderless'</summary>
    public string displayMode = "fullscreen";
    /// <summary>window size in windowed mode, "widthxheight"</summary>
    public string windowSize = "1280x720";

    public GameSettings Clone() => (GameSettings)MemberwiseClone();
}

public static class Settings
{
    private const string KEY = "cozy-treasure-hunter-settings-v1";

    public static GameSettings DEFAULT_SETTINGS => new GameSettings();

    public static GameSettings loadSettings()
    {
        try
        {
            var raw = PlayerPrefs.GetString(KEY, null);
            if (!string.IsNullOrEmpty(raw))
            {
                var s = new GameSettings();
                JsonConvert.PopulateObject(raw, s);
                return s;
            }
        }
        catch
        {
            /* ignore */
        }
        return new GameSettings();
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

    /// <summary>Flag so that "New game" from the pause menu starts straight away instead of returning to the main menu.</summary>
    private static bool autostart;

    public static void requestAutostart() => autostart = true;

    public static bool takeAutostart()
    {
        var v = autostart;
        autostart = false;
        return v;
    }
}
