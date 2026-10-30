import * as THREE from 'three';

export type SkyMode = 'golden' | 'day' | 'sunset' | 'night';

interface Preset {
  top: number;
  bottom: number;
  fog: number;
  fogDensity: number;
  sun: number;
  sunIntensity: number;
  sunPos: [number, number, number];
  hemiSky: number;
  hemiGround: number;
  hemiIntensity: number;
  exposure: number;
  stars: number;
  fire: number;
}

const PRESETS: Record<SkyMode, Preset> = {
  golden: {
    top: 0x8fb6c9, bottom: 0xffd9a0, fog: 0xf3d9b8, fogDensity: 0.018, sun: 0xffc27a, sunIntensity: 1.9,
    sunPos: [-14, 13, -22], hemiSky: 0xcfe3ee, hemiGround: 0xc9a27a, hemiIntensity: 0.55, exposure: 0.85, stars: 0, fire: 0.6,
  },
  day: {
    top: 0x5fa8e0, bottom: 0xcfeaf5, fog: 0xece9df, fogDensity: 0.0042, sun: 0xfff4de, sunIntensity: 3.2,
    sunPos: [10, 32, 6], hemiSky: 0xcfe8ff, hemiGround: 0xe2cfa8, hemiIntensity: 1.1, exposure: 1.0, stars: 0, fire: 0.3,
  },
  sunset: {
    top: 0x4a4a8a, bottom: 0xff8a5c, fog: 0xe9a38a, fogDensity: 0.02, sun: 0xff7a3d, sunIntensity: 2.2,
    sunPos: [-6, 5, 34], hemiSky: 0xb99ac8, hemiGround: 0xa5694e, hemiIntensity: 0.7, exposure: 0.95, stars: 0.2, fire: 1.0,
  },
  night: {
    top: 0x070b24, bottom: 0x20305a, fog: 0x18223f, fogDensity: 0.022, sun: 0x8fa6ff, sunIntensity: 0.55,
    sunPos: [14, 24, 20], hemiSky: 0x3a4a85, hemiGround: 0x1b1b2e, hemiIntensity: 0.55, exposure: 1.0, stars: 1, fire: 1.8,
  },
};

/** One full day: morning, noon, sunset, dusk, night, and back to morning. */
const KEYS: { t: number; p: SkyMode }[] = [
  { t: 0, p: 'golden' },
  { t: 0.28, p: 'day' },
  { t: 0.56, p: 'sunset' },
  { t: 0.67, p: 'night' },
  { t: 0.92, p: 'night' },
  { t: 1, p: 'golden' },
];

/** The sun and moon sit just inside the sky dome, beyond the distant mountains, so peaks pass in front of them. */
const SKY_BODY_DIST = 296;

const smoothstep = (a: number, b: number, x: number): number => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

const moonTexture = (): THREE.CanvasTexture => {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const g = c.getContext('2d')!;
  const grad = g.createRadialGradient(64, 64, 0, 64, 64, 62);
  grad.addColorStop(0, '#f6f8ff');
  grad.addColorStop(0.85, '#dfe6f6');
  grad.addColorStop(0.95, 'rgba(223,230,246,0.6)');
  grad.addColorStop(1, 'rgba(223,230,246,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 128, 128);
  // a few soft craters
  for (const [x, y, r] of [[48, 48, 12], [78, 70, 16], [60, 88, 8], [86, 42, 7], [38, 78, 6]]) {
    const cg = g.createRadialGradient(x, y, 0, x, y, r);
    cg.addColorStop(0, 'rgba(150,160,185,0.55)');
    cg.addColorStop(1, 'rgba(150,160,185,0)');
    g.fillStyle = cg;
    g.fillRect(x - r, y - r, r * 2, r * 2);
  }
  return new THREE.CanvasTexture(c);
};

const SKY_VERT = /* glsl */ `
  varying vec3 vDir;
  void main() {
    vDir = normalize(position);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }
`;
const SKY_FRAG = /* glsl */ `
  uniform vec3 uTop;
  uniform vec3 uBottom;
  varying vec3 vDir;
  void main() {
    float h = clamp(vDir.y * 1.6 + 0.12, 0.0, 1.0);
    gl_FragColor = vec4(mix(uBottom, uTop, pow(h, 0.7)), 1.0);
  }
`;

const glowTexture = (): THREE.CanvasTexture => {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const g = c.getContext('2d')!;
  const grad = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.2, 'rgba(255,255,255,0.7)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 128, 128);
  return new THREE.CanvasTexture(c);
};

const puffTexture = (seed: number): THREE.CanvasTexture => {
  const c = document.createElement('canvas');
  c.width = 256;
  c.height = 128;
  const g = c.getContext('2d')!;
  let s = seed;
  const r = (): number => ((s = (s * 16807) % 2147483647) / 2147483647);
  for (let i = 0; i < 16; i++) {
    const x = 50 + r() * 156;
    const y = 50 + r() * 40 - Math.abs(x - 128) * 0.15;
    const rad = 22 + r() * 30;
    const grad = g.createRadialGradient(x, y, 0, x, y, rad);
    grad.addColorStop(0, 'rgba(255,255,255,0.55)');
    grad.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = grad;
    g.fillRect(x - rad, y - rad, rad * 2, rad * 2);
  }
  return new THREE.CanvasTexture(c);
};

const discTexture = (): THREE.CanvasTexture => {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const g = c.getContext('2d')!;
  const grad = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.62, 'rgba(255,255,255,1)');
  grad.addColorStop(0.72, 'rgba(255,255,255,0.35)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 128, 128);
  return new THREE.CanvasTexture(c);
};

interface Cloud {
  group: THREE.Group;
  speed: number;
}

/** Sky dome, sun, hemisphere light, fog, with smooth cross-fades between lighting moods. */
export class Sky {
  readonly sun: THREE.DirectionalLight;
  readonly hemi: THREE.HemisphereLight;
  readonly fog: THREE.FogExp2;
  /** 0.3..1.8 multiplier for the campfire/lantern lights */
  fireBoost = 0.6;

  private dome: THREE.Mesh;
  private stars: THREE.Points;
  private glow: THREE.Sprite;
  private core: THREE.Sprite;
  private clouds: Cloud[] = [];
  private cloudMats: THREE.SpriteMaterial[] = [];
  private cloudTint = new THREE.Color();
  /** the sun's shadow box follows this point (the player) */
  readonly focus = new THREE.Vector3();
  private uniforms = { uTop: { value: new THREE.Color() }, uBottom: { value: new THREE.Color() } };
  private tmp = new THREE.Color();
  private tmpB = new THREE.Color();
  private moonGlow: THREE.Sprite;
  private moonCore: THREE.Sprite;
  private sunPos = new THREE.Vector3();
  private moonPos = new THREE.Vector3();
  private lightPos = new THREE.Vector3();
  /** 0 in full daylight, 1 in deep night */
  starAmount = 0;
  /** time of day in [0,1) */
  time = 0;
  readonly group = new THREE.Group();

  constructor(private renderer: THREE.WebGLRenderer, scene: THREE.Scene) {
    this.fog = new THREE.FogExp2(PRESETS.golden.fog, PRESETS.golden.fogDensity);
    scene.fog = this.fog;

    this.dome = new THREE.Mesh(
      new THREE.SphereGeometry(300, 32, 16),
      new THREE.ShaderMaterial({
        vertexShader: SKY_VERT,
        fragmentShader: SKY_FRAG,
        uniforms: this.uniforms,
        side: THREE.BackSide,
        depthWrite: false,
        fog: false,
      }),
    );
    this.group.add(this.dome);

    const sp = new Float32Array(400 * 3);
    for (let i = 0; i < 400; i++) {
      const v = new THREE.Vector3(Math.random() * 2 - 1, Math.random() * 0.9 + 0.1, Math.random() * 2 - 1)
        .normalize()
        .multiplyScalar(280);
      sp.set([v.x, v.y, v.z], i * 3);
    }
    const sg = new THREE.BufferGeometry();
    sg.setAttribute('position', new THREE.BufferAttribute(sp, 3));
    this.stars = new THREE.Points(
      sg,
      new THREE.PointsMaterial({ color: 0xffffff, size: 1.6, sizeAttenuation: false, transparent: true, opacity: 0, fog: false }),
    );
    this.group.add(this.stars);

    this.glow = new THREE.Sprite(
      new THREE.SpriteMaterial({
        map: glowTexture(), color: 0xffd9a0, blending: THREE.AdditiveBlending, depthWrite: false, fog: false, transparent: true, toneMapped: false,
      }),
    );
    this.glow.scale.setScalar(165);
    this.group.add(this.glow);
    this.core = new THREE.Sprite(
      new THREE.SpriteMaterial({ map: discTexture(), color: 0xfff2c4, depthWrite: false, fog: false, transparent: true, toneMapped: false }),
    );
    this.core.scale.setScalar(33);
    this.group.add(this.core);
    this.moonGlow = new THREE.Sprite(
      new THREE.SpriteMaterial({
        map: glowTexture(), color: 0x9db4ff, blending: THREE.AdditiveBlending, depthWrite: false, fog: false, transparent: true,
      }),
    );
    this.moonGlow.scale.setScalar(105);
    this.moonCore = new THREE.Sprite(
      new THREE.SpriteMaterial({ map: moonTexture(), color: 0xffffff, depthWrite: false, fog: false, transparent: true, toneMapped: false }),
    );
    this.moonCore.scale.setScalar(30);
    this.group.add(this.moonGlow, this.moonCore);
    this.buildClouds();

    this.hemi = new THREE.HemisphereLight(0xcfe3ee, 0xc9a27a, 0.9);
    this.sun = new THREE.DirectionalLight(0xffc27a, 2.6);
    this.sun.castShadow = true;
    this.sun.shadow.mapSize.set(2048, 2048);
    const cam = this.sun.shadow.camera;
    cam.left = -26; cam.right = 26; cam.top = 26; cam.bottom = -26; cam.near = 1; cam.far = 120;
    this.sun.shadow.bias = -0.0004;
    this.sun.shadow.normalBias = 0.04;
    this.group.add(this.hemi, this.sun, this.sun.target);
    this.setTime(0);
  }

  private buildClouds(): void {
    for (let i = 0; i < 3; i++) {
      this.cloudMats.push(
        new THREE.SpriteMaterial({ map: puffTexture(11 + i * 97), transparent: true, depthWrite: false, fog: false, opacity: 0.92 }),
      );
    }
    for (let i = 0; i < 16; i++) {
      const g = new THREE.Group();
      const parts = 4 + Math.floor(Math.random() * 4);
      for (let k = 0; k < parts; k++) {
        const sp = new THREE.Sprite(this.cloudMats[(i + k) % 3]);
        sp.scale.set(70 + Math.random() * 60, 28 + Math.random() * 14, 1);
        sp.position.set((k - parts / 2) * 38 + Math.random() * 14, Math.random() * 8, Math.random() * 20);
        g.add(sp);
      }
      const ang = Math.random() * Math.PI * 2;
      const r = 120 + Math.random() * 150;
      g.position.set(Math.cos(ang) * r, 70 + Math.random() * 60, Math.sin(ang) * r);
      this.group.add(g);
      this.clouds.push({ group: g, speed: 1.2 + Math.random() * 2.2 });
    }
  }

  /** 0..1 how overcast and rainy it is; the next setTime applies it */
  rain = 0;

  /** Underwater, the sky, sun, moon and clouds are not there, and the fog is the water. */
  underwater = false;

  setUnderwater(on: boolean): void {
    this.underwater = on;
    for (const o of [this.dome, this.stars, this.glow, this.core, this.moonGlow, this.moonCore]) o.visible = !on;
    for (const c of this.clouds) c.group.visible = !on;
  }

  /** True once it is dark enough for lanterns and window lights. */
  get isDark(): boolean {
    return this.starAmount > 0.3;
  }

  update(dt: number): void {
    for (const c of this.clouds) {
      c.group.position.x += c.speed * dt;
      if (c.group.position.x > 320) c.group.position.x = -320;
    }
  }

  private presetAt(t: number): { a: Preset; b: Preset; k: number } {
    let i = 0;
    while (i < KEYS.length - 2 && t >= KEYS[i + 1].t) i++;
    const k0 = KEYS[i];
    const k1 = KEYS[i + 1];
    return { a: PRESETS[k0.p], b: PRESETS[k1.p], k: smoothstep(k0.t, k1.t, t) };
  }

  /** Sets the whole sky, lighting and fog for a time of day in [0,1). */
  setTime(time: number): void {
    const t = ((time % 1) + 1) % 1;
    this.time = t;
    const { a, b, k } = this.presetAt(t);
    const mixCol = (out: THREE.Color, ha: number, hb: number): THREE.Color => out.set(ha).lerp(this.tmpB.set(hb), k);
    const num = (x: number, y: number): number => x + (y - x) * k;

    this.uniforms.uTop.value.copy(mixCol(this.tmp, a.top, b.top));
    this.uniforms.uBottom.value.copy(mixCol(this.tmp, a.bottom, b.bottom));
    this.fog.color.copy(mixCol(this.tmp, a.fog, b.fog));
    this.fog.density = num(a.fogDensity, b.fogDensity);
    this.sun.color.copy(mixCol(this.tmp, a.sun, b.sun));
    this.sun.intensity = num(a.sunIntensity, b.sunIntensity);
    this.hemi.color.copy(mixCol(this.tmp, a.hemiSky, b.hemiSky));
    this.hemi.groundColor.copy(mixCol(this.tmp, a.hemiGround, b.hemiGround));
    this.hemi.intensity = num(a.hemiIntensity, b.hemiIntensity);
    this.renderer.toneMappingExposure = num(a.exposure, b.exposure);
    this.starAmount = num(a.stars, b.stars);
    (this.stars.material as THREE.PointsMaterial).opacity = this.starAmount;
    this.fireBoost = num(a.fire, b.fire);

    // the sun climbs from one side to the other; the moon does the same, over the sea
    const s = t / 0.62;
    const sa = Math.PI * (1 - Math.min(1.1, s));
    this.sunPos.set(Math.cos(sa) * 110, Math.sin(sa) * 85 + 2, -30);
    const u = Math.max(0, (t - 0.58) / 0.42);
    const ma = Math.PI * (1 - Math.min(1.1, u));
    this.moonPos.set(Math.cos(ma) * 110, Math.sin(ma) * 80 + 2, 55);

    // sunlight hands over to moonlight around dusk
    const w = smoothstep(0.56, 0.68, t);
    this.lightPos.copy(this.sunPos).lerp(this.moonPos, w);
    this.sun.position.copy(this.lightPos).add(this.focus);
    this.sun.target.position.copy(this.focus);

    const sunVis = Math.min(1, Math.max(0, this.sunPos.y / 14)) * (1 - w);
    this.glow.position.copy(this.sunPos).normalize().multiplyScalar(SKY_BODY_DIST);
    this.core.position.copy(this.glow.position);
    (this.core.material as THREE.SpriteMaterial).color.copy(this.sun.color).lerp(this.tmp.set(0xffffff), 0.55);
    (this.core.material as THREE.SpriteMaterial).opacity = sunVis;
    (this.glow.material as THREE.SpriteMaterial).color.copy(this.sun.color);
    (this.glow.material as THREE.SpriteMaterial).opacity = Math.min(1, this.sun.intensity / 2.2) * sunVis;

    const moonVis = Math.min(1, Math.max(0, this.moonPos.y / 14)) * smoothstep(0.6, 0.7, t) * (1 - smoothstep(0.93, 1, t));
    this.moonCore.position.copy(this.moonPos).normalize().multiplyScalar(SKY_BODY_DIST);
    this.moonGlow.position.copy(this.moonCore.position);
    (this.moonCore.material as THREE.SpriteMaterial).opacity = moonVis;
    (this.moonGlow.material as THREE.SpriteMaterial).opacity = moonVis * 0.55;

    // clouds pick up the haze colour and fade with the light
    const light = Math.max(this.sun.intensity * (1 - w), 0.55);
    this.cloudTint.copy(this.uniforms.uBottom.value).lerp(this.tmp.set(0xffffff), 0.55).multiplyScalar(Math.min(1, 0.22 + light / 2.4));
    this.cloudMats.forEach((m) => m.color.copy(this.cloudTint));

    // rain: grey, dim and close
    const r = this.rain;
    if (r > 0.001) {
      const grey = this.tmp.set(0x6f7882).multiplyScalar(0.35 + 0.65 * Math.min(1, this.sun.intensity / 2.2));
      this.uniforms.uTop.value.lerp(grey, r * 0.85);
      this.uniforms.uBottom.value.lerp(grey, r * 0.9);
      this.fog.color.lerp(grey, r * 0.9);
      this.fog.density *= 1 + r * 0.9;
      this.sun.intensity *= 1 - r * 0.78;
      this.hemi.intensity *= 1 - r * 0.18;
      this.fireBoost = Math.max(this.fireBoost, 0.6 + r * 0.5);
      const clear = 1 - r;
      (this.core.material as THREE.SpriteMaterial).opacity *= clear;
      (this.glow.material as THREE.SpriteMaterial).opacity *= clear;
      this.cloudMats.forEach((m) => {
        m.color.multiplyScalar(1 - r * 0.45);
        m.opacity = 0.92 + r * 0.08;
      });
    } else {
      this.cloudMats.forEach((m) => (m.opacity = 0.92));
    }
    if (this.underwater) {
      // the fog becomes the water: a clear blue-green that swallows everything past a few dozen metres, and dims at night
      const day = 0.28 + 0.72 * (1 - this.starAmount);
      this.fog.color.set(0x1b7aa6).multiplyScalar(day);
      this.fog.density = 0.011;
      this.hemi.color.set(0x9fdcf0);
      this.hemi.groundColor.set(0x3b7f8f);
      this.hemi.intensity = Math.max(this.hemi.intensity, 1.15) * (0.4 + 0.6 * day);
    }
  }
}
