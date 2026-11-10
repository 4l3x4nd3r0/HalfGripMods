"""Builds the Judgment Day sprites (python -I make_jd_sprites.py, needs Pillow).

The artwork is imagine.wagon's, in art/credit: the gun body (168x32, with the trigger), the lever (36x20) and the card icon (58x18). This
script only puts them where the game wants them. It takes the place of the slug shotgun's sprites, so every file is the
size of the sprite it replaces (IMG_3632_0 / under / cover are 258x48, the pump IMG_3633_0 is 33x15):
  jd_body.png        258x48  the gun without the lever. Muzzle on the left
  jd_clear.png       258x48  empty, for the shotgun's "under" and "cover" layers
  jd_pump_clear.png   33x15  empty, for the game's pump sprite (the held model's pump part is switched off)
  jd_lever.png        WxH    the lever on its own, padded on its top left so the pin is inside the picture; the plugin turns it about the pin
  jd_lever_layer.png 258x48  the lever alone, in place: the held gun's lever mesh is extruded from this (the held body is jd_body.png)
  jd_icon.png         58x18  the shop card's icon
and jd_preview.png, enlarged, for looking at.

Numbers the plugin relies on (printed at the end; the constants in Lever.cs must match): where the lever sprite sits in the body, its
size, and its hinge. Keep clear of the rear-view mirror (columns 13..110 below row 41) and the leaderboard (columns 197.. below row 28)."""
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUTDIR = HERE               # redraw-art.ps1 copies jd_*.png into mods\JudgmentDay\sprites
CREDIT = os.path.join(HERE, "credit")
PLAYER_BODY = os.path.join(CREDIT, "jd_body_imagine_wagon.png")
PLAYER_LEVER = os.path.join(CREDIT, "jd_lever_imagine_wagon.png")
PLAYER_ICON = os.path.join(CREDIT, "jd_icon_imagine_wagon.png")

W, H = 258, 48
GUN_AT = (16, 2)            # where the gun's top left goes in the 258x48 body: puts the receiver where the game's loading area is
PIN = (145, 23)             # where the lever pivots in the body picture (column, row): the pin imagine.wagon marked under the receiver
LEVER_TOP_LEFT = (144, 22)  # where the lever picture sits in the body: its upper right edge lies exactly on the stock's underside here
LEVER_PAD = 4               # the pin is at the picture's top left corner, so the picture is padded to keep the pivot inside it

def save(img, name):
    img.save(os.path.join(OUTDIR, name))


for path in (PLAYER_BODY, PLAYER_LEVER, PLAYER_ICON):
    if not os.path.exists(path):
        raise SystemExit("Missing " + path + ": imagine.wagon's pictures belong in art/credit.")
os.makedirs(OUTDIR, exist_ok=True)

body = Image.new("RGBA", (W, H), (0, 0, 0, 0))
body.alpha_composite(Image.open(PLAYER_BODY).convert("RGBA"), GUN_AT)
raw = Image.open(PLAYER_LEVER).convert("RGBA")
lev = Image.new("RGBA", (raw.width + LEVER_PAD, raw.height + LEVER_PAD), (0, 0, 0, 0))
lev.alpha_composite(raw, (LEVER_PAD, LEVER_PAD))
lever_at = (LEVER_TOP_LEFT[0] - LEVER_PAD, LEVER_TOP_LEFT[1] - LEVER_PAD)
lever_hinge = (PIN[0] - lever_at[0], PIN[1] - lever_at[1])
icon = Image.open(PLAYER_ICON).convert("RGBA")

save(body, "jd_body.png")
save(Image.new("RGBA", (W, H), (0, 0, 0, 0)), "jd_clear.png")
save(Image.new("RGBA", (33, 15), (0, 0, 0, 0)), "jd_pump_clear.png")
save(lev, "jd_lever.png")
layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
layer.alpha_composite(lev, lever_at)
save(layer, "jd_lever_layer.png")
save(icon, "jd_icon.png")

held = body.copy()
held.alpha_composite(layer)
prev = Image.new("RGBA", (W * 4, H * 4 + icon.height * 8 + 8), (60, 70, 60, 255))
prev.alpha_composite(held.resize((W * 4, H * 4), Image.NEAREST), (0, 0))
prev.alpha_composite(icon.resize((icon.width * 8, icon.height * 8), Image.NEAREST), (0, H * 4 + 4))
prev.save(os.path.join(HERE, "jd_preview.png"))

print("gun bbox in the body", held.getchannel("A").getbbox())
print("LEVER_AT", lever_at, "LEVER_SIZE", lev.size, "LEVER_HINGE", lever_hinge,
      "-> hinge in the body at", (lever_at[0] + lever_hinge[0], lever_at[1] + lever_hinge[1]))
