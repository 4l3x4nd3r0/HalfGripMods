"""Draws the Sniper Rifle's flat art as pixel art at the game's own pixel density (SCALE), so it sits with the rest of the cockpit.

The rifle body is a lit, textured side-on render of a low-poly TF2 Sniper Rifle model (licence unknown, so the model itself is not
in the repo; `sniper_render.png` is the render), shrunk to the game's pixel size and cut down to a small palette. Run once with the model to (re)make the render, after which only Pillow is needed:
  python -I make_sniper_sprites.py --obj "path\\to\\rifle final LP.obj" --textures "path\\to\\textures"
  python -I make_sniper_sprites.py                                      reads sniper_render.png

The textures folder holds the model's Material.00N_Base_Color.png files. Needs numpy as well as Pillow for the render.

  sniper_render.png   the raw render, cropped, no outline (kept so the model is not needed again), not shipped
  sniper_body.png     the rifle seen from the side, muzzle on the left (SniperGun.cs draws it at 14 * SCALE pixels per unit)
  sniper_bolt.png     bolt handle, 83x17 game pixels at SCALE
  sniper_round.png    a cartridge stood upright, 10x34 (the SMG magazine it replaces is 16x56)
  sniper_icon.png     31x18 game pixels inventory icon, at SCALE
  sniper_dot.png      the red laser dot
  sniper_clear_*.png  fully transparent stand-ins for the SMG's back, round and feed sprites, which a bolt-action has no use for
  sniper_preview.png  contact sheet, not shipped
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.dirname(os.path.abspath(__file__))
CLEAR = (0, 0, 0, 0)

# How many times denser than the game's own sprites the art is. SniperGun.Scale must match; 1 is the game's own density.
SCALE = 1
PALETTE = {"body": 18, "small": 14}
# The rifle body is drawn coarser than the small parts: the cockpit's pixels (wheel, shifter, dash) are about 1.75x the size of the SMG's.
BODY_WIDTH = 166
SUPERSAMPLE = 4
ROUND_SHRINK = 34 / 56
OUTLINE = (22, 20, 24, 255)

STEEL = (88, 94, 104, 255)
STEEL_LIGHT = (140, 148, 160, 255)
STEEL_DARK = (46, 50, 58, 255)
BRASS = (214, 168, 70, 255)
BRASS_LIGHT = (246, 212, 124, 255)
BRASS_DARK = (150, 108, 40, 255)
COPPER = (176, 98, 60, 255)
COPPER_LIGHT = (214, 140, 96, 255)
LEAD = (156, 156, 164, 255)


def parse_obj(path):
    verts, uvs, norms, tris = [], [], [], []
    material = None
    for line in open(path, encoding="utf-8", errors="ignore"):
        p = line.split()
        if not p:
            continue
        if p[0] == "v":
            verts.append([float(x) for x in p[1:4]])
        elif p[0] == "vt":
            uvs.append([float(x) for x in p[1:3]])
        elif p[0] == "vn":
            norms.append([float(x) for x in p[1:4]])
        elif p[0] == "usemtl":
            material = p[1]
        elif p[0] == "f":
            corners = []
            for token in p[1:]:
                parts = token.split("/")
                corners.append((int(parts[0]) - 1, int(parts[1]) - 1 if len(parts) > 1 and parts[1] else -1, int(parts[2]) - 1 if len(parts) > 2 and parts[2] else -1))
            for i in range(1, len(corners) - 1):
                tris.append((material, corners[0], corners[i], corners[i + 1]))
    return verts, uvs, norms, tris


def render_model(obj_path, texture_dir, width, view_from_front):
    """Orthographic side view with a z-buffer, textured and lit from above and in front. Returns an RGBA image `width` wide."""
    import numpy as np

    verts, uvs, norms, tris = parse_obj(obj_path)
    v = np.array(verts)
    uv = np.array(uvs)
    vn = np.array(norms)
    x0, x1, y0, y1 = v[:, 0].min(), v[:, 0].max(), v[:, 1].min(), v[:, 1].max()
    big = width * SUPERSAMPLE
    scale = big / (x1 - x0)
    height = int((y1 - y0) * scale) + 2
    zsign = 1.0 if view_from_front else -1.0

    textures = {}
    for name in {t[0] for t in tris}:
        path = os.path.join(texture_dir, name + "_Base_Color.png")
        textures[name] = np.array(Image.open(path).convert("RGB")) if os.path.exists(path) else None

    colour = np.zeros((height, big, 3), np.float32)
    depth = np.full((height, big), -1e9, np.float32)
    covered = np.zeros((height, big), bool)
    light = np.array([-0.35, 0.65, 0.7])
    light /= np.linalg.norm(light)

    for material, a, b, c in tris:
        pts = np.array([[(v[i][0] - x0) * scale, (y1 - v[i][1]) * scale, v[i][2] * zsign] for i, _, _ in (a, b, c)])
        minx, maxx = int(max(0, np.floor(pts[:, 0].min()))), int(min(big - 1, np.ceil(pts[:, 0].max())))
        miny, maxy = int(max(0, np.floor(pts[:, 1].min()))), int(min(height - 1, np.ceil(pts[:, 1].max())))
        if maxx < minx or maxy < miny:
            continue
        gx, gy = np.meshgrid(np.arange(minx, maxx + 1) + 0.5, np.arange(miny, maxy + 1) + 0.5)
        (ax, ay, az), (bx, by, bz), (cx, cy, cz) = pts
        det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(det) < 1e-9:
            continue
        w0 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / det
        w1 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / det
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
        if not inside.any():
            continue
        z = w0 * az + w1 * bz + w2 * cz
        region = depth[miny:maxy + 1, minx:maxx + 1]
        win = inside & (z > region)
        if not win.any():
            continue

        # Texture colour by the interpolated uv.
        tex = textures.get(material)
        if tex is not None and a[1] >= 0:
            uu = w0 * uv[a[1]][0] + w1 * uv[b[1]][0] + w2 * uv[c[1]][0]
            vv = w0 * uv[a[1]][1] + w1 * uv[b[1]][1] + w2 * uv[c[1]][1]
            th, tw = tex.shape[:2]
            tx = np.clip((uu % 1.0) * tw, 0, tw - 1).astype(int)
            ty = np.clip((1.0 - (vv % 1.0)) * th, 0, th - 1).astype(int)
            base = tex[ty, tx].astype(np.float32)
        else:
            base = np.full(win.shape + (3,), 128.0, np.float32)

        # Lighting by the interpolated normal, turned to face the viewer.
        if a[2] >= 0:
            n = w0[..., None] * vn[a[2]] + w1[..., None] * vn[b[2]] + w2[..., None] * vn[c[2]]
        else:
            e1 = np.array([bx - ax, by - ay, bz - az])
            e2 = np.array([cx - ax, cy - ay, cz - az])
            fn = np.cross(e1, e2)
            n = np.broadcast_to(fn, win.shape + (3,)).astype(np.float64)
            n = n * np.array([1, -1, 1])
        n = n * np.array([1.0, 1.0, zsign])
        length = np.linalg.norm(n, axis=-1, keepdims=True)
        n = n / np.maximum(length, 1e-9)
        facing = np.where(n[..., 2:3] < 0, -n, n)
        diffuse = np.clip((facing * light).sum(-1), 0, 1)
        shade = (0.42 + 0.78 * diffuse)[..., None]
        shaded = np.clip(base * shade, 0, 255)

        sub_colour = colour[miny:maxy + 1, minx:maxx + 1]
        sub_colour[win] = shaded[win]
        region[win] = z[win]
        covered[miny:maxy + 1, minx:maxx + 1][win] = True

    rgba = np.zeros((height, big, 4), np.uint8)
    rgba[..., :3] = colour.astype(np.uint8)
    rgba[..., 3] = np.where(covered, 255, 0)
    image = Image.fromarray(rgba, "RGBA")
    image = crop_sight_wire(image)
    # Premultiplied so the transparent edge does not bleed black into the colours when it is shrunk.
    small = image.convert("RGBa").resize((width, round(image.height / SUPERSAMPLE)), Image.BOX).convert("RGBA")
    small.putalpha(small.getchannel("A").point(lambda a: 255 if a > 110 else 0))
    return small


def crop_sight_wire(image):
    """The thin wire leaning over the scope's rear is part of the model's sight; cut everything above the scope's top."""
    w, h = image.size
    alpha = image.getchannel("A")
    top = 0
    for y in range(h):
        row = alpha.crop((int(w * 0.40), y, int(w * 0.78), y + 1)).tobytes()
        if sum(1 for px in row if px > 0) > w * 0.15:
            top = y
            break
    return image.crop((0, top, w, h))


def pixelate(image, colours):
    """Hard-edged pixel art from a smooth image: no half-transparent pixels, and the colours cut down to a small palette."""
    alpha = image.getchannel("A").point(lambda a: 255 if a >= 128 else 0)
    rgb = image.convert("RGB").quantize(colors=colours, method=Image.MEDIANCUT, dither=Image.NONE).convert("RGB")
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out


def finish_body(render):
    """Punches the colours up a little and puts a dark outline round the shape, so it reads against the dashboard."""
    from PIL import ImageEnhance

    body = ImageEnhance.Contrast(render.convert("RGB").filter(ImageFilter.MedianFilter(3))).enhance(1.3)
    body = ImageEnhance.Color(body).enhance(1.15).convert("RGBA")
    alpha = render.getchannel("A")
    body.putalpha(alpha)
    body = pixelate(body, PALETTE["body"])
    grown = alpha.filter(ImageFilter.MaxFilter(3))
    canvas = Image.new("RGBA", (render.width + 2, render.height + 2), CLEAR)
    outline = Image.new("RGBA", canvas.size, OUTLINE)
    mask = Image.new("L", canvas.size, 0)
    mask.paste(grown, (1, 1))
    canvas.paste(outline, (0, 0), mask)
    canvas.alpha_composite(body, (1, 1))
    return canvas


def locate(body):
    """Muzzle row, and the two places the sprites are lined up on, in this art's pixels: the breech (a cartridge is dropped onto the
    receiver here) and the bolt (the middle of the bolt handle's art sits here). Both are over the receiver, which the render puts
    about 46% of the way down at 57-61% of the way along."""
    w, h = body.size
    mask = body.getchannel("A").load()
    rows = [y for y in range(h) if mask[2, y] > 0]
    muzzle_row = (min(rows) + max(rows) + 1) / 2
    breech = (round(w * 0.61), round(h * 0.46))
    bolt = (round(w * 0.57), round(h * 0.45))
    return muzzle_row, breech, bolt


def hi_res(size, draw_fn):
    """Draws at 4x the final size (so shapes can be given in game pixels times U) and shrinks to `size` x SCALE with smoothing."""
    u = 4 * SCALE
    canvas = Image.new("RGBA", (size[0] * u, size[1] * u), CLEAR)
    draw_fn(ImageDraw.Draw(canvas), u)
    return pixelate(canvas.convert("RGBa").resize((size[0] * SCALE, size[1] * SCALE), Image.LANCZOS).convert("RGBA"), PALETTE["small"])


def draw_bolt():
    """83x17: a rod with a base and an angled arm ending in a ball knob, lying along the slide's travel."""
    def paint(d, u):
        d.rectangle((2 * u, 11 * u, 60 * u, 14.2 * u), fill=STEEL, outline=OUTLINE, width=int(0.5 * u))
        d.rectangle((2.5 * u, 11.5 * u, 59.5 * u, 12.4 * u), fill=STEEL_LIGHT)
        d.rectangle((2.5 * u, 13.4 * u, 59.5 * u, 13.9 * u), fill=STEEL_DARK)
        d.rounded_rectangle((59 * u, 8.6 * u, 67 * u, 15.6 * u), radius=1.2 * u, fill=STEEL_DARK, outline=OUTLINE, width=int(0.5 * u))
        d.line((66 * u, 12 * u, 74 * u, 5 * u), fill=OUTLINE, width=int(3.2 * u))
        d.line((66 * u, 12 * u, 74 * u, 5 * u), fill=STEEL, width=int(2.0 * u))
        d.line((66 * u, 11.4 * u, 74 * u, 4.4 * u), fill=STEEL_LIGHT, width=int(0.7 * u))
        d.ellipse((71.6 * u, 0.8 * u, 80.4 * u, 9.6 * u), fill=STEEL, outline=OUTLINE, width=int(0.6 * u))
        d.ellipse((73 * u, 2 * u, 76.5 * u, 5.5 * u), fill=STEEL_LIGHT)
    return hi_res((83, 17), paint)


def draw_round():
    """A cartridge stood on its base, tip up: pointed copper bullet, necked brass case, rim and primer. Drawn in 16x56 game pixels and
    shrunk to 10x34, the same shape as the SMG magazine it stands in for but a smaller thing on the rack."""
    def paint(d, u):
        u = u * ROUND_SHRINK
        cx = 8 * u
        # Bullet: an ogive that tapers to a lead tip.
        d.polygon([(cx, 0.5 * u), (cx + 2.2 * u, 5 * u), (cx + 3.2 * u, 13 * u), (cx - 3.2 * u, 13 * u), (cx - 2.2 * u, 5 * u)],
                  fill=COPPER, outline=OUTLINE)
        d.polygon([(cx, 0.5 * u), (cx + 1.2 * u, 3 * u), (cx - 1.2 * u, 3 * u)], fill=LEAD)
        d.polygon([(cx - 3 * u, 12 * u), (cx - 1.6 * u, 12 * u), (cx - 1.8 * u, 5 * u), (cx - 2.1 * u, 5.5 * u)], fill=COPPER_LIGHT)
        # Case: a short shoulder up to the neck, then the body, the extractor groove and the rim.
        d.polygon([(cx - 3.2 * u, 13 * u), (cx + 3.2 * u, 13 * u), (cx + 4.4 * u, 19 * u), (cx - 4.4 * u, 19 * u)], fill=BRASS, outline=OUTLINE)
        d.rectangle((cx - 4.4 * u, 19 * u, cx + 4.4 * u, 51 * u), fill=BRASS, outline=OUTLINE, width=int(0.5 * u))
        d.rectangle((cx - 3.6 * u, 19.5 * u, cx - 2.2 * u, 50 * u), fill=BRASS_LIGHT)
        d.rectangle((cx + 2.6 * u, 19.5 * u, cx + 3.9 * u, 50 * u), fill=BRASS_DARK)
        d.rectangle((cx - 4.4 * u, 51 * u, cx + 4.4 * u, 52.2 * u), fill=BRASS_DARK, outline=OUTLINE, width=int(0.4 * u))
        d.rectangle((cx - 5.2 * u, 52.2 * u, cx + 5.2 * u, 55.4 * u), fill=BRASS, outline=OUTLINE, width=int(0.5 * u))
        d.ellipse((cx - 1.8 * u, 52.8 * u, cx + 1.8 * u, 55 * u), fill=(196, 196, 204, 255))
    return hi_res((10, 34), paint)


def draw_dot():
    im = Image.new("RGBA", (16, 16), CLEAR)
    px = im.load()
    for y in range(16):
        for x in range(16):
            r = ((x - 7.5) ** 2 + (y - 7.5) ** 2) ** 0.5
            if r <= 2.2:
                px[x, y] = (255, 70, 60, 255)
            elif r <= 3.4:
                px[x, y] = (255, 40, 40, 170)
            elif r <= 7.5:
                px[x, y] = (255, 0, 0, int(70 * (1 - (r - 3.4) / 4.1)))
    return im


def main():
    args = sys.argv[1:]
    render_path = os.path.join(OUT, "sniper_render.png")
    if "--obj" in args:
        obj = args[args.index("--obj") + 1]
        textures = args[args.index("--textures") + 1] if "--textures" in args else os.path.join(os.path.dirname(obj), "textures")
        render = render_model(obj, textures, BODY_WIDTH, "--back" not in args)
        render.save(render_path)
        print("rendered", render.size, "into sniper_render.png")
    elif os.path.exists(render_path):
        render = Image.open(render_path).convert("RGBA")
    else:
        raise SystemExit("no sniper_render.png: run once with --obj <rifle .obj> --textures <folder>")

    body = finish_body(render)
    body.save(os.path.join(OUT, "sniper_body.png"))
    muzzle_row, breech, bolt = locate(body)
    print("sniper_body.png %dx%d: muzzle row %.1f, breech col %d row %.1f, bolt col %d row %d (SniperGun.cs: BodyWidth/BodyHeight/MuzzleRow/BreechColumn/BreechRow/BoltColumn/BoltRow, pixels per unit %.2f)"
          % (body.width, body.height, muzzle_row, breech[0], breech[1], bolt[0], bolt[1], 14 * body.width / 290))

    bolt = draw_bolt()
    bolt.save(os.path.join(OUT, "sniper_bolt.png"))
    rnd = draw_round()
    rnd.save(os.path.join(OUT, "sniper_round.png"))
    draw_dot().save(os.path.join(OUT, "sniper_dot.png"))

    icon = Image.new("RGBA", (31 * SCALE, 18 * SCALE), CLEAR)
    small = pixelate(body.convert("RGBa").resize((31 * SCALE, max(1, round(body.height * 31 * SCALE / body.width))), Image.LANCZOS).convert("RGBA"), PALETTE["small"])
    icon.alpha_composite(small, (0, (icon.height - small.height) // 2))
    icon.save(os.path.join(OUT, "sniper_icon.png"))

    Image.new("RGBA", (80, 11), CLEAR).save(os.path.join(OUT, "sniper_clear_back.png"))
    Image.new("RGBA", (8, 6), CLEAR).save(os.path.join(OUT, "sniper_clear_round.png"))
    Image.new("RGBA", (13, 5), CLEAR).save(os.path.join(OUT, "sniper_clear_feed.png"))

    # Contact sheet (not shipped).
    sheet = Image.new("RGBA", (body.width + 40, body.height + 260), (58, 58, 68, 255))
    sheet.alpha_composite(body, (20, 10))
    x, y = 20, body.height + 30
    for im, mult in ((bolt, 1), (rnd, 1), (draw_dot(), 4), (icon, 2)):
        big = im.resize((im.width * mult, im.height * mult), Image.NEAREST)
        sheet.alpha_composite(big, (x, y))
        x += big.width + 24
    sheet.save(os.path.join(OUT, "sniper_preview.png"))


if __name__ == "__main__":
    main()
