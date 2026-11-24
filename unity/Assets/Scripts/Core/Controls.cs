using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The game's keyboard controls as an Input System action map (unity/UI_PLAN.md B3). Each thing a key does is an action with
// a meaning (Forward, Use, Pause, Confirm...), bound by default to the keys the game always used. One key can carry several
// actions, as before: E is "Use 2" in play, "next tab" in the camp and "zoom in" while cleaning.
//
// A key press is still handled once, as a Press that carries every action bound to that key, so the order of the game's
// checks and its timing are what they were. The controller and the smoke test hold the game's virtual keys ("KeyW"), which
// mean the default bindings whatever the player has rebound.

/// <summary>What a key does in the game.</summary>
public enum Act
{
    // walking, swimming and driving
    Forward, Backward, Left, Right, Run, Ascend, Descend, Brake,
    // in play
    Use, Use2, ToolSwap, Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8, SlotOther,
    Mute, Map, MapSwitch, Pause, Surface, ScopeHint, Continue,
    // menus, the camp and the journal
    MenuUp, MenuDown, MenuLeft, MenuRight, Confirm, TabPrev, TabNext, PagePrev, PageNext,
    // cleaning a find
    PanLeft, PanRight, PanUp, PanDown, ZoomIn, ZoomOut, RotateLeft, RotateRight, TiltUp, TiltDown,
}

/// <summary>One key press: the key's code and the actions it carries.</summary>
public readonly struct Press
{
    public readonly string code;
    private readonly ulong acts;

    public Press(string code, ulong acts)
    {
        this.code = code;
        this.acts = acts;
    }

    public bool Is(Act a) => (acts & Controls.Bit(a)) != 0;

    public bool Is(Act a, Act b) => Is(a) || Is(b);

    public bool Is(Act a, Act b, Act c) => Is(a) || Is(b) || Is(c);

    /// <summary>Any of the tool slot keys, 1 to 8 or the others.</summary>
    public bool IsSlot => (acts & Controls.SlotBits) != 0;

    public override string ToString() => code;
}

public static class Controls
{
    /// <summary>Where the player's own bindings are kept, apart from the settings.</summary>
    private const string PREFS_KEY = "tidefinder-controls-v1";

    /// <summary>False in the smoke tests: a rebind there changes this run only, never the player's own keys.</summary>
    private static bool persist;

    /// <summary>The default keys of each action, as the game's key codes (InputManager.DomCode).</summary>
    private static readonly (Act act, Key[] keys)[] Defaults =
    {
        (Act.Forward, new[] { Key.W }),
        (Act.Backward, new[] { Key.S }),
        (Act.Left, new[] { Key.A }),
        (Act.Right, new[] { Key.D }),
        (Act.Run, new[] { Key.LeftShift }),
        (Act.Ascend, new[] { Key.Space }),
        (Act.Descend, new[] { Key.C, Key.LeftCtrl }),
        (Act.Brake, new[] { Key.Space }),
        (Act.Use, new[] { Key.F }),
        (Act.Use2, new[] { Key.E }),
        (Act.ToolSwap, new[] { Key.Space }),
        (Act.Slot1, new[] { Key.Digit1 }),
        (Act.Slot2, new[] { Key.Digit2 }),
        (Act.Slot3, new[] { Key.Digit3 }),
        (Act.Slot4, new[] { Key.Digit4 }),
        (Act.Slot5, new[] { Key.Digit5 }),
        (Act.Slot6, new[] { Key.Digit6 }),
        (Act.Slot7, new[] { Key.Digit7 }),
        (Act.Slot8, new[] { Key.Digit8 }),
        (Act.SlotOther, new[] { Key.Digit9, Key.Digit0 }),
        (Act.Mute, new[] { Key.N }),
        (Act.Map, new[] { Key.M }),
        (Act.MapSwitch, new[] { Key.T }),
        (Act.Pause, new[] { Key.Escape }),
        (Act.Surface, new[] { Key.Q }),
        (Act.ScopeHint, new[] { Key.H }),
        (Act.Continue, new[] { Key.Space }),
        (Act.MenuUp, new[] { Key.UpArrow, Key.W }),
        (Act.MenuDown, new[] { Key.DownArrow, Key.S }),
        (Act.MenuLeft, new[] { Key.LeftArrow, Key.A }),
        (Act.MenuRight, new[] { Key.RightArrow, Key.D }),
        (Act.Confirm, new[] { Key.Enter, Key.NumpadEnter, Key.Space }),
        (Act.TabPrev, new[] { Key.Q }),
        (Act.TabNext, new[] { Key.E }),
        (Act.PagePrev, new[] { Key.LeftArrow, Key.A }),
        (Act.PageNext, new[] { Key.RightArrow, Key.D }),
        (Act.PanLeft, new[] { Key.A, Key.LeftArrow }),
        (Act.PanRight, new[] { Key.D, Key.RightArrow }),
        (Act.PanUp, new[] { Key.W, Key.UpArrow }),
        (Act.PanDown, new[] { Key.S, Key.DownArrow }),
        (Act.ZoomIn, new[] { Key.E, Key.Equals }),
        (Act.ZoomOut, new[] { Key.Q, Key.Minus }),
        (Act.RotateLeft, new[] { Key.Q, Key.LeftArrow }),
        (Act.RotateRight, new[] { Key.E, Key.RightArrow }),
        (Act.TiltUp, new[] { Key.UpArrow }),
        (Act.TiltDown, new[] { Key.DownArrow }),
    };

    internal static ulong Bit(Act a) => 1UL << (int)a;

    internal static readonly ulong SlotBits = Bit(Act.Slot1) | Bit(Act.Slot2) | Bit(Act.Slot3) | Bit(Act.Slot4) | Bit(Act.Slot5)
        | Bit(Act.Slot6) | Bit(Act.Slot7) | Bit(Act.Slot8) | Bit(Act.SlotOther);

    // declared before Map: static fields are set in the order they are written, and Build fills this one
    /// <summary>For each key code, the actions it carries by default and as bound now.</summary>
    private static readonly Dictionary<string, ulong> byDefault = new Dictionary<string, ulong>();
    private static Dictionary<string, ulong> bound;

    /// <summary>The action map: one button action per Act, named after it. It is never enabled; it holds the bindings.</summary>
    public static readonly InputActionMap Map = Build();

    private static InputActionMap Build()
    {
        if (Enum.GetValues(typeof(Act)).Length > 64) throw new InvalidOperationException("Press keeps the actions in 64 bits");
        var map = new InputActionMap("Game");
        foreach (var (act, keys) in Defaults)
        {
            var a = map.AddAction(act.ToString(), InputActionType.Button);
            foreach (var k in keys)
            {
                a.AddBinding(Path(k));
                string code = InputManager.DomCode(k);
                byDefault.TryGetValue(code, out ulong bits);
                byDefault[code] = bits | Bit(act);
            }
        }
        return map;
    }

    private static string Path(Key k)
    {
        // the control names of the Keyboard layout
        switch (k)
        {
            case Key.LeftShift: return "<Keyboard>/leftShift";
            case Key.LeftCtrl: return "<Keyboard>/leftCtrl";
            case Key.UpArrow: return "<Keyboard>/upArrow";
            case Key.DownArrow: return "<Keyboard>/downArrow";
            case Key.LeftArrow: return "<Keyboard>/leftArrow";
            case Key.RightArrow: return "<Keyboard>/rightArrow";
            case Key.NumpadEnter: return "<Keyboard>/numpadEnter";
            case Key.Equals: return "<Keyboard>/equals";
            case Key.Minus: return "<Keyboard>/minus";
            case Key.Escape: return "<Keyboard>/escape";
            case Key.Enter: return "<Keyboard>/enter";
            case Key.Space: return "<Keyboard>/space";
        }
        if (k >= Key.Digit1 && k <= Key.Digit9) return "<Keyboard>/" + (char)('1' + (k - Key.Digit1));
        if (k == Key.Digit0) return "<Keyboard>/0";
        if (k >= Key.A && k <= Key.Z) return "<Keyboard>/" + (char)('a' + (k - Key.A));
        throw new ArgumentException("no binding path for " + k);
    }

    /// <summary>Loads the player's own bindings, if any. The smoke tests always play with the defaults.</summary>
    public static void Load(bool defaultsOnly)
    {
        persist = !defaultsOnly;
        Map.RemoveAllBindingOverrides();
        if (!defaultsOnly)
        {
            try
            {
                string json = PlayerPrefs.GetString(PREFS_KEY, "");
                if (json.Length > 0) Map.LoadBindingOverridesFromJson(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not read the key bindings, using the defaults: " + e.Message);
                Map.RemoveAllBindingOverrides();
            }
        }
        bound = null;
        Changed?.Invoke();
    }

    /// <summary>Keeps the player's bindings (after a rebind) and makes them take effect.</summary>
    public static void Save()
    {
        if (persist)
        {
            PlayerPrefs.SetString(PREFS_KEY, Map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }
        bound = null;
        Changed?.Invoke();
    }

    /// <summary>The actions the Settings screen lets the player rebind, with their names there.</summary>
    public static readonly (Act act, string label)[] Rebindable =
    {
        (Act.Forward, "Walk forward"),
        (Act.Backward, "Walk back"),
        (Act.Left, "Step left"),
        (Act.Right, "Step right"),
        (Act.Run, "Run"),
        (Act.Use, "Use: dig, pick up, talk"),
        (Act.Use2, "Second use"),
        (Act.ToolSwap, "Swap detector and trowel"),
        (Act.Map, "Map"),
        (Act.Mute, "Mute"),
        (Act.Ascend, "Swim up"),
        (Act.Descend, "Swim down"),
        (Act.Surface, "Swim to the surface"),
        (Act.Brake, "Brake the cart or boat"),
    };

    /// <summary>Raised when the bindings change (a rebind, a reset or a load), so key pictures can follow.</summary>
    public static event Action Changed;

    private static InputActionRebindingExtensions.RebindingOperation rebinding;
    private static int quietUntil = -1;

    /// <summary>
    /// True while a rebind waits for its key, and on the frame it ends: that key press is the new binding, not a command to
    /// the game (InputManager treats it as typing).
    /// </summary>
    public static bool Quiet => rebinding != null || Time.frameCount <= quietUntil;

    /// <summary>The name of an action's key as the player sees it ("F", "Shift", "Space"); its first binding.</summary>
    public static string KeyName(Act a)
    {
        var action = Map[a.ToString()];
        return action.bindings.Count > 0 ? NameOf(action.bindings[0].effectivePath) : "";
    }

    /// <summary>All of an action's keys, for the Settings row ("C / Ctrl").</summary>
    public static string KeyNames(Act a)
    {
        var names = new List<string>();
        foreach (var b in Map[a.ToString()].bindings)
        {
            string n = NameOf(b.effectivePath);
            if (n.Length > 0 && !names.Contains(n)) names.Add(n);
        }
        return string.Join(" / ", names);
    }

    private static string NameOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        int slash = path.LastIndexOf('/');
        string c = slash >= 0 ? path.Substring(slash + 1) : path;
        if (c.Length == 1) return c.ToUpperInvariant();
        switch (c)
        {
            case "leftShift": case "rightShift": return "Shift";
            case "leftCtrl": case "rightCtrl": return "Ctrl";
            case "leftAlt": case "rightAlt": return "Alt";
            case "space": return "Space";
            case "enter": return "Enter";
            case "numpadEnter": return "Num Enter";
            case "tab": return "Tab";
            case "backspace": return "Backspace";
            case "upArrow": return "Up";
            case "downArrow": return "Down";
            case "leftArrow": return "Left";
            case "rightArrow": return "Right";
            case "equals": return "=";
            case "minus": return "-";
            case "comma": return ",";
            case "period": return ".";
            case "slash": return "/";
            case "semicolon": return ";";
            case "quote": return "'";
            case "backquote": return "`";
            case "leftBracket": return "[";
            case "rightBracket": return "]";
            case "backslash": return "\\";
        }
        return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
    }

    /// <summary>
    /// Waits for the next key on the keyboard and binds the action's first binding to it, then keeps it. Esc cancels; so
    /// does waiting more than eight seconds. `done` runs either way.
    /// </summary>
    public static void Rebind(Act a, Action done)
    {
        Cancel();
        var action = Map[a.ToString()];
        rebinding = action.PerformInteractiveRebinding(0)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsExcluding("<Keyboard>/anyKey")
            .WithCancelingThrough("<Keyboard>/escape")
            .WithTimeout(8f)
            .OnComplete(_ =>
            {
                Finish();
                Save();
                done?.Invoke();
            })
            .OnCancel(_ =>
            {
                Finish();
                done?.Invoke();
            });
        rebinding.Start();
    }

    /// <summary>Stops a rebind that is waiting (the Settings screen closing), keeping the old key.</summary>
    public static void Cancel() => rebinding?.Cancel();

    private static void Finish()
    {
        var op = rebinding;
        rebinding = null;
        quietUntil = Time.frameCount + 1;
        op?.Dispose();
    }

    /// <summary>Puts every key back to the game's own, and keeps that.</summary>
    public static void ResetAll()
    {
        Cancel();
        Map.RemoveAllBindingOverrides();
        Save();
    }

    /// <summary>Makes binding overrides applied to Map directly (a rebind in progress) take effect.</summary>
    public static void Rebound() => bound = null;

    /// <summary>The actions a key carries as bound now (a key on the keyboard).</summary>
    public static Press Bound(string code)
    {
        if (bound == null) bound = Resolve();
        bound.TryGetValue(code, out ulong bits);
        return new Press(code, bits);
    }

    /// <summary>The actions a virtual key carries (the controller's and the smoke test's): always the default bindings.</summary>
    public static Press Default(string code)
    {
        byDefault.TryGetValue(code, out ulong bits);
        return new Press(code, bits);
    }

    /// <summary>The key codes of each binding as it is now, overrides included.</summary>
    private static Dictionary<string, ulong> Resolve()
    {
        var kb = Keyboard.current;
        if (kb == null) return new Dictionary<string, ulong>(byDefault);
        var table = new Dictionary<string, ulong>();
        foreach (var action in Map.actions)
        {
            var act = (Act)Enum.Parse(typeof(Act), action.name);
            foreach (var b in action.bindings)
            {
                if (string.IsNullOrEmpty(b.effectivePath)) continue;
                foreach (var key in kb.allKeys)
                {
                    if (key == null || !InputControlPath.Matches(b.effectivePath, key)) continue;
                    string code = InputManager.DomCode(key.keyCode);
                    if (code == null) continue;
                    table.TryGetValue(code, out ulong bits);
                    table[code] = bits | Bit(act);
                }
            }
        }
        return table;
    }
}
