using System;
using System.Collections.Generic;

// Port of src/world/Sky.ts

/// <summary>Sky dome, sun, hemisphere light, fog, with smooth cross-fades between lighting moods.</summary>
public class Sky
{
    private class Preset
    {
        public int top, bottom, fog, sun, hemiSky, hemiGround;
        public double fogDensity, sunIntensity, hemiIntensity, exposure, stars, fire;
        public double[] sunPos;
    }

    private static readonly Dictionary<string, Preset> PRESETS = new Dictionary<string, Preset>
    {
        { "golden", new Preset { top = 0x8fb6c9, bottom = 0xffd9a0, fog = 0xf3d9b8, fogDensity = 0.018, sun = 0xffc27a, sunIntensity = 1.9, sunPos = new double[] { -14, 13, -22 }, hemiSky = 0xcfe3ee, hemiGround = 0xc9a27a, hemiIntensity = 0.55, exposure = 0.85, stars = 0, fire = 0.6 } },
        { "day", new Preset { top = 0x5fa8e0, bottom = 0xcfeaf5, fog = 0xece9df, fogDensity = 0.0042, sun = 0xfff4de, sunIntensity = 3.2, sunPos = new double[] { 10, 32, 6 }, hemiSky = 0xcfe8ff, hemiGround = 0xe2cfa8, hemiIntensity = 1.1, exposure = 1.0, stars = 0, fire = 0.3 } },
        { "sunset", new Preset { top = 0x4a4a8a, bottom = 0xff8a5c, fog = 0xe9a38a, fogDensity = 0.02, sun = 0xff7a3d, sunIntensity = 2.2, sunPos = new double[] { -6, 5, 34 }, hemiSky = 0xb99ac8, hemiGround = 0xa5694e, hemiIntensity = 0.7, exposure = 0.95, stars = 0.2, fire = 1.0 } },
        { "night", new Preset { top = 0x070b24, bottom = 0x20305a, fog = 0x18223f, fogDensity = 0.022, sun = 0x8fa6ff, sunIntensity = 0.55, sunPos = new double[] { 14, 24, 20 }, hemiSky = 0x3a4a85, hemiGround = 0x1b1b2e, hemiIntensity = 0.55, exposure = 1.0, stars = 1, fire = 1.8 } },
    };

    /// <summary>One full day: morning, noon, sunset, dusk, night, and back to morning.</summary>
    private static readonly (double t, string p)[] KEYS =
    {
        (0, "golden"), (0.28, "day"), (0.56, "sunset"), (0.67, "night"), (0.92, "night"), (1, "golden"),
    };

    /// <summary>The sun and moon sit just inside the sky dome, beyond the distant mountains, so peaks pass in front of them.</summary>
    private const double SKY_BODY_DIST = 296;

    private static double smoothstep(double a, double b, double x)
    {
        double t = Math.Min(1, Math.Max(0, (x - a) / (b - a)));
        return t * t * (3 - 2 * t);
    }

    private static THREE.CanvasTexture moonTexture()
    {
        var c = new HTMLCanvasElement(128, 128);
        var g = c.getContext("2d");
        var grad = g.createRadialGradient(64, 64, 0, 64, 64, 62);
        grad.addColorStop(0, "#f6f8ff");
        grad.addColorStop(0.85, "#dfe6f6");
        grad.addColorStop(0.95, "rgba(223,230,246,0.6)");
        grad.addColorStop(1, "rgba(223,230,246,0)");
        g.fillStyle = grad;
        g.fillRect(0, 0, 128, 128);
        // a few soft craters
        foreach (var (x, y, r) in new[] { (48.0, 48.0, 12.0), (78, 70, 16), (60, 88, 8), (86, 42, 7), (38, 78, 6) })
        {
            var cg = g.createRadialGradient(x, y, 0, x, y, r);
            cg.addColorStop(0, "rgba(150,160,185,0.55)");
            cg.addColorStop(1, "rgba(150,160,185,0)");
            g.fillStyle = cg;
            g.fillRect(x - r, y - r, r * 2, r * 2);
        }
        return new THREE.CanvasTexture(c);
    }

    private static THREE.CanvasTexture glowTexture()
    {
        var c = new HTMLCanvasElement(128, 128);
        var g = c.getContext("2d");
        var grad = g.createRadialGradient(64, 64, 0, 64, 64, 64);
        grad.addColorStop(0, "rgba(255,255,255,1)");
        grad.addColorStop(0.2, "rgba(255,255,255,0.7)");
        grad.addColorStop(1, "rgba(255,255,255,0)");
        g.fillStyle = grad;
        g.fillRect(0, 0, 128, 128);
        return new THREE.CanvasTexture(c);
    }

    private static THREE.CanvasTexture puffTexture(long seed)
    {
        var c = new HTMLCanvasElement(256, 128);
        var g = c.getContext("2d");
        long s = seed;
        double r() => (s = (s * 16807) % 2147483647) / 2147483647.0;
        for (int i = 0; i < 16; i++)
        {
            double x = 50 + r() * 156;
            double y = 50 + r() * 40 - Math.Abs(x - 128) * 0.15;
            double rad = 22 + r() * 30;
            var grad = g.createRadialGradient(x, y, 0, x, y, rad);
            grad.addColorStop(0, "rgba(255,255,255,0.55)");
            grad.addColorStop(1, "rgba(255,255,255,0)");
            g.fillStyle = grad;
            g.fillRect(x - rad, y - rad, rad * 2, rad * 2);
        }
        return new THREE.CanvasTexture(c);
    }

    private static THREE.CanvasTexture discTexture()
    {
        var c = new HTMLCanvasElement(128, 128);
        var g = c.getContext("2d");
        var grad = g.createRadialGradient(64, 64, 0, 64, 64, 64);
        grad.addColorStop(0, "rgba(255,255,255,1)");
        grad.addColorStop(0.62, "rgba(255,255,255,1)");
        grad.addColorStop(0.72, "rgba(255,255,255,0.35)");
        grad.addColorStop(1, "rgba(255,255,255,0)");
        g.fillStyle = grad;
        g.fillRect(0, 0, 128, 128);
        return new THREE.CanvasTexture(c);
    }

    private class Cloud
    {
        public THREE.Group group;
        public double speed;
    }

    public readonly THREE.DirectionalLight sun;
    public readonly THREE.HemisphereLight hemi;
    public readonly THREE.FogExp2 fog;
    /// <summary>0.3..1.8 multiplier for the campfire/lantern lights</summary>
    public double fireBoost = 0.6;

    private readonly THREE.WebGLRenderer renderer;
    private readonly THREE.Mesh dome;
    private readonly THREE.Points stars;
    private readonly THREE.Sprite glow, core;
    private readonly List<Cloud> clouds = new List<Cloud>();
    private readonly List<THREE.SpriteMaterial> cloudMats = new List<THREE.SpriteMaterial>();
    private readonly THREE.Color cloudTint = new THREE.Color();
    /// <summary>the sun's shadow box follows this point (the player)</summary>
    public readonly THREE.Vector3 focus = new THREE.Vector3();
    private readonly Dictionary<string, THREE.Uniform> uniforms = new Dictionary<string, THREE.Uniform>
    {
        { "uTop", new THREE.Uniform(new THREE.Color()) },
        { "uBottom", new THREE.Uniform(new THREE.Color()) },
    };
    private THREE.Color uTop => (THREE.Color)uniforms["uTop"].value;
    private THREE.Color uBottom => (THREE.Color)uniforms["uBottom"].value;
    private readonly THREE.Color tmp = new THREE.Color(), tmpB = new THREE.Color();
    private readonly THREE.Sprite moonGlow, moonCore;
    private readonly THREE.Vector3 sunPos = new THREE.Vector3(), moonPos = new THREE.Vector3(), lightPos = new THREE.Vector3();
    /// <summary>0 in full daylight, 1 in deep night</summary>
    public double starAmount = 0;
    /// <summary>time of day in [0,1)</summary>
    public double time = 0;
    public readonly THREE.Group group = new THREE.Group();

    public Sky(THREE.WebGLRenderer renderer, THREE.Scene scene)
    {
        this.renderer = renderer;
        fog = new THREE.FogExp2(PRESETS["golden"].fog, PRESETS["golden"].fogDensity);
        scene.fog = fog;

        dome = new THREE.Mesh(
            new THREE.SphereGeometry(300, 32, 16),
            new THREE.ShaderMaterial { customShader = "Cozy/SkyDome", uniforms = uniforms, side = THREE.Side.BackSide, depthWrite = false, fog = false });
        group.add(dome);

        var sp = new float[400 * 3];
        for (int i = 0; i < 400; i++)
        {
            var v = new THREE.Vector3(JS.Random() * 2 - 1, JS.Random() * 0.9 + 0.1, JS.Random() * 2 - 1).normalize().multiplyScalar(280);
            sp[i * 3] = (float)v.x; sp[i * 3 + 1] = (float)v.y; sp[i * 3 + 2] = (float)v.z;
        }
        var sg = new THREE.BufferGeometry();
        sg.setAttribute("position", new THREE.BufferAttribute(sp, 3));
        stars = new THREE.Points(sg, new THREE.PointsMaterial { color = 0xffffff, size = 1.6, sizeAttenuation = false, transparent = true, opacity = 0, fog = false });
        group.add(stars);

        glow = new THREE.Sprite(new THREE.SpriteMaterial { map = glowTexture(), color = 0xffd9a0, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false, transparent = true, toneMapped = false });
        glow.scale.setScalar(165);
        group.add(glow);
        core = new THREE.Sprite(new THREE.SpriteMaterial { map = discTexture(), color = 0xfff2c4, depthWrite = false, fog = false, transparent = true, toneMapped = false });
        core.scale.setScalar(33);
        group.add(core);
        moonGlow = new THREE.Sprite(new THREE.SpriteMaterial { map = glowTexture(), color = 0x9db4ff, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false, transparent = true });
        moonGlow.scale.setScalar(105);
        moonCore = new THREE.Sprite(new THREE.SpriteMaterial { map = moonTexture(), color = 0xffffff, depthWrite = false, fog = false, transparent = true, toneMapped = false });
        moonCore.scale.setScalar(30);
        group.add(moonGlow, moonCore);
        buildClouds();

        hemi = new THREE.HemisphereLight(0xcfe3ee, 0xc9a27a, 0.9);
        sun = new THREE.DirectionalLight(0xffc27a, 2.6);
        sun.castShadow = true;
        sun.shadow.mapSize.set(2048, 2048);
        var cam = (THREE.OrthographicCamera)sun.shadow.camera;
        cam.left = -26; cam.right = 26; cam.top = 26; cam.bottom = -26; cam.near = 1; cam.far = 120;
        sun.shadow.bias = -0.0004;
        sun.shadow.normalBias = 0.04;
        group.add(hemi, sun, sun.target);
        setTime(0);
    }

    private void buildClouds()
    {
        for (int i = 0; i < 3; i++)
            cloudMats.Add(new THREE.SpriteMaterial { map = puffTexture(11 + i * 97), transparent = true, depthWrite = false, fog = false, opacity = 0.92 });
        for (int i = 0; i < 16; i++)
        {
            var g = new THREE.Group();
            int parts = 4 + (int)Math.Floor(JS.Random() * 4);
            for (int k = 0; k < parts; k++)
            {
                var s = new THREE.Sprite(cloudMats[(i + k) % 3]);
                s.scale.set(70 + JS.Random() * 60, 28 + JS.Random() * 14, 1);
                s.position.set((k - parts / 2.0) * 38 + JS.Random() * 14, JS.Random() * 8, JS.Random() * 20);
                g.add(s);
            }
            double ang = JS.Random() * Math.PI * 2;
            double r = 120 + JS.Random() * 150;
            g.position.set(Math.Cos(ang) * r, 70 + JS.Random() * 60, Math.Sin(ang) * r);
            group.add(g);
            clouds.Add(new Cloud { group = g, speed = 1.2 + JS.Random() * 2.2 });
        }
    }

    /// <summary>0..1 how overcast and rainy it is; the next setTime applies it</summary>
    public double rain = 0;

    /// <summary>Underwater, the sky, sun, moon and clouds are not there, and the fog is the water.</summary>
    public bool underwater = false;

    /// <summary>Underground the sky stays (it shows through the hatch), but the room is lit by torches, not by the day.</summary>
    public bool underground = false;
    /// <summary>0 deep in the stone room .. 1 in the sea cave, where the daylight and the open air come in</summary>
    public double openAir = 0;

    /// <summary>The cloud materials, so a view from below can show the same clouds in the same light.</summary>
    public List<THREE.SpriteMaterial> cloudMaterials => cloudMats;

    public void setUnderground(bool on) => underground = on;

    public void setUnderwater(bool on)
    {
        underwater = on;
        foreach (var o in new THREE.Object3D[] { dome, stars, glow, core, moonGlow, moonCore }) o.visible = !on;
        foreach (var c in clouds) c.group.visible = !on;
    }

    /// <summary>True once it is dark enough for lanterns and window lights.</summary>
    public bool isDark => starAmount > 0.3 || rain > 0.4;

    public void update(double dt)
    {
        foreach (var c in clouds)
        {
            c.group.position.x += c.speed * dt;
            if (c.group.position.x > 320) c.group.position.x = -320;
        }
    }

    private (Preset a, Preset b, double k) presetAt(double t)
    {
        int i = 0;
        while (i < KEYS.Length - 2 && t >= KEYS[i + 1].t) i++;
        var k0 = KEYS[i];
        var k1 = KEYS[i + 1];
        return (PRESETS[k0.p], PRESETS[k1.p], smoothstep(k0.t, k1.t, t));
    }

    /// <summary>Sets the whole sky, lighting and fog for a time of day in [0,1).</summary>
    public void setTime(double timeOfDay)
    {
        double t = ((timeOfDay % 1) + 1) % 1;
        time = t;
        var (a, b, k) = presetAt(t);
        THREE.Color mixCol(THREE.Color o, int ha, int hb) => o.set(ha).lerp(tmpB.set(hb), k);
        double num(double x, double y) => x + (y - x) * k;

        uTop.copy(mixCol(tmp, a.top, b.top));
        uBottom.copy(mixCol(tmp, a.bottom, b.bottom));
        fog.color.copy(mixCol(tmp, a.fog, b.fog));
        fog.density = num(a.fogDensity, b.fogDensity);
        sun.color.copy(mixCol(tmp, a.sun, b.sun));
        sun.intensity = num(a.sunIntensity, b.sunIntensity);
        hemi.color.copy(mixCol(tmp, a.hemiSky, b.hemiSky));
        hemi.groundColor.copy(mixCol(tmp, a.hemiGround, b.hemiGround));
        hemi.intensity = num(a.hemiIntensity, b.hemiIntensity);
        renderer.toneMappingExposure = num(a.exposure, b.exposure);
        starAmount = num(a.stars, b.stars);
        stars.material.opacity = starAmount;
        fireBoost = num(a.fire, b.fire);

        // the sun climbs from one side to the other; the moon does the same, over the sea
        double s = t / 0.62;
        double sa = Math.PI * (1 - Math.Min(1.1, s));
        sunPos.set(Math.Cos(sa) * 110, Math.Sin(sa) * 85 + 2, -30);
        double u = Math.Max(0, (t - 0.58) / 0.42);
        double ma = Math.PI * (1 - Math.Min(1.1, u));
        moonPos.set(Math.Cos(ma) * 110, Math.Sin(ma) * 80 + 2, 55);

        // sunlight hands over to moonlight around dusk
        double w = smoothstep(0.56, 0.68, t);
        lightPos.copy(sunPos).lerp(moonPos, w);
        sun.position.copy(lightPos).add(focus);
        sun.target.position.copy(focus);

        double sunVis = Math.Min(1, Math.Max(0, sunPos.y / 14)) * (1 - w);
        glow.position.copy(sunPos).normalize().multiplyScalar(SKY_BODY_DIST);
        core.position.copy(glow.position);
        core.material.color.copy(sun.color).lerp(tmp.set(0xffffff), 0.55);
        core.material.opacity = sunVis;
        glow.material.color.copy(sun.color);
        glow.material.opacity = Math.Min(1, sun.intensity / 2.2) * sunVis;

        double moonVis = Math.Min(1, Math.Max(0, moonPos.y / 14)) * smoothstep(0.6, 0.7, t) * (1 - smoothstep(0.93, 1, t));
        moonCore.position.copy(moonPos).normalize().multiplyScalar(SKY_BODY_DIST);
        moonGlow.position.copy(moonCore.position);
        moonCore.material.opacity = moonVis;
        moonGlow.material.opacity = moonVis * 0.55;

        // clouds pick up the haze colour and fade with the light
        double light = Math.Max(sun.intensity * (1 - w), 0.55);
        cloudTint.copy(uBottom).lerp(tmp.set(0xffffff), 0.55).multiplyScalar(Math.Min(1, 0.22 + light / 2.4));
        foreach (var m in cloudMats) m.color.copy(cloudTint);

        // rain: grey, dim and close
        double r = rain;
        if (r > 0.001)
        {
            var grey = tmp.set(0x6f7882).multiplyScalar(0.35 + 0.65 * Math.Min(1, sun.intensity / 2.2));
            uTop.lerp(grey, r * 0.85);
            uBottom.lerp(grey, r * 0.9);
            fog.color.lerp(grey, r * 0.9);
            fog.density *= 1 + r * 0.9;
            sun.intensity *= 1 - r * 0.78;
            hemi.intensity *= 1 - r * 0.18;
            fireBoost = Math.Max(fireBoost, 0.6 + r * 0.5);
            double clear = 1 - r;
            core.material.opacity *= clear;
            glow.material.opacity *= clear;
            foreach (var m in cloudMats)
            {
                m.color.multiplyScalar(1 - r * 0.45);
                m.opacity = 0.92 + r * 0.08;
            }
        }
        else foreach (var m in cloudMats) m.opacity = 0.92;
        if (underground)
        {
            double kk = openAir;
            hemi.intensity *= 0.16 + 0.5 * kk;
            tmp.copy(fog.color);
            fog.color.set(0x0c0907).lerp(tmp, kk);
            fog.density = 0.02 + (fog.density * 0.6 - 0.02) * kk;
        }
        if (underwater)
        {
            // the fog becomes the water: a clear blue-green that swallows everything past a few dozen metres, and dims at night
            double day = 0.28 + 0.72 * (1 - starAmount);
            fog.color.set(0x1b7aa6).multiplyScalar(day);
            fog.density = 0.011;
            hemi.color.set(0x9fdcf0);
            hemi.groundColor.set(0x3b7f8f);
            hemi.intensity = Math.Max(hemi.intensity, 1.15) * (0.4 + 0.6 * day);
        }
    }
}
