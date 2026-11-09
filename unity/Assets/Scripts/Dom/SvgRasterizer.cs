using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

// Small SVG rasterizer that draws <svg>...</svg> markup through CanvasRenderingContext2D into a cached Texture2D.

internal static class SvgRasterizer
{
    private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

    private struct SvgState
    {
        public string fill;
        public string stroke;
        public double strokeWidth;
        public string strokeLinecap;
        public string strokeLinejoin;
        public double opacity;
        public bool fillExplicit;
    }

    public static Texture2D Rasterize(string svgMarkup, float fontSizePx, out float displayWidthPx, out float displayHeightPx)
    {
        displayWidthPx = 18f;
        displayHeightPx = 18f;
        if (string.IsNullOrWhiteSpace(svgMarkup)) return null;

        float emBase = fontSizePx > 1f ? fontSizePx : 16.36f; // 1.1em * 16.36 ~= 18px default
        var rootAttrs = ParseAttributes(ExtractOpeningTag(svgMarkup, "svg"));

        double vbX = 0, vbY = 0, vbW = 32, vbH = 32;
        if (rootAttrs.TryGetValue("viewBox", out string vbStr) && !string.IsNullOrWhiteSpace(vbStr))
        {
            double[] nums = ParseNumberList(vbStr);
            if (nums.Length >= 4 && nums[2] > 0 && nums[3] > 0)
            {
                vbX = nums[0];
                vbY = nums[1];
                vbW = nums[2];
                vbH = nums[3];
            }
        }

        displayWidthPx = rootAttrs.TryGetValue("width", out string wAttr) ? ParseSvgCssSize(wAttr, emBase, (float)vbW) : (float)(1.1 * emBase);
        displayHeightPx = rootAttrs.TryGetValue("height", out string hAttr) ? ParseSvgCssSize(hAttr, emBase, (float)vbH) : (float)(1.1 * emBase);

        if (_cache.TryGetValue(svgMarkup, out Texture2D cached) && cached != null)
        {
            return cached;
        }

        int texW = Math.Max(64, (int)Math.Ceiling(vbW * 2.0));
        int texH = Math.Max(64, (int)Math.Ceiling(vbH * 2.0));

        var canvas = new HTMLCanvasElement(texW, texH);
        var ctx = canvas.getContext("2d");
        ctx.clearRect(0, 0, texW, texH);
        ctx.save();
        ctx.scale(texW / vbW, texH / vbH);
        if (vbX != 0 || vbY != 0) ctx.translate(-vbX, -vbY);

        var initialState = new SvgState
        {
            fill = "#000000",
            stroke = "none",
            strokeWidth = 1.0,
            strokeLinecap = "butt",
            strokeLinejoin = "miter",
            opacity = 1.0,
            fillExplicit = false,
        };
        ApplyStyleAttributes(rootAttrs, ref initialState);

        var stateStack = new Stack<SvgState>();
        stateStack.Push(initialState);

        RenderSvgTokens(svgMarkup, ctx, stateStack);

        ctx.restore();

        byte[] rgba = canvas.ToUnityRGBA(flipY: true);
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            name = "SvgIcon",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixelData(rgba, 0);
        tex.Apply(false, false);

        _cache[svgMarkup] = tex;
        return tex;
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

    private static string ExtractOpeningTag(string markup, string tag)
    {
        var m = Regex.Match(markup, @"<" + tag + @"\b([^>]*)>", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static void RenderSvgTokens(string svg, CanvasRenderingContext2D ctx, Stack<SvgState> stateStack)
    {
        var tagRegex = new Regex(@"<(/?)([a-zA-Z0-9:_-]+)([^>]*?)(/?)>", RegexOptions.Singleline);
        foreach (Match m in tagRegex.Matches(svg))
        {
            bool isClosing = m.Groups[1].Value == "/";
            string tag = m.Groups[2].Value.ToLowerInvariant();
            string attrStr = m.Groups[3].Value;
            bool selfClosing = m.Groups[4].Value == "/";

            if (tag == "svg") continue;

            if (isClosing)
            {
                if (tag == "g" && stateStack.Count > 1)
                {
                    stateStack.Pop();
                    ctx.restore();
                }
                continue;
            }

            SvgState state = stateStack.Peek();
            var attrs = ParseAttributes(attrStr);
            ApplyStyleAttributes(attrs, ref state);

            if (tag == "g")
            {
                ctx.save();
                if (attrs.TryGetValue("transform", out string gXf))
                {
                    ApplySvgTransform(ctx, gXf);
                }
                if (!selfClosing)
                {
                    stateStack.Push(state);
                }
                else
                {
                    ctx.restore();
                }
                continue;
            }

            ctx.save();
            if (attrs.TryGetValue("transform", out string xf))
            {
                ApplySvgTransform(ctx, xf);
            }
            ctx.globalAlpha = state.opacity;
            ctx.beginPath();

            bool hasPath = false;
            bool isOpenTwoPointPath = false;

            switch (tag)
            {
                case "path":
                    if (attrs.TryGetValue("d", out string d) && !string.IsNullOrWhiteSpace(d))
                    {
                        DrawSvgPath(ctx, d, out isOpenTwoPointPath);
                        hasPath = true;
                    }
                    break;

                case "rect":
                {
                    double x = GetAttrDouble(attrs, "x", 0);
                    double y = GetAttrDouble(attrs, "y", 0);
                    double w = GetAttrDouble(attrs, "width", 0);
                    double h = GetAttrDouble(attrs, "height", 0);
                    double rx = GetAttrDouble(attrs, "rx", 0);
                    double ry = GetAttrDouble(attrs, "ry", rx);
                    double r = Math.Max(rx, ry);
                    if (w > 0 && h > 0)
                    {
                        if (r > 0) ctx.roundRect(x, y, w, h, r);
                        else ctx.rect(x, y, w, h);
                        hasPath = true;
                    }
                    break;
                }

                case "circle":
                {
                    double cx = GetAttrDouble(attrs, "cx", 0);
                    double cy = GetAttrDouble(attrs, "cy", 0);
                    double r = GetAttrDouble(attrs, "r", 0);
                    if (r > 0)
                    {
                        ctx.arc(cx, cy, r, 0, Math.PI * 2);
                        ctx.closePath();
                        hasPath = true;
                    }
                    break;
                }

                case "ellipse":
                {
                    double cx = GetAttrDouble(attrs, "cx", 0);
                    double cy = GetAttrDouble(attrs, "cy", 0);
                    double rx = GetAttrDouble(attrs, "rx", 0);
                    double ry = GetAttrDouble(attrs, "ry", 0);
                    if (rx > 0 && ry > 0)
                    {
                        ctx.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2);
                        ctx.closePath();
                        hasPath = true;
                    }
                    break;
                }

                case "line":
                {
                    double x1 = GetAttrDouble(attrs, "x1", 0);
                    double y1 = GetAttrDouble(attrs, "y1", 0);
                    double x2 = GetAttrDouble(attrs, "x2", 0);
                    double y2 = GetAttrDouble(attrs, "y2", 0);
                    ctx.moveTo(x1, y1);
                    ctx.lineTo(x2, y2);
                    hasPath = true;
                    isOpenTwoPointPath = true;
                    break;
                }

                case "polyline":
                case "polygon":
                {
                    if (attrs.TryGetValue("points", out string ptsStr) && !string.IsNullOrWhiteSpace(ptsStr))
                    {
                        double[] pts = ParseNumberList(ptsStr);
                        if (pts.Length >= 4)
                        {
                            ctx.moveTo(pts[0], pts[1]);
                            for (int i = 2; i + 1 < pts.Length; i += 2)
                            {
                                ctx.lineTo(pts[i], pts[i + 1]);
                            }
                            if (tag == "polygon") ctx.closePath();
                            hasPath = true;
                        }
                    }
                    break;
                }
            }

            if (hasPath)
            {
                bool shouldFill = !string.IsNullOrEmpty(state.fill) &&
                                  !string.Equals(state.fill, "none", StringComparison.OrdinalIgnoreCase) &&
                                  !(isOpenTwoPointPath && !state.fillExplicit);
                if (shouldFill)
                {
                    ctx.fillStyle = state.fill;
                    ctx.fill();
                }

                bool shouldStroke = !string.IsNullOrEmpty(state.stroke) &&
                                    !string.Equals(state.stroke, "none", StringComparison.OrdinalIgnoreCase) &&
                                    state.strokeWidth > 0;
                if (shouldStroke)
                {
                    ctx.strokeStyle = state.stroke;
                    ctx.lineWidth = state.strokeWidth;
                    ctx.lineCap = state.strokeLinecap ?? "butt";
                    ctx.lineJoin = state.strokeLinejoin ?? "miter";
                    ctx.stroke();
                }
            }

            ctx.restore();
        }
    }

    private static void ApplyStyleAttributes(Dictionary<string, string> attrs, ref SvgState state)
    {
        if (attrs.TryGetValue("fill", out string fill))
        {
            state.fill = fill.Trim();
            state.fillExplicit = true;
        }
        if (attrs.TryGetValue("stroke", out string stroke))
        {
            state.stroke = stroke.Trim();
        }
        if (attrs.TryGetValue("stroke-width", out string sw) &&
            double.TryParse(sw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double swVal))
        {
            state.strokeWidth = swVal;
        }
        if (attrs.TryGetValue("stroke-linecap", out string lc))
        {
            state.strokeLinecap = lc.Trim();
        }
        if (attrs.TryGetValue("stroke-linejoin", out string lj))
        {
            state.strokeLinejoin = lj.Trim();
        }
        if (attrs.TryGetValue("opacity", out string op) &&
            double.TryParse(op.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double opVal))
        {
            state.opacity *= Math.Max(0, Math.Min(1, opVal));
        }
    }

    private static void ApplySvgTransform(CanvasRenderingContext2D ctx, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var matches = Regex.Matches(raw, @"([a-zA-Z]+)\s*\(([^)]*)\)");
        foreach (Match m in matches)
        {
            string fn = m.Groups[1].Value.ToLowerInvariant();
            double[] args = ParseNumberList(m.Groups[2].Value);
            switch (fn)
            {
                case "translate":
                    if (args.Length >= 1)
                    {
                        double tx = args[0];
                        double ty = args.Length >= 2 ? args[1] : 0;
                        ctx.translate(tx, ty);
                    }
                    break;
                case "rotate":
                    if (args.Length >= 1)
                    {
                        double rad = args[0] * Math.PI / 180.0;
                        if (args.Length >= 3)
                        {
                            ctx.translate(args[1], args[2]);
                            ctx.rotate(rad);
                            ctx.translate(-args[1], -args[2]);
                        }
                        else
                        {
                            ctx.rotate(rad);
                        }
                    }
                    break;
                case "scale":
                    if (args.Length >= 1)
                    {
                        double sx = args[0];
                        double sy = args.Length >= 2 ? args[1] : sx;
                        ctx.scale(sx, sy);
                    }
                    break;
            }
        }
    }

    private static void DrawSvgPath(CanvasRenderingContext2D ctx, string d, out bool isOpenTwoPointPath)
    {
        isOpenTwoPointPath = false;
        var tokenRegex = new Regex(@"([MmLlHhVvCcSsQqTtAaZz])|([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)");
        var matches = tokenRegex.Matches(d);

        double curX = 0, curY = 0;
        double startX = 0, startY = 0;
        double lastCtrlX = 0, lastCtrlY = 0;
        char prevCmd = '\0';
        char cmd = '\0';
        int segmentCount = 0;
        bool closed = false;

        int idx = 0;
        int count = matches.Count;

        bool HasNextNumber()
        {
            return idx < count && string.IsNullOrEmpty(matches[idx].Groups[1].Value);
        }

        double NextNumber()
        {
            if (idx >= count) return 0;
            string s = matches[idx++].Value;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        while (idx < count)
        {
            string cmdToken = matches[idx].Groups[1].Value;
            if (!string.IsNullOrEmpty(cmdToken))
            {
                cmd = cmdToken[0];
                idx++;
            }
            else if (cmd == '\0')
            {
                break;
            }

            switch (cmd)
            {
                case 'M':
                case 'm':
                {
                    bool first = true;
                    while (HasNextNumber())
                    {
                        double x = NextNumber();
                        double y = NextNumber();
                        if (cmd == 'm') { x += curX; y += curY; }
                        curX = x;
                        curY = y;
                        if (first)
                        {
                            startX = curX;
                            startY = curY;
                            ctx.moveTo(curX, curY);
                            first = false;
                        }
                        else
                        {
                            ctx.lineTo(curX, curY);
                            segmentCount++;
                        }
                        lastCtrlX = curX;
                        lastCtrlY = curY;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'L':
                case 'l':
                {
                    while (HasNextNumber())
                    {
                        double x = NextNumber();
                        double y = NextNumber();
                        if (cmd == 'l') { x += curX; y += curY; }
                        curX = x;
                        curY = y;
                        ctx.lineTo(curX, curY);
                        segmentCount++;
                        lastCtrlX = curX;
                        lastCtrlY = curY;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'H':
                case 'h':
                {
                    while (HasNextNumber())
                    {
                        double x = NextNumber();
                        if (cmd == 'h') x += curX;
                        curX = x;
                        ctx.lineTo(curX, curY);
                        segmentCount++;
                        lastCtrlX = curX;
                        lastCtrlY = curY;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'V':
                case 'v':
                {
                    while (HasNextNumber())
                    {
                        double y = NextNumber();
                        if (cmd == 'v') y += curY;
                        curY = y;
                        ctx.lineTo(curX, curY);
                        segmentCount++;
                        lastCtrlX = curX;
                        lastCtrlY = curY;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'C':
                case 'c':
                {
                    while (HasNextNumber())
                    {
                        double x1 = NextNumber(), y1 = NextNumber();
                        double x2 = NextNumber(), y2 = NextNumber();
                        double x = NextNumber(), y = NextNumber();
                        if (cmd == 'c')
                        {
                            x1 += curX; y1 += curY;
                            x2 += curX; y2 += curY;
                            x += curX; y += curY;
                        }
                        ctx.bezierCurveTo(x1, y1, x2, y2, x, y);
                        lastCtrlX = x2;
                        lastCtrlY = y2;
                        curX = x;
                        curY = y;
                        segmentCount += 2;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'S':
                case 's':
                {
                    while (HasNextNumber())
                    {
                        double x1 = (prevCmd == 'C' || prevCmd == 'c' || prevCmd == 'S' || prevCmd == 's')
                            ? 2 * curX - lastCtrlX : curX;
                        double y1 = (prevCmd == 'C' || prevCmd == 'c' || prevCmd == 'S' || prevCmd == 's')
                            ? 2 * curY - lastCtrlY : curY;
                        double x2 = NextNumber(), y2 = NextNumber();
                        double x = NextNumber(), y = NextNumber();
                        if (cmd == 's')
                        {
                            x2 += curX; y2 += curY;
                            x += curX; y += curY;
                        }
                        ctx.bezierCurveTo(x1, y1, x2, y2, x, y);
                        lastCtrlX = x2;
                        lastCtrlY = y2;
                        curX = x;
                        curY = y;
                        segmentCount += 2;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'Q':
                case 'q':
                {
                    while (HasNextNumber())
                    {
                        double x1 = NextNumber(), y1 = NextNumber();
                        double x = NextNumber(), y = NextNumber();
                        if (cmd == 'q')
                        {
                            x1 += curX; y1 += curY;
                            x += curX; y += curY;
                        }
                        ctx.quadraticCurveTo(x1, y1, x, y);
                        lastCtrlX = x1;
                        lastCtrlY = y1;
                        curX = x;
                        curY = y;
                        segmentCount += 2;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'T':
                case 't':
                {
                    while (HasNextNumber())
                    {
                        double x1 = (prevCmd == 'Q' || prevCmd == 'q' || prevCmd == 'T' || prevCmd == 't')
                            ? 2 * curX - lastCtrlX : curX;
                        double y1 = (prevCmd == 'Q' || prevCmd == 'q' || prevCmd == 'T' || prevCmd == 't')
                            ? 2 * curY - lastCtrlY : curY;
                        double x = NextNumber(), y = NextNumber();
                        if (cmd == 't')
                        {
                            x += curX; y += curY;
                        }
                        ctx.quadraticCurveTo(x1, y1, x, y);
                        lastCtrlX = x1;
                        lastCtrlY = y1;
                        curX = x;
                        curY = y;
                        segmentCount += 2;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'A':
                case 'a':
                {
                    while (HasNextNumber())
                    {
                        double rx = Math.Abs(NextNumber());
                        double ry = Math.Abs(NextNumber());
                        double xAxisRotationDeg = NextNumber();
                        double largeArcFlag = NextNumber();
                        double sweepFlag = NextNumber();
                        double x = NextNumber();
                        double y = NextNumber();
                        if (cmd == 'a')
                        {
                            x += curX;
                            y += curY;
                        }
                        DrawSvgArc(ctx, curX, curY, rx, ry, xAxisRotationDeg, largeArcFlag != 0, sweepFlag != 0, x, y);
                        curX = x;
                        curY = y;
                        lastCtrlX = curX;
                        lastCtrlY = curY;
                        segmentCount += 2;
                        prevCmd = cmd;
                    }
                    break;
                }

                case 'Z':
                case 'z':
                {
                    ctx.closePath();
                    curX = startX;
                    curY = startY;
                    lastCtrlX = curX;
                    lastCtrlY = curY;
                    closed = true;
                    prevCmd = cmd;
                    cmd = '\0';
                    break;
                }

                default:
                    cmd = '\0';
                    break;
            }
        }

        isOpenTwoPointPath = !closed && segmentCount <= 1;
    }

    private static void DrawSvgArc(
        CanvasRenderingContext2D ctx,
        double x1, double y1,
        double rx, double ry,
        double phiDeg,
        bool largeArc,
        bool sweep,
        double x2, double y2)
    {
        if ((x1 == x2 && y1 == y2) || rx <= 0 || ry <= 0)
        {
            ctx.lineTo(x2, y2);
            return;
        }

        double phi = phiDeg * Math.PI / 180.0;
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);

        double dx2 = (x1 - x2) * 0.5;
        double dy2 = (y1 - y2) * 0.5;
        double x1p = cosPhi * dx2 + sinPhi * dy2;
        double y1p = -sinPhi * dx2 + cosPhi * dy2;

        double rxSq = rx * rx;
        double rySq = ry * ry;
        double x1pSq = x1p * x1p;
        double y1pSq = y1p * y1p;

        double lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1.0)
        {
            double s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        double sign = (largeArc == sweep) ? -1.0 : 1.0;
        double num = Math.Max(0.0, rxSq * rySq - rxSq * y1pSq - rySq * x1pSq);
        double den = rxSq * y1pSq + rySq * x1pSq;
        double coef = sign * (den > 1e-12 ? Math.Sqrt(num / den) : 0.0);

        double cxp = coef * (rx * y1p / ry);
        double cyp = coef * (-ry * x1p / rx);

        double cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) * 0.5;
        double cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) * 0.5;

        double ux = (x1p - cxp) / rx;
        double uy = (y1p - cyp) / ry;
        double vx = (-x1p - cxp) / rx;
        double vy = (-y1p - cyp) / ry;

        double theta1 = Math.Atan2(uy, ux);
        double dTheta = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        if (!sweep && dTheta > 0) dTheta -= 2.0 * Math.PI;
        else if (sweep && dTheta < 0) dTheta += 2.0 * Math.PI;

        int steps = Math.Max(8, (int)Math.Ceiling(Math.Abs(dTheta) / (Math.PI / 12.0)));
        for (int i = 1; i <= steps; i++)
        {
            double a = theta1 + dTheta * i / steps;
            double cosA = Math.Cos(a);
            double sinA = Math.Sin(a);
            double px = cosPhi * rx * cosA - sinPhi * ry * sinA + cx;
            double py = sinPhi * rx * cosA + cosPhi * ry * sinA + cy;
            ctx.lineTo(px, py);
        }
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

    private static double GetAttrDouble(Dictionary<string, string> attrs, string key, double def)
    {
        if (attrs.TryGetValue(key, out string raw) && !string.IsNullOrWhiteSpace(raw))
        {
            string s = raw.Trim();
            if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 2).Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
        }
        return def;
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
}
