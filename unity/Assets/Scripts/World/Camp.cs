using System;
using System.Collections.Generic;
using System.Linq;
using static Beach;

// Port of src/world/Camp.ts

public class Camp
{
    /// <summary>'fire' | 'bin' | 'board' | 'photos' | 'bench' | 'lantern' | 'camper' | 'radio' | 'coffee' | 'book' | 'post' | 'hammock' | 'sign'</summary>
    public class Solid { public double x, z, r; }

    public struct HammockSpot { public double x, y, z, yaw, exitX, exitZ; }

    /// <summary>Wood of one colour is one shared material, so the props made from it can be welded together.</summary>
    private static readonly Dictionary<int, THREE.MeshStandardMaterial> woods = new Dictionary<int, THREE.MeshStandardMaterial>();

    private static THREE.MeshStandardMaterial wood(int c = 0x6b4a2f)
    {
        if (!woods.TryGetValue(c, out var m))
        {
            m = new THREE.MeshStandardMaterial { color = c, roughness = 0.9, metalness = 0 };
            woods[c] = m;
        }
        return m;
    }

    private static THREE.Mesh box(double w, double h, double d, THREE.Material mat, double x = 0, double y = 0, double z = 0)
    {
        var m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
        m.position.set(x, y, z);
        m.castShadow = true;
        m.receiveShadow = true;
        return m;
    }

    private static THREE.Mesh cyl(double rt, double rb, double h, THREE.Material mat, int seg = 12)
    {
        var m = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, h, seg), mat);
        m.castShadow = true;
        m.receiveShadow = true;
        return m;
    }

    public readonly THREE.Group group = new THREE.Group();
    public readonly List<THREE.Object3D> clickables = new List<THREE.Object3D>();
    /// <summary>circular footprints in world space, for player collision</summary>
    public readonly List<Solid> obstacles = new List<Solid>();
    private readonly List<THREE.MeshStandardMaterial> windows = new List<THREE.MeshStandardMaterial>();
    private readonly THREE.PointLight camperLight = new THREE.PointLight(0xffc070, 0, 9, 2);
    public readonly THREE.Vector3 firePos = new THREE.Vector3(0, CAMP_Y + 0.5, CAMP_Z);
    private readonly List<THREE.Mesh> flames = new List<THREE.Mesh>();
    private readonly THREE.Group radio = new THREE.Group();
    private readonly THREE.MeshStandardMaterial radioLed = new THREE.MeshStandardMaterial { color = 0x401010, emissive = 0xff3a2a, emissiveIntensity = 0 };
    private readonly THREE.Group radioAntenna = new THREE.Group();
    private bool radioOn = false;
    private readonly THREE.MeshStandardMaterial bookCover = new THREE.MeshStandardMaterial { color = 0x7b2d26, roughness = 0.7, emissive = 0xffb347, emissiveIntensity = 0 };
    private bool bookNew = false;
    private readonly THREE.Group mailFlag = new THREE.Group();
    private bool mailNew = false;
    private readonly THREE.Group hammockGroup = new THREE.Group();
    private readonly THREE.Group hammockCloth = new THREE.Group();
    private bool hammockOccupied = false;
    /// <summary>where the hammock sits in the camp, and which way it runs</summary>
    private readonly (double x, double z, double rot) hammockAt = (0, -5.2, 0.1);
    private readonly THREE.PointLight fireLight;
    private readonly THREE.Group rugGroup = new THREE.Group();
    private readonly THREE.Group lightsGroup = new THREE.Group();
    private readonly THREE.Group signGroup = new THREE.Group();
    private readonly List<THREE.MeshStandardMaterial> bulbs = new List<THREE.MeshStandardMaterial>();
    private readonly THREE.MeshStandardMaterial signMat = new THREE.MeshStandardMaterial { roughness = 0.8 };
    private string signText = "";
    private readonly Dictionary<string, (THREE.Group group, List<Solid> solids)> extras = new Dictionary<string, (THREE.Group, List<Solid>)>();
    private readonly List<THREE.Mesh> tikiFlames = new List<THREE.Mesh>();
    private readonly Dictionary<string, List<Solid>> decorSolids = new Dictionary<string, List<Solid>>
    {
        ["lights"] = new List<Solid> { new Solid { x = -6.2, z = CAMP_Z - 4.2, r = 0.3 }, new Solid { x = 6.0, z = CAMP_Z - 4.8, r = 0.3 } },
        ["sign"] = new List<Solid> { new Solid { x = 4.6, z = CAMP_Z + 6.4, r = 0.4 } },
    };
    private readonly THREE.PointLight lanternLight;
    private THREE.Mesh lanternGlass;
    private readonly ParticleSystem embers;
    private readonly THREE.Group shelf = new THREE.Group();
    private readonly Dictionary<string, THREE.Object3D> trophies = new Dictionary<string, THREE.Object3D>();
    private readonly THREE.Color ember = new THREE.Color(0xff9a3c);
    private double time = 0;
    private double emberTimer = 0;

    public Camp()
    {
        group.position.set(0, CAMP_Y, CAMP_Z);

        buildFire();
        buildBench();
        buildBin();
        buildBoard();
        buildPhotoBoard();
        buildPost();
        buildHammock();
        buildCamper();
        lanternGlass = new THREE.Mesh();
        lanternLight = new THREE.PointLight(0xffc874, 0, 12, 2);
        buildLantern();
        buildDecor();

        // solid footprints for the other props, so neither walking nor driving passes through them
        void solid(double lx, double lz, double r) => obstacles.Add(new Solid { x = lx, z = CAMP_Z + lz, r = r });
        solid(-12.4, 4.4, 0.7); // recycling bin
        solid(-4.7, 1.2, 1.0); // notice board
        solid(PHOTO_AT.x, PHOTO_AT.z, 1.0); // photo board
        solid(3.7, 0.8, 1.3); // workbench (and the journal on it)
        solid(1.7, 0.9, 0.45); // coffee stump
        solid(2.2, -1.6, 0.25); // lantern pole
        solid(6.3, 3.4, 0.3); // the post
        foreach (var s in new[] { -1, 1 })
        {
            // the two hammock posts
            var h = hammockAt;
            solid(h.x + Math.Cos(h.rot) * 1.7 * s, h.z - Math.Sin(h.rot) * 1.7 * s, 0.25);
        }

        fireLight = new THREE.PointLight(0xff8a3a, 20, 14, 2);
        fireLight.position.set(0, 0.9, 0);
        group.add(fireLight);

        embers = new ParticleSystem(80, 0.07, -0.4, true);
        group.add(embers.points);
        weldProps();
    }

    private void mark(THREE.Object3D obj, string kind, double radius = 1.1, double y = 0.8)
    {
        obj.userData["kind"] = kind;
        clickables.Add(obj);
        // generous invisible hit proxy
        var proxy = new THREE.Mesh(new THREE.SphereGeometry(radius, 8, 6), new THREE.MeshBasicMaterial { visible = false });
        proxy.position.y = y;
        obj.add(proxy);
    }

    /// <summary>
    /// Stands something built elsewhere in the camp (the aquarium): it can be clicked and walked up to like the camp's own
    /// props, and neither walking nor driving passes through it. `x`, `z` are in the camp's own space.
    /// </summary>
    public void addProp(THREE.Object3D obj, string kind, double x, double z, double hitRadius, double solidRadius)
    {
        mark(obj, kind, hitRadius, 1.1);
        group.add(obj);
        obstacles.Add(new Solid { x = x, z = CAMP_Z + z, r = solidRadius });
    }

    public string kindOf(THREE.Object3D obj)
    {
        var o = obj;
        while (o != null)
        {
            var k = o.userData.Str("kind");
            if (!string.IsNullOrEmpty(k)) return k;
            o = o.parent;
        }
        return null;
    }

    private void buildFire()
    {
        var g = new THREE.Group();
        var logMat = wood(0x5a3b24);
        for (int i = 0; i < 6; i++)
        {
            var log = cyl(0.11, 0.12, 1.2, logMat, 8);
            double a = i / 6.0 * Math.PI * 2;
            log.position.set(Math.Cos(a) * 0.35, 0.18, Math.Sin(a) * 0.35);
            log.rotation.z = Math.PI / 2 - 0.35;
            log.rotation.y = -a;
            g.add(log);
        }
        var stoneMat = new THREE.MeshStandardMaterial { color = 0x8b8680, roughness = 1 };
        for (int i = 0; i < 12; i++)
        {
            double a = i / 12.0 * Math.PI * 2;
            var s = new THREE.Mesh(new THREE.DodecahedronGeometry(0.2 + JS.Random() * 0.06), stoneMat);
            s.position.set(Math.Cos(a) * 0.95, 0.08, Math.Sin(a) * 0.95);
            s.rotation.set(JS.Random(), JS.Random(), JS.Random());
            s.castShadow = true;
            s.receiveShadow = true;
            g.add(s);
        }
        var glow = new THREE.Mesh(new THREE.CircleGeometry(0.8, 20), new THREE.MeshBasicMaterial { color = 0x2a1408 });
        glow.rotation.x = -Math.PI / 2;
        glow.position.y = 0.03;
        g.add(glow);
        var flameCols = new[] { 0xd9380f, 0xe0701a, 0xe8a33a };
        for (int i = 0; i < flameCols.Length; i++)
        {
            var f = new THREE.Mesh(
                new THREE.ConeGeometry(0.34 - i * 0.07, 1.1 - i * 0.2, 10, 1, true),
                new THREE.MeshBasicMaterial
                {
                    color = flameCols[i], transparent = true, opacity = 0.55, blending = THREE.Blending.AdditiveBlending, depthWrite = false, side = THREE.Side.DoubleSide,
                });
            f.position.y = 0.65 + i * 0.04;
            flames.Add(f);
            g.add(f);
        }
        // Tin mug on a stump beside the fire
        // the coffee mug on its own stump is a separate thing to interact with
        var cup = new THREE.Group();
        var stump = cyl(0.3, 0.34, 0.4, wood(0x7a5538), 14);
        stump.position.y = 0.2;
        var mug = cyl(0.1, 0.09, 0.2, new THREE.MeshStandardMaterial { color = 0xc9ced3, metalness = 0.8, roughness = 0.35 }, 14);
        mug.position.y = 0.5;
        var handle = new THREE.Mesh(new THREE.TorusGeometry(0.05, 0.012, 6, 12, Math.PI), new THREE.MeshStandardMaterial { color = 0xc9ced3, metalness = 0.8, roughness = 0.35 });
        handle.position.set(0.1, 0.5, 0);
        handle.rotation.z = -Math.PI / 2;
        var coffee = new THREE.Mesh(new THREE.CircleGeometry(0.085, 14), new THREE.MeshStandardMaterial { color = 0x3a2112, roughness = 0.3 });
        coffee.rotation.x = -Math.PI / 2;
        coffee.position.y = 0.595;
        cup.add(stump, mug, handle, coffee);
        cup.position.set(1.7, 0, 0.9);
        mark(cup, "coffee", 0.4, 0.5);
        group.add(cup);
        foreach (var a in new[] { Math.PI * 0.9, Math.PI * 1.35 })
        {
            var s = cyl(0.32, 0.36, 0.42, wood(0x7a5538), 14);
            s.position.set(Math.Cos(a) * 2, 0.21, Math.Sin(a) * 2);
            g.add(s);
        }
        mark(g, "fire");
        group.add(g);
    }

    private void buildBench()
    {
        var g = new THREE.Group();
        var top = box(2.2, 0.12, 1.0, wood(0x8a6240), 0, 1.0, 0);
        g.add(top);
        foreach (var sx in new[] { -1, 1 })
        foreach (var sz in new[] { -0.38, 0.38 })
            g.add(box(0.12, 1.0, 0.12, wood(0x5e4128), sx * 0.95, 0.5, sz));
        g.add(box(2.0, 0.08, 0.7, wood(0x5e4128), 0, 0.35, 0));
        // back shelf for trophies
        g.add(box(2.2, 0.08, 0.4, wood(0x7a5538), 0, 2.0, -0.4));
        g.add(box(0.08, 1.0, 0.08, wood(0x5e4128), -1.0, 1.5, -0.55), box(0.08, 1.0, 0.08, wood(0x5e4128), 1.0, 1.5, -0.55));
        shelf.position.set(0, 2.04, -0.4);
        g.add(shelf);
        g.position.set(3.7, 0, 0.8);
        g.rotation.y = -0.5;
        buildRadio(g);
        buildBook(g);
        mark(g, "bench");
        group.add(g);
    }

    /// <summary>A chunky retro radio on the workbench. Click it to play music.</summary>
    private void buildRadio(THREE.Group bench)
    {
        var r = radio;
        var shell = new THREE.MeshStandardMaterial { color = 0xd9704a, roughness = 0.5 };
        var cream = new THREE.MeshStandardMaterial { color = 0xf3ead2, roughness = 0.6 };
        var dark = new THREE.MeshStandardMaterial { color = 0x2b2420, roughness = 0.8 };
        r.add(box(0.56, 0.3, 0.2, shell, 0, 0.15, 0));
        r.add(box(0.5, 0.24, 0.02, cream, 0, 0.15, 0.105));
        for (int i = 0; i < 6; i++) r.add(box(0.2, 0.02, 0.012, dark, -0.12, 0.07 + i * 0.035, 0.12));
        r.add(box(0.17, 0.07, 0.012, dark, 0.14, 0.23, 0.12));
        r.add(box(0.012, 0.07, 0.014, new THREE.MeshStandardMaterial { color = 0xffd25a, emissive = 0xffa020, emissiveIntensity = 0.4 }, 0.12, 0.23, 0.125));
        foreach (var kx in new[] { 0.09, 0.2 })
        {
            var knob = cyl(0.032, 0.032, 0.03, dark, 12);
            knob.rotation.x = Math.PI / 2;
            knob.position.set(kx, 0.1, 0.13);
            r.add(knob);
        }
        var led = new THREE.Mesh(new THREE.SphereGeometry(0.016, 8, 6), radioLed);
        led.position.set(0.23, 0.2, 0.125);
        r.add(led);
        var handle = new THREE.Mesh(new THREE.TorusGeometry(0.12, 0.012, 6, 14, Math.PI), dark);
        handle.position.set(0, 0.3, 0);
        r.add(handle);
        // the aerial pivots at its foot, which sits in a small socket on the shell, so it reads as part of the radio
        var metal = new THREE.MeshStandardMaterial { color = 0xc9ced3, metalness = 0.8, roughness = 0.3 };
        var socket = cyl(0.022, 0.028, 0.03, dark, 10);
        socket.position.set(0.2, 0.312, -0.05);
        r.add(socket);
        var rod = cyl(0.006, 0.006, 0.5, metal, 6);
        rod.position.y = 0.23;
        var tip = new THREE.Mesh(new THREE.SphereGeometry(0.014, 8, 6), metal);
        tip.position.y = 0.48;
        var stub = cyl(0.011, 0.011, 0.05, metal, 8);
        stub.position.y = 0.01;
        radioAntenna.add(rod, tip, stub);
        radioAntenna.position.set(0.2, 0.32, -0.05);
        radioAntenna.rotation.z = -0.35;
        r.add(radioAntenna);
        r.position.set(0.72, 1.06, -0.12);
        r.rotation.y = 0.15;
        mark(r, "radio", 0.4, 0.2);
        bench.add(r);
    }

    // ---------------------------------------------------------------- decorations

    private void buildDecor()
    {
        // the rug: woven stripes with a border and fringe
        var cv = new HTMLCanvasElement(512, 320);
        var c = cv.getContext("2d");
        c.fillStyle = "#b8453a";
        c.fillRect(0, 0, 512, 320);
        c.fillStyle = "#f0dcae";
        c.fillRect(16, 16, 480, 288);
        var stripes = new[] { "#2f6f8a", "#e3a43b", "#b8453a", "#4a8a5a", "#2f6f8a" };
        for (int i = 0; i < stripes.Length; i++)
        {
            c.fillStyle = stripes[i];
            c.fillRect(34, 34 + i * 50, 444, 36);
        }
        c.fillStyle = "#f0dcae";
        for (int x = 48; x < 470; x += 38)
        {
            c.beginPath();
            c.moveTo(x, 160);
            c.lineTo(x + 12, 148);
            c.lineTo(x + 24, 160);
            c.lineTo(x + 12, 172);
            c.fill();
        }
        c.strokeStyle = "rgba(0,0,0,0.07)";
        for (int y = 0; y < 320; y += 4)
        {
            c.beginPath();
            c.moveTo(0, y);
            c.lineTo(512, y);
            c.stroke();
        }
        var tex = new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
        THREE.MeshStandardMaterial edge() => new THREE.MeshStandardMaterial { color = 0xb8453a, roughness = 1 };
        var rug = new THREE.Mesh(new THREE.BoxGeometry(3.6, 0.05, 2.25), new THREE.Material[]
        {
            edge(), edge(),
            new THREE.MeshStandardMaterial { map = tex, roughness = 1, polygonOffset = true, polygonOffsetFactor = -2, polygonOffsetUnits = -2 },
            edge(), edge(), edge(),
        });
        rug.position.y = 0.04;
        rug.receiveShadow = true;
        rugGroup.add(rug);
        // fringe at the two short ends
        var fringe = new THREE.MeshStandardMaterial { color = 0xf0dcae, roughness = 1 };
        foreach (var sx in new[] { -1, 1 })
        {
            for (int i = 0; i < 16; i++)
            {
                var f = new THREE.Mesh(new THREE.BoxGeometry(0.12, 0.012, 0.025), fringe);
                f.position.set(sx * 1.86, 0.05, -1.05 + i * 0.14);
                rugGroup.add(f);
            }
        }
        rugGroup.position.set(-0.6, 0, 4.4);
        rugGroup.rotation.y = 0.22;
        group.add(rugGroup);

        // string lights: two poles and a sagging wire with a bulb every little while
        var pole = new THREE.MeshStandardMaterial { color = 0x6b4a2f, roughness = 0.9 };
        var ends = new List<THREE.Vector3>();
        foreach (var s in decorSolids["lights"])
        {
            double lx = s.x;
            double lz = s.z - CAMP_Z;
            var p = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.09, 3.3, 8), pole);
            p.position.set(lx, 1.65, lz);
            p.castShadow = true;
            var cap = new THREE.Mesh(new THREE.SphereGeometry(0.09, 8, 6), pole);
            cap.position.set(lx, 3.32, lz);
            lightsGroup.add(p, cap);
            ends.Add(new THREE.Vector3(lx, 3.2, lz));
        }
        var wireMat = new THREE.MeshStandardMaterial { color = 0x1b1b1b, roughness = 0.6 };
        THREE.Vector3 at(double t, double drop) =>
            new THREE.Vector3().lerpVectors(ends[0], ends[1], t).add(new THREE.Vector3(0, -Math.Sin(t * Math.PI) * 0.7 - drop, 0));
        var pts = new List<THREE.Vector3>();
        for (int i = 0; i <= 24; i++) pts.Add(at(i / 24.0, 0));
        lightsGroup.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 48, 0.012, 5), wireMat));
        var colors = new[] { 0xffd27a, 0xff8a6a, 0x9fe08a, 0x8ac8ff, 0xffb0d0 };
        for (int i = 1; i < 19; i++)
        {
            int col = colors[i % colors.Length];
            var m = new THREE.MeshStandardMaterial { color = col, emissive = col, emissiveIntensity = 0.2, roughness = 0.3 };
            bulbs.Add(m);
            var b = new THREE.Mesh(new THREE.SphereGeometry(0.065, 8, 6), m);
            b.position.copy(at(i / 19.0, 0.07));
            var cap = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.025, 0.04, 6), wireMat);
            cap.position.copy(at(i / 19.0, 0)).add(new THREE.Vector3(0, -0.01, 0));
            lightsGroup.add(b, cap);
        }
        group.add(lightsGroup);
        buildExtras();

        // the sign: a post with a painted board, facing the sea
        // two posts stand behind the board, so nothing covers the writing
        var board = new THREE.Mesh(new THREE.BoxGeometry(1.7, 0.62, 0.07), new THREE.Material[] { pole, pole, pole, pole, signMat, signMat });
        board.position.set(0, 1.5, 0);
        board.castShadow = true;
        signGroup.add(board);
        foreach (var sx in new[] { -0.7, 0.7 })
        {
            var post = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.08, 1.85, 8), pole);
            post.position.set(sx, 0.92, -0.1);
            post.castShadow = true;
            signGroup.add(post);
        }
        signGroup.position.set(decorSolids["sign"][0].x, 0, decorSolids["sign"][0].z - CAMP_Z);
        signGroup.rotation.y = 0.15;
        mark(signGroup, "sign", 1.1, 1.5);
        group.add(signGroup);
        // only clickable once it has been bought
        clickables.Remove(signGroup);
        setSignText("Cozy Camp");
        rugGroup.visible = lightsGroup.visible = signGroup.visible = false;
    }

    /// <summary>Adds one of the extra decorations: a group at a camp-local spot, with the solid footprints it should have.</summary>
    private void addExtra(string id, THREE.Group g, (double x, double z, double r)[] solids)
    {
        g.visible = false;
        group.add(g);
        extras[id] = (g, solids.Select(s => new Solid { x = s.x, z = CAMP_Z + s.z, r = s.r }).ToList());
    }

    private void buildExtras()
    {
        var woodM = new THREE.MeshStandardMaterial { color = 0x7a5538, roughness = 0.9 };
        THREE.CanvasTexture stripes(string a, string b, int n)
        {
            var cv = new HTMLCanvasElement(256, 16);
            var c = cv.getContext("2d");
            double w = 256.0 / n;
            for (int i = 0; i < n; i++)
            {
                c.fillStyle = i % 2 != 0 ? b : a;
                c.fillRect(i * w, 0, w + 1, 16);
            }
            var t = new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace };
            t.wrapS = THREE.Wrapping.RepeatWrapping;
            return t;
        }

        // a cooler box
        {
            var g = new THREE.Group();
            var red = new THREE.MeshStandardMaterial { color = 0xc8372e, roughness = 0.55 };
            var white = new THREE.MeshStandardMaterial { color = 0xf3f0e8, roughness = 0.5 };
            var body = new THREE.Mesh(new THREE.BoxGeometry(0.78, 0.4, 0.46), red);
            body.position.y = 0.22;
            var lid = new THREE.Mesh(new THREE.BoxGeometry(0.82, 0.09, 0.5), white);
            lid.position.y = 0.47;
            var handle = new THREE.Mesh(new THREE.TorusGeometry(0.08, 0.016, 6, 12, Math.PI), white);
            handle.position.set(0.4, 0.34, 0);
            handle.rotation.y = Math.PI / 2;
            foreach (var m in new[] { body, lid, handle }) m.castShadow = true;
            g.add(body, lid, handle);
            g.position.set(1.9, 0, 3.0);
            g.rotation.y = 0.5;
            addExtra("cooler", g, new[] { (1.9, 3.0, 0.5) });
        }

        // two potted palms
        {
            var g = new THREE.Group();
            var clay = new THREE.MeshStandardMaterial { color = 0xb8623c, roughness = 0.85 };
            var soil = new THREE.MeshStandardMaterial { color = 0x3a2a1c, roughness = 1 };
            var bark = new THREE.MeshStandardMaterial { color = 0x8a6a45, roughness = 0.9 };
            var leaf = new THREE.MeshStandardMaterial { color = 0x3f9a4a, roughness = 0.7, side = THREE.Side.DoubleSide };
            foreach (var (x, z, s) in new[] { (6.3, -1.3, 1.0), (-7.2, -1.6, 0.85) })
            {
                var p = new THREE.Group();
                var pot = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.2, 0.42, 14), clay);
                pot.position.y = 0.21;
                var rim = new THREE.Mesh(new THREE.TorusGeometry(0.3, 0.035, 6, 16), clay);
                rim.rotation.x = Math.PI / 2;
                rim.position.y = 0.42;
                var dirt = new THREE.Mesh(new THREE.CircleGeometry(0.28, 14), soil);
                dirt.rotation.x = -Math.PI / 2;
                dirt.position.y = 0.41;
                var trunk = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.06, 0.95, 7), bark);
                trunk.position.set(0.04, 0.9, 0);
                trunk.rotation.z = -0.08;
                p.add(pot, rim, dirt, trunk);
                for (int i = 0; i < 8; i++)
                {
                    double a = i / 8.0 * Math.PI * 2;
                    var f = new THREE.Mesh(new THREE.ConeGeometry(0.1, 0.95, 4), leaf);
                    f.scale.set(1, 1, 0.22);
                    f.geometry.translate(0, 0.47, 0);
                    f.castShadow = true;
                    var pivot = new THREE.Group();
                    pivot.position.set(0.08, 1.36, 0);
                    pivot.rotation.y = a;
                    f.rotation.set(1.1, 0, 0);
                    pivot.add(f);
                    p.add(pivot);
                }
                p.position.set(x, 0, z);
                p.scale.setScalar(s);
                g.add(p);
            }
            addExtra("palms", g, new[] { (6.3, -1.3, 0.45), (-7.2, -1.6, 0.4) });
        }

        // a beach umbrella with a folding lounger
        {
            var g = new THREE.Group();
            var white = new THREE.MeshStandardMaterial { color = 0xf4efe4, roughness = 0.6 };
            var pole = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.03, 2.4, 8), white);
            pole.position.y = 1.2;
            var canopyMat = new THREE.MeshStandardMaterial { map = stripes("#d84a4a", "#f4efe4", 12), roughness = 0.75, side = THREE.Side.DoubleSide };
            var canopy = new THREE.Mesh(new THREE.ConeGeometry(1.55, 0.55, 24, 1, true), canopyMat);
            canopy.position.y = 2.3;
            canopy.castShadow = true;
            var tip = new THREE.Mesh(new THREE.SphereGeometry(0.05, 8, 6), white);
            tip.position.y = 2.6;
            var um = new THREE.Group();
            um.add(pole, canopy, tip);
            um.rotation.z = 0.1;
            g.add(um);
            var fabric = new THREE.MeshStandardMaterial { map = stripes("#2f78a8", "#f4efe4", 8), roughness = 0.8 };
            var seat = new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.05, 0.72), fabric);
            seat.position.set(0.3, 0.3, 0.9);
            var back = new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.05, 0.8), fabric);
            back.position.set(0.3, 0.62, 1.55);
            back.rotation.x = -0.95;
            g.add(seat, back);
            foreach (var (x, z) in new[] { (0.0, 0.58), (0.6, 0.58), (0.0, 1.24), (0.6, 1.24) })
            {
                var leg = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.3, 0.04), woodM);
                leg.position.set(x, 0.15, z);
                g.add(leg);
            }
            g.position.set(-4.4, 0, 5.4);
            g.rotation.y = 0.3;
            addExtra("umbrella", g, new[] { (-4.4, 5.4, 0.35) });
        }

        // a surfboard rack
        {
            var g = new THREE.Group();
            foreach (var sx in new[] { -0.85, 0.85 })
            {
                var post = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.06, 1.6, 7), woodM);
                post.position.set(sx, 0.8, 0);
                post.castShadow = true;
                g.add(post);
            }
            foreach (var y in new[] { 0.45, 1.2 })
            {
                var bar = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.035, 1.9, 6), woodM);
                bar.rotation.z = Math.PI / 2;
                bar.position.set(0, y, 0);
                g.add(bar);
            }
            var colors = new[] { 0xe8604a, 0x2fa8a0, 0xf2c23a };
            for (int i = 0; i < colors.Length; i++)
            {
                var board = new THREE.Mesh(new THREE.SphereGeometry(1, 20, 12), new THREE.MeshStandardMaterial { color = colors[i], roughness = 0.35 });
                board.scale.set(0.25, 1.15, 0.04);
                board.position.set(-0.5 + i * 0.5, 1.2, 0.12);
                board.rotation.z = (i - 1) * 0.07;
                board.rotation.x = -0.1;
                board.castShadow = true;
                var stripe = new THREE.Mesh(new THREE.BoxGeometry(0.06, 1.6, 0.012), new THREE.MeshStandardMaterial { color = 0xf6f1e4, roughness = 0.4 });
                stripe.position.set(0, 0, 1.0);
                stripe.scale.set(0.24, 1.2, 0.2);
                board.add(stripe);
                g.add(board);
            }
            g.position.set(8.6, 0, -0.4);
            g.rotation.y = -0.9;
            addExtra("surf", g, new[] { (8.6, -0.4, 0.8) });
        }

        // tiki torches
        {
            var g = new THREE.Group();
            var bamboo = new THREE.MeshStandardMaterial { color = 0xc8a45a, roughness = 0.8 };
            var dark = new THREE.MeshStandardMaterial { color = 0x2a1c10, roughness = 0.9 };
            var flameMat = new THREE.MeshBasicMaterial { color = 0xff9a2a, transparent = true, opacity = 0.92, fog = true };
            var coreMat = new THREE.MeshBasicMaterial { color = 0xffe07a, transparent = true, opacity = 0.95 };
            foreach (var (x, z) in new[] { (-3.2, 7.3), (1.8, 7.5) })
            {
                var t = new THREE.Group();
                var pole = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.055, 1.9, 8), bamboo);
                pole.position.y = 0.95;
                pole.castShadow = true;
                t.add(pole);
                foreach (var y in new[] { 0.4, 0.9, 1.4 })
                {
                    var ring = new THREE.Mesh(new THREE.TorusGeometry(0.052, 0.012, 5, 10), dark);
                    ring.rotation.x = Math.PI / 2;
                    ring.position.y = y;
                    t.add(ring);
                }
                var cup = new THREE.Mesh(new THREE.CylinderGeometry(0.11, 0.06, 0.16, 10, 1, true), dark);
                cup.material = new THREE.MeshStandardMaterial { color = 0x2a1c10, roughness = 0.9, side = THREE.Side.DoubleSide };
                cup.position.y = 1.98;
                var flame = new THREE.Mesh(new THREE.ConeGeometry(0.085, 0.32, 8), flameMat);
                flame.position.y = 2.17;
                var core = new THREE.Mesh(new THREE.ConeGeometry(0.045, 0.2, 6), coreMat);
                core.position.y = 2.12;
                t.add(cup, flame, core);
                tikiFlames.Add(flame);
                tikiFlames.Add(core);
                t.position.set(x, 0, z);
                g.add(t);
            }
            addExtra("tiki", g, new[] { (-3.2, 7.3, 0.2), (1.8, 7.5, 0.2) });
        }
    }

    private void setSignText(string text)
    {
        if (text == signText) return;
        signText = text;
        var cv = new HTMLCanvasElement(512, 190);
        var c = cv.getContext("2d");
        c.fillStyle = "#c8935a";
        c.fillRect(0, 0, 512, 190);
        c.strokeStyle = "rgba(80,45,20,0.25)";
        c.lineWidth = 2;
        for (int y = 20; y < 190; y += 26)
        {
            c.beginPath();
            c.moveTo(0, y);
            c.lineTo(512, y + 3);
            c.stroke();
        }
        c.strokeStyle = "#6b4325";
        c.lineWidth = 10;
        c.strokeRect(8, 8, 496, 174);
        c.fillStyle = "#3d2412";
        c.textAlign = "center";
        c.textBaseline = "middle";
        int size = 74;
        c.font = $"bold {size}px Georgia, serif";
        while (c.measureText(text).width > 440 && size > 28)
        {
            size -= 4;
            c.font = $"bold {size}px Georgia, serif";
        }
        c.fillText(text, 256, 98);
        var tex = new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace };
        signMat.map?.dispose();
        signMat.map = tex;
        signMat.needsUpdate = true;
    }

    private THREE.Group skull;

    /// <summary>The monster's skull, mounted on a stone plinth at the edge of the camp.</summary>
    public void showSkull()
    {
        if (skull != null) return;
        var g = new THREE.Group();
        var plinth = box(1.9, 0.62, 3.6, new THREE.MeshStandardMaterial { color = 0x8b8680, roughness = 1 }, 0, 0.19, 0);
        var head = SeaCave.buildMonsterSkull();
        head.position.y = 1.25;
        g.add(plinth, head);
        const double x = 9.4;
        const double z = -4.6;
        const double rot = 2.4;
        g.position.set(x, 0, z);
        g.rotation.y = rot;
        group.add(g);
        foreach (var s in new[] { -0.85, 0.85 })
            obstacles.Add(new Solid { x = x + Math.Sin(rot) * s, z = CAMP_Z + z + Math.Cos(rot) * s, r = 1.25 });
        skull = g;
    }

    /// <summary>Past their reach the camp's lights only cost time, so they switch off once the player is well away.</summary>
    public void setLightsNear(bool near)
    {
        fireLight.visible = lanternLight.visible = camperLight.visible = near;
    }

    /// <summary>Welds the fixed parts of each prop together; what moves or lights up on its own stays separate.</summary>
    private void weldProps()
    {
        var keep = new HashSet<THREE.Object3D>(flames.Cast<THREE.Object3D>().Concat(tikiFlames).Concat(new THREE.Object3D[] { mailFlag, radioAntenna, hammockCloth, lanternGlass, shelf })
            .Concat(photoSlots.Select(p => (THREE.Object3D)p.slot)));
        foreach (var prop in group.children.ToList().Concat(new THREE.Object3D[] { radio, hammockCloth }))
        {
            keep.Remove(prop);
            Merge.mergeStatic(prop, keep);
            if (prop == hammockCloth) keep.Add(prop);
        }
    }

    /// <summary>Shows the decorations the player has bought, and what the sign says.</summary>
    public void setDecor(IList<string> owned, string text)
    {
        rugGroup.visible = owned.Contains("rug");
        lightsGroup.visible = owned.Contains("lights");
        bool sign = owned.Contains("sign");
        signGroup.visible = sign;
        setSignText(text);
        bool has = clickables.Contains(signGroup);
        if (sign && !has) clickables.Add(signGroup);
        if (!sign && has) clickables.Remove(signGroup);
        void set(string key, bool on)
        {
            foreach (var s in decorSolids[key])
            {
                int i = obstacles.IndexOf(s);
                if (on && i < 0) obstacles.Add(s);
                if (!on && i >= 0) obstacles.RemoveAt(i);
            }
        }
        set("lights", owned.Contains("lights"));
        set("sign", sign);
        foreach (var kv in extras)
        {
            bool on = owned.Contains(kv.Key);
            kv.Value.group.visible = on;
            foreach (var s in kv.Value.solids)
            {
                int i = obstacles.IndexOf(s);
                if (on && i < 0) obstacles.Add(s);
                if (!on && i >= 0) obstacles.RemoveAt(i);
            }
        }
    }

    /// <summary>A striped hammock slung between two posts on spreader bars. Press F to lie in it and sleep until morning.</summary>
    private void buildHammock()
    {
        var g = hammockGroup;
        var rope = new THREE.MeshStandardMaterial { color = 0xcdbd95, roughness = 0.9 };
        var bar = wood(0x8a6240);
        var post = wood(0x6b4a2f);
        const double half = 1.3; // half the length of the cloth, between the bars
        const double wide = 0.4; // half its width
        const double drop = 0.14; // how far the bars hang below the knots on the posts
        const double sag = 0.6;
        THREE.Mesh strand(THREE.Vector3 a, THREE.Vector3 b, double r, THREE.Material mat)
        {
            var d = b.clone().sub(a);
            var m = cyl(r, r, d.length(), mat, 6);
            m.position.copy(a).add(b).multiplyScalar(0.5);
            m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), d.normalize());
            return m;
        }
        // the cloth and everything that hangs with it swing from the line between the two knots
        hammockCloth.position.y = 1.52;
        foreach (var s in new[] { -1, 1 })
        {
            var pole = cyl(0.07, 0.095, 2.05, post, 10);
            pole.position.set(s * 1.72, 1.02, 0);
            pole.rotation.z = -s * 0.03;
            pole.castShadow = true;
            g.add(pole);
            var top = new THREE.Mesh(new THREE.SphereGeometry(0.07, 10, 6), post);
            top.scale.y = 0.45;
            top.position.set(s * 1.75, 2.05, 0);
            g.add(top);
            // rope lashed around the post, then a fan of cords from the knot to the bar
            for (int i = 0; i < 3; i++)
            {
                var turn = new THREE.Mesh(new THREE.TorusGeometry(0.086, 0.011, 6, 16), rope);
                turn.rotation.x = Math.PI / 2;
                turn.position.set(s * 1.735, 1.5 + i * 0.022, 0);
                g.add(turn);
            }
            var knot = new THREE.Vector3(s * 1.655, 0, 0);
            var k = new THREE.Mesh(new THREE.SphereGeometry(0.026, 8, 6), rope);
            k.position.copy(knot);
            hammockCloth.add(k);
            for (int i = 0; i < 5; i++) hammockCloth.add(strand(knot, new THREE.Vector3(s * half, -drop, (i - 2) * 0.18), 0.007, rope));
            var spreader = cyl(0.02, 0.02, wide * 2 + 0.1, bar, 8);
            spreader.rotation.x = Math.PI / 2;
            spreader.position.set(s * half, -drop, 0);
            spreader.castShadow = true;
            hammockCloth.add(spreader);
        }
        // the stripes run the length of the cloth, the way a woven hammock's do
        THREE.CanvasTexture makeTex()
        {
            var cvs = new HTMLCanvasElement(8, 128);
            var x = cvs.getContext("2d");
            var cols = new[] { "#d9704a", "#e9d9b4", "#3f9a8d", "#e9d9b4", "#d9704a", "#e9d9b4", "#3f9a8d", "#e9d9b4", "#d9704a" };
            for (int i = 0; i < cols.Length; i++)
            {
                x.fillStyle = cols[i];
                x.fillRect(0, i * 128.0 / cols.Length, 8, 128.0 / cols.Length + 1);
            }
            return new THREE.CanvasTexture(cvs) { colorSpace = THREE.ColorSpace.SRGBColorSpace };
        }
        var tex = makeTex();
        // the cloth hangs in a long curve between the bars, and its sides rise into a cradle in the middle
        THREE.Vector3 surface(double u, double v)
        {
            double bowl = 1 - u * u;
            return new THREE.Vector3(u * half, -drop - sag * bowl + 0.2 * v * v * bowl, v * wide * (1 - 0.12 * bowl));
        }
        var geo = new THREE.PlaneGeometry(2 * half, 2 * wide, 32, 12);
        var p = geo.attributes.position;
        for (int i = 0; i < p.count; i++)
        {
            var atp = surface(p.getX(i) / half, p.getY(i) / wide);
            p.setXYZ(i, atp.x, atp.y, atp.z);
        }
        geo.computeVertexNormals();
        var cloth = new THREE.Mesh(geo, new THREE.MeshStandardMaterial { map = tex, roughness = 0.9, side = THREE.Side.DoubleSide });
        cloth.castShadow = true;
        cloth.receiveShadow = true;
        hammockCloth.add(cloth);
        // a rope hem down each long edge gives the cloth some weight
        foreach (var v in new[] { -1, 1 })
        {
            var pts = new List<THREE.Vector3>();
            for (int i = 0; i <= 20; i++) pts.Add(surface(i / 10.0 - 1, v));
            var hem = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 48, 0.012, 6), rope);
            hammockCloth.add(hem);
        }
        // a soft pillow up at the head end, plumped by squashing a ball and denting its middle
        var pillowGeo = new THREE.SphereGeometry(0.2, 18, 12);
        var q = pillowGeo.attributes.position;
        for (int i = 0; i < q.count; i++)
        {
            double dent = 1 - 0.28 * Math.Exp(-(Math.Pow(q.getX(i), 2) + Math.Pow(q.getZ(i), 2)) / 0.02);
            q.setY(i, q.getY(i) * dent);
        }
        pillowGeo.computeVertexNormals();
        var pillow = new THREE.Mesh(pillowGeo, new THREE.MeshStandardMaterial { color = 0xf3ead2, roughness = 0.9 });
        pillow.scale.set(0.95, 0.55, 1.45);
        var head = surface(-0.62, 0);
        pillow.position.set(head.x, head.y + 0.085, 0);
        pillow.rotation.z = -0.32;
        pillow.castShadow = true;
        hammockCloth.add(pillow);
        g.add(hammockCloth);
        g.position.set(hammockAt.x, 0, hammockAt.z);
        g.rotation.y = hammockAt.rot;
        mark(g, "hammock", 1.3, 0.9);
        group.add(g);
    }

    /// <summary>World position and facing of someone lying in the hammock, and a spot to stand when getting out.</summary>
    public HammockSpot hammockSpot()
    {
        group.updateWorldMatrix(true, true);
        var c = hammockGroup.localToWorld(new THREE.Vector3(0, 0.78, 0));
        var o = hammockGroup.localToWorld(new THREE.Vector3(0.2, 0, 1.6));
        return new HammockSpot { x = c.x, y = c.y, z = c.z, yaw = hammockAt.rot - Math.PI / 2, exitX = o.x, exitZ = o.z };
    }

    public void setHammockOccupied(bool on) => hammockOccupied = on;

    /// <summary>A leaning post with a blue mailbox. Its red flag goes up when a letter has arrived.</summary>
    private void buildPost()
    {
        var g = new THREE.Group();
        var blue = new THREE.MeshStandardMaterial { color = 0x3b6ea5, roughness = 0.55 };
        var red = new THREE.MeshStandardMaterial { color = 0xd9382c, roughness = 0.5 };
        g.add(box(0.14, 1.3, 0.14, wood(0x6b4a2f), 0, 0.65, 0));
        g.add(box(0.5, 0.26, 0.3, blue, 0, 1.43, 0));
        var roof = new THREE.Mesh(new THREE.CylinderGeometry(0.15, 0.15, 0.5, 14, 1, false, 0, Math.PI), blue);
        roof.rotation.set(0, 0, Math.PI / 2);
        roof.position.set(0, 1.56, 0);
        roof.scale.set(1, 1, 1);
        roof.castShadow = true;
        g.add(roof);
        g.add(box(0.2, 0.02, 0.02, new THREE.MeshStandardMaterial { color = 0x101820 }, 0, 1.45, 0.16));
        // the flag: a small red arm that swings up on a hinge
        mailFlag.position.set(0.27, 1.4, 0.0);
        mailFlag.add(box(0.03, 0.22, 0.03, red, 0, 0.11, 0), box(0.13, 0.08, 0.02, red, 0.065, 0.2, 0));
        g.add(mailFlag);
        g.position.set(6.3, 0, 3.4);
        g.rotation.y = -0.4;
        mark(g, "post", 0.7, 1.2);
        group.add(g);
    }

    /// <summary>The red flag stands up while there is unread mail.</summary>
    public void setMailNew(bool on) => mailNew = on;

    /// <summary>The camp journal: a closed book lying on the workbench beside the radio.</summary>
    private void buildBook(THREE.Group bench)
    {
        var b = new THREE.Group();
        var pages = new THREE.MeshStandardMaterial { color = 0xf1e6c8, roughness = 0.9 };
        var gold = new THREE.MeshStandardMaterial { color = 0xd6a94a, metalness = 0.6, roughness = 0.4 };
        b.add(box(0.37, 0.02, 0.27, bookCover, 0, 0.01, 0));
        b.add(box(0.33, 0.05, 0.245, pages, 0.012, 0.045, 0));
        b.add(box(0.37, 0.02, 0.27, bookCover, 0, 0.08, 0));
        b.add(box(0.03, 0.09, 0.27, bookCover, -0.175, 0.045, 0));
        b.add(box(0.2, 0.004, 0.12, gold, 0.02, 0.092, 0));
        // a ribbon marker hanging out of the bottom
        b.add(box(0.025, 0.004, 0.09, new THREE.MeshStandardMaterial { color = 0xe0b04a, roughness = 0.6 }, 0.1, 0.002, 0.17));
        b.position.set(-0.05, 1.06, 0.12);
        b.rotation.y = -0.3;
        mark(b, "book", 0.32, 0.12);
        bench.add(b);
    }

    /// <summary>The book glows softly while it holds pages the player has not read yet.</summary>
    public void setBookNew(bool on) => bookNew = on;

    /// <summary>World position of the radio, used for distance-based volume.</summary>
    public THREE.Vector3 radioPosition(THREE.Vector3 o = null) => radio.getWorldPosition(o ?? new THREE.Vector3());

    public void setRadio(bool on) => radioOn = on;

    private void buildBin()
    {
        var g = new THREE.Group();
        var green = new THREE.MeshStandardMaterial { color = 0x3f8f5a, roughness = 0.7 };
        var body = cyl(0.5, 0.42, 0.9, green, 16);
        body.position.y = 0.45;
        var lid = cyl(0.54, 0.54, 0.08, new THREE.MeshStandardMaterial { color = 0x2f7347, roughness = 0.7 }, 16);
        lid.position.y = 0.94;
        // the recycling symbol, painted on: a patch of the bin's own curved wall, a hair proud of it
        var sign = new THREE.Mesh(
            new THREE.CylinderGeometry(0.494, 0.45, 0.5, 8, 1, true, -0.54, 1.08),
            new THREE.MeshStandardMaterial { map = recycleLogo(), alphaTest = 0.5, roughness = 0.7 });
        sign.position.y = 0.45;
        g.add(body, lid, sign);
        // this spot is outside the levelled camp pad, where the sand lies a little lower
        g.position.set(-12.4, -0.24, 4.4);
        g.rotation.y = 0.9;
        mark(g, "bin");
        group.add(g);
    }

    /// <summary>Three arrows chasing each other around a triangle, white on nothing, as a texture.</summary>
    private THREE.CanvasTexture recycleLogo()
    {
        var cv = new HTMLCanvasElement(256, 256);
        var c = cv.getContext("2d");
        c.translate(128, 150);
        c.fillStyle = c.strokeStyle = "#ffffff";
        c.lineWidth = 27;
        const double R = 112;
        var top = (x: 0.0, y: -R);
        var left = (x: -R * Math.Sin(Math.PI / 3), y: R / 2);
        var right = (x: -left.x, y: R / 2);
        (double x, double y) along((double x, double y) p, (double x, double y) q, double u) => (p.x + (q.x - p.x) * u, p.y + (q.y - p.y) * u);
        // each arrow bends around one corner of the triangle and stops short of the tail of the next
        var from = along(left, top, 0.6);
        var shaft = along(top, right, 0.3);
        var tip = along(top, right, 0.58);
        double dx = (right.x - top.x) / (R * Math.Sqrt(3));
        double dy = (right.y - top.y) / (R * Math.Sqrt(3));
        for (int i = 0; i < 3; i++)
        {
            c.beginPath();
            c.moveTo(from.x, from.y);
            c.arcTo(top.x, top.y, shaft.x, shaft.y, 12);
            c.lineTo(shaft.x, shaft.y);
            c.stroke();
            c.beginPath();
            c.moveTo(tip.x, tip.y);
            c.lineTo(shaft.x - dy * 34, shaft.y + dx * 34);
            c.lineTo(shaft.x + dy * 34, shaft.y - dx * 34);
            c.closePath();
            c.fill();
            c.rotate(Math.PI * 2 / 3);
        }
        return new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
    }

    private void buildBoard()
    {
        var g = new THREE.Group();
        foreach (var sx in new[] { -0.85, 0.85 }) g.add(box(0.12, 2.2, 0.12, wood(0x5e4128), sx, 1.1, 0));
        g.add(box(2.0, 1.3, 0.08, wood(0xb38a5a), 0, 1.5, 0.04));
        g.add(box(2.2, 0.12, 0.2, wood(0x5e4128), 0, 2.25, 0));
        var colors = new[] { 0xfff3a8, 0xffc9d6, 0xc9f0ff, 0xd6ffc9 };
        for (int i = 0; i < colors.Length; i++)
        {
            var note = new THREE.Mesh(new THREE.PlaneGeometry(0.4, 0.4), new THREE.MeshStandardMaterial { color = colors[i], roughness = 0.9, side = THREE.Side.DoubleSide });
            note.position.set(-0.65 + i % 2 * 0.55 + (i > 1 ? 0.5 : 0), 1.75 - Math.Floor(i / 2.0) * 0.55, 0.1);
            note.rotation.z = (JS.Random() - 0.5) * 0.3;
            g.add(note);
        }
        // the title, painted on a cream plate along the top beam
        var cv = new HTMLCanvasElement(768, 112);
        var c = cv.getContext("2d");
        c.fillStyle = "#f6efe0";
        c.fillRect(0, 0, cv.width, cv.height);
        c.strokeStyle = "#5e4128";
        c.lineWidth = 6;
        c.strokeRect(9, 9, cv.width - 18, cv.height - 18);
        c.fillStyle = "#4a3020";
        c.font = "bold 66px Georgia, serif";
        c.textAlign = "center";
        c.textBaseline = "middle";
        c.fillText("LOST & FOUND", cv.width / 2.0, cv.height / 2.0 + 3);
        var tex = new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
        var title = new THREE.Mesh(new THREE.PlaneGeometry(1.8, 0.2625), new THREE.MeshStandardMaterial { map = tex, roughness = 1 });
        title.position.set(0, 2.25, 0.105);
        g.add(title);
        g.position.set(-4.7, 0, 1.2);
        g.rotation.y = 0.9;
        mark(g, "board");
        group.add(g);
    }

    /// <summary>where the photo board stands in the camp, and which way it faces</summary>
    private static readonly (double x, double z, double rot) PHOTO_AT = (-3.4, -2.8, 0.53);
    /// <summary>the polaroids on the photo board: each one's group and the material its picture is drawn with</summary>
    private readonly List<(THREE.Group slot, THREE.MeshStandardMaterial pic)> photoSlots = new List<(THREE.Group, THREE.MeshStandardMaterial)>();
    private readonly List<UnityEngine.Texture> shownPhotos = new List<UnityEngine.Texture>();

    /// <summary>A cork board for the camera's photos, with room for eight polaroids, next to the fire.</summary>
    private void buildPhotoBoard()
    {
        var g = new THREE.Group();
        foreach (var sx in new[] { -1.08, 1.08 }) g.add(box(0.12, 2.2, 0.12, wood(0x5e4128), sx, 1.1, 0));
        g.add(box(2.36, 1.36, 0.08, wood(0x5e4128), 0, 1.5, 0.0));
        var cork = new THREE.MeshStandardMaterial { color = 0xc49a6c, roughness = 1 };
        g.add(box(2.2, 1.2, 0.04, cork, 0, 1.5, 0.05));
        g.add(box(2.5, 0.12, 0.2, wood(0x5e4128), 0, 2.25, 0));
        var paper = new THREE.MeshStandardMaterial { color = 0xfdfbf4, roughness = 0.85 };
        var pinMat = new THREE.MeshStandardMaterial { color = 0xd94b3e, roughness = 0.4 };
        var tilts = new[] { -0.05, 0.04, -0.02, 0.06, 0.03, -0.06, 0.05, -0.03 };
        for (int i = 0; i < Photos.BOARD_SLOTS; i++)
        {
            int col = i % 4, row = i / 4;
            var slot = new THREE.Group();
            var frame = new THREE.Mesh(new THREE.PlaneGeometry(0.46, 0.42), paper);
            frame.receiveShadow = true;
            var pic = new THREE.MeshStandardMaterial { color = 0xffffff, roughness = 0.55 };
            var picture = new THREE.Mesh(new THREE.PlaneGeometry(0.4, 0.3), pic);
            picture.position.set(0, 0.03, 0.002);
            picture.receiveShadow = true;
            var pin = new THREE.Mesh(new THREE.SphereGeometry(0.022, 10, 8), pinMat);
            pin.position.set(0, 0.18, 0.02);
            pin.castShadow = true;
            slot.add(frame, picture, pin);
            slot.position.set(-0.81 + col * 0.54, 1.78 - row * 0.56, 0.08);
            slot.rotation.z = tilts[i];
            slot.visible = false;
            g.add(slot);
            photoSlots.Add((slot, pic));
        }
        // the title, painted on a cream plate along the top beam, like the Lost & Found board
        var cv = new HTMLCanvasElement(768, 112);
        var c = cv.getContext("2d");
        c.fillStyle = "#f6efe0";
        c.fillRect(0, 0, cv.width, cv.height);
        c.strokeStyle = "#5e4128";
        c.lineWidth = 6;
        c.strokeRect(9, 9, cv.width - 18, cv.height - 18);
        c.fillStyle = "#4a3020";
        c.font = "bold 66px Georgia, serif";
        c.textAlign = "center";
        c.textBaseline = "middle";
        c.fillText("PHOTOS", cv.width / 2.0, cv.height / 2.0 + 3);
        var tex = new THREE.CanvasTexture(cv) { colorSpace = THREE.ColorSpace.SRGBColorSpace, anisotropy = 4 };
        var title = new THREE.Mesh(new THREE.PlaneGeometry(1.8, 0.2625), new THREE.MeshStandardMaterial { map = tex, roughness = 1 });
        title.position.set(0, 2.25, 0.105);
        g.add(title);
        g.position.set(PHOTO_AT.x, 0, PHOTO_AT.z);
        g.rotation.y = PHOTO_AT.rot;
        mark(g, "photos", 1.2, 1.4);
        group.add(g);
    }

    /// <summary>A spot in front of the photo board, in world space, and the yaw that looks straight at it.</summary>
    public (double x, double z, double yaw) photoBoardFront(double dist)
    {
        double r = PHOTO_AT.rot;
        return (PHOTO_AT.x + Math.Sin(r) * dist, CAMP_Z + PHOTO_AT.z + Math.Cos(r) * dist, r);
    }

    /// <summary>Pins these pictures to the photo board, in order; the rest of the board stays empty.</summary>
    public void setPhotos(IList<UnityEngine.Texture> pics)
    {
        for (int i = 0; i < photoSlots.Count; i++)
        {
            var (slot, mat) = photoSlots[i];
            var tex = i < pics.Count ? pics[i] : null;
            slot.visible = tex != null;
            var was = i < shownPhotos.Count ? shownPhotos[i] : null;
            if (tex == was) continue;
            if (tex != null)
            {
                var t = new THREE.Texture { colorSpace = THREE.ColorSpace.SRGBColorSpace };
                t.userData["unity"] = tex;
                mat.map = t;
                mat.needsUpdate = true;
            }
        }
        shownPhotos.Clear();
        for (int i = 0; i < photoSlots.Count; i++) shownPhotos.Add(i < pics.Count ? pics[i] : null);
    }

    /// <summary>A little house on wheels parked beside the fire.</summary>
    private void buildCamper()
    {
        var g = new THREE.Group();
        var body = new THREE.MeshStandardMaterial { color = 0x9fd3c7, roughness = 0.6, metalness = 0.1 };
        var trim = new THREE.MeshStandardMaterial { color = 0xd9704a, roughness = 0.6 };
        var cream = new THREE.MeshStandardMaterial { color = 0xf3ead2, roughness = 0.7 };
        var black = new THREE.MeshStandardMaterial { color = 0x24262a, roughness = 0.8 };
        g.add(box(4.6, 0.18, 2.0, black, 0, 0.6, 0));
        g.add(box(4.4, 1.55, 2.0, body, 0, 1.45, 0));
        g.add(box(4.42, 0.22, 2.02, trim, 0, 1.2, 0));
        // a gently arched roof: the top half of a flattened barrel lying along the camper, a touch longer than the body so it overhangs
        var roof = new THREE.Mesh(new THREE.CylinderGeometry(1.0, 1.0, 4.5, 24, 1, false, 0, Math.PI), cream);
        roof.rotation.set(0, 0, Math.PI / 2);
        roof.scale.set(0.38, 1, 1.03);
        roof.position.set(0, 2.2, 0);
        roof.castShadow = true;
        g.add(roof);
        // chimney
        g.add(box(0.25, 0.5, 0.25, black, -1.4, 2.7, -0.4));
        // windows on both long sides, glowing warmly
        foreach (var side in new[] { 1, -1 })
        {
            foreach (var x in new[] { -1.3, 0.2 })
            {
                var mat = new THREE.MeshStandardMaterial { color = 0xffe3a8, emissive = 0xffb84d, emissiveIntensity = 0.35, roughness = 0.3 };
                windows.Add(mat);
                g.add(box(0.95, 0.65, 0.04, mat, x, 1.7, side * 1.02));
                g.add(box(1.05, 0.75, 0.03, trim, x, 1.7, side * 1.0));
            }
        }
        // door on the fire side
        g.add(box(0.8, 1.35, 0.05, new THREE.MeshStandardMaterial { color = 0x7a5538, roughness = 0.8 }, 1.65, 1.35, 1.03));
        g.add(box(0.06, 0.06, 0.07, cream, 1.4, 1.3, 1.08));
        // striped awning with poles, flush with the end of the camper
        THREE.Mesh stripe(int i) => box(0.3, 0.05, 1.3, i % 2 != 0 ? cream : trim, 0.55 + i * 0.3, 2.15 - i * 0.0, 1.65);
        for (int i = 0; i < 6; i++)
        {
            var s = stripe(i);
            s.rotation.x = 0.22;
            g.add(s);
        }
        foreach (var x in new[] { 0.45, 2.15 }) g.add(box(0.06, 1.7, 0.06, wood(0x4a3523), x, 1.1, 2.25));
        // string lights along the awning edge
        for (int i = 0; i < 7; i++)
        {
            var bulb = new THREE.Mesh(new THREE.SphereGeometry(0.06, 8, 6), new THREE.MeshStandardMaterial { color = 0xfff0b0, emissive = 0xffc860, emissiveIntensity = 1.2 });
            bulb.position.set(0.45 + i * 0.28, 1.9 - Math.Sin(i / 6.0 * Math.PI) * -0.0, 2.3);
            g.add(bulb);
        }
        // wheels (both sides) and a hitch with a jockey wheel
        foreach (var side in new[] { 1, -1 })
        {
            var tire = cyl(0.48, 0.48, 0.26, black, 20);
            tire.rotation.x = Math.PI / 2;
            tire.position.set(0.4, 0.48, side * 1.12);
            var hub = cyl(0.2, 0.2, 0.28, new THREE.MeshStandardMaterial { color = 0xc9ced3, metalness = 0.8, roughness = 0.3 }, 12);
            hub.rotation.x = Math.PI / 2;
            hub.position.copy(tire.position);
            g.add(tire, hub);
            g.add(box(1.0, 0.05, 0.3, trim, 0.4, 0.98, side * 1.12));
        }
        g.add(box(1.8, 0.12, 0.14, black, -3.2, 0.55, 0));
        var jockey = cyl(0.12, 0.12, 0.1, black, 10);
        jockey.rotation.x = Math.PI / 2;
        jockey.position.set(-3.7, 0.2, 0);
        g.add(jockey, box(0.05, 0.4, 0.05, black, -3.7, 0.4, 0));
        camperLight.position.set(0.5, 1.7, 2.2);
        g.add(camperLight);

        g.position.set(-9.5, -0.04, 1.5);
        g.rotation.y = 0.5;
        mark(g, "camper");
        group.add(g);
        foreach (var s in new[] { -1.4, 0, 1.4 })
            obstacles.Add(new Solid { x = g.position.x + s * Math.Cos(0.5), z = CAMP_Z + g.position.z - s * Math.Sin(0.5), r = 1.35 });
    }

    private void buildLantern()
    {
        var g = new THREE.Group();
        g.add(box(0.07, 2.0, 0.07, wood(0x4a3523), 0, 1.0, 0));
        g.add(box(0.5, 0.06, 0.06, wood(0x4a3523), 0.22, 1.95, 0));
        var metal = new THREE.MeshStandardMaterial { color = 0x3b3f44, metalness = 0.8, roughness = 0.4 };
        var cap = cyl(0.04, 0.16, 0.1, metal, 8);
        cap.position.set(0.42, 1.78, 0);
        var bse = cyl(0.14, 0.14, 0.05, metal, 8);
        bse.position.set(0.42, 1.38, 0);
        lanternGlass = new THREE.Mesh(
            new THREE.CylinderGeometry(0.13, 0.13, 0.35, 10),
            new THREE.MeshStandardMaterial { color = 0xffe3a8, emissive = 0xffb84d, emissiveIntensity = 0.4, transparent = true, opacity = 0.85 });
        lanternGlass.position.set(0.42, 1.58, 0);
        lanternLight.position.set(0.42, 1.58, 0);
        g.add(cap, bse, lanternGlass, lanternLight);
        g.position.set(2.2, 0, -1.6);
        mark(g, "lantern");
        group.add(g);
    }

    /// <summary>Brings the lantern to life for the evening modes.</summary>
    public void setLantern(bool on)
    {
        lanternLight.intensity = on ? 9 : 0;
        ((THREE.MeshStandardMaterial)lanternGlass.material).emissiveIntensity = on ? 2.2 : 0.4;
    }

    /// <summary>Puts an assembled antique on the shelf above the workbench.</summary>
    public void showTrophy(string setId)
    {
        if (trophies.ContainsKey(setId)) return;
        var gold = new THREE.MeshStandardMaterial { color = 0xd4a843, metalness = 0.9, roughness = 0.28 };
        var t = new THREE.Group();
        if (setId == "astrolabe")
        {
            for (int i = 0; i < 3; i++)
            {
                var ring = new THREE.Mesh(new THREE.TorusGeometry(0.28, 0.018, 8, 40), gold);
                ring.rotation.set(i * Math.PI / 3, i * Math.PI / 5, 0);
                ring.castShadow = true;
                t.add(ring);
            }
            t.add(new THREE.Mesh(new THREE.SphereGeometry(0.07, 12, 10), new THREE.MeshStandardMaterial { color = 0x4aa3d6, metalness = 0.3, roughness = 0.2 }));
            t.position.set(-0.55, 0.35, 0);
        }
        else
        {
            var disc = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.3, 0.05, 28), gold);
            disc.rotation.x = Math.PI / 2;
            var sk = new THREE.Mesh(new THREE.SphereGeometry(0.1, 12, 10), new THREE.MeshStandardMaterial { color = 0xf0e8d0, roughness = 0.7 });
            sk.position.z = 0.04;
            t.add(disc, sk);
            t.position.set(0.55, 0.3, 0);
        }
        trophies[setId] = t;
        shelf.add(t);
    }

    public void update(double dt, double boost, bool lanternOn, double night = 0)
    {
        time += dt;
        double t = time;
        for (int i = 0; i < flames.Count; i++)
        {
            var f = flames[i];
            double s = 1 + Math.Sin(t * (9 + i * 3) + i) * 0.14 + Math.Sin(t * 17 + i * 2) * 0.06;
            f.scale.set(1 + Math.Sin(t * 11 + i) * 0.08, s, 1 + Math.Cos(t * 13 + i) * 0.08);
            f.rotation.y = t * (1.5 + i);
        }
        fireLight.intensity = (14 + Math.Sin(t * 13) * 3 + Math.Sin(t * 7.3) * 2 + JS.Random() * 2) * (0.5 + boost);
        fireLight.position.x = Math.Sin(t * 9) * 0.06;
        setLantern(lanternOn);
        for (int i = 0; i < tikiFlames.Count; i++)
        {
            var f = tikiFlames[i];
            double k = 1 + Math.Sin(t * 13 + i * 2.1) * 0.14 + Math.Sin(t * 7.7 + i) * 0.08;
            f.scale.set(1 / k * 0.95, k, 1 / k * 0.95);
            f.rotation.y = t * 2 + i;
        }
        if (lightsGroup.visible)
            for (int i = 0; i < bulbs.Count; i++) bulbs[i].emissiveIntensity = 0.15 + night * (1.5 + Math.Sin(t * 2.2 + i * 1.7) * 0.35);
        radioLed.emissiveIntensity = radioOn ? 1.2 + Math.Sin(t * 6) * 0.5 : 0;
        bookCover.emissiveIntensity = bookNew ? 0.4 + Math.Sin(t * 3) * 0.25 : 0;
        double flagTarget = mailNew ? 0 : -Math.PI / 2;
        mailFlag.rotation.z += (flagTarget - mailFlag.rotation.z) * Math.Min(1, dt * 5);
        // the hammock rocks gently, a little more when someone is lying in it
        hammockCloth.rotation.x = Math.Sin(t * 0.9) * (hammockOccupied ? 0.05 : 0.018);
        radioAntenna.rotation.z = -0.35 + (radioOn ? Math.Sin(t * 5) * 0.03 : 0);
        camperLight.intensity = lanternOn ? 6 : 0.8;
        foreach (var w in windows) w.emissiveIntensity = lanternOn ? 1.6 : 0.35;

        emberTimer -= dt;
        if (emberTimer <= 0)
        {
            emberTimer = 0.06;
            var p = new THREE.Vector3((JS.Random() - 0.5) * 0.4, 0.8, (JS.Random() - 0.5) * 0.4);
            var v = new THREE.Vector3((JS.Random() - 0.5) * 0.4, 0.8 + JS.Random() * 0.8, (JS.Random() - 0.5) * 0.4);
            embers.emit(p, v, 1.6 + JS.Random(), ember);
        }
        embers.update(dt);
    }
}
