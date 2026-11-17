using System;

/// <summary>
/// The old pictures on the walls of the house past the nets, on one shared texture: an oil portrait of Captain Holt in his
/// bicorne hat (Resources/Pictures/captain) and his ship the Marigold under full sail at dusk (Resources/Pictures/ship). Both
/// are yellowed under old varnish, foxed with brown spots and crazed with fine cracks.
/// </summary>
public static class OldPictures
{
    /// <summary>The texture's size, and where each picture sits on it (x, y, width, height in pixels).</summary>
    public const int SIZE = 1024;
    public static readonly (int x, int y, int w, int h) CAPTAIN = (0, 440, 400, 520), SHIP = (400, 0, 624, 420);
    /// <summary>the old sea chart lying on the captain's table, drawn into the free corner of the same texture</summary>
    public static readonly (int x, int y, int w, int h) CHART = (420, 450, 576, 400);

    public static THREE.CanvasTexture make()
    {
        var c = new HTMLCanvasElement(SIZE, SIZE);
        var g = c.getContext("2d");
        var rand = Stones.rng(1712);
        captain(g, CAPTAIN, rand);
        ship(g, SHIP, rand);
        chart(g, CHART, Stones.rng(40));
        return new THREE.CanvasTexture(c) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
    }

    private static void paint(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, string resource)
    {
        var tex = UnityEngine.Resources.Load<UnityEngine.Texture2D>(resource);
        if (tex == null || !tex.isReadable) return;
        var px = tex.GetPixels32();
        int tw = tex.width, th = tex.height, cw = g.canvas.width;
        var dst = g.canvas.px;
        for (int y = 0; y < r.h; y++)
        {
            int sy = (th - 1 - y * th / r.h) * tw;
            int di = ((r.y + y) * cw + r.x) * 4;
            for (int x = 0; x < r.w; x++, di += 4)
            {
                var p = px[sy + x * tw / r.w];
                dst[di] = p.r;
                dst[di + 1] = p.g;
                dst[di + 2] = p.b;
                dst[di + 3] = 255;
            }
        }
        g.canvas.version++;
        UnityEngine.Resources.UnloadAsset(tex);
    }

    /// <summary>Old varnish, brown spots and fine cracks over a picture; dark at the edges, as old paint goes.</summary>
    private static void age(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, Func<double> rand, double vignette = 0.3, double varnish = 0.05, double spotAlpha = 0.45)
    {
        g.save();
        if (varnish > 0)
        {
            g.fillStyle = $"rgba(170,120,40,{JS.Str(varnish)})";
            g.fillRect(r.x, r.y, r.w, r.h);
        }
        var v = g.createRadialGradient(r.x + r.w / 2.0, r.y + r.h / 2.0, Math.Min(r.w, r.h) * 0.3, r.x + r.w / 2.0, r.y + r.h / 2.0, Math.Max(r.w, r.h) * 0.75);
        v.addColorStop(0, "rgba(40,25,10,0)");
        v.addColorStop(1, $"rgba(40,25,10,{JS.Str(vignette)})");
        g.fillStyle = v;
        g.fillRect(r.x, r.y, r.w, r.h);
        for (int i = 0; i < 26; i++)
        {
            double x = r.x + rand() * r.w, y = r.y + rand() * r.h, rad = 1.5 + rand() * 5;
            g.fillStyle = $"rgba(110,70,30,{JS.Str((0.15 + rand() * 0.25) * spotAlpha)})";
            g.beginPath();
            g.arc(x, y, rad, 0, Math.PI * 2);
            g.fill();
        }
        g.strokeStyle = "rgba(50,35,20,0.22)";
        g.lineWidth = 0.8;
        for (int i = 0; i < 18; i++)
        {
            double x = r.x + rand() * r.w, y = r.y + rand() * r.h;
            g.beginPath();
            g.moveTo(x, y);
            for (int k = 0; k < 4; k++)
            {
                x += (rand() - 0.5) * 40;
                y += (rand() - 0.5) * 40;
                g.lineTo(Math.Max(r.x, Math.Min(r.x + r.w, x)), Math.Max(r.y, Math.Min(r.y + r.h, y)));
            }
            g.stroke();
        }
        g.restore();
    }

    /// <summary>A small brass nameplate centered near the bottom of a painting, scaled so both plates match on the wall.</summary>
    private static void plate(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, string text)
    {
        double k = r.h / 520.0;
        double cx = r.x + r.w / 2.0;
        double pw = Math.Round(144 * k), ph = Math.Round(26 * k), bottom = Math.Round(40 * k);
        int fontPx = (int)Math.Round(15 * k);
        g.fillStyle = "#8f7432";
        g.fillRect(cx - pw / 2, r.y + r.h - bottom, pw, ph);
        g.fillStyle = "#2a1c10";
        g.font = $"bold {fontPx}px serif";
        g.textAlign = "center";
        g.fillText(text, cx, r.y + r.h - bottom + Math.Round(18 * k));
    }

    /// <summary>How far along a ray from (cx, cy) going (dx, dy) it is to the edge of a rectangle (so lines can stop at the frame).</summary>
    private static double toEdge(double cx, double cy, double dx, double dy, double x0, double y0, double x1, double y1)
    {
        double t = double.MaxValue;
        if (dx > 1e-9) t = Math.Min(t, (x1 - cx) / dx); else if (dx < -1e-9) t = Math.Min(t, (x0 - cx) / dx);
        if (dy > 1e-9) t = Math.Min(t, (y1 - cy) / dy); else if (dy < -1e-9) t = Math.Min(t, (y0 - cy) / dy);
        return t;
    }

    /// <summary>
    /// An old sea chart, and no place in particular: stained, creased and browned parchment with a double ruled border, rhumb
    /// lines running out from two compass roses, little waves, a small ship on a dotted course that wanders off to nowhere, an
    /// empty banner with a few scribbled lines in it, and foxing and wear. No coast, no names, no cross marking a spot.
    /// </summary>
    private static void chart(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, Func<double> rand)
    {
        double W = r.w, H = r.h;
        g.save();
        g.translate(r.x, r.y);
        // the parchment, a little darker toward one corner, as if it lay in the light from the window
        var bg = g.createLinearGradient(0, 0, W, H);
        bg.addColorStop(0, "#e0cc9e");
        bg.addColorStop(0.5, "#d4bb88");
        bg.addColorStop(1, "#c6a870");
        g.fillStyle = bg;
        g.fillRect(0, 0, W, H);
        // mottling: big soft tea-coloured stains, kept inside the sheet
        for (int i = 0; i < 38; i++)
        {
            double rad = 28 + rand() * 80;
            double x = rad + rand() * (W - 2 * rad), y = rad + rand() * (H - 2 * rad);
            var s = g.createRadialGradient(x, y, 0, x, y, rad);
            s.addColorStop(0, $"rgba(125,82,32,{JS.Str(0.05 + rand() * 0.11)})");
            s.addColorStop(1, "rgba(125,82,32,0)");
            g.fillStyle = s;
            g.fillRect(x - rad, y - rad, rad * 2, rad * 2);
        }
        // one old water ring, the mark of a cup set down on it
        g.strokeStyle = "rgba(120,78,30,0.20)";
        g.lineWidth = 3.5;
        g.beginPath();
        g.arc(W * 0.78, H * 0.2, 46, 0.3, Math.PI * 1.85);
        g.stroke();
        g.strokeStyle = "rgba(120,78,30,0.10)";
        g.lineWidth = 9;
        g.beginPath();
        g.arc(W * 0.78, H * 0.2, 42, 0.3, Math.PI * 1.85);
        g.stroke();

        // the ruled border: a heavy line and a light one, with ticks between them like a scale
        const double M = 24;
        double x0 = M, y0 = M, x1 = W - M, y1 = H - M;
        g.strokeStyle = "rgba(62,40,20,0.85)";
        g.lineWidth = 3;
        g.strokeRect(x0, y0, x1 - x0, y1 - y0);
        g.lineWidth = 1.2;
        g.strokeRect(x0 + 8, y0 + 8, x1 - x0 - 16, y1 - y0 - 16);
        g.lineWidth = 1;
        for (double x = x0 + 8; x < x1 - 8; x += 12)
        {
            g.beginPath(); g.moveTo(x, y0 + 3); g.lineTo(x, y0 + 8); g.moveTo(x, y1 - 3); g.lineTo(x, y1 - 8); g.stroke();
        }
        for (double y = y0 + 8; y < y1 - 8; y += 12)
        {
            g.beginPath(); g.moveTo(x0 + 3, y); g.lineTo(x0 + 8, y); g.moveTo(x1 - 3, y); g.lineTo(x1 - 8, y); g.stroke();
        }
        double ix0 = x0 + 12, iy0 = y0 + 12, ix1 = x1 - 12, iy1 = y1 - 12;

        // rhumb lines: sixteen from each of two roses, thin and faint, stopping at the inner frame
        var roses = new[] { (x: W * 0.66, y: H * 0.64), (x: W * 0.27, y: H * 0.36) };
        g.lineWidth = 0.8;
        foreach (var (rx, ry) in roses)
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8 + 0.07, dx = Math.Cos(a), dy = Math.Sin(a);
                double len = toEdge(rx, ry, dx, dy, ix0, iy0, ix1, iy1);
                g.strokeStyle = k % 4 == 0 ? "rgba(70,45,22,0.34)" : "rgba(70,45,22,0.19)";
                g.beginPath();
                g.moveTo(rx + dx * 56, ry + dy * 56);
                g.lineTo(rx + dx * len, ry + dy * len);
                g.stroke();
            }

        // small waves, scattered where the lines are thin
        g.strokeStyle = "rgba(60,40,22,0.5)";
        g.lineWidth = 1.2;
        g.lineCap = "round";
        for (int i = 0; i < 70; i++)
        {
            double x = ix0 + 14 + rand() * (ix1 - ix0 - 28), y = iy0 + 14 + rand() * (iy1 - iy0 - 28);
            bool near = false;
            foreach (var (rx, ry) in roses) if (Math.Abs(x - rx) < 64 && Math.Abs(y - ry) < 64) near = true;
            if (near) continue;
            g.beginPath();
            g.moveTo(x - 9, y);
            g.quadraticCurveTo(x - 4.5, y - 5, x, y);
            g.quadraticCurveTo(x + 4.5, y + 5, x + 9, y);
            g.stroke();
        }

        // two compass roses: rings, a long four-pointed star over a short one, and a fleur at the north point
        for (int n = 0; n < roses.Length; n++)
        {
            var (cx, cy) = roses[n];
            double R = n == 0 ? 54 : 38;
            g.fillStyle = "rgba(214,190,140,0.9)";
            g.beginPath(); g.arc(cx, cy, R * 1.06, 0, Math.PI * 2); g.fill();
            g.strokeStyle = "rgba(62,40,20,0.85)";
            g.lineWidth = 1.6;
            g.beginPath(); g.arc(cx, cy, R, 0, Math.PI * 2); g.stroke();
            g.lineWidth = 0.9;
            g.beginPath(); g.arc(cx, cy, R * 0.8, 0, Math.PI * 2); g.stroke();
            for (int p = 0; p < 8; p++)
            {
                double a = p * Math.PI / 4 - Math.PI / 2;
                double len = p % 2 == 0 ? R * 0.98 : R * 0.62;
                double wing = p % 2 == 0 ? 0.2 : 0.14;
                g.fillStyle = p % 2 == 0 ? "rgba(84,34,22,0.9)" : "rgba(70,48,26,0.8)";
                g.beginPath();
                g.moveTo(cx + Math.Cos(a) * len, cy + Math.Sin(a) * len);
                g.lineTo(cx + Math.Cos(a + wing) * R * 0.2, cy + Math.Sin(a + wing) * R * 0.2);
                g.lineTo(cx, cy);
                g.lineTo(cx + Math.Cos(a - wing) * R * 0.2, cy + Math.Sin(a - wing) * R * 0.2);
                g.closePath();
                g.fill();
            }
            // the north point: a small fleur above the ring
            g.fillStyle = "rgba(70,40,20,0.9)";
            g.beginPath(); g.arc(cx, cy - R * 1.2, 3.4, 0, Math.PI * 2); g.fill();
            g.beginPath(); g.arc(cx - 6, cy - R * 1.12, 2.6, 0, Math.PI * 2); g.fill();
            g.beginPath(); g.arc(cx + 6, cy - R * 1.12, 2.6, 0, Math.PI * 2); g.fill();
            g.fillStyle = "rgba(58,36,18,0.8)";
            g.beginPath(); g.arc(cx, cy, 3.2, 0, Math.PI * 2); g.fill();
        }

        // a small ship under sail, and her dotted course, which curls off across the sea and simply stops
        double sx = W * 0.14, sy = H * 0.7;
        g.strokeStyle = "rgba(60,36,18,0.9)";
        g.fillStyle = "rgba(60,36,18,0.85)";
        g.lineWidth = 1.5;
        g.beginPath(); g.moveTo(sx - 20, sy); g.lineTo(sx + 22, sy); g.lineTo(sx + 14, sy + 9); g.lineTo(sx - 13, sy + 9); g.closePath(); g.fill();
        g.beginPath(); g.moveTo(sx, sy); g.lineTo(sx, sy - 38); g.moveTo(sx + 11, sy); g.lineTo(sx + 11, sy - 28); g.stroke();
        g.fillStyle = "rgba(236,222,188,0.95)";
        g.beginPath(); g.moveTo(sx - 15, sy - 4); g.quadraticCurveTo(sx - 21, sy - 20, sx - 14, sy - 34); g.lineTo(sx - 1, sy - 34); g.quadraticCurveTo(sx + 4, sy - 20, sx - 1, sy - 4); g.closePath(); g.fill(); g.stroke();
        g.beginPath(); g.moveTo(sx + 12, sy - 4); g.quadraticCurveTo(sx + 8, sy - 14, sx + 12, sy - 26); g.lineTo(sx + 21, sy - 26); g.quadraticCurveTo(sx + 25, sy - 14, sx + 21, sy - 4); g.closePath(); g.fill(); g.stroke();
        g.strokeStyle = "rgba(62,40,20,0.7)";
        g.lineWidth = 1.5;
        g.setLineDash(new[] { 2.0, 5.5 });
        g.beginPath();
        g.moveTo(sx + 28, sy + 6);
        g.bezierCurveTo(W * 0.3, H * 0.86, W * 0.4, H * 0.55, W * 0.5, H * 0.74);
        g.bezierCurveTo(W * 0.56, H * 0.84, W * 0.52, H * 0.5, W * 0.44, H * 0.3);
        g.stroke();
        g.setLineDash(new double[0]);

        // an empty banner across the top, with a few lines of scribble where a title would be
        double bx = W * 0.5 - 120, by = y0 + 26;
        g.fillStyle = "rgba(222,198,148,0.95)";
        g.strokeStyle = "rgba(62,40,20,0.8)";
        g.lineWidth = 1.4;
        g.beginPath();
        g.moveTo(bx, by); g.quadraticCurveTo(bx + 120, by - 9, bx + 240, by);
        g.lineTo(bx + 232, by + 20); g.lineTo(bx + 240, by + 40);
        g.quadraticCurveTo(bx + 120, by + 31, bx, by + 40);
        g.lineTo(bx + 8, by + 20);
        g.closePath();
        g.fill(); g.stroke();
        g.strokeStyle = "rgba(55,36,20,0.55)";
        g.lineWidth = 1.1;
        for (int row = 0; row < 2; row++)
        {
            double yy = by + 14 + row * 12, xx = bx + 26 + rand() * 10;
            g.beginPath();
            g.moveTo(xx, yy);
            while (xx < bx + 205 - rand() * 30)
            {
                xx += 5 + rand() * 6;
                g.lineTo(xx, yy + (rand() - 0.5) * 5);
            }
            g.stroke();
        }
        // scribbled notes in the margin: short wavy strokes in a hand nobody could read
        for (int blockN = 0; blockN < 2; blockN++)
        {
            double nx = blockN == 0 ? W * 0.72 : W * 0.12, ny = blockN == 0 ? H * 0.3 : H * 0.18;
            for (int row = 0; row < 4; row++)
            {
                double xx = nx, yy = ny + row * 10;
                g.beginPath();
                g.moveTo(xx, yy);
                int words = 2 + (int)(rand() * 2);
                for (int wd = 0; wd < words; wd++)
                {
                    double end = xx + 18 + rand() * 26;
                    while (xx < end)
                    {
                        xx += 3 + rand() * 3;
                        g.lineTo(xx, yy + (rand() - 0.5) * 6);
                    }
                    xx += 8;
                    g.moveTo(xx, yy);
                }
                g.stroke();
            }
        }

        // folds: a crease down the middle and across, a light edge and a dark one, rubbed thin where it was opened and shut
        foreach (var vertical in new[] { true, false })
        {
            double at = vertical ? W * 0.5 + (rand() - 0.5) * 10 : H * 0.5 + (rand() - 0.5) * 10;
            for (int pass = 0; pass < 2; pass++)
            {
                g.strokeStyle = pass == 0 ? "rgba(70,45,20,0.22)" : "rgba(255,244,214,0.30)";
                g.lineWidth = pass == 0 ? 2.2 : 1.6;
                g.beginPath();
                double off = pass == 0 ? 0 : 2.2;
                if (vertical) { g.moveTo(at + off, 2); g.lineTo(at + off + (rand() - 0.5) * 6, H - 2); }
                else { g.moveTo(2, at + off); g.lineTo(W - 2, at + off + (rand() - 0.5) * 6); }
                g.stroke();
            }
        }
        g.lineCap = "butt";

        // foxing, fine cracks, and the sheet browned and burnt toward its edges
        for (int i = 0; i < 46; i++)
        {
            double x = 6 + rand() * (W - 12), y = 6 + rand() * (H - 12), rad = 1 + rand() * 3.6;
            g.fillStyle = $"rgba(112,68,28,{JS.Str(0.12 + rand() * 0.3)})";
            g.beginPath(); g.arc(x, y, rad, 0, Math.PI * 2); g.fill();
        }
        g.strokeStyle = "rgba(60,40,20,0.2)";
        g.lineWidth = 0.7;
        for (int i = 0; i < 14; i++)
        {
            double x = 6 + rand() * (W - 12), y = 6 + rand() * (H - 12);
            g.beginPath();
            g.moveTo(x, y);
            for (int k = 0; k < 4; k++)
            {
                x = Math.Max(2, Math.Min(W - 2, x + (rand() - 0.5) * 36));
                y = Math.Max(2, Math.Min(H - 2, y + (rand() - 0.5) * 36));
                g.lineTo(x, y);
            }
            g.stroke();
        }
        var edge = g.createRadialGradient(W / 2, H / 2, Math.Min(W, H) * 0.32, W / 2, H / 2, Math.Max(W, H) * 0.64);
        edge.addColorStop(0, "rgba(70,38,14,0)");
        edge.addColorStop(0.7, "rgba(70,38,14,0.20)");
        edge.addColorStop(1, "rgba(40,20,8,0.6)");
        g.fillStyle = edge;
        g.fillRect(0, 0, W, H);
        g.restore();
    }

    private static void captain(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, Func<double> rand)
    {
        paint(g, r, "Pictures/captain");
        plate(g, r, "CAPT. M. HOLT");
        age(g, r, rand);
    }

    private static void ship(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, Func<double> rand)
    {
        paint(g, r, "Pictures/ship");
        plate(g, r, "THE MARIGOLD");
        age(g, r, rand);
    }
}
