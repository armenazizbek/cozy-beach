using System;
using System.Collections.Generic;
using static Beach;

/// <summary>
/// The opening, played once before a new game: the Marigold in a storm at night with the village's gold in her hold, the reef
/// that took her, the same sea calm at dawn long afterwards, and the camp on the beach where the player takes over. It is all
/// played in the world itself; this class holds the ship, the camera's path, the captions and the weather for each moment.
/// </summary>
public class Opening
{
    /// <summary>where she strikes: well out, where the water is deep and dark enough to hide what is under her waterline</summary>
    private static readonly (double x, double z) REEF = (34, 240);
    /// <summary>when each shot ends, in seconds from the start</summary>
    private static readonly double[] CUTS = { 13.8, 22.4, 33.4, 43.4 };
    public const double LENGTH = 43.4;

    private static readonly (double from, double to, string text)[] CAPTIONS =
    {
        // each is on screen for as long as it takes to say (see Resources/Voice/opening-N.wav, in this order)
        (0.8, 7.5, "Forty bars of gold, melted down from every spoon and locket a village could spare."),
        (8.1, 13.2, "They trusted Captain Holt to carry it over the sea, aboard the Marigold."),
        (14.6, 17.9, "On the sixth night, the storm found her."),
        (18.5, 21.1, "She went onto the reef in the dark."),
        (23.3, 27.1, "The crew reached land. The gold did not."),
        (27.7, 32.9, "The sea kept it, and kept quiet about it, for a very long time."),
        (34.2, 37.7, "Now the tide has gone out on a quiet beach."),
        (38.3, 42.8, "You have a metal detector, a trowel, and nothing to rush."),
    };

    public readonly THREE.Group group = new THREE.Group();
    private readonly THREE.Group ship = new THREE.Group();
    private readonly THREE.Group sails = new THREE.Group();
    private readonly List<THREE.Mesh> lamps = new List<THREE.Mesh>();

    public double time { get; private set; }
    public bool done => time >= LENGTH;
    /// <summary>the time of day the sky should show now, and whether it is raining</summary>
    public double dayTime { get; private set; } = 0.8;
    public bool raining { get; private set; } = true;
    /// <summary>0..1, the light of a lightning flash dying away</summary>
    public double flash { get; private set; }
    /// <summary>the words on screen now, or null between captions</summary>
    public string caption { get; private set; }
    private int shot = -1;
    /// <summary>the narrator: each caption is also spoken. `volume` is the game's master volume</summary>
    private UnityEngine.AudioSource voice;
    private int spoken = -1;
    public double volume = 1;
    private bool cutting = false;
    private double nextFlash = 1.6;
    /// <summary>the bolt of the last strike, and where the next one should come down once the camera is in place</summary>
    private THREE.Mesh bolt;
    private readonly THREE.MeshBasicMaterial boltMat = new THREE.MeshBasicMaterial { color = 0xf4f8ff, transparent = true, opacity = 0, fog = false, side = THREE.Side.DoubleSide, depthWrite = false };
    private bool strike = false;
    /// <summary>seconds since the last strike, for the flicker and the shake that follow it</summary>
    private double sinceStrike = 10;
    /// <summary>0..1: how hard it is blowing</summary>
    public double gale => shot <= 1 ? 1 : 0;

    /// <summary>For the smoke test: in the first shot, looks down on the deck from above her quarter instead.</summary>
    internal int smokeView = 0;

    /// <summary>For the smoke test: lightning now.</summary>
    internal void smokeStrike() => nextFlash = 0;

    /// <summary>
    /// A bolt of lightning: a jagged line from the clouds to the sea some way off in front of the camera, with a fork
    /// leaving it part of the way down. Two crossed ribbons, so it has width from any side.
    /// </summary>
    private void buildBolt(THREE.Vector3 from, THREE.Vector3 look)
    {
        if (bolt != null) group.remove(bolt);
        double aim = Math.Atan2(look.x - from.x, look.z - from.z) + (JS.Random() - 0.5) * 1.5;
        double dist = 55 + JS.Random() * 60;
        double bx = from.x + Math.Sin(aim) * dist, bz = from.z + Math.Cos(aim) * dist;
        var pos = new List<double>();
        var idx = new List<int>();
        void ribbon(double x0, double y0, double z0, double x1, double z1, double y1, int steps, double width, double wander)
        {
            double px = x0, pz = z0;
            foreach (var (ax, az) in new[] { (1.0, 0.0), (0.0, 1.0) })
            {
                int start = pos.Count / 3;
                double sx = x0, sz = z0;
                var seed = Stones.rng((long)(Math.Abs(x0 * 31 + z0 * 17 + y0) * 100) + 7);
                for (int i = 0; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double x = x0 + (x1 - x0) * t + (i == 0 || i == steps ? 0 : (seed() - 0.5) * wander);
                    double z = z0 + (z1 - z0) * t + (i == 0 || i == steps ? 0 : (seed() - 0.5) * wander);
                    double y = y0 + (y1 - y0) * t;
                    double w = width * (1 - t * 0.6);
                    pos.AddRange(new[] { x - ax * w, y, z - az * w, x + ax * w, y, z + az * w });
                    if (i > 0)
                    {
                        int a = start + (i - 1) * 2;
                        idx.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
                    }
                    sx = x; sz = z;
                }
                px = sx; pz = sz;
            }
        }
        double top = 58 + JS.Random() * 14;
        ribbon(bx + (JS.Random() - 0.5) * 16, top, bz + (JS.Random() - 0.5) * 16, bx, bz, WATER_LEVEL, 13, 0.55, 7);
        // a fork that leaves the main stroke and dies out before it reaches the water
        double fy = top * (0.45 + JS.Random() * 0.2);
        ribbon(bx + (JS.Random() - 0.5) * 6, fy, bz + (JS.Random() - 0.5) * 6, bx + (JS.Random() - 0.5) * 34, bz + (JS.Random() - 0.5) * 34, fy * 0.25, 7, 0.28, 5);
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        bolt = new THREE.Mesh(g, boltMat);
        bolt.frustumCulled = false;
        bolt.renderOrder = 5;
        group.add(bolt);
    }

    public Opening()
    {
        buildShip();
        buildReef();
        group.add(ship);
    }

    // ---------------------------------------------------------------- the Marigold

    private const double SHIP_L = 15, SHIP_W = 2.5, SHIP_D = 1.5, SHIP_H = 1.5;
    /// <summary>how far under the waterline the hull is drawn before it is closed off</summary>
    private const double CUT = 0.85;

    // the hull's lines, u running from 0 at the stem to 1 at the stern: half her beam, how deep her keel runs under the
    // waterline, and the height of her rail above it (it sweeps up to the bow, and steps up to the quarterdeck aft)
    private static double beam(double u) => SHIP_W * (u < 0.5 ? Math.Pow(Math.Sin(Math.PI / 2 * u / 0.5), 0.75) : 1 - 0.3 * Math.Pow((u - 0.5) / 0.5, 2));
    private static double keel(double u) => -SHIP_D * (u < 0.18 ? 1 - Math.Pow(1 - u / 0.18, 2) : u > 0.8 ? 1 - 0.45 * Math.Pow((u - 0.8) / 0.2, 2) : 1);
    private static double sheer(double u) => SHIP_H + 0.8 * Math.Pow(Math.Max(0, 1 - u / 0.3), 2) + 1.0 * smooth(0.66, 0.71, u);
    private static double along(double u) => -SHIP_L / 2 + u * SHIP_L;

    /// <summary>
    /// The skin of the hull between two heights on her side (0 at the rail, 1 at the keel), pushed out from the hull by `out`:
    /// the whole hull is one of these, and the painted band under the rail is another lying just outside it.
    /// </summary>
    private static THREE.BufferGeometry skin(double t0, double t1, int rows, double push, bool closeStern)
    {
        const int N = 30;
        var pos = new List<double>();
        var idx = new List<int>();
        int per = rows + 1;
        foreach (var side in new[] { -1, 1 })
        {
            int start = pos.Count / 3;
            for (int i = 0; i <= N; i++)
            {
                double u = (double)i / N;
                double w = beam(u), top = sheer(u), bottom = keel(u);
                for (int j = 0; j <= rows; j++)
                {
                    double t = t0 + (t1 - t0) * j / rows;
                    // nearly upright sides that turn in to the keel at the bottom
                    // the sea here is clear enough to see through, so the hull stops a little under the waterline (drawn
                    // to her keel she would seem to hang above the water), and is closed off flat there
                    double y = top + (bottom - top) * Math.Pow(t, 1.15);
                    double tw = y < -CUT ? Math.Pow((-CUT - top) / (bottom - top), 1 / 1.15) : t;
                    pos.Add(side * (w * (1 - tw * tw * tw) + (w > 0.01 ? push : 0)));
                    pos.Add(Math.Max(-CUT, y));
                    pos.Add(along(u));
                }
            }
            for (int i = 0; i < N; i++)
            for (int j = 0; j < rows; j++)
            {
                int a = start + i * per + j, b = a + per;
                idx.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        }
        if (closeStern)
        {
            // the bottom, joining the lowest row of one side to the lowest row of the other
            int other = (N + 1) * per;
            for (int i = 0; i < N; i++)
            {
                int a = i * per + rows, b = a + per;
                idx.AddRange(new[] { a, other + a, b, b, other + a, other + b });
            }
            // the flat stern, between the last rib of one side and the last rib of the other
            int left = N * per, right = (N + 1) * per + N * per;
            for (int j = 0; j < rows; j++) idx.AddRange(new[] { left + j, right + j, left + j + 1, left + j + 1, right + j, right + j + 1 });
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>The deck: planking from rail to rail, following the sweep of the rail a little below it.</summary>
    private static THREE.BufferGeometry deckGeometry()
    {
        const int N = 30;
        var pos = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i <= N; i++)
        {
            double u = (double)i / N;
            foreach (var side in new[] { -1, 1 })
            {
                pos.Add(side * beam(u) * 0.97);
                pos.Add(sheer(u) - 0.32);
                pos.Add(along(u));
            }
            if (i < N) idx.AddRange(new[] { i * 2, i * 2 + 2, i * 2 + 1, i * 2 + 1, i * 2 + 2, i * 2 + 3 });
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>The height of the deck at a point along her, 0 at the stem and 1 at the stern.</summary>
    private static double deckY(double u) => sheer(u) - 0.32;

    /// <summary>
    /// What stands on the deck: plank seams, the main hatch with a grating, the gold's crates lashed down beside it, barrels,
    /// a capstan, coiled rope, a cabin front where the quarterdeck steps up, and the wheel up on the quarterdeck.
    /// </summary>
    private void buildDeck(THREE.Material wood, THREE.Material dark, THREE.Material trim, THREE.Material deckMat)
    {
        var seam = new THREE.MeshStandardMaterial { color = 0x6e5236, roughness = 1 };
        var crateMat = new THREE.MeshStandardMaterial { color = 0x9a7650, roughness = 0.95 };
        var rope = new THREE.MeshStandardMaterial { color = 0xb59a6a, roughness = 1 };
        var iron = new THREE.MeshStandardMaterial { color = 0x34363a, roughness = 0.6, metalness = 0.6 };
        THREE.Mesh at(THREE.BufferGeometry g, THREE.Material m, double x, double u, double up, double ry = 0)
        {
            var mesh = new THREE.Mesh(g, m);
            mesh.position.set(x, deckY(u) + up, along(u));
            mesh.rotation.y = ry;
            mesh.castShadow = true;
            ship.add(mesh);
            return mesh;
        }
        // seams between the planks along the waist
        for (int k = -4; k <= 4; k++)
        {
            double x = k * 0.48;
            double u0 = 0.16, u1 = 0.655;
            var line = new THREE.BoxGeometry(0.025, 0.01, along(u1) - along(u0));
            var m = at(line, seam, x, (u0 + u1) / 2, 0.006);
            m.castShadow = false;
        }
        // the main hatch: a raised coaming round a grating
        double hu = 0.47;
        at(new THREE.BoxGeometry(1.9, 0.28, 2.3), dark, 0, hu, 0.14);
        at(new THREE.BoxGeometry(1.7, 0.04, 2.1), wood, 0, hu, 0.29);
        for (int i = 0; i < 6; i++) at(new THREE.BoxGeometry(1.7, 0.05, 0.08), dark, 0, hu - 0.065 + i * 0.026, 0.32);
        // the gold's crates, lashed down forward of the hatch, with iron straps
        foreach (var (x, u, ry, up) in new[] { (-0.75, 0.37, 0.05, 0.0), (0.7, 0.36, -0.08, 0.0), (0.0, 0.355, 0.02, 0.0), (-0.35, 0.36, 0.2, 0.6) })
        {
            var c = at(new THREE.BoxGeometry(0.85, 0.6, 0.7), crateMat, x, u, 0.3 + up, ry);
            foreach (var sx in new[] { -0.25, 0.25 })
            {
                var band = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.62, 0.72), iron);
                band.position.x = sx;
                c.add(band);
            }
        }
        // rope over the crates, down to the deck either side
        foreach (var x in new[] { -1.05, 1.05 }) at(new THREE.BoxGeometry(0.04, 0.04, 1.4), rope, x * 0.95, 0.36, 0.62);
        // barrels by the rail, and one lying on its side
        foreach (var (x, u) in new[] { (1.6, 0.56), (1.55, 0.6), (-1.6, 0.58) })
        {
            at(new THREE.CylinderGeometry(0.28, 0.25, 0.75, 12), crateMat, x, u, 0.375);
            foreach (var y in new[] { 0.12, 0.62 })
            {
                var hoop = at(new THREE.TorusGeometry(0.275, 0.018, 5, 14), iron, x, u, y);
                hoop.rotation.x = Math.PI / 2;
            }
        }
        var lying = at(new THREE.CylinderGeometry(0.26, 0.24, 0.7, 12), crateMat, -1.45, 0.62, 0.26);
        lying.rotation.z = Math.PI / 2;
        // the capstan, between the masts
        at(new THREE.CylinderGeometry(0.28, 0.36, 0.65, 10), dark, 0, 0.555, 0.32);
        for (int i = 0; i < 4; i++) at(new THREE.BoxGeometry(1.5, 0.05, 0.07), wood, 0, 0.555, 0.58, i * Math.PI / 4);
        // coils of rope on the deck
        foreach (var (x, u) in new[] { (-1.4, 0.24), (1.35, 0.27), (0.9, 0.64) })
        {
            var coil = at(new THREE.TorusGeometry(0.22, 0.06, 6, 16), rope, x, u, 0.06);
            coil.rotation.x = Math.PI / 2;
        }
        // where the quarterdeck steps up: a cabin front with a door, two small windows and a short ladder
        double fu = 0.655;
        double rise = deckY(0.72) - deckY(fu);
        var front = at(new THREE.BoxGeometry(beam(fu) * 1.92, rise, 0.1), wood, 0, fu, rise / 2);
        at(new THREE.BoxGeometry(0.75, Math.Min(rise - 0.1, 0.95), 0.06), dark, 0.85, fu, Math.Min(rise - 0.1, 0.95) / 2 - 0.05);
        var glow = new THREE.MeshBasicMaterial { color = 0xffc66a, fog = false };
        foreach (var x in new[] { -1.15, -0.35 }) at(new THREE.BoxGeometry(0.32, 0.26, 0.04), glow, x, fu, rise * 0.6).position.z -= 0.06;
        for (int i = 0; i < 3; i++) at(new THREE.BoxGeometry(0.55, 0.05, 0.16), wood, -1.75 + 0.0, fu - 0.008 - i * 0.008, 0.25 + i * 0.3);
        // a rail along the front of the quarterdeck
        at(new THREE.BoxGeometry(beam(0.72) * 1.9, 0.07, 0.07), trim, 0, 0.705, rise + 0.55);
        for (int i = -3; i <= 3; i++) at(new THREE.BoxGeometry(0.05, 0.55, 0.05), dark, i * 0.6, 0.705, rise + 0.27);
        // the wheel, up on the quarterdeck
        double wu = 0.8;
        at(new THREE.BoxGeometry(0.18, 0.75, 0.18), dark, 0, wu, 0.375);
        var wheel = at(new THREE.TorusGeometry(0.42, 0.035, 6, 18), wood, 0, wu, 0.95);
        for (int i = 0; i < 4; i++)
        {
            var spoke = at(new THREE.BoxGeometry(0.03, 1.0, 0.03), wood, 0, wu, 0.95);
            spoke.rotation.z = i * Math.PI / 4;
        }
    }

    /// <summary>
    /// A small two-masted trader, bow toward -z and the waterline at y = 0: a wooden hull with a sharp stem, a flat stern and
    /// a raised quarterdeck, a painted band under the rail, a bowsprit and jib, square sails on two masts, lit windows in the
    /// stern and lanterns at the stern rail and the mastheads.
    /// </summary>
    private void buildShip()
    {
        var wood = new THREE.MeshStandardMaterial { color = 0x5c3f29, roughness = 0.9, side = THREE.Side.DoubleSide };
        var dark = new THREE.MeshStandardMaterial { color = 0x3a2a1e, roughness = 0.9 };
        var trim = new THREE.MeshStandardMaterial { color = 0xc79a3a, roughness = 0.6, side = THREE.Side.DoubleSide };
        var deckMat = new THREE.MeshStandardMaterial { color = 0x8a6a48, roughness = 1, side = THREE.Side.DoubleSide };
        var canvas = new THREE.MeshStandardMaterial { color = 0xe9dfc8, roughness = 1, side = THREE.Side.DoubleSide };
        var glow = new THREE.MeshBasicMaterial { color = 0xffc66a, fog = false };
        THREE.Mesh put(THREE.BufferGeometry g, THREE.Material m, double x, double y, double z, THREE.Object3D into = null)
        {
            var mesh = new THREE.Mesh(g, m);
            mesh.position.set(x, y, z);
            mesh.castShadow = true;
            (into ?? ship).add(mesh);
            return mesh;
        }
        put(skin(0, 1, 9, 0, true), wood, 0, 0, 0);
        put(skin(0.05, 0.17, 2, 0.03, false), trim, 0, 0, 0);
        put(deckGeometry(), deckMat, 0, 0, 0);
        buildDeck(wood, dark, trim, deckMat);
        // the stern: three lit windows, the rudder under them and a lantern on a post above
        double sternZ = SHIP_L / 2, sternY = sheer(1);
        foreach (var sx in new[] { -0.85, 0, 0.85 }) put(new THREE.BoxGeometry(0.5, 0.5, 0.06), glow, sx, sternY - 0.85, sternZ + 0.02);
        put(new THREE.BoxGeometry(SHIP_W * 1.42, 0.1, 0.1), trim, 0, sternY - 0.45, sternZ + 0.03);
        put(new THREE.BoxGeometry(0.12, 0.9, 0.6), dark, 0, 0.15, sternZ + 0.28);
        put(new THREE.CylinderGeometry(0.035, 0.035, 1.0, 6), dark, 0, sternY + 0.5, sternZ - 0.15);
        lamps.Add(put(new THREE.SphereGeometry(0.19, 8, 6), glow, 0, sternY + 1.05, sternZ - 0.15));
        // the bowsprit, reaching forward and up from the stem
        const double rise = 0.33;
        double bowY = sheer(0) - 0.1, bowZ = -SHIP_L / 2;
        var sprit = put(new THREE.CylinderGeometry(0.06, 0.1, 5, 7), dark, 0, bowY + Math.Sin(rise) * 1.3, bowZ - Math.Cos(rise) * 1.3);
        sprit.rotation.x = -(Math.PI / 2 - rise);
        double tipY = bowY + Math.Sin(rise) * 3.8, tipZ = bowZ - Math.Cos(rise) * 3.8;
        // two masts, each with a yard and a square sail that bellies forward
        double foreTop = 0, foreZ = 0, foreFoot = 0;
        foreach (var (u, h, w) in new[] { (0.34, 10.0, 5.6), (0.6, 8.6, 4.8) })
        {
            double mz = along(u), foot = sheer(u) - 0.32;
            put(new THREE.CylinderGeometry(0.09, 0.16, h, 8), dark, 0, foot + h / 2, mz);
            double yardY = foot + h * 0.84;
            put(new THREE.CylinderGeometry(0.055, 0.055, w + 0.5, 6), dark, 0, yardY, mz - 0.18).rotation.z = Math.PI / 2;
            double sh = h * 0.52;
            var sail = new THREE.PlaneGeometry(w, sh, 8, 6);
            var sp = sail.attributes.position;
            for (int i = 0; i < sp.count; i++)
            {
                double ux = sp.getX(i) / (w / 2), uy = sp.getY(i) / (sh / 2);
                sp.setZ(i, -(1 - ux * ux) * (1 - uy * uy * 0.6) * 0.9);
            }
            sail.computeVertexNormals();
            put(sail, canvas, 0, yardY - sh / 2 - 0.1, mz - 0.22, sails);
            lamps.Add(put(new THREE.SphereGeometry(0.13, 8, 6), glow, 0, foot + h + 0.13, mz));
            if (foreTop == 0) { foreTop = foot + h * 0.9; foreZ = mz - 0.2; foreFoot = foot + 1.2; }
        }
        // the jib: a three-cornered sail from the end of the bowsprit to the foremast
        var jib = new THREE.BufferGeometry();
        jib.setAttribute("position", new THREE.Float32BufferAttribute(new List<double> { 0, tipY, tipZ, 0, foreTop, foreZ, 0.25, foreFoot, foreZ - 1.2 }, 3));
        jib.setIndex(new List<int> { 0, 1, 2 });
        jib.computeVertexNormals();
        put(jib, canvas, 0, 0, 0, sails);
        ship.add(sails);
        // a pennant at the main masthead
        put(new THREE.BoxGeometry(0.02, 0.3, 1.2), new THREE.MeshStandardMaterial { color = 0xc23b2f, roughness = 0.9 }, 0, sheer(0.34) + 9.9, along(0.34) + 0.7);
        ship.rotation.order = "YXZ";
    }

    /// <summary>The reef: black rocks standing just clear of the water.</summary>
    private void buildReef()
    {
        var mat = new THREE.MeshStandardMaterial { color = 0x23262a, roughness = 1, flatShading = true };
        var geo = new THREE.DodecahedronGeometry(1, 1);
        foreach (var (dx, dz, s) in new[] { (-3.5, -6.0, 2.6), (1.5, -8.5, 3.4), (5.5, -5.0, 2.2), (-7.0, -9.5, 1.8), (8.5, -9.0, 2.8), (-1.0, -12.0, 2.0) })
        {
            var m = new THREE.Mesh(geo, mat);
            m.scale.set(s, s * 0.7, s * 1.2);
            m.rotation.set(dx, dz, s);
            m.position.set(REEF.x + dx, WATER_LEVEL - s * 0.15, REEF.z + dz);
            group.add(m);
        }
    }

    // ---------------------------------------------------------------- playing it

    /// <summary>Jumps to the end.</summary>
    public void skip() => time = LENGTH;

    /// <summary>Speaks caption number i, if its recording is there.</summary>
    private void say(int i)
    {
        var clip = UnityEngine.Resources.Load<UnityEngine.AudioClip>("Voice/opening-" + (i + 1));
        AutoShot.Trace(clip != null ? $"narrator: line {i + 1}, {clip.length:0.0} s" : $"narrator: no recording for line {i + 1}");
        if (clip == null) return;
        if (voice == null)
        {
            var go = new UnityEngine.GameObject("OpeningVoice");
            voice = go.AddComponent<UnityEngine.AudioSource>();
            voice.spatialBlend = 0;
        }
        voice.Stop();
        voice.clip = clip;
        voice.volume = (float)Math.Min(1, volume);
        voice.Play();
    }

    /// <summary>The opening is over or was skipped: the narrator stops mid-word if need be.</summary>
    public void dispose()
    {
        if (voice == null) return;
        voice.Stop();
        UnityEngine.Object.Destroy(voice.gameObject);
        voice = null;
    }

    /// <summary>
    /// Moves the opening on and puts the camera where this moment wants it. `fade` blacks the screen out and calls back at the
    /// darkest point (used at each cut); `thunder` is called with each flash of lightning.
    /// </summary>
    public void update(double dt, THREE.PerspectiveCamera cam, double seaLevel, Func<double, double, double> swell, Func<double, double, double> groundAt, Action<Action> fade, Action thunder)
    {
        if (shot < 0) enter(0);
        time += dt;
        // each cut goes through black; the next shot is set up while nothing can be seen
        if (!cutting && shot < CUTS.Length - 1 && time >= CUTS[shot] - 0.32)
        {
            cutting = true;
            int next = shot + 1;
            fade(() =>
            {
                cutting = false;
                enter(next);
            });
        }

        caption = null;
        for (int i = 0; i < CAPTIONS.Length; i++)
        {
            if (time < CAPTIONS[i].from || time >= CAPTIONS[i].to) continue;
            caption = CAPTIONS[i].text;
            if (i != spoken)
            {
                spoken = i;
                say(i);
            }
        }

        // lightning, only while the storm lasts: a strike is two or three quick flickers, then the dark again
        sinceStrike += dt;
        if (shot <= 1)
        {
            nextFlash -= dt;
            if (nextFlash <= 0)
            {
                sinceStrike = 0;
                strike = true;
                nextFlash = 1.7 + JS.Random() * 2.6;
                thunder();
            }
        }
        double f = sinceStrike;
        flash = shot > 1 ? 0 : f < 0.07 ? 1 : f < 0.13 ? 0.25 : f < 0.2 ? 0.85 : f < 0.27 ? 0.2 : f < 0.33 ? 0.55 : Math.Max(0, 0.4 - (f - 0.33) * 1.6);
        boltMat.opacity = f < 0.33 ? Math.Min(1, flash * 1.3) : 0;

        var look = new THREE.Vector3();
        if (shot == 0)
        {
            // running before the storm: the ship drives toward the reef, pitching and rolling, and the camera circles her low
            double s = time / CUTS[0];
            double sx = REEF.x + 6, sz = REEF.z + 66 - s * 48;
            // she rides on the sea where she is: rising and falling with it, never pushed down into it, so no water comes
            // aboard. Her pitch and roll are kept small enough that the rail never dips under the surface beside her
            double y = seaLevel + swell(sx, sz);
            ship.position.set(sx, y, sz);
            ship.rotation.set(Math.Sin(time * 1.25) * 0.1, 0.08 + Math.Sin(time * 0.5) * 0.05, Math.Sin(time * 0.9 + 1) * 0.17);
            double a = 2.2 - s * 1.1;
            cam.position.set(sx + Math.Cos(a) * 19, seaLevel + 2.8 + Math.Sin(time * 1.1) * 0.9, sz + Math.Sin(a) * 19 - 4);
            look.set(sx, y + 4, sz - 2);
            if (smokeView == 1) { cam.position.set(sx + 6, y + 8, sz + 10); look.set(sx, y + 1.5, sz - 1); }
            else if (smokeView == 2) { cam.position.set(sx + 9, y + 1.2, sz + 1); look.set(sx, y + 0.6, sz); }
        }
        else if (shot == 1)
        {
            // on the reef: she heels over and settles by the head while the seas break over her
            double s = Math.Min(1, (time - CUTS[0]) / (CUTS[1] - CUTS[0]));
            double e = s * s * (3 - 2 * s);
            double sx = REEF.x, sz = REEF.z - 1 - e * 1.5;
            ship.position.set(sx, seaLevel - 0.4 - e * 2.4 + Math.Sin(time * 2.2) * 0.12 * (1 - e), sz);
            ship.rotation.set(-0.12 - e * 0.38 + Math.Sin(time * 1.7) * 0.03, 0.25, 0.2 + e * 0.75);
            double a = 0.7 + s * 0.5;
            cam.position.set(sx + Math.Cos(a) * 15, seaLevel + 3.2 + s * 1.2, sz + Math.Sin(a) * 15);
            look.set(sx, seaLevel + 2.5 - e * 1.2, sz);
        }
        else if (shot == 2)
        {
            // long afterwards, at dawn: in over the calm water toward the beach
            double s = (time - CUTS[1]) / (CUTS[2] - CUTS[1]);
            double z = 150 - s * 70;
            cam.position.set(18 - s * 10, seaLevel + 5.5 - s * 2.6, z);
            look.set(0, 2, CAMP_Z);
        }
        else
        {
            // the camp: a slow turn round it, as on the title screen
            double s = time - CUTS[2];
            double a = 0.9 + s * 0.06;
            double x = 2 + Math.Cos(a) * 13, z = CAMP_Z + 5 + Math.Sin(a) * 13;
            cam.position.set(x, groundAt(x, z) + 3.0, z);
            look.set(0, groundAt(0, CAMP_Z) + 1.6, CAMP_Z);
        }
        if (shot <= 1)
        {
            // the camera rides the same sea, and a strike close by jolts it
            double jolt = Math.Max(0, 0.5 - sinceStrike) * 0.5;
            look.x += Math.Sin(time * 31) * jolt;
            look.y += Math.Sin(time * 0.9) * 0.5 + Math.Cos(time * 37) * jolt;
        }
        cam.lookAt(look);
        if (shot <= 1) cam.rotation.z += Math.Sin(time * 0.8) * 0.045;
        if (strike)
        {
            strike = false;
            buildBolt(cam.position, look);
        }
        // the lanterns gutter in the wind
        foreach (var l in lamps) l.scale.setScalar(0.85 + Math.Sin(time * 13 + l.position.y) * 0.15);
    }

    private void enter(int n)
    {
        shot = n;
        bool storm = n <= 1;
        dayTime = storm ? 0.8 : n == 2 ? 0.02 : 0.06;
        raining = storm;
        // the ship and the reef belong to the night of the wreck
        group.visible = storm;
        flash = 0;
    }
}
