import { DEF_BY_ID, ITEM_DEFS, QUEST_DEFS, SHARD_SETS } from './items';
import { BOOK_SHOW_ALL, INITIAL_PAGE_IDS } from './Journal';
import type { Category, QuestDef } from './items';

export type LightingMode = 'cycle' | 'golden' | 'day' | 'sunset' | 'night';
export type TierKey = 'detector' | 'digging' | 'cleaning' | 'gear';

export interface InventoryItem {
  uid: number;
  defId: string;
  name: string;
  category: Category;
  value: number;
  lore: string;
}

export interface FoundResult {
  /** the backpack entry, or null for shards and Lost & Found items */
  item: InventoryItem | null;
  /** set when the find belongs to a Lost & Found note */
  quest?: { id: string; giver: string; reward: number; progress: number; need: number };
}

export interface GameData {
  coins: number;
  warmth: number;
  inventory: InventoryItem[];
  shards: Record<string, number[]>;
  assembled: string[];
  tiers: Record<TierKey, number>;
  ecology: number;
  questsDone: string[];
  ownsGun: boolean;
  ownsCartKey: boolean;
  ownsBoatKey: boolean;
  ownsBow: boolean;
  /** bought marker flags at least once, so the flag tool is in the hands */
  ownsFlags: boolean;
  /** bought the bucket and spade, so the sandcastle tool is in the hands */
  ownsBucket: boolean;
  /** picked up the old treasure map from the island's altar */
  hasTreasureMap: boolean;
  /** found the message in a bottle, which holds a second map */
  hasBottleMap: boolean;
  /** bought the mask, snorkel, fins and harpoon, so the player can dive */
  ownsDiveGear: boolean;
  /** camp decorations bought so far (ids from DECOR) */
  decor: string[];
  /** which of SIGN_TEXTS the camp sign shows */
  signText: number;
  /** flags in the bag, ready to plant */
  flags: number;
  /** dog bones in the bag; without one the dog will not fetch */
  bones: number;
  /** journal pages written so far */
  pages: string[];
  /** pages written since the book was last opened */
  unreadPages: string[];
  /** thank-you letters on their way: id of the note they answer, and seconds until they arrive */
  pendingLetters: { id: string; in: number }[];
  /** letters that have arrived at the camp post */
  letters: string[];
  unreadLetters: string[];
  /** bones bought so far this game; the shop only has BONE_STOCK to sell */
  bonesBought: number;
  /** which day of the adventure it is */
  day: number;
  questCounts: Record<string, number>;
  lighting: LightingMode;
  found: Record<string, number>;
  nextUid: number;
}

export const TIER_COSTS = [0, 250, 800];
export const GUN_COST = 150;
export const CART_KEY_COST = 300;
export const BOAT_KEY_COST = 350;
export const BOW_COST = 220;
export const BONE_COST = 8;
export const BUCKET_COST = 40;
export const DIVE_COST = 1500;

/** Things to make the camp feel like home. Each is bought once and appears at a fixed place in the camp. */
export const DECOR: { id: string; name: string; cost: number; blurb: string }[] = [
  { id: 'rug', name: 'Woven Rug', cost: 450, blurb: 'A thick striped rug to lay out in front of the fire. Soft underfoot, and it ties the whole camp together.' },
  { id: 'sign', name: 'Camp Sign', cost: 300, blurb: 'A painted wooden sign by the path. Click it to change what it says.' },
  { id: 'lights', name: 'String Lights', cost: 700, blurb: 'A strand of little bulbs strung between two poles. They glow warmly once the sun goes down.' },
  { id: 'cooler', name: 'Cooler Box', cost: 150, blurb: 'A red and white cooler by the workbench. Nobody knows what is in it, and nobody checks.' },
  { id: 'palms', name: 'Potted Palms', cost: 250, blurb: 'Two little palms in clay pots, one at each end of the camp.' },
  { id: 'umbrella', name: 'Beach Umbrella', cost: 350, blurb: 'A striped parasol with a folding lounger underneath it, for doing absolutely nothing.' },
  { id: 'surf', name: 'Surfboard Rack', cost: 400, blurb: 'A driftwood rack holding three colourful boards. You do not surf. That is not the point.' },
  { id: 'tiki', name: 'Tiki Torches', cost: 500, blurb: 'Two bamboo torches with dancing flames to light the way up from the water.' },
];

export const SIGN_TEXTS = ['Cozy Camp', 'Gone Treasure Hunting', 'Treasure Hunter HQ', 'Sandy Toes Welcome', 'Dig Here (Maybe)', 'Home Sweet Dune'];
/** how many bones the shop will ever sell in one game, so the dog cannot be spammed */
export const BONE_STOCK = 8;
export const FLAG_PACK_COST = 15;
export const FLAG_PACK_SIZE = 5;
export const DETECTOR_RADIUS = [2.0, 3.8, 5.5];
export const DIG_RADIUS = [0.7, 1.2, 1.8];
export const DIG_STEP = [0.12, 0.25, 0.45];
export const DIG_COST = [6, 4, 2];
export const BRUSH_PX = [16, 30, 45];
export const MAX_WARMTH = [100, 180, 300];

export const TIER_INFO: Record<TierKey, { label: string; names: string[]; desc: string[] }> = {
  detector: {
    label: 'Detector',
    names: ['Rusty Wand', 'Dual-Frequency Coil', 'Ground Penetrating Radar'],
    desc: [
      'Small scan radius (2.0m). Same beep for trash and treasure.',
      'Radius 3.8m. HUD waveform tells trash from gold.',
      'Radius 5.5m. Glowing heatmap projected onto the sand.',
    ],
  },
  digging: {
    label: 'Digging',
    names: ['Plastic Scoop', 'Trench Trowel', 'Heavy Sand Sifter'],
    desc: [
      'Radius 0.7m, 0.12m per click, costs 6 Warmth.',
      'Radius 1.2m, 0.25m per click, costs 4 Warmth.',
      'Radius 1.8m, unearths items in 1–2 clicks, costs 2 Warmth.',
    ],
  },
  cleaning: {
    label: 'Cleaning',
    names: ['Old Toothbrush', 'Horsehair & Brass Brush', 'Camp Ultrasonic Bath'],
    desc: [
      'Small brush (16px).',
      'Wide brush (30px). Cleaned items sell for +25%.',
      'Widest brush (45px) plus an Instant Clean button for common items.',
    ],
  },
  gear: {
    label: 'Thermos & Gear',
    names: ['Tin Mug (Tea)', 'Insulated Thermos (Spiced Chai)', 'Brass Espresso Flask & Lantern'],
    desc: [
      '100 Max Warmth. Wet sand drains 1.5x Warmth.',
      '180 Max Warmth. Waterproof boots negate the wet sand penalty.',
      '300 Max Warmth. Unlocks the lantern: freeze the sky at any time of day, or let the day and night cycle run.',
    ],
  },
};

const defaultData = (): GameData => ({
  coins: 0,
  warmth: 100,
  inventory: [],
  shards: {},
  assembled: [],
  tiers: { detector: 1, digging: 1, cleaning: 1, gear: 1 },
  ecology: 0,
  questsDone: [],
  ownsGun: false,
  ownsCartKey: false,
  ownsBoatKey: false,
  ownsBow: false,
  ownsFlags: false,
  ownsBucket: false,
  hasTreasureMap: false,
  hasBottleMap: false,
  ownsDiveGear: false,
  decor: [],
  signText: 0,
  flags: 0,
  bones: 0,
  pages: [...INITIAL_PAGE_IDS],
  unreadPages: [],
  pendingLetters: [],
  letters: [],
  unreadLetters: [],
  bonesBought: 0,
  day: 1,
  questCounts: {},
  lighting: 'cycle',
  found: {},
  nextUid: 1,
});

export class StateManager {
  data: GameData = defaultData();
  private listeners = new Set<() => void>();

  constructor(initial?: GameData) {
    if (initial) this.data = { ...defaultData(), ...initial };
    else this.data.warmth = this.maxWarmth;
  }

  subscribe(fn: () => void): () => void {
    this.listeners.add(fn);
    return () => this.listeners.delete(fn);
  }

  emit(): void {
    this.listeners.forEach((fn) => fn());
  }

  tier(key: TierKey): number {
    return this.data.tiers[key];
  }
  get maxWarmth(): number {
    return MAX_WARMTH[this.tier('gear') - 1];
  }
  get detectorRadius(): number {
    return DETECTOR_RADIUS[this.tier('detector') - 1];
  }
  get digRadius(): number {
    return DIG_RADIUS[this.tier('digging') - 1];
  }
  get digStep(): number {
    return DIG_STEP[this.tier('digging') - 1];
  }
  get brushPx(): number {
    return BRUSH_PX[this.tier('cleaning') - 1];
  }
  digCost(wet: boolean): number {
    const base = DIG_COST[this.tier('digging') - 1];
    return wet && this.tier('gear') === 1 ? base * 1.5 : base;
  }
  get lanternUnlocked(): boolean {
    return this.tier('gear') >= 3;
  }

  addCoins(n: number): void {
    this.data.coins += n;
    this.emit();
  }

  spendWarmth(n: number): void {
    this.data.warmth = Math.max(0, this.data.warmth - n);
    this.emit();
  }

  restoreWarmth(): void {
    this.data.warmth = this.maxWarmth;
    this.emit();
  }

  /**
   * Stores a cleaned item. Shards go to the shard tray, Lost & Found items are delivered
   * on the spot (reward paid, note ticked off), everything else to the backpack.
   */
  addFound(defId: string): FoundResult {
    const def = DEF_BY_ID.get(defId);
    if (!def) return { item: null };
    this.data.found[defId] = (this.data.found[defId] ?? 0) + 1;
    if (def.category === 'shard' && def.shardSet !== undefined && def.shardIndex !== undefined) {
      const owned = (this.data.shards[def.shardSet] ??= []);
      if (!owned.includes(def.shardIndex)) owned.push(def.shardIndex);
      this.emit();
      return { item: null };
    }
    const q = QUEST_DEFS.find((x) => !this.data.questsDone.includes(x.id) && x.need.defId === defId);
    if (q) {
      const progress = (this.data.questCounts[q.id] = (this.data.questCounts[q.id] ?? 0) + 1);
      let reward = 0;
      if (progress >= q.need.count) {
        this.data.questsDone.push(q.id);
        this.data.coins += q.reward;
        reward = q.reward;
      }
      this.emit();
      return { item: null, quest: { id: q.id, giver: q.giver, reward, progress, need: q.need.count } };
    }
    const bonus = this.tier('cleaning') >= 2 ? 1.25 : 1;
    const item: InventoryItem = {
      uid: this.data.nextUid++,
      defId,
      name: def.name,
      category: def.category,
      value: Math.round(def.value * bonus),
      lore: def.lore,
    };
    this.data.inventory.push(item);
    this.emit();
    return { item };
  }

  hasShard(setId: string, idx: number): boolean {
    return !!this.data.shards[setId]?.includes(idx);
  }

  count(defId?: string, category?: Category): number {
    return this.data.inventory.filter(
      (i) => (defId === undefined || i.defId === defId) && (category === undefined || i.category === category),
    ).length;
  }

  sell(uid: number): number {
    const idx = this.data.inventory.findIndex((i) => i.uid === uid);
    if (idx < 0) return 0;
    const [item] = this.data.inventory.splice(idx, 1);
    this.data.coins += item.value;
    this.emit();
    return item.value;
  }

  sellCoinsAndRings(): number {
    const sellable = this.data.inventory.filter((i) => i.category === 'coin' || i.category === 'jewelry');
    const total = sellable.reduce((s, i) => s + i.value, 0);
    this.data.inventory = this.data.inventory.filter((i) => !sellable.includes(i));
    this.data.coins += total;
    this.emit();
    return total;
  }

  /** Recycles every piece of trash. Returns coins earned and pieces recycled. */
  recycleAll(): { coins: number; pieces: number } {
    const trash = this.data.inventory.filter((i) => i.category === 'trash');
    const coins = trash.reduce((s, i) => s + i.value, 0);
    this.data.inventory = this.data.inventory.filter((i) => i.category !== 'trash');
    this.data.coins += coins;
    this.data.ecology += trash.length;
    this.emit();
    return { coins, pieces: trash.length };
  }

  canAssemble(setId: string): boolean {
    return (this.data.shards[setId]?.length ?? 0) >= 3 && !this.data.assembled.includes(setId);
  }

  assemble(setId: string): number {
    if (!this.canAssemble(setId)) return 0;
    const set = SHARD_SETS.find((s) => s.id === setId);
    if (!set) return 0;
    this.data.assembled.push(setId);
    this.data.coins += set.payout;
    this.emit();
    return set.payout;
  }

  questProgress(q: QuestDef): number {
    return this.data.questsDone.includes(q.id) ? q.need.count : Math.min(q.need.count, this.data.questCounts[q.id] ?? 0);
  }

  /** Defs of quest items still needed that nobody has found yet, for the spawner to guarantee. */
  activeQuestDefIds(): string[] {
    const ids: string[] = [];
    for (const q of QUEST_DEFS) {
      if (this.data.questsDone.includes(q.id) || !q.need.defId) continue;
      const missing = q.need.count - this.questProgress(q);
      for (let i = 0; i < missing; i++) ids.push(q.need.defId);
    }
    return ids;
  }

  upgradeCost(key: TierKey): number | null {
    const t = this.tier(key);
    return t >= 3 ? null : TIER_COSTS[t];
  }

  buyUpgrade(key: TierKey): boolean {
    const cost = this.upgradeCost(key);
    if (cost === null || this.data.coins < cost) return false;
    this.data.coins -= cost;
    this.data.tiers[key]++;
    if (key === 'gear') this.data.warmth = this.maxWarmth;
    this.emit();
    return true;
  }

  /** Without the key the golf cart will not start. */
  buyCartKey(): boolean {
    if (this.data.ownsCartKey || this.data.coins < CART_KEY_COST) return false;
    this.data.coins -= CART_KEY_COST;
    this.data.ownsCartKey = true;
    this.emit();
    return true;
  }

  /** The bow shoots unlimited arrows. */
  buyBow(): boolean {
    if (this.data.ownsBow || this.data.coins < BOW_COST) return false;
    this.data.coins -= BOW_COST;
    this.data.ownsBow = true;
    this.emit();
    return true;
  }

  /** Bones the shop can still sell. */
  get bonesLeft(): number {
    return Math.max(0, BONE_STOCK - (this.data.bonesBought ?? 0));
  }

  /** Writes a page into the journal. True only the first time. */
  unlockPage(id: string): boolean {
    this.data.pages ??= [...INITIAL_PAGE_IDS];
    this.data.unreadPages ??= [];
    if (this.data.pages.includes(id)) return false;
    this.data.pages.push(id);
    // with the whole book open from the start there is nothing "new" to flag
    if (!BOOK_SHOW_ALL) this.data.unreadPages.push(id);
    this.emit();
    return true;
  }

  /** An owner has written back: the letter reaches the post after `delay` seconds. */
  queueLetter(id: string, delay: number): void {
    this.data.pendingLetters ??= [];
    this.data.letters ??= [];
    if (this.data.letters.includes(id) || this.data.pendingLetters.some((p) => p.id === id)) return;
    this.data.pendingLetters.push({ id, in: delay });
    this.emit();
  }

  /** Moves time along for letters in the post. Returns the ids of any that just arrived. */
  tickMail(dt: number): string[] {
    const pending = this.data.pendingLetters;
    if (!pending?.length) return [];
    const arrived: string[] = [];
    for (const p of pending) {
      p.in -= dt;
      if (p.in <= 0) arrived.push(p.id);
    }
    if (!arrived.length) return [];
    this.data.pendingLetters = pending.filter((p) => !arrived.includes(p.id));
    this.data.letters ??= [];
    this.data.unreadLetters ??= [];
    for (const id of arrived) {
      this.data.letters.push(id);
      this.data.unreadLetters.push(id);
    }
    this.emit();
    return arrived;
  }

  markLettersRead(): void {
    if (!this.data.unreadLetters?.length) return;
    this.data.unreadLetters = [];
    this.emit();
  }

  markPagesRead(): void {
    if (!this.data.unreadPages?.length) return;
    this.data.unreadPages = [];
    this.emit();
  }

  buyBone(): boolean {
    if (this.data.coins < BONE_COST || this.bonesLeft <= 0) return false;
    this.data.coins -= BONE_COST;
    this.data.bones = (this.data.bones ?? 0) + 1;
    this.data.bonesBought = (this.data.bonesBought ?? 0) + 1;
    this.emit();
    return true;
  }

  useBone(): boolean {
    if ((this.data.bones ?? 0) <= 0) return false;
    this.data.bones--;
    this.emit();
    return true;
  }

  buyDecor(id: string): boolean {
    const d = DECOR.find((x) => x.id === id);
    if (!d || this.data.decor.includes(id) || this.data.coins < d.cost) return false;
    this.data.coins -= d.cost;
    this.data.decor = [...this.data.decor, id];
    this.emit();
    return true;
  }

  cycleSign(): void {
    this.data.signText = ((this.data.signText ?? 0) + 1) % SIGN_TEXTS.length;
    this.emit();
  }

  buyDiveGear(): boolean {
    if (this.data.ownsDiveGear || this.data.coins < DIVE_COST) return false;
    this.data.coins -= DIVE_COST;
    this.data.ownsDiveGear = true;
    this.emit();
    return true;
  }

  buyBucket(): boolean {
    if (this.data.ownsBucket || this.data.coins < BUCKET_COST) return false;
    this.data.coins -= BUCKET_COST;
    this.data.ownsBucket = true;
    this.emit();
    return true;
  }

  /** A pack of marker flags: pay once, get a handful. Can be bought again and again. */
  buyFlags(): boolean {
    if (this.data.coins < FLAG_PACK_COST) return false;
    this.data.coins -= FLAG_PACK_COST;
    this.data.flags = (this.data.flags ?? 0) + FLAG_PACK_SIZE;
    this.data.ownsFlags = true;
    this.emit();
    return true;
  }

  /** Takes a flag out of the bag. */
  useFlag(): boolean {
    if ((this.data.flags ?? 0) <= 0) return false;
    this.data.flags--;
    this.emit();
    return true;
  }

  returnFlag(): void {
    this.data.flags = (this.data.flags ?? 0) + 1;
    this.emit();
  }

  /** Without the key the boat's motor will not start. */
  buyBoatKey(): boolean {
    if (this.data.ownsBoatKey || this.data.coins < BOAT_KEY_COST) return false;
    this.data.coins -= BOAT_KEY_COST;
    this.data.ownsBoatKey = true;
    this.emit();
    return true;
  }

  /** The toy pistol is a purely-for-fun purchase. */
  buyGun(): boolean {
    if (this.data.ownsGun || this.data.coins < GUN_COST) return false;
    this.data.coins -= GUN_COST;
    this.data.ownsGun = true;
    this.emit();
    return true;
  }

  setLighting(mode: LightingMode): void {
    this.data.lighting = mode;
    this.emit();
  }

  get totalDefs(): number {
    return ITEM_DEFS.length;
  }
}
