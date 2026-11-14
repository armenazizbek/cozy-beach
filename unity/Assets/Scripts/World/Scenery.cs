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
        public THREE.Group group;
        /// <summary>what sways: the crown's group, or for a palm out across the land its one crown mesh</summary>
        public THREE.Object3D crown;
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
    /// <summary>
    /// A mountain of the range round the beach. It is drawn far off in the colour of the haze, and lit as rock when the player
    /// comes close to it; it is also where the land ends, so `rGround` is how wide it is where it meets the ground.
    /// </summary>
    public class Mountain
    {
        public double x, z, rGround;
        /// <summary>the cone itself: its radius at its foot, how tall it is, and where its foot is</summary>
        public double radius, height, footY;
        public int row;
        public THREE.MeshStandardMaterial mat;
        public double lastNear = -1;
        public THREE.Color lastHaze = new THREE.Color(-1, -1, -1);
    }

    /// <summary>every mountain of the range; the land is everything inside it</summary>
    public readonly List<Mountain> mountains = new List<Mountain>();
    private static List<Mountain> range = new List<Mountain>();
    /// <summary>the haze colour of each row of the range, eased toward the sky's</summary>
    private readonly THREE.Color[] rowHaze = { new THREE.Color(0x6f8fa6), new THREE.Color(0x6f8fa6) };
    private static readonly double[] ROW_HAZE = { 0.55, 0.35 };
    private static readonly THREE.Color ROCK = new THREE.Color(0x8a8172);
    private readonly THREE.Color mixed = new THREE.Color();

    /// <summary>No one walks further than this from the middle of the beach, between the mountains or past them.</summary>
    public const double LAND_R = 215;

    /// <summary>True where a point is on the land, at least `margin` clear of the mountains and the far edge of it.</summary>
    public static bool onLand(double x, double z, double margin = 0)
    {
        if (JS.Hypot(x, z) > LAND_R - margin) return false;
        foreach (var m in range) if (JS.Hypot(x - m.x, z - m.z) < m.rGround + 2 + margin) return false;
        return true;
    }

    /// <summary>Moves a point back onto the land: out of the foot of any mountain, and inside the far edge.</summary>
    public void keepOnLand(ref double x, ref double z, double extra = 0)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            double r = JS.Hypot(x, z), lim = LAND_R - extra;
            if (r > lim)
            {
                x *= lim / r;
                z *= lim / r;
            }
            foreach (var m in mountains)
            {
                double dx = x - m.x, dz = z - m.z, d = JS.Hypot(dx, dz), min = m.rGround + 2 + extra;
                if (d >= min || d < 1e-6) continue;
                x = m.x + dx / d * min;
                z = m.z + dz / d * min;
            }
        }
    }
    private readonly THREE.Color tmp = new THREE.Color();
    private readonly THREE.Color tint = new THREE.Color(0x6f8fa6);

    public Scenery()
    {
        // the mountains first: the palms out across the land keep clear of them
        buildMountains();
        buildPalms();
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

    /// <summary>No palm within this many metres of the pier's middle line, from this far up the beach out to its end.</summary>
    private const double PIER_CLEAR_HALF = 4.5, PIER_CLEAR_RUN = 14;

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
        // and out across the rest of the land, in groves, from the shore back to the mountains. These come from a sequence of
        // their own, after all the others, so every palm on the middle of the beach stays where it always stood
        var grove = Stones.rng(77);
        var more = new List<(double x, double z)>();
        bool free(double x, double z) =>
            !(Math.Abs(x) < 76 && z > -78) && z < 33 && onLand(x, z, 5) && baseHeight(x, z) > WATER_LEVEL + 0.4 &&
            spots.All(p => JS.Hypot(p.x - x, p.z - z) > 3.5) && more.All(p => JS.Hypot(p.x - x, p.z - z) > 3.5);
        for (int g = 0, tries = 0; g < 40 && tries < 4000; tries++)
        {
            double a = grove() * Math.PI * 2, d = 75 + grove() * 135;
            double gx = Math.Sin(a) * d, gz = -Math.Abs(Math.Cos(a)) * d + 32;
            if (!free(gx, gz)) continue;
            more.Add((gx, gz));
            int n = 1 + (int)Math.Floor(grove() * 5);
            for (int i = 0; i < n * 4 && n > 0; i++)
            {
                double aa = grove() * Math.PI * 2, dd = 3.5 + grove() * 5;
                double px = gx + Math.Cos(aa) * dd, pz = gz + Math.Sin(aa) * dd;
                if (!free(px, pz)) continue;
                more.Add((px, pz));
                n--;
            }
            g++;
        }
        int innerCount = spots.Count;
        outerTrunks.Clear();
        spots.AddRange(more);
        int index = -1;

        foreach (var (spotX, z) in spots)
        {
            index++;
            if (index >= innerCount)
            {
                // out across the land, a palm is two meshes, not a dozen: the frame rate stays where it was with a hundred more
                buildOuterPalm(spotX, z, rand, frondGeo, frondCols, frondMat, trunkMat);
                continue;
            }
            if (JS.Hypot(spotX, z - CAMP_Z) < 15) continue; // keep the camp clear
            // keep the pier and the walk down to it clear: a palm there is moved aside, not dropped, so every other palm
            // keeps the shape it always had
            double x = spotX;
            if (z > Pier.Z0 - PIER_CLEAR_RUN && Math.Abs(x - Pier.X) < PIER_CLEAR_HALF)
                x = Pier.X + (x < Pier.X ? -PIER_CLEAR_HALF : PIER_CLEAR_HALF);
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
        if (outerTrunks.Count > 0)
        {
            var all = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(outerTrunks), trunkMat);
            all.castShadow = true;
            all.receiveShadow = true;
            group.add(all);
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
        range = mountains;
        for (int ri = 0; ri < rows.Length; ri++)
        {
            var row = rows[ri];
            // a range behind the dunes, curving round to both sides (none out over the sea)
            for (int a = -95; a <= 95; a += 9)
            {
                double ang = a * Math.PI / 180;
                double r = row.dist + rand() * 20;
                double x = Math.Sin(ang) * r, z = -Math.Cos(ang) * r;
                double radius = (45 + rand() * 45) * row.size;
                double height = (45 + rand() * 70) * row.size * (Math.Abs(a) > 70 ? 0.6 : 1);
                // each mountain has its own material: far off it is the haze's colour, and up close it is lit rock
                var mat = new THREE.MeshStandardMaterial { color = 0x000000, emissive = 0x6f8fa6, roughness = 1, metalness = 0, flatShading = true, fog = false };
                var m = peak(radius, height, rand, mat);
                m.position.set(x, height / 2 - 6, z);
                m.rotation.y = rand() * 6;
                group.add(m);
                // how wide it is where it meets the ground (the cone's rough sides reach out a fifth further at most)
                double g = baseHeight(x, z);
                double rg = Math.Max(0, radius * (1 - (g + 6) / height) * 1.15);
                mountains.Add(new Mountain { x = x, z = z, rGround = rg, radius = radius, height = height, footY = -6, row = ri, mat = mat });
            }
        }
    }

    /// <summary>
    /// One of the palms out across the land: the same tree, but its trunk and rings are one mesh and its fronds and coconuts
    /// another, which still sways. Its coconuts cannot be shot down.
    /// </summary>
    /// <summary>the trunks of all the palms out across the land, gathered into one mesh once they are all placed</summary>
    private readonly List<THREE.BufferGeometry> outerTrunks = new List<THREE.BufferGeometry>();

    private void buildOuterPalm(double x, double z, Func<double> rand, THREE.BufferGeometry frondGeo, THREE.Color[] frondCols, THREE.Material frondMat, THREE.Material trunkMat)
    {
        double h = 5.5 + rand() * 3;
        double lean = (rand() - 0.5) * 2.2;
        double leanDir = rand() * Math.PI * 2;
        var curve = new THREE.QuadraticBezierCurve3(
            new THREE.Vector3(0, 0, 0),
            new THREE.Vector3(Math.Cos(leanDir) * lean * 0.2, h * 0.55, Math.Sin(leanDir) * lean * 0.2),
            new THREE.Vector3(Math.Cos(leanDir) * lean, h, Math.Sin(leanDir) * lean));
        var woody = new List<THREE.BufferGeometry> { new THREE.TubeGeometry(curve, 10, 0.2, 7) };
        var foot = new THREE.CylinderGeometry(0.2, 0.22, 1, 7);
        foot.translate(0, -0.5, 0);
        woody.Add(foot);
        for (int i = 1; i < 12; i++)
        {
            var pt = curve.getPoint(i / 12.0);
            var ring = new THREE.TorusGeometry(0.21 - i * 0.004, 0.03, 4, 8);
            ring.rotateX(Math.PI / 2);
            ring.translate(pt.x, pt.y, pt.z);
            woody.Add(ring);
        }
        double baseY = baseHeight(x, z) - 0.1;
        double yaw = 0;
        foreach (var w in woody) w.deleteAttribute("uv");
        var leaves = new List<THREE.BufferGeometry>();
        const int fronds = 9;
        for (int i = 0; i < fronds; i++)
        {
            var f = new THREE.Object3D();
            f.rotation.order = "YXZ";
            f.rotation.y = (double)i / fronds * Math.PI * 2 + rand() * 0.3;
            f.rotation.x = Math.PI / 2 - (0.35 + rand() * 0.4);
            f.scale.setScalar(0.9 + rand() * 0.35);
            f.updateMatrix();
            var geo = frondGeo.clone().applyMatrix4(f.matrix);
            geo.deleteAttribute("uv");
            leaves.Add(Ecology.paint(geo, frondCols[i % 3].getHex()));
        }
        int nutCount = 3 + (int)Math.Floor(rand() * 3);
        for (int i = 0; i < nutCount; i++)
        {
            var nut = new THREE.SphereGeometry(0.17, 8, 6);
            double a = (double)i / nutCount * Math.PI * 2 + rand();
            nut.translate(Math.Cos(a) * 0.22, -0.28 - rand() * 0.1, Math.Sin(a) * 0.22);
            leaves.Add(Ecology.paint(nut, 0x5a3a1e));
        }
        yaw = rand() * Math.PI * 2;
        // the trunk goes into the one shared mesh, already turned and standing where it belongs
        var trunkGeo = THREE.BufferGeometryUtils.mergeGeometries(woody);
        trunkGeo.rotateY(yaw);
        trunkGeo.translate(x, baseY, z);
        outerTrunks.Add(trunkGeo);
        // the crown is a mesh of its own, so it can sway: turned the same way as the trunk, at the top of it
        var top = curve.getPoint(1);
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
        var crown = new THREE.Mesh(THREE.BufferGeometryUtils.mergeGeometries(leaves), frondMat);
        crown.castShadow = true;
        crown.rotation.order = "YXZ";
        crown.rotation.y = yaw;
        crown.position.set(x + top.x * cy + top.z * sy, baseY + top.y, z - top.x * sy + top.z * cy);
        group.add(crown);
        palms.Add(new Palm { group = null, crown = crown, phase = rand() * 6, x = x, z = z, rings = null, lod = 0, k = 1, kf = 1 });
        trunks.Add(new Trunk { x = x, z = z });
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
        for (int r = 0; r < rowHaze.Length; r++)
        {
            tmp.copy(tint).lerp(fog, ROW_HAZE[r] * 0.22).multiplyScalar(light);
            rowHaze[r].lerp(tmp, 0.04);
        }
        foreach (var m in mountains)
        {
            // far off, a flat shape in the colour of the haze, as it always was; within a hundred metres or so it comes up as
            // rock, lit by the sun like the ground the player stands on
            double edge = JS.Hypot(camX - m.x, camZ - m.z) - m.rGround;
            double near = smooth(130, 25, edge);
            var haze = rowHaze[m.row];
            if (Math.Abs(near - m.lastNear) < 0.003 && Math.Abs(haze.r - m.lastHaze.r) + Math.Abs(haze.g - m.lastHaze.g) + Math.Abs(haze.b - m.lastHaze.b) < 0.003) continue;
            m.lastNear = near;
            m.lastHaze.copy(haze);
            m.mat.color.copy(ROCK).multiplyScalar(near);
            m.mat.emissive.copy(haze).multiplyScalar(1 - near);
        }
        foreach (var p in palms)
        {
            if (p.rings == null)
            {
                // a palm out across the land has no parts to drop; it only sways
                p.crown.rotation.z = Math.Sin(t * 0.8 + p.phase) * 0.03;
                p.crown.rotation.x = Math.Cos(t * 0.6 + p.phase) * 0.025;
                continue;
            }
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
