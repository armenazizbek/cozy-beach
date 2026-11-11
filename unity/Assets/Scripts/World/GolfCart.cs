using System;
using System.Collections.Generic;

// Port of src/world/GolfCart.ts

public class DriveInput
{
    /// <summary>+1 forward, -1 back/brake</summary>
    public double throttle;
    /// <summary>+1 left, -1 right</summary>
    public double steer;
    public bool brake;
}

public struct Resolved
{
    public double x, z;
    public bool hit;
    public Resolved(double x, double z, bool hit) { this.x = x; this.z = z; this.hit = hit; }
}

/// <summary>A little electric golf cart the player can climb into and drive around the island.</summary>
public class GolfCart
{
    private const double MAX_FORWARD = 10;
    private const double MAX_REVERSE = 4;
    private const double WHEEL_BASE = 1.7;

    public readonly THREE.Group group = new THREE.Group();
    private THREE.MeshStandardMaterial paint = new THREE.MeshStandardMaterial();
    public double x, z;
    /// <summary>same convention as three.js rotation.y: forward is (-sin, -cos); positive turns left</summary>
    public double heading;
    public double speed = 0, steerAngle = 0;

    private readonly List<THREE.Group> frontWheels = new List<THREE.Group>();
    private readonly List<THREE.Object3D> spinners = new List<THREE.Object3D>();
    private double wheelRoll = 0;
    private readonly THREE.Group steeringWheel = new THREE.Group();
    private readonly THREE.SpotLight headlight;
    private readonly List<THREE.MeshStandardMaterial> lampMats = new List<THREE.MeshStandardMaterial>();
    private readonly THREE.Vector3 tmp = new THREE.Vector3();
    private readonly Beach beach;

    public GolfCart(double x, double z, double heading, Beach beach)
    {
        this.x = x;
        this.z = z;
        this.heading = heading;
        this.beach = beach;
        build();
        headlight = new THREE.SpotLight(0xfff0c8, 0, 32, 0.55, 0.6, 1.5);
        headlight.visible = false;
        headlight.position.set(0, 0.9, -1.4);
        headlight.target.position.set(0, 0.5, -10);
        group.add(headlight, headlight.target);
        place();
    }

    /// <summary>Gold paint, from the Luxury shop.</summary>
    public void setGilded()
    {
        paint.color.set(0xe8b93a);
        paint.metalness = 0.6;
        paint.roughness = 0.3;
    }

    private THREE.Mesh part(THREE.BufferGeometry geo, THREE.Material mat, double px, double py, double pz, THREE.Object3D parent = null)
    {
        var m = new THREE.Mesh(geo, mat);
        m.position.set(px, py, pz);
        m.castShadow = true;
        m.receiveShadow = true;
        (parent ?? group).add(m);
        return m;
    }

    private void build()
    {
        var teal = new THREE.MeshStandardMaterial { color = 0x2f9e8f, roughness = 0.45, metalness = 0.2 };
        var white = new THREE.MeshStandardMaterial { color = 0xf4f2ea, roughness = 0.6 };
        paint = teal;
        var cream = new THREE.MeshStandardMaterial { color = 0xe6d7b3, roughness = 0.85 };
        var dark = new THREE.MeshStandardMaterial { color = 0x24262a, roughness = 0.7, metalness = 0.4 };
        THREE.BufferGeometry box(double w, double h, double d) => new THREE.BoxGeometry(w, h, d);

        // frame and body
        part(box(1.45, 0.16, 2.9), dark, 0, 0.42, 0.05);
        part(box(1.5, 0.52, 1.15), teal, 0, 0.72, -0.95);
        part(box(1.42, 0.08, 1.7), white, 0, 0.52, 0.6);
        // bench seat and backrest
        part(box(1.1, 0.18, 0.62), dark, 0, 0.65, 0.4);
        part(box(1.2, 0.16, 0.52), cream, 0, 0.82, 0.35);
        part(box(1.2, 0.5, 0.1), cream, 0, 1.15, 0.66);
        // cargo bed with side walls
        part(box(1.4, 0.1, 0.85), teal, 0, 0.58, 1.38);
        foreach (var sx in new[] { -1, 1 }) part(box(0.06, 0.32, 0.85), teal, sx * 0.7, 0.76, 1.38);
        part(box(1.4, 0.32, 0.06), teal, 0, 0.76, 1.8);
        // dashboard and steering column
        part(box(1.35, 0.32, 0.16), teal, 0, 1.02, -0.42);
        var column = part(new THREE.CylinderGeometry(0.025, 0.025, 0.4, 8), dark, -0.36, 1.044, -0.204);
        column.rotation.x = 0.67; // along the wheel's axis, from the dashboard up to the hub
        steeringWheel.position.set(-0.36, 1.2, -0.08);
        steeringWheel.rotation.x = -0.9;
        var wheelMesh = new THREE.Mesh(new THREE.TorusGeometry(0.17, 0.022, 8, 22), dark) { castShadow = true };
        steeringWheel.add(wheelMesh);
        foreach (var a in new[] { 0, Math.PI / 2, Math.PI, Math.PI * 1.5 })
        {
            var spoke = new THREE.Mesh(new THREE.BoxGeometry(0.015, 0.17, 0.015), dark);
            spoke.position.set(Math.Sin(a) * 0.085, Math.Cos(a) * 0.085, 0);
            spoke.rotation.z = -a;
            steeringWheel.add(spoke);
        }
        group.add(steeringWheel);

        // roof canopy
        foreach (var (px, pz) in new[] { (-0.68, -0.52), (0.68, -0.52), (-0.68, 0.78), (0.68, 0.78) })
            part(new THREE.CylinderGeometry(0.028, 0.028, 1.45, 8), dark, px, 1.4, pz);
        part(box(1.62, 0.06, 2.0), white, 0, 2.14, 0.13);
        part(box(1.66, 0.03, 2.04), teal, 0, 2.1, 0.13);

        // wheels: front ones steer
        var tire = new THREE.CylinderGeometry(0.3, 0.3, 0.2, 18);
        var hub = new THREE.CylinderGeometry(0.13, 0.13, 0.22, 10);
        var hubMat = new THREE.MeshStandardMaterial { color = 0xc9ced3, roughness = 0.3, metalness = 0.8 };
        foreach (var (wx, wz, front) in new[] { (-0.82, -1.0, true), (0.82, -1.0, true), (-0.82, 1.1, false), (0.82, 1.1, false) })
        {
            var pivot = new THREE.Group();
            pivot.position.set(wx, 0.3, wz);
            group.add(pivot);
            var spin = new THREE.Group();
            pivot.add(spin);
            var t = part(tire, dark, 0, 0, 0, spin);
            t.rotation.z = Math.PI / 2;
            var h = part(hub, hubMat, 0, 0, 0, spin);
            h.rotation.z = Math.PI / 2;
            // a lug so the spin is visible
            part(box(0.24, 0.05, 0.08), hubMat, 0, 0.18, 0, spin);
            spinners.Add(spin);
            if (front) frontWheels.Add(pivot);
        }

        // lamps
        foreach (var sx in new[] { -0.5, 0.5 })
        {
            var mat = new THREE.MeshStandardMaterial { color = 0xfff6d8, emissive = 0xffe9a8, emissiveIntensity = 0.3 };
            lampMats.Add(mat);
            part(new THREE.SphereGeometry(0.1, 10, 8), mat, sx, 0.85, -1.54);
        }
        foreach (var sx in new[] { -0.55, 0.55 })
            part(box(0.14, 0.08, 0.04), new THREE.MeshStandardMaterial { color = 0xc62828, emissive = 0x8a1010, emissiveIntensity = 0.5 }, sx, 0.78, 1.82);
        // a small flag on the roof, to spot the cart from far away
        part(new THREE.CylinderGeometry(0.01, 0.01, 0.6, 6), dark, 0.7, 2.45, -0.7);
        part(new THREE.PlaneGeometry(0.3, 0.2), new THREE.MeshStandardMaterial { color = 0xe63946, side = THREE.Side.DoubleSide }, 0.85, 2.65, -0.7);
    }

    /// <summary>World position of the driver's eyes.</summary>
    public THREE.Vector3 seat(THREE.Vector3 o = null)
    {
        o ??= tmp;
        group.updateWorldMatrix(true, false);
        return group.localToWorld(o.set(-0.36, 1.46, 0.18));
    }

    public void setHeadlights(bool on)
    {
        headlight.intensity = on ? 35 : 0;
        // a light that is off still costs every material in the scene a spot-light loop, so take it out of the render altogether
        headlight.visible = on;
        foreach (var m in lampMats) m.emissiveIntensity = on ? 2 : 0.3;
    }

    /// <summary>The most a hole can drop the cart, however deep it was dug.</summary>
    private const double MAX_DIP = 0.22;

    /// <summary>
    /// The ground as the cart feels it. A wheel is far wider than the trowel, so it bridges a hole instead of following it to
    /// the bottom: a shallow hole is felt nearly in full, a deep one never by more than MAX_DIP. The cart still rocks as it
    /// crosses a dug-up stretch, but it no longer drops into a pit and climbs back out. Heaps of sand are felt as they are.
    /// </summary>
    private double ground(double px, double pz)
    {
        double depth = beach.depthAt(px, pz);
        double felt = MAX_DIP * (1 - Math.Exp(-depth / MAX_DIP));
        return beach.heightAt(px, pz) + depth - felt;
    }

    /// <summary>Puts the body on the ground, tilted to the slope under it.</summary>
    private void place()
    {
        double fx = -Math.Sin(heading), fz = -Math.Cos(heading);
        double rx = Math.Cos(heading), rz = -Math.Sin(heading);
        double h(double dx, double dz) => ground(x + dx, z + dz);
        double hF = h(fx * 1.0, fz * 1.0), hB = h(-fx * 1.0, -fz * 1.0), hR = h(rx * 0.8, rz * 0.8), hL = h(-rx * 0.8, -rz * 0.8);
        group.position.set(x, (hF + hB + hR + hL) / 4 - 0.02, z);
        group.rotation.set(Math.Atan2(hF - hB, 2.0), heading, Math.Atan2(hR - hL, 1.6), "YXZ");
    }

    public void update(double dt, DriveInput input, Func<double, double, Resolved> resolve)
    {
        var inp = input ?? new DriveInput();
        if (inp.throttle > 0) speed += (speed < 0 ? 9 : 5.5) * dt;
        else if (inp.throttle < 0) speed -= (speed > 0.3 ? 9 : 4) * dt;
        else speed -= JS.Sign(speed) * Math.Min(Math.Abs(speed), 3 * dt);
        if (inp.brake) speed -= JS.Sign(speed) * Math.Min(Math.Abs(speed), 14 * dt);
        speed = Math.Max(-MAX_REVERSE, Math.Min(MAX_FORWARD, speed));

        steerAngle += (inp.steer * 0.55 - steerAngle) * Math.Min(1, dt * 6);
        double eff = steerAngle * (1 - Math.Min(0.5, Math.Abs(speed) / 20));
        heading += speed / WHEEL_BASE * Math.Tan(eff) * dt;

        double nx = x - Math.Sin(heading) * speed * dt;
        double nz = z - Math.Cos(heading) * speed * dt;
        var r = resolve(nx, nz);
        if (r.hit && Math.Abs(speed) > 0.5) speed *= -0.25;
        x = r.x;
        z = r.z;

        wheelRoll += speed * dt / 0.3;
        foreach (var s in spinners) s.rotation.x = wheelRoll;
        foreach (var f in frontWheels) f.rotation.y = steerAngle;
        steeringWheel.rotation.z = steerAngle * 2.6;
        place();
    }
}
