"""Draws the card pictures: mods\\<Mod>\\icon.png (128x128) for every mod, the HalfGripTools picture, the BepInEx picture and the
installer's own picture and .ico.

The mod pictures are built from each mod's own pixel sprites, scaled up by a whole number so the pixels stay sharp. They are not the
sprites the game shows in its shop (those stay as they are); a mod points at its picture with "icon": "icon.png" in mod.json.
The hand with the wrench, the BepInEx plug and the installer badge are drawn here from scratch. Nothing from the game or from
BepInEx is used. Only Pillow is needed:
  python -I make_icons.py
"""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
MODS = os.path.join(os.path.dirname(HERE), "mods")
TOOLS_REPO = r"F:\Git\halfGripTools"
ASSETS = os.path.join(os.path.dirname(HERE), "installer", "assets")

SIZE = 128
BOX = 120


def load(mod, name):
    return Image.open(os.path.join(MODS, mod, "sprites", name)).convert("RGBA")


def scaled(im, scale):
    return im.resize((im.width * scale, im.height * scale), Image.NEAREST)


def m1911_assembled():
    """The pistol as it sits in the car: the body sprite has no slide, so lay the back strip and the slide on it."""
    canvas = Image.new("RGBA", (150, 100), (0, 0, 0, 0))
    canvas.alpha_composite(load("M1911", "m1911_body.png"), (0, 0))
    canvas.alpha_composite(load("M1911", "m1911_back.png"), (48, 9))
    canvas.alpha_composite(load("M1911", "m1911_slide.png"), (62, 3))
    return canvas.crop(canvas.getbbox())


def multiplayer_icon():
    """Three Dacias in different colours, one behind the other."""
    car = load("DaciaBody", "dacia_icon.png")
    out = Image.new("RGBA", (46, 30), (0, 0, 0, 0))
    for i, order in enumerate(((0, 1, 2), (2, 0, 1), (1, 2, 0))):
        r, g, b, a = car.split()
        bands = (r, g, b)
        tinted = Image.merge("RGBA", (bands[order[0]], bands[order[1]], bands[order[2]], a))
        out.alpha_composite(tinted, (i * 7, (2 - i) * 5))
    return out


def playground_icon():
    im = Image.new("RGBA", (32, 18), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    hz = 4
    d.line([(0, hz), (31, hz)], fill=(120, 210, 190, 255))
    for k in range(-6, 7):
        d.line([(16 + k * 2, hz), (16 + k * 6, 17)], fill=(60, 140, 130, 255))
    for y in (6, 9, 13, 17):
        d.line([(0, y), (31, y)], fill=(60, 140, 130, 255))
    return im


def charms():
    names = ("glove_icon.png", "chute_icon.png", "rabbitfoot_icon.png")
    pics = [load("SpeedCharms", n) for n in names]
    out = Image.new("RGBA", (sum(p.width for p in pics) + 2, max(p.height for p in pics)), (0, 0, 0, 0))
    x = 0
    for p in pics:
        out.alpha_composite(p, (x, 0))
        x += p.width + 1
    return out


def fit(img):
    """The biggest whole-number scale that fits the box, centred on a transparent square."""
    img = img.crop(img.getbbox())
    scale = max(1, min(BOX // img.width, BOX // img.height))
    big = scaled(img, scale)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.alpha_composite(big, ((SIZE - big.width) // 2, (SIZE - big.height) // 2))
    return out


def outlined(img, colour):
    """A one pixel outline round whatever is not transparent."""
    alpha = img.getchannel("A")
    out = Image.new("RGBA", (img.width + 2, img.height + 2), (0, 0, 0, 0))
    px = out.load()
    src = alpha.load()
    for y in range(img.height):
        for x in range(img.width):
            if src[x, y] > 0:
                for dx, dy in ((0, 1), (1, 0), (2, 1), (1, 2)):
                    if px[x + dx, y + dy][3] == 0:
                        px[x + dx, y + dy] = colour
    out.alpha_composite(img, (1, 1))
    return out


def rect(d, x0, y0, x1, y1, colour):
    d.rectangle([x0, y0, x1, y1], fill=colour)


def wrench_flat():
    """An adjustable wrench lying flat, handle on the left, jaw opening to the right, 72x22."""
    im = Image.new("RGBA", (72, 22), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    light, mid, dark = (216, 224, 238, 255), (166, 176, 198, 255), (98, 108, 130, 255)
    d.ellipse([2, 4, 15, 17], fill=mid)
    d.rectangle([9, 7, 54, 14], fill=mid)
    d.ellipse([6, 8, 11, 13], fill=(0, 0, 0, 0))
    d.rectangle([13, 7, 54, 8], fill=light)
    d.rectangle([13, 14, 54, 14], fill=dark)
    # the head: two jaws and the screw wheel between them
    d.rectangle([50, 1, 68, 20], fill=mid)
    d.rectangle([50, 1, 68, 3], fill=light)
    d.rectangle([50, 19, 68, 20], fill=dark)
    d.rectangle([60, 7, 69, 13], fill=(0, 0, 0, 0))
    d.rectangle([60, 6, 69, 6], fill=dark)
    d.rectangle([60, 14, 69, 14], fill=light)
    d.rectangle([53, 7, 59, 14], fill=dark)
    for x in (54, 56, 58):
        d.line([(x, 8), (x, 13)], fill=light)
    for x, y in ((50, 1), (68, 1), (50, 20), (68, 20)):
        d.point((x, y), fill=(0, 0, 0, 0))
    return im


def badge_hand():
    """The Half Grip glove, fist raised, holding an adjustable wrench across a round red badge, 64x64."""
    out = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)
    d.ellipse([1, 1, 62, 62], fill=(20, 20, 26, 255))
    d.ellipse([3, 3, 60, 60], fill=(226, 228, 236, 255))
    d.ellipse([5, 5, 58, 58], fill=(209, 41, 41, 255))
    # a darker red crescent low on the right gives the badge some depth
    d.pieslice([5, 5, 58, 58], 20, 110, fill=(160, 30, 32, 255))
    d.ellipse([9, 9, 54, 48], fill=(209, 41, 41, 255))
    d.ellipse([5, 5, 58, 58], outline=(226, 228, 236, 255))

    # the wrench, rotated so its head is up on the right
    wrench = outlined(wrench_flat().rotate(38, expand=True, resample=Image.NEAREST), (12, 12, 16, 255))
    out.alpha_composite(wrench, (28 - wrench.width // 2, 32 - wrench.height // 2))

    # the glove: forearm, back of the hand, four checkered fingers over the handle, thumb, cuff
    arm = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    a = ImageDraw.Draw(arm)
    dark, mid, grey, pale, cuff = (46, 48, 62, 255), (30, 32, 42, 255), (138, 142, 158, 255), (206, 208, 220, 255), (226, 228, 236, 255)
    a.rectangle([22, 44, 35, 63], fill=dark)
    a.rectangle([20, 33, 36, 45], fill=dark)
    for i in range(4):
        x = 20 + i * 4
        for y in range(24, 33):
            for dx in range(3):
                a.point((x + dx, y), fill=pale if (x + dx + y) % 2 == 0 else grey)
        a.rectangle([x + 3, 24, x + 3, 32], fill=mid)
    a.rectangle([20, 33, 36, 33], fill=mid)
    for x, y in ((20, 24), (35, 24)):
        a.point((x, y), fill=(0, 0, 0, 0))
    a.rectangle([34, 31, 42, 40], fill=dark)
    a.rectangle([35, 31, 42, 31], fill=pale)
    a.rectangle([42, 32, 42, 40], fill=mid)
    a.rectangle([34, 40, 42, 40], fill=mid)
    a.point((42, 31), fill=(0, 0, 0, 0))
    a.rectangle([21, 49, 36, 52], fill=cuff)
    a.rectangle([21, 50, 36, 50], fill=(209, 41, 41, 255))
    a.rectangle([21, 52, 36, 52], fill=(170, 172, 184, 255))
    glove = outlined(arm, (12, 12, 16, 255))
    out.alpha_composite(glove, (-1, -1))
    return out


def bepinex_plug():
    """A plug going into a cartridge, 32x32."""
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    slate, slate_dark, label = (88, 96, 124, 255), (58, 64, 86, 255), (214, 130, 60, 255)
    metal, metal_dark = (214, 218, 228, 255), (140, 144, 158, 255)
    # the cartridge, with its slot
    rect(d, 4, 15, 27, 29, slate)
    rect(d, 4, 28, 27, 29, slate_dark)
    rect(d, 27, 15, 27, 29, slate_dark)
    rect(d, 10, 15, 21, 17, (20, 22, 30, 255))
    rect(d, 8, 21, 23, 26, label)
    rect(d, 8, 26, 23, 26, (160, 92, 40, 255))
    rect(d, 10, 23, 16, 23, (255, 214, 150, 255))
    # the plug and its two pins, one pin already in the slot
    rect(d, 9, 3, 22, 10, (232, 232, 238, 255))
    rect(d, 9, 9, 22, 10, (180, 182, 196, 255))
    rect(d, 12, 11, 13, 17, metal)
    rect(d, 18, 11, 19, 15, metal)
    rect(d, 13, 11, 13, 17, metal_dark)
    rect(d, 19, 11, 19, 15, metal_dark)
    # the cable
    rect(d, 14, 0, 17, 3, (70, 72, 86, 255))
    return outlined(im, (12, 12, 16, 255))


def installer_picture():
    """The badge picture with a green download arrow in a dark square, bottom right."""
    base = badge_hand()
    d = ImageDraw.Draw(base)
    rect(d, 43, 43, 63, 63, (12, 12, 16, 255))
    rect(d, 44, 44, 62, 62, (31, 31, 38, 255))
    green = (64, 184, 89, 255)
    rect(d, 51, 47, 55, 55, green)
    rect(d, 48, 54, 58, 55, green)
    rect(d, 49, 56, 57, 56, green)
    rect(d, 50, 57, 56, 57, green)
    rect(d, 52, 58, 54, 58, green)
    rect(d, 46, 60, 60, 61, green)
    return base


def save_big(img, path, size=SIZE):
    scale = max(1, size // img.width)
    big = scaled(img, scale)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.alpha_composite(big, ((size - big.width) // 2, (size - big.height) // 2))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out.save(path, optimize=True)
    return out


def main():
    mods = {
        "M1911": m1911_assembled(),
        "RPG": load("RPG", "rpg_icon.png"),
        "SniperRifle": load("SniperRifle", "sniper_icon.png"),
        "Railgun": load("Railgun", "railgun_icon.png"),
        "Minigun": load("Minigun", "minigun_icon.png"),
        "SpeedCharms": charms(),
        "DaciaBody": load("DaciaBody", "dacia_icon.png"),
        "BombRace": load("BombRace", "bomb_icon.png"),
        "InfinitePlayground": playground_icon(),
        "Highway": load("Highway", "highway_icon.png"),
        "Multiplayer": multiplayer_icon(),
    }
    for name, pic in mods.items():
        path = os.path.join(MODS, name, "icon.png")
        fit(pic).save(path, optimize=True)
        print("wrote", path)

    hand = badge_hand()
    save_big(hand, os.path.join(TOOLS_REPO, "icon.png"))
    save_big(hand, os.path.join(ASSETS, "tools.png"))
    save_big(bepinex_plug(), os.path.join(ASSETS, "bepinex.png"))
    installer = save_big(installer_picture(), os.path.join(ASSETS, "installer.png"), 256)
    installer.save(
        os.path.join(ASSETS, "installer.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)], bitmap_format="bmp")
    print("wrote the tools, BepInEx and installer pictures")


if __name__ == "__main__":
    main()
