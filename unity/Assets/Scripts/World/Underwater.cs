using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/world/Underwater.ts

/// <summary>The sea floor: sand, rocks, coral, kelp, a wreck, fish, bubbles and the harpoon's spears.</summary>
public class Underwater
{
    /// <summary>The sea floor is a small separate world, far from the beach. Nothing on land is drawn while someone is down here.</summary>
    public static readonly (double x, double z, double half) UW = (1200, 1200, 58);
    /// <summary>the height of the water's surface seen from below</summary>
    public const double UW_SURFACE = 0;
    /// <summary>where the diver slips into the water: the shallow edge</summary>
    public static readonly (double x, double z) UW_ENTRY = (UW.x, UW.z + 48);

    private static readonly (double x, double z) WRECK = (UW.x + 14, UW.z - 18);

    internal static Func<double> seeded(int seed)
    {
        int s = seed;
        return () =>
        {
            s = unchecked(s + 0x6d2b79f5);
            int t = JS.Imul(s ^ (int)((uint)s >> 15), 1 | s);
            t = unchecked(t + JS.Imul(t ^ (int)((uint)t >> 7), 61 | t)) ^ t;
            return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
        };
    }

    private class Fish
    {
        public THREE.Group root;
        public THREE.Mesh tail;
        public double hit;
        public double x;
        public double y;
        public double z;
        public double heading;
        public double turn;
        public double speed;
        public double ty;
        public double phase;
        public bool alive;
        public double respawn;
        /// <summary>which of Catches.REEF it is</summary>
        public int species;
        /// <summary>the one golden fish: very fast, and it darts away in bursts</summary>
        public bool gold;
        public double dash;
    }

    private class Spear
    {
        public THREE.Group mesh;
        public bool active;
        public THREE.Vector3 pos;
        public THREE.Vector3 dir;
        public double dist;
        public double stuck;
    }

    public class FindSpot
    {
        public string id;
        public double x;
        public double z;
    }

    public readonly THREE.Group group = new THREE.Group();
    public readonly (double x, double z) wreck = WRECK;
    private readonly THREE.Mesh floorMesh;
    private readonly THREE.Mesh surface;
    private readonly List<(THREE.Object3D o, double ph, double amp)> kelp = new List<(THREE.Object3D, double, double)>();
    private readonly List<Fish> fish = new List<Fish>();
    private readonly List<Spear> spears = new List<Spear>();
    private readonly List<FindSpot> spots = new List<FindSpot>();
    private readonly ParticleSystem bubbles = new ParticleSystem(260, 0.07, -1.1, false);
    private readonly ParticleSystem motes = new ParticleSystem(150, 0.045, 0, false);
    private readonly THREE.Color bubbleColor = new THREE.Color(0xd8f3ff);
    private readonly THREE.Color moteColor = new THREE.Color(0xcfe9e0);
    private double breath = 2;
    private double cooldown = 0;
    private readonly Func<double> fishRand = seeded(77);
    /// <summary>called with the spot where a fish was speared</summary>
    public Action<THREE.Vector3, double, string> onCatch = (_at, _reward, _label) => { };
    public readonly SeaCreatures creatures;
    /// <summary>the light, the rock formations, the shoals and the jellyfish</summary>
    private readonly UnderwaterDecor decor;

    public Underwater()
    {
        var rand = seeded(2024);
        group.visible = false;
        group.matrixWorldAutoUpdate = false;

        // ---- the floor
        // the floor runs far past the swimming area, with fine cells near the middle and coarse ones far off, so its edge is never in view
        const int seg = 200;
        const double R = 360;
        var geo = new THREE.PlaneGeometry(2, 2, seg, seg);
        geo.rotateX(-Math.PI / 2);
        var pos = geo.attributes.position;
        for (int i = 0; i < pos.count; i++)
        {
            double u = pos.getX(i);
            double w = pos.getZ(i);
            pos.setXYZ(i, JS.Sign(u) * Math.Pow(Math.Abs(u), 1.6) * R, 0, JS.Sign(w) * Math.Pow(Math.Abs(w), 1.6) * R);
        }
        var col = new float[pos.count * 3];
        var sand = new THREE.Color(0xdcc693);
        var deep = new THREE.Color(0x6f8f88);
        var c = new THREE.Color();
        for (int i = 0; i < pos.count; i++)
        {
            double x = pos.getX(i) + UW.x;
            double z = pos.getZ(i) + UW.z;
            double y = floorAt(x, z);
            pos.setXYZ(i, pos.getX(i), y, pos.getZ(i));
            double k = Math.Min(1, Math.Max(0, (-y - 5.4) / 21));
            double ripple = 0.5 + 0.5 * Math.Sin((x * 0.9 + Math.Sin(z * 0.3) * 2.2) * 1.6);
            // paler on the rises and darker in the hollows, with broad patches of coarser, greyer sand
            double rise = Math.Sin((x - UW.x) * 0.05 + 0.8) * Math.Cos((z - UW.z) * 0.045 + 0.3);
            double patch = 0.5 + 0.5 * Math.Sin(x * 0.21 + Math.Sin(z * 0.17) * 1.8) * Math.Sin(z * 0.19 - x * 0.07);
            c.copy(sand).lerp(deep, k * 0.8).lerp(deep, patch * 0.22).multiplyScalar(0.88 + ripple * 0.12 + rise * 0.1);
            col[i * 3] = (float)c.r;
            col[i * 3 + 1] = (float)c.g;
            col[i * 3 + 2] = (float)c.b;
        }
        geo.setAttribute("color", new THREE.BufferAttribute(col, 3));
        geo.translate(UW.x, 0, UW.z);
        geo.computeVertexNormals();
        floorMesh = new THREE.Mesh(geo, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1 });
        group.add(floorMesh);

        // ---- the surface, seen from below
        surface = new THREE.Mesh(
            new THREE.PlaneGeometry(500, 500),
            new THREE.MeshBasicMaterial { color = 0x9fe6f7, transparent = true, opacity = 0.55, side = THREE.Side.DoubleSide, depthWrite = false });
        surface.rotation.x = Math.PI / 2;
        surface.position.y = UW_SURFACE;
        group.add(surface);

        // the light shafts that used to be placed here drew 32 numbers from the sequence; keep drawing them so the plants, rocks and finds stay where they were
        for (int i = 0; i < 32; i++) rand();
        buildRocks(rand);
        var coralSpots = buildCoral(rand);
        buildKelp(rand);
        buildWreck();
        buildFish();
        buildSpears();
        creatures = new SeaCreatures((x, z) => floorAt(x, z));
        group.add(creatures.group);
        layOutFinds(rand, coralSpots);
        decor = new UnderwaterDecor(group, (x, z) => floorAt(x, z), WRECK, (o, ph, amp) => kelp.Add((o, ph, amp)));
        group.add(bubbles.points, motes.points);
    }

    /// <summary>The height of the sea floor: a shelf that falls away from the shallow edge, with ripples and slow hills.</summary>
    public double floorAt(double x, double z)
    {
        double lx = x - UW.x;
        double lz = z - UW.z;
        double t = Math.Min(1, Math.Max(0, (lz + UW.half) / (UW.half * 2)));
        double y = (-9.6 + t * 7.8) * 3;
        y += Math.Sin(lx * 0.35 + lz * 0.12) * 0.1 + Math.Sin(lx * 0.13 - lz * 0.3) * 0.16;
        y += Math.Sin(lx * 0.07) * Math.Cos(lz * 0.06) * 0.8;
        // long rises and hollows, so there is always something over the next ridge
        y += Math.Sin(lx * 0.05 + 0.8) * Math.Cos(lz * 0.045 + 0.3) * 1.5 + Math.Sin(lx * 0.11 - lz * 0.09) * 0.45;
        return y;
    }

    public bool contains(double x, double z) =>
        Math.Abs(x - UW.x) < UW.half + 10 && Math.Abs(z - UW.z) < UW.half + 10;

    public void setActive(bool on)
    {
        group.visible = on;
        group.matrixWorldAutoUpdate = on;
        if (!on)
        {
            foreach (var s in spears)
            {
                s.active = false;
                s.mesh.visible = false;
            }
        }
    }

    public List<FindSpot> findSpots() => spots;

    // ---------------------------------------------------------------- scenery

    private void onFloor(THREE.Object3D o, double x, double z, double sink = 0)
    {
        o.position.set(x, floorAt(x, z) - sink, z);
        group.add(o);
    }

    private void buildRocks(Func<double> rand)
    {
        var geo = new THREE.DodecahedronGeometry(1, 0);
        var mats = new[] { 0x7d7f80, 0x6a6a64, 0x5f716a }.Select(k => new THREE.MeshStandardMaterial { color = k, roughness = 1, flatShading = true }).ToArray();
        for (int i = 0; i < 30; i++)
        {
            double x = UW.x + (rand() - 0.5) * UW.half * 1.8;
            double z = UW.z + (rand() - 0.5) * UW.half * 1.8;
            if (JS.Hypot(x - WRECK.x, z - WRECK.z) < 7) continue;
            var m = new THREE.Mesh(geo, mats[i % 3]);
            double s = 0.4 + rand() * 1.1;
            m.scale.set(s * (1 + rand() * 0.6), s * (0.6 + rand() * 0.4), s * (1 + rand() * 0.5));
            m.rotation.set(rand() * 3, rand() * 3, rand() * 3);
            onFloor(m, x, z, s * 0.25);
        }
    }

    /// <summary>Several kinds of coral in clumps, with sea fans and rocks between. Returns where they stand, so oysters can lie beside them.</summary>
    private List<(double x, double z)> buildCoral(Func<double> rand)
    {
        THREE.Color C(int k) => new THREE.Color(k);
        var palettes = new (THREE.Color lo, THREE.Color hi)[]
        {
            (C(0xc2455f), C(0xffb0c0)), (C(0xd9742a), C(0xffd08a)), (C(0x7a4ab8), C(0xe0c4ff)), (C(0xd9b232), C(0xfff2a0)), (C(0x2f9a8a), C(0xb4fff0)),
        };
        var fans = new[] { 0xe85a7a, 0xb06ad8, 0xf08a3a };
        var coralSpots = new List<(double x, double z)>();
        for (int i = 0; i < 40; i++)
        {
            double x = UW.x + (rand() - 0.5) * UW.half * 1.7;
            double z = UW.z + (rand() - 0.4) * UW.half * 1.5;
            if (JS.Hypot(x - WRECK.x, z - WRECK.z) < 6) continue;
            var (lo, hi) = palettes[(int)Math.Floor(rand() * palettes.Length)];
            int kind = i % 5;
            var g = new THREE.Group();
            // a clump is a few pieces of one kind, close together
            int n = kind == 2 ? 1 : 2 + (int)Math.Floor(rand() * 3);
            for (int k = 0; k < n; k++)
            {
                THREE.Object3D piece;
                if (kind == 0 || kind == 4) piece = SeaFlora.staghorn(rand, lo, hi);
                else if (kind == 1) piece = SeaFlora.brainCoral(rand, C(0x8f8a3a), C(0xd7cf6a));
                else if (kind == 2) piece = SeaFlora.seaFan(fans[(int)Math.Floor(rand() * fans.Length)]);
                else piece = SeaFlora.tubeCoral(rand, lo, hi);
                double a = rand() * Math.PI * 2;
                double d = n == 1 ? 0 : 0.35 + rand() * 0.5;
                piece.position.set(Math.Cos(a) * d, 0, Math.Sin(a) * d);
                piece.rotation.y = rand() * 6;
                piece.scale.setScalar(0.7 + rand() * 0.7);
                g.add(piece);
            }
            // a few tufts of grass at the foot of it
            for (int k = 0; k < 2; k++)
            {
                var t = SeaFlora.eelgrass(rand);
                double a = rand() * 6;
                t.position.set(Math.Cos(a) * 0.9, 0, Math.Sin(a) * 0.9);
                g.add(t);
                kelp.Add((t, rand() * 6, 0.1));
            }
            onFloor(g, x, z, 0.04);
            if (kind == 2) kelp.Add((g.children[0], rand() * 6, 0.025));
            coralSpots.Add((x, z));
        }
        return coralSpots;
    }

    /// <summary>Meadows of seagrass, eel grass and swaying kelp forests.</summary>
    private void buildKelp(Func<double> rand)
    {
        for (int i = 0; i < 170; i++)
        {
            double x = UW.x + (rand() - 0.5) * UW.half * 1.9;
            double z = UW.z + (rand() - 0.5) * UW.half * 1.9;
            if (JS.Hypot(x - WRECK.x, z - WRECK.z) < 4.5) continue;
            var t = SeaFlora.seagrass(rand);
            onFloor(t, x, z, 0.03);
            kelp.Add((t, rand() * 6, 0.08));
        }
        // kelp grows in forests, deeper out where the water is
        for (int f = 0; f < 6; f++)
        {
            double cx = UW.x + (rand() - 0.5) * UW.half * 1.5;
            double cz = UW.z + (rand() * 0.8 - 0.8) * UW.half;
            if (JS.Hypot(cx - WRECK.x, cz - WRECK.z) < 12) continue;
            for (int i = 0; i < 9; i++)
            {
                double x = cx + (rand() - 0.5) * 9;
                double z = cz + (rand() - 0.5) * 9;
                var k = SeaFlora.kelp(rand);
                onFloor(k, x, z, 0.05);
                kelp.Add((k, rand() * 6, 0.07));
            }
        }
    }

    private void buildWreck()
    {
        var wood = new THREE.MeshStandardMaterial { color = 0x5a4332, roughness = 1 };
        var mossy = new THREE.MeshStandardMaterial { color = 0x4d5f45, roughness = 1 };
        var iron = new THREE.MeshStandardMaterial { color = 0x40464a, roughness = 0.7, metalness = 0.5 };
        var g = new THREE.Group();
        var keel = new THREE.Mesh(new THREE.BoxGeometry(11, 0.3, 0.4), wood);
        keel.position.y = 0.15;
        g.add(keel);
        g.add(UnderwaterDecor.hullShell());
        // the stump of the mainmast still stands, with its top and a spar hanging from it
        var stump = new THREE.Mesh(new THREE.CylinderGeometry(0.13, 0.18, 4.6, 8), wood);
        stump.position.set(0.8, 2.4, 0);
        stump.rotation.set(0.1, 0, -0.14);
        g.add(stump);
        var top = new THREE.Mesh(new THREE.CylinderGeometry(0.55, 0.4, 0.3, 10, 1, true), mossy);
        top.position.set(1.25, 4.1, 0.2);
        top.rotation.copy(stump.rotation);
        g.add(top);
        var spar = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.06, 3.6, 6), wood);
        spar.position.set(1.0, 3.3, 0.1);
        spar.rotation.set(Math.PI / 2 - 0.35, 0.3, 0.2);
        g.add(spar);
        // the ribs of the hull, broken off in places
        for (int i = 0; i < 11; i++)
        {
            double x = -5 + i;
            double k = 1 - Math.Pow(Math.Abs(x) / 5.4, 2.2);
            double r = 0.6 + k * 1.4;
            bool broken = i == 4 || i == 5 || i == 8;
            double arc = broken ? Math.PI * (0.45 + (i % 3) * 0.08) : Math.PI * 0.92;
            var rib = new THREE.Mesh(new THREE.TorusGeometry(r, 0.09, 6, 14, arc), i % 2 != 0 ? wood : mossy);
            rib.position.set(x, r * 0.95, 0);
            rib.rotation.set(0, Math.PI / 2, Math.PI + (Math.PI - arc) / 2 - (broken && i == 8 ? 0.8 : 0));
            g.add(rib);
        }
        // planks still clinging to the sides
        foreach (var side in new[] { -1, 1 })
        {
            for (int p = 0; p < 4; p++)
            {
                double len = 2.5 + ((p * 7 + (side > 0 ? 3 : 0)) % 4) * 0.9;
                var plank = new THREE.Mesh(new THREE.BoxGeometry(len, 0.05, 0.28), p % 2 != 0 ? wood : mossy);
                double r = 1.6 - p * 0.15;
                plank.position.set(-2.8 + p * 1.6 + side * 0.4, 0.5 + p * 0.28, side * (r - p * 0.18));
                plank.rotation.set(side * 0.55, 0, 0);
                g.add(plank);
            }
        }
        // a fallen mast and yard
        var mast = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.16, 7, 8), wood);
        mast.position.set(-1.5, 0.45, 2.4);
        mast.rotation.set(0.05, 0.35, Math.PI / 2 - 0.12);
        g.add(mast);
        var yard = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.07, 4.2, 6), wood);
        yard.position.set(2.2, 0.35, 3.1);
        yard.rotation.set(0.3, 1.1, Math.PI / 2);
        g.add(yard);
        // crates, barrels and an anchor
        var crate = new THREE.BoxGeometry(0.7, 0.55, 0.7);
        foreach (var (x, z, r) in new[] { (-0.6, 0.2, 0.2), (0.5, -0.5, 0.7), (1.6, 0.3, 0.1), (-3.2, -0.4, 0.5), (3.4, 0.5, 0.3) })
        {
            var c = new THREE.Mesh(crate, mossy);
            c.position.set(x, 0.3, z);
            c.rotation.set(0.1, r, 0.12);
            g.add(c);
        }
        foreach (var (x, z) in new[] { (4.4, -1.4), (5.2, 1.6), (-4.6, 1.8) })
        {
            var b = new THREE.Mesh(new THREE.CylinderGeometry(0.28, 0.28, 0.7, 10), wood);
            b.position.set(x, 0.25, z);
            b.rotation.set(Math.PI / 2 * (x > 0 ? 0 : 1), 0, 0.3);
            g.add(b);
            var band = new THREE.Mesh(new THREE.TorusGeometry(0.28, 0.02, 5, 14), iron);
            band.position.copy(b.position);
            band.rotation.copy(b.rotation);
            band.rotation.x += Math.PI / 2;
            g.add(band);
        }
        var anchor = new THREE.Group();
        anchor.add(new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 1.7, 6), iron));
        var arms = new THREE.Mesh(new THREE.TorusGeometry(0.55, 0.06, 6, 14, Math.PI), iron);
        arms.position.y = -0.75;
        arms.rotation.z = Math.PI;
        var stock = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 1.0, 6), iron);
        stock.position.y = 0.8;
        stock.rotation.z = Math.PI / 2;
        var ring = new THREE.Mesh(new THREE.TorusGeometry(0.14, 0.03, 5, 10), iron);
        ring.position.y = 0.95;
        anchor.add(arms, stock, ring);
        anchor.position.set(7, 0.7, -0.5);
        anchor.rotation.set(1.2, 0.5, 0.35);
        g.add(anchor);
        g.rotation.y = 0.5;
        onFloor(g, WRECK.x, WRECK.z, 0.35);
    }

    // ---------------------------------------------------------------- finds

    private void layOutFinds(Func<double> rand, List<(double x, double z)> coral)
    {
        void add(string id, double x, double z)
        {
            spots.Add(new FindSpot { id = id, x = x, z = z });
        }
        var w = WRECK;
        // gold in the hold, a bell at each end
        add("gold_bar", w.x - 1.2, w.z + 0.5);
        add("gold_bar", w.x + 0.7, w.z - 0.9);
        add("gold_bar", w.x + 1.9, w.z + 0.2);
        add("ship_bell", w.x - 5.2, w.z + 2.6);
        add("ship_bell", w.x + 5.4, w.z - 2.8);
        // plates in the wreckage
        for (int i = 0; i < 4; i++)
        {
            double a = rand() * Math.PI * 2;
            double r = 3 + rand() * 5;
            add("porcelain_plate", w.x + Math.Cos(a) * r, w.z + Math.Sin(a) * r);
        }
        // an oyster beside a few of the corals
        for (int i = 0; i < 4; i++)
        {
            var c = coral[(i * 7 + 2) % coral.Count];
            add("sea_oyster", c.x + 0.9, c.z + 0.5);
        }
        // the message in a bottle: out past the wreck, in the deep
        add("message_bottle", UW.x - 21, UW.z - 30);
        // starfish scattered on the sand
        for (int i = 0; i < 10; i++)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                double x = UW.x + (rand() - 0.5) * UW.half * 1.6;
                double z = UW.z + (rand() * 0.9 - 0.45) * UW.half * 1.8;
                if (JS.Hypot(x - w.x, z - w.z) < 7) continue;
                add("starfish", x, z);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- fish and spears

    /// <summary>One species of fish: a smooth body with a pale belly and markings, fins, eyes, and a forked tail that is its own piece so it can wag.</summary>
    internal static (THREE.BufferGeometry body, THREE.BufferGeometry tail) fishModel(int top, int belly, int accent, int pattern)
    {
        var cTop = new THREE.Color(top);
        var cBelly = new THREE.Color(belly);
        var cAcc = new THREE.Color(accent);
        THREE.BufferGeometry solid(THREE.BufferGeometry g, THREE.Color col0)
        {
            g.deleteAttribute("uv");
            int n = g.attributes.position.count;
            var colArr = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                colArr[i * 3] = (float)col0.r;
                colArr[i * 3 + 1] = (float)col0.g;
                colArr[i * 3 + 2] = (float)col0.b;
            }
            g.setAttribute("color", new THREE.BufferAttribute(colArr, 3));
            return g;
        }
        // the body: a loft along z, nose at +z, from a rounded profile
        var prof = new THREE.SplineCurve(new (double u, double r)[]
        {
            (0, 0), (0.05, 0.42), (0.18, 0.82), (0.4, 1), (0.6, 0.86), (0.78, 0.5), (0.9, 0.24), (1, 0.16),
        }.Select(pt => new THREE.Vector2(pt.u, pt.r)).ToList());
        const int along = 28;
        const int around = 16;
        var pos = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        var c = new THREE.Color();
        for (int i = 0; i <= along; i++)
        {
            double u = (double)i / along;
            double r = Math.Max(0, prof.getPoint(u).y);
            double z = 0.5 - u * 0.92;
            for (int j = 0; j < around; j++)
            {
                double phi = (double)j / around * Math.PI * 2;
                double sy = Math.Sin(phi);
                double h = 0.2 * r * (sy > 0 ? 1.05 : 0.85);
                double w = 0.085 * r;
                pos.Add(Math.Cos(phi) * w);
                pos.Add(sy * h);
                pos.Add(z);
                double back = Math.Min(1, Math.Max(0, (sy + 0.35) / 0.9));
                c.copy(cBelly).lerp(cTop, back * back * (3 - 2 * back));
                // markings: pale bands (clown fish), a stripe along the flank, or a darker back
                if (pattern == 1 && ((u > 0.2 && u < 0.3) || (u > 0.5 && u < 0.6))) c.set(0xf6f6f2);
                else if (pattern == 2 && Math.Abs(sy + 0.05) < 0.2) c.lerp(cAcc, 0.8);
                else if (pattern == 3 && u > 0.7) c.copy(cAcc);
                col.Add(c.r);
                col.Add(c.g);
                col.Add(c.b);
            }
        }
        for (int i = 0; i < along; i++)
        {
            for (int j = 0; j < around; j++)
            {
                int a = i * around + j;
                int b = i * around + ((j + 1) % around);
                idx.Add(a);
                idx.Add(a + around);
                idx.Add(b);
                idx.Add(b);
                idx.Add(a + around);
                idx.Add(b + around);
            }
        }
        var body = new THREE.BufferGeometry();
        body.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        body.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        body.setIndex(idx);
        body.computeVertexNormals();
        var parts = new List<THREE.BufferGeometry> { body };
        // a flat fin from a shape in (z, y), thin
        THREE.BufferGeometry fin((double z, double y)[] pts, THREE.Color colour)
        {
            var sh = new THREE.Shape();
            for (int i = 0; i < pts.Length; i++)
            {
                if (i != 0) sh.lineTo(pts[i].z, pts[i].y);
                else sh.moveTo(pts[i].z, pts[i].y);
            }
            var g = new THREE.ShapeGeometry(sh);
            g.rotateY(-Math.PI / 2); // the shape's x becomes z
            return solid(g, colour);
        }
        parts.Add(fin(new[] { (0.2, 0.17), (0.1, 0.25), (-0.02, 0.26), (-0.2, 0.15) }, cAcc));
        parts.Add(fin(new[] { (-0.08, -0.13), (-0.2, -0.2), (-0.24, -0.12) }, cAcc));
        foreach (var side in new[] { -1, 1 })
        {
            var pf = solid(new THREE.ShapeGeometry(new THREE.Shape(new[] { new THREE.Vector2(0, 0), new THREE.Vector2(0.1, 0.05), new THREE.Vector2(0.17, -0.04), new THREE.Vector2(0.03, -0.04) })), cAcc);
            pf.rotateX(Math.PI / 2);
            pf.rotateZ(side * 0.7);
            pf.translate(side * 0.065, -0.03, 0.12);
            parts.Add(pf);
            var eye = solid(new THREE.SphereGeometry(0.03, 8, 6), new THREE.Color(0xfafafa));
            eye.translate(side * 0.066, 0.05, 0.37);
            parts.Add(eye);
            var pupil = solid(new THREE.SphereGeometry(0.017, 6, 5), new THREE.Color(0x101820));
            pupil.translate(side * 0.083, 0.05, 0.375);
            parts.Add(pupil);
        }
        var tailGeo = fin(new[] { (0.0, 0.02), (-0.18, 0.17), (-0.13, 0.0), (-0.18, -0.17), (0.0, -0.02) }, cAcc);
        return (THREE.BufferGeometryUtils.mergeGeometries(parts, false), tailGeo);
    }

    private void buildFish()
    {
        // the reef's fish are the ones the net can bring home (see Catches.REEF)
        var species = Catches.REEF.Select(s => fishModel(s.top, s.belly, s.accent, s.pattern)).ToArray();
        var mat = fishMat;
        var rand = fishRand;
        for (int i = 0; i < 30; i++)
        {
            var m = species[i % species.Length];
            var root = new THREE.Group();
            root.add(new THREE.Mesh(m.body, mat));
            var tail = new THREE.Mesh(m.tail, mat);
            tail.position.z = -0.42;
            root.add(tail);
            root.scale.setScalar(0.55 + rand() * 0.65);
            group.add(root);
            var f = new Fish
            {
                root = root, tail = tail, hit = 0.7 * root.scale.x, x = 0, y = 0, z = 0, heading = rand() * 6.28, turn = 0, speed = 0.8 + rand() * 0.9,
                ty = 0, phase = rand() * 6, alive = true, respawn = 0, species = i % species.Length,
            };
            placeFish(f);
            fish.Add(f);
        }
        // the golden fish: just one, bigger than most, and quick
        var (gRoot, gTail) = makeFish(Catches.GOLD);
        gRoot.scale.setScalar(0.85);
        group.add(gRoot);
        var gf = new Fish
        {
            root = gRoot, tail = gTail, hit = 0.5, x = 0, y = 0, z = 0, heading = rand() * 6.28, turn = 0, speed = 3.2,
            ty = 0, phase = 1.3, alive = true, respawn = 0, species = -1, gold = true,
        };
        placeFish(gf);
        fish.Add(gf);
        golden = gf;
    }

    private Fish golden;

    /// <summary>Where the golden fish is right now, or null once it has been caught (the -autoshot-fish test).</summary>
    public THREE.Vector3 goldAt() => golden != null && golden.alive ? new THREE.Vector3(golden.x, golden.y, golden.z) : null;

    /// <summary>Once the golden fish is in the aquarium it is gone from the sea for good.</summary>
    public void setGoldCaught(bool caught)
    {
        if (golden == null) return;
        golden.alive = !caught;
        golden.root.visible = !caught;
        golden.respawn = 1e9;
    }

    private void placeFish(Fish f)
    {
        var rand = fishRand;
        f.x = UW.x + (rand() - 0.5) * UW.half * 1.7;
        f.z = UW.z + (rand() - 0.2) * UW.half * 1.5;
        double floor = floorAt(f.x, f.z);
        f.y = floor + 0.8 + rand() * Math.Max(0.5, -floor - 2.2);
        f.ty = f.y;
        f.alive = true;
        f.root.visible = true;
    }

    private void buildSpears()
    {
        var shaft = new THREE.MeshStandardMaterial { color = 0xcfd6dc, metalness = 0.8, roughness = 0.3 };
        var tip = new THREE.MeshStandardMaterial { color = 0xf2f4f6, metalness = 0.9, roughness = 0.2 };
        for (int i = 0; i < 6; i++)
        {
            var m = new THREE.Group();
            var s = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.015, 1.1, 6), shaft);
            s.rotation.x = Math.PI / 2;
            var t = new THREE.Mesh(new THREE.ConeGeometry(0.04, 0.2, 6), tip);
            t.rotation.x = Math.PI / 2;
            t.position.z = 0.65;
            m.add(s, t);
            m.visible = false;
            group.add(m);
            spears.Add(new Spear { mesh = m, active = false, pos = new THREE.Vector3(), dir = new THREE.Vector3(), dist = 0, stuck = 0 });
        }
    }

    private readonly THREE.MeshStandardMaterial fishMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.4, metalness = 0.12, side = THREE.Side.DoubleSide };

    /// <summary>A fish of one species to show somewhere else (the aquarium, the end of a fishing line): its body, and the tail that wags.</summary>
    public (THREE.Group root, THREE.Mesh tail) makeFish(SeaSpecies s)
    {
        var m = fishModel(s.top, s.belly, s.accent, s.pattern);
        var root = new THREE.Group();
        root.add(new THREE.Mesh(m.body, fishMat));
        var tail = new THREE.Mesh(m.tail, fishMat);
        tail.position.z = -0.42;
        root.add(tail);
        return (root, tail);
    }

    /// <summary>
    /// The net: takes the nearest fish or animal within reach in front of the diver, alive. Returns what it was, or null when the
    /// net came up empty. The creature is gone from the sea for a while, as when speared.
    /// </summary>
    public SeaSpecies scoop(THREE.Vector3 origin, THREE.Vector3 dir, double reach = 2.6)
    {
        var d = dir.clone().normalize();
        var to = new THREE.Vector3();
        double best = double.PositiveInfinity;
        Fish bestFish = null;
        Animal bestAnimal = null;
        var at = new THREE.Vector3();
        // how well a point is placed for the net: close and near the middle of the view scores low; out of reach is no catch
        double score(THREE.Vector3 p, double size)
        {
            to.copy(p).sub(origin);
            double dist = to.length();
            if (dist > reach + size) return double.PositiveInfinity;
            double facing = dist > 1e-6 ? to.dot(d) / dist : 1;
            // a wide mouth: anything within about 50 degrees of where the diver looks, wider still when it is very close
            if (facing < (dist < 1 + size ? 0.2 : 0.62)) return double.PositiveInfinity;
            return dist * (1.6 - facing);
        }
        var p = new THREE.Vector3();
        foreach (var f in fish)
        {
            if (!f.alive) continue;
            double sc = score(p.set(f.x, f.y, f.z), f.hit);
            if (sc < best)
            {
                best = sc;
                bestFish = f;
                bestAnimal = null;
                at.copy(p);
            }
        }
        foreach (var an in creatures.animals)
        {
            if (!an.alive) continue;
            double size = an.kind == "turtle" ? 0.9 : 0.4;
            if (an.pts.Count == 0)
            {
                double sc = score(p.set(an.x, an.y, an.z), size);
                if (sc < best)
                {
                    best = sc;
                    bestFish = null;
                    bestAnimal = an;
                    at.copy(p);
                }
                continue;
            }
            foreach (var pt in an.pts)
            {
                double sc = score(pt, size);
                if (sc < best)
                {
                    best = sc;
                    bestFish = null;
                    bestAnimal = an;
                    at.copy(pt);
                }
            }
        }
        if (bestFish == null && bestAnimal == null) return null;
        for (int i = 0; i < 16; i++) bubbles.emit(at, new THREE.Vector3((JS.Random() - 0.5) * 1.2, JS.Random() * 0.9, (JS.Random() - 0.5) * 1.2), 1.6, bubbleColor);
        if (bestFish != null)
        {
            bestFish.alive = false;
            bestFish.root.visible = false;
            bestFish.respawn = 25 + JS.Random() * 15;
            return bestFish.gold ? Catches.GOLD : Catches.REEF[bestFish.species];
        }
        creatures.catchAnimal(bestAnimal);
        // a turtle is back at once after a spear glances off it; after the net it stays away a while like the rest
        if (bestAnimal.kind == "turtle") bestAnimal.respawn = 40 + JS.Random() * 20;
        return Catches.animal(bestAnimal.kind);
    }

    /// <summary>Looses a spear from `origin` along `dir`. Returns false while the gun is still being reloaded.</summary>
    public bool fire(THREE.Vector3 origin, THREE.Vector3 dir)
    {
        if (cooldown > 0) return false;
        var s = spears.FirstOrDefault(p => !p.active);
        if (s == null) return false;
        cooldown = 0.7;
        s.active = true;
        s.stuck = 0;
        s.dist = 0;
        s.pos.copy(origin);
        s.dir.copy(dir).normalize();
        s.mesh.visible = true;
        s.mesh.position.copy(s.pos);
        s.mesh.lookAt(s.pos.clone().add(s.dir));
        for (int i = 0; i < 5; i++) bubbles.emit(s.pos, new THREE.Vector3((JS.Random() - 0.5) * 0.4, 0.2 + JS.Random() * 0.4, (JS.Random() - 0.5) * 0.4), 1.4, bubbleColor);
        return true;
    }

    public bool reloading => cooldown > 0;

    private readonly THREE.Color surfaceBase = new THREE.Color(0x9fe6f7);

    /// <summary>At night the surface seen from below is dark too. 1 is full daylight.</summary>
    public void setDaylight(double k)
    {
        ((THREE.MeshBasicMaterial)surface.material).color.copy(surfaceBase).multiplyScalar(k);
        decor.setDaylight(k);
    }

    public void update(double dt, double t, THREE.Vector3 cam)
    {
        cooldown = Math.Max(0, cooldown - dt);
        surface.position.set(cam.x, UW_SURFACE, cam.z);
        foreach (var k in kelp)
        {
            k.o.rotation.z = Math.Sin(t * 0.9 + k.ph) * k.amp;
            k.o.rotation.x = Math.Cos(t * 0.7 + k.ph) * k.amp * 0.8;
        }

        // fish drift about, and slip away from a diver who comes too close
        foreach (var f in fish)
        {
            if (!f.alive)
            {
                if (f.gold) continue;
                f.respawn -= dt;
                if (f.respawn <= 0) placeFish(f);
                continue;
            }
            double dx = cam.x - f.x;
            double dz = cam.z - f.z;
            double d = JS.Hypot(dx, dz, cam.y - f.y);
            if (JS.Random() < dt * 0.4) f.turn = (JS.Random() - 0.5) * 1.6;
            double speed = f.speed;
            if (f.gold && d < 5)
            {
                // it sees the diver from afar and bolts in bursts: a hard dash, then a short glide, then another. A diver who keeps
                // chasing gains a little on every glide, and the edges of the water turn it back, so it can be caught.
                f.dash += dt;
                double cycle = f.dash % 1.7;
                speed = cycle < 0.7 ? 7 : 1.2;
                double away = Math.Atan2(-dx, -dz);
                double da = away - f.heading;
                da = Math.Atan2(Math.Sin(da), Math.Cos(da));
                f.heading += da * Math.Min(1, dt * 5) + (JS.Random() - 0.5) * dt * 2.5;
                double lx0 = f.x - UW.x, lz0 = f.z - UW.z;
                if (Math.Abs(lx0) > UW.half - 3 || Math.Abs(lz0) > UW.half - 3)
                {
                    double back = Math.Atan2(-lx0, -lz0) - f.heading;
                    back = Math.Atan2(Math.Sin(back), Math.Cos(back));
                    f.heading += back * Math.Min(1, dt * 3);
                }
            }
            else if (d < 3.2 && !f.gold)
            {
                // away from the diver
                double away = Math.Atan2(-dx, -dz);
                double da = away - f.heading;
                da = Math.Atan2(Math.Sin(da), Math.Cos(da));
                f.heading += da * Math.Min(1, dt * 4);
                speed *= 2.4;
            }
            else
            {
                f.heading += f.turn * dt;
                // turn back at the edge of the water
                double lx = f.x - UW.x;
                double lz = f.z - UW.z;
                if (Math.Abs(lx) > UW.half - 4 || Math.Abs(lz) > UW.half - 4)
                {
                    double da = Math.Atan2(-lx, -lz) - f.heading;
                    da = Math.Atan2(Math.Sin(da), Math.Cos(da));
                    f.heading += da * Math.Min(1, dt * 2);
                }
            }
            f.x += Math.Sin(f.heading) * speed * dt;
            f.z += Math.Cos(f.heading) * speed * dt;
            double floor = floorAt(f.x, f.z);
            if (JS.Random() < dt * 0.3) f.ty = floor + 0.7 + JS.Random() * Math.Max(0.5, -floor - 2.2);
            f.ty = Math.Min(UW_SURFACE - 1.4, Math.Max(floor + 0.6, f.ty));
            f.y += (f.ty - f.y) * Math.Min(1, dt * 0.8);
            f.root.position.set(f.x, f.y, f.z);
            f.root.rotation.y = f.heading;
            f.tail.rotation.y = Math.Sin(t * (7 + speed * 3) + f.phase) * 0.5;
        }

        // spears fly straight, and stop in the first fish or the sand
        foreach (var s in spears)
        {
            if (!s.active) continue;
            if (s.stuck > 0)
            {
                s.stuck -= dt;
                if (s.stuck <= 0)
                {
                    s.active = false;
                    s.mesh.visible = false;
                }
                continue;
            }
            double step = 26 * dt;
            var from = s.pos.clone();
            s.pos.addScaledVector(s.dir, step);
            s.dist += step;
            s.mesh.position.copy(s.pos);
            if (JS.Random() < 0.7) bubbles.emit(s.pos, new THREE.Vector3(0, 0.3, 0), 0.9, bubbleColor);
            var seg = new THREE.Line3(from, s.pos.clone());
            var tmp = new THREE.Vector3();
            bool caught = false;
            foreach (var f in fish)
            {
                // the golden fish is far too quick for a spear
                if (!f.alive || f.gold) continue;
                seg.closestPointToPoint(new THREE.Vector3(f.x, f.y, f.z), true, tmp);
                if (tmp.distanceTo(new THREE.Vector3(f.x, f.y, f.z)) < f.hit)
                {
                    f.alive = false;
                    f.root.visible = false;
                    f.respawn = 25 + JS.Random() * 15;
                    var at = new THREE.Vector3(f.x, f.y, f.z);
                    for (int i = 0; i < 14; i++) bubbles.emit(at, new THREE.Vector3((JS.Random() - 0.5) * 1.2, JS.Random() * 0.8, (JS.Random() - 0.5) * 1.2), 1.6, bubbleColor);
                    onCatch(at, 1, "fish");
                    caught = true;
                    break;
                }
            }
            if (!caught)
            {
                foreach (var an in creatures.animals)
                {
                    if (!an.alive || an.r <= 0) continue;
                    foreach (var p in an.pts)
                    {
                        seg.closestPointToPoint(p, true, tmp);
                        if (tmp.distanceTo(p) < an.r)
                        {
                            creatures.catchAnimal(an);
                            for (int i = 0; i < 14; i++) bubbles.emit(p, new THREE.Vector3((JS.Random() - 0.5) * 1.2, JS.Random() * 0.8, (JS.Random() - 0.5) * 1.2), 1.6, bubbleColor);
                            onCatch(p.clone(), an.reward, an.label);
                            caught = true;
                            break;
                        }
                    }
                    if (caught) break;
                }
            }
            if (caught)
            {
                s.active = false;
                s.mesh.visible = false;
            }
            else if (s.pos.y < floorAt(s.pos.x, s.pos.z) + 0.05)
            {
                s.stuck = 1.2;
                for (int i = 0; i < 6; i++) bubbles.emit(s.pos, new THREE.Vector3((JS.Random() - 0.5) * 0.8, JS.Random() * 0.4, (JS.Random() - 0.5) * 0.8), 1, moteColor);
            }
            else if (s.dist > 18 || s.pos.y > UW_SURFACE - 0.1)
            {
                s.active = false;
                s.mesh.visible = false;
            }
        }

        // the diver's own bubbles, and specks drifting in the light
        breath -= dt;
        if (breath <= 0)
        {
            breath = 3.2 + JS.Random() * 1.6;
            for (int i = 0; i < 7; i++)
            {
                bubbles.emit(
                    new THREE.Vector3(cam.x + (JS.Random() - 0.5) * 0.15, cam.y - 0.1, cam.z + (JS.Random() - 0.5) * 0.15),
                    new THREE.Vector3((JS.Random() - 0.5) * 0.2, 0.5 + JS.Random() * 0.4, (JS.Random() - 0.5) * 0.2),
                    2.2,
                    bubbleColor);
            }
        }
        if (JS.Random() < dt * 40)
        {
            motes.emit(
                new THREE.Vector3(cam.x + (JS.Random() - 0.5) * 14, cam.y + (JS.Random() - 0.5) * 6, cam.z + (JS.Random() - 0.5) * 14),
                new THREE.Vector3((JS.Random() - 0.5) * 0.12, (JS.Random() - 0.3) * 0.08, (JS.Random() - 0.5) * 0.12),
                7,
                moteColor);
        }
        creatures.update(dt, t, cam);
        decor.update(dt, t, cam);
        bubbles.update(dt);
        motes.update(dt);
    }
}
