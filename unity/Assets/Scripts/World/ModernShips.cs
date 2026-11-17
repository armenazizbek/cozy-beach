using System;
using System.Collections.Generic;
using static Beach;

/// <summary>
/// The ships that pass along the horizon at night (see <see cref="Lighthouse"/>): four different modern vessels, so no two look
/// alike from the lamp. A container ship stacked with coloured boxes, a tanker with its pipe rack and manifold, a white cruise
/// liner with rows of lit windows and lifeboats, and a bulk carrier with hatch covers and deck cranes. Each has a lofted hull with
/// a raked stem and a painted boot-topping, a proper superstructure with a bridge, a funnel, masts, radar and navigation lights.
/// Bow toward -z, waterline at y = 0, a little under fifty units long. Everything is welded into a few meshes per ship.
/// </summary>
public static class ModernShips
{
    public const int CONTAINER = 0, TANKER = 1, CRUISE = 2, BULK = 3;

    /// <summary>What <see cref="build"/> made: the materials the lighthouse's beam lights up, the lamps that flicker and the sizes.</summary>
    public class Built
    {
        public THREE.MeshStandardMaterial hullMat, cabinMat;
        public THREE.MeshBasicMaterial glowMat, navRedMat, navGreenMat, patchMat, wakeMat;
        public List<THREE.Mesh> lamps = new List<THREE.Mesh>();
        public double length;
    }

    private sealed class Spec
    {
        public double L, B, F, rise, bowLen, sternLen, rake, flare, lowerTop, sternBeam = 0.72;
        public int upper, lower, deck;
    }

    private static double top(Spec s, double u) => s.F + s.rise * Math.Pow(1 - smooth(0, 0.2, u), 1.5);

    private static double half(Spec s, double u, double t)
    {
        double e = u < s.bowLen ? Math.Pow(u / s.bowLen, 0.85) : 1;
        double run = u > 1 - s.sternLen ? 1 - (1 - s.sternBeam) * Math.Pow((u - (1 - s.sternLen)) / s.sternLen, 1.8) : 1;
        double flare = 1 + s.flare * (1 - t) * (1 - smooth(0, s.bowLen * 1.4, u));
        return s.B * e * run * flare * (1 - 0.1 * t * t);
    }

    private static double Z(Spec s, double u) => -s.L / 2 + u * s.L;
    private static double zOf(Spec s, double z) => (z + s.L / 2) / s.L;

    private static double hash(int a, int b)
    {
        double v = Math.Sin(a * 12.9898 + b * 78.233) * 43758.5453;
        return v - Math.Floor(v);
    }

    // ---------------------------------------------------------------- shared pieces

    private static THREE.BufferGeometry hull(Spec s)
    {
        return ShipKit.loft(s.L, 40, 8,
            (u, t) => half(s, u, t),
            (u, t) => top(s, u) + (-0.35 - top(s, u)) * t,
            (u, t) =>
            {
                double y = top(s, u) + (-0.35 - top(s, u)) * t;
                return y < s.lowerTop ? s.lower : ShipKit.shade(s.upper, 0.96 + 0.06 * Math.Sin(u * 40));
            },
            true,
            (u, t) => -s.rake * (1 - smooth(0, s.bowLen * 0.8, u)) * Math.Pow(1 - t, 1.3));
    }

    /// <summary>The weather deck, and the low bulwark that runs round its edge and closes round the bow.</summary>
    private static void deckAndBulwark(Spec s, ShipKit h, double bulwark, int bulwarkColor)
    {
        h.addPainted(ShipKit.deck(s.L, 0, 1, 40, u => half(s, u, 0) - 0.08, u => top(s, u), s.deck));
        if (bulwark <= 0) return;
        foreach (var side in new[] { -1, 1 })
        {
            int sd = side;
            h.addPainted(ShipKit.surface((a, b) =>
            {
                double u = a;
                return (sd * (half(s, u, 0) - 0.05), top(s, u) + b * bulwark, Z(s, u) - s.rake * (1 - smooth(0, s.bowLen * 0.8, u)));
            }, 40, 1, (a, b) => b > 0.5 ? ShipKit.shade(bulwarkColor, 1.15) : bulwarkColor));
        }
    }

    /// <summary>A lit window, facing out to one side (+1 or -1), or forward (0).</summary>
    private static void win(ShipKit g, double w, double h, double x, double y, double z, int facing)
    {
        if (facing == 0) g.put(new THREE.PlaneGeometry(w, h), x, y, z, 0xffffff, 0, Math.PI, 0);
        else g.put(new THREE.PlaneGeometry(w, h), x, y, z, 0xffffff, 0, facing * Math.PI / 2, 0);
    }

    /// <summary>A row of lit windows along both sides of a block, `xOut` from the centre line, between two z positions.</summary>
    private static void windowRow(ShipKit g, double z0, double z1, double y, double h, double w, double pitch, double xOut)
    {
        for (double z = z0 + w / 2 + 0.05; z <= z1 - w / 2 - 0.05; z += pitch)
            foreach (var side in new[] { -1, 1 }) win(g, w, h, side * (xOut + 0.015), y, z, side);
    }

    private static void blockAt(ShipKit k, double halfW, double y0, double y1, double z0, double z1, int color, double x = 0)
        => k.box(halfW * 2, y1 - y0, z1 - z0, x, (y0 + y1) / 2, (z0 + z1) / 2, color);

    /// <summary>A funnel: an oval stack with a colour band and a black top, leaning aft a little.</summary>
    private static void funnel(ShipKit h, double x, double y, double z, double rx, double rz, double height, int body, int band, int topCol, double lean = 0.1)
    {
        var g = new THREE.CylinderGeometry(rx * 0.86, rx, height, 14);
        g.scale(1, 1, rz / rx);
        h.put(g, x, y + height / 2, z, body, lean);
        var bandG = new THREE.CylinderGeometry(rx * 0.865, rx * 0.88, height * 0.22, 14);
        bandG.scale(1, 1, rz / rx);
        h.put(bandG, x, y + height * 0.62, z + lean * height * 0.12, band, lean);
        var capG = new THREE.CylinderGeometry(rx * 0.84, rx * 0.86, height * 0.1, 14);
        capG.scale(1, 1, rz / rx);
        h.put(capG, x, y + height * 0.97, z + lean * height * 0.45, topCol, lean);
    }

    /// <summary>A mast with a yard and a radar scanner, and a lamp at the top (which the lighthouse makes flicker a little).</summary>
    private static void mast(ShipKit h, ShipKit c, Built b, THREE.Group vessel, double x, double y0, double z, double height, bool radar)
    {
        h.cyl(0.07, 0.11, height, 6, x, y0 + height / 2, z, 0xc9ced4);
        h.box(1.9, 0.07, 0.07, x, y0 + height * 0.7, z, 0xc9ced4);
        h.box(0.9, 0.06, 0.06, x, y0 + height * 0.45, z, 0xc9ced4);
        if (radar)
        {
            h.box(0.12, 0.3, 0.6, x - 0.9, y0 + height * 0.7 + 0.2, z, 0x6a7078);
            h.box(1.5, 0.08, 0.16, x + 0.05, y0 + height * 0.55 + 0.1, z, 0x8a9096);
            h.box(0.1, 0.22, 0.1, x + 0.05, y0 + height * 0.55 - 0.02, z, 0x6a7078);
        }
        lamp(vessel, b, x, y0 + height + 0.12, z, 0.2);
    }

    private static void lamp(THREE.Group vessel, Built b, double x, double y, double z, double r)
    {
        var m = new THREE.Mesh(new THREE.SphereGeometry(r, 8, 6), b.glowMat);
        m.position.set(x, y, z);
        vessel.add(m);
        b.lamps.Add(m);
    }

    private static void navLights(THREE.Group vessel, Built b, double wingX, double y, double z)
    {
        var red = new THREE.Mesh(new THREE.SphereGeometry(0.2, 6, 5), b.navRedMat);
        red.position.set(-wingX, y, z);
        vessel.add(red);
        var green = new THREE.Mesh(new THREE.SphereGeometry(0.2, 6, 5), b.navGreenMat);
        green.position.set(wingX, y, z);
        vessel.add(green);
    }

    /// <summary>The white deckhouse at the stern (or wherever): a stack of decks, each a little narrower, a bridge with wings and windows.</summary>
    private static double house(Spec s, ShipKit h, ShipKit c, ShipKit g, Built b, THREE.Group vessel, double z0, double z1, int decks, double deckH, double halfW, bool bridge)
    {
        double y = top(s, zOf(s, (z0 + z1) / 2));
        double w = halfW;
        for (int d = 0; d < decks; d++)
        {
            double y0 = y + d * deckH, y1 = y0 + deckH;
            double dz0 = z0 + d * 0.12, dz1 = z1 - d * 0.04;
            blockAt(c, w, y0, y1, dz0, dz1, d == decks - 1 ? 0xdde2e8 : 0xeef1f4);
            // a thin dark deck edge between decks, then the windows
            blockAt(c, w + 0.06, y1 - 0.05, y1 + 0.02, dz0 - 0.02, dz1 + 0.02, 0x9aa1a8);
            windowRow(g, dz0, dz1 - 0.2, (y0 + y1) / 2 + 0.05, 0.4, 0.55, 0.95, w);
            if (d >= 1) win(g, w * 1.6, 0.34, 0, (y0 + y1) / 2 + 0.05, dz0 - 0.015, 0);
            w -= 0.14;
        }
        double topY = y + decks * deckH;
        if (bridge)
        {
            // the wheelhouse on top: wide, with a band of lit glass round its front and sides, and wings reaching out each side
            double bz0 = z0 + decks * 0.12 - 0.1, bz1 = bz0 + 2.0;
            blockAt(c, w + 0.15, topY, topY + 1.0, bz0, bz1, 0xf0f2f5);
            win(g, (w + 0.15) * 2 - 0.15, 0.5, 0, topY + 0.55, bz0 - 0.015, 0);
            foreach (var side in new[] { -1, 1 })
            {
                win(g, 1.5, 0.5, side * (w + 0.165), topY + 0.55, (bz0 + bz1) / 2, side);
                c.box(2.4, 0.08, 1.0, side * (w + 1.0), topY + 0.45, bz0 + 0.6, 0xdfe3e8);
            }
            blockAt(c, w + 0.3, topY + 1.0, topY + 1.09, bz0 - 0.15, bz1 + 0.1, 0xcfd4da);
            navLights(vessel, b, w + 2.1, topY + 0.7, bz0 + 0.6);
            topY += 1.09;
        }
        return topY;
    }

    // ---------------------------------------------------------------- the four ships

    public static Built build(int kind, THREE.Group vessel)
    {
        var b = new Built();
        b.hullMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.82, emissive = 0xc9964c, emissiveIntensity = 0, side = THREE.Side.DoubleSide, fog = false };
        b.cabinMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.75, emissive = 0xffdf9a, emissiveIntensity = 0, side = THREE.Side.DoubleSide, fog = false };
        b.glowMat = new THREE.MeshBasicMaterial { color = 0xffd88a, fog = false, side = THREE.Side.DoubleSide };
        b.navRedMat = new THREE.MeshBasicMaterial { color = 0xff4b3e, fog = false };
        b.navGreenMat = new THREE.MeshBasicMaterial { color = 0x3ce87a, fog = false };
        var h = new ShipKit();
        var c = new ShipKit();
        var g = new ShipKit();
        var pool = new ShipKit();
        double beam;
        switch (kind)
        {
            case CONTAINER: beam = containerShip(vessel, b, h, c, g); break;
            case TANKER: beam = tanker(vessel, b, h, c, g); break;
            case CRUISE: beam = cruiseLiner(vessel, b, h, c, g, pool); break;
            default: beam = bulkCarrier(vessel, b, h, c, g); break;
        }
        void weld(ShipKit k, THREE.Material m)
        {
            if (k.count == 0) return;
            vessel.add(k.mesh(m, false));
        }
        weld(h, b.hullMat);
        weld(c, b.cabinMat);
        weld(g, b.glowMat);
        weld(pool, new THREE.MeshBasicMaterial { color = 0x6fd6ff, fog = false, side = THREE.Side.DoubleSide });

        // a wake that trails behind her, and the bow wave either side; and the pool of light the beam makes round her when it sweeps by
        double L = b.length;
        b.wakeMat = new THREE.MeshBasicMaterial { color = 0xdbeeff, transparent = true, opacity = 0.26, depthWrite = false, fog = false, side = THREE.Side.DoubleSide };
        var trail = ShipKit.surface((a, t) => (((t * 2 - 1) * (beam * 0.75 + a * beam * 1.5)), 0.06, L / 2 + a * L * 0.95), 6, 2, (a, t) => 0xffffff);
        var bowL = ShipKit.surface((a, t) => (-(beam * 0.95 + a * beam * 1.15) + t * 0.5, 0.06, -L / 2 + 1.0 + a * L * 0.28), 5, 1, (a, t) => 0xffffff);
        var bowR = ShipKit.surface((a, t) => ((beam * 0.95 + a * beam * 1.15) - t * 0.5, 0.06, -L / 2 + 1.0 + a * L * 0.28), 5, 1, (a, t) => 0xffffff);
        var wake = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(new List<THREE.BufferGeometry> { trail, bowL, bowR }), b.wakeMat);
        wake.renderOrder = 3;
        vessel.add(wake);
        b.patchMat = new THREE.MeshBasicMaterial { color = 0xffe8a8, transparent = true, opacity = 0, blending = THREE.Blending.AdditiveBlending, depthWrite = false, fog = false, side = THREE.Side.DoubleSide };
        var patch = new THREE.Mesh(new THREE.CircleGeometry(L * 0.5, 24), b.patchMat);
        patch.rotation.x = -Math.PI / 2;
        patch.position.set(0, 0.08, 0);
        patch.renderOrder = 4;
        vessel.add(patch);
        return b;
    }

    // ---------------- container ship: navy hull, stacks of coloured boxes, the house and funnel right aft

    private static double containerShip(THREE.Group vessel, Built b, ShipKit h, ShipKit c, ShipKit g)
    {
        var s = new Spec { L = 48, B = 3.9, F = 3.3, rise = 0.9, bowLen = 0.2, sternLen = 0.2, rake = 2.0, flare = 0.25, lowerTop = 0.85, upper = 0x1d2d4a, lower = 0x8a2d25, deck = 0x3a3f46 };
        b.length = s.L;
        h.addPainted(hull(s));
        deckAndBulwark(s, h, 0.4, 0x1d2d4a);
        // the forecastle: windlass, a pair of mooring winches, an anchor-chain cover and a short mast with lights
        double bowZ = Z(s, 0.07);
        h.box(1.4, 0.4, 1.0, 0, top(s, 0.07) + 0.2, bowZ, 0x55606c);
        foreach (var x in new[] { -1.1, 1.1 }) h.cyl(0.3, 0.3, 0.45, 8, x, top(s, 0.08) + 0.22, Z(s, 0.1), 0x6a7078);
        mast(h, c, b, vessel, 0, top(s, 0.09), Z(s, 0.1) + 0.6, 3.2, false);
        // the stacks: nine bays between the forecastle and the house, as many rows across as the hull allows, tiers rising toward the middle
        int[] palette = { 0xb03a2e, 0x2f5d8a, 0xd98c1f, 0x3f7d4e, 0xe3ded2, 0x7a7f86, 0xc9b22b, 0x8f4a2b, 0x26323f, 0x6a3f7a, 0x1f7a8c };
        const double CW = 1.26, CH = 1.24, CL = 3.4;
        for (int bay = 0; bay < 9; bay++)
        {
            double z = Z(s, 0.17) + bay * (CL + 0.14) + CL / 2;
            double width = 2 * (half(s, zOf(s, z), 0) - 0.3);
            int rows = Math.Max(1, (int)(width / (CW + 0.06)));
            for (int r = 0; r < rows; r++)
            {
                double x = (r - (rows - 1) / 2.0) * (CW + 0.06);
                double mid = 1 - Math.Abs(x) / (width / 2 + 0.01);
                int tiers = 2 + (int)(hash(bay, r) * 1.6 + mid * 1.9);
                // the bays nearest the house are kept low so the bridge can see over them
                if (bay >= 7) tiers = Math.Min(tiers, 3);
                for (int t = 0; t < tiers; t++)
                {
                    int col = palette[(int)(hash(bay / 2 + r / 2 * 3, t / 2 + 11) * palette.Length) % palette.Length];
                    double shade = 0.82 + 0.2 * hash(bay * 7 + r, t);
                    h.box(CW, CH - 0.04, CL, x, top(s, zOf(s, z)) + 0.1 + t * CH + CH / 2, z, ShipKit.shade(col, shade));
                    // the door-end ribs, as a darker strip on the aft face
                    h.box(CW - 0.1, CH - 0.2, 0.04, x, top(s, zOf(s, z)) + 0.1 + t * CH + CH / 2, z + CL / 2 + 0.02, ShipKit.shade(col, shade * 0.7));
                }
            }
            // a lashing bridge between the bays: a low rail with a lamp at each end
            h.box(width, 0.06, 0.1, 0, top(s, zOf(s, z)) + 0.55, z + CL / 2 + 0.07, 0x7a8088);
        }
        // the house right aft, the funnel behind its bridge, and the aft mast
        double hz0 = Z(s, 0.8), hz1 = Z(s, 0.875);
        double topY = house(s, h, c, g, b, vessel, hz0, hz1, 5, 1.1, 3.1, true);
        funnel(h, 0, topY - 3.0, Z(s, 0.9), 1.0, 1.35, 3.6, 0x2a2f38, 0xd9792b, 0x14181d, 0.12);
        mast(h, c, b, vessel, 0, topY, (hz0 + hz1) / 2 - 0.3, 3.2, true);
        // the aft deck: mooring gear and a crane for the stores
        h.box(1.4, 0.4, 1.0, 0, top(s, 0.93) + 0.2, Z(s, 0.945), 0x55606c);
        h.cyl(0.3, 0.3, 1.8, 8, 2.4, top(s, 0.95) + 0.9, Z(s, 0.945), 0xd9a02b);
        h.rod(2.4, top(s, 0.95) + 1.8, Z(s, 0.945), 1.6, top(s, 0.95) + 2.6, Z(s, 0.945) + 1.4, 0.07, 0xd9a02b, 5);
        // a stern light low on the transom
        lamp(vessel, b, 0, top(s, 1) + 0.9, s.L / 2 - 0.1, 0.18);
        return s.B;
    }

    // ---------------- tanker: low and long, the pipe rack down its back, the manifold amidships, the house aft

    private static double tanker(THREE.Group vessel, Built b, ShipKit h, ShipKit c, ShipKit g)
    {
        var s = new Spec { L = 56, B = 4.3, F = 2.1, rise = 0.8, bowLen = 0.18, sternLen = 0.2, rake = 1.4, flare = 0.15, lowerTop = 0.9, upper = 0x232830, lower = 0x7b2a22, deck = 0x6b3a2e, sternBeam = 0.8 };
        b.length = s.L;
        h.addPainted(hull(s));
        deckAndBulwark(s, h, 0.5, 0x232830);
        // the forecastle: windlass, the mooring winches, the bulbous cover of the chain locker and a mast
        h.box(1.8, 0.5, 1.2, 0, top(s, 0.06) + 0.25, Z(s, 0.07), 0x55606c);
        foreach (var x in new[] { -1.4, 1.4 }) h.cyl(0.35, 0.35, 0.5, 8, x, top(s, 0.09) + 0.25, Z(s, 0.1), 0x6a7078);
        mast(h, c, b, vessel, 0, top(s, 0.1), Z(s, 0.12), 3.8, false);
        // the pipe rack: a catwalk along the centre line on posts, with a rail, from the forecastle to the house
        double rackY = top(s, 0.5) + 0.85;
        double rz0 = Z(s, 0.2), rz1 = Z(s, 0.82);
        h.box(0.9, 0.08, rz1 - rz0, 0, rackY, (rz0 + rz1) / 2, 0xb0a590);
        foreach (var sx in new[] { -0.42, 0.42 })
        {
            h.box(0.04, 0.05, rz1 - rz0, sx, rackY + 0.55, (rz0 + rz1) / 2, 0xd0c9b5);
        }
        for (double z = rz0; z <= rz1; z += 3.6)
        {
            h.box(0.1, 0.85, 0.1, -0.4, rackY - 0.42, z, 0x6a7078);
            h.box(0.1, 0.85, 0.1, 0.4, rackY - 0.42, z, 0x6a7078);
            foreach (var sx in new[] { -0.42, 0.42 }) h.box(0.04, 0.55, 0.04, sx, rackY + 0.28, z, 0xd0c9b5);
        }
        // the cargo lines under the catwalk: two fat pipes lengthwise
        foreach (var x in new[] { -0.9, 0.9 }) h.cyl(0.22, 0.22, rz1 - rz0, 8, x, top(s, 0.5) + 0.42, (rz0 + rz1) / 2, 0x4a5058, Math.PI / 2);
        // the manifold amidships: cross pipes out to both sides ending in flanges, and a hose derrick each side
        for (int i = 0; i < 4; i++)
        {
            double z = Z(s, 0.46) + i * 1.0;
            h.cyl(0.14, 0.14, 2 * (s.B - 0.5), 8, 0, top(s, 0.5) + 0.4 + (i % 2) * 0.12, z, 0x7a808a, 0, 0, Math.PI / 2);
            foreach (var side in new[] { -1, 1 })
            {
                h.cyl(0.24, 0.24, 0.12, 8, side * (s.B - 0.55), top(s, 0.5) + 0.4 + (i % 2) * 0.12, z, 0xb0a07a, 0, 0, Math.PI / 2);
            }
        }
        foreach (var side in new[] { -1, 1 })
        {
            h.cyl(0.14, 0.18, 2.4, 6, side * (s.B - 1.0), top(s, 0.5) + 1.2, Z(s, 0.5) + 0.6, 0xc9ced4);
            h.rod(side * (s.B - 1.0), top(s, 0.5) + 2.4, Z(s, 0.5) + 0.6, side * (s.B + 0.8), top(s, 0.5) + 2.0, Z(s, 0.5) + 0.6, 0.07, 0xc9ced4, 5);
        }
        // the tank hatches: round lids in two rows, with the vent posts and the tall mast risers between them
        for (int i = 0; i < 9; i++)
        {
            double z = Z(s, 0.2) + i * (s.L * 0.64 / 8.0);
            foreach (var side in new[] { -1, 1 })
            {
                double x = side * (s.B * 0.55);
                h.cyl(0.55, 0.6, 0.18, 12, x, top(s, zOf(s, z)) + 0.09, z, 0x8a4a3a);
                if (i % 2 == 0) h.cyl(0.05, 0.07, 1.4, 5, x * 0.62, top(s, zOf(s, z)) + 0.7, z, 0xc9ced4);
            }
        }
        // the house, aft: five decks with a bridge on top, the funnel behind it and the mast over the bridge
        double hz0 = Z(s, 0.855), hz1 = Z(s, 0.965);
        double topY = house(s, h, c, g, b, vessel, hz0, hz1, 5, 1.15, 3.4, true);
        funnel(h, 0, topY - 3.2, Z(s, 0.945), 1.0, 1.1, 3.6, 0xc4c8cd, 0x2d6fb0, 0x14181d, 0.1);
        mast(h, c, b, vessel, 0, topY, (hz0 + hz1) / 2 - 0.9, 3.6, true);
        // deck lamps, the stern light and a flare mast at the stern
        foreach (var side in new[] { -1, 1 }) h.cyl(0.06, 0.06, 1.2, 5, side * 3.7, top(s, 0.88) + 0.6, Z(s, 0.84), 0xc9ced4);
        lamp(vessel, b, 0, top(s, 1) + 0.9, s.L / 2 - 0.1, 0.18);
        return s.B;
    }

    // ---------------- cruise liner: white decks stacked up to a sun deck, lit windows by the hundred, lifeboats along her side

    private static double cruiseLiner(THREE.Group vessel, Built b, ShipKit h, ShipKit c, ShipKit g, ShipKit pool)
    {
        var s = new Spec { L = 52, B = 3.9, F = 2.7, rise = 0.9, bowLen = 0.22, sternLen = 0.22, rake = 1.8, flare = 0.3, lowerTop = 0.7, upper = 0xeceff3, lower = 0x1a2c4c, deck = 0xb08a58, sternBeam = 0.75 };
        b.length = s.L;
        h.addPainted(hull(s));
        // a navy stripe along the hull under the rail, and the lit portholes of the lowest cabins
        h.addPainted(ShipKit.surface((a, t) =>
        {
            double u = a;
            double y = top(s, u) - 0.55 - t * 0.3;
            double tt = (top(s, u) - y) / (top(s, u) + 0.35);
            return (half(s, u, tt) + 0.02, y, Z(s, u) - s.rake * (1 - smooth(0, s.bowLen * 0.8, u)) * Math.Pow(1 - tt, 1.3));
        }, 40, 1, (a, t) => 0x2a4a7a));
        deckAndBulwark(s, h, 0.0, 0);
        foreach (var side in new[] { -1, 1 })
            for (double z = Z(s, 0.22); z < Z(s, 0.9); z += 1.6)
            {
                double u = zOf(s, z);
                win(g, 0.26, 0.26, side * (half(s, u, 0.45 / (top(s, u) + 0.35)) + 0.035), top(s, u) - 0.45, z, side);
            }
        // the decks of the superstructure, each shorter than the one below, with rows of lit windows and balcony rails under them
        double[,] decksU = { { 0.1, 0.93 }, { 0.14, 0.91 }, { 0.2, 0.89 }, { 0.26, 0.87 }, { 0.33, 0.82 }, { 0.4, 0.74 }, { 0.47, 0.66 } };
        double y0 = top(s, 0.5);
        double w = s.B - 0.08;
        double prevTop = y0;
        for (int d = 0; d < decksU.GetLength(0); d++)
        {
            double z0 = Z(s, decksU[d, 0]), z1 = Z(s, decksU[d, 1]);
            double yA = y0 + d * 1.1, yB = yA + 1.1;
            double dw = Math.Min(w, half(s, zOf(s, (z0 + z1) / 2), 0) - 0.1) - d * 0.12;
            // each deck's body, and a pale rail band under the next
            c.box(dw * 2, 1.1, z1 - z0, 0, (yA + yB) / 2, (z0 + z1) / 2, 0xf3f5f7);
            c.box(dw * 2 + 0.12, 0.07, z1 - z0 + 0.1, 0, yB - 0.03, (z0 + z1) / 2, 0xc9ced4);
            if (d >= 1) windowRow(g, z0 + 0.3, z1 - 0.3, (yA + yB) / 2 + 0.05, 0.42, 0.62, 1.0, dw);
            // the decks' ends: forward-facing glass on the lounges, and the lit stern
            if (d >= 1 && d <= 4) win(g, dw * 1.7, 0.4, 0, (yA + yB) / 2 + 0.05, z0 - 0.015, 0);
            prevTop = yB;
            // lifeboats, a row each side on the second deck, hung out in their davits
            if (d == 1)
                for (double z = z0 + 3; z < z1 - 3; z += 2.7)
                    foreach (var side in new[] { -1, 1 })
                    {
                        h.sphere(0.4, side * (dw + 0.38), yA + 0.45, z, 0xe8730c, 0.55, 0.55, 1.9, 8, 5);
                        h.box(0.08, 0.5, 0.08, side * (dw + 0.15), yA + 0.7, z - 0.5, 0x8a9096);
                        h.box(0.08, 0.5, 0.08, side * (dw + 0.15), yA + 0.7, z + 0.5, 0x8a9096);
                    }
        }
        // the bridge across the front of the fifth deck, wider than the deck below it, with wings and a band of lit glass
        double bzF = Z(s, 0.33) - 0.2, bzB = bzF + 2.4, byA = y0 + 4 * 1.1 + 1.1;
        c.box(2 * (w - 0.35) + 0.5, 1.0, bzB - bzF, 0, byA + 0.5, (bzF + bzB) / 2, 0xf3f5f7);
        win(g, 2 * (w - 0.35) + 0.2, 0.46, 0, byA + 0.55, bzF - 0.015, 0);
        foreach (var side in new[] { -1, 1 })
        {
            win(g, 1.6, 0.46, side * (w - 0.35 + 0.265), byA + 0.55, (bzF + bzB) / 2, side);
            c.box(2.2, 0.08, 1.0, side * (w + 0.7), byA + 0.4, bzF + 0.9, 0xdfe3e8);
        }
        navLights(vessel, b, w + 1.7, byA + 0.65, bzF + 0.9);
        // the sun deck: a lit blue pool aft of the funnel, loungers' glow, and the big funnel with a mast forward of it
        double sunY = y0 + decksU.GetLength(0) * 1.1;
        // the pool sits on the roof of the fifth deck, where the sixth stops short of it, with a rail either side
        double poolY = y0 + 5 * 1.1;
        pool.box(2.2, 0.05, 3.2, 0, poolY + 0.03, Z(s, 0.78), 0xffffff);
        h.box(0.06, 0.4, 3.4, -1.3, poolY + 0.2, Z(s, 0.78), 0xc9ced4);
        h.box(0.06, 0.4, 3.4, 1.3, poolY + 0.2, Z(s, 0.78), 0xc9ced4);
        funnel(h, 0, sunY - 1.1 - 0.2, Z(s, 0.58), 1.15, 1.9, 4.2, 0xe5b52e, 0x1a2c4c, 0x14181d, 0.14);
        mast(h, c, b, vessel, 0, byA + 1.0, Z(s, 0.35), 3.4, true);
        // a second, aft funnel, small, for the galley, and the stern light
        funnel(h, 0, sunY - 1.2, Z(s, 0.82), 0.55, 0.7, 1.8, 0xe5b52e, 0x1a2c4c, 0x14181d, 0.1);
        lamp(vessel, b, 0, top(s, 1) + 0.7, s.L / 2 - 0.1, 0.18);
        return s.B;
    }

    // ---------------- bulk carrier: a long run of hatch covers with a crane standing in each gap, the house aft

    private static double bulkCarrier(THREE.Group vessel, Built b, ShipKit h, ShipKit c, ShipKit g)
    {
        var s = new Spec { L = 46, B = 3.7, F = 2.3, rise = 0.9, bowLen = 0.2, sternLen = 0.2, rake = 1.6, flare = 0.2, lowerTop = 0.85, upper = 0x1d3b34, lower = 0x7b2a22, deck = 0x2b4a3a, sternBeam = 0.78 };
        b.length = s.L;
        h.addPainted(hull(s));
        deckAndBulwark(s, h, 0.45, 0x1d3b34);
        h.box(1.6, 0.45, 1.1, 0, top(s, 0.06) + 0.22, Z(s, 0.07), 0x55606c);
        foreach (var x in new[] { -1.2, 1.2 }) h.cyl(0.32, 0.32, 0.45, 8, x, top(s, 0.09) + 0.22, Z(s, 0.1), 0x6a7078);
        mast(h, c, b, vessel, 0, top(s, 0.1), Z(s, 0.12), 3.4, false);
        // five hatch covers in a line, each a raised box with a coaming and cross-joints, and the crane between each pair
        double start = Z(s, 0.17);
        const double HL = 4.8, GAP = 1.55;
        for (int i = 0; i < 5; i++)
        {
            double z = start + i * (HL + GAP) + HL / 2;
            double u = zOf(s, z);
            double hw = half(s, u, 0) - 0.55;
            h.box(hw * 2, 0.55, HL, 0, top(s, u) + 0.27, z, 0x35604a);
            h.box(hw * 2 + 0.16, 0.12, HL + 0.16, 0, top(s, u) + 0.5, z, 0x244636);
            for (int k = -1; k <= 1; k++) h.box(hw * 2, 0.04, 0.06, 0, top(s, u) + 0.56, z + k * HL * 0.3, 0x1c3a2c);
            foreach (var sx in new[] { -hw * 0.5, hw * 0.5 }) h.cyl(0.18, 0.18, 0.06, 8, sx, top(s, u) + 0.57, z, 0x8a8f95);
        }
        for (int i = 0; i < 4; i++)
        {
            double z = start + (i + 1) * (HL + GAP) - GAP / 2;
            double u = zOf(s, z);
            double y = top(s, u);
            int dir = i % 2 == 0 ? -1 : 1;
            // the crane: a pedestal, a cab with a window, a lattice jib raised toward the bow or the stern, a rest post for it
            h.cyl(0.5, 0.55, 1.5, 10, 0, y + 0.75, z, 0x6a7078);
            h.box(1.2, 1.0, 1.5, 0, y + 2.0, z, 0xe0a52c);
            g.box(0.5, 0.3, 0.04, 0, y + 2.1, z - 0.76 * dir - 0.0, 0xffffff);
            h.box(0.9, 0.12, 0.9, 0, y + 2.56, z, 0x6a7078);
            double jx = 0, jy = y + 2.3, jz = z + dir * 0.4;
            double ex = 0, ey = jy + 4.1, ez = jz + dir * 4.5;
            h.spar(jx - 0.14, jy, jz, ex - 0.07, ey, ez, 0.12, 0.07, 0xe0a52c, 5);
            h.spar(jx + 0.14, jy, jz, ex + 0.07, ey, ez, 0.12, 0.07, 0xe0a52c, 5);
            for (int k = 1; k < 5; k++)
            {
                double f0 = (k - 0.5) / 5, f1 = (k + 0.5) / 5;
                h.rod(-0.14 + f0 * 0.07, jy + (ey - jy) * f0, jz + (ez - jz) * f0, 0.14 - f1 * 0.07, jy + (ey - jy) * f1, jz + (ez - jz) * f1, 0.03, 0xe0a52c, 3);
            }
            // the derrick wire from the cab's top to the jib's head, and the hook hanging from it
            h.rod(0, y + 2.6, z - dir * 0.2, ex, ey, ez, 0.02, 0x2a2f38, 3);
            h.rod(ex, ey, ez, ex, ey - 2.0, ez, 0.015, 0x2a2f38, 3);
            h.box(0.2, 0.25, 0.2, ex, ey - 2.1, ez, 0x2a2f38);
            h.cyl(0.07, 0.09, 1.2, 6, 0, y + 0.6, z - dir * 2.3, 0x6a7078);
        }
        // the house aft: four decks, a bridge on top, the funnel and the mast
        double hz0 = Z(s, 0.86), hz1 = Z(s, 0.955);
        double topY = house(s, h, c, g, b, vessel, hz0, hz1, 4, 1.15, 3.1, true);
        funnel(h, 0, topY - 2.6, Z(s, 0.935), 0.95, 1.1, 3.0, 0x3a424c, 0x2d6fb0, 0x14181d, 0.1);
        mast(h, c, b, vessel, 0, topY, (hz0 + hz1) / 2 - 0.9, 3.4, true);
        lamp(vessel, b, 0, top(s, 1) + 0.9, s.L / 2 - 0.1, 0.18);
        return s.B;
    }
}
