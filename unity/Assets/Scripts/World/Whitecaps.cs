using System;
using System.Collections.Generic;
using static Beach;

// Port of src/world/Whitecaps.ts (the GLSL is in Resources/Shaders/CozyWhitecaps.shader)

/// <summary>Whitecaps: patches of foam that bloom, drift and melt away across the open sea around the player.</summary>
public class Whitecaps
{
    private const int COUNT = 260;
    private const double RADIUS = 110;

    /// <summary>A lacy, streaky foam patch, wider than it is tall.</summary>
    private static THREE.CanvasTexture foamTexture()
    {
        const int w = 256, h = 96;
        var c = new HTMLCanvasElement(w, h);
        var g = c.getContext("2d");
        // a body of soft blobs along the streak
        for (int i = 0; i < 70; i++)
        {
            double t = JS.Random();
            double x = 20 + t * (w - 40);
            double spread = Math.Sin(t * Math.PI);
            double y = h / 2.0 + (JS.Random() - 0.5) * h * 0.5 * spread;
            double r = 5 + JS.Random() * 14 * (0.4 + spread);
            var grad = g.createRadialGradient(x, y, 0, x, y, r);
            grad.addColorStop(0, "rgba(255,255,255,0.9)");
            grad.addColorStop(1, "rgba(255,255,255,0)");
            g.fillStyle = grad;
            g.fillRect(x - r, y - r, r * 2, r * 2);
        }
        // punch lace holes so it reads as foam rather than a smudge
        g.globalCompositeOperation = "destination-out";
        for (int i = 0; i < 55; i++)
        {
            g.fillStyle = $"rgba(0,0,0,{JS.Str(0.35 + JS.Random() * 0.5)})";
            g.beginPath();
            g.arc(JS.Random() * w, JS.Random() * h, 1.5 + JS.Random() * 5, 0, Math.PI * 2);
            g.fill();
        }
        return new THREE.CanvasTexture(c);
    }

    private class Patch
    {
        public double x, z, age, life = 1, len = 1, yaw;
    }

    public readonly THREE.InstancedMesh mesh;
    private readonly List<Patch> patches = new List<Patch>();
    private readonly THREE.InstancedBufferAttribute alpha;
    private readonly THREE.Object3D dummy = new THREE.Object3D();
    private readonly THREE.Color color = new THREE.Color();
    private readonly THREE.Color nightTint = new THREE.Color(0x6f86a8);

    public Whitecaps()
    {
        var geo = new THREE.PlaneGeometry(1, 1);
        geo.rotateX(-Math.PI / 2);
        alpha = new THREE.InstancedBufferAttribute(new float[COUNT], 1);
        geo.setAttribute("aAlpha", alpha);
        var mat = new THREE.ShaderMaterial
        {
            customShader = "Cozy/Whitecaps",
            uniforms = new Dictionary<string, THREE.Uniform> { { "uMap", new THREE.Uniform(foamTexture()) }, { "uColor", new THREE.Uniform(new THREE.Color(0xffffff)) } },
            transparent = true,
            depthWrite = false,
            side = THREE.Side.DoubleSide,
            polygonOffset = true,
            polygonOffsetFactor = -2,
            polygonOffsetUnits = -2,
        };
        mesh = new THREE.InstancedMesh(geo, mat, COUNT) { frustumCulled = false, renderOrder = 2 };
        for (int i = 0; i < COUNT; i++)
        {
            var p = new Patch();
            patches.Add(p);
            spawn(p, 0, SHORE_Z + 60, true);
        }
    }

    /// <summary>Puts a patch somewhere in the sea near the player (or near a given spot at the start).</summary>
    private void spawn(Patch p, double px, double pz, bool scatterAge = false)
    {
        for (int tries = 0; tries < 8; tries++)
        {
            double a = JS.Random() * Math.PI * 2;
            double r = Math.Sqrt(JS.Random()) * RADIUS;
            p.x = px + Math.Cos(a) * r;
            p.z = pz + Math.Sin(a) * r;
            if (p.z > SHORE_Z + 4) break; // only open water
        }
        if (p.z <= SHORE_Z + 4) p.z = SHORE_Z + 6 + JS.Random() * 80;
        p.life = 7 + JS.Random() * 9;
        p.age = scatterAge ? JS.Random() * p.life : 0;
        p.len = 5 + JS.Random() * 11;
        // the streaks run roughly along the shore, as swell does
        p.yaw = (JS.Random() - 0.5) * 0.6;
    }

    public void update(double dt, double level, double px, double pz, double night, Func<double, double, double> swell)
    {
        const double drift = 0.35;
        for (int i = 0; i < COUNT; i++)
        {
            var p = patches[i];
            p.age += dt;
            p.x += drift * dt;
            p.z += 0.12 * dt;
            bool far = JS.Hypot(p.x - px, p.z - pz) > RADIUS * 1.15;
            if (p.age >= p.life || far) spawn(p, px, pz);
            double u = p.age / p.life;
            // swell up quickly, hold, then melt away
            double a = Math.Pow(Math.Sin(Math.PI * Math.Min(1, u)), 0.7);
            alpha.array[i] = (float)(a * 0.8);
            double grow = 0.75 + 0.35 * u;
            dummy.position.set(p.x, level + swell(p.x, p.z) + 0.07, p.z);
            dummy.rotation.set(0, p.yaw, 0);
            dummy.scale.set(p.len * grow, 1, p.len * grow * 0.34);
            dummy.updateMatrix();
            mesh.setMatrixAt(i, dummy.matrix);
        }
        mesh.instanceMatrix.needsUpdate = true;
        alpha.needsUpdate = true;
        // foam looks grey-blue in the dark
        color.set(0xffffff).lerp(nightTint, night * 0.8);
        ((THREE.Color)((THREE.ShaderMaterial)mesh.material).uniforms["uColor"].value).copy(color);
    }
}
