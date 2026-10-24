"""Cuts the RPG's flat art out of the game's own launcher texture (uv_racheta, 287x109), read from the game folder.

The texture holds a side view of the launcher and, above it, the rocket. From that:
  rpg_body.png        the launcher (shown at a higher pixel density than the SMG, see RpgItem.cs)
  rpg_rocket.png      the rocket stood upright, in the 16x56 canvas of the SMG's magazine (used for the loaded and empty one)
  rpg_rocket_side.png the rocket lying down, warhead first (97x25), for the 3D models
  rpg_icon.png        31x18 inventory icon
  rpg_clear_*.png     fully transparent stand-ins for the SMG's slide, round and feed sprites, which the launcher has no use for
"""
import os

import UnityPy
from PIL import Image, ImageOps

ROOT = r"F:\SteamLibrary\steamapps\common\Half Grip Demo\HalfGrip_Data"
OUT = os.path.dirname(os.path.abspath(__file__))
CLEAR = (0, 0, 0, 0)

LAUNCHER_BOX = (14, 44, 272, 106)   # x0, y0, x1, y1 (exclusive) in the texture, measured from its opaque pixels
ROCKET_BOX = (177, 8, 274, 33)


def load_texture():
    for name in ("resources.assets", "sharedassets0.assets"):
        env = UnityPy.load(os.path.join(ROOT, name))
        for obj in env.objects:
            if obj.type.name == "Texture2D":
                tex = obj.read()
                if tex.m_Name == "uv_racheta":
                    return tex.image.convert("RGBA")
    raise SystemExit("uv_racheta texture not found in the game files")


def main():
    tex = load_texture()
    # The texture has the muzzle on the right; the game's guns all point left, so mirror it.
    launcher = ImageOps.mirror(tex.crop(LAUNCHER_BOX))
    rocket = tex.crop(ROCKET_BOX)

    launcher.save(os.path.join(OUT, "rpg_body.png"))

    # Rocket upright, warhead up, squeezed into the magazine canvas. The launcher is muzzle-loaded, so the only "empty"
    # magazine is a rocket sitting in the tube; both magazine sprites are the same whole rocket.
    upright = rocket.rotate(90, expand=True)
    full = upright.resize((16, 56), Image.LANCZOS)
    full.save(os.path.join(OUT, "rpg_rocket.png"))
    tube = full

    # The same rocket lying down for the 3D models (in the tube, and in flight): warhead on the left, which is the front.
    ImageOps.mirror(rocket).save(os.path.join(OUT, "rpg_rocket_side.png"))

    # Where the tube's mouth is in rpg_body.png: the left edge, on the row the barrel is centred on.
    opaque_rows = [y for y in range(launcher.height) if launcher.getpixel((0, y))[3] > 0]
    muzzle_row = (min(opaque_rows) + max(opaque_rows) + 1) / 2
    print("muzzle pixel: x=0 row=%.1f of %dx%d (MuzzleLoader.MuzzleRow)" % (muzzle_row, launcher.width, launcher.height))

    # Icon: the launcher is long and thin, so it fills the width and sits in the middle of the 31x18 slot.
    scale = 31 / launcher.width
    small = launcher.resize((31, max(1, round(launcher.height * scale))), Image.LANCZOS)
    icon = Image.new("RGBA", (31, 18), CLEAR)
    icon.alpha_composite(small, (0, (18 - small.height) // 2))
    icon.save(os.path.join(OUT, "rpg_icon.png"))

    Image.new("RGBA", (83, 17), CLEAR).save(os.path.join(OUT, "rpg_clear_slide.png"))
    Image.new("RGBA", (80, 11), CLEAR).save(os.path.join(OUT, "rpg_clear_back.png"))
    Image.new("RGBA", (8, 6), CLEAR).save(os.path.join(OUT, "rpg_clear_round.png"))
    Image.new("RGBA", (13, 5), CLEAR).save(os.path.join(OUT, "rpg_clear_feed.png"))

    # Preview (not shipped).
    bg = Image.new("RGBA", (launcher.width * 3 + 200, launcher.height * 3 + 40), (58, 58, 68, 255))
    bg.alpha_composite(launcher.resize((launcher.width * 3, launcher.height * 3), Image.NEAREST), (10, 10))
    x = launcher.width * 3 + 30
    for im in (full, tube):
        big = im.resize((im.width * 3, im.height * 3), Image.NEAREST)
        bg.alpha_composite(big, (x, 10))
        x += big.width + 20
    bg.save(os.path.join(OUT, "rpg_preview.png"))
    print("wrote rpg_body.png", launcher.size, "rpg_rocket.png rpg_icon.png", icon.size)


if __name__ == "__main__":
    main()
