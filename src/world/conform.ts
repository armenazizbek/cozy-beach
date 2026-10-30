import * as THREE from 'three';
import { Beach } from './Beach';

/**
 * Bends a flat overlay (a ring or disc lying in the XZ plane) so it follows the real ground
 * under it, instead of being sliced by slopes, mounds and holes.
 * The mesh must sit at the ground point (cx, cy, cz) and may only be scaled in x and z.
 */
export const conformToTerrain = (mesh: THREE.Mesh, beach: Beach, cx: number, cy: number, cz: number, lift = 0.06): void => {
  const pos = mesh.geometry.attributes.position as THREE.BufferAttribute;
  const sx = mesh.scale.x;
  const sz = mesh.scale.z;
  for (let i = 0; i < pos.count; i++) {
    const h = beach.heightAt(cx + pos.getX(i) * sx, cz + pos.getZ(i) * sz);
    pos.setY(i, h - cy + lift);
  }
  pos.needsUpdate = true;
  mesh.geometry.computeBoundingSphere();
};
