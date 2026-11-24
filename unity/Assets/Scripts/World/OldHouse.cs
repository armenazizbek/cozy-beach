using System;
using System.Collections.Generic;
using static Beach;

/// <summary>
/// A very old wooden house on stilts past the nets on the left of the beach (as the map shows it): grey, sun-bleached
/// planks with gaps where boards have fallen, a sagging shingle roof with a hole in it and moss in its shade, a door hanging
/// open, broken shutters, a porch with a leaning post and a snapped rail, and a stone chimney that has started to lean.
/// It faces the beach, and it can be walked into: up the steps, across the porch and in at the door (<see cref="walk"/>).
/// Everything in it is welded into one mesh and none of it moves, so it costs one draw and nothing per frame. Inside, the
/// walls and the roof are the same boards seen from within: the light comes through their gaps and the hole in the roof.
/// </summary>
public class OldHouse
{
    /// <summary>where it stands: well outside the nets on the beach's left, a little inland of the camp's line to the sea</summary>
    public const double X = -88, Z = -8;
    /// <summary>how wide it is along its front, how deep, how tall the walls are, and how deep the porch</summary>
    private const double W = 6.2, D = 4.6, WALL_H = 2.5, RISE = 1.55, PORCH = 1.7;

    public readonly THREE.Group group = new THREE.Group { name = "old house" };
    /// <summary>what blocks the golf cart, in the world (people walk in and out, see walk)</summary>
    public readonly List<Camp.Solid> cartSolids = new List<Camp.Solid>();
    /// <summary>what blocks walking and driving alike: the chimney, which stands outside the walls</summary>
    public readonly List<Camp.Solid> solids = new List<Camp.Solid>();
    /// <summary>the ground it takes, for the stones and the grass to keep clear of</summary>
    public static Spot footprint => new Spot { x = X, z = Z, r = 6.5 };

    private readonly Func<double> rand = Stones.rng(1897);
    private readonly List<THREE.BufferGeometry> wood = new List<THREE.BufferGeometry>();
    private readonly THREE.Object3D place = new THREE.Object3D();
    private double floorY;
    /// <summary>the walls, rails and posts that stop someone walking, as segments in the house's own frame (x0, z0, x1, z1)</summary>
    private readonly List<(double x0, double z0, double x1, double z1)> walls = new List<(double, double, double, double)>();
    private const double BODY = 0.3;
    /// <summary>how far out the steps reach from the porch, and how wide they are</summary>
    private const double STEPS_HALF = 0.62;
    /// <summary>how far out the steps reach from the porch (set by buildPorch from the height they have to climb)</summary>
    private double STEPS = 0.95;

    private static readonly int[] BOARDS = { 0x8a7a66, 0x7d6e5c, 0x948571, 0x6f6252, 0x9a8b75, 0x85755f };
    private const int SHUTTER = 0x5f8a80;
    private const int POST = 0x5e5144, DARK = 0x4a3f34, MOSS = 0x5f6b3c, STONE = 0x7b766c, RUST = 0x6e3f22;

    public OldHouse()
    {
        // the front (the door) faces +z in the house's own frame; turned a quarter it faces the beach (+x)
        group.rotation.y = Math.PI / 2;
        double ground = baseHeight(X, Z);
        group.position.set(X, ground, Z);
        // the floor stands on its posts a little above the highest sand under it
        double hi = double.NegativeInfinity, lo = double.PositiveInfinity;
        for (double a = -1; a <= 1; a += 0.5)
            for (double b = -1; b <= 1.4; b += 0.6)
            {
                // the house's +z (front) is the world's +x, its +x the world's -z
                double h = baseHeight(X + b * (D / 2 + PORCH * 0.5), Z - a * W / 2) - ground;
                hi = Math.Max(hi, h);
                lo = Math.Min(lo, h);
            }
        floorY = hi + 0.7;
        double postFoot = lo - 0.6;

        buildPosts(postFoot);
        buildFloor();
        buildWalls();
        buildDoorAndWindows();
        buildRoof();
        buildPorch(postFoot);
        buildChimney(postFoot);
        buildClutter();
        buildWater();
        // the pictures first: their frames go in with the wood, which is welded together next
        var pictures = buildPictures();

        var woodMesh = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(wood), new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1, metalness = 0 });
        woodMesh.castShadow = true;
        woodMesh.receiveShadow = true;
        group.add(woodMesh);
        group.add(pictures);
        // nothing in it ever moves: the scene copy skips it (Object3D.syncStatic)
        group.syncStatic = true;

        // the cart cannot get in: the house and its porch are solid to it
        foreach (var dz in new[] { -1.7, 0, 1.7 }) cartSolids.Add(new Camp.Solid { x = X - 0.2, z = Z + dz, r = 2.5 });
        foreach (var dz in new[] { -2.2, 0, 2.2 }) cartSolids.Add(new Camp.Solid { x = X + D / 2 + PORCH * 0.55, z = Z + dz, r = 1.15 });
        var (cxw, czw) = toWorld(-W / 2 - 0.42, 0);
        solids.Add(new Camp.Solid { x = cxw, z = czw, r = 0.75 });

        // what someone walking bumps into: the walls with a gap for the door, the porch's ends, and its rail with a gap for
        // the steps. Together they close the house off all round, so the only way in (and under nothing) is up the steps
        double z1 = D / 2 + PORCH - 0.15;
        walls.Add((-W / 2, -D / 2, W / 2, -D / 2));
        walls.Add((-W / 2, -D / 2, -W / 2, z1));
        walls.Add((W / 2, -D / 2, W / 2, z1));
        walls.Add((-W / 2, D / 2, -0.56, D / 2));
        walls.Add((0.56, D / 2, W / 2, D / 2));
        walls.Add((-W / 2, z1, -STEPS_HALF - 0.05, z1));
        walls.Add((STEPS_HALF + 0.05, z1, W / 2, z1));
        // the steps' sides, so they are climbed from the front
        walls.Add((-STEPS_HALF - 0.05, z1, -STEPS_HALF - 0.05, z1 + STEPS));
        walls.Add((STEPS_HALF + 0.05, z1, STEPS_HALF + 0.05, z1 + STEPS));
        // the barrel on the porch
        walls.Add((-W / 2 + 0.6, D / 2 + 0.55, -W / 2 + 0.6, D / 2 + 0.55));
        // the table and chair under the left window inside
        walls.Add((-2.35, D / 2 - 0.55, -1.25, D / 2 - 0.55));
        walls.Add((-1.68, 1.02, -1.68, 1.02));
    }

    // ---------------------------------------------------------------- rain coming in

    /// <summary>
    /// The rain that comes in through the hole in the roof. It is apart from <see cref="group"/> (which never changes, and is copied
    /// to the scene once), standing in the same place and turned the same way, so it uses the house's own frame.
    /// </summary>
    public readonly THREE.Group water = new THREE.Group { name = "old house rain" };

    /// <summary>
    /// The house (with its paintings, which OldPictures paints into one texture) and the rain that drips and pools inside it,
    /// each realised once under World.Land (unity/UI_PLAN.md B1). The house does not move: only its materials are pushed (the
    /// paintings' texture can arrive after it is placed). The rain brings in line, by name, its group, the puddle, the damp
    /// patch, the rings and the drip lines, the drips' mesh when it is rewritten, and its materials' fading.
    /// </summary>
    private Realized live, liveWater;
    private readonly List<(THREE.Object3D o, UnityEngine.Transform t)> waterParts = new List<(THREE.Object3D, UnityEngine.Transform)>();
    private readonly List<(THREE.Material m, MaterialKit.On on)> mats = new List<(THREE.Material, MaterialKit.On)>();
    private UnityEngine.Mesh dripMesh;
    private int dripVersion = -1;

    /// <summary>Puts the house and its rain into the world as native objects.</summary>
    public void Place()
    {
        live = Realizer.Realize(group, World.Land, World.Layer);
        liveWater = Realizer.Realize(water, World.Land, World.Layer);
        foreach (var r in new[] { live, liveWater })
            foreach (var kv in r.map)
            {
                THREE.Material m = null;
                var on = MaterialKit.On.Mesh;
                switch (kv.Key)
                {
                    case THREE.Mesh mesh: m = mesh.material; break;
                    case THREE.Line line: m = line.material; on = MaterialKit.On.Line; break;
                    case THREE.Points pts: m = pts.material; on = MaterialKit.On.Points; break;
                }
                if (m != null && !mats.Contains((m, on))) mats.Add((m, on));
            }
        var parts = new List<THREE.Object3D> { water, puddle, damp, dripLines };
        if (rings != null) parts.AddRange(rings);
        foreach (var o in parts)
        {
            var t = o != null ? liveWater[o] : null;
            if (t != null) waterParts.Add((o, t));
        }
        dripMesh = liveWater[dripLines]?.GetComponent<UnityEngine.MeshFilter>()?.sharedMesh;
        if (dripMesh != null) dripVersion = dripLines.geometry.ContentVersion();
        NativeFrame.BeforeDraw += Sync;
    }

    /// <summary>Brings the rain in the house in line, and pushes the materials, before the frame is drawn.</summary>
    private void Sync()
    {
        if (live == null) return;
        if (live.root.parent != null && !live.root.parent.gameObject.activeInHierarchy) return;
        foreach (var (m, on) in mats) MaterialKit.PushOnce(m, on);
        // the rain, hidden, costs nothing but switching it off (as the whole-tree copy left it)
        var waterT = liveWater.root;
        if (waterT.gameObject.activeSelf != water.visible) waterT.gameObject.SetActive(water.visible);
        if (!water.visible) return;
        foreach (var (o, t) in waterParts)
        {
            if (o == water) continue;
            if (t.gameObject.activeSelf != o.visible) t.gameObject.SetActive(o.visible);
            if (o.visible) Realizer.Pose(o, t);
        }
        Realizer.Pose(water, waterT);
        // the drips are rewritten as they fall; refilled whenever the rain shows, as before
        if (dripMesh != null)
        {
            int v = dripLines.geometry.ContentVersion();
            if (v != dripVersion)
            {
                MeshKit.RefillLines(dripMesh, dripLines.geometry, true);
                dripVersion = v;
            }
        }
    }

    /// <summary>where the rain falls through the roof, on the floor, in the house's frame: the back half, away from the door and the dog's corner</summary>
    private const double LEAK_X = -1.4, LEAK_Z = -1.4;
    private const int DRIPS = 26;
    private THREE.Mesh puddle, damp;
    private THREE.Mesh[] rings;
    private double[] ringPhase;
    private THREE.LineSegments dripLines;
    private float[] dripPos;
    private double[] dripSeed;
    private THREE.LineBasicMaterial dripMat;
    private THREE.MeshStandardMaterial puddleMat, dampMat;
    private THREE.MeshBasicMaterial[] ringMats;
    private double[] ringOx, ringOz;
    /// <summary>how much water is on the floor, 0..1: it gathers while it rains and dries out slowly after</summary>
    private double wet;
    private readonly Func<double> waterRand = Stones.rng(731);

    /// <summary>For the -autoshot-dograin test: as much water on the floor as after a long rain (0..1).</summary>
    internal void smokeWet(double v) => wet = v;

    /// <summary>A flat, irregular blob on the floor, of radius 1 at most: a puddle's edge is never a circle.</summary>
    private static THREE.BufferGeometry blob(double s1, double s2)
    {
        const int N = 36;
        var pos = new List<double> { 0, 0, 0 };
        var idx = new List<int>();
        for (int i = 0; i < N; i++)
        {
            double a = i * Math.PI * 2 / N;
            double rad = (0.86 + 0.12 * Math.Sin(3 * a + s1) + 0.07 * Math.Sin(5 * a + s2)) / 1.05;
            pos.AddRange(new[] { Math.Cos(a) * rad, 0, Math.Sin(a) * rad });
            idx.AddRange(new[] { 0, 1 + (i + 1) % N, 1 + i });
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    private void buildWater()
    {
        water.position.copy(group.position);
        water.rotation.y = group.rotation.y;
        // dark wet wood spreading round the standing water, then the water itself, a little above the boards
        dampMat = new THREE.MeshStandardMaterial { color = 0x1c140d, roughness = 1, transparent = true, opacity = 0.4, depthWrite = false, polygonOffset = true, polygonOffsetFactor = -1, polygonOffsetUnits = -1 };
        puddleMat = new THREE.MeshStandardMaterial
        {
            color = 0x3f5a6b, emissive = 0x1a2a38, emissiveIntensity = 0.7, roughness = 0.05, metalness = 0, transparent = true, opacity = 0.72, depthWrite = false,
            polygonOffset = true, polygonOffsetFactor = -2, polygonOffsetUnits = -2,
        };
        damp = new THREE.Mesh(blob(0.7, 2.1), dampMat) { visible = false, renderOrder = 1 };
        puddle = new THREE.Mesh(blob(2.4, 0.4), puddleMat) { visible = false, renderOrder = 2 };
        water.add(damp, puddle);
        // rings spreading from where the drops land, each with its own material so each can fade on its own
        var ringGeo = new THREE.RingGeometry(0.86, 1.0, 28);
        ringGeo.rotateX(-Math.PI / 2);
        rings = new THREE.Mesh[3];
        ringMats = new THREE.MeshBasicMaterial[3];
        ringPhase = new double[3];
        ringOx = new double[3];
        ringOz = new double[3];
        for (int i = 0; i < rings.Length; i++)
        {
            ringMats[i] = new THREE.MeshBasicMaterial { color = 0xe4f0f8, transparent = true, opacity = 0, depthWrite = false, side = THREE.Side.DoubleSide, polygonOffset = true, polygonOffsetFactor = -3, polygonOffsetUnits = -3 };
            rings[i] = new THREE.Mesh(ringGeo, ringMats[i]) { visible = false, renderOrder = 3 };
            ringPhase[i] = i / 3.0;
            water.add(rings[i]);
        }
        // the drops: short streaks falling through the hole to the floor
        dripPos = new float[DRIPS * 6];
        dripSeed = new double[DRIPS * 3];
        for (int i = 0; i < DRIPS; i++)
        {
            dripSeed[i * 3] = LEAK_X + (waterRand() - 0.5) * 0.9;
            dripSeed[i * 3 + 1] = waterRand();
            dripSeed[i * 3 + 2] = LEAK_Z + (waterRand() - 0.5) * 0.45;
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.BufferAttribute(dripPos, 3));
        dripMat = new THREE.LineBasicMaterial { color = 0xd5e6f2, transparent = true, opacity = 0, depthWrite = false, fog = false };
        dripLines = new THREE.LineSegments(geo, dripMat) { frustumCulled = false, visible = false };
        water.add(dripLines);
        water.visible = false;
    }

    /// <summary>
    /// While it rains, water comes in through the hole in the roof: drops fall to the floor and ripple where they land, and a puddle
    /// gathers and spreads, with the boards dark and damp round it. After the rain it dries away slowly. `dogX`, `dogZ` is where
    /// the dog is (in the world): it lies in the far corner, but the puddle is held back from it all the same, so the water
    /// can never reach it.
    /// </summary>
    public void updateWater(double dt, double t, double rain, double dogX, double dogZ)
    {
        bool falling = rain > 0.08;
        if (falling) wet = Math.Min(1, wet + dt * rain / 90);
        else wet = Math.Max(0, wet - dt / 150);
        water.visible = falling || wet > 0.004;
        if (!water.visible) return;

        double floor = floorY;
        // the puddle: it starts as a wet patch where the drops land, and spreads with the square root of the water
        double k = Math.Sqrt(wet);
        double r = wet > 0.003 ? 0.14 + 0.82 * k : 0;
        // held back from the dog, and from anything else standing near: the blob (with its damp ring) never gets within a
        // metre of it, however long it rains
        if (contains(dogX, dogZ))
        {
            var (dx, dz) = toLocal(dogX, dogZ);
            double dist = JS.Hypot(dx - LEAK_X, dz - LEAK_Z);
            r = Math.Min(r, Math.Max(0, (dist - 1.0) / 1.6));
        }
        puddle.visible = r > 0.02;
        damp.visible = r > 0.02;
        if (puddle.visible)
        {
            double breathe = 1 + Math.Sin(t * 1.3) * 0.012;
            puddle.position.set(LEAK_X, floor + 0.014, LEAK_Z);
            puddle.scale.set(r * 1.18 * breathe, 1, r * 0.88 * breathe);
            puddle.rotation.y = 0.35;
            damp.position.set(LEAK_X, floor + 0.009, LEAK_Z);
            damp.scale.set(r * 1.6, 1, r * 1.2);
            damp.rotation.y = 0.35;
            puddleMat.opacity = 0.35 + 0.4 * Math.Min(1, wet * 3);
            dampMat.opacity = 0.42 * Math.Min(1, wet * 4);
        }

        // the drops
        dripLines.visible = falling;
        if (falling)
        {
            double top = floor + 3.1, fall = 7.0;
            dripMat.opacity = Math.Min(1, rain) * 0.85;
            for (int i = 0; i < DRIPS; i++)
            {
                // each drop falls at its own pace and starts again from the hole when it lands
                double ph = (dripSeed[i * 3 + 1] + t * (fall / (top - floor)) * (0.85 + 0.3 * ((i * 7) % 5) / 4.0)) % 1.0;
                // (ph runs 0 to 1 as the drop falls: from the hole at the top down to the floor)
                double y = top - ph * (top - floor - 0.03);
                double wx = dripSeed[i * 3] + Math.Sin(i * 3.1) * 0.02, wz = dripSeed[i * 3 + 2];
                dripPos[i * 6] = (float)wx;
                dripPos[i * 6 + 1] = (float)y;
                dripPos[i * 6 + 2] = (float)wz;
                dripPos[i * 6 + 3] = (float)(wx + 0.01);
                dripPos[i * 6 + 4] = (float)(y + 0.24);
                dripPos[i * 6 + 5] = (float)wz;
            }
            dripLines.geometry.attributes.position.needsUpdate = true;
        }

        // ripples: three rings in turn, each starting small at a fresh spot where a drop has landed and spreading out as it fades
        for (int i = 0; i < rings.Length; i++)
        {
            double old = ringPhase[i];
            ringPhase[i] = (ringPhase[i] + dt * 1.4) % 1.0;
            if (ringPhase[i] < old)
            {
                ringOx[i] = (waterRand() - 0.5) * 2;
                ringOz[i] = (waterRand() - 0.5) * 2;
            }
            bool on = falling && r > 0.05;
            rings[i].visible = on;
            if (!on) continue;
            double p = ringPhase[i];
            double reach = Math.Max(0.1, r * 0.75);
            rings[i].position.set(LEAK_X + ringOx[i] * reach * 0.6, floor + 0.02, LEAK_Z + ringOz[i] * reach * 0.45);
            double s = 0.04 + p * 0.3;
            rings[i].scale.set(s, 1, s * 0.8);
            // bright as it starts, gone as it reaches its full size
            ringMats[i].opacity = (1 - p) * (1 - p) * 0.6 * Math.Min(1, wet * 6 + 0.3);
        }
    }

    // ---------------------------------------------------------------- walking in and out

    /// <summary>A point of the house's own frame in the world (its front, +z, faces the world's +x).</summary>
    private static (double x, double z) toWorld(double lx, double lz) => (X + lz, Z - lx);

    private static (double lx, double lz) toLocal(double x, double z) => (Z - z, x - X);

    /// <summary>True on the floor or the porch.</summary>
    /// <summary>Whether (x, z) is under the roof or the porch, or within <paramref name="margin"/> of them.</summary>
    public bool covers(double x, double z, double margin)
    {
        var (lx, lz) = toLocal(x, z);
        return Math.Abs(lx) < W / 2 + margin && lz > -D / 2 - margin && lz < D / 2 + PORCH + margin;
    }

    public bool contains(double x, double z)
    {
        var (lx, lz) = toLocal(x, z);
        return Math.Abs(lx) < W / 2 && lz > -D / 2 && lz < D / 2 + PORCH;
    }

    /// <summary>
    /// Someone walking from (x0, z0) to (x, z): kept out of the walls, the rails and the posts, and told the height of what
    /// they stand on (the floor, the porch, or a step of the stairs), or null when that is just the sand.
    /// </summary>
    public double? walk(double x0, double z0, ref double x, ref double z)
    {
        var (lx, lz) = toLocal(x, z);
        // far from the house nothing here matters
        if (Math.Abs(lx) > W / 2 + 2 || lz < -D / 2 - 2 || lz > D / 2 + PORCH + STEPS + 2) return null;
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var (ax, az, bx, bz) in walls)
            {
                double dx = bx - ax, dz = bz - az, len2 = dx * dx + dz * dz;
                double t = len2 > 1e-9 ? Math.Max(0, Math.Min(1, ((lx - ax) * dx + (lz - az) * dz) / len2)) : 0;
                double px = ax + dx * t, pz = az + dz * t;
                double ex = lx - px, ez = lz - pz, d = Math.Sqrt(ex * ex + ez * ez);
                double r = len2 > 1e-9 ? BODY : 0.62;
                if (d >= r) continue;
                if (d < 1e-6)
                {
                    // exactly on it: back the way they came
                    var (ox, oz) = toLocal(x0, z0);
                    ex = ox - px;
                    ez = oz - pz;
                    d = Math.Max(1e-6, Math.Sqrt(ex * ex + ez * ez));
                }
                lx = px + ex / d * r;
                lz = pz + ez / d * r;
            }
        }
        var (wx, wz) = toWorld(lx, lz);
        x = wx;
        z = wz;
        return floorAt(x, z);
    }

    /// <summary>The height of what stands at a point: the floor, the porch or a step, or null where it is just the sand.</summary>
    public double? floorAt(double x, double z)
    {
        var (lx, lz) = toLocal(x, z);
        double floor = group.position.y + floorY;
        if (Math.Abs(lx) < W / 2 && lz > -D / 2 && lz < D / 2 + PORCH) return floor;
        double front = D / 2 + PORCH;
        if (Math.Abs(lx) < STEPS_HALF + 0.05 && lz >= front && lz < front + STEPS)
        {
            // up the steps: from the sand at their foot to the porch at their top
            double k = (front + STEPS - lz) / STEPS;
            double sand = baseHeight(x, z);
            return sand + (floor - sand) * k;
        }
        return null;
    }

    /// <summary>True within a few metres of the house, where its own walls (not the cart's rough circles) are what count.</summary>
    public bool near(double x, double z) => JS.Hypot(x - X, z - Z) < 8.5;

    /// <summary>
    /// The dog's way in out of the rain, in the world: through the gap in the nets on this side, across to the foot of the
    /// steps, up them, over the porch and in at the door to a dry corner of the room. Back out is the same way reversed.
    /// </summary>
    public List<(double x, double z)> shelterRoute()
    {
        var gapZ = CoveNets.TOP_Z + 59;
        var list = new List<(double x, double z)> { (-CoveNets.SIDE_X + 5, gapZ), (-CoveNets.SIDE_X - 5, gapZ) };
        foreach (var (lx, lz) in new[] { (0.0, D / 2 + PORCH + STEPS + 0.6), (0.0, D / 2 + PORCH - 0.5), (0.0, D / 2 - 0.3), (1.6, -0.9) })
            list.Add(toWorld(lx, lz));
        return list;
    }

    /// <summary>Which way the dog lies in its corner: looking out at the door (the world's +x).</summary>
    public const double SHELTER_HEADING = -Math.PI / 2;

    // ---------------------------------------------------------------- pieces

    /// <summary>The sand's height under a point of the house's own frame, measured from the house's own origin.</summary>
    private double sandAt(double lx, double lz)
    {
        var (wx, wz) = toWorld(lx, lz);
        return baseHeight(wx, wz) - group.position.y;
    }

    private int board() => BOARDS[(int)Math.Floor(rand() * BOARDS.Length)];

    private static int mix(int a, int b, double t)
    {
        var c = new THREE.Color(a).lerp(new THREE.Color(b), t);
        return c.getHex();
    }

    /// <summary>A box of wood, turned and placed in the house's frame, in a colour.</summary>
    private void box(double w, double h, double d, double x, double y, double z, double rx, double ry, double rz, int color, List<THREE.BufferGeometry> into = null)
    {
        var g = Ecology.paint(new THREE.BoxGeometry(w, h, d), color);
        place.position.set(x, y, z);
        place.rotation.set(rx, ry, rz);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        g.applyMatrix4(place.matrix);
        (into ?? wood).Add(g);
    }

    private void post(double x, double z, double top, double foot, double lean = 0, double r = 0.11)
    {
        var g = Ecology.paint(new THREE.CylinderGeometry(r * 0.85, r, top - foot, 7), POST);
        place.position.set(x, (top + foot) / 2, z);
        place.rotation.set(lean * 0.4, rand() * 3, lean);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        g.applyMatrix4(place.matrix);
        wood.Add(g);
    }

    /// <summary>A horizontal board along x from x0 to x1, at height y, in a wall at depth z (or along z when `alongZ`).</summary>
    private void plank(double a0, double a1, double y, double at, bool alongZ, double thick = 0.06, double tall = 0.19)
    {
        if (a1 - a0 < 0.12) return;
        double len = a1 - a0, mid = (a0 + a1) / 2;
        double tilt = (rand() - 0.5) * 0.035;
        double shift = (rand() - 0.5) * 0.02;
        int c = board();
        if (alongZ) box(thick, tall, len, at + shift, y, mid, tilt, 0, 0, c);
        else box(len, tall, thick, mid, y, at + shift, 0, 0, tilt, c);
    }

    private void buildPosts(double foot)
    {
        foreach (var x in new[] { -W / 2 + 0.1, 0, W / 2 - 0.1 })
            foreach (var z in new[] { -D / 2 + 0.1, D / 2 - 0.1 })
                post(x, z, floorY, foot, (rand() - 0.5) * 0.05);
    }

    private void buildFloor()
    {
        // the floor's boards run front to back, under the walls
        for (double x = -W / 2; x < W / 2 - 0.05; x += 0.26)
            box(0.24, 0.07, D + 0.1, x + 0.13, floorY - 0.035, 0, 0, 0, (rand() - 0.5) * 0.02, board());
        // joists under them
        foreach (var z in new[] { -D / 2 + 0.15, 0, D / 2 - 0.15 }) box(W, 0.14, 0.12, 0, floorY - 0.14, z, 0, 0, 0, POST);
        // the tie beams across the room under the roof
        for (double x = -W / 2 + 0.75; x < W / 2; x += 1.55) box(0.12, 0.14, D - 0.06, x, floorY + WALL_H - 0.02, 0, 0, 0, 0, POST);
    }

    private void buildWalls()
    {
        double y0 = floorY + 0.1;
        int rows = (int)Math.Round(WALL_H / 0.21);
        for (int r = 0; r < rows; r++)
        {
            double y = y0 + r * 0.21;
            double h = y - floorY;
            // a board fallen off here and there leaves a gap
            bool back = rand() > 0.07, left = rand() > 0.06, right = rand() > 0.08;
            if (back) plank(-W / 2, W / 2, y, -D / 2, false);
            if (left) plank(-D / 2, D / 2, y, -W / 2, true);
            if (right) plank(-D / 2, D / 2, y, W / 2, true);
            // the front: open for the door and the two windows
            var cuts = new List<(double, double)>();
            if (h < 2.05) cuts.Add((-0.52, 0.52));
            if (h > 0.95 && h < 1.95) { cuts.Add((-2.25, -1.25)); cuts.Add((1.25, 2.25)); }
            cuts.Sort();
            double from = -W / 2;
            foreach (var (a, b) in cuts)
            {
                plank(from, a, y, D / 2, false);
                from = b;
            }
            plank(from, W / 2, y, D / 2, false);
        }
        // the gables at the ends, under the ridge: shorter boards as the roof closes in
        for (double y = floorY + WALL_H + 0.1; y < floorY + WALL_H + RISE - 0.1; y += 0.21)
        {
            double k = 1 - (y - floorY - WALL_H) / RISE;
            double half = D / 2 * k;
            if (rand() > 0.1) plank(-half, half, y, -W / 2, true);
            plank(-half, half, y, W / 2, true);
        }
        // corner boards and the beam along the top of each wall
        foreach (var x in new[] { -W / 2, W / 2 })
            foreach (var z in new[] { -D / 2, D / 2 })
                box(0.14, WALL_H + 0.1, 0.14, x, floorY + WALL_H / 2 + 0.05, z, 0, 0, (rand() - 0.5) * 0.03, DARK);
        box(W + 0.2, 0.14, 0.14, 0, floorY + WALL_H + 0.05, D / 2, 0, 0, 0.012, DARK);
        box(W + 0.2, 0.14, 0.14, 0, floorY + WALL_H + 0.05, -D / 2, 0, 0, -0.008, DARK);
    }

    private void buildDoorAndWindows()
    {
        // the door frame
        box(0.12, 2.05, 0.12, -0.56, floorY + 1.02, D / 2 + 0.02, 0, 0, 0, DARK);
        box(0.12, 2.05, 0.12, 0.56, floorY + 1.02, D / 2 + 0.02, 0, 0, 0.02, DARK);
        box(1.24, 0.12, 0.12, 0, floorY + 2.1, D / 2 + 0.02, 0, 0, 0.03, DARK);
        // the door itself hangs half open on its lower hinge, sagging: four boards and two battens
        var door = new List<THREE.BufferGeometry>();
        for (int i = 0; i < 4; i++)
            box(0.23, 1.92, 0.05, 0.13 + i * 0.235, 0.96, 0, 0, 0, (rand() - 0.5) * 0.02, board(), door);
        box(0.94, 0.12, 0.05, 0.47, 0.45, 0.05, 0, 0, 0.05, DARK, door);
        box(0.94, 0.12, 0.05, 0.47, 1.45, 0.05, 0, 0, -0.04, DARK, door);
        var d = THREE.BufferGeometryUtils.mergeGeometries(door);
        place.position.set(-0.52, floorY + 0.03, D / 2 + 0.05);
        // swung right open, out onto the porch and back against the wall, so the doorway is clear
        place.rotation.set(0, -2.15, -0.04);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        d.applyMatrix4(place.matrix);
        wood.Add(d);

        foreach (var cx in new[] { -1.75, 1.75 })
        {
            double fz = D / 2 + 0.035, cy = floorY + 1.45;
            // the frame round the opening, a deeper sill under it, and a cross of glazing bars (the glass is long gone)
            box(1.16, 0.1, 0.18, cx, floorY + 0.93, fz + 0.02, 0, 0, 0, DARK);
            box(1.16, 0.1, 0.1, cx, floorY + 1.97, fz, 0, 0, 0, DARK);
            box(0.1, 1.06, 0.1, cx - 0.53, cy, fz, 0, 0, 0, DARK);
            box(0.1, 1.06, 0.1, cx + 0.53, cy, fz, 0, 0, 0, DARK);
            box(0.045, 0.96, 0.045, cx, cy, fz, 0, 0, 0, DARK);
            box(0.98, 0.045, 0.045, cx, cy, fz, 0, 0, 0, DARK);
            // a shutter either side, folded back flat against the boards; one hangs a little crooked on a tired hinge
            foreach (var side in new[] { -1, 1 })
            {
                var sh = new List<THREE.BufferGeometry>();
                // painted once, a sea green long since faded and flaking back to grey wood
                for (int i = 0; i < 3; i++) box(0.165, 1.0, 0.04, side * (0.09 + i * 0.172), 0, 0, 0, 0, 0, mix(SHUTTER, board(), 0.25 + rand() * 0.3), sh);
                box(0.5, 0.08, 0.03, side * 0.26, 0.3, 0.035, 0, 0, 0, mix(SHUTTER, DARK, 0.45), sh);
                box(0.5, 0.08, 0.03, side * 0.26, -0.3, 0.035, 0, 0, 0, mix(SHUTTER, DARK, 0.45), sh);
                var g = THREE.BufferGeometryUtils.mergeGeometries(sh);
                bool crooked = cx < 0 && side < 0;
                place.position.set(cx + side * 0.6, cy - (crooked ? 0.06 : 0), D / 2 + 0.075);
                place.rotation.set(0, 0, crooked ? side * -0.1 : (rand() - 0.5) * 0.02);
                place.scale.set(1, 1, 1);
                place.updateMatrix();
                g.applyMatrix4(place.matrix);
                wood.Add(g);
            }
        }
    }

    private void buildRoof()
    {
        double eave = floorY + WALL_H + 0.05, ridge = floorY + WALL_H + RISE;
        double run = D / 2 + 0.45;
        double slope = Math.Atan2(RISE, D / 2);
        double len = Math.Sqrt(run * run + Math.Pow(RISE * run / (D / 2), 2));
        foreach (var side in new[] { -1, 1 })
        {
            int rows = 11;
            for (int r = 0; r < rows; r++)
            {
                double t = (r + 0.5) / rows;
                // the roof sags in the middle of its length, and more toward the eaves
                // t runs from the ridge (0) down to the eave (1)
                double z = side * t * run;
                double y = ridge - t * (RISE * run / (D / 2)) + 0.06 - (1 - t) * 0.02;
                // a hole where a few shingle rows are gone, on the side away from the beach
                bool hole = side < 0 && r >= 4 && r <= 6;
                double segs = 5;
                for (int s = 0; s < segs; s++)
                {
                    double x0 = -W / 2 - 0.4 + s * (W + 0.8) / segs, x1 = x0 + (W + 0.8) / segs;
                    if (hole && (s == 1 || (s == 2 && r == 5))) continue;
                    double mid = (x0 + x1) / 2;
                    double sag = 0.14 * Math.Sin(Math.PI * (mid + W / 2 + 0.4) / (W + 0.8)) * (0.5 + 0.5 * (1 - t));
                    // moss creeps over the rows nearest the eaves on the shady back
                    int c = board();
                    if (side < 0 && t > 0.55 && rand() < 0.6) c = mix(c, MOSS, 0.55 + rand() * 0.3);
                    else c = mix(c, 0x5c5248, 0.35);
                    // most boards overlap their neighbours a little; some have shrunk and leave a slit of daylight
                    double depth = len / rows + (rand() < 0.3 ? -0.05 : 0.012);
                    box(x1 - x0 + 0.04, 0.05, depth, mid, y - sag, z, side * slope, 0, (rand() - 0.5) * 0.03, c);
                }
            }
        }
        // the ridge beam, bowed by the years
        for (int s = 0; s < 4; s++)
        {
            double x = -W / 2 - 0.4 + (s + 0.5) * (W + 0.8) / 4;
            double sag = 0.13 * Math.Sin(Math.PI * (s + 0.5) / 4);
            box((W + 0.8) / 4 + 0.02, 0.16, 0.18, x, ridge + 0.1 - sag, 0, 0, 0, 0, DARK);
        }
        // the rafters seen under the eaves and in the hole
        for (double x = -W / 2; x <= W / 2 + 0.01; x += W / 4)
            foreach (var side in new[] { -1, 1 })
                box(0.1, 0.1, len, x, (eave + ridge) / 2 - 0.22, side * run / 2, side * slope, 0, 0, POST);
    }

    private void buildPorch(double foot)
    {
        double z0 = D / 2, z1 = D / 2 + PORCH;
        double py = floorY - 0.04;
        // its boards run along the front, a few gone, one snapped and fallen through
        for (double z = z0 + 0.12; z < z1; z += 0.24)
        {
            if (rand() < 0.1) continue;
            if (Math.Abs(z - (z0 + 1.0)) < 0.13)
            {
                box(W * 0.45, 0.06, 0.22, -W * 0.27, py, z, 0, 0, 0, board());
                box(W * 0.4, 0.06, 0.22, W * 0.28, sandAt(W * 0.28, z) + 0.05, z + 0.1, 0.05, 0.2, -0.04, board());
                continue;
            }
            box(W, 0.06, 0.22, 0, py, z, 0, 0, (rand() - 0.5) * 0.02, board());
        }
        foreach (var x in new[] { -W / 2 + 0.15, W / 2 - 0.15 }) post(x, z1 - 0.12, py, foot);
        // the steps down to the sand: as many as the height needs, each tread on two stringers that rest on the sand
        double foot0 = sandAt(0, z1 + 0.9);
        int n = Math.Max(2, (int)Math.Round((py - foot0) / 0.24));
        const double TREAD = 0.3;
        STEPS = n * TREAD + 0.1;
        double footY = sandAt(0, z1 + STEPS);
        double rise = (py - footY) / (n + 1);
        for (int i = 1; i <= n; i++)
            box(1.3, 0.06, 0.32, 0, py - i * rise, z1 + 0.05 + (i - 0.5) * TREAD, 0, 0, i == n ? 0.05 : (rand() - 0.5) * 0.02, board());
        foreach (var sx in new[] { -0.66, 0.66 })
        {
            double dz = STEPS, dy = py - footY;
            double L = Math.Sqrt(dz * dz + dy * dy);
            box(0.08, 0.16, L, sx, (py + footY) / 2 - 0.08, z1 + dz / 2, Math.Atan2(dy, dz), 0, 0, DARK);
        }

        // the porch roof: a lean-to under the eaves, on two posts; the right post leans and its corner has dropped
        double ry = floorY + WALL_H - 0.05;
        post(-W / 2 + 0.2, z1 - 0.15, ry - 0.45, py, 0);
        post(W / 2 - 0.2, z1 - 0.15, ry - 0.62, py, -0.1);
        for (int i = 0; i < 9; i++)
        {
            double x = -W / 2 - 0.2 + (i + 0.5) * (W + 0.4) / 9;
            double drop = 0.45 + 0.2 * Math.Max(0, (x + W / 2) / W);
            double a = Math.Atan2(drop, PORCH + 0.3);
            if (i == 6) continue; // one board gone
            box((W + 0.4) / 9 + 0.02, 0.05, PORCH + 0.45, x, ry - drop / 2, z0 + (PORCH + 0.3) / 2, a, 0, (rand() - 0.5) * 0.04, mix(board(), 0x5c5248, 0.3));
        }
        box(W + 0.3, 0.12, 0.12, 0.05, ry - 0.5, z1 - 0.15, 0, 0, -0.035, DARK);
        // the rail along the front, snapped on the right of the steps
        box(W / 2 - 0.8, 0.07, 0.07, -W / 4 - 0.4, py + 0.85, z1 - 0.15, 0, 0, 0.01, DARK);
        box(W / 2 - 1.6, 0.07, 0.07, W / 4 + 0.75, py + 0.85, z1 - 0.15, 0, 0, -0.03, DARK);
        // the snapped length of rail and one of its balusters lie on the porch boards where they fell
        box(0.85, 0.07, 0.07, 1.1, py + 0.065, z1 - 0.45, 0, 0.35, 0, DARK);
        box(0.05, 0.05, 0.8, 1.45, py + 0.055, z1 - 0.7, 0, 0.9, 0, board());
        for (double x = -W / 2 + 0.4; x < -0.7; x += 0.35) box(0.05, 0.8, 0.05, x, py + 0.43, z1 - 0.15, 0, 0, (rand() - 0.5) * 0.08, board());
        for (double x = 1.9; x < W / 2 - 0.2; x += 0.35) box(0.05, 0.8, 0.05, x, py + 0.43, z1 - 0.15, 0, 0, (rand() - 0.5) * 0.08, board());
    }

    private void buildChimney(double foot)
    {
        // stones stacked up the left end, out from the wall, leaning a little away from it toward the top
        double x = -W / 2 - 0.42, top = floorY + WALL_H + RISE + 0.9;
        int n = 0;
        for (double y = foot + 0.6; y < top; y += 0.3, n++)
        {
            double k = (y - foot) / (top - foot);
            double lean = k * k * 0.22;
            for (int i = 0; i < 2; i++)
            {
                int c = mix(STONE, rand() < 0.5 ? 0x5e5a52 : 0x8e877a, rand() * 0.7);
                if (y > floorY + WALL_H && rand() < 0.25) c = mix(c, MOSS, 0.4);
                double w = (k < 0.5 ? 0.85 : 0.6) / 2 + rand() * 0.05;
                box(w, 0.28, 0.6 + rand() * 0.08, x - lean + (i - 0.5) * w * 0.98, y, (rand() - 0.5) * 0.05, 0, (rand() - 0.5) * 0.12, (rand() - 0.5) * 0.06, c);
            }
        }
        // a rusted cap sitting crooked on top
        box(0.5, 0.06, 0.5, x - 0.24, top + 0.08, 0, 0.12, 0.3, 0.1, RUST);
    }

    // ---------------------------------------------------------------- the pictures on the walls

    /// <summary>
    /// Captain Holt's portrait and his ship on the back wall, each in its frame and hanging a little crooked. The paintings share one texture (OldPictures); the frames go in with the wood.
    /// </summary>
    private THREE.Mesh buildPictures()
    {
        var pics = new List<THREE.BufferGeometry>();
        double backZ = -D / 2 + 0.075;
        hang(pics, OldPictures.CAPTAIN, 0.6, 0.78, 0.085, 0xa07a34, -1.35, floorY + 1.55, backZ, 0, 0.03);
        hang(pics, OldPictures.SHIP, 1.15, 0.775, 0.085, 0x6e4a26, 0.75, floorY + 1.5, backZ, 0, -0.02);
        // the chart on the table, flat on the parchment sheet buildTable makes: the table's and the sheet's own placing, composed
        {
            const double th = 0.74;
            place.position.set(-1.8, floorY, D / 2 - 0.52);
            place.rotation.set(0, 0.04, 0);
            place.scale.set(1, 1, 1);
            place.updateMatrix();
            var tableM = place.matrix.clone();
            place.position.set(-0.12, th + 0.0085, -0.02);
            place.rotation.set(0, -0.14, 0);
            place.updateMatrix();
            var sheetM = new THREE.Matrix4().multiplyMatrices(tableM, place.matrix);
            var chart = new THREE.PlaneGeometry(0.45, 0.31);
            chart.rotateX(-Math.PI / 2);
            // its top edge toward the window, away from whoever sits in the chair
            chart.rotateY(Math.PI);
            var cuv = chart.attributes.uv;
            var c = OldPictures.CHART;
            double cu0 = (double)c.x / OldPictures.SIZE, cu1 = (double)(c.x + c.w) / OldPictures.SIZE;
            double cv1 = 1 - (double)c.y / OldPictures.SIZE, cv0 = 1 - (double)(c.y + c.h) / OldPictures.SIZE;
            for (int i = 0; i < cuv.count; i++) cuv.setXY(i, cu0 + cuv.getX(i) * (cu1 - cu0), cv0 + cuv.getY(i) * (cv1 - cv0));
            chart.applyMatrix4(sheetM);
            pics.Add(chart);
        }
        var mat = new THREE.MeshStandardMaterial { map = OldPictures.make(), roughness = 0.75, metalness = 0 };
        var m = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(pics), mat) { receiveShadow = true };
        return m;
    }

    /// <summary>One framed picture: the part of the texture at `r`, w by h metres, its frame `bar` wide, hung at (x, y, z)
    /// facing out from its wall (turned `ry`), tilted `rz`.</summary>
    private void hang(List<THREE.BufferGeometry> pics, (int x, int y, int w, int h) r, double w, double h, double bar, int frame,
        double x, double y, double z, double ry, double rz)
    {
        place.position.set(x, y, z);
        place.rotation.set(0, ry, rz);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        var m = place.matrix.clone();
        // the painting, its corners mapped onto its part of the texture (the canvas's top row is the texture's top)
        var g = new THREE.PlaneGeometry(w, h);
        var uv = g.attributes.uv;
        double u0 = (double)r.x / OldPictures.SIZE, u1 = (double)(r.x + r.w) / OldPictures.SIZE;
        double v1 = 1 - (double)r.y / OldPictures.SIZE, v0 = 1 - (double)(r.y + r.h) / OldPictures.SIZE;
        for (int i = 0; i < uv.count; i++) uv.setXY(i, u0 + uv.getX(i) * (u1 - u0), v0 + uv.getY(i) * (v1 - v0));
        g.translate(0, 0, 0.012);
        g.applyMatrix4(m);
        pics.Add(g);
        // the frame: four bars round it, standing a little proud of the painting, and a darker backing board
        var parts = new List<THREE.BufferGeometry>();
        box(w + 2 * bar, bar, 0.07, 0, h / 2 + bar / 2, 0.03, 0, 0, 0, frame, parts);
        box(w + 2 * bar, bar, 0.07, 0, -h / 2 - bar / 2, 0.03, 0, 0, 0, frame, parts);
        box(bar, h, 0.07, -w / 2 - bar / 2, 0, 0.03, 0, 0, 0, frame, parts);
        box(bar, h, 0.07, w / 2 + bar / 2, 0, 0.03, 0, 0, 0, frame, parts);
        box(w + 0.02, h + 0.02, 0.012, 0, 0, 0, 0, 0, 0, DARK, parts);
        var f = THREE.BufferGeometryUtils.mergeGeometries(parts);
        f.applyMatrix4(m);
        wood.Add(f);
    }

    private void buildClutter()
    {
        double py = floorY - 0.04;
        // a barrel on the porch, its hoops gone rusty
        var bar = Ecology.paint(new THREE.CylinderGeometry(0.3, 0.27, 0.8, 12), 0x7a6650);
        place.position.set(-W / 2 + 0.6, py + 0.4, D / 2 + 0.55);
        place.rotation.set(0, 0, 0);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        bar.applyMatrix4(place.matrix);
        wood.Add(bar);
        foreach (var hy in new[] { 0.15, 0.65 })
        {
            var hoop = Ecology.paint(new THREE.TorusGeometry(0.3, 0.018, 4, 16), RUST);
            place.position.set(-W / 2 + 0.6, py + hy, D / 2 + 0.55);
            place.rotation.set(Math.PI / 2, 0, 0);
            place.updateMatrix();
            hoop.applyMatrix4(place.matrix);
            wood.Add(hoop);
        }
        // a broken crate tipped on its side by the steps, half buried in the sand
        double cz = D / 2 + PORCH + 0.8, cs = sandAt(1.6, cz);
        box(0.6, 0.45, 0.5, 1.6, cs + 0.12, cz, 0.35, 0.5, 0.25, board());
        box(0.62, 0.05, 0.52, 1.95, cs + 0.05, cz + 0.4, 0.05, 0.5, 0.1, board());
        // boards that came off the walls, lying in the sand along the side
        for (int i = 0; i < 4; i++)
        {
            double bx = W / 2 + 0.6 + rand() * 0.8, bz = -D / 2 + rand() * D;
            box(1.2 + rand() * 1.2, 0.05, 0.19, bx, sandAt(bx, bz) + 0.03, bz, 0, rand() * 3, (rand() - 0.5) * 0.04, board());
        }
        buildTable();
    }

    private void cyl(double rTop, double rBot, double h, int segs, double x, double y, double z, double rx, double ry, double rz, int color, List<THREE.BufferGeometry> into)
    {
        var g = Ecology.paint(new THREE.CylinderGeometry(rTop, rBot, h, segs), color);
        place.position.set(x, y, z);
        place.rotation.set(rx, ry, rz);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        g.applyMatrix4(place.matrix);
        into.Add(g);
    }

    /// <summary>
    /// Captain Holt's table and chair under the left window, with a half-unrolled chart of the cove, a stick of red sealing wax
    /// and an empty green bottle (see Story.cs: "I drew a map of this beach, marked the hatch... and sealed it in a green bottle").
    /// </summary>
    private void buildTable()
    {
        var tbl = new List<THREE.BufferGeometry>();
        const double tw = 1.16, td = 0.68, th = 0.74;
        // four legs, apron rails under the top, and a stretcher between the legs
        foreach (var lx in new[] { -tw / 2 + 0.08, tw / 2 - 0.08 })
            foreach (var lz in new[] { -td / 2 + 0.07, td / 2 - 0.07 })
                box(0.065, th - 0.04, 0.065, lx, (th - 0.04) / 2, lz, 0, 0, 0, POST, tbl);
        foreach (var lz in new[] { -td / 2 + 0.07, td / 2 - 0.07 })
            box(tw - 0.16, 0.08, 0.04, 0, th - 0.08, lz, 0, 0, 0, DARK, tbl);
        foreach (var lx in new[] { -tw / 2 + 0.08, tw / 2 - 0.08 })
        {
            box(0.04, 0.08, td - 0.14, lx, th - 0.08, 0, 0, 0, 0, DARK, tbl);
            box(0.045, 0.045, td - 0.14, lx, 0.18, 0, 0, 0, 0, POST, tbl);
        }
        box(tw - 0.16, 0.045, 0.045, 0, 0.18, 0, 0, 0, 0, POST, tbl);
        // four weathered planks for the tabletop
        for (int i = 0; i < 4; i++)
            box(tw, 0.042, 0.162, 0, th - 0.021, -0.255 + i * 0.17, 0, 0, 0, BOARDS[i % BOARDS.Length], tbl);

        double top = th;
        // an old sea chart unrolled across the boards, curled into a scroll at either end. The sheet is only a thin edge of aged
        // paper here: the chart itself is drawn on a plane laid over it (see buildPictures, OldPictures.chart), a chart of no place in particular
        var map = new List<THREE.BufferGeometry>();
        box(0.46, 0.008, 0.32, 0, 0.004, 0, 0, 0, 0, 0xb59a68, map);
        cyl(0.024, 0.024, 0.33, 10, -0.22, 0.024, 0, Math.PI / 2, 0, 0, 0xc2a874, map);
        cyl(0.015, 0.015, 0.334, 8, -0.22, 0.024, 0, Math.PI / 2, 0, 0, 0x5a4a32, map);
        cyl(0.014, 0.014, 0.325, 10, 0.22, 0.014, 0, Math.PI / 2, 0, 0, 0xc2a874, map);
        // a blob of red sealing wax holding down its corner
        cyl(0.022, 0.025, 0.008, 10, 0.13, 0.009, 0.09, 0, 0, 0, 0x9e201b, map);
        var mapGeo = THREE.BufferGeometryUtils.mergeGeometries(map);
        place.position.set(-0.12, top, -0.02);
        place.rotation.set(0, -0.14, 0);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        mapGeo.applyMatrix4(place.matrix);
        tbl.Add(mapGeo);

        // a stick of red sealing wax, a small brass signet stamp, and a cork beside the bottle
        box(0.13, 0.018, 0.024, 0.21, top + 0.009, -0.11, 0, 0.58, 0, 0xa8241e, tbl);
        box(0.018, 0.016, 0.022, 0.16, top + 0.008, -0.078, 0, 0.58, 0, 0x5c1410, tbl);
        cyl(0.018, 0.020, 0.014, 8, 0.15, top + 0.007, 0.02, 0, 0, 0, 0xb8923a, tbl);
        cyl(0.015, 0.010, 0.052, 8, 0.15, top + 0.038, 0.02, 0, 0, 0, DARK, tbl);
        cyl(0.014, 0.012, 0.034, 10, 0.22, top + 0.013, 0.15, Math.PI / 2, 0.4, 0.5, 0xb98b57, tbl);

        var tblGeo = THREE.BufferGeometryUtils.mergeGeometries(tbl);
        place.position.set(-1.8, floorY, D / 2 - 0.52);
        place.rotation.set(0, 0.04, 0);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        tblGeo.applyMatrix4(place.matrix);
        wood.Add(tblGeo);

        // the empty green glass bottle from LootGenerator, lying on the table where the window light catches it
        var bottle = LootGenerator.buildItem("glass_bottle");
        bottle.scale.setScalar(0.44);
        bottle.position.set(-1.8 + 0.35, floorY + top + 0.049, D / 2 - 0.52 + 0.10);
        bottle.rotation.set(0, -1.1, 0);
        group.add(bottle);

        // a wooden chair pushed back from the table and turned a little toward the door
        var ch = new List<THREE.BufferGeometry>();
        const double cw = 0.44, cd = 0.42, sh = 0.44;
        foreach (var lx in new[] { -cw / 2 + 0.035, cw / 2 - 0.035 })
        {
            box(0.048, sh, 0.048, lx, sh / 2, cd / 2 - 0.035, 0, 0, 0, POST, ch);
            box(0.048, 0.88, 0.048, lx, 0.44, -cd / 2 + 0.035, -0.04, 0, 0, POST, ch);
        }
        box(cw, 0.038, cd, 0, sh + 0.019, 0, 0, 0, 0, BOARDS[2], ch);
        box(cw - 0.06, 0.07, 0.026, 0, 0.65, -cd / 2 + 0.025, 0, 0, 0.02, BOARDS[0], ch);
        box(cw - 0.06, 0.075, 0.026, 0, 0.81, -cd / 2 + 0.018, 0, 0, -0.015, BOARDS[4], ch);
        var chGeo = THREE.BufferGeometryUtils.mergeGeometries(ch);
        place.position.set(-1.68, floorY, 1.02);
        place.rotation.set(0, -2.7, 0);
        place.scale.set(1, 1, 1);
        place.updateMatrix();
        chGeo.applyMatrix4(place.matrix);
        wood.Add(chGeo);
    }
}
