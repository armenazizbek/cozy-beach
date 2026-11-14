using System;
using System.Collections.Generic;

// Port of src/world/Beach.ts
//
// One change for Unity: in Three.js every chunk mesh pointed at the same 521x521 vertex buffers. A Unity mesh owns its vertices, so
// here each chunk keeps its own 53x53 slice of the grid; the master arrays below are the truth, and digging copies the changed
// part into the slices it touches. Heights, colours, normals and the level-of-detail rules are unchanged.

public class IslandSpec
{
    public double x, z, R;
    /// <summary>0 the green island with the ruins, 1 the ring of sand around a lagoon, 2 the dark volcanic rock</summary>
    public int kind;
    /// <summary>what the maps call it</summary>
    public string name;

    /// <summary>True within the square of sea this island's ground can lie in.</summary>
    public bool near(double px, double pz) => Math.Abs(px - x) <= R * 1.2 && Math.Abs(pz - z) <= R * 1.2;
}

public class SeaSpec
{
    public double x, z;
}

public class Beach
{
    public const double BEACH_SIZE = 140;
    public const int BEACH_SEGMENTS = 520;
    public const double HALF = BEACH_SIZE / 2;
    public const double CAMP_Z = -60;
    public const double WATER_LEVEL = -0.3;
    public const double WET_Z = 31;
    public const double SHORE_Z = 40;
    public const double DIG_MIN_Z = -45;
    private const double MAX_DUG = 1.6;
    private const double MIN_DUG = -0.3; // piled-up sand around a hole sits above the surface

    public static double smooth(double a, double b, double x)
    {
        double t = Math.Min(1, Math.Max(0, (x - a) / (b - a)));
        return t * t * (3 - 2 * t);
    }

    private static double rawHeight(double x, double z)
    {
        double slope = 0.95 - (z + 22.5) * 0.02;
        double calm = 1 - smooth(22, 36, z) * 0.85; // flatter sand near the water keeps the shoreline tidy
        double dune = (Math.Sin(x * 0.23 + z * 0.11) * 0.07 + Math.Sin(x * 0.09 - z * 0.17) * 0.06 + Math.Sin(x * 0.5 + z * 0.41) * 0.015) * calm;
        double ridge = smooth(-50, -68, z) * 0.7;
        // rolling dune hills inland, well away from the diggable sand
        double hills = smooth(-62, -78, z) * (Math.Sin(x * 0.07 + z * 0.05) * 1.1 + Math.Sin(x * 0.13 - z * 0.09) * 0.6);
        // and further back, the long dunes that climb toward the mountains. They come in gently behind the playable square,
        // so the ground the player walks on out there is the ground that is drawn
        double back = z < -HALF ? smooth(-HALF, -HALF - 15, z) * (Math.Sin(x * 0.04) * 1.2 + Math.Sin(x * 0.11 + z * 0.05) * 0.5 + (-z - HALF) * 0.02) : 0;
        return slope + dune + ridge + hills + back;
    }

    public static readonly double CAMP_Y = rawHeight(0, CAMP_Z);

    /// <summary>A small island out at sea, a boat ride from the beach.</summary>
    public static readonly IslandSpec ISLAND = new IslandSpec { x = -14, z = 200, R = 22, kind = 0, name = "Island" };

    /// <summary>A ring of pale sand around a lagoon, far out to the east. A gap in the ring lets a boat in.</summary>
    public static readonly IslandSpec LAGOON = new IslandSpec { x = 196, z = 292, R = 34, kind = 1, name = "Lagoon Cay" };

    /// <summary>A dark volcanic cone with a lighthouse on its summit, far out to the west.</summary>
    public static readonly IslandSpec EMBER = new IslandSpec { x = -204, z = 312, R = 30, kind = 2, name = "Ember Rock" };

    /// <summary>Every island in the sea. The first is the one with the ruins.</summary>
    public static readonly IslandSpec[] ISLANDS = { ISLAND, LAGOON, EMBER };

    /// <summary>The way through the lagoon's ring of sand, as an angle from its middle: it faces the first island and the beach.</summary>
    public const double LAGOON_GAP = -2.45;

    /// <summary>How far out to sea the boat can go and the sea creatures roam: out to x = +-290 and z = 420.</summary>
    public static readonly SeaSpec SEA = new SeaSpec { x = 290, z = 420 };

    /// <summary>
    /// How far to either side the sea is open at a distance z from the land. Close in it is as narrow as it always was (the
    /// mountains come down to the coast on both sides of the beach); further out it opens to its full width.
    /// </summary>
    public static double seaHalfWidth(double z) => 150 + (SEA.x - 150) * smooth(100, 190, z);

    /// <summary>The island this point belongs to, or null out on the open sea and on the beach.</summary>
    public static IslandSpec islandNear(double x, double z)
    {
        foreach (var s in ISLANDS) if (s.near(x, z)) return s;
        return null;
    }

    /// <summary>The smallest turn from angle b to angle a, in -pi..pi.</summary>
    private static double turn(double a, double b) => Math.Atan2(Math.Sin(a - b), Math.Cos(a - b));

    /// <summary>Height of the islands at a point, or a very low number away from them so the sea floor wins there.</summary>
    public static double islandHeight(double x, double z)
    {
        var s = islandNear(x, z);
        if (s == null) return -10;
        double dx = x - s.x, dz = z - s.z;
        double d = JS.Hypot(dx, dz);
        if (d > s.R * 1.2) return -10;
        double a = Math.Atan2(dz, dx);
        return s.kind == 1 ? lagoonHeight(s, x, z, d, a) : s.kind == 2 ? emberHeight(s, x, z, d, a) : greenHeight(s, x, z, d, a);
    }

    private static double greenHeight(IslandSpec s, double x, double z, double d, double a)
    {
        // an uneven coast, not a perfect circle
        double rn = s.R * (1 + 0.16 * Math.Sin(3 * a + 1.2) + 0.09 * Math.Sin(5 * a + 0.4) + 0.05 * Math.Sin(9 * a));
        double t = d / rn;
        if (t >= 1) return -10;
        double h = -2.2 + 6.4 * (1 - t * t);
        // a flat summit for the ruins
        const double top = 3.7;
        if (h > top) h = top + (h - top) * 0.12;
        // rolling land above the water, and a cliff on the western side
        double land = smooth(-0.3, 1.0, h);
        h += land * (Math.Sin(x * 0.31 + z * 0.17) * 0.28 + Math.Sin(x * 0.13 - z * 0.27) * 0.35 + Math.Sin(x * 0.6 + z * 0.5) * 0.06);
        double cliff = Math.Exp(-Math.Pow(Math.Atan2(Math.Sin(a - 3.0), Math.Cos(a - 3.0)) / 0.45, 2));
        h += land * cliff * 1.3 * smooth(0.15, 0.6, t);
        return h;
    }

    /// <summary>
    /// A low ring of sand: a ridge between the open sea and a shallow lagoon, cut through on one side so a boat can sail in.
    /// The side across from the gap is a little higher, a dune to camp on.
    /// </summary>
    private static double lagoonHeight(IslandSpec s, double x, double z, double d, double a)
    {
        double rn = s.R * (1 + 0.07 * Math.Sin(2 * a + 0.7) + 0.045 * Math.Sin(5 * a + 2.1) + 0.025 * Math.Sin(9 * a + 1.0));
        double t = d / rn;
        if (t >= 1) return -10;
        // the lagoon floor inside, falling away to the sea floor outside
        double floor = -1.35 - 1.05 * smooth(0.66, 1.0, t);
        double ridge = Math.Exp(-Math.Pow((t - 0.66) / 0.17, 2));
        double gap = Math.Exp(-Math.Pow(turn(a, LAGOON_GAP) / 0.4, 2));
        double dune = Math.Exp(-Math.Pow(turn(a, LAGOON_GAP + Math.PI) / 0.7, 2));
        double h = floor + ridge * (3.3 + 0.9 * dune) * (1 - gap);
        double land = smooth(-0.3, 0.8, h);
        h += land * (Math.Sin(x * 0.37 + z * 0.21) * 0.14 + Math.Sin(x * 0.17 - z * 0.33) * 0.16);
        return h;
    }

    /// <summary>A volcanic cone: an apron of black sand, a steep cone of rock in the middle of it and a hollow crater in its top.</summary>
    private static double emberHeight(IslandSpec s, double x, double z, double d, double a)
    {
        double rn = s.R * (1 + 0.12 * Math.Sin(3 * a + 0.5) + 0.07 * Math.Sin(4 * a + 2.0) + 0.04 * Math.Sin(7 * a));
        double t = d / rn;
        if (t >= 1) return -10;
        double h = -2.2 + 5.2 * (1 - t * t);
        // the apron: nearly level black sand
        const double flat = 0.9;
        if (h > flat) h = flat + (h - flat) * 0.12;
        double land = smooth(-0.3, 0.8, h);
        h += land * (Math.Sin(x * 0.29 + z * 0.19) * 0.16 + Math.Sin(x * 0.15 - z * 0.31) * 0.18);
        // the cone, with gullies down its sides, and a level top for the lighthouse to stand on
        double tc = d / s.R;
        double cone = smooth(0.62, 0.22, tc);
        h += cone * (7.4 + Math.Sin(5 * a + 0.8) * 0.4 * smooth(0.2, 0.4, tc));
        return h;
    }

    /// <summary>Undisturbed sand height of the main beach and the sea floor. The camp site is levelled so props sit flat.</summary>
    public static double baseHeightMain(double x, double z)
    {
        double d = JS.Hypot(x, z - CAMP_Z);
        double k = 1 - smooth(10, 14, d);
        return rawHeight(x, z) * (1 - k) + CAMP_Y * k;
    }

    /// <summary>Height of everything: the beach, the sea floor and the island.</summary>
    public static double baseHeight(double x, double z) => Math.Max(baseHeightMain(x, z), islandHeight(x, z));

    /// <summary>The beach is cut into squares of this many grid cells, each drawn at the detail its distance needs.</summary>
    private const int CHUNK_CELLS = 52;
    /// <summary>every how many grid cells a detail level keeps a vertex</summary>
    private static readonly int[] LOD_STEPS = { 1, 2, 4 };
    /// <summary>metres from the camera at which a chunk drops to the next level</summary>
    private const double LOD_NEAR = 16;
    private const double LOD_MID = 45;
    /// <summary>a chunk with holes in it stays at this level or finer, so a hole keeps its shape from afar</summary>
    private const int LOD_DUG = 1;

    /// <summary>
    /// The triangles of one chunk at one detail level. The rim of a chunk is always kept at full detail, so neighbours at different levels meet
    /// exactly and no gap can open between them; the coarser middle is stitched to that rim.
    /// </summary>
    private static List<int> chunkIndex(int r0, int c0, int step, int n)
    {
        const int N = CHUNK_CELLS;
        int at(int r, int c) => (r0 + r) * n + c0 + c;
        var idx = new List<int>();
        void quad(int r, int c, int s)
        {
            int a = at(r, c), b = at(r + s, c), e = at(r + s, c + s), d = at(r, c + s);
            idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(b); idx.Add(e); idx.Add(d);
        }
        if (step == 1)
        {
            for (int r = 0; r < N; r++) for (int c = 0; c < N; c++) quad(r, c, 1);
            return idx;
        }
        for (int r = step; r < N - step; r += step) for (int c = step; c < N - step; c += step) quad(r, c, step);
        List<int> ring(int lo, int hi, int s)
        {
            var o = new List<int>();
            for (int c = lo; c < hi; c += s) o.Add(at(lo, c));
            for (int r = lo; r < hi; r += s) o.Add(at(r, hi));
            for (int c = hi; c > lo; c -= s) o.Add(at(hi, c));
            for (int r = hi; r > lo; r -= s) o.Add(at(r, lo));
            return o;
        }
        var outer = ring(0, N, 1);
        var inner = ring(step, N - step, step);
        int i = 0, j = 0;
        while (i < outer.Count || j < inner.Count)
        {
            int ov = outer[i % outer.Count];
            int w = inner[j % inner.Count];
            if (j >= inner.Count || (i < outer.Count && (double)(i + 1) / outer.Count <= (double)(j + 1) / inner.Count))
            {
                idx.Add(outer[(i + 1) % outer.Count]); idx.Add(ov); idx.Add(w);
                i++;
            }
            else
            {
                idx.Add(w); idx.Add(inner[(j + 1) % inner.Count]); idx.Add(ov);
                j++;
            }
        }
        return idx;
    }

    private static readonly THREE.Color DRY = new THREE.Color("#e6ccb2");
    private static readonly THREE.Color WET = new THREE.Color("#b08968");
    private static readonly THREE.Color DARK = new THREE.Color("#7f5539");

    /// <summary>Coarse terrain ring around the playable beach so the world never just ends.</summary>
    public static THREE.Mesh createSurround()
    {
        const double size = 1300;
        const int seg = 260; // 5m cells, so the playable square lines up exactly with the grid
        var geo = new THREE.PlaneGeometry(size, size, seg, seg);
        geo.rotateX(-Math.PI / 2);
        var pos = geo.attributes.position;
        var col = new float[pos.count * 3];
        var c = new THREE.Color();
        var far = new THREE.Color("#d9b98c");
        for (int i = 0; i < pos.count; i++)
        {
            double x = pos.getX(i), z = pos.getZ(i);
            // the same height function as the beach (including the levelled camp pad and the dunes behind), so the two meet
            // cleanly and the player walks on what is drawn
            double y = baseHeightMain(x, z);
            y = Math.Max(y, -2.2);
            // the ring of vertices touching the beach sits a hair below it. If the coarse terrain were even slightly higher
            // than the beach edge, the gap between them would show the sky; the skirt under the beach's edge covers the rest.
            // Only a hair: walking out there, a step down onto it would show
            bool onEdge = (Math.Abs(Math.Abs(x) - HALF) < 0.01 && Math.Abs(z) <= HALF + 0.01) || (Math.Abs(Math.Abs(z) - HALF) < 0.01 && Math.Abs(x) <= HALF + 0.01);
            if (onEdge) y -= 0.05;
            pos.setY(i, y);
            c.copy(DRY).lerp(WET, smooth(WET_Z - 2.5, WET_Z + 2, z));
            c.lerp(far, smooth(-HALF, -HALF - 60, z) * 0.5);
            col[i * 3] = (float)c.r; col[i * 3 + 1] = (float)c.g; col[i * 3 + 2] = (float)c.b;
        }
        geo.setAttribute("color", new THREE.BufferAttribute(col, 3));
        // leave out every triangle that lies under the playable beach, so a dug hole shows a real floor
        var index = geo.index;
        var kept = new List<int>();
        bool inBeach(int v) => Math.Abs(pos.getX(v)) <= HALF + 0.01 && Math.Abs(pos.getZ(v)) <= HALF + 0.01;
        for (int i = 0; i < index.count; i += 3)
        {
            int a = (int)index.getX(i), b = (int)index.getX(i + 1), c2 = (int)index.getX(i + 2);
            if (!(inBeach(a) && inBeach(b) && inBeach(c2))) { kept.Add(a); kept.Add(b); kept.Add(c2); }
        }
        geo.setIndex(kept);
        geo.computeVertexNormals();
        var m = new THREE.Mesh(geo, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1, metalness = 0, bumpMap = grainTexture(size / 1.6), bumpScale = 1.4 });
        m.receiveShadow = true;
        return m;
    }

    /// <summary>
    /// A thin sand-coloured curtain hanging from the whole edge of the playable beach. The beach is
    /// finely detailed and the surrounding terrain is coarse, so along the seam they differ by a few
    /// centimetres; without this, a sliver of sky shows through that gap as a pale blue streak.
    /// </summary>
    public static THREE.Mesh createSkirt()
    {
        int n = BEACH_SEGMENTS;
        double step = BEACH_SIZE / n;
        const double depth = 5;
        var pts = new List<(double x, double z)>();
        // walk round the perimeter
        for (int i = 0; i <= n; i++) pts.Add((-HALF + i * step, -HALF));
        for (int i = 1; i <= n; i++) pts.Add((HALF, -HALF + i * step));
        for (int i = 1; i <= n; i++) pts.Add((HALF - i * step, HALF));
        for (int i = 1; i < n; i++) pts.Add((-HALF, HALF - i * step));
        pts.Add(pts[0]);
        var pos = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        var c = new THREE.Color();
        for (int i = 0; i < pts.Count; i++)
        {
            var (x, z) = pts[i];
            double h = baseHeight(x, z) - 0.02;
            c.copy(DRY).lerp(WET, smooth(WET_Z - 2.5, WET_Z + 2, z));
            pos.AddRange(new[] { x, h, z, x, h - depth, z });
            col.AddRange(new[] { c.r, c.g, c.b, c.r * 0.7, c.g * 0.7, c.b * 0.7 });
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
        return new THREE.Mesh(g, new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1, side = THREE.Side.DoubleSide });
    }

    /// <summary>Tileable speckle used as a bump map so the sand looks grainy up close.</summary>
    /// <summary>The fine sand grain for the bump map, tiled `repeat` times across the texture's UVs (the beach's by default).</summary>
    internal static THREE.CanvasTexture grainTexture(double repeat = BEACH_SIZE / 1.6)
    {
        const int size = 256;
        var c = new HTMLCanvasElement(size, size);
        var g = c.getContext("2d");
        var img = g.createImageData(size, size);
        for (int i = 0; i < size * size; i++)
        {
            byte v = (byte)(110 + JS.Random() * 90);
            img.data[i * 4] = img.data[i * 4 + 1] = img.data[i * 4 + 2] = v;
            img.data[i * 4 + 3] = 255;
        }
        g.putImageData(img, 0, 0);
        // a few soft blotches give the grain larger ripples to catch the light
        g.globalAlpha = 0.18;
        for (int i = 0; i < 90; i++)
        {
            double x = JS.Random() * size, y = JS.Random() * size, r = 6 + JS.Random() * 18;
            var grad = g.createRadialGradient(x, y, 0, x, y, r);
            bool light = JS.Random() < 0.5;
            grad.addColorStop(0, light ? "#fff" : "#000");
            grad.addColorStop(1, "rgba(128,128,128,0)");
            g.fillStyle = grad;
            foreach (var ox in new double[] { -size, 0, size })
            foreach (var oy in new double[] { -size, 0, size })
                g.fillRect(x - r + ox, y - r + oy, r * 2, r * 2);
        }
        var t = new THREE.CanvasTexture(c);
        t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
        t.repeat.set(repeat, repeat);
        t.anisotropy = 8;
        return t;
    }

    /// <summary>Tileable "turned-over damp sand": dark clods, pale pebbles and shell flecks.</summary>
    private static THREE.CanvasTexture dampTexture()
    {
        const int size = 256;
        var c = new HTMLCanvasElement(size, size);
        var g = c.getContext("2d");
        g.fillStyle = "rgb(170,160,148)";
        g.fillRect(0, 0, size, size);
        void wrap(Action<double, double> fn)
        {
            foreach (var ox in new double[] { -size, 0, size })
            foreach (var oy in new double[] { -size, 0, size })
                fn(ox, oy);
        }
        double rnd(double a, double b) => a + JS.Random() * (b - a);
        // soft dark clods
        for (int i = 0; i < 70; i++)
        {
            double x = rnd(0, size), y = rnd(0, size), r = rnd(8, 24);
            bool dark = JS.Random() < 0.7;
            wrap((ox, oy) =>
            {
                var grad = g.createRadialGradient(x + ox, y + oy, 0, x + ox, y + oy, r);
                grad.addColorStop(0, dark ? "rgba(80,66,54,0.55)" : "rgba(205,190,170,0.4)");
                grad.addColorStop(1, "rgba(0,0,0,0)");
                g.fillStyle = grad;
                g.fillRect(x + ox - r, y + oy - r, r * 2, r * 2);
            });
        }
        // fine grit
        for (int i = 0; i < 2600; i++)
        {
            double v = JS.Random() < 0.5 ? rnd(95, 135) : rnd(185, 225);
            g.fillStyle = $"rgba({JS.Str(v)},{JS.Str(v - 6)},{JS.Str(v - 14)},0.7)";
            g.fillRect(rnd(0, size), rnd(0, size), rnd(1, 2.4), rnd(1, 2.4));
        }
        // little pebbles and shell fragments with a dark underside
        for (int i = 0; i < 55; i++)
        {
            double x = rnd(0, size), y = rnd(0, size), rx = rnd(1.5, 4.5), ry = rx * rnd(0.6, 1);
            bool shell = JS.Random() < 0.3;
            string fill = shell ? "rgb(238,228,210)" : $"rgb({JS.Str(rnd(150, 200))},{JS.Str(rnd(135, 180))},{JS.Str(rnd(115, 160))})";
            wrap((ox, oy) =>
            {
                g.fillStyle = "rgba(55,44,36,0.6)";
                g.beginPath();
                g.ellipse(x + ox + 0.8, y + oy + 1, rx, ry, 0, 0, Math.PI * 2);
                g.fill();
                g.fillStyle = fill;
                g.beginPath();
                g.ellipse(x + ox, y + oy, rx, ry, 0, 0, Math.PI * 2);
                g.fill();
            });
        }
        var t = new THREE.CanvasTexture(c);
        t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
        t.anisotropy = 8;
        return t;
    }

    private class Chunk
    {
        public THREE.Mesh mesh;
        public List<int>[] lods;
        public double x, z;
        public int lod;
        public bool dug;
        public int r0, c0;
        public THREE.BufferAttribute pos, nor, col, damp;
    }

    public readonly THREE.Group mesh = new THREE.Group();
    private readonly List<Chunk> chunks = new List<Chunk>();
    private readonly THREE.PlaneGeometry geo;
    private readonly THREE.BufferAttribute pos;
    private readonly THREE.BufferAttribute colAttr;
    private readonly THREE.BufferAttribute dampAttr;
    private readonly int n = BEACH_SEGMENTS + 1;
    private readonly double step = BEACH_SIZE / BEACH_SEGMENTS;
    private readonly float[] baseH;
    private readonly float[] dug;
    private readonly float[] baseColors;
    private readonly THREE.Color tmp = new THREE.Color();

    public Beach()
    {
        geo = new THREE.PlaneGeometry(BEACH_SIZE, BEACH_SIZE, BEACH_SEGMENTS, BEACH_SEGMENTS);
        geo.rotateX(-Math.PI / 2);
        pos = geo.attributes.position;
        int count = pos.count;
        baseH = new float[count];
        dug = new float[count];
        baseColors = new float[count * 3];
        colAttr = new THREE.BufferAttribute(new float[count * 3], 3);
        geo.setAttribute("color", colAttr);
        dampAttr = new THREE.BufferAttribute(new float[count], 1);
        geo.setAttribute("damp", dampAttr);

        for (int i = 0; i < count; i++)
        {
            double x = pos.getX(i), z = pos.getZ(i);
            baseH[i] = (float)baseHeight(x, z);
            pos.setY(i, baseH[i]);
            double wet = smooth(WET_Z - 2.5, WET_Z + 2, z);
            tmp.copy(DRY).lerp(WET, wet);
            double grain = 0.96 + JS.Random() * 0.08;
            baseColors[i * 3] = (float)(tmp.r * grain);
            baseColors[i * 3 + 1] = (float)(tmp.g * grain);
            baseColors[i * 3 + 2] = (float)(tmp.b * grain);
        }
        refreshColors();
        geo.computeVertexNormals();

        buildChunks(makeMaterial());
    }

    /// <summary>Every chunk holds its own slice of the grid; the slices overlap by one row so neighbours meet exactly.</summary>
    private void buildChunks(THREE.Material mat)
    {
        double size = CHUNK_CELLS * step;
        int across = BEACH_SEGMENTS / CHUNK_CELLS;
        int ln = CHUNK_CELLS + 1;
        for (int row = 0; row < across; row++)
        {
            for (int col = 0; col < across; col++)
            {
                int r0 = row * CHUNK_CELLS, c0 = col * CHUNK_CELLS;
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                for (int r = 0; r <= CHUNK_CELLS; r++)
                for (int c = 0; c <= CHUNK_CELLS; c++)
                {
                    double y = baseH[(r0 + r) * n + c0 + c];
                    lo = Math.Min(lo, y);
                    hi = Math.Max(hi, y);
                }
                double x = -HALF + (col + 0.5) * size, z = -HALF + (row + 0.5) * size;
                var sphere = new THREE.Sphere(new THREE.Vector3(x, (lo + hi) / 2, z), JS.Hypot(size * 0.71, (hi - lo) / 2 + MAX_DUG));
                var ch = new Chunk
                {
                    x = x, z = z, r0 = r0, c0 = c0,
                    pos = new THREE.BufferAttribute(new float[ln * ln * 3], 3),
                    nor = new THREE.BufferAttribute(new float[ln * ln * 3], 3),
                    col = new THREE.BufferAttribute(new float[ln * ln * 3], 3),
                    damp = new THREE.BufferAttribute(new float[ln * ln], 1),
                };
                ch.lods = new List<int>[LOD_STEPS.Length];
                for (int k = 0; k < LOD_STEPS.Length; k++) ch.lods[k] = chunkIndex(0, 0, LOD_STEPS[k], ln);
                var g = new THREE.BufferGeometry();
                g.setAttribute("position", ch.pos);
                g.setAttribute("normal", ch.nor);
                g.setAttribute("color", ch.col);
                g.setAttribute("damp", ch.damp);
                g.setIndex(ch.lods[0]);
                g.boundingSphere = sphere;
                ch.mesh = new THREE.Mesh(g, mat);
                ch.mesh.receiveShadow = true;
                mesh.add(ch.mesh);
                chunks.Add(ch);
                CopyIntoChunk(ch, 0, n - 1, 0, n - 1);
            }
        }
    }

    /// <summary>Copies the master grid into one chunk's slice, for the rows and columns given.</summary>
    private void CopyIntoChunk(Chunk ch, int minR, int maxR, int minC, int maxC)
    {
        int ln = CHUNK_CELLS + 1;
        int r0 = Math.Max(minR, ch.r0), r1 = Math.Min(maxR, ch.r0 + CHUNK_CELLS);
        int c0 = Math.Max(minC, ch.c0), c1 = Math.Min(maxC, ch.c0 + CHUNK_CELLS);
        if (r0 > r1 || c0 > c1) return;
        var mp = pos.array;
        var mn = geo.attributes.normal.array;
        var mc = colAttr.array;
        var md = dampAttr.array;
        for (int r = r0; r <= r1; r++)
        for (int c = c0; c <= c1; c++)
        {
            int gi = r * n + c, li = (r - ch.r0) * ln + (c - ch.c0);
            ch.pos.array[li * 3] = mp[gi * 3]; ch.pos.array[li * 3 + 1] = mp[gi * 3 + 1]; ch.pos.array[li * 3 + 2] = mp[gi * 3 + 2];
            ch.nor.array[li * 3] = mn[gi * 3]; ch.nor.array[li * 3 + 1] = mn[gi * 3 + 1]; ch.nor.array[li * 3 + 2] = mn[gi * 3 + 2];
            ch.col.array[li * 3] = mc[gi * 3]; ch.col.array[li * 3 + 1] = mc[gi * 3 + 1]; ch.col.array[li * 3 + 2] = mc[gi * 3 + 2];
            ch.damp.array[li] = md[gi];
        }
        ch.pos.needsUpdate = true;
        ch.nor.needsUpdate = true;
        ch.col.needsUpdate = true;
        ch.damp.needsUpdate = true;
    }

    /// <summary>Puts every chunk on one detail level, so that loading can send them all to the graphics chip. The next frame sets them right.</summary>
    public void setAllDetail(int lod)
    {
        foreach (var c in chunks)
        {
            c.lod = lod;
            c.mesh.geometry.setIndex(c.lods[lod]);
        }
    }

    /// <summary>Picks each chunk's detail for where the camera is.</summary>
    public void updateDetail(double camX, double camZ)
    {
        double half = CHUNK_CELLS * step / 2;
        foreach (var c in chunks)
        {
            double d = JS.Hypot(Math.Max(0, Math.Abs(camX - c.x) - half), Math.Max(0, Math.Abs(camZ - c.z) - half));
            // a few metres of give, so a chunk standing at the edge does not flicker between two levels
            double give = c.lod == 0 ? 3 : 0;
            double give2 = c.lod <= 1 ? 3 : 0;
            int lod = d > LOD_MID + give2 ? 2 : d > LOD_NEAR + give ? 1 : 0;
            if (c.dug) lod = Math.Min(lod, LOD_DUG);
            if (lod != c.lod)
            {
                c.lod = lod;
                c.mesh.geometry.setIndex(c.lods[lod]);
            }
        }
    }

    /// <summary>Remembers which chunks have been dug.</summary>
    private void noteDug(int minR, int maxR, int minC, int maxC)
    {
        int across = BEACH_SEGMENTS / CHUNK_CELLS;
        int last = across - 1;
        for (int r = Math.Min(last, minR / CHUNK_CELLS); r <= Math.Min(last, maxR / CHUNK_CELLS); r++)
        for (int c = Math.Min(last, minC / CHUNK_CELLS); c <= Math.Min(last, maxC / CHUNK_CELLS); c++)
            chunks[r * across + c].dug = true;
    }

    /// <summary>Sand material that blends in a detailed damp-sand texture wherever the ground has been dug.</summary>
    private THREE.MeshStandardMaterial makeMaterial()
    {
        var damp = dampTexture();
        var mat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 1, metalness = 0, bumpMap = grainTexture(), bumpScale = 1.4 };
        // the onBeforeCompile patch lives in Resources/Shaders/CozyBeach.shader
        mat.customShader = "Cozy/Beach";
        mat.customUniforms["_DampMap"] = new THREE.Uniform(damp);
        return mat;
    }

    private int idx(double x, double z)
    {
        int c = JS.RoundInt((x + HALF) / step);
        int r = JS.RoundInt((z + HALF) / step);
        return Math.Min(n - 1, Math.Max(0, r)) * n + Math.Min(n - 1, Math.Max(0, c));
    }

    private double sample(float[] arr, double x, double z)
    {
        double fx = Math.Min(n - 1.001, Math.Max(0, (x + HALF) / step));
        double fz = Math.Min(n - 1.001, Math.Max(0, (z + HALF) / step));
        int c = (int)Math.Floor(fx), r = (int)Math.Floor(fz);
        double tx = fx - c, tz = fz - r;
        int i = r * n + c;
        double a = arr[i] * (1 - tx) + arr[i + 1] * tx;
        double b = arr[i + n] * (1 - tx) + arr[i + n + 1] * tx;
        return a * (1 - tz) + b * tz;
    }

    /// <summary>True on the finely drawn middle of the beach, the only sand that can be dug.</summary>
    public static bool inGrid(double x, double z) => Math.Abs(x) <= HALF && Math.Abs(z) <= HALF;

    // outside the middle of the beach nothing has ever been dug, and the holes' grid must not be read past its edge
    public double heightAt(double x, double z) => inGrid(x, z) ? baseHeight(x, z) - sample(dug, x, z) : baseHeight(x, z);

    public double depthAt(double x, double z) => inGrid(x, z) ? Math.Max(0, sample(dug, x, z)) : 0;

    public bool canDig(double x, double z) => Math.Abs(x) < HALF - 2 && z > DIG_MIN_Z && z < SHORE_Z - 2;

    private readonly THREE.Vector3 rayPt = new THREE.Vector3();

    /// <summary>Marches a ray against the height field.</summary>
    public THREE.Vector3 raycast(THREE.Vector3 origin, THREE.Vector3 dir, double maxDist = 140)
    {
        const double stepT = 0.2;
        var p = rayPt;
        double prevT = 0;
        for (double t = 0; t < maxDist; t += stepT)
        {
            p.copy(dir).multiplyScalar(t).add(origin);
            bool inGrid = Math.Abs(p.x) <= HALF && Math.Abs(p.z) <= HALF;
            if (!inGrid && islandNear(p.x, p.z) == null)
            {
                prevT = t;
                continue;
            }
            if (p.y <= heightAt(p.x, p.z))
            {
                double lo = prevT, hi = t;
                for (int k = 0; k < 8; k++)
                {
                    double mid = (lo + hi) / 2;
                    p.copy(dir).multiplyScalar(mid).add(origin);
                    if (p.y <= heightAt(p.x, p.z)) hi = mid;
                    else lo = mid;
                }
                p.copy(dir).multiplyScalar(hi).add(origin);
                p.y = heightAt(p.x, p.z);
                return p.clone();
            }
            prevT = t;
        }
        return null;
    }

    /// <summary>Hands the changed part of the grid to the chunks that show it (the TS marked update ranges on the GPU buffers).</summary>
    private void markRowsDirty(int minR, int maxR, int minC = 0, int maxC = int.MaxValue)
    {
        int r0 = Math.Max(0, minR - 1), r1 = Math.Min(n - 1, maxR + 1);
        int c0 = Math.Max(0, minC - 1), c1 = Math.Min(n - 1, maxC + 1);
        foreach (var ch in chunks)
        {
            if (ch.r0 > r1 || ch.r0 + CHUNK_CELLS < r0 || ch.c0 > c1 || ch.c0 + CHUNK_CELLS < c0) continue;
            CopyIntoChunk(ch, r0, r1, c0, c1);
        }
    }

    /// <summary>
    /// Digs a bowl with fairly steep walls and a wobbly, natural edge, and heaps some of the
    /// sand into a low rim around it.
    /// </summary>
    public void digAt(double x, double z, double radius, double depth)
    {
        double reach = radius * 1.8;
        int minC = Math.Max(0, (int)Math.Floor((x - reach + HALF) / step));
        int maxC = Math.Min(n - 1, (int)Math.Ceiling((x + reach + HALF) / step));
        int minR = Math.Max(0, (int)Math.Floor((z - reach + HALF) / step));
        int maxR = Math.Min(n - 1, (int)Math.Ceiling((z + reach + HALF) / step));
        double ph1 = JS.Random() * 6.283, ph2 = JS.Random() * 6.283;
        for (int r = minR; r <= maxR; r++)
        {
            for (int c = minC; c <= maxC; c++)
            {
                int i = r * n + c;
                double dx = pos.getX(i) - x, dz = pos.getZ(i) - z;
                double d = JS.Hypot(dx, dz);
                if (d >= reach) continue;
                double ang = Math.Atan2(dz, dx);
                double wobble = 1 + 0.1 * Math.Sin(ang * 5 + ph1) + 0.06 * Math.Sin(ang * 9 + ph2);
                double dn = d / wobble / radius;
                double delta = 0;
                if (dn < 1) delta = depth * (1 - dn * dn * dn);
                else if (dn < 1.8)
                {
                    double t = (dn - 1) / 0.8;
                    delta = -0.34 * depth * Math.Sin(Math.PI * Math.Pow(t, 0.6));
                }
                dug[i] = (float)Math.Min(MAX_DUG, Math.Max(MIN_DUG, dug[i] + delta));
                pos.setY(i, baseH[i] - dug[i]);
            }
        }
        noteDug(minR, maxR, minC, maxC);
        refreshColors(minR, maxR, minC, maxC);
        updateNormals(Math.Max(0, minR - 1), Math.Min(n - 1, maxR + 1), Math.Max(0, minC - 1), Math.Min(n - 1, maxC + 1));
        markRowsDirty(minR, maxR, minC, maxC);
    }

    /// <summary>Seconds of full rain for a hole to settle to a third of its depth; a whole spell of rain all but fills it.</summary>
    private const double RAIN_FILL_TIME = 20;
    private double rainClock = 0;

    /// <summary>
    /// Rain washes the sand back into the holes, and the heaps beside them down, a little at a time, until the beach is
    /// smooth again. `rain` is 0..1. Nothing changes inside the `keep` circles (x, z, radius): a find lying in its hole, the
    /// captain's hatch. Runs every half second of rain, and only over the parts of the beach that were ever dug.
    /// Returns true when it changed the sand.
    /// </summary>
    public bool rainFill(double dt, double rain, IList<(double x, double z, double r)> keep)
    {
        if (rain <= 0.3)
        {
            rainClock = 0;
            return false;
        }
        rainClock += dt * rain;
        if (rainClock < 0.5) return false;
        double k = Math.Exp(-rainClock / RAIN_FILL_TIME);
        rainClock = 0;
        bool any = false;
        foreach (var ch in chunks)
        {
            if (!ch.dug) continue;
            int r1 = Math.Min(n - 1, ch.r0 + CHUNK_CELLS), c1 = Math.Min(n - 1, ch.c0 + CHUNK_CELLS);
            bool changed = false, still = false;
            for (int r = ch.r0; r <= r1; r++)
            {
                for (int c = ch.c0; c <= c1; c++)
                {
                    int i = r * n + c;
                    if (dug[i] == 0) continue;
                    double x = pos.getX(i), z = pos.getZ(i);
                    bool kept = false;
                    foreach (var kc in keep)
                        if ((x - kc.x) * (x - kc.x) + (z - kc.z) * (z - kc.z) < kc.r * kc.r) { kept = true; break; }
                    if (kept)
                    {
                        still = true;
                        continue;
                    }
                    double v = dug[i] * k;
                    if (Math.Abs(v) < 0.01) v = 0;
                    else still = true;
                    dug[i] = (float)v;
                    pos.setY(i, baseH[i] - v);
                    changed = true;
                }
            }
            if (changed)
            {
                refreshColors(ch.r0, r1, ch.c0, c1);
                updateNormals(Math.Max(0, ch.r0 - 1), Math.Min(n - 1, r1 + 1), Math.Max(0, ch.c0 - 1), Math.Min(n - 1, c1 + 1));
                markRowsDirty(ch.r0, r1, ch.c0, c1);
                any = true;
            }
            // a chunk that is smooth again is not looked at by the next shower
            if (!still) ch.dug = false;
        }
        return any;
    }

    /// <summary>Smooths away every hole and heap within `radius` of a point, so the ground there is untouched again.</summary>
    public void clearDug(double x, double z, double radius)
    {
        int minC = Math.Max(0, (int)Math.Floor((x - radius + HALF) / step));
        int maxC = Math.Min(n - 1, (int)Math.Ceiling((x + radius + HALF) / step));
        int minR = Math.Max(0, (int)Math.Floor((z - radius + HALF) / step));
        int maxR = Math.Min(n - 1, (int)Math.Ceiling((z + radius + HALF) / step));
        for (int r = minR; r <= maxR; r++)
        {
            for (int c = minC; c <= maxC; c++)
            {
                int i = r * n + c;
                if (JS.Hypot(pos.getX(i) - x, pos.getZ(i) - z) > radius) continue;
                dug[i] = 0;
                pos.setY(i, baseH[i]);
            }
        }
        refreshColors(minR, maxR, minC, maxC);
        updateNormals(Math.Max(0, minR - 1), Math.Min(n - 1, maxR + 1), Math.Max(0, minC - 1), Math.Min(n - 1, maxC + 1));
        markRowsDirty(minR, maxR, minC, maxC);
    }

    /// <summary>
    /// Shapes a neat, round pit: a flat floor `floor` deep out to `flatR`, smooth sand walls up to
    /// the surface at `wallR` with a low rim, and a band out to `blendR` where whatever was dug before
    /// eases into it. The shape is a clamp of the old ground, so doing it again changes nothing.
    /// With a floor and rim of 0 it simply keeps the sand there untouched, as under something set in it.
    /// </summary>
    public void shapePit(double x, double z, double floor, double flatR, double wallR, double blendR, double rim = 0.07)
    {
        int minC = Math.Max(0, (int)Math.Floor((x - blendR + HALF) / step));
        int maxC = Math.Min(n - 1, (int)Math.Ceiling((x + blendR + HALF) / step));
        int minR = Math.Max(0, (int)Math.Floor((z - blendR + HALF) / step));
        int maxR = Math.Min(n - 1, (int)Math.Ceiling((z + blendR + HALF) / step));
        bool changed = false;
        for (int r = minR; r <= maxR; r++)
        {
            for (int c = minC; c <= maxC; c++)
            {
                int i = r * n + c;
                double d = JS.Hypot(pos.getX(i) - x, pos.getZ(i) - z);
                if (d >= blendR) continue;
                // the clean bowl, with a little heap of sand around its edge
                double want = floor * (1 - smooth(flatR, wallR, d)) - rim * Math.Exp(-Math.Pow((d - wallR) / 0.35, 2));
                // inside the walls the ground is exactly the bowl; further out the old ground is let back in bit by bit
                double s = smooth(wallR, blendR, d);
                double lo = want + (MIN_DUG - want) * s;
                double hi = want + (MAX_DUG - want) * s;
                double v = Math.Min(hi, Math.Max(lo, dug[i]));
                if (Math.Abs(v - dug[i]) < 1e-4) continue;
                dug[i] = (float)v;
                pos.setY(i, baseH[i] - v);
                changed = true;
            }
        }
        if (!changed) return;
        noteDug(minR, maxR, minC, maxC);
        refreshColors(minR, maxR, minC, maxC);
        updateNormals(Math.Max(0, minR - 1), Math.Min(n - 1, maxR + 1), Math.Max(0, minC - 1), Math.Min(n - 1, maxC + 1));
        markRowsDirty(minR, maxR, minC, maxC);
    }

    /// <summary>Recomputes normals only around a dig, straight from the height field.</summary>
    private void updateNormals(int minR, int maxR, int minC, int maxC)
    {
        var nrm = geo.attributes.normal;
        double h(int r, int c)
        {
            int i = Math.Min(n - 1, Math.Max(0, r)) * n + Math.Min(n - 1, Math.Max(0, c));
            return baseH[i] - dug[i];
        }
        double k = 1 / (2 * step);
        for (int r = minR; r <= maxR; r++)
        {
            for (int c = minC; c <= maxC; c++)
            {
                double nx = (h(r, c - 1) - h(r, c + 1)) * k;
                double nz = (h(r - 1, c) - h(r + 1, c)) * k;
                double len = JS.Hypot(nx, 1, nz);
                nrm.setXYZ(r * n + c, nx / len, 1 / len, nz / len);
            }
        }
    }

    private void refreshColors(int minR = 0, int maxR = -1, int minC = 0, int maxC = -1)
    {
        if (maxR < 0) maxR = n - 1;
        if (maxC < 0) maxC = n - 1;
        for (int r = minR; r <= maxR; r++)
        {
            for (int c = minC; c <= maxC; c++)
            {
                int i = r * n + c;
                double d0 = dug[i];
                // dark damp sand in the hole, slightly darker freshly-heaped sand on the rim
                double k = d0 > 0 ? Math.Min(1, d0 / 0.2) : Math.Min(1, -d0 / 0.12) * 0.4;
                double ao = 1 - Math.Min(0.42, Math.Max(0, d0) * 0.6);
                dampAttr.setX(i, k);
                for (int ch = 0; ch < 3; ch++)
                {
                    double b = baseColors[i * 3 + ch];
                    double d = ch == 0 ? DARK.r : ch == 1 ? DARK.g : DARK.b;
                    colAttr.array[i * 3 + ch] = (float)((b + (d - b) * k) * ao);
                }
            }
        }
    }

    /// <summary>Sparse copy of every dug (or heaped) vertex, for saving.</summary>
    public List<double[]> exportDug()
    {
        var o = new List<double[]>();
        for (int i = 0; i < dug.Length; i++)
            if (Math.Abs(dug[i]) > 0.002) o.Add(new double[] { i, JS.Round(dug[i] * 1000) / 1000 });
        return o;
    }

    /// <summary>Puts saved holes back and rebuilds the sand mesh around them.</summary>
    public void importDug(List<double[]> list)
    {
        Array.Clear(dug, 0, dug.Length);
        foreach (var e in list)
        {
            int i = (int)e[0];
            if (i >= 0 && i < dug.Length) dug[i] = (float)e[1];
        }
        for (int i = 0; i < dug.Length; i++) pos.setY(i, baseH[i] - dug[i]);
        foreach (var c in chunks) c.dug = false;
        foreach (var e in list)
        {
            int i = (int)e[0];
            int r = i / n, c = i % n;
            noteDug(r, r, c, c);
        }
        refreshColors();
        geo.computeVertexNormals();
        foreach (var ch in chunks) CopyIntoChunk(ch, 0, n - 1, 0, n - 1);
    }

    /// <summary>The z where the sand meets a given water level at x.</summary>
    public double shoreZ(double x, double level)
    {
        double lo = 0, hi = HALF;
        for (int k = 0; k < 10; k++)
        {
            double mid = (lo + hi) / 2;
            if (baseHeight(x, mid) > level) lo = mid;
            else hi = mid;
        }
        return (lo + hi) / 2;
    }
}
