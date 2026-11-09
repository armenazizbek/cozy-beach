using System;
using System.Collections.Generic;

/// <summary>Pixels as getImageData returns them: RGBA bytes, not premultiplied, first row at the top.</summary>
public class ImageData
{
    public int width, height;
    public byte[] data;
    public THREE.PixelFormat format = THREE.PixelFormat.RGBAFormat;

    public ImageData(int w, int h)
    {
        width = w;
        height = h;
        data = new byte[w * h * 4];
    }

    public ImageData(int w, int h, byte[] d, THREE.PixelFormat f = THREE.PixelFormat.RGBAFormat)
    {
        width = w;
        height = h;
        data = d;
        format = f;
    }
}

public class TextMetrics
{
    public double width;
    public double actualBoundingBoxAscent, actualBoundingBoxDescent;
}

public class CanvasGradient
{
    internal bool radial;
    internal double x0, y0, r0, x1, y1, r1;
    internal readonly List<(double t, CssColor_RGBA c)> stops = new List<(double, CssColor_RGBA)>();

    public void addColorStop(double offset, string color)
    {
        var c = THREE.CssColor.Parse(color);
        var s = new CssColor_RGBA { r = c.r, g = c.g, b = c.b, a = c.a };
        int i = stops.Count;
        while (i > 0 && stops[i - 1].t > offset) i--;
        stops.Insert(i, (offset, s));
    }

    internal CssColor_RGBA At(double t)
    {
        if (stops.Count == 0) return new CssColor_RGBA();
        if (t <= stops[0].t) return stops[0].c;
        if (t >= stops[stops.Count - 1].t) return stops[stops.Count - 1].c;
        for (int i = 1; i < stops.Count; i++)
        {
            if (t <= stops[i].t)
            {
                var a = stops[i - 1];
                var b = stops[i];
                double k = b.t == a.t ? 1 : (t - a.t) / (b.t - a.t);
                // canvas interpolates in premultiplied space
                double aa = a.c.a + (b.c.a - a.c.a) * k;
                if (aa <= 0) return new CssColor_RGBA();
                double pr = a.c.r * a.c.a + (b.c.r * b.c.a - a.c.r * a.c.a) * k;
                double pg = a.c.g * a.c.a + (b.c.g * b.c.a - a.c.g * a.c.a) * k;
                double pb = a.c.b * a.c.a + (b.c.b * b.c.a - a.c.b * a.c.a) * k;
                return new CssColor_RGBA { r = pr / aa, g = pg / aa, b = pb / aa, a = aa };
            }
        }
        return stops[stops.Count - 1].c;
    }
}

public struct CssColor_RGBA
{
    public double r, g, b, a;
}

/// <summary>document.createElement('canvas') for drawing textures in code. Pixels live on the CPU; a CanvasTexture uploads them.</summary>
public class HTMLCanvasElement : HTMLElement
{
    private int _w, _h;
    /// <summary>Premultiplied RGBA, sRGB values, first row at the top (as a browser keeps it).</summary>
    internal byte[] px;
    private CanvasRenderingContext2D ctx;
    public int version;
    private UnityEngine.Texture2D _uiTex;
    private int _uiUploadedVersion = -1;
    private byte[] _uiBuf;
    private UnityEngine.UIElements.VisualElement _texTarget;

    /// <summary>
    /// Shows the canvas pixels on this element instead of the canvas's own background (the mini-map puts them on a layer
    /// above the map that the GPU draws). Null puts them back.
    /// </summary>
    internal UnityEngine.UIElements.VisualElement textureTarget
    {
        get => _texTarget;
        set
        {
            if (ve != null) ve.style.backgroundImage = UnityEngine.UIElements.StyleKeyword.Null;
            _texTarget = value;
            if (_uiTex != null) (_texTarget ?? ve).style.backgroundImage = new UnityEngine.UIElements.StyleBackground(_uiTex);
        }
    }

    public HTMLCanvasElement(int width = 300, int height = 150) : base("CANVAS")
    {
        _w = Math.Max(1, width);
        _h = Math.Max(1, height);
        px = new byte[_w * _h * 4];
    }

    public int width
    {
        get => _w;
        set
        {
            _w = Math.Max(1, value);
            px = new byte[_w * _h * 4];
            ctx?.Reset();
            version++;
            if (ve != null && id != "scene" && string.IsNullOrEmpty(style?.width))
                ve.style.width = _w;
        }
    }

    public int height
    {
        get => _h;
        set
        {
            _h = Math.Max(1, value);
            px = new byte[_w * _h * 4];
            ctx?.Reset();
            version++;
            if (ve != null && id != "scene" && string.IsNullOrEmpty(style?.height))
                ve.style.height = _h;
        }
    }

    public CanvasRenderingContext2D getContext(string kind = "2d", object opts = null) => ctx ??= new CanvasRenderingContext2D(this);

    /// <summary>Re-uploads the software canvas into a UI Toolkit background texture when attached to the UI.</summary>
    internal void SyncUITexture()
    {
        if (ve == null || id == "scene" || ctx == null || _uiUploadedVersion == version) return;
        _uiUploadedVersion = version;
        if (_uiTex == null || _uiTex.width != _w || _uiTex.height != _h)
        {
            if (_uiTex != null) UnityEngine.Object.Destroy(_uiTex);
            _uiTex = new UnityEngine.Texture2D(_w, _h, UnityEngine.TextureFormat.RGBA32, false)
            {
                name = string.IsNullOrEmpty(id) ? (string.IsNullOrEmpty(className) ? "UICanvas" : className) : id,
                wrapMode = UnityEngine.TextureWrapMode.Clamp,
                filterMode = UnityEngine.FilterMode.Bilinear,
            };
            (_texTarget ?? ve).style.backgroundImage = new UnityEngine.UIElements.StyleBackground(_uiTex);
        }
        int len = _w * _h * 4;
        if (_uiBuf == null || _uiBuf.Length != len) _uiBuf = new byte[len];
        for (int y = 0; y < _h; y++)
        {
            int sy = _h - 1 - y;
            int si = sy * _w * 4, di = y * _w * 4;
            for (int x = 0; x < _w; x++, si += 4, di += 4)
            {
                int a = px[si + 3];
                if (a == 0) { _uiBuf[di] = 0; _uiBuf[di + 1] = 0; _uiBuf[di + 2] = 0; _uiBuf[di + 3] = 0; continue; }
                if (a == 255) { _uiBuf[di] = px[si]; _uiBuf[di + 1] = px[si + 1]; _uiBuf[di + 2] = px[si + 2]; _uiBuf[di + 3] = 255; continue; }
                _uiBuf[di] = (byte)Math.Min(255, px[si] * 255 / a);
                _uiBuf[di + 1] = (byte)Math.Min(255, px[si + 1] * 255 / a);
                _uiBuf[di + 2] = (byte)Math.Min(255, px[si + 2] * 255 / a);
                _uiBuf[di + 3] = (byte)a;
            }
        }
        _uiTex.SetPixelData(_uiBuf, 0);
        _uiTex.Apply(false, false);
    }

    /// <summary>Straight (not premultiplied) RGBA32 with the first row at the bottom, ready for Texture2D.SetPixelData.</summary>
    internal byte[] ToUnityRGBA(bool flipY)
    {
        var o = new byte[_w * _h * 4];
        for (int y = 0; y < _h; y++)
        {
            int sy = flipY ? _h - 1 - y : y;
            int si = sy * _w * 4, di = y * _w * 4;
            for (int x = 0; x < _w; x++, si += 4, di += 4)
            {
                int a = px[si + 3];
                if (a == 0) { o[di] = 0; o[di + 1] = 0; o[di + 2] = 0; o[di + 3] = 0; continue; }
                if (a == 255) { o[di] = px[si]; o[di + 1] = px[si + 1]; o[di + 2] = px[si + 2]; o[di + 3] = 255; continue; }
                o[di] = (byte)Math.Min(255, px[si] * 255 / a);
                o[di + 1] = (byte)Math.Min(255, px[si + 1] * 255 / a);
                o[di + 2] = (byte)Math.Min(255, px[si + 2] * 255 / a);
                o[di + 3] = (byte)a;
            }
        }
        return o;
    }

    /// <summary>A PNG data URL is not needed in Unity; this hands back the canvas itself for UI images to show.</summary>
    public HTMLCanvasElement toDataURL(string type = "image/png") => this;
}

/// <summary>
/// A software CanvasRenderingContext2D: paths, arcs, curves, gradients, dashes, transforms, compositing, text and drawImage, close
/// enough to the browser for the game's procedural textures and maps.
/// </summary>
public class CanvasRenderingContext2D
{
    public readonly HTMLCanvasElement canvas;

    private struct Xf
    {
        public double a, b, c, d, e, f;
        public static Xf Identity => new Xf { a = 1, d = 1 };
        public (double, double) Apply(double x, double y) => (a * x + c * y + e, b * x + d * y + f);
        public Xf Mul(Xf m) => new Xf
        {
            a = a * m.a + c * m.b, b = b * m.a + d * m.b,
            c = a * m.c + c * m.d, d = b * m.c + d * m.d,
            e = a * m.e + c * m.f + e, f = b * m.e + d * m.f + f,
        };
        public Xf Inverse()
        {
            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12) return Identity;
            double id = 1 / det;
            return new Xf { a = d * id, b = -b * id, c = -c * id, d = a * id, e = (c * f - d * e) * id, f = (b * e - a * f) * id };
        }
        public double Scale => Math.Sqrt(Math.Abs(a * d - b * c));
    }

    private class State
    {
        public Xf xf = Xf.Identity;
        public object fillStyle = "#000000", strokeStyle = "#000000";
        public double lineWidth = 1, globalAlpha = 1, miterLimit = 10, lineDashOffset;
        public string lineCap = "butt", lineJoin = "miter", globalCompositeOperation = "source-over";
        public string font = "10px sans-serif", textAlign = "start", textBaseline = "alphabetic", filter = "none";
        public double[] lineDash = new double[0];
        public double shadowBlur, shadowOffsetX, shadowOffsetY;
        public string shadowColor = "rgba(0, 0, 0, 0)";
        public bool imageSmoothingEnabled = true;
        public State Clone()
        {
            var s = (State)MemberwiseClone();
            s.lineDash = (double[])lineDash.Clone();
            return s;
        }
    }

    private State st = new State();
    private readonly Stack<State> stack = new Stack<State>();
    // the current path, in device pixels
    private readonly List<List<(double x, double y)>> subs = new List<List<(double, double)>>();
    private readonly List<bool> closedSubs = new List<bool>();
    private List<(double x, double y)> cur;
    private double lastX, lastY;

    internal CanvasRenderingContext2D(HTMLCanvasElement c) { canvas = c; }

    internal void Reset()
    {
        st = new State();
        stack.Clear();
        beginPath();
    }

    // ------------------------------------------------------------------ state

    public object fillStyle { get => st.fillStyle; set => st.fillStyle = value; }
    public object strokeStyle { get => st.strokeStyle; set => st.strokeStyle = value; }
    public double lineWidth { get => st.lineWidth; set { if (value > 0) st.lineWidth = value; } }
    public double globalAlpha { get => st.globalAlpha; set { if (value >= 0 && value <= 1) st.globalAlpha = value; } }
    public string lineCap { get => st.lineCap; set => st.lineCap = value; }
    public string lineJoin { get => st.lineJoin; set => st.lineJoin = value; }
    public double miterLimit { get => st.miterLimit; set => st.miterLimit = value; }
    public string globalCompositeOperation { get => st.globalCompositeOperation; set => st.globalCompositeOperation = value; }
    public string font { get => st.font; set => st.font = value; }
    public string textAlign { get => st.textAlign; set => st.textAlign = value; }
    public string textBaseline { get => st.textBaseline; set => st.textBaseline = value; }
    public string filter { get => st.filter; set => st.filter = value; }
    public double shadowBlur { get => st.shadowBlur; set => st.shadowBlur = value; }
    public string shadowColor { get => st.shadowColor; set => st.shadowColor = value; }
    public double shadowOffsetX { get => st.shadowOffsetX; set => st.shadowOffsetX = value; }
    public double shadowOffsetY { get => st.shadowOffsetY; set => st.shadowOffsetY = value; }
    public double lineDashOffset { get => st.lineDashOffset; set => st.lineDashOffset = value; }
    public bool imageSmoothingEnabled { get => st.imageSmoothingEnabled; set => st.imageSmoothingEnabled = value; }
    public string imageSmoothingQuality { get; set; } = "low";

    public void save() => stack.Push(st.Clone());
    public void restore() { if (stack.Count > 0) st = stack.Pop(); }

    public void setLineDash(double[] segs)
    {
        var list = new List<double>(segs);
        if (list.Count % 2 == 1) list.AddRange(segs);
        st.lineDash = list.ToArray();
    }
    public double[] getLineDash() => (double[])st.lineDash.Clone();

    public void translate(double x, double y) => st.xf = st.xf.Mul(new Xf { a = 1, d = 1, e = x, f = y });
    public void scale(double x, double y) => st.xf = st.xf.Mul(new Xf { a = x, d = y });
    public void rotate(double ang)
    {
        double c = Math.Cos(ang), s = Math.Sin(ang);
        st.xf = st.xf.Mul(new Xf { a = c, b = s, c = -s, d = c });
    }
    public void transform(double a, double b, double c, double d, double e, double f) => st.xf = st.xf.Mul(new Xf { a = a, b = b, c = c, d = d, e = e, f = f });
    public void setTransform(double a, double b, double c, double d, double e, double f) => st.xf = new Xf { a = a, b = b, c = c, d = d, e = e, f = f };
    public void resetTransform() => st.xf = Xf.Identity;

    public CanvasGradient createLinearGradient(double x0, double y0, double x1, double y1) =>
        new CanvasGradient { x0 = x0, y0 = y0, x1 = x1, y1 = y1 };

    public CanvasGradient createRadialGradient(double x0, double y0, double r0, double x1, double y1, double r1) =>
        new CanvasGradient { radial = true, x0 = x0, y0 = y0, r0 = r0, x1 = x1, y1 = y1, r1 = r1 };

    // ------------------------------------------------------------------ paths

    public void beginPath()
    {
        subs.Clear();
        closedSubs.Clear();
        cur = null;
    }

    private void EnsureSub(double x, double y)
    {
        if (cur == null) moveTo(x, y);
    }

    private void AddDev(double x, double y)
    {
        var (dx, dy) = st.xf.Apply(x, y);
        cur.Add((dx, dy));
        lastX = x;
        lastY = y;
    }

    public void moveTo(double x, double y)
    {
        cur = new List<(double, double)>();
        subs.Add(cur);
        closedSubs.Add(false);
        AddDev(x, y);
    }

    public void lineTo(double x, double y)
    {
        if (cur == null) { moveTo(x, y); return; }
        AddDev(x, y);
    }

    public void closePath()
    {
        if (cur == null || cur.Count == 0) return;
        closedSubs[closedSubs.Count - 1] = true;
        var first = cur[0];
        // a new subpath starts at the same point
        var inv = st.xf.Inverse();
        var (ux, uy) = inv.Apply(first.x, first.y);
        cur = new List<(double, double)> { first };
        subs.Add(cur);
        closedSubs.Add(false);
        lastX = ux;
        lastY = uy;
    }

    private int Segs(double len) => Math.Max(4, Math.Min(256, (int)Math.Ceiling(len * st.xf.Scale / 2.5)));

    public void quadraticCurveTo(double cpx, double cpy, double x, double y)
    {
        EnsureSub(cpx, cpy);
        double x0 = lastX, y0 = lastY;
        int n = Segs(JS.Hypot(cpx - x0, cpy - y0) + JS.Hypot(x - cpx, y - cpy));
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n, mt = 1 - t;
            AddDev(mt * mt * x0 + 2 * mt * t * cpx + t * t * x, mt * mt * y0 + 2 * mt * t * cpy + t * t * y);
        }
    }

    public void bezierCurveTo(double c1x, double c1y, double c2x, double c2y, double x, double y)
    {
        EnsureSub(c1x, c1y);
        double x0 = lastX, y0 = lastY;
        int n = Segs(JS.Hypot(c1x - x0, c1y - y0) + JS.Hypot(c2x - c1x, c2y - c1y) + JS.Hypot(x - c2x, y - c2y));
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n, mt = 1 - t;
            double a = mt * mt * mt, b = 3 * mt * mt * t, c = 3 * mt * t * t, d = t * t * t;
            AddDev(a * x0 + b * c1x + c * c2x + d * x, a * y0 + b * c1y + c * c2y + d * y);
        }
    }

    public void arc(double x, double y, double r, double a0, double a1, bool ccw = false) => ellipse(x, y, r, r, 0, a0, a1, ccw);

    /// <summary>A rounded corner at (x1, y1) between the current point and (x2, y2), as the canvas spec draws it.</summary>
    public void arcTo(double x1, double y1, double x2, double y2, double r)
    {
        EnsureSub(x1, y1);
        double x0 = lastX, y0 = lastY;
        double dx0 = x0 - x1, dy0 = y0 - y1, dx2 = x2 - x1, dy2 = y2 - y1;
        double l0 = JS.Hypot(dx0, dy0), l2 = JS.Hypot(dx2, dy2);
        if (r <= 0 || l0 < 1e-9 || l2 < 1e-9 || Math.Abs(dx0 * dy2 - dy0 * dx2) < 1e-9) { lineTo(x1, y1); return; }
        double ux0 = dx0 / l0, uy0 = dy0 / l0, ux2 = dx2 / l2, uy2 = dy2 / l2;
        double half = Math.Acos(Math.Max(-1, Math.Min(1, ux0 * ux2 + uy0 * uy2))) / 2;
        double along = r / Math.Tan(half);
        double tx0 = x1 + ux0 * along, ty0 = y1 + uy0 * along;
        double tx2 = x1 + ux2 * along, ty2 = y1 + uy2 * along;
        double bx = ux0 + ux2, by = uy0 + uy2, bl = JS.Hypot(bx, by);
        double cd = r / Math.Sin(half);
        double cx = x1 + bx / bl * cd, cy = y1 + by / bl * cd;
        double a0 = Math.Atan2(ty0 - cy, tx0 - cx), a1 = Math.Atan2(ty2 - cy, tx2 - cx);
        double d = Math.Atan2(Math.Sin(a1 - a0), Math.Cos(a1 - a0));
        lineTo(tx0, ty0);
        ellipse(cx, cy, r, r, 0, a0, a0 + d, d < 0);
    }

    public void ellipse(double x, double y, double rx, double ry, double rot, double a0, double a1, bool ccw = false)
    {
        double sweep = a1 - a0;
        const double TAU = Math.PI * 2;
        if (!ccw)
        {
            if (sweep >= TAU) sweep = TAU;
            else { sweep %= TAU; if (sweep < 0) sweep += TAU; }
        }
        else
        {
            if (-sweep >= TAU) sweep = -TAU;
            else { sweep %= TAU; if (sweep > 0) sweep -= TAU; }
        }
        if (a1 - a0 == 0) sweep = 0;
        int n = Math.Max(2, Math.Min(360, (int)Math.Ceiling(Math.Abs(sweep) * Math.Max(rx, ry) * st.xf.Scale / 2.0)));
        n = Math.Max(n, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 32)));
        double cr = Math.Cos(rot), sr = Math.Sin(rot);
        for (int i = 0; i <= n; i++)
        {
            double t = a0 + sweep * i / n;
            double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
            double px = x + ex * cr - ey * sr, py = y + ex * sr + ey * cr;
            if (i == 0 && cur == null) moveTo(px, py);
            else AddDev(px, py);
        }
    }

    public void rect(double x, double y, double w, double h)
    {
        moveTo(x, y);
        lineTo(x + w, y);
        lineTo(x + w, y + h);
        lineTo(x, y + h);
        closePath();
    }

    public void roundRect(double x, double y, double w, double h, double r)
    {
        r = Math.Max(0, Math.Min(r, Math.Min(Math.Abs(w), Math.Abs(h)) / 2));
        moveTo(x + r, y);
        lineTo(x + w - r, y);
        ellipse(x + w - r, y + r, r, r, 0, -Math.PI / 2, 0);
        lineTo(x + w, y + h - r);
        ellipse(x + w - r, y + h - r, r, r, 0, 0, Math.PI / 2);
        lineTo(x + r, y + h);
        ellipse(x + r, y + h - r, r, r, 0, Math.PI / 2, Math.PI);
        lineTo(x, y + r);
        ellipse(x + r, y + r, r, r, 0, Math.PI, Math.PI * 1.5);
        closePath();
    }

    public void roundRect(double x, double y, double w, double h, double[] radii) => roundRect(x, y, w, h, radii.Length > 0 ? radii[0] : 0);

    // ------------------------------------------------------------------ painting

    public void fill(string rule = "nonzero") => FillPolys(CollectPolys(true), st.fillStyle, rule == "evenodd");

    public void stroke() => FillPolys(StrokePolys(), st.strokeStyle, false);

    public void fillRect(double x, double y, double w, double h)
    {
        var saved = SavePath();
        beginPath();
        rect(x, y, w, h);
        fill();
        RestorePath(saved);
    }

    public void strokeRect(double x, double y, double w, double h)
    {
        var saved = SavePath();
        beginPath();
        rect(x, y, w, h);
        stroke();
        RestorePath(saved);
    }

    public void clearRect(double x, double y, double w, double h)
    {
        // clearing ignores compositing and alpha
        var saved = SavePath();
        var savedOp = st.globalCompositeOperation;
        var savedAlpha = st.globalAlpha;
        var savedShadow = st.shadowBlur;
        st.globalCompositeOperation = "copy-clear";
        st.globalAlpha = 1;
        st.shadowBlur = 0;
        beginPath();
        rect(x, y, w, h);
        FillPolys(CollectPolys(true), "#000", false);
        st.globalCompositeOperation = savedOp;
        st.globalAlpha = savedAlpha;
        st.shadowBlur = savedShadow;
        RestorePath(saved);
    }

    private (List<List<(double, double)>>, List<bool>, List<(double, double)>) SavePath() =>
        (new List<List<(double, double)>>(subs), new List<bool>(closedSubs), cur);

    private void RestorePath((List<List<(double, double)>> s, List<bool> c, List<(double, double)> cu) p)
    {
        subs.Clear();
        subs.AddRange(p.s);
        closedSubs.Clear();
        closedSubs.AddRange(p.c);
        cur = p.cu;
    }

    private List<List<(double x, double y)>> CollectPolys(bool forFill)
    {
        var res = new List<List<(double x, double y)>>();
        foreach (var s in subs) if (s.Count >= (forFill ? 3 : 2)) res.Add(s);
        return res;
    }

    // ------------------------------------------------------------------ strokes

    private List<List<(double x, double y)>> StrokePolys()
    {
        var res = new List<List<(double x, double y)>>();
        var inv = st.xf.Inverse();
        double hw = st.lineWidth / 2;
        for (int si = 0; si < subs.Count; si++)
        {
            var dev = subs[si];
            if (dev.Count < 2) continue;
            // stroke in user space, then back to device pixels
            var pts = new List<(double x, double y)>();
            foreach (var p in dev)
            {
                var u = inv.Apply(p.x, p.y);
                if (pts.Count == 0 || JS.Hypot(u.Item1 - pts[pts.Count - 1].x, u.Item2 - pts[pts.Count - 1].y) > 1e-9) pts.Add(u);
            }
            bool closed = closedSubs[si];
            if (closed && pts.Count > 2 && JS.Hypot(pts[0].x - pts[pts.Count - 1].x, pts[0].y - pts[pts.Count - 1].y) > 1e-9) pts.Add(pts[0]);
            if (pts.Count < 2) continue;
            var pieces = st.lineDash.Length > 0 ? Dash(pts) : new List<List<(double x, double y)>> { pts };
            foreach (var piece in pieces)
            {
                bool loop = closed && st.lineDash.Length == 0;
                StrokeLine(piece, hw, loop, res);
            }
        }
        // back to device space
        for (int i = 0; i < res.Count; i++)
        {
            var poly = res[i];
            for (int k = 0; k < poly.Count; k++) poly[k] = st.xf.Apply(poly[k].x, poly[k].y);
        }
        return res;
    }

    private List<List<(double x, double y)>> Dash(List<(double x, double y)> pts)
    {
        var res = new List<List<(double x, double y)>>();
        var dash = st.lineDash;
        double total = 0;
        foreach (var d in dash) total += d;
        if (total <= 0) return new List<List<(double, double)>> { pts };
        int di = 0;
        double left = dash[0];
        double off = st.lineDashOffset % total;
        while (off > 0)
        {
            if (off >= left) { off -= left; di = (di + 1) % dash.Length; left = dash[di]; }
            else { left -= off; off = 0; }
        }
        bool on = di % 2 == 0;
        List<(double, double)> curDash = on ? new List<(double, double)> { pts[0] } : null;
        for (int i = 1; i < pts.Count; i++)
        {
            double ax = pts[i - 1].x, ay = pts[i - 1].y, bx = pts[i].x, by = pts[i].y;
            double segLen = JS.Hypot(bx - ax, by - ay), pos = 0;
            while (segLen - pos > left)
            {
                pos += left;
                double t = pos / segLen;
                var p = (ax + (bx - ax) * t, ay + (by - ay) * t);
                if (on) { curDash.Add(p); res.Add(curDash); curDash = null; }
                else curDash = new List<(double, double)> { p };
                on = !on;
                di = (di + 1) % dash.Length;
                left = dash[di];
            }
            left -= segLen - pos;
            if (on) curDash.Add((bx, by));
        }
        if (on && curDash != null && curDash.Count > 1) res.Add(curDash);
        return res;
    }

    private static void AddCircle(List<List<(double x, double y)>> res, double cx, double cy, double r)
    {
        int n = Math.Max(8, Math.Min(64, (int)Math.Ceiling(r * 2)));
        var c = new List<(double, double)>(n);
        for (int i = 0; i < n; i++)
        {
            double a = Math.PI * 2 * i / n;
            c.Add((cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        res.Add(c);
    }

    private void StrokeLine(List<(double x, double y)> pts, double hw, bool loop, List<List<(double x, double y)>> res)
    {
        int n = pts.Count;
        for (int i = 0; i + 1 < n; i++)
        {
            double ax = pts[i].x, ay = pts[i].y, bx = pts[i + 1].x, by = pts[i + 1].y;
            double dx = bx - ax, dy = by - ay, len = JS.Hypot(dx, dy);
            if (len < 1e-12) continue;
            double nx = -dy / len * hw, ny = dx / len * hw;
            double ex = 0, ey = 0, sx = 0, sy = 0;
            if (st.lineCap == "square")
            {
                if (i == 0 && !loop) { sx = -dx / len * hw; sy = -dy / len * hw; }
                if (i == n - 2 && !loop) { ex = dx / len * hw; ey = dy / len * hw; }
            }
            res.Add(new List<(double, double)>
            {
                (ax + sx + nx, ay + sy + ny), (bx + ex + nx, by + ey + ny), (bx + ex - nx, by + ey - ny), (ax + sx - nx, ay + sy - ny),
            });
        }
        // joins
        var joints = new List<((double x, double y) a, (double x, double y) p, (double x, double y) b)>();
        for (int i = 1; i < n - 1; i++) joints.Add((pts[i - 1], pts[i], pts[i + 1]));
        if (loop && n > 2) joints.Add((pts[n - 2], pts[n - 1], pts[1]));
        foreach (var (a, p, b) in joints)
        {
            if (st.lineJoin == "round") { AddCircle(res, p.x, p.y, hw); continue; }
            double d1x = p.x - a.x, d1y = p.y - a.y, d2x = b.x - p.x, d2y = b.y - p.y;
            double l1 = JS.Hypot(d1x, d1y), l2 = JS.Hypot(d2x, d2y);
            if (l1 < 1e-12 || l2 < 1e-12) continue;
            double n1x = -d1y / l1, n1y = d1x / l1, n2x = -d2y / l2, n2y = d2x / l2;
            double cross = d1x * d2y - d1y * d2x;
            // the outer side of the turn
            double sgn = cross > 0 ? -1 : 1;
            var o1 = (p.x + n1x * hw * sgn, p.y + n1y * hw * sgn);
            var o2 = (p.x + n2x * hw * sgn, p.y + n2y * hw * sgn);
            var tri = new List<(double, double)> { p, o1, o2 };
            if (st.lineJoin == "miter")
            {
                double mx = n1x + n2x, my = n1y + n2y, ml = JS.Hypot(mx, my);
                if (ml > 1e-9)
                {
                    double cosHalf = ml / 2;
                    double miterLen = 1 / Math.Max(1e-6, cosHalf);
                    if (miterLen <= st.miterLimit)
                    {
                        var tip = (p.x + mx / ml * hw * miterLen * sgn, p.y + my / ml * hw * miterLen * sgn);
                        tri = new List<(double, double)> { p, o1, tip, o2 };
                    }
                }
            }
            res.Add(tri);
        }
        if (!loop && st.lineCap == "round")
        {
            AddCircle(res, pts[0].x, pts[0].y, hw);
            AddCircle(res, pts[n - 1].x, pts[n - 1].y, hw);
        }
        // every piece the same way round, so the nonzero fill joins them instead of cutting holes
        foreach (var poly in res)
        {
            double area = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var p0 = poly[i];
                var p1 = poly[(i + 1) % poly.Count];
                area += p0.x * p1.y - p1.x * p0.y;
            }
            if (area < 0) poly.Reverse();
        }
    }

    // ------------------------------------------------------------------ the rasteriser

    private float[] covBuf;
    private float[] blurBuf, blurTmp;

    /// <summary>Coverage (0..1) for each pixel of the polygons, inside a bounding box. Four sub-scanlines per row, exact across x.</summary>
    private float[] Rasterize(List<List<(double x, double y)>> polys, bool evenOdd, out int bx0, out int by0, out int bw, out int bh)
    {
        int W = canvas.width, H = canvas.height;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var edges = new List<(double x0, double y0, double x1, double y1, int dir)>();
        foreach (var poly in polys)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                minX = Math.Min(minX, a.x); maxX = Math.Max(maxX, a.x);
                minY = Math.Min(minY, a.y); maxY = Math.Max(maxY, a.y);
                if (a.y == b.y) continue;
                if (a.y < b.y) edges.Add((a.x, a.y, b.x, b.y, 1));
                else edges.Add((b.x, b.y, a.x, a.y, -1));
            }
        }
        bx0 = Math.Max(0, (int)Math.Floor(minX));
        by0 = Math.Max(0, (int)Math.Floor(minY));
        int bx1 = Math.Min(W, (int)Math.Ceiling(maxX) + 1);
        int by1 = Math.Min(H, (int)Math.Ceiling(maxY) + 1);
        bw = Math.Max(0, bx1 - bx0);
        bh = Math.Max(0, by1 - by0);
        if (bw == 0 || bh == 0 || edges.Count == 0) { bw = 0; bh = 0; return null; }
        int need = bw * bh;
        if (covBuf == null || covBuf.Length < need) covBuf = new float[need];
        else Array.Clear(covBuf, 0, need);
        var xs = new List<(double x, int dir)>(32);
        const int SS = 4;
        // active edge list: edges sorted by their top, so each scanline only looks at the edges that cross it (a stroked
        // waveform or a map full of holes has hundreds of edges, and testing all of them on every scanline was the slow part)
        edges.Sort((p, q) => p.y0.CompareTo(q.y0));
        var active = new List<int>(32);
        int nextEdge = 0;
        for (int py = by0; py < by1; py++)
        {
            int row = (py - by0) * bw;
            for (int s = 0; s < SS; s++)
            {
                double sy = py + (s + 0.5) / SS;
                while (nextEdge < edges.Count && edges[nextEdge].y0 <= sy) active.Add(nextEdge++);
                xs.Clear();
                for (int k = active.Count - 1; k >= 0; k--)
                {
                    var e = edges[active[k]];
                    if (sy >= e.y1)
                    {
                        active[k] = active[active.Count - 1];
                        active.RemoveAt(active.Count - 1);
                        continue;
                    }
                    if (sy < e.y0) continue;
                    double t = (sy - e.y0) / (e.y1 - e.y0);
                    xs.Add((e.x0 + (e.x1 - e.x0) * t, e.dir));
                }
                if (xs.Count < 2) continue;
                xs.Sort((p, q) => p.x.CompareTo(q.x));
                int wind = 0;
                for (int i = 0; i < xs.Count - 1; i++)
                {
                    wind += xs[i].dir;
                    bool inside = evenOdd ? (wind & 1) != 0 : wind != 0;
                    if (!inside) continue;
                    double xa = Math.Max(bx0, xs[i].x), xb = Math.Min(bx1, xs[i + 1].x);
                    if (xb <= xa) continue;
                    int ia = (int)Math.Floor(xa), ib = (int)Math.Floor(xb);
                    const float w = 1f / SS;
                    if (ia == ib)
                    {
                        covBuf[row + ia - bx0] += (float)(xb - xa) * w;
                        continue;
                    }
                    covBuf[row + ia - bx0] += (float)(ia + 1 - xa) * w;
                    for (int x = ia + 1; x < ib && x < bx1; x++) covBuf[row + x - bx0] += w;
                    if (ib < bx1) covBuf[row + ib - bx0] += (float)(xb - ib) * w;
                }
            }
        }
        return covBuf;
    }

    private CssColor_RGBA SolidOf(object style)
    {
        if (style is string s)
        {
            var c = THREE.CssColor.Parse(s);
            return new CssColor_RGBA { r = c.r, g = c.g, b = c.b, a = c.a };
        }
        return new CssColor_RGBA { a = 1 };
    }

    private void FillPolys(List<List<(double x, double y)>> polys, object style, bool evenOdd)
    {
        if (polys.Count == 0) return;
        if (st.shadowBlur > 0 && st.globalCompositeOperation == "source-over")
        {
            var sc = THREE.CssColor.Parse(st.shadowColor);
            if (sc.a > 0)
            {
                var shifted = new List<List<(double x, double y)>>();
                foreach (var p in polys)
                {
                    var q = new List<(double, double)>(p.Count);
                    foreach (var v in p) q.Add((v.x + st.shadowOffsetX, v.y + st.shadowOffsetY));
                    shifted.Add(q);
                }
                var cov0 = Rasterize(shifted, evenOdd, out int sx0, out int sy0, out int sw, out int sh);
                if (cov0 != null)
                {
                    int pad = (int)Math.Ceiling(st.shadowBlur);
                    var blurred = BlurCoverage(cov0, sx0, sy0, sw, sh, pad, st.shadowBlur / 2, out int nx0, out int ny0, out int nw, out int nh);
                    CompositeSolid(blurred, nx0, ny0, nw, nh, new CssColor_RGBA { r = sc.r, g = sc.g, b = sc.b, a = sc.a });
                }
            }
        }
        var cov = Rasterize(polys, evenOdd, out int x0, out int y0, out int w, out int h);
        if (cov == null) return;
        if (style is CanvasGradient g)
        {
            var inv = st.xf.Inverse();
            Composite(cov, x0, y0, w, h, (px, py) =>
            {
                var (ux, uy) = inv.Apply(px, py);
                return GradientAt(g, ux, uy);
            });
        }
        else CompositeSolid(cov, x0, y0, w, h, SolidOf(style));
    }

    private static CssColor_RGBA GradientAt(CanvasGradient g, double x, double y)
    {
        if (!g.radial)
        {
            double dx = g.x1 - g.x0, dy = g.y1 - g.y0, l2 = dx * dx + dy * dy;
            double t = l2 == 0 ? 0 : ((x - g.x0) * dx + (y - g.y0) * dy) / l2;
            return g.At(t);
        }
        // two-circle radial gradient: find the largest t with |p - c(t)| = r(t), r(t) >= 0
        double cdx = g.x1 - g.x0, cdy = g.y1 - g.y0, dr = g.r1 - g.r0;
        double pdx = x - g.x0, pdy = y - g.y0;
        double a = cdx * cdx + cdy * cdy - dr * dr;
        double b = pdx * cdx + pdy * cdy + g.r0 * dr;
        double c = pdx * pdx + pdy * pdy - g.r0 * g.r0;
        double tt;
        if (Math.Abs(a) < 1e-9)
        {
            if (Math.Abs(b) < 1e-12) return new CssColor_RGBA();
            tt = c / (2 * b);
        }
        else
        {
            double disc = b * b - a * c;
            if (disc < 0) return new CssColor_RGBA();
            double sq = Math.Sqrt(disc);
            double t1 = (b + sq) / a, t2 = (b - sq) / a;
            tt = Math.Max(t1, t2);
            if (g.r0 + dr * tt < 0) tt = Math.Min(t1, t2);
        }
        if (g.r0 + dr * tt < 0) return new CssColor_RGBA();
        return g.At(tt);
    }

    private float[] BlurCoverage(float[] src, int x0, int y0, int w, int h, int pad, double sigma, out int nx0, out int ny0, out int nw, out int nh)
    {
        nx0 = Math.Max(0, x0 - pad);
        ny0 = Math.Max(0, y0 - pad);
        int nx1 = Math.Min(canvas.width, x0 + w + pad), ny1 = Math.Min(canvas.height, y0 + h + pad);
        nw = nx1 - nx0;
        nh = ny1 - ny0;
        // reused between calls: the detector's glowing line is blurred every frame, and fresh arrays each time fed the GC
        int n = nw * nh;
        if (blurBuf == null || blurBuf.Length < n) { blurBuf = new float[n]; blurTmp = new float[n]; }
        var buf = blurBuf;
        var tmp = blurTmp;
        Array.Clear(buf, 0, n);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            buf[(y + y0 - ny0) * nw + (x + x0 - nx0)] = src[y * w + x];
        int r = Math.Max(1, (int)Math.Round(sigma * 0.85));
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlurH(buf, tmp, nw, nh, r);
            BoxBlurV(tmp, buf, nw, nh, r);
        }
        return buf;
    }

    private static void BoxBlurH(float[] s, float[] d, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            float acc = 0;
            int row = y * w;
            for (int x = -r; x <= r; x++) acc += x >= 0 && x < w ? s[row + x] : 0;
            for (int x = 0; x < w; x++)
            {
                d[row + x] = acc * inv;
                int add = x + r + 1, sub = x - r;
                acc += (add < w ? s[row + add] : 0) - (sub >= 0 ? s[row + sub] : 0);
            }
        }
    }

    private static void BoxBlurV(float[] s, float[] d, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        for (int x = 0; x < w; x++)
        {
            float acc = 0;
            for (int y = -r; y <= r; y++) acc += y >= 0 && y < h ? s[y * w + x] : 0;
            for (int y = 0; y < h; y++)
            {
                d[y * w + x] = acc * inv;
                int add = y + r + 1, sub = y - r;
                acc += (add < h ? s[add * w + x] : 0) - (sub >= 0 ? s[sub * w + x] : 0);
            }
        }
    }

    // The canvas is drawn on the CPU, so the per-pixel loops below are the hot path of every canvas in the game (the minimap
    // alone redraws while you walk). The composite operation is resolved once per fill, solid colours skip the per-pixel paint
    // callback, and plain source-over blending is done inline.
    private enum Op { SourceOver, CopyClear, DestinationOut, DestinationIn, Lighter, Multiply, Screen, SourceAtop, DestinationOver }

    private static Op OpOf(string op)
    {
        switch (op)
        {
            case "copy-clear": return Op.CopyClear;
            case "destination-out": return Op.DestinationOut;
            case "destination-in": return Op.DestinationIn;
            case "lighter": return Op.Lighter;
            case "multiply": return Op.Multiply;
            case "screen": return Op.Screen;
            case "source-atop": return Op.SourceAtop;
            case "destination-over": return Op.DestinationOver;
            default: return Op.SourceOver;
        }
    }

    private static byte ToByte(double v)
    {
        int i = (int)(v + 0.5);
        return (byte)(i < 0 ? 0 : i > 255 ? 255 : i);
    }

    private static void ClearPixel(byte[] px, int i, float c)
    {
        float keep = 1 - c;
        px[i] = (byte)(px[i] * keep); px[i + 1] = (byte)(px[i + 1] * keep);
        px[i + 2] = (byte)(px[i + 2] * keep); px[i + 3] = (byte)(px[i + 3] * keep);
    }

    /// <summary>Blends paint into the canvas through a coverage mask, with globalAlpha and the composite operation.</summary>
    private void Composite(float[] cov, int x0, int y0, int w, int h, Func<double, double, CssColor_RGBA> paint)
    {
        var px = canvas.px;
        int W = canvas.width;
        var op = OpOf(st.globalCompositeOperation);
        double ga = st.globalAlpha;
        for (int y = 0; y < h; y++)
        {
            int cy = y + y0;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float c = cov[row + x];
                if (c <= 0.0005f) continue;
                if (c > 1) c = 1;
                int cx = x + x0;
                int i = (cy * W + cx) * 4;
                if (op == Op.CopyClear) { ClearPixel(px, i, c); continue; }
                var col = paint(cx + 0.5, cy + 0.5);
                double sa = col.a * ga * c;
                if (sa <= 0) continue;
                BlendPixel(px, i, col.r, col.g, col.b, sa, op);
            }
        }
        canvas.version++;
    }

    /// <summary>Composite for one solid colour: the common case (fills, strokes, clearRect), without a callback per pixel.</summary>
    private void CompositeSolid(float[] cov, int x0, int y0, int w, int h, CssColor_RGBA col)
    {
        var px = canvas.px;
        int W = canvas.width;
        var op = OpOf(st.globalCompositeOperation);
        double alpha = col.a * st.globalAlpha;
        if (op != Op.CopyClear && alpha <= 0) return;
        // premultiplied colour, 0..255
        double pr = col.r * 255, pg = col.g * 255, pb = col.b * 255;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int i = ((y + y0) * W + x0) * 4;
            for (int x = 0; x < w; x++, i += 4)
            {
                float c = cov[row + x];
                if (c <= 0.0005f) continue;
                if (c > 1) c = 1;
                if (op == Op.CopyClear) { ClearPixel(px, i, c); continue; }
                double sa = alpha * c;
                if (op == Op.SourceOver)
                {
                    double k = 1 - sa;
                    px[i] = ToByte(pr * sa + px[i] * k);
                    px[i + 1] = ToByte(pg * sa + px[i + 1] * k);
                    px[i + 2] = ToByte(pb * sa + px[i + 2] * k);
                    px[i + 3] = ToByte(255 * sa + px[i + 3] * k);
                }
                else if (op == Op.DestinationOut)
                {
                    // cutting a shape out (the minimap's round edge every frame): premultiplied pixels just fade
                    double k = 1 - sa;
                    px[i] = ToByte(px[i] * k);
                    px[i + 1] = ToByte(px[i + 1] * k);
                    px[i + 2] = ToByte(px[i + 2] * k);
                    px[i + 3] = ToByte(px[i + 3] * k);
                }
                else BlendPixel(px, i, col.r, col.g, col.b, sa, op);
            }
        }
        canvas.version++;
    }

    private static void BlendPixel(byte[] px, int i, double r, double g, double b, double sa, Op op)
    {
        double dr = px[i] / 255.0, dg = px[i + 1] / 255.0, db = px[i + 2] / 255.0, da = px[i + 3] / 255.0;
        double sr = r * sa, sg = g * sa, sb = b * sa;
        double or_, og, ob, oa;
        switch (op)
        {
            case Op.DestinationOut:
                or_ = dr * (1 - sa); og = dg * (1 - sa); ob = db * (1 - sa); oa = da * (1 - sa);
                break;
            case Op.DestinationIn:
                or_ = dr * sa; og = dg * sa; ob = db * sa; oa = da * sa;
                break;
            case Op.Lighter:
                or_ = Math.Min(1, sr + dr); og = Math.Min(1, sg + dg); ob = Math.Min(1, sb + db); oa = Math.Min(1, sa + da);
                break;
            case Op.Multiply:
                or_ = sr * (1 - da) + dr * (1 - sa) + sr * dr;
                og = sg * (1 - da) + dg * (1 - sa) + sg * dg;
                ob = sb * (1 - da) + db * (1 - sa) + sb * db;
                oa = sa + da * (1 - sa);
                break;
            case Op.Screen:
                or_ = sr + dr - sr * dr; og = sg + dg - sg * dg; ob = sb + db - sb * db; oa = sa + da * (1 - sa);
                break;
            case Op.SourceAtop:
                or_ = sr * da + dr * (1 - sa); og = sg * da + dg * (1 - sa); ob = sb * da + db * (1 - sa); oa = da;
                break;
            case Op.DestinationOver:
                or_ = dr + sr * (1 - da); og = dg + sg * (1 - da); ob = db + sb * (1 - da); oa = da + sa * (1 - da);
                break;
            default:
                or_ = sr + dr * (1 - sa); og = sg + dg * (1 - sa); ob = sb + db * (1 - sa); oa = sa + da * (1 - sa);
                break;
        }
        px[i] = ToByte(or_ * 255);
        px[i + 1] = ToByte(og * 255);
        px[i + 2] = ToByte(ob * 255);
        px[i + 3] = ToByte(oa * 255);
    }

    // ------------------------------------------------------------------ images

    public ImageData createImageData(int w, int h) => new ImageData(w, h);

    public ImageData getImageData(int sx, int sy, int sw, int sh)
    {
        var img = new ImageData(sw, sh);
        var px = canvas.px;
        int W = canvas.width, H = canvas.height;
        for (int y = 0; y < sh; y++)
        for (int x = 0; x < sw; x++)
        {
            int cx = sx + x, cy = sy + y;
            if (cx < 0 || cy < 0 || cx >= W || cy >= H) continue;
            int si = (cy * W + cx) * 4, di = (y * sw + x) * 4;
            int a = px[si + 3];
            img.data[di + 3] = (byte)a;
            if (a == 0) continue;
            img.data[di] = (byte)Math.Min(255, (px[si] * 255 + a / 2) / a);
            img.data[di + 1] = (byte)Math.Min(255, (px[si + 1] * 255 + a / 2) / a);
            img.data[di + 2] = (byte)Math.Min(255, (px[si + 2] * 255 + a / 2) / a);
        }
        return img;
    }

    public void putImageData(ImageData img, int dx, int dy)
    {
        var px = canvas.px;
        int W = canvas.width, H = canvas.height;
        for (int y = 0; y < img.height; y++)
        for (int x = 0; x < img.width; x++)
        {
            int cx = dx + x, cy = dy + y;
            if (cx < 0 || cy < 0 || cx >= W || cy >= H) continue;
            int si = (y * img.width + x) * 4, di = (cy * W + cx) * 4;
            int a = img.data[si + 3];
            px[di] = (byte)(img.data[si] * a / 255);
            px[di + 1] = (byte)(img.data[si + 1] * a / 255);
            px[di + 2] = (byte)(img.data[si + 2] * a / 255);
            px[di + 3] = (byte)a;
        }
        canvas.version++;
    }

    public void drawImage(HTMLCanvasElement img, double dx, double dy) => drawImage(img, 0, 0, img.width, img.height, dx, dy, img.width, img.height);

    public void drawImage(HTMLCanvasElement img, double dx, double dy, double dw, double dh) => drawImage(img, 0, 0, img.width, img.height, dx, dy, dw, dh);

    public void drawImage(HTMLCanvasElement img, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh)
    {
        if (sw == 0 || sh == 0 || dw == 0 || dh == 0) return;
        // a picture copied 1:1 onto whole pixels (no scaling, rotation, alpha or filter): copy rows, no resampling
        var t = st.xf;
        if (t.a == 1 && t.d == 1 && t.b == 0 && t.c == 0 && sw == dw && sh == dh && st.globalAlpha == 1 &&
            OpOf(st.globalCompositeOperation) == Op.SourceOver && ParseFilter(st.filter) == null)
        {
            double ox = dx + t.e, oy = dy + t.f;
            if (ox == Math.Floor(ox) && oy == Math.Floor(oy) && sx == Math.Floor(sx) && sy == Math.Floor(sy) && sw == Math.Floor(sw) && sh == Math.Floor(sh))
            {
                BlitAligned(img, (int)sx, (int)sy, (int)sw, (int)sh, (int)ox, (int)oy);
                return;
            }
        }
        // the destination rectangle in device space
        var poly = new List<(double x, double y)>
        {
            st.xf.Apply(dx, dy), st.xf.Apply(dx + dw, dy), st.xf.Apply(dx + dw, dy + dh), st.xf.Apply(dx, dy + dh),
        };
        var cov = Rasterize(new List<List<(double x, double y)>> { poly }, false, out int x0, out int y0, out int w, out int h);
        if (cov == null) return;
        var inv = st.xf.Inverse();
        var src = img.px;
        int iw = img.width, ih = img.height;
        var filt = ParseFilter(st.filter);
        bool smooth = st.imageSmoothingEnabled;
        if (filt == null && OpOf(st.globalCompositeOperation) == Op.SourceOver)
        {
            DrawImageFast(cov, x0, y0, w, h, inv, src, iw, ih, sx, sy, sw / dw, sh / dh, dx, dy, smooth);
            return;
        }
        Composite(cov, x0, y0, w, h, (px, py) =>
        {
            var (ux, uy) = inv.Apply(px, py);
            double u = sx + (ux - dx) / dw * sw - 0.5, v = sy + (uy - dy) / dh * sh - 0.5;
            double r, g, b, a;
            if (smooth) SampleBilinear(src, iw, ih, u, v, out r, out g, out b, out a);
            else SampleNearest(src, iw, ih, u + 0.5, v + 0.5, out r, out g, out b, out a);
            if (a <= 0) return new CssColor_RGBA();
            r /= a; g /= a; b /= a;
            if (filt != null) ApplyFilter(filt, ref r, ref g, ref b);
            return new CssColor_RGBA { r = r, g = g, b = b, a = a };
        });
    }

    /// <summary>Premultiplied source-over of a pixel-aligned rectangle; opaque pixels are simply copied.</summary>
    private void BlitAligned(HTMLCanvasElement img, int sx, int sy, int w, int h, int ox, int oy)
    {
        var src = img.px;
        var dst = canvas.px;
        int W = canvas.width, H = canvas.height, iw = img.width, ih = img.height;
        // the part that lies inside both the source picture and this canvas
        int x0 = Math.Max(0, Math.Max(-ox, -sx)), y0 = Math.Max(0, Math.Max(-oy, -sy));
        int x1 = Math.Min(w, Math.Min(W - ox, iw - sx)), y1 = Math.Min(h, Math.Min(H - oy, ih - sy));
        for (int y = y0; y < y1; y++)
        {
            int si = ((sy + y) * iw + sx + x0) * 4, di = ((oy + y) * W + ox + x0) * 4;
            for (int x = x0; x < x1; x++, si += 4, di += 4)
            {
                int a = src[si + 3];
                if (a == 0) continue;
                if (a == 255)
                {
                    dst[di] = src[si]; dst[di + 1] = src[si + 1]; dst[di + 2] = src[si + 2]; dst[di + 3] = 255;
                    continue;
                }
                int k = 255 - a;
                dst[di] = (byte)(src[si] + (dst[di] * k + 127) / 255);
                dst[di + 1] = (byte)(src[si + 1] + (dst[di + 1] * k + 127) / 255);
                dst[di + 2] = (byte)(src[si + 2] + (dst[di + 2] * k + 127) / 255);
                dst[di + 3] = (byte)(a + (dst[di + 3] * k + 127) / 255);
            }
        }
        canvas.version++;
    }

    /// <summary>
    /// drawImage with plain source-over and no filter, in one tight loop: the source position steps along each row (the
    /// transform is affine), and the premultiplied pixels blend straight in. Same result as the general path.
    /// </summary>
    private void DrawImageFast(float[] cov, int x0, int y0, int w, int h, Xf inv, byte[] src, int iw, int ih,
        double sx, double sy, double kx, double ky, double dx, double dy, bool smooth)
    {
        var px = canvas.px;
        int W = canvas.width;
        double ga = st.globalAlpha;
        // source position of a device point: u = sx - 0.5 + (ux - dx) * kx, with (ux, uy) = inv(device)
        double duX = inv.a * kx, dvX = inv.b * ky;
        for (int y = 0; y < h; y++)
        {
            double pyc = y + y0 + 0.5, pxc = x0 + 0.5;
            double u = sx - 0.5 + (inv.a * pxc + inv.c * pyc + inv.e - dx) * kx;
            double v = sy - 0.5 + (inv.b * pxc + inv.d * pyc + inv.f - dy) * ky;
            int row = y * w;
            int i = ((y + y0) * W + x0) * 4;
            for (int x = 0; x < w; x++, i += 4, u += duX, v += dvX)
            {
                float c = cov[row + x];
                if (c <= 0.0005f) continue;
                if (c > 1) c = 1;
                double R, G, B, A;
                if (smooth)
                {
                    int ux0 = (int)Math.Floor(u), vy0 = (int)Math.Floor(v);
                    double fx = u - ux0, fy = v - vy0;
                    int xa = ux0 < 0 ? 0 : ux0 >= iw ? iw - 1 : ux0, xb = ux0 + 1 < 0 ? 0 : ux0 + 1 >= iw ? iw - 1 : ux0 + 1;
                    int ya = vy0 < 0 ? 0 : vy0 >= ih ? ih - 1 : vy0, yb = vy0 + 1 < 0 ? 0 : vy0 + 1 >= ih ? ih - 1 : vy0 + 1;
                    int i00 = (ya * iw + xa) * 4, i10 = (ya * iw + xb) * 4, i01 = (yb * iw + xa) * 4, i11 = (yb * iw + xb) * 4;
                    double w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
                    A = src[i00 + 3] * w00 + src[i10 + 3] * w10 + src[i01 + 3] * w01 + src[i11 + 3] * w11;
                    if (A <= 0) continue;
                    R = src[i00] * w00 + src[i10] * w10 + src[i01] * w01 + src[i11] * w11;
                    G = src[i00 + 1] * w00 + src[i10 + 1] * w10 + src[i01 + 1] * w01 + src[i11 + 1] * w11;
                    B = src[i00 + 2] * w00 + src[i10 + 2] * w10 + src[i01 + 2] * w01 + src[i11 + 2] * w11;
                }
                else
                {
                    int nx = (int)Math.Floor(u + 0.5), ny = (int)Math.Floor(v + 0.5);
                    nx = nx < 0 ? 0 : nx >= iw ? iw - 1 : nx;
                    ny = ny < 0 ? 0 : ny >= ih ? ih - 1 : ny;
                    int si = (ny * iw + nx) * 4;
                    A = src[si + 3];
                    if (A <= 0) continue;
                    R = src[si]; G = src[si + 1]; B = src[si + 2];
                }
                // premultiplied source over: out = src * (ga * c) + dst * (1 - srcAlpha * ga * c)
                double s = ga * c, k = 1 - A / 255.0 * s;
                px[i] = ToByte(R * s + px[i] * k);
                px[i + 1] = ToByte(G * s + px[i + 1] * k);
                px[i + 2] = ToByte(B * s + px[i + 2] * k);
                px[i + 3] = ToByte(A * s + px[i + 3] * k);
            }
        }
        canvas.version++;
    }

    private static void SampleNearest(byte[] p, int w, int h, double u, double v, out double r, out double g, out double b, out double a)
    {
        int x = Math.Max(0, Math.Min(w - 1, (int)Math.Floor(u))), y = Math.Max(0, Math.Min(h - 1, (int)Math.Floor(v)));
        int i = (y * w + x) * 4;
        r = p[i] / 255.0; g = p[i + 1] / 255.0; b = p[i + 2] / 255.0; a = p[i + 3] / 255.0;
    }

    private static void SampleBilinear(byte[] p, int w, int h, double u, double v, out double r, out double g, out double b, out double a)
    {
        int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
        double fx = u - x0, fy = v - y0;
        r = g = b = a = 0;
        for (int k = 0; k < 4; k++)
        {
            int xx = Math.Max(0, Math.Min(w - 1, x0 + (k & 1))), yy = Math.Max(0, Math.Min(h - 1, y0 + (k >> 1)));
            double wt = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
            int i = (yy * w + xx) * 4;
            r += p[i] / 255.0 * wt; g += p[i + 1] / 255.0 * wt; b += p[i + 2] / 255.0 * wt; a += p[i + 3] / 255.0 * wt;
        }
    }

    // CSS filter functions as one colour matrix (sepia, hue-rotate, saturate, contrast, brightness, grayscale)
    private static double[] ParseFilter(string f)
    {
        if (string.IsNullOrEmpty(f) || f == "none") return null;
        double[] m = Identity5();
        int i = 0;
        while (i < f.Length)
        {
            int open = f.IndexOf('(', i);
            if (open < 0) break;
            int close = f.IndexOf(')', open);
            string name = f.Substring(i, open - i).Trim();
            string arg = f.Substring(open + 1, close - open - 1).Trim();
            double v;
            if (arg.EndsWith("deg")) v = double.Parse(arg.Replace("deg", ""), System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.EndsWith("%")) v = double.Parse(arg.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture) / 100;
            else v = double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            double[] fm = null;
            switch (name)
            {
                case "sepia":
                {
                    double a = 1 - Math.Min(1, v);
                    fm = Mat(0.393 + 0.607 * a, 0.769 - 0.769 * a, 0.189 - 0.189 * a,
                        0.349 - 0.349 * a, 0.686 + 0.314 * a, 0.168 - 0.168 * a,
                        0.272 - 0.272 * a, 0.534 - 0.534 * a, 0.131 + 0.869 * a);
                    break;
                }
                case "grayscale":
                {
                    double a = 1 - Math.Min(1, v);
                    fm = Mat(0.2126 + 0.7874 * a, 0.7152 - 0.7152 * a, 0.0722 - 0.0722 * a,
                        0.2126 - 0.2126 * a, 0.7152 + 0.2848 * a, 0.0722 - 0.0722 * a,
                        0.2126 - 0.2126 * a, 0.7152 - 0.7152 * a, 0.0722 + 0.9278 * a);
                    break;
                }
                case "saturate":
                    fm = Mat(0.213 + 0.787 * v, 0.715 - 0.715 * v, 0.072 - 0.072 * v,
                        0.213 - 0.213 * v, 0.715 + 0.285 * v, 0.072 - 0.072 * v,
                        0.213 - 0.213 * v, 0.715 - 0.715 * v, 0.072 + 0.928 * v);
                    break;
                case "hue-rotate":
                {
                    double a = v * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
                    fm = Mat(0.213 + c * 0.787 - s * 0.213, 0.715 - c * 0.715 - s * 0.715, 0.072 - c * 0.072 + s * 0.928,
                        0.213 - c * 0.213 + s * 0.143, 0.715 + c * 0.285 + s * 0.140, 0.072 - c * 0.072 - s * 0.283,
                        0.213 - c * 0.213 - s * 0.787, 0.715 - c * 0.715 + s * 0.715, 0.072 + c * 0.928 + s * 0.072);
                    break;
                }
                case "brightness":
                    fm = Mat(v, 0, 0, 0, v, 0, 0, 0, v);
                    break;
                case "contrast":
                {
                    fm = Mat(v, 0, 0, 0, v, 0, 0, 0, v);
                    double off = 0.5 - 0.5 * v;
                    fm[3] = off; fm[7] = off; fm[11] = off;
                    break;
                }
            }
            if (fm != null) m = Mul(fm, m);
            i = close + 1;
        }
        return m;
    }

    private static double[] Identity5() => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
    private static double[] Mat(double a, double b, double c, double d, double e, double f, double g, double h, double k) =>
        new[] { a, b, c, 0, d, e, f, 0, g, h, k, 0 };

    // 3x4 matrices (rgb rows plus an offset column)
    private static double[] Mul(double[] a, double[] b)
    {
        var r = new double[12];
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
                r[row * 4 + col] = a[row * 4] * b[col] + a[row * 4 + 1] * b[4 + col] + a[row * 4 + 2] * b[8 + col];
            r[row * 4 + 3] = a[row * 4] * b[3] + a[row * 4 + 1] * b[7] + a[row * 4 + 2] * b[11] + a[row * 4 + 3];
        }
        return r;
    }

    private static void ApplyFilter(double[] m, ref double r, ref double g, ref double b)
    {
        double nr = m[0] * r + m[1] * g + m[2] * b + m[3];
        double ng = m[4] * r + m[5] * g + m[6] * b + m[7];
        double nb = m[8] * r + m[9] * g + m[10] * b + m[11];
        r = Math.Max(0, Math.Min(1, nr));
        g = Math.Max(0, Math.Min(1, ng));
        b = Math.Max(0, Math.Min(1, nb));
    }

    // ------------------------------------------------------------------ text

    public TextMetrics measureText(string text)
    {
        var f = CanvasFonts.Get(st.font);
        f.Prepare(text);
        double w = 0;
        foreach (var ch in text) w += f.Advance(ch);
        return new TextMetrics { width = w, actualBoundingBoxAscent = f.ascent, actualBoundingBoxDescent = f.descent };
    }

    public void fillText(string text, double x, double y, double maxWidth = 0) => DrawText(text, x, y, st.fillStyle, false, maxWidth);

    public void strokeText(string text, double x, double y, double maxWidth = 0) => DrawText(text, x, y, st.strokeStyle, true, maxWidth);

    private void DrawText(string text, double x, double y, object style, bool outline, double maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return;
        var f = CanvasFonts.Get(st.font);
        f.Prepare(text);
        double total = 0;
        foreach (var ch in text) total += f.Advance(ch);
        double squeeze = maxWidth > 0 && total > maxWidth ? maxWidth / total : 1;
        string align = st.textAlign;
        if (align == "start") align = "left";
        if (align == "end") align = "right";
        double ox = align == "center" ? -total * squeeze / 2 : align == "right" ? -total * squeeze : 0;
        double oy = st.textBaseline switch
        {
            "middle" => (f.ascent - f.descent) / 2,
            "top" => f.ascent,
            "hanging" => f.ascent * 0.8,
            "bottom" => -f.descent,
            "ideographic" => -f.descent,
            _ => 0,
        };
        // a coverage mask for the whole line in device space, then one composite
        var devScale = st.xf.Scale;
        var poly = new List<(double x, double y)>
        {
            st.xf.Apply(x + ox - 2, y + oy - f.ascent - 2), st.xf.Apply(x + ox + total * squeeze + 2, y + oy - f.ascent - 2),
            st.xf.Apply(x + ox + total * squeeze + 2, y + oy + f.descent + 2), st.xf.Apply(x + ox - 2, y + oy + f.descent + 2),
        };
        var box = Rasterize(new List<List<(double x, double y)>> { poly }, false, out int x0, out int y0, out int w, out int h);
        if (box == null) return;
        var mask = new float[w * h];
        var inv = st.xf.Inverse();
        // where each glyph starts, in user units along the line
        var starts = new double[text.Length];
        double pen = 0;
        for (int i = 0; i < text.Length; i++) { starts[i] = pen; pen += f.Advance(text[i]) * squeeze; }
        for (int py = 0; py < h; py++)
        for (int pxl = 0; pxl < w; pxl++)
        {
            // two by two samples per pixel
            float acc = 0;
            for (int s = 0; s < 4; s++)
            {
                double sx = x0 + pxl + 0.25 + 0.5 * (s & 1), sy = y0 + py + 0.25 + 0.5 * (s >> 1);
                var (ux, uy) = inv.Apply(sx, sy);
                double lx = (ux - (x + ox)), ly = uy - (y + oy);
                // which glyph
                int gi = Array.BinarySearch(starts, lx);
                if (gi < 0) gi = ~gi - 1;
                if (gi < 0 || gi >= text.Length) continue;
                acc += f.Coverage(text[gi], (lx - starts[gi]) / squeeze, ly);
            }
            mask[py * w + pxl] = acc / 4;
        }
        if (outline)
        {
            // a stroked outline: the mask's edge band
            var edge = new float[w * h];
            int r = Math.Max(1, (int)Math.Round(st.lineWidth * devScale / 2));
            for (int py = 0; py < h; py++)
            for (int pxl = 0; pxl < w; pxl++)
            {
                float mx = 0, mn = 1;
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = pxl + dx, yy = py + dy;
                    float v = xx >= 0 && yy >= 0 && xx < w && yy < h ? mask[yy * w + xx] : 0;
                    if (v > mx) mx = v;
                    if (v < mn) mn = v;
                }
                edge[py * w + pxl] = mx - mn;
            }
            mask = edge;
        }
        if (st.shadowBlur > 0)
        {
            var sc = THREE.CssColor.Parse(st.shadowColor);
            if (sc.a > 0)
            {
                int pad = (int)Math.Ceiling(st.shadowBlur);
                var blurred = BlurCoverage(mask, x0 + (int)st.shadowOffsetX, y0 + (int)st.shadowOffsetY, w, h, pad, st.shadowBlur / 2, out int nx0, out int ny0, out int nw, out int nh);
                Composite(blurred, nx0, ny0, nw, nh, (a, b) => new CssColor_RGBA { r = sc.r, g = sc.g, b = sc.b, a = sc.a });
            }
        }
        Func<double, double, CssColor_RGBA> paint;
        if (style is CanvasGradient g)
        {
            var gi2 = st.xf.Inverse();
            paint = (px, py) => { var (ux, uy) = gi2.Apply(px, py); return GradientAt(g, ux, uy); };
        }
        else
        {
            var solid = SolidOf(style);
            paint = (px, py) => solid;
        }
        Composite(mask, x0, y0, w, h, paint);
    }
}
