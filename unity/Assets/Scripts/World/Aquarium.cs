using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The aquarium at camp: a glass tank with a lit hood on a wooden cabinet, sand banked up to a painted sea at the back,
/// plants, rocks, driftwood, coral and a bubbler. A small one of every kind of sea creature caught alive lives in it:
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

    /// <summary>
    /// What of the tank moves or shows, for the camp to keep its GameObjects in line (unity/UI_PLAN.md B1): each fish and its
    /// tail, each creature's wrapper and the creature's own parts. A catch added later makes the camp rebuild, and it asks
    /// again.
    /// </summary>
    internal IEnumerable<THREE.Object3D> Moving()
    {
        foreach (var f in swimmers.Values)
        {
            yield return f.root;
            yield return f.tail;
        }
        foreach (var b in bubbles) yield return b;
        foreach (var d in dwellers.Values)
        {
            yield return d.wrap;
            var parts = new List<THREE.Object3D>();
            d.animal.root.traverse(o => parts.Add(o));
            foreach (var o in parts) yield return o;
        }
    }
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

    /// <summary>a small fixed sequence of numbers in 0..1, so the tank comes out the same every time</summary>
    private static double hash(int n)
    {
        double v = Math.Sin(n * 12.9898 + 4.1414) * 43758.5453;
        return v - Math.Floor(v);
    }

    /// <summary>
    /// One blade of water plant: a strip that narrows to its tip and bends over as it rises, darker at the foot. Built
    /// standing on (0, 0, 0), bending toward +x.
    /// </summary>
    private static THREE.BufferGeometry blade(double h, double w, double bend, int segs, THREE.Color foot, THREE.Color tip)
    {
        var pos = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        var c = new THREE.Color();
        for (int i = 0; i <= segs; i++)
        {
            double t = (double)i / segs;
            double half = w / 2 * (1 - t * 0.8);
            double x = bend * t * t, y = h * t - Math.Abs(bend) * t * t * 0.3;
            double z = Math.Sin(t * 2.4) * w * 0.4;
            pos.AddRange(new[] { x - half, y, z, x + half, y, z });
            c.copy(foot).lerp(tip, t);
            col.AddRange(new[] { (double)c.r, c.g, c.b, c.r, c.g, c.b });
            if (i > 0)
            {
                int a = (i - 1) * 2;
                idx.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        g.setIndex(idx);
        g.computeVertexNormals();
        return g;
    }

    /// <summary>A flat panel coloured from `bottom` to `top` (the painted sea on the back glass).</summary>
    private static THREE.BufferGeometry gradient(double w, double h, THREE.Color bottom, THREE.Color top)
    {
        var g = new THREE.PlaneGeometry(w, h, 1, 4);
        var p = g.getAttribute("position");
        var col = new List<double>();
        var c = new THREE.Color();
        for (int i = 0; i < p.count; i++)
        {
            double t = (p.getY(i) + h / 2) / h;
            c.copy(bottom).lerp(top, t * t);
            col.AddRange(new[] { (double)c.r, c.g, c.b });
        }
        g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        return g;
    }

    /// <summary>the bubbles rising from the stone in the back corner</summary>
    private readonly List<THREE.Mesh> bubbles = new List<THREE.Mesh>();
    private static readonly (double x, double z) BUBBLER = (-0.66, -0.15);

    public Aquarium(Underwater sea)
    {
        this.sea = sea;
        var wood = new THREE.MeshStandardMaterial { color = 0x7a5433, roughness = 0.85 };
        var darkWood = new THREE.MeshStandardMaterial { color = 0x4a3020, roughness = 0.9 };
        var brass = new THREE.MeshStandardMaterial { color = 0xc9a25a, roughness = 0.35, metalness = 0.8 };
        var trim = new THREE.MeshStandardMaterial { color = 0x23292e, roughness = 0.45, metalness = 0.3 };
        var lamp = new THREE.MeshStandardMaterial { color = 0xfff6dc, emissive = 0xfff1cc, emissiveIntensity = 1.2, roughness = 0.5 };
        var sandMat = new THREE.MeshStandardMaterial { color = 0xe9d3a6, roughness = 1 };
        var bank = new THREE.MeshStandardMaterial { color = 0xdcc290, roughness = 1, flatShading = true };
        var rock = new THREE.MeshStandardMaterial { color = 0x737b80, roughness = 0.95, flatShading = true };
        var rockDark = new THREE.MeshStandardMaterial { color = 0x565e63, roughness = 0.95, flatShading = true };
        var drift = new THREE.MeshStandardMaterial { color = 0x8a6a4c, roughness = 0.95, flatShading = true };
        var plantMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.75, side = THREE.Side.DoubleSide };
        var moss = new THREE.MeshStandardMaterial { color = 0x3f8f43, roughness = 1, flatShading = true };
        var coralPink = new THREE.MeshStandardMaterial { color = 0xf07c8c, roughness = 0.7 };
        var coralOrange = new THREE.MeshStandardMaterial { color = 0xf2a45a, roughness = 0.8, flatShading = true };
        var shell = new THREE.MeshStandardMaterial { color = 0xf4e6d4, roughness = 0.6 };
        var shellPink = new THREE.MeshStandardMaterial { color = 0xf0c0b0, roughness = 0.6 };
        var backdrop = new THREE.MeshBasicMaterial { vertexColors = true };
        var pebbleMats = new[]
        {
            new THREE.MeshStandardMaterial { color = 0xb8aa96, roughness = 0.9, flatShading = true },
            new THREE.MeshStandardMaterial { color = 0x8d8479, roughness = 0.9, flatShading = true },
            new THREE.MeshStandardMaterial { color = 0xd8cbb4, roughness = 0.9, flatShading = true },
        };
        var water = new THREE.MeshStandardMaterial { color = 0x4fb6d4, roughness = 0.1, transparent = true, opacity = 0.16, depthWrite = false };
        var surface = new THREE.MeshStandardMaterial { color = 0xbff0fa, roughness = 0.05, transparent = true, opacity = 0.45, depthWrite = false, side = THREE.Side.DoubleSide };
        var glass = new THREE.MeshStandardMaterial { color = 0xe2f6f8, roughness = 0.05, metalness = 0.1, transparent = true, opacity = 0.1, depthWrite = false };
        var glassEdge = new THREE.MeshStandardMaterial { color = 0x8fcfc4, roughness = 0.1, transparent = true, opacity = 0.55, depthWrite = false };
        var bubbleMat = new THREE.MeshStandardMaterial { color = 0xeafcff, roughness = 0.05, transparent = true, opacity = 0.55, depthWrite = false };

        var still = new THREE.Group();
        // ---- the stand: a cabinet with two doors under a thick top
        const double front = D / 2 + 0.04;
        double bodyH = BASE - 0.16, bodyY = 0.08 + (BASE - 0.16) / 2;
        still.add(box(W + 0.18, 0.06, D + 0.16, wood, 0, BASE - 0.05, 0));
        still.add(box(W + 0.1, bodyH, D + 0.08, darkWood, 0, bodyY, 0));
        still.add(box(W + 0.14, 0.08, D + 0.12, wood, 0, 0.04, 0));
        foreach (var sx in new[] { -1, 1 })
        {
            // the doors, framed, and their brass knobs
            double dx = sx * (W + 0.1) / 4;
            still.add(box((W + 0.1) / 2 - 0.05, bodyH - 0.08, 0.02, wood, dx, bodyY, front + 0.01));
            still.add(box((W + 0.1) / 2 - 0.15, bodyH - 0.2, 0.012, darkWood, dx, bodyY, front + 0.022));
            var knob = new THREE.Mesh(new THREE.SphereGeometry(0.018, 10, 8), brass);
            knob.position.set(sx * 0.07, bodyY + 0.05, front + 0.035);
            still.add(knob);
        }
        // ---- the tank: slim black rims at the foot and the top, a hood with its lamp on top
        const double f = 0.022;
        foreach (var y in new[] { BASE + f / 2 - 0.02, BASE + H - f / 2 })
        {
            foreach (var sz in new[] { -1, 1 }) still.add(box(W + f * 2, f, f, trim, 0, y, sz * (D / 2 + f / 2)));
            foreach (var sx in new[] { -1, 1 }) still.add(box(f, f, D + f * 2, trim, sx * (W / 2 + f / 2), y, 0));
        }
        still.add(box(W + 0.05, 0.04, D + 0.05, trim, 0, BASE + H + 0.02, 0));
        var lampBar = box(W - 0.2, 0.012, 0.05, lamp, 0, BASE + H - 0.004, 0.02);
        lampBar.castShadow = false;
        still.add(lampBar);
        still.add(box(W, 0.02, D, trim, 0, BASE - 0.01, 0));
        // ---- the painted sea on the back glass: deep blue at the sand, light toward the top
        var back = new THREE.Mesh(gradient(W - 0.004, H - 0.02, new THREE.Color(0x0e4f6e), new THREE.Color(0x58b9d6)), backdrop);
        back.position.set(0, BASE + H / 2 - 0.01, -D / 2 + 0.004);
        still.add(back);
        // and on the end walls, seen from inside only: from the front the whole tank is sea, from the side one sees in
        foreach (var sx in new[] { -1, 1 })
        {
            var end = new THREE.Mesh(gradient(D - 0.004, H - 0.02, new THREE.Color(0x0e4f6e), new THREE.Color(0x58b9d6)), backdrop);
            end.position.set(sx * (W / 2 - 0.004), BASE + H / 2 - 0.01, 0);
            end.rotation.y = -sx * Math.PI / 2;
            still.add(end);
        }
        // ---- the sand: a flat floor that banks up toward the back, and pebbles on it
        still.add(box(W, SAND, D, sandMat, 0, BASE + SAND / 2, 0));
        var slope = new THREE.Mesh(new THREE.SphereGeometry(1, 18, 6, 0, Math.PI * 2, 0, Math.PI / 2), bank);
        slope.scale.set(W / 2 - 0.02, 0.1, D * 0.45);
        slope.position.set(0, BASE + SAND - 0.03, -D / 2 + 0.02);
        slope.receiveShadow = true;
        still.add(slope);
        for (int i = 0; i < 46; i++)
        {
            double px = (hash(i) - 0.5) * (W - 0.08), pz = (hash(i + 100) - 0.5) * (D - 0.06);
            double r = 0.008 + hash(i + 200) * 0.012;
            var pebble = new THREE.Mesh(new THREE.DodecahedronGeometry(r, 0), pebbleMats[i % 3]);
            double rise = pz < 0 ? 0.07 * Math.Max(0, 1 - Math.Pow(px / (W / 2), 2)) * Math.Max(0, -pz / (D / 2)) : 0;
            pebble.position.set(px, BASE + SAND + rise + r * 0.3, pz);
            pebble.rotation.set(i, i * 0.7, i * 1.3);
            pebble.scale.y = 0.6;
            still.add(pebble);
        }
        // ---- rocks: a pile at the back right (the octopus's corner) and the bubbler's stone at the back left
        void stone(double x, double y, double z, double r, double sy, THREE.Material mat, int seed)
        {
            var m = new THREE.Mesh(new THREE.DodecahedronGeometry(r, 1), mat);
            m.position.set(x, BASE + SAND + y, z);
            m.rotation.set(seed * 0.9, seed * 2.1, seed * 0.4);
            m.scale.set(1.1, sy, 0.9);
            m.castShadow = true;
            m.receiveShadow = true;
            still.add(m);
        }
        stone(0.66, 0.06, -0.12, 0.12, 0.75, rock, 1);
        stone(0.5, 0.04, -0.15, 0.08, 0.7, rockDark, 2);
        stone(0.78, 0.04, 0.02, 0.07, 0.6, rockDark, 3);
        stone(0.6, 0.15, -0.14, 0.06, 0.8, rock, 4);
        stone(BUBBLER.x, 0.02, BUBBLER.z, 0.05, 0.6, rockDark, 5);
        stone(-0.3, 0.02, 0.14, 0.035, 0.6, rock, 6);
        // ---- a piece of driftwood leaning up from the middle of the back
        void branch(double x0, double y0, double z0, double len, double r, double lean)
        {
            // stands at (x0, y0, z0) and leans `lean` radians toward +x (negative: toward -x)
            var m = new THREE.Mesh(new THREE.CylinderGeometry(r * 0.6, r, len, 7), drift);
            m.position.set(x0 + Math.Sin(lean) * len / 2, BASE + SAND + y0 + Math.Cos(lean) * len / 2, z0);
            m.rotation.set(0.12, 0, -lean);
            m.castShadow = true;
            still.add(m);
        }
        // one log lying low and rising to the right, a fork growing up from its middle and a short stub at its foot
        branch(-0.27, 0.0, -0.16, 0.5, 0.028, 1.2);
        branch(-0.03, 0.085, -0.155, 0.22, 0.016, 0.35);
        branch(-0.26, 0.01, -0.16, 0.1, 0.018, -0.75);
        // ---- plants: tall ribbons in the back corners and along the back, small tufts in front, moss balls
        var footGreen = new THREE.Color(0x1f5f36);
        void clump(double x, double z, int n, double h, double w, THREE.Color tip, int seed)
        {
            for (int i = 0; i < n; i++)
            {
                double k = hash(seed * 31 + i);
                double hh = h * (0.65 + 0.35 * k);
                var m = new THREE.Mesh(blade(hh, w, (hash(seed * 17 + i) - 0.5) * hh * 0.7, 7, footGreen, tip), plantMat);
                double a = i * 2.4 + seed;
                m.position.set(x + Math.Cos(a) * 0.025 * (i % 3), BASE + SAND - 0.005, z + Math.Sin(a) * 0.02 * (i % 3));
                m.rotation.y = a;
                m.castShadow = false;
                still.add(m);
            }
        }
        var tall = new THREE.Color(0x6cc46a);
        var teal = new THREE.Color(0x4fb59a);
        var lime = new THREE.Color(0xa4d65e);
        clump(-0.8, -0.12, 7, 0.6, 0.03, tall, 1);
        clump(-0.58, -0.19, 5, 0.42, 0.028, teal, 2);
        clump(0.83, -0.18, 6, 0.55, 0.03, tall, 3);
        clump(0.3, -0.19, 5, 0.38, 0.026, teal, 4);
        clump(-0.28, -0.2, 4, 0.3, 0.024, tall, 5);
        clump(-0.55, 0.14, 6, 0.08, 0.018, lime, 6);
        clump(0.12, 0.17, 5, 0.07, 0.018, lime, 7);
        clump(0.52, 0.16, 6, 0.09, 0.018, lime, 8);
        clump(-0.05, 0.12, 4, 0.06, 0.016, lime, 9);
        foreach (var (mx, mz, mr) in new[] { (-0.42, 0.1, 0.035), (-0.37, 0.15, 0.025), (0.35, 0.1, 0.03) })
        {
            var ball = new THREE.Mesh(new THREE.IcosahedronGeometry(mr, 1), moss);
            ball.position.set(mx, BASE + SAND + mr * 0.85, mz);
            still.add(ball);
        }
        // ---- corals: a branching pink one and a round orange one by the rocks
        void twig(double x, double y, double z, double len, double r, double lean, double turn, int depth)
        {
            // grows from (x, y, z), `lean` radians off upright, toward the compass direction `turn`
            double dx = Math.Sin(lean) * Math.Cos(turn) * len, dy = Math.Cos(lean) * len, dz = Math.Sin(lean) * Math.Sin(turn) * len;
            var m = new THREE.Mesh(new THREE.CylinderGeometry(r * 0.75, r, len, 6), coralPink);
            m.position.set(x + dx / 2, y + dy / 2, z + dz / 2);
            m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), new THREE.Vector3(dx, dy, dz).normalize());
            still.add(m);
            if (depth == 0)
            {
                var tipBall = new THREE.Mesh(new THREE.SphereGeometry(r * 0.9, 6, 5), coralPink);
                tipBall.position.set(x + dx, y + dy, z + dz);
                still.add(tipBall);
                return;
            }
            twig(x + dx, y + dy, z + dz, len * 0.72, r * 0.75, lean + 0.45, turn + 0.9, depth - 1);
            twig(x + dx, y + dy, z + dz, len * 0.72, r * 0.75, lean + 0.4, turn + 3.6, depth - 1);
        }
        twig(0.2, BASE + SAND, -0.02, 0.07, 0.016, 0.1, 0.0, 2);
        twig(0.24, BASE + SAND, 0.03, 0.05, 0.013, 0.3, 2.0, 1);
        var brain = new THREE.Mesh(new THREE.IcosahedronGeometry(0.055, 1), coralOrange);
        brain.position.set(0.42, BASE + SAND + 0.025, 0.02);
        brain.scale.y = 0.6;
        still.add(brain);
        // ---- shells on the sand at the front
        var conch = new THREE.Mesh(new THREE.ConeGeometry(0.018, 0.05, 7), shell);
        conch.position.set(-0.12, BASE + SAND + 0.014, 0.17);
        conch.rotation.set(0, 0.6, Math.PI / 2.3);
        still.add(conch);
        var clam = new THREE.Mesh(new THREE.SphereGeometry(0.024, 10, 5, 0, Math.PI * 2, 0, Math.PI / 2), shellPink);
        clam.position.set(0.66, BASE + SAND, 0.16);
        clam.scale.set(1, 0.45, 0.85);
        still.add(clam);
        Merge.mergeStatic(still);

        // ---- the bubbler's bubbles, rising and starting again at the stone (they move: see update)
        for (int i = 0; i < 9; i++)
        {
            var b = new THREE.Mesh(new THREE.SphereGeometry(0.007 + (i % 3) * 0.003, 8, 6), bubbleMat);
            b.renderOrder = 1;
            bubbles.Add(b);
            life.add(b);
        }
        // ---- the water (drawn after everything in it, so the life inside shows through), its surface and the glass
        var waterBox = new THREE.Mesh(new THREE.BoxGeometry(W, H - 0.06, D), water);
        waterBox.position.set(0, BASE + (H - 0.06) / 2, 0);
        waterBox.renderOrder = 2;
        var top = new THREE.Mesh(new THREE.PlaneGeometry(W, D), surface);
        top.rotation.x = -Math.PI / 2;
        top.position.set(0, BASE + H - 0.06, 0);
        top.renderOrder = 2;
        var line = new THREE.Mesh(new THREE.BoxGeometry(W, 0.006, 0.004), surface);
        line.position.set(0, BASE + H - 0.06, D / 2 - 0.004);
        line.renderOrder = 2;
        var panes = new THREE.Group();
        const double g = 0.006;
        foreach (var sz in new[] { -1, 1 }) panes.add(box(W + g * 2, H, g, glass, 0, BASE + H / 2, sz * (D / 2 + g / 2)));
        foreach (var sx in new[] { -1, 1 }) panes.add(box(g, H, D, glass, sx * (W / 2 + g / 2), BASE + H / 2, 0));
        // the green of thick glass seen edge on, at the four upright corners
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                panes.add(box(g * 1.6, H - f * 2, g * 1.6, glassEdge, sx * (W / 2 + g / 2), BASE + H / 2, sz * (D / 2 + g / 2)));
        panes.traverse(o => { if (o is THREE.Mesh m) { m.castShadow = false; m.receiveShadow = false; m.renderOrder = 3; } });
        group.add(still, life, waterBox, top, line, panes);
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
        // the bubbles: each rises from the stone to the surface, wobbling a little, and starts again
        for (int i = 0; i < bubbles.Count; i++)
        {
            double rise = H - SAND - 0.12;
            double k = (t * 0.22 + i / (double)bubbles.Count) % 1;
            bubbles[i].position.set(BUBBLER.x + Math.Sin(t * 5 + i * 1.7) * 0.008 * k, BASE + SAND + 0.05 + k * rise, BUBBLER.z + Math.Cos(t * 4 + i) * 0.006 * k);
        }
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
