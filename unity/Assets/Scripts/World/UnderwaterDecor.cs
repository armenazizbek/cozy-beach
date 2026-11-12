using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// What makes the sea floor a place and not a sandy plain: sunlight rippling over the sand and under the surface, shafts of
/// light slanting down, outcrops of boulders with coral growing on them, a stone arch, shoals of small silver fish that move
/// as one, jellyfish drifting in mid-water, and the old jars spilled round the wreck. None of it can be caught or picked up.
/// </summary>
public class UnderwaterDecor
{
    private static readonly (double x, double z, double half) UW = Underwater.UW;

    private readonly THREE.Group group;
    private readonly Func<double, double, double> floorAt;
    private readonly Func<double> rand = Underwater.seeded(9001);

    // light
    private readonly THREE.CanvasTexture causticA, causticB, causticTop;
    private readonly THREE.Mesh shimmer;
    private readonly THREE.Sprite sunPatch;
    private readonly List<THREE.MeshBasicMaterial> causticMats = new List<THREE.MeshBasicMaterial>();
    private class Ray { public THREE.Mesh mesh; public double x, z, phase, born, strength; }
    private readonly List<Ray> rays = new List<Ray>();
    private double daylight = 1;

    // life
    private class Shoal
    {
        public List<(THREE.Mesh mesh, double ox, double oy, double oz, double phase)> fish = new List<(THREE.Mesh, double, double, double, double)>();
        public double x, y, z, heading, turn, ty, speed;
    }
    private readonly List<Shoal> shoals = new List<Shoal>();
    private class Jelly { public THREE.Group root; public double x, y, z, phase, drift, baseY; }
    private readonly List<Jelly> jellies = new List<Jelly>();

    public UnderwaterDecor(THREE.Group group, Func<double, double, double> floorAt, (double x, double z) wreck, Action<THREE.Object3D, double, double> sway)
    {
        this.group = group;
        this.floorAt = floorAt;

        var canvas = causticCanvas();
        causticA = tiled(canvas, 1);
        causticB = tiled(canvas, 0.61);
        causticTop = tiled(canvas, 1);
        buildCaustics();
        shimmer = new THREE.Mesh(
            shimmerDisc(),
            new THREE.MeshBasicMaterial { map = causticTop, color = 0xd8f6ff, vertexColors = true, transparent = true, opacity = 0.15, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false, side = THREE.Side.DoubleSide });
        shimmer.renderOrder = 2;
        group.add(shimmer);
        sunPatch = new THREE.Sprite(new THREE.SpriteMaterial { map = glowTexture(), color = 0xeafcff, transparent = true, opacity = 0.5, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false });
        sunPatch.scale.setScalar(34);
        sunPatch.renderOrder = 3;
        group.add(sunPatch);
        buildRays();

        buildOutcrops(wreck, sway);
        buildArch(sway);
        buildJars(wreck);
        buildShoals();
        buildJellies();
    }

    // ---------------------------------------------------------------- light

    /// <summary>how far from the diver the shimmer under the surface reaches, and the size of one tile of it</summary>
    private const double SHIMMER = 95, SHIMMER_TILE = 5.6;

    /// <summary>
    /// A flat disc for the shimmer under the surface. It is bright overhead and fades to nothing toward its rim: far off, seen
    /// at a low angle, the pattern would only break up into streaks.
    /// </summary>
    private static THREE.BufferGeometry shimmerDisc()
    {
        const int RINGS = 10, AROUND = 40;
        var pos = new List<double>();
        var uv = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i <= RINGS; i++)
        {
            double f = (double)i / RINGS, r = SHIMMER * f * f;
            double bright = 1 - Beach.smooth(0.22, 0.9, r / SHIMMER);
            for (int j = 0; j < AROUND; j++)
            {
                double a = (double)j / AROUND * Math.PI * 2;
                double x = Math.Cos(a) * r, z = Math.Sin(a) * r;
                pos.Add(x); pos.Add(0); pos.Add(z);
                uv.Add(x / SHIMMER_TILE); uv.Add(z / SHIMMER_TILE);
                col.Add(bright); col.Add(bright); col.Add(bright);
                if (i == RINGS) continue;
                int k = i * AROUND + j, n = i * AROUND + (j + 1) % AROUND;
                idx.AddRange(new[] { k, k + AROUND, n, n, k + AROUND, n + AROUND });
            }
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        geo.setAttribute("uv", new THREE.Float32BufferAttribute(uv, 2));
        geo.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        geo.setIndex(idx);
        geo.computeVertexNormals();
        return geo;
    }
    /// <summary>metres of sea floor one tile of the rippling light covers</summary>
    private const double TILE = 11;

    /// <summary>
    /// The pattern sunlight makes on the sand under moving water: a net of bright lines round darker cells. It tiles, so it can
    /// be laid across the whole floor. Each pixel is lit by how close it is to the border between its two nearest cells.
    /// </summary>
    private HTMLCanvasElement causticCanvas()
    {
        const int N = 128, CELLS = 26;
        var pts = new (double x, double y)[CELLS];
        for (int i = 0; i < CELLS; i++) pts[i] = (rand(), rand());
        var c = new HTMLCanvasElement(N, N);
        var g = c.getContext("2d");
        var img = g.createImageData(N, N);
        var data = img.data;
        for (int v = 0; v < N; v++)
        {
            for (int u = 0; u < N; u++)
            {
                double px = (u + 0.5) / N, py = (v + 0.5) / N;
                // bent a little, so the lines curve like light through water and not like cracks in glass
                double bx = px + Math.Sin(py * Math.PI * 6) * 0.022 + Math.Sin(py * Math.PI * 14 + 1) * 0.008;
                double by = py + Math.Sin(px * Math.PI * 4 + 2) * 0.026 + Math.Sin(px * Math.PI * 10) * 0.009;
                px = bx - Math.Floor(bx);
                py = by - Math.Floor(by);
                double d1 = 9, d2 = 9;
                foreach (var p in pts)
                {
                    // the nearest copy of the point, wrapping round the edges
                    double dx = Math.Abs(px - p.x), dy = Math.Abs(py - p.y);
                    if (dx > 0.5) dx = 1 - dx;
                    if (dy > 0.5) dy = 1 - dy;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) d2 = d;
                }
                double edge = 1 - Math.Min(1, (d2 - d1) / 0.075);
                double b = Math.Pow(edge, 2.6) * 235 + 6;
                int k = (v * N + u) * 4;
                data[k] = data[k + 1] = data[k + 2] = (byte)b;
                data[k + 3] = 255;
            }
        }
        g.putImageData(img, 0, 0);
        return c;
    }

    private static THREE.CanvasTexture tiled(HTMLCanvasElement canvas, double repeat)
    {
        var t = new THREE.CanvasTexture(canvas);
        t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
        t.repeat.set(repeat, repeat);
        // seen at a low angle far off, the net would break up into streaks without smaller copies to draw from
        t.generateMipmaps = true;
        t.anisotropy = 4;
        t.needsUpdate = true;
        return t;
    }

    /// <summary>Two sheets of that light lying on the sand, sliding slowly across each other so the net never stops changing.</summary>
    private void buildCaustics()
    {
        const int N = 96;
        double span = UW.half + 16;
        var pos = new float[(N + 1) * (N + 1) * 3];
        var uv = new float[(N + 1) * (N + 1) * 2];
        var idx = new List<int>();
        for (int j = 0; j <= N; j++)
        {
            for (int i = 0; i <= N; i++)
            {
                double x = UW.x - span + 2 * span * i / N, z = UW.z - span + 2 * span * j / N;
                int k = j * (N + 1) + i;
                pos[k * 3] = (float)x;
                pos[k * 3 + 1] = (float)(floorAt(x, z) + 0.05);
                pos[k * 3 + 2] = (float)z;
                uv[k * 2] = (float)(x / TILE);
                uv[k * 2 + 1] = (float)(z / TILE);
                if (i < N && j < N) idx.AddRange(new[] { k, k + N + 1, k + 1, k + 1, k + N + 1, k + N + 2 });
            }
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
        geo.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
        geo.setIndex(idx);
        geo.computeVertexNormals();
        foreach (var tex in new[] { causticA, causticB })
        {
            var mat = new THREE.MeshBasicMaterial
            {
                map = tex, color = 0xcdf3ff, transparent = true, opacity = 0.2, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false,
                polygonOffset = true, polygonOffsetFactor = -2, polygonOffsetUnits = -2,
            };
            causticMats.Add(mat);
            var sheet = new THREE.Mesh(geo, mat);
            sheet.renderOrder = 1;
            sheet.frustumCulled = false;
            group.add(sheet);
        }
    }

    /// <summary>A soft round glow.</summary>
    private static THREE.CanvasTexture glowTexture()
    {
        var c = new HTMLCanvasElement(128, 128);
        var g = c.getContext("2d");
        var grad = g.createRadialGradient(64, 64, 0, 64, 64, 64);
        grad.addColorStop(0, "rgba(255,255,255,0.9)");
        grad.addColorStop(0.3, "rgba(255,255,255,0.35)");
        grad.addColorStop(1, "rgba(255,255,255,0)");
        g.fillStyle = grad;
        g.fillRect(0, 0, 128, 128);
        return new THREE.CanvasTexture(c);
    }

    /// <summary>A shaft of light: brightest at the top and along its middle, fading to nothing toward the bottom and the sides.</summary>
    private static THREE.CanvasTexture rayTexture()
    {
        const int W = 32, H = 128;
        var c = new HTMLCanvasElement(W, H);
        var g = c.getContext("2d");
        var img = g.createImageData(W, H);
        var data = img.data;
        for (int v = 0; v < H; v++)
        {
            for (int u = 0; u < W; u++)
            {
                double across = Math.Pow(Math.Sin(Math.PI * (u + 0.5) / W), 2);
                double down = Math.Pow(1 - (v + 0.5) / H, 1.4) * Math.Min(1, (v + 0.5) / 6);
                int k = (v * W + u) * 4;
                data[k] = data[k + 1] = data[k + 2] = 255;
                data[k + 3] = (byte)(255 * across * down);
            }
        }
        g.putImageData(img, 0, 0);
        return new THREE.CanvasTexture(c);
    }

    private const double RAY_REACH = 26;

    private void buildRays()
    {
        var tex = rayTexture();
        for (int i = 0; i < 14; i++)
        {
            double w = 1.6 + rand() * 2.6, h = 15 + rand() * 7;
            var geo = new THREE.PlaneGeometry(w, h);
            geo.translate(0, -h / 2, 0);
            var mesh = new THREE.Mesh(geo, new THREE.MeshBasicMaterial { map = tex, color = 0xd9f7ff, transparent = true, opacity = 0, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false, side = THREE.Side.DoubleSide });
            mesh.rotation.order = "YXZ";
            mesh.renderOrder = 3;
            mesh.frustumCulled = false;
            group.add(mesh);
            double a = rand() * Math.PI * 2, r = 6 + rand() * (RAY_REACH - 8);
            rays.Add(new Ray { mesh = mesh, x = UW.x + Math.Cos(a) * r, z = UW.z + 40 + Math.Sin(a) * r, phase = rand() * 6, born = -10, strength = 0.13 + rand() * 0.09 });
        }
    }

    /// <summary>At night there is no sunlight to play on the sand. 1 is full daylight.</summary>
    public void setDaylight(double k) => daylight = Math.Max(0, (k - 0.18) / 0.82);

    // ---------------------------------------------------------------- rock

    private static readonly int[] ROCK = { 0x6f7476, 0x5d625f, 0x586a64, 0x7b7a70 };

    private THREE.Object3D coralPiece()
    {
        THREE.Color C(int k) => new THREE.Color(k);
        var palettes = new (THREE.Color lo, THREE.Color hi)[]
        {
            (C(0xc2455f), C(0xffb0c0)), (C(0xd9742a), C(0xffd08a)), (C(0x7a4ab8), C(0xe0c4ff)), (C(0xd9b232), C(0xfff2a0)), (C(0x2f9a8a), C(0xb4fff0)), (C(0x2f6fc2), C(0xa8d8ff)),
        };
        var (lo, hi) = palettes[(int)Math.Floor(rand() * palettes.Length)];
        double kind = rand();
        if (kind < 0.4) return SeaFlora.staghorn(rand, lo, hi);
        if (kind < 0.6) return SeaFlora.tubeCoral(rand, lo, hi);
        if (kind < 0.78) return SeaFlora.brainCoral(rand, C(0x8f8a3a), C(0xd7cf6a));
        return SeaFlora.seaFan(new[] { 0xe85a7a, 0xb06ad8, 0xf08a3a }[(int)Math.Floor(rand() * 3)]);
    }

    /// <summary>A boulder in a pile; returns the height of its top, so something can stand on it.</summary>
    private double boulder(THREE.Group g, THREE.BufferGeometry geo, THREE.Material[] mats, double x, double y, double z, double r)
    {
        var m = new THREE.Mesh(geo, mats[(int)Math.Floor(rand() * mats.Length)]);
        double sx = r * (1 + rand() * 0.5), sy = r * (0.7 + rand() * 0.4), sz = r * (1 + rand() * 0.4);
        m.scale.set(sx, sy, sz);
        m.rotation.set(rand() * 0.5, rand() * 6, rand() * 0.5);
        m.position.set(x, y, z);
        g.add(m);
        return y + sy * 0.72;
    }

    /// <summary>
    /// Outcrops: piles of big boulders standing out of the sand, each one a small reef of its own, with coral on the tops, kelp
    /// at the foot and a skirt of smaller stones.
    /// </summary>
    private void buildOutcrops((double x, double z) wreck, Action<THREE.Object3D, double, double> sway)
    {
        var geo = new THREE.DodecahedronGeometry(1, 1);
        var mats = ROCK.Select(k => (THREE.Material)new THREE.MeshStandardMaterial { color = k, roughness = 1, flatShading = true }).ToArray();
        var placed = new List<(double x, double z)>();
        for (int n = 0; n < 40 && placed.Count < 11; n++)
        {
            double x = UW.x + (rand() - 0.5) * UW.half * 1.75;
            double z = UW.z + (rand() - 0.56) * UW.half * 1.7;
            if (JS.Hypot(x - wreck.x, z - wreck.z) < 13 || JS.Hypot(x - (UW.x - 20), z - (UW.z + 4)) < 10) continue;
            if (placed.Any(p => JS.Hypot(p.x - x, p.z - z) < 15)) continue;
            placed.Add((x, z));
            var g = new THREE.Group();
            var tops = new List<(double x, double y, double z)>();
            int count = 5 + (int)Math.Floor(rand() * 5);
            double spread = 1.8 + rand() * 2.2;
            for (int k = 0; k < count; k++)
            {
                double a = rand() * Math.PI * 2, d = k == 0 ? 0 : 0.6 + rand() * spread;
                double r = k == 0 ? 2.0 + rand() * 1.4 : 0.9 + rand() * 1.5;
                double px = Math.Cos(a) * d, pz = Math.Sin(a) * d;
                // the later ones sit up on the first, so the pile has height
                double lift = k > count / 2 ? 0.8 + rand() * 1.8 : 0;
                double top = boulder(g, geo, mats, px, r * 0.3 + lift + floorAt(x + px, z + pz) - floorAt(x, z), pz, r);
                tops.Add((px, top, pz));
            }
            for (int k = 0; k < 6; k++)
            {
                double a = rand() * Math.PI * 2, d = spread + 1.5 + rand() * 2.5;
                double px = Math.Cos(a) * d, pz = Math.Sin(a) * d;
                boulder(g, geo, mats, px, floorAt(x + px, z + pz) - floorAt(x, z) + 0.05, pz, 0.25 + rand() * 0.4);
            }
            int corals = 4 + (int)Math.Floor(rand() * 5);
            for (int k = 0; k < corals; k++)
            {
                var top = tops[(int)Math.Floor(rand() * tops.Count)];
                var piece = coralPiece();
                piece.position.set(top.x + (rand() - 0.5) * 0.9, top.y - 0.12, top.z + (rand() - 0.5) * 0.9);
                piece.rotation.y = rand() * 6;
                piece.scale.setScalar(0.8 + rand() * 0.9);
                g.add(piece);
            }
            for (int k = 0; k < 4; k++)
            {
                double a = rand() * Math.PI * 2, d = spread + 0.8 + rand() * 1.6;
                var plant = rand() < 0.5 ? SeaFlora.kelp(rand) : SeaFlora.seagrass(rand);
                double px = Math.Cos(a) * d, pz = Math.Sin(a) * d;
                plant.position.set(px, floorAt(x + px, z + pz) - floorAt(x, z) - 0.05, pz);
                g.add(plant);
                sway(plant, rand() * 6, 0.07);
            }
            g.position.set(x, floorAt(x, z), z);
            group.add(g);
        }
    }

    /// <summary>A stone arch wide and tall enough to swim through: two piers of stacked boulders and a span across them, hung with sea fans.</summary>
    private void buildArch(Action<THREE.Object3D, double, double> sway)
    {
        var geo = new THREE.DodecahedronGeometry(1, 1);
        var mats = ROCK.Select(k => (THREE.Material)new THREE.MeshStandardMaterial { color = k, roughness = 1, flatShading = true }).ToArray();
        double cx = UW.x - 20, cz = UW.z + 4;
        var g = new THREE.Group();
        double top = 0;
        foreach (var side in new[] { -1, 1 })
        {
            double y = -0.3;
            for (int k = 0; k < 4; k++)
            {
                double r = 1.7 - k * 0.2;
                y = boulder(g, geo, mats, side * (3.6 - k * 0.35) + (rand() - 0.5) * 0.3, y + r * 0.55, (rand() - 0.5) * 0.5, r) - r * 0.25;
            }
            top = Math.Max(top, y);
        }
        for (int k = -2; k <= 2; k++) boulder(g, geo, mats, k * 1.25, top + 0.55 - Math.Abs(k) * 0.28, (rand() - 0.5) * 0.4, 1.25 - Math.Abs(k) * 0.1);
        for (int k = 0; k < 7; k++)
        {
            var piece = coralPiece();
            double px = (rand() - 0.5) * 7;
            piece.position.set(px, top + 1.15 - Math.Abs(px) * 0.25, (rand() - 0.5) * 0.8);
            piece.rotation.y = rand() * 6;
            piece.scale.setScalar(0.8 + rand() * 0.7);
            g.add(piece);
        }
        foreach (var side in new[] { -1, 1 })
        {
            var plant = SeaFlora.kelp(rand);
            plant.position.set(side * 5.6, -0.05, 1.2);
            g.add(plant);
            sway(plant, rand() * 6, 0.07);
        }
        g.position.set(cx, floorAt(cx, cz), cz);
        g.rotation.y = 0.35;
        group.add(g);
    }

    /// <summary>The ship's cargo: tall clay jars spilled on the sand round the wreck, some whole and some lying on their sides.</summary>
    private void buildJars((double x, double z) wreck)
    {
        var clay = new[] { 0xb8774a, 0xa8694a, 0xc08a5c }.Select(k => new THREE.MeshStandardMaterial { color = k, roughness = 0.9, side = THREE.Side.DoubleSide }).ToArray();
        var profile = new[] { (0.0, 0.0), (0.07, 0.0), (0.16, 0.12), (0.24, 0.36), (0.25, 0.52), (0.19, 0.7), (0.09, 0.82), (0.075, 0.95), (0.11, 1.0), (0.085, 1.0) }
            .Select(p => new THREE.Vector2(p.Item1, p.Item2)).ToList();
        var jar = new THREE.LatheGeometry(profile, 16);
        var handle = new THREE.TorusGeometry(0.09, 0.02, 5, 10, Math.PI);
        foreach (var (dx, dz, lying) in new[] { (-7.5, 3.5, false), (-6.6, 4.3, true), (6.5, 4.0, true), (8.2, -3.0, false), (2.5, 6.0, true), (-2.0, -6.2, false), (-3.0, -5.4, true) })
        {
            var g = new THREE.Group();
            var mat = clay[(int)Math.Floor(rand() * clay.Length)];
            g.add(new THREE.Mesh(jar, mat));
            foreach (var side in new[] { -1, 1 })
            {
                var h = new THREE.Mesh(handle, mat);
                h.position.set(side * 0.13, 0.82, 0);
                h.rotation.z = -side * Math.PI / 2;
                g.add(h);
            }
            double x = wreck.x + dx, z = wreck.z + dz;
            g.scale.setScalar(0.8 + rand() * 0.35);
            g.rotation.set(lying ? 1.35 + rand() * 0.2 : (rand() - 0.5) * 0.25, rand() * 6, 0);
            g.position.set(x, floorAt(x, z) + (lying ? 0.17 : -0.08), z);
            group.add(g);
        }
    }

    // ---------------------------------------------------------------- life

    /// <summary>Shoals of small silver fish. Each shoal wanders the reef as one body, and bends away from a diver.</summary>
    private void buildShoals()
    {
        var looks = new[] { (0x8fa9bd, 0xf4f8fa, 0x5f7c93), (0xd8b84a, 0xfff3c2, 0xb07a1e), (0x6bb7c9, 0xeafcff, 0x2f7f9a) };
        var mat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.3, metalness = 0.25, side = THREE.Side.DoubleSide };
        for (int s = 0; s < 4; s++)
        {
            var look = looks[s % looks.Length];
            var model = Underwater.fishModel(look.Item1, look.Item2, look.Item3, 0);
            var tail = model.tail.clone();
            tail.translate(0, 0, -0.42);
            var geo = THREE.BufferGeometryUtils.mergeGeometries(new List<THREE.BufferGeometry> { model.body, tail }, false);
            var shoal = new Shoal { heading = rand() * 6.28, speed = 1.0 + rand() * 0.5 };
            shoal.x = UW.x + (rand() - 0.5) * UW.half * 1.4;
            shoal.z = UW.z + (rand() - 0.5) * UW.half * 1.4;
            shoal.y = shoal.ty = floorAt(shoal.x, shoal.z) + 2.5 + rand() * 3;
            int count = 22 + (int)Math.Floor(rand() * 10);
            for (int i = 0; i < count; i++)
            {
                var m = new THREE.Mesh(geo, mat);
                m.scale.setScalar(0.3 + rand() * 0.12);
                group.add(m);
                // a long, flattened cloud of fish
                shoal.fish.Add((m, (rand() - 0.5) * 2.4, (rand() - 0.5) * 1.3, (rand() - 0.5) * 4.6, rand() * 6));
            }
            shoals.Add(shoal);
        }
    }

    /// <summary>Jellyfish: a clear bell that pulses, with a few trailing threads, rising and sinking slowly in mid-water.</summary>
    private void buildJellies()
    {
        var bell = new THREE.SphereGeometry(0.34, 18, 10, 0, Math.PI * 2, 0, Math.PI / 2);
        var skirt = new THREE.CylinderGeometry(0.33, 0.27, 0.1, 18, 1, true);
        var thread = new THREE.CylinderGeometry(0.012, 0.003, 1, 5);
        thread.translate(0, -0.5, 0);
        var tints = new[] { (0xf3b6d8, 0x8a2f66), (0xb9d8ff, 0x2a4f9a), (0xe9d2ff, 0x5a2f9a) };
        for (int i = 0; i < 12; i++)
        {
            var (tint, glow) = tints[i % tints.Length];
            var clear = new THREE.MeshStandardMaterial { color = tint, emissive = glow, emissiveIntensity = 0.5, roughness = 0.2, transparent = true, opacity = 0.5, side = THREE.Side.DoubleSide, depthWrite = false };
            var core = new THREE.MeshStandardMaterial { color = tint, emissive = glow, emissiveIntensity = 0.9, roughness = 0.4, transparent = true, opacity = 0.8 };
            var root = new THREE.Group();
            root.add(new THREE.Mesh(bell, clear));
            var rim = new THREE.Mesh(skirt, clear);
            rim.position.y = -0.04;
            root.add(rim);
            var heart = new THREE.Mesh(new THREE.SphereGeometry(0.13, 10, 8), core);
            heart.position.y = 0.08;
            heart.scale.y = 0.7;
            root.add(heart);
            for (int k = 0; k < 7; k++)
            {
                var t = new THREE.Mesh(thread, core);
                double a = k / 7.0 * Math.PI * 2;
                t.position.set(Math.Cos(a) * 0.2, -0.05, Math.Sin(a) * 0.2);
                t.scale.y = 0.7 + rand() * 0.9;
                root.add(t);
            }
            root.scale.setScalar(0.8 + rand() * 0.9);
            group.add(root);
            var j = new Jelly { root = root, phase = rand() * 6, drift = rand() * 6 };
            j.x = UW.x + (rand() - 0.5) * UW.half * 1.7;
            j.z = UW.z + (rand() - 0.5) * UW.half * 1.7;
            double floor = floorAt(j.x, j.z);
            j.baseY = floor + 2.2 + rand() * Math.Max(1, -floor - 5);
            j.y = j.baseY;
            jellies.Add(j);
        }
    }

    // ---------------------------------------------------------------- every frame

    public void update(double dt, double t, THREE.Vector3 cam)
    {
        // the two sheets of light slide across each other
        causticA.offset.set(t * 0.021, t * 0.013);
        causticB.offset.set(-t * 0.012 + 0.37, t * 0.017 + 0.11);
        foreach (var m in causticMats) m.opacity = 0.2 * daylight;
        // the shimmer under the surface stays with the diver, and its pattern stays where it is in the world
        shimmer.position.set(cam.x, Underwater.UW_SURFACE - 0.03, cam.z);
        causticTop.offset.set(cam.x / SHIMMER_TILE + t * 0.03, cam.z / SHIMMER_TILE + t * 0.02);
        ((THREE.MeshBasicMaterial)shimmer.material).opacity = 0.15 * daylight;
        sunPatch.position.set(cam.x + 5, Underwater.UW_SURFACE - 0.4, cam.z - 7);
        sunPatch.material.opacity = 0.55 * daylight;

        foreach (var r in rays)
        {
            double d = JS.Hypot(r.x - cam.x, r.z - cam.z);
            if (d > RAY_REACH)
            {
                // left behind: it comes down again somewhere ahead, and fades in
                double a = JS.Random() * Math.PI * 2, rad = RAY_REACH * (0.55 + JS.Random() * 0.4);
                r.x = cam.x + Math.Cos(a) * rad;
                r.z = cam.z + Math.Sin(a) * rad;
                r.born = t;
                d = rad;
            }
            double fade = Math.Min(1, (t - r.born) / 2.5) * Math.Min(1, Math.Max(0, (d - 2.5) / 4));
            double flicker = 0.65 + 0.35 * Math.Sin(t * 0.6 + r.phase);
            ((THREE.MeshBasicMaterial)r.mesh.material).opacity = r.strength * fade * flicker * daylight;
            r.mesh.position.set(r.x, Underwater.UW_SURFACE, r.z);
            // turned to face the diver, and all slanting the same way, as the sun stands
            r.mesh.rotation.y = Math.Atan2(cam.x - r.x, cam.z - r.z);
            r.mesh.rotation.z = 0.24 + Math.Sin(t * 0.25 + r.phase) * 0.03;
        }

        foreach (var s in shoals)
        {
            if (JS.Random() < dt * 0.3) s.turn = (JS.Random() - 0.5) * 0.9;
            double speed = s.speed;
            double dx = cam.x - s.x, dz = cam.z - s.z;
            double near = JS.Hypot(dx, dz, cam.y - s.y);
            if (near < 5.5)
            {
                double away = Math.Atan2(-dx, -dz) - s.heading;
                s.heading += Math.Atan2(Math.Sin(away), Math.Cos(away)) * Math.Min(1, dt * 2.5);
                speed *= 2.6;
            }
            else
            {
                s.heading += s.turn * dt;
                double lx = s.x - UW.x, lz = s.z - UW.z;
                if (Math.Abs(lx) > UW.half - 8 || Math.Abs(lz) > UW.half - 8)
                {
                    double back = Math.Atan2(-lx, -lz) - s.heading;
                    s.heading += Math.Atan2(Math.Sin(back), Math.Cos(back)) * Math.Min(1, dt * 1.5);
                }
            }
            s.x += Math.Sin(s.heading) * speed * dt;
            s.z += Math.Cos(s.heading) * speed * dt;
            double floor = floorAt(s.x, s.z);
            if (JS.Random() < dt * 0.15) s.ty = floor + 2.2 + JS.Random() * Math.Max(1, -floor - 5);
            s.ty = Math.Min(Underwater.UW_SURFACE - 2, Math.Max(floor + 2, s.ty));
            s.y += (s.ty - s.y) * Math.Min(1, dt * 0.5);
            double ch = Math.Cos(s.heading), sh = Math.Sin(s.heading);
            foreach (var (mesh, ox, oy, oz, phase) in s.fish)
            {
                // each keeps its place in the shoal, give or take a little wandering of its own
                double wx = ox + Math.Sin(t * 0.7 + phase) * 0.25, wy = oy + Math.Sin(t * 0.9 + phase * 1.7) * 0.15, wz = oz + Math.Cos(t * 0.5 + phase) * 0.4;
                mesh.position.set(s.x + wx * ch + wz * sh, s.y + wy, s.z - wx * sh + wz * ch);
                mesh.rotation.y = s.heading + Math.Sin(t * 6 + phase) * 0.12;
            }
        }

        foreach (var j in jellies)
        {
            double beat = Math.Sin(t * 1.5 + j.phase);
            j.root.scale.x = j.root.scale.z = j.root.scale.y * (1 + beat * 0.1) / (1 - beat * 0.02);
            j.x += Math.Sin(t * 0.07 + j.drift) * 0.12 * dt;
            j.z += Math.Cos(t * 0.06 + j.drift) * 0.12 * dt;
            j.y = j.baseY + Math.Sin(t * 0.22 + j.phase) * 0.9;
            j.root.position.set(j.x, j.y, j.z);
            j.root.rotation.z = Math.Sin(t * 0.4 + j.phase) * 0.12;
        }
    }

    // ---------------------------------------------------------------- the wreck's hull

    /// <summary>
    /// What is left of the wreck's planking, in the wreck's own space (keel along x): a shell that follows the ribs from the keel
    /// up both sides, with a ragged top edge, holes rotted through it and one side stove in.
    /// </summary>
    public static THREE.Mesh hullShell()
    {
        const int NX = 30, NT = 16;
        double hash(int a, int b)
        {
            double v = Math.Sin(a * 127.1 + b * 311.7) * 43758.5453;
            return v - Math.Floor(v);
        }
        var pos = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i <= NX; i++)
        {
            double x = -5 + 10.0 * i / NX;
            double r = 0.6 + (1 - Math.Pow(Math.Abs(x) / 5.4, 2.2)) * 1.4;
            for (int j = 0; j <= NT; j++)
            {
                double th = -1.8 + 3.6 * j / NT;
                pos.Add(x);
                pos.Add(r * 0.95 - (r + 0.07) * Math.Cos(th));
                pos.Add((r + 0.07) * Math.Sin(th));
            }
        }
        for (int i = 0; i < NX; i++)
        {
            // how far up the side the planking still reaches here
            double reach = 1.0 + 0.75 * hash(i / 2, 7);
            for (int j = 0; j < NT; j++)
            {
                double th = -1.8 + 3.6 * (j + 0.5) / NT;
                if (Math.Abs(th) > reach) continue;
                if (hash(i, j) < 0.06) continue;
                // the hole in her side, where the reef came in
                if (th > 0.25 && i >= 11 && i <= 16) continue;
                int a = i * (NT + 1) + j, b = a + NT + 1;
                idx.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        geo.setIndex(idx);
        geo.computeVertexNormals();
        return new THREE.Mesh(geo, new THREE.MeshStandardMaterial { color = 0x64503c, roughness = 1, side = THREE.Side.DoubleSide, flatShading = true });
    }
}
