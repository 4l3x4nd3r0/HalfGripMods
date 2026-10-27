"""Draws the Railgun's art (python -I make_railgun_sprites.py, needs Pillow): a scrap-built gauss gun in the game's worn, dark-outlined
pixel style, a Mad Max piece of work: a rusty exhaust pipe for a barrel, six ignition coils with spark-plug caps welded on a rack above
it, a car battery strapped underneath with jumper cables (one clamped on the rack), a hazard-striped receiver with a painted
lightning bolt, a rag-wrapped grip and a bent-pipe stock. The pump is an air-pump plunger: a ribbed sleeve with a pressure gauge on top
and a T-handle on a rod.

It takes the place of the slug shotgun's sprites, so every file is exactly the size of the sprite it replaces (read from the game:
IMG_3632_0, IMG_3632_under_0 and IMG_3632_cover are 258x48, the pump IMG_3633_0 is 33x15, the shell is 12x24):
  railgun_body.png     258x48  the gun without the pump, the coils dark. Muzzle on the left, as the shotgun's
  railgun_held.png     258x48  the same with the pump drawn at its rest place: the held 3D model is extruded from this
  railgun_coil_0..5    258x48  one coil each, lit (and the muzzle's bore for coil 0), on a clear canvas: the plugin lays them over the body
                               in steps as the charge builds, and draws the arcs between the spark plugs itself (COIL_X, TIP_Y below)
  railgun_clear.png    258x48  empty, for the shotgun's "under" and "cover" layers
  railgun_pump.png      33x15  the air pump: sleeve, gauge face (the needle is drawn by the plugin, centre GAUGE), rod, T-handle
  railgun_ball.png      12x24  a greasy steel ball bearing, for the shells
  railgun_icon.png      45x11  the shop card's icon (the gun, small)
and railgun_preview.png, everything enlarged, for looking at.

Numbers the plugin relies on (they are printed at the end; the constants in RailLayout.cs must match):
  the pump rests over body x 23..56, y 9..24 (the game puts it there); the body's grab area is near x 130, y 10."""
import os
import random
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
rng = random.Random(1979)

W, H = 258, 48
CLEAR = (0, 0, 0, 0)
OUT = (18, 16, 18, 255)
STEEL = [(40, 42, 48, 255), (78, 82, 92, 255), (122, 128, 140, 255), (172, 178, 190, 255)]
RUST = [(64, 30, 16, 255), (112, 56, 26, 255), (160, 86, 40, 255), (202, 122, 62, 255)]
COPPER = [(58, 28, 14, 255), (128, 66, 30, 255), (190, 112, 54, 255), (232, 160, 92, 255)]
BRASS = [(66, 50, 16, 255), (140, 108, 34, 255), (196, 160, 62, 255), (238, 206, 110, 255)]
RAG = [(70, 58, 40, 255), (122, 104, 70, 255), (170, 150, 106, 255), (206, 188, 140, 255)]
CERAMIC = [(120, 118, 108, 255), (176, 172, 158, 255), (224, 220, 204, 255)]
YELLOW = (216, 172, 38, 255)
BLACK = (28, 26, 28, 255)
RED = [(96, 18, 18, 255), (176, 36, 32, 255), (226, 70, 56, 255)]
BATT = [(30, 36, 46, 255), (58, 68, 84, 255), (92, 106, 126, 255)]
DIM = (34, 78, 104, 255)
GLOW = (110, 214, 255, 255)
GLOW_CORE = (226, 248, 255, 255)
HALO = (90, 190, 255, 110)

COIL_X = [24, 44, 64, 84, 104, 124]     # left edge of each ignition coil, rows 0..8, nine wide
COIL_W = 9
DIGIT_X, DIGIT_ROW = 152, 13           # the shots-loaded digit the plugin draws in the receiver's window: 3x5 pixels
LAMP_X, LAMP_Y = 187, 12                # the ammo lamp on the receiver: a 5x5 round bulb, green when a ball is chambered, red when not
TIP_Y = 0                               # the spark plug's terminal row; its x is COIL_X + 4
PUMP_REST = (23, 9)                     # where the pump sprite's top left sits in the body
GAUGE = (10.5, 4.5)                     # the gauge's centre in the pump sprite, pixels from its top left


def rect(d, x0, y0, x1, y1, fill, outline=OUT):
    d.rectangle((x0, y0, x1, y1), fill=outline)
    d.rectangle((x0 + 1, y0 + 1, x1 - 1, y1 - 1), fill=fill)


def tube(d, x0, y0, x1, y1, ramp):
    """A horizontal tube: outline, a highlight on top shading to a dark underside."""
    d.rectangle((x0, y0, x1, y1), fill=OUT)
    h = y1 - y0 - 1
    for i in range(h):
        t = i / max(1, h - 1)
        shade = 3 if t < 0.2 else 2 if t < 0.45 else 1 if t < 0.8 else 0
        d.line((x0 + 1, y0 + 1 + i, x1 - 1, y0 + 1 + i), fill=ramp[shade])


def rod(d, points, colour, width=3, outline=True):
    if outline:
        d.line(points, fill=OUT, width=width + 2, joint="curve")
    d.line(points, fill=colour, width=width, joint="curve")


def speckle(img, box, colour, amount, only=None):
    x0, y0, x1, y1 = box
    px = img.load()
    for _ in range(amount):
        x = rng.randint(x0, x1)
        y = rng.randint(y0, y1)
        p = px[x, y]
        if p[3] > 0 and p != OUT and (only is None or p in only):
            px[x, y] = colour


def weld(d, x, y, n=4, vertical=False):
    """A bead of weld: a few light dots in a short row."""
    for i in range(n):
        xx, yy = (x, y + i * 2) if vertical else (x + i * 2, y)
        d.point((xx, yy), fill=STEEL[3])
        d.point((xx + (0 if vertical else 1), yy + (1 if vertical else 0)), fill=STEEL[1])


def bulb(d, cx, cy, colour):
    """The lamp's round 5x5 glass: a plus-shaped square with the corners off."""
    d.rectangle((cx - 1, cy - 2, cx + 1, cy + 2), fill=colour)
    d.rectangle((cx - 2, cy - 1, cx + 2, cy + 1), fill=colour)


def coil(d, x, lit=False):
    """An ignition coil: a ceramic spark-plug cap on a copper-wound can. Lit, only the windings and the plug's tip glow."""
    if lit:
        for yy in range(3, 9):
            for xx in range(x + 1, x + COIL_W - 1):
                d.point((xx, yy), fill=GLOW_CORE if (xx + yy) % 3 == 0 else GLOW)
        d.point((x + 4, 0), fill=GLOW_CORE)
        d.point((x + 4, 1), fill=GLOW)
        d.point((x + 3, 1), fill=HALO)
        d.point((x + 5, 1), fill=HALO)
        for yy in range(3, 9):
            d.point((x - 1, yy), fill=HALO)
            d.point((x + COIL_W, yy), fill=HALO)
        return
    # the plug cap
    d.rectangle((x + 3, 0, x + 5, 3), fill=OUT)
    d.rectangle((x + 4, 0, x + 4, 1), fill=STEEL[3])
    d.rectangle((x + 4, 2, x + 4, 3), fill=CERAMIC[2])
    d.point((x + 3, 2), fill=CERAMIC[1])
    d.point((x + 5, 2), fill=CERAMIC[0])
    # the can with its copper windings
    d.rectangle((x, 3, x + COIL_W - 1, 8), fill=OUT)
    for yy in range(4, 8):
        shade = (2, 3, 1, 0)[yy - 4]
        d.line((x + 1, yy, x + COIL_W - 2, yy), fill=COPPER[shade])
    for xx in range(x + 2, x + COIL_W - 2, 2):
        d.line((xx, 4, xx, 7), fill=COPPER[0])
    d.point((x + 1, 4), fill=COPPER[3])


def body_layer():
    img = Image.new("RGBA", (W, H), CLEAR)
    d = ImageDraw.Draw(img)

    # ---- the exhaust-pipe barrel, rows 14..24, with the flared tip at the muzzle
    tube(d, 3, 14, 141, 24, RUST)
    d.rectangle((2, 12, 9, 26), fill=OUT)
    d.rectangle((3, 13, 8, 25), fill=RUST[1])
    d.rectangle((3, 13, 8, 14), fill=RUST[3])
    d.rectangle((3, 24, 8, 25), fill=RUST[0])
    d.rectangle((3, 16, 6, 22), fill=(10, 10, 14, 255))
    d.rectangle((4, 18, 5, 20), fill=DIM)
    # dents and a patched hole
    d.rectangle((88, 17, 94, 21), fill=OUT)
    d.rectangle((89, 18, 93, 20), fill=STEEL[1])
    d.point((90, 19), fill=STEEL[3])
    d.point((92, 19), fill=STEEL[0])
    # hose clamps round it, each with a screw nub
    for x in (36, 76, 118):
        d.rectangle((x, 12, x + 3, 26), fill=OUT)
        d.rectangle((x + 1, 13, x + 2, 25), fill=STEEL[2])
        d.line((x + 1, 13, x + 1, 25), fill=STEEL[3])
        d.rectangle((x + 1, 10, x + 2, 11), fill=OUT)
        d.point((x + 1, 11), fill=STEEL[3])

    # ---- the rack the coils stand on: a welded plate over the pipe
    rect(d, 16, 9, 142, 13, STEEL[1])
    d.line((17, 10, 141, 10), fill=STEEL[2])
    d.line((17, 12, 141, 12), fill=STEEL[0])
    for x in COIL_X:
        d.rectangle((x - 1, 8, x + COIL_W, 9), fill=OUT)
        d.line((x, 9, x + COIL_W - 1, 9), fill=STEEL[2])
    for x in range(20, 140, 18):
        weld(d, x, 11, 3)
    for x in (18, 140):
        d.rectangle((x, 13, x + 1, 14), fill=OUT)
    # the coils, with a wire from plug to plug
    for x in COIL_X:
        coil(d, x)

    # ---- a brace welded under the pipe, and the car battery hung from it
    rect(d, 18, 25, 132, 27, STEEL[1])
    d.line((19, 25, 131, 25), fill=STEEL[2])
    rect(d, 62, 27, 106, 38, BATT[1])
    d.rectangle((63, 28, 105, 29), fill=BATT[2])
    d.rectangle((63, 36, 105, 37), fill=BATT[0])
    d.rectangle((70, 30, 86, 35), fill=OUT)
    d.rectangle((71, 31, 85, 34), fill=(214, 206, 170, 255))
    d.line((72, 32, 80, 32), fill=(60, 60, 64, 255))
    d.line((72, 33, 84, 33), fill=(120, 40, 36, 255))
    d.rectangle((92, 30, 100, 35), fill=OUT)
    d.rectangle((93, 31, 99, 34), fill=YELLOW)
    d.line((93, 32, 99, 32), fill=BLACK)
    # the terminals on top (red plus, black minus) and the straps holding it up
    d.rectangle((66, 25, 70, 28), fill=OUT)
    d.rectangle((67, 25, 69, 27), fill=RED[2])
    d.rectangle((98, 25, 102, 28), fill=OUT)
    d.rectangle((99, 25, 101, 27), fill=BLACK)
    d.point((100, 26), fill=STEEL[2])
    for x in (74, 90):
        d.rectangle((x, 26, x + 2, 38), fill=OUT)
        d.rectangle((x + 1, 27, x + 1, 37), fill=RUST[2])
    # jumper cables: the red one hangs loose with its clamp, the black one climbs to the rack and bites
    rod(d, [(68, 28), (60, 29), (53, 30), (48, 31), (50, 33)], RED[1], 2)
    d.rectangle((47, 33, 54, 37), fill=OUT)
    d.rectangle((48, 34, 53, 36), fill=RED[2])
    d.line((48, 34, 53, 34), fill=(250, 200, 190, 255))
    d.point((54, 35), fill=STEEL[3])
    rod(d, [(100, 28), (110, 30), (124, 28), (134, 22), (136, 15)], BLACK, 2)
    d.rectangle((133, 8, 139, 14), fill=OUT)
    d.rectangle((134, 9, 138, 13), fill=BLACK)
    d.line((134, 9, 138, 9), fill=STEEL[2])
    d.point((136, 12), fill=STEEL[3])

    # ---- the receiver: welded plates, a hazard band, a painted bolt
    rect(d, 142, 3, 196, 33, STEEL[1])
    d.rectangle((143, 4, 195, 7), fill=STEEL[2])
    d.rectangle((143, 8, 195, 9), fill=STEEL[1])
    d.rectangle((143, 30, 195, 32), fill=STEEL[0])
    d.rectangle((144, 0, 186, 2), fill=OUT)
    d.rectangle((145, 1, 185, 2), fill=RUST[1])
    for x in range(146, 196, 8):
        d.point((x, 5), fill=STEEL[3])
        d.point((x, 31), fill=STEEL[3])
    for i in range(12):
        x = 146 + i * 4
        d.polygon([(x, 25), (x + 2, 25), (x + 4, 29), (x + 2, 29)], fill=YELLOW if i % 2 == 0 else BLACK)
    d.rectangle((146, 24, 194, 24), fill=OUT)
    d.rectangle((146, 30, 194, 30), fill=OUT)
    d.polygon([(166, 10), (174, 10), (170, 16), (177, 16), (164, 25 - 2), (167, 18), (161, 18)], fill=OUT)
    d.polygon([(166, 11), (173, 11), (169, 17), (175, 17), (165, 22), (167, 19), (162, 19)], fill=(236, 226, 150, 255))
    d.line((167, 12, 171, 12), fill=(255, 255, 230, 255))
    d.rectangle((150, 12, 156, 18), fill=OUT)
    d.rectangle((151, 13, 155, 17), fill=(12, 12, 16, 255))
    # the ammo lamp: a caged bulb the plugin lights green (a ball is chambered) or red (it is not)
    d.rectangle((LAMP_X - 4, LAMP_Y - 4, LAMP_X + 4, LAMP_Y + 4), fill=OUT)
    d.rectangle((LAMP_X - 3, LAMP_Y - 3, LAMP_X + 3, LAMP_Y + 3), fill=(26, 26, 30, 255))
    bulb(d, LAMP_X, LAMP_Y, (46, 50, 58, 255))
    for (dx, dy) in ((-4, -4), (4, -4), (-4, 4), (4, 4)):
        d.point((LAMP_X + dx, LAMP_Y + dy), fill=STEEL[2])
    weld(d, 146, 33, 4)
    weld(d, 192, 20, 3, vertical=True)

    # ---- the trigger guard, a bent rebar, and the trigger
    rod(d, [(150, 34), (150, 40), (166, 41), (166, 34)], STEEL[2], 2)
    d.rectangle((158, 34, 159, 38), fill=OUT)

    # ---- the grip, wrapped in rags
    d.polygon([(168, 33), (186, 33), (190, 47), (173, 47)], fill=OUT)
    d.polygon([(169, 34), (185, 34), (188, 46), (174, 46)], fill=RAG[1])
    for y in range(35, 46, 3):
        d.line((169 + (y - 34) // 3, y, 186 + (y - 34) // 3, y + 1), fill=RAG[3 if (y // 3) % 2 else 0])
    d.line((169, 34, 185, 34), fill=RAG[2])
    d.point((176, 40), fill=RUST[2])

    # ---- the bent-pipe stock with a rag-wrapped cheek rest and a steel butt plate
    rod(d, [(197, 9), (226, 9), (250, 12)], RUST[2], 3)
    rod(d, [(197, 22), (222, 24), (247, 24)], RUST[1], 3)
    rod(d, [(212, 11), (236, 22)], STEEL[1], 2)
    d.rectangle((250, 6, 256, 27), fill=OUT)
    d.rectangle((251, 7, 255, 26), fill=STEEL[0])
    d.line((252, 8, 252, 25), fill=STEEL[2])
    d.rectangle((214, 3, 240, 10), fill=OUT)
    d.rectangle((215, 4, 239, 9), fill=RAG[1])
    for x in range(216, 240, 4):
        d.line((x, 4, x + 2, 9), fill=RAG[3])
        d.line((x + 2, 4, x + 4, 9), fill=RAG[0])
    weld(d, 200, 7, 3)
    weld(d, 200, 19, 3)

    # ---- wear
    speckle(img, (4, 15, 140, 23), RUST[0], 40)
    speckle(img, (4, 15, 140, 23), RUST[3], 26)
    speckle(img, (143, 4, 197, 30), RUST[2], 30, only=(STEEL[1], STEEL[2], STEEL[0]))
    speckle(img, (143, 4, 197, 30), STEEL[3], 14)
    speckle(img, (17, 10, 141, 12), RUST[1], 10, only=(STEEL[1], STEEL[2], STEEL[0]))
    return img


def coil_layer(i):
    img = Image.new("RGBA", (W, H), CLEAR)
    d = ImageDraw.Draw(img)
    coil(d, COIL_X[i], lit=True)
    if i == 0:
        # the muzzle's bore wakes up with the first coil
        d.rectangle((4, 17, 5, 21), fill=GLOW)
        d.point((4, 19), fill=GLOW_CORE)
    return img


def lamp_layer(green):
    """The lamp lit, on a clear canvas: green when a ball is chambered, red when not (with a soft halo)."""
    img = Image.new("RGBA", (W, H), CLEAR)
    d = ImageDraw.Draw(img)
    body = (70, 232, 112, 255) if green else (236, 52, 44, 255)
    core = (196, 255, 208, 255) if green else (255, 176, 154, 255)
    halo = (90, 255, 140, 90) if green else (255, 80, 60, 90)
    for (dx, dy) in ((-3, -1), (-3, 0), (-3, 1), (3, -1), (3, 0), (3, 1), (-1, -3), (0, -3), (1, -3), (-1, 3), (0, 3), (1, 3)):
        d.point((LAMP_X + dx, LAMP_Y + dy), fill=halo)
    bulb(d, LAMP_X, LAMP_Y, body)
    d.point((LAMP_X - 1, LAMP_Y - 1), fill=core)
    d.point((LAMP_X, LAMP_Y - 1), fill=core)
    return img


def pump():
    """The air pump, 33x15: ribbed sleeve (rows 6..14) with the gauge on top, a rod and a T-handle at the rear (right)."""
    img = Image.new("RGBA", (33, 15), CLEAR)
    d = ImageDraw.Draw(img)
    tube(d, 0, 6, 21, 14, STEEL)
    for x in range(5, 18):
        if x % 3 == 2:
            d.line((x, 7, x, 13), fill=STEEL[0])
    for x in (2, 19):
        d.rectangle((x, 6, x + 1, 14), fill=OUT)
        d.line((x, 7, x, 13), fill=BRASS[2])
    # the gauge: a brass ring, a pale face with ticks and a red zone; the needle comes from the plugin
    d.ellipse((5, 0, 16, 9), fill=OUT)
    d.ellipse((6, 1, 15, 8), fill=BRASS[2])
    d.ellipse((7, 2, 14, 7), fill=(226, 220, 196, 255))
    for (x, y) in ((8, 6), (7, 4), (8, 3), (10, 2), (13, 3), (14, 4), (13, 6)):
        d.point((x, y), fill=(60, 56, 52, 255))
    d.point((13, 3), fill=RED[2])
    d.point((14, 4), fill=RED[2])
    d.point((13, 6), fill=RED[2])
    d.point((6, 2), fill=BRASS[3])
    # the rod and the T-handle
    d.rectangle((22, 9, 27, 11), fill=OUT)
    d.line((22, 10, 27, 10), fill=STEEL[2])
    d.rectangle((28, 3, 32, 14), fill=OUT)
    d.rectangle((29, 4, 31, 13), fill=(52, 48, 54, 255))
    d.line((29, 4, 29, 13), fill=(98, 92, 98, 255))
    for y in range(5, 13, 3):
        d.line((29, y, 31, y), fill=BLACK)
    speckle(img, (1, 7, 20, 13), RUST[2], 8, only=(STEEL[1], STEEL[2]))
    return img


def ball():
    img = Image.new("RGBA", (12, 24), CLEAR)
    d = ImageDraw.Draw(img)
    cx, cy, r = 6, 12, 5
    d.ellipse((cx - r - 1, cy - r - 1, cx + r, cy + r), fill=OUT)
    d.ellipse((cx - r, cy - r, cx + r - 1, cy + r - 1), fill=STEEL[1])
    d.ellipse((cx - r + 1, cy - r + 1, cx + r - 3, cy + r - 3), fill=STEEL[2])
    d.rectangle((cx - 3, cy - 4, cx - 2, cy - 3), fill=STEEL[3])
    d.point((cx - 3, cy - 4), fill=(255, 255, 255, 255))
    d.rectangle((cx + 1, cy + 2, cx + 3, cy + 4), fill=STEEL[0])
    d.point((cx + 2, cy + 3), fill=(96, 70, 30, 255))
    d.point((cx, cy + 4), fill=(96, 70, 30, 255))
    return img


def icon():
    """45x11, the size of the slug shotgun's card picture (Slice_7_0), the same parts in the same order along the gun: flared
    exhaust pipe, six coils on the rack, battery under the pipe, hazard receiver, rag grip, bent-pipe stock."""
    img = Image.new("RGBA", (45, 11), CLEAR)
    d = ImageDraw.Draw(img)
    # coils on the rack above the pipe
    d.rectangle((5, 3, 25, 3), fill=STEEL[1])
    for x in (6, 9, 12, 15, 18, 21):
        d.line((x, 1, x, 2), fill=COPPER[2])
        d.point((x, 0), fill=CERAMIC[2])
    # the exhaust pipe with a flared mouth, and two clamps
    d.rectangle((3, 4, 26, 6), fill=RUST[2])
    d.line((3, 4, 26, 4), fill=RUST[3])
    d.line((3, 6, 26, 6), fill=RUST[0])
    d.rectangle((1, 3, 3, 7), fill=RUST[1])
    d.point((2, 5), fill=GLOW)
    for x in (10, 20):
        d.line((x, 3, x, 7), fill=STEEL[2])
    # battery under it, with red and black terminals
    d.rectangle((11, 7, 18, 9), fill=BATT[1])
    d.line((11, 7, 18, 7), fill=BATT[2])
    d.point((12, 6), fill=RED[2])
    d.point((17, 6), fill=BLACK)
    d.point((5, 8), fill=RED[1])
    d.point((6, 9), fill=RED[1])
    # receiver with a hazard band
    d.rectangle((27, 2, 35, 8), fill=STEEL[1])
    d.line((27, 2, 35, 2), fill=STEEL[2])
    for i in range(4):
        d.point((28 + i * 2, 7), fill=YELLOW)
        d.point((29 + i * 2, 7), fill=BLACK)
    d.point((31, 4), fill=(236, 226, 150, 255))
    d.point((32, 5), fill=(236, 226, 150, 255))
    # grip wrapped in rags
    d.polygon([(30, 9), (33, 9), (34, 10), (31, 10)], fill=RAG[2])
    # the bent-pipe stock with a rag cheek rest and a plate
    d.line((36, 3, 43, 3), fill=RUST[2])
    d.line((36, 7, 41, 9), fill=RUST[1])
    d.line((38, 2, 41, 2), fill=RAG[2])
    d.line((43, 3, 43, 9), fill=STEEL[0])
    px = img.load()
    solid = [[px[x, y][3] > 0 for x in range(45)] for y in range(11)]
    for y in range(11):
        for x in range(45):
            if solid[y][x]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < 45 and 0 <= ny < 11 and solid[ny][nx]:
                    px[x, y] = OUT
                    break
    return img


def save(img, name):
    img.save(os.path.join(HERE, name))


body = body_layer()
pump_img = pump()
held = body.copy()
held.alpha_composite(pump_img, PUMP_REST)
coils = [coil_layer(i) for i in range(6)]
save(body, "railgun_body.png")
save(held, "railgun_held.png")
for i, c in enumerate(coils):
    save(c, "railgun_coil_%d.png" % i)
save(Image.new("RGBA", (W, H), CLEAR), "railgun_clear.png")
save(lamp_layer(True), "railgun_lamp_green.png")
save(lamp_layer(False), "railgun_lamp_red.png")
save(pump_img, "railgun_pump.png")
save(ball(), "railgun_ball.png")
ic = icon()
save(ic, "railgun_icon.png")

# a preview: the body with the pump in place, then fully lit, then the parts
sheet = Image.new("RGBA", (W * 3 + 20, H * 3 * 2 + 40 + 24 * 6), (70, 80, 90, 255))
b3 = held.resize((W * 3, H * 3), Image.NEAREST)
sheet.paste(b3, (10, 10), b3)
lit = held.copy()
for c in coils:
    lit.alpha_composite(c)
l3 = lit.resize((W * 3, H * 3), Image.NEAREST)
sheet.paste(l3, (10, H * 3 + 20), l3)
pump_big = pump_img.resize((33 * 6, 15 * 6), Image.NEAREST)
sheet.paste(pump_big, (10, H * 6 + 30), pump_big)
ball_big = ball().resize((12 * 6, 24 * 6), Image.NEAREST)
sheet.paste(ball_big, (260, H * 6 + 30), ball_big)
ic_big = ic.resize((45 * 6, 11 * 6), Image.NEAREST)
sheet.paste(ic_big, (360, H * 6 + 30), ic_big)
sheet.save(os.path.join(HERE, "railgun_preview.png"))
print("railgun art drawn: body/held 258x48, 6 coil layers, pump 33x15, ball 12x24, icon 45x11")
print("COIL_X", COIL_X, "COIL_W", COIL_W, "TIP_Y", TIP_Y, "PUMP_REST", PUMP_REST, "GAUGE", GAUGE, "LAMP", LAMP_X, LAMP_Y, "DIGIT", DIGIT_X, DIGIT_ROW)
