using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/mechanics/ViewModel.ts

/// <summary>
/// First-person hands and tools (parented to the camera) plus an invisible body
/// whose only job is to cast the player's shadow onto the sand.
/// </summary>
public class ViewModel
{
    private static readonly THREE.Vector3 Y = new THREE.Vector3(0, 1, 0);
    private static readonly THREE.MeshStandardMaterial SLEEVE = new THREE.MeshStandardMaterial { color = 0x4f7396, roughness = 0.9 };
    private static readonly THREE.MeshStandardMaterial SKIN = new THREE.MeshStandardMaterial { color = 0xe0b08a, roughness = 0.8 };

    /// <summary>A cylinder stretched between two points, optionally tapering to radius `rb` at `b`.</summary>
    private static THREE.Mesh limb(THREE.Vector3 a, THREE.Vector3 b, double r, THREE.Material mat, double rb = double.NaN)
    {
        if (double.IsNaN(rb)) rb = r;
        var dir = b.clone().sub(a);
        var m = new THREE.Mesh(new THREE.CylinderGeometry(rb, r, dir.length(), 14), mat);
        m.position.copy(a).add(b).multiplyScalar(0.5);
        m.quaternion.setFromUnitVectors(Y, dir.normalize());
        return m;
    }

    private static THREE.Vector3 v(double x, double y, double z) => new THREE.Vector3(x, y, z);

    /// <summary>
    /// A soft-edged solid cut from a side-on outline. The outline's x runs forward (toward -z) and its y is height, so it can be drawn just as
    /// the object looks from the side; `x` centres it left to right and `wide` is its full thickness.
    /// </summary>
    private static THREE.Mesh sideCut(Action<THREE.Shape> draw, double x, double wide, THREE.Material mat, double soft = 0.012)
    {
        var shape = new THREE.Shape();
        draw(shape);
        double depth = wide - soft * 2;
        var geo = new THREE.ExtrudeGeometry(shape, new THREE.ExtrudeGeometryOptions { depth = depth, bevelEnabled = true, bevelSize = soft, bevelThickness = soft, bevelSegments = 3, curveSegments = 10 });
        geo.rotateY(Math.PI / 2);
        geo.translate(x - depth / 2, 0, 0);
        return new THREE.Mesh(geo, mat);
    }

    /// <summary>What an arm is made of: the hand, the forearm, and a wider band over part of the forearm (0 at the wrist, 1 at the elbow).</summary>
    private class ArmStyle
    {
        public THREE.Material hand, fore, band;
        public double from, to;
    }

    /// <summary>A bare forearm with a rolled-up shirt sleeve.</summary>
    private static readonly ArmStyle BARE = new ArmStyle { hand = SKIN, fore = SKIN, band = SLEEVE, from = 0.42, to = 1 };
    /// <summary>A diver's glove and wetsuit, with a yellow cuff at the wrist.</summary>
    private static readonly ArmStyle DIVER = new ArmStyle
    {
        hand = new THREE.MeshStandardMaterial { color = 0x22262b, roughness = 0.8 },
        fore = new THREE.MeshStandardMaterial { color = 0x1f3a4d, roughness = 0.7 },
        band = new THREE.MeshStandardMaterial { color = 0xf2c14e, roughness = 0.6 },
        from = 0.03,
        to = 0.075,
    };

    private const double BINOC_SCALE = 0.72;
    private const double CAMERA_SCALE = 0.5;
    private const double BOW_SCALE = 0.55;
    private const double BOW_HAND_Y = -0.05;
    private static readonly THREE.Vector3 BOW_ELBOW = v(0.85, -1.15, 1.25);

    /// <summary>How a hand takes a handle that runs on from the forearm, as in a handshake: the back of the hand out and up, the fingers under, the thumb over.</summary>
    private class Handshake
    {
        public THREE.Vector3 back;
        public double slant;
    }

    private static readonly Handshake HANDSHAKE = new Handshake { back = v(0.7, 0.7, 0), slant = 0.08 };

    private const double WRIST_R = 0.03;
    private const double ELBOW_R = 0.043;

    /// <summary>child of the camera</summary>
    public readonly THREE.Group group = new THREE.Group();
    /// <summary>lives in the world; invisible to the camera but casts a shadow</summary>
    public readonly THREE.Group proxy = new THREE.Group();

    private readonly THREE.Group root = new THREE.Group();
    private readonly THREE.Group detector = new THREE.Group(), trowel = new THREE.Group(), gun = new THREE.Group(), binoc = new THREE.Group(), bow = new THREE.Group(), photoCam = new THREE.Group();
    private readonly List<THREE.Mesh> bowString = new List<THREE.Mesh>();
    private THREE.Group bowArrow = new THREE.Group();
    private THREE.Mesh bowHand = new THREE.Mesh();
    /// <summary>where the wrist of the drawing hand sits relative to the hand</summary>
    private THREE.Vector3 bowWrist = new THREE.Vector3();
    private THREE.Mesh bowForearm = new THREE.Mesh(), bowSleeve = new THREE.Mesh();
    private bool raised = false;
    private THREE.Mesh flash = new THREE.Mesh();
    /// <summary>sits at the very tip of the barrel; bullets start here</summary>
    public readonly THREE.Object3D muzzle = new THREE.Object3D();
    private double flashT = 0, kickT = 0;
    private readonly THREE.Group proxyGun = new THREE.Group(), proxyDetector = new THREE.Group(), proxyTrowel = new THREE.Group(), proxyHead = new THREE.Group();
    private readonly List<THREE.Mesh> legs = new List<THREE.Mesh>();
    private double bob = 0, moving = 0, swing = 0;
    private int tool = 1;
    private List<THREE.MeshStandardMaterial> detectorGold = new List<THREE.MeshStandardMaterial>();
    private List<THREE.MeshStandardMaterial> trowelSteel = new List<THREE.MeshStandardMaterial>();
    private List<THREE.MeshStandardMaterial> trowelGrips = new List<THREE.MeshStandardMaterial>();
    private List<THREE.Group> trowelModels = new List<THREE.Group>();
    private readonly THREE.Group shovelHand = new THREE.Group();
    private bool trowelGilded = false;
    private int digTier = 0;
    private readonly THREE.Group harpoon = new THREE.Group();
    /// <summary>swimming: the hands drift and sway slowly instead of bobbing with footsteps</summary>
    private bool swim = false;

    public void setSwim(bool on)
    {
        swim = on;
        camArmsBare.visible = !on;
        camArmsDiver.visible = on;
    }

    /// <summary>the camera's two pairs of arms: bare on land, in the wetsuit when diving</summary>
    private readonly THREE.Group camArmsBare = new THREE.Group(), camArmsDiver = new THREE.Group();

    private readonly THREE.Group flag = new THREE.Group(), bucket = new THREE.Group();

    public ViewModel()
    {
        buildDetector();
        buildTrowel();
        buildGun();
        buildBinoculars();
        buildCamera();
        buildBow();
        buildFlag();
        buildBucket();
        buildHarpoon();
        root.add(detector, trowel, gun, binoc, photoCam, bow, flag, bucket, harpoon);
        group.add(root);
        group.traverse(o => { if (o is THREE.Mesh m) m.frustumCulled = false; });
        buildProxy();
        setTool(1);
    }

    /// <summary>
    /// A hand closed on a rod, with its forearm running back to `elbow`. `axis` points out of the thumb side of the fist, so a rod that runs
    /// forward from a low arm takes an axis pointing back at the player. `scale` is the scale of `parent`, so hands stay one size on every tool.
    /// With `shake` the rod points where the thumb points, and the hand closes on it from the top and outside.
    /// </summary>
    private (THREE.Mesh hand, THREE.Vector3 wrist) hold(THREE.Object3D parent, THREE.Vector3 grip, THREE.Vector3 axis, THREE.Vector3 elbow, int side, double r, double scale = 1, ArmStyle style = null, Handshake shake = null)
    {
        style ??= BARE;
        double k = 1 / scale;
        var a = axis.clone().normalize();
        var toElbow = elbow.clone().sub(grip);
        var f = toElbow.addScaledVector(a, -toElbow.dot(a)).normalize().negate();
        var y = f.clone().cross(a);
        if (shake != null)
        {
            y.copy(shake.back).addScaledVector(a, -shake.back.dot(a)).normalize();
            f.crossVectors(a, y);
        }
        double slant = shake?.slant ?? 0;
        var w = Hand.wristAt(r * scale, slant);
        var wrist = grip.clone().addScaledVector(a, w.x * k).addScaledVector(y, side * w.y * k).addScaledVector(f, w.z * k);
        var d = elbow.clone().sub(wrist).normalize();
        var hand = Hand.buildFist(r * scale, v(d.dot(a), side * d.dot(y), d.dot(f)), style.hand, slant);
        hand.position.copy(grip);
        hand.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(a, y, f));
        // a left hand is a right hand mirrored through its palm
        hand.scale.set(k, side * k, k);
        parent.add(hand);
        return (hand, wrist);
    }

    /// <summary>The same, with a fixed forearm.</summary>
    private void armOn(THREE.Object3D parent, THREE.Vector3 grip, THREE.Vector3 axis, THREE.Vector3 elbow, int side, double r, double scale = 1, ArmStyle style = null, Handshake shake = null)
    {
        style ??= BARE;
        var (_, wrist) = hold(parent, grip, axis, elbow, side, r, scale, style, shake);
        double k = 1 / scale;
        double at(double u) => WRIST_R + (ELBOW_R - WRIST_R) * u;
        parent.add(limb(wrist, elbow, WRIST_R * k, style.fore, ELBOW_R * k));
        parent.add(limb(wrist.clone().lerp(elbow, style.from), wrist.clone().lerp(elbow, style.to), (at(style.from) + 0.007) * k, style.band, (at(style.to) + 0.007) * k));
    }

    /// <summary>A diver's spear gun: a slim dark barrel, a moulded grip and trigger, two rubber bands stretched back to a steel spear, and a line reel.</summary>
    private void buildHarpoon()
    {
        var g = harpoon;
        const double X = 0.26;
        var steel = new THREE.MeshStandardMaterial { color = 0xcfd6dc, metalness = 0.7, roughness = 0.32 };
        var black = new THREE.MeshStandardMaterial { color = 0x23272b, roughness = 0.55, metalness = 0.2 };
        var barrelMat = new THREE.MeshStandardMaterial { color = 0x2d3a48, roughness = 0.4, metalness = 0.4 };
        var yellow = new THREE.MeshStandardMaterial { color = 0xf2c14e, roughness = 0.5 };
        var rubber = new THREE.MeshStandardMaterial { color = 0xb5482f, roughness = 0.65 };
        THREE.Mesh along(double r, double r2, double z0, double z1, double y, THREE.Material mat, int seg = 16)
        {
            var m = new THREE.Mesh(new THREE.CylinderGeometry(r, r2, Math.Abs(z1 - z0), seg), mat);
            m.rotation.x = -Math.PI / 2; // the +y end of the cylinder points forward
            m.position.set(X, y, (z0 + z1) / 2);
            return m;
        }
        // the barrel and the blunt muzzle block the bands loop through
        g.add(along(0.026, 0.026, -0.8, -1.32, -0.275, barrelMat));
        g.add(along(0.04, 0.036, -1.32, -1.37, -0.268, black));
        g.add(along(0.042, 0.042, -1.335, -1.35, -0.268, yellow));
        // the receiver: a rounded block the barrel and the grip grow out of
        g.add(sideCut(o =>
        {
            o.moveTo(0.53, -0.25);
            o.lineTo(0.82, -0.25);
            o.quadraticCurveTo(0.845, -0.25, 0.845, -0.275);
            o.quadraticCurveTo(0.845, -0.3, 0.82, -0.3);
            o.lineTo(0.535, -0.3);
            o.quadraticCurveTo(0.51, -0.3, 0.51, -0.275);
            o.quadraticCurveTo(0.51, -0.25, 0.53, -0.25);
        }, X, 0.06, black));
        // The grip, raked back like a pistol's. It is slim and rounded so that it fits inside the closed hand: the fingers close
        // on a rod GRIP_R thick, so no part of the grip may lie further than that from the line the hand closes on (a wider
        // slab, as it used to be, has the fingers passing through it and their tips showing on the far side).
        const double GRIP_R = 0.028, RAKE = 0.3, GRIP_Y = -0.37, GRIP_Z = -0.6026;
        // how far forward the middle of the grip is at a given height, following the rake
        double mid(double y) => -GRIP_Z + (y - GRIP_Y) * Math.Tan(RAKE);
        g.add(sideCut(o =>
        {
            const double half = 0.015; // the soft edge adds 0.01 all round
            o.moveTo(mid(-0.29) + half, -0.29);
            o.lineTo(mid(-0.455) + half, -0.455);
            o.quadraticCurveTo(mid(-0.468) + half * 0.75, -0.468, mid(-0.468), -0.468);
            o.quadraticCurveTo(mid(-0.468) - half * 1.25, -0.468, mid(-0.455) - half, -0.455);
            o.lineTo(mid(-0.29) - half, -0.29);
        }, X, 0.04, black, 0.01));
        // the trigger and its guard, just in front of the grip where the first finger reaches them
        var guard = new THREE.Mesh(new THREE.TorusGeometry(0.052, 0.007, 8, 18, Math.PI * 1.05), black);
        guard.rotation.set(0, Math.PI / 2, Math.PI);
        guard.position.set(X, -0.3, -0.705);
        var trigger = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.045, 0.012), yellow);
        trigger.position.set(X, -0.325, -0.695);
        trigger.rotation.x = -0.25;
        g.add(guard, trigger);
        // the spear lies along the top, with a barbed point and a flopper
        g.add(along(0.0065, 0.0065, -0.5, -1.56, -0.232, steel, 8));
        var point = along(0.0135, 0.0065, -1.61, -1.56, -0.232, steel, 10);
        point.rotation.x = Math.PI / 2;
        g.add(point);
        var tip = new THREE.Mesh(new THREE.ConeGeometry(0.0135, 0.075, 10), steel);
        tip.rotation.x = -Math.PI / 2;
        tip.position.set(X, -0.232, -1.645);
        g.add(tip);
        foreach (var side in new[] { -1, 1 })
        {
            var barb = new THREE.Mesh(new THREE.BoxGeometry(0.004, 0.006, 0.08), steel);
            barb.position.set(X + side * 0.013, -0.232, -1.6);
            barb.rotation.y = -side * 0.3;
            g.add(barb);
            // a rubber band stretched from the muzzle back to a notch in the spear
            var band = new THREE.Mesh(
                new THREE.TubeGeometry(new THREE.CatmullRomCurve3(new[] { v(X + side * 0.036, -0.262, -1.35), v(X + side * 0.024, -0.25, -1.1), v(X + side * 0.008, -0.235, -0.86) }), 16, 0.0065, 8),
                rubber);
            g.add(band);
        }
        // the line reel under the barrel
        var reel = new THREE.Mesh(new THREE.CylinderGeometry(0.038, 0.038, 0.03, 18), yellow);
        reel.rotation.z = Math.PI / 2;
        reel.position.set(X, -0.33, -1.0);
        var reelHub = new THREE.Mesh(new THREE.CylinderGeometry(0.014, 0.014, 0.04, 10), black);
        reelHub.rotation.z = Math.PI / 2;
        reelHub.position.copy(reel.position);
        var reelArm = limb(v(X, -0.3, -1.0), v(X, -0.33, -1.0), 0.008, black);
        g.add(reel, reelHub, reelArm);
        // the hand closes high on the grip, right under the receiver
        armOn(g, v(X, GRIP_Y, GRIP_Z), v(0, Math.Cos(RAKE), -Math.Sin(RAKE)), v(0.56, -0.95, 0.35), 1, GRIP_R, 1, DIVER);
        // turned in toward the aim, so the barrel and spear run across the view
        g.position.set(0.13, 0.04, 0);
        g.rotation.y = 0.17;
        g.visible = false;
    }

    /// <summary>A red pail with a yellow spade sticking out of it.</summary>
    private void buildBucket()
    {
        var g = bucket;
        var red = new THREE.MeshStandardMaterial { color = 0xe5453a, roughness = 0.5 };
        var white = new THREE.MeshStandardMaterial { color = 0xf6efe0, roughness = 0.6 };
        var yellow = new THREE.MeshStandardMaterial { color = 0xf2c14e, roughness = 0.55 };
        var sand = new THREE.MeshStandardMaterial { color = 0xdcc38f, roughness = 1 };
        var pail = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.09, 0.2, 18, 1, true), red);
        pail.material.side = THREE.Side.DoubleSide;
        pail.position.set(0.3, -0.38, -0.55);
        var rim = new THREE.Mesh(new THREE.TorusGeometry(0.12, 0.012, 6, 20), white);
        rim.rotation.x = Math.PI / 2;
        rim.position.set(0.3, -0.28, -0.55);
        var band = new THREE.Mesh(new THREE.CylinderGeometry(0.108, 0.1, 0.035, 18, 1, true), white);
        band.position.set(0.3, -0.38, -0.55);
        var fill = new THREE.Mesh(new THREE.CircleGeometry(0.115, 18), sand);
        fill.rotation.x = -Math.PI / 2;
        fill.position.set(0.3, -0.3, -0.55);
        var bottom = new THREE.Mesh(new THREE.CircleGeometry(0.09, 16), red);
        bottom.rotation.x = Math.PI / 2;
        bottom.position.set(0.3, -0.48, -0.55);
        var handle = new THREE.Mesh(new THREE.TorusGeometry(0.12, 0.008, 6, 18, Math.PI), new THREE.MeshStandardMaterial { color = 0xc9ced3, metalness = 0.7, roughness = 0.4 });
        handle.position.set(0.3, -0.28, -0.55);
        handle.rotation.set(0, 0, 0);
        var stick = new THREE.Mesh(new THREE.CylinderGeometry(0.01, 0.01, 0.36, 8), yellow);
        stick.position.set(0.23, -0.15, -0.58);
        stick.rotation.set(0.1, 0, 0.35);
        var blade = new THREE.Mesh(new THREE.BoxGeometry(0.09, 0.012, 0.11), yellow);
        blade.position.set(0.17, 0.02, -0.6);
        blade.rotation.set(0.2, 0, 0.35);
        g.add(pail, rim, band, fill, bottom, handle, stick, blade);
        // carried by the top of the bail, hanging a little lower in the view
        armOn(g, v(0.33, -0.163, -0.55), v(-1, 0, 0), v(0.66, -0.95, 0.2), 1, 0.008);
        g.position.set(-0.08, -0.06, 0);
        g.visible = false;
    }

    private void buildFlag()
    {
        var g = flag;
        var pole = new THREE.Mesh(new THREE.CylinderGeometry(0.01, 0.012, 0.6, 8), new THREE.MeshStandardMaterial { color = 0xd9c9a3, roughness = 0.8 });
        pole.position.set(0.26, -0.27, -0.55);
        pole.rotation.x = -0.25;
        var shape = new THREE.Shape();
        shape.moveTo(0, 0);
        shape.lineTo(-0.1, -0.025);
        shape.lineTo(0, -0.05);
        shape.closePath();
        var cloth = new THREE.Mesh(new THREE.ShapeGeometry(shape), new THREE.MeshStandardMaterial { color = 0xe63946, roughness = 0.7, side = THREE.Side.DoubleSide });
        cloth.position.set(0.252, 0.015, -0.63);
        g.add(pole, cloth);
        var up = v(0, Math.Cos(0.25), -Math.Sin(0.25));
        armOn(g, pole.position.clone().addScaledVector(up, -0.07), up, v(0.64, -0.95, 0.2), 1, 0.012);
    }

    private void buildDetector()
    {
        var g = detector;
        var rubber = new THREE.MeshStandardMaterial { color = 0x1f2124, roughness = 0.85 };
        var body = new THREE.MeshStandardMaterial { color = 0x3a4048, roughness = 0.45, metalness = 0.5 };
        var steel = new THREE.MeshStandardMaterial { color = 0xcdd3da, roughness = 0.3, metalness = 0.9 };
        var yellow = new THREE.MeshStandardMaterial { color = 0xe6b422, roughness = 0.5 };
        THREE.Mesh tube(THREE.Vector3[] pts, double r, THREE.Material mat) => new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 24, r, 8), mat);

        // upper S-shaft: armrest, hand grip, control box
        g.add(tube(new[] { v(0.34, -0.5, -0.12), v(0.31, -0.34, -0.36), v(0.3, -0.29, -0.55), v(0.26, -0.28, -0.8) }, 0.017, steel));
        // foam grip with ridges
        g.add(limb(v(0.3, -0.3, -0.4), v(0.3, -0.29, -0.6), 0.034, rubber));
        for (int i = 0; i < 4; i++)
        {
            var ring = new THREE.Mesh(new THREE.TorusGeometry(0.035, 0.006, 6, 14), yellow);
            ring.position.set(0.3, -0.295, -0.43 - i * 0.045);
            g.add(ring);
        }
        // padded armrest cradle with a strap
        var cuff = new THREE.Mesh(new THREE.TorusGeometry(0.095, 0.02, 8, 18, Math.PI), rubber);
        cuff.position.set(0.37, -0.5, -0.12);
        cuff.rotation.z = Math.PI;
        var strap = new THREE.Mesh(new THREE.TorusGeometry(0.1, 0.008, 6, 18, Math.PI), yellow);
        strap.position.copy(cuff.position);
        g.add(cuff, strap);

        // control box with a glowing screen, knobs and a signal LED
        detectorGold = new List<THREE.MeshStandardMaterial> { body, steel };
        var box = new THREE.Mesh(new THREE.BoxGeometry(0.15, 0.075, 0.17), body);
        box.position.set(0.25, -0.27, -0.88);
        g.add(box);
        screenMat = new THREE.MeshStandardMaterial { color = 0x0c1a12, emissive = 0x39ff88, emissiveIntensity = 0.25 };
        var screen = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.004, 0.07), screenMat);
        screen.position.set(0.25, -0.23, -0.9);
        g.add(screen);
        foreach (var kx in new[] { 0.21, 0.29 })
        {
            var knob = new THREE.Mesh(new THREE.CylinderGeometry(0.016, 0.016, 0.02, 10), yellow);
            knob.position.set(kx, -0.23, -0.82);
            g.add(knob);
        }
        ledMat = new THREE.MeshStandardMaterial { color = 0x401010, emissive = 0xff3030, emissiveIntensity = 0.1 };
        var led = new THREE.Mesh(new THREE.SphereGeometry(0.011, 8, 6), ledMat);
        led.position.set(0.25, -0.23, -0.95);
        g.add(led);

        // lower shaft with a cam-lock collar, then the search coil
        var neck = v(0.04, -0.52, -1.62);
        g.add(limb(v(0.24, -0.29, -0.96), neck, 0.014, steel));
        var collar = new THREE.Mesh(new THREE.CylinderGeometry(0.022, 0.022, 0.05, 10), yellow);
        collar.position.set(0.15, -0.4, -1.28);
        collar.quaternion.setFromUnitVectors(Y, neck.clone().sub(v(0.24, -0.29, -0.96)).normalize());
        g.add(collar);

        var c = new THREE.Group();
        c.position.set(0.04, -0.57, -1.7);
        c.rotation.x = -0.22;
        var disc = new THREE.Mesh(new THREE.CylinderGeometry(0.27, 0.27, 0.035, 36), rubber);
        disc.scale.set(1.18, 1, 1);
        var rim = new THREE.Mesh(new THREE.TorusGeometry(0.27, 0.012, 8, 36), yellow);
        rim.rotation.x = Math.PI / 2;
        rim.scale.set(1.18, 1, 1);
        rim.position.y = 0.02;
        var hub = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.07, 0.045, 14), body);
        hub.position.y = 0.012;
        var bolt = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.02, 0.05, 6), steel);
        bolt.position.y = 0.02;
        var yoke = new THREE.Mesh(new THREE.BoxGeometry(0.08, 0.05, 0.1), body);
        yoke.position.set(0, 0.04, 0.2);
        c.add(disc, rim, hub, bolt, yoke);
        g.add(c);
        coil = c;
        g.scale.setScalar(0.78);
        g.position.set(0.04, -0.13, -0.26);

        // coil cable looping back to the control box
        g.add(tube(new[] { v(0.14, -0.55, -1.55), v(0.2, -0.5, -1.3), v(0.31, -0.4, -1.05), v(0.3, -0.29, -0.95) }, 0.006, rubber));

        // the right hand on the grip, its forearm lying in the armrest
        armOn(g, v(0.3, -0.295, -0.5), v(0, 0.05, -1), v(0.43, -0.72, 0.3), 1, 0.034, 0.78, BARE, HANDSHAKE);
        // the other hand steadies the shaft from the left, just behind the control box
        armOn(g, v(0.272, -0.284, -0.72), v(0.16, -0.04, 1), v(-0.3, -1.1, 0.05), -1, 0.0175, 0.78);
    }

    private THREE.Group coil = new THREE.Group();
    private THREE.MeshStandardMaterial screenMat = new THREE.MeshStandardMaterial();
    private THREE.MeshStandardMaterial ledMat = new THREE.MeshStandardMaterial();
    private double signal = 0;
    private string signalKind = null;
    private double clock = 0;

    /// <summary>Lets the detector's own screen and LED react to what the coil is sensing.</summary>
    public void setSignal(double strength, string kind)
    {
        signal = strength;
        signalKind = kind;
    }

    /// <summary>A garden trowel: a shaped orange handle, a steel neck that cranks down, and a dished, pointed blade.</summary>
    private void buildTrowel()
    {
        var g = trowel;
        var orange = new THREE.MeshStandardMaterial { color = 0xe0732f, roughness = 0.55 };
        trowelGrips = new List<THREE.MeshStandardMaterial> { orange };
        var steel = new THREE.MeshStandardMaterial { color = 0xd6dbe1, roughness = 0.42, metalness = 0.35, side = THREE.Side.DoubleSide };

        var t0 = new THREE.Group();
        // the handle swells in the palm, narrows, and flares at the butt; lathed along y, then laid along z with the blade toward -z
        var profile = new[] { (0.017, -0.08), (0.0185, -0.06), (0.0215, -0.03), (0.0225, 0.0), (0.0215, 0.03), (0.0195, 0.052), (0.0215, 0.068), (0.024, 0.076), (0.02, 0.084), (0.0, 0.087) };
        var handleGeo = new THREE.LatheGeometry(profile.Select(p => new THREE.Vector2(p.Item1, p.Item2)).ToList(), 18);
        handleGeo.rotateX(Math.PI / 2);
        var handle = new THREE.Mesh(handleGeo, orange);
        var ferrule = new THREE.Mesh(new THREE.CylinderGeometry(0.0185, 0.0185, 0.03, 16), steel);
        ferrule.rotation.x = Math.PI / 2;
        ferrule.position.z = -0.09;
        // the neck drops from the ferrule to the blade, then carries on as the pressed spine, flattening and fading out along the hollow
        var neckCurve = new THREE.CatmullRomCurve3(new[] { v(0, 0, -0.095), v(0, -0.004, -0.112), v(0, -0.03, -0.128), v(0, -0.046, -0.146), v(0, -0.0525, -0.17), v(0, -0.0565, -0.21), v(0, -0.058, -0.26), v(0, -0.058, -0.31) });
        var neckPos = new List<double>();
        var neckIdx = new List<int>();
        const int steps = 48, around = 10;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            var c = neckCurve.getPointAt(t);
            var tan = neckCurve.getTangentAt(t);
            double fade = Math.Min(1, (1 - t) / 0.55);
            double r = 0.0075 * (0.02 + 0.98 * fade * fade * (3 - 2 * fade));
            double up = 1 - 0.45 * Math.Min(1, t / 0.5);
            for (int j = 0; j < around; j++)
            {
                double th = (double)j / around * Math.PI * 2;
                double side = Math.Cos(th) * r * (1 + (1 - fade) * 0.6);
                double outv = Math.Sin(th) * r * up;
                neckPos.Add(side); neckPos.Add(c.y + tan.z * outv); neckPos.Add(c.z - tan.y * outv);
                if (i < steps)
                {
                    int a = i * around + j, b = i * around + (j + 1) % around;
                    neckIdx.AddRange(new[] { a, a + around, b, b, a + around, b + around });
                }
            }
        }
        var neckGeo = new THREE.BufferGeometry();
        neckGeo.setAttribute("position", new THREE.Float32BufferAttribute(neckPos, 3));
        neckGeo.setIndex(neckIdx);
        neckGeo.computeVertexNormals();
        var neck = new THREE.Mesh(neckGeo, steel);
        // the blade is one dished sheet that grows out of the neck: it swells to rounded shoulders, then tapers to a point, straight to the tip.
        // It is brushed steel, darker down the hollow and lighter toward the edges, with a fine rolled rim around it
        const double LEN = 0.23;
        double halfWidth(double u) => 0.064 * (0.14 + 0.86 * Math.Sin(Math.Min(1, u / 0.17) * Math.PI * 0.5)) * Math.Pow(1 - Math.Pow(u, 1.6), 0.9);
        double lift(double u, double s) => -0.058 + 0.012 * Math.Pow(Math.Max(0, 1 - u / 0.17), 2) + 0.024 * (halfWidth(u) / 0.064) * s * s;
        double atZ(double u) => -0.15 - u * LEN;
        var hollow = new THREE.Color(0x87909b);
        var edge = new THREE.Color(0xd9dee3);
        var shade = new THREE.Color();
        var pos = new List<double>();
        var col = new List<double>();
        var idx = new List<int>();
        const int rows = 26, cols = 14;
        for (int i = 0; i <= rows; i++)
        {
            double u = (double)i / rows;
            for (int j = 0; j <= cols; j++)
            {
                double s = (double)j / cols * 2 - 1;
                pos.Add(s * halfWidth(u)); pos.Add(lift(u, s)); pos.Add(atZ(u));
                shade.copy(hollow).lerp(edge, s * s);
                col.Add(shade.r); col.Add(shade.g); col.Add(shade.b);
            }
        }
        for (int i = 0; i < rows; i++)
        for (int j = 0; j < cols; j++)
        {
            int a = i * (cols + 1) + j, b = a + cols + 1;
            idx.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
        }
        var bladeGeo = new THREE.BufferGeometry();
        bladeGeo.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        bladeGeo.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
        bladeGeo.setIndex(idx);
        bladeGeo.computeVertexNormals();
        var bladeMat = new THREE.MeshStandardMaterial { vertexColors = true, roughness = 0.4, metalness = 0.4, side = THREE.Side.DoubleSide };
        trowelSteel = new List<THREE.MeshStandardMaterial> { steel, bladeMat };
        var blade = new THREE.Mesh(bladeGeo, bladeMat);
        var outline = new List<THREE.Vector3>();
        foreach (var side in new[] { -1, 1 })
        for (int i = 0; i <= rows; i++)
        {
            double u = side < 0 ? (double)i / rows : 1 - (double)i / rows;
            outline.Add(v(side * halfWidth(u), lift(u, side), atZ(u)));
        }
        var rim = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(outline, true, "catmullrom", 0.2), 120, 0.002, 6, true), steel);
        var model = new THREE.Group();
        model.add(handle, ferrule, neck, blade, rim);
        t0.add(model);
        trowelModels = new List<THREE.Group> { model, buildScoop(), buildShovel() };
        t0.add(trowelModels[1], trowelModels[2]);
        // held out ahead with the tip turned in toward the middle of the view, rolled a little so the face of the blade shows
        t0.position.set(0.19, -0.26, -0.6);
        t0.rotation.x = -0.04;
        t0.rotation.y = 0.5;
        t0.rotation.z = 0.3;
        t0.updateMatrix();
        g.add(t0);
        armOn(g, v(0, 0, 0.015).applyMatrix4(t0.matrix), v(0, 0, -1).transformDirection(t0.matrix), v(0.56, -0.68, 0.25), 1, 0.021, 1, BARE, HANDSHAKE);
        // the shovel is a two-handed tool: the left hand takes the shaft ahead of the grip
        shovelHand.visible = false;
        g.add(shovelHand);
        armOn(shovelHand, v(0, 0, -0.17).applyMatrix4(t0.matrix), v(0, 0, 1).transformDirection(t0.matrix), v(-0.25, -0.95, 0.2), -1, 0.021);
    }

    /// <summary>Stretches a unit-height cylinder between two points.</summary>
    private void stretch(THREE.Mesh m, THREE.Vector3 a, THREE.Vector3 b)
    {
        var d = b.clone().sub(a);
        m.position.copy(a).add(b).multiplyScalar(0.5);
        m.quaternion.setFromUnitVectors(Y, d.clone().normalize());
        m.scale.y = d.length();
    }

    /// <summary>A short wooden bow with a nocked arrow. The string and the drawing hand follow `setDraw`.</summary>
    private void buildBow()
    {
        var g = bow;
        var wood = new THREE.MeshStandardMaterial { color = 0x7a4a24, roughness = 0.7 };
        var leather = new THREE.MeshStandardMaterial { color = 0x3a2616, roughness = 0.9 };
        var stringMat = new THREE.MeshStandardMaterial { color = 0xf1ead6, roughness = 0.9 };
        // limbs: tips curve back toward the archer (+z), the grip bulges forward
        var curve = new THREE.CatmullRomCurve3(new[] { v(0, 0.46, 0.15), v(0, 0.28, -0.02), v(0, 0, -0.07), v(0, -0.28, -0.02), v(0, -0.46, 0.15) });
        var limbs = new THREE.Mesh(new THREE.TubeGeometry(curve, 28, 0.017, 8), wood) { castShadow = true };
        var grip = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.15, 10), leather);
        grip.position.set(0, 0, -0.07);
        g.add(limbs, grip);
        for (int i = 0; i < 2; i++)
        {
            var s = new THREE.Mesh(new THREE.CylinderGeometry(0.003, 0.003, 1, 4), stringMat);
            bowString.Add(s);
            g.add(s);
        }
        bowArrow = Arrows.buildArrow();
        bowArrow.rotation.y = Math.PI; // tip forward (-z)
        g.add(bowArrow);
        // the left hand holds the grip, the right hooks the string; its forearm follows the draw
        armOn(g, v(0, -0.01, -0.07), v(0, 1, 0), v(-0.8, -1.2, 1.1), -1, 0.03, BOW_SCALE);
        var start = v(0, BOW_HAND_Y, 0.15);
        var right = hold(g, start, v(0, 1, 0), BOW_ELBOW, 1, 0.006, BOW_SCALE);
        bowHand = right.hand;
        bowWrist = right.wrist.sub(start);
        double k = 1 / BOW_SCALE;
        bowForearm = new THREE.Mesh(new THREE.CylinderGeometry(ELBOW_R * k, WRIST_R * k, 1, 14), SKIN);
        bowSleeve = new THREE.Mesh(new THREE.CylinderGeometry((ELBOW_R + 0.007) * k, (ELBOW_R + 0.001) * k, 1, 14), SLEEVE);
        g.add(bowForearm, bowSleeve);
        g.rotation.set(0.0, -0.12, -0.12);
        g.scale.setScalar(BOW_SCALE);
        g.visible = false;
        setDraw(0);
    }

    /// <summary>0 relaxed .. 1 fully drawn.</summary>
    public void setDraw(double d)
    {
        var nock = v(0, 0, 0.15 + d * 0.3);
        stretch(bowString[0], v(0, 0.46, 0.15), nock);
        stretch(bowString[1], v(0, -0.46, 0.15), nock);
        bowArrow.position.set(0, 0, nock.z - 0.38);
        bowHand.position.set(0, BOW_HAND_Y, nock.z);
        var wrist = bowHand.position.clone().add(bowWrist);
        stretch(bowForearm, wrist, BOW_ELBOW);
        stretch(bowSleeve, wrist.clone().lerp(BOW_ELBOW, 0.42), BOW_ELBOW);
        // drawing the bow pulls it a little toward the shooter and lifts it
        bow.position.set(-0.16, -0.27 + d * 0.01, -0.95 + d * 0.04);
    }

    /// <summary>The kick of letting the string go.</summary>
    public void releaseBow() => kickT = 0.6;

    /// <summary>Binoculars held in both hands, resting in front of the chest until they are raised.</summary>
    private void buildBinoculars()
    {
        var g = binoc;
        var body = new THREE.MeshStandardMaterial { color = 0x2b3430, roughness = 0.6, metalness = 0.3 };
        var metal = new THREE.MeshStandardMaterial { color = 0x15181a, roughness = 0.35, metalness = 0.8 };
        var glass = new THREE.MeshPhysicalMaterial { color = 0x6aa8d8, roughness = 0.05, metalness = 0.2, clearcoat = 1, transparent = true, opacity = 0.85 };
        foreach (var sx in new[] { -1, 1 })
        {
            double x = sx * 0.085;
            var barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.062, 0.058, 0.32, 14), body);
            barrel.rotation.x = Math.PI / 2;
            barrel.position.set(x, -0.2, -0.58);
            var lens = new THREE.Mesh(new THREE.CylinderGeometry(0.072, 0.072, 0.07, 14), metal);
            lens.rotation.x = Math.PI / 2;
            lens.position.set(x, -0.2, -0.76);
            var glassDisc = new THREE.Mesh(new THREE.CylinderGeometry(0.058, 0.058, 0.01, 14), glass);
            glassDisc.rotation.x = Math.PI / 2;
            glassDisc.position.set(x, -0.2, -0.797);
            var eye = new THREE.Mesh(new THREE.CylinderGeometry(0.046, 0.05, 0.07, 12), metal);
            eye.rotation.x = Math.PI / 2;
            eye.position.set(x, -0.2, -0.4);
            g.add(barrel, lens, glassDisc, eye);
            // a hand around each barrel from underneath
            armOn(g, v(x, -0.2, -0.56), v(0, 0, 1), v(sx * 0.8, -1.35, 0.2), sx < 0 ? -1 : 1, 0.06, BINOC_SCALE);
        }
        var bridge = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.045, 0.2), body);
        bridge.position.set(0, -0.2, -0.58);
        var wheel = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.045, 10), metal);
        wheel.position.set(0, -0.172, -0.6);
        g.add(bridge, wheel);
        g.rotation.x = 0.12;
        g.scale.setScalar(BINOC_SCALE);
        g.position.set(0, -0.16, -0.16);
        g.visible = false;
    }

    /// <summary>
    /// A hand holding one end of the camera (see <see cref="Hand.buildCameraGrip"/>), with its forearm running back to `elbow`.
    /// `end` is the middle of that end of the camera, `halfDepth` half the camera's thickness and `top` how far its top is above
    /// `end`, all in the camera group's own units. The right hand (`side` 1) has its first finger on the shutter button.
    /// </summary>
    private void cameraArm(THREE.Object3D parent, THREE.Vector3 end, int side, THREE.Vector3 elbow, double halfDepth, double top, ArmStyle style)
    {
        const double scale = CAMERA_SCALE;
        double k = 1 / scale;
        var w = Hand.cameraWrist;
        var wrist = end.clone().add(v(side * w.x * k, w.y * k, w.z * k));
        var d = elbow.clone().sub(wrist).normalize();
        var hand = Hand.buildCameraGrip(halfDepth * scale, top * scale, side > 0, v(side * d.x, d.y, d.z), style.hand);
        hand.position.copy(end);
        // a left hand is a right hand mirrored from side to side
        hand.scale.set(side * k, k, k);
        parent.add(hand);
        double at(double u) => WRIST_R + (ELBOW_R - WRIST_R) * u;
        parent.add(limb(wrist, elbow, WRIST_R * k, style.fore, ELBOW_R * k));
        parent.add(limb(wrist.clone().lerp(elbow, style.from), wrist.clone().lerp(elbow, style.to), (at(style.from) + 0.007) * k, style.band, (at(style.to) + 0.007) * k));
    }

    /// <summary>A little retro camera held in both hands in front of the chest: a silver top, black leatherette, a round lens and a flash window.</summary>
    private void buildCamera()
    {
        var g = photoCam;
        var leather = new THREE.MeshStandardMaterial { color = 0x26282c, roughness = 0.85 };
        var silver = new THREE.MeshStandardMaterial { color = 0xc9ced4, roughness = 0.3, metalness = 0.75 };
        var black = new THREE.MeshStandardMaterial { color = 0x121416, roughness = 0.4, metalness = 0.5 };
        var glass = new THREE.MeshPhysicalMaterial { color = 0x5b86b0, roughness = 0.05, metalness = 0.3, clearcoat = 1 };
        var flashMat = new THREE.MeshStandardMaterial { color = 0xf4f1e8, roughness = 0.2, emissive = 0xffffff, emissiveIntensity = 0.15 };
        var red = new THREE.MeshStandardMaterial { color = 0xc8362a, roughness = 0.5 };
        const double Y = -0.2, Z = -0.6;
        // body: leatherette below, a silver plate on top
        var body = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.13, 0.085), leather);
        body.position.set(0, Y - 0.02, Z);
        var top = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.05, 0.085), silver);
        top.position.set(0, Y + 0.07, Z);
        // The rounded ends of the body, where the hands hold it. A camera is held by its two ends, not from underneath like a
        // binocular barrel: the palm against the end, the fingers flat across the front, the thumb flat across the back and the
        // right hand's first finger on the shutter button. The forearms come in from behind and below.
        foreach (var sx in new[] { -1, 1 })
        {
            var end = new THREE.Mesh(new THREE.CylinderGeometry(0.0425, 0.0425, 0.13, 16), leather);
            end.position.set(sx * 0.15, Y - 0.02, Z);
            var cap = new THREE.Mesh(new THREE.CylinderGeometry(0.0425, 0.0425, 0.05, 16), silver);
            cap.position.set(sx * 0.15, Y + 0.07, Z);
            g.add(end, cap);
            // the middle of the end, halfway up the body
            var grip = v(sx * 0.15, Y + 0.005, Z);
            var elbow = v(sx * 0.78, -1.05, 0.75);
            cameraArm(camArmsBare, grip, sx, elbow, 0.0425, 0.09, BARE);
            cameraArm(camArmsDiver, grip, sx, elbow, 0.0425, 0.09, DIVER);
        }
        // the lens, pointing away from the player
        var barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.062, 0.066, 0.07, 20), black);
        barrel.rotation.x = Math.PI / 2;
        barrel.position.set(0.02, Y - 0.01, Z - 0.075);
        var ring = new THREE.Mesh(new THREE.CylinderGeometry(0.068, 0.068, 0.02, 20), silver);
        ring.rotation.x = Math.PI / 2;
        ring.position.set(0.02, Y - 0.01, Z - 0.1);
        var lens = new THREE.Mesh(new THREE.CylinderGeometry(0.046, 0.046, 0.008, 20), glass);
        lens.rotation.x = Math.PI / 2;
        lens.position.set(0.02, Y - 0.01, Z - 0.111);
        // the flash window, the viewfinder and the shutter button
        var flashWin = new THREE.Mesh(new THREE.BoxGeometry(0.07, 0.03, 0.01), flashMat);
        flashWin.position.set(-0.1, Y + 0.07, Z - 0.045);
        var finder = new THREE.Mesh(new THREE.BoxGeometry(0.045, 0.03, 0.012), black);
        finder.position.set(0.09, Y + 0.07, Z + 0.045);
        var button = new THREE.Mesh(new THREE.CylinderGeometry(0.014, 0.014, 0.014, 12), red);
        button.position.set(0.11, Y + 0.1, Z);
        var dial = new THREE.Mesh(new THREE.CylinderGeometry(0.022, 0.022, 0.016, 16), silver);
        dial.position.set(-0.06, Y + 0.103, Z);
        camArmsDiver.visible = false;
        g.add(body, top, barrel, ring, lens, flashWin, finder, button, dial, camArmsBare, camArmsDiver);
        // smaller and lower than the binoculars, so the camera rests below the middle of the view
        g.rotation.x = 0.3;
        g.scale.setScalar(CAMERA_SCALE);
        g.position.set(0.04, -0.29, -0.34);
        g.visible = false;
    }

    /// <summary>The gold finishes from the Luxury shop.</summary>
    public void setGolden(bool det, bool trw)
    {
        const int gold = 0xf0c24a;
        if (det) foreach (var m in detectorGold) m.color.set(gold);
        trowelGilded = trw;
        if (trw)
        {
            foreach (var m in trowelSteel) m.color.set(gold);
            foreach (var m in trowelGrips) m.color.set(0x4a2c17);
        }
    }

    /// <summary>The digging upgrade changes the tool in hand: 1 the garden trowel, 2 a deep sand scoop, 3 a heavy-duty metal beach shovel.</summary>
    public void setDigTier(int tier)
    {
        if (tier == digTier) return;
        digTier = tier;
        int which = Math.Min(2, Math.Max(0, tier - 1));
        for (int i = 0; i < trowelModels.Count; i++) trowelModels[i].visible = i == which;
        shovelHand.visible = which == 2;
    }

    /// <summary>A honeycomb of holes as an alpha map (white solid, black hole), for the perforated sifting scoop.</summary>
    private THREE.CanvasTexture hexHoles()
    {
        var c = new HTMLCanvasElement(76, 132);
        var x = c.getContext("2d");
        x.fillStyle = "#fff";
        x.fillRect(0, 0, c.width, c.height);
        x.fillStyle = "#000";
        for (int row = 0; row <= 4; row++)
        for (int col = 0; col <= 2; col++)
        {
            double cx = col * 38 + (row % 2 == 1 ? 19 : 0), cy = row * 33;
            x.beginPath();
            for (int k = 0; k < 6; k++)
            {
                double a = Math.PI / 3 * k + Math.PI / 6;
                x.lineTo(cx + Math.Cos(a) * 16, cy + Math.Sin(a) * 16);
            }
            x.fill();
        }
        var t = new THREE.CanvasTexture(c);
        t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
        return t;
    }

    /// <summary>A stainless sand-sifting scoop: a honeycomb-perforated half-pipe with a closed top plate, and a foam grip on a post.</summary>
    private THREE.Group buildScoop()
    {
        var g = new THREE.Group();
        var steel = new THREE.MeshStandardMaterial { color = 0xe6eaee, roughness = 0.3, metalness = 0.4, side = THREE.Side.DoubleSide };
        var holes = hexHoles();
        var mesh = (THREE.MeshStandardMaterial)steel.clone();
        mesh.alphaMap = holes;
        mesh.alphaTest = 0.5;
        var foam = new THREE.MeshStandardMaterial { color = 0x1b1d20, roughness = 0.85 };
        trowelGrips.Add(foam);
        trowelSteel.Add(steel);
        trowelSteel.Add(mesh);
        const double R = 0.075, len = 0.27, z0 = -0.07;
        // the shell: the lower half of a cylinder lying along z. Its digging edge is cut in a sweeping curve, so the walls end near the
        // top plate and the bottom runs out furthest, like the real thing
        double frontAt(double th) => z0 - len * (0.45 + 0.55 * Math.Pow(Math.Max(0, Math.Cos(th)), 0.6));
        const int N = 40, M = 12;
        var pos = new List<double>();
        var uv = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i <= N; i++)
        {
            double th = -Math.PI / 2 + Math.PI * i / N;
            double zf = frontAt(th);
            for (int j = 0; j <= M; j++)
            {
                double z = z0 + (zf - z0) * j / M;
                pos.Add(Math.Sin(th) * R); pos.Add(-Math.Cos(th) * R); pos.Add(z);
                uv.Add((th + Math.PI / 2) / Math.PI); uv.Add((z0 - z) / len);
            }
        }
        for (int i = 0; i < N; i++)
        for (int j = 0; j < M; j++)
        {
            int a = i * (M + 1) + j, b = a + M + 1;
            idx.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
        }
        var shellGeo = new THREE.BufferGeometry();
        shellGeo.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        shellGeo.setAttribute("uv", new THREE.Float32BufferAttribute(uv, 2));
        shellGeo.setIndex(idx);
        shellGeo.computeVertexNormals();
        holes.repeat.set(3, 3.5);
        var shell = new THREE.Mesh(shellGeo, mesh);
        shell.position.set(0, -R, 0);
        // a rolled steel rim along the cut edge
        var edge = new List<THREE.Vector3>();
        for (int i = 0; i <= N; i++)
        {
            double th = -Math.PI / 2 + Math.PI * i / N;
            edge.Add(v(Math.Sin(th) * R, -Math.Cos(th) * R, frontAt(th)));
        }
        var rim = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(edge), 60, 0.0028, 6), steel);
        rim.position.set(0, -R, 0);
        var capGeo = new THREE.CircleGeometry(R, 24, Math.PI, Math.PI);
        var cap = new THREE.Mesh(capGeo, mesh);
        cap.position.set(0, -R, z0);
        // the solid top plate over the back of the scoop, with a socket tube rising from it
        var plate = new THREE.Mesh(new THREE.BoxGeometry(R * 2, 0.004, 0.12), steel);
        plate.position.set(0, -R, z0 - 0.06);
        var post = new THREE.Mesh(new THREE.CylinderGeometry(0.011, 0.011, 0.075, 14), steel);
        post.position.set(0, -R / 2 - 0.004, -0.1);
        post.rotation.x = 0.35;
        var gripGeo = new THREE.CylinderGeometry(0.021, 0.021, 0.17, 16);
        gripGeo.rotateX(Math.PI / 2);
        var grip = new THREE.Mesh(gripGeo, foam);
        grip.position.set(0, -0.004, 0.025);
        g.add(shell, rim, cap, plate, post, grip);
        g.rotation.x = 0.1;
        g.visible = false;
        return g;
    }

    /// <summary>A heavy-duty metal beach shovel: a long steel shaft, a D-grip, a ribbed blade with step plates.</summary>
    private THREE.Group buildShovel()
    {
        var g = new THREE.Group();
        var steel = new THREE.MeshStandardMaterial { color = 0xc3cad2, roughness = 0.4, metalness = 0.35, side = THREE.Side.DoubleSide };
        var grip = new THREE.MeshStandardMaterial { color = 0x222a30, roughness = 0.7 };
        trowelSteel.Add(steel);
        trowelGrips.Add(grip);
        var shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.014, 0.014, 0.52, 12), steel);
        shaft.rotation.x = Math.PI / 2;
        shaft.position.z = -0.12;
        // a T-handle across the end of the shaft, clear of the right forearm, with the foam sleeve running right up to it
        var barGeo = new THREE.CylinderGeometry(0.014, 0.014, 0.12, 14);
        barGeo.rotateZ(Math.PI / 2);
        var dGrip = new THREE.Mesh(barGeo, grip);
        dGrip.position.z = 0.14;
        var sleeveGeo = new THREE.CylinderGeometry(0.021, 0.021, 0.2, 14);
        sleeveGeo.rotateX(Math.PI / 2);
        var sleeve = new THREE.Mesh(sleeveGeo, grip);
        sleeve.position.z = 0.04;
        // the blade: a rounded-shoulder spade shape, thin and slightly dished, lowered toward the tip
        const double w = 0.115;
        var sh = new THREE.Shape();
        sh.moveTo(-w * 0.6, 0);
        sh.lineTo(w * 0.6, 0);
        sh.lineTo(w, -0.07);
        sh.quadraticCurveTo(w * 1.02, -0.2, 0, -0.3);
        sh.quadraticCurveTo(-w * 1.02, -0.2, -w, -0.07);
        sh.closePath();
        var bladeGeo = new THREE.ExtrudeGeometry(sh, new THREE.ExtrudeGeometryOptions { depth = 0.008, bevelEnabled = false });
        bladeGeo.rotateX(Math.PI / 2);
        var blade = new THREE.Mesh(bladeGeo, steel);
        var rib = new THREE.Mesh(new THREE.BoxGeometry(0.014, 0.012, 0.2), steel);
        rib.position.set(0, 0.008, -0.12);
        var socket = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.026, 0.1, 12), steel);
        socket.rotation.x = Math.PI / 2;
        socket.position.z = -0.01;
        var bladeG = new THREE.Group();
        bladeG.add(blade, rib, socket);
        foreach (var sx in new[] { -1, 1 })
        {
            var step = new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.006, 0.05), steel);
            step.position.set(sx * 0.05, 0.003, -0.06);
            bladeG.add(step);
        }
        bladeG.position.z = -0.38;
        bladeG.rotation.x = 0.22;
        g.add(shaft, dGrip, sleeve, bladeG);
        g.position.z = 0.04;
        g.visible = false;
        return g;
    }

    /// <summary>Hides the hand-held model while the player is looking through the binoculars or the camera.</summary>
    public void setRaised(bool r)
    {
        raised = r;
        group.visible = tool != 0 && !r;
    }

    /// <summary>A chunky plastic toy pistol: a rounded red body, a blue grip, a yellow barrel tip so nobody mistakes it, and a little hammer.</summary>
    private void buildGun()
    {
        var g = gun;
        const double X = 0.26;
        var red = new THREE.MeshStandardMaterial { color = 0xdf4a35, roughness = 0.38 };
        var blue = new THREE.MeshStandardMaterial { color = 0x2f79c9, roughness = 0.45 };
        var yellow = new THREE.MeshStandardMaterial { color = 0xf7c948, roughness = 0.42 };
        var dark = new THREE.MeshStandardMaterial { color = 0x2b2f35, roughness = 0.5 };
        var body = sideCut(o =>
        {
            o.moveTo(0.44, -0.232);
            o.lineTo(0.85, -0.232);
            o.quadraticCurveTo(0.885, -0.232, 0.885, -0.262);
            o.lineTo(0.885, -0.282);
            o.quadraticCurveTo(0.885, -0.31, 0.855, -0.31);
            o.lineTo(0.47, -0.31);
            o.quadraticCurveTo(0.41, -0.31, 0.41, -0.268);
            o.quadraticCurveTo(0.41, -0.232, 0.44, -0.232);
        }, X, 0.082, red, 0.018);
        // the grip rakes back toward the shooter's hand
        var handle = sideCut(o =>
        {
            o.moveTo(0.585, -0.3);
            o.lineTo(0.525, -0.465);
            o.quadraticCurveTo(0.512, -0.495, 0.485, -0.495);
            o.lineTo(0.44, -0.495);
            o.quadraticCurveTo(0.41, -0.495, 0.42, -0.465);
            o.lineTo(0.465, -0.3);
            o.closePath();
        }, X, 0.066, blue, 0.016);
        var barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.031, 0.031, 0.075, 18), red);
        barrel.rotation.x = Math.PI / 2;
        barrel.position.set(X, -0.27, -0.91);
        var tip = new THREE.Mesh(new THREE.CylinderGeometry(0.037, 0.037, 0.03, 18), yellow);
        tip.rotation.x = Math.PI / 2;
        tip.position.set(X, -0.27, -0.95);
        var bore = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.018, 0.005, 14), dark);
        bore.rotation.x = Math.PI / 2;
        bore.position.set(X, -0.27, -0.967);
        var stripe = sideCut(o =>
        {
            o.moveTo(0.46, -0.262);
            o.lineTo(0.84, -0.262);
            o.lineTo(0.84, -0.274);
            o.lineTo(0.46, -0.274);
            o.closePath();
        }, X, 0.086, yellow, 0.001);
        var guard = new THREE.Mesh(new THREE.TorusGeometry(0.052, 0.009, 8, 18, Math.PI * 1.05), red);
        guard.rotation.set(0, Math.PI / 2, Math.PI);
        guard.position.set(X, -0.305, -0.645);
        var trigger = new THREE.Mesh(new THREE.BoxGeometry(0.014, 0.05, 0.016), yellow);
        trigger.position.set(X, -0.332, -0.635);
        trigger.rotation.x = -0.25;
        var hammer = new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.042, 0.03), dark);
        hammer.position.set(X, -0.225, -0.43);
        hammer.rotation.x = 0.4;
        var sight = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.022, 0.03), yellow);
        sight.position.set(X, -0.222, -0.84);
        g.add(body, handle, barrel, tip, bore, stripe, guard, trigger, hammer, sight);
        armOn(g, v(X, -0.39, -0.48), v(0, Math.Cos(0.3), -Math.Sin(0.3)), v(0.62, -1.05, 0.3), 1, 0.03, 0.72);
        flash = new THREE.Mesh(new THREE.ConeGeometry(0.06, 0.2, 8),
            new THREE.MeshBasicMaterial { color = 0xffd27a, transparent = true, opacity = 0.9, blending = THREE.Blending.AdditiveBlending, depthWrite = false });
        flash.rotation.x = -Math.PI / 2;
        flash.position.set(0.26, -0.27, -1.0);
        flash.visible = false;
        g.add(flash);
        muzzle.position.set(0.26, -0.27, -0.96);
        g.add(muzzle);
        g.scale.setScalar(0.72);
        // held out to the right and turned in toward the aim, so its side shows rather than just its back
        g.position.set(0.16, -0.03, -0.12);
        g.rotation.y = 0.28;
    }

    /// <summary>Recoil and muzzle flash.</summary>
    public void fire()
    {
        kickT = 1;
        flashT = 0.06;
    }

    private void buildProxy()
    {
        var mat = new THREE.MeshBasicMaterial { colorWrite = false, depthWrite = false };
        THREE.Mesh part(THREE.BufferGeometry geo, THREE.Vector3 p, THREE.Object3D parent = null)
        {
            var m = new THREE.Mesh(geo, mat);
            m.position.copy(p);
            m.castShadow = true;
            (parent ?? proxy).add(m);
            return m;
        }
        part(new THREE.CapsuleGeometry(0.2, 0.55, 4, 8), v(0, 1.15, 0));
        foreach (var s in new[] { -1, 1 }) legs.Add(part(new THREE.CapsuleGeometry(0.085, 0.6, 4, 8), v(s * 0.1, 0.4, 0)));
        proxy.add(proxyHead);
        proxyHead.position.set(0, 1.58, 0);
        part(new THREE.SphereGeometry(0.13, 10, 8), v(0, 0.08, 0), proxyHead);
        // arms reaching forward to the held tool
        foreach (var s in new[] { -1, 1 })
        {
            var a = limb(v(s * 0.26, 1.35, 0), v(s * 0.2, 1.1, -0.45), 0.065, mat);
            a.castShadow = true;
            proxy.add(a);
        }
        var stick = limb(v(0.2, 1.1, -0.45), v(0.05, 0.12, -1.35), 0.025, mat);
        stick.castShadow = true;
        var disc = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.3, 0.04, 20), mat);
        disc.position.set(0.05, 0.12, -1.35);
        disc.castShadow = true;
        proxyDetector.add(stick, disc);
        var stick2 = limb(v(0.2, 1.1, -0.45), v(0.12, 0.55, -1.0), 0.025, mat);
        stick2.castShadow = true;
        var scoop = new THREE.Mesh(new THREE.BoxGeometry(0.11, 0.012, 0.3), mat);
        scoop.position.set(0.12, 0.45, -1.1);
        scoop.rotation.x = -0.5;
        scoop.castShadow = true;
        proxyTrowel.add(stick2, scoop);
        var pistol = limb(v(0.2, 1.1, -0.45), v(0.2, 1.14, -0.9), 0.04, mat);
        pistol.castShadow = true;
        proxyGun.add(pistol);
        proxy.add(proxyDetector, proxyTrowel, proxyGun);
    }

    /// <summary>0 hides both tools.</summary>
    public void setTool(int n)
    {
        tool = n;
        detector.visible = proxyDetector.visible = n == 1;
        trowel.visible = proxyTrowel.visible = n == 2;
        gun.visible = proxyGun.visible = n == 4;
        binoc.visible = n == 5;
        photoCam.visible = n == 3;
        bow.visible = n == 6;
        flag.visible = n == 7;
        bucket.visible = n == 8;
        harpoon.visible = n == 9;
        group.visible = n != 0 && !((n == 5 || n == 3) && raised);
    }

    public void doSwing() => swing = 1;

    /// <summary>Places the shadow body at the player's feet.</summary>
    public void update(double dt, bool isMoving, THREE.Vector3 feet, double yaw, double pitch)
    {
        moving += ((isMoving ? 1 : 0) - moving) * Math.Min(1, dt * 8);
        if (swim) bob += dt * (1.6 + moving * 1.4);
        else if (isMoving) bob += dt * 9;
        clock += dt;
        swing = Math.Max(0, swing - dt * 3.5);
        if (tool == 1)
        {
            // the coil drifts gently from side to side, like a slow sweep
            detector.rotation.y = Math.Sin(clock * 1.1) * 0.05;
            coil.rotation.z = Math.Sin(clock * 1.1 + 1) * 0.04;
            double pulse = signal > 0 ? 0.5 + 0.5 * Math.Sin(clock * (6 + signal * 22)) : 0;
            ledMat.emissiveIntensity = 0.1 + pulse * (0.6 + signal * 1.6);
            ledMat.emissive.set(signalKind == "trash" ? 0xff9a3c : signalKind == "coin" ? 0x7aff7a : signalKind == "deep" ? 0xc79bff : 0xff3030);
            screenMat.emissiveIntensity = 0.25 + signal * 1.4;
            screenMat.emissive.set(signalKind == "trash" ? 0xff9a3c : signalKind == "deep" ? 0xc79bff : 0x39ff88);
        }
        kickT = Math.Max(0, kickT - dt * 7);
        flashT = Math.Max(0, flashT - dt);
        flash.visible = flashT > 0;
        double s = Math.Sin(swing * Math.PI);
        // underwater the hands always drift a little, and sway wider and slower when kicking
        double sw = swim ? 0.5 + moving : moving;
        double amp = swim ? 2.6 : 1;
        // the trowel stabs forward and a little down, so it stays in view through the dig
        bool dig = tool == 2;
        root.position.set(Math.Cos(bob * 0.5) * 0.012 * sw * amp, 0.2 + Math.Sin(bob) * 0.014 * sw * amp - s * (dig ? 0.03 : 0.06), 0.1 + s * (dig ? -0.08 : 0.1));
        root.rotation.x = dig ? -s * 0.12 : tool == 4 || tool == 9 ? kickT * 0.22 : Math.Sin(bob * 0.5) * 0.01 * sw * amp;
        if (tool == 4 || tool == 9) root.position.z += kickT * 0.08;
        root.rotation.z = Math.Cos(bob * 0.5) * 0.01 * sw * amp;

        proxy.position.copy(feet);
        proxy.rotation.y = yaw;
        proxyHead.rotation.x = pitch;
        double stride = Math.Sin(bob) * 0.45 * moving;
        legs[0].rotation.x = stride;
        legs[1].rotation.x = -stride;
        proxyTrowel.rotation.x = -s * 0.5;
    }
}
