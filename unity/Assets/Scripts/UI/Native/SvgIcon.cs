using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// The UI's drawings (the tool icons, the shop's pictures, the detector on the intro card) at run time (unity/UI_PLAN.md
/// U10): their pixels come baked from the editor (IconAtlas, made by Editor/IconBaker.cs with Editor/SvgRasterizer.cs), so
/// nothing is drawn now; only the size they show at is worked out here, from the drawing's width, height and viewBox and
/// the text size around it, as the rasteriser did.
/// </summary>
public static class SvgIcon
{


    private static readonly System.Collections.Generic.HashSet<string> missing = new System.Collections.Generic.HashSet<string>();

    /// <summary>The baked picture for a drawing and the size it shows at (null when it was not baked, which is reported once).</summary>
    public static Texture2D Get(string svgMarkup, float fontSizePx, out float displayWidthPx, out float displayHeightPx)
    {
        displayWidthPx = 18f;
        displayHeightPx = 18f;
        if (string.IsNullOrWhiteSpace(svgMarkup)) return null;
        float emBase = fontSizePx > 1f ? fontSizePx : 16.36f; // 1.1em * 16.36 ~= 18px default
        var rootAttrs = ParseAttributes(ExtractOpeningTag(svgMarkup, "svg"));
        double vbW = 32, vbH = 32;
        if (rootAttrs.TryGetValue("viewBox", out string vbStr) && !string.IsNullOrWhiteSpace(vbStr))
        {
            double[] nums = ParseNumberList(vbStr);
            if (nums.Length >= 4 && nums[2] > 0 && nums[3] > 0)
            {
                vbW = nums[2];
                vbH = nums[3];
            }
        }
        displayWidthPx = rootAttrs.TryGetValue("width", out string wAttr) ? ParseSvgCssSize(wAttr, emBase, (float)vbW) : (float)(1.1 * emBase);
        displayHeightPx = rootAttrs.TryGetValue("height", out string hAttr) ? ParseSvgCssSize(hAttr, emBase, (float)vbH) : (float)(1.1 * emBase);
        var baked = IconAtlas.Get(svgMarkup);
        if (baked == null && missing.Add(svgMarkup)) Debug.LogWarning("[SvgIcon] a drawing was not baked (Editor/IconBaker.cs): " + svgMarkup.Substring(0, Math.Min(60, svgMarkup.Length)));
        return baked;
    }

    private static string ExtractOpeningTag(string markup, string tag)
    {
        var m = Regex.Match(markup, @"<" + tag + @"\b([^>]*)>", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static Dictionary<string, string> ParseAttributes(string attrText)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(attrText)) return dict;
        var m = Regex.Matches(attrText, @"([a-zA-Z0-9:_-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))");
        foreach (Match match in m)
        {
            string key = match.Groups[1].Value;
            string val = match.Groups[2].Success ? match.Groups[2].Value
                       : match.Groups[3].Success ? match.Groups[3].Value
                       : match.Groups[4].Value;
            dict[key] = val;
        }
        return dict;
    }

    private static double[] ParseNumberList(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<double>();
        var matches = Regex.Matches(s, @"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?");
        var res = new double[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            double.TryParse(matches[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out res[i]);
        }
        return res;
    }

    private static float ParseSvgCssSize(string raw, float emBase, float fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        string s = raw.Trim();
        if (s.EndsWith("em", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 2).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float em))
                return Mathf.Round(em * emBase);
        }
        else if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s.Substring(0, s.Length - 2).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
                return px;
        }
        else if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float num))
        {
            return num;
        }
        return fallback;
    }
}
