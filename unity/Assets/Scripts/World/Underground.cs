using System;
using System.Collections.Generic;
using static SeaCave;

// Port of src/world/Underground.ts

public class UGSpec
{
    public double y = -60;
}

/// <summary>The room under the dunes sits far below the beach, so nothing on the surface can ever be near it. Local x and z are world x and z.</summary>
public static class UG
{
    public const double y = -60;
}

/// <summary>The chamber below the hatch: a stone room with wall torches, a stair up to an open hatch with the sky above it, and a few old furnishings.</summary>
public class Underground
{
    /// <summary>The room under the dunes sits far below the beach, so nothing on the surface can ever be near it. Local x and z are world x and z.</summary>
    public static readonly UGSpec UG = new UGSpec { y = -60 };

    /// <summary>the room is 16 long (x) and 10 wide (z); the ceiling is 4.5 up</summary>
    private static readonly (double x0, double x1, double z0, double z1, double h) ROOM = (-8, 8, -5, 5, 4.5);
    /// <summary>the stairs run along the north wall, rising toward the east, and climb through a hole in the ceiling</summary>
    private static readonly (double x0, double x1, double z0, double z1, int steps, double rise, double landingX1) STAIRS = (-4, 6.95, -5, -3, 15, 0.29, 7.8);
    private static readonly double STEP_RUN = (STAIRS.x1 - STAIRS.x0) / STAIRS.steps;
    private static readonly double TOP = STAIRS.steps * STAIRS.rise;
    /// <summary>past the top step a wooden ladder, like the one under the hatch outside, climbs the rest of the way to the rim of the stairwell</summary>
    private static readonly double SHAFT_TOP = STAIRS.steps * STAIRS.rise + 3.0;
    private const double TILE = 2.5;
    /// <summary>where the great door stands in the east wall</summary>
    private const double DOOR_Z = 0.5;
    /// <summary>a shaft in the ceiling, open to the sky, that lets the noon sun reach the lock of the great door</summary>
    private static readonly (double x0, double x1, double z0, double z1, double h) SUN_SHAFT = (5, 6.7, -0.15, 1.15, 1.6);
    /// <summary>behind the door a stone stair goes down to the sea cave</summary>
    private static readonly (double x0, double x1, double halfWidth, int steps) PASSAGE = (8.5, 13, 1.05, 15);

    private static Func<double> rng(double seed)
    {
        int s = JS.ToInt32(seed);
        return () =>
        {
            s = unchecked(s + 0x6d2b79f5);
            int t = JS.Imul(s ^ (int)((uint)s >> 15), 1 | s);
            t = unchecked(t + JS.Imul(t ^ (int)((uint)t >> 7), 61 | t)) ^ t;
            return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
        };
    }

    /// <summary>A block-stone texture: rows of dressed blocks with dark mortar, offset row by row.</summary>
    private static THREE.CanvasTexture stoneTexture(double[] bse, int rows, int cols, double seed)
    {
        const int size = 256;
        var cv = new HTMLCanvasElement(size, size);
        var c = cv.getContext("2d");
        var r = rng(seed);
        c.fillStyle = "#17120e";
        c.fillRect(0, 0, size, size);
        double bh = (double)size / rows;
        double bw = (double)size / cols;
        for (int y = 0; y < rows; y++)
        {
            double off = (y % 2) * (bw / 2);
            for (int x = -1; x < cols + 1; x++)
            {
                double k = 0.82 + r() * 0.3;
                c.fillStyle = $"rgb({JS.Str(JS.Round(bse[0] * k))},{JS.Str(JS.Round(bse[1] * k))},{JS.Str(JS.Round(bse[2] * k))})";
                c.fillRect(x * bw + off + 2, y * bh + 2, bw - 4, bh - 4);
                // a little grain on each block
                for (int i = 0; i < 24; i++)
                {
                    double g = r() * 0.5 - 0.25;
                    c.fillStyle = g > 0 ? $"rgba(255,240,220,{JS.Str(g * 0.25)})" : $"rgba(0,0,0,{JS.Str(-g * 0.5)})";
                    c.fillRect(x * bw + off + 4 + r() * (bw - 10), y * bh + 4 + r() * (bh - 10), 2 + r() * 4, 1 + r() * 3);
                }
            }
        }
        var t = new THREE.CanvasTexture(cv);
        t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
        t.colorSpace = THREE.ColorSpace.SRGBColorSpace;
        t.anisotropy = 4;
        return t;
    }

    /// <summary>A box whose texture coordinates are scaled to its size, so blocks stay the same size on every wall.</summary>
    private static THREE.Mesh slab(double w, double h, double d, THREE.Material mat, double x, double y, double z)
    {
        var g = new THREE.BoxGeometry(w, h, d);
        var uv = g.attributes.uv;
        double[][] dims =
        {
            new[] { d, h }, new[] { d, h }, new[] { w, d }, new[] { w, d }, new[] { w, h }, new[] { w, h },
        };
        for (int f = 0; f < 6; f++)
        {
            for (int v = 0; v < 4; v++)
            {
                int i = f * 4 + v;
                uv.setXY(i, uv.getX(i) * dims[f][0] / TILE, uv.getY(i) * dims[f][1] / TILE);
            }
        }
        var m = new THREE.Mesh(g, mat);
        m.position.set(x, y, z);
        m.castShadow = true;
        m.receiveShadow = true;
        return m;
    }

    private class Torch
    {
        public List<THREE.Mesh> flames;
        public THREE.PointLight light;
        public double ph;
        /// <summary>which part it lights: the room behind the door, the cave, or (by the door) both</summary>
        public string place;
    }

    public readonly THREE.Group group = new THREE.Group();
    public readonly List<Solid> colliders = new List<Solid>();
    /// <summary>where you stand after coming down, and which way you face</summary>
    public readonly (double x, double z, double yaw) entry = (-6.5, 0.5, -Math.PI / 2);
    private readonly List<Torch> torches = new List<Torch>();
    public readonly SeaCave cave = new SeaCave();
    private bool doorOpen = false;
    private readonly List<(THREE.Group pivot, int side)> leaves = new List<(THREE.Group, int)>();
    /// <summary>0 shut .. 1 wide open</summary>
    private double swing = 0;
    private readonly THREE.Mesh beam;
    private THREE.MeshStandardMaterial sigil = null;
    private readonly THREE.Group seal = new THREE.Group();

    public Underground()
    {
        group.position.y = UG.y;
        group.visible = false;
        group.matrixWorldAutoUpdate = false;
        var wall = new THREE.MeshStandardMaterial { map = stoneTexture(new double[] { 122, 112, 100 }, 4, 2, 11), roughness = 0.95 };
        var floor = new THREE.MeshStandardMaterial { map = stoneTexture(new double[] { 96, 90, 82 }, 4, 4, 23), roughness = 0.9 };
        var roof = new THREE.MeshStandardMaterial { map = stoneTexture(new double[] { 82, 76, 70 }, 4, 2, 37), roughness = 1 };
        var stairMat = new THREE.MeshStandardMaterial { map = stoneTexture(new double[] { 138, 128, 114 }, 2, 2, 51), roughness = 0.9 };
        var wood = new THREE.MeshStandardMaterial { color = 0x6b4a30, roughness = 0.95 };
        var darkWood = new THREE.MeshStandardMaterial { color = 0x4a3322, roughness = 0.95 };
        var iron = new THREE.MeshStandardMaterial { color = 0x2c2f34, roughness = 0.5, metalness = 0.7 };
        void add(THREE.Object3D o) => group.add(o);

        // floor and the four walls
        add(slab(16.6, 0.4, 10.6, floor, 0, -0.2, 0));
        add(slab(16.6, ROOM.h, 0.5, wall, 0, ROOM.h / 2, ROOM.z0 - 0.25));
        add(slab(16.6, ROOM.h, 0.5, wall, 0, ROOM.h / 2, ROOM.z1 + 0.25));
        add(slab(0.5, ROOM.h, 10.6, wall, ROOM.x0 - 0.25, ROOM.h / 2, 0));
        // the east wall has a way through it, behind the great door
        add(slab(0.5, ROOM.h, DOOR_Z - 1.2 + 5.3, wall, ROOM.x1 + 0.25, ROOM.h / 2, (-5.3 + DOOR_Z - 1.2) / 2));
        add(slab(0.5, ROOM.h, 5.3 - DOOR_Z - 1.2, wall, ROOM.x1 + 0.25, ROOM.h / 2, (5.3 + DOOR_Z + 1.2) / 2));
        add(slab(0.5, ROOM.h - 3.05, 2.4, wall, ROOM.x1 + 0.25, (ROOM.h + 3.05) / 2, DOOR_Z));

        // the ceiling, with a hole over the top of the stairs
        var hole = (x0: 2.6, x1: 7.8, z0: -5.0, z1: -3.0);
        add(slab(hole.x0 + 8.5, 0.5, 11, roof, (-8.5 + hole.x0) / 2, ROOM.h + 0.25, 0));
        // the rest of the ceiling, built around the sun shaft
        var sun = SUN_SHAFT;
        add(slab(sun.x0 - hole.x0, 0.5, 5.5 - hole.z1, roof, (hole.x0 + sun.x0) / 2, ROOM.h + 0.25, (hole.z1 + 5.5) / 2));
        add(slab(8.5 - sun.x1, 0.5, 5.5 - hole.z1, roof, (sun.x1 + 8.5) / 2, ROOM.h + 0.25, (hole.z1 + 5.5) / 2));
        add(slab(sun.x1 - sun.x0, 0.5, sun.z0 - hole.z1, roof, (sun.x0 + sun.x1) / 2, ROOM.h + 0.25, (hole.z1 + sun.z0) / 2));
        add(slab(sun.x1 - sun.x0, 0.5, 5.5 - sun.z1, roof, (sun.x0 + sun.x1) / 2, ROOM.h + 0.25, (sun.z1 + 5.5) / 2));
        // a small square shaft climbs from that opening to the open sky, cut so that the noon sun falls on the great door's lock
        double up = ROOM.h + 0.5 + sun.h / 2;
        const double th = 0.35;
        add(slab(th, sun.h, sun.z1 - sun.z0 + th * 2, wall, sun.x0 - th / 2, up, (sun.z0 + sun.z1) / 2));
        add(slab(th, sun.h, sun.z1 - sun.z0 + th * 2, wall, sun.x1 + th / 2, up, (sun.z0 + sun.z1) / 2));
        add(slab(sun.x1 - sun.x0, sun.h, th, wall, (sun.x0 + sun.x1) / 2, up, sun.z0 - th / 2));
        add(slab(sun.x1 - sun.x0, sun.h, th, wall, (sun.x0 + sun.x1) / 2, up, sun.z1 + th / 2));
        // iron bars across the top: the grate that can be seen on the beach above
        double barY = ROOM.h + 0.5 + sun.h - 0.06;
        for (int i = 1; i < 5; i++)
        {
            double x = sun.x0 + (sun.x1 - sun.x0) * i / 5;
            var bar = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.08, sun.z1 - sun.z0 + 0.1), iron);
            bar.position.set(x, barY, (sun.z0 + sun.z1) / 2);
            add(bar);
        }
        for (int i = 1; i < 4; i++)
        {
            double z = sun.z0 + (sun.z1 - sun.z0) * i / 4;
            var bar = new THREE.Mesh(new THREE.BoxGeometry(sun.x1 - sun.x0 + 0.1, 0.08, 0.06), iron);
            bar.position.set((sun.x0 + sun.x1) / 2, barY, z);
            add(bar);
        }

        // the stairwell: walls on up to a rim, open to the sky
        // Each wall starts exactly where the thing below it ends and never overlaps another box, so no two faces lie in the same plane
        // (which made the stairwell flicker). The north and east walls start at the top of the room's own walls; the west and south
        // ones sit on top of the ceiling slabs.
        (double h, double y) wallTo(double y0) => (SHAFT_TOP - y0, (SHAFT_TOP + y0) / 2);
        var fromWall = wallTo(ROOM.h);
        var fromRoof = wallTo(ROOM.h + 0.5);
        // north: from the stairwell's west edge to the room's east wall
        add(slab(8.5 - hole.x0, fromWall.h, 0.5, wall, (hole.x0 + 8.5) / 2, fromWall.y, hole.z0 - 0.25));
        // east: between the north wall and the ceiling slab
        add(slab(8.5 - hole.x1, fromWall.h, hole.z1 - hole.z0, wall, (hole.x1 + 8.5) / 2, fromWall.y, (hole.z0 + hole.z1) / 2));
        // west and south: standing on the ceiling slabs
        add(slab(0.5, fromRoof.h, hole.z1 - hole.z0, wall, hole.x0 - 0.25, fromRoof.y, (hole.z0 + hole.z1) / 2));
        add(slab(hole.x1 - hole.x0, fromRoof.h, 0.5, wall, (hole.x0 + hole.x1) / 2, fromRoof.y, hole.z1 + 0.25));
        // a timber frame around the opening, and the door standing open beside it
        foreach (var (w, d, x, z) in new[]
        {
            (hole.x1 - hole.x0 + 1.2, 0.3, (hole.x0 + hole.x1) / 2, hole.z0 - 0.1),
            (hole.x1 - hole.x0 + 1.2, 0.3, (hole.x0 + hole.x1) / 2, hole.z1 + 0.1),
        })
        {
            var f = new THREE.Mesh(new THREE.BoxGeometry(w, 0.18, d), darkWood);
            f.position.set(x, SHAFT_TOP + 0.09, z);
            f.castShadow = true;
            add(f);
        }
        // the hatch door: hinged on the east rim and standing open, leaning back away from the opening
        var door = new THREE.Group();
        const int planks = 6;
        double span = hole.z1 - hole.z0;
        for (int i = 0; i < planks; i++)
        {
            var p = new THREE.Mesh(new THREE.BoxGeometry(2.4, 0.08, span / planks - 0.02), i % 2 != 0 ? wood : darkWood);
            p.position.set(-1.2, 0, -span / 2 + span / planks * (i + 0.5));
            p.castShadow = true;
            door.add(p);
        }
        foreach (var x in new[] { -0.5, -1.9 })
        {
            var strap = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.04, span + 0.04), iron);
            strap.position.set(x, 0.06, 0);
            door.add(strap);
        }
        var handle = new THREE.Mesh(new THREE.TorusGeometry(0.14, 0.03, 6, 16), iron);
        handle.position.set(-1.2, 0.1, 0);
        handle.rotation.x = Math.PI / 2;
        door.add(handle);
        door.position.set(hole.x1 + 0.2, SHAFT_TOP + 0.18, (hole.z0 + hole.z1) / 2);
        door.rotation.z = -1.92;
        add(door);

        // the wooden ladder up the east wall of the stairwell, the same as the one that goes down from the hatch outside
        var rail = new THREE.MeshStandardMaterial { color = 0x6b4a30, roughness = 0.95 };
        double ladderX = hole.x1 - 0.08;
        var rails = new[] { (hole.z0 + hole.z1) / 2 - 0.45, (hole.z0 + hole.z1) / 2 + 0.45 };
        foreach (var z in rails)
        {
            var r = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.035, 3.5, 8), rail);
            r.position.set(ladderX, TOP - 0.1 + 1.75, z);
            r.castShadow = true;
            add(r);
        }
        for (int i = 0; i < 12; i++)
        {
            var rung = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.025, 0.9, 6), rail);
            rung.rotation.x = Math.PI / 2;
            rung.position.set(ladderX, TOP + 0.2 + i * 0.28, (hole.z0 + hole.z1) / 2);
            rung.castShadow = true;
            add(rung);
        }

        // the stairs: solid blocks, so they read as one stone flight
        for (int i = 0; i < STAIRS.steps; i++)
        {
            double top = (i + 1) * STAIRS.rise;
            add(slab(STEP_RUN, top, STAIRS.z1 - STAIRS.z0, stairMat, STAIRS.x0 + STEP_RUN * (i + 0.5), top / 2, (STAIRS.z0 + STAIRS.z1) / 2));
        }
        add(slab(STAIRS.landingX1 - STAIRS.x1, TOP, STAIRS.z1 - STAIRS.z0, stairMat, (STAIRS.x1 + STAIRS.landingX1) / 2, TOP / 2, (STAIRS.z0 + STAIRS.z1) / 2));

        buildTorches();
        buildFurniture(wood, darkWood, iron);
        buildDoor(darkWood, iron);
        buildPassage(wall, floor, stairMat, roof);
        setSide(false);
        group.add(cave.group);
        colliders.AddRange(cave.colliders);
        // the beam of noon sun: it comes down the shaft in the ceiling and narrows to the lock
        var from = new THREE.Vector3(5.3, ROOM.h + 0.5 + SUN_SHAFT.h, DOOR_Z);
        var to = new THREE.Vector3(ROOM.x1 - 0.08, 1.35, DOOR_Z);
        var dir = to.clone().sub(from);
        // it fades in from nothing at the top, so it has no hard end up in the stairwell
        var fade = new HTMLCanvasElement(4, 64);
        var c = fade.getContext("2d");
        var grad = c.createLinearGradient(0, 0, 0, 64);
        grad.addColorStop(0, "#000");
        grad.addColorStop(0.14, "#fff");
        grad.addColorStop(1, "#fff");
        c.fillStyle = grad;
        c.fillRect(0, 0, 4, 64);
        beam = new THREE.Mesh(
            new THREE.CylinderGeometry(0.42, 0.1, dir.length(), 18, 1, true),
            new THREE.MeshBasicMaterial { color = 0xffe9b0, alphaMap = new THREE.CanvasTexture(fade), transparent = true, opacity = 0.32, blending = THREE.Blending.AdditiveBlending, depthWrite = false, side = THREE.Side.DoubleSide, fog = false }
        );
        beam.position.copy(from).add(to).multiplyScalar(0.5);
        beam.quaternion.setFromUnitVectors(new THREE.Vector3(0, -1, 0), dir.normalize());
        beam.visible = false;
        add(beam);
    }

    /// <summary>The stone stair down from the door to the cave.</summary>
    private void buildPassage(THREE.Material wall, THREE.Material floor, THREE.Material stairMat, THREE.Material roof)
    {
        double run = (PASSAGE.x1 - PASSAGE.x0) / PASSAGE.steps;
        double rise = -CAVE_Y / PASSAGE.steps;
        void add(THREE.Object3D o) => group.add(o);
        double wide = PASSAGE.halfWidth * 2 + 0.5;
        add(slab(PASSAGE.x0 - ROOM.x1, 0.4, wide, floor, (PASSAGE.x0 + ROOM.x1) / 2, -0.2, DOOR_Z));
        for (int i = 0; i < PASSAGE.steps; i++)
        {
            add(slab(run, 0.5, wide, stairMat, PASSAGE.x0 + run * (i + 0.5), -(i + 1) * rise - 0.25, DOOR_Z));
        }
        double len = PASSAGE.x1 - ROOM.x1 + 0.6;
        double cx = (PASSAGE.x1 + ROOM.x1 + 0.6) / 2;
        double h = 3.05 - CAVE_Y;
        foreach (var s in new[] { -1, 1 }) add(slab(len, h, 0.4, wall, cx, CAVE_Y + h / 2, DOOR_Z + s * (PASSAGE.halfWidth + 0.35)));
        // the roof slopes down with the stair, so at the bottom it runs in under the rock of the cave wall
        const double fall = 0.45;
        double tilt = Math.Atan(fall);
        double x0 = ROOM.x1;
        double x1 = PASSAGE.x1 + 0.9;
        double under(double x) => 3.05 - (x - PASSAGE.x0) * fall;
        var lid = slab((x1 - x0) / Math.Cos(tilt), 0.4, wide + 0.8, roof, (x0 + x1) / 2, under((x0 + x1) / 2) + 0.2 / Math.Cos(tilt), DOOR_Z);
        lid.rotation.z = -tilt;
        add(lid);
    }

    /// <summary>Lights cost time, so each side of the stair only keeps its own: the room's while in the room, the cave's while in the cave.</summary>
    public void setSide(bool inCave)
    {
        foreach (var t in torches) t.light.visible = t.place == "door" || (t.place == "cave") == inCave;
        foreach (var l in cave.lights) l.visible = inCave;
    }

    /// <summary>True when the sun is reaching the keyhole.</summary>
    public void setBeam(bool on)
    {
        beam.visible = on && !doorOpen;
    }

    /// <summary>Opens the great door: swinging, or already standing open when a saved game is loaded.</summary>
    public void openDoor(bool instant = false)
    {
        doorOpen = true;
        beam.visible = false;
        if (sigil != null) sigil.emissiveIntensity = 2.2;
        seal.visible = true;
        if (instant)
        {
            swing = 1;
            poseDoor();
        }
    }

    public bool isDoorOpen => doorOpen;

    private void poseDoor()
    {
        double k = swing * swing * (3 - 2 * swing);
        foreach (var l in leaves) l.pivot.rotation.y = -l.side * 1.8 * k;
    }

    /// <summary>True when close enough to reach the gold.</summary>
    public bool nearGold(double x, double z)
    {
        return doorOpen && x > PASSAGE.x1 && cave.nearGold(x, z);
    }

    /// <summary>The height of the floor at a point: flat, or the steps of the stair.</summary>
    public double floorAt(double x, double z)
    {
        if (doorOpen && x > PASSAGE.x0)
        {
            if (x >= PASSAGE.x1) return Math.Min(CAVE_Y + 0.4, cave.floorAt(x, z));
            double step = Math.Floor((x - PASSAGE.x0) / (PASSAGE.x1 - PASSAGE.x0) * PASSAGE.steps) + 1;
            return CAVE_Y * step / PASSAGE.steps;
        }
        if (z > STAIRS.z0 - 0.2 && z < STAIRS.z1 && x > STAIRS.x0)
        {
            if (x >= STAIRS.x1) return TOP;
            return (Math.Floor((x - STAIRS.x0) / STEP_RUN) + 1) * STAIRS.rise;
        }
        return 0;
    }

    /// <summary>Keeps a walker inside the walls.</summary>
    public (double x, double z) clamp(double x, double z)
    {
        if (doorOpen)
        {
            if (x > PASSAGE.x1 + 0.6) return cave.clamp(x, z);
            if (x > ROOM.x1 - 1 && Math.Abs(z - DOOR_Z) < PASSAGE.halfWidth) return (x, z);
            if (x > ROOM.x1) return (x, Math.Min(DOOR_Z + PASSAGE.halfWidth - 0.05, Math.Max(DOOR_Z - PASSAGE.halfWidth + 0.05, z)));
        }
        return (Math.Min(STAIRS.landingX1 - 0.45, Math.Max(ROOM.x0 + 0.4, x)), Math.Min(ROOM.z1 - 0.4, Math.Max(ROOM.z0 + 0.3, z)));
    }

    /// <summary>how far the ladder lifts you above the top step; at the top of it the hatch can be reached</summary>
    public readonly double ladderH = 2.0;

    /// <summary>True where the ladder hangs: at the east end of the landing, against the wall.</summary>
    public bool onLadder(double x, double z)
    {
        // only on the landing at the top of the stair: the cave beyond the great door lies on the same line, and must not count
        return x > STAIRS.x1 + 0.2 && x < STAIRS.landingX1 && z > STAIRS.z0 + 0.2 && z < STAIRS.z1 - 0.2;
    }

    public void setActive(bool on)
    {
        group.visible = on;
        group.matrixWorldAutoUpdate = on;
    }

    public void update(double t, double dt = 0)
    {
        foreach (var tc in torches)
        {
            double k = 1 + Math.Sin(t * 11 + tc.ph) * 0.12 + Math.Sin(t * 6.3 + tc.ph * 2) * 0.1 + (JS.Random() - 0.5) * 0.06;
            tc.light.intensity = 11 * k;
            for (int i = 0; i < tc.flames.Count; i++)
            {
                var f = tc.flames[i];
                f.scale.set(1 / Math.Sqrt(k), k, 1 / Math.Sqrt(k));
                f.rotation.y = t * (2 + i) + tc.ph;
            }
        }
        cave.update(t, dt);
        if (doorOpen && swing < 1)
        {
            swing = Math.Min(1, swing + 0.008);
            poseDoor();
        }
    }

    // ---------------------------------------------------------------- torches

    private void torch(double x, double y, double z, double faceX, double faceZ, string place = "room")
    {
        var iron = new THREE.MeshStandardMaterial { color = 0x2c2f34, roughness = 0.5, metalness = 0.7 };
        var wood = new THREE.MeshStandardMaterial { color = 0x5a3b24, roughness = 0.95 };
        var charMat = new THREE.MeshStandardMaterial { color = 0x1a120c, roughness = 1 };
        var g = new THREE.Group();
        // built facing +z (out of the wall), then turned to face into the room
        var plate = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.4, 0.05), iron);
        plate.position.z = 0.03;
        var arm = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.05, 0.4), iron);
        arm.position.set(0, -0.1, 0.22);
        var stick = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.05, 0.75, 8), wood);
        stick.position.set(0, 0.15, 0.36);
        stick.rotation.x = -0.3;
        var head = new THREE.Mesh(new THREE.CylinderGeometry(0.075, 0.05, 0.16, 8), charMat);
        head.position.set(0, 0.55, 0.28);
        g.add(plate, arm, stick, head);
        var flames = new List<THREE.Mesh>();
        var outer = new THREE.Mesh(new THREE.ConeGeometry(0.11, 0.45, 8), new THREE.MeshBasicMaterial { color = 0xff8a22, transparent = true, opacity = 0.92 });
        outer.position.set(0, 0.84, 0.28);
        var inner = new THREE.Mesh(new THREE.ConeGeometry(0.06, 0.28, 6), new THREE.MeshBasicMaterial { color = 0xffe27a });
        inner.position.set(0, 0.78, 0.28);
        g.add(outer, inner);
        flames.Add(outer);
        flames.Add(inner);
        var light = new THREE.PointLight(0xff9a3a, 11, 13, 2);
        light.position.set(0, 0.85, 0.5);
        g.add(light);
        g.position.set(x, y, z);
        g.rotation.y = Math.Atan2(faceX, faceZ);
        group.add(g);
        torches.Add(new Torch { flames = flames, light = light, ph = JS.Random() * 6, place = place });
    }

    private void buildTorches()
    {
        // along the south wall, the west wall, and the north wall above the lower steps
        foreach (var x in new[] { -5, 0, 5 }) torch(x, 2.2, ROOM.z1 - 0.02, 0, -1);
        torch(ROOM.x0 + 0.02, 2.2, 0.5, 1, 0);
        torch(-6.5, 2.2, ROOM.z0 + 0.02, 0, 1);
        torch(-1, 3.4, ROOM.z0 + 0.02, 0, 1);
        // either side of the great door
        torch(ROOM.x1 - 0.02, 2.4, DOOR_Z - 2.1, -1, 0, "door");
        torch(ROOM.x1 - 0.02, 2.4, DOOR_Z + 2.1, -1, 0, "door");
        // on the cave wall either side of the stair, so the cave is never dark, whatever the hour
        torch(PASSAGE.x1 + 0.45, CAVE_Y + 2.1, DOOR_Z - 2.3, 1, 0, "cave");
        torch(PASSAGE.x1 + 0.45, CAVE_Y + 2.1, DOOR_Z + 2.3, 1, 0, "cave");
        // up the stairwell, so the climb is lit
        torch(4.2, 5.9, ROOM.z0 + 0.02, 0, 1);
        torch(6.6, 6.4, ROOM.z0 + 0.02, 0, 1);
    }

    // ---------------------------------------------------------------- the door

    /// <summary>A great iron-bound double door set in a stone frame, with a faintly glowing sigil over it.</summary>
    private void buildDoor(THREE.Material darkWood, THREE.Material iron)
    {
        var stone = new THREE.MeshStandardMaterial { map = stoneTexture(new double[] { 150, 140, 126 }, 2, 2, 91), roughness = 0.85 };
        var g = new THREE.Group();
        // built facing +z (out of the wall), then turned to face west, into the room
        const double W = 2.4;
        const double H = 3.0;
        THREE.Mesh add(THREE.Mesh m)
        {
            m.castShadow = true;
            m.receiveShadow = true;
            g.add(m);
            return m;
        }
        // the stone frame: two jambs, a heavy lintel and a step
        foreach (var sx in new[] { -1, 1 }) add(new THREE.Mesh(new THREE.BoxGeometry(0.4, H + 0.3, 0.45), stone)).position.set(sx * (W / 2 + 0.2), (H + 0.3) / 2, 0.12);
        add(new THREE.Mesh(new THREE.BoxGeometry(W + 1.2, 0.45, 0.5), stone)).position.set(0, H + 0.3, 0.14);
        add(new THREE.Mesh(new THREE.BoxGeometry(W + 1.0, 0.12, 0.7), stone)).position.set(0, 0.06, 0.25);
        // the two leaves
        var leaf = new THREE.MeshStandardMaterial { color = 0x3a2a1e, roughness = 0.9 };
        foreach (var sx in new[] { -1, 1 })
        {
            // each leaf turns on a hinge at the outer edge of the frame, swinging away from the room
            var pivot = new THREE.Group();
            pivot.position.x = sx * W / 2;
            g.add(pivot);
            leaves.Add((pivot, sx));
            THREE.Mesh part(THREE.Mesh m, double x, double y, double z)
            {
                m.castShadow = true;
                m.receiveShadow = true;
                m.position.set(x - sx * W / 2, y, z);
                pivot.add(m);
                return m;
            }
            part(new THREE.Mesh(new THREE.BoxGeometry(W / 2 - 0.03, H - 0.1, 0.16), leaf), sx * (W / 4), H / 2 + 0.05, 0);
            foreach (var y in new[] { 0.55, 1.5, 2.45 })
            {
                part(new THREE.Mesh(new THREE.BoxGeometry(W / 2 - 0.06, 0.16, 0.04), iron), sx * (W / 4), y, 0.1);
                for (int i = 0; i < 4; i++) part(new THREE.Mesh(new THREE.SphereGeometry(0.035, 6, 5), iron), sx * (W / 4) + (i - 1.5) * 0.26, y, 0.13);
            }
            part(new THREE.Mesh(new THREE.TorusGeometry(0.15, 0.025, 8, 18), iron), sx * 0.2, 1.3, 0.11);
            // half a lock plate on each leaf: it parts down the middle as the door opens
            part(new THREE.Mesh(new THREE.BoxGeometry(0.13, 0.4, 0.05), iron), sx * 0.066, 1.35, 0.12);
            if (sx < 0) part(new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.1, 0.02, 20), new THREE.MeshBasicMaterial { color = 0x050403 }), -0.066, 1.35, 0.15).rotation.x = Math.PI / 2;
        }
        // the medallion, sitting in the lock once the door has taken it
        var coin = new THREE.Mesh(new THREE.CylinderGeometry(0.095, 0.095, 0.03, 24), new THREE.MeshStandardMaterial { color = 0xd8ac4a, metalness = 0.75, roughness = 0.3 });
        coin.rotation.x = Math.PI / 2;
        seal.add(coin);
        seal.position.set(W / 2 - 0.066, 1.35, 0.17);
        seal.visible = false;
        leaves[0].pivot.add(seal);
        // a sigil above the door: a ring and a ring of small lights, glowing faintly teal
        var glow = new THREE.MeshStandardMaterial { color = 0x1f6f7a, emissive = 0x3fd0e0, emissiveIntensity = 0.9, roughness = 0.4 };
        sigil = glow;
        var ring = new THREE.Mesh(new THREE.TorusGeometry(0.5, 0.025, 8, 36), glow);
        ring.position.set(0, H + 0.85, 0.2);
        g.add(ring);
        for (int i = 0; i < 8; i++)
        {
            double a = i / 8.0 * Math.PI * 2;
            var dot = new THREE.Mesh(new THREE.SphereGeometry(0.04, 6, 5), glow);
            dot.position.set(Math.Cos(a) * 0.5, H + 0.85 + Math.Sin(a) * 0.5, 0.2);
            g.add(dot);
        }
        var inner = new THREE.Mesh(new THREE.TorusGeometry(0.22, 0.02, 8, 24), glow);
        inner.position.set(0, H + 0.85, 0.2);
        g.add(inner);
        g.position.set(ROOM.x1 - 0.12, 0, DOOR_Z);
        g.rotation.y = -Math.PI / 2;
        group.add(g);
        _ = darkWood;
    }

    // ---------------------------------------------------------------- furnishings

    private void buildFurniture(THREE.Material wood, THREE.Material darkWood, THREE.Material iron)
    {
        var stone = new THREE.MeshStandardMaterial { map = stoneTexture(new double[] { 150, 140, 126 }, 2, 2, 71), roughness = 0.85 };
        void post(double x, double z, double r) => colliders.Add(new Solid { x = x, z = z, r = r });
        // stone pillars
        foreach (var (x, z) in new[] { (-3.0, 2.6), (1.5, 2.6), (6.0, 2.6), (-3.0, -1.4) })
        {
            var bse = new THREE.Mesh(new THREE.BoxGeometry(1.1, 0.35, 1.1), stone);
            bse.position.set(x, 0.175, z);
            var shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.42, 0.46, ROOM.h - 0.7, 14), stone);
            shaft.position.set(x, ROOM.h / 2, z);
            var cap = new THREE.Mesh(new THREE.BoxGeometry(1.1, 0.35, 1.1), stone);
            cap.position.set(x, ROOM.h - 0.175, z);
            foreach (var m in new[] { bse, shaft, cap })
            {
                m.castShadow = true;
                m.receiveShadow = true;
                group.add(m);
            }
            post(x, z, 0.65);
        }
        // barrels and crates in the south-west corner
        var band = iron;
        foreach (var (x, z) in new[] { (-7.0, 4.0), (-6.1, 4.3), (-7.1, 3.1) })
        {
            var b = new THREE.Mesh(new THREE.CylinderGeometry(0.4, 0.34, 0.9, 14), wood);
            b.position.set(x, 0.45, z);
            b.castShadow = true;
            b.receiveShadow = true;
            group.add(b);
            foreach (var y in new[] { 0.2, 0.7 })
            {
                var r = new THREE.Mesh(new THREE.TorusGeometry(0.38 - (y < 0.4 ? 0.02 : 0), 0.02, 5, 16), band);
                r.rotation.x = Math.PI / 2;
                r.position.set(x, y, z);
                group.add(r);
            }
            post(x, z, 0.5);
        }
        foreach (var (x, z, s, r) in new[] { (-5.2, 4.1, 0.8, 0.2), (-5.4, 3.3, 0.6, -0.4) })
        {
            var c = new THREE.Mesh(new THREE.BoxGeometry(s, s, s), darkWood);
            c.position.set(x, s / 2, z);
            c.rotation.y = r;
            c.castShadow = true;
            c.receiveShadow = true;
            group.add(c);
            post(x, z, s * 0.7);
        }
        // a long table and two benches near the west wall
        var table = new THREE.Mesh(new THREE.BoxGeometry(2.4, 0.12, 1.0), wood);
        table.position.set(-6.3, 0.95, -1.5);
        table.rotation.y = Math.PI / 2;
        table.castShadow = true;
        group.add(table);
        foreach (var (dx, dz) in new[] { (-0.45, -1.0), (0.45, -1.0), (-0.45, 1.0), (0.45, 1.0) })
        {
            var leg = new THREE.Mesh(new THREE.BoxGeometry(0.12, 0.95, 0.12), darkWood);
            leg.position.set(-6.3 + dx, 0.475, -1.5 + dz);
            group.add(leg);
        }
        foreach (var dx in new[] { -0.95, 0.95 })
        {
            var bench = new THREE.Mesh(new THREE.BoxGeometry(0.4, 0.1, 2.2), wood);
            bench.position.set(-6.3 + dx, 0.5, -1.5);
            bench.castShadow = true;
            group.add(bench);
        }
        post(-6.3, -1.5, 1.5);
        // a lantern and an old map-case on the table
        var lantern = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.12, 0.28, 8), new THREE.MeshStandardMaterial { color = 0xffd98a, emissive = 0xffa030, emissiveIntensity = 0.9 });
        lantern.position.set(-6.3, 1.2, -1.2);
        group.add(lantern);
        // the captain's journal: open on a lectern in the middle of the ring
        var lectern = new THREE.Group();
        var pillar = new THREE.Mesh(new THREE.CylinderGeometry(0.2, 0.28, 0.85, 12), stone);
        pillar.position.y = 0.3 + 0.425;
        var top = new THREE.Group();
        top.position.set(0, 1.18, 0);
        top.rotation.z = 0.3;
        var desk = new THREE.Mesh(new THREE.BoxGeometry(0.8, 0.06, 0.6), wood);
        var cover = new THREE.Mesh(new THREE.BoxGeometry(0.66, 0.035, 0.46), new THREE.MeshStandardMaterial { color = 0x5a2a20, roughness = 0.7 });
        cover.position.y = 0.05;
        var pageMat = new THREE.MeshStandardMaterial { color = 0xf1e6c6, roughness = 0.9, emissive = 0xd9a24a, emissiveIntensity = 0.22 };
        foreach (var sx in new[] { -1, 1 })
        {
            var pg = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.03, 0.4), pageMat);
            pg.position.set(sx * 0.155, 0.085, 0);
            pg.rotation.z = sx * -0.05;
            top.add(pg);
        }
        top.add(desk, cover);
        lectern.add(pillar, top);
        lectern.position.set(0, 0, -0.3);
        foreach (var m in new[] { pillar, desk, cover })
        {
            m.castShadow = true;
            m.receiveShadow = true;
        }
        group.add(lectern);
        post(0, -0.3, 0.45);

        // a round stone dais in the middle, with a ring cut into it
        var dais = new THREE.Mesh(new THREE.CylinderGeometry(1.6, 1.8, 0.3, 28), stone);
        dais.position.set(0, 0.15, -0.3);
        dais.receiveShadow = true;
        dais.castShadow = true;
        group.add(dais);
        var ring = new THREE.Mesh(new THREE.TorusGeometry(1.2, 0.04, 6, 40), new THREE.MeshStandardMaterial { color = 0x8a6a3a, metalness = 0.6, roughness = 0.5 });
        ring.rotation.x = Math.PI / 2;
        ring.position.set(0, 0.31, -0.3);
        group.add(ring);
    }

    /// <summary>True when close enough to the door in the east wall.</summary>
    public bool nearDoor(double x, double z)
    {
        return !doorOpen && JS.Hypot(x - 7.7, z - DOOR_Z) < 2.4;
    }

    /// <summary>True when close enough to the lectern to read the journal.</summary>
    public bool nearBook(double x, double z)
    {
        return JS.Hypot(x - 0, z - -0.3) < 2.4;
    }
}
