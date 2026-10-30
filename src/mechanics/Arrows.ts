import * as THREE from 'three';
import { Beach } from '../world/Beach';

interface Arrow {
  mesh: THREE.Group;
  vel: THREE.Vector3;
  stuck: boolean;
  life: number;
}

export interface ArrowHooks {
  onHit: (hitObject: THREE.Object3D, at?: THREE.Vector3) => void;
  onStick: () => void;
}

const GRAVITY = 7;
const TRUNK_HEIGHT = 5.5;
const MAX_ARROWS = 40;

/** An arrow with its tip along +z, so `lookAt` aims it along its flight. */
export const buildArrow = (): THREE.Group => {
  const g = new THREE.Group();
  const wood = new THREE.MeshStandardMaterial({ color: 0xc9a46a, roughness: 0.7 });
  const steel = new THREE.MeshStandardMaterial({ color: 0xb9bec4, roughness: 0.3, metalness: 0.9 });
  const feather = new THREE.MeshStandardMaterial({ color: 0xe8e2d4, roughness: 0.9, side: THREE.DoubleSide });
  const accent = new THREE.MeshStandardMaterial({ color: 0xd2452f, roughness: 0.8, side: THREE.DoubleSide });
  const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.008, 0.008, 0.8, 6), wood);
  shaft.rotation.x = Math.PI / 2;
  const head = new THREE.Mesh(new THREE.ConeGeometry(0.022, 0.1, 6), steel);
  head.rotation.x = Math.PI / 2;
  head.position.z = 0.45;
  g.add(shaft, head);
  for (let i = 0; i < 3; i++) {
    const f = new THREE.Mesh(new THREE.PlaneGeometry(0.03, 0.14), i === 0 ? accent : feather);
    f.rotation.set(Math.PI / 2, 0, 0);
    f.position.set(0, 0, -0.3);
    f.rotation.z = 0;
    const holder = new THREE.Group();
    holder.rotation.z = (i * Math.PI * 2) / 3;
    f.position.y = 0.017;
    f.rotation.set(Math.PI / 2, 0, Math.PI / 2);
    holder.add(f);
    g.add(holder);
  }
  g.traverse((o) => ((o as THREE.Mesh).isMesh ? ((o as THREE.Mesh).castShadow = true) : undefined));
  return g;
};

/** Arrows that fly in an arc, strike creatures, and stay stuck in the ground or a palm for a while. */
export class Arrows {
  readonly group = new THREE.Group();
  private arrows: Arrow[] = [];
  private a = new THREE.Vector3();
  private b = new THREE.Vector3();
  private c = new THREE.Vector3();
  private tmp = new THREE.Vector3();

  spawn(origin: THREE.Vector3, dir: THREE.Vector3, speed: number): void {
    const mesh = buildArrow();
    mesh.position.copy(origin);
    mesh.lookAt(origin.clone().add(dir));
    this.group.add(mesh);
    this.arrows.push({ mesh, vel: dir.clone().multiplyScalar(speed), stuck: false, life: 0 });
    while (this.arrows.length > MAX_ARROWS) this.remove(0);
  }

  private remove(i: number): void {
    this.group.remove(this.arrows[i].mesh);
    this.arrows.splice(i, 1);
  }

  private segDist(a: THREE.Vector3, b: THREE.Vector3, p: THREE.Vector3): number {
    this.c.copy(b).sub(a);
    const len2 = this.c.lengthSq() || 1e-6;
    const t = Math.max(0, Math.min(1, this.b.copy(p).sub(a).dot(this.c) / len2));
    return this.b.copy(a).addScaledVector(this.c, t).distanceTo(p);
  }

  update(dt: number, targets: THREE.Object3D[], beach: Beach, trunks: { x: number; z: number }[], hooks: ArrowHooks): void {
    for (let i = this.arrows.length - 1; i >= 0; i--) {
      const ar = this.arrows[i];
      ar.life += dt;
      if (ar.stuck) {
        // arrows that stay in the world shrink away after a while
        const left = 22 - ar.life;
        if (left <= 0) this.remove(i);
        else if (left < 1.5) ar.mesh.scale.setScalar(Math.max(0.01, left / 1.5));
        continue;
      }
      const prev = this.a.copy(ar.mesh.position);
      ar.vel.y -= GRAVITY * dt;
      ar.mesh.position.addScaledVector(ar.vel, dt);
      ar.mesh.lookAt(this.tmp.copy(ar.mesh.position).add(ar.vel));

      let done = false;
      const world = new THREE.Vector3();
      for (const t of targets) {
        t.getWorldPosition(world);
        if (this.segDist(prev, ar.mesh.position, world) < ((t.userData.hitRadius as number | undefined) ?? 0.42)) {
          hooks.onHit(t, ar.mesh.position.clone());
          this.remove(i);
          done = true;
          break;
        }
      }
      if (done) continue;

      const p = ar.mesh.position;
      for (const t of trunks) {
        if (Math.hypot(p.x - t.x, p.z - t.z) < 0.32 && p.y < beach.heightAt(t.x, t.z) + TRUNK_HEIGHT) {
          ar.stuck = true;
          ar.life = 0;
          hooks.onStick();
          break;
        }
      }
      if (!ar.stuck && p.y <= beach.heightAt(p.x, p.z) + 0.04) {
        ar.stuck = true;
        ar.life = 0;
        p.y = beach.heightAt(p.x, p.z) + 0.04;
        hooks.onStick();
      } else if (!ar.stuck && (Math.abs(p.x) > 240 || Math.abs(p.z) > 240 || ar.life > 8)) {
        this.remove(i);
      }
    }
  }
}
