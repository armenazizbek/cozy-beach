import * as THREE from 'three';
import { buildItem } from '../mechanics/LootGenerator';

const SIZE = 168;

/**
 * Takes a little photograph of each find, for the polaroids on the Lost & Found board. It uses a renderer of its own, once, while the
 * game loads, and hands that back when it is done. Returns an image for each id; if the photos cannot be taken the map is simply empty.
 */
export const takeItemPhotos = (ids: string[]): Map<string, string> => {
  const photos = new Map<string, string>();
  let renderer: THREE.WebGLRenderer | null = null;
  try {
    renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
    renderer.setSize(SIZE, SIZE, false);
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0xd9c6a3);
    const sun = new THREE.DirectionalLight(0xfff2dc, 3);
    sun.position.set(2, 4, 3);
    scene.add(new THREE.HemisphereLight(0xffffff, 0xb59a70, 1.6), sun);
    const camera = new THREE.PerspectiveCamera(28, 1, 0.01, 100);
    const box = new THREE.Box3();
    const middle = new THREE.Vector3();
    const size = new THREE.Vector3();
    for (const id of new Set(ids)) {
      const item = buildItem(id);
      scene.add(item);
      box.setFromObject(item).getCenter(middle);
      const reach = box.getSize(size).length() / 2;
      // from a little above, the way you would photograph something on a table, and close enough to fill the frame
      camera.position.copy(middle).add(new THREE.Vector3(0.45, 0.8, 0.95).normalize().multiplyScalar(reach / Math.sin(THREE.MathUtils.degToRad(14)) * 1.02));
      camera.lookAt(middle);
      renderer.render(scene, camera);
      photos.set(id, renderer.domElement.toDataURL('image/jpeg', 0.85));
      scene.remove(item);
      item.traverse((o) => {
        if (o instanceof THREE.Mesh) o.geometry.dispose();
      });
    }
  } catch {
    /* the board reads fine without pictures */
  }
  renderer?.dispose();
  renderer?.forceContextLoss();
  return photos;
};
