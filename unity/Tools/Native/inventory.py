"""Counts what is left of the Three.js and DOM emulation in the game code (unity/NATIVE_PLAN.md, section 1).

    python unity/Tools/Native/inventory.py            # tables for the plan, as Markdown
    python unity/Tools/Native/inventory.py --totals   # one line per folder and unit, to track progress per step

Plain Python, nothing to install. Counts are of source text with whole-line // comments left out, so they are a measure of
how much code touches each layer, not an exact parse.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.normpath(os.path.join(HERE, '..', '..', 'Assets', 'Scripts'))

CANVAS_API = (
    'createLinearGradient|createRadialGradient|createImageData|getImageData|measureText|imageSmoothingEnabled|'
    'globalAlpha|lineDashOffset|lineWidth|miterLimit|shadowBlur|shadowOffsetX|shadowOffsetY|getLineDash|fillStyle|'
    'strokeStyle|filter|font|globalCompositeOperation|lineCap|lineJoin|shadowColor|textAlign|textBaseline|arc|arcTo|'
    'beginPath|bezierCurveTo|clearRect|closePath|drawImage|ellipse|fill|fillRect|fillText|lineTo|moveTo|putImageData|'
    'quadraticCurveTo|rect|resetTransform|restore|rotate|roundRect|save|scale|setLineDash|setTransform|stroke|'
    'strokeRect|strokeText|transform|translate|addColorStop'
)

PATTERNS = {
    # every use of the C# Three.js copy by name (types, enums, BufferGeometryUtils, the bridge)
    'three': r'\bTHREE\.',
    # the DOM-style calls named in the brief: el(...), classList, innerHTML, document., style.
    'dom': r'\bel(<[A-Za-z]+>)?\(|\bclassList\b|\binnerHTML\b|\bdocument\.|\bstyle\.',
    # the same plus the rest of the browser API the game code calls
    'domw': r'\bel(<[A-Za-z]+>)?\(|\bclassList\b|\binnerHTML\b|\bdocument\.|\bstyle\.|\bwindow\.|\bquerySelector(All)?\(|'
            r'\baddEventListener\(|\bcreateElement\(|\btextContent\b|\bsetAttribute\(|\bonclick\b|'
            r'\bgetBoundingClientRect\(|\bHTML[A-Za-z]*Element\b',
    # the canvas calls named in the brief
    'c2d': r'\bgetContext\(|\bfillText\(|\bfillRect\(|\bstrokeRect\(|\bdrawImage\(|\bmeasureText\(',
    # every CanvasRenderingContext2D member used on a context variable (g, ctx, c, x, bg, ...)
    'call': r'\b(g|ctx|c|x|bg|t|sampleCtx|waveCtx|miniCtx|grad|gr)\.(' + CANVAS_API + r')\b(?!\.)|\bgetContext\(',
    # canvases made
    'canv': r'getContext\("2d"',
    # Object3D members used without the THREE. prefix (transforms, children, flags, materials)
    'graph': r'\.position\.|\.rotation\.|\.scale\.|\.quaternion\.|\.add\(|\.remove\(|\.visible\b|\.traverse\(|\.userData\b|'
             r'\.getWorldPosition\(|\.lookAt\(|\.updateMatrixWorld\(|castShadow|receiveShadow|renderOrder|frustumCulled|'
             r'\.material\b|\.geometry\b',
}
COLS = ['three', 'dom', 'domw', 'c2d', 'call', 'canv', 'graph']
HEAD = ('| Unit | File | Lines | `THREE.` | DOM (brief) | DOM (wide) | 2D (brief) | 2D (all) | Canvases | Scene-graph members |\n'
        '|---|---|---:|---:|---:|---:|---:|---:|---:|---:|')

UNITS = [
    ('C0 Harness', 'Core/AutoShot.cs'),
    ('C1 Host, loop, input', 'Core/GameHost.cs Core/Prebuilt.cs Core/MainThread.cs Core/FramePacing.cs Core/InputManager.cs Core/GamepadInput.cs'),
    ('C2 Game hub', 'Core/Game.cs'),
    ('C3 Particles, opening', 'Core/Particles.cs Core/Opening.cs'),
    ('C4 Pure logic (no change)', 'Core/Achievements.cs Core/AudioManager.cs Core/Catches.cs Core/Constellations.cs Core/Help.cs '
                                  'Core/Items.cs Core/Journal.cs Core/Mail.cs Core/Photos.cs Core/Platform.cs Core/SaveGame.cs '
                                  'Core/Settings.cs Core/StateManager.cs Core/SteamService.cs Core/Storage.cs Core/Story.cs'),
    ('W1 Pilot props', 'World/Stones.cs World/Flags.cs World/Pier.cs World/Whitecaps.cs'),
    ('W2 Small props', 'World/SeaTreasure.cs World/SeaModels.cs World/Telescope.cs World/GolfCart.cs World/Aquarium.cs '
                       'World/Dolphins.cs World/ShipKit.cs World/CoveNets.cs'),
    ('W3 Weather, tracks, critters', 'World/Rain.cs World/Tracks.cs World/Grass.cs World/Ecology.cs World/Birds.cs World/Sandcastles.cs'),
    ('W4 Vehicles, dog, ships', 'World/Boat.cs World/Dog.cs World/ModernShips.cs World/OldShip.cs'),
    ('W5 Terrain and atmosphere', 'World/Beach.cs World/Conform.cs World/Ocean.cs World/Sky.cs'),
    ('W6 Palms and scenery', 'World/Scenery.cs'),
    ('W7 Camp', 'World/Camp.cs'),
    ('W8 Islands', 'World/Island.cs'),
    ('W9 Lighthouse', 'World/Lighthouse.cs'),
    ('W10 Old house and pictures', 'World/OldHouse.cs World/OldPictures.cs'),
    ('W11 Under the sea', 'World/SeaCave.cs World/SeaFlora.cs World/SeaCreatures.cs World/SeaLife.cs World/Underwater.cs World/UnderwaterDecor.cs'),
    ('W12 Underground', 'World/Underground.cs'),
    ('W13 Loot', 'World/LootSpawner.cs Mechanics/LootGenerator.cs'),
    ('M1 Tools in hand', 'Mechanics/ViewModel.cs Mechanics/Hand.cs Mechanics/DigTool.cs Mechanics/DetectorTool.cs '
                         'Mechanics/Fishing.cs Mechanics/Arrows.cs Mechanics/Bullets.cs'),
    ('M2 Workbench and item photos', 'Mechanics/CleaningStation.cs UI/ItemPhotos.cs'),
    ('I1 Loading screen', 'UI/LoadingSea.cs'),
    ('I2 HUD, menus, settings', 'UI/UIManager.cs UI/Glyphs.cs UI/PadText.cs'),
    ('I3 Camp screens', 'UI/CampModal.cs UI/ShopArt.cs'),
    ('I4 Map, minimap, telescope', 'UI/MapView.cs UI/MiniMapGpu.cs UI/TelescopeView.cs'),
]


def main():
    regex = {k: re.compile(v) for k, v in PATTERNS.items()}
    unit_of = {}
    for name, files in UNITS:
        for f in files.split():
            unit_of[f] = name
    rows = []
    for folder in ['Core', 'World', 'Mechanics', 'UI']:
        for f in sorted(os.listdir(os.path.join(SCRIPTS, folder))):
            if not f.endswith('.cs'):
                continue
            rel = f'{folder}/{f}'
            src = open(os.path.join(SCRIPTS, rel), encoding='utf-8-sig').read()
            code = '\n'.join(re.sub(r'^\s*//.*$', '', line) for line in src.split('\n'))
            counts = {k: len(r.findall(code)) for k, r in regex.items()}
            rows.append((unit_of.get(rel, '?? (new file: add it to UNITS)'), rel, src.count('\n') + (0 if src.endswith('\n') else 1), counts))

    order = [u for u, _ in UNITS] + sorted({r[0] for r in rows} - {u for u, _ in UNITS})
    totals_only = '--totals' in sys.argv
    if not totals_only:
        print(HEAD)
        for u in order:
            for r in sorted((r for r in rows if r[0] == u), key=lambda r: -(r[3]['three'] + r[3]['domw'] + r[3]['call'])):
                c = r[3]
                print(f"| {u.split(' ')[0]} | `{r[1]}` | {r[2]} | " + ' | '.join(str(c[k]) for k in COLS) + ' |')
        tot = {k: sum(r[3][k] for r in rows) for k in COLS}
        print(f"| | **Total ({len(rows)} files)** | **{sum(r[2] for r in rows)}** | " + ' | '.join(f'**{tot[k]}**' for k in COLS) + ' |')
        print()
        print(HEAD.replace('| File |', '| Files |').replace('|---|---|', '|---|---:|'))
    for u in order:
        rs = [r for r in rows if r[0] == u]
        if not rs:
            continue
        s = {k: sum(r[3][k] for r in rs) for k in COLS}
        print(f'| {u} | {len(rs)} | {sum(r[2] for r in rs)} | ' + ' | '.join(str(s[k]) for k in COLS) + ' |')
    print()
    print('| Folder | Files | Lines | `THREE.` | DOM (brief) | DOM (wide) | 2D (brief) | 2D (all) | Canvases | Scene-graph members |')
    print('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    for folder in ['Core', 'World', 'Mechanics', 'UI']:
        rs = [r for r in rows if r[1].startswith(folder + '/')]
        s = {k: sum(r[3][k] for r in rs) for k in COLS}
        print(f'| {folder} | {len(rs)} | {sum(r[2] for r in rs)} | ' + ' | '.join(str(s[k]) for k in COLS) + ' |')


if __name__ == '__main__':
    main()
