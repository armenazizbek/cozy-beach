import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
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
  /** the body, which rocks a little as the crab scuttles */
  body: THREE.Mesh;
}

const rand = (a: number, b: number): number => a + Math.random() * (b - a);

const Y_AXIS = new THREE.Vector3(0, 1, 0);

/** One colour for a whole piece, so the pieces of a crab can be merged into a single mesh. */
const paint = (g: THREE.BufferGeometry, color: number): THREE.BufferGeometry => {
  g.deleteAttribute('uv');
  const c = new THREE.Color(color);
  const n = (g.attributes.position as THREE.BufferAttribute).count;
  const col = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) col.set([c.r, c.g, c.b], i * 3);
  g.setAttribute('color', new THREE.BufferAttribute(col, 3));
  return g;
};

/** A tapering rod between two points: a limb. */
const limb = (a: THREE.Vector3, b: THREE.Vector3, r: number, color: number): THREE.BufferGeometry => {
  const d = b.clone().sub(a);
  const g = new THREE.CylinderGeometry(r * 0.75, r, d.length(), 6, 1);
  g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(Y_AXIS, d.clone().normalize()));
  g.translate((a.x + b.x) / 2, (a.y + b.y) / 2, (a.z + b.z) / 2);
  return paint(g, color);
};

/**
 * A shore crab, facing +z: a broad domed shell with a paler top, two eyes on stalks, two arms ending in claws with open pincers,
 * and three bent legs on each side. Built once per shell colour and shared by every crab of that colour.
 */
const crabGeometry = (shell: number, light: number, leg: number): THREE.BufferGeometry => {
  const parts: THREE.BufferGeometry[] = [];
  const v = (x: number, y: number, z: number): THREE.Vector3 => new THREE.Vector3(x, y, z);
  const body = new THREE.SphereGeometry(0.15, 18, 12);
  body.scale(1.35, 0.5, 0.95);
  body.translate(0, 0.1, 0);
  parts.push(paint(body, shell));
  const top = new THREE.SphereGeometry(0.115, 14, 8, 0, Math.PI * 2, 0, Math.PI / 2);
  top.scale(1.3, 0.55, 0.9);
  top.translate(0, 0.115, 0);
  parts.push(paint(top, light));
  // a row of small points along the front edge of the shell
  for (let i = -2; i <= 2; i++) {
    const t = new THREE.ConeGeometry(0.014, 0.04, 5);
    t.rotateX(Math.PI / 2);
    t.translate(i * 0.05, 0.1, 0.135 - Math.abs(i) * 0.012);
    parts.push(paint(t, shell));
  }
  for (const s of [-1, 1]) {
    // eyes on short stalks
    parts.push(limb(v(s * 0.045, 0.13, 0.1), v(s * 0.055, 0.185, 0.115), 0.01, light));
    const eye = new THREE.SphereGeometry(0.017, 8, 6);
    eye.translate(s * 0.055, 0.195, 0.118);
    parts.push(paint(eye, 0x141010));
    // an arm with an elbow, and a claw
    const shoulder = v(s * 0.17, 0.1, 0.07);
    const elbow = v(s * 0.26, 0.11, 0.14);
    const wrist = v(s * 0.23, 0.11, 0.23);
    parts.push(limb(shoulder, elbow, 0.02, shell), limb(elbow, wrist, 0.019, shell));
    const palm = new THREE.SphereGeometry(0.052, 10, 8);
    palm.scale(1, 0.7, 1.2);
    palm.translate(s * 0.225, 0.11, 0.265);
    parts.push(paint(palm, shell));
    for (const open of [-1, 1]) {
      const f = new THREE.ConeGeometry(0.026, 0.1, 6);
      f.rotateX(Math.PI / 2);
      f.rotateY(open * 0.28 * -s);
      f.translate(s * 0.225 + open * 0.02 * s, 0.11 + open * 0.008, 0.33);
      parts.push(paint(f, light));
    }
    // three legs, each bent at the knee and ending on the sand
    for (let i = 0; i < 3; i++) {
      const z = -0.07 + i * 0.06;
      const knee = v(s * (0.25 + i * 0.01), 0.145, z - 0.01);
      parts.push(limb(v(s * 0.15, 0.085, z), knee, 0.011, leg), limb(knee, v(s * (0.33 + i * 0.012), 0.0, z - 0.035), 0.009, leg));
    }
  }
  const merged = mergeGeometries(parts, false)!;
  merged.computeVertexNormals();
  return merged;
};

/** Reddish-orange, sandy-brown and deep red crabs. */
const CRAB_LOOKS: THREE.BufferGeometry[] = [
  crabGeometry(0xe0643f, 0xf08a5a, 0xc9502f),
  crabGeometry(0xb98a58, 0xd6b183, 0x9c7040),
  crabGeometry(0xc23e2e, 0xdc6a4c, 0x9e2c20),
];

/** Grass tufts and scuttling crabs that grow with the Beach Ecology Score. */
export class Ecology {
  readonly group = new THREE.Group();
  private tufts: THREE.Object3D[] = [];
  readonly crabs: Crab[] = [];
  private grassMat = new THREE.MeshStandardMaterial({ color: 0x6ea84f, roughness: 0.9 });
  private crabMat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.65 });

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
    const body = new THREE.Mesh(CRAB_LOOKS[Math.floor(Math.random() * CRAB_LOOKS.length)], this.crabMat);
    body.castShadow = true;
    g.scale.setScalar(rand(0.85, 1.25));
    g.add(body);
    const x = rand(-60, 60);
    const z = rand(DIG_MIN_Z + 3, WET_Z - 1);
    g.position.set(x, this.beach.heightAt(x, z), z);
    this.group.add(g);
    const hit = new THREE.Mesh(new THREE.SphereGeometry(0.42, 8, 6), new THREE.MeshBasicMaterial({ visible: false }));
    hit.position.y = 0.12;
    g.add(hit);
    const crab: Crab = { mesh: g, vx: 0, vz: 0, timer: 0, phase: Math.random() * 6, alive: true, respawn: 0, hit, body };
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
        m.position.y += Math.abs(Math.sin(c.phase)) * 0.012;
        c.body.rotation.z = Math.sin(c.phase) * 0.05;
        m.rotation.y += (Math.atan2(c.vx, c.vz) - Math.PI / 2 - m.rotation.y) * Math.min(1, dt * 4);
      }
    }
    // crabs ignore terrain edits except sinking into holes; keep them from clipping
  }
}
