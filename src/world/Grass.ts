import * as THREE from 'three';
import { HALF, SHORE_Z, WATER_LEVEL } from './Beach';
import type { Beach } from './Beach';

interface Spot {
  x: number;
  z: number;
  r: number;
}

/** One tuft of beach grass: a fan of tapering blades that lean outward, dark at the root and lighter at the tip. */
const tuftGeometry = (): THREE.BufferGeometry => {
  const pos: number[] = [];
  const col: number[] = [];
  const nrm: number[] = [];
  const root = new THREE.Color(0x93ae4c);
  const tip = new THREE.Color(0xe0dd8c);
  const blades = 9;
  for (let i = 0; i < blades; i++) {
    const a = (i / blades) * Math.PI * 2 + (i % 2) * 0.4;
    const off = 0.02 + (i % 3) * 0.035;
    const h = 0.26 + ((i * 37) % 10) * 0.028;
    const w = 0.026;
    const lean = 0.07 + ((i * 13) % 7) * 0.02;
    const ca = Math.cos(a);
    const sa = Math.sin(a);
    // the blade lies in a plane through its root, leaning away from the centre of the tuft
    const rootX = ca * off;
    const rootZ = sa * off;
    const sx = -sa * w;
    const sz = ca * w;
    const mid = 0.55;
    const verts: [number, number, number, THREE.Color][] = [
      [rootX - sx, 0, rootZ - sz, root],
      [rootX + sx, 0, rootZ + sz, root],
      [rootX - sx * 0.6 + ca * lean * 0.35, h * mid, rootZ - sz * 0.6 + sa * lean * 0.35, root.clone().lerp(tip, 0.5)],
      [rootX + sx * 0.6 + ca * lean * 0.35, h * mid, rootZ + sz * 0.6 + sa * lean * 0.35, root.clone().lerp(tip, 0.5)],
      [rootX + ca * lean, h, rootZ + sa * lean, tip],
    ];
    // every blade is added facing both ways, with its normal pointing up, so it is lit like the sand rather than going dark from behind
    const tri = (p: number, q: number, r: number): void => {
      for (const order of [[p, q, r], [p, r, q]]) {
        for (const k of order) {
          pos.push(verts[k][0], verts[k][1], verts[k][2]);
          col.push(verts[k][3].r, verts[k][3].g, verts[k][3].b);
          nrm.push(0, 1, 0);
        }
      }
    };
    tri(0, 1, 2);
    tri(1, 3, 2);
    tri(2, 3, 4);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(nrm, 3));
  return g;
};

/** A very small amount of grass: a few clumps, mostly where the sand meets the dunes. It sways a little in the wind. */
export class Grass {
  readonly group = new THREE.Group();
  private mesh: THREE.InstancedMesh | null = null;
  private uniforms = { uTime: { value: 0 } };
  private mat: THREE.MeshLambertMaterial;
  private geo = tuftGeometry();

  constructor(private beach: Beach) {
    this.mat = new THREE.MeshLambertMaterial({ vertexColors: true, emissive: 0x3d4a1c });
    this.mat.onBeforeCompile = (shader): void => {
      shader.uniforms.uTime = this.uniforms.uTime;
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nuniform float uTime;')
        .replace(
          '#include <begin_vertex>',
          `#include <begin_vertex>
          #ifdef USE_INSTANCING
            float ph = instanceMatrix[3].x * 0.37 + instanceMatrix[3].z * 0.29;
            transformed.x += sin(uTime * 1.7 + ph) * 0.05 * position.y;
            transformed.z += cos(uTime * 1.3 + ph * 1.3) * 0.035 * position.y;
          #endif`,
        );
    };
  }

  /** Plants the clumps, keeping every tuft a metre or so clear of buried finds so the grass never hides one. */
  scatter(keepClear: Spot[]): void {
    if (this.mesh) {
      this.group.remove(this.mesh);
      this.mesh.dispose();
      this.mesh = null;
    }
    const ok = (x: number, z: number): boolean => {
      if (Math.abs(x) > HALF - 3 || z > SHORE_Z - 8) return false;
      if (this.beach.heightAt(x, z) < WATER_LEVEL + 0.5) return false;
      return !keepClear.some((k) => Math.hypot(k.x - x, k.z - z) < k.r + 1.1);
    };
    const tufts: { x: number; z: number; s: number; a: number }[] = [];
    const clump = (cx: number, cz: number, n: number, spread: number): void => {
      for (let i = 0; i < n; i++) {
        const a = Math.random() * Math.PI * 2;
        const d = Math.random() * spread;
        const x = cx + Math.cos(a) * d;
        const z = cz + Math.sin(a) * d;
        if (ok(x, z)) tufts.push({ x, z, s: 0.8 + Math.random() * 0.7, a: Math.random() * Math.PI * 2 });
      }
    };
    // most of it on the dry sand toward the dunes, a little further down the beach
    for (let c = 0, tries = 0; c < 6 && tries < 200; tries++) {
      const x = (Math.random() - 0.5) * 2 * (HALF - 10);
      const z = -46 + Math.random() * 10;
      if (!ok(x, z)) continue;
      clump(x, z, 5 + Math.floor(Math.random() * 4), 1.1);
      c++;
    }
    for (let c = 0, tries = 0; c < 3 && tries < 200; tries++) {
      const x = (Math.random() - 0.5) * 2 * (HALF - 10);
      const z = -36 + Math.random() * 50;
      if (!ok(x, z)) continue;
      clump(x, z, 3 + Math.floor(Math.random() * 3), 0.8);
      c++;
    }
    if (!tufts.length) return;
    const mesh = new THREE.InstancedMesh(this.geo, this.mat, tufts.length);
    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    tufts.forEach((t, i) => {
      q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), t.a);
      m.compose(new THREE.Vector3(t.x, this.beach.heightAt(t.x, t.z) - 0.02, t.z), q, new THREE.Vector3(t.s, t.s * (0.9 + Math.random() * 0.3), t.s));
      mesh.setMatrixAt(i, m);
    });
    mesh.castShadow = false;
    mesh.receiveShadow = true;
    mesh.frustumCulled = false;
    this.mesh = mesh;
    this.group.add(mesh);
  }

  update(t: number): void {
    this.uniforms.uTime.value = t;
  }
}
