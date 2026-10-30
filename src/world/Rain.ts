import * as THREE from 'three';

const DROPS = 1600;
const BOX = 15;
const HEIGHT = 18;

/**
 * Whether a day has a rainy spell, and when in the day it falls. Rain is rare: about one day in seven,
 * never on the first day, and it always arrives and leaves by the same hash so a saved game agrees.
 */
export const rainWindow = (day: number): { start: number; end: number } | null => {
  if (day <= 1) return null;
  const h = Math.sin(day * 127.1 + 311.7) * 43758.5453;
  const r = h - Math.floor(h);
  if (r > 0.14) return null;
  const s = Math.sin(day * 269.5 + 183.3) * 43758.5453;
  const start = 0.08 + (s - Math.floor(s)) * 0.32;
  return { start, end: start + 0.26 };
};

/** Rain streaks that stay around the camera, plus the smoothed 0..1 amount of rain. */
export class Rain {
  readonly group = new THREE.Group();
  /** 0 dry .. 1 pouring */
  amount = 0;
  private lines: THREE.LineSegments;
  private pos: Float32Array;
  private seeds: Float32Array;
  private mat = new THREE.LineBasicMaterial({ color: 0xcfe0ee, transparent: true, opacity: 0, depthWrite: false, fog: false });

  constructor() {
    this.pos = new Float32Array(DROPS * 6);
    this.seeds = new Float32Array(DROPS * 3);
    for (let i = 0; i < DROPS; i++) {
      this.seeds[i * 3] = (Math.random() * 2 - 1) * BOX;
      this.seeds[i * 3 + 1] = Math.random() * HEIGHT;
      this.seeds[i * 3 + 2] = (Math.random() * 2 - 1) * BOX;
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(this.pos, 3));
    this.lines = new THREE.LineSegments(geo, this.mat);
    this.lines.frustumCulled = false;
    this.lines.visible = false;
    this.group.add(this.lines);
  }

  /** Eases toward `target` and moves the drops. `cam` is where the rain should fall around. */
  update(dt: number, target: number, cam: THREE.Vector3): void {
    this.amount += (target - this.amount) * Math.min(1, dt * 0.35);
    if (Math.abs(this.amount - target) < 0.002) this.amount = target;
    const on = this.amount > 0.01;
    this.lines.visible = on;
    if (!on) return;
    this.mat.opacity = this.amount * 0.5;
    const fall = 22;
    const slant = 3;
    for (let i = 0; i < DROPS; i++) {
      let y = this.seeds[i * 3 + 1] - fall * dt;
      let x = this.seeds[i * 3] - slant * dt;
      if (y < 0) {
        y += HEIGHT;
        x = (Math.random() * 2 - 1) * BOX;
        this.seeds[i * 3 + 2] = (Math.random() * 2 - 1) * BOX;
      }
      if (x < -BOX) x += BOX * 2;
      this.seeds[i * 3] = x;
      this.seeds[i * 3 + 1] = y;
      const z = this.seeds[i * 3 + 2];
      const wx = cam.x + x;
      const wy = cam.y - 4 + y;
      const wz = cam.z + z;
      this.pos[i * 6] = wx;
      this.pos[i * 6 + 1] = wy;
      this.pos[i * 6 + 2] = wz;
      this.pos[i * 6 + 3] = wx + slant * 0.03;
      this.pos[i * 6 + 4] = wy + 0.7;
      this.pos[i * 6 + 5] = wz;
    }
    (this.lines.geometry.attributes.position as THREE.BufferAttribute).needsUpdate = true;
  }
}
