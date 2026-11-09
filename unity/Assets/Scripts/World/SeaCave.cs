using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/world/SeaCave.ts

/// <summary>The cave behind the great door: open to the sea at the far end, with the bones of something very large lying around a heap of gold.</summary>
public class SeaCave
{
    /// <summary>The floor of the cave, below the floor of the stone room.</summary>
    public const double CAVE_Y = -2.4;
    /// <summary>where the walkable sand is: an ellipse around this point</summary>
    private static readonly (double x, double z, double rx, double rz) CAVE = (23.5, 0.5, 11, 9);
    /// <summary>where the rock wall stands, at the foot of the stair</summary>
    private const double PORTAL_X = 13.25;
    /// <summary>the edge of the sea</summary>
    private const double WATER_X = 30.5;
    private const double WATER_Y = CAVE_Y - 0.5;
    /// <summary>the heap of gold</summary>
    private static readonly (double x, double z, double r) HOARD = (25, 1.5, 3.6);
    /// <summary>How much the gold is worth. It buys everything in the camper, luxuries included, whenever the player gets here.</summary>
    public const int HOARD_VALUE = 30000;

    private const int BONE = 0xe6dcc4;

    public class Solid { public double x, z, r; }

    private static Func<double> rng(double seed)
    {
        double s = seed;
        return () =>
        {
            s = (s * 1664525 + 1013904223) % 4294967296;
            return s / 4294967296;
        };
    }

    /// <summary>Rough rock: layered bands of stone, with cracks running through them. Light is high ground, so it serves as its own bump map.</summary>
    private static THREE.CanvasTexture rockTexture()
    {
        const int size = 512;
        var cv = new HTMLCanvasElement(size, size);
        var c = cv.getContext("2d");
        var rand = rng(19);
        c.fillStyle = "#7d7266";
        c.fillRect(0, 0, size, size);
        // the bands of the rock, wandering a little
        for (double y = 0; y < size; y += 6 + rand() * 26)
        {
            double v = 86 + rand() * 66;
            c.strokeStyle = $"rgba({JS.Str(v + 12)},{JS.Str(v + 2)},{JS.Str(v - 12)},0.55)";
            c.lineWidth = 3 + rand() * 22;
            c.beginPath();
            double ph = rand() * 6.28;
            for (int x = 0; x <= size; x += 12)
            {
                // whole waves only, so the texture tiles
                double py = y + Math.Sin((double)x / size * Math.PI * 2 * 2 + ph) * 10;
                if (x == 0) c.moveTo(x, py);
                else c.lineTo(x, py);
            }
            c.stroke();
        }
        // blotches and grit
        for (int i = 0; i < 900; i++)
        {
            double v = rand() < 0.5 ? 50 : 170;
            c.fillStyle = $"rgba({JS.Str(v)},{JS.Str(v - 6)},{JS.Str(v - 14)},{JS.Str(0.08 + rand() * 0.16)})";
            double r = 1 + rand() * rand() * 22;
            c.beginPath();
            // the arguments are evaluated left to right, as in the browser
            double ex = rand() * size, ey = rand() * size, erx = r * (1 + rand()), erot = rand() * 3;
            c.ellipse(ex, ey, erx, r, erot, 0, Math.PI * 2);
            c.fill();
        }
        // cracks
        c.strokeStyle = "rgba(22,17,13,0.8)";
        for (int i = 0; i < 46; i++)
        {
            double x = rand() * size;
            double y = rand() * size;
            double a = rand() * 6.28;
            c.lineWidth = 0.8 + rand() * 2.4;
            c.beginPath();
            c.moveTo(x, y);
            for (int k = 0; k < 7; k++)
            {
                a += (rand() - 0.5) * 1.3;
                x += Math.Cos(a) * (14 + rand() * 24);
                y += Math.Sin(a) * (14 + rand() * 24);
                c.lineTo(x, y);
            }
            c.stroke();
        }
        var t = new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
        t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
        t.repeat.set(9, 4);
        return t;
    }

    /// <summary>Coins lying thick on each other, for the body of the heap under the loose ones.</summary>
    private static THREE.CanvasTexture coinTexture()
    {
        const int size = 512;
        var cv = new HTMLCanvasElement(size, size);
        var c = cv.getContext("2d");
        var rand = rng(53);
        c.fillStyle = "#6e4a10";
        c.fillRect(0, 0, size, size);
        var offs = new[] { (0, 0), (size, 0), (-size, 0), (0, size), (0, -size) };
        for (int i = 0; i < 1500; i++)
        {
            double x = rand() * size;
            double y = rand() * size;
            double r = 13 + rand() * 7;
            double tilt = 0.45 + rand() * 0.55;
            double rot = rand() * 3.14;
            double g = 150 + rand() * 80;
            foreach (var (ox, oy) in offs)
            {
                c.beginPath();
                c.ellipse(x + ox, y + oy, r, r * tilt, rot, 0, Math.PI * 2);
                c.fillStyle = $"rgb({JS.Str(g + 18)},{JS.Str(g * 0.74)},{JS.Str(g * 0.24)})";
                c.fill();
                c.strokeStyle = "rgba(70,42,6,0.75)";
                c.lineWidth = 1.6;
                c.stroke();
            }
        }
        var t = new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
        t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
        return t;
    }

    /// <summary>Distance to a capsule that tapers from `ra` to `rb`.</summary>
    private static double capsule(double px, double py, double pz, double ax, double ay, double az, double bx, double by, double bz, double ra, double rb)
    {
        double dx = bx - ax, dy = by - ay, dz = bz - az;
        double qx = px - ax, qy = py - ay, qz = pz - az;
        double t = Math.Min(1, Math.Max(0, (qx * dx + qy * dy + qz * dz) / (dx * dx + dy * dy + dz * dz)));
        double rx = qx - dx * t, ry = qy - dy * t, rz = qz - dz * t;
        return Math.Sqrt(rx * rx + ry * ry + rz * rz) - (ra + (rb - ra) * t);
    }

    private class SkullGeo { public THREE.BufferGeometry head, jaw, upper, lower; }

    private static SkullGeo skullGeo;

    private static double sq(double v) => v * v;

    /// <summary>The sculpted parts of the skull, made once and shared by the cave and the trophy at camp.</summary>
    private static SkullGeo skullParts()
    {
        if (skullGeo != null) return skullGeo;
        THREE.Vector3 V(double x, double y, double z) => new THREE.Vector3(x, y, z);
        double head(double x, double yy, double z)
        {
            // the skull is the same on both sides, and it faces -z
            double px = Math.Abs(x);
            double d = capsule(px, yy, z, 0, 0.55, 0.9, 0, 0.6, 0.4, 0.62, 0.55);
            d = Hand.blendShapes(d, capsule(px, yy, z, 0, 0.35, 0.2, 0, 0.2, -1.5, 0.42, 0.2), 0.25);
            d = Hand.blendShapes(d, capsule(px, yy, z, 0.36, 0.78, 0.4, 0.3, 0.66, -0.25, 0.2, 0.13), 0.14);
            d = Hand.blendShapes(d, capsule(px, yy, z, 0.52, 0.2, 0.6, 0.24, 0.12, -0.7, 0.17, 0.1), 0.16);
            // the horns sweep back from the brow
            d = Hand.blendShapes(d, capsule(px, yy, z, 0.42, 0.9, 0.55, 0.72, 1.2, 1.25, 0.13, 0.08), 0.1);
            d = Hand.blendShapes(d, capsule(px, yy, z, 0.72, 1.2, 1.25, 0.72, 1.12, 1.95, 0.08, 0.015), 0.04);
            // the sockets, the nostrils and the hollow behind the eye
            d = -Hand.blendShapes(-d, Math.Sqrt(sq(px - 0.46) + sq(yy - 0.6) + sq(z - 0.02)) - 0.27, 0.05);
            d = -Hand.blendShapes(-d, Math.Sqrt(sq(px - 0.13) + sq(yy - 0.42) + sq(z + 1.4)) - 0.08, 0.05);
            d = -Hand.blendShapes(-d, Math.Sqrt(sq(px - 0.66) + sq(yy - 0.4) + sq(z - 0.92)) - 0.22, 0.05);
            return d;
        }
        double jaw(double x, double yy, double z)
        {
            double px = Math.Abs(x);
            double d = capsule(px, yy, z, 0.52, -0.1, 0.95, 0.14, -0.2, -1.32, 0.13, 0.09);
            d = Hand.blendShapes(d, capsule(px, yy, z, 0.52, -0.1, 0.95, 0.58, 0.22, 1.05, 0.12, 0.09), 0.1);
            d = Hand.blendShapes(d, capsule(px, yy, z, 0, -0.2, -1.35, 0.14, -0.2, -1.32, 0.09, 0.09), 0.1);
            return d;
        }
        var headGeo = Hand.solidSurface(head, V(-1.1, -0.4, -1.9), V(1.1, 1.5, 2.1), 0.045);
        var jawGeo = Hand.solidSurface(jaw, V(-0.85, -0.5, -1.6), V(0.85, 0.45, 1.25), 0.045);
        THREE.BufferGeometry tooth(double x, double y, double z, double len, double r, bool down, double lean)
        {
            var t = new THREE.ConeGeometry(r, len, 7);
            t.translate(0, len / 2 - 0.04, 0); // the root sits just inside the bone
            t.rotateZ(lean);
            if (down) t.rotateX(Math.PI);
            t.translate(x, y, z);
            return t;
        }
        var upper = new List<THREE.BufferGeometry>();
        var lower = new List<THREE.BufferGeometry>();
        foreach (var s in new[] { -1, 1 })
        {
            for (int i = 0; i < 10; i++)
            {
                double u = i / 9.0;
                // along the underside of the snout, from the tip back to the cheek
                double z = -1.5 + u * 1.55;
                double t = (0.2 - z) / 1.7;
                double axisY = 0.35 - 0.15 * t;
                double r = 0.42 - 0.22 * t;
                double big = i == 2 ? 1.5 : 1;
                upper.Add(tooth(s * r * 0.72, axisY - r * 0.62, z, (0.2 + 0.06 * (1 - u)) * big, 0.042 * big, true, s * 0.12));
                // along the top of the lower jaw
                double jz = -1.3 + u * 1.5;
                double k = (0.95 - jz) / 2.27;
                lower.Add(tooth(s * (0.52 - 0.38 * k), -0.1 - 0.1 * k + (0.13 - 0.04 * k) * 0.7, jz, (0.16 + 0.05 * (1 - u)) * big, 0.036 * big, false, -s * 0.1));
            }
        }
        skullGeo = new SkullGeo
        {
            head = headGeo, jaw = jawGeo,
            upper = THREE.BufferGeometryUtils.mergeGeometries(upper), lower = THREE.BufferGeometryUtils.mergeGeometries(lower),
        };
        return skullGeo;
    }

    /// <summary>The great skull, with its horns and teeth. It is about four metres long at scale 1, and faces -z.</summary>
    public static THREE.Group buildMonsterSkull()
    {
        var parts = skullParts();
        var g = new THREE.Group();
        var bone = new THREE.MeshStandardMaterial { color = BONE, roughness = 0.85 };
        var enamel = new THREE.MeshStandardMaterial { color = 0xf4eedd, roughness = 0.5 };
        var head = new THREE.Mesh(parts.head, bone);
        var upper = new THREE.Mesh(parts.upper, enamel);
        head.castShadow = true;
        head.receiveShadow = true;
        g.add(head, upper);
        // the lower jaw hangs open from a hinge at the back of the skull, and its teeth go with it
        var hinge = new THREE.Group();
        hinge.position.set(0, -0.1, 0.95);
        hinge.rotation.x = -0.2;
        var jaw = new THREE.Mesh(parts.jaw, bone);
        var lower = new THREE.Mesh(parts.lower, enamel);
        jaw.position.set(0, 0.1, -0.95);
        lower.position.copy(jaw.position);
        jaw.castShadow = true;
        jaw.receiveShadow = true;
        hinge.add(jaw, lower);
        g.add(hinge);
        // the dark inside the sockets
        var dark = new THREE.MeshBasicMaterial { color = 0x14100c };
        foreach (var s in new[] { -1, 1 })
        {
            var socket = new THREE.Mesh(new THREE.SphereGeometry(0.2, 12, 8), dark);
            socket.position.set(s * 0.34, 0.6, 0.06);
            g.add(socket);
        }
        return g;
    }

    /// <summary>How high the sand is at a point: it heaps up here and there, then runs down into the sea.</summary>
    private static double sandAt(double x, double z) =>
        CAVE_Y + 0.12 * Math.Sin(x * 0.7 + z * 0.3) + 0.09 * Math.Sin(z * 0.9 - x * 0.2) - Math.Max(0, x - WATER_X + 2) * 0.3;

    /// <summary>The gold has slumped into several overlapping piles, the way loose coins do, rather than one smooth dome.</summary>
    private static readonly (double x, double z, double a, double w)[] PILES =
    {
        (0, 0, 1.45, 2.3),
        (-1.7, 1.1, 0.95, 1.5),
        (1.6, -1.1, 0.9, 1.5),
        (0.8, 2, 0.6, 1.2),
        (-1, -1.9, 0.55, 1.2),
    };

    /// <summary>How high the heap stands at a point, measured from the middle of the heap.</summary>
    private static double heapHeight(double dx, double dz)
    {
        double h = 0;
        foreach (var (x, z, a, w) in PILES) h += a * Math.Exp(-(sq(dx - x) + sq(dz - z)) / (w * w));
        return h + 0.035 * Math.Sin(dx * 5.3 + dz * 2.1) * Math.Min(1, h * 2);
    }

    public readonly THREE.Group group = new THREE.Group();
    public readonly List<Solid> colliders = new List<Solid>();
    private readonly THREE.Group heap = new THREE.Group();
    private readonly THREE.InstancedMesh coins;
    /// <summary>the church bell, half sunk in the gold; it settles with the heap once the gold is taken</summary>
    private THREE.Mesh bell;
    private readonly THREE.PointLight daylight;
    /// <summary>lights that can go off when the player is back in the stone room</summary>
    public readonly List<THREE.PointLight> lights = new List<THREE.PointLight>();
    private readonly List<(THREE.Sprite sprite, double speed)> clouds = new List<(THREE.Sprite, double)>();
    private THREE.Object3D shark;

    public SeaCave()
    {
        buildRock();
        buildSand();
        buildBones();
        var gold = new THREE.MeshStandardMaterial { color = 0xe0ae3a, roughness = 0.42, metalness = 0.45, emissive = 0x3a2604, emissiveIntensity = 0.6 };
        var piled = coinTexture();
        heap.position.set(HOARD.x, CAVE_Y, HOARD.z);
        group.add(heap);
        coins = buildHeap(piled);
        buildBars(gold);
        buildChests(piled);
        buildBell();
        colliders.Add(new Solid { x = HOARD.x, z = HOARD.z, r = HOARD.r * 0.72 });
        // warm light on the gold, cold daylight from the sea
        var warm = new THREE.PointLight(0xffc268, 26, 16, 2);
        warm.position.set(HOARD.x - 3, CAVE_Y + 3.2, HOARD.z);
        daylight = new THREE.PointLight(0x8fd8ff, 40, 34, 2);
        daylight.position.set(31, CAVE_Y + 2.6, 0.5);
        group.add(warm, daylight);
        lights.Add(warm);
        lights.Add(daylight);
    }

    /// <summary>A rough dome of rock with its far end broken open to the sea.</summary>
    private void buildRock()
    {
        var geo = new THREE.SphereGeometry(1, 72, 36, 0, Math.PI * 2, 0, Math.PI * 0.56);
        var pos = geo.attributes.position;
        var open = new List<bool>();
        var col = new float[pos.count * 3];
        var shade = new THREE.Color();
        for (int i = 0; i < pos.count; i++)
        {
            double x = pos.getX(i), y = pos.getY(i), z = pos.getZ(i);
            double bump = 1 + 0.07 * Math.Sin(x * 9 + y * 5) + 0.05 * Math.Sin(z * 11 - y * 7) + 0.03 * Math.Sin(x * 23 + z * 19) + 0.018 * Math.Sin(x * 51 + y * 37) * Math.Sin(z * 43);
            open.Add(x > 0.62 && y < 0.62 && Math.Abs(z) < 0.62);
            // the west end is cut back flat, so the stair from the door comes out at the foot of the wall
            double wx = Math.Max(25 + x * 14.5 * bump, PORTAL_X + 0.12 * Math.Sin(z * 9 + y * 7));
            pos.setXYZ(i, wx, CAVE_Y - 0.6 + y * 8 * bump, 0.5 + z * 12 * bump);
            shade.setRGB(0.92, 0.85, 0.76).multiplyScalar(0.72 + 0.4 * Math.Abs(Math.Sin(x * 13 + y * 9)));
            col[i * 3] = (float)shade.r;
            col[i * 3 + 1] = (float)shade.g;
            col[i * 3 + 2] = (float)shade.b;
        }
        geo.setAttribute("color", new THREE.BufferAttribute(col, 3));
        var index = geo.index;
        var kept = new List<int>();
        for (int i = 0; i < index.count; i += 3)
        {
            int a = (int)index.getX(i), b = (int)index.getX(i + 1), c = (int)index.getX(i + 2);
            if (!(open[a] && open[b] && open[c])) { kept.Add(a); kept.Add(b); kept.Add(c); }
        }
        geo.setIndex(kept);
        geo.computeVertexNormals();
        var stone = rockTexture();
        var rockMat = new THREE.MeshStandardMaterial { vertexColors = true, map = stone, bumpMap = stone, bumpScale = 3, roughness = 0.95, side = THREE.Side.BackSide, flatShading = true };
        // the doorway: the rock simply is not there inside the opening of the stair (the discard lives in the Cozy/CaveRock shader)
        rockMat.customShader = "Cozy/CaveRock";
        var rock = new THREE.Mesh(geo, rockMat);
        rock.receiveShadow = true;
        group.add(rock);
    }

    private void buildSand()
    {
        var geo = new THREE.PlaneGeometry(31, 30, 62, 60);
        geo.rotateX(-Math.PI / 2);
        geo.translate(13 + 31 / 2.0, 0, 0.5);
        var pos = geo.attributes.position;
        for (int i = 0; i < pos.count; i++) pos.setY(i, sandAt(pos.getX(i), pos.getZ(i)));
        geo.computeVertexNormals();
        var sand = new THREE.Mesh(geo, new THREE.MeshStandardMaterial { color = 0xc9b08a, roughness = 1 });
        sand.receiveShadow = true;
        group.add(sand);
    }

    /// <summary>The real sea, seen through the mouth of the cave.</summary>
    public void addSea(THREE.Mesh sheet)
    {
        sheet.position.set(WATER_X - 2 + 350, WATER_Y, 0.5);
        group.add(sheet);
    }

    /// <summary>The sky's own clouds, drifting low enough over the water to be seen from in here.</summary>
    public void addClouds(IList<THREE.SpriteMaterial> mats)
    {
        var rand = rng(29);
        for (int i = 0; i < 9; i++)
        {
            var sprite = new THREE.Sprite(mats[i % mats.Count]);
            sprite.scale.set(55 + rand() * 60, 16 + rand() * 12, 1);
            sprite.position.set(120 + rand() * 170, 24 + rand() * 40, -130 + rand() * 260);
            group.add(sprite);
            clouds.Add((sprite, 0.8 + rand() * 1.2));
        }
    }

    /// <summary>One shark, cruising to and fro beyond the mouth of the cave.</summary>
    public void addShark(THREE.Object3D model)
    {
        // a copy of just its shape: the original is tied to the game's targets, and this one is only for looking at
        THREE.Object3D copy(THREE.Object3D o)
        {
            THREE.Object3D c = o is THREE.Mesh m ? (m.materials != null ? new THREE.Mesh(m.geometry, m.materials) : new THREE.Mesh(m.geometry, m.material)) : new THREE.Group();
            c.position.copy(o.position);
            c.quaternion.copy(o.quaternion);
            c.scale.copy(o.scale);
            foreach (var k in o.children.ToList()) c.add(copy(k));
            return c;
        }
        shark = copy(model);
        group.add(shark);
    }

    /// <summary>The skeleton lies in a long curve around the back of the cave, its ribs standing up out of the sand.</summary>
    private void buildBones()
    {
        var mat = new THREE.MeshStandardMaterial { color = BONE, roughness = 0.85 };
        var spine = new THREE.CatmullRomCurve3(new[]
        {
            new THREE.Vector3(17.3, CAVE_Y + 1.1, -4.3),
            new THREE.Vector3(19.5, CAVE_Y + 2.1, -5.8),
            new THREE.Vector3(22.5, CAVE_Y + 2.7, -6.3),
            new THREE.Vector3(25.5, CAVE_Y + 2.5, -5.9),
            new THREE.Vector3(28, CAVE_Y + 1.6, -4.6),
            new THREE.Vector3(29.4, CAVE_Y + 0.7, -2.4),
            new THREE.Vector3(29, CAVE_Y + 0.25, -0.2),
        });
        var parts = new List<THREE.BufferGeometry>();
        var up = new THREE.Vector3(0, 1, 0);
        // the backbone, knob by knob, getting smaller toward the tail
        const int knobs = 46;
        for (int i = 0; i <= knobs; i++)
        {
            double u = (double)i / knobs;
            var at = spine.getPointAt(u);
            var dir = spine.getTangentAt(u);
            double r = 0.34 * (1 - u * 0.78);
            var knob = new THREE.CylinderGeometry(r, r * 0.92, 0.3 - u * 0.12, 10);
            knob.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(up, dir));
            knob.translate(at.x, at.y, at.z);
            parts.Add(knob);
            if (u < 0.72)
            {
                var spike = new THREE.ConeGeometry(0.1 * (1 - u * 0.6), 0.75 * (1 - u * 0.6), 6);
                spike.translate(at.x, at.y + 0.42 * (1 - u * 0.5), at.z);
                parts.Add(spike);
            }
        }
        // the ribs arch out and down on both sides, longest in the middle
        for (int i = 0; i < 13; i++)
        {
            double u = 0.1 + i / 12.0 * 0.58;
            var at = spine.getPointAt(u);
            var dir = spine.getTangentAt(u);
            var side = new THREE.Vector3().crossVectors(dir, up).normalize();
            double size = (1.4 + 1.5 * Math.Sin(i / 12.0 * Math.PI)) * 1.05;
            foreach (var s in new[] { -1, 1 })
            {
                var pts = new[] { 0, 0.3, 0.6, 0.85, 1 }.Select(k =>
                {
                    double o = Math.Sin(k * Math.PI * 0.62) * size * 1.2;
                    double y = Math.Max(sandAt(at.x, at.z) - 0.1, at.y + Math.Sin(k * 1.4) * size * 0.2 - k * k * (at.y - CAVE_Y + 0.3));
                    return at.clone().addScaledVector(side, s * o).setY(y);
                }).ToList();
                parts.Add(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 14, 0.1 - i * 0.002, 7));
            }
            colliders.Add(new Solid { x = at.x, z = at.z, r = 2.1 });
        }
        var merged = THREE.BufferGeometryUtils.mergeGeometries(parts.Select(p => p.index != null ? p.toNonIndexed() : p).Select(p =>
        {
            p.deleteAttribute("uv");
            return p;
        }).ToList());
        var bones = new THREE.Mesh(merged, mat);
        bones.castShadow = true;
        bones.receiveShadow = true;
        group.add(bones);
        // the skull rests on the sand at the end of the neck, jaw open toward whoever comes in
        var skull = buildMonsterSkull();
        skull.scale.setScalar(1.25);
        skull.position.set(15.9, CAVE_Y + 0.72, -2.9);
        skull.rotation.set(0, Math.PI * 0.72, 0.12);
        group.add(skull);
        colliders.Add(new Solid { x = 15.9, z = -2.9, r = 2.2 });
    }

    /// <summary>The heap: a body of piled coins, thousands of loose ones lying on its slopes, and a few jewels among them.</summary>
    private THREE.InstancedMesh buildHeap(THREE.CanvasTexture piled)
    {
        var body = new THREE.PlaneGeometry(12, 12, 90, 90);
        body.rotateX(-Math.PI / 2);
        var pos = body.attributes.position;
        double ground(double dx, double dz) => sandAt(HOARD.x + dx, HOARD.z + dz) - CAVE_Y;
        for (int i = 0; i < pos.count; i++)
        {
            double dx = pos.getX(i), dz = pos.getZ(i);
            double h = heapHeight(dx, dz);
            pos.setY(i, ground(dx, dz) + (h < 0.03 ? -0.3 : h));
        }
        body.computeVertexNormals();
        var uv = body.attributes.uv;
        for (int i = 0; i < uv.count; i++) uv.setXY(i, uv.getX(i) * 7, uv.getY(i) * 7);
        var bodyMat = new THREE.MeshStandardMaterial { map = piled, bumpMap = piled, bumpScale = 2, roughness = 0.5, metalness = 0.35, emissive = 0x2c1c04, emissiveIntensity = 0.5 };
        var mesh = new THREE.Mesh(body, bodyMat);
        mesh.receiveShadow = true;
        heap.add(mesh);

        void place(THREE.Object3D d, double dx, double dz, double tilt, Func<double> rnd, double lift)
        {
            const double e = 0.08;
            var n = new THREE.Vector3(heapHeight(dx - e, dz) - heapHeight(dx + e, dz), 2 * e, heapHeight(dx, dz - e) - heapHeight(dx, dz + e)).normalize();
            d.position.set(dx, ground(dx, dz) + heapHeight(dx, dz) + lift, dz);
            d.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), n);
            d.rotateY(rnd() * 6.28);
            d.rotateX((rnd() - 0.5) * tilt);
        }

        const int N = 5200;
        var coinMesh = new THREE.InstancedMesh(new THREE.CylinderGeometry(0.075, 0.075, 0.016, 10), new THREE.MeshStandardMaterial { roughness = 0.34, metalness = 0.5, emissive = 0x3a2604, emissiveIntensity = 0.55 }, N);
        var rand = rng(41);
        var dd = new THREE.Object3D();
        var a = new THREE.Color(0xf2c64e);
        var b = new THREE.Color(0xb98422);
        var tint = new THREE.Color();
        for (int i = 0; i < N; i++)
        {
            double ang = rand() * Math.PI * 2;
            double r = Math.Sqrt(rand()) * 5;
            double dx = Math.Cos(ang) * r;
            double dz = Math.Sin(ang) * r;
            // loose coins are rare out at the edge, and lie flat there
            place(dd, dx, dz, heapHeight(dx, dz) < 0.05 ? 0.25 : 0.9, rand, 0.02);
            dd.updateMatrix();
            coinMesh.setMatrixAt(i, dd.matrix);
            coinMesh.setColorAt(i, tint.copy(a).lerp(b, rand()));
        }
        heap.add(coinMesh);

        // a scatter of cut stones among the coins
        var jewels = new THREE.InstancedMesh(new THREE.OctahedronGeometry(0.1), new THREE.MeshStandardMaterial { roughness = 0.15, metalness = 0.2 }, 70);
        var shades = new[] { 0xd32f4b, 0x2f9e6e, 0x3f7fd9, 0xe8e2d4 };
        for (int i = 0; i < 70; i++)
        {
            double ang = rand() * Math.PI * 2;
            double r = Math.Sqrt(rand()) * 3.2;
            place(dd, Math.Cos(ang) * r, Math.Sin(ang) * r, 1.2, rand, 0.05);
            dd.updateMatrix();
            jewels.setMatrixAt(i, dd.matrix);
            jewels.setColorAt(i, tint.set(shades[i % 4]));
        }
        heap.add(jewels);
        return coinMesh;
    }

    /// <summary>The town's forty bars, stacked as they were loaded and slumped where the sea left them.</summary>
    private void buildBars(THREE.Material gold)
    {
        var bars = new THREE.InstancedMesh(new THREE.BoxGeometry(0.42, 0.13, 0.2), gold, 40);
        var rand = rng(77);
        var d = new THREE.Object3D();
        for (int i = 0; i < 40; i++)
        {
            int layer = i < 22 ? 0 : i < 34 ? 1 : 2;
            double a = 3.6 + i % 12 * 0.13 + rand() * 0.05;
            double r = 3.9 + i % 4 * 0.27;
            double x = HOARD.x + Math.Cos(a) * r;
            double z = HOARD.z + Math.Sin(a) * r;
            d.position.set(x, sandAt(x, z) + heapHeight(x - HOARD.x, z - HOARD.z) * 0.6 + 0.07 + layer * 0.14, z);
            // the arguments are evaluated left to right, as in the browser
            double ry = a + rand() * 0.5;
            double rz = (rand() - 0.5) * 0.12;
            d.rotation.set(0, ry, rz);
            d.updateMatrix();
            bars.setMatrixAt(i, d.matrix);
        }
        bars.castShadow = true;
        group.add(bars);
        colliders.Add(new Solid { x = HOARD.x - 3.9, z = HOARD.z - 1.1, r = 1 });
    }

    /// <summary>Two sea chests thrown open, each a real box with a curved lid on a hinge, full to the top.</summary>
    private void buildChests(THREE.CanvasTexture piled)
    {
        var woodM = new THREE.MeshStandardMaterial { color = 0x5a3a26, roughness = 0.9 };
        var iron = new THREE.MeshStandardMaterial { color = 0x35302c, roughness = 0.55, metalness = 0.6 };
        var inside = new THREE.MeshStandardMaterial { map = piled, bumpMap = piled, bumpScale = 2, roughness = 0.5, metalness = 0.35, emissive = 0x2c1c04, emissiveIntensity = 0.5 };
        const double L = 1.3, D = 0.8, H = 0.62, T = 0.06;
        // the lid's end shape: flat along the bottom, arched over the top, with the hinge at the back edge
        THREE.Shape arch(double grow)
        {
            var o = new THREE.Shape();
            o.moveTo(-grow, 0);
            o.lineTo(D + grow, 0);
            o.absellipse(D / 2, 0, D / 2 + grow, 0.3 + grow, 0, Math.PI, false, 0);
            o.closePath();
            return o;
        }
        THREE.Mesh lidPiece(double grow, double wide, double x, THREE.Material m)
        {
            var geo = new THREE.ExtrudeGeometry(arch(grow), new THREE.ExtrudeGeometryOptions { depth = wide, bevelEnabled = false, curveSegments = 14 });
            geo.rotateY(-Math.PI / 2);
            geo.translate(x + wide / 2, 0, 0);
            return new THREE.Mesh(geo, m) { castShadow = true };
        }
        foreach (var (x, z, rot) in new[] { (27.6, 5.0, -0.5), (21.5, 5.6, 0.7) })
        {
            var g = new THREE.Group();
            void part(double w, double h, double d, double px, double py, double pz, THREE.Material m)
            {
                var mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), m);
                mesh.position.set(px, py, pz);
                mesh.castShadow = true;
                mesh.receiveShadow = true;
                g.add(mesh);
            }
            part(L, T, D, 0, T / 2, 0, woodM);
            foreach (var s in new[] { -1, 1 })
            {
                part(L, H, T, 0, H / 2, s * (D - T) / 2, woodM);
                part(T, H, D - T * 2, s * (L - T) / 2, H / 2, 0, woodM);
                // iron straps round the outside
                foreach (var bx in new[] { -0.42, 0.42 }) part(0.1, H + 0.02, 0.015, bx, H / 2, s * (D + 0.015) / 2, iron);
            }
            // heaped full of coins
            var fill = new THREE.Mesh(new THREE.SphereGeometry(1, 20, 10, 0, Math.PI * 2, 0, Math.PI / 2), inside);
            fill.scale.set((L - T * 2) / 2, 0.2, (D - T * 2) / 2);
            fill.position.y = H - 0.03;
            g.add(fill);
            var lid = new THREE.Group();
            lid.position.set(0, H, -D / 2);
            lid.rotation.x = -1.95;
            lid.add(lidPiece(0, L, 0, woodM));
            foreach (var bx in new[] { -0.42, 0.42 }) lid.add(lidPiece(0.015, 0.1, bx, iron));
            g.add(lid);
            g.position.set(x, sandAt(x, z), z);
            g.rotation.y = rot;
            group.add(g);
            colliders.Add(new Solid { x = x, z = z, r = 1 });
        }
    }

    /// <summary>The town's old church bell, half sunk in the gold.</summary>
    private void buildBell()
    {
        var profile = new[] { (0, 1.1), (0.14, 1.08), (0.24, 0.96), (0.3, 0.7), (0.36, 0.4), (0.5, 0.12), (0.6, 0.0) }.Select(p => new THREE.Vector2(p.Item1, p.Item2)).ToList();
        var bronze = new THREE.MeshStandardMaterial { color = 0x8a6a3a, roughness = 0.45, metalness = 0.75, side = THREE.Side.DoubleSide };
        var b = new THREE.Mesh(new THREE.LatheGeometry(profile, 24), bronze);
        b.rotation.set(0.5, 0, -0.42);
        b.castShadow = true;
        group.add(b);
        bell = b;
        settleBell(1);
    }

    /// <summary>Sits the bell into the heap, which stands squash of its full height (the heap is scaled from the cave floor up).</summary>
    private void settleBell(double squash)
    {
        double x = HOARD.x + 1.9;
        double z = HOARD.z + 1.4;
        // on whichever is higher there: what is left of the gold, or the sand under it
        double top = Math.Max(sandAt(x, z), CAVE_Y + (sandAt(x, z) - CAVE_Y + heapHeight(1.9, 1.4)) * squash);
        bell.position.set(x, top - 0.15, z);
    }

    /// <summary>The height of the sand under a point.</summary>
    public double floorAt(double x, double z) => sandAt(x, z);

    /// <summary>Keeps a walker on the sand of the cave.</summary>
    public (double x, double z) clamp(double x, double z)
    {
        double nx = (x - CAVE.x) / CAVE.rx;
        double nz = (z - CAVE.z) / CAVE.rz;
        double d = JS.Hypot(nx, nz);
        double k = d > 1 ? 1 / d : 1;
        return (Math.Min(WATER_X, CAVE.x + nx * k * CAVE.rx), CAVE.z + nz * k * CAVE.rz);
    }

    /// <summary>True when close enough to reach the gold.</summary>
    public bool nearGold(double x, double z) => JS.Hypot(x - HOARD.x, z - HOARD.z) < HOARD.r + 1.5;

    /// <summary>Once it has been taken only a thin scattering is left, so the cave stays a place worth visiting.</summary>
    public void setTaken(bool on)
    {
        coins.count = on ? 1400 : 5200;
        heap.scale.y = on ? 0.3 : 1;
        // the bell is not part of the heap, so it is lowered with it rather than left hanging where the gold used to be
        settleBell(heap.scale.y);
    }

    /// <summary>How much daylight comes in from the sea: 0 at night .. 1 by day.</summary>
    public void setDaylight(double k) => daylight.intensity = 3 + 37 * k;

    public void update(double t, double dt = 0)
    {
        foreach (var c in clouds)
        {
            c.sprite.position.z += c.speed * dt;
            if (c.sprite.position.z > 150) c.sprite.position.z = -150;
        }
        if (shark != null)
        {
            // a long lazy oval out in the bay; the fin shows above the water and the rest stays under it
            double a = t * 0.13;
            double x = 66 + Math.Cos(a) * 16;
            double z = 0.5 + Math.Sin(a) * 26;
            shark.position.set(x, WATER_Y - 0.24, z);
            shark.rotation.set(0, Math.Atan2(Math.Sin(a) * 16, -Math.Cos(a) * 26), 0);
        }
    }

    /// <summary>The stone-framed iron grate on the beach, over the shaft that lights the great door.</summary>
    public static THREE.Group buildGrate()
    {
        var g = new THREE.Group();
        var stone = new THREE.MeshStandardMaterial { color = 0x9a9184, roughness = 0.95 };
        var iron = new THREE.MeshStandardMaterial { color = 0x33302c, roughness = 0.55, metalness = 0.6 };
        var dark = new THREE.MeshBasicMaterial { color = 0x0a0806 };
        const double W = 1.7, D = 1.3, rim = 0.32;
        void block(double w, double d, double x, double z)
        {
            var m = new THREE.Mesh(new THREE.BoxGeometry(w, 0.42, d), stone);
            m.position.set(x, 0.03, z);
            m.castShadow = true;
            m.receiveShadow = true;
            g.add(m);
        }
        block(W + rim * 2, rim, 0, -(D + rim) / 2);
        block(W + rim * 2, rim, 0, (D + rim) / 2);
        block(rim, D, -(W + rim) / 2, 0);
        block(rim, D, (W + rim) / 2, 0);
        // the dark of the shaft below
        var pit = new THREE.Mesh(new THREE.PlaneGeometry(W, D), dark);
        pit.rotation.x = -Math.PI / 2;
        pit.position.y = -0.02;
        g.add(pit);
        for (int i = 1; i < 5; i++)
        {
            var bar = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.07, D + 0.1), iron);
            bar.position.set(-W / 2 + W * i / 5, 0.17, 0);
            bar.castShadow = true;
            g.add(bar);
        }
        for (int i = 1; i < 4; i++)
        {
            var bar = new THREE.Mesh(new THREE.BoxGeometry(W + 0.1, 0.07, 0.06), iron);
            bar.position.set(0, 0.2, -D / 2 + D * i / 4);
            bar.castShadow = true;
            g.add(bar);
        }
        return g;
    }
}
