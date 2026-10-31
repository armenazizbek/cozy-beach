import * as THREE from 'three';
import { SHORE_Z } from './Beach';

const COUNT = 260;
const RADIUS = 110;

const VERT = /* glsl */ `
  attribute float aAlpha;
  varying float vAlpha;
  varying vec2 vUv;
  varying float vDepth;
  void main() {
    vAlpha = aAlpha;
    vUv = uv;
    vec4 mv = modelViewMatrix * instanceMatrix * vec4(position, 1.0);
    vDepth = -mv.z;
    gl_Position = projectionMatrix * mv;
  }
`;

const FRAG = /* glsl */ `
  uniform sampler2D uMap;
  uniform vec3 uColor;
  varying float vAlpha;
  varying vec2 vUv;
  varying float vDepth;
  void main() {
    float mask = texture2D(uMap, vUv).a;
    float far = 1.0 - smoothstep(140.0, 230.0, vDepth);
    float a = mask * vAlpha * far;
    if (a < 0.01) discard;
    gl_FragColor = vec4(uColor, a);
  }
`;

/** A lacy, streaky foam patch, wider than it is tall. */
const foamTexture = (): THREE.CanvasTexture => {
  const w = 256;
  const h = 96;
  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  const g = c.getContext('2d')!;
  // a body of soft blobs along the streak
  for (let i = 0; i < 70; i++) {
    const t = Math.random();
    const x = 20 + t * (w - 40);
    const spread = Math.sin(t * Math.PI);
    const y = h / 2 + (Math.random() - 0.5) * h * 0.5 * spread;
    const r = 5 + Math.random() * 14 * (0.4 + spread);
    const grad = g.createRadialGradient(x, y, 0, x, y, r);
    grad.addColorStop(0, 'rgba(255,255,255,0.9)');
    grad.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = grad;
    g.fillRect(x - r, y - r, r * 2, r * 2);
  }
  // punch lace holes so it reads as foam rather than a smudge
  g.globalCompositeOperation = 'destination-out';
  for (let i = 0; i < 55; i++) {
    g.fillStyle = `rgba(0,0,0,${0.35 + Math.random() * 0.5})`;
    g.beginPath();
    g.arc(Math.random() * w, Math.random() * h, 1.5 + Math.random() * 5, 0, Math.PI * 2);
    g.fill();
  }
  return new THREE.CanvasTexture(c);
};

interface Patch {
  x: number;
  z: number;
  age: number;
  life: number;
  len: number;
  yaw: number;
}

/** Whitecaps: patches of foam that bloom, drift and melt away across the open sea around the player. */
export class Whitecaps {
  readonly mesh: THREE.InstancedMesh;
  private patches: Patch[] = [];
  private alpha: THREE.InstancedBufferAttribute;
  private dummy = new THREE.Object3D();
  private color = new THREE.Color();

  constructor() {
    const geo = new THREE.PlaneGeometry(1, 1);
    geo.rotateX(-Math.PI / 2);
    this.alpha = new THREE.InstancedBufferAttribute(new Float32Array(COUNT), 1);
    geo.setAttribute('aAlpha', this.alpha);
    const mat = new THREE.ShaderMaterial({
      vertexShader: VERT,
      fragmentShader: FRAG,
      uniforms: { uMap: { value: foamTexture() }, uColor: { value: new THREE.Color(0xffffff) } },
      transparent: true,
      depthWrite: false,
      side: THREE.DoubleSide,
      polygonOffset: true,
      polygonOffsetFactor: -2,
      polygonOffsetUnits: -2,
    });
    this.mesh = new THREE.InstancedMesh(geo, mat, COUNT);
    this.mesh.frustumCulled = false;
    this.mesh.renderOrder = 2;
    for (let i = 0; i < COUNT; i++) {
      const p: Patch = { x: 0, z: 0, age: 0, life: 1, len: 1, yaw: 0 };
      this.patches.push(p);
      this.spawn(p, 0, SHORE_Z + 60, true);
    }
  }

  /** Puts a patch somewhere in the sea near the player (or near a given spot at the start). */
  private spawn(p: Patch, px: number, pz: number, scatterAge = false): void {
    for (let tries = 0; tries < 8; tries++) {
      const a = Math.random() * Math.PI * 2;
      const r = Math.sqrt(Math.random()) * RADIUS;
      p.x = px + Math.cos(a) * r;
      p.z = pz + Math.sin(a) * r;
      if (p.z > SHORE_Z + 4) break; // only open water
    }
    if (p.z <= SHORE_Z + 4) p.z = SHORE_Z + 6 + Math.random() * 80;
    p.life = 7 + Math.random() * 9;
    p.age = scatterAge ? Math.random() * p.life : 0;
    p.len = 5 + Math.random() * 11;
    // the streaks run roughly along the shore, as swell does
    p.yaw = (Math.random() - 0.5) * 0.6;
  }

  update(dt: number, level: number, px: number, pz: number, night: number, swell: (x: number, z: number) => number): void {
    const drift = 0.35;
    for (let i = 0; i < COUNT; i++) {
      const p = this.patches[i];
      p.age += dt;
      p.x += drift * dt;
      p.z += 0.12 * dt;
      const far = Math.hypot(p.x - px, p.z - pz) > RADIUS * 1.15;
      if (p.age >= p.life || far) this.spawn(p, px, pz);
      const u = p.age / p.life;
      // swell up quickly, hold, then melt away
      const a = Math.sin(Math.PI * Math.min(1, u)) ** 0.7;
      this.alpha.array[i] = a * 0.8;
      const grow = 0.75 + 0.35 * u;
      this.dummy.position.set(p.x, level + swell(p.x, p.z) + 0.07, p.z);
      this.dummy.rotation.set(0, p.yaw, 0);
      this.dummy.scale.set(p.len * grow, 1, p.len * grow * 0.34);
      this.dummy.updateMatrix();
      this.mesh.setMatrixAt(i, this.dummy.matrix);
    }
    this.mesh.instanceMatrix.needsUpdate = true;
    this.alpha.needsUpdate = true;
    // foam looks grey-blue in the dark
    this.color.set(0xffffff).lerp(new THREE.Color(0x6f86a8), night * 0.8);
    (this.mesh.material as THREE.ShaderMaterial).uniforms.uColor.value.copy(this.color);
  }
}
