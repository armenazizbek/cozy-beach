using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Island.ts

/// <summary>
/// A small island out at sea, reached only by boat. Its terrain is part of the world's height function (see Beach.cs), so the boat,
/// the player, the maps and every find already know where the land is; this class builds what you see and what you can bump into.
/// There are three, each of its own kind: the green island (palms, bushes, boulders, an old stone ruin on the hilltop and a wrecked
/// boat on the sand), the lagoon cay (a ring of pale sand with a castaway's camp on its dune) and the ember rock (a dark volcanic
/// cone with basalt columns at its foot and a lighthouse on its summit).
/// </summary>
public class Island
{
    public class Spot { public double x, z, r; }

    public class FindSpot { public string id; public double x, z; }

    private static Func<double> rng(double seed) => () =>
    {
        seed = (seed * 1664525 + 1013904223) % 4294967296;
        return seed / 4294967296;
    };

    private static double smooth(double a, double b, double x)
    {
        double t = Math.Min(1, Math.Max(0, (x - a) / (b - a)));
        return t * t * (3 - 2 * t);
    }

    private static THREE.BufferGeometry jitter(THREE.BufferGeometry geo, double amount, Func<double> rand)
    {
        var p = geo.attributes.position;
        var seen = new Dictionary<(double, double, double), double[]>();
        for (int i = 0; i < p.count; i++)
        {
            var key = (JS.Round(p.getX(i) * 400), JS.Round(p.getY(i) * 400), JS.Round(p.getZ(i) * 400));
            if (!seen.TryGetValue(key, out var d))
            {
                d = new[] { (rand() - 0.5) * amount, (rand() - 0.5) * amount, (rand() - 0.5) * amount };
                seen[key] = d;
            }
            p.setXYZ(i, p.getX(i) + d[0], p.getY(i) + d[1], p.getZ(i) + d[2]);
        }
        geo.computeVertexNormals();
        return geo;
    }

    private static double slopeAt(double x, double z)
    {
        const double e = 0.5;
        double nx = baseHeight(x - e, z) - baseHeight(x + e, z);
        double nz = baseHeight(x, z - e) - baseHeight(x, z + e);
        var n = new THREE.Vector3(nx, 2 * e, nz).normalize();
        return 1 - n.y;
    }

    public readonly IslandSpec spec;
    /// <summary>0 the green island, 1 the lagoon cay, 2 the ember rock</summary>
    public readonly int kind;
    public readonly THREE.Group group = new THREE.Group();
    /// <summary>solid circles for walking around: trunks, boulders, ruin pillars, the wreck</summary>
    public readonly List<Spot> colliders = new List<Spot>();
    /// <summary>the island's landmark: the old stone circle on the summit, the castaway's camp, or the crater</summary>
    public readonly (double x, double z) ruins;
    /// <summary>how far around the landmark the palms, bushes and boulders keep away</summary>
    private readonly double clearR;
    private readonly List<(THREE.Group crown, double phase, List<THREE.Mesh> fronds, THREE.Group nuts)> palms = new List<(THREE.Group, double, List<THREE.Mesh>, THREE.Group)>();
    private readonly THREE.Group shrubs = new THREE.Group();
    private readonly List<THREE.Mesh> foam = new List<THREE.Mesh>();
    private (double x, double z) wreck = (0, 0);
    /// <summary>the old map lying on the altar until someone takes it</summary>
    private readonly THREE.Group scroll = new THREE.Group();
    private double scrollY = 0;
    private readonly Func<double> rand;
    /// <summary>the rag on the camp's signal mast</summary>
    private THREE.Object3D pennant = null;
    /// <summary>the ember rock's lighthouse; null on the other islands</summary>
    public Lighthouse lighthouse = null;
    /// <summary>what lies in the open here, by item id; no other island has the same finds</summary>
    public readonly string[] findIds;
    /// <summary>every material on the island, with its own colour, so distance haze can be applied by hand</summary>
    private readonly List<(THREE.Material mat, THREE.Color @base)> haze = new List<(THREE.Material, THREE.Color)>();

    public Island(IslandSpec spec = null)
    {
        this.spec = spec ?? ISLAND;
        kind = this.spec.kind;
        rand = rng(4711 + kind * 977);
        scroll.visible = false;
        if (kind == 1)
        {
            findIds = new[] { "spyglass", "nautilus", "red_coral" };
            ruins = highestAlong(LAGOON_GAP + Math.PI, 0.4, 0.9);
            clearR = 5.5;
            buildTerrain();
            buildFoam();
            buildPalms();
            group.add(shrubs);
            buildShrubs();
            buildRocks();
            buildCamp();
            prepareHaze();
        }
        else if (kind == 2)
        {
            findIds = new[] { "sun_disc", "fire_opal", "obsidian_blade" };
            ruins = (this.spec.x, this.spec.z);
            clearR = 6.5;
            buildTerrain();
            buildFoam();
            buildPalms();
            group.add(shrubs);
            buildShrubs();
            buildRocks();
            buildColumns();
            buildSnags();
            buildCairn();
            buildLighthouse();
            prepareHaze();
            // the lamp and its beams are added after: they shine by their own light, and the haze must not grey them
            lighthouse.buildLight(group);
        }
        else
        {
            findIds = new[] { "stone_idol", "black_pearl", "conch", "piece_eight" };
            ruins = (this.spec.x + 1, this.spec.z - 1);
            clearR = 5;
            buildTerrain();
            buildFoam();
            buildPalms();
            group.add(shrubs);
            buildShrubs();
            buildRocks();
            buildRuins();
            scroll.visible = true;
            buildScroll();
            buildWreck();
            prepareHaze();
        }
    }

    /// <summary>The highest ground along one direction from the middle, between two fractions of the island's radius.</summary>
    private (double x, double z) highestAlong(double a, double from, double to)
    {
        double bestR = spec.R * from, bestH = double.NegativeInfinity;
        for (double r = spec.R * from; r <= spec.R * to; r += 0.25)
        {
            double h = baseHeight(spec.x + Math.Cos(a) * r, spec.z + Math.Sin(a) * r);
            if (h > bestH) { bestH = h; bestR = r; }
        }
        return (spec.x + Math.Cos(a) * bestR, spec.z + Math.Sin(a) * bestR);
    }

    /// <summary>
    /// The island is far out, and the scene's fog would hide it completely at dawn, dusk and night. So its materials ignore the fog
    /// and are tinted toward the fog colour by hand instead, with a cap, so it always stays visible as a hazy shape.
    /// </summary>
    private void prepareHaze()
    {
        var seen = new HashSet<THREE.Material>();
        group.traverse(o =>
        {
            if (!(o is THREE.Mesh mesh)) return;
            foreach (var m in mesh.materials ?? new[] { mesh.material })
            {
                if (m == null || seen.Contains(m)) continue;
                seen.Add(m);
                if (m is THREE.MeshStandardMaterial s)
                {
                    s.fog = false;
                    haze.Add((s, s.color.clone()));
                }
                else if (m is THREE.MeshBasicMaterial b)
                {
                    b.fog = false;
                    haze.Add((b, b.color.clone()));
                }
            }
        });
    }

    public bool contains(double x, double z) => JS.Hypot(x - spec.x, z - spec.z) < spec.R * 1.1;

    /// <summary>Where a boat at (bx, bz) can put someone ashore, heading straight for the island.</summary>
    public (double x, double z)? landingFrom(double bx, double bz)
    {
        double dx = spec.x - bx;
        double dz = spec.z - bz;
        double L = JS.Hypot(dx, dz);
        if (L > spec.R * 1.2 + 30) return null;
        if (kind == 1)
        {
            // a ring has no land in its middle, and a boat may be lying inside it: the nearest shore, whichever way it is
            (double x, double z)? best = null;
            double bestS = 10;
            for (int k = 0; k < 24; k++)
            {
                double a = k / 24.0 * Math.PI * 2, cx = Math.Cos(a), cz = Math.Sin(a);
                for (double s = 0; s < bestS; s += 0.4)
                {
                    if (baseHeight(bx + cx * s, bz + cz * s) <= WATER_LEVEL + 0.3) continue;
                    bestS = s;
                    best = (bx + cx * (s + 1.0), bz + cz * (s + 1.0));
                    break;
                }
            }
            return best;
        }
        double ux = dx / JS.Or(L, 1);
        double uz = dz / JS.Or(L, 1);
        for (double s = 0; s < L; s += 0.4)
        {
            double x = bx + ux * s;
            double z = bz + uz * s;
            if (baseHeight(x, z) > WATER_LEVEL + 0.3) return (x + ux * 1.0, z + uz * 1.0);
        }
        return null;
    }

    // ---------------------------------------------------------------- terrain

    /// <summary>Smooth value noise in 0..1, about one bump per `cell` metres; the same at the same spot every time.</summary>
    private static double vnoise(double x, double z, double cell, double seed)
    {
        double fx = x / cell, fz = z / cell;
        int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz);
        double tx = fx - ix, tz = fz - iz;
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        double h(int i, int j)
        {
            double v = Math.Sin(i * 127.1 + j * 311.7 + seed * 74.7) * 43758.5453;
            return v - Math.Floor(v);
        }
        double a = h(ix, iz) + (h(ix + 1, iz) - h(ix, iz)) * tx;
        double b = h(ix, iz + 1) + (h(ix + 1, iz + 1) - h(ix, iz + 1)) * tx;
        return a + (b - a) * tz;
    }

    /// <summary>Two sizes of noise together: broad patches with smaller ones inside them.</summary>
    private static double patches(double x, double z, double seed) =>
        vnoise(x, z, 6, seed) * 0.65 + vnoise(x, z, 1.8, seed + 3) * 0.35;

    private void buildTerrain()
    {
        double half = spec.R * 1.25;
        // finer than the coarse grid it once used, so where the sand meets the grass, and the grass the rock, is a clear
        // line and not a smear across a metre
        const double step = 0.4;
        int n = (int)Math.Ceiling(2 * half / step);
        var geo = new THREE.BufferGeometry();
        var pos = new float[(n + 1) * (n + 1) * 3];
        var col = new float[(n + 1) * (n + 1) * 3];
        var uv = new float[(n + 1) * (n + 1) * 2];
        var contrib = new List<bool>();
        var c = new THREE.Color();
        // the same sand as the main beach, so the island's beach looks like the one you sailed from
        var sandDry = new THREE.Color(kind == 1 ? "#f3e6c4" : kind == 2 ? "#5b5650" : "#e6cfa0");
        var sandWet = new THREE.Color(kind == 1 ? "#c9ad86" : kind == 2 ? "#35322f" : "#b08968");
        var floor = new THREE.Color(kind == 1 ? 0xa9cdb6 : kind == 2 ? 0x41585a : 0x6f9a8a);
        // the lagoon cay: thin beach grass along the top of the ring
        var duneLight = new THREE.Color(0xa9bd62);
        var duneDark = new THREE.Color(0x7d9a4a);
        // the ember rock: basalt, scrub clinging to the lower cone, rust near the rim and sulphur in the crater
        var basaltLight = new THREE.Color(0x56514c);
        var basaltDark = new THREE.Color(0x2f2c2a);
        var scrubLight = new THREE.Color(0x6f8246);
        var scrubDark = new THREE.Color(0x4f6638);
        var rust = new THREE.Color(0x80503a);
        var sulphur = new THREE.Color(0xb3a658);
        var grassLight = new THREE.Color(0x7fb04a);
        var grassMid = new THREE.Color(0x5f9a3a);
        var grassDark = new THREE.Color(0x467f33);
        var dirt = new THREE.Color(0x8f7650);
        var rockLight = new THREE.Color(0x8d877c);
        var rockDark = new THREE.Color(0x67625a);
        var g = new THREE.Color();
        for (int j = 0; j <= n; j++)
        {
            for (int i = 0; i <= n; i++)
            {
                double x = spec.x - half + i * step;
                double z = spec.z - half + j * step;
                double h = Math.Max(baseHeight(x, z), -2.2);
                int k = (j * (n + 1) + i) * 3;
                pos[k] = (float)x;
                pos[k + 1] = (float)h;
                pos[k + 2] = (float)z;
                uv[(j * (n + 1) + i) * 2] = (float)(x / 1.6);
                uv[(j * (n + 1) + i) * 2 + 1] = (float)(z / 1.6);
                contrib.Add(islandHeight(x, z) > baseHeightMain(x, z) + 0.01);
                double slope = slopeAt(x, z);
                if (h < WATER_LEVEL)
                {
                    c.copy(sandWet).lerp(floor, Math.Min(1, (WATER_LEVEL - h) / 1.4));
                }
                else if (kind == 1)
                {
                    c.copy(sandWet).lerp(sandDry, smooth(WATER_LEVEL, 0.25, h));
                    c.offsetHSL(0, 0, (patches(x, z, 1) - 0.5) * 0.04);
                    // beach grass only on the crest, in loose patches with sand showing through
                    double p = patches(x, z, 2);
                    g.copy(duneDark).lerp(duneLight, p);
                    double edge = 1.45 + (patches(x, z, 7) - 0.5) * 0.9;
                    c.lerp(g, smooth(edge - 0.1, edge + 0.12, h) * smooth(0.3, 0.55, patches(x, z, 5)) * 0.85);
                }
                else if (kind == 2)
                {
                    c.copy(sandWet).lerp(sandDry, smooth(WATER_LEVEL, 0.3, h));
                    c.offsetHSL(0, 0, (patches(x, z, 1) - 0.5) * 0.04);
                    double tc = JS.Hypot(x - spec.x, z - spec.z) / spec.R;
                    // rock from where the cone lifts off the sand
                    double r = patches(x, z, 9);
                    g.copy(basaltDark).lerp(basaltLight, r);
                    c.lerp(g, Math.Max(smooth(1.15, 1.7, h), smooth(0.3, 0.45, slope)));
                    // scrub in patches on the lower cone
                    double p = patches(x, z, 2);
                    g.copy(scrubDark).lerp(scrubLight, p);
                    c.lerp(g, smooth(1.3, 1.9, h) * (1 - smooth(3.8, 5.4, h)) * smooth(0.5, 0.64, patches(x, z, 5)) * 0.9);
                    // rust-red toward the summit
                    c.lerp(rust, smooth(5.2, 7.8, h) * (0.35 + 0.45 * r));
                }
                else
                {
                    // sand: damp at the water, dry above it, with faint ripples of colour
                    c.copy(sandWet).lerp(sandDry, smooth(WATER_LEVEL, 0.3, h));
                    c.offsetHSL(0, 0, (patches(x, z, 1) - 0.5) * 0.05);
                    // grass: patches of lighter and darker green, and a few bare dirt spots
                    double p = patches(x, z, 2);
                    g.copy(grassMid).lerp(p > 0.5 ? grassLight : grassDark, Math.Abs(p - 0.5) * 1.6);
                    g.lerp(dirt, smooth(0.68, 0.8, patches(x, z, 5)) * 0.7);
                    // where the grass begins wanders up and down the slope, and is a fairly sharp edge
                    double edge = 0.55 + (patches(x, z, 7) - 0.5) * 0.7;
                    c.lerp(g, smooth(edge - 0.08, edge + 0.08, h));
                    // rock only where it is really steep (the cliff), and the bare stone of the summit
                    double r = patches(x, z, 9);
                    g.copy(rockDark).lerp(rockLight, r);
                    double bare = Math.Max(smooth(0.3, 0.45, slope), smooth(3.3, 3.65, h) * smooth(0.35, 0.6, r));
                    c.lerp(g, bare);
                }
                col[k] = (float)c.r;
                col[k + 1] = (float)c.g;
                col[k + 2] = (float)c.b;
            }
        }
        var idx = new List<int>();
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i;
                int b = a + 1;
                int d = a + n + 1;
                int e = d + 1;
                if (!(contrib[a] || contrib[b] || contrib[d] || contrib[e])) continue;
                idx.AddRange(new[] { a, d, b, b, d, e });
            }
        }
        geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
        geo.setAttribute("color", new THREE.BufferAttribute(col, 3));
        geo.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
        geo.setIndex(idx);
        geo.computeVertexNormals();
        // it overlaps the coarse sea floor under it, and wins where they touch. The sand grain of the beach gives the ground
        // a texture up close, instead of plain smooth colour
        var mat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1, bumpMap = grainTexture(1), bumpScale = 1.2, polygonOffset = true, polygonOffsetFactor = -2, polygonOffsetUnits = -2 };
        var mesh = new THREE.Mesh(geo, mat);
        mesh.receiveShadow = true;
        mesh.castShadow = true;
        group.add(mesh);
    }

    /// <summary>A band of foam along the whole coast, found by walking out from the centre to where the land meets the water.</summary>
    private void buildFoam()
    {
        const int steps = 160;
        var rings = new[] { (-0.25, 0.35, 0.55), (-0.6, 1.5, 0.2) };
        var shore = new List<double>();
        for (int k = 0; k < steps; k++)
        {
            double a = (double)k / steps * Math.PI * 2;
            double r = 4;
            if (kind == 1)
            {
                // a ring: its middle is water, so the coast is found from the outside in. Nothing is found where the gap is
                r = spec.R * 1.3;
                while (r > 2 && baseHeight(spec.x + Math.Cos(a) * r, spec.z + Math.Sin(a) * r) <= WATER_LEVEL) r -= 0.25;
                shore.Add(r > 2 ? r : double.NaN);
                continue;
            }
            while (r < spec.R * 1.3 && baseHeight(spec.x + Math.Cos(a) * r, spec.z + Math.Sin(a) * r) > WATER_LEVEL) r += 0.25;
            shore.Add(r);
        }
        foreach (var (inner, outer, opacity) in rings)
        {
            var pos = new List<double>();
            var idx = new List<int>();
            for (int k = 0; k <= steps; k++)
            {
                double a = (double)(k % steps) / steps * Math.PI * 2;
                double r = shore[k % steps];
                bool open = double.IsNaN(r) || double.IsNaN(shore[(k + steps - 1) % steps]);
                if (double.IsNaN(r)) r = spec.R;
                pos.AddRange(new[] { spec.x + Math.Cos(a) * (r + inner), 0, spec.z + Math.Sin(a) * (r + inner) });
                pos.AddRange(new[] { spec.x + Math.Cos(a) * (r + outer), 0, spec.z + Math.Sin(a) * (r + outer) });
                if (k > 0 && !open)
                {
                    int b = (k - 1) * 2;
                    idx.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
                }
            }
            var g = new THREE.BufferGeometry();
            g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
            g.setIndex(idx);
            var m = new THREE.Mesh(g, new THREE.MeshBasicMaterial { color = 0xffffff, transparent = true, opacity = opacity, depthWrite = false, side = THREE.Side.DoubleSide, fog = true });
            m.userData["base"] = opacity;
            m.renderOrder = 3;
            group.add(m);
            foam.Add(m);
        }
    }

    // ---------------------------------------------------------------- things standing on it

    /// <summary>A random spot on dry land that is not too steep, or null after a number of tries.</summary>
    private (double x, double z)? landSpot(double minH, double maxH, double maxSlope, List<Spot> avoid, double gap, double rMin = 3, double rMax = double.NaN)
    {
        if (double.IsNaN(rMax)) rMax = spec.R * 0.8;
        for (int tries = 0; tries < 120; tries++)
        {
            double a = rand() * Math.PI * 2;
            double r = rMin + rand() * (rMax - rMin);
            double x = spec.x + Math.Cos(a) * r;
            double z = spec.z + Math.Sin(a) * r;
            double h = baseHeight(x, z);
            if (h < minH || h > maxH || slopeAt(x, z) > maxSlope) continue;
            if (JS.Hypot(x - ruins.x, z - ruins.z) < clearR) continue;
            if (avoid.Any(s => JS.Hypot(s.x - x, s.z - z) < s.r + gap)) continue;
            return (x, z);
        }
        return null;
    }

    private THREE.BufferGeometry frondGeometry()
    {
        var geo = new THREE.PlaneGeometry(0.7, 3.4, 3, 10);
        var p = geo.attributes.position;
        for (int i = 0; i < p.count; i++)
        {
            double x = p.getX(i);
            double t = (p.getY(i) + 1.7) / 3.4;
            double taper = Math.Sin(Math.PI * Math.Min(1, t * 1.05 + 0.02)) * 0.85 + 0.15;
            p.setX(i, x * taper);
            p.setZ(i, -t * t * 1.4 + Math.Abs(x) * 0.25);
            p.setY(i, p.getY(i) + 1.7);
        }
        geo.computeVertexNormals();
        return geo;
    }

    private void buildPalms()
    {
        var frond = frondGeometry();
        // a lusher, deeper green than the beach palms
        var frondMats = (kind == 2 ? new[] { 0x5f7f3a, 0x6c8a42, 0x52702f } : kind == 1 ? new[] { 0x3f9a44, 0x4caa4e, 0x34863a } : new[] { 0x2f8a3a, 0x3c9a44, 0x28762f }).Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.85, side = THREE.Side.DoubleSide }).ToArray();
        var trunkMat = new THREE.MeshStandardMaterial { color = 0x7a5a3a, roughness = 1 };
        var nutMat = new THREE.MeshStandardMaterial { color = 0x5a3a1e, roughness = 0.7 };
        var placed = new List<Spot>();
        int palmCount = kind == 1 ? 15 : kind == 2 ? 5 : 17;
        for (int n = 0; n < palmCount; n++)
        {
            var found = kind == 1 ? landSpot(0.7, 3.4, 0.25, placed, 3.0, spec.R * 0.45, spec.R * 0.95)
                : kind == 2 ? landSpot(0.45, 1.3, 0.2, placed, 4.0, spec.R * 0.5, spec.R * 0.9)
                : landSpot(0.8, 3.1, 0.2, placed, 3.2);
            if (found == null) continue;
            var spot = found.Value;
            placed.Add(new Spot { x = spot.x, z = spot.z, r = 0.4 });
            var g = new THREE.Group();
            double h = 5 + rand() * 3;
            double lean = (rand() - 0.5) * 2.6;
            double dir = rand() * Math.PI * 2;
            var curve = new THREE.QuadraticBezierCurve3(
                new THREE.Vector3(0, 0, 0),
                new THREE.Vector3(Math.Cos(dir) * lean * 0.2, h * 0.55, Math.Sin(dir) * lean * 0.2),
                new THREE.Vector3(Math.Cos(dir) * lean, h, Math.Sin(dir) * lean));
            var trunk = new THREE.Mesh(new THREE.TubeGeometry(curve, 12, 0.2, 7), trunkMat);
            trunk.castShadow = true;
            g.add(trunk);
            var crown = new THREE.Group();
            crown.position.copy(curve.getPoint(1));
            var frondMeshes = new List<THREE.Mesh>();
            var nutGroup = new THREE.Group();
            crown.add(nutGroup);
            for (int i = 0; i < 9; i++)
            {
                var f = new THREE.Mesh(frond, frondMats[i % 3]);
                f.rotation.order = "YXZ";
                f.rotation.y = i / 9.0 * Math.PI * 2 + rand() * 0.3;
                f.rotation.x = Math.PI / 2 - (0.35 + rand() * 0.4);
                f.scale.setScalar(0.95 + rand() * 0.4);
                f.userData["s0"] = f.scale.x;
                f.castShadow = true;
                crown.add(f);
                frondMeshes.Add(f);
            }
            for (int i = 0; i < 4; i++)
            {
                var nut = new THREE.Mesh(new THREE.SphereGeometry(0.17, 8, 6), nutMat);
                double a = i / 4.0 * Math.PI * 2 + rand();
                nut.position.set(Math.Cos(a) * 0.22, -0.28, Math.Sin(a) * 0.22);
                nutGroup.add(nut);
            }
            g.add(crown);
            g.position.set(spot.x, baseHeight(spot.x, spot.z) - 0.1, spot.z);
            g.rotation.y = rand() * Math.PI * 2;
            group.add(g);
            palms.Add((crown, rand() * 6, frondMeshes, nutGroup));
            colliders.Add(new Spot { x = spot.x, z = spot.z, r = 0.4 });
        }
    }

    private void buildShrubs()
    {
        var mats = (kind == 2 ? new[] { 0x6a7a3c, 0x7d8848, 0x55662f } : kind == 1 ? new[] { 0x5f9a44, 0x74ac50, 0x4c8438 } : new[] { 0x3f8036, 0x4f9a3d, 0x2f6e2c }).Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.95, flatShading = true }).ToArray();
        int shrubCount = kind == 1 ? 22 : kind == 2 ? 16 : 34;
        for (int n = 0; n < shrubCount; n++)
        {
            var found = kind == 1 ? landSpot(1.0, 3.4, 0.3, new List<Spot>(), 0, spec.R * 0.45, spec.R * 0.95)
                : kind == 2 ? landSpot(0.5, 3.6, 0.45, new List<Spot>(), 0, spec.R * 0.3, spec.R * 0.88)
                : landSpot(0.55, 3.0, 0.3, new List<Spot>(), 0, 3, spec.R * 0.82);
            if (found == null) continue;
            var spot = found.Value;
            var clump = new THREE.Group();
            int parts = 2 + (int)Math.Floor(rand() * 3);
            for (int i = 0; i < parts; i++)
            {
                var s = jitter(new THREE.IcosahedronGeometry(0.32 + rand() * 0.25, 1), 0.12, rand);
                var m = new THREE.Mesh(s, mats[(int)Math.Floor(rand() * 3)]);
                // the arguments are evaluated left to right, as in the browser
                double px = (rand() - 0.5) * 0.7, py = 0.2 + rand() * 0.1, pz = (rand() - 0.5) * 0.7;
                m.position.set(px, py, pz);
                m.scale.y = 0.65;
                m.castShadow = true;
                clump.add(m);
            }
            clump.position.set(spot.x, baseHeight(spot.x, spot.z) - 0.05, spot.z);
            shrubs.add(clump);
        }
    }

    private void buildRocks()
    {
        var mats = (kind == 2 ? new[] { 0x3b3835, 0x2c2a28, 0x4a4641 } : kind == 1 ? new[] { 0xb3aa98, 0x9f9686, 0xc4bca9 } : new[] { 0x77726a, 0x65605a, 0x8a847a }).Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.95, flatShading = true }).ToArray();
        var placed = new List<Spot>();
        int rockCount = kind == 1 ? 6 : kind == 2 ? 24 : 14;
        for (int n = 0; n < rockCount; n++)
        {
            // mostly on the cliff and the steeper ground, some on the sand
            var found = kind == 1 ? landSpot(0.0, 1.2, 1, placed, 1.5, spec.R * 0.42, spec.R * 0.95)
                : kind == 2 ? landSpot(0.0, 4.6, 1, placed, 1.6, spec.R * 0.25, spec.R * 0.9)
                : landSpot(0.0, 3.6, 1, placed, 1.5, 3, spec.R * 0.95);
            if (found == null) continue;
            var spot = found.Value;
            double r = 0.5 + rand() * 1.0;
            placed.Add(new Spot { x = spot.x, z = spot.z, r = r });
            var geo = jitter(new THREE.IcosahedronGeometry(1, 1), 0.35, rand);
            var m = new THREE.Mesh(geo, mats[(int)Math.Floor(rand() * 3)]);
            double sx = r * (0.9 + rand() * 0.3), sy = r * (0.6 + rand() * 0.3), sz = r * (0.9 + rand() * 0.3);
            m.scale.set(sx, sy, sz);
            m.rotation.y = rand() * 6;
            m.position.set(spot.x, baseHeight(spot.x, spot.z) + r * 0.1, spot.z);
            m.castShadow = true;
            m.receiveShadow = true;
            group.add(m);
            if (r >= 0.6) colliders.Add(new Spot { x = spot.x, z = spot.z, r = r * 0.85 });
        }
    }

    /// <summary>An old circle of stone pillars on the summit, some still joined by a lintel, with a low altar in the middle.</summary>
    private void buildRuins()
    {
        var stone = new THREE.MeshStandardMaterial { color = 0x9a948a, roughness = 0.95, flatShading = true };
        var moss = new THREE.MeshStandardMaterial { color = 0x5c8a3c, roughness = 1 };
        double cx = ruins.x, cz = ruins.z;
        double bse = baseHeight(cx, cz);
        var g = new THREE.Group();
        g.position.set(cx, bse, cz);
        THREE.Mesh box(double w, double h, double d, THREE.Material mat, double x, double y, double z)
        {
            var m = new THREE.Mesh(jitter(new THREE.BoxGeometry(w, h, d), 0.06, rand), mat);
            m.position.set(x, y + h / 2, z);
            m.castShadow = true;
            m.receiveShadow = true;
            g.add(m);
            return m;
        }
        const int N = 6;
        var heights = new[] { 2.8, 2.8, 1.7, 2.6, 2.6, 1.3 };
        for (int i = 0; i < N; i++)
        {
            double a = (double)i / N * Math.PI * 2 + 0.4;
            double x = Math.Cos(a) * 3.5;
            double z = Math.Sin(a) * 3.5;
            double h = heights[i];
            var p = box(0.78, h, 0.78, stone, x, 0, z);
            p.rotation.y = a + rand() * 0.2;
            box(0.86, 0.12, 0.86, stone, x, h, z).rotation.y = p.rotation.y;
            if (h > 2.4) box(0.8, 0.05, 0.8, moss, x, h + 0.12, z);
            colliders.Add(new Spot { x = cx + x, z = cz + z, r = 0.55 });
        }
        // two lintels across the tall pairs
        foreach (var (i, j) in new[] { (0, 1), (3, 4) })
        {
            double a1 = (double)i / N * Math.PI * 2 + 0.4;
            double a2 = (double)j / N * Math.PI * 2 + 0.4;
            double x1 = Math.Cos(a1) * 3.5, z1 = Math.Sin(a1) * 3.5;
            double x2 = Math.Cos(a2) * 3.5, z2 = Math.Sin(a2) * 3.5;
            double len = JS.Hypot(x2 - x1, z2 - z1) + 0.6;
            var lintel = box(len, 0.42, 0.7, stone, (x1 + x2) / 2, heights[i] + 0.12, (z1 + z2) / 2);
            lintel.rotation.y = -Math.Atan2(z2 - z1, x2 - x1);
        }
        // the altar
        box(1.9, 0.18, 1.3, stone, 0, 0, 0);
        box(1.5, 0.5, 1.0, stone, 0, 0.18, 0);
        box(1.7, 0.14, 1.2, stone, 0, 0.68, 0);
        colliders.Add(new Spot { x = cx, z = cz, r = 1.05 });
        // a fallen column lying in the grass
        var col = new THREE.Mesh(jitter(new THREE.CylinderGeometry(0.34, 0.38, 2.4, 10), 0.05, rand), stone);
        col.rotation.z = Math.PI / 2;
        col.rotation.y = 0.8;
        col.position.set(-2.3, 0.4, 3.2);
        col.castShadow = true;
        g.add(col);
        group.add(g);
    }

    /// <summary>A rolled-up old map on top of the altar.</summary>
    private void buildScroll()
    {
        var paper = new THREE.MeshStandardMaterial { color = 0xe6d3a2, roughness = 0.9, emissive = 0x6b4a10, emissiveIntensity = 0.12 };
        var edge = new THREE.MeshStandardMaterial { color = 0xc9ad74, roughness = 0.9 };
        var ribbon = new THREE.MeshStandardMaterial { color = 0xc23b2f, roughness = 0.6 };
        var roll = new THREE.Mesh(new THREE.CylinderGeometry(0.075, 0.075, 0.5, 14), paper);
        roll.rotation.z = Math.PI / 2;
        scroll.add(roll);
        foreach (var x in new[] { -0.25, 0.25 })
        {
            var cap = new THREE.Mesh(new THREE.CylinderGeometry(0.085, 0.085, 0.03, 14), edge);
            cap.rotation.z = Math.PI / 2;
            cap.position.x = x;
            scroll.add(cap);
        }
        var tie = new THREE.Mesh(new THREE.TorusGeometry(0.08, 0.012, 6, 14), ribbon);
        tie.rotation.y = Math.PI / 2;
        scroll.add(tie);
        // the top of the altar is 0.82 above the summit
        scrollY = baseHeight(ruins.x, ruins.z) + 0.82 + 0.08;
        scroll.position.set(ruins.x, scrollY, ruins.z);
        scroll.rotation.y = 0.5;
        scroll.traverse(o => { if (o is THREE.Mesh m) m.castShadow = true; });
        group.add(scroll);
    }

    public bool mapAvailable => scroll.visible;

    /// <summary>Whether the player is standing close enough to the altar to take the map.</summary>
    public bool nearMap(double px, double pz) => scroll.visible && JS.Hypot(px - ruins.x, pz - ruins.z) < 2.7;

    /// <summary>The map is taken: it leaves the altar.</summary>
    public void takeMap() => scroll.visible = false;

    /// <summary>The hull of a small boat half buried in the sand near the water, with a broken mast.</summary>
    private void buildWreck()
    {
        // walk out from the middle toward the main beach (north) until the ground meets the water, then back up a little
        const double a = -1.25;
        double r = 6;
        while (r < spec.R * 1.2 && baseHeight(spec.x + Math.Cos(a) * r, spec.z + Math.Sin(a) * r) > 0.15) r += 0.25;
        r -= 1.8;
        double x = spec.x + Math.Cos(a) * r;
        double z = spec.z + Math.Sin(a) * r;
        wreck = (x, z);
        var wood = new THREE.MeshStandardMaterial { color = 0x7b5a3a, roughness = 0.95, side = THREE.Side.DoubleSide };
        var grey = new THREE.MeshStandardMaterial { color = 0x8c7d68, roughness = 0.95 };
        var g = new THREE.Group();
        var hull = new THREE.Mesh(new THREE.CylinderGeometry(0.95, 0.95, 3.6, 14, 1, true, 0, Math.PI), wood);
        hull.rotation.set(0, 0, Math.PI / 2);
        hull.rotation.order = "ZYX";
        hull.scale.set(0.8, 1, 1);
        hull.position.y = 0.55;
        hull.castShadow = true;
        g.add(hull);
        for (int i = -2; i <= 2; i++)
        {
            var rib = new THREE.Mesh(new THREE.TorusGeometry(0.88, 0.04, 6, 14, Math.PI), grey);
            rib.position.set(i * 0.7, 0.55, 0);
            rib.rotation.y = Math.PI / 2;
            rib.rotation.x = Math.PI;
            rib.scale.set(1, 0.8, 1);
            g.add(rib);
        }
        var mast = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.09, 2.2, 8), grey);
        mast.position.set(-0.3, 1.5, 0);
        mast.rotation.z = 0.35;
        mast.castShadow = true;
        g.add(mast);
        g.position.set(x, baseHeight(x, z) - 0.15, z);
        g.rotation.y = 0.5 + rand() * 1.5;
        g.rotation.z = 0.12;
        group.add(g);
        colliders.Add(new Spot { x = x, z = z, r = 1.4 });
    }

    // ---------------------------------------------------------------- the lagoon cay

    /// <summary>which way the castaway's camp faces: its open side looks down at the lagoon</summary>
    private double campYaw = 0;

    /// <summary>A point of the camp, given in its own right (x) and forward (z) metres, in the world.</summary>
    private (double x, double z) campPoint(double lx, double lz) =>
        (ruins.x + lx * Math.Cos(campYaw) + lz * Math.Sin(campYaw), ruins.z - lx * Math.Sin(campYaw) + lz * Math.Cos(campYaw));

    /// <summary>
    /// Somebody lived on the dune for a while: a lean-to of driftwood and palm thatch with a bedroll and a sea chest under it, a ring
    /// of stones round a dead fire, a log to sit on, a water barrel, and a mast with a red rag on it to be seen from a passing ship.
    /// </summary>
    private void buildCamp()
    {
        campYaw = Math.Atan2(spec.x - ruins.x, spec.z - ruins.z);
        double bse = baseHeight(ruins.x, ruins.z);
        var g = new THREE.Group();
        g.position.set(ruins.x, bse, ruins.z);
        g.rotation.y = campYaw;
        group.add(g);
        // the dune falls away on both sides, so every piece is set down on the ground under it
        double ground(double lx, double lz)
        {
            var w = campPoint(lx, lz);
            return baseHeight(w.x, w.z) - bse;
        }
        THREE.Mesh put(THREE.BufferGeometry geo, THREE.Material mat, double x, double y, double z)
        {
            var m = new THREE.Mesh(geo, mat);
            m.position.set(x, y, z);
            m.castShadow = true;
            m.receiveShadow = true;
            g.add(m);
            return m;
        }
        void solid(double lx, double lz, double r)
        {
            var w = campPoint(lx, lz);
            colliders.Add(new Spot { x = w.x, z = w.z, r = r });
        }
        var drift = new THREE.MeshStandardMaterial { color = 0x9a8468, roughness = 1 };
        var driftDark = new THREE.MeshStandardMaterial { color = 0x7a6650, roughness = 1 };
        var thatch = new[] { 0x9a8f52, 0x857a45, 0xa89c60 }.Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 1, side = THREE.Side.DoubleSide }).ToArray();
        var iron = new THREE.MeshStandardMaterial { color = 0x34363a, roughness = 0.6, metalness = 0.6 };
        var stone = new THREE.MeshStandardMaterial { color = 0x9c958a, roughness = 0.95, flatShading = true };

        // the lean-to: two posts, a ridge pole, and a roof sloping down to the sand behind
        const double ridgeY = 1.9, backZ = -2.3;
        foreach (var sx in new[] { -1.55, 1.55 })
        {
            double gy = ground(sx, 0);
            put(new THREE.CylinderGeometry(0.06, 0.085, ridgeY - gy + 0.5, 7), drift, sx, (ridgeY + gy - 0.5) / 2, 0).rotation.z = sx * 0.02;
        }
        put(new THREE.CylinderGeometry(0.055, 0.065, 3.6, 7), driftDark, 0, ridgeY, 0).rotation.z = Math.PI / 2;
        double backY = ground(0, backZ) + 0.05;
        double run = JS.Hypot(ridgeY - backY, backZ);
        double tilt = Math.Atan2(ridgeY - backY, -backZ);
        for (int i = 0; i < 5; i++)
        {
            var rafter = put(new THREE.CylinderGeometry(0.035, 0.04, run + 0.3, 6), drift, -1.4 + i * 0.7, (ridgeY + backY) / 2, backZ / 2);
            rafter.rotation.x = Math.PI / 2 - tilt;
        }
        for (int i = 0; i < 6; i++)
        {
            var leaf = put(new THREE.BoxGeometry(0.66, 0.045, run + 0.25), thatch[i % 3], -1.5 + i * 0.6, (ridgeY + backY) / 2 + 0.07 + (i % 2) * 0.03, backZ / 2);
            leaf.rotation.order = "YXZ";
            leaf.rotation.x = -tilt;
            leaf.rotation.y = (rand() - 0.5) * 0.08;
        }
        // a bedroll and a sea chest under it
        double bedY = ground(-0.65, -0.9);
        put(new THREE.BoxGeometry(0.8, 0.1, 1.9), new THREE.MeshStandardMaterial { color = 0x6f8aa0, roughness = 1 }, -0.65, bedY + 0.05, -0.9);
        put(new THREE.CylinderGeometry(0.13, 0.13, 0.7, 10), new THREE.MeshStandardMaterial { color = 0xc9bfa4, roughness = 1 }, -0.65, bedY + 0.16, -1.7).rotation.z = Math.PI / 2;
        double chestY = ground(0.85, -1.0);
        var chestWood = new THREE.MeshStandardMaterial { color = 0x6b4a2c, roughness = 0.9 };
        put(new THREE.BoxGeometry(0.85, 0.42, 0.5), chestWood, 0.85, chestY + 0.21, -1.0);
        var lid = put(new THREE.CylinderGeometry(0.25, 0.25, 0.85, 14, 1, false, 0, Math.PI), chestWood, 0.85, chestY + 0.42, -1.0);
        lid.rotation.z = Math.PI / 2;
        foreach (var bx in new[] { -0.28, 0.28 }) put(new THREE.BoxGeometry(0.06, 0.44, 0.52), iron, 0.85 + bx, chestY + 0.22, -1.0);
        solid(-0.8, -1.0, 1.15);
        solid(0.8, -1.0, 1.15);

        // the fire: a ring of stones, ash, and two burnt logs
        const double fx = 0.3, fz = 2.3;
        double fireY = ground(fx, fz);
        for (int i = 0; i < 8; i++)
        {
            double a = i / 8.0 * Math.PI * 2 + rand() * 0.3;
            var st = put(jitter(new THREE.IcosahedronGeometry(0.17 + rand() * 0.06, 1), 0.07, rand), stone, fx + Math.Cos(a) * 0.62, fireY + 0.07, fz + Math.Sin(a) * 0.62);
            st.scale.y = 0.75;
        }
        put(new THREE.CircleGeometry(0.52, 18), new THREE.MeshStandardMaterial { color = 0x4a4640, roughness = 1 }, fx, fireY + 0.03, fz).rotation.x = -Math.PI / 2;
        var burnt = new THREE.MeshStandardMaterial { color = 0x2a2420, roughness = 1 };
        foreach (var ry in new[] { 0.5, 2.1 })
        {
            var log = put(new THREE.CylinderGeometry(0.06, 0.07, 0.85, 7), burnt, fx, fireY + 0.1, fz);
            log.rotation.order = "YXZ";
            log.rotation.y = ry;
            log.rotation.x = Math.PI / 2 - 0.12;
        }
        solid(fx, fz, 0.8);
        // a log to sit on, and a barrel
        double seatY = ground(-1.7, 2.9);
        var seat = put(new THREE.CylinderGeometry(0.2, 0.23, 1.9, 9), driftDark, -1.7, seatY + 0.18, 2.9);
        seat.rotation.order = "YXZ";
        seat.rotation.y = 0.9;
        seat.rotation.x = Math.PI / 2;
        solid(-1.25, 3.25, 0.45);
        solid(-2.15, 2.55, 0.45);
        double barrelY = ground(2.5, 0.5);
        var barrel = put(new THREE.CylinderGeometry(0.29, 0.26, 0.8, 14), new THREE.MeshStandardMaterial { color = 0x7b5a36, roughness = 0.9 }, 2.5, barrelY + 0.4, 0.5);
        foreach (var hy in new[] { 0.15, 0.62 })
        {
            var hoop = put(new THREE.TorusGeometry(0.285, 0.018, 6, 18), iron, 2.5, barrelY + hy, 0.5);
            hoop.rotation.x = Math.PI / 2;
        }
        barrel.rotation.y = 0.4;
        solid(2.5, 0.5, 0.5);

        // the signal mast, with a spar and a red rag that stirs in the wind
        const double mx = 3.4, mz = -1.0;
        double mastY = ground(mx, mz);
        put(new THREE.CylinderGeometry(0.055, 0.1, 6.4, 8), drift, mx, mastY + 2.9, mz);
        put(new THREE.CylinderGeometry(0.035, 0.035, 1.5, 6), driftDark, mx, mastY + 5.6, mz).rotation.z = Math.PI / 2;
        foreach (var sx in new[] { -1, 1 })
        {
            // rope stays down to the sand
            var stay = put(new THREE.CylinderGeometry(0.012, 0.012, 4.4, 4), driftDark, mx + sx * 0.9, mastY + 2.05, mz);
            stay.rotation.z = sx * 0.42;
        }
        var rag = new THREE.Group();
        rag.position.set(mx, mastY + 5.25, mz);
        var cloth = new THREE.Mesh(new THREE.PlaneGeometry(1.25, 0.62, 5, 2), new THREE.MeshStandardMaterial { color = 0xc23b2f, roughness = 0.9, side = THREE.Side.DoubleSide });
        var cp = cloth.geometry.attributes.position;
        for (int i = 0; i < cp.count; i++)
        {
            // torn along its free edge, and hanging a little
            double u = (cp.getX(i) + 0.625) / 1.25;
            cp.setY(i, cp.getY(i) * (1 - u * 0.35) - u * u * 0.18);
            cp.setZ(i, Math.Sin(u * 5) * 0.06 * u);
        }
        cloth.geometry.computeVertexNormals();
        cloth.position.x = 0.67;
        cloth.castShadow = true;
        rag.add(cloth);
        g.add(rag);
        pennant = rag;
        solid(mx, mz, 0.3);
    }

    // ---------------------------------------------------------------- the ember rock

    /// <summary>the pile of flat stones on the crater's rim</summary>
    private (double x, double z) cairn = (0, 0);

    /// <summary>An outcrop of six-sided basalt columns where the cone meets the black sand, tallest in the middle, like organ pipes.</summary>
    private void buildColumns()
    {
        const double a = -1.15;
        double r = spec.R * 0.3;
        while (r < spec.R * 0.8 && baseHeight(spec.x + Math.Cos(a) * r, spec.z + Math.Sin(a) * r) > 1.5) r += 0.25;
        double cx = spec.x + Math.Cos(a) * r, cz = spec.z + Math.Sin(a) * r;
        var mats = new[] { 0x3a3734, 0x2d2b29, 0x45413c }.Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.85, flatShading = true }).ToArray();
        var capMat = new THREE.MeshStandardMaterial { color = 0x57524c, roughness = 0.9, flatShading = true };
        const double pitch = 0.64;
        for (int q = -4; q <= 4; q++)
        {
            for (int k = -4; k <= 4; k++)
            {
                double lx = (q + k * 0.5) * pitch, lz = k * pitch * 0.866;
                double d = JS.Hypot(lx, lz);
                if (d > 2.5 || rand() < 0.08) continue;
                double x = cx + lx, z = cz + lz;
                double h = 0.5 + 2.7 * Math.Pow(1 - d / 2.7, 0.8) + rand() * 0.9;
                double y0 = baseHeight(x, z) - 0.5;
                var col = new THREE.Mesh(new THREE.CylinderGeometry(0.37, 0.37, h + 0.5, 6), mats[(int)Math.Floor(rand() * 3)]);
                col.position.set(x, y0 + (h + 0.5) / 2, z);
                col.castShadow = true;
                col.receiveShadow = true;
                group.add(col);
                var cap = new THREE.Mesh(new THREE.CylinderGeometry(0.33, 0.37, 0.05, 6), capMat);
                cap.position.set(x, y0 + h + 0.52, z);
                group.add(cap);
            }
        }
        colliders.Add(new Spot { x = cx, z = cz, r = 2.5 });
    }

    /// <summary>A few dead trees on the black sand, bleached grey, each with a couple of bare branches.</summary>
    private void buildSnags()
    {
        var wood = new THREE.MeshStandardMaterial { color = 0xa59c8d, roughness = 1 };
        var placed = new List<Spot>(colliders);
        for (int n = 0; n < 4; n++)
        {
            var found = landSpot(0.4, 1.3, 0.3, placed, 3.5, spec.R * 0.5, spec.R * 0.9);
            if (found == null) continue;
            var spot = found.Value;
            placed.Add(new Spot { x = spot.x, z = spot.z, r = 0.4 });
            var g = new THREE.Group();
            double h = 2.4 + rand() * 1.4;
            double lean = (rand() - 0.5) * 1.2;
            var top = new THREE.Vector3(lean, h, (rand() - 0.5) * 0.6);
            var trunkCurve = new THREE.QuadraticBezierCurve3(new THREE.Vector3(0, 0, 0), new THREE.Vector3(lean * 0.1, h * 0.55, 0), top);
            var trunk = new THREE.Mesh(new THREE.TubeGeometry(trunkCurve, 10, 0.11, 6), wood);
            trunk.castShadow = true;
            g.add(trunk);
            int branches = 2 + (int)Math.Floor(rand() * 2);
            for (int i = 0; i < branches; i++)
            {
                double at = 0.45 + i * 0.2 + rand() * 0.1;
                var p0 = trunkCurve.getPoint(Math.Min(0.95, at));
                double dir = rand() * Math.PI * 2, len = 0.7 + rand() * 0.8;
                var p1 = new THREE.Vector3(p0.x + Math.Cos(dir) * len, p0.y + len * (0.5 + rand() * 0.5), p0.z + Math.Sin(dir) * len);
                var mid = new THREE.Vector3((p0.x + p1.x) / 2 + Math.Cos(dir) * 0.15, (p0.y + p1.y) / 2 - 0.1, (p0.z + p1.z) / 2 + Math.Sin(dir) * 0.15);
                var branch = new THREE.Mesh(new THREE.TubeGeometry(new THREE.QuadraticBezierCurve3(p0, mid, p1), 6, 0.045, 5), wood);
                branch.castShadow = true;
                g.add(branch);
            }
            g.position.set(spot.x, baseHeight(spot.x, spot.z) - 0.1, spot.z);
            group.add(g);
            colliders.Add(new Spot { x = spot.x, z = spot.z, r = 0.35 });
        }
    }

    /// <summary>A pile of flat stones on the crater's rim: someone climbed up here long ago and left a mark, and something beside it.</summary>
    private void buildCairn()
    {
        cairn = highestAlong(-1.75, 0.22, 0.32);
        var mats = new[] { 0x4a4641, 0x3b3835, 0x5a554e }.Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.95, flatShading = true }).ToArray();
        double y = baseHeight(cairn.x, cairn.z) - 0.05;
        var radii = new[] { 0.44, 0.36, 0.3, 0.22, 0.15 };
        for (int i = 0; i < radii.Length; i++)
        {
            double r = radii[i];
            var m = new THREE.Mesh(jitter(new THREE.IcosahedronGeometry(1, 1), 0.3, rand), mats[i % 3]);
            m.scale.set(r, r * 0.42, r * 0.9);
            m.rotation.y = rand() * 6;
            m.position.set(cairn.x + (rand() - 0.5) * 0.06, y + r * 0.42, cairn.z + (rand() - 0.5) * 0.06);
            m.castShadow = true;
            m.receiveShadow = true;
            group.add(m);
            y += r * 0.74;
        }
        colliders.Add(new Spot { x = cairn.x, z = cairn.z, r = 0.5 });
    }

    /// <summary>
    /// The lighthouse on the summit. It stands on the highest ground under it, so its floor is never buried; its door faces
    /// the beach, the way a boat comes in.
    /// </summary>
    private void buildLighthouse()
    {
        double top = double.NegativeInfinity;
        for (int i = 0; i < 24; i++)
        {
            double a = i / 24.0 * Math.PI * 2;
            foreach (var r in new[] { 0.0, 1.5, 3.0, 3.6 })
                top = Math.Max(top, baseHeight(spec.x + Math.Cos(a) * r, spec.z + Math.Sin(a) * r));
        }
        double door = Math.Atan2(-spec.z, -spec.x);
        lighthouse = new Lighthouse(spec.x, spec.z, top, door);
        group.add(lighthouse.group);
    }

    /// <summary>Lays `id` down on a free, walkable spot in a band of heights and distances from the middle.</summary>
    private void scatter(List<FindSpot> o, Func<double> rnd, string id, double minH, double maxH, double rMin, double rMax, double maxSlope)
    {
        for (int tries = 0; tries < 400; tries++)
        {
            double a = rnd() * Math.PI * 2;
            double rad = rMin + rnd() * (rMax - rMin);
            double x = spec.x + Math.Cos(a) * rad;
            double z = spec.z + Math.Sin(a) * rad;
            double h = baseHeight(x, z);
            if (h < minH || h > maxH || slopeAt(x, z) > maxSlope) continue;
            if (colliders.Any(c => JS.Hypot(c.x - x, c.z - z) < c.r + 1.0)) continue;
            if (o.Any(t => JS.Hypot(t.x - x, t.z - z) < 4)) continue;
            o.Add(new FindSpot { id = id, x = x, z = z });
            return;
        }
    }

    /// <summary>The lagoon cay: a spyglass left at the camp, nautilus shells on the seaward beach and red coral washed up inside the ring.</summary>
    private List<FindSpot> lagoonFinds()
    {
        var rnd = rng(131);
        var o = new List<FindSpot>();
        var at = campPoint(1.0, 0.9);
        o.Add(new FindSpot { id = "spyglass", x = at.x, z = at.z });
        for (int i = 0; i < 4; i++) scatter(o, rnd, "nautilus", 0.1, 0.6, spec.R * 0.66, spec.R * 0.98, 0.25);
        for (int i = 0; i < 3; i++) scatter(o, rnd, "red_coral", 0.1, 0.55, spec.R * 0.38, spec.R * 0.6, 0.25);
        return o;
    }

    /// <summary>The ember rock: a golden sun by the cairn on the rim, fire opals on the cone and obsidian blades on the black sand.</summary>
    private List<FindSpot> emberFinds()
    {
        var rnd = rng(173);
        var o = new List<FindSpot>();
        // beside the cairn, a step along the rim from it
        double a = Math.Atan2(cairn.z - spec.z, cairn.x - spec.x) + 0.3;
        double r = JS.Hypot(cairn.x - spec.x, cairn.z - spec.z);
        o.Add(new FindSpot { id = "sun_disc", x = spec.x + Math.Cos(a) * r, z = spec.z + Math.Sin(a) * r });
        for (int i = 0; i < 3; i++) scatter(o, rnd, "fire_opal", 2.4, 8.2, spec.R * 0.2, spec.R * 0.52, 0.34);
        for (int i = 0; i < 4; i++) scatter(o, rnd, "obsidian_blade", 0.25, 1.0, spec.R * 0.55, spec.R * 0.9, 0.2);
        return o;
    }

    /// <summary>Where the island's own finds lie in the open: idols below the altar, pearls by the wreck and the cliff, conches on the shore.</summary>
    public List<FindSpot> findSpots()
    {
        if (kind == 1) return lagoonFinds();
        if (kind == 2) return emberFinds();
        var rnd = rng(99);
        var o = new List<FindSpot>();
        var r = ruins;
        o.Add(new FindSpot { id = "stone_idol", x = r.x - 1.0, z = r.z + 1.9 });
        o.Add(new FindSpot { id = "stone_idol", x = r.x + 1.1, z = r.z + 2.0 });
        o.Add(new FindSpot { id = "black_pearl", x = wreck.x + 2.0, z = wreck.z + 1.0 });
        // a second pearl tucked at the foot of the cliff on the western side
        const double ca = 3.0;
        o.Add(new FindSpot { id = "black_pearl", x = spec.x + Math.Cos(ca) * 13, z = spec.z + Math.Sin(ca) * 13 });
        var taken = o.Select(f => (f.x, f.z)).ToList();
        void place(string id, double minH, double maxH, double rMin, double rMax)
        {
            for (int tries = 0; tries < 200; tries++)
            {
                double a = rnd() * Math.PI * 2;
                double rad = rMin + rnd() * (rMax - rMin);
                double x = spec.x + Math.Cos(a) * rad;
                double z = spec.z + Math.Sin(a) * rad;
                double h = baseHeight(x, z);
                if (h < minH || h > maxH || slopeAt(x, z) > 0.2) continue;
                if (colliders.Any(c => JS.Hypot(c.x - x, c.z - z) < c.r + 1.0)) continue;
                if (taken.Any(t => JS.Hypot(t.x - x, t.z - z) < 4)) continue;
                o.Add(new FindSpot { id = id, x = x, z = z });
                taken.Add((x, z));
                return;
            }
        }
        for (int i = 0; i < 4; i++) place("conch", 0.25, 0.6, 12, spec.R * 0.85);
        for (int i = 0; i < 4; i++) place("piece_eight", 0.9, 3.0, 4, spec.R * 0.7);
        return o;
    }

    public void update(double t, double surfaceY, THREE.Color fog, double density, double camX, double camZ, double dt = 0)
    {
        double dist = JS.Hypot(camX - spec.x, camZ - spec.z);
        double d = Math.Max(0, dist - spec.R * 0.6);
        double f = Math.Min(0.62, 1 - Math.Exp(-Math.Pow(d * density, 2)));
        foreach (var (mat, bse) in haze)
        {
            if (mat is THREE.MeshStandardMaterial s)
            {
                s.color.copy(bse).multiplyScalar(1 - f);
                s.emissive.copy(fog).multiplyScalar(f * 0.9);
            }
            else if (mat is THREE.MeshBasicMaterial b)
            {
                b.color.copy(bse).lerp(fog, f);
            }
        }
        // the map bobs very slightly, as if it wants to be picked up
        scroll.position.y = scrollY + Math.Sin(t * 2) * 0.012;
        // the rag on the mast swings about in the wind
        if (pennant != null) pennant.rotation.y = 0.6 + Math.Sin(t * 1.3) * 0.35 + Math.Sin(t * 3.1) * 0.12;
        // the lighthouse's lamp is lit when the sky is dark: how dark is read off the colour of the haze
        if (lighthouse != null)
        {
            double light = (fog.r + fog.g + fog.b) / 3;
            lighthouse.update(t, dt, Math.Max(0, Math.Min(1, (0.42 - light) / 0.27)));
        }
        foreach (var p in palms)
        {
            p.crown.rotation.z = Math.Sin(t * 0.8 + p.phase) * 0.03;
            p.crown.rotation.x = Math.Cos(t * 0.6 + p.phase) * 0.025;
        }
        for (int i = 0; i < foam.Count; i++)
        {
            var fm = foam[i];
            fm.position.y = surfaceY + 0.05;
            var m = (THREE.MeshBasicMaterial)fm.material;
            m.opacity = fm.userData.Num("base") * (0.7 + 0.3 * Math.Sin(t * 0.9 + i * 1.7));
        }
    }
}
