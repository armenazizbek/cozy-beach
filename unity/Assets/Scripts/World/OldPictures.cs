using System;

/// <summary>
/// The old pictures on the walls of the house past the nets, painted in code onto one texture: a portrait of Captain Holt in
/// his bicorne hat, his ship the Marigold under full sail at dusk (both from the opening). Both are yellowed under old varnish, foxed with brown spots and crazed with fine cracks.
/// </summary>
public static class OldPictures
{
    /// <summary>The texture's size, and where each picture sits on it (x, y, width, height in pixels).</summary>
    public const int SIZE = 1024;
    public static readonly (int x, int y, int w, int h) CAPTAIN = (0, 0, 400, 520), SHIP = (400, 0, 624, 420);

    public static THREE.CanvasTexture make()
    {
        var c = new HTMLCanvasElement(SIZE, SIZE);
        var g = c.getContext("2d");
        var rand = Stones.rng(1712);
        captain(g, CAPTAIN, rand);
        ship(g, SHIP, rand);
        return new THREE.CanvasTexture(c) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
    }

    /// <summary>Old varnish, brown spots and fine cracks over a picture; dark at the edges, as old paint goes.</summary>
    private static void age(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, Func<double> rand, double vignette = 0.55)
    {
        g.save();
        g.fillStyle = "rgba(170,120,40,0.20)";
        g.fillRect(r.x, r.y, r.w, r.h);
        var v = g.createRadialGradient(r.x + r.w / 2.0, r.y + r.h / 2.0, Math.Min(r.w, r.h) * 0.3, r.x + r.w / 2.0, r.y + r.h / 2.0, Math.Max(r.w, r.h) * 0.75);
        v.addColorStop(0, "rgba(40,25,10,0)");
        v.addColorStop(1, $"rgba(40,25,10,{JS.Str(vignette)})");
        g.fillStyle = v;
        g.fillRect(r.x, r.y, r.w, r.h);
        for (int i = 0; i < 26; i++)
        {
            double x = r.x + rand() * r.w, y = r.y + rand() * r.h, rad = 1.5 + rand() * 5;
            g.fillStyle = $"rgba(110,70,30,{JS.Str(0.15 + rand() * 0.25)})";
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

    private static void captain(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, Func<double> rand)
    {
        double cx = r.x + r.w / 2.0, top = r.y;
        // a dark, warm ground behind him
        var bg = g.createRadialGradient(cx, top + 200, 20, cx, top + 260, 330);
        bg.addColorStop(0, "#6b5a3a");
        bg.addColorStop(1, "#2a2216");
        g.fillStyle = bg;
        g.fillRect(r.x, r.y, r.w, r.h);
        // his coat: broad navy shoulders with brass buttons and gold epaulettes
        g.fillStyle = "#1f2a44";
        g.beginPath();
        g.moveTo(r.x + 20, r.y + r.h);
        g.bezierCurveTo(r.x + 30, top + 360, cx - 120, top + 320, cx - 70, top + 318);
        g.lineTo(cx + 70, top + 318);
        g.bezierCurveTo(cx + 120, top + 320, r.x + r.w - 30, top + 360, r.x + r.w - 20, r.y + r.h);
        g.closePath();
        g.fill();
        // the white of his shirt and the lapels
        g.fillStyle = "#d9cfb6";
        g.beginPath();
        g.moveTo(cx - 34, top + 322);
        g.lineTo(cx + 34, top + 322);
        g.lineTo(cx, top + 430);
        g.closePath();
        g.fill();
        g.fillStyle = "#2d3a5c";
        g.beginPath();
        g.moveTo(cx - 34, top + 322); g.lineTo(cx - 6, top + 432); g.lineTo(cx - 60, top + 520); g.lineTo(cx - 75, top + 340);
        g.closePath();
        g.fill();
        g.beginPath();
        g.moveTo(cx + 34, top + 322); g.lineTo(cx + 6, top + 432); g.lineTo(cx + 60, top + 520); g.lineTo(cx + 75, top + 340);
        g.closePath();
        g.fill();
        g.fillStyle = "#c9a43c";
        foreach (var by in new[] { 450.0, 480, 510 })
        {
            g.beginPath(); g.arc(cx - 44, top + by, 5, 0, Math.PI * 2); g.fill();
            g.beginPath(); g.arc(cx + 44, top + by, 5, 0, Math.PI * 2); g.fill();
        }
        foreach (var s in new[] { -1, 1 })
        {
            g.fillStyle = "#c9a43c";
            g.beginPath();
            g.ellipse(cx + s * 108, top + 344, 46, 16, s * 0.25, 0, Math.PI * 2);
            g.fill();
            g.strokeStyle = "#e6c35c";
            g.lineWidth = 2;
            for (int k = -3; k <= 3; k++)
            {
                g.beginPath();
                g.moveTo(cx + s * (108 + k * 11), top + 352);
                g.lineTo(cx + s * (110 + k * 12), top + 372);
                g.stroke();
            }
        }
        // his neck, face and ears
        g.fillStyle = "#c08a64";
        g.fillRect(cx - 24, top + 270, 48, 56);
        g.fillStyle = "#d29b72";
        g.beginPath();
        g.ellipse(cx, top + 225, 62, 78, 0, 0, Math.PI * 2);
        g.fill();
        g.beginPath(); g.ellipse(cx - 62, top + 228, 11, 18, 0, 0, Math.PI * 2); g.fill();
        g.beginPath(); g.ellipse(cx + 62, top + 228, 11, 18, 0, 0, Math.PI * 2); g.fill();
        // shadow down one side of the face, as the light falls from the left
        g.fillStyle = "rgba(90,50,30,0.28)";
        g.beginPath();
        g.ellipse(cx + 26, top + 230, 34, 74, 0, -Math.PI / 2, Math.PI / 2);
        g.fill();
        // a grey beard and moustache
        g.fillStyle = "#9a948a";
        g.beginPath();
        g.moveTo(cx - 58, top + 238);
        g.bezierCurveTo(cx - 60, top + 300, cx - 30, top + 330, cx, top + 336);
        g.bezierCurveTo(cx + 30, top + 330, cx + 60, top + 300, cx + 58, top + 238);
        g.bezierCurveTo(cx + 40, top + 262, cx - 40, top + 262, cx - 58, top + 238);
        g.fill();
        g.fillStyle = "#837d72";
        g.beginPath();
        g.ellipse(cx - 18, top + 258, 22, 8, 0.2, 0, Math.PI * 2);
        g.ellipse(cx + 18, top + 258, 22, 8, -0.2, 0, Math.PI * 2);
        g.fill();
        g.fillStyle = "#6e3b2c";
        g.fillRect(cx - 14, top + 272, 28, 5);
        // eyes, brows and nose
        g.fillStyle = "#f0e6d0";
        g.beginPath(); g.ellipse(cx - 22, top + 210, 9, 5, 0, 0, Math.PI * 2); g.fill();
        g.beginPath(); g.ellipse(cx + 22, top + 210, 9, 5, 0, 0, Math.PI * 2); g.fill();
        g.fillStyle = "#2c3a48";
        g.beginPath(); g.arc(cx - 21, top + 210, 4, 0, Math.PI * 2); g.fill();
        g.beginPath(); g.arc(cx + 23, top + 210, 4, 0, Math.PI * 2); g.fill();
        g.strokeStyle = "#5b5248";
        g.lineWidth = 5;
        g.beginPath(); g.moveTo(cx - 36, top + 196); g.lineTo(cx - 10, top + 192); g.stroke();
        g.beginPath(); g.moveTo(cx + 10, top + 192); g.lineTo(cx + 36, top + 196); g.stroke();
        g.strokeStyle = "rgba(110,60,40,0.7)";
        g.lineWidth = 3;
        g.beginPath(); g.moveTo(cx, top + 212); g.lineTo(cx - 6, top + 242); g.lineTo(cx + 6, top + 246); g.stroke();
        // the bicorne hat, black with a gold edge and a cockade
        g.fillStyle = "#15151a";
        g.beginPath();
        g.moveTo(cx - 130, top + 170);
        g.bezierCurveTo(cx - 90, top + 60, cx + 90, top + 60, cx + 130, top + 170);
        g.bezierCurveTo(cx + 60, top + 150, cx - 60, top + 150, cx - 130, top + 170);
        g.fill();
        g.strokeStyle = "#c9a43c";
        g.lineWidth = 4;
        g.beginPath();
        g.moveTo(cx - 130, top + 170);
        g.bezierCurveTo(cx - 60, top + 150, cx + 60, top + 150, cx + 130, top + 170);
        g.stroke();
        g.fillStyle = "#c9a43c";
        g.beginPath(); g.arc(cx + 34, top + 120, 13, 0, Math.PI * 2); g.fill();
        g.fillStyle = "#7a1f1f";
        g.beginPath(); g.arc(cx + 34, top + 120, 7, 0, Math.PI * 2); g.fill();
        // a small brass plate under him
        g.fillStyle = "#8f7432";
        g.fillRect(cx - 70, r.y + r.h - 40, 140, 26);
        g.fillStyle = "#2a1c10";
        g.font = "bold 15px serif";
        g.textAlign = "center";
        g.fillText("CAPT. M. HOLT", cx, r.y + r.h - 22);
        age(g, r, rand, 0.7);
    }

    private static void ship(CanvasRenderingContext2D g, (int x, int y, int w, int h) r, Func<double> rand)
    {
        double horizon = r.y + r.h * 0.62;
        // a dusk sky, amber low and slate high
        var sky = g.createLinearGradient(0, r.y, 0, horizon);
        sky.addColorStop(0, "#5a6470");
        sky.addColorStop(0.6, "#b08a5a");
        sky.addColorStop(1, "#e0b070");
        g.fillStyle = sky;
        g.fillRect(r.x, r.y, r.w, horizon - r.y);
        // the sun low on the water and a few long clouds
        g.fillStyle = "rgba(250,220,150,0.8)";
        g.beginPath(); g.arc(r.x + r.w * 0.78, horizon - 18, 28, 0, Math.PI * 2); g.fill();
        g.fillStyle = "rgba(90,80,80,0.35)";
        for (int i = 0; i < 5; i++)
        {
            g.beginPath();
            g.ellipse(r.x + rand() * r.w, r.y + 30 + rand() * 120, 70 + rand() * 80, 9 + rand() * 6, 0, 0, Math.PI * 2);
            g.fill();
        }
        // the sea, darker toward the viewer, with long swells
        var sea = g.createLinearGradient(0, horizon, 0, r.y + r.h);
        sea.addColorStop(0, "#3e5a5c");
        sea.addColorStop(1, "#1d2c30");
        g.fillStyle = sea;
        g.fillRect(r.x, horizon, r.w, r.y + r.h - horizon);
        g.strokeStyle = "rgba(220,200,150,0.35)";
        g.lineWidth = 2;
        for (int i = 0; i < 26; i++)
        {
            double y = horizon + 8 + rand() * (r.y + r.h - horizon - 10), x = r.x + rand() * r.w, w = 20 + rand() * 50;
            g.beginPath();
            g.moveTo(x, y);
            g.quadraticCurveTo(x + w / 2, y - 4, x + w, y);
            g.stroke();
        }
        // the ship: a dark hull with a gold stripe, three masts, full sails, heeling a little
        double sx = r.x + r.w * 0.42, sy = horizon + 30;
        g.save();
        g.translate(sx, sy);
        g.rotate(-0.04);
        g.fillStyle = "#2b1d14";
        g.beginPath();
        g.moveTo(-170, -30); g.lineTo(150, -30); g.lineTo(190, -55); g.lineTo(170, -30);
        g.quadraticCurveTo(150, 22, 110, 26); g.lineTo(-140, 26); g.quadraticCurveTo(-170, 10, -175, -45);
        g.closePath();
        g.fill();
        g.fillStyle = "#b8923c";
        g.fillRect(-160, -18, 300, 5);
        g.fillStyle = "#e8d6a8";
        for (int i = 0; i < 9; i++) g.fillRect(-140 + i * 30, -10, 9, 7);
        g.strokeStyle = "#1b120c";
        g.lineWidth = 4;
        foreach (var (mx, mh) in new[] { (-95.0, 210.0), (0, 250), (90, 200) })
        {
            g.beginPath(); g.moveTo(mx, -30); g.lineTo(mx, -30 - mh); g.stroke();
        }
        g.lineWidth = 1;
        g.beginPath(); g.moveTo(190, -55); g.lineTo(0, -280); g.lineTo(-175, -45); g.stroke();
        foreach (var (mx, mh) in new[] { (-95.0, 210.0), (0, 250), (90, 200) })
        {
            for (int k = 0; k < 3; k++)
            {
                double y0 = -40 - k * mh / 3.2, y1 = y0 - mh / 3.6, half = 58 - k * 12;
                g.fillStyle = k == 0 ? "#e9dcc0" : "#f2e7cf";
                g.beginPath();
                g.moveTo(mx - half, y1);
                g.lineTo(mx + half, y1);
                g.quadraticCurveTo(mx + half + 14, (y0 + y1) / 2, mx + half - 4, y0);
                g.lineTo(mx - half + 4, y0);
                g.quadraticCurveTo(mx - half + 14, (y0 + y1) / 2, mx - half, y1);
                g.fill();
            }
        }
        g.fillStyle = "#e9dcc0";
        g.beginPath(); g.moveTo(186, -58); g.lineTo(100, -200); g.lineTo(98, -60); g.closePath(); g.fill();
        g.fillStyle = "#9b2a20";
        g.beginPath(); g.moveTo(0, -280); g.lineTo(34, -272); g.lineTo(0, -262); g.closePath(); g.fill();
        g.restore();
        // a wash of white at her bow
        g.fillStyle = "rgba(230,225,210,0.6)";
        g.beginPath(); g.ellipse(sx + 175, sy + 22, 40, 7, 0, 0, Math.PI * 2); g.fill();
        g.fillStyle = "#2a1c10";
        g.font = "italic 18px serif";
        g.textAlign = "left";
        g.fillText("The Marigold", r.x + 18, r.y + r.h - 16);
        age(g, r, rand, 0.55);
    }
}
