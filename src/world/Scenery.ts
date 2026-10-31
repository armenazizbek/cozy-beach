import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { CAMP_Z, baseHeight } from './Beach';

/** tiny deterministic PRNG so the island looks the same every visit */
const rng = (seed: number) => () => {
  seed = (seed * 1664525 + 1013904223) % 4294967296;
  return seed / 4294967296;
};

interface Nut {
  mesh: THREE.Mesh;
  crown: THREE.Group;
  home: THREE.Vector3;
  /** on the tree and shootable */
  onTree: boolean;
  /** seconds until it hangs on the tree again */
  regrow: number;
  vel: THREE.Vector3;
  falling: boolean;
  /** seconds it lies on the sand before vanishing */
  lie: number;
  /** 0..1 how far it has grown back on the tree */
  growT: number;
}

interface Palm {
  group: THREE.Group;
  crown: THREE.Group;
  phase: number;
  x: number;
  z: number;
  /** the small parts that only matter up close: the rings on the trunk, and the coconuts */
  rings: THREE.Mesh;
  nuts: THREE.Group;
  /** half the fronds, kept at every distance */
  crownA: THREE.Mesh;
  /** the other half, which shrink away far off */
  crownB: THREE.Mesh;
  /** 0 = every part, 1 = no rings or coconuts, 2 = and half the fronds */
  lod: number;
  /** 0..1 how much of the small detail is showing: the rings, coconuts and every other frond ease in and out instead of popping */
  k: number;
  kf: number;
}

/** How far a palm is before it is drawn with fewer parts. Well beyond the reach of the sun's shadow, so shadows are unchanged. */
const LOD_MID = 42;
const LOD_FAR = 90;

export class Scenery {
  readonly group = new THREE.Group();
  /** trunk footprints, for simple collision */
  readonly trunks: { x: number; z: number }[] = [];
  private palms: Palm[] = [];
  private nuts: Nut[] = [];
  private nutTargets: THREE.Object3D[] = [];
  private mountainMats: { mat: THREE.MeshBasicMaterial; haze: number }[] = [];
  private tmp = new THREE.Color();
  private tint = new THREE.Color(0x6f8fa6);

  constructor() {
    this.buildPalms();
    this.buildMountains();
  }

  // ------------------------------------------------------------ palms

  private frondGeometry(): THREE.BufferGeometry {
    const geo = new THREE.PlaneGeometry(0.7, 3.4, 3, 10);
    const p = geo.attributes.position as THREE.BufferAttribute;
    for (let i = 0; i < p.count; i++) {
      const x = p.getX(i);
      const t = (p.getY(i) + 1.7) / 3.4; // 0 at the base, 1 at the tip
      const taper = Math.sin(Math.PI * Math.min(1, t * 1.05 + 0.02)) * 0.85 + 0.15;
      p.setX(i, x * taper);
      p.setZ(i, -t * t * 1.4 + Math.abs(x) * 0.25); // droop and a slight V-fold
      p.setY(i, p.getY(i) + 1.7);
    }
    geo.computeVertexNormals();
    return geo;
  }

  private buildPalms(): void {
    const rand = rng(7);
    const frondGeo = this.frondGeometry();
    const frondCols = [0x3f8f3a, 0x4d9d41, 0x35802f].map((c) => new THREE.Color(c));
    const frondMat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.85, side: THREE.DoubleSide });
    const trunkMat = new THREE.MeshStandardMaterial({ color: 0x8a6a46, roughness: 1 });
    const nutMat = new THREE.MeshStandardMaterial({ color: 0x5a3a1e, roughness: 0.7 });

    const spots: [number, number][] = [];
    // lining both sides of the beach
    for (let i = 0; i < 20; i++) spots.push([-(64 + rand() * 12), -66 + rand() * 106]);
    for (let i = 0; i < 20; i++) spots.push([64 + rand() * 12, -66 + rand() * 106]);
    // inland behind the camp
    for (let i = 0; i < 20; i++) spots.push([(rand() - 0.5) * 130, -(66 + rand() * 10)]);
    // scattered across the open beach
    for (let i = 0; i < 46; i++) spots.push([(rand() - 0.5) * 128, -44 + rand() * 72]);
    // leaning toward the water
    for (let i = 0; i < 18; i++) spots.push([(rand() - 0.5) * 130, 34 + rand() * 3]);

    for (const [x, z] of spots) {
      if (Math.hypot(x, z - CAMP_Z) < 15) continue; // keep the camp clear
      const g = new THREE.Group();
      const h = 5.5 + rand() * 3;
      const lean = (rand() - 0.5) * 2.2;
      const leanDir = rand() * Math.PI * 2;
      const curve = new THREE.QuadraticBezierCurve3(
        new THREE.Vector3(0, 0, 0),
        new THREE.Vector3(Math.cos(leanDir) * lean * 0.2, h * 0.55, Math.sin(leanDir) * lean * 0.2),
        new THREE.Vector3(Math.cos(leanDir) * lean, h, Math.sin(leanDir) * lean),
      );
      // the trunk reaches well down into the sand, so even the deepest hole never uncovers its foot
      const rootGeo = new THREE.CylinderGeometry(0.2, 0.22, 2, 8);
      rootGeo.translate(0, -1, 0);
      const trunk = new THREE.Mesh(mergeGeometries([rootGeo, new THREE.TubeGeometry(curve, 14, 0.2, 8)]), trunkMat);
      trunk.castShadow = true;
      trunk.receiveShadow = true;
      g.add(trunk);
      // rings along the trunk (and down the buried foot, in case someone digs beside the tree)
      const ringGeos: THREE.BufferGeometry[] = [];
      for (let i = -3; i < 12; i++) {
        const pt = i > 0 ? curve.getPoint(i / 12) : new THREE.Vector3(0, (i / 12) * h, 0);
        const ring = new THREE.TorusGeometry(0.21 - Math.max(0, i) * 0.004, 0.03, 5, 10);
        ring.rotateX(Math.PI / 2);
        ring.translate(pt.x, pt.y, pt.z);
        ringGeos.push(ring);
      }
      const rings = new THREE.Mesh(mergeGeometries(ringGeos), trunkMat);
      g.add(rings);
      const crown = new THREE.Group();
      crown.position.copy(curve.getPoint(1));
      const halves: THREE.BufferGeometry[][] = [[], []];
      const nutGroup = new THREE.Group();
      crown.add(nutGroup);
      const fronds = 9;
      for (let i = 0; i < fronds; i++) {
        const f = new THREE.Object3D();
        f.rotation.order = 'YXZ';
        f.rotation.y = (i / fronds) * Math.PI * 2 + rand() * 0.3;
        f.rotation.x = Math.PI / 2 - (0.35 + rand() * 0.4);
        f.scale.setScalar(0.9 + rand() * 0.35);
        f.updateMatrix();
        const shade = frondCols[i % 3];
        const geo = frondGeo.clone().applyMatrix4(f.matrix);
        const tint = new Float32Array(geo.attributes.position.count * 3);
        for (let k = 0; k < tint.length; k += 3) tint.set([shade.r, shade.g, shade.b], k);
        geo.setAttribute('color', new THREE.BufferAttribute(tint, 3));
        halves[i % 2].push(geo);
      }
      const crownA = new THREE.Mesh(mergeGeometries(halves[0]), frondMat);
      const crownB = new THREE.Mesh(mergeGeometries(halves[1]), frondMat);
      crownA.castShadow = crownB.castShadow = true;
      crown.add(crownA, crownB);
      const nuts = 3 + Math.floor(rand() * 3);
      for (let i = 0; i < nuts; i++) {
        const nut = new THREE.Mesh(new THREE.SphereGeometry(0.17, 10, 8), nutMat);
        const a = (i / nuts) * Math.PI * 2 + rand();
        nut.position.set(Math.cos(a) * 0.22, -0.28 - rand() * 0.1, Math.sin(a) * 0.22);
        nut.castShadow = true;
        nut.userData.hitRadius = 0.5;
        nut.userData.coconut = true;
        nutGroup.add(nut);
        this.nuts.push({ mesh: nut, crown, home: nut.position.clone(), onTree: true, regrow: 0, vel: new THREE.Vector3(), falling: false, lie: 0, growT: 1 });
        this.nutTargets.push(nut);
      }
      g.add(crown);
      g.position.set(x, baseHeight(x, z) - 0.1, z);
      g.rotation.y = rand() * Math.PI * 2;
      this.group.add(g);
      this.palms.push({ group: g, crown, phase: rand() * 6, x, z, rings, nuts: nutGroup, crownA, crownB, lod: 0, k: 1, kf: 1 });
      this.trunks.push({ x, z });
    }
  }

  // ------------------------------------------------------------ mountains

  private peak(radius: number, height: number, rand: () => number, mat: THREE.Material): THREE.Mesh {
    const geo = new THREE.ConeGeometry(radius, height, 9, 4);
    const p = geo.attributes.position as THREE.BufferAttribute;
    // the cone repeats its seam vertices, so each position gets one shared roughness value;
    // otherwise the two edges of the seam drift apart and leave a crack in the mountain
    const rough = new Map<string, [number, number, number]>();
    for (let i = 0; i < p.count; i++) {
      const x = p.getX(i);
      const y = p.getY(i);
      const z = p.getZ(i);
      if (y < height / 2 - 0.01 && y > -height / 2 + 0.01) {
        // rounded to whole millimetres so tiny float differences (and -0 vs 0) still match
        const key = `${Math.round(x * 1000)},${Math.round(y * 1000)},${Math.round(z * 1000)}`;
        let r = rough.get(key);
        if (!r) {
          r = [0.8 + rand() * 0.4, 0.8 + rand() * 0.4, (rand() - 0.5) * height * 0.08];
          rough.set(key, r);
        }
        p.setX(i, x * r[0]);
        p.setZ(i, z * r[1]);
        p.setY(i, y + r[2]);
      }
    }
    geo.computeVertexNormals();
    return new THREE.Mesh(geo, mat);
  }

  private buildMountains(): void {
    const rand = rng(21);
    const rows: { dist: number; haze: number; size: number }[] = [
      { dist: 265, haze: 0.55, size: 1 },
      { dist: 235, haze: 0.35, size: 0.8 },
    ];
    for (const row of rows) {
      const mat = new THREE.MeshBasicMaterial({ color: 0x6f8fa6, fog: false });
      this.mountainMats.push({ mat, haze: row.haze });
      // a range behind the dunes, curving round to both sides (none out over the sea)
      for (let a = -95; a <= 95; a += 9) {
        const ang = (a * Math.PI) / 180;
        const r = row.dist + rand() * 20;
        const x = Math.sin(ang) * r;
        const z = -Math.cos(ang) * r;
        const radius = (45 + rand() * 45) * row.size;
        const height = (45 + rand() * 70) * row.size * (Math.abs(a) > 70 ? 0.6 : 1);
        const m = this.peak(radius, height, rand, mat);
        m.position.set(x, height / 2 - 6, z);
        m.rotation.y = rand() * 6;
        this.group.add(m);
      }
    }
  }

  /**
   * Distant ranges keep a steady blue-grey and only dim with the light, with just a hint of the
   * sky's haze, so they do not swing in colour as the sun moves.
   */
  /** Coconuts still hanging on the trees. */
  targets(): THREE.Object3D[] {
    // coconuts on palms that are drawn without them (far away) cannot be hit either
    return this.nutTargets.filter((n) => n.parent?.visible !== false);
  }

  /** A coconut was shot: it drops from the crown. Returns where it was, or null if it is not one of ours. */
  shootNut(obj: THREE.Object3D): THREE.Vector3 | null {
    const n = this.nuts.find((x) => x.mesh === obj && x.onTree);
    if (!n) return null;
    const at = n.mesh.getWorldPosition(new THREE.Vector3());
    n.mesh.parent?.remove(n.mesh);
    n.mesh.position.copy(at);
    this.group.add(n.mesh);
    n.onTree = false;
    n.falling = true;
    n.lie = 7;
    n.regrow = 120;
    n.vel.set((Math.random() - 0.5) * 1.2, 1.2, (Math.random() - 0.5) * 1.2);
    n.mesh.userData.coconut = false;
    const i = this.nutTargets.indexOf(obj);
    if (i >= 0) this.nutTargets.splice(i, 1);
    return at;
  }

  private updateNuts(dt: number): void {
    for (const n of this.nuts) {
      if (n.onTree) {
        // a coconut that has just grown back swells out gently
        if (n.growT < 1) {
          n.growT = Math.min(1, n.growT + dt / 2);
          const e = n.growT * n.growT * (3 - 2 * n.growT);
          n.mesh.scale.setScalar(Math.max(0.01, e));
        }
        continue;
      }
      if (n.falling) {
        n.vel.y -= 9.8 * dt;
        n.mesh.position.addScaledVector(n.vel, dt);
        const ground = baseHeight(n.mesh.position.x, n.mesh.position.z) + 0.17;
        if (n.mesh.position.y <= ground) {
          n.mesh.position.y = ground;
          if (n.vel.y < -2) {
            n.vel.y *= -0.3;
            n.vel.x *= 0.5;
            n.vel.z *= 0.5;
          } else {
            n.falling = false;
          }
        }
      } else if (n.mesh.visible) {
        // after a while on the sand it shrinks away rather than vanishing
        n.lie -= dt;
        if (n.lie <= 0) {
          const k = Math.max(0, 1 + n.lie / 1.0);
          n.mesh.scale.setScalar(Math.max(0.001, k));
          if (k <= 0) n.mesh.visible = false;
        }
      }
      n.regrow -= dt;
      if (n.regrow <= 0) {
        this.group.remove(n.mesh);
        n.mesh.position.copy(n.home);
        n.mesh.visible = true;
        n.growT = 0;
        n.mesh.scale.setScalar(0.01);
        (n.crown.children[0] as THREE.Group).add(n.mesh); // back into the palm's coconut group, which the distance detail switches on and off
        n.mesh.userData.coconut = true;
        n.onTree = true;
        n.falling = false;
        this.nutTargets.push(n.mesh);
      }
    }
  }

  update(t: number, fog: THREE.Color, dt = 0, camX = 0, camZ = 0): void {
    this.updateNuts(dt);
    const lum = Math.min(1, 0.2126 * fog.r + 0.7152 * fog.g + 0.0722 * fog.b);
    const light = 0.32 + 0.68 * Math.min(1, lum / 0.75);
    for (const { mat, haze } of this.mountainMats) {
      this.tmp.copy(this.tint).lerp(fog, haze * 0.22).multiplyScalar(light);
      mat.color.lerp(this.tmp, 0.04);
    }
    for (const p of this.palms) {
      // fewer parts the further away it is, with a few metres of give so it does not flicker at the edge
      const d = Math.hypot(p.x - camX, p.z - camZ);
      const want = d > LOD_FAR + (p.lod === 2 ? -4 : 4) ? 2 : d > LOD_MID + (p.lod >= 1 ? -4 : 4) ? 1 : 0;
      p.lod = want;
      // ease toward the wanted amount of detail, so parts grow in and shrink away instead of popping
      const wantK = want === 0 ? 1 : 0;
      const wantF = want < 2 ? 1 : 0;
      if (p.k !== wantK || p.kf !== wantF) {
        const a = dt > 0 ? Math.min(1, dt * 4) : 1;
        p.k += (wantK - p.k) * a;
        p.kf += (wantF - p.kf) * a;
        if (Math.abs(wantK - p.k) < 0.01) p.k = wantK;
        if (Math.abs(wantF - p.kf) < 0.01) p.kf = wantF;
        const k = Math.max(p.k, 0.001);
        p.rings.visible = p.k > 0.01;
        p.nuts.visible = p.k > 0.01;
        p.nuts.scale.setScalar(k);
        p.crownB.visible = p.kf > 0.01;
        p.crownB.scale.setScalar(Math.max(p.kf, 0.001));
      }
      p.crown.rotation.z = Math.sin(t * 0.8 + p.phase) * 0.03;
      p.crown.rotation.x = Math.cos(t * 0.6 + p.phase) * 0.025;
    }
  }
}
