import * as THREE from 'three';
import { Beach } from './Beach';

export interface DriveInput {
  /** +1 forward, -1 back/brake */
  throttle: number;
  /** +1 left, -1 right */
  steer: number;
  brake: boolean;
}

export interface Resolved {
  x: number;
  z: number;
  hit: boolean;
}

const MAX_FORWARD = 10;
const MAX_REVERSE = 4;
const WHEEL_BASE = 1.7;

/** A little electric golf cart the player can climb into and drive around the island. */
export class GolfCart {
  readonly group = new THREE.Group();
  private paint = new THREE.MeshStandardMaterial();
  x: number;
  z: number;
  /** same convention as three.js rotation.y: forward is (-sin, -cos); positive turns left */
  heading: number;
  speed = 0;
  steerAngle = 0;

  private frontWheels: THREE.Group[] = [];
  private spinners: THREE.Object3D[] = [];
  private wheelRoll = 0;
  private steeringWheel = new THREE.Group();
  private headlight: THREE.SpotLight;
  private lampMats: THREE.MeshStandardMaterial[] = [];
  private tmp = new THREE.Vector3();

  constructor(x: number, z: number, heading: number, private beach: Beach) {
    this.x = x;
    this.z = z;
    this.heading = heading;
    this.build();
    this.headlight = new THREE.SpotLight(0xfff0c8, 0, 32, 0.55, 0.6, 1.5);
    this.headlight.visible = false;
    this.headlight.position.set(0, 0.9, -1.4);
    this.headlight.target.position.set(0, 0.5, -10);
    this.group.add(this.headlight, this.headlight.target);
    this.place();
  }

  /** Gold paint, from the Luxury shop. */
  setGilded(): void {
    this.paint.color.set(0xe8b93a);
    this.paint.metalness = 0.6;
    this.paint.roughness = 0.3;
  }

  private part(geo: THREE.BufferGeometry, mat: THREE.Material, x: number, y: number, z: number, parent: THREE.Object3D = this.group): THREE.Mesh {
    const m = new THREE.Mesh(geo, mat);
    m.position.set(x, y, z);
    m.castShadow = true;
    m.receiveShadow = true;
    parent.add(m);
    return m;
  }

  private build(): void {
    const teal = new THREE.MeshStandardMaterial({ color: 0x2f9e8f, roughness: 0.45, metalness: 0.2 });
    const white = new THREE.MeshStandardMaterial({ color: 0xf4f2ea, roughness: 0.6 });
    this.paint = teal;
    const cream = new THREE.MeshStandardMaterial({ color: 0xe6d7b3, roughness: 0.85 });
    const dark = new THREE.MeshStandardMaterial({ color: 0x24262a, roughness: 0.7, metalness: 0.4 });
    const box = (w: number, h: number, d: number): THREE.BufferGeometry => new THREE.BoxGeometry(w, h, d);

    // frame and body
    this.part(box(1.45, 0.16, 2.9), dark, 0, 0.42, 0.05);
    this.part(box(1.5, 0.52, 1.15), teal, 0, 0.72, -0.95);
    this.part(box(1.42, 0.08, 1.7), white, 0, 0.52, 0.6);
    // bench seat and backrest
    this.part(box(1.1, 0.18, 0.62), dark, 0, 0.65, 0.4);
    this.part(box(1.2, 0.16, 0.52), cream, 0, 0.82, 0.35);
    this.part(box(1.2, 0.5, 0.1), cream, 0, 1.15, 0.66);
    // cargo bed with side walls
    this.part(box(1.4, 0.1, 0.85), teal, 0, 0.58, 1.38);
    for (const sx of [-1, 1]) this.part(box(0.06, 0.32, 0.85), teal, sx * 0.7, 0.76, 1.38);
    this.part(box(1.4, 0.32, 0.06), teal, 0, 0.76, 1.8);
    // dashboard and steering column
    this.part(box(1.35, 0.32, 0.16), teal, 0, 1.02, -0.42);
    const column = this.part(new THREE.CylinderGeometry(0.025, 0.025, 0.4, 8), dark, -0.36, 1.044, -0.204);
    column.rotation.x = 0.67; // along the wheel's axis, from the dashboard up to the hub
    this.steeringWheel.position.set(-0.36, 1.2, -0.08);
    this.steeringWheel.rotation.x = -0.9;
    const wheelMesh = new THREE.Mesh(new THREE.TorusGeometry(0.17, 0.022, 8, 22), dark);
    wheelMesh.castShadow = true;
    this.steeringWheel.add(wheelMesh);
    for (const a of [0, Math.PI / 2, Math.PI, Math.PI * 1.5]) {
      const spoke = new THREE.Mesh(new THREE.BoxGeometry(0.015, 0.17, 0.015), dark);
      spoke.position.set(Math.sin(a) * 0.085, Math.cos(a) * 0.085, 0);
      spoke.rotation.z = -a;
      this.steeringWheel.add(spoke);
    }
    this.group.add(this.steeringWheel);

    // roof canopy
    for (const [px, pz] of [[-0.68, -0.52], [0.68, -0.52], [-0.68, 0.78], [0.68, 0.78]]) {
      this.part(new THREE.CylinderGeometry(0.028, 0.028, 1.45, 8), dark, px, 1.4, pz);
    }
    this.part(box(1.62, 0.06, 2.0), white, 0, 2.14, 0.13);
    this.part(box(1.66, 0.03, 2.04), teal, 0, 2.1, 0.13);

    // wheels: front ones steer
    const tire = new THREE.CylinderGeometry(0.3, 0.3, 0.2, 18);
    const hub = new THREE.CylinderGeometry(0.13, 0.13, 0.22, 10);
    const hubMat = new THREE.MeshStandardMaterial({ color: 0xc9ced3, roughness: 0.3, metalness: 0.8 });
    for (const [wx, wz, front] of [[-0.82, -1.0, true], [0.82, -1.0, true], [-0.82, 1.1, false], [0.82, 1.1, false]] as const) {
      const pivot = new THREE.Group();
      pivot.position.set(wx, 0.3, wz);
      this.group.add(pivot);
      const spin = new THREE.Group();
      pivot.add(spin);
      const t = this.part(tire, dark, 0, 0, 0, spin);
      t.rotation.z = Math.PI / 2;
      const h = this.part(hub, hubMat, 0, 0, 0, spin);
      h.rotation.z = Math.PI / 2;
      // a lug so the spin is visible
      this.part(box(0.24, 0.05, 0.08), hubMat, 0, 0.18, 0, spin);
      this.spinners.push(spin);
      if (front) this.frontWheels.push(pivot);
    }

    // lamps
    for (const sx of [-0.5, 0.5]) {
      const mat = new THREE.MeshStandardMaterial({ color: 0xfff6d8, emissive: 0xffe9a8, emissiveIntensity: 0.3 });
      this.lampMats.push(mat);
      this.part(new THREE.SphereGeometry(0.1, 10, 8), mat, sx, 0.85, -1.54);
    }
    for (const sx of [-0.55, 0.55]) {
      this.part(box(0.14, 0.08, 0.04), new THREE.MeshStandardMaterial({ color: 0xc62828, emissive: 0x8a1010, emissiveIntensity: 0.5 }), sx, 0.78, 1.82);
    }
    // a small flag on the roof, to spot the cart from far away
    this.part(new THREE.CylinderGeometry(0.01, 0.01, 0.6, 6), dark, 0.7, 2.45, -0.7);
    this.part(new THREE.PlaneGeometry(0.3, 0.2), new THREE.MeshStandardMaterial({ color: 0xe63946, side: THREE.DoubleSide }), 0.85, 2.65, -0.7);
  }

  /** World position of the driver's eyes. */
  seat(out = this.tmp): THREE.Vector3 {
    this.group.updateWorldMatrix(true, false);
    return this.group.localToWorld(out.set(-0.36, 1.46, 0.18));
  }

  setHeadlights(on: boolean): void {
    this.headlight.intensity = on ? 35 : 0;
    // a light that is off still costs every material in the scene a spot-light loop, so take it out of the render altogether
    this.headlight.visible = on;
    for (const m of this.lampMats) m.emissiveIntensity = on ? 2 : 0.3;
  }

  /** Puts the body on the ground, tilted to the slope under it. */
  private place(): void {
    const fx = -Math.sin(this.heading);
    const fz = -Math.cos(this.heading);
    const rx = Math.cos(this.heading);
    const rz = -Math.sin(this.heading);
    const h = (dx: number, dz: number): number => this.beach.heightAt(this.x + dx, this.z + dz);
    const hF = h(fx * 1.0, fz * 1.0);
    const hB = h(-fx * 1.0, -fz * 1.0);
    const hR = h(rx * 0.8, rz * 0.8);
    const hL = h(-rx * 0.8, -rz * 0.8);
    this.group.position.set(this.x, (hF + hB + hR + hL) / 4 - 0.02, this.z);
    this.group.rotation.set(Math.atan2(hF - hB, 2.0), this.heading, Math.atan2(hR - hL, 1.6), 'YXZ');
  }

  update(dt: number, input: DriveInput | null, resolve: (x: number, z: number) => Resolved): void {
    const inp = input ?? { throttle: 0, steer: 0, brake: false };
    if (inp.throttle > 0) this.speed += (this.speed < 0 ? 9 : 5.5) * dt;
    else if (inp.throttle < 0) this.speed -= (this.speed > 0.3 ? 9 : 4) * dt;
    else this.speed -= Math.sign(this.speed) * Math.min(Math.abs(this.speed), 3 * dt);
    if (inp.brake) this.speed -= Math.sign(this.speed) * Math.min(Math.abs(this.speed), 14 * dt);
    this.speed = Math.max(-MAX_REVERSE, Math.min(MAX_FORWARD, this.speed));

    this.steerAngle += (inp.steer * 0.55 - this.steerAngle) * Math.min(1, dt * 6);
    const eff = this.steerAngle * (1 - Math.min(0.5, Math.abs(this.speed) / 20));
    this.heading += (this.speed / WHEEL_BASE) * Math.tan(eff) * dt;

    const nx = this.x - Math.sin(this.heading) * this.speed * dt;
    const nz = this.z - Math.cos(this.heading) * this.speed * dt;
    const r = resolve(nx, nz);
    if (r.hit && Math.abs(this.speed) > 0.5) this.speed *= -0.25;
    this.x = r.x;
    this.z = r.z;

    this.wheelRoll += (this.speed * dt) / 0.3;
    for (const s of this.spinners) s.rotation.x = this.wheelRoll;
    for (const f of this.frontWheels) f.rotation.y = this.steerAngle;
    this.steeringWheel.rotation.z = this.steerAngle * 2.6;
    this.place();
  }
}
