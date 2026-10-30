export type Category = 'trash' | 'coin' | 'jewelry' | 'quest' | 'shard';
export type Signature = 'trash' | 'coin' | 'deep';
export type Zone = 'wet' | 'dry' | 'any';

export interface ItemDef {
  id: string;
  name: string;
  category: Category;
  value: number;
  lore: string;
  signature: Signature;
  depth: [number, number];
  weight: number;
  zone: Zone;
  shardSet?: string;
  shardIndex?: number;
}

export interface ShardSet {
  id: string;
  name: string;
  payout: number;
  lore: string;
}

export const SHARD_SETS: ShardSet[] = [
  {
    id: 'astrolabe',
    name: 'Ancient Astrolabe',
    payout: 250,
    lore: 'A navigator’s instrument, glued whole again. The stars it once measured still gleam on its rings.',
  },
  {
    id: 'medallion',
    name: 'Pirate Medallion',
    payout: 250,
    lore: 'A swaggering old medallion. Somewhere, a very smug skull is smiling at you.',
  },
];

const shard = (set: string, idx: number, name: string): ItemDef => ({
  id: `${set}_${idx + 1}`,
  name: `${name} Shard (${idx + 1}/3)`,
  category: 'shard',
  value: 27,
  lore: 'A curved fragment of something far older than the tide. Two more pieces are out there…',
  signature: 'deep',
  depth: [0.45, 0.8],
  weight: 0,
  zone: 'any',
  shardSet: set,
  shardIndex: idx,
});

/**
 * How much a find sells for, as a share of the list prices below. The beach holds about 6000g of sellable finds in all, so scaling
 * them down keeps the camper shop's upgrades (several hundred gold each) a real goal across many hours instead of a one-hour errand.
 */
const VALUE_SCALE = 0.55;

const RAW_DEFS: ItemDef[] = [
  {
    id: 'pulltab', name: 'Soda Pull-Tab', category: 'trash', value: 5, signature: 'trash',
    lore: 'Somebody’s summer, long since flat. Perfectly recyclable.', depth: [0.12, 0.3], weight: 5, zone: 'any',
  },
  {
    id: 'nail', name: 'Bent Rusty Nail', category: 'trash', value: 6, signature: 'trash',
    lore: 'Pried from a pier plank decades ago. It has opinions about rust.', depth: [0.12, 0.3], weight: 4, zone: 'any',
  },
  {
    id: 'tincan', name: 'Crushed Tin Can', category: 'trash', value: 8, signature: 'trash',
    lore: 'Once held beans. Now it holds a small piece of the shoreline.', depth: [0.15, 0.35], weight: 4, zone: 'any',
  },
  {
    id: 'penny', name: 'Old Copper Penny', category: 'coin', value: 15, signature: 'coin',
    lore: 'Worn smooth by a thousand tides and a thousand pockets.', depth: [0.22, 0.45], weight: 5, zone: 'any',
  },
  {
    id: 'quarter', name: 'Silver Quarter', category: 'coin', value: 25, signature: 'coin',
    lore: 'Heads, you find it. Tails, you also find it.', depth: [0.25, 0.5], weight: 3, zone: 'any',
  },
  {
    id: 'doubloon', name: '1700s Gold Doubloon', category: 'coin', value: 35, signature: 'coin',
    lore: 'Minted for a king, lost by a sailor, found by you.', depth: [0.3, 0.55], weight: 1.5, zone: 'any',
  },
  {
    id: 'signet', name: 'Signet Ring', category: 'jewelry', value: 60, signature: 'deep',
    lore: 'A crest nobody remembers, worn proudly by somebody once.', depth: [0.4, 0.65], weight: 1.4, zone: 'any',
  },
  {
    id: 'ruby_band', name: 'Ruby Band', category: 'jewelry', value: 120, signature: 'deep',
    lore: 'The ruby catches the morning light like it never left your hand.', depth: [0.45, 0.7], weight: 0.8, zone: 'any',
  },
  {
    id: 'compass', name: 'Grandfather’s Silver Compass', category: 'quest', value: 150, signature: 'deep',
    lore: 'The needle still points home. Someone has been missing this.', depth: [0.45, 0.75], weight: 0, zone: 'wet',
  },
  {
    id: 'brass_key', name: 'Captain’s Brass Key', category: 'quest', value: 180, signature: 'deep',
    lore: 'Ornate, heavy, and clearly meant to open something important.', depth: [0.45, 0.75], weight: 0, zone: 'dry',
  },
  {
    id: 'bottle_cap', name: 'Bottle Cap', category: 'trash', value: 5, signature: 'trash',
    lore: 'Popped open on a happy afternoon, long ago.', depth: [0.12, 0.35], weight: 3.5, zone: 'any',
  },
  {
    id: 'glass_bottle', name: 'Glass Bottle', category: 'trash', value: 9, signature: 'trash',
    lore: 'No message inside, sadly. Just a lot of sand.', depth: [0.12, 0.35], weight: 3.5, zone: 'any',
  },
  {
    id: 'fishing_hook', name: 'Rusty Fishing Hook', category: 'trash', value: 6, signature: 'trash',
    lore: 'The one that got away. It got away with the line, too.', depth: [0.12, 0.35], weight: 3.5, zone: 'any',
  },
  {
    id: 'flip_flop', name: 'Lost Flip-Flop', category: 'trash', value: 8, signature: 'trash',
    lore: 'Its partner is probably somewhere on another beach.', depth: [0.12, 0.35], weight: 3.5, zone: 'any',
  },
  {
    id: 'rusty_bolt', name: 'Rusty Bolt', category: 'trash', value: 6, signature: 'trash',
    lore: 'Once held something important together.', depth: [0.12, 0.35], weight: 3.5, zone: 'any',
  },
  {
    id: 'plastic_ring', name: 'Six-Pack Ring', category: 'trash', value: 5, signature: 'trash',
    lore: 'Best taken to the recycling bin before the gulls find it.', depth: [0.12, 0.35], weight: 3.5, zone: 'any',
  },
  {
    id: 'dime', name: 'Silver Dime', category: 'coin', value: 18, signature: 'coin',
    lore: 'Small, bright and surprisingly heavy with stories.', depth: [0.22, 0.5], weight: 4, zone: 'any',
  },
  {
    id: 'nickel', name: 'Old Nickel', category: 'coin', value: 20, signature: 'coin',
    lore: 'It has been through a great many pockets.', depth: [0.22, 0.5], weight: 4, zone: 'any',
  },
  {
    id: 'silver_dollar', name: 'Silver Dollar', category: 'coin', value: 45, signature: 'coin',
    lore: 'A proper, weighty coin. Someone would have missed this one.', depth: [0.3, 0.55], weight: 1.2, zone: 'any',
  },
  {
    id: 'real', name: 'Spanish Real', category: 'coin', value: 40, signature: 'coin',
    lore: 'Cut from a bar of silver on a ship that never arrived.', depth: [0.3, 0.55], weight: 1, zone: 'any',
  },
  {
    id: 'denarius', name: 'Roman Denarius', category: 'coin', value: 60, signature: 'coin',
    lore: 'An emperor’s face, worn soft by two thousand years.', depth: [0.35, 0.6], weight: 0.6, zone: 'any',
  },
  {
    id: 'sovereign', name: 'Gold Sovereign', category: 'coin', value: 75, signature: 'coin',
    lore: 'It still shines as if it were minted this morning.', depth: [0.35, 0.6], weight: 0.4, zone: 'any',
  },
  {
    id: 'pocket_watch', name: 'Silver Pocket Watch', category: 'quest', value: 130, signature: 'deep',
    lore: 'It stopped ticking a long time ago, but the engraving is still readable.', depth: [0.45, 0.8], weight: 0, zone: 'wet',
  },
  {
    id: 'pearl_necklace', name: 'Pearl Necklace', category: 'quest', value: 170, signature: 'deep',
    lore: 'Every pearl is still on its string. Someone will be overjoyed.', depth: [0.45, 0.8], weight: 0, zone: 'any',
  },
  {
    id: 'sunglasses', name: 'Vintage Sunglasses', category: 'quest', value: 80, signature: 'deep',
    lore: 'Round, dark, and impossibly stylish.', depth: [0.45, 0.8], weight: 0, zone: 'dry',
  },
  {
    id: 'harmonica', name: 'Silver Harmonica', category: 'quest', value: 100, signature: 'deep',
    lore: 'It still plays one wheezy, lovely note.', depth: [0.45, 0.8], weight: 0, zone: 'any',
  },
  {
    id: 'wedding_band', name: 'Wedding Band', category: 'quest', value: 160, signature: 'deep',
    lore: 'A small diamond and a very big reason to look for it.', depth: [0.45, 0.8], weight: 0, zone: 'wet',
  },
  {
    id: 'toy_boat', name: 'Wooden Toy Sailboat', category: 'quest', value: 70, signature: 'deep',
    lore: 'Hand-carved. Somebody’s whole summer was spent on this.', depth: [0.45, 0.8], weight: 0, zone: 'any',
  },
  // finds that lie out in the open on the island, and nowhere else
  {
    id: 'piece_eight', name: 'Piece of Eight', category: 'coin', value: 55, signature: 'coin',
    lore: 'Hacked from a silver bar by a hurried hand, and spent by a pirate who never came back for the change.', depth: [0.2, 0.4], weight: 0, zone: 'any',
  },
  {
    id: 'black_pearl', name: 'Black Pearl', category: 'jewelry', value: 130, signature: 'deep',
    lore: 'It drinks the light and gives back a green shimmer. The island does not give these up easily.', depth: [0.3, 0.5], weight: 0, zone: 'any',
  },
  {
    id: 'stone_idol', name: 'Carved Stone Idol', category: 'jewelry', value: 100, signature: 'deep',
    lore: 'A small, solemn face carved long before anyone drew a map of this place.', depth: [0.3, 0.5], weight: 0, zone: 'any',
  },
  {
    id: 'conch', name: 'Pink Conch Shell', category: 'jewelry', value: 60, signature: 'deep',
    lore: 'Hold it to your ear and the whole sea is still in there.', depth: [0.2, 0.4], weight: 0, zone: 'any',
  },
  // finds that lie on the sea floor, for a diver
  {
    id: 'starfish', name: 'Orange Starfish', category: 'jewelry', value: 12, signature: 'deep',
    lore: 'Five arms, a rough sunset-orange back and a very calm opinion of the tide.', depth: [0.1, 0.2], weight: 0, zone: 'any',
  },
  {
    id: 'porcelain_plate', name: 'Painted Porcelain Plate', category: 'jewelry', value: 50, signature: 'deep',
    lore: 'Blue flowers on white, and not a chip on it. Somebody once ate their dinner off this.', depth: [0.1, 0.2], weight: 0, zone: 'any',
  },
  {
    id: 'ship_bell', name: 'Brass Ship\u2019s Bell', category: 'jewelry', value: 110, signature: 'deep',
    lore: 'It rang through fog for a hundred years, and now it rings only for the fish.', depth: [0.1, 0.2], weight: 0, zone: 'any',
  },
  {
    id: 'sea_oyster', name: 'Pearl Oyster', category: 'jewelry', value: 80, signature: 'deep',
    lore: 'The shell is clamped shut. Inside, something round and quietly lovely.', depth: [0.1, 0.2], weight: 0, zone: 'any',
  },
  {
    id: 'gold_bar', name: 'Gold Bar', category: 'jewelry', value: 150, signature: 'deep',
    lore: 'Stamped with a crown and a number nobody has used in two hundred years. It is much heavier than it looks.', depth: [0.1, 0.2], weight: 0, zone: 'any',
  },
  // a bottle on the sea floor with a map inside, and the strongbox that map leads to
  {
    id: 'message_bottle', name: 'Message in a Bottle', category: 'quest', value: 0, signature: 'deep',
    lore: 'The cork is sealed with wax, and something is rolled up inside: a map, drawn in a hurry by someone who did not expect to be found.', depth: [0.1, 0.2], weight: 0, zone: 'any',
  },
  {
    id: 'captain_chest', name: 'Hidden Hatch', category: 'quest', value: 0, signature: 'deep',
    lore: 'A trapdoor buried flat in the dune. It is not something you can pick up: it is a way down.', depth: [0.3, 0.45], weight: 0, zone: 'any',
  },
  // a buried chest that only shows up for someone who holds the treasure map
  {
    id: 'treasure_cache', name: 'Sealed Treasure Chest', category: 'jewelry', value: 280, signature: 'deep',
    lore: 'Heavy, salt-stained and still locked. Whoever buried it meant it to stay buried, and drew a map so they could find it again.', depth: [0.3, 0.45], weight: 0, zone: 'any',
  },
  shard('astrolabe', 0, 'Astrolabe'),
  shard('astrolabe', 1, 'Astrolabe'),
  shard('astrolabe', 2, 'Astrolabe'),
  shard('medallion', 0, 'Medallion'),
  shard('medallion', 1, 'Medallion'),
  shard('medallion', 2, 'Medallion'),
];

export const ITEM_DEFS: ItemDef[] = RAW_DEFS.map((d) => ({ ...d, value: Math.max(3, Math.round(d.value * VALUE_SCALE)) }));

/** The finds that lie on the sea floor. */
export const DIVE_ITEM_IDS = new Set(['message_bottle', 'starfish', 'porcelain_plate', 'ship_bell', 'sea_oyster', 'gold_bar']);

/** The finds that belong to the island. */
export const ISLAND_ITEM_IDS = new Set(['piece_eight', 'black_pearl', 'stone_idol', 'conch']);

export const DEF_BY_ID = new Map<string, ItemDef>(ITEM_DEFS.map((d) => [d.id, d]));

export interface QuestNeed {
  defId?: string;
  category?: Category;
  count: number;
}

export interface QuestDef {
  id: string;
  giver: string;
  text: string;
  reward: number;
  need: QuestNeed;
}

export const QUEST_DEFS: QuestDef[] = [
  {
    id: 'q_compass', giver: 'Marta',
    text: 'Lost my grandfather’s silver compass near the wet shoreline.',
    reward: 85, need: { defId: 'compass', count: 1 },
  },
  {
    id: 'q_key', giver: 'Old Pim',
    text: 'Dropped the captain’s brass key while walking the dunes. Please look!',
    reward: 125, need: { defId: 'brass_key', count: 1 },
  },
  {
    id: 'q_ruby', giver: 'Juno',
    text: 'My ruby band slipped off while I was clamming. I’d be so grateful.',
    reward: 70, need: { defId: 'ruby_band', count: 1 },
  },
  {
    id: 'q_quarters', giver: 'The Collector',
    text: 'Seeking two shiny silver quarters for my jar. Not picky, just shiny.',
    reward: 65, need: { defId: 'quarter', count: 2 },
  },
  {
    id: 'q_watch', giver: 'Grandma Ines',
    text: 'My silver pocket watch fell out near the wet shoreline. It belonged to my father.',
    reward: 85, need: { defId: 'pocket_watch', count: 1 },
  },
  {
    id: 'q_pearls', giver: 'Celeste',
    text: 'I lost my pearl necklace while paddling. Every pearl was a birthday present.',
    reward: 110, need: { defId: 'pearl_necklace', count: 1 },
  },
  {
    id: 'q_glasses', giver: 'Rico',
    text: 'My lucky sunglasses blew off somewhere up near the dunes. I cannot see without them. Okay, I can, but I look worse.',
    reward: 65, need: { defId: 'sunglasses', count: 1 },
  },
  {
    id: 'q_harmonica', giver: 'Busker Bo',
    text: 'I dropped my harmonica on the way to the pier. The music has gone out of my day.',
    reward: 75, need: { defId: 'harmonica', count: 1 },
  },
  {
    id: 'q_wedding', giver: 'Dana',
    text: 'I took my wedding band off to swim and… well. Please look near the water.',
    reward: 105, need: { defId: 'wedding_band', count: 1 },
  },
  {
    id: 'q_boat', giver: 'Little Mo',
    text: 'My wooden sailboat drifted away from me. It has a red sail and a very brave captain.',
    reward: 55, need: { defId: 'toy_boat', count: 1 },
  },
  {
    id: 'q_pennies', giver: 'The Jar Lady',
    text: 'I am filling a jar with old copper pennies. Bring me three and I will pay well.',
    reward: 40, need: { defId: 'penny', count: 3 },
  },
  {
    id: 'q_doubloon', giver: 'Captain Brine',
    text: 'A real pirate doubloon! If you find one I will take it off your hands for a fair price.',
    reward: 100, need: { defId: 'doubloon', count: 1 },
  },
  {
    id: 'q_denarius', giver: 'Professor Plum',
    text: 'Romans once sailed this coast. A denarius would make my whole year, truly.',
    reward: 105, need: { defId: 'denarius', count: 1 },
  },
];
