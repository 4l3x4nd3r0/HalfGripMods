"""Builds the Judgment Day art (python -I make_jd_sprites.py, needs Pillow and numpy) from the user's reference picture, the Terminator wiki's
1887.jpg (art\\jd_reference.png (not in the repo: download the 1887.jpg picture from the Terminator wiki, https://terminator.fandom.com/wiki/Winchester_Model_1887, and save it as PNG), 500x126, a render on white), painted in the game's own style.

The SHAPE is the reference's: the picture is cut out, shrunk to a short gun GUN_W pixels long (the game's slug shotgun sprite is 258 long)
and every pixel is sorted into steel, wood or butt pad. The LOOK is the game's (read off its slug shotgun sprite IMG_3632_0 and the
45x11 card icons): silver steel in four tones with orange rust patches, brown wood in four tones with dark red blotches, a black butt
pad, a dark edge. A photo-real version was rejected ('too realistic for Half Grip'). The lever is drawn by hand as a chunky loop and
taken out of the body so the plugin can swing it about its hinge at the stock end; the card icon is drawn by hand in the flat style of
the game's other card icons.

It takes the place of the slug shotgun's sprites, so every file is the size of the sprite it replaces
(IMG_3632_0 / under / cover are 258x48, the pump IMG_3633_0 is 33x15):
  jd_body.png        258x48  the gun without the lever. Muzzle on the left
  jd_clear.png       258x48  empty, for the shotgun's "under" and "cover" layers
  jd_pump_clear.png   33x15  empty, for the game's pump sprite (the held model's pump part is switched off)
  jd_lever.png        WxH    the lever loop on its own (size printed below); the plugin turns it about the hinge (also printed)
  jd_lever_layer.png 258x48  the lever alone, in place: the held gun's lever mesh is extruded from this (the held body is jd_body.png)
  jd_icon.png         45x11  the shop card's icon
and jd_preview.png, enlarged, for looking at.

Numbers the plugin relies on (printed at the end; the constants in Lever.cs must match): where the lever sprite sits in the body, its
size, and its hinge. Keep clear of the rear-view mirror (columns 13..110 below row 41) and the leaderboard (columns 197.. below row 28).

The reference picture is a fan wiki's picture of the film prop: the art made from it has an unconfirmed licence, like the Dacia's and the
Sniper's, so keep it out of the MIT licence of any release."""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUTDIR = HERE               # redraw-art.ps1 copies jd_*.png into mods\JudgmentDay\sprites
REF = os.path.join(HERE, "jd_reference.png")

W, H = 258, 48
GUN_W = 168                 # the gun's length in pixels (the slug shotgun is 258)
GUN_AT = (16, 2)            # where the gun's top left goes in the 258x48 body: puts the receiver where the game's loading area is
RIM = (30, 26, 28)
PIVOT = (424, 72)           # the lever's hinge in the reference: the front end of the loop, under the receiver by the trigger
LEVER_SX, LEVER_SY = 1.55, 1.45   # how much bigger the lever loop is than the reference's, about its pivot
RECEIVER_X = (322, 440)     # the receiver's span along the reference picture, to tell it from barrel and stock

# The game's palette, sampled from its slug shotgun sprite and card icons.
STEEL = [(52, 52, 60), (112, 112, 120), (172, 172, 178), (224, 224, 228)]
WOOD = [(58, 26, 16), (98, 48, 26), (140, 76, 40), (172, 104, 56)]
RUST = [(104, 46, 22), (150, 74, 32), (190, 100, 46)]
BLOTCH = [(78, 30, 22), (112, 52, 30)]
PAD = [(26, 26, 30), (46, 46, 52)]
BRASS_DARK = (150, 112, 40)
BRASS_LIGHT = (232, 200, 108)
rng = random.Random(1887)


def cut_out(ref):
    """The reference without its white background and without its thin lever (which is redrawn), as an RGBA image in reference pixels."""
    w, h = ref.size
    tmp = ref.copy()
    for seed in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w // 2, 0), (w // 2, h - 1)):
        ImageDraw.floodfill(tmp, seed, (255, 0, 255), thresh=42)
    t = np.asarray(tmp).astype(int)
    bg = (t[:, :, 0] == 255) & (t[:, :, 1] == 0) & (t[:, :, 2] == 255)
    fg = ~bg
    r = np.asarray(ref).astype(int)
    ys, xs = np.mgrid[0:h, 0:w]
    # The lever hangs under the receiver and the stock: take out the front part wholesale, and from the part against the stock keep only
    # the brown wood.
    fg &= ~((xs >= 395) & (xs < 425) & (ys >= 70))
    brown = (r[:, :, 0] - r[:, :, 2]) >= 10
    fg &= ~((xs >= 425) & (ys >= 68) & ~brown)
    mask = Image.fromarray((fg * 255).astype(np.uint8))
    mask = mask.filter(ImageFilter.MinFilter(3))             # no pale fringe from the white background
    m = np.asarray(mask) > 0
    rgb = r.copy()
    rgb[~m] = RIM
    out = np.dstack([rgb.astype(np.uint8), (m * 255).astype(np.uint8)])
    return Image.fromarray(out, "RGBA")


def tones(lum, sel, noise=0.07):
    """Tone 0..3 of each selected pixel from its rank in the selection (so a near-black receiver still gets all four tones)."""
    vals = lum[sel]
    lo, hi = np.percentile(vals, 6), np.percentile(vals, 96)
    level = np.clip((lum - lo) / max(1e-6, hi - lo), 0, 1)
    level = np.clip(level + np.random.RandomState(7).uniform(-noise, noise, lum.shape), 0, 1)
    return np.minimum(3, (level * 4).astype(int))


def blobs(mask_ok, count, size, rnd):
    """Random-walk blobs inside mask_ok: lists of pixels."""
    h, w = mask_ok.shape
    ys, xs = np.nonzero(mask_ok)
    found = []
    if len(xs) == 0:
        return found
    for _ in range(count):
        i = rnd.randrange(len(xs))
        x, y = int(xs[i]), int(ys[i])
        cells = []
        for _ in range(rnd.randint(size // 2, size)):
            if 0 <= x < w and 0 <= y < h and mask_ok[y, x]:
                cells.append((x, y))
            dx, dy = rnd.choice(((1, 0), (-1, 0), (0, 1), (0, 1), (1, 0), (0, -1)))
            x, y = x + dx, y + dy
        found.append(cells)
    return found


def game_style(img, bx0, scale):
    """Paints the shrunk reference in the game's palette: steel, wood and butt pad by position and colour, tones by rank."""
    a = np.asarray(img).astype(float)
    h, w = a.shape[:2]
    alpha = a[:, :, 3] > 127
    rgb = a[:, :, :3]
    lum = 0.3 * rgb[:, :, 0] + 0.59 * rgb[:, :, 1] + 0.11 * rgb[:, :, 2]
    ys, xs = np.mgrid[0:h, 0:w]
    ref_x = bx0 + xs / scale
    wood = alpha & ((rgb[:, :, 0] - rgb[:, :, 2]) >= 8)
    receiver = alpha & ~wood & (ref_x >= RECEIVER_X[0]) & (ref_x <= RECEIVER_X[1])
    steel = alpha & ~wood
    pad = wood & (xs >= w - 5)
    wood = wood & ~pad

    # Edge: any opaque pixel with a clear neighbour below or to the right gets the darkest tone, like the game's sprites.
    clear = ~alpha
    below = np.zeros_like(alpha)
    below[:-1, :] = clear[1:, :]
    below[-1, :] = True
    right = np.zeros_like(alpha)
    right[:, :-1] = clear[:, 1:]
    right[:, -1] = True
    edge = alpha & (below | right)

    out = np.zeros((h, w, 4), dtype=np.uint8)
    t_steel = tones(lum, steel & ~edge)
    t_wood = tones(lum, (wood | pad) & ~edge)
    for y in range(h):
        for x in range(w):
            if not alpha[y, x]:
                continue
            if pad[y, x]:
                out[y, x] = (*PAD[1 if lum[y, x] > np.percentile(lum[pad], 60) else 0], 255)
            elif wood[y, x]:
                out[y, x] = (*WOOD[0 if edge[y, x] else t_wood[y, x]], 255)
            else:
                out[y, x] = (*STEEL[0 if edge[y, x] else t_steel[y, x]], 255)

    # Rust on the receiver and a little on the barrel; dark red blotches on the wood.
    rnd = random.Random(33)
    inner_steel = steel & ~edge & (out[:, :, 0] > 100)
    for cells in blobs(inner_steel & receiver, 9, 12, rnd) + blobs(inner_steel & ~receiver, 4, 8, rnd):
        for x, y in cells:
            out[y, x] = (*RUST[rnd.randrange(3)], 255)
    inner_wood = wood & ~edge
    for cells in blobs(inner_wood, 14, 9, rnd):
        for x, y in cells:
            out[y, x] = (*BLOTCH[rnd.randrange(2)], 255)
    return Image.fromarray(out, "RGBA")


def build():
    ref = Image.open(REF).convert("RGB")
    cut = cut_out(ref)
    bbox = cut.getchannel("A").getbbox()
    bx0, by0, bx1, by1 = bbox
    s = GUN_W / float(bx1 - bx0)
    gh = int(round((by1 - by0) * s))
    gun = cut.crop(bbox).resize((GUN_W, gh), Image.LANCZOS)
    gun = game_style(gun, bx0, s)
    body = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    body.alpha_composite(gun, GUN_AT)

    def to_body(x, y):
        return GUN_AT[0] + (x - bx0) * s, GUN_AT[1] + (y - by0) * s

    return body, to_body, s


def chaikin(points, rounds=2):
    for _ in range(rounds):
        out = []
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            out.append((0.75 * x0 + 0.25 * x1, 0.75 * y0 + 0.25 * y1))
            out.append((0.25 * x0 + 0.75 * x1, 0.25 * y0 + 0.75 * y1))
        points = out + [points[-1]]
    return points


def lever(to_body):
    # The loop's centre line in the reference's pixels (read off a 5x enlargement of the reference), smoothed into a plain oval-ish ring:
    # the front bend, along the bottom, the rear end against the stock, and back along the stock to the pivot.
    ref_pts = [(424, 72), (413, 84), (407, 92), (420, 97), (436, 103), (452, 112), (465, 112), (472, 104), (468, 92), (456, 80), (446, 72), (433, 70), (424, 72)]
    # Bigger than the real thing, as a game's lever is: the loop is stretched about the pivot (easy to see and to grab).
    big = [(PIVOT[0] + (x - PIVOT[0]) * LEVER_SX, PIVOT[1] + (y - PIVOT[1]) * LEVER_SY) for x, y in ref_pts]
    pts = chaikin([to_body(x, y) for x, y in big])
    pivot = to_body(*PIVOT)
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    at = (int(min(xs)) - 5, int(min(ys)) - 6)
    size = (int(max(xs)) - at[0] + 6, int(max(ys)) - at[1] + 6)
    img = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    local = [(x - at[0], y - at[1]) for x, y in pts]
    edge, mid, light = STEEL[0] + (255,), STEEL[1] + (255,), STEEL[2] + (255,)
    hinge = (int(round(pivot[0])) - at[0], int(round(pivot[1])) - at[1])
    hx, hy = hinge

    # The loop is a cast band: thick where the two arms leave the pivot, thinner along the bottom (as in the ArtStation M1887 model).
    n = len(local)

    def width_at(i):
        u = math.sin(math.pi * (i + 0.5) / max(1, n - 1))
        return 7 - 2 * u            # 7 at the arms, 5 along the bottom

    def pass_(wdelta, colour):
        for i in range(n - 1):
            w = max(1, int(round(width_at(i))) - wdelta)
            d.line([local[i], local[i + 1]], fill=colour, width=w)
            r = w / 2.0
            d.ellipse((local[i][0] - r + 0.5, local[i][1] - r + 0.5, local[i][0] + r - 0.5, local[i][1] + r - 0.5), fill=colour)

    pass_(0, edge)
    pass_(2, mid)
    d.line([(x - 0.6, y - 1.0) for x, y in local], fill=light, width=1, joint="curve")
    # A little rust and wear on the band, like the game's own metal.
    rnd = random.Random(5)
    for _ in range(7):
        i = rnd.randrange(n)
        px, py = int(local[i][0]), int(local[i][1] + 1)
        if 0 <= px < size[0] and 0 <= py < size[1] and img.getpixel((px, py))[3] > 0:
            img.putpixel((px, py), RUST[rnd.randrange(3)] + (255,))

    # The trigger: in the 1887 it is carried on the lever, hanging inside the loop's front half: a blade that curves down and ends in a
    # little hook that curls back.
    trig = [(hx + 2, hy + 2), (hx + 1, hy + 6), (hx + 1, hy + 10), (hx + 3, hy + 12), (hx + 5, hy + 11)]
    d.line(trig, fill=edge, width=4, joint="curve")
    d.line(trig, fill=mid, width=2, joint="curve")
    d.line([(x - 0.5, y - 0.5) for x, y in trig[:3]], fill=light, width=1)

    # The knuckle at the pivot: a rounded block with a brass pin and a screw, where the lever meets the receiver.
    d.rounded_rectangle((hx - 4, hy - 3, hx + 6, hy + 3), radius=2, fill=edge)
    d.rounded_rectangle((hx - 3, hy - 2, hx + 5, hy + 2), radius=1, fill=mid)
    d.line((hx - 2, hy - 2, hx + 4, hy - 2), fill=light, width=1)
    d.rectangle((hx - 1, hy - 1, hx + 1, hy + 1), fill=BRASS_DARK + (255,))
    d.point((hx, hy), fill=BRASS_LIGHT + (255,))
    d.point((hx + 4, hy), fill=STEEL[0] + (255,))
    return img, at, hinge


def icon():
    """The card icon, drawn by hand in the flat style of the game's other 45x11 gun icons: a grey barrel, a grey receiver block with a
    light top, a brown stock with grain, a dark outline on top, the lever loop under the receiver."""
    o = (48, 48, 52, 255)          # outline
    G = (206, 206, 210, 255)       # light steel
    g = (150, 150, 156, 255)       # steel
    d = (96, 96, 104, 255)         # shade
    wl = (176, 108, 60, 255)       # wood light
    wm = (140, 76, 40, 255)        # wood
    wk = (92, 46, 28, 255)         # wood dark
    r = (186, 94, 42, 255)         # rust
    pad = (30, 30, 34, 255)
    img = Image.new("RGBA", (45, 11), (0, 0, 0, 0))
    px = img.load()

    def put(x, y, c):
        if 0 <= x < 45 and 0 <= y < 11:
            px[x, y] = c

    def row(y, x0, x1, c):
        for x in range(x0, x1 + 1):
            put(x, y, c)

    # Barrel: outline, light top, body, shade; the tube end under it.
    row(1, 1, 27, o)
    row(2, 1, 27, G)
    row(3, 1, 27, g)
    row(4, 1, 8, d)
    row(5, 3, 8, g)
    row(6, 3, 8, d)
    row(7, 3, 8, o)
    put(2, 0, o)
    # Long forend, a little grain.
    row(4, 9, 27, wl)
    row(5, 9, 27, wm)
    row(6, 9, 27, wm)
    row(7, 9, 27, wk)
    for x in range(11, 27, 3):
        put(x, 5, wk)
    # Receiver block.
    row(0, 29, 35, o)
    for y in range(1, 8):
        row(y, 28, 36, g)
    row(1, 29, 35, G)
    row(2, 29, 35, G)
    row(6, 28, 36, d)
    row(7, 28, 36, d)
    row(3, 30, 33, o)
    put(31, 5, r)
    put(32, 5, r)
    put(33, 4, r)
    put(36, 0, o)
    put(37, 0, o)
    # Stock: sloping to the butt, with a pad.
    for x in range(37, 44):
        top = 2 + (x - 37) // 3
        bottom = 7 + (x - 37) // 2
        for y in range(top, bottom + 1):
            put(x, y, wm)
        put(x, top, wl)
        put(x, bottom, wk)
    for y in range(4, 10):
        put(43, y, pad)
    put(40, 5, wk)
    put(41, 6, wk)
    # The lever loop under the receiver: a plain ring.
    put(30, 8, d)
    put(29, 9, d)
    row(10, 30, 35, d)
    put(36, 9, d)
    put(37, 8, d)
    return img


def save(img, name):
    img.save(os.path.join(OUTDIR, name))


if not os.path.exists(REF):
    raise SystemExit("Missing " + REF + ": the reference picture is not in the repo (see the top of this script).")
os.makedirs(OUTDIR, exist_ok=True)
body, to_body, scale = build()
lev, lever_at, lever_hinge = lever(to_body)
held = body.copy()
held.alpha_composite(lev, lever_at)
save(body, "jd_body.png")
save(Image.new("RGBA", (W, H), (0, 0, 0, 0)), "jd_clear.png")
save(Image.new("RGBA", (33, 15), (0, 0, 0, 0)), "jd_pump_clear.png")
save(lev, "jd_lever.png")
layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
layer.alpha_composite(lev, lever_at)
save(layer, "jd_lever_layer.png")
ic = icon()
save(ic, "jd_icon.png")

prev = Image.new("RGBA", (W * 4, H * 4 + 11 * 8 + 8), (60, 70, 60, 255))
prev.alpha_composite(held.resize((W * 4, H * 4), Image.NEAREST), (0, 0))
prev.alpha_composite(ic.resize((45 * 8, 11 * 8), Image.NEAREST), (0, H * 4 + 4))
prev.save(os.path.join(HERE, "jd_preview.png"))

print("scale", round(scale, 4), "gun bbox in the body", held.getchannel("A").getbbox())
print("LEVER_AT", lever_at, "LEVER_SIZE", lev.size, "LEVER_HINGE", lever_hinge,
      "-> hinge in the body at", (lever_at[0] + lever_hinge[0], lever_at[1] + lever_hinge[1]))
