import * as THREE from 'three';
import { DIG_MIN_Z, HALF, SHORE_Z, WATER_LEVEL } from './Beach';
import type { Beach } from './Beach';

/** tiny deterministic PRNG so the beach looks the same every visit */
const rng = (seed: number) => () => {
  seed = (seed * 1664525 + 1013904223) % 4294967296;
  return seed / 4294967296;
};

interface Spot {
  x: number;
  z: number;
  r: number;
}

/** Stones and boulders scattered over the beach. Nothing is buried near them, so they never hide a find. */
export class Stones {
  readonly group = new THREE.Group();
  /** the bigger stones, which block walking and driving */
  readonly colliders: Spot[] = [];
  private shapes: THREE.BufferGeometry[] = [];
  private mats: THREE.MeshStandardMaterial[] = [];

  constructor(private beach: Beach) {
    const rand = rng(91);
    // a handful of lumpy rock shapes that every stone reuses at different sizes
    for (let v = 0; v < 6; v++) {
      const geo = new THREE.IcosahedronGeometry(1, 2);
      const p = geo.attributes.position as THREE.BufferAttribute;
      const seen = new Map<string, number>();
      for (let i = 0; i < p.count; i++) {
        const key = `${Math.round(p.getX(i) * 1000)},${Math.round(p.getY(i) * 1000)},${Math.round(p.getZ(i) * 1000)}`;
        let k = seen.get(key);
        if (k === undefined) {
          k = 0.82 + rand() * 0.34;
          seen.set(key, k);
        }
        // squashed and lumpy, with a flatter bottom so it sits on the sand
        p.setXYZ(i, p.getX(i) * k * 1.12, Math.max(p.getY(i), -0.45) * k * 0.72, p.getZ(i) * k);
      }
      geo.computeVertexNormals();
      this.shapes.push(geo);
    }
    for (const c of [0x7a7872, 0x65635e, 0x948b7c, 0x5c5a57, 0x877c6b, 0x726859]) {
      this.mats.push(new THREE.MeshStandardMaterial({ color: c, roughness: 0.95, flatShading: true }));
    }
  }

  /**
   * Lays the stones down, keeping every one well clear of `keepClear` (buried finds, the camp, the cart and so on) so that a
   * stone can never sit on top of something the player is meant to dig up.
   */
  scatter(keepClear: Spot[]): void {
    for (const m of [...this.group.children]) this.group.remove(m);
    this.colliders.length = 0;
    const rand = rng(1234);
    const placed: Spot[] = [];
    const ok = (x: number, z: number, r: number): boolean => {
      if (Math.abs(x) > HALF - 3 || z > SHORE_Z - 6 || z < -(HALF - 4)) return false;
      const h = this.beach.heightAt(x, z);
      if (h < WATER_LEVEL + 0.35) return false;
      // finds, camp and so on: a generous margin so the stone is never over something buried
      if (keepClear.some((k) => Math.hypot(k.x - x, k.z - z) < k.r + r + 1.4)) return false;
      if (placed.some((s) => Math.hypot(s.x - x, s.z - z) < s.r + r + 0.15)) return false;
      return true;
    };
    const lay = (x: number, z: number, r: number): void => {
      const mesh = new THREE.Mesh(this.shapes[Math.floor(rand() * this.shapes.length)], this.mats[Math.floor(rand() * this.mats.length)]);
      mesh.scale.set(r * (0.9 + rand() * 0.3), r * (0.75 + rand() * 0.3), r * (0.9 + rand() * 0.3));
      mesh.rotation.y = rand() * Math.PI * 2;
      // sunk a little into the sand
      mesh.position.set(x, this.beach.heightAt(x, z) + r * 0.12, z);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      this.group.add(mesh);
      placed.push({ x, z, r });
      if (r >= 0.42) this.colliders.push({ x, z, r: r * 0.85 });
    };

    // a few big boulders at random spots over the whole beach, not too close to each other
    const x0 = -(HALF - 8);
    const z0 = -42;
    const z1 = SHORE_Z - 9;
    const spots: Spot[] = [];
    for (let tries = 0; spots.length < 6 && tries < 600; tries++) {
      const r = 0.9 + Math.random() * 0.7;
      const x = x0 + Math.random() * 2 * (HALF - 8);
      const z = z0 + Math.random() * (z1 - z0);
      if (spots.some((q) => Math.hypot(q.x - x, q.z - z) < 14)) continue;
      if (!ok(x, z, r)) continue;
      lay(x, z, r);
      spots.push({ x, z, r });
    }
  }
}
