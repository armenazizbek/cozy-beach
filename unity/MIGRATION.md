# Cozy Treasure Hunter: Unity port

This folder is a Unity 6.3 LTS project (`6000.3.28f1`, Universal Render Pipeline) that is a straight port of the Three.js game in
`../src`. The aim is the same game, the same look and the same numbers, running under Unity, so it can later go to consoles.

## How to open it

1. Open Unity Hub and sign in (a free Personal licence is enough).
2. Add this folder (`unity/`) as a project and open it with `6000.3.28f1`.
3. On the first open the editor script `Assets/Editor/ProjectSetup.cs` creates the URP pipeline asset, turns on the Input System,
   adds `Assets/Scenes/Main.unity` to the build and sets the player settings. Unity may ask to restart once for the Input System.
4. Press Play in `Assets/Scenes/Main.unity`.

Builds: `Assets/Editor/BuildScript.cs` (`BuildScript.BuildWindows`) writes `Builds/Windows/CozyTreasureHunter.exe`. From the
command line, with the editor closed:

```
"C:\Users\Armen\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod BuildScript.BuildWindows -logFile build.log
```

The short way, from the repository's root: `npm run dist:unity` runs that build and copies the game, without its debug
symbols, to `release/unity-win-unpacked`, ready to play or to upload to Steam; `npm run dist:unity:quick` does the same with
the quick Mono build. Both need the editor and the game closed (`scripts/dist-unity.mjs`).

`BuildWindows` is the release build: it compiles the C# to native code with IL2CPP when Unity Hub's *Windows Build Support
(IL2CPP)* module is installed (much faster game code; consoles need it), and falls back to Mono with a warning otherwise.
`BuildScript.BuildWindowsMono` makes a quick Mono build for testing changes. Switching between the two clears
`Builds/Windows` first (Unity refuses to build one over the other). Do not ship the two folders whose names end in
`_ButDontShipItWithYourGame` and `_DoNotShip`: they are debug symbols, about 1.2 GB.

### Colour emoji

The UI is full of emoji (menu, HUD, journal, achievements, camp). Unity's text engine cannot draw colour emoji fonts, so the
system's Segoe UI Emoji came out as black outlines or empty boxes. The game ships its own instead: Microsoft's **Fluent Emoji**
(MIT, the same design Windows shows in the browser build).

- `Tools/Emoji/update_emoji.py` scans `Assets/Scripts` for emoji, downloads any new ones from Fluent into `Tools/Emoji/svg`,
  and draws them all into `Assets/Art/Emoji/EmojiAtlas.png` with Microsoft Edge. **Run it after adding a new emoji to the code.**
- `Assets/Editor/EmojiSpriteAssetBuilder.cs` (run by every build, or the menu *Cozy Treasure Hunter > Rebuild Emoji Sprite
  Asset*) turns the atlas into the sprite asset `Assets/Resources/UI/Emoji.asset`.
- `Dom/EmojiText.cs` swaps each emoji in on-screen text for a `<sprite>` tag from that asset.
- The build writes the Fluent licence to `ThirdPartyNotices.txt` next to the exe; keep that file in the shipped game.

### The UI's look

The menus and the HUD are built by the DOM layer (`Assets/Scripts/Dom`) from HTML-like elements, as in the browser game, but
they no longer look like a web page:

- **One page, scaled.** The UI is laid out on a 1920 x 1080 page that `DomHost` scales to the screen, so it takes up the same
  share of a 720p window and a 4K TV. Sizes in `Assets/Resources/UI/Style.uss` are in those page pixels.
- **The skin.** Panels, buttons, key caps, controller buttons, meters and frames are pictures in
  `Assets/Resources/UI/Skin`, drawn by `Tools/UiSkin/make_skin.py` (plain Python, nothing to install) and stretched as
  9-slices. **Run the script again after changing a colour or a shape in it.** `Assets/Editor/UiSkinImporter.cs` gives the
  pictures their import settings; every build checks them.
- **Keys as pictures.** `Assets/Scripts/UI/Glyphs.cs` draws the keys and buttons that prompts, hints and menus name: a key
  cap for F, the green A button, a trigger, a mouse with a button lit. Hints are still written as plain sentences
  ("Click to dig · F picks it up"); `Glyphs.fill` finds the keys in them.
- **One highlight.** The mouse, the keyboard (arrows or W A S D, Enter, Esc, Q and E for tabs) and a controller all move the
  same focus through a menu (`Game.menuKey`, `Game.padNav`, `HTMLElement.HoverFocus`), and a line along the bottom of every
  menu says what the keys do (`Game.menuTick`, `UIManager.setMenuBar`).
- **Fonts** come from the system for now (Trebuchet MS for text, Georgia Bold for headings). A font file put in
  `Assets/Resources/UI/Fonts` as `Body`, `BodyBold` or `Display` is used instead, which is what a console build will need.

### Smoke test

`Assets/Scripts/Core/AutoShot.cs` checks a build without anyone at the keyboard. It does nothing unless the game is started
with `-autoshot`:

```
Builds\Windows\CozyTreasureHunter.exe -autoshot C:\temp\shots -screen-width 1280 -screen-height 720 -screen-fullscreen 0
```

It waits for the loading screen, saves `1-title.png`, starts a game as Begin would, saves `2-camp.png` and `3-sea.png`, writes
the frame rate to the player log (`%USERPROFILE%\AppData\LocalLow\Cozy Beach\Cozy Treasure Hunter\Player.log`) and quits.
Saves go to a `saves` folder inside the shot folder, so real saves are never read or written.

Add `-autoshot-tour` to also photograph the screens with the most text (Lost & Found, Sell, Upgrades, Rest, journal,
letters, Help, Credits), for checking text wrapping and scrolling.

Add `-autoshot-ui` to photograph every menu and the HUD twice, as they look with the keyboard and with a controller
(`ui-kb-*.png`, `ui-pad-*.png`). Add `-autoshot-pad` to drive the title menu with a virtual controller.

Add `-autoshot-birds` to fire a bullet at a standing gull and at a flying one (the log shows the coins before and after
each) and to photograph the line of keys shown while swimming.

Add `-autoshot-shadow` to photograph the player's own shadow on the island's grass and thrown onto its big rocks.

Add `-autoshot-island` to photograph the island from its beach, its slope and its west side.

Add `-autoshot-islands` to sail to the two far islands (Lagoon Cay and Ember Rock) and photograph each from the sea, from
its shore and at its landmark, then the big map (`17-*`, `18-*`, `19-*`). The log says where the boat floats, where stepping
off puts the player and where each island's finds lie. With `-autoshot-crust` as well, the workbench shots are of the far
islands' six finds.

Add `-autoshot-opening` to choose New Game and photograph the opening at a moment in each of its four shots (storm, reef,
dawn, camp) and the how-to-play card after it (`24-*`). The log gives the caption, the rain and the hour at each. Keep
hands off the keyboard and mouse while it runs: any key skips the opening.

Add `-autoshot-dive` to photograph the sea floor from nine places (the way in, the reef, the wreck, the deep, looking up)
and measure the frame rate down there (`22-*`).

Add `-autoshot-dolphins` to take the boat across open water with the dolphin pod called up: arriving, alongside (ahead and
to each side) and after the boat has stopped (`21-*`). The log says whether the pod is with the boat.

Add `-autoshot-crust` to photograph finds of different shapes on the workbench, still in their sand.

Add `-autoshot-saves` to autosave three times, start a new game and autosave twice more: the log lists every save, and
each game must keep its own two autosaves.

Add `-autoshot-rain` to dig a deep hole and build a sandcastle, then let it rain for a minute: the log shows the hole
filling and the castle washing away.

Add `-autoshot-pier` to photograph the walk down to the pier, which must be free of palms.

Add `-autoshot-holes` to dig three deep holes (up the beach, on the wet sand, at the water's edge) and park the golf cart
across one: no hole may show water, and the log says how far the cart sits below flat sand.

Add `-autoshot-walk` to measure standing, walking and running. Each measurement logs the frame rate and the slowest parts
of the game's frame in milliseconds (`THREE.Bridge.BridgeStats`; the `using (BridgeStats.Time(...))` lines in `Game.frame`).

Add `-autoshot-load <save id>` to load that save the way Load Game does and photograph it. Copy the saves to test into the
shot folder's `saves` folder first; the ids are in `cozy-treasure-hunter-saves-v3.json`.

## How the port is laid out

The TypeScript was written against Three.js, and nearly every model, texture and sound is made in code. Rather than redesign it,
the port brings a small part of Three.js across to C#, so each TypeScript file becomes one C# file with the same shape:

| TypeScript | C# |
|---|---|
| `src/core/*.ts` | `Assets/Scripts/Core/*.cs` |
| `src/world/*.ts` | `Assets/Scripts/World/*.cs` |
| `src/mechanics/*.ts` | `Assets/Scripts/Mechanics/*.cs` |
| `src/ui/*.ts` | `Assets/Scripts/UI/*.cs` (UI Toolkit) |
| `src/style.css` | `Assets/Resources/UI/Style.uss` |
| `three` | `Assets/Scripts/Three/` (namespace `THREE`) |
| `public/music/*.mp3` | `Assets/Resources/Music/*.mp3` |

### The `THREE` namespace

`Assets/Scripts/Three` re-implements the parts of Three.js the game uses: vectors, quaternions, Euler angles, matrices, colours,
`Object3D` and its scene graph, `BufferGeometry` and every primitive geometry (box, cylinder, sphere, torus, cone, lathe, tube,
extrude, shape, polyhedra, capsule...), curves and shapes, materials, textures, a software 2D canvas for `CanvasTexture`, and a
raycaster. The geometry generators follow Three.js's own source, so vertices come out identical.

`THREE.WebGLRenderer` is the bridge to Unity. Every frame it walks the scene graph and copies each object's transform, visibility,
geometry and material into a matching Unity `GameObject` with a `MeshRenderer`. Game code never touches Unity transforms directly;
it moves `THREE.Object3D`s exactly as the TypeScript did.

### Coordinates

Three.js is right-handed and Unity is left-handed. All game code keeps Three.js numbers (yaw 0 looks toward -z, the sea is at +z).
The bridge mirrors the z axis when it hands things to Unity:

- positions `(x, y, z)` become `(x, y, -z)`
- rotations `(qx, qy, qz, qw)` become `(-qx, -qy, qz, qw)`
- meshes have z negated on positions and normals, and triangle winding reversed

Because the mirror is applied to every local transform, whole hierarchies stay consistent, and the picture is the same as in the
browser (not mirrored).

### Numbers

Game code uses `double` like JavaScript, so literals and `Math.*` port unchanged. `JS.cs` holds the JavaScript behaviours C# does
not share: `JS.Round` (rounds .5 up, unlike `Math.Round`), `JS.Random`, `JS.Hypot`, `JS.Sign`, `JS.Max`/`JS.Min` with many
arguments.

### Shading

The shaders in `Assets/Resources/Shaders` reproduce Three.js's `MeshStandardMaterial` lighting (Lambert diffuse with 1/pi, GGX
specular), its ACES filmic tone mapping applied per material, and its linear and exp2 fog mixed in sRGB, so colours and light
intensities from the TypeScript can be used as they are. Unity lights carry the Three.js intensity unchanged; the shader does the
Three.js maths.

## What is ported

See `PORT_STATUS.md` for the file-by-file list.
