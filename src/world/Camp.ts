import * as THREE from 'three';
import { ParticleSystem } from '../core/Particles';
import { CAMP_Y, CAMP_Z } from './Beach';

export type CampKind = 'fire' | 'bin' | 'board' | 'bench' | 'lantern' | 'camper' | 'radio' | 'coffee' | 'book' | 'post' | 'hammock' | 'sign';

const wood = (c = 0x6b4a2f): THREE.MeshStandardMaterial =>
  new THREE.MeshStandardMaterial({ color: c, roughness: 0.9, metalness: 0 });

const box = (w: number, h: number, d: number, mat: THREE.Material, x = 0, y = 0, z = 0): THREE.Mesh => {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
  m.position.set(x, y, z);
  m.castShadow = true;
  m.receiveShadow = true;
  return m;
};

const cyl = (rt: number, rb: number, h: number, mat: THREE.Material, seg = 12): THREE.Mesh => {
  const m = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, h, seg), mat);
  m.castShadow = true;
  m.receiveShadow = true;
  return m;
};

export class Camp {
  readonly group = new THREE.Group();
  readonly clickables: THREE.Object3D[] = [];
  /** circular footprints in world space, for player collision */
  readonly obstacles: { x: number; z: number; r: number }[] = [];
  private windows: THREE.MeshStandardMaterial[] = [];
  private camperLight = new THREE.PointLight(0xffc070, 0, 9, 2);
  readonly firePos = new THREE.Vector3(0, CAMP_Y + 0.5, CAMP_Z);
  private flames: THREE.Mesh[] = [];
  private radio = new THREE.Group();
  private radioLed = new THREE.MeshStandardMaterial({ color: 0x401010, emissive: 0xff3a2a, emissiveIntensity: 0 });
  private radioAntenna = new THREE.Group();
  private radioOn = false;
  private bookCover = new THREE.MeshStandardMaterial({ color: 0x7b2d26, roughness: 0.7, emissive: 0xffb347, emissiveIntensity: 0 });
  private bookNew = false;
  private mailFlag = new THREE.Group();
  private mailNew = false;
  private hammockGroup = new THREE.Group();
  private hammockCloth = new THREE.Group();
  private hammockOccupied = false;
  /** where the hammock sits in the camp, and which way it runs */
  private hammockAt = { x: 0, z: -5.2, rot: 0.1 };
  private fireLight: THREE.PointLight;
  private rugGroup = new THREE.Group();
  private lightsGroup = new THREE.Group();
  private signGroup = new THREE.Group();
  private bulbs: THREE.MeshStandardMaterial[] = [];
  private signMat = new THREE.MeshStandardMaterial({ roughness: 0.8 });
  private signText = '';
  private extras = new Map<string, { group: THREE.Group; solids: { x: number; z: number; r: number }[] }>();
  private tikiFlames: THREE.Mesh[] = [];
  private decorSolids = {
    lights: [{ x: -6.2, z: CAMP_Z - 4.2, r: 0.3 }, { x: 6.0, z: CAMP_Z - 4.8, r: 0.3 }],
    sign: [{ x: 4.6, z: CAMP_Z + 6.4, r: 0.4 }],
  };
  private lanternLight: THREE.PointLight;
  private lanternGlass: THREE.Mesh;
  private embers: ParticleSystem;
  private shelf = new THREE.Group();
  private trophies = new Map<string, THREE.Object3D>();
  private ember = new THREE.Color(0xff9a3c);
  private time = 0;
  private emberTimer = 0;

  constructor() {
    this.group.position.set(0, CAMP_Y, CAMP_Z);

    this.buildFire();
    this.buildBench();
    this.buildBin();
    this.buildBoard();
    this.buildPost();
    this.buildHammock();
    this.buildCamper();
    this.lanternGlass = new THREE.Mesh();
    this.lanternLight = new THREE.PointLight(0xffc874, 0, 12, 2);
    this.buildLantern();
    this.buildDecor();

    // solid footprints for the other props, so neither walking nor driving passes through them
    const solid = (lx: number, lz: number, r: number): void => {
      this.obstacles.push({ x: lx, z: CAMP_Z + lz, r });
    };
    solid(-12.4, 4.4, 0.7); // recycling bin
    solid(-4.7, 1.2, 1.0); // notice board
    solid(3.7, 0.8, 1.3); // workbench (and the journal on it)
    solid(1.7, 0.9, 0.45); // coffee stump
    solid(2.2, -1.6, 0.25); // lantern pole
    solid(6.3, 3.4, 0.3); // the post
    for (const s of [-1, 1]) {
      // the two hammock posts
      const h = this.hammockAt;
      solid(h.x + Math.cos(h.rot) * 1.7 * s, h.z - Math.sin(h.rot) * 1.7 * s, 0.25);
    }

    this.fireLight = new THREE.PointLight(0xff8a3a, 20, 14, 2);
    this.fireLight.position.set(0, 0.9, 0);
    this.group.add(this.fireLight);

    this.embers = new ParticleSystem(80, 0.07, -0.4, true);
    this.group.add(this.embers.points);
  }

  private mark(obj: THREE.Object3D, kind: CampKind, radius = 1.1, y = 0.8): void {
    obj.userData.kind = kind;
    this.clickables.push(obj);
    // generous invisible hit proxy
    const proxy = new THREE.Mesh(new THREE.SphereGeometry(radius, 8, 6), new THREE.MeshBasicMaterial({ visible: false }));
    proxy.position.y = y;
    obj.add(proxy);
  }

  kindOf(obj: THREE.Object3D | null): CampKind | null {
    let o: THREE.Object3D | null = obj;
    while (o) {
      if (o.userData.kind) return o.userData.kind as CampKind;
      o = o.parent;
    }
    return null;
  }

  private buildFire(): void {
    const g = new THREE.Group();
    const logMat = wood(0x5a3b24);
    for (let i = 0; i < 6; i++) {
      const log = cyl(0.11, 0.12, 1.2, logMat, 8);
      const a = (i / 6) * Math.PI * 2;
      log.position.set(Math.cos(a) * 0.35, 0.18, Math.sin(a) * 0.35);
      log.rotation.z = Math.PI / 2 - 0.35;
      log.rotation.y = -a;
      g.add(log);
    }
    const stoneMat = new THREE.MeshStandardMaterial({ color: 0x8b8680, roughness: 1 });
    for (let i = 0; i < 12; i++) {
      const a = (i / 12) * Math.PI * 2;
      const s = new THREE.Mesh(new THREE.DodecahedronGeometry(0.2 + Math.random() * 0.06), stoneMat);
      s.position.set(Math.cos(a) * 0.95, 0.08, Math.sin(a) * 0.95);
      s.rotation.set(Math.random(), Math.random(), Math.random());
      s.castShadow = true;
      s.receiveShadow = true;
      g.add(s);
    }
    const glow = new THREE.Mesh(
      new THREE.CircleGeometry(0.8, 20),
      new THREE.MeshBasicMaterial({ color: 0x2a1408 }),
    );
    glow.rotation.x = -Math.PI / 2;
    glow.position.y = 0.03;
    g.add(glow);
    const flameCols = [0xd9380f, 0xe0701a, 0xe8a33a];
    flameCols.forEach((c, i) => {
      const f = new THREE.Mesh(
        new THREE.ConeGeometry(0.34 - i * 0.07, 1.1 - i * 0.2, 10, 1, true),
        new THREE.MeshBasicMaterial({
          color: c, transparent: true, opacity: 0.55, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide,
        }),
      );
      f.position.y = 0.65 + i * 0.04;
      this.flames.push(f);
      g.add(f);
    });
    // Tin mug on a stump beside the fire
    // the coffee mug on its own stump is a separate thing to interact with
    const cup = new THREE.Group();
    const stump = cyl(0.3, 0.34, 0.4, wood(0x7a5538), 14);
    stump.position.y = 0.2;
    const mug = cyl(0.1, 0.09, 0.2, new THREE.MeshStandardMaterial({ color: 0xc9ced3, metalness: 0.8, roughness: 0.35 }), 14);
    mug.position.y = 0.5;
    const handle = new THREE.Mesh(new THREE.TorusGeometry(0.05, 0.012, 6, 12, Math.PI), new THREE.MeshStandardMaterial({ color: 0xc9ced3, metalness: 0.8, roughness: 0.35 }));
    handle.position.set(0.1, 0.5, 0);
    handle.rotation.z = -Math.PI / 2;
    const coffee = new THREE.Mesh(new THREE.CircleGeometry(0.085, 14), new THREE.MeshStandardMaterial({ color: 0x3a2112, roughness: 0.3 }));
    coffee.rotation.x = -Math.PI / 2;
    coffee.position.y = 0.595;
    cup.add(stump, mug, handle, coffee);
    cup.position.set(1.7, 0, 0.9);
    this.mark(cup, 'coffee', 0.4, 0.5);
    this.group.add(cup);
    for (const a of [Math.PI * 0.9, Math.PI * 1.35]) {
      const s = cyl(0.32, 0.36, 0.42, wood(0x7a5538), 14);
      s.position.set(Math.cos(a) * 2, 0.21, Math.sin(a) * 2);
      g.add(s);
    }
    this.mark(g, 'fire');
    this.group.add(g);
  }


  private buildBench(): void {
    const g = new THREE.Group();
    const top = box(2.2, 0.12, 1.0, wood(0x8a6240), 0, 1.0, 0);
    g.add(top);
    for (const sx of [-1, 1]) {
      for (const sz of [-0.38, 0.38]) g.add(box(0.12, 1.0, 0.12, wood(0x5e4128), sx * 0.95, 0.5, sz));
    }
    g.add(box(2.0, 0.08, 0.7, wood(0x5e4128), 0, 0.35, 0));
    // back shelf for trophies
    g.add(box(2.2, 0.08, 0.4, wood(0x7a5538), 0, 2.0, -0.4));
    g.add(box(0.08, 1.0, 0.08, wood(0x5e4128), -1.0, 1.5, -0.55), box(0.08, 1.0, 0.08, wood(0x5e4128), 1.0, 1.5, -0.55));
    this.shelf.position.set(0, 2.04, -0.4);
    g.add(this.shelf);
    g.position.set(3.7, 0, 0.8);
    g.rotation.y = -0.5;
    this.buildRadio(g);
    this.buildBook(g);
    this.mark(g, 'bench');
    this.group.add(g);
  }

  /** A chunky retro radio on the workbench. Click it to play music. */
  private buildRadio(bench: THREE.Group): void {
    const r = this.radio;
    const shell = new THREE.MeshStandardMaterial({ color: 0xd9704a, roughness: 0.5 });
    const cream = new THREE.MeshStandardMaterial({ color: 0xf3ead2, roughness: 0.6 });
    const dark = new THREE.MeshStandardMaterial({ color: 0x2b2420, roughness: 0.8 });
    r.add(box(0.56, 0.3, 0.2, shell, 0, 0.15, 0));
    r.add(box(0.5, 0.24, 0.02, cream, 0, 0.15, 0.105));
    for (let i = 0; i < 6; i++) r.add(box(0.2, 0.02, 0.012, dark, -0.12, 0.07 + i * 0.035, 0.12));
    r.add(box(0.17, 0.07, 0.012, dark, 0.14, 0.23, 0.12));
    r.add(box(0.012, 0.07, 0.014, new THREE.MeshStandardMaterial({ color: 0xffd25a, emissive: 0xffa020, emissiveIntensity: 0.4 }), 0.12, 0.23, 0.125));
    for (const kx of [0.09, 0.2]) {
      const knob = cyl(0.032, 0.032, 0.03, dark, 12);
      knob.rotation.x = Math.PI / 2;
      knob.position.set(kx, 0.1, 0.13);
      r.add(knob);
    }
    const led = new THREE.Mesh(new THREE.SphereGeometry(0.016, 8, 6), this.radioLed);
    led.position.set(0.23, 0.2, 0.125);
    r.add(led);
    const handle = new THREE.Mesh(new THREE.TorusGeometry(0.12, 0.012, 6, 14, Math.PI), dark);
    handle.position.set(0, 0.3, 0);
    r.add(handle);
    // the aerial pivots at its foot, which sits in a small socket on the shell, so it reads as part of the radio
    const metal = new THREE.MeshStandardMaterial({ color: 0xc9ced3, metalness: 0.8, roughness: 0.3 });
    const socket = cyl(0.022, 0.028, 0.03, dark, 10);
    socket.position.set(0.2, 0.312, -0.05);
    r.add(socket);
    const rod = cyl(0.006, 0.006, 0.5, metal, 6);
    rod.position.y = 0.23;
    const tip = new THREE.Mesh(new THREE.SphereGeometry(0.014, 8, 6), metal);
    tip.position.y = 0.48;
    const stub = cyl(0.011, 0.011, 0.05, metal, 8);
    stub.position.y = 0.01;
    this.radioAntenna.add(rod, tip, stub);
    this.radioAntenna.position.set(0.2, 0.32, -0.05);
    this.radioAntenna.rotation.z = -0.35;
    r.add(this.radioAntenna);
    r.position.set(0.72, 1.06, -0.12);
    r.rotation.y = 0.15;
    this.mark(r, 'radio', 0.4, 0.2);
    bench.add(r);
  }

  // ---------------------------------------------------------------- decorations

  private buildDecor(): void {
    // the rug: woven stripes with a border and fringe
    const cv = document.createElement('canvas');
    cv.width = 512;
    cv.height = 320;
    const c = cv.getContext('2d')!;
    c.fillStyle = '#b8453a';
    c.fillRect(0, 0, 512, 320);
    c.fillStyle = '#f0dcae';
    c.fillRect(16, 16, 480, 288);
    const stripes = ['#2f6f8a', '#e3a43b', '#b8453a', '#4a8a5a', '#2f6f8a'];
    stripes.forEach((col, i) => {
      c.fillStyle = col;
      c.fillRect(34, 34 + i * 50, 444, 36);
    });
    c.fillStyle = '#f0dcae';
    for (let x = 48; x < 470; x += 38) {
      c.beginPath();
      c.moveTo(x, 160);
      c.lineTo(x + 12, 148);
      c.lineTo(x + 24, 160);
      c.lineTo(x + 12, 172);
      c.fill();
    }
    c.strokeStyle = 'rgba(0,0,0,0.07)';
    for (let y = 0; y < 320; y += 4) {
      c.beginPath();
      c.moveTo(0, y);
      c.lineTo(512, y);
      c.stroke();
    }
    const tex = new THREE.CanvasTexture(cv);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 4;
    const edge = (): THREE.MeshStandardMaterial => new THREE.MeshStandardMaterial({ color: 0xb8453a, roughness: 1 });
    const rug = new THREE.Mesh(new THREE.BoxGeometry(3.6, 0.05, 2.25), [
      edge(), edge(),
      new THREE.MeshStandardMaterial({ map: tex, roughness: 1, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 }),
      edge(), edge(), edge(),
    ]);
    rug.position.y = 0.04;
    rug.receiveShadow = true;
    this.rugGroup.add(rug);
    // fringe at the two short ends
    const fringe = new THREE.MeshStandardMaterial({ color: 0xf0dcae, roughness: 1 });
    for (const sx of [-1, 1]) {
      for (let i = 0; i < 16; i++) {
        const f = new THREE.Mesh(new THREE.BoxGeometry(0.12, 0.012, 0.025), fringe);
        f.position.set(sx * 1.86, 0.05, -1.05 + i * 0.14);
        this.rugGroup.add(f);
      }
    }
    this.rugGroup.position.set(-0.6, 0, 4.4);
    this.rugGroup.rotation.y = 0.22;
    this.group.add(this.rugGroup);

    // string lights: two poles and a sagging wire with a bulb every little while
    const pole = new THREE.MeshStandardMaterial({ color: 0x6b4a2f, roughness: 0.9 });
    const ends: THREE.Vector3[] = [];
    for (const s of this.decorSolids.lights) {
      const lx = s.x;
      const lz = s.z - CAMP_Z;
      const p = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.09, 3.3, 8), pole);
      p.position.set(lx, 1.65, lz);
      p.castShadow = true;
      const cap = new THREE.Mesh(new THREE.SphereGeometry(0.09, 8, 6), pole);
      cap.position.set(lx, 3.32, lz);
      this.lightsGroup.add(p, cap);
      ends.push(new THREE.Vector3(lx, 3.2, lz));
    }
    const wireMat = new THREE.MeshStandardMaterial({ color: 0x1b1b1b, roughness: 0.6 });
    const at = (t: number, drop: number): THREE.Vector3 =>
      new THREE.Vector3().lerpVectors(ends[0], ends[1], t).add(new THREE.Vector3(0, -Math.sin(t * Math.PI) * 0.7 - drop, 0));
    const pts: THREE.Vector3[] = [];
    for (let i = 0; i <= 24; i++) pts.push(at(i / 24, 0));
    this.lightsGroup.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 48, 0.012, 5), wireMat));
    const colors = [0xffd27a, 0xff8a6a, 0x9fe08a, 0x8ac8ff, 0xffb0d0];
    for (let i = 1; i < 19; i++) {
      const col = colors[i % colors.length];
      const m = new THREE.MeshStandardMaterial({ color: col, emissive: col, emissiveIntensity: 0.2, roughness: 0.3 });
      this.bulbs.push(m);
      const b = new THREE.Mesh(new THREE.SphereGeometry(0.065, 8, 6), m);
      b.position.copy(at(i / 19, 0.07));
      const cap = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.025, 0.04, 6), wireMat);
      cap.position.copy(at(i / 19, 0)).add(new THREE.Vector3(0, -0.01, 0));
      this.lightsGroup.add(b, cap);
    }
    this.group.add(this.lightsGroup);
    this.buildExtras();

    // the sign: a post with a painted board, facing the sea
    // two posts stand behind the board, so nothing covers the writing
    const board = new THREE.Mesh(new THREE.BoxGeometry(1.7, 0.62, 0.07), [pole, pole, pole, pole, this.signMat, this.signMat]);
    board.position.set(0, 1.5, 0);
    board.castShadow = true;
    this.signGroup.add(board);
    for (const sx of [-0.7, 0.7]) {
      const post = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.08, 1.85, 8), pole);
      post.position.set(sx, 0.92, -0.1);
      post.castShadow = true;
      this.signGroup.add(post);
    }
    this.signGroup.position.set(this.decorSolids.sign[0].x, 0, this.decorSolids.sign[0].z - CAMP_Z);
    this.signGroup.rotation.y = 0.15;
    this.mark(this.signGroup, 'sign', 1.1, 1.5);
    this.group.add(this.signGroup);
    // only clickable once it has been bought
    this.clickables.splice(this.clickables.indexOf(this.signGroup), 1);
    this.setSignText('Cozy Camp');
    this.rugGroup.visible = this.lightsGroup.visible = this.signGroup.visible = false;
  }

  /** Adds one of the extra decorations: a group at a camp-local spot, with the solid footprints it should have. */
  private addExtra(id: string, group: THREE.Group, solids: [number, number, number][]): void {
    group.visible = false;
    this.group.add(group);
    this.extras.set(id, { group, solids: solids.map(([x, z, r]) => ({ x, z: CAMP_Z + z, r })) });
  }

  private buildExtras(): void {
    const wood = new THREE.MeshStandardMaterial({ color: 0x7a5538, roughness: 0.9 });
    const stripes = (a: string, b: string, n: number): THREE.CanvasTexture => {
      const cv = document.createElement('canvas');
      cv.width = 256;
      cv.height = 16;
      const c = cv.getContext('2d')!;
      const w = 256 / n;
      for (let i = 0; i < n; i++) {
        c.fillStyle = i % 2 ? b : a;
        c.fillRect(i * w, 0, w + 1, 16);
      }
      const t = new THREE.CanvasTexture(cv);
      t.colorSpace = THREE.SRGBColorSpace;
      t.wrapS = THREE.RepeatWrapping;
      return t;
    };

    // a cooler box
    {
      const g = new THREE.Group();
      const red = new THREE.MeshStandardMaterial({ color: 0xc8372e, roughness: 0.55 });
      const white = new THREE.MeshStandardMaterial({ color: 0xf3f0e8, roughness: 0.5 });
      const body = new THREE.Mesh(new THREE.BoxGeometry(0.78, 0.4, 0.46), red);
      body.position.y = 0.22;
      const lid = new THREE.Mesh(new THREE.BoxGeometry(0.82, 0.09, 0.5), white);
      lid.position.y = 0.47;
      const handle = new THREE.Mesh(new THREE.TorusGeometry(0.08, 0.016, 6, 12, Math.PI), white);
      handle.position.set(0.4, 0.34, 0);
      handle.rotation.y = Math.PI / 2;
      for (const m of [body, lid, handle]) m.castShadow = true;
      g.add(body, lid, handle);
      g.position.set(1.9, 0, 3.0);
      g.rotation.y = 0.5;
      this.addExtra('cooler', g, [[1.9, 3.0, 0.5]]);
    }

    // two potted palms
    {
      const g = new THREE.Group();
      const clay = new THREE.MeshStandardMaterial({ color: 0xb8623c, roughness: 0.85 });
      const soil = new THREE.MeshStandardMaterial({ color: 0x3a2a1c, roughness: 1 });
      const bark = new THREE.MeshStandardMaterial({ color: 0x8a6a45, roughness: 0.9 });
      const leaf = new THREE.MeshStandardMaterial({ color: 0x3f9a4a, roughness: 0.7, side: THREE.DoubleSide });
      for (const [x, z, s] of [[6.3, -1.3, 1], [-7.2, -1.6, 0.85]] as const) {
        const p = new THREE.Group();
        const pot = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.2, 0.42, 14), clay);
        pot.position.y = 0.21;
        const rim = new THREE.Mesh(new THREE.TorusGeometry(0.3, 0.035, 6, 16), clay);
        rim.rotation.x = Math.PI / 2;
        rim.position.y = 0.42;
        const dirt = new THREE.Mesh(new THREE.CircleGeometry(0.28, 14), soil);
        dirt.rotation.x = -Math.PI / 2;
        dirt.position.y = 0.41;
        const trunk = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.06, 0.95, 7), bark);
        trunk.position.set(0.04, 0.9, 0);
        trunk.rotation.z = -0.08;
        p.add(pot, rim, dirt, trunk);
        for (let i = 0; i < 8; i++) {
          const a = (i / 8) * Math.PI * 2;
          const f = new THREE.Mesh(new THREE.ConeGeometry(0.1, 0.95, 4), leaf);
          f.scale.set(1, 1, 0.22);
          f.geometry.translate(0, 0.47, 0);
          f.castShadow = true;
          const pivot = new THREE.Group();
          pivot.position.set(0.08, 1.36, 0);
          pivot.rotation.y = a;
          f.rotation.set(1.1, 0, 0);
          pivot.add(f);
          p.add(pivot);
        }
        p.position.set(x, 0, z);
        p.scale.setScalar(s);
        g.add(p);
      }
      this.addExtra('palms', g, [[6.3, -1.3, 0.45], [-7.2, -1.6, 0.4]]);
    }

    // a beach umbrella with a folding lounger
    {
      const g = new THREE.Group();
      const white = new THREE.MeshStandardMaterial({ color: 0xf4efe4, roughness: 0.6 });
      const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.025, 0.03, 2.4, 8), white);
      pole.position.y = 1.2;
      const canopyMat = new THREE.MeshStandardMaterial({ map: stripes('#d84a4a', '#f4efe4', 12), roughness: 0.75, side: THREE.DoubleSide });
      const canopy = new THREE.Mesh(new THREE.ConeGeometry(1.55, 0.55, 24, 1, true), canopyMat);
      canopy.position.y = 2.3;
      canopy.castShadow = true;
      const tip = new THREE.Mesh(new THREE.SphereGeometry(0.05, 8, 6), white);
      tip.position.y = 2.6;
      const um = new THREE.Group();
      um.add(pole, canopy, tip);
      um.rotation.z = 0.1;
      g.add(um);
      const fabric = new THREE.MeshStandardMaterial({ map: stripes('#2f78a8', '#f4efe4', 8), roughness: 0.8 });
      const seat = new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.05, 0.72), fabric);
      seat.position.set(0.3, 0.3, 0.9);
      const back = new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.05, 0.8), fabric);
      back.position.set(0.3, 0.62, 1.55);
      back.rotation.x = -0.95;
      g.add(seat, back);
      for (const [x, z] of [[0, 0.58], [0.6, 0.58], [0, 1.24], [0.6, 1.24]]) {
        const leg = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.3, 0.04), wood);
        leg.position.set(x, 0.15, z);
        g.add(leg);
      }
      g.position.set(-4.4, 0, 5.4);
      g.rotation.y = 0.3;
      this.addExtra('umbrella', g, [[-4.4, 5.4, 0.35]]);
    }

    // a surfboard rack
    {
      const g = new THREE.Group();
      for (const sx of [-0.85, 0.85]) {
        const post = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.06, 1.6, 7), wood);
        post.position.set(sx, 0.8, 0);
        post.castShadow = true;
        g.add(post);
      }
      for (const y of [0.45, 1.2]) {
        const bar = new THREE.Mesh(new THREE.CylinderGeometry(0.035, 0.035, 1.9, 6), wood);
        bar.rotation.z = Math.PI / 2;
        bar.position.set(0, y, 0);
        g.add(bar);
      }
      const colors = [0xe8604a, 0x2fa8a0, 0xf2c23a];
      colors.forEach((col, i) => {
        const board = new THREE.Mesh(new THREE.SphereGeometry(1, 20, 12), new THREE.MeshStandardMaterial({ color: col, roughness: 0.35 }));
        board.scale.set(0.25, 1.15, 0.04);
        board.position.set(-0.5 + i * 0.5, 1.2, 0.12);
        board.rotation.z = (i - 1) * 0.07;
        board.rotation.x = -0.1;
        board.castShadow = true;
        const stripe = new THREE.Mesh(new THREE.BoxGeometry(0.06, 1.6, 0.012), new THREE.MeshStandardMaterial({ color: 0xf6f1e4, roughness: 0.4 }));
        stripe.position.set(0, 0, 1.0);
        stripe.scale.set(0.24, 1.2, 0.2);
        board.add(stripe);
        g.add(board);
      });
      g.position.set(8.6, 0, -0.4);
      g.rotation.y = -0.9;
      this.addExtra('surf', g, [[8.6, -0.4, 0.8]]);
    }

    // tiki torches
    {
      const g = new THREE.Group();
      const bamboo = new THREE.MeshStandardMaterial({ color: 0xc8a45a, roughness: 0.8 });
      const dark = new THREE.MeshStandardMaterial({ color: 0x2a1c10, roughness: 0.9 });
      const flameMat = new THREE.MeshBasicMaterial({ color: 0xff9a2a, transparent: true, opacity: 0.92, fog: true });
      const coreMat = new THREE.MeshBasicMaterial({ color: 0xffe07a, transparent: true, opacity: 0.95 });
      for (const [x, z] of [[-3.2, 7.3], [1.8, 7.5]] as const) {
        const t = new THREE.Group();
        const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.055, 1.9, 8), bamboo);
        pole.position.y = 0.95;
        pole.castShadow = true;
        t.add(pole);
        for (const y of [0.4, 0.9, 1.4]) {
          const ring = new THREE.Mesh(new THREE.TorusGeometry(0.052, 0.012, 5, 10), dark);
          ring.rotation.x = Math.PI / 2;
          ring.position.y = y;
          t.add(ring);
        }
        const cup = new THREE.Mesh(new THREE.CylinderGeometry(0.11, 0.06, 0.16, 10, 1, true), dark);
        cup.material = new THREE.MeshStandardMaterial({ color: 0x2a1c10, roughness: 0.9, side: THREE.DoubleSide });
        cup.position.y = 1.98;
        const flame = new THREE.Mesh(new THREE.ConeGeometry(0.085, 0.32, 8), flameMat);
        flame.position.y = 2.17;
        const core = new THREE.Mesh(new THREE.ConeGeometry(0.045, 0.2, 6), coreMat);
        core.position.y = 2.12;
        t.add(cup, flame, core);
        this.tikiFlames.push(flame, core);
        t.position.set(x, 0, z);
        g.add(t);
      }
      this.addExtra('tiki', g, [[-3.2, 7.3, 0.2], [1.8, 7.5, 0.2]]);
    }
  }

  private setSignText(text: string): void {
    if (text === this.signText) return;
    this.signText = text;
    const cv = document.createElement('canvas');
    cv.width = 512;
    cv.height = 190;
    const c = cv.getContext('2d')!;
    c.fillStyle = '#c8935a';
    c.fillRect(0, 0, 512, 190);
    c.strokeStyle = 'rgba(80,45,20,0.25)';
    c.lineWidth = 2;
    for (let y = 20; y < 190; y += 26) {
      c.beginPath();
      c.moveTo(0, y);
      c.lineTo(512, y + 3);
      c.stroke();
    }
    c.strokeStyle = '#6b4325';
    c.lineWidth = 10;
    c.strokeRect(8, 8, 496, 174);
    c.fillStyle = '#3d2412';
    c.textAlign = 'center';
    c.textBaseline = 'middle';
    let size = 74;
    c.font = `bold ${size}px Georgia, serif`;
    while (c.measureText(text).width > 440 && size > 28) {
      size -= 4;
      c.font = `bold ${size}px Georgia, serif`;
    }
    c.fillText(text, 256, 98);
    const tex = new THREE.CanvasTexture(cv);
    tex.colorSpace = THREE.SRGBColorSpace;
    this.signMat.map?.dispose();
    this.signMat.map = tex;
    this.signMat.needsUpdate = true;
  }

  /** Shows the decorations the player has bought, and what the sign says. */
  setDecor(owned: string[], signText: string): void {
    this.rugGroup.visible = owned.includes('rug');
    this.lightsGroup.visible = owned.includes('lights');
    const sign = owned.includes('sign');
    this.signGroup.visible = sign;
    this.setSignText(signText);
    const has = this.clickables.includes(this.signGroup);
    if (sign && !has) this.clickables.push(this.signGroup);
    if (!sign && has) this.clickables.splice(this.clickables.indexOf(this.signGroup), 1);
    const set = (key: 'lights' | 'sign', on: boolean): void => {
      for (const s of this.decorSolids[key]) {
        const i = this.obstacles.indexOf(s);
        if (on && i < 0) this.obstacles.push(s);
        if (!on && i >= 0) this.obstacles.splice(i, 1);
      }
    };
    set('lights', owned.includes('lights'));
    set('sign', sign);
    for (const [id, e] of this.extras) {
      const on = owned.includes(id);
      e.group.visible = on;
      for (const s of e.solids) {
        const i = this.obstacles.indexOf(s);
        if (on && i < 0) this.obstacles.push(s);
        if (!on && i >= 0) this.obstacles.splice(i, 1);
      }
    }
  }

  /** A striped hammock slung between two posts on spreader bars. Press F to lie in it and sleep until morning. */
  private buildHammock(): void {
    const g = this.hammockGroup;
    const rope = new THREE.MeshStandardMaterial({ color: 0xcdbd95, roughness: 0.9 });
    const bar = wood(0x8a6240);
    const post = wood(0x6b4a2f);
    const half = 1.3; // half the length of the cloth, between the bars
    const wide = 0.4; // half its width
    const drop = 0.14; // how far the bars hang below the knots on the posts
    const sag = 0.6;
    const strand = (a: THREE.Vector3, b: THREE.Vector3, r: number, mat: THREE.Material): THREE.Mesh => {
      const d = b.clone().sub(a);
      const m = cyl(r, r, d.length(), mat, 6);
      m.position.copy(a).add(b).multiplyScalar(0.5);
      m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), d.normalize());
      return m;
    };
    // the cloth and everything that hangs with it swing from the line between the two knots
    this.hammockCloth.position.y = 1.52;
    for (const s of [-1, 1]) {
      const pole = cyl(0.07, 0.095, 2.05, post, 10);
      pole.position.set(s * 1.72, 1.02, 0);
      pole.rotation.z = -s * 0.03;
      pole.castShadow = true;
      g.add(pole);
      const top = new THREE.Mesh(new THREE.SphereGeometry(0.07, 10, 6), post);
      top.scale.y = 0.45;
      top.position.set(s * 1.75, 2.05, 0);
      g.add(top);
      // rope lashed around the post, then a fan of cords from the knot to the bar
      for (let i = 0; i < 3; i++) {
        const turn = new THREE.Mesh(new THREE.TorusGeometry(0.086, 0.011, 6, 16), rope);
        turn.rotation.x = Math.PI / 2;
        turn.position.set(s * 1.735, 1.5 + i * 0.022, 0);
        g.add(turn);
      }
      const knot = new THREE.Vector3(s * 1.655, 0, 0);
      const k = new THREE.Mesh(new THREE.SphereGeometry(0.026, 8, 6), rope);
      k.position.copy(knot);
      this.hammockCloth.add(k);
      for (let i = 0; i < 5; i++) this.hammockCloth.add(strand(knot, new THREE.Vector3(s * half, -drop, (i - 2) * 0.18), 0.007, rope));
      const spreader = cyl(0.02, 0.02, wide * 2 + 0.1, bar, 8);
      spreader.rotation.x = Math.PI / 2;
      spreader.position.set(s * half, -drop, 0);
      spreader.castShadow = true;
      this.hammockCloth.add(spreader);
    }
    // the stripes run the length of the cloth, the way a woven hammock's do
    const tex = (() => {
      const c = document.createElement('canvas');
      c.width = 8;
      c.height = 128;
      const x = c.getContext('2d')!;
      const cols = ['#d9704a', '#e9d9b4', '#3f9a8d', '#e9d9b4', '#d9704a', '#e9d9b4', '#3f9a8d', '#e9d9b4', '#d9704a'];
      cols.forEach((col, i) => {
        x.fillStyle = col;
        x.fillRect(0, (i * 128) / cols.length, 8, 128 / cols.length + 1);
      });
      const t = new THREE.CanvasTexture(c);
      t.colorSpace = THREE.SRGBColorSpace;
      return t;
    })();
    // the cloth hangs in a long curve between the bars, and its sides rise into a cradle in the middle
    const surface = (u: number, v: number): THREE.Vector3 => {
      const bowl = 1 - u * u;
      return new THREE.Vector3(u * half, -drop - sag * bowl + 0.2 * v * v * bowl, v * wide * (1 - 0.12 * bowl));
    };
    const geo = new THREE.PlaneGeometry(2 * half, 2 * wide, 32, 12);
    const p = geo.attributes.position as THREE.BufferAttribute;
    for (let i = 0; i < p.count; i++) {
      const at = surface(p.getX(i) / half, p.getY(i) / wide);
      p.setXYZ(i, at.x, at.y, at.z);
    }
    geo.computeVertexNormals();
    const cloth = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ map: tex, roughness: 0.9, side: THREE.DoubleSide }));
    cloth.castShadow = true;
    cloth.receiveShadow = true;
    this.hammockCloth.add(cloth);
    // a rope hem down each long edge gives the cloth some weight
    for (const v of [-1, 1]) {
      const pts: THREE.Vector3[] = [];
      for (let i = 0; i <= 20; i++) pts.push(surface(i / 10 - 1, v));
      const hem = new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 48, 0.012, 6), rope);
      this.hammockCloth.add(hem);
    }
    // a soft pillow up at the head end, plumped by squashing a ball and denting its middle
    const pillowGeo = new THREE.SphereGeometry(0.2, 18, 12);
    const q = pillowGeo.attributes.position as THREE.BufferAttribute;
    for (let i = 0; i < q.count; i++) {
      const dent = 1 - 0.28 * Math.exp(-(q.getX(i) ** 2 + q.getZ(i) ** 2) / 0.02);
      q.setY(i, q.getY(i) * dent);
    }
    pillowGeo.computeVertexNormals();
    const pillow = new THREE.Mesh(pillowGeo, new THREE.MeshStandardMaterial({ color: 0xf3ead2, roughness: 0.9 }));
    pillow.scale.set(0.95, 0.55, 1.45);
    const head = surface(-0.62, 0);
    pillow.position.set(head.x, head.y + 0.085, 0);
    pillow.rotation.z = -0.32;
    pillow.castShadow = true;
    this.hammockCloth.add(pillow);
    g.add(this.hammockCloth);
    g.position.set(this.hammockAt.x, 0, this.hammockAt.z);
    g.rotation.y = this.hammockAt.rot;
    this.mark(g, 'hammock', 1.3, 0.9);
    this.group.add(g);
  }

  /** World position and facing of someone lying in the hammock, and a spot to stand when getting out. */
  hammockSpot(): { x: number; y: number; z: number; yaw: number; exitX: number; exitZ: number } {
    this.group.updateWorldMatrix(true, true);
    const c = this.hammockGroup.localToWorld(new THREE.Vector3(0, 0.78, 0));
    const out = this.hammockGroup.localToWorld(new THREE.Vector3(0.2, 0, 1.6));
    return { x: c.x, y: c.y, z: c.z, yaw: this.hammockAt.rot - Math.PI / 2, exitX: out.x, exitZ: out.z };
  }

  setHammockOccupied(on: boolean): void {
    this.hammockOccupied = on;
  }

  /** A leaning post with a blue mailbox. Its red flag goes up when a letter has arrived. */
  private buildPost(): void {
    const g = new THREE.Group();
    const blue = new THREE.MeshStandardMaterial({ color: 0x3b6ea5, roughness: 0.55 });
    const red = new THREE.MeshStandardMaterial({ color: 0xd9382c, roughness: 0.5 });
    g.add(box(0.14, 1.3, 0.14, wood(0x6b4a2f), 0, 0.65, 0));
    g.add(box(0.5, 0.26, 0.3, blue, 0, 1.43, 0));
    const roof = new THREE.Mesh(new THREE.CylinderGeometry(0.15, 0.15, 0.5, 14, 1, false, 0, Math.PI), blue);
    roof.rotation.set(0, 0, Math.PI / 2);
    roof.position.set(0, 1.56, 0);
    roof.scale.set(1, 1, 1);
    roof.castShadow = true;
    g.add(roof);
    g.add(box(0.2, 0.02, 0.02, new THREE.MeshStandardMaterial({ color: 0x101820 }), 0, 1.45, 0.16));
    // the flag: a small red arm that swings up on a hinge
    this.mailFlag.position.set(0.27, 1.4, 0.0);
    this.mailFlag.add(box(0.03, 0.22, 0.03, red, 0, 0.11, 0), box(0.13, 0.08, 0.02, red, 0.065, 0.2, 0));
    g.add(this.mailFlag);
    g.position.set(6.3, 0, 3.4);
    g.rotation.y = -0.4;
    this.mark(g, 'post', 0.7, 1.2);
    this.group.add(g);
  }

  /** The red flag stands up while there is unread mail. */
  setMailNew(on: boolean): void {
    this.mailNew = on;
  }

  /** The camp journal: a closed book lying on the workbench beside the radio. */
  private buildBook(bench: THREE.Group): void {
    const b = new THREE.Group();
    const pages = new THREE.MeshStandardMaterial({ color: 0xf1e6c8, roughness: 0.9 });
    const gold = new THREE.MeshStandardMaterial({ color: 0xd6a94a, metalness: 0.6, roughness: 0.4 });
    b.add(box(0.37, 0.02, 0.27, this.bookCover, 0, 0.01, 0));
    b.add(box(0.33, 0.05, 0.245, pages, 0.012, 0.045, 0));
    b.add(box(0.37, 0.02, 0.27, this.bookCover, 0, 0.08, 0));
    b.add(box(0.03, 0.09, 0.27, this.bookCover, -0.175, 0.045, 0));
    b.add(box(0.2, 0.004, 0.12, gold, 0.02, 0.092, 0));
    // a ribbon marker hanging out of the bottom
    b.add(box(0.025, 0.004, 0.09, new THREE.MeshStandardMaterial({ color: 0xe0b04a, roughness: 0.6 }), 0.1, 0.002, 0.17));
    b.position.set(-0.05, 1.06, 0.12);
    b.rotation.y = -0.3;
    this.mark(b, 'book', 0.32, 0.12);
    bench.add(b);
  }

  /** The book glows softly while it holds pages the player has not read yet. */
  setBookNew(on: boolean): void {
    this.bookNew = on;
  }

  /** World position of the radio, used for distance-based volume. */
  radioPosition(out = new THREE.Vector3()): THREE.Vector3 {
    return this.radio.getWorldPosition(out);
  }

  setRadio(on: boolean): void {
    this.radioOn = on;
  }

  private buildBin(): void {
    const g = new THREE.Group();
    const green = new THREE.MeshStandardMaterial({ color: 0x3f8f5a, roughness: 0.7 });
    const body = cyl(0.5, 0.42, 0.9, green, 16);
    body.position.y = 0.45;
    const lid = cyl(0.54, 0.54, 0.08, new THREE.MeshStandardMaterial({ color: 0x2f7347, roughness: 0.7 }), 16);
    lid.position.y = 0.94;
    // the recycling symbol, painted on: a patch of the bin's own curved wall, a hair proud of it
    const sign = new THREE.Mesh(
      new THREE.CylinderGeometry(0.494, 0.45, 0.5, 8, 1, true, -0.54, 1.08),
      new THREE.MeshStandardMaterial({ map: this.recycleLogo(), alphaTest: 0.5, roughness: 0.7 }),
    );
    sign.position.y = 0.45;
    g.add(body, lid, sign);
    // this spot is outside the levelled camp pad, where the sand lies a little lower
    g.position.set(-12.4, -0.24, 4.4);
    g.rotation.y = 0.9;
    this.mark(g, 'bin');
    this.group.add(g);
  }

  /** Three arrows chasing each other around a triangle, white on nothing, as a texture. */
  private recycleLogo(): THREE.CanvasTexture {
    const cv = document.createElement('canvas');
    cv.width = 256;
    cv.height = 256;
    const c = cv.getContext('2d')!;
    c.translate(128, 150);
    c.fillStyle = c.strokeStyle = '#ffffff';
    c.lineWidth = 27;
    const R = 112;
    const top = { x: 0, y: -R };
    const left = { x: -R * Math.sin(Math.PI / 3), y: R / 2 };
    const right = { x: -left.x, y: R / 2 };
    const along = (p: { x: number; y: number }, q: { x: number; y: number }, u: number): { x: number; y: number } => ({
      x: p.x + (q.x - p.x) * u,
      y: p.y + (q.y - p.y) * u,
    });
    // each arrow bends around one corner of the triangle and stops short of the tail of the next
    const from = along(left, top, 0.6);
    const shaft = along(top, right, 0.3);
    const tip = along(top, right, 0.58);
    const dx = (right.x - top.x) / (R * Math.sqrt(3));
    const dy = (right.y - top.y) / (R * Math.sqrt(3));
    for (let i = 0; i < 3; i++) {
      c.beginPath();
      c.moveTo(from.x, from.y);
      c.arcTo(top.x, top.y, shaft.x, shaft.y, 12);
      c.lineTo(shaft.x, shaft.y);
      c.stroke();
      c.beginPath();
      c.moveTo(tip.x, tip.y);
      c.lineTo(shaft.x - dy * 34, shaft.y + dx * 34);
      c.lineTo(shaft.x + dy * 34, shaft.y - dx * 34);
      c.closePath();
      c.fill();
      c.rotate((Math.PI * 2) / 3);
    }
    const tex = new THREE.CanvasTexture(cv);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 4;
    return tex;
  }

  private buildBoard(): void {
    const g = new THREE.Group();
    for (const sx of [-0.85, 0.85]) g.add(box(0.12, 2.2, 0.12, wood(0x5e4128), sx, 1.1, 0));
    g.add(box(2.0, 1.3, 0.08, wood(0xb38a5a), 0, 1.5, 0.04));
    g.add(box(2.2, 0.12, 0.2, wood(0x5e4128), 0, 2.25, 0));
    const colors = [0xfff3a8, 0xffc9d6, 0xc9f0ff, 0xd6ffc9];
    colors.forEach((c, i) => {
      const note = new THREE.Mesh(
        new THREE.PlaneGeometry(0.4, 0.4),
        new THREE.MeshStandardMaterial({ color: c, roughness: 0.9, side: THREE.DoubleSide }),
      );
      note.position.set(-0.65 + (i % 2) * 0.55 + (i > 1 ? 0.5 : 0), 1.75 - Math.floor(i / 2) * 0.55, 0.1);
      note.rotation.z = (Math.random() - 0.5) * 0.3;
      g.add(note);
    });
    // the title, painted on a cream plate along the top beam
    const cv = document.createElement('canvas');
    cv.width = 768;
    cv.height = 112;
    const c = cv.getContext('2d')!;
    c.fillStyle = '#f6efe0';
    c.fillRect(0, 0, cv.width, cv.height);
    c.strokeStyle = '#5e4128';
    c.lineWidth = 6;
    c.strokeRect(9, 9, cv.width - 18, cv.height - 18);
    c.fillStyle = '#4a3020';
    c.font = 'bold 66px Georgia, serif';
    c.textAlign = 'center';
    c.textBaseline = 'middle';
    c.fillText('LOST & FOUND', cv.width / 2, cv.height / 2 + 3);
    const tex = new THREE.CanvasTexture(cv);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 4;
    const title = new THREE.Mesh(new THREE.PlaneGeometry(1.8, 0.2625), new THREE.MeshStandardMaterial({ map: tex, roughness: 1 }));
    title.position.set(0, 2.25, 0.105);
    g.add(title);
    g.position.set(-4.7, 0, 1.2);
    g.rotation.y = 0.9;
    this.mark(g, 'board');
    this.group.add(g);
  }

  /** A little house on wheels parked beside the fire. */
  private buildCamper(): void {
    const g = new THREE.Group();
    const body = new THREE.MeshStandardMaterial({ color: 0x9fd3c7, roughness: 0.6, metalness: 0.1 });
    const trim = new THREE.MeshStandardMaterial({ color: 0xd9704a, roughness: 0.6 });
    const cream = new THREE.MeshStandardMaterial({ color: 0xf3ead2, roughness: 0.7 });
    const black = new THREE.MeshStandardMaterial({ color: 0x24262a, roughness: 0.8 });
    g.add(box(4.6, 0.18, 2.0, black, 0, 0.6, 0));
    g.add(box(4.4, 1.55, 2.0, body, 0, 1.45, 0));
    g.add(box(4.42, 0.22, 2.02, trim, 0, 1.2, 0));
    // a gently arched roof: the top half of a flattened barrel lying along the camper, a touch longer than the body so it overhangs
    const roof = new THREE.Mesh(new THREE.CylinderGeometry(1.0, 1.0, 4.5, 24, 1, false, 0, Math.PI), cream);
    roof.rotation.set(0, 0, Math.PI / 2);
    roof.scale.set(0.38, 1, 1.03);
    roof.position.set(0, 2.2, 0);
    roof.castShadow = true;
    g.add(roof);
    // chimney
    g.add(box(0.25, 0.5, 0.25, black, -1.4, 2.7, -0.4));
    // windows on both long sides, glowing warmly
    for (const side of [1, -1]) {
      for (const x of [-1.3, 0.2]) {
        const mat = new THREE.MeshStandardMaterial({ color: 0xffe3a8, emissive: 0xffb84d, emissiveIntensity: 0.35, roughness: 0.3 });
        this.windows.push(mat);
        g.add(box(0.95, 0.65, 0.04, mat, x, 1.7, side * 1.02));
        g.add(box(1.05, 0.75, 0.03, trim, x, 1.7, side * 1.0));
      }
    }
    // door on the fire side
    g.add(box(0.8, 1.35, 0.05, new THREE.MeshStandardMaterial({ color: 0x7a5538, roughness: 0.8 }), 1.65, 1.35, 1.03));
    g.add(box(0.06, 0.06, 0.07, cream, 1.4, 1.3, 1.08));
    // striped awning with poles
    const stripe = (i: number): THREE.Mesh => box(0.3, 0.05, 1.3, i % 2 ? cream : trim, 1.1 + i * 0.3, 2.15 - i * 0.0, 1.65);
    for (let i = 0; i < 6; i++) {
      const s = stripe(i);
      s.rotation.x = 0.22;
      g.add(s);
    }
    for (const x of [1.0, 2.7]) g.add(box(0.06, 1.7, 0.06, wood(0x4a3523), x, 1.1, 2.25));
    // string lights along the awning edge
    for (let i = 0; i < 7; i++) {
      const bulb = new THREE.Mesh(
        new THREE.SphereGeometry(0.06, 8, 6),
        new THREE.MeshStandardMaterial({ color: 0xfff0b0, emissive: 0xffc860, emissiveIntensity: 1.2 }),
      );
      bulb.position.set(1.0 + i * 0.28, 1.9 - Math.sin((i / 6) * Math.PI) * -0.0, 2.3);
      g.add(bulb);
    }
    // wheels (both sides) and a hitch with a jockey wheel
    for (const side of [1, -1]) {
      const tire = cyl(0.48, 0.48, 0.26, black, 20);
      tire.rotation.x = Math.PI / 2;
      tire.position.set(0.4, 0.48, side * 1.12);
      const hub = cyl(0.2, 0.2, 0.28, new THREE.MeshStandardMaterial({ color: 0xc9ced3, metalness: 0.8, roughness: 0.3 }), 12);
      hub.rotation.x = Math.PI / 2;
      hub.position.copy(tire.position);
      g.add(tire, hub);
      g.add(box(1.0, 0.05, 0.3, trim, 0.4, 0.98, side * 1.12));
    }
    g.add(box(1.8, 0.12, 0.14, black, -3.2, 0.55, 0));
    const jockey = cyl(0.12, 0.12, 0.1, black, 10);
    jockey.rotation.x = Math.PI / 2;
    jockey.position.set(-3.7, 0.2, 0);
    g.add(jockey, box(0.05, 0.4, 0.05, black, -3.7, 0.4, 0));
    this.camperLight.position.set(0.5, 1.7, 2.2);
    g.add(this.camperLight);

    g.position.set(-9.5, -0.04, 1.5);
    g.rotation.y = 0.5;
    this.mark(g, 'camper');
    this.group.add(g);
    for (const s of [-1.4, 0, 1.4]) {
      this.obstacles.push({
        x: g.position.x + s * Math.cos(0.5),
        z: CAMP_Z + g.position.z - s * Math.sin(0.5),
        r: 1.35,
      });
    }
  }

  private buildLantern(): void {
    const g = new THREE.Group();
    g.add(box(0.07, 2.0, 0.07, wood(0x4a3523), 0, 1.0, 0));
    g.add(box(0.5, 0.06, 0.06, wood(0x4a3523), 0.22, 1.95, 0));
    const metal = new THREE.MeshStandardMaterial({ color: 0x3b3f44, metalness: 0.8, roughness: 0.4 });
    const cap = cyl(0.04, 0.16, 0.1, metal, 8);
    cap.position.set(0.42, 1.78, 0);
    const base = cyl(0.14, 0.14, 0.05, metal, 8);
    base.position.set(0.42, 1.38, 0);
    this.lanternGlass = new THREE.Mesh(
      new THREE.CylinderGeometry(0.13, 0.13, 0.35, 10),
      new THREE.MeshStandardMaterial({ color: 0xffe3a8, emissive: 0xffb84d, emissiveIntensity: 0.4, transparent: true, opacity: 0.85 }),
    );
    this.lanternGlass.position.set(0.42, 1.58, 0);
    this.lanternLight.position.set(0.42, 1.58, 0);
    g.add(cap, base, this.lanternGlass, this.lanternLight);
    g.position.set(2.2, 0, -1.6);
    this.mark(g, 'lantern');
    this.group.add(g);
  }

  /** Brings the lantern to life for the evening modes. */
  setLantern(on: boolean): void {
    this.lanternLight.intensity = on ? 9 : 0;
    (this.lanternGlass.material as THREE.MeshStandardMaterial).emissiveIntensity = on ? 2.2 : 0.4;
  }

  /** Puts an assembled antique on the shelf above the workbench. */
  showTrophy(setId: string): void {
    if (this.trophies.has(setId)) return;
    const gold = new THREE.MeshStandardMaterial({ color: 0xd4a843, metalness: 0.9, roughness: 0.28 });
    const t = new THREE.Group();
    if (setId === 'astrolabe') {
      for (let i = 0; i < 3; i++) {
        const ring = new THREE.Mesh(new THREE.TorusGeometry(0.28, 0.018, 8, 40), gold);
        ring.rotation.set((i * Math.PI) / 3, (i * Math.PI) / 5, 0);
        ring.castShadow = true;
        t.add(ring);
      }
      t.add(new THREE.Mesh(new THREE.SphereGeometry(0.07, 12, 10), new THREE.MeshStandardMaterial({ color: 0x4aa3d6, metalness: 0.3, roughness: 0.2 })));
      t.position.set(-0.55, 0.35, 0);
    } else {
      const disc = new THREE.Mesh(new THREE.CylinderGeometry(0.3, 0.3, 0.05, 28), gold);
      disc.rotation.x = Math.PI / 2;
      const skull = new THREE.Mesh(new THREE.SphereGeometry(0.1, 12, 10), new THREE.MeshStandardMaterial({ color: 0xf0e8d0, roughness: 0.7 }));
      skull.position.z = 0.04;
      t.add(disc, skull);
      t.position.set(0.55, 0.3, 0);
    }
    this.trophies.set(setId, t);
    this.shelf.add(t);
  }

  update(dt: number, boost: number, lanternOn: boolean, night = 0): void {
    this.time += dt;
    const t = this.time;
    this.flames.forEach((f, i) => {
      const s = 1 + Math.sin(t * (9 + i * 3) + i) * 0.14 + Math.sin(t * 17 + i * 2) * 0.06;
      f.scale.set(1 + Math.sin(t * 11 + i) * 0.08, s, 1 + Math.cos(t * 13 + i) * 0.08);
      f.rotation.y = t * (1.5 + i);
    });
    this.fireLight.intensity = (14 + Math.sin(t * 13) * 3 + Math.sin(t * 7.3) * 2 + Math.random() * 2) * (0.5 + boost);
    this.fireLight.position.x = Math.sin(t * 9) * 0.06;
    this.setLantern(lanternOn);
    this.tikiFlames.forEach((f, i) => {
      const k = 1 + Math.sin(t * 13 + i * 2.1) * 0.14 + Math.sin(t * 7.7 + i) * 0.08;
      f.scale.set(1 / k * 0.95, k, 1 / k * 0.95);
      f.rotation.y = t * 2 + i;
    });
    if (this.lightsGroup.visible) this.bulbs.forEach((m, i) => (m.emissiveIntensity = 0.15 + night * (1.5 + Math.sin(t * 2.2 + i * 1.7) * 0.35)));
    this.radioLed.emissiveIntensity = this.radioOn ? 1.2 + Math.sin(t * 6) * 0.5 : 0;
    this.bookCover.emissiveIntensity = this.bookNew ? 0.4 + Math.sin(t * 3) * 0.25 : 0;
    const flagTarget = this.mailNew ? 0 : -Math.PI / 2;
    this.mailFlag.rotation.z += (flagTarget - this.mailFlag.rotation.z) * Math.min(1, dt * 5);
    // the hammock rocks gently, a little more when someone is lying in it
    this.hammockCloth.rotation.x = Math.sin(t * 0.9) * (this.hammockOccupied ? 0.05 : 0.018);
    this.radioAntenna.rotation.z = -0.35 + (this.radioOn ? Math.sin(t * 5) * 0.03 : 0);
    this.camperLight.intensity = lanternOn ? 6 : 0.8;
    for (const w of this.windows) w.emissiveIntensity = lanternOn ? 1.6 : 0.35;

    this.emberTimer -= dt;
    if (this.emberTimer <= 0) {
      this.emberTimer = 0.06;
      const p = new THREE.Vector3((Math.random() - 0.5) * 0.4, 0.8, (Math.random() - 0.5) * 0.4);
      const v = new THREE.Vector3((Math.random() - 0.5) * 0.4, 0.8 + Math.random() * 0.8, (Math.random() - 0.5) * 0.4);
      this.embers.emit(p, v, 1.6 + Math.random(), this.ember);
    }
    this.embers.update(dt);
  }
}
