using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Scenery.ts

public class Scenery
{
    private class Nut
    {
        public THREE.Mesh mesh;
        public THREE.Group crown;
        public THREE.Vector3 home;
        /// <summary>on the tree and shootable</summary>
        public bool onTree;
        /// <summary>seconds until it hangs on the tree again</summary>
        public double regrow;
        public THREE.Vector3 vel;
        public bool falling;
        /// <summary>seconds it lies on the sand before vanishing</summary>
        public double lie;
        /// <summary>0..1 how far it has grown back on the tree</summary>
        public double growT;
    }

    private class Palm
    {
        public THREE.Group group, crown;
        public double phase, x, z;
        /// <summary>the small parts that only matter up close: the rings on the trunk, and the coconuts</summary>
        public THREE.Mesh rings;
        public THREE.Group nuts;
        /// <summary>half the fronds, kept at every distance</summary>
        public THREE.Mesh crownA;
        /// <summary>the other half, which shrink away far off</summary>
        public THREE.Mesh crownB;
        /// <summary>0 = every part, 1 = no rings or coconuts, 2 = and half the fronds</summary>
        public int lod;
        /// <summary>0..1 how much of the small detail is showing</summary>
        public double k, kf;
    }

    public class Trunk
    {
        public double x, z;
    }

    /// <summary>How far a palm is before it is drawn with fewer parts. Well beyond the reach of the sun's shadow, so shadows are unchanged.</summary>
    private const double LOD_MID = 42;
    private const double LOD_FAR = 90;

    public readonly THREE.Group group = new THREE.Group();
    /// <summary>trunk footprints, for simple collision</summary>
    public readonly List<Trunk> trunks = new List<Trunk>();
    private readonly List<Palm> palms = new List<Palm>();
    private readonly List<Nut> nuts = new List<Nut>();
    private readonly List<THREE.Object3D> nutTargets = new List<THREE.Object3D>();
    private readonly List<(THREE.MeshBasicMaterial mat, double haze)> mountainMats = new List<(THREE.MeshBasicMaterial, double)>();
    private readonly THREE.Color tmp = new THREE.Color();
    private readonly THREE.Color tint = new THREE.Color(0x6f8fa6);

    public Scenery()
    {
        buildPalms();
        buildMountains();
    }

    // ------------------------------------------------------------ palms

    private THREE.BufferGeometry frondGeometry()
    {
        var geo = new THREE.PlaneGeometry(0.7, 3.4, 3, 10);
        var p = geo.attributes.position;
        for (int i = 0; i < p.count; i++)
        {
            double x = p.getX(i);
            double t = (p.getY(i) + 1.7) / 3.4; // 0 at the base, 1 at the tip
            double taper = Math.Sin(Math.PI * Math.Min(1, t * 1.05 + 0.02)) * 0.85 + 0.15;
            p.setX(i, x * taper);
            p.setZ(i, -t * t * 1.4 + Math.Abs(x) * 0.25); // droop and a slight V-fold
            p.setY(i, p.getY(i) + 1.7);
        }
        geo.computeVertexNormals();
        return geo;
    }

    private void buildPalms()
    {
        var rand = Stones.rng(7);
        var frondGeo = frondGeometry();
        var frondCols = new[] { 0x3f8f3a, 0x4d9d41, 0x35802f }.Select(c => new THREE.Color(c)).ToArray();
        var frondMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.85, side = THREE.Side.DoubleSide };
        var trunkMat = new THREE.MeshStandardMaterial { color = 0x8a6a46, roughness = 1 };
        var nutMat = new THREE.MeshStandardMaterial { color = 0x5a3a1e, roughness = 0.7 };

        var spots = new List<(double x, double z)>();
        // lining both sides of the beach
        for (int i = 0; i < 20; i++) spots.Add((-(64 + rand() * 12), -66 + rand() * 106));
        for (int i = 0; i < 20; i++) spots.Add((64 + rand() * 12, -66 + rand() * 106));
        // inland behind the camp
        for (int i = 0; i < 20; i++) spots.Add(((rand() - 0.5) * 130, -(66 + rand() * 10)));
        // scattered across the open beach
        for (int i = 0; i < 46; i++) spots.Add(((rand() - 0.5) * 128, -44 + rand() * 72));
        // leaning toward the water
        for (int i = 0; i < 18; i++) spots.Add(((rand() - 0.5) * 130, 34 + rand() * 3));

        foreach (var (x, z) in spots)
        {
            if (JS.Hypot(x, z - CAMP_Z) < 15) continue; // keep the camp clear
            var g = new THREE.Group();
            double h = 5.5 + rand() * 3;
            double lean = (rand() - 0.5) * 2.2;
            double leanDir = rand() * Math.PI * 2;
            var curve = new THREE.QuadraticBezierCurve3(
                new THREE.Vector3(0, 0, 0),
                new THREE.Vector3(Math.Cos(leanDir) * lean * 0.2, h * 0.55, Math.Sin(leanDir) * lean * 0.2),
                new THREE.Vector3(Math.Cos(leanDir) * lean, h, Math.Sin(leanDir) * lean));
            // the trunk reaches well down into the sand, so even the deepest hole never uncovers its foot
            var rootGeo = new THREE.CylinderGeometry(0.2, 0.22, 2, 8);
            rootGeo.translate(0, -1, 0);
            var trunk = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(new THREE.BufferGeometry[] { rootGeo, new THREE.TubeGeometry(curve, 14, 0.2, 8) }), trunkMat);
            trunk.castShadow = true;
            trunk.receiveShadow = true;
            g.add(trunk);
            // rings along the trunk (and down the buried foot, in case someone digs beside the tree)
            var ringGeos = new List<THREE.BufferGeometry>();
            for (int i = -3; i < 12; i++)
            {
                var pt = i > 0 ? curve.getPoint(i / 12.0) : new THREE.Vector3(0, i / 12.0 * h, 0);
                var ring = new THREE.TorusGeometry(0.21 - Math.Max(0, i) * 0.004, 0.03, 5, 10);
                ring.rotateX(Math.PI / 2);
                ring.translate(pt.x, pt.y, pt.z);
                ringGeos.Add(ring);
            }
            var rings = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(ringGeos), trunkMat);
            g.add(rings);
            var crown = new THREE.Group();
            crown.position.copy(curve.getPoint(1));
            var halves = new[] { new List<THREE.BufferGeometry>(), new List<THREE.BufferGeometry>() };
            var nutGroup = new THREE.Group();
            crown.add(nutGroup);
            const int fronds = 9;
            for (int i = 0; i < fronds; i++)
            {
                var f = new THREE.Object3D();
                f.rotation.order = "YXZ";
                f.rotation.y = (double)i / fronds * Math.PI * 2 + rand() * 0.3;
                f.rotation.x = Math.PI / 2 - (0.35 + rand() * 0.4);
                f.scale.setScalar(0.9 + rand() * 0.35);
                f.updateMatrix();
                var shade = frondCols[i % 3];
                var geo = frondGeo.clone().applyMatrix4(f.matrix);
                var tintArr = new float[geo.attributes.position.count * 3];
                for (int k = 0; k < tintArr.Length; k += 3) { tintArr[k] = (float)shade.r; tintArr[k + 1] = (float)shade.g; tintArr[k + 2] = (float)shade.b; }
                geo.setAttribute("color", new THREE.BufferAttribute(tintArr, 3));
                halves[i % 2].Add(geo);
            }
            var crownA = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(halves[0]), frondMat);
            var crownB = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(halves[1]), frondMat);
            crownA.castShadow = crownB.castShadow = true;
            crown.add(crownA, crownB);
            int nutCount = 3 + (int)Math.Floor(rand() * 3);
            for (int i = 0; i < nutCount; i++)
            {
                var nut = new THREE.Mesh(new THREE.SphereGeometry(0.17, 10, 8), nutMat);
                double a = (double)i / nutCount * Math.PI * 2 + rand();
                nut.position.set(Math.Cos(a) * 0.22, -0.28 - rand() * 0.1, Math.Sin(a) * 0.22);
                nut.castShadow = true;
                nut.userData["hitRadius"] = 0.5;
                nut.userData["coconut"] = true;
                nutGroup.add(nut);
                nuts.Add(new Nut { mesh = nut, crown = crown, home = nut.position.clone(), onTree = true, regrow = 0, vel = new THREE.Vector3(), falling = false, lie = 0, growT = 1 });
                nutTargets.Add(nut);
            }
            g.add(crown);
            g.position.set(x, baseHeight(x, z) - 0.1, z);
            g.rotation.y = rand() * Math.PI * 2;
            group.add(g);
            palms.Add(new Palm { group = g, crown = crown, phase = rand() * 6, x = x, z = z, rings = rings, nuts = nutGroup, crownA = crownA, crownB = crownB, lod = 0, k = 1, kf = 1 });
            trunks.Add(new Trunk { x = x, z = z });
        }
    }

    // ------------------------------------------------------------ mountains

    private THREE.Mesh peak(double radius, double height, Func<double> rand, THREE.Material mat)
    {
        var geo = new THREE.ConeGeometry(radius, height, 9, 4);
        var p = geo.attributes.position;
        // the cone repeats its seam vertices, so each position gets one shared roughness value;
        // otherwise the two edges of the seam drift apart and leave a crack in the mountain
        var rough = new Dictionary<string, double[]>();
        for (int i = 0; i < p.count; i++)
        {
            double x = p.getX(i), y = p.getY(i), z = p.getZ(i);
            if (y < height / 2 - 0.01 && y > -height / 2 + 0.01)
            {
                // rounded to whole millimetres so tiny float differences (and -0 vs 0) still match
                string key = $"{JS.Round(x * 1000) + 0},{JS.Round(y * 1000) + 0},{JS.Round(z * 1000) + 0}";
                if (!rough.TryGetValue(key, out var r))
                {
                    r = new[] { 0.8 + rand() * 0.4, 0.8 + rand() * 0.4, (rand() - 0.5) * height * 0.08 };
                    rough[key] = r;
                }
                p.setX(i, x * r[0]);
                p.setZ(i, z * r[1]);
                p.setY(i, y + r[2]);
            }
        }
        geo.computeVertexNormals();
        return new THREE.Mesh(geo, mat);
    }

    private void buildMountains()
    {
        var rand = Stones.rng(21);
        var rows = new[] { (dist: 265.0, haze: 0.55, size: 1.0), (dist: 235.0, haze: 0.35, size: 0.8) };
        foreach (var row in rows)
        {
            var mat = new THREE.MeshBasicMaterial { color = 0x6f8fa6, fog = false };
            mountainMats.Add((mat, row.haze));
            // a range behind the dunes, curving round to both sides (none out over the sea)
            for (int a = -95; a <= 95; a += 9)
            {
                double ang = a * Math.PI / 180;
                double r = row.dist + rand() * 20;
                double x = Math.Sin(ang) * r, z = -Math.Cos(ang) * r;
                double radius = (45 + rand() * 45) * row.size;
                double height = (45 + rand() * 70) * row.size * (Math.Abs(a) > 70 ? 0.6 : 1);
                var m = peak(radius, height, rand, mat);
                m.position.set(x, height / 2 - 6, z);
                m.rotation.y = rand() * 6;
                group.add(m);
            }
        }
    }

    /// <summary>Coconuts still hanging on the trees.</summary>
    public List<THREE.Object3D> targets()
    {
        // coconuts on palms that are drawn without them (far away) cannot be hit either
        return nutTargets.Where(n => n.parent == null || n.parent.visible).ToList();
    }

    /// <summary>A coconut was shot: it drops from the crown. Returns where it was, or null if it is not one of ours.</summary>
    public THREE.Vector3 shootNut(THREE.Object3D obj)
    {
        var n = nuts.FirstOrDefault(x => x.mesh == obj && x.onTree);
        if (n == null) return null;
        var at = n.mesh.getWorldPosition(new THREE.Vector3());
        n.mesh.parent?.remove(n.mesh);
        n.mesh.position.copy(at);
        group.add(n.mesh);
        n.onTree = false;
        n.falling = true;
        n.lie = 7;
        n.regrow = 120;
        n.vel.set((JS.Random() - 0.5) * 1.2, 1.2, (JS.Random() - 0.5) * 1.2);
        n.mesh.userData["coconut"] = false;
        nutTargets.Remove(obj);
        return at;
    }

    private void updateNuts(double dt)
    {
        foreach (var n in nuts)
        {
            if (n.onTree)
            {
                // a coconut that has just grown back swells out gently
                if (n.growT < 1)
                {
                    n.growT = Math.Min(1, n.growT + dt / 2);
                    double e = n.growT * n.growT * (3 - 2 * n.growT);
                    n.mesh.scale.setScalar(Math.Max(0.01, e));
                }
                continue;
            }
            if (n.falling)
            {
                n.vel.y -= 9.8 * dt;
                n.mesh.position.addScaledVector(n.vel, dt);
                double ground = baseHeight(n.mesh.position.x, n.mesh.position.z) + 0.17;
                if (n.mesh.position.y <= ground)
                {
                    n.mesh.position.y = ground;
                    if (n.vel.y < -2)
                    {
                        n.vel.y *= -0.3;
                        n.vel.x *= 0.5;
                        n.vel.z *= 0.5;
                    }
                    else n.falling = false;
                }
            }
            else if (n.mesh.visible)
            {
                // after a while on the sand it shrinks away rather than vanishing
                n.lie -= dt;
                if (n.lie <= 0)
                {
                    double k = Math.Max(0, 1 + n.lie / 1.0);
                    n.mesh.scale.setScalar(Math.Max(0.001, k));
                    if (k <= 0) n.mesh.visible = false;
                }
            }
            n.regrow -= dt;
            if (n.regrow <= 0)
            {
                group.remove(n.mesh);
                n.mesh.position.copy(n.home);
                n.mesh.visible = true;
                n.growT = 0;
                n.mesh.scale.setScalar(0.01);
                n.crown.children[0].add(n.mesh); // back into the palm's coconut group, which the distance detail switches on and off
                n.mesh.userData["coconut"] = true;
                n.onTree = true;
                n.falling = false;
                nutTargets.Add(n.mesh);
            }
        }
    }

    public void update(double t, THREE.Color fog, double dt = 0, double camX = 0, double camZ = 0)
    {
        updateNuts(dt);
        double lum = Math.Min(1, 0.2126 * fog.r + 0.7152 * fog.g + 0.0722 * fog.b);
        double light = 0.32 + 0.68 * Math.Min(1, lum / 0.75);
        foreach (var (mat, haze) in mountainMats)
        {
            tmp.copy(tint).lerp(fog, haze * 0.22).multiplyScalar(light);
            mat.color.lerp(tmp, 0.04);
        }
        foreach (var p in palms)
        {
            // fewer parts the further away it is, with a few metres of give so it does not flicker at the edge
            double d = JS.Hypot(p.x - camX, p.z - camZ);
            int want = d > LOD_FAR + (p.lod == 2 ? -4 : 4) ? 2 : d > LOD_MID + (p.lod >= 1 ? -4 : 4) ? 1 : 0;
            p.lod = want;
            // ease toward the wanted amount of detail, so parts grow in and shrink away instead of popping
            double wantK = want == 0 ? 1 : 0;
            double wantF = want < 2 ? 1 : 0;
            if (p.k != wantK || p.kf != wantF)
            {
                double a = dt > 0 ? Math.Min(1, dt * 4) : 1;
                p.k += (wantK - p.k) * a;
                p.kf += (wantF - p.kf) * a;
                if (Math.Abs(wantK - p.k) < 0.01) p.k = wantK;
                if (Math.Abs(wantF - p.kf) < 0.01) p.kf = wantF;
                double k = Math.Max(p.k, 0.001);
                p.rings.visible = p.k > 0.01;
                p.nuts.visible = p.k > 0.01;
                p.nuts.scale.setScalar(k);
                p.crownB.visible = p.kf > 0.01;
                p.crownB.scale.setScalar(Math.Max(p.kf, 0.001));
            }
            p.crown.rotation.z = Math.Sin(t * 0.8 + p.phase) * 0.03;
            p.crown.rotation.x = Math.Cos(t * 0.6 + p.phase) * 0.025;
        }
    }
}
