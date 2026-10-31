import * as THREE from 'three';
import { buildArrow } from './Arrows';
import { buildFist, wristAt } from './Hand';

const Y = new THREE.Vector3(0, 1, 0);
const SLEEVE = new THREE.MeshStandardMaterial({ color: 0x4f7396, roughness: 0.9 });
const SKIN = new THREE.MeshStandardMaterial({ color: 0xe0b08a, roughness: 0.8 });
const DARK = new THREE.MeshStandardMaterial({ color: 0x3a3f45, roughness: 0.5, metalness: 0.6 });
const BRASS = new THREE.MeshStandardMaterial({ color: 0xb08a4a, roughness: 0.4, metalness: 0.7 });

/** A cylinder stretched between two points, optionally tapering to radius `rb` at `b`. */
const limb = (a: THREE.Vector3, b: THREE.Vector3, r: number, mat: THREE.Material, rb = r): THREE.Mesh => {
  const dir = b.clone().sub(a);
  const m = new THREE.Mesh(new THREE.CylinderGeometry(rb, r, dir.length(), 14), mat);
  m.position.copy(a).add(b).multiplyScalar(0.5);
  m.quaternion.setFromUnitVectors(Y, dir.normalize());
  return m;
};

const v = (x: number, y: number, z: number): THREE.Vector3 => new THREE.Vector3(x, y, z);

/**
 * A soft-edged solid cut from a side-on outline. The outline's x runs forward (toward -z) and its y is height, so it can be drawn just as
 * the object looks from the side; `x` centres it left to right and `wide` is its full thickness.
 */
const sideCut = (draw: (s: THREE.Shape) => void, x: number, wide: number, mat: THREE.Material, soft = 0.012): THREE.Mesh => {
  const shape = new THREE.Shape();
  draw(shape);
  const depth = wide - soft * 2;
  const geo = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: true, bevelSize: soft, bevelThickness: soft, bevelSegments: 3, curveSegments: 10 });
  geo.rotateY(Math.PI / 2);
  geo.translate(x - depth / 2, 0, 0);
  return new THREE.Mesh(geo, mat);
};

/** What an arm is made of: the hand, the forearm, and a wider band over part of the forearm (0 at the wrist, 1 at the elbow). */
interface ArmStyle {
  hand: THREE.Material;
  fore: THREE.Material;
  band: THREE.Material;
  from: number;
  to: number;
}

/** A bare forearm with a rolled-up shirt sleeve. */
const BARE: ArmStyle = { hand: SKIN, fore: SKIN, band: SLEEVE, from: 0.42, to: 1 };
/** A diver's glove and wetsuit, with a yellow cuff at the wrist. */
const DIVER: ArmStyle = {
  hand: new THREE.MeshStandardMaterial({ color: 0x22262b, roughness: 0.8 }),
  fore: new THREE.MeshStandardMaterial({ color: 0x1f3a4d, roughness: 0.7 }),
  band: new THREE.MeshStandardMaterial({ color: 0xf2c14e, roughness: 0.6 }),
  from: 0.03,
  to: 0.075,
};

const BINOC_SCALE = 0.72;
const BOW_SCALE = 0.55;
const BOW_HAND_Y = -0.05;
const BOW_ELBOW = v(0.85, -1.15, 1.25);

/** How a hand takes a handle that runs on from the forearm, as in a handshake: the back of the hand out and up, the fingers under, the thumb over. */
interface Handshake {
  back: THREE.Vector3;
  slant: number;
}

const HANDSHAKE: Handshake = { back: v(0.7, 0.7, 0), slant: 0.08 };

const WRIST_R = 0.03;
const ELBOW_R = 0.043;

/**
 * First-person hands and tools (parented to the camera) plus an invisible body
 * whose only job is to cast the player's shadow onto the sand.
 */
export class ViewModel {
  /** child of the camera */
  readonly group = new THREE.Group();
  /** lives in the world; invisible to the camera but casts a shadow */
  readonly proxy = new THREE.Group();

  private root = new THREE.Group();
  private detector = new THREE.Group();
  private trowel = new THREE.Group();
  private gun = new THREE.Group();
  private binoc = new THREE.Group();
  private bow = new THREE.Group();
  private bowString: THREE.Mesh[] = [];
  private bowArrow = new THREE.Group();
  private bowHand = new THREE.Mesh();
  /** where the wrist of the drawing hand sits relative to the hand */
  private bowWrist = new THREE.Vector3();
  private bowForearm = new THREE.Mesh();
  private bowSleeve = new THREE.Mesh();
  private raised = false;
  private flash = new THREE.Mesh();
  /** sits at the very tip of the barrel; bullets start here */
  readonly muzzle = new THREE.Object3D();
  private flashT = 0;
  private kickT = 0;
  private proxyGun = new THREE.Group();
  private proxyDetector = new THREE.Group();
  private proxyTrowel = new THREE.Group();
  private proxyHead = new THREE.Group();
  private legs: THREE.Mesh[] = [];
  private bob = 0;
  private moving = 0;
  private swing = 0;
  private tool: 0 | 1 | 2 | 4 | 5 | 6 | 7 | 8 | 9 = 1;
  private detectorGold: THREE.MeshStandardMaterial[] = [];
  private trowelSteel: THREE.MeshStandardMaterial[] = [];
  private trowelGrip = new THREE.MeshStandardMaterial();
  private harpoon = new THREE.Group();
  /** swimming: the hands drift and sway slowly instead of bobbing with footsteps */
  private swim = false;

  setSwim(on: boolean): void {
    this.swim = on;
  }
  private flag = new THREE.Group();
  private bucket = new THREE.Group();

  constructor() {
    this.buildDetector();
    this.buildTrowel();
    this.buildGun();
    this.buildBinoculars();
    this.buildBow();
    this.buildFlag();
    this.buildBucket();
    this.buildHarpoon();
    this.root.add(this.detector, this.trowel, this.gun, this.binoc, this.bow, this.flag, this.bucket, this.harpoon);
    this.group.add(this.root);
    this.group.traverse((o) => {
      if (o instanceof THREE.Mesh) o.frustumCulled = false;
    });
    this.buildProxy();
    this.setTool(1);
  }

  /**
   * A hand closed on a rod, with its forearm running back to `elbow`. `axis` points out of the thumb side of the fist, so a rod that runs
   * forward from a low arm takes an axis pointing back at the player. `scale` is the scale of `parent`, so hands stay one size on every tool.
   * With `shake` the rod points where the thumb points, and the hand closes on it from the top and outside.
   */
  private hold(parent: THREE.Object3D, grip: THREE.Vector3, axis: THREE.Vector3, elbow: THREE.Vector3, side: 1 | -1, r: number, scale = 1, style = BARE, shake?: Handshake): { hand: THREE.Mesh; wrist: THREE.Vector3 } {
    const k = 1 / scale;
    const a = axis.clone().normalize();
    const toElbow = elbow.clone().sub(grip);
    const f = toElbow.addScaledVector(a, -toElbow.dot(a)).normalize().negate();
    const y = f.clone().cross(a);
    if (shake) {
      y.copy(shake.back).addScaledVector(a, -shake.back.dot(a)).normalize();
      f.crossVectors(a, y);
    }
    const slant = shake?.slant ?? 0;
    const w = wristAt(r * scale, slant);
    const wrist = grip.clone().addScaledVector(a, w.x * k).addScaledVector(y, side * w.y * k).addScaledVector(f, w.z * k);
    const d = elbow.clone().sub(wrist).normalize();
    const hand = buildFist(r * scale, v(d.dot(a), side * d.dot(y), d.dot(f)), style.hand, slant);
    hand.position.copy(grip);
    hand.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(a, y, f));
    // a left hand is a right hand mirrored through its palm
    hand.scale.set(k, side * k, k);
    parent.add(hand);
    return { hand, wrist };
  }

  /** The same, with a fixed forearm. */
  private armOn(parent: THREE.Object3D, grip: THREE.Vector3, axis: THREE.Vector3, elbow: THREE.Vector3, side: 1 | -1, r: number, scale = 1, style = BARE, shake?: Handshake): void {
    const { wrist } = this.hold(parent, grip, axis, elbow, side, r, scale, style, shake);
    const k = 1 / scale;
    const at = (u: number): number => WRIST_R + (ELBOW_R - WRIST_R) * u;
    parent.add(limb(wrist, elbow, WRIST_R * k, style.fore, ELBOW_R * k));
    parent.add(limb(wrist.clone().lerp(elbow, style.from), wrist.clone().lerp(elbow, style.to), (at(style.from) + 0.007) * k, style.band, (at(style.to) + 0.007) * k));
  }

  /** A diver's spear gun: a slim dark barrel, a moulded grip and trigger, two rubber bands stretched back to a steel spear, and a line reel. */
  private buildHarpoon(): void {
    const g = this.harpoon;
    const X = 0.26;
    const steel = new THREE.MeshStandardMaterial({ color: 0xcfd6dc, metalness: 0.7, roughness: 0.32 });
    const black = new THREE.MeshStandardMaterial({ color: 0x23272b, roughness: 0.55, metalness: 0.2 });
    const barrelMat = new THREE.MeshStandardMaterial({ color: 0x2d3a48, roughness: 0.4, metalness: 0.4 });
    const yellow = new THREE.MeshStandardMaterial({ color: 0xf2c14e, roughness: 0.5 });
    const rubber = new THREE.MeshStandardMaterial({ color: 0xb5482f, roughness: 0.65 });
    const along = (r: number, r2: number, z0: number, z1: number, y: number, mat: THREE.Material, seg = 16): THREE.Mesh => {
      const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r2, Math.abs(z1 - z0), seg), mat);
      m.rotation.x = -Math.PI / 2; // the +y end of the cylinder points forward
      m.position.set(X, y, (z0 + z1) / 2);
      return m;
    };
    // the barrel and the blunt muzzle block the bands loop through
    g.add(along(0.026, 0.026, -0.8, -1.32, -0.275, barrelMat));
    g.add(along(0.04, 0.036, -1.32, -1.37, -0.268, black));
    g.add(along(0.042, 0.042, -1.335, -1.35, -0.268, yellow));
    // the receiver and grip in one moulded piece, raked back like a pistol's
    g.add(
      sideCut(
        (o) => {
          o.moveTo(0.53, -0.25);
          o.lineTo(0.82, -0.25);
          o.quadraticCurveTo(0.845, -0.25, 0.845, -0.275);
          o.quadraticCurveTo(0.845, -0.3, 0.82, -0.3);
          o.lineTo(0.68, -0.3);
          o.lineTo(0.625, -0.45);
          o.quadraticCurveTo(0.612, -0.48, 0.585, -0.48);
          o.lineTo(0.55, -0.48);
          o.quadraticCurveTo(0.525, -0.48, 0.532, -0.45);
          o.lineTo(0.568, -0.3);
          o.quadraticCurveTo(0.51, -0.295, 0.51, -0.27);
          o.quadraticCurveTo(0.51, -0.25, 0.53, -0.25);
        },
        X,
        0.06,
        black,
      ),
    );
    // the trigger and its guard, in the gap in front of the grip
    const guard = new THREE.Mesh(new THREE.TorusGeometry(0.052, 0.007, 8, 18, Math.PI * 1.05), black);
    guard.rotation.set(0, Math.PI / 2, Math.PI);
    guard.position.set(X, -0.3, -0.735);
    const trigger = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.045, 0.012), yellow);
    trigger.position.set(X, -0.325, -0.725);
    trigger.rotation.x = -0.25;
    g.add(guard, trigger);
    // the spear lies along the top, with a barbed point and a flopper
    g.add(along(0.0065, 0.0065, -0.5, -1.56, -0.232, steel, 8));
    const point = along(0.0135, 0.0065, -1.61, -1.56, -0.232, steel, 10);
    point.rotation.x = Math.PI / 2;
    g.add(point);
    const tip = new THREE.Mesh(new THREE.ConeGeometry(0.0135, 0.075, 10), steel);
    tip.rotation.x = -Math.PI / 2;
    tip.position.set(X, -0.232, -1.645);
    g.add(tip);
    for (const side of [-1, 1]) {
      const barb = new THREE.Mesh(new THREE.BoxGeometry(0.004, 0.006, 0.08), steel);
      barb.position.set(X + side * 0.013, -0.232, -1.6);
      barb.rotation.y = -side * 0.3;
      g.add(barb);
      // a rubber band stretched from the muzzle back to a notch in the spear
      const band = new THREE.Mesh(
        new THREE.TubeGeometry(new THREE.CatmullRomCurve3([v(X + side * 0.036, -0.262, -1.35), v(X + side * 0.024, -0.25, -1.1), v(X + side * 0.008, -0.235, -0.86)]), 16, 0.0065, 8),
        rubber,
      );
      g.add(band);
    }
    // the line reel under the barrel
    const reel = new THREE.Mesh(new THREE.CylinderGeometry(0.038, 0.038, 0.03, 18), yellow);
    reel.rotation.z = Math.PI / 2;
    reel.position.set(X, -0.33, -1.0);
    const reelHub = new THREE.Mesh(new THREE.CylinderGeometry(0.014, 0.014, 0.04, 10), black);
    reelHub.rotation.z = Math.PI / 2;
    reelHub.position.copy(reel.position);
    const reelArm = limb(v(X, -0.3, -1.0), v(X, -0.33, -1.0), 0.008, black);
    g.add(reel, reelHub, reelArm);
    this.armOn(g, v(X, -0.385, -0.598), v(0, Math.cos(0.3), -Math.sin(0.3)), v(0.56, -0.95, 0.35), 1, 0.028, 1, DIVER);
    // turned in toward the aim, so the barrel and spear run across the view
    g.position.set(0.13, 0.04, 0);
    g.rotation.y = 0.17;
    g.visible = false;
  }

  /** A red pail with a yellow spade sticking out of it. */
  private buildBucket(): void {
    const g = this.bucket;
    const red = new THREE.MeshStandardMaterial({ color: 0xe5453a, roughness: 0.5 });
    const white = new THREE.MeshStandardMaterial({ color: 0xf6efe0, roughness: 0.6 });
    const yellow = new THREE.MeshStandardMaterial({ color: 0xf2c14e, roughness: 0.55 });
    const sand = new THREE.MeshStandardMaterial({ color: 0xdcc38f, roughness: 1 });
    const pail = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.09, 0.2, 18, 1, true), red);
    pail.material.side = THREE.DoubleSide;
    pail.position.set(0.3, -0.38, -0.55);
    const rim = new THREE.Mesh(new THREE.TorusGeometry(0.12, 0.012, 6, 20), white);
    rim.rotation.x = Math.PI / 2;
    rim.position.set(0.3, -0.28, -0.55);
    const band = new THREE.Mesh(new THREE.CylinderGeometry(0.108, 0.1, 0.035, 18, 1, true), white);
    band.position.set(0.3, -0.38, -0.55);
    const fill = new THREE.Mesh(new THREE.CircleGeometry(0.115, 18), sand);
    fill.rotation.x = -Math.PI / 2;
    fill.position.set(0.3, -0.3, -0.55);
    const bottom = new THREE.Mesh(new THREE.CircleGeometry(0.09, 16), red);
    bottom.rotation.x = Math.PI / 2;
    bottom.position.set(0.3, -0.48, -0.55);
    const handle = new THREE.Mesh(new THREE.TorusGeometry(0.12, 0.008, 6, 18, Math.PI), new THREE.MeshStandardMaterial({ color: 0xc9ced3, metalness: 0.7, roughness: 0.4 }));
    handle.position.set(0.3, -0.28, -0.55);
    handle.rotation.set(0, 0, 0);
    const stick = new THREE.Mesh(new THREE.CylinderGeometry(0.01, 0.01, 0.36, 8), yellow);
    stick.position.set(0.23, -0.15, -0.58);
    stick.rotation.set(0.1, 0, 0.35);
    const blade = new THREE.Mesh(new THREE.BoxGeometry(0.09, 0.012, 0.11), yellow);
    blade.position.set(0.17, 0.02, -0.6);
    blade.rotation.set(0.2, 0, 0.35);
    g.add(pail, rim, band, fill, bottom, handle, stick, blade);
    // carried by the top of the bail, hanging a little lower in the view
    this.armOn(g, v(0.33, -0.163, -0.55), v(-1, 0, 0), v(0.66, -0.95, 0.2), 1, 0.008);
    g.position.set(-0.08, -0.06, 0);
    g.visible = false;
  }

  private buildFlag(): void {
    const g = this.flag;
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.01, 0.012, 0.6, 8), new THREE.MeshStandardMaterial({ color: 0xd9c9a3, roughness: 0.8 }));
    pole.position.set(0.26, -0.27, -0.55);
    pole.rotation.x = -0.25;
    const shape = new THREE.Shape();
    shape.moveTo(0, 0);
    shape.lineTo(-0.1, -0.025);
    shape.lineTo(0, -0.05);
    shape.closePath();
    const cloth = new THREE.Mesh(new THREE.ShapeGeometry(shape), new THREE.MeshStandardMaterial({ color: 0xe63946, roughness: 0.7, side: THREE.DoubleSide }));
    cloth.position.set(0.252, 0.015, -0.63);
    g.add(pole, cloth);
    const up = v(0, Math.cos(0.25), -Math.sin(0.25));
    this.armOn(g, pole.position.clone().addScaledVector(up, -0.07), up, v(0.64, -0.95, 0.2), 1, 0.012);
  }

  private buildDetector(): void {
    const g = this.detector;
    const rubber = new THREE.MeshStandardMaterial({ color: 0x1f2124, roughness: 0.85 });
    const body = new THREE.MeshStandardMaterial({ color: 0x3a4048, roughness: 0.45, metalness: 0.5 });
    const steel = new THREE.MeshStandardMaterial({ color: 0xcdd3da, roughness: 0.3, metalness: 0.9 });
    const yellow = new THREE.MeshStandardMaterial({ color: 0xe6b422, roughness: 0.5 });
    const tube = (pts: THREE.Vector3[], r: number, mat: THREE.Material): THREE.Mesh =>
      new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 24, r, 8), mat);

    // upper S-shaft: armrest, hand grip, control box
    g.add(tube([v(0.34, -0.5, -0.12), v(0.31, -0.34, -0.36), v(0.3, -0.29, -0.55), v(0.26, -0.28, -0.8)], 0.017, steel));
    // foam grip with ridges
    g.add(limb(v(0.3, -0.3, -0.4), v(0.3, -0.29, -0.6), 0.034, rubber));
    for (let i = 0; i < 4; i++) {
      const ring = new THREE.Mesh(new THREE.TorusGeometry(0.035, 0.006, 6, 14), yellow);
      ring.position.set(0.3, -0.295, -0.43 - i * 0.045);
      g.add(ring);
    }
    // padded armrest cradle with a strap
    const cuff = new THREE.Mesh(new THREE.TorusGeometry(0.095, 0.02, 8, 18, Math.PI), rubber);
    cuff.position.set(0.37, -0.5, -0.12);
    cuff.rotation.z = Math.PI;
    const strap = new THREE.Mesh(new THREE.TorusGeometry(0.1, 0.008, 6, 18, Math.PI), yellow);
    strap.position.copy(cuff.position);
    g.add(cuff, strap);

    // control box with a glowing screen, knobs and a signal LED
    this.detectorGold = [body, steel];
    const box = new THREE.Mesh(new THREE.BoxGeometry(0.15, 0.075, 0.17), body);
    box.position.set(0.25, -0.27, -0.88);
    g.add(box);
    this.screenMat = new THREE.MeshStandardMaterial({ color: 0x0c1a12, emissive: 0x39ff88, emissiveIntensity: 0.25 });
    const screen = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.004, 0.07), this.screenMat);
    screen.position.set(0.25, -0.23, -0.9);
    g.add(screen);
    for (const kx of [0.21, 0.29]) {
      const knob = new THREE.Mesh(new THREE.CylinderGeometry(0.016, 0.016, 0.02, 10), yellow);
      knob.position.set(kx, -0.23, -0.82);
      g.add(knob);
    }
    this.ledMat = new THREE.MeshStandardMaterial({ color: 0x401010, emissive: 0xff3030, emissiveIntensity: 0.1 });
    const led = new THREE.Mesh(new THREE.SphereGeometry(0.011, 8, 6), this.ledMat);
    led.position.set(0.25, -0.23, -0.95);
    g.add(led);

    // lower shaft with a cam-lock collar, then the search coil
    const neck = v(0.04, -0.52, -1.62);
    g.add(limb(v(0.24, -0.29, -0.96), neck, 0.014, steel));
    const collar = new THREE.Mesh(new THREE.CylinderGeometry(0.022, 0.022, 0.05, 10), yellow);
    collar.position.set(0.15, -0.4, -1.28);
    collar.quaternion.setFromUnitVectors(Y, neck.clone().sub(v(0.24, -0.29, -0.96)).normalize());
    g.add(collar);

    const coil = new THREE.Group();
    coil.position.set(0.04, -0.57, -1.7);
    coil.rotation.x = -0.22;
    const disc = new THREE.Mesh(new THREE.CylinderGeometry(0.27, 0.27, 0.035, 36), rubber);
    disc.scale.set(1.18, 1, 1);
    const rim = new THREE.Mesh(new THREE.TorusGeometry(0.27, 0.012, 8, 36), yellow);
    rim.rotation.x = Math.PI / 2;
    rim.scale.set(1.18, 1, 1);
    rim.position.y = 0.02;
    const hub = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.07, 0.045, 14), body);
    hub.position.y = 0.012;
    const bolt = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.02, 0.05, 6), steel);
    bolt.position.y = 0.02;
    const yoke = new THREE.Mesh(new THREE.BoxGeometry(0.08, 0.05, 0.1), body);
    yoke.position.set(0, 0.04, 0.2);
    coil.add(disc, rim, hub, bolt, yoke);
    g.add(coil);
    this.coil = coil;
    g.scale.setScalar(0.78);
    g.position.set(0.04, -0.13, -0.26);

    // coil cable looping back to the control box
    g.add(tube([v(0.14, -0.55, -1.55), v(0.2, -0.5, -1.3), v(0.31, -0.4, -1.05), v(0.3, -0.29, -0.95)], 0.006, rubber));

    // the right hand on the grip, its forearm lying in the armrest
    this.armOn(g, v(0.3, -0.295, -0.5), v(0, 0.05, -1), v(0.43, -0.72, 0.3), 1, 0.034, 0.78, BARE, HANDSHAKE);
    // the other hand steadies the shaft from the left, just behind the control box
    this.armOn(g, v(0.272, -0.284, -0.72), v(0.16, -0.04, 1), v(-0.3, -1.1, 0.05), -1, 0.0175, 0.78);
  }

  private coil = new THREE.Group();
  private screenMat = new THREE.MeshStandardMaterial();
  private ledMat = new THREE.MeshStandardMaterial();
  private signal = 0;
  private signalKind: string | null = null;
  private clock = 0;

  /** Lets the detector's own screen and LED react to what the coil is sensing. */
  setSignal(strength: number, kind: string | null): void {
    this.signal = strength;
    this.signalKind = kind;
  }

  /** A garden trowel: a shaped orange handle, a steel neck that cranks down, and a dished, pointed blade. */
  private buildTrowel(): void {
    const g = this.trowel;
    const orange = new THREE.MeshStandardMaterial({ color: 0xe0732f, roughness: 0.55 });
    this.trowelGrip = orange;
    const steel = new THREE.MeshStandardMaterial({ color: 0xd6dbe1, roughness: 0.42, metalness: 0.35, side: THREE.DoubleSide });

    const tool = new THREE.Group();
    // the handle swells in the palm, narrows, and flares at the butt; lathed along y, then laid along z with the blade toward -z
    const profile = [[0.017, -0.08], [0.0185, -0.06], [0.0215, -0.03], [0.0225, 0], [0.0215, 0.03], [0.0195, 0.052], [0.0215, 0.068], [0.024, 0.076], [0.02, 0.084], [0, 0.087]];
    const handleGeo = new THREE.LatheGeometry(profile.map(([r, y]) => new THREE.Vector2(r, y)), 18);
    handleGeo.rotateX(Math.PI / 2);
    const handle = new THREE.Mesh(handleGeo, orange);
    const ferrule = new THREE.Mesh(new THREE.CylinderGeometry(0.0185, 0.0185, 0.03, 16), steel);
    ferrule.rotation.x = Math.PI / 2;
    ferrule.position.z = -0.09;
    // the neck drops from the ferrule to the blade, then carries on as the pressed spine, flattening and fading out along the hollow
    const neckCurve = new THREE.CatmullRomCurve3([v(0, 0, -0.095), v(0, -0.004, -0.112), v(0, -0.03, -0.128), v(0, -0.046, -0.146), v(0, -0.0525, -0.17), v(0, -0.0565, -0.21), v(0, -0.058, -0.26), v(0, -0.058, -0.31)]);
    const neckPos: number[] = [];
    const neckIdx: number[] = [];
    const steps = 48;
    const around = 10;
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      const c = neckCurve.getPointAt(t);
      const tan = neckCurve.getTangentAt(t);
      const fade = Math.min(1, (1 - t) / 0.55);
      const r = 0.0075 * (0.02 + 0.98 * fade * fade * (3 - 2 * fade));
      const up = 1 - 0.45 * Math.min(1, t / 0.5);
      for (let j = 0; j < around; j++) {
        const th = (j / around) * Math.PI * 2;
        const side = Math.cos(th) * r * (1 + (1 - fade) * 0.6);
        const out = Math.sin(th) * r * up;
        neckPos.push(side, c.y + tan.z * out, c.z - tan.y * out);
        if (i < steps) {
          const a = i * around + j;
          const b = i * around + ((j + 1) % around);
          neckIdx.push(a, a + around, b, b, a + around, b + around);
        }
      }
    }
    const neckGeo = new THREE.BufferGeometry();
    neckGeo.setAttribute('position', new THREE.Float32BufferAttribute(neckPos, 3));
    neckGeo.setIndex(neckIdx);
    neckGeo.computeVertexNormals();
    const neck = new THREE.Mesh(neckGeo, steel);
    // the blade is one dished sheet that grows out of the neck: it swells to rounded shoulders, then tapers to a point, straight to the tip.
    // It is brushed steel, darker down the hollow and lighter toward the edges, with a fine rolled rim around it
    const LEN = 0.23;
    const halfWidth = (u: number): number => 0.064 * (0.14 + 0.86 * Math.sin(Math.min(1, u / 0.17) * Math.PI * 0.5)) * Math.pow(1 - Math.pow(u, 1.6), 0.9);
    const lift = (u: number, s: number): number => -0.058 + 0.012 * Math.pow(Math.max(0, 1 - u / 0.17), 2) + 0.024 * (halfWidth(u) / 0.064) * s * s;
    const atZ = (u: number): number => -0.15 - u * LEN;
    const hollow = new THREE.Color(0x87909b);
    const edge = new THREE.Color(0xd9dee3);
    const shade = new THREE.Color();
    const pos: number[] = [];
    const col: number[] = [];
    const idx: number[] = [];
    const rows = 26;
    const cols = 14;
    for (let i = 0; i <= rows; i++) {
      const u = i / rows;
      for (let j = 0; j <= cols; j++) {
        const s = (j / cols) * 2 - 1;
        pos.push(s * halfWidth(u), lift(u, s), atZ(u));
        shade.copy(hollow).lerp(edge, s * s);
        col.push(shade.r, shade.g, shade.b);
      }
    }
    for (let i = 0; i < rows; i++) {
      for (let j = 0; j < cols; j++) {
        const a = i * (cols + 1) + j;
        const b = a + cols + 1;
        idx.push(a, b, a + 1, a + 1, b, b + 1);
      }
    }
    const bladeGeo = new THREE.BufferGeometry();
    bladeGeo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    bladeGeo.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
    bladeGeo.setIndex(idx);
    bladeGeo.computeVertexNormals();
    const bladeMat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.4, metalness: 0.4, side: THREE.DoubleSide });
    this.trowelSteel = [steel, bladeMat];
    const blade = new THREE.Mesh(bladeGeo, bladeMat);
    const outline: THREE.Vector3[] = [];
    for (const side of [-1, 1]) {
      for (let i = 0; i <= rows; i++) {
        const u = side < 0 ? i / rows : 1 - i / rows;
        outline.push(v(side * halfWidth(u), lift(u, side), atZ(u)));
      }
    }
    const rim = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(outline, true, 'catmullrom', 0.2), 120, 0.002, 6, true), steel);
    tool.add(handle, ferrule, neck, blade, rim);
    // held out ahead with the tip turned in toward the middle of the view, rolled a little so the face of the blade shows
    tool.position.set(0.19, -0.26, -0.6);
    tool.rotation.x = -0.04;
    tool.rotation.y = 0.5;
    tool.rotation.z = 0.3;
    tool.updateMatrix();
    g.add(tool);
    this.armOn(g, v(0, 0, 0.015).applyMatrix4(tool.matrix), v(0, 0, -1).transformDirection(tool.matrix), v(0.56, -0.68, 0.25), 1, 0.021, 1, BARE, HANDSHAKE);
  }

  /** Stretches a unit-height cylinder between two points. */
  private stretch(m: THREE.Mesh, a: THREE.Vector3, b: THREE.Vector3): void {
    const d = b.clone().sub(a);
    m.position.copy(a).add(b).multiplyScalar(0.5);
    m.quaternion.setFromUnitVectors(Y, d.clone().normalize());
    m.scale.y = d.length();
  }

  /** A short wooden bow with a nocked arrow. The string and the drawing hand follow `setDraw`. */
  private buildBow(): void {
    const g = this.bow;
    const wood = new THREE.MeshStandardMaterial({ color: 0x7a4a24, roughness: 0.7 });
    const leather = new THREE.MeshStandardMaterial({ color: 0x3a2616, roughness: 0.9 });
    const stringMat = new THREE.MeshStandardMaterial({ color: 0xf1ead6, roughness: 0.9 });
    // limbs: tips curve back toward the archer (+z), the grip bulges forward
    const curve = new THREE.CatmullRomCurve3([v(0, 0.46, 0.15), v(0, 0.28, -0.02), v(0, 0, -0.07), v(0, -0.28, -0.02), v(0, -0.46, 0.15)]);
    const limbs = new THREE.Mesh(new THREE.TubeGeometry(curve, 28, 0.017, 8), wood);
    limbs.castShadow = true;
    const grip = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.15, 10), leather);
    grip.position.set(0, 0, -0.07);
    g.add(limbs, grip);
    for (let i = 0; i < 2; i++) {
      const s = new THREE.Mesh(new THREE.CylinderGeometry(0.003, 0.003, 1, 4), stringMat);
      this.bowString.push(s);
      g.add(s);
    }
    this.bowArrow = buildArrow();
    this.bowArrow.rotation.y = Math.PI; // tip forward (-z)
    g.add(this.bowArrow);
    // the left hand holds the grip, the right hooks the string; its forearm follows the draw
    this.armOn(g, v(0, -0.01, -0.07), v(0, 1, 0), v(-0.8, -1.2, 1.1), -1, 0.03, BOW_SCALE);
    const start = v(0, BOW_HAND_Y, 0.15);
    const right = this.hold(g, start, v(0, 1, 0), BOW_ELBOW, 1, 0.006, BOW_SCALE);
    this.bowHand = right.hand;
    this.bowWrist = right.wrist.sub(start);
    const k = 1 / BOW_SCALE;
    this.bowForearm = new THREE.Mesh(new THREE.CylinderGeometry(ELBOW_R * k, WRIST_R * k, 1, 14), SKIN);
    this.bowSleeve = new THREE.Mesh(new THREE.CylinderGeometry((ELBOW_R + 0.007) * k, (ELBOW_R + 0.001) * k, 1, 14), SLEEVE);
    g.add(this.bowForearm, this.bowSleeve);
    g.rotation.set(0.0, -0.12, -0.12);
    g.scale.setScalar(BOW_SCALE);
    g.visible = false;
    this.setDraw(0);
  }

  /** 0 relaxed .. 1 fully drawn. */
  setDraw(d: number): void {
    const nock = v(0, 0, 0.15 + d * 0.3);
    this.stretch(this.bowString[0], v(0, 0.46, 0.15), nock);
    this.stretch(this.bowString[1], v(0, -0.46, 0.15), nock);
    this.bowArrow.position.set(0, 0, nock.z - 0.38);
    this.bowHand.position.set(0, BOW_HAND_Y, nock.z);
    const wrist = this.bowHand.position.clone().add(this.bowWrist);
    this.stretch(this.bowForearm, wrist, BOW_ELBOW);
    this.stretch(this.bowSleeve, wrist.clone().lerp(BOW_ELBOW, 0.42), BOW_ELBOW);
    // drawing the bow pulls it a little toward the shooter and lifts it
    this.bow.position.set(-0.16, -0.27 + d * 0.01, -0.95 + d * 0.04);
  }

  /** The kick of letting the string go. */
  releaseBow(): void {
    this.kickT = 0.6;
  }

  /** Binoculars held in both hands, resting in front of the chest until they are raised. */
  private buildBinoculars(): void {
    const g = this.binoc;
    const body = new THREE.MeshStandardMaterial({ color: 0x2b3430, roughness: 0.6, metalness: 0.3 });
    const metal = new THREE.MeshStandardMaterial({ color: 0x15181a, roughness: 0.35, metalness: 0.8 });
    const glass = new THREE.MeshPhysicalMaterial({ color: 0x6aa8d8, roughness: 0.05, metalness: 0.2, clearcoat: 1, transparent: true, opacity: 0.85 });
    for (const sx of [-1, 1]) {
      const x = sx * 0.085;
      const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.062, 0.058, 0.32, 14), body);
      barrel.rotation.x = Math.PI / 2;
      barrel.position.set(x, -0.2, -0.58);
      const lens = new THREE.Mesh(new THREE.CylinderGeometry(0.072, 0.072, 0.07, 14), metal);
      lens.rotation.x = Math.PI / 2;
      lens.position.set(x, -0.2, -0.76);
      const glassDisc = new THREE.Mesh(new THREE.CylinderGeometry(0.058, 0.058, 0.01, 14), glass);
      glassDisc.rotation.x = Math.PI / 2;
      glassDisc.position.set(x, -0.2, -0.797);
      const eye = new THREE.Mesh(new THREE.CylinderGeometry(0.046, 0.05, 0.07, 12), metal);
      eye.rotation.x = Math.PI / 2;
      eye.position.set(x, -0.2, -0.4);
      g.add(barrel, lens, glassDisc, eye);
      // a hand around each barrel from underneath
      this.armOn(g, v(x, -0.2, -0.56), v(0, 0, 1), v(sx * 0.8, -1.35, 0.2), sx < 0 ? -1 : 1, 0.06, BINOC_SCALE);
    }
    const bridge = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.045, 0.2), body);
    bridge.position.set(0, -0.2, -0.58);
    const wheel = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.045, 10), metal);
    wheel.position.set(0, -0.172, -0.6);
    g.add(bridge, wheel);
    g.rotation.x = 0.12;
    g.scale.setScalar(BINOC_SCALE);
    g.position.set(0, -0.16, -0.16);
    g.visible = false;
  }

  /** The gold finishes from the Luxury shop. */
  setGolden(detector: boolean, trowel: boolean): void {
    const gold = 0xf0c24a;
    if (detector) for (const m of this.detectorGold) m.color.set(gold);
    if (trowel) {
      for (const m of this.trowelSteel) m.color.set(gold);
      this.trowelGrip.color.set(0x4a2c17);
    }
  }

  /** Hides the hand-held model while the player is looking through the binoculars. */
  setRaised(raised: boolean): void {
    this.raised = raised;
    this.group.visible = this.tool !== 0 && !raised;
  }

  /** A chunky plastic toy pistol: a rounded red body, a blue grip, a yellow barrel tip so nobody mistakes it, and a little hammer. */
  private buildGun(): void {
    const g = this.gun;
    const X = 0.26;
    const red = new THREE.MeshStandardMaterial({ color: 0xdf4a35, roughness: 0.38 });
    const blue = new THREE.MeshStandardMaterial({ color: 0x2f79c9, roughness: 0.45 });
    const yellow = new THREE.MeshStandardMaterial({ color: 0xf7c948, roughness: 0.42 });
    const dark = new THREE.MeshStandardMaterial({ color: 0x2b2f35, roughness: 0.5 });
    const body = sideCut(
      (o) => {
        o.moveTo(0.44, -0.232);
        o.lineTo(0.85, -0.232);
        o.quadraticCurveTo(0.885, -0.232, 0.885, -0.262);
        o.lineTo(0.885, -0.282);
        o.quadraticCurveTo(0.885, -0.31, 0.855, -0.31);
        o.lineTo(0.47, -0.31);
        o.quadraticCurveTo(0.41, -0.31, 0.41, -0.268);
        o.quadraticCurveTo(0.41, -0.232, 0.44, -0.232);
      },
      X,
      0.082,
      red,
      0.018,
    );
    // the grip rakes back toward the shooter's hand
    const handle = sideCut(
      (o) => {
        o.moveTo(0.585, -0.3);
        o.lineTo(0.525, -0.465);
        o.quadraticCurveTo(0.512, -0.495, 0.485, -0.495);
        o.lineTo(0.44, -0.495);
        o.quadraticCurveTo(0.41, -0.495, 0.42, -0.465);
        o.lineTo(0.465, -0.3);
        o.closePath();
      },
      X,
      0.066,
      blue,
      0.016,
    );
    const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.031, 0.031, 0.075, 18), red);
    barrel.rotation.x = Math.PI / 2;
    barrel.position.set(X, -0.27, -0.91);
    const tip = new THREE.Mesh(new THREE.CylinderGeometry(0.037, 0.037, 0.03, 18), yellow);
    tip.rotation.x = Math.PI / 2;
    tip.position.set(X, -0.27, -0.95);
    const bore = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.018, 0.005, 14), dark);
    bore.rotation.x = Math.PI / 2;
    bore.position.set(X, -0.27, -0.967);
    const stripe = sideCut(
      (o) => {
        o.moveTo(0.46, -0.262);
        o.lineTo(0.84, -0.262);
        o.lineTo(0.84, -0.274);
        o.lineTo(0.46, -0.274);
        o.closePath();
      },
      X,
      0.086,
      yellow,
      0.001,
    );
    const guard = new THREE.Mesh(new THREE.TorusGeometry(0.052, 0.009, 8, 18, Math.PI * 1.05), red);
    guard.rotation.set(0, Math.PI / 2, Math.PI);
    guard.position.set(X, -0.305, -0.645);
    const trigger = new THREE.Mesh(new THREE.BoxGeometry(0.014, 0.05, 0.016), yellow);
    trigger.position.set(X, -0.332, -0.635);
    trigger.rotation.x = -0.25;
    const hammer = new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.042, 0.03), dark);
    hammer.position.set(X, -0.225, -0.43);
    hammer.rotation.x = 0.4;
    const sight = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.022, 0.03), yellow);
    sight.position.set(X, -0.222, -0.84);
    g.add(body, handle, barrel, tip, bore, stripe, guard, trigger, hammer, sight);
    this.armOn(g, v(X, -0.39, -0.48), v(0, Math.cos(0.3), -Math.sin(0.3)), v(0.62, -1.05, 0.3), 1, 0.03, 0.72);
    this.flash = new THREE.Mesh(
      new THREE.ConeGeometry(0.06, 0.2, 8),
      new THREE.MeshBasicMaterial({ color: 0xffd27a, transparent: true, opacity: 0.9, blending: THREE.AdditiveBlending, depthWrite: false }),
    );
    this.flash.rotation.x = -Math.PI / 2;
    this.flash.position.set(0.26, -0.27, -1.0);
    this.flash.visible = false;
    g.add(this.flash);
    this.muzzle.position.set(0.26, -0.27, -0.96);
    g.add(this.muzzle);
    g.scale.setScalar(0.72);
    // held out to the right and turned in toward the aim, so its side shows rather than just its back
    g.position.set(0.16, -0.03, -0.12);
    g.rotation.y = 0.28;
  }

  /** Recoil and muzzle flash. */
  fire(): void {
    this.kickT = 1;
    this.flashT = 0.06;
  }

  private buildProxy(): void {
    const mat = new THREE.MeshBasicMaterial({ colorWrite: false, depthWrite: false });
    const part = (geo: THREE.BufferGeometry, p: THREE.Vector3, parent: THREE.Object3D = this.proxy): THREE.Mesh => {
      const m = new THREE.Mesh(geo, mat);
      m.position.copy(p);
      m.castShadow = true;
      parent.add(m);
      return m;
    };
    part(new THREE.CapsuleGeometry(0.2, 0.55, 4, 8), v(0, 1.15, 0));
    for (const s of [-1, 1]) this.legs.push(part(new THREE.CapsuleGeometry(0.085, 0.6, 4, 8), v(s * 0.1, 0.4, 0)));
    this.proxy.add(this.proxyHead);
    this.proxyHead.position.set(0, 1.58, 0);
    part(new THREE.SphereGeometry(0.13, 10, 8), v(0, 0.08, 0), this.proxyHead);
    // arms reaching forward to the held tool
    for (const s of [-1, 1]) {
      const a = limb(v(s * 0.26, 1.35, 0), v(s * 0.2, 1.1, -0.45), 0.065, mat);
      a.castShadow = true;
      this.proxy.add(a);
    }
    const stick = limb(v(0.2, 1.1, -0.45), v(0.05, 0.12, -1.35), 0.025, mat);
    stick.castShadow = true;
    const disc = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.3, 0.04, 20), mat);
    disc.position.set(0.05, 0.12, -1.35);
    disc.castShadow = true;
    this.proxyDetector.add(stick, disc);
    const stick2 = limb(v(0.2, 1.1, -0.45), v(0.12, 0.55, -1.0), 0.025, mat);
    stick2.castShadow = true;
    const scoop = new THREE.Mesh(new THREE.BoxGeometry(0.11, 0.012, 0.3), mat);
    scoop.position.set(0.12, 0.45, -1.1);
    scoop.rotation.x = -0.5;
    scoop.castShadow = true;
    this.proxyTrowel.add(stick2, scoop);
    const pistol = limb(v(0.2, 1.1, -0.45), v(0.2, 1.14, -0.9), 0.04, mat);
    pistol.castShadow = true;
    this.proxyGun.add(pistol);
    this.proxy.add(this.proxyDetector, this.proxyTrowel, this.proxyGun);
  }

  /** 0 hides both tools. */
  setTool(n: 0 | 1 | 2 | 4 | 5 | 6 | 7 | 8 | 9): void {
    this.tool = n;
    this.detector.visible = this.proxyDetector.visible = n === 1;
    this.trowel.visible = this.proxyTrowel.visible = n === 2;
    this.gun.visible = this.proxyGun.visible = n === 4;
    this.binoc.visible = n === 5;
    this.bow.visible = n === 6;
    this.flag.visible = n === 7;
    this.bucket.visible = n === 8;
    this.harpoon.visible = n === 9;
    this.group.visible = n !== 0 && !(n === 5 && this.raised);
  }

  doSwing(): void {
    this.swing = 1;
  }

  /** Places the shadow body at the player's feet. */
  update(dt: number, moving: boolean, feet: THREE.Vector3, yaw: number, pitch: number): void {
    this.moving += ((moving ? 1 : 0) - this.moving) * Math.min(1, dt * 8);
    if (this.swim) this.bob += dt * (1.6 + this.moving * 1.4);
    else if (moving) this.bob += dt * 9;
    this.clock += dt;
    this.swing = Math.max(0, this.swing - dt * 3.5);
    if (this.tool === 1) {
      // the coil drifts gently from side to side, like a slow sweep
      this.detector.rotation.y = Math.sin(this.clock * 1.1) * 0.05;
      this.coil.rotation.z = Math.sin(this.clock * 1.1 + 1) * 0.04;
      const pulse = this.signal > 0 ? 0.5 + 0.5 * Math.sin(this.clock * (6 + this.signal * 22)) : 0;
      this.ledMat.emissiveIntensity = 0.1 + pulse * (0.6 + this.signal * 1.6);
      this.ledMat.emissive.set(this.signalKind === 'trash' ? 0xff9a3c : this.signalKind === 'coin' ? 0x7aff7a : this.signalKind === 'deep' ? 0xc79bff : 0xff3030);
      this.screenMat.emissiveIntensity = 0.25 + this.signal * 1.4;
      this.screenMat.emissive.set(this.signalKind === 'trash' ? 0xff9a3c : this.signalKind === 'deep' ? 0xc79bff : 0x39ff88);
    }
    this.kickT = Math.max(0, this.kickT - dt * 7);
    this.flashT = Math.max(0, this.flashT - dt);
    this.flash.visible = this.flashT > 0;
    const s = Math.sin(this.swing * Math.PI);
    // underwater the hands always drift a little, and sway wider and slower when kicking
    const sw = this.swim ? 0.5 + this.moving : this.moving;
    const amp = this.swim ? 2.6 : 1;
    // the trowel stabs forward and a little down, so it stays in view through the dig
    const dig = this.tool === 2;
    this.root.position.set(Math.cos(this.bob * 0.5) * 0.012 * sw * amp, 0.2 + Math.sin(this.bob) * 0.014 * sw * amp - s * (dig ? 0.03 : 0.06), 0.1 + s * (dig ? -0.08 : 0.1));
    this.root.rotation.x = dig ? -s * 0.12 : this.tool === 4 || this.tool === 9 ? this.kickT * 0.22 : Math.sin(this.bob * 0.5) * 0.01 * sw * amp;
    if (this.tool === 4 || this.tool === 9) this.root.position.z += this.kickT * 0.08;
    this.root.rotation.z = Math.cos(this.bob * 0.5) * 0.01 * sw * amp;

    this.proxy.position.copy(feet);
    this.proxy.rotation.y = yaw;
    this.proxyHead.rotation.x = pitch;
    const stride = Math.sin(this.bob) * 0.45 * this.moving;
    this.legs[0].rotation.x = stride;
    this.legs[1].rotation.x = -stride;
    this.proxyTrowel.rotation.x = -s * 0.5;
  }
}
