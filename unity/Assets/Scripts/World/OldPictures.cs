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

    public static THREE.CanvasTexture make()
    {
        var c = new HTMLCanvasElement(SIZE, SIZE);
        var g = c.getContext("2d");
        var rand = Stones.rng(1712);
        captain(g, CAPTAIN, rand);
        ship(g, SHIP, rand);
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
