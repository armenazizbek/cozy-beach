"""
Draws the game's icon, unity/Assets/Icons/Tidefinder.png (1024 x 1024), in the loading screen's look: a golden morning
over the sea, the sun on the horizon and a message in a bottle riding the waves. Kept bold and simple so it still reads
at 16 x 16 in a file list.

    python unity/Tools/Icon/make_icon.py

Needs Pillow (python -m pip install pillow). Unity picks the new picture up on the next build (ProjectSetup sets it as
the icon for every platform).
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

S = 2048  # drawn at twice the size, then scaled down smooth
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Icons", "Tidefinder.png")

SKY = [(0.0, (0x4E, 0x9A, 0xA6)), (0.33, (0xA7, 0xCF, 0xC4)), (0.6, (0xF6, 0xD6, 0x9E))]
SEA = [(0x63, 0xA9, 0xAA), (0x4B, 0x94, 0x9C), (0x39, 0x80, 0x8B), (0x2B, 0x6F, 0x78)]
FOAM = (0xFF, 0xF5, 0xDC)
SUN = (0xFF, 0xEC, 0xB8)
HORIZON = 0.6


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def sky_at(v):
    for (v0, c0), (v1, c1) in zip(SKY, SKY[1:]):
        if v <= v1:
            return lerp(c0, c1, (v - v0) / (v1 - v0))
    return SKY[-1][1]


def wave_y(k, x):
    """The top of wave layer k at x (0..1), as a fraction of the height."""
    base = HORIZON + (1 - HORIZON) * [0.1, 0.34, 0.62, 0.86][k]
    amp = [0.006, 0.012, 0.02, 0.028][k]
    return base + amp * math.sin(x * math.pi * 2 * [3.0, 2.4, 1.8, 1.4][k] + k * 1.3)


def main():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    art = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(art)

    # sky
    hy = int(S * HORIZON)
    for y in range(hy):
        d.line([(0, y), (S, y)], fill=sky_at(y / S) + (255,))
    # the sun, half risen, with a soft glow
    sx, sy, sr = S * 0.5, S * HORIZON - S * 0.03, S * 0.19
    # the glow starts as sun-coloured but clear everywhere, so blurring it never darkens its edge
    glow = Image.new("RGBA", (S, S), SUN + (0,))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([sx - sr * 1.7, sy - sr * 1.7, sx + sr * 1.7, sy + sr * 1.7], fill=SUN + (90,))
    glow = glow.filter(ImageFilter.GaussianBlur(S * 0.04))
    art.alpha_composite(glow)
    d.ellipse([sx - sr, sy - sr, sx + sr, sy + sr], fill=SUN + (255,))
    # the far water covers the sun's lower half
    d.rectangle([0, hy, S, S], fill=SEA[0] + (255,))

    def wave(k):
        pts = [(x / 64 * S, wave_y(k, x / 64) * S) for x in range(65)]
        d.polygon(pts + [(S, S), (0, S)], fill=SEA[k] + (255,))
        d.line(pts, fill=FOAM + (255,), width=int(S * (0.006 + 0.003 * k)), joint="curve")

    # the sun's light on the far water
    for i in range(5):
        y = S * (HORIZON + 0.025 + i * 0.035)
        w = S * (0.17 - i * 0.025)
        d.rounded_rectangle([sx - w / 2, y, sx + w / 2, y + S * 0.012], radius=S * 0.006, fill=SUN + (200 - i * 30,))

    wave(0)
    wave(1)

    # the bottle, lying across the swell with its neck up to the right
    bx, by, ang, k = S * 0.5, S * 0.775, math.radians(-22), S / 120.0

    def R(px, py):
        """A point of the bottle (x along it, neck towards +x; y across it) in the picture."""
        c, s = math.cos(ang), math.sin(ang)
        return (bx + (px * c - py * s) * k, by + (px * s + py * c) * k)

    def arc(cx, cy, r, a0, a1, n=10):
        return [R(cx + r * math.cos(a), cy + r * math.sin(a)) for a in [a0 + (a1 - a0) * i / n for i in range(n + 1)]]

    body = (
        arc(-26, -9, 6, math.pi, math.pi * 1.5)
        + [R(10, -15)]
        + [R(10 + 8 * t, -15 + 9 * t * t) for t in [0.25, 0.5, 0.75, 1.0]]
        + [R(30, -6), R(30, -7.5), R(34, -7.5), R(34, 7.5), R(30, 7.5), R(30, 6)]
        + [R(18 - 8 * t, 6 + 9 * t * t) for t in [0.0, 0.25, 0.5, 0.75, 1.0]]
        + arc(-26, 9, 6, math.pi * 0.5, math.pi)
    )
    d.polygon(body, fill=(0x4F, 0x8E, 0x6C, 255))
    # the rolled letter inside, tied with a red ribbon
    d.polygon([R(-22, -6), R(6, -6), R(6, 6), R(-22, 6)], fill=(0xFF, 0xEE, 0xC8, 255))
    d.polygon([R(-22, -6), R(-19, -6), R(-19, 6), R(-22, 6)], fill=(0xD8, 0xBC, 0x8C, 255))
    d.polygon([R(3, -6), R(6, -6), R(6, 6), R(3, 6)], fill=(0xD8, 0xBC, 0x8C, 255))
    d.polygon([R(-10, -6.3), R(-6.5, -6.3), R(-6.5, 6.3), R(-10, 6.3)], fill=(0xC8, 0x3A, 0x30, 255))
    # glass over it, its edge, a shine and the cork
    glass = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(glass).polygon(body, fill=(0x8C, 0xD2, 0xAA, 70))
    art.alpha_composite(glass)
    d.line(body + [body[0]], fill=(0x1E, 0x45, 0x34, 255), width=int(S * 0.009), joint="curve")
    d.line([R(-24, -11), R(4, -11)], fill=(255, 255, 255, 200), width=int(S * 0.011))
    d.polygon([R(34, -5.5), R(43, -4.5), R(43, 4.5), R(34, 5.5)], fill=(0xA8, 0x72, 0x45, 255), outline=(0x5C, 0x38, 0x1E, 255))

    # the near waves in front of it
    wave(2)
    wave(3)

    # cut to a rounded square
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * 0.2), fill=255)
    img.paste(art, (0, 0), mask)
    img = img.resize((1024, 1024), Image.LANCZOS)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.save(OUT)
    print("wrote", os.path.abspath(OUT))


if __name__ == "__main__":
    main()
