using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Glyphs for the software canvas. CSS font strings ("bold italic 42px Georgia, serif") become Unity dynamic fonts made from the
/// installed system fonts; each glyph is read back from the font texture once and kept as a coverage bitmap.
/// </summary>
public static class CanvasFonts
{
    public class Face
    {
        public double size, ascent, descent;
        internal Font font;
        internal int px;
        internal FontStyle style;
        internal readonly Dictionary<char, GlyphBits> glyphs = new Dictionary<char, GlyphBits>();

        public double Advance(char c) => Glyph(c).advance;

        /// <summary>How much of the glyph covers the point (x right of the pen, y below the baseline), 0..1.</summary>
        public float Coverage(char c, double x, double y)
        {
            var g = Glyph(c);
            if (g.bits == null) return 0;
            // rows run bottom to top; the glyph's top sits bearingY above the baseline
            double gx = x - g.bearingX, gy = g.h - g.bearingY - y;
            double u = gx - 0.5, v = gy - 0.5;
            int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
            double fx = u - x0, fy = v - y0;
            float acc = 0;
            for (int k = 0; k < 4; k++)
            {
                int xx = x0 + (k & 1), yy = y0 + (k >> 1);
                if (xx < 0 || yy < 0 || xx >= g.w || yy >= g.h) continue;
                double wt = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
                acc += (float)(g.bits[yy * g.w + xx] / 255.0 * wt);
            }
            return acc;
        }

        internal GlyphBits Glyph(char c)
        {
            if (glyphs.TryGetValue(c, out var g)) return g;
            CanvasFonts.Prepare(this, c.ToString());
            return glyphs.TryGetValue(c, out g) ? g : new GlyphBits { advance = size * 0.5 };
        }

        /// <summary>Makes sure every character of the string has its bitmap (one texture read for all of them).</summary>
        public void Prepare(string text) => CanvasFonts.Prepare(this, text);
    }

    internal class GlyphBits
    {
        public byte[] bits;
        public int w, h;
        public double bearingX, bearingY, advance;
    }

    private static readonly Dictionary<string, Face> faces = new Dictionary<string, Face>();
    private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

    public static Face Get(string css)
    {
        if (faces.TryGetValue(css, out var f)) return f;
        f = Parse(css);
        faces[css] = f;
        return f;
    }

    private static readonly Dictionary<string, string[]> families = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        { "georgia", new[] { "Georgia", "Times New Roman", "DejaVu Serif", "Liberation Serif" } },
        { "serif", new[] { "Georgia", "Times New Roman", "DejaVu Serif", "Liberation Serif" } },
        { "trebuchet ms", new[] { "Trebuchet MS", "Segoe UI", "Verdana", "DejaVu Sans", "Arial" } },
        { "sans-serif", new[] { "Segoe UI", "Arial", "Helvetica", "DejaVu Sans", "Liberation Sans" } },
        { "system-ui", new[] { "Segoe UI", "Arial", "Helvetica", "DejaVu Sans" } },
        { "monospace", new[] { "Consolas", "Courier New", "DejaVu Sans Mono", "Liberation Mono" } },
    };

    private static Face Parse(string css)
    {
        bool bold = false, italic = false;
        double size = 10;
        string fams = "sans-serif";
        var parts = css.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (p.Length == 0) continue;
            if (p == "bold" || p == "bolder" || p == "600" || p == "700" || p == "800" || p == "900") { bold = true; continue; }
            if (p == "italic" || p == "oblique") { italic = true; continue; }
            if (p.EndsWith("px"))
            {
                size = double.Parse(p.Substring(0, p.Length - 2).Split('/')[0], CultureInfo.InvariantCulture);
                fams = string.Join(" ", parts, i + 1, parts.Length - i - 1);
                break;
            }
        }
        var names = new List<string>();
        foreach (var fam in fams.Split(','))
        {
            var n = fam.Trim().Trim('"', '\'');
            if (families.TryGetValue(n, out var list)) names.AddRange(list);
            else names.Add(n);
        }
        names.Add("Arial");
        var key = string.Join("|", names);
        if (!fonts.TryGetValue(key, out var font) || font == null)
        {
            font = Font.CreateDynamicFontFromOSFont(names.ToArray(), Math.Max(4, (int)Math.Round(size)));
            fonts[key] = font;
        }
        var face = new Face
        {
            size = size,
            font = font,
            px = Math.Max(4, (int)Math.Round(size)),
            style = bold && italic ? FontStyle.BoldAndItalic : bold ? FontStyle.Bold : italic ? FontStyle.Italic : FontStyle.Normal,
        };
        face.ascent = size * 0.8;
        face.descent = size * 0.2;
        // ascent from a capital letter's top, so 'middle' and 'top' baselines sit where a browser puts them
        Prepare(face, "Hg");
        if (face.glyphs.TryGetValue('H', out var hg) && hg.bits != null) face.ascent = Math.Max(hg.bearingY / 0.72, size * 0.6);
        if (face.glyphs.TryGetValue('g', out var gg) && gg.bits != null) face.descent = Math.Max(gg.h - gg.bearingY, size * 0.15);
        return face;
    }

    private static RenderTexture readRT;
    private static Texture2D readTex;

    private static void Prepare(Face f, string text)
    {
        var missing = new List<char>();
        foreach (var c in text) if (!f.glyphs.ContainsKey(c) && !missing.Contains(c)) missing.Add(c);
        if (missing.Count == 0 || f.font == null) return;
        var s = new string(missing.ToArray());
        f.font.RequestCharactersInTexture(s, f.px, f.style);
        var tex = f.font.material != null ? f.font.material.mainTexture : null;
        Color32[] pixels = null;
        int tw = 0, th = 0;
        if (tex != null)
        {
            tw = tex.width;
            th = tex.height;
            if (readRT == null || readRT.width != tw || readRT.height != th)
            {
                if (readRT != null) readRT.Release();
                readRT = new RenderTexture(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                readTex = new Texture2D(tw, th, TextureFormat.RGBA32, false, true);
            }
            var prev = RenderTexture.active;
            Graphics.Blit(tex, readRT);
            RenderTexture.active = readRT;
            readTex.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            readTex.Apply(false);
            RenderTexture.active = prev;
            pixels = readTex.GetPixels32();
        }
        foreach (var c in missing)
        {
            var g = new GlyphBits { advance = f.size * 0.5 };
            f.glyphs[c] = g;
            if (!f.font.GetCharacterInfo(c, out var info, f.px, f.style)) continue;
            g.advance = info.advance * (f.size / f.px);
            if (pixels == null) continue;
            int w = info.maxX - info.minX, h = info.maxY - info.minY;
            if (w <= 0 || h <= 0) continue;
            g.w = w;
            g.h = h;
            g.bearingX = info.minX;
            g.bearingY = info.maxY;
            g.bits = new byte[w * h];
            for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
            {
                float fu = (xx + 0.5f) / w, fv = (yy + 0.5f) / h;
                var uv = info.uvBottomLeft + (info.uvBottomRight - info.uvBottomLeft) * fu + (info.uvTopLeft - info.uvBottomLeft) * fv;
                int px = Mathf.Clamp((int)(uv.x * tw), 0, tw - 1), py = Mathf.Clamp((int)(uv.y * th), 0, th - 1);
                g.bits[yy * w + xx] = pixels[py * tw + px].a;
            }
        }
    }
}
