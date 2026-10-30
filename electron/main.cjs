const { app, BrowserWindow, ipcMain, Menu, screen, shell } = require('electron');
const fs = require('fs');
const path = require('path');

// the game starts its own sounds on the first click, but a controller press is not a click
app.commandLine.appendSwitch('autoplay-policy', 'no-user-gesture-required');

const DEV_URL = process.env.ELECTRON_DEV_URL;

// ---------------------------------------------------------------- Steam
// Only used when the game is started by Steam; anywhere else the module or the client is missing and this stays off.
let steam = null;
try {
  const steamworks = require('steamworks.js');
  steam = steamworks.init();
  // the Steam overlay needs the game to draw in-process
  app.commandLine.appendSwitch('in-process-gpu');
  app.commandLine.appendSwitch('disable-direct-composition');
} catch {
  steam = null;
}

ipcMain.on('steam:available', (e) => {
  e.returnValue = steam !== null;
});

ipcMain.on('steam:achievement', (_e, id) => {
  if (!steam || typeof id !== 'string') return;
  try {
    if (!steam.achievement.isActivated(id)) steam.achievement.activate(id);
  } catch {
    /* an achievement Steam does not know about is not worth a crash */
  }
});

// ---------------------------------------------------------------- saves
// Saves and achievements are plain files in the game's folder (userData/saves), which Steam Auto-Cloud can copy to the cloud.
const savesDir = () => path.join(app.getPath('userData'), 'saves');
// keys are made of letters, digits and dashes only, so they are safe as file names
const fileFor = (key) => {
  if (typeof key !== 'string' || !/^[A-Za-z0-9_-]{1,120}$/.test(key)) return null;
  return path.join(savesDir(), `${key}.json`);
};

ipcMain.on('store:get', (e, key) => {
  const f = fileFor(key);
  try {
    e.returnValue = f && fs.existsSync(f) ? fs.readFileSync(f, 'utf8') : null;
  } catch {
    e.returnValue = null;
  }
});

ipcMain.on('store:set', (e, key, value) => {
  const f = fileFor(key);
  if (!f || typeof value !== 'string') {
    e.returnValue = false;
    return;
  }
  try {
    fs.mkdirSync(savesDir(), { recursive: true });
    // write beside it first, then rename, so a crash never leaves half a save
    const tmp = `${f}.tmp`;
    fs.writeFileSync(tmp, value);
    fs.renameSync(tmp, f);
    e.returnValue = true;
  } catch {
    e.returnValue = false;
  }
});

ipcMain.on('store:remove', (e, key) => {
  const f = fileFor(key);
  try {
    if (f && fs.existsSync(f)) fs.unlinkSync(f);
  } catch {
    /* ignore */
  }
  e.returnValue = true;
});

// how the window is shown is remembered between runs
const cfgPath = () => path.join(app.getPath('userData'), 'display.json');
const readCfg = () => {
  try {
    return { mode: 'fullscreen', width: 1280, height: 720, ...JSON.parse(fs.readFileSync(cfgPath(), 'utf8')) };
  } catch {
    return { mode: 'fullscreen', width: 1280, height: 720 };
  }
};
let cfg = null;
let mainWin = null;
// a window's border can only be chosen when it is created
let frameless = false;

const applyDisplay = (win) => {
  if (cfg.mode === 'fullscreen') {
    win.setFullScreen(true);
    return;
  }
  win.setFullScreen(false);
  if (cfg.mode === 'borderless') {
    win.setBounds(screen.getDisplayMatching(win.getBounds()).bounds);
  } else {
    win.setSize(cfg.width, cfg.height);
    win.center();
  }
};

function createWindow() {
  Menu.setApplicationMenu(null);
  cfg = readCfg();
  frameless = cfg.mode === 'borderless';
  const area = screen.getPrimaryDisplay().bounds;
  const win = new BrowserWindow({
    width: frameless ? area.width : cfg.width,
    height: frameless ? area.height : cfg.height,
    x: frameless ? area.x : undefined,
    y: frameless ? area.y : undefined,
    frame: !frameless,
    minWidth: 800,
    minHeight: 500,
    backgroundColor: '#14100b',
    title: 'Cozy Treasure Hunter',
    autoHideMenuBar: true,
    fullscreen: !DEV_URL && cfg.mode === 'fullscreen',
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  mainWin = win;
  if (DEV_URL) win.loadURL(DEV_URL);
  else win.loadFile(path.join(__dirname, '..', 'dist', 'index.html'));

  // F11 toggles fullscreen; Alt+Enter too
  win.webContents.on('before-input-event', (_e, input) => {
    if (input.type !== 'keyDown') return;
    if (input.key === 'F11' || (input.alt && input.key === 'Enter')) win.setFullScreen(!win.isFullScreen());
  });
  // links never navigate the game window away
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:/.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
}

ipcMain.on('desktop:quit', () => app.quit());

ipcMain.handle('desktop:display', (_e, next) => {
  cfg = {
    mode: ['fullscreen', 'windowed', 'borderless'].includes(next.mode) ? next.mode : 'fullscreen',
    width: Math.max(800, Math.min(7680, Number(next.width) || 1280)),
    height: Math.max(500, Math.min(4320, Number(next.height) || 720)),
  };
  try {
    fs.writeFileSync(cfgPath(), JSON.stringify(cfg));
  } catch {
    /* not being able to remember it is not worth a crash */
  }
  if ((cfg.mode === 'borderless') !== frameless) return { restart: true };
  if (mainWin) applyDisplay(mainWin);
  return { restart: false };
});

app.whenReady().then(() => {
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => app.quit());
