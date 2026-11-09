using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Island.ts

/// <summary>
/// A small island out at sea, reached only by boat. Its terrain is part of the world's height function (see Beach.cs), so the boat,
/// the player, the maps and every find already know where the land is; this class builds what you see and what you can bump into:
/// the green and rocky ground, palms, bushes, boulders, an old stone ruin on the hilltop and a wrecked boat on the sand.
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

    public readonly THREE.Group group = new THREE.Group();
    /// <summary>solid circles for walking around: trunks, boulders, ruin pillars, the wreck</summary>
    public readonly List<Spot> colliders = new List<Spot>();
    /// <summary>the old stone circle on the summit</summary>
    public readonly (double x, double z) ruins = (ISLAND.x + 1, ISLAND.z - 1);
    private readonly List<(THREE.Group crown, double phase, List<THREE.Mesh> fronds, THREE.Group nuts)> palms = new List<(THREE.Group, double, List<THREE.Mesh>, THREE.Group)>();
    private readonly THREE.Group shrubs = new THREE.Group();
    private readonly List<THREE.Mesh> foam = new List<THREE.Mesh>();
    private (double x, double z) wreck = (0, 0);
    /// <summary>the old map lying on the altar until someone takes it</summary>
    private readonly THREE.Group scroll = new THREE.Group();
    private double scrollY = 0;
    private readonly Func<double> rand = rng(4711);
    /// <summary>every material on the island, with its own colour, so distance haze can be applied by hand</summary>
    private readonly List<(THREE.Material mat, THREE.Color @base)> haze = new List<(THREE.Material, THREE.Color)>();

    public Island()
    {
        buildTerrain();
        buildFoam();
        buildPalms();
        group.add(shrubs);
        buildShrubs();
        buildRocks();
        buildRuins();
        buildScroll();
        buildWreck();
        prepareHaze();
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

    public bool contains(double x, double z) => JS.Hypot(x - ISLAND.x, z - ISLAND.z) < ISLAND.R * 1.1;

    /// <summary>Where a boat at (bx, bz) can put someone ashore, heading straight for the island.</summary>
    public (double x, double z)? landingFrom(double bx, double bz)
    {
        double dx = ISLAND.x - bx;
        double dz = ISLAND.z - bz;
        double L = JS.Hypot(dx, dz);
        if (L > ISLAND.R * 1.2 + 30) return null;
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

    private void buildTerrain()
    {
        double half = ISLAND.R * 1.25;
        const double step = 0.75;
        int n = (int)Math.Ceiling(2 * half / step);
        var geo = new THREE.BufferGeometry();
        var pos = new float[(n + 1) * (n + 1) * 3];
        var col = new float[(n + 1) * (n + 1) * 3];
        var contrib = new List<bool>();
        var c = new THREE.Color();
        var sandDry = new THREE.Color(0xe8d3a3);
        var sandWet = new THREE.Color(0xb89b6a);
        var floor = new THREE.Color(0x6f9a8a);
        var grass = new[] { new THREE.Color(0x5f9f3b), new THREE.Color(0x86b84f), new THREE.Color(0x3f7e34) };
        var rock = new[] { new THREE.Color(0x77726a), new THREE.Color(0x58544e), new THREE.Color(0x9a9488) };
        for (int j = 0; j <= n; j++)
        {
            for (int i = 0; i <= n; i++)
            {
                double x = ISLAND.x - half + i * step;
                double z = ISLAND.z - half + j * step;
                double h = Math.Max(baseHeight(x, z), -2.2);
                int k = (j * (n + 1) + i) * 3;
                pos[k] = (float)x;
                pos[k + 1] = (float)h;
                pos[k + 2] = (float)z;
                contrib.Add(islandHeight(x, z) > baseHeightMain(x, z) + 0.01);
                double slope = slopeAt(x, z);
                double noise = Math.Sin(x * 0.7) * Math.Sin(z * 0.6) * 0.5 + 0.5;
                if (h < WATER_LEVEL)
                {
                    c.copy(sandWet).lerp(floor, Math.Min(1, (WATER_LEVEL - h) / 1.4));
                }
                else
                {
                    c.copy(sandWet).lerp(sandDry, smooth(WATER_LEVEL, 0.35, h));
                    // grass takes over from the sand a little way up the slope, in patches
                    int gi = (int)Math.Floor(noise * 2.99);
                    var g = grass[gi].clone().lerp(grass[(gi + 1) % 3], noise * 3 % 1);
                    c.lerp(g, smooth(0.45, 1.1, h + (noise - 0.5) * 0.4));
                    // bare rock on the steep cliff and on the summit
                    var stone = rock[(int)Math.Floor((Math.Sin(x * 1.3 + z * 0.9) + 1) / 2 * 2.99)];
                    c.lerp(stone, Math.Max(smooth(0.2, 0.42, slope), smooth(3.1, 3.6, h) * 0.8));
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
        geo.setIndex(idx);
        geo.computeVertexNormals();
        // it overlaps the coarse sea floor under it, and wins where they touch
        var mat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1, polygonOffset = true, polygonOffsetFactor = -2, polygonOffsetUnits = -2 };
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
            while (r < ISLAND.R * 1.3 && baseHeight(ISLAND.x + Math.Cos(a) * r, ISLAND.z + Math.Sin(a) * r) > WATER_LEVEL) r += 0.25;
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
                pos.AddRange(new[] { ISLAND.x + Math.Cos(a) * (r + inner), 0, ISLAND.z + Math.Sin(a) * (r + inner) });
                pos.AddRange(new[] { ISLAND.x + Math.Cos(a) * (r + outer), 0, ISLAND.z + Math.Sin(a) * (r + outer) });
                if (k > 0)
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
        if (double.IsNaN(rMax)) rMax = ISLAND.R * 0.8;
        for (int tries = 0; tries < 120; tries++)
        {
            double a = rand() * Math.PI * 2;
            double r = rMin + rand() * (rMax - rMin);
            double x = ISLAND.x + Math.Cos(a) * r;
            double z = ISLAND.z + Math.Sin(a) * r;
            double h = baseHeight(x, z);
            if (h < minH || h > maxH || slopeAt(x, z) > maxSlope) continue;
            if (JS.Hypot(x - ruins.x, z - ruins.z) < 5) continue;
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
        var frondMats = new[] { 0x2f8a3a, 0x3c9a44, 0x28762f }.Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.85, side = THREE.Side.DoubleSide }).ToArray();
        var trunkMat = new THREE.MeshStandardMaterial { color = 0x7a5a3a, roughness = 1 };
        var nutMat = new THREE.MeshStandardMaterial { color = 0x5a3a1e, roughness = 0.7 };
        var placed = new List<Spot>();
        for (int n = 0; n < 17; n++)
        {
            var found = landSpot(0.8, 3.1, 0.2, placed, 3.2);
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
        var mats = new[] { 0x3f8036, 0x4f9a3d, 0x2f6e2c }.Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.95, flatShading = true }).ToArray();
        for (int n = 0; n < 34; n++)
        {
            var found = landSpot(0.55, 3.0, 0.3, new List<Spot>(), 0, 3, ISLAND.R * 0.82);
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
        var mats = new[] { 0x77726a, 0x65605a, 0x8a847a }.Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 0.95, flatShading = true }).ToArray();
        var placed = new List<Spot>();
        for (int n = 0; n < 14; n++)
        {
            // mostly on the cliff and the steeper ground, some on the sand
            var found = landSpot(0.0, 3.6, 1, placed, 1.5, 3, ISLAND.R * 0.95);
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
        while (r < ISLAND.R * 1.2 && baseHeight(ISLAND.x + Math.Cos(a) * r, ISLAND.z + Math.Sin(a) * r) > 0.15) r += 0.25;
        r -= 1.8;
        double x = ISLAND.x + Math.Cos(a) * r;
        double z = ISLAND.z + Math.Sin(a) * r;
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

    /// <summary>Where the island's own finds lie in the open: idols below the altar, pearls by the wreck and the cliff, conches on the shore.</summary>
    public List<FindSpot> findSpots()
    {
        var rnd = rng(99);
        var o = new List<FindSpot>();
        var r = ruins;
        o.Add(new FindSpot { id = "stone_idol", x = r.x - 1.0, z = r.z + 1.9 });
        o.Add(new FindSpot { id = "stone_idol", x = r.x + 1.1, z = r.z + 2.0 });
        o.Add(new FindSpot { id = "black_pearl", x = wreck.x + 2.0, z = wreck.z + 1.0 });
        // a second pearl tucked at the foot of the cliff on the western side
        const double ca = 3.0;
        o.Add(new FindSpot { id = "black_pearl", x = ISLAND.x + Math.Cos(ca) * 13, z = ISLAND.z + Math.Sin(ca) * 13 });
        var taken = o.Select(f => (f.x, f.z)).ToList();
        void place(string id, double minH, double maxH, double rMin, double rMax)
        {
            for (int tries = 0; tries < 200; tries++)
            {
                double a = rnd() * Math.PI * 2;
                double rad = rMin + rnd() * (rMax - rMin);
                double x = ISLAND.x + Math.Cos(a) * rad;
                double z = ISLAND.z + Math.Sin(a) * rad;
                double h = baseHeight(x, z);
                if (h < minH || h > maxH || slopeAt(x, z) > 0.2) continue;
                if (colliders.Any(c => JS.Hypot(c.x - x, c.z - z) < c.r + 1.0)) continue;
                if (taken.Any(t => JS.Hypot(t.x - x, t.z - z) < 4)) continue;
                o.Add(new FindSpot { id = id, x = x, z = z });
                taken.Add((x, z));
                return;
            }
        }
        for (int i = 0; i < 4; i++) place("conch", 0.25, 0.6, 12, ISLAND.R * 0.85);
        for (int i = 0; i < 4; i++) place("piece_eight", 0.9, 3.0, 4, ISLAND.R * 0.7);
        return o;
    }

    public void update(double t, double surfaceY, THREE.Color fog, double density, double camX, double camZ, double dt = 0)
    {
        double dist = JS.Hypot(camX - ISLAND.x, camZ - ISLAND.z);
        double d = Math.Max(0, dist - ISLAND.R * 0.6);
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
