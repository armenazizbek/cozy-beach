using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/world/Boat.ts

/// <summary>A small motorboat the player can climb into and drive across the sea.</summary>
public class Boat
{
    private const double MAX_FORWARD = 14;
    private const double MAX_REVERSE = 4;

    // The hull is described along its length by t, 0 at the stem and 1 at the transom, and around each cross-section by v, 0 at the deck edge
    // and 1 at the keel. The section is one curve from the edge down to the keel, flaring out at the top and running to a V underneath.
    private const double BOW_Z = -2.75;
    private const double HULL_LEN = 5.05;
    /// <summary>where the deck floor is, and where the raised foredeck ends</summary>
    private const double SOLE_Y = 0.46;
    private const double FOREDECK_T = 0.356;

    private static double beamAt(double t) => 0.98 * Math.Pow(Math.Sin(Math.Min(1, t / 0.62) * Math.PI * 0.5), 0.85) * (1 - 0.05 * Math.Pow(Math.Max(0, (t - 0.62) / 0.38), 2));
    private static double sheerAt(double t) => 0.82 + 0.42 * Math.Pow(1 - t, 2.6);
    private static double keelAt(double t) => -0.34 + 0.98 * Math.Pow(Math.Max(0, 1 - t / 0.34), 2);

    private static THREE.Vector3 hullPoint(double t, double v, double side)
    {
        double w = beamAt(t), top = sheerAt(t), keel = keelAt(t);
        double a = (1 - v) * (1 - v), b = 2 * v * (1 - v), c = v * v;
        return new THREE.Vector3(side * (a * w + b * w * 0.86), a * top + b * (keel + (top - keel) * 0.12) + c * keel, BOW_Z + HULL_LEN * t);
    }

    /// <summary>How far round the section (v) the hull comes down to height y.</summary>
    private static double vAtHeight(double t, double y)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < 18; i++)
        {
            double mid = (lo + hi) / 2;
            if (hullPoint(t, mid, 1).y > y) lo = mid;
            else hi = mid;
        }
        return (lo + hi) / 2;
    }

    /// <summary>A grid of points as a smooth surface, with `at(i / rows, j / cols)` giving each one.</summary>
    public static THREE.BufferGeometry sheet(int rows, int cols, Func<double, double, THREE.Vector3> at, bool flip = false)
    {
        var pos = new List<double>();
        var idx = new List<int>();
        for (int i = 0; i <= rows; i++)
        for (int j = 0; j <= cols; j++)
        {
            var p = at((double)i / rows, (double)j / cols);
            pos.Add(p.x); pos.Add(p.y); pos.Add(p.z);
            if (i < rows && j < cols)
            {
                int a = i * (cols + 1) + j, b = a + cols + 1;
                if (flip) idx.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                else idx.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        }
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        geo.setIndex(idx);
        geo.computeVertexNormals();
        return geo;
    }

    /// <summary>A tube along a curve through the points.</summary>
    private static THREE.BufferGeometry pipe(IList<THREE.Vector3> pts, double r, bool closed = false) =>
        new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts, closed), pts.Count * 4, r, 8, closed);

    /// <summary>A cylinder between two points.</summary>
    private static THREE.Mesh rod(THREE.Vector3 a, THREE.Vector3 b, double r, THREE.Material mat)
    {
        var d = b.clone().sub(a);
        var m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, d.length(), 8), mat);
        m.position.copy(a).add(b).multiplyScalar(0.5);
        m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), d.normalize());
        return m;
    }

    public readonly THREE.Group group = new THREE.Group();
    public readonly ParticleSystem wake = new ParticleSystem(500, 0.2, 0, false);
    public double x, z;
    /// <summary>same convention as three.js rotation.y: forward is (-sin, -cos); positive turns left</summary>
    public double heading;
    public double speed = 0, steerAngle = 0;

    private double vx = 0, vz = 0;
    private readonly THREE.Group wheel = new THREE.Group(), motor = new THREE.Group();
    private THREE.MeshStandardMaterial stripe = new THREE.MeshStandardMaterial(), rail = new THREE.MeshStandardMaterial();
    private THREE.Mesh nameplate = new THREE.Mesh();
    private readonly THREE.Group prop = new THREE.Group();
    private double pitch = 0, roll = 0, foamTimer = 0;
    private readonly THREE.Vector3 tmp = new THREE.Vector3();
    private readonly THREE.Color white = new THREE.Color(0xf2fbff);

    public Boat(double x, double z, double heading)
    {
        this.x = x;
        this.z = z;
        this.heading = heading;
        build();
    }

    private void build()
    {
        var whiteM = new THREE.MeshStandardMaterial { color = 0xf4f6f8, roughness = 0.3, metalness = 0.1, side = THREE.Side.DoubleSide };
        var blue = new THREE.MeshStandardMaterial { color = 0x1f5fa8, roughness = 0.4, side = THREE.Side.DoubleSide };
        var bottom = new THREE.MeshStandardMaterial { color = 0x173b5e, roughness = 0.6, side = THREE.Side.DoubleSide };
        var teak = new THREE.MeshStandardMaterial { color = 0xa87a4a, roughness = 0.85 };
        var dark = new THREE.MeshStandardMaterial { color = 0x22262b, roughness = 0.6, metalness = 0.4 };
        var chrome = new THREE.MeshStandardMaterial { color = 0xcfd6dc, roughness = 0.3, metalness = 0.7 };
        var cream = new THREE.MeshStandardMaterial { color = 0xe9ddc0, roughness = 0.9 };
        stripe = blue;
        rail = teak;
        THREE.Mesh add(THREE.BufferGeometry geo, THREE.Material mat, double px = 0, double py = 0, double pz = 0, THREE.Object3D parent = null)
        {
            var m = new THREE.Mesh(geo, mat);
            m.position.set(px, py, pz);
            m.castShadow = true;
            m.receiveShadow = true;
            (parent ?? group).add(m);
            return m;
        }
        THREE.BufferGeometry soft(double w, double h, double d, double r) => new THREE.RoundedBoxGeometry(w, h, d, 3, r);

        // ---- the hull shell, in bands: white topsides with a blue stripe, and dark paint below the water
        void band(double v0, double v1, THREE.Material mat)
        {
            foreach (var side in new[] { -1, 1 })
                add(sheet(56, 8, (a, b) => hullPoint(Math.Max(0.0005, a), v0 + (v1 - v0) * b, side), side < 0), mat);
        }
        band(0, 0.14, whiteM);
        band(0.14, 0.2, blue);
        band(0.2, 0.47, whiteM);
        band(0.47, 1, bottom);
        // the flat transom closes the stern, in the same bands
        void across(double t, double v0, double v1, THREE.Material mat) => add(sheet(6, 8, (a, b) => hullPoint(t, v0 + (v1 - v0) * a, b * 2 - 1)), mat);
        across(1, 0, 0.14, whiteM);
        across(1, 0.14, 0.2, blue);
        across(1, 0.2, 0.47, whiteM);
        across(1, 0.47, 1, bottom);
        // a bulkhead closes the front of the cockpit under the foredeck
        across(FOREDECK_T, 0, vAtHeight(FOREDECK_T, SOLE_Y), whiteM);

        // ---- the foredeck, crowned a little, with a hatch on it
        add(sheet(24, 12, (a, b) =>
        {
            double t = Math.Max(0.0005, a * FOREDECK_T);
            double s = b * 2 - 1;
            return new THREE.Vector3(s * beamAt(t), sheerAt(t) + 0.06 * (1 - s * s) * Math.Min(1, t / 0.12), BOW_Z + HULL_LEN * t);
        }), whiteM);
        var hatch = add(soft(0.62, 0.05, 0.52, 0.02), cream, 0, sheerAt(0.24) + 0.07, BOW_Z + HULL_LEN * 0.24);
        hatch.rotation.x = 0.11;

        // ---- the cockpit floor: teak planks running fore and aft, out to where the hull meets it
        THREE.CanvasTexture planksTex()
        {
            var cv = new HTMLCanvasElement(128, 8);
            var x2 = cv.getContext("2d");
            x2.fillStyle = "#b5885a";
            x2.fillRect(0, 0, 128, 8);
            x2.fillStyle = "#5b3f25";
            for (int i = 0; i < 4; i++) x2.fillRect(i * 128 / 4.0, 0, 3, 8);
            var t = new THREE.CanvasTexture(cv);
            t.colorSpace = THREE.ColorSpace.SRGBColorSpace;
            t.wrapS = t.wrapT = THREE.Wrapping.RepeatWrapping;
            return t;
        }
        var planks = planksTex();
        var sole = sheet(20, 10, (a, b) =>
        {
            double t = FOREDECK_T + (1 - FOREDECK_T) * a;
            return new THREE.Vector3((b * 2 - 1) * hullPoint(t, vAtHeight(t, SOLE_Y), 1).x, SOLE_Y, BOW_Z + HULL_LEN * t);
        });
        var soleP = sole.attributes.position;
        var soleUv = new List<double>();
        for (int i = 0; i < soleP.count; i++) { soleUv.Add(soleP.getX(i) * 2); soleUv.Add(soleP.getZ(i)); }
        sole.setAttribute("uv", new THREE.Float32BufferAttribute(soleUv, 2));
        add(sole, new THREE.MeshStandardMaterial { map = planks, roughness = 0.85 });

        // ---- a teak rail along the top edge of the hull, right round the boat
        var railPts = new List<THREE.Vector3>();
        for (int i = 0; i <= 22; i++) railPts.Add(hullPoint(Math.Max(0.0005, 1 - i / 22.0), 0, -1));
        for (int i = 1; i <= 22; i++) railPts.Add(hullPoint(i / 22.0, 0, 1));
        add(pipe(railPts, 0.032, true), teak);

        // ---- a stainless bow rail on three stanchions a side
        var railT = new[] { 0.32, 0.22, 0.12 };
        var upper = new List<THREE.Vector3>();
        foreach (var side in new[] { -1, 1 })
        {
            var pts = railT.Select(t => new THREE.Vector3(side * (beamAt(t) - 0.09), sheerAt(t) + 0.32, BOW_Z + HULL_LEN * t)).ToList();
            for (int i = 0; i < railT.Length; i++) group.add(rod(new THREE.Vector3(pts[i].x, sheerAt(railT[i]), pts[i].z), pts[i], 0.012, chrome));
            if (side < 0) upper.AddRange(pts);
            else
            {
                upper.Add(new THREE.Vector3(0, sheerAt(0.03) + 0.3, BOW_Z + HULL_LEN * 0.03));
                pts.Reverse();
                upper.AddRange(pts);
            }
        }
        add(pipe(upper, 0.013), chrome);

        // ---- the helm console, moulded in one piece with a sloping dash
        var profile = new THREE.Shape();
        profile.moveTo(0.3, SOLE_Y);
        profile.lineTo(0.3, 0.92);
        profile.quadraticCurveTo(0.3, 0.99, 0.37, 1.0);
        profile.lineTo(0.72, 1.1);
        profile.quadraticCurveTo(0.82, 1.12, 0.84, 1.04);
        profile.lineTo(0.84, SOLE_Y);
        profile.closePath();
        var consoleGeo = new THREE.ExtrudeGeometry(profile, new THREE.ExtrudeGeometryOptions { depth = 1.06, bevelEnabled = true, bevelSize = 0.03, bevelThickness = 0.03, bevelSegments = 3 });
        consoleGeo.rotateY(Math.PI / 2);
        consoleGeo.translate(-0.53, 0, 0);
        add(consoleGeo, whiteM);
        const double slope = 0.278;
        var dash = add(soft(0.96, 0.02, 0.34, 0.008), dark, 0, 1.078, -0.545);
        dash.rotation.x = slope;
        foreach (var sx in new[] { -0.28, 0.28 })
        {
            var gauge = add(new THREE.CylinderGeometry(0.052, 0.052, 0.012, 16), chrome, sx, 1.094, -0.56);
            gauge.rotation.x = slope;
            var face = add(new THREE.CylinderGeometry(0.044, 0.044, 0.014, 16), cream, sx, 1.096, -0.56);
            face.rotation.x = slope;
        }
        // the throttle lever beside the wheel
        group.add(rod(new THREE.Vector3(0.42, 1.04, -0.42), new THREE.Vector3(0.42, 1.2, -0.35), 0.012, chrome));
        add(new THREE.SphereGeometry(0.032, 10, 8), dark, 0.42, 1.21, -0.345);

        // the column rises from the dash toward the driver, and the wheel sits square on its end
        var column = add(new THREE.CylinderGeometry(0.022, 0.03, 0.32, 8), dark, 0, 1.19, -0.41);
        column.rotation.x = 0.6;
        wheel.position.set(0, 1.325, -0.32);
        wheel.rotation.x = -(Math.PI / 2 - 0.6);
        var hub = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 0.04, 12), dark);
        hub.rotation.x = Math.PI / 2;
        wheel.add(hub);
        var rim = new THREE.Mesh(new THREE.TorusGeometry(0.17, 0.022, 8, 22), dark);
        wheel.add(rim);
        for (int i = 0; i < 3; i++)
        {
            var sp = new THREE.Mesh(new THREE.BoxGeometry(0.015, 0.34, 0.015), dark);
            sp.rotation.z = i * Math.PI / 3;
            wheel.add(sp);
        }
        group.add(wheel);

        // ---- a windscreen that wraps back around the console, in a slim frame
        // plain diffuse glass: no sun glints or reflections, so its colour stays steady
        var glass = new THREE.MeshLambertMaterial { color = 0xcfe8f5, transparent = true, opacity = 0.2, side = THREE.Side.DoubleSide, depthWrite = false };
        THREE.Vector3 glassAt(double s, double h) => new THREE.Vector3(s * 0.6, 1.1 + 0.42 * h, -0.86 + 0.2 * s * s + 0.17 * h);
        group.add(new THREE.Mesh(sheet(16, 2, (a, b) => glassAt(a * 2 - 1, b)), glass));
        List<THREE.Vector3> span(double h) => Enumerable.Range(0, 9).Select(i => glassAt(i / 4.0 - 1, h)).ToList();
        add(pipe(span(1), 0.013), dark);
        foreach (var s in new[] { -1, 1 }) add(pipe(new[] { glassAt(s, -0.05), glassAt(s, 0.5), glassAt(s, 1) }, 0.013), dark);

        // ---- two helm seats on pedestals
        foreach (var sx in new[] { -0.42, 0.42 })
        {
            add(new THREE.CylinderGeometry(0.16, 0.18, 0.03, 16), dark, sx, SOLE_Y + 0.015, 0.5);
            add(new THREE.CylinderGeometry(0.05, 0.05, 0.2, 10), chrome, sx, SOLE_Y + 0.12, 0.5);
            add(soft(0.52, 0.14, 0.5, 0.06), cream, sx, 0.72, 0.5);
            var back = add(soft(0.52, 0.46, 0.12, 0.05), cream, sx, 1.0, 0.77);
            back.rotation.x = 0.12;
        }
        // ---- the stern bench, right across the boat
        add(soft(1.5, 0.3, 0.42, 0.04), whiteM, 0, 0.61, 1.98);
        add(soft(1.46, 0.12, 0.4, 0.05), cream, 0, 0.82, 1.98);
        add(soft(1.42, 0.3, 0.09, 0.04), cream, 0, 1.0, 2.2);

        // ---- the outboard, on a clamp over the transom: it turns with the wheel, and the propeller spins
        add(soft(0.26, 0.22, 0.14, 0.02), dark, 0, 0.74, 2.33);
        motor.position.set(0, 0, 2.42);
        group.add(motor);
        var cowl = add(soft(0.36, 0.5, 0.52, 0.12), dark, 0, 1.02, 0.08, motor);
        cowl.rotation.x = 0.06;
        add(soft(0.37, 0.06, 0.53, 0.02), whiteM, 0, 0.92, 0.08, motor);
        add(soft(0.13, 1.05, 0.24, 0.05), dark, 0, 0.27, 0.04, motor);
        add(new THREE.BoxGeometry(0.3, 0.02, 0.36), dark, 0, -0.08, 0.06, motor);
        var gearcase = add(new THREE.CapsuleGeometry(0.058, 0.22, 4, 12), dark, 0, -0.2, 0.05, motor);
        gearcase.rotation.x = Math.PI / 2;
        add(new THREE.BoxGeometry(0.018, 0.16, 0.12), dark, 0, -0.3, 0.03, motor);
        prop.position.set(0, -0.2, 0.25);
        motor.add(prop);
        var hubCap = new THREE.Mesh(new THREE.ConeGeometry(0.045, 0.1, 10), chrome);
        hubCap.rotation.x = Math.PI / 2;
        prop.add(hubCap);
        for (int i = 0; i < 3; i++)
        {
            var pivot = new THREE.Group();
            pivot.rotation.z = i * Math.PI * 2 / 3;
            var blade = new THREE.Mesh(new THREE.SphereGeometry(0.1, 10, 6), chrome);
            blade.scale.set(0.5, 1, 0.08);
            blade.position.y = 0.1;
            blade.rotation.y = 0.5;
            pivot.add(blade);
            prop.add(pivot);
        }

        // ---- navigation lights on the bow, and a little flag at the stern quarter
        const double lightT = 0.27;
        double lightY = sheerAt(lightT) + 0.01;
        add(new THREE.SphereGeometry(0.05, 10, 8), new THREE.MeshStandardMaterial { color = 0xd62828, emissive = 0xaa0000, emissiveIntensity = 0.6 }, -beamAt(lightT) - 0.02, lightY, BOW_Z + HULL_LEN * lightT);
        add(new THREE.SphereGeometry(0.05, 10, 8), new THREE.MeshStandardMaterial { color = 0x2a9d4a, emissive = 0x007a2a, emissiveIntensity = 0.6 }, beamAt(lightT) + 0.02, lightY, BOW_Z + HULL_LEN * lightT);
        add(new THREE.CylinderGeometry(0.012, 0.012, 1.0, 6), dark, 0.74, 1.3, 2.14);
        add(new THREE.PlaneGeometry(0.34, 0.22), new THREE.MeshStandardMaterial { color = 0xe63946, side = THREE.Side.DoubleSide }, 0.91, 1.66, 2.14);
        Merge.mergeStatic(group, new HashSet<THREE.Object3D> { wheel });
        // the name on the stern, painted when the boat is given the Marigold's colours
        var cv2 = new HTMLCanvasElement(256, 64);
        var c = cv2.getContext("2d");
        c.fillStyle = "#b8892a";
        c.font = "bold italic 42px Georgia, serif";
        c.textAlign = "center";
        c.textBaseline = "middle";
        c.fillText("Marigold II", 128, 34);
        var tex = new THREE.CanvasTexture(cv2) { colorSpace = THREE.ColorSpace.SRGBColorSpace };
        nameplate = new THREE.Mesh(new THREE.PlaneGeometry(0.64, 0.16), new THREE.MeshStandardMaterial { map = tex, transparent = true, roughness = 0.6 });
        nameplate.position.set(-0.52, 0.55, 2.32);
        nameplate.visible = false;
        group.add(nameplate);
        Merge.mergeStatic(motor, new HashSet<THREE.Object3D> { prop });
    }

    /// <summary>The captain's colours and the name on the stern, from the Luxury shop.</summary>
    public void setMarigold()
    {
        stripe.color.set(0xe0a92e);
        rail.color.set(0xd9ab45);
        rail.metalness = 0.5;
        nameplate.visible = true;
    }

    /// <summary>World position of the helmsman's eyes.</summary>
    public THREE.Vector3 seat(THREE.Vector3 o = null)
    {
        o ??= tmp;
        group.updateWorldMatrix(true, false);
        return group.localToWorld(o.set(0, 1.85, 1.0));
    }

    /// <summary>Moves and bobs the boat on the water. `level` is the surface height.</summary>
    public void update(double dt, double t, DriveInput input, double level, Func<double, double, Resolved> resolve)
    {
        var inp = input ?? new DriveInput();
        double prevSpeed = speed;
        if (inp.throttle > 0) speed += (speed < 0 ? 6 : 3.6) * dt;
        else if (inp.throttle < 0) speed -= (speed > 0.3 ? 5 : 2.2) * dt;
        else speed *= Math.Exp(-dt * 0.35);
        if (inp.brake) speed *= Math.Exp(-dt * 2.4);
        speed = Math.Max(-MAX_REVERSE, Math.Min(MAX_FORWARD, speed));

        steerAngle += (inp.steer * 1 - steerAngle) * Math.Min(1, dt * 3);
        // a boat only turns when water flows past the rudder
        // (a little still bites at a standstill, so a boat nosed into the shore can swing round)
        double grip = Math.Max(0.3, Math.Min(1, Math.Abs(speed) / 3.5));
        heading += steerAngle * 0.75 * grip * JS.Sign(JS.Or(speed, 1)) * dt;

        // it carries on sideways a little in a turn: velocity chases the heading
        double tx = -Math.Sin(heading) * speed, tz = -Math.Cos(heading) * speed;
        double k = Math.Min(1, dt * 1.7);
        vx += (tx - vx) * k;
        vz += (tz - vz) * k;

        var r = resolve(x + vx * dt, z + vz * dt);
        if (r.hit)
        {
            if (Math.Abs(speed) > 1) speed *= -0.3;
            vx *= -0.3;
            vz *= -0.3;
        }
        x = r.x;
        z = r.z;

        // bow lifts as it speeds up and it leans into turns
        double targetPitch = Math.Max(0, speed) * 0.012 + Math.Sin(t * 1.3) * 0.015;
        double targetRoll = -steerAngle * speed * 0.012 + Math.Sin(t * 0.9 + 1) * 0.025;
        pitch += (targetPitch - pitch) * Math.Min(1, dt * 3);
        roll += (targetRoll - roll) * Math.Min(1, dt * 3);
        group.position.set(x, level + Math.Sin(t * 1.7) * 0.04 + 0.05, z);
        group.rotation.set(pitch, heading, roll, "YXZ");
        wheel.rotation.z = steerAngle * 2.4;
        motor.rotation.y = -steerAngle * 0.45;
        prop.rotation.z += speed * dt * 6;

        // wake: foam fanning out behind the stern, and bow spray when it is going fast
        foamTimer -= dt;
        double sp2 = Math.Abs(speed);
        if (foamTimer <= 0 && sp2 > 1)
        {
            foamTimer = 0.04;
            double fx = -Math.Sin(heading), fz = -Math.Cos(heading), rx = Math.Cos(heading), rz = -Math.Sin(heading);
            foreach (var side in new[] { -1, 1 })
            {
                var p = new THREE.Vector3(x - fx * 2.6 + rx * side * 0.5, level + 0.04, z - fz * 2.6 + rz * side * 0.5);
                var v = new THREE.Vector3(rx * side * (0.8 + sp2 * 0.1), 0.1, rz * side * (0.8 + sp2 * 0.1));
                wake.emit(p, v, 1.6, white);
            }
            if (sp2 > 5)
            {
                var bow = new THREE.Vector3(x + fx * 2.4, level + 0.1, z + fz * 2.4);
                foreach (var side in new[] { -1, 1 }) wake.emit(bow, new THREE.Vector3(rx * side * 1.8, 0.8, rz * side * 1.8), 0.7, white);
            }
        }
        wake.update(dt);
    }
}
