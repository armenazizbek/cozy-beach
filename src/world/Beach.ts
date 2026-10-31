import * as THREE from 'three';

export const BEACH_SIZE = 140;
export const BEACH_SEGMENTS = 520;
export const HALF = BEACH_SIZE / 2;
export const CAMP_Z = -60;
export const WATER_LEVEL = -0.3;
export const WET_Z = 31;
export const SHORE_Z = 40;
export const DIG_MIN_Z = -45;
const MAX_DUG = 1.6;
const MIN_DUG = -0.3; // piled-up sand around a hole sits above the surface

const smooth = (a: number, b: number, x: number): number => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

const rawHeight = (x: number, z: number): number => {
  const slope = 0.95 - (z + 22.5) * 0.02;
  const calm = 1 - smooth(22, 36, z) * 0.85; // flatter sand near the water keeps the shoreline tidy
  const dune =
    (Math.sin(x * 0.23 + z * 0.11) * 0.07 + Math.sin(x * 0.09 - z * 0.17) * 0.06 + Math.sin(x * 0.5 + z * 0.41) * 0.015) * calm;
  const ridge = smooth(-50, -68, z) * 0.7;
  // rolling dune hills inland, well away from the diggable sand
  const hills = smooth(-62, -78, z) * (Math.sin(x * 0.07 + z * 0.05) * 1.1 + Math.sin(x * 0.13 - z * 0.09) * 0.6);
  return slope + dune + ridge + hills;
};

export const CAMP_Y = rawHeight(0, CAMP_Z);

/** A small island out at sea, a boat ride from the beach. */
export const ISLAND = { x: -14, z: 200, R: 22 };

/** How far out to sea the boat can go and the sea creatures roam: out to x = +-150 and z = 250. */
export const SEA = { x: 150, z: 250 };

/** Height of the island at a point, or a very low number away from it so the sea floor wins there. */
export const islandHeight = (x: number, z: number): number => {
  const dx = x - ISLAND.x;
  const dz = z - ISLAND.z;
  const d = Math.hypot(dx, dz);
  if (d > ISLAND.R * 1.2) return -10;
  const a = Math.atan2(dz, dx);
  // an uneven coast, not a perfect circle
  const rn = ISLAND.R * (1 + 0.16 * Math.sin(3 * a + 1.2) + 0.09 * Math.sin(5 * a + 0.4) + 0.05 * Math.sin(9 * a));
  const t = d / rn;
  if (t >= 1) return -10;
  let h = -2.2 + 6.4 * (1 - t * t);
  // a flat summit for the ruins
  const top = 3.7;
  if (h > top) h = top + (h - top) * 0.12;
  // rolling land above the water, and a cliff on the western side
  const land = smooth(-0.3, 1.0, h);
  h += land * (Math.sin(x * 0.31 + z * 0.17) * 0.28 + Math.sin(x * 0.13 - z * 0.27) * 0.35 + Math.sin(x * 0.6 + z * 0.5) * 0.06);
  const cliff = Math.exp(-Math.pow(Math.atan2(Math.sin(a - 3.0), Math.cos(a - 3.0)) / 0.45, 2));
  h += land * cliff * 1.3 * smooth(0.15, 0.6, t);
  return h;
};

/** Undisturbed sand height of the main beach and the sea floor. The camp site is levelled so props sit flat. */
export const baseHeightMain = (x: number, z: number): number => {
  const d = Math.hypot(x, z - CAMP_Z);
  const k = 1 - smooth(10, 14, d);
  return rawHeight(x, z) * (1 - k) + CAMP_Y * k;
};

/** Height of everything: the beach, the sea floor and the island. */
export const baseHeight = (x: number, z: number): number => Math.max(baseHeightMain(x, z), islandHeight(x, z));

/** The beach is cut into squares of this many grid cells, each drawn at the detail its distance needs. */
const CHUNK_CELLS = 52;
/** every how many grid cells a detail level keeps a vertex */
const LOD_STEPS = [1, 2, 4];
/** metres from the camera at which a chunk drops to the next level */
const LOD_NEAR = 16;
const LOD_MID = 45;
/** a chunk with holes in it stays at this level or finer, so a hole keeps its shape from afar */
const LOD_DUG = 1;

/**
 * The triangles of one chunk at one detail level. The rim of a chunk is always kept at full detail, so neighbours at different levels meet
 * exactly and no gap can open between them; the coarser middle is stitched to that rim.
 */
const chunkIndex = (r0: number, c0: number, step: number, n: number): number[] => {
  const N = CHUNK_CELLS;
  const at = (r: number, c: number): number => (r0 + r) * n + c0 + c;
  const idx: number[] = [];
  const quad = (r: number, c: number, s: number): void => {
    const a = at(r, c);
    const b = at(r + s, c);
    const e = at(r + s, c + s);
    const d = at(r, c + s);
    idx.push(a, b, d, b, e, d);
  };
  if (step === 1) {
    for (let r = 0; r < N; r++) for (let c = 0; c < N; c++) quad(r, c, 1);
    return idx;
  }
  for (let r = step; r < N - step; r += step) for (let c = step; c < N - step; c += step) quad(r, c, step);
  const ring = (lo: number, hi: number, s: number): number[] => {
    const out: number[] = [];
    for (let c = lo; c < hi; c += s) out.push(at(lo, c));
    for (let r = lo; r < hi; r += s) out.push(at(r, hi));
    for (let c = hi; c > lo; c -= s) out.push(at(hi, c));
    for (let r = hi; r > lo; r -= s) out.push(at(r, lo));
    return out;
  };
  const outer = ring(0, N, 1);
  const inner = ring(step, N - step, step);
  let i = 0;
  let j = 0;
  while (i < outer.length || j < inner.length) {
    const o = outer[i % outer.length];
    const w = inner[j % inner.length];
    if (j >= inner.length || (i < outer.length && (i + 1) / outer.length <= (j + 1) / inner.length)) {
      idx.push(outer[(i + 1) % outer.length], o, w);
      i++;
    } else {
      idx.push(w, inner[(j + 1) % inner.length], o);
      j++;
    }
  }
  return idx;
};

const DRY = new THREE.Color('#e6ccb2');
const WET = new THREE.Color('#b08968');
const DARK = new THREE.Color('#7f5539');

/** Coarse terrain ring around the playable beach so the world never just ends. */
export const createSurround = (): THREE.Mesh => {
  const size = 760;
  const seg = 152; // 5m cells, so the playable square lines up exactly with the grid
  const geo = new THREE.PlaneGeometry(size, size, seg, seg);
  geo.rotateX(-Math.PI / 2);
  const pos = geo.attributes.position as THREE.BufferAttribute;
  const col = new Float32Array(pos.count * 3);
  const c = new THREE.Color();
  for (let i = 0; i < pos.count; i++) {
    const x = pos.getX(i);
    const z = pos.getZ(i);
    // the same height function as the beach (including the levelled camp pad), so the two meet cleanly
    let y = baseHeightMain(x, z);
    // rolling dunes far behind the camp, and a floor under the sea
    if (z < -HALF) y += Math.sin(x * 0.04) * 1.2 + Math.sin(x * 0.11 + z * 0.05) * 0.5 + (-z - HALF) * 0.02;
    y = Math.max(y, -2.2);
    // the ring of vertices touching the beach sits a little below it. If the coarse terrain were even
    // slightly higher than the beach edge, the gap between them would show the sky as a pale streak.
    const onEdge = (Math.abs(Math.abs(x) - HALF) < 0.01 && Math.abs(z) <= HALF + 0.01) || (Math.abs(Math.abs(z) - HALF) < 0.01 && Math.abs(x) <= HALF + 0.01);
    if (onEdge) y -= 0.3;
    pos.setY(i, y);
    c.copy(DRY).lerp(WET, smooth(WET_Z - 2.5, WET_Z + 2, z));
    c.lerp(new THREE.Color('#d9b98c'), smooth(-HALF, -HALF - 60, z) * 0.5);
    col.set([c.r, c.g, c.b], i * 3);
  }
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  // leave out every triangle that lies under the playable beach, so a dug hole shows a real floor
  const index = geo.index!;
  const kept: number[] = [];
  const inBeach = (v: number): boolean => Math.abs(pos.getX(v)) <= HALF + 0.01 && Math.abs(pos.getZ(v)) <= HALF + 0.01;
  for (let i = 0; i < index.count; i += 3) {
    const a = index.getX(i);
    const b = index.getX(i + 1);
    const c2 = index.getX(i + 2);
    if (!(inBeach(a) && inBeach(b) && inBeach(c2))) kept.push(a, b, c2);
  }
  geo.setIndex(kept);
  geo.computeVertexNormals();
  const m = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 1 }));
  m.receiveShadow = true;
  return m;
};

/**
 * A thin sand-coloured curtain hanging from the whole edge of the playable beach. The beach is
 * finely detailed and the surrounding terrain is coarse, so along the seam they differ by a few
 * centimetres; without this, a sliver of sky shows through that gap as a pale blue streak.
 */
export const createSkirt = (): THREE.Mesh => {
  const n = BEACH_SEGMENTS;
  const step = BEACH_SIZE / n;
  const depth = 5;
  const pts: [number, number][] = [];
  // walk round the perimeter
  for (let i = 0; i <= n; i++) pts.push([-HALF + i * step, -HALF]);
  for (let i = 1; i <= n; i++) pts.push([HALF, -HALF + i * step]);
  for (let i = 1; i <= n; i++) pts.push([HALF - i * step, HALF]);
  for (let i = 1; i < n; i++) pts.push([-HALF, HALF - i * step]);
  pts.push(pts[0]);
  const pos: number[] = [];
  const col: number[] = [];
  const idx: number[] = [];
  const c = new THREE.Color();
  pts.forEach(([x, z], i) => {
    const h = baseHeight(x, z) - 0.02;
    c.copy(DRY).lerp(WET, smooth(WET_Z - 2.5, WET_Z + 2, z));
    pos.push(x, h, z, x, h - depth, z);
    col.push(c.r, c.g, c.b, c.r * 0.7, c.g * 0.7, c.b * 0.7);
    if (i > 0) {
      const a = (i - 1) * 2;
      idx.push(a, a + 1, a + 2, a + 1, a + 3, a + 2);
    }
  });
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  g.setIndex(idx);
  g.computeVertexNormals();
  return new THREE.Mesh(g, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 1, side: THREE.DoubleSide }));
};

/** Tileable speckle used as a bump map so the sand looks grainy up close. */
const grainTexture = (): THREE.CanvasTexture => {
  const size = 256;
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const g = c.getContext('2d')!;
  const img = g.createImageData(size, size);
  for (let i = 0; i < size * size; i++) {
    const v = 110 + Math.random() * 90;
    img.data[i * 4] = img.data[i * 4 + 1] = img.data[i * 4 + 2] = v;
    img.data[i * 4 + 3] = 255;
  }
  g.putImageData(img, 0, 0);
  // a few soft blotches give the grain larger ripples to catch the light
  g.globalAlpha = 0.18;
  for (let i = 0; i < 90; i++) {
    const x = Math.random() * size;
    const y = Math.random() * size;
    const r = 6 + Math.random() * 18;
    const grad = g.createRadialGradient(x, y, 0, x, y, r);
    const light = Math.random() < 0.5;
    grad.addColorStop(0, light ? '#fff' : '#000');
    grad.addColorStop(1, 'rgba(128,128,128,0)');
    g.fillStyle = grad;
    for (const ox of [-size, 0, size]) for (const oy of [-size, 0, size]) g.fillRect(x - r + ox, y - r + oy, r * 2, r * 2);
  }
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(BEACH_SIZE / 1.6, BEACH_SIZE / 1.6);
  t.anisotropy = 8;
  return t;
};

/** Tileable "turned-over damp sand": dark clods, pale pebbles and shell flecks. */
const dampTexture = (): THREE.CanvasTexture => {
  const size = 256;
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const g = c.getContext('2d')!;
  g.fillStyle = 'rgb(170,160,148)';
  g.fillRect(0, 0, size, size);
  const wrap = (fn: (ox: number, oy: number) => void): void => {
    for (const ox of [-size, 0, size]) for (const oy of [-size, 0, size]) fn(ox, oy);
  };
  const rnd = (a: number, b: number): number => a + Math.random() * (b - a);
  // soft dark clods
  for (let i = 0; i < 70; i++) {
    const x = rnd(0, size);
    const y = rnd(0, size);
    const r = rnd(8, 24);
    const dark = Math.random() < 0.7;
    wrap((ox, oy) => {
      const grad = g.createRadialGradient(x + ox, y + oy, 0, x + ox, y + oy, r);
      grad.addColorStop(0, dark ? 'rgba(80,66,54,0.55)' : 'rgba(205,190,170,0.4)');
      grad.addColorStop(1, 'rgba(0,0,0,0)');
      g.fillStyle = grad;
      g.fillRect(x + ox - r, y + oy - r, r * 2, r * 2);
    });
  }
  // fine grit
  for (let i = 0; i < 2600; i++) {
    const v = Math.random() < 0.5 ? rnd(95, 135) : rnd(185, 225);
    g.fillStyle = `rgba(${v},${v - 6},${v - 14},0.7)`;
    g.fillRect(rnd(0, size), rnd(0, size), rnd(1, 2.4), rnd(1, 2.4));
  }
  // little pebbles and shell fragments with a dark underside
  for (let i = 0; i < 55; i++) {
    const x = rnd(0, size);
    const y = rnd(0, size);
    const rx = rnd(1.5, 4.5);
    const ry = rx * rnd(0.6, 1);
    const shell = Math.random() < 0.3;
    wrap((ox, oy) => {
      g.fillStyle = 'rgba(55,44,36,0.6)';
      g.beginPath();
      g.ellipse(x + ox + 0.8, y + oy + 1, rx, ry, 0, 0, Math.PI * 2);
      g.fill();
      g.fillStyle = shell ? 'rgb(238,228,210)' : `rgb(${rnd(150, 200)},${rnd(135, 180)},${rnd(115, 160)})`;
      g.beginPath();
      g.ellipse(x + ox, y + oy, rx, ry, 0, 0, Math.PI * 2);
      g.fill();
    });
  }
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = 8;
  return t;
};

interface Chunk {
  mesh: THREE.Mesh;
  lods: THREE.BufferGeometry[];
  x: number;
  z: number;
  lod: number;
  dug: boolean;
}

export class Beach {
  readonly mesh = new THREE.Group();
  private chunks: Chunk[] = [];
  private geo: THREE.PlaneGeometry;
  private pos: THREE.BufferAttribute;
  private colAttr: THREE.BufferAttribute;
  private dampAttr: THREE.BufferAttribute;
  private n = BEACH_SEGMENTS + 1;
  private step = BEACH_SIZE / BEACH_SEGMENTS;
  private base: Float32Array;
  private dug: Float32Array;
  private baseColors: Float32Array;
  private tmp = new THREE.Color();

  constructor() {
    this.geo = new THREE.PlaneGeometry(BEACH_SIZE, BEACH_SIZE, BEACH_SEGMENTS, BEACH_SEGMENTS);
    this.geo.rotateX(-Math.PI / 2);
    this.pos = this.geo.attributes.position as THREE.BufferAttribute;
    const count = this.pos.count;
    this.base = new Float32Array(count);
    this.dug = new Float32Array(count);
    this.baseColors = new Float32Array(count * 3);
    this.colAttr = new THREE.BufferAttribute(new Float32Array(count * 3), 3);
    this.geo.setAttribute('color', this.colAttr);
    this.dampAttr = new THREE.BufferAttribute(new Float32Array(count), 1);
    this.geo.setAttribute('damp', this.dampAttr);

    for (let i = 0; i < count; i++) {
      const x = this.pos.getX(i);
      const z = this.pos.getZ(i);
      this.base[i] = baseHeight(x, z);
      this.pos.setY(i, this.base[i]);
      const wet = smooth(WET_Z - 2.5, WET_Z + 2, z);
      this.tmp.copy(DRY).lerp(WET, wet);
      const grain = 0.96 + Math.random() * 0.08;
      this.baseColors[i * 3] = this.tmp.r * grain;
      this.baseColors[i * 3 + 1] = this.tmp.g * grain;
      this.baseColors[i * 3 + 2] = this.tmp.b * grain;
    }
    this.refreshColors();
    this.geo.computeVertexNormals();

    this.buildChunks(this.makeMaterial());
  }

  /** Every chunk draws from the one shared grid of vertices, so a dug hole shows up at once whatever detail level is in use. */
  private buildChunks(mat: THREE.Material): void {
    const size = CHUNK_CELLS * this.step;
    const across = BEACH_SEGMENTS / CHUNK_CELLS;
    for (let row = 0; row < across; row++) {
      for (let col = 0; col < across; col++) {
        const r0 = row * CHUNK_CELLS;
        const c0 = col * CHUNK_CELLS;
        let lo = Infinity;
        let hi = -Infinity;
        for (let r = 0; r <= CHUNK_CELLS; r++) {
          for (let c = 0; c <= CHUNK_CELLS; c++) {
            const y = this.base[(r0 + r) * this.n + c0 + c];
            lo = Math.min(lo, y);
            hi = Math.max(hi, y);
          }
        }
        const x = -HALF + (col + 0.5) * size;
        const z = -HALF + (row + 0.5) * size;
        const sphere = new THREE.Sphere(new THREE.Vector3(x, (lo + hi) / 2, z), Math.hypot(size * 0.71, (hi - lo) / 2 + MAX_DUG));
        const lods = LOD_STEPS.map((step) => {
          const g = new THREE.BufferGeometry();
          for (const name of ['position', 'normal', 'color', 'damp']) g.setAttribute(name, this.geo.attributes[name]);
          g.setIndex(chunkIndex(r0, c0, step, this.n));
          g.boundingSphere = sphere;
          return g;
        });
        const mesh = new THREE.Mesh(lods[0], mat);
        mesh.receiveShadow = true;
        this.mesh.add(mesh);
        this.chunks.push({ mesh, lods, x, z, lod: 0, dug: false });
      }
    }
  }

  /** Puts every chunk on one detail level, so that loading can send them all to the graphics chip. The next frame sets them right. */
  setAllDetail(lod: number): void {
    for (const c of this.chunks) {
      c.lod = lod;
      c.mesh.geometry = c.lods[lod];
    }
  }

  /** Picks each chunk's detail for where the camera is. */
  updateDetail(camX: number, camZ: number): void {
    const half = (CHUNK_CELLS * this.step) / 2;
    for (const c of this.chunks) {
      const d = Math.hypot(Math.max(0, Math.abs(camX - c.x) - half), Math.max(0, Math.abs(camZ - c.z) - half));
      // a few metres of give, so a chunk standing at the edge does not flicker between two levels
      const give = c.lod === 0 ? 3 : 0;
      const give2 = c.lod <= 1 ? 3 : 0;
      let lod = d > LOD_MID + give2 ? 2 : d > LOD_NEAR + give ? 1 : 0;
      if (c.dug) lod = Math.min(lod, LOD_DUG);
      if (lod !== c.lod) {
        c.lod = lod;
        c.mesh.geometry = c.lods[lod];
      }
    }
  }

  /** Remembers which chunks have been dug. */
  private noteDug(minR: number, maxR: number, minC: number, maxC: number): void {
    const across = BEACH_SEGMENTS / CHUNK_CELLS;
    const last = across - 1;
    for (let r = Math.min(last, Math.floor(minR / CHUNK_CELLS)); r <= Math.min(last, Math.floor(maxR / CHUNK_CELLS)); r++) {
      for (let c = Math.min(last, Math.floor(minC / CHUNK_CELLS)); c <= Math.min(last, Math.floor(maxC / CHUNK_CELLS)); c++) this.chunks[r * across + c].dug = true;
    }
  }

  /** Sand material that blends in a detailed damp-sand texture wherever the ground has been dug. */
  private makeMaterial(): THREE.MeshStandardMaterial {
    const damp = dampTexture();
    const mat = new THREE.MeshStandardMaterial({
      vertexColors: true, roughness: 1, metalness: 0, bumpMap: grainTexture(), bumpScale: 1.4,
    });
    mat.onBeforeCompile = (shader) => {
      shader.uniforms.dampMap = { value: damp };
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nattribute float damp;\nvarying float vDamp;\nvarying vec3 vWPos;')
        .replace('#include <begin_vertex>', '#include <begin_vertex>\nvDamp = damp;\nvWPos = (modelMatrix * vec4(transformed, 1.0)).xyz;');
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', '#include <common>\nuniform sampler2D dampMap;\nvarying float vDamp;\nvarying vec3 vWPos;')
        .replace(
          '#include <map_fragment>',
          `#include <map_fragment>
          vec2 duv = vWPos.xz * 0.9;
          vec3 dt = mix(texture2D(dampMap, duv).rgb, texture2D(dampMap, duv * 3.3 + 0.37).rgb, 0.45);
          diffuseColor.rgb *= mix(vec3(1.0), dt * 1.55, smoothstep(0.04, 0.55, vDamp));`,
        );
    };
    return mat;
  }

  private idx(x: number, z: number): number {
    const c = Math.round((x + HALF) / this.step);
    const r = Math.round((z + HALF) / this.step);
    return Math.min(this.n - 1, Math.max(0, r)) * this.n + Math.min(this.n - 1, Math.max(0, c));
  }

  private sample(arr: Float32Array, x: number, z: number): number {
    const fx = Math.min(this.n - 1.001, Math.max(0, (x + HALF) / this.step));
    const fz = Math.min(this.n - 1.001, Math.max(0, (z + HALF) / this.step));
    const c = Math.floor(fx);
    const r = Math.floor(fz);
    const tx = fx - c;
    const tz = fz - r;
    const i = r * this.n + c;
    const a = arr[i] * (1 - tx) + arr[i + 1] * tx;
    const b = arr[i + this.n] * (1 - tx) + arr[i + this.n + 1] * tx;
    return a * (1 - tz) + b * tz;
  }

  heightAt(x: number, z: number): number {
    return baseHeight(x, z) - this.sample(this.dug, x, z);
  }

  depthAt(x: number, z: number): number {
    return Math.max(0, this.sample(this.dug, x, z));
  }

  canDig(z: number): boolean {
    return z > DIG_MIN_Z && z < SHORE_Z - 2;
  }

  /** Marches a ray against the height field. */
  raycast(origin: THREE.Vector3, dir: THREE.Vector3): THREE.Vector3 | null {
    const step = 0.2;
    const p = new THREE.Vector3();
    let prevT = 0;
    for (let t = 0; t < 140; t += step) {
      p.copy(dir).multiplyScalar(t).add(origin);
      const inGrid = Math.abs(p.x) <= HALF && Math.abs(p.z) <= HALF;
      if (!inGrid && Math.hypot(p.x - ISLAND.x, p.z - ISLAND.z) > ISLAND.R * 1.2) {
        prevT = t;
        continue;
      }
      if (p.y <= this.heightAt(p.x, p.z)) {
        let lo = prevT;
        let hi = t;
        for (let k = 0; k < 8; k++) {
          const mid = (lo + hi) / 2;
          p.copy(dir).multiplyScalar(mid).add(origin);
          if (p.y <= this.heightAt(p.x, p.z)) hi = mid;
          else lo = mid;
        }
        p.copy(dir).multiplyScalar(hi).add(origin);
        p.y = this.heightAt(p.x, p.z);
        return p.clone();
      }
      prevT = t;
    }
    return null;
  }

  /**
   * Digs a bowl with fairly steep walls and a wobbly, natural edge, and heaps some of the
   * sand into a low rim around it.
   */
  digAt(x: number, z: number, radius: number, depth: number): void {
    const reach = radius * 1.8;
    const minC = Math.max(0, Math.floor((x - reach + HALF) / this.step));
    const maxC = Math.min(this.n - 1, Math.ceil((x + reach + HALF) / this.step));
    const minR = Math.max(0, Math.floor((z - reach + HALF) / this.step));
    const maxR = Math.min(this.n - 1, Math.ceil((z + reach + HALF) / this.step));
    const ph1 = Math.random() * 6.283;
    const ph2 = Math.random() * 6.283;
    for (let r = minR; r <= maxR; r++) {
      for (let c = minC; c <= maxC; c++) {
        const i = r * this.n + c;
        const dx = this.pos.getX(i) - x;
        const dz = this.pos.getZ(i) - z;
        const d = Math.hypot(dx, dz);
        if (d >= reach) continue;
        const ang = Math.atan2(dz, dx);
        const wobble = 1 + 0.1 * Math.sin(ang * 5 + ph1) + 0.06 * Math.sin(ang * 9 + ph2);
        const dn = d / wobble / radius;
        let delta = 0;
        if (dn < 1) {
          delta = depth * (1 - dn * dn * dn);
        } else if (dn < 1.8) {
          const t = (dn - 1) / 0.8;
          delta = -0.34 * depth * Math.sin(Math.PI * Math.pow(t, 0.6));
        }
        this.dug[i] = Math.min(MAX_DUG, Math.max(MIN_DUG, this.dug[i] + delta));
        this.pos.setY(i, this.base[i] - this.dug[i]);
      }
    }
    this.noteDug(minR, maxR, minC, maxC);
    this.refreshColors(minR, maxR, minC, maxC);
    this.updateNormals(Math.max(0, minR - 1), Math.min(this.n - 1, maxR + 1), Math.max(0, minC - 1), Math.min(this.n - 1, maxC + 1));
    this.pos.needsUpdate = true;
    this.colAttr.needsUpdate = true;
    this.dampAttr.needsUpdate = true;
    this.geo.attributes.normal.needsUpdate = true;
  }

  /** Smooths away every hole and heap within `radius` of a point, so the ground there is untouched again. */
  clearDug(x: number, z: number, radius: number): void {
    const minC = Math.max(0, Math.floor((x - radius + HALF) / this.step));
    const maxC = Math.min(this.n - 1, Math.ceil((x + radius + HALF) / this.step));
    const minR = Math.max(0, Math.floor((z - radius + HALF) / this.step));
    const maxR = Math.min(this.n - 1, Math.ceil((z + radius + HALF) / this.step));
    for (let r = minR; r <= maxR; r++) {
      for (let c = minC; c <= maxC; c++) {
        const i = r * this.n + c;
        if (Math.hypot(this.pos.getX(i) - x, this.pos.getZ(i) - z) > radius) continue;
        this.dug[i] = 0;
        this.pos.setY(i, this.base[i]);
      }
    }
    this.refreshColors(minR, maxR, minC, maxC);
    this.updateNormals(Math.max(0, minR - 1), Math.min(this.n - 1, maxR + 1), Math.max(0, minC - 1), Math.min(this.n - 1, maxC + 1));
    this.pos.needsUpdate = true;
    this.colAttr.needsUpdate = true;
    this.dampAttr.needsUpdate = true;
    this.geo.attributes.normal.needsUpdate = true;
  }

  /** Recomputes normals only around a dig, straight from the height field. */
  private updateNormals(minR: number, maxR: number, minC: number, maxC: number): void {
    const nrm = this.geo.attributes.normal as THREE.BufferAttribute;
    const h = (r: number, c: number): number => {
      const i = Math.min(this.n - 1, Math.max(0, r)) * this.n + Math.min(this.n - 1, Math.max(0, c));
      return this.base[i] - this.dug[i];
    };
    const k = 1 / (2 * this.step);
    for (let r = minR; r <= maxR; r++) {
      for (let c = minC; c <= maxC; c++) {
        const nx = (h(r, c - 1) - h(r, c + 1)) * k;
        const nz = (h(r - 1, c) - h(r + 1, c)) * k;
        const len = Math.hypot(nx, 1, nz);
        nrm.setXYZ(r * this.n + c, nx / len, 1 / len, nz / len);
      }
    }
  }

  private refreshColors(minR = 0, maxR = this.n - 1, minC = 0, maxC = this.n - 1): void {
    for (let r = minR; r <= maxR; r++) {
      for (let c = minC; c <= maxC; c++) {
        const i = r * this.n + c;
        const dug = this.dug[i];
        // dark damp sand in the hole, slightly darker freshly-heaped sand on the rim
        const k = dug > 0 ? Math.min(1, dug / 0.2) : Math.min(1, -dug / 0.12) * 0.4;
        const ao = 1 - Math.min(0.42, Math.max(0, dug) * 0.6);
        this.dampAttr.setX(i, k);
        for (let ch = 0; ch < 3; ch++) {
          const b = this.baseColors[i * 3 + ch];
          const d = ch === 0 ? DARK.r : ch === 1 ? DARK.g : DARK.b;
          this.colAttr.array[i * 3 + ch] = (b + (d - b) * k) * ao;
        }
      }
    }
  }

  /** Sparse copy of every dug (or heaped) vertex, for saving. */
  exportDug(): [number, number][] {
    const out: [number, number][] = [];
    for (let i = 0; i < this.dug.length; i++) {
      if (Math.abs(this.dug[i]) > 0.002) out.push([i, Math.round(this.dug[i] * 1000) / 1000]);
    }
    return out;
  }

  /** Puts saved holes back and rebuilds the sand mesh around them. */
  importDug(list: [number, number][]): void {
    this.dug.fill(0);
    for (const [i, v] of list) this.dug[i] = v;
    for (let i = 0; i < this.dug.length; i++) this.pos.setY(i, this.base[i] - this.dug[i]);
    for (const c of this.chunks) c.dug = false;
    for (const [i] of list) {
      const r = Math.floor(i / this.n);
      const c = i % this.n;
      this.noteDug(r, r, c, c);
    }
    this.refreshColors();
    this.geo.computeVertexNormals();
    this.pos.needsUpdate = true;
    this.colAttr.needsUpdate = true;
    this.dampAttr.needsUpdate = true;
    this.geo.attributes.normal.needsUpdate = true;
  }

  /** The z where the sand meets a given water level at x. */
  shoreZ(x: number, level: number): number {
    let lo = 0;
    let hi = HALF;
    for (let k = 0; k < 10; k++) {
      const mid = (lo + hi) / 2;
      if (baseHeight(x, mid) > level) lo = mid;
      else hi = mid;
    }
    return (lo + hi) / 2;
  }
}
