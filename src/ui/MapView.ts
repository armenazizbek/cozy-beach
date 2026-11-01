import { CAMP_Z, HALF, ISLAND, WATER_LEVEL, WET_Z, baseHeight } from '../world/Beach';

export interface Waypoint {
  x: number;
  z: number;
}

export interface MapPlayer {
  x: number;
  z: number;
  /** three.js rotation.y: forward is (-sin, -cos) */
  yaw: number;
}

/** The terrain is painted over the island and plenty of open sea around it, so the boat never leaves the map. */
const EXT_X0 = -210;
const EXT_Z0 = -150;
const EXT = 420;
const PPM = 2; // painted pixels per metre
const BASE_PX = EXT * PPM;

/** The part of the world the full map shows: a square centred a little out to sea. */
export const MAP_VIEW = { cx: 0, cz: 70, half: 172 };

/**
 * Draws the island from above. The terrain is painted once into a canvas; the round mini-map
 * (which turns with the player) and the full map (north up) both reuse it.
 */
export class MapView {
  private base = document.createElement('canvas');
  /** planted marker flags, set by the game and edited in place */
  flags: [number, number][] = [];
  /** where the dog is while it is out on a trip */
  dog: { x: number; z: number } | null = null;
  /** where the treasure map says to dig; set only while the player holds the map */
  treasures: [number, number][] = [];
  /** where the captain's strongbox is buried, once the bottle map has been found */
  captain: [number, number][] = [];

  constructor(trunks: { x: number; z: number }[]) {
    this.base.width = this.base.height = BASE_PX;
    const g = this.base.getContext('2d')!;
    const img = g.createImageData(BASE_PX, BASE_PX);
    const data = img.data;
    const m = EXT / BASE_PX;
    const islBox = ISLAND.R * 1.2;
    for (let v = 0; v < BASE_PX; v++) {
      const z = EXT_Z0 + (v + 0.5) * m;
      const deepSeaRow = z > 88 && Math.abs(z - ISLAND.z) > islBox;
      if (deepSeaRow) {
        const rowBase = v * BASE_PX * 4;
        for (let u = 0; u < BASE_PX; u++) {
          const i = rowBase + u * 4;
          data[i] = 30; data[i + 1] = 95; data[i + 2] = 155; data[i + 3] = 255;
        }
        continue;
      }
      for (let u = 0; u < BASE_PX; u++) {
        const x = EXT_X0 + (u + 0.5) * m;
        if (z > 88 && Math.abs(x - ISLAND.x) > islBox) {
          const i = (v * BASE_PX + u) * 4;
          data[i] = 30; data[i + 1] = 95; data[i + 2] = 155; data[i + 3] = 255;
          continue;
        }
        const h = baseHeight(x, z);
        let r: number;
        let gr: number;
        let b: number;
        if (h < WATER_LEVEL) {
          const d = Math.min(1, (WATER_LEVEL - h) / 0.9);
          r = 70 - d * 40; gr = 150 - d * 55; b = 200 - d * 45;
        } else if (h < WATER_LEVEL + 0.12) {
          r = 236; gr = 240; b = 235; // foam line
        } else {
          const wet = Math.min(1, Math.max(0, (z - (WET_Z - 3)) / 6));
          const hill = Math.min(1, Math.max(0, (h - 1.6) / 1.4));
          r = 232 - wet * 50 - hill * 70; gr = 206 - wet * 55 - hill * 36; b = 170 - wet * 40 - hill * 62;
        }
        const i = (v * BASE_PX + u) * 4;
        data[i] = r; data[i + 1] = gr; data[i + 2] = b; data[i + 3] = 255;
      }
    }
    g.putImageData(img, 0, 0);
    // palms
    g.fillStyle = 'rgba(52,120,52,0.9)';
    g.beginPath();
    for (const t of trunks) {
      const px = (t.x - EXT_X0) * PPM;
      const pz = (t.z - EXT_Z0) * PPM;
      g.moveTo(px + 2.4, pz);
      g.arc(px, pz, 2.4, 0, Math.PI * 2);
    }
    g.fill();
  }

  private drawHoles(ctx: CanvasRenderingContext2D, holes: [number, number][], radius: number, cx?: number, cz?: number, maxR?: number): void {
    if (holes.length === 0) return;
    ctx.fillStyle = 'rgba(95,60,34,0.85)';
    ctx.beginPath();
    if (cx !== undefined && cz !== undefined && maxR !== undefined) {
      for (const [x, z] of holes) {
        if (Math.abs(x - cx) > maxR || Math.abs(z - cz) > maxR) continue;
        ctx.moveTo(x + radius, z);
        ctx.arc(x, z, radius, 0, Math.PI * 2);
      }
    } else {
      for (const [x, z] of holes) {
        ctx.moveTo(x + radius, z);
        ctx.arc(x, z, radius, 0, Math.PI * 2);
      }
    }
    ctx.fill();
  }

  /** A map pin: a red teardrop with a white centre. Drawn in world units, tip at (x, z). */
  private drawPaw(ctx: CanvasRenderingContext2D, x: number, z: number, s: number): void {
    ctx.save();
    ctx.translate(x, z);
    ctx.fillStyle = '#7a4a24';
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = s * 0.22;
    const blob = (bx: number, bz: number, r: number): void => {
      ctx.beginPath();
      ctx.arc(bx, bz, r, 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();
    };
    blob(0, s * 0.35, s * 0.95);
    blob(-s * 1.05, -s * 0.5, s * 0.5);
    blob(-s * 0.4, -s * 1.25, s * 0.5);
    blob(s * 0.4, -s * 1.25, s * 0.5);
    blob(s * 1.05, -s * 0.5, s * 0.5);
    ctx.restore();
  }

  /** A red cross, like the ones on an old treasure map. */
  private drawCross(ctx: CanvasRenderingContext2D, x: number, z: number, s: number, pulse = 1): void {
    ctx.save();
    ctx.translate(x, z);
    ctx.lineCap = 'round';
    for (const [w, col] of [[s * 0.9, 'rgba(255,248,230,0.95)'], [s * 0.5, '#b3201a']] as const) {
      ctx.lineWidth = w * pulse;
      ctx.strokeStyle = col;
      ctx.beginPath();
      ctx.moveTo(-s, -s);
      ctx.lineTo(s, s);
      ctx.moveTo(s, -s);
      ctx.lineTo(-s, s);
      ctx.stroke();
    }
    ctx.restore();
  }

  /** The treasure map: the island on old paper, with a red cross where each chest is buried. */
  drawTreasure(ctx: CanvasRenderingContext2D, size: number, p: MapPlayer, time: number, dest: Waypoint | null = null, cart: Waypoint | null = null, boat: Waypoint | null = null, marks: [number, number][] = this.treasures, bottle = false): void {
    const { cx, cz, half } = MAP_VIEW;
    const sc = size / (2 * half);
    ctx.clearRect(0, 0, size, size);
    ctx.save();
    ctx.scale(sc, sc);
    ctx.translate(half - cx, half - cz);
    // the land drawn in browns, like ink on paper
    ctx.filter = bottle ? 'sepia(0.55) hue-rotate(75deg) saturate(0.6) contrast(0.85) brightness(1.1)' : 'sepia(1) saturate(0.7) contrast(0.8) brightness(1.08)';
    ctx.drawImage(this.base, EXT_X0, EXT_Z0, EXT, EXT);
    ctx.filter = 'none';
    this.drawCamp(ctx, 0, CAMP_Z, 1.8);
    const pulse = 1 + Math.sin(time * 4) * 0.12;
    if (cart) this.drawCart(ctx, cart.x, cart.z, 1.9);
    if (boat) this.drawBoat(ctx, boat.x, boat.z, 2.0);
    for (const [x, z] of marks) this.drawCross(ctx, x, z, 3.4, pulse);
    this.drawFlags(ctx, 3.2);
    if (this.dog) this.drawPaw(ctx, this.dog.x, this.dog.z, 3);
    // the destination the player has marked: the same dashed line and pin as on the island map
    if (dest) {
      ctx.setLineDash([2.2, 1.8]);
      ctx.strokeStyle = 'rgba(230,57,70,0.95)';
      ctx.lineWidth = 0.7;
      ctx.beginPath();
      ctx.moveTo(p.x, p.z);
      ctx.lineTo(dest.x, dest.z);
      ctx.stroke();
      ctx.setLineDash([]);
      this.drawPin(ctx, dest.x, dest.z, 1.7);
    }
    ctx.restore();
    // aged paper over everything
    ctx.save();
    ctx.globalCompositeOperation = 'multiply';
    const g = ctx.createRadialGradient(size / 2, size / 2, size * 0.25, size / 2, size / 2, size * 0.75);
    g.addColorStop(0, bottle ? 'rgba(206,226,208,0.78)' : 'rgba(236,214,164,0.75)');
    g.addColorStop(1, bottle ? 'rgba(84,112,96,0.92)' : 'rgba(150,104,52,0.9)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, size, size);
    ctx.restore();
    // a dashed border and a compass rose
    ctx.strokeStyle = bottle ? 'rgba(30,70,52,0.75)' : 'rgba(90,50,20,0.7)';
    ctx.lineWidth = 3;
    ctx.setLineDash([10, 6]);
    ctx.strokeRect(10, 10, size - 20, size - 20);
    ctx.setLineDash([]);
    ctx.save();
    ctx.translate(size - 52, 56);
    ctx.fillStyle = 'rgba(90,50,20,0.85)';
    for (let i = 0; i < 4; i++) {
      ctx.rotate(Math.PI / 2);
      ctx.beginPath();
      ctx.moveTo(0, -22);
      ctx.lineTo(5, -5);
      ctx.lineTo(-5, -5);
      ctx.closePath();
      ctx.fill();
    }
    ctx.font = '700 13px Georgia, serif';
    ctx.textAlign = 'center';
    ctx.fillText('N', 0, -28);
    ctx.restore();
    ctx.font = 'italic 600 14px Georgia, serif';
    ctx.fillStyle = 'rgba(70,36,12,0.9)';
    ctx.textAlign = 'center';
    ctx.fillText('Camp', ((0 - cx + half) / (2 * half)) * size, ((CAMP_Z - cz + half) / (2 * half)) * size + 34);
    ctx.fillText('Island', ((ISLAND.x - cx + half) / (2 * half)) * size, ((ISLAND.z - cz + half) / (2 * half)) * size - 34);
    if (bottle) {
      ctx.textAlign = 'left';
      ctx.fillText('Found in a bottle', 24, size - 24);
    }
    ctx.save();
    ctx.translate(((p.x - cx + half) / (2 * half)) * size, ((p.z - cz + half) / (2 * half)) * size);
    ctx.rotate(Math.atan2(-Math.cos(p.yaw), -Math.sin(p.yaw)) + Math.PI / 2);
    this.arrow(ctx, size / 70);
    ctx.restore();
  }

  private drawFlags(ctx: CanvasRenderingContext2D, s: number): void {
    for (const [x, z] of this.flags) {
      ctx.save();
      ctx.translate(x, z);
      ctx.lineWidth = s * 0.28;
      ctx.strokeStyle = '#ffffff';
      ctx.fillStyle = '#e63946';
      ctx.beginPath();
      ctx.moveTo(0, 0);
      ctx.lineTo(0, -s * 2.4);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(0, -s * 2.4);
      ctx.lineTo(s * 1.8, -s * 1.8);
      ctx.lineTo(0, -s * 1.2);
      ctx.closePath();
      ctx.fill();
      ctx.stroke();
      ctx.restore();
    }
  }

  private drawPin(ctx: CanvasRenderingContext2D, x: number, z: number, scale: number): void {
    ctx.save();
    ctx.translate(x, z);
    ctx.scale(scale, scale);
    ctx.fillStyle = '#e63946';
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 0.45;
    ctx.beginPath();
    ctx.moveTo(0, 0);
    ctx.bezierCurveTo(-2.6, -2.6, -2.2, -5.8, 0, -5.8);
    ctx.bezierCurveTo(2.2, -5.8, 2.6, -2.6, 0, 0);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
    ctx.fillStyle = '#fff';
    ctx.beginPath();
    ctx.arc(0, -3.6, 1, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  /** The boat: a little white hull with a blue stripe. */
  private drawBoat(ctx: CanvasRenderingContext2D, x: number, z: number, scale: number): void {
    ctx.save();
    ctx.translate(x, z);
    ctx.scale(scale, scale);
    ctx.fillStyle = '#ffffff';
    ctx.strokeStyle = '#1f5fa8';
    ctx.lineWidth = 0.5;
    ctx.beginPath();
    ctx.moveTo(0, -2.6);
    ctx.quadraticCurveTo(1.6, -0.8, 1.2, 2.2);
    ctx.lineTo(-1.2, 2.2);
    ctx.quadraticCurveTo(-1.6, -0.8, 0, -2.6);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
    ctx.restore();
  }

  /** The golf cart: a small teal car-shape. */
  private drawCart(ctx: CanvasRenderingContext2D, x: number, z: number, scale: number): void {
    ctx.save();
    ctx.translate(x, z);
    ctx.scale(scale, scale);
    ctx.fillStyle = '#1f8f86';
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 0.4;
    ctx.beginPath();
    ctx.roundRect(-1.4, -2, 2.8, 4, 0.8);
    ctx.fill();
    ctx.stroke();
    ctx.fillStyle = '#fff';
    ctx.fillRect(-0.9, -1.2, 1.8, 1.1);
    ctx.restore();
  }

  private drawCamp(ctx: CanvasRenderingContext2D, x: number, z: number, scale: number): void {
    ctx.save();
    ctx.translate(x, z);
    ctx.scale(scale, scale);
    ctx.fillStyle = '#d9703f';
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 0.45;
    ctx.beginPath();
    ctx.moveTo(0, -2.6);
    ctx.lineTo(2.4, 1.6);
    ctx.lineTo(-2.4, 1.6);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
    ctx.restore();
  }

  private arrow(ctx: CanvasRenderingContext2D, px: number): void {
    ctx.save();
    ctx.fillStyle = 'rgba(255,255,255,0.18)';
    ctx.beginPath();
    ctx.moveTo(0, 0);
    ctx.arc(0, 0, px * 4.2, -Math.PI / 2 - 0.5, -Math.PI / 2 + 0.5);
    ctx.closePath();
    ctx.fill();
    ctx.fillStyle = '#ff4a3a';
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = px * 0.35;
    ctx.beginPath();
    ctx.moveTo(0, -px * 1.5);
    ctx.lineTo(px, px);
    ctx.lineTo(0, px * 0.45);
    ctx.lineTo(-px, px);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
    ctx.restore();
  }

  private lastMiniKey = '';

  /** Round mini-map that turns so that "forward" is always up. */
  drawMini(ctx: CanvasRenderingContext2D, size: number, p: MapPlayer, holes: [number, number][], dest: Waypoint | null = null, cart: Waypoint | null = null, boat: Waypoint | null = null): void {
    const key = `${size}|${(p.x * 40) | 0},${(p.z * 40) | 0},${(p.yaw * 400) | 0}|${holes.length}|${this.flags.length}|${dest ? `${(dest.x * 10) | 0},${(dest.z * 10) | 0}` : ''}|${cart ? `${(cart.x * 20) | 0},${(cart.z * 20) | 0}` : ''}|${boat ? `${(boat.x * 20) | 0},${(boat.z * 20) | 0}` : ''}|${this.dog ? `${(this.dog.x * 20) | 0},${(this.dog.z * 20) | 0}` : ''}`;
    if (key === this.lastMiniKey) return;
    this.lastMiniKey = key;

    const r = size / 2;
    const sc = size / 60; // 60 metres across
    ctx.clearRect(0, 0, size, size);
    ctx.save();
    ctx.beginPath();
    ctx.arc(r, r, r - 2, 0, Math.PI * 2);
    ctx.clip();
    ctx.fillStyle = '#0d1b26';
    ctx.fillRect(0, 0, size, size);

    const fx = -Math.sin(p.yaw);
    const fz = -Math.cos(p.yaw);
    ctx.save();
    ctx.translate(r, r);
    ctx.rotate(-Math.PI / 2 - Math.atan2(fz, fx));
    ctx.scale(sc, sc);
    ctx.translate(-p.x, -p.z);
    ctx.drawImage(this.base, EXT_X0, EXT_Z0, EXT, EXT);
    this.drawHoles(ctx, holes, 0.9, p.x, p.z, 34);
    this.drawCamp(ctx, 0, CAMP_Z, 1.2);
    if (cart) this.drawCart(ctx, cart.x, cart.z, 1.2);
    if (boat) this.drawBoat(ctx, boat.x, boat.z, 1.3);
    this.drawFlags(ctx, 2.2);
    if (this.dog) this.drawPaw(ctx, this.dog.x, this.dog.z, 1.7);
    if (dest) {
      ctx.setLineDash([1.4, 1.1]);
      ctx.strokeStyle = 'rgba(230,57,70,0.95)';
      ctx.lineWidth = 0.55;
      ctx.beginPath();
      ctx.moveTo(p.x, p.z);
      ctx.lineTo(dest.x, dest.z);
      ctx.stroke();
      ctx.setLineDash([]);
      this.drawPin(ctx, dest.x, dest.z, 0.9);
    }
    ctx.restore();

    // keep the camp visible even when it is off the edge of the little map
    const dx = 0 - p.x;
    const dz = CAMP_Z - p.z;
    const ang = -Math.PI / 2 - Math.atan2(fz, fx);
    const rx = dx * Math.cos(ang) - dz * Math.sin(ang);
    const rz = dx * Math.sin(ang) + dz * Math.cos(ang);
    const dist = Math.hypot(rx, rz) * sc;
    if (dist > r - 12) {
      const k = (r - 12) / dist;
      this.drawCamp(ctx, r + rx * sc * k, r + rz * sc * k, 1.6);
    }

    if (this.dog) {
      const ex = this.dog.x - p.x;
      const ez = this.dog.z - p.z;
      const qx = ex * Math.cos(ang) - ez * Math.sin(ang);
      const qz = ex * Math.sin(ang) + ez * Math.cos(ang);
      const dd = Math.hypot(qx, qz) * sc;
      if (dd > r - 12) {
        const k = (r - 12) / dd;
        this.drawPaw(ctx, r + qx * sc * k, r + qz * sc * k, 4);
      }
    }

    if (dest) {
      const ddx = dest.x - p.x;
      const ddz = dest.z - p.z;
      const rx2 = ddx * Math.cos(ang) - ddz * Math.sin(ang);
      const rz2 = ddx * Math.sin(ang) + ddz * Math.cos(ang);
      const d2 = Math.hypot(rx2, rz2) * sc;
      if (d2 > r - 12) {
        const k = (r - 12) / d2;
        this.drawPin(ctx, r + rx2 * sc * k, r + rz2 * sc * k + 7, 1.7);
      }
    }

    ctx.translate(r, r);
    this.arrow(ctx, size / 30);
    ctx.restore();
    ctx.strokeStyle = 'rgba(255,248,230,0.95)';
    ctx.lineWidth = 3;
    ctx.beginPath();
    ctx.arc(r, r, r - 2, 0, Math.PI * 2);
    ctx.stroke();
  }

  /** The whole island, north (inland) at the top. */
  drawBig(ctx: CanvasRenderingContext2D, size: number, p: MapPlayer, holes: [number, number][], dest: Waypoint | null = null, cart: Waypoint | null = null, boat: Waypoint | null = null): void {
    const { cx, cz, half } = MAP_VIEW;
    const sc = size / (2 * half);
    ctx.clearRect(0, 0, size, size);
    ctx.save();
    ctx.scale(sc, sc);
    ctx.translate(half - cx, half - cz);
    ctx.drawImage(this.base, EXT_X0, EXT_Z0, EXT, EXT);
    this.drawHoles(ctx, holes, 0.8);
    this.drawCamp(ctx, 0, CAMP_Z, 1.8);
    if (cart) this.drawCart(ctx, cart.x, cart.z, 1.9);
    if (boat) this.drawBoat(ctx, boat.x, boat.z, 2.0);
    this.drawFlags(ctx, 3.2);
    if (this.dog) this.drawPaw(ctx, this.dog.x, this.dog.z, 3);
    if (dest) {
      ctx.setLineDash([2.2, 1.8]);
      ctx.strokeStyle = 'rgba(230,57,70,0.95)';
      ctx.lineWidth = 0.7;
      ctx.beginPath();
      ctx.moveTo(p.x, p.z);
      ctx.lineTo(dest.x, dest.z);
      ctx.stroke();
      ctx.setLineDash([]);
      this.drawPin(ctx, dest.x, dest.z, 1.7);
    }
    ctx.restore();

    ctx.font = '600 14px "Trebuchet MS", sans-serif';
    ctx.fillStyle = '#3b2a1e';
    ctx.textAlign = 'center';
    ctx.fillText('Camp', ((0 - cx + half) / (2 * half)) * size, ((CAMP_Z - cz + half) / (2 * half)) * size + 34);
    ctx.fillStyle = '#ffffff';
    ctx.fillText('Sea', size / 2, size - 14);
    ctx.fillText('Island', ((ISLAND.x - cx + half) / (2 * half)) * size, ((ISLAND.z - cz + half) / (2 * half)) * size - 34);

    ctx.save();
    ctx.translate(((p.x - cx + half) / (2 * half)) * size, ((p.z - cz + half) / (2 * half)) * size);
    ctx.rotate(Math.atan2(-Math.cos(p.yaw), -Math.sin(p.yaw)) + Math.PI / 2);
    this.arrow(ctx, size / 70);
    ctx.restore();
  }
}
