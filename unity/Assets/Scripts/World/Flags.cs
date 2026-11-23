using System;
using System.Collections.Generic;
using UnityEngine;

// Port of src/world/Flags.ts; native since unity/NATIVE_PLAN.md S9.

/// <summary>Marker flags the player plants on the beach. Their positions also feed the maps.</summary>
public class Flags
{
    private class Placed
    {
        public Transform group;
        public Transform cloth;
        public double phase;
    }

    private Transform root;

    /// <summary>The flags' parent under the world root (made on first use: Flags is built before the world is bound).</summary>
    public Transform Root => root != null ? root : root = Draw.Group(World.Root);

    /// <summary>[x, z] of every planted flag; edited in place so the map can hold on to it</summary>
    public readonly List<double[]> positions = new List<double[]>();
    private readonly List<Placed> placed = new List<Placed>();
    private readonly THREE.CylinderGeometry pole = new THREE.CylinderGeometry(0.025, 0.03, 1.7, 6);
    private readonly THREE.MeshStandardMaterial poleMat = new THREE.MeshStandardMaterial { color = 0xd9c9a3, roughness = 0.8 };
    private readonly THREE.BufferGeometry clothGeo;
    private readonly THREE.MeshStandardMaterial clothMat = new THREE.MeshStandardMaterial { color = 0xe63946, roughness = 0.7, side = THREE.Side.DoubleSide };
    private readonly THREE.SphereGeometry capGeo = new THREE.SphereGeometry(0.05, 8, 6);
    private Mesh poleMesh, clothMesh, capMesh;

    public Flags()
    {
        var shape = new THREE.Shape();
        shape.moveTo(0, 0);
        shape.lineTo(0.8, -0.25);
        shape.lineTo(0, -0.5);
        shape.closePath();
        clothGeo = new THREE.ShapeGeometry(shape);
    }

    public void add(double x, double z, double y)
    {
        if (poleMesh == null)
        {
            poleMesh = MeshKit.ToMesh(pole);
            clothMesh = MeshKit.ToMesh(clothGeo);
            capMesh = MeshKit.ToMesh(capGeo);
        }
        var g = Draw.Group(Root);
        var p = Draw.Mesh(g, poleMesh, MaterialKit.Get(poleMat), cast: true).transform;
        p.localPosition = new Vector3(0, 0.75f, 0);
        var cloth = Draw.Mesh(g, clothMesh, MaterialKit.Get(clothMat), cast: true).transform;
        cloth.localPosition = GameSpace.V(0.03, 1.55, 0);
        var cap = Draw.Mesh(g, capMesh, MaterialKit.Get(poleMat)).transform;
        cap.localPosition = new Vector3(0, 1.62f, 0);
        g.localPosition = GameSpace.V(x, y, z);
        g.localRotation = GameSpace.Euler(0, JS.Random() * Math.PI * 2, 0);
        placed.Add(new Placed { group = g, cloth = cloth, phase = JS.Random() * 6 });
        positions.Add(new[] { x, z });
    }

    /// <summary>Index of the flag nearest to (x, z) within reach, or -1.</summary>
    public int nearest(double x, double z, double reach)
    {
        int best = -1;
        double bd = reach;
        for (int i = 0; i < positions.Count; i++)
        {
            double d = JS.Hypot(positions[i][0] - x, positions[i][1] - z);
            if (d < bd)
            {
                bd = d;
                best = i;
            }
        }
        return best;
    }

    public void remove(int i)
    {
        if (i < 0 || i >= placed.Count) return;
        UnityEngine.Object.Destroy(placed[i].group.gameObject);
        placed.RemoveAt(i);
        positions.RemoveAt(i);
    }

    /// <summary>Replaces every flag, e.g. when a save is loaded.</summary>
    public void setAll(List<double[]> list, Func<double, double, double> heightAt)
    {
        while (placed.Count > 0) remove(placed.Count - 1);
        foreach (var p in list) add(p[0], p[1], heightAt(p[0], p[1]));
    }

    public void update(double t)
    {
        foreach (var p in placed)
            p.cloth.localRotation = GameSpace.Euler(0, Math.Sin(t * 2.4 + p.phase) * 0.22, Math.Sin(t * 3.1 + p.phase) * 0.05);
    }
}
