import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { UW } from './Underwater';

type Floor = (x: number, z: number) => number;

const rnd = (a: number, b: number): number => a + Math.random() * (b - a);

/** Paints a whole piece one colour, so differently coloured pieces can be merged into a single mesh. */
const solid = (g: THREE.BufferGeometry, color: number): THREE.BufferGeometry => {
  g.deleteAttribute('uv');
  const c = new THREE.Color(color);
  const n = (g.attributes.position as THREE.BufferAttribute).count;
  const col = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) col.set([c.r, c.g, c.b], i * 3);
  g.setAttribute('color', new THREE.BufferAttribute(col, 3));
  return g;
};

const vcol = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.6, side: THREE.DoubleSide });

/** A swimmer or crawler the harpoon can hit. */
export interface Animal {
  kind: 'octopus' | 'seahorse' | 'snake' | 'crab' | 'turtle';
  label: string;
  reward: number;
  /** how close the spear has to pass to one of the points */
  r: number;
  /** the places on the body that count as hit, in world space */
  pts: THREE.Vector3[];
  alive: boolean;
  respawn: number;
  /** seconds a catch stays gone */
  gone: number;
  root: THREE.Object3D;
  x: number;
  y: number;
  z: number;
  heading: number;
  /** moves and animates it */
  tick: (dt: number, t: number, cam: THREE.Vector3) => void;
  /** puts it somewhere new in the water */
  place: () => void;
}

/** Octopuses, seahorses, sea snakes and crabs living on the sea floor. */
export class SeaCreatures {
  readonly group = new THREE.Group();
  readonly animals: Animal[] = [];

  constructor(private floor: Floor) {
    for (let i = 0; i < 3; i++) this.animals.push(this.octopus());
    for (let i = 0; i < 8; i++) this.animals.push(this.seahorse());
    for (let i = 0; i < 4; i++) this.animals.push(this.snake());
    for (let i = 0; i < 14; i++) this.animals.push(this.crab());
    for (let i = 0; i < 3; i++) this.animals.push(this.turtle());
    for (const a of this.animals) {
      a.place();
      this.group.add(a.root);
    }
  }

  update(dt: number, t: number, cam: THREE.Vector3): void {
    for (const a of this.animals) {
      if (!a.alive) {
        a.respawn -= dt;
        if (a.respawn <= 0) {
          a.alive = true;
          a.root.visible = true;
          a.place();
        }
        continue;
      }
      a.tick(dt, t, cam);
    }
  }

  /** Called when the harpoon hits one. */
  catchAnimal(a: Animal): void {
    a.alive = false;
    a.root.visible = false;
    a.respawn = a.gone * (0.8 + Math.random() * 0.5);
  }

  private spot(a: Animal, lift: [number, number]): void {
    a.x = UW.x + (Math.random() - 0.5) * UW.half * 1.6;
    a.z = UW.z + (Math.random() - 0.3) * UW.half * 1.4;
    const f = this.floor(a.x, a.z);
    a.y = f + rnd(lift[0], lift[1]);
    a.heading = Math.random() * Math.PI * 2;
  }

  /** Turns the animal back in when it nears the edge of the water. */
  private keepIn(a: Animal, dt: number): void {
    const lx = a.x - UW.x;
    const lz = a.z - UW.z;
    if (Math.abs(lx) > UW.half - 5 || Math.abs(lz) > UW.half - 5) {
      let d = Math.atan2(-lx, -lz) - a.heading;
      d = Math.atan2(Math.sin(d), Math.cos(d));
      a.heading += d * Math.min(1, dt * 2.5);
    }
  }

  /** Swings the heading away from the diver when they are close. Returns true while it is fleeing. */
  private flee(a: Animal, cam: THREE.Vector3, dt: number, near: number): boolean {
    const dx = cam.x - a.x;
    const dz = cam.z - a.z;
    if (Math.hypot(dx, dz, cam.y - a.y) > near) return false;
    let d = Math.atan2(-dx, -dz) - a.heading;
    d = Math.atan2(Math.sin(d), Math.cos(d));
    a.heading += d * Math.min(1, dt * 5);
    return true;
  }

  // ---------------------------------------------------------------- octopus

  private octopus(): Animal {
    const root = new THREE.Group();
    const skin = 0xc4533a;
    const pale = 0xeaa88e;
    const white = new THREE.MeshStandardMaterial({ color: 0xf2ead6, roughness: 0.3 });
    const iris = new THREE.MeshStandardMaterial({ color: 0xe0a02a, roughness: 0.3 });
    const dark = new THREE.MeshBasicMaterial({ color: 0x14100e });

    // head and mantle are one smooth shape: darker on top, lighter underneath, with freckles
    const bodyParts: THREE.BufferGeometry[] = [];
    const head = new THREE.SphereGeometry(0.27, 22, 16);
    head.scale(1, 1.05, 1.1);
    head.translate(0, 0.46, 0.04);
    const mantle = new THREE.SphereGeometry(0.28, 22, 16);
    mantle.scale(0.95, 1.45, 1);
    mantle.rotateX(0.5);
    mantle.translate(0, 0.78, -0.16);
    for (const g of [head, mantle]) {
      g.deleteAttribute('uv');
      const p = g.attributes.position as THREE.BufferAttribute;
      const col = new Float32Array(p.count * 3);
      const c = new THREE.Color();
      for (let i = 0; i < p.count; i++) {
        const k = Math.min(1, Math.max(0, (p.getY(i) - 0.2) / 0.9));
        const fleck = Math.sin(p.getX(i) * 38) * Math.sin(p.getZ(i) * 34 + p.getY(i) * 20);
        c.set(0xd9785a).lerp(new THREE.Color(0x96402c), k).lerp(new THREE.Color(0xf0b49a), Math.max(0, fleck - 0.55) * 1.2);
        col.set([c.r, c.g, c.b], i * 3);
      }
      g.setAttribute('color', new THREE.BufferAttribute(col, 3));
      bodyParts.push(g);
    }
    // a bulge over each eye
    for (const sx of [-1, 1]) {
      const brow = solid(new THREE.SphereGeometry(0.075, 10, 8), 0xb04a32);
      brow.scale(1.2, 0.8, 1);
      brow.translate(sx * 0.19, 0.56, 0.18);
      bodyParts.push(brow);
    }
    const body = new THREE.Mesh(mergeGeometries(bodyParts, false)!, vcol);
    const model = new THREE.Group();
    model.add(body);
    for (const sx of [-1, 1]) {
      const eye = new THREE.Mesh(new THREE.SphereGeometry(0.06, 12, 10), white);
      eye.position.set(sx * 0.2, 0.52, 0.2);
      const ir = new THREE.Mesh(new THREE.SphereGeometry(0.04, 10, 8), iris);
      ir.position.set(sx * 0.225, 0.52, 0.235);
      const pupil = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.012, 0.014), dark);
      pupil.position.set(sx * 0.235, 0.52, 0.265);
      pupil.rotation.y = sx * 0.6;
      model.add(eye, ir, pupil);
    }
    // the siphon
    const siphon = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.04, 0.12, 8), new THREE.MeshStandardMaterial({ color: 0xb04a32, roughness: 0.6 }));
    siphon.position.set(0.14, 0.36, 0.25);
    siphon.rotation.x = Math.PI / 2 - 0.3;
    model.add(siphon);

    // each arm is one long tapering curve that curls at the tip, with a row of little suckers underneath
    const armGeo = (len: number): THREE.BufferGeometry => {
      const path = new THREE.CatmullRomCurve3([
        [0, 0.02, 0.0], [0, -0.1, 0.2], [0, -0.17, 0.5], [0, -0.17, 0.78], [0, -0.11, 1.0], [0, -0.02, 1.12], [0, 0.07, 1.12], [0, 0.12, 1.04],
      ].map(([x, y, z]) => new THREE.Vector3(x, y, z * len)));
      const tube = new THREE.TubeGeometry(path, 40, 0.07, 8);
      const tp = tube.attributes.position as THREE.BufferAttribute;
      const ring = 9;
      for (let i = 0; i < tp.count; i++) {
        const t = Math.floor(i / ring) / 40;
        const c = path.getPoint(Math.min(1, t));
        const k = Math.max(0.12, 1 - t * 0.92);
        tp.setXYZ(i, c.x + (tp.getX(i) - c.x) * k, c.y + (tp.getY(i) - c.y) * k, c.z + (tp.getZ(i) - c.z) * k);
      }
      tube.computeVertexNormals();
      const parts: THREE.BufferGeometry[] = [solid(tube, skin)];
      for (let i = 0; i < 12; i++) {
        const t = 0.12 + i * 0.065;
        const c = path.getPoint(t);
        const sk = solid(new THREE.SphereGeometry(0.03 * (1 - t * 0.7), 6, 5), pale);
        sk.translate(c.x, c.y - 0.07 * (1 - t * 0.9), c.z);
        parts.push(sk);
      }
      return mergeGeometries(parts, false)!;
    };
    const arms: { pivot: THREE.Group; ph: number; ang: number }[] = [];
    for (let i = 0; i < 8; i++) {
      const ang = (i / 8) * Math.PI * 2 + 0.2;
      const pivot = new THREE.Group();
      pivot.position.set(Math.sin(ang) * 0.17, 0.26, Math.cos(ang) * 0.17);
      pivot.rotation.y = ang;
      pivot.add(new THREE.Mesh(armGeo(rnd(0.7, 0.95)), vcol));
      model.add(pivot);
      arms.push({ pivot, ph: Math.random() * 6, ang });
    }
    root.add(model);
    root.scale.setScalar(rnd(0.9, 1.2));
    let spread = 1;
    const a: Animal = {
      kind: 'octopus', label: 'octopus', reward: 5, r: 0.5, gone: 90, alive: true, respawn: 0, root,
      pts: [new THREE.Vector3(), new THREE.Vector3()], x: 0, y: 0, z: 0, heading: 0,
      place: () => this.spot(a, [0.15, 0.4]),
      tick: (dt, t, cam) => {
        const fleeing = this.flee(a, cam, dt, 3.2);
        const speed = fleeing ? 2.4 : 0.35;
        a.heading += Math.sin(t * 0.3 + a.x) * dt * 0.3;
        this.keepIn(a, dt);
        a.x += Math.sin(a.heading) * speed * dt;
        a.z += Math.cos(a.heading) * speed * dt;
        const f = this.floor(a.x, a.z);
        const want = f + (fleeing ? 1.4 : 0.1) + Math.sin(t * 0.9 + a.z) * 0.08;
        a.y += (want - a.y) * Math.min(1, dt * 2.2);
        root.position.set(a.x, a.y, a.z);
        root.rotation.y = a.heading;
        // the body pulses gently; when fleeing it tilts forward and the arms stream out behind
        const pulse = 1 + Math.sin(t * (fleeing ? 7 : 1.3)) * (fleeing ? 0.06 : 0.025);
        body.scale.set(1 / Math.sqrt(pulse), pulse, 1 / Math.sqrt(pulse));
        model.rotation.x = fleeing ? 0.5 : Math.sin(t * 1.1) * 0.03;
        spread += ((fleeing ? 0 : 1) - spread) * Math.min(1, dt * 4);
        for (const arm of arms) {
          // calm: arms fan out around the body; fleeing: they gather behind it
          const yaw = Math.PI + (arm.ang - Math.PI) * (0.12 + 0.88 * spread);
          arm.pivot.rotation.y = yaw + Math.sin(t * 0.9 + arm.ph) * 0.12 * spread;
          arm.pivot.rotation.x = -0.05 - (fleeing ? 0.5 : 0) + Math.sin(t * (fleeing ? 7 : 1.7) + arm.ph) * 0.1;
          arm.pivot.rotation.z = Math.sin(t * 1.4 + arm.ph * 2) * 0.12 * spread;
        }
        a.pts[0].set(a.x, a.y + 0.6, a.z);
        a.pts[1].set(a.x, a.y + 0.15, a.z);
      },
    };
    return a;
  }

  // ---------------------------------------------------------------- seahorse

  private seahorse(): Animal {
    const root = new THREE.Group();
    const curve = new THREE.CatmullRomCurve3([
      [0, 0.5, 0.07], [0, 0.4, 0.0], [0, 0.26, 0.05], [0, 0.12, 0.0], [0, 0.02, -0.07], [0, -0.06, -0.15], [0, -0.14, -0.12], [0, -0.17, -0.04], [0, -0.11, -0.01], [0, -0.07, -0.05],
    ].map(([x, y, z]) => new THREE.Vector3(x, y, z)));
    const top = Math.random() < 0.5 ? 0xf2b630 : 0xf08a3a;
    const parts: THREE.BufferGeometry[] = [];
    const body = new THREE.TubeGeometry(curve, 32, 0.045, 8);
    // taper the tube: thick in the belly, thin toward the curled tail
    const bp = body.attributes.position as THREE.BufferAttribute;
    const ring = 9;
    for (let i = 0; i < bp.count; i++) {
      const seg = Math.floor(i / ring) / 32;
      const c = curve.getPoint(Math.min(1, seg));
      const k = seg < 0.45 ? 1 + (0.45 - seg) * 0.3 : Math.max(0.3, 1 - (seg - 0.45) * 1.25);
      bp.setXYZ(i, bp.getX(i), c.y + (bp.getY(i) - c.y) * k, c.z + (bp.getZ(i) - c.z) * k);
      void c;
    }
    body.computeVertexNormals();
    parts.push(solid(body, top));
    const head = solid(new THREE.SphereGeometry(0.06, 10, 8), top);
    head.translate(0, 0.51, 0.08);
    const snout = solid(new THREE.CylinderGeometry(0.014, 0.022, 0.13, 8), 0xe89a38);
    snout.rotateX(Math.PI / 2);
    snout.translate(0, 0.49, 0.2);
    const crest = solid(new THREE.ConeGeometry(0.02, 0.07, 6), 0xd9772a);
    crest.translate(0, 0.6, 0.04);
    parts.push(head, snout, crest);
    for (const sx of [-1, 1]) {
      const eye = solid(new THREE.SphereGeometry(0.014, 6, 5), 0x101418);
      eye.translate(sx * 0.045, 0.53, 0.1);
      parts.push(eye);
    }
    for (let i = 0; i < 6; i++) {
      const ridge = solid(new THREE.SphereGeometry(0.03, 6, 5), 0xd9772a);
      const p = curve.getPoint(0.05 + i * 0.07);
      ridge.translate(0, p.y, p.z - 0.04);
      parts.push(ridge);
    }
    const mesh = new THREE.Mesh(mergeGeometries(parts, false)!, vcol);
    // a thin dorsal fin that flutters
    const finShape = new THREE.Shape([new THREE.Vector2(0, 0), new THREE.Vector2(0.1, 0.03), new THREE.Vector2(0.1, 0.16), new THREE.Vector2(0, 0.14)]);
    const fin = new THREE.Mesh(
      new THREE.ShapeGeometry(finShape),
      new THREE.MeshStandardMaterial({ color: 0xf6d36a, transparent: true, opacity: 0.75, side: THREE.DoubleSide, roughness: 0.4 }),
    );
    fin.position.set(0, 0.12, -0.06);
    fin.rotation.y = -Math.PI / 2;
    const model = new THREE.Group();
    model.add(mesh, fin);
    model.scale.setScalar(rnd(0.8, 1.15));
    root.add(model);
    const a: Animal = {
      kind: 'seahorse', label: 'seahorse', reward: 2, r: 0.3, gone: 50, alive: true, respawn: 0, root,
      pts: [new THREE.Vector3()], x: 0, y: 0, z: 0, heading: 0,
      place: () => this.spot(a, [0.8, 3.2]),
      tick: (dt, t, cam) => {
        const fleeing = this.flee(a, cam, dt, 2.2);
        const speed = fleeing ? 0.9 : 0.18;
        a.heading += Math.sin(t * 0.4 + a.z) * dt * 0.4;
        this.keepIn(a, dt);
        a.x += Math.sin(a.heading) * speed * dt;
        a.z += Math.cos(a.heading) * speed * dt;
        const f = this.floor(a.x, a.z);
        a.y = Math.min(-1.2, Math.max(f + 0.6, a.y + Math.sin(t * 0.8 + a.x) * dt * 0.1));
        root.position.set(a.x, a.y + Math.sin(t * 1.4 + a.z) * 0.06, a.z);
        root.rotation.y = a.heading;
        model.rotation.x = -0.1 + Math.sin(t * 1.1 + a.x) * 0.06;
        fin.scale.set(1, 1 + Math.sin(t * 22 + a.x) * 0.18, 1);
        a.pts[0].set(a.x, a.y + 0.25, a.z);
      },
    };
    return a;
  }

  // ---------------------------------------------------------------- sea snake

  private snake(): Animal {
    const root = new THREE.Group();
    const n = 22;
    const geo = new THREE.SphereGeometry(1, 10, 8);
    const bandA = new THREE.MeshStandardMaterial({ color: 0xcfe0d8, roughness: 0.45 });
    const bandB = new THREE.MeshStandardMaterial({ color: 0x1c3238, roughness: 0.45 });
    const segs: THREE.Mesh[] = [];
    for (let i = 0; i < n; i++) {
      const m = new THREE.Mesh(geo, Math.floor(i / 2) % 2 ? bandB : bandA);
      const r = i === 0 ? 0.075 : 0.07 * (1 - (i / n) * 0.65);
      m.scale.set(r, r * (i === 0 ? 0.75 : 1.1), r * 1.5);
      segs.push(m);
      root.add(m);
    }
    for (const sx of [-1, 1]) {
      const eye = new THREE.Mesh(new THREE.SphereGeometry(0.014, 6, 5), new THREE.MeshBasicMaterial({ color: 0xfafafa }));
      eye.position.set(sx * 0.05, 0.03, 0.06);
      segs[0].add(eye);
      eye.scale.set(1 / 0.075, 1 / 0.056, 1 / 0.1125);
    }
    const tmpFwd = new THREE.Vector3();
    const a: Animal = {
      kind: 'snake', label: 'sea snake', reward: 3, r: 0.35, gone: 60, alive: true, respawn: 0, root,
      pts: [new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3()], x: 0, y: 0, z: 0, heading: 0,
      place: () => this.spot(a, [0.5, 3.5]),
      tick: (dt, t, cam) => {
        const fleeing = this.flee(a, cam, dt, 3.5);
        const speed = fleeing ? 2.6 : 0.8;
        a.heading += Math.sin(t * 0.35 + a.x * 0.3) * dt * 0.5;
        this.keepIn(a, dt);
        a.x += Math.sin(a.heading) * speed * dt;
        a.z += Math.cos(a.heading) * speed * dt;
        const f = this.floor(a.x, a.z);
        a.y = Math.min(-1.2, Math.max(f + 0.4, a.y + Math.sin(t * 0.5 + a.z * 0.2) * dt * 0.4));
        const fx = Math.sin(a.heading);
        const fz = Math.cos(a.heading);
        tmpFwd.set(fx, 0, fz);
        const wave = fleeing ? 9 : 4.5;
        for (let i = 0; i < n; i++) {
          const along = i * 0.13;
          const side = Math.sin(t * wave - i * 0.5) * 0.11 * (0.4 + i / n);
          segs[i].position.set(a.x - fx * along + fz * side, a.y + Math.sin(t * 2 + i * 0.3) * 0.02, a.z - fz * along - fx * side);
          // each piece faces roughly along the body
          segs[i].rotation.y = a.heading + Math.cos(t * wave - i * 0.5) * 0.45;
        }
        a.pts[0].copy(segs[0].position);
        a.pts[1].copy(segs[Math.floor(n / 2)].position);
        a.pts[2].copy(segs[n - 1].position);
      },
    };
    return a;
  }

  // ---------------------------------------------------------------- sea turtle

  /** Gentle and slow, and not something to spear: the harpoon passes by. */
  private turtle(): Animal {
    const root = new THREE.Group();
    const parts: THREE.BufferGeometry[] = [];
    // the shell: a flattened dome with plates of two greens
    const shell = new THREE.SphereGeometry(0.5, 28, 18);
    shell.scale(1, 0.42, 1.25);
    const sp = shell.attributes.position as THREE.BufferAttribute;
    const col = new Float32Array(sp.count * 3);
    const c = new THREE.Color();
    for (let i = 0; i < sp.count; i++) {
      const x = sp.getX(i);
      const y = sp.getY(i);
      const z = sp.getZ(i);
      if (y < -0.02) c.set(0xe3d9a8);
      else {
        const plate = Math.sin(x * 15 + Math.sin(z * 9) * 1.3) * Math.sin(z * 12 + Math.sin(x * 7) * 1.1);
        c.set(plate > 0.1 ? 0x5a7a3a : 0x3f5a2a).lerp(new THREE.Color(0x8a6a3a), Math.max(0, -plate) * 0.4);
      }
      col.set([c.r, c.g, c.b], i * 3);
    }
    shell.deleteAttribute('uv');
    shell.setAttribute('color', new THREE.BufferAttribute(col, 3));
    parts.push(shell);
    const skin = 0x8fa860;
    const head = solid(new THREE.SphereGeometry(0.14, 12, 9), skin);
    head.scale(1, 0.85, 1.2);
    head.translate(0, 0.0, 0.72);
    const neck = solid(new THREE.CylinderGeometry(0.09, 0.11, 0.25, 8), skin);
    neck.rotateX(Math.PI / 2);
    neck.translate(0, 0, 0.58);
    const beak = solid(new THREE.SphereGeometry(0.05, 8, 6), 0x6e8248);
    beak.translate(0, -0.03, 0.85);
    const tail = solid(new THREE.ConeGeometry(0.05, 0.22, 6), skin);
    tail.rotateX(-Math.PI / 2);
    tail.translate(0, -0.02, -0.7);
    parts.push(head, neck, beak, tail);
    for (const sx of [-1, 1]) {
      const eye = solid(new THREE.SphereGeometry(0.025, 6, 5), 0x101418);
      eye.translate(sx * 0.1, 0.04, 0.8);
      parts.push(eye);
    }
    const body = new THREE.Mesh(mergeGeometries(parts, false)!, vcol);
    // the flippers are separate, so they can beat
    const flip = (len: number, w: number): THREE.BufferGeometry => {
      const g = solid(new THREE.SphereGeometry(1, 10, 8), skin);
      g.scale(len, 0.035, w);
      g.translate(len * 0.9, 0, 0);
      return g;
    };
    const flippers: { m: THREE.Mesh; side: number; front: boolean }[] = [];
    for (const side of [-1, 1]) {
      for (const front of [true, false]) {
        const m = new THREE.Mesh(front ? flip(0.38, 0.13) : flip(0.2, 0.09), vcol);
        m.position.set(side * (front ? 0.36 : 0.28), -0.07, front ? 0.34 : -0.45);
        if (side < 0) m.scale.x = -1;
        flippers.push({ m, side, front });
        body.add(m);
      }
    }
    const model = new THREE.Group();
    model.add(body);
    model.scale.setScalar(rnd(1.0, 1.5));
    root.add(model);
    const a: Animal = {
      kind: 'turtle', label: 'turtle', reward: 0, r: 0, gone: 1, alive: true, respawn: 0, root,
      pts: [], x: 0, y: 0, z: 0, heading: 0,
      place: () => this.spot(a, [2.2, 6]),
      tick: (dt, t) => {
        a.heading += Math.sin(t * 0.18 + a.x * 0.1) * dt * 0.25;
        this.keepIn(a, dt);
        const speed = 0.75;
        a.x += Math.sin(a.heading) * speed * dt;
        a.z += Math.cos(a.heading) * speed * dt;
        const f = this.floor(a.x, a.z);
        a.y = Math.min(-2.2, Math.max(f + 1.4, a.y + Math.sin(t * 0.25 + a.z * 0.1) * dt * 0.4));
        root.position.set(a.x, a.y + Math.sin(t * 0.8 + a.z) * 0.08, a.z);
        root.rotation.y = a.heading;
        model.rotation.z = Math.sin(t * 0.8) * 0.05;
        model.rotation.x = Math.sin(t * 0.5 + a.x) * 0.04;
        for (const fl of flippers) {
          const beat = Math.sin(t * 1.5 + (fl.front ? 0 : 1.2) + (fl.side > 0 ? 0 : 0.0));
          fl.m.rotation.z = fl.side * (fl.front ? beat * 0.55 : beat * 0.3) * (fl.side > 0 ? -1 : -1) * -1;
        }
      },
    };
    return a;
  }

  // ---------------------------------------------------------------- crab

  private crab(): Animal {
    const root = new THREE.Group();
    const shell = [0xd4552e, 0xc2452c, 0xe0703a][Math.floor(Math.random() * 3)];
    const parts: THREE.BufferGeometry[] = [];
    const body = solid(new THREE.SphereGeometry(0.2, 14, 10), shell);
    body.scale(1.25, 0.55, 0.95);
    body.translate(0, 0.14, 0);
    parts.push(body);
    for (const sx of [-1, 1]) {
      // eye stalks, legs and a claw on each side
      const stalk = solid(new THREE.CylinderGeometry(0.01, 0.01, 0.07, 5), 0xe8a070);
      stalk.translate(sx * 0.07, 0.25, 0.15);
      const eye = solid(new THREE.SphereGeometry(0.02, 6, 5), 0x101418);
      eye.translate(sx * 0.07, 0.29, 0.16);
      parts.push(stalk, eye);
      for (let i = 0; i < 3; i++) {
        const leg = solid(new THREE.CylinderGeometry(0.012, 0.01, 0.26, 5), 0xb8421f);
        leg.rotateZ(sx * 1.0);
        leg.translate(sx * 0.3, 0.1, -0.06 + i * 0.09);
        const foot = solid(new THREE.CylinderGeometry(0.01, 0.006, 0.12, 5), 0xb8421f);
        foot.rotateZ(sx * 0.2);
        foot.translate(sx * 0.42, 0.02, -0.06 + i * 0.09);
        parts.push(leg, foot);
      }
      const arm = solid(new THREE.CylinderGeometry(0.02, 0.025, 0.2, 6), 0xc2452c);
      arm.rotateX(Math.PI / 2 - 0.2);
      arm.translate(sx * 0.2, 0.15, 0.2);
      const claw = solid(new THREE.SphereGeometry(0.07, 8, 6), shell);
      claw.scale(1, 0.7, 1.35);
      claw.translate(sx * 0.2, 0.15, 0.36);
      const pincer = solid(new THREE.ConeGeometry(0.03, 0.1, 6), 0xf0d0b0);
      pincer.rotateX(Math.PI / 2);
      pincer.translate(sx * 0.2, 0.15, 0.46);
      parts.push(arm, claw, pincer);
    }
    const mesh = new THREE.Mesh(mergeGeometries(parts, false)!, vcol);
    mesh.castShadow = false;
    root.add(mesh);
    root.scale.setScalar(rnd(0.8, 1.25));
    let dir = 1;
    let pause = 0;
    const a: Animal = {
      kind: 'crab', label: 'crab', reward: 1, r: 0.35, gone: 35, alive: true, respawn: 0, root,
      pts: [new THREE.Vector3()], x: 0, y: 0, z: 0, heading: 0,
      place: () => {
        this.spot(a, [0, 0]);
        a.y = this.floor(a.x, a.z);
      },
      tick: (dt, t, cam) => {
        const fleeing = this.flee(a, cam, dt, 2.6);
        pause -= dt;
        if (pause <= 0 && !fleeing && Math.random() < dt * 0.6) {
          pause = rnd(0.6, 2.2);
          dir = Math.random() < 0.5 ? -1 : 1;
          a.heading += rnd(-0.6, 0.6);
        }
        this.keepIn(a, dt);
        const speed = fleeing ? 1.9 : pause > 0 ? 0.55 : 0;
        // a crab walks sideways
        const sx = Math.cos(a.heading) * dir;
        const sz = -Math.sin(a.heading) * dir;
        a.x += sx * speed * dt;
        a.z += sz * speed * dt;
        a.y = this.floor(a.x, a.z) + 0.01;
        root.position.set(a.x, a.y, a.z);
        root.rotation.y = a.heading;
        mesh.rotation.z = speed > 0 ? Math.sin(t * 26) * 0.05 : 0;
        a.pts[0].set(a.x, a.y + 0.15, a.z);
      },
    };
    return a;
  }
}
