# Tidefinder: finishing the migration (UI rebuild and what else is left)

Status: **accepted 2026-11-24**: the owner chose the recommendation for every decision in section 9 (D1 to D7) and asked
for it to be built on the branch `claude/ui-rebuild`, without questions. Progress is logged step by step in
`unity/UI_LOG.md`.

The native rewrite (`unity/NATIVE_PLAN.md`, log in `unity/NATIVE_LOG.md`) is merged into `main` (c6219d2). The Three.js
bridge and the browser DOM layer are gone. All drawing and all UI are native Unity. But two things are still shaped like
the browser game they came from:

- **Part A, the UI:** every screen is built in C# by `UiNode`, which copies the old web page element for element:
  `t-div`/`t-span` tag classes, the `html > body > #ui` tree, the page's z-index and pointer-events tables, text set as
  HTML. `Style.uss` still carries the CSS-era workarounds that made that page look right.
- **Part B, the rest:** the game still describes its world with Three.js-style objects (`THREE.Object3D`, meshes,
  materials, `THREE.Vector3`). It still calls a `WebGLRenderer` front, reads keys as browser key codes (`"KeyW"`), and has
  some stale names and docs.

Part A is what was asked for. Part B lists everything else that I found still needs migrating, so that the whole job is
in one place. Section 8 gives the order for both.

Everything keeps the current look unless a step says otherwise. The rule from the native rewrite still holds: parity
first, optimise later.

---

## 1. Inventory: the UI today

Line counts are from `main` at c6219d2.

### 1.1 The native UI kit (`Assets/Scripts/UI/Native`, 3,606 lines)

| File | Lines | What it does | DOM-shaped? |
|---|---|---|---|
| `UiNode.cs` | 649 | An element built as the page built one: tag class, own text label, emoji, uppercase rules, fonts by class, pill corners, `.hidden`, the page's picking rules, scroll boxes, z-order, SVG drawings, focus and click | **Yes, entirely.** Replaced by UXML elements plus controllers |
| `SvgRasterizer.cs` | 765 | Rasterises inline SVG at runtime (tool icons, shop pictures) | Runtime SVG. Becomes an editor-only baker |
| `UiAnim.cs` | 466 | The page's `@keyframes` (key bob, card pop, camp slide, menu fade, sleep fade, z's) and helper classes for `:nth-child`, `:has()`, `::before` | Mostly. Most of it becomes USS transitions |
| `TextMarkup.cs` | 441 | Parses text as HTML (entities, whitespace, `<b>`/`<i>`) | **Yes.** Plain strings and rich text instead |
| `UiRoot.cs` | 336 | The UIDocument, PanelSettings, fonts, emoji, the 1920x1080 page scale and the UI size setting | No. Kept (fonts: decision D3) |
| `Page.cs` | 181 | The `html > body > #scene, #ui` tree, body state classes, pointer lock, resize, page metrics, is-over-UI, typing, tick | **The tree is.** Replaced by `Root.uxml` layers; the services stay |
| `MenuNav.cs` | 131 | `NavEl` and the menu highlight | Half. On UI Toolkit focus instead (section 3) |
| `UiCanvas.cs` | 106 | A CPU-painted picture shown on an element | Kept where needed; most users move to `Painter2D` |
| `UiRange.cs` | 104 | The page's range input (track, fill, knob) | Becomes a styled `Slider` or a small custom control |
| `GameTimers.cs` | 94 | setTimeout/setInterval/requestAnimationFrame on the test clock | Kept (it is what keeps the test runs deterministic) |
| `UiText.cs` | 92 | The page's text input over a `TextField` | Becomes a styled `TextField` |
| `LoadingScreen.cs` | 73 | The loading screen | Small. Moves to UXML |
| `UiX.cs` | 56 | Element helpers | Goes |
| `EmojiText.cs` | 53 | Emoji to sprite tags | Kept |
| `SelfCheckUi.cs` | 35 | Every visible button takes the pointer | Kept and extended |

### 1.2 The screens (`Assets/Scripts/UI`, 5,723 lines)

| File | Lines | What it builds | Notes |
|---|---|---|---|
| `UIManager.cs` | 1,929 | HUD (stats, chips, tool badge, d-pad tools, prompts, hint, toasts, crosshair, draw bar, dive tint, fade, menu bar), inspect and result, intro, opening caption, main menu, pause, settings (174 lines), saves (105), help, achievements, credits, farewell, journal, letters, story, ending, sleep veil, big map, binocular lenses and readout (187), viewfinder (96), photo flash | 241 `UiNode` builds. One class for everything. Its biggest methods: settings 174, binoculars 112, saves 105, viewfinder 96, map 76, help 71 |
| `CampModal.cs` | 925 | The camp: tabs and nine pages (rest, recycle, sell and workbench, Lost & Found board, upgrades and shop at 331 lines, luxury, shelf, photos at 114, aquarium) | 71 builds. The whole page is rebuilt on every state change, and keeps focus and scroll across the rebuild |
| `TelescopeView.cs` | 904 | The eyepiece: GPU sky (`generateVisualContent`), labels, count, messages, card | Already native drawing. Only 8 elements are DOM-shaped |
| `MapView.cs` | 751 | Draws the big map and the minimap markers on CPU canvases | CPU painter: the minimap markers every frame, the big map on repaint |
| `LoadingSea.cs` | 514 | The loading screen's animated sea (`Painter2D`) | Already native. Keep |
| `ShopArt.cs` | 284 | Builds SVG strings for the 36 shop pictures | Runtime SVG. Moves to the editor baker |
| `Glyphs.cs` | 195 | Key caps and controller buttons, hint layout with keys, the menu bar | Logic kept; elements become a template |
| `MiniMapGpu.cs` | 130 | The minimap's base and holes on the GPU | Native. Keep |
| `PadText.cs`, `ItemPhotos.cs` | 115 | Controller wording; photographs of finds (3D, not UI) | Keep |

### 1.3 UI code living outside `UI/`

- **`Game.cs` (6,963 lines)** holds the menu navigation: `overlayRoot`, `padNav`, `menuAccept`, `menuKey`, `menuTick`
  and `backButton`, about 280 lines from line 3287, plus `padButton`. It also sets body state classes
  (`nolock`, `usingpad`, `pointer`) and makes about 60 kinds of `ui.*` call (84 toasts, 21 `setTool`, 20 `setPrompt`,
  18 `fade`, 16 `setHint`, ...).
- **`AutoShot.cs`** (the test driver) reaches into the UI by class name (`settings-card`, `achievements`, `pause`), through
  `UIManager.removeFirst` and `MenuNav.Find`. Its controller test logs every reachable button's box. Those logs are
  compared, so any layout change shows up there.

### 1.4 Style and skin

- **`Resources/UI/Style.uss`**: 3,051 lines in 26 sections, 384 rules. 86 rules use tag classes (`.t-h1`, `.t-p`, ...)
  and 16 repeat themselves for `.unity-label`, because text sits in a Label inside each element. Its header still
  describes the deleted DOM layer (`DomHost`, `CssAnimations`, `HTMLElement`).
- **`Resources/UI/Skin`**: 9-slice pictures made by `unity/Tools/UiSkin/make_skin.py`. Not DOM-shaped. Kept as they are.
- **Fonts** are created at runtime from OS fonts (Trebuchet MS, Georgia, Segoe), with bundled faces on the Deck.

### 1.5 Drawn on the CPU each time they change

The binocular lenses and legend (full screen, redrawn on resize), the binocular readout, the viewfinder (full screen),
the detector waveform (every frame while scanning), the minimap markers (every frame, 0.03 to 0.12 ms) and the big map
(on repaint) are all painted by `TexturePainter` and uploaded as textures. All of them except the big map's base can
be `Painter2D` vector drawing instead: sharper at every UI scale, with no texture upload.

---

## 2. Target architecture

### 2.1 The shape of a screen

- **One screen is one UXML file, one USS file and one C# controller.** The controller gets its elements once with
  `Q<T>("name")`, wires events and owns the screen's state. Class names carry over unchanged where the look depends on
  them, so most of `Style.uss` moves over as it is.
- **List items are UXML templates** (`VisualTreeAsset`), instantiated by the controller: save rows, achievement rows,
  journal pages, letters, shop cells, photo cards, Lost & Found notes, help blocks, settings rows.
- **Built-in controls where they fit:**
  - `Button` with `clicked`.
  - `ScrollView` for the five scroll boxes.
  - `TextField` for the save name.
  - A styled `Slider` for the settings sliders. The current knob, track and fill are 9-slice pictures; if `Slider`
    cannot take them without a visible change, a 60-line `[UxmlElement]` control replaces `UiRange`.
  - **Not** `DropdownField`. The settings choice rows (‹ value ›, left and right to change) are made for a controller and
    stay a small custom control (`ChoiceRow`). The same goes for the switch rows (`ToggleRow`).
- **The root is `Root.uxml`**, with fixed layers in a fixed order instead of the page's z-index table:
  `world` (`#scene`) < `hud` < `overlays` (binoculars, viewfinder, telescope) < `screens` (menus, camp, cards)
  < `dialogs` (saves, help, achievements, credits) < `toasts` < `fade` < `menu-bar` < `loading`.
  `ScreenStack` opens and closes screens inside a layer, so nothing has to sort siblings any more.
- **`picking-mode="Ignore"` in UXML** replaces the pointer-events table. **State classes on the root** (`diving`,
  `underground`, `menu-open`, `scope-on`, `viewfinder`, `usingpad`, `nolock`) are fine USS practice and stay.

### 2.2 What goes

| Goes | Replaced by |
|---|---|
| `UiNode`, `UiX` | UXML elements and controllers |
| Tag classes (`.t-div`, `.t-h1`, ... 86 rules) | Real class names (`.heading`, `.body-text`) or Unity's `Label` styles |
| The `.unity-label` repeats (16 rules) | Text set on `Label` elements directly, so there is no inner label to fix |
| `TextMarkup` | Plain strings; `<b>` and `<i>` as rich text where a text needs them (nearly none do) |
| The `html > body` tree in `Page` | `Root.uxml` layers (Page keeps pointer lock, metrics, typing and the tick) |
| Runtime `SvgRasterizer`, `ShopArt` SVG strings | An editor menu that bakes every icon and shop picture to sprites in `Resources/UI/Icons`, as `EmojiSpriteAssetBuilder` does for emoji. Baked at 2x for the largest size each is shown at |
| Most of `UiAnim` | USS transitions for card pop, camp slide and the menu and sleep fades. A small `UiAnim` stays for the two loops USS cannot do (key bob, z's) |
| The structural helper classes (`.even`, `.nth-1`, `has-check`, the polaroid pin) | Set by the templates directly |
| The CPU overlays of 1.5 (not the big map's base) | `Painter2D` in `generateVisualContent`, as `TelescopeView` and `LoadingSea` already do |

### 2.3 How far to go, and where it is not worth it

**Recommended: all screens to UXML plus controllers, list items as templates, icons baked, overlays to `Painter2D`, and
`Style.uss` split per screen with the DOM workarounds removed.** That is the point at which nothing page-shaped is left
and the UI can be edited in UI Builder.

Not worth it:

- **`TelescopeView`'s sky and `LoadingSea`:** already GPU-native. Only their few text elements move.
- **The big map's base picture:** it is drawn from scenery data. As a `TexturePainter` picture it matches pixel for pixel,
  and as `Painter2D` it would need redrawing for nothing. Its markers can move to `Painter2D`.
- **`MiniMapGpu`:** already native.
- **UI Toolkit's own data binding** (`dataSource`, `[CreateProperty]`): the HUD changes a handful of labels from game
  state that the controllers already hold. Plain assignments with a changed-value check are simpler and cheaper.
  Revisit only if a screen grows a lot of bound fields.
- **A pixel-exact copy of today's text layout through every step:** keep it where the harness can check it, but expect
  sub-pixel changes when text moves from an inner Label to the element itself. Those are signed off as a group (D2).
- **Splitting `Game.cs` beyond its UI and menu code:** that is a refactor, not a migration (Part B, B6).

---

## 3. Navigation (controller, keyboard and mouse)

Today the game runs its own navigation: the nearest button in the pressed direction, the next row first, scroll boxes
rolled back before leaving, the back-button rules, the chosen button remembered per menu, and the bottom menu bar.
`MenuNav` keeps its own highlight (`NavEl`) next to UI Toolkit's focus. UI Toolkit's built-in navigation is switched off,
because with both running the highlighted button and the pressed one drifted apart.

**Recommended (D4): keep the game's navigation rules and run them on UI Toolkit's focus.**

- **Move the navigation out of `Game.cs` into a `MenuNav` service.** `overlayRoot` becomes `ScreenStack.Top`; `padNav`,
  `menuAccept`, `menuKey`, `menuTick` and `backButton` move with their logic unchanged.
- **The highlight is `focusController.focusedElement`.** `NavEl` and the native-focus field go. Buttons are focusable,
  rows that are chosen whole are focusable, and controls inside rows are `focusable = false` (as `nofocus` does today).
- **A screen can mark its back button and its default button in UXML** (classes `back` and `default`), instead of the
  back button being found by matching words like "Back|Done|Close".
- **The mouse moving onto a button focuses it** (as now). The panel's built-in move, submit and cancel navigation stays
  switched off.

**Testing the mouse for real.** The parity runs switch the real mouse off, which is how a menu could ignore the mouse
and still pass every test. Two additions:

1. **`SelfCheckUi` grows:** every button on screen must be picked at its centre (as now), every scroll box must scroll
   on a wheel event, and every screen in `ScreenStack` gets opened once and checked.
2. **A mouse suite, `-autoshot-mouse`:** AutoShot adds a virtual `Mouse` device through the Input System (as it already
   adds a virtual gamepad) and drives it: it moves onto buttons and checks the highlight follows, clicks through the
   main menu into settings and back, drags a slider, scrolls Help and clicks a camp tab. Everything goes through the
   real input path (Input System, then the panel, then picking, then the click). U0 starts with a short spike to confirm
   that UI Toolkit takes events from a virtual Input System mouse in a player build. If it does not, the suite sends
   pointer events to the panel directly, which still checks picking and handlers.

---

## 4. Order and steps (Part A)

Each step is shippable on its own. Screens move one at a time; until the last step both kinds of element coexist under
`Root.uxml`, as page and native elements coexisted during S26 to S33.

| Step | What | Suites |
|---|---|---|
| **U0** | **Harness first** (section 5): fixed test settings in place of yours, a display guard, a fresh reference, the virtual-mouse spike and `-autoshot-mouse`, `SelfCheckUi` grown | all + mouse |
| **U1** | **Foundation:** `Root.uxml` and its layers, `ScreenStack`, a `UiScreen` controller base, `MenuNav` moved out of `Game.cs` onto UI Toolkit focus, the icon baker (sprites for the 6 tool icons and the 36 shop pictures), `Common.uss` split out of `Style.uss` (the skin, buttons, cards, keys) | all + mouse + pad |
| **U2** | **Pilot: the camp.** The riskiest screen: the largest, rebuilt on every state change, nine pages, focus and scroll kept across rebuilds, baked shop pictures, photo textures, tilted polaroids. Its pages become templates, and rebuilds update rows in place instead of rebuilding the page. **Calibration gate:** measure the time against the estimate and re-plan before U3 | shop + tour + photo + ui + pad + mouse |
| **U3** | Settings and saves: `ChoiceRow`, `ToggleRow`, the slider, `TextField` with Steam's keyboard, the save row template, the two-press delete | settings + saves + pad + mouse |
| **U4** | Main menu, pause, Help, Achievements, Credits, farewell, ending | ui + tour + pad + layout + mouse |
| **U5** | Journal, letters, story pages, the sleep veil | tour + ui |
| **U6** | HUD: stats, chips, tool badge with baked icons, d-pad tools, prompts and hint with key templates, toasts, crosshair, draw bar, dive tint, fade, menu bar. Per-frame updates use changed-value checks, and the HUD's frame cost is measured (IL2CPP) | core + gear + pad + dograin + perf |
| **U7** | Inspect and result, intro, opening caption and flash, loading screen | crust + opening + newgame + base (loading shot) |
| **U8** | Overlays: binocular lenses, legend and readout, viewfinder, photo flash, detector waveform and minimap markers to `Painter2D`; the big map screen to UXML (base stays a painter picture) | islands + telescope + gear + photo + perf |
| **U9** | The telescope's labels, count, message and card to UXML | telescope + islands |
| **U10** | **Clean-up:** delete `UiNode`, `UiX`, `TextMarkup`, the runtime `SvgRasterizer`, most of `UiAnim`, the `html/body` tree in `Page`, the tag-class rules, the `.unity-label` repeats and every DOM-era note in `Style.uss` (by then split per screen); update `MIGRATION.md`, `PORT_STATUS.md` and the `Style.uss` header | all + mouse + perf |

**Why the camp is the pilot:** it holds every hard case at once (templates, rebuilds, focus memory, scrolling, pictures,
textures, rotation), and it sits behind one `open`/`close`. If the approach holds there, the rest is repetition. The
settings screen is the second-hardest (custom controls), so it comes straight after.

---

## 5. Verification

The parity harness (`unity/Tools/Parity`) stays the gate. Three fixes come first (U0), because two of them broke the last
comparison:

1. **Fixed settings for test runs.** Both the reference build and the branch read the player's own settings from
   PlayerPrefs (`HKCU\Software\Cozy Beach\Tidefinder`: `cozy-treasure-hunter-settings-v1` and Unity's `Screenmanager`
   values). After you played, VSync was off and fullscreen was set at 2560x1440, so the runs no longer matched the
   reference. **Recommended (D7):** `run_suites.ps1` and `run_perf.ps1` back up those values, write a fixed test set
   (VSync on, windowed 1280x720, sharp off, render scale 1, the volumes the reference used), run, and restore yours
   afterwards, even when a run fails. This also works for the frozen reference build, which cannot be changed.
2. **A display guard.** Every run logs its display (refresh, dpi, OS scale) in its `context` line. `compare.py` refuses
   to compare runs whose context differs from the reference's and says so, instead of reporting picture differences.
   The display changed twice during the rewrite (240 Hz/96 dpi, then 144 Hz/144 dpi, now 360 Hz/96 dpi).
3. **A fresh reference** from the `native-baseline` build on today's display, with the fixed settings: shots, logs and
   scene dumps. From U1 on, each step compares against **the step before it** (the shots of `main` at its last commit),
   not only against the old browser-shaped build. Any intended change is signed off once and becomes the new reference.

Per step: every suite exits cleanly; logs identical (a layout change that moves boxes in the controller log is checked
and signed off); shots within thresholds (REVIEW opened and explained); scene dumps unchanged; `SelfCheckUi` and the
mouse suite pass. IL2CPP perf is measured at U1, U6, U8 and U10 against the previous measurement. Pass criteria are as in
`NATIVE_PLAN.md` 4.4.

**At each phase boundary** (after U2, U5, U8 and U10) you play for about 15 minutes with a controller, the keyboard and
the mouse.

---

## 6. Part B: everything else that still needs migrating

Found by going through the code, the tools and the docs after the merge.

### B1. The world model is still Three.js-shaped (the largest item)

`Assets/Scripts/Three` has 7,408 lines: `Object3D`, `Mesh`, `Group`, `BufferGeometry`, the geometry generators, `Curves`
and `Earcut`, materials, `Vector3`/`Quaternion`/`Matrix4`/`Color`, cameras and lights. Game code makes **4,220
references**: World 2,884, Mechanics 1,112, Native 267, Core 209, UI 15. Modules build descriptions, and `Realizer`,
`Realized.Follow/Apply` and `LiveTree` copy them onto GameObjects every frame. That copying is what the S34 perf pass had
to tune.

**Recommended: after the UI, convert module by module to author GameObjects and Transforms directly**, which was the
native plan's original intent:

- the geometry generators become `MeshKit` generators (same vertices, same order);
- material descriptions become `MaterialKit` specs;
- the math types become Unity's or a small double-precision `GameSpace` type where the game needs doubles;
- each module drops its `Realized`/`LiveTree`.

Keep the order of `NATIVE_PLAN.md` section 3 (leaves first, hubs last; Stones and Flags are already direct). At the end,
`Three/` keeps only what was always meant to stay: the geometry maths in `MeshKit`, `Earcut`, `Curves`, `ColorUtil` and
`TexturePainter`. The parity harness and its scene dumps guard this the same way they guarded the rewrite. It is the step
with the most risk to behaviour, because `JS.Random` must be drawn in the same order.

### B2. The renderer front (`WebGLRenderer`, namespace `THREE.Render`)

The game still calls `renderer.render(scene, camera)` (4 calls), `setAnimationLoop`, `shadowMap`, `setPixelRatio`,
`setRenderTarget`, `compile` and `CapturePhoto`. Photos read pixels back synchronously (`ReadPixels`) and encode the JPG
on the main thread; the Deck plan (3.9) asks for `AsyncGPUReadback` and encoding off the main thread.

**Recommended:** `GameHost` drives the frame directly, `Cameras` and `Photo` services replace the renderer front, photos
use `AsyncGPUReadback`, and `MaterialCache`/`TextureCache`/`UrpSettings`/`FrameStats` leave the `THREE.Render`
namespace. The materials' `bridgeEntry`, `bridgeKind` and `bridgeGen` fields are renamed. Do this together with B1 or
just before it.

### B3. Input reads browser key codes

`InputManager` turns Unity keys into browser `KeyboardEvent.code` strings (`"KeyW"` 26 uses, `"Escape"` 14, `"Space"`
13, ...), scales the mouse by "CSS pixels", and raises browser-style handlers (`onKey(code)`, `onPointerDown`). The
gamepad is a separate path (`GamepadInput`).

**Recommended:** one Input System action map for the keyboard, mouse and controller. The game's handlers become actions
with the same meaning, and the mouse scale is kept as a sensitivity factor so look speed does not change. That gives key
rebinding (often expected on PC) and a clean path to Steam Input. It changes no look, so the logs are the gate. It is
medium size and independent of the UI work.

### B4. Docs and comments that describe deleted code

- `Style.uss` header: `DomHost`, `CssAnimations`, `HTMLElement` (it is fixed in U10 anyway, or earlier as a doc fix).
- 87 comments in code still mention "the bridge" and 11 mention the DOM layer.
- `STEAM_DECK_PLAN.md` has 23 stale paths (for example `Three/Bridge/UrpSettings.cs`, and line numbers in `UIManager.cs`).
- `PORT_STATUS.md` has 5 (its notes on `HTMLElement.ApplyDisplay`, `FitPillRadius` and `DisplayText`).
- `NATIVE_PLAN.md` is a historical record: leave it.

**Recommended:** one small commit before U0, so nobody follows a stale path.

### B5. The harness and the player share settings

This is covered in section 5. It is listed here because it is a migration leftover too: the browser build kept settings
in `localStorage`, and the port kept one store for both.

### B6. Kept on purpose (not migrations)

- **`JS` helpers** (`JS.Random` 275 uses, `JS.Hypot` 194, ...): the game's maths and its seeded randomness. Keep them.
- **`TexturePainter` (Canvas2D) and `CanvasFonts`:** world textures and signs, kept for exact pixels (NATIVE_PLAN
  decision 2).
- **`AudioManager`'s synth** (`OnAudioFilterRead` oscillators and filtered noise): a port of the browser's Web Audio
  graph, but already native Unity audio. Keep it unless the sound is to change.
- **`GameTimers`:** what keeps test runs deterministic.
- **`Game.cs` at 6,963 lines:** splitting it is a refactor. Only its UI and menu code moves, in U1 and U6.

---

## 7. Risks

| # | Risk | Likelihood / impact | Mitigation |
|---|---|---|---|
| 1 | **Text layout shifts** when text moves from an inner Label to the element, or when tag-class rules become real classes: line breaks, ellipses, label heights | High / low | Screen by screen with shots; the controller log shows box changes; group sign-off (D2) |
| 2 | **Focus behaviour changes** when the highlight moves onto UI Toolkit focus: hover stealing focus, focus lost on rebuild, the camp's focus memory | Medium / high | The controller log and the new mouse suite; the camp pilot first; focus restore in the `UiScreen` base |
| 3 | **The mouse breaks again unseen** | Medium / high | `-autoshot-mouse` and a grown `SelfCheckUi` from U0, in every step's suites |
| 4 | **The built-in `Slider` and `ScrollView` look different** from the skinned page versions (scroller, drag, wheel speed) | Medium / medium | Style them through the theme; if they cannot match, a small custom control (as `ChoiceRow`) |
| 5 | **Baked icons look different** from runtime-rasterised ones (anti-aliasing, size) | Low / low | Bake at 2x the largest shown size with the same rasteriser; compare the shots |
| 6 | **The test settings swap leaves the player's settings changed** if a run is killed | Low / medium | Back up to a file first; restore in a `finally` and at the next run's start if a backup is found |
| 7 | **The HUD's per-frame cost rises** with controllers instead of direct element writes | Low / medium | Changed-value checks; IL2CPP perf at U6 |
| 8 | **B1 changes behaviour** through the order of `JS.Random` draws | Medium / high | Same rule as the rewrite: draws stay in place; logs compared every step; seeded runs |

---

## 8. Estimate and order

**Calibration:** the native rewrite went far faster than planned. The UI track S26 to S33 was planned at 15 to 24 days
and took about a day of agent time, with a full parity pass after every part. Rebuilding the same screens as UXML and
templates is more design work than mirroring them, so this plan counts roughly 1.5 to 2 times that, and the camp pilot
(U2) re-measures it.

| Work | Planned for a person | Expected with the agent | Confidence |
|---|---|---|---|
| B4 docs | 0.5 day | an hour | High |
| U0 harness (settings, guard, reference, mouse spike and suite) | 2 to 3 days | half a day | Medium (the virtual-mouse spike) |
| U1 foundation | 3 to 4 days | half a day to a day | Medium |
| U2 camp pilot | 3 to 4 days | half a day | Medium; gate |
| U3 to U9 | 8 to 12 days | 1.5 to 3 days | Medium |
| U10 clean-up | 1 to 2 days | a few hours | High |
| **Part A total** | **17 to 25 days** | **about 3 to 5 days** | |
| B3 input actions | 3 to 4 days | half a day to a day | Medium |
| B2 renderer front and photos | 2 to 3 days | half a day | Medium |
| B1 world model | 20 to 30 days | 4 to 7 days | Low to medium (the largest change to behaviour) |

**Recommended order:** B4, then U0 to U10 (with B3 slotted in any time after U1, since it is independent), then B2, then
B1. The UI comes first because its gaps are the visible ones; B1 last because it is the largest and gains nothing a player
can see apart from frame time.

---

## 9. Decisions for you

| # | Decision | Recommended | Other choice |
|---|---|---|---|
| D1 | How far to take the UI | **All screens to UXML plus controllers, list items as templates, icons baked, overlays to `Painter2D`, `Style.uss` split with the workarounds gone** (2.3) | UXML only for fixed layouts and C# builders for lists, as NATIVE_PLAN decision 3 said (less to do, but the C#-built lists stay hard to edit) |
| D2 | Small visible changes | **Allowed where text moves from an inner Label to its element: sub-pixel shifts, signed off per group of screens** | Pixel parity at every step (slower, and fights the new structure) |
| D3 | Fonts | **Bundled fonts on every platform, as font assets made at build time**: one look on PC and Deck, no reliance on OS fonts. This is a visible change on PC (Fira Sans and PT Serif in place of Trebuchet and Georgia), so it needs your OK | Keep OS fonts on Windows and bundled ones on the Deck (today) |
| D4 | Navigation | **Keep the game's own navigation rules, run on UI Toolkit's focus, moved out of `Game.cs`** (section 3) | UI Toolkit's built-in navigation (simpler, but it moved differently from the game before and the two drifted) |
| D5 | Testing the mouse | **A virtual Input System mouse in AutoShot (`-autoshot-mouse`) plus a grown `SelfCheckUi`** | Self-checks only (cheaper; misses handler and focus bugs) |
| D6 | Part B | **Do B4 now; B3 during the UI work; B2 and then B1 after the UI** | Stop after the UI and keep the Three-shaped model (it works; the cost is the per-frame sync and the browser-shaped code) |
| D7 | Test settings | **The scripts swap in fixed settings and restore yours** (works with the frozen reference build) | A `-settings <file>` flag (cleaner, but the reference build would have to be rebuilt to understand it) |

Branch: `claude/ui-rebuild`, from `main` at 0af08c1. Each step is committed when its full pass is clean; nothing is
pushed unless asked.
