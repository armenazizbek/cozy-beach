import * as THREE from 'three';
import { Beach, CAMP_Z, DIG_MIN_Z, WET_Z } from './Beach';

interface Crab {
  mesh: THREE.Group;
  vx: number;
  vz: number;
  timer: number;
  phase: number;
  alive: boolean;
  respawn: number;
  hit: THREE.Mesh;
}

const rand = (a: number, b: number): number => a + Math.random() * (b - a);

/** Grass tufts and scuttling crabs that grow with the Beach Ecology Score. */
export class Ecology {
  readonly group = new THREE.Group();
  private tufts: THREE.Object3D[] = [];
  readonly crabs: Crab[] = [];
  private grassMat = new THREE.MeshStandardMaterial({ color: 0x6ea84f, roughness: 0.9 });
  private crabMat = new THREE.MeshStandardMaterial({ color: 0xe8704f, roughness: 0.7 });
  private eyeMat = new THREE.MeshStandardMaterial({ color: 0x111111 });

  constructor(private beach: Beach) {}

  setScore(score: number): void {
    const wantTufts = Math.min(260, score * 4);
    while (this.tufts.length < wantTufts) this.addTuft();
    const wantCrabs = 26 + Math.min(10, Math.floor(score / 2));
    while (this.crabs.length < wantCrabs) this.addCrab();
  }

  private addTuft(): void {
    const g = new THREE.Group();
    const blades = 5 + Math.floor(Math.random() * 4);
    for (let i = 0; i < blades; i++) {
      const b = new THREE.Mesh(new THREE.ConeGeometry(0.035, rand(0.22, 0.4), 4), this.grassMat);
      b.position.set(rand(-0.08, 0.08), 0.15, rand(-0.08, 0.08));
      b.rotation.set(rand(-0.4, 0.4), 0, rand(-0.4, 0.4));
      b.castShadow = true;
      g.add(b);
    }
    // near the dunes mostly, and a few mid-beach
    const x = rand(-66, 66);
    const z = Math.random() < 0.7 ? rand(DIG_MIN_Z - 6, DIG_MIN_Z + 4) : rand(-40, 26);
    if (Math.hypot(x, z - CAMP_Z) < 13) {
      g.position.set(x, this.beach.heightAt(x, DIG_MIN_Z + 6), DIG_MIN_Z + 6);
    } else {
      g.position.set(x, this.beach.heightAt(x, z) - 0.02, z);
    }
    this.group.add(g);
    this.tufts.push(g);
  }

  private addCrab(): void {
    const g = new THREE.Group();
    const body = new THREE.Mesh(new THREE.SphereGeometry(0.14, 12, 8), this.crabMat);
    body.scale.set(1.3, 0.55, 1);
    body.position.y = 0.09;
    g.add(body);
    for (const s of [-1, 1]) {
      const eye = new THREE.Mesh(new THREE.SphereGeometry(0.025, 6, 6), this.eyeMat);
      eye.position.set(s * 0.06, 0.19, 0.1);
      g.add(eye);
      const claw = new THREE.Mesh(new THREE.SphereGeometry(0.06, 8, 6), this.crabMat);
      claw.position.set(s * 0.25, 0.1, 0.1);
      g.add(claw);
      for (let l = 0; l < 3; l++) {
        const leg = new THREE.Mesh(new THREE.CylinderGeometry(0.01, 0.01, 0.14, 4), this.crabMat);
        leg.position.set(s * 0.19, 0.05, -0.04 + l * 0.06);
        leg.rotation.z = s * 0.9;
        g.add(leg);
      }
    }
    g.traverse((o) => (o.castShadow = true));
    const x = rand(-60, 60);
    const z = rand(DIG_MIN_Z + 3, WET_Z - 1);
    g.position.set(x, this.beach.heightAt(x, z), z);
    this.group.add(g);
    const hit = new THREE.Mesh(new THREE.SphereGeometry(0.42, 8, 6), new THREE.MeshBasicMaterial({ visible: false }));
    hit.position.y = 0.12;
    g.add(hit);
    const crab: Crab = { mesh: g, vx: 0, vz: 0, timer: 0, phase: Math.random() * 6, alive: true, respawn: 0, hit };
    hit.userData.crab = crab;
    this.crabs.push(crab);
  }

  /** Invisible hit spheres of the crabs that are still alive. */
  crabTargets(): THREE.Object3D[] {
    return this.crabs.filter((c) => c.alive && c.mesh.visible).map((c) => c.hit);
  }

  /** Removes a crab for a while; returns where it was. */
  shoot(obj: THREE.Object3D): THREE.Vector3 | null {
    const crab = obj.userData.crab as Crab | undefined;
    if (!crab || !crab.alive) return null;
    crab.alive = false;
    crab.respawn = 25;
    crab.mesh.visible = false;
    return crab.mesh.position.clone();
  }

  update(dt: number, px = 1e9, pz = 1e9): void {
    for (const c of this.crabs) {
      if (!c.alive) {
        c.respawn -= dt;
        if (c.respawn <= 0) {
          const x = rand(-60, 60);
          const z = rand(DIG_MIN_Z + 3, WET_Z - 1);
          c.mesh.position.set(x, this.beach.heightAt(x, z), z);
          c.mesh.visible = true;
          c.alive = true;
        }
        continue;
      }
      // scuttle away from a player who comes too close
      const dxp = c.mesh.position.x - px;
      const dzp = c.mesh.position.z - pz;
      const dp = Math.hypot(dxp, dzp);
      if (dp < 5) {
        c.vx = (dxp / (dp || 1)) * 2.4;
        c.vz = (dzp / (dp || 1)) * 2.4;
        c.timer = 0.5;
      }
      c.timer -= dt;
      if (c.timer <= 0) {
        c.timer = rand(0.6, 2.4);
        if (Math.random() < 0.4) {
          c.vx = 0;
          c.vz = 0;
        } else {
          const a = Math.random() * Math.PI * 2;
          const s = rand(0.5, 1.1);
          c.vx = Math.cos(a) * s;
          c.vz = Math.sin(a) * s * 0.5;
        }
      }
      const m = c.mesh;
      let nx = m.position.x + c.vx * dt;
      let nz = m.position.z + c.vz * dt;
      if (nz > WET_Z + 1.5 || nz < DIG_MIN_Z + 1 || Math.abs(nx) > 66) {
        c.vx *= -1;
        c.vz *= -1;
        nx = m.position.x;
        nz = m.position.z;
      }
      m.position.set(nx, this.beach.heightAt(nx, nz), nz);
      // a crab this far off is a speck: do not draw it
      m.visible = dp < 70;
      const moving = Math.abs(c.vx) + Math.abs(c.vz) > 0.01;
      if (moving) {
        c.phase += dt * 22;
        m.position.y += Math.abs(Math.sin(c.phase)) * 0.015;
        m.rotation.y += (Math.atan2(c.vx, c.vz) - Math.PI / 2 - m.rotation.y) * Math.min(1, dt * 4);
      }
    }
    // crabs ignore terrain edits except sinking into holes; keep them from clipping
  }
}
