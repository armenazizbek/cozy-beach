using System;
using UnityEngine;

/// <summary>
/// What the game is running on. The only question so far is whether this is a Steam Deck (or a handheld that should be
/// treated like one): first run defaults, text size and graphics quality follow from it. Steam sets the environment variable
/// SteamDeck=1 for games started on a Deck, under Proton as well; the graphics chip's name is the fallback. Once Steamworks is
/// wired in (STEAM_DECK_PLAN.md, task 2.1) SteamUtils.IsSteamRunningOnSteamDeck can be added to <see cref="IsSteamDeck"/>.
/// </summary>
public static class Platform
{
    private static bool? deck;

    /// <summary>The command line flag -force-deck pretends to be a Deck on any machine, to try the Deck's settings on a PC.</summary>
    public static bool IsSteamDeck
    {
        get
        {
            if (deck.HasValue) return deck.Value;
            bool v = false;
            try
            {
                v = Array.IndexOf(Environment.GetCommandLineArgs(), "-force-deck") >= 0
                    || SteamService.OnDeck
                    || Environment.GetEnvironmentVariable("SteamDeck") == "1";
                if (!v)
                {
                    // the Deck's APU as the graphics driver names it (LCD model, OLED model)
                    string gpu = SystemInfo.graphicsDeviceName ?? "";
                    v = gpu.IndexOf("Custom GPU 0405", StringComparison.OrdinalIgnoreCase) >= 0
                        || gpu.IndexOf("Custom GPU 0932", StringComparison.OrdinalIgnoreCase) >= 0
                        || gpu.IndexOf("Van Gogh", StringComparison.OrdinalIgnoreCase) >= 0
                        || gpu.IndexOf("Sephiroth", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                /* not a Deck as far as we can tell */
            }
            deck = v;
            Debug.Log($"[Platform] Steam Deck: {v} (graphics: {SystemInfo.graphicsDeviceName})");
            return v;
        }
    }

    /// <summary>One line describing the machine and the graphics setup, for the smoke test's log.</summary>
    public static string Describe()
    {
        var res = Screen.currentResolution;
        return $"deck {IsSteamDeck}; {SystemInfo.graphicsDeviceType} on {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceVersion}); " +
               $"cpu {SystemInfo.processorType} x{SystemInfo.processorCount}; screen {Screen.width}x{Screen.height} at {res.refreshRateRatio.value:0.#} Hz, " +
               $"{Screen.fullScreenMode}, dpi {Screen.dpi:0}";
    }
}
