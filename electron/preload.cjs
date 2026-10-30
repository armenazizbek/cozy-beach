const { contextBridge, ipcRenderer } = require('electron');

// is the game running under Steam? (the main process knows)
const underSteam = ipcRenderer.sendSync('steam:available') === true;

// the little bit of the desktop the game is allowed to use
contextBridge.exposeInMainWorld('desktop', {
  quit: () => ipcRenderer.send('desktop:quit'),
  /** { mode: 'fullscreen' | 'windowed' | 'borderless', width, height } -> { restart } */
  setDisplay: (cfg) => ipcRenderer.invoke('desktop:display', cfg),
  /** saves and achievements, kept as files so Steam can copy them to the cloud */
  store: {
    get: (key) => ipcRenderer.sendSync('store:get', key),
    set: (key, value) => ipcRenderer.sendSync('store:set', key, value) === true,
    remove: (key) => void ipcRenderer.sendSync('store:remove', key),
  },
  /** only present when Steam started the game */
  steam: underSteam ? { unlock: (id) => ipcRenderer.send('steam:achievement', id) } : undefined,
});
