import * as THREE from 'three';
import { DEF_BY_ID } from '../core/items';
import { mergeGeometries, mergeVertices } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

const metal = (color: number, rough = 0.3, m = 0.9): THREE.MeshStandardMaterial =>
  new THREE.MeshStandardMaterial({ color, roughness: rough, metalness: m });

const add = (g: THREE.Group, geo: THREE.BufferGeometry, mat: THREE.Material, x = 0, y = 0, z = 0): THREE.Mesh => {
  const m = new THREE.Mesh(geo, mat);
  m.position.set(x, y, z);
  m.castShadow = true;
  g.add(m);
  return m;
};

const torusFlat = (r: number, tube: number, arc = Math.PI * 2): THREE.TorusGeometry => {
  const t = new THREE.TorusGeometry(r, tube, 10, 36, arc);
  t.rotateX(Math.PI / 2);
  return t;
};

/** A flat piece cut from a closed outline with optional holes, with softly rounded edges. It lies in the x-z plane. */
const plate = (outline: [number, number][], holes: [number, number][][], depth: number, bevel = 0.012): THREE.BufferGeometry => {
  const sh = new THREE.Shape(outline.map(([x, y]) => new THREE.Vector2(x, y)));
  for (const h of holes) sh.holes.push(new THREE.Path(h.map(([x, y]) => new THREE.Vector2(x, y))));
  const g = new THREE.ExtrudeGeometry(sh, { depth, bevelEnabled: true, bevelSize: bevel, bevelThickness: bevel, bevelSegments: 2, curveSegments: 16 });
  g.rotateX(-Math.PI / 2);
  g.translate(0, -depth / 2, 0);
  return g;
};

/** Points on an ellipse, for building outlines and holes. */
const ellipse = (cx: number, cy: number, rx: number, ry: number, from = 0, to = Math.PI * 2, n = 28): [number, number][] =>
  Array.from({ length: n + 1 }, (_, i) => {
    const a = from + ((to - from) * i) / n;
    return [cx + Math.cos(a) * rx, cy + Math.sin(a) * ry] as [number, number];
  });

/** Gives a piece one colour, so differently coloured pieces can be merged into one mesh. */
const tint = (g: THREE.BufferGeometry, color: number): THREE.BufferGeometry => {
  g.deleteAttribute('uv');
  const c = new THREE.Color(color);
  const n = (g.attributes.position as THREE.BufferAttribute).count;
  const col = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) col.set([c.r, c.g, c.b], i * 3);
  g.setAttribute('color', new THREE.BufferAttribute(col, 3));
  return g;
};

const COIN_STYLE: Record<string, { color: number; rough: number; size?: number }> = {
  penny: { color: 0xb87333, rough: 0.38 },
  quarter: { color: 0xd9dde2, rough: 0.28 },
  doubloon: { color: 0xffc933, rough: 0.2 },
  dime: { color: 0xcfd4d9, rough: 0.3, size: 0.78 },
  nickel: { color: 0xa8b0b7, rough: 0.35, size: 0.88 },
  silver_dollar: { color: 0xe8ecef, rough: 0.24, size: 1.15 },
  real: { color: 0xc6cbd0, rough: 0.42, size: 1.0 },
  denarius: { color: 0x8f6f45, rough: 0.5, size: 0.85 },
  sovereign: { color: 0xffd85a, rough: 0.16, size: 0.95 },
  piece_eight: { color: 0xcfcab0, rough: 0.45, size: 1.05 },
};

const buildCoin = (g: THREE.Group, id: string): void => {
  const s = COIN_STYLE[id];
  const mat = metal(s.color, s.rough);
  g.scale.setScalar(s.size ?? 1);
  add(g, new THREE.CylinderGeometry(0.35, 0.35, 0.06, 32), mat);
  for (const y of [0.032, -0.032]) add(g, torusFlat(0.31, 0.017), mat, 0, y, 0);
  // embossed centre
  add(g, new THREE.CylinderGeometry(0.15, 0.15, 0.02, 20), mat, 0, 0.04, 0);
  add(g, new THREE.OctahedronGeometry(0.07), mat, 0, 0.065, 0);
  add(g, new THREE.CylinderGeometry(0.12, 0.12, 0.02, 20), mat, 0, -0.04, 0);
};

/** A soda-can pull-tab: a rounded plate with a big finger ring, a small rivet hole and a slot, bent very slightly. */
const buildPullTab = (g: THREE.Group): void => {
  const mat = metal(0xd3d7da, 0.28);
  const left = ellipse(-0.2, 0, 0.19, 0.17, Math.PI / 2, (3 * Math.PI) / 2, 24);
  const right = ellipse(0.22, 0, 0.12, 0.1, -Math.PI / 2, Math.PI / 2, 16);
  const body = add(g, plate([...left, ...right], [ellipse(-0.2, 0, 0.115, 0.085), ellipse(0.2, 0, 0.032, 0.032, 0, Math.PI * 2, 14), ellipse(0.03, 0, 0.055, 0.02, 0, Math.PI * 2, 14)], 0.03, 0.01), mat);
  body.rotation.z = 0.04;
  // a raised rim round the finger ring, and the little tongue that pried the can open
  const rim = add(g, new THREE.TorusGeometry(0.14, 0.012, 6, 28), mat, -0.2, 0.025, 0);
  rim.rotation.x = Math.PI / 2;
  rim.scale.set(1, 0.9, 1);
  const tongue = add(g, new THREE.BoxGeometry(0.1, 0.025, 0.09), mat, 0.33, 0.012, 0);
  tongue.rotation.z = -0.15;
};

/** A long rusty nail with a flat head and a bend in the shaft, tapering to a point. */
const buildNail = (g: THREE.Group): void => {
  const rust = new THREE.MeshStandardMaterial({ color: 0x8a4b22, roughness: 0.9, metalness: 0.35 });
  const dark = new THREE.MeshStandardMaterial({ color: 0x5e3216, roughness: 0.95, metalness: 0.2 });
  const path = new THREE.CatmullRomCurve3([
    new THREE.Vector3(-0.3, 0, 0), new THREE.Vector3(-0.1, 0, 0), new THREE.Vector3(0.12, 0, 0.0), new THREE.Vector3(0.26, 0, 0.0),
    new THREE.Vector3(0.36, 0, 0.07), new THREE.Vector3(0.42, 0, 0.16),
  ]);
  const shaft = new THREE.TubeGeometry(path, 40, 0.036, 8);
  // thick near the head, a sharp point at the far end
  const p = shaft.attributes.position as THREE.BufferAttribute;
  const ring = 9;
  for (let i = 0; i < p.count; i++) {
    const t = Math.floor(i / ring) / 40;
    const c = path.getPoint(Math.min(1, t));
    const k = t < 0.86 ? 1 - t * 0.1 : Math.max(0.12, 0.91 - (t - 0.86) * 5.6);
    p.setXYZ(i, c.x + (p.getX(i) - c.x) * k, c.y + (p.getY(i) - c.y) * k, c.z + (p.getZ(i) - c.z) * k);
  }
  shaft.computeVertexNormals();
  add(g, shaft, rust);
  const head = add(g, new THREE.CylinderGeometry(0.085, 0.07, 0.03, 16), dark, -0.31, 0, 0);
  head.rotation.z = Math.PI / 2;
  const dome = add(g, new THREE.SphereGeometry(0.07, 12, 8, 0, Math.PI * 2, 0, Math.PI / 2), dark, -0.325, 0, 0);
  dome.rotation.z = Math.PI / 2;
  // scabs of rust along the shaft
  for (const t of [0.15, 0.3, 0.5]) {
    const c = path.getPoint(t);
    add(g, new THREE.SphereGeometry(0.032, 6, 5), dark, c.x, 0.012, c.z).scale.set(1.6, 0.8, 1);
  }
};

const buildTinCan = (g: THREE.Group): void => {
  const geo = new THREE.CylinderGeometry(0.2, 0.2, 0.42, 28, 18, true);
  const p = geo.attributes.position as THREE.BufferAttribute;
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i);
    const y = p.getY(i);
    const z = p.getZ(i);
    const ang = Math.atan2(z, x);
    const ridge = 1 + Math.sin(y * 48) * 0.05;
    const dent = 1 - Math.max(0, Math.sin(ang * 2 + y * 6)) * 0.22 * (0.4 + Math.abs(y));
    p.setXYZ(i, x * ridge * dent, y, z * ridge * (0.85 + 0.15 * dent));
  }
  geo.computeVertexNormals();
  const mat = new THREE.MeshStandardMaterial({ color: 0x9aa0a6, roughness: 0.5, metalness: 0.8, side: THREE.DoubleSide });
  const can = add(g, geo, mat);
  can.rotation.z = Math.PI / 2;
  can.rotation.y = 0.3;
  for (const s of [-1, 1]) {
    const cap = add(g, new THREE.CylinderGeometry(0.18, 0.18, 0.015, 20), mat, s * 0.21, 0, 0);
    cap.rotation.z = Math.PI / 2;
  }
  const band = add(
    g,
    new THREE.CylinderGeometry(0.205, 0.205, 0.14, 24, 1, true, 0.4, 3.6),
    new THREE.MeshStandardMaterial({ color: 0xb53b30, roughness: 0.7, metalness: 0.2, side: THREE.DoubleSide }),
  );
  band.rotation.z = Math.PI / 2;
};

const buildRing = (g: THREE.Group, id: string): void => {
  const gold = metal(0xf2c14e, 0.22);
  // the band stands upright, a little thicker where the stone sits
  add(g, new THREE.TorusGeometry(0.3, 0.065, 18, 48), gold);
  const shoulder = add(g, new THREE.SphereGeometry(0.1, 14, 10), gold, 0, 0.3, 0);
  shoulder.scale.set(1, 1.1, 1.4);
  if (id === 'signet') {
    add(g, new THREE.CylinderGeometry(0.17, 0.17, 0.07, 6), gold, 0, 0.36, 0);
    const crest = add(g, new THREE.OctahedronGeometry(0.07), metal(0xd8a62d, 0.18), 0, 0.42, 0);
    crest.scale.y = 0.5;
  } else {
    // a claw setting holding a cut ruby: a flat table and crown above a pointed pavilion
    const ruby = new THREE.MeshStandardMaterial({ color: 0xc8102e, roughness: 0.12, metalness: 0.15, emissive: 0x5a0010, emissiveIntensity: 0.45 });
    add(g, new THREE.CylinderGeometry(0.15, 0.1, 0.05, 8), gold, 0, 0.39, 0);
    add(g, new THREE.CylinderGeometry(0.085, 0.135, 0.075, 8), ruby, 0, 0.455, 0);
    add(g, new THREE.CylinderGeometry(0.135, 0.01, 0.12, 8), ruby, 0, 0.36, 0);
    for (let i = 0; i < 4; i++) {
      const a = (i / 4) * Math.PI * 2 + Math.PI / 4;
      const claw = add(g, new THREE.CylinderGeometry(0.014, 0.018, 0.09, 6), gold, Math.cos(a) * 0.125, 0.42, Math.sin(a) * 0.125);
      claw.rotation.set(Math.sin(a) * 0.3, 0, -Math.cos(a) * 0.3);
    }
  }
};

const buildCompass = (g: THREE.Group): void => {
  const silver = metal(0xcfd2d6, 0.25);
  add(g, new THREE.CylinderGeometry(0.34, 0.34, 0.12, 36), silver);
  add(g, torusFlat(0.34, 0.025), silver, 0, 0.06, 0);
  add(g, new THREE.CylinderGeometry(0.29, 0.29, 0.01, 36), new THREE.MeshStandardMaterial({ color: 0xf2ecd8, roughness: 0.9 }), 0, 0.062, 0);
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    const tick = add(g, new THREE.BoxGeometry(0.02, 0.006, i % 2 ? 0.04 : 0.07), new THREE.MeshStandardMaterial({ color: 0x3a3a3a }), Math.cos(a) * 0.25, 0.068, Math.sin(a) * 0.25);
    tick.rotation.y = -a + Math.PI / 2;
  }
  const red = add(g, new THREE.ConeGeometry(0.035, 0.24, 4), new THREE.MeshStandardMaterial({ color: 0xd32f2f, roughness: 0.4 }), 0.12, 0.08, 0);
  red.rotation.z = -Math.PI / 2;
  const white = add(g, new THREE.ConeGeometry(0.035, 0.24, 4), new THREE.MeshStandardMaterial({ color: 0xf5f5f5, roughness: 0.4 }), -0.12, 0.08, 0);
  white.rotation.z = Math.PI / 2;
  add(g, new THREE.SphereGeometry(0.03, 10, 8), silver, 0, 0.085, 0);
  add(g, new THREE.CylinderGeometry(0.3, 0.3, 0.02, 36), new THREE.MeshPhysicalMaterial({
    color: 0xcfeaff, transparent: true, opacity: 0.28, roughness: 0.02, transmission: 0.6, ior: 1.45, thickness: 0.05,
  }), 0, 0.11, 0);
  add(g, new THREE.CylinderGeometry(0.045, 0.045, 0.1, 12), silver, 0, 0.0, -0.4).rotation.x = Math.PI / 2;
  add(g, torusFlat(0.07, 0.016), silver, 0, 0, -0.5);
};

const buildKey = (g: THREE.Group): void => {
  const brass = metal(0xb5883a, 0.3);
  add(g, torusFlat(0.15, 0.04), brass, -0.38, 0, 0);
  add(g, torusFlat(0.07, 0.022), brass, -0.38, 0, 0);
  for (const a of [0, Math.PI / 2, Math.PI, Math.PI * 1.5]) {
    add(g, new THREE.SphereGeometry(0.035, 8, 6), brass, -0.38 + Math.cos(a) * 0.19, 0, Math.sin(a) * 0.19);
  }
  const shaft = add(g, new THREE.CylinderGeometry(0.03, 0.03, 0.75, 12), brass, 0.1, 0, 0);
  shaft.rotation.z = Math.PI / 2;
  add(g, new THREE.BoxGeometry(0.1, 0.045, 0.16), brass, 0.38, 0, 0.09);
  add(g, new THREE.BoxGeometry(0.08, 0.045, 0.11), brass, 0.25, 0, 0.075);
  add(g, new THREE.CylinderGeometry(0.045, 0.045, 0.04, 12), brass, -0.18, 0, 0).rotation.z = Math.PI / 2;
};

const buildShard = (g: THREE.Group, setId: string, idx: number): void => {
  const arc = (Math.PI * 2) / 3 + 0.12;
  const start = idx * ((Math.PI * 2) / 3);
  if (setId === 'astrolabe') {
    const brass = metal(0xc9973f, 0.3);
    const ring = torusFlat(0.5, 0.055, arc);
    const mesh = add(g, ring, brass);
    mesh.rotation.y = -start;
    const inner = add(g, torusFlat(0.36, 0.025, arc * 0.9), brass);
    inner.rotation.y = -start - 0.1;
    for (let i = 0; i < 3; i++) {
      const a = start + 0.25 + i * 0.55;
      const star = add(g, new THREE.OctahedronGeometry(0.055), metal(0xffe29a, 0.2), Math.cos(a) * 0.5, 0.055, -Math.sin(a) * 0.5);
      star.scale.y = 0.6;
    }
  } else {
    const bronze = metal(0xd2a24a, 0.28);
    const wedge = add(g, new THREE.CylinderGeometry(0.5, 0.5, 0.07, 32, 1, false, start, arc), bronze);
    (wedge.material as THREE.MeshStandardMaterial).side = THREE.DoubleSide;
    const rim = add(g, torusFlat(0.47, 0.022, arc), bronze, 0, 0.04, 0);
    rim.rotation.y = -start;
    const mid = start + arc / 2;
    add(g, new THREE.SphereGeometry(0.06, 10, 8), new THREE.MeshStandardMaterial({ color: 0xf0e4c4, roughness: 0.6 }), Math.cos(mid) * 0.28, 0.06, -Math.sin(mid) * 0.28);
  }
};

const buildBottleCap = (g: THREE.Group): void => {
  const mat = metal(0xc43d2f, 0.4, 0.6);
  add(g, new THREE.CylinderGeometry(0.2, 0.2, 0.04, 24), mat);
  for (let i = 0; i < 21; i++) {
    const a = (i / 21) * Math.PI * 2;
    add(g, new THREE.BoxGeometry(0.03, 0.06, 0.045), mat, Math.cos(a) * 0.2, 0.02, Math.sin(a) * 0.2).rotation.y = -a;
  }
  add(g, new THREE.CylinderGeometry(0.12, 0.12, 0.01, 18), metal(0xf0e8d0, 0.5, 0.2), 0, 0.025, 0);
};

/** An empty green glass bottle on its side, with a worn paper label and a chipped lip. */
const buildBottle = (g: THREE.Group): void => {
  const glass = new THREE.MeshStandardMaterial({ color: 0x3d9a56, roughness: 0.1, metalness: 0.1, transparent: true, opacity: 0.62, side: THREE.DoubleSide, depthWrite: false });
  const paper = new THREE.MeshStandardMaterial({ color: 0xe7dcc0, roughness: 0.9, side: THREE.DoubleSide });
  const pts = [
    [0, -0.28], [0.07, -0.28], [0.1, -0.265], [0.112, -0.23], [0.112, 0.06], [0.106, 0.12], [0.085, 0.17], [0.06, 0.22], [0.043, 0.27], [0.04, 0.36], [0.048, 0.375], [0.05, 0.4], [0.044, 0.405],
  ].map(([x, y]) => new THREE.Vector2(x, y));
  // the bottle's axis runs along x, neck toward +x
  const bottle = add(g, new THREE.LatheGeometry(pts, 24), glass, 0, 0.112, 0);
  bottle.rotation.z = -Math.PI / 2;
  bottle.renderOrder = 2;
  const label = add(g, new THREE.CylinderGeometry(0.118, 0.118, 0.17, 24, 1, true, 0.4, 4.4), paper, -0.05, 0.112, 0);
  label.rotation.z = Math.PI / 2;
  const stripe = add(g, new THREE.CylinderGeometry(0.119, 0.119, 0.04, 24, 1, true, 0.4, 4.4), new THREE.MeshStandardMaterial({ color: 0xa33a2a, roughness: 0.8, side: THREE.DoubleSide }), -0.05, 0.112, 0);
  stripe.rotation.z = Math.PI / 2;
  // a little dirt in the bottom of the glass
  const silt = add(g, new THREE.SphereGeometry(0.1, 10, 6), new THREE.MeshStandardMaterial({ color: 0x8a7a55, roughness: 1 }), -0.22, 0.05, 0);
  silt.scale.set(0.6, 0.45, 0.9);
};

/** A J-shaped fishing hook lying flat: an eye, a straight shank, a round bend and a barbed point that comes back toward the eye. */
const buildHook = (g: THREE.Group): void => {
  const mat = new THREE.MeshStandardMaterial({ color: 0x8a5a38, roughness: 0.85, metalness: 0.5 });
  const rust = new THREE.MeshStandardMaterial({ color: 0x6b3f22, roughness: 0.95, metalness: 0.3 });
  const R = 0.14;
  const cx = 0.1;
  const pts: THREE.Vector3[] = [];
  for (let i = 0; i <= 6; i++) pts.push(new THREE.Vector3(-0.26 + (i / 6) * (cx + 0.26), 0, -R));
  for (let i = 1; i <= 20; i++) {
    const th = -Math.PI / 2 + (i / 20) * Math.PI * 1.08;
    pts.push(new THREE.Vector3(cx + Math.cos(th) * R, 0, Math.sin(th) * R));
  }
  const path = new THREE.CatmullRomCurve3(pts, false, 'catmullrom', 0.2);
  const wire = new THREE.TubeGeometry(path, 90, 0.022, 8);
  // thinner toward the point
  const wp = wire.attributes.position as THREE.BufferAttribute;
  for (let i = 0; i < wp.count; i++) {
    const t = Math.floor(i / 9) / 90;
    const c = path.getPoint(Math.min(1, t));
    const k = t > 0.8 ? Math.max(0.3, 1 - (t - 0.8) * 3.2) : 1;
    wp.setXYZ(i, c.x + (wp.getX(i) - c.x) * k, c.y + (wp.getY(i) - c.y) * k, c.z + (wp.getZ(i) - c.z) * k);
  }
  wire.computeVertexNormals();
  add(g, wire, mat);
  // the sharp tip and a barb on the inside of the point
  const end = pts[pts.length - 1];
  const prev = pts[pts.length - 3];
  const dir = end.clone().sub(prev).normalize();
  const tip = add(g, new THREE.ConeGeometry(0.014, 0.07, 8), mat, end.x + dir.x * 0.03, 0, end.z + dir.z * 0.03);
  tip.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
  const barb = add(g, new THREE.ConeGeometry(0.014, 0.06, 6), rust, end.x - dir.x * 0.04 + dir.z * 0.025, 0, end.z - dir.z * 0.04 - dir.x * 0.025);
  barb.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), new THREE.Vector3(-dir.x * 0.5 + dir.z * 0.6, 0, -dir.z * 0.5 - dir.x * 0.6).normalize());
  // the eye at the top of the shank
  add(g, torusFlat(0.038, 0.012), mat, -0.3, 0, -R);
  add(g, new THREE.CylinderGeometry(0.026, 0.026, 0.02, 10), rust, -0.22, 0, -R).rotation.z = Math.PI / 2;
};

/** A flat band that follows a curve, like a rubber strap: `width` across, `thick` deep, always lying face up. */
const ribbon = (curve: THREE.Curve<THREE.Vector3>, width: number, thick: number, segs = 30): THREE.BufferGeometry => {
  const pos: number[] = [];
  const idx: number[] = [];
  const up = new THREE.Vector3(0, 1, 0);
  const t = new THREE.Vector3();
  const side = new THREE.Vector3();
  const nrm = new THREE.Vector3();
  for (let i = 0; i <= segs; i++) {
    const p = curve.getPoint(i / segs);
    curve.getTangent(i / segs, t);
    side.crossVectors(up, t).normalize();
    if (side.lengthSq() < 1e-4) side.set(1, 0, 0);
    nrm.crossVectors(t, side).normalize();
    for (const [a, b] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
      pos.push(p.x + side.x * a * (width / 2) + nrm.x * b * (thick / 2), p.y + side.y * a * (width / 2) + nrm.y * b * (thick / 2), p.z + side.z * a * (width / 2) + nrm.z * b * (thick / 2));
    }
  }
  for (let i = 0; i < segs; i++) {
    for (let k = 0; k < 4; k++) {
      const a = i * 4 + k;
      const b = i * 4 + ((k + 1) % 4);
      idx.push(a, a + 4, b, b, a + 4, b + 4);
    }
  }
  // close the two ends
  idx.push(0, 1, 2, 0, 2, 3, segs * 4, segs * 4 + 2, segs * 4 + 1, segs * 4, segs * 4 + 3, segs * 4 + 2);
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setIndex(idx);
  g.computeVertexNormals();
  return g;
};

/** A beach flip-flop: a layered rubber sole shaped like a left foot, with a moulded footbed, and a yellow Y strap that comes up through the toe post. */
const buildFlipFlop = (g: THREE.Group): void => {
  // the footprint: [z, outer edge, inner edge], toe at +z. A narrow heel, a slim arch, a wide ball and a broad big toe.
  const prof: [number, number, number][] = [
    [-0.45, 0.0, 0.0], [-0.42, 0.095, -0.09], [-0.34, 0.125, -0.105], [-0.2, 0.105, -0.08], [-0.06, 0.12, -0.085], [0.1, 0.16, -0.125],
    [0.24, 0.175, -0.155], [0.36, 0.15, -0.165], [0.44, 0.1, -0.13], [0.48, 0.03, -0.06], [0.49, 0.0, 0.0],
  ];
  const side = (k: 1 | 2): THREE.Vector3[] => prof.map((p) => new THREE.Vector3(p[k], 0, -p[0]));
  const loop = new THREE.CatmullRomCurve3([...side(1), ...side(2).slice(1, -1).reverse()], true, 'centripetal');
  const outline = loop.getPoints(90).map((v) => [v.x, v.z] as [number, number]);
  const scaled = (k: number, sz = k): [number, number][] => outline.map(([x, y]) => [x * k, y * sz]);
  const navy = new THREE.MeshStandardMaterial({ color: 0x164a90, roughness: 0.9 });
  const blue = new THREE.MeshStandardMaterial({ color: 0x2f86d6, roughness: 0.85 });
  const light = new THREE.MeshStandardMaterial({ color: 0x7fbcec, roughness: 0.8 });
  // outsole, midsole and the footbed, each a little smaller than the one beneath
  add(g, plate(scaled(1.04), [], 0.03, 0.008), navy).position.y = 0.0;
  add(g, plate(scaled(1.0), [], 0.045, 0.012), blue).position.y = 0.04;
  add(g, plate(scaled(0.9), [], 0.025, 0.008), light).position.y = 0.075;
  // the heel cup and the arch support, moulded into the footbed
  const cup = add(g, new THREE.SphereGeometry(0.12, 16, 10), light, 0.0, 0.088, -0.33);
  cup.scale.set(1.0, 0.3, 0.9);
  const arch = add(g, new THREE.SphereGeometry(0.1, 14, 8), light, -0.0, 0.088, -0.1);
  arch.scale.set(0.8, 0.22, 1.5);
  // grooves across the ball of the foot, and a worn patch where the heel presses
  const groove = new THREE.MeshStandardMaterial({ color: 0x2a6fb4, roughness: 0.9 });
  for (let i = 0; i < 6; i++) {
    const gr = add(g, new THREE.BoxGeometry(0.24, 0.008, 0.014), groove, 0.02, 0.095, 0.1 + i * 0.045);
    gr.rotation.y = -0.1;
  }
  // the strap: two flat bands from the sides of the sole, rising and meeting at the toe post
  const yellow = new THREE.MeshStandardMaterial({ color: 0xf3c62c, roughness: 0.7 });
  const post = new THREE.Vector3(0.0, 0.09, 0.27);
  for (const [x, sx] of [[0.15, 1], [-0.15, -1]] as const) {
    const path = new THREE.CatmullRomCurve3([
      new THREE.Vector3(x * 0.95, 0.08, 0.03),
      new THREE.Vector3(x * 0.9, 0.14, 0.075),
      new THREE.Vector3(x * 0.55, 0.17, 0.17),
      new THREE.Vector3(sx * 0.07, 0.13, post.z - 0.03),
      new THREE.Vector3(0, 0.1, post.z),
    ]);
    add(g, ribbon(path, 0.05, 0.022, 36), yellow);
    // the strap pushes through the sole with a round plug on each side
    add(g, new THREE.CylinderGeometry(0.035, 0.035, 0.02, 12), yellow, x * 0.95, 0.092, 0.03);
  }
  // the knob where the straps meet, and the toe post it is anchored by
  add(g, new THREE.SphereGeometry(0.04, 12, 8), yellow, 0, 0.1, post.z).scale.set(1.1, 0.7, 1.1);
  add(g, new THREE.CylinderGeometry(0.028, 0.034, 0.08, 10), yellow, 0, 0.06, post.z);
};

const buildBolt = (g: THREE.Group): void => {
  const mat = new THREE.MeshStandardMaterial({ color: 0x8a4b22, roughness: 0.9, metalness: 0.5 });
  const head = add(g, new THREE.CylinderGeometry(0.09, 0.09, 0.06, 6), mat, -0.18, 0, 0);
  head.rotation.z = Math.PI / 2;
  const shaft = add(g, new THREE.CylinderGeometry(0.04, 0.04, 0.34, 10), mat, 0.02, 0, 0);
  shaft.rotation.z = Math.PI / 2;
  for (let i = 0; i < 6; i++) {
    const ring = add(g, new THREE.TorusGeometry(0.042, 0.006, 5, 10), mat, -0.04 + i * 0.045, 0, 0);
    ring.rotation.y = Math.PI / 2;
  }
};

/** The plastic carrier from a six-pack: a thin sheet with six round holes, joined along the middle. */
const buildPlasticRing = (g: THREE.Group): void => {
  const mat = new THREE.MeshStandardMaterial({ color: 0xdfeef4, roughness: 0.35, transparent: true, opacity: 0.82, side: THREE.DoubleSide });
  const r = 0.095;
  const cols = [-0.21, 0, 0.21];
  const rows = [-0.1, 0.1];
  const holes = rows.flatMap((z) => cols.map((x) => ellipse(x, z, r, r, 0, Math.PI * 2, 24)));
  // the outline hugs the holes: a rounded rectangle with a notch between each pair of rings
  const outline: [number, number][] = [];
  const R = 0.13;
  const corner = (cx: number, cz: number, a0: number): void => void outline.push(...ellipse(cx, cz, R, R, a0, a0 + Math.PI / 2, 8));
  corner(0.21, 0.1, 0);
  corner(-0.21, 0.1, Math.PI / 2);
  corner(-0.21, -0.1, Math.PI);
  corner(0.21, -0.1, (3 * Math.PI) / 2);
  add(g, plate(outline, holes, 0.022, 0.006), mat);
  // the two little finger tabs at the ends
  for (const sx of [-1, 1]) add(g, plate(ellipse(sx * 0.37, 0, 0.05, 0.03, 0, Math.PI * 2, 12), [], 0.02, 0.005), mat);
};

const buildWatch = (g: THREE.Group): void => {
  const silver = metal(0xcfd2d6, 0.25);
  add(g, new THREE.CylinderGeometry(0.26, 0.26, 0.09, 36), silver);
  add(g, torusFlat(0.26, 0.02), silver, 0, 0.045, 0);
  add(g, new THREE.CylinderGeometry(0.22, 0.22, 0.01, 36), new THREE.MeshStandardMaterial({ color: 0xf4efe0, roughness: 0.8 }), 0, 0.048, 0);
  for (let i = 0; i < 12; i++) {
    const a = (i / 12) * Math.PI * 2;
    add(g, new THREE.BoxGeometry(0.015, 0.006, i % 3 === 0 ? 0.05 : 0.03), new THREE.MeshStandardMaterial({ color: 0x3a3a3a }), Math.cos(a) * 0.18, 0.055, Math.sin(a) * 0.18).rotation.y = -a + Math.PI / 2;
  }
  add(g, new THREE.BoxGeometry(0.012, 0.006, 0.14), new THREE.MeshStandardMaterial({ color: 0x222222 }), 0.0, 0.06, -0.06);
  add(g, new THREE.BoxGeometry(0.1, 0.006, 0.012), new THREE.MeshStandardMaterial({ color: 0x222222 }), 0.04, 0.06, 0.0);
  add(g, new THREE.CylinderGeometry(0.04, 0.04, 0.06, 10), silver, 0, 0, -0.31).rotation.x = Math.PI / 2;
  add(g, torusFlat(0.06, 0.014), silver, 0, 0, -0.4);
  // short chain
  for (let i = 0; i < 6; i++) add(g, torusFlat(0.03, 0.007), silver, Math.sin(i * 0.9) * 0.08, 0, -0.5 - i * 0.045).rotation.y = i * 0.6;
};

const buildPearls = (g: THREE.Group): void => {
  const pearl = new THREE.MeshPhysicalMaterial({ color: 0xf6efe6, roughness: 0.2, metalness: 0.1, clearcoat: 1, clearcoatRoughness: 0.1 });
  const n = 18;
  for (let i = 0; i < n; i++) {
    const a = (i / n) * Math.PI * 2;
    const r = 0.06 + 0.012 * Math.sin((i / n) * Math.PI);
    add(g, new THREE.SphereGeometry(r, 14, 10), pearl, Math.cos(a) * 0.32, 0, Math.sin(a) * 0.32);
  }
  add(g, new THREE.BoxGeometry(0.1, 0.05, 0.06), metal(0xf2c14e, 0.25), 0.32, 0.0, 0.0);
};

/** Sunglasses lying open on their backs: rounded dark lenses, a sturdy frame, a nose bridge and two long arms. */
const buildSunglasses = (g: THREE.Group): void => {
  const frame = new THREE.MeshStandardMaterial({ color: 0x1c1c20, roughness: 0.35, metalness: 0.2 });
  const lens = new THREE.MeshStandardMaterial({ color: 0x1b2a36, roughness: 0.08, metalness: 0.3, transparent: true, opacity: 0.88 });
  // a rounded, slightly squared lens that is wider at the top
  const lensShape = (k: number): [number, number][] => [
    ...ellipse(0, 0.015, 0.165 * k, 0.1 * k, 0, Math.PI, 16),
    ...ellipse(0.0, -0.02, 0.15 * k, 0.1 * k, Math.PI, Math.PI * 2, 16),
  ];
  for (const sx of [-1, 1]) {
    const x = sx * 0.2;
    const f = add(g, plate(lensShape(1.05), [lensShape(0.82)], 0.045, 0.008), frame, x, 0, 0);
    f.scale.set(1, 1, 1);
    add(g, plate(lensShape(0.84), [], 0.012, 0.004), lens, x, 0.01, 0);
    // a hinge block and the long arm, bent slightly outward
    add(g, new THREE.BoxGeometry(0.05, 0.045, 0.05), frame, sx * 0.355, 0, 0.0);
    const arm = add(g, new THREE.BoxGeometry(0.035, 0.03, 0.42), frame, sx * 0.375, 0, 0.23);
    arm.rotation.y = sx * -0.07;
    add(g, new THREE.SphereGeometry(0.022, 8, 6), frame, sx * 0.39, 0, 0.44);
    // a nose pad
    add(g, new THREE.SphereGeometry(0.02, 8, 6), frame, sx * 0.05, -0.02, 0.07).scale.set(0.8, 0.6, 1.4);
  }
  const bridge = add(g, new THREE.TorusGeometry(0.06, 0.02, 8, 14, Math.PI), frame, 0, 0, -0.01);
  bridge.rotation.x = Math.PI / 2;
  add(g, new THREE.BoxGeometry(0.46, 0.02, 0.03), frame, 0, 0, -0.085);
};

const buildHarmonica = (g: THREE.Group): void => {
  const silver = metal(0xcfd2d6, 0.25);
  const dark = new THREE.MeshStandardMaterial({ color: 0x25272b, roughness: 0.7 });
  add(g, new THREE.BoxGeometry(0.56, 0.11, 0.1), silver);
  add(g, new THREE.BoxGeometry(0.58, 0.02, 0.12), silver, 0, 0.065, 0);
  add(g, new THREE.BoxGeometry(0.58, 0.02, 0.12), silver, 0, -0.065, 0);
  for (let i = 0; i < 10; i++) add(g, new THREE.BoxGeometry(0.03, 0.05, 0.02), dark, -0.22 + i * 0.049, 0, 0.055);
  add(g, new THREE.BoxGeometry(0.04, 0.16, 0.12), silver, -0.3, 0, 0);
  add(g, new THREE.BoxGeometry(0.04, 0.16, 0.12), silver, 0.3, 0, 0);
};

const buildWeddingBand = (g: THREE.Group): void => {
  const gold = metal(0xf2c14e, 0.18);
  add(g, new THREE.TorusGeometry(0.3, 0.06, 18, 48), gold);
  const gem = add(
    g,
    new THREE.OctahedronGeometry(0.1),
    new THREE.MeshPhysicalMaterial({ color: 0xf6fbff, transmission: 0.95, ior: 2.4, roughness: 0.02, thickness: 0.3, metalness: 0 }),
    0, 0.38, 0,
  );
  gem.scale.y = 1.2;
  for (const [x, z] of [[0.06, 0.06], [-0.06, 0.06], [0.06, -0.06], [-0.06, -0.06]]) add(g, new THREE.CylinderGeometry(0.01, 0.01, 0.08, 6), gold, x, 0.33, z);
};

/** A wooden toy sailboat: a carved hull, a deck, a cabin, a mast with a red mainsail and white jib, and a little flag. */
const buildBoat = (g: THREE.Group): void => {
  const wood = new THREE.MeshStandardMaterial({ color: 0xa6663a, roughness: 0.75 });
  const deckMat = new THREE.MeshStandardMaterial({ color: 0xd9b27a, roughness: 0.8 });
  const white = new THREE.MeshStandardMaterial({ color: 0xf4f0e6, roughness: 0.8, side: THREE.DoubleSide });
  const red = new THREE.MeshStandardMaterial({ color: 0xd9382c, roughness: 0.8, side: THREE.DoubleSide });
  // the hull: a side profile (bow to the right), pinched toward the bow when seen from above
  const prof = new THREE.Shape();
  prof.moveTo(-0.42, 0.14);
  prof.quadraticCurveTo(-0.44, -0.02, -0.28, -0.07);
  prof.quadraticCurveTo(0.0, -0.1, 0.24, -0.06);
  prof.quadraticCurveTo(0.44, -0.02, 0.52, 0.18);
  prof.lineTo(0.5, 0.2);
  prof.quadraticCurveTo(0.1, 0.14, -0.42, 0.14);
  let hullGeo: THREE.BufferGeometry = new THREE.ExtrudeGeometry(prof, { depth: 0.26, bevelEnabled: true, bevelSize: 0.03, bevelThickness: 0.04, bevelSegments: 4, curveSegments: 20 });
  hullGeo.translate(0, 0, -0.13);
  const hp = hullGeo.attributes.position as THREE.BufferAttribute;
  for (let i = 0; i < hp.count; i++) {
    const x = hp.getX(i);
    const k = x > 0 ? 1 - 0.7 * Math.pow(Math.min(1, x / 0.55), 1.6) : 1 - 0.18 * Math.pow(Math.min(1, -x / 0.45), 2);
    hp.setZ(i, hp.getZ(i) * k);
  }
  // weld the pieces so the hull shades smoothly, like turned wood, instead of in flat facets
  hullGeo.deleteAttribute('normal');
  hullGeo.deleteAttribute('uv');
  hullGeo = mergeVertices(hullGeo, 1e-4);
  hullGeo.computeVertexNormals();
  add(g, hullGeo, wood);
  // the deck and a small cabin at the stern
  add(g, new THREE.BoxGeometry(0.62, 0.012, 0.22), deckMat, -0.04, 0.2, 0);
  add(g, new THREE.BoxGeometry(0.15, 0.07, 0.15), wood, -0.3, 0.238, 0);
  // the mast, boom and sails
  add(g, new THREE.CylinderGeometry(0.014, 0.018, 0.78, 8), wood, 0.06, 0.58, 0);
  add(g, new THREE.CylinderGeometry(0.01, 0.01, 0.34, 6), wood, -0.11, 0.38, 0).rotation.z = Math.PI / 2;
  const main = new THREE.Shape();
  main.moveTo(0, 0);
  main.lineTo(0, 0.66);
  main.quadraticCurveTo(-0.1, 0.3, -0.27, 0.0);
  main.closePath();
  add(g, new THREE.ShapeGeometry(main), red, 0.05, 0.38, 0.005);
  const jib = new THREE.Shape();
  jib.moveTo(0, 0);
  jib.lineTo(0, 0.54);
  jib.quadraticCurveTo(0.12, 0.2, 0.3, 0.0);
  jib.closePath();
  add(g, new THREE.ShapeGeometry(jib), white, 0.075, 0.38, -0.005);
  // a tiny flag at the top of the mast
  const flag = new THREE.Shape();
  flag.moveTo(0, 0);
  flag.lineTo(0.09, 0.03);
  flag.lineTo(0, 0.06);
  flag.closePath();
  add(g, new THREE.ShapeGeometry(flag), red, 0.065, 0.93, 0);
};

const buildBlackPearl = (g: THREE.Group): void => {
  const gold = metal(0xd9aa45, 0.25);
  const pearl = new THREE.MeshStandardMaterial({ color: 0x1b1f24, roughness: 0.12, metalness: 0.55, emissive: 0x06201f, emissiveIntensity: 0.6 });
  add(g, new THREE.SphereGeometry(0.26, 24, 18), pearl, 0, 0.12, 0);
  add(g, new THREE.CylinderGeometry(0.2, 0.09, 0.14, 16), gold, 0, -0.17, 0);
  for (const a of [0, 1, 2, 3]) {
    const ang = (a * Math.PI) / 2;
    const claw = add(g, new THREE.CylinderGeometry(0.012, 0.012, 0.22, 6), gold, Math.cos(ang) * 0.17, -0.02, Math.sin(ang) * 0.17);
    claw.rotation.set(Math.sin(ang) * 0.35, 0, -Math.cos(ang) * 0.35);
  }
};

/** A weathered stone idol: a seated figure with rounded, carved forms, a heavy-browed face, folded arms, a stepped headdress and moss in the damp places. */
const buildIdol = (g: THREE.Group): void => {
  const parts: THREE.BufferGeometry[] = [];
  const stone = 0x9a9488;
  const deep = 0x77716a;
  const dark = 0x1f1c19;
  const cream = 0xd9d1bd;
  // a box with rounded edges and corners
  const rbox = (w: number, h: number, d: number, r: number): THREE.BufferGeometry => {
    const geo = new THREE.BoxGeometry(w, h, d, 6, 6, 6);
    const p = geo.attributes.position as THREE.BufferAttribute;
    const v = new THREE.Vector3();
    const c = new THREE.Vector3();
    for (let i = 0; i < p.count; i++) {
      v.fromBufferAttribute(p, i);
      c.set(Math.max(-(w / 2 - r), Math.min(w / 2 - r, v.x)), Math.max(-(h / 2 - r), Math.min(h / 2 - r, v.y)), Math.max(-(d / 2 - r), Math.min(d / 2 - r, v.z)));
      v.sub(c);
      if (v.lengthSq() > 0) v.normalize().multiplyScalar(r);
      p.setXYZ(i, c.x + v.x, c.y + v.y, c.z + v.z);
    }
    return geo;
  };
  const put = (geo: THREE.BufferGeometry, color: number, x: number, y: number, z: number, sx = 1, sy = 1, sz = 1): void => {
    geo.scale(sx, sy, sz);
    geo.translate(x, y, z);
    parts.push(tint(geo, color));
  };
  // plinth and the body, a rounded tapering mass seated on it
  put(rbox(0.72, 0.12, 0.54, 0.035), deep, 0, -0.5, 0);
  const body = new THREE.LatheGeometry(
    [[0, -0.44], [0.3, -0.44], [0.32, -0.3], [0.29, -0.05], [0.25, 0.14], [0.2, 0.27], [0.11, 0.33], [0, 0.34]].map(([r, y]) => new THREE.Vector2(r, y)),
    28,
  );
  put(body, stone, 0, 0, 0, 1, 1, 0.78);
  put(rbox(0.64, 0.17, 0.4, 0.07), stone, 0, 0.22, 0);
  // arms folded across the belly, with hands
  for (const sx of [-1, 1]) {
    const arm = new THREE.TubeGeometry(
      new THREE.CatmullRomCurve3([new THREE.Vector3(sx * 0.3, 0.22, 0.04), new THREE.Vector3(sx * 0.31, 0.04, 0.16), new THREE.Vector3(sx * 0.2, -0.08, 0.27), new THREE.Vector3(sx * 0.06, -0.1, 0.3)]),
      20,
      0.055,
      8,
    );
    parts.push(tint(arm, deep));
    put(rbox(0.1, 0.07, 0.09, 0.025), stone, sx * 0.05, -0.1, 0.3);
  }
  // a row of carved diamonds across the chest
  for (let i = 0; i < 5; i++) {
    const d = rbox(0.05, 0.05, 0.03, 0.01);
    d.rotateZ(Math.PI / 4);
    put(d, deep, (i - 2) * 0.085, 0.17 - Math.abs(i - 2) * 0.025, 0.2);
  }
  // neck, and the head: a tall rounded block
  put(new THREE.CylinderGeometry(0.11, 0.13, 0.12, 14), deep, 0, 0.38, 0);
  put(rbox(0.46, 0.52, 0.42, 0.13), stone, 0, 0.68, 0);
  // brow, eyes, nose, mouth, chin and ears
  put(rbox(0.48, 0.08, 0.14, 0.035), deep, 0, 0.77, 0.19);
  for (const sx of [-1, 1]) {
    const eye = new THREE.SphereGeometry(0.05, 12, 8);
    put(eye, dark, sx * 0.115, 0.69, 0.2, 1, 0.75, 0.6);
    put(rbox(0.07, 0.05, 0.05, 0.02), stone, sx * 0.115, 0.64, 0.2);
    put(rbox(0.065, 0.22, 0.1, 0.025), stone, sx * 0.26, 0.66, 0);
    put(rbox(0.055, 0.1, 0.07, 0.022), stone, sx * 0.26, 0.52, 0);
  }
  put(rbox(0.1, 0.22, 0.12, 0.035), deep, 0, 0.62, 0.23);
  put(rbox(0.24, 0.045, 0.06, 0.016), dark, 0, 0.47, 0.205);
  for (let i = 0; i < 4; i++) put(rbox(0.032, 0.038, 0.03, 0.01), cream, (i - 1.5) * 0.05, 0.485, 0.215);
  put(rbox(0.22, 0.08, 0.1, 0.03), stone, 0, 0.41, 0.18);
  // a three-tier headdress with studs round the lowest band
  put(new THREE.CylinderGeometry(0.28, 0.3, 0.09, 18), deep, 0, 0.98, 0);
  put(new THREE.CylinderGeometry(0.21, 0.24, 0.09, 16), stone, 0, 1.07, 0);
  put(new THREE.CylinderGeometry(0.12, 0.15, 0.1, 14), deep, 0, 1.16, 0);
  put(new THREE.SphereGeometry(0.06, 10, 8), stone, 0, 1.26, 0);
  for (let i = 0; i < 10; i++) {
    const a = (i / 10) * Math.PI * 2;
    put(new THREE.SphereGeometry(0.03, 8, 6), cream, Math.cos(a) * 0.29, 0.98, Math.sin(a) * 0.29);
  }
  const merged = mergeGeometries(parts, false)!;
  // weather it: a slight lumpiness, damp moss on the low and upward-facing parts
  merged.computeVertexNormals();
  const p = merged.attributes.position as THREE.BufferAttribute;
  const n = merged.attributes.normal as THREE.BufferAttribute;
  const col = merged.attributes.color as THREE.BufferAttribute;
  const moss = new THREE.Color(0x6f8048);
  const c = new THREE.Color();
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i);
    const y = p.getY(i);
    const z = p.getZ(i);
    const lump = Math.sin(x * 23 + y * 11) * Math.sin(y * 19 + z * 17) * Math.sin(z * 21 + x * 13);
    const d = 0.007 * lump;
    p.setXYZ(i, x + n.getX(i) * d, y + n.getY(i) * d, z + n.getZ(i) * d);
    c.fromBufferAttribute(col, i);
    const patch = Math.sin(x * 9 + z * 7) * Math.sin(y * 8 + x * 5);
    const damp = Math.max(0, n.getY(i)) * 0.6 + Math.max(0, -0.1 - y) * 0.9;
    const m = Math.max(0, Math.min(1, (patch * 0.5 + damp - 0.2) * 1.4));
    c.lerp(moss, m * 0.65);
    c.multiplyScalar(0.9 + lump * 0.08);
    col.setXYZ(i, c.r, c.g, c.b);
  }
  merged.computeVertexNormals();
  const mesh = new THREE.Mesh(merged, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.95 }));
  mesh.castShadow = true;
  g.add(mesh);
};

/** A pink conch: a stepped spire at one end, a big body whorl with knobs on its shoulder, spiral ridges, a flared lip and a narrow canal. */
const buildConch = (g: THREE.Group): void => {
  // the outline of the shell from the apex (axis y = 0) to the canal (y = 1.05), radius first
  const pts: [number, number][] = [[0, 0]];
  for (let i = 0; i < 5; i++) {
    const y0 = 0.04 + i * 0.085;
    const r0 = 0.03 + i * 0.03;
    pts.push([r0, y0], [r0 + 0.04, y0 + 0.07]);
  }
  pts.push([0.25, 0.5], [0.3, 0.56], [0.31, 0.64], [0.28, 0.76], [0.2, 0.88], [0.1, 0.98], [0.055, 1.05], [0, 1.07]);
  const lathe = new THREE.LatheGeometry(pts.map(([r, y]) => new THREE.Vector2(r, y)), 36);
  const lp = lathe.attributes.position as THREE.BufferAttribute;
  const col = new Float32Array(lp.count * 3);
  const peach = new THREE.Color(0xf4bcab);
  const rose = new THREE.Color(0xe0907f);
  const pale = new THREE.Color(0xfbe3d8);
  const c = new THREE.Color();
  for (let i = 0; i < lp.count; i++) {
    const y = lp.getY(i);
    const ang = Math.atan2(lp.getZ(i), lp.getX(i));
    const band = 0.5 + 0.5 * Math.sin(y * 34 + ang * 1.5);
    c.copy(peach).lerp(rose, band * 0.7).lerp(pale, Math.max(0, 0.5 - y) * 0.3);
    col.set([c.r, c.g, c.b], i * 3);
  }
  lathe.deleteAttribute('uv');
  lathe.setAttribute('color', new THREE.BufferAttribute(col, 3));
  lathe.computeVertexNormals();
  const parts: THREE.BufferGeometry[] = [lathe];
  // spiral ridges winding round the body
  const radiusAt = (y: number): number => {
    for (let i = 1; i < pts.length; i++) {
      if (y <= pts[i][1]) {
        const [r0, y0] = pts[i - 1];
        const [r1, y1] = pts[i];
        return r0 + ((r1 - r0) * (y - y0)) / Math.max(1e-4, y1 - y0);
      }
    }
    return 0;
  };
  for (const off of [0, Math.PI]) {
    const path = new THREE.CatmullRomCurve3(
      Array.from({ length: 60 }, (_, i) => {
        const y = 0.12 + (i / 59) * 0.86;
        const a = off + y * 15;
        const r = radiusAt(y) + 0.008;
        return new THREE.Vector3(Math.cos(a) * r, y, Math.sin(a) * r);
      }),
    );
    const ridge = tint(new THREE.TubeGeometry(path, 90, 0.012, 6), 0xd98978);
    parts.push(ridge);
  }
  // knobs round the shoulder
  for (let i = 0; i < 9; i++) {
    const a = (i / 9) * Math.PI * 2;
    const k = new THREE.ConeGeometry(0.035, 0.1, 6);
    k.rotateZ(-Math.PI / 2);
    k.rotateY(-a);
    k.translate(Math.cos(a) * 0.3, 0.57, Math.sin(a) * 0.3);
    parts.push(tint(k, 0xe9a595));
  }
  const body = new THREE.Mesh(mergeGeometries(parts, false)!, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.45 }));
  body.castShadow = true;
  // lay it on its side: the apex to the left, the canal to the right
  const shell = new THREE.Group();
  shell.add(body);
  // the flared lip along one side of the body whorl, glossy pink inside
  const lipMat = new THREE.MeshStandardMaterial({ color: 0xf59a86, roughness: 0.25, side: THREE.DoubleSide });
  const lip = new THREE.Mesh(new THREE.SphereGeometry(0.3, 24, 14, 0, Math.PI, 0, Math.PI), lipMat);
  lip.scale.set(0.55, 1.15, 1);
  lip.position.set(0.02, 0.72, 0.0);
  lip.rotation.y = Math.PI / 2;
  shell.add(lip);
  const inner = new THREE.Mesh(new THREE.CircleGeometry(0.27, 24), new THREE.MeshStandardMaterial({ color: 0xffc9b8, roughness: 0.2, side: THREE.DoubleSide }));
  inner.scale.set(0.55, 1.1, 1);
  inner.position.set(0.15, 0.72, 0.0);
  inner.rotation.y = Math.PI / 2;
  shell.add(inner);
  shell.rotation.z = -Math.PI / 2;
  shell.scale.setScalar(0.75);
  shell.position.set(-0.4, 0.2, 0);
  g.add(shell);
};

/** A wooden treasure chest: a planked box and a domed lid, bound in iron straps, with corner plates and a gold lock. */
const buildCache = (g: THREE.Group): void => {
  const wood = new THREE.MeshStandardMaterial({ color: 0x7a4a24, roughness: 0.85 });
  const dark = new THREE.MeshStandardMaterial({ color: 0x4a2c14, roughness: 0.9 });
  const band = new THREE.MeshStandardMaterial({ color: 0x2c2f34, roughness: 0.45, metalness: 0.7 });
  const gold = metal(0xf2c14e, 0.25);
  const W = 0.72;
  const D = 0.46;
  const BH = 0.3;
  // the box, with plank lines
  add(g, new THREE.BoxGeometry(W, BH, D), wood, 0, 0, 0);
  for (const y of [-0.05, 0.05]) {
    add(g, new THREE.BoxGeometry(W + 0.004, 0.008, D + 0.004), dark, 0, y, 0);
  }
  // the lid: half a cylinder lying along x, with flat half-disc ends so it is closed
  const lid = add(g, new THREE.CylinderGeometry(D / 2, D / 2, W, 24, 1, false, 0, Math.PI), wood, 0, BH / 2, 0);
  lid.rotation.z = Math.PI / 2;
  for (const sx of [-1, 1]) {
    const cap = add(g, new THREE.CircleGeometry(D / 2, 24, 0, Math.PI), wood, sx * (W / 2 + 0.001), BH / 2, 0);
    cap.rotation.y = sx * (Math.PI / 2);
  }
  // iron straps over the lid and down the front, back and sides of the box
  for (const x of [-0.24, 0.24]) {
    const arch = add(g, new THREE.CylinderGeometry(D / 2 + 0.012, D / 2 + 0.012, 0.06, 24, 1, false, 0, Math.PI), band, x, BH / 2, 0);
    arch.rotation.z = Math.PI / 2;
    for (const sz of [-1, 1]) add(g, new THREE.BoxGeometry(0.06, BH + 0.01, 0.014), band, x, 0, sz * (D / 2 + 0.003));
    add(g, new THREE.BoxGeometry(0.06, 0.014, D + 0.01), band, x, -BH / 2 + 0.002, 0);
  }
  // corner plates and studs
  for (const sx of [-1, 1]) for (const sz of [-1, 1]) {
    add(g, new THREE.BoxGeometry(0.05, 0.12, 0.05), band, sx * (W / 2 - 0.005), -0.09, sz * (D / 2 - 0.005));
    add(g, new THREE.SphereGeometry(0.014, 6, 5), gold, sx * (W / 2 + 0.002), 0.05, sz * (D / 2 - 0.05));
  }
  // the lock plate and hasp on the front
  add(g, new THREE.BoxGeometry(0.13, 0.15, 0.025), gold, 0, 0.1, D / 2 + 0.014);
  add(g, new THREE.BoxGeometry(0.09, 0.09, 0.03), band, 0, 0.1, D / 2 + 0.03);
  add(g, new THREE.CylinderGeometry(0.014, 0.014, 0.04, 8), gold, 0, 0.1, D / 2 + 0.05).rotation.x = Math.PI / 2;
};

/** A glass bottle lying on its side, sealed with a cork and wax, with a rolled map inside. */
const buildMessageBottle = (g: THREE.Group): void => {
  const glass = new THREE.MeshStandardMaterial({ color: 0x7fc29a, roughness: 0.1, metalness: 0.05, transparent: true, opacity: 0.45, side: THREE.DoubleSide, depthWrite: false });
  const paper = new THREE.MeshStandardMaterial({ color: 0xe9d9a8, roughness: 0.9 });
  const ink = new THREE.MeshStandardMaterial({ color: 0xb89a5c, roughness: 0.9 });
  const cork = new THREE.MeshStandardMaterial({ color: 0xb98b57, roughness: 0.9 });
  const wax = new THREE.MeshStandardMaterial({ color: 0xa02a2a, roughness: 0.5 });
  const pts = [
    [0, -0.3], [0.07, -0.3], [0.115, -0.28], [0.13, -0.24], [0.13, 0.1], [0.125, 0.16], [0.105, 0.22], [0.075, 0.27], [0.052, 0.31], [0.046, 0.4], [0.054, 0.41], [0.056, 0.435], [0.05, 0.44],
  ].map(([x, y]) => new THREE.Vector2(x, y));
  // the bottle's axis runs along x, neck toward -x
  const bottle = add(g, new THREE.LatheGeometry(pts, 24), glass, 0, 0.14, 0);
  bottle.rotation.z = Math.PI / 2;
  bottle.renderOrder = 2;
  // the rolled map lies in the body, with a few turns showing along it
  const scroll = add(g, new THREE.CylinderGeometry(0.05, 0.05, 0.36, 14), paper, 0.04, 0.14, 0);
  scroll.rotation.z = Math.PI / 2;
  for (const x of [-0.1, -0.02, 0.06, 0.14, 0.22]) add(g, new THREE.TorusGeometry(0.052, 0.005, 5, 14), ink, x, 0.14, 0).rotation.y = Math.PI / 2;
  const plug = add(g, new THREE.CylinderGeometry(0.047, 0.04, 0.1, 12), cork, -0.425, 0.14, 0);
  plug.rotation.z = Math.PI / 2;
  const seal = add(g, new THREE.SphereGeometry(0.052, 10, 8), wax, -0.485, 0.14, 0);
  seal.scale.set(0.45, 1, 1);
  const ribbon = add(g, new THREE.TorusGeometry(0.052, 0.01, 6, 14), new THREE.MeshStandardMaterial({ color: 0xc0392b, roughness: 0.7 }), -0.33, 0.14, 0);
  ribbon.rotation.y = Math.PI / 2;
};

/** A trapdoor set into the sand: a frame, weathered planks with iron straps and a ring. Open, the door stands up and the strongbox sits below. */
const buildHatch = (g: THREE.Group, open: boolean): void => {
  const frameMat = new THREE.MeshStandardMaterial({ color: 0x4a3322, roughness: 0.95 });
  const plankMats = [0x6b4a30, 0x5e4129, 0x735234, 0x57402a].map((c) => new THREE.MeshStandardMaterial({ color: c, roughness: 0.95 }));
  const iron = new THREE.MeshStandardMaterial({ color: 0x2c2f34, roughness: 0.5, metalness: 0.7 });
  const S = 1.0;
  for (const [x, z, w, d] of [[0, -S / 2, S + 0.16, 0.1], [0, S / 2, S + 0.16, 0.1], [-S / 2, 0, 0.1, S], [S / 2, 0, 0.1, S]] as const) {
    add(g, new THREE.BoxGeometry(w, 0.07, d), frameMat, x, 0.02, z);
  }
  const door = new THREE.Group();
  const plankW = (S - 0.04) / 5;
  for (let i = 0; i < 5; i++) {
    const p = add(door, new THREE.BoxGeometry(plankW - 0.01, 0.045, S - 0.08), plankMats[i % plankMats.length], -S / 2 + 0.04 + plankW * (i + 0.5), 0, (S - 0.08) / 2);
    p.rotation.y = (i % 2 ? 1 : -1) * 0.004;
  }
  for (const z of [0.2, 0.78]) add(door, new THREE.BoxGeometry(S - 0.06, 0.02, 0.08), iron, 0, 0.032, z);
  const ring = add(door, new THREE.TorusGeometry(0.07, 0.014, 6, 16), iron, 0, 0.07, 0.5);
  ring.rotation.x = open ? 0 : -0.5;
  for (const x of [-0.3, 0.3]) add(door, new THREE.CylinderGeometry(0.025, 0.025, 0.09, 8), iron, x, 0.03, 0.02).rotation.z = Math.PI / 2;
  if (!open) {
    door.position.set(0, 0.06, -S / 2 + 0.04);
    g.add(door);
    return;
  }
  // the door hinges up at the back, and a dark shaft opens up with the top of a ladder at the front
  const pit = add(g, new THREE.BoxGeometry(S - 0.06, 0.02, S - 0.06), new THREE.MeshStandardMaterial({ color: 0x0a0705, roughness: 1 }), 0, 0.0, 0);
  pit.receiveShadow = true;
  door.position.set(0, 0.07, -S / 2 + 0.02);
  door.rotation.x = -1.75;
  g.add(door);
  const rope = new THREE.MeshStandardMaterial({ color: 0x6b4a30, roughness: 0.95 });
  for (const x of [-0.25, 0.25]) {
    const rail = add(g, new THREE.CylinderGeometry(0.025, 0.025, 0.5, 8), rope, x, 0.16, 0.36);
    rail.rotation.x = 0.0;
  }
  for (const y of [0.0, 0.14, 0.28]) add(g, new THREE.CylinderGeometry(0.018, 0.018, 0.5, 6), rope, 0, y + 0.04, 0.36).rotation.z = Math.PI / 2;
};

/** A real five-armed starfish: a rounded star that is thick in the middle and tapers to the tips, with a ridge of bumps along each arm. */
const buildStarfish = (g: THREE.Group): void => {
  const arms = 5;
  const radius = (th: number): number => 0.1 + 0.3 * Math.pow(Math.abs(Math.cos((arms / 2) * th)), 3.2);
  const topH = (f: number, armness: number): number => (0.045 + 0.05 * (1 - f * f)) * (0.55 + 0.45 * (1 - f)) * (0.6 + 0.4 * armness + 0.3 * (1 - f)) * Math.pow(1 - f * f * f, 0.6) * 1.25;
  const rings = 10;
  const around = 100;
  const top = new THREE.Color(0xef7f2e);
  const ridge = new THREE.Color(0xf6b25a);
  const belly = new THREE.Color(0xf3d6a6);
  const pos: number[] = [];
  const col: number[] = [];
  const idx: number[] = [];
  const c = new THREE.Color();
  // two surfaces share the rim: the domed back (s = 1) and the flatter underside (s = -1)
  for (const s of [1, -1]) {
    const base = pos.length / 3;
    for (let k = 0; k <= rings; k++) {
      const f = k / rings;
      for (let j = 0; j < around; j++) {
        const th = (j / around) * Math.PI * 2;
        const r = radius(th) * f;
        const armness = Math.pow(Math.abs(Math.cos((arms / 2) * th)), 3.2);
        const h = s === 1 ? topH(f, armness) : -topH(f, armness) * 0.36;
        pos.push(Math.cos(th) * r, h + 0.05, Math.sin(th) * r);
        if (s === 1) c.copy(top).lerp(ridge, Math.pow(armness, 3) * (1 - f * 0.5) * 0.9);
        else c.copy(belly);
        col.push(c.r, c.g, c.b);
      }
    }
    for (let k = 0; k < rings; k++) {
      for (let j = 0; j < around; j++) {
        const a = base + k * around + j;
        const b = base + k * around + ((j + 1) % around);
        const cc = a + around;
        const d = b + around;
        if (s === 1) idx.push(a, b, cc, b, d, cc);
        else idx.push(a, cc, b, b, cc, d);
      }
    }
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  geo.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  geo.setIndex(idx);
  geo.computeVertexNormals();
  g.add(new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.75 })));
  // the little bumps along each arm, and the sieve plate in the middle
  const bump = new THREE.MeshStandardMaterial({ color: 0xfbe0b0, roughness: 0.6 });
  for (let i = 0; i < arms; i++) {
    const th = (i / arms) * Math.PI * 2;
    for (let k = 0; k < 6; k++) {
      const r = 0.1 + k * 0.045;
      const sc = 1 - k * 0.1;
      const m = add(g, new THREE.SphereGeometry(0.014 * sc + 0.004, 8, 6), bump, Math.cos(th) * r, 0.05 + topH(r / 0.4, 1) + 0.004, Math.sin(th) * r);
      m.scale.y = 0.8;
    }
  }
  add(g, new THREE.CircleGeometry(0.03, 10), new THREE.MeshStandardMaterial({ color: 0xffe6b8, roughness: 0.5 }), 0, 0.05 + topH(0.02, 0.2) + 0.004, 0).rotation.x = -Math.PI / 2;
};

const buildShipBell = (g: THREE.Group): void => {
  const brass = metal(0xc79a3a, 0.3);
  brass.side = THREE.DoubleSide;
  // the wall of the bell has a thickness: out along the outside, round the lip, and back up the inside
  const pts = [
    [0.0, 0.36], [0.07, 0.35], [0.1, 0.3], [0.14, 0.16], [0.2, 0.02], [0.27, -0.1], [0.31, -0.17], [0.325, -0.2], [0.29, -0.2], [0.27, -0.16],
    [0.2, -0.07], [0.14, 0.04], [0.09, 0.2], [0.0, 0.3],
  ].map(([x, y]) => new THREE.Vector2(x, y));
  add(g, new THREE.LatheGeometry(pts, 32), brass, 0, 0, 0);
  // a raised band around the waist and the lip
  add(g, new THREE.TorusGeometry(0.2, 0.012, 6, 32), brass, 0, 0.0, 0).rotation.x = Math.PI / 2;
  add(g, new THREE.TorusGeometry(0.31, 0.014, 6, 32), brass, 0, -0.17, 0).rotation.x = Math.PI / 2;
  const hang = add(g, new THREE.TorusGeometry(0.06, 0.016, 8, 14), brass, 0, 0.4, 0);
  hang.rotation.y = Math.PI / 2;
  add(g, new THREE.SphereGeometry(0.055, 12, 10), metal(0x5d4720, 0.5), 0, -0.12, 0);
};

const buildPlate = (g: THREE.Group): void => {
  const white = new THREE.MeshStandardMaterial({ color: 0xf5f3ee, roughness: 0.25 });
  const blue = new THREE.MeshStandardMaterial({ color: 0x2f5fa8, roughness: 0.3 });
  add(g, new THREE.CylinderGeometry(0.36, 0.26, 0.05, 28), white, 0, 0, 0);
  add(g, new THREE.TorusGeometry(0.33, 0.012, 6, 28), blue, 0, 0.028, 0).rotation.x = Math.PI / 2;
  add(g, new THREE.CylinderGeometry(0.17, 0.17, 0.012, 24), blue, 0, 0.028, 0);
  for (let i = 0; i < 6; i++) {
    const a = (i / 6) * Math.PI * 2;
    add(g, new THREE.SphereGeometry(0.036, 8, 6), blue, Math.cos(a) * 0.23, 0.03, Math.sin(a) * 0.23);
  }
};

/** One valve of an oyster: a ridged, flattened dome that is a little lopsided, like a real shell. */
const oysterValve = (mat: THREE.Material, flip: boolean): THREE.Mesh => {
  const geo = new THREE.SphereGeometry(0.3, 28, 14, 0, Math.PI * 2, 0, Math.PI / 2);
  const p = geo.attributes.position as THREE.BufferAttribute;
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i);
    const y = p.getY(i);
    const z = p.getZ(i);
    const ang = Math.atan2(z, x);
    const ridge = 1 + 0.05 * Math.cos(16 * ang) * (1 - y / 0.3) + 0.04 * Math.sin(5 * ang);
    p.setXYZ(i, x * ridge * 1.05, y * (flip ? -0.42 : 0.42), z * ridge * 0.86);
  }
  geo.computeVertexNormals();
  const m = new THREE.Mesh(geo, mat);
  m.castShadow = true;
  return m;
};

const buildOyster = (g: THREE.Group): void => {
  const shell = new THREE.MeshStandardMaterial({ color: 0x8c8574, roughness: 0.95, side: THREE.DoubleSide });
  const inner = new THREE.MeshStandardMaterial({ color: 0xeae0f2, roughness: 0.2, metalness: 0.25, side: THREE.DoubleSide });
  const pearl = new THREE.MeshStandardMaterial({ color: 0xfaf6ee, roughness: 0.1, metalness: 0.3 });
  const low = oysterValve(shell, true);
  low.position.y = 0.1;
  const lowIn = oysterValve(inner, true);
  lowIn.scale.setScalar(0.93);
  lowIn.position.y = 0.12;
  // the other valve hangs open on its hinge at the back
  const hinge = new THREE.Group();
  hinge.position.set(0, 0.11, -0.24);
  hinge.rotation.x = -0.75;
  const lid = oysterValve(shell, false);
  lid.position.set(0, 0, 0.24);
  const lidIn = oysterValve(inner, false);
  lidIn.scale.setScalar(0.93);
  lidIn.position.set(0, -0.015, 0.24);
  hinge.add(lid, lidIn);
  g.add(low, lowIn, hinge);
  const p = add(g, new THREE.SphereGeometry(0.07, 16, 12), pearl, 0, 0.17, 0.02);
  p.scale.y = 0.95;
};

const buildGoldBar = (g: THREE.Group): void => {
  const gold = metal(0xf5c542, 0.2);
  const stamp = metal(0xc99a2a, 0.45);
  // an ingot is a trapezoid prism, wider at the bottom, with softly rounded edges
  const sh = new THREE.Shape();
  sh.moveTo(-0.12, 0);
  sh.lineTo(0.12, 0);
  sh.lineTo(0.082, 0.085);
  sh.lineTo(-0.082, 0.085);
  sh.closePath();
  const geo = new THREE.ExtrudeGeometry(sh, { depth: 0.46, bevelEnabled: true, bevelSize: 0.014, bevelThickness: 0.014, bevelSegments: 2 });
  geo.translate(0, 0, -0.23);
  const bar = (x: number, y: number, z: number, r = 0): void => {
    const b = add(g, geo, gold, x, y, z);
    b.rotation.y = r;
    add(g, new THREE.BoxGeometry(0.08, 0.006, 0.12), stamp, x, y + 0.092, z).rotation.y = r;
  };
  bar(-0.14, 0, 0.0, 0.04);
  bar(0.14, 0, 0.02, -0.03);
  bar(0, 0.1, 0.0, 0.05);
};

/** Builds the clean, shiny procedural model for an item id. */
export const buildItem = (defId: string): THREE.Group => {
  const def = DEF_BY_ID.get(defId);
  const g = new THREE.Group();
  // the captain's hatch is a scene piece rather than a find, so it has no definition and sits on the ground as it was built
  const piece = defId === 'captain_hatch' || defId === 'captain_hatch_open';
  if (!def && !piece) return g;
  switch (defId) {
    case 'pulltab': buildPullTab(g); break;
    case 'nail': buildNail(g); break;
    case 'tincan': buildTinCan(g); break;
    case 'treasure_cache': buildCache(g); break;
    case 'captain_hatch': buildHatch(g, false); break;
    case 'captain_hatch_open': buildHatch(g, true); break;
    case 'message_bottle': buildMessageBottle(g); break;
    case 'starfish': buildStarfish(g); break;
    case 'ship_bell': buildShipBell(g); break;
    case 'porcelain_plate': buildPlate(g); break;
    case 'sea_oyster': buildOyster(g); break;
    case 'gold_bar': buildGoldBar(g); break;
    case 'black_pearl': buildBlackPearl(g); break;
    case 'stone_idol': buildIdol(g); break;
    case 'conch': buildConch(g); break;
    case 'piece_eight':
    case 'penny': case 'quarter': case 'doubloon': case 'dime': case 'nickel':
    case 'silver_dollar': case 'real': case 'denarius': case 'sovereign':
      buildCoin(g, defId);
      break;
    case 'bottle_cap': buildBottleCap(g); break;
    case 'glass_bottle': buildBottle(g); break;
    case 'fishing_hook': buildHook(g); break;
    case 'flip_flop': buildFlipFlop(g); break;
    case 'rusty_bolt': buildBolt(g); break;
    case 'plastic_ring': buildPlasticRing(g); break;
    case 'pocket_watch': buildWatch(g); break;
    case 'pearl_necklace': buildPearls(g); break;
    case 'sunglasses': buildSunglasses(g); break;
    case 'harmonica': buildHarmonica(g); break;
    case 'wedding_band': buildWeddingBand(g); break;
    case 'toy_boat': buildBoat(g); break;
    case 'signet': case 'ruby_band': buildRing(g, defId); break;
    case 'compass': buildCompass(g); break;
    case 'brass_key': buildKey(g); break;
    default:
      if (def && def.shardSet !== undefined && def.shardIndex !== undefined) buildShard(g, def.shardSet, def.shardIndex);
  }
  if (piece) {
    const keep = new THREE.Group();
    keep.add(g);
    return keep;
  }
  // centre on its bounding box so the dirt crust encloses it evenly
  const box = new THREE.Box3().setFromObject(g);
  const c = box.getCenter(new THREE.Vector3());
  const root = new THREE.Group();
  g.position.sub(c);
  root.add(g);
  return root;
};

const SAND = new THREE.MeshStandardMaterial({ color: 0x9c825b, roughness: 0.95, metalness: 0 });

/** Sand-caked version of an item, shown lying in the dug hole. */
export const buildDirty = (defId: string): THREE.Group => {
  const g = buildItem(defId);
  g.traverse((o) => {
    if (o instanceof THREE.Mesh) {
      o.material = SAND;
      o.castShadow = true;
      o.receiveShadow = true;
    }
  });
  return g;
};

export interface CrustRig {
  root: THREE.Group;
  crust: THREE.Mesh;
  canvas: HTMLCanvasElement;
  texture: THREE.CanvasTexture;
  size: number;
}

export const newCrustCanvas = (): HTMLCanvasElement => {
  const c = document.createElement('canvas');
  c.width = c.height = 256;
  const g = c.getContext('2d')!;
  g.fillStyle = '#fff';
  g.fillRect(0, 0, 256, 256);
  return c;
};

/**
 * Wraps a clean item in one unified ellipsoid dirt crust. The crust's UVs are the
 * regular sphere mapping, so brushing never depends on the item's own (messy) UVs.
 */
export const wrapWithCrust = (item: THREE.Group, canvas: HTMLCanvasElement, fitSize = 2.0): CrustRig => {
  const root = new THREE.Group();
  const box = new THREE.Box3().setFromObject(item);
  const size = box.getSize(new THREE.Vector3());
  const maxDim = Math.max(size.x, size.y, size.z, 0.01);
  const k = fitSize / maxDim;
  const inner = new THREE.Group();
  inner.add(item);
  inner.scale.setScalar(k);
  root.add(inner);

  const half = size.clone().multiplyScalar(0.5 * k);
  const maxHalf = Math.max(half.x, half.y, half.z);
  const rx = Math.max(half.x * 1.5, maxHalf * 0.2);
  const ry = Math.max(half.y * 1.5, maxHalf * 0.2);
  const rz = Math.max(half.z * 1.5, maxHalf * 0.2);

  const texture = new THREE.CanvasTexture(canvas);
  const crust = new THREE.Mesh(
    new THREE.SphereGeometry(1, 64, 64),
    new THREE.MeshStandardMaterial({
      color: 0x9c825b, roughness: 0.95, transparent: true, alphaMap: texture, side: THREE.DoubleSide, depthWrite: false,
    }),
  );
  crust.scale.set(rx, ry, rz);
  crust.castShadow = true;
  root.add(crust);
  return { root, crust, canvas, texture, size: fitSize };
};
