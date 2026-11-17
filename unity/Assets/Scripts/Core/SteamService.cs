using System;
using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

/// <summary>
/// Steam (Steamworks.NET): achievements, Deck detection, the overlay pause and the on-screen keyboard. The game plays
/// the same without Steam: if Steam is not running or the library is missing, every call here quietly does nothing.
/// Development uses steam_appid.txt next to the exe (480 is Steam's test app) until the real App ID exists.
/// </summary>
public static class SteamService
{
    public static bool Ready { get; private set; }

    /// <summary>true if Steam says this is a Steam Deck (also true in Steam's Big Picture on a Deck)</summary>
    public static bool OnDeck { get; private set; }

    /// <summary>called when the Steam overlay opens (true) or closes (false)</summary>
    public static Action<bool> overlayChanged;

    /// <summary>called with the text from the on-screen keyboard, or null if it was cancelled</summary>
    private static Action<string> keyboardDone;

#if !DISABLESTEAMWORKS
    private static Callback<GameOverlayActivated_t> overlayCb;
    private static Callback<GamepadTextInputDismissed_t> keyboardCb;
#endif

    public static void Init()
    {
#if !DISABLESTEAMWORKS
        try
        {
            if (Ready) return;
            if (!Packsize.Test()) { Debug.LogWarning("[Steam] wrong struct packing, Steam is not used"); return; }
            if (!DllCheck.Test()) { Debug.LogWarning("[Steam] steam_api64.dll is missing or the wrong version, Steam is not used"); return; }
            // RestartAppIfNecessary(appId) goes here once the real App ID exists
            if (!SteamAPI.Init()) { Debug.Log("[Steam] not running, playing without Steam"); return; }
            Ready = true;
            OnDeck = SteamUtils.IsRunningOnSteamHardware() == ESteamHardwareType.k_ESteamHardwareTypeSteamDeck
                     || SteamUtils.GetSteamHardwareDefaultConfig() == ESteamHardwareDefaultConfig.k_ESteamHardwareDefaultConfigSteamDeck;
            overlayCb = Callback<GameOverlayActivated_t>.Create(r => overlayChanged?.Invoke(r.m_bActive != 0));
            keyboardCb = Callback<GamepadTextInputDismissed_t>.Create(r =>
            {
                var done = keyboardDone;
                keyboardDone = null;
                if (done == null) return;
                if (r.m_bSubmitted && SteamUtils.GetEnteredGamepadTextInput(out string text, r.m_unSubmittedText + 1u)) done(text);
                else done(null);
            });
            Debug.Log($"[Steam] ready; user {SteamFriends.GetPersonaName()}, on Deck: {OnDeck}");
        }
        catch (Exception e)
        {
            Ready = false;
            Debug.LogWarning("[Steam] could not start: " + e.Message);
        }
#endif
    }

    public static void Tick()
    {
#if !DISABLESTEAMWORKS
        if (!Ready) return;
        try { SteamAPI.RunCallbacks(); }
        catch (Exception e) { Debug.LogWarning("[Steam] callbacks failed: " + e.Message); Ready = false; }
#endif
    }

    public static void Shutdown()
    {
#if !DISABLESTEAMWORKS
        if (!Ready) return;
        Ready = false;
        try { SteamAPI.Shutdown(); }
        catch { /* closing anyway */ }
#endif
    }

    public static void UnlockAchievement(string id)
    {
#if !DISABLESTEAMWORKS
        if (!Ready) return;
        try
        {
            if (SteamUserStats.SetAchievement(id)) SteamUserStats.StoreStats();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Steam] achievement " + id + " failed: " + e.Message);
        }
#endif
    }

    /// <summary>
    /// Opens Steam's on-screen keyboard (a Deck, Big Picture or a controller-only setup). Returns false when Steam cannot show
    /// it, and then nothing happens. <paramref name="done"/> gets the typed text, or null when the player cancelled.
    /// </summary>
    public static bool ShowKeyboard(string title, string current, int maxLength, Action<string> done)
    {
#if !DISABLESTEAMWORKS
        if (!Ready) return false;
        try
        {
            keyboardDone = done;
            if (SteamUtils.ShowGamepadTextInput(EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                    EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine, title, (uint)maxLength, current ?? ""))
                return true;
            keyboardDone = null;
        }
        catch (Exception e)
        {
            keyboardDone = null;
            Debug.LogWarning("[Steam] keyboard failed: " + e.Message);
        }
#endif
        return false;
    }

    /// <summary>Adds a picture file to the player's Steam screenshot library (so it shows up on the Deck). False when Steam is not running.</summary>
    public static bool AddScreenshot(string path, int width, int height)
    {
#if !DISABLESTEAMWORKS
        if (!Ready) return false;
        try
        {
            return SteamScreenshots.AddScreenshotToLibrary(path, null, width, height) != Steamworks.ScreenshotHandle.Invalid;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Steam] screenshot failed: " + e.Message);
        }
#endif
        return false;
    }
}
