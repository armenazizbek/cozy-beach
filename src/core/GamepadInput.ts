export type PadButton = 'a' | 'b' | 'x' | 'y' | 'lb' | 'rb' | 'lt' | 'rt' | 'back' | 'start' | 'l3' | 'r3' | 'up' | 'down' | 'left' | 'right';
export type PadDir = 'up' | 'down' | 'left' | 'right';

/** Index in the "standard" gamepad layout (Xbox, PlayStation and most other pads map to it). */
const BUTTONS: PadButton[] = ['a', 'b', 'x', 'y', 'lb', 'rb', 'lt', 'rt', 'back', 'start', 'l3', 'r3', 'up', 'down', 'left', 'right'];

const DEAD = 0.2;
const dead = (x: number, y: number, out: { x: number; y: number }): void => {
  const m = Math.hypot(x, y);
  if (m < DEAD) {
    out.x = out.y = 0;
    return;
  }
  const k = Math.min(1, (m - DEAD) / (1 - DEAD)) / m;
  out.x = x * k;
  out.y = y * k;
};

/** Reads the first connected controller once per frame and turns it into presses, sticks and menu steps. */
export class GamepadInput {
  /** left stick: x right, y down (like the browser), dead zone already removed */
  readonly move = { x: 0, y: 0 };
  /** right stick */
  readonly look = { x: 0, y: 0 };
  lt = 0;
  rt = 0;
  connected = false;
  onButton?: (b: PadButton, down: boolean) => void;
  /** a menu step: d-pad or left stick, with key repeat */
  onNav?: (d: PadDir) => void;
  /** any real input at all, so the game can switch its prompts to controller wording */
  onActivity?: () => void;

  private held = new Set<PadButton>();
  private navDir: PadDir | null = null;
  private navT = 0;

  isDown(b: PadButton): boolean {
    return this.held.has(b);
  }

  poll(dt: number): void {
    const pads = navigator.getGamepads?.() ?? [];
    const pad = Array.from(pads).find((p) => p && p.connected) ?? null;
    this.connected = !!pad;
    if (!pad) {
      if (this.held.size) {
        this.held.clear();
        this.navDir = null;
      }
      this.move.x = this.move.y = this.look.x = this.look.y = this.lt = this.rt = 0;
      return;
    }
    let active = false;
    BUTTONS.forEach((name, i) => {
      const b = pad.buttons[i];
      if (!b) return;
      const down = name === 'lt' || name === 'rt' ? b.value > 0.4 || b.pressed : b.pressed;
      if (down !== this.held.has(name)) {
        if (down) this.held.add(name);
        else this.held.delete(name);
        active = true;
        this.onButton?.(name, down);
      }
    });
    this.lt = pad.buttons[6]?.value ?? 0;
    this.rt = pad.buttons[7]?.value ?? 0;
    dead(pad.axes[0] ?? 0, pad.axes[1] ?? 0, this.move);
    dead(pad.axes[2] ?? 0, pad.axes[3] ?? 0, this.look);
    if (Math.hypot(this.move.x, this.move.y) > 0.4 || Math.hypot(this.look.x, this.look.y) > 0.4) active = true;
    if (active) this.onActivity?.();

    // menu stepping
    let dir: PadDir | null = null;
    if (this.held.has('up')) dir = 'up';
    else if (this.held.has('down')) dir = 'down';
    else if (this.held.has('left')) dir = 'left';
    else if (this.held.has('right')) dir = 'right';
    else if (Math.hypot(this.move.x, this.move.y) > 0.6) {
      dir = Math.abs(this.move.x) > Math.abs(this.move.y) ? (this.move.x > 0 ? 'right' : 'left') : this.move.y > 0 ? 'down' : 'up';
    }
    if (dir !== this.navDir) {
      this.navDir = dir;
      this.navT = 0.4;
      if (dir) this.onNav?.(dir);
    } else if (dir) {
      this.navT -= dt;
      if (this.navT <= 0) {
        this.navT = 0.14;
        this.onNav?.(dir);
      }
    }
  }
}
