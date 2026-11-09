using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/world/SeaTreasure.ts

public class Chest
{
    public THREE.Group group;
    public double x, z;
    public bool taken;
    public double phase;
}

/// <summary>Three treasure chests floating at sea, each marked by a little flag. Sail up to one to haul it aboard.</summary>
public class SeaTreasure
{
    public const double CHEST_REWARD = 175;

    /// <summary>Where the chests drift. All of them are out in open water, a boat ride from the beach.</summary>
    private static readonly double[][] SPOTS = { new double[] { -72, 92 }, new double[] { 58, 128 }, new double[] { 92, 66 } };

    public readonly THREE.Group group = new THREE.Group();
    public readonly List<Chest> chests = new List<Chest>();
    private readonly ParticleSystem sparks = new ParticleSystem(120, 0.16, -0.3, true);
    private readonly THREE.Color gold = new THREE.Color(0xffd45a);
    private double sparkTimer;

    public SeaTreasure()
    {
        foreach (var s in SPOTS)
        {
            var g = build();
            group.add(g);
            chests.Add(new Chest { group = g, x = s[0], z = s[1], taken = false, phase = JS.Random() * 6 });
        }
        group.add(sparks.points);
    }

    private THREE.Group build()
    {
        var g = new THREE.Group();
        var wood = new THREE.MeshStandardMaterial { color = 0x80512a, roughness = 0.85 };
        var band = new THREE.MeshStandardMaterial { color = 0x2c2f34, roughness = 0.45, metalness = 0.7 };
        var goldMat = new THREE.MeshStandardMaterial { color = 0xf2c14e, roughness = 0.25, metalness = 0.9 };
        THREE.Mesh add(THREE.BufferGeometry geo, THREE.Material mat, double x, double y, double z)
        {
            var m = new THREE.Mesh(geo, mat);
            m.position.set(x, y, z);
            m.castShadow = true;
            g.add(m);
            return m;
        }
        // chest body and a rounded lid
        add(new THREE.BoxGeometry(1.2, 0.62, 0.8), wood, 0, 0.3, 0);
        var lid = add(new THREE.CylinderGeometry(0.4, 0.4, 1.2, 14, 1, false, 0, Math.PI), wood, 0, 0.62, 0);
        lid.rotation.z = Math.PI / 2;
        lid.rotation.y = Math.PI / 2;
        // iron bands
        foreach (var x in new[] { -0.42, 0.42 })
        {
            add(new THREE.BoxGeometry(0.1, 0.64, 0.84), band, x, 0.3, 0);
            var arch = add(new THREE.CylinderGeometry(0.42, 0.42, 0.1, 14, 1, false, 0, Math.PI), band, x, 0.62, 0);
            arch.rotation.z = Math.PI / 2;
            arch.rotation.y = Math.PI / 2;
        }
        add(new THREE.BoxGeometry(0.18, 0.2, 0.06), goldMat, 0, 0.58, 0.42);
        // a few gold coins peeking out, as if the lid is not quite shut
        for (int i = 0; i < 3; i++) add(new THREE.CylinderGeometry(0.07, 0.07, 0.02, 10), goldMat, -0.25 + i * 0.25, 0.97, 0.05);
        // two orange floats keep it on the surface
        foreach (var x in new[] { -0.85, 0.85 })
            add(new THREE.SphereGeometry(0.26, 12, 10), new THREE.MeshStandardMaterial { color = 0xff7a2f, roughness = 0.5 }, x, 0.12, 0);
        // a tall pole and a bright flag so it can be spotted from far across the water
        add(new THREE.CylinderGeometry(0.03, 0.03, 3.2, 6), band, 0, 1.9, -0.2);
        var flag = add(new THREE.PlaneGeometry(0.9, 0.55), new THREE.MeshStandardMaterial { color = 0xff3d3d, emissive = 0x661010, emissiveIntensity = 0.5, side = THREE.Side.DoubleSide }, 0.45, 3.2, -0.2);
        flag.name = "flag";
        return g;
    }

    /// <summary>How many chests are still out there.</summary>
    public int remaining => chests.Count(c => !c.taken);

    /// <summary>Marks chests as already found (when loading a save).</summary>
    public void setTaken(List<bool> flags)
    {
        for (int i = 0; i < chests.Count; i++)
        {
            chests[i].taken = flags != null && i < flags.Count && flags[i];
            chests[i].group.visible = !chests[i].taken;
        }
    }

    public List<bool> takenFlags() => chests.Select(c => c.taken).ToList();

    /// <summary>
    /// Floats the chests and checks whether the boat has reached one.
    /// Returns the chest that was just picked up, or null.
    /// </summary>
    public Chest update(double dt, double t, double level, double? boatX, double? boatZ)
    {
        Chest got = null;
        sparkTimer -= dt;
        foreach (var c in chests)
        {
            if (c.taken) continue;
            c.group.position.set(c.x, level - 0.18 + Math.Sin(t * 1.4 + c.phase) * 0.07, c.z);
            c.group.rotation.set(Math.Sin(t * 1.1 + c.phase) * 0.05, c.phase + Math.Sin(t * 0.25 + c.phase) * 0.3, Math.Cos(t * 0.9 + c.phase) * 0.06);
            var flag = c.group.getObjectByName("flag");
            if (flag != null) flag.rotation.y = Math.Sin(t * 3 + c.phase) * 0.25;
            if (sparkTimer <= 0)
            {
                sparks.emit(
                    new THREE.Vector3(c.x + (JS.Random() - 0.5) * 0.8, level + 1.0, c.z + (JS.Random() - 0.5) * 0.5),
                    new THREE.Vector3((JS.Random() - 0.5) * 0.3, 0.7, (JS.Random() - 0.5) * 0.3),
                    1.4,
                    gold);
            }
            if (boatX != null && JS.Hypot(boatX.Value - c.x, boatZ.Value - c.z) < 4)
            {
                c.taken = true;
                c.group.visible = false;
                got = c;
            }
        }
        if (sparkTimer <= 0) sparkTimer = 0.12;
        sparks.update(dt);
        return got;
    }
}
