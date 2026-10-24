# Draws sprites/highway_icon.png: a 20x20 round badge like the game's StadiumAsh map icon (the other maps use 12x12; map nodes show an
# icon at its native pixel size, so this one is as big as the biggest of them). Pale rim, green highway-sign fill, a road running into
# the distance with dashes down the middle and a bridge crossing it.
# Run:  python -I art/make_highway_icon.py (redraw-art.ps1 copies the result into mods\Highway\sprites)
import math, os
from PIL import Image

N = 20
img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
px = img.load()

RIM = (218, 218, 218, 255)
OUTLINE = (20, 20, 20, 255)
FILL = (24, 104, 64, 255)
SKY = (36, 132, 88, 255)
ROAD = (43, 43, 43, 255)
LINE = (218, 218, 218, 255)
DECK = (150, 150, 150, 255)

c = (N - 1) / 2.0
for y in range(N):
    for x in range(N):
        r = math.hypot(x - c, y - c)
        if r > 9.9:
            continue
        if r > 9.0:
            px[x, y] = OUTLINE
        elif r > 8.0:
            px[x, y] = RIM
        else:
            px[x, y] = SKY if y < 7 else FILL


def inside(x, y):
    return 0 <= x < N and 0 <= y < N and math.hypot(x - c, y - c) <= 8.0


# road running into the distance: narrow at the horizon (row 7), wider at the bottom, with a bridge across it
def half_at(y):
    return 0.7 + (y - 7) * 0.34

for y in range(7, 19):
    for x in range(N):
        if inside(x, y) and abs(x - c) <= half_at(y):
            px[x, y] = ROAD
for y in (8, 9, 15, 16):
    for x in (9, 10):
        if inside(x, y):
            px[x, y] = LINE
# the bridge: a pale deck with a dark shadow line under it
for x in range(N):
    if inside(x, 11):
        px[x, 11] = DECK
    if inside(x, 12):
        px[x, 12] = DECK
    if inside(x, 13):
        px[x, 13] = ROAD
for x in (6, 13):
    for y in (11, 12, 13):
        if inside(x, y):
            px[x, y] = ROAD

img.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "highway_icon.png"))
print("wrote highway_icon.png", img.size)

