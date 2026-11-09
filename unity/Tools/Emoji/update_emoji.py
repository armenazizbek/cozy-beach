"""
Rebuilds the colour emoji atlas the Unity UI uses in place of system emoji (Unity's text engine cannot draw colour emoji fonts).

    python unity/Tools/Emoji/update_emoji.py

1. Scans unity/Assets/Scripts for emoji.
2. Downloads the Flat SVG of any emoji not yet in svg/ from Microsoft's Fluent Emoji (MIT, github.com/microsoft/fluentui-emoji),
   checking each against Fluent's own metadata so the right picture is used.
3. Renders all of svg/ into unity/Assets/Art/Emoji/EmojiAtlas.png with Microsoft Edge (headless, transparent background), and
   writes EmojiAtlas.json next to it.

Then, in Unity: menu "Cozy Treasure Hunter > Rebuild Emoji Sprite Asset" (also run by every build) turns the atlas into
Assets/Resources/UI/Emoji.asset.
"""
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
UNITY = os.path.normpath(os.path.join(HERE, '..', '..'))
SCRIPTS = os.path.join(UNITY, 'Assets', 'Scripts')
SVG_DIR = os.path.join(HERE, 'svg')
OUT_DIR = os.path.join(UNITY, 'Assets', 'Art', 'Emoji')

CELL, PAD, COLS = 96, 4, 10  # each emoji is drawn 88 px wide in a 96 px cell

EMOJI = re.compile(
    r'(?:[\U0001F000-\U0001FAFF\u2600-\u27BF\u2B00-\u2BFF\u2190-\u21FF\u2300-\u23FF\u3030\u303D\u3297\u3299\u2139\u24C2'
    r'\u25AA-\u25FE\u2934\u2935](?:\uFE0F|\u200D|[\U0001F3FB-\U0001F3FF])*)+')
# plain text symbols the fonts already draw well
TEXT_SYMBOLS = {0x2713, 0x25BE, 0x25C6, 0x25C7}
# Fluent folders are named after CLDR short names, which sometimes differ from Unicode names
FLUENT_NAMES = {
    0x1F52B: 'Water pistol', 0x1F3EE: 'Red paper lantern', 0x2600: 'Sun', 0x2753: 'Red question mark',
    0x2754: 'White question mark', 0x2B50: 'Star', 0x267B: 'Recycling symbol', 0x1F4A1: 'Light bulb',
    0x1F37E: 'Bottle with popping cork', 0x1F3D5: 'Camping', 0x1F5D1: 'Wastebasket', 0x1F54A: 'Dove',
    0x1F327: 'Cloud with rain', 0x1F6CF: 'Bed', 0x1F50A: 'Speaker high volume', 0x1F507: 'Muted speaker',
    0x1F6A9: 'Triangular flag', 0x1F531: 'Trident emblem', 0x1F93F: 'Diving mask', 0x1F43E: 'Paw prints',
    0x1F4A5: 'Collision', 0x1F30A: 'Water wave', 0x1F4CD: 'Round pushpin', 0x2699: 'Gear', 0x1F39E: 'Film frames',
}
RAW = 'https://raw.githubusercontent.com/microsoft/fluentui-emoji/main/'


def used_emoji():
    found = []
    for root, _, files in os.walk(SCRIPTS):
        for f in files:
            if not f.endswith('.cs'):
                continue
            with open(os.path.join(root, f), encoding='utf-8') as fh:
                for seq in EMOJI.findall(fh.read()):
                    base = [ord(c) for c in seq if c not in '\uFE0F\u200D']
                    for cp in base:
                        if cp not in TEXT_SYMBOLS and not 0x1F3FB <= cp <= 0x1F3FF and cp not in found:
                            found.append(cp)
    return sorted(found)


def fetch_missing(cps):
    missing = [cp for cp in cps if not os.path.exists(os.path.join(SVG_DIR, '%x.svg' % cp))]
    if not missing:
        return
    req = urllib.request.Request('https://api.github.com/repos/microsoft/fluentui-emoji/git/trees/main?recursive=1',
                                 headers={'User-Agent': 'cozy-treasure-hunter-emoji-tool'})
    tree = json.loads(urllib.request.urlopen(req).read())['tree']
    flat = {}
    for e in tree:
        m = re.match(r'assets/([^/]+)/(?:Default/)?Flat/[^/]+\.svg$', e['path'])
        if m:
            flat.setdefault(m.group(1).lower(), (m.group(1), e['path']))
    os.makedirs(SVG_DIR, exist_ok=True)
    for cp in missing:
        names = [FLUENT_NAMES.get(cp, ''), unicodedata.name(chr(cp), '')]
        hit = next((flat[n.lower()] for n in names if n and n.lower() in flat), None)
        if not hit:
            print('  no Fluent emoji found for U+%04X %s: add it to FLUENT_NAMES' % (cp, names[1]))
            continue
        folder, svg = hit
        meta = json.loads(urllib.request.urlopen(RAW + urllib.parse.quote('assets/%s/metadata.json' % folder)).read())
        if meta.get('unicode', '').split()[0].lower() != '%x' % cp:
            print('  U+%04X matched "%s", which is %s: add the right name to FLUENT_NAMES' % (cp, folder, meta.get('unicode')))
            continue
        with open(os.path.join(SVG_DIR, '%x.svg' % cp), 'wb') as fh:
            fh.write(urllib.request.urlopen(RAW + urllib.parse.quote(svg)).read())
        print('  fetched U+%04X %s' % (cp, folder))


def find_edge():
    for p in (r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
              r'C:\Program Files\Microsoft\Edge\Application\msedge.exe'):
        if os.path.exists(p):
            return p
    sys.exit('Microsoft Edge was not found; it is used to draw the SVGs.')


def render_atlas():
    cps = sorted(int(f[:-4], 16) for f in os.listdir(SVG_DIR) if f.endswith('.svg'))
    rows = (len(cps) + COLS - 1) // COLS
    w, h = COLS * CELL, rows * CELL
    tmp = tempfile.mkdtemp(prefix='emoji-atlas-')
    try:
        cells = []
        for i, cp in enumerate(cps):
            src = urllib.parse.quote(os.path.join(SVG_DIR, '%x.svg' % cp).replace('\\', '/'))
            x, y = (i % COLS) * CELL + PAD, (i // COLS) * CELL + PAD
            cells.append('<img src="file:///%s" style="left:%dpx;top:%dpx">' % (src, x, y))
        page = os.path.join(tmp, 'atlas.html')
        with open(page, 'w', encoding='utf-8') as fh:
            fh.write('<!doctype html><html><head><style>html,body{margin:0;background:transparent;overflow:hidden}'
                     'img{position:absolute;width:%dpx;height:%dpx}</style></head><body>%s</body></html>'
                     % (CELL - 2 * PAD, CELL - 2 * PAD, ''.join(cells)))
        os.makedirs(OUT_DIR, exist_ok=True)
        png = os.path.join(OUT_DIR, 'EmojiAtlas.png')
        subprocess.run([find_edge(), '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
                        '--default-background-color=00000000', '--user-data-dir=' + os.path.join(tmp, 'profile'),
                        '--allow-file-access-from-files', '--window-size=%d,%d' % (w, h), '--screenshot=' + png,
                        'file:///' + page.replace('\\', '/')], check=True, capture_output=True, timeout=120)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    with open(os.path.join(OUT_DIR, 'EmojiAtlas.json'), 'w', encoding='utf-8') as fh:
        json.dump({'cell': CELL, 'padding': PAD, 'columns': COLS, 'width': w, 'height': h,
                   'unicodes': ['%x' % cp for cp in cps]}, fh, indent=1)
    print('atlas: %d emoji, %dx%d -> %s' % (len(cps), w, h, png))


if __name__ == '__main__':
    cps = used_emoji()
    print('%d emoji used in the game code' % len(cps))
    fetch_missing(cps)
    render_atlas()
