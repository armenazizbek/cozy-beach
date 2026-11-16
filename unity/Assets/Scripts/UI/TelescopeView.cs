using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The view through the telescope on Lagoon Cay: the whole screen goes dark but for a round eyepiece full of sky. The sky is a
/// band of stars all the way round, from the sea on the horizon to high overhead; the player pans it with the mouse (drag),
/// the keyboard or a stick, and zooms with the wheel, Q / E or the triggers. The stars are there at any hour, but they are
/// fainter by day and brightest at night.
///
/// Hidden among them are the constellations of <see cref="Constellations"/>. A constellation is found by clicking its stars
/// (each one lights, and the lines between lit neighbours appear) or by holding it framed in the eyepiece, which slowly brings
/// it into focus. Once found its lines are drawn in with a soft glow, its name is written under it and a card tells its lore.
/// Nothing is timed and nothing can be failed.
///
/// The sky is drawn by the GPU as one UI Toolkit layer (generateVisualContent): soft textured quads for the stars, the glow of
/// the Milky Way and the lines; the eyepiece's black surround and brass rim are meshes on top. The words are DOM elements.
/// </summary>
public class TelescopeView
{
    // ---------------------------------------------------------------- the sky's stars

    private struct Star
    {
        public double az, alt, b, phase, speed;
        public Color32 tint;
        /// <summary>the constellation the star belongs to and its index there, or null for a star of the field</summary>
        public string con;
        public int idx;
    }

    private readonly List<Star> stars = new List<Star>();
    private readonly List<(double az, double alt, double r, double a)> milky = new List<(double, double, double, double)>();
    /// <summary>for each constellation: where its stars are in the list above</summary>
    private readonly Dictionary<string, int[]> conStars = new Dictionary<string, int[]>();

    private const double MIN_ALT = -12, MAX_ALT = 78, MIN_FOV = 8, MAX_FOV = 64;

    // ---------------------------------------------------------------- the view

    public bool isOpen { get; private set; }
    /// <summary>where the eyepiece points: degrees round from north and up from the horizon, and how many degrees it shows across</summary>
    public double viewAz = 30, viewAlt = 34, fov = 46;
    /// <summary>0 by day, 1 at night: how bright the stars are</summary>
    public double night = 1;

    private readonly HashSet<string> found = new HashSet<string>();
    private readonly Dictionary<string, HashSet<int>> lit = new Dictionary<string, HashSet<int>>();
    /// <summary>seconds since each constellation was found in this look (for drawing its lines in)</summary>
    private readonly Dictionary<string, double> foundAge = new Dictionary<string, double>();
    /// <summary>a star just clicked, for a small ripple: its index and how long ago</summary>
    private int pulseStar = -1;
    private double pulseT = 9;
    private string focusId = null;
    private double focus = 0;
    private string hintId = null;
    private double hintT = 0;
    private double time = 0;
    private double cardT = 0;
    private double msgT = 0;

    /// <summary>a constellation has just been found; the game pays out and writes it into the journal</summary>
    public Action<ConstellationDef> onFound;
    /// <summary>puts the view's page on the screen (under the toasts and the fade)</summary>
    public Action<HTMLElement> mount;
    /// <summary>a small sound: "light" a star of a pattern lit, "field" a star on its own, "found", "hint"</summary>
    public Action<string> onSound;

    // ---------------------------------------------------------------- the page

    private HTMLElement wrap, countEl, msgEl, card, cardName, cardLore, cardReward;
    private VisualElement layer;
    private readonly Dictionary<string, HTMLElement> labels = new Dictionary<string, HTMLElement>();
    private static Texture2D softTex, ringTex, cloudTex;

    // the mouse: pressed where, and whether it has moved far enough to be a drag
    private bool mouseDown = false, dragging = false;
    private Vector2 downAt, lastAt;

    public TelescopeView()
    {
        buildSky();
    }

    private static Func<double> rng(double seed) => () =>
    {
        seed = (seed * 1664525 + 1013904223) % 4294967296;
        return seed / 4294967296;
    };

    private static double wrap180(double a)
    {
        a %= 360;
        if (a > 180) a -= 360;
        if (a < -180) a += 360;
        return a;
    }

    private static Color32 starTint(double r)
    {
        if (r < 0.55) return new Color32(255, 255, 255, 255);
        if (r < 0.75) return new Color32(205, 222, 255, 255);
        if (r < 0.9) return new Color32(255, 240, 205, 255);
        return new Color32(255, 212, 175, 255);
    }

    private void buildSky()
    {
        var rand = rng(20261116);
        // the constellations first, so the field can keep clear of their stars
        foreach (var c in Constellations.ALL)
        {
            var idx = new int[c.stars.Length];
            for (int i = 0; i < c.stars.Length; i++)
            {
                var s = c.stars[i];
                idx[i] = stars.Count;
                stars.Add(new Star
                {
                    az = (c.az + s[0] + 360) % 360,
                    alt = c.alt + s[1],
                    b = s[2],
                    phase = rand() * 6.3,
                    speed = 1.2 + rand() * 1.8,
                    tint = c.id == "scorpion" && i == 4 ? new Color32(255, 170, 130, 255)
                        : c.id == "orion" && i == 0 ? new Color32(255, 186, 140, 255)
                        : c.id == "orion" && i == 6 ? new Color32(196, 214, 255, 255)
                        : starTint(rand()),
                    con = c.id,
                    idx = i,
                });
            }
            conStars[c.id] = idx;
        }
        int conCount = stars.Count;
        // the field: mostly faint, a few bright, a little thicker along the Milky Way
        for (int n = 0; n < 5200; n++)
        {
            double az = rand() * 360;
            double alt = MIN_ALT + 2 + rand() * (MAX_ALT + 8 - MIN_ALT);
            double band = Math.Exp(-Math.Pow((alt - milkyAlt(az)) / 9, 2));
            if (rand() > 0.55 + 0.45 * band) continue;
            double b = Math.Pow(rand(), 3.2);
            bool clash = false;
            foreach (var c in Constellations.ALL)
            {
                double dx = wrap180(az - c.az), dy = alt - c.alt;
                if (Math.Abs(dx) > c.extent + 3 || Math.Abs(dy) > c.extent + 3) continue;
                // inside a pattern the field stays faint, so its own stars stand out a little
                if (Math.Sqrt(dx * dx + dy * dy) < c.extent + 2) b = Math.Min(b, 0.42);
                for (int i = 0; i < conCount; i++)
                {
                    if (stars[i].con != c.id) continue;
                    double ex = wrap180(az - stars[i].az), ey = alt - stars[i].alt;
                    if (ex * ex + ey * ey < 2.2) { clash = true; break; }
                }
                if (clash) break;
            }
            if (clash) continue;
            stars.Add(new Star { az = az, alt = alt, b = b, phase = rand() * 6.3, speed = 0.8 + rand() * 2.4, tint = starTint(rand()) });
        }
        // the Milky Way: soft clouds along a band across the sky
        for (int n = 0; n < 900; n++)
        {
            double az = rand() * 360;
            double alt = milkyAlt(az) + (rand() + rand() - 1) * 9;
            milky.Add((az, alt, 5 + rand() * 8, 0.004 + rand() * 0.008));
        }
    }

    private static double milkyAlt(double az) => 36 + 26 * Math.Sin((az + 40) * Math.PI / 180);

    // ---------------------------------------------------------------- open and close

    public void show(IEnumerable<string> already, double night)
    {
        this.night = night;
        found.Clear();
        foreach (var id in already) found.Add(id);
        foundAge.Clear();
        foreach (var id in found) foundAge[id] = 99;
        lit.Clear();
        focus = 0;
        focusId = null;
        hintId = null;
        hintT = 0;
        cardT = 0;
        msgT = 0;
        mouseDown = dragging = false;
        if (wrap != null) return;
        isOpen = true;
        ensureTextures();
        wrap = document.createElement("div");
        wrap.className = "glass";
        layer = new VisualElement { pickingMode = PickingMode.Ignore };
        layer.style.position = Position.Absolute;
        layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
        layer.generateVisualContent += draw;
        wrap.ve.Add(layer);
        countEl = document.createElement("div");
        countEl.className = "glass-count";
        msgEl = document.createElement("div");
        msgEl.className = "glass-msg";
        card = document.createElement("div");
        card.className = "glass-card hidden";
        cardName = document.createElement("h2");
        cardLore = document.createElement("p");
        cardReward = document.createElement("div");
        cardReward.className = "glass-reward";
        card.append(cardName, cardLore, cardReward);
        wrap.append(countEl, msgEl, card);
        if (mount != null) mount(wrap);
        else document.body.append(wrap);
        updateCount();
        message(found.Count == 0
            ? "A sky full of stars. Somewhere in it are pictures: click their stars, or hold one in the middle of the eyepiece."
            : found.Count >= Constellations.ALL.Length ? "Every constellation is found. The sky is yours to wander." : "Back at the eyepiece. The stars are where you left them.", 7);
    }

    public void hide()
    {
        isOpen = false;
        if (wrap == null) return;
        layer.generateVisualContent -= draw;
        wrap.remove();
        wrap = null;
        layer = null;
        labels.Clear();
    }

    private void updateCount()
    {
        if (countEl != null) countEl.textContent = $"🔭  Constellations found: {found.Count} of {Constellations.ALL.Length}";
    }

    private void message(string text, double seconds)
    {
        if (msgEl == null) return;
        msgEl.textContent = text;
        msgEl.classList.remove("hidden");
        msgT = seconds;
    }

    // ---------------------------------------------------------------- each frame

    public class Controls
    {
        /// <summary>pan, -1..1 each way (right and up are positive)</summary>
        public double panX, panY;
        /// <summary>zoom, -1..1 (positive goes in)</summary>
        public double zoom;
        /// <summary>pick the star in the middle of the eyepiece</summary>
        public bool select;
        /// <summary>ask for a hint</summary>
        public bool hint;
        /// <summary>the mouse may pan, zoom and pick (not while the controller is in use)</summary>
        public bool mouse = true;
    }

    public void update(double dt, Controls c)
    {
        if (!isOpen || layer == null) return;
        time += dt;
        pulseT += dt;
        var keys = foundAge.Keys.ToList();
        foreach (var k in keys) foundAge[k] += dt;

        double ppd = radius() * 2 / fov;
        // the keyboard and the sticks: a steady pan, quicker when zoomed out
        viewAz += c.panX * fov * 0.55 * dt;
        viewAlt += c.panY * fov * 0.55 * dt;
        if (c.zoom != 0) fov *= Math.Exp(-c.zoom * dt * 1.3);

        if (c.mouse) mouseInput(ppd);

        fov = Math.Max(MIN_FOV, Math.Min(MAX_FOV, fov));
        viewAz = (viewAz % 360 + 360) % 360;
        viewAlt = Math.Max(MIN_ALT + fov * 0.3, Math.Min(MAX_ALT - fov * 0.3, viewAlt));

        if (c.select) pick(new Vector2((float)cx(), (float)cy()), 30);
        if (c.hint) giveHint();

        updateFocus(dt);

        if (hintT > 0) hintT -= dt;
        if (cardT > 0)
        {
            cardT -= dt;
            if (cardT <= 0) card.classList.add("hidden");
        }
        if (msgT > 0)
        {
            msgT -= dt;
            if (msgT <= 0) msgEl.classList.add("hidden");
        }
        placeLabels();
        layer.MarkDirtyRepaint();
    }

    private void mouseInput(double ppd)
    {
        var m = Mouse.current;
        if (m == null || layer.panel == null) return;
        var sp = m.position.ReadValue();
        var p = layer.WorldToLocal(RuntimePanelUtils.ScreenToPanel(layer.panel, new Vector2(sp.x, Screen.height - sp.y)));
        double wheel = m.scroll.ReadValue().y;
        if (wheel != 0) fov *= Math.Exp(-Math.Sign(wheel) * 0.12);
        if (m.leftButton.wasPressedThisFrame)
        {
            mouseDown = true;
            dragging = false;
            downAt = lastAt = p;
        }
        else if (mouseDown && m.leftButton.isPressed)
        {
            if (!dragging && (p - downAt).magnitude > 5) dragging = true;
            if (dragging)
            {
                var d = p - lastAt;
                viewAz -= d.x / ppd;
                viewAlt += d.y / ppd;
            }
            lastAt = p;
        }
        if (mouseDown && m.leftButton.wasReleasedThisFrame)
        {
            mouseDown = false;
            if (!dragging) pick(p, 22);
            dragging = false;
        }
    }

    // ---------------------------------------------------------------- the sky on the screen

    private double radius() => layer == null ? 400 : Math.Min(layer.contentRect.width, layer.contentRect.height) * 0.41;
    private double cx() => layer == null ? 960 : layer.contentRect.width / 2;
    private double cy() => layer == null ? 540 : layer.contentRect.height / 2 + 8;

    private Vector2 toScreen(double az, double alt)
    {
        double ppd = radius() * 2 / fov;
        return new Vector2((float)(cx() + wrap180(az - viewAz) * ppd), (float)(cy() - (alt - viewAlt) * ppd));
    }

    private bool inEye(Vector2 p, double margin = 0)
    {
        double dx = p.x - cx(), dy = p.y - cy();
        return dx * dx + dy * dy < (radius() - margin) * (radius() - margin);
    }

    // ---------------------------------------------------------------- finding

    /// <summary>A press at a point of the eyepiece: the nearest star within reach lights, if it belongs to a pattern not yet found.</summary>
    private void pick(Vector2 at, double reach)
    {
        if (!inEye(at)) return;
        int best = -1;
        double bestD = reach * reach;
        for (int i = 0; i < stars.Count; i++)
        {
            var s = stars[i];
            // faint field stars are not worth aiming at; the bright ones and the patterns' stars are
            if (s.con == null && s.b < 0.25) continue;
            var p = toScreen(s.az, s.alt);
            double dx = p.x - at.x, dy = p.y - at.y;
            // a pattern's star wins over a field star at nearly the same distance
            double d = dx * dx + dy * dy - (s.con != null ? 40 : 0);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        if (best < 0) return;
        var st = stars[best];
        pulseStar = best;
        pulseT = 0;
        if (st.con == null)
        {
            onSound?.Invoke("field");
            if (lit.Count == 0) message("Just a star on its own. The pictures are made of brighter ones.", 4);
            return;
        }
        if (found.Contains(st.con))
        {
            onSound?.Invoke("field");
            return;
        }
        if (!lit.TryGetValue(st.con, out var set)) lit[st.con] = set = new HashSet<int>();
        if (!set.Add(st.idx)) return;
        var def = Constellations.BY_ID[st.con];
        onSound?.Invoke("light");
        if (set.Count >= def.stars.Length)
        {
            markFound(def);
            return;
        }
        if (set.Count == 1) message("That star is part of something bigger. Find its neighbours.", 5);
        else message($"{set.Count} of its {def.stars.Length} stars.", 3);
    }

    private void markFound(ConstellationDef def)
    {
        if (found.Contains(def.id)) return;
        found.Add(def.id);
        foundAge[def.id] = 0;
        lit.Remove(def.id);
        if (focusId == def.id)
        {
            focusId = null;
            focus = 0;
        }
        if (hintId == def.id) hintT = 0;
        onSound?.Invoke("found");
        onFound?.Invoke(def);
        cardName.textContent = "✨ " + def.name;
        cardLore.textContent = def.lore;
        bool all = found.Count >= Constellations.ALL.Length;
        cardReward.textContent = all
            ? $"+{Constellations.REWARD} coins, and {Constellations.ALL_BONUS} more for the whole sky"
            : $"+{Constellations.REWARD} coins  ·  {found.Count} of {Constellations.ALL.Length} found";
        card.classList.remove("hidden");
        cardT = 11;
        updateCount();
        if (all) message("Every constellation in the sky is found. ⭐", 8);
        else msgEl?.classList.add("hidden");
    }

    /// <summary>Holding a pattern framed in the middle of the eyepiece slowly brings it into focus.</summary>
    private void updateFocus(double dt)
    {
        string best = null;
        double bestD = double.MaxValue;
        foreach (var c in Constellations.ALL)
        {
            if (found.Contains(c.id)) continue;
            double dx = wrap180(c.az - viewAz), dy = c.alt - viewAlt;
            double d = Math.Sqrt(dx * dx + dy * dy);
            bool centred = d < Math.Max(2.5, c.extent * 0.38);
            bool framed = fov >= c.extent * 2.05 && fov <= Math.Max(c.extent * 4.4, 22);
            if (centred && framed && d < bestD)
            {
                bestD = d;
                best = c.id;
            }
        }
        if (best != null && best == focusId)
        {
            focus += dt / 2.6;
            if (focus >= 1)
            {
                markFound(Constellations.BY_ID[best]);
                return;
            }
        }
        else if (best != null && focus <= 0.02)
        {
            focusId = best;
            focus = 0.02;
        }
        else
        {
            focus = Math.Max(0, focus - dt * 0.7);
            if (focus <= 0) focusId = null;
        }
    }

    private void giveHint()
    {
        ConstellationDef best = null;
        double bestD = double.MaxValue;
        foreach (var c in Constellations.ALL)
        {
            if (found.Contains(c.id)) continue;
            double dx = wrap180(c.az - viewAz), dy = c.alt - viewAlt;
            double d = dx * dx + dy * dy;
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }
        if (best == null)
        {
            message("There is nothing left to find. Every constellation is in the journal.", 4);
            return;
        }
        hintId = best.id;
        hintT = 9;
        onSound?.Invoke("hint");
        message($"A soft glow marks where {best.name.Replace("The ", "the ")} hides. Follow the arrow, then frame it or click its stars.", 7);
    }

    // ---------------------------------------------------------------- the words over the sky

    private void placeLabels()
    {
        foreach (var id in found)
        {
            var c = Constellations.BY_ID[id];
            if (!labels.TryGetValue(id, out var lab))
            {
                lab = document.createElement("div");
                lab.className = "glass-label";
                lab.textContent = c.name;
                wrap.append(lab);
                labels[id] = lab;
            }
            double lowest = c.stars.Min(s => s[1]);
            var p = toScreen(c.az, c.alt + lowest - 2.2);
            double fade = Math.Min(1, (foundAge.TryGetValue(id, out var age) ? age : 99) / 1.8);
            bool show = inEye(p, 40) && fade > 0.05;
            lab.classList.toggle("hidden", !show);
            if (!show) continue;
            lab.style.left = $"{p.x - 160:0}px";
            lab.style.top = $"{p.y:0}px";
            lab.style.opacity = (fade * 0.9).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // ---------------------------------------------------------------- drawing

    private static void ensureTextures()
    {
        if (softTex != null) return;
        softTex = makeTex(r => Math.Max(Math.Exp(-Math.Pow(r / 0.16, 2)), 0.32 * Math.Exp(-Math.Pow(r / 0.45, 2))) * Math.Max(0, 1 - r * r));
        ringTex = makeTex(r => Math.Exp(-Math.Pow((r - 0.8) / 0.075, 2)));
        // the Milky Way's clouds: broad and soft all through, with no bright middle, so many of them blend into one haze
        cloudTex = makeTex(r => Math.Exp(-Math.Pow(r / 0.55, 2)) * Math.Max(0, 1 - r * r));
    }

    private static Texture2D makeTex(Func<double, double> alpha)
    {
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                double dx = (x + 0.5) / N * 2 - 1, dy = (y + 0.5) / N * 2 - 1;
                double a = Math.Max(0, Math.Min(1, alpha(Math.Sqrt(dx * dx + dy * dy))));
                px[y * N + x] = new Color32(255, 255, 255, (byte)Math.Round(a * 255));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }

    /// <summary>Gathers quads that share a texture and hands them to UI Toolkit in one go.</summary>
    private class Batch
    {
        public readonly List<Vertex> v = new List<Vertex>();
        public readonly List<ushort> i = new List<ushort>();
        public Texture tex;
        public MeshGenerationContext mgc;

        public void use(Texture t)
        {
            if (t != tex) flush();
            tex = t;
        }

        public void flush()
        {
            if (v.Count == 0) return;
            var md = mgc.Allocate(v.Count, i.Count, tex);
            md.SetAllVertices(v.ToArray());
            md.SetAllIndices(i.ToArray());
            v.Clear();
            i.Clear();
        }

        private static Vertex V(Vector2 p, Color32 c, Vector2 uv) => new Vertex { position = new Vector3(p.x, p.y, Vertex.nearZ), tint = c, uv = uv };

        /// <summary>A quad from four corners in order round it; the winding is put right whichever way round they come.</summary>
        public void quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 ca, Color32 cb, Color32 cc, Color32 cd)
        {
            if (v.Count > 60000) flush();
            int o = v.Count;
            v.Add(V(a, ca, new Vector2(0, 0)));
            v.Add(V(b, cb, new Vector2(1, 0)));
            v.Add(V(c, cc, new Vector2(1, 1)));
            v.Add(V(d, cd, new Vector2(0, 1)));
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (cross > 0)
            {
                i.Add((ushort)o); i.Add((ushort)(o + 1)); i.Add((ushort)(o + 2));
                i.Add((ushort)o); i.Add((ushort)(o + 2)); i.Add((ushort)(o + 3));
            }
            else
            {
                i.Add((ushort)o); i.Add((ushort)(o + 2)); i.Add((ushort)(o + 1));
                i.Add((ushort)o); i.Add((ushort)(o + 3)); i.Add((ushort)(o + 2));
            }
        }

        public void quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col) => quad(a, b, c, d, col, col, col, col);

        /// <summary>A soft dot (or ring) of this radius.</summary>
        public void dot(Vector2 p, float r, Color32 c) =>
            quad(new Vector2(p.x - r, p.y - r), new Vector2(p.x + r, p.y - r), new Vector2(p.x + r, p.y + r), new Vector2(p.x - r, p.y + r), c);

        /// <summary>A soft line: the texture's middle row runs across it, so it fades out at both sides.</summary>
        public void line(Vector2 a, Vector2 b, float w, Color32 c)
        {
            var d = b - a;
            if (d.sqrMagnitude < 0.01f) return;
            var n = new Vector2(-d.y, d.x).normalized * w;
            if (v.Count > 60000) flush();
            int o = v.Count;
            v.Add(V(a + n, c, new Vector2(0.5f, 0)));
            v.Add(V(b + n, c, new Vector2(0.5f, 0)));
            v.Add(V(b - n, c, new Vector2(0.5f, 1)));
            v.Add(V(a - n, c, new Vector2(0.5f, 1)));
            float cross = (v[o + 1].position.x - v[o].position.x) * (v[o + 2].position.y - v[o].position.y) - (v[o + 1].position.y - v[o].position.y) * (v[o + 2].position.x - v[o].position.x);
            if (cross > 0)
            {
                i.Add((ushort)o); i.Add((ushort)(o + 1)); i.Add((ushort)(o + 2));
                i.Add((ushort)o); i.Add((ushort)(o + 2)); i.Add((ushort)(o + 3));
            }
            else
            {
                i.Add((ushort)o); i.Add((ushort)(o + 2)); i.Add((ushort)(o + 1));
                i.Add((ushort)o); i.Add((ushort)(o + 3)); i.Add((ushort)(o + 2));
            }
        }

        /// <summary>A ring between two radii, each edge with its own colour (for fades and the brass rim).</summary>
        public void annulus(Vector2 ctr, float r0, float r1, Color32 c0, Color32 c1, int seg = 120, float from = 0, float to = Mathf.PI * 2)
        {
            for (int k = 0; k < seg; k++)
            {
                float a0 = from + (to - from) * k / seg, a1 = from + (to - from) * (k + 1) / seg;
                var u0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var u1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                quad(ctr + u0 * r0, ctr + u1 * r0, ctr + u1 * r1, ctr + u0 * r1, c0, c0, c1, c1);
            }
        }
    }

    private static Color32 Col(double r, double g, double b, double a) =>
        new Color32((byte)Math.Round(Clamp01(r) * 255), (byte)Math.Round(Clamp01(g) * 255), (byte)Math.Round(Clamp01(b) * 255), (byte)Math.Round(Clamp01(a) * 255));

    private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

    private static Color32 Lerp(Color32 a, Color32 b, double t) => Color32.Lerp(a, b, (float)Clamp01(t));

    private static Color32 Alpha(Color32 c, double a) => new Color32(c.r, c.g, c.b, (byte)Math.Round(Clamp01(a) * 255));

    private void draw(MeshGenerationContext mgc)
    {
        if (layer == null) return;
        var rect = layer.contentRect;
        if (rect.width < 10 || rect.height < 10) return;
        var B = new Batch { mgc = mgc };
        float R = (float)radius();
        var ctr = new Vector2((float)cx(), (float)cy());
        double ppd = R * 2 / fov;
        double nt = Clamp01(night);
        // how many degrees of sky are in the eyepiece each way, with a margin, for leaving out what cannot be seen
        double halfDeg = fov / 2 + 3;

        // the sky: deep blue by day, nearly black at night, lighter toward the horizon
        B.use(null);
        var topDay = new Color32(30, 52, 92, 255);
        var lowDay = new Color32(62, 92, 136, 255);
        var topNight = new Color32(4, 7, 18, 255);
        var lowNight = new Color32(16, 26, 48, 255);
        Color32 skyAt(double alt)
        {
            double t = Clamp01(alt / 50);
            var top = Lerp(topDay, topNight, nt);
            var low = Lerp(lowDay, lowNight, nt);
            return Lerp(low, top, Math.Sqrt(t));
        }
        const int ROWS = 28;
        for (int k = 0; k < ROWS; k++)
        {
            float y0 = ctr.y - R + 2 * R * k / ROWS, y1 = ctr.y - R + 2 * R * (k + 1) / ROWS;
            double alt0 = viewAlt + (ctr.y - y0) / ppd, alt1 = viewAlt + (ctr.y - y1) / ppd;
            var c0 = skyAt(alt0);
            var c1 = skyAt(alt1);
            B.quad(new Vector2(ctr.x - R, y0), new Vector2(ctr.x + R, y0), new Vector2(ctr.x + R, y1), new Vector2(ctr.x - R, y1), c0, c0, c1, c1);
        }

        // the Milky Way
        B.use(cloudTex);
        foreach (var m in milky)
        {
            double dx = wrap180(m.az - viewAz), dy = m.alt - viewAlt;
            if (Math.Abs(dx) > halfDeg + m.r || Math.Abs(dy) > halfDeg + m.r) continue;
            var p = toScreen(m.az, m.alt);
            B.dot(p, (float)(m.r * ppd), Col(0.78, 0.76, 0.95, m.a * (0.35 + 0.65 * nt)));
        }

        // a hinted constellation glows faintly where it hides
        if (hintId != null && hintT > 0)
        {
            var h = Constellations.BY_ID[hintId];
            double fade = Math.Min(1, hintT / 1.5) * Math.Min(1, (9 - hintT) / 0.8);
            var p = toScreen(h.az, h.alt);
            double pulse = 0.85 + 0.15 * Math.Sin(time * 2.2);
            B.dot(p, (float)((h.extent + 5) * ppd * 1.25), Col(1.0, 0.86, 0.55, 0.13 * fade * pulse));
        }

        // the lines: found patterns drawn in with a glow, partly lit ones faintly between their lit stars
        foreach (var c in Constellations.ALL)
        {
            double dx = wrap180(c.az - viewAz), dy = c.alt - viewAlt;
            if (Math.Abs(dx) > halfDeg + c.extent || Math.Abs(dy) > halfDeg + c.extent) continue;
            var idx = conStars[c.id];
            if (found.Contains(c.id))
            {
                double age = foundAge.TryGetValue(c.id, out var a) ? a : 99;
                for (int k = 0; k < c.lines.Length; k++)
                {
                    // each line grows from its first star to its second, one after another
                    double grow = Clamp01((age - k * 0.16) / 0.5);
                    if (grow <= 0) continue;
                    var s0 = stars[idx[c.lines[k][0]]];
                    var s1 = stars[idx[c.lines[k][1]]];
                    var p0 = toScreen(s0.az, s0.alt);
                    var p1 = toScreen(s1.az, s1.alt);
                    var pe = Vector2.Lerp(p0, p1, (float)grow);
                    double flare = 1 + 1.4 * Math.Exp(-age * 1.2);
                    B.line(p0, pe, (float)(9 * flare), Col(1.0, 0.82, 0.48, 0.16 * flare));
                    B.line(p0, pe, 2.6f, Col(1.0, 0.92, 0.72, 0.85));
                }
            }
            else if (lit.TryGetValue(c.id, out var set) && set.Count > 1)
            {
                foreach (var ln in c.lines)
                {
                    if (!set.Contains(ln[0]) || !set.Contains(ln[1])) continue;
                    var s0 = stars[idx[ln[0]]];
                    var s1 = stars[idx[ln[1]]];
                    B.line(toScreen(s0.az, s0.alt), toScreen(s1.az, s1.alt), 2.2f, Col(0.75, 0.85, 1.0, 0.45));
                }
            }
        }

        // the stars
        double zoomGrow = Math.Pow(46 / fov, 0.35);
        for (int i = 0; i < stars.Count; i++)
        {
            var s = stars[i];
            double dx = wrap180(s.az - viewAz), dy = s.alt - viewAlt;
            if (Math.Abs(dx) > halfDeg || Math.Abs(dy) > halfDeg) continue;
            if (s.alt < -0.4) continue; // under the sea
            double tw = 0.82 + 0.18 * Math.Sin(time * s.speed + s.phase);
            double b = s.b;
            bool mine = s.con != null && found.Contains(s.con);
            bool lights = s.con != null && lit.TryGetValue(s.con, out var set) && set.Contains(s.idx);
            if (mine || lights) b = Math.Min(1, b + 0.15);
            // faint stars wash out in daylight; the haze low over the sea dims them all a little
            double vis = (0.45 + 0.55 * nt) * (b < 0.3 ? 0.35 + 0.65 * nt : 1) * (0.55 + 0.45 * Clamp01(s.alt / 10));
            double size = (2.2 + b * b * 9) * zoomGrow;
            var p = toScreen(s.az, s.alt);
            B.dot(p, (float)size, Alpha(s.tint, Clamp01((0.25 + 0.75 * b) * tw * vis)));
        }

        // rings round the lit stars, and a ripple round the one just clicked
        B.use(ringTex);
        foreach (var kv in lit)
        {
            var idx = conStars[kv.Key];
            foreach (var k in kv.Value)
            {
                var s = stars[idx[k]];
                B.dot(toScreen(s.az, s.alt), (float)(13 * zoomGrow), Col(0.75, 0.88, 1.0, 0.75));
            }
        }
        if (pulseStar >= 0 && pulseT < 0.9)
        {
            var s = stars[pulseStar];
            double t = pulseT / 0.9;
            B.dot(toScreen(s.az, s.alt), (float)((10 + 26 * t) * zoomGrow), Col(1, 1, 1, 0.6 * (1 - t)));
        }

        // the sea below the horizon, with a pale line where it meets the sky
        B.use(null);
        double horizonY = ctr.y + viewAlt * ppd;
        if (horizonY < ctr.y + R)
        {
            float hy = (float)Math.Max(ctr.y - R, horizonY);
            var seaTop = Lerp(new Color32(40, 66, 98, 255), new Color32(10, 18, 32, 255), nt);
            var seaLow = Lerp(new Color32(20, 38, 60, 255), new Color32(3, 7, 14, 255), nt);
            B.quad(new Vector2(ctr.x - R, hy), new Vector2(ctr.x + R, hy), new Vector2(ctr.x + R, ctr.y + R), new Vector2(ctr.x - R, ctr.y + R), seaTop, seaTop, seaLow, seaLow);
            B.use(softTex);
            if (horizonY > ctr.y - R)
                B.line(new Vector2(ctr.x - R, (float)horizonY), new Vector2(ctr.x + R, (float)horizonY), 5, Col(0.7, 0.78, 0.9, 0.22 + 0.1 * (1 - nt)));
        }

        // the focus: a thin arc filling round the middle while a pattern is held framed there
        B.use(null);
        var retC = Col(0.95, 0.92, 0.82, 0.32);
        float gap = 9, arm = 20;
        B.quad(new Vector2(ctr.x - gap - arm, ctr.y - 0.7f), new Vector2(ctr.x - gap, ctr.y - 0.7f), new Vector2(ctr.x - gap, ctr.y + 0.7f), new Vector2(ctr.x - gap - arm, ctr.y + 0.7f), retC);
        B.quad(new Vector2(ctr.x + gap, ctr.y - 0.7f), new Vector2(ctr.x + gap + arm, ctr.y - 0.7f), new Vector2(ctr.x + gap + arm, ctr.y + 0.7f), new Vector2(ctr.x + gap, ctr.y + 0.7f), retC);
        B.quad(new Vector2(ctr.x - 0.7f, ctr.y - gap - arm), new Vector2(ctr.x + 0.7f, ctr.y - gap - arm), new Vector2(ctr.x + 0.7f, ctr.y - gap), new Vector2(ctr.x - 0.7f, ctr.y - gap), retC);
        B.quad(new Vector2(ctr.x - 0.7f, ctr.y + gap), new Vector2(ctr.x + 0.7f, ctr.y + gap), new Vector2(ctr.x + 0.7f, ctr.y + gap + arm), new Vector2(ctr.x - 0.7f, ctr.y + gap + arm), retC);
        if (focus > 0.02 && focusId != null)
        {
            var fc = Col(1.0, 0.86, 0.55, 0.75);
            B.annulus(ctr, 34, 37, Col(1, 1, 1, 0.12), Col(1, 1, 1, 0.12), 64);
            B.annulus(ctr, 33.5f, 37.5f, fc, fc, 64, -Mathf.PI / 2, -Mathf.PI / 2 + Mathf.PI * 2 * (float)focus);
        }

        // the eyepiece: a soft darkening inside the edge, black all round outside, and a brass rim
        var black = new Color32(0, 0, 0, 255);
        var clear = new Color32(0, 0, 0, 0);
        B.annulus(ctr, R * 0.82f, R, clear, new Color32(0, 0, 0, 200));
        float far = Math.Max(rect.width, rect.height) * 1.5f;
        B.annulus(ctr, R - 0.5f, far, black, black, 120);
        B.annulus(ctr, R, R + 10, new Color32(120, 92, 46, 255), new Color32(196, 158, 88, 255));
        B.annulus(ctr, R + 10, R + 18, new Color32(196, 158, 88, 255), new Color32(70, 52, 26, 255));
        B.annulus(ctr, R + 18, R + 22, new Color32(40, 30, 16, 255), new Color32(0, 0, 0, 255));

        // the arrow at the rim toward a hint that is out of sight
        if (hintId != null && hintT > 0)
        {
            var h = Constellations.BY_ID[hintId];
            var p = toScreen(h.az, h.alt);
            var d = p - ctr;
            if (d.magnitude > R * 0.7f)
            {
                var u = d.normalized;
                var n = new Vector2(-u.y, u.x);
                var tip = ctr + u * (R - 14);
                var bse = ctr + u * (R - 40);
                double fade = Math.Min(1, hintT / 1.5) * (0.75 + 0.25 * Math.Sin(time * 4));
                var ac = Col(1.0, 0.86, 0.55, 0.85 * fade);
                B.quad(tip, bse + n * 14, bse, bse - n * 14, ac);
            }
        }
        B.flush();
    }

    // ---------------------------------------------------------------- for the smoke test

    /// <summary>Points the eyepiece straight at a constellation, framed.</summary>
    public void lookAt(string id)
    {
        var c = Constellations.BY_ID[id];
        viewAz = c.az;
        viewAlt = c.alt;
        fov = Math.Max(MIN_FOV, Math.Min(MAX_FOV, c.extent * 2.9));
    }

    /// <summary>Clicks the stars of a constellation one after another, as the player would.</summary>
    public void clickAll(string id, int upTo = 99)
    {
        var idx = conStars[id];
        for (int k = 0; k < Math.Min(upTo, idx.Length); k++)
        {
            var s = stars[idx[k]];
            pick(toScreen(s.az, s.alt), 22);
        }
    }

    public string describe() =>
        $"view az {viewAz:0.0} alt {viewAlt:0.0} fov {fov:0.0}, night {night:0.00}, found {found.Count}/{Constellations.ALL.Length} [{string.Join(",", found)}], focus {focusId ?? "-"} {focus:0.00}, lit {string.Join(",", lit.Select(kv => kv.Key + ":" + kv.Value.Count))}";
}
