import * as THREE from 'three';
import { ISLAND, WATER_LEVEL, baseHeight, baseHeightMain, islandHeight } from './Beach';

const rng = (seed: number) => () => {
  seed = (seed * 1664525 + 1013904223) % 4294967296;
  return seed / 4294967296;
};

const smooth = (a: number, b: number, x: number): number => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

const jitter = (geo: THREE.BufferGeometry, amount: number, rand: () => number): THREE.BufferGeometry => {
  const p = geo.attributes.position as THREE.BufferAttribute;
  const seen = new Map<string, [number, number, number]>();
  for (let i = 0; i < p.count; i++) {
    const key = `${Math.round(p.getX(i) * 400)},${Math.round(p.getY(i) * 400)},${Math.round(p.getZ(i) * 400)}`;
    let d = seen.get(key);
    if (!d) {
      d = [(rand() - 0.5) * amount, (rand() - 0.5) * amount, (rand() - 0.5) * amount];
      seen.set(key, d);
    }
    p.setXYZ(i, p.getX(i) + d[0], p.getY(i) + d[1], p.getZ(i) + d[2]);
  }
  geo.computeVertexNormals();
  return geo;
};

const slopeAt = (x: number, z: number): number => {
  const e = 0.5;
  const nx = baseHeight(x - e, z) - baseHeight(x + e, z);
  const nz = baseHeight(x, z - e) - baseHeight(x, z + e);
  const n = new THREE.Vector3(nx, 2 * e, nz).normalize();
  return 1 - n.y;
};

interface Spot {
  x: number;
  z: number;
  r: number;
}

/**
 * A small island out at sea, reached only by boat. Its terrain is part of the world's height function (see Beach.ts), so the boat,
 * the player, the maps and every find already know where the land is; this class builds what you see and what you can bump into:
 * the green and rocky ground, palms, bushes, boulders, an old stone ruin on the hilltop and a wrecked boat on the sand.
 */
export class Island {
  readonly group = new THREE.Group();
  /** solid circles for walking around: trunks, boulders, ruin pillars, the wreck */
  readonly colliders: Spot[] = [];
  /** the old stone circle on the summit */
  readonly ruins = { x: ISLAND.x + 1, z: ISLAND.z - 1 };
  private palms: { crown: THREE.Group; phase: number; fronds: THREE.Mesh[]; nuts: THREE.Group }[] = [];
  private shrubs = new THREE.Group();
  private foam: THREE.Mesh[] = [];
  private wreck = { x: 0, z: 0 };
  /** the old map lying on the altar until someone takes it */
  private scroll = new THREE.Group();
  private scrollY = 0;
  private rand = rng(4711);
  /** every material on the island, with its own colour, so distance haze can be applied by hand */
  private haze: { mat: THREE.MeshStandardMaterial | THREE.MeshBasicMaterial; base: THREE.Color }[] = [];

  constructor() {
    this.buildTerrain();
    this.buildFoam();
    this.buildPalms();
    this.group.add(this.shrubs);
    this.buildShrubs();
    this.buildRocks();
    this.buildRuins();
    this.buildScroll();
    this.buildWreck();
    this.prepareHaze();
  }

  /**
   * The island is far out, and the scene's fog would hide it completely at dawn, dusk and night. So its materials ignore the fog
   * and are tinted toward the fog colour by hand instead, with a cap, so it always stays visible as a hazy shape.
   */
  private prepareHaze(): void {
    const seen = new Set<THREE.Material>();
    this.group.traverse((o) => {
      if (!(o instanceof THREE.Mesh)) return;
      for (const m of Array.isArray(o.material) ? o.material : [o.material]) {
        if (seen.has(m)) continue;
        seen.add(m);
        if (m instanceof THREE.MeshStandardMaterial || m instanceof THREE.MeshBasicMaterial) {
          m.fog = false;
          this.haze.push({ mat: m, base: m.color.clone() });
        }
      }
    });
  }

  contains(x: number, z: number): boolean {
    return Math.hypot(x - ISLAND.x, z - ISLAND.z) < ISLAND.R * 1.1;
  }

  /** Where a boat at (bx, bz) can put someone ashore, heading straight for the island. */
  landingFrom(bx: number, bz: number): { x: number; z: number } | null {
    const dx = ISLAND.x - bx;
    const dz = ISLAND.z - bz;
    const L = Math.hypot(dx, dz);
    if (L > ISLAND.R * 1.2 + 30) return null;
    const ux = dx / (L || 1);
    const uz = dz / (L || 1);
    for (let s = 0; s < L; s += 0.4) {
      const x = bx + ux * s;
      const z = bz + uz * s;
      if (baseHeight(x, z) > WATER_LEVEL + 0.3) return { x: x + ux * 1.0, z: z + uz * 1.0 };
    }
    return null;
  }

  // ---------------------------------------------------------------- terrain

  private buildTerrain(): void {
    const half = ISLAND.R * 1.25;
    const step = 0.75;
    const n = Math.ceil((2 * half) / step);
    const geo = new THREE.BufferGeometry();
    const pos = new Float32Array((n + 1) * (n + 1) * 3);
    const col = new Float32Array((n + 1) * (n + 1) * 3);
    const contrib: boolean[] = [];
    const c = new THREE.Color();
    const sandDry = new THREE.Color(0xe8d3a3);
    const sandWet = new THREE.Color(0xb89b6a);
    const floor = new THREE.Color(0x6f9a8a);
    const grass = [new THREE.Color(0x5f9f3b), new THREE.Color(0x86b84f), new THREE.Color(0x3f7e34)];
    const rock = [new THREE.Color(0x77726a), new THREE.Color(0x58544e), new THREE.Color(0x9a9488)];
    for (let j = 0; j <= n; j++) {
      for (let i = 0; i <= n; i++) {
        const x = ISLAND.x - half + i * step;
        const z = ISLAND.z - half + j * step;
        const h = Math.max(baseHeight(x, z), -2.2);
        const k = (j * (n + 1) + i) * 3;
        pos[k] = x;
        pos[k + 1] = h;
        pos[k + 2] = z;
        contrib.push(islandHeight(x, z) > baseHeightMain(x, z) + 0.01);
        const slope = slopeAt(x, z);
        const noise = Math.sin(x * 0.7) * Math.sin(z * 0.6) * 0.5 + 0.5;
        if (h < WATER_LEVEL) {
          c.copy(sandWet).lerp(floor, Math.min(1, (WATER_LEVEL - h) / 1.4));
        } else {
          c.copy(sandWet).lerp(sandDry, smooth(WATER_LEVEL, 0.35, h));
          // grass takes over from the sand a little way up the slope, in patches
          const g = grass[Math.floor(noise * 2.99)].clone().lerp(grass[(Math.floor(noise * 2.99) + 1) % 3], (noise * 3) % 1);
          c.lerp(g, smooth(0.45, 1.1, h + (noise - 0.5) * 0.4));
          // bare rock on the steep cliff and on the summit
          const stone = rock[Math.floor(((Math.sin(x * 1.3 + z * 0.9) + 1) / 2) * 2.99)];
          c.lerp(stone, Math.max(smooth(0.2, 0.42, slope), smooth(3.1, 3.6, h) * 0.8));
        }
        col[k] = c.r;
        col[k + 1] = c.g;
        col[k + 2] = c.b;
      }
    }
    const idx: number[] = [];
    for (let j = 0; j < n; j++) {
      for (let i = 0; i < n; i++) {
        const a = j * (n + 1) + i;
        const b = a + 1;
        const d = a + n + 1;
        const e = d + 1;
        if (!(contrib[a] || contrib[b] || contrib[d] || contrib[e])) continue;
        idx.push(a, d, b, b, d, e);
      }
    }
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    geo.setIndex(idx);
    geo.computeVertexNormals();
    // it overlaps the coarse sea floor under it, and wins where they touch
    const mat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 1, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.receiveShadow = true;
    mesh.castShadow = true;
    this.group.add(mesh);
  }

  /** A band of foam along the whole coast, found by walking out from the centre to where the land meets the water. */
  private buildFoam(): void {
    const steps = 160;
    const rings: [number, number, number][] = [[-0.25, 0.35, 0.55], [-0.6, 1.5, 0.2]];
    const shore: number[] = [];
    for (let k = 0; k < steps; k++) {
      const a = (k / steps) * Math.PI * 2;
      let r = 4;
      while (r < ISLAND.R * 1.3 && baseHeight(ISLAND.x + Math.cos(a) * r, ISLAND.z + Math.sin(a) * r) > WATER_LEVEL) r += 0.25;
      shore.push(r);
    }
    for (const [inner, outer, opacity] of rings) {
      const pos: number[] = [];
      const idx: number[] = [];
      for (let k = 0; k <= steps; k++) {
        const a = ((k % steps) / steps) * Math.PI * 2;
        const r = shore[k % steps];
        pos.push(ISLAND.x + Math.cos(a) * (r + inner), 0, ISLAND.z + Math.sin(a) * (r + inner));
        pos.push(ISLAND.x + Math.cos(a) * (r + outer), 0, ISLAND.z + Math.sin(a) * (r + outer));
        if (k > 0) {
          const b = (k - 1) * 2;
          idx.push(b, b + 1, b + 2, b + 1, b + 3, b + 2);
        }
      }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      g.setIndex(idx);
      const m = new THREE.Mesh(g, new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity, depthWrite: false, side: THREE.DoubleSide, fog: true }));
      m.userData.base = opacity;
      m.renderOrder = 3;
      this.group.add(m);
      this.foam.push(m);
    }
  }

  // ---------------------------------------------------------------- things standing on it

  /** A random spot on dry land that is not too steep, or null after a number of tries. */
  private landSpot(minH: number, maxH: number, maxSlope: number, avoid: Spot[], gap: number, rMin = 3, rMax = ISLAND.R * 0.8): { x: number; z: number } | null {
    for (let tries = 0; tries < 120; tries++) {
      const a = this.rand() * Math.PI * 2;
      const r = rMin + this.rand() * (rMax - rMin);
      const x = ISLAND.x + Math.cos(a) * r;
      const z = ISLAND.z + Math.sin(a) * r;
      const h = baseHeight(x, z);
      if (h < minH || h > maxH || slopeAt(x, z) > maxSlope) continue;
      if (Math.hypot(x - this.ruins.x, z - this.ruins.z) < 5) continue;
      if (avoid.some((s) => Math.hypot(s.x - x, s.z - z) < s.r + gap)) continue;
      return { x, z };
    }
    return null;
  }

  private frondGeometry(): THREE.BufferGeometry {
    const geo = new THREE.PlaneGeometry(0.7, 3.4, 3, 10);
    const p = geo.attributes.position as THREE.BufferAttribute;
    for (let i = 0; i < p.count; i++) {
      const x = p.getX(i);
      const t = (p.getY(i) + 1.7) / 3.4;
      const taper = Math.sin(Math.PI * Math.min(1, t * 1.05 + 0.02)) * 0.85 + 0.15;
      p.setX(i, x * taper);
      p.setZ(i, -t * t * 1.4 + Math.abs(x) * 0.25);
      p.setY(i, p.getY(i) + 1.7);
    }
    geo.computeVertexNormals();
    return geo;
  }

  private buildPalms(): void {
    const rand = this.rand;
    const frond = this.frondGeometry();
    // a lusher, deeper green than the beach palms
    const frondMats = [0x2f8a3a, 0x3c9a44, 0x28762f].map((c) => new THREE.MeshStandardMaterial({ color: c, roughness: 0.85, side: THREE.DoubleSide }));
    const trunkMat = new THREE.MeshStandardMaterial({ color: 0x7a5a3a, roughness: 1 });
    const nutMat = new THREE.MeshStandardMaterial({ color: 0x5a3a1e, roughness: 0.7 });
    const placed: Spot[] = [];
    for (let n = 0; n < 17; n++) {
      const spot = this.landSpot(0.8, 3.1, 0.2, placed, 3.2);
      if (!spot) continue;
      placed.push({ x: spot.x, z: spot.z, r: 0.4 });
      const g = new THREE.Group();
      const h = 5 + rand() * 3;
      const lean = (rand() - 0.5) * 2.6;
      const dir = rand() * Math.PI * 2;
      const curve = new THREE.QuadraticBezierCurve3(
        new THREE.Vector3(0, 0, 0),
        new THREE.Vector3(Math.cos(dir) * lean * 0.2, h * 0.55, Math.sin(dir) * lean * 0.2),
        new THREE.Vector3(Math.cos(dir) * lean, h, Math.sin(dir) * lean),
      );
      const trunk = new THREE.Mesh(new THREE.TubeGeometry(curve, 12, 0.2, 7), trunkMat);
      trunk.castShadow = true;
      g.add(trunk);
      const crown = new THREE.Group();
      crown.position.copy(curve.getPoint(1));
      const frondMeshes: THREE.Mesh[] = [];
      const nutGroup = new THREE.Group();
      crown.add(nutGroup);
      for (let i = 0; i < 9; i++) {
        const f = new THREE.Mesh(frond, frondMats[i % 3]);
        f.rotation.order = 'YXZ';
        f.rotation.y = (i / 9) * Math.PI * 2 + rand() * 0.3;
        f.rotation.x = Math.PI / 2 - (0.35 + rand() * 0.4);
        f.scale.setScalar(0.95 + rand() * 0.4);
        f.userData.s0 = f.scale.x;
        f.castShadow = true;
        crown.add(f);
        frondMeshes.push(f);
      }
      for (let i = 0; i < 4; i++) {
        const nut = new THREE.Mesh(new THREE.SphereGeometry(0.17, 8, 6), nutMat);
        const a = (i / 4) * Math.PI * 2 + rand();
        nut.position.set(Math.cos(a) * 0.22, -0.28, Math.sin(a) * 0.22);
        nutGroup.add(nut);
      }
      g.add(crown);
      g.position.set(spot.x, baseHeight(spot.x, spot.z) - 0.1, spot.z);
      g.rotation.y = rand() * Math.PI * 2;
      this.group.add(g);
      this.palms.push({ crown, phase: rand() * 6, fronds: frondMeshes, nuts: nutGroup });
      this.colliders.push({ x: spot.x, z: spot.z, r: 0.4 });
    }
  }

  private buildShrubs(): void {
    const rand = this.rand;
    const mats = [0x3f8036, 0x4f9a3d, 0x2f6e2c].map((c) => new THREE.MeshStandardMaterial({ color: c, roughness: 0.95, flatShading: true }));
    for (let n = 0; n < 34; n++) {
      const spot = this.landSpot(0.55, 3.0, 0.3, [], 0, 3, ISLAND.R * 0.82);
      if (!spot) continue;
      const clump = new THREE.Group();
      const parts = 2 + Math.floor(rand() * 3);
      for (let i = 0; i < parts; i++) {
        const s = jitter(new THREE.IcosahedronGeometry(0.32 + rand() * 0.25, 1), 0.12, rand);
        const m = new THREE.Mesh(s, mats[Math.floor(rand() * 3)]);
        m.position.set((rand() - 0.5) * 0.7, 0.2 + rand() * 0.1, (rand() - 0.5) * 0.7);
        m.scale.y = 0.65;
        m.castShadow = true;
        clump.add(m);
      }
      clump.position.set(spot.x, baseHeight(spot.x, spot.z) - 0.05, spot.z);
      this.shrubs.add(clump);
    }
  }

  private buildRocks(): void {
    const rand = this.rand;
    const mats = [0x77726a, 0x65605a, 0x8a847a].map((c) => new THREE.MeshStandardMaterial({ color: c, roughness: 0.95, flatShading: true }));
    const placed: Spot[] = [];
    for (let n = 0; n < 14; n++) {
      // mostly on the cliff and the steeper ground, some on the sand
      const spot = this.landSpot(0.0, 3.6, 1, placed, 1.5, 3, ISLAND.R * 0.95);
      if (!spot) continue;
      const r = 0.5 + rand() * 1.0;
      placed.push({ x: spot.x, z: spot.z, r });
      const geo = jitter(new THREE.IcosahedronGeometry(1, 1), 0.35, rand);
      const m = new THREE.Mesh(geo, mats[Math.floor(rand() * 3)]);
      m.scale.set(r * (0.9 + rand() * 0.3), r * (0.6 + rand() * 0.3), r * (0.9 + rand() * 0.3));
      m.rotation.y = rand() * 6;
      m.position.set(spot.x, baseHeight(spot.x, spot.z) + r * 0.1, spot.z);
      m.castShadow = true;
      m.receiveShadow = true;
      this.group.add(m);
      if (r >= 0.6) this.colliders.push({ x: spot.x, z: spot.z, r: r * 0.85 });
    }
  }

  /** An old circle of stone pillars on the summit, some still joined by a lintel, with a low altar in the middle. */
  private buildRuins(): void {
    const rand = this.rand;
    const stone = new THREE.MeshStandardMaterial({ color: 0x9a948a, roughness: 0.95, flatShading: true });
    const moss = new THREE.MeshStandardMaterial({ color: 0x5c8a3c, roughness: 1 });
    const { x: cx, z: cz } = this.ruins;
    const base = baseHeight(cx, cz);
    const g = new THREE.Group();
    g.position.set(cx, base, cz);
    const box = (w: number, h: number, d: number, mat: THREE.Material, x: number, y: number, z: number): THREE.Mesh => {
      const m = new THREE.Mesh(jitter(new THREE.BoxGeometry(w, h, d), 0.06, rand), mat);
      m.position.set(x, y + h / 2, z);
      m.castShadow = true;
      m.receiveShadow = true;
      g.add(m);
      return m;
    };
    const N = 6;
    const heights = [2.8, 2.8, 1.7, 2.6, 2.6, 1.3];
    for (let i = 0; i < N; i++) {
      const a = (i / N) * Math.PI * 2 + 0.4;
      const x = Math.cos(a) * 3.5;
      const z = Math.sin(a) * 3.5;
      const h = heights[i];
      const p = box(0.78, h, 0.78, stone, x, 0, z);
      p.rotation.y = a + rand() * 0.2;
      box(0.86, 0.12, 0.86, stone, x, h, z).rotation.y = p.rotation.y;
      if (h > 2.4) box(0.8, 0.05, 0.8, moss, x, h + 0.12, z);
      this.colliders.push({ x: cx + x, z: cz + z, r: 0.55 });
    }
    // two lintels across the tall pairs
    for (const [i, j] of [[0, 1], [3, 4]]) {
      const a1 = (i / N) * Math.PI * 2 + 0.4;
      const a2 = (j / N) * Math.PI * 2 + 0.4;
      const x1 = Math.cos(a1) * 3.5;
      const z1 = Math.sin(a1) * 3.5;
      const x2 = Math.cos(a2) * 3.5;
      const z2 = Math.sin(a2) * 3.5;
      const len = Math.hypot(x2 - x1, z2 - z1) + 0.6;
      const lintel = box(len, 0.42, 0.7, stone, (x1 + x2) / 2, heights[i] + 0.12, (z1 + z2) / 2);
      lintel.rotation.y = -Math.atan2(z2 - z1, x2 - x1);
    }
    // the altar
    box(1.9, 0.18, 1.3, stone, 0, 0, 0);
    box(1.5, 0.5, 1.0, stone, 0, 0.18, 0);
    box(1.7, 0.14, 1.2, stone, 0, 0.68, 0);
    this.colliders.push({ x: cx, z: cz, r: 1.05 });
    // a fallen column lying in the grass
    const col = new THREE.Mesh(jitter(new THREE.CylinderGeometry(0.34, 0.38, 2.4, 10), 0.05, rand), stone);
    col.rotation.z = Math.PI / 2;
    col.rotation.y = 0.8;
    col.position.set(-2.3, 0.4, 3.2);
    col.castShadow = true;
    g.add(col);
    this.group.add(g);
  }

  /** A rolled-up old map on top of the altar. */
  private buildScroll(): void {
    const paper = new THREE.MeshStandardMaterial({ color: 0xe6d3a2, roughness: 0.9, emissive: 0x6b4a10, emissiveIntensity: 0.12 });
    const edge = new THREE.MeshStandardMaterial({ color: 0xc9ad74, roughness: 0.9 });
    const ribbon = new THREE.MeshStandardMaterial({ color: 0xc23b2f, roughness: 0.6 });
    const roll = new THREE.Mesh(new THREE.CylinderGeometry(0.075, 0.075, 0.5, 14), paper);
    roll.rotation.z = Math.PI / 2;
    this.scroll.add(roll);
    for (const x of [-0.25, 0.25]) {
      const cap = new THREE.Mesh(new THREE.CylinderGeometry(0.085, 0.085, 0.03, 14), edge);
      cap.rotation.z = Math.PI / 2;
      cap.position.x = x;
      this.scroll.add(cap);
    }
    const tie = new THREE.Mesh(new THREE.TorusGeometry(0.08, 0.012, 6, 14), ribbon);
    tie.rotation.y = Math.PI / 2;
    this.scroll.add(tie);
    const { x, z } = this.ruins;
    // the top of the altar is 0.82 above the summit
    this.scrollY = baseHeight(x, z) + 0.82 + 0.08;
    this.scroll.position.set(x, this.scrollY, z);
    this.scroll.rotation.y = 0.5;
    this.scroll.traverse((o) => {
      if (o instanceof THREE.Mesh) o.castShadow = true;
    });
    this.group.add(this.scroll);
  }

  get mapAvailable(): boolean {
    return this.scroll.visible;
  }

  /** Whether the player is standing close enough to the altar to take the map. */
  nearMap(px: number, pz: number): boolean {
    return this.scroll.visible && Math.hypot(px - this.ruins.x, pz - this.ruins.z) < 2.7;
  }

  /** The map is taken: it leaves the altar. */
  takeMap(): void {
    this.scroll.visible = false;
  }

  /** The hull of a small boat half buried in the sand near the water, with a broken mast. */
  private buildWreck(): void {
    // walk out from the middle toward the main beach (north) until the ground meets the water, then back up a little
    const a = -1.25;
    let r = 6;
    while (r < ISLAND.R * 1.2 && baseHeight(ISLAND.x + Math.cos(a) * r, ISLAND.z + Math.sin(a) * r) > 0.15) r += 0.25;
    r -= 1.8;
    const x = ISLAND.x + Math.cos(a) * r;
    const z = ISLAND.z + Math.sin(a) * r;
    this.wreck = { x, z };
    const wood = new THREE.MeshStandardMaterial({ color: 0x7b5a3a, roughness: 0.95, side: THREE.DoubleSide });
    const grey = new THREE.MeshStandardMaterial({ color: 0x8c7d68, roughness: 0.95 });
    const g = new THREE.Group();
    const hull = new THREE.Mesh(new THREE.CylinderGeometry(0.95, 0.95, 3.6, 14, 1, true, 0, Math.PI), wood);
    hull.rotation.set(0, 0, Math.PI / 2);
    hull.rotation.order = 'ZYX';
    hull.scale.set(0.8, 1, 1);
    hull.position.y = 0.55;
    hull.castShadow = true;
    g.add(hull);
    for (let i = -2; i <= 2; i++) {
      const rib = new THREE.Mesh(new THREE.TorusGeometry(0.88, 0.04, 6, 14, Math.PI), grey);
      rib.position.set(i * 0.7, 0.55, 0);
      rib.rotation.y = Math.PI / 2;
      rib.rotation.x = Math.PI;
      rib.scale.set(1, 0.8, 1);
      g.add(rib);
    }
    const mast = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.09, 2.2, 8), grey);
    mast.position.set(-0.3, 1.5, 0);
    mast.rotation.z = 0.35;
    mast.castShadow = true;
    g.add(mast);
    g.position.set(x, baseHeight(x, z) - 0.15, z);
    g.rotation.y = 0.5 + this.rand() * 1.5;
    g.rotation.z = 0.12;
    this.group.add(g);
    this.colliders.push({ x, z, r: 1.4 });
  }

  /** Where the island's own finds lie in the open: idols below the altar, pearls by the wreck and the cliff, conches on the shore. */
  findSpots(): { id: string; x: number; z: number }[] {
    const rand = rng(99);
    const out: { id: string; x: number; z: number }[] = [];
    const r = this.ruins;
    out.push({ id: 'stone_idol', x: r.x - 1.0, z: r.z + 1.9 }, { id: 'stone_idol', x: r.x + 1.1, z: r.z + 2.0 });
    out.push({ id: 'black_pearl', x: this.wreck.x + 2.0, z: this.wreck.z + 1.0 });
    // a second pearl tucked at the foot of the cliff on the western side
    const ca = 3.0;
    out.push({ id: 'black_pearl', x: ISLAND.x + Math.cos(ca) * 13, z: ISLAND.z + Math.sin(ca) * 13 });
    const taken: { x: number; z: number }[] = [...out];
    const place = (id: string, minH: number, maxH: number, rMin: number, rMax: number): void => {
      for (let tries = 0; tries < 200; tries++) {
        const a = rand() * Math.PI * 2;
        const rad = rMin + rand() * (rMax - rMin);
        const x = ISLAND.x + Math.cos(a) * rad;
        const z = ISLAND.z + Math.sin(a) * rad;
        const h = baseHeight(x, z);
        if (h < minH || h > maxH || slopeAt(x, z) > 0.2) continue;
        if (this.colliders.some((c) => Math.hypot(c.x - x, c.z - z) < c.r + 1.0)) continue;
        if (taken.some((t) => Math.hypot(t.x - x, t.z - z) < 4)) continue;
        out.push({ id, x, z });
        taken.push({ x, z });
        return;
      }
    };
    for (let i = 0; i < 4; i++) place('conch', 0.25, 0.6, 12, ISLAND.R * 0.85);
    for (let i = 0; i < 4; i++) place('piece_eight', 0.9, 3.0, 4, ISLAND.R * 0.7);
    return out;
  }

  update(t: number, surfaceY: number, fog: THREE.Color, density: number, camX: number, camZ: number, dt = 0): void {
    const dist = Math.hypot(camX - ISLAND.x, camZ - ISLAND.z);
    const d = Math.max(0, dist - ISLAND.R * 0.6);
    const f = Math.min(0.62, 1 - Math.exp(-Math.pow(d * density, 2)));
    for (const { mat, base } of this.haze) {
      if (mat instanceof THREE.MeshStandardMaterial) {
        mat.color.copy(base).multiplyScalar(1 - f);
        mat.emissive.copy(fog).multiplyScalar(f * 0.9);
      } else {
        mat.color.copy(base).lerp(fog, f);
      }
    }
    // the map bobs very slightly, as if it wants to be picked up
    this.scroll.position.y = this.scrollY + Math.sin(t * 2) * 0.012;
    for (const p of this.palms) {
      p.crown.rotation.z = Math.sin(t * 0.8 + p.phase) * 0.03;
      p.crown.rotation.x = Math.cos(t * 0.6 + p.phase) * 0.025;
    }
    this.foam.forEach((f, i) => {
      f.position.y = surfaceY + 0.05;
      const m = f.material as THREE.MeshBasicMaterial;
      m.opacity = (f.userData.base as number) * (0.7 + 0.3 * Math.sin(t * 0.9 + i * 1.7));
    });
  }
}
