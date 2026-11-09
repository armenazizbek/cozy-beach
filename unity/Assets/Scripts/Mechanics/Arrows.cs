using System;
using System.Collections.Generic;

// Port of src/mechanics/Arrows.ts

public class ArrowHooks
{
    public Action<THREE.Object3D, THREE.Vector3> onHit;
    public Action onStick;
}

/// <summary>Arrows that fly in an arc, strike creatures, and stay stuck in the ground or a palm for a while.</summary>
public class Arrows
{
    private class Arrow
    {
        public THREE.Group mesh;
        public THREE.Vector3 vel;
        public bool stuck;
        public double life;
    }

    private const double GRAVITY = 7;
    private const double TRUNK_HEIGHT = 5.5;
    private const int MAX_ARROWS = 40;

    /// <summary>An arrow with its tip along +z, so `lookAt` aims it along its flight.</summary>
    public static THREE.Group buildArrow()
    {
        var g = new THREE.Group();
        var wood = new THREE.MeshStandardMaterial { color = 0xc9a46a, roughness = 0.7 };
        var steel = new THREE.MeshStandardMaterial { color = 0xb9bec4, roughness = 0.3, metalness = 0.9 };
        var feather = new THREE.MeshStandardMaterial { color = 0xe8e2d4, roughness = 0.9, side = THREE.Side.DoubleSide };
        var accent = new THREE.MeshStandardMaterial { color = 0xd2452f, roughness = 0.8, side = THREE.Side.DoubleSide };
        var shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.008, 0.008, 0.8, 6), wood);
        shaft.rotation.x = Math.PI / 2;
        var head = new THREE.Mesh(new THREE.ConeGeometry(0.022, 0.1, 6), steel);
        head.rotation.x = Math.PI / 2;
        head.position.z = 0.45;
        g.add(shaft, head);
        for (int i = 0; i < 3; i++)
        {
            var f = new THREE.Mesh(new THREE.PlaneGeometry(0.03, 0.14), i == 0 ? accent : feather);
            f.rotation.set(Math.PI / 2, 0, 0);
            f.position.set(0, 0, -0.3);
            f.rotation.z = 0;
            var holder = new THREE.Group();
            holder.rotation.z = i * Math.PI * 2 / 3;
            f.position.y = 0.017;
            f.rotation.set(Math.PI / 2, 0, Math.PI / 2);
            holder.add(f);
            g.add(holder);
        }
        g.traverse(o => { if (o is THREE.Mesh m) m.castShadow = true; });
        return g;
    }

    public readonly THREE.Group group = new THREE.Group();
    private readonly List<Arrow> arrows = new List<Arrow>();
    private readonly THREE.Vector3 a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3(), tmp = new THREE.Vector3();

    /// <summary>true while anything is flying or stuck in flight, so targets are only gathered when they can be hit</summary>
    public bool busy => arrows.Count > 0;

    public void spawn(THREE.Vector3 origin, THREE.Vector3 dir, double speed)
    {
        var mesh = buildArrow();
        mesh.position.copy(origin);
        mesh.lookAt(origin.clone().add(dir));
        group.add(mesh);
        arrows.Add(new Arrow { mesh = mesh, vel = dir.clone().multiplyScalar(speed), stuck = false, life = 0 });
        while (arrows.Count > MAX_ARROWS) remove(0);
    }

    private void remove(int i)
    {
        group.remove(arrows[i].mesh);
        arrows.RemoveAt(i);
    }

    private double segDist(THREE.Vector3 pa, THREE.Vector3 pb, THREE.Vector3 p)
    {
        c.copy(pb).sub(pa);
        double len2 = JS.Or(c.lengthSq(), 1e-6);
        double t = Math.Max(0, Math.Min(1, b.copy(p).sub(pa).dot(c) / len2));
        return b.copy(pa).addScaledVector(c, t).distanceTo(p);
    }

    public void update(double dt, List<THREE.Object3D> targets, Beach beach, List<Scenery.Trunk> trunks, ArrowHooks hooks)
    {
        for (int i = arrows.Count - 1; i >= 0; i--)
        {
            var ar = arrows[i];
            ar.life += dt;
            if (ar.stuck)
            {
                // arrows that stay in the world shrink away after a while
                double left = 22 - ar.life;
                if (left <= 0) remove(i);
                else if (left < 1.5) ar.mesh.scale.setScalar(Math.Max(0.01, left / 1.5));
                continue;
            }
            var prev = a.copy(ar.mesh.position);
            ar.vel.y -= GRAVITY * dt;
            ar.mesh.position.addScaledVector(ar.vel, dt);
            ar.mesh.lookAt(tmp.copy(ar.mesh.position).add(ar.vel));

            bool done = false;
            var world = new THREE.Vector3();
            foreach (var t in targets)
            {
                t.getWorldPosition(world);
                if (segDist(prev, ar.mesh.position, world) < t.userData.Num("hitRadius", 0.42))
                {
                    hooks.onHit(t, ar.mesh.position.clone());
                    remove(i);
                    done = true;
                    break;
                }
            }
            if (done) continue;

            var p = ar.mesh.position;
            foreach (var t in trunks)
            {
                if (JS.Hypot(p.x - t.x, p.z - t.z) < 0.32 && p.y < beach.heightAt(t.x, t.z) + TRUNK_HEIGHT)
                {
                    ar.stuck = true;
                    ar.life = 0;
                    hooks.onStick();
                    break;
                }
            }
            if (!ar.stuck && p.y <= beach.heightAt(p.x, p.z) + 0.04)
            {
                ar.stuck = true;
                ar.life = 0;
                p.y = beach.heightAt(p.x, p.z) + 0.04;
                hooks.onStick();
            }
            else if (!ar.stuck && (Math.Abs(p.x) > 240 || Math.Abs(p.z) > 240 || ar.life > 8)) remove(i);
        }
    }
}
