import * as THREE from 'three';
import { DEF_BY_ID, ITEM_DEFS, SHARD_SETS } from '../core/items';
import type { ItemDef } from '../core/items';
import type { StateManager } from '../core/StateManager';
import { buildDirty, buildItem } from '../mechanics/LootGenerator';
import { Beach, DIG_MIN_Z, HALF, SHORE_Z, WET_Z } from './Beach';

export interface BuriedItem {
  id: number;
  def: ItemDef;
  x: number;
  z: number;
  targetDepth: number;
  revealed: boolean;
  collected: boolean;
  mesh?: THREE.Group;
  /** persistent brush progress so leaving the workbench never loses it */
  crustCanvas?: HTMLCanvasElement;
  popT: number;
  /** the captain's hatch has been lifted, showing the strongbox */
  opened?: boolean;
}

const rand = (a: number, b: number): number => a + Math.random() * (b - a);

export class LootSpawner {
  readonly group = new THREE.Group();
  items: BuriedItem[] = [];
  private nextId = 1;
  /** circles nothing may be buried in: under the grate, and around the hatch */
  private keepOut: { x: number; z: number; r: number; except?: BuriedItem }[] = [];
  /** the buried chests of the treasure map only exist for someone who holds the map */
  secretsOpen = false;
  /** the captain's strongbox only exists for someone who holds the map from the bottle */
  bottleOpen = false;
  /** how high the ground is at a point */
  ground: (x: number, z: number) => number = (x, z) => this.beach.heightAt(x, z);

  constructor(private beach: Beach, private state: StateManager) {}

  private locked(it: BuriedItem): boolean {
    return (it.def.id === 'treasure_cache' && !this.secretsOpen) || (it.def.id === 'captain_chest' && !this.bottleOpen);
  }

  /** Buries every find on the beach once, at the start. Nothing is ever re-rolled. */
  populate(): void {
    this.items = [];
    const picks: ItemDef[] = [];
    const common = ITEM_DEFS.filter((d) => d.weight > 0);
    const total = common.reduce((s, d) => s + d.weight, 0);
    const roll = (): ItemDef => {
      let r = Math.random() * total;
      for (const d of common) {
        r -= d.weight;
        if (r <= 0) return d;
      }
      return common[0];
    };
    for (let i = 0; i < 330; i++) picks.push(roll());

    // every quest item still wanted, and every shard of every unfinished set, so nothing is unfindable
    for (const id of this.state.activeQuestDefIds()) {
      const def = DEF_BY_ID.get(id);
      if (def) picks.push(def);
    }
    for (const set of SHARD_SETS) {
      if (this.state.data.assembled.includes(set.id)) continue;
      for (let i = 0; i < 3; i++) {
        if (!this.state.hasShard(set.id, i)) picks.push(DEF_BY_ID.get(`${set.id}_${i + 1}`)!);
      }
    }
    // a few extra treasures for the patient
    for (const id of ['doubloon', 'doubloon', 'sovereign', 'denarius', 'silver_dollar', 'ruby_band', 'signet']) picks.push(DEF_BY_ID.get(id)!);

    const placed: { x: number; z: number }[] = this.items.map((i) => ({ x: i.x, z: i.z }));
    for (const def of picks) {
      let x = 0;
      let z = 0;
      for (let tries = 0; tries < 30; tries++) {
        x = rand(-(HALF - 4), HALF - 4);
        if (def.zone === 'wet') z = rand(WET_Z + 0.5, SHORE_Z - 3);
        else if (def.zone === 'dry') z = rand(DIG_MIN_Z + 1.5, 6);
        else z = rand(DIG_MIN_Z + 1, SHORE_Z - 3);
        if (placed.every((p) => Math.hypot(p.x - x, p.z - z) > 2.2)) break;
      }
      placed.push({ x, z });
      this.items.push({
        id: this.nextId++,
        def,
        x,
        z,
        targetDepth: rand(def.depth[0], def.depth[1]),
        revealed: false,
        collected: false,
        popT: 0,
      });
    }
  }

  /** Buries a find at a chosen spot. */
  addBuried(def: ItemDef, x: number, z: number, depth: number): void {
    const it: BuriedItem = { id: this.nextId++, def, x, z, targetDepth: depth, revealed: false, collected: false, popT: 0 };
    this.items.push(it);
    if (this.inKeepOut(it.x, it.z, it)) this.rebury(it);
  }

  /**
   * Keeps every find out of a circle, now and from then on: whatever lies there, even one already
   * dug up but not taken, is buried again a little way off. Nothing can end up stuck under the
   * grate or beside the hatch, where it could never be dug out and the story could not be finished.
   */
  keepClear(x: number, z: number, r: number, except?: BuriedItem): void {
    this.keepOut.push({ x, z, r, except });
    for (const it of this.items) {
      if (it.collected || it.targetDepth < 0 || !this.inKeepOut(it.x, it.z, it)) continue;
      this.rebury(it);
    }
  }

  private inKeepOut(x: number, z: number, it: BuriedItem): boolean {
    return this.keepOut.some((k) => k.except !== it && Math.hypot(x - k.x, z - k.z) < k.r);
  }

  /** Moves a find to the nearest open spot on the diggable beach, outside every kept-clear circle. */
  private rebury(it: BuriedItem): void {
    this.disposeMesh(it);
    it.revealed = false;
    it.popT = 0;
    const zMin = it.def.zone === 'wet' ? WET_Z + 0.5 : DIG_MIN_Z + 1;
    const zMax = it.def.zone === 'dry' ? 6 : SHORE_Z - 3;
    const free = (x: number, z: number, gap: number): boolean =>
      Math.abs(x) < HALF - 4 && z > zMin && z < zMax && !this.inKeepOut(x, z, it) &&
      this.items.every((o) => o === it || o.collected || Math.hypot(o.x - x, o.z - z) > gap);
    // widening rings around where it was; if the beach there is crowded, settle for less room between finds
    for (const gap of [1.5, 0.6, 0]) {
      for (let ring = 1; ring <= 60; ring++) {
        const rad = ring * 0.5;
        const n = 8 + ring * 2;
        const off = Math.random() * Math.PI * 2;
        for (let k = 0; k < n; k++) {
          const a = off + (k / n) * Math.PI * 2;
          const x = it.x + Math.cos(a) * rad;
          const z = it.z + Math.sin(a) * rad;
          if (!free(x, z, gap)) continue;
          it.x = x;
          it.z = z;
          return;
        }
      }
    }
  }

  /** Adds a find that lies out in the open from the start, like the ones on the island. */
  addOpen(def: ItemDef, x: number, z: number): void {
    this.items.push({ id: this.nextId++, def, x, z, targetDepth: -1, revealed: false, collected: false, popT: 0 });
  }

  exportItems(): { defId: string; x: number; z: number; targetDepth: number; collected: boolean }[] {
    return this.items.map((i) => ({ defId: i.def.id, x: i.x, z: i.z, targetDepth: i.targetDepth, collected: i.collected }));
  }

  /** Replaces every find with a saved layout; call checkReveals() after the holes are restored. */
  importItems(list: { defId: string; x: number; z: number; targetDepth: number; collected: boolean }[]): void {
    for (const it of this.items) this.disposeMesh(it);
    this.items = [];
    for (const s of list) {
      const def = DEF_BY_ID.get(s.defId);
      if (!def) continue;
      this.items.push({
        id: this.nextId++, def, x: s.x, z: s.z, targetDepth: s.targetDepth, revealed: false, collected: s.collected, popT: 0,
      });
    }
  }

  private disposeMesh(it: BuriedItem): void {
    if (it.mesh) {
      this.group.remove(it.mesh);
      it.mesh = undefined;
    }
  }

  /** Closest still-buried item within `radius` of x,z. */
  nearest(x: number, z: number, radius: number): { item: BuriedItem; d: number } | null {
    let best: { item: BuriedItem; d: number } | null = null;
    for (const it of this.items) {
      if (it.revealed || it.collected) continue;
      if (this.locked(it)) continue;
      const d = Math.hypot(it.x - x, it.z - z);
      if (d < radius && (!best || d < best.d)) best = { item: it, d };
    }
    return best;
  }

  /** Reveals any item whose hole is now deep enough. Returns the newly revealed. */
  checkReveals(): BuriedItem[] {
    const fresh: BuriedItem[] = [];
    for (const it of this.items) {
      // once found, the hatch always sits at the bottom of a neat pit, however the sand around it was dug
      if (it.revealed && !it.collected && it.def.id === 'captain_chest') this.tidyHatchPit(it);
      if (it.revealed || it.collected) continue;
      if (this.locked(it)) continue;
      // a buried chest is wide, so a hole dug next to the cross turns it up too, not only one dug exactly on top of it
      const wide = it.def.id === 'treasure_cache' || it.def.id === 'captain_chest';
      let depth = this.beach.depthAt(it.x, it.z);
      if (wide) for (const [dx, dz] of [[0.45, 0], [-0.45, 0], [0, 0.45], [0, -0.45]]) depth = Math.max(depth, this.beach.depthAt(it.x + dx, it.z + dz) + 0.1);
      if (depth >= it.targetDepth) {
        it.revealed = true;
        it.popT = 0;
        if (it.def.id === 'captain_chest') this.tidyHatchPit(it);
        // the strongbox is buried under a wooden hatch; everything else shows as sand-covered
        const mesh = it.def.id === 'captain_chest' ? buildItem('captain_hatch') : buildDirty(it.def.id);
        const proxy = new THREE.Mesh(new THREE.SphereGeometry(0.65, 8, 6), new THREE.MeshBasicMaterial({ visible: false }));
        mesh.add(proxy);
        mesh.position.set(it.x, this.ground(it.x, it.z) + 0.05, it.z);
        mesh.rotation.y = Math.random() * Math.PI * 2;
        mesh.userData.item = it;
        it.mesh = mesh;
        this.group.add(mesh);
        fresh.push(it);
      }
    }
    return fresh;
  }

  /** Digs the sand around the hatch into a clean round pit with a flat floor, so the frame lies flush on it and no wall cuts through it. */
  private tidyHatchPit(it: BuriedItem): void {
    this.beach.shapePit(it.x, it.z, Math.max(0.38, it.targetDepth + 0.05), 0.95, 1.9, 2.7);
  }

  /** Lifts the captain's hatch: the closed door is swapped for the open one with the strongbox inside. */
  openHatch(it: BuriedItem): void {
    const old = it.mesh;
    if (!old || it.opened) return;
    const mesh = buildItem('captain_hatch_open');
    const proxy = new THREE.Mesh(new THREE.SphereGeometry(0.75, 8, 6), new THREE.MeshBasicMaterial({ visible: false }));
    mesh.add(proxy);
    mesh.position.copy(old.position);
    mesh.rotation.copy(old.rotation);
    mesh.userData.item = it;
    this.group.remove(old);
    it.mesh = mesh;
    it.opened = true;
    this.group.add(mesh);
  }

  /** Meshes the player can click on right now. */
  pickables(): THREE.Object3D[] {
    return this.items.filter((i) => i.revealed && !i.collected && i.mesh).map((i) => i.mesh!);
  }

  itemFromObject(obj: THREE.Object3D | null): BuriedItem | null {
    let o: THREE.Object3D | null = obj;
    while (o) {
      if (o.userData.item) return o.userData.item as BuriedItem;
      o = o.parent;
    }
    return null;
  }

  markCollected(it: BuriedItem): void {
    it.collected = true;
    this.disposeMesh(it);
  }

  update(dt: number, t: number, hovered: BuriedItem | null): void {
    for (const it of this.items) {
      if (!it.mesh) continue;
      it.popT = Math.min(1, it.popT + dt * 4);
      const pop = 0.5 + 0.5 * (1 - Math.pow(1 - it.popT, 3));
      // the hatch is part of the ground, so it does not swell when looked at
      const lit = it === hovered && it.def.id !== 'captain_chest';
      const hover = lit ? 1.15 + Math.sin(t * 6) * 0.04 : 1;
      it.mesh.scale.setScalar(pop * hover);
      it.mesh.position.y = this.ground(it.x, it.z) + 0.05 + (lit ? 0.04 : 0);
    }
  }
}
