import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { blendShapes, solidSurface } from '../mechanics/Hand';

/** The floor of the cave, below the floor of the stone room. */
export const CAVE_Y = -2.4;
/** where the walkable sand is: an ellipse around this point */
const CAVE = { x: 23.5, z: 0.5, rx: 11, rz: 9 };
/** where the rock wall stands, at the foot of the stair */
const PORTAL_X = 13.25;
/** the edge of the sea */
const WATER_X = 30.5;
const WATER_Y = CAVE_Y - 0.5;
/** the heap of gold */
const HOARD = { x: 25, z: 1.5, r: 3.6 };
/** How much the gold is worth. It buys everything in the camper, luxuries included, whenever the player gets here. */
export const HOARD_VALUE = 30000;

const BONE = 0xe6dcc4;

const rng = (seed: number): (() => number) => {
  let s = seed;
  return () => {
    s = (s * 1664525 + 1013904223) % 4294967296;
    return s / 4294967296;
  };
};

/** Rough rock: layered bands of stone, with cracks running through them. Light is high ground, so it serves as its own bump map. */
const rockTexture = (): THREE.CanvasTexture => {
  const size = 512;
  const cv = document.createElement('canvas');
  cv.width = cv.height = size;
  const c = cv.getContext('2d')!;
  const rand = rng(19);
  c.fillStyle = '#7d7266';
  c.fillRect(0, 0, size, size);
  // the bands of the rock, wandering a little
  for (let y = 0; y < size; y += 6 + rand() * 26) {
    const v = 86 + rand() * 66;
    c.strokeStyle = `rgba(${v + 12},${v + 2},${v - 12},0.55)`;
    c.lineWidth = 3 + rand() * 22;
    c.beginPath();
    const ph = rand() * 6.28;
    for (let x = 0; x <= size; x += 12) {
      // whole waves only, so the texture tiles
      const py = y + Math.sin((x / size) * Math.PI * 2 * 2 + ph) * 10;
      if (x === 0) c.moveTo(x, py);
      else c.lineTo(x, py);
    }
    c.stroke();
  }
  // blotches and grit
  for (let i = 0; i < 900; i++) {
    const v = rand() < 0.5 ? 50 : 170;
    c.fillStyle = `rgba(${v},${v - 6},${v - 14},${0.08 + rand() * 0.16})`;
    const r = 1 + rand() * rand() * 22;
    c.beginPath();
    c.ellipse(rand() * size, rand() * size, r * (1 + rand()), r, rand() * 3, 0, Math.PI * 2);
    c.fill();
  }
  // cracks
  c.strokeStyle = 'rgba(22,17,13,0.8)';
  for (let i = 0; i < 46; i++) {
    let x = rand() * size;
    let y = rand() * size;
    let a = rand() * 6.28;
    c.lineWidth = 0.8 + rand() * 2.4;
    c.beginPath();
    c.moveTo(x, y);
    for (let k = 0; k < 7; k++) {
      a += (rand() - 0.5) * 1.3;
      x += Math.cos(a) * (14 + rand() * 24);
      y += Math.sin(a) * (14 + rand() * 24);
      c.lineTo(x, y);
    }
    c.stroke();
  }
  const t = new THREE.CanvasTexture(cv);
  t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(9, 4);
  t.anisotropy = 4;
  return t;
};

/** Coins lying thick on each other, for the body of the heap under the loose ones. */
const coinTexture = (): THREE.CanvasTexture => {
  const size = 512;
  const cv = document.createElement('canvas');
  cv.width = cv.height = size;
  const c = cv.getContext('2d')!;
  const rand = rng(53);
  c.fillStyle = '#6e4a10';
  c.fillRect(0, 0, size, size);
  for (let i = 0; i < 1500; i++) {
    const x = rand() * size;
    const y = rand() * size;
    const r = 13 + rand() * 7;
    const tilt = 0.45 + rand() * 0.55;
    const rot = rand() * 3.14;
    const g = 150 + rand() * 80;
    for (const [ox, oy] of [[0, 0], [size, 0], [-size, 0], [0, size], [0, -size]]) {
      c.beginPath();
      c.ellipse(x + ox, y + oy, r, r * tilt, rot, 0, Math.PI * 2);
      c.fillStyle = `rgb(${g + 18},${g * 0.74},${g * 0.24})`;
      c.fill();
      c.strokeStyle = 'rgba(70,42,6,0.75)';
      c.lineWidth = 1.6;
      c.stroke();
    }
  }
  const t = new THREE.CanvasTexture(cv);
  t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = 4;
  return t;
};

/** Distance to a capsule that tapers from `ra` to `rb`. */
const capsule = (p: THREE.Vector3, a: THREE.Vector3, b: THREE.Vector3, ra: number, rb: number): number => {
  const ab = b.clone().sub(a);
  const t = Math.min(1, Math.max(0, p.clone().sub(a).dot(ab) / ab.lengthSq()));
  return p.distanceTo(a.clone().addScaledVector(ab, t)) - (ra + (rb - ra) * t);
};

let skullGeo: { head: THREE.BufferGeometry; jaw: THREE.BufferGeometry; upper: THREE.BufferGeometry; lower: THREE.BufferGeometry } | null = null;

/** The sculpted parts of the skull, made once and shared by the cave and the trophy at camp. */
const skullParts = (): NonNullable<typeof skullGeo> => {
  if (skullGeo) return skullGeo;
  const V = (x: number, y: number, z: number): THREE.Vector3 => new THREE.Vector3(x, y, z);
  const p = new THREE.Vector3();
  const head = (x: number, yy: number, z: number): number => {
    // the skull is the same on both sides, and it faces -z
    p.set(Math.abs(x), yy, z);
    let d = capsule(p, V(0, 0.55, 0.9), V(0, 0.6, 0.4), 0.62, 0.55);
    d = blendShapes(d, capsule(p, V(0, 0.35, 0.2), V(0, 0.2, -1.5), 0.42, 0.2), 0.25);
    d = blendShapes(d, capsule(p, V(0.36, 0.78, 0.4), V(0.3, 0.66, -0.25), 0.2, 0.13), 0.14);
    d = blendShapes(d, capsule(p, V(0.52, 0.2, 0.6), V(0.24, 0.12, -0.7), 0.17, 0.1), 0.16);
    // the horns sweep back from the brow
    d = blendShapes(d, capsule(p, V(0.42, 0.9, 0.55), V(0.72, 1.2, 1.25), 0.13, 0.08), 0.1);
    d = blendShapes(d, capsule(p, V(0.72, 1.2, 1.25), V(0.72, 1.12, 1.95), 0.08, 0.015), 0.04);
    // the sockets, the nostrils and the hollow behind the eye
    const cut = (c: THREE.Vector3, r: number): void => {
      d = -blendShapes(-d, p.distanceTo(c) - r, 0.05);
    };
    cut(V(0.46, 0.6, 0.02), 0.27);
    cut(V(0.13, 0.42, -1.4), 0.08);
    cut(V(0.66, 0.4, 0.92), 0.22);
    return d;
  };
  const jaw = (x: number, yy: number, z: number): number => {
    p.set(Math.abs(x), yy, z);
    let d = capsule(p, V(0.52, -0.1, 0.95), V(0.14, -0.2, -1.32), 0.13, 0.09);
    d = blendShapes(d, capsule(p, V(0.52, -0.1, 0.95), V(0.58, 0.22, 1.05), 0.12, 0.09), 0.1);
    d = blendShapes(d, capsule(p, V(0, -0.2, -1.35), V(0.14, -0.2, -1.32), 0.09, 0.09), 0.1);
    return d;
  };
  const headGeo = solidSurface(head, V(-1.1, -0.4, -1.9), V(1.1, 1.5, 2.1), 0.045);
  const jawGeo = solidSurface(jaw, V(-0.85, -0.5, -1.6), V(0.85, 0.45, 1.25), 0.045);
  const tooth = (x: number, y: number, z: number, len: number, r: number, down: boolean, lean: number): THREE.BufferGeometry => {
    const t = new THREE.ConeGeometry(r, len, 7);
    t.translate(0, len / 2 - 0.04, 0); // the root sits just inside the bone
    t.rotateZ(lean);
    if (down) t.rotateX(Math.PI);
    t.translate(x, y, z);
    return t;
  };
  const upper: THREE.BufferGeometry[] = [];
  const lower: THREE.BufferGeometry[] = [];
  for (const s of [-1, 1]) {
    for (let i = 0; i < 10; i++) {
      const u = i / 9;
      // along the underside of the snout, from the tip back to the cheek
      const z = -1.5 + u * 1.55;
      const t = (0.2 - z) / 1.7;
      const axisY = 0.35 - 0.15 * t;
      const r = 0.42 - 0.22 * t;
      const big = i === 2 ? 1.5 : 1;
      upper.push(tooth(s * r * 0.72, axisY - r * 0.62, z, (0.2 + 0.06 * (1 - u)) * big, 0.042 * big, true, s * 0.12));
      // along the top of the lower jaw
      const jz = -1.3 + u * 1.5;
      const k = (0.95 - jz) / 2.27;
      lower.push(tooth(s * (0.52 - 0.38 * k), -0.1 - 0.1 * k + (0.13 - 0.04 * k) * 0.7, jz, (0.16 + 0.05 * (1 - u)) * big, 0.036 * big, false, -s * 0.1));
    }
  }
  skullGeo = { head: headGeo, jaw: jawGeo, upper: mergeGeometries(upper), lower: mergeGeometries(lower) };
  return skullGeo;
};

/** The great skull, with its horns and teeth. It is about four metres long at scale 1, and faces -z. */
export const buildMonsterSkull = (): THREE.Group => {
  const parts = skullParts();
  const g = new THREE.Group();
  const bone = new THREE.MeshStandardMaterial({ color: BONE, roughness: 0.85 });
  const enamel = new THREE.MeshStandardMaterial({ color: 0xf4eedd, roughness: 0.5 });
  const head = new THREE.Mesh(parts.head, bone);
  const upper = new THREE.Mesh(parts.upper, enamel);
  head.castShadow = true;
  head.receiveShadow = true;
  g.add(head, upper);
  // the lower jaw hangs open from a hinge at the back of the skull, and its teeth go with it
  const hinge = new THREE.Group();
  hinge.position.set(0, -0.1, 0.95);
  hinge.rotation.x = -0.2;
  const jaw = new THREE.Mesh(parts.jaw, bone);
  const lower = new THREE.Mesh(parts.lower, enamel);
  jaw.position.set(0, 0.1, -0.95);
  lower.position.copy(jaw.position);
  jaw.castShadow = true;
  jaw.receiveShadow = true;
  hinge.add(jaw, lower);
  g.add(hinge);
  // the dark inside the sockets
  const dark = new THREE.MeshBasicMaterial({ color: 0x14100c });
  for (const s of [-1, 1]) {
    const socket = new THREE.Mesh(new THREE.SphereGeometry(0.2, 12, 8), dark);
    socket.position.set(s * 0.34, 0.6, 0.06);
    g.add(socket);
  }
  return g;
};

/** How high the sand is at a point: it heaps up here and there, then runs down into the sea. */
const sandAt = (x: number, z: number): number =>
  CAVE_Y + 0.12 * Math.sin(x * 0.7 + z * 0.3) + 0.09 * Math.sin(z * 0.9 - x * 0.2) - Math.max(0, x - WATER_X + 2) * 0.3;

/** The gold has slumped into several overlapping piles, the way loose coins do, rather than one smooth dome. */
const PILES: [number, number, number, number][] = [
  [0, 0, 1.45, 2.3],
  [-1.7, 1.1, 0.95, 1.5],
  [1.6, -1.1, 0.9, 1.5],
  [0.8, 2, 0.6, 1.2],
  [-1, -1.9, 0.55, 1.2],
];

/** How high the heap stands at a point, measured from the middle of the heap. */
const heapHeight = (dx: number, dz: number): number => {
  let h = 0;
  for (const [x, z, a, w] of PILES) h += a * Math.exp(-((dx - x) ** 2 + (dz - z) ** 2) / (w * w));
  return h + 0.035 * Math.sin(dx * 5.3 + dz * 2.1) * Math.min(1, h * 2);
};

/** The cave behind the great door: open to the sea at the far end, with the bones of something very large lying around a heap of gold. */
export class SeaCave {
  readonly group = new THREE.Group();
  readonly colliders: { x: number; z: number; r: number }[] = [];
  private heap = new THREE.Group();
  private coins: THREE.InstancedMesh;
  /** the church bell, half sunk in the gold; it settles with the heap once the gold is taken */
  private bell!: THREE.Mesh;
  private daylight: THREE.PointLight;
  /** lights that can go off when the player is back in the stone room */
  readonly lights: THREE.PointLight[] = [];
  private clouds: { sprite: THREE.Sprite; speed: number }[] = [];
  private shark: THREE.Object3D | null = null;

  constructor() {
    this.buildRock();
    this.buildSand();
    this.buildBones();
    const gold = new THREE.MeshStandardMaterial({ color: 0xe0ae3a, roughness: 0.42, metalness: 0.45, emissive: 0x3a2604, emissiveIntensity: 0.6 });
    const piled = coinTexture();
    this.heap.position.set(HOARD.x, CAVE_Y, HOARD.z);
    this.group.add(this.heap);
    this.coins = this.buildHeap(piled);
    this.buildBars(gold);
    this.buildChests(piled);
    this.buildBell();
    this.colliders.push({ x: HOARD.x, z: HOARD.z, r: HOARD.r * 0.72 });
    // warm light on the gold, cold daylight from the sea
    const warm = new THREE.PointLight(0xffc268, 26, 16, 2);
    warm.position.set(HOARD.x - 3, CAVE_Y + 3.2, HOARD.z);
    this.daylight = new THREE.PointLight(0x8fd8ff, 40, 34, 2);
    this.daylight.position.set(31, CAVE_Y + 2.6, 0.5);
    this.group.add(warm, this.daylight);
    this.lights.push(warm, this.daylight);
  }

  /** A rough dome of rock with its far end broken open to the sea. */
  private buildRock(): void {
    const geo = new THREE.SphereGeometry(1, 72, 36, 0, Math.PI * 2, 0, Math.PI * 0.56);
    const pos = geo.attributes.position as THREE.BufferAttribute;
    const open: boolean[] = [];
    const col = new Float32Array(pos.count * 3);
    const shade = new THREE.Color();
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i);
      const y = pos.getY(i);
      const z = pos.getZ(i);
      const bump = 1 + 0.07 * Math.sin(x * 9 + y * 5) + 0.05 * Math.sin(z * 11 - y * 7) + 0.03 * Math.sin(x * 23 + z * 19) + 0.018 * Math.sin(x * 51 + y * 37) * Math.sin(z * 43);
      open.push(x > 0.62 && y < 0.62 && Math.abs(z) < 0.62);
      // the west end is cut back flat, so the stair from the door comes out at the foot of the wall
      const wx = Math.max(25 + x * 14.5 * bump, PORTAL_X + 0.12 * Math.sin(z * 9 + y * 7));
      pos.setXYZ(i, wx, CAVE_Y - 0.6 + y * 8 * bump, 0.5 + z * 12 * bump);
      shade.setRGB(0.92, 0.85, 0.76).multiplyScalar(0.72 + 0.4 * Math.abs(Math.sin(x * 13 + y * 9)));
      col.set([shade.r, shade.g, shade.b], i * 3);
    }
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    const index = geo.index!;
    const kept: number[] = [];
    for (let i = 0; i < index.count; i += 3) {
      const a = index.getX(i);
      const b = index.getX(i + 1);
      const c = index.getX(i + 2);
      if (!(open[a] && open[b] && open[c])) kept.push(a, b, c);
    }
    geo.setIndex(kept);
    geo.computeVertexNormals();
    const stone = rockTexture();
    const rockMat = new THREE.MeshStandardMaterial({ vertexColors: true, map: stone, bumpMap: stone, bumpScale: 3, roughness: 0.95, side: THREE.BackSide, flatShading: true });
    // the doorway: the rock simply is not there inside the opening of the stair
    rockMat.onBeforeCompile = (sh) => {
      sh.vertexShader = sh.vertexShader
        .replace('#include <common>', '#include <common>\nvarying vec3 vRock;')
        .replace('#include <begin_vertex>', '#include <begin_vertex>\nvRock = position;');
      sh.fragmentShader = sh.fragmentShader
        .replace('#include <common>', '#include <common>\nvarying vec3 vRock;')
        .replace('#include <map_fragment>', `#include <map_fragment>
        if (vRock.x < ${(PORTAL_X + 0.6).toFixed(2)} && abs(vRock.z - 0.5) < 1.2 && vRock.y < 0.7) discard;`);
    };
    const rock = new THREE.Mesh(geo, rockMat);
    rock.receiveShadow = true;
    this.group.add(rock);
  }

  private buildSand(): void {
    const geo = new THREE.PlaneGeometry(31, 30, 62, 60);
    geo.rotateX(-Math.PI / 2);
    geo.translate(13 + 31 / 2, 0, 0.5);
    const pos = geo.attributes.position as THREE.BufferAttribute;
    for (let i = 0; i < pos.count; i++) pos.setY(i, sandAt(pos.getX(i), pos.getZ(i)));
    geo.computeVertexNormals();
    const sand = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ color: 0xc9b08a, roughness: 1 }));
    sand.receiveShadow = true;
    this.group.add(sand);
  }

  /** The real sea, seen through the mouth of the cave. */
  addSea(sheet: THREE.Mesh): void {
    sheet.position.set(WATER_X - 2 + 350, WATER_Y, 0.5);
    this.group.add(sheet);
  }

  /** The sky's own clouds, drifting low enough over the water to be seen from in here. */
  addClouds(mats: THREE.SpriteMaterial[]): void {
    const rand = rng(29);
    for (let i = 0; i < 9; i++) {
      const sprite = new THREE.Sprite(mats[i % mats.length]);
      sprite.scale.set(55 + rand() * 60, 16 + rand() * 12, 1);
      sprite.position.set(120 + rand() * 170, 24 + rand() * 40, -130 + rand() * 260);
      this.group.add(sprite);
      this.clouds.push({ sprite, speed: 0.8 + rand() * 1.2 });
    }
  }

  /** One shark, cruising to and fro beyond the mouth of the cave. */
  addShark(model: THREE.Object3D): void {
    // a copy of just its shape: the original is tied to the game's targets, and this one is only for looking at
    const copy = (o: THREE.Object3D): THREE.Object3D => {
      const c = o instanceof THREE.Mesh ? new THREE.Mesh(o.geometry, o.material) : new THREE.Group();
      c.position.copy(o.position);
      c.quaternion.copy(o.quaternion);
      c.scale.copy(o.scale);
      for (const k of o.children) c.add(copy(k));
      return c;
    };
    this.shark = copy(model);
    this.group.add(this.shark);
  }

  /** The skeleton lies in a long curve around the back of the cave, its ribs standing up out of the sand. */
  private buildBones(): void {
    const mat = new THREE.MeshStandardMaterial({ color: BONE, roughness: 0.85 });
    const spine = new THREE.CatmullRomCurve3([
      new THREE.Vector3(17.3, CAVE_Y + 1.1, -4.3),
      new THREE.Vector3(19.5, CAVE_Y + 2.1, -5.8),
      new THREE.Vector3(22.5, CAVE_Y + 2.7, -6.3),
      new THREE.Vector3(25.5, CAVE_Y + 2.5, -5.9),
      new THREE.Vector3(28, CAVE_Y + 1.6, -4.6),
      new THREE.Vector3(29.4, CAVE_Y + 0.7, -2.4),
      new THREE.Vector3(29, CAVE_Y + 0.25, -0.2),
    ]);
    const parts: THREE.BufferGeometry[] = [];
    const up = new THREE.Vector3(0, 1, 0);
    // the backbone, knob by knob, getting smaller toward the tail
    const knobs = 46;
    for (let i = 0; i <= knobs; i++) {
      const u = i / knobs;
      const at = spine.getPointAt(u);
      const dir = spine.getTangentAt(u);
      const r = 0.34 * (1 - u * 0.78);
      const knob = new THREE.CylinderGeometry(r, r * 0.92, 0.3 - u * 0.12, 10);
      knob.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(up, dir));
      knob.translate(at.x, at.y, at.z);
      parts.push(knob);
      if (u < 0.72) {
        const spike = new THREE.ConeGeometry(0.1 * (1 - u * 0.6), 0.75 * (1 - u * 0.6), 6);
        spike.translate(at.x, at.y + 0.42 * (1 - u * 0.5), at.z);
        parts.push(spike);
      }
    }
    // the ribs arch out and down on both sides, longest in the middle
    for (let i = 0; i < 13; i++) {
      const u = 0.1 + (i / 12) * 0.58;
      const at = spine.getPointAt(u);
      const dir = spine.getTangentAt(u);
      const side = new THREE.Vector3().crossVectors(dir, up).normalize();
      const size = (1.4 + 1.5 * Math.sin((i / 12) * Math.PI)) * 1.05;
      for (const s of [-1, 1]) {
        const pts = [0, 0.3, 0.6, 0.85, 1].map((k) => {
          const out = Math.sin(k * Math.PI * 0.62) * size * 1.2;
          const y = Math.max(sandAt(at.x, at.z) - 0.1, at.y + Math.sin(k * 1.4) * size * 0.2 - k * k * (at.y - CAVE_Y + 0.3));
          return at.clone().addScaledVector(side, s * out).setY(y);
        });
        parts.push(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 14, 0.1 - i * 0.002, 7));
      }
      this.colliders.push({ x: at.x, z: at.z, r: 2.1 });
    }
    const merged = mergeGeometries(parts.map((p) => (p.index ? p.toNonIndexed() : p)).map((p) => {
      p.deleteAttribute('uv');
      return p;
    }));
    const bones = new THREE.Mesh(merged, mat);
    bones.castShadow = true;
    bones.receiveShadow = true;
    this.group.add(bones);
    // the skull rests on the sand at the end of the neck, jaw open toward whoever comes in
    const skull = buildMonsterSkull();
    skull.scale.setScalar(1.25);
    skull.position.set(15.9, CAVE_Y + 0.72, -2.9);
    skull.rotation.set(0, Math.PI * 0.72, 0.12);
    this.group.add(skull);
    this.colliders.push({ x: 15.9, z: -2.9, r: 2.2 });
  }

  /** The heap: a body of piled coins, thousands of loose ones lying on its slopes, and a few jewels among them. */
  private buildHeap(piled: THREE.CanvasTexture): THREE.InstancedMesh {
    const body = new THREE.PlaneGeometry(12, 12, 90, 90);
    body.rotateX(-Math.PI / 2);
    const pos = body.attributes.position as THREE.BufferAttribute;
    const ground = (dx: number, dz: number): number => sandAt(HOARD.x + dx, HOARD.z + dz) - CAVE_Y;
    for (let i = 0; i < pos.count; i++) {
      const dx = pos.getX(i);
      const dz = pos.getZ(i);
      const h = heapHeight(dx, dz);
      pos.setY(i, ground(dx, dz) + (h < 0.03 ? -0.3 : h));
    }
    body.computeVertexNormals();
    const uv = body.attributes.uv as THREE.BufferAttribute;
    for (let i = 0; i < uv.count; i++) uv.setXY(i, uv.getX(i) * 7, uv.getY(i) * 7);
    const bodyMat = new THREE.MeshStandardMaterial({ map: piled, bumpMap: piled, bumpScale: 2, roughness: 0.5, metalness: 0.35, emissive: 0x2c1c04, emissiveIntensity: 0.5 });
    const mesh = new THREE.Mesh(body, bodyMat);
    mesh.receiveShadow = true;
    this.heap.add(mesh);

    const place = (d: THREE.Object3D, dx: number, dz: number, tilt: number, rand: () => number, lift: number): void => {
      const e = 0.08;
      const n = new THREE.Vector3(heapHeight(dx - e, dz) - heapHeight(dx + e, dz), 2 * e, heapHeight(dx, dz - e) - heapHeight(dx, dz + e)).normalize();
      d.position.set(dx, ground(dx, dz) + heapHeight(dx, dz) + lift, dz);
      d.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), n);
      d.rotateY(rand() * 6.28);
      d.rotateX((rand() - 0.5) * tilt);
    };

    const N = 5200;
    const coins = new THREE.InstancedMesh(new THREE.CylinderGeometry(0.075, 0.075, 0.016, 10), new THREE.MeshStandardMaterial({ roughness: 0.34, metalness: 0.5, emissive: 0x3a2604, emissiveIntensity: 0.55 }), N);
    const rand = rng(41);
    const d = new THREE.Object3D();
    const a = new THREE.Color(0xf2c64e);
    const b = new THREE.Color(0xb98422);
    const tint = new THREE.Color();
    for (let i = 0; i < N; i++) {
      const ang = rand() * Math.PI * 2;
      const r = Math.sqrt(rand()) * 5;
      const dx = Math.cos(ang) * r;
      const dz = Math.sin(ang) * r;
      // loose coins are rare out at the edge, and lie flat there
      place(d, dx, dz, heapHeight(dx, dz) < 0.05 ? 0.25 : 0.9, rand, 0.02);
      d.updateMatrix();
      coins.setMatrixAt(i, d.matrix);
      coins.setColorAt(i, tint.copy(a).lerp(b, rand()));
    }
    this.heap.add(coins);

    // a scatter of cut stones among the coins
    const jewels = new THREE.InstancedMesh(new THREE.OctahedronGeometry(0.1), new THREE.MeshStandardMaterial({ roughness: 0.15, metalness: 0.2 }), 70);
    const shades = [0xd32f4b, 0x2f9e6e, 0x3f7fd9, 0xe8e2d4];
    for (let i = 0; i < 70; i++) {
      const ang = rand() * Math.PI * 2;
      const r = Math.sqrt(rand()) * 3.2;
      place(d, Math.cos(ang) * r, Math.sin(ang) * r, 1.2, rand, 0.05);
      d.updateMatrix();
      jewels.setMatrixAt(i, d.matrix);
      jewels.setColorAt(i, tint.set(shades[i % 4]));
    }
    this.heap.add(jewels);
    return coins;
  }

  /** The town's forty bars, stacked as they were loaded and slumped where the sea left them. */
  private buildBars(gold: THREE.Material): void {
    const bars = new THREE.InstancedMesh(new THREE.BoxGeometry(0.42, 0.13, 0.2), gold, 40);
    const rand = rng(77);
    const d = new THREE.Object3D();
    for (let i = 0; i < 40; i++) {
      const layer = i < 22 ? 0 : i < 34 ? 1 : 2;
      const a = 3.6 + (i % 12) * 0.13 + rand() * 0.05;
      const r = 3.9 + (i % 4) * 0.27;
      const x = HOARD.x + Math.cos(a) * r;
      const z = HOARD.z + Math.sin(a) * r;
      d.position.set(x, sandAt(x, z) + heapHeight(x - HOARD.x, z - HOARD.z) * 0.6 + 0.07 + layer * 0.14, z);
      d.rotation.set(0, a + rand() * 0.5, (rand() - 0.5) * 0.12);
      d.updateMatrix();
      bars.setMatrixAt(i, d.matrix);
    }
    bars.castShadow = true;
    this.group.add(bars);
    this.colliders.push({ x: HOARD.x - 3.9, z: HOARD.z - 1.1, r: 1 });
  }

  /** Two sea chests thrown open, each a real box with a curved lid on a hinge, full to the top. */
  private buildChests(piled: THREE.CanvasTexture): void {
    const wood = new THREE.MeshStandardMaterial({ color: 0x5a3a26, roughness: 0.9 });
    const iron = new THREE.MeshStandardMaterial({ color: 0x35302c, roughness: 0.55, metalness: 0.6 });
    const inside = new THREE.MeshStandardMaterial({ map: piled, bumpMap: piled, bumpScale: 2, roughness: 0.5, metalness: 0.35, emissive: 0x2c1c04, emissiveIntensity: 0.5 });
    const L = 1.3;
    const D = 0.8;
    const H = 0.62;
    const T = 0.06;
    /** the lid's end shape: flat along the bottom, arched over the top, with the hinge at the back edge */
    const arch = (grow: number): THREE.Shape => {
      const o = new THREE.Shape();
      o.moveTo(-grow, 0);
      o.lineTo(D + grow, 0);
      o.absellipse(D / 2, 0, D / 2 + grow, 0.3 + grow, 0, Math.PI, false, 0);
      o.closePath();
      return o;
    };
    const lidPiece = (grow: number, wide: number, x: number, mat: THREE.Material): THREE.Mesh => {
      const geo = new THREE.ExtrudeGeometry(arch(grow), { depth: wide, bevelEnabled: false, curveSegments: 14 });
      geo.rotateY(-Math.PI / 2);
      geo.translate(x + wide / 2, 0, 0);
      const m = new THREE.Mesh(geo, mat);
      m.castShadow = true;
      return m;
    };
    for (const [x, z, rot] of [[27.6, 5, -0.5], [21.5, 5.6, 0.7]] as const) {
      const g = new THREE.Group();
      const part = (w: number, h: number, d: number, px: number, py: number, pz: number, mat: THREE.Material): void => {
        const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
        m.position.set(px, py, pz);
        m.castShadow = true;
        m.receiveShadow = true;
        g.add(m);
      };
      part(L, T, D, 0, T / 2, 0, wood);
      for (const s of [-1, 1]) {
        part(L, H, T, 0, H / 2, (s * (D - T)) / 2, wood);
        part(T, H, D - T * 2, (s * (L - T)) / 2, H / 2, 0, wood);
        // iron straps round the outside
        for (const bx of [-0.42, 0.42]) part(0.1, H + 0.02, 0.015, bx, H / 2, (s * (D + 0.015)) / 2, iron);
      }
      // heaped full of coins
      const fill = new THREE.Mesh(new THREE.SphereGeometry(1, 20, 10, 0, Math.PI * 2, 0, Math.PI / 2), inside);
      fill.scale.set((L - T * 2) / 2, 0.2, (D - T * 2) / 2);
      fill.position.y = H - 0.03;
      g.add(fill);
      const lid = new THREE.Group();
      lid.position.set(0, H, -D / 2);
      lid.rotation.x = -1.95;
      lid.add(lidPiece(0, L, 0, wood));
      for (const bx of [-0.42, 0.42]) lid.add(lidPiece(0.015, 0.1, bx, iron));
      g.add(lid);
      g.position.set(x, sandAt(x, z), z);
      g.rotation.y = rot;
      this.group.add(g);
      this.colliders.push({ x, z, r: 1 });
    }
  }

  /** The town's old church bell, half sunk in the gold. */
  private buildBell(): void {
    const profile = [[0, 1.1], [0.14, 1.08], [0.24, 0.96], [0.3, 0.7], [0.36, 0.4], [0.5, 0.12], [0.6, 0]].map(([r, y]) => new THREE.Vector2(r, y));
    const bronze = new THREE.MeshStandardMaterial({ color: 0x8a6a3a, roughness: 0.45, metalness: 0.75, side: THREE.DoubleSide });
    const bell = new THREE.Mesh(new THREE.LatheGeometry(profile, 24), bronze);
    bell.rotation.set(0.5, 0, -0.42);
    bell.castShadow = true;
    this.group.add(bell);
    this.bell = bell;
    this.settleBell(1);
  }

  /** Sits the bell into the heap, which stands squash of its full height (the heap is scaled from the cave floor up). */
  private settleBell(squash: number): void {
    const x = HOARD.x + 1.9;
    const z = HOARD.z + 1.4;
    // on whichever is higher there: what is left of the gold, or the sand under it
    const top = Math.max(sandAt(x, z), CAVE_Y + (sandAt(x, z) - CAVE_Y + heapHeight(1.9, 1.4)) * squash);
    this.bell.position.set(x, top - 0.15, z);
  }

  /** The height of the sand under a point. */
  floorAt(x: number, z: number): number {
    return sandAt(x, z);
  }

  /** Keeps a walker on the sand of the cave. */
  clamp(x: number, z: number): { x: number; z: number } {
    const nx = (x - CAVE.x) / CAVE.rx;
    const nz = (z - CAVE.z) / CAVE.rz;
    const d = Math.hypot(nx, nz);
    const k = d > 1 ? 1 / d : 1;
    return { x: Math.min(WATER_X, CAVE.x + nx * k * CAVE.rx), z: CAVE.z + nz * k * CAVE.rz };
  }

  /** True when close enough to reach the gold. */
  nearGold(x: number, z: number): boolean {
    return Math.hypot(x - HOARD.x, z - HOARD.z) < HOARD.r + 1.5;
  }

  /** Once it has been taken only a thin scattering is left, so the cave stays a place worth visiting. */
  setTaken(on: boolean): void {
    this.coins.count = on ? 1400 : 5200;
    this.heap.scale.y = on ? 0.3 : 1;
    // the bell is not part of the heap, so it is lowered with it rather than left hanging where the gold used to be
    this.settleBell(this.heap.scale.y);
  }

  /** How much daylight comes in from the sea: 0 at night .. 1 by day. */
  setDaylight(k: number): void {
    this.daylight.intensity = 3 + 37 * k;
  }

  update(t: number, dt = 0): void {
    for (const c of this.clouds) {
      c.sprite.position.z += c.speed * dt;
      if (c.sprite.position.z > 150) c.sprite.position.z = -150;
    }
    if (this.shark) {
      // a long lazy oval out in the bay; the fin shows above the water and the rest stays under it
      const a = t * 0.13;
      const x = 66 + Math.cos(a) * 16;
      const z = 0.5 + Math.sin(a) * 26;
      this.shark.position.set(x, WATER_Y - 0.24, z);
      this.shark.rotation.set(0, Math.atan2(Math.sin(a) * 16, -Math.cos(a) * 26), 0);
    }
  }
}

/** The stone-framed iron grate on the beach, over the shaft that lights the great door. */
export const buildGrate = (): THREE.Group => {
  const g = new THREE.Group();
  const stone = new THREE.MeshStandardMaterial({ color: 0x9a9184, roughness: 0.95 });
  const iron = new THREE.MeshStandardMaterial({ color: 0x33302c, roughness: 0.55, metalness: 0.6 });
  const dark = new THREE.MeshBasicMaterial({ color: 0x0a0806 });
  const W = 1.7;
  const D = 1.3;
  const rim = 0.32;
  const block = (w: number, d: number, x: number, z: number): void => {
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, 0.42, d), stone);
    m.position.set(x, 0.03, z);
    m.castShadow = true;
    m.receiveShadow = true;
    g.add(m);
  };
  block(W + rim * 2, rim, 0, -(D + rim) / 2);
  block(W + rim * 2, rim, 0, (D + rim) / 2);
  block(rim, D, -(W + rim) / 2, 0);
  block(rim, D, (W + rim) / 2, 0);
  // the dark of the shaft below
  const pit = new THREE.Mesh(new THREE.PlaneGeometry(W, D), dark);
  pit.rotation.x = -Math.PI / 2;
  pit.position.y = -0.02;
  g.add(pit);
  for (let i = 1; i < 5; i++) {
    const bar = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.07, D + 0.1), iron);
    bar.position.set(-W / 2 + (W * i) / 5, 0.17, 0);
    bar.castShadow = true;
    g.add(bar);
  }
  for (let i = 1; i < 4; i++) {
    const bar = new THREE.Mesh(new THREE.BoxGeometry(W + 0.1, 0.07, 0.06), iron);
    bar.position.set(0, 0.2, -D / 2 + (D * i) / 4);
    bar.castShadow = true;
    g.add(bar);
  }
  return g;
};
