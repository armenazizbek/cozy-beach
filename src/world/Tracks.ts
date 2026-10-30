import * as THREE from 'three';
import { Beach, WATER_LEVEL } from './Beach';

const FOOT_MAX = 180;
const TIRE_MAX = 480;
/** a trail only keeps its newest marks; older ones fade away quickly */
const FOOT_TRAIL = 22;
const TIRE_TRAIL = 150;
const FOOT_LIFE = 18;
const TIRE_LIFE = 14;

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
    float far = 1.0 - smoothstep(30.0, 55.0, vDepth);
    float a = mask * vAlpha * far * 0.62;
    if (a < 0.01) discard;
    gl_FragColor = vec4(uColor, a);
  }
`;

const footTexture = (): THREE.CanvasTexture => {
  const c = document.createElement('canvas');
  c.width = 64;
  c.height = 128;
  const g = c.getContext('2d')!;
  g.fillStyle = '#fff';
  // toe pad, ball, gap, then the heel (a shoe sole seen from above, toe at the top)
  g.beginPath();
  g.ellipse(32, 38, 21, 32, 0, 0, Math.PI * 2);
  g.fill();
  g.beginPath();
  g.ellipse(32, 100, 15, 20, 0, 0, Math.PI * 2);
  g.fill();
  g.globalCompositeOperation = 'destination-out';
  g.fillRect(0, 72, 64, 7);
  for (let i = 0; i < 4; i++) g.fillRect(14, 22 + i * 12, 36, 3);
  return new THREE.CanvasTexture(c);
};

/** A dog's paw seen from above: four toes and a pad, toes at the top. */
const pawTexture = (): THREE.CanvasTexture => {
  const c = document.createElement('canvas');
  c.width = 64;
  c.height = 64;
  const g = c.getContext('2d')!;
  g.fillStyle = '#fff';
  g.beginPath();
  g.ellipse(32, 42, 17, 14, 0, 0, Math.PI * 2);
  g.fill();
  for (const [x, y, r] of [[12, 28, 0.35], [24, 14, 0.1], [40, 14, -0.1], [52, 28, -0.35]] as const) {
    g.beginPath();
    g.ellipse(x, y, 7, 10, r, 0, Math.PI * 2);
    g.fill();
  }
  return new THREE.CanvasTexture(c);
};

const treadTexture = (): THREE.CanvasTexture => {
  const c = document.createElement('canvas');
  c.width = 32;
  c.height = 128;
  const g = c.getContext('2d')!;
  g.fillStyle = '#fff';
  g.fillRect(2, 0, 28, 128);
  g.globalCompositeOperation = 'destination-out';
  for (let y = 4; y < 128; y += 10) g.fillRect(0, y, 32, 4);
  g.fillRect(14, 0, 4, 128);
  return new THREE.CanvasTexture(c);
};

class Decals {
  readonly mesh: THREE.InstancedMesh;
  private age: Float32Array;
  private seq: Float32Array;
  private seqNow = 0;
  private alpha: THREE.InstancedBufferAttribute;
  private next = 0;
  private m = new THREE.Matrix4();
  private q = new THREE.Quaternion();
  private basis = new THREE.Matrix4();

  constructor(private max: number, private life: number, private trail: number, texture: THREE.Texture) {
    const geo = new THREE.PlaneGeometry(1, 1);
    geo.rotateX(-Math.PI / 2);
    this.alpha = new THREE.InstancedBufferAttribute(new Float32Array(max), 1);
    geo.setAttribute('aAlpha', this.alpha);
    const mat = new THREE.ShaderMaterial({
      vertexShader: VERT,
      fragmentShader: FRAG,
      uniforms: { uMap: { value: texture }, uColor: { value: new THREE.Color(0x4a321e) } },
      transparent: true,
      depthWrite: false,
      side: THREE.DoubleSide,
      polygonOffset: true,
      polygonOffsetFactor: -2,
      polygonOffsetUnits: -2,
    });
    this.mesh = new THREE.InstancedMesh(geo, mat, max);
    this.mesh.frustumCulled = false;
    this.mesh.renderOrder = 1;
    this.age = new Float32Array(max).fill(1e9);
    this.seq = new Float32Array(max);
    // park every instance out of sight until it is used
    this.m.makeScale(0, 0, 0);
    for (let i = 0; i < max; i++) this.mesh.setMatrixAt(i, this.m);
  }

  /** Lays one mark on the ground at x,z. yaw is a three.js rotation.y; (w, l) are its size in metres. */
  put(beach: Beach, x: number, z: number, yaw: number, w: number, l: number, mirror = false): void {
    const e = 0.2;
    const h = beach.heightAt(x, z);
    if (h < WATER_LEVEL + 0.03) return;
    const nx = beach.heightAt(x - e, z) - beach.heightAt(x + e, z);
    const nz = beach.heightAt(x, z - e) - beach.heightAt(x, z + e);
    const n = new THREE.Vector3(nx, 2 * e, nz).normalize();
    if (n.y < 0.78) return; // too steep: the wall of a hole
    const f0 = new THREE.Vector3(-Math.sin(yaw), 0, -Math.cos(yaw));
    const f = f0.sub(n.clone().multiplyScalar(f0.dot(n))).normalize();
    const r = new THREE.Vector3().crossVectors(f, n);
    this.basis.makeBasis(r, n, f.clone().negate());
    this.q.setFromRotationMatrix(this.basis);
    this.m.compose(new THREE.Vector3(x, h + 0.025, z), this.q, new THREE.Vector3(mirror ? -w : w, 1, l));
    const i = this.next;
    this.next = (this.next + 1) % this.max;
    this.mesh.setMatrixAt(i, this.m);
    this.mesh.instanceMatrix.needsUpdate = true;
    this.age[i] = 0;
    this.seq[i] = ++this.seqNow;
  }

  update(dt: number, strength: number): void {
    for (let i = 0; i < this.max; i++) {
      if (this.age[i] > this.life) {
        this.alpha.array[i] = 0;
        continue;
      }
      // marks beyond the newest `trail` ones age much faster, so a long walk never leaves a permanent road
      this.age[i] += this.seqNow - this.seq[i] > this.trail ? dt * 5 : dt;
      const t = Math.max(0, this.age[i] / this.life);
      // visible for most of its life, then it smooths away
      this.alpha.array[i] = Math.max(0, t < 0.55 ? 1 : 1 - (t - 0.55) / 0.45) * strength;
    }
    this.alpha.needsUpdate = true;
  }
}

/** Footprints and tyre tracks that stay in the sand for a while and then fade out. */
export class Tracks {
  readonly group = new THREE.Group();
  private feet: Decals;
  private tires: Decals;
  private paws: Decals;
  private stepLeft = false;

  constructor(private beach: Beach) {
    this.feet = new Decals(FOOT_MAX, FOOT_LIFE, FOOT_TRAIL, footTexture());
    this.tires = new Decals(TIRE_MAX, TIRE_LIFE, TIRE_TRAIL, treadTexture());
    this.paws = new Decals(260, 24, 120, pawTexture());
    this.group.add(this.feet.mesh, this.tires.mesh, this.paws.mesh);
  }

  /** One step by the player; alternates left and right feet. */
  step(x: number, z: number, yaw: number): void {
    this.stepLeft = !this.stepLeft;
    const side = this.stepLeft ? -1 : 1;
    const px = x + Math.cos(yaw) * 0.14 * side;
    const pz = z - Math.sin(yaw) * 0.14 * side;
    this.feet.put(this.beach, px, pz, yaw, 0.13, 0.3, this.stepLeft);
  }

  /** One paw print by the dog. */
  paw(x: number, z: number, yaw: number, left: boolean): void {
    this.paws.put(this.beach, x, z, yaw, 0.1, 0.11, left);
  }

  tire(x: number, z: number, yaw: number): void {
    this.tires.put(this.beach, x, z, yaw, 0.24, 0.6);
  }

  /** `night` (0..1) makes the marks a little fainter in the dark. */
  update(dt: number, night: number): void {
    const s = 1 - 0.45 * night;
    this.feet.update(dt, s);
    this.tires.update(dt, s);
    this.paws.update(dt, s);
  }
}
