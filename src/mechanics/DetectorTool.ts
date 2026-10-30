import * as THREE from 'three';
import type { AudioManager, PingKind } from '../core/AudioManager';
import type { StateManager } from '../core/StateManager';
import type { LootSpawner } from '../world/LootSpawner';
import type { Beach } from '../world/Beach';
import { conformToTerrain } from '../world/conform';

export interface Signal {
  strength: number;
  kind: PingKind | null;
}

export class DetectorTool {
  readonly group = new THREE.Group();
  readonly signal: Signal = { strength: 0, kind: null };
  private coil: THREE.Mesh;
  private pulse: THREE.Mesh;
  private edge: THREE.Mesh;
  /** a dark band under each bright ring, so the rings stand out on pale sand as well as dark */
  private pulseDark: THREE.Mesh;
  private edgeDark: THREE.Mesh;
  private heat: THREE.Mesh;
  private heatCanvas = document.createElement('canvas');
  private heatTex: THREE.CanvasTexture;
  private pingTimer = 0;
  private pulseT = 0;
  private radius = 2;

  constructor() {
    const dark = new THREE.MeshStandardMaterial({ color: 0x3a3f45, roughness: 0.5, metalness: 0.6 });
    const brass = new THREE.MeshStandardMaterial({ color: 0xb08a4a, roughness: 0.4, metalness: 0.8 });

    this.coil = new THREE.Mesh(new THREE.CylinderGeometry(0.5, 0.5, 0.07, 28), dark);
    this.coil.castShadow = true;
    const hub = new THREE.Mesh(new THREE.TorusGeometry(0.5, 0.04, 8, 28), brass);
    hub.rotation.x = Math.PI / 2;
    this.coil.add(hub);
    const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.04, 2.6, 8), brass);
    shaft.position.set(0, 1.2, -1.0);
    shaft.rotation.x = 0.7;
    shaft.castShadow = true;
    const grip = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.07, 0.5, 10), new THREE.MeshStandardMaterial({ color: 0x3b2a1e, roughness: 0.9 }));
    grip.position.set(0, 2.3, -1.9);
    grip.rotation.x = 0.7;
    const arm = new THREE.Mesh(new THREE.TorusGeometry(0.22, 0.03, 6, 16, Math.PI), brass);
    arm.position.set(0, 2.1, -1.65);
    arm.rotation.set(0, 0, 0);
    this.group.add(this.coil, shaft, grip, arm);

    this.pulse = new THREE.Mesh(
      new THREE.RingGeometry(0.93, 1, 64).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0x9be37a, transparent: true, opacity: 0.7, side: THREE.DoubleSide, depthWrite: false }),
    );
    this.edge = new THREE.Mesh(
      new THREE.RingGeometry(0.98, 1, 64).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.8, side: THREE.DoubleSide, depthWrite: false }),
    );
    const darkRing = (inner: number, outer: number, opacity: number): THREE.Mesh => {
      const m = new THREE.Mesh(
        new THREE.RingGeometry(inner, outer, 64).rotateX(-Math.PI / 2),
        new THREE.MeshBasicMaterial({ color: 0x2b1a0c, transparent: true, opacity, side: THREE.DoubleSide, depthWrite: false }),
      );
      m.renderOrder = 4;
      return m;
    };
    this.pulseDark = darkRing(0.925, 1.02, 0.3);
    this.edgeDark = darkRing(0.972, 1.006, 0.4);
    this.heatCanvas.width = this.heatCanvas.height = 128;
    this.heatTex = new THREE.CanvasTexture(this.heatCanvas);
    this.heat = new THREE.Mesh(
      new THREE.RingGeometry(0.0001, 1, 48, 10).rotateX(-Math.PI / 2),
      new THREE.MeshBasicMaterial({ map: this.heatTex, transparent: true, depthWrite: false, side: THREE.DoubleSide, opacity: 0.9 }),
    );
    this.heat.renderOrder = 4;
    this.pulse.renderOrder = this.edge.renderOrder = 5;
  }

  /** The ground-hugging overlay objects live in the world, not the wand group. */
  get overlays(): THREE.Object3D[] {
    return [this.pulse, this.edge, this.heat, this.pulseDark, this.edgeDark];
  }

  setActive(on: boolean): void {
    this.group.visible = on;
    this.overlays.forEach((o) => (o.visible = on));
  }

  update(dt: number, hit: THREE.Vector3 | null, state: StateManager, spawner: LootSpawner, audio: AudioManager, beach: Beach): void {
    const tier = state.tier('detector');
    this.radius = state.detectorRadius;
    this.group.scale.setScalar(0.8 + tier * 0.12);
    this.coil.scale.set(1, 1, 1);

    if (!hit) {
      this.signal.strength = 0;
      this.signal.kind = null;
      this.overlays.forEach((o) => (o.visible = false));
      this.group.visible = false;
      return;
    }
    this.group.visible = true;
    this.pulse.visible = this.edge.visible = this.pulseDark.visible = this.edgeDark.visible = true;
    this.heat.visible = tier >= 3;

    this.group.position.set(hit.x, hit.y + 0.12, hit.z);
    for (const o of this.overlays) o.position.set(hit.x, hit.y, hit.z);
    this.edge.scale.set(this.radius, 1, this.radius);
    this.edgeDark.scale.set(this.radius, 1, this.radius);
    this.heat.scale.set(this.radius, 1, this.radius);

    const near = spawner.nearest(hit.x, hit.z, this.radius);
    if (near) {
      const s = 1 - near.d / this.radius;
      this.signal.strength = s;
      this.signal.kind = tier >= 2 ? near.item.def.signature : 'generic';
    } else {
      this.signal.strength = 0;
      this.signal.kind = null;
    }

    // radar pulse ring: faster and brighter the closer you get
    const s = this.signal.strength;
    this.pulseT += dt * (0.5 + s * 2.2);
    const ph = this.pulseT % 1;
    const pr = Math.max(0.05, ph * this.radius);
    this.pulse.scale.set(pr, 1, pr);
    this.pulseDark.scale.set(pr, 1, pr);
    // the rings and the heat map follow the ground, so hills and holes never cut them off
    conformToTerrain(this.pulse, beach, hit.x, hit.y, hit.z, 0.07);
    conformToTerrain(this.edge, beach, hit.x, hit.y, hit.z, 0.075);
    conformToTerrain(this.pulseDark, beach, hit.x, hit.y, hit.z, 0.066);
    conformToTerrain(this.edgeDark, beach, hit.x, hit.y, hit.z, 0.068);
    if (tier >= 3) conformToTerrain(this.heat, beach, hit.x, hit.y, hit.z, 0.1);
    const pm = this.pulse.material as THREE.MeshBasicMaterial;
    pm.opacity = (1 - ph) * (0.5 + s * 0.5);
    (this.pulseDark.material as THREE.MeshBasicMaterial).opacity = (1 - ph) * 0.3;
    // saturated colours that read against pale sand (the old pastels washed out)
    pm.color.set(this.signal.kind === 'trash' ? 0xff7a1a : this.signal.kind === 'deep' ? 0xa95cff : s > 0 ? 0xffc400 : 0x35d27a);

    audio.detectorStatic(dt, this.signal.strength, this.signal.kind);

    // beep: 800ms far, 90ms directly overhead
    if (near) {
      this.pingTimer -= dt * 1000;
      if (this.pingTimer <= 0) {
        this.pingTimer = 800 - (800 - 90) * Math.pow(s, 0.85);
        audio.ping(this.signal.kind ?? 'generic', s);
      }
    } else {
      this.pingTimer = Math.min(this.pingTimer, 0);
    }

    if (tier >= 3) this.drawHeat(hit, spawner);
  }

  private drawHeat(hit: THREE.Vector3, spawner: LootSpawner): void {
    const g = this.heatCanvas.getContext('2d')!;
    const S = 128;
    g.clearRect(0, 0, S, S);
    g.fillStyle = 'rgba(10,30,40,0.25)';
    g.beginPath();
    g.arc(S / 2, S / 2, S / 2, 0, Math.PI * 2);
    g.fill();
    for (const it of spawner.items) {
      if (it.revealed || it.collected) continue;
      const dx = it.x - hit.x;
      const dz = it.z - hit.z;
      const d = Math.hypot(dx, dz);
      if (d > this.radius) continue;
      const cx = S / 2 + (dx / this.radius) * (S / 2);
      const cy = S / 2 + (dz / this.radius) * (S / 2);
      const grad = g.createRadialGradient(cx, cy, 0, cx, cy, 20);
      const hot = it.def.signature === 'trash' ? '255,150,60' : it.def.signature === 'coin' ? '255,226,122' : '199,155,255';
      grad.addColorStop(0, `rgba(${hot},0.95)`);
      grad.addColorStop(1, `rgba(${hot},0)`);
      g.fillStyle = grad;
      g.fillRect(cx - 20, cy - 20, 40, 40);
    }
    this.heatTex.needsUpdate = true;
  }

  /** Live oscilloscope trace. */
  drawWaveform(ctx: CanvasRenderingContext2D, w: number, h: number, t: number, tier: number): void {
    ctx.fillStyle = '#0b1a14';
    ctx.fillRect(0, 0, w, h);
    ctx.strokeStyle = 'rgba(120,255,170,0.12)';
    ctx.lineWidth = 1;
    for (let x = 0; x < w; x += w / 10) {
      ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h); ctx.stroke();
    }
    for (let y = 0; y < h; y += h / 4) {
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke();
    }
    const { strength, kind } = this.signal;
    const mid = h / 2;
    const amp = kind ? 0.12 + strength * 0.85 : 0.04;
    const color =
      kind === 'trash' ? '#ff9a3c' : kind === 'coin' ? '#a8ee7a' : kind === 'deep' ? '#c79bff' : kind === 'generic' ? '#b8d4c0' : '#4a7a5e';
    ctx.strokeStyle = color;
    ctx.shadowColor = color;
    ctx.shadowBlur = 6;
    ctx.lineWidth = 2;
    ctx.beginPath();
    for (let x = 0; x <= w; x += 2) {
      const a = x / w * Math.PI * 6 + t * 7;
      let v: number;
      switch (kind) {
        case 'trash':
          v = (Math.random() * 2 - 1) * 0.8 + Math.sign(Math.sin(a * 2.3)) * 0.3;
          break;
        case 'coin':
          v = Math.sin(a);
          break;
        case 'deep':
          v = (Math.sin(a) + 0.6 * Math.sin(a * 2.01 + t * 2)) * (0.65 + 0.35 * Math.sin(t * 4)) * 0.75;
          break;
        case 'generic':
          v = Math.sin(a) * 0.8 + (Math.random() - 0.5) * 0.25;
          break;
        default:
          v = (Math.random() - 0.5) * 0.4;
      }
      const y = mid - v * amp * (h / 2 - 4);
      if (x === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.shadowBlur = 0;
    ctx.fillStyle = 'rgba(160,255,200,0.7)';
    ctx.font = '10px monospace';
    const label =
      tier < 2 && kind ? 'SIGNAL' : kind === 'trash' ? 'TRASH' : kind === 'coin' ? 'COIN / METAL' : kind === 'deep' ? 'DEEP ANTIQUE' : 'NO SIGNAL';
    ctx.fillText(label, 6, 12);
  }
}
