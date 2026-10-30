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

export class Ocean {
  readonly group = new THREE.Group();
  private water: THREE.Mesh;
  private waterBase: Float32Array;
  private foam: THREE.Mesh;
  private foamTex: THREE.CanvasTexture;
  private foamPos: THREE.BufferAttribute;
  private level = WATER_LEVEL;

  /** current height of the water surface */
  get surfaceY(): number {
    return this.level;
  }
  private foamCols = 300;

  constructor(private beach: Beach) {
    const wg = new THREE.PlaneGeometry(900, 420, 120, 70);
    wg.rotateX(-Math.PI / 2);
    this.waterBase = new Float32Array(wg.attributes.position.array);
    this.water = new THREE.Mesh(
      wg,
      new THREE.MeshStandardMaterial({
        color: 0x1573c4,
        emissive: 0x0a3a78,
        emissiveIntensity: 0.55,
        roughness: 0.2,
        metalness: 0.15,
        transparent: true,
        opacity: 0.93,
      }),
    );
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
    // slow tidal breathing, plus surface ripples
    this.level = WATER_LEVEL + Math.sin(t * 0.9) * 0.05 + Math.sin(t * 0.37) * 0.03;
    this.water.position.y = this.level;
    const p = this.water.geometry.attributes.position as THREE.BufferAttribute;
    for (let i = 0; i < p.count; i++) {
      const x = this.waterBase[i * 3];
      const z = this.waterBase[i * 3 + 2];
      p.setY(i, Math.sin(x * 0.35 + t * 1.3) * 0.03 + Math.sin(z * 0.5 - t * 1.7) * 0.04);
    }
    p.needsUpdate = true;
    this.water.geometry.computeVertexNormals();

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
