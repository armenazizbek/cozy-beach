using System;
using System.Collections.Generic;
using System.Globalization;

namespace THREE
{
    /// <summary>
    /// Three.js colour: r, g, b are stored in linear sRGB (Three's working space). Hex numbers and CSS strings are sRGB and are
    /// converted on the way in, exactly as Three.js does with colour management on.
    /// </summary>
    public class Color
    {
        public double r = 1, g = 1, b = 1;

        public Color() { }
        public Color(int hex) { setHex(hex); }
        public Color(double r, double g, double b) { setRGB(r, g, b); }
        public Color(string style) { setStyle(style); }
        public Color(Color c) { copy(c); }

        /// <summary>Lets materials be written `color = 0xffaa00` as in Three's option objects.</summary>
        public static implicit operator Color(int hex) => new Color(hex);
        public static implicit operator Color(string css) => new Color(css);

        public static double SRGBToLinear(double c) => c < 0.04045 ? c * 0.0773993808 : Math.Pow(c * 0.9478672986 + 0.0521327014, 2.4);
        public static double LinearToSRGB(double c) => c < 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 0.41666) - 0.055;

        public Color set(int hex) => setHex(hex);
        public Color set(Color c) => copy(c);
        public Color set(string s) => setStyle(s);
        public Color set(double r, double g, double b) => setRGB(r, g, b);

        public Color setScalar(double s) { r = s; g = s; b = s; return this; }

        public Color setHex(int hex)
        {
            r = SRGBToLinear(((hex >> 16) & 255) / 255.0);
            g = SRGBToLinear(((hex >> 8) & 255) / 255.0);
            b = SRGBToLinear((hex & 255) / 255.0);
            return this;
        }

        /// <summary>Linear values, as Three's setRGB does in the working colour space.</summary>
        public Color setRGB(double r, double g, double b) { this.r = r; this.g = g; this.b = b; return this; }

        /// <summary>Values given in sRGB (what `setRGB(r, g, b, SRGBColorSpace)` does).</summary>
        public Color setSRGB(double r, double g, double b) { this.r = SRGBToLinear(r); this.g = SRGBToLinear(g); this.b = SRGBToLinear(b); return this; }

        private static double hue2rgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * 6 * (2.0 / 3 - t);
            return p;
        }

        /// <summary>HSL in the working (linear) space, like Three's default.</summary>
        public Color setHSL(double h, double s, double l)
        {
            h = MathUtils.euclideanModulo(h, 1);
            s = MathUtils.clamp(s, 0, 1);
            l = MathUtils.clamp(l, 0, 1);
            if (s == 0) { r = g = b = l; }
            else
            {
                double p = l <= 0.5 ? l * (1 + s) : l + s - l * s;
                double q = 2 * l - p;
                r = hue2rgb(q, p, h + 1.0 / 3);
                g = hue2rgb(q, p, h);
                b = hue2rgb(q, p, h - 1.0 / 3);
            }
            return this;
        }

        public struct HSL { public double h, s, l; }

        public HSL getHSL()
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double hue = 0, sat, light = (min + max) / 2.0;
            if (min == max) { hue = 0; sat = 0; }
            else
            {
                double delta = max - min;
                sat = light <= 0.5 ? delta / (max + min) : delta / (2 - max - min);
                if (max == r) hue = (g - b) / delta + (g < b ? 6 : 0);
                else if (max == g) hue = (b - r) / delta + 2;
                else hue = (r - g) / delta + 4;
                hue /= 6;
            }
            return new HSL { h = hue, s = sat, l = light };
        }

        public Color offsetHSL(double h, double s, double l)
        {
            var hsl = getHSL();
            return setHSL(hsl.h + h, hsl.s + s, hsl.l + l);
        }

        public Color setStyle(string style)
        {
            style = style.Trim();
            if (style.StartsWith("#"))
            {
                var hex = style.Substring(1);
                if (hex.Length == 3)
                {
                    int rr = Convert.ToInt32(hex.Substring(0, 1), 16), gg = Convert.ToInt32(hex.Substring(1, 1), 16), bb = Convert.ToInt32(hex.Substring(2, 1), 16);
                    return setSRGB(rr / 15.0, gg / 15.0, bb / 15.0);
                }
                if (hex.Length >= 6) return setHex(Convert.ToInt32(hex.Substring(0, 6), 16));
            }
            var css = CssColor.Parse(style);
            return setSRGB(css.r, css.g, css.b);
        }

        public Color fromBufferAttribute(BufferAttribute a, int i) { r = a.getX(i); g = a.getY(i); b = a.getZ(i); return this; }
        public Color clone() => new Color().copy(this);
        public Color copy(Color c) { r = c.r; g = c.g; b = c.b; return this; }
        public Color copySRGBToLinear(Color c) { r = SRGBToLinear(c.r); g = SRGBToLinear(c.g); b = SRGBToLinear(c.b); return this; }
        public Color copyLinearToSRGB(Color c) { r = LinearToSRGB(c.r); g = LinearToSRGB(c.g); b = LinearToSRGB(c.b); return this; }
        public Color convertSRGBToLinear() => copySRGBToLinear(this);
        public Color convertLinearToSRGB() => copyLinearToSRGB(this);

        public int getHex()
        {
            int R = (int)Math.Round(MathUtils.clamp(LinearToSRGB(r) * 255, 0, 255));
            int G = (int)Math.Round(MathUtils.clamp(LinearToSRGB(g) * 255, 0, 255));
            int B = (int)Math.Round(MathUtils.clamp(LinearToSRGB(b) * 255, 0, 255));
            return (R << 16) | (G << 8) | B;
        }

        public string getHexString() => getHex().ToString("x6");
        public string getStyle() => "#" + getHexString();

        public Color add(Color c) { r += c.r; g += c.g; b += c.b; return this; }
        public Color addColors(Color a, Color c) { r = a.r + c.r; g = a.g + c.g; b = a.b + c.b; return this; }
        public Color addScalar(double s) { r += s; g += s; b += s; return this; }
        public Color sub(Color c) { r = Math.Max(0, r - c.r); g = Math.Max(0, g - c.g); b = Math.Max(0, b - c.b); return this; }
        public Color multiply(Color c) { r *= c.r; g *= c.g; b *= c.b; return this; }
        public Color multiplyScalar(double s) { r *= s; g *= s; b *= s; return this; }
        public Color lerp(Color c, double a) { r += (c.r - r) * a; g += (c.g - g) * a; b += (c.b - b) * a; return this; }
        public Color lerpColors(Color a, Color c, double t)
        {
            r = a.r + (c.r - a.r) * t;
            g = a.g + (c.g - a.g) * t;
            b = a.b + (c.b - a.b) * t;
            return this;
        }
        public Color lerpHSL(Color c, double alpha)
        {
            var a = getHSL();
            var h = c.getHSL();
            return setHSL(MathUtils.lerp(a.h, h.h, alpha), MathUtils.lerp(a.s, h.s, alpha), MathUtils.lerp(a.l, h.l, alpha));
        }
        public bool equals(Color c) => c.r == r && c.g == g && c.b == b;

        /// <summary>For shader vectors: the linear values as they are.</summary>
        public UnityEngine.Vector4 ToLinearVector(double a = 1) => new UnityEngine.Vector4((float)r, (float)g, (float)b, (float)a);

        /// <summary>A Unity colour holding these linear values (for vertex colours and lights' linear colour).</summary>
        public UnityEngine.Color ToUnityLinear(double a = 1) => new UnityEngine.Color((float)r, (float)g, (float)b, (float)a);

        /// <summary>A Unity colour in sRGB, for places that expect the gamma value (UI, Light.color which Unity linearises).</summary>
        public UnityEngine.Color ToUnitySRGB(double a = 1) =>
            new UnityEngine.Color((float)LinearToSRGB(r), (float)LinearToSRGB(g), (float)LinearToSRGB(b), (float)a);
    }

    /// <summary>Parses CSS colour strings ("rgba(255, 200, 120, 0.5)", "hsl(30, 80%, 50%)", "#ffcc00", names) into sRGB 0..1.</summary>
    public static class CssColor
    {
        public struct RGBA { public double r, g, b, a; }

        private static readonly Dictionary<string, int> names = new Dictionary<string, int>
        {
            { "white", 0xffffff }, { "black", 0x000000 }, { "red", 0xff0000 }, { "green", 0x008000 }, { "blue", 0x0000ff },
            { "yellow", 0xffff00 }, { "orange", 0xffa500 }, { "gray", 0x808080 }, { "grey", 0x808080 }, { "gold", 0xffd700 },
            { "brown", 0xa52a2a }, { "pink", 0xffc0cb }, { "purple", 0x800080 }, { "cyan", 0x00ffff }, { "magenta", 0xff00ff },
            { "silver", 0xc0c0c0 }, { "navy", 0x000080 }, { "teal", 0x008080 }, { "maroon", 0x800000 }, { "olive", 0x808000 },
            { "lime", 0x00ff00 }, { "aqua", 0x00ffff }, { "tan", 0xd2b48c }, { "beige", 0xf5f5dc }, { "ivory", 0xfffff0 },
            { "khaki", 0xf0e68c }, { "coral", 0xff7f50 }, { "salmon", 0xfa8072 }, { "crimson", 0xdc143c }, { "wheat", 0xf5deb3 },
            { "darkred", 0x8b0000 }, { "darkgreen", 0x006400 }, { "skyblue", 0x87ceeb }, { "sienna", 0xa0522d }, { "peru", 0xcd853f },
            { "chocolate", 0xd2691e }, { "saddlebrown", 0x8b4513 }, { "goldenrod", 0xdaa520 }, { "darkgoldenrod", 0xb8860b },
            { "lightgray", 0xd3d3d3 }, { "lightgrey", 0xd3d3d3 }, { "darkgray", 0xa9a9a9 }, { "darkgrey", 0xa9a9a9 },
            { "dimgray", 0x696969 }, { "whitesmoke", 0xf5f5f5 }, { "linen", 0xfaf0e6 }, { "cornsilk", 0xfff8dc }, { "seashell", 0xfff5ee },
        };

        private static double Num(string s, double scale)
        {
            s = s.Trim();
            if (s.EndsWith("%")) return double.Parse(s.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
            return double.Parse(s, CultureInfo.InvariantCulture) / scale;
        }

        public static RGBA Parse(string style)
        {
            style = style.Trim().ToLowerInvariant();
            if (style == "transparent") return new RGBA { r = 0, g = 0, b = 0, a = 0 };
            if (style.StartsWith("#"))
            {
                var hex = style.Substring(1);
                if (hex.Length == 3 || hex.Length == 4)
                {
                    double R = Convert.ToInt32(hex.Substring(0, 1), 16) / 15.0, G = Convert.ToInt32(hex.Substring(1, 1), 16) / 15.0;
                    double B = Convert.ToInt32(hex.Substring(2, 1), 16) / 15.0, A = hex.Length == 4 ? Convert.ToInt32(hex.Substring(3, 1), 16) / 15.0 : 1;
                    return new RGBA { r = R, g = G, b = B, a = A };
                }
                int v = Convert.ToInt32(hex.Substring(0, 6), 16);
                double alpha = hex.Length == 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) / 255.0 : 1;
                return new RGBA { r = ((v >> 16) & 255) / 255.0, g = ((v >> 8) & 255) / 255.0, b = (v & 255) / 255.0, a = alpha };
            }
            int open = style.IndexOf('(');
            if (open > 0 && style.EndsWith(")"))
            {
                string fn = style.Substring(0, open).Trim();
                string body = style.Substring(open + 1, style.Length - open - 2).Replace("/", ",");
                var parts = body.Contains(",") ? body.Split(',') : body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                double a = parts.Length > 3 ? Num(parts[3], 1) : 1;
                if (fn == "rgb" || fn == "rgba")
                    return new RGBA { r = Num(parts[0], 255), g = Num(parts[1], 255), b = Num(parts[2], 255), a = a };
                if (fn == "hsl" || fn == "hsla")
                {
                    double h = double.Parse(parts[0].Trim().Replace("deg", ""), CultureInfo.InvariantCulture) / 360.0;
                    double s = Num(parts[1], 100), l = Num(parts[2], 100);
                    h = MathUtils.euclideanModulo(h, 1);
                    double R, G, B;
                    if (s == 0) R = G = B = l;
                    else
                    {
                        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                        R = Hue(p, q, h + 1.0 / 3); G = Hue(p, q, h); B = Hue(p, q, h - 1.0 / 3);
                    }
                    return new RGBA { r = R, g = G, b = B, a = a };
                }
            }
            if (names.TryGetValue(style, out var hx))
                return new RGBA { r = ((hx >> 16) & 255) / 255.0, g = ((hx >> 8) & 255) / 255.0, b = (hx & 255) / 255.0, a = 1 };
            return new RGBA { r = 0, g = 0, b = 0, a = 1 };
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
