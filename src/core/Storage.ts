/**
 * Where saves and achievements are kept. In the desktop app they are plain JSON files in the game's folder, which Steam can
 * copy to the cloud; in a browser they stay in localStorage. Either way the rest of the game just uses `store`.
 */
interface DesktopStore {
  get(key: string): string | null;
  set(key: string, value: string): boolean;
  remove(key: string): void;
}

const desktop = (): DesktopStore | undefined => (window as unknown as { desktop?: { store?: DesktopStore } }).desktop?.store;

export const store = {
  getItem(key: string): string | null {
    const d = desktop();
    if (d) {
      try {
        const v = d.get(key);
        if (v !== null) return v;
        // something saved by an earlier version, in the browser's own storage: bring it across to the files
        const old = localStorage.getItem(key);
        if (old !== null) {
          d.set(key, old);
          return old;
        }
      } catch {
        /* fall through */
      }
      return null;
    }
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  },

  /** Returns false when the value could not be written (a full disk, a blocked browser store). */
  setItem(key: string, value: string): boolean {
    const d = desktop();
    if (d) {
      try {
        return d.set(key, value);
      } catch {
        return false;
      }
    }
    try {
      localStorage.setItem(key, value);
      return true;
    } catch {
      return false;
    }
  },

  removeItem(key: string): void {
    try {
      desktop()?.remove(key);
    } catch {
      /* ignore */
    }
    try {
      localStorage.removeItem(key);
    } catch {
      /* ignore */
    }
  },
};
