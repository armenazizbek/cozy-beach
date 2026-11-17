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

    /// <summary>
    /// The graphics preset a computer should start on, from what the graphics chip reports: the Deck and chips that share the
    /// computer's memory (laptop and office graphics) get 'handheld', a graphics card with little memory 'low' or 'medium',
    /// a proper card 'high'. It only picks the first-run default; the player can change it in Settings at any time.
    /// </summary>
    public static string AutoQuality()
    {
        string q;
        try
        {
            if (IsSteamDeck) return "handheld";
            string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
            int vram = SystemInfo.graphicsMemorySize; // MB (for chips that share memory this is a share of the computer's)
            int cores = SystemInfo.processorCount;
            bool integrated = gpu.Contains("uhd") || gpu.Contains("iris") || gpu.Contains("intel(r) hd") || gpu.Contains("hd graphics")
                              || gpu.Contains("radeon(tm) graphics") || gpu.Contains("radeon graphics") || gpu.Contains("vega 3")
                              || gpu.Contains("vega 6") || gpu.Contains("vega 7") || gpu.Contains("vega 8") || gpu.Contains("vega 10")
                              || gpu.Contains("vega 11") || gpu.Contains("microsoft basic") || gpu.Contains("llvmpipe");
            if (integrated) q = "handheld";
            else if (vram < 2048 || cores < 4) q = "low";
            else if (vram < 5000) q = "medium";
            else q = "high";
            Debug.Log($"[Platform] auto graphics: {q} (gpu '{SystemInfo.graphicsDeviceName}', {vram} MB, {cores} cores)");
        }
        catch
        {
            q = "high";
        }
        return q;
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
