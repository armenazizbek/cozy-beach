"""Draws the game's UI skin: the panels, buttons, key caps, controller buttons, meters and frames that Style.uss stretches
over the menus and the HUD.

    python Tools/UiSkin/make_skin.py

writes PNG files to Assets/Resources/UI/Skin. Run it again after changing a colour or a shape here. It needs nothing but
Python's standard library. Everything is drawn at twice the size it is shown (Style.uss halves it again with
-unity-slice-scale: 0.5px or a fixed width and height), so edges stay crisp on a 4K screen.

The look: flat colours, a thick dark outline and a hard "lip" under each button, to sit with the low-poly world.
"""
import math
import os
import struct
import sys
import zlib

K = 2  # texture pixels per UI pixel

INK = (42, 28, 20)
PAPER = (251, 242, 222)
PAPER_LIP = (226, 205, 164)
PAPER_IN = (241, 228, 198)
CORAL = (234, 106, 71)
CORAL_HI = (246, 139, 104)
CORAL_LIP = (176, 62, 40)
TEAL = (47, 150, 138)
TEAL_HI = (84, 180, 166)
TEAL_LIP = (28, 100, 93)
GOLD = (255, 200, 74)
GOLD_LIP = (212, 146, 40)
NAVY = (26, 36, 50)
LEAF = (111, 174, 79)
LEAF_LIP = (70, 122, 48)
WHITE = (255, 255, 255)


def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else hi if v > hi else v


class Img:
    def __init__(self, w, h):
        """w and h in UI pixels"""
        self.w, self.h = int(round(w * K)), int(round(h * K))
        self.px = [[0.0, 0.0, 0.0, 0.0] for _ in range(self.w * self.h)]

    def fill(self, sdf, color, alpha=1.0, clip=None):
        """Paints where sdf < 0 (distances in UI pixels), with a one-texel soft edge."""
        r, g, b = color
        for y in range(self.h):
            fy = (y + 0.5) / K
            row = y * self.w
            for x in range(self.w):
                fx = (x + 0.5) / K
                c = clamp(0.5 - sdf(fx, fy) * K)
                if c <= 0:
                    continue
                if clip is not None:
                    c = min(c, clamp(0.5 - clip(fx, fy) * K))
                    if c <= 0:
                        continue
                sa = c * alpha
                p = self.px[row + x]
                da = p[3]
                oa = sa + da * (1 - sa)
                if oa <= 0:
                    continue
                k = da * (1 - sa)
                p[0] = (r * sa + p[0] * k) / oa
                p[1] = (g * sa + p[1] * k) / oa
                p[2] = (b * sa + p[2] * k) / oa
                p[3] = oa

    def shade(self, fn):
        """Paints with a colour and alpha that change from texel to texel: fn(x, y) -> (r, g, b, a)."""
        for y in range(self.h):
            for x in range(self.w):
                r, g, b, a = fn((x + 0.5) / K, (y + 0.5) / K)
                self.px[y * self.w + x] = [r, g, b, a]

    def save(self, path):
        raw = bytearray()
        for y in range(self.h):
            raw.append(0)
            for x in range(self.w):
                p = self.px[y * self.w + x]
                raw += bytes((int(round(clamp(p[0], 0, 255))), int(round(clamp(p[1], 0, 255))),
                              int(round(clamp(p[2], 0, 255))), int(round(clamp(p[3]) * 255))))

        def chunk(tag, data):
            body = tag + data
            return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

        png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 6, 0, 0, 0))
        png += chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
        with open(path, "wb") as f:
            f.write(png)


# ---------------------------------------------------------------- shapes (signed distance, negative inside)

def rrect(x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    hw, hh = (x1 - x0) / 2, (y1 - y0) / 2
    r = min(r, hw, hh)

    def f(x, y):
        dx = abs(x - cx) - (hw - r)
        dy = abs(y - cy) - (hh - r)
        return math.hypot(max(dx, 0), max(dy, 0)) + min(max(dx, dy), 0) - r
    return f


def circle(cx, cy, r):
    return lambda x, y: math.hypot(x - cx, y - cy) - r


def poly(points, round_=0.0):
    """A convex polygon, points clockwise on screen (y down), corners rounded by round_."""
    edges = []
    segs = []
    n = len(points)
    for i in range(n):
        ax, ay = points[i]
        bx, by = points[(i + 1) % n]
        ex, ey = bx - ax, by - ay
        ln = math.hypot(ex, ey)
        edges.append((ax, ay, ey / ln, -ex / ln))  # outward normal for clockwise points with y down
        segs.append((ax, ay, ex, ey, ln * ln))

    def f(x, y):
        inside = max((x - ax) * nx + (y - ay) * ny for ax, ay, nx, ny in edges)
        if inside <= 0:
            return inside - round_
        best = min(
            math.hypot(x - (ax + clamp(((x - ax) * ex + (y - ay) * ey) / l2) * ex),
                       y - (ay + clamp(((x - ax) * ex + (y - ay) * ey) / l2) * ey))
            for ax, ay, ex, ey, l2 in segs
        )
        return best - round_
    return f


def grow(sdf, d):
    return lambda x, y: sdf(x, y) - d


def shift(sdf, dx, dy):
    return lambda x, y: sdf(x - dx, y - dy)


def both(a, b):
    return lambda x, y: max(a(x, y), b(x, y))


def minus(a, b):
    return lambda x, y: max(a(x, y), -b(x, y))


def above(yy):
    return lambda x, y: y - yy


# ---------------------------------------------------------------- pieces

def button(size, margin, outline, radius, lip, face, hi, lipc, ring=None, pressed=False):
    """A chunky button: outline, a darker lip along the bottom, a lighter strip along the top. `ring` is the focus ring,
    drawn in the margin that every button keeps free for it."""
    im = Img(size, size)
    a, b = margin, size - margin
    if ring is not None:
        im.fill(rrect(0.5, 0.5, size - 0.5, size - 0.5, radius + margin), INK)
        im.fill(rrect(1.75, 1.75, size - 1.75, size - 1.75, radius + margin - 1.25), ring)
    top = a + (lip - 1 if pressed else 0)
    im.fill(rrect(a, top, b, b, radius), INK)
    im.fill(rrect(a + outline, top + outline, b - outline, b - outline, radius - outline), lipc)
    f = rrect(a + outline, top + outline, b - outline, b - outline - (1 if pressed else lip), radius - outline)
    im.fill(f, face)
    im.fill(f, hi, clip=above(top + outline + 3))
    return im


def panel(size, outline, radius, lip, shadow, face, lipc, shadow_alpha=0.34):
    """A card: outline, a lip, and a hard shadow underneath (inside the picture, so the element's bottom padding allows for it)."""
    im = Img(size, size)
    box = rrect(0, 0, size, size - shadow, radius)
    im.fill(shift(box, 0, shadow), (20, 12, 8), shadow_alpha)
    im.fill(box, INK)
    im.fill(grow(box, -outline), lipc)
    im.fill(rrect(outline, outline, size - outline, size - shadow - outline - lip, radius - outline), face)
    return im


def plate(size, outline, radius, fill, fill_alpha, edge, edge_alpha, glint=0.10, ring=None):
    """A flat translucent plate for the HUD. With `ring`, the edge is a coloured line inside a dark one."""
    im = Img(size, size)
    box = rrect(0, 0, size, size, radius)
    im.fill(box, edge, edge_alpha)
    inset = outline
    if ring is not None:
        im.fill(grow(box, -1.5), ring)
        inset = outline + 1.5
    inner = grow(box, -inset)
    # the middle replaces the edge colour instead of being painted over it, so it stays see-through
    for i, p in enumerate(im.px):
        x, y = (i % im.w + 0.5) / K, (i // im.w + 0.5) / K
        c = clamp(0.5 - inner(x, y) * K)
        if c <= 0:
            continue
        p[0] = p[0] * (1 - c) + fill[0] * c
        p[1] = p[1] * (1 - c) + fill[1] * c
        p[2] = p[2] * (1 - c) + fill[2] * c
        p[3] = p[3] * (1 - c) + fill_alpha * c
    if glint > 0:
        im.fill(inner, WHITE, glint, clip=above(inset + 2))
    return im


def row(size, radius, fill, edge_alpha=0.16, ring=None):
    """An inset row for lists: items, saves, settings."""
    im = Img(size, size)
    m = 3
    if ring is not None:
        im.fill(rrect(0, 0, size, size, radius + m), INK)
        im.fill(rrect(1.5, 1.5, size - 1.5, size - 1.5, radius + m - 1.5), ring)
        im.fill(rrect(m + 1, m + 1, size - m - 1, size - m - 1, radius - 1), INK)
        im.fill(rrect(m + 2.5, m + 2.5, size - m - 2.5, size - m - 2.5, radius - 2.5), fill)
    else:
        im.fill(rrect(m, m, size - m, size - m, radius), INK, edge_alpha)
        im.fill(rrect(m + 1.5, m + 1.5, size - m - 1.5, size - m - 1.5, radius - 1.5), fill)
    return im


def round_button(size, face, lipc, hi):
    im = Img(size, size)
    c = size / 2
    im.fill(circle(c, c, c - 0.5), INK)
    im.fill(circle(c, c, c - 3), lipc)
    f = circle(c, c - 1.5, c - 4)
    im.fill(both(f, circle(c, c, c - 3)), face)
    im.fill(both(circle(c, c - 1.5, c - 6), minus(above(c - 2), circle(c, c + 2.5, c - 5))), hi, 0.55)
    return im


def bar_track():
    im = Img(32, 24)
    im.fill(rrect(0, 0, 32, 24, 12), INK)
    im.fill(rrect(3, 3, 29, 21, 9), (74, 55, 41))
    im.fill(rrect(3, 3, 29, 21, 9), (20, 12, 8), 0.35, clip=above(8))
    return im


def bar_fill(face, hi):
    im = Img(24, 18)
    f = rrect(0, 0, 24, 18, 9)
    im.fill(f, face)
    im.fill(f, hi, clip=above(6))
    return im


def toggle(on):
    w, h = 60, 32
    im = Img(w, h)
    im.fill(rrect(0, 0, w, h, h / 2), INK)
    im.fill(rrect(3, 3, w - 3, h - 3, h / 2 - 3), LEAF if on else (150, 132, 112))
    im.fill(rrect(3, 3, w - 3, h - 3, h / 2 - 3), LEAF_LIP if on else (116, 100, 84), clip=above(9))
    cx = w - h / 2 if on else h / 2
    im.fill(circle(cx, h / 2, h / 2 - 1.5), INK)
    im.fill(circle(cx, h / 2, h / 2 - 4.5), PAPER_LIP)
    im.fill(both(circle(cx, h / 2 - 1.5, h / 2 - 5), circle(cx, h / 2, h / 2 - 4.5)), PAPER)
    return im


def knob():
    s = 30
    im = Img(s, s)
    c = s / 2
    im.fill(circle(c, c, c - 0.5), INK)
    im.fill(circle(c, c, c - 3.5), PAPER_LIP)
    im.fill(both(circle(c, c - 1.5, c - 4), circle(c, c, c - 3.5)), PAPER)
    return im


def arrow(direction, face):
    s = 28
    im = Img(s, s)
    pts = [(8.5, 5), (21, 14), (8.5, 23)]
    if direction < 0:
        pts = [(s - x, y) for x, y in reversed(pts)]
    tri = poly(pts)
    im.fill(grow(tri, 3.2), INK)
    im.fill(grow(tri, 0.6), face)
    return im


def minimap_ring():
    """Sits over the round mini-map: 150 wide inside, a paper band with dark edges, a hard shadow below."""
    d = 150
    band = 7
    m = band + 3
    w, h = d + m * 2, d + m * 2 + 6
    im = Img(w, h)
    c = w / 2
    out = circle(c, c, d / 2 + band + 2.5)
    hole = circle(c, c, d / 2 - 1)
    im.fill(minus(shift(out, 0, 6), circle(c, c, d / 2 + 2)), (20, 12, 8), 0.34)
    im.fill(minus(out, hole), INK)
    im.fill(minus(circle(c, c, d / 2 + band), circle(c, c, d / 2 + 1.5)), PAPER_LIP)
    im.fill(minus(circle(c, c - 1.2, d / 2 + band - 0.5), circle(c, c, d / 2 + 1.5)), PAPER, clip=circle(c, c, d / 2 + band))
    # four small ticks at the compass points
    for ang in (0, 90, 180, 270):
        a = math.radians(ang)
        tx, ty = c + math.cos(a) * (d / 2 + band / 2 + 0.8), c + math.sin(a) * (d / 2 + band / 2 + 0.8)
        im.fill(circle(tx, ty, 1.7), INK, 0.75)
    return im


def menu_plate():
    """Behind the chosen line of the main menu: a slanted coral plate with a hard shadow."""
    w, h, sk = 96, 64, 9
    im = Img(w, h)
    body = poly([(sk + 7, 6.5), (w - 10, 6.5), (w - 10 - sk, h - 12.5), (7, h - 12.5)], 2.5)
    im.fill(shift(body, 4, 6), (20, 12, 8), 0.4)
    im.fill(grow(body, 3), INK)
    im.fill(body, CORAL_LIP)
    im.fill(shift(body, 0, -4), CORAL, clip=body)
    im.fill(shift(body, 0, -4), CORAL_HI, clip=both(body, above(12.5)))
    return im


def scrim_left():
    im = Img(256, 2)

    def fn(x, y):
        t = clamp(x / 256.0)
        a = 0.74 * (1 - t) ** 1.7
        return (14, 20, 30, a)
    im.shade(fn)
    return im


def scrim_bottom():
    im = Img(2, 128)

    def fn(x, y):
        t = clamp(y / 128.0)
        return (14, 20, 30, 0.66 * t ** 1.6)
    im.shade(fn)
    return im


def scrim_top():
    im = Img(2, 128)

    def fn(x, y):
        t = clamp(1 - y / 128.0)
        return (14, 20, 30, 0.5 * t ** 1.8)
    im.shade(fn)
    return im


def stick(letter_gap=False):
    """A thumbstick seen from above, for "left stick" and "right stick"."""
    s = 34
    im = Img(s, s)
    c = s / 2
    im.fill(circle(c, c, c - 0.5), INK)
    im.fill(circle(c, c, c - 3), (92, 104, 118))
    im.fill(circle(c, c, c - 7), INK)
    im.fill(circle(c, c - 1, c - 9.5), (52, 62, 74))
    return im


def dpad(lit=None):
    """A d-pad; `lit` is the arm to light up: 'up', 'down', 'left', 'right' or None."""
    s = 34
    im = Img(s, s)
    c = s / 2
    arm = 5.2
    cross = lambda x, y: min(rrect(c - arm, 2.5, c + arm, s - 2.5, 2.5)(x, y), rrect(2.5, c - arm, s - 2.5, c + arm, 2.5)(x, y))
    im.fill(grow(cross, 2.5), INK)
    im.fill(cross, (70, 82, 96))
    spots = {"up": (c, 7.5), "down": (c, s - 7.5), "left": (7.5, c), "right": (s - 7.5, c)}
    if lit in spots:
        lx, ly = spots[lit]
        im.fill(both(cross, circle(lx, ly, 6.5)), GOLD)
    return im


def mouse(button):
    """A mouse with one button lit: 'l', 'r' or 'm' (the wheel)."""
    w, h = 26, 34
    im = Img(w, h)
    body = rrect(2.5, 2.5, w - 2.5, h - 2.5, 10.5)
    im.fill(grow(body, 2.5), INK)
    im.fill(body, PAPER)
    im.fill(body, PAPER_LIP, clip=lambda x, y: (h - 7) - y)
    split = 15
    if button == "l":
        im.fill(both(body, rrect(0, 0, w / 2, split, 0)), CORAL)
    elif button == "r":
        im.fill(both(body, rrect(w / 2, 0, w, split, 0)), CORAL)
    im.fill(both(body, rrect(0, split - 1, w, split + 1, 0)), INK)
    im.fill(both(body, rrect(w / 2 - 1, 0, w / 2 + 1, split, 0)), INK)
    wheel = rrect(w / 2 - 3, 6, w / 2 + 3, 13, 3)
    im.fill(grow(wheel, 1.6), INK)
    im.fill(wheel, CORAL if button == "m" else (150, 132, 112))
    return im


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.normpath(os.path.join(here, "..", "..", "Assets", "Resources", "UI", "Skin"))
    os.makedirs(out, exist_ok=True)
    only = set(sys.argv[1:])
    made = []

    def save(name, make):
        if only and name not in only:
            return
        make().save(os.path.join(out, name + ".png"))
        made.append(name)

    # buttons: 64 px square, cut 22 / 22 / 22 / 27 (left, top, right, bottom)
    big = dict(size=64, margin=4, outline=3, radius=15, lip=5)
    save("btn", lambda: button(face=CORAL, hi=CORAL_HI, lipc=CORAL_LIP, **big))
    save("btn-focus", lambda: button(face=CORAL, hi=CORAL_HI, lipc=CORAL_LIP, ring=GOLD, **big))
    save("btn-down", lambda: button(face=CORAL, hi=CORAL, lipc=CORAL_LIP, ring=GOLD, pressed=True, **big))
    save("btn-ghost", lambda: button(face=PAPER, hi=WHITE, lipc=PAPER_LIP, **big))
    save("btn-ghost-focus", lambda: button(face=PAPER, hi=WHITE, lipc=PAPER_LIP, ring=GOLD, **big))
    save("btn-teal", lambda: button(face=TEAL, hi=TEAL_HI, lipc=TEAL_LIP, **big))
    save("btn-teal-focus", lambda: button(face=TEAL, hi=TEAL_HI, lipc=TEAL_LIP, ring=GOLD, **big))
    # small buttons and tabs: 44 px square, cut 15 / 15 / 15 / 18
    small = dict(size=44, margin=3, outline=2.5, radius=10, lip=3.5)
    save("btn-sm", lambda: button(face=CORAL, hi=CORAL_HI, lipc=CORAL_LIP, **small))
    save("btn-sm-focus", lambda: button(face=CORAL, hi=CORAL_HI, lipc=CORAL_LIP, ring=GOLD, **small))
    save("btn-sm-ghost", lambda: button(face=PAPER, hi=WHITE, lipc=PAPER_LIP, **small))
    save("btn-sm-ghost-focus", lambda: button(face=PAPER, hi=WHITE, lipc=PAPER_LIP, ring=GOLD, **small))
    save("btn-sm-teal", lambda: button(face=TEAL, hi=TEAL_HI, lipc=TEAL_LIP, **small))
    save("btn-sm-teal-focus", lambda: button(face=TEAL, hi=TEAL_HI, lipc=TEAL_LIP, ring=GOLD, **small))

    # cards: 96 px square, cut 34 / 34 / 34 / 46
    save("panel", lambda: panel(96, 4, 24, 5, 8, PAPER, PAPER_LIP))
    # the title strip on a card and the plate behind a heading: 56 px square, cut 20 / 20 / 20 / 24
    save("title", lambda: panel(56, 3, 13, 4, 4, TEAL, TEAL_LIP, 0.3))
    save("title-coral", lambda: panel(56, 3, 13, 4, 4, CORAL, CORAL_LIP, 0.3))

    # HUD plates: 48 px square, cut 16 all round
    save("plate", lambda: plate(48, 2.5, 13, NAVY, 0.80, (8, 12, 18), 0.92))
    save("plate-gold", lambda: plate(48, 2.5, 13, NAVY, 0.90, INK, 1, ring=GOLD))
    save("plate-paper", lambda: plate(48, 2.5, 13, PAPER, 0.96, INK, 1, glint=0))

    # list rows: 48 px square, cut 16 all round
    save("row", lambda: row(48, 10, PAPER_IN))
    save("row-focus", lambda: row(48, 10, (255, 248, 226), ring=GOLD))
    save("row-on", lambda: row(48, 10, (255, 240, 205), ring=CORAL))

    # key caps and controller buttons
    save("key", lambda: button(size=36, margin=0, outline=2.5, radius=8, lip=3.5, face=PAPER, hi=WHITE, lipc=PAPER_LIP))
    save("key-dark", lambda: button(size=36, margin=0, outline=2.5, radius=8, lip=3.5, face=(66, 78, 92), hi=(92, 104, 118), lipc=(36, 44, 54)))
    save("pad-a", lambda: round_button(34, (104, 184, 74), (62, 126, 44), (170, 226, 140)))
    save("pad-b", lambda: round_button(34, (229, 84, 62), (166, 48, 34), (250, 150, 130)))
    save("pad-x", lambda: round_button(34, (72, 146, 220), (40, 96, 160), (150, 200, 250)))
    save("pad-y", lambda: round_button(34, (244, 184, 50), (190, 128, 24), (255, 226, 140)))
    save("pad-stick", stick)
    save("pad-dpad", dpad)
    for d in ("up", "down", "left", "right"):
        save("pad-dpad-" + d, lambda d=d: dpad(d))
    save("mouse-l", lambda: mouse("l"))
    save("mouse-r", lambda: mouse("r"))
    save("mouse-m", lambda: mouse("m"))

    # meters, sliders, switches
    save("bar", bar_track)
    save("bar-fill", lambda: bar_fill((244, 150, 58), (255, 196, 110)))
    save("bar-fill-cold", lambda: bar_fill((92, 150, 222), (156, 198, 246)))
    save("bar-fill-teal", lambda: bar_fill(TEAL, TEAL_HI))
    save("knob", knob)
    save("toggle-off", lambda: toggle(False))
    save("toggle-on", lambda: toggle(True))
    save("arrow-left", lambda: arrow(-1, PAPER))
    save("arrow-right", lambda: arrow(1, PAPER))
    save("arrow-gold", lambda: arrow(1, GOLD))

    # frames and shading
    save("minimap-ring", minimap_ring)
    save("menu-plate", menu_plate)
    save("scrim-left", scrim_left)
    save("scrim-bottom", scrim_bottom)
    save("scrim-top", scrim_top)

    print("wrote %d pictures to %s" % (len(made), out))


if __name__ == "__main__":
    main()
