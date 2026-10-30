import type { GameData } from './StateManager';
import { store } from './Storage';

const INDEX_KEY = 'cozy-treasure-hunter-saves-v3';
const SLOT_PREFIX = 'cozy-treasure-hunter-slot-';
const OLD_KEY = 'cozy-treasure-hunter-save-v2';
const LOAD_FLAG = 'cozy-treasure-hunter-load';

/** The one slot the game saves into by itself; every autosave writes over it. */
export const AUTOSAVE_ID = 'autosave';

export interface SavedItem {
  defId: string;
  x: number;
  z: number;
  targetDepth: number;
  collected: boolean;
}

/** Everything needed to put the world back exactly as the player left it. */
export interface Snapshot {
  v: 1;
  savedAt: number;
  data: GameData;
  player: { x: number; z: number; yaw: number; pitch: number };
  tool: 1 | 2 | 4 | 5 | 6 | 7 | 8;
  radio: boolean;
  /** which radio station was playing */
  radioTrack?: number;
  /** time of day in [0,1) */
  dayTime?: number;
  /** the beach resolution it was saved on; holes from another map size cannot be reused */
  size?: number;
  /** where the player dug, for the map */
  holes?: [number, number][];
  waypoint?: { x: number; z: number } | null;
  /** planted marker flags */
  flags?: [number, number][];
  /** sand castles on the beach, with how worn down they are */
  castles?: { x: number; z: number; stage: number; hp: number; seed?: number }[];
  /** the find the dog is working on: its index in the item list, or -1 */
  dog?: { target: number };
  cart?: { x: number; z: number; heading: number };
  boat?: { x: number; z: number; heading: number };
  /** which sea chests have already been hauled in */
  chests?: boolean[];
  items: SavedItem[];
  /** sparse list of [vertex index, dug depth] for every changed sand vertex */
  dug: [number, number][];
}

/** The small summary shown in the save list; the heavy snapshot lives under its own key. */
export interface SaveMeta {
  id: string;
  name: string;
  savedAt: number;
  coins: number;
  finds: number;
}

const readIndex = (): SaveMeta[] => {
  try {
    const raw = store.getItem(INDEX_KEY);
    return raw ? (JSON.parse(raw) as SaveMeta[]) : [];
  } catch {
    return [];
  }
};

const writeIndex = (list: SaveMeta[]): boolean => store.setItem(INDEX_KEY, JSON.stringify(list));

/** Brings the earlier single-slot save along, once. */
const migrateOld = (): void => {
  try {
    const raw = store.getItem(OLD_KEY);
    if (!raw) return;
    const snap = JSON.parse(raw) as Snapshot;
    store.removeItem(OLD_KEY);
    if (snap.v === 1) writeSave(snap, 'Earlier save');
  } catch {
    /* ignore */
  }
};

/** All saves, newest first. */
export const listSaves = (): SaveMeta[] => {
  migrateOld();
  return readIndex().sort((a, b) => b.savedAt - a.savedAt);
};

export const readSave = (id: string): Snapshot | null => {
  try {
    const raw = store.getItem(SLOT_PREFIX + id);
    if (!raw) return null;
    const s = JSON.parse(raw) as Snapshot;
    return s.v === 1 ? s : null;
  } catch {
    return null;
  }
};

/** Saves a snapshot as a new entry, or over the entry `id`. Returns null if storage refused. */
export const writeSave = (snap: Snapshot, name: string, id?: string): SaveMeta | null => {
  const meta: SaveMeta = {
    id: id ?? `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4).toString(36)}`,
    name: name.trim() || 'Unnamed save',
    savedAt: snap.savedAt,
    coins: snap.data.coins,
    finds: Object.keys(snap.data.found).length,
  };
  if (!store.setItem(SLOT_PREFIX + meta.id, JSON.stringify(snap))) return null;
  const list = readIndex().filter((m) => m.id !== meta.id);
  list.push(meta);
  return writeIndex(list) ? meta : null;
};

export const deleteSave = (id: string): void => {
  store.removeItem(SLOT_PREFIX + id);
  writeIndex(readIndex().filter((m) => m.id !== id));
};

/** Loading swaps the whole world, so we remember which save and reload the page into it. */
export const requestLoad = (id: string): void => {
  try {
    sessionStorage.setItem(LOAD_FLAG, id);
  } catch {
    /* ignore */
  }
};

/** Returns the save to start from, if the player asked to load one before this page load. */
export const takePendingLoad = (): Snapshot | null => {
  let id: string | null = null;
  try {
    id = sessionStorage.getItem(LOAD_FLAG);
    sessionStorage.removeItem(LOAD_FLAG);
  } catch {
    return null;
  }
  return id ? readSave(id) : null;
};

export const timeLabel = (t: number): string =>
  new Date(t).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
