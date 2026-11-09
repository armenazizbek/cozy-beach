using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Grass.ts (the sway patch is in Resources/Shaders/CozyGrass.shader)

/// <summary>A very small amount of grass: a few clumps, mostly where the sand meets the dunes. It sways a little in the wind.</summary>
public class Grass
{
    /// <summary>One tuft of beach grass: a fan of tapering blades that lean outward, dark at the root and lighter at the tip.</summary>
    private static THREE.BufferGeometry tuftGeometry()
    {
        var pos = new List<double>();
        var col = new List<double>();
        var nrm = new List<double>();
        var root = new THREE.Color(0x93ae4c);
        var tip = new THREE.Color(0xe0dd8c);
        const int blades = 9;
        for (int i = 0; i < blades; i++)
        {
            double a = (double)i / blades * Math.PI * 2 + (i % 2) * 0.4;
            double off = 0.02 + (i % 3) * 0.035;
            double h = 0.26 + ((i * 37) % 10) * 0.028;
            const double w = 0.026;
            double lean = 0.07 + ((i * 13) % 7) * 0.02;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            // the blade lies in a plane through its root, leaning away from the centre of the tuft
            double rootX = ca * off, rootZ = sa * off;
            double sx = -sa * w, sz = ca * w;
            const double mid = 0.55;
            var verts = new (double x, double y, double z, THREE.Color c)[]
            {
                (rootX - sx, 0, rootZ - sz, root),
                (rootX + sx, 0, rootZ + sz, root),
                (rootX - sx * 0.6 + ca * lean * 0.35, h * mid, rootZ - sz * 0.6 + sa * lean * 0.35, root.clone().lerp(tip, 0.5)),
                (rootX + sx * 0.6 + ca * lean * 0.35, h * mid, rootZ + sz * 0.6 + sa * lean * 0.35, root.clone().lerp(tip, 0.5)),
                (rootX + ca * lean, h, rootZ + sa * lean, tip),
            };
            // every blade is added facing both ways, with its normal pointing up, so it is lit like the sand rather than going dark from behind
            void tri(int p, int q, int r)
            {
                foreach (var order in new[] { new[] { p, q, r }, new[] { p, r, q } })
                foreach (var k in order)
                {
                    pos.Add(verts[k].x); pos.Add(verts[k].y); pos.Add(verts[k].z);
                    col.Add(verts[k].c.r); col.Add(verts[k].c.g); col.Add(verts[k].c.b);
                    nrm.Add(0); nrm.Add(1); nrm.Add(0);
                }
            }
            tri(0, 1, 2);
            tri(1, 3, 2);
            tri(2, 3, 4);
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        g.setAttribute("normal", new THREE.Float32BufferAttribute(nrm, 3));
        return g;
    }

    private class Tuft
    {
        public double x, z;
        public bool alive;
    }

    public readonly THREE.Group group = new THREE.Group();
    private THREE.InstancedMesh mesh;
    private List<Tuft> tufts = new List<Tuft>();
    private readonly THREE.Matrix4 zero = new THREE.Matrix4().makeScale(0, 0, 0);
    private readonly THREE.Uniform uTime = new THREE.Uniform(0.0);
    private readonly THREE.MeshLambertMaterial mat;
    private readonly THREE.BufferGeometry geo = tuftGeometry();
    private readonly Beach beach;

    public Grass(Beach beach)
    {
        this.beach = beach;
        mat = new THREE.MeshLambertMaterial { vertexColors = true, emissive = 0x3d4a1c };
        mat.customShader = "Cozy/Grass";
        mat.customUniforms["uTime"] = uTime;
    }

    /// <summary>Plants the clumps, keeping every tuft a metre or so clear of buried finds so the grass never hides one.</summary>
    public void scatter(List<Spot> keepClear)
    {
        if (mesh != null)
        {
            group.remove(mesh);
            mesh.dispose();
            mesh = null;
        }
        bool ok(double x, double z)
        {
            if (Math.Abs(x) > HALF - 3 || z > SHORE_Z - 8) return false;
            if (beach.heightAt(x, z) < WATER_LEVEL + 0.5) return false;
            return !keepClear.Any(k => JS.Hypot(k.x - x, k.z - z) < k.r + 1.1);
        }
        var list = new List<(double x, double z, double s, double a)>();
        void clump(double cx, double cz, int n, double spread)
        {
            for (int i = 0; i < n; i++)
            {
                double a = JS.Random() * Math.PI * 2;
                double d = JS.Random() * spread;
                double x = cx + Math.Cos(a) * d, z = cz + Math.Sin(a) * d;
                if (ok(x, z)) list.Add((x, z, 0.8 + JS.Random() * 0.7, JS.Random() * Math.PI * 2));
            }
        }
        // most of it on the dry sand toward the dunes, a little further down the beach
        for (int c = 0, tries = 0; c < 6 && tries < 200; tries++)
        {
            double x = (JS.Random() - 0.5) * 2 * (HALF - 10);
            double z = -46 + JS.Random() * 10;
            if (!ok(x, z)) continue;
            clump(x, z, 5 + (int)Math.Floor(JS.Random() * 4), 1.1);
            c++;
        }
        for (int c = 0, tries = 0; c < 3 && tries < 200; tries++)
        {
            double x = (JS.Random() - 0.5) * 2 * (HALF - 10);
            double z = -36 + JS.Random() * 50;
            if (!ok(x, z)) continue;
            clump(x, z, 3 + (int)Math.Floor(JS.Random() * 3), 0.8);
            c++;
        }
        if (list.Count == 0) return;
        var im = new THREE.InstancedMesh(geo, mat, list.Count);
        var m = new THREE.Matrix4();
        var q = new THREE.Quaternion();
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), t.a);
            m.compose(new THREE.Vector3(t.x, beach.heightAt(t.x, t.z) - 0.02, t.z), q, new THREE.Vector3(t.s, t.s * (0.9 + JS.Random() * 0.3), t.s));
            im.setMatrixAt(i, m);
        }
        im.castShadow = false;
        im.receiveShadow = true;
        im.frustumCulled = false;
        mesh = im;
        tufts = list.Select(t => new Tuft { x = t.x, z = t.z, alive = true }).ToList();
        group.add(im);
        trimDug();
    }

    /// <summary>Clears away any tuft whose sand has been dug out from under it.</summary>
    public void trimDug()
    {
        if (mesh == null) return;
        bool changed = false;
        for (int i = 0; i < tufts.Count; i++)
        {
            var t = tufts[i];
            if (!t.alive) continue;
            bool dug = beach.depthAt(t.x, t.z) > 0.02 || beach.depthAt(t.x + 0.14, t.z) > 0.02 || beach.depthAt(t.x - 0.14, t.z) > 0.02
                || beach.depthAt(t.x, t.z + 0.14) > 0.02 || beach.depthAt(t.x, t.z - 0.14) > 0.02;
            if (dug)
            {
                t.alive = false;
                mesh.setMatrixAt(i, zero);
                changed = true;
            }
        }
        if (changed) mesh.instanceMatrix.needsUpdate = true;
    }

    public void update(double t) => uTime.value = t;
}
