import * as THREE from 'three';
import { HALF, SHORE_Z } from './Beach';
import type { Beach } from './Beach';
import type { BuriedItem } from './LootSpawner';

export type DogState = 'rest' | 'run' | 'find' | 'return';

export interface DogEnv {
  playerX: number;
  playerZ: number;
  /** keeps the dog out of palms, camp props and the sea */
  resolve: (x: number, z: number) => { x: number; z: number };
  /** distance from the player, for the volume */
  bark: (distance: number) => void;
  dust: (at: THREE.Vector3) => void;
  /** leaves a paw print in the sand */
  print: (x: number, z: number, yaw: number, left: boolean) => void;
}

interface Waypoint {
  x: number;
  z: number;
  /** seconds to stop and sniff the ground here */
  sniff: number;
}

const TURN = 5;
const TROT = 5.2;
const SNIFF_SPEED = 3.6;
const GIVE_UP = 300;

/** One cross-section of a smooth body part: where it is along the axis, how far its centre is shifted, and its two half-sizes. */
interface Ring {
  t: number;
  c: number;
  a: number;
  b: number;
}

const catmull = (p0: number, p1: number, p2: number, p3: number, u: number): number =>
  0.5 * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u * u * u);

/**
 * A smooth, organic tube through a list of cross-sections (like lofting in a modelling program), so a body, a leg or an ear is one
 * continuous surface instead of a pile of balls. `axis` z runs front to back (rings are in the x-y plane, `c` shifts them up);
 * axis y runs top to bottom (rings are in the x-z plane, `c` shifts them toward the back). Colours blend from `low` to `high` across the
 * ring, which gives the fur a lighter underside for free.
 */
const loft = (axis: 'z' | 'y', keys: Ring[], low: number, high: number, steps = 6, around = 22): THREE.BufferGeometry => {
  const rows: Ring[] = [];
  const n = keys.length;
  for (let i = 0; i < n - 1; i++) {
    const k0 = keys[Math.max(0, i - 1)];
    const k1 = keys[i];
    const k2 = keys[i + 1];
    const k3 = keys[Math.min(n - 1, i + 2)];
    for (let j = 0; j < steps; j++) {
      const u = j / steps;
      rows.push({
        t: catmull(k0.t, k1.t, k2.t, k3.t, u),
        c: catmull(k0.c, k1.c, k2.c, k3.c, u),
        a: Math.max(0.002, catmull(k0.a, k1.a, k2.a, k3.a, u)),
        b: Math.max(0.002, catmull(k0.b, k1.b, k2.b, k3.b, u)),
      });
    }
  }
  rows.push(keys[n - 1]);
  const pos: number[] = [];
  const col: number[] = [];
  const cLow = new THREE.Color(low);
  const cHigh = new THREE.Color(high);
  const tmp = new THREE.Color();
  for (const r of rows) {
    for (let k = 0; k < around; k++) {
      const th = (k / around) * Math.PI * 2;
      const ca = Math.cos(th);
      const sb = Math.sin(th);
      if (axis === 'z') pos.push(r.a * ca, r.c + r.b * sb, r.t);
      else pos.push(r.a * ca, r.t, r.c + r.b * sb);
      const m = Math.min(1, Math.max(0, (sb + 0.55) / 1.4));
      tmp.copy(cLow).lerp(cHigh, m * m * (3 - 2 * m));
      col.push(tmp.r, tmp.g, tmp.b);
    }
  }
  const idx: number[] = [];
  for (let i = 0; i < rows.length - 1; i++) {
    for (let k = 0; k < around; k++) {
      const a0 = i * around + k;
      const a1 = i * around + ((k + 1) % around);
      const b0 = (i + 1) * around + k;
      const b1 = (i + 1) * around + ((k + 1) % around);
      idx.push(a0, b0, a1, a1, b0, b1);
    }
  }
  // close both ends with a fan to the centre of the ring
  for (const end of [0, rows.length - 1]) {
    const r = rows[end];
    const centre = pos.length / 3;
    if (axis === 'z') pos.push(0, r.c, r.t);
    else pos.push(0, r.t, r.c);
    col.push(col[end * around * 3], col[end * around * 3 + 1], col[end * around * 3 + 2]);
    for (let k = 0; k < around; k++) idx.push(centre, end * around + k, end * around + ((k + 1) % around));
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  geo.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  geo.setIndex(idx);
  geo.computeVertexNormals();
  return geo;
};

/** The three bones of a leg. The fur over them is one skinned surface, so a leg bends instead of coming apart at the joints. */
interface Leg {
  top: THREE.Bone;
  knee: THREE.Bone;
  paw: THREE.Bone;
}

/** How far below the hip the knee sits, and how far below the knee the ankle sits. */
const THIGH = 0.228;
const SHIN = 0.24;

/** 0 above a joint, 1 below it, with a smooth blend over `width` either side. */
const jointBlend = (y: number, joint: number, width: number): number => {
  const u = Math.min(1, Math.max(0, (joint + width - y) / (2 * width)));
  return u * u * (3 - 2 * u);
};

/** A shoulder or haunch that starts thin inside the body and swells out through the flank, so there is no edge where the leg begins. */
const FRONT_LEG: Ring[] = [
  { t: 0.15, c: 0.0, a: 0.012, b: 0.03 },
  { t: 0.1, c: 0.0, a: 0.045, b: 0.085 },
  { t: 0.03, c: 0.0, a: 0.085, b: 0.125 },
  { t: -0.07, c: 0.0, a: 0.075, b: 0.105 },
  { t: -0.16, c: 0.008, a: 0.056, b: 0.074 },
  { t: -THIGH, c: 0.0, a: 0.045, b: 0.054 },
  { t: -0.31, c: 0.0, a: 0.039, b: 0.045 },
  { t: -0.4, c: 0.0, a: 0.036, b: 0.041 },
  { t: -0.44, c: -0.004, a: 0.04, b: 0.046 },
  { t: -0.456, c: -0.02, a: 0.05, b: 0.066 },
  { t: -0.47, c: -0.026, a: 0.052, b: 0.075 },
  { t: -0.482, c: -0.026, a: 0.036, b: 0.052 },
];

const HIND_LEG: Ring[] = [
  { t: 0.17, c: 0.0, a: 0.012, b: 0.03 },
  { t: 0.12, c: 0.005, a: 0.05, b: 0.095 },
  { t: 0.05, c: 0.015, a: 0.09, b: 0.125 },
  { t: -0.05, c: 0.025, a: 0.082, b: 0.115 },
  { t: -0.15, c: 0.02, a: 0.06, b: 0.08 },
  { t: -THIGH, c: 0.0, a: 0.046, b: 0.054 },
  { t: -0.31, c: 0.0, a: 0.039, b: 0.045 },
  { t: -0.4, c: 0.0, a: 0.036, b: 0.041 },
  { t: -0.44, c: -0.004, a: 0.04, b: 0.046 },
  { t: -0.456, c: -0.02, a: 0.05, b: 0.066 },
  { t: -0.47, c: -0.026, a: 0.052, b: 0.075 },
  { t: -0.482, c: -0.026, a: 0.036, b: 0.052 },
];

/** The camp dog. It sits by the camp until given a bone, then follows its nose to a buried find and barks until the player digs it up. */
export class Dog {
  readonly group = new THREE.Group();
  state: DogState = 'rest';
  x = 0;
  z = 0;
  heading = 0;
  /** the find the dog is working on, if any */
  item: BuriedItem | null = null;

  private home = { x: 0, z: 0, heading: 0 };
  private speed = 0;
  private gait = 0;
  private sit = 0;
  private lie = 1;
  /** true while the player is close enough that the dog sits up; it lies down again when they leave */
  private wantSit = false;
  /** seconds left of the happy reaction to being petted */
  private loveT = 0;
  private dig = 0;
  private sniffAmt = 0;
  private kick = 0;
  private barkT = 0;
  private dustT = 0;
  private workT = 0;
  private pauseT = 0;
  private pathT = 0;
  private stride = 0;
  private stepSide = false;
  private path: Waypoint[] = [];
  /** where the dog stands while it works: in front of the find, facing it, never on top of it */
  private stand = { x: 0, z: 0 };
  private bodyPivot = new THREE.Group();
  private headPivot = new THREE.Group();
  private jaw = new THREE.Group();
  private tongue: THREE.Mesh;
  private ears: THREE.Group[] = [];
  private tail: THREE.Group[] = [];
  private front: Leg[] = [];
  private hind: Leg[] = [];
  private tmp = new THREE.Vector3();

  constructor(private beach: Beach) {
    this.tongue = new THREE.Mesh(new THREE.SphereGeometry(0.03, 8, 6), new THREE.MeshStandardMaterial({ color: 0xe0707a, roughness: 0.6 }));
    this.build();
  }

  private build(): void {
    // a golden retriever: smooth golden coat, lighter underneath and on the feathering, a broad head, long muzzle, soft hanging ears
    const GOLD = 0xd39a45;
    const PALE = 0xf0cd8a;
    const DEEP = 0xb9792b;
    const coat = (): THREE.MeshStandardMaterial => new THREE.MeshStandardMaterial({ color: 0xffffff, vertexColors: true, roughness: 0.92, side: THREE.DoubleSide });
    const furMat = coat();
    const earMat = coat();
    const deepMat = new THREE.MeshStandardMaterial({ color: DEEP, roughness: 0.95 });
    const black = new THREE.MeshStandardMaterial({ color: 0x17120e, roughness: 0.3 });
    const brown = new THREE.MeshStandardMaterial({ color: 0x3a2412, roughness: 0.3 });
    const shine = new THREE.MeshBasicMaterial({ color: 0xffffff });
    const mesh = (parent: THREE.Object3D, geo: THREE.BufferGeometry, mat: THREE.Material, x = 0, y = 0, z = 0): THREE.Mesh => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x, y, z);
      m.castShadow = true;
      parent.add(m);
      return m;
    };
    const ball = (parent: THREE.Object3D, r: number, mat: THREE.Material, x: number, y: number, z: number, sx = 1, sy = 1, sz = 1): THREE.Mesh => {
      const m = mesh(parent, new THREE.SphereGeometry(r, 16, 12), mat, x, y, z);
      m.scale.set(sx, sy, sz);
      return m;
    };

    // ---- torso and neck in one smooth piece: rump, level back, a deep chest, then the neck rising into the head
    this.bodyPivot.position.set(0, 0.43, 0.22);
    this.group.add(this.bodyPivot);
    const b = this.bodyPivot;
    mesh(
      b,
      loft(
        'z',
        [
          { t: 0.22, c: 0.1, a: 0.04, b: 0.05 },
          { t: 0.16, c: 0.11, a: 0.13, b: 0.14 },
          { t: 0.04, c: 0.12, a: 0.17, b: 0.18 },
          { t: -0.14, c: 0.12, a: 0.165, b: 0.17 },
          { t: -0.3, c: 0.1, a: 0.17, b: 0.185 },
          { t: -0.46, c: 0.07, a: 0.19, b: 0.215 },
          { t: -0.6, c: 0.1, a: 0.165, b: 0.2 },
          { t: -0.72, c: 0.2, a: 0.115, b: 0.15 },
          { t: -0.8, c: 0.28, a: 0.1, b: 0.12 },
        ],
        PALE,
        GOLD,
      ),
      furMat,
    );
    // a fluffy ruff on the chest
    mesh(
      b,
      loft(
        'z',
        [
          { t: -0.3, c: -0.02, a: 0.05, b: 0.035 },
          { t: -0.46, c: -0.06, a: 0.11, b: 0.07 },
          { t: -0.6, c: -0.03, a: 0.09, b: 0.07 },
          { t: -0.7, c: 0.06, a: 0.06, b: 0.06 },
        ],
        PALE,
        PALE,
        5,
        16,
      ),
      furMat,
    );

    // ---- head: a broad skull, a clear stop, a long square muzzle and a big black nose
    this.headPivot.position.set(0, 0.27, -0.76);
    b.add(this.headPivot);
    const h = this.headPivot;
    mesh(
      h,
      loft(
        'z',
        [
          { t: 0.07, c: 0.03, a: 0.075, b: 0.085 },
          { t: -0.01, c: 0.04, a: 0.105, b: 0.105 },
          { t: -0.09, c: 0.035, a: 0.1, b: 0.1 },
          { t: -0.15, c: 0.0, a: 0.066, b: 0.062 },
          { t: -0.25, c: -0.015, a: 0.062, b: 0.055 },
          { t: -0.32, c: -0.02, a: 0.052, b: 0.05 },
          { t: -0.345, c: -0.02, a: 0.03, b: 0.034 },
        ],
        PALE,
        GOLD,
      ),
      furMat,
    );
    ball(h, 0.036, black, 0, -0.012, -0.345, 1.15, 0.85, 0.9);
    // lower jaw: opens a little to bark or pant
    this.jaw.position.set(0, -0.045, -0.1);
    h.add(this.jaw);
    mesh(
      this.jaw,
      loft(
        'z',
        [
          { t: 0.0, c: -0.01, a: 0.045, b: 0.02 },
          { t: -0.1, c: -0.012, a: 0.05, b: 0.02 },
          { t: -0.2, c: -0.005, a: 0.04, b: 0.018 },
        ],
        PALE,
        PALE,
        4,
        14,
      ),
      furMat,
    );
    this.tongue.position.set(0, 0.0, -0.1);
    this.tongue.scale.set(0.9, 0.3, 2.2);
    this.jaw.add(this.tongue);
    this.tongue.visible = false;
    for (const s of [-1, 1]) {
      ball(h, 0.02, brown, s * 0.064, 0.055, -0.115);
      ball(h, 0.0055, shine, s * 0.06, 0.063, -0.131);
      ball(h, 0.018, deepMat, s * 0.06, 0.088, -0.11, 1.5, 0.6, 1.0);
      // soft ears that hang from the top of the head, close to the cheeks
      const ear = new THREE.Group();
      ear.position.set(s * 0.09, 0.075, 0.015);
      ear.rotation.z = s * 0.22;
      mesh(
        ear,
        loft(
          'y',
          [
            { t: 0.0, c: 0.0, a: 0.03, b: 0.05 },
            { t: -0.06, c: 0.0, a: 0.02, b: 0.07 },
            { t: -0.12, c: 0.0, a: 0.012, b: 0.07 },
            { t: -0.17, c: 0.0, a: 0.006, b: 0.04 },
          ],
          GOLD,
          DEEP,
          5,
          16,
        ),
        earMat,
      );
      h.add(ear);
      this.ears.push(ear);
    }

    // ---- legs: hip to toe is one skinned surface over three bones; its colour runs from the flank's gold to pale paws
    const flank = new THREE.Color(PALE).lerp(new THREE.Color(GOLD), 0.42);
    const toe = new THREE.Color(PALE);
    const tint = new THREE.Color();
    const makeLeg = (x: number, y: number, z: number, keys: Ring[]): Leg => {
      const geo = loft('y', keys, PALE, PALE, 5, 18);
      const pos = geo.getAttribute('position');
      const col = geo.getAttribute('color');
      const index: number[] = [];
      const weight: number[] = [];
      for (let i = 0; i < pos.count; i++) {
        const vy = pos.getY(i);
        const k = jointBlend(vy, -THIGH, 0.05);
        const a = jointBlend(vy, -THIGH - SHIN, 0.03);
        index.push(0, 1, 2, 0);
        weight.push(1 - k, k * (1 - a), k * a, 0);
        tint.copy(toe).lerp(flank, Math.min(1, Math.max(0, (vy + 0.42) / 0.42)));
        col.setXYZ(i, tint.r, tint.g, tint.b);
      }
      geo.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(index, 4));
      geo.setAttribute('skinWeight', new THREE.Float32BufferAttribute(weight, 4));
      const top = new THREE.Bone();
      const knee = new THREE.Bone();
      const paw = new THREE.Bone();
      top.position.set(x, y, z);
      knee.position.y = -THIGH;
      paw.position.y = -SHIN;
      top.add(knee);
      knee.add(paw);
      const skin = new THREE.SkinnedMesh(geo, furMat);
      skin.position.set(x, y, z);
      skin.castShadow = true;
      skin.frustumCulled = false;
      b.add(top, skin);
      this.group.updateMatrixWorld(true);
      skin.bind(new THREE.Skeleton([top, knee, paw]));
      return { top, knee, paw };
    };
    for (const s of [-1, 1]) {
      this.front.push(makeLeg(s * 0.11, 0.05, -0.44, FRONT_LEG));
      this.hind.push(makeLeg(s * 0.1, 0.05, 0.02, HIND_LEG));
    }

    // ---- tail: a long, feathered plume that is slim at the root, full in the middle and tapers to a point
    let parent: THREE.Object3D = b;
    const lens = [0.15, 0.15, 0.14];
    const shapes: Ring[][] = [
      [{ t: 0, c: 0, a: 0.04, b: 0.045 }, { t: 0.15, c: 0, a: 0.045, b: 0.06 }],
      [{ t: 0, c: 0, a: 0.045, b: 0.06 }, { t: 0.15, c: -0.01, a: 0.04, b: 0.085 }],
      [{ t: 0, c: -0.01, a: 0.04, b: 0.085 }, { t: 0.07, c: -0.02, a: 0.03, b: 0.075 }, { t: 0.14, c: -0.01, a: 0.006, b: 0.02 }],
    ];
    for (let i = 0; i < 3; i++) {
      const seg = new THREE.Group();
      seg.position.set(0, i === 0 ? 0.14 : lens[i - 1], i === 0 ? 0.2 : 0);
      mesh(seg, loft('y', shapes[i], GOLD, PALE, 4, 16), furMat);
      parent.add(seg);
      parent = seg;
      this.tail.push(seg);
    }
  }

  get away(): boolean {
    return this.state !== 'rest';
  }

  setHome(x: number, z: number, heading: number): void {
    this.home = { x, z, heading };
    if (this.state === 'rest') {
      this.x = x;
      this.z = z;
      this.heading = heading;
      this.place();
    }
  }

  /** Works out a wandering route from the dog to the find: weaving to either side, with a few stops to sniff the ground. */
  private buildPath(item: BuriedItem): Waypoint[] {
    const sx = this.x;
    const sz = this.z;
    const dx = this.stand.x - sx;
    const dz = this.stand.z - sz;
    const dist = Math.hypot(dx, dz);
    const ux = dx / (dist || 1);
    const uz = dz / (dist || 1);
    const rx = -uz;
    const rz = ux;
    const n = Math.max(1, Math.floor(dist / 5.5));
    const list: Waypoint[] = [];
    let side = Math.random() < 0.5 ? -1 : 1;
    for (let i = 1; i <= n; i++) {
      const along = (i / (n + 1)) * dist;
      const off = side * (1.6 + Math.random() * 3.4) * Math.min(1, dist / 14);
      side = -side;
      const x = Math.max(-(HALF - 3), Math.min(HALF - 3, sx + ux * along + rx * off));
      const z = Math.min(SHORE_Z - 4, Math.max(-(HALF - 3), sz + uz * along + rz * off));
      list.push({ x, z, sniff: Math.random() < 0.5 ? 0.6 + Math.random() * 0.9 : 0 });
    }
    list.push({ x: this.stand.x, z: this.stand.z, sniff: 0 });
    return list;
  }

  /** Sends the dog after a buried find. `instant` puts it straight at the spot (used when a save is loaded). */
  startMission(item: BuriedItem, instant = false): void {
    this.item = item;
    // stand about a metre short of the find, on the side the dog comes from
    const fromX = instant ? this.home.x : this.x;
    const fromZ = instant ? this.home.z : this.z;
    const ax = fromX - item.x;
    const az = fromZ - item.z;
    const al = Math.hypot(ax, az) || 1;
    this.stand = { x: item.x + (ax / al) * 1.0, z: item.z + (az / al) * 1.0 };
    this.workT = 0;
    this.kick = 1;
    this.pauseT = 0;
    if (instant) {
      this.path = [];
      this.x = this.stand.x;
      this.z = this.stand.z;
      this.heading = Math.atan2(-(item.x - this.x), -(item.z - this.z));
      this.state = 'find';
      this.barkT = 2;
    } else {
      this.path = this.buildPath(item);
      this.state = 'run';
    }
  }

  /** The player gives the dog some love: it sits up, shakes its head and wags. */
  pet(): void {
    if (this.state !== 'rest') return;
    this.loveT = 2.4;
    this.kick = 0.6;
  }

  /** The find is done: trot back to the spot by the camp. */
  recall(): void {
    this.item = null;
    this.path = [];
    this.pauseT = 0;
    this.state = 'return';
  }

  private place(): void {
    const ground = this.beach.heightAt(this.x, this.z);
    const bob = this.speed > 0.5 ? Math.abs(Math.sin(this.gait)) * 0.03 * Math.min(1, this.speed / TROT) : 0;
    this.group.position.set(this.x, ground - this.sit * 0.25 - this.lie * 0.34 + bob, this.z);
    this.group.rotation.y = this.heading;
  }

  update(dt: number, t: number, env: DogEnv): void {
    this.pathT += dt;
    this.loveT = Math.max(0, this.loveT - dt);
    // where is the dog headed?
    let tx = this.home.x;
    let tz = this.home.z;
    if (this.state === 'run') {
      const wp = this.path[0];
      if (wp) {
        tx = wp.x;
        tz = wp.z;
      } else if (this.item) {
        tx = this.stand.x;
        tz = this.stand.z;
      }
    }
    const moving = this.state === 'run' || this.state === 'return';
    let sniffing = false;
    if (moving) {
      const dx = tx - this.x;
      const dz = tz - this.z;
      const d = Math.hypot(dx, dz);
      if (this.pauseT > 0) {
        // stopped with its nose to the sand
        this.pauseT -= dt;
        sniffing = true;
        this.speed *= Math.exp(-dt * 8);
        this.heading += Math.sin(t * 3.1) * dt * 0.8;
      } else {
        // trailing a scent: a slow, weaving walk with the nose down; heading home it trots with its head up
        const trail = this.state === 'run';
        const want = Math.atan2(-dx, -dz) + (trail ? Math.sin(this.pathT * 2.3) * 0.28 : 0);
        let diff = want - this.heading;
        diff = Math.atan2(Math.sin(diff), Math.cos(diff));
        this.heading += Math.max(-TURN * dt, Math.min(TURN * dt, diff));
        const top = trail ? SNIFF_SPEED : TROT;
        const slow = Math.min(1, d / 2.5);
        this.speed += (top * Math.max(0.4, slow) - this.speed) * Math.min(1, dt * 4);
        this.x += -Math.sin(this.heading) * this.speed * dt;
        this.z += -Math.cos(this.heading) * this.speed * dt;
        const r = env.resolve(this.x, this.z);
        this.x = r.x;
        this.z = r.z;
        this.gait += dt * (5 + this.speed * 2.6);
        sniffing = trail;
        // a pair of paw prints every stride: a front paw ahead and the opposite hind paw behind
        this.stride += this.speed * dt;
        if (this.stride > 0.36 && this.speed > 0.8) {
          this.stride = 0;
          this.stepSide = !this.stepSide;
          const side = this.stepSide ? 1 : -1;
          const fx = -Math.sin(this.heading);
          const fz = -Math.cos(this.heading);
          const rx = Math.cos(this.heading);
          const rz = -Math.sin(this.heading);
          env.print(this.x + fx * 0.32 + rx * 0.075 * side, this.z + fz * 0.32 + rz * 0.075 * side, this.heading, side < 0);
          env.print(this.x - fx * 0.26 - rx * 0.075 * side, this.z - fz * 0.26 - rz * 0.075 * side, this.heading, side > 0);
        }
      }
      if (d < (this.state === 'run' && this.path.length > 1 ? 0.9 : 0.5) && this.pauseT <= 0) {
        if (this.state === 'run') {
          const wp = this.path.shift();
          if (wp && wp.sniff > 0) this.pauseT = wp.sniff;
          if (this.path.length === 0) {
            this.state = 'find';
            this.barkT = 0.6;
            this.workT = 0;
          }
        } else {
          this.state = 'rest';
          this.x = this.home.x;
          this.z = this.home.z;
        }
      }
    } else {
      this.speed *= Math.exp(-dt * 8);
      if (this.state === 'rest') {
        // sit and face the player when they come near, the fire otherwise
        const dx = env.playerX - this.x;
        const dz = env.playerZ - this.z;
        // lie by the fire; sit up and look at the player once they come close, and settle again when they leave
        const dist = Math.hypot(dx, dz);
        if (dist < 5.5 || this.loveT > 0) this.wantSit = true;
        else if (dist > 8) this.wantSit = false;
        const want = this.wantSit ? Math.atan2(-dx, -dz) : this.home.heading;
        let diff = want - this.heading;
        diff = Math.atan2(Math.sin(diff), Math.cos(diff));
        this.heading += diff * Math.min(1, dt * 1.6);
      }
    }

    // digging and barking at the find
    let digging = false;
    if (this.state === 'find' && this.item) {
      this.workT += dt;
      digging = !this.item.revealed;
      // face the find, paws toward it
      let face = Math.atan2(-(this.item.x - this.x), -(this.item.z - this.z)) - this.heading;
      face = Math.atan2(Math.sin(face), Math.cos(face));
      this.heading += face * Math.min(1, dt * 5);
      const k = Math.min(1, dt * 2.5);
      this.x += (this.stand.x - this.x) * k;
      this.z += (this.stand.z - this.z) * k;
      if (digging) {
        this.barkT -= dt;
        if (this.barkT <= 0) {
          this.barkT = 3.2 + Math.random() * 1.2;
          this.kick = 1;
          env.bark(Math.hypot(env.playerX - this.x, env.playerZ - this.z));
        }
        this.dustT -= dt;
        if (this.dustT <= 0) {
          this.dustT = 0.35;
          env.dust(this.tmp.set(this.item.x, this.beach.heightAt(this.item.x, this.item.z) + 0.05, this.item.z));
        }
      }
      // nobody came: it gives up after a while and goes home
      if (this.workT > GIVE_UP) this.recall();
    }

    this.pose(dt, t, env, digging, sniffing);
    this.place();
  }

  /** Sets every joint for the current state: sitting, trotting, sniffing, digging or barking. */
  private pose(dt: number, t: number, env: DogEnv, digging: boolean, sniffing: boolean): void {
    const resting = this.state === 'rest';
    this.sit += ((resting && this.wantSit ? 1 : 0) - this.sit) * Math.min(1, dt * 3.5);
    this.lie += ((resting && !this.wantSit ? 1 : 0) - this.lie) * Math.min(1, dt * 3);
    const L = this.lie;
    const love = Math.min(1, this.loveT * 2);
    const shake = this.loveT > 1.5 ? 1 : Math.max(0, (this.loveT - 0.6) / 0.9);
    this.dig += ((digging ? 1 : 0) - this.dig) * Math.min(1, dt * 6);
    this.sniffAmt += ((sniffing ? 1 : 0) - this.sniffAmt) * Math.min(1, dt * 5);
    this.kick = Math.max(0, this.kick - dt * 4);
    const s = this.sit;
    const run = Math.min(1, this.speed / TROT);
    const p = Math.sin(this.gait);
    const c = Math.cos(this.gait);
    const scratch = Math.sin(t * 20) * 0.9 * this.dig;

    this.bodyPivot.rotation.x = s * 0.95 + L * 0.1 - this.dig * 0.2 + run * 0.03 + Math.sin(this.gait * 2) * 0.02 * run;
    // front legs hang straight when sitting (cancelling the tilt of the body) and swing and fold when walking
    const fSwing = p * 0.75 * run;
    this.front.forEach((leg, i) => {
      const sign = i === 0 ? 1 : -1;
      // lying: the front legs stretch out ahead along the sand
      leg.top.rotation.x = -s * 0.95 + L * 1.4 + this.dig * 0.2 + fSwing * sign + scratch * sign;
      leg.knee.rotation.x = -Math.max(0, c * sign) * 0.9 * run - Math.max(0, this.dig * Math.sin(t * 20) * sign) * 0.8 - L * 0.15;
      leg.paw.rotation.x = L * 0.3;
      leg.top.rotation.z = 0;
    });
    // hind legs fold under the dog when sitting: thigh forward, shin back down, paw flat on the ground
    const hSwing = p * 0.7 * run;
    this.hind.forEach((leg, i) => {
      const sign = i === 0 ? -1 : 1;
      // lying: the hind legs fold under and out to the sides
      leg.top.rotation.x = s * 0.62 + L * 1.15 + hSwing * sign + 0.1;
      leg.knee.rotation.x = -s * 1.5 - L * 2.0 + Math.max(0, c * -sign) * 0.8 * run - 0.15;
      leg.paw.rotation.x = s * 1.35 + L * 0.9;
      leg.top.rotation.z = L * (i === 0 ? -0.4 : 0.4);
    });

    // head: level when sitting, nose near the sand when sniffing, dipped at the dig, jerked up on a bark
    const bearing = Math.atan2(-(env.playerX - this.x), -(env.playerZ - this.z)) - this.heading;
    const rel = Math.atan2(Math.sin(bearing), Math.cos(bearing));
    const look = resting && this.sit > 0.6 && Math.hypot(env.playerX - this.x, env.playerZ - this.z) < 9 ? Math.max(-0.7, Math.min(0.7, rel * 0.6)) : 0;
    const nose = Math.sin(t * 13) * 0.07 * this.sniffAmt;
    this.headPivot.rotation.x = -s * 0.6 - L * 0.12 - this.dig * 0.55 - this.sniffAmt * 0.95 + nose + this.kick * 0.5 + love * 0.2;
    const headTurn = look + Math.sin(t * 2.1) * 0.25 * this.sniffAmt + Math.sin(t * 17) * 0.42 * shake;
    this.headPivot.rotation.y += (headTurn - this.headPivot.rotation.y) * Math.min(1, dt * (this.loveT > 0 ? 22 : 5));
    // mouth: open on a bark, panting while it works
    const pant = resting ? 0.0 : 0.5 + Math.sin(t * 14) * 0.15;
    this.jaw.rotation.x = Math.max(this.kick * 0.55, pant * 0.35 * (1 - this.sniffAmt * 0.6), love * 0.22);
    this.tongue.visible = (!resting && this.sniffAmt < 0.5) || love > 0.3;
    // ears flop with the stride
    this.ears.forEach((e, i) => {
      e.rotation.x = Math.sin(this.gait * 2 + i) * 0.18 * run + this.kick * 0.25;
    });
    // tail: lazy wag at rest, fast and loose when busy; each segment lags the one before
    const wag = this.loveT > 0 ? 19 : resting ? 3.5 : 15;
    const amp = this.loveT > 0 ? 0.75 : resting ? 0.3 : 0.5;
    this.tail.forEach((seg, i) => {
      seg.rotation.z = Math.sin(t * wag - i * 0.7) * amp * (1 - i * 0.2);
      seg.rotation.x = i === 0 ? 0.7 - run * 0.35 : 0.2;
    });
  }
}
