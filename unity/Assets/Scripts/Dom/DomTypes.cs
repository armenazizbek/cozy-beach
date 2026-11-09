using System;
using System.Collections.Generic;
using System.Reflection;

// DOM Event, DOMRect, DOMTokenList, DOMStringMap, Node (Section 3 of PORT_SPEC.md)

public class EventInit
{
    public bool bubbles;
    public double clientX;
    public double clientY;
    public int button;
    public double deltaY;
    public string key;
    public string code;
    public bool shiftKey;
    public bool ctrlKey;
    public bool altKey;
}

public class Event
{
    public string type;
    public HTMLElement target, currentTarget;
    public string key, code;
    public int button;
    public int buttons;
    public double clientX, clientY, deltaY;
    public double movementX, movementY;
    public bool shiftKey, ctrlKey, altKey;
    public bool repeat;
    public bool bubbles;
    public bool defaultPrevented;
    public bool propagationStopped;

    public Event() { }

    public Event(string type, object init = null)
    {
        this.type = type;
        if (init is EventInit ei)
        {
            bubbles = ei.bubbles;
            clientX = ei.clientX;
            clientY = ei.clientY;
            button = ei.button;
            deltaY = ei.deltaY;
            key = ei.key;
            code = ei.code;
            shiftKey = ei.shiftKey;
            ctrlKey = ei.ctrlKey;
            altKey = ei.altKey;
        }
        else if (init != null)
        {
            var t = init.GetType();
            TryReadProp(t, init, "bubbles", ref bubbles);
            TryReadProp(t, init, "clientX", ref clientX);
            TryReadProp(t, init, "clientY", ref clientY);
            TryReadProp(t, init, "button", ref button);
            TryReadProp(t, init, "deltaY", ref deltaY);
            TryReadProp(t, init, "key", ref key);
            TryReadProp(t, init, "code", ref code);
            TryReadProp(t, init, "shiftKey", ref shiftKey);
            TryReadProp(t, init, "ctrlKey", ref ctrlKey);
            TryReadProp(t, init, "altKey", ref altKey);
        }
    }

    private static void TryReadProp<T>(Type t, object obj, string name, ref T dst)
    {
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p != null)
        {
            var v = p.GetValue(obj);
            if (v is T tv) { dst = tv; return; }
            if (typeof(T) == typeof(double) && v is IConvertible c)
            {
                dst = (T)(object)c.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }
            if (typeof(T) == typeof(int) && v is IConvertible ci)
            {
                dst = (T)(object)ci.ToInt32(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }
        }
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (f != null)
        {
            var v = f.GetValue(obj);
            if (v is T tv) { dst = tv; return; }
            if (typeof(T) == typeof(double) && v is IConvertible c)
            {
                dst = (T)(object)c.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }
            if (typeof(T) == typeof(int) && v is IConvertible ci)
            {
                dst = (T)(object)ci.ToInt32(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }
        }
    }

    public void preventDefault()
    {
        defaultPrevented = true;
    }

    public void stopPropagation()
    {
        propagationStopped = true;
    }
}

public class MouseEvent : Event
{
    public MouseEvent() { }
    public MouseEvent(string type, object init = null) : base(type, init) { }
}

public class PointerEvent : MouseEvent
{
    public PointerEvent() { }
    public PointerEvent(string type, object init = null) : base(type, init) { }
}

public class KeyboardEvent : Event
{
    public KeyboardEvent() { }
    public KeyboardEvent(string type, object init = null) : base(type, init) { }
}

public class WheelEvent : MouseEvent
{
    public WheelEvent() { }
    public WheelEvent(string type, object init = null) : base(type, init) { }
}

public class DOMRect
{
    public double left, top, right, bottom, width, height, x, y;
}

public class DOMTokenList
{
    private readonly HTMLElement _owner;
    private readonly List<string> _tokens = new List<string>();

    internal DOMTokenList(HTMLElement owner)
    {
        _owner = owner;
    }

    internal IReadOnlyList<string> Tokens => _tokens;

    public int length => _tokens.Count;

    public void add(params string[] c)
    {
        if (c == null || c.Length == 0) return;
        bool changed = false;
        for (int i = 0; i < c.Length; i++)
        {
            string item = c[i];
            if (string.IsNullOrWhiteSpace(item)) continue;
            string[] parts = item.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int j = 0; j < parts.Length; j++)
            {
                string tok = parts[j];
                if (!_tokens.Contains(tok))
                {
                    _tokens.Add(tok);
                    _owner.ve?.AddToClassList(tok);
                    changed = true;
                }
            }
        }
        if (changed) _owner.OnClassTokensModified();
    }

    public void remove(params string[] c)
    {
        if (c == null || c.Length == 0) return;
        bool changed = false;
        for (int i = 0; i < c.Length; i++)
        {
            string item = c[i];
            if (string.IsNullOrWhiteSpace(item)) continue;
            string[] parts = item.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int j = 0; j < parts.Length; j++)
            {
                string tok = parts[j];
                if (_tokens.Remove(tok))
                {
                    _owner.ve?.RemoveFromClassList(tok);
                    changed = true;
                }
            }
        }
        if (changed) _owner.OnClassTokensModified();
    }

    public bool toggle(string c, bool? force = null)
    {
        if (string.IsNullOrWhiteSpace(c)) return false;
        string tok = c.Trim();
        bool has = _tokens.Contains(tok);
        bool want = force ?? !has;
        if (want && !has)
        {
            _tokens.Add(tok);
            _owner.ve?.AddToClassList(tok);
            _owner.OnClassTokensModified();
        }
        else if (!want && has)
        {
            _tokens.Remove(tok);
            _owner.ve?.RemoveFromClassList(tok);
            _owner.OnClassTokensModified();
        }
        return want;
    }

    public bool contains(string c)
    {
        if (string.IsNullOrEmpty(c)) return false;
        return _tokens.Contains(c);
    }

    internal void SetFromClassNameString(string cls)
    {
        for (int i = 0; i < _tokens.Count; i++)
        {
            _owner.ve?.RemoveFromClassList(_tokens[i]);
        }
        _tokens.Clear();
        if (!string.IsNullOrWhiteSpace(cls))
        {
            string[] parts = cls.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string tok = parts[i];
                if (!_tokens.Contains(tok))
                {
                    _tokens.Add(tok);
                    _owner.ve?.AddToClassList(tok);
                }
            }
        }
        _owner.OnClassTokensModified();
    }

    public override string ToString()
    {
        return _tokens.Count == 0 ? "" : string.Join(" ", _tokens);
    }
}

public class DOMStringMap
{
    private readonly HTMLElement _owner;
    private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

    internal DOMStringMap(HTMLElement owner)
    {
        _owner = owner;
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _map.TryGetValue(key, out var val) ? val : null;
        }
        set
        {
            if (string.IsNullOrEmpty(key)) return;
            if (value == null)
            {
                _map.Remove(key);
                _owner.SetRawAttribute("data-" + CamelToKebab(key), null);
            }
            else
            {
                _map[key] = value;
                _owner.SetRawAttribute("data-" + CamelToKebab(key), value);
            }
        }
    }

    public bool ContainsKey(string k)
    {
        return !string.IsNullOrEmpty(k) && _map.ContainsKey(k);
    }

    public bool TryGetValue(string k, out string value)
    {
        if (string.IsNullOrEmpty(k))
        {
            value = null;
            return false;
        }
        return _map.TryGetValue(k, out value);
    }

    internal void SetFromDataAttribute(string dataAttrName, string value)
    {
        if (string.IsNullOrEmpty(dataAttrName) || !dataAttrName.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
            return;
        string camel = KebabToCamel(dataAttrName.Substring(5));
        if (value == null) _map.Remove(camel);
        else _map[camel] = value;
    }

    internal static string CamelToKebab(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsUpper(c))
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    internal static string KebabToCamel(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        bool upperNext = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '-')
            {
                upperNext = true;
            }
            else if (upperNext)
            {
                sb.Append(char.ToUpperInvariant(c));
                upperNext = false;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }
}

public static class Node
{
    public const int DOCUMENT_POSITION_DISCONNECTED = 1;
    public const int DOCUMENT_POSITION_PRECEDING = 2;
    public const int DOCUMENT_POSITION_FOLLOWING = 4;
    public const int DOCUMENT_POSITION_CONTAINS = 8;
    public const int DOCUMENT_POSITION_CONTAINED_BY = 16;
}
