import * as THREE from 'three';

/** The room under the dunes sits far below the beach, so nothing on the surface can ever be near it. Local x and z are world x and z. */
export const UG = { y: -60 };

/** the room is 16 long (x) and 10 wide (z); the ceiling is 4.5 up */
const ROOM = { x0: -8, x1: 8, z0: -5, z1: 5, h: 4.5 };
/** the stairs run along the north wall, rising toward the east, and climb through a hole in the ceiling */
const STAIRS = { x0: -4, x1: 6.95, z0: -5, z1: -3, steps: 15, rise: 0.29, landingX1: 7.8 };
const STEP_RUN = (STAIRS.x1 - STAIRS.x0) / STAIRS.steps;
const TOP = STAIRS.steps * STAIRS.rise;
/** past the top step a wooden ladder, like the one under the hatch outside, climbs the rest of the way to the rim of the stairwell */
const SHAFT_TOP = STAIRS.steps * STAIRS.rise + 3.0;
const TILE = 2.5;

const rng = (seed: number): (() => number) => {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
};

/** A block-stone texture: rows of dressed blocks with dark mortar, offset row by row. */
const stoneTexture = (base: [number, number, number], rows: number, cols: number, seed: number): THREE.CanvasTexture => {
  const size = 256;
  const cv = document.createElement('canvas');
  cv.width = cv.height = size;
  const c = cv.getContext('2d')!;
  const r = rng(seed);
  c.fillStyle = '#17120e';
  c.fillRect(0, 0, size, size);
  const bh = size / rows;
  const bw = size / cols;
  for (let y = 0; y < rows; y++) {
    const off = (y % 2) * (bw / 2);
    for (let x = -1; x < cols + 1; x++) {
      const k = 0.82 + r() * 0.3;
      c.fillStyle = `rgb(${Math.round(base[0] * k)},${Math.round(base[1] * k)},${Math.round(base[2] * k)})`;
      c.fillRect(x * bw + off + 2, y * bh + 2, bw - 4, bh - 4);
      // a little grain on each block
      for (let i = 0; i < 24; i++) {
        const g = r() * 0.5 - 0.25;
        c.fillStyle = g > 0 ? `rgba(255,240,220,${g * 0.25})` : `rgba(0,0,0,${-g * 0.5})`;
        c.fillRect(x * bw + off + 4 + r() * (bw - 10), y * bh + 4 + r() * (bh - 10), 2 + r() * 4, 1 + r() * 3);
      }
    }
  }
  const t = new THREE.CanvasTexture(cv);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 4;
  return t;
};

/** A box whose texture coordinates are scaled to its size, so blocks stay the same size on every wall. */
const slab = (w: number, h: number, d: number, mat: THREE.Material | THREE.Material[], x: number, y: number, z: number): THREE.Mesh => {
  const g = new THREE.BoxGeometry(w, h, d);
  const uv = g.attributes.uv as THREE.BufferAttribute;
  const dims = [[d, h], [d, h], [w, d], [w, d], [w, h], [w, h]];
  for (let f = 0; f < 6; f++) {
    for (let v = 0; v < 4; v++) {
      const i = f * 4 + v;
      uv.setXY(i, (uv.getX(i) * dims[f][0]) / TILE, (uv.getY(i) * dims[f][1]) / TILE);
    }
  }
  const m = new THREE.Mesh(g, mat);
  m.position.set(x, y, z);
  m.castShadow = true;
  m.receiveShadow = true;
  return m;
};

interface Torch {
  flames: THREE.Mesh[];
  light: THREE.PointLight;
  ph: number;
}

/** The chamber below the hatch: a stone room with wall torches, a stair up to an open hatch with the sky above it, and a few old furnishings. */
export class Underground {
  readonly group = new THREE.Group();
  readonly colliders: { x: number; z: number; r: number }[] = [];
  /** where you stand after coming down, and which way you face */
  readonly entry = { x: -6.5, z: 0.5, yaw: -Math.PI / 2 };
  private torches: Torch[] = [];

  constructor() {
    this.group.position.y = UG.y;
    this.group.visible = false;
    const wall = new THREE.MeshStandardMaterial({ map: stoneTexture([122, 112, 100], 4, 2, 11), roughness: 0.95 });
    const floor = new THREE.MeshStandardMaterial({ map: stoneTexture([96, 90, 82], 4, 4, 23), roughness: 0.9 });
    const roof = new THREE.MeshStandardMaterial({ map: stoneTexture([82, 76, 70], 4, 2, 37), roughness: 1 });
    const stairMat = new THREE.MeshStandardMaterial({ map: stoneTexture([138, 128, 114], 2, 2, 51), roughness: 0.9 });
    const wood = new THREE.MeshStandardMaterial({ color: 0x6b4a30, roughness: 0.95 });
    const darkWood = new THREE.MeshStandardMaterial({ color: 0x4a3322, roughness: 0.95 });
    const iron = new THREE.MeshStandardMaterial({ color: 0x2c2f34, roughness: 0.5, metalness: 0.7 });
    const add = (o: THREE.Object3D): void => void this.group.add(o);

    // floor and the four walls
    add(slab(16.6, 0.4, 10.6, floor, 0, -0.2, 0));
    add(slab(16.6, ROOM.h, 0.5, wall, 0, ROOM.h / 2, ROOM.z0 - 0.25));
    add(slab(16.6, ROOM.h, 0.5, wall, 0, ROOM.h / 2, ROOM.z1 + 0.25));
    add(slab(0.5, ROOM.h, 10.6, wall, ROOM.x0 - 0.25, ROOM.h / 2, 0));
    add(slab(0.5, ROOM.h, 10.6, wall, ROOM.x1 + 0.25, ROOM.h / 2, 0));

    // the ceiling, with a hole over the top of the stairs
    const hole = { x0: 2.6, x1: 7.8, z0: -5, z1: -3 };
    add(slab(hole.x0 + 8.5, 0.5, 11, roof, (-8.5 + hole.x0) / 2, ROOM.h + 0.25, 0));
    add(slab(8.5 - hole.x0, 0.5, 5.5 - hole.z1, roof, (hole.x0 + 8.5) / 2, ROOM.h + 0.25, (hole.z1 + 5.5) / 2));

    // the stairwell: walls on up to a rim, open to the sky
    // Each wall starts exactly where the thing below it ends and never overlaps another box, so no two faces lie in the same plane
    // (which made the stairwell flicker). The north and east walls start at the top of the room's own walls; the west and south
    // ones sit on top of the ceiling slabs.
    const wallTo = (y0: number): { h: number; y: number } => ({ h: SHAFT_TOP - y0, y: (SHAFT_TOP + y0) / 2 });
    const fromWall = wallTo(ROOM.h);
    const fromRoof = wallTo(ROOM.h + 0.5);
    // north: from the stairwell's west edge to the room's east wall
    add(slab(8.5 - hole.x0, fromWall.h, 0.5, wall, (hole.x0 + 8.5) / 2, fromWall.y, hole.z0 - 0.25));
    // east: between the north wall and the ceiling slab
    add(slab(8.5 - hole.x1, fromWall.h, hole.z1 - hole.z0, wall, (hole.x1 + 8.5) / 2, fromWall.y, (hole.z0 + hole.z1) / 2));
    // west and south: standing on the ceiling slabs
    add(slab(0.5, fromRoof.h, hole.z1 - hole.z0, wall, hole.x0 - 0.25, fromRoof.y, (hole.z0 + hole.z1) / 2));
    add(slab(hole.x1 - hole.x0, fromRoof.h, 0.5, wall, (hole.x0 + hole.x1) / 2, fromRoof.y, hole.z1 + 0.25));
    // a timber frame around the opening, and the door standing open beside it
    for (const [w, d, x, z] of [[hole.x1 - hole.x0 + 1.2, 0.3, (hole.x0 + hole.x1) / 2, hole.z0 - 0.1], [hole.x1 - hole.x0 + 1.2, 0.3, (hole.x0 + hole.x1) / 2, hole.z1 + 0.1]] as const) {
      const f = new THREE.Mesh(new THREE.BoxGeometry(w, 0.18, d), darkWood);
      f.position.set(x, SHAFT_TOP + 0.09, z);
      f.castShadow = true;
      add(f);
    }
    // the hatch door: hinged on the east rim and standing open, leaning back away from the opening
    const door = new THREE.Group();
    const planks = 6;
    const span = hole.z1 - hole.z0;
    for (let i = 0; i < planks; i++) {
      const p = new THREE.Mesh(new THREE.BoxGeometry(2.4, 0.08, span / planks - 0.02), i % 2 ? wood : darkWood);
      p.position.set(-1.2, 0, -span / 2 + (span / planks) * (i + 0.5));
      p.castShadow = true;
      door.add(p);
    }
    for (const x of [-0.5, -1.9]) {
      const strap = new THREE.Mesh(new THREE.BoxGeometry(0.14, 0.04, span + 0.04), iron);
      strap.position.set(x, 0.06, 0);
      door.add(strap);
    }
    const handle = new THREE.Mesh(new THREE.TorusGeometry(0.14, 0.03, 6, 16), iron);
    handle.position.set(-1.2, 0.1, 0);
    handle.rotation.x = Math.PI / 2;
    door.add(handle);
    door.position.set(hole.x1 + 0.2, SHAFT_TOP + 0.18, (hole.z0 + hole.z1) / 2);
    door.rotation.z = -1.92;
    add(door);

    // the wooden ladder up the east wall of the stairwell, the same as the one that goes down from the hatch outside
    const rail = new THREE.MeshStandardMaterial({ color: 0x6b4a30, roughness: 0.95 });
    const ladderX = hole.x1 - 0.08;
    const rails = [(hole.z0 + hole.z1) / 2 - 0.45, (hole.z0 + hole.z1) / 2 + 0.45];
    for (const z of rails) {
      const r = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.035, 3.5, 8), rail);
      r.position.set(ladderX, TOP - 0.1 + 1.75, z);
      r.castShadow = true;
      add(r);
    }
    for (let i = 0; i < 12; i++) {
      const rung = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.025, 0.9, 6), rail);
      rung.rotation.x = Math.PI / 2;
      rung.position.set(ladderX, TOP + 0.2 + i * 0.28, (hole.z0 + hole.z1) / 2);
      rung.castShadow = true;
      add(rung);
    }

    // the stairs: solid blocks, so they read as one stone flight
    for (let i = 0; i < STAIRS.steps; i++) {
      const top = (i + 1) * STAIRS.rise;
      add(slab(STEP_RUN, top, STAIRS.z1 - STAIRS.z0, stairMat, STAIRS.x0 + STEP_RUN * (i + 0.5), top / 2, (STAIRS.z0 + STAIRS.z1) / 2));
    }
    add(slab(STAIRS.landingX1 - STAIRS.x1, TOP, STAIRS.z1 - STAIRS.z0, stairMat, (STAIRS.x1 + STAIRS.landingX1) / 2, TOP / 2, (STAIRS.z0 + STAIRS.z1) / 2));

    this.buildTorches();
    this.buildFurniture(wood, darkWood, iron);
  }

  /** The height of the floor at a point: flat, or the steps of the stair. */
  floorAt(x: number, z: number): number {
    if (z > STAIRS.z0 - 0.2 && z < STAIRS.z1 && x > STAIRS.x0) {
      if (x >= STAIRS.x1) return TOP;
      return (Math.floor((x - STAIRS.x0) / STEP_RUN) + 1) * STAIRS.rise;
    }
    return 0;
  }

  /** Keeps a walker inside the walls. */
  clamp(x: number, z: number): { x: number; z: number } {
    return { x: Math.min(STAIRS.landingX1 - 0.45, Math.max(ROOM.x0 + 0.4, x)), z: Math.min(ROOM.z1 - 0.4, Math.max(ROOM.z0 + 0.3, z)) };
  }

  /** how far the ladder lifts you above the top step; at the top of it the hatch can be reached */
  readonly ladderH = 2.0;

  /** True where the ladder hangs: at the east end of the landing, against the wall. */
  onLadder(x: number, z: number): boolean {
    return x > STAIRS.x1 + 0.2 && z > STAIRS.z0 + 0.2 && z < STAIRS.z1 - 0.2;
  }

  setActive(on: boolean): void {
    this.group.visible = on;
  }

  update(t: number): void {
    for (const tc of this.torches) {
      const k = 1 + Math.sin(t * 11 + tc.ph) * 0.12 + Math.sin(t * 6.3 + tc.ph * 2) * 0.1 + (Math.random() - 0.5) * 0.06;
      tc.light.intensity = 11 * k;
      tc.flames.forEach((f, i) => {
        f.scale.set(1 / Math.sqrt(k), k, 1 / Math.sqrt(k));
        f.rotation.y = t * (2 + i) + tc.ph;
      });
    }
  }

  // ---------------------------------------------------------------- torches

  private torch(x: number, y: number, z: number, faceX: number, faceZ: number): void {
    const iron = new THREE.MeshStandardMaterial({ color: 0x2c2f34, roughness: 0.5, metalness: 0.7 });
    const wood = new THREE.MeshStandardMaterial({ color: 0x5a3b24, roughness: 0.95 });
    const char = new THREE.MeshStandardMaterial({ color: 0x1a120c, roughness: 1 });
    const g = new THREE.Group();
    // built facing +z (out of the wall), then turned to face into the room
    const plate = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.4, 0.05), iron);
    plate.position.z = 0.03;
    const arm = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.05, 0.4), iron);
    arm.position.set(0, -0.1, 0.22);
    const stick = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.05, 0.75, 8), wood);
    stick.position.set(0, 0.15, 0.36);
    stick.rotation.x = -0.3;
    const head = new THREE.Mesh(new THREE.CylinderGeometry(0.075, 0.05, 0.16, 8), char);
    head.position.set(0, 0.55, 0.28);
    g.add(plate, arm, stick, head);
    const flames: THREE.Mesh[] = [];
    const outer = new THREE.Mesh(new THREE.ConeGeometry(0.11, 0.45, 8), new THREE.MeshBasicMaterial({ color: 0xff8a22, transparent: true, opacity: 0.92 }));
    outer.position.set(0, 0.84, 0.28);
    const inner = new THREE.Mesh(new THREE.ConeGeometry(0.06, 0.28, 6), new THREE.MeshBasicMaterial({ color: 0xffe27a }));
    inner.position.set(0, 0.78, 0.28);
    g.add(outer, inner);
    flames.push(outer, inner);
    const light = new THREE.PointLight(0xff9a3a, 11, 13, 2);
    light.position.set(0, 0.85, 0.5);
    g.add(light);
    g.position.set(x, y, z);
    g.rotation.y = Math.atan2(faceX, faceZ);
    this.group.add(g);
    this.torches.push({ flames, light, ph: Math.random() * 6 });
  }

  private buildTorches(): void {
    // along the south wall, the west wall, and the north wall above the lower steps
    for (const x of [-5, 0, 5]) this.torch(x, 2.2, ROOM.z1 - 0.02, 0, -1);
    this.torch(ROOM.x0 + 0.02, 2.2, 0.5, 1, 0);
    this.torch(-6.5, 2.2, ROOM.z0 + 0.02, 0, 1);
    this.torch(-1, 3.4, ROOM.z0 + 0.02, 0, 1);
    // up the stairwell, so the climb is lit
    this.torch(4.2, 5.9, ROOM.z0 + 0.02, 0, 1);
    this.torch(6.6, 6.4, ROOM.z0 + 0.02, 0, 1);
  }

  // ---------------------------------------------------------------- furnishings

  private buildFurniture(wood: THREE.Material, darkWood: THREE.Material, iron: THREE.Material): void {
    const stone = new THREE.MeshStandardMaterial({ map: stoneTexture([150, 140, 126], 2, 2, 71), roughness: 0.85 });
    const post = (x: number, z: number, r: number): void => void this.colliders.push({ x, z, r });
    // stone pillars
    for (const [x, z] of [[-3, 2.6], [1.5, 2.6], [6, 2.6], [-3, -1.4]] as const) {
      const base = new THREE.Mesh(new THREE.BoxGeometry(1.1, 0.35, 1.1), stone);
      base.position.set(x, 0.175, z);
      const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.42, 0.46, ROOM.h - 0.7, 14), stone);
      shaft.position.set(x, ROOM.h / 2, z);
      const cap = new THREE.Mesh(new THREE.BoxGeometry(1.1, 0.35, 1.1), stone);
      cap.position.set(x, ROOM.h - 0.175, z);
      for (const m of [base, shaft, cap]) {
        m.castShadow = true;
        m.receiveShadow = true;
        this.group.add(m);
      }
      post(x, z, 0.65);
    }
    // barrels and crates in the south-west corner
    const band = iron;
    for (const [x, z] of [[-7, 4], [-6.1, 4.3], [-7.1, 3.1]] as const) {
      const b = new THREE.Mesh(new THREE.CylinderGeometry(0.4, 0.34, 0.9, 14), wood);
      b.position.set(x, 0.45, z);
      b.castShadow = true;
      b.receiveShadow = true;
      this.group.add(b);
      for (const y of [0.2, 0.7]) {
        const r = new THREE.Mesh(new THREE.TorusGeometry(0.38 - (y < 0.4 ? 0.02 : 0), 0.02, 5, 16), band);
        r.rotation.x = Math.PI / 2;
        r.position.set(x, y, z);
        this.group.add(r);
      }
      post(x, z, 0.5);
    }
    for (const [x, z, s, r] of [[-5.2, 4.1, 0.8, 0.2], [-5.4, 3.3, 0.6, -0.4]] as const) {
      const c = new THREE.Mesh(new THREE.BoxGeometry(s, s, s), darkWood);
      c.position.set(x, s / 2, z);
      c.rotation.y = r;
      c.castShadow = true;
      c.receiveShadow = true;
      this.group.add(c);
      post(x, z, s * 0.7);
    }
    // a long table and two benches near the west wall
    const table = new THREE.Mesh(new THREE.BoxGeometry(2.4, 0.12, 1.0), wood);
    table.position.set(-6.3, 0.95, -1.5);
    table.rotation.y = Math.PI / 2;
    table.castShadow = true;
    this.group.add(table);
    for (const [dx, dz] of [[-0.45, -1.0], [0.45, -1.0], [-0.45, 1.0], [0.45, 1.0]]) {
      const leg = new THREE.Mesh(new THREE.BoxGeometry(0.12, 0.95, 0.12), darkWood);
      leg.position.set(-6.3 + dx, 0.475, -1.5 + dz);
      this.group.add(leg);
    }
    for (const dx of [-0.95, 0.95]) {
      const bench = new THREE.Mesh(new THREE.BoxGeometry(0.4, 0.1, 2.2), wood);
      bench.position.set(-6.3 + dx, 0.5, -1.5);
      bench.castShadow = true;
      this.group.add(bench);
    }
    post(-6.3, -1.5, 1.5);
    // a lantern and an old map-case on the table
    const lantern = new THREE.Mesh(new THREE.CylinderGeometry(0.1, 0.12, 0.28, 8), new THREE.MeshStandardMaterial({ color: 0xffd98a, emissive: 0xffa030, emissiveIntensity: 0.9 }));
    lantern.position.set(-6.3, 1.2, -1.2);
    this.group.add(lantern);
    // a round stone dais in the middle, with a ring cut into it
    const dais = new THREE.Mesh(new THREE.CylinderGeometry(1.6, 1.8, 0.3, 28), stone);
    dais.position.set(0, 0.15, -0.3);
    dais.receiveShadow = true;
    dais.castShadow = true;
    this.group.add(dais);
    const ring = new THREE.Mesh(new THREE.TorusGeometry(1.2, 0.04, 6, 40), new THREE.MeshStandardMaterial({ color: 0x8a6a3a, metalness: 0.6, roughness: 0.5 }));
    ring.rotation.x = Math.PI / 2;
    ring.position.set(0, 0.31, -0.3);
    this.group.add(ring);
  }
}
