import { store } from './Storage';

export interface AchievementDef {
  /** also the name to create the achievement under in Steamworks */
  id: string;
  name: string;
  desc: string;
  icon: string;
  /** shown as "?" until it is earned */
  secret?: boolean;
  /** the journal page whose discovery earns it, when it comes from something that happens once */
  page?: string;
}

export const ACHIEVEMENTS: AchievementDef[] = [
  // the beach
  { id: 'FIRST_DIG', name: 'First Dig', desc: 'Dig your first hole in the sand.', icon: '⛏️', page: 'dig_first' },
  { id: 'FINDS_10', name: 'Beachcomber', desc: 'Find 10 things.', icon: '🐚' },
  { id: 'FINDS_50', name: 'Treasure Hunter', desc: 'Find 50 things.', icon: '🧭' },
  { id: 'FINDS_150', name: 'Pack Rat', desc: 'Find 150 things.', icon: '🎒' },
  { id: 'QUEST_FIRST', name: 'Finders, Returners', desc: 'Return a lost item to its owner.', icon: '💌' },
  { id: 'QUEST_ALL', name: 'Everything Back Home', desc: 'Return every lost item on the board.', icon: '🏡' },
  { id: 'ASSEMBLED', name: 'Restorer', desc: 'Piece both antiques back together.', icon: '🏺' },
  { id: 'RICH', name: 'Nest Egg', desc: 'Hold 2000 coins at once.', icon: '💰' },
  { id: 'CHEST_BURIED', name: 'X Marks the Spot', desc: 'Dig up a sealed chest from the treasure map.', icon: '🗺️', page: 'x_marks' },
  { id: 'CHEST_SEA', name: 'Chest at Sea', desc: 'Haul in a treasure chest from the water.', icon: '⚓', page: 'chest_first' },
  // camp life
  { id: 'MAIL', name: 'You’ve Got Mail', desc: 'Receive a letter at the camp post.', icon: '📬', page: 'mail_first' },
  { id: 'HAMMOCK', name: 'Naptime', desc: 'Sleep in the hammock until morning.', icon: '🛏️', page: 'hammock_first' },
  { id: 'RADIO', name: 'Tune In', desc: 'Turn on the radio.', icon: '📻', page: 'radio_on' },
  { id: 'DECOR', name: 'Home Sweet Camp', desc: 'Buy every camp decoration.', icon: '🏮' },
  { id: 'WEEK', name: 'A Week by the Sea', desc: 'Reach day 7.', icon: '📅' },
  { id: 'NIGHT', name: 'Night Owl', desc: 'Be out on the beach after dark.', icon: '🌙', page: 'night_first' },
  { id: 'RAIN', name: 'Rainy Day', desc: 'Be caught out in the rain.', icon: '🌧️', page: 'rain_first' },
  { id: 'CASTLE', name: 'Sandy Builder', desc: 'Build a sandcastle.', icon: '🏰', page: 'castle_first' },
  { id: 'DOG_PET', name: 'Good Dog', desc: 'Give the dog some love.', icon: '🐕', page: 'dog_pet' },
  { id: 'DOG_FIND', name: 'Fetch!', desc: 'Let the dog sniff out a find.', icon: '🦴', page: 'dog_found' },
  // wheels, water and the island
  { id: 'BOAT', name: 'Captain', desc: 'Buy the boat key.', icon: '🚤' },
  { id: 'ISLAND', name: 'Landfall', desc: 'Set foot on the island.', icon: '🏝️', page: 'island_first' },
  { id: 'RUINS', name: 'Old Stones', desc: 'Find the ruins on the island.', icon: '🗿', page: 'island_ruins' },
  { id: 'MAP', name: 'An Old Map', desc: 'Take the map from the island’s altar.', icon: '📜', page: 'map_taken' },
  // below the surface
  { id: 'DIVE', name: 'Into the Blue', desc: 'Dive for the first time.', icon: '🤿', page: 'dive_first' },
  { id: 'WRECK', name: 'The Sunken Ship', desc: 'Find the shipwreck.', icon: '⚓', page: 'wreck_found' },
  { id: 'HARPOON', name: 'First Catch', desc: 'Spear your first fish.', icon: '🐟', page: 'harpoon_first' },
  { id: 'CREATURE', name: 'Strange Neighbours', desc: 'Spear an octopus, seahorse, sea snake or crab.', icon: '🐙', page: 'sea_creature' },
  { id: 'BOTTLE', name: 'Message from the Deep', desc: 'Find the bottle with the map inside.', icon: '🍾', page: 'bottle_map' },
  // the room under the dunes
  { id: 'HATCH', name: 'A Door Under the Sand', desc: 'Dig up the hidden hatch.', icon: '🚪', page: 'captain_hatch' },
  { id: 'UNDERGROUND', name: 'Under the Dunes', desc: 'Climb down into the stone room.', icon: '🔥', page: 'underground_first' },
  { id: 'JOURNAL', name: 'The Captain’s Story', desc: 'Read the captain’s journal.', icon: '📖', page: 'captain_journal' },
  { id: 'DOOR', name: 'Knock Knock', desc: 'Try the great door.', icon: '✨', secret: true, page: 'great_door' },
  { id: 'DOOR_OPEN', name: 'High Noon', desc: 'Open the great door.', icon: '☀️', secret: true, page: 'door_open' },
  { id: 'HOARD', name: 'The Marigold\u2019s Gold', desc: 'Find the lost gold.', icon: '💰', secret: true, page: 'hoard_found' },
];

const KEY = 'cozy-treasure-hunter-achievements-v1';

/** What the desktop app offers for Steam, if it is running under Steam. */
interface SteamBridge {
  unlock(id: string): void;
}
const steam = (): SteamBridge | undefined => (window as unknown as { desktop?: { steam?: SteamBridge } }).desktop?.steam;

/** The player's achievements. They belong to the player, not to one saved game, so they have their own file. */
export class Achievements {
  private done = new Set<string>();
  private byPage = new Map(ACHIEVEMENTS.filter((a) => a.page).map((a) => [a.page as string, a.id]));
  /** called with an achievement the moment it is earned */
  onUnlock: (a: AchievementDef) => void = () => undefined;

  constructor() {
    try {
      const raw = store.getItem(KEY);
      if (raw) for (const id of JSON.parse(raw) as string[]) this.done.add(id);
    } catch {
      /* an unreadable file just starts again */
    }
    // anything earned before Steam was running is handed over now
    for (const id of this.done) steam()?.unlock(id);
  }

  has(id: string): boolean {
    return this.done.has(id);
  }

  get count(): number {
    return this.done.size;
  }

  unlock(id: string): void {
    if (this.done.has(id)) return;
    const def = ACHIEVEMENTS.find((a) => a.id === id);
    if (!def) return;
    this.done.add(id);
    store.setItemLater(KEY, JSON.stringify([...this.done]));
    steam()?.unlock(id);
    this.onUnlock(def);
  }

  /** Earns the achievement tied to a journal page, if there is one. */
  page(pageId: string): void {
    const id = this.byPage.get(pageId);
    if (id) this.unlock(id);
  }
}
