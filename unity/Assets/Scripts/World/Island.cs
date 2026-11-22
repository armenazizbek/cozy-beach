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
    /// <summary>the lagoon cay's telescope on the dune; null on the other islands</summary>
    public Telescope telescope = null;
    /// <summary>where the telescope stands, kept clear of palms and bushes</summary>
    private (double x, double z)? scopeSpot = null;
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
            scopeSpot = highestAlong(SCOPE_ANGLE, 0.5, 0.85);
            buildTerrain();
            buildFoam();
            buildPalms();
            group.add(shrubs);
            buildShrubs();
            buildRocks();
            buildCamp();
            buildTelescope();
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
        gatherStill();
    }

    /// <summary>
    /// Moves everything on the island that never moves by itself (the ground, rocks, bushes, ruins, the camp's lean-to, the
    /// columns and the cairn) into one group that the scene copy skips (Object3D.syncStatic); its materials still follow the
    /// haze. What does move stays where it was: the palms, the foam, the old map, the camp with its flapping rag, the
    /// lighthouse and the telescope. Nothing moves in the world: the new group sits where the island's own group is.
    /// </summary>
    private void gatherStill()
    {
        var keep = new HashSet<THREE.Object3D>();
        void keepTop(THREE.Object3D o)
        {
            while (o != null && o.parent != group) o = o.parent;
            if (o != null) keep.Add(o);
        }
        foreach (var p in palms) keepTop(p.crown);
        foreach (var fm in foam) keepTop(fm);
        keepTop(scroll);
        if (pennant != null) keepTop(pennant);
        if (lighthouse != null) keepTop(lighthouse.group);
        if (telescope != null) keepTop(telescope.group);
        var still = new THREE.Group { name = "still", syncStatic = true };
        foreach (var c in group.children.ToList())
        {
            if (keep.Contains(c)) continue;
            still.add(c);
        }
        group.add(still);
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
            if (scopeSpot.HasValue && JS.Hypot(x - scopeSpot.Value.x, z - scopeSpot.Value.z) < 3.4) continue;
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
            // droop and a slight fold, both towards +z: the frond is tilted up out of the crown by turning +y towards +z,
            // so +z is downwards there and the tip arches over and hangs, the leaflets falling either side of the midrib
            p.setZ(i, t * t * 1.7 + Math.Abs(x) * 0.25);
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
            // the crown sways as a whole, but its fronds and coconuts never move inside it (Object3D.syncStatic)
            var crown = new THREE.Group { syncStatic = true };
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
    /// Somebody lived on the dune for a while: a lean-to of driftwood thatched with dried palm fronds, a woven mat with a rumpled
    /// blanket and a pillow under it, a sea chest bound in iron, a ring of stones round a dead fire with a cooking spit over it,
    /// a driftwood log to sit on, a water barrel, and a mast with a red rag on it to be seen from a passing ship. Everything but
    /// the rag is merged into a few meshes, one per material, and kept with the island's still things.
    /// </summary>
    private void buildCamp()
    {
        campYaw = Math.Atan2(spec.x - ruins.x, spec.z - ruins.z);
        double bse = baseHeight(ruins.x, ruins.z);
        // the camp's own random numbers, so its details never shift anything else on the island
        var cr = rng(5150);
        // the still parts, and (in their own group) the rag that stirs in the wind
        var gs = new THREE.Group();
        gs.position.set(ruins.x, bse, ruins.z);
        gs.rotation.y = campYaw;
        group.add(gs);
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
        void solid(double lx, double lz, double r)
        {
            var w = campPoint(lx, lz);
            colliders.Add(new Spot { x = w.x, z = w.z, r = r });
        }
        THREE.Vector3 V(double x, double y, double z) => new THREE.Vector3(x, y, z);

        var parts = new Dictionary<THREE.Material, List<THREE.BufferGeometry>>();
        // a shape, moved into place, goes into its material's pile; the piles become one mesh each at the end
        void add(THREE.BufferGeometry geo, THREE.Material mat, double x = 0, double y = 0, double z = 0, double rx = 0, double ry = 0, double rz = 0,
                 double sx = 1, double sy = 1, double sz = 1, string order = "XYZ")
        {
            var o = new THREE.Object3D();
            o.rotation.order = order;
            o.position.set(x, y, z);
            o.rotation.set(rx, ry, rz);
            o.scale.set(sx, sy, sz);
            o.updateMatrix();
            geo.applyMatrix4(o.matrix);
            geo.deleteAttribute("uv");
            if (geo.index != null) geo = geo.toNonIndexed();
            if (!parts.TryGetValue(mat, out var list)) parts[mat] = list = new List<THREE.BufferGeometry>();
            list.Add(geo);
        }
        // a length of driftwood from a to b: bent a little, knobbly, its ends worn round
        void wood(THREE.Vector3 a, THREE.Vector3 b, double r, THREE.Material mat, double bend = 0.12)
        {
            var mid = a.clone().lerp(b, 0.5);
            mid.x += (cr() - 0.5) * bend;
            mid.y += (cr() - 0.5) * bend * 0.5;
            mid.z += (cr() - 0.5) * bend;
            var tube = new THREE.TubeGeometry(new THREE.QuadraticBezierCurve3(a, mid, b), 8, r, 7);
            add(jitter(tube, r * 0.3, cr), mat);
            add(new THREE.SphereGeometry(r, 7, 5), mat, a.x, a.y, a.z);
            add(new THREE.SphereGeometry(r * 0.95, 7, 5), mat, b.x, b.y, b.z);
        }
        // a rope from a to b, sagging between them
        void rope(THREE.Vector3 a, THREE.Vector3 b, double sag, THREE.Material mat)
        {
            var mid = a.clone().lerp(b, 0.5);
            mid.y -= sag;
            add(new THREE.TubeGeometry(new THREE.QuadraticBezierCurve3(a, mid, b), 10, 0.012, 4), mat);
        }
        // a few turns of rope round a joint
        void lash(double x, double y, double z, double r, double rx, double ry, THREE.Material mat)
        {
            for (int i = 0; i < 3; i++)
                add(new THREE.TorusGeometry(r, 0.014, 4, 12), mat, x + (i - 1) * 0.028, y, z, rx, ry + Math.PI / 2, 0, order: "YXZ");
        }

        var drift = new THREE.MeshStandardMaterial { color = 0x9a8468, roughness = 1 };
        var driftDark = new THREE.MeshStandardMaterial { color = 0x7a6650, roughness = 1 };
        var cord = new THREE.MeshStandardMaterial { color = 0xb59a6a, roughness = 1 };
        var thatch = new[] { 0xa39a5a, 0x8f874a, 0xb3a866, 0x9c9150 }
            .Select(c => new THREE.MeshStandardMaterial { color = c, roughness = 1, side = THREE.Side.DoubleSide }).ToArray();
        var iron = new THREE.MeshStandardMaterial { color = 0x34363a, roughness = 0.6, metalness = 0.6 };
        var stone = new THREE.MeshStandardMaterial { color = 0x9c958a, roughness = 0.95, flatShading = true };

        // ---- the lean-to: two forked posts, a ridge pole tied into the forks, rafters down to the sand behind, and fronds over them
        const double ridgeY = 1.9, backZ = -2.3;
        foreach (var sx in new[] { -1.55, 1.55 })
        {
            double gy = ground(sx, 0);
            wood(V(sx, gy - 0.3, 0), V(sx + sx * 0.015, ridgeY - 0.02, 0), 0.075, drift, 0.06);
            // the fork the ridge pole rests in
            wood(V(sx, ridgeY - 0.06, 0), V(sx - 0.05, ridgeY + 0.22, 0.12), 0.04, drift, 0.03);
            wood(V(sx, ridgeY - 0.06, 0), V(sx + 0.05, ridgeY + 0.2, -0.13), 0.04, drift, 0.03);
            lash(sx, ridgeY + 0.04, 0, 0.085, 0, 0, cord);
        }
        wood(V(-1.95, ridgeY + 0.06, 0.02), V(1.95, ridgeY + 0.03, -0.02), 0.06, driftDark, 0.1);
        double backY = ground(0, backZ) + 0.05;
        for (int i = 0; i < 5; i++)
        {
            double x = -1.4 + i * 0.7;
            wood(V(x, ridgeY + 0.12, 0.28), V(x + (cr() - 0.5) * 0.15, backY - 0.08, backZ - 0.2), 0.038, drift, 0.08);
        }
        // the roof: dried fronds laid from the ridge down to the sand, side by side and overlapping, their tips hanging over the
        // front; each frond is a strip arched across its midrib with ragged leaflet edges
        var front = V(0, ridgeY + 0.16, 0.32);
        var back = V(0, backY + 0.02, backZ - 0.15);
        var down = back.clone().sub(front);
        double len = down.length();
        down.normalize();
        var up = V(0, -down.z, down.y).normalize();
        if (up.y < 0) up.negate();
        for (int i = 0; i < 11; i++)
        {
            double cx = -1.75 + i * 0.35 + (cr() - 0.5) * 0.06;
            double width = 0.5 + cr() * 0.12;
            double lift = 0.03 + (i % 2) * 0.035;
            double hang = 0.35 + cr() * 0.25;
            var frond = new THREE.PlaneGeometry(1, 1, 4, 14);
            var fp = frond.attributes.position;
            for (int k = 0; k < fp.count; k++)
            {
                double u = fp.getX(k);              // -0.5 .. 0.5 across
                double v = 0.5 - fp.getY(k);        // 0 at the front, 1 at the back
                double along = -hang + v * (len + hang);
                double taper = v < 0.85 ? 1 : 1 - (v - 0.85) / 0.15 * 0.6;
                double edge = Math.Abs(u) > 0.49 ? Math.Sin(v * 70 + i) * 0.07 : 0;
                var pt = front.clone()
                    .addScaledVector(down, Math.Max(0, along) + edge)
                    .addScaledVector(V(1, 0, 0), cx + u * width * taper)
                    .addScaledVector(up, lift - Math.Abs(u) * 0.09);
                // in front of the ridge the frond is not held up by anything, so it bends down over it
                if (along < 0)
                {
                    pt.addScaledVector(V(0, 0, 1), -along * 0.55);
                    pt.y -= along * along * 1.6 + (-along) * 0.35;
                }
                fp.setXYZ(k, pt.x, pt.y, pt.z);
            }
            frond.computeVertexNormals();
            add(frond, thatch[i % 4]);
        }
        // ---- a woven mat, a rumpled blanket, a rolled blanket at its foot and a pillow
        double bedY = ground(-0.65, -0.9);
        add(new THREE.RoundedBoxGeometry(0.85, 0.05, 1.95, 2, 0.02), new THREE.MeshStandardMaterial { color = 0xbca877, roughness = 1 }, -0.65, bedY + 0.03, -0.9);
        var weave = new THREE.MeshStandardMaterial { color = 0xa08d5c, roughness = 1 };
        for (int i = 0; i < 9; i++) add(new THREE.BoxGeometry(0.86, 0.012, 0.03), weave, -0.65, bedY + 0.057, -1.75 + i * 0.21);
        var blanket = new THREE.PlaneGeometry(0.78, 1.15, 8, 12);
        var bp = blanket.attributes.position;
        for (int k = 0; k < bp.count; k++)
        {
            double u = bp.getX(k), v = bp.getY(k);
            double lump = 0.035 * Math.Sin(u * 9 + v * 4) + 0.03 * Math.Sin(v * 13 - u * 3) + 0.02;
            double drape = Math.Abs(u) > 0.36 ? -0.05 : 0;
            bp.setXYZ(k, u, v, lump + drape);
        }
        blanket.computeVertexNormals();
        var wool = new THREE.MeshStandardMaterial { color = 0x5f7f96, roughness = 1, side = THREE.Side.DoubleSide };
        add(blanket, wool, -0.65, bedY + 0.08, -0.55, -Math.PI / 2);
        add(new THREE.CapsuleGeometry(0.09, 0.62, 3, 9), wool, -0.65, bedY + 0.14, 0.12, 0, 0, Math.PI / 2);
        add(new THREE.SphereGeometry(0.22, 12, 8), new THREE.MeshStandardMaterial { color = 0xd3c8a8, roughness = 1 }, -0.65, bedY + 0.13, -1.6, 0, 0.15, 0, 1.3, 0.42, 0.75);
        // ---- the sea chest: planks, a rounded lid, iron bands over both, a lock plate and rope handles
        double chestY = ground(0.85, -1.0);
        var chestWood = new THREE.MeshStandardMaterial { color = 0x6b4a2c, roughness = 0.9 };
        var seam = new THREE.MeshStandardMaterial { color = 0x3e2a18, roughness = 1 };
        add(new THREE.RoundedBoxGeometry(0.85, 0.42, 0.5, 2, 0.025), chestWood, 0.85, chestY + 0.21, -1.0);
        foreach (var py in new[] { 0.14, 0.28 }) add(new THREE.BoxGeometry(0.8, 0.012, 0.505), seam, 0.85, chestY + py, -1.0);
        add(new THREE.CylinderGeometry(0.25, 0.25, 0.85, 16, 1, false, 0, Math.PI), chestWood, 0.85, chestY + 0.42, -1.0, 0, 0, Math.PI / 2);
        foreach (var bx in new[] { -0.3, 0.3 })
        {
            add(new THREE.BoxGeometry(0.06, 0.43, 0.515), iron, 0.85 + bx, chestY + 0.215, -1.0);
            add(new THREE.TorusGeometry(0.258, 0.022, 4, 16, Math.PI), iron, 0.85 + bx, chestY + 0.42, -1.0, 0, Math.PI / 2, 0);
            add(new THREE.TorusGeometry(0.07, 0.014, 4, 10, Math.PI), cord, 0.85 + bx * 1.45, chestY + 0.3, -1.0, 0, Math.PI / 2, Math.PI);
        }
        add(new THREE.BoxGeometry(0.11, 0.13, 0.02), iron, 0.85, chestY + 0.36, -0.74);
        add(new THREE.CylinderGeometry(0.014, 0.014, 0.02, 6), seam, 0.85, chestY + 0.35, -0.728, Math.PI / 2);
        solid(-0.8, -1.0, 1.15);
        solid(0.8, -1.0, 1.15);

        // ---- the fire: a ring of stones round a mound of ash, half-burnt logs fallen in, and a spit over it with a pot
        const double fx = 0.3, fz = 2.3;
        double fireY = ground(fx, fz);
        for (int i = 0; i < 9; i++)
        {
            double a = i / 9.0 * Math.PI * 2 + cr() * 0.25;
            double r = 0.15 + cr() * 0.08;
            add(jitter(new THREE.IcosahedronGeometry(r, 1), 0.06, cr), stone, fx + Math.Cos(a) * 0.62, fireY + r * 0.45, fz + Math.Sin(a) * 0.62, 0, cr() * 3, 0, 1, 0.7, 1);
        }
        add(jitter(new THREE.IcosahedronGeometry(0.45, 2), 0.05, cr), new THREE.MeshStandardMaterial { color = 0x6b665f, roughness = 1 }, fx, fireY + 0.01, fz, 0, 0, 0, 1, 0.16, 1);
        var burnt = new THREE.MeshStandardMaterial { color = 0x2a2420, roughness = 1 };
        var charred = new THREE.MeshStandardMaterial { color = 0x5a4634, roughness = 1 };
        for (int i = 0; i < 4; i++)
        {
            double a = i / 4.0 * Math.PI * 2 + 0.4;
            var outer = V(fx + Math.Cos(a) * 0.5, fireY + 0.06, fz + Math.Sin(a) * 0.5);
            var inner = V(fx + Math.Cos(a + 0.3) * 0.08, fireY + 0.16 + (i % 2) * 0.05, fz + Math.Sin(a + 0.3) * 0.08);
            // the outer end is still wood, the inner end burnt black
            var midPt = outer.clone().lerp(inner, 0.45);
            wood(outer, midPt, 0.055, charred, 0.02);
            wood(midPt, inner, 0.05, burnt, 0.02);
        }
        foreach (var side in new[] { -1, 1 })
        {
            double px = fx + side * 0.78;
            double py = ground(px, fz);
            wood(V(px, py - 0.15, fz), V(px, py + 0.72, fz), 0.03, drift, 0.04);
            wood(V(px, py + 0.62, fz), V(px - side * 0.03, py + 0.8, fz + 0.09), 0.022, drift, 0.02);
            wood(V(px, py + 0.62, fz), V(px + side * 0.03, py + 0.79, fz - 0.09), 0.022, drift, 0.02);
        }
        wood(V(fx - 0.95, fireY + 0.72, fz), V(fx + 0.95, fireY + 0.73, fz), 0.022, driftDark, 0.03);
        var soot = new THREE.MeshStandardMaterial { color = 0x2b2c2e, roughness = 0.7, metalness = 0.3 };
        rope(V(fx + 0.1, fireY + 0.72, fz), V(fx + 0.1, fireY + 0.46, fz), 0.0, cord);
        add(new THREE.CylinderGeometry(0.13, 0.1, 0.17, 12, 1, true), soot, fx + 0.1, fireY + 0.37, fz);
        add(new THREE.CircleGeometry(0.1, 12), soot, fx + 0.1, fireY + 0.285, fz, Math.PI / 2);
        add(new THREE.TorusGeometry(0.13, 0.008, 4, 14, Math.PI), soot, fx + 0.1, fireY + 0.455, fz, 0, Math.PI / 2, 0);
        solid(fx, fz, 0.8);

        // ---- a driftwood log to sit on, with a broken branch, and the water barrel
        double seatY = ground(-1.7, 2.9);
        {
            // a sawn log, a little thicker at one end, settled into the sand: bark round it, pale end grain with its rings at the
            // cut ends, and one short branch stub. Made along y, then laid down along the seat's line
            var bark = new THREE.MeshStandardMaterial { color = 0x6f573f, roughness = 1 };
            var grain = new THREE.MeshStandardMaterial { color = 0xc8a878, roughness = 1 };
            var rings = new THREE.MeshStandardMaterial { color = 0x9c7f55, roughness = 1 };
            const double HALF = 0.9, R_TOP = 0.17, R_BOTTOM = 0.2;
            var parts2 = new List<(THREE.BufferGeometry geo, THREE.Material mat)>();
            parts2.Add((jitter(new THREE.CylinderGeometry(R_TOP, R_BOTTOM, HALF * 2, 14, 5, true), 0.012, cr), bark));
            foreach (var end in new[] { 1, -1 })
            {
                double r = end > 0 ? R_TOP : R_BOTTOM;
                THREE.BufferGeometry cap(THREE.BufferGeometry geo, double lift)
                {
                    geo.rotateX(-end * Math.PI / 2);
                    return geo.translate(0, end * (HALF + lift), 0);
                }
                parts2.Add((cap(new THREE.CircleGeometry(r * 0.98, 14), 0), grain));
                parts2.Add((cap(new THREE.RingGeometry(r * 0.42, r * 0.47, 14), 0.003), rings));
                parts2.Add((cap(new THREE.RingGeometry(r * 0.7, r * 0.74, 14), 0.003), rings));
                parts2.Add((cap(new THREE.RingGeometry(r * 0.86, r * 0.99, 14), 0.003), bark));
            }
            // the branch stub: out of the side and a little up, sawn off short
            THREE.BufferGeometry branch(THREE.BufferGeometry geo)
            {
                geo.rotateX(-Math.PI / 2);
                geo.rotateY(-0.6);
                return geo.translate(0, 0.32, 0);
            }
            parts2.Add((branch(new THREE.CylinderGeometry(0.045, 0.06, 0.24, 8, 1, true).translate(0, 0.27, 0)), bark));
            parts2.Add((branch(new THREE.CircleGeometry(0.045, 8).rotateX(-Math.PI / 2).translate(0, 0.39, 0)), grain));
            double seatY0 = seatY + 0.15;
            foreach (var (geo, mat) in parts2) add(geo, mat, -1.7, seatY0, 2.9, Math.PI / 2, 0.9, 0, order: "YXZ");
        }
        solid(-1.25, 3.25, 0.45);
        solid(-2.15, 2.55, 0.45);
        double barrelY = ground(2.5, 0.5);
        double staveR(double y) => 0.25 + 0.05 * (1 - (y / 0.4) * (y / 0.4));
        var profile = new List<THREE.Vector2>();
        for (int i = 0; i <= 8; i++)
        {
            double y = -0.4 + i * 0.1;
            profile.Add(new THREE.Vector2(staveR(y), y));
        }
        add(new THREE.LatheGeometry(profile, 14), new THREE.MeshStandardMaterial { color = 0x7b5a36, roughness = 0.9, flatShading = true }, 2.5, barrelY + 0.4, 0.5, 0, 0.4, 0);
        add(new THREE.CircleGeometry(0.245, 14), new THREE.MeshStandardMaterial { color = 0x5e4428, roughness = 1 }, 2.5, barrelY + 0.77, 0.5, -Math.PI / 2);
        foreach (var hy in new[] { -0.33, -0.2, 0.2, 0.33 })
            add(new THREE.TorusGeometry(staveR(hy) + 0.006, 0.016, 4, 20), iron, 2.5, barrelY + 0.4 + hy, 0.5, Math.PI / 2);
        solid(2.5, 0.5, 0.5);

        // ---- the signal mast, with a spar tied across it, rope stays pegged into the sand, and a red rag that stirs in the wind
        const double mx = 3.4, mz = -1.0;
        double mastY = ground(mx, mz);
        wood(V(mx, mastY - 0.3, mz), V(mx + 0.04, mastY + 6.1, mz), 0.085, drift, 0.14);
        wood(V(mx - 0.75, mastY + 5.6, mz + 0.04), V(mx + 0.78, mastY + 5.62, mz - 0.03), 0.035, driftDark, 0.06);
        lash(mx + 0.02, mastY + 5.6, mz, 0.1, 0, 0, cord);
        foreach (var sx in new[] { -1, 1 })
        {
            double sxw = mx + sx * 1.9;
            double peg = ground(sxw, mz);
            rope(V(mx + 0.03, mastY + 4.2, mz), V(sxw, peg + 0.18, mz), 0.12, cord);
            wood(V(sxw, peg - 0.1, mz), V(sxw - sx * 0.05, peg + 0.25, mz), 0.03, driftDark, 0.01);
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

        foreach (var kv in parts)
        {
            var merged = THREE.BufferGeometryUtils.mergeGeometries(kv.Value);
            if (merged == null)
            {
                UnityEngine.Debug.LogWarning("Island camp: could not merge one material's parts; drawing them one by one");
                foreach (var one in kv.Value) gs.add(new THREE.Mesh(one, kv.Key) { castShadow = true, receiveShadow = true });
                continue;
            }
            merged.computeBoundingSphere();
            gs.add(new THREE.Mesh(merged, kv.Key) { castShadow = true, receiveShadow = true });
        }
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

    /// <summary>which way from the middle of the cay the telescope stands: along the ring from the camp's dune, facing the open sea to the north</summary>
    private const double SCOPE_ANGLE = LAGOON_GAP + Math.PI + 0.9;

    /// <summary>The telescope, on the crest of the ring a little way round from the camp, its tube turned out to sea.</summary>
    private void buildTelescope()
    {
        var at = scopeSpot.Value;
        double ox = at.x - spec.x, oz = at.z - spec.z;
        telescope = new Telescope(at.x, at.z, baseHeight(at.x, at.z) - 0.03, Math.Atan2(ox, oz));
        group.add(telescope.group);
        colliders.Add(new Spot { x = at.x, z = at.z, r = 0.55 });
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
            lighthouse.update(t, dt, Math.Max(0, Math.Min(1, (0.42 - light) / 0.27)), surfaceY);
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
