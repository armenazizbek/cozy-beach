import * as THREE from 'three';

interface Placed {
  group: THREE.Group;
  cloth: THREE.Mesh;
  phase: number;
}

/** Marker flags the player plants on the beach. Their positions also feed the maps. */
export class Flags {
  readonly group = new THREE.Group();
  /** [x, z] of every planted flag; edited in place so the map can hold on to it */
  readonly positions: [number, number][] = [];
  private placed: Placed[] = [];
  private pole = new THREE.CylinderGeometry(0.025, 0.03, 1.7, 6);
  private poleMat = new THREE.MeshStandardMaterial({ color: 0xd9c9a3, roughness: 0.8 });
  private clothGeo: THREE.BufferGeometry;
  private clothMat = new THREE.MeshStandardMaterial({ color: 0xe63946, roughness: 0.7, side: THREE.DoubleSide });
  private capGeo = new THREE.SphereGeometry(0.05, 8, 6);

  constructor() {
    const shape = new THREE.Shape();
    shape.moveTo(0, 0);
    shape.lineTo(0.8, -0.25);
    shape.lineTo(0, -0.5);
    shape.closePath();
    this.clothGeo = new THREE.ShapeGeometry(shape);
  }

  add(x: number, z: number, y: number): void {
    const g = new THREE.Group();
    const pole = new THREE.Mesh(this.pole, this.poleMat);
    pole.position.y = 0.75;
    pole.castShadow = true;
    const cloth = new THREE.Mesh(this.clothGeo, this.clothMat);
    cloth.position.set(0.03, 1.55, 0);
    cloth.castShadow = true;
    const cap = new THREE.Mesh(this.capGeo, this.poleMat);
    cap.position.y = 1.62;
    g.add(pole, cloth, cap);
    g.position.set(x, y, z);
    g.rotation.y = Math.random() * Math.PI * 2;
    this.group.add(g);
    this.placed.push({ group: g, cloth, phase: Math.random() * 6 });
    this.positions.push([x, z]);
  }

  /** Index of the flag nearest to (x, z) within reach, or -1. */
  nearest(x: number, z: number, reach: number): number {
    let best = -1;
    let bd = reach;
    this.positions.forEach(([fx, fz], i) => {
      const d = Math.hypot(fx - x, fz - z);
      if (d < bd) {
        bd = d;
        best = i;
      }
    });
    return best;
  }

  remove(i: number): void {
    const p = this.placed[i];
    if (!p) return;
    this.group.remove(p.group);
    this.placed.splice(i, 1);
    this.positions.splice(i, 1);
  }

  /** Replaces every flag, e.g. when a save is loaded. */
  setAll(list: [number, number][], heightAt: (x: number, z: number) => number): void {
    while (this.placed.length) this.remove(this.placed.length - 1);
    for (const [x, z] of list) this.add(x, z, heightAt(x, z));
  }

  update(t: number): void {
    for (const p of this.placed) {
      p.cloth.rotation.y = Math.sin(t * 2.4 + p.phase) * 0.22;
      p.cloth.rotation.z = Math.sin(t * 3.1 + p.phase) * 0.05;
    }
  }
}
