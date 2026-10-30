import * as THREE from 'three';
import { Beach, WET_Z, SHORE_Z } from './Beach';

const rand = (a: number, b: number): number => a + Math.random() * (b - a);

interface Parts {
  root: THREE.Group;
  legs: THREE.Group;
  left: THREE.Group;
  right: THREE.Group;
}

type Mode = 'fly' | 'ground' | 'flee' | 'away';

interface Gull extends Parts {
  mode: Mode;
  // circling flight
  cx: number;
  cz: number;
  r: number;
  ang: number;
  omega: number;
  alt: number;
  phase: number;
  // on the ground
  vx: number;
  vz: number;
  timer: number;
  climb: number;
  hit: THREE.Mesh;
}

/** Seagulls: some glide in lazy circles over the shore, others stroll on the wet sand and flap away when approached. */
export class Birds {
  readonly group = new THREE.Group();
  private gulls: Gull[] = [];
  private wingGeo: THREE.BufferGeometry;
  private white = new THREE.MeshStandardMaterial({ color: 0xf6f4ee, roughness: 0.8 });
  private grey = new THREE.MeshStandardMaterial({ color: 0xb9bec4, roughness: 0.8 });
  private dark = new THREE.MeshStandardMaterial({ color: 0x2a2c30, roughness: 0.8 });
  private beak = new THREE.MeshStandardMaterial({ color: 0xf2a526, roughness: 0.6 });
  private cryTimer = 4;

  constructor(private beach: Beach) {
    const shape = new THREE.Shape();
    shape.moveTo(0, 0.17);
    shape.lineTo(0.55, 0.14);
    shape.lineTo(1.3, -0.04);
    shape.lineTo(1.22, -0.12);
    shape.lineTo(0.55, -0.1);
    shape.lineTo(0, -0.16);
    shape.closePath();
    this.wingGeo = new THREE.ExtrudeGeometry(shape, { depth: 0.015, bevelEnabled: false });
    this.wingGeo.rotateX(-Math.PI / 2);

    for (let i = 0; i < 14; i++) this.addFlying();
    for (let i = 0; i < 12; i++) this.addGround();
  }

  private makeGull(): Parts {
    const root = new THREE.Group();
    const body = new THREE.Mesh(new THREE.SphereGeometry(0.16, 12, 8), this.white);
    body.scale.set(0.85, 0.8, 1.9);
    body.castShadow = true;
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.085, 10, 8), this.white);
    head.position.set(0, 0.07, 0.34);
    const beak = new THREE.Mesh(new THREE.ConeGeometry(0.03, 0.12, 6), this.beak);
    beak.rotation.x = Math.PI / 2;
    beak.position.set(0, 0.05, 0.46);
    const eyeL = new THREE.Mesh(new THREE.SphereGeometry(0.012, 6, 4), this.dark);
    eyeL.position.set(0.05, 0.09, 0.4);
    const eyeR = eyeL.clone();
    eyeR.position.x = -0.05;
    const tail = new THREE.Mesh(new THREE.BoxGeometry(0.16, 0.02, 0.2), this.grey);
    tail.position.set(0, 0.0, -0.36);
    root.add(body, head, beak, eyeL, eyeR, tail);
    // orange legs, only visible while standing
    const legs = new THREE.Group();
    for (const x of [-0.05, 0.05]) {
      const leg = new THREE.Mesh(new THREE.CylinderGeometry(0.008, 0.008, 0.2, 5), this.beak);
      leg.position.set(x, -0.18, 0.02);
      const foot = new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.008, 0.09), this.beak);
      foot.position.set(x, -0.28, 0.05);
      legs.add(leg, foot);
    }
    root.add(legs);

    const wing = (): THREE.Group => {
      const w = new THREE.Group();
      const m = new THREE.Mesh(this.wingGeo, this.grey);
      const tip = new THREE.Mesh(new THREE.BoxGeometry(0.2, 0.018, 0.1), this.dark);
      tip.position.set(1.2, 0.005, -0.06);
      w.add(m, tip);
      return w;
    };
    const right = wing();
    right.position.set(0.1, 0.05, 0.02);
    const left = wing();
    left.position.set(-0.1, 0.05, 0.02);
    left.scale.x = -1;
    root.add(right, left);
    root.scale.setScalar(1.5);
    this.group.add(root);
    return { root, legs, left, right };
  }

  private blank(mode: Mode): Gull {
    const parts = this.makeGull();
    const hit = new THREE.Mesh(new THREE.SphereGeometry(0.6, 8, 6), new THREE.MeshBasicMaterial({ visible: false }));
    hit.userData.hitRadius = 1;
    parts.root.add(hit);
    const gull: Gull = {
      ...parts, hit, mode, cx: 0, cz: 0, r: 0, ang: 0, omega: 0, alt: 0, phase: Math.random() * 6,
      vx: 0, vz: 0, timer: 0, climb: 0,
    };
    hit.userData.gull = gull;
    return gull;
  }

  private addFlying(): void {
    const g = this.blank('fly');
    g.cx = rand(-55, 55);
    g.cz = rand(0, 60);
    g.r = rand(14, 42);
    g.ang = Math.random() * Math.PI * 2;
    g.omega = (rand(5, 8) / g.r) * (Math.random() < 0.5 ? 1 : -1);
    g.alt = rand(12, 30);
    this.gulls.push(g);
  }

  private landSpot(g: Gull): void {
    const x = rand(-62, 62);
    const z = rand(WET_Z + 1, SHORE_Z - 3);
    g.root.position.set(x, this.beach.heightAt(x, z) + 0.2, z);
    g.mode = 'ground';
    g.timer = rand(1, 4);
    g.vx = g.vz = 0;
    g.root.visible = true;
  }

  private addGround(): void {
    const g = this.blank('ground');
    this.landSpot(g);
    this.gulls.push(g);
  }

  /** Hit spheres of every gull that is currently around. */
  targets(): THREE.Object3D[] {
    return this.gulls.filter((g) => g.mode !== 'away' && g.root.visible).map((g) => g.hit);
  }

  /** A gull is startled right out of sight for a good while. Returns where it was. */
  shoot(obj: THREE.Object3D): THREE.Vector3 | null {
    const g = obj.userData.gull as Gull | undefined;
    if (!g || g.mode === 'away') return null;
    const at = g.root.getWorldPosition(new THREE.Vector3());
    g.mode = 'away';
    g.timer = rand(40, 70);
    g.root.visible = false;
    g.root.rotation.x = 0;
    return at;
  }

  /** Wing angle in radians; positive lifts the tips. */
  private setWings(g: Gull, a: number): void {
    g.legs.visible = g.mode === 'ground';
    g.right.rotation.z = a;
    g.left.rotation.z = -a;
  }

  /** onCry(distance) is called when a gull squawks so the sound can be placed. */
  update(dt: number, t: number, px: number, pz: number, onCry: (distance: number) => void): void {
    for (const g of this.gulls) {
      const r = g.root;
      if (g.mode === 'fly') {
        g.ang += g.omega * dt;
        const dir = Math.sign(g.omega);
        const x = g.cx + Math.cos(g.ang) * g.r;
        const z = g.cz + Math.sin(g.ang) * g.r;
        const y = g.alt + Math.sin(t * 0.5 + g.phase) * 1.4;
        r.rotation.order = 'YXZ';
        r.rotation.y = Math.atan2(-Math.sin(g.ang) * dir, Math.cos(g.ang) * dir);
        r.rotation.z = -dir * 0.3;
        r.position.set(x, y, z);
        // flap, then glide, then flap again
        const flapAmt = Math.max(0, Math.sin(t * 0.35 + g.phase)) ;
        this.setWings(g, 0.12 + Math.sin(t * 6.5 + g.phase) * 0.5 * flapAmt);
      } else if (g.mode === 'ground') {
        g.timer -= dt;
        if (g.timer <= 0) {
          g.timer = rand(1.5, 4.5);
          if (Math.random() < 0.5) {
            g.vx = g.vz = 0;
          } else {
            const a = Math.random() * Math.PI * 2;
            g.vx = Math.cos(a) * 0.45;
            g.vz = Math.sin(a) * 0.45;
          }
        }
        let nx = r.position.x + g.vx * dt;
        let nz = r.position.z + g.vz * dt;
        if (nz < WET_Z || nz > SHORE_Z - 2 || Math.abs(nx) > 64) {
          g.vx *= -1;
          g.vz *= -1;
          nx = r.position.x;
          nz = r.position.z;
        }
        const moving = g.vx !== 0 || g.vz !== 0;
        r.position.set(nx, this.beach.heightAt(nx, nz) + 0.42 + (moving ? Math.abs(Math.sin(t * 8 + g.phase)) * 0.02 : 0), nz);
        if (moving) {
          const target = Math.atan2(g.vx, g.vz);
          let d = target - r.rotation.y;
          d = Math.atan2(Math.sin(d), Math.cos(d));
          r.rotation.y += d * Math.min(1, dt * 4);
        }
        r.rotation.z = 0;
        this.setWings(g, -1.15);
        // startled by a visitor
        const d = Math.hypot(px - nx, pz - nz);
        if (d < 5.5) {
          g.mode = 'flee';
          g.climb = 0;
          const away = Math.atan2(nx - px, nz - pz);
          g.vx = Math.sin(away) * 5.5;
          g.vz = Math.cos(away) * 5.5;
          r.rotation.y = away;
          onCry(d);
        }
      } else if (g.mode === 'flee') {
        g.climb += dt * 4;
        r.position.x += g.vx * dt;
        r.position.z += g.vz * dt;
        r.position.y += Math.min(6, g.climb) * dt;
        r.rotation.x = -0.35;
        this.setWings(g, Math.sin(t * 16 + g.phase) * 0.8);
        if (r.position.y > 16) {
          g.mode = 'away';
          g.timer = rand(25, 45);
          r.visible = false;
          r.rotation.x = 0;
        }
      } else {
        g.timer -= dt;
        if (g.timer <= 0) this.landSpot(g);
      }
    }

    // a distant squawk now and then, from one of the circling birds
    this.cryTimer -= dt;
    if (this.cryTimer <= 0) {
      this.cryTimer = rand(6, 16);
      const flyers = this.gulls.filter((g) => g.mode === 'fly');
      const g = flyers[Math.floor(Math.random() * flyers.length)];
      if (g) onCry(Math.hypot(g.root.position.x - px, g.root.position.z - pz));
    }
    // gulls too far away to make out are not drawn
    for (const g of this.gulls) if (g.mode !== 'away') g.root.visible = Math.hypot(g.root.position.x - px, g.root.position.z - pz) < 140;
  }
}
