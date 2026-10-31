import * as THREE from 'three';

/** How far the fingers sit from the centre of whatever they hold. */
export const gripRadius = (r: number): number => Math.max(r, 0.012) + 0.011;

/** Where the wrist sits in the hand's own space. With `slant` it moves back along the rod, in line with a handle held like a handshake. */
export const wristAt = (r: number, slant: number): THREE.Vector3 => {
  const R = gripRadius(r);
  return slant > 0 ? new THREE.Vector3(-slant, R * 0.35, -R * 0.1) : new THREE.Vector3(0, R * 0.3, -(R + 0.03));
};

/** A bone of the hand: a capsule that tapers from `ra` at a to `rb` at b. */
interface Bone {
  ax: number;
  ay: number;
  az: number;
  bx: number;
  by: number;
  bz: number;
  ra: number;
  rb: number;
}

const bone = (a: THREE.Vector3, b: THREE.Vector3, ra: number, rb = ra): Bone => ({ ax: a.x, ay: a.y, az: a.z, bx: b.x, by: b.y, bz: b.z, ra, rb });

/** Signed distance from a point to a bone: negative inside it. */
const toBone = (s: Bone, x: number, y: number, z: number): number => {
  const dx = s.bx - s.ax;
  const dy = s.by - s.ay;
  const dz = s.bz - s.az;
  const px = x - s.ax;
  const py = y - s.ay;
  const pz = z - s.az;
  const t = Math.min(1, Math.max(0, (px * dx + py * dy + pz * dz) / (dx * dx + dy * dy + dz * dz)));
  const qx = px - dx * t;
  const qy = py - dy * t;
  const qz = pz - dz * t;
  return Math.sqrt(qx * qx + qy * qy + qz * qz) - (s.ra + (s.rb - s.ra) * t);
};

/** The union of two shapes, with the crease between them filled in over a distance of about `k`. */
export const blendShapes = (a: number, b: number, k: number): number => {
  const h = Math.max(k - Math.abs(a - b), 0) / k;
  return Math.min(a, b) - (h * h * k) / 4;
};

const merged = (bones: Bone[], k: number, x: number, y: number, z: number): number => {
  let d = Infinity;
  for (const s of bones) d = blendShapes(d, toBone(s, x, y, z), k);
  return d;
};

/** Turns a distance function into one closed mesh (surface nets), with normals taken from the function so it shades perfectly smooth. */
export const solidSurface = (f: (x: number, y: number, z: number) => number, lo: THREE.Vector3, hi: THREE.Vector3, step: number): THREE.BufferGeometry => {
  const nx = Math.ceil((hi.x - lo.x) / step) + 1;
  const ny = Math.ceil((hi.y - lo.y) / step) + 1;
  const nz = Math.ceil((hi.z - lo.z) / step) + 1;
  const at = (i: number, j: number, k: number): number => i + nx * (j + ny * k);
  const field = new Float32Array(nx * ny * nz);
  // blocks that a coarse sample shows are nowhere near the surface are filled in without asking the function about every point
  const B = 4;
  const reach = (Math.sqrt(3) * B * step) / 2 + 0.01;
  for (let bk = 0; bk < nz; bk += B) {
    for (let bj = 0; bj < ny; bj += B) {
      for (let bi = 0; bi < nx; bi += B) {
        const mid = f(lo.x + (bi + B / 2) * step, lo.y + (bj + B / 2) * step, lo.z + (bk + B / 2) * step);
        const far = Math.abs(mid) > reach;
        for (let k = bk; k < Math.min(nz, bk + B); k++) {
          for (let j = bj; j < Math.min(ny, bj + B); j++) {
            for (let i = bi; i < Math.min(nx, bi + B); i++) field[at(i, j, k)] = far ? mid : f(lo.x + i * step, lo.y + j * step, lo.z + k * step);
          }
        }
      }
    }
  }
  // one vertex in every cell the surface passes through, at the average of where it crosses the cell's edges
  const cell = new Int32Array(field.length).fill(-1);
  const pos: number[] = [];
  const corner = new Float32Array(8);
  for (let k = 0; k < nz - 1; k++) {
    for (let j = 0; j < ny - 1; j++) {
      for (let i = 0; i < nx - 1; i++) {
        let inside = 0;
        for (let c = 0; c < 8; c++) {
          corner[c] = field[at(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1))];
          if (corner[c] < 0) inside++;
        }
        if (inside === 0 || inside === 8) continue;
        let sx = 0;
        let sy = 0;
        let sz = 0;
        let n = 0;
        for (let c = 0; c < 8; c++) {
          for (const bit of [1, 2, 4]) {
            if (c & bit || corner[c] < 0 === corner[c | bit] < 0) continue;
            const t = corner[c] / (corner[c] - corner[c | bit]);
            sx += (c & 1) + (bit === 1 ? t : 0);
            sy += ((c >> 1) & 1) + (bit === 2 ? t : 0);
            sz += ((c >> 2) & 1) + (bit === 4 ? t : 0);
            n++;
          }
        }
        cell[at(i, j, k)] = pos.length / 3;
        pos.push(lo.x + (i + sx / n) * step, lo.y + (j + sy / n) * step, lo.z + (k + sz / n) * step);
      }
    }
  }
  // one quad across every grid edge the surface crosses, joining the four cells around it
  const idx: number[] = [];
  const quad = (inside: boolean, a: number, b: number, c: number, d: number): void => {
    if (inside) idx.push(a, b, c, a, c, d);
    else idx.push(a, c, b, a, d, c);
  };
  for (let k = 1; k < nz - 1; k++) {
    for (let j = 1; j < ny - 1; j++) {
      for (let i = 1; i < nx - 1; i++) {
        const v = field[at(i, j, k)] < 0;
        if (v !== field[at(i + 1, j, k)] < 0) quad(v, cell[at(i, j - 1, k - 1)], cell[at(i, j, k - 1)], cell[at(i, j, k)], cell[at(i, j - 1, k)]);
        if (v !== field[at(i, j + 1, k)] < 0) quad(v, cell[at(i - 1, j, k - 1)], cell[at(i - 1, j, k)], cell[at(i, j, k)], cell[at(i, j, k - 1)]);
        if (v !== field[at(i, j, k + 1)] < 0) quad(v, cell[at(i - 1, j - 1, k)], cell[at(i, j - 1, k)], cell[at(i, j, k)], cell[at(i - 1, j, k)]);
      }
    }
  }
  const nor = new Float32Array(pos.length);
  const e = 0.001;
  for (let i = 0; i < pos.length; i += 3) {
    const [x, y, z] = [pos[i], pos[i + 1], pos[i + 2]];
    // four samples at the corners of a little tetrahedron give the same slope as six along the axes
    const a = f(x + e, y - e, z - e);
    const b = f(x - e, y - e, z + e);
    const c = f(x - e, y + e, z - e);
    const d = f(x + e, y + e, z + e);
    const g = new THREE.Vector3(a - b - c + d, -a - b + c + d, -a + b - c + d).normalize();
    nor.set([g.x, g.y, g.z], i);
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  geo.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  geo.setIndex(idx);
  return geo;
};

/**
 * A right hand closed around a rod of radius `r`, as one solid surface with the start of the forearm grown out of it. In its own space
 * the rod runs along x with the thumb toward +x, the back of the hand faces +y and the fingers reach toward +z from the wrist, then
 * curl around the rod and back. `toElbow` is the unit direction the forearm leaves in. With `slant` the hand instead takes a handle that
 * runs on from the arm, with the wrist that far back along it and the thumb lying along the handle toward +x.
 */
export const buildFist = (r: number, toElbow: THREE.Vector3, mat: THREE.Material, slant = 0): THREE.Mesh => {
  const R = gripRadius(r);
  const v = (x: number, y: number, z: number): THREE.Vector3 => new THREE.Vector3(x, y, z);
  const around = (x: number, rad: number, th: number): THREE.Vector3 => v(x, Math.cos(th) * rad, Math.sin(th) * rad);
  const wrist = wristAt(r, slant);

  const fingers = [
    { x: 0.03, rad: 0.0115, len: 0.085 },
    { x: 0.01, rad: 0.012, len: 0.092 },
    { x: -0.01, rad: 0.0115, len: 0.085 },
    { x: -0.03, rad: 0.0105, len: 0.07 },
  ];
  // the arm always runs on out of the wrist
  const palm: Bone[] = [bone(wrist, wrist.clone().addScaledVector(toElbow, 0.09), 0.028, 0.03)];
  let digits: Bone[][];
  let thumb: Bone[];
  const curled = (f: { x: number; rad: number }, knuckle: THREE.Vector3, start: number, end: number, drift: number): Bone[] => {
    const th = (u: number): number => start + (end - start) * u;
    const pts = [knuckle, around(f.x - drift * 0.4, R, th(0.4)), around(f.x - drift * 0.72, R, th(0.72)), around(f.x - drift, R, th(1))];
    return [bone(pts[0], pts[1], f.rad, f.rad * 0.96), bone(pts[1], pts[2], f.rad * 0.96, f.rad * 0.9), bone(pts[2], pts[3], f.rad * 0.9, f.rad * 0.82)];
  };

  if (slant > 0) {
    // a handle in line with the arm: a flat back of the hand running forward along it, the knuckles to the outside, the fingers
    // curling under, the web of the thumb over the top and the thumb lying along the handle
    const knuckles = fingers.map((f) => v(f.x * 0.95 + 0.008, R * 0.8 + 0.008, R * 0.62));
    fingers.forEach((f, i) => palm.push(bone(v(wrist.x + f.x * 0.2, wrist.y + 0.004, wrist.z - f.x * 0.4), knuckles[i], 0.0135, 0.0125)));
    const t0 = v(0.005, R * 0.25, -(R + 0.004));
    const t1 = v(0.04, R * 0.3, -(R + 0.006));
    const t2 = v(0.072, R * 0.1, -(R + 0.002));
    palm.push(bone(v(wrist.x, wrist.y, wrist.z - 0.01), t0, 0.02, 0.017));
    palm.push(bone(wrist, v(-0.045, -R * 0.7, R * 0.35), 0.017, 0.014));
    digits = fingers.map((f, i) => {
      const start = Math.atan2(knuckles[i].z, knuckles[i].y);
      return curled(f, knuckles[i], start, start + Math.min(3, f.len / R), 0.008);
    });
    thumb = [bone(t0, t1, 0.0165, 0.014), bone(t1, t2, 0.014, 0.0115)];
  } else {
    // a rod across the hand: the bones of the back of the hand and the two pads of the palm
    for (const f of fingers) palm.push(bone(v(wrist.x + f.x * 0.6, wrist.y, wrist.z), v(f.x, R + 0.012, 0.02), 0.0155, 0.014));
    palm.push(bone(v(wrist.x + 0.014, wrist.y, wrist.z), v(0.034, -R * 0.35, -(R + 0.012)), 0.021, 0.018));
    palm.push(bone(v(wrist.x - 0.014, wrist.y, wrist.z), v(-0.03, -R * 0.35, -(R + 0.012)), 0.018, 0.016));
    digits = fingers.map((f) => curled(f, v(f.x, R + 0.012, 0.02), 0.35, 0.35 + Math.min(3.3, f.len / R), 0));
    // the thumb grows from the heel of the hand and closes the other way, over the fingers
    const tR = R + 0.01;
    const tEnd = Math.max(2.7, 4.9 - 0.075 / tR);
    const tTh = (u: number): number => 4.9 + (tEnd - 4.9) * u;
    const t0 = around(0.042, tR, tTh(0));
    const t1 = around(0.04, tR, tTh(0.5));
    const t2 = around(0.032, tR, tTh(1));
    thumb = [bone(v(0.03, -R * 0.2, -(R + 0.012)), t0, 0.018, 0.0155), bone(t0, t1, 0.0155, 0.0135), bone(t1, t2, 0.0135, 0.0115)];
  }

  const dist = (x: number, y: number, z: number): number => {
    let hand = merged(digits[0], 0.003, x, y, z);
    for (let i = 1; i < digits.length; i++) hand = blendShapes(hand, merged(digits[i], 0.003, x, y, z), 0.006);
    hand = blendShapes(hand, merged(palm, 0.018, x, y, z), 0.012);
    return blendShapes(hand, merged(thumb, 0.004, x, y, z), 0.012);
  };

  const box = new THREE.Box3();
  for (const s of [...palm, ...thumb, ...digits.flat()]) {
    box.expandByPoint(v(s.ax, s.ay, s.az));
    box.expandByPoint(v(s.bx, s.by, s.bz));
  }
  box.expandByScalar(0.04);
  return new THREE.Mesh(solidSurface(dist, box.min, box.max, 0.004), mat);
};
