using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The aquarium at camp: a glass tank on a wooden stand. A small one of every kind of sea creature caught alive lives in it:
/// the fish swim round, the animals of the sea floor go about their business on the sand. Its group is in the camp's own space.
/// </summary>
public class Aquarium
{
    /// <summary>where it stands in the camp, and which way its front faces (toward the fire)</summary>
    public static readonly (double x, double z, double rot) AT = (8.3, 2.3, -1.83);

    /// <summary>the inside of the tank: width, height, depth, and the height of its floor above the ground</summary>
    private const double W = 1.8, H = 0.72, D = 0.5, BASE = 0.8;
    private const double SAND = 0.07;

    public readonly THREE.Group group = new THREE.Group { name = "Aquarium" };
    private readonly THREE.Group life = new THREE.Group();
    private readonly Underwater sea;

    private class Swimmer
    {
        public THREE.Group root;
        public THREE.Mesh tail;
        /// <summary>the middle of its loop, and how far the loop reaches along and across the tank</summary>
        public double y, ax, az, speed, phase;
    }

    private class Dweller
    {
        public Animal animal;
        public THREE.Group wrap;
        public double scale;
        /// <summary>where in the tank it stays, and how far it drifts about that spot</summary>
        public THREE.Vector3 home;
        public double drift, phase;
    }

    private readonly Dictionary<string, Swimmer> swimmers = new Dictionary<string, Swimmer>();
    private readonly Dictionary<string, Dweller> dwellers = new Dictionary<string, Dweller>();
    /// <summary>far above everything, so nothing in the tank thinks a diver is near and flees</summary>
    private readonly THREE.Vector3 nobody = new THREE.Vector3(0, 1e6, 0);

    private static THREE.Mesh box(double w, double h, double d, THREE.Material mat, double x, double y, double z)
    {
        var m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
        m.position.set(x, y, z);
        m.castShadow = true;
        m.receiveShadow = true;
        return m;
    }

    public Aquarium(Underwater sea)
    {
        this.sea = sea;
        var wood = new THREE.MeshStandardMaterial { color = 0x6b4a2f, roughness = 0.9 };
        var darkWood = new THREE.MeshStandardMaterial { color = 0x4a3020, roughness = 0.9 };
        var trim = new THREE.MeshStandardMaterial { color = 0x2b3238, roughness = 0.5, metalness = 0.4 };
        var sandMat = new THREE.MeshStandardMaterial { color = 0xe8d6b0, roughness = 1 };
        var rock = new THREE.MeshStandardMaterial { color = 0x7d8388, roughness = 0.95, flatShading = true };
        var weed = new THREE.MeshStandardMaterial { color = 0x3f9a5a, roughness = 0.8, side = THREE.Side.DoubleSide };
        var weed2 = new THREE.MeshStandardMaterial { color = 0x2f7f6a, roughness = 0.8, side = THREE.Side.DoubleSide };
        var coral = new THREE.MeshStandardMaterial { color = 0xf08a7a, roughness = 0.8 };
        var water = new THREE.MeshStandardMaterial { color = 0x8fd8ee, roughness = 0.1, transparent = true, opacity = 0.2, depthWrite = false };

        var still = new THREE.Group();
        // the stand: a table with a shelf
        still.add(box(W + 0.16, 0.08, D + 0.14, wood, 0, BASE - 0.06, 0));
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                still.add(box(0.09, BASE - 0.1, 0.09, darkWood, sx * (W / 2 - 0.02), (BASE - 0.1) / 2, sz * (D / 2 - 0.02)));
        still.add(box(W + 0.02, 0.05, D, wood, 0, 0.26, 0));
        // the tank's frame: a rim round the bottom and the top, and four corner posts
        const double f = 0.035;
        foreach (var y in new[] { BASE + f / 2 - 0.02, BASE + H + f / 2 })
        {
            foreach (var sz in new[] { -1, 1 }) still.add(box(W + f * 2, f, f, trim, 0, y, sz * (D / 2 + f / 2)));
            foreach (var sx in new[] { -1, 1 }) still.add(box(f, f, D + f * 2, trim, sx * (W / 2 + f / 2), y, 0));
        }
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                still.add(box(f, H, f, trim, sx * (W / 2 + f / 2), BASE + H / 2, sz * (D / 2 + f / 2)));
        // the floor of the tank and its sand
        still.add(box(W, 0.02, D, trim, 0, BASE - 0.01, 0));
        still.add(box(W, SAND, D, sandMat, 0, BASE + SAND / 2, 0));
        // rocks, coral and weed
        void stone(double x, double z, double r)
        {
            var m = new THREE.Mesh(new THREE.DodecahedronGeometry(r, 0), rock);
            m.position.set(x, BASE + SAND + r * 0.45, z);
            m.rotation.set(x * 3, z * 7, x + z);
            m.scale.y = 0.7;
            still.add(m);
        }
        stone(-0.62, -0.12, 0.11);
        stone(-0.48, -0.16, 0.07);
        stone(0.7, -0.1, 0.09);
        void plant(double x, double z, double h, THREE.Material mat)
        {
            for (int i = 0; i < 3; i++)
            {
                var blade = new THREE.Mesh(new THREE.PlaneGeometry(0.035, h * (1 - i * 0.18)), mat);
                blade.position.set(x + (i - 1) * 0.03, BASE + SAND + h * (1 - i * 0.18) / 2, z + (i % 2) * 0.02);
                blade.rotation.set(0, i * 1.1, (i - 1) * 0.12);
                still.add(blade);
            }
        }
        plant(-0.78, 0.1, 0.42, weed);
        plant(-0.2, -0.17, 0.3, weed2);
        plant(0.42, -0.15, 0.48, weed);
        plant(0.8, 0.12, 0.34, weed2);
        for (int i = 0; i < 4; i++)
        {
            var branch = new THREE.Mesh(new THREE.CylinderGeometry(0.012, 0.02, 0.16 - i * 0.02, 6), coral);
            branch.position.set(0.12 + i * 0.035, BASE + SAND + 0.07, 0.13 - i * 0.02);
            branch.rotation.z = (i - 1.5) * 0.28;
            still.add(branch);
        }
        Merge.mergeStatic(still);
        // the water is drawn after everything in it, so the life inside shows through
        var waterBox = new THREE.Mesh(new THREE.BoxGeometry(W, H - 0.05, D), water);
        waterBox.position.set(0, BASE + (H - 0.05) / 2, 0);
        waterBox.renderOrder = 2;
        group.add(still, life, waterBox);
        group.position.set(AT.x, 0, AT.z);
        group.rotation.y = AT.rot;
    }

    /// <summary>Makes sure a small one of each of these species is in the tank (and nothing else).</summary>
    public void setCaught(ICollection<string> ids)
    {
        foreach (var kv in swimmers) kv.Value.root.visible = ids.Contains(kv.Key);
        foreach (var kv in dwellers) kv.Value.wrap.visible = ids.Contains(kv.Key);
        foreach (var id in ids)
        {
            if (swimmers.ContainsKey(id) || dwellers.ContainsKey(id) || !Catches.BY_ID.TryGetValue(id, out var s)) continue;
            if (s.kind == "fish") addSwimmer(s);
            else addDweller(s);
        }
    }

    private void addSwimmer(SeaSpecies s)
    {
        int n = Array.IndexOf(Catches.ALL, s);
        var (root, tail) = sea.makeFish(s);
        root.scale.setScalar(0.17 + (n % 3) * 0.02);
        root.traverse(o => { if (o is THREE.Mesh m) m.castShadow = false; });
        life.add(root);
        // each fish has a lane of its own: its height, how wide its loop is, and where on the loop it starts
        int lanes = Catches.ROD.Length + Catches.REEF.Length + 1;
        double k = (n % lanes + 0.5) / lanes;
        swimmers[s.id] = new Swimmer
        {
            root = root, tail = tail,
            y = BASE + SAND + 0.14 + ((n * 7) % lanes + 0.5) / lanes * (H - SAND - 0.3),
            ax = W / 2 - 0.22 - (n % 3) * 0.07, az = D / 2 - 0.12 - (n % 2) * 0.03,
            speed = 0.32 + k * 0.3, phase = n * 2.4,
        };
    }

    private void addDweller(SeaSpecies s)
    {
        var a = sea.creatures.spawnDetached(s.kind);
        if (a == null) return;
        // the size each kind is shown at, where it lives in the tank and how far it wanders from there
        var (scale, home, drift) = s.kind switch
        {
            "crab" => (0.3, new THREE.Vector3(-0.3, BASE + SAND, 0.12), 0.22),
            "octopus" => (0.15, new THREE.Vector3(0.62, BASE + SAND, 0.04), 0.06),
            "seahorse" => (0.36, new THREE.Vector3(-0.72, BASE + SAND + 0.2, 0.02), 0.05),
            "snake" => (0.2, new THREE.Vector3(0.12, BASE + SAND + 0.1, -0.06), 0.14),
            _ => (0.17, new THREE.Vector3(0, BASE + H * 0.74, 0), 0.45),
        };
        var wrap = new THREE.Group();
        wrap.scale.setScalar(scale);
        a.root.traverse(o => { if (o is THREE.Mesh m) m.castShadow = false; });
        wrap.add(a.root);
        life.add(wrap);
        dwellers[s.id] = new Dweller { animal = a, wrap = wrap, scale = scale, home = home, drift = drift, phase = dwellers.Count * 1.7 };
    }

    private readonly THREE.Box3 bounds = new THREE.Box3();
    private readonly THREE.Vector3 lo = new THREE.Vector3(), hi = new THREE.Vector3();

    /// <summary>
    /// Nudges something back inside the glass if any part of it, a tentacle, a tail or the length of a snake, has strayed past a wall,
    /// the sand or the water's top. `o` is measured with the tank turned square to the world, so the box is the tank's own.
    /// </summary>
    private void keepInside(THREE.Object3D o)
    {
        bounds.setFromObject(o, true);
        if (bounds.isEmpty()) return;
        lo.copy(bounds.min);
        hi.copy(bounds.max);
        group.worldToLocal(lo);
        group.worldToLocal(hi);
        const double m = 0.02;
        double x0 = -W / 2 + m, x1 = W / 2 - m, y0 = BASE + SAND * 0.5, y1 = BASE + H - 0.07, z0 = -D / 2 + m, z1 = D / 2 - m;
        double fix(double a, double b, double lowEdge, double highEdge)
        {
            // too big to fit: stay in the middle
            if (b - a > highEdge - lowEdge) return (lowEdge + highEdge) / 2 - (a + b) / 2;
            if (a < lowEdge) return lowEdge - a;
            if (b > highEdge) return highEdge - b;
            return 0;
        }
        o.position.x += fix(lo.x, hi.x, x0, x1);
        o.position.y += fix(lo.y, hi.y, y0, y1);
        o.position.z += fix(lo.z, hi.z, z0, z1);
    }

    /// <summary>Only worth doing while someone is near enough to see it.</summary>
    public void update(double dt, double t)
    {
        foreach (var f in swimmers.Values)
        {
            if (!f.root.visible) continue;
            double u = t * f.speed + f.phase;
            double x = Math.Cos(u) * f.ax, z = Math.Sin(u) * f.az;
            f.root.position.set(x, f.y + Math.Sin(u * 1.7 + f.phase) * 0.03, z);
            // the nose (+z of the model) points along the loop
            f.root.rotation.y = Math.Atan2(-Math.Sin(u) * f.ax, Math.Cos(u) * f.az);
            f.tail.rotation.y = Math.Sin(t * 7 + f.phase) * 0.5;
        }
        foreach (var d in dwellers.Values)
        {
            if (!d.wrap.visible) continue;
            var a = d.animal;
            // the animal lives on in its own patch of sea floor, far away; the wrapper carries that spot to its place in the tank
            a.tick(dt, t, nobody);
            double u = t * 0.11 + d.phase;
            double hx = d.home.x + Math.Cos(u) * d.drift, hz = d.home.z + Math.Sin(u * 1.3) * Math.Min(d.drift, D / 2 - 0.14);
            d.wrap.position.set(hx - a.x * d.scale, d.home.y - a.y * d.scale, hz - a.z * d.scale);
        }
        // however far a body reaches, none of it leaves the tank
        double turned = group.rotation.y;
        group.rotation.y = 0;
        group.updateMatrixWorld(true);
        foreach (var f in swimmers.Values) if (f.root.visible) keepInside(f.root);
        foreach (var d in dwellers.Values) if (d.wrap.visible) keepInside(d.wrap);
        group.rotation.y = turned;
        group.updateMatrixWorld(true);
    }
}
