"""Draws dacia_model.txt (the file the plugin loads) with dacia_mask.png / dacia_base.png: the zones in accent / main / tertiary = lime / blue / white.

  python preview_dacia_model.py [views] [gaps] [cockpit]

  views    dacia_model_preview.png   four views
  gaps     dacia_gaps.png            back-facing surfaces in orange, glass and lights left out: orange showing through the bodywork is a hole
  cockpit  dacia_cockpit.png         the view from the driver's eye (the game's head camera: x -0.14, y 0.73, z -0.22)
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ATLAS_W, ATLAS_H = 2048, 1024
PAL = [np.array([60, 230, 90]), np.array([42, 111, 214]), np.array([235, 235, 235])]


def load_model(path=os.path.join(HERE, "dacia_model.txt")):
    meshes, cur = {}, None
    for line in open(path):
        p = line.split()
        if p[0] == "mesh":
            cur = {"v": [], "t": [], "space": p[2]}
            meshes[p[1]] = cur
        elif p[0] == "v":
            cur["v"].append([float(x) for x in p[1:9]])
        else:
            cur["t"].append([int(x) for x in p[1:4]])
    for m in meshes.values():
        m["v"], m["t"] = np.array(m["v"]), np.array(m["t"])
    return meshes


def rasterize(meshes, cam, size, colour_of, skip=(), near=None):
    """cam(P: Nx3) -> (sx, sy, depth) arrays; smaller depth is nearer."""
    W, H = size
    img = np.zeros((H, W, 3))
    img[:] = (58, 62, 72)
    zb = np.full((H, W), 1e9)
    for name, m in meshes.items():
        if name in skip:
            continue
        V, T = m["v"], m["t"]
        sx, sy, dp = cam(V[:, :3])
        for t in T:
            a, b, c = t
            if near is not None and min(dp[a], dp[b], dp[c]) < near:
                continue
            x0, x1 = int(max(0, min(sx[a], sx[b], sx[c]))), int(min(W - 1, max(sx[a], sx[b], sx[c])) + 1)
            y0, y1 = int(max(0, min(sy[a], sy[b], sy[c]))), int(min(H - 1, max(sy[a], sy[b], sy[c])) + 1)
            if x1 <= x0 or y1 <= y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
            d = (sy[b] - sy[c]) * (sx[a] - sx[c]) + (sx[c] - sx[b]) * (sy[a] - sy[c])
            if abs(d) < 1e-9:
                continue
            l0 = ((sy[b] - sy[c]) * (xs - sx[c]) + (sx[c] - sx[b]) * (ys - sy[c])) / d
            l1 = ((sy[c] - sy[a]) * (xs - sx[c]) + (sx[a] - sx[c]) * (ys - sy[c])) / d
            l2 = 1 - l0 - l1
            inside = (l0 >= -0.001) & (l1 >= -0.001) & (l2 >= -0.001)
            if not inside.any():
                continue
            depth = l0 * dp[a] + l1 * dp[b] + l2 * dp[c]
            upd = inside & (depth < zb[y0:y1, x0:x1])
            if not upd.any():
                continue
            col = colour_of(name, V, a, b, c, l0, l1, l2, cam)
            zb[y0:y1, x0:x1] = np.where(upd, depth, zb[y0:y1, x0:x1])
            reg = img[y0:y1, x0:x1]
            reg[upd] = col[upd]
    return img


def make_colour_fn(mask, base, gaps=False):
    light = np.array([0.35, 0.85, 0.4])
    light /= np.linalg.norm(light)

    def colour(name, V, a, b, c, l0, l1, l2, cam):
        n = l0[..., None] * V[a, 3:6] + l1[..., None] * V[b, 3:6] + l2[..., None] * V[c, 3:6]      # interpolated, as the GPU does
        n = n / np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)
        shade = (0.45 + 0.55 * np.maximum(0.0, n @ light))[..., None]
        shp = l0.shape + (3,)
        if name == "glass":
            return np.full(shp, (150, 195, 225)) * shade
        if name == "headlights":
            return np.full(shp, (255, 245, 200))
        if name == "taillights":
            return np.full(shp, (200, 30, 30))
        u = l0 * V[a, 6] + l1 * V[b, 6] + l2 * V[c, 6]
        v = l0 * V[a, 7] + l1 * V[b, 7] + l2 * V[c, 7]
        tx = np.clip((u * ATLAS_W).astype(int), 0, ATLAS_W - 1)
        ty = np.clip(((1 - v) * ATLAS_H).astype(int), 0, ATLAS_H - 1)
        mc = mask[ty, tx]
        zone = mc[..., 0:1] * PAL[0] + mc[..., 1:2] * PAL[1] + mc[..., 2:3] * PAL[2]
        return zone * base[ty, tx][..., None] * 1.15 * shade

    return colour


def ortho_cam(yaw, pitch, size, scale=170, centre=(0.0, 0.45, 0.0)):
    W, H = size
    ya, pa = math.radians(yaw), math.radians(pitch)

    def cam(P):
        x, y, z = P[:, 0] - centre[0], P[:, 1] - centre[1], P[:, 2] - centre[2]
        xr, zr = x * math.cos(ya) - z * math.sin(ya), x * math.sin(ya) + z * math.cos(ya)
        yr, zr2 = y * math.cos(pa) + zr * math.sin(pa), -y * math.sin(pa) + zr * math.cos(pa)
        return W / 2 + xr * scale, H / 2 + 30 - yr * scale, zr2
    cam.rot = lambda n: np.array([n[0] * math.cos(ya) - n[2] * math.sin(ya), 0, 0])
    cam.yaw, cam.pitch = ya, pa
    return cam


def main():
    what = set(sys.argv[1:]) or {"views"}
    meshes = load_model()
    mask = np.asarray(Image.open(os.path.join(HERE, "dacia_mask.png"))).astype(float) / 255
    base = np.asarray(Image.open(os.path.join(HERE, "dacia_base.png"))).astype(float) / 255
    colour = make_colour_fn(mask, base)
    size = (900, 520)

    def sheet(views, gaps, path):
        out = Image.new("RGB", (size[0] * 2, size[1] * 2))
        for i, (name, yaw, pitch) in enumerate(views):
            cam = ortho_cam(yaw, pitch, size)
            ya, pa = cam.yaw, cam.pitch

            def facing_away(n):
                # the normal's depth component after the same rotation: positive means turned away from the camera
                xr, zr = n[0] * math.cos(ya) - n[2] * math.sin(ya), n[0] * math.sin(ya) + n[2] * math.cos(ya)
                return -n[1] * math.sin(pa) + zr * math.cos(pa)

            def fn(name_, V, a, b, c, l0, l1, l2, cam_):
                if gaps and facing_away(V[a, 3:6]) > 0.02:
                    return np.full(l0.shape + (3,), (255, 140, 0))
                return colour(name_, V, a, b, c, l0, l1, l2, cam_)
            im = Image.fromarray(np.clip(rasterize(meshes, cam, size, fn, skip=("glass", "headlights", "taillights") if gaps else ()), 0, 255).astype(np.uint8))
            ImageDraw.Draw(im).text((8, 6), name, fill=(255, 255, 255))
            out.paste(im, ((i % 2) * size[0], (i // 2) * size[1]))
        out.save(path)
        print("wrote", path)

    if "views" in what:
        sheet([("rear 3/4", 35, 14), ("side", 90, 4), ("front 3/4", 215, 14), ("top", 20, 80)], False, os.path.join(HERE, "dacia_model_preview.png"))
    if "gaps" in what:
        sheet([("front-left", 215, 12), ("front-right", 145, 12), ("rear-left", 320, 12), ("rear-right", 40, 12)], True, os.path.join(HERE, "dacia_gaps.png"))
    if "closeup" in what:
        out = Image.new("RGB", (size[0] * 2, size[1] * 2))
        for i, (name, yaw, pitch, centre, scale) in enumerate([("face", 200, 8, (0, 0.28, 1.5), 420), ("face, straight", 180, 4, (0, 0.28, 1.5), 420),
                                                          ("tail", 340, 10, (0, 0.40, -1.6), 420), ("side, front half", 90, 3, (0, 0.40, 0.7), 330)]):
            cam = ortho_cam(yaw, pitch, size, scale=scale, centre=centre)
            im = Image.fromarray(np.clip(rasterize(meshes, cam, size, colour), 0, 255).astype(np.uint8))
            ImageDraw.Draw(im).text((8, 6), name, fill=(255, 255, 255))
            out.paste(im, ((i % 2) * size[0], (i // 2) * size[1]))
        out.save(os.path.join(HERE, "dacia_closeup.png"))
        print("wrote dacia_closeup.png")
    if "cockpit" in what:
        W, H = 1000, 520
        eye = np.array([-0.14, 0.73, -0.22])
        f = W / 2 / math.tan(math.radians(42))

        def cam(P):
            d = P - eye
            z = np.maximum(d[:, 2], 0.02)
            return W / 2 + f * d[:, 0] / z, H / 2 - f * d[:, 1] / z, d[:, 2]
        img = rasterize(meshes, cam, (W, H), lambda *a: colour(*a), skip=("glass", "headlights", "taillights"), near=0.08)
        img[(img == (58, 62, 72)).all(axis=2)] = (205, 220, 235)
        Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(os.path.join(HERE, "dacia_cockpit.png"))
        print("wrote dacia_cockpit.png")


if __name__ == "__main__":
    main()
