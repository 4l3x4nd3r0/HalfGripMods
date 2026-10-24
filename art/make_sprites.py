"""Draws the M1911 sprites from scratch (stainless frame and slide, black checkered grips).

Every sprite keeps the exact pixel size of the SMG sprite it replaces, because the game positions
and animates them by those sizes. Run:  python make_sprites.py  -> writes the PNGs next to this file.

Body canvas coordinates (150x79), from the SMG's scene layout:
  the slide sprite rests over x 62..145, y 3..20 and the loose magazine sits behind the body at x 106..122.
"""
import os
import random

from PIL import Image, ImageDraw

OUT = os.path.dirname(os.path.abspath(__file__))
rng = random.Random(1911)

CLEAR = (0, 0, 0, 0)

# Stainless ramps, top to bottom.
STEEL = [(214, 209, 194), (192, 186, 169), (169, 163, 146), (146, 140, 124), (121, 116, 102), (96, 91, 80), (70, 66, 58)]
ENGRAVE = (86, 80, 68)
ENGRAVE_HI = (226, 221, 206)
BRASS_SERR = [(214, 172, 84), (186, 142, 58), (150, 108, 38)]
PEARL = [(244, 241, 232), (230, 226, 214), (212, 208, 196), (196, 192, 182)]
PEARL_EDGE = (150, 146, 136)
HALO = [(244, 208, 104), (222, 178, 70)]
MANTLE = [(58, 112, 98), (40, 86, 76), (30, 64, 58)]
ROBE = [(176, 92, 78), (140, 66, 56)]
SKIN = (222, 184, 146)

STEEL_DARK = [(120, 128, 138), (96, 104, 114), (74, 81, 90), (54, 60, 68), (40, 45, 52)]
BLUED = [(88, 96, 106), (64, 71, 80), (46, 52, 60), (34, 39, 46), (26, 30, 36)]
OUTLINE = (30, 33, 38)
GRIP = (27, 27, 31)
GRIP_HI = (58, 58, 66)
GRIP_LO = (12, 12, 14)
BRASS = [(236, 204, 92), (201, 162, 52), (150, 112, 34)]


def mask_poly(size, pts):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m


def mask_rect(size, x0, y0, x1, y1):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rectangle([x0, y0, x1, y1], fill=255)
    return m


def mask_ellipse(size, x0, y0, x1, y1):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).ellipse([x0, y0, x1, y1], fill=255)
    return m


def union(*masks):
    out = masks[0].copy()
    for m in masks[1:]:
        out = Image.composite(Image.new("L", out.size, 255), out, m)
    return out


def subtract(a, b):
    out = a.copy()
    return Image.composite(Image.new("L", a.size, 0), out, b)


def noise(c, amount=4):
    d = rng.randint(-amount, amount)
    return tuple(max(0, min(255, v + d)) for v in c)


def paint(img, mask, ramp, outline=OUTLINE, grain=3, hilite=True):
    """Fill a mask with a top-to-bottom ramp, a 1px outline and a bright top edge."""
    w, h = img.size
    px = img.load()
    mp = mask.load()
    ys = [y for y in range(h) for x in range(w) if mp[x, y] > 127]
    if not ys:
        return
    top, bot = min(ys), max(ys)
    span = max(1, bot - top)

    def on(x, y):
        return 0 <= x < w and 0 <= y < h and mp[x, y] > 127

    for y in range(h):
        for x in range(w):
            if not on(x, y):
                continue
            edge = not (on(x - 1, y) and on(x + 1, y) and on(x, y - 1) and on(x, y + 1))
            if edge and outline is not None:
                px[x, y] = outline + (255,)
                continue
            t = (y - top) / span
            c = ramp[min(len(ramp) - 1, int(t * len(ramp)))]
            if hilite and not on(x, y - 2):
                c = ramp[0]
            px[x, y] = noise(c, grain) + (255,)


def dot(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((x, y), c + (255,))


def engrave(img, x0, y0, x1, y1, amp=2.0, freq=0.5, phase=0.0, curls=True):
    """Scroll work: a sine vine with leaf curls, drawn only over steel (never over outlines or empty pixels)."""
    px = img.load()
    yc = (y0 + y1) / 2.0

    def steel(x, y):
        if not (0 <= x < img.width and 0 <= y < img.height):
            return False
        r, g, b, a = px[x, y]
        return a > 0 and (r + g + b) / 3 > 100

    import math
    for x in range(x0, x1 + 1):
        y = int(round(yc + amp * math.sin(x * freq + phase)))
        if steel(x, y):
            px[x, y] = ENGRAVE + (255,)
        if steel(x, y + 1) and (x % 2 == 0):
            px[x, y + 1] = ENGRAVE_HI + (255,)
        if curls and int(x * freq + phase) % 6 == 0 and x % 3 == 0:
            for dx, dy in ((1, -2), (2, -2), (2, -1), (0, 2), (-1, 2), (-1, 1)):
                if steel(x + dx, y + dy):
                    px[x + dx, y + dy] = ENGRAVE + (255,)


# --------------------------------------------------------------------------------------- slide 83x17
def make_slide():
    W, H = 83, 17
    img = Image.new("RGBA", (W, H), CLEAR)
    size = (W, H)

    # Slide with a sloped nose at the front (left) and a flat top.
    slide = mask_poly(size, [(3, 3), (80, 3), (82, 5), (82, 15), (1, 15), (0, 12), (0, 6)])
    paint(img, slide, STEEL)

    # Engraved border along the top and a scroll on the front panel.
    engrave(img, 4, 4, 58, 5, amp=0.8, freq=0.9, curls=False)
    engrave(img, 3, 8, 28, 13, amp=2.2, freq=0.55)
    engrave(img, 3, 11, 28, 15, amp=1.4, freq=0.7, phase=1.5, curls=False)
    engrave(img, 60, 4, 80, 5, amp=0.6, freq=1.0, curls=False)

    # Engraved plaque in the middle of the slide ("Crafted by ...").
    for x in range(31, 59):
        dot(img, x, 7, (118, 112, 98))
        dot(img, x, 11, (118, 112, 98))
        if x % 2 == 0:
            dot(img, x, 9, ENGRAVE)
    for y in range(8, 11):
        dot(img, 31, y, (118, 112, 98))
        dot(img, 58, y, (118, 112, 98))

    # Brass rear serrations.
    for x in range(61, 78, 2):
        for y in range(5, 15):
            dot(img, x, y, BRASS_SERR[(y - 5) * len(BRASS_SERR) // 10])
            dot(img, x + 1, y, (116, 82, 30))

    # Slide stop notch on the bottom edge.
    for x in range(38, 43):
        img.putpixel((x, 15), CLEAR)

    # Front sight blade and rear sight with a notch.
    fs = mask_poly(size, [(6, 0), (9, 0), (10, 3), (5, 3)])
    paint(img, fs, STEEL_DARK, hilite=False)
    rs = mask_poly(size, [(67, 1), (79, 1), (80, 3), (66, 3)])
    paint(img, rs, STEEL_DARK, hilite=False)
    for x in range(71, 76):
        for y in range(1, 3):
            dot(img, x, y, (16, 17, 20))

    return img


# --------------------------------------------------------------------------------------- body 150x79
def make_body():
    W, H = 150, 79
    img = Image.new("RGBA", (W, H), CLEAR)
    size = (W, H)

    # Hammer behind the slide.
    hammer = mask_poly(size, [(142, 12), (148, 11), (149, 13), (149, 18), (142, 19)])
    paint(img, hammer, STEEL)
    for y in range(13, 18, 2):
        for x in range(145, 149):
            dot(img, x, y, ENGRAVE)

    # Lanyard ring at the rear of the frame.
    ring = subtract(mask_ellipse(size, 141, 2, 149, 10), mask_ellipse(size, 143, 4, 147, 8))
    paint(img, ring, STEEL, outline=(60, 56, 48), hilite=False, grain=1)

    # Barrel and bushing sticking out of the slide's nose.
    barrel = mask_rect(size, 52, 8, 57, 12)
    paint(img, barrel, STEEL_DARK, hilite=False)
    for x in range(52, 54):
        for y in range(9, 12):
            dot(img, x, y, (10, 10, 12))
    bushing = mask_poly(size, [(56, 5), (62, 5), (62, 16), (56, 16)])
    paint(img, bushing, STEEL)

    # Frame: dust cover under the slide and the rail it rides on.
    frame = mask_poly(size, [(58, 17), (146, 17), (146, 24), (88, 24), (80, 28), (58, 28), (56, 24)])
    paint(img, frame, STEEL)
    # Recoil spring plug visible at the nose of the dust cover.
    for x in range(58, 62):
        for y in range(18, 23):
            dot(img, x, y, (92, 86, 74))
    # Rail lines.
    for x in range(64, 120):
        dot(img, x, 21, (110, 104, 90))
    engrave(img, 64, 18, 112, 20, amp=0.7, freq=0.8, curls=False)
    engrave(img, 62, 22, 82, 27, amp=1.6, freq=0.6)

    # Trigger guard: a ring joined to the frame and the front strap.
    outer = mask_poly(size, [(78, 26), (101, 26), (101, 44), (96, 47), (86, 47), (80, 43), (77, 36)])
    inner = mask_poly(size, [(83, 29), (97, 29), (97, 41), (94, 43), (87, 43), (83, 39)])
    paint(img, subtract(outer, inner), STEEL)

    # Trigger inside the guard.
    trig = mask_poly(size, [(94, 27), (97, 27), (96, 35), (93, 38), (91, 36), (93, 33)])
    paint(img, trig, BRASS_SERR, outline=(96, 66, 22), hilite=False, grain=1)

    # Grip frame (front strap, backstrap with the beavertail, mainspring housing).
    grip = mask_poly(size, [(99, 24), (146, 24), (149, 26), (148, 29), (141, 36), (145, 70), (143, 76), (138, 79), (105, 79), (103, 76), (101, 48), (98, 33)])
    paint(img, grip, STEEL)

    # Mother-of-pearl grip panel with a small Virgin of Guadalupe inlay.
    panel = mask_poly(size, [(103, 31), (137, 31), (140, 37), (142, 70), (139, 75), (134, 76), (108, 76), (105, 70), (104, 50)])
    paint(img, panel, PEARL, outline=PEARL_EDGE, grain=3, hilite=False)
    pm = panel.load()
    # Soft swirls in the pearl.
    for y in range(33, 75):
        for x in range(105, 141):
            if pm[x, y] > 127 and pm[x - 1, y] > 127 and pm[x + 1, y] > 127 and pm[x, y - 1] > 127 and pm[x, y + 1] > 127:
                if ((x * 3 + y * 5) // 4) % 9 == 0:
                    dot(img, x, y, (188, 192, 196))
    # Gold halo with rays.
    import math
    cx, cy = 122, 54
    halo = mask_ellipse(size, cx - 11, cy - 21, cx + 11, cy + 21)
    paint(img, halo, HALO, outline=(190, 146, 52), hilite=False, grain=2)
    for k in range(28):
        ang = k * (2 * math.pi / 28)
        rx, ry = 13.2, 23.4
        dot(img, int(round(cx + rx * math.cos(ang))), int(round(cy + ry * math.sin(ang))), (226, 184, 80))
    # Mantle, robe, head and the crescent under her feet.
    mantle = mask_poly(size, [(122, 36), (127, 39), (130, 52), (131, 66), (113, 66), (114, 52), (117, 39)])
    paint(img, mantle, MANTLE, outline=(24, 52, 46), hilite=False, grain=2)
    robe = mask_poly(size, [(120, 44), (124, 44), (126, 56), (127, 67), (117, 67), (118, 56)])
    paint(img, robe, ROBE, outline=(110, 50, 44), hilite=False, grain=2)
    head = mask_ellipse(size, 120, 35, 124, 40)
    paint(img, head, [SKIN], outline=(150, 112, 84), hilite=False, grain=1)
    for x, y in ((118, 50), (125, 47), (116, 58), (128, 60), (120, 41), (124, 62)):
        dot(img, x, y, (236, 200, 96))
    dot(img, 121, 47, (236, 200, 150))
    dot(img, 122, 47, (236, 200, 150))
    crescent = subtract(mask_ellipse(size, 114, 66, 130, 71), mask_ellipse(size, 114, 64, 130, 69))
    paint(img, crescent, [(40, 40, 46)], outline=None, hilite=False, grain=1)

    # Grip screw bushing.
    bush = mask_ellipse(size, 118, 40, 122, 44)
    paint(img, bush, STEEL, outline=(70, 64, 54), hilite=False, grain=1)

    engrave(img, 126, 24, 146, 28, amp=1.0, freq=0.7, curls=False)

    # Beavertail grip safety and thumb safety.
    beaver = mask_poly(size, [(124, 22), (149, 22), (149, 27), (142, 33), (136, 30), (128, 27)])
    paint(img, beaver, STEEL)
    safety = mask_poly(size, [(116, 22), (130, 22), (131, 26), (118, 26)])
    paint(img, safety, STEEL_DARK)

    # Slide stop and magazine release.
    stop = mask_poly(size, [(84, 24), (91, 24), (92, 28), (85, 28)])
    paint(img, stop, STEEL_DARK, hilite=False)
    cat = mask_ellipse(size, 105, 33, 108, 36)
    paint(img, cat, STEEL_DARK, outline=(40, 45, 52), hilite=False, grain=1)

    return img


# --------------------------------------------------------------------------------------- back strip 80x11
def make_back():
    """The dark strip that sits behind the slide. For the 1911 it is the barrel hood showing when the slide runs back."""
    W, H = 80, 11
    img = Image.new("RGBA", (W, H), CLEAR)
    size = (W, H)
    hood = mask_rect(size, 14, 2, 79, 9)
    paint(img, hood, BLUED, outline=(16, 18, 22), hilite=True)
    return img


# --------------------------------------------------------------------------------------- magazines
def make_mag(w, h, full):
    img = Image.new("RGBA", (w, h), CLEAR)
    size = (w, h)
    top = 4 if full else 1

    if full:
        # Top round, brass.
        rnd = mask_poly(size, [(5, 0), (10, 0), (11, 5), (4, 5)])
        paint(img, rnd, BRASS, outline=(96, 70, 20), hilite=False, grain=2)

    # Feed lips and tube.
    body = mask_poly(size, [(2, top + 2), (13, top + 2), (14, top + 5), (14, h - 7), (1, h - 7), (1, top + 5)])
    paint(img, body, BLUED, outline=(14, 16, 20))
    lips = mask_poly(size, [(3, top), (12, top), (13, top + 3), (2, top + 3)])
    paint(img, lips, STEEL_DARK, hilite=False)

    # Witness holes down the side.
    for i in range(8):
        y = top + 8 + i * 4
        if y + 2 < h - 9:
            for dy in range(2):
                for x in range(6, 9):
                    dot(img, x, y + dy, (200, 190, 150) if (full and i < 5) else (12, 13, 16))

    # Base pad.
    base = mask_poly(size, [(0, h - 7), (15, h - 7), (15, h - 1), (0, h - 1)])
    paint(img, base, [GRIP, (20, 20, 24)], outline=GRIP_LO, hilite=False)
    for x in range(2, 14, 2):
        dot(img, x, h - 4, GRIP_HI)

    return img


def make_round():
    """Fully transparent: the game shows a chambered round and a feed round on the resting gun, and neither should be seen."""
    return Image.new("RGBA", (8, 6), CLEAR)


def make_feed():
    return Image.new("RGBA", (13, 5), CLEAR)


# --------------------------------------------------------------------------------------- icon + preview
def compose(body, slide, back, mag):
    """The gun as it sits in the car: body, back strip, slide, then the mag sticking out of the grip (behind the body)."""
    canvas = Image.new("RGBA", (150, 100), CLEAR)
    canvas.alpha_composite(mag, (106, 36))
    canvas.alpha_composite(body, (0, 0))
    canvas.alpha_composite(back, (48, 9))
    canvas.alpha_composite(slide, (62, 3))
    return canvas


def trim(img):
    box = img.getbbox()
    return img.crop(box) if box else img


def main():
    body = make_body()
    slide = make_slide()
    back = make_back()
    mag_empty = make_mag(16, 54, False)
    mag_full = make_mag(16, 56, True)
    rnd = make_round()

    files = {
        "m1911_body.png": body,
        "m1911_slide.png": slide,
        "m1911_back.png": back,
        "m1911_mag_empty.png": mag_empty,
        "m1911_mag_full.png": mag_full,
        "m1911_round.png": rnd,
        "m1911_feed.png": make_feed(),
    }
    for name, im in files.items():
        im.save(os.path.join(OUT, name))

    gun = compose(body, slide, back, mag_full)

    # Inventory icon: the game's slot expects 31x18 (the SMG's icon size), so shrink the gun, without its magazine, to fit.
    icon_src = trim(compose(body, slide, back, Image.new("RGBA", (16, 54), CLEAR)))
    scale = min(31 / icon_src.width, 18 / icon_src.height)
    small = icon_src.resize((max(1, round(icon_src.width * scale)), max(1, round(icon_src.height * scale))), Image.LANCZOS)
    icon = Image.new("RGBA", (31, 18), CLEAR)
    icon.alpha_composite(small, ((31 - small.width) // 2, (18 - small.height) // 2))
    icon.save(os.path.join(OUT, "m1911_icon.png"))

    # Preview (not shipped): the gun at 5x on the game's dark cabin colour, next to the loose parts.
    bg = (58, 58, 68, 255)
    big = gun.resize((gun.width * 5, gun.height * 5), Image.NEAREST)
    prev = Image.new("RGBA", (big.width + 5 * 16 * 2 + 80, big.height + 20), bg)
    prev.alpha_composite(big, (10, 10))
    x = big.width + 30
    for im in (mag_empty, mag_full):
        s = im.resize((im.width * 5, im.height * 5), Image.NEAREST)
        prev.alpha_composite(s, (x, 10))
        x += s.width + 20
    prev.save(os.path.join(OUT, "preview.png"))
    print("wrote", ", ".join(files), "m1911_icon.png preview.png", "icon", icon.size)


if __name__ == "__main__":
    main()
