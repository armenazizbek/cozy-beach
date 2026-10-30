import * as THREE from 'three';
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
    const white = new THREE.MeshStandardMaterial({ color: 0xf4f6f8, roughness: 0.25, metalness: 0.15 });
    const blue = new THREE.MeshStandardMaterial({ color: 0x1f5fa8, roughness: 0.4 });
    const teak = new THREE.MeshStandardMaterial({ color: 0xa87a4a, roughness: 0.85 });
    const dark = new THREE.MeshStandardMaterial({ color: 0x22262b, roughness: 0.6, metalness: 0.4 });
    const cream = new THREE.MeshStandardMaterial({ color: 0xe9ddc0, roughness: 0.9 });

    // hull: a top-down outline with a pointed bow, extruded up and softly bevelled
    const outline = (scale: number): THREE.Shape => {
      const s = new THREE.Shape();
      s.moveTo(0, 2.7 * scale);
      s.bezierCurveTo(0.55 * scale, 1.5 * scale, 0.95 * scale, 0.4 * scale, 0.95 * scale, -0.6 * scale);
      s.lineTo(0.9 * scale, -2.3 * scale);
      s.lineTo(-0.9 * scale, -2.3 * scale);
      s.lineTo(-0.95 * scale, -0.6 * scale);
      s.bezierCurveTo(-0.95 * scale, 0.4 * scale, -0.55 * scale, 1.5 * scale, 0, 2.7 * scale);
      return s;
    };
    const hullGeo = new THREE.ExtrudeGeometry(outline(1), { depth: 0.7, bevelEnabled: true, bevelSize: 0.07, bevelThickness: 0.07, bevelSegments: 2 });
    hullGeo.rotateX(-Math.PI / 2); // outline +y (the bow) now points along -z
    const hull = new THREE.Mesh(hullGeo, white);
    hull.position.y = -0.32;
    hull.castShadow = true;
    hull.receiveShadow = true;
    this.group.add(hull);

    const stripeGeo = new THREE.ExtrudeGeometry(outline(1.012), { depth: 0.14, bevelEnabled: false });
    stripeGeo.rotateX(-Math.PI / 2);
    const stripe = new THREE.Mesh(stripeGeo, blue);
    stripe.position.y = 0.0;
    this.group.add(stripe);

    const deckGeo = new THREE.ExtrudeGeometry(outline(0.84), { depth: 0.03, bevelEnabled: false });
    deckGeo.rotateX(-Math.PI / 2);
    const deck = new THREE.Mesh(deckGeo, teak);
    deck.position.y = 0.455;
    deck.receiveShadow = true;
    this.group.add(deck);

    const add = (geo: THREE.BufferGeometry, mat: THREE.Material, x: number, y: number, z: number): THREE.Mesh => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x, y, z);
      m.castShadow = true;
      this.group.add(m);
      return m;
    };
    const box = (w: number, h: number, d: number): THREE.BufferGeometry => new THREE.BoxGeometry(w, h, d);

    // helm console, dashboard and steering wheel
    add(box(1.2, 0.55, 0.5), white, 0, 0.75, -0.55);
    add(box(1.1, 0.06, 0.3), dark, 0, 1.05, -0.62);
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

    // windscreen
    // plain diffuse glass: no sun glints or reflections, so its colour stays steady
    const glass = new THREE.MeshLambertMaterial({ color: 0xcfe8f5, transparent: true, opacity: 0.2, side: THREE.DoubleSide, depthWrite: false });
    const screen = add(box(1.25, 0.42, 0.025), glass, 0, 1.23, -0.86);
    // the glass leans back toward the driver, as a windscreen should
    screen.rotation.x = 0.4;
    for (const sx of [-0.62, 0.62]) {
      const post = add(new THREE.CylinderGeometry(0.02, 0.02, 0.5, 6), dark, sx, 1.21, -0.85);
      post.rotation.x = 0.4;
    }

    // seats, stern bench and outboard motor
    for (const sx of [-0.42, 0.42]) {
      add(box(0.55, 0.3, 0.55), cream, sx, 0.66, 0.5);
      add(box(0.55, 0.45, 0.1), cream, sx, 0.95, 0.8);
    }
    add(box(1.4, 0.3, 0.45), cream, 0, 0.65, 1.7);
    add(box(0.4, 0.75, 0.32), dark, 0, 0.45, 2.45);
    add(box(0.12, 0.7, 0.12), dark, 0, -0.1, 2.55);
    // fenders along the side
    for (const sx of [-1, 1]) {
      for (const sz of [-0.2, 0.9]) {
        add(new THREE.CylinderGeometry(0.07, 0.07, 0.34, 8), new THREE.MeshStandardMaterial({ color: 0xe8662a, roughness: 0.6 }), sx * 0.97, 0.3, sz);
      }
    }
    // navigation lights and a little flag
    add(new THREE.SphereGeometry(0.06, 8, 6), new THREE.MeshStandardMaterial({ color: 0xd62828, emissive: 0xaa0000, emissiveIntensity: 0.6 }), -0.4, 0.55, -1.7);
    add(new THREE.SphereGeometry(0.06, 8, 6), new THREE.MeshStandardMaterial({ color: 0x2a9d4a, emissive: 0x007a2a, emissiveIntensity: 0.6 }), 0.4, 0.55, -1.7);
    add(new THREE.CylinderGeometry(0.012, 0.012, 1.0, 6), dark, 0.7, 1.0, 1.85);
    add(new THREE.PlaneGeometry(0.34, 0.22), new THREE.MeshStandardMaterial({ color: 0xe63946, side: THREE.DoubleSide }), 0.87, 1.4, 1.85);

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
