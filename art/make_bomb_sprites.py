"""Draws the Bomb race's art (python -I make_bomb_sprites.py, needs Pillow):
  bomb_icon.png  24x30  the map overlay: a ring round the node with a fuse on top, like the game's other objective rings
  bomb_dash.png  44x26  the C4 block on the dash, mirrored from the gear display (a screen, two bulbs and wires)
  bomb_mini.png   7x7   a tiny bomb, shown three across on the block's screen before the bomb is armed
  bomb_bulb.png   4x4   a bulb, white so the plugin can tint it red or green
  bomb_glow.png  12x12  the soft halo behind a lit bulb
  bomb_c4.png    24x16  the face of the C4 block stuck on the back of the cars (label, and two sockets for the bulbs)
and bomb_preview.png, all of them side by side and enlarged, for looking at. Positions the plugin relies on are printed at the end;
they must match the constants in DashBomb.cs and C4Visual.cs."""
import math
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))

K = (10, 10, 12, 255)         # black rim
BODY = (44, 46, 56, 255)      # body
BODY_LIGHT = (84, 88, 102, 255)
BODY_DARK = (28, 29, 36, 255)
SHINE = (176, 182, 198, 255)
STEEL = (128, 132, 144, 255)
STEEL_LIGHT = (176, 180, 192, 255)
FUSE = (150, 110, 60, 255)
SPARK = (255, 120, 30, 255)
SPARK_CORE = (255, 224, 80, 255)
CLEAR = (0, 0, 0, 0)


def from_rows(rows, colours):
    img = Image.new("RGBA", (len(rows[0]), len(rows)), CLEAR)
    for y, row in enumerate(rows):
        assert len(row) == len(rows[0]), (y, row)
        for x, c in enumerate(row):
            img.putpixel((x, y), colours[c])
    return img


def map_icon():
    """The overlay round a map node on the run map. The game draws its objectives as rings (orange for one, flames for another, silver
    for another), 24 wide, 24 or more tall with a crest on top, and sets them round the node by the sprite's pivot (0.5, 0.4 for the
    24x30 flame ring). This one is a charcoal ring with a red line inside and a lit fuse where the flames would be."""
    w, h = 24, 30
    img = Image.new("RGBA", (w, h), CLEAR)
    cx, cy, radius = 12.0, 18.0, 12.0
    for y in range(h):
        for x in range(w):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d = math.hypot(dx, dy)
            if d > radius or d < 8.0:
                continue
            lit = (-dx - dy) / max(d, 0.001) / 1.414     # 1 at the top left, -1 at the bottom right
            if d > 11.0:
                c = K
            elif d > 9.0:
                c = BODY_LIGHT if lit > 0.35 else (BODY_DARK if lit < -0.4 else BODY)
                if lit > 0.78:
                    c = SHINE
            else:
                c = (206, 46, 40, 255) if lit > -0.2 else (148, 30, 28, 255)
            img.putpixel((x, y), c)
    # The cap on the top of the ring, and the fuse out of it with a spark at the end.
    for x in range(9, 15):
        img.putpixel((x, 3), K)
        img.putpixel((x, 4), K if x in (9, 14) else STEEL_LIGHT)
        img.putpixel((x, 5), K if x in (9, 14) else STEEL)
    for p in ((11, 2), (12, 2), (12, 1), (13, 1)):
        img.putpixel(p, FUSE)
    for p in ((14, 1), (13, 0), (15, 0), (14, 2)):
        img.putpixel(p, SPARK)
    img.putpixel((14, 0), SPARK_CORE)
    img.putpixel((15, 1), SPARK_CORE)
    return img


# The C4 block on the dash. The screen is the black panel (its inside is where the plugin puts the text and the little bombs); the two
# bulbs sit in sockets on the right. All of these numbers are copied into DashBomb.cs.
DASH_W, DASH_H = 44, 26
DASH_PANEL = (3, 3, 28, 15)            # x, y, width, height of the black screen
DASH_BULBS = {"red": (35, 4), "green": (35, 12)}   # top-left of each 4x4 bulb


def dash_bomb():
    base = (196, 186, 150, 255)
    edge = (150, 140, 108, 255)
    hi = (222, 212, 176, 255)
    img = Image.new("RGBA", (DASH_W, DASH_H), base)
    for x in range(DASH_W):
        img.putpixel((x, 0), edge)
        img.putpixel((x, DASH_H - 1), edge)
        img.putpixel((x, 1), hi)
    for y in range(DASH_H):
        img.putpixel((0, y), edge)
        img.putpixel((DASH_W - 1, y), edge)
    px, py, pw, ph = DASH_PANEL
    for y in range(py - 1, py + ph + 1):
        for x in range(px - 1, px + pw + 1):
            rim = x in (px - 1, px + pw) or y in (py - 1, py + ph)
            img.putpixel((x, y), (70, 70, 76, 255) if rim else (12, 12, 14, 255))
    # Sockets round the bulbs: a black frame 6x6.
    for bx, by in DASH_BULBS.values():
        for y in range(by - 1, by + 5):
            for x in range(bx - 1, bx + 5):
                e = x in (bx - 1, bx + 4) or y in (by - 1, by + 4)
                img.putpixel((x, y), K if e else (34, 30, 28, 255))
    # "C4" and three wires underneath.
    for gx, name in ((4, "C"), (8, "4")):
        for y, row in enumerate(GLYPHS[name]):
            for x, c in enumerate(row):
                if c == "X":
                    img.putpixel((gx + x, 20 + y), K)
    for x0, colour in ((15, (190, 40, 36, 255)), (21, (50, 90, 190, 255)), (27, (220, 190, 50, 255))):
        for x in range(x0, x0 + 4):
            img.putpixel((x, 22), colour)
            img.putpixel((x, 23), K)
    return img


def mini_bomb():
    rows = [
        "....SY.",
        "...F...",
        "..RRR..",
        ".RHMMR.",
        ".RMDDR.",
        ".RDDDR.",
        "..RRR..",
    ]
    colours = {".": CLEAR, "R": (150, 154, 168, 255), "H": (214, 218, 230, 255), "M": (96, 100, 116, 255), "D": (60, 62, 76, 255),
               "F": FUSE, "S": SPARK, "Y": SPARK_CORE}
    return from_rows(rows, colours)


def bulb():
    rows = [
        ".XX.",
        "XXXX",
        "XXXX",
        ".XX.",
    ]
    img = from_rows(rows, {".": CLEAR, "X": (255, 255, 255, 255)})
    img.putpixel((1, 1), (255, 255, 255, 255))
    return img


def glow():
    n = 12
    img = Image.new("RGBA", (n, n), CLEAR)
    c = (n - 1) / 2
    for y in range(n):
        for x in range(n):
            d = math.hypot(x - c, y - c) / c
            a = max(0.0, 1.0 - d)
            if a > 0.05:
                img.putpixel((x, y), (255, 255, 255, int(255 * a * a)))
    return img


C4_W, C4_H = 24, 16
# Centres, in pixels from the top left of the face, of the two bulb sockets.
C4_BULBS = {"red": (18, 5), "green": (18, 11)}
GLYPHS = {
    "C": ["XXX", "X..", "X..", "X..", "XXX"],
    "4": ["X.X", "X.X", "XXX", "..X", "..X"],
}


def c4_face():
    base = (196, 186, 150, 255)
    edge = (150, 140, 108, 255)
    hi = (222, 212, 176, 255)
    img = Image.new("RGBA", (C4_W, C4_H), base)
    for x in range(C4_W):
        img.putpixel((x, 0), edge)
        img.putpixel((x, C4_H - 1), edge)
        img.putpixel((x, 1), hi)
    for y in range(C4_H):
        img.putpixel((0, y), edge)
        img.putpixel((C4_W - 1, y), edge)
    # Black label with "C4" in it.
    for y in range(3, 12):
        for x in range(2, 12):
            img.putpixel((x, y), K)
    for gx, name in ((4, "C"), (8, "4")):
        for y, row in enumerate(GLYPHS[name]):
            for x, c in enumerate(row):
                if c == "X":
                    img.putpixel((gx + x, 5 + y), (232, 226, 200, 255))
    # Sockets
    for cx, cy in C4_BULBS.values():
        for y in range(cy - 3, cy + 3):
            for x in range(cx - 3, cx + 3):
                e = x in (cx - 3, cx + 2) or y in (cy - 3, cy + 2)
                img.putpixel((x, y), K if e else (40, 36, 30, 255))
    return img


sprites = {
    "bomb_icon.png": map_icon(),
    "bomb_dash.png": dash_bomb(),
    "bomb_mini.png": mini_bomb(),
    "bomb_bulb.png": bulb(),
    "bomb_glow.png": glow(),
    "bomb_c4.png": c4_face(),
}
for name, im in sprites.items():
    im.save(os.path.join(HERE, name))

# One sheet, each at 8x, on the grey-green of the run map.
scale = 8
width = sum(im.width * scale + 16 for im in sprites.values()) + 16
height = max(im.height for im in sprites.values()) * scale + 32
sheet = Image.new("RGBA", (width, height), (150, 165, 140, 255))
x = 16
for im in sprites.values():
    big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
    sheet.paste(big, (x, 16), big)
    x += big.width + 16
sheet.save(os.path.join(HERE, "bomb_preview.png"))

print("bomb art written:", ", ".join(f"{n} {i.width}x{i.height}" for n, i in sprites.items()))
print("dash block", (DASH_W, DASH_H), "screen (x, y, w, h):", DASH_PANEL, "bulbs (top-left of each 4x4):", DASH_BULBS)
print("c4 face", (C4_W, C4_H), "bulb socket centres:", C4_BULBS)
