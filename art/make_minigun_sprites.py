"""Draws the Minigun's art (python -I make_minigun_sprites.py, needs Pillow), in the game's worn, dark-outlined pixel style:
  minigun_body.png        150x79  the Wii-style remote lying on its side (front, with the sensor, on the left): scuffed plastic, a frayed
                                  strap. Same size as the SMG body it replaces, so the held model lines up
  minigun_icon.png         40x18  the shop card's icon: a big minigun with a small remote joined to it by a cord
  minigun_display.png     128x24  the heat display that sits on the dash: a riveted casing, a black screen and HEAT in the game's letters
                                  (the plugin lights 21 segments on it, 3x10 pixels each, from x 34, every 4 pixels, from row 7)
  minigun_lock.png         16x20  the padlock that sits on the remote while the gun is locked
  minigun_clear_slide.png  83x17  empty stand-ins for the SMG's slide, back, round and feed sprites
  minigun_clear_back.png   80x11
  minigun_clear_round.png   8x6
  minigun_clear_feed.png   13x5
and minigun_preview.png, the drawn ones enlarged, for looking at."""
import os
import random
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
rng = random.Random(1911)

CLEAR = (0, 0, 0, 0)
K = (18, 18, 22, 255)
PLASTIC = [(70, 72, 84, 255), (146, 150, 164, 255), (204, 208, 220, 255), (240, 242, 248, 255)]
DARK = (44, 47, 58, 255)
GREY = (110, 116, 130, 255)
BLUE = (70, 170, 255, 255)
RED = (214, 52, 48, 255)
RUST = (124, 70, 34, 255)
GRIME = (96, 90, 82, 255)
BRASS = [(66, 50, 16, 255), (140, 108, 34, 255), (196, 160, 62, 255), (238, 206, 110, 255)]


def save(img, name):
    img.save(os.path.join(HERE, name))


def speckle(img, box, colour, amount, only_light=False):
    x0, y0, x1, y1 = box
    px = img.load()
    for _ in range(amount):
        x = rng.randint(x0, x1)
        y = rng.randint(y0, y1)
        p = px[x, y]
        if p[3] and p != K and (not only_light or p[0] > 140):
            px[x, y] = colour


def button(d, cx, cy, r, ring=K, face=PLASTIC[1], hi=PLASTIC[3]):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=ring)
    d.ellipse((cx - r + 1, cy - r + 1, cx + r - 1, cy + r - 1), fill=face)
    d.point((cx - r + 2, cy - r + 2), fill=hi)


def remote():
    """A worn Wii remote, about two thirds of the SMG's size: the SMG hangs on the roof lining by the mirror, and a full-size
    remote covered it."""
    img = Image.new("RGBA", (150, 79), CLEAR)
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = 28, 28, 124, 52
    cy = 40
    d.rounded_rectangle((x0, y0, x1, y1), radius=5, fill=K)
    d.rounded_rectangle((x0 + 1, y0 + 1, x1 - 1, y1 - 1), radius=4, fill=PLASTIC[2])
    d.rounded_rectangle((x0 + 2, y0 + 2, x1 - 2, y0 + 5), radius=2, fill=PLASTIC[3])
    d.rectangle((x0 + 3, y1 - 5, x1 - 3, y1 - 2), fill=PLASTIC[1])
    for x in range(x0 + 4, x1 - 3):
        if x % 2 == 0:
            d.point((x, y1 - 6), fill=PLASTIC[1])
    d.line((x0 + 5, y1 - 1, x1 - 5, y1 - 1), fill=PLASTIC[0])

    # The sensor window at the front, with a glint, and the red power dot.
    d.rounded_rectangle((x0 + 1, cy - 8, x0 + 6, cy + 8), radius=2, fill=K)
    d.rectangle((x0 + 2, cy - 6, x0 + 5, cy + 6), fill=(30, 32, 44, 255))
    d.line((x0 + 3, cy - 5, x0 + 3, cy - 2), fill=(110, 120, 150, 255))
    d.point((x0 + 12, y0 + 4), fill=RED)

    # The D-pad.
    px = x0 + 18
    d.rectangle((px - 3, cy - 9, px + 3, cy + 9), fill=K)
    d.rectangle((px - 9, cy - 3, px + 9, cy + 3), fill=K)
    d.rectangle((px - 2, cy - 8, px + 2, cy + 8), fill=DARK)
    d.rectangle((px - 8, cy - 2, px + 8, cy + 2), fill=DARK)
    d.line((px - 1, cy - 7, px - 1, cy - 4), fill=GREY)
    d.line((px - 7, cy - 1, px - 4, cy - 1), fill=GREY)

    # The big A button.
    ax = x0 + 38
    button(d, ax, cy, 8)
    d.ellipse((ax - 5, cy - 5, ax + 5, cy + 5), fill=PLASTIC[2])
    d.point((ax - 2, cy - 3), fill=PLASTIC[3])
    d.point((ax + 2, cy + 2), fill=GRIME)

    # Minus, Home, Plus.
    button(d, x0 + 54, cy, 3)
    d.line((x0 + 53, cy, x0 + 55, cy), fill=DARK)
    button(d, x0 + 64, cy, 4)
    d.rectangle((x0 + 62, cy, x0 + 66, cy), fill=DARK)
    button(d, x0 + 74, cy, 3)
    d.line((x0 + 73, cy, x0 + 75, cy), fill=DARK)
    d.line((x0 + 74, cy - 1, x0 + 74, cy + 1), fill=DARK)

    # 1 and 2, the player lights, the strap lug.
    button(d, x0 + 84, cy - 6, 3)
    button(d, x0 + 84, cy + 6, 3)
    for i in range(4):
        d.rectangle((x1 - 6, cy - 6 + i * 3, x1 - 5, cy - 5 + i * 3), fill=BLUE if i == 0 else DARK)
    d.rectangle((x1 - 1, cy - 3, x1 + 2, cy + 3), fill=K)
    d.rectangle((x1, cy - 1, x1 + 1, cy + 1), fill=GREY)
    d.line((x1 + 2, cy - 1, x1 + 5, cy - 4), fill=K)
    d.line((x1 + 2, cy + 1, x1 + 6, cy + 4), fill=K)
    d.line((x1 + 5, cy - 4, x1 + 7, cy - 6), fill=GRIME)
    d.line((x1 + 6, cy + 4, x1 + 7, cy + 7), fill=GRIME)

    # Wear.
    speckle(img, (x0 + 3, y0 + 2, x1 - 3, y1 - 2), GRIME, 55, only_light=True)
    speckle(img, (x0 + 3, y0 + 2, x1 - 3, y1 - 2), PLASTIC[0], 22, only_light=True)
    speckle(img, (x0 + 30, y0 + 2, x1 - 20, y1 - 2), RUST, 8, only_light=True)
    for _ in range(3):
        sx = rng.randint(x0 + 8, x1 - 14)
        sy = rng.randint(y0 + 3, y1 - 4)
        d.line((sx, sy, sx + rng.randint(3, 6), sy + rng.randint(-1, 1)), fill=PLASTIC[3] if rng.random() < 0.5 else GRIME)
    return img


GLYPHS = {
    "H": ["X...X", "X...X", "X...X", "XXXXX", "X...X", "X...X", "X...X"],
    "E": ["XXXXX", "X....", "X....", "XXXX.", "X....", "X....", "XXXXX"],
    "A": [".XXX.", "X...X", "X...X", "XXXXX", "X...X", "X...X", "X...X"],
    "T": ["XXXXX", "..X..", "..X..", "..X..", "..X..", "..X..", "..X.."],
}


def display():
    img = Image.new("RGBA", (128, 24), CLEAR)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, 127, 23), radius=3, fill=K)
    d.rounded_rectangle((1, 1, 126, 22), radius=2, fill=(70, 72, 82, 255))
    d.line((3, 1, 124, 1), fill=(130, 134, 148, 255))
    d.line((3, 22, 124, 22), fill=(40, 42, 50, 255))
    for x, y in ((3, 3), (124, 3), (3, 20), (124, 20)):
        d.point((x, y), fill=(150, 154, 166, 255))
        d.point((x + 1, y + 1) if x < 60 else (x - 1, y + 1), fill=K)
    # The screen: black, with a dim red grid where the segments light up.
    d.rectangle((5, 4, 122, 19), fill=K)
    d.rectangle((6, 5, 121, 18), fill=(16, 8, 8, 255))
    for i in range(21):
        x = 34 + i * 4
        d.rectangle((x, 7, x + 2, 16), fill=(36, 12, 12, 255))
    # HEAT, in the game's red.
    x = 8
    for ch in "HEAT":
        for r, row in enumerate(GLYPHS[ch]):
            for c, v in enumerate(row):
                if v == "X":
                    d.point((x + c, 8 + r), fill=(214, 52, 48, 255))
        x += 6
    # A scratch or two on the glass.
    d.line((100, 5, 108, 7), fill=(60, 36, 36, 255))
    return img


def lock():
    img = Image.new("RGBA", (16, 20), CLEAR)
    d = ImageDraw.Draw(img)
    # The shackle, then the body.
    d.arc((3, 0, 12, 12), 180, 360, fill=K, width=3)
    d.arc((4, 1, 11, 11), 180, 360, fill=GREY, width=1)
    d.line((4, 6, 4, 9), fill=K)
    d.line((11, 6, 11, 9), fill=K)
    d.rectangle((1, 8, 14, 19), fill=K)
    d.rectangle((2, 9, 13, 18), fill=BRASS[1])
    d.rectangle((2, 9, 13, 10), fill=BRASS[3])
    d.rectangle((2, 16, 13, 18), fill=BRASS[0])
    d.ellipse((6, 11, 9, 14), fill=K)
    d.rectangle((7, 13, 8, 16), fill=K)
    d.point((3, 10), fill=(255, 245, 190, 255))
    return img


def icon():
    """40x18. A big minigun side on: a bundle of barrels (three lit tubes, so the six read as round) with clamps and a muzzle ring
    pointing right, a short heat shroud, the tall housing with a motor and spade grip behind it, and an ammo box underneath. The
    remote that aims it is a small add-on in the bottom-right corner, joined to the housing by a coiled cord."""
    img = Image.new("RGBA", (40, 18), CLEAR)
    d = ImageDraw.Draw(img)
    OL = (24, 24, 30, 255)
    ST = [(52, 56, 58, 255), (96, 102, 104, 255), (150, 156, 158, 255), (200, 204, 206, 255)]
    OLIVE = [(34, 40, 28, 255), (70, 80, 56, 255), (104, 118, 80, 255)]
    # The barrels, x 18..39, rows 2..11: an outline, three tubes with a dark gap between, a glint on each.
    d.rectangle((18, 2, 39, 11), fill=OL)
    for top in (3, 6, 9):
        d.line((19, top, 38, top), fill=ST[3])
        d.line((19, top + 1, 38, top + 1), fill=ST[1])
    for x in range(21, 38, 4):
        d.point((x, 4), fill=ST[0])
        d.point((x + 2, 7), fill=ST[0])
    # Three clamps and the muzzle ring, sticking out above and below.
    for x in (25, 31):
        d.rectangle((x, 1, x + 1, 12), fill=OL)
        d.line((x, 2, x, 11), fill=ST[2])
    d.rectangle((37, 0, 39, 13), fill=OL)
    d.line((38, 1, 38, 12), fill=ST[2])
    # The heat shroud over the back of the barrels.
    d.rectangle((15, 1, 21, 12), fill=OL)
    d.rectangle((16, 2, 20, 11), fill=OLIVE[1])
    d.line((16, 2, 20, 2), fill=OLIVE[2])
    d.line((16, 11, 20, 11), fill=OLIVE[0])
    d.line((19, 3, 19, 10), fill=OLIVE[0])
    # The tall housing, with a top plate and a brass feed tab.
    d.rectangle((6, 0, 16, 13), fill=OL)
    d.rectangle((7, 1, 15, 12), fill=ST[1])
    d.rectangle((7, 1, 15, 3), fill=OLIVE[1])
    d.line((7, 1, 15, 1), fill=OLIVE[2])
    d.line((7, 12, 15, 12), fill=ST[0])
    d.rectangle((11, 5, 14, 9), fill=ST[0])
    d.rectangle((12, 6, 13, 8), fill=BRASS[1])
    d.point((12, 6), fill=BRASS[3])
    # The motor behind it and the spade grip with its handles.
    d.rectangle((2, 3, 6, 10), fill=OL)
    d.rectangle((3, 4, 5, 9), fill=ST[0])
    d.line((3, 4, 5, 4), fill=ST[1])
    d.rectangle((0, 1, 1, 12), fill=OL)
    d.line((0, 2, 0, 11), fill=ST[1])
    d.point((1, 2), fill=ST[2])
    d.point((1, 11), fill=ST[2])
    # The ammo box under the housing, with a brass link where the belt leaves it.
    d.rectangle((5, 13, 14, 17), fill=OL)
    d.rectangle((6, 14, 13, 16), fill=OLIVE[1])
    d.line((6, 14, 13, 14), fill=OLIVE[2])
    d.line((6, 16, 13, 16), fill=OLIVE[0])
    d.point((8, 15), fill=OLIVE[2])
    d.point((11, 15), fill=BRASS[2])
    d.point((14, 13), fill=BRASS[2])
    # The mount: a column down from the shroud and a plate.
    d.rectangle((16, 13, 19, 15), fill=OL)
    d.rectangle((17, 13, 18, 15), fill=ST[1])
    d.rectangle((15, 16, 21, 17), fill=OL)
    d.line((16, 16, 20, 16), fill=ST[0])
    # The remote that aims it, small, bottom right, and its coiled cord back to the mount.
    d.rectangle((29, 13, 39, 17), fill=OL)
    d.rectangle((30, 14, 38, 16), fill=PLASTIC[2])
    d.line((30, 14, 38, 14), fill=PLASTIC[3])
    d.rectangle((30, 14, 31, 16), fill=DARK)
    d.rectangle((33, 15, 34, 15), fill=DARK)
    d.point((33, 14), fill=DARK)
    d.point((36, 15), fill=GREY)
    d.point((37, 15), fill=BLUE)
    for x, y in ((28, 15), (26, 16), (24, 15), (22, 16)):
        d.point((x, y), fill=OL)
    d.point((27, 15), fill=GRIME)
    d.point((25, 15), fill=GRIME)
    d.point((23, 16), fill=GRIME)
    return img


body = remote()
disp = display()
padlock = lock()
ic = icon()
save(body, "minigun_body.png")
save(ic, "minigun_icon.png")
save(disp, "minigun_display.png")
save(padlock, "minigun_lock.png")
save(Image.new("RGBA", (83, 17), CLEAR), "minigun_clear_slide.png")
save(Image.new("RGBA", (80, 11), CLEAR), "minigun_clear_back.png")
save(Image.new("RGBA", (8, 6), CLEAR), "minigun_clear_round.png")
save(Image.new("RGBA", (13, 5), CLEAR), "minigun_clear_feed.png")

sheet = Image.new("RGBA", (150 * 3 + 20, 79 * 3 + 24 * 5 + 30 + 20 * 6), (60, 70, 80, 255))
big = body.resize((150 * 3, 79 * 3), Image.NEAREST)
sheet.paste(big, (10, 10), big)
y = 79 * 3 + 20
d4 = disp.resize((128 * 4, 24 * 4), Image.NEAREST)
sheet.paste(d4, (10, y), d4)
l6 = padlock.resize((16 * 6, 20 * 6), Image.NEAREST)
sheet.paste(l6, (10 + 128 * 4 - 16 * 6 + 20 if False else 10, y + 24 * 4 + 10), l6)
i6 = ic.resize((40 * 6, 18 * 6), Image.NEAREST)
sheet.paste(i6, (130, y + 24 * 4 + 10), i6)
sheet.save(os.path.join(HERE, "minigun_preview.png"))
print("minigun art drawn: body 150x79, icon 40x18, display 128x24, lock 16x20")
