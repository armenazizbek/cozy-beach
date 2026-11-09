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
    private readonly THREE.Mesh coil, pulse, edge;
    /// <summary>a dark band under each bright ring, so the rings stand out on pale sand as well as dark</summary>
    private readonly THREE.Mesh pulseDark, edgeDark;
    private readonly THREE.Mesh heat;
    private readonly HTMLCanvasElement heatCanvas = new HTMLCanvasElement(128, 128);
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
    }

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
        // the rings and the heat map follow the ground, so hills and holes never cut them off
        Conform.conformToTerrain(pulse, beach, hit.x, hit.y, hit.z, 0.07);
        Conform.conformToTerrain(edge, beach, hit.x, hit.y, hit.z, 0.075);
        Conform.conformToTerrain(pulseDark, beach, hit.x, hit.y, hit.z, 0.066);
        Conform.conformToTerrain(edgeDark, beach, hit.x, hit.y, hit.z, 0.068);
        if (tier >= 3) Conform.conformToTerrain(heat, beach, hit.x, hit.y, hit.z, 0.1);
        var pm = (THREE.MeshBasicMaterial)pulse.material;
        pm.opacity = (1 - ph) * (0.5 + s * 0.5);
        pulseDark.material.opacity = (1 - ph) * 0.3;
        // saturated colours that read against pale sand (the old pastels washed out)
        pm.color.set(signal.kind == "trash" ? 0xff7a1a : signal.kind == "deep" ? 0xa95cff : s > 0 ? 0xffc400 : 0x35d27a);

        audio.detectorStatic(dt, signal.strength, signal.kind);

        // beep: 800ms far, 90ms directly overhead
        if (near != null)
        {
            pingTimer -= dt * 1000;
            if (pingTimer <= 0)
            {
                pingTimer = 800 - (800 - 90) * Math.Pow(s, 0.85);
                audio.ping(signal.kind ?? "generic", s);
            }
        }
        else pingTimer = Math.Min(pingTimer, 0);

        if (tier >= 3) drawHeat(hit, spawner);
    }

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
    /// <summary>The screen and its grid never change, so they are drawn once and copied in (this canvas is drawn on the CPU here).</summary>
    private HTMLCanvasElement waveBg;

    public void drawWaveform(CanvasRenderingContext2D ctx, double w, double h, double t, int tier)
    {
        if (waveBg == null || waveBg.width != (int)w || waveBg.height != (int)h)
        {
            waveBg = new HTMLCanvasElement((int)w, (int)h);
            var bg = waveBg.getContext("2d");
            bg.fillStyle = "#0b1a14";
            bg.fillRect(0, 0, w, h);
            bg.strokeStyle = "rgba(120,255,170,0.12)";
            bg.lineWidth = 1;
            for (double x = 0; x < w; x += w / 10) { bg.beginPath(); bg.moveTo(x, 0); bg.lineTo(x, h); bg.stroke(); }
            for (double y = 0; y < h; y += h / 4) { bg.beginPath(); bg.moveTo(0, y); bg.lineTo(w, y); bg.stroke(); }
        }
        ctx.drawImage(waveBg, 0, 0);
        double strength = signal.strength;
        string kind = signal.kind;
        double mid = h / 2;
        double amp = kind != null ? 0.12 + strength * 0.85 : 0.04;
        string color = kind == "trash" ? "#ff9a3c" : kind == "coin" ? "#a8ee7a" : kind == "deep" ? "#c79bff" : kind == "generic" ? "#b8d4c0" : "#4a7a5e";
        ctx.strokeStyle = color;
        ctx.shadowColor = color;
        ctx.shadowBlur = 6;
        ctx.lineWidth = 2;
        ctx.beginPath();
        for (double x = 0; x <= w; x += 2)
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
            double y = mid - v * amp * (h / 2 - 4);
            if (x == 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        }
        ctx.stroke();
        ctx.shadowBlur = 0;
        string label = tier < 2 && kind != null ? "SIGNAL" : kind == "trash" ? "TRASH" : kind == "coin" ? "COIN / METAL" : kind == "deep" ? "DEEP ANTIQUE" : "NO SIGNAL";
        // the label only changes with the signal, so its text is drawn once and copied in on top, as before
        if (labelCv == null || labelText != label || labelCv.width != (int)w)
        {
            labelText = label;
            labelCv = new HTMLCanvasElement((int)w, 18);
            var lc = labelCv.getContext("2d");
            lc.fillStyle = "rgba(160,255,200,0.7)";
            lc.font = "10px monospace";
            lc.fillText(label, 6, 12);
        }
        ctx.drawImage(labelCv, 0, 0);
    }

    private HTMLCanvasElement labelCv;
    private string labelText;
}
