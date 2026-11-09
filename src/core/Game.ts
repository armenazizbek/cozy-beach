import * as THREE from 'three';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { CleaningStation } from '../mechanics/CleaningStation';
import { DetectorTool } from '../mechanics/DetectorTool';
import { Arrows } from '../mechanics/Arrows';
import { Bullets } from '../mechanics/Bullets';
import { DigTool } from '../mechanics/DigTool';
import { CampModal } from '../ui/CampModal';
import type { CampTab } from '../ui/CampModal';
import { UIManager } from '../ui/UIManager';
import { HOARD_VALUE, buildGrate } from '../world/SeaCave';
import { takeItemPhotos } from '../ui/ItemPhotos';
import { BEACH_SEGMENTS, Beach, CAMP_Z, HALF, SHORE_Z, ISLAND, SEA, WATER_LEVEL, createSkirt, createSurround } from '../world/Beach';
import { Birds } from '../world/Birds';
import { Boat } from '../world/Boat';
import { Camp } from '../world/Camp';
import { GolfCart } from '../world/GolfCart';
import type { CampKind } from '../world/Camp';
import { Ecology } from '../world/Ecology';
import { LootSpawner } from '../world/LootSpawner';
import type { BuriedItem } from '../world/LootSpawner';
import { ViewModel } from '../mechanics/ViewModel';
import { MapView } from '../ui/MapView';
import { Flags } from '../world/Flags';
import { Dog } from '../world/Dog';
import { Stones } from '../world/Stones';
import { BOOK_SHOW_ALL, JOURNAL_PAGES, PAGE_BY_ID } from './Journal';
import { LETTERS, LETTER_BY_ID } from './Mail';
import { Grass } from '../world/Grass';
import { MAX_CASTLES, Sandcastles } from '../world/Sandcastles';
import { Island } from '../world/Island';
import { ParticleSystem } from './Particles';
import { Underwater, UW, UW_ENTRY, UW_SURFACE } from '../world/Underwater';
import { UG, Underground } from '../world/Underground';
import { CAPTAIN_PAGES } from './Story';
import { ACHIEVEMENTS, Achievements } from './Achievements';
import type { DogEnv } from '../world/Dog';
import { Rain, rainWindow } from '../world/Rain';
import { GamepadInput } from './GamepadInput';
import type { PadButton, PadDir } from './GamepadInput';
import { MAP_VIEW } from '../ui/MapView';
import type { Waypoint } from '../ui/MapView';
import { Ocean } from '../world/Ocean';
import { Scenery } from '../world/Scenery';
import { SeaLife } from '../world/SeaLife';
import { CHEST_REWARD, SeaTreasure } from '../world/SeaTreasure';
import { Sky } from '../world/Sky';
import { Tracks } from '../world/Tracks';
import { Whitecaps } from '../world/Whitecaps';
import { AudioManager } from './AudioManager';
import { InputManager } from './InputManager';
import { AUTOSAVE_ID, deleteSave, listSaves, requestLoad, takePendingLoad, writeSave } from './SaveGame';
import { loadSettings, requestAutostart, saveSettings, takeAutostart } from './Settings';
import type { GameSettings } from './Settings';
import type { SaveMeta, Snapshot } from './SaveGame';
import { DEF_BY_ID, DIVE_ITEM_IDS, ISLAND_ITEM_IDS, QUEST_DEFS, SHARD_SETS } from './items';
import { StateManager } from './StateManager';
import type { LightingMode } from './StateManager';
import { DECOR, SIGN_TEXTS } from './StateManager';

/** set when the page was reloaded to load a saved game */
const PENDING: Snapshot | null = takePendingLoad();
/** set when "New game" was chosen from the pause menu: skip the main menu and start straight away */
const AUTOSTART = takeAutostart();

export type GameState = 'MENU' | 'EXPLORE' | 'INSPECT' | 'CAMP';

const DETECT_REACH = 4;
/** coins for hitting a crab or a seagull with the gun or the bow */
const CRAB_REWARD = 1;
/** a gull on the sand pays this; one shot in the air pays more */
const BIRD_REWARD = 2;
const BIRD_AIR_REWARD = 5;
const COCONUT_REWARD = 0.5;
/** Above this the extra sharpness is barely visible, and the cost keeps climbing. */
const MAX_PIXEL_RATIO = 1.5;
const NO_TARGETS: THREE.Object3D[] = [];

/** the chance that a bone sends the dog after a Lost & Found item instead of a coin or trash */
const DOG_LOST_CHANCE = 0.06;
const SHARK_REWARD = 5;
const FISH_REWARD = 1;
const LIGHT_ORDER: LightingMode[] = ['cycle', 'golden', 'day', 'rain', 'sunset', 'night'];
/** how long a full day lasts, in seconds (5 minutes) */
const DAY_SECONDS = 5 * 60;
/** how often the game saves itself, in seconds of play */
const AUTOSAVE_SECONDS = 90;
/** the shortest gap between two autosaves, whatever triggers them */
const AUTOSAVE_MIN_GAP = 15000;
/** how far the grate's stone frame reaches from its middle (it is 2.34 by 1.94): no digging inside this */
const GRATE_R = 1.6;
/** no find is buried closer than this to the grate, so none can be dug out from under the stone */
const GRATE_CLEAR = 2.6;
/** the time of day each fixed mood is frozen at */
const MOOD_TIME: Record<Exclude<LightingMode, 'cycle'>, number> = { golden: 0.02, day: 0.28, rain: 0.4, sunset: 0.56, night: 0.78 };
const CATEGORY_LABEL: Record<string, string> = {
  trash: 'Recyclable', coin: 'Coin', jewelry: 'Jewelry', quest: 'Lost & Found', shard: 'Antique shard',
};

export class Game {
  readonly renderer: THREE.WebGLRenderer;
  readonly scene = new THREE.Scene();
  readonly camera = new THREE.PerspectiveCamera(50, 1, 0.1, 1000);
  readonly state = new StateManager(PENDING?.data);
  readonly audio = new AudioManager();
  readonly input: InputManager;
  readonly ui: UIManager;
  readonly campModal: CampModal;
  readonly beach = new Beach();
  readonly ocean: Ocean;
  readonly sky: Sky;
  readonly camp = new Camp();
  readonly scenery = new Scenery();
  readonly birds: Birds;
  readonly sea = new SeaLife();
  readonly whitecaps = new Whitecaps();
  readonly treasure = new SeaTreasure();
  readonly mapView = new MapView(this.scenery.trunks);
  private holes: [number, number][] = [];
  private mapOpen = false;
  readonly cart: GolfCart;
  readonly boat: Boat;
  private vehicle: 'cart' | 'boat' | null = null;
  private tracks: Tracks;
  private stepDist = 0;
  private lastFoot = { x: 0, z: 0, set: false };
  private tireDist = 0;
  private lastCart = { x: 0, z: 0, set: false };
  private driving = false;
  /** where the player stands on the boat's deck (boat-local x, z) */
  private deck = { x: 0, z: 0.5 };
  private interaction2: { label: string; run: () => void } | null = null;
  private lookOffset = 0;
  private dustTimer = 0;
  private baseFov = 50;
  private waypoint: Waypoint | null = null;
  private beam: THREE.Mesh;
  readonly ecology: Ecology;
  readonly spawner: LootSpawner;
  readonly detector = new DetectorTool();
  readonly dig = new DigTool();
  readonly bullets = new Bullets();
  readonly arrows = new Arrows();
  private drawing = false;
  private charge = 0;
  readonly station: CleaningStation;

  gameState: GameState = 'EXPLORE';
  settings: GameSettings = loadSettings();
  private menuT = 0;
  private menuShot = 0;
  private menuSwitching = false;
  private menuTarget: THREE.Object3D | null = null;
  private menuFocus = new THREE.Vector3();
  private menuCamReady = false;
  tool: 1 | 2 | 4 | 5 | 6 | 7 | 8 = 1;
  readonly flags = new Flags();
  readonly dog: Dog;
  readonly stones = new Stones(this.beach);
  readonly grass = new Grass(this.beach);
  readonly castles = new Sandcastles(this.beach);
  readonly island = new Island();
  readonly under = new Underwater();
  readonly achievements = new Achievements();
  private achT = 0;
  readonly cellar = new Underground();
  /** in the room under the hatch: the land is hidden and not updated, as when diving */
  private underground = false;
  /** how far up the ladder the player has climbed */
  private ladderY = 0;
  /** little hearts that float up when the dog is petted */
  private hearts = new ParticleSystem(60, 0.17, -0.5, false, true);
  /** swimming on the sea floor: the land is hidden and is not updated while this is true */
  private diving = false;
  private diveReturn: { x: number; z: number; yaw: number; pitch: number } | null = null;
  private diveVel = new THREE.Vector3();
  private diveY = -1.4;
  private diveBg: THREE.Color | THREE.Texture | null = null;
  private hiddenByDive: THREE.Object3D[] = [];
  private lift = { up: false, down: false };
  private dogWas = 'rest';
  readonly rain = new Rain();
  readonly pad = new GamepadInput();
  /** the last thing the player touched was the controller */
  usingPad = false;
  /** the controller is holding the cleaning brush */
  private padBrush = false;
  /** where the grate over the sun shaft lies on the beach */
  private grate: { x: number; z: number } | null = null;
  /** the dropdown whose choices are open for the controller */
  private padSelect: HTMLSelectElement | null = null;
  private padKeys = new Set<string>();
  private padRun = false;
  private padL3Was = false;
  private padCalling = false;
  private mapCursor = { u: 0.5, v: 0.5 };
  private mapCursorEl: HTMLElement | null = null;
  private wasRaining = false;
  private zoomIdx = 0;
  private binocView = false;
  private rangeText = '—';
  private rangeTick = 0;
  private rayCaster = new THREE.Raycaster();
  private shotCooldown = 0;
  private crabsShot = 0;

  private started = false;
  private paused = false;
  private clock = new THREE.Clock();
  private time = 0;
  readonly view = new ViewModel();
  /** yaw uses three's rotation.y: 0 looks toward -z (the dunes and the camp), PI toward the sea */
  private player = { x: 0, z: CAMP_Z + 9, yaw: 0, pitch: -0.12 };
  private walkTo: { x: number; z: number; yaw: number; pitch: number } | null = null;
  private moving = false;
  private dayTime = 0;
  private eyeY = 0;
  private feet = new THREE.Vector3();
  private hoveredItem: BuriedItem | null = null;
  private hoveredCamp: CampKind | null = null;
  private inspectItem: BuriedItem | null = null;
  private transitioning = false;
  private chillWarned = false;
  private canvas: HTMLCanvasElement;

  constructor(canvas: HTMLCanvasElement, uiRoot: HTMLElement) {
    this.canvas = canvas;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, MAX_PIXEL_RATIO));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;

    const pmrem = new THREE.PMREMGenerator(this.renderer);
    this.scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
    this.scene.environmentIntensity = 0.3;

    this.sky = new Sky(this.renderer, this.scene);
    this.ocean = new Ocean(this.beach);
    this.ecology = new Ecology(this.beach);
    this.birds = new Birds(this.beach);
    this.spawner = new LootSpawner(this.beach, this.state);
    // finds on the sea floor rest on it, not on the beach far away
    this.spawner.ground = (x, z) => (this.under.contains(x, z) ? this.under.floorAt(x, z) : this.beach.heightAt(x, z));
    this.under.onCatch = (_at, reward, label) => this.onFishSpeared(reward, label);
    this.station = new CleaningStation(this.renderer);

    this.scene.add(
      this.sky.group,
      this.beach.mesh,
      createSurround(),
      createSkirt(),
      this.scenery.group,
      this.birds.group,
      this.sea.group,
      this.whitecaps.mesh,
      this.treasure.group,
      this.ocean.group,
      this.camp.group,
      this.ecology.group,
      this.spawner.group,
      ...this.detector.overlays,
      this.dig.ring,
      this.dig.ringDark,
      this.dig.particles.points,
      this.bullets.group,
      this.arrows.group,
      this.flags.group,
      this.rain.group,
      this.stones.group,
      this.grass.group,
      this.castles.group,
      this.island.group,
      this.under.group,
      this.cellar.group,
      this.hearts.points,
      this.view.proxy,
      this.camera,
    );
    this.tracks = new Tracks(this.beach);
    this.scene.add(this.tracks.group);
    this.cart = new GolfCart(9, CAMP_Z + 9, Math.PI, this.beach);
    this.scene.add(this.cart.group);
    this.dog = new Dog(this.beach);
    this.scene.add(this.dog.group);
    this.placeDogHome();
    this.boat = new Boat(-16, SHORE_Z + 4.5, Math.PI);
    this.scene.add(this.boat.group, this.boat.wake.points);
    this.beam = new THREE.Mesh(
      new THREE.CylinderGeometry(0.18, 0.18, 60, 10, 1, true),
      new THREE.MeshBasicMaterial({ color: 0xffd27a, transparent: true, opacity: 0.4, blending: THREE.AdditiveBlending, depthWrite: false, fog: false, side: THREE.DoubleSide }),
    );
    this.beam.visible = false;
    this.scene.add(this.beam);
    this.camera.add(this.view.group);
    this.camera.rotation.order = 'YXZ';
    this.camera.near = 0.05;
    this.camera.updateProjectionMatrix();

    this.input = new InputManager(canvas);
    this.input.handlers = {
      onClick: (b) => this.onClick(b),
      onPointerDown: (b) => this.onPointerDown(b),
      onPointerUp: (b) => this.onPointerUp(b),
      onPointerMove: (dx, dy, buttons) => this.onPointerMove(dx, dy, buttons),
      onWheel: (d) => this.onWheel(d),
      onKey: (k) => {
        if (!this.padCalling) this.setUsingPad(false);
        this.onKey(k);
      },
    };
    this.pad.onButton = (b, down) => this.padButton(b, down);
    this.pad.onNav = (d) => this.padNav(d);
    this.pad.onActivity = () => this.setUsingPad(true);

    this.ui = new UIManager(uiRoot, this.state, {
      onTool: (n) => this.selectSlot(n),
      onInstantClean: () => this.station.instantClean(),
      onInspectBack: () => this.exitInspect(),
      onContinue: () => this.exitInspect(),
      onMute: () => this.audio.toggleMute(),
    });
    this.campModal = new CampModal(uiRoot, this.state, this.audio, {
      onSip: () => this.drinkCoffee(),
      onClose: () => this.closeCamp(),
      onLighting: () => this.cycleLighting(),
      onRecycled: () => undefined,
      onAssembled: (id) => this.camp.showTrophy(id),
      onLuxury: () => this.applyLuxury(),
      toast: (m) => this.ui.toast(m),
    });

    this.station.onProgress = (p) => this.ui.setProgress(p);
    this.station.onComplete = () => this.onCleaned();
    this.state.subscribe(() => this.ecology.setScore(this.state.data.ecology));
    // the flag count shows next to the tool name
    this.state.subscribe(() => {
      if (this.tool === 7 && this.started && !this.driving && !this.alt && this.gameState === 'EXPLORE') this.ui.setTool(7);
    });

    this.ecology.setScore(this.state.data.ecology);
    this.state.data.assembled.forEach((id) => this.camp.showTrophy(id));
    this.cellar.cave.addSea(this.ocean.seaSheet(900, 700));
    this.cellar.cave.addClouds(this.sky.cloudMaterials);
    this.cellar.cave.addShark(this.sea.sharks[0].root);
    if (this.state.data.doorOpen) this.cellar.openDoor(true);
    this.cellar.cave.setTaken(this.state.data.hoardTaken);
    this.applyLuxury();
    const showDecor = (): void => {
      const d = this.state.data;
      this.camp.setDecor(d.decor ?? [], SIGN_TEXTS[d.signText ?? 0] ?? SIGN_TEXTS[0]);
      if ((d.decor ?? []).length) this.discover('camp_decor');
    };
    this.state.subscribe(showDecor);
    showDecor();
    this.achievements.onUnlock = (a) => {
      this.audio.discover();
      this.ui.toast(`🏆 Achievement: ${a.name}`);
    };
    this.spawner.populate();
    this.dayTime = PENDING?.dayTime ?? 0;
    this.sky.setTime(this.dayTime);
    this.selectSlot(1);
    this.ui.setMode('EXPLORE');
    this.applySettings();
    this.applyDisplay(true);
    if (PENDING) {
      this.ui.skipIntro();
      this.restoreWorld(PENDING);
    } else if (AUTOSTART) {
      this.ui.skipIntro();
      this.started = true;
      this.ui.toast('A fresh start. Sweep the sand with your detector.');
    } else {
      this.enterMenu();
    }

    this.scatterStones();
    this.ensureIslandFinds();
    this.ensureTreasureCaches();
    this.ensureCaptainChest();
    this.placeGrate();
    this.ensureDiveFinds();

    // Esc while the mouse is captured is swallowed by the browser: treat losing the lock as pausing
    document.addEventListener('pointerlockchange', () => {
      if (this.locked) this.setLockFailed(false);
      if (!this.locked && this.started && this.gameState === 'EXPLORE' && !this.transitioning && !this.mapOpen && !this.bookOpen && !this.hammock) this.setPaused(true);
    });
    window.addEventListener('resize', () => this.resize());
    this.resize();
    this.mapView.flags = this.flags.positions;
    if (import.meta.env.DEV) (window as unknown as { game: Game }).game = this;
  }

  /** Gets everything ready behind the loading screen, then starts the game. Nothing runs yet, so nothing has to compete for the machine. */
  async start(): Promise<void> {
    try {
      await this.prepareEverything();
    } catch {
      /* the game can still run: whatever was not prepared is simply built the first time it is seen */
    }
    this.renderer.setAnimationLoop(() => this.frame());
    const boot = document.getElementById('boot');
    boot?.classList.add('gone');
    window.setTimeout(() => boot?.remove(), 700);
  }

  /**
   * Lights are built into shaders, so a place with a different set of lights needs every material rebuilt, and things are only sent to the
   * graphics chip the first time they are drawn. Both are done here once, so the game never has to stop for them while playing.
   */
  private async prepareEverything(): Promise<void> {
    const bar = document.getElementById('boot-bar');
    const places: { camp: boolean; near: boolean; under: boolean; cellar: boolean; cave: boolean }[] = [
      { camp: true, near: false, under: false, cellar: false, cave: false },
      { camp: false, near: false, under: true, cellar: false, cave: false },
      { camp: false, near: false, under: false, cellar: true, cave: true },
      { camp: false, near: false, under: false, cellar: true, cave: false },
      { camp: true, near: true, under: false, cellar: false, cave: false },
    ];
    const steps = places.length + 2;
    const done = (n: number): void => {
      if (bar) bar.style.width = `${(n / steps) * 100}%`;
    };
    for (let i = 0; i < places.length; i++) {
      const p = places[i];
      this.camp.group.visible = p.camp;
      this.cart.group.visible = p.camp;
      this.under.group.visible = p.under;
      this.cellar.group.visible = p.cellar;
      this.cellar.setSide(p.cave);
      this.camp.setLightsNear(p.near);
      await this.renderer.compileAsync(this.scene, this.camera);
      done(i + 1);
    }
    // the last place was the camp as it stands at the start
    this.under.group.visible = false;
    this.cellar.group.visible = false;
    await this.station.prepare(this.renderer, this.spawner.group);
    this.campModal.photos = takeItemPhotos(QUEST_DEFS.flatMap((q) => q.need.defId ?? []));
    done(places.length + 1);
    await new Promise((resolve) => setTimeout(resolve, 30));
    this.sendEverythingToGpu();
    done(steps);
  }

  /** Draws the whole world once into a one-pixel image, with everything shown: all that is uploaded, and it costs almost nothing to fill. */
  private sendEverythingToGpu(): void {
    const shown: THREE.Object3D[] = [];
    const unculled: THREE.Object3D[] = [];
    this.scene.traverse((o) => {
      if (!o.visible) {
        o.visible = true;
        shown.push(o);
      }
      if (o.frustumCulled) {
        o.frustumCulled = false;
        unculled.push(o);
      }
    });
    // their torches stay out, so this uses the shaders made for the camp
    const dark: THREE.Object3D[] = [];
    for (const world of [this.under.group, this.cellar.group]) {
      world.traverse((o) => {
        if (o instanceof THREE.Light && o.visible) {
          o.visible = false;
          dark.push(o);
        }
      });
    }
    const target = new THREE.WebGLRenderTarget(1, 1);
    try {
      this.renderer.setRenderTarget(target);
      for (let lod = 2; lod >= 0; lod--) {
        this.beach.setAllDetail(lod);
        this.renderer.render(this.scene, this.camera);
      }
    } finally {
      this.renderer.setRenderTarget(null);
      target.dispose();
      for (const o of shown) o.visible = false;
      for (const o of unculled) o.frustumCulled = true;
      for (const o of dark) o.visible = true;
    }
  }

  private resize(): void {
    const w = window.innerWidth;
    const h = window.innerHeight;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.station.resize(w, h);
  }

  // ---------------------------------------------------------------- input

  private onClick(button: number): void {
    if (!this.started || this.transitioning || this.paused || this.driving) return;
    if (this.hammock) {
      this.leaveHammock(false);
      return;
    }
    if (this.underground) {
      if (button === 0 && this.gameState === 'EXPLORE' && !this.locked && !this.usingPad) this.requestLock(true);
      return;
    }
    if (this.diving) {
      // underwater a click either picks up what you are looking at or looses the harpoon
      if (button === 0 && this.gameState === 'EXPLORE') {
        if (!this.locked && !this.usingPad) this.requestLock(true);
        if (this.hoveredItem) this.useFind(this.hoveredItem);
        else this.fireHarpoon();
      }
      return;
    }
    if (button === 2) {
      if (this.gameState === 'EXPLORE' && this.tool === 8) this.knockCastle();
      else if (this.binocView) this.setBinocView(false);
      return;
    }
    if (button !== 0) return;
    if (this.gameState === 'INSPECT') return;
    // try to capture the mouse for smooth look; if the browser refuses, the free cursor still works
    if (this.gameState === 'EXPLORE' && !this.locked && !this.usingPad) this.requestLock(true);
    if (this.gameState === 'EXPLORE' && this.tool === 5) {
      if (!this.binocView) this.setBinocView(true);
      else this.zoomIdx = 1 - this.zoomIdx;
      this.audio.ui();
      return;
    }
    if (this.gameState === 'EXPLORE' && this.tool === 6) return; // the bow works on press and release
    if (this.hoveredItem && this.gameState === 'EXPLORE') {
      this.useFind(this.hoveredItem);
      return;
    }
    if (this.hoveredCamp) {
      this.useCampObject(this.hoveredCamp);
      return;
    }
    if (this.gameState === 'EXPLORE' && this.tool === 2 && this.toolHit) this.tryDig(this.toolHit);
    if (this.gameState === 'EXPLORE' && this.tool === 4) this.shoot();
    if (this.gameState === 'EXPLORE' && this.tool === 7) this.plantFlag();
    if (this.gameState === 'EXPLORE' && this.tool === 8) this.buildCastle();
  }

  private plantFlag(): void {
    if ((this.state.data.flags ?? 0) <= 0) {
      this.audio.error();
      this.ui.toast('Out of flags. Pick one up with F, or buy more at the camper shop.');
      return;
    }
    const at = this.toolHit;
    if (!at) return;
    if (this.beach.heightAt(at.x, at.z) < this.ocean.surfaceY + 0.1) {
      this.audio.error();
      this.ui.toast('Flags need dry sand.');
      return;
    }
    this.state.useFlag();
    if (this.state.data.flags <= 0) {
      // that was the last one: the flag tool leaves the list and the next tool takes its place
      const order = this.toolOrder();
      this.selectSlot(order[order.indexOf(5) + 1] ?? order[0]);
    }
    this.flags.add(at.x, at.z, this.beach.heightAt(at.x, at.z) - 0.05);
    this.discover('flag_first');
    this.audio.thud();
    this.dig.particles.burst(at.clone().setY(at.y + 0.05), 8, 1, 1.2, 0.5, new THREE.Color(0xe6ccb2));
    this.ui.toast(`Flag planted (${this.state.data.flags} left). It is on your maps.`);
  }

  private canUseBow(): boolean {
    return this.started && !this.paused && !this.driving && !this.alt && !this.transitioning && this.gameState === 'EXPLORE' && this.tool === 6;
  }

  private onPointerDown(button: number): void {
    if (!this.canUseBow()) return;
    if (button === 2 && this.drawing) {
      this.cancelDraw(); // right-click lets the string down again
    } else if (button === 0 && !this.drawing) {
      this.drawing = true;
      this.charge = 0;
      this.audio.bowDraw();
    }
  }

  private onPointerUp(button: number): void {
    if (button !== 0 || !this.drawing) return;
    const power = this.charge;
    this.cancelDraw();
    if (power >= 0.18) this.shootArrow(power);
  }

  private cancelDraw(): void {
    this.drawing = false;
    this.charge = 0;
    this.view.setDraw(0);
    this.ui.setDraw(0);
  }

  private shootArrow(power: number): void {
    const ray = this.input.ray(this.camera, true);
    const beachHit = this.beach.raycast(ray.ray.origin, ray.ray.direction);
    const aimDist = Math.min(beachHit ? beachHit.distanceTo(ray.ray.origin) : 70, 70);
    const aim = ray.ray.origin.clone().addScaledVector(ray.ray.direction, aimDist);
    this.camera.updateMatrixWorld(true);
    const origin = this.camera.localToWorld(new THREE.Vector3(0.05, -0.1, -0.7));
    const dir = aim.sub(origin).normalize();
    // a lob that climbs a little at long range, since arrows drop
    dir.y += 0.012 * (1 - power);
    dir.normalize();
    this.arrows.spawn(origin, dir, 14 + power * 38);
    this.view.releaseBow();
    this.audio.bowRelease(power);
    this.discover('bow_first');
  }

  private onPointerMove(dx: number, dy: number, buttons: number): void {
    if (!this.started || this.paused) return;
    if (this.gameState === 'INSPECT') {
      if (buttons & 2) this.station.rotate(dx * 0.0045, dy * 0.0045);
      // a real push of the mouse takes the brush back from the controller; a nudge of the desk does not
      if (this.padBrush && Math.abs(dx) + Math.abs(dy) > 6) this.setUsingPad(false);
      return;
    }
    // mouse look: with the pointer locked, or right-drag while the cursor is free
    if (Math.abs(dx) + Math.abs(dy) > 1) this.setUsingPad(false);
    if (this.locked || (buttons & 2) !== 0) this.applyLook(dx, dy);
  }

  /** Turns the view by a mouse-sized amount of movement (pixels); the controller's right stick feeds it too. */
  private applyLook(dx: number, dy: number): void {
    if (this.driving) {
      this.lookOffset = Math.min(1.5, Math.max(-1.5, this.lookOffset - dx * 0.0022));
      this.player.pitch = Math.min(1.2, Math.max(-1.0, this.player.pitch - dy * 0.0022));
      return;
    }
    this.walkTo = null;
    const zk = this.camera.fov / this.baseFov;
    const sens = this.settings.sensitivity;
    this.player.yaw -= dx * 0.0022 * zk * sens;
    this.player.pitch = Math.min(1.45, Math.max(-1.45, this.player.pitch - dy * 0.0022 * zk * sens));
  }

  /** Captures the mouse for looking around, so no pointer shows while playing. Quietly does nothing if the browser says no. */
  private grabMouse(): void {
    if (this.usingPad || this.locked) return;
    this.requestLock(false);
  }

  private lockErrors = 0;

  /**
   * Asks the browser to capture the mouse. A refusal without a click is normal (browsers want a click first), so only repeated
   * refusals of a real click, or a browser with no such feature, switch on the steer-with-the-cursor fallback.
   */
  private requestLock(fromClick: boolean): void {
    if (!this.canvas.requestPointerLock) {
      this.setLockFailed(true);
      return;
    }
    try {
      const p = this.canvas.requestPointerLock() as Promise<void> | undefined;
      p?.then?.(() => (this.lockErrors = 0)).catch?.(() => {
        if (fromClick && ++this.lockErrors >= 2) this.setLockFailed(true);
      });
    } catch {
      if (fromClick && ++this.lockErrors >= 2) this.setLockFailed(true);
    }
  }

  private setLockFailed(on: boolean): void {
    this.lockFailed = on;
    if (!on) this.lockErrors = 0;
    // with no mouse capture the cursor has to stay visible, since it is what steers
    document.body.classList.toggle('nolock', on);
  }

  /** the browser refused to capture the mouse, so the cursor itself has to do the steering */
  private lockFailed = false;

  /** True only when the mouse could not be captured (an old browser) and the cursor has to steer by itself. */
  private get cursorSteer(): boolean {
    return !this.locked && !this.usingPad && this.lockFailed;
  }

  private lastWheel = 0;

  /** The d-pad's left slot: flags, bow and gun share it, and each press moves to the next one the player has. */
  private padOtherTool(): void {
    const others = this.ui.padOtherTools();
    if (!others.length) {
      this.ui.toast('No other tools yet. The camper shop sells flags, a bow and a toy pistol.');
      return;
    }
    const i = others.indexOf(this.tool as 8 | 7 | 6 | 4);
    this.selectSlot(others[(i + 1) % others.length]);
  }

  /** The tools the player can scroll through, in order. Flags only while there are some left. */
  private toolOrder(): (1 | 2 | 4 | 5 | 6 | 7 | 8)[] {
    const order: (1 | 2 | 4 | 5 | 6 | 7 | 8)[] = [1, 2, 5];
    if ((this.state.data.flags ?? 0) > 0) order.push(7);
    if (this.state.data.ownsBucket) order.push(8);
    if (this.state.data.ownsBow) order.push(6);
    if (this.state.data.ownsGun) order.push(4);
    if (this.vehicle === 'boat') order.splice(0, 2);
    return order;
  }

  /** scroll swaps between the detector and the trowel */
  private onWheel(d: number): void {
    const now = performance.now();
    if (!this.started || this.paused || this.driving || this.alt || this.gameState !== 'EXPLORE' || now - this.lastWheel < 180) return;
    this.lastWheel = now;
    const order = this.toolOrder();
    const i = order.indexOf(this.tool);
    this.selectSlot(order[(i + (d > 0 ? 1 : order.length - 1)) % order.length]);
  }

  /** away from the beach altogether: diving, or in the room under the hatch */
  private get alt(): boolean {
    return this.diving || this.underground;
  }

  private get locked(): boolean {
    return document.pointerLockElement === this.canvas;
  }

  /** The island map pauses the world like the pause menu does. */
  private setMap(on: boolean): void {
    if (on === this.mapOpen) return;
    this.mapOpen = on;
    this.paused = on;
    if (on) this.discover('map_first');
    this.input.keys.clear();
    if (on) {
      document.exitPointerLock?.();
      this.audio.setOceanLevel(0.02);
    } else {
      this.audio.setOceanLevel(0.06);
      this.grabMouse();
    }
    this.ui.showMap(
      on,
      (ctx, size) => this.mapView.drawBig(ctx, size, { x: this.player.x, z: this.player.z, yaw: this.player.yaw }, this.holes, this.waypoint, { x: this.cart.x, z: this.cart.z }, { x: this.boat.x, z: this.boat.z }),
      (u, v) => this.pickDestination(u, v),
      [
        ...(this.state.data.hasTreasureMap
          ? [{ name: 'Treasure map', draw: (ctx: CanvasRenderingContext2D, size: number) => this.mapView.drawTreasure(ctx, size, { x: this.player.x, z: this.player.z, yaw: this.player.yaw }, this.time, this.waypoint, { x: this.cart.x, z: this.cart.z }, { x: this.boat.x, z: this.boat.z }, this.mapView.treasures) }]
          : []),
        ...(this.state.data.hasBottleMap
          ? [{ name: 'Bottle map', draw: (ctx: CanvasRenderingContext2D, size: number) => this.mapView.drawTreasure(ctx, size, { x: this.player.x, z: this.player.z, yaw: this.player.yaw }, this.time, this.waypoint, { x: this.cart.x, z: this.cart.z }, { x: this.boat.x, z: this.boat.z }, this.mapView.captain, true) }]
          : []),
      ],
    );
  }

  /** A click on the full map sets (or clears) the destination. u and v are 0..1 across the map. */
  private pickDestination(u: number, v: number): void {
    const x = MAP_VIEW.cx - MAP_VIEW.half + u * 2 * MAP_VIEW.half;
    const z = MAP_VIEW.cz - MAP_VIEW.half + v * 2 * MAP_VIEW.half;
    if (this.waypoint && Math.hypot(this.waypoint.x - x, this.waypoint.z - z) < 6) {
      this.setWaypoint(null);
      this.ui.toast('Destination removed.');
      return;
    }
    this.setWaypoint({ x, z });
    this.ui.toast('Destination marked. Follow the dashed line or the glowing beam.');
  }

  private setWaypoint(w: Waypoint | null): void {
    this.waypoint = w;
    this.beam.visible = w !== null;
    if (w) this.beam.position.set(w.x, Math.max(this.beach.heightAt(w.x, w.z), this.ocean.surfaceY) + 30, w.z);
  }

  private setPaused(on: boolean): void {
    if (on === this.paused) return;
    this.paused = on;
    this.ui.showPause(on, {
      onResume: () => this.setPaused(false),
      onNew: () => this.newGame(),
      onSettings: () => this.openSettings(),
      onAchievements: () => this.showAchievements(),
      onMenu: () => this.backToMenu(),
      saves: this.saveHooks(),
    });
    this.audio.setOceanLevel(on ? 0.02 : this.gameState === 'INSPECT' ? 0.02 : 0.06);
    this.audio.setMusicLevel(on ? 0.08 : 0, 1);
    if (on) document.exitPointerLock?.();
    else this.grabMouse();
    this.input.keys.clear();
  }


  private onKey(code: string): void {
    if (!this.started) return;

    if (this.hammock) {
      if (code !== 'KeyN') this.leaveHammock(false);
      return;
    }
    if (code === 'KeyN') {
      this.audio.toggleMute();
      return;
    }
    if (code === 'KeyM') {
      if (this.mapOpen) this.setMap(false);
      else if (!this.paused && !this.alt && this.gameState === 'EXPLORE' && !this.transitioning) this.setMap(true);
      return;
    }
    if (code === 'Escape') {
      if (this.bookOpen) this.closeBook();
      else if (this.mapOpen) this.setMap(false);
      else if (this.paused) this.setPaused(false);
      else if (this.gameState === 'EXPLORE' && !this.transitioning) this.setPaused(true);
      else if (this.gameState === 'CAMP') this.closeCamp();
      else if (this.gameState === 'INSPECT') this.exitInspect();
      return;
    }
    if (this.mapOpen && code === 'KeyT') {
      this.ui.toggleMap();
      return;
    }
    if (this.bookOpen) {
      if (code === 'ArrowLeft' || code === 'KeyA') this.ui.bookTurn(-1);
      else if (code === 'ArrowRight' || code === 'KeyD') this.ui.bookTurn(1);
      return;
    }
    if (this.paused) return;
    if (this.gameState === 'INSPECT') {
      if (code === 'Escape') this.exitInspect();
      else if (code === 'Space' && this.ui.isResultOpen()) this.exitInspect();
      return;
    }
    if (this.underground) {
      if (code === 'KeyF') this.interaction?.run();
      else if (code.startsWith('Digit')) this.ui.toast('There is nothing to dig or shoot down here.');
      return;
    }
    if (this.diving) {
      // Q swims back up; F picks up what is in reach
      if (code === 'KeyQ') this.leaveDive();
      else if (code === 'KeyF') this.interaction?.run();
      else if (code.startsWith('Digit')) this.ui.toast('Down here the harpoon is your only tool.');
      return;
    }
    if (this.driving && code !== 'KeyF' && code !== 'KeyE') return;
    switch (code) {
      case 'KeyF':
        if (this.gameState === 'EXPLORE' && !this.transitioning) this.interaction?.run();
        break;
      case 'KeyE':
        if (this.gameState !== 'EXPLORE' || this.transitioning) break;
        if (this.interaction2) this.interaction2.run();
        else if (this.vehicle === 'boat') {
          this.audio.error();
          this.ui.toast('You cannot leave the boat out here. Park it right at the beach first, so you can climb back aboard.');
        }
        break;
      case 'Digit1': this.selectSlot(1); break;
      case 'Digit2': this.selectSlot(2); break;
      case 'Digit4': this.selectSlot(4); break;
      case 'Digit5': this.selectSlot(5); break;
      case 'Digit6': this.selectSlot(6); break;
      case 'Digit7': this.selectSlot(7); break;
      case 'Digit8': this.selectSlot(8); break;
      case 'Space':
        if (this.gameState === 'CAMP') break;
        this.selectSlot(this.tool === 1 ? 2 : 1);
        break;
      case 'Escape':
        if (this.gameState === 'CAMP') this.closeCamp();
        break;
    }
  }

  /** Raises the binoculars to the eyes (the zoomed view) or lowers them back into the hands. */
  private setBinocView(on: boolean): void {
    if (this.drawing) this.cancelDraw();
    const v = on && this.tool === 5;
    this.binocView = v;
    if (v) this.discover('binocs_first');
    if (v) this.zoomIdx = 0;
    this.view.setRaised(v);
  }

  private selectSlot(n: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8): void {
    if (!this.started && n !== 1) return;
    if (this.alt) {
      this.ui.toast(this.underground ? 'There is nothing to dig or shoot down here.' : 'Down here the harpoon is your only tool.');
      return;
    }
    if (n === 8 && !this.state.data.ownsBucket) {
      this.ui.toast('You do not have a bucket yet. The camper shop sells one.');
      return;
    }
    if (n === 7 && (this.state.data.flags ?? 0) <= 0) {
      this.ui.toast('You have no marker flags. The camper shop sells packs of 5.');
      return;
    }
    if (n === 6 && !this.state.data.ownsBow) {
      this.ui.toast('You do not own a bow yet. The camper shop sells one.');
      return;
    }
    if (n === 4 && !this.state.data.ownsGun) {
      this.ui.toast('You do not own a gun yet. The camper shop sells a toy pistol.');
      return;
    }
    if (this.vehicle === 'boat' && (n === 1 || n === 2 || n === 7 || n === 8)) {
      this.ui.toast('Out on the boat you can only use the binoculars or a weapon.');
      return;
    }
    if (this.gameState === 'INSPECT' || this.transitioning) return;
    if (n === 3) {
      if (this.gameState === 'CAMP') this.closeCamp();
      else this.openCamp('rest');
      return;
    }
    if (this.gameState === 'CAMP') this.closeCamp();
    this.tool = n;
    this.setBinocView(false);
    this.view.setTool(n);
    this.ui.setTool(n);
    this.detector.setActive(n === 1);
    this.dig.setActive(n === 2);
    this.audio.ui();
  }

  // ---------------------------------------------------------------- shooting

  private shoot(): void {
    const now = performance.now();
    if (now - this.shotCooldown < 260) return;
    this.shotCooldown = now;
    this.view.fire();
    this.audio.shot();
    this.discover('gun_first');
    // the bullet leaves the muzzle and flies toward whatever the crosshair is on
    const ray = this.input.ray(this.camera, true);
    const beachHit = this.beach.raycast(ray.ray.origin, ray.ray.direction);
    const aimDist = Math.min(beachHit ? beachHit.distanceTo(ray.ray.origin) : 60, 60);
    const aim = ray.ray.origin.clone().addScaledVector(ray.ray.direction, aimDist);
    this.camera.updateMatrixWorld(true);
    const muzzle = this.view.muzzle.getWorldPosition(new THREE.Vector3());
    this.bullets.spawn(muzzle, aim.sub(muzzle).normalize());
  }

  /** A bullet or arrow strikes a sandcastle: chunks fly off, and enough hits bring it down. */
  private hitCastle(obj: THREE.Object3D, at: THREE.Vector3 | undefined, power: number): void {
    const c = this.castles.castleOf(obj);
    if (!c) return;
    const p = at ?? new THREE.Vector3(c.x, this.beach.heightAt(c.x, c.z) + 0.5, c.z);
    this.dig.particles.burst(p, 18, 1.8, 1.8, 0.8, new THREE.Color(0xdcc38f));
    this.audio.thud();
    this.discover('castle_shot');
    if (this.castles.hit(c, at, power)) {
      this.dig.particles.burst(new THREE.Vector3(c.x, this.beach.heightAt(c.x, c.z) + 0.4, c.z), 34, 2.2, 2, 1, new THREE.Color(0xdcc38f));
      this.castles.remove(c);
      this.audio.crumble();
    }
  }

  private onBulletHit(obj: THREE.Object3D, impact?: THREE.Vector3, power = 0.14): void {
    if (this.castles.castleOf(obj)) {
      this.hitCastle(obj, impact, power);
      return;
    }
    if (obj.userData.shark || obj.userData.fish) {
      const res = this.sea.hit(obj);
      if (!res) return;
      this.audio.splash(5);
      if (res.kind === 'fish') {
        this.state.addCoins(FISH_REWARD);
        this.discover('fish_caught');
        this.ui.toast(`Caught a fish! +${FISH_REWARD}g`);
      } else if (res.killed) {
        this.state.addCoins(SHARK_REWARD);
        this.discover('shark_shot');
        this.ui.toast(`The shark is down! +${SHARK_REWARD}g`);
      } else {
        this.ui.toast('A hit! The shark bolts. One more shot should do it.');
      }
      return;
    }
    if (obj.userData.coconut) {
      const at = this.scenery.shootNut(obj);
      if (at) {
        this.dig.particles.burst(at, 18, 1.6, 1.4, 0.7, new THREE.Color(0xc9b28a));
        this.audio.splat();
        this.state.addCoins(COCONUT_REWARD);
        this.discover('coconut');
        this.ui.toast(`A coconut drops! +${COCONUT_REWARD}g`);
      }
      return;
    }
    if (obj.userData.gull) {
      const at = this.birds.shoot(obj);
      if (at) {
        this.dig.particles.burst(at, 40, 2.4, 1.2, 1.6, new THREE.Color(0xf6f4ee));
        this.audio.gull(4);
        const reward = this.birds.lastShotAirborne ? BIRD_AIR_REWARD : BIRD_REWARD;
        this.state.addCoins(reward);
        this.discover('bird_shot');
        this.ui.toast(`Poof! A flurry of feathers. +${reward}g`);
      }
      return;
    }
    const at = this.ecology.shoot(obj);
    if (!at) return;
    at.y += 0.15;
    this.dig.particles.burst(at, 28, 2.2, 3, 0.9, new THREE.Color(0xe8704f));
    this.audio.splat();
    this.crabsShot++;
    this.state.addCoins(CRAB_REWARD);
    this.discover('crab_shot');
    this.ui.toast(`Got one! +${CRAB_REWARD}g (${this.crabsShot})`);
  }

  // ---------------------------------------------------------------- digging

  private toolHit: THREE.Vector3 | null = null;

  private tryDig(hit: THREE.Vector3): void {
    if (this.onIsland) {
      this.audio.error();
      this.ui.toast('The island is too rocky to dig. Its treasures lie out in the open.');
      return;
    }
    if (!this.beach.canDig(hit.z)) {
      this.ui.toast('Leave the dunes in peace. Dig down on the open beach.');
      this.audio.error();
      return;
    }
    if (this.state.data.warmth <= 0) {
      this.ui.toast('You are too chilly to dig. Drink some coffee at the campfire to warm up.');
      this.audio.error();
      return;
    }
    if (this.grate && Math.hypot(hit.x - this.grate.x, hit.z - this.grate.z) < GRATE_R) {
      this.ui.toast('The old stone grate is set firm in the sand. There is no digging under it.');
      this.audio.error();
      return;
    }
    this.dig.dig(hit.x, hit.z, this.beach, this.state);
    this.tidyGrateGround();
    this.discover('dig_first');
    if (!this.holes.some(([x, z]) => Math.hypot(x - hit.x, z - hit.z) < 0.9)) this.holes.push([hit.x, hit.z]);
    this.view.doSwing();
    this.audio.crunch();
    const fresh = this.spawner.checkReveals();
    this.grass.trimDug();
    for (const it of fresh) {
      this.dig.particles.burst(new THREE.Vector3(it.x, this.beach.heightAt(it.x, it.z) + 0.2, it.z), 24, 1.6, 3.4, 1, new THREE.Color(0xf3e2b8));
      this.audio.coin();
      this.ui.toast('Something is in the hole. Click it!');
    }
    const max = this.state.maxWarmth;
    if (!this.chillWarned && this.state.data.warmth / max < 0.25) {
      this.chillWarned = true;
      this.ui.toast('Getting chilly… a warm drink awaits at camp.');
    }
    if (this.state.data.warmth / max > 0.6) this.chillWarned = false;
  }

  // ---------------------------------------------------------------- inspect

  /** What using a find does: most are picked up, but the captain's hatch has to be opened first. */
  private useFind(it: BuriedItem): void {
    if (it.def.id === 'captain_chest') {
      if (!it.opened) {
        this.spawner.openHatch(it);
        this.audio.thud();
        this.discover('captain_hatch');
        this.dig.particles.burst(new THREE.Vector3(it.x, this.beach.heightAt(it.x, it.z) + 0.1, it.z), 14, 1, 1.4, 0.6, new THREE.Color(0xe6ccb2));
      } else {
        this.enterUnderground();
      }
      return;
    }
    this.enterInspect(it);
  }

  private enterInspect(item: BuriedItem): void {
    this.setBinocView(false);
    this.transitioning = true;
    document.exitPointerLock?.();
    this.ui.fade(() => {
      this.inspectItem = item;
      this.gameState = 'INSPECT';
      this.station.enter(item, this.state.brushPx);
      this.ui.showInspect(item.def.name);
      this.ui.setProgress(this.station.progress);
      this.ui.setMode('INSPECT');
      this.audio.setOceanLevel(0.02);
      this.detector.setActive(false);
      this.dig.setActive(false);
      this.ui.setHint(null);
      setTimeout(() => (this.transitioning = false), 350);
    });
  }

  private onCleaned(): void {
    const item = this.inspectItem;
    if (!item) return;
    const def = item.def;
    this.audio.discover();
    if (def.id === 'message_bottle') {
      // the bottle itself is not kept: what matters is the map rolled up inside
      this.spawner.markCollected(item);
      this.takeBottleMap();
      this.ui.showResult(def.name, def.lore, 'A map is tucked inside. Open the map (M) and press T to read it.', 'Message in a bottle');
      return;
    }
    const res = this.state.addFound(def.id);
    const stored = res.item;
    if (ISLAND_ITEM_IDS.has(def.id)) this.discover('island_find');
    if (DIVE_ITEM_IDS.has(def.id)) this.discover('dive_find');
    if (def.id === 'treasure_cache') this.discover('x_marks');
    this.discover(def.category === 'trash' ? 'find_trash' : def.category === 'coin' ? 'find_coin' : def.category === 'jewelry' ? 'find_jewel' : def.category === 'shard' ? 'find_shard' : 'quest_first');
    this.spawner.markCollected(item);
    let worth: string;
    if (def.category === 'shard' && def.shardSet) {
      const n = this.state.data.shards[def.shardSet]?.length ?? 0;
      const set = SHARD_SETS.find((s) => s.id === def.shardSet)!;
      worth = `${n}/3 shards of the ${set.name}`;
    } else if (res.quest) {
      worth = res.quest.reward > 0
        ? `Delivered to ${res.quest.giver}: +${res.quest.reward}g`
        : `${res.quest.progress}/${res.quest.need} found for ${res.quest.giver}`;
      if (res.quest.reward > 0) {
        this.audio.coin();
        this.sendLetter(res.quest.id);
      }
    } else {
      const bonus = this.state.tier('cleaning') >= 2 ? '  (+25% brush bonus)' : '';
      worth = `Worth ${stored?.value ?? def.value}g${bonus}`;
    }
    this.ui.showResult(def.name, def.lore, worth, CATEGORY_LABEL[def.category]);
  }

  private exitInspect(): void {
    if (this.gameState !== 'INSPECT' || this.transitioning) return;
    this.transitioning = true;
    this.ui.fade(() => {
      this.station.exit();
      this.inspectItem = null;
      this.gameState = 'EXPLORE';
      this.ui.hideInspect();
      this.ui.setMode('EXPLORE');
      this.audio.setOceanLevel(this.diving ? 0.015 : 0.06);
      this.autoSave();
      if (this.diving) this.applyDiveHands();
      else {
        this.ui.setTool(this.tool);
        this.view.setTool(this.tool);
        this.detector.setActive(this.tool === 1);
        this.dig.setActive(this.tool === 2);
      }
      setTimeout(() => (this.transitioning = false), 350);
      this.grabMouse();
    });
  }

  // ---------------------------------------------------------------- camp

  private useCampObject(kind: CampKind): void {
    const tabs: Record<CampKind, CampTab | null> = {
      fire: 'rest', bin: 'recycle', board: 'board', bench: 'sell', lantern: null, camper: 'upgrades', radio: null, coffee: null, book: null, post: null, hammock: null, sign: null,
    };
    if (kind === 'coffee') {
      this.drinkCoffee();
      return;
    }
    if (kind === 'book') {
      this.openBook();
      return;
    }
    if (kind === 'post') {
      this.openMail();
      return;
    }
    if (kind === 'hammock') {
      this.enterHammock();
      return;
    }
    if (kind === 'sign') {
      this.state.cycleSign();
      this.audio.ui();
      return;
    }
    if (kind === 'radio') {
      const on = !this.audio.radioOn;
      this.audio.setRadio(on);
      this.camp.setRadio(on);
      if (on) this.discover('radio_on');
      this.audio.ui();
      this.ui.toast(on ? `📻 ${this.audio.trackName}` : 'Radio off.');
      return;
    }
    const tab = tabs[kind];
    if (tab) {
      this.openCamp(tab, false);
      return;
    }
    // the lantern is just a light: on or off
    this.lanternForce = !this.lanternOn;
    this.audio.ui();
  }

  /** walk: stroll over to the fire first (hotkey 3); clicking a camp prop opens it where you stand */
  private openCamp(tab: CampTab, walk = true): void {
    if (this.transitioning) return;
    this.setBinocView(false);
    document.exitPointerLock?.();
    if (this.gameState === 'EXPLORE') {
      if (walk && Math.hypot(this.player.x, this.player.z - CAMP_Z) > 6) {
        this.walkTo = { x: 0.5, z: CAMP_Z + 5, yaw: 0, pitch: -0.2 };
      }
      this.gameState = 'CAMP';
      this.detector.setActive(false);
      this.dig.setActive(false);
      this.view.setTool(0);
      this.ui.setTool(0);
      this.ui.setMode('CAMP');
    }
    this.audio.ui();
    this.campModal.open(tab);
  }

  private closeCamp(): void {
    this.campModal.close();
    if (this.gameState !== 'CAMP') return;
    this.autoSave();
    this.walkTo = null;
    this.gameState = 'EXPLORE';
    this.ui.setMode('EXPLORE');
    this.ui.setTool(this.tool);
    this.view.setTool(this.tool);
    this.detector.setActive(this.tool === 1);
    this.dig.setActive(this.tool === 2);
    this.grabMouse();
  }

  private cycleLighting(): void {
    if (!this.state.lanternUnlocked) return;
    const next = LIGHT_ORDER[(LIGHT_ORDER.indexOf(this.state.data.lighting) + 1) % LIGHT_ORDER.length];
    this.state.setLighting(next);
    this.audio.ui();
  }

  /** Advances the clock (or eases to a frozen time of day) and lights the world to match. */
  private updateDayNight(dt: number): void {
    const mode = this.state.data.lighting;
    const sleeping = this.hammock !== null;
    if (mode === 'cycle' || sleeping) {
      const next = this.dayTime + (dt * (sleeping ? this.hammockSpeed : 1)) / DAY_SECONDS;
      if (next >= 1) {
        // a new morning
        this.state.data.day++;
        this.state.emit();
        this.ui.toast(`A new day begins. Day ${this.state.data.day}`);
      }
      this.dayTime = next % 1;
    } else {
      const target = MOOD_TIME[mode];
      let diff = target - this.dayTime;
      diff -= Math.round(diff);
      this.dayTime = (this.dayTime + diff * Math.min(1, dt * 1.5) + 1) % 1;
    }
    // rare rainy days: a spell of rain on about one day in seven, only while the day cycle runs
    const win = rainWindow(this.state.data.day);
    const raining = mode === 'rain' || (mode === 'cycle' && win !== null && this.dayTime > win.start && this.dayTime < win.end);
    this.rain.update(dt, raining ? 1 : 0, this.camera.position);
    if (raining !== this.wasRaining) {
      this.wasRaining = raining;
      if (this.started) this.ui.toast(raining ? 'Clouds roll in... it begins to rain.' : 'The rain eases off.');
      if (raining) this.discover('rain_first');
    }
    this.sky.rain = this.rain.amount;
    if (this.sky.isDark) this.discover('night_first');
    this.audio.setRain(this.rain.amount);
    this.sky.focus.set(this.player.x, 0, this.player.z);
    this.sky.setTime(this.dayTime);
    this.ocean.setNight(this.sky.starAmount);
    const t = this.dayTime;
    this.ui.setClock(this.rain.amount > 0.4 ? '🌧️' : t < 0.08 || t > 0.94 ? '🌅' : t < 0.5 ? '☀️' : t < 0.64 ? '🌇' : '🌙', this.state.data.day);
  }

  /** null: lit by itself after dark; otherwise the player's choice */
  private lanternForce: boolean | null = null;

  private get lanternOn(): boolean {
    return this.lanternForce ?? this.sky.isDark;
  }

  // ---------------------------------------------------------------- controller

  private setUsingPad(on: boolean): void {
    if (on === this.usingPad) return;
    this.usingPad = on;
    document.body.classList.toggle('usingpad', on);
    this.ui.setPadMode(on);
    if (on) document.exitPointerLock?.();
    else this.mapCursorEl?.remove();
  }

  /** Holds a virtual key for the controller, without disturbing a key the keyboard is already holding. */
  private setPadKey(code: string, on: boolean): void {
    const keys = this.input.keys;
    if (on) {
      if (!keys.has(code)) {
        keys.add(code);
        this.padKeys.add(code);
      }
    } else if (this.padKeys.has(code)) {
      keys.delete(code);
      this.padKeys.delete(code);
    }
  }

  private padKey(code: string): void {
    this.padCalling = true;
    try {
      this.input.handlers.onKey?.(code);
    } finally {
      this.padCalling = false;
    }
  }

  /** The menu or card the controller should drive right now, if any. */
  private overlayRoot(): HTMLElement | null {
    const list = Array.from(document.querySelectorAll<HTMLElement>('.mainmenu, .pause:not(.bigmap), .camp:not(.hidden), .intro:not(.gone)'));
    return list.reduce<HTMLElement | null>((best, el) => (!best || best.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING ? el : best), null);
  }

  /** A dropdown cannot be opened from code, so a controller gets its choices as a short list of buttons under the row. */
  private openPadOptions(sel: HTMLSelectElement): void {
    const list = document.createElement('div');
    list.className = 'set-options';
    Array.from(sel.options).forEach((o, i) => {
      const b = document.createElement('button');
      b.className = i === sel.selectedIndex ? 'set-option on' : 'set-option';
      b.textContent = o.textContent;
      b.onclick = (): void => {
        const changed = i !== sel.selectedIndex;
        sel.selectedIndex = i;
        this.closePadOptions(list);
        if (changed) sel.dispatchEvent(new Event('change', { bubbles: true }));
      };
      list.append(b);
    });
    this.padSelect = sel;
    (sel.closest('.set-row') ?? sel).after(list);
    list.querySelector<HTMLElement>('.on')?.focus();
    list.scrollIntoView({ block: 'nearest' });
    this.audio.ui();
  }

  private closePadOptions(list: HTMLElement): void {
    list.remove();
    this.padSelect?.focus();
    this.padSelect = null;
    this.audio.ui();
  }

  private padFocusables(root: HTMLElement): HTMLElement[] {
    // while a list of choices is open, only its choices can be reached
    const scope = root.querySelector<HTMLElement>('.set-options') ?? root;
    return Array.from(scope.querySelectorAll<HTMLElement>('button, input, select')).filter((e) => {
      if ((e as HTMLButtonElement).disabled) return false;
      const r = e.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    });
  }

  /** The part of a menu that scrolls, if it is long enough to need it. */
  private padScroller(root: HTMLElement): HTMLElement | null {
    const all = Array.from(root.querySelectorAll<HTMLElement>('.camp-body, .help-body, .ach-list'));
    return all.find((e) => e.scrollHeight > e.clientHeight + 1) ?? null;
  }

  private padNav(d: PadDir): void {
    const root = this.overlayRoot();
    if (!root) return;
    const items = this.padFocusables(root);
    if (!items.length) return;
    const cur = items.find((e) => e === document.activeElement);
    if (!cur) {
      items[0].focus();
      return;
    }
    // from outside a scrolled list, up first rolls the list back before leaving for what is above it
    const scroller = this.padScroller(root);
    if (scroller && d === 'up' && scroller.scrollTop > 0 && !scroller.contains(cur)) {
      scroller.scrollBy(0, -150);
      return;
    }
    if (cur instanceof HTMLInputElement && cur.type === 'range' && (d === 'left' || d === 'right')) {
      if (d === 'left') cur.stepDown();
      else cur.stepUp();
      cur.dispatchEvent(new Event('input', { bubbles: true }));
      return;
    }
    const c = cur.getBoundingClientRect();
    const cx = c.left + c.width / 2;
    const cy = c.top + c.height / 2;
    const vertical = d === 'up' || d === 'down';
    const options: { el: HTMLElement; along: number; across: number }[] = [];
    for (const it of items) {
      if (it === cur) continue;
      const r = it.getBoundingClientRect();
      const dx = r.left + r.width / 2 - cx;
      const dy = r.top + r.height / 2 - cy;
      const along = d === 'up' ? -dy : d === 'down' ? dy : d === 'left' ? -dx : dx;
      if (along > 2) options.push({ el: it, along, across: vertical ? Math.abs(dx) : Math.abs(dy) });
    }
    let best: HTMLElement | null = null;
    let bestScore = Infinity;
    // up and down go to the very next row, whatever is in it: a small checkbox off to one side must not be skipped for a wide slider further on
    const nearest = options.length ? Math.min(...options.map((o) => o.along)) : 0;
    for (const o of options) {
      if (vertical && o.along > nearest + 14) continue;
      const score = vertical ? o.across : o.along + o.across * 2.2;
      if (score < bestScore) {
        bestScore = score;
        best = o.el;
      }
    }
    if (best) {
      best.focus();
      best.scrollIntoView({ block: 'nearest' });
      this.audio.ui();
    } else if (scroller && (d === 'up' || d === 'down')) {
      // nothing further to move to: a page of plain text, like the notes on the board, just scrolls
      scroller.scrollBy(0, d === 'down' ? 150 : -150);
    }
  }

  private padBack(root: HTMLElement): void {
    const back = Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => /^\W*(done|back|close|cancel|resume|leave)/i.test(b.textContent ?? ''));
    if (back && !back.disabled) back.click();
    else this.padKey('Escape');
  }

  private padButton(b: PadButton, down: boolean): void {
    this.setUsingPad(true);

    if (this.hammock) {
      if (down) this.leaveHammock(false);
      return;
    }
    const root = this.overlayRoot();
    if (root) {
      if (!down) return;
      if (b === 'a') {
        const cur = document.activeElement as HTMLElement | null;
        if (cur instanceof HTMLSelectElement) this.openPadOptions(cur);
        else if (cur && root.contains(cur) && cur.tagName !== 'BODY') cur.click();
        else this.padFocusables(root)[0]?.focus();
      } else if (b === 'b') {
        const open = root.querySelector<HTMLElement>('.set-options');
        if (open) this.closePadOptions(open);
        else this.padBack(root);
      }
      else if ((b === 'lb' || b === 'rb') && this.campModal.isOpen) this.campModal.stepTab(b === 'rb' ? 1 : -1);
      else if (b === 'lb' && this.bookOpen) this.ui.bookTurn(-1);
      else if (b === 'rb' && this.bookOpen) this.ui.bookTurn(1);
      else if (b === 'start' && this.paused) this.padKey('Escape');
      return;
    }
    if (this.mapOpen) {
      if (!down) return;
      if (b === 'lb' || b === 'rb') this.ui.toggleMap();
      else if (b === 'b' || b === 'y' || b === 'start') this.padKey('Escape');
      else if (b === 'a') {
        const c = document.querySelector<HTMLCanvasElement>('.bigmap-canvas');
        if (c) {
          const r = c.getBoundingClientRect();
          c.dispatchEvent(new MouseEvent('click', { clientX: r.left + this.mapCursor.u * r.width, clientY: r.top + this.mapCursor.v * r.height, bubbles: true }));
        }
      }
      return;
    }
    if (this.gameState === 'INSPECT') {
      if (!down) return;
      if (b === 'a' || b === 'start') {
        if (this.ui.isResultOpen()) this.padKey('Space');
        else if (b === 'start') this.padKey('Escape');
      } else if (b === 'b') this.padKey('Escape');
      else if (b === 'y') Array.from(document.querySelectorAll<HTMLButtonElement>('button:not(.hidden)')).find((x) => x.textContent === 'Instant Clean')?.click();
      return;
    }
    if (!this.started) return;
    if (b === 'rt' || b === 'lt') {
      if (this.driving || this.paused) return;
      const btn = b === 'rt' ? 0 : 2;
      this.padCalling = true;
      try {
        if (down) this.input.handlers.onPointerDown?.(btn);
        else {
          this.input.handlers.onPointerUp?.(btn, false);
          this.input.handlers.onClick?.(btn);
        }
      } finally {
        this.padCalling = false;
      }
      return;
    }
    if (this.underground) {
      if (down && b === 'a') this.padKey('KeyF');
      else if (down && b === 'start') this.padKey('Escape');
      else if (down && b === 'back') this.padKey('KeyN');
      return;
    }
    if (this.diving) {
      // swimming: the bumpers go up and down, the rest keep their usual jobs
      if (b === 'rb') this.lift.up = down;
      else if (b === 'lb') this.lift.down = down;
      else if (down && b === 'a') this.padKey('KeyF');
      else if (down && b === 'x') this.padKey('KeyQ');
      else if (down && b === 'start') this.padKey('Escape');
      else if (down && b === 'back') this.padKey('KeyN');
      return;
    }
    if (!down) return;
    switch (b) {
      case 'a': this.padKey('KeyF'); break;
      case 'x': this.padKey('KeyE'); break;
      case 'y': this.padKey('KeyM'); break;
      case 'start': this.padKey('Escape'); break;
      case 'back': this.padKey('KeyN'); break;
      case 'r3': if (!this.driving) this.padKey('Space'); break;
      case 'lb': this.onWheel(-1); break;
      case 'rb': this.onWheel(1); break;
      case 'up': this.padKey('Digit1'); break;
      case 'right': this.padKey('Digit2'); break;
      case 'down': this.padKey('Digit5'); break;
      case 'left': this.padOtherTool(); break;
      default: break;
    }
  }

  /** The controller lets go of the cleaning brush: the mouse has the pointer again and the trigger no longer counts as held. */
  private releasePadBrush(): void {
    if (!this.padBrush) return;
    this.padBrush = false;
    this.input.padPointer = false;
    this.input.buttons &= ~1;
    this.station.brushScale = 1;
  }

  /** Per-frame controller work: menu focus, looking, driving and the map cursor. */
  private padUpdate(dt: number): void {
    const pad = this.pad;
    if (!pad.connected || !this.usingPad) {
      for (const c of Array.from(this.padKeys)) this.setPadKey(c, false);
      this.releasePadBrush();
      return;
    }
    const root = this.overlayRoot();
    if (root) {
      for (const c of Array.from(this.padKeys)) this.setPadKey(c, false);
      const items = this.padFocusables(root);
      const cur = document.activeElement as HTMLElement | null;
      // the camp opens on its current page, not on the Close button at the top
      if (items.length && (!cur || !items.includes(cur))) (root.querySelector<HTMLElement>('.tab.on') ?? items[0]).focus();
      // the right stick scrolls whatever long list is showing
      if (pad.look.y) this.padScroller(root)?.scrollBy(0, pad.look.y * dt * 700);
      return;
    }
    if (this.mapOpen) {
      const c = document.querySelector<HTMLCanvasElement>('.bigmap-canvas');
      if (c) {
        this.mapCursor.u = Math.min(1, Math.max(0, this.mapCursor.u + pad.move.x * dt * 0.7));
        this.mapCursor.v = Math.min(1, Math.max(0, this.mapCursor.v + pad.move.y * dt * 0.7));
        if (!this.mapCursorEl) {
          this.mapCursorEl = document.createElement('div');
          this.mapCursorEl.className = 'map-cursor';
          document.body.append(this.mapCursorEl);
        }
        const r = c.getBoundingClientRect();
        this.mapCursorEl.style.left = `${r.left + this.mapCursor.u * r.width}px`;
        this.mapCursorEl.style.top = `${r.top + this.mapCursor.v * r.height}px`;
      }
      return;
    }
    if (this.mapCursorEl) {
      this.mapCursorEl.remove();
      this.mapCursorEl = null;
    }
    const k = this.input.keys;
    void k;
    if (this.gameState === 'INSPECT') {
      // left stick turns the find, right stick moves the brush, RT scrubs
      const m = pad.move;
      const l = pad.look;
      if (!this.padBrush) {
        this.padBrush = true;
        this.input.padPointer = true;
        this.input.pointer.set(0, 0);
        this.station.brushScale = 1.7;
      }
      this.station.rotate(m.x * dt * 2.2, m.y * dt * 1.8);
      // the same speed on screen both ways, and never off the find
      const aspect = this.canvas.clientWidth / Math.max(1, this.canvas.clientHeight);
      this.input.pointer.x = Math.max(-1, Math.min(1, this.input.pointer.x + (l.x * dt * 1.6) / aspect));
      this.input.pointer.y = Math.max(-1, Math.min(1, this.input.pointer.y - l.y * dt * 1.6));
      if (l.x || l.y) this.station.keepOnItem(this.input.pointer);
      if (pad.rt > 0.3) {
        this.input.buttons |= 1;
        // holding the trigger is enough: a stick is not a mouse, and it can rest while the find turns under the brush
        this.input.speed = Math.max(this.input.speed, 120, Math.hypot(l.x, l.y) * 500, Math.hypot(m.x, m.y) * 500);
      } else this.input.buttons &= ~1;
      return;
    }
    this.releasePadBrush();
    for (const c of ['ArrowRight', 'ArrowLeft', 'ArrowDown', 'ArrowUp']) this.setPadKey(c, false);
    if (!this.started || this.paused || this.transitioning || this.gameState !== 'EXPLORE') return;

    // looking: the right stick, eased so small pushes aim finely
    const l = pad.look;
    if (l.x || l.y) {
      const sx = Math.sign(l.x) * Math.abs(l.x) ** 1.6;
      const sy = Math.sign(l.y) * Math.abs(l.y) ** 1.6;
      this.applyLook(sx * dt * 950, sy * dt * 650);
    }
    // L3 is a tap: it latches running while the stick is held, and letting go of the stick ends the run
    const l3 = pad.isDown('l3');
    if (l3 && !this.padL3Was) this.padRun = true;
    this.padL3Was = l3;
    if (this.driving || (pad.move.x === 0 && pad.move.y === 0)) this.padRun = false;
    this.setPadKey('ShiftLeft', this.padRun);
    if (this.driving) {
      const m = pad.move;
      this.setPadKey('KeyW', pad.rt > 0.15 || m.y < -0.5);
      this.setPadKey('KeyS', pad.lt > 0.15 || m.y > 0.5);
      this.setPadKey('KeyA', m.x < -0.35);
      this.setPadKey('KeyD', m.x > 0.35);
      this.setPadKey('Space', pad.isDown('b') || pad.isDown('r3'));
    } else {
      for (const c of ['KeyW', 'KeyS', 'KeyA', 'KeyD', 'Space']) this.setPadKey(c, false);
    }
  }

  // ---------------------------------------------------------------- main menu and settings

  private applySettings(): void {
    const st = this.settings;
    this.audio.setVolumes(st.master, st.music, st.ambience);
    this.renderer.setPixelRatio((st.sharp ? Math.min(window.devicePixelRatio, MAX_PIXEL_RATIO) : 1) * st.renderScale);
    this.resize();
    const changed = this.renderer.shadowMap.enabled !== st.shadows;
    this.renderer.shadowMap.enabled = st.shadows;
    this.sky.sun.castShadow = st.shadows;
    if (changed) {
      // lit materials were compiled with or without shadows, so ask for a rebuild
      this.scene.traverse((o) => {
        const m = (o as THREE.Mesh).material as THREE.Material | THREE.Material[] | undefined;
        if (Array.isArray(m)) m.forEach((x) => (x.needsUpdate = true));
        else if (m) m.needsUpdate = true;
      });
    }
  }

  private shownDisplay = '';

  /** Fullscreen, windowed or borderless, and the window size. The desktop app does all three; a browser can only go fullscreen. */
  private applyDisplay(atStart: boolean): void {
    const st = this.settings;
    const key = `${st.displayMode}|${st.windowSize}`;
    if (key === this.shownDisplay) return;
    this.shownDisplay = key;
    const desktop = (window as unknown as { desktop?: { setDisplay: (c: { mode: string; width: number; height: number }) => Promise<{ restart: boolean }> } }).desktop;
    if (desktop) {
      const [width, height] = st.windowSize.split('x').map(Number);
      void desktop.setDisplay({ mode: st.displayMode, width: width || 1280, height: height || 720 }).then((r) => {
        if (r?.restart && !atStart) this.ui.toast('Close and reopen the game to change the window border.');
      });
      return;
    }
    if (atStart) return; // a browser only allows fullscreen after a click
    if (st.displayMode === 'fullscreen') void document.documentElement.requestFullscreen?.().catch(() => undefined);
    else if (document.fullscreenElement) void document.exitFullscreen?.().catch(() => undefined);
  }

  private openSettings(onClose?: () => void): void {
    this.ui.openSettings(
      this.settings,
      (s) => {
        this.settings = s;
        this.applySettings();
        this.applyDisplay(false);
        saveSettings(s);
      },
      onClose,
    );
  }

  /** The title screen: the island carries on living behind it while a slow camera drifts around. */
  private enterMenu(): void {
    this.gameState = 'MENU';
    this.started = false;
    this.view.group.visible = false;
    this.view.proxy.visible = false;
    this.detector.setActive(false);
    this.dig.setActive(false);
    this.ui.skipIntro();
    this.ui.setMode('MENU');
    this.menuT = 0;
    this.menuShot = 0;
    this.pickMenuTarget();
    this.showMenu();
  }

  private showMenu(): void {
    this.ui.showMainMenu({
      hasSaves: listSaves().length > 0,
      onAchievements: () => this.showAchievements(),
      onNew: () => this.menuNewGame(),
      onLoad: () => this.ui.openSaves('load', this.saveHooks()),
      onSettings: () => this.openSettings(),
      onExit: () => {
        // in the desktop app this really closes the game
        const desktop = (window as unknown as { desktop?: { quit: () => void } }).desktop;
        if (desktop) {
          desktop.quit();
          return;
        }
        this.ui.hideMainMenu();
        try {
          window.close();
        } catch {
          /* browsers only allow this for windows a script opened */
        }
        this.ui.showFarewell(() => this.showMenu());
      },
    });
  }

  /** "New Game": the controls card first, then the adventure begins. */
  private menuNewGame(): void {
    this.ui.hideMainMenu();
    this.ui.showIntro(() => this.beginPlay());
  }

  private beginPlay(): void {
    this.ui.fade(() => {
      this.gameState = 'EXPLORE';
      this.ui.setMode('EXPLORE');
      this.started = true;
      // a new game starts at the foot of the camp, looking at it
      this.player.x = 0;
      this.player.z = CAMP_Z + 9;
      this.player.yaw = 0;
      this.player.pitch = -0.12;
      this.eyeY = 0;
      this.view.proxy.visible = true;
      this.camera.fov = this.baseFov;
      this.camera.updateProjectionMatrix();
      this.selectSlot(1);
      this.ui.toast('Sweep the sand with your detector. Listen for the beep.');
      // the click on Begin was a moment ago, so the game can take the mouse right away and the pointer never shows
      this.grabMouse();
    });
  }

  /** Chooses what the next menu shot looks at: a crab on the beach, or a shark close to shore. */
  private pickMenuTarget(): void {
    this.menuTarget = null;
    this.menuCamReady = false;
    if (this.menuShot === 1) {
      const crabs = this.ecology.crabs.filter((c) => c.alive);
      if (crabs.length) {
        this.menuTarget = crabs[Math.floor(Math.random() * crabs.length)].mesh;
        this.menuFocus.copy(this.menuTarget.getWorldPosition(new THREE.Vector3()));
      }
    } else if (this.menuShot === 2) {
      const shark = this.sea.sharks.find((s) => s.alive);
      if (shark) {
        // bring one in close enough to watch from the shore
        shark.x = (Math.random() - 0.5) * 24;
        shark.z = SHORE_Z + 20;
        shark.heading = Math.PI / 2 + (Math.random() - 0.5) * 0.6;
        this.menuTarget = shark.root;
        this.menuFocus.copy(shark.root.getWorldPosition(new THREE.Vector3()));
      }
    }
  }

  private updateMenuCamera(dt: number): void {
    this.menuT += dt;
    if (!this.menuSwitching && this.menuT > 17) {
      this.menuSwitching = true;
      this.ui.fade(() => {
        this.menuShot = (this.menuShot + 1) % 3;
        this.menuT = 0;
        this.menuSwitching = false;
        this.pickMenuTarget();
      });
    }
    const cam = this.camera;
    const look = new THREE.Vector3();
    const want = new THREE.Vector3();
    const t = this.menuT;
    let follow = 0; // how quickly the camera catches up with what it is watching; 0 = not smoothed
    if (this.menuShot === 0 || !this.menuTarget) {
      // a slow circle round the camp, with the sea behind it
      const a = 0.35 + t * 0.07;
      const x = 3 + Math.cos(a) * 15;
      const z = CAMP_Z + 4 + Math.sin(a) * 15;
      want.set(x, this.beach.heightAt(x, z) + 3.4 + Math.sin(t * 0.3) * 0.4, z);
      look.set(0, this.beach.heightAt(0, CAMP_Z) + 1.8, CAMP_Z);
    } else if (this.menuShot === 1) {
      // low and close, drifting along behind a crab: it is the smoothed focus, not the crab, that the camera tracks
      const tp = this.menuTarget.getWorldPosition(new THREE.Vector3());
      this.menuFocus.lerp(tp, 1 - Math.exp(-dt * 1.6));
      const a = t * 0.12;
      const f = this.menuFocus;
      want.set(f.x + Math.cos(a) * 3.4, this.beach.heightAt(f.x, f.z) + 0.95, f.z + Math.sin(a) * 3.4);
      look.copy(f).add(new THREE.Vector3(0, 0.2, 0));
      follow = 2.2;
    } else {
      // from the water's edge, watching a shark swim by
      const tp = this.menuTarget.getWorldPosition(new THREE.Vector3());
      this.menuFocus.lerp(tp, 1 - Math.exp(-dt * 1.2));
      const f = this.menuFocus;
      const cx = Math.max(-18, Math.min(18, f.x * 0.35));
      want.set(cx, this.beach.heightAt(cx, SHORE_Z - 4) + 1.9, SHORE_Z - 4);
      look.copy(f).add(new THREE.Vector3(0, 0.5, 0));
      follow = 2.2;
    }
    if (follow > 0 && !this.menuCamReady) {
      cam.position.copy(want);
      this.menuCamReady = true;
    } else if (follow > 0) {
      cam.position.lerp(want, 1 - Math.exp(-dt * follow));
    } else {
      cam.position.copy(want);
    }
    cam.lookAt(look);
  }

  // ---------------------------------------------------------------- save / load

  private snapshot(): Snapshot {
    // saved while diving, the game comes back on the beach where the dive began
    const pl = this.alt && this.diveReturn ? this.diveReturn : this.player;
    return {
      v: 1,
      savedAt: Date.now(),
      data: JSON.parse(JSON.stringify(this.state.data)),
      player: { x: pl.x, z: pl.z, yaw: pl.yaw, pitch: pl.pitch },
      tool: this.tool,
      radio: this.audio.radioOn,
      radioTrack: this.audio.trackNumber,
      dayTime: this.dayTime,
      size: BEACH_SEGMENTS,
      holes: this.holes,
      waypoint: this.waypoint,
      flags: this.flags.positions.map(([x, z]) => [x, z] as [number, number]),
      castles: this.castles.data(),
      dog: { target: this.dog.item ? this.spawner.items.indexOf(this.dog.item) : -1 },
      cart: { x: this.cart.x, z: this.cart.z, heading: this.cart.heading },
      boat: { x: this.boat.x, z: this.boat.z, heading: this.boat.heading },
      chests: this.treasure.takenFlags(),
      items: this.spawner.exportItems(),
      dug: this.beach.exportDug(),
    };
  }

  private saveHooks(): { list: () => SaveMeta[]; save: (n: string, id?: string) => SaveMeta | null; load: (id: string) => void; remove: (id: string) => void } {
    return {
      list: () => listSaves(),
      save: (name, id) => this.saveGame(name, id),
      load: (id) => this.loadGame(id),
      remove: (id) => deleteSave(id),
    };
  }

  /** The achievements that depend on how much you have done, rather than on one thing happening. */
  private checkAchievements(): void {
    if (!this.started || this.gameState === 'MENU') return;
    const d = this.state.data;
    const a = this.achievements;
    const finds = Object.values(d.found).reduce((sum, n) => sum + n, 0);
    if (finds >= 10) a.unlock('FINDS_10');
    if (finds >= 50) a.unlock('FINDS_50');
    if (finds >= 150) a.unlock('FINDS_150');
    if ((d.questsDone?.length ?? 0) >= 1) a.unlock('QUEST_FIRST');
    if (QUEST_DEFS.every((q) => d.questsDone?.includes(q.id))) a.unlock('QUEST_ALL');
    if ((d.assembled?.length ?? 0) >= 2) a.unlock('ASSEMBLED');
    if (d.coins >= 2000) a.unlock('RICH');
    if (d.ownsBoatKey) a.unlock('BOAT');
    if ((d.decor?.length ?? 0) >= DECOR.length) a.unlock('DECOR');
    if (d.day >= 7) a.unlock('WEEK');
  }

  /** The list of achievements on screen, with the earned ones marked. */
  private showAchievements(): void {
    this.ui.showAchievements(ACHIEVEMENTS.map((x) => ({ name: x.name, desc: x.desc, icon: x.icon, done: this.achievements.has(x.id), secret: !!x.secret })));
  }

  private autoT = 0;
  private lastAuto = -Infinity;

  /**
   * Saves the world into the single autosave slot, over whatever was there. Quiet: no message. It skips the menu, sleeping and
   * the ending, and never runs twice in quick succession.
   */
  private autoSave(): void {
    this.autoT = 0;
    const now = performance.now();
    if (now - this.lastAuto < AUTOSAVE_MIN_GAP) return;
    if (!this.started || this.gameState === 'MENU' || this.hammock) return;
    this.lastAuto = now;
    writeSave(this.snapshot(), 'Autosave', AUTOSAVE_ID);
  }

  /** Writes the whole world as a new save, or over an existing one. */
  private saveGame(name: string, id?: string): SaveMeta | null {
    const meta = writeSave(this.snapshot(), name, id);
    if (!meta) {
      this.ui.toast('Could not save. The browser storage is full or blocked.');
      return null;
    }
    this.audio.coin();
    this.ui.toast(id ? `Saved over "${meta.name}".` : `Saved as "${meta.name}".`);
    return meta;
  }

  private loadGame(id: string): void {
    requestLoad(id);
    location.reload();
  }

  /** A fresh start is simply a fresh page; nothing is loaded unless you ask for it. */
  private newGame(): void {
    requestAutostart();
    location.reload();
  }

  private backToMenu(): void {
    location.reload();
  }

  /** Called once on start-up after a load: puts the saved world back. */
  private restoreWorld(s: Snapshot): void {
    // a save from a smaller, older map keeps your progress but not the old holes and finds
    const sameMap = s.size === BEACH_SEGMENTS;
    if (sameMap) {
      this.spawner.importItems(s.items);
      this.beach.importDug(s.dug);
      this.holes = s.holes ?? [];
      this.setWaypoint(s.waypoint ?? null);
      if (s.cart) {
        this.cart.x = s.cart.x;
        this.cart.z = s.cart.z;
        this.cart.heading = s.cart.heading;
        this.cart.update(0, null, (x, z) => ({ x, z, hit: false }));
      }
      this.treasure.setTaken(s.chests ?? []);
      if (s.boat) {
        this.boat.x = s.boat.x;
        this.boat.z = s.boat.z;
        this.boat.heading = s.boat.heading;
      }
      this.spawner.checkReveals();
      const job = s.dog && s.dog.target >= 0 ? this.spawner.items[s.dog.target] : undefined;
      if (job && !job.collected) this.dog.startMission(job, true);
      this.player.x = s.player.x;
      this.player.z = s.player.z;
    } else {
      this.player.x = 0;
      this.player.z = CAMP_Z + 9;
    }
    this.flags.setAll(s.flags ?? [], (x, z) => this.beach.heightAt(x, z) - 0.05);
    this.castles.setAll(s.castles ?? []);
    this.player.yaw = s.player.yaw;
    this.player.pitch = s.player.pitch;
    this.started = true;
    this.selectSlot(s.tool);
    this.audio.setTrack(s.radioTrack ?? 0);
    if (s.radio) {
      this.audio.setRadio(true);
      this.camp.setRadio(true);
    }
    this.ui.toast(sameMap ? 'Game loaded. Welcome back.' : 'Game loaded. The map has grown, so the beach was laid out fresh.');
  }

  // ---------------------------------------------------------------- coffee

  /** A warm drink from the thermos: refills Warmth. The beach itself never changes. */
  private drinkCoffee(): void {
    if (this.state.data.warmth >= this.state.maxWarmth) {
      this.ui.toast('You are already warm and cozy.');
      return;
    }
    this.state.restoreWarmth();
    this.discover('coffee_first');
    this.audio.sip();
    this.ui.toast('Ahh. Warm again.');
  }

  // ---------------------------------------------------------------- frame

  /** Bow draw strength and the binocular zoom, which both change the field of view. */
  private applyToolFov(dt: number): void {
    if (this.drawing && this.canUseBow()) {
      this.charge = Math.min(1, this.charge + dt / 0.9);
      this.view.setDraw(this.charge);
      this.ui.setDraw(this.charge);
    } else if (this.drawing) {
      this.cancelDraw();
    }
    // a slight tunnel of focus as the bow is drawn
    const wantFov = this.binocView && this.gameState === 'EXPLORE'
      ? (this.zoomIdx === 0 ? 16 : 7)
      : this.baseFov - (this.drawing ? this.charge * 7 : 0);
    if (Math.abs(this.camera.fov - wantFov) > 0.05) {
      this.camera.fov += (wantFov - this.camera.fov) * Math.min(1, dt * 9);
      this.camera.updateProjectionMatrix();
    }
  }

  private updateCamera(dt: number): void {
    if (this.hammock) {
      this.updateHammock(dt);
      return;
    }
    if (this.underground) {
      this.updateUnderground(dt);
      return;
    }
    if (this.diving) {
      this.updateDive(dt);
      return;
    }
    if (this.driving) {
      this.updateDriving(dt);
      return;
    }
    if (this.vehicle === 'boat') {
      this.updateDeck(dt);
      return;
    }
    const p = this.player;
    this.moving = false;
    const px0 = p.x;
    const pz0 = p.z;
    // No pointer lock available? Steer by holding the cursor away from the screen centre, like a gamepad stick.
    if (this.started && this.gameState === 'EXPLORE' && this.cursorSteer && !this.walkTo) {
      const { x, y } = this.input.pointer;
      const dz = (v: number, zone: number): number => (Math.abs(v) < zone ? 0 : Math.sign(v) * (Math.abs(v) - zone) / (1 - zone));
      const zk = this.camera.fov / this.baseFov;
      p.yaw -= dz(x, 0.15) * 2.6 * dt * zk * this.settings.sensitivity;
      p.pitch = Math.min(1.45, Math.max(-1.45, p.pitch + dz(y, 0.2) * 1.5 * dt * zk * this.settings.sensitivity));
    }
    if (this.started && this.gameState === 'EXPLORE') {
      const k = this.input.keys;
      const pm = this.pad.move;
      const f = Math.max(-1, Math.min(1, (k.has('KeyW') ? 1 : 0) - (k.has('KeyS') ? 1 : 0) - pm.y));
      const s = Math.max(-1, Math.min(1, (k.has('KeyD') ? 1 : 0) - (k.has('KeyA') ? 1 : 0) + pm.x));
      if (f || s) {
        this.walkTo = null;
        const len = Math.max(1, Math.hypot(f, s));
        const sp = (k.has('ShiftLeft') ? 5.2 : 3.2) * dt;
        // three's rotation.y: forward is (-sin, -cos), right is (cos, -sin)
        const fx = -Math.sin(p.yaw);
        const fz = -Math.cos(p.yaw);
        const rx = Math.cos(p.yaw);
        const rz = -Math.sin(p.yaw);
        p.x += ((fx * f + rx * s) / len) * sp;
        p.z += ((fz * f + rz * s) / len) * sp;
        this.moving = true;
      }
    } else if (this.walkTo) {
      const w = this.walkTo;
      const k = 1 - Math.exp(-dt * 2.5);
      let dyaw = w.yaw - p.yaw;
      dyaw = Math.atan2(Math.sin(dyaw), Math.cos(dyaw));
      p.x += (w.x - p.x) * k;
      p.z += (w.z - p.z) * k;
      p.yaw += dyaw * k;
      p.pitch += (w.pitch - p.pitch) * k;
      this.moving = Math.hypot(w.x - p.x, w.z - p.z) > 0.3;
    }
    // on the island the limit is the water's edge; on the beach it is the shore and the dunes
    const onIsland = this.island.contains(p.x, p.z);
    if (onIsland) {
      if (this.beach.heightAt(p.x, p.z) < WATER_LEVEL + 0.25) {
        p.x = px0;
        p.z = pz0;
      }
      for (const s of this.island.colliders) {
        const d = Math.hypot(p.x - s.x, p.z - s.z);
        const min = s.r + 0.3;
        if (d < min) {
          p.x = s.x + ((p.x - s.x) / (d || 1)) * min;
          p.z = s.z + ((p.z - s.z) / (d || 1)) * min;
        }
      }
      this.discover('island_first');
      if (Math.hypot(p.x - this.island.ruins.x, p.z - this.island.ruins.z) < 7) this.discover('island_ruins');
    } else {
      // keep out of the sea, off the edge, and out of the fire
      p.x = Math.min(HALF - 2, Math.max(-(HALF - 2), p.x));
      p.z = Math.min(SHORE_Z - 3, Math.max(-(HALF - 2), p.z));
    }
    for (const o of this.camp.obstacles) {
      const d = Math.hypot(p.x - o.x, p.z - o.z);
      if (d < o.r) {
        p.x = o.x + ((p.x - o.x) / (d || 1)) * o.r;
        p.z = o.z + ((p.z - o.z) / (d || 1)) * o.r;
      }
    }
    // the parked cart is long, so it blocks walking with a nose, a middle and a tail
    for (const off of [-1.1, 0, 1.1]) {
      const cx = this.cart.x - Math.sin(this.cart.heading) * off;
      const cz = this.cart.z - Math.cos(this.cart.heading) * off;
      const d = Math.hypot(p.x - cx, p.z - cz);
      if (d < 1.05) {
        p.x = cx + ((p.x - cx) / (d || 1)) * 1.05;
        p.z = cz + ((p.z - cz) / (d || 1)) * 1.05;
      }
    }
    for (const s of this.castles.colliders) {
      const d = Math.hypot(p.x - s.x, p.z - s.z);
      const min = s.r + 0.3;
      if (d < min) {
        p.x = s.x + ((p.x - s.x) / (d || 1)) * min;
        p.z = s.z + ((p.z - s.z) / (d || 1)) * min;
      }
    }
    for (const s of this.stones.colliders) {
      const d = Math.hypot(p.x - s.x, p.z - s.z);
      const min = s.r + 0.3;
      if (d < min) {
        p.x = s.x + ((p.x - s.x) / (d || 1)) * min;
        p.z = s.z + ((p.z - s.z) / (d || 1)) * min;
      }
    }
    for (const tr of this.scenery.trunks) {
      const d = Math.hypot(p.x - tr.x, p.z - tr.z);
      if (d < 0.6) {
        p.x = tr.x + ((p.x - tr.x) / (d || 1)) * 0.6;
        p.z = tr.z + ((p.z - tr.z) / (d || 1)) * 0.6;
      }
    }
    const fd = Math.hypot(p.x, p.z - CAMP_Z);
    if (fd < 1.6) {
      p.x = (p.x / (fd || 1)) * 1.6;
      p.z = CAMP_Z + ((p.z - CAMP_Z) / (fd || 1)) * 1.6;
    }
    const ground = this.beach.heightAt(p.x, p.z);
    if (this.eyeY === 0) this.eyeY = ground + 1.65;
    this.eyeY += (ground + 1.65 - this.eyeY) * Math.min(1, dt * 12);
    this.feet.set(p.x, ground, p.z);
    this.camera.position.set(p.x, this.eyeY + (this.moving ? Math.sin(this.time * 9) * 0.025 : 0), p.z);
    this.camera.rotation.set(p.pitch, p.yaw, 0);
    this.view.update(dt, this.moving, this.feet, p.yaw, p.pitch);
    // binoculars narrow the field of view
    this.applyToolFov(dt);
    this.leaveFootprints();
  }

  // ---------------------------------------------------------------- the room under the hatch

  private enterUnderground(): void {
    if (this.transitioning || this.alt) return;
    this.transitioning = true;
    if (this.drawing) this.cancelDraw();
    this.setBinocView(false);
    this.ui.fade(() => {
      this.diveReturn = { x: this.player.x, z: this.player.z, yaw: this.player.yaw, pitch: this.player.pitch };
      this.underground = true;
      this.hideLand(true);
      this.spawner.group.visible = false;
      this.cellar.setActive(true);
      this.sky.setUnderground(true);
      this.player.x = this.cellar.entry.x;
      this.player.z = this.cellar.entry.z;
      this.player.yaw = this.cellar.entry.yaw;
      this.player.pitch = 0;
      this.eyeY = UG.y + 1.65;
      this.ladderY = 0;
      this.walkTo = null;
      this.view.setTool(0);
      this.ui.setTool(0);
      this.detector.setActive(false);
      this.dig.setActive(false);
      this.ui.setUnderground(true);
      this.audio.thud();
      this.audio.setOceanLevel(0.006);
      this.discover('underground_first');
      setTimeout(() => (this.transitioning = false), 350);
    });
  }

  private leaveUnderground(): void {
    if (this.transitioning || !this.underground) return;
    this.transitioning = true;
    this.ui.fade(() => {
      const r = this.diveReturn;
      this.underground = false;
      this.cellar.setActive(false);
      this.sky.setUnderground(false);
      this.sky.openAir = 0;
      this.spawner.group.visible = true;
      this.hideLand(false);
      if (r) {
        this.player.x = r.x;
        this.player.z = r.z;
        this.player.yaw = r.yaw;
        this.player.pitch = r.pitch;
      }
      this.diveReturn = null;
      this.eyeY = 0;
      this.lastFoot.set = false;
      this.view.setTool(this.tool);
      this.ui.setTool(this.tool);
      this.detector.setActive(this.tool === 1);
      this.dig.setActive(this.tool === 2);
      this.ui.setUnderground(false);
      this.audio.thud();
      this.audio.setOceanLevel(0.06);
      setTimeout(() => (this.transitioning = false), 350);
    });
  }

  /** Walking about the room: like walking the beach, but with walls, furniture and the stairs. */
  private updateUnderground(dt: number): void {
    const p = this.player;
    this.moving = false;
    if (this.started && this.gameState === 'EXPLORE') {
      const k = this.input.keys;
      const pm = this.pad.move;
      const f = Math.max(-1, Math.min(1, (k.has('KeyW') ? 1 : 0) - (k.has('KeyS') ? 1 : 0) - pm.y));
      const s = Math.max(-1, Math.min(1, (k.has('KeyD') ? 1 : 0) - (k.has('KeyA') ? 1 : 0) + pm.x));
      // facing the ladder, forward and back climb it instead of walking
      const climbing = this.cellar.onLadder(p.x, p.z) && (this.ladderY > 0.02 || (f > 0 && -Math.sin(p.yaw) > 0.3));
      if (climbing) {
        this.ladderY = Math.min(this.cellar.ladderH, Math.max(0, this.ladderY + f * 1.6 * dt));
        if (f) this.moving = true;
      }
      const fw = climbing ? 0 : f;
      if (fw || s) {
        const len = Math.max(1, Math.hypot(fw, s));
        const sp = (k.has('ShiftLeft') ? 5.2 : 3.2) * dt;
        p.x += ((-Math.sin(p.yaw) * fw + Math.cos(p.yaw) * s) / len) * sp;
        p.z += ((-Math.cos(p.yaw) * fw - Math.sin(p.yaw) * s) / len) * sp;
        this.moving = true;
      }
    }
    // up the ladder you cannot step off sideways
    if (this.ladderY > 0.02) {
      p.x = Math.max(p.x, 7.1);
      p.z = Math.min(-3.3, Math.max(-4.7, p.z));
    } else if (!this.cellar.onLadder(p.x, p.z)) {
      this.ladderY = 0;
    }
    for (const c of this.cellar.colliders) {
      const d = Math.hypot(p.x - c.x, p.z - c.z);
      if (d < c.r) {
        p.x = c.x + ((p.x - c.x) / (d || 1)) * c.r;
        p.z = c.z + ((p.z - c.z) / (d || 1)) * c.r;
      }
    }
    const c = this.cellar.clamp(p.x, p.z);
    p.x = c.x;
    p.z = c.z;
    this.cellar.setBeam(this.isMidday);
    // the outside comes in as you go down the stair to the cave
    const air = Math.min(1, Math.max(0, (p.x - 11) / 4));
    this.sky.openAir += (air - this.sky.openAir) * Math.min(1, dt * 3);
    this.cellar.setSide(p.x > 11);
    this.cellar.cave.setDaylight(1 - this.sky.starAmount);
    const ground = UG.y + this.cellar.floorAt(p.x, p.z) + this.ladderY;
    this.eyeY += (ground + 1.65 - this.eyeY) * Math.min(1, dt * 12);
    this.feet.set(p.x, ground, p.z);
    this.camera.position.set(p.x, this.eyeY + (this.moving ? Math.sin(this.time * 9) * 0.025 : 0), p.z);
    this.camera.rotation.set(p.pitch, p.yaw, 0);
    this.view.update(dt, this.moving, this.feet, p.yaw, p.pitch);
    this.applyToolFov(dt);
  }

  // ---------------------------------------------------------------- diving

  /** True where the water is close enough to slip in: the shallow edge of the beach, or the island's coast. */
  private canDive(): boolean {
    if (this.vehicle || this.driving || this.hammock || this.alt || !this.started) return false;
    if (this.onIsland) return this.beach.heightAt(this.player.x, this.player.z) < WATER_LEVEL + 0.8;
    return this.player.z > SHORE_Z - 8;
  }

  /** Adds any sea-floor finds a save does not have yet (a save from before diving, say). */
  private ensureDiveFinds(): void {
    const have = new Map<string, number>();
    for (const i of this.spawner.items) if (DIVE_ITEM_IDS.has(i.def.id)) have.set(i.def.id, (have.get(i.def.id) ?? 0) + 1);
    for (const s of this.under.findSpots()) {
      const n = have.get(s.id) ?? 0;
      if (n > 0) {
        have.set(s.id, n - 1);
        continue;
      }
      const def = DEF_BY_ID.get(s.id);
      if (def) this.spawner.addOpen(def, s.x, s.z);
    }
    this.spawner.checkReveals();
    this.showFindsFor(false);
  }

  /** Land finds are drawn on land and sea-floor finds only underwater. */
  private showFindsFor(underwater: boolean): void {
    for (const it of this.spawner.items) {
      if (it.mesh) it.mesh.visible = this.under.contains(it.x, it.z) === underwater;
    }
  }

  /** Everything on land is hidden while diving, and put back afterwards; the sky and the finds stay. */
  private hideLand(on: boolean): void {
    if (on) {
      const keep = new Set<THREE.Object3D>([this.sky.group, this.camera, this.under.group, this.cellar.group, this.spawner.group]);
      this.hiddenByDive = this.scene.children.filter((o) => o.visible && !keep.has(o));
      for (const o of this.hiddenByDive) o.visible = false;
    } else {
      for (const o of this.hiddenByDive) o.visible = true;
      this.hiddenByDive = [];
    }
    this.showFindsFor(on);
  }

  private applyDiveHands(): void {
    this.view.setSwim(true);
    this.view.setTool(9);
    this.ui.setTool(0);
    this.ui.setBadgeText('\u{1F531}', 'Harpoon');
    this.detector.setActive(false);
    this.dig.setActive(false);
  }

  private enterDive(): void {
    if (this.transitioning || this.alt) return;
    if (!this.state.data.ownsDiveGear) {
      this.audio.error();
      this.ui.toast('You need diving gear to go under. The camper shop sells a mask, snorkel, fins and a harpoon.');
      return;
    }
    this.transitioning = true;
    if (this.drawing) this.cancelDraw();
    this.setBinocView(false);
    this.ui.fade(() => {
      this.diveReturn = { x: this.player.x, z: this.player.z, yaw: this.player.yaw, pitch: this.player.pitch };
      this.diving = true;
      this.hideLand(true);
      this.under.setActive(true);
      this.sky.setUnderwater(true);
      this.diveBg = this.scene.background;
      this.scene.background = new THREE.Color(0x1b7aa6);
      this.player.x = UW_ENTRY.x;
      this.player.z = UW_ENTRY.z;
      this.player.yaw = 0;
      this.player.pitch = -0.25;
      this.diveY = -1.3;
      this.diveVel.set(0, 0, 0);
      this.walkTo = null;
      this.applyDiveHands();
      this.ui.setDiving(true);
      this.audio.splash(2);
      this.audio.setOceanLevel(0.015);
      this.discover('dive_first');
      setTimeout(() => (this.transitioning = false), 350);
    });
  }

  private leaveDive(): void {
    if (this.transitioning || !this.diving) return;
    this.transitioning = true;
    this.ui.fade(() => {
      const r = this.diveReturn;
      this.diving = false;
      this.under.setActive(false);
      this.sky.setUnderwater(false);
      this.scene.background = this.diveBg;
      this.hideLand(false);
      if (r) {
        this.player.x = r.x;
        this.player.z = r.z;
        this.player.yaw = r.yaw;
        this.player.pitch = r.pitch;
      }
      this.diveReturn = null;
      this.lift.up = this.lift.down = false;
      this.diveVel.set(0, 0, 0);
      this.eyeY = 0;
      this.lastFoot.set = false;
      this.view.setSwim(false);
      this.view.setTool(this.tool);
      this.ui.setTool(this.tool);
      this.detector.setActive(this.tool === 1);
      this.dig.setActive(this.tool === 2);
      this.camera.rotation.z = 0;
      this.ui.setDiving(false);
      this.audio.splash(2);
      this.audio.setOceanLevel(0.06);
      setTimeout(() => (this.transitioning = false), 350);
    });
  }

  /** Swimming: move along the way you look, Space rises, C sinks. */
  private updateDive(dt: number): void {
    const p = this.player;
    if (this.started && this.gameState === 'EXPLORE') {
      const k = this.input.keys;
      const pm = this.pad.move;
      const f = Math.max(-1, Math.min(1, (k.has('KeyW') ? 1 : 0) - (k.has('KeyS') ? 1 : 0) - pm.y));
      const s = Math.max(-1, Math.min(1, (k.has('KeyD') ? 1 : 0) - (k.has('KeyA') ? 1 : 0) + pm.x));
      const v = (k.has('Space') || this.lift.up ? 1 : 0) - (k.has('KeyC') || k.has('ControlLeft') || this.lift.down ? 1 : 0);
      const cp = Math.cos(p.pitch);
      const want = new THREE.Vector3(
        -Math.sin(p.yaw) * cp * f + Math.cos(p.yaw) * s,
        Math.sin(p.pitch) * f + v * 0.85,
        -Math.cos(p.yaw) * cp * f - Math.sin(p.yaw) * s,
      );
      if (want.length() > 1) want.normalize();
      want.multiplyScalar(k.has('ShiftLeft') ? 3.8 : 2.4);
      this.diveVel.lerp(want, Math.min(1, dt * 2.5));
    } else {
      this.diveVel.multiplyScalar(Math.max(0, 1 - dt * 3));
    }
    this.moving = this.diveVel.length() > 0.3;
    p.x += this.diveVel.x * dt;
    p.z += this.diveVel.z * dt;
    this.diveY += this.diveVel.y * dt;
    const lim = UW.half - 3;
    p.x = Math.min(UW.x + lim, Math.max(UW.x - lim, p.x));
    p.z = Math.min(UW.z + lim, Math.max(UW.z - lim, p.z));
    const floor = this.under.floorAt(p.x, p.z);
    this.diveY = Math.min(UW_SURFACE - 0.9, Math.max(floor + 0.8, this.diveY));
    // a slow rise and fall, like the kick of the fins
    const bob = Math.sin(this.time * (this.moving ? 1.7 : 0.9)) * (this.moving ? 0.05 : 0.03);
    this.camera.position.set(p.x, this.diveY + bob, p.z);
    this.camera.rotation.set(p.pitch, p.yaw, Math.sin(this.time * 0.7) * 0.012);
    this.feet.set(p.x, this.diveY - 1.6, p.z);
    this.view.update(dt, this.moving, this.feet, p.yaw, p.pitch);
    this.applyToolFov(dt);
    if (Math.hypot(p.x - this.under.wreck.x, p.z - this.under.wreck.z) < 9) this.discover('wreck_found');
  }

  /** Looses a spear along the crosshair. */
  private fireHarpoon(): void {
    this.camera.updateMatrixWorld(true);
    const ray = this.input.ray(this.camera, true);
    const aim = ray.ray.origin.clone().addScaledVector(ray.ray.direction, 12);
    const origin = this.camera.localToWorld(new THREE.Vector3(0.14, -0.12, -0.9));
    if (!this.under.fire(origin, aim.sub(origin))) return;
    this.view.fire();
    this.audio.splash(2);
  }

  private onFishSpeared(reward: number, label: string): void {
    this.state.addCoins(reward);
    this.discover('harpoon_first');
    if (label !== 'fish') this.discover('sea_creature');
    this.audio.coin();
    this.ui.toast(`Speared a ${label}! +${reward}g`);
  }

  /** A print every stride while the player is on foot, pointing the way they are moving. */
  private leaveFootprints(): void {
    const p = this.player;
    const last = this.lastFoot;
    if (!last.set) {
      last.x = p.x;
      last.z = p.z;
      last.set = true;
      return;
    }
    const dx = p.x - last.x;
    const dz = p.z - last.z;
    const d = Math.hypot(dx, dz);
    last.x = p.x;
    last.z = p.z;
    if (d > 2 || d < 1e-4) return; // a teleport or no movement
    this.stepDist += d;
    const stride = this.input.keys.has('ShiftLeft') ? 1.0 : 0.78;
    if (this.stepDist >= stride) {
      this.stepDist = 0;
      this.tracks.step(p.x, p.z, Math.atan2(-dx, -dz));
    }
  }

  // ---------------------------------------------------------------- golf cart

  /** Keeps a round vehicle out of palms, camp props, the fire and the sea. */
  private resolveCart(x: number, z: number, radius: number): { x: number; z: number; hit: boolean } {
    let hit = false;
    const push = (cx: number, cz: number, r: number): void => {
      const d = Math.hypot(x - cx, z - cz);
      if (d < r) {
        x = cx + ((x - cx) / (d || 1)) * r;
        z = cz + ((z - cz) / (d || 1)) * r;
        hit = true;
      }
    };
    for (const t of this.scenery.trunks) push(t.x, t.z, radius + 0.3);
    for (const s of this.stones.colliders) push(s.x, s.z, s.r + radius * 0.8);
    for (const s of this.castles.colliders) push(s.x, s.z, s.r + radius * 0.6);
    for (const o of this.camp.obstacles) push(o.x, o.z, o.r + radius * 0.7);
    push(0, CAMP_Z, 1.6 + radius * 0.6);
    const nx = Math.min(HALF - 2, Math.max(-(HALF - 2), x));
    const nz = Math.min(SHORE_Z - 4, Math.max(-(HALF - 2), z));
    if (nx !== x || nz !== z) hit = true;
    return { x: nx, z: nz, hit };
  }

  private enterVehicle(v: 'cart' | 'boat'): void {
    this.setBinocView(false);
    this.driving = true;
    this.vehicle = v;
    this.lookOffset = 0;
    this.player.pitch = -0.08;
    this.view.setTool(0);
    this.view.proxy.visible = false;
    this.detector.setActive(false);
    this.dig.setActive(false);
    this.ui.setTool(0);
    this.audio.ui();
    this.audio.setEngine(true, 0, v === 'boat');
    this.discover(v === 'boat' ? 'boat_drive' : 'cart_drive');
  }

  private exitVehicle(): void {
    const v = this.vehicle;
    if (v === 'boat') {
      // you can only step out where it is shallow enough to wade to the beach
      if (!this.nearShore()) {
        this.ui.toast('Park the boat closer to the shore to step off, so you can climb back aboard.');
        this.audio.error();
        return;
      }
      const spot = this.landingSpot();
      this.player.x = spot.x;
      this.player.z = spot.z;
    } else {
      // step out on the left side of the cart, clear of its body
      const lx = -Math.cos(this.cart.heading);
      const lz = Math.sin(this.cart.heading);
      this.player.x = this.cart.x + lx * 2.0;
      this.player.z = this.cart.z + lz * 2.0;
      this.cart.speed = 0;
    }
    const heading = v === 'boat' ? this.boat.heading : this.cart.heading;
    this.driving = false;
    this.vehicle = null;
    this.boat.speed = 0;
    this.audio.setEngine(false);
    this.player.yaw = heading + this.lookOffset;
    this.view.proxy.visible = true;
    this.view.setTool(this.tool);
    this.ui.setTool(this.tool);
    this.detector.setActive(this.tool === 1);
    this.dig.setActive(this.tool === 2);
    this.camera.fov = this.baseFov;
    this.camera.updateProjectionMatrix();
    this.audio.ui();
  }

  /** Keeps the boat afloat: it cannot move onto sand, and it slides along the coast instead of stopping dead. */
  private resolveBoat(nx: number, nz: number): { x: number; z: number; hit: boolean } {
    const b = this.boat;
    const level = this.ocean.surfaceY;
    const fx = -Math.sin(b.heading);
    const fz = -Math.cos(b.heading);
    const rx = Math.cos(b.heading);
    const rz = -Math.sin(b.heading);
    /** depth of water under the shallowest point of the hull; at least -0.02 means it floats */
    const clearance = (x: number, z: number): number => {
      if (Math.abs(x) > SEA.x || z > SEA.z) return -Infinity;
      let m = Infinity;
      for (const [al, side] of [[0, 0], [2.3, 0], [-2.3, 0], [0, 0.9], [0, -0.9]] as const) {
        m = Math.min(m, level - this.beach.heightAt(x + fx * al + rx * side, z + fz * al + rz * side));
      }
      return m;
    };
    const floats = (x: number, z: number): boolean => clearance(x, z) >= 0.02;
    if (floats(nx, nz)) return { x: nx, z: nz, hit: false };
    if (floats(nx, b.z)) return { x: nx, z: b.z, hit: true };
    if (floats(b.x, nz)) return { x: b.x, z: nz, hit: true };
    // already aground (turned onto the sand, or pushed in by a wave): any move towards deeper water is allowed,
    // so the boat can always work itself free instead of sticking for good
    const here = clearance(b.x, b.z);
    if (here < 0.02) {
      let best = { x: b.x, z: b.z, c: here };
      const tries: [number, number][] = [[nx, nz], [nx, b.z], [b.x, nz]];
      for (let i = 0; i < 12; i++) tries.push([b.x + Math.cos((i * Math.PI) / 6) * 0.25, b.z + Math.sin((i * Math.PI) / 6) * 0.25]);
      for (const [x, z] of tries) {
        const c = clearance(x, z);
        if (c > best.c + 1e-4) best = { x, z, c };
      }
      return { x: best.x, z: best.z, hit: true };
    }
    return { x: b.x, z: b.z, hit: true };
  }

  private updateBoating(dt: number): void {
    const k = this.input.keys;
    const boat = this.boat;
    if (this.cursorSteer) {
      const px = this.input.pointer.x;
      if (Math.abs(px) > 0.35) this.lookOffset = Math.min(1.5, Math.max(-1.5, this.lookOffset - Math.sign(px) * (Math.abs(px) - 0.35) * 2.2 * dt));
    }
    boat.update(
      dt,
      this.time,
      {
        throttle: (k.has('KeyW') ? 1 : 0) - (k.has('KeyS') ? 1 : 0),
        steer: (k.has('KeyA') ? 1 : 0) - (k.has('KeyD') ? 1 : 0),
        brake: k.has('Space'),
      },
      this.ocean.heightAt(boat.x, boat.z),
      (x, z) => {
        const r = this.resolveBoat(x, z);
        if (r.hit && Math.abs(boat.speed) > 2.5) this.audio.thud();
        return r;
      },
    );
    const p = this.player;
    p.x = boat.x;
    p.z = boat.z;
    p.yaw = boat.heading + this.lookOffset;
    this.camera.position.copy(boat.seat());
    this.camera.rotation.set(p.pitch, p.yaw, 0);
    this.feet.set(boat.x, this.ocean.surfaceY, boat.z);
    this.moving = false;
    const sp = Math.abs(boat.speed) / 14;
    const fov = this.baseFov + sp * 12;
    if (Math.abs(this.camera.fov - fov) > 0.05) {
      this.camera.fov += (fov - this.camera.fov) * Math.min(1, dt * 5);
      this.camera.updateProjectionMatrix();
    }
    this.audio.setEngine(true, sp, true);
  }

  /** Standing on the boat's deck: free to look around and use weapons while it floats along. */
  private updateDeck(dt: number): void {
    const p = this.player;
    const boat = this.boat;
    const k = this.input.keys;
    if (this.started && this.cursorSteer) {
      const { x, y } = this.input.pointer;
      const dz = (v: number, zone: number): number => (Math.abs(v) < zone ? 0 : Math.sign(v) * (Math.abs(v) - zone) / (1 - zone));
      const zk = this.camera.fov / this.baseFov;
      p.yaw -= dz(x, 0.15) * 2.6 * dt * zk * this.settings.sensitivity;
      p.pitch = Math.min(1.45, Math.max(-1.45, p.pitch + dz(y, 0.2) * 1.5 * dt * zk * this.settings.sensitivity));
    }
    // a few steps around the deck, relative to where the player is looking
    const f = Math.max(-1, Math.min(1, (k.has('KeyW') ? 1 : 0) - (k.has('KeyS') ? 1 : 0) - this.pad.move.y));
    const sd = Math.max(-1, Math.min(1, (k.has('KeyD') ? 1 : 0) - (k.has('KeyA') ? 1 : 0) + this.pad.move.x));
    this.moving = f !== 0 || sd !== 0;
    if (this.moving) {
      const yl = p.yaw - boat.heading;
      const len = Math.max(1, Math.hypot(f, sd));
      this.deck.x += (((-Math.sin(yl)) * f + Math.cos(yl) * sd) / len) * 1.5 * dt;
      this.deck.z += (((-Math.cos(yl)) * f + (-Math.sin(yl)) * sd) / len) * 1.5 * dt;
    }
    this.deck.x = Math.min(0.62, Math.max(-0.62, this.deck.x));
    this.deck.z = Math.min(1.75, Math.max(-0.2, this.deck.z));
    boat.group.updateWorldMatrix(true, false);
    const eye = boat.group.localToWorld(new THREE.Vector3(this.deck.x, 1.95, this.deck.z));
    const floor = boat.group.localToWorld(new THREE.Vector3(this.deck.x, 0.46, this.deck.z));
    p.x = floor.x;
    p.z = floor.z;
    this.camera.position.copy(eye);
    this.camera.rotation.set(p.pitch, p.yaw, 0);
    this.feet.copy(floor);
    this.view.update(dt, this.moving, this.feet, p.yaw, p.pitch);
    this.applyToolFov(dt);
  }

  /** Where the player would stand on the beach after stepping off the boat. */
  private landingSpot(): { x: number; z: number; island: boolean } {
    // out by the island, the landing is on its shore
    if (Math.hypot(this.boat.x - ISLAND.x, this.boat.z - ISLAND.z) < ISLAND.R + 20) {
      const il = this.island.landingFrom(this.boat.x, this.boat.z);
      if (il) return { ...il, island: true };
    }
    const shore = this.beach.shoreZ(this.boat.x, this.ocean.surfaceY);
    return { x: Math.min(HALF - 2, Math.max(-(HALF - 2), this.boat.x)), z: Math.min(SHORE_Z - 3, shore - 1.5), island: false };
  }

  /**
   * True only when the boat is parked close enough to the beach that the player can walk back
   * to it afterwards (boarding works from about 12 m), so nobody gets stranded ashore.
   */
  private nearShore(): boolean {
    const l = this.landingSpot();
    return Math.hypot(this.boat.x - l.x, this.boat.z - l.z) <= 9;
  }

  /** Lets go of the wheel and stands up on the deck, free to use a weapon. */
  private leaveHelm(): void {
    this.driving = false;
    if (this.tool === 1 || this.tool === 2 || this.tool === 7 || this.tool === 8) this.tool = 5;
    this.deck = { x: 0, z: 0.5 };
    this.player.yaw = this.boat.heading + this.lookOffset;
    this.view.proxy.visible = true;
    this.view.setTool(this.tool);
    this.ui.setTool(this.tool);
    this.detector.setActive(false);
    this.dig.setActive(false);
    this.camera.fov = this.baseFov;
    this.camera.updateProjectionMatrix();
    this.audio.ui();
  }

  /** Back to the wheel. */
  private takeHelm(): void {
    this.setBinocView(false);
    this.lookOffset = Math.atan2(Math.sin(this.player.yaw - this.boat.heading), Math.cos(this.player.yaw - this.boat.heading));
    this.driving = true;
    this.view.setTool(0);
    this.view.proxy.visible = false;
    this.ui.setTool(0);
    this.audio.ui();
    this.audio.setEngine(true, 0, true);
  }

  private updateDriving(dt: number): void {
    if (this.vehicle === 'boat') {
      this.updateBoating(dt);
      return;
    }
    const k = this.input.keys;
    const cart = this.cart;
    // with the cursor free, holding it near a screen edge glances left or right
    if (!this.locked) {
      const px = this.input.pointer.x;
      if (Math.abs(px) > 0.35) this.lookOffset = Math.min(1.5, Math.max(-1.5, this.lookOffset - Math.sign(px) * (Math.abs(px) - 0.35) * 2.2 * dt));
    }
    cart.update(
      dt,
      {
        throttle: (k.has('KeyW') ? 1 : 0) - (k.has('KeyS') ? 1 : 0),
        steer: (k.has('KeyA') ? 1 : 0) - (k.has('KeyD') ? 1 : 0),
        brake: k.has('Space'),
      },
      (x, z) => {
        const r = this.resolveCart(x, z, 1.1);
        if (r.hit && Math.abs(cart.speed) > 2) this.audio.thud();
        return r;
      },
    );

    const p = this.player;
    p.x = cart.x;
    p.z = cart.z;
    p.yaw = cart.heading + this.lookOffset;
    const seat = cart.seat();
    this.camera.position.copy(seat);
    this.camera.rotation.set(p.pitch, p.yaw, 0);
    this.feet.set(cart.x, seat.y - 1.4, cart.z);
    this.moving = false;

    // speed feel: a wider view, a motor hum and a trail of sand
    const sp = Math.abs(cart.speed) / 10;
    const fov = this.baseFov + sp * 10;
    if (Math.abs(this.camera.fov - fov) > 0.05) {
      this.camera.fov += (fov - this.camera.fov) * Math.min(1, dt * 5);
      this.camera.updateProjectionMatrix();
    }
    this.audio.setEngine(true, sp);
    this.dustTimer -= dt;
    if (this.dustTimer <= 0 && Math.abs(cart.speed) > 2) {
      this.dustTimer = 0.06;
      const bx = cart.x + Math.sin(cart.heading) * 1.2;
      const bz = cart.z + Math.cos(cart.heading) * 1.2;
      for (const side of [-0.8, 0.8]) {
        const sx = bx + Math.cos(cart.heading) * side;
        const sz = bz - Math.sin(cart.heading) * side;
        this.dig.particles.burst(new THREE.Vector3(sx, this.beach.heightAt(sx, sz) + 0.15, sz), 2, 0.9, 0.9, 0.7, new THREE.Color(0xe6ccb2));
      }
    }
    cart.setHeadlights(this.sky.isDark);

    // tyre tracks, four marks every half metre or so
    const lc = this.lastCart;
    if (!lc.set) {
      lc.x = cart.x;
      lc.z = cart.z;
      lc.set = true;
    }
    this.tireDist += Math.hypot(cart.x - lc.x, cart.z - lc.z);
    lc.x = cart.x;
    lc.z = cart.z;
    if (this.tireDist >= 0.55 && Math.abs(cart.speed) > 0.4) {
      this.tireDist = 0;
      const fx = -Math.sin(cart.heading);
      const fz = -Math.cos(cart.heading);
      const rx = Math.cos(cart.heading);
      const rz = -Math.sin(cart.heading);
      for (const [along, steer] of [[-1.1, 0], [1.0, cart.steerAngle * 0.5]] as const) {
        for (const side of [-0.82, 0.82]) {
          this.tracks.tire(cart.x + fx * along + rx * side, cart.z + fz * along + rz * side, cart.heading + steer);
        }
      }
    }
  }

  /** The dog's resting place: a free spot near the fire, facing it. */
  private placeDogHome(): void {
    const spots: [number, number][] = [];
    for (const r of [3.6, 4.4, 5.2]) {
      for (let a = 0; a < 16; a++) spots.push([Math.cos((a / 16) * Math.PI * 2) * r, CAMP_Z + Math.sin((a / 16) * Math.PI * 2) * r]);
    }
    spots.sort((p, q) => Math.hypot(p[0] + 1.2, p[1] - CAMP_Z - 3) - Math.hypot(q[0] + 1.2, q[1] - CAMP_Z - 3));
    const [x, z] = spots.find(([sx, sz]) => this.camp.obstacles.every((o) => Math.hypot(sx - o.x, sz - o.z) > o.r + 0.9)) ?? spots[0];
    this.dog.setHome(x, z, Math.atan2(-(0 - x), -(CAMP_Z - z)));
  }

  /** The player offers a bone: the dog picks a buried coin or piece of trash (never a Lost & Found item) and runs off to it. */
  private giveBone(): void {
    if (this.dog.away) return;
    const buried = this.spawner.items.filter((i) => !i.collected && !i.revealed);
    const cands = buried.filter((i) => i.def.category === 'coin' || i.def.category === 'trash');
    // very rarely the dog's nose leads it to something from the Lost & Found board that is still wanted
    const wanted = new Set(this.state.activeQuestDefIds());
    const lost = buried.filter((i) => i.def.category === 'quest' && wanted.has(i.def.id));
    const special = lost.length > 0 && Math.random() < DOG_LOST_CHANCE;
    if (!cands.length && !special) {
      this.ui.toast('The dog sniffs around the beach, but there is nothing left to find. Keep your bone.');
      this.audio.error();
      return;
    }
    // somewhere not too far from camp, so the trip is a short one
    const near = cands.filter((i) => Math.hypot(i.x, i.z - CAMP_Z) < 70);
    const pool = special ? lost : near.length ? near : cands;
    const item = pool[Math.floor(Math.random() * pool.length)];
    this.state.useBone();
    this.discover('dog_bone');
    if (item.def.category === 'quest') this.discover('dog_lost');
    this.dog.startMission(item);
    this.audio.yip();
    this.ui.toast(special ? 'The dog gulps down the bone, sniffs the air... and bolts off, nose twitching. It has caught a very special scent!' : 'The dog gulps down the bone, sniffs the air... and bolts off!');
  }

  /** Puts the stones down, well away from every buried find, so none can ever be hidden or blocked by one. */
  private scatterStones(): void {
    const clear = this.spawner.items.filter((i) => !i.collected).map((i) => ({ x: i.x, z: i.z, r: 0 }));
    clear.push({ x: 0, z: CAMP_Z, r: 13 }, { x: this.cart.x, z: this.cart.z, r: 3.5 });
    this.stones.scatter(clear);
    this.grass.scatter(clear);
  }

  // ---------------------------------------------------------------- journal

  /** set while the player is lying in the hammock; time runs fast until the next morning */
  private hammock: { startDay: number; t: number } | null = null;
  private hammockSpeed = 1;

  private enterHammock(): void {
    if (this.hammock || this.driving || this.gameState !== 'EXPLORE' || this.transitioning) return;
    this.setBinocView(false);
    if (this.drawing) this.cancelDraw();
    this.walkTo = null;
    this.hammock = { startDay: this.state.data.day, t: 0 };
    this.hammockSpeed = 1;
    this.view.setTool(0);
    this.view.proxy.visible = false;
    this.detector.setActive(false);
    this.dig.setActive(false);
    this.ui.setTool(0);
    const h = this.camp.hammockSpot();
    this.player.x = h.x;
    this.player.z = h.z;
    this.player.yaw = h.yaw;
    this.camp.setHammockOccupied(true);
    this.ui.setSleep(true);
    this.discover('hammock_first');
    this.audio.ui();
  }

  /** Gets up: the tools come back, warmth is full again, and the player stands beside the hammock. */
  private leaveHammock(morning: boolean): void {
    if (!this.hammock) return;
    this.hammock = null;
    this.hammockSpeed = 1;
    this.camp.setHammockOccupied(false);
    this.ui.setSleep(false);
    const h = this.camp.hammockSpot();
    this.player.x = h.exitX;
    this.player.z = h.exitZ;
    this.player.pitch = -0.1;
    this.view.proxy.visible = true;
    this.view.setTool(this.tool);
    this.ui.setTool(this.tool);
    this.detector.setActive(this.tool === 1);
    this.dig.setActive(this.tool === 2);
    this.camera.fov = this.baseFov;
    this.camera.updateProjectionMatrix();
    this.state.restoreWarmth();
    this.audio.ui();
    this.ui.toast(morning ? `Good morning! Day ${this.state.data.day}. You feel warm and rested.` : 'You get up. A bit of rest never hurt.');
  }

  /** The view from the hammock: looking up through the palms, swaying. Time races on until it is morning again. */
  private updateHammock(dt: number): void {
    const hm = this.hammock!;
    hm.t += dt;
    this.hammockSpeed += (26 - this.hammockSpeed) * Math.min(1, dt * 1.5);
    const h = this.camp.hammockSpot();
    const sway = Math.sin(hm.t * 0.9);
    this.moving = false;
    this.camera.position.set(h.x, h.y + 0.5 + Math.sin(hm.t * 1.7) * 0.015, h.z);
    this.camera.rotation.set(0.62 + Math.sin(hm.t * 0.5) * 0.04, h.yaw + Math.sin(hm.t * 0.35) * 0.08, sway * 0.05);
    this.feet.set(h.x, h.y - 0.4, h.z);
    // a new day has begun and the light is up: wake up
    if (this.state.data.day > hm.startDay && this.dayTime > 0.04) this.leaveHammock(true);
  }

  private bookOpen = false;
  /** what the reader overlay is showing: the journal or the post */
  private readerKind: 'book' | 'mail' | 'story' = 'book';

  /** Writes a story into the journal the first time something happens. */
  private discover(id: string): void {
    if (this.started) this.achievements.page(id);
    if (!this.started || !this.state.unlockPage(id)) return;
    // the whole book is open from the start, so there is no announcement; the page just stops saying "to discover"
    if (BOOK_SHOW_ALL) return;
    const page = PAGE_BY_ID.get(id);
    if (page) this.ui.toast(`📖 New journal page: ${page.title}`);
  }

  private openBook(): void {
    if (this.bookOpen || this.gameState !== 'EXPLORE') return;
    this.bookOpen = true;
    this.readerKind = 'book';
    this.paused = true;
    this.input.keys.clear();
    document.exitPointerLock?.();
    this.audio.ui();
    this.audio.setOceanLevel(0.02);
    const unread = new Set(this.state.data.unreadPages ?? []);
    const shown = BOOK_SHOW_ALL ? JOURNAL_PAGES : JOURNAL_PAGES.filter((p) => this.state.data.pages.includes(p.id));
    this.ui.showBook(
      shown.map((p) => ({ ...p, isNew: unread.has(p.id), found: this.state.data.pages.includes(p.id) })),
      () => this.closeBook(),
    );
  }

  /** The post at camp: read the letters the owners have sent. */
  private openMail(): void {
    if (this.bookOpen || this.gameState !== 'EXPLORE') return;
    this.bookOpen = true;
    this.readerKind = 'mail';
    this.paused = true;
    this.input.keys.clear();
    document.exitPointerLock?.();
    this.audio.ui();
    this.audio.setOceanLevel(0.02);
    const unread = new Set(this.state.data.unreadLetters ?? []);
    const got = (this.state.data.letters ?? []).map((id) => LETTER_BY_ID.get(id)).filter((l): l is NonNullable<typeof l> => !!l);
    this.ui.showMail(
      got.map((l) => ({ ...l, isNew: unread.has(l.id) })),
      () => this.closeBook(),
    );
  }

  /** The time around noon, when the sun stands high enough to reach down the shaft in the ceiling. */
  private get isMidday(): boolean {
    return this.dayTime > 0.2 && this.dayTime < 0.34;
  }

  /** The great door takes the medallion as its key, and the astrolabe to catch the noon sun on the lock. */
  private tryGreatDoor(): void {
    if (this.bookOpen || this.gameState !== 'EXPLORE') return;
    this.discover('great_door');
    const made = this.state.data.assembled;
    if (!made.includes('medallion')) {
      this.audio.thud();
      this.ui.toast('It will not move. The lock is round and flat, the size of a large medallion.');
    } else if (!made.includes('astrolabe')) {
      this.audio.thud();
      this.ui.toast('The medallion fits the lock, but it will not turn. Rings are cut around it, like the rings of an old brass astrolabe.');
    } else if (!this.isMidday) {
      this.audio.thud();
      this.ui.toast('The medallion and the astrolabe fit, but the lock stays dark. Your astrolabe needs the sun: come back around midday.');
    } else {
      this.state.data.doorOpen = true;
      this.state.emit();
      this.cellar.openDoor();
      this.audio.discover();
      this.audio.thud();
      this.discover('door_open');
      this.ui.toast('The sun catches the astrolabe, the medallion turns, and the great door opens. Cold sea air comes up the stairs.');
    }
  }

  /** The end of the story: the gold, then a last card, and the game carries on. */
  private takeHoard(): void {
    if (this.bookOpen || this.gameState !== 'EXPLORE' || this.state.data.hoardTaken) return;
    const d = this.state.data;
    d.hoardTaken = true;
    this.state.addCoins(HOARD_VALUE);
    this.cellar.cave.setTaken(true);
    this.discover('hoard_found');
    this.bookOpen = true;
    this.readerKind = 'story';
    this.paused = true;
    this.input.keys.clear();
    document.exitPointerLock?.();
    this.audio.discover();
    this.audio.setEnding(true);
    const finds = Object.values(d.found).reduce((a, b) => a + b, 0);
    this.ui.showEnding(
      [
        [d.day === 1 ? 'day on the beach' : 'days on the beach', String(d.day)],
        ['finds cleaned', String(finds)],
        ['gold found', `${HOARD_VALUE}g`],
      ],
      () => {
        this.audio.setEnding(false);
        this.closeBook();
        this.autoSave();
        this.ui.toast('The camper has something new for you: a Luxury page in its shop.');
      },
    );
  }

  /** Puts everything the player has bought with the gold on show. */
  private applyLuxury(): void {
    const has = (id: string): boolean => this.state.data.luxury.includes(id);
    this.view.setGolden(has('gold_detector'), has('gold_trowel'));
    if (has('gold_cart')) this.cart.setGilded();
    if (has('marigold_boat')) this.boat.setMarigold();
    if (has('monster_skull')) this.camp.showSkull();
    if (has('gold_bone')) this.dog.setGoldenBone();
  }

  private openStory(): void {
    if (this.bookOpen || this.gameState !== 'EXPLORE') return;
    this.bookOpen = true;
    this.readerKind = 'story';
    this.paused = true;
    this.input.keys.clear();
    document.exitPointerLock?.();
    this.audio.ui();
    this.audio.setOceanLevel(0.006);
    this.discover('captain_journal');
    this.ui.showStory('The captain\u2019s journal', CAPTAIN_PAGES, () => this.closeBook());
  }

  /** A letter has been written: it will reach the post after a short while. */
  private sendLetter(noteId: string): void {
    if (!LETTERS.some((l) => l.id === noteId)) return;
    this.state.queueLetter(noteId, 40 + Math.random() * 30);
  }

  private closeBook(): void {
    if (!this.bookOpen) return;
    this.bookOpen = false;
    this.paused = false;
    this.ui.hideBook();
    if (this.readerKind === 'mail') this.state.markLettersRead();
    else if (this.readerKind === 'book') this.state.markPagesRead();
    this.audio.ui();
    this.audio.setOceanLevel(this.underground ? 0.006 : 0.06);
    this.input.keys.clear();
    this.grabMouse();
  }

  /** The radio's second button: tune to the next station, switching the radio on if it was off. */
  private changeSong(): void {
    this.audio.setTrack(this.audio.trackNumber + 1);
    if (!this.audio.radioOn) {
      this.audio.setRadio(true);
      this.camp.setRadio(true);
    }
    this.audio.ui();
    this.discover('radio_change');
    this.ui.toast(`📻 ${this.audio.trackName}  (${this.audio.trackNumber + 1} of ${this.audio.trackCount})`);
  }

  private mapPaintT = 0;
  private mapListT = 0;
  private campLightsNear = true;
  private lastCastleNote = { msg: '', at: 0 };

  /** A short warning from the bucket, never repeated while the player keeps clicking. */
  private castleNote(msg: string): void {
    const now = performance.now();
    if (msg === this.lastCastleNote.msg && now - this.lastCastleNote.at < 4000) return;
    this.lastCastleNote = { msg, at: now };
    this.audio.error();
    this.ui.toast(msg);
  }

  /** The bucket: start a castle on the sand, or add the next stage to the one you are aiming at. Success is shown by the castle itself, not by messages. */
  private buildCastle(): void {
    const at = this.toolHit;
    if (!at) return;
    const h = this.beach.heightAt(at.x, at.z);
    const near = this.castles.nearest(at.x, at.z, 1.7);
    const puff = (): void => this.dig.particles.burst(new THREE.Vector3(at.x, h + 0.15, at.z), 16, 1.2, 1.6, 0.7, new THREE.Color(0xdcc38f));
    if (near) {
      if (!this.castles.upgrade(near)) return; // finished: the hint at the bottom already says so
      puff();
      this.audio.sandBuild();
      if (near.stage >= 3) this.discover('castle_grand');
      return;
    }
    if (h < WATER_LEVEL + 0.35) return this.castleNote('Too close to the water. Build a little higher up the sand.');
    if (Math.hypot(at.x, at.z - CAMP_Z) < 12) return this.castleNote('Too close to camp. Go out onto the open beach.');
    if (this.castles.nearest(at.x, at.z, 2.6)) return this.castleNote('That is too close to another castle.');
    if (this.castles.count >= MAX_CASTLES) return this.castleNote(`You have ${MAX_CASTLES} castles already. Right-click one to knock it down.`);
    this.castles.add(at.x, at.z);
    puff();
    this.audio.sandBuild();
    this.discover('castle_first');
  }

  /** Knocks down the castle you are aiming at. */
  private knockCastle(): void {
    const at = this.toolHit;
    if (!at) return;
    const c = this.castles.nearest(at.x, at.z, 1.7);
    if (!c) return;
    this.dig.particles.burst(new THREE.Vector3(c.x, this.beach.heightAt(c.x, c.z) + 0.3, c.z), 28, 1.8, 1.6, 0.9, new THREE.Color(0xdcc38f));
    this.castles.remove(c);
    this.audio.crumble();
  }

  /** True while the player is on foot on the island. */
  private get onIsland(): boolean {
    return this.started && this.vehicle === null && this.island.contains(this.player.x, this.player.z);
  }

  /**
   * Four sealed chests are buried on the beach from the start. They are invisible to the detector and the trowel until the player
   * takes the old map from the island's altar; the map then shows where each one lies.
   */
  private ensureTreasureCaches(): void {
    const has = this.state.data.hasTreasureMap;
    this.spawner.secretsOpen = has;
    if (has) this.island.takeMap();
    // chests from an earlier version were buried deeper than they needed to be
    for (const i of this.spawner.items) if (i.def.id === 'treasure_cache' && !i.revealed && !i.collected) i.targetDepth = Math.min(i.targetDepth, 0.45);
    if (!this.spawner.items.some((i) => i.def.id === 'treasure_cache')) {
      const def = DEF_BY_ID.get('treasure_cache');
      if (def) {
        // one in each quarter of the open beach, away from the camp and from other finds
        const zones: [number, number, number, number][] = [[-52, -14, -38, -6], [14, 52, -38, -6], [-52, -14, 2, 24], [14, 52, 2, 24]];
        for (const [x0, x1, z0, z1] of zones) {
          for (let tries = 0; tries < 80; tries++) {
            const x = x0 + Math.random() * (x1 - x0);
            const z = z0 + Math.random() * (z1 - z0);
            if (this.spawner.items.some((i) => Math.hypot(i.x - x, i.z - z) < 3)) continue;
            this.spawner.addBuried(def, x, z, 0.3 + Math.random() * 0.15);
            break;
          }
        }
      }
    }
    this.spawner.checkReveals();
  }

  /** True when the player is within a few metres of a buried chest the treasure map marks. */
  private nearCache(): boolean {
    const d = this.state.data;
    return this.spawner.items.some((i) => ((i.def.id === 'treasure_cache' && d.hasTreasureMap) || (i.def.id === 'captain_chest' && d.hasBottleMap)) && !i.collected && !i.revealed && Math.hypot(i.x - this.player.x, i.z - this.player.z) < 7);
  }

  /** The captain's strongbox lies buried on the main beach from the start; only the bottle's map shows where. */
  private ensureCaptainChest(): void {
    this.spawner.bottleOpen = this.state.data.hasBottleMap;
    if (!this.spawner.items.some((i) => i.def.id === 'captain_chest')) {
      const def = DEF_BY_ID.get('captain_chest');
      if (def) {
        // the emptiest spot out of a good many tries; whatever still lies close is moved away just below
        let best = { x: 55, z: -10, clear: -1 };
        for (let tries = 0; tries < 120; tries++) {
          const east = Math.random() < 0.5;
          const x = (east ? 1 : -1) * (50 + Math.random() * 14);
          const z = -32 + Math.random() * 46;
          let clear = Infinity;
          for (const i of this.spawner.items) clear = Math.min(clear, Math.hypot(i.x - x, i.z - z));
          if (clear > best.clear) best = { x, z, clear };
        }
        this.spawner.addBuried(def, best.x, best.z, 0.3 + Math.random() * 0.15);
      }
    }
    // nothing else is ever buried near the hatch: any find that lies close to it is moved out of the way
    const hatch = this.spawner.items.find((i) => i.def.id === 'captain_chest');
    if (hatch) this.spawner.keepClear(hatch.x, hatch.z, 5, hatch);
    this.spawner.checkReveals();
  }

  /** The top of the sun shaft: an old grate in the sand a few steps from the hatch, there to be noticed from the first day. */
  private placeGrate(): void {
    const hatch = this.spawner.items.find((i) => i.def.id === 'captain_chest');
    if (!hatch) return;
    const clear = (x: number, z: number): boolean =>
      Math.abs(x) < HALF - 4 &&
      this.stones.colliders.every((s) => Math.hypot(x - s.x, z - s.z) > s.r + 1.8) &&
      this.scenery.trunks.every((t) => Math.hypot(x - t.x, z - t.z) > 2);
    const spots: [number, number][] = [[-1.85, 4.5], [1.85, 4.5], [-1.85, -4.5], [1.85, -4.5], [4.5, 1.85], [-4.5, 1.85], [4.5, -1.85], [-4.5, -1.85]];
    const [ox, oz] = spots.find(([dx, dz]) => clear(hatch.x + dx, hatch.z + dz)) ?? spots[0];
    const x = hatch.x + ox;
    const z = hatch.z + oz;
    this.grate = { x, z };
    // the stone sits on sand nobody has dug: a hole made under it in an older save is filled in again
    this.tidyGrateGround();
    const g = buildGrate();
    g.position.set(x, this.beach.heightAt(x, z), z);
    this.scene.add(g);
    this.camp.obstacles.push({ x, z, r: 1.3 });
    // nothing lies buried under the stone, or close enough to it that digging it out would mean digging under the stone
    this.spawner.keepClear(x, z, GRATE_CLEAR);
    this.spawner.checkReveals();
  }

  /** Keeps the sand under the grate as it was, with any hole dug beside it easing up to meet the stone. */
  private tidyGrateGround(): void {
    if (this.grate) this.beach.shapePit(this.grate.x, this.grate.z, 0, GRATE_R, GRATE_R + 0.15, GRATE_R + 1.3, 0);
  }

  /** The map from the bottle is now in the map menu. */
  private takeBottleMap(): void {
    // a hole dug there before the map was found does not count: the ground is made smooth again, so the hatch has to be dug for properly
    const hatch = this.spawner.items.find((i) => i.def.id === 'captain_chest');
    if (hatch && !hatch.revealed) {
      this.beach.clearDug(hatch.x, hatch.z, 4);
      this.holes = this.holes.filter(([x, z]) => Math.hypot(x - hatch.x, z - hatch.z) > 4.5);
    }
    this.state.data.hasBottleMap = true;
    this.state.emit();
    this.spawner.bottleOpen = true;
    this.spawner.checkReveals();
    this.discover('bottle_map');
  }

  /** Picks the old map up off the altar. */
  private takeTreasureMap(): void {
    if (this.state.data.hasTreasureMap) return;
    this.state.data.hasTreasureMap = true;
    this.state.emit();
    this.island.takeMap();
    this.spawner.secretsOpen = true;
    this.spawner.checkReveals();
    this.audio.discover();
    this.discover('map_taken');
    this.ui.toast('You take an old map from the altar. Open the map (M) and press T to read it.');
  }

  /** The island's own finds lie in the open from the start. Added once, and again for older saves that never had them. */
  private ensureIslandFinds(): void {
    // a save from before the island moved may hold finds out at the old spot: drop the ones not picked up yet
    this.spawner.items = this.spawner.items.filter((i) => !ISLAND_ITEM_IDS.has(i.def.id) || i.collected || this.island.contains(i.x, i.z));
    const have = new Map<string, number>();
    for (const i of this.spawner.items) if (ISLAND_ITEM_IDS.has(i.def.id)) have.set(i.def.id, (have.get(i.def.id) ?? 0) + 1);
    for (const s of this.island.findSpots()) {
      const n = have.get(s.id) ?? 0;
      if (n > 0) {
        have.set(s.id, n - 1);
        continue;
      }
      const def = DEF_BY_ID.get(s.id);
      if (def) this.spawner.addOpen(def, s.x, s.z);
    }
    this.spawner.checkReveals();
  }


  private petCount = 0;

  /** A bit of love for the dog: it shakes its head and wags, and little hearts float up. */
  private petDog(): void {
    if (this.dog.state !== 'rest') return;
    this.dog.pet();
    this.discover('dog_pet');
    this.audio.yip();
    const at = new THREE.Vector3(this.dog.x, this.beach.heightAt(this.dog.x, this.dog.z) + 0.75, this.dog.z);
    this.hearts.burst(at, 6, 0.4, 0.7, 1.9, new THREE.Color(0xff4d6d));
    const lines = [
      'You scratch behind its ears. The dog shakes its head happily.',
      'Good dog! It leans into your hand and wags its whole body.',
      'The dog licks your hand and gives a happy little shake.',
    ];
    this.ui.toast(lines[this.petCount++ % lines.length]);
    if ((this.state.data.bones ?? 0) <= 0 && this.petCount % 3 === 1) this.ui.toast('A bone from the camper shop would get it fetching.');
  }

  private dogEnv: DogEnv = {
    playerX: 0,
    playerZ: 0,
    resolve: (x, z) => this.resolveCart(x, z, 0.35),
    bark: (d) => this.audio.bark(d),
    dust: (p) => this.dig.particles.burst(p, 7, 0.9, 1.3, 0.55, this.dogDustColor),
    print: (x, z, yaw, left) => this.tracks.paw(x, z, yaw, left),
  };
  private dogDustColor = new THREE.Color(0xe6ccb2);
  private dogMapPos = { x: 0, z: 0 };
  private static readonly CAMP_REACH: Record<CampKind, number> = {
    fire: 3.2, bin: 2.4, board: 2.8, bench: 2.4, lantern: 2.2, camper: 3.6, radio: 2.6, coffee: 2.2, book: 2.6, post: 2.6, hammock: 2.8, sign: 2.6,
  };

  private updateDog(dt: number, t: number): void {
    this.dogEnv.playerX = this.player.x;
    this.dogEnv.playerZ = this.player.z;
    this.dog.update(dt, t, this.dogEnv);
    if (this.dog.state === 'find' && this.dogWas === 'run') {
      this.discover('dog_found');
      this.ui.toast('Woof! The dog has found something. Follow the paw on your map and dig there.');
    }
    if (this.dog.state === 'find' && this.dog.item?.collected) {
      this.dog.recall();
      this.audio.yip();
      this.ui.toast('Good dog! It trots back to camp.');
    }
    this.dogWas = this.dog.state;
    if (this.dog.away) {
      this.dogMapPos.x = this.dog.x;
      this.dogMapPos.z = this.dog.z;
      this.mapView.dog = this.dogMapPos;
    } else {
      this.mapView.dog = null;
    }
  }

  private radioPos = new THREE.Vector3();
  private interaction: { label: string; run: () => void } | null = null;
  private tmpPos = new THREE.Vector3();

  private aimDir = new THREE.Vector3();
  private aimTo = new THREE.Vector3();

  /**
   * How well the crosshair covers something of about `r` metres at this spot: 1 dead centre, up to 2 at the edge of the
   * allowance, null when it is looked away from. Standing right on top of it always counts.
   */
  private aim(x: number, y: number, z: number, r: number): number | null {
    const to = this.aimTo.set(x, y, z).sub(this.camera.position);
    const dist = to.length();
    if (dist < r * 0.6) return 1.5;
    const ang = Math.acos(Math.min(1, Math.max(-1, to.dot(this.aimDir) / dist)));
    const tol = Math.atan2(r, dist) + 0.2;
    return ang > tol ? null : 1 + ang / tol;
  }

  /** Works out what the player is standing close enough to use (the F prompt). */
  private updateInteraction(): void {
    this.view.setDigTier(this.state.tier('digging'));
    this.camera.getWorldDirection(this.aimDir);
    this.interaction = null;
    this.interaction2 = null;
    this.ui.setPrompt2(null);
    if (this.hammock) {
      this.ui.setPrompt(null);
      return;
    }
    if (!this.started || this.gameState !== 'EXPLORE' || this.transitioning) {
      this.ui.setPrompt(null);
      return;
    }
    if (this.underground) {
      if (this.ladderY >= this.cellar.ladderH - 0.15) this.interaction = { label: 'Climb out through the hatch', run: () => this.leaveUnderground() };
      else if (this.cellar.nearDoor(this.player.x, this.player.z)) this.interaction = { label: 'Try the great door', run: () => this.tryGreatDoor() };
      else if (!this.state.data.hoardTaken && this.cellar.nearGold(this.player.x, this.player.z)) this.interaction = { label: 'Take the gold', run: () => this.takeHoard() };
      else if (this.cellar.nearBook(this.player.x, this.player.z)) this.interaction = { label: 'Read the captain\u2019s journal', run: () => this.openStory() };
      this.ui.setPrompt(this.interaction?.label ?? null);
      this.ui.setPrompt2(null);
      return;
    }
    if (this.diving) {
      // F picks up a find within reach; Q always swims back up
      const cp = this.camera.position;
      let near: BuriedItem | null = null;
      let nd = 2.8;
      for (const it of this.spawner.items) {
        if (!it.revealed || it.collected || !this.under.contains(it.x, it.z)) continue;
        const d = Math.hypot(cp.x - it.x, cp.y - this.under.floorAt(it.x, it.z), cp.z - it.z);
        if (d < nd) {
          nd = d;
          near = it;
        }
      }
      if (near) {
        const found = near;
        this.interaction = { label: 'Pick up and clean this find', run: () => this.enterInspect(found) };
      }
      this.ui.setPrompt(this.interaction?.label ?? null);
      this.ui.setPrompt2(null);
      return;
    }
    if (this.vehicle === 'boat') {
      this.interaction = this.driving
        ? { label: 'Leave the wheel and stand on deck', run: () => this.leaveHelm() }
        : { label: 'Take the wheel', run: () => this.takeHelm() };
      // you can only get off where the water is shallow enough to wade to the beach
      if (this.nearShore()) this.interaction2 = { label: this.landingSpot().island ? 'Step off the boat onto the island' : 'Step off the boat onto the beach', run: () => this.exitVehicle() };
      this.ui.setPrompt(this.interaction.label);
      this.ui.setPrompt2(this.interaction2?.label ?? null);
      return;
    }
    if (this.driving) {
      this.interaction = { label: 'Get out of the golf cart', run: () => this.exitVehicle() };
      this.ui.setPrompt(this.interaction.label);
      return;
    }
    let best = Infinity;
    if (Math.hypot(this.player.x, this.player.z - CAMP_Z) < 16) {
      const reach = Game.CAMP_REACH;
      const labels: Record<CampKind, string> = {
        fire: 'Rest by the campfire',
        bin: 'Recycle your trash',
        board: 'Read the Lost & Found board',
        bench: 'Use the workbench',
        lantern: this.lanternOn ? 'Turn the lantern off' : 'Turn the lantern on',
        camper: 'Open the camper (upgrades)',
        radio: this.audio.radioOn ? `Turn the radio off (${this.audio.trackName})` : 'Turn the radio on',
        coffee: 'Drink the coffee (rest and warm up)',
        hammock: 'Lie in the hammock (sleep until morning)',
        sign: 'Change the sign',
        post: (this.state.data.unreadLetters?.length ?? 0) > 0 ? `Check the post (${this.state.data.unreadLetters.length} new)` : 'Check the post',
        book: (this.state.data.unreadPages?.length ?? 0) > 0 ? `Read the journal (${this.state.data.unreadPages.length} new)` : 'Read the journal',
      };
      for (const obj of this.camp.clickables) {
        const kind = obj.userData.kind as CampKind;
        obj.getWorldPosition(this.tmpPos);
        const score = Math.hypot(this.player.x - this.tmpPos.x, this.player.z - this.tmpPos.z) / reach[kind];
        // the radio sits on the workbench, so it wins ties when you stand right next to it
        const aim = score < 1 ? this.aim(this.tmpPos.x, this.tmpPos.y, this.tmpPos.z, Math.max(0.7, reach[kind] * 0.4)) : null;
        const rank = (kind === 'radio' || kind === 'coffee' || kind === 'book' || kind === 'post' ? score * 0.6 : score) * (aim ?? 0);
        if (aim !== null && rank < best) {
          best = rank;
          this.interaction = { label: labels[kind], run: () => this.useCampObject(kind) };
          this.interaction2 = kind === 'radio' ? { label: 'Change the song', run: () => this.changeSong() } : null;
        }
      }
    }
    {
      const score = Math.hypot(this.player.x - this.cart.x, this.player.z - this.cart.z) / 3.6;
      const aim = score < 1 ? this.aim(this.cart.x, this.beach.heightAt(this.cart.x, this.cart.z) + 0.8, this.cart.z, 1.6) : null;
      if (aim !== null && score * 0.8 * aim < best) {
        best = score * 0.8 * aim;
        this.interaction = this.state.data.ownsCartKey
          ? { label: 'Drive the golf cart', run: () => this.enterVehicle('cart') }
          : {
              label: 'Golf cart (needs a key)',
              run: () => {
                this.audio.error();
                this.ui.toast('The golf cart will not start without a key. The camper shop sells one.');
              },
            };
      }
    }
    {
      const score = Math.hypot(this.player.x - this.boat.x, this.player.z - this.boat.z) / 12;
      const aim = score < 1 ? this.aim(this.boat.x, 0.5, this.boat.z, 4) : null;
      if (aim !== null && score * 0.85 * aim < best) {
        best = score * 0.85 * aim;
        this.interaction = this.state.data.ownsBoatKey
          ? { label: 'Drive the boat', run: () => this.enterVehicle('boat') }
          : {
              label: 'Boat (needs a key)',
              run: () => {
                this.audio.error();
                this.ui.toast('The boat will not start without a key. The camper shop sells one.');
              },
            };
      }
    }
    {
      const fi = this.flags.nearest(this.player.x, this.player.z, 2.2);
      if (fi >= 0 && 0.5 < best) {
        best = 0.5;
        this.interaction = {
          label: 'Pick up this flag',
          run: () => {
            this.flags.remove(fi);
            this.state.returnFlag();
            this.audio.ui();
            this.ui.toast(`Flag picked up (${this.state.data.flags} in your bag).`);
          },
        };
      }
    }
    {
      const score = Math.hypot(this.player.x - this.dog.x, this.player.z - this.dog.z) / 2.8;
      const aim = !this.dog.away && score < 1 ? this.aim(this.dog.x, this.beach.heightAt(this.dog.x, this.dog.z) + 0.4, this.dog.z, 0.8) : null;
      if (aim !== null && score * 0.8 * aim < best) {
        best = score * 0.8 * aim;
        if ((this.state.data.bones ?? 0) > 0) this.interaction = { label: 'Give the dog a bone', run: () => this.giveBone() };
        // the second button (E, or X on a controller) is always for a little love
        this.interaction2 = { label: 'Pet the dog', run: () => this.petDog() };
      }
    }
    if (this.grate && Math.hypot(this.player.x - this.grate.x, this.player.z - this.grate.z) < 2.6 && 0.5 < best) {
      best = 0.5;
      this.interaction = {
        label: 'Look through the grate',
        run: () => {
          this.audio.ui();
          this.ui.toast('An old iron grate, set in cut stone. Cold air rises through the bars. It seems there is something underground here.');
        },
      };
    }
    if (!this.state.data.hasTreasureMap && this.island.nearMap(this.player.x, this.player.z) && 0.3 < best) {
      best = 0.3;
      this.interaction = { label: 'Take the old map from the altar', run: () => this.takeTreasureMap() };
    }
    for (const it of this.spawner.items) {
      if (!it.revealed || it.collected) continue;
      const score = Math.hypot(this.player.x - it.x, this.player.z - it.z) / 2.2;
      const aim = score < 1 ? this.aim(it.x, this.beach.heightAt(it.x, it.z), it.z, it.def.id === 'captain_chest' ? 1.5 : 0.6) : null;
      if (aim !== null && score * aim < best) {
        best = score * aim;
        this.interaction = it.def.id === 'captain_chest'
          ? { label: it.opened ? 'Climb down the ladder' : 'Open the hatch', run: () => this.useFind(it) }
          : { label: 'Pick up and clean this find', run: () => this.enterInspect(it) };
      }
    }
    if (!this.interaction && this.canDive()) {
      this.interaction = { label: this.state.data.ownsDiveGear ? 'Dive in' : 'Dive in (needs diving gear)', run: () => this.enterDive() };
    }
    this.ui.setPrompt(this.interaction?.label ?? null);
    this.ui.setPrompt2(this.interaction2?.label ?? null);
  }


  /** with the radio on the music plays everywhere; the tune follows the place the player is in */
  private updateMusic(): void {
    const menu = this.gameState === 'MENU';
    this.audio.setMenu(menu);
    if (menu) {
      this.audio.setMusicLevel(1, 1);
      return;
    }
    if (!this.audio.radioOn) return;
    this.audio.setPlace(this.underground ? 4 : this.diving ? 3 : this.sky.isDark ? 2 : 1);
    this.audio.setMusicLevel(this.paused ? 0.08 : 1, 1);
  }

  private updateHover(): void {
    this.hoveredItem = null;
    this.hoveredCamp = null;
    this.toolHit = null;
    if (!this.started || this.driving || this.gameState === 'INSPECT' || this.campModal.isOpen && this.isOverPanel()) return;
    if (this.underground) return;
    if (this.diving) {
      // underwater only the finds can be looked at
      if (this.gameState !== 'EXPLORE') return;
      const hit = this.input.ray(this.camera, true).intersectObjects(this.spawner.pickables(), true)[0];
      if (hit && hit.distance < 6) this.hoveredItem = this.spawner.itemFromObject(hit.object);
      return;
    }
    const ray = this.input.ray(this.camera, this.gameState === 'EXPLORE');
    const nearCamp = this.gameState !== 'EXPLORE' || Math.hypot(this.player.x, this.player.z - CAMP_Z) < 22;
    const beachHit = this.beach.raycast(ray.ray.origin, ray.ray.direction, 12);
    const beachDist = beachHit ? beachHit.distanceTo(ray.ray.origin) : Infinity;
    const itemHit = this.gameState === 'EXPLORE' ? ray.intersectObjects(this.spawner.pickables(), true)[0] : undefined;
    const campHit = nearCamp ? ray.intersectObjects(this.camp.clickables, true)[0] : undefined;
    if (itemHit && itemHit.distance < 6 && itemHit.distance < (campHit?.distance ?? Infinity)) {
      this.hoveredItem = this.spawner.itemFromObject(itemHit.object);
    } else if (campHit && campHit.distance < 8 && campHit.distance < beachDist + 0.5) {
      this.hoveredCamp = this.camp.kindOf(campHit.object);
    }
    // the detector only sweeps the sand close to the player; the trowel reaches a little less far
    const near = beachHit
      ? this.tool === 2 || this.tool === 7 || this.tool === 8
        ? beachDist < 5.5
        : Math.hypot(beachHit.x - this.player.x, beachHit.z - this.player.z) < DETECT_REACH
      : false;
    if (this.gameState === 'EXPLORE' && !this.hoveredItem && !this.hoveredCamp && near) {
      this.toolHit = beachHit;
    }
    if (this.onIsland && (this.tool === 1 || this.tool === 2)) this.toolHit = null;
  }

  private isOverPanel(): boolean {
    // the camp panel hugs the right edge of the screen
    const px = (this.input.pointer.x + 1) / 2 * window.innerWidth;
    return px > window.innerWidth - Math.min(496, window.innerWidth * 0.94);
  }

  private zeroNDC = new THREE.Vector2(0, 0);
  private mapPlayer = { x: 0, z: 0, yaw: 0 };
  private mapCart = { x: 0, z: 0 };
  private mapBoat = { x: 0, z: 0 };

  /** Distance to whatever is under the binocular reticle. Measured every few frames. */
  private updateRange(): void {
    if (this.rangeTick++ % 6 !== 0) return;
    this.camera.updateMatrixWorld(true);
    this.rayCaster.setFromCamera(this.zeroNDC, this.camera);
    const ray = this.rayCaster.ray;
    let best = Infinity;
    const beachHit = this.beach.raycast(ray.origin, ray.direction);
    if (beachHit) best = beachHit.distanceTo(ray.origin);
    // the sea surface
    if (ray.direction.y < -0.0005) {
      const t = (this.ocean.surfaceY - ray.origin.y) / ray.direction.y;
      if (t > 0 && t < best) best = t;
    }
    const things = [this.camp.group, this.scenery.group, this.boat.group, this.cart.group, this.sea.group, this.birds.group, this.ecology.group, this.treasure.group];
    const hit = this.rayCaster.intersectObjects(things, true).find((h) => h.object.visible && h.distance > 0.5);
    if (hit && hit.distance < best) best = hit.distance;
    this.rangeText = best < 900 ? `${Math.round(best)} m` : '—';
  }

  private updateHint(): void {
    if (this.hammock) {
      this.ui.setHint('Resting... press any key to get up');
      this.ui.setCrosshair(false);
      return;
    }
    if (this.driving) {
      if (this.vehicle === 'boat') {
        this.ui.setHint('W / S throttle and reverse · A / D steer · Space slows · F leaves the wheel (to use a weapon) · E steps off when parked at the beach');
        this.ui.setBadgeText('🚤', 'Boat');
      } else {
        this.ui.setHint('W / S drive and reverse · A / D steer · Space brakes · F gets out');
        this.ui.setBadgeText('🛺', 'Golf cart');
      }
      this.ui.setCrosshair(false);
      return;
    }
    if (this.underground) {
      this.ui.setBinoculars(false, '', '');
      this.ui.setHint(this.gameState === 'INSPECT' ? null : 'Climb the stairs and the ladder, then press F at the top to go back out');
      this.ui.setCrosshair(this.gameState === 'EXPLORE');
      return;
    }
    if (this.diving) {
      this.ui.setBinoculars(false, '', '');
      this.ui.setBadgeText('\u{1F531}', 'Harpoon');
      this.ui.setHint(
        this.gameState === 'INSPECT' ? null
          : this.hoveredItem ? 'Click to pick it up'
            : this.under.reloading ? 'Reloading...'
              : 'Click to fire the harpoon · F picks up a find · Q swims back up · WASD swim · Space up · C down',
      );
      this.ui.setCrosshair(this.gameState === 'EXPLORE');
      return;
    }
    const binoc = this.tool === 5 && this.binocView && this.gameState === 'EXPLORE';
    if (binoc) this.updateRange();
    this.ui.setBinoculars(binoc, `x${(this.baseFov / this.camera.fov).toFixed(0)}`, this.rangeText);
    if (binoc) {
      this.ui.setHint(null);
      this.ui.setCrosshair(false);
      return;
    }
    let hint: string | null = null;
    if (this.hoveredItem) hint = this.hoveredItem.def.id === 'captain_chest' ? (this.hoveredItem.opened ? 'Click to climb down the ladder' : 'Click to open the hatch') : 'Click to take it to the workbench';
    else if (this.hoveredCamp) {
      hint = {
        fire: 'Click: rest at the campfire', bin: 'Click: recycling bin', board: 'Click: Lost & Found board',
        bench: 'Click: workbench (sell & assemble)', lantern: this.lanternOn ? 'Click: turn the lantern off' : 'Click: turn the lantern on', camper: 'Click: camper (upgrades)',
        radio: this.audio.radioOn ? 'Click: turn the radio off' : 'Click: turn the radio on',
        coffee: 'Click: drink the coffee',
        book: 'Click: read the journal',
        post: 'Click: check the post',
        hammock: 'Click: lie in the hammock',
        sign: 'Click: change the sign',
      }[this.hoveredCamp];
    } else if ((this.tool === 1 || this.tool === 2) && this.nearCache() && !this.onIsland) {
      hint = 'A cross on your map is close. Sweep with the detector and dig where the beep is fastest.';
    } else if (this.onIsland && (this.tool === 1 || this.tool === 2)) {
      hint = 'The island is too rocky to dig. Its treasures lie out in the open.';
    } else if (this.gameState === 'EXPLORE' && this.tool === 8) {
      const c = this.toolHit ? this.castles.nearest(this.toolHit.x, this.toolHit.z, 1.6) : null;
      hint = c ? (c.stage >= 3 ? 'A finished castle · right-click knocks it down' : 'Click to add to this castle · right-click knocks it down') : 'Click to start a sandcastle on the sand';
    } else if (this.gameState === 'EXPLORE' && this.tool === 7) {
      hint = `Click to plant a flag (${this.state.data.flags ?? 0} in your bag) · F beside a flag picks it up`;
    } else if (this.gameState === 'EXPLORE' && this.tool === 6) {
      hint = this.drawing ? 'Release to shoot · right-click to let the string down' : 'Hold click to draw the bow, release to shoot';
    } else if (this.gameState === 'EXPLORE' && this.tool === 5) {
      hint = 'Click to raise the binoculars and look through them';
    } else if (this.gameState === 'EXPLORE' && this.tool === 4) {
      hint = 'Click to shoot · crabs scuttle away, so be quick';
    } else if (this.gameState === 'EXPLORE' && this.toolHit && this.tool === 2) {
      if (!this.beach.canDig(this.toolHit.z)) hint = 'Too close to the dunes';
      else if (this.state.data.warmth <= 0) hint = 'Too chilly to dig. Warm up at the campfire';
      else hint = 'Click to dig';
    }
    if (!hint && this.started && this.gameState === 'EXPLORE' && !this.locked && !this.usingPad) {
      hint = this.lockFailed ? 'Move the mouse toward a screen edge to turn · click to use your tool' : 'Click to take control of the mouse';
    }
    if (!hint && this.gameState === 'EXPLORE' && this.tool === 2 && !this.toolHit) hint = 'Look at the sand closer to dig';
    if (!hint && this.gameState === 'EXPLORE' && this.tool === 1 && !this.toolHit) hint = 'Aim at the sand near your feet to sweep';
    this.ui.setHint(this.gameState === 'INSPECT' ? null : hint);
    this.ui.setCrosshair(this.gameState === 'EXPLORE');
  }

  private frame(): void {
    const rawDt = this.clock.getDelta();
    this.pad.poll(Math.min(rawDt, 0.05));
    this.padUpdate(Math.min(rawDt, 0.05));
    if (this.paused) {
      if (this.mapOpen) {
        this.time += Math.min(rawDt, 0.05);
        this.mapPaintT -= rawDt;
        if (this.mapPaintT <= 0) {
          this.mapPaintT = 0.12;
          this.ui.repaintMap();
        }
      }
      this.audio.detectorEnabled = false;
      if (this.vehicle) this.audio.setEngine(false);
      this.renderer.render(this.gameState === 'INSPECT' ? this.station.scene : this.scene, this.gameState === 'INSPECT' ? this.station.camera : this.camera);
      return;
    }
    const dt = Math.min(rawDt, 0.05);
    this.time += dt;
    this.autoT += dt;
    if (this.autoT >= AUTOSAVE_SECONDS) this.autoSave();
    this.achT += dt;
    if (this.achT >= 1) {
      this.achT = 0;
      this.checkAchievements();
    }
    const t = this.time;
    this.input.tick(dt);

    this.updateDayNight(dt);
    this.sky.update(dt);
    if (this.underground) {
      this.ocean.update(t, this.camera.position.x, this.camera.position.z);
      this.cellar.update(t, dt);
    } else if (this.diving) {
      // the land is hidden: only the water around the diver is alive
      this.under.update(dt, t, this.camera.position);
      // the water, and what shows past it, follow the time of day
      (this.scene.background as THREE.Color | null)?.copy(this.sky.fog.color);
      this.under.setDaylight(0.18 + 0.82 * (1 - this.sky.starAmount));
    } else {
      this.ocean.update(t, this.camera.position.x, this.camera.position.z);
      this.beach.updateDetail(this.camera.position.x, this.camera.position.z);
      // past the reach of the camp's lights (with a little give so they do not flicker at the edge)
      const fromCamp = Math.hypot(this.camera.position.x, this.camera.position.z - CAMP_Z);
      if (fromCamp > (this.campLightsNear ? 34 : 30) === this.campLightsNear) {
        this.campLightsNear = !this.campLightsNear;
        this.camp.setLightsNear(this.campLightsNear);
      }
      this.camp.update(dt, this.sky.fireBoost, this.lanternOn, this.sky.starAmount);
      this.ecology.update(dt, this.player.x, this.player.z);
      this.scenery.update(t, this.sky.fog.color, dt, this.camera.position.x, this.camera.position.z);
      this.grass.update(t);
      this.birds.update(dt, t, this.player.x, this.player.z, (d) => this.audio.gull(d));
      this.tracks.update(dt, this.sky.starAmount);
      this.sea.update(dt, t, this.ocean.surfaceY, this.player.x, this.player.z, (d) => this.audio.splash(d));
      this.whitecaps.update(dt, this.ocean.surfaceY, this.player.x, this.player.z, this.sky.starAmount, (x, z) => this.ocean.swellAt(x, z));
      const chest = this.treasure.update(dt, t, this.ocean.surfaceY, this.vehicle === 'boat' ? { x: this.boat.x, z: this.boat.z } : null);
      if (chest) {
        this.state.addCoins(CHEST_REWARD);
        this.discover('chest_first');
        this.sea.splash(new THREE.Vector3(chest.x, this.ocean.surfaceY, chest.z), 40);
        this.audio.discover();
        this.audio.splash(3);
        const left = this.treasure.remaining;
        this.ui.toast(left > 0 ? `Treasure chest hauled aboard! +${CHEST_REWARD}g. ${left} more out at sea.` : `The last treasure chest! +${CHEST_REWARD}g. You found them all.`);
      }
      // when nobody is driving, the boat just bobs gently at its mooring
      if (!(this.vehicle === 'boat' && this.driving)) {
        this.boat.update(dt, t, null, this.ocean.heightAt(this.boat.x, this.boat.z), (x, z) => (this.vehicle === 'boat' ? this.resolveBoat(x, z) : { x, z, hit: false }));
        if (this.vehicle === 'boat') {
          const sp = Math.abs(this.boat.speed) / 14;
          this.audio.setEngine(sp > 0.03, sp, true);
        }
      }
    }

    this.camp.setBookNew((this.state.data.unreadPages?.length ?? 0) > 0);
    this.camp.setMailNew((this.state.data.unreadLetters?.length ?? 0) > 0);
    if (this.started && this.gameState !== 'MENU') {
      const arrived = this.state.tickMail(dt * (this.hammock ? this.hammockSpeed : 1));
      for (const id of arrived) {
        const from = LETTER_BY_ID.get(id)?.from ?? 'someone';
        this.discover('mail_first');
        this.audio.discover();
        this.ui.toast(`📬 A letter has arrived at the camp post, from ${from}`);
      }
    }
    if (!this.alt) this.updateDog(dt, t);
    if (this.gameState !== 'EXPLORE') this.ui.setPrompt(null);
    this.audio.detectorEnabled = false;
    if (this.gameState === 'INSPECT') {
      const k = this.input.keys;
      const yaw = ((k.has('KeyE') || k.has('ArrowRight') ? 1 : 0) - (k.has('KeyQ') || k.has('ArrowLeft') ? 1 : 0)) * dt * 2.2;
      const pitch = ((k.has('ArrowDown') ? 1 : 0) - (k.has('ArrowUp') ? 1 : 0)) * dt * 1.8;
      if (yaw || pitch) this.station.rotate(yaw, pitch);
      const scrubbing = (this.input.buttons & 1) === 1;
      const painted = this.station.pointer(this.input.pointer, scrubbing && this.input.speed > 15, this.input.speed);
      if (painted) this.audio.brush(this.input.speed);
      this.station.update(dt);
      this.renderer.render(this.station.scene, this.station.camera);
      return;
    }

    this.audio.detectorEnabled = this.gameState === 'EXPLORE' && this.tool === 1 && !this.driving && !this.alt;
    if (this.gameState === 'MENU') this.updateMenuCamera(dt);
    else this.updateCamera(dt);
    this.updateMusic();
    this.updateInteraction();
    this.updateHover();

    if (this.gameState === 'EXPLORE') {
      if (this.driving || this.alt) {
        /* hands are on the wheel, or on the harpoon */
      } else if (this.tool === 1) {
        this.detector.update(dt, this.toolHit, this.state, this.spawner, this.audio, this.beach);
        this.view.setSignal(this.detector.signal.strength, this.detector.signal.kind);
        this.detector.drawWaveform(this.ui.waveCtx, 240, 84, t, this.state.tier('detector'));
      } else if (this.tool === 2) {
        this.dig.hover(this.toolHit, this.state, !!this.toolHit && this.beach.canDig(this.toolHit.z) && this.state.data.warmth > 0, this.beach);
      }
    }
    this.dig.update(dt);
    this.hearts.update(dt);
    const targets = !this.alt && (this.arrows.busy || this.bullets.busy) ? [...this.ecology.crabTargets(), ...this.birds.targets(), ...this.sea.targets(), ...this.scenery.targets(), ...this.castles.targets()] : NO_TARGETS;
    if (!this.alt) this.arrows.update(dt, targets, this.beach, this.scenery.trunks, {
      onHit: (o, at) => this.onBulletHit(o, at, 0.2),
      onStick: () => this.audio.thud(),
    });
    if (!this.alt) this.bullets.update(dt, targets, this.beach, {
      onHit: (o, at) => this.onBulletHit(o, at, 0.14),
      onGround: (p) => this.dig.particles.burst(p.setY(p.y + 0.05), 10, 1, 1.6, 0.5, new THREE.Color(0xe6ccb2)),
    });
    this.spawner.update(dt, t, this.hoveredItem);
    if (!this.alt) {
      this.flags.update(t);
      // the buried chests on the maps barely change, so a couple of looks a second is plenty
      this.mapListT -= dt;
      if (this.mapListT <= 0) {
        this.mapListT = 0.5;
        this.mapView.captain = this.state.data.hasBottleMap ? this.spawner.items.filter((i) => i.def.id === 'captain_chest' && !i.collected).map((i) => [i.x, i.z] as [number, number]) : [];
        this.mapView.treasures = this.state.data.hasTreasureMap ? this.spawner.items.filter((i) => i.def.id === 'treasure_cache' && !i.collected).map((i) => [i.x, i.z] as [number, number]) : [];
      }
      this.island.update(t, this.ocean.surfaceY, this.sky.fog.color, this.sky.fog.density, this.camera.position.x, this.camera.position.z, dt);
      this.castles.update(dt, this.rain.amount, (x, z, byRain) => {
        this.dig.particles.burst(new THREE.Vector3(x, this.beach.heightAt(x, z) + 0.2, z), 24, 1.6, 1.4, 0.9, new THREE.Color(0xdcc38f));
        this.audio.crumble();
        this.ui.toast(byRain ? 'The rain has washed away one of your sandcastles.' : 'The sea has taken one of your sandcastles.');
        this.discover('castle_washed');
      });
    }
    if (this.waypoint) {
      (this.beam.material as THREE.MeshBasicMaterial).opacity = 0.3 + Math.sin(t * 3) * 0.1;
      if (this.gameState === 'EXPLORE' && Math.hypot(this.waypoint.x - this.player.x, this.waypoint.z - this.player.z) < 3.5) {
        this.setWaypoint(null);
        this.audio.discover();
        this.ui.toast('You have reached your destination.');
      }
    }
    if (!this.alt) {
      this.mapPlayer.x = this.player.x;
      this.mapPlayer.z = this.player.z;
      this.mapPlayer.yaw = this.player.yaw;
      this.mapCart.x = this.cart.x;
      this.mapCart.z = this.cart.z;
      this.mapBoat.x = this.boat.x;
      this.mapBoat.z = this.boat.z;
      this.mapView.drawMini(this.ui.miniCtx, 150, this.mapPlayer, this.holes, this.waypoint, this.mapCart, this.mapBoat);
    }
    this.updateHint();
    this.canvas.classList.toggle('pointer', !!(this.hoveredItem || this.hoveredCamp));
    if (this.audio.ready) {
      const fd = Math.hypot(this.camera.position.x - this.camp.group.position.x, this.camera.position.z - this.camp.group.position.z);
      const near = Math.max(0, 1 - fd / 12);
      if (near > 0 && Math.random() < dt * (1.5 + near * 4)) this.audio.crackle(near);
    }
    this.renderer.render(this.scene, this.camera);
  }
}
