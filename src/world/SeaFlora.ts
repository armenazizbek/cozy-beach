import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

type Rand = () => number;

const flora = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.8, side: THREE.DoubleSide });
const Y = new THREE.Vector3(0, 1, 0);

/** Keeps only the attributes every piece shares, so pieces can be merged into one mesh. */
const clean = (g: THREE.BufferGeometry): THREE.BufferGeometry => {
  g.deleteAttribute('uv');
  return g;
};

/** Colours a piece from `lo` at its lowest point to `hi` at `top`. */
const paint = (g: THREE.BufferGeometry, lo: THREE.Color, hi: THREE.Color, top: number, bottom = 0): THREE.BufferGeometry => {
  const p = g.attributes.position as THREE.BufferAttribute;
  const col = new Float32Array(p.count * 3);
  const c = new THREE.Color();
  for (let i = 0; i < p.count; i++) {
    const t = Math.min(1, Math.max(0, (p.getY(i) - bottom) / Math.max(0.001, top - bottom)));
    c.copy(lo).lerp(hi, t);
    col.set([c.r, c.g, c.b], i * 3);
  }
  g.setAttribute('color', new THREE.BufferAttribute(col, 3));
  return g;
};

const finish = (parts: THREE.BufferGeometry[]): THREE.Mesh => {
  const geo = mergeGeometries(parts, false)!;
  geo.computeVertexNormals();
  return new THREE.Mesh(geo, flora);
};

/** A flat, tapering leaf that curves over and twists a little. */
const blade = (len: number, width: number, bend: number, twist: number, segs: number, rand: Rand): THREE.BufferGeometry => {
  const pos: number[] = [];
  const idx: number[] = [];
  const phase = rand() * 6;
  for (let i = 0; i <= segs; i++) {
    const t = i / segs;
    const w = (width / 2) * (1 - Math.pow(t, 1.7) * 0.92) * (0.85 + 0.15 * Math.sin(t * 9 + phase));
    const cx = bend * len * t * t + Math.sin(t * 5 + phase) * 0.04 * len * t;
    const cy = t * len * (1 - 0.12 * t * Math.abs(bend));
    const cz = Math.sin(t * twist + phase) * 0.05 * len * t;
    const a = Math.sin(t * twist * 0.8 + phase) * 0.9;
    pos.push(cx - Math.cos(a) * w, cy, cz - Math.sin(a) * w, cx + Math.cos(a) * w, cy, cz + Math.sin(a) * w);
    if (i < segs) {
      const k = i * 2;
      idx.push(k, k + 1, k + 2, k + 1, k + 3, k + 2);
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setIndex(idx);
  g.computeVertexNormals();
  return g;
};

const tuft = (count: number, len: [number, number], width: number, spread: number, segs: number, twist: number, lo: THREE.Color, hi: THREE.Color, rand: Rand): THREE.Mesh => {
  const parts: THREE.BufferGeometry[] = [];
  let top = 0;
  const made: { g: THREE.BufferGeometry; l: number }[] = [];
  for (let i = 0; i < count; i++) {
    const l = len[0] + rand() * (len[1] - len[0]);
    const g = blade(l, width * (0.8 + rand() * 0.5), (rand() - 0.5) * 0.7, twist, segs, rand);
    g.rotateY(rand() * Math.PI * 2);
    g.translate((rand() - 0.5) * spread, 0, (rand() - 0.5) * spread);
    top = Math.max(top, l);
    made.push({ g, l });
  }
  for (const m of made) {
    const shade = new THREE.Color().copy(lo).offsetHSL((rand() - 0.5) * 0.04, 0, (rand() - 0.5) * 0.08);
    parts.push(paint(m.g, shade, hi, top));
  }
  return finish(parts);
};

/** A low clump of thin grass-like blades. */
export const seagrass = (rand: Rand): THREE.Mesh =>
  tuft(9 + Math.floor(rand() * 7), [0.45, 1.1], 0.06, 0.35, 5, 3, new THREE.Color(0x2c6b3c), new THREE.Color(0xa6d86a), rand);

/** Tall, wide golden-green ribbons that sway. */
export const kelp = (rand: Rand): THREE.Mesh =>
  tuft(3 + Math.floor(rand() * 3), [2.4, 4.4], 0.3, 0.3, 14, 4, new THREE.Color(0x3c5a22), new THREE.Color(0xc4cf4a), rand);

/** A short clump of broad, bright blades (eel grass). */
export const eelgrass = (rand: Rand): THREE.Mesh =>
  tuft(5 + Math.floor(rand() * 3), [0.9, 1.7], 0.16, 0.3, 8, 2, new THREE.Color(0x287a4a), new THREE.Color(0x7fe08a), rand);

const branch = (parts: THREE.BufferGeometry[], from: THREE.Vector3, dir: THREE.Vector3, len: number, r: number, depth: number, rand: Rand): void => {
  const g = clean(new THREE.CylinderGeometry(r * 0.66, r, len, 7, 1));
  g.translate(0, len / 2, 0);
  g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(Y, dir));
  g.translate(from.x, from.y, from.z);
  parts.push(g);
  const end = from.clone().addScaledVector(dir, len);
  if (depth <= 0) {
    const t = clean(new THREE.SphereGeometry(r * 0.7, 7, 5));
    t.translate(end.x, end.y, end.z);
    parts.push(t);
    return;
  }
  const n = rand() < 0.35 ? 3 : 2;
  for (let i = 0; i < n; i++) {
    const axis = new THREE.Vector3(rand() - 0.5, 0, rand() - 0.5).normalize();
    const d = dir.clone().applyAxisAngle(axis, 0.4 + rand() * 0.45);
    d.y += 0.25;
    branch(parts, end, d.normalize(), len * (0.7 + rand() * 0.12), r * 0.66, depth - 1, rand);
  }
};

/** Antler-shaped branching coral that thins toward pale tips. */
export const staghorn = (rand: Rand, lo: THREE.Color, hi: THREE.Color): THREE.Mesh => {
  const parts: THREE.BufferGeometry[] = [];
  const n = 2 + Math.floor(rand() * 2);
  for (let i = 0; i < n; i++) {
    const a = rand() * Math.PI * 2;
    const dir = new THREE.Vector3(Math.cos(a) * 0.35, 1, Math.sin(a) * 0.35).normalize();
    branch(parts, new THREE.Vector3((rand() - 0.5) * 0.25, 0, (rand() - 0.5) * 0.25), dir, 0.34 + rand() * 0.1, 0.07, 3, rand);
  }
  let top = 0;
  for (const g of parts) {
    g.computeBoundingBox();
    top = Math.max(top, g.boundingBox!.max.y);
  }
  parts.forEach((g) => paint(g, lo, hi, top));
  return finish(parts);
};

/** A rounded dome covered in winding grooves. */
export const brainCoral = (rand: Rand, lo: THREE.Color, hi: THREE.Color): THREE.Mesh => {
  const g = clean(new THREE.SphereGeometry(0.5, 30, 20, 0, Math.PI * 2, 0, Math.PI / 2));
  const p = g.attributes.position as THREE.BufferAttribute;
  const col = new Float32Array(p.count * 3);
  const ph = rand() * 10;
  const c = new THREE.Color();
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i);
    const y = p.getY(i);
    const z = p.getZ(i);
    const d = Math.sin(24 * x + 4 * Math.sin(15 * z + ph) + 8 * y) * Math.sin(20 * z + 3 * Math.sin(13 * x + ph) + 6 * y);
    const k = 1 + 0.045 * d;
    p.setXYZ(i, x * k, y * k * 0.75, z * k);
    c.copy(lo).lerp(hi, d * 0.5 + 0.5);
    col.set([c.r, c.g, c.b], i * 3);
  }
  g.setAttribute('color', new THREE.BufferAttribute(col, 3));
  const m = finish([g]);
  return m;
};

/** A ring of open tubes of different heights. */
export const tubeCoral = (rand: Rand, lo: THREE.Color, hi: THREE.Color): THREE.Mesh => {
  const parts: THREE.BufferGeometry[] = [];
  const n = 6 + Math.floor(rand() * 5);
  for (let i = 0; i < n; i++) {
    const h = 0.3 + rand() * 0.5;
    const r = 0.055 + rand() * 0.035;
    const a = rand() * Math.PI * 2;
    const d = 0.05 + rand() * 0.22;
    const tube = clean(new THREE.CylinderGeometry(r, r * 1.15, h, 10, 1, true));
    tube.translate(0, h / 2, 0);
    const rim = clean(new THREE.TorusGeometry(r, r * 0.22, 6, 12));
    rim.rotateX(Math.PI / 2);
    rim.translate(0, h, 0);
    const lean = new THREE.Euler((rand() - 0.5) * 0.35, 0, (rand() - 0.5) * 0.35);
    for (const g of [tube, rim]) {
      g.applyMatrix4(new THREE.Matrix4().makeRotationFromEuler(lean));
      g.translate(Math.cos(a) * d, 0, Math.sin(a) * d);
      paint(g, lo, hi, 0.8);
      parts.push(g);
    }
  }
  return finish(parts);
};

let fanTex: THREE.CanvasTexture | null = null;

const fanTexture = (): THREE.CanvasTexture => {
  if (fanTex) return fanTex;
  const cv = document.createElement('canvas');
  cv.width = cv.height = 256;
  const c = cv.getContext('2d')!;
  c.strokeStyle = '#fff';
  c.lineCap = 'round';
  const cx = 128;
  const cy = 252;
  const polar = (r: number, a: number): [number, number] => [cx + Math.sin(a) * r, cy - Math.cos(a) * r];
  // a short stalk, then rays that spread out and fork, joined by wavy cross-strands: a lacy net in the shape of a fan
  c.lineWidth = 9;
  c.beginPath();
  c.moveTo(cx, 256);
  c.lineTo(...polar(48, 0));
  c.stroke();
  const spread = 1.0;
  const rays = 11;
  for (let i = 0; i < rays; i++) {
    const a = -spread + (2 * spread * i) / (rays - 1);
    c.lineWidth = 4;
    c.beginPath();
    c.moveTo(...polar(46, a * 0.3));
    c.lineTo(...polar(120, a * 0.85));
    c.lineTo(...polar(236, a));
    c.stroke();
    // a fork between this ray and the next
    if (i < rays - 1) {
      const m = a + spread / (rays - 1);
      c.lineWidth = 3;
      c.beginPath();
      c.moveTo(...polar(125, m * 0.9));
      c.lineTo(...polar(236, m));
      c.stroke();
    }
  }
  c.lineWidth = 3;
  for (let r = 70; r < 240; r += 24) {
    c.beginPath();
    for (let k = 0; k <= 40; k++) {
      const a = -spread * (0.55 + r / 520) + (2 * spread * (0.55 + r / 520) * k) / 40;
      const rr = r + Math.sin(k * 1.9 + r) * 2.5;
      const [x, y] = polar(rr, a);
      if (k === 0) c.moveTo(x, y);
      else c.lineTo(x, y);
    }
    c.stroke();
  }
  fanTex = new THREE.CanvasTexture(cv);
  fanTex.colorSpace = THREE.SRGBColorSpace;
  return fanTex;
};

/** A lacy sea fan that stands on a short stalk. */
export const seaFan = (color: number): THREE.Group => {
  const g = new THREE.Group();
  const fan = new THREE.Mesh(
    new THREE.PlaneGeometry(1.25, 1.25),
    new THREE.MeshStandardMaterial({ map: fanTexture(), color, alphaTest: 0.45, side: THREE.DoubleSide, roughness: 0.8 }),
  );
  fan.position.y = 0.62;
  g.add(fan);
  return g;
};
