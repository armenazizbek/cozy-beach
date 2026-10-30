import * as THREE from 'three';
import { ParticleSystem } from '../core/Particles';
import { SEA, SHORE_Z } from './Beach';

const rand = (a: number, b: number): number => a + Math.random() * (b - a);

const SHARK_LEN = 4.4;

// swimming area: out beyond the surf, in front of the beach
const X_MAX = SEA.x;
const Z_MIN = SHORE_Z + 10;
const Z_MAX = SEA.z;

interface Wanderer {
  x: number;
  z: number;
  heading: number;
  speed: number;
  phase: number;
}

interface Shark extends Wanderer {
  root: THREE.Group;
  body: THREE.Mesh;
  /** untouched vertex positions, which the swimming wave is applied to */
  rest: Float32Array;
  caudal: THREE.Group;
  hit: THREE.Mesh;
  alive: boolean;
  respawn: number;
  wake: number;
  /** 0 while cruising; 0..1 through a leap out of the water */
  breach: number;
  breachFrom: THREE.Vector2;
  /** hits it can still take; two are needed to bring a shark down */
  hp: number;
  /** seconds left of darting away after being hit */
  flinch: number;
  baseSpeed: number;
}

interface School extends Wanderer {
  fish: { mesh: THREE.Group; tail: THREE.Mesh; ox: number; oz: number; ph: number; hit: THREE.Mesh; alive: boolean; respawn: number }[];
}

interface Jumper {
  mesh: THREE.Group;
  active: boolean;
  t: number;
  sx: number;
  sz: number;
  dx: number;
  dz: number;
  yaw: number;
}

/** Sharks cutting through the water and schools of fish, some of which leap out of the sea. */
export class SeaLife {
  readonly group = new THREE.Group();
  readonly sharks: Shark[] = [];
  private schools: School[] = [];
  private jumpers: Jumper[] = [];
  private splashes = new ParticleSystem(260, 0.14, 7, false);
  private wakeParticles = new ParticleSystem(300, 0.13, 0, false);
  private jumpTimer = 3;
  private white = new THREE.Color(0xeaf7ff);
  private fishGeo = new THREE.SphereGeometry(1, 8, 6);
  private tailGeo = new THREE.ConeGeometry(0.07, 0.16, 4);
  private sharkBody: THREE.BufferGeometry;
  private caudal: THREE.BufferGeometry;
  private pectoral: THREE.BufferGeometry;
  private breachTimer = 40;

  constructor() {
    const fin = new THREE.MeshStandardMaterial({ color: 0x586a7a, roughness: 0.45, metalness: 0.08, side: THREE.DoubleSide });
    const finGeo = this.finGeometry();
    this.sharkBody = this.bodyGeometry();
    this.caudal = this.caudalGeometry();
    this.pectoral = this.pectoralGeometry();
    // a bigger sea, so a few more of everything
    for (let i = 0; i < 6; i++) this.sharks.push(this.makeShark(finGeo, fin));
    for (let i = 0; i < 12; i++) this.schools.push(this.makeSchool());
    for (let i = 0; i < 6; i++) this.jumpers.push(this.makeJumper());
    this.group.add(this.splashes.points, this.wakeParticles.points);
  }

  private finGeometry(): THREE.BufferGeometry {
    const sh = new THREE.Shape();
    sh.moveTo(0.45, 0);
    sh.quadraticCurveTo(0.3, 0.5, -0.2, 1.05);
    sh.quadraticCurveTo(-0.15, 0.5, -0.5, 0);
    sh.closePath();
    const g = new THREE.ExtrudeGeometry(sh, { depth: 0.07, bevelEnabled: true, bevelSize: 0.015, bevelThickness: 0.015, bevelSegments: 1 });
    g.translate(0, 0, -0.035);
    g.rotateY(Math.PI / 2); // the fin's front now points along -z
    return g;
  }

  /** A torpedo-shaped body along z (nose at -z), grey on top and pale underneath. */
  private bodyGeometry(): THREE.BufferGeometry {
    const L = SHARK_LEN;
    const profile = [
      [0.0, 0.03], [0.1, 0.1], [0.22, 0.2], [0.4, 0.34], [0.58, 0.42], [0.74, 0.4], [0.86, 0.31], [0.94, 0.2], [0.985, 0.08], [1.0, 0.0],
    ];
    const pts: THREE.Vector2[] = [];
    // smooth the profile with a Catmull-Rom curve
    const curve = new THREE.SplineCurve(profile.map(([u, r]) => new THREE.Vector2(u * L - L / 2, r)));
    for (const pt of curve.getPoints(44)) pts.push(new THREE.Vector2(Math.max(0, pt.y), pt.x));
    const g = new THREE.LatheGeometry(pts, 22);
    g.rotateX(-Math.PI / 2); // lathe axis y -> z, nose (+y) -> -z
    g.scale(1.3, 0.95, 1);
    g.computeVertexNormals();
    const pos = g.attributes.position as THREE.BufferAttribute;
    const nrm = g.attributes.normal as THREE.BufferAttribute;
    const col = new Float32Array(pos.count * 3);
    const top = new THREE.Color(0x56697a);
    const belly = new THREE.Color(0xe9eff1);
    const c = new THREE.Color();
    for (let i = 0; i < pos.count; i++) {
      const t = Math.min(1, Math.max(0, (nrm.getY(i) + 0.15) / 0.5));
      const k = t * t * (3 - 2 * t);
      c.copy(belly).lerp(top, k);
      col.set([c.r, c.g, c.b], i * 3);
    }
    g.setAttribute('color', new THREE.BufferAttribute(col, 3));
    return g;
  }

  private caudalGeometry(): THREE.BufferGeometry {
    const sh = new THREE.Shape();
    sh.moveTo(0, 0.1);
    sh.lineTo(0.3, 0.95);
    sh.quadraticCurveTo(0.62, 1.18, 0.7, 1.2);
    sh.quadraticCurveTo(0.45, 0.5, 0.42, 0.05);
    sh.quadraticCurveTo(0.5, -0.4, 0.34, -0.72);
    sh.lineTo(0.12, -0.2);
    sh.closePath();
    const g = new THREE.ExtrudeGeometry(sh, { depth: 0.05, bevelEnabled: false });
    g.translate(0, 0, -0.025);
    g.rotateY(-Math.PI / 2); // sweeps backwards (+z)
    return g;
  }

  private pectoralGeometry(): THREE.BufferGeometry {
    const sh = new THREE.Shape();
    sh.moveTo(0, 0.28);
    sh.quadraticCurveTo(0.55, 0.1, 1.05, -0.5);
    sh.lineTo(0.2, -0.22);
    sh.closePath();
    const g = new THREE.ExtrudeGeometry(sh, { depth: 0.035, bevelEnabled: false });
    g.rotateX(-Math.PI / 2);
    return g;
  }

  private makeShark(finGeo: THREE.BufferGeometry, finMat: THREE.Material): Shark {
    const root = new THREE.Group();
    const bodyMat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.42, metalness: 0.05 });
    const bodyGeo = this.sharkBody.clone();
    const body = new THREE.Mesh(bodyGeo, bodyMat);
    body.castShadow = true;
    const rest = new Float32Array((bodyGeo.attributes.position as THREE.BufferAttribute).array);

    const dorsal = new THREE.Mesh(finGeo, finMat);
    dorsal.position.set(0, 0.36, -0.15);
    dorsal.castShadow = true;
    const dorsal2 = new THREE.Mesh(finGeo, finMat);
    dorsal2.scale.setScalar(0.28);
    dorsal2.position.set(0, 0.2, 1.15);

    const pec = this.pectoral;
    for (const side of [-1, 1]) {
      const f = new THREE.Mesh(pec, finMat);
      f.material = finMat;
      f.position.set(side * 0.3, -0.16, -0.65);
      f.rotation.z = side * 0.55;
      f.scale.x = side;
      root.add(f);
    }
    const caudal = new THREE.Group();
    const lobe = new THREE.Mesh(this.caudal, finMat);
    lobe.castShadow = true;
    caudal.add(lobe);
    caudal.position.set(0, 0.0, SHARK_LEN / 2 - 0.05);

    const dark = new THREE.MeshStandardMaterial({ color: 0x14181c, roughness: 0.3 });
    for (const side of [-1, 1]) {
      const eye = new THREE.Mesh(new THREE.SphereGeometry(0.045, 8, 6), dark);
      eye.position.set(side * 0.2, 0.08, -1.62);
      root.add(eye);
      for (let i = 0; i < 4; i++) {
        const slit = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.2, 0.03), dark);
        slit.position.set(side * 0.395, 0.04, -1.0 + i * 0.1);
        slit.rotation.z = side * 0.2;
        root.add(slit);
      }
    }
    const mouth = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.012, 0.02), dark);
    mouth.position.set(0, -0.1, -1.92);
    root.add(mouth);

    const hit = new THREE.Mesh(new THREE.SphereGeometry(1, 8, 6), new THREE.MeshBasicMaterial({ visible: false }));
    hit.position.set(0, 0.4, 0);
    hit.userData.hitRadius = 2.2;
    root.add(body, dorsal, dorsal2, caudal, hit);
    root.scale.setScalar(1.3);
    this.group.add(root);
    const s: Shark = {
      root, body, rest, caudal, hit, alive: true, respawn: 0, wake: 0, breach: 0, breachFrom: new THREE.Vector2(), hp: 2, flinch: 0, baseSpeed: 3,
      x: rand(-X_MAX, X_MAX), z: rand(Z_MIN + 10, Z_MAX), heading: Math.random() * 6.28, speed: rand(2.4, 3.6), phase: Math.random() * 6,
    };
    s.baseSpeed = s.speed;
    hit.userData.shark = s;
    return s;
  }

  private makeSchool(): School {
    const silver = Math.random() < 0.5;
    const bodyMat = new THREE.MeshStandardMaterial({ color: silver ? 0xcfd8dc : 0xff8a3c, roughness: 0.35, metalness: silver ? 0.5 : 0.1 });
    const fish: School['fish'] = [];
    const n = 8 + Math.floor(Math.random() * 5);
    for (let i = 0; i < n; i++) {
      const g = new THREE.Group();
      const body = new THREE.Mesh(this.fishGeo, bodyMat);
      body.scale.set(0.09, 0.07, 0.24);
      const tail = new THREE.Mesh(this.tailGeo, bodyMat);
      tail.rotation.x = -Math.PI / 2;
      tail.position.z = 0.28;
      g.add(body, tail);
      g.scale.setScalar(rand(0.9, 1.4));
      const hit = new THREE.Mesh(new THREE.SphereGeometry(1, 6, 4), new THREE.MeshBasicMaterial({ visible: false }));
      hit.scale.setScalar(0.28);
      hit.userData.hitRadius = 0.55;
      g.add(hit);
      this.group.add(g);
      const f = { mesh: g, tail, ox: rand(-1.6, 1.6), oz: rand(-1.6, 1.6), ph: Math.random() * 6, hit, alive: true, respawn: 0 };
      hit.userData.fish = f;
      fish.push(f);
    }
    return {
      x: rand(-X_MAX, X_MAX), z: rand(SHORE_Z + 6, Z_MAX - 20), heading: Math.random() * 6.28, speed: rand(1.2, 2.2), phase: Math.random() * 6, fish,
    };
  }

  private makeJumper(): Jumper {
    const g = new THREE.Group();
    const mat = new THREE.MeshStandardMaterial({ color: 0xd8e4ea, roughness: 0.3, metalness: 0.6 });
    const body = new THREE.Mesh(this.fishGeo, mat);
    body.scale.set(0.1, 0.08, 0.28);
    const tail = new THREE.Mesh(this.tailGeo, mat);
    tail.rotation.x = -Math.PI / 2;
    tail.position.z = 0.32;
    g.add(body, tail);
    g.scale.setScalar(1.6);
    g.visible = false;
    this.group.add(g);
    return { mesh: g, active: false, t: 0, sx: 0, sz: 0, dx: 0, dz: 0, yaw: 0 };
  }

  /** Gently wandering steering that also turns the swimmer back from the edges of its patch. */
  private steer(w: Wanderer, dt: number, t: number, turnRate: number): void {
    w.heading += Math.sin(t * 0.35 + w.phase) * turnRate * dt;
    let tx = 0;
    let tz = 0;
    if (w.x > X_MAX) tx = -1;
    else if (w.x < -X_MAX) tx = 1;
    if (w.z > Z_MAX) tz = -1;
    else if (w.z < Z_MIN) tz = 1;
    if (tx || tz) {
      const want = Math.atan2(-tx, -tz);
      let d = want - w.heading;
      d = Math.atan2(Math.sin(d), Math.cos(d));
      w.heading += d * Math.min(1, dt * 1.4);
    }
    w.x += -Math.sin(w.heading) * w.speed * dt;
    w.z += -Math.cos(w.heading) * w.speed * dt;
  }

  /** Hit spheres of the sharks and fish that are currently in the water. */
  targets(): THREE.Object3D[] {
    const out: THREE.Object3D[] = this.sharks.filter((s) => s.alive).map((s) => s.hit);
    for (const sc of this.schools) for (const f of sc.fish) if (f.alive && f.mesh.visible) out.push(f.hit);
    return out;
  }

  /**
   * Registers a hit on a shark or a fish. A fish is caught at once. A shark takes two hits:
   * the first makes it bolt, the second sends it diving away for good measure.
   */
  hit(obj: THREE.Object3D): { kind: 'shark' | 'fish'; killed: boolean; at: THREE.Vector3 } | null {
    const s = obj.userData.shark as Shark | undefined;
    if (s) {
      if (!s.alive) return null;
      const at = new THREE.Vector3(s.x, s.root.position.y, s.z);
      s.hp--;
      if (s.hp > 0) {
        s.flinch = 3;
        s.speed = s.baseSpeed * 2.4;
        this.splash(at, 26);
        return { kind: 'shark', killed: false, at };
      }
      s.alive = false;
      s.breach = 0;
      s.respawn = rand(30, 50);
      s.hp = 2;
      s.flinch = 0;
      s.speed = s.baseSpeed;
      s.root.visible = false;
      this.splash(at, 50);
      return { kind: 'shark', killed: true, at };
    }
    const f = obj.userData.fish as School['fish'][number] | undefined;
    if (f && f.alive) {
      f.alive = false;
      f.respawn = rand(18, 30);
      f.mesh.visible = false;
      const at = f.mesh.getWorldPosition(new THREE.Vector3());
      this.splash(at, 12);
      return { kind: 'fish', killed: true, at };
    }
    return null;
  }

  splash(at: THREE.Vector3, count = 22): void {
    this.splashes.burst(at, count, 1.6, 4.2, 1.1, this.white);
  }

  /** `level` is the current surface height; onSplash(distance) lets the sound be placed. */
  update(dt: number, t: number, level: number, px: number, pz: number, onSplash: (distance: number) => void): void {
    // now and then one of the sharks launches itself out of the water
    this.breachTimer -= dt;
    if (this.breachTimer <= 0) {
      this.breachTimer = rand(35, 80);
      const ready = this.sharks.filter((x) => x.alive && x.breach === 0);
      const pick = ready[Math.floor(Math.random() * ready.length)];
      if (pick) {
        pick.breach = 0.0001;
        pick.breachFrom.set(pick.x, pick.z);
        this.splash(new THREE.Vector3(pick.x, level, pick.z), 45);
        onSplash(Math.hypot(pick.x - px, pick.z - pz) * 0.5);
      }
    }

    for (const s of this.sharks) {
      if (!s.alive) {
        s.respawn -= dt;
        if (s.respawn <= 0) {
          s.alive = true;
          s.root.visible = true;
          s.x = rand(-X_MAX, X_MAX);
          s.z = rand(Z_MIN + 10, Z_MAX);
        }
        continue;
      }
      if (s.flinch > 0) {
        s.flinch -= dt;
        if (s.flinch <= 0) s.speed = s.baseSpeed;
      }
      const leaping = s.breach > 0;
      if (leaping) {
        s.breach += dt / 1.9;
        if (s.breach >= 1) {
          s.breach = 0;
          this.splash(new THREE.Vector3(s.x, level, s.z), 55);
          onSplash(Math.hypot(s.x - px, s.z - pz) * 0.5);
        }
      }
      this.steer(s, dt, t, leaping ? 0.05 : 0.55);
      const u = s.breach;
      const arc = leaping ? 4 * 2.6 * u * (1 - u) : 0;
      s.root.position.set(s.x, level - 0.22 + arc, s.z);
      // nose up as it leaves the water, nose down as it returns
      s.root.rotation.set(leaping ? (0.5 - u) * 2.2 : Math.sin(t * 1.1 + s.phase) * 0.02, s.heading, leaping ? Math.sin(u * Math.PI * 2) * 0.35 : 0, 'YXZ');

      // swimming: a wave runs down the body, strongest at the tail
      const pos = s.body.geometry.attributes.position as THREE.BufferAttribute;
      const amp = leaping ? 0.28 : 0.12;
      const w = t * (leaping ? 7 : 3.6) + s.phase;
      for (let i = 0; i < pos.count; i++) {
        const z = s.rest[i * 3 + 2];
        const k = (z + SHARK_LEN / 2) / SHARK_LEN; // 0 at the nose, 1 at the tail
        pos.setX(i, s.rest[i * 3] + Math.sin(w - k * 3.4) * amp * k * k);
      }
      pos.needsUpdate = true;
      s.body.geometry.computeVertexNormals();
      const tailOff = Math.sin(w - 3.4) * amp;
      s.caudal.position.x = tailOff;
      s.caudal.rotation.y = Math.cos(w - 3.4) * amp * 1.6;

      // bow wave and a foamy wake behind the fin
      s.wake -= dt;
      if (s.wake <= 0 && !leaping) {
        s.wake = 0.06;
        const fx = -Math.sin(s.heading);
        const fz = -Math.cos(s.heading);
        const rx = Math.cos(s.heading);
        const rz = -Math.sin(s.heading);
        for (const side of [-1, 1]) {
          const v = new THREE.Vector3(rx * side * 0.35, 0.05, rz * side * 0.35);
          this.wakeParticles.emit(new THREE.Vector3(s.x - fx * 2.2 + rx * side * 0.3, level + 0.04, s.z - fz * 2.2 + rz * side * 0.3), v, 1.4, this.white);
          this.wakeParticles.emit(new THREE.Vector3(s.x + fx * 2.3 + rx * side * 0.25, level + 0.05, s.z + fz * 2.3 + rz * side * 0.25), v.multiplyScalar(1.8), 0.8, this.white);
        }
      }
    }

    for (const sc of this.schools) {
      this.steer(sc, dt, t, 0.9);
      const cs = Math.cos(sc.heading);
      const sn = Math.sin(sc.heading);
      for (const f of sc.fish) {
        if (!f.alive) {
          f.respawn -= dt;
          if (f.respawn <= 0) {
            f.alive = true;
            f.mesh.visible = true;
          }
          continue;
        }
        // a school far across the water is not drawn, and does not need animating either
        const far = Math.hypot(sc.x - px, sc.z - pz) > 130;
        f.mesh.visible = !far;
        if (far) continue;
        const ox = f.ox + Math.sin(t * 1.3 + f.ph) * 0.35;
        const oz = f.oz + Math.cos(t * 1.1 + f.ph) * 0.35;
        f.mesh.position.set(sc.x + cs * ox + sn * oz, level + 0.02 + Math.sin(t * 5 + f.ph) * 0.02, sc.z - sn * ox + cs * oz);
        f.mesh.rotation.y = sc.heading + Math.sin(t * 6 + f.ph) * 0.12;
        f.tail.rotation.y = Math.sin(t * 14 + f.ph) * 0.7;
      }
    }

    // now and then a fish leaps clear of the water
    this.jumpTimer -= dt;
    if (this.jumpTimer <= 0) {
      this.jumpTimer = rand(2.5, 6);
      const free = this.jumpers.find((j) => !j.active);
      const school = this.schools[Math.floor(Math.random() * this.schools.length)];
      if (free && school) {
        free.active = true;
        free.t = 0;
        free.sx = school.x + rand(-1, 1);
        free.sz = school.z + rand(-1, 1);
        free.yaw = school.heading + rand(-0.5, 0.5);
        free.dx = -Math.sin(free.yaw) * 3;
        free.dz = -Math.cos(free.yaw) * 3;
        free.mesh.visible = true;
        this.splash(new THREE.Vector3(free.sx, level, free.sz), 10);
        onSplash(Math.hypot(free.sx - px, free.sz - pz));
      }
    }
    for (const j of this.jumpers) {
      if (!j.active) continue;
      j.t += dt / 0.95;
      const u = j.t;
      if (u >= 1) {
        j.active = false;
        j.mesh.visible = false;
        const x = j.sx + j.dx;
        const z = j.sz + j.dz;
        this.splash(new THREE.Vector3(x, level, z), 12);
        onSplash(Math.hypot(x - px, z - pz));
        continue;
      }
      const y = level + 4 * 1.3 * u * (1 - u);
      j.mesh.position.set(j.sx + j.dx * u, y, j.sz + j.dz * u);
      // nose up on the way out, down on the way back in, with a little twist
      j.mesh.rotation.set((0.5 - u) * 1.8, j.yaw, u * 9, 'YXZ');
    }

    this.splashes.update(dt);
    this.wakeParticles.update(dt);
  }
}
