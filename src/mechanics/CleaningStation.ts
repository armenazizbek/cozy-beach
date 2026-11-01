import * as THREE from 'three';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { ParticleSystem } from '../core/Particles';
import type { BuriedItem } from '../world/LootSpawner';
import { buildItem, newCrustCanvas, wrapWithCrust } from './LootGenerator';
import type { CrustRig } from './LootGenerator';
import { ITEM_DEFS } from '../core/items';

export const CLEAN_THRESHOLD = 0.92;
const SAMPLE_EVERY_MS = 150;

type Phase = 'idle' | 'brushing' | 'finishing' | 'done';

/** Isolated macro workbench scene where items are brushed clean. */
export class CleaningStation {
  readonly scene = new THREE.Scene();
  readonly camera = new THREE.PerspectiveCamera(40, 1, 0.1, 50);
  readonly particles = new ParticleSystem(300, 0.07, 1.5, false);
  progress = 0;
  phase: Phase = 'idle';
  onProgress: (p: number) => void = () => undefined;
  onComplete: () => void = () => undefined;

  private pivot = new THREE.Group();
  /** where the item is turning to; the pivot eases toward it so dragging feels smooth */
  private target = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.9, 0.4, 0, 'YXZ'));
  private qa = new THREE.Quaternion();
  private axis = new THREE.Vector3();
  private rig: CrustRig | null = null;
  private item: BuriedItem | null = null;
  private brushPx = 16;
  /** a stick covers ground far more slowly than a mouse, so with a controller the brush is wider */
  brushScale = 1;
  private lastUV: THREE.Vector2 | null = null;
  private lastUVVec = new THREE.Vector2();
  private sampleCv = document.createElement('canvas');
  private sampleCtx: CanvasRenderingContext2D;
  private brushGrad: CanvasGradient;
  private sinceSample = 0;
  private raycaster = new THREE.Raycaster();
  private sparkles = new ParticleSystem(240, 0.12, -0.5, true);
  private dust = new THREE.Color(0xc9ae80);
  private gold = new THREE.Color(0xffe27a);
  private brushMesh: THREE.Group;
  private brushT = 0;
  private time = 0;
  private fadeAcc = 0;

  constructor(renderer: THREE.WebGLRenderer) {
    const pmrem = new THREE.PMREMGenerator(renderer);
    this.scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
    this.scene.environmentIntensity = 0.55;
    this.scene.background = new THREE.Color(0x2b2118);

    const bench = new THREE.Mesh(
      new THREE.CylinderGeometry(3.2, 3.4, 0.3, 48),
      new THREE.MeshStandardMaterial({ color: 0x7a5538, roughness: 0.85 }),
    );
    bench.position.y = -1.75;
    bench.receiveShadow = true;
    const cloth = new THREE.Mesh(
      new THREE.CylinderGeometry(1.9, 1.9, 0.04, 48),
      new THREE.MeshStandardMaterial({ color: 0xb8473b, roughness: 1 }),
    );
    cloth.position.y = -1.58;
    cloth.receiveShadow = true;
    this.scene.add(bench, cloth);

    const warm = new THREE.DirectionalLight(0xffe0b0, 1.5);
    warm.position.set(3, 5, 4);
    warm.castShadow = true;
    warm.shadow.mapSize.set(1024, 1024);
    const rim = new THREE.PointLight(0x9fc3ff, 18, 14, 2);
    rim.position.set(-4, 2, -3);
    this.scene.add(warm, rim, new THREE.AmbientLight(0xffffff, 0.35), this.pivot, this.particles.points, this.sparkles.points);

    this.brushMesh = new THREE.Group();
    const bristles = new THREE.Mesh(
      new THREE.BoxGeometry(0.3, 0.12, 0.14),
      new THREE.MeshStandardMaterial({ color: 0xe4d5ae, roughness: 1 }),
    );
    const back = new THREE.Mesh(new THREE.BoxGeometry(0.34, 0.1, 0.16), new THREE.MeshStandardMaterial({ color: 0x8a6240, roughness: 0.7 }));
    back.position.y = 0.11;
    const handle = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.04, 0.9, 8), new THREE.MeshStandardMaterial({ color: 0x8a6240, roughness: 0.7 }));
    handle.position.set(0.3, 0.45, 0);
    handle.rotation.z = -0.9;
    this.brushMesh.add(bristles, back, handle);
    this.brushMesh.visible = false;
    this.scene.add(this.brushMesh);

    this.camera.position.set(0, 3.1, 4.6);
    this.camera.lookAt(0, -0.2, 0);

    this.sampleCv.width = this.sampleCv.height = 64;
    this.sampleCtx = this.sampleCv.getContext('2d', { willReadFrequently: true })!;
    this.brushGrad = this.sampleCtx.createRadialGradient(0, 0, 0, 0, 0, 1);
    this.brushGrad.addColorStop(0, 'rgba(0,0,0,0.9)');
    this.brushGrad.addColorStop(0.65, 'rgba(0,0,0,0.5)');
    this.brushGrad.addColorStop(1, 'rgba(0,0,0,0)');
  }

  resize(w: number, h: number): void {
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  /**
   * Readies the bench's shaders ahead of time. The bench has its own lights, so everything drawn here needs its own versions of them:
   * every find (`finds`), plus a sample find in its crust, drawn once so the crust is on the graphics chip too.
   */
  async prepare(renderer: THREE.WebGLRenderer, finds: THREE.Object3D): Promise<void> {
    const sample = wrapWithCrust(buildItem(ITEM_DEFS[0].id), newCrustCanvas());
    this.pivot.add(sample.root);
    await renderer.compileAsync(this.scene, this.camera);
    await renderer.compileAsync(finds, this.camera, this.scene);
    const target = new THREE.WebGLRenderTarget(1, 1);
    renderer.setRenderTarget(target);
    renderer.render(this.scene, this.camera);
    renderer.setRenderTarget(null);
    target.dispose();
    this.pivot.remove(sample.root);
  }

  enter(item: BuriedItem, brushPx: number): void {
    this.exit();
    this.item = item;
    this.brushPx = brushPx;
    item.crustCanvas ??= newCrustCanvas();
    const clean = buildItem(item.def.id);
    clean.traverse((o) => (o.castShadow = true));
    this.rig = wrapWithCrust(clean, item.crustCanvas);
    this.pivot.add(this.rig.root);
    this.pivot.position.set(0, -0.15, 0);
    this.target.setFromEuler(new THREE.Euler(0.9, 0.4, 0, 'YXZ'));
    this.pivot.quaternion.copy(this.target);
    this.lastUV = null;
    this.sampleNow();
    this.phase = this.progress >= CLEAN_THRESHOLD ? 'finishing' : 'brushing';
    this.brushMesh.visible = true;
    this.brushMesh.position.set(0, 4, 0);
  }

  exit(): void {
    if (this.rig) this.pivot.remove(this.rig.root);
    this.rig = null;
    this.item = null;
    this.phase = 'idle';
    this.brushMesh.visible = false;
  }

  rotate(dyaw: number, dpitch: number): void {
    // turn around the screen's own axes, so the item follows the drag in any orientation and can be flipped over
    this.target.premultiply(this.qa.setFromAxisAngle(this.axis.set(0, 1, 0), dyaw));
    this.target.premultiply(this.qa.setFromAxisAngle(this.axis.set(1, 0, 0), dpitch));
    this.target.normalize();
  }

  private ndc = new THREE.Vector2();
  private centre = new THREE.Vector3();

  /** Pulls a stick-driven brush back onto the find, so it can never wander off the edge out of sight. */
  keepOnItem(ndc: THREE.Vector2): void {
    if (!this.rig) return;
    const hits = (p: THREE.Vector2): boolean => {
      this.raycaster.setFromCamera(p, this.camera);
      return this.raycaster.intersectObject(this.rig!.crust, false).length > 0;
    };
    if (hits(ndc)) return;
    this.rig.crust.getWorldPosition(this.centre).project(this.camera);
    const inside = new THREE.Vector2(this.centre.x, this.centre.y);
    const outside = ndc.clone();
    const mid = new THREE.Vector2();
    for (let i = 0; i < 7; i++) {
      mid.copy(inside).add(outside).multiplyScalar(0.5);
      if (hits(mid)) inside.copy(mid);
      else outside.copy(mid);
    }
    ndc.copy(inside);
  }

  /** Pointer position in NDC; moves the brush mesh and, when `down`, scrubs. */
  pointer(ndc: THREE.Vector2, down: boolean, speed: number): boolean {
    this.ndc.copy(ndc);
    if (!this.rig || this.phase === 'idle') return false;
    this.raycaster.setFromCamera(ndc, this.camera);
    const hit = this.raycaster.intersectObject(this.rig.crust, false)[0];
    this.brushMesh.visible = !!hit || this.phase === 'brushing';
    if (!hit || !hit.uv) {
      this.lastUV = null;
      return false;
    }
    this.brushMesh.position.copy(hit.point).addScaledVector(this.axis.copy(hit.face!.normal).transformDirection(this.rig.crust.matrixWorld), 0.08);
    if (!down || this.phase !== 'brushing') {
      this.lastUV = null;
      return false;
    }
    this.paint(hit.uv);
    this.brushT = 1;
    const n = Math.min(4, 1 + Math.floor(speed / 500));
    for (let i = 0; i < n; i++) {
      this.particles.burst(hit.point, 1, 0.6, 0.4, 0.7, this.dust);
    }
    return true;
  }

  private paint(uv: THREE.Vector2): void {
    const rig = this.rig!;
    const ctx = rig.canvas.getContext('2d')!;
    const S = rig.canvas.width;
    ctx.fillStyle = this.brushGrad;
    const stamp = (u: number, v: number): void => {
      const x = u * S;
      const y = (1 - v) * S;
      const theta = (1 - v) * Math.PI;
      const ry = this.brushPx * this.brushScale;
      // a circle on the sphere is wider in UV space near the poles; sphere u spans 2*PI, v spans PI
      const rx = Math.min(S / 2, (ry * 2) / Math.max(0.25, Math.sin(theta)));
      ctx.save();
      ctx.translate(x, y);
      ctx.scale(rx, ry);
      ctx.beginPath();
      ctx.arc(0, 0, 1, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();
      // wrap seam
      if (x - rx < 0 || x + rx > S) {
        const off = x - rx < 0 ? S : -S;
        ctx.save();
        ctx.translate(x + off, y);
        ctx.scale(rx, ry);
        ctx.beginPath();
        ctx.arc(0, 0, 1, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
      }
    };
    if (this.lastUV && Math.abs(this.lastUV.x - uv.x) < 0.3) {
      const d = this.lastUV.distanceTo(uv) * S;
      const steps = Math.min(12, Math.ceil(d / Math.max(4, this.brushPx * 0.5)));
      for (let i = 1; i <= steps; i++) {
        const k = i / steps;
        stamp(this.lastUV.x + (uv.x - this.lastUV.x) * k, this.lastUV.y + (uv.y - this.lastUV.y) * k);
      }
    } else {
      stamp(uv.x, uv.y);
    }
    this.lastUV = this.lastUVVec.copy(uv);
    rig.texture.needsUpdate = true;
  }

  /** Area-weighted fraction of the crust that is now see-through. */
  private sampleNow(): void {
    if (!this.rig) {
      this.progress = 0;
      return;
    }
    const S = 64;
    this.sampleCtx.drawImage(this.rig.canvas, 0, 0, S, S);
    const data = this.sampleCtx.getImageData(0, 0, S, S).data;
    let clean = 0;
    let total = 0;
    const N = 16; // logical 16x16 grid, each cell sampled at its centre area
    const cell = S / N;
    for (let gy = 0; gy < N; gy++) {
      const w = Math.sin(((gy + 0.5) / N) * Math.PI);
      for (let gx = 0; gx < N; gx++) {
        let sum = 0;
        let cnt = 0;
        for (let y = gy * cell; y < (gy + 1) * cell; y++) {
          for (let x = gx * cell; x < (gx + 1) * cell; x++) {
            sum += data[(y * S + x) * 4];
            cnt++;
          }
        }
        clean += w * (1 - sum / cnt / 255);
        total += w;
      }
    }
    this.progress = Math.min(1, clean / total);
    this.onProgress(this.progress);
  }

  /** Instantly clears the crust (Ultrasonic Bath). */
  instantClean(): void {
    if (!this.rig || this.phase !== 'brushing') return;
    const ctx = this.rig.canvas.getContext('2d')!;
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, this.rig.canvas.width, this.rig.canvas.height);
    this.rig.texture.needsUpdate = true;
    this.sampleNow();
    this.beginFinish();
  }

  private beginFinish(): void {
    this.phase = 'finishing';
    this.fadeAcc = 0;
    this.brushMesh.visible = false;
    const p = new THREE.Vector3();
    for (let i = 0; i < 90; i++) {
      p.set((Math.random() - 0.5) * 2.2, (Math.random() - 0.5) * 1.2, (Math.random() - 0.5) * 2.2);
      const v = new THREE.Vector3((Math.random() - 0.5) * 1.6, Math.random() * 1.6, (Math.random() - 0.5) * 1.6);
      this.sparkles.emit(p, v, 1.2 + Math.random() * 0.8, this.gold);
    }
  }

  update(dt: number): void {
    this.time += dt;
    if (this.rig) {
      this.pivot.quaternion.slerp(this.target, 1 - Math.exp(-dt * 14));
      this.pivot.position.y = -0.15 + Math.sin(this.time * 1.2) * 0.02;
    }
    this.brushT = Math.max(0, this.brushT - dt * 6);
    this.brushMesh.rotation.z = Math.sin(this.time * 40) * 0.25 * this.brushT;

    if (this.phase === 'brushing') {
      this.sinceSample += dt * 1000;
      if (this.sinceSample >= SAMPLE_EVERY_MS) {
        this.sinceSample = 0;
        this.sampleNow();
        if (this.progress >= CLEAN_THRESHOLD) this.beginFinish();
      }
    } else if (this.phase === 'finishing' && this.rig) {
      // complete the last few percent with a soft golden fade
      const ctx = this.rig.canvas.getContext('2d')!;
      ctx.fillStyle = 'rgba(0,0,0,0.12)';
      ctx.fillRect(0, 0, this.rig.canvas.width, this.rig.canvas.height);
      this.rig.texture.needsUpdate = true;
      this.fadeAcc += dt;
      this.progress = Math.min(1, this.progress + dt * 0.6);
      this.onProgress(this.progress);
      if (this.fadeAcc > 0.7) {
        ctx.fillStyle = '#000';
        ctx.fillRect(0, 0, this.rig.canvas.width, this.rig.canvas.height);
        this.rig.texture.needsUpdate = true;
        this.rig.crust.visible = false;
        this.progress = 1;
        this.onProgress(1);
        this.phase = 'done';
        this.onComplete();
      }
    }
    this.particles.update(dt);
    this.sparkles.update(dt);
  }
}
