using System;
using System.Collections.Generic;
using System.Linq;
using static Underwater;

// Port of src/world/SeaCreatures.ts

/// <summary>A swimmer or crawler the harpoon can hit.</summary>
public class Animal
{
    /// <summary>'octopus' | 'seahorse' | 'snake' | 'crab' | 'turtle'</summary>
    public string kind;
    public string label;
    public double reward;
    /// <summary>how close the spear has to pass to one of the points</summary>
    public double r;
    /// <summary>the places on the body that count as hit, in world space</summary>
    public List<THREE.Vector3> pts;
    public bool alive;
    public double respawn;
    /// <summary>seconds a catch stays gone</summary>
    public double gone;
    public THREE.Object3D root;
    public double x;
    public double y;
    public double z;
    public double heading;
    /// <summary>moves and animates it</summary>
    public Action<double, double, THREE.Vector3> tick;
    /// <summary>puts it somewhere new in the water</summary>
    public Action place;
}

/// <summary>Octopuses, seahorses, sea snakes and crabs living on the sea floor.</summary>
public class SeaCreatures
{
    private static double rnd(double a, double b) => a + JS.Random() * (b - a);

    /// <summary>Paints a whole piece one colour, so differently coloured pieces can be merged into a single mesh.</summary>
    private static THREE.BufferGeometry solid(THREE.BufferGeometry g, int color)
    {
        g.deleteAttribute("uv");
        var c = new THREE.Color(color);
        int n = g.attributes.position.count;
        var col = new float[n * 3];
        for (int i = 0; i < n; i++)
        {
            col[i * 3] = (float)c.r;
            col[i * 3 + 1] = (float)c.g;
            col[i * 3 + 2] = (float)c.b;
        }
        g.setAttribute("color", new THREE.BufferAttribute(col, 3));
        return g;
    }

    private static readonly THREE.MeshStandardMaterial vcol = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.6, side = THREE.Side.DoubleSide };

    public readonly THREE.Group group = new THREE.Group();
    public readonly List<Animal> animals = new List<Animal>();
    private readonly Func<double, double, double> floor;

    public SeaCreatures(Func<double, double, double> floor)
    {
        this.floor = floor;
        for (int i = 0; i < 3; i++) animals.Add(octopus());
        for (int i = 0; i < 8; i++) animals.Add(seahorse());
        for (int i = 0; i < 4; i++) animals.Add(snake());
        for (int i = 0; i < 14; i++) animals.Add(crab());
        for (int i = 0; i < 3; i++) animals.Add(turtle());
        foreach (var a in animals)
        {
            a.place();
            group.add(a.root);
        }
    }

    public void update(double dt, double t, THREE.Vector3 cam)
    {
        foreach (var a in animals)
        {
            if (!a.alive)
            {
                a.respawn -= dt;
                if (a.respawn <= 0)
                {
                    a.alive = true;
                    a.root.visible = true;
                    a.place();
                }
                continue;
            }
            a.tick(dt, t, cam);
        }
    }

    /// <summary>
    /// One more animal of a kind that is not part of the sea: nothing here moves or draws it. Whoever asks for it adds its
    /// `root` somewhere and calls its `tick` (the aquarium keeps a small one of each kind caught).
    /// </summary>
    public Animal spawnDetached(string kind)
    {
        Animal a = kind == "octopus" ? octopus() : kind == "seahorse" ? seahorse() : kind == "snake" ? snake() : kind == "turtle" ? turtle() : kind == "crab" ? crab() : null;
        a?.place();
        return a;
    }

    /// <summary>Called when the harpoon hits one.</summary>
    public void catchAnimal(Animal a)
    {
        a.alive = false;
        a.root.visible = false;
        a.respawn = a.gone * (0.8 + JS.Random() * 0.5);
    }

    private void spot(Animal a, double[] lift)
    {
        a.x = UW.x + (JS.Random() - 0.5) * UW.half * 1.6;
        a.z = UW.z + (JS.Random() - 0.3) * UW.half * 1.4;
        double f = floor(a.x, a.z);
        a.y = f + rnd(lift[0], lift[1]);
        a.heading = JS.Random() * Math.PI * 2;
    }

    /// <summary>Turns the animal back in when it nears the edge of the water.</summary>
    private void keepIn(Animal a, double dt)
    {
        double lx = a.x - UW.x;
        double lz = a.z - UW.z;
        if (Math.Abs(lx) > UW.half - 5 || Math.Abs(lz) > UW.half - 5)
        {
            double d = Math.Atan2(-lx, -lz) - a.heading;
            d = Math.Atan2(Math.Sin(d), Math.Cos(d));
            a.heading += d * Math.Min(1, dt * 2.5);
        }
    }

    /// <summary>Swings the heading away from the diver when they are close. Returns true while it is fleeing.</summary>
    private bool flee(Animal a, THREE.Vector3 cam, double dt, double near)
    {
        double dx = cam.x - a.x;
        double dz = cam.z - a.z;
        if (JS.Hypot(dx, dz, cam.y - a.y) > near) return false;
        double d = Math.Atan2(-dx, -dz) - a.heading;
        d = Math.Atan2(Math.Sin(d), Math.Cos(d));
        a.heading += d * Math.Min(1, dt * 5);
        return true;
    }

    // ---------------------------------------------------------------- octopus

    private Animal octopus()
    {
        var root = new THREE.Group();
        const int skin = 0xc4533a;
        const int pale = 0xeaa88e;
        var white = new THREE.MeshStandardMaterial { color = 0xf2ead6, roughness = 0.3 };
        var iris = new THREE.MeshStandardMaterial { color = 0xe0a02a, roughness = 0.3 };
        var dark = new THREE.MeshBasicMaterial { color = 0x14100e };

        // head and mantle are one smooth shape: darker on top, lighter underneath, with freckles
        var bodyParts = new List<THREE.BufferGeometry>();
        var head = new THREE.SphereGeometry(0.27, 22, 16);
        head.scale(1, 1.05, 1.1);
        head.translate(0, 0.46, 0.04);
        var mantle = new THREE.SphereGeometry(0.28, 22, 16);
        mantle.scale(0.95, 1.45, 1);
        mantle.rotateX(0.5);
        mantle.translate(0, 0.78, -0.16);
        foreach (var g in new[] { head, mantle })
        {
            g.deleteAttribute("uv");
            var p = g.attributes.position;
            var col = new float[p.count * 3];
            var c = new THREE.Color();
            for (int i = 0; i < p.count; i++)
            {
                double k = Math.Min(1, Math.Max(0, (p.getY(i) - 0.2) / 0.9));
                double fleck = Math.Sin(p.getX(i) * 38) * Math.Sin(p.getZ(i) * 34 + p.getY(i) * 20);
                c.set(0xd9785a).lerp(new THREE.Color(0x96402c), k).lerp(new THREE.Color(0xf0b49a), Math.Max(0, fleck - 0.55) * 1.2);
                col[i * 3] = (float)c.r;
                col[i * 3 + 1] = (float)c.g;
                col[i * 3 + 2] = (float)c.b;
            }
            g.setAttribute("color", new THREE.BufferAttribute(col, 3));
            bodyParts.Add(g);
        }
        // a bulge over each eye
        foreach (var sx in new[] { -1, 1 })
        {
            var brow = solid(new THREE.SphereGeometry(0.075, 10, 8), 0xb04a32);
            brow.scale(1.2, 0.8, 1);
            brow.translate(sx * 0.19, 0.56, 0.18);
            bodyParts.Add(brow);
        }
        var body = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(bodyParts, false), vcol);
        var model = new THREE.Group();
        model.add(body);
        foreach (var sx in new[] { -1, 1 })
        {
            var eye = new THREE.Mesh(new THREE.SphereGeometry(0.06, 12, 10), white);
            eye.position.set(sx * 0.2, 0.52, 0.2);
            var ir = new THREE.Mesh(new THREE.SphereGeometry(0.04, 10, 8), iris);
            ir.position.set(sx * 0.225, 0.52, 0.235);
            var pupil = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.012, 0.014), dark);
            pupil.position.set(sx * 0.235, 0.52, 0.265);
            pupil.rotation.y = sx * 0.6;
            model.add(eye, ir, pupil);
        }
        // the siphon
        var siphon = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.04, 0.12, 8), new THREE.MeshStandardMaterial { color = 0xb04a32, roughness = 0.6 });
        siphon.position.set(0.14, 0.36, 0.25);
        siphon.rotation.x = Math.PI / 2 - 0.3;
        model.add(siphon);

        // each arm is one long tapering curve that curls at the tip, with a row of little suckers underneath
        THREE.BufferGeometry armGeo(double len)
        {
            var path = new THREE.CatmullRomCurve3(new (double x, double y, double z)[]
            {
                (0, 0.02, 0.0), (0, -0.1, 0.2), (0, -0.17, 0.5), (0, -0.17, 0.78), (0, -0.11, 1.0), (0, -0.02, 1.12), (0, 0.07, 1.12), (0, 0.12, 1.04),
            }.Select(pt => new THREE.Vector3(pt.x, pt.y, pt.z * len)).ToList());
            var tube = new THREE.TubeGeometry(path, 40, 0.07, 8);
            var tp = tube.attributes.position;
            const int ring = 9;
            for (int i = 0; i < tp.count; i++)
            {
                double t = Math.Floor((double)i / ring) / 40;
                var c = path.getPoint(Math.Min(1, t));
                double k = Math.Max(0.12, 1 - t * 0.92);
                tp.setXYZ(i, c.x + (tp.getX(i) - c.x) * k, c.y + (tp.getY(i) - c.y) * k, c.z + (tp.getZ(i) - c.z) * k);
            }
            tube.computeVertexNormals();
            var parts = new List<THREE.BufferGeometry> { solid(tube, skin) };
            for (int i = 0; i < 12; i++)
            {
                double t = 0.12 + i * 0.065;
                var c = path.getPoint(t);
                var sk = solid(new THREE.SphereGeometry(0.03 * (1 - t * 0.7), 6, 5), pale);
                sk.translate(c.x, c.y - 0.07 * (1 - t * 0.9), c.z);
                parts.Add(sk);
            }
            return THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        }
        var arms = new List<(THREE.Group pivot, double ph, double ang)>();
        for (int i = 0; i < 8; i++)
        {
            double ang = i / 8.0 * Math.PI * 2 + 0.2;
            var pivot = new THREE.Group();
            pivot.position.set(Math.Sin(ang) * 0.17, 0.26, Math.Cos(ang) * 0.17);
            pivot.rotation.y = ang;
            pivot.add(new THREE.Mesh(armGeo(rnd(0.7, 0.95)), vcol));
            model.add(pivot);
            arms.Add((pivot, JS.Random() * 6, ang));
        }
        root.add(model);
        root.scale.setScalar(rnd(0.9, 1.2));
        double spread = 1;
        Animal a = null;
        a = new Animal
        {
            kind = "octopus", label = "octopus", reward = 5, r = 0.5, gone = 90, alive = true, respawn = 0, root = root,
            pts = new List<THREE.Vector3> { new THREE.Vector3(), new THREE.Vector3() }, x = 0, y = 0, z = 0, heading = 0,
            place = () => spot(a, new double[] { 0.15, 0.4 }),
            tick = (dt, t, cam) =>
            {
                bool fleeing = flee(a, cam, dt, 3.2);
                double speed = fleeing ? 2.4 : 0.35;
                a.heading += Math.Sin(t * 0.3 + a.x) * dt * 0.3;
                keepIn(a, dt);
                a.x += Math.Sin(a.heading) * speed * dt;
                a.z += Math.Cos(a.heading) * speed * dt;
                double f = floor(a.x, a.z);
                double want = f + (fleeing ? 1.4 : 0.1) + Math.Sin(t * 0.9 + a.z) * 0.08;
                a.y += (want - a.y) * Math.Min(1, dt * 2.2);
                root.position.set(a.x, a.y, a.z);
                root.rotation.y = a.heading;
                // the body pulses gently; when fleeing it tilts forward and the arms stream out behind
                double pulse = 1 + Math.Sin(t * (fleeing ? 7 : 1.3)) * (fleeing ? 0.06 : 0.025);
                body.scale.set(1 / Math.Sqrt(pulse), pulse, 1 / Math.Sqrt(pulse));
                model.rotation.x = fleeing ? 0.5 : Math.Sin(t * 1.1) * 0.03;
                spread += ((fleeing ? 0 : 1) - spread) * Math.Min(1, dt * 4);
                foreach (var arm in arms)
                {
                    // calm: arms fan out around the body; fleeing: they gather behind it
                    double yaw = Math.PI + (arm.ang - Math.PI) * (0.12 + 0.88 * spread);
                    arm.pivot.rotation.y = yaw + Math.Sin(t * 0.9 + arm.ph) * 0.12 * spread;
                    arm.pivot.rotation.x = -0.05 - (fleeing ? 0.5 : 0) + Math.Sin(t * (fleeing ? 7 : 1.7) + arm.ph) * 0.1;
                    arm.pivot.rotation.z = Math.Sin(t * 1.4 + arm.ph * 2) * 0.12 * spread;
                }
                a.pts[0].set(a.x, a.y + 0.6, a.z);
                a.pts[1].set(a.x, a.y + 0.15, a.z);
            },
        };
        return a;
    }

    // ---------------------------------------------------------------- seahorse

    private Animal seahorse()
    {
        var root = new THREE.Group();
        var curve = new THREE.CatmullRomCurve3(new (double x, double y, double z)[]
        {
            (0, 0.5, 0.07), (0, 0.4, 0.0), (0, 0.26, 0.05), (0, 0.12, 0.0), (0, 0.02, -0.07), (0, -0.06, -0.15), (0, -0.14, -0.12), (0, -0.17, -0.04), (0, -0.11, -0.01), (0, -0.07, -0.05),
        }.Select(pt => new THREE.Vector3(pt.x, pt.y, pt.z)).ToList());
        int top = JS.Random() < 0.5 ? 0xf2b630 : 0xf08a3a;
        var parts = new List<THREE.BufferGeometry>();
        var body = new THREE.TubeGeometry(curve, 32, 0.045, 8);
        // taper the tube: thick in the belly, thin toward the curled tail
        var bp = body.attributes.position;
        const int ring = 9;
        for (int i = 0; i < bp.count; i++)
        {
            double seg = Math.Floor((double)i / ring) / 32;
            var c = curve.getPoint(Math.Min(1, seg));
            double k = seg < 0.45 ? 1 + (0.45 - seg) * 0.3 : Math.Max(0.3, 1 - (seg - 0.45) * 1.25);
            bp.setXYZ(i, bp.getX(i), c.y + (bp.getY(i) - c.y) * k, c.z + (bp.getZ(i) - c.z) * k);
        }
        body.computeVertexNormals();
        parts.Add(solid(body, top));
        var head = solid(new THREE.SphereGeometry(0.06, 10, 8), top);
        head.translate(0, 0.51, 0.08);
        var snout = solid(new THREE.CylinderGeometry(0.014, 0.022, 0.13, 8), 0xe89a38);
        snout.rotateX(Math.PI / 2);
        snout.translate(0, 0.49, 0.2);
        var crest = solid(new THREE.ConeGeometry(0.02, 0.07, 6), 0xd9772a);
        crest.translate(0, 0.6, 0.04);
        parts.Add(head);
        parts.Add(snout);
        parts.Add(crest);
        foreach (var sx in new[] { -1, 1 })
        {
            var eye = solid(new THREE.SphereGeometry(0.014, 6, 5), 0x101418);
            eye.translate(sx * 0.045, 0.53, 0.1);
            parts.Add(eye);
        }
        for (int i = 0; i < 6; i++)
        {
            var ridge = solid(new THREE.SphereGeometry(0.03, 6, 5), 0xd9772a);
            var p = curve.getPoint(0.05 + i * 0.07);
            ridge.translate(0, p.y, p.z - 0.04);
            parts.Add(ridge);
        }
        var mesh = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(parts, false), vcol);
        // a thin dorsal fin that flutters
        var finShape = new THREE.Shape(new[] { new THREE.Vector2(0, 0), new THREE.Vector2(0.1, 0.03), new THREE.Vector2(0.1, 0.16), new THREE.Vector2(0, 0.14) });
        var fin = new THREE.Mesh(
            new THREE.ShapeGeometry(finShape),
            new THREE.MeshStandardMaterial { color = 0xf6d36a, transparent = true, opacity = 0.75, side = THREE.Side.DoubleSide, roughness = 0.4 });
        fin.position.set(0, 0.12, -0.06);
        fin.rotation.y = -Math.PI / 2;
        var model = new THREE.Group();
        model.add(mesh, fin);
        model.scale.setScalar(rnd(0.8, 1.15));
        root.add(model);
        Animal a = null;
        a = new Animal
        {
            kind = "seahorse", label = "seahorse", reward = 2, r = 0.3, gone = 50, alive = true, respawn = 0, root = root,
            pts = new List<THREE.Vector3> { new THREE.Vector3() }, x = 0, y = 0, z = 0, heading = 0,
            place = () => spot(a, new double[] { 0.8, 3.2 }),
            tick = (dt, t, cam) =>
            {
                bool fleeing = flee(a, cam, dt, 2.2);
                double speed = fleeing ? 0.9 : 0.18;
                a.heading += Math.Sin(t * 0.4 + a.z) * dt * 0.4;
                keepIn(a, dt);
                a.x += Math.Sin(a.heading) * speed * dt;
                a.z += Math.Cos(a.heading) * speed * dt;
                double f = floor(a.x, a.z);
                a.y = Math.Min(-1.2, Math.Max(f + 0.6, a.y + Math.Sin(t * 0.8 + a.x) * dt * 0.1));
                root.position.set(a.x, a.y + Math.Sin(t * 1.4 + a.z) * 0.06, a.z);
                root.rotation.y = a.heading;
                model.rotation.x = -0.1 + Math.Sin(t * 1.1 + a.x) * 0.06;
                fin.scale.set(1, 1 + Math.Sin(t * 22 + a.x) * 0.18, 1);
                a.pts[0].set(a.x, a.y + 0.25, a.z);
            },
        };
        return a;
    }

    // ---------------------------------------------------------------- sea snake

    private Animal snake()
    {
        var root = new THREE.Group();
        const int n = 22;
        var geo = new THREE.SphereGeometry(1, 10, 8);
        var bandA = new THREE.MeshStandardMaterial { color = 0xcfe0d8, roughness = 0.45 };
        var bandB = new THREE.MeshStandardMaterial { color = 0x1c3238, roughness = 0.45 };
        var segs = new List<THREE.Mesh>();
        for (int i = 0; i < n; i++)
        {
            var m = new THREE.Mesh(geo, (i / 2) % 2 != 0 ? bandB : bandA);
            double r = i == 0 ? 0.075 : 0.07 * (1 - (double)i / n * 0.65);
            m.scale.set(r, r * (i == 0 ? 0.75 : 1.1), r * 1.5);
            segs.Add(m);
            root.add(m);
        }
        foreach (var sx in new[] { -1, 1 })
        {
            var eye = new THREE.Mesh(new THREE.SphereGeometry(0.014, 6, 5), new THREE.MeshBasicMaterial { color = 0xfafafa });
            eye.position.set(sx * 0.05, 0.03, 0.06);
            segs[0].add(eye);
            eye.scale.set(1 / 0.075, 1 / 0.056, 1 / 0.1125);
        }
        var tmpFwd = new THREE.Vector3();
        Animal a = null;
        a = new Animal
        {
            kind = "snake", label = "sea snake", reward = 3, r = 0.35, gone = 60, alive = true, respawn = 0, root = root,
            pts = new List<THREE.Vector3> { new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3() }, x = 0, y = 0, z = 0, heading = 0,
            place = () => spot(a, new double[] { 0.5, 3.5 }),
            tick = (dt, t, cam) =>
            {
                bool fleeing = flee(a, cam, dt, 3.5);
                double speed = fleeing ? 2.6 : 0.8;
                a.heading += Math.Sin(t * 0.35 + a.x * 0.3) * dt * 0.5;
                keepIn(a, dt);
                a.x += Math.Sin(a.heading) * speed * dt;
                a.z += Math.Cos(a.heading) * speed * dt;
                double f = floor(a.x, a.z);
                a.y = Math.Min(-1.2, Math.Max(f + 0.4, a.y + Math.Sin(t * 0.5 + a.z * 0.2) * dt * 0.4));
                double fx = Math.Sin(a.heading);
                double fz = Math.Cos(a.heading);
                tmpFwd.set(fx, 0, fz);
                double wave = fleeing ? 9 : 4.5;
                for (int i = 0; i < n; i++)
                {
                    double along = i * 0.13;
                    double side = Math.Sin(t * wave - i * 0.5) * 0.11 * (0.4 + (double)i / n);
                    segs[i].position.set(a.x - fx * along + fz * side, a.y + Math.Sin(t * 2 + i * 0.3) * 0.02, a.z - fz * along - fx * side);
                    // each piece faces roughly along the body
                    segs[i].rotation.y = a.heading + Math.Cos(t * wave - i * 0.5) * 0.45;
                }
                a.pts[0].copy(segs[0].position);
                a.pts[1].copy(segs[n / 2].position);
                a.pts[2].copy(segs[n - 1].position);
            },
        };
        return a;
    }

    // ---------------------------------------------------------------- sea turtle

    /// <summary>Gentle and slow, and not something to spear: the harpoon passes by.</summary>
    private Animal turtle()
    {
        var root = new THREE.Group();
        var parts = new List<THREE.BufferGeometry>();
        // the shell: a flattened dome with plates of two greens
        var shell = new THREE.SphereGeometry(0.5, 28, 18);
        shell.scale(1, 0.42, 1.25);
        var sp = shell.attributes.position;
        var col = new float[sp.count * 3];
        var c = new THREE.Color();
        for (int i = 0; i < sp.count; i++)
        {
            double x = sp.getX(i);
            double y = sp.getY(i);
            double z = sp.getZ(i);
            if (y < -0.02) c.set(0xe3d9a8);
            else
            {
                double plate = Math.Sin(x * 15 + Math.Sin(z * 9) * 1.3) * Math.Sin(z * 12 + Math.Sin(x * 7) * 1.1);
                c.set(plate > 0.1 ? 0x5a7a3a : 0x3f5a2a).lerp(new THREE.Color(0x8a6a3a), Math.Max(0, -plate) * 0.4);
            }
            col[i * 3] = (float)c.r;
            col[i * 3 + 1] = (float)c.g;
            col[i * 3 + 2] = (float)c.b;
        }
        shell.deleteAttribute("uv");
        shell.setAttribute("color", new THREE.BufferAttribute(col, 3));
        parts.Add(shell);
        const int skin = 0x8fa860;
        var head = solid(new THREE.SphereGeometry(0.14, 12, 9), skin);
        head.scale(1, 0.85, 1.2);
        head.translate(0, 0.0, 0.72);
        var neck = solid(new THREE.CylinderGeometry(0.09, 0.11, 0.25, 8), skin);
        neck.rotateX(Math.PI / 2);
        neck.translate(0, 0, 0.58);
        var beak = solid(new THREE.SphereGeometry(0.05, 8, 6), 0x6e8248);
        beak.translate(0, -0.03, 0.85);
        var tail = solid(new THREE.ConeGeometry(0.05, 0.22, 6), skin);
        tail.rotateX(-Math.PI / 2);
        tail.translate(0, -0.02, -0.7);
        parts.Add(head);
        parts.Add(neck);
        parts.Add(beak);
        parts.Add(tail);
        foreach (var sx in new[] { -1, 1 })
        {
            var eye = solid(new THREE.SphereGeometry(0.025, 6, 5), 0x101418);
            eye.translate(sx * 0.1, 0.04, 0.8);
            parts.Add(eye);
        }
        var body = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(parts, false), vcol);
        // the flippers are separate, so they can beat
        THREE.BufferGeometry flip(double len, double w)
        {
            var g = solid(new THREE.SphereGeometry(1, 10, 8), skin);
            g.scale(len, 0.035, w);
            g.translate(len * 0.9, 0, 0);
            return g;
        }
        var flippers = new List<(THREE.Mesh m, int side, bool front)>();
        foreach (var side in new[] { -1, 1 })
        {
            foreach (var front in new[] { true, false })
            {
                var m = new THREE.Mesh(front ? flip(0.38, 0.13) : flip(0.2, 0.09), vcol);
                m.position.set(side * (front ? 0.36 : 0.28), -0.07, front ? 0.34 : -0.45);
                if (side < 0) m.scale.x = -1;
                flippers.Add((m, side, front));
                body.add(m);
            }
        }
        var model = new THREE.Group();
        model.add(body);
        model.scale.setScalar(rnd(1.0, 1.5));
        root.add(model);
        Animal a = null;
        a = new Animal
        {
            kind = "turtle", label = "turtle", reward = 0, r = 0, gone = 1, alive = true, respawn = 0, root = root,
            pts = new List<THREE.Vector3>(), x = 0, y = 0, z = 0, heading = 0,
            place = () => spot(a, new double[] { 2.2, 6 }),
            tick = (dt, t, _cam) =>
            {
                a.heading += Math.Sin(t * 0.18 + a.x * 0.1) * dt * 0.25;
                keepIn(a, dt);
                const double speed = 0.75;
                a.x += Math.Sin(a.heading) * speed * dt;
                a.z += Math.Cos(a.heading) * speed * dt;
                double f = floor(a.x, a.z);
                a.y = Math.Min(-2.2, Math.Max(f + 1.4, a.y + Math.Sin(t * 0.25 + a.z * 0.1) * dt * 0.4));
                root.position.set(a.x, a.y + Math.Sin(t * 0.8 + a.z) * 0.08, a.z);
                root.rotation.y = a.heading;
                model.rotation.z = Math.Sin(t * 0.8) * 0.05;
                model.rotation.x = Math.Sin(t * 0.5 + a.x) * 0.04;
                foreach (var fl in flippers)
                {
                    double beat = Math.Sin(t * 1.5 + (fl.front ? 0 : 1.2) + (fl.side > 0 ? 0 : 0.0));
                    fl.m.rotation.z = fl.side * (fl.front ? beat * 0.55 : beat * 0.3) * (fl.side > 0 ? -1 : -1) * -1;
                }
            },
        };
        return a;
    }

    // ---------------------------------------------------------------- crab

    private Animal crab()
    {
        var root = new THREE.Group();
        int shell = new[] { 0xd4552e, 0xc2452c, 0xe0703a }[(int)Math.Floor(JS.Random() * 3)];
        var parts = new List<THREE.BufferGeometry>();
        var body = solid(new THREE.SphereGeometry(0.2, 14, 10), shell);
        body.scale(1.25, 0.55, 0.95);
        body.translate(0, 0.14, 0);
        parts.Add(body);
        foreach (var sx in new[] { -1, 1 })
        {
            // eye stalks, legs and a claw on each side
            var stalk = solid(new THREE.CylinderGeometry(0.01, 0.01, 0.07, 5), 0xe8a070);
            stalk.translate(sx * 0.07, 0.25, 0.15);
            var eye = solid(new THREE.SphereGeometry(0.02, 6, 5), 0x101418);
            eye.translate(sx * 0.07, 0.29, 0.16);
            parts.Add(stalk);
            parts.Add(eye);
            for (int i = 0; i < 3; i++)
            {
                var leg = solid(new THREE.CylinderGeometry(0.012, 0.01, 0.26, 5), 0xb8421f);
                leg.rotateZ(sx * 1.0);
                leg.translate(sx * 0.3, 0.1, -0.06 + i * 0.09);
                var foot = solid(new THREE.CylinderGeometry(0.01, 0.006, 0.12, 5), 0xb8421f);
                foot.rotateZ(sx * 0.2);
                foot.translate(sx * 0.42, 0.02, -0.06 + i * 0.09);
                parts.Add(leg);
                parts.Add(foot);
            }
            var arm = solid(new THREE.CylinderGeometry(0.02, 0.025, 0.2, 6), 0xc2452c);
            arm.rotateX(Math.PI / 2 - 0.2);
            arm.translate(sx * 0.2, 0.15, 0.2);
            var claw = solid(new THREE.SphereGeometry(0.07, 8, 6), shell);
            claw.scale(1, 0.7, 1.35);
            claw.translate(sx * 0.2, 0.15, 0.36);
            var pincer = solid(new THREE.ConeGeometry(0.03, 0.1, 6), 0xf0d0b0);
            pincer.rotateX(Math.PI / 2);
            pincer.translate(sx * 0.2, 0.15, 0.46);
            parts.Add(arm);
            parts.Add(claw);
            parts.Add(pincer);
        }
        var mesh = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(parts, false), vcol);
        mesh.castShadow = false;
        root.add(mesh);
        root.scale.setScalar(rnd(0.8, 1.25));
        int dir = 1;
        double pause = 0;
        Animal a = null;
        a = new Animal
        {
            kind = "crab", label = "crab", reward = 1, r = 0.35, gone = 35, alive = true, respawn = 0, root = root,
            pts = new List<THREE.Vector3> { new THREE.Vector3() }, x = 0, y = 0, z = 0, heading = 0,
            place = () =>
            {
                spot(a, new double[] { 0, 0 });
                a.y = floor(a.x, a.z);
            },
            tick = (dt, t, cam) =>
            {
                bool fleeing = flee(a, cam, dt, 2.6);
                pause -= dt;
                if (pause <= 0 && !fleeing && JS.Random() < dt * 0.6)
                {
                    pause = rnd(0.6, 2.2);
                    dir = JS.Random() < 0.5 ? -1 : 1;
                    a.heading += rnd(-0.6, 0.6);
                }
                keepIn(a, dt);
                double speed = fleeing ? 1.9 : pause > 0 ? 0.55 : 0;
                // a crab walks sideways
                double sx = Math.Cos(a.heading) * dir;
                double sz = -Math.Sin(a.heading) * dir;
                a.x += sx * speed * dt;
                a.z += sz * speed * dt;
                a.y = floor(a.x, a.z) + 0.01;
                root.position.set(a.x, a.y, a.z);
                root.rotation.y = a.heading;
                mesh.rotation.z = speed > 0 ? Math.Sin(t * 26) * 0.05 : 0;
                a.pts[0].set(a.x, a.y + 0.15, a.z);
            },
        };
        return a;
    }
}
