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
    private readonly THREE.SphereGeometry fishGeo = new THREE.SphereGeometry(1, 8, 6);
    private readonly THREE.ConeGeometry tailGeo = new THREE.ConeGeometry(0.07, 0.16, 4);
    private readonly THREE.BufferGeometry sharkBody;
    /// <summary>the body's radius along its length: 0 at the nose, 1 at the tail</summary>
    private List<THREE.Vector2> outline = new List<THREE.Vector2>();
    private readonly THREE.BufferGeometry caudal;
    private readonly THREE.BufferGeometry pectoral;
    private double breachTimer = 40;

    public SeaLife()
    {
        var fin = new THREE.MeshStandardMaterial { color = 0x586a7a, roughness = 0.45, metalness = 0.08, side = THREE.Side.DoubleSide };
        var finGeo = finGeometry();
        sharkBody = bodyGeometry();
        caudal = caudalGeometry();
        pectoral = pectoralGeometry();
        // a bigger sea, so a few more of everything
        for (int i = 0; i < 2; i++) sharks.Add(makeShark(finGeo, fin));
        for (int i = 0; i < 12; i++) schools.Add(makeSchool());
        for (int i = 0; i < 6; i++) jumpers.Add(makeJumper());
        group.add(splashes.points, wakeParticles.points);
    }

    private THREE.BufferGeometry finGeometry()
    {
        var sh = new THREE.Shape();
        sh.moveTo(0.45, 0);
        sh.quadraticCurveTo(0.3, 0.5, -0.2, 1.05);
        sh.quadraticCurveTo(-0.15, 0.5, -0.5, 0);
        sh.closePath();
        var g = new THREE.ExtrudeGeometry(sh, new THREE.ExtrudeGeometryOptions { depth = 0.07, bevelEnabled = true, bevelSize = 0.015, bevelThickness = 0.015, bevelSegments = 1 });
        g.translate(0, 0, -0.035);
        g.rotateY(Math.PI / 2); // the fin's front now points along -z
        return g;
    }

    /// <summary>A torpedo-shaped body along z (nose at -z), grey on top and pale underneath.</summary>
    private THREE.BufferGeometry bodyGeometry()
    {
        const double L = SHARK_LEN;
        var profile = new[]
        {
            (0.0, 0.03), (0.1, 0.1), (0.22, 0.2), (0.4, 0.34), (0.58, 0.42), (0.74, 0.4), (0.86, 0.31), (0.94, 0.2), (0.985, 0.08), (1.0, 0.0),
        };
        var pts = new List<THREE.Vector2>();
        // smooth the profile with a Catmull-Rom curve
        var curve = new THREE.SplineCurve(profile.Select(p => new THREE.Vector2(p.Item1 * L - L / 2, p.Item2)).ToList());
        foreach (var pt in curve.getPoints(44)) pts.Add(new THREE.Vector2(Math.Max(0, pt.y), pt.x));
        outline = curve.getPoints(200).Select(pt => new THREE.Vector2((pt.x + L / 2) / L, Math.Max(0, pt.y))).ToList();
        var g = new THREE.LatheGeometry(pts, 22);
        g.rotateX(-Math.PI / 2); // lathe axis y -> z, nose (+y) -> -z
        g.scale(1.3, 0.95, 1);
        g.computeVertexNormals();
        var pos = g.attributes.position;
        var nrm = g.attributes.normal;
        var col = new float[pos.count * 3];
        var top = new THREE.Color(0x56697a);
        var belly = new THREE.Color(0xe9eff1);
        var c = new THREE.Color();
        for (int i = 0; i < pos.count; i++)
        {
            double t = Math.Min(1, Math.Max(0, (nrm.getY(i) + 0.15) / 0.5));
            double k = t * t * (3 - 2 * t);
            c.copy(belly).lerp(top, k);
            col[i * 3] = (float)c.r;
            col[i * 3 + 1] = (float)c.g;
            col[i * 3 + 2] = (float)c.b;
        }
        g.setAttribute("color", new THREE.BufferAttribute(col, 3));
        return g;
    }

    private THREE.BufferGeometry caudalGeometry()
    {
        var sh = new THREE.Shape();
        sh.moveTo(0, 0.1);
        sh.lineTo(0.3, 0.95);
        sh.quadraticCurveTo(0.62, 1.18, 0.7, 1.2);
        sh.quadraticCurveTo(0.45, 0.5, 0.42, 0.05);
        sh.quadraticCurveTo(0.5, -0.4, 0.34, -0.72);
        sh.lineTo(0.12, -0.2);
        sh.closePath();
        var g = new THREE.ExtrudeGeometry(sh, new THREE.ExtrudeGeometryOptions { depth = 0.05, bevelEnabled = false });
        g.translate(0, 0, -0.025);
        g.rotateY(-Math.PI / 2); // sweeps backwards (+z)
        return g;
    }

    private THREE.BufferGeometry pectoralGeometry()
    {
        var sh = new THREE.Shape();
        sh.moveTo(0, 0.28);
        sh.quadraticCurveTo(0.55, 0.1, 1.05, -0.5);
        sh.lineTo(0.2, -0.22);
        sh.closePath();
        var g = new THREE.ExtrudeGeometry(sh, new THREE.ExtrudeGeometryOptions { depth = 0.035, bevelEnabled = false });
        g.rotateX(-Math.PI / 2);
        return g;
    }

    /// <summary>Half the body's width and height (in model units) at a point along it, 0 at the nose and 1 at the tail.</summary>
    private (double w, double h) halfSize(double u)
    {
        double r = 0;
        // the lathed outline runs from the tail (0) to the nose (1)
        double uu = 1 - u;
        for (int i = 1; i < outline.Count; i++)
        {
            if (outline[i].x >= uu)
            {
                var a = outline[i - 1];
                var b = outline[i];
                r = a.y + (b.y - a.y) * (uu - a.x) / Math.Max(1e-6, b.x - a.x);
                break;
            }
        }
        // the body is lathed, then squeezed 1.3 wide and 0.95 high
        return (r * 1.3, r * 0.95);
    }

    private Shark makeShark(THREE.BufferGeometry finGeo, THREE.Material finMat)
    {
        var root = new THREE.Group();
        var bodyMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.42, metalness = 0.05 };
        var bodyGeo = sharkBody.clone();
        var body = new THREE.Mesh(bodyGeo, bodyMat);
        body.castShadow = true;
        var rest = (float[])bodyGeo.attributes.position.array.Clone();

        var dorsal = new THREE.Mesh(finGeo, finMat);
        dorsal.position.set(0, 0.36, -0.15);
        dorsal.castShadow = true;
        var dorsal2 = new THREE.Mesh(finGeo, finMat);
        dorsal2.scale.setScalar(0.28);
        dorsal2.position.set(0, 0.2, 1.15);

        var pec = pectoral;
        foreach (var side in new[] { -1, 1 })
        {
            var f = new THREE.Mesh(pec, finMat);
            f.material = finMat;
            f.position.set(side * 0.3, -0.16, -0.65);
            f.rotation.z = side * 0.55;
            f.scale.x = side;
            root.add(f);
        }
        var caudal = new THREE.Group();
        var lobe = new THREE.Mesh(this.caudal, finMat);
        lobe.castShadow = true;
        caudal.add(lobe);
        caudal.position.set(0, 0.0, SHARK_LEN / 2 - 0.05);

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
                var slit = new THREE.Mesh(new THREE.BoxGeometry(0.012, hg.h * 0.62, 0.016), dark);
                slit.position.set(side * (hg.w - 0.004), 0.02, zAt(ug));
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
            // the pelvic fins, small and low
            var pelvic = new THREE.Mesh(pectoral, finMat);
            pelvic.scale.set(side * 0.28, 0.28, 0.28);
            pelvic.position.set(side * 0.12, -0.2, 0.62);
            pelvic.rotation.z = side * 0.5;
            root.add(pelvic);
        }
        // the mouth: a wide open grin on the underside of the snout, a dark gape with a row of teeth along each jaw
        var gape = new THREE.Mesh(new THREE.CircleGeometry(0.2, 20, 0, Math.PI), gum);
        gape.rotation.x = Math.PI / 2;
        gape.rotation.z = Math.PI;
        gape.scale.set(1, 1.35, 1);
        gape.position.set(0, -0.2, -1.86);
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
        teeth(1, -0.195, 0.07);
        teeth(-1, -0.22, 0.045);
        // a small anal fin behind the belly
        var anal = new THREE.Mesh(finGeo, finMat);
        anal.scale.setScalar(0.22);
        anal.rotation.x = Math.PI;
        anal.position.set(0, -0.22, 1.2);
        root.add(anal);

        var hit = new THREE.Mesh(new THREE.SphereGeometry(1, 8, 6), new THREE.MeshBasicMaterial { visible = false });
        hit.position.set(0, 0.4, 0);
        hit.userData["hitRadius"] = 2.2;
        root.add(body, dorsal, dorsal2, caudal, hit);
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

    private School makeSchool()
    {
        bool silver = JS.Random() < 0.5;
        var bodyMat = new THREE.MeshStandardMaterial { color = silver ? 0xcfd8dc : 0xff8a3c, roughness = 0.35, metalness = silver ? 0.5 : 0.1 };
        var fish = new List<School.Fish>();
        int n = 8 + (int)Math.Floor(JS.Random() * 5);
        for (int i = 0; i < n; i++)
        {
            var g = new THREE.Group();
            var body = new THREE.Mesh(fishGeo, bodyMat);
            body.scale.set(0.09, 0.07, 0.24);
            var tail = new THREE.Mesh(tailGeo, bodyMat);
            tail.rotation.x = -Math.PI / 2;
            tail.position.z = 0.28;
            g.add(body, tail);
            g.scale.setScalar(rand(0.9, 1.4));
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

    private Jumper makeJumper()
    {
        var g = new THREE.Group();
        var mat = new THREE.MeshStandardMaterial { color = 0xd8e4ea, roughness = 0.3, metalness = 0.6 };
        var body = new THREE.Mesh(fishGeo, mat);
        body.scale.set(0.1, 0.08, 0.28);
        var tail = new THREE.Mesh(tailGeo, mat);
        tail.rotation.x = -Math.PI / 2;
        tail.position.z = 0.32;
        g.add(body, tail);
        g.scale.setScalar(1.6);
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
        if (w.x > X_MAX) tx = -1;
        else if (w.x < -X_MAX) tx = 1;
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

    /// <summary>`level` is the current surface height; onSplash(distance) lets the sound be placed.</summary>
    public void update(double dt, double t, double level, double px, double pz, Action<double> onSplash)
    {
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
                s.breach += dt / BREACH_SECONDS;
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
            j.t += dt / 0.95;
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
