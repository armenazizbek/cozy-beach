using System;
using System.Collections.Generic;
using UnityEngine;

// document & window static classes (Section 3 of PORT_SPEC.md)

public static class document
{
    private static HTMLElement _documentElement;
    private static HTMLElement _body;
    public static HTMLElement activeElement;

    private static readonly Dictionary<string, List<Action<Event>>> _listeners =
        new Dictionary<string, List<Action<Event>>>(StringComparer.OrdinalIgnoreCase);

    public static HTMLElement documentElement
    {
        get
        {
            if (_documentElement == null) EnsureDetachedDocument();
            return _documentElement;
        }
        internal set => _documentElement = value;
    }

    public static HTMLElement body
    {
        get
        {
            if (_body == null) EnsureDetachedDocument();
            return _body;
        }
        internal set => _body = value;
    }

    private static void EnsureDetachedDocument()
    {
        if (_documentElement == null)
        {
            _documentElement = new HTMLElement("HTML");
        }
        if (_body == null)
        {
            _body = new HTMLElement("BODY");
            _documentElement.appendChild(_body);
        }
    }

    public static HTMLElement createElement(string tag)
    {
        string norm = string.IsNullOrWhiteSpace(tag) ? "DIV" : tag.Trim().ToUpperInvariant();
        return norm switch
        {
            "BUTTON" => new HTMLButtonElement(),
            "INPUT" => new HTMLInputElement(),
            "SELECT" => new HTMLSelectElement(),
            "OPTION" => new HTMLOptionElement(),
            "IMG" => new HTMLImageElement(),
            "CANVAS" => new HTMLCanvasElement(),
            "P" => new HTMLParagraphElement(),
            "DIV" => new HTMLDivElement(),
            "SPAN" => new HTMLSpanElement(),
            "LABEL" => new HTMLLabelElement(),
            "H1" or "H2" or "H3" or "H4" or "H5" or "H6" => new HTMLHeadingElement(norm),
            "UL" => new HTMLUListElement(),
            "LI" => new HTMLLIElement(),
            _ => new HTMLElement(norm),
        };
    }

    public static T createElement<T>(string tag) where T : HTMLElement
    {
        var el = createElement(tag);
        if (el is T typed) return typed;
        if (typeof(T) == typeof(HTMLButtonElement)) return new HTMLButtonElement() as T;
        if (typeof(T) == typeof(HTMLInputElement)) return new HTMLInputElement() as T;
        if (typeof(T) == typeof(HTMLSelectElement)) return new HTMLSelectElement() as T;
        if (typeof(T) == typeof(HTMLOptionElement)) return new HTMLOptionElement() as T;
        if (typeof(T) == typeof(HTMLImageElement)) return new HTMLImageElement() as T;
        if (typeof(T) == typeof(HTMLCanvasElement)) return new HTMLCanvasElement() as T;
        if (typeof(T) == typeof(HTMLParagraphElement)) return new HTMLParagraphElement() as T;
        return Activator.CreateInstance<T>();
    }

    public static HTMLElement getElementById(string id)
    {
        if (string.IsNullOrEmpty(id) || _documentElement == null) return null;
        return FindById(_documentElement, id);
    }

    public static T getElementById<T>(string id) where T : HTMLElement
    {
        return getElementById(id) as T;
    }

    private static HTMLElement FindById(HTMLElement root, string id)
    {
        if (root == null) return null;
        if (string.Equals(root.id, id, StringComparison.Ordinal)) return root;
        var ch = root.children;
        for (int i = 0; i < ch.Count; i++)
        {
            var found = FindById(ch[i], id);
            if (found != null) return found;
        }
        return null;
    }

    public static T querySelector<T>(string sel) where T : HTMLElement
    {
        return SelectorEngine.QuerySelector<T>(documentElement, sel);
    }

    public static HTMLElement querySelector(string sel)
    {
        return SelectorEngine.QuerySelector<HTMLElement>(documentElement, sel);
    }

    public static List<T> querySelectorAll<T>(string sel) where T : HTMLElement
    {
        return SelectorEngine.QuerySelectorAll<T>(documentElement, sel);
    }

    public static List<HTMLElement> querySelectorAll(string sel)
    {
        return SelectorEngine.QuerySelectorAll<HTMLElement>(documentElement, sel);
    }

    public static void addEventListener(string type, Action<Event> fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        if (!_listeners.TryGetValue(type, out var list))
        {
            list = new List<Action<Event>>();
            _listeners[type] = list;
        }
        if (!list.Contains(fn)) list.Add(fn);
    }

    public static void addEventListener(string type, Action fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        addEventListener(type, _ => fn());
    }

    public static void removeEventListener(string type, Action<Event> fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        if (_listeners.TryGetValue(type, out var list))
        {
            list.Remove(fn);
        }
    }

    internal static void DispatchEvent(Event e)
    {
        if (e == null || string.IsNullOrEmpty(e.type)) return;
        if (_listeners.TryGetValue(e.type, out var list) && list.Count > 0)
        {
            var copy = list.ToArray();
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i]?.Invoke(e);
                if (e.propagationStopped) break;
            }
        }
    }

    internal static void ClearListeners()
    {
        _listeners.Clear();
    }

    public static void exitPointerLock()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    public static bool pointerLocked => UnityEngine.Cursor.lockState == CursorLockMode.Locked;

    public static HTMLElement pointerLockElement => pointerLocked ? (getElementById("scene") ?? body) : null;

    public static HTMLElement fullscreenElement =>
        Screen.fullScreenMode != FullScreenMode.Windowed ? documentElement : null;

    public static void requestFullscreen()
    {
        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
    }

    public static void exitFullscreen()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;
    }
}

public static class window
{
    public class ScreenInfo
    {
        public int width => Screen.currentResolution.width > 0 ? Screen.currentResolution.width : Screen.width;
        public int height => Screen.currentResolution.height > 0 ? Screen.currentResolution.height : Screen.height;
    }

    public static readonly ScreenInfo screen = new ScreenInfo();

    internal static double _innerWidth = 1280;
    internal static double _innerHeight = 720;
    internal static double _devicePixelRatio = 1.0;
    internal static double _osPixelRatio = 1.0;

    /// <summary>
    /// Windows' own display scaling (125% is 1.25). The UI no longer follows it: it is laid out on a 1920 x 1080 page that is
    /// stretched to the screen, and <see cref="devicePixelRatio"/> is that stretch.
    /// </summary>
    public static double osPixelRatio
    {
        get
        {
            DomHost.RefreshMetricsIfNeeded();
            return _osPixelRatio;
        }
    }

    public static double innerWidth
    {
        get
        {
            DomHost.RefreshMetricsIfNeeded();
            return _innerWidth;
        }
        set => _innerWidth = value;
    }

    public static double innerHeight
    {
        get
        {
            DomHost.RefreshMetricsIfNeeded();
            return _innerHeight;
        }
        set => _innerHeight = value;
    }

    public static double devicePixelRatio
    {
        get
        {
            DomHost.RefreshMetricsIfNeeded();
            return _devicePixelRatio;
        }
        set => _devicePixelRatio = value;
    }

    private sealed class TimerTask
    {
        public int id;
        public Action fn;
        public Action<double> rafFn;
        public double nextFireMs;
        public double intervalMs;
        public bool isInterval;
        public bool isRaf;
        public bool cancelled;
    }

    private static int _nextTimerId = 1;
    private static readonly List<TimerTask> _timers = new List<TimerTask>();
    private static readonly Dictionary<string, List<Action<Event>>> _listeners =
        new Dictionary<string, List<Action<Event>>>(StringComparer.OrdinalIgnoreCase);

    private static double NowMs() => Time.realtimeSinceStartupAsDouble * 1000.0;

    public static int setTimeout(Action fn, double ms = 0)
    {
        if (fn == null) return 0;
        int id = _nextTimerId++;
        _timers.Add(new TimerTask
        {
            id = id,
            fn = fn,
            nextFireMs = NowMs() + Math.Max(0.0, ms),
            intervalMs = 0,
            isInterval = false,
            isRaf = false,
        });
        return id;
    }

    public static void clearTimeout(int id)
    {
        for (int i = 0; i < _timers.Count; i++)
        {
            if (_timers[i].id == id)
            {
                _timers[i].cancelled = true;
            }
        }
    }

    public static int setInterval(Action fn, double ms)
    {
        if (fn == null) return 0;
        int id = _nextTimerId++;
        double period = Math.Max(1.0, ms);
        _timers.Add(new TimerTask
        {
            id = id,
            fn = fn,
            nextFireMs = NowMs() + period,
            intervalMs = period,
            isInterval = true,
            isRaf = false,
        });
        return id;
    }

    public static void clearInterval(int id) => clearTimeout(id);

    public static int requestAnimationFrame(Action<double> fn)
    {
        if (fn == null) return 0;
        int id = _nextTimerId++;
        _timers.Add(new TimerTask
        {
            id = id,
            rafFn = fn,
            nextFireMs = 0,
            isRaf = true,
        });
        return id;
    }

    public static int requestAnimationFrame(Action fn)
    {
        if (fn == null) return 0;
        return requestAnimationFrame(_ => fn());
    }

    public static void cancelAnimationFrame(int id) => clearTimeout(id);

    internal static void RunTimers()
    {
        if (_timers.Count == 0) return;
        double now = NowMs();

        // Snapshot current count so newly queued rAFs/timeouts fire on the next tick
        int count = _timers.Count;
        for (int i = 0; i < count && i < _timers.Count; i++)
        {
            var t = _timers[i];
            if (t == null || t.cancelled) continue;

            if (t.isRaf)
            {
                t.cancelled = true;
                try { t.rafFn?.Invoke(now); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            else if (now >= t.nextFireMs)
            {
                if (t.isInterval)
                {
                    t.nextFireMs = now + t.intervalMs;
                }
                else
                {
                    t.cancelled = true;
                }
                try { t.fn?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        for (int i = _timers.Count - 1; i >= 0; i--)
        {
            if (_timers[i] == null || _timers[i].cancelled)
            {
                _timers.RemoveAt(i);
            }
        }
    }

    internal static void ClearAllTimersAndListeners()
    {
        _timers.Clear();
        _listeners.Clear();
    }

    public static void addEventListener(string type, Action<Event> fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        if (!_listeners.TryGetValue(type, out var list))
        {
            list = new List<Action<Event>>();
            _listeners[type] = list;
        }
        if (!list.Contains(fn)) list.Add(fn);
    }

    public static void addEventListener(string type, Action fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        addEventListener(type, _ => fn());
    }

    public static void removeEventListener(string type, Action<Event> fn)
    {
        if (string.IsNullOrEmpty(type) || fn == null) return;
        if (_listeners.TryGetValue(type, out var list))
        {
            list.Remove(fn);
        }
    }

    internal static void DispatchEvent(Event e)
    {
        if (e == null || string.IsNullOrEmpty(e.type)) return;
        if (_listeners.TryGetValue(e.type, out var list) && list.Count > 0)
        {
            var copy = list.ToArray();
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i]?.Invoke(e);
                if (e.propagationStopped) break;
            }
        }
    }

    public static void close()
    {
        Application.Quit();
    }
}
