using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/SeaLife.ts

public class Wanderer
{
    public double x, z, heading, speed, phase;
}

public class Shark : Wanderer
{
    public THREE.Group root;
    public THREE.Mesh body;
    /// <summary>untouched vertex positions, which the swimming wave is applied to</summary>
    public float[] rest;
    public THREE.Group caudal;
    public THREE.Mesh hit;
    public bool alive;
    public double respawn;
    public double wake;
    /// <summary>0 while cruising; 0..1 through a leap out of the water</summary>
    public double breach;
    public THREE.Vector2 breachFrom;
    /// <summary>hits it can still take; two are needed to bring a shark down</summary>
    public int hp;
    /// <summary>seconds left of darting away after being hit</summary>
    public double flinch;
    public double baseSpeed;
}

public class SeaHit
{
    public string kind;
    public bool killed;
    public THREE.Vector3 at;
}

/// <summary>Sharks cutting through the water and schools of fish, some of which leap out of the sea.</summary>
public class SeaLife
{
    private static double rand(double a, double b) => a + JS.Random() * (b - a);

    private const double SHARK_LEN = 4.4;

    // a shark's leap: how long it lasts, how fast it travels, how far below the surface it starts and how high it climbs
    private const double BREACH_SECONDS = 2.4;
    private const double BREACH_SPEED = 6.5;
    private const double BREACH_DEPTH = 1.8;
    private const double BREACH_PEAK = 2.7;
    /// <summary>the point in the leap, as a fraction of it, where the nose breaks the surface</summary>
    private static readonly double BREACH_OUT = (1 - Math.Sqrt(1 - BREACH_DEPTH / (BREACH_PEAK + BREACH_DEPTH))) / 2;

    // swimming area: out beyond the surf, in front of the beach
    private static readonly double X_MAX = SEA.x;
    private const double Z_MIN = SHORE_Z + 10;
    private static readonly double Z_MAX = SEA.z;

    private class School : Wanderer
    {
        public class Fish
        {
            public THREE.Group mesh;
            public THREE.Mesh tail;
            public double ox, oz, ph;
            public THREE.Mesh hit;
            public bool alive;
            public double respawn;
        }

        public List<Fish> fish;
    }

    private class Jumper
    {
        public THREE.Group mesh;
        public bool active;
        public double t, sx, sz, dx, dz, yaw;
    }

    public readonly THREE.Group group = new THREE.Group();
    public readonly List<Shark> sharks = new List<Shark>();
    private readonly List<School> schools = new List<School>();
    private readonly List<Jumper> jumpers = new List<Jumper>();
    private readonly ParticleSystem splashes = new ParticleSystem(260, 0.14, 7, false);
    private readonly ParticleSystem wakeParticles = new ParticleSystem(300, 0.13, 0, false);
    private double jumpTimer = 3;
    private readonly THREE.Color white = new THREE.Color(0xeaf7ff);
    /// <summary>the shark's body with its fins grown on (they bend with it as it swims), and its tail fin</summary>
    private readonly THREE.BufferGeometry sharkBody;
    private readonly THREE.BufferGeometry caudal;
    /// <summary>the shark's half width and half height along it, 0 at the nose and 1 at the tail</summary>
    private readonly Func<double, double> sharkW, sharkH;
    /// <summary>the fish of the open sea: the same kinds as bite at the pier, a body and a tail that wags for each</summary>
    private readonly List<(THREE.BufferGeometry body, THREE.BufferGeometry tail)> fishKinds = new List<(THREE.BufferGeometry, THREE.BufferGeometry)>();
    private readonly THREE.MeshStandardMaterial fishMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.38, metalness = 0.18, side = THREE.Side.DoubleSide };
    private double breachTimer = 40;

    public SeaLife()
    {
        sharkW = SeaModels.profile(new[]
        {
            (0.0, 0.0), (0.015, 0.09), (0.05, 0.18), (0.1, 0.26), (0.2, 0.37), (0.34, 0.46), (0.46, 0.46), (0.6, 0.37),
            (0.72, 0.26), (0.82, 0.165), (0.9, 0.11), (0.95, 0.095), (1.0, 0.09),
        });
        sharkH = SeaModels.profile(new[]
        {
            (0.0, 0.0), (0.015, 0.075), (0.05, 0.15), (0.1, 0.225), (0.2, 0.33), (0.34, 0.425), (0.46, 0.435), (0.6, 0.365),
            (0.72, 0.265), (0.82, 0.17), (0.9, 0.105), (0.95, 0.078), (1.0, 0.068),
        });
        sharkBody = buildSharkBody();
        caudal = buildCaudal();
        foreach (var k in Catches.ROD) fishKinds.Add(Underwater.fishModel(k.top, k.belly, k.accent, k.pattern));
        // a bigger sea, so a few more of everything
        // the sea is wide: enough of them that some are always in sight
        for (int i = 0; i < 4; i++) sharks.Add(makeShark());
        for (int i = 0; i < 22; i++) schools.Add(makeSchool());
        for (int i = 0; i < 6; i++) jumpers.Add(makeJumper(i));
        group.add(splashes.points, wakeParticles.points);
    }

    private const int SHARK_TOP = 0x5a6d7e, SHARK_BELLY = 0xeef2f3;

    /// <summary>z along the shark at a point along it, 0 at the nose and 1 at the tail</summary>
    private static double zAlong(double u) => -SHARK_LEN / 2 + u * SHARK_LEN;

    /// <summary>
    /// The shark's body: a torpedo with a rounded snout, a deep chest and a slim tail stalk, and the fins that stand on it (the tall
    /// first dorsal, the second dorsal, the long pectorals, the pelvics and the anal fin) all part of one shape, grey on top and
    /// white below.
    /// </summary>
    private THREE.BufferGeometry buildSharkBody()
    {
        // the body is grey above and white below; the fins are grey all over, except under the pectorals
        THREE.BufferGeometry grey(THREE.BufferGeometry g) => SeaModels.countershade(g, SHARK_TOP, SHARK_TOP);
        var parts = new List<THREE.BufferGeometry> { SeaModels.countershade(SeaModels.body(SHARK_LEN, sharkW, sharkH), SHARK_TOP, SHARK_BELLY, -0.12, 0.42) };
        // the tall, swept first dorsal, standing on the back over the middle of the body
        var dorsal = SeaModels.fin(1.0, zAlong(0.35), zAlong(0.52), zAlong(0.6), 0.1, 1.5, 0.45);
        dorsal.translate(0, sharkH(0.43) * 0.96, 0);
        parts.Add(grey(dorsal));
        // the small second dorsal and the anal fin under it, near the tail
        var dorsal2 = SeaModels.fin(0.2, zAlong(0.735), zAlong(0.79), zAlong(0.81), 0.05, 1.3, 0.5);
        dorsal2.translate(0, sharkH(0.76) * 0.95, 0);
        parts.Add(grey(dorsal2));
        var anal = SeaModels.fin(0.17, zAlong(0.77), zAlong(0.82), zAlong(0.835), 0.045, 1.3, 0.5);
        anal.rotateZ(Math.PI);
        anal.translate(0, -sharkH(0.795) * 0.8, 0);
        parts.Add(grey(anal));
        foreach (var side in new[] { -1, 1 })
        {
            // the long pectorals: low on the flanks behind the gills, reaching out, down and back
            var pec = SeaModels.fin(0.95, zAlong(0.235), zAlong(0.34), zAlong(0.46), 0.085, 1.35, 0.5);
            pec.rotateZ(-side * (Math.PI / 2 + 0.42));
            pec.translate(side * sharkW(0.28) * 0.72, -sharkH(0.28) * 0.5, 0);
            parts.Add(SeaModels.countershade(pec, SHARK_TOP, 0xdfe6ea, -0.35, 0.3));
            // the small pelvic fins under the belly
            var pel = SeaModels.fin(0.26, zAlong(0.6), zAlong(0.66), zAlong(0.7), 0.045, 1.3, 0.5);
            pel.rotateZ(-side * (Math.PI / 2 + 0.75));
            pel.translate(side * sharkW(0.63) * 0.45, -sharkH(0.63) * 0.62, 0);
            parts.Add(grey(pel));
        }
        var g = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>The tail fin, in its own space at the end of the tail stalk: a tall, swept upper lobe and a shorter lower one, like a crescent.</summary>
    private THREE.BufferGeometry buildCaudal()
    {
        var upper = SeaModels.fin(1.15, -0.32, 0.02, 0.82, 0.07, 1.2, 0.5);
        var lower = SeaModels.fin(0.68, -0.24, 0.02, 0.5, 0.06, 1.2, 0.5);
        lower.rotateZ(Math.PI);
        var g = THREE.BufferGeometryUtils.mergeGeometries(new List<THREE.BufferGeometry> { upper, lower }, false);
        return SeaModels.countershade(g, SHARK_TOP, SHARK_TOP);
    }

    /// <summary>Half the body's width and height (in model units) at a point along it, 0 at the nose and 1 at the tail.</summary>
    private (double w, double h) halfSize(double u) => (sharkW(u), sharkH(u));

    private Shark makeShark()
    {
        var root = new THREE.Group();
        var bodyMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.42, metalness = 0.05 };
        var bodyGeo = sharkBody.clone();
        var body = new THREE.Mesh(bodyGeo, bodyMat);
        body.castShadow = true;
        var rest = (float[])bodyGeo.attributes.position.array.Clone();

        var caudal = new THREE.Group();
        var lobe = new THREE.Mesh(this.caudal, bodyMat);
        lobe.castShadow = true;
        caudal.add(lobe);
        caudal.position.set(0, 0.0, SHARK_LEN / 2 - 0.1);

        var dark = new THREE.MeshStandardMaterial { color = 0x14181c, roughness = 0.3 };
        var glossy = new THREE.MeshStandardMaterial { color = 0x07090b, roughness = 0.08, metalness = 0.4 };
        var whiteMat = new THREE.MeshStandardMaterial { color = 0xf5f3ec, roughness = 0.35 };
        var gum = new THREE.MeshStandardMaterial { color = 0x4a1418, roughness = 0.6 };
        double zAt(double u) => -SHARK_LEN / 2 + u * SHARK_LEN;
        foreach (var side in new[] { -1, 1 })
        {
            // an eye sitting on the side of the head: a dark socket, a glossy black eye and a speck of light
            const double ue = 0.115;
            var hs = halfSize(ue);
            const double ey = 0.07;
            double ex = hs.w * Math.Sqrt(Math.Max(0.05, 1 - Math.Pow(ey / hs.h, 2)));
            var socket = new THREE.Mesh(new THREE.SphereGeometry(0.075, 12, 8), dark);
            socket.scale.set(0.55, 0.95, 1.1);
            socket.position.set(side * (ex - 0.005), ey, zAt(ue));
            var eye = new THREE.Mesh(new THREE.SphereGeometry(0.06, 14, 10), glossy);
            eye.position.set(side * (ex + 0.012), ey, zAt(ue) - 0.005);
            var glint = new THREE.Mesh(new THREE.SphereGeometry(0.014, 6, 5), new THREE.MeshBasicMaterial { color = 0xffffff });
            glint.position.set(side * (ex + 0.05), ey + 0.028, zAt(ue) - 0.03);
            root.add(socket, eye, glint);
            // five curved gill slits behind the head
            for (int i = 0; i < 5; i++)
            {
                double ug = 0.255 + i * 0.026;
                var hg = halfSize(ug);
                // short slits high on the flank, curving with the body
                var slit = new THREE.Mesh(new THREE.BoxGeometry(0.012, hg.h * 0.4, 0.016), dark);
                slit.position.set(side * (hg.w * 0.985 - 0.004), hg.h * 0.12, zAt(ug));
                slit.rotation.z = side * (-0.18 + 0.02 * i);
                slit.rotation.y = side * 0.0;
                root.add(slit);
            }
            // a nostril pit on the snout
            var nostril = new THREE.Mesh(new THREE.SphereGeometry(0.018, 6, 5), dark);
            const double un = 0.045;
            var hn = halfSize(un);
            nostril.position.set(side * hn.w * 0.8, 0.03, zAt(un));
            root.add(nostril);
        }
        // the mouth: a wide open grin on the underside of the snout, a dark gape with a row of teeth along each jaw
        double jaw = -sharkH(0.08) * 0.84 + 0.005;
        var gape = new THREE.Mesh(new THREE.CircleGeometry(0.2, 20, 0, Math.PI), gum);
        gape.rotation.x = Math.PI / 2;
        gape.rotation.z = Math.PI;
        gape.scale.set(1, 1.35, 1);
        gape.position.set(0, jaw, -1.86);
        root.add(gape);
        void teeth(int rows, double y, double len)
        {
            for (int i = 0; i < 15; i++)
            {
                double th = -1.15 + i / 14.0 * 2.3;
                var t = new THREE.Mesh(new THREE.ConeGeometry(0.014, len, 5), whiteMat);
                double x = Math.Sin(th) * 0.21;
                double z = -1.86 + (1 - Math.Cos(th)) * 0.32 - 0.06 + rows * 0.015;
                t.position.set(x, y + (rows == 1 ? -len / 2 : len / 2), z);
                if (rows == 1) t.rotation.x = Math.PI;
                t.rotation.z = -Math.Sin(th) * 0.35;
                root.add(t);
            }
        }
        teeth(1, jaw + 0.005, 0.07);
        teeth(-1, jaw - 0.02, 0.045);

        var hit = new THREE.Mesh(new THREE.SphereGeometry(1, 8, 6), new THREE.MeshBasicMaterial { visible = false });
        hit.position.set(0, 0.4, 0);
        hit.userData["hitRadius"] = 2.2;
        root.add(body, caudal, hit);
        root.scale.setScalar(1.3);
        group.add(root);
        var s = new Shark
        {
            root = root, body = body, rest = rest, caudal = caudal, hit = hit, alive = true, respawn = 0, wake = 0, breach = 0,
            breachFrom = new THREE.Vector2(), hp = 2, flinch = 0, baseSpeed = 3,
            x = rand(-X_MAX, X_MAX), z = rand(Z_MIN + 10, Z_MAX), heading = JS.Random() * 6.28, speed = rand(2.4, 3.6), phase = JS.Random() * 6,
        };
        s.baseSpeed = s.speed;
        hit.userData["shark"] = s;
        return s;
    }

    /// <summary>
    /// A fish of the open sea, nose toward -z as the schools swim. The model is the underwater fish (fins, eyes and markings), in
    /// the colours of one of the kinds that bite at the pier.
    /// </summary>
    private (THREE.Group mesh, THREE.Mesh tail) makeFish(int kind, double size)
    {
        var (bodyGeo, tailGeo) = fishKinds[kind % fishKinds.Count];
        var g = new THREE.Group();
        var model = new THREE.Group();
        model.rotation.y = Math.PI;
        model.add(new THREE.Mesh(bodyGeo, fishMat));
        var tail = new THREE.Mesh(tailGeo, fishMat);
        tail.position.z = -0.42;
        model.add(tail);
        model.scale.setScalar(size);
        g.add(model);
        return (g, tail);
    }

    private School makeSchool()
    {
        // one kind of fish to a school, mostly the common ones
        int kind = JS.Random() < 0.7 ? (int)Math.Floor(JS.Random() * 2) : 2 + (int)Math.Floor(JS.Random() * (fishKinds.Count - 2));
        var fish = new List<School.Fish>();
        int n = 8 + (int)Math.Floor(JS.Random() * 5);
        for (int i = 0; i < n; i++)
        {
            var (g, tail) = makeFish(kind, rand(0.55, 0.8));
            var hit = new THREE.Mesh(new THREE.SphereGeometry(1, 6, 4), new THREE.MeshBasicMaterial { visible = false });
            hit.scale.setScalar(0.28);
            hit.userData["hitRadius"] = 0.55;
            g.add(hit);
            group.add(g);
            var f = new School.Fish { mesh = g, tail = tail, ox = rand(-1.6, 1.6), oz = rand(-1.6, 1.6), ph = JS.Random() * 6, hit = hit, alive = true, respawn = 0 };
            hit.userData["fish"] = f;
            fish.Add(f);
        }
        return new School
        {
            x = rand(-X_MAX, X_MAX), z = rand(SHORE_Z + 6, Z_MAX - 20), heading = JS.Random() * 6.28, speed = rand(1.2, 2.2), phase = JS.Random() * 6, fish = fish,
        };
    }

    private Jumper makeJumper(int i)
    {
        var (g, _) = makeFish(i, 1.05);
        g.visible = false;
        group.add(g);
        return new Jumper { mesh = g, active = false, t = 0, sx = 0, sz = 0, dx = 0, dz = 0, yaw = 0 };
    }

    /// <summary>Gently wandering steering that also turns the swimmer back from the edges of its patch.</summary>
    private void steer(Wanderer w, double dt, double t, double turnRate)
    {
        w.heading += Math.Sin(t * 0.35 + w.phase) * turnRate * dt;
        double tx = 0;
        double tz = 0;
        // the sea is narrower close to the land
        double xMax = seaHalfWidth(w.z);
        if (w.x > xMax) tx = -1;
        else if (w.x < -xMax) tx = 1;
        if (w.z > Z_MAX) tz = -1;
        else if (w.z < Z_MIN) tz = 1;
        if (tx != 0 || tz != 0)
        {
            double want = Math.Atan2(-tx, -tz);
            double d = want - w.heading;
            d = Math.Atan2(Math.Sin(d), Math.Cos(d));
            w.heading += d * Math.Min(1, dt * 1.4);
        }
        w.x += -Math.Sin(w.heading) * w.speed * dt;
        w.z += -Math.Cos(w.heading) * w.speed * dt;
    }

    /// <summary>Hit spheres of the sharks and fish that are currently in the water.</summary>
    public List<THREE.Object3D> targets()
    {
        var o = sharks.Where(s => s.alive).Select(s => (THREE.Object3D)s.hit).ToList();
        foreach (var sc in schools) foreach (var f in sc.fish) if (f.alive && f.mesh.visible) o.Add(f.hit);
        return o;
    }

    /// <summary>
    /// Registers a hit on a shark or a fish. A fish is caught at once. A shark takes two hits:
    /// the first makes it bolt, the second sends it diving away for good measure.
    /// </summary>
    public SeaHit hit(THREE.Object3D obj)
    {
        var s = obj.userData.Get<Shark>("shark");
        if (s != null)
        {
            if (!s.alive) return null;
            var at = new THREE.Vector3(s.x, s.root.position.y, s.z);
            s.hp--;
            if (s.hp > 0)
            {
                s.flinch = 3;
                s.speed = s.baseSpeed * 2.4;
                splash(at, 26);
                return new SeaHit { kind = "shark", killed = false, at = at };
            }
            s.alive = false;
            s.breach = 0;
            s.respawn = rand(30, 50);
            s.hp = 2;
            s.flinch = 0;
            s.speed = s.baseSpeed;
            s.root.visible = false;
            splash(at, 50);
            return new SeaHit { kind = "shark", killed = true, at = at };
        }
        var f = obj.userData.Get<School.Fish>("fish");
        if (f != null && f.alive)
        {
            f.alive = false;
            f.respawn = rand(18, 30);
            f.mesh.visible = false;
            var at = f.mesh.getWorldPosition(new THREE.Vector3());
            splash(at, 12);
            return new SeaHit { kind = "fish", killed = true, at = at };
        }
        return null;
    }

    public void splash(THREE.Vector3 at, int count = 22)
    {
        splashes.burst(at, count, 1.6, 4.2, 1.1, white);
    }

    /// <summary>
    /// For the -autoshot-sealife test: holds the first shark at the top of a leap, the second cruising and a fish in the air, all at
    /// fixed spots, so they can be photographed. Null in the game.
    /// </summary>
    internal (double x, double z)? smokeHold;

    /// <summary>`level` is the current surface height; onSplash(distance) lets the sound be placed.</summary>
    public void update(double dt, double t, double level, double px, double pz, Action<double> onSplash)
    {
        if (smokeHold != null)
        {
            breachTimer = 1e9;
            jumpTimer = 1e9;
            var (hx, hz) = smokeHold.Value;
            for (int i = 0; i < sharks.Count; i++)
            {
                var sh = sharks[i];
                sh.alive = true;
                sh.root.visible = true;
                sh.heading = Math.PI / 2;
                sh.x = hx + (i == 0 ? 0 : 3);
                sh.z = hz + (i == 0 ? 0 : 7);
                sh.breach = i == 0 ? 0.5 : 0;
                sh.breachFrom.set(sh.x, sh.z);
            }
            var j0 = jumpers[0];
            j0.active = true;
            j0.mesh.visible = true;
            j0.t = 0.42;
            j0.sx = hx - 2.2;
            j0.sz = hz - 2.5;
            j0.yaw = Math.PI / 2;
            j0.dx = 0;
            j0.dz = 0;
            var sc0 = schools[0];
            sc0.x = hx - 1;
            sc0.z = hz - 4.5;
            sc0.heading = Math.PI / 2;
            sc0.speed = 0;
            dt = 0.0001;
        }
        // now and then one of the sharks launches itself out of the water
        breachTimer -= dt;
        if (breachTimer <= 0)
        {
            breachTimer = rand(35, 80);
            var ready = sharks.Where(x => x.alive && x.breach == 0).ToList();
            var pick = ready.Count > 0 ? ready[(int)Math.Floor(JS.Random() * ready.Count)] : null;
            if (pick != null)
            {
                pick.breach = 0.0001;
                pick.breachFrom.set(pick.x, pick.z);
                // always out to sea, never at the beach or off the edge of the water
                if (pick.z < Z_MIN + 22) pick.heading = Math.PI + (JS.Random() - 0.5) * 0.6;
                else if (Math.Abs(pick.x) > X_MAX - 18) pick.heading = Math.Atan2(pick.x, 0) + Math.PI * 0.5;
                splash(new THREE.Vector3(pick.x, level, pick.z), 45);
                onSplash(JS.Hypot(pick.x - px, pick.z - pz) * 0.5);
            }
        }

        foreach (var s in sharks)
        {
            bool leapDone = false;
            if (!s.alive)
            {
                s.respawn -= dt;
                if (s.respawn <= 0)
                {
                    s.alive = true;
                    s.root.visible = true;
                    s.x = rand(-X_MAX, X_MAX);
                    s.z = rand(Z_MIN + 10, Z_MAX);
                }
                continue;
            }
            if (s.flinch > 0)
            {
                s.flinch -= dt;
                if (s.flinch <= 0) s.speed = s.baseSpeed;
            }
            bool leaping = s.breach > 0;
            double height = 0;
            double pitch = 0;
            if (leaping)
            {
                // a real leap: it comes up from below the surface at a slant, arcs over and goes back in headfirst
                double prev = s.breach;
                if (smokeHold == null) s.breach += dt / BREACH_SECONDS;
                double u0 = Math.Min(1, s.breach);
                s.x += -Math.Sin(s.heading) * BREACH_SPEED * dt;
                s.z += -Math.Cos(s.heading) * BREACH_SPEED * dt;
                height = -BREACH_DEPTH + 4 * (BREACH_PEAK + BREACH_DEPTH) * u0 * (1 - u0);
                double slope = 4 * (BREACH_PEAK + BREACH_DEPTH) * (1 - 2 * u0) / (BREACH_SPEED * BREACH_SECONDS);
                pitch = Math.Atan(slope);
                var at = new THREE.Vector3(s.x - Math.Sin(s.heading) * 1.8, level, s.z - Math.Cos(s.heading) * 1.8);
                if (prev < BREACH_OUT && s.breach >= BREACH_OUT)
                {
                    splash(at, 60);
                    onSplash(JS.Hypot(s.x - px, s.z - pz) * 0.5);
                }
                if (prev < 1 - BREACH_OUT && s.breach >= 1 - BREACH_OUT)
                {
                    splash(at, 70);
                    onSplash(JS.Hypot(s.x - px, s.z - pz) * 0.5);
                }
                // spray drips off it while it is in the air
                if (height > 0.2 && JS.Random() < dt * 14) splashes.burst(new THREE.Vector3(s.x, level + height, s.z), 4, 0.6, 0.5, 0.7, white);
                if (s.breach >= 1)
                {
                    s.breach = 0;
                    leapDone = true;
                }
            }
            else
            {
                steer(s, dt, t, 0.55);
            }
            double u = Math.Min(1, s.breach != 0 ? s.breach : (leapDone ? 1 : 0));
            s.root.position.set(s.x, level - 0.22 + height, s.z);
            // nose up as it leaves the water, level at the top, nose down as it returns, with a slow half-roll over the top
            s.root.rotation.set(leaping ? pitch : Math.Sin(t * 1.1 + s.phase) * 0.02, s.heading, leaping ? (u - 0.5) * 1.5 : 0, "YXZ");

            // swimming: a wave runs down the body, strongest at the tail
            var pos = s.body.geometry.attributes.position;
            double amp = leaping ? 0.28 : 0.12;
            double w = t * (leaping ? 7 : 3.6) + s.phase;
            for (int i = 0; i < pos.count; i++)
            {
                double z = s.rest[i * 3 + 2];
                double k = (z + SHARK_LEN / 2) / SHARK_LEN; // 0 at the nose, 1 at the tail
                pos.setX(i, s.rest[i * 3] + Math.Sin(w - k * 3.4) * amp * k * k);
            }
            pos.needsUpdate = true;
            s.body.geometry.computeVertexNormals();
            double tailOff = Math.Sin(w - 3.4) * amp;
            s.caudal.position.x = tailOff;
            s.caudal.rotation.y = Math.Cos(w - 3.4) * amp * 1.6;

            // bow wave and a foamy wake behind the fin
            s.wake -= dt;
            if (s.wake <= 0 && !leaping)
            {
                s.wake = 0.06;
                double fx = -Math.Sin(s.heading);
                double fz = -Math.Cos(s.heading);
                double rx = Math.Cos(s.heading);
                double rz = -Math.Sin(s.heading);
                foreach (var side in new[] { -1, 1 })
                {
                    var v = new THREE.Vector3(rx * side * 0.35, 0.05, rz * side * 0.35);
                    wakeParticles.emit(new THREE.Vector3(s.x - fx * 2.2 + rx * side * 0.3, level + 0.04, s.z - fz * 2.2 + rz * side * 0.3), v, 1.4, white);
                    wakeParticles.emit(new THREE.Vector3(s.x + fx * 2.3 + rx * side * 0.25, level + 0.05, s.z + fz * 2.3 + rz * side * 0.25), v.multiplyScalar(1.8), 0.8, white);
                }
            }
        }

        foreach (var sc in schools)
        {
            steer(sc, dt, t, 0.9);
            double cs = Math.Cos(sc.heading);
            double sn = Math.Sin(sc.heading);
            foreach (var f in sc.fish)
            {
                if (!f.alive)
                {
                    f.respawn -= dt;
                    if (f.respawn <= 0)
                    {
                        f.alive = true;
                        f.mesh.visible = true;
                    }
                    continue;
                }
                // a school far across the water is not drawn, and does not need animating either
                bool far = JS.Hypot(sc.x - px, sc.z - pz) > 130;
                f.mesh.visible = !far;
                if (far) continue;
                double ox = f.ox + Math.Sin(t * 1.3 + f.ph) * 0.35;
                double oz = f.oz + Math.Cos(t * 1.1 + f.ph) * 0.35;
                f.mesh.position.set(sc.x + cs * ox + sn * oz, level + 0.02 + Math.Sin(t * 5 + f.ph) * 0.02, sc.z - sn * ox + cs * oz);
                f.mesh.rotation.y = sc.heading + Math.Sin(t * 6 + f.ph) * 0.12;
                f.tail.rotation.y = Math.Sin(t * 14 + f.ph) * 0.7;
            }
        }

        // now and then a fish leaps clear of the water
        jumpTimer -= dt;
        if (jumpTimer <= 0)
        {
            jumpTimer = rand(2.5, 6);
            var free = jumpers.FirstOrDefault(j => !j.active);
            var school = schools.Count > 0 ? schools[(int)Math.Floor(JS.Random() * schools.Count)] : null;
            if (free != null && school != null)
            {
                free.active = true;
                free.t = 0;
                free.sx = school.x + rand(-1, 1);
                free.sz = school.z + rand(-1, 1);
                free.yaw = school.heading + rand(-0.5, 0.5);
                free.dx = -Math.Sin(free.yaw) * 3;
                free.dz = -Math.Cos(free.yaw) * 3;
                free.mesh.visible = true;
                splash(new THREE.Vector3(free.sx, level, free.sz), 10);
                onSplash(JS.Hypot(free.sx - px, free.sz - pz));
            }
        }
        foreach (var j in jumpers)
        {
            if (!j.active) continue;
            if (smokeHold == null) j.t += dt / 0.95;
            double u = j.t;
            if (u >= 1)
            {
                j.active = false;
                j.mesh.visible = false;
                double x = j.sx + j.dx;
                double z = j.sz + j.dz;
                splash(new THREE.Vector3(x, level, z), 12);
                onSplash(JS.Hypot(x - px, z - pz));
                continue;
            }
            double y = level + 4 * 1.3 * u * (1 - u);
            j.mesh.position.set(j.sx + j.dx * u, y, j.sz + j.dz * u);
            // nose up on the way out, down on the way back in, with a little twist
            j.mesh.rotation.set((0.5 - u) * 1.8, j.yaw, u * 9, "YXZ");
        }

        splashes.update(dt);
        wakeParticles.update(dt);
    }
}
