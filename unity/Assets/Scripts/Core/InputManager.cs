using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Port of src/core/InputManager.ts. The browser's pointer and key events become a poll of the Input System each frame that
// raises the same handlers with the same values: DOM key codes ("KeyW", "ShiftLeft"), mouse buttons 0 / 1 / 2, movement in
// CSS pixels with y pointing down.

public class InputHandlers
{
    public Action<int> onPointerDown;
    public Action<int, bool> onPointerUp;
    /// <summary>every move; dx,dy in CSS pixels since last move; buttons is the held-button bitmask</summary>
    public Action<double, double, int> onPointerMove;
    public Action<int> onClick;
    public Action<double> onWheel;
    public Action<string> onKey;
}

public class InputManager
{
    private const double DRAG_THRESHOLD = 5;

    public readonly THREE.Vector2 pointer = new THREE.Vector2(0, 0);
    public readonly HashSet<string> keys = new HashSet<string>();
    /// <summary>Keys held down by the -autoshot smoke test (Core/AutoShot.cs) as if pressed, even when the window has no focus.</summary>
    internal readonly HashSet<string> held = new HashSet<string>();
    public InputHandlers handlers = new InputHandlers();
    public int buttons = 0;
    /// <summary>the controller is moving the pointer, so the mouse leaves it and the held buttons alone</summary>
    public bool padPointer = false;
    /// <summary>pointer speed in px/s, smoothed</summary>
    public double speed = 0;
    /// <summary>true while the pointer is over a menu or panel, so a press there does not reach the game</summary>
    public Func<bool> overUI = () => false;
    /// <summary>true while a text box has the keyboard (typing a save name)</summary>
    public Func<bool> typing = () => false;
    private readonly THREE.Raycaster raycaster = new THREE.Raycaster();
    private double startX, startY;
    private bool dragging;
    private int downButton = -1;
    private bool pressedOverUI;

    /// <summary>Whether the mouse is captured, as document.pointerLockElement was.</summary>
    public bool locked => Cursor.lockState == CursorLockMode.Locked;

    public void requestPointerLock()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void exitPointerLock()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>CSS pixels per screen pixel, so mouse speed matches the browser build on a scaled display.</summary>
    private static double CssScale => Screen.dpi > 0 ? 96.0 / Screen.dpi : 1;

    private void setPointer(Vector2 screenPos)
    {
        pointer.set(screenPos.x / Math.Max(1, Screen.width) * 2 - 1, screenPos.y / Math.Max(1, Screen.height) * 2 - 1);
    }

    private static int Buttons(Mouse m) => (m.leftButton.isPressed ? 1 : 0) | (m.rightButton.isPressed ? 2 : 0) | (m.middleButton.isPressed ? 4 : 0);

    /// <summary>Reads the keyboard and mouse once a frame and raises the handlers, like the browser's events.</summary>
    public void poll()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            bool block = typing();
            foreach (var k in kb.allKeys)
            {
                if (k == null) continue;
                string code = DomCode(k.keyCode);
                if (code == null) continue;
                if (k.wasPressedThisFrame && !block)
                {
                    if (keys.Add(code)) handlers.onKey?.Invoke(code);
                }
                else if (!k.isPressed && keys.Contains(code)) keys.Remove(code);
            }
            if (block) keys.Clear();
        }
        if (!Application.isFocused) keys.Clear();
        foreach (var h in held) keys.Add(h);

        var m = Mouse.current;
        if (m == null) return;
        var pos = m.position.ReadValue();
        var delta = m.delta.ReadValue();
        int b = Buttons(m);
        double css = CssScale;
        // presses
        foreach (var (ctl, id) in new (ButtonControl, int)[] { (m.leftButton, 0), (m.middleButton, 1), (m.rightButton, 2) })
        {
            if (ctl.wasPressedThisFrame)
            {
                pressedOverUI = overUI();
                if (pressedOverUI) continue;
                if (!locked) setPointer(pos);
                buttons = b;
                downButton = id;
                startX = pos.x;
                startY = pos.y;
                dragging = false;
                handlers.onPointerDown?.Invoke(id);
            }
        }
        // movement
        if (delta.sqrMagnitude > 0 && !padPointer)
        {
            if (locked) pointer.set(0, 0);
            else setPointer(pos);
            double dx = delta.x * css, dy = -delta.y * css;
            double dt = Math.Max(1e-3, Time.unscaledDeltaTime);
            speed += (JS.Hypot(dx, dy) / dt - speed) * 0.35;
            buttons = b;
            if (b != 0 && JS.Hypot(pos.x - startX, pos.y - startY) * css > DRAG_THRESHOLD) dragging = true;
            handlers.onPointerMove?.Invoke(dx, dy, b);
        }
        // releases
        foreach (var (ctl, id) in new (ButtonControl, int)[] { (m.leftButton, 0), (m.middleButton, 1), (m.rightButton, 2) })
        {
            if (!ctl.wasReleasedThisFrame || downButton == -1) continue;
            buttons = b;
            bool wasDrag = dragging;
            handlers.onPointerUp?.Invoke(id, wasDrag);
            if (!wasDrag && !pressedOverUI) handlers.onClick?.Invoke(id);
            dragging = false;
            downButton = -1;
        }
        var scroll = m.scroll.ReadValue();
        if (Math.Abs(scroll.y) > 0.01 && !overUI()) handlers.onWheel?.Invoke(-Math.Sign(scroll.y));
    }

    /// <summary>call each frame to let speed decay when the pointer is still</summary>
    public void tick(double dt) => speed *= Math.Exp(-dt * 8);

    public THREE.Raycaster ray(THREE.Camera camera, bool center = false)
    {
        raycaster.setFromCamera(locked || center ? new THREE.Vector2(0, 0) : pointer, camera);
        return raycaster;
    }

    /// <summary>The browser's KeyboardEvent.code for an Input System key.</summary>
    public static string DomCode(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return "Key" + (char)('A' + (k - Key.A));
        if (k >= Key.Digit1 && k <= Key.Digit9) return "Digit" + (char)('1' + (k - Key.Digit1));
        switch (k)
        {
            case Key.Digit0: return "Digit0";
            case Key.Space: return "Space";
            case Key.Enter: return "Enter";
            case Key.Tab: return "Tab";
            case Key.Escape: return "Escape";
            case Key.Backspace: return "Backspace";
            case Key.LeftShift: return "ShiftLeft";
            case Key.RightShift: return "ShiftRight";
            case Key.LeftCtrl: return "ControlLeft";
            case Key.RightCtrl: return "ControlRight";
            case Key.LeftAlt: return "AltLeft";
            case Key.RightAlt: return "AltRight";
            case Key.UpArrow: return "ArrowUp";
            case Key.DownArrow: return "ArrowDown";
            case Key.LeftArrow: return "ArrowLeft";
            case Key.RightArrow: return "ArrowRight";
            case Key.Minus: return "Minus";
            case Key.Equals: return "Equal";
            case Key.Comma: return "Comma";
            case Key.Period: return "Period";
            case Key.Slash: return "Slash";
            case Key.Semicolon: return "Semicolon";
            case Key.Quote: return "Quote";
            case Key.Backquote: return "Backquote";
            case Key.LeftBracket: return "BracketLeft";
            case Key.RightBracket: return "BracketRight";
            case Key.Backslash: return "Backslash";
            case Key.Delete: return "Delete";
            case Key.Home: return "Home";
            case Key.End: return "End";
            case Key.PageUp: return "PageUp";
            case Key.PageDown: return "PageDown";
            case Key.F1: return "F1";
            case Key.F2: return "F2";
            case Key.F3: return "F3";
            case Key.F4: return "F4";
            case Key.F5: return "F5";
            case Key.F6: return "F6";
            case Key.F7: return "F7";
            case Key.F8: return "F8";
            case Key.F9: return "F9";
            case Key.F10: return "F10";
            case Key.F11: return "F11";
            case Key.F12: return "F12";
            case Key.Numpad0: return "Numpad0";
            case Key.Numpad1: return "Numpad1";
            case Key.Numpad2: return "Numpad2";
            case Key.Numpad3: return "Numpad3";
            case Key.Numpad4: return "Numpad4";
            case Key.Numpad5: return "Numpad5";
            case Key.Numpad6: return "Numpad6";
            case Key.Numpad7: return "Numpad7";
            case Key.Numpad8: return "Numpad8";
            case Key.Numpad9: return "Numpad9";
            case Key.NumpadEnter: return "NumpadEnter";
            default: return null;
        }
    }
}
