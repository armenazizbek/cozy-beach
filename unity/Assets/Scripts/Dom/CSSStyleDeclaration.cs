using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

// CSSStyleDeclaration mapping to VisualElement.style (Section 3 of PORT_SPEC.md)

public class CSSStyleDeclaration
{
    private readonly HTMLElement _owner;
    private readonly Dictionary<string, string> _props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal CSSStyleDeclaration(HTMLElement owner)
    {
        _owner = owner;
    }

    public string width { get => getPropertyValue("width"); set => setProperty("width", value); }
    public string height { get => getPropertyValue("height"); set => setProperty("height", value); }
    public string left { get => getPropertyValue("left"); set => setProperty("left", value); }
    public string top { get => getPropertyValue("top"); set => setProperty("top", value); }
    public string right { get => getPropertyValue("right"); set => setProperty("right", value); }
    public string bottom { get => getPropertyValue("bottom"); set => setProperty("bottom", value); }
    public string display { get => getPropertyValue("display"); set => setProperty("display", value); }
    public string opacity { get => getPropertyValue("opacity"); set => setProperty("opacity", value); }
    public string transform { get => getPropertyValue("transform"); set => setProperty("transform", value); }
    public string background { get => getPropertyValue("background"); set => setProperty("background", value); }
    public string backgroundColor { get => getPropertyValue("background-color"); set => setProperty("background-color", value); }
    public string color { get => getPropertyValue("color"); set => setProperty("color", value); }
    public string cursor { get => getPropertyValue("cursor"); set => setProperty("cursor", value); }
    public string visibility { get => getPropertyValue("visibility"); set => setProperty("visibility", value); }
    public string pointerEvents { get => getPropertyValue("pointer-events"); set => setProperty("pointer-events", value); }
    public string transition { get => getPropertyValue("transition"); set => setProperty("transition", value); }
    public string borderColor { get => getPropertyValue("border-color"); set => setProperty("border-color", value); }
    public string fontSize { get => getPropertyValue("font-size"); set => setProperty("font-size", value); }
    public string maxHeight { get => getPropertyValue("max-height"); set => setProperty("max-height", value); }
    public string zIndex { get => getPropertyValue("z-index"); set => setProperty("z-index", value); }

    public string cssText
    {
        get
        {
            if (_props.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var kv in _props)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(kv.Key).Append(": ").Append(kv.Value);
            }
            return sb.ToString();
        }
        set
        {
            var keys = new List<string>(_props.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                setProperty(keys[i], "");
            }
            _props.Clear();
            if (string.IsNullOrWhiteSpace(value)) return;
            string[] decls = value.Split(';');
            for (int i = 0; i < decls.Length; i++)
            {
                string d = decls[i];
                int colon = d.IndexOf(':');
                if (colon <= 0) continue;
                string k = d.Substring(0, colon).Trim();
                string v = d.Substring(colon + 1).Trim();
                if (!string.IsNullOrEmpty(k))
                {
                    setProperty(k, v);
                }
            }
        }
    }

    public string getPropertyValue(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        string norm = NormalizePropName(name);
        return _props.TryGetValue(norm, out var v) ? (v ?? "") : "";
    }

    public void removeProperty(string name)
    {
        setProperty(name, "");
    }

    public void setProperty(string name, string value)
    {
        if (string.IsNullOrEmpty(name)) return;
        string norm = NormalizePropName(name);
        string val = value?.Trim() ?? "";
        if (string.IsNullOrEmpty(val))
        {
            _props.Remove(norm);
        }
        else
        {
            _props[norm] = val;
        }
        ApplyToVisualElement(norm, val);
    }

    private static string NormalizePropName(string name)
    {
        if (name.StartsWith("--", StringComparison.Ordinal)) return name;
        switch (name)
        {
            case "backgroundColor": return "background-color";
            case "pointerEvents": return "pointer-events";
            case "borderColor": return "border-color";
            case "fontSize": return "font-size";
            case "maxHeight": return "max-height";
            case "minHeight": return "min-height";
            case "maxWidth": return "max-width";
            case "minWidth": return "min-width";
            case "zIndex": return "z-index";
            default: return name.ToLowerInvariant();
        }
    }

    private void ApplyToVisualElement(string prop, string val)
    {
        VisualElement ve = _owner?.ve;
        if (ve == null) return;
        IStyle s = ve.style;

        if (prop.StartsWith("--", StringComparison.Ordinal))
        {
            // Custom property --tilt on .polaroid in CampModal.ts rotates the element by that angle
            if (string.Equals(prop, "--tilt", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(val))
                {
                    s.rotate = StyleKeyword.Null;
                }
                else
                {
                    float deg = ParseAngleDegrees(val);
                    s.rotate = new StyleRotate(new Rotate(new Angle(deg, AngleUnit.Degree)));
                }
            }
            return;
        }

        switch (prop)
        {
            case "width":
                s.width = ParseStyleLength(val);
                break;
            case "height":
                s.height = ParseStyleLength(val);
                break;
            case "left":
                s.left = ParseStyleLength(val);
                break;
            case "top":
                s.top = ParseStyleLength(val);
                break;
            case "right":
                s.right = ParseStyleLength(val);
                break;
            case "bottom":
                s.bottom = ParseStyleLength(val);
                break;
            case "max-height":
                s.maxHeight = ParseStyleLength(val);
                break;
            case "max-width":
                s.maxWidth = ParseStyleLength(val);
                break;
            case "min-height":
                s.minHeight = ParseStyleLength(val);
                break;
            case "min-width":
                s.minWidth = ParseStyleLength(val);
                break;
            case "font-size":
                s.fontSize = ParseStyleLength(val);
                break;
            case "display":
                // the element ranks this against the .hidden class and the hidden attribute
                _owner.ApplyDisplay();
                break;
            case "visibility":
                if (string.IsNullOrEmpty(val))
                {
                    s.visibility = StyleKeyword.Null;
                }
                else if (string.Equals(val, "hidden", StringComparison.OrdinalIgnoreCase))
                {
                    s.visibility = Visibility.Hidden;
                }
                else
                {
                    s.visibility = Visibility.Visible;
                }
                break;
            case "opacity":
                if (string.IsNullOrEmpty(val))
                {
                    s.opacity = StyleKeyword.Null;
                }
                else if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float op))
                {
                    s.opacity = Mathf.Clamp01(op);
                }
                break;
            case "background":
            case "background-color":
                if (string.IsNullOrEmpty(val))
                {
                    s.backgroundColor = StyleKeyword.Null;
                }
                else if (TryParseCssColor(val, out Color bgCol))
                {
                    s.backgroundColor = bgCol;
                }
                break;
            case "color":
                if (string.IsNullOrEmpty(val))
                {
                    s.color = StyleKeyword.Null;
                }
                else if (TryParseCssColor(val, out Color fgCol))
                {
                    s.color = fgCol;
                }
                break;
            case "border-color":
                if (string.IsNullOrEmpty(val))
                {
                    s.borderTopColor = StyleKeyword.Null;
                    s.borderRightColor = StyleKeyword.Null;
                    s.borderBottomColor = StyleKeyword.Null;
                    s.borderLeftColor = StyleKeyword.Null;
                }
                else if (TryParseCssColor(val, out Color bCol))
                {
                    s.borderTopColor = bCol;
                    s.borderRightColor = bCol;
                    s.borderBottomColor = bCol;
                    s.borderLeftColor = bCol;
                }
                break;
            case "pointer-events":
                _owner.UpdatePickingMode();
                break;
            case "transform":
                ApplyTransform(s, val);
                break;
            case "z-index":
                _owner.parentElement?.ReorderChildrenByZIndex();
                break;
            case "overflow":
            case "overflow-y":
                if (string.Equals(val, "auto", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(val, "scroll", StringComparison.OrdinalIgnoreCase))
                {
                    _owner.EnsureScrollView();
                }
                break;
            case "position":
                if (string.Equals(val, "absolute", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(val, "fixed", StringComparison.OrdinalIgnoreCase))
                {
                    s.position = Position.Absolute;
                }
                else if (string.Equals(val, "relative", StringComparison.OrdinalIgnoreCase))
                {
                    s.position = Position.Relative;
                }
                break;
            case "cursor":
            case "transition":
            case "vertical-align":
            default:
                // Stored in _props and ignored if unsupported by UI Toolkit
                break;
        }
    }

    internal StyleLength ParseStyleLength(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return StyleKeyword.Null;
        string s = raw.Trim();
        if (string.Equals(s, "auto", StringComparison.OrdinalIgnoreCase))
            return StyleKeyword.Auto;
        if (string.Equals(s, "none", StringComparison.OrdinalIgnoreCase))
            return StyleKeyword.None;
        if (string.Equals(s, "initial", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "unset", StringComparison.OrdinalIgnoreCase))
            return StyleKeyword.Initial;

        if (s.EndsWith("%", StringComparison.Ordinal))
        {
            if (float.TryParse(s.Substring(0, s.Length - 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return new StyleLength(Length.Percent(pct));
        }
        else if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 2).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
                return new StyleLength(px);
        }
        else if (s.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 2).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float em))
                return new StyleLength(em * _owner.GetEffectiveFontSize());
        }
        else if (s.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 3).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float rem))
                return new StyleLength(rem * 16f);
        }
        else if (s.EndsWith("vw", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 2).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float vw))
                return new StyleLength((float)(vw * 0.01 * window.innerWidth));
        }
        else if (s.EndsWith("vh", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 2).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float vh))
                return new StyleLength((float)(vh * 0.01 * window.innerHeight));
        }
        else if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float num))
        {
            return new StyleLength(num);
        }
        return StyleKeyword.Null;
    }

    internal Length ParseLengthValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new Length(0f);
        string s = raw.Trim();
        if (s.EndsWith("%", StringComparison.Ordinal))
        {
            if (float.TryParse(s.Substring(0, s.Length - 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return Length.Percent(pct);
        }
        else if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 2).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
                return new Length(px);
        }
        else if (s.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 2).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float em))
                return new Length(em * _owner.GetEffectiveFontSize());
        }
        else if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float num))
        {
            return new Length(num);
        }
        return new Length(0f);
    }

    internal float ParseAngleDegrees(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0f;
        string s = raw.Trim();
        if (s.StartsWith("var(", StringComparison.OrdinalIgnoreCase) && s.EndsWith(")", StringComparison.Ordinal))
        {
            string varName = s.Substring(4, s.Length - 5).Trim();
            int comma = varName.IndexOf(',');
            string fallback = "";
            if (comma >= 0)
            {
                fallback = varName.Substring(comma + 1).Trim();
                varName = varName.Substring(0, comma).Trim();
            }
            string resolved = getPropertyValue(varName);
            s = !string.IsNullOrEmpty(resolved) ? resolved : fallback;
        }
        if (s.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 3).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                return d;
        }
        else if (s.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 3).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
                return r * Mathf.Rad2Deg;
        }
        else if (s.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 4).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
                return t * 360f;
        }
        else if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float n))
        {
            return n;
        }
        return 0f;
    }

    private void ApplyTransform(IStyle s, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            s.translate = StyleKeyword.Null;
            s.rotate = StyleKeyword.Null;
            s.scale = StyleKeyword.Null;
            return;
        }
        if (string.Equals(raw.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            s.translate = new StyleTranslate(new Translate(0f, 0f, 0f));
            s.rotate = new StyleRotate(new Rotate(new Angle(0f, AngleUnit.Degree)));
            s.scale = new StyleScale(new Scale(Vector3.one));
            return;
        }

        var matches = Regex.Matches(raw, @"([a-zA-Z0-9]+)\s*\(([^)]*)\)");
        Length tx = new Length(0f), ty = new Length(0f);
        bool hasTranslate = false;
        float rotDeg = 0f;
        bool hasRotate = false;
        float sx = 1f, sy = 1f;
        bool hasScale = false;

        foreach (Match m in matches)
        {
            string fn = m.Groups[1].Value.ToLowerInvariant();
            string argsRaw = m.Groups[2].Value.Trim();
            string[] args = Regex.Split(argsRaw, @"[\s,]+");
            switch (fn)
            {
                case "translate":
                case "translate3d":
                    if (args.Length >= 1 && !string.IsNullOrEmpty(args[0])) tx = ParseLengthValue(args[0]);
                    if (args.Length >= 2 && !string.IsNullOrEmpty(args[1])) ty = ParseLengthValue(args[1]);
                    hasTranslate = true;
                    break;
                case "translatex":
                    if (args.Length >= 1 && !string.IsNullOrEmpty(args[0])) tx = ParseLengthValue(args[0]);
                    hasTranslate = true;
                    break;
                case "translatey":
                    if (args.Length >= 1 && !string.IsNullOrEmpty(args[0])) ty = ParseLengthValue(args[0]);
                    hasTranslate = true;
                    break;
                case "rotate":
                case "rotatez":
                    rotDeg += ParseAngleDegrees(argsRaw);
                    hasRotate = true;
                    break;
                case "scale":
                    if (args.Length >= 1 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float s0))
                    {
                        sx = s0;
                        sy = args.Length >= 2 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s1) ? s1 : s0;
                        hasScale = true;
                    }
                    break;
                case "scalex":
                    if (args.Length >= 1 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float scx))
                    {
                        sx = scx;
                        hasScale = true;
                    }
                    break;
                case "scaley":
                    if (args.Length >= 1 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float scy))
                    {
                        sy = scy;
                        hasScale = true;
                    }
                    break;
            }
        }

        if (hasTranslate) s.translate = new StyleTranslate(new Translate(tx, ty, 0f));
        if (hasRotate) s.rotate = new StyleRotate(new Rotate(new Angle(rotDeg, AngleUnit.Degree)));
        if (hasScale) s.scale = new StyleScale(new Scale(new Vector3(sx, sy, 1f)));
    }

    internal static bool TryParseCssColor(string raw, out Color color)
    {
        color = Color.clear;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string s = raw.Trim();
        if (string.Equals(s, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.clear;
            return true;
        }
        // Handle CSS variables from :root if present
        if (s.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
        {
            if (s.IndexOf("--ink", StringComparison.OrdinalIgnoreCase) >= 0) s = "#3b2a1e";
            else if (s.IndexOf("--paper2", StringComparison.OrdinalIgnoreCase) >= 0) s = "#efdfc0";
            else if (s.IndexOf("--paper", StringComparison.OrdinalIgnoreCase) >= 0) s = "#f7ecd6";
            else if (s.IndexOf("--accent2", StringComparison.OrdinalIgnoreCase) >= 0) s = "#b9582a";
            else if (s.IndexOf("--accent", StringComparison.OrdinalIgnoreCase) >= 0) s = "#d9803f";
            else if (s.IndexOf("--leaf", StringComparison.OrdinalIgnoreCase) >= 0) s = "#6ea84f";
            else if (s.IndexOf("--glass", StringComparison.OrdinalIgnoreCase) >= 0) s = "rgba(255, 244, 222, 0.88)";
        }
        // If gradient, extract first color token
        if (s.IndexOf("gradient(", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var m = Regex.Match(s, @"#[0-9a-fA-F]{3,8}|rgba?\([^)]+\)");
            if (m.Success) s = m.Value;
            else return false;
        }
        var rgba = THREE.CssColor.Parse(s);
        color = new Color((float)rgba.r, (float)rgba.g, (float)rgba.b, (float)rgba.a);
        return true;
    }
}
