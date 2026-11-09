using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Ecology.ts

public class Crab
{
    public THREE.Group mesh;
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
        group.add(g);
        tufts.Add(g);
    }

    private void addCrab()
    {
        var g = new THREE.Group();
        var body = new THREE.Mesh(CRAB_LOOKS[(int)Math.Floor(JS.Random() * CRAB_LOOKS.Length)], crabMat);
        body.castShadow = true;
        g.scale.setScalar(rand(0.85, 1.25));
        g.add(body);
        double x = rand(-60, 60);
        double z = rand(DIG_MIN_Z + 3, WET_Z - 1);
        g.position.set(x, beach.heightAt(x, z), z);
        group.add(g);
        var hit = new THREE.Mesh(new THREE.SphereGeometry(0.42, 8, 6), new THREE.MeshBasicMaterial { visible = false });
        hit.position.y = 0.12;
        g.add(hit);
        var crab = new Crab { mesh = g, vx = 0, vz = 0, timer = 0, phase = JS.Random() * 6, alive = true, respawn = 0, hit = hit, body = body };
        hit.userData["crab"] = crab;
        crabs.Add(crab);
    }

    /// <summary>Invisible hit spheres of the crabs that are still alive.</summary>
    public List<THREE.Object3D> crabTargets() => crabs.Where(c => c.alive && c.mesh.visible).Select(c => (THREE.Object3D)c.hit).ToList();

    /// <summary>Removes a crab for a while; returns where it was.</summary>
    public THREE.Vector3 shoot(THREE.Object3D obj)
    {
        var crab = obj.userData.TryGetValue("crab", out var o) ? o as Crab : null;
        if (crab == null || !crab.alive) return null;
        crab.alive = false;
        crab.respawn = 25;
        crab.mesh.visible = false;
        return crab.mesh.position.clone();
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
                    c.mesh.position.set(x, beach.heightAt(x, z), z);
                    c.mesh.visible = true;
                    c.alive = true;
                }
                continue;
            }
            // scuttle away from a player who comes too close
            double dxp = c.mesh.position.x - px, dzp = c.mesh.position.z - pz;
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
            var m = c.mesh;
            double nx = m.position.x + c.vx * dt, nz = m.position.z + c.vz * dt;
            if (nz > WET_Z + 1.5 || nz < DIG_MIN_Z + 1 || Math.Abs(nx) > 66)
            {
                c.vx *= -1;
                c.vz *= -1;
                nx = m.position.x;
                nz = m.position.z;
            }
            m.position.set(nx, beach.heightAt(nx, nz), nz);
            // a crab this far off is a speck: do not draw it
            m.visible = dp < 70;
            bool moving = Math.Abs(c.vx) + Math.Abs(c.vz) > 0.01;
            if (moving)
            {
                c.phase += dt * 22;
                m.position.y += Math.Abs(Math.Sin(c.phase)) * 0.012;
                c.body.rotation.z = Math.Sin(c.phase) * 0.05;
                m.rotation.y += (Math.Atan2(c.vx, c.vz) - Math.PI / 2 - m.rotation.y) * Math.Min(1, dt * 4);
            }
        }
        // crabs ignore terrain edits except sinking into holes; keep them from clipping
    }
}
