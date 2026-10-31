import * as THREE from 'three';
import { Beach, HALF, WATER_LEVEL } from './Beach';

const BEACH_W = HALF * 2;

const foamTexture = (): THREE.CanvasTexture => {
  const c = document.createElement('canvas');
  c.width = 256;
  c.height = 64;
  const g = c.getContext('2d')!;
  const grad = g.createLinearGradient(0, 0, 0, 64);
  grad.addColorStop(0, 'rgba(255,255,255,0.95)');
  grad.addColorStop(0.35, 'rgba(255,255,255,0.55)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 256, 64);
  g.globalCompositeOperation = 'destination-out';
  for (let i = 0; i < 90; i++) {
    g.fillStyle = `rgba(0,0,0,${0.2 + Math.random() * 0.5})`;
    g.beginPath();
    g.arc(Math.random() * 256, 8 + Math.random() * 56, 2 + Math.random() * 6, 0, Math.PI * 2);
    g.fill();
  }
  const t = new THREE.CanvasTexture(c);
  t.wrapS = THREE.RepeatWrapping;
  t.repeat.set(6, 1);
  return t;
};

/**
 * The swell: three long waves rolling in toward the shore. The shader and `swellAt` below must agree, so that what floats rides the water.
 * Each wave is [amplitude, kx, kz, speed].
 */
const SWELL: [number, number, number, number][] = [
  [0.1, 0.1, 0.16, 0.9],
  [0.07, -0.17, 0.23, 1.2],
  [0.045, 0.31, 0.21, 1.6],
];

const SWELL_GLSL = /* glsl */ `
  const int SWELLS = 3;
  const vec4 uSwell[SWELLS] = vec4[SWELLS](vec4(0.1, 0.1, 0.16, 0.9), vec4(0.07, -0.17, 0.23, 1.2), vec4(0.045, 0.31, 0.21, 1.6));
  float swellHeight(vec2 p, float t) {
    float h = 0.0;
    for (int i = 0; i < SWELLS; i++) h += uSwell[i].x * sin(dot(p, uSwell[i].yz) + t * uSwell[i].w);
    return h;
  }
  vec2 swellSlope(vec2 p, float t) {
    vec2 g = vec2(0.0);
    for (int i = 0; i < SWELLS; i++) g += uSwell[i].x * uSwell[i].yz * cos(dot(p, uSwell[i].yz) + t * uSwell[i].w);
    return g;
  }
`;

/** A tiling field of soft bumps: its slopes in red and green, its height in blue. The water scrolls this past itself to make ripples. */
const rippleTexture = (): THREE.DataTexture => {
  const N = 256;
  const lattice = (cells: number, seed: number): ((x: number, y: number) => number) => {
    const rnd = (i: number, j: number): number => {
      const v = Math.sin(((i + cells) % cells) * 127.1 + ((j + cells) % cells) * 311.7 + seed) * 43758.5453;
      return v - Math.floor(v);
    };
    const fade = (u: number): number => u * u * (3 - 2 * u);
    return (x, y) => {
      const fx = x * cells;
      const fy = y * cells;
      const i = Math.floor(fx);
      const j = Math.floor(fy);
      const u = fade(fx - i);
      const w = fade(fy - j);
      const a = rnd(i, j) + (rnd(i + 1, j) - rnd(i, j)) * u;
      const b = rnd(i, j + 1) + (rnd(i + 1, j + 1) - rnd(i, j + 1)) * u;
      return a + (b - a) * w;
    };
  };
  const layers = [lattice(5, 1.3), lattice(11, 4.7), lattice(23, 9.1), lattice(47, 2.2)];
  const weight = [1, 0.5, 0.25, 0.12];
  const h = new Float32Array(N * N);
  let lo = Infinity;
  let hi = -Infinity;
  for (let j = 0; j < N; j++) {
    for (let i = 0; i < N; i++) {
      let v = 0;
      layers.forEach((f, k) => (v += f(i / N, j / N) * weight[k]));
      h[j * N + i] = v;
      lo = Math.min(lo, v);
      hi = Math.max(hi, v);
    }
  }
  const data = new Uint8Array(N * N * 4);
  const at = (i: number, j: number): number => h[((j + N) % N) * N + ((i + N) % N)];
  for (let j = 0; j < N; j++) {
    for (let i = 0; i < N; i++) {
      const dx = (at(i + 1, j) - at(i - 1, j)) * 6;
      const dy = (at(i, j + 1) - at(i, j - 1)) * 6;
      const o = (j * N + i) * 4;
      data[o] = Math.max(0, Math.min(255, (dx * 0.5 + 0.5) * 255));
      data[o + 1] = Math.max(0, Math.min(255, (dy * 0.5 + 0.5) * 255));
      data[o + 2] = ((h[j * N + i] - lo) / (hi - lo)) * 255;
      data[o + 3] = 255;
    }
  }
  const t = new THREE.DataTexture(data, N, N, THREE.RGBAFormat);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.magFilter = THREE.LinearFilter;
  t.minFilter = THREE.LinearMipmapLinearFilter;
  t.generateMipmaps = true;
  t.anisotropy = 4;
  t.needsUpdate = true;
  return t;
};

export class Ocean {
  readonly group = new THREE.Group();
  private water: THREE.Mesh;
  private time = { value: 0 };
  private foam: THREE.Mesh;
  private foamTex: THREE.CanvasTexture;
  private foamPos: THREE.BufferAttribute;
  private level = WATER_LEVEL;

  /** current height of the water surface */
  get surfaceY(): number {
    return this.level;
  }
  private foamCols = 300;

  /** The swell alone, without the tide, at a point on the open sea. */
  swellAt(x: number, z: number, t = this.time.value): number {
    let h = 0;
    for (const [a, kx, kz, w] of SWELL) h += a * Math.sin(x * kx + z * kz + t * w);
    return h;
  }

  /** The height of the water at a point, swell included. The swell dies away in the shallows. */
  heightAt(x: number, z: number): number {
    const depth = WATER_LEVEL - this.beach.heightAt(x, z);
    const k = Math.min(1, Math.max(0, (depth - 0.05) / 0.65));
    return this.level + this.swellAt(x, z) * k * k * (3 - 2 * k);
  }

  constructor(private beach: Beach) {
    const wg = new THREE.PlaneGeometry(900, 420, 240, 140);
    wg.rotateX(-Math.PI / 2);
    // how deep the sea is under each point, measured once: the colour, the clearness and the swell all follow the real shape of the sea bed
    const pos = wg.attributes.position as THREE.BufferAttribute;
    const depth = new Float32Array(pos.count);
    for (let i = 0; i < pos.count; i++) depth[i] = WATER_LEVEL - beach.heightAt(pos.getX(i), pos.getZ(i) + 220);
    wg.setAttribute('aDepth', new THREE.BufferAttribute(depth, 1));
    const mat = new THREE.MeshStandardMaterial({
      color: 0x1573c4,
      emissive: 0x0a3a78,
      emissiveIntensity: 0.55,
      roughness: 0.22,
      metalness: 0.15,
      transparent: true,
    });
    const ripple = rippleTexture();
    mat.onBeforeCompile = (sh) => {
      sh.uniforms.uTime = this.time;
      sh.uniforms.uRipple = { value: ripple };
      sh.uniforms.uShallow = { value: new THREE.Color(0x1aa0b6) };
      sh.uniforms.uMid = { value: new THREE.Color(0x1573c4) };
      sh.uniforms.uDeep = { value: new THREE.Color(0x0e55aa) };
      sh.vertexShader = sh.vertexShader
        .replace(
          '#include <common>',
          `#include <common>
          attribute float aDepth;
          uniform float uTime;
          varying vec3 vSea;
          varying float vSeaDepth;
          ${SWELL_GLSL}`,
        )
        .replace(
          '#include <begin_vertex>',
          `#include <begin_vertex>
          vec2 seaAt = (modelMatrix * vec4(position, 1.0)).xz;
          float seaLift = swellHeight(seaAt, uTime) * smoothstep(0.05, 0.7, aDepth);
          transformed.y += seaLift;
          vSea = vec3(seaAt.x, seaLift, seaAt.y);
          vSeaDepth = aDepth;`,
        );
      sh.fragmentShader = sh.fragmentShader
        .replace(
          '#include <common>',
          `#include <common>
          uniform float uTime;
          uniform sampler2D uRipple;
          uniform vec3 uShallow;
          uniform vec3 uMid;
          uniform vec3 uDeep;
          varying vec3 vSea;
          varying float vSeaDepth;
          vec3 seaFine;
          vec3 seaWide;
          ${SWELL_GLSL}`,
        )
        .replace(
          '#include <color_fragment>',
          `#include <color_fragment>
          // ripples at three sizes, drifting different ways so they never read as a repeating pattern
          vec2 seaP = vSea.xz;
          seaWide = texture2D(uRipple, seaP / 34.0 + uTime * vec2(0.006, -0.011)).xyz;
          vec3 seaMid = texture2D(uRipple, seaP / 11.0 + uTime * vec2(-0.017, -0.026)).xyz;
          seaFine = texture2D(uRipple, seaP / 4.1 + uTime * vec2(0.035, -0.052)).xyz;
          // turquoise over the sand, blue further out, darker over the deep, with slow patches of tone across it
          float seaD = clamp(vSeaDepth, 0.0, 40.0);
          vec3 seaColor = mix(uShallow, uMid, smoothstep(0.02, 0.7, seaD));
          seaColor = mix(seaColor, uDeep, smoothstep(0.8, 4.0, seaD) * 0.85);
          seaColor *= 0.72 + 0.3 * seaWide.z + 0.2 * seaMid.z + 0.08 * seaFine.z;
          seaColor += vec3(0.05, 0.09, 0.11) * smoothstep(0.0, 0.2, vSea.y);
          // thin threads of foam where the ripples pile up on the crests of the swell
          float seaFoam = smoothstep(0.74, 0.84, seaMid.z * 0.62 + seaFine.z * 0.32 + vSea.y * 0.5 + 0.04);
          seaFoam *= smoothstep(0.3, 0.9, seaD) * 0.3;
          diffuseColor.rgb = mix(seaColor, vec3(0.92, 0.96, 0.98), seaFoam);
          diffuseColor.a = mix(0.8, 0.96, smoothstep(0.0, 0.3, seaD));`,
        )
        .replace(
          '#include <normal_fragment_maps>',
          `#include <normal_fragment_maps>
          vec2 seaTilt = swellSlope(vSea.xz, uTime) * smoothstep(0.05, 0.7, vSeaDepth);
          seaTilt += (seaWide.xy - 0.5) * 0.18 + (seaMid.xy - 0.5) * 0.24 + (seaFine.xy - 0.5) * 0.2;
          normal = normalize((viewMatrix * vec4(normalize(vec3(-seaTilt.x, 1.0, -seaTilt.y)), 0.0)).xyz);`,
        )
        .replace(
          '#include <lights_fragment_end>',
          `#include <lights_fragment_end>
          // keep the sun's reflection a glitter on the water rather than a white sheet across it
          reflectedLight.directSpecular *= 0.3;`,
        )
        .replace(
          '#include <fog_fragment>',
          `// the haze sits over the land: the sea keeps its colour far longer than the beach does, so it still reads as water from the camp,
          // and only melts into the sky toward the horizon
          #ifdef USE_FOG
            float seaHaze = max(fogDensity * 0.2, 0.004);
            seaHaze = 1.0 - exp(-seaHaze * seaHaze * vFogDepth * vFogDepth);
            gl_FragColor.rgb = mix(gl_FragColor.rgb, fogColor, seaHaze);
          #endif`,
        )
        .replace(
          '#include <opaque_fragment>',
          `// the sea takes on the colour of the sky toward the horizon
          float seaGraze = pow(1.0 - max(dot(normal, normalize(vViewPosition)), 0.0), 5.0);
          outgoingLight = mix(outgoingLight, fogColor, seaGraze * 0.14);
          #include <opaque_fragment>`,
        );
    };
    this.water = new THREE.Mesh(wg, mat);
    this.water.frustumCulled = false;
    this.water.position.set(0, WATER_LEVEL, 220);
    this.water.receiveShadow = true;
    this.group.add(this.water);

    // Foam strip hugging the contour where the water meets the sand.
    const fg = new THREE.PlaneGeometry(BEACH_W, 1, this.foamCols, 1);
    this.foamPos = fg.attributes.position as THREE.BufferAttribute;
    this.foamTex = foamTexture();
    this.foam = new THREE.Mesh(
      fg,
      new THREE.MeshBasicMaterial({ map: this.foamTex, transparent: true, depthWrite: false, opacity: 0.9, side: THREE.DoubleSide }),
    );
    this.foam.renderOrder = 2;
    this.foam.frustumCulled = false;
    this.group.add(this.foam);

    this.update(0);
  }

  /** The water's own blue glow would look odd at night, so it fades with the dark (0 day .. 1 night). */
  setNight(n: number): void {
    (this.water.material as THREE.MeshStandardMaterial).emissiveIntensity = 0.55 * (1 - 0.85 * n);
  }

  update(t: number): void {
    // slow tidal breathing; the waves and ripples themselves are drawn by the shader
    this.level = WATER_LEVEL + Math.sin(t * 0.9) * 0.05 + Math.sin(t * 0.37) * 0.03;
    this.water.position.y = this.level;
    this.time.value = t;

    // foam band follows the shoreline contour
    const surge = 0.5 + 0.5 * Math.sin(t * 0.9 + 1.1);
    const width = 0.6 + surge * 1.6;
    for (let i = 0; i <= this.foamCols; i++) {
      const x = -HALF + (i / this.foamCols) * HALF * 2;
      const z0 = this.beach.shoreZ(x, this.level);
      const wob = Math.sin(x * 0.6 + t * 1.4) * 0.18;
      this.foamPos.setXYZ(i, x, this.level + 0.02, z0 + wob);
      this.foamPos.setXYZ(i + this.foamCols + 1, x, this.level + 0.02, z0 + wob + width);
    }
    this.foamPos.needsUpdate = true;
    this.foamTex.offset.x = t * 0.02;
    (this.foam.material as THREE.MeshBasicMaterial).opacity = 0.55 + surge * 0.4;
  }
}
