using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

/// <summary>
/// Old fishing nets strung on driftwood posts round the treasure cove, the sand where the tide leaves its finds, and round the
/// camp above it. They run down both sides and across the top, a good way behind the camp, with wide gaps to walk through;
/// banners hang in the gaps. Inside the nets things can be dug up (on the open sand below the camp); outside them there is
/// only sand.
/// </summary>
public class CoveNets
{
    /// <summary>where the nets stand: just outside the diggable sand at the sides, and well behind the camp at the top</summary>
    public const double SIDE_X = HALF - 1.2, TOP_Z = CAMP_Z - 22, BOTTOM_Z = SHORE_Z - 6;

    public readonly THREE.Group group = new THREE.Group();
    /// <summary>what blocks walking and driving: the posts, and the nets between them</summary>
    public readonly List<Spot> colliders = new List<Spot>();

    /// <summary>True on the sand inside the nets, where things are buried.</summary>
    public static bool inCove(double x, double z) => Math.Abs(x) < SIDE_X && z > TOP_Z && z < SHORE_Z + 2;

    private readonly Func<double> rand = Stones.rng(9191);
    private readonly List<THREE.BufferGeometry> wood = new List<THREE.BufferGeometry>(), net = new List<THREE.BufferGeometry>(),
        rope = new List<THREE.BufferGeometry>(), floats = new List<THREE.BufferGeometry>();
    private readonly List<(THREE.Mesh mesh, double phase)> banners = new List<(THREE.Mesh, double)>();

    private const double POST_GAP = 3.2, NET_H = 1.15;

    public CoveNets(IList<Scenery.Trunk> trunks)
    {
        // each run of net is a line, with the gaps along it to walk through (as distances along the line)
        var runs = new List<(double x0, double z0, double x1, double z1, (double from, double to)[] gaps)>
        {
            // across the top, behind the camp: a wide gap straight up from it, and one toward each side
            (-SIDE_X, TOP_Z, SIDE_X, TOP_Z, new[] { (SIDE_X - 8, SIDE_X + 8), (13.0, 19.0), (2 * SIDE_X - 19, 2 * SIDE_X - 13) }),
            // down each side, with three gaps
            (-SIDE_X, TOP_Z, -SIDE_X, BOTTOM_Z, new[] { (22.0, 28.0), (56.0, 62.0), (88.0, 94.0) }),
            (SIDE_X, TOP_Z, SIDE_X, BOTTOM_Z, new[] { (16.0, 22.0), (50.0, 56.0), (82.0, 88.0) }),
        };
        foreach (var run in runs) buildRun(run.x0, run.z0, run.x1, run.z1, run.gaps, trunks);

        var netTex = netTexture();
        netTex.wrapS = netTex.wrapT = THREE.Wrapping.RepeatWrapping;
        add(wood, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1 }, true);
        add(net, new THREE.MeshStandardMaterial { map = netTex, color = 0xe2d6b8, roughness = 1, transparent = true, alphaTest = 0.35, side = THREE.Side.DoubleSide }, false);
        add(rope, new THREE.MeshStandardMaterial { color = 0xb8a47c, roughness = 1 }, true);
        add(floats, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.7 }, true);
    }

    private void add(List<THREE.BufferGeometry> parts, THREE.Material mat, bool shadow)
    {
        if (parts.Count == 0) return;
        var g = THREE.BufferGeometryUtils.mergeGeometries(parts, false);
        g.computeVertexNormals();
        var m = new THREE.Mesh(g, mat);
        m.castShadow = shadow;
        m.receiveShadow = true;
        group.add(m);
    }

    /// <summary>A diamond mesh of knotted twine, see-through between the strands.</summary>
    private static THREE.CanvasTexture netTexture()
    {
        var cv = new HTMLCanvasElement(64, 64);
        var c = cv.getContext("2d");
        c.clearRect(0, 0, 64, 64);
        c.strokeStyle = "rgba(255,255,255,1)";
        c.lineWidth = 3;
        c.beginPath();
        c.moveTo(0, 32); c.lineTo(32, 0); c.lineTo(64, 32); c.lineTo(32, 64); c.closePath();
        c.moveTo(-32, 32); c.lineTo(0, 0);
        c.stroke();
        c.fillStyle = "rgba(255,255,255,1)";
        foreach (var (x, y) in new[] { (0, 32), (32, 0), (64, 32), (32, 64) })
        {
            c.beginPath();
            c.arc(x, y, 3.2, 0, Math.PI * 2);
            c.fill();
        }
        return new THREE.CanvasTexture(cv);
    }

    private void buildRun(double x0, double z0, double x1, double z1, (double from, double to)[] gaps, IList<Scenery.Trunk> trunks)
    {
        double len = JS.Hypot(x1 - x0, z1 - z0), ux = (x1 - x0) / len, uz = (z1 - z0) / len;
        bool inGap(double s) => gaps.Any(g => s > g.from && s < g.to);
        // a palm standing on the line makes a gap of its own: the net is left off round it
        bool nearPalm(double s)
        {
            double x = x0 + ux * s, z = z0 + uz * s;
            return trunks.Any(t => JS.Hypot(t.x - x, t.z - z) < 1.4);
        }
        // posts every few metres, with a post at each side of every gap
        var stops = new List<double>();
        for (double s = 0; s <= len + 0.01; s += POST_GAP) stops.Add(Math.Min(s, len));
        foreach (var g in gaps) { stops.Add(g.from); stops.Add(g.to); }
        stops = stops.Where(s => !inGap(s)).Distinct().OrderBy(s => s).ToList();
        for (int i = 0; i < stops.Count; i++)
        {
            double s = stops[i];
            if (nearPalm(s)) continue;
            post(x0 + ux * s, z0 + uz * s);
            if (i + 1 >= stops.Count) continue;
            double s2 = stops[i + 1];
            if (inGap((s + s2) / 2) || nearPalm(s2) || nearPalm((s + s2) / 2)) continue;
            panel(x0 + ux * s, z0 + uz * s, x0 + ux * s2, z0 + uz * s2);
        }
        // a banner in every gap, on two tall poles, facing out of the cove (and the other side facing in)
        foreach (var g in gaps)
        {
            double sm = (g.from + g.to) / 2;
            double mx = x0 + ux * sm, mz = z0 + uz * sm;
            banner(mx, mz, ux, uz, Math.Min(4.2, g.to - g.from - 1));
        }
    }

    private static int shade(Func<double> rand) => new[] { 0xa08a6a, 0x8f7a5c, 0xb09a78 }[(int)Math.Floor(rand() * 3)];

    /// <summary>A driftwood post, leaning a little, with a knot of rope at its top.</summary>
    private void post(double x, double z)
    {
        double y = baseHeight(x, z);
        double h = NET_H + 0.35 + rand() * 0.2;
        var p = new THREE.CylinderGeometry(0.06, 0.08, h + 0.4, 6);
        p.translate(0, h / 2 - 0.2, 0);
        p.rotateX((rand() - 0.5) * 0.12);
        p.rotateZ((rand() - 0.5) * 0.12);
        p.translate(x, y, z);
        wood.Add(Ecology.paint(p, shade(rand)));
        colliders.Add(new Spot(x, z, 0.2));
    }

    /// <summary>
    /// The net between two posts: hung from a rope, sagging in the middle, its lower edge resting on the sand, with cork floats
    /// along the rope. It blocks the way like the posts do.
    /// </summary>
    private void panel(double ax, double az, double bx, double bz)
    {
        const int N = 8;
        double len = JS.Hypot(bx - ax, bz - az);
        var pos = new List<double>();
        var uv = new List<double>();
        var idx = new List<int>();
        var ropePts = new List<THREE.Vector3>();
        for (int i = 0; i <= N; i++)
        {
            double t = (double)i / N, x = ax + (bx - ax) * t, z = az + (bz - az) * t;
            double ground = baseHeight(x, z);
            double sag = Math.Sin(Math.PI * t) * 0.22;
            double top = ground + NET_H - sag, bottom = ground + 0.02;
            // the net hangs a little loose: its foot lies a hand's width to one side
            double off = Math.Sin(Math.PI * t) * 0.06;
            double nx = -(bz - az) / len * off, nz = (bx - ax) / len * off;
            pos.AddRange(new[] { x + nx, bottom, z + nz, x, top, z });
            uv.AddRange(new[] { t * len / 0.45, 0, t * len / 0.45, (top - bottom) / 0.45 });
            ropePts.Add(new THREE.Vector3(x, top, z));
            if (i < N) idx.AddRange(new[] { i * 2, i * 2 + 2, i * 2 + 1, i * 2 + 1, i * 2 + 2, i * 2 + 3 });
            if (i > 0 && i < N && i % 2 == 0)
            {
                var f = new THREE.CylinderGeometry(0.07, 0.07, 0.12, 8);
                f.rotateZ(Math.PI / 2);
                f.rotateY(-Math.Atan2(bz - az, bx - ax));
                f.translate(x, top, z);
                floats.Add(Ecology.paint(f, rand() < 0.75 ? 0xc9a26a : 0xd8573a));
            }
            colliders.Add(new Spot(x, z, 0.25));
        }
        var g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setAttribute("uv", new THREE.Float32BufferAttribute(uv, 2));
        g.setIndex(idx);
        net.Add(g);
        rope.Add(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(ropePts), 16, 0.018, 4));
    }

    /// <summary>The words on each side of a banner.</summary>
    private static THREE.CanvasTexture bannerTexture(bool outside)
    {
        var cv = new HTMLCanvasElement(512, 192);
        var c = cv.getContext("2d");
        c.fillStyle = outside ? "#f1e2bf" : "#e9dcc0";
        c.fillRect(0, 0, 512, 192);
        // a painted border and two little shells in the corners
        c.strokeStyle = "#a8432f";
        c.lineWidth = 8;
        c.strokeRect(10, 10, 492, 172);
        c.fillStyle = "#2c4a5a";
        c.textAlign = "center";
        // just the title, large enough to read from across the sand
        c.textBaseline = "middle";
        c.font = outside ? "bold 44px Georgia, serif" : "bold 44px Georgia, serif";
        c.fillText(outside ? "TREASURE COVE" : "Beyond the nets", 256, 98);
        return new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
    }

    private static THREE.CanvasTexture outsideTex, insideTex;

    /// <summary>A cloth banner between two tall poles in a gap, one face to the outside of the cove and one to the inside.</summary>
    private void banner(double x, double z, double ux, double uz, double width)
    {
        outsideTex ??= bannerTexture(true);
        insideTex ??= bannerTexture(false);
        double y = baseHeight(x, z);
        const double top = 3.1, h = 1.1;
        // the poles stand at the ends of the banner, just inside the gap's posts
        foreach (var s in new[] { -1, 1 })
        {
            double px = x + ux * s * (width / 2 + 0.1), pz = z + uz * s * (width / 2 + 0.1);
            var p = new THREE.CylinderGeometry(0.06, 0.08, top + 0.6, 6);
            p.translate(px, baseHeight(px, pz) + (top + 0.6) / 2 - 0.4, pz);
            wood.Add(Ecology.paint(p, 0x8f7a5c));
            var cap = new THREE.SphereGeometry(0.1, 8, 6);
            cap.translate(px, baseHeight(px, pz) + top + 0.22, pz);
            wood.Add(Ecology.paint(cap, 0xa8432f));
            colliders.Add(new Spot(px, pz, 0.2));
        }
        // which way is out of the cove: away from the middle of the beach
        double nx = -uz, nz = ux;
        if (nx * x + nz * (z - 0) < 0) { nx = -nx; nz = -nz; }
        double yaw = Math.Atan2(nx, nz);
        foreach (var (tex, flip) in new[] { (outsideTex, 0.0), (insideTex, Math.PI) })
        {
            var cloth = new THREE.PlaneGeometry(width, h, 10, 2);
            var cp = cloth.attributes.position;
            for (int i = 0; i < cp.count; i++)
            {
                // a gentle belly in the cloth, and a little sag in the middle of the top edge
                double u = cp.getX(i) / (width / 2);
                cp.setZ(i, (1 - u * u) * 0.08);
                cp.setY(i, cp.getY(i) - (1 - u * u) * 0.05);
            }
            cloth.computeVertexNormals();
            var m = new THREE.Mesh(cloth, new THREE.MeshStandardMaterial { map = tex, roughness = 1 });
            m.castShadow = true;
            m.rotation.order = "YXZ";
            m.rotation.y = yaw + flip;
            m.position.set(x + nx * (flip == 0 ? 0.015 : -0.015), y + top - h / 2, z + nz * (flip == 0 ? 0.015 : -0.015));
            group.add(m);
            banners.Add((m, rand() * 6));
        }
    }

    public void update(double t)
    {
        // the banners stir in the breeze
        foreach (var (m, ph) in banners) m.rotation.x = Math.Sin(t * 1.3 + ph) * 0.05;
    }
}
