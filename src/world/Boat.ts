import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';
import { ParticleSystem } from '../core/Particles';

export interface BoatInput {
  throttle: number;
  steer: number;
  brake: boolean;
}

export interface BoatResolved {
  x: number;
  z: number;
  hit: boolean;
}

const MAX_FORWARD = 14;
const MAX_REVERSE = 4;

// The hull is described along its length by t, 0 at the stem and 1 at the transom, and around each cross-section by v, 0 at the deck edge
// and 1 at the keel. The section is one curve from the edge down to the keel, flaring out at the top and running to a V underneath.
const BOW_Z = -2.75;
const HULL_LEN = 5.05;
/** where the deck floor is, and where the raised foredeck ends */
const SOLE_Y = 0.46;
const FOREDECK_T = 0.356;

const beamAt = (t: number): number => 0.98 * Math.pow(Math.sin(Math.min(1, t / 0.62) * Math.PI * 0.5), 0.85) * (1 - 0.05 * Math.max(0, (t - 0.62) / 0.38) ** 2);
const sheerAt = (t: number): number => 0.82 + 0.42 * (1 - t) ** 2.6;
const keelAt = (t: number): number => -0.34 + 0.98 * Math.max(0, 1 - t / 0.34) ** 2;

const hullPoint = (t: number, v: number, side: number): THREE.Vector3 => {
  const w = beamAt(t);
  const top = sheerAt(t);
  const keel = keelAt(t);
  const a = (1 - v) * (1 - v);
  const b = 2 * v * (1 - v);
  const c = v * v;
  return new THREE.Vector3(side * (a * w + b * w * 0.86), a * top + b * (keel + (top - keel) * 0.12) + c * keel, BOW_Z + HULL_LEN * t);
};

/** How far round the section (v) the hull comes down to height y. */
const vAtHeight = (t: number, y: number): number => {
  let lo = 0;
  let hi = 1;
  for (let i = 0; i < 18; i++) {
    const mid = (lo + hi) / 2;
    if (hullPoint(t, mid, 1).y > y) lo = mid;
    else hi = mid;
  }
  return (lo + hi) / 2;
};

/** A grid of points as a smooth surface, with `at(i / rows, j / cols)` giving each one. */
const sheet = (rows: number, cols: number, at: (a: number, b: number) => THREE.Vector3, flip = false): THREE.BufferGeometry => {
  const pos: number[] = [];
  const idx: number[] = [];
  for (let i = 0; i <= rows; i++) {
    for (let j = 0; j <= cols; j++) {
      const p = at(i / rows, j / cols);
      pos.push(p.x, p.y, p.z);
      if (i < rows && j < cols) {
        const a = i * (cols + 1) + j;
        const b = a + cols + 1;
        if (flip) idx.push(a, a + 1, b, a + 1, b + 1, b);
        else idx.push(a, b, a + 1, a + 1, b, b + 1);
      }
    }
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  geo.setIndex(idx);
  geo.computeVertexNormals();
  return geo;
};

/** A tube along a curve through the points. */
const pipe = (pts: THREE.Vector3[], r: number, closed = false): THREE.BufferGeometry => new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts, closed), pts.length * 4, r, 8, closed);

/** A cylinder between two points. */
const rod = (a: THREE.Vector3, b: THREE.Vector3, r: number, mat: THREE.Material): THREE.Mesh => {
  const d = b.clone().sub(a);
  const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, d.length(), 8), mat);
  m.position.copy(a).add(b).multiplyScalar(0.5);
  m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), d.normalize());
  return m;
};

/** A small motorboat the player can climb into and drive across the sea. */
export class Boat {
  readonly group = new THREE.Group();
  readonly wake = new ParticleSystem(500, 0.2, 0, false);
  x: number;
  z: number;
  /** same convention as three.js rotation.y: forward is (-sin, -cos); positive turns left */
  heading: number;
  speed = 0;
  steerAngle = 0;

  private vx = 0;
  private vz = 0;
  private wheel = new THREE.Group();
  private motor = new THREE.Group();
  private prop = new THREE.Group();
  private pitch = 0;
  private roll = 0;
  private foamTimer = 0;
  private tmp = new THREE.Vector3();
  private white = new THREE.Color(0xf2fbff);

  constructor(x: number, z: number, heading: number) {
    this.x = x;
    this.z = z;
    this.heading = heading;
    this.build();
  }

  private build(): void {
    const steelLike = { side: THREE.DoubleSide };
    const white = new THREE.MeshStandardMaterial({ color: 0xf4f6f8, roughness: 0.3, metalness: 0.1, ...steelLike });
    const blue = new THREE.MeshStandardMaterial({ color: 0x1f5fa8, roughness: 0.4, ...steelLike });
    const bottom = new THREE.MeshStandardMaterial({ color: 0x173b5e, roughness: 0.6, ...steelLike });
    const teak = new THREE.MeshStandardMaterial({ color: 0xa87a4a, roughness: 0.85 });
    const dark = new THREE.MeshStandardMaterial({ color: 0x22262b, roughness: 0.6, metalness: 0.4 });
    const chrome = new THREE.MeshStandardMaterial({ color: 0xcfd6dc, roughness: 0.3, metalness: 0.7 });
    const cream = new THREE.MeshStandardMaterial({ color: 0xe9ddc0, roughness: 0.9 });
    const add = (geo: THREE.BufferGeometry, mat: THREE.Material, x = 0, y = 0, z = 0, parent: THREE.Object3D = this.group): THREE.Mesh => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x, y, z);
      m.castShadow = true;
      m.receiveShadow = true;
      parent.add(m);
      return m;
    };
    const soft = (w: number, h: number, d: number, r: number): THREE.BufferGeometry => new RoundedBoxGeometry(w, h, d, 3, r);

    // ---- the hull shell, in bands: white topsides with a blue stripe, and dark paint below the water
    const band = (v0: number, v1: number, mat: THREE.Material): void => {
      for (const side of [-1, 1]) {
        add(sheet(56, 8, (a, b) => hullPoint(Math.max(0.0005, a), v0 + (v1 - v0) * b, side), side < 0), mat);
      }
    };
    band(0, 0.14, white);
    band(0.14, 0.2, blue);
    band(0.2, 0.47, white);
    band(0.47, 1, bottom);
    // the flat transom closes the stern, in the same bands
    const across = (t: number, v0: number, v1: number, mat: THREE.Material): void => {
      add(sheet(6, 8, (a, b) => hullPoint(t, v0 + (v1 - v0) * a, b * 2 - 1)), mat);
    };
    across(1, 0, 0.14, white);
    across(1, 0.14, 0.2, blue);
    across(1, 0.2, 0.47, white);
    across(1, 0.47, 1, bottom);
    // a bulkhead closes the front of the cockpit under the foredeck
    across(FOREDECK_T, 0, vAtHeight(FOREDECK_T, SOLE_Y), white);

    // ---- the foredeck, crowned a little, with a hatch on it
    add(
      sheet(24, 12, (a, b) => {
        const t = Math.max(0.0005, a * FOREDECK_T);
        const s = b * 2 - 1;
        return new THREE.Vector3(s * beamAt(t), sheerAt(t) + 0.06 * (1 - s * s) * Math.min(1, t / 0.12), BOW_Z + HULL_LEN * t);
      }),
      white,
    );
    const hatch = add(soft(0.62, 0.05, 0.52, 0.02), cream, 0, sheerAt(0.24) + 0.07, BOW_Z + HULL_LEN * 0.24);
    hatch.rotation.x = 0.11;

    // ---- the cockpit floor: teak planks running fore and aft, out to where the hull meets it
    const planks = (() => {
      const c = document.createElement('canvas');
      c.width = 128;
      c.height = 8;
      const x = c.getContext('2d')!;
      x.fillStyle = '#b5885a';
      x.fillRect(0, 0, 128, 8);
      x.fillStyle = '#5b3f25';
      for (let i = 0; i < 4; i++) x.fillRect((i * 128) / 4, 0, 3, 8);
      const t = new THREE.CanvasTexture(c);
      t.colorSpace = THREE.SRGBColorSpace;
      t.wrapS = t.wrapT = THREE.RepeatWrapping;
      return t;
    })();
    const sole = sheet(20, 10, (a, b) => {
      const t = FOREDECK_T + (1 - FOREDECK_T) * a;
      return new THREE.Vector3((b * 2 - 1) * hullPoint(t, vAtHeight(t, SOLE_Y), 1).x, SOLE_Y, BOW_Z + HULL_LEN * t);
    });
    const soleP = sole.attributes.position as THREE.BufferAttribute;
    sole.setAttribute('uv', new THREE.Float32BufferAttribute(Array.from({ length: soleP.count }, (_, i) => [soleP.getX(i) * 2, soleP.getZ(i)]).flat(), 2));
    add(sole, new THREE.MeshStandardMaterial({ map: planks, roughness: 0.85 }));

    // ---- a teak rail along the top edge of the hull, right round the boat
    const rail: THREE.Vector3[] = [];
    for (let i = 0; i <= 22; i++) rail.push(hullPoint(Math.max(0.0005, 1 - i / 22), 0, -1));
    for (let i = 1; i <= 22; i++) rail.push(hullPoint(i / 22, 0, 1));
    add(pipe(rail, 0.032, true), teak);

    // ---- a stainless bow rail on three stanchions a side
    const railT = [0.32, 0.22, 0.12];
    const upper: THREE.Vector3[] = [];
    for (const side of [-1, 1]) {
      const pts = railT.map((t) => new THREE.Vector3(side * (beamAt(t) - 0.09), sheerAt(t) + 0.32, BOW_Z + HULL_LEN * t));
      railT.forEach((t, i) => this.group.add(rod(new THREE.Vector3(pts[i].x, sheerAt(t), pts[i].z), pts[i], 0.012, chrome)));
      if (side < 0) upper.push(...pts);
      else upper.push(new THREE.Vector3(0, sheerAt(0.03) + 0.3, BOW_Z + HULL_LEN * 0.03), ...pts.reverse());
    }
    add(pipe(upper, 0.013), chrome);

    // ---- the helm console, moulded in one piece with a sloping dash
    const profile = new THREE.Shape();
    profile.moveTo(0.3, SOLE_Y);
    profile.lineTo(0.3, 0.92);
    profile.quadraticCurveTo(0.3, 0.99, 0.37, 1.0);
    profile.lineTo(0.72, 1.1);
    profile.quadraticCurveTo(0.82, 1.12, 0.84, 1.04);
    profile.lineTo(0.84, SOLE_Y);
    profile.closePath();
    const consoleGeo = new THREE.ExtrudeGeometry(profile, { depth: 1.06, bevelEnabled: true, bevelSize: 0.03, bevelThickness: 0.03, bevelSegments: 3 });
    consoleGeo.rotateY(Math.PI / 2);
    consoleGeo.translate(-0.53, 0, 0);
    add(consoleGeo, white);
    const slope = 0.278;
    const dash = add(soft(0.96, 0.02, 0.34, 0.008), dark, 0, 1.078, -0.545);
    dash.rotation.x = slope;
    for (const sx of [-0.28, 0.28]) {
      const gauge = add(new THREE.CylinderGeometry(0.052, 0.052, 0.012, 16), chrome, sx, 1.094, -0.56);
      gauge.rotation.x = slope;
      const face = add(new THREE.CylinderGeometry(0.044, 0.044, 0.014, 16), cream, sx, 1.096, -0.56);
      face.rotation.x = slope;
    }
    // the throttle lever beside the wheel
    this.group.add(rod(new THREE.Vector3(0.42, 1.04, -0.42), new THREE.Vector3(0.42, 1.2, -0.35), 0.012, chrome));
    add(new THREE.SphereGeometry(0.032, 10, 8), dark, 0.42, 1.21, -0.345);

    // the column rises from the dash toward the driver, and the wheel sits square on its end
    const column = add(new THREE.CylinderGeometry(0.022, 0.03, 0.32, 8), dark, 0, 1.19, -0.41);
    column.rotation.x = 0.6;
    this.wheel.position.set(0, 1.325, -0.32);
    this.wheel.rotation.x = -(Math.PI / 2 - 0.6);
    const hub = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 0.04, 12), dark);
    hub.rotation.x = Math.PI / 2;
    this.wheel.add(hub);
    const rim = new THREE.Mesh(new THREE.TorusGeometry(0.17, 0.022, 8, 22), dark);
    this.wheel.add(rim);
    for (let i = 0; i < 3; i++) {
      const sp = new THREE.Mesh(new THREE.BoxGeometry(0.015, 0.34, 0.015), dark);
      sp.rotation.z = (i * Math.PI) / 3;
      this.wheel.add(sp);
    }
    this.group.add(this.wheel);

    // ---- a windscreen that wraps back around the console, in a slim frame
    // plain diffuse glass: no sun glints or reflections, so its colour stays steady
    const glass = new THREE.MeshLambertMaterial({ color: 0xcfe8f5, transparent: true, opacity: 0.2, side: THREE.DoubleSide, depthWrite: false });
    const glassAt = (s: number, h: number): THREE.Vector3 => new THREE.Vector3(s * 0.6, 1.1 + 0.42 * h, -0.86 + 0.2 * s * s + 0.17 * h);
    this.group.add(new THREE.Mesh(sheet(16, 2, (a, b) => glassAt(a * 2 - 1, b)), glass));
    const span = (h: number): THREE.Vector3[] => Array.from({ length: 9 }, (_, i) => glassAt(i / 4 - 1, h));
    add(pipe(span(1), 0.013), dark);
    for (const s of [-1, 1]) add(pipe([glassAt(s, -0.05), glassAt(s, 0.5), glassAt(s, 1)], 0.013), dark);

    // ---- two helm seats on pedestals
    for (const sx of [-0.42, 0.42]) {
      add(new THREE.CylinderGeometry(0.16, 0.18, 0.03, 16), dark, sx, SOLE_Y + 0.015, 0.5);
      add(new THREE.CylinderGeometry(0.05, 0.05, 0.2, 10), chrome, sx, SOLE_Y + 0.12, 0.5);
      add(soft(0.52, 0.14, 0.5, 0.06), cream, sx, 0.72, 0.5);
      const back = add(soft(0.52, 0.46, 0.12, 0.05), cream, sx, 1.0, 0.77);
      back.rotation.x = 0.12;
    }
    // ---- the stern bench, right across the boat
    add(soft(1.5, 0.3, 0.42, 0.04), white, 0, 0.61, 1.98);
    add(soft(1.46, 0.12, 0.4, 0.05), cream, 0, 0.82, 1.98);
    add(soft(1.42, 0.3, 0.09, 0.04), cream, 0, 1.0, 2.2);

    // ---- the outboard, on a clamp over the transom: it turns with the wheel, and the propeller spins
    add(soft(0.26, 0.22, 0.14, 0.02), dark, 0, 0.74, 2.33);
    this.motor.position.set(0, 0, 2.42);
    this.group.add(this.motor);
    const cowl = add(soft(0.36, 0.5, 0.52, 0.12), dark, 0, 1.02, 0.08, this.motor);
    cowl.rotation.x = 0.06;
    add(soft(0.37, 0.06, 0.53, 0.02), white, 0, 0.92, 0.08, this.motor);
    add(soft(0.13, 1.05, 0.24, 0.05), dark, 0, 0.27, 0.04, this.motor);
    add(new THREE.BoxGeometry(0.3, 0.02, 0.36), dark, 0, -0.08, 0.06, this.motor);
    const gearcase = add(new THREE.CapsuleGeometry(0.058, 0.22, 4, 12), dark, 0, -0.2, 0.05, this.motor);
    gearcase.rotation.x = Math.PI / 2;
    add(new THREE.BoxGeometry(0.018, 0.16, 0.12), dark, 0, -0.3, 0.03, this.motor);
    this.prop.position.set(0, -0.2, 0.25);
    this.motor.add(this.prop);
    const hubCap = new THREE.Mesh(new THREE.ConeGeometry(0.045, 0.1, 10), chrome);
    hubCap.rotation.x = Math.PI / 2;
    this.prop.add(hubCap);
    for (let i = 0; i < 3; i++) {
      const pivot = new THREE.Group();
      pivot.rotation.z = (i * Math.PI * 2) / 3;
      const blade = new THREE.Mesh(new THREE.SphereGeometry(0.1, 10, 6), chrome);
      blade.scale.set(0.5, 1, 0.08);
      blade.position.y = 0.1;
      blade.rotation.y = 0.5;
      pivot.add(blade);
      this.prop.add(pivot);
    }

    // ---- navigation lights on the bow, and a little flag at the stern quarter
    const lightT = 0.27;
    const lightY = sheerAt(lightT) + 0.01;
    add(new THREE.SphereGeometry(0.05, 10, 8), new THREE.MeshStandardMaterial({ color: 0xd62828, emissive: 0xaa0000, emissiveIntensity: 0.6 }), -beamAt(lightT) - 0.02, lightY, BOW_Z + HULL_LEN * lightT);
    add(new THREE.SphereGeometry(0.05, 10, 8), new THREE.MeshStandardMaterial({ color: 0x2a9d4a, emissive: 0x007a2a, emissiveIntensity: 0.6 }), beamAt(lightT) + 0.02, lightY, BOW_Z + HULL_LEN * lightT);
    add(new THREE.CylinderGeometry(0.012, 0.012, 1.0, 6), dark, 0.74, 1.3, 2.14);
    add(new THREE.PlaneGeometry(0.34, 0.22), new THREE.MeshStandardMaterial({ color: 0xe63946, side: THREE.DoubleSide }), 0.91, 1.66, 2.14);
  }

  /** World position of the helmsman's eyes. */
  seat(out = this.tmp): THREE.Vector3 {
    this.group.updateWorldMatrix(true, false);
    return this.group.localToWorld(out.set(0, 1.85, 1.0));
  }

  /** Moves and bobs the boat on the water. `level` is the surface height. */
  update(dt: number, t: number, input: BoatInput | null, level: number, resolve: (x: number, z: number) => BoatResolved): void {
    const inp = input ?? { throttle: 0, steer: 0, brake: false };
    const prevSpeed = this.speed;
    if (inp.throttle > 0) this.speed += (this.speed < 0 ? 6 : 3.6) * dt;
    else if (inp.throttle < 0) this.speed -= (this.speed > 0.3 ? 5 : 2.2) * dt;
    else this.speed *= Math.exp(-dt * 0.35);
    if (inp.brake) this.speed *= Math.exp(-dt * 2.4);
    this.speed = Math.max(-MAX_REVERSE, Math.min(MAX_FORWARD, this.speed));

    this.steerAngle += (inp.steer * 1 - this.steerAngle) * Math.min(1, dt * 3);
    // a boat only turns when water flows past the rudder
    const grip = Math.min(1, Math.abs(this.speed) / 3.5);
    this.heading += this.steerAngle * 0.75 * grip * Math.sign(this.speed || 1) * dt;

    // it carries on sideways a little in a turn: velocity chases the heading
    const tx = -Math.sin(this.heading) * this.speed;
    const tz = -Math.cos(this.heading) * this.speed;
    const k = Math.min(1, dt * 1.7);
    this.vx += (tx - this.vx) * k;
    this.vz += (tz - this.vz) * k;

    const r = resolve(this.x + this.vx * dt, this.z + this.vz * dt);
    if (r.hit) {
      if (Math.abs(this.speed) > 1) this.speed *= -0.3;
      this.vx *= -0.3;
      this.vz *= -0.3;
    }
    this.x = r.x;
    this.z = r.z;

    // bow lifts as it speeds up and it leans into turns
    const accel = (this.speed - prevSpeed) / Math.max(dt, 0.001);
    const targetPitch = Math.max(0, this.speed) * 0.012 - Math.max(-0.05, Math.min(0.05, accel * 0.01)) * 0 + Math.sin(t * 1.3) * 0.015;
    const targetRoll = -this.steerAngle * this.speed * 0.012 + Math.sin(t * 0.9 + 1) * 0.025;
    this.pitch += (targetPitch - this.pitch) * Math.min(1, dt * 3);
    this.roll += (targetRoll - this.roll) * Math.min(1, dt * 3);
    this.group.position.set(this.x, level + Math.sin(t * 1.7) * 0.04 + 0.05, this.z);
    this.group.rotation.set(this.pitch, this.heading, this.roll, 'YXZ');
    this.wheel.rotation.z = this.steerAngle * 2.4;
    this.motor.rotation.y = -this.steerAngle * 0.45;
    this.prop.rotation.z += this.speed * dt * 6;

    // wake: foam fanning out behind the stern, and bow spray when it is going fast
    this.foamTimer -= dt;
    const sp = Math.abs(this.speed);
    if (this.foamTimer <= 0 && sp > 1) {
      this.foamTimer = 0.04;
      const fx = -Math.sin(this.heading);
      const fz = -Math.cos(this.heading);
      const rx = Math.cos(this.heading);
      const rz = -Math.sin(this.heading);
      for (const side of [-1, 1]) {
        const p = new THREE.Vector3(this.x - fx * 2.6 + rx * side * 0.5, level + 0.04, this.z - fz * 2.6 + rz * side * 0.5);
        const v = new THREE.Vector3(rx * side * (0.8 + sp * 0.1), 0.1, rz * side * (0.8 + sp * 0.1));
        this.wake.emit(p, v, 1.6, this.white);
      }
      if (sp > 5) {
        const bow = new THREE.Vector3(this.x + fx * 2.4, level + 0.1, this.z + fz * 2.4);
        for (const side of [-1, 1]) {
          this.wake.emit(bow, new THREE.Vector3(rx * side * 1.8, 0.8, rz * side * 1.8), 0.7, this.white);
        }
      }
    }
    this.wake.update(dt);
  }
}
