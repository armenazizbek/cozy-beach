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

/** how far the tide rises and falls from the middle */
const TIDE = 0.08;

// The sea surface follows the viewer: a grid centred on the camera, dense underfoot and stretching
// coarse toward the horizon. Detail sits where it is seen, instead of being spread evenly across water
// too far off (and too lost in the haze) to resolve it. One flat plane would need ~16x the triangles
// to hold this much detail near the shore.
const SEA_REACH = 560; // how far the surface reaches from the viewer, in world units
const SEA_GRID = 40; // cells from the centre to each edge, per axis; the grid is (2n+1) across
const SEA_FALLOFF = 1.5; // >1 packs the detail toward the viewer

/** A flat grid in the XZ plane, centred on the origin, whose spacing grows toward the edges. */
const buildSeaGrid = (): { geo: THREE.BufferGeometry; localX: Float32Array; localZ: Float32Array } => {
  const k = SEA_GRID;
  const side = 2 * k + 1;
  const n = side * side;
  const position = new Float32Array(n * 3);
  const normal = new Float32Array(n * 3);
  const uv = new Float32Array(n * 2);
  const localX = new Float32Array(n);
  const localZ = new Float32Array(n);
  // an index 0..2k maps to an offset that grows with its distance from the centre
  const at = (i: number): number => {
    const u = (i - k) / k;
    return SEA_REACH * Math.sign(u) * Math.abs(u) ** SEA_FALLOFF;
  };
  let v = 0;
  for (let iz = 0; iz < side; iz++) {
    const z = at(iz);
    for (let ix = 0; ix < side; ix++) {
      const x = at(ix);
      position[v * 3] = x;
      position[v * 3 + 2] = z;
      normal[v * 3 + 1] = 1;
      uv[v * 2] = ix / (side - 1);
      uv[v * 2 + 1] = iz / (side - 1);
      localX[v] = x;
      localZ[v] = z;
      v++;
    }
  }
  const index = new Uint16Array(2 * k * 2 * k * 6);
  let o = 0;
  for (let iz = 0; iz < 2 * k; iz++) {
    for (let ix = 0; ix < 2 * k; ix++) {
      const a = iz * side + ix;
      const b = a + 1;
      const c = a + side;
      const d = c + 1;
      // wound so the surface faces up (+y)
      index[o++] = a; index[o++] = c; index[o++] = b;
      index[o++] = b; index[o++] = c; index[o++] = d;
    }
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(position, 3));
  geo.setAttribute('normal', new THREE.BufferAttribute(normal, 3));
  geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  geo.setIndex(new THREE.BufferAttribute(index, 1));
  return { geo, localX, localZ };
};

export class Ocean {
  readonly group = new THREE.Group();
  private water: THREE.Mesh;
  private time = { value: 0 };
  private foam: THREE.Mesh;
  private foamTex: THREE.CanvasTexture;
  private foamUniform = { value: new THREE.Vector4() };
  private level = WATER_LEVEL;
  /** per-vertex local offsets of the sea grid, and the sea-bed depth under each (refreshed as the viewer moves) */
  private seaLocalX!: Float32Array;
  private seaLocalZ!: Float32Array;
  private seaDepth!: Float32Array;
  private seaDepthAttr!: THREE.BufferAttribute;
  private seaAt = { x: Infinity, z: Infinity };

  /** current height of the water surface */
  get surfaceY(): number {
    return this.level;
  }
  private foamCols = 300;
  /** the shoreline at the lowest and the highest the tide goes, worked out once; each frame reads between the two */
  private shoreLow = new Float32Array(this.foamCols + 1);
  private shoreHigh = new Float32Array(this.foamCols + 1);

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
    const { geo: wg, localX, localZ } = buildSeaGrid();
    this.seaLocalX = localX;
    this.seaLocalZ = localZ;
    // how deep the sea is under each point: the colour, the clearness and the swell all follow the sea bed.
    // the grid follows the viewer, so this is refreshed as it moves (see update) rather than measured once.
    this.seaDepth = new Float32Array(localX.length).fill(30);
    this.seaDepthAttr = new THREE.BufferAttribute(this.seaDepth, 1);
    this.seaDepthAttr.setUsage(THREE.DynamicDrawUsage);
    wg.setAttribute('aDepth', this.seaDepthAttr);
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
    this.water.position.set(0, WATER_LEVEL, 0); // moved to the viewer each frame in update()
    this.water.receiveShadow = true;
    this.group.add(this.water);

    for (let i = 0; i <= this.foamCols; i++) {
      const x = -HALF + (i / this.foamCols) * HALF * 2;
      this.shoreLow[i] = beach.shoreZ(x, WATER_LEVEL - TIDE);
      this.shoreHigh[i] = beach.shoreZ(x, WATER_LEVEL + TIDE);
    }
    // Foam strip hugging the contour where the water meets the sand.
    const fg = new THREE.PlaneGeometry(BEACH_W, 1, this.foamCols, 1);
    const foamPos = fg.attributes.position as THREE.BufferAttribute;
    const shoreSpan = new Float32Array(foamPos.count);
    for (let i = 0; i <= this.foamCols; i++) {
      const x = -HALF + (i / this.foamCols) * HALF * 2;
      const low = this.shoreLow[i];
      const span = this.shoreHigh[i] - low;
      foamPos.setXYZ(i, x, 0, low);
      foamPos.setXYZ(i + this.foamCols + 1, x, 1, low);
      shoreSpan[i] = span;
      shoreSpan[i + this.foamCols + 1] = span;
    }
    fg.setAttribute('aShoreSpan', new THREE.BufferAttribute(shoreSpan, 1));
    this.foamTex = foamTexture();
    const foamMat = new THREE.MeshBasicMaterial({ map: this.foamTex, transparent: true, depthWrite: false, opacity: 0.9, side: THREE.DoubleSide });
    foamMat.onBeforeCompile = (sh) => {
      sh.uniforms.uFoam = this.foamUniform;
      sh.vertexShader = sh.vertexShader
        .replace(
          '#include <common>',
          `#include <common>
          attribute float aShoreSpan;
          uniform vec4 uFoam;`,
        )
        .replace(
          '#include <begin_vertex>',
          `float foamZ = position.z + aShoreSpan * uFoam.y + sin(position.x * 0.6 + uFoam.x * 1.4) * 0.18 + position.y * uFoam.z;
          vec3 transformed = vec3(position.x, uFoam.w, foamZ);`,
        );
    };
    this.foam = new THREE.Mesh(fg, foamMat);
    this.foam.renderOrder = 2;
    this.foam.frustumCulled = false;
    this.group.add(this.foam);

    this.update(0);
  }

  /** A sheet of this same open sea, with the same waves and colours, for looking out at it from somewhere else. */
  seaSheet(width: number, length: number): THREE.Mesh {
    const geo = new THREE.PlaneGeometry(length, width, 70, 90);
    geo.rotateX(-Math.PI / 2);
    geo.setAttribute('aDepth', new THREE.BufferAttribute(new Float32Array(geo.attributes.position.count).fill(30), 1));
    return new THREE.Mesh(geo, this.water.material);
  }

  /** The water's own blue glow would look odd at night, so it fades with the dark (0 day .. 1 night). */
  setNight(n: number): void {
    (this.water.material as THREE.MeshStandardMaterial).emissiveIntensity = 0.55 * (1 - 0.85 * n);
  }

  update(t: number, camX = 0, camZ = 0): void {
    // slow tidal breathing; the waves and ripples themselves are drawn by the shader
    this.level = WATER_LEVEL + Math.sin(t * 0.9) * 0.05 + Math.sin(t * 0.37) * 0.03;
    this.water.position.y = this.level;
    this.time.value = t;

    // the surface grid follows the viewer; the sea-bed depth under it is refreshed only when it has moved
    if (this.water.visible) {
      this.water.position.x = camX;
      this.water.position.z = camZ;
      if (Math.abs(camX - this.seaAt.x) > 0.5 || Math.abs(camZ - this.seaAt.z) > 0.5) {
        this.seaAt.x = camX;
        this.seaAt.z = camZ;
        const depth = this.seaDepth;
        const lx = this.seaLocalX;
        const lz = this.seaLocalZ;
        for (let i = 0; i < depth.length; i++) depth[i] = WATER_LEVEL - this.beach.heightAt(lx[i] + camX, lz[i] + camZ);
        this.seaDepthAttr.needsUpdate = true;
      }
    }

    // foam band follows the shoreline contour in the vertex shader
    const surge = 0.5 + 0.5 * Math.sin(t * 0.9 + 1.1);
    const width = 0.6 + surge * 1.6;
    const tide = (this.level - WATER_LEVEL + TIDE) / (TIDE * 2);
    this.foamUniform.value.set(t, tide, width, this.level + 0.02);
    this.foamTex.offset.x = t * 0.02;
    (this.foam.material as THREE.MeshBasicMaterial).opacity = 0.55 + surge * 0.4;
  }
}
