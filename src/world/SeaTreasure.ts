import * as THREE from 'three';
import { ParticleSystem } from '../core/Particles';

export const CHEST_REWARD = 175;

/** Where the chests drift. All of them are out in open water, a boat ride from the beach. */
const SPOTS: [number, number][] = [
  [-72, 92],
  [58, 128],
  [92, 66],
];

interface Chest {
  group: THREE.Group;
  x: number;
  z: number;
  taken: boolean;
  phase: number;
}

/** Three treasure chests floating at sea, each marked by a little flag. Sail up to one to haul it aboard. */
export class SeaTreasure {
  readonly group = new THREE.Group();
  readonly chests: Chest[] = [];
  private sparks = new ParticleSystem(120, 0.16, -0.3, true);
  private gold = new THREE.Color(0xffd45a);
  private sparkTimer = 0;

  constructor() {
    for (let i = 0; i < SPOTS.length; i++) {
      const g = this.build();
      const [x, z] = SPOTS[i];
      this.group.add(g);
      this.chests.push({ group: g, x, z, taken: false, phase: Math.random() * 6 });
    }
    this.group.add(this.sparks.points);
  }

  private build(): THREE.Group {
    const g = new THREE.Group();
    const wood = new THREE.MeshStandardMaterial({ color: 0x80512a, roughness: 0.85 });
    const band = new THREE.MeshStandardMaterial({ color: 0x2c2f34, roughness: 0.45, metalness: 0.7 });
    const goldMat = new THREE.MeshStandardMaterial({ color: 0xf2c14e, roughness: 0.25, metalness: 0.9 });
    const add = (geo: THREE.BufferGeometry, mat: THREE.Material, x: number, y: number, z: number): THREE.Mesh => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x, y, z);
      m.castShadow = true;
      g.add(m);
      return m;
    };
    // chest body and a rounded lid
    add(new THREE.BoxGeometry(1.2, 0.62, 0.8), wood, 0, 0.3, 0);
    const lid = add(new THREE.CylinderGeometry(0.4, 0.4, 1.2, 14, 1, false, 0, Math.PI), wood, 0, 0.62, 0);
    lid.rotation.z = Math.PI / 2;
    lid.rotation.y = Math.PI / 2;
    // iron bands
    for (const x of [-0.42, 0.42]) {
      add(new THREE.BoxGeometry(0.1, 0.64, 0.84), band, x, 0.3, 0);
      const arch = add(new THREE.CylinderGeometry(0.42, 0.42, 0.1, 14, 1, false, 0, Math.PI), band, x, 0.62, 0);
      arch.rotation.z = Math.PI / 2;
      arch.rotation.y = Math.PI / 2;
    }
    add(new THREE.BoxGeometry(0.18, 0.2, 0.06), goldMat, 0, 0.58, 0.42);
    // a few gold coins peeking out, as if the lid is not quite shut
    for (let i = 0; i < 3; i++) add(new THREE.CylinderGeometry(0.07, 0.07, 0.02, 10), goldMat, -0.25 + i * 0.25, 0.97, 0.05);
    // two orange floats keep it on the surface
    for (const x of [-0.85, 0.85]) {
      add(new THREE.SphereGeometry(0.26, 12, 10), new THREE.MeshStandardMaterial({ color: 0xff7a2f, roughness: 0.5 }), x, 0.12, 0);
    }
    // a tall pole and a bright flag so it can be spotted from far across the water
    add(new THREE.CylinderGeometry(0.03, 0.03, 3.2, 6), band, 0, 1.9, -0.2);
    const flag = add(new THREE.PlaneGeometry(0.9, 0.55), new THREE.MeshStandardMaterial({ color: 0xff3d3d, emissive: 0x661010, emissiveIntensity: 0.5, side: THREE.DoubleSide }), 0.45, 3.2, -0.2);
    flag.name = 'flag';
    return g;
  }

  /** How many chests are still out there. */
  get remaining(): number {
    return this.chests.filter((c) => !c.taken).length;
  }

  /** Marks chests as already found (when loading a save). */
  setTaken(flags: boolean[]): void {
    this.chests.forEach((c, i) => {
      c.taken = !!flags[i];
      c.group.visible = !c.taken;
    });
  }

  takenFlags(): boolean[] {
    return this.chests.map((c) => c.taken);
  }

  /**
   * Floats the chests and checks whether the boat has reached one.
   * Returns the chest that was just picked up, or null.
   */
  update(dt: number, t: number, level: number, boat: { x: number; z: number } | null): Chest | null {
    let got: Chest | null = null;
    this.sparkTimer -= dt;
    for (const c of this.chests) {
      if (c.taken) continue;
      c.group.position.set(c.x, level - 0.18 + Math.sin(t * 1.4 + c.phase) * 0.07, c.z);
      c.group.rotation.set(Math.sin(t * 1.1 + c.phase) * 0.05, c.phase + Math.sin(t * 0.25 + c.phase) * 0.3, Math.cos(t * 0.9 + c.phase) * 0.06);
      const flag = c.group.getObjectByName('flag');
      if (flag) flag.rotation.y = Math.sin(t * 3 + c.phase) * 0.25;
      if (this.sparkTimer <= 0) {
        this.sparks.emit(
          new THREE.Vector3(c.x + (Math.random() - 0.5) * 0.8, level + 1.0, c.z + (Math.random() - 0.5) * 0.5),
          new THREE.Vector3((Math.random() - 0.5) * 0.3, 0.7, (Math.random() - 0.5) * 0.3),
          1.4,
          this.gold,
        );
      }
      if (boat && Math.hypot(boat.x - c.x, boat.z - c.z) < 4) {
        c.taken = true;
        c.group.visible = false;
        got = c;
      }
    }
    if (this.sparkTimer <= 0) this.sparkTimer = 0.12;
    this.sparks.update(dt);
    return got;
  }
}
