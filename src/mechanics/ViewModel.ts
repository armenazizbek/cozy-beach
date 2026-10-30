import * as THREE from 'three';
import { buildArrow } from './Arrows';

const Y = new THREE.Vector3(0, 1, 0);
const SLEEVE = new THREE.MeshStandardMaterial({ color: 0x4f7396, roughness: 0.9 });
const SKIN = new THREE.MeshStandardMaterial({ color: 0xe0b08a, roughness: 0.8 });
const DARK = new THREE.MeshStandardMaterial({ color: 0x3a3f45, roughness: 0.5, metalness: 0.6 });
const BRASS = new THREE.MeshStandardMaterial({ color: 0xb08a4a, roughness: 0.4, metalness: 0.7 });

/** A cylinder stretched between two points. */
const limb = (a: THREE.Vector3, b: THREE.Vector3, r: number, mat: THREE.Material): THREE.Mesh => {
  const dir = b.clone().sub(a);
  const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, dir.length(), 10), mat);
  m.position.copy(a).add(b).multiplyScalar(0.5);
  m.quaternion.setFromUnitVectors(Y, dir.normalize());
  return m;
};

const ball = (p: THREE.Vector3, r: number, mat: THREE.Material): THREE.Mesh => {
  const m = new THREE.Mesh(new THREE.SphereGeometry(r, 12, 10), mat);
  m.position.copy(p);
  return m;
};

const v = (x: number, y: number, z: number): THREE.Vector3 => new THREE.Vector3(x, y, z);

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
  private bowForearm = new THREE.Mesh();
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

  /** A diver's spear gun: a long steel barrel with a spear in it, a black housing and a pistol grip. */
  private buildHarpoon(): void {
    const g = this.harpoon;
    const steel = new THREE.MeshStandardMaterial({ color: 0xcfd6dc, metalness: 0.85, roughness: 0.3 });
    const black = new THREE.MeshStandardMaterial({ color: 0x23272b, roughness: 0.6, metalness: 0.2 });
    const yellow = new THREE.MeshStandardMaterial({ color: 0xf2c14e, roughness: 0.5 });
    const skin = new THREE.MeshStandardMaterial({ color: 0xd9a47a, roughness: 0.8 });
    const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.02, 0.95, 10), steel);
    barrel.rotation.x = Math.PI / 2;
    barrel.position.set(0.26, -0.26, -0.95);
    const tip = new THREE.Mesh(new THREE.ConeGeometry(0.032, 0.2, 8), steel);
    tip.rotation.x = -Math.PI / 2;
    tip.position.set(0.26, -0.26, -1.52);
    const housing = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.06, 0.28), black);
    housing.position.set(0.26, -0.285, -0.68);
    const grip = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.12, 0.05), black);
    grip.position.set(0.26, -0.36, -0.6);
    grip.rotation.x = 0.25;
    const band = new THREE.Mesh(new THREE.TorusGeometry(0.03, 0.008, 6, 14), yellow);
    band.position.set(0.26, -0.26, -0.85);
    const band2 = band.clone();
    band2.position.z = -1.15;
    const hand = new THREE.Mesh(new THREE.SphereGeometry(0.045, 8, 6), skin);
    hand.position.set(0.26, -0.4, -0.58);
    // the arm in a wetsuit sleeve, coming up from the bottom right corner of the view, and a glove on the hand
    const suit = new THREE.MeshStandardMaterial({ color: 0x1f3a4d, roughness: 0.7 });
    const cuff = new THREE.MeshStandardMaterial({ color: 0xf2c14e, roughness: 0.6 });
    const glove = new THREE.MeshStandardMaterial({ color: 0x22262b, roughness: 0.8 });
    (hand as THREE.Mesh).material = glove;
    hand.scale.set(1.15, 1, 1.3);
    const wrist = v(0.27, -0.42, -0.5);
    const elbow = v(0.42, -0.62, 0.05);
    const arm = limb(wrist, elbow, 0.052, suit);
    const sleeve = limb(wrist, v(0.31, -0.49, -0.34), 0.058, cuff);
    const upper = limb(elbow, v(0.5, -0.85, 0.5), 0.062, suit);
    g.add(barrel, tip, housing, grip, band, band2, hand, arm, sleeve, upper);
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
    stick.position.set(0.37, -0.15, -0.58);
    stick.rotation.set(0.1, 0, -0.35);
    const blade = new THREE.Mesh(new THREE.BoxGeometry(0.09, 0.012, 0.11), yellow);
    blade.position.set(0.43, 0.02, -0.6);
    blade.rotation.set(0.2, 0, -0.35);
    g.add(pail, rim, band, fill, bottom, handle, stick, blade);
    g.visible = false;
  }

  private buildFlag(): void {
    const g = this.flag;
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.01, 0.012, 0.6, 8), new THREE.MeshStandardMaterial({ color: 0xd9c9a3, roughness: 0.8 }));
    pole.position.set(0.3, -0.45, -0.55);
    pole.rotation.x = -0.25;
    const shape = new THREE.Shape();
    shape.moveTo(0, 0);
    shape.lineTo(-0.1, -0.025);
    shape.lineTo(0, -0.05);
    shape.closePath();
    const cloth = new THREE.Mesh(new THREE.ShapeGeometry(shape), new THREE.MeshStandardMaterial({ color: 0xe63946, roughness: 0.7, side: THREE.DoubleSide }));
    cloth.position.set(0.292, -0.165, -0.63);
    g.add(pole, cloth);
    // the grip hand
    const hand = new THREE.Mesh(new THREE.SphereGeometry(0.04, 8, 6), new THREE.MeshStandardMaterial({ color: 0xd9a47a, roughness: 0.8 }));
    hand.position.set(0.3, -0.5, -0.5);
    g.add(hand);
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

    // hands
    g.add(limb(v(0.5, -0.9, 0.3), v(0.34, -0.5, -0.1), 0.034, SLEEVE));
    g.add(limb(v(0.34, -0.5, -0.1), v(0.31, -0.31, -0.45), 0.032, SLEEVE), ball(v(0.3, -0.3, -0.48), 0.052, SKIN));
    g.add(limb(v(-0.1, -0.95, 0.2), v(0.2, -0.3, -0.82), 0.032, SLEEVE), ball(v(0.19, -0.29, -0.83), 0.05, SKIN));
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

  /** A garden trowel: orange grip, steel ferrule and shank, and a flat pointed blade. */
  private buildTrowel(): void {
    const g = this.trowel;
    const grip = new THREE.MeshStandardMaterial({ color: 0xe0732f, roughness: 0.55 });
    const steel = new THREE.MeshStandardMaterial({ color: 0xc4cad1, roughness: 0.28, metalness: 0.9 });

    const tool = new THREE.Group();
    const handle = new THREE.Mesh(new THREE.CylinderGeometry(0.034, 0.028, 0.22, 14), grip);
    handle.rotation.x = Math.PI / 2;
    handle.position.z = 0.02;
    const cap = new THREE.Mesh(new THREE.SphereGeometry(0.034, 12, 8), grip);
    cap.position.z = 0.13;
    const ferrule = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.04, 14), steel);
    ferrule.rotation.x = Math.PI / 2;
    ferrule.position.z = -0.1;
    const shank = limb(v(0, 0, -0.11), v(0, -0.045, -0.25), 0.012, steel);

    // leaf-shaped blade, drawn in XY (tip at +y) then laid flat so the tip points forward (-z)
    const shape = new THREE.Shape();
    shape.moveTo(-0.032, 0);
    shape.lineTo(-0.058, 0.07);
    shape.quadraticCurveTo(-0.062, 0.19, 0, 0.32);
    shape.quadraticCurveTo(0.062, 0.19, 0.058, 0.07);
    shape.lineTo(0.032, 0);
    shape.closePath();
    const bladeGeo = new THREE.ExtrudeGeometry(shape, { depth: 0.008, bevelEnabled: true, bevelSize: 0.004, bevelThickness: 0.003, bevelSegments: 1 });
    bladeGeo.rotateX(-Math.PI / 2);
    const blade = new THREE.Mesh(bladeGeo, steel);
    blade.position.set(0, -0.055, -0.235);
    blade.rotation.x = -0.18;
    // a raised rib down the middle gives it the scooped look
    const rib = new THREE.Mesh(new THREE.BoxGeometry(0.014, 0.012, 0.2), steel);
    rib.position.set(0, -0.052, -0.34);
    rib.rotation.x = -0.18;
    tool.add(handle, cap, ferrule, shank, blade, rib);
    // held with the tip pointing forward and down
    tool.position.set(0.26, -0.17, -0.55);
    tool.rotation.x = -0.32;
    tool.rotation.y = 0.06;
    g.add(tool);

    // right hand wraps the grip, left hand rests nearby
    g.add(limb(v(0.5, -0.9, 0.2), v(0.27, -0.2, -0.5), 0.04, SLEEVE), ball(v(0.26, -0.17, -0.52), 0.052, SKIN));
    g.add(limb(v(-0.45, -0.95, 0.2), v(-0.25, -0.45, -0.55), 0.04, SLEEVE), ball(v(-0.24, -0.44, -0.57), 0.05, SKIN));
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
    // hands: the left holds the grip, the right draws the string
    g.add(ball(v(0.0, -0.02, -0.07), 0.07, SKIN));
    g.add(limb(v(-0.55, -1.0, 1.0), v(-0.01, -0.06, -0.04), 0.055, SLEEVE));
    this.bowHand = ball(v(0, 0, 0), 0.065, SKIN);
    g.add(this.bowHand);
    this.bowForearm = new THREE.Mesh(new THREE.CylinderGeometry(0.055, 0.055, 1, 10), SLEEVE);
    g.add(this.bowForearm);
    g.position.set(-0.1, -0.2, -0.95);
    g.rotation.set(0.0, 0.08, -0.08);
    g.scale.setScalar(0.55);
    g.visible = false;
    this.setDraw(0);
  }

  /** 0 relaxed .. 1 fully drawn. */
  setDraw(d: number): void {
    const nock = v(0, 0, 0.15 + d * 0.3);
    this.stretch(this.bowString[0], v(0, 0.46, 0.15), nock);
    this.stretch(this.bowString[1], v(0, -0.46, 0.15), nock);
    this.bowArrow.position.set(0, 0, nock.z - 0.38);
    const hand = v(0.03, -0.02, nock.z + 0.03);
    this.bowHand.position.copy(hand);
    this.stretch(this.bowForearm, v(0.55, -1.0, 1.0), hand);
    // drawing the bow pulls it a little toward the shooter and lifts it
    this.bow.position.set(-0.1, -0.2 + d * 0.01, -0.95 + d * 0.04);
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
      // a hand cupping each barrel from underneath
      g.add(ball(v(sx * 0.1, -0.27, -0.55), 0.058, SKIN));
      g.add(limb(v(sx * 0.42, -0.9, 0.15), v(sx * 0.12, -0.3, -0.5), 0.036, SLEEVE));
    }
    const bridge = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.045, 0.2), body);
    bridge.position.set(0, -0.2, -0.58);
    const wheel = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 0.045, 10), metal);
    wheel.position.set(0, -0.172, -0.6);
    g.add(bridge, wheel);
    g.rotation.x = 0.12;
    g.scale.setScalar(0.5);
    g.position.set(0, -0.12, -0.1);
    g.visible = false;
  }

  /** Hides the hand-held model while the player is looking through the binoculars. */
  setRaised(raised: boolean): void {
    this.raised = raised;
    this.group.visible = this.tool !== 0 && !raised;
  }

  private buildGun(): void {
    const g = this.gun;
    const metal = new THREE.MeshStandardMaterial({ color: 0x2f3338, roughness: 0.35, metalness: 0.8 });
    const grip = new THREE.MeshStandardMaterial({ color: 0x7a4a2a, roughness: 0.8 });
    const slide = new THREE.Mesh(new THREE.BoxGeometry(0.065, 0.075, 0.36), metal);
    slide.position.set(0.26, -0.27, -0.64);
    const barrel = new THREE.Mesh(new THREE.CylinderGeometry(0.022, 0.022, 0.12, 10), metal);
    barrel.rotation.x = Math.PI / 2;
    barrel.position.set(0.26, -0.27, -0.88);
    const handle = new THREE.Mesh(new THREE.BoxGeometry(0.055, 0.17, 0.075), grip);
    handle.position.set(0.28, -0.39, -0.5);
    handle.rotation.x = 0.28;
    const sight = new THREE.Mesh(new THREE.BoxGeometry(0.014, 0.02, 0.02), metal);
    sight.position.set(0.26, -0.225, -0.8);
    g.add(slide, barrel, handle, sight);
    g.add(limb(v(0.5, -0.9, 0.2), v(0.29, -0.37, -0.48), 0.04, SLEEVE), ball(v(0.29, -0.36, -0.5), 0.05, SKIN));
    g.add(limb(v(-0.1, -0.95, 0.2), v(0.23, -0.4, -0.52), 0.04, SLEEVE), ball(v(0.23, -0.39, -0.54), 0.05, SKIN));
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
    g.position.set(-0.02, -0.02, -0.1);
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
    this.root.position.set(Math.cos(this.bob * 0.5) * 0.012 * sw * amp, 0.2 + Math.sin(this.bob) * 0.014 * sw * amp - s * 0.06, 0.1 + s * 0.1);
    this.root.rotation.x = this.tool === 2 ? -s * 0.55 : this.tool === 4 || this.tool === 9 ? this.kickT * 0.22 : Math.sin(this.bob * 0.5) * 0.01 * sw * amp;
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
