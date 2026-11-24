using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Ecology.ts

public class Crab
{
    /// <summary>the crab as built (a description), realised once; its GameObjects are posed from the numbers below</summary>
    public THREE.Group mesh;
    public UnityEngine.Transform meshT, bodyT;
    /// <summary>where it is, which way it faces, how its body rocks, its size, and whether it is drawn (unity/UI_PLAN.md B1)</summary>
    public double x, y, z, ry, rock, size;
    public bool shown = true;
    public Target target;
    public double vx, vz, timer, phase;
    public bool alive;
    public double respawn;
    public THREE.Mesh hit;
    /// <summary>the body, which rocks a little as the crab scuttles</summary>
    public THREE.Mesh body;
}

/// <summary>Grass tufts and scuttling crabs that grow with the Beach Ecology Score.</summary>
public class Ecology
{
    private static double rand(double a, double b) => a + JS.Random() * (b - a);

    private static readonly THREE.Vector3 Y_AXIS = new THREE.Vector3(0, 1, 0);

    /// <summary>One colour for a whole piece, so the pieces of a crab can be merged into a single mesh.</summary>
    public static THREE.BufferGeometry paint(THREE.BufferGeometry g, int color)
    {
        g.deleteAttribute("uv");
        var c = new THREE.Color(color);
        int n = g.attributes.position.count;
        var col = new float[n * 3];
        for (int i = 0; i < n; i++) { col[i * 3] = (float)c.r; col[i * 3 + 1] = (float)c.g; col[i * 3 + 2] = (float)c.b; }
        g.setAttribute("color", new THREE.BufferAttribute(col, 3));
        return g;
    }

    /// <summary>A tapering rod between two points: a limb.</summary>
    public static THREE.BufferGeometry limb(THREE.Vector3 a, THREE.Vector3 b, double r, int color)
    {
        var d = b.clone().sub(a);
        var g = new THREE.CylinderGeometry(r * 0.75, r, d.length(), 6, 1);
        g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(Y_AXIS, d.clone().normalize()));
        g.translate((a.x + b.x) / 2, (a.y + b.y) / 2, (a.z + b.z) / 2);
        return paint(g, color);
    }

    /// <summary>
    /// A shore crab, facing +z: a broad domed shell with a paler top, two eyes on stalks, two arms ending in claws with open pincers,
    /// and three bent legs on each side. Built once per shell colour and shared by every crab of that colour.
    /// </summary>
    private static THREE.BufferGeometry crabGeometry(int shell, int light, int leg)
    {
        var parts = new List<THREE.BufferGeometry>();
        THREE.Vector3 v(double x, double y, double z) => new THREE.Vector3(x, y, z);
        var body = new THREE.SphereGeometry(0.15, 18, 12);
        body.scale(1.35, 0.5, 0.95);
        body.translate(0, 0.1, 0);
        parts.Add(paint(body, shell));
        var top = new THREE.SphereGeometry(0.115, 14, 8, 0, Math.PI * 2, 0, Math.PI / 2);
        top.scale(1.3, 0.55, 0.9);
        top.translate(0, 0.115, 0);
        parts.Add(paint(top, light));
        // a row of small points along the front edge of the shell
        for (int i = -2; i <= 2; i++)
        {
            var t = new THREE.ConeGeometry(0.014, 0.04, 5);
            t.rotateX(Math.PI / 2);
            t.translate(i * 0.05, 0.1, 0.135 - Math.Abs(i) * 0.012);
            parts.Add(paint(t, shell));
        }
        foreach (var s in new[] { -1, 1 })
        {
            // eyes on short stalks
            parts.Add(limb(v(s * 0.045, 0.13, 0.1), v(s * 0.055, 0.185, 0.115), 0.01, light));
            var eye = new THREE.SphereGeometry(0.017, 8, 6);
            eye.translate(s * 0.055, 0.195, 0.118);
            parts.Add(paint(eye, 0x141010));
            // an arm with an elbow, and a claw
            var shoulder = v(s * 0.17, 0.1, 0.07);
            var elbow = v(s * 0.26, 0.11, 0.14);
            var wrist = v(s * 0.23, 0.11, 0.23);
            parts.Add(limb(shoulder, elbow, 0.02, shell));
            parts.Add(limb(elbow, wrist, 0.019, shell));
            var palm = new THREE.SphereGeometry(0.052, 10, 8);
            palm.scale(1, 0.7, 1.2);
            palm.translate(s * 0.225, 0.11, 0.265);
            parts.Add(paint(palm, shell));
            foreach (var open in new[] { -1, 1 })
            {
                var f = new THREE.ConeGeometry(0.026, 0.1, 6);
                f.rotateX(Math.PI / 2);
                f.rotateY(open * 0.28 * -s);
                f.translate(s * 0.225 + open * 0.02 * s, 0.11 + open * 0.008, 0.33);
                parts.Add(paint(f, light));
            }
            // three legs, each bent at the knee and ending on the sand
            for (int i = 0; i < 3; i++)
            {
                double z = -0.07 + i * 0.06;
                var knee = v(s * (0.25 + i * 0.01), 0.145, z - 0.01);
                parts.Add(limb(v(s * 0.15, 0.085, z), knee, 0.011, leg));
                parts.Add(limb(knee, v(s * (0.33 + i * 0.012), 0.0, z - 0.035), 0.009, leg));
            }
        }
        var merged = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        merged.computeVertexNormals();
        return merged;
    }

    /// <summary>Reddish-orange, sandy-brown and deep red crabs.</summary>
    private static readonly THREE.BufferGeometry[] CRAB_LOOKS =
    {
        crabGeometry(0xe0643f, 0xf08a5a, 0xc9502f),
        crabGeometry(0xb98a58, 0xd6b183, 0x9c7040),
        crabGeometry(0xc23e2e, 0xdc6a4c, 0x9e2c20),
    };

    public readonly THREE.Group group = new THREE.Group();
    private readonly List<THREE.Object3D> tufts = new List<THREE.Object3D>();
    public readonly List<Crab> crabs = new List<Crab>();
    private readonly THREE.MeshStandardMaterial grassMat = new THREE.MeshStandardMaterial { color = 0x6ea84f, roughness = 0.9 };
    private readonly THREE.MeshStandardMaterial crabMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.65 };
    private readonly Beach beach;

    public Ecology(Beach beach) { this.beach = beach; }

    /// <summary>
    /// Tufts and crabs are realised as they are added, under one root. A crab's place, turn and rock are numbers on the Crab,
    /// and Pose sets its GameObjects from them (unity/UI_PLAN.md B1); the aim and the binoculars do not need the descriptions.
    /// </summary>
    private UnityEngine.Transform root;

    /// <summary>The crabs' and tufts' GameObjects under the world root, once placed.</summary>
    public UnityEngine.Transform Root => root;

    /// <summary>Puts what exists so far into the world; everything added later goes in as it is made.</summary>
    public void Place()
    {
        root = Draw.Group(World.Land);
        foreach (var t in tufts) Realizer.Realize(t, root, World.Layer);
        foreach (var c in crabs) PlaceCrab(c);
    }

    private void PlaceCrab(Crab c)
    {
        var live = Realizer.Realize(c.mesh, root, World.Layer);
        c.meshT = live.root;
        c.bodyT = live[c.body];
    }

    /// <summary>Sets a crab's GameObjects from its numbers. A hidden crab is left where it was, as the scene copy left it.</summary>
    private static void Pose(Crab c)
    {
        if (c.meshT == null) return;
        if (c.meshT.gameObject.activeSelf != c.shown) c.meshT.gameObject.SetActive(c.shown);
        // the body rocks whether or not the crab is drawn (it was posed by itself); the crab moves only while drawn
        c.bodyT.localRotation = GameSpace.Euler(0, 0, c.rock);
        if (!c.shown) return;
        c.meshT.localPosition = GameSpace.V(c.x, c.y, c.z);
        c.meshT.localRotation = GameSpace.Euler(0, c.ry, 0);
    }

    public void setScore(double score)
    {
        double wantTufts = Math.Min(260, score * 4);
        while (tufts.Count < wantTufts) addTuft();
        double wantCrabs = 26 + Math.Min(10, Math.Floor(score / 2));
        while (crabs.Count < wantCrabs) addCrab();
    }

    private void addTuft()
    {
        var g = new THREE.Group();
        int blades = 5 + (int)Math.Floor(JS.Random() * 4);
        for (int i = 0; i < blades; i++)
        {
            var b = new THREE.Mesh(new THREE.ConeGeometry(0.035, rand(0.22, 0.4), 4), grassMat);
            b.position.set(rand(-0.08, 0.08), 0.15, rand(-0.08, 0.08));
            b.rotation.set(rand(-0.4, 0.4), 0, rand(-0.4, 0.4));
            b.castShadow = true;
            g.add(b);
        }
        // near the dunes mostly, and a few mid-beach
        double x = rand(-66, 66);
        double z = JS.Random() < 0.7 ? rand(DIG_MIN_Z - 6, DIG_MIN_Z + 4) : rand(-40, 26);
        if (JS.Hypot(x, z - CAMP_Z) < 13) g.position.set(x, beach.heightAt(x, DIG_MIN_Z + 6), DIG_MIN_Z + 6);
        else g.position.set(x, beach.heightAt(x, z) - 0.02, z);
        // the blades never move inside the tuft (the tuft itself can be moved or removed): the scene copy skips them
        g.syncStatic = true;
        group.add(g);
        tufts.Add(g);
        if (root != null) Realizer.Realize(g, root, World.Layer);
    }

    private void addCrab()
    {
        var g = new THREE.Group();
        var body = new THREE.Mesh(CRAB_LOOKS[(int)Math.Floor(JS.Random() * CRAB_LOOKS.Length)], crabMat);
        body.castShadow = true;
        double size = rand(0.85, 1.25);
        g.scale.setScalar(size);
        g.add(body);
        double x = rand(-60, 60);
        double z = rand(DIG_MIN_Z + 3, WET_Z - 1);
        g.position.set(x, beach.heightAt(x, z), z);
        group.add(g);
        var hit = new THREE.Mesh(new THREE.SphereGeometry(0.42, 8, 6), new THREE.MeshBasicMaterial { visible = false });
        hit.position.y = 0.12;
        g.add(hit);
        var crab = new Crab { mesh = g, vx = 0, vz = 0, timer = 0, phase = JS.Random() * 6, alive = true, respawn = 0, hit = hit, body = body,
            x = g.position.x, y = g.position.y, z = g.position.z, size = size };
        hit.userData["crab"] = crab;
        // the hit sphere sits 0.12 up the crab's middle, scaled with it
        crab.target = new Target(TargetKind.Crab, crab, 0.42, v => v.set(crab.x, crab.y + 0.12 * crab.size, crab.z));
        crabs.Add(crab);
        if (root != null) PlaceCrab(crab);
    }

    /// <summary>Invisible hit spheres of the crabs that are still alive.</summary>
    public List<Target> crabTargets() => crabs.Where(c => c.alive && c.shown).Select(c => c.target).ToList();

    /// <summary>Removes a crab for a while; returns where it was.</summary>
    public THREE.Vector3 shoot(object who)
    {
        var crab = who as Crab;
        if (crab == null || !crab.alive) return null;
        crab.alive = false;
        crab.respawn = 25;
        crab.shown = false;
        Pose(crab);
        return new THREE.Vector3(crab.x, crab.y, crab.z);
    }

    public void update(double dt, double px = 1e9, double pz = 1e9)
    {
        foreach (var c in crabs)
        {
            if (!c.alive)
            {
                c.respawn -= dt;
                if (c.respawn <= 0)
                {
                    double x = rand(-60, 60);
                    double z = rand(DIG_MIN_Z + 3, WET_Z - 1);
                    c.x = x;
                    c.y = beach.heightAt(x, z);
                    c.z = z;
                    c.shown = true;
                    c.alive = true;
                }
                continue;
            }
            // scuttle away from a player who comes too close
            double dxp = c.x - px, dzp = c.z - pz;
            double dp = JS.Hypot(dxp, dzp);
            if (dp < 5)
            {
                c.vx = dxp / JS.Or(dp, 1) * 2.4;
                c.vz = dzp / JS.Or(dp, 1) * 2.4;
                c.timer = 0.5;
            }
            c.timer -= dt;
            if (c.timer <= 0)
            {
                c.timer = rand(0.6, 2.4);
                if (JS.Random() < 0.4)
                {
                    c.vx = 0;
                    c.vz = 0;
                }
                else
                {
                    double a = JS.Random() * Math.PI * 2;
                    double s = rand(0.5, 1.1);
                    c.vx = Math.Cos(a) * s;
                    c.vz = Math.Sin(a) * s * 0.5;
                }
            }
            double nx = c.x + c.vx * dt, nz = c.z + c.vz * dt;
            if (nz > WET_Z + 1.5 || nz < DIG_MIN_Z + 1 || Math.Abs(nx) > 66)
            {
                c.vx *= -1;
                c.vz *= -1;
                nx = c.x;
                nz = c.z;
            }
            c.x = nx;
            c.y = beach.heightAt(nx, nz);
            c.z = nz;
            // a crab this far off is a speck: do not draw it
            c.shown = dp < 70;
            bool moving = Math.Abs(c.vx) + Math.Abs(c.vz) > 0.01;
            if (moving)
            {
                c.phase += dt * 22;
                c.y += Math.Abs(Math.Sin(c.phase)) * 0.012;
                c.rock = Math.Sin(c.phase) * 0.05;
                c.ry += (Math.Atan2(c.vx, c.vz) - Math.PI / 2 - c.ry) * Math.Min(1, dt * 4);
            }
        }
        // crabs ignore terrain edits except sinking into holes; keep them from clipping
        foreach (var c in crabs) Pose(c);
    }
}
