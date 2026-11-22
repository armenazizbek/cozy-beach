using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The loading screen's picture: a golden morning over the sea, drawn by the GPU as one UI Toolkit layer and moving all the
/// time the world is being built. Layers of waves roll in, the sun glints on the water, gulls drift past and a message in a
/// bottle bobs on the swell. It answers the player while they wait: the water rises under the pointer, a click (a tap on a
/// Deck's screen, A on a controller, Space on a keyboard) makes a splash, and a click on the bottle makes it hop.
/// </summary>
public class LoadingSea : VisualElement
{
    // colours: the sky from soft teal down to a golden horizon, the sea from pale far water to the deep teal near the shore
    private static readonly Color32 SKY_TOP = new Color32(0x4e, 0x9a, 0xa6, 255);
    private static readonly Color32 SKY_MID = new Color32(0xa7, 0xcf, 0xc4, 255);
    private static readonly Color32 SKY_LOW = new Color32(0xf6, 0xd6, 0x9e, 255);
    private static readonly Color32 FAR_SEA = new Color32(0x86, 0xbd, 0xb6, 255);
    private static readonly Color32[] SEA =
    {
        new Color32(0x63, 0xa9, 0xaa, 255),
        new Color32(0x4b, 0x94, 0x9c, 255),
        new Color32(0x39, 0x80, 0x8b, 255),
        new Color32(0x2b, 0x6f, 0x78, 255),
    };
    private static readonly Color SUN = new Color(1f, 0.93f, 0.72f, 1f);
    private static readonly Color FOAM = new Color(1f, 0.96f, 0.86f, 1f);
    private static readonly Color GULL = new Color(0.2f, 0.16f, 0.13f, 0.65f);

    // each wave layer: where it sits between the horizon and the bottom (0..1), its height and its pace
    private static readonly float[] BASE = { 0.07f, 0.24f, 0.47f, 0.74f };
    private static readonly float[] AMP = { 0.004f, 0.009f, 0.016f, 0.026f };
    private static readonly float[] SPEED = { 0.35f, 0.5f, 0.7f, 0.95f };
    private static readonly float[] FREQ = { 9f, 6.5f, 4.6f, 3.2f };
    private const float HORIZON = 0.64f;
    private const int STEPS = 96;

    private float t;
    private float last = -1;
    private float pointerX = -1;
    private float pointerLift;
    private float pointerIdle = 99;

    private struct Ripple { public float x, age, power; }
    private struct Drop { public float x, y, vx, vy, life, size; }
    private readonly List<Ripple> ripples = new List<Ripple>();
    private readonly List<Drop> drops = new List<Drop>();
    private readonly System.Random rand = new System.Random();

    // the bottle: where it floats across the screen (0..1), and its own hop above the water
    private float bottleHop;
    private float bottleHopV;
    private Vector2 bottleAt;

    public LoadingSea()
    {
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.left = 0;
        style.top = 0;
        style.right = 0;
        style.bottom = 0;
        generateVisualContent += Draw;
        schedule.Execute(Step).Every(0);
    }

    /// <summary>Listens on the whole loading screen (its texts and bar included), so the sea answers wherever the pointer is.</summary>
    public void Listen(VisualElement screen)
    {
        screen.RegisterCallback<PointerMoveEvent>(e =>
        {
            pointerX = this.WorldToLocal(e.position).x;
            pointerIdle = 0;
        });
        screen.RegisterCallback<PointerDownEvent>(e =>
        {
            var at = this.WorldToLocal(e.position);
            pointerX = at.x;
            pointerIdle = 0;
            if (Vector2.Distance(at, bottleAt) < Size * 0.06f) Hop();
            else Splash(at.x, 1f);
        });
    }

    private float W => contentRect.width;
    private float H => contentRect.height;
    private float Size => Math.Min(W, H);

    private void Step()
    {
        float now = Time.realtimeSinceStartup;
        // a long frame (a big piece of the world being built) only moves the sea on by a short step, never in a jump
        float dt = last < 0 ? 0.016f : Mathf.Clamp(now - last, 0f, 0.05f);
        last = now;
        // before the first layout the size is not known yet (NaN), and it would poison the bottle's hop for good
        if (!(W > 1 && H > 1)) return;
        t += dt;

        try
        {
            if (Gamepad.current?.buttonSouth.wasPressedThisFrame == true || Keyboard.current?.spaceKey.wasPressedThisFrame == true)
                Splash(W * (0.15f + 0.7f * (float)rand.NextDouble()), 1f);
        }
        catch
        {
            /* no input devices: the sea simply rolls on */
        }

        pointerIdle += dt;
        float lift = pointerX >= 0 && pointerIdle < 1.2f ? 1f : 0f;
        pointerLift += (lift - pointerLift) * Math.Min(1f, dt * 3f);

        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            var r = ripples[i];
            r.age += dt;
            if (r.age > 4f) ripples.RemoveAt(i);
            else ripples[i] = r;
        }

        float g = Size * 1.6f;
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i];
            d.vy += g * dt;
            d.x += d.vx * dt;
            d.y += d.vy * dt;
            d.life -= dt;
            if (d.life <= 0) drops.RemoveAt(i);
            else drops[i] = d;
        }

        // the bottle falls back onto the water after a hop
        bottleHopV -= Size * 2.2f * dt;
        bottleHop += bottleHopV * dt;
        if (bottleHop < 0)
        {
            if (bottleHopV < -Size * 0.3f) Splash(bottleAt.x, 0.6f);
            bottleHop = 0;
            bottleHopV = 0;
        }

        MarkDirtyRepaint();
    }

    private void Hop()
    {
        if (bottleHop > Size * 0.01f) return;
        bottleHopV = Size * 0.75f;
        Splash(bottleAt.x, 0.5f);
    }

    private void Splash(float x, float power)
    {
        if (W <= 0) return;
        ripples.Add(new Ripple { x = x, power = power });
        int layer = 3;
        float y = WaveY(layer, x);
        int n = (int)(14 * power) + 6;
        for (int i = 0; i < n; i++)
        {
            float a = (float)(Math.PI * (0.15 + 0.7 * rand.NextDouble()));
            float v = Size * (0.35f + 0.45f * (float)rand.NextDouble()) * (0.6f + 0.4f * power);
            drops.Add(new Drop
            {
                x = x + (float)(rand.NextDouble() - 0.5) * Size * 0.03f,
                y = y,
                vx = -Mathf.Cos(a) * v * 0.6f,
                vy = -Mathf.Sin(a) * v,
                life = 0.8f + 0.5f * (float)rand.NextDouble(),
                size = Size * (0.004f + 0.005f * (float)rand.NextDouble()),
            });
        }
    }

    private static float Bump(float d) => Mathf.Exp(-d * d);

    /// <summary>The height of a wave layer's surface at x, with the pointer's swell and the splashes' rings in it.</summary>
    private float WaveY(int k, float x)
    {
        float h = H, w = Math.Max(1f, W);
        float horizon = h * HORIZON;
        float baseY = horizon + (h - horizon) * BASE[k];
        float u = x / w;
        float y = baseY + h * AMP[k] * (0.62f * Mathf.Sin(u * FREQ[k] * 6.283f + t * SPEED[k] * 2.1f + k * 1.7f)
                                        + 0.38f * Mathf.Sin(u * FREQ[k] * 11.1f - t * SPEED[k] * 1.3f + k));
        float near = (k + 1) / 4f;
        if (pointerLift > 0.001f && pointerX >= 0)
            y -= h * 0.022f * near * pointerLift * Bump((x - pointerX) / (w * 0.07f)) * (0.85f + 0.15f * Mathf.Sin(t * 4f));
        foreach (var r in ripples)
        {
            float spread = w * 0.16f * r.age;
            float fade = Mathf.Exp(-r.age * 1.1f) * r.power;
            float ring = Bump((x - r.x - spread) / (w * 0.035f)) + Bump((x - r.x + spread) / (w * 0.035f));
            y -= h * 0.03f * near * fade * ring;
        }
        return y;
    }

    private void Draw(MeshGenerationContext mgc)
    {
        float w = W, h = H;
        if (w <= 1 || h <= 1) return;
        float horizon = h * HORIZON;

        // sky in two bands, then the far flat water below the horizon
        Gradient(mgc, 0, 0, w, horizon * 0.55f, SKY_TOP, SKY_MID);
        Gradient(mgc, 0, horizon * 0.55f, w, horizon, SKY_MID, SKY_LOW);

        var p = mgc.painter2D;

        // the sun, half risen, with a soft glow
        float sunR = h * 0.075f;
        var sun = new Vector2(w * 0.78f, horizon - sunR * 0.3f);
        Circle(p, sun, sunR * 2.6f, new Color(SUN.r, SUN.g, SUN.b, 0.10f));
        Circle(p, sun, sunR * 1.7f, new Color(SUN.r, SUN.g, SUN.b, 0.18f));
        Circle(p, sun, sunR, SUN);
        // the far water covers the sun's lower part, so it sits on the horizon
        Gradient(mgc, 0, horizon, w, h, FAR_SEA, SEA[0]);

        Clouds(p, w, h);
        Gulls(p, w, h);

        Wave(p, 0, w, h);
        for (int k = 1; k < 4; k++) Wave(p, k, w, h);
        Reflection(mgc, sun, sunR, horizon, h);
        // the bottle rides the third wave
        Bottle(p, w, h);

        // splash drops
        foreach (var d in drops)
            Circle(p, new Vector2(d.x, d.y), d.size, new Color(FOAM.r, FOAM.g, FOAM.b, Mathf.Clamp01(d.life * 1.4f) * 0.9f));
    }

    private void Wave(Painter2D p, int k, float w, float h)
    {
        var pts = new Vector2[STEPS + 1];
        for (int i = 0; i <= STEPS; i++)
        {
            float x = w * i / STEPS;
            pts[i] = new Vector2(x, WaveY(k, x));
        }
        p.fillColor = SEA[k];
        p.BeginPath();
        p.MoveTo(new Vector2(0, h + 2));
        foreach (var q in pts) p.LineTo(q);
        p.LineTo(new Vector2(w, h + 2));
        p.ClosePath();
        p.Fill();

        // a line of foam along the crest
        p.strokeColor = new Color(FOAM.r, FOAM.g, FOAM.b, 0.22f + 0.1f * k);
        p.lineWidth = Math.Max(1.5f, h * (0.002f + 0.0015f * k));
        p.lineJoin = LineJoin.Round;
        p.BeginPath();
        p.MoveTo(pts[0]);
        for (int i = 1; i <= STEPS; i++) p.LineTo(pts[i]);
        p.Stroke();
    }

    private void Bottle(Painter2D p, float w, float h)
    {
        float x = w * (0.24f + 0.03f * Mathf.Sin(t * 0.23f));
        float y = WaveY(2, x);
        float slope = (WaveY(2, x + 6) - WaveY(2, x - 6)) / 12f;
        float angle = Mathf.Atan(slope) * 0.9f + 0.18f * Mathf.Sin(t * 1.4f) + 1.3f;
        float s = h * 0.0024f;
        var c = new Vector2(x, y - s * 5 - bottleHop);
        bottleAt = c;
        float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
        // the bottle's own frame: x across it, y along it from the base (+) to the cork (-)
        Vector2 R(float bx, float by) => c + new Vector2(bx * cs - by * sn, bx * sn + by * cs) * s;
        const float BW = 12;

        void Glass()
        {
            p.BeginPath();
            p.MoveTo(R(-BW + 4, 26));
            p.LineTo(R(BW - 4, 26));
            p.QuadraticCurveTo(R(BW, 26), R(BW, 22));
            p.LineTo(R(BW, -12));
            p.BezierCurveTo(R(BW, -19), R(4.5f, -17), R(4.5f, -23));
            p.LineTo(R(4.5f, -31));
            p.LineTo(R(5.8f, -31));
            p.LineTo(R(5.8f, -35));
            p.LineTo(R(-5.8f, -35));
            p.LineTo(R(-5.8f, -31));
            p.LineTo(R(-4.5f, -31));
            p.LineTo(R(-4.5f, -23));
            p.BezierCurveTo(R(-4.5f, -17), R(-BW, -19), R(-BW, -12));
            p.LineTo(R(-BW, 22));
            p.QuadraticCurveTo(R(-BW, 26), R(-BW + 4, 26));
            p.ClosePath();
        }
        void Quad(Color col, Vector2 a, Vector2 b, Vector2 d, Vector2 e)
        {
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.LineTo(d);
            p.LineTo(e);
            p.ClosePath();
            p.Fill();
        }

        // the glass behind the letter
        p.fillColor = new Color(0.33f, 0.6f, 0.47f, 0.55f);
        Glass();
        p.Fill();
        // the rolled letter, tied with a red ribbon
        Quad(new Color(1f, 0.93f, 0.78f, 1f), R(-5.5f, -9), R(5.5f, -9), R(5.5f, 18), R(-5.5f, 18));
        Quad(new Color(0.86f, 0.74f, 0.55f, 1f), R(-5.5f, -9), R(5.5f, -9), R(5.5f, -6.5f), R(-5.5f, -6.5f));
        Quad(new Color(0.86f, 0.74f, 0.55f, 1f), R(-5.5f, 15.5f), R(5.5f, 15.5f), R(5.5f, 18), R(-5.5f, 18));
        Quad(new Color(0.78f, 0.24f, 0.2f, 1f), R(-5.6f, 3), R(5.6f, 3), R(5.6f, 6.5f), R(-5.6f, 6.5f));
        // the glass in front: a light green wash, then its edge
        p.fillColor = new Color(0.55f, 0.82f, 0.66f, 0.28f);
        Glass();
        p.Fill();
        p.strokeColor = new Color(0.14f, 0.32f, 0.25f, 0.9f);
        p.lineWidth = Math.Max(1.2f, s * 1.4f);
        p.lineJoin = LineJoin.Round;
        Glass();
        p.Stroke();
        // the cork
        Quad(new Color(0.66f, 0.45f, 0.27f, 1f), R(-4.2f, -35), R(4.2f, -35), R(3.6f, -42), R(-3.6f, -42));
        p.strokeColor = new Color(0.36f, 0.22f, 0.12f, 1f);
        p.lineWidth = Math.Max(1f, s);
        p.BeginPath();
        p.MoveTo(R(-4.2f, -35));
        p.LineTo(R(4.2f, -35));
        p.LineTo(R(3.6f, -42));
        p.LineTo(R(-3.6f, -42));
        p.ClosePath();
        p.Stroke();
        // light on the glass
        p.strokeColor = new Color(1f, 1f, 1f, 0.6f);
        p.lineWidth = Math.Max(1.5f, s * 2.2f);
        p.lineCap = LineCap.Round;
        p.BeginPath();
        p.MoveTo(R(-8.5f, 19));
        p.LineTo(R(-8.5f, -9));
        p.Stroke();
        p.lineWidth = Math.Max(1f, s * 1.4f);
        p.BeginPath();
        p.MoveTo(R(-2.8f, -24));
        p.LineTo(R(-2.8f, -29));
        p.Stroke();

        // the water over its lower part, so it sits in the wave rather than on it
        if (bottleHop < s * 2)
        {
            float half = s * 34;
            const int N = 14;
            var crest = new Vector2[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float xi = x - half + 2 * half * i / N;
                crest[i] = new Vector2(xi, WaveY(2, xi));
            }
            p.fillColor = new Color(SEA[2].r / 255f, SEA[2].g / 255f, SEA[2].b / 255f, 0.72f);
            p.BeginPath();
            p.MoveTo(crest[0]);
            for (int i = 1; i <= N; i++) p.LineTo(crest[i]);
            p.LineTo(new Vector2(x + half, crest[N].y + s * 18));
            p.LineTo(new Vector2(x - half, crest[0].y + s * 18));
            p.ClosePath();
            p.Fill();
            // the crest's foam again across it, and a ring of ripples round the bottle
            p.strokeColor = new Color(FOAM.r, FOAM.g, FOAM.b, 0.55f);
            p.lineWidth = Math.Max(1.5f, h * 0.005f);
            p.BeginPath();
            p.MoveTo(crest[2]);
            for (int i = 3; i <= N - 2; i++) p.LineTo(crest[i]);
            p.Stroke();
            float ring = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);
            p.strokeColor = new Color(FOAM.r, FOAM.g, FOAM.b, 0.35f * (1 - ring) + 0.1f);
            p.lineWidth = Math.Max(1f, s);
            p.BeginPath();
            float rx = s * (30 + 10 * ring), ry = s * (3.5f + 1.5f * ring);
            for (int i = 0; i <= 28; i++)
            {
                float a = Mathf.PI * 2 * i / 28;
                var q = new Vector2(x + Mathf.Cos(a) * rx, y + s * 3 + Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(q);
                else p.LineTo(q);
            }
            p.Stroke();
        }
    }

    /// <summary>
    /// The sun's light on the water: a soft warm glow under the sun, brightest at the horizon and fading down and to the sides,
    /// gently swaying. A small grid, so the fade is smooth in both directions.
    /// </summary>
    private void Reflection(MeshGenerationContext mgc, Vector2 sun, float sunR, float horizon, float h)
    {
        const int COLS = 8, ROWS = 8;
        float top = horizon, bottom = horizon + (h - horizon) * 0.75f;
        float shimmer = 0.88f + 0.12f * Mathf.Sin(t * 2.6f);
        var md = mgc.Allocate((COLS + 1) * (ROWS + 1), COLS * ROWS * 6);
        for (int r = 0; r <= ROWS; r++)
        {
            float v = (float)r / ROWS;
            float y = top + (bottom - top) * v;
            float half = sunR * (0.9f + 1.4f * v);
            float sway = Mathf.Sin(t * 0.9f + v * 2.5f) * sunR * 0.1f * v;
            float down = (1 - v) * (1 - v);
            for (int c = 0; c <= COLS; c++)
            {
                float u = (float)c / COLS * 2 - 1;
                float across = Mathf.Max(0, 1 - u * u);
                byte alpha = (byte)(95 * shimmer * down * across * across);
                md.SetNextVertex(new Vertex { position = new Vector3(sun.x + sway + u * half, y, Vertex.nearZ), tint = new Color32(255, 236, 190, alpha) });
            }
        }
        for (int r = 0; r < ROWS; r++)
            for (int c = 0; c < COLS; c++)
            {
                ushort i0 = (ushort)(r * (COLS + 1) + c), i1 = (ushort)(i0 + 1), i2 = (ushort)(i0 + COLS + 1), i3 = (ushort)(i2 + 1);
                md.SetNextIndex(i0); md.SetNextIndex(i1); md.SetNextIndex(i3);
                md.SetNextIndex(i0); md.SetNextIndex(i3); md.SetNextIndex(i2);
            }
    }

    private void Clouds(Painter2D p, float w, float h)
    {
        for (int i = 0; i < 3; i++)
        {
            float span = w * 1.5f;
            float x = Mathf.Repeat(w * (0.12f + 0.4f * i) + t * w * (0.006f + 0.003f * i), span) - w * 0.25f;
            float baseY = h * (0.16f + 0.08f * i);
            float cw = h * (0.3f - 0.05f * i);
            float ch = cw * 0.3f;
            // a soft halo, then the cloud, then a faint shade along its flat underside
            Cloud(p, x, baseY + ch * 0.04f, cw * 1.06f, ch * 1.1f, new Color(1f, 0.98f, 0.94f, 0.22f), i);
            Cloud(p, x, baseY, cw, ch, new Color(1f, 0.98f, 0.94f, 0.88f), i);
            Cloud(p, x + cw * 0.03f, baseY, cw * 0.82f, ch * 0.22f, new Color(0.86f, 0.9f, 0.93f, 0.16f), i + 3);
        }
    }

    /// <summary>One cloud as a single shape: a flat base and three rounded puffs along the top.</summary>
    private static void Cloud(Painter2D p, float x, float baseY, float cw, float ch, Color col, int seed)
    {
        float x0 = x - cw / 2, x1 = x + cw / 2;
        float valley = baseY - ch * 0.42f;
        // the puffs from right to left: their widths (shares of the cloud) and heights
        float[] widths = seed % 2 == 0 ? new[] { 0.28f, 0.4f, 0.32f } : new[] { 0.34f, 0.36f, 0.3f };
        float[] heights = seed % 2 == 0 ? new[] { 0.7f, 1.0f, 0.62f } : new[] { 0.62f, 1.0f, 0.75f };
        float inner = cw - ch * 0.5f;
        p.fillColor = col;
        p.BeginPath();
        p.MoveTo(new Vector2(x0 + ch * 0.3f, baseY));
        p.LineTo(new Vector2(x1 - ch * 0.3f, baseY));
        p.BezierCurveTo(new Vector2(x1 + ch * 0.05f, baseY), new Vector2(x1 + ch * 0.05f, valley), new Vector2(x1 - ch * 0.25f, valley));
        float at = x1 - ch * 0.25f;
        for (int k = 0; k < 3; k++)
        {
            float next = at - inner * widths[k];
            float top = baseY - ch * heights[k] * 1.33f;
            p.BezierCurveTo(new Vector2(at + ch * 0.05f, top), new Vector2(next - ch * 0.05f, top), new Vector2(next, valley));
            at = next;
        }
        p.BezierCurveTo(new Vector2(x0 - ch * 0.05f, valley), new Vector2(x0 - ch * 0.05f, baseY), new Vector2(x0 + ch * 0.3f, baseY));
        p.ClosePath();
        p.Fill();
    }

    private void Gulls(Painter2D p, float w, float h)
    {
        p.strokeColor = GULL;
        p.lineWidth = Math.Max(1.5f, h * 0.0028f);
        p.lineCap = LineCap.Round;
        for (int i = 0; i < 3; i++)
        {
            float span = w + h * 0.3f;
            float x = Mathf.Repeat(w * (0.2f + 0.31f * i) + t * w * (0.018f + 0.006f * i), span) - h * 0.15f;
            float y = h * (0.2f + 0.06f * i) + Mathf.Sin(t * 0.6f + i * 2f) * h * 0.012f;
            float span2 = h * (0.018f - 0.003f * i);
            float flap = Mathf.Sin(t * (5f + i) + i) * span2 * 0.45f;
            p.BeginPath();
            p.MoveTo(new Vector2(x - span2, y - flap * 0.4f));
            p.QuadraticCurveTo(new Vector2(x - span2 * 0.45f, y - span2 * 0.55f - flap), new Vector2(x, y));
            p.QuadraticCurveTo(new Vector2(x + span2 * 0.45f, y - span2 * 0.55f - flap), new Vector2(x + span2, y - flap * 0.4f));
            p.Stroke();
        }
    }

    private static void Circle(Painter2D p, Vector2 c, float r, Color col)
    {
        p.fillColor = col;
        p.BeginPath();
        p.Arc(c, r, 0f, 360f);
        p.ClosePath();
        p.Fill();
    }

    /// <summary>A rectangle shaded from one colour at the top to another at the bottom.</summary>
    private static void Gradient(MeshGenerationContext mgc, float x0, float y0, float x1, float y1, Color32 top, Color32 bottom)
    {
        var md = mgc.Allocate(4, 6);
        md.SetNextVertex(new Vertex { position = new Vector3(x0, y0, Vertex.nearZ), tint = top });
        md.SetNextVertex(new Vertex { position = new Vector3(x1, y0, Vertex.nearZ), tint = top });
        md.SetNextVertex(new Vertex { position = new Vector3(x1, y1, Vertex.nearZ), tint = bottom });
        md.SetNextVertex(new Vertex { position = new Vector3(x0, y1, Vertex.nearZ), tint = bottom });
        md.SetNextIndex(0);
        md.SetNextIndex(1);
        md.SetNextIndex(2);
        md.SetNextIndex(0);
        md.SetNextIndex(2);
        md.SetNextIndex(3);
    }
}
