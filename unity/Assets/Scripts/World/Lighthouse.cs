using System;
using System.Collections.Generic;
using static Beach;

/// <summary>
/// The lighthouse on the summit of the ember rock. A white tower with red bands and a wooden door at its foot; inside, a stair
/// winds up round a stone column to the top, where it comes out on an open deck with a railing. In the middle of the deck
/// stands the lantern, a glass room with the lamp in it, and the deck runs all the way round it.
///
/// Initially the lighthouse is encrusted with sand and salt, and its lamp stays dark even at night. Once the player brushes it
/// clean (on the workbench, like a find from the beach), a short intro shows day turning to night, ships setting out along the
/// horizon and the lighthouse beams sweeping over them; from then on the lamp shines every night and ships travel the horizon.
///
/// It also says where the player can walk: the walls, the door, the column, the stair and the deck are all handled here
/// (<see cref="walk"/>), because none of them can be told from the height of the ground.
/// </summary>
public class Lighthouse
{
    // all in metres, from the middle of the tower and the ground it stands on
    private const double R0 = 3.4, R1 = 3.0, WALL = 0.35, CORE = 1.5, H = 13, TURN = 3.25;
    private const double DOOR_W = 1.2, DOOR_H = 2.3;
    /// <summary>the deck reaches out this far, and the railing stands at its edge</summary>
    private const double DECK = 3.75;
    /// <summary>how wide around the lantern's glass is</summary>
    private const double LANTERN = 1.5;
    /// <summary>how far round the last turn of the stair the hole in the deck begins: below that, there is headroom under it</summary>
    private const double HOLE = 0.385;
    /// <summary>
    /// how far the player's eye is kept from every wall, the column, the stair and the door: enough that neither the camera nor
    /// the tool held out in front of it ever reaches into them
    /// </summary>
    private const double BODY = 0.42;
    /// <summary>how far either side of the middle of the doorway the player may be while passing through it</summary>
    private const double DOOR_PASS = 0.14;
    /// <summary>the most the floor may rise or fall in one step without it being a wall or a drop</summary>
    private const double STEP = 0.55;

    public readonly double cx, cz, ground;
    /// <summary>which way the door faces, and where the stair starts: angles round the tower, as atan2(z, x)</summary>
    public readonly double doorAngle, stairStart;
    public readonly THREE.Group group = new THREE.Group();

    public bool doorOpen { get; private set; } = false;
    private double doorSwing = 0;
    private THREE.Group doorPivot;
    private double doorClosedYaw;

    // where the player is: outside on the rock, inside on the ground floor or the stair, or up on the deck
    private bool inside = false;
    /// <summary>true while the player is out on the deck at the top</summary>
    public bool atTop { get; private set; } = false;
    /// <summary>how high the player's feet are above the tower's floor</summary>
    private double feet = 0;

    /// <summary>false while the lamp is still caked in sand and salt; true once the player has climbed up and brushed it clean</summary>
    public bool cleaned { get; private set; } = false;
    /// <summary>the sand crust canvas used when brushing the lighthouse lamp on the workbench</summary>
    public HTMLCanvasElement crustCanvas;

    private readonly THREE.MeshStandardMaterial cleanPaint = new THREE.MeshStandardMaterial { color = 0xffffff, vertexColors = true, roughness = 0.85, flatShading = false };
    private readonly THREE.MeshStandardMaterial cleanGlass = new THREE.MeshStandardMaterial { color = 0xcfe6ee, roughness = 0.05, metalness = 0.2, transparent = true, opacity = 0.28, side = THREE.Side.DoubleSide, depthWrite = false };
    private readonly THREE.MeshStandardMaterial sandCrustMat = new THREE.MeshStandardMaterial { color = 0x9c825b, vertexColors = true, roughness = 0.97, flatShading = true };

    private readonly THREE.Group dirtGroup = new THREE.Group();
    private THREE.MeshStandardMaterial brassMat;
    private THREE.MeshStandardMaterial lensMat;
    private readonly List<THREE.MeshBasicMaterial> beamMats = new List<THREE.MeshBasicMaterial>();
    public THREE.Group beams { get; private set; }
    private double beamPhase = 0;
    private double lastNight = 0;

    // ---------------------------------------------------------------- horizon ships & intro

    public class HorizonShip
    {
        public THREE.Group root, vessel;
        public List<THREE.MeshStandardMaterial> solidMats = new List<THREE.MeshStandardMaterial>();
        public THREE.MeshStandardMaterial hullMat, cabinMat;
        public THREE.MeshBasicMaterial glowMat, navRedMat, navGreenMat, patchMat, wakeMat;
        public List<THREE.Mesh> lamps = new List<THREE.Mesh>();
        public double startX, startZ, endX, endZ, duration, pause, offset, baseScale;
        public double x, z, heading, lit, fade;
    }

    public readonly THREE.Group shipsGroup = new THREE.Group();
    public readonly List<HorizonShip> ships = new List<HorizonShip>();
    private double shipTime = 0;

    private static readonly double[] INTRO_CUTS = { 4.2, 8.4 };
    public const double INTRO_LENGTH = 8.4;
    private static readonly (double from, double to, string text)[] INTRO_CAPTIONS =
    {
        (0.3, 3.9, "Day gives way to night, and the cleaned lamp shines out across the dark water."),
        (4.4, 8.1, "Far out on the horizon, distant ships appear and pass through the night as the beam sweeps the sea."),
    };

    public bool introActive { get; private set; } = false;
    public double introTime { get; private set; } = 0;
    public bool introDone => introTime >= INTRO_LENGTH;
    public double introDayTime { get; private set; } = 0.78;
    public string introCaption { get; private set; } = null;
    private int introShot = -1;
    private bool introCutting = false;
    private double introStartDay = 0.28;

    public Lighthouse(double x, double z, double groundY, double door)
    {
        cx = x;
        cz = z;
        ground = groundY;
        doorAngle = door;
        stairStart = door + 0.6;
        group.position.set(cx, ground, cz);
        buildTower();
        buildStair();
        buildTop();
        buildDoor();
        buildDirt();
    }

    // ---------------------------------------------------------------- shape

    private static double outer(double y) => R0 + (R1 - R0) * Math.Max(0, Math.Min(H, y)) / H;
    private static double inner(double y) => outer(y) - WALL;
    private static double turn(double a, double b) => Math.Atan2(Math.Sin(a - b), Math.Cos(a - b));

    /// <summary>How far round one turn of the stair an angle is, 0..1, from where the stair starts.</summary>
    private double frac(double a)
    {
        double f = (a - stairStart) / (Math.PI * 2);
        return f - Math.Floor(f);
    }

    /// <summary>
    /// A solid ring segment: from radius rIn to rOut, from angle a0 to a1 (as atan2(z, x)), from y0 to y1, with every face
    /// closed so it can be seen from any side. The radii can differ at the top and the bottom, for a tapering wall.
    /// </summary>
    private static THREE.BufferGeometry sector(double rIn0, double rOut0, double rIn1, double rOut1, double a0, double a1, double y0, double y1, int segs, int color)
    {
        var pos = new List<double>();
        var idx = new List<int>();
        THREE.Vector3 P(double r, double a, double y) => new THREE.Vector3(Math.Cos(a) * r, y, Math.Sin(a) * r);
        void tri(THREE.Vector3 p, THREE.Vector3 q, THREE.Vector3 s, THREE.Vector3 outward)
        {
            var n = q.clone().sub(p).cross(s.clone().sub(p));
            if (n.dot(outward) < 0) { var t = q; q = s; s = t; }
            int b = pos.Count / 3;
            foreach (var v in new[] { p, q, s }) { pos.Add(v.x); pos.Add(v.y); pos.Add(v.z); }
            idx.Add(b); idx.Add(b + 1); idx.Add(b + 2);
        }
        void quad(THREE.Vector3 a, THREE.Vector3 b, THREE.Vector3 c, THREE.Vector3 d, THREE.Vector3 outward)
        {
            tri(a, b, c, outward);
            tri(a, c, d, outward);
        }
        for (int i = 0; i < segs; i++)
        {
            double u0 = a0 + (a1 - a0) * i / segs, u1 = a0 + (a1 - a0) * (i + 1) / segs, um = (u0 + u1) / 2;
            var radial = new THREE.Vector3(Math.Cos(um), 0, Math.Sin(um));
            quad(P(rOut0, u0, y0), P(rOut0, u1, y0), P(rOut1, u1, y1), P(rOut1, u0, y1), radial);
            if (rIn0 > 0.001 || rIn1 > 0.001) quad(P(rIn0, u0, y0), P(rIn0, u1, y0), P(rIn1, u1, y1), P(rIn1, u0, y1), radial.clone().multiplyScalar(-1));
            quad(P(rIn1, u0, y1), P(rIn1, u1, y1), P(rOut1, u1, y1), P(rOut1, u0, y1), new THREE.Vector3(0, 1, 0));
            quad(P(rIn0, u0, y0), P(rIn0, u1, y0), P(rOut0, u1, y0), P(rOut0, u0, y0), new THREE.Vector3(0, -1, 0));
        }
        if (Math.Abs(a1 - a0) < Math.PI * 2 - 1e-6)
        {
            quad(P(rIn0, a0, y0), P(rOut0, a0, y0), P(rOut1, a0, y1), P(rIn1, a0, y1), new THREE.Vector3(Math.Sin(a0), 0, -Math.Cos(a0)));
            quad(P(rIn0, a1, y0), P(rOut0, a1, y0), P(rOut1, a1, y1), P(rIn1, a1, y1), new THREE.Vector3(-Math.Sin(a1), 0, Math.Cos(a1)));
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return Ecology.paint(g, color);
    }

    private const int WHITE = 0xf1ece0, RED = 0xb8322a, STONE = 0x8d877c, STONE_DARK = 0x6f6a62, WOOD = 0x6b4a30, IRON = 0x2f3236;
    private const int SAND_A = 0x9c825b, SAND_B = 0x8a714c, SAND_C = 0xab9269;

    private THREE.Mesh add(List<THREE.BufferGeometry> parts, THREE.Material mat, bool shadows = true)
    {
        var g = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        g.computeVertexNormals();
        var m = new THREE.Mesh(g, mat);
        m.castShadow = shadows;
        m.receiveShadow = true;
        group.add(m);
        return m;
    }

    /// <summary>The tower: its walls with the doorway, red bands, a stone footing, small windows, the floor inside and the column.</summary>
    private void buildTower()
    {
        var parts = new List<THREE.BufferGeometry>();
        // the doorway leaves a gap in the wall from the ground to the top of the door
        double gap = DOOR_W / 2 / R0;
        double a0 = doorAngle + gap, a1 = doorAngle + Math.PI * 2 - gap;
        // a stone footing, sunk into the rock
        parts.Add(sector(inner(0), R0 + 0.25, inner(0), R0 + 0.2, a0, a1, -1.5, 0.45, 48, STONE));
        // the wall, white with two red bands, with real openings in it: the doorway and the windows up the stair
        var openings = new List<(double a, double half, double y0, double y1)> { (doorAngle, gap, 0.45, DOOR_H) };
        openings.AddRange(windows);
        var bands = new[] { (0.45, 4.0, WHITE), (4.0, 5.6, RED), (5.6, 8.6, WHITE), (8.6, 10.2, RED), (10.2, H - 0.25, WHITE) };
        ring(parts, openings, 0.45, H - 0.25, y => inner(y), y => outer(y), y =>
        {
            foreach (var (b0, b1, c) in bands) if (y >= b0 && y < b1) return c;
            return WHITE;
        });
        // inside, the walls are plain whitewash: the bands are paint on the outside only
        ring(parts, openings, DOOR_H, H - 0.25, y => inner(y) - 0.015, y => inner(y) - 0.005, _ => 0xe4ddcd);
        // the door frame: two posts and a lintel of dark stone, standing proud of the wall on both sides. Each piece reaches a
        // little past the edge of the opening, so the faces of the wall's own edges are hidden inside it and never flicker
        foreach (var s in new[] { -1, 1 })
        {
            double ia = doorAngle + s * (gap - 0.02), oa = doorAngle + s * (gap + 0.075);
            parts.Add(sector(inner(0) - 0.03, R0 + 0.08, inner(DOOR_H) - 0.03, outer(DOOR_H) + 0.08, Math.Min(ia, oa), Math.Max(ia, oa), 0, DOOR_H + 0.2, 1, STONE_DARK));
        }
        parts.Add(sector(inner(DOOR_H) - 0.04, outer(DOOR_H) + 0.11, inner(DOOR_H) - 0.04, outer(DOOR_H + 0.3) + 0.11, doorAngle - gap - 0.09, doorAngle + gap + 0.09, DOOR_H - 0.04, DOOR_H + 0.32, 4, STONE_DARK));
        // a step up to the door
        parts.Add(sector(R0 - 0.1, R0 + 0.75, R0 - 0.1, R0 + 0.75, doorAngle - gap - 0.08, doorAngle + gap + 0.08, -0.6, 0.08, 4, STONE));
        // each window: a sill outside, and a # of iron bars across the middle of the opening
        foreach (var (a, half, y0, y1) in windows)
        {
            double ym = (y0 + y1) / 2, rm = (inner(ym) + outer(ym)) / 2;
            parts.Add(sector(outer(y0) - 0.05, outer(y0) + 0.13, outer(y0) - 0.05, outer(y0) + 0.13, a - half - 0.05, a + half + 0.05, y0 - 0.1, y0 - 0.012, 3, STONE));
            foreach (var k in new[] { -1.0 / 3, 1.0 / 3 })
            {
                double ba = a + k * 2 * half;
                parts.Add(sector(rm - 0.035, rm + 0.035, rm - 0.035, rm + 0.035, ba - 0.012, ba + 0.012, y0 - 0.01, y1 + 0.01, 1, IRON));
                double by = ym + k * (y1 - y0);
                // the cross bars are a hair thinner, so where they cross the uprights no two faces lie in one plane
                parts.Add(sector(rm - 0.028, rm + 0.028, rm - 0.028, rm + 0.028, a - half - 0.01, a + half + 0.01, by - 0.02, by + 0.02, 3, IRON));
            }
        }
        // the floor inside, and the stone column the stair winds round
        parts.Add(sector(0, inner(0) + 0.05, 0, inner(0) + 0.05, 0, Math.PI * 2, -0.25, 0.02, 32, STONE_DARK));
        parts.Add(sector(0, CORE, 0, CORE, 0, Math.PI * 2, 0, H, 32, 0xa49d90));
        add(parts, cleanPaint);
    }

    /// <summary>
    /// The windows: two to each turn of the stair, cut where the stair is at a height to look out of them, so that the view
    /// turns and drops away as the player climbs. None near the door, and none up where the deck's corbel is.
    /// </summary>
    private List<(double a, double half, double y0, double y1)> windows
    {
        get
        {
            if (windowList != null) return windowList;
            windowList = new List<(double, double, double, double)>();
            for (int k = 0; k < 4; k++)
            {
                foreach (var f in new[] { 0.28, 0.76 })
                {
                    double a = stairStart + f * Math.PI * 2;
                    double y0 = (k + f) * TURN + 1.05, y1 = y0 + 1.15;
                    if (y1 > H - 1.1) continue;
                    if (y0 < DOOR_H + 0.4 && Math.Abs(turn(a, doorAngle)) < 0.8) continue;
                    windowList.Add((a, 0.34 / R0 * 2, y0, y1));
                }
            }
            return windowList;
        }
    }
    private List<(double a, double half, double y0, double y1)> windowList;

    /// <summary>For the smoke test: the middle of the window nearest the player's eye, in the world.</summary>
    public THREE.Vector3 nearestWindow(double x, double z, double eye)
    {
        THREE.Vector3 best = null;
        double bestD = double.PositiveInfinity;
        foreach (var (a, _, y0, y1) in windows)
        {
            var p = new THREE.Vector3(cx + Math.Cos(a) * R0, ground + (y0 + y1) / 2, cz + Math.Sin(a) * R0);
            double d = JS.Hypot(p.x - x, p.y - eye, p.z - z);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>
    /// A ring of wall between two heights, with openings cut through it. The wall is built in horizontal slices, cut at every
    /// band of colour and at the top and bottom of every opening; each slice leaves out the openings it crosses, so their
    /// edges come out as clean sills, heads and jambs.
    /// </summary>
    private static void ring(List<THREE.BufferGeometry> parts, List<(double a, double half, double y0, double y1)> openings, double yLo, double yHi,
        Func<double, double> rIn, Func<double, double> rOut, Func<double, int> colorAt)
    {
        var cuts = new List<double> { yLo, yHi, 4.0, 5.6, 8.6, 10.2 };
        foreach (var o in openings) { cuts.Add(o.y0); cuts.Add(o.y1); }
        cuts.RemoveAll(y => y < yLo || y > yHi);
        cuts.Sort();
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            double y0 = cuts[i], y1 = cuts[i + 1];
            if (y1 - y0 < 1e-4) continue;
            double mid = (y0 + y1) / 2;
            // the openings this slice crosses, as angle ranges from the first one round to a full turn
            var gaps = new List<(double from, double to)>();
            foreach (var o in openings) if (mid > o.y0 && mid < o.y1) gaps.Add((o.a - o.half, o.a + o.half));
            int color = colorAt(mid);
            if (gaps.Count == 0)
            {
                parts.Add(sector(rIn(y0), rOut(y0), rIn(y1), rOut(y1), 0, Math.PI * 2, y0, y1, 48, color));
                continue;
            }
            // walk round from the end of one opening to the start of the next
            double basis = gaps[0].from;
            var norm = new List<(double from, double to)>();
            foreach (var (f, t) in gaps)
            {
                double ff = basis + ((f - basis) % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
                norm.Add((ff, ff + (t - f)));
            }
            norm.Sort((p, q) => p.from.CompareTo(q.from));
            for (int g = 0; g < norm.Count; g++)
            {
                double from = norm[g].to, to = g + 1 < norm.Count ? norm[g + 1].from : norm[0].from + Math.PI * 2;
                if (to - from < 1e-4) continue;
                int segs = Math.Max(2, (int)Math.Ceiling((to - from) / (Math.PI * 2) * 48));
                parts.Add(sector(rIn(y0), rOut(y0), rIn(y1), rOut(y1), from, to, y0, y1, segs, color));
            }
        }
    }

    /// <summary>The stair: wedge-shaped steps from the column to the wall, twenty to a turn, each solid underneath.</summary>
    private void buildStair()
    {
        var parts = new List<THREE.BufferGeometry>();
        const int PER = 20;
        int steps = (int)Math.Round(H / TURN * PER);
        double rise = TURN / PER;
        for (int j = 0; j < steps; j++)
        {
            // each step reaches a hair under the next, so no two of their faces lie in one plane and flicker
            double a0 = stairStart + Math.PI * 2 * j / PER, a1 = a0 + Math.PI * 2 / PER + 0.004;
            double top = (j + 0.5) * rise;
            double r = inner(top) - 0.02;
            // the first turn is built solid down to the floor, so nobody walks in under it
            parts.Add(sector(CORE - 0.02, r, CORE - 0.02, r, a0, a1, top < TURN * 0.8 ? 0 : top - 0.45, top, 3, j % 2 == 0 ? 0x9a948a : 0x928c82));
            // the front edge of each tread, a little darker
            parts.Add(sector(CORE + 0.01, r - 0.01, CORE + 0.01, r - 0.01, a0 - 0.006, a0 + 0.02, top - 0.03, top + 0.005, 1, STONE_DARK));
        }
        add(parts, cleanPaint);
    }

    /// <summary>The top: the deck with its hole for the stair, the railing, the lantern's glass room and its roof.</summary>
    private void buildTop()
    {
        var parts = new List<THREE.BufferGeometry>();
        double holeA = stairStart + Math.PI * 2 * HOLE, holeB = stairStart + Math.PI * 2;
        // the deck: whole where the stair is below headroom, and only its outer ring over the last part of the stair
        parts.Add(sector(CORE - 0.02, DECK, CORE - 0.02, DECK, stairStart, holeA, H - 0.25, H, 24, 0x8f8a80));
        parts.Add(sector(inner(H), DECK, inner(H), DECK, holeA, holeB, H - 0.25, H, 40, 0x8f8a80));
        // a corbelled ring under the deck's edge, where it overhangs the wall
        parts.Add(sector(outer(H - 0.9) - 0.05, outer(H - 0.9) + 0.05, outer(H) - 0.05, DECK, 0, Math.PI * 2, H - 0.9, H - 0.25, 48, WHITE));
        // the railing: posts, a top rail and a middle rail
        for (int i = 0; i < 28; i++)
        {
            double a = i / 28.0 * Math.PI * 2;
            parts.Add(sector(DECK - 0.1, DECK - 0.04, DECK - 0.1, DECK - 0.04, a - 0.012, a + 0.012, H, H + 1.05, 1, IRON));
        }
        foreach (var y in new[] { H + 1.0, H + 0.55 }) parts.Add(sector(DECK - 0.11, DECK - 0.03, DECK - 0.11, DECK - 0.03, 0, Math.PI * 2, y, y + 0.06, 56, IRON));
        // the lantern: a low wall, the glazing bars, a cornice and a red roof with a ball on top
        parts.Add(sector(LANTERN - 0.2, LANTERN, LANTERN - 0.2, LANTERN, 0, Math.PI * 2, H, H + 0.8, 32, WHITE));
        for (int i = 0; i < 8; i++)
        {
            double a = i / 8.0 * Math.PI * 2;
            parts.Add(sector(LANTERN - 0.12, LANTERN - 0.02, LANTERN - 0.12, LANTERN - 0.02, a - 0.035, a + 0.035, H + 0.8, H + 2.9, 1, IRON));
        }
        parts.Add(sector(0, LANTERN + 0.15, 0, LANTERN + 0.15, 0, Math.PI * 2, H + 2.9, H + 3.1, 32, IRON));
        var roof = new THREE.ConeGeometry(LANTERN + 0.25, 1.4, 24);
        roof.translate(0, H + 3.8, 0);
        parts.Add(Ecology.paint(roof, RED));
        var ball = new THREE.SphereGeometry(0.16, 10, 8);
        ball.translate(0, H + 4.6, 0);
        parts.Add(Ecology.paint(ball, IRON));
        var spike = new THREE.CylinderGeometry(0.02, 0.03, 0.8, 5);
        spike.translate(0, H + 5.0, 0);
        parts.Add(Ecology.paint(spike, IRON));
        add(parts, cleanPaint);
        // the glass, between the bars
        var glass = new THREE.Mesh(new THREE.CylinderGeometry(LANTERN - 0.07, LANTERN - 0.07, 2.1, 24, 1, true), cleanGlass);
        glass.position.y = H + 1.85;
        glass.renderOrder = 3;
        group.add(glass);
    }

    /// <summary>The door: planks with iron straps and a ring, hung on the left-hand post. It swings inward when opened.</summary>
    private void buildDoor()
    {
        double rm = R0 - WALL / 2;
        double gap = DOOR_W / 2 / R0;
        double ha = doorAngle - gap;
        doorPivot = new THREE.Group();
        doorPivot.position.set(Math.Cos(ha) * rm, 0, Math.Sin(ha) * rm);
        // its x axis runs along the doorway, from the hinge to the latch
        doorClosedYaw = Math.Atan2(-Math.Cos(doorAngle), -Math.Sin(doorAngle));
        doorPivot.rotation.y = doorClosedYaw;
        var parts = new List<THREE.BufferGeometry>();
        double w = DOOR_W - 0.2;
        for (int i = 0; i < 5; i++)
        {
            var plank = new THREE.BoxGeometry(w / 5 - 0.012, DOOR_H - 0.08, 0.08);
            plank.translate(0.08 + w / 10 + i * w / 5, (DOOR_H - 0.08) / 2 + 0.02, 0);
            parts.Add(Ecology.paint(plank, i % 2 == 0 ? WOOD : 0x5e4129));
        }
        foreach (var y in new[] { 0.4, 1.9 })
        {
            var strap = new THREE.BoxGeometry(w - 0.05, 0.08, 0.1);
            strap.translate(0.08 + w / 2, y, 0);
            parts.Add(Ecology.paint(strap, IRON));
        }
        var ring = new THREE.TorusGeometry(0.08, 0.016, 6, 14);
        ring.translate(0.08 + w - 0.16, 1.1, 0.07);
        parts.Add(Ecology.paint(ring, IRON));
        var ring2 = new THREE.TorusGeometry(0.08, 0.016, 6, 14);
        ring2.translate(0.08 + w - 0.16, 1.1, -0.07);
        parts.Add(Ecology.paint(ring2, IRON));
        var g = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        g.computeVertexNormals();
        var m = new THREE.Mesh(g, cleanPaint);
        m.castShadow = true;
        doorPivot.add(m);
        group.add(doorPivot);
    }

    /// <summary>
    /// Crusty sand and dried salt caked directly onto the lamp inside the lantern room before the player climbs up, takes the
    /// lamp and cleans it. Hidden once <see cref="setClean"/> is called with true.
    /// </summary>
    private void buildDirt()
    {
        var rand = Stones.rng(913);
        var parts = new List<THREE.BufferGeometry>();
        int[] cols = { SAND_A, SAND_B, SAND_C };
        // sand crust around the foot of the brass lamp stand
        parts.Add(sector(0.18, 0.48, 0.16, 0.36, 0, Math.PI * 2, H + 0.78, H + 1.05, 16, SAND_B));
        // crusty shell patches caked over the Fresnel lens sphere (y = H + 1.6, r = 0.5)
        var shell = new THREE.SphereGeometry(0.535, 14, 10);
        shell.scale(1.02, 0.96, 1.02);
        shell.translate(0, H + 1.6, 0);
        parts.Add(Ecology.paint(shell, SAND_A));
        // lumpy sand and salt clods clinging around the lens and brass rings
        for (int i = 0; i < 24; i++)
        {
            double a = rand() * Math.PI * 2;
            double v = (rand() - 0.45) * 1.1;
            double r = 0.46 * Math.Cos(v * 0.7);
            double y = H + 1.55 + v * 0.52;
            double s = 0.11 + rand() * 0.12;
            var clod = new THREE.SphereGeometry(s, 7, 5);
            clod.scale(0.8 + rand() * 0.4, 0.7 + rand() * 0.4, 0.9 + rand() * 0.4);
            clod.rotateY(-a);
            clod.translate(Math.Cos(a) * r, y, Math.Sin(a) * r);
            parts.Add(Ecology.paint(clod, cols[i % 3]));
        }
        var g = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        g.computeVertexNormals();
        var mesh = new THREE.Mesh(g, sandCrustMat);
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        dirtGroup.add(mesh);
        group.add(dirtGroup);
    }

    /// <summary>
    /// Builds a clean standalone model of the lighthouse lamp (brass stand, glowing Fresnel lens and brass dioptric rings) for
    /// the cleaning workbench, where it is wrapped in a sand crust and brushed clean by the player just like a beach find.
    /// </summary>
    public static THREE.Group buildCleanModel()
    {
        var root = new THREE.Group();
        var brass = new THREE.MeshStandardMaterial { color = 0xc79a3a, roughness = 0.32, metalness = 0.72 };
        var darkBrass = new THREE.MeshStandardMaterial { color = 0x8a6824, roughness = 0.45, metalness = 0.65 };
        var glass = new THREE.MeshStandardMaterial { color = 0xfff1c8, emissive = 0xffd27a, emissiveIntensity = 0.55, roughness = 0.12, metalness = 0.1 };

        // flared brass base and pedestal stand
        var baseFoot = new THREE.Mesh(new THREE.CylinderGeometry(0.36, 0.44, 0.12, 20), darkBrass);
        baseFoot.position.y = 0.06;
        root.add(baseFoot);
        var stand = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.32, 0.72, 16), brass);
        stand.position.y = 0.48;
        root.add(stand);
        var collar = new THREE.Mesh(new THREE.TorusGeometry(0.24, 0.035, 8, 24), darkBrass);
        collar.rotation.x = Math.PI / 2;
        collar.position.y = 0.84;
        root.add(collar);

        // the Fresnel lens sphere
        const double ly = 1.38;
        var lens = new THREE.Mesh(new THREE.SphereGeometry(0.52, 24, 18), glass);
        lens.position.y = ly;
        root.add(lens);

        // horizontal brass Fresnel rings
        for (int i = -2; i <= 2; i++)
        {
            var band = new THREE.Mesh(new THREE.TorusGeometry(0.52 * Math.Cos(i * 0.32) + 0.02, 0.028, 8, 28), brass);
            band.rotation.x = Math.PI / 2;
            band.position.y = ly + Math.Sin(i * 0.32) * 0.52;
            root.add(band);
        }
        // vertical brass ribs joining the rings
        for (int i = 0; i < 4; i++)
        {
            var rib = new THREE.Mesh(new THREE.TorusGeometry(0.53, 0.018, 6, 24), darkBrass);
            rib.rotation.y = i * Math.PI / 4;
            rib.position.y = ly;
            root.add(rib);
        }
        // top brass cap and carrying ring
        var cap = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.24, 0.11, 16), brass);
        cap.position.y = ly + 0.54;
        root.add(cap);
        var handle = new THREE.Mesh(new THREE.TorusGeometry(0.085, 0.022, 8, 18), brass);
        handle.position.y = ly + 0.66;
        root.add(handle);

        var box = new THREE.Box3().setFromObject(root);
        var c = box.getCenter(new THREE.Vector3());
        foreach (var ch in root.children) ch.position.sub(c);
        return root;
    }

    /// <summary>
    /// The lamp, its beams and the ships on the horizon. They are added apart from the rest, after the island has set up its
    /// haze: the lamp shines by its own light, so the distance must not grey it.
    /// </summary>
    public void buildLight(THREE.Group into)
    {
        var holder = new THREE.Group();
        holder.position.copy(group.position);
        // a brass stand and the lens: a glowing ball girdled with the rings of the glass that bends the light
        brassMat = new THREE.MeshStandardMaterial { color = 0x6e5a3a, roughness = 0.85, metalness = 0.2, fog = false };
        var stand = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.32, 1.0, 12), brassMat);
        stand.position.y = H + 0.5;
        holder.add(stand);
        lensMat = new THREE.MeshStandardMaterial { color = 0x6e604b, emissive = 0xffd27a, emissiveIntensity = 0, roughness = 0.9, fog = false };
        var lens = new THREE.Mesh(new THREE.SphereGeometry(0.5, 20, 14), lensMat);
        lens.position.y = H + 1.6;
        holder.add(lens);
        for (int i = -2; i <= 2; i++)
        {
            var band = new THREE.Mesh(new THREE.TorusGeometry(0.5 * Math.Cos(i * 0.32) + 0.02, 0.025, 6, 24), brassMat);
            band.rotation.x = Math.PI / 2;
            band.position.y = H + 1.6 + Math.Sin(i * 0.32) * 0.5;
            holder.add(band);
        }
        // two beams, back to back, each fading out along its length and reaching out toward the horizon
        beams = new THREE.Group();
        beams.position.y = H + 1.6;
        beams.visible = false;
        foreach (var flip in new[] { 0.0, Math.PI })
        {
            const double len = 520;
            var cone = new THREE.ConeGeometry(22.0, len, 20, 6, true);
            cone.translate(0, -len / 2, 0);
            cone.rotateZ(Math.PI / 2 - 0.032);
            var cp = cone.attributes.position;
            var col = new float[cp.count * 3];
            for (int i = 0; i < cp.count; i++)
            {
                double k = Math.Max(0, 1 - cp.getX(i) / len);
                col[i * 3] = col[i * 3 + 1] = col[i * 3 + 2] = (float)(k * k);
            }
            cone.setAttribute("color", new THREE.BufferAttribute(col, 3));
            var mat = new THREE.MeshBasicMaterial { color = 0xfff0c4, vertexColors = true, transparent = true, opacity = 0, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false, side = THREE.Side.DoubleSide };
            beamMats.Add(mat);
            var beam = new THREE.Mesh(cone, mat);
            beam.rotation.y = flip;
            beam.renderOrder = 6;
            beam.frustumCulled = false;
            beams.add(beam);
        }
        holder.add(beams);
        into.add(holder);

        buildShips();
        into.add(shipsGroup);
        setClean(cleaned);
    }

    /// <summary>Switches the lighthouse lamp between its initial sand-encrusted state and its cleaned, shining state.</summary>
    public void setClean(bool on)
    {
        cleaned = on;
        dirtGroup.visible = !on;
        if (brassMat != null)
        {
            brassMat.color.setHex(on ? 0xc79a3a : 0x6e5a3a);
            brassMat.roughness = on ? 0.35 : 0.85;
            brassMat.metalness = on ? 0.7 : 0.2;
        }
        if (lensMat != null)
        {
            lensMat.color.setHex(on ? 0xfff1c8 : 0x6e604b);
            lensMat.roughness = on ? 0.1 : 0.9;
            lensMat.emissiveIntensity = on ? 0.5 + lastNight * 2.2 : 0;
        }
        if (beams != null) beams.visible = on && lastNight > 0.001;
        foreach (var m in beamMats) m.opacity = on ? 0.22 * lastNight : 0;
        shipsGroup.visible = on && lastNight > 0.15;
    }

    // ---------------------------------------------------------------- distant horizon ships (night only, unreachable)

    private HorizonShip makeHorizonShip(double startX, double startZ, double endX, double endZ, double duration, double pause, double offset, double baseScale)
    {
        var root = new THREE.Group();
        var vessel = new THREE.Group();
        vessel.scale.setScalar(baseScale);
        vessel.rotation.order = "YXZ";

        var hullMat = new THREE.MeshStandardMaterial { color = 0x1b2634, emissive = 0xc9964c, emissiveIntensity = 0, roughness = 0.82, side = THREE.Side.DoubleSide, fog = false };
        var bootMat = new THREE.MeshStandardMaterial { color = 0x6b2822, roughness = 0.88, side = THREE.Side.DoubleSide, fog = false };
        var deckMat = new THREE.MeshStandardMaterial { color = 0x36414d, roughness = 0.9, side = THREE.Side.DoubleSide, fog = false };
        var cabinMat = new THREE.MeshStandardMaterial { color = 0xdce3ec, emissive = 0xffdf9a, emissiveIntensity = 0, roughness = 0.75, side = THREE.Side.DoubleSide, fog = false };
        var stackMat = new THREE.MeshStandardMaterial { color = 0xb54434, roughness = 0.8, fog = false };
        var darkMat = new THREE.MeshStandardMaterial { color = 0x151b22, roughness = 0.85, fog = false };

        var glowMat = new THREE.MeshBasicMaterial { color = 0xffd88a, fog = false };
        var navRedMat = new THREE.MeshBasicMaterial { color = 0xff4b3e, fog = false };
        var navGreenMat = new THREE.MeshBasicMaterial { color = 0x3ce87a, fog = false };

        var lamps = new List<THREE.Mesh>();
        THREE.Mesh put(THREE.BufferGeometry g, THREE.Material m, double x, double y, double z)
        {
            var mesh = new THREE.Mesh(g, m);
            mesh.position.set(x, y, z);
            vessel.add(mesh);
            return mesh;
        }

        // sleek coaster/steamer hull: main body + flared bow wedge + stern counter
        put(new THREE.BoxGeometry(3.4, 1.8, 12.0), hullMat, 0, 0.9, 1.2);
        put(new THREE.BoxGeometry(3.48, 0.45, 12.1), bootMat, 0, 0.15, 1.2);
        var bow = new THREE.ConeGeometry(2.4, 5.2, 4);
        bow.rotateY(Math.PI / 4);
        bow.rotateX(-Math.PI / 2);
        bow.scale(1.0, 0.55, 1.0);
        put(bow, hullMat, 0, 0.95, -7.2);
        put(new THREE.BoxGeometry(3.1, 0.25, 11.4), deckMat, 0, 1.85, 1.2);

        // raised forecastle at the bow
        put(new THREE.BoxGeometry(3.2, 0.75, 3.4), hullMat, 0, 2.1, -3.6);

        // aft/midships white bridge superstructure (two tiers)
        put(new THREE.BoxGeometry(2.6, 1.35, 4.8), cabinMat, 0, 2.5, 2.8);
        put(new THREE.BoxGeometry(2.2, 1.15, 2.6), cabinMat, 0, 3.7, 2.1);
        put(new THREE.BoxGeometry(2.4, 0.18, 2.9), darkMat, 0, 4.32, 2.1);

        // raked smokestack behind the bridge
        var stack = put(new THREE.CylinderGeometry(0.42, 0.48, 2.2, 10), stackMat, 0, 4.2, 3.9);
        stack.rotation.x = 0.14;
        var stackTop = put(new THREE.CylinderGeometry(0.44, 0.44, 0.42, 10), darkMat, 0, 5.1, 4.02);
        stackTop.rotation.x = 0.14;

        // forward and aft signal masts with warm masthead lanterns
        put(new THREE.CylinderGeometry(0.07, 0.11, 5.2, 6), darkMat, 0, 4.4, -2.8);
        put(new THREE.CylinderGeometry(0.06, 0.09, 3.8, 6), darkMat, 0, 5.6, 2.1);
        lamps.Add(put(new THREE.SphereGeometry(0.32, 8, 6), glowMat, 0, 7.05, -2.8));
        lamps.Add(put(new THREE.SphereGeometry(0.30, 8, 6), glowMat, 0, 7.55, 2.1));
        lamps.Add(put(new THREE.SphereGeometry(0.24, 8, 6), glowMat, 0, 2.35, 7.25));

        // port (red) and starboard (green) bridge wing navigation lights
        put(new THREE.SphereGeometry(0.20, 6, 5), navRedMat, -1.25, 3.85, 1.2);
        put(new THREE.SphereGeometry(0.20, 6, 5), navGreenMat, 1.25, 3.85, 1.2);

        // warm glowing bridge windows and cabin portholes along both sides so the ship reads clearly on the night horizon
        put(new THREE.BoxGeometry(1.7, 0.42, 0.08), glowMat, 0, 3.85, 0.78);
        foreach (var side in new[] { -1, 1 })
        {
            put(new THREE.BoxGeometry(0.08, 0.42, 1.6), glowMat, side * 1.12, 3.85, 2.0);
            for (int k = 0; k < 4; k++)
            {
                put(new THREE.BoxGeometry(0.08, 0.36, 0.58), glowMat, side * 1.32, 2.55, 1.0 + k * 1.1);
            }
            for (int k = 0; k < 3; k++)
            {
                put(new THREE.BoxGeometry(0.08, 0.28, 0.48), glowMat, side * 1.72, 1.25, -2.2 + k * 2.1);
            }
        }

        // faint wake and a warm reflection on the water when the lighthouse beam sweeps across the distant vessel
        var wakeMat = new THREE.MeshBasicMaterial { color = 0xdbeeff, transparent = true, opacity = 0.26, depthWrite = false, fog = false, side = THREE.Side.DoubleSide };
        var wake = new THREE.Mesh(new THREE.PlaneGeometry(4.0, 24), wakeMat);
        wake.rotation.x = -Math.PI / 2;
        wake.position.set(0, 0.06, 9.5);
        wake.renderOrder = 3;
        vessel.add(wake);

        var patchMat = new THREE.MeshBasicMaterial { color = 0xffe8a8, transparent = true, opacity = 0, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false, side = THREE.Side.DoubleSide };
        var patch = new THREE.Mesh(new THREE.CircleGeometry(14, 20), patchMat);
        patch.rotation.x = -Math.PI / 2;
        patch.position.set(0, 0.08, 0);
        patch.renderOrder = 4;
        vessel.add(patch);

        root.add(vessel);
        shipsGroup.add(root);
        return new HorizonShip
        {
            root = root, vessel = vessel,
            solidMats = new List<THREE.MeshStandardMaterial> { hullMat, bootMat, deckMat, cabinMat, stackMat, darkMat },
            hullMat = hullMat, cabinMat = cabinMat,
            glowMat = glowMat, navRedMat = navRedMat, navGreenMat = navGreenMat,
            patchMat = patchMat, wakeMat = wakeMat, lamps = lamps,
            startX = startX, startZ = startZ, endX = endX, endZ = endZ,
            duration = duration, pause = pause, offset = offset, baseScale = baseScale,
        };
    }

    private void buildShips()
    {
        ships.Clear();
        // Distant ships right on the far horizon line (z = 795..855, ~485-545m from the lighthouse and ~400m past the playable boat limit SEA.z = 420).
        // Each ship appears smoothly as it starts moving, travels across the horizon edge, and fades out at the end of its run.
        ships.Add(makeHorizonShip(cx - 260, 795, cx + 280, 815, 52, 8, 7.0, 1.25));
        ships.Add(makeHorizonShip(cx + 310, 845, cx - 240, 825, 58, 9, 25.0, 1.30));
        ships.Add(makeHorizonShip(-90, 820, 380, 835, 60, 10, 16.0, 1.25));
        ships.Add(makeHorizonShip(cx - 110, 855, cx - 440, 740, 54, 11, 35.0, 1.20));
        shipsGroup.visible = false;
    }

    private void updateShips(double t, double dt, double night, double surfaceY)
    {
        // Horizon ships are visible ONLY at night
        double nightVis = smooth(0.15, 0.45, night);
        if (nightVis <= 0.001)
        {
            shipsGroup.visible = false;
            return;
        }
        shipsGroup.visible = true;
        shipTime += dt;

        for (int i = 0; i < ships.Count; i++)
        {
            var s = ships[i];
            double cycle = s.duration + s.pause;
            double tau = ((shipTime + s.offset) % cycle + cycle) % cycle;
            if (tau >= s.duration)
            {
                s.root.visible = false;
                s.fade = 0;
                continue;
            }

            double u = tau / s.duration;
            // Grow from small as the ship comes in from the distance and shrink smaller as it sails far away, then become invisible
            double fade = Math.Min(smooth(0.0, 0.26, u), smooth(1.0, 0.74, u));
            double alpha = fade * nightVis;
            s.fade = alpha;
            if (alpha <= 0.12)
            {
                s.root.visible = false;
                continue;
            }
            s.root.visible = true;

            s.x = s.startX + (s.endX - s.startX) * u;
            s.z = s.startZ + (s.endZ - s.startZ) * u;
            s.heading = Math.Atan2(-(s.endX - s.startX), -(s.endZ - s.startZ));

            double sy = surfaceY + Math.Sin(t * 0.95 + i * 1.7) * 0.06 * alpha;
            s.root.position.set(s.x, sy, s.z);
            s.root.rotation.set(Math.Sin(t * 1.0 + i * 1.4) * 0.02, s.heading, Math.Sin(t * 0.8 + i * 2.1) * 0.035, "YXZ");
            s.vessel.scale.setScalar(s.baseScale * alpha);

            // how strongly either of the lighthouse's two opposite beams is sweeping across this distant ship right now
            double shipAngle = Math.Atan2(s.z - cz, s.x - cx);
            double c = Math.Abs(Math.Cos(shipAngle - beamPhase));
            double beamHit = cleaned ? nightVis * Math.Pow(Math.Max(0, (c - 0.82) / 0.18), 1.4) : 0;
            s.lit = Math.Max(beamHit, s.lit - dt * 0.85);

            s.hullMat.emissiveIntensity = (0.06 + 0.45 * s.lit) * alpha;
            s.cabinMat.emissiveIntensity = (0.14 + 0.75 * s.lit) * alpha;
            s.patchMat.opacity = 0.28 * s.lit * alpha;
            s.wakeMat.opacity = (0.18 + 0.18 * s.lit) * alpha;
            foreach (var l in s.lamps) l.scale.setScalar(0.95 + Math.Sin(t * 9 + i * 2 + l.position.y) * 0.12 + s.lit * 0.3);
        }
    }

    // ---------------------------------------------------------------- short intro after cleaning

    public void beginIntro(double currentDayTime)
    {
        setClean(true);
        introActive = true;
        introTime = 0;
        introShot = -1;
        introCutting = false;
        introCaption = null;
        // start shipTime so the first distant ship is just appearing and moving out on the far horizon during the intro
        shipTime = 0;
        // start from daytime so the sky visibly sweeps through sunset into night during the first shot
        introStartDay = (currentDayTime > 0.62 && currentDayTime <= 0.94) ? 0.44 : currentDayTime > 0.94 ? currentDayTime - 1.0 : currentDayTime;
        introDayTime = currentDayTime;
        enterIntroShot(0);
    }

    public void endIntro()
    {
        introActive = false;
        introCaption = null;
        introDayTime = 0.78;
    }

    private void enterIntroShot(int n)
    {
        introShot = n;
        if (n == 0)
        {
            beamPhase = Math.PI * 0.5 - 1.1;
        }
        else if (n == 1)
        {
            // sweep the beam across the northern horizon where the distant ships are passing
            beamPhase = Math.PI * 0.5 - 0.38;
        }
    }

    public void updateIntro(double dt, THREE.PerspectiveCamera cam, double seaLevel, Action<Action> fade)
    {
        if (!introActive) return;
        if (introShot < 0) enterIntroShot(0);
        introTime += dt;

        if (!introCutting && introShot < INTRO_CUTS.Length - 1 && introTime >= INTRO_CUTS[introShot] - 0.32)
        {
            introCutting = true;
            int next = introShot + 1;
            fade(() =>
            {
                introCutting = false;
                enterIntroShot(next);
            });
        }

        introCaption = null;
        for (int i = 0; i < INTRO_CAPTIONS.Length; i++)
        {
            if (introTime >= INTRO_CAPTIONS[i].from && introTime < INTRO_CAPTIONS[i].to)
            {
                introCaption = INTRO_CAPTIONS[i].text;
                break;
            }
        }

        // over the first 2.6 seconds of shot 0, day changes smoothly to night (0.78) so the lighthouse shines
        if (introTime < 2.6)
        {
            double k = smooth(0, 2.6, introTime);
            introDayTime = ((introStartDay + (0.78 - introStartDay) * k) % 1 + 1) % 1;
        }
        else
        {
            introDayTime = 0.78;
        }

        var look = new THREE.Vector3();
        if (introShot == 0)
        {
            // outside on the ember rock, watching day turn to night and the cleaned lighthouse lamp wake up
            double s = Math.Min(1, introTime / INTRO_CUTS[0]);
            double a = -0.95 + s * 0.5;
            cam.position.set(cx + Math.Cos(a) * 27, ground + 8.0 + s * 3.0, cz + Math.Sin(a) * 27);
            look.set(cx, ground + 11.5, cz);
        }
        else
        {
            // high beside the glowing lantern, looking out past its turning beams toward the distant ships on the far horizon
            double s = Math.Min(1, (introTime - INTRO_CUTS[0]) / (INTRO_CUTS[1] - INTRO_CUTS[0]));
            cam.position.set(cx - 7 + s * 4, ground + H + 2.8 + s * 0.4, cz - 14);
            look.set(cx + 12, seaLevel + 5, cz + 490);
        }
        cam.lookAt(look);
    }

    // ---------------------------------------------------------------- the door & cleaning

    /// <summary>True when the player is close enough to the door, on the ground, to open or close it.</summary>
    public bool nearDoor(double x, double z)
    {
        if (atTop || feet > 1) return false;
        double dx = cx + Math.Cos(doorAngle) * (R0 - WALL / 2), dz = cz + Math.Sin(doorAngle) * (R0 - WALL / 2);
        return JS.Hypot(x - dx, z - dz) < 2.6;
    }

    /// <summary>True when the player has climbed up to the top deck beside the lantern and the lamp still needs cleaning.</summary>
    public bool nearClean(double x, double z) => !cleaned && atTop;

    public void toggleDoor() => doorOpen = !doorOpen;

    // ---------------------------------------------------------------- walking

    /// <summary>For the smoke test: where the player is in the tower.</summary>
    public string describe(double x, double z) =>
        $"{(atTop ? "on the deck" : inside ? "inside" : "outside")}, feet {feet:0.00}, r {JS.Hypot(x - cx, z - cz):0.00}, door {(doorOpen ? "open" : "shut")}, {(cleaned ? "clean" : "dirty")}";

    public bool isInside => inside;
    public const double STAIR_R = 2.15, DECK_WALK_R = 2.75;

    /// <summary>Forgets where the player was in the tower (after a jump somewhere else, or a load).</summary>
    public void reset()
    {
        inside = false;
        atTop = false;
        feet = 0;
    }

    /// <summary>
    /// The player has tried to move from (x0, z0) to (x, z). Keeps them out of the walls and the column, lets them through the
    /// door only while it is open, carries them up and down the stair and round the deck at the top. Returns the height of the
    /// floor under them, or null when they are out on the rock and the ground itself is the floor.
    /// </summary>
    public double? walk(double x0, double z0, ref double x, ref double z)
    {
        double dx = x - cx, dz = z - cz, r = JS.Hypot(dx, dz), a = Math.Atan2(dz, dx);
        if (r > DECK + 1.5)
        {
            reset();
            return null;
        }
        if (atTop) return walkTop(x0, z0, ref x, ref z, r, a);
        if (inside) return walkInside(x0, z0, ref x, ref z, r, a);
        // outside, on the rock: the wall is solid except at an open door
        double lateral = r * Math.Sin(turn(a, doorAngle));
        bool facing = Math.Cos(turn(a, doorAngle)) > 0;
        if (r < R0 + BODY)
        {
            if (doorOpen && facing && Math.Abs(lateral) < DOOR_W / 2 - 0.22)
            {
                // in the doorway: kept to its middle, clear of the posts
                keepToDoorway(ref x, ref z);
                pushFromDoor(ref x, ref z);
                if (JS.Hypot(x - cx, z - cz) < inner(0) - 0.05)
                {
                    inside = true;
                    feet = 0;
                    return ground;
                }
            }
            else
            {
                double k = (R0 + BODY) / Math.Max(1e-6, r);
                x = cx + dx * k;
                z = cz + dz * k;
            }
        }
        pushFromDoor(ref x, ref z);
        return null;
    }

    /// <summary>The height of the stair under a point, on the turn nearest to the given height; the ground floor is 0 and it stops at the top.</summary>
    private double stairAt(double a, double near)
    {
        double f = frac(a);
        double k = Math.Round((near - f * TURN) / TURN);
        return Math.Max(0, Math.Min(H, f * TURN + k * TURN));
    }

    /// <summary>Moves a point in the doorway in to the middle of it, so the eye never comes close to the door posts.</summary>
    private void keepToDoorway(ref double x, ref double z)
    {
        double dx = x - cx, dz = z - cz;
        double along = dx * Math.Cos(doorAngle) + dz * Math.Sin(doorAngle);
        double lat = -dx * Math.Sin(doorAngle) + dz * Math.Cos(doorAngle);
        lat = Math.Max(-DOOR_PASS, Math.Min(DOOR_PASS, lat));
        x = cx + along * Math.Cos(doorAngle) - lat * Math.Sin(doorAngle);
        z = cz + along * Math.Sin(doorAngle) + lat * Math.Cos(doorAngle);
    }

    /// <summary>Keeps the player out of the door leaf, wherever it has swung to.</summary>
    private void pushFromDoor(ref double x, ref double z)
    {
        if (doorPivot == null) return;
        double th = doorPivot.rotation.y, len = DOOR_W - 0.12;
        double hx = cx + doorPivot.position.x, hz = cz + doorPivot.position.z;
        double ux = Math.Cos(th), uz = -Math.Sin(th);
        double t = Math.Max(0, Math.Min(len, (x - hx) * ux + (z - hz) * uz));
        double px = hx + ux * t, pz = hz + uz * t;
        double d = JS.Hypot(x - px, z - pz);
        if (d >= BODY || d < 1e-6) return;
        x = px + (x - px) / d * BODY;
        z = pz + (z - pz) / d * BODY;
    }

    private double? walkInside(double x0, double z0, ref double x, ref double z, double r, double a)
    {
        double floor = stairAt(a, feet);
        // and a body's width further on, the way they are going: a step that rises too far there is a wall to walk up against,
        // and they stop short of it rather than with their face in it
        double mx = x - x0, mz = z - z0, ml = JS.Hypot(mx, mz);
        if (ml > 1e-6)
        {
            double qx = x + mx / ml * BODY - cx, qz = z + mz / ml * BODY - cz;
            if (stairAt(Math.Atan2(qz, qx), feet) - feet > STEP)
            {
                x = x0;
                z = z0;
                return ground + feet;
            }
        }
        if (Math.Abs(floor - feet) > STEP)
        {
            // walking into the side of the stair, or off the edge of it: stay where they were
            x = x0;
            z = z0;
            return ground + feet;
        }
        feet = floor;
        double dx = x - cx, dz = z - cz;
        double lateral = r * Math.Sin(turn(a, doorAngle));
        bool atDoor = feet < 0.3 && doorOpen && Math.Cos(turn(a, doorAngle)) > 0 && Math.Abs(lateral) < DOOR_W / 2 - 0.22;
        // the wall leans in as it rises, so it is measured at the eye
        double rMax = inner(feet + 1.65) - 0.015 - BODY;
        if (atDoor && r > rMax)
        {
            keepToDoorway(ref x, ref z);
            dx = x - cx;
            dz = z - cz;
            r = JS.Hypot(dx, dz);
        }
        if (r > rMax && !atDoor)
        {
            double k = rMax / r;
            x = cx + dx * k;
            z = cz + dz * k;
        }
        else if (atDoor && r > R0 + 0.05)
        {
            inside = false;
            feet = 0;
            return null;
        }
        if (r < CORE + BODY)
        {
            double k = (CORE + BODY) / Math.Max(1e-6, r);
            x = cx + dx * k;
            z = cz + dz * k;
        }
        if (feet < 0.3) pushFromDoor(ref x, ref z);
        if (feet >= H - 0.02) atTop = true;
        return ground + feet;
    }

    private double? walkTop(double x0, double z0, ref double x, ref double z, double r, double a)
    {
        double f = frac(a);
        // over the stairwell: near the top of the stair they step down onto it; anywhere else the hole is a drop
        if (r < inner(H) - 0.05 && f > HOLE)
        {
            double down = H - TURN + f * TURN;
            if (H - down <= STEP)
            {
                atTop = false;
                inside = true;
                feet = down;
                return ground + feet;
            }
            x = x0;
            z = z0;
            return ground + H;
        }
        double dx = x - cx, dz = z - cz;
        // round the lantern and inside the railing
        if (r < LANTERN + BODY)
        {
            double k = (LANTERN + BODY) / Math.Max(1e-6, r);
            x = cx + dx * k;
            z = cz + dz * k;
        }
        else if (r > DECK - 0.15 - BODY)
        {
            double k = (DECK - 0.15 - BODY) / r;
            x = cx + dx * k;
            z = cz + dz * k;
        }
        feet = H;
        return ground + H;
    }

    // ---------------------------------------------------------------- every frame

    /// <summary>`night` is 0 by day and 1 in the dark: once cleaned, the lamp is lit and turning at night and ships sail the horizon.</summary>
    public void update(double t, double dt, double night, double surfaceY = WATER_LEVEL)
    {
        lastNight = night;
        // the door swings to where it should be
        double want = doorOpen ? 1 : 0;
        doorSwing += (want - doorSwing) * Math.Min(1, dt * 4);
        doorPivot.rotation.y = doorClosedYaw - doorSwing * 1.75;
        if (lensMat == null) return;
        if (!cleaned)
        {
            lensMat.emissiveIntensity = 0;
            beams.visible = false;
            foreach (var m in beamMats) m.opacity = 0;
            shipsGroup.visible = false;
            return;
        }
        beamPhase += dt * 0.45;
        lensMat.emissiveIntensity = 0.5 + night * 2.2;
        beams.visible = night > 0.001;
        beams.rotation.y = -beamPhase;
        foreach (var m in beamMats) m.opacity = 0.22 * night;
        updateShips(t, dt, night, surfaceY);
    }
}
