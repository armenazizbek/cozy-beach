import * as THREE from 'three';
import { WET_Z } from './Beach';
import type { Beach } from './Beach';

export interface CastleData {
  x: number;
  z: number;
  stage: number;
  hp: number;
  /** picks the design, so a loaded castle looks the way it did */
  seed?: number;
}

export interface Castle {
  group: THREE.Group;
  /** an invisible marker bullets and arrows can hit */
  target: THREE.Object3D;
  x: number;
  z: number;
  stage: number;
  /** 1 fresh, 0 gone: rain, the sea and shots wear it down */
  hp: number;
  seed: number;
  style: string;
  /** the parts of each stage, as pivots that sit on the sand so they can grow upward */
  parts: THREE.Group[][];
  /** parts that are still rising, with how far they have got (0..1) */
  rising: { pivot: THREE.Group; t: number }[];
  /** chunks that have been shot off and are tumbling away */
  falling: { pivot: THREE.Group; v: THREE.Vector3; spin: THREE.Vector3; t: number }[];
  mats: THREE.MeshStandardMaterial[];
  wet: number;
}

const BASE = new THREE.Color(0xdcc38f);
const WET = new THREE.Color(0xa88a5c);
const GATE_COLOR = 0x7a6038;
export const MAX_CASTLES = 6;
const STYLES = ['round', 'keep', 'spire', 'step'] as const;
const FLAGS = [0xe0452f, 0x2f78d0, 0xf2b632, 0x35a86a, 0xc23bb0];

const seeded = (seed: number) => {
  // small seeds make the first few values of this simple generator alike, so mix the seed up and skip a few
  seed = (Math.imul(seed ^ 0x9e3779b9, 2654435761) >>> 0) % 4294967296;
  const next = (): number => {
    seed = (seed * 1664525 + 1013904223) % 4294967296;
    return seed / 4294967296;
  };
  for (let i = 0; i < 4; i++) next();
  return next;
};

const jitter = (geo: THREE.BufferGeometry, amount: number): THREE.BufferGeometry => {
  const p = geo.attributes.position as THREE.BufferAttribute;
  const seen = new Map<string, [number, number, number]>();
  for (let i = 0; i < p.count; i++) {
    const key = `${Math.round(p.getX(i) * 500)},${Math.round(p.getY(i) * 500)},${Math.round(p.getZ(i) * 500)}`;
    let d = seen.get(key);
    if (!d) {
      d = [(Math.random() - 0.5) * amount, (Math.random() - 0.5) * amount * 0.6, (Math.random() - 0.5) * amount];
      seen.set(key, d);
    }
    p.setXYZ(i, p.getX(i) + d[0], p.getY(i) + d[1], p.getZ(i) + d[2]);
  }
  geo.computeVertexNormals();
  return geo;
};

/**
 * Little sand castles the player builds with a bucket, in three stages and a different design each time. Rain, the sea and
 * gunfire wear them down: a shot knocks chunks off, and enough damage brings the whole thing down.
 */
export class Sandcastles {
  readonly group = new THREE.Group();
  castles: Castle[] = [];
  /** solid circles for walking and driving around */
  readonly colliders: { x: number; z: number; r: number }[] = [];

  constructor(private beach: Beach) {}

  nearest(x: number, z: number, reach: number): Castle | null {
    let best: Castle | null = null;
    let bd = reach;
    for (const c of this.castles) {
      const d = Math.hypot(c.x - x, c.z - z);
      if (d < bd) {
        bd = d;
        best = c;
      }
    }
    return best;
  }

  get count(): number {
    return this.castles.length;
  }

  /** What bullets and arrows can hit. */
  targets(): THREE.Object3D[] {
    return this.castles.map((c) => c.target);
  }

  castleOf(obj: THREE.Object3D): Castle | null {
    return this.castles.find((c) => c.target === obj) ?? null;
  }

  private refreshColliders(): void {
    this.colliders.length = 0;
    for (const c of this.castles) this.colliders.push({ x: c.x, z: c.z, r: 0.95 });
  }

  private build(c: Castle): void {
    const rand = seeded(c.seed);
    const pick = <T,>(list: readonly T[]): T => list[Math.floor(rand() * list.length)];
    const between = (a: number, b: number): number => a + rand() * (b - a);
    c.style = pick(STYLES);
    const tall = between(0.8, 1.3);

    const mat = new THREE.MeshStandardMaterial({ color: BASE.clone().offsetHSL((rand() - 0.5) * 0.012, 0, (rand() - 0.5) * 0.03), roughness: 1, flatShading: true });
    c.mats.push(mat);
    const dark = new THREE.MeshStandardMaterial({ color: GATE_COLOR, roughness: 1 });
    const flagMat = new THREE.MeshStandardMaterial({ color: pick(FLAGS), roughness: 0.6, side: THREE.DoubleSide });

    // a part is a pivot on the sand with the mesh standing on it, so scaling the pivot makes the part grow upward
    const part = (stage: number, geo: THREE.BufferGeometry, m: THREE.Material, x: number, y: number, z: number, lift: number): THREE.Group => {
      const pivot = new THREE.Group();
      pivot.position.set(x, y, z);
      const mesh = new THREE.Mesh(geo, m);
      mesh.position.y = lift;
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      pivot.add(mesh);
      pivot.visible = false;
      c.group.add(pivot);
      c.parts[stage - 1].push(pivot);
      return pivot;
    };
    const cyl = (r0: number, r1: number, h: number, seg = 14): THREE.BufferGeometry => jitter(new THREE.CylinderGeometry(r0, r1, h, seg, 1), 0.03);
    const box = (w: number, h: number, d: number): THREE.BufferGeometry => jitter(new THREE.BoxGeometry(w, h, d), 0.03);
    const pyramid = (r: number, h: number): THREE.BufferGeometry => new THREE.ConeGeometry(r, h, 4).rotateY(Math.PI / 4);
    const flag = (x: number, y: number, z: number): void => {
      const pole = part(3, new THREE.CylinderGeometry(0.014, 0.014, 0.55, 6), mat, x, y, z, 0.275);
      const cloth = new THREE.Mesh(new THREE.PlaneGeometry(0.28, 0.17), flagMat);
      cloth.position.set(0.15, 0.45, 0);
      pole.add(cloth);
    };

    // stage 1 is the same for every castle: a wide base and a rounded mound
    part(1, jitter(new THREE.CylinderGeometry(1.05, 1.18, 0.14, 26, 1), 0.04), mat, 0, 0, 0, 0.07);
    part(1, jitter(new THREE.SphereGeometry(0.78, 20, 10, 0, Math.PI * 2, 0, Math.PI / 2), 0.05), mat, 0, 0.1, 0, 0).scale.set(1, 0.7, 1);

    if (c.style === 'round') {
      // a ring wall with battlements, a round keep, and three to five round towers
      const N = 10 + Math.floor(rand() * 7);
      for (let i = 0; i < N; i++) {
        const a = (i / N) * Math.PI * 2;
        const w = part(2, box((Math.PI * 2 * 0.86) / N + 0.06, 0.36, 0.15), mat, Math.cos(a) * 0.86, 0.12, Math.sin(a) * 0.86, 0.18);
        w.rotation.y = -a + Math.PI / 2;
        if (i % 2 === 0) part(2, box(0.15, 0.12, 0.15), mat, Math.cos(a) * 0.86, 0.48, Math.sin(a) * 0.86, 0.06).rotation.y = -a + Math.PI / 2;
      }
      part(2, cyl(0.3, 0.38, 0.85 * tall), mat, 0, 0.12, 0, 0.425 * tall);
      part(2, cyl(0.4, 0.34, 0.12), mat, 0, 0.12 + 0.85 * tall, 0, 0.06);
      const towers = 3 + Math.floor(rand() * 3);
      const start = rand() * Math.PI;
      const cones = rand() < 0.6;
      for (let k = 0; k < towers; k++) {
        const a = start + (k * Math.PI * 2) / towers;
        const x = Math.cos(a) * 0.93;
        const z = Math.sin(a) * 0.93;
        const th = 0.62 + rand() * 0.3;
        part(3, cyl(0.19, 0.25, th), mat, x, 0.12, z, th / 2);
        part(3, cyl(0.27, 0.22, 0.1), mat, x, 0.12 + th, z, 0.05);
        if (cones) part(3, new THREE.ConeGeometry(0.2, 0.3, 10), mat, x, 0.22 + th, z, 0.15);
      }
      part(3, box(0.3, 0.3, 0.12), dark, 0, 0.12, 0.9, 0.15);
      flag(0, 0.12 + 0.97 * tall, 0);
    } else if (c.style === 'keep') {
      // square walls and a square keep, with a little pyramid roof on each corner tower
      for (const side of [0, 1, 2, 3]) {
        const a = (side * Math.PI) / 2;
        const px = Math.cos(a) * 0.85;
        const pz = Math.sin(a) * 0.85;
        part(2, box(1.75, 0.36, 0.16), mat, px, 0.12, pz, 0.18).rotation.y = -a + Math.PI / 2;
        for (let i = -2; i <= 2; i += 1) {
          if (i % 2) continue;
          const m = part(2, box(0.16, 0.12, 0.16), mat, px + Math.cos(a + Math.PI / 2) * i * 0.34, 0.48, pz + Math.sin(a + Math.PI / 2) * i * 0.34, 0.06);
          m.rotation.y = -a + Math.PI / 2;
        }
      }
      part(2, box(0.58, 0.95 * tall, 0.58), mat, 0, 0.12, 0, 0.475 * tall);
      for (const [dx, dz] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) part(2, box(0.14, 0.12, 0.14), mat, dx * 0.22, 0.12 + 0.95 * tall, dz * 0.22, 0.06);
      for (const [dx, dz] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
        const th = 0.66 + rand() * 0.25;
        part(3, box(0.34, th, 0.34), mat, dx * 0.85, 0.12, dz * 0.85, th / 2);
        part(3, pyramid(0.3, 0.32), mat, dx * 0.85, 0.12 + th, dz * 0.85, 0.16);
      }
      part(3, box(0.34, 0.32, 0.12), dark, 0, 0.12, 0.88, 0.16);
      flag(0, 0.12 + 0.95 * tall + 0.12, 0);
    } else if (c.style === 'spire') {
      // a tall central spire with a ring of little turrets, and a low wall around them
      const N = 12;
      for (let i = 0; i < N; i++) {
        const a = (i / N) * Math.PI * 2;
        part(2, box(0.5, 0.2, 0.14), mat, Math.cos(a) * 0.88, 0.12, Math.sin(a) * 0.88, 0.1).rotation.y = -a + Math.PI / 2;
      }
      part(2, cyl(0.12, 0.36, 1.4 * tall, 16), mat, 0, 0.12, 0, 0.7 * tall);
      part(2, new THREE.ConeGeometry(0.2, 0.55 * tall, 14), mat, 0, 0.12 + 1.4 * tall, 0, 0.275 * tall);
      const turrets = 5 + Math.floor(rand() * 3);
      for (let k = 0; k < turrets; k++) {
        const a = (k / turrets) * Math.PI * 2 + 0.3;
        const th = 0.48 + rand() * 0.25;
        part(2, cyl(0.1, 0.14, th), mat, Math.cos(a) * 0.7, 0.12, Math.sin(a) * 0.7, th / 2);
        part(3, new THREE.ConeGeometry(0.15, 0.24, 8), mat, Math.cos(a) * 0.7, 0.12 + th, Math.sin(a) * 0.7, 0.12);
      }
      part(3, box(0.32, 0.26, 0.14), dark, 0, 0.12, 0.9, 0.13);
      part(3, box(0.5, 0.06, 0.34), mat, 0, 0.12, 1.05, 0.03); // a little bridge out front
      flag(0, 0.12 + 1.4 * tall + 0.52 * tall, 0);
    } else {
      // a stepped pyramid with a stair up the front and small towers on the lowest step
      part(2, box(1.55, 0.34, 1.55), mat, 0, 0.12, 0, 0.17);
      part(2, box(1.1, 0.34, 1.1), mat, 0, 0.46, 0, 0.17);
      part(3, box(0.62, 0.4 * tall, 0.62), mat, 0, 0.8, 0, 0.2 * tall);
      for (let s = 0; s < 4; s++) part(3, box(0.34, 0.1 + s * 0.1, 0.16), mat, 0, 0.12, 0.86 + (3 - s) * 0.16, (0.1 + s * 0.1) / 2);
      for (const [dx, dz] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
        part(3, cyl(0.1, 0.14, 0.5), mat, dx * 0.62, 0.46, dz * 0.62, 0.25);
        part(3, new THREE.ConeGeometry(0.13, 0.2, 8), mat, dx * 0.62, 0.96, dz * 0.62, 0.1);
      }
      flag(0, 0.8 + 0.4 * tall, 0);
    }
  }

  add(x: number, z: number, stage = 1, hp = 1, seed = Math.floor(Math.random() * 1e9)): Castle {
    const c: Castle = {
      group: new THREE.Group(), target: new THREE.Object3D(), x, z, stage: 0, hp, seed, style: 'round',
      parts: [[], [], []], rising: [], falling: [], mats: [], wet: 0,
    };
    c.group.position.set(x, this.beach.heightAt(x, z) - 0.03, z);
    c.group.rotation.y = rand01(seed) * Math.PI * 2;
    this.build(c);
    c.target.position.set(0, 0.55, 0);
    c.target.userData.hitRadius = 1.0;
    c.group.add(c.target);
    this.group.add(c.group);
    this.castles.push(c);
    for (let s = 1; s <= stage; s++) this.raise(c, s, hp === 1 && stage === 1);
    this.refreshColliders();
    return c;
  }

  /** Makes a stage appear. With `animate` the parts rise out of the sand. */
  private raise(c: Castle, stage: number, animate: boolean): void {
    c.stage = Math.max(c.stage, stage);
    for (const pivot of c.parts[stage - 1]) {
      pivot.visible = true;
      if (animate) {
        pivot.userData.sy = pivot.scale.y;
        pivot.scale.y = 0.01;
        c.rising.push({ pivot, t: -Math.random() * 0.25 });
      }
    }
  }

  /** Adds the next stage to a castle. Returns false if it is already finished. */
  upgrade(c: Castle): boolean {
    if (c.stage >= 3) return false;
    this.raise(c, c.stage + 1, true);
    return true;
  }

  remove(c: Castle): void {
    this.group.remove(c.group);
    this.castles = this.castles.filter((k) => k !== c);
    this.refreshColliders();
  }

  /**
   * A shot lands. Knocks the chunks nearest to `at` off the castle and wears it down. Returns true if the whole castle came down.
   */
  hit(c: Castle, at: THREE.Vector3 | undefined, power: number): boolean {
    c.hp -= power;
    const world = new THREE.Vector3();
    const live = [...c.parts[1], ...c.parts[2]].filter((p) => p.visible && !c.falling.some((f) => f.pivot === p));
    const from = at ?? c.group.getWorldPosition(new THREE.Vector3());
    live.sort((a, b) => a.getWorldPosition(world).distanceTo(from) - b.getWorldPosition(world).distanceTo(from));
    const knock = power >= 0.18 ? 3 : 2;
    for (const p of live.slice(0, knock)) {
      c.falling.push({
        pivot: p,
        v: new THREE.Vector3((Math.random() - 0.5) * 2.2, 1.6 + Math.random() * 1.4, (Math.random() - 0.5) * 2.2),
        spin: new THREE.Vector3((Math.random() - 0.5) * 6, (Math.random() - 0.5) * 6, (Math.random() - 0.5) * 6),
        t: 0,
      });
    }
    // nothing but the bare mound left of a built-up castle counts as ruined
    const standing = live.length - Math.min(knock, live.length);
    return c.hp <= 0 || (c.stage >= 2 && standing <= 0);
  }

  /** Rain, the shore and tumbling chunks; `onWash` is told about each castle that is lost to the weather or the sea. */
  update(dt: number, rain: number, onWash: (x: number, z: number, byRain: boolean) => void): void {
    for (const c of [...this.castles]) {
      for (let i = c.rising.length - 1; i >= 0; i--) {
        const r = c.rising[i];
        r.t += dt / 0.8;
        if (r.t < 0) continue;
        const e = Math.min(1, r.t);
        r.pivot.scale.y = Math.max(0.01, (r.pivot.userData.sy ?? 1) * (1 - Math.pow(1 - e, 3)));
        if (e >= 1) c.rising.splice(i, 1);
      }
      // chunks that were shot off fly away and disappear
      for (let i = c.falling.length - 1; i >= 0; i--) {
        const f = c.falling[i];
        f.t += dt;
        f.v.y -= 9 * dt;
        f.pivot.position.addScaledVector(f.v, dt);
        f.pivot.rotation.x += f.spin.x * dt;
        f.pivot.rotation.y += f.spin.y * dt;
        f.pivot.rotation.z += f.spin.z * dt;
        if (f.t > 0.8) {
          f.pivot.visible = false;
          c.falling.splice(i, 1);
        }
      }
      const shore = c.z > WET_Z - 1.5;
      const rainRate = rain > 0.3 ? rain / 90 : 0;
      c.hp -= dt * (rainRate + (shore ? 1 / 220 : 0));
      c.wet += ((rain > 0.3 ? 1 : 0) - c.wet) * Math.min(1, dt * 0.5);
      if (c.hp <= 0) {
        onWash(c.x, c.z, rain > 0.3);
        this.remove(c);
        continue;
      }
      // it slumps and darkens as it wears away
      const k = 0.35 + 0.65 * Math.pow(Math.max(0, c.hp), 0.7);
      c.group.scale.set(1 + (1 - k) * 0.12, k, 1 + (1 - k) * 0.12);
      for (const m of c.mats) m.color.copy(BASE).lerp(WET, Math.min(1, c.wet * 0.6 + (1 - c.hp) * 0.4));
    }
  }

  data(): CastleData[] {
    return this.castles.map((c) => ({ x: c.x, z: c.z, stage: c.stage, hp: c.hp, seed: c.seed }));
  }

  setAll(list: CastleData[]): void {
    for (const c of [...this.castles]) this.remove(c);
    for (const d of list) this.add(d.x, d.z, Math.max(1, Math.min(3, d.stage)), d.hp, d.seed);
  }
}

const rand01 = (seed: number): number => seeded(seed ^ 0x9e3779b9)();
