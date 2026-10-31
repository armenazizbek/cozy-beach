import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

/**
 * Welds the fixed parts of a prop into one mesh per material, so a chair made of ten boxes costs one draw instead of ten. Anything that
 * moves, is clickable on its own or is drawn see-through is left alone: pass moving parts in `keep`, and whatever is inside them stays too.
 */
export const mergeStatic = (root: THREE.Object3D, keep: Set<THREE.Object3D> = new Set()): void => {
  root.updateWorldMatrix(true, true);
  const toRoot = root.matrixWorld.clone().invert();
  const sets = new Map<string, { mat: THREE.Material; cast: boolean; receive: boolean; parts: THREE.Mesh[] }>();
  const walk = (o: THREE.Object3D): void => {
    for (const c of o.children) {
      if (keep.has(c) || c.userData.kind || !c.visible) continue;
      if (c instanceof THREE.Mesh && !(c instanceof THREE.InstancedMesh) && !(c instanceof THREE.SkinnedMesh)) {
        const mat = c.material as THREE.Material | THREE.Material[];
        const fits = !Array.isArray(mat) && mat.visible && !mat.transparent && c.matrixWorld.determinant() * root.matrixWorld.determinant() > 0 && Object.keys(c.userData).length === 0;
        if (fits) {
          const key = `${mat.uuid}|${c.castShadow}|${c.receiveShadow}`;
          const set = sets.get(key) ?? { mat, cast: c.castShadow, receive: c.receiveShadow, parts: [] };
          set.parts.push(c);
          sets.set(key, set);
        }
      }
      walk(c);
    }
  };
  walk(root);
  for (const { mat, cast, receive, parts } of sets.values()) {
    if (parts.length < 2) continue;
    const textured = !!(mat as THREE.MeshStandardMaterial).map;
    const tinted = mat.vertexColors;
    const geos = parts.map((m) => {
      const g = m.geometry.index ? m.geometry.toNonIndexed() : m.geometry.clone();
      for (const name of Object.keys(g.attributes)) {
        if (name === 'position' || name === 'normal' || (name === 'uv' && textured) || (name === 'color' && tinted)) continue;
        g.deleteAttribute(name);
      }
      g.morphAttributes = {};
      g.applyMatrix4(new THREE.Matrix4().multiplyMatrices(toRoot, m.matrixWorld));
      return g;
    });
    const joined = mergeGeometries(geos);
    if (!joined) continue;
    for (const m of parts) m.parent?.remove(m);
    const merged = new THREE.Mesh(joined, mat);
    merged.castShadow = cast;
    merged.receiveShadow = receive;
    root.add(merged);
  }
};
