import * as THREE from 'three';
import { ParticleSystem } from '../core/Particles';
import type { StateManager } from '../core/StateManager';
import { Beach, WET_Z } from '../world/Beach';
import { conformToTerrain } from '../world/conform';

const SAND_DRY = new THREE.Color('#e6ccb2');
const SAND_WET = new THREE.Color('#9c7a56');

export class DigTool {
  readonly group = new THREE.Group();
  readonly ring: THREE.Mesh;
  /** a dark band under the bright ring so it shows on pale sand */
  readonly ringDark: THREE.Mesh;
  readonly particles = new ParticleSystem(260, 0.11, 9, false);
  private tool = new THREE.Group();
  private blade: THREE.Mesh;
  private swing = 0;

  constructor() {
    this.blade = new THREE.Mesh(
      new THREE.SphereGeometry(0.32, 16, 10, 0, Math.PI * 2, 0, Math.PI / 2),
      new THREE.MeshStandardMaterial({ color: 0xe0623a, roughness: 0.5, metalness: 0.1, side: THREE.DoubleSide }),
    );
    this.blade.rotation.x = Math.PI;
    this.blade.scale.set(1, 0.7, 1.2);
    this.blade.castShadow = true;
    const handle = new THREE.Mesh(
      new THREE.CylinderGeometry(0.045, 0.05, 1.3, 8),
      new THREE.MeshStandardMaterial({ color: 0x8a6240, roughness: 0.8 }),
    );
    handle.position.set(0, 0.55, -0.55);
    handle.rotation.x = -0.9;
    handle.castShadow = true;
    this.tool.add(this.blade, handle);
    this.group.add(this.tool);

    this.ring = new THREE.Mesh(
      new THREE.RingGeometry(0.93, 1, 64).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0xfff1c9, transparent: true, opacity: 0.8, side: THREE.DoubleSide, depthWrite: false }),
    );
    this.ring.renderOrder = 5;
    this.ringDark = new THREE.Mesh(
      new THREE.RingGeometry(0.92, 1.025, 64).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0x2b1a0c, transparent: true, opacity: 0.32, side: THREE.DoubleSide, depthWrite: false }),
    );
    this.ringDark.renderOrder = 4;
  }

  setActive(on: boolean): void {
    this.group.visible = on;
    this.ring.visible = on;
    this.ringDark.visible = on;
  }

  /** Called every frame while the trowel is the active tool. */
  hover(hit: THREE.Vector3 | null, state: StateManager, canDig: boolean, beach: Beach): void {
    if (!hit) {
      this.group.visible = false;
      this.ring.visible = false;
      this.ringDark.visible = false;
      return;
    }
    this.group.visible = true;
    this.ring.visible = true;
    this.ringDark.visible = true;
    const tier = state.tier('digging');
    const mat = this.blade.material as THREE.MeshStandardMaterial;
    mat.color.set(tier === 1 ? 0xe0623a : tier === 2 ? 0xaab4bd : 0xc9973f);
    mat.metalness = tier === 1 ? 0.1 : 0.8;
    this.group.scale.setScalar(0.7 + tier * 0.25);
    this.group.position.set(hit.x, hit.y + 0.28, hit.z);
    this.ring.position.set(hit.x, hit.y, hit.z);
    this.ring.scale.set(state.digRadius, 1, state.digRadius);
    this.ringDark.position.set(hit.x, hit.y, hit.z);
    this.ringDark.scale.set(state.digRadius, 1, state.digRadius);
    conformToTerrain(this.ring, beach, hit.x, hit.y, hit.z, 0.066);
    conformToTerrain(this.ringDark, beach, hit.x, hit.y, hit.z, 0.058);
    (this.ring.material as THREE.MeshBasicMaterial).color.set(canDig ? 0xffffff : 0xff4a3a);
  }

  /** Performs one dig. Returns false if something stopped it. */
  dig(x: number, z: number, beach: Beach, state: StateManager): void {
    const wet = z > WET_Z;
    beach.digAt(x, z, state.digRadius, state.digStep);
    state.spendWarmth(state.digCost(wet));
    this.swing = 1;
    const p = new THREE.Vector3(x, beach.heightAt(x, z) + 0.1, z);
    const col = SAND_DRY.clone().lerp(SAND_WET, wet ? 0.8 : Math.random() * 0.4);
    this.particles.burst(p, 16 + Math.floor(state.digRadius * 14), 1.4 + state.digRadius, 3.4, 0.8, col);
  }

  update(dt: number): void {
    this.swing = Math.max(0, this.swing - dt * 4);
    this.tool.rotation.x = -this.swing * 0.7;
    this.tool.position.y = -this.swing * 0.08;
    this.particles.update(dt);
  }
}
