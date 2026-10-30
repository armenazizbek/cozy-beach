import type { StateManager } from '../core/StateManager';
import { TIER_INFO } from '../core/StateManager';
import { timeLabel } from '../core/SaveGame';
import type { GameSettings } from '../core/Settings';
import { HELP_TOPICS } from '../core/Help';
import { pad, padText } from './PadText';
import type { JournalPage } from '../core/Journal';
import type { Letter } from '../core/Mail';
import type { SaveMeta } from '../core/SaveGame';

const MINI_PX = 150;

export type UIMode = 'EXPLORE' | 'INSPECT' | 'CAMP' | 'MENU';

export interface UIHooks {
  onTool: (n: 1 | 2 | 3 | 4 | 5 | 6) => void;
  onInstantClean: () => void;
  onInspectBack: () => void;
  onContinue: () => void;
  onMute: () => boolean;
}

export interface SaveHooks {
  list: () => SaveMeta[];
  /** saves as a new entry, or over `overwriteId`; returns the entry or null on failure */
  save: (name: string, overwriteId?: string) => SaveMeta | null;
  load: (id: string) => void;
  remove: (id: string) => void;
}

export interface MenuHooks {
  hasSaves: boolean;
  onAchievements: () => void;
  onNew: () => void;
  onLoad: () => void;
  onSettings: () => void;
  onExit: () => void;
}

export interface PauseHooks {
  onAchievements: () => void;
  onResume: () => void;
  onNew: () => void;
  onSettings: () => void;
  onMenu: () => void;
  saves: SaveHooks;
}

const el = <K extends keyof HTMLElementTagNameMap>(tag: K, cls = '', html = ''): HTMLElementTagNameMap[K] => {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (html) e.innerHTML = html;
  return e;
};

export class UIManager {
  readonly waveCanvas = el('canvas', 'wave');
  readonly waveCtx: CanvasRenderingContext2D;
  private root: HTMLElement;
  private coins = el('span', 'val');
  private warmFill = el('div', 'fill');
  private warmText = el('span', 'val');
  private mugName = el('span', 'sub');
  private pack = el('span', 'val');
  private eco = el('span', 'val');
  private bones = el('span', 'val');
  private boneChip = el('div', 'chip hidden', '🦴 ');
  private badge = el('div', 'tool-badge');
  private badgeIcon = el('span', 'tb-ico');
  private badgeName = el('span', 'tb-name');
  /** the controller's d-pad tool picker, shown instead of the badge while a controller is in use */
  private padTools = el('div', 'padtools hidden');
  private padSlots: Record<'up' | 'right' | 'down' | 'left', HTMLElement> = {
    up: el('div', 'pt up'),
    right: el('div', 'pt right'),
    down: el('div', 'pt down'),
    left: el('div', 'pt left'),
  };
  private padCaption = el('div', 'pt-caption');
  private curTool: number = 1;
  private badgeOverride = false;
  private hint = el('div', 'hint');
  private toasts = el('div', 'toasts');
  private fadeEl = el('div', 'fade');
  private hud = el('div', 'hud');
  private scope = el('div', 'scope');
  private inspect = el('div', 'inspect hidden');
  private inspectTitle = el('div', 'inspect-title');
  private progFill = el('div', 'fill');
  private progText = el('span', 'val');
  private instantBtn = el('button', 'btn', 'Instant Clean');
  private result = el('div', 'result hidden');
  private intro = el('div', 'intro');
  private cross = el('div', 'cross');
  private drawbar = el('div', 'drawbar hidden');
  private drawFill = el('div', 'fill');
  private binoc = el('canvas', 'binoc hidden');
  private binocKey = '';
  readonly miniCanvas = el('canvas', 'minimap');
  private mapEl: HTMLElement | null = null;
  private mapToggle: (() => void) | null = null;
  private mapRepaint: (() => void) | null = null;
  private clock = el('div', 'chip clock');
  private prompt = el('div', 'prompt');
  private prompt2 = el('div', 'prompt p2');
  private prompt2Text = el('span', 'txt');
  private promptText = el('span', 'txt');
  private muteBtn =el('button', 'chip mute', '🔊');

  constructor(root: HTMLElement, private state: StateManager, private hooks: UIHooks) {
    this.root = root;
    this.waveCanvas.width = 240;
    this.waveCanvas.height = 84;
    this.waveCtx = this.waveCanvas.getContext('2d')!;
    this.buildHud();
    this.buildInspect();
    this.kbdF = el('span', 'kbd', 'F');
    this.kbdE = el('span', 'kbd', 'E');
    this.prompt.append(this.kbdF, this.promptText);
    this.prompt2.append(this.kbdE, this.prompt2Text);
    this.drawbar.append(this.drawFill);
    root.append(this.binoc, this.cross, this.drawbar, this.prompt, this.prompt2, this.hud, this.scope, this.hint, this.toasts, this.inspect, this.result, this.fadeEl, this.intro);
    state.subscribe(() => this.refresh());
    this.refresh();
  }

  private buildHud(): void {
    const left = el('div', 'panel stats');
    const coinRow = el('div', 'row', '<span class="ico">🪙</span>');
    coinRow.append(this.coins);
    const warmRow = el('div', 'row col');
    const head = el('div', 'row between', '<span class="ico">☕</span>');
    head.append(this.mugName, this.warmText);
    const bar = el('div', 'bar');
    bar.append(this.warmFill);
    warmRow.append(head, bar);
    left.append(coinRow, warmRow);

    const right = el('div', 'right-chips');
    const packChip = el('div', 'chip', '🎒 ');
    packChip.append(this.pack);
    packChip.title = 'Backpack';
    const ecoChip = el('div', 'chip', '🌿 ');
    ecoChip.append(this.eco);
    ecoChip.title = 'Beach Ecology Score';
    this.muteBtn.title = 'Mute (N)';
    this.muteBtn.onclick = (): void => {
      this.muteBtn.textContent = this.hooks.onMute() ? '🔇' : '🔊';
    };
    this.clock.title = 'Time of day and the day count';
    this.boneChip.append(this.bones);
    this.boneChip.title = 'Dog bones';
    right.append(this.clock, packChip, ecoChip, this.boneChip, this.muteBtn);

    this.badge.append(this.badgeIcon, this.badgeName);
    this.miniCanvas.width = this.miniCanvas.height = MINI_PX;
    const center = el('div', 'pt-center');
    center.innerHTML = '<i></i><i></i>';
    this.padTools.append(this.padSlots.up, this.padSlots.left, center, this.padSlots.right, this.padSlots.down, this.padCaption);
    this.hud.append(left, right, this.miniCanvas, this.badge, this.padTools);

    this.scope.append(el('div', 'scope-title', 'DETECTOR SIGNAL'), this.waveCanvas);
  }

  private buildInspect(): void {
    const prog = el('div', 'bar big');
    prog.append(this.progFill);
    const row = el('div', 'row between');
    row.append(el('span', 'sub', 'Cleanliness'), this.progText);
    const back = el('button', 'btn ghost', 'Back (Esc)');
    back.onclick = (): void => this.hooks.onInspectBack();
    this.instantBtn.onclick = (): void => this.hooks.onInstantClean();
    const btns = el('div', 'row gap');
    btns.append(back, this.instantBtn);
    this.inspect.append(
      this.inspectTitle,
      el('div', 'inspect-help', 'Drag with the left mouse to scrub · Right-drag or Q / E / arrows to rotate'),
      prog,
      row,
      btns,
    );
  }

  refresh(): void {
    this.updatePadTools();
    const d = this.state.data;
    this.coins.textContent = `${d.coins}g`;
    const max = this.state.maxWarmth;
    this.warmFill.style.width = `${(d.warmth / max) * 100}%`;
    this.warmFill.classList.toggle('low', d.warmth / max < 0.25);
    this.warmText.textContent = `${Math.round(d.warmth)} / ${max}`;
    this.mugName.textContent = TIER_INFO.gear.names[this.state.tier('gear') - 1];
    this.pack.textContent = `${d.inventory.length}`;
    this.eco.textContent = `${d.ecology}`;
    this.bones.textContent = `${d.bones ?? 0}`;
    this.boneChip.classList.toggle('hidden', (d.bones ?? 0) <= 0);
    this.instantBtn.classList.toggle('hidden', this.state.tier('cleaning') < 3);
  }

  setTool(n: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 0): void {
    const info: Record<number, [string, string]> = {
      1: ['📡', 'Detector'],
      2: ['⛏️', 'Trowel'],
      3: ['⛺', 'Camp'],
      4: ['🔫', 'Gun'],
      5: ['🔭', 'Binoculars'],
      6: ['🏹', 'Bow'],
      7: ['🚩', 'Flags'],
      8: ['🪣', 'Bucket'],
    };
    this.curTool = n;
    this.badgeOverride = false;
    this.badge.classList.toggle('hidden', n === 0 || pad.on);
    this.updatePadTools();
    if (n !== 0) {
      this.badgeIcon.textContent = info[n][0];
      this.badgeName.textContent = n === 7 ? `Flags x${this.state.data.flags ?? 0}` : info[n][1];
    }
    this.scope.classList.toggle('hidden', n !== 1);
  }

  /** Tools that share the d-pad's left slot: flags (while any are left), the bow and the gun. */
  padOtherTools(): (8 | 7 | 6 | 4)[] {
    const d = this.state.data;
    const list: (8 | 7 | 6 | 4)[] = [];
    if ((d.flags ?? 0) > 0) list.push(7);
    if (d.ownsBucket) list.push(8);
    if (d.ownsBow) list.push(6);
    if (d.ownsGun) list.push(4);
    return list;
  }

  /** Draws the d-pad: a tool on each side, the one in hand lit up. */
  private updatePadTools(): void {
    const show = pad.on && this.curTool !== 0 && !this.badgeOverride;
    this.padTools.classList.toggle('hidden', !show);
    if (!show) return;
    const icons: Record<number, [string, string]> = {
      1: ['📡', 'Detector'], 2: ['⛏️', 'Trowel'], 5: ['🔭', 'Binoculars'], 6: ['🏹', 'Bow'], 4: ['🔫', 'Gun'], 7: ['🚩', 'Flags'], 8: ['🪣', 'Bucket'],
    };
    const others = this.padOtherTools();
    const leftTool = others.includes(this.curTool as 8 | 7 | 6 | 4) ? (this.curTool as 8 | 7 | 6 | 4) : others[0];
    const slotTool: Record<'up' | 'right' | 'down' | 'left', number | undefined> = { up: 1, right: 2, down: 5, left: leftTool };
    (Object.keys(this.padSlots) as ('up' | 'right' | 'down' | 'left')[]).forEach((k) => {
      const t = slotTool[k];
      const slot = this.padSlots[k];
      slot.textContent = t ? icons[t][0] : '·';
      if (t === 7) slot.append(el('small', '', `x${this.state.data.flags ?? 0}`));
      if (k === 'left' && others.length > 1) slot.append(el('em', '', '⟳'));
      slot.classList.toggle('on', t === this.curTool);
      slot.classList.toggle('empty', !t);
    });
    this.padCaption.textContent = icons[this.curTool]?.[1] ? (this.curTool === 7 ? `Flags x${this.state.data.flags ?? 0}` : icons[this.curTool][1]) : '';
  }

  setMode(mode: UIMode): void {
    this.hud.classList.toggle('hidden', mode === 'INSPECT' || mode === 'MENU');
    this.scope.classList.toggle('dim', mode === 'CAMP');
    if (mode === 'INSPECT' || mode === 'MENU') this.scope.classList.add('hidden');
    document.body.dataset.mode = mode;
  }

  showPause(on: boolean, hooks?: PauseHooks): void {
    if (!on) {
      this.pauseEl?.remove();
      this.pauseEl = null;
      this.closeSaves();
      return;
    }
    if (this.pauseEl || !hooks) return;
    const p = el('div', 'pause');
    p.innerHTML = `
      <div class="intro-card">
        <h1>Paused</h1>
        <p class="tag">Take your time. The beach will wait.</p>
        ${pad.on ? `<div class="keys">
          <div class="krow"><b>Left stick</b><span>Walk (press it in to hurry)</span></div>
          <div class="krow"><b>Right stick</b><span>Look around</span></div>
          <div class="krow"><b>LB / RB</b><span>Swap tool</span></div>
          <div class="krow"><b>RT</b><span>Use the tool in your hands</span></div>
          <div class="krow"><b>LT</b><span>Lower the binoculars, or let the bow string down</span></div>
          <div class="krow"><b>A</b><span>Use what is next to you (cart, boat, camp)</span></div>
          <div class="krow"><b>X</b><span>Step off the boat</span></div>
          <div class="krow"><b>Y</b><span>Open the island map</span></div>
          <div class="krow"><b>D-pad</b><span>Detector, trowel, binoculars, flags</span></div>
          <div class="krow"><b>Back</b><span>Mute sound</span></div>
          <div class="krow"><b>Start</b><span>Resume</span></div>
        </div>` : `<div class="keys">
          <div class="krow"><b>WASD</b><span>Walk</span></div>
          <div class="krow"><b>Shift</b><span>Hurry</span></div>
          <div class="krow"><b>Mouse</b><span>Look around</span></div>
          <div class="krow"><b>Scroll</b><span>Swap tool (the gun once you buy it)</span></div>
          <div class="krow"><b>Click</b><span>Use the tool in your hands</span></div>
          <div class="krow"><b>Binoculars</b><span>Scroll to them, click to zoom</span></div>
          <div class="krow"><b>Bow</b><span>Hold click to draw, release to shoot</span></div>
          <div class="krow"><b>7</b><span>Marker flags: click to plant, F to pick up</span></div>
          <div class="krow"><b>F</b><span>Use what is next to you</span></div>
          <div class="krow"><b>Cart</b><span>F beside the golf cart to drive it</span></div>
          <div class="krow"><b>M</b><span>Open the island map</span></div>
          <div class="krow"><b>N</b><span>Mute sound</span></div>
          <div class="krow"><b>Esc</b><span>Resume</span></div>
        </div>`}
        <div class="pause-actions"></div>
        <p class="save-info"></p>
      </div>`;
    const actions = p.querySelector('.pause-actions') as HTMLElement;
    const info = p.querySelector('.save-info') as HTMLElement;

    const mainButtons = (): void => {
      const count = hooks.saves.list().length;
      info.textContent = count === 0 ? 'No saved games yet' : `${count} saved game${count === 1 ? '' : 's'}`;
      actions.innerHTML = '';
      const resume = el('button', 'btn big', 'Resume');
      resume.onclick = (): void => hooks.onResume();
      const row = el('div', 'row gap');
      const save = el('button', 'btn ghost', '💾 Save game');
      save.onclick = (): void => this.openSaves('save', hooks.saves, mainButtons);
      const load = el('button', 'btn ghost', '📂 Load game');
      load.disabled = count === 0;
      load.onclick = (): void => this.openSaves('load', hooks.saves, mainButtons);
      const fresh = el('button', 'btn ghost', '✨ New game');
      fresh.onclick = (): void => confirmStep('Start a brand new game? Anything you have not saved will be lost.', 'Yes, start over', hooks.onNew);
      const settings = el('button', 'btn ghost', '⚙ Settings');
      settings.onclick = (): void => hooks.onSettings();
      const menu = el('button', 'btn ghost', '🏠 Main menu');
      menu.onclick = (): void => confirmStep('Return to the main menu? Anything you have not saved will be lost.', 'Yes, go to the menu', hooks.onMenu);
      row.append(save, load, settings);
      const row2 = el('div', 'row gap');
      const ach = el('button', 'btn ghost', '🏆 Achievements');
      ach.onclick = (): void => hooks.onAchievements();
      const help = el('button', 'btn ghost', '❓ Help');
      help.onclick = (): void => this.showHelp();
      row2.append(help, ach, fresh, menu);
      actions.append(resume, row, row2);
    };
    const confirmStep = (question: string, yes: string, run: () => void): void => {
      actions.innerHTML = '';
      const q = el('p', 'confirm-q', question);
      const row = el('div', 'row gap');
      const ok = el('button', 'btn', yes);
      ok.onclick = (): void => run();
      const no = el('button', 'btn ghost', 'Cancel');
      no.onclick = (): void => mainButtons();
      row.append(ok, no);
      actions.append(q, row);
    };
    mainButtons();
    this.pauseEl = p;
    this.root.append(p);
  }

  private sleepEl: HTMLElement | null = null;
  private bookEl: HTMLElement | null = null;
  private bookTurnFn: ((dir: number) => void) | null = null;
  private menuEl: HTMLElement | null = null;
  private settingsEl: HTMLElement | null = null;

  /** The main menu: buttons on the left, the living beach behind. */
  showMainMenu(h: MenuHooks): void {
    this.hideMainMenu();
    const m = el('div', 'mainmenu');
    const panel = el('div', 'mm-panel');
    panel.append(el('div', 'mm-kicker', 'a quiet little adventure'), el('h1', 'mm-title', 'Cozy Treasure Hunter'), el('p', 'mm-tag', 'Misty mornings. Buried treasure. Nothing to rush.'));
    const list = el('div', 'mm-buttons');
    const mk = (icon: string, label: string, fn: () => void, disabled = false): HTMLButtonElement => {
      const b = el('button', 'mm-btn');
      b.innerHTML = `<span class="mm-ico">${icon}</span><span>${label}</span>`;
      b.disabled = disabled;
      b.onclick = fn;
      list.append(b);
      return b;
    };
    mk('🏝️', 'New Game', h.onNew);
    mk('📂', 'Load Game', h.onLoad, !h.hasSaves);
    mk('⚙️', 'Settings', h.onSettings);
    mk('❓', 'Help', () => this.showHelp());
    mk('🏆', 'Achievements', h.onAchievements);
    mk('🎞️', 'Credits', () => this.showCredits());
    mk('🚪', 'Exit Game', h.onExit);
    panel.append(list);
    m.append(panel);
    this.menuEl = m;
    this.root.append(m);
  }

  hideMainMenu(): void {
    this.menuEl?.remove();
    this.menuEl = null;
  }

  /** Sliders and switches. Every change is passed straight back so it takes effect at once. */
  openSettings(settings: GameSettings, onChange: (s: GameSettings) => void, onClose?: () => void): void {
    this.closeSettings();
    const wrap = el('div', 'pause saves');
    const card = el('div', 'intro-card save-card settings-card');
    card.append(el('h1', '', 'Settings'));
    const rows = el('div', 'set-rows');
    const slider = (label: string, key: 'master' | 'music' | 'ambience' | 'sensitivity', min: number, max: number, step: number, fmt: (v: number) => string): void => {
      const row = el('label', 'set-row');
      const name = el('span', 'set-name', label);
      const input = el('input', 'set-range');
      input.type = 'range';
      input.min = String(min);
      input.max = String(max);
      input.step = String(step);
      input.value = String(settings[key]);
      const val = el('span', 'set-val', fmt(settings[key]));
      input.oninput = (): void => {
        settings[key] = Number(input.value);
        val.textContent = fmt(settings[key]);
        onChange(settings);
      };
      row.append(name, input, val);
      rows.append(row);
    };
    const select = (label: string, key: 'displayMode' | 'windowSize', options: [string, string][]): void => {
      const row = el('label', 'set-row');
      const sel = el('select', 'set-select');
      for (const [value, text] of options) {
        const o = el('option', '', text);
        o.value = value;
        sel.append(o);
      }
      sel.value = settings[key];
      sel.onchange = (): void => {
        (settings as unknown as Record<string, string>)[key] = sel.value;
        onChange(settings);
      };
      row.append(el('span', 'set-name', label), sel);
      rows.append(row);
    };
    const toggle = (label: string, hint: string, key: 'shadows' | 'sharp'): void => {
      const row = el('label', 'set-row');
      const name = el('span', 'set-name', label);
      const box = el('input', 'set-check');
      box.type = 'checkbox';
      box.checked = settings[key];
      box.onchange = (): void => {
        settings[key] = box.checked;
        onChange(settings);
      };
      const h = el('span', 'set-val faint', hint);
      row.append(name, box, h);
      rows.append(row);
    };
    const pct = (v: number): string => `${Math.round(v * 100)}%`;
    rows.append(el('h3', '', 'Sound'));
    slider('Master volume', 'master', 0, 1, 0.05, pct);
    slider('Radio music', 'music', 0, 1, 0.05, pct);
    slider('Sea and wind', 'ambience', 0, 1, 0.05, pct);
    rows.append(el('h3', '', 'Controls'));
    slider('Mouse look speed', 'sensitivity', 0.4, 2.5, 0.05, (v) => `${v.toFixed(2)}x`);
    const desktop = (window as unknown as { desktop?: unknown }).desktop !== undefined;
    rows.append(el('h3', '', 'Display'));
    select('Screen mode', 'displayMode', [
      ['fullscreen', 'Fullscreen'],
      ['windowed', 'Windowed'],
      ...(desktop ? ([['borderless', 'Windowed, no border']] as [string, string][]) : []),
    ]);
    if (desktop) {
      const sizes = [[1280, 720], [1366, 768], [1600, 900], [1920, 1080], [2560, 1440], [3840, 2160]].filter(([w, h]) => w <= window.screen.width && h <= window.screen.height);
      select('Window size', 'windowSize', sizes.map(([w, h]) => [`${w}x${h}`, `${w} × ${h}`] as [string, string]));
    }
    rows.append(el('h3', '', 'Graphics'));
    toggle('Shadows', 'turn off for a smoother game on slower computers', 'shadows');
    toggle('Sharp picture', 'full screen resolution, heavier to draw', 'sharp');
    const back = el('button', 'btn', 'Done');
    back.onclick = (): void => {
      this.closeSettings();
      onClose?.();
    };
    card.append(rows, back);
    wrap.append(card);
    this.settingsEl = wrap;
    this.root.append(wrap);
  }

  closeSettings(): void {
    this.settingsEl?.remove();
    this.settingsEl = null;
  }

  /** How to play: tabs of short topics, from what the game is about to the controls and saving. */
  showHelp(): void {
    const wrap = el('div', 'pause helpscreen');
    const card = el('div', 'intro-card help-card');
    card.append(el('h1', '', 'Help'));
    const tabs = el('div', 'help-tabs');
    const body = el('div', 'help-body');
    const buttons = HELP_TOPICS.map((t, i) => {
      const b = el('button', `btn small ghost${i === 0 ? ' on' : ''}`, `${t.icon} ${t.title}`);
      b.onclick = (): void => show(i);
      tabs.append(b);
      return b;
    });
    const show = (i: number): void => {
      buttons.forEach((b, k) => b.classList.toggle('on', k === i));
      body.replaceChildren();
      body.scrollTop = 0;
      for (const blk of HELP_TOPICS[i].blocks) {
        if (blk.h) body.append(el('h3', '', blk.h));
        if (blk.p) body.append(el('p', 'help-p', blk.p));
        if (blk.list) {
          const ul = el('ul', 'help-list');
          for (const item of blk.list) ul.append(el('li', '', item));
          body.append(ul);
        }
        if (blk.keys) {
          const keys = el('div', 'keys');
          for (const [k, v] of blk.keys) {
            const row = el('div', 'krow');
            row.append(el('b', '', k), el('span', '', v));
            keys.append(row);
          }
          body.append(keys);
        }
      }
    };
    show(0);
    const back = el('button', 'btn big', 'Back');
    back.onclick = (): void => wrap.remove();
    card.append(tabs, body, back);
    wrap.append(card);
    this.root.append(wrap);
  }

  /** Every achievement, with the ones already earned lit up. */
  showAchievements(list: { name: string; desc: string; icon: string; done: boolean; secret: boolean }[]): void {
    const wrap = el('div', 'pause achievements');
    const card = el('div', 'intro-card ach-card');
    const done = list.filter((a) => a.done).length;
    card.append(el('h1', '', 'Achievements'), el('p', 'tag', `${done} of ${list.length} earned`));
    const grid = el('div', 'ach-list');
    for (const a of list) {
      const row = el('div', `ach-row${a.done ? ' done' : ''}`);
      const hidden = a.secret && !a.done;
      row.append(el('span', 'ach-icon', hidden ? '❔' : a.icon));
      const text = el('div', 'ach-text');
      text.append(el('b', '', hidden ? 'Secret' : a.name), el('span', '', hidden ? 'Keep exploring to find out.' : a.desc));
      row.append(text);
      grid.append(row);
    }
    const back = el('button', 'btn big', 'Back');
    back.onclick = (): void => wrap.remove();
    card.append(grid, back);
    wrap.append(card);
    this.root.append(wrap);
  }

  /** The credits: who made the game. Opened from the main menu. */
  showCredits(): void {
    const wrap = el('div', 'pause credits');
    const card = el('div', 'intro-card');
    card.append(
      el('h1', '', 'Credits'),
      el('p', 'tag', 'Cozy Treasure Hunter'),
      el('p', 'credit', 'Made by Armen Azizbekyan, a solo developer and the creator of this game.'),
      el('p', 'credit', 'Thank you for playing.'),
      el('p', 'save-note', `Version ${__APP_VERSION__} · ${__BUILD_YEAR__}`),
    );
    const back = el('button', 'btn big', 'Back');
    back.onclick = (): void => wrap.remove();
    card.append(back);
    wrap.append(card);
    this.root.append(wrap);
  }

  /** What "Exit Game" shows: a web page cannot always close itself, so say goodbye nicely. */
  showFarewell(onBack: () => void): void {
    const wrap = el('div', 'pause farewell');
    const card = el('div', 'intro-card');
    card.append(el('h1', '', 'See you on the beach'), el('p', 'tag', 'Thanks for playing. Your saved games are kept for next time.'), el('p', 'save-note', 'You can close this tab now.'));
    const back = el('button', 'btn big', 'Back to the menu');
    back.onclick = (): void => {
      wrap.remove();
      onBack();
    };
    card.append(back);
    wrap.append(card);
    this.root.append(wrap);
  }

  /** The journal: two pages side by side (one on a narrow screen) with buttons to turn them. */
  showBook(pages: (JournalPage & { isNew: boolean; found: boolean })[], onClose: () => void): void {
    this.hideBook();
    const wrap = el('div', 'pause bookview');
    const book = el('div', 'book');
    const spread = el('div', 'book-spread');
    const nav = el('div', 'book-nav');
    const prev = el('button', 'btn ghost small', '‹ Previous');
    prev.dataset.act = 'prev';
    const count = el('span', 'book-count');
    const next = el('button', 'btn ghost small', 'Next ›');
    next.dataset.act = 'next';
    const close = el('button', 'btn small', padText('Close (Esc)'));
    close.dataset.kb = 'Close (Esc)';
    close.onclick = (): void => onClose();
    nav.append(prev, count, next, close);
    book.append(spread, nav);
    wrap.append(book);
    const per = window.innerWidth < 760 ? 1 : 2;
    let first = Math.max(0, pages.findIndex((p) => p.isNew));
    first -= first % per;
    const render = (): void => {
      spread.replaceChildren();
      for (let k = 0; k < per; k++) {
        const p = pages[first + k];
        const side = el('div', `book-page ${k === 0 ? 'left' : 'right'}`);
        if (p) {
          const head = el('div', 'bp-head');
          head.append(el('span', 'bp-group', `${p.icon}  ${p.group}`));
          if (p.isNew) head.append(el('span', 'bp-new', 'New'));
          else if (!p.found) head.append(el('span', 'bp-todo', 'To discover'));
          side.append(head, el('h2', '', p.title), el('p', '', p.text), el('div', 'bp-num', String(first + k + 1)));
        }
        spread.append(side);
      }
      const last = Math.min(pages.length, first + per);
      count.textContent = pages.length ? `${first + 1}${last > first + 1 ? '–' + last : ''} of ${pages.length}` : 'No pages yet';
      prev.disabled = first <= 0;
      next.disabled = first + per >= pages.length;
    };
    const turn = (dir: number): void => {
      const n = first + dir * per;
      if (n < 0 || n >= pages.length) return;
      first = n;
      render();
      if (document.activeElement === prev || document.activeElement === next) {
        // keep the focus on a button that still works, so a controller can keep turning
        if ((document.activeElement as HTMLButtonElement).disabled) (dir > 0 ? prev : next).focus();
      }
    };
    prev.onclick = (): void => turn(-1);
    next.onclick = (): void => turn(1);
    this.bookTurnFn = turn;
    render();
    this.bookEl = wrap;
    this.root.append(wrap);
  }

  /** The camp post: one letter at a time on cream notepaper, newest first. */
  showMail(letters: (Letter & { isNew: boolean })[], onClose: () => void): void {
    this.hideBook();
    const wrap = el('div', 'pause bookview mailview');
    const card = el('div', 'mail');
    const sheet = el('div', 'mail-sheet');
    const nav = el('div', 'book-nav');
    const prev = el('button', 'btn ghost small', '‹ Newer');
    prev.dataset.act = 'prev';
    const count = el('span', 'book-count');
    const next = el('button', 'btn ghost small', 'Older ›');
    next.dataset.act = 'next';
    const close = el('button', 'btn small', padText('Close (Esc)'));
    close.dataset.kb = 'Close (Esc)';
    close.onclick = (): void => onClose();
    nav.append(prev, count, next, close);
    card.append(sheet, nav);
    wrap.append(card);
    // newest letters first
    const list = [...letters].reverse();
    let i = Math.max(0, list.findIndex((l) => l.isNew));
    const render = (): void => {
      sheet.replaceChildren();
      const l = list[i];
      if (!l) {
        sheet.append(el('p', 'mail-empty', 'No letters yet. Return something to its owner and they will write to you.'));
        count.textContent = 'Empty post box';
      } else {
        const head = el('div', 'mail-head');
        head.append(el('span', 'mail-from', `From: ${l.from}`));
        if (l.isNew) head.append(el('span', 'bp-new', 'New'));
        head.append(el('span', 'mail-stamp', '✉'));
        sheet.append(head, el('p', 'mail-greeting', l.greeting), el('p', 'mail-body', l.text), el('p', 'mail-sign', l.sign));
        count.textContent = `Letter ${i + 1} of ${list.length}`;
      }
      prev.disabled = i <= 0;
      next.disabled = i >= list.length - 1;
    };
    const turn = (dir: number): void => {
      // left (dir -1) means a newer letter, right an older one
      const n = i + dir;
      if (n < 0 || n >= list.length) return;
      i = n;
      render();
      if ((document.activeElement === prev || document.activeElement === next) && (document.activeElement as HTMLButtonElement).disabled) (dir > 0 ? prev : next).focus();
    };
    prev.onclick = (): void => turn(-1);
    next.onclick = (): void => turn(1);
    this.bookTurnFn = turn;
    render();
    this.bookEl = wrap;
    this.root.append(wrap);
  }

  /** The end of part one. Closing it returns to the game. */
  showTeaser(onClose: () => void): void {
    this.hideBook();
    const wrap = el('div', 'pause bookview teaser');
    const card = el('div', 'intro-card');
    card.append(
      el('h1', '', 'To be continued\u2026'),
      el('p', 'tag', 'Part 2'),
      el('p', 'credit', 'The door will open in the next part of the adventure.'),
    );
    const back = el('button', 'btn big', 'Back');
    back.onclick = (): void => onClose();
    card.append(back);
    wrap.append(card);
    this.bookEl = wrap;
    this.root.append(wrap);
  }

  /** A handwritten journal read page by page, oldest entry first. Shares the post's paper look. */
  showStory(title: string, pages: { heading: string; text: string; sign: string }[], onClose: () => void): void {
    this.hideBook();
    const wrap = el('div', 'pause bookview mailview');
    const card = el('div', 'mail');
    const sheet = el('div', 'mail-sheet');
    const nav = el('div', 'book-nav');
    const prev = el('button', 'btn ghost small', '\u2039 Earlier');
    prev.dataset.act = 'prev';
    const count = el('span', 'book-count');
    const next = el('button', 'btn ghost small', 'Later \u203a');
    next.dataset.act = 'next';
    const close = el('button', 'btn small', padText('Close (Esc)'));
    close.dataset.kb = 'Close (Esc)';
    close.onclick = (): void => onClose();
    nav.append(prev, count, next, close);
    card.append(sheet, nav);
    wrap.append(card);
    let i = 0;
    const render = (): void => {
      sheet.replaceChildren();
      const p = pages[i];
      const head = el('div', 'mail-head');
      head.append(el('span', 'mail-from', title), el('span', 'mail-stamp', '\ud83d\udcd6'));
      sheet.append(head, el('p', 'mail-greeting', p.heading), el('p', 'mail-body', p.text), el('p', 'mail-sign', p.sign));
      count.textContent = `Page ${i + 1} of ${pages.length}`;
      prev.disabled = i <= 0;
      next.disabled = i >= pages.length - 1;
    };
    const turn = (dir: number): void => {
      const n = i + dir;
      if (n < 0 || n >= pages.length) return;
      i = n;
      render();
      if ((document.activeElement === prev || document.activeElement === next) && (document.activeElement as HTMLButtonElement).disabled) (dir > 0 ? prev : next).focus();
    };
    prev.onclick = (): void => turn(-1);
    next.onclick = (): void => turn(1);
    this.bookTurnFn = turn;
    render();
    this.bookEl = wrap;
    this.root.append(wrap);
  }

  bookTurn(dir: number): void {
    this.bookTurnFn?.(dir);
  }

  /** A soft dark vignette with drifting Zs while the player sleeps in the hammock. */
  setSleep(on: boolean): void {
    if (!on) {
      this.sleepEl?.remove();
      this.sleepEl = null;
      return;
    }
    if (this.sleepEl) return;
    const v = el('div', 'sleepveil');
    for (let i = 0; i < 3; i++) v.append(el('span', `zz zz${i}`, 'z'));
    v.append(el('div', 'sleepmsg', 'Resting until morning...'));
    this.sleepEl = v;
    this.root.append(v);
  }

  hideBook(): void {
    this.bookEl?.remove();
    this.bookEl = null;
    this.bookTurnFn = null;
  }

  /** The list of saves, for choosing what to load or what to overwrite. */
  openSaves(mode: 'save' | 'load', hooks: SaveHooks, onClose?: () => void): void {
    this.closeSaves();
    const m = el('div', 'pause saves');
    const card = el('div', 'intro-card save-card');
    const title = el('h1', '', mode === 'save' ? 'Save game' : 'Load game');
    card.append(title);

    let nameInput: HTMLInputElement | null = null;
    if (mode === 'save') {
      const row = el('div', 'save-new');
      nameInput = el('input', 'save-name');
      nameInput.type = 'text';
      nameInput.maxLength = 40;
      nameInput.placeholder = 'Name this save';
      const create = el('button', 'btn', 'Save as new');
      row.append(nameInput, create);
      card.append(row);
      create.onclick = (): void => {
        const meta = hooks.save(nameInput!.value || `Save ${hooks.list().length + 1}`);
        if (meta) renderList();
      };
      nameInput.value = `Save ${hooks.list().length + 1}`;
      nameInput.addEventListener('keydown', (e) => e.stopPropagation());
    }

    const list = el('div', 'save-list');
    const note = el('p', 'save-note', mode === 'save' ? 'Make as many saves as you like. Pick one below to save over it.' : 'Loading replaces your current game. Unsaved progress is lost.');
    const back = el('button', 'btn ghost', 'Back');
    back.onclick = (): void => {
      this.closeSaves();
      onClose?.();
    };
    card.append(list, note, back);
    m.append(card);

    const renderList = (): void => {
      list.innerHTML = '';
      const saves = hooks.list();
      if (saves.length === 0) list.append(el('p', 'save-empty', 'No saved games yet.'));
      for (const meta of saves) {
        const row = el('div', 'save-row');
        const left = el('div', 'si');
        left.append(el('b', '', meta.name), el('span', '', `${timeLabel(meta.savedAt)} · ${meta.coins}g · ${meta.finds} kinds found`));
        const right = el('div', 'sb');
        const main = el('button', 'btn small', mode === 'save' ? 'Overwrite' : 'Load');
        main.onclick = (): void => {
          if (mode === 'save') {
            hooks.save(nameInput?.value.trim() || meta.name, meta.id);
            renderList();
          } else hooks.load(meta.id);
        };
        const del = el('button', 'btn small ghost', '🗑');
        let armed = false;
        del.onclick = (): void => {
          if (!armed) {
            armed = true;
            del.textContent = 'Sure?';
            setTimeout(() => {
              armed = false;
              del.textContent = '🗑';
            }, 2500);
            return;
          }
          hooks.remove(meta.id);
          renderList();
        };
        right.append(main, del);
        row.append(left, right);
        list.append(row);
      }
    };
    renderList();
    this.savesEl = m;
    this.root.append(m);
  }

  closeSaves(): void {
    this.savesEl?.remove();
    this.savesEl = null;
  }

  private savesEl: HTMLElement | null = null;
  private pauseEl: HTMLElement | null = null;

  /** A second prompt, for the E key. */
  setPrompt2(label: string | null): void {
    if (label) this.prompt2Text.textContent = label;
    this.prompt2.classList.toggle('on', !!label);
  }

  /** Shows the "press F" prompt for something the player is standing next to. */
  setPrompt(label: string | null): void {
    if (label) this.promptText.textContent = label;
    this.prompt.classList.toggle('on', !!label);
  }

  setClock(icon: string, day: number): void {
    const text = `${icon} Day ${day}`;
    if (this.clock.textContent !== text) this.clock.textContent = text;
  }

  get miniCtx(): CanvasRenderingContext2D {
    return this.miniCanvas.getContext('2d')!;
  }

  /** Full-screen island map. `draw` paints onto the large canvas it is given. */
  showMap(on: boolean, draw?: (ctx: CanvasRenderingContext2D, size: number) => void, onPick?: (u: number, v: number) => void, alts: { name: string; draw: (ctx: CanvasRenderingContext2D, size: number) => void }[] = []): void {
    if (!on) {
      this.mapEl?.remove();
      this.mapEl = null;
      this.mapToggle = null;
      this.mapRepaint = null;
      return;
    }
    if (this.mapEl || !draw) return;
    const wrap = el('div', 'pause bigmap');
    const card = el('div', 'intro-card map-card');
    const names = ['Island map', ...alts.map((x) => x.name)];
    let mode = 0;
    const title = el('h1', '', names[0]);
    card.append(title);
    const tabs = el('div', 'map-tabs');
    const tabBtns = names.map((n, i) => el('button', `btn small ghost${i === 0 ? ' on' : ''}`, n));
    if (alts.length) {
      tabs.append(...tabBtns);
      card.append(tabs);
    }
    const size = Math.min(600, Math.floor(Math.min(window.innerWidth, window.innerHeight) * 0.72));
    const c = el('canvas', 'bigmap-canvas');
    c.width = c.height = size;
    c.style.width = c.style.height = `${size}px`;
    const note = alts.length ? (pad.on ? 'LB / RB switches maps. ' : 'Press T to switch maps. ') : '';
    card.append(c, el('p', 'save-note', note + (pad.on ? 'You are the red arrow. Move the cursor with the left stick; A sets a destination, A on the pin removes it. B or Y closes.' : 'You are the red arrow. Click the map to set a destination, click the pin to remove it. M or Esc closes.')));
    wrap.append(card);
    const ctx2 = c.getContext('2d')!;
    const paint = (): void => (mode > 0 ? alts[mode - 1].draw(ctx2, size) : draw(ctx2, size));
    paint();
    const setMode = (i: number): void => {
      mode = i;
      title.textContent = names[i];
      tabBtns.forEach((t, k) => t.classList.toggle('on', k === i));
      paint();
    };
    tabBtns.forEach((t, i) => (t.onclick = (): void => setMode(i)));
    this.mapToggle = (): void => setMode((mode + 1) % names.length);
    this.mapRepaint = (): void => {
      if (mode > 0) paint();
    };
    c.style.cursor = 'crosshair';
    c.addEventListener('click', (e) => {
      const r = c.getBoundingClientRect();
      onPick?.((e.clientX - r.left) / r.width, (e.clientY - r.top) / r.height);
      paint();
    });
    this.mapEl = wrap;
    this.root.append(wrap);
  }

  /** Flips between the island map and the treasure map, if the player has one. */
  toggleMap(): void {
    this.mapToggle?.();
  }

  /** Redraws the map that is showing (the treasure crosses pulse). */
  repaintMap(): void {
    this.mapRepaint?.();
  }

  /** Replaces the tool badge text (used while driving). */
  /** The minimap is of no use under the dunes. */
  setUnderground(on: boolean): void {
    document.body.classList.toggle('underground', on);
  }

  private diveTint: HTMLElement | null = null;

  /** Underwater the minimap goes away and a soft blue vignette comes in at the edges. */
  setDiving(on: boolean): void {
    document.body.classList.toggle('diving', on);
    if (!this.diveTint) {
      this.diveTint = el('div', 'dive-tint');
      this.hud.append(this.diveTint);
    }
    this.diveTint.classList.toggle('on', on);
  }

  setBadgeText(icon: string, name: string): void {
    this.badgeOverride = true;
    this.updatePadTools();
    this.badge.classList.remove('hidden');
    this.badgeIcon.textContent = icon;
    this.badgeName.textContent = name;
  }

  /** The two-lens binocular view with a range readout. Redraws only when something changed. */
  setBinoculars(on: boolean, zoom = '', range = ''): void {
    const key = `${on}|${pad.on}|${zoom}|${range}|${window.innerWidth}x${window.innerHeight}`;
    if (key === this.binocKey) return;
    this.binocKey = key;
    this.binoc.classList.toggle('hidden', !on);
    if (!on) return;
    const w = (this.binoc.width = window.innerWidth);
    const h = (this.binoc.height = window.innerHeight);
    const g = this.binoc.getContext('2d')!;
    const r = Math.min(w * 0.27, h * 0.46);
    const off = r * 0.62;
    g.fillStyle = 'rgba(4,6,8,0.97)';
    g.fillRect(0, 0, w, h);
    g.globalCompositeOperation = 'destination-out';
    for (const dx of [-off, off]) {
      g.beginPath();
      g.arc(w / 2 + dx, h / 2, r, 0, Math.PI * 2);
      g.fill();
    }
    g.globalCompositeOperation = 'source-over';
    g.strokeStyle = 'rgba(30,34,40,0.9)';
    g.lineWidth = 5;
    for (const dx of [-off, off]) {
      g.beginPath();
      g.arc(w / 2 + dx, h / 2, r, 0, Math.PI * 2);
      g.stroke();
    }
    // reticle: a fine cross with ticks, centred between the lenses
    g.strokeStyle = 'rgba(255,255,255,0.75)';
    g.lineWidth = 1.5;
    g.beginPath();
    g.moveTo(w / 2 - 36, h / 2);
    g.lineTo(w / 2 - 6, h / 2);
    g.moveTo(w / 2 + 6, h / 2);
    g.lineTo(w / 2 + 36, h / 2);
    g.moveTo(w / 2, h / 2 - 36);
    g.lineTo(w / 2, h / 2 - 6);
    g.moveTo(w / 2, h / 2 + 6);
    g.lineTo(w / 2, h / 2 + 36);
    g.stroke();
    for (let i = 1; i <= 4; i++) {
      g.beginPath();
      g.moveTo(w / 2 - 6 - i * 7, h / 2 - 4);
      g.lineTo(w / 2 - 6 - i * 7, h / 2 + 4);
      g.moveTo(w / 2 + 6 + i * 7, h / 2 - 4);
      g.lineTo(w / 2 + 6 + i * 7, h / 2 + 4);
      g.stroke();
    }
    // readout
    g.font = '600 20px "Trebuchet MS", sans-serif';
    g.textAlign = 'center';
    g.fillStyle = 'rgba(255,248,230,0.95)';
    g.fillText(`${zoom}   ·   ${range}`, w / 2, h - 62);

    // legend: how to use and how to put the binoculars down
    const items: [string, string][] = pad.on
      ? [['RT', 'Change zoom'], ['LT', 'Lower binoculars'], ['LB / RB', 'Switch tool']]
      : [['Click', 'Change zoom'], ['Right-click', 'Lower binoculars'], ['Scroll', 'Switch tool']];
    g.font = '600 13px "Trebuchet MS", sans-serif';
    const labelFont = '13px "Trebuchet MS", sans-serif';
    const gap = 26;
    const keyPad = 9;
    const parts = items.map(([k, t]) => {
      g.font = '700 13px "Trebuchet MS", sans-serif';
      const kw = g.measureText(k).width + keyPad * 2;
      g.font = labelFont;
      const tw = g.measureText(t).width;
      return { k, t, kw, tw, total: kw + 8 + tw };
    });
    const totalW = parts.reduce((sum, p) => sum + p.total, 0) + gap * (parts.length - 1);
    let x = (w - totalW) / 2;
    const y = h - 30;
    for (const p of parts) {
      g.fillStyle = 'rgba(247,236,214,0.95)';
      g.beginPath();
      g.roundRect(x, y - 14, p.kw, 24, 7);
      g.fill();
      g.fillStyle = '#3b2a1e';
      g.font = '700 13px "Trebuchet MS", sans-serif';
      g.textAlign = 'center';
      g.fillText(p.k, x + p.kw / 2, y + 3);
      g.fillStyle = 'rgba(255,248,230,0.9)';
      g.font = labelFont;
      g.textAlign = 'left';
      g.fillText(p.t, x + p.kw + 8, y + 3);
      x += p.total + gap;
    }
  }

  /** The strength meter under the crosshair while a bow is drawn. 0 hides it. */
  setDraw(p: number): void {
    this.drawbar.classList.toggle('hidden', p <= 0.001);
    this.drawFill.style.width = `${Math.round(p * 100)}%`;
  }

  setCrosshair(on: boolean): void {
    this.cross.classList.toggle('on', on);
  }

  private kbdF: HTMLElement | null = null;
  private kbdE: HTMLElement | null = null;

  /** Switches prompts and hints between keyboard and controller wording. */
  setPadMode(on: boolean): void {
    pad.on = on;
    this.badge.classList.toggle('hidden', this.curTool === 0 || (on && !this.badgeOverride));
    this.updatePadTools();
    if (this.kbdF) this.kbdF.textContent = on ? 'A' : 'F';
    if (this.kbdE) this.kbdE.textContent = on ? 'X' : 'E';
    // labels that name a key, like "Back (Esc)"
    this.root.querySelectorAll<HTMLElement>('button').forEach((b) => {
      const kb = b.dataset.kb ?? b.textContent ?? '';
      if (!/\((Esc|Space)\)/.test(kb)) return;
      b.dataset.kb = kb;
      b.textContent = padText(kb);
    });
    const help = this.root.querySelector<HTMLElement>('.inspect-help');
    if (help) help.textContent = on ? 'Left stick turns the find · hold RT and move the right stick to brush it clean' : 'Drag with the left mouse to scrub · Right-drag or Q / E / arrows to rotate';
    this.binocKey = '';
  }

  setHint(text: string | null): void {
    this.hint.textContent = text ? padText(text) : '';
    this.hint.classList.toggle('show', !!text);
  }

  toast(msg: string): void {
    const t = el('div', 'toast', padText(msg));
    this.toasts.append(t);
    requestAnimationFrame(() => t.classList.add('in'));
    setTimeout(() => {
      t.classList.remove('in');
      setTimeout(() => t.remove(), 500);
    }, 3200);
  }

  /** Cross-fades through black, running `mid` at the darkest point. */
  fade(mid: () => void): void {
    this.fadeEl.classList.add('on');
    setTimeout(() => {
      mid();
      requestAnimationFrame(() => this.fadeEl.classList.remove('on'));
    }, 320);
  }

  showInspect(name: string): void {
    this.inspectTitle.textContent = name === '' ? '' : 'Something encrusted in sand…';
    this.result.classList.add('hidden');
    this.inspect.classList.remove('hidden');
    this.setProgress(0);
  }

  hideInspect(): void {
    this.inspect.classList.add('hidden');
    this.result.classList.add('hidden');
  }

  setProgress(p: number): void {
    this.progFill.style.width = `${Math.round(p * 100)}%`;
    this.progText.textContent = `${Math.round(p * 100)}%`;
  }

  showResult(name: string, lore: string, valueText: string, category: string): void {
    this.inspectTitle.textContent = name;
    this.result.innerHTML = '';
    const card = el('div', 'card');
    card.append(
      el('div', 'cat', category),
      el('h2', '', name),
      el('p', 'lore', lore),
      el('div', 'worth', valueText),
    );
    const b = el('button', 'btn', padText('Add to Backpack  (Space)'));
    b.dataset.kb = 'Add to Backpack  (Space)';
    b.onclick = (): void => this.hooks.onContinue();
    card.append(b);
    this.result.append(card);
    this.result.classList.remove('hidden');
  }

  isResultOpen(): boolean {
    return !this.result.classList.contains('hidden');
  }

  showIntro(onStart: () => void, onLoad?: () => void): void {
    this.intro.classList.remove('gone');
    if (!this.intro.isConnected) this.root.append(this.intro);
    this.intro.innerHTML = `
      <div class="intro-card">
        <h1>Cozy Treasure Hunter</h1>
        <p class="tag">A misty golden morning. A quiet beach. Nothing to rush.</p>
        ${pad.on ? `<div class="keys">
          <div class="krow"><b>LB / RB</b><span>Switch between your tools</span></div>
          <div class="krow"><b>Detector</b><span>Sweep the sand and listen for the beep</span></div>
          <div class="krow"><b>Trowel</b><span>Dig where the signal sings (RT)</span></div>
          <div class="krow"><b>RT</b><span>Brush a dug-up find clean</span></div>
          <div class="krow"><b>Binoculars</b><span>Pick them with LB / RB, RT to zoom</span></div>
          <div class="krow"><b>A</b><span>Use what is next to you</span></div>
          <div class="krow"><b>Cart</b><span>A beside the golf cart to drive it</span></div>
          <div class="krow"><b>Left stick</b><span>Walk (press it in to hurry)</span></div>
          <div class="krow"><b>Right stick</b><span>Look around</span></div>
          <div class="krow"><b>Y</b><span>Open the island map</span></div>
          <div class="krow"><b>Back</b><span>Mute sound</span></div>
        </div>` : `<div class="keys">
          <div class="krow"><b>Scroll</b><span>Switch between your tools</span></div>
          <div class="krow"><b>Detector</b><span>Sweep the sand and listen for the beep</span></div>
          <div class="krow"><b>Trowel</b><span>Dig where the signal sings</span></div>
          <div class="krow"><b>Click</b><span>Brush a dug-up find clean</span></div>
          <div class="krow"><b>Binoculars</b><span>Scroll to them, click to zoom</span></div>
          <div class="krow"><b>F</b><span>Use what is next to you</span></div>
          <div class="krow"><b>Cart</b><span>F beside the golf cart to drive it</span></div>
          <div class="krow"><b>WASD</b><span>Walk (hold Shift to hurry)</span></div>
          <div class="krow"><b>Mouse</b><span>Look around (click to capture)</span></div>
          <div class="krow"><b>M</b><span>Open the island map</span></div>
          <div class="krow"><b>N</b><span>Mute sound</span></div>
          <div class="krow"><b>Pad</b><span>Sticks move and look · RT use · LT aim · A interact · LB/RB tools · Start pause</span></div>
        </div>`}
        <div class="row gap"><button class="btn big" data-act="begin">Begin</button>${onLoad ? '<button class="btn ghost big" data-act="load">Load saved game</button>' : ''}</div>
      </div>`;
    const go = (): void => {
      this.intro.classList.add('gone');
      setTimeout(() => this.intro.remove(), 700);
      onStart();
    };
    this.intro.querySelector('[data-act=begin]')!.addEventListener('click', go);
    this.intro.querySelector('[data-act=load]')?.addEventListener('click', () => onLoad?.());
  }

  /** Used when a saved game is loaded straight into play. */
  skipIntro(): void {
    this.intro.remove();
  }

  get introOpen(): boolean {
    return this.intro.isConnected && !this.intro.classList.contains('gone');
  }

  get container(): HTMLElement {
    return this.root;
  }
}
