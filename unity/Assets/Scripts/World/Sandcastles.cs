using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Sandcastles.ts

public class Castle
{
    public THREE.Group group;
    /// <summary>an invisible marker bullets and arrows can hit</summary>
    public THREE.Object3D target;
    public double x, z;
    public int stage;
    /// <summary>1 fresh, 0 gone: rain, the sea and shots wear it down</summary>
    public double hp;
    public double seed;
    public string style;
    /// <summary>the parts of each stage, as pivots that sit on the sand so they can grow upward</summary>
    public List<THREE.Group>[] parts;
    /// <summary>parts that are still rising, with how far they have got (0..1)</summary>
    public List<Rising> rising = new List<Rising>();
    /// <summary>chunks that have been shot off and are tumbling away</summary>
    public List<Falling> falling = new List<Falling>();
    public List<THREE.MeshStandardMaterial> mats = new List<THREE.MeshStandardMaterial>();
    public double wet;
    /// <summary>run over by the golf cart: its pieces are flying off and it is gone once they have landed</summary>
    public bool smashed;
    /// <summary>the castle's GameObjects (native since unity/NATIVE_PLAN.md S11)</summary>
    public LiveTree native;

    public class Rising { public THREE.Group pivot; public double t; }
    public class Falling
    {
        public THREE.Group pivot;
        public THREE.Vector3 v, spin;
        public double t;
        /// <summary>a piece knocked off by the cart: it lands on the sand, slides to a stop and sinks away</summary>
        public bool smash;
        public bool landed;
        public double sink;
        /// <summary>the base mound, which is not thrown but squashed flat under the wheels</summary>
        public bool squash;
    }
}

/// <summary>
/// Little sand castles the player builds with a bucket, in three stages and a different design each time. Rain, the sea and
/// gunfire wear them down: a shot knocks chunks off, and enough damage brings the whole thing down.
/// </summary>
public class Sandcastles
{
    private static readonly THREE.Color BASE = new THREE.Color(0xdcc38f);
    private static readonly THREE.Color WET = new THREE.Color(0xa88a5c);
    private const int GATE_COLOR = 0x7a6038;
    public const int MAX_CASTLES = 6;
    private static readonly string[] STYLES = { "round", "keep", "spire", "step" };
    private static readonly int[] FLAGS = { 0xe0452f, 0x2f78d0, 0xf2b632, 0x35a86a, 0xc23bb0 };

    private static Func<double> seeded(double seedIn)
    {
        // small seeds make the first few values of this simple generator alike, so mix the seed up and skip a few
        double seed = JS.ToUint32(JS.Imul(JS.ToInt32(seedIn) ^ unchecked((int)0x9e3779b9), 2654435761));
        double next()
        {
            seed = (seed * 1664525 + 1013904223) % 4294967296;
            return seed / 4294967296;
        }
        for (int i = 0; i < 4; i++) next();
        return next;
    }

    private static double rand01(double seed) => seeded(JS.ToInt32(seed) ^ unchecked((int)0x9e3779b9))();

    private static THREE.BufferGeometry jitter(THREE.BufferGeometry geo, double amount)
    {
        var p = geo.attributes.position;
        var seen = new Dictionary<(double, double, double), double[]>();
        for (int i = 0; i < p.count; i++)
        {
            var key = (JS.Round(p.getX(i) * 500), JS.Round(p.getY(i) * 500), JS.Round(p.getZ(i) * 500));
            if (!seen.TryGetValue(key, out var d))
            {
                d = new[] { (JS.Random() - 0.5) * amount, (JS.Random() - 0.5) * amount * 0.6, (JS.Random() - 0.5) * amount };
                seen[key] = d;
            }
            p.setXYZ(i, p.getX(i) + d[0], p.getY(i) + d[1], p.getZ(i) + d[2]);
        }
        geo.computeVertexNormals();
        return geo;
    }

    public readonly THREE.Group group = new THREE.Group();
    public List<Castle> castles = new List<Castle>();
    /// <summary>solid circles for walking and driving around</summary>
    public readonly List<Collider> colliders = new List<Collider>();
    private readonly Beach beach;

    public class Collider { public double x, z, r; }

    public Sandcastles(Beach beach) { this.beach = beach; }

    private UnityEngine.Transform root;

    /// <summary>
    /// Puts the castles into the world as native objects. Each castle is a LiveTree, brought in line with its description at
    /// draw time (NativeFrame), after everything that grows, crumbles, smashes or washes them this frame.
    /// </summary>
    public void Place()
    {
        root = Draw.Group(World.Land);
        foreach (var c in castles) c.native = new LiveTree(c.group, root);
        NativeFrame.BeforeDraw += () =>
        {
            foreach (var c in castles) c.native?.Sync();
        };
    }

    public Castle nearest(double x, double z, double reach)
    {
        Castle best = null;
        double bd = reach;
        foreach (var c in castles)
        {
            if (c.smashed) continue;
            double d = JS.Hypot(c.x - x, c.z - z);
            if (d < bd)
            {
                bd = d;
                best = c;
            }
        }
        return best;
    }

    public int count => castles.Count(c => !c.smashed);

    /// <summary>What bullets and arrows can hit.</summary>
    public List<Target> targets() => castles.Where(c => !c.smashed).Select(c => Target.Of(TargetKind.Castle, c.target)).ToList();

    public Castle castleOf(THREE.Object3D obj) => castles.FirstOrDefault(c => c.target == obj);

    private void refreshColliders()
    {
        colliders.Clear();
        foreach (var c in castles) if (!c.smashed) colliders.Add(new Collider { x = c.x, z = c.z, r = 0.95 });
    }

    private void build(Castle c)
    {
        var rand = seeded(c.seed);
        T pick<T>(IList<T> list) => list[(int)Math.Floor(rand() * list.Count)];
        double between(double a, double b) => a + rand() * (b - a);
        c.style = pick(STYLES);
        double tall = between(0.8, 1.3);

        var mat = new THREE.MeshStandardMaterial { color = BASE.clone().offsetHSL((rand() - 0.5) * 0.012, 0, (rand() - 0.5) * 0.03), roughness = 1, flatShading = true };
        c.mats.Add(mat);
        var dark = new THREE.MeshStandardMaterial { color = GATE_COLOR, roughness = 1 };
        var flagMat = new THREE.MeshStandardMaterial { color = pick(FLAGS), roughness = 0.6, side = THREE.Side.DoubleSide };

        // a part is a pivot on the sand with the mesh standing on it, so scaling the pivot makes the part grow upward
        THREE.Group part(int stage, THREE.BufferGeometry geo, THREE.Material m, double x, double y, double z, double lift)
        {
            var pivot = new THREE.Group();
            pivot.position.set(x, y, z);
            var mesh = new THREE.Mesh(geo, m);
            mesh.position.y = lift;
            mesh.castShadow = true;
            mesh.receiveShadow = true;
            pivot.add(mesh);
            pivot.visible = false;
            c.group.add(pivot);
            c.parts[stage - 1].Add(pivot);
            return pivot;
        }
        THREE.BufferGeometry cyl(double r0, double r1, double h, int seg = 14) => jitter(new THREE.CylinderGeometry(r0, r1, h, seg, 1), 0.03);
        THREE.BufferGeometry box(double w, double h, double d) => jitter(new THREE.BoxGeometry(w, h, d), 0.03);
        THREE.BufferGeometry pyramid(double r, double h) => new THREE.ConeGeometry(r, h, 4).rotateY(Math.PI / 4);
        void flag(double x, double y, double z)
        {
            var pole = part(3, new THREE.CylinderGeometry(0.014, 0.014, 0.55, 6), mat, x, y, z, 0.275);
            var cloth = new THREE.Mesh(new THREE.PlaneGeometry(0.28, 0.17), flagMat);
            cloth.position.set(0.15, 0.45, 0);
            pole.add(cloth);
        }
        var corners = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };

        // stage 1 is the same for every castle: a wide base and a rounded mound
        part(1, jitter(new THREE.CylinderGeometry(1.05, 1.18, 0.14, 26, 1), 0.04), mat, 0, 0, 0, 0.07);
        part(1, jitter(new THREE.SphereGeometry(0.78, 20, 10, 0, Math.PI * 2, 0, Math.PI / 2), 0.05), mat, 0, 0.1, 0, 0).scale.set(1, 0.7, 1);

        if (c.style == "round")
        {
            // a ring wall with battlements, a round keep, and three to five round towers
            int N = 10 + (int)Math.Floor(rand() * 7);
            for (int i = 0; i < N; i++)
            {
                double a = (double)i / N * Math.PI * 2;
                var w = part(2, box(Math.PI * 2 * 0.86 / N + 0.06, 0.36, 0.15), mat, Math.Cos(a) * 0.86, 0.12, Math.Sin(a) * 0.86, 0.18);
                w.rotation.y = -a + Math.PI / 2;
                if (i % 2 == 0) part(2, box(0.15, 0.12, 0.15), mat, Math.Cos(a) * 0.86, 0.48, Math.Sin(a) * 0.86, 0.06).rotation.y = -a + Math.PI / 2;
            }
            part(2, cyl(0.3, 0.38, 0.85 * tall), mat, 0, 0.12, 0, 0.425 * tall);
            part(2, cyl(0.4, 0.34, 0.12), mat, 0, 0.12 + 0.85 * tall, 0, 0.06);
            int towers = 3 + (int)Math.Floor(rand() * 3);
            double start = rand() * Math.PI;
            bool cones = rand() < 0.6;
            for (int k = 0; k < towers; k++)
            {
                double a = start + k * Math.PI * 2 / towers;
                double x = Math.Cos(a) * 0.93;
                double z = Math.Sin(a) * 0.93;
                double th = 0.62 + rand() * 0.3;
                part(3, cyl(0.19, 0.25, th), mat, x, 0.12, z, th / 2);
                part(3, cyl(0.27, 0.22, 0.1), mat, x, 0.12 + th, z, 0.05);
                if (cones) part(3, new THREE.ConeGeometry(0.2, 0.3, 10), mat, x, 0.22 + th, z, 0.15);
            }
            part(3, box(0.3, 0.3, 0.12), dark, 0, 0.12, 0.9, 0.15);
            flag(0, 0.12 + 0.97 * tall, 0);
        }
        else if (c.style == "keep")
        {
            // square walls and a square keep, with a little pyramid roof on each corner tower
            foreach (var side in new[] { 0, 1, 2, 3 })
            {
                double a = side * Math.PI / 2;
                double px = Math.Cos(a) * 0.85;
                double pz = Math.Sin(a) * 0.85;
                part(2, box(1.75, 0.36, 0.16), mat, px, 0.12, pz, 0.18).rotation.y = -a + Math.PI / 2;
                for (int i = -2; i <= 2; i += 1)
                {
                    if (i % 2 != 0) continue;
                    var m = part(2, box(0.16, 0.12, 0.16), mat, px + Math.Cos(a + Math.PI / 2) * i * 0.34, 0.48, pz + Math.Sin(a + Math.PI / 2) * i * 0.34, 0.06);
                    m.rotation.y = -a + Math.PI / 2;
                }
            }
            part(2, box(0.58, 0.95 * tall, 0.58), mat, 0, 0.12, 0, 0.475 * tall);
            foreach (var (dx, dz) in corners) part(2, box(0.14, 0.12, 0.14), mat, dx * 0.22, 0.12 + 0.95 * tall, dz * 0.22, 0.06);
            foreach (var (dx, dz) in corners)
            {
                double th = 0.66 + rand() * 0.25;
                part(3, box(0.34, th, 0.34), mat, dx * 0.85, 0.12, dz * 0.85, th / 2);
                part(3, pyramid(0.3, 0.32), mat, dx * 0.85, 0.12 + th, dz * 0.85, 0.16);
            }
            part(3, box(0.34, 0.32, 0.12), dark, 0, 0.12, 0.88, 0.16);
            flag(0, 0.12 + 0.95 * tall + 0.12, 0);
        }
        else if (c.style == "spire")
        {
            // a tall central spire with a ring of little turrets, and a low wall around them
            const int N = 12;
            for (int i = 0; i < N; i++)
            {
                double a = (double)i / N * Math.PI * 2;
                part(2, box(0.5, 0.2, 0.14), mat, Math.Cos(a) * 0.88, 0.12, Math.Sin(a) * 0.88, 0.1).rotation.y = -a + Math.PI / 2;
            }
            part(2, cyl(0.12, 0.36, 1.4 * tall, 16), mat, 0, 0.12, 0, 0.7 * tall);
            part(2, new THREE.ConeGeometry(0.2, 0.55 * tall, 14), mat, 0, 0.12 + 1.4 * tall, 0, 0.275 * tall);
            int turrets = 5 + (int)Math.Floor(rand() * 3);
            for (int k = 0; k < turrets; k++)
            {
                double a = (double)k / turrets * Math.PI * 2 + 0.3;
                double th = 0.48 + rand() * 0.25;
                part(2, cyl(0.1, 0.14, th), mat, Math.Cos(a) * 0.7, 0.12, Math.Sin(a) * 0.7, th / 2);
                part(3, new THREE.ConeGeometry(0.15, 0.24, 8), mat, Math.Cos(a) * 0.7, 0.12 + th, Math.Sin(a) * 0.7, 0.12);
            }
            part(3, box(0.32, 0.26, 0.14), dark, 0, 0.12, 0.9, 0.13);
            part(3, box(0.5, 0.06, 0.34), mat, 0, 0.12, 1.05, 0.03); // a little bridge out front
            flag(0, 0.12 + 1.4 * tall + 0.52 * tall, 0);
        }
        else
        {
            // a stepped pyramid with a stair up the front and small towers on the lowest step
            part(2, box(1.55, 0.34, 1.55), mat, 0, 0.12, 0, 0.17);
            part(2, box(1.1, 0.34, 1.1), mat, 0, 0.46, 0, 0.17);
            part(3, box(0.62, 0.4 * tall, 0.62), mat, 0, 0.8, 0, 0.2 * tall);
            for (int s = 0; s < 4; s++) part(3, box(0.34, 0.1 + s * 0.1, 0.16), mat, 0, 0.12, 0.86 + (3 - s) * 0.16, (0.1 + s * 0.1) / 2);
            foreach (var (dx, dz) in corners)
            {
                part(3, cyl(0.1, 0.14, 0.5), mat, dx * 0.62, 0.46, dz * 0.62, 0.25);
                part(3, new THREE.ConeGeometry(0.13, 0.2, 8), mat, dx * 0.62, 0.96, dz * 0.62, 0.1);
            }
            flag(0, 0.8 + 0.4 * tall, 0);
        }
    }

    public Castle add(double x, double z, int stage = 1, double hp = 1, double? seedIn = null)
    {
        double seed = seedIn ?? Math.Floor(JS.Random() * 1e9);
        var c = new Castle
        {
            group = new THREE.Group(), target = new THREE.Object3D(), x = x, z = z, stage = 0, hp = hp, seed = seed, style = "round",
            parts = new[] { new List<THREE.Group>(), new List<THREE.Group>(), new List<THREE.Group>() }, wet = 0,
        };
        c.group.position.set(x, beach.heightAt(x, z) - 0.03, z);
        c.group.rotation.y = rand01(seed) * Math.PI * 2;
        build(c);
        c.target.position.set(0, 0.55, 0);
        c.target.userData["hitRadius"] = 1.0;
        c.group.add(c.target);
        group.add(c.group);
        castles.Add(c);
        if (root != null) c.native = new LiveTree(c.group, root);
        for (int s = 1; s <= stage; s++) raise(c, s, hp == 1 && stage == 1);
        refreshColliders();
        return c;
    }

    /// <summary>Makes a stage appear. With `animate` the parts rise out of the sand.</summary>
    private void raise(Castle c, int stage, bool animate)
    {
        c.stage = Math.Max(c.stage, stage);
        foreach (var pivot in c.parts[stage - 1])
        {
            pivot.visible = true;
            if (animate)
            {
                pivot.userData["sy"] = pivot.scale.y;
                pivot.scale.y = 0.01;
                c.rising.Add(new Castle.Rising { pivot = pivot, t = -JS.Random() * 0.25 });
            }
        }
    }

    /// <summary>Adds the next stage to a castle. Returns false if it is already finished.</summary>
    public bool upgrade(Castle c)
    {
        if (c.stage >= 3) return false;
        raise(c, c.stage + 1, true);
        return true;
    }

    public void remove(Castle c)
    {
        group.remove(c.group);
        c.native?.Destroy();
        c.native = null;
        castles = castles.Where(k => k != c).ToList();
        refreshColliders();
    }

    /// <summary>
    /// The golf cart runs into any castle within `reach` of (x, z), going at `speed` (m/s, negative in reverse) with its nose
    /// toward `heading` (Three's yaw). Each castle hit bursts: its towers, roofs and flags are thrown up and off to both sides
    /// of the cart's path, the walls tumble after them, and the mound is squashed flat. Returns the castles hit.
    /// </summary>
    public List<Castle> smashAt(double x, double z, double reach, double heading, double speed)
    {
        List<Castle> hit = null;
        foreach (var c in castles)
        {
            if (c.smashed || JS.Hypot(c.x - x, c.z - z) > reach) continue;
            smash(c, heading, speed);
            (hit ??= new List<Castle>()).Add(c);
        }
        if (hit != null) refreshColliders();
        return hit;
    }

    private void smash(Castle c, double heading, double speed)
    {
        c.smashed = true;
        c.rising.Clear();
        double s = Math.Min(1.25, Math.Max(0.6, Math.Abs(speed) / 6));
        // the cart's way forward and its right hand, in the world (Three's axes: yaw 0 looks toward -z)
        double dir = speed >= 0 ? 1 : -1;
        double fx = -Math.Sin(heading) * dir, fz = -Math.Cos(heading) * dir;
        double rx = Math.Cos(heading), rz = -Math.Sin(heading);
        // the castle group is turned: world directions are turned back into its own frame
        double a = c.group.rotation.y, ca = Math.Cos(a), sa = Math.Sin(a);
        THREE.Vector3 local(double wx, double wy, double wz) => new THREE.Vector3(wx * ca - wz * sa, wy, wx * sa + wz * ca);
        var already = new HashSet<THREE.Group>(c.falling.Select(f => f.pivot));
        for (int stage = 0; stage < 3; stage++)
        {
            foreach (var p in c.parts[stage])
            {
                if (!p.visible || already.Contains(p)) continue;
                if (stage == 0)
                {
                    c.falling.Add(new Castle.Falling { pivot = p, v = new THREE.Vector3(), spin = new THREE.Vector3(), smash = true, squash = true });
                    continue;
                }
                // which side of the cart's path this piece stands on, in the world
                double wx = p.position.x * ca + p.position.z * sa, wz = -p.position.x * sa + p.position.z * ca;
                double side = wx * rx + wz * rz;
                double sign = Math.Abs(side) < 0.08 ? (JS.Random() < 0.5 ? -1 : 1) : Math.Sign(side);
                // thrown out to the sides of the cart, not up at the windscreen: sideways is the strongest push, and what stood
                // in the cart's way (near its centre line) is pushed aside hardest. The higher it stood (roofs, cones, flags),
                // the further it goes
                double high = Math.Min(1, Math.Max(0, p.position.y / 1.2));
                double centre = 1 - Math.Min(1, Math.Abs(side) / 0.9);
                double out_ = (2.6 + JS.Random() * 1.2 + high * 1.6 + centre * 1.4) * s;
                double ahead = (0.4 + JS.Random() * 0.7) * s;
                double up = (1.3 + JS.Random() * 0.9 + high * 1.1) * s;
                c.falling.Add(new Castle.Falling
                {
                    pivot = p,
                    v = local(rx * sign * out_ + fx * ahead, up, rz * sign * out_ + fz * ahead),
                    spin = new THREE.Vector3((JS.Random() - 0.5) * 9, (JS.Random() - 0.5) * 7, sign * (3 + JS.Random() * 5)),
                    t = -JS.Random() * 0.08 * (1 - high),
                    smash = true,
                });
            }
        }
    }

    /// <summary>
    /// A shot lands. Knocks the chunks nearest to `at` off the castle and wears it down. Returns true if the whole castle came down.
    /// </summary>
    public bool hit(Castle c, THREE.Vector3 at, double power)
    {
        if (c.smashed) return false;
        c.hp -= power;
        var world = new THREE.Vector3();
        var live = c.parts[1].Concat(c.parts[2]).Where(p => p.visible && !c.falling.Any(f => f.pivot == p)).ToList();
        var from = at ?? c.group.getWorldPosition(new THREE.Vector3());
        live = live.OrderBy(p => p.getWorldPosition(world).distanceTo(from)).ToList();
        int knock = power >= 0.18 ? 3 : 2;
        foreach (var p in live.Take(knock))
        {
            c.falling.Add(new Castle.Falling
            {
                pivot = p,
                v = new THREE.Vector3((JS.Random() - 0.5) * 2.2, 1.6 + JS.Random() * 1.4, (JS.Random() - 0.5) * 2.2),
                spin = new THREE.Vector3((JS.Random() - 0.5) * 6, (JS.Random() - 0.5) * 6, (JS.Random() - 0.5) * 6),
                t = 0,
            });
        }
        // nothing but the bare mound left of a built-up castle counts as ruined
        int standing = live.Count - Math.Min(knock, live.Count);
        return c.hp <= 0 || (c.stage >= 2 && standing <= 0);
    }

    /// <summary>Rain, the shore and tumbling chunks; `onWash` is told about each castle that is lost to the weather or the sea.</summary>
    public void update(double dt, double rain, Action<double, double, bool> onWash)
    {
        foreach (var c in castles.ToList())
        {
            if (c.smashed)
            {
                updateSmash(c, dt);
                continue;
            }
            for (int i = c.rising.Count - 1; i >= 0; i--)
            {
                var r = c.rising[i];
                r.t += dt / 0.8;
                if (r.t < 0) continue;
                double e = Math.Min(1, r.t);
                r.pivot.scale.y = Math.Max(0.01, r.pivot.userData.Num("sy", 1) * (1 - Math.Pow(1 - e, 3)));
                if (e >= 1) c.rising.RemoveAt(i);
            }
            // chunks that were shot off fly away and disappear
            for (int i = c.falling.Count - 1; i >= 0; i--)
            {
                var f = c.falling[i];
                f.t += dt;
                f.v.y -= 9 * dt;
                f.pivot.position.addScaledVector(f.v, dt);
                f.pivot.rotation.x += f.spin.x * dt;
                f.pivot.rotation.y += f.spin.y * dt;
                f.pivot.rotation.z += f.spin.z * dt;
                if (f.t > 0.8)
                {
                    f.pivot.visible = false;
                    c.falling.RemoveAt(i);
                }
            }
            bool shore = c.z > WET_Z - 1.5;
            // a proper shower breaks a castle in about half a minute
            double rainRate = rain > 0.3 ? rain / 28 : 0;
            c.hp -= dt * (rainRate + (shore ? 1 / 220.0 : 0));
            c.wet += ((rain > 0.3 ? 1 : 0) - c.wet) * Math.Min(1, dt * 0.5);
            if (c.hp <= 0)
            {
                onWash(c.x, c.z, rain > 0.3);
                remove(c);
                continue;
            }
            // it slumps and darkens as it wears away
            double k = 0.35 + 0.65 * Math.Pow(Math.Max(0, c.hp), 0.7);
            c.group.scale.set(1 + (1 - k) * 0.12, k, 1 + (1 - k) * 0.12);
            foreach (var m in c.mats) m.color.copy(BASE).lerp(WET, Math.Min(1, c.wet * 0.6 + (1 - c.hp) * 0.4));
        }
    }

    /// <summary>The pieces of a castle the cart went through: thrown, tumbling, landing on the sand, sliding, then sinking away.</summary>
    private void updateSmash(Castle c, double dt)
    {
        double sy = Math.Max(0.2, c.group.scale.y);
        for (int i = c.falling.Count - 1; i >= 0; i--)
        {
            var f = c.falling[i];
            f.t += dt;
            if (f.t < 0) continue;
            var p = f.pivot;
            if (f.squash)
            {
                // the mound under the wheels flattens and spreads a little, then is gone
                double k = Math.Min(1, f.t / 0.45);
                p.scale.y = Math.Max(0.02, (1 - k) * (p.userData.TryGetValue("sy0", out var s0) ? (double)s0 : (double)(p.userData["sy0"] = p.scale.y)));
                p.scale.x = p.scale.z = 1 + k * 0.25;
                if (k >= 1) { p.visible = false; c.falling.RemoveAt(i); }
                continue;
            }
            if (!f.landed)
            {
                f.v.y -= 9.8 * dt / sy;
                p.position.addScaledVector(f.v, dt);
                p.rotation.x += f.spin.x * dt;
                p.rotation.y += f.spin.y * dt;
                p.rotation.z += f.spin.z * dt;
                // on the sand (the castle's own floor): a dull bounce, then it slides to a stop
                if (p.position.y <= 0 && f.v.y < 0)
                {
                    p.position.y = 0;
                    if (f.v.y < -3)
                    {
                        f.v.y *= -0.25;
                        f.v.x *= 0.5;
                        f.v.z *= 0.5;
                        f.spin.multiplyScalar(0.4);
                    }
                    else f.landed = true;
                }
            }
            else
            {
                double drag = Math.Exp(-dt * 6);
                f.v.x *= drag;
                f.v.z *= drag;
                p.position.x += f.v.x * dt;
                p.position.z += f.v.z * dt;
                f.sink += dt;
                // after a moment on the sand it sinks into it and is gone
                if (f.sink > 0.6)
                {
                    double k = Math.Max(0, 1 - (f.sink - 0.6) / 0.5);
                    p.scale.setScalar(Math.Max(0.001, k));
                    if (k <= 0) { p.visible = false; c.falling.RemoveAt(i); continue; }
                }
            }
            // never longer than a few seconds, wherever it ended up
            if (f.t > 3.5) { p.visible = false; c.falling.RemoveAt(i); }
        }
        if (c.falling.Count == 0) remove(c);
    }

    public List<CastleSave> data() => castles.Where(c => !c.smashed).Select(c => new CastleSave { x = c.x, z = c.z, stage = c.stage, hp = c.hp, seed = c.seed }).ToList();

    public void setAll(IEnumerable<CastleSave> list)
    {
        foreach (var c in castles.ToList()) remove(c);
        foreach (var d in list) add(d.x, d.z, (int)Math.Max(1, Math.Min(3, d.stage)), d.hp, d.seed);
    }
}
