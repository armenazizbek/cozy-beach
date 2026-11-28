using System;
using System.Collections.Generic;

// Port of src/mechanics/DetectorTool.ts

public class Signal
{
    public double strength;
    /// <summary>'trash' | 'coin' | 'deep' | 'generic' or null</summary>
    public string kind;
}

public class DetectorTool
{
    public readonly THREE.Group group = new THREE.Group();
    public readonly Signal signal = new Signal { strength = 0, kind = null };
    /// <summary>0..1 short envelope peaking on each detector beep, used to give controller vibration a crisp pulse.</summary>
    public double pingPulse { get; private set; }
    private readonly THREE.Mesh coil, pulse, edge;
    /// <summary>a dark band under each bright ring, so the rings stand out on pale sand as well as dark</summary>
    private readonly THREE.Mesh pulseDark, edgeDark;
    private readonly THREE.Mesh heat;
    private readonly TexturePainter heatCanvas = new TexturePainter(128, 128);
    private readonly THREE.CanvasTexture heatTex;
    private double pingTimer = 0, pulseT = 0, radius = 2;

    public DetectorTool()
    {
        var dark = new THREE.MeshStandardMaterial { color = 0x3a3f45, roughness = 0.5, metalness = 0.6 };
        var brass = new THREE.MeshStandardMaterial { color = 0xb08a4a, roughness = 0.4, metalness = 0.8 };

        coil = new THREE.Mesh(new THREE.CylinderGeometry(0.5, 0.5, 0.07, 28), dark) { castShadow = true };
        var hub = new THREE.Mesh(new THREE.TorusGeometry(0.5, 0.04, 8, 28), brass);
        hub.rotation.x = Math.PI / 2;
        coil.add(hub);
        var shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 2.6, 8), brass);
        shaft.position.set(0, 1.2, -1.0);
        shaft.rotation.x = 0.7;
        shaft.castShadow = true;
        var grip = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.07, 0.5, 10), new THREE.MeshStandardMaterial { color = 0x3b2a1e, roughness = 0.9 });
        grip.position.set(0, 2.3, -1.9);
        grip.rotation.x = 0.7;
        var arm = new THREE.Mesh(new THREE.TorusGeometry(0.22, 0.03, 6, 16, Math.PI), brass);
        arm.position.set(0, 2.1, -1.65);
        arm.rotation.set(0, 0, 0);
        group.add(coil, shaft, grip, arm);

        pulse = new THREE.Mesh(new THREE.RingGeometry(0.93, 1, 64).rotateX(-Math.PI / 2),
            new THREE.MeshBasicMaterial { color = 0x9be37a, transparent = true, opacity = 0.7, side = THREE.Side.DoubleSide, depthWrite = false });
        edge = new THREE.Mesh(new THREE.RingGeometry(0.98, 1, 64).rotateX(-Math.PI / 2),
            new THREE.MeshBasicMaterial { color = 0xffffff, transparent = true, opacity = 0.8, side = THREE.Side.DoubleSide, depthWrite = false });
        THREE.Mesh darkRing(double inner, double outer, double opacity)
        {
            var m = new THREE.Mesh(new THREE.RingGeometry(inner, outer, 64).rotateX(-Math.PI / 2),
                new THREE.MeshBasicMaterial { color = 0x2b1a0c, transparent = true, opacity = opacity, side = THREE.Side.DoubleSide, depthWrite = false });
            m.renderOrder = 4;
            return m;
        }
        pulseDark = darkRing(0.925, 1.02, 0.3);
        edgeDark = darkRing(0.972, 1.006, 0.4);
        heatTex = new THREE.CanvasTexture(heatCanvas);
        heat = new THREE.Mesh(new THREE.RingGeometry(0.0001, 1, 48, 10).rotateX(-Math.PI / 2),
            new THREE.MeshBasicMaterial { map = heatTex, transparent = true, depthWrite = false, side = THREE.Side.DoubleSide, opacity = 0.9 });
        heat.renderOrder = 4;
        pulse.renderOrder = edge.renderOrder = 5;
    }

    /// <summary>The ground-hugging overlay objects live in the world, not the wand group.</summary>
    public THREE.Object3D[] overlays => new THREE.Object3D[] { pulse, edge, heat, pulseDark, edgeDark };

    public void setActive(bool on)
    {
        group.visible = on;
        foreach (var o in overlays) o.visible = on;
        if (!on)
        {
            signal.strength = 0;
            signal.kind = null;
            pingPulse = 0;
        }
    }

    /// <summary>
    /// Dual-motor controller vibration (0..1 low and high frequency) for the current detector reading.
    /// Zero with no find in range; rises smoothly as the coil nears the buried find, with a short pulse on each beep.
    /// Sensitivity below 1 scales strength down; above 1 raises sensitivity so distant finds are felt sooner and stronger
    /// while still climbing cleanly right up to the target.
    /// </summary>
    public static (float low, float high) ComputeVibration(double strength, double pulse, string kind, double sensitivity)
    {
        if (double.IsNaN(strength) || double.IsNaN(sensitivity) || strength <= 0 || sensitivity <= 0) return (0f, 0f);
        double s = Math.Min(1, Math.Max(0, strength));
        double p = double.IsNaN(pulse) ? 0 : Math.Min(1, Math.Max(0, pulse));
        double sens = Math.Min(GameSettings.VIBRATION_MAX, Math.Max(0, sensitivity));

        double baseLow = 0.03 + 0.52 * Math.Pow(s, 1.85);
        double baseHigh = 0.05 + 0.40 * Math.Pow(s, 1.35);
        double pulseAmp = (0.08 + 0.34 * s) * p;

        double lowW = 1.0, highW = 1.0;
        if (kind == "deep") { lowW = 1.08; highW = 0.88; }
        else if (kind == "coin") { lowW = 0.92; highW = 1.12; }
        else if (kind == "trash") { lowW = 1.04; highW = 0.82; }

        double rawLow = Math.Min(1, (baseLow + pulseAmp * 0.85) * lowW);
        double rawHigh = Math.Min(1, (baseHigh + pulseAmp) * highW);

        double low = sens <= 1 ? rawLow * sens : Math.Pow(rawLow, 1.0 / sens);
        double high = sens <= 1 ? rawHigh * sens : Math.Pow(rawHigh, 1.0 / sens);
        return ((float)Math.Min(1, Math.Max(0, low)), (float)Math.Min(1, Math.Max(0, high)));
    }

    /// <summary>Representative vibration pulse used in Settings so the player can feel changes right away.</summary>
    public static (float low, float high) PreviewVibration(double sensitivity) => ComputeVibration(0.65, 0.65, "generic", sensitivity);

    public void update(double dt, THREE.Vector3 hit, StateManager state, LootSpawner spawner, AudioManager audio, Beach beach)
    {
        int tier = state.tier("detector");
        radius = state.detectorRadius;
        group.scale.setScalar(0.8 + tier * 0.12);
        coil.scale.set(1, 1, 1);

        if (hit == null)
        {
            signal.strength = 0;
            signal.kind = null;
            pingPulse = 0;
            foreach (var o in overlays) o.visible = false;
            group.visible = false;
            return;
        }
        group.visible = true;
        pulse.visible = edge.visible = pulseDark.visible = edgeDark.visible = true;
        heat.visible = tier >= 3;

        group.position.set(hit.x, hit.y + 0.12, hit.z);
        foreach (var o in overlays) o.position.set(hit.x, hit.y, hit.z);
        edge.scale.set(radius, 1, radius);
        edgeDark.scale.set(radius, 1, radius);
        heat.scale.set(radius, 1, radius);

        var near = spawner.nearest(hit.x, hit.z, radius);
        if (near != null)
        {
            signal.strength = 1 - near.Value.d / radius;
            signal.kind = tier >= 2 ? near.Value.item.def.signature : "generic";
        }
        else
        {
            signal.strength = 0;
            signal.kind = null;
        }

        // radar pulse ring: faster and brighter the closer you get
        double s = signal.strength;
        pulseT += dt * (0.5 + s * 2.2);
        double ph = pulseT % 1;
        double pr = Math.Max(0.05, ph * radius);
        pulse.scale.set(pr, 1, pr);
        pulseDark.scale.set(pr, 1, pr);
        // the rings and the heat map follow the ground, so hills and holes never cut them off. The growing pulse needs it
        // every frame; the fixed edge and the heat map only when the aim point, the reach or the sand under them has moved
        // (each is a mesh sent to the graphics chip again)
        Conform.conformToTerrain(pulse, beach, hit.x, hit.y, hit.z, 0.07);
        Conform.conformToTerrain(pulseDark, beach, hit.x, hit.y, hit.z, 0.066);
        bool moved = Math.Abs(hit.x - edgeAt.x) > 0.02 || Math.Abs(hit.z - edgeAt.z) > 0.02 || Math.Abs(hit.y - edgeAt.y) > 0.005
            || radius != edgeRadius || beach.version != edgeSand;
        if (moved)
        {
            edgeAt.copy(hit);
            edgeRadius = radius;
            edgeSand = beach.version;
            Conform.conformToTerrain(edge, beach, hit.x, hit.y, hit.z, 0.075);
            Conform.conformToTerrain(edgeDark, beach, hit.x, hit.y, hit.z, 0.068);
        }
        if (tier >= 3 && (moved || !heatShaped))
        {
            heatShaped = true;
            Conform.conformToTerrain(heat, beach, hit.x, hit.y, hit.z, 0.1);
        }
        var pm = (THREE.MeshBasicMaterial)pulse.material;
        pm.opacity = (1 - ph) * (0.5 + s * 0.5);
        pulseDark.material.opacity = (1 - ph) * 0.3;
        // saturated colours that read against pale sand (the old pastels washed out)
        pm.color.set(signal.kind == "trash" ? 0xff7a1a : signal.kind == "deep" ? 0xa95cff : s > 0 ? 0xffc400 : 0x35d27a);

        audio.detectorStatic(dt, signal.strength, signal.kind);

        // beep: 800ms far, 90ms directly overhead
        if (near != null)
        {
            pingPulse = Math.Max(0, pingPulse - dt / 0.085);
            double interval = 800 - (800 - 90) * Math.Pow(s, 0.85);
            if (pingTimer > interval) pingTimer = interval;
            pingTimer -= dt * 1000;
            if (pingTimer <= 0)
            {
                pingTimer = interval;
                pingPulse = 1;
                audio.ping(signal.kind ?? "generic", s);
            }
        }
        else
        {
            pingTimer = Math.Min(pingTimer, 0);
            pingPulse = 0;
        }

        // the heat map is a canvas drawn on the CPU and uploaded: ten times a second is plenty for blobs that barely move
        heatT -= dt;
        if (tier >= 3 && (heatT <= 0 || moved))
        {
            heatT = 0.1;
            drawHeat(hit, spawner);
        }
    }

    private readonly THREE.Vector3 edgeAt = new THREE.Vector3(double.NaN, 0, 0);
    private double edgeRadius = -1, heatT;
    private int edgeSand = -1;
    private bool heatShaped;

    private void drawHeat(THREE.Vector3 hit, LootSpawner spawner)
    {
        var g = heatCanvas.getContext("2d");
        const int S = 128;
        g.clearRect(0, 0, S, S);
        g.fillStyle = "rgba(10,30,40,0.25)";
        g.beginPath();
        g.arc(S / 2.0, S / 2.0, S / 2.0, 0, Math.PI * 2);
        g.fill();
        foreach (var it in spawner.items)
        {
            if (it.revealed || it.collected) continue;
            double dx = it.x - hit.x, dz = it.z - hit.z;
            double d = JS.Hypot(dx, dz);
            if (d > radius) continue;
            double cx = S / 2.0 + dx / radius * (S / 2.0), cy = S / 2.0 + dz / radius * (S / 2.0);
            var grad = g.createRadialGradient(cx, cy, 0, cx, cy, 20);
            string hot = it.def.signature == "trash" ? "255,150,60" : it.def.signature == "coin" ? "255,226,122" : "199,155,255";
            grad.addColorStop(0, $"rgba({hot},0.95)");
            grad.addColorStop(1, $"rgba({hot},0)");
            g.fillStyle = grad;
            g.fillRect(cx - 20, cy - 20, 40, 40);
        }
        heatTex.needsUpdate = true;
    }

    /// <summary>Live oscilloscope trace.</summary>
    // The detector screen. The browser redrew it all on a canvas every frame; here the CPU canvas holds only the screen and its
    // grid (drawn once), the moving line is a GPU vector layer above it (Painter2D) and the label a small texture on top.
    private TexturePainter waveBg;
    private UnityEngine.UIElements.VisualElement waveLine;
    private UnityEngine.UIElements.VisualElement waveHost;
    private readonly List<UnityEngine.Vector2> wavePts = new List<UnityEngine.Vector2>();
    private UnityEngine.Color waveColor;
    private float waveW = 1, waveH = 1;
    private string labelText;
    private string waveColorText;
    private double lastWaveT = double.NegativeInfinity;

    public void drawWaveform(CanvasRenderingContext2D ctx, double w, double h, double t, int tier)
    {
        var host = UiCanvas.Of(ctx.canvas)?.ve;
        if (waveBg == null || waveBg.width != (int)w || waveBg.height != (int)h || !ReferenceEquals(host, waveHost))
        {
            waveBg = new TexturePainter((int)w, (int)h);
            var bg = waveBg.getContext("2d");
            bg.fillStyle = "#0b1a14";
            bg.fillRect(0, 0, w, h);
            bg.strokeStyle = "rgba(120,255,170,0.12)";
            bg.lineWidth = 1;
            for (double x = 0; x < w; x += w / 10) { bg.beginPath(); bg.moveTo(x, 0); bg.lineTo(x, h); bg.stroke(); }
            for (double y = 0; y < h; y += h / 4) { bg.beginPath(); bg.moveTo(0, y); bg.lineTo(w, y); bg.stroke(); }
            waveHost = host;
            waveW = (float)w;
            waveH = (float)h;
            if (host != null)
            {
                waveLine = WaveLayer(host);
                waveLine.generateVisualContent += DrawWaveLine;
            }
            labelText = null;
        }
        double strength = signal.strength;
        string kind = signal.kind;
        double mid = h / 2;
        double amp = kind != null ? 0.12 + strength * 0.85 : 0.04;
        string color = kind == "trash" ? "#ff9a3c" : kind == "coin" ? "#a8ee7a" : kind == "deep" ? "#c79bff" : kind == "generic" ? "#b8d4c0" : "#4a7a5e";
        if (color != waveColorText)
        {
            waveColorText = color;
            UnityEngine.ColorUtility.TryParseHtmlString(color, out waveColor);
        }
        // the line is redrawn about 30 times a second with a point every 4 px: the eye cannot tell it from every frame and
        // every 2 px, and the strokes (three of them, round-joined) are the expensive part
        bool redraw = wavePts.Count == 0 || t - lastWaveT >= 1.0 / 30 || t < lastWaveT;
        if (redraw)
        {
            lastWaveT = t;
            wavePts.Clear();
        }
        for (double x = 0; redraw && x <= w; x += 4)
        {
            double a = x / w * Math.PI * 6 + t * 7;
            double v;
            switch (kind)
            {
                case "trash": v = (JS.Random() * 2 - 1) * 0.8 + JS.Sign(Math.Sin(a * 2.3)) * 0.3; break;
                case "coin": v = Math.Sin(a); break;
                case "deep": v = (Math.Sin(a) + 0.6 * Math.Sin(a * 2.01 + t * 2)) * (0.65 + 0.35 * Math.Sin(t * 4)) * 0.75; break;
                case "generic": v = Math.Sin(a) * 0.8 + (JS.Random() - 0.5) * 0.25; break;
                default: v = (JS.Random() - 0.5) * 0.4; break;
            }
            wavePts.Add(new UnityEngine.Vector2((float)x, (float)(mid - v * amp * (h / 2 - 4))));
        }
        if (redraw) waveLine?.MarkDirtyRepaint();

        string label = tier < 2 && kind != null ? "SIGNAL" : kind == "trash" ? "TRASH" : kind == "coin" ? "COIN / METAL" : kind == "deep" ? "DEEP ANTIQUE" : "NO SIGNAL";
        // the label only changes with the signal: the canvas (screen, grid and label) is redrawn only then, so it is
        // uploaded only then too
        if (labelText != label)
        {
            labelText = label;
            ctx.clearRect(0, 0, w, h);
            ctx.drawImage(waveBg, 0, 0);
            ctx.fillStyle = "rgba(160,255,200,0.7)";
            ctx.font = "12px monospace";
            ctx.fillText(label, 6, 12);
        }
    }

    private static UnityEngine.UIElements.VisualElement WaveLayer(UnityEngine.UIElements.VisualElement host)
    {
        var v = new UnityEngine.UIElements.VisualElement { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
        v.style.position = UnityEngine.UIElements.Position.Absolute;
        v.style.left = 0;
        v.style.top = 0;
        v.style.right = 0;
        v.style.bottom = 0;
        host.Add(v);
        return v;
    }

    /// <summary>The line with its glow: the canvas used a 6 px shadow blur; soft wider strokes underneath look the same.</summary>
    private void DrawWaveLine(UnityEngine.UIElements.MeshGenerationContext mgc)
    {
        if (wavePts.Count < 2) return;
        var rect = waveLine.contentRect;
        float sx = rect.width / waveW, sy = rect.height / waveH;
        var p = mgc.painter2D;
        p.lineJoin = UnityEngine.UIElements.LineJoin.Round;
        p.lineCap = UnityEngine.UIElements.LineCap.Round;
        var glow = new[] { (5.5f, 0.05f), (3.5f, 0.09f) };
        foreach (var (width, alpha) in glow) Stroke(p, sx, sy, width, new UnityEngine.Color(waveColor.r, waveColor.g, waveColor.b, alpha));
        Stroke(p, sx, sy, 2f, waveColor);
    }

    private void Stroke(UnityEngine.UIElements.Painter2D p, float sx, float sy, float width, UnityEngine.Color c)
    {
        p.strokeColor = c;
        p.lineWidth = width * Math.Min(sx, sy);
        p.BeginPath();
        p.MoveTo(new UnityEngine.Vector2(wavePts[0].x * sx, wavePts[0].y * sy));
        for (int i = 1; i < wavePts.Count; i++) p.LineTo(new UnityEngine.Vector2(wavePts[i].x * sx, wavePts[i].y * sy));
        p.Stroke();
    }
}
