const KEY = 'cozy-treasure-hunter-settings-v1';

export interface GameSettings {
  /** 0..1 overall volume */
  master: number;
  /** 0..1 the radio */
  music: number;
  /** 0..1 sea and wind */
  ambience: number;
  /** mouse-look speed multiplier */
  sensitivity: number;
  shadows: boolean;
  /** render at the screen's full pixel density (sharper, heavier) */
  sharp: boolean;
  /** how the game window is shown (desktop app; the browser can only do fullscreen or not) */
  displayMode: 'fullscreen' | 'windowed' | 'borderless';
  /** window size in windowed mode, "widthxheight" */
  windowSize: string;
}

export const DEFAULT_SETTINGS: GameSettings = {
  master: 1,
  music: 1,
  ambience: 1,
  sensitivity: 1,
  shadows: true,
  sharp: true,
  displayMode: 'fullscreen',
  windowSize: '1280x720',
};

export const loadSettings = (): GameSettings => {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return { ...DEFAULT_SETTINGS, ...(JSON.parse(raw) as Partial<GameSettings>) };
  } catch {
    /* ignore */
  }
  return { ...DEFAULT_SETTINGS };
};

export const saveSettings = (s: GameSettings): void => {
  try {
    localStorage.setItem(KEY, JSON.stringify(s));
  } catch {
    /* ignore */
  }
};

/** Flag so that "New game" from the pause menu starts straight away instead of returning to the main menu. */
const AUTOSTART = 'cozy-treasure-hunter-autostart';

export const requestAutostart = (): void => {
  try {
    sessionStorage.setItem(AUTOSTART, '1');
  } catch {
    /* ignore */
  }
};

export const takeAutostart = (): boolean => {
  try {
    const v = sessionStorage.getItem(AUTOSTART) === '1';
    sessionStorage.removeItem(AUTOSTART);
    return v;
  } catch {
    return false;
  }
};
