using System;
using System.Collections.Generic;

// Port of src/mechanics/Bullets.ts

public class BulletHooks
{
    /// <summary>a target was struck; the bullet vanishes</summary>
    public Action<THREE.Object3D, THREE.Vector3> onHit;
    public Action<THREE.Vector3> onGround;
}

/// <summary>Visible, glowing bullets that fly across the beach and leave a short tracer trail.</summary>
public class Bullets
{
    private class Bullet
    {
        public THREE.Mesh mesh;
        public THREE.Vector3 vel;
        public double life;
    }

    private const double SPEED = 38;
    private const double GRAVITY = 3.2;
    private static readonly THREE.Color TRAIL = new THREE.Color(0xffd27a);

    public readonly THREE.Group group = new THREE.Group();
    private readonly List<Bullet> bullets = new List<Bullet>();
    private readonly ParticleSystem trail = new ParticleSystem(400, 0.09, 0, true);
    private readonly THREE.SphereGeometry geo = new THREE.SphereGeometry(0.05, 8, 6);
    private readonly THREE.MeshBasicMaterial mat = new THREE.MeshBasicMaterial { color = 0xfff4c8 };
    private readonly THREE.Vector3 a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3();

    public Bullets() { group.add(trail.points); }

    /// <summary>true while anything is flying or stuck in flight, so targets are only gathered when they can be hit</summary>
    public bool busy => bullets.Count > 0;

    public void spawn(THREE.Vector3 origin, THREE.Vector3 dir)
    {
        var mesh = new THREE.Mesh(geo, mat);
        mesh.scale.set(1, 1, 4);
        mesh.position.copy(origin);
        mesh.lookAt(origin.clone().add(dir));
        group.add(mesh);
        bullets.Add(new Bullet { mesh = mesh, vel = dir.clone().multiplyScalar(SPEED), life = 2.2 });
    }

    /// <summary>Shortest distance from point p to the segment a-b.</summary>
    private double segDist(THREE.Vector3 pa, THREE.Vector3 pb, THREE.Vector3 p)
    {
        c.copy(pb).sub(pa);
        double len2 = JS.Or(c.lengthSq(), 1e-6);
        double t = Math.Max(0, Math.Min(1, b.copy(p).sub(pa).dot(c) / len2));
        return b.copy(pa).addScaledVector(c, t).distanceTo(p);
    }

    public void update(double dt, List<THREE.Object3D> targets, Beach beach, BulletHooks hooks)
    {
        var target = new THREE.Vector3();
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            var bl = bullets[i];
            var prev = a.copy(bl.mesh.position);
            bl.vel.y -= GRAVITY * dt;
            bl.mesh.position.addScaledVector(bl.vel, dt);
            bl.mesh.lookAt(bl.mesh.position.clone().add(bl.vel));
            bl.life -= dt;

            // tracer: a handful of glowing specks along the path just travelled
            const int steps = 4;
            for (int s = 0; s < steps; s++)
            {
                var p = prev.clone().lerp(bl.mesh.position, (double)s / steps);
                trail.emit(p, new THREE.Vector3(), 0.28, TRAIL);
            }

            bool done = bl.life <= 0;
            foreach (var hit in targets)
            {
                hit.getWorldPosition(target);
                if (segDist(prev, bl.mesh.position, target) < hit.userData.Num("hitRadius", 0.42))
                {
                    hooks.onHit(hit, bl.mesh.position.clone());
                    done = true;
                    break;
                }
            }
            if (!done)
            {
                var p = bl.mesh.position;
                if (p.y <= beach.heightAt(p.x, p.z))
                {
                    hooks.onGround(p.clone());
                    done = true;
                }
                else if (Math.Abs(p.x) > 320 || Math.Abs(p.z) > 320) done = true;
            }
            if (done)
            {
                group.remove(bl.mesh);
                bullets.RemoveAt(i);
            }
        }
        trail.update(dt);
    }
}
