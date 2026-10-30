const { contextBridge, ipcRenderer } = require('electron');

// the little bit of the desktop the game is allowed to use
contextBridge.exposeInMainWorld('desktop', {
  quit: () => ipcRenderer.send('desktop:quit'),
  /** { mode: 'fullscreen' | 'windowed' | 'borderless', width, height } -> { restart } */
  setDisplay: (cfg) => ipcRenderer.invoke('desktop:display', cfg),
});
