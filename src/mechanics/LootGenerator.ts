import * as THREE from 'three';
import { DEF_BY_ID } from '../core/items';

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

const buildPullTab = (g: THREE.Group): void => {
  const mat = metal(0xcfd3d6, 0.32);
  const a = add(g, torusFlat(0.18, 0.045), mat);
  a.scale.set(1.2, 1, 1);
  add(g, torusFlat(0.09, 0.035), mat, 0.3, 0, 0);
  add(g, new THREE.BoxGeometry(0.26, 0.05, 0.1), mat, -0.28, 0, 0);
  add(g, new THREE.CylinderGeometry(0.03, 0.03, 0.07, 10), mat, -0.4, 0, 0);
};

const buildNail = (g: THREE.Group): void => {
  const mat = new THREE.MeshStandardMaterial({ color: 0x8a4b22, roughness: 0.88, metalness: 0.4 });
  const shaft = add(g, new THREE.CylinderGeometry(0.025, 0.018, 0.55, 8), mat, 0.1, 0, 0);
  shaft.rotation.z = Math.PI / 2;
  const bent = add(g, new THREE.CylinderGeometry(0.018, 0.012, 0.22, 8), mat, 0.4, 0, 0.06);
  bent.rotation.set(Math.PI / 2 - 0.5, 0, Math.PI / 2);
  const head = add(g, new THREE.CylinderGeometry(0.075, 0.075, 0.025, 14), mat, -0.18, 0, 0);
  head.rotation.z = Math.PI / 2;
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
  add(g, new THREE.TorusGeometry(0.3, 0.07, 18, 48), gold);
  if (id === 'signet') {
    add(g, new THREE.CylinderGeometry(0.17, 0.17, 0.07, 6), gold, 0, 0.36, 0);
    const crest = add(g, new THREE.OctahedronGeometry(0.07), metal(0xd8a62d, 0.18), 0, 0.42, 0);
    crest.scale.y = 0.5;
  } else {
    const gem = add(
      g,
      new THREE.OctahedronGeometry(0.15),
      new THREE.MeshPhysicalMaterial({
        color: 0xd0102a, transmission: 0.9, ior: 1.5, thickness: 0.4, roughness: 0.04,
        attenuationColor: new THREE.Color(0xff2040), attenuationDistance: 0.6, metalness: 0,
      }),
      0, 0.42, 0,
    );
    gem.scale.y = 1.25;
    for (const [x, z] of [[0.1, 0.1], [-0.1, 0.1], [0.1, -0.1], [-0.1, -0.1]]) {
      add(g, new THREE.CylinderGeometry(0.014, 0.014, 0.12, 6), gold, x, 0.34, z);
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

const buildBottle = (g: THREE.Group): void => {
  const glass = new THREE.MeshPhysicalMaterial({
    color: 0x3f8f52, transmission: 0.75, ior: 1.5, roughness: 0.08, thickness: 0.2, transparent: true, opacity: 0.9,
  });
  const body = add(g, new THREE.CylinderGeometry(0.11, 0.11, 0.38, 20), glass, -0.12, 0, 0);
  body.rotation.z = Math.PI / 2;
  const shoulder = add(g, new THREE.CylinderGeometry(0.045, 0.11, 0.1, 20), glass, 0.12, 0, 0);
  shoulder.rotation.z = Math.PI / 2;
  const neck = add(g, new THREE.CylinderGeometry(0.04, 0.04, 0.14, 14), glass, 0.24, 0, 0);
  neck.rotation.z = Math.PI / 2;
  add(g, new THREE.TorusGeometry(0.045, 0.012, 8, 14), glass, 0.31, 0, 0).rotation.y = Math.PI / 2;
};

/** A J-shaped fishing hook lying flat: an eye, a straight shank, a round bend and a barbed point that comes back toward the eye. */
const buildHook = (g: THREE.Group): void => {
  const mat = new THREE.MeshStandardMaterial({ color: 0x8a5a38, roughness: 0.85, metalness: 0.5 });
  const rust = new THREE.MeshStandardMaterial({ color: 0x6b3f22, roughness: 0.95, metalness: 0.3 });
  const R = 0.11;
  const cx = 0.1;
  const pts: THREE.Vector3[] = [];
  for (let i = 0; i <= 6; i++) pts.push(new THREE.Vector3(-0.22 + (i / 6) * (cx + 0.22), 0, -R));
  for (let i = 1; i <= 18; i++) {
    const th = -Math.PI / 2 + (i / 18) * Math.PI;
    pts.push(new THREE.Vector3(cx + Math.cos(th) * R, 0, Math.sin(th) * R));
  }
  for (let i = 1; i <= 5; i++) pts.push(new THREE.Vector3(cx - (i / 5) * 0.13, 0, R));
  const path = new THREE.CatmullRomCurve3(pts, false, 'catmullrom', 0.2);
  add(g, new THREE.TubeGeometry(path, 80, 0.013, 8), mat, 0, 0, 0);
  // the sharp point, and a barb just behind it
  const end = pts[pts.length - 1];
  const tip = add(g, new THREE.ConeGeometry(0.013, 0.05, 8), mat, end.x - 0.02, 0, end.z);
  tip.rotation.z = Math.PI / 2;
  const barb = add(g, new THREE.ConeGeometry(0.011, 0.045, 6), rust, end.x + 0.045, 0, end.z - 0.018);
  barb.rotation.z = Math.PI / 2 + 0.7;
  // the eye at the top of the shank
  add(g, torusFlat(0.03, 0.009), mat, -0.255, 0, -R);
  // a flattened end where the line is tied
  add(g, new THREE.CylinderGeometry(0.02, 0.02, 0.012, 10), rust, -0.2, 0, -R);
};

const buildFlipFlop = (g: THREE.Group): void => {
  const sole = new THREE.Shape();
  sole.moveTo(0, -0.42);
  sole.bezierCurveTo(0.2, -0.42, 0.22, -0.1, 0.17, 0.12);
  sole.bezierCurveTo(0.15, 0.3, 0.1, 0.42, 0, 0.42);
  sole.bezierCurveTo(-0.1, 0.42, -0.15, 0.3, -0.17, 0.12);
  sole.bezierCurveTo(-0.22, -0.1, -0.2, -0.42, 0, -0.42);
  const geo = new THREE.ExtrudeGeometry(sole, { depth: 0.05, bevelEnabled: true, bevelSize: 0.01, bevelThickness: 0.008, bevelSegments: 1 });
  geo.rotateX(-Math.PI / 2);
  add(g, geo, new THREE.MeshStandardMaterial({ color: 0x2f7fd0, roughness: 0.9 }));
  const strapMat = new THREE.MeshStandardMaterial({ color: 0xf1c232, roughness: 0.8 });
  for (const sx of [-0.12, 0.12]) {
    const arm = add(g, new THREE.CylinderGeometry(0.018, 0.018, 0.3, 8), strapMat, sx * 0.55, 0.12, 0.05);
    arm.rotation.set(Math.PI / 2 - 0.2, 0, sx > 0 ? -0.35 : 0.35);
  }
  add(g, new THREE.SphereGeometry(0.03, 8, 6), strapMat, 0, 0.09, 0.2);
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

const buildPlasticRing = (g: THREE.Group): void => {
  const mat = new THREE.MeshStandardMaterial({ color: 0xe8f1f5, roughness: 0.4, transparent: true, opacity: 0.8 });
  for (let r = 0; r < 2; r++) for (let c = 0; c < 3; c++) add(g, torusFlat(0.1, 0.014), mat, (c - 1) * 0.21, 0, (r - 0.5) * 0.21);
  add(g, new THREE.BoxGeometry(0.64, 0.012, 0.03), mat, 0, 0, 0);
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

const buildSunglasses = (g: THREE.Group): void => {
  const frame = new THREE.MeshStandardMaterial({ color: 0x1f1f23, roughness: 0.4 });
  const lens = new THREE.MeshPhysicalMaterial({ color: 0x14202a, roughness: 0.05, transmission: 0.35, transparent: true, opacity: 0.85 });
  for (const sx of [-1, 1]) {
    add(g, torusFlat(0.17, 0.025), frame, sx * 0.2, 0, 0);
    add(g, new THREE.CylinderGeometry(0.17, 0.17, 0.01, 24), lens, sx * 0.2, 0, 0);
    const arm = add(g, new THREE.BoxGeometry(0.03, 0.02, 0.34), frame, sx * 0.4, 0, 0.17);
    arm.rotation.y = sx * 0.12;
  }
  add(g, new THREE.TorusGeometry(0.05, 0.015, 6, 10, Math.PI), frame, 0, 0.0, 0).rotation.x = Math.PI / 2;
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

const buildBoat = (g: THREE.Group): void => {
  const wood = new THREE.MeshStandardMaterial({ color: 0x9a5f34, roughness: 0.75 });
  const hull = add(g, new THREE.SphereGeometry(0.32, 20, 10, 0, Math.PI * 2, Math.PI / 2, Math.PI / 2), wood);
  hull.scale.set(1.5, 0.7, 0.7);
  hull.rotation.x = Math.PI;
  add(g, new THREE.CylinderGeometry(0.012, 0.012, 0.6, 8), wood, 0, 0.3, 0);
  const sailShape = new THREE.Shape();
  sailShape.moveTo(0, 0);
  sailShape.lineTo(0, 0.5);
  sailShape.lineTo(0.3, 0.05);
  sailShape.closePath();
  const sail = add(g, new THREE.ExtrudeGeometry(sailShape, { depth: 0.01, bevelEnabled: false }), new THREE.MeshStandardMaterial({ color: 0xd9382c, roughness: 0.8, side: THREE.DoubleSide }), 0.02, 0.1, -0.005);
  sail.rotation.y = 0;
  const jib = new THREE.Shape();
  jib.moveTo(0, 0);
  jib.lineTo(0, 0.4);
  jib.lineTo(-0.22, 0.04);
  jib.closePath();
  add(g, new THREE.ExtrudeGeometry(jib, { depth: 0.01, bevelEnabled: false }), new THREE.MeshStandardMaterial({ color: 0xf4f0e6, roughness: 0.8, side: THREE.DoubleSide }), -0.02, 0.1, -0.005);
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

const buildIdol = (g: THREE.Group): void => {
  const stone = new THREE.MeshStandardMaterial({ color: 0x8d877c, roughness: 0.92 });
  const dark = new THREE.MeshStandardMaterial({ color: 0x2c2926, roughness: 0.9 });
  add(g, new THREE.CylinderGeometry(0.2, 0.28, 0.5, 8), stone, 0, 0, 0);
  add(g, new THREE.SphereGeometry(0.21, 16, 12), stone, 0, 0.36, 0);
  add(g, new THREE.BoxGeometry(0.5, 0.09, 0.3), stone, 0, 0.55, 0);
  for (const s of [-1, 1]) add(g, new THREE.SphereGeometry(0.035, 8, 6), dark, s * 0.08, 0.4, 0.18);
  add(g, new THREE.BoxGeometry(0.14, 0.04, 0.05), dark, 0, 0.3, 0.2);
  add(g, new THREE.BoxGeometry(0.34, 0.06, 0.1), stone, 0, 0.02, 0.24);
};

const buildConch = (g: THREE.Group): void => {
  const shell = new THREE.MeshStandardMaterial({ color: 0xf0b3a6, roughness: 0.55 });
  const lip = new THREE.MeshStandardMaterial({ color: 0xf7d2c8, roughness: 0.4, side: THREE.DoubleSide });
  const body = add(g, new THREE.SphereGeometry(0.27, 18, 14), shell, 0, 0, 0);
  body.scale.set(1.25, 0.8, 0.9);
  // the spire: a few shrinking whorls
  for (let i = 0; i < 4; i++) {
    const w = add(g, new THREE.SphereGeometry(0.2 - i * 0.04, 14, 10), shell, -0.28 - i * 0.12, 0.05 + i * 0.03, 0);
    w.scale.set(1, 0.85, 0.85);
  }
  // the flared lip
  const l = add(g, new THREE.SphereGeometry(0.26, 16, 12, 0, Math.PI), lip, 0.14, 0.02, 0.15);
  l.rotation.set(0.2, Math.PI / 2 + 0.4, 0);
  l.scale.set(0.9, 0.55, 0.5);
};

const buildCache = (g: THREE.Group): void => {
  const wood = new THREE.MeshStandardMaterial({ color: 0x7a4a24, roughness: 0.85 });
  const band = new THREE.MeshStandardMaterial({ color: 0x2c2f34, roughness: 0.45, metalness: 0.7 });
  const gold = metal(0xf2c14e, 0.25);
  add(g, new THREE.BoxGeometry(0.7, 0.36, 0.46), wood, 0, 0, 0);
  const lid = add(g, new THREE.CylinderGeometry(0.23, 0.23, 0.7, 14, 1, false, 0, Math.PI), wood, 0, 0.18, 0);
  lid.rotation.set(0, Math.PI / 2, Math.PI / 2);
  for (const x of [-0.24, 0.24]) {
    add(g, new THREE.BoxGeometry(0.06, 0.38, 0.5), band, x, 0, 0);
    const arch = add(g, new THREE.CylinderGeometry(0.245, 0.245, 0.06, 14, 1, false, 0, Math.PI), band, x, 0.18, 0);
    arch.rotation.set(0, Math.PI / 2, Math.PI / 2);
  }
  add(g, new THREE.BoxGeometry(0.1, 0.12, 0.04), gold, 0, 0.1, 0.25);
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
