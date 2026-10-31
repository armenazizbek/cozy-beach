import type { AudioManager } from '../core/AudioManager';
import { padText } from './PadText';
import { DEF_BY_ID, ITEM_DEFS, QUEST_DEFS, SHARD_SETS } from '../core/items';
import type { LightingMode, TierKey } from '../core/StateManager';
import { DECOR, DIVE_COST, LUXURY, BUCKET_COST, BONE_COST, BONE_STOCK, BOAT_KEY_COST, BOW_COST, CART_KEY_COST, FLAG_PACK_COST, FLAG_PACK_SIZE, GUN_COST, StateManager, TIER_INFO } from '../core/StateManager';

export type CampTab = 'rest' | 'recycle' | 'sell' | 'board' | 'upgrades' | 'luxury' | 'shelf';

export interface CampHooks {
  onSip: () => void;
  onClose: () => void;
  onLighting: () => void;
  onRecycled: (pieces: number) => void;
  onAssembled: (setId: string) => void;
  onLuxury: () => void;
  toast: (msg: string) => void;
}

const TABS: [CampTab, string][] = [
  ['rest', '☕ Rest'],
  ['recycle', '♻️ Recycle'],
  ['sell', '🪙 Sell & Assemble'],
  ['board', '📌 Lost & Found'],
  ['upgrades', '🧰 Upgrades'],
  ['luxury', '👑 Luxury'],
  ['shelf', '🏺 Trophy Shelf'],
];

const LIGHT_NAMES: Record<LightingMode, string> = {
  cycle: 'Day & night cycle',
  golden: 'Golden Morning',
  day: 'Bright Day',
  rain: 'Rainy Day',
  sunset: 'Sunset',
  night: 'Cozy Night',
};

export class CampModal {
  private panel = document.createElement('div');
  private body = document.createElement('div');
  private tabs = document.createElement('div');
  private tab: CampTab = 'rest';
  private _open = false;

  constructor(
    parent: HTMLElement,
    private state: StateManager,
    private audio: AudioManager,
    private hooks: CampHooks,
  ) {
    this.panel.className = 'camp hidden';
    const head = document.createElement('div');
    head.className = 'camp-head';
    head.innerHTML = '<h2>Base Camp</h2>';
    const close = document.createElement('button');
    close.className = 'btn ghost small';
    close.textContent = 'Close (Esc)';
    close.dataset.kb = 'Close (Esc)';
    close.onclick = (): void => this.hooks.onClose();
    head.append(close);
    this.tabs.className = 'tabs';
    this.body.className = 'camp-body';
    this.panel.append(head, this.tabs, this.body);
    parent.append(this.panel);
    state.subscribe(() => {
      if (this._open) this.render();
    });
  }

  get isOpen(): boolean {
    return this._open;
  }

  open(tab: CampTab = 'rest'): void {
    this.tab = tab;
    this._open = true;
    this.panel.classList.remove('hidden');
    this.render();
  }

  close(): void {
    this._open = false;
    this.panel.classList.add('hidden');
  }

  private btn(label: string, fn: () => void, cls = '', disabled = false): HTMLButtonElement {
    const b = document.createElement('button');
    b.className = `btn ${cls}`;
    b.textContent = label;
    b.disabled = disabled;
    b.onclick = (): void => {
      fn();
    };
    return b;
  }

  private render(): void {
    this.tabs.innerHTML = '';
    for (const [id, label] of TABS) {
      if (id === 'luxury' && !this.state.data.hoardTaken) continue;
      const t = this.btn(label, () => {
        this.audio.ui();
        this.tab = id;
        this.render();
      }, `tab ${id === this.tab ? 'on' : ''}`);
      this.tabs.append(t);
    }
    this.body.innerHTML = '';
    const d = this.state.data;
    const coins = document.createElement('div');
    coins.className = 'coins-line';
    coins.textContent = `🪙 ${d.coins}g`;
    this.body.append(coins);
    switch (this.tab) {
      case 'rest': this.renderRest(); break;
      case 'recycle': this.renderRecycle(); break;
      case 'sell': this.renderSell(); break;
      case 'board': this.renderBoard(); break;
      case 'upgrades': this.renderUpgrades(); break;
      case 'luxury': this.renderLuxury(); break;
      case 'shelf': this.renderShelf(); break;
    }
  }

  private p(text: string, cls = ''): HTMLParagraphElement {
    const p = document.createElement('p');
    p.className = cls;
    p.textContent = padText(text);
    return p;
  }

  private renderRest(): void {
    const mug = TIER_INFO.gear.names[this.state.tier('gear') - 1];
    this.body.append(
      this.p(`Pour a warm cup from your ${mug}. It restores your Warmth to full.`),
      this.btn('☕ Sip from Thermos', () => this.hooks.onSip(), 'big'),
    );
    const lantern = document.createElement('div');
    lantern.className = 'section';
    lantern.append(this.p(`Lantern: ${LIGHT_NAMES[this.state.data.lighting]}`));
    if (this.state.lanternUnlocked) {
      lantern.append(this.btn('🏮 Switch lighting', () => this.hooks.onLighting()));
    } else {
      lantern.append(this.p('Upgrade to the Brass Espresso Flask & Lantern to change the lighting.', 'faint'));
    }
    this.body.append(lantern);
  }

  private renderRecycle(): void {
    const trash = this.state.data.inventory.filter((i) => i.category === 'trash');
    this.body.append(this.p(`Beach Ecology Score: ${this.state.data.ecology}  (grass tufts and crabs return as it grows)`));
    if (trash.length === 0) {
      this.body.append(this.p('No trash in your backpack. Pull-tabs, nails and cans you clean up will go here.', 'faint'));
      return;
    }
    const list = document.createElement('div');
    list.className = 'list';
    for (const t of trash) list.append(this.row(`${t.name}`, `${t.value}g`));
    const total = trash.reduce((s, i) => s + i.value, 0);
    this.body.append(
      list,
      this.btn(`♻️ Recycle all (${trash.length}) for ${total}g`, () => {
        const r = this.state.recycleAll();
        this.audio.coin();
        this.hooks.onRecycled(r.pieces);
        this.hooks.toast(`Recycled ${r.pieces} pieces for ${r.coins}g. The beach thanks you!`);
      }, 'big'),
    );
  }

  private row(left: string, right: string, extra?: HTMLElement): HTMLElement {
    const r = document.createElement('div');
    r.className = 'item-row';
    const a = document.createElement('span');
    a.textContent = left;
    const b = document.createElement('span');
    b.className = 'right';
    b.textContent = right;
    r.append(a, b);
    if (extra) r.append(extra);
    return r;
  }

  private renderSell(): void {
    const sellable = this.state.data.inventory.filter((i) => i.category === 'coin' || i.category === 'jewelry');
    this.body.append(this.p('Sell cleaned coins and jewelry. Collect all 3 shards of an antique and glue them together for a big payout.'));
    const list = document.createElement('div');
    list.className = 'list';
    if (sellable.length === 0) list.append(this.p('Nothing to sell yet.', 'faint'));
    for (const it of sellable) {
      list.append(
        this.row(it.name, `${it.value}g`, this.btn('Sell', () => {
          this.state.sell(it.uid);
          this.audio.coin();
        }, 'small')),
      );
    }
    this.body.append(list);
    if (sellable.length > 1) {
      const total = sellable.reduce((s, i) => s + i.value, 0);
      this.body.append(this.btn(`Sell all for ${total}g`, () => {
        this.state.sellCoinsAndRings();
        this.audio.coin();
      }));
    }

    const h = document.createElement('h3');
    h.textContent = 'Workbench: antique shards';
    this.body.append(h);
    for (const set of SHARD_SETS) {
      const owned = this.state.data.shards[set.id] ?? [];
      const done = this.state.data.assembled.includes(set.id);
      const row = document.createElement('div');
      row.className = 'shard-row';
      const pips = [0, 1, 2].map((i) => (owned.includes(i) || done ? '◆' : '◇')).join(' ');
      row.append(this.p(`${set.name}   ${pips}   ${done ? 'Assembled' : `${owned.length}/3`}`));
      if (!done) {
        row.append(this.btn(`🔧 Assemble for ${set.payout}g`, () => {
          const pay = this.state.assemble(set.id);
          if (pay) {
            this.audio.discover();
            this.hooks.onAssembled(set.id);
            this.hooks.toast(`The ${set.name} is whole again! +${pay}g`);
          }
        }, 'small', !this.state.canAssemble(set.id)));
      }
      this.body.append(row);
    }
  }

  private renderBoard(): void {
    this.body.append(this.p('Sticky notes from the locals. Find and clean the item and it is delivered for you, with the reward paid at once.'));
    for (const q of QUEST_DEFS) {
      const done = this.state.data.questsDone.includes(q.id);
      const prog = this.state.questProgress(q);
      const note = document.createElement('div');
      note.className = `note ${done ? 'done' : ''}`;
      note.append(
        this.p(`“${q.text}”`),
        this.p(`— ${q.giver}   ·   Reward: ${q.reward}g   ·   ${done ? 'Already found ✓ reward paid' : `${prog}/${q.need.count} found`}`, 'faint'),
      );
      this.body.append(note);
    }
  }

  private renderUpgrades(): void {
    const keys: TierKey[] = ['detector', 'digging', 'cleaning', 'gear'];
    const grid = document.createElement('div');
    grid.className = 'upgrades';
    for (const k of keys) {
      const info = TIER_INFO[k];
      const cur = this.state.tier(k);
      const col = document.createElement('div');
      col.className = 'ucol';
      const h = document.createElement('h3');
      h.textContent = info.label;
      col.append(h);
      for (let t = 1; t <= 3; t++) {
        const c = document.createElement('div');
        c.className = `ucell ${t <= cur ? 'owned' : ''} ${t === cur ? 'current' : ''}`;
        const name = document.createElement('b');
        name.textContent = info.names[t - 1];
        c.append(name, this.p(info.desc[t - 1], 'faint'));
        if (t === cur + 1) {
          const cost = this.state.upgradeCost(k)!;
          c.append(this.btn(`Buy ${cost}g`, () => {
            if (this.state.buyUpgrade(k)) {
              this.audio.discover();
              this.hooks.toast(`Upgraded: ${info.names[t - 1]}`);
            } else {
              this.audio.error();
              this.hooks.toast(`You need ${cost}g for that.`);
            }
          }, 'small', this.state.data.coins < cost));
        } else if (t <= cur) {
          c.append(this.p(t === cur ? 'Equipped' : 'Owned', 'tag'));
        }
        col.append(c);
      }
      grid.append(col);
    }
    this.body.append(grid);

    const fun = document.createElement('div');
    fun.className = 'section';
    const fh = document.createElement('h3');
    fh.textContent = 'Shop extras';
    const cell = document.createElement('div');
    cell.className = `ucell ${this.state.data.ownsGun ? 'owned' : ''}`;
    const name = document.createElement('b');
    name.textContent = 'Toy Pistol';
    cell.append(name, this.p('Purely for fun: pop at crabs, seagulls and sharks. Pays nothing. Pick it with the scroll wheel once you own it.', 'faint'));
    if (this.state.data.ownsGun) {
      cell.append(this.p('Owned', 'tag'));
    } else {
      cell.append(this.btn(`Buy ${GUN_COST}g`, () => {
        if (this.state.buyGun()) {
          this.audio.discover();
          this.hooks.toast('You bought the Toy Pistol! Scroll to pick it up.');
        } else {
          this.audio.error();
          this.hooks.toast(`You need ${GUN_COST}g for that.`);
        }
      }, 'small', this.state.data.coins < GUN_COST));
    }
    const keyCell = document.createElement('div');
    keyCell.className = `ucell ${this.state.data.ownsCartKey ? 'owned' : ''}`;
    const keyName = document.createElement('b');
    keyName.textContent = 'Golf Cart Key';
    keyCell.append(keyName, this.p('The golf cart will not start without it. Press F beside the cart to drive it.', 'faint'));
    if (this.state.data.ownsCartKey) {
      keyCell.append(this.p('Owned', 'tag'));
    } else {
      keyCell.append(this.btn(`Buy ${CART_KEY_COST}g`, () => {
        if (this.state.buyCartKey()) {
          this.audio.discover();
          this.hooks.toast('You bought the golf cart key! Press F beside the cart.');
        } else {
          this.audio.error();
          this.hooks.toast(`You need ${CART_KEY_COST}g for that.`);
        }
      }, 'small', this.state.data.coins < CART_KEY_COST));
    }
    const boatCell = document.createElement('div');
    boatCell.className = `ucell ${this.state.data.ownsBoatKey ? 'owned' : ''}`;
    const boatName = document.createElement('b');
    boatName.textContent = 'Boat Key';
    boatCell.append(boatName, this.p('The boat will not start without it. Press F beside the boat, down at the shore, to take the helm.', 'faint'));
    if (this.state.data.ownsBoatKey) {
      boatCell.append(this.p('Owned', 'tag'));
    } else {
      boatCell.append(this.btn(`Buy ${BOAT_KEY_COST}g`, () => {
        if (this.state.buyBoatKey()) {
          this.audio.discover();
          this.hooks.toast('You bought the boat key! Head down to the shore.');
        } else {
          this.audio.error();
          this.hooks.toast(`You need ${BOAT_KEY_COST}g for that.`);
        }
      }, 'small', this.state.data.coins < BOAT_KEY_COST));
    }
    const bowCell = document.createElement('div');
    bowCell.className = `ucell ${this.state.data.ownsBow ? 'owned' : ''}`;
    const bowName = document.createElement('b');
    bowName.textContent = 'Hunting Bow';
    bowCell.append(bowName, this.p('Hold click to draw, release to shoot. Unlimited arrows. Pick it with the scroll wheel.', 'faint'));
    if (this.state.data.ownsBow) {
      bowCell.append(this.p('Owned', 'tag'));
    } else {
      bowCell.append(this.btn(`Buy ${BOW_COST}g`, () => {
        if (this.state.buyBow()) {
          this.audio.discover();
          this.hooks.toast('You bought the bow! Scroll to pick it up.');
        } else {
          this.audio.error();
          this.hooks.toast(`You need ${BOW_COST}g for that.`);
        }
      }, 'small', this.state.data.coins < BOW_COST));
    }
    const flagCell = document.createElement('div');
    flagCell.className = 'ucell';
    const flagName = document.createElement('b');
    flagName.textContent = 'Marker Flags';
    flagCell.append(
      flagName,
      this.p(`A pack of ${FLAG_PACK_SIZE} flags to plant wherever you like. They show on the mini-map and the island map. Press 7 or scroll to take them out; F beside a flag picks it up again.`, 'faint'),
      this.p(`In your bag: ${this.state.data.flags ?? 0}`, 'tag'),
    );
    flagCell.append(this.btn(`Buy ${FLAG_PACK_SIZE} flags · ${FLAG_PACK_COST}g`, () => {
      if (this.state.buyFlags()) {
        this.audio.discover();
        this.hooks.toast(`You bought ${FLAG_PACK_SIZE} flags! Press 7 to take them out.`);
      } else {
        this.audio.error();
        this.hooks.toast(`You need ${FLAG_PACK_COST}g for that.`);
      }
    }, 'small', this.state.data.coins < FLAG_PACK_COST));
    const diveCell = document.createElement('div');
    diveCell.className = `ucell ${this.state.data.ownsDiveGear ? 'owned' : ''}`;
    const diveName = document.createElement('b');
    diveName.textContent = 'Diving Gear';
    diveCell.append(
      diveName,
      this.p('A mask, snorkel and fins, and a harpoon gun. Stand near the water and press F to dive in, and Q to come up. Down there you can find starfish, shipwreck pieces and gold, and spear fish for 1g each. Only the harpoon works underwater.', 'faint'),
    );
    if (this.state.data.ownsDiveGear) {
      diveCell.append(this.p('Owned', 'tag'));
    } else {
      diveCell.append(this.btn(`Buy ${DIVE_COST}g`, () => {
        if (this.state.buyDiveGear()) {
          this.audio.discover();
          this.hooks.toast('You bought the diving gear! Wade to the water\u2019s edge and press F to dive in.');
        } else {
          this.audio.error();
          this.hooks.toast(`You need ${DIVE_COST}g for that.`);
        }
      }, 'small', this.state.data.coins < DIVE_COST));
    }
    const decorCells = DECOR.map((d) => {
      const owned = (this.state.data.decor ?? []).includes(d.id);
      const cell = document.createElement('div');
      cell.className = `ucell ${owned ? 'owned' : ''}`;
      const nm = document.createElement('b');
      nm.textContent = d.name;
      cell.append(nm, this.p(d.blurb, 'faint'));
      if (owned) cell.append(this.p('Placed in your camp', 'tag'));
      else {
        cell.append(this.btn(`Buy ${d.cost}g`, () => {
          if (this.state.buyDecor(d.id)) {
            this.audio.discover();
            this.hooks.toast(`${d.name} is up in your camp.`);
          } else {
            this.audio.error();
            this.hooks.toast(`You need ${d.cost}g for that.`);
          }
        }, 'small', this.state.data.coins < d.cost));
      }
      return cell;
    });
    const bucketCell = document.createElement('div');
    bucketCell.className = `ucell ${this.state.data.ownsBucket ? 'owned' : ''}`;
    const bucketName = document.createElement('b');
    bucketName.textContent = 'Bucket and Spade';
    bucketCell.append(
      bucketName,
      this.p('Build sandcastles on the beach. Click the sand to start a mound and click it again to add walls, towers and a flag. Right-click knocks a castle down. Rain and the shore wear them away. Press 8 or scroll to take it out.', 'faint'),
    );
    if (this.state.data.ownsBucket) {
      bucketCell.append(this.p('Owned', 'tag'));
    } else {
      bucketCell.append(this.btn(`Buy ${BUCKET_COST}g`, () => {
        if (this.state.buyBucket()) {
          this.audio.discover();
          this.hooks.toast('You bought the bucket! Press 8 to take it out and build.');
        } else {
          this.audio.error();
          this.hooks.toast(`You need ${BUCKET_COST}g for that.`);
        }
      }, 'small', this.state.data.coins < BUCKET_COST));
    }
    const boneCell = document.createElement('div');
    boneCell.className = 'ucell';
    const boneName = document.createElement('b');
    boneName.textContent = 'Dog Bone';
    boneCell.append(
      boneName,
      this.p(`Give a bone to the camp dog (F beside it) and it runs off to sniff out a buried coin or piece of trash, then barks until you dig it up. Without a bone the dog will not go. One bone per trip. The shop only has ${BONE_STOCK} bones to sell in a whole game, so use them wisely.`, 'faint'),
      this.p(`In your bag: ${this.state.data.bones ?? 0} · Left in the shop: ${this.state.bonesLeft} of ${BONE_STOCK}`, 'tag'),
    );
    const soldOut = this.state.bonesLeft <= 0;
    boneCell.append(this.btn(soldOut ? 'Sold out' : `Buy a bone · ${BONE_COST}g`, () => {
      if (this.state.buyBone()) {
        this.audio.discover();
        this.hooks.toast('You bought a bone! Give it to the dog at camp.');
      } else {
        this.audio.error();
        this.hooks.toast(`You need ${BONE_COST}g for that.`);
      }
    }, 'small', soldOut || this.state.data.coins < BONE_COST));
    fun.append(fh, keyCell, boatCell, bowCell, flagCell, bucketCell, diveCell, ...decorCells, boneCell, cell);
    this.body.append(fun);
  }

  /** Only there once the gold has been found. */
  private renderLuxury(): void {
    const grid = document.createElement('div');
    grid.className = 'section';
    const head = document.createElement('h4');
    head.textContent = 'Things the Marigold\u2019s gold can buy';
    grid.append(head);
    for (const l of LUXURY) {
      const owned = this.state.data.luxury.includes(l.id);
      const cell = document.createElement('div');
      cell.className = `ucell ${owned ? 'owned' : ''}`;
      const name = document.createElement('b');
      name.textContent = l.name;
      cell.append(name, this.p(l.desc));
      if (owned) cell.append(this.p('Yours', 'tag'));
      else {
        cell.append(this.btn(`Buy · ${l.cost}g`, () => {
          if (this.state.buyLuxury(l.id)) {
            this.audio.discover();
            this.hooks.onLuxury();
            this.hooks.toast(`${l.name} is yours.`);
          } else this.audio.error();
          this.render();
        }, 'small', this.state.data.coins < l.cost));
      }
      grid.append(cell);
    }
    this.body.append(grid);
  }

  private renderShelf(): void {
    const d = this.state.data;
    this.body.append(this.p('Assembled antiques sit on the shelf at your workbench.'));
    for (const set of SHARD_SETS) {
      if (d.assembled.includes(set.id)) {
        const n = document.createElement('div');
        n.className = 'note';
        n.append(this.p(`🏺 ${set.name}`), this.p(set.lore, 'faint'));
        this.body.append(n);
      }
    }
    if (d.assembled.length === 0) this.body.append(this.p('Empty so far.', 'faint'));
    const h = document.createElement('h3');
    h.textContent = `Discoveries: ${Object.keys(d.found).length} / ${ITEM_DEFS.length} kinds`;
    this.body.append(h);
    const list = document.createElement('div');
    list.className = 'list';
    for (const [id, n] of Object.entries(d.found)) {
      const def = DEF_BY_ID.get(id);
      if (def) list.append(this.row(def.name, `×${n}`));
    }
    this.body.append(list);
    const inv = d.inventory.filter((i) => i.category !== 'trash');
    if (inv.length) {
      const hh = document.createElement('h3');
      hh.textContent = 'In your backpack';
      this.body.append(hh);
      const l2 = document.createElement('div');
      l2.className = 'list';
      for (const i of inv) l2.append(this.row(i.name, `${i.value}g`));
      this.body.append(l2);
    }
  }
}
