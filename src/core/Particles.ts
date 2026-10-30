import * as THREE from 'three';

const VERT = /* glsl */ `
  attribute float aAlpha;
  attribute vec3 aColor;
  uniform float uSize;
  varying float vAlpha;
  varying vec3 vColor;
  void main() {
    vAlpha = aAlpha;
    vColor = aColor;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    gl_PointSize = uSize * (500.0 / max(0.1, -mv.z));
    gl_Position = projectionMatrix * mv;
  }
`;

const FRAG = /* glsl */ `
  varying float vAlpha;
  varying vec3 vColor;
  void main() {
    float d = length(gl_PointCoord - 0.5) * 2.0;
    if (d > 1.0) discard;
    float a = (1.0 - d * d) * vAlpha;
    gl_FragColor = vec4(vColor, a);
  }
`;

/** Same, but each particle is a little heart. */
const HEART_FRAG = /* glsl */ `
  varying float vAlpha;
  varying vec3 vColor;
  void main() {
    vec2 p = (gl_PointCoord - 0.5) * 2.0;
    float x = p.x * 1.25;
    float y = -p.y * 1.25 + 0.25;
    float f = pow(x * x + y * y - 1.0, 3.0) - x * x * y * y * y;
    if (f > 0.0) discard;
    float shade = 0.85 + 0.25 * clamp(-p.y * 0.5 + 0.3, 0.0, 1.0);
    gl_FragColor = vec4(vColor * shade, vAlpha);
  }
`;

/** Small CPU-simulated billboard particle pool. */
export class ParticleSystem {
  readonly points: THREE.Points;
  private pos: Float32Array;
  private col: Float32Array;
  private alpha: Float32Array;
  private vel: Float32Array;
  private life: Float32Array;
  private maxLife: Float32Array;
  private cursor = 0;

  constructor(
    private max: number,
    size: number,
    private gravity: number,
    additive = false,
    heart = false,
  ) {
    this.pos = new Float32Array(max * 3);
    this.col = new Float32Array(max * 3);
    this.alpha = new Float32Array(max);
    this.vel = new Float32Array(max * 3);
    this.life = new Float32Array(max);
    this.maxLife = new Float32Array(max).fill(1);
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(this.pos, 3));
    geo.setAttribute('aColor', new THREE.BufferAttribute(this.col, 3));
    geo.setAttribute('aAlpha', new THREE.BufferAttribute(this.alpha, 1));
    const mat = new THREE.ShaderMaterial({
      vertexShader: VERT,
      fragmentShader: heart ? HEART_FRAG : FRAG,
      uniforms: { uSize: { value: size } },
      transparent: true,
      depthWrite: false,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
    });
    this.points = new THREE.Points(geo, mat);
    this.points.frustumCulled = false;
    this.points.renderOrder = 10;
  }

  emit(p: THREE.Vector3, v: THREE.Vector3, life: number, color: THREE.Color): void {
    const i = this.cursor;
    this.cursor = (this.cursor + 1) % this.max;
    this.pos.set([p.x, p.y, p.z], i * 3);
    this.vel.set([v.x, v.y, v.z], i * 3);
    this.col.set([color.r, color.g, color.b], i * 3);
    this.life[i] = life;
    this.maxLife[i] = life;
  }

  burst(p: THREE.Vector3, count: number, speed: number, up: number, life: number, color: THREE.Color): void {
    const v = new THREE.Vector3();
    for (let n = 0; n < count; n++) {
      const a = Math.random() * Math.PI * 2;
      const s = speed * (0.3 + Math.random() * 0.7);
      v.set(Math.cos(a) * s, up * (0.5 + Math.random()), Math.sin(a) * s);
      this.emit(p, v, life * (0.6 + Math.random() * 0.6), color);
    }
  }

  update(dt: number): void {
    for (let i = 0; i < this.max; i++) {
      if (this.life[i] <= 0) {
        this.alpha[i] = 0;
        continue;
      }
      this.life[i] -= dt;
      const i3 = i * 3;
      this.vel[i3 + 1] -= this.gravity * dt;
      this.pos[i3] += this.vel[i3] * dt;
      this.pos[i3 + 1] += this.vel[i3 + 1] * dt;
      this.pos[i3 + 2] += this.vel[i3 + 2] * dt;
      this.alpha[i] = Math.max(0, this.life[i] / this.maxLife[i]);
    }
    const g = this.points.geometry;
    g.attributes.position.needsUpdate = true;
    g.attributes.aAlpha.needsUpdate = true;
    g.attributes.aColor.needsUpdate = true;
  }
}
