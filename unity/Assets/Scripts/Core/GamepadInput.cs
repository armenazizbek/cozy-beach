using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Port of src/core/GamepadInput.ts: the "standard" gamepad layout read from the Input System's Gamepad.

public class StickValue
{
    public double x, y;
}

/// <summary>Reads the first connected controller once per frame and turns it into presses, sticks and menu steps.</summary>
public class GamepadInput
{
    /// <summary>'a' | 'b' | 'x' | 'y' | 'lb' | 'rb' | 'lt' | 'rt' | 'back' | 'start' | 'l3' | 'r3' | 'up' | 'down' | 'left' | 'right'</summary>
    private static readonly string[] BUTTONS = { "a", "b", "x", "y", "lb", "rb", "lt", "rt", "back", "start", "l3", "r3", "up", "down", "left", "right" };

    private const double DEAD = 0.2;

    private static void dead(double x, double y, StickValue o)
    {
        double m = JS.Hypot(x, y);
        if (m < DEAD)
        {
            o.x = o.y = 0;
            return;
        }
        double k = Math.Min(1, (m - DEAD) / (1 - DEAD)) / m;
        o.x = x * k;
        o.y = y * k;
    }

    /// <summary>left stick: x right, y down (like the browser), dead zone already removed</summary>
    public readonly StickValue move = new StickValue();
    /// <summary>right stick</summary>
    public readonly StickValue look = new StickValue();
    public double lt = 0, rt = 0;
    public bool connected = false;
    public Action<string, bool> onButton;
    /// <summary>a menu step: d-pad or left stick, with key repeat</summary>
    public Action<string> onNav;
    /// <summary>any real input at all, so the game can switch its prompts to controller wording</summary>
    public Action onActivity;

    private readonly HashSet<string> held = new HashSet<string>();
    private string navDir;
    private double navT;

    public bool isDown(string b) => held.Contains(b);
    /// <summary>True while any button at all is held.</summary>
    public bool anyDown => held.Count > 0;

    private static bool Pressed(Gamepad p, int i)
    {
        switch (i)
        {
            case 0: return p.buttonSouth.isPressed;
            case 1: return p.buttonEast.isPressed;
            case 2: return p.buttonWest.isPressed;
            case 3: return p.buttonNorth.isPressed;
            case 4: return p.leftShoulder.isPressed;
            case 5: return p.rightShoulder.isPressed;
            case 6: return p.leftTrigger.ReadValue() > 0.4f || p.leftTrigger.isPressed;
            case 7: return p.rightTrigger.ReadValue() > 0.4f || p.rightTrigger.isPressed;
            case 8: return p.selectButton.isPressed;
            case 9: return p.startButton.isPressed;
            case 10: return p.leftStickButton.isPressed;
            case 11: return p.rightStickButton.isPressed;
            case 12: return p.dpad.up.isPressed;
            case 13: return p.dpad.down.isPressed;
            case 14: return p.dpad.left.isPressed;
            case 15: return p.dpad.right.isPressed;
        }
        return false;
    }

    public void poll(double dt)
    {
        var pad = Gamepad.current;
        connected = pad != null;
        if (pad == null)
        {
            if (held.Count > 0)
            {
                held.Clear();
                navDir = null;
            }
            move.x = move.y = look.x = look.y = lt = rt = 0;
            return;
        }
        bool active = false;
        for (int i = 0; i < BUTTONS.Length; i++)
        {
            string name = BUTTONS[i];
            bool down = Pressed(pad, i);
            if (down != held.Contains(name))
            {
                if (down) held.Add(name);
                else held.Remove(name);
                active = true;
                onButton?.Invoke(name, down);
            }
        }
        lt = pad.leftTrigger.ReadValue();
        rt = pad.rightTrigger.ReadValue();
        var ls = pad.leftStick.ReadValue();
        var rs = pad.rightStick.ReadValue();
        // the browser's axes point down for y
        dead(ls.x, -ls.y, move);
        dead(rs.x, -rs.y, look);
        if (JS.Hypot(move.x, move.y) > 0.4 || JS.Hypot(look.x, look.y) > 0.4) active = true;
        if (active) onActivity?.Invoke();

        // menu stepping
        string dir = null;
        if (held.Contains("up")) dir = "up";
        else if (held.Contains("down")) dir = "down";
        else if (held.Contains("left")) dir = "left";
        else if (held.Contains("right")) dir = "right";
        else if (JS.Hypot(move.x, move.y) > 0.6)
            dir = Math.Abs(move.x) > Math.Abs(move.y) ? (move.x > 0 ? "right" : "left") : move.y > 0 ? "down" : "up";
        if (dir != navDir)
        {
            navDir = dir;
            navT = 0.4;
            if (dir != null) onNav?.Invoke(dir);
        }
        else if (dir != null)
        {
            navT -= dt;
            if (navT <= 0)
            {
                navT = 0.14;
                onNav?.Invoke(dir);
            }
        }
    }
}
