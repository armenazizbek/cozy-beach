import * as THREE from 'three';

export interface InputHandlers {
  onPointerDown?: (button: number) => void;
  onPointerUp?: (button: number, wasDrag: boolean) => void;
  /** every move; dx,dy in CSS pixels since last move; buttons is the held-button bitmask */
  onPointerMove?: (dx: number, dy: number, buttons: number) => void;
  onClick?: (button: number) => void;
  onWheel?: (delta: number) => void;
  onKey?: (key: string) => void;
}

const BLOCK_DEFAULT = new Set(['Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Tab']);
const DRAG_THRESHOLD = 5;

export class InputManager {
  readonly pointer = new THREE.Vector2(0, 0);
  readonly keys = new Set<string>();
  handlers: InputHandlers = {};
  buttons = 0;
  /** the controller is moving the pointer, so the mouse leaves it and the held buttons alone */
  padPointer = false;
  /** pointer speed in px/s, smoothed */
  speed = 0;
  private raycaster = new THREE.Raycaster();
  private startX = 0;
  private startY = 0;
  private lastX = 0;
  private lastY = 0;
  private lastT = 0;
  private dragging = false;
  private downButton = -1;
  private overUI = false;

  constructor(private el: HTMLElement) {
    el.addEventListener('pointerdown', this.down);
    window.addEventListener('pointermove', this.move);
    window.addEventListener('pointerup', this.up);
    el.addEventListener('contextmenu', (e) => e.preventDefault());
    el.addEventListener('wheel', (e) => {
      e.preventDefault();
      this.handlers.onWheel?.(Math.sign(e.deltaY));
    }, { passive: false });
    window.addEventListener('keydown', (e) => {
      if (BLOCK_DEFAULT.has(e.code)) e.preventDefault();
      if (e.repeat) return;
      this.keys.add(e.code);
      this.handlers.onKey?.(e.code);
    });
    window.addEventListener('keyup', (e) => this.keys.delete(e.code));
    window.addEventListener('blur', () => this.keys.clear());
  }

  private setPointer(e: PointerEvent): void {
    this.pointer.set((e.clientX / window.innerWidth) * 2 - 1, -(e.clientY / window.innerHeight) * 2 + 1);
  }

  private down = (e: PointerEvent): void => {
    if (document.pointerLockElement !== this.el) this.setPointer(e);
    this.buttons = e.buttons;
    this.downButton = e.button;
    this.startX = this.lastX = e.clientX;
    this.startY = this.lastY = e.clientY;
    this.lastT = performance.now();
    this.dragging = false;
    this.overUI = false;
    this.handlers.onPointerDown?.(e.button);
  };

  private move = (e: PointerEvent): void => {
    const locked = document.pointerLockElement === this.el;
    if (this.padPointer) {
      this.handlers.onPointerMove?.(e.movementX, e.movementY, e.buttons);
      return;
    }
    if (locked) this.pointer.set(0, 0);
    else this.setPointer(e);
    const now = performance.now();
    const dx = locked ? e.movementX : e.clientX - this.lastX;
    const dy = locked ? e.movementY : e.clientY - this.lastY;
    const dt = Math.max(1, now - this.lastT) / 1000;
    this.speed += (Math.hypot(dx, dy) / dt - this.speed) * 0.35;
    this.lastX = e.clientX;
    this.lastY = e.clientY;
    this.lastT = now;
    this.buttons = e.buttons;
    if (e.buttons && Math.hypot(e.clientX - this.startX, e.clientY - this.startY) > DRAG_THRESHOLD) this.dragging = true;
    this.handlers.onPointerMove?.(dx, dy, e.buttons);
  };

  private up = (e: PointerEvent): void => {
    if (this.downButton === -1) return;
    this.buttons = e.buttons;
    const wasDrag = this.dragging;
    this.handlers.onPointerUp?.(e.button, wasDrag);
    if (!wasDrag && !this.overUI) this.handlers.onClick?.(e.button);
    this.dragging = false;
    this.downButton = -1;
  };

  /** call each frame to let speed decay when the pointer is still */
  tick(dt: number): void {
    this.speed *= Math.exp(-dt * 8);
  }

  ray(camera: THREE.Camera, center = false): THREE.Raycaster {
    const locked = document.pointerLockElement === this.el;
    this.raycaster.setFromCamera(locked || center ? new THREE.Vector2(0, 0) : this.pointer, camera);
    return this.raycaster;
  }
}
