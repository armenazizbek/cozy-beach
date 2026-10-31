import * as THREE from 'three';
import { ParticleSystem } from '../core/Particles';
import * as flora from './SeaFlora';
import { SeaCreatures } from './SeaCreatures';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

/** The sea floor is a small separate world, far from the beach. Nothing on land is drawn while someone is down here. */
export const UW = { x: 1200, z: 1200, half: 58 };
/** the height of the water's surface seen from below */
export const UW_SURFACE = 0;
/** where the diver slips into the water: the shallow edge */
export const UW_ENTRY = { x: UW.x, z: UW.z + 48 };

const WRECK = { x: UW.x + 14, z: UW.z - 18 };

const seeded = (seed: number): (() => number) => {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
};

interface Fish {
  root: THREE.Group;
  tail: THREE.Mesh;
  hit: number;
  x: number;
  y: number;
  z: number;
  heading: number;
  turn: number;
  speed: number;
  ty: number;
  phase: number;
  alive: boolean;
  respawn: number;
}

interface Spear {
  mesh: THREE.Group;
  active: boolean;
  pos: THREE.Vector3;
  dir: THREE.Vector3;
  dist: number;
  stuck: number;
}

/** The sea floor: sand, rocks, coral, kelp, a wreck, fish, bubbles and the harpoon's spears. */
export class Underwater {
  readonly group = new THREE.Group();
  readonly wreck = WRECK;
  private floorMesh: THREE.Mesh;
  private surface: THREE.Mesh;
  private kelp: { o: THREE.Object3D; ph: number; amp: number }[] = [];
  private fish: Fish[] = [];
  private spears: Spear[] = [];
  private spots: { id: string; x: number; z: number }[] = [];
  private bubbles = new ParticleSystem(260, 0.07, -1.1, false);
  private motes = new ParticleSystem(150, 0.045, 0, false);
  private bubbleColor = new THREE.Color(0xd8f3ff);
  private moteColor = new THREE.Color(0xcfe9e0);
  private breath = 2;
  private cooldown = 0;
  private fishRand = seeded(77);
  /** called with the spot where a fish was speared */
  onCatch: (at: THREE.Vector3, reward: number, label: string) => void = () => undefined;
  readonly creatures: SeaCreatures;

  constructor() {
    const rand = seeded(2024);
    this.group.visible = false;
    this.group.matrixWorldAutoUpdate = false;

    // ---- the floor
    // the floor runs far past the swimming area, with fine cells near the middle and coarse ones far off, so its edge is never in view
    const seg = 200;
    const R = 360;
    const geo = new THREE.PlaneGeometry(2, 2, seg, seg);
    geo.rotateX(-Math.PI / 2);
    const pos = geo.attributes.position as THREE.BufferAttribute;
    for (let i = 0; i < pos.count; i++) {
      const u = pos.getX(i);
      const w = pos.getZ(i);
      pos.setXYZ(i, Math.sign(u) * Math.pow(Math.abs(u), 1.6) * R, 0, Math.sign(w) * Math.pow(Math.abs(w), 1.6) * R);
    }
    const col = new Float32Array(pos.count * 3);
    const sand = new THREE.Color(0xdcc693);
    const deep = new THREE.Color(0x6f8f88);
    const c = new THREE.Color();
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i) + UW.x;
      const z = pos.getZ(i) + UW.z;
      const y = this.floorAt(x, z);
      pos.setXYZ(i, pos.getX(i), y, pos.getZ(i));
      const k = Math.min(1, Math.max(0, (-y - 5.4) / 21));
      const ripple = 0.5 + 0.5 * Math.sin((x * 0.9 + Math.sin(z * 0.3) * 2.2) * 1.6);
      c.copy(sand).lerp(deep, k * 0.8).multiplyScalar(0.9 + ripple * 0.14);
      col.set([c.r, c.g, c.b], i * 3);
    }
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    geo.translate(UW.x, 0, UW.z);
    geo.computeVertexNormals();
    this.floorMesh = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 1 }));
    this.group.add(this.floorMesh);

    // ---- the surface, seen from below
    this.surface = new THREE.Mesh(
      new THREE.PlaneGeometry(500, 500),
      new THREE.MeshBasicMaterial({ color: 0x9fe6f7, transparent: true, opacity: 0.55, side: THREE.DoubleSide, depthWrite: false }),
    );
    this.surface.rotation.x = Math.PI / 2;
    this.surface.position.y = UW_SURFACE;
    this.group.add(this.surface);

    // the light shafts that used to be placed here drew 32 numbers from the sequence; keep drawing them so the plants, rocks and finds stay where they were
    for (let i = 0; i < 32; i++) rand();
    this.buildRocks(rand);
    const coralSpots = this.buildCoral(rand);
    this.buildKelp(rand);
    this.buildWreck();
    this.buildFish();
    this.buildSpears();
    this.creatures = new SeaCreatures((x, z) => this.floorAt(x, z));
    this.group.add(this.creatures.group);
    this.layOutFinds(rand, coralSpots);
    this.group.add(this.bubbles.points, this.motes.points);
  }

  /** The height of the sea floor: a shelf that falls away from the shallow edge, with ripples and slow hills. */
  floorAt(x: number, z: number): number {
    const lx = x - UW.x;
    const lz = z - UW.z;
    const t = Math.min(1, Math.max(0, (lz + UW.half) / (UW.half * 2)));
    let y = (-9.6 + t * 7.8) * 3;
    y += Math.sin(lx * 0.35 + lz * 0.12) * 0.1 + Math.sin(lx * 0.13 - lz * 0.3) * 0.16;
    y += Math.sin(lx * 0.07) * Math.cos(lz * 0.06) * 0.8;
    return y;
  }

  contains(x: number, z: number): boolean {
    return Math.abs(x - UW.x) < UW.half + 10 && Math.abs(z - UW.z) < UW.half + 10;
  }

  setActive(on: boolean): void {
    this.group.visible = on;
    this.group.matrixWorldAutoUpdate = on;
    if (!on) for (const s of this.spears) {
      s.active = false;
      s.mesh.visible = false;
    }
  }

  findSpots(): { id: string; x: number; z: number }[] {
    return this.spots;
  }

  // ---------------------------------------------------------------- scenery

  private onFloor(o: THREE.Object3D, x: number, z: number, sink = 0): void {
    o.position.set(x, this.floorAt(x, z) - sink, z);
    this.group.add(o);
  }

  private buildRocks(rand: () => number): void {
    const geo = new THREE.DodecahedronGeometry(1, 0);
    const mats = [0x7d7f80, 0x6a6a64, 0x5f716a].map((k) => new THREE.MeshStandardMaterial({ color: k, roughness: 1, flatShading: true }));
    for (let i = 0; i < 30; i++) {
      const x = UW.x + (rand() - 0.5) * UW.half * 1.8;
      const z = UW.z + (rand() - 0.5) * UW.half * 1.8;
      if (Math.hypot(x - WRECK.x, z - WRECK.z) < 7) continue;
      const m = new THREE.Mesh(geo, mats[i % 3]);
      const s = 0.4 + rand() * 1.1;
      m.scale.set(s * (1 + rand() * 0.6), s * (0.6 + rand() * 0.4), s * (1 + rand() * 0.5));
      m.rotation.set(rand() * 3, rand() * 3, rand() * 3);
      this.onFloor(m, x, z, s * 0.25);
    }
  }

  /** Several kinds of coral in clumps, with sea fans and rocks between. Returns where they stand, so oysters can lie beside them. */
  private buildCoral(rand: () => number): { x: number; z: number }[] {
    const C = (k: number): THREE.Color => new THREE.Color(k);
    const palettes: [THREE.Color, THREE.Color][] = [
      [C(0xc2455f), C(0xffb0c0)], [C(0xd9742a), C(0xffd08a)], [C(0x7a4ab8), C(0xe0c4ff)], [C(0xd9b232), C(0xfff2a0)], [C(0x2f9a8a), C(0xb4fff0)],
    ];
    const fans = [0xe85a7a, 0xb06ad8, 0xf08a3a];
    const spots: { x: number; z: number }[] = [];
    for (let i = 0; i < 40; i++) {
      const x = UW.x + (rand() - 0.5) * UW.half * 1.7;
      const z = UW.z + (rand() - 0.4) * UW.half * 1.5;
      if (Math.hypot(x - WRECK.x, z - WRECK.z) < 6) continue;
      const [lo, hi] = palettes[Math.floor(rand() * palettes.length)];
      const kind = i % 5;
      const g = new THREE.Group();
      // a clump is a few pieces of one kind, close together
      const n = kind === 2 ? 1 : 2 + Math.floor(rand() * 3);
      for (let k = 0; k < n; k++) {
        let piece: THREE.Object3D;
        if (kind === 0 || kind === 4) piece = flora.staghorn(rand, lo, hi);
        else if (kind === 1) piece = flora.brainCoral(rand, C(0x8f8a3a), C(0xd7cf6a));
        else if (kind === 2) piece = flora.seaFan(fans[Math.floor(rand() * fans.length)]);
        else piece = flora.tubeCoral(rand, lo, hi);
        const a = rand() * Math.PI * 2;
        const d = n === 1 ? 0 : 0.35 + rand() * 0.5;
        piece.position.set(Math.cos(a) * d, 0, Math.sin(a) * d);
        piece.rotation.y = rand() * 6;
        piece.scale.setScalar(0.7 + rand() * 0.7);
        g.add(piece);
      }
      // a few tufts of grass at the foot of it
      for (let k = 0; k < 2; k++) {
        const t = flora.eelgrass(rand);
        const a = rand() * 6;
        t.position.set(Math.cos(a) * 0.9, 0, Math.sin(a) * 0.9);
        g.add(t);
        this.kelp.push({ o: t, ph: rand() * 6, amp: 0.1 });
      }
      this.onFloor(g, x, z, 0.04);
      if (kind === 2) this.kelp.push({ o: g.children[0], ph: rand() * 6, amp: 0.025 });
      spots.push({ x, z });
    }
    return spots;
  }

  /** Meadows of seagrass, eel grass and swaying kelp forests. */
  private buildKelp(rand: () => number): void {
    for (let i = 0; i < 170; i++) {
      const x = UW.x + (rand() - 0.5) * UW.half * 1.9;
      const z = UW.z + (rand() - 0.5) * UW.half * 1.9;
      if (Math.hypot(x - WRECK.x, z - WRECK.z) < 4.5) continue;
      const t = flora.seagrass(rand);
      this.onFloor(t, x, z, 0.03);
      this.kelp.push({ o: t, ph: rand() * 6, amp: 0.08 });
    }
    // kelp grows in forests, deeper out where the water is
    for (let f = 0; f < 6; f++) {
      const cx = UW.x + (rand() - 0.5) * UW.half * 1.5;
      const cz = UW.z + (rand() * 0.8 - 0.8) * UW.half;
      if (Math.hypot(cx - WRECK.x, cz - WRECK.z) < 12) continue;
      for (let i = 0; i < 9; i++) {
        const x = cx + (rand() - 0.5) * 9;
        const z = cz + (rand() - 0.5) * 9;
        const k = flora.kelp(rand);
        this.onFloor(k, x, z, 0.05);
        this.kelp.push({ o: k, ph: rand() * 6, amp: 0.07 });
      }
    }
  }

  private buildWreck(): void {
    const wood = new THREE.MeshStandardMaterial({ color: 0x5a4332, roughness: 1 });
    const mossy = new THREE.MeshStandardMaterial({ color: 0x4d5f45, roughness: 1 });
    const iron = new THREE.MeshStandardMaterial({ color: 0x40464a, roughness: 0.7, metalness: 0.5 });
    const g = new THREE.Group();
    const keel = new THREE.Mesh(new THREE.BoxGeometry(11, 0.3, 0.4), wood);
    keel.position.y = 0.15;
    g.add(keel);
    // the ribs of the hull, broken off in places
    for (let i = 0; i < 11; i++) {
      const x = -5 + i;
      const k = 1 - Math.pow(Math.abs(x) / 5.4, 2.2);
      const r = 0.6 + k * 1.4;
      const broken = i === 4 || i === 5 || i === 8;
      const arc = broken ? Math.PI * (0.45 + (i % 3) * 0.08) : Math.PI * 0.92;
      const rib = new THREE.Mesh(new THREE.TorusGeometry(r, 0.09, 6, 14, arc), i % 2 ? wood : mossy);
      rib.position.set(x, r * 0.95, 0);
      rib.rotation.set(0, Math.PI / 2, Math.PI + (Math.PI - arc) / 2 - (broken && i === 8 ? 0.8 : 0));
      g.add(rib);
    }
    // planks still clinging to the sides
    for (const side of [-1, 1]) {
      for (let p = 0; p < 4; p++) {
        const len = 2.5 + ((p * 7 + (side > 0 ? 3 : 0)) % 4) * 0.9;
        const plank = new THREE.Mesh(new THREE.BoxGeometry(len, 0.05, 0.28), p % 2 ? wood : mossy);
        const r = 1.6 - p * 0.15;
        plank.position.set(-2.8 + p * 1.6 + side * 0.4, 0.5 + p * 0.28, side * (r - p * 0.18));
        plank.rotation.set(side * 0.55, 0, 0);
        g.add(plank);
      }
    }
    // a fallen mast and yard
    const mast = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.16, 7, 8), wood);
    mast.position.set(-1.5, 0.45, 2.4);
    mast.rotation.set(0.05, 0.35, Math.PI / 2 - 0.12);
    g.add(mast);
    const yard = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.07, 4.2, 6), wood);
    yard.position.set(2.2, 0.35, 3.1);
    yard.rotation.set(0.3, 1.1, Math.PI / 2);
    g.add(yard);
    // crates, barrels and an anchor
    const crate = new THREE.BoxGeometry(0.7, 0.55, 0.7);
    for (const [x, z, r] of [[-0.6, 0.2, 0.2], [0.5, -0.5, 0.7], [1.6, 0.3, 0.1], [-3.2, -0.4, 0.5], [3.4, 0.5, 0.3]] as const) {
      const c = new THREE.Mesh(crate, mossy);
      c.position.set(x, 0.3, z);
      c.rotation.set(0.1, r, 0.12);
      g.add(c);
    }
    for (const [x, z] of [[4.4, -1.4], [5.2, 1.6], [-4.6, 1.8]] as const) {
      const b = new THREE.Mesh(new THREE.CylinderGeometry(0.28, 0.28, 0.7, 10), wood);
      b.position.set(x, 0.25, z);
      b.rotation.set(Math.PI / 2 * (x > 0 ? 0 : 1), 0, 0.3);
      g.add(b);
      const band = new THREE.Mesh(new THREE.TorusGeometry(0.28, 0.02, 5, 14), iron);
      band.position.copy(b.position);
      band.rotation.copy(b.rotation);
      band.rotation.x += Math.PI / 2;
      g.add(band);
    }
    const anchor = new THREE.Group();
    anchor.add(new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, 1.7, 6), iron));
    const arms = new THREE.Mesh(new THREE.TorusGeometry(0.55, 0.06, 6, 14, Math.PI), iron);
    arms.position.y = -0.75;
    arms.rotation.z = Math.PI;
    const stock = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 1.0, 6), iron);
    stock.position.y = 0.8;
    stock.rotation.z = Math.PI / 2;
    const ring = new THREE.Mesh(new THREE.TorusGeometry(0.14, 0.03, 5, 10), iron);
    ring.position.y = 0.95;
    anchor.add(arms, stock, ring);
    anchor.position.set(7, 0.7, -0.5);
    anchor.rotation.set(1.2, 0.5, 0.35);
    g.add(anchor);
    g.rotation.y = 0.5;
    this.onFloor(g, WRECK.x, WRECK.z, 0.35);
  }

  // ---------------------------------------------------------------- finds

  private layOutFinds(rand: () => number, coral: { x: number; z: number }[]): void {
    const add = (id: string, x: number, z: number): void => {
      this.spots.push({ id, x, z });
    };
    const w = WRECK;
    // gold in the hold, a bell at each end
    add('gold_bar', w.x - 1.2, w.z + 0.5);
    add('gold_bar', w.x + 0.7, w.z - 0.9);
    add('gold_bar', w.x + 1.9, w.z + 0.2);
    add('ship_bell', w.x - 5.2, w.z + 2.6);
    add('ship_bell', w.x + 5.4, w.z - 2.8);
    // plates in the wreckage
    for (let i = 0; i < 4; i++) {
      const a = rand() * Math.PI * 2;
      const r = 3 + rand() * 5;
      add('porcelain_plate', w.x + Math.cos(a) * r, w.z + Math.sin(a) * r);
    }
    // an oyster beside a few of the corals
    for (let i = 0; i < 4; i++) {
      const c = coral[(i * 7 + 2) % coral.length];
      add('sea_oyster', c.x + 0.9, c.z + 0.5);
    }
    // the message in a bottle: out past the wreck, in the deep
    add('message_bottle', UW.x - 21, UW.z - 30);
    // starfish scattered on the sand
    for (let i = 0; i < 10; i++) {
      for (let tries = 0; tries < 20; tries++) {
        const x = UW.x + (rand() - 0.5) * UW.half * 1.6;
        const z = UW.z + (rand() * 0.9 - 0.45) * UW.half * 1.8;
        if (Math.hypot(x - w.x, z - w.z) < 7) continue;
        add('starfish', x, z);
        break;
      }
    }
  }

  // ---------------------------------------------------------------- fish and spears

  /** One species of fish: a smooth body with a pale belly and markings, fins, eyes, and a forked tail that is its own piece so it can wag. */
  private fishModel(top: number, belly: number, accent: number, pattern: number): { body: THREE.BufferGeometry; tail: THREE.BufferGeometry } {
    const cTop = new THREE.Color(top);
    const cBelly = new THREE.Color(belly);
    const cAcc = new THREE.Color(accent);
    const solid = (g: THREE.BufferGeometry, c: THREE.Color): THREE.BufferGeometry => {
      g.deleteAttribute('uv');
      const n = (g.attributes.position as THREE.BufferAttribute).count;
      const col = new Float32Array(n * 3);
      for (let i = 0; i < n; i++) col.set([c.r, c.g, c.b], i * 3);
      g.setAttribute('color', new THREE.BufferAttribute(col, 3));
      return g;
    };
    // the body: a loft along z, nose at +z, from a rounded profile
    const prof = new THREE.SplineCurve([
      [0, 0], [0.05, 0.42], [0.18, 0.82], [0.4, 1], [0.6, 0.86], [0.78, 0.5], [0.9, 0.24], [1, 0.16],
    ].map(([u, r]) => new THREE.Vector2(u, r)));
    const along = 28;
    const around = 16;
    const pos: number[] = [];
    const col: number[] = [];
    const idx: number[] = [];
    const c = new THREE.Color();
    for (let i = 0; i <= along; i++) {
      const u = i / along;
      const r = Math.max(0, prof.getPoint(u).y);
      const z = 0.5 - u * 0.92;
      for (let j = 0; j < around; j++) {
        const phi = (j / around) * Math.PI * 2;
        const sy = Math.sin(phi);
        const h = 0.2 * r * (sy > 0 ? 1.05 : 0.85);
        const w = 0.085 * r;
        pos.push(Math.cos(phi) * w, sy * h, z);
        const back = Math.min(1, Math.max(0, (sy + 0.35) / 0.9));
        c.copy(cBelly).lerp(cTop, back * back * (3 - 2 * back));
        // markings: pale bands (clown fish), a stripe along the flank, or a darker back
        if (pattern === 1 && ((u > 0.2 && u < 0.3) || (u > 0.5 && u < 0.6))) c.set(0xf6f6f2);
        else if (pattern === 2 && Math.abs(sy + 0.05) < 0.2) c.lerp(cAcc, 0.8);
        else if (pattern === 3 && u > 0.7) c.copy(cAcc);
        col.push(c.r, c.g, c.b);
      }
    }
    for (let i = 0; i < along; i++) {
      for (let j = 0; j < around; j++) {
        const a = i * around + j;
        const b = i * around + ((j + 1) % around);
        idx.push(a, a + around, b, b, a + around, b + around);
      }
    }
    const body = new THREE.BufferGeometry();
    body.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    body.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
    body.setIndex(idx);
    body.computeVertexNormals();
    const parts: THREE.BufferGeometry[] = [body];
    // a flat fin from a shape in (z, y), thin
    const fin = (pts: [number, number][], colour: THREE.Color): THREE.BufferGeometry => {
      const sh = new THREE.Shape();
      pts.forEach(([z, y], i) => (i ? sh.lineTo(z, y) : sh.moveTo(z, y)));
      const g = new THREE.ShapeGeometry(sh);
      g.rotateY(-Math.PI / 2); // the shape's x becomes z
      return solid(g, colour);
    };
    parts.push(fin([[0.2, 0.17], [0.1, 0.25], [-0.02, 0.26], [-0.2, 0.15]], cAcc));
    parts.push(fin([[-0.08, -0.13], [-0.2, -0.2], [-0.24, -0.12]], cAcc));
    for (const side of [-1, 1]) {
      const pf = solid(new THREE.ShapeGeometry(new THREE.Shape([new THREE.Vector2(0, 0), new THREE.Vector2(0.1, 0.05), new THREE.Vector2(0.17, -0.04), new THREE.Vector2(0.03, -0.04)])), cAcc);
      pf.rotateX(Math.PI / 2);
      pf.rotateZ(side * 0.7);
      pf.translate(side * 0.065, -0.03, 0.12);
      parts.push(pf);
      const eye = solid(new THREE.SphereGeometry(0.03, 8, 6), new THREE.Color(0xfafafa));
      eye.translate(side * 0.066, 0.05, 0.37);
      parts.push(eye);
      const pupil = solid(new THREE.SphereGeometry(0.017, 6, 5), new THREE.Color(0x101820));
      pupil.translate(side * 0.083, 0.05, 0.375);
      parts.push(pupil);
    }
    const tailGeo = fin([[0, 0.02], [-0.18, 0.17], [-0.13, 0], [-0.18, -0.17], [0, -0.02]], cAcc);
    return { body: mergeGeometries(parts, false)!, tail: tailGeo };
  }

  private buildFish(): void {
    const species = [
      this.fishModel(0xf28a1e, 0xffc880, 0xf28a1e, 1), // clown fish
      this.fishModel(0x2f6fd0, 0x9cc4f2, 0xf2d03a, 3), // blue with a yellow tail
      this.fishModel(0xd8484f, 0xf6b8b0, 0xc23a40, 0), // snapper
      this.fishModel(0x4fa862, 0xe6e48a, 0xf0c63a, 2), // striped wrasse
      this.fishModel(0x8c98a6, 0xf2f4f6, 0x6c7886, 0), // silver
    ];
    const mat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.4, metalness: 0.12, side: THREE.DoubleSide });
    const rand = this.fishRand;
    for (let i = 0; i < 30; i++) {
      const m = species[i % species.length];
      const root = new THREE.Group();
      root.add(new THREE.Mesh(m.body, mat));
      const tail = new THREE.Mesh(m.tail, mat);
      tail.position.z = -0.42;
      root.add(tail);
      root.scale.setScalar(0.55 + rand() * 0.65);
      this.group.add(root);
      const f: Fish = {
        root, tail, hit: 0.7 * root.scale.x, x: 0, y: 0, z: 0, heading: rand() * 6.28, turn: 0, speed: 0.8 + rand() * 0.9,
        ty: 0, phase: rand() * 6, alive: true, respawn: 0,
      };
      this.placeFish(f);
      this.fish.push(f);
    }
  }

  private placeFish(f: Fish): void {
    const rand = this.fishRand;
    f.x = UW.x + (rand() - 0.5) * UW.half * 1.7;
    f.z = UW.z + (rand() - 0.2) * UW.half * 1.5;
    const floor = this.floorAt(f.x, f.z);
    f.y = floor + 0.8 + rand() * Math.max(0.5, -floor - 2.2);
    f.ty = f.y;
    f.alive = true;
    f.root.visible = true;
  }

  private buildSpears(): void {
    const shaft = new THREE.MeshStandardMaterial({ color: 0xcfd6dc, metalness: 0.8, roughness: 0.3 });
    const tip = new THREE.MeshStandardMaterial({ color: 0xf2f4f6, metalness: 0.9, roughness: 0.2 });
    for (let i = 0; i < 6; i++) {
      const m = new THREE.Group();
      const s = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.015, 1.1, 6), shaft);
      s.rotation.x = Math.PI / 2;
      const t = new THREE.Mesh(new THREE.ConeGeometry(0.04, 0.2, 6), tip);
      t.rotation.x = Math.PI / 2;
      t.position.z = 0.65;
      m.add(s, t);
      m.visible = false;
      this.group.add(m);
      this.spears.push({ mesh: m, active: false, pos: new THREE.Vector3(), dir: new THREE.Vector3(), dist: 0, stuck: 0 });
    }
  }

  /** Looses a spear from `origin` along `dir`. Returns false while the gun is still being reloaded. */
  fire(origin: THREE.Vector3, dir: THREE.Vector3): boolean {
    if (this.cooldown > 0) return false;
    const s = this.spears.find((p) => !p.active);
    if (!s) return false;
    this.cooldown = 0.7;
    s.active = true;
    s.stuck = 0;
    s.dist = 0;
    s.pos.copy(origin);
    s.dir.copy(dir).normalize();
    s.mesh.visible = true;
    s.mesh.position.copy(s.pos);
    s.mesh.lookAt(s.pos.clone().add(s.dir));
    for (let i = 0; i < 5; i++) this.bubbles.emit(s.pos, new THREE.Vector3((Math.random() - 0.5) * 0.4, 0.2 + Math.random() * 0.4, (Math.random() - 0.5) * 0.4), 1.4, this.bubbleColor);
    return true;
  }

  get reloading(): boolean {
    return this.cooldown > 0;
  }

  private surfaceBase = new THREE.Color(0x9fe6f7);

  /** At night the surface seen from below is dark too. 1 is full daylight. */
  setDaylight(k: number): void {
    (this.surface.material as THREE.MeshBasicMaterial).color.copy(this.surfaceBase).multiplyScalar(k);
  }

  update(dt: number, t: number, cam: THREE.Vector3): void {
    this.cooldown = Math.max(0, this.cooldown - dt);
    this.surface.position.set(cam.x, UW_SURFACE, cam.z);
    for (const k of this.kelp) {
      k.o.rotation.z = Math.sin(t * 0.9 + k.ph) * k.amp;
      k.o.rotation.x = Math.cos(t * 0.7 + k.ph) * k.amp * 0.8;
    }

    // fish drift about, and slip away from a diver who comes too close
    for (const f of this.fish) {
      if (!f.alive) {
        f.respawn -= dt;
        if (f.respawn <= 0) this.placeFish(f);
        continue;
      }
      const dx = cam.x - f.x;
      const dz = cam.z - f.z;
      const d = Math.hypot(dx, dz, cam.y - f.y);
      if (Math.random() < dt * 0.4) f.turn = (Math.random() - 0.5) * 1.6;
      let speed = f.speed;
      if (d < 3.2) {
        // away from the diver
        const away = Math.atan2(-dx, -dz);
        let da = away - f.heading;
        da = Math.atan2(Math.sin(da), Math.cos(da));
        f.heading += da * Math.min(1, dt * 4);
        speed *= 2.4;
      } else {
        f.heading += f.turn * dt;
        // turn back at the edge of the water
        const lx = f.x - UW.x;
        const lz = f.z - UW.z;
        if (Math.abs(lx) > UW.half - 4 || Math.abs(lz) > UW.half - 4) {
          let da = Math.atan2(-lx, -lz) - f.heading;
          da = Math.atan2(Math.sin(da), Math.cos(da));
          f.heading += da * Math.min(1, dt * 2);
        }
      }
      f.x += Math.sin(f.heading) * speed * dt;
      f.z += Math.cos(f.heading) * speed * dt;
      const floor = this.floorAt(f.x, f.z);
      if (Math.random() < dt * 0.3) f.ty = floor + 0.7 + Math.random() * Math.max(0.5, -floor - 2.2);
      f.ty = Math.min(UW_SURFACE - 1.4, Math.max(floor + 0.6, f.ty));
      f.y += (f.ty - f.y) * Math.min(1, dt * 0.8);
      f.root.position.set(f.x, f.y, f.z);
      f.root.rotation.y = f.heading;
      f.tail.rotation.y = Math.sin(t * (7 + speed * 3) + f.phase) * 0.5;
    }

    // spears fly straight, and stop in the first fish or the sand
    for (const s of this.spears) {
      if (!s.active) continue;
      if (s.stuck > 0) {
        s.stuck -= dt;
        if (s.stuck <= 0) {
          s.active = false;
          s.mesh.visible = false;
        }
        continue;
      }
      const step = 26 * dt;
      const from = s.pos.clone();
      s.pos.addScaledVector(s.dir, step);
      s.dist += step;
      s.mesh.position.copy(s.pos);
      if (Math.random() < 0.7) this.bubbles.emit(s.pos, new THREE.Vector3(0, 0.3, 0), 0.9, this.bubbleColor);
      const seg = new THREE.Line3(from, s.pos.clone());
      const tmp = new THREE.Vector3();
      let caught = false;
      for (const f of this.fish) {
        if (!f.alive) continue;
        seg.closestPointToPoint(new THREE.Vector3(f.x, f.y, f.z), true, tmp);
        if (tmp.distanceTo(new THREE.Vector3(f.x, f.y, f.z)) < f.hit) {
          f.alive = false;
          f.root.visible = false;
          f.respawn = 25 + Math.random() * 15;
          const at = new THREE.Vector3(f.x, f.y, f.z);
          for (let i = 0; i < 14; i++) this.bubbles.emit(at, new THREE.Vector3((Math.random() - 0.5) * 1.2, Math.random() * 0.8, (Math.random() - 0.5) * 1.2), 1.6, this.bubbleColor);
          this.onCatch(at, 1, 'fish');
          caught = true;
          break;
        }
      }
      if (!caught) {
        for (const an of this.creatures.animals) {
          if (!an.alive || an.r <= 0) continue;
          for (const p of an.pts) {
            seg.closestPointToPoint(p, true, tmp);
            if (tmp.distanceTo(p) < an.r) {
              this.creatures.catchAnimal(an);
              for (let i = 0; i < 14; i++) this.bubbles.emit(p, new THREE.Vector3((Math.random() - 0.5) * 1.2, Math.random() * 0.8, (Math.random() - 0.5) * 1.2), 1.6, this.bubbleColor);
              this.onCatch(p.clone(), an.reward, an.label);
              caught = true;
              break;
            }
          }
          if (caught) break;
        }
      }
      if (caught) {
        s.active = false;
        s.mesh.visible = false;
      } else if (s.pos.y < this.floorAt(s.pos.x, s.pos.z) + 0.05) {
        s.stuck = 1.2;
        for (let i = 0; i < 6; i++) this.bubbles.emit(s.pos, new THREE.Vector3((Math.random() - 0.5) * 0.8, Math.random() * 0.4, (Math.random() - 0.5) * 0.8), 1, this.moteColor);
      } else if (s.dist > 18 || s.pos.y > UW_SURFACE - 0.1) {
        s.active = false;
        s.mesh.visible = false;
      }
    }

    // the diver's own bubbles, and specks drifting in the light
    this.breath -= dt;
    if (this.breath <= 0) {
      this.breath = 3.2 + Math.random() * 1.6;
      for (let i = 0; i < 7; i++) {
        this.bubbles.emit(
          new THREE.Vector3(cam.x + (Math.random() - 0.5) * 0.15, cam.y - 0.1, cam.z + (Math.random() - 0.5) * 0.15),
          new THREE.Vector3((Math.random() - 0.5) * 0.2, 0.5 + Math.random() * 0.4, (Math.random() - 0.5) * 0.2),
          2.2,
          this.bubbleColor,
        );
      }
    }
    if (Math.random() < dt * 40) {
      this.motes.emit(
        new THREE.Vector3(cam.x + (Math.random() - 0.5) * 14, cam.y + (Math.random() - 0.5) * 6, cam.z + (Math.random() - 0.5) * 14),
        new THREE.Vector3((Math.random() - 0.5) * 0.12, (Math.random() - 0.3) * 0.08, (Math.random() - 0.5) * 0.12),
        7,
        this.moteColor,
      );
    }
    this.creatures.update(dt, t, cam);
    this.bubbles.update(dt);
    this.motes.update(dt);
  }
}
