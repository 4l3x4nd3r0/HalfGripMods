"""Makes the Dacia body's inventory icon from the game's own Scrap Body icon (chassis id aero_1), read from the game folder.

The icon keeps its silhouette and shading; its colours are replaced with the Dacia livery: blue upper half, lime green lower half.
  dacia_icon.png     same size as the Scrap Body icon
  dacia_preview.png  3x enlargement next to the original (not shipped)
If the sprite cannot be found, a small pixel-art sedan is drawn at 32x18 instead.
"""
import os

import UnityPy
from PIL import Image, ImageDraw
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

GAME = r"F:\SteamLibrary\steamapps\common\Half Grip Demo"
OUT = os.path.dirname(os.path.abspath(__file__))
BLUE = (42, 111, 214)
LIME = (60, 230, 90)


def stock_icon():
    env = UnityPy.load(os.path.join(GAME, "HalfGrip_Data"))
    gen = TypeTreeGenerator("6000.3.22f1")
    gen.load_local_game(GAME)
    env.typetree_generator = gen
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            d = obj.parse_as_dict()
        except Exception:
            continue
        if d.get("id") == "aero_1" and "rollingFriction" in d:
            ref = obj.assets_file.get_pptr(d["image"]) if hasattr(obj.assets_file, "get_pptr") else None
            sprite = ref.read() if ref else None
            if sprite is None:
                sprite = env.assets_file_lookup(d["image"]) if hasattr(env, "assets_file_lookup") else None
            return sprite.image.convert("RGBA") if sprite else None
    return None


def draw_fallback():
    im = Image.new("RGBA", (32, 18), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    dr.rectangle((3, 9, 29, 14), fill=LIME + (255,))
    dr.rectangle((3, 7, 29, 10), fill=BLUE + (255,))
    dr.polygon([(8, 7), (11, 3), (22, 3), (26, 7)], fill=BLUE + (255,))
    dr.rectangle((12, 4, 15, 6), fill=(190, 220, 255, 255))
    dr.rectangle((17, 4, 21, 6), fill=(190, 220, 255, 255))
    for x in (8, 24):
        dr.ellipse((x - 3, 12, x + 3, 17), fill=(30, 30, 30, 255))
        dr.ellipse((x - 1, 14, x + 1, 16), fill=(255, 255, 255, 255))
    return im


def recolour(src):
    out = src.copy()
    px = out.load()
    ys = [y for y in range(out.height) if any(px[x, y][3] > 0 for x in range(out.width))]
    top, bottom = min(ys), max(ys)
    split = top + (bottom - top) * 0.55
    for y in range(out.height):
        base = BLUE if y < split else LIME
        for x in range(out.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            lum = (0.3 * r + 0.59 * g + 0.11 * b) / 255
            k = 0.45 + 0.75 * lum
            px[x, y] = tuple(min(255, int(c * k)) for c in base) + (a,)
    return out


def main():
    try:
        src = stock_icon()
    except Exception as e:
        print("could not read the stock icon:", e)
        src = None
    if src is None:
        print("using the drawn fallback icon")
        icon = draw_fallback()
        src = icon
    else:
        icon = recolour(src)
    icon.save(os.path.join(OUT, "dacia_icon.png"))
    bg = Image.new("RGBA", (icon.width * 6 + 30, icon.height * 3 + 20), (58, 58, 68, 255))
    for i, im in enumerate((src, icon)):
        big = im.resize((im.width * 3, im.height * 3), Image.NEAREST)
        bg.alpha_composite(big, (10 + i * (big.width + 10), 10))
    bg.save(os.path.join(OUT, "dacia_preview.png"))
    print("wrote dacia_icon.png", icon.size)


if __name__ == "__main__":
    main()
