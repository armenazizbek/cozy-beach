using System;
using System.Collections.Generic;
using static Beach;

/// <summary>
/// The Marigold: a three-masted merchant ship of the old kind, the one that carried the village's gold and went down on the reef
/// (see <see cref="Opening"/>). Hand-built from a planked, tumbled-home hull with a raked stem, a beakhead with a gilded figurehead,
/// a forecastle and a high quarterdeck, gun ports with cannon, a stern gallery with lit windows, three masts with tops, yards, stays,
/// shrouds and ratlines, and patched canvas on every spar. Bow toward -z, waterline at y = 0, about sixteen units long. It is
/// drawn as six welded meshes (wood, gold, iron, cloth, lamp glass), so the whole ship costs very little.
/// </summary>
public static class OldShip
{
    public const double L = 16, W = 2.7, DRAFT = 1.6, CUT = 0.9;
    private const int STATIONS = 44, ROWS = 22;

    private const int PLANK = 0x5c3f29, DARKW = 0x3a2a1e, GOLD = 0xd9ad45, MARIGOLD = 0xc9861c, DECK = 0x8a6a48, SEAM = 0x5e452e;
    private const int CRATE = 0x9a7650, ROPE = 0xb59a6a, TARRED = 0x3a2a1e, IRON = 0x2e3034, CANVAS = 0xe3d8bd, RED = 0xb53a2e, RATLINE = 0x6a5538;

    // ---------------------------------------------------------------- the lines of the hull

    private static double along(double u) => -L / 2 + u * L;

    /// <summary>half her beam at the waterline, u = 0 at the stem and 1 at the stern: bluff at the bow, full amidships, tucked in aft</summary>
    private static double hb(double u) => W * (u < 0.5 ? Math.Pow(Math.Sin(Math.PI / 2 * u / 0.5), 0.85) : 1 - 0.36 * Math.Pow((u - 0.5) / 0.5, 2.2));

    /// <summary>the height of her rail above the waterline: swept up at the bow, stepped up for the forecastle and for the quarterdeck</summary>
    private static double sheer(double u) =>
        1.45 + 0.35 * Math.Pow(Math.Max(0, 1 - u / 0.35), 2) + 0.7 * (1 - smooth(0.17, 0.2, u)) + 1.05 * smooth(0.64, 0.69, u) + 0.18 * smooth(0.9, 1, u);

    private static double keel(double u) => -DRAFT * (u < 0.15 ? 1 - Math.Pow(1 - u / 0.15, 2) : u > 0.8 ? 1 - 0.45 * Math.Pow((u - 0.8) / 0.2, 2) : 1);

    /// <summary>how far forward or aft a point of the hull is pushed from its station: the stem rakes forward, the stern overhangs</summary>
    private static double shift(double u, double t) => -1.1 * (1 - smooth(0, 0.2, u)) * Math.Pow(1 - t, 1.5) + 0.6 * smooth(0.9, 1, u) * Math.Pow(1 - t, 1.2);

    /// <summary>the sea is clear here, so the hull is cut off a little under the waterline and closed flat</summary>
    private static double tClip(double u)
    {
        double top = sheer(u), bottom = keel(u);
        return bottom > -CUT ? 1 : Math.Pow((-CUT - top) / (bottom - top), 1 / 1.1);
    }

    private static double tc(double u, double t) => Math.Min(t, tClip(u));
    private static double height(double u, double t) => Math.Max(-CUT, sheer(u) + (keel(u) - sheer(u)) * Math.Pow(tc(u, t), 1.1));

    private static double half(double u, double t)
    {
        double tt = tc(u, t);
        // the sides fall inward a little toward the rail (tumblehome) and turn in under the waterline toward the keel
        return hb(u) * (0.88 + 0.12 * smooth(0, 0.5, tt)) * (1 - tt * tt * tt);
    }

    /// <summary>where the hull's side is at a height above the waterline, at station u: its half-width and its z</summary>
    private static (double x, double z) surf(double u, double y)
    {
        double lo = 0, hi = 1;
        for (int k = 0; k < 26; k++)
        {
            double mid = (lo + hi) / 2;
            if (height(u, mid) > y) lo = mid; else hi = mid;
        }
        double t = (lo + hi) / 2;
        return (half(u, t), along(u) + shift(u, t));
    }

    // the decks: the forecastle, the waist and the quarterdeck, each at its own height
    private static double foreDeck(double u) => 1.4 + 0.25 * Math.Pow(Math.Max(0, 1 - u / 0.185), 2);
    private const double WAIST = 0.6;
    private static double poopDeck(double u) => 1.65 + 0.18 * smooth(0.9, 1, u);
    private static double deckAt(double u) => u < 0.185 ? foreDeck(u) : u < 0.667 ? WAIST : poopDeck(u);

    private static double hash(int a, int b)
    {
        double s = Math.Sin(a * 12.9898 + b * 78.233) * 43758.5453;
        return s - Math.Floor(s);
    }

    private static int hullColor(double u, double t)
    {
        double y = height(u, t);
        int row = (int)Math.Round(t * ROWS), st = (int)Math.Round(u * STATIONS);
        if (row == 0) return 0x2f2118;
        if (row <= 3) return shadeOf(MARIGOLD, 0.92 + 0.12 * hash(row, st / 5));
        double k = (row % 2 == 0 ? 1.0 : 0.86) * (0.9 + 0.2 * hash(row, st / 4));
        int c = ShipKit.shade(PLANK, k);
        if (y < 0.5) c = ShipKit.mix(c, 0x1f1d1a, smooth(0.5, -0.1, y));
        if (y < -0.1) c = ShipKit.mix(c, 0x2a3a26, smooth(-0.1, -0.9, y) * 0.8);
        return c;
    }

    private static int shadeOf(int c, double k) => ShipKit.shade(c, k);

    // ---------------------------------------------------------------- building her

    /// <summary>Builds the Marigold into `ship`; the lanterns that gutter in the wind are added to `lamps`, the canvas to `sails`.</summary>
    public static void build(THREE.Group ship, THREE.Group sails, List<THREE.Mesh> lamps)
    {
        var wood = new ShipKit();
        var gold = new ShipKit();
        var iron = new ShipKit();
        var cloth = new ShipKit();
        var glow = new ShipKit();

        buildHull(wood, gold);
        buildDecks(wood, gold, iron, glow);
        buildStern(wood, gold, glow);
        buildBow(wood, gold, iron);
        buildMasts(ship, wood, gold, iron, cloth, lamps);
        // the great lantern on its staff over the stern, which gutters in the wind
        lamp(ship, lamps, 0, poopDeck(1) + 1.55, along(1) - 0.35, 0.17);

        THREE.Mesh weld(ShipKit k, THREE.Material m, THREE.Object3D into, bool shadow = true)
        {
            if (k.count == 0) return null;
            var mesh = k.mesh(m, shadow);
            into.add(mesh);
            return mesh;
        }
        weld(wood, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.9, side = THREE.Side.DoubleSide }, ship);
        weld(gold, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.4, metalness = 0.55, side = THREE.Side.DoubleSide }, ship);
        weld(iron, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.55, metalness = 0.6 }, ship);
        weld(cloth, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1, side = THREE.Side.DoubleSide }, sails);
        weld(glow, new THREE.MeshBasicMaterial { color = 0xffc66a, fog = false }, ship, false);
        ship.add(sails);
    }

    private static void buildHull(ShipKit wood, ShipKit gold)
    {
        var hull = ShipKit.loft(L, STATIONS, ROWS, half, height, hullColor, true, shift);
        wood.addPainted(hull);
        // two wales: heavy planks standing out from the side, and a rubbing strake near the water
        foreach (var (t0, t1, col) in new[] { (0.205, 0.265, 0x2a1c12), (0.40, 0.45, 0x2a1c12), (0.60, 0.64, 0x24180f) })
            wood.addPainted(ShipKit.loft(L, STATIONS, 1, half, height, (u, t) => col, false, shift, t0, t1, 0.05));

        // the rail cap: a flat strip along the top edge of each side, a little wider than the planking
        foreach (var side in new[] { -1, 1 })
        {
            var s = side;
            Func<double, double, (double x, double y, double z)> f = (a, b) =>
            {
                double u = a;
                double x = half(u, 0) + 0.07 - b * 0.24;
                return (s * x, sheer(u) + 0.03, along(u) + shift(u, 0));
            };
            wood.addPainted(ShipKit.surface(f, STATIONS, 1, (a, b) => 0x2a1c12));
        }
        // gilded cap on the quarterdeck rail and a gold beading under the marigold frieze
        foreach (var side in new[] { -1, 1 })
        {
            var s = side;
            Func<double, double, (double x, double y, double z)> g = (a, b) =>
            {
                double u = 0.69 + a * 0.31;
                return (s * (half(u, 0.125) + 0.03 + b * 0.01), height(u, 0.125), along(u) + shift(u, 0.125));
            };
            gold.addPainted(ShipKit.surface(g, 12, 1, (a, b) => GOLD));
        }
    }

    private static void buildDecks(ShipKit wood, ShipKit gold, ShipKit iron, ShipKit glow)
    {
        double inset = 0.13;
        wood.addPainted(ShipKit.deck(L, 0, 0.185, 8, u => half(u, 0) - inset, foreDeck, DECK));
        wood.addPainted(ShipKit.deck(L, 0.185, 0.667, 16, u => half(u, 0) - inset, u => WAIST, DECK));
        wood.addPainted(ShipKit.deck(L, 0.667, 1, 10, u => half(u, 0) - inset, poopDeck, DECK));
        // plank seams along the waist, the forecastle and the quarterdeck
        for (int k = -4; k <= 4; k++)
        {
            double x = k * 0.5;
            wood.box(0.025, 0.012, along(0.667) - along(0.185), x, WAIST + 0.007, (along(0.667) + along(0.185)) / 2, SEAM);
            if (Math.Abs(x) < 1.5)
            {
                wood.box(0.025, 0.012, along(1) - along(0.667) - 0.6, x, poopDeck(0.8) + 0.007, (along(1) + along(0.667)) / 2 - 0.1, SEAM);
                wood.box(0.025, 0.012, along(0.185) - along(0.02), x * 0.8, foreDeck(0.1) + 0.007, (along(0.185) + along(0.02)) / 2, SEAM);
            }
        }

        // bulkheads where the decks step: the forecastle's, and the quarterdeck's with its door and lit windows
        double fu = 0.185, fw = half(fu, 0) - inset;
        wood.box(fw * 2, foreDeck(fu) - WAIST, 0.12, 0, (foreDeck(fu) + WAIST) / 2, along(fu), 0x4a3322);
        wood.box(0.6, 0.62, 0.05, 0.5, WAIST + 0.33, along(fu) + 0.07, 0x1c130d);
        wood.box(fw * 2 + 0.1, 0.06, 0.18, 0, foreDeck(fu) + 0.03, along(fu), 0x2a1c12);
        double pu = 0.667, pw = half(pu, 0) - inset, rise = poopDeck(pu) - WAIST;
        wood.box(pw * 2, rise, 0.14, 0, WAIST + rise / 2, along(pu), 0x4d3424);
        wood.box(pw * 2 + 0.1, 0.07, 0.2, 0, WAIST + rise + 0.035, along(pu), 0x2a1c12);
        wood.box(0.72, 0.9, 0.05, 0.85, WAIST + 0.5, along(pu) - 0.08, 0x1c130d);
        foreach (var x in new[] { -1.25, -0.45 })
        {
            wood.box(0.46, 0.4, 0.05, x, WAIST + rise * 0.6, along(pu) - 0.07, 0xc79a3a);
            glow.box(0.34, 0.28, 0.05, x, WAIST + rise * 0.6, along(pu) - 0.1, 0xffffff);
        }
        // a ladder up to the quarterdeck on the port side
        foreach (var x in new[] { -1.95, -1.55 }) wood.rod(x, WAIST, along(pu) - 0.7, x, poopDeck(pu), along(pu) - 0.05, 0.03, DARKW);
        for (int i = 0; i < 4; i++)
        {
            double k = (i + 0.7) / 4.4;
            wood.rod(-1.97, WAIST + rise * k, along(pu) - 0.7 + 0.65 * k, -1.53, WAIST + rise * k, along(pu) - 0.7 + 0.65 * k, 0.025, DARKW);
        }
        // a rail along the front of the quarterdeck
        double rz = along(0.69);
        wood.box(pw * 1.9, 0.06, 0.06, 0, poopDeck(0.69) + 0.58, rz, 0x2a1c12);
        for (int i = -4; i <= 4; i++) wood.rod(i * 0.5, poopDeck(0.69), rz, i * 0.5, poopDeck(0.69) + 0.58, rz, 0.025, DARKW);

        // the main hatch with a coaming and a grating, and the gold's crates lashed down aft of the mainmast
        double hz = along(0.35);
        wood.box(1.9, 0.28, 2.0, 0, WAIST + 0.14, hz, 0x3a2a1e);
        wood.box(1.7, 0.04, 1.8, 0, WAIST + 0.3, hz, 0x6a4a30);
        for (int i = 0; i < 7; i++) wood.box(1.7, 0.045, 0.07, 0, WAIST + 0.33, hz - 0.78 + i * 0.26, 0x2a1c12);
        for (int i = 0; i < 7; i++) wood.box(0.07, 0.045, 1.8, -0.75 + i * 0.25, WAIST + 0.335, hz, 0x2a1c12);
        double cz = along(0.585);
        foreach (var (x, dz, ry, up) in new[] { (-0.75, 0.0, 0.05, 0.0), (0.0, 0.05, -0.04, 0.0), (0.75, 0.0, 0.08, 0.0), (-0.35, 0.0, 0.2, 0.62) })
        {
            wood.box(0.72, 0.6, 0.62, x, WAIST + 0.3 + up, cz + dz, CRATE, 0, ry, 0);
            foreach (var sx in new[] { -0.2, 0.2 }) iron.box(0.06, 0.62, 0.64, x + sx * Math.Cos(ry), WAIST + 0.3 + up, cz + dz - sx * Math.Sin(ry), IRON, 0, ry, 0);
        }
        // a lashing across the crates
        foreach (var dz in new[] { -0.15, 0.15 }) wood.rod(-1.1, WAIST + 0.03, cz + dz, -0.4, WAIST + 0.95, cz + dz, 0.025, ROPE);
        // barrels by the rail and one lying on its side
        foreach (var (x, u) in new[] { (1.75, 0.54), (1.7, 0.585), (-1.75, 0.56) })
        {
            wood.cyl(0.26, 0.23, 0.72, 10, x, WAIST + 0.36, along(u), CRATE);
            foreach (var y in new[] { 0.14, 0.58 }) iron.put(new THREE.TorusGeometry(0.255, 0.02, 4, 12), x, WAIST + y, along(u), IRON, Math.PI / 2);
        }
        wood.cyl(0.25, 0.23, 0.68, 10, -1.6, WAIST + 0.25, along(0.64), CRATE, 0, 0, Math.PI / 2);
        // the capstan, with its bars shipped
        wood.cyl(0.26, 0.34, 0.65, 8, 0, WAIST + 0.33, along(0.45), DARKW);
        for (int i = 0; i < 4; i++) wood.box(1.4, 0.05, 0.07, 0, WAIST + 0.6, along(0.45), 0x6a4a30, 0, i * Math.PI / 4, 0);
        // coils of rope
        foreach (var (x, u) in new[] { (-1.55, 0.27), (1.5, 0.3), (1.0, 0.63) }) wood.put(new THREE.TorusGeometry(0.22, 0.06, 5, 12), x, WAIST + 0.07, along(u), ROPE, Math.PI / 2);
        // pumps by the mainmast and bitts at the foot of each mast
        wood.rod(0.45, WAIST, along(0.5) + 0.4, 0.45, WAIST + 0.95, along(0.5) + 0.4, 0.05, DARKW);
        wood.rod(0.45, WAIST + 0.95, along(0.5) + 0.4, 0.8, WAIST + 0.8, along(0.5) + 0.4, 0.03, IRON);
        foreach (var u in new[] { 0.25, 0.5 })
            foreach (var x in new[] { -0.4, 0.4 }) wood.cyl(0.07, 0.08, 0.5, 6, x, WAIST + 0.25, along(u) + 0.35, 0x4a3322);

        // up on the quarterdeck: a skylight with a lit pane, the wheel and its binnacle with a lamp in it
        wood.box(1.0, 0.3, 0.7, 0, poopDeck(0.73) + 0.15, along(0.735), 0x4a3322);
        glow.box(0.8, 0.02, 0.5, 0, poopDeck(0.73) + 0.31, along(0.735), 0xffffff);
        double wu = 0.89;
        wood.box(0.5, 0.55, 0.4, 0, poopDeck(wu) + 0.275, along(wu) + 0.22, 0x3a2a1e);
        glow.box(0.18, 0.14, 0.02, 0, poopDeck(wu) + 0.4, along(wu) + 0.02, 0xffffff);
        wood.box(0.16, 0.7, 0.16, 0, poopDeck(wu) + 0.35, along(wu) - 0.35, DARKW);
        // the wheel stands upright across the ship, its hub toward the helmsman: spokes and handles round its rim
        double wy = poopDeck(wu) + 0.95, wz = along(wu) - 0.35;
        wood.put(new THREE.TorusGeometry(0.4, 0.035, 5, 16), 0, wy, wz, 0x6a4a30);
        for (int i = 0; i < 4; i++) wood.box(0.03, 0.96, 0.03, 0, wy, wz, 0x6a4a30, 0, 0, i * Math.PI / 4);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            wood.cyl(0.025, 0.025, 0.26, 4, Math.Sin(a) * 0.5, wy + Math.Cos(a) * 0.5, wz, 0x6a4a30, 0, 0, -a);
        }

        // gun ports: three a side along the waist, each with a cannon run out and a lid hooked up above it
        foreach (var u in new[] { 0.29, 0.39, 0.56 })
            foreach (var side in new[] { -1, 1 })
            {
                double py = WAIST + 0.42;
                var (sx, sz) = surf(u, py);
                wood.box(0.12, 0.34, 0.4, side * (sx - 0.01), py, sz, 0x120b07);
                wood.box(0.05, 0.05, 0.5, side * (sx + 0.05), py + 0.2, sz, MARIGOLD);
                wood.box(0.05, 0.05, 0.5, side * (sx + 0.05), py - 0.2, sz, MARIGOLD);
                // the cannon: a barrel pointing out of the port on a low carriage
                iron.cyl(0.1, 0.15, 1.3, 8, side * (sx - 0.42), py, sz, IRON, 0, 0, Math.PI / 2);
                iron.put(new THREE.TorusGeometry(0.13, 0.025, 4, 8), side * (sx + 0.22), py, sz, IRON, 0, Math.PI / 2);
                wood.box(0.75, 0.2, 0.46, side * (sx - 0.6), WAIST + 0.14, sz, 0x4a3322);
                foreach (var dz in new[] { -0.2, 0.2 }) wood.cyl(0.11, 0.11, 0.06, 8, side * (sx - 0.6), WAIST + 0.11, sz + dz, 0x2a1c12, Math.PI / 2);
            }
        // pin rails along the bulwark at the masts, with belaying pins
        foreach (var u in new[] { 0.25, 0.5 })
            foreach (var side in new[] { -1, 1 })
            {
                var (sx, sz) = surf(u, WAIST + 0.5);
                wood.box(0.06, 0.07, 1.3, side * (sx - 0.12), WAIST + 0.5, sz, 0x2a1c12);
                for (int i = 0; i < 6; i++) wood.rod(side * (sx - 0.12), WAIST + 0.5, sz - 0.55 + i * 0.22, side * (sx - 0.12), WAIST + 0.72, sz - 0.55 + i * 0.22, 0.015, ROPE);
            }
        // the windlass up on the forecastle, with two bitts and a few handspikes
        double wl = 0.1;
        wood.cyl(0.17, 0.17, 1.2, 8, 0, foreDeck(wl) + 0.42, along(wl), DARKW, 0, 0, Math.PI / 2);
        foreach (var x in new[] { -0.55, 0.55 }) wood.box(0.12, 0.45, 0.22, x, foreDeck(wl) + 0.22, along(wl), 0x4a3322);
        foreach (var d in new[] { -0.7, 0.7 }) iron.put(new THREE.TorusGeometry(0.19, 0.02, 4, 10), d * 0.88, foreDeck(wl) + 0.42, along(wl), IRON, 0, Math.PI / 2);
        wood.rod(-0.45, foreDeck(wl) + 0.6, along(wl) - 0.1, -0.7, foreDeck(wl) + 1.0, along(wl) - 0.3, 0.025, 0x6a4a30);
        // the ship's bell on a small frame at the forecastle bulkhead
        wood.rod(-0.3, foreDeck(0.17), along(0.17) + 0.15, -0.3, foreDeck(0.17) + 0.95, along(0.17) + 0.15, 0.03, DARKW);
        wood.rod(0.3, foreDeck(0.17), along(0.17) + 0.15, 0.3, foreDeck(0.17) + 0.95, along(0.17) + 0.15, 0.03, DARKW);
        wood.rod(-0.3, foreDeck(0.17) + 0.95, along(0.17) + 0.15, 0.3, foreDeck(0.17) + 0.95, along(0.17) + 0.15, 0.03, DARKW);
        gold.add(new THREE.SphereGeometry(0.13, 8, 6, 0, Math.PI * 2, 0, Math.PI * 0.75).translate(0, foreDeck(0.17) + 0.72, along(0.17) + 0.15), GOLD);
    }

    private static void buildStern(ShipKit wood, ShipKit gold, ShipKit glow)
    {
        // the stern: a gilded cornice, pilasters, two rows of lit windows, a carved panel above and the rudder under the counter
        double yLow = 0.35, yHigh = 1.85;
        var (_, zLow) = surf(1, yLow);
        var (_, zHigh) = surf(1, yHigh);
        double lean = Math.Atan2(zHigh - zLow, yHigh - yLow);
        double xs = surf(1, 1.2).x;
        double zAt(double y) => surf(1, y).z;
        void pane(double w, double h, double x, double y, double thick, ShipKit k, int col)
        {
            k.box(w, h, thick, x, y, zAt(y) + thick / 2 - 0.01, col, lean);
        }
        // the lower counter's moulding and the broad gold frieze under the quarterdeck rail
        pane(xs * 2 + 0.2, 0.12, 0, 0.1, 0.16, gold, GOLD);
        pane(xs * 2 + 0.14, 0.1, 0, 1.88, 0.16, gold, GOLD);
        pane(xs * 2 + 0.1, 0.34, 0, 2.2, 0.08, wood, 0x24180f);
        foreach (var x in new[] { -1.0, -0.5, 0, 0.5, 1.0 }) pane(0.26, 0.2, x * 0.8, 2.2, 0.1, gold, GOLD);
        // two rows of windows, the lower row the great cabin's
        foreach (var (y, h) in new[] { (0.75, 0.52), (1.5, 0.4) })
            foreach (var x in new[] { -0.9, -0.3, 0.3, 0.9 })
            {
                if (Math.Abs(x) > xs - 0.25) continue;
                pane(0.44, h + 0.1, x, y, 0.1, gold, GOLD);
                pane(0.34, h, x, y, 0.12, glow, 0xffffff);
            }
        foreach (var x in new[] { -1.2, -0.6, 0, 0.6, 1.2 }) if (Math.Abs(x) < xs - 0.1) pane(0.07, 1.55, x, 1.1, 0.1, gold, GOLD);
        // the quarter galleries: a lit window on each corner, jutting from the side
        foreach (var side in new[] { -1, 1 })
        {
            double qx = surf(0.97, 1.3).x, qz = surf(0.97, 1.3).z;
            wood.box(0.34, 1.0, 0.5, side * (qx + 0.12), 1.15, qz - 0.1, 0x3a2a1e);
            glow.box(0.04, 0.46, 0.3, side * (qx + 0.3), 1.2, qz - 0.1, 0xffffff);
            gold.box(0.42, 0.07, 0.58, side * (qx + 0.12), 1.7, qz - 0.1, GOLD);
        }
        // rudder, with its tiller head showing through the counter
        wood.box(0.14, 2.0, 0.55, 0, 0.1, along(1) + 0.55, 0x2a1c12);
        // the taffrail across the stern of the quarterdeck, and the ensign staff
        double sy = poopDeck(1);
        double tz = along(1) - 0.1;
        double tx = half(1, 0) - 0.1;
        wood.box(tx * 2, 0.06, 0.06, 0, sy + 0.62, tz, 0x2a1c12);
        for (int i = -3; i <= 3; i++) wood.rod(i * tx / 3, sy, tz, i * tx / 3, sy + 0.62, tz, 0.025, DARKW);
        // the great stern lantern: four posts, a roof and a flame, on a stout staff over the taffrail
        wood.rod(0, sy, tz - 0.25, 0, sy + 1.3, tz - 0.25, 0.04, DARKW);
        foreach (var dx in new[] { -0.17, 0.17 })
            foreach (var dz in new[] { -0.17, 0.17 }) gold.rod(dx, sy + 1.3, tz - 0.25 + dz, dx, sy + 1.82, tz - 0.25 + dz, 0.02, GOLD);
        gold.add(new THREE.ConeGeometry(0.32, 0.3, 4, 1).rotateY(Math.PI / 4).translate(0, sy + 2.0, tz - 0.25), GOLD);
        gold.box(0.4, 0.07, 0.4, 0, sy + 1.27, tz - 0.25, GOLD);
    }

    private static void buildBow(ShipKit wood, ShipKit gold, ShipKit iron)
    {
        // the beakhead: a small grated platform out past the stem, with a cutwater curving down to the water and a gilded figurehead
        double stemZ = along(0) + shift(0, 0), stemY = sheer(0);
        double pz = stemZ - 1.15;
        wood.addPainted(ShipKit.polygon(new[] { -0.55, stemY - 0.9, stemZ + 0.2, 0.55, stemY - 0.9, stemZ + 0.2, 0.12, stemY - 0.88, pz, -0.12, stemY - 0.88, pz }, 0x6a4a30));
        foreach (var side in new[] { -1, 1 })
        {
            wood.rod(side * 0.55, stemY - 0.85, stemZ + 0.2, side * 0.12, stemY - 0.85, pz, 0.04, 0x2a1c12);
            wood.rod(side * 0.55, stemY - 0.85, stemZ + 0.2, side * 0.12, stemY - 0.45, pz, 0.025, DARKW);
        }
        // knees under it and the cutwater: the stem's own curve carried on to the platform
        wood.rod(0, stemY - 0.9, pz, 0, -0.6, stemZ + 0.45, 0.07, 0x2a1c12);
        wood.rod(0, stemY - 0.9, pz, 0, stemY - 0.2, stemZ + 0.05, 0.1, 0x2a1c12);
        // figurehead: a gilded lion, chest and head carried forward on the end of the platform, with a mane and a scroll
        gold.add(new THREE.SphereGeometry(0.3, 8, 6).scale(0.85, 0.95, 1.5).translate(0, stemY - 0.5, pz + 0.05), GOLD);
        gold.add(new THREE.SphereGeometry(0.2, 8, 6).translate(0, stemY - 0.3, pz - 0.42), GOLD);
        gold.add(new THREE.ConeGeometry(0.1, 0.3, 6).rotateX(-Math.PI / 2).translate(0, stemY - 0.3, pz - 0.7), GOLD);
        gold.add(new THREE.SphereGeometry(0.26, 8, 5, 0, Math.PI * 2, 0, Math.PI * 0.65).scale(1, 0.8, 0.8).translate(0, stemY - 0.2, pz - 0.28), 0xb8832a);
        gold.put(new THREE.TorusGeometry(0.32, 0.045, 5, 12, Math.PI * 1.4), 0, stemY - 0.6, pz + 0.45, GOLD, 0, Math.PI / 2, 0);
        // the bowsprit, rising forward from the forecastle deck
        double bz0 = along(0.08), by0 = foreDeck(0.08) + 0.2;
        double tipZ = stemZ - 4.2, tipY = stemY + 1.35;
        wood.spar(0, by0, bz0, 0, tipY, tipZ, 0.17, 0.07, 0x3a2a1e, 7);
        // an iron-banded cap, a spritsail yard's stump and a bobstay down to the cutwater
        iron.cyl(0.11, 0.11, 0.1, 6, 0, tipY - 0.02, tipZ + 0.05, IRON, Math.PI / 2);
        wood.rod(0, tipY - 0.05, tipZ + 0.1, 0, stemY - 0.9, pz + 0.05, 0.02, ROPE);
        // catheads: short beams standing out each side of the bow, each with an anchor hung from it
        foreach (var side in new[] { -1, 1 })
        {
            double u = 0.12;
            var (sx, sz) = surf(u, foreDeck(u) + 0.1);
            wood.spar(side * (sx - 0.4), foreDeck(u) + 0.35, sz, side * (sx + 0.75), foreDeck(u) + 0.45, sz - 0.2, 0.09, 0.07, 0x2a1c12);
            double ax = side * (sx + 0.7), ay = foreDeck(u) - 0.3, az = sz - 0.2;
            // the anchor: a long shank, a stock across its head, two curved arms at the foot and a ring
            iron.rod(ax, ay, az, ax, ay + 0.95, az, 0.04, IRON);
            iron.rod(ax - 0.35, ay + 0.85, az, ax + 0.35, ay + 0.85, az, 0.03, IRON);
            iron.rod(ax, ay + 1.0, az, ax, ay + 1.15, az, 0.03, IRON);
            iron.put(new THREE.TorusGeometry(0.07, 0.015, 4, 8), ax, ay + 1.15, az, IRON, 0, Math.PI / 2);
            iron.rod(ax, ay, az, ax - 0.34, ay + 0.3, az, 0.035, IRON);
            iron.rod(ax, ay, az, ax + 0.34, ay + 0.3, az, 0.035, IRON);
            iron.rod(ax + side * 0.0, ay + 1.1, az, ax, foreDeck(u) + 0.4, az, 0.012, ROPE);
        }
        // a small gilded carving at each cheek of the bow
        foreach (var side in new[] { -1, 1 })
        {
            var (sx, sz) = surf(0.05, foreDeck(0.05) - 0.2);
            gold.add(new THREE.SphereGeometry(0.14, 6, 5).scale(0.5, 1, 1.5).translate(side * (sx + 0.03), foreDeck(0.05) - 0.45, sz), GOLD);
        }
    }

    /// <summary>A lantern: a small glowing ball that the opening makes gutter in the wind.</summary>
    private static void lamp(THREE.Group ship, List<THREE.Mesh> lamps, double x, double y, double z, double r)
    {
        var m = new THREE.Mesh(new THREE.SphereGeometry(r, 8, 6), new THREE.MeshBasicMaterial { color = 0xffc66a, fog = false });
        m.position.set(x, y, z);
        ship.add(m);
        lamps.Add(m);
    }

    private static void buildMasts(THREE.Group ship, ShipKit wood, ShipKit gold, ShipKit iron, ShipKit cloth, List<THREE.Mesh> lamps)
    {
        double bowZ = along(0) + shift(0, 0);
        double tipZ = bowZ - 4.2, tipY = sheer(0) + 1.35;
        // each of the two big masts: lower mast, top with a platform, topmast, two yards with a sail each, shrouds, ratlines
        double foreTopY = 0, foreTopZ = 0, mainTopY = 0, mainTopZ = 0;
        int seed = 3;
        foreach (var (name, u, H, cw, tw, brace) in new[] { ("fore", 0.25, 10.6, 5.8, 4.2, 0.07), ("main", 0.5, 12.4, 6.4, 4.8, -0.05) })
        {
            double mz = along(u), foot = WAIST - 0.05;
            double topY = foot + 0.6 * H, headY = foot + H;
            wood.spar(0, foot, mz, 0, topY + 0.3, mz, 0.18, 0.11, 0x3a2a1e, 8);
            wood.spar(0, foot + 0.55 * H, mz, 0, headY, mz, 0.08, 0.035, 0x3a2a1e, 6);
            // iron hoops round the mast at the deck, the partners and the top
            foreach (var hy in new[] { foot + 0.4, foot + 1.6, topY - 0.2 }) iron.cyl(0.19, 0.19, 0.06, 8, 0, hy, mz, IRON);
            // the top: a platform with a rim, trestletrees under it
            wood.box(1.5, 0.07, 1.2, 0, topY, mz, 0x4a3322);
            wood.box(1.6, 0.06, 0.07, 0, topY + 0.33, mz - 0.55, 0x2a1c12);
            wood.box(1.6, 0.06, 0.07, 0, topY + 0.33, mz + 0.55, 0x2a1c12);
            wood.box(0.07, 0.06, 1.2, -0.78, topY + 0.33, mz, 0x2a1c12);
            wood.box(0.07, 0.06, 1.2, 0.78, topY + 0.33, mz, 0x2a1c12);
            foreach (var dx in new[] { -0.78, 0.78 })
                foreach (var dz in new[] { -0.55, 0.55 }) wood.rod(dx, topY, mz + dz, dx, topY + 0.33, mz + dz, 0.02, DARKW);
            wood.box(0.12, 0.3, 1.0, -0.2, topY - 0.17, mz, 0x2a1c12);
            wood.box(0.12, 0.3, 1.0, 0.2, topY - 0.17, mz, 0x2a1c12);
            lamp(ship, lamps, 0, headY + 0.1, mz, 0.12);
            gold.add(new THREE.SphereGeometry(0.07, 6, 5).translate(0, headY + 0.28, mz), GOLD);
            if (name == "fore") { foreTopY = headY - 0.3; foreTopZ = mz; } else { mainTopY = headY - 0.3; mainTopZ = mz; }

            // yards and sails. Both sails belly forward, the wind coming from astern, and are hung a little crooked on their braces
            double y1 = foot + 0.4 * H, y2 = foot + 0.76 * H;
            double c1 = Math.Cos(brace), s1 = Math.Sin(brace);
            foreach (var (yy, w, sh, bel) in new[] { (y1, cw, y1 - 2.35, 1.0), (y2, tw, (y2 - y1) - 0.1, 0.8) })
            {
                double yz = mz - 0.25;
                // the yard, a spar thickest at the slings, with iron at the ends
                wood.spar(-(w / 2 + 0.3) * c1, yy, yz + (w / 2 + 0.3) * s1, (w / 2 + 0.3) * c1, yy, yz - (w / 2 + 0.3) * s1, 0.06, 0.06, 0x3a2a1e, 6);
                iron.cyl(0.07, 0.07, 0.1, 6, 0, yy, yz, IRON, 0, 0, Math.PI / 2);
                // the sail: stitched cloth with a bolt-rope round it, hung from the yard, bellied forward, its foot a little ragged
                var g = ShipKit.sail(w, sh, 6, bel, 0.3, 1.16, CANVAS, 0.22, seed++);
                g.rotateY(brace);
                g.translate(0, yy - 0.04, yz - 0.06);
                cloth.addPainted(g);
                // a patch or two sewn on, sitting just proud of the cloth
                cloth.box(0.9, 0.7, 0.03, -w * 0.18, yy - sh * 0.42, yz - bel * 0.62, 0xb8a98a, 0, brace, 0);
                cloth.box(0.6, 0.5, 0.03, w * 0.2, yy - sh * 0.7, yz - bel * 0.5, 0xa89a7c, 0, brace, 0);
                // clew lines and lifts from the yardarms back to the mast
                foreach (var side in new[] { -1, 1 })
                {
                    double ex = side * (w / 2 + 0.2) * c1, ez = yz - side * (w / 2 + 0.2) * s1;
                    wood.rod(ex, yy, ez, side * 0.2, yy + 1.0, mz, 0.014, ROPE);
                    wood.rod(side * (w / 2) * 1.08, yy - sh, yz - 0.6, side * 1.2, WAIST + 1.0, mz + 0.4, 0.012, ROPE);
                }
            }
            // shrouds: four a side from the top down to the rail, with ratlines tied across them
            var (sx, sz) = surf(u, sheer(u) - 0.05);
            foreach (var side in new[] { -1, 1 })
            {
                var feet = new List<(double x, double y, double z)>();
                for (int i = 0; i < 4; i++)
                {
                    double bz = mz - 0.55 + i * 0.37;
                    var tp = (x: side * (0.55 - i * 0.04), y: topY - 0.1, z: mz - 0.3 + i * 0.2);
                    var ft = (x: side * (sx - 0.05), y: sheer(u) + 0.05, z: bz);
                    wood.rod(tp.x, tp.y, tp.z, ft.x, ft.y, ft.z, 0.02, TARRED, 3);
                    feet.Add(ft);
                    // topmast shrouds from the top up to the topmast head
                    wood.rod(side * 0.4, topY + 0.3, mz + (i - 1.5) * 0.25, 0, headY - 0.5, mz, 0.012, TARRED, 3);
                }
                for (int k = 1; k <= 8; k++)
                {
                    double f = k / 9.0;
                    for (int i = 0; i < 3; i++)
                    {
                        double ax = side * ((0.55 - i * 0.04) * f + (sx - 0.05) * (1 - f)), ay = (topY - 0.1) * f + (sheer(u) + 0.05) * (1 - f), az = (mz - 0.3 + i * 0.2) * f + feet[i].z * (1 - f);
                        double bx = side * ((0.55 - (i + 1) * 0.04) * f + (sx - 0.05) * (1 - f)), by = ay, bz2 = (mz - 0.3 + (i + 1) * 0.2) * f + feet[i + 1].z * (1 - f);
                        wood.rod(ax, ay, az, bx, by, bz2, 0.012, RATLINE, 3);
                    }
                }
                // deadeyes and chains at the rail
                for (int i = 0; i < 4; i++) iron.cyl(0.05, 0.05, 0.04, 6, feet[i].x, feet[i].y - 0.04, feet[i].z, IRON);
            }
        }
        // crow's nest barrel on the mainmast, lit from inside
        {
            double mz = along(0.5), nestY = WAIST - 0.05 + 12.4 * 0.9;
            wood.cyl(0.4, 0.34, 0.55, 10, 0, nestY, mz, 0x3a2a1e);
            iron.cyl(0.41, 0.41, 0.04, 10, 0, nestY + 0.1, mz, IRON);
            iron.cyl(0.35, 0.35, 0.04, 10, 0, nestY - 0.18, mz, IRON);
        }
        // the mizzen: a small mast on the quarterdeck, with a gaff and boom and a spanker, and a topsail yard above
        {
            double u = 0.8, mz = along(u), foot = poopDeck(u) - 0.05, H = 7.4;
            wood.spar(0, foot, mz, 0, foot + H, mz, 0.13, 0.04, 0x3a2a1e, 7);
            iron.cyl(0.14, 0.14, 0.05, 8, 0, foot + 0.4, mz, IRON);
            wood.box(0.9, 0.06, 0.8, 0, foot + 0.62 * H, mz, 0x4a3322);
            lamp(ship, lamps, 0, foot + H + 0.1, mz, 0.1);
            // the boom, reaching aft over the stern, and the gaff above it
            double bY = foot + 1.15, gY = foot + 0.66 * H;
            double boomEnd = mz + 3.7, gaffEndZ = mz + 2.6, gaffEndY = gY + 1.5;
            wood.spar(0, bY, mz + 0.1, 0, bY - 0.1, boomEnd, 0.07, 0.045, 0x3a2a1e, 6);
            wood.spar(0, gY, mz + 0.1, 0, gaffEndY, gaffEndZ, 0.06, 0.035, 0x3a2a1e, 6);
            Func<double, double, (double x, double y, double z)> f = (a, b) =>
            {
                // a is along the foot (0 at the mast to 1 at the boom's end), b is up the sail
                double lowY = bY + 0.05 - 0.1 * a, lowZ = mz + 0.15 + (boomEnd - mz - 0.15) * a;
                double hiY = gY + (gaffEndY - gY) * a, hiZ = mz + 0.15 + (gaffEndZ - mz - 0.15) * a;
                double belly = 0.55 * Math.Sin(Math.PI * a) * Math.Sin(Math.PI * (0.15 + 0.7 * b));
                return (belly, lowY + (hiY - lowY) * b, lowZ + (hiZ - lowZ) * b);
            };
            cloth.addPainted(ShipKit.surface(f, 8, 8, (a, b) => ShipKit.shade(CANVAS, 0.88 + 0.1 * (((int)(a * 8)) % 2))));
            // topsail yard and a small sail above the gaff
            wood.spar(-1.4, foot + 0.8 * H, mz - 0.2, 1.4, foot + 0.8 * H, mz - 0.2, 0.045, 0.045, 0x3a2a1e, 5);
            var ts = ShipKit.sail(2.5, 2.0, 4, 0.5, 0.2, 1.15, CANVAS, 0.15, 21);
            ts.translate(0, foot + 0.8 * H - 0.03, mz - 0.28);
            cloth.addPainted(ts);
            // the ensign: a red flag at the gaff's end, streaming forward before the wind
            cloth.addPainted(flag(0, gaffEndY, gaffEndZ, 1.7, 0.95, RED, 0xe9dfc8, 4));
            // shrouds from the top
            foreach (var side in new[] { -1, 1 })
            {
                var (sx, sz) = surf(0.78, sheer(0.78) - 0.05);
                foreach (var dz in new[] { -0.3, 0.1, 0.5 }) wood.rod(side * 0.35, foot + 0.62 * H - 0.05, mz, side * (sx - 0.05), sheer(0.78) + 0.05, mz + dz, 0.016, TARRED, 3);
                for (int k = 1; k <= 5; k++)
                {
                    double f2 = k / 6.0;
                    double ax = side * ((0.35) * f2 + (sx - 0.05) * (1 - f2)), ay = (foot + 0.62 * H - 0.05) * f2 + (sheer(0.78) + 0.05) * (1 - f2);
                    wood.rod(ax, ay, mz * f2 + (mz - 0.3) * (1 - f2), ax, ay, mz * f2 + (mz + 0.5) * (1 - f2), 0.01, RATLINE, 3);
                }
            }
            // backstays to the quarters, and the stay forward to the mainmast's top
            wood.rod(0, foot + 0.9 * H, mz, 0, mainTopY - 1.2, mainTopZ, 0.015, TARRED, 3);
            foreach (var side in new[] { -1, 1 }) wood.rod(0, foot + 0.9 * H, mz, side * 1.6, sheer(0.95) + 0.05, along(0.95), 0.014, TARRED, 3);
        }
        // pennants at the fore and main mastheads, streaming forward, and a house flag below the main top
        cloth.addPainted(flag(0.0, mainTopY + 0.55, mainTopZ, 2.4, 0.35, RED, 0xc9861c, 2, true));
        cloth.addPainted(flag(0.0, foreTopY + 0.5, foreTopZ, 1.9, 0.3, 0xc9861c, 0xb53a2e, 2, true));

        // the stays: the great ropes that hold the masts forward
        wood.rod(0, foreTopY, foreTopZ, 0, tipY, tipZ, 0.02, TARRED, 3);
        wood.rod(0, mainTopY, mainTopZ, 0, foreTopY - 1.2, foreTopZ, 0.02, TARRED, 3);
        wood.rod(0, foreTopY - 2.8, foreTopZ - 0.1, 0, sheer(0) + 0.1, bowZ + 0.6, 0.018, TARRED, 3);
        foreach (var side in new[] { -1, 1 })
        {
            // backstays from the mastheads down to the quarters
            wood.rod(0, foreTopY, foreTopZ, side * 2.2, sheer(0.4), along(0.42), 0.014, TARRED, 3);
            wood.rod(0, mainTopY, mainTopZ, side * 2.2, sheer(0.55), along(0.58), 0.014, TARRED, 3);
        }
        // two jibs and a fore-topmast staysail, with their sheets: three-cornered sails set on the stays, bellied to leeward
        Func<double, double, double> belly = (a, b) => 0.4 * Math.Sin(Math.PI * b) * a * (1 - a * 0.5);
        void triSail(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz, int col, int s)
        {
            Func<double, double, (double x, double y, double z)> f = (a, b) =>
            {
                double q1x = ax + a * (bx - ax), q1y = ay + a * (by - ay), q1z = az + a * (bz - az);
                double q2x = ax + a * (cx - ax), q2y = ay + a * (cy - ay), q2z = az + a * (cz - az);
                return (q1x + (q2x - q1x) * b + belly(a, b), q1y + (q2y - q1y) * b, q1z + (q2z - q1z) * b);
            };
            cloth.addPainted(ShipKit.surface(f, 6, 6, (a, b) => ShipKit.shade(col, 0.9 + 0.1 * (((int)(b * 6)) % 2))));
        }
        // the outer jib: tack at the bowsprit's tip, head at the fore-topmast head, clew in toward the bow
        triSail(0, tipY - 0.05, tipZ + 0.1, 0, foreTopY - 0.5, foreTopZ - 0.15, 0.1, tipY - 0.25, tipZ + 2.0, 0xd8cdb2, 1);
        // the inner jib, below it, set on the fore-topmast stay's lower reach
        double inZ = bowZ - 1.2, inY = sheer(0) + 0.55;
        triSail(0, inY + 0.9, inZ - 0.2, 0, foreTopY - 3.0, foreTopZ - 0.2, 0.1, inY - 0.1, bowZ + 1.2, 0xcfc4a8, 2);
        // sheets running aft from the sails' clews
        foreach (var side in new[] { -1, 1 }) wood.rod(side * 0.1, tipY - 0.25, tipZ + 2.0, side * 1.0, sheer(0.12), along(0.14), 0.012, ROPE, 3);
    }

    /// <summary>A flag or pennant streaming forward (toward -z) from a staff, rippling along its length. `swallow` cuts a notch in the fly.</summary>
    private static THREE.BufferGeometry flag(double x, double y, double z, double len, double h, int color, int stripe, int ripples, bool pennant = false)
    {
        return ShipKit.surface((a, b) =>
        {
            double hh = pennant ? h * (1 - 0.92 * a) : h;
            double wave = Math.Sin(a * ripples * 1.6 - 0.8) * 0.13 * a;
            return (x + wave, y + (b - 0.5) * hh - a * (pennant ? 0.05 : 0.18), z - a * len);
        }, 8, 2, (a, b) => b > 0.4 && b < 0.6 ? stripe : color);
    }
}
