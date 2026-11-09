using System;
using System.Collections.Generic;

// Port of src/core/Particles.ts (the GLSL is in Resources/Shaders/CozyParticles.shader)

/// <summary>Small CPU-simulated billboard particle pool.</summary>
public class ParticleSystem
{
    public readonly THREE.Points points;
    private readonly float[] pos, col, alpha;
    private readonly double[] vel, life, maxLife;
    private int cursor;
    private readonly int max;
    private readonly double gravity;

    public ParticleSystem(int max, double size, double gravity, bool additive = false, bool heart = false)
    {
        this.max = max;
        this.gravity = gravity;
        pos = new float[max * 3];
        col = new float[max * 3];
        alpha = new float[max];
        vel = new double[max * 3];
        life = new double[max];
        maxLife = new double[max];
        for (int i = 0; i < max; i++) maxLife[i] = 1;
        var geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
        geo.setAttribute("aColor", new THREE.BufferAttribute(col, 3));
        geo.setAttribute("aAlpha", new THREE.BufferAttribute(alpha, 1));
        var mat = new THREE.ShaderMaterial
        {
            customShader = heart ? "Cozy/ParticlesHeart" : "Cozy/Particles",
            uniforms = new Dictionary<string, THREE.Uniform> { { "uSize", new THREE.Uniform(size) } },
            transparent = true,
            depthWrite = false,
            blending = additive ? THREE.Blending.AdditiveBlending : THREE.Blending.NormalBlending,
        };
        points = new THREE.Points(geo, mat) { frustumCulled = false, renderOrder = 10 };
    }

    public void emit(THREE.Vector3 p, THREE.Vector3 v, double lifetime, THREE.Color color)
    {
        int i = cursor;
        cursor = (cursor + 1) % max;
        pos[i * 3] = (float)p.x; pos[i * 3 + 1] = (float)p.y; pos[i * 3 + 2] = (float)p.z;
        vel[i * 3] = v.x; vel[i * 3 + 1] = v.y; vel[i * 3 + 2] = v.z;
        col[i * 3] = (float)color.r; col[i * 3 + 1] = (float)color.g; col[i * 3 + 2] = (float)color.b;
        life[i] = lifetime;
        maxLife[i] = lifetime;
    }

    public void burst(THREE.Vector3 p, int count, double speed, double up, double lifetime, THREE.Color color)
    {
        var v = new THREE.Vector3();
        for (int n = 0; n < count; n++)
        {
            double a = JS.Random() * Math.PI * 2;
            double s = speed * (0.3 + JS.Random() * 0.7);
            v.set(Math.Cos(a) * s, up * (0.5 + JS.Random()), Math.Sin(a) * s);
            emit(p, v, lifetime * (0.6 + JS.Random() * 0.6), color);
        }
    }

    public void update(double dt)
    {
        for (int i = 0; i < max; i++)
        {
            if (life[i] <= 0)
            {
                alpha[i] = 0;
                continue;
            }
            life[i] -= dt;
            int i3 = i * 3;
            vel[i3 + 1] -= gravity * dt;
            pos[i3] += (float)(vel[i3] * dt);
            pos[i3 + 1] += (float)(vel[i3 + 1] * dt);
            pos[i3 + 2] += (float)(vel[i3 + 2] * dt);
            alpha[i] = (float)Math.Max(0, life[i] / maxLife[i]);
        }
        var g = points.geometry;
        g.attributes.position.needsUpdate = true;
        g.attributes["aAlpha"].needsUpdate = true;
        g.attributes["aColor"].needsUpdate = true;
    }
}
