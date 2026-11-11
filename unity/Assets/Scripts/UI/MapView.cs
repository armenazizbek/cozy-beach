using System;
using System.Collections.Generic;
using static Beach;

// Port of src/ui/MapView.ts

internal static class Canvas2DClipExt
{
    public static void clip(this CanvasRenderingContext2D ctx) { }
}

public class Waypoint
{
    public double x;
    public double z;

    public static implicit operator XZ(Waypoint w) => w == null ? null : new XZ { x = w.x, z = w.z };
    public static implicit operator Waypoint(XZ w) => w == null ? null : new Waypoint { x = w.x, z = w.z };
}

public class MapPlayer
{
    public double x;
    public double z;
    /// <summary>three.js rotation.y: forward is (-sin, -cos)</summary>
    public double yaw;
}

public class MapViewBounds
{
    public double cx;
    public double cz;
    public double half;

    public void Deconstruct(out double cx, out double cz, out double half)
    {
        cx = this.cx;
        cz = this.cz;
        half = this.half;
    }
}

/// <summary>
/// Draws the island from above. The terrain is painted once into a canvas; the round mini-map
/// (which turns with the player) and the full map (north up) both reuse it.
/// </summary>
public class MapView
{
    /// <summary>The terrain is painted over the island and plenty of open sea around it, so the boat never leaves the map.</summary>
    private const double EXT_X0 = -320;
    private const double EXT_Z0 = -150;
    private const double EXT = 640;
    private const double PPM = 2; // painted pixels per metre
    private const int BASE_PX = (int)(EXT * PPM);

    /// <summary>The part of the world the full map shows: a square centred a little out to sea.</summary>
    public static readonly MapViewBounds MAP_VIEW = new MapViewBounds { cx = 0, cz = 162, half = 300 };

    /// <summary>
    /// The markers on the full map are drawn in metres. The map once showed 344 m across and now shows more, so they are
    /// drawn this much larger to stay the size they were on the screen.
    /// </summary>
    private static readonly double MARK = MAP_VIEW.half / 172;

    private HTMLCanvasElement _base = new HTMLCanvasElement(BASE_PX, BASE_PX);
    /// <summary>planted marker flags, set by the game and edited in place</summary>
    public List<double[]> flags = new List<double[]>();
    /// <summary>where the dog is while it is out on a trip</summary>
    public Waypoint dog = null;
    /// <summary>where the treasure map says to dig; set only while the player holds the map</summary>
    public List<double[]> treasures = new List<double[]>();
    /// <summary>where the captain's strongbox is buried, once the bottle map has been found</summary>
    public List<double[]> captain = new List<double[]>();

    public MapView(IList<Scenery.Trunk> trunks)
    {
        this._base.width = this._base.height = BASE_PX;
        var g = this._base.getContext("2d");
        var img = g.createImageData(BASE_PX, BASE_PX);
        var data = img.data;
        var m = EXT / BASE_PX;
        for (int v = 0; v < BASE_PX; v++)
        {
            var z = EXT_Z0 + (v + 0.5) * m;
            var deepSeaRow = z > 88;
            if (deepSeaRow) foreach (var isl in ISLANDS) if (Math.Abs(z - isl.z) <= isl.R * 1.2) deepSeaRow = false;
            if (deepSeaRow)
            {
                var rowBase = v * BASE_PX * 4;
                for (int u = 0; u < BASE_PX; u++)
                {
                    var i = rowBase + u * 4;
                    data[i] = 30; data[i + 1] = 95; data[i + 2] = 155; data[i + 3] = 255;
                }
                continue;
            }
            for (int u = 0; u < BASE_PX; u++)
            {
                var x = EXT_X0 + (u + 0.5) * m;
                var isle = z > 88 ? islandNear(x, z) : null;
                if (z > 88 && isle == null)
                {
                    var idx = (v * BASE_PX + u) * 4;
                    data[idx] = 30; data[idx + 1] = 95; data[idx + 2] = 155; data[idx + 3] = 255;
                    continue;
                }
                var h = baseHeight(x, z);
                double r;
                double gr;
                double b;
                if (h < WATER_LEVEL)
                {
                    var d = Math.Min(1, (WATER_LEVEL - h) / 0.9);
                    r = 70 - d * 40; gr = 150 - d * 55; b = 200 - d * 45;
                }
                else if (h < WATER_LEVEL + 0.12)
                {
                    r = 236; gr = 240; b = 235; // foam line
                }
                else
                {
                    var wet = Math.Min(1, Math.Max(0, (z - (WET_Z - 3)) / 6));
                    var hill = Math.Min(1, Math.Max(0, (h - 1.6) / 1.4));
                    r = 232 - wet * 50 - hill * 70; gr = 206 - wet * 55 - hill * 36; b = 170 - wet * 40 - hill * 62;
                    if (isle != null && isle.kind == 1)
                    {
                        // the cay: pale sand, a little green along the crest
                        var crest = Math.Min(1, Math.Max(0, (h - 1.4) / 0.8));
                        r = 238 - crest * 80; gr = 226 - crest * 38; b = 192 - crest * 92;
                    }
                    else if (isle != null && isle.kind == 2)
                    {
                        // the ember rock: black sand, and the cone darker still with a rusty top
                        var cone = Math.Min(1, Math.Max(0, (h - 1.2) / 6.5));
                        r = 104 - cone * 34 + Math.Max(0, cone - 0.6) * 150; gr = 98 - cone * 46; b = 92 - cone * 48;
                    }
                }
                var i = (v * BASE_PX + u) * 4;
                data[i] = (byte)r; data[i + 1] = (byte)gr; data[i + 2] = (byte)b; data[i + 3] = 255;
            }
        }
        g.putImageData(img, 0, 0);
        // palms
        g.fillStyle = "rgba(52,120,52,0.9)";
        g.beginPath();
        foreach (var t in trunks)
        {
            var px = (t.x - EXT_X0) * PPM;
            var pz = (t.z - EXT_Z0) * PPM;
            g.moveTo(px + 2.4, pz);
            g.arc(px, pz, 2.4, 0, Math.PI * 2);
        }
        g.fill();
    }

    private void drawHoles(CanvasRenderingContext2D ctx, IList<double[]> holes, double radius, double? cx = null, double? cz = null, double? maxR = null)
    {
        if (holes.Count == 0) return;
        ctx.fillStyle = "rgba(95,60,34,0.85)";
        ctx.beginPath();
        if (cx.HasValue && cz.HasValue && maxR.HasValue)
        {
            double cxv = cx.Value, czv = cz.Value, mrv = maxR.Value;
            foreach (var h in holes)
            {
                double x = h[0], z = h[1];
                if (Math.Abs(x - cxv) > mrv || Math.Abs(z - czv) > mrv) continue;
                ctx.moveTo(x + radius, z);
                ctx.arc(x, z, radius, 0, Math.PI * 2);
            }
        }
        else
        {
            foreach (var h in holes)
            {
                double x = h[0], z = h[1];
                ctx.moveTo(x + radius, z);
                ctx.arc(x, z, radius, 0, Math.PI * 2);
            }
        }
        ctx.fill();
    }

    /// <summary>A map pin: a red teardrop with a white centre. Drawn in world units, tip at (x, z).</summary>
    private void drawPaw(CanvasRenderingContext2D ctx, double x, double z, double s)
    {
        ctx.save();
        ctx.translate(x, z);
        ctx.fillStyle = "#7a4a24";
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = s * 0.22;
        void blob(double bx, double bz, double r)
        {
            ctx.beginPath();
            ctx.arc(bx, bz, r, 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
        }
        blob(0, s * 0.35, s * 0.95);
        blob(-s * 1.05, -s * 0.5, s * 0.5);
        blob(-s * 0.4, -s * 1.25, s * 0.5);
        blob(s * 0.4, -s * 1.25, s * 0.5);
        blob(s * 1.05, -s * 0.5, s * 0.5);
        ctx.restore();
    }

    /// <summary>A red cross, like the ones on an old treasure map.</summary>
    private void drawCross(CanvasRenderingContext2D ctx, double x, double z, double s, double pulse = 1)
    {
        ctx.save();
        ctx.translate(x, z);
        ctx.lineCap = "round";
        var passes = new (double w, string col)[] { (s * 0.9, "rgba(255,248,230,0.95)"), (s * 0.5, "#b3201a") };
        foreach (var (w, col) in passes)
        {
            ctx.lineWidth = w * pulse;
            ctx.strokeStyle = col;
            ctx.beginPath();
            ctx.moveTo(-s, -s);
            ctx.lineTo(s, s);
            ctx.moveTo(s, -s);
            ctx.lineTo(-s, s);
            ctx.stroke();
        }
        ctx.restore();
    }

    /// <summary>The treasure map: the island on old paper, with a red cross where each chest is buried.</summary>
    public void drawTreasure(CanvasRenderingContext2D ctx, double size, MapPlayer p, double time, Waypoint dest = null, Waypoint cart = null, Waypoint boat = null, IList<double[]> marks = null, bool bottle = false)
    {
        if (marks == null) marks = this.treasures;
        var (cx, cz, half) = MAP_VIEW;
        var sc = size / (2 * half);
        ctx.clearRect(0, 0, size, size);
        ctx.save();
        ctx.scale(sc, sc);
        ctx.translate(half - cx, half - cz);
        // the land drawn in browns, like ink on paper
        ctx.filter = bottle ? "sepia(0.55) hue-rotate(75deg) saturate(0.6) contrast(0.85) brightness(1.1)" : "sepia(1) saturate(0.7) contrast(0.8) brightness(1.08)";
        ctx.drawImage(this._base, EXT_X0, EXT_Z0, EXT, EXT);
        ctx.filter = "none";
        this.drawPier(ctx);
        this.drawCamp(ctx, 0, CAMP_Z, 1.8 * MARK);
        var pulse = 1 + Math.Sin(time * 4) * 0.12;
        if (cart != null) this.drawCart(ctx, cart.x, cart.z, 1.9 * MARK);
        if (boat != null) this.drawBoat(ctx, boat.x, boat.z, 2.0 * MARK);
        foreach (var mk in marks) this.drawCross(ctx, mk[0], mk[1], 3.4 * MARK, pulse);
        this.drawFlags(ctx, 3.2 * MARK);
        if (this.dog != null) this.drawPaw(ctx, this.dog.x, this.dog.z, 3 * MARK);
        // the destination the player has marked: the same dashed line and pin as on the island map
        if (dest != null)
        {
            ctx.setLineDash(new double[] { 2.2 * MARK, 1.8 * MARK });
            ctx.strokeStyle = "rgba(230,57,70,0.95)";
            ctx.lineWidth = 0.7 * MARK;
            ctx.beginPath();
            ctx.moveTo(p.x, p.z);
            ctx.lineTo(dest.x, dest.z);
            ctx.stroke();
            ctx.setLineDash(Array.Empty<double>());
            this.drawPin(ctx, dest.x, dest.z, 1.7 * MARK);
        }
        ctx.restore();
        // aged paper over everything
        ctx.save();
        ctx.globalCompositeOperation = "multiply";
        var g = ctx.createRadialGradient(size / 2, size / 2, size * 0.25, size / 2, size / 2, size * 0.75);
        g.addColorStop(0, bottle ? "rgba(206,226,208,0.78)" : "rgba(236,214,164,0.75)");
        g.addColorStop(1, bottle ? "rgba(84,112,96,0.92)" : "rgba(150,104,52,0.9)");
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, size, size);
        ctx.restore();
        // a dashed border and a compass rose
        ctx.strokeStyle = bottle ? "rgba(30,70,52,0.75)" : "rgba(90,50,20,0.7)";
        ctx.lineWidth = 3;
        ctx.setLineDash(new double[] { 10, 6 });
        ctx.strokeRect(10, 10, size - 20, size - 20);
        ctx.setLineDash(Array.Empty<double>());
        ctx.save();
        ctx.translate(size - 52, 56);
        ctx.fillStyle = "rgba(90,50,20,0.85)";
        for (int i = 0; i < 4; i++)
        {
            ctx.rotate(Math.PI / 2);
            ctx.beginPath();
            ctx.moveTo(0, -22);
            ctx.lineTo(5, -5);
            ctx.lineTo(-5, -5);
            ctx.closePath();
            ctx.fill();
        }
        ctx.font = "700 13px Georgia, serif";
        ctx.textAlign = "center";
        ctx.fillText("N", 0, -28);
        ctx.restore();
        ctx.font = "italic 600 14px Georgia, serif";
        ctx.fillStyle = "rgba(70,36,12,0.9)";
        ctx.textAlign = "center";
        ctx.fillText("Camp", ((0 - cx + half) / (2 * half)) * size, ((CAMP_Z - cz + half) / (2 * half)) * size + 30);
        this.nameIslands(ctx, size);
        if (bottle)
        {
            ctx.textAlign = "left";
            ctx.fillText("Found in a bottle", 24, size - 24);
        }
        ctx.save();
        ctx.translate(((p.x - cx + half) / (2 * half)) * size, ((p.z - cz + half) / (2 * half)) * size);
        ctx.rotate(Math.Atan2(-Math.Cos(p.yaw), -Math.Sin(p.yaw)) + Math.PI / 2);
        this.arrow(ctx, size / 70);
        ctx.restore();
    }

    /// <summary>Each island's name, just above it on a full map. The text colour and font are the caller's.</summary>
    private void nameIslands(CanvasRenderingContext2D ctx, double size)
    {
        var (cx, cz, half) = MAP_VIEW;
        foreach (var isl in ISLANDS)
        {
            double px = size / (2 * half);
            ctx.fillText(isl.name, (isl.x - cx + half) * px, (isl.z - cz + half) * px - isl.R * px - 10);
        }
    }

    private void drawFlags(CanvasRenderingContext2D ctx, double s)
    {
        foreach (var f in this.flags)
        {
            double x = f[0], z = f[1];
            ctx.save();
            ctx.translate(x, z);
            ctx.lineWidth = s * 0.28;
            ctx.strokeStyle = "#ffffff";
            ctx.fillStyle = "#e63946";
            ctx.beginPath();
            ctx.moveTo(0, 0);
            ctx.lineTo(0, -s * 2.4);
            ctx.stroke();
            ctx.beginPath();
            ctx.moveTo(0, -s * 2.4);
            ctx.lineTo(s * 1.8, -s * 1.8);
            ctx.lineTo(0, -s * 1.2);
            ctx.closePath();
            ctx.fill();
            ctx.stroke();
            ctx.restore();
        }
    }

    private void drawPin(CanvasRenderingContext2D ctx, double x, double z, double scale)
    {
        ctx.save();
        ctx.translate(x, z);
        ctx.scale(scale, scale);
        ctx.fillStyle = "#e63946";
        ctx.strokeStyle = "#fff";
        ctx.lineWidth = 0.45;
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.bezierCurveTo(-2.6, -2.6, -2.2, -5.8, 0, -5.8);
        ctx.bezierCurveTo(2.2, -5.8, 2.6, -2.6, 0, 0);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = "#fff";
        ctx.beginPath();
        ctx.arc(0, -3.6, 1, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }

    /// <summary>The boat: a little white hull with a blue stripe.</summary>
    private void drawBoat(CanvasRenderingContext2D ctx, double x, double z, double scale)
    {
        ctx.save();
        ctx.translate(x, z);
        ctx.scale(scale, scale);
        ctx.fillStyle = "#ffffff";
        ctx.strokeStyle = "#1f5fa8";
        ctx.lineWidth = 0.5;
        ctx.beginPath();
        ctx.moveTo(0, -2.6);
        ctx.quadraticCurveTo(1.6, -0.8, 1.2, 2.2);
        ctx.lineTo(-1.2, 2.2);
        ctx.quadraticCurveTo(-1.6, -0.8, 0, -2.6);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.restore();
    }

    /// <summary>The golf cart: a small teal car-shape.</summary>
    private void drawCart(CanvasRenderingContext2D ctx, double x, double z, double scale)
    {
        ctx.save();
        ctx.translate(x, z);
        ctx.scale(scale, scale);
        ctx.fillStyle = "#1f8f86";
        ctx.strokeStyle = "#fff";
        ctx.lineWidth = 0.4;
        ctx.beginPath();
        ctx.roundRect(-1.4, -2, 2.8, 4, 0.8);
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = "#fff";
        ctx.fillRect(-0.9, -1.2, 1.8, 1.1);
        ctx.restore();
    }

    /// <summary>The fishing pier, a brown strip out from the beach. The map's units are the world's.</summary>
    private void drawPier(CanvasRenderingContext2D ctx)
    {
        ctx.fillStyle = "#8a6238";
        ctx.fillRect(Pier.X - 1.6, Pier.Z0, 3.2, Pier.Z1 - Pier.Z0);
    }

    /// <summary>
    /// The camp: a round cream badge with an orange tent in it. A badge and not a bare triangle, which looked just like the
    /// red arrow that is the player. `turn` undoes the mini-map's turning, so the tent always stands upright.
    /// </summary>
    private void drawCamp(CanvasRenderingContext2D ctx, double x, double z, double scale, double turn = 0)
    {
        ctx.save();
        ctx.translate(x, z);
        if (turn != 0) ctx.rotate(turn);
        ctx.scale(scale, scale);
        // the badge, with a dark rim so it reads on the sand and on the sea
        ctx.fillStyle = "#fbf2de";
        ctx.strokeStyle = "#2a1c14";
        ctx.lineWidth = 0.5;
        ctx.beginPath();
        ctx.arc(0, 0, 2.6, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
        // the tent
        ctx.fillStyle = "#d9703f";
        ctx.beginPath();
        ctx.moveTo(0, -1.6);
        ctx.lineTo(1.75, 1.25);
        ctx.lineTo(-1.75, 1.25);
        ctx.closePath();
        ctx.fill();
        // its open door
        ctx.fillStyle = "#2a1c14";
        ctx.beginPath();
        ctx.moveTo(0, -0.15);
        ctx.lineTo(0.6, 1.25);
        ctx.lineTo(-0.6, 1.25);
        ctx.closePath();
        ctx.fill();
        ctx.restore();
    }

    private void arrow(CanvasRenderingContext2D ctx, double px)
    {
        ctx.save();
        ctx.fillStyle = "rgba(255,255,255,0.18)";
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.arc(0, 0, px * 4.2, -Math.PI / 2 - 0.5, -Math.PI / 2 + 0.5);
        ctx.closePath();
        ctx.fill();
        ctx.fillStyle = "#ff4a3a";
        ctx.strokeStyle = "#fff";
        ctx.lineWidth = px * 0.35;
        ctx.beginPath();
        ctx.moveTo(0, -px * 1.5);
        ctx.lineTo(px, px);
        ctx.lineTo(0, px * 0.45);
        ctx.lineTo(-px, px);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.restore();
    }

    private string lastMiniKey = "";
    private MiniMapGpu miniGpu;

    /// <summary>Round mini-map that turns so that "forward" is always up.</summary>
    public void drawMini(CanvasRenderingContext2D ctx, double size, MapPlayer p, IList<double[]> holes, Waypoint dest = null, Waypoint cart = null, Waypoint boat = null)
    {
        var key = $"{JS.Str(size)}|{JS.ToInt32(p.x * 40)},{JS.ToInt32(p.z * 40)},{JS.ToInt32(p.yaw * 400)}|{holes.Count}|{this.flags.Count}|{(dest != null ? $"{JS.ToInt32(dest.x * 10)},{JS.ToInt32(dest.z * 10)}" : "")}|{(cart != null ? $"{JS.ToInt32(cart.x * 20)},{JS.ToInt32(cart.z * 20)}" : "")}|{(boat != null ? $"{JS.ToInt32(boat.x * 20)},{JS.ToInt32(boat.z * 20)}" : "")}|{(this.dog != null ? $"{JS.ToInt32(this.dog.x * 20)},{JS.ToInt32(this.dog.z * 20)}" : "")}";
        if (key == this.lastMiniKey) return;
        this.lastMiniKey = key;

        var r = size / 2;
        var sc = size / 60; // 60 metres across
        var fx = -Math.Sin(p.yaw);
        var fz = -Math.Cos(p.yaw);
        // The sea, the island and the dig holes are drawn by the GPU underneath (MiniMapGpu: one turned texture and a vector
        // layer); this canvas only carries the markers, so it is cleared to transparent and the circle is cut by the layer.
        if (this.miniGpu == null || this.miniGpu.Host != ctx.canvas.ve)
            this.miniGpu = new MiniMapGpu(ctx.canvas, size, this._base, EXT_X0, EXT_Z0, EXT);
        this.miniGpu.SetHoles(holes);
        this.miniGpu.Place(p.x, p.z, -Math.PI / 2 - Math.Atan2(fz, fx), r);
        ctx.clearRect(0, 0, size, size);
        ctx.save();

        ctx.save();
        ctx.translate(r, r);
        ctx.rotate(-Math.PI / 2 - Math.Atan2(fz, fx));
        ctx.scale(sc, sc);
        ctx.translate(-p.x, -p.z);
        this.drawPier(ctx);
        // the camera of this little map turns with the player; the tent is turned back so it stands upright
        this.drawCamp(ctx, 0, CAMP_Z, 1.15, Math.PI / 2 + Math.Atan2(fz, fx));
        if (cart != null) this.drawCart(ctx, cart.x, cart.z, 1.2);
        if (boat != null) this.drawBoat(ctx, boat.x, boat.z, 1.3);
        this.drawFlags(ctx, 2.2);
        if (this.dog != null) this.drawPaw(ctx, this.dog.x, this.dog.z, 1.7);
        if (dest != null)
        {
            ctx.setLineDash(new double[] { 1.4, 1.1 });
            ctx.strokeStyle = "rgba(230,57,70,0.95)";
            ctx.lineWidth = 0.55;
            ctx.beginPath();
            ctx.moveTo(p.x, p.z);
            ctx.lineTo(dest.x, dest.z);
            ctx.stroke();
            ctx.setLineDash(Array.Empty<double>());
            this.drawPin(ctx, dest.x, dest.z, 0.9);
        }
        ctx.restore();

        // keep the camp visible even when it is off the edge of the little map
        var dx = 0 - p.x;
        var dz = CAMP_Z - p.z;
        var ang = -Math.PI / 2 - Math.Atan2(fz, fx);
        var rx = dx * Math.Cos(ang) - dz * Math.Sin(ang);
        var rz = dx * Math.Sin(ang) + dz * Math.Cos(ang);
        var dist = JS.Hypot(rx, rz) * sc;
        if (dist > r - 12)
        {
            var k = (r - 12) / dist;
            // pinned to the rim at the same size it has on the map, in screen pixels
            this.drawCamp(ctx, r + rx * sc * k, r + rz * sc * k, 1.15 * sc);
        }

        if (this.dog != null)
        {
            var ex = this.dog.x - p.x;
            var ez = this.dog.z - p.z;
            var qx = ex * Math.Cos(ang) - ez * Math.Sin(ang);
            var qz = ex * Math.Sin(ang) + ez * Math.Cos(ang);
            var dd = JS.Hypot(qx, qz) * sc;
            if (dd > r - 12)
            {
                var k = (r - 12) / dd;
                this.drawPaw(ctx, r + qx * sc * k, r + qz * sc * k, 4);
            }
        }

        if (dest != null)
        {
            var ddx = dest.x - p.x;
            var ddz = dest.z - p.z;
            var rx2 = ddx * Math.Cos(ang) - ddz * Math.Sin(ang);
            var rz2 = ddx * Math.Sin(ang) + ddz * Math.Cos(ang);
            var d2 = JS.Hypot(rx2, rz2) * sc;
            if (d2 > r - 12)
            {
                var k = (r - 12) / d2;
                this.drawPin(ctx, r + rx2 * sc * k, r + rz2 * sc * k + 7, 1.7);
            }
        }

        ctx.translate(r, r);
        this.arrow(ctx, size / 30);
        ctx.restore();
        // the round cut and the rim are MiniMapGpu's clipping layer and border
    }

    /// <summary>The whole island, north (inland) at the top.</summary>
    public void drawBig(CanvasRenderingContext2D ctx, double size, MapPlayer p, IList<double[]> holes, Waypoint dest = null, Waypoint cart = null, Waypoint boat = null)
    {
        var (cx, cz, half) = MAP_VIEW;
        var sc = size / (2 * half);
        ctx.clearRect(0, 0, size, size);
        ctx.save();
        ctx.scale(sc, sc);
        ctx.translate(half - cx, half - cz);
        ctx.drawImage(this._base, EXT_X0, EXT_Z0, EXT, EXT);
        this.drawHoles(ctx, holes, 0.8);
        this.drawPier(ctx);
        this.drawCamp(ctx, 0, CAMP_Z, 1.8 * MARK);
        if (cart != null) this.drawCart(ctx, cart.x, cart.z, 1.9 * MARK);
        if (boat != null) this.drawBoat(ctx, boat.x, boat.z, 2.0 * MARK);
        this.drawFlags(ctx, 3.2 * MARK);
        if (this.dog != null) this.drawPaw(ctx, this.dog.x, this.dog.z, 3 * MARK);
        if (dest != null)
        {
            ctx.setLineDash(new double[] { 2.2 * MARK, 1.8 * MARK });
            ctx.strokeStyle = "rgba(230,57,70,0.95)";
            ctx.lineWidth = 0.7 * MARK;
            ctx.beginPath();
            ctx.moveTo(p.x, p.z);
            ctx.lineTo(dest.x, dest.z);
            ctx.stroke();
            ctx.setLineDash(Array.Empty<double>());
            this.drawPin(ctx, dest.x, dest.z, 1.7 * MARK);
        }
        ctx.restore();

        ctx.font = "600 14px \"Trebuchet MS\", sans-serif";
        ctx.fillStyle = "#3b2a1e";
        ctx.textAlign = "center";
        ctx.fillText("Camp", ((0 - cx + half) / (2 * half)) * size, ((CAMP_Z - cz + half) / (2 * half)) * size + 30);
        ctx.fillStyle = "#ffffff";
        ctx.fillText("Sea", size / 2, size - 14);
        this.nameIslands(ctx, size);

        ctx.save();
        ctx.translate(((p.x - cx + half) / (2 * half)) * size, ((p.z - cz + half) / (2 * half)) * size);
        ctx.rotate(Math.Atan2(-Math.Cos(p.yaw), -Math.Sin(p.yaw)) + Math.PI / 2);
        this.arrow(ctx, size / 70);
        ctx.restore();
    }
}
