using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Dog.ts

public class DogEnv
{
    public double playerX, playerZ;
    /// <summary>keeps the dog out of palms, camp props and the sea</summary>
    public Func<double, double, (double x, double z)> resolve;
    /// <summary>distance from the player, for the volume</summary>
    public Action<double> bark;
    public Action<THREE.Vector3> dust;
    /// <summary>leaves a paw print in the sand</summary>
    public Action<double, double, double, bool> print;
}

/// <summary>The camp dog. It sits by the camp until given a bone, then follows its nose to a buried find and barks until the player digs it up.</summary>
public class Dog
{
    private class Waypoint
    {
        public double x, z;
        /// <summary>seconds to stop and sniff the ground here</summary>
        public double sniff;
    }

    private const double TURN = 5;
    private const double TROT = 5.2;
    private const double SNIFF_SPEED = 3.6;
    private const double GIVE_UP = 300;

    /// <summary>One cross-section of a smooth body part: where it is along the axis, how far its centre is shifted, and its two half-sizes.</summary>
    private struct Ring
    {
        public double t, c, a, b;
        public Ring(double t, double c, double a, double b) { this.t = t; this.c = c; this.a = a; this.b = b; }
    }

    private static double catmull(double p0, double p1, double p2, double p3, double u) =>
        0.5 * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u * u * u);

    /// <summary>
    /// A smooth, organic tube through a list of cross-sections (like lofting in a modelling program), so a body, a leg or an ear is one
    /// continuous surface instead of a pile of balls. `axis` z runs front to back (rings are in the x-y plane, `c` shifts them up);
    /// axis y runs top to bottom (rings are in the x-z plane, `c` shifts them toward the back). Colours blend from `low` to `high` across the
    /// ring, which gives the fur a lighter underside for free.
    /// </summary>
    private static THREE.BufferGeometry loft(char axis, Ring[] keys, int low, int high, int steps = 6, int around = 22)
    {
        var rows = new List<Ring>();
        int n = keys.Length;
        for (int i = 0; i < n - 1; i++)
        {
            var k0 = keys[Math.Max(0, i - 1)];
            var k1 = keys[i];
            var k2 = keys[i + 1];
            var k3 = keys[Math.Min(n - 1, i + 2)];
            for (int j = 0; j < steps; j++)
            {
                double u = (double)j / steps;
                rows.Add(new Ring(
                    catmull(k0.t, k1.t, k2.t, k3.t, u),
                    catmull(k0.c, k1.c, k2.c, k3.c, u),
                    Math.Max(0.002, catmull(k0.a, k1.a, k2.a, k3.a, u)),
                    Math.Max(0.002, catmull(k0.b, k1.b, k2.b, k3.b, u))));
            }
        }
        rows.Add(keys[n - 1]);
        var pos = new List<double>();
        var col = new List<double>();
        var cLow = new THREE.Color(low);
        var cHigh = new THREE.Color(high);
        var tmp = new THREE.Color();
        foreach (var r in rows)
        {
            for (int k = 0; k < around; k++)
            {
                double th = (double)k / around * Math.PI * 2;
                double ca = Math.Cos(th);
                double sb = Math.Sin(th);
                if (axis == 'z') { pos.Add(r.a * ca); pos.Add(r.c + r.b * sb); pos.Add(r.t); }
                else { pos.Add(r.a * ca); pos.Add(r.t); pos.Add(r.c + r.b * sb); }
                double m = Math.Min(1, Math.Max(0, (sb + 0.55) / 1.4));
                tmp.copy(cLow).lerp(cHigh, m * m * (3 - 2 * m));
                col.Add(tmp.r); col.Add(tmp.g); col.Add(tmp.b);
            }
        }
        var idx = new List<int>();
        for (int i = 0; i < rows.Count - 1; i++)
        {
            for (int k = 0; k < around; k++)
            {
                int a0 = i * around + k;
                int a1 = i * around + (k + 1) % around;
                int b0 = (i + 1) * around + k;
                int b1 = (i + 1) * around + (k + 1) % around;
                idx.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
            }
        }
        // close both ends with a fan to the centre of the ring
        foreach (var end in new[] { 0, rows.Count - 1 })
        {
            var r = rows[end];
            int centre = pos.Count / 3;
            if (axis == 'z') { pos.Add(0); pos.Add(r.c); pos.Add(r.t); }
            else { pos.Add(0); pos.Add(r.t); pos.Add(r.c); }
            col.Add(col[end * around * 3]); col.Add(col[end * around * 3 + 1]); col.Add(col[end * around * 3 + 2]);
            for (int k = 0; k < around; k++) idx.AddRange(new[] { centre, end * around + k, end * around + (k + 1) % around });
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        geo.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        geo.setIndex(idx);
        geo.computeVertexNormals();
        return geo;
    }

    /// <summary>The three bones of a leg. The fur over them is one skinned surface, so a leg bends instead of coming apart at the joints.</summary>
    private class Leg
    {
        public THREE.Bone top, knee, paw;
    }

    /// <summary>How far below the hip the knee sits, and how far below the knee the ankle sits.</summary>
    private const double THIGH = 0.228;
    private const double SHIN = 0.24;

    /// <summary>0 above a joint, 1 below it, with a smooth blend over `width` either side.</summary>
    private static double jointBlend(double y, double joint, double width)
    {
        double u = Math.Min(1, Math.Max(0, (joint + width - y) / (2 * width)));
        return u * u * (3 - 2 * u);
    }

    /// <summary>A shoulder or haunch that starts thin inside the body and swells out through the flank, so there is no edge where the leg begins.</summary>
    private static readonly Ring[] FRONT_LEG =
    {
        new Ring(0.15, 0.0, 0.012, 0.03),
        new Ring(0.1, 0.0, 0.045, 0.085),
        new Ring(0.03, 0.0, 0.085, 0.125),
        new Ring(-0.07, 0.0, 0.075, 0.105),
        new Ring(-0.16, 0.008, 0.056, 0.074),
        new Ring(-THIGH, 0.0, 0.045, 0.054),
        new Ring(-0.31, 0.0, 0.039, 0.045),
        new Ring(-0.4, 0.0, 0.036, 0.041),
        new Ring(-0.44, -0.004, 0.04, 0.046),
        new Ring(-0.456, -0.02, 0.05, 0.066),
        new Ring(-0.47, -0.026, 0.052, 0.075),
        new Ring(-0.482, -0.026, 0.036, 0.052),
    };

    private static readonly Ring[] HIND_LEG =
    {
        new Ring(0.17, 0.0, 0.012, 0.03),
        new Ring(0.12, 0.005, 0.05, 0.095),
        new Ring(0.05, 0.015, 0.09, 0.125),
        new Ring(-0.05, 0.025, 0.082, 0.115),
        new Ring(-0.15, 0.02, 0.06, 0.08),
        new Ring(-THIGH, 0.0, 0.046, 0.054),
        new Ring(-0.31, 0.0, 0.039, 0.045),
        new Ring(-0.4, 0.0, 0.036, 0.041),
        new Ring(-0.44, -0.004, 0.04, 0.046),
        new Ring(-0.456, -0.02, 0.05, 0.066),
        new Ring(-0.47, -0.026, 0.052, 0.075),
        new Ring(-0.482, -0.026, 0.036, 0.052),
    };

    public readonly THREE.Group group = new THREE.Group();
    /// <summary>'rest' | 'run' | 'find' | 'return'</summary>
    public string state = "rest";
    public double x = 0, z = 0, heading = 0;
    /// <summary>the find the dog is working on, if any</summary>
    public BuriedItem item;

    private (double x, double z, double heading) home = (0, 0, 0);
    private double speed = 0, gait = 0, sit = 0, lie = 1;
    /// <summary>true while the player is close enough that the dog sits up; it lies down again when they leave</summary>
    private bool wantSit = false;
    /// <summary>seconds left of the happy reaction to being petted</summary>
    private double loveT = 0;
    private double dig = 0, sniffAmt = 0, kick = 0, barkT = 0, dustT = 0, workT = 0, pauseT = 0, pathT = 0, stride = 0;
    private bool stepSide = false;
    private List<Waypoint> path = new List<Waypoint>();
    /// <summary>where the dog stands while it works: in front of the find, facing it, never on top of it</summary>
    private (double x, double z) stand = (0, 0);
    private readonly THREE.Group bodyPivot = new THREE.Group();
    private readonly THREE.Group headPivot = new THREE.Group();
    private readonly THREE.Group jaw = new THREE.Group();
    private readonly THREE.Mesh tongue;
    private readonly List<THREE.Group> ears = new List<THREE.Group>();
    private readonly List<THREE.Group> tail = new List<THREE.Group>();
    private readonly List<Leg> front = new List<Leg>();
    private readonly List<Leg> hind = new List<Leg>();
    private readonly THREE.Vector3 tmp = new THREE.Vector3();
    private readonly Beach beach;

    public Dog(Beach beach)
    {
        this.beach = beach;
        tongue = new THREE.Mesh(new THREE.SphereGeometry(0.03, 8, 6), new THREE.MeshStandardMaterial { color = 0xe0707a, roughness = 0.6 });
        build();
    }

    private void build()
    {
        // a golden retriever: smooth golden coat, lighter underneath and on the feathering, a broad head, long muzzle, soft hanging ears
        const int GOLD = 0xd39a45;
        const int PALE = 0xf0cd8a;
        const int DEEP = 0xb9792b;
        THREE.MeshStandardMaterial coat() => new THREE.MeshStandardMaterial { color = 0xffffff, vertexColors = true, roughness = 0.92, side = THREE.Side.DoubleSide };
        var furMat = coat();
        var earMat = coat();
        var deepMat = new THREE.MeshStandardMaterial { color = DEEP, roughness = 0.95 };
        var black = new THREE.MeshStandardMaterial { color = 0x17120e, roughness = 0.3 };
        var brown = new THREE.MeshStandardMaterial { color = 0x3a2412, roughness = 0.3 };
        var shine = new THREE.MeshBasicMaterial { color = 0xffffff };
        THREE.Mesh mesh(THREE.Object3D parent, THREE.BufferGeometry geo, THREE.Material mat, double px = 0, double py = 0, double pz = 0)
        {
            var m = new THREE.Mesh(geo, mat);
            m.position.set(px, py, pz);
            m.castShadow = true;
            parent.add(m);
            return m;
        }
        THREE.Mesh ball(THREE.Object3D parent, double r, THREE.Material mat, double px, double py, double pz, double sx = 1, double sy = 1, double sz = 1)
        {
            var m = mesh(parent, new THREE.SphereGeometry(r, 16, 12), mat, px, py, pz);
            m.scale.set(sx, sy, sz);
            return m;
        }

        // ---- torso and neck in one smooth piece: rump, level back, a deep chest, then the neck rising into the head
        bodyPivot.position.set(0, 0.43, 0.22);
        group.add(bodyPivot);
        var b = bodyPivot;
        mesh(b, loft('z', new[]
        {
            new Ring(0.22, 0.1, 0.04, 0.05),
            new Ring(0.16, 0.11, 0.13, 0.14),
            new Ring(0.04, 0.12, 0.17, 0.18),
            new Ring(-0.14, 0.12, 0.165, 0.17),
            new Ring(-0.3, 0.1, 0.17, 0.185),
            new Ring(-0.46, 0.07, 0.19, 0.215),
            new Ring(-0.6, 0.1, 0.165, 0.2),
            new Ring(-0.72, 0.2, 0.115, 0.15),
            new Ring(-0.8, 0.28, 0.1, 0.12),
        }, PALE, GOLD), furMat);
        // a fluffy ruff on the chest
        mesh(b, loft('z', new[]
        {
            new Ring(-0.3, -0.02, 0.05, 0.035),
            new Ring(-0.46, -0.06, 0.11, 0.07),
            new Ring(-0.6, -0.03, 0.09, 0.07),
            new Ring(-0.7, 0.06, 0.06, 0.06),
        }, PALE, PALE, 5, 16), furMat);

        // ---- head: a broad skull, a clear stop, a long square muzzle and a big black nose
        headPivot.position.set(0, 0.27, -0.76);
        b.add(headPivot);
        var h = headPivot;
        mesh(h, loft('z', new[]
        {
            new Ring(0.07, 0.03, 0.075, 0.085),
            new Ring(-0.01, 0.04, 0.105, 0.105),
            new Ring(-0.09, 0.035, 0.1, 0.1),
            new Ring(-0.15, 0.0, 0.066, 0.062),
            new Ring(-0.25, -0.015, 0.062, 0.055),
            new Ring(-0.32, -0.02, 0.052, 0.05),
            new Ring(-0.345, -0.02, 0.03, 0.034),
        }, PALE, GOLD), furMat);
        ball(h, 0.036, black, 0, -0.012, -0.345, 1.15, 0.85, 0.9);
        // lower jaw: opens a little to bark or pant
        jaw.position.set(0, -0.045, -0.1);
        h.add(jaw);
        mesh(jaw, loft('z', new[]
        {
            new Ring(0.0, -0.01, 0.045, 0.02),
            new Ring(-0.1, -0.012, 0.05, 0.02),
            new Ring(-0.2, -0.005, 0.04, 0.018),
        }, PALE, PALE, 4, 14), furMat);
        tongue.position.set(0, 0.0, -0.1);
        tongue.scale.set(0.9, 0.3, 2.2);
        jaw.add(tongue);
        tongue.visible = false;
        foreach (var s in new[] { -1, 1 })
        {
            ball(h, 0.02, brown, s * 0.064, 0.055, -0.115);
            ball(h, 0.0055, shine, s * 0.06, 0.063, -0.131);
            ball(h, 0.018, deepMat, s * 0.06, 0.088, -0.11, 1.5, 0.6, 1.0);
            // soft ears that hang from the top of the head, close to the cheeks
            var ear = new THREE.Group();
            ear.position.set(s * 0.09, 0.075, 0.015);
            ear.rotation.z = s * 0.22;
            mesh(ear, loft('y', new[]
            {
                new Ring(0.0, 0.0, 0.03, 0.05),
                new Ring(-0.06, 0.0, 0.02, 0.07),
                new Ring(-0.12, 0.0, 0.012, 0.07),
                new Ring(-0.17, 0.0, 0.006, 0.04),
            }, GOLD, DEEP, 5, 16), earMat);
            h.add(ear);
            ears.Add(ear);
        }

        // ---- legs: hip to toe is one skinned surface over three bones; its colour runs from the flank's gold to pale paws
        var flank = new THREE.Color(PALE).lerp(new THREE.Color(GOLD), 0.42);
        var toe = new THREE.Color(PALE);
        var tint = new THREE.Color();
        Leg makeLeg(double lx, double ly, double lz, Ring[] keys)
        {
            var geo = loft('y', keys, PALE, PALE, 5, 18);
            var pos = geo.getAttribute("position");
            var col = geo.getAttribute("color");
            var index = new List<int>();
            var weight = new List<double>();
            for (int i = 0; i < pos.count; i++)
            {
                double vy = pos.getY(i);
                double k = jointBlend(vy, -THIGH, 0.05);
                double a = jointBlend(vy, -THIGH - SHIN, 0.03);
                index.AddRange(new[] { 0, 1, 2, 0 });
                weight.AddRange(new[] { 1 - k, k * (1 - a), k * a, 0 });
                tint.copy(toe).lerp(flank, Math.Min(1, Math.Max(0, (vy + 0.42) / 0.42)));
                col.setXYZ(i, tint.r, tint.g, tint.b);
            }
            geo.setAttribute("skinIndex", new THREE.Uint16BufferAttribute(index, 4));
            geo.setAttribute("skinWeight", new THREE.Float32BufferAttribute(weight, 4));
            var top = new THREE.Bone();
            var knee = new THREE.Bone();
            var paw = new THREE.Bone();
            top.position.set(lx, ly, lz);
            knee.position.y = -THIGH;
            paw.position.y = -SHIN;
            top.add(knee);
            knee.add(paw);
            var skin = new THREE.SkinnedMesh(geo, furMat);
            skin.position.set(lx, ly, lz);
            skin.castShadow = true;
            skin.frustumCulled = false;
            b.add(top, skin);
            group.updateMatrixWorld(true);
            skin.bind(new THREE.Skeleton(new[] { top, knee, paw }));
            return new Leg { top = top, knee = knee, paw = paw };
        }
        foreach (var s in new[] { -1, 1 })
        {
            front.Add(makeLeg(s * 0.11, 0.05, -0.44, FRONT_LEG));
            hind.Add(makeLeg(s * 0.1, 0.05, 0.02, HIND_LEG));
        }

        // ---- tail: a long, feathered plume that is slim at the root, full in the middle and tapers to a point
        THREE.Object3D parent = b;
        var lens = new[] { 0.15, 0.15, 0.14 };
        var shapes = new[]
        {
            new[] { new Ring(0, 0, 0.04, 0.045), new Ring(0.15, 0, 0.045, 0.06) },
            new[] { new Ring(0, 0, 0.045, 0.06), new Ring(0.15, -0.01, 0.04, 0.085) },
            new[] { new Ring(0, -0.01, 0.04, 0.085), new Ring(0.07, -0.02, 0.03, 0.075), new Ring(0.14, -0.01, 0.006, 0.02) },
        };
        for (int i = 0; i < 3; i++)
        {
            var seg = new THREE.Group();
            seg.position.set(0, i == 0 ? 0.14 : lens[i - 1], i == 0 ? 0.2 : 0);
            mesh(seg, loft('y', shapes[i], GOLD, PALE, 4, 16), furMat);
            parent.add(seg);
            parent = seg;
            tail.Add(seg);
        }
    }

    /// <summary>The Golden Bone from the Luxury shop: the dog carries it in its mouth from now on.</summary>
    public void setGoldenBone()
    {
        if (jaw.getObjectByName("golden-bone") != null) return;
        var gold = new THREE.MeshStandardMaterial { color = 0xf0c24a, roughness = 0.3, metalness = 0.75 };
        var bone = new THREE.Group { name = "golden-bone" };
        var shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.02, 0.2, 8), gold);
        shaft.rotation.z = Math.PI / 2;
        bone.add(shaft);
        foreach (var sx in new[] { -1, 1 })
        foreach (var sz in new[] { -1, 1 })
        {
            var knob = new THREE.Mesh(new THREE.SphereGeometry(0.03, 8, 6), gold);
            knob.position.set(sx * 0.11, 0, sz * 0.02);
            bone.add(knob);
        }
        bone.position.set(0, 0.01, -0.13);
        jaw.add(bone);
    }

    public bool away => state != "rest";

    public void setHome(double hx, double hz, double hheading)
    {
        home = (hx, hz, hheading);
        if (state == "rest")
        {
            x = hx;
            z = hz;
            heading = hheading;
            place();
        }
    }

    /// <summary>Works out a wandering route from the dog to the find: weaving to either side, with a few stops to sniff the ground.</summary>
    private List<Waypoint> buildPath(BuriedItem item)
    {
        double sx = x, sz = z;
        double dx = stand.x - sx, dz = stand.z - sz;
        double dist = JS.Hypot(dx, dz);
        double ux = dx / JS.Or(dist, 1), uz = dz / JS.Or(dist, 1);
        double rx = -uz, rz = ux;
        int n = Math.Max(1, (int)Math.Floor(dist / 5.5));
        var list = new List<Waypoint>();
        double side = JS.Random() < 0.5 ? -1 : 1;
        for (int i = 1; i <= n; i++)
        {
            double along = (double)i / (n + 1) * dist;
            double off = side * (1.6 + JS.Random() * 3.4) * Math.Min(1, dist / 14);
            side = -side;
            double wx = Math.Max(-(HALF - 3), Math.Min(HALF - 3, sx + ux * along + rx * off));
            double wz = Math.Min(SHORE_Z - 4, Math.Max(-(HALF - 3), sz + uz * along + rz * off));
            list.Add(new Waypoint { x = wx, z = wz, sniff = JS.Random() < 0.5 ? 0.6 + JS.Random() * 0.9 : 0 });
        }
        list.Add(new Waypoint { x = stand.x, z = stand.z, sniff = 0 });
        return list;
    }

    /// <summary>Sends the dog after a buried find. `instant` puts it straight at the spot (used when a save is loaded).</summary>
    public void startMission(BuriedItem it, bool instant = false)
    {
        item = it;
        // stand about a metre short of the find, on the side the dog comes from
        double fromX = instant ? home.x : x;
        double fromZ = instant ? home.z : z;
        double ax = fromX - it.x, az = fromZ - it.z;
        double al = JS.Or(JS.Hypot(ax, az), 1);
        stand = (it.x + ax / al * 1.0, it.z + az / al * 1.0);
        workT = 0;
        kick = 1;
        pauseT = 0;
        if (instant)
        {
            path = new List<Waypoint>();
            x = stand.x;
            z = stand.z;
            heading = Math.Atan2(-(it.x - x), -(it.z - z));
            state = "find";
            barkT = 2;
        }
        else
        {
            path = buildPath(it);
            state = "run";
        }
    }

    /// <summary>The player gives the dog some love: it sits up, shakes its head and wags.</summary>
    public void pet()
    {
        if (state != "rest") return;
        loveT = 2.4;
        kick = 0.6;
    }

    /// <summary>The find is done: trot back to the spot by the camp.</summary>
    public void recall()
    {
        item = null;
        path = new List<Waypoint>();
        pauseT = 0;
        state = "return";
    }

    private void place()
    {
        double ground = beach.heightAt(x, z);
        double bob = speed > 0.5 ? Math.Abs(Math.Sin(gait)) * 0.03 * Math.Min(1, speed / TROT) : 0;
        group.position.set(x, ground - sit * 0.25 - lie * 0.34 + bob, z);
        group.rotation.y = heading;
    }

    public void update(double dt, double t, DogEnv env)
    {
        pathT += dt;
        loveT = Math.Max(0, loveT - dt);
        // where is the dog headed?
        double tx = home.x, tz = home.z;
        if (state == "run")
        {
            var wp = path.Count > 0 ? path[0] : null;
            if (wp != null) { tx = wp.x; tz = wp.z; }
            else if (item != null) { tx = stand.x; tz = stand.z; }
        }
        bool moving = state == "run" || state == "return";
        bool sniffing = false;
        if (moving)
        {
            double dx = tx - x, dz = tz - z;
            double d = JS.Hypot(dx, dz);
            if (pauseT > 0)
            {
                // stopped with its nose to the sand
                pauseT -= dt;
                sniffing = true;
                speed *= Math.Exp(-dt * 8);
                heading += Math.Sin(t * 3.1) * dt * 0.8;
            }
            else
            {
                // trailing a scent: a slow, weaving walk with the nose down; heading home it trots with its head up
                bool trail = state == "run";
                double want = Math.Atan2(-dx, -dz) + (trail ? Math.Sin(pathT * 2.3) * 0.28 : 0);
                double diff = want - heading;
                diff = Math.Atan2(Math.Sin(diff), Math.Cos(diff));
                heading += Math.Max(-TURN * dt, Math.Min(TURN * dt, diff));
                double top = trail ? SNIFF_SPEED : TROT;
                double slow = Math.Min(1, d / 2.5);
                speed += (top * Math.Max(0.4, slow) - speed) * Math.Min(1, dt * 4);
                x += -Math.Sin(heading) * speed * dt;
                z += -Math.Cos(heading) * speed * dt;
                var r = env.resolve(x, z);
                x = r.x;
                z = r.z;
                gait += dt * (5 + speed * 2.6);
                sniffing = trail;
                // a pair of paw prints every stride: a front paw ahead and the opposite hind paw behind
                stride += speed * dt;
                if (stride > 0.36 && speed > 0.8)
                {
                    stride = 0;
                    stepSide = !stepSide;
                    int side = stepSide ? 1 : -1;
                    double fx = -Math.Sin(heading), fz = -Math.Cos(heading);
                    double rx = Math.Cos(heading), rz = -Math.Sin(heading);
                    env.print(x + fx * 0.32 + rx * 0.075 * side, z + fz * 0.32 + rz * 0.075 * side, heading, side < 0);
                    env.print(x - fx * 0.26 - rx * 0.075 * side, z - fz * 0.26 - rz * 0.075 * side, heading, side > 0);
                }
            }
            if (d < (state == "run" && path.Count > 1 ? 0.9 : 0.5) && pauseT <= 0)
            {
                if (state == "run")
                {
                    Waypoint wp = null;
                    if (path.Count > 0) { wp = path[0]; path.RemoveAt(0); }
                    if (wp != null && wp.sniff > 0) pauseT = wp.sniff;
                    if (path.Count == 0)
                    {
                        state = "find";
                        barkT = 0.6;
                        workT = 0;
                    }
                }
                else
                {
                    state = "rest";
                    x = home.x;
                    z = home.z;
                }
            }
        }
        else
        {
            speed *= Math.Exp(-dt * 8);
            if (state == "rest")
            {
                // sit and face the player when they come near, the fire otherwise
                double dx = env.playerX - x, dz = env.playerZ - z;
                // lie by the fire; sit up and look at the player once they come close, and settle again when they leave
                double dist = JS.Hypot(dx, dz);
                if (dist < 5.5 || loveT > 0) wantSit = true;
                else if (dist > 8) wantSit = false;
                double want = wantSit ? Math.Atan2(-dx, -dz) : home.heading;
                double diff = want - heading;
                diff = Math.Atan2(Math.Sin(diff), Math.Cos(diff));
                heading += diff * Math.Min(1, dt * 1.6);
            }
        }

        // digging and barking at the find
        bool digging = false;
        if (state == "find" && item != null)
        {
            workT += dt;
            digging = !item.revealed;
            // face the find, paws toward it
            double face = Math.Atan2(-(item.x - x), -(item.z - z)) - heading;
            face = Math.Atan2(Math.Sin(face), Math.Cos(face));
            heading += face * Math.Min(1, dt * 5);
            double k = Math.Min(1, dt * 2.5);
            x += (stand.x - x) * k;
            z += (stand.z - z) * k;
            if (digging)
            {
                barkT -= dt;
                if (barkT <= 0)
                {
                    barkT = 3.2 + JS.Random() * 1.2;
                    kick = 1;
                    env.bark(JS.Hypot(env.playerX - x, env.playerZ - z));
                }
                dustT -= dt;
                if (dustT <= 0)
                {
                    dustT = 0.35;
                    env.dust(tmp.set(item.x, beach.heightAt(item.x, item.z) + 0.05, item.z));
                }
            }
            // nobody came: it gives up after a while and goes home
            if (workT > GIVE_UP) recall();
        }

        pose(dt, t, env, digging, sniffing);
        place();
    }

    /// <summary>Sets every joint for the current state: sitting, trotting, sniffing, digging or barking.</summary>
    private void pose(double dt, double t, DogEnv env, bool digging, bool sniffing)
    {
        bool resting = state == "rest";
        sit += ((resting && wantSit ? 1 : 0) - sit) * Math.Min(1, dt * 3.5);
        lie += ((resting && !wantSit ? 1 : 0) - lie) * Math.Min(1, dt * 3);
        double L = lie;
        double love = Math.Min(1, loveT * 2);
        double shake = loveT > 1.5 ? 1 : Math.Max(0, (loveT - 0.6) / 0.9);
        dig += ((digging ? 1 : 0) - dig) * Math.Min(1, dt * 6);
        sniffAmt += ((sniffing ? 1 : 0) - sniffAmt) * Math.Min(1, dt * 5);
        kick = Math.Max(0, kick - dt * 4);
        double s = sit;
        double run = Math.Min(1, speed / TROT);
        double p = Math.Sin(gait);
        double c = Math.Cos(gait);
        double scratch = Math.Sin(t * 20) * 0.9 * dig;

        bodyPivot.rotation.x = s * 0.95 + L * 0.1 - dig * 0.2 + run * 0.03 + Math.Sin(gait * 2) * 0.02 * run;
        // front legs hang straight when sitting (cancelling the tilt of the body) and swing and fold when walking
        double fSwing = p * 0.75 * run;
        for (int i = 0; i < front.Count; i++)
        {
            var leg = front[i];
            double sign = i == 0 ? 1 : -1;
            // lying: the front legs stretch out ahead along the sand
            leg.top.rotation.x = -s * 0.95 + L * 1.4 + dig * 0.2 + fSwing * sign + scratch * sign;
            leg.knee.rotation.x = -Math.Max(0, c * sign) * 0.9 * run - Math.Max(0, dig * Math.Sin(t * 20) * sign) * 0.8 - L * 0.15;
            leg.paw.rotation.x = L * 0.3;
            leg.top.rotation.z = 0;
        }
        // hind legs fold under the dog when sitting: thigh forward, shin back down, paw flat on the ground
        double hSwing = p * 0.7 * run;
        for (int i = 0; i < hind.Count; i++)
        {
            var leg = hind[i];
            double sign = i == 0 ? -1 : 1;
            // lying: the hind legs fold under and out to the sides
            leg.top.rotation.x = s * 0.62 + L * 1.15 + hSwing * sign + 0.1;
            leg.knee.rotation.x = -s * 1.5 - L * 2.0 + Math.Max(0, c * -sign) * 0.8 * run - 0.15;
            leg.paw.rotation.x = s * 1.35 + L * 0.9;
            leg.top.rotation.z = L * (i == 0 ? -0.4 : 0.4);
        }

        // head: level when sitting, nose near the sand when sniffing, dipped at the dig, jerked up on a bark
        double bearing = Math.Atan2(-(env.playerX - x), -(env.playerZ - z)) - heading;
        double rel = Math.Atan2(Math.Sin(bearing), Math.Cos(bearing));
        double look = resting && sit > 0.6 && JS.Hypot(env.playerX - x, env.playerZ - z) < 9 ? Math.Max(-0.7, Math.Min(0.7, rel * 0.6)) : 0;
        double nose = Math.Sin(t * 13) * 0.07 * sniffAmt;
        headPivot.rotation.x = -s * 0.6 - L * 0.12 - dig * 0.55 - sniffAmt * 0.95 + nose + kick * 0.5 + love * 0.2;
        double headTurn = look + Math.Sin(t * 2.1) * 0.25 * sniffAmt + Math.Sin(t * 17) * 0.42 * shake;
        headPivot.rotation.y += (headTurn - headPivot.rotation.y) * Math.Min(1, dt * (loveT > 0 ? 22 : 5));
        // mouth: open on a bark, panting while it works
        double pant = resting ? 0.0 : 0.5 + Math.Sin(t * 14) * 0.15;
        jaw.rotation.x = JS.Max(kick * 0.55, pant * 0.35 * (1 - sniffAmt * 0.6), love * 0.22);
        tongue.visible = (!resting && sniffAmt < 0.5) || love > 0.3;
        // ears flop with the stride
        for (int i = 0; i < ears.Count; i++) ears[i].rotation.x = Math.Sin(gait * 2 + i) * 0.18 * run + kick * 0.25;
        // tail: lazy wag at rest, fast and loose when busy; each segment lags the one before
        double wag = loveT > 0 ? 19 : resting ? 3.5 : 15;
        double amp = loveT > 0 ? 0.75 : resting ? 0.3 : 0.5;
        for (int i = 0; i < tail.Count; i++)
        {
            var seg = tail[i];
            seg.rotation.z = Math.Sin(t * wag - i * 0.7) * amp * (1 - i * 0.2);
            seg.rotation.x = i == 0 ? 0.7 - run * 0.35 : 0.2;
        }
    }
}
