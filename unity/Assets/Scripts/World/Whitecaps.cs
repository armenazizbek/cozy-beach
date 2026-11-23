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
        /// <summary>where it was put down, and when (on the foam's own clock): the shader drifts it on from there</summary>
        public double x, z, born, life = 1, len = 1, yaw;
    }

    /// <summary>
    /// One mesh of COUNT quads, never moved on the CPU: each quad carries its patch's spawn point, size, heading, birth and life
    /// (aPatch, aLife), and Cozy/Whitecaps drifts it, bobs it on the swell, grows it and fades it in and out. The CPU only puts a
    /// spent patch down somewhere new, and sends the new values at most ten times a second. (The patches used to be an
    /// InstancedMesh whose every matrix changed each frame, so the bridge baked all of them into a new mesh every frame.)
    /// </summary>
    private readonly THREE.BufferGeometry geo;
    private readonly THREE.ShaderMaterial mat;
    /// <summary>The foam's GameObject under the world root (native since unity/NATIVE_PLAN.md S9), once placed.</summary>
    public UnityEngine.Transform root;
    private UnityEngine.Mesh unityMesh;
    private readonly List<Patch> patches = new List<Patch>();
    private readonly float[] patchData = new float[COUNT * 4 * 4], lifeData = new float[COUNT * 4 * 4];
    private readonly THREE.BufferAttribute patchAttr, lifeAttr;
    private readonly THREE.Uniform clockU = new THREE.Uniform(0.0), swellU = new THREE.Uniform(0.0), levelU = new THREE.Uniform(0.0);
    private readonly THREE.Color color = new THREE.Color();
    private readonly THREE.Color nightTint = new THREE.Color(0x6f86a8);
    private const double DRIFT_X = 0.35, DRIFT_Z = 0.12;
    /// <summary>the foam's own clock, and when its patches were last sent to the GPU</summary>
    private double clock = 0, sentAt = -1;
    private bool dirty = true;

    public Whitecaps()
    {
        // four corners per patch, as PlaneGeometry(1, 1) laid flat: x and z from -0.5 to 0.5
        var pos = new float[COUNT * 4 * 3];
        var uv = new float[COUNT * 4 * 2];
        var index = new List<int>(COUNT * 6);
        double[][] corners = { new[] { -0.5, -0.5 }, new[] { 0.5, -0.5 }, new[] { -0.5, 0.5 }, new[] { 0.5, 0.5 } };
        for (int i = 0; i < COUNT; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                int v = i * 4 + c;
                pos[v * 3] = (float)corners[c][0];
                pos[v * 3 + 2] = (float)corners[c][1];
                uv[v * 2] = (float)(corners[c][0] + 0.5);
                uv[v * 2 + 1] = (float)(0.5 - corners[c][1]);
            }
            int b = i * 4;
            index.AddRange(new[] { b, b + 2, b + 1, b + 2, b + 3, b + 1 });
        }
        geo = new THREE.BufferGeometry();
        geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
        geo.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
        patchAttr = new THREE.BufferAttribute(patchData, 4);
        lifeAttr = new THREE.BufferAttribute(lifeData, 4);
        geo.setAttribute("aPatch", patchAttr);
        geo.setAttribute("aLife", lifeAttr);
        geo.setIndex(index);
        mat = new THREE.ShaderMaterial
        {
            customShader = "Cozy/Whitecaps",
            uniforms = new Dictionary<string, THREE.Uniform>
            {
                { "uMap", new THREE.Uniform(foamTexture()) }, { "uColor", new THREE.Uniform(new THREE.Color(0xffffff)) },
                { "uClock", clockU }, { "uSwellTime", swellU }, { "uLevel", levelU },
            },
            transparent = true,
            depthWrite = false,
            side = THREE.Side.DoubleSide,
            polygonOffset = true,
            polygonOffsetFactor = -2,
            polygonOffsetUnits = -2,
        };
        for (int i = 0; i < COUNT; i++)
        {
            var p = new Patch();
            patches.Add(p);
            spawn(i, p, 0, SHORE_Z + 60, true);
        }
    }

    /// <summary>Puts the foam into the world: one mesh, never culled, drawn after the sea (renderOrder 2).</summary>
    public void Place()
    {
        unityMesh = MeshKit.ToMesh(geo);
        unityMesh.MarkDynamic();
        root = Draw.Mesh(World.Land, unityMesh, MaterialKit.Get(mat), order: 2, unculled: true).transform;
        dirty = false;
    }

    /// <summary>Puts a patch somewhere in the sea near the player (or near a given spot at the start).</summary>
    private void spawn(int i, Patch p, double px, double pz, bool scatterAge = false)
    {
        for (int tries = 0; tries < 8; tries++)
        {
            double a = JS.Random() * Math.PI * 2;
            double r = Math.Sqrt(JS.Random()) * RADIUS;
            p.x = px + Math.Cos(a) * r;
            p.z = pz + Math.Sin(a) * r;
            if (p.z > SHORE_Z + 4 && islandNear(p.x, p.z) == null) break; // only open water
        }
        if (p.z <= SHORE_Z + 4) p.z = SHORE_Z + 6 + JS.Random() * 80;
        p.life = 7 + JS.Random() * 9;
        p.born = clock - (scatterAge ? JS.Random() * p.life : 0);
        p.len = 5 + JS.Random() * 11;
        // the streaks run roughly along the shore, as swell does
        p.yaw = (JS.Random() - 0.5) * 0.6;
        // a patch that was set down before it began drifts on from there: move its start back by the drift it has done
        double age = clock - p.born;
        p.x -= DRIFT_X * age;
        p.z -= DRIFT_Z * age;
        for (int c = 0; c < 4; c++)
        {
            int o = (i * 4 + c) * 4;
            patchData[o] = (float)p.x; patchData[o + 1] = (float)p.z; patchData[o + 2] = (float)p.yaw; patchData[o + 3] = (float)p.len;
            lifeData[o] = (float)p.born; lifeData[o + 1] = (float)p.life;
        }
        dirty = true;
    }

    public void update(double dt, double level, double px, double pz, double night, double swellTime)
    {
        clock += dt;
        for (int i = 0; i < COUNT; i++)
        {
            var p = patches[i];
            double age = clock - p.born;
            double x = p.x + DRIFT_X * age, z = p.z + DRIFT_Z * age;
            bool far = JS.Hypot(x - px, z - pz) > RADIUS * 1.15;
            if (age >= p.life || far) spawn(i, p, px, pz);
        }
        // a spent patch is invisible in the shader until it is sent again, so a tenth of a second's wait shows nothing
        if (dirty && (clock - sentAt >= 0.1 || sentAt < 0))
        {
            // the patches' data lives in the mesh's TEXCOORD2 (aLife) and TEXCOORD3 (aPatch), as MeshKit lays extras out
            if (unityMesh != null) MeshKit.Refill(unityMesh, geo);
            dirty = false;
            sentAt = clock;
        }
        clockU.value = clock;
        swellU.value = swellTime;
        levelU.value = level;
        // foam looks grey-blue in the dark
        color.set(0xffffff).lerp(nightTint, night * 0.8);
        ((THREE.Color)mat.uniforms["uColor"].value).copy(color);
        MaterialKit.Push(mat);
    }
}
