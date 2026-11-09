using System;
using System.Collections.Generic;
using static Beach;

// Port of src/world/Tracks.ts (the GLSL is in Resources/Shaders/CozyTracks.shader)

internal class Decals
{
    public readonly THREE.InstancedMesh mesh;
    private readonly double[] age, seq;
    private double seqNow;
    private readonly THREE.InstancedBufferAttribute alpha;
    private int next;
    private readonly THREE.Matrix4 m = new THREE.Matrix4();
    private readonly THREE.Quaternion q = new THREE.Quaternion();
    private readonly THREE.Matrix4 basis = new THREE.Matrix4();
    private readonly int max;
    private readonly double life, trail;

    public Decals(int max, double life, double trail, THREE.Texture texture)
    {
        this.max = max;
        this.life = life;
        this.trail = trail;
        var geo = new THREE.PlaneGeometry(1, 1);
        geo.rotateX(-Math.PI / 2);
        alpha = new THREE.InstancedBufferAttribute(new float[max], 1);
        geo.setAttribute("aAlpha", alpha);
        var mat = new THREE.ShaderMaterial
        {
            customShader = "Cozy/Tracks",
            uniforms = new Dictionary<string, THREE.Uniform> { { "uMap", new THREE.Uniform(texture) }, { "uColor", new THREE.Uniform(new THREE.Color(0x4a321e)) } },
            transparent = true,
            depthWrite = false,
            side = THREE.Side.DoubleSide,
            polygonOffset = true,
            polygonOffsetFactor = -2,
            polygonOffsetUnits = -2,
        };
        mesh = new THREE.InstancedMesh(geo, mat, max) { frustumCulled = false, renderOrder = 1 };
        age = new double[max];
        for (int i = 0; i < max; i++) age[i] = 1e9;
        seq = new double[max];
        // park every instance out of sight until it is used
        m.makeScale(0, 0, 0);
        for (int i = 0; i < max; i++) mesh.setMatrixAt(i, m);
    }

    /// <summary>Lays one mark on the ground at x,z. yaw is a three.js rotation.y; (w, l) are its size in metres.</summary>
    public void put(Beach beach, double x, double z, double yaw, double w, double l, bool mirror = false)
    {
        const double e = 0.2;
        double h = beach.heightAt(x, z);
        if (h < WATER_LEVEL + 0.03) return;
        double nx = beach.heightAt(x - e, z) - beach.heightAt(x + e, z);
        double nz = beach.heightAt(x, z - e) - beach.heightAt(x, z + e);
        var n = new THREE.Vector3(nx, 2 * e, nz).normalize();
        if (n.y < 0.78) return; // too steep: the wall of a hole
        var f0 = new THREE.Vector3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        var f = f0.sub(n.clone().multiplyScalar(f0.dot(n))).normalize();
        var r = new THREE.Vector3().crossVectors(f, n);
        basis.makeBasis(r, n, f.clone().negate());
        q.setFromRotationMatrix(basis);
        m.compose(new THREE.Vector3(x, h + 0.025, z), q, new THREE.Vector3(mirror ? -w : w, 1, l));
        int i = next;
        next = (next + 1) % max;
        mesh.setMatrixAt(i, m);
        mesh.instanceMatrix.needsUpdate = true;
        age[i] = 0;
        seq[i] = ++seqNow;
    }

    public void update(double dt, double strength)
    {
        for (int i = 0; i < max; i++)
        {
            if (age[i] > life)
            {
                alpha.array[i] = 0;
                continue;
            }
            // marks beyond the newest `trail` ones age much faster, so a long walk never leaves a permanent road
            age[i] += seqNow - seq[i] > trail ? dt * 5 : dt;
            double t = Math.Max(0, age[i] / life);
            // visible for most of its life, then it smooths away
            alpha.array[i] = (float)(Math.Max(0, t < 0.55 ? 1 : 1 - (t - 0.55) / 0.45) * strength);
        }
        alpha.needsUpdate = true;
    }
}

/// <summary>Footprints and tyre tracks that stay in the sand for a while and then fade out.</summary>
public class Tracks
{
    private const int FOOT_MAX = 180;
    private const int TIRE_MAX = 480;
    /// <summary>a trail only keeps its newest marks; older ones fade away quickly</summary>
    private const int FOOT_TRAIL = 22;
    private const int TIRE_TRAIL = 150;
    private const double FOOT_LIFE = 18;
    private const double TIRE_LIFE = 14;

    private static THREE.CanvasTexture footTexture()
    {
        var c = new HTMLCanvasElement(64, 128);
        var g = c.getContext("2d");
        g.fillStyle = "#fff";
        // toe pad, ball, gap, then the heel (a shoe sole seen from above, toe at the top)
        g.beginPath();
        g.ellipse(32, 38, 21, 32, 0, 0, Math.PI * 2);
        g.fill();
        g.beginPath();
        g.ellipse(32, 100, 15, 20, 0, 0, Math.PI * 2);
        g.fill();
        g.globalCompositeOperation = "destination-out";
        g.fillRect(0, 72, 64, 7);
        for (int i = 0; i < 4; i++) g.fillRect(14, 22 + i * 12, 36, 3);
        return new THREE.CanvasTexture(c);
    }

    /// <summary>A dog's paw seen from above: four toes and a pad, toes at the top.</summary>
    private static THREE.CanvasTexture pawTexture()
    {
        var c = new HTMLCanvasElement(64, 64);
        var g = c.getContext("2d");
        g.fillStyle = "#fff";
        g.beginPath();
        g.ellipse(32, 42, 17, 14, 0, 0, Math.PI * 2);
        g.fill();
        foreach (var (x, y, r) in new[] { (12.0, 28.0, 0.35), (24, 14, 0.1), (40, 14, -0.1), (52, 28, -0.35) })
        {
            g.beginPath();
            g.ellipse(x, y, 7, 10, r, 0, Math.PI * 2);
            g.fill();
        }
        return new THREE.CanvasTexture(c);
    }

    private static THREE.CanvasTexture treadTexture()
    {
        var c = new HTMLCanvasElement(32, 128);
        var g = c.getContext("2d");
        g.fillStyle = "#fff";
        g.fillRect(2, 0, 28, 128);
        g.globalCompositeOperation = "destination-out";
        for (int y = 4; y < 128; y += 10) g.fillRect(0, y, 32, 4);
        g.fillRect(14, 0, 4, 128);
        return new THREE.CanvasTexture(c);
    }

    public readonly THREE.Group group = new THREE.Group();
    private readonly Decals feet, tires, paws;
    private bool stepLeft;
    private readonly Beach beach;

    public Tracks(Beach beach)
    {
        this.beach = beach;
        feet = new Decals(FOOT_MAX, FOOT_LIFE, FOOT_TRAIL, footTexture());
        tires = new Decals(TIRE_MAX, TIRE_LIFE, TIRE_TRAIL, treadTexture());
        paws = new Decals(260, 24, 120, pawTexture());
        group.add(feet.mesh, tires.mesh, paws.mesh);
    }

    /// <summary>One step by the player; alternates left and right feet.</summary>
    public void step(double x, double z, double yaw)
    {
        stepLeft = !stepLeft;
        int side = stepLeft ? -1 : 1;
        double px = x + Math.Cos(yaw) * 0.14 * side;
        double pz = z - Math.Sin(yaw) * 0.14 * side;
        feet.put(beach, px, pz, yaw, 0.13, 0.3, stepLeft);
    }

    /// <summary>One paw print by the dog.</summary>
    public void paw(double x, double z, double yaw, bool left) => paws.put(beach, x, z, yaw, 0.1, 0.11, left);

    public void tire(double x, double z, double yaw) => tires.put(beach, x, z, yaw, 0.24, 0.6);

    /// <summary>`night` (0..1) makes the marks a little fainter in the dark.</summary>
    public void update(double dt, double night)
    {
        double s = 1 - 0.45 * night;
        feet.update(dt, s);
        tires.update(dt, s);
        paws.update(dt, s);
    }
}
