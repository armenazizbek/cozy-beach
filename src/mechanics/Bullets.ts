import * as THREE from 'three';
import { ParticleSystem } from '../core/Particles';
import { Beach } from '../world/Beach';

interface Bullet {
  mesh: THREE.Mesh;
  vel: THREE.Vector3;
  life: number;
}

export interface BulletHooks {
  /** a target was struck; the bullet vanishes */
  onHit: (hitObject: THREE.Object3D, at?: THREE.Vector3) => void;
  onGround: (at: THREE.Vector3) => void;
}

const SPEED = 38;
const GRAVITY = 3.2;
const TRAIL = new THREE.Color(0xffd27a);

/** Visible, glowing bullets that fly across the beach and leave a short tracer trail. */
export class Bullets {
  readonly group = new THREE.Group();
  private bullets: Bullet[] = [];
  private trail = new ParticleSystem(400, 0.09, 0, true);
  private geo = new THREE.SphereGeometry(0.05, 8, 6);
  private mat = new THREE.MeshBasicMaterial({ color: 0xfff4c8 });
  private a = new THREE.Vector3();
  private b = new THREE.Vector3();
  private c = new THREE.Vector3();

  constructor() {
    this.group.add(this.trail.points);
  }

  /** true while anything is flying or stuck in flight, so targets are only gathered when they can be hit */
  get busy(): boolean {
    return this.bullets.length > 0;
  }

  spawn(origin: THREE.Vector3, dir: THREE.Vector3): void {
    const mesh = new THREE.Mesh(this.geo, this.mat);
    mesh.scale.set(1, 1, 4);
    mesh.position.copy(origin);
    mesh.lookAt(origin.clone().add(dir));
    this.group.add(mesh);
    this.bullets.push({ mesh, vel: dir.clone().multiplyScalar(SPEED), life: 2.2 });
  }

  /** Shortest distance from point p to the segment a-b. */
  private segDist(a: THREE.Vector3, b: THREE.Vector3, p: THREE.Vector3): number {
    this.c.copy(b).sub(a);
    const len2 = this.c.lengthSq() || 1e-6;
    const t = Math.max(0, Math.min(1, this.b.copy(p).sub(a).dot(this.c) / len2));
    return this.b.copy(a).addScaledVector(this.c, t).distanceTo(p);
  }

  update(dt: number, targets: THREE.Object3D[], beach: Beach, hooks: BulletHooks): void {
    const target = new THREE.Vector3();
    for (let i = this.bullets.length - 1; i >= 0; i--) {
      const bl = this.bullets[i];
      const prev = this.a.copy(bl.mesh.position);
      bl.vel.y -= GRAVITY * dt;
      bl.mesh.position.addScaledVector(bl.vel, dt);
      bl.mesh.lookAt(bl.mesh.position.clone().add(bl.vel));
      bl.life -= dt;

      // tracer: a handful of glowing specks along the path just travelled
      const steps = 4;
      for (let s = 0; s < steps; s++) {
        const p = prev.clone().lerp(bl.mesh.position, s / steps);
        this.trail.emit(p, new THREE.Vector3(), 0.28, TRAIL);
      }

      let done = bl.life <= 0;
      for (const hit of targets) {
        hit.getWorldPosition(target);
        if (this.segDist(prev, bl.mesh.position, target) < ((hit.userData.hitRadius as number | undefined) ?? 0.42)) {
          hooks.onHit(hit, bl.mesh.position.clone());
          done = true;
          break;
        }
      }
      if (!done) {
        const p = bl.mesh.position;
        if (p.y <= beach.heightAt(p.x, p.z)) {
          hooks.onGround(p.clone());
          done = true;
        } else if (Math.abs(p.x) > 320 || Math.abs(p.z) > 320) {
          done = true;
        }
      }
      if (done) {
        this.group.remove(bl.mesh);
        this.bullets.splice(i, 1);
      }
    }
    this.trail.update(dt);
  }
}
