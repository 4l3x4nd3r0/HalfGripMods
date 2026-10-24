"""Draws the SpeedCharms sprites: three pixel-art charms for the rearview mirror.

The game's own charms are about 19x22 (drawn at 25 pixels per unit as a charm, 100 as an item icon), with a
muted palette and a dark outline. Each charm is drawn once on a 19x22 canvas with a string at the top:
  <stem>_charm.png  19x22, as hung from the mirror
  <stem>_icon.png   19x21, the same drawing without the top row of string
Run:  python make_speed_sprites.py  -> writes the PNGs next to this file.
"""
import os

from PIL import Image

OUT = os.path.dirname(os.path.abspath(__file__))
W, H = 19, 22

OUTLINE = (30, 24, 24, 255)
STRING = (190, 176, 150, 255)
STRING_DARK = (120, 108, 88, 255)

WHITE = (226, 222, 210, 255)
BLACK = (40, 40, 46, 255)
POLE = (150, 120, 84, 255)
POLE_DARK = (104, 80, 54, 255)

FUR = [(176, 160, 138, 255), (146, 130, 110, 255), (112, 98, 82, 255)]
CLAW = (214, 208, 190, 255)
CHAIN = (150, 154, 162, 255)

CARD = [(238, 228, 196, 255), (214, 202, 168, 255), (150, 130, 96, 255)]
GLOVE_GREY = (170, 170, 178, 255)
GLOVE_GREY_DARK = (120, 120, 130, 255)
HELMET = [(206, 82, 70, 255), (168, 56, 52, 255), (120, 40, 40, 255)]
HELMET_HI = (240, 150, 130, 255)
VISOR = [(58, 86, 112, 255), (30, 44, 60, 255)]
VISOR_GLINT = (150, 190, 214, 255)

CHUTE_RED = [(196, 70, 60, 255), (156, 48, 44, 255)]
CHUTE_WHITE = [(232, 228, 216, 255), (196, 190, 176, 255)]
CHUTE_PACK = [(88, 94, 104, 255), (62, 67, 76, 255)]
BUCKLE = (214, 176, 80, 255)


def canvas():
    return Image.new("RGBA", (W, H), (0, 0, 0, 0))


def put(img, pts, colour):
    for x, y in pts:
        if 0 <= x < W and 0 <= y < H:
            img.putpixel((x, y), colour)


def rect(img, x0, y0, x1, y1, colour):
    put(img, [(x, y) for y in range(y0, y1 + 1) for x in range(x0, x1 + 1)], colour)


def outline(img):
    """Adds a one pixel dark outline around everything drawn so far (not around the string)."""
    src = img.copy()
    for y in range(H):
        for x in range(W):
            if src.getpixel((x, y))[3] != 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < W and 0 <= ny < H and src.getpixel((nx, ny))[3] != 0:
                    img.putpixel((x, y), OUTLINE)
                    break


def string(img, x, length=3):
    for y in range(length):
        put(img, [(x, y)], STRING if y % 2 == 0 else STRING_DARK)


def glove():
    """The player's fingerless glove (black, grey fingertips, wrist strap) with a checkered back, hung by a string."""
    img = canvas()
    ox = 3  # hand columns 0..14 sit at x 3..17, so the middle finger is on the string at x 9
    # Four fingers, 3 wide, each with a different top row; grey tips run down to y 9.
    for col0, top in ((2, 6), (5, 5), (8, 5), (11, 6)):
        for y in range(top, 10):
            for dx in range(3):
                shade = GLOVE_GREY if dx < 2 else GLOVE_GREY_DARK
                if dx == 0 and col0 != 2 and y > top:
                    shade = BLACK
                put(img, [(ox + col0 + dx, y)], shade)
    # String comes down onto the middle finger.
    string(img, 9, 5)
    # Black glove body with a 1px checkerboard on the back of the hand.
    for y in range(10, 18):
        for x in range(ox + 1, ox + 14):
            put(img, [(x, y)], WHITE if (x + y) % 2 == 0 else BLACK)
    # Thumb sticking out on the left.
    for y in range(11, 15):
        for x in range(ox - 1, ox + 2):
            put(img, [(x, y)], GLOVE_GREY if y < 14 else GLOVE_GREY_DARK)
    # Wrist strap with studs, then the cuff.
    rect(img, ox + 2, 18, ox + 12, 19, GLOVE_GREY)
    put(img, [(ox + 4, 18), (ox + 7, 18), (ox + 10, 18)], BLACK)
    rect(img, ox + 3, 20, ox + 11, 21, BLACK)
    outline(img)
    return img


def rabbitfoot():
    """A rabbit's foot on a short chain."""
    img = canvas()
    put(img, [(9, 0), (9, 1)], CHAIN)
    put(img, [(8, 2), (10, 2)], CHAIN)
    put(img, [(9, 3)], CHAIN)
    # Leg, widening into the heel and palm.
    rows = {
        4: (8, 10), 5: (8, 10), 6: (7, 11), 7: (7, 11), 8: (6, 12),
        9: (6, 12), 10: (5, 13), 11: (5, 13), 12: (5, 13), 13: (5, 13),
    }
    for y, (x0, x1) in rows.items():
        for x in range(x0, x1 + 1):
            shade = 0 if x < x0 + 2 else (2 if x > x1 - 2 else 1)
            put(img, [(x, y)], FUR[shade])
    # Three toes, side ones shorter, each with a claw.
    for x0, x1, bottom in ((5, 6, 17), (8, 10, 19), (12, 13, 17)):
        for y in range(14, bottom + 1):
            for x in range(x0, x1 + 1):
                put(img, [(x, y)], FUR[0] if x == x0 else (FUR[2] if x == x1 and x1 > x0 else FUR[1]))
        put(img, [((x0 + x1) // 2, bottom + 1)], CLAW)
    # Fur texture.
    put(img, [(8, 6), (10, 8), (7, 11), (11, 12), (9, 15)], FUR[2])
    outline(img)
    return img


def chute():
    """A packed drag chute: a small canopy of red and white panels over a grey pack, hung by a ring."""
    img = canvas()
    put(img, [(9, 0), (9, 1), (8, 2), (10, 2)], STRING)
    put(img, [(9, 2)], BUCKLE)
    # Canopy: a half dome of alternating red and white panels.
    dome = {
        3: (7, 11), 4: (5, 13), 5: (4, 14), 6: (3, 15), 7: (3, 15), 8: (3, 15),
    }
    for y, (x0, x1) in dome.items():
        for x in range(x0, x1 + 1):
            panel = ((x - 3) // 3) % 2
            colour = (CHUTE_RED if panel == 0 else CHUTE_WHITE)[0 if y < 6 else 1]
            put(img, [(x, y)], colour)
    # Scalloped skirt.
    for x in range(3, 16):
        if (x - 3) % 3 != 1:
            put(img, [(x, 9)], CHUTE_RED[1] if ((x - 3) // 3) % 2 == 0 else CHUTE_WHITE[1])
    # Shroud lines pulling in to the pack.
    for i, x in enumerate((4, 7, 11, 14)):
        tx = 9 + (x - 9) // 3
        for y in range(10, 14):
            lx = x + int((tx - x) * (y - 10) / 3)
            put(img, [(lx, y)], STRING_DARK)
    # Pack.
    rect(img, 6, 14, 12, 20, CHUTE_PACK[0])
    rect(img, 6, 18, 12, 20, CHUTE_PACK[1])
    rect(img, 6, 16, 12, 16, CHUTE_PACK[1])
    rect(img, 8, 15, 10, 17, BUCKLE)
    put(img, [(9, 16)], CHUTE_PACK[1])
    outline(img)
    return img


def save(stem, img):
    img.save(os.path.join(OUT, stem + "_charm.png"))
    # The icon is the charm without its top row.
    img.crop((0, 1, W, H)).save(os.path.join(OUT, stem + "_icon.png"))


if __name__ == "__main__":
    for stem, draw in (("glove", glove), ("rabbitfoot", rabbitfoot), ("chute", chute)):
        save(stem, draw())
    # A contact sheet at 8x, not shipped.
    sheet = Image.new("RGBA", (3 * (W * 8 + 16), H * 8), (60, 60, 60, 255))
    for i, stem in enumerate(("glove", "rabbitfoot", "chute")):
        im = Image.open(os.path.join(OUT, stem + "_charm.png")).resize((W * 8, H * 8), Image.NEAREST)
        sheet.alpha_composite(im, (i * (W * 8 + 16), 0))
    sheet.save(os.path.join(OUT, "speed_preview.png"))
    print("wrote glove, rabbitfoot, chute sprites")
