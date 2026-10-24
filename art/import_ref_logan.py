"""One-off tool: turns a reference Dacia Logan model (a glTF binary) into the Dacia body for Half Grip.

  python import_ref_logan.py <logan.glb>

Needs numpy, Pillow, trimesh and fast-simplification (for quadric decimation). The reference is not shipped; what ships is the
result: dacia_model.txt, dacia_mask.png and dacia_base.png, which the plugin loads (see make_dacia_model.py for the file format).

Fitting the reference to the game. The game's own cars are real cars scaled 0.83 in width and length and 0.64 in height, which is
also what makes its wheels (radius 0.208) and its driver's camera (y 0.73, z -0.22) fit them. So the same scale is used here,
anchored on the wheels: the game's front axle (z 1.01), rear axle (z -1.175), wheel centre height (y 0.096) and track (x 0.625).

Paint. The paint belongs to the player. The paint shader has three zones; the body faces are laid out on the shared atlas by
their orientation and painted there (make_dacia_model.Painter), the trim parts take a fixed zone:
  main (G): body     accent (R): bumpers, skirts, mirrors, chrome strip, shield, roof banner     tertiary (B): black plastics, numbers
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import make_dacia_model as M      # the atlas, the painter and the file writer

LIBS = os.environ.get("LOGAN_PYLIBS")
if LIBS:
    sys.path.insert(0, LIBS)
import trimesh
import fast_simplification
from scipy.spatial import cKDTree
import struct, json

# ---- the reference -----------------------------------------------------------------------------------------------------------
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def load_glb(path):
    """Parts of a .glb as {material name: (positions, triangles, normals)}, in metres, x right, y up, z forward (the root's rotation applied)."""
    b = open(path, "rb").read()
    off, chunks = 12, []
    while off < len(b):
        n, _ = struct.unpack("<I4s", b[off:off + 8])
        chunks.append(b[off + 8:off + 8 + n])
        off += 8 + n
    j, blob = json.loads(chunks[0]), chunks[1]

    def acc(i):
        a = j["accessors"][i]
        bv = j["bufferViews"][a["bufferView"]]
        dt, n = np.dtype(CT[a["componentType"]]), NC[a["type"]]
        o = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride") or dt.itemsize * n
        if stride == dt.itemsize * n:
            return np.frombuffer(blob, dtype=dt, count=a["count"] * n, offset=o).reshape(a["count"], n)
        return np.stack([np.frombuffer(blob, dtype=dt, count=n, offset=o + k * stride) for k in range(a["count"])])

    parts = {}
    for node in j["nodes"]:
        if "mesh" not in node:
            continue
        for p in j["meshes"][node["mesh"]]["primitives"]:
            name = j["materials"][p["material"]]["name"]
            pos = acc(p["attributes"]["POSITION"]).astype(np.float64)
            idx = acc(p["indices"]).reshape(-1, 3).astype(np.int64)
            nor = acc(p["attributes"]["NORMAL"]).astype(np.float64)
            pos = np.stack([pos[:, 0], -pos[:, 2], pos[:, 1]], 1)      # the root node turns the model upright
            nor = np.stack([nor[:, 0], -nor[:, 2], nor[:, 1]], 1)
            if name in parts:
                prev_pos, prev_idx, prev_nor = parts[name]
                parts[name] = (np.concatenate([prev_pos, pos]), np.concatenate([prev_idx, idx + len(prev_pos)]), np.concatenate([prev_nor, nor]))
            else:
                parts[name] = (pos, idx, nor)
    return parts


# ---- into the game's space --------------------------------------------------------------------------------------------------
REF_FRONT_AXLE, REF_REAR_AXLE, REF_WHEEL_Y = 1.311, -1.319, 0.318
SX = 0.625 / 0.75
SZ = (1.01 - -1.175) / (REF_FRONT_AXLE - REF_REAR_AXLE)
SY = 0.64


def to_game(p):
    return np.stack([p[:, 0] * SX, 0.096 + (p[:, 1] - REF_WHEEL_Y) * SY, 1.01 + (p[:, 2] - REF_FRONT_AXLE) * SZ], 1)


def game_normals(nor):
    """Normals through the anisotropic scale (inverse transpose)."""
    n = np.stack([nor[:, 0] / SX, nor[:, 1] / SY, nor[:, 2] / SZ], 1)
    return n / np.maximum(np.linalg.norm(n, axis=1), 1e-12)[:, None]


def decimate(pos, tris, nor, target):
    """Welds, reduces to about `target` triangles with quadric decimation, and gives every vertex the normal of the closest vertex of
    the original: the shading stays that of the full detail model."""
    gp = to_game(pos)
    m = trimesh.Trimesh(vertices=gp, faces=tris, process=True)
    m.remove_unreferenced_vertices()
    if len(m.faces) > target:
        v, f = fast_simplification.simplify(m.vertices.astype(np.float32), m.faces.astype(np.int32), target_reduction=1.0 - target / len(m.faces))
        m = trimesh.Trimesh(vertices=v, faces=f, process=True)
    v, f = np.asarray(m.vertices, float), np.asarray(m.faces, int)
    # the decimated surface's own vertex normals say which way is out; of the closest original vertices take the first that agrees
    # (a double-skinned door has an inner layer whose normals point the other way)
    own = np.zeros_like(v)
    fa, fb, fc = v[f[:, 0]], v[f[:, 1]], v[f[:, 2]]
    fnrm = np.cross(fb - fa, fc - fa)
    for k in range(3):
        np.add.at(own, f[:, k], fnrm)
    own /= np.maximum(np.linalg.norm(own, axis=1), 1e-12)[:, None]
    gn = game_normals(nor)
    _, nearest = cKDTree(gp).query(v, k=12)
    cand = gn[nearest]                                   # (V, 12, 3)
    agree = (cand * own[:, None, :]).sum(2)              # (V, 12)
    pick = np.argmax(agree > 0.5, axis=1)
    ok = (agree > 0.5).any(axis=1)
    vn = np.where(ok[:, None], cand[np.arange(len(v)), pick], own)
    return v, f, vn


def smooth_corner_normals(v, f, crease_deg=38.0):
    """One normal per triangle corner: the area weighted mean of the faces round that vertex that are within the crease angle."""
    a, b, c = v[f[:, 0]], v[f[:, 1]], v[f[:, 2]]
    fn = np.cross(b - a, c - a)
    area = np.linalg.norm(fn, axis=1)
    area[area == 0] = 1e-12
    fn = fn / area[:, None]
    inc = [[] for _ in range(len(v))]
    for t, tri in enumerate(f):
        for k in tri:
            inc[k].append(t)
    cos_c = np.cos(np.radians(crease_deg))
    out = np.zeros((len(f), 3, 3))
    for t, tri in enumerate(f):
        for k, vi in enumerate(tri):
            g = np.array(inc[vi])
            sel = g[(fn[g] @ fn[t]) > cos_c]
            n = (fn[sel] * area[sel][:, None]).sum(0)
            out[t, k] = n / (np.linalg.norm(n) or 1.0)
    return fn, out


# ---- which parts go where ---------------------------------------------------------------------------------------------------
# (material, kind, target triangles)    kind: proj = painted by the livery, others = a fixed paint zone.
# The body panels are doubled-skinned sheets that tear when decimated, so the shell, trim and floor are kept whole.
SHELL = [
    ("mat_caroserie.007", "proj", 1000000),
    ("black_accents.007", "DARK", 1000000),
    ("bottom.007", "DARK", 1000000),
    ("glass_indicator.007", "DARK", 300),
    ("front_frille.005", "DARK", 12),
    ("exhaust.007", "DARK", 200),
    ("black_exhaust.005", "DARK", 36),
    ("wing_chrome.007", "DARK", 5000),
    ("wing_carbon.007", "DARK", 192),
    ("mat_caroserie_chrome.007", "ACCENT", 136),
    ("mirrors_new.007", "ACCENT", 1000000),
    ("chromatic.005", "ACCENT", 300),
    ("dacia_logo.005", "ACCENT", 300),
    ("badges.001", "ACCENT", 220),
]
GLASS = [("Windows_ext.005", 366), ("Sunshade.005", 42)]      # the banner fills the band between the windscreen and the roof header
HEADLIGHTS = [("headlight.005", 220), ("mat_geam.005", 230), ("ceva_faruri.003", 18)]
TAILLIGHTS = [("taillights_off__trans_.007", 243), ("taillights_no_light.007", 195)]
MIRROR_X = 0.76     # body parts further out than this are the mirrors: accent paint


CABIN_CENTRE = np.array([0.0, 0.55, -0.45])
CABIN_BOX = ((-0.80, 0.80), (0.02, 1.00), (-1.60, 0.82))      # x, y, z of the passenger cabin in game units


def drop_inner_skins(v, f, vn):
    """The reference's doors, roof and pillars are double skinned: an outer skin and an inner one that faces the cabin. The inner skins
    are never seen from outside and, in the game's cockpit, fight with the stock interior, so they are removed."""
    c = (v[f[:, 0]] + v[f[:, 1]] + v[f[:, 2]]) / 3
    n = vn[f].mean(axis=1)
    inside = np.abs(c[:, 0]) < MIRROR_X - 0.015           # the mirrors stay: their back faces are what the driver sees
    for k, (lo, hi) in enumerate(CABIN_BOX):
        inside &= (c[:, k] > lo) & (c[:, k] < hi)
    toward = ((CABIN_CENTRE - c) * n).sum(1) > 0
    keep = ~(inside & toward)
    print("  inner skin faces removed: %d of %d" % ((~keep).sum(), len(f)))
    return f[keep]


def collect(parts, spec):
    """spec: [(material, kind, target)] -> list of (game-space vertices, faces, kind)"""
    out = []
    for name, kind, target in spec:
        if name not in parts:
            print("  (missing part %s)" % name)
            continue
        pos, tris, nor = parts[name]
        v, f, vn = decimate(pos, tris, nor, target)
        if name in ("mat_caroserie.007", "black_accents.007"):      # the window seals are the black lines seen from the cockpit
            f = drop_inner_skins(v, f, vn)
        if name == "mirrors_new.007":                               # the side mirrors only: the rear view mirror is drawn by the game
            f = f[np.abs((v[f[:, 0]][:, 0] + v[f[:, 1]][:, 0] + v[f[:, 2]][:, 0]) / 3) > 0.70]
        out.append((v, f, kind, vn))
    return out


# ---- writing ----------------------------------------------------------------------------------------------------------------
def emit(name, space, groups, smooth=True, double=False, f_out=None, projected_uv=True):
    nv = nt = 0
    lines = ["mesh %s %s\n" % (name, space)]
    for group in groups:
        v, f, kind = group[:3]
        vn = group[3] if len(group) > 3 else None
        if vn is not None:
            a, b, c = v[f[:, 0]], v[f[:, 1]], v[f[:, 2]]
            fn = np.cross(b - a, c - a)
            fn = fn / np.maximum(np.linalg.norm(fn, axis=1), 1e-12)[:, None]
            f = f.copy()
            wrong = (fn * vn[f].mean(axis=1)).sum(1) < 0          # a triangle the decimator turned inside out: put it back
            f[wrong] = f[wrong][:, [0, 2, 1]]
            fn[wrong] *= -1
            cn = vn[f]
        elif smooth:
            fn, cn = smooth_corner_normals(v, f)
        else:
            a, b, c = v[f[:, 0]], v[f[:, 1]], v[f[:, 2]]
            fn = np.cross(b - a, c - a)
            fn = fn / np.maximum(np.linalg.norm(fn, axis=1), 1e-12)[:, None]
            cn = np.repeat(fn[:, None, :], 3, axis=1)
        for t, tri in enumerate(f):
            pts = [tuple(v[k]) for k in tri]
            fk = kind
            if kind == "proj":
                cx = abs(sum(p[0] for p in pts) / 3)
                if cx > MIRROR_X:
                    fk = "ACCENT"
            for flip in ((False, True) if double else (False,)):
                order = (0, 2, 1) if flip else (0, 1, 2)
                if fk == "proj":
                    view = M.face_view(tuple(fn[t]))
                    if view is None:
                        uvs = [M.swatch_uv("DARK")] * 3
                    else:
                        uvs = [view.uv(pts[i]) for i in order]
                elif name == "glass":
                    uvs = [(0.5 + 0.25 * pts[i][0], 0.5 + 0.25 * pts[i][2]) for i in order]
                else:
                    uvs = [M.swatch_uv(fk)] * 3
                for k, i in enumerate(order):
                    n = cn[t, i] * (-1 if flip else 1)
                    lines.append("v %.4f %.4f %.4f %.3f %.3f %.3f %.5f %.5f\n" % (pts[i] + tuple(n) + tuple(uvs[k])))
                lines.append("t %d %d %d\n" % (nv, nv + 1, nv + 2))
                nv += 3
                nt += 1
    print("%-11s %6d vertices %6d triangles" % (name, nv, nt))
    return lines


def backing(x0, x1, y0, y1, z0, z1):
    """A dark box: (vertices, triangles, zone)."""
    v = np.array([[x, y, z] for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)], float)
    quads = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    f, c = [], v.mean(0)
    for a, b, c_, d in quads:
        for tri in ((a, b, c_), (a, c_, d)):
            n = np.cross(v[tri[1]] - v[tri[0]], v[tri[2]] - v[tri[0]])
            f.append(tri if np.dot(n, v[list(tri)].mean(0) - c) > 0 else tri[::-1])
    return (v, np.array(f), "DARK")


def front_faces(group, sign, minimum=0.10):
    """Keeps the triangles of a lamp whose normal looks out of the car along z (sign +1 front, -1 rear)."""
    v, f, kind, vn = group
    n = vn[f].mean(axis=1)
    keep = n[:, 2] * sign > minimum
    print("  lamp faces kept: %d of %d" % (keep.sum(), len(f)))
    return (v, f[keep], kind, vn)


def wall(x, z0, z1, y0, y1):
    """A dark upright panel at x, facing away from the car (toward the wheel)."""
    s = 1 if x > 0 else -1
    v = np.array([[x, y0, z0], [x, y0, z1], [x, y1, z1], [x, y1, z0]], float)
    f = np.array([[0, 1, 2], [0, 2, 3]]) if s > 0 else np.array([[0, 2, 1], [0, 3, 2]])
    return (v, f, "DARK")


def firewall():
    """The stock body has a solid cowl under its hood line, so the driver never looks into the engine bay."""
    z = 0.76
    lines = []
    return [(np.array([[-0.66, 0.02, z], [0.66, 0.02, z], [0.66, 0.53, z], [-0.66, 0.53, z]]), np.array([[0, 1, 2], [0, 2, 3]]), "DARK")]


# ---- livery -----------------------------------------------------------------------------------------------------------------
def paint():
    """The atlas in game coordinates; the zones follow the reference's panels (doors, bumpers, skirts)."""
    ACC, MAIN, TERT = M.ACCENT, M.MAIN, M.TERT
    P = M.Painter()
    for v in (M.V_SIDE_R, M.V_SIDE_L):
        sgn = 1 if v is M.V_SIDE_R else -1
        A = lambda z: z * sgn
        # accent: skirts and the lower front / rear bumpers
        P.poly(v, [(A(z), y) for z, y in ((-1.50, -0.2), (-1.50, 0.16), (1.30, 0.16), (1.30, -0.2))], ACC, 190)
        P.poly(v, [(A(z), y) for z, y in ((1.20, -0.2), (1.20, 0.24), (1.36, 0.31), (1.90, 0.31), (1.90, -0.2))], ACC, 190)
        P.poly(v, [(A(z), y) for z, y in ((-1.34, -0.2), (-1.34, 0.30), (-1.90, 0.33), (-1.90, -0.2))], ACC, 190)
        # the race number, straight on the front door in the accent colour
        P.text(v, A(0.20), 0.36, "300", 78, ACC, 214)
    v = M.V_FRONT
    P.rect(v, -0.85, 0.85, -0.2, 0.20, ACC, 190)
    P.text(v, 0.0, 0.135, "H&R", 34, MAIN, 214)
    v = M.V_REAR
    P.rect(v, -0.85, 0.85, -0.2, 0.26, ACC, 190)
    P.text(v, 0.0, 0.36, "300", 50, ACC, 214)
    v = M.V_TOP
    P.text(v, 0.0, 1.20, "300", 104, ACC, 214, rotate=180)
    for name, zone in (("MAIN", MAIN), ("ACCENT", ACC), ("DARK", TERT), ("GLASS", MAIN), ("LIGHT", MAIN)):
        x, y = M.SWATCH[name]
        P.dm.rectangle([x, y, x + 63, y + 63], fill=zone)
        P.db.rectangle([x, y, x + 63, y + 63], fill=120 if name == "DARK" else 214)
    P.mask.save(os.path.join(HERE, "dacia_mask.png"))
    P.base.save(os.path.join(HERE, "dacia_base.png"))
    return P


# ---- main -------------------------------------------------------------------------------------------------------------------
def main(glb):
    parts = load_glb(glb)
    print("reference parts:", len(parts))
    shell = collect(parts, SHELL) + firewall()
    glass = [(v, f, "GLASS") for v, f, _, _ in collect(parts, [(n, "GLASS", t) for n, t in GLASS])]
    head = [(v, f, "LIGHT", vn) for v, f, _, vn in collect(parts, [(n, "LIGHT", t) for n, t in HEADLIGHTS])]
    tail = [(v, f, "LIGHT", vn) for v, f, _, vn in collect(parts, [(n, "LIGHT", t) for n, t in TAILLIGHTS])]
    head_whole = head
    head = [front_faces(g, +1) for g in head]
    tail = [front_faces(g, -1) for g in tail]
    # The hood is its own mesh (the game's hood renderer) so the plugin can hide it from the cockpit, the way the game hides the windscreen.
    v, f, kind, vn = shell[0]
    c = (v[f[:, 0]] + v[f[:, 1]] + v[f[:, 2]]) / 3
    n = vn[f].mean(axis=1)
    is_hood = (c[:, 2] > 0.78) & (c[:, 1] > 0.38) & (np.abs(c[:, 0]) < 0.62) & (n[:, 1] > 0.15)
    print("  hood faces: %d" % is_hood.sum())
    shell[0] = (v, f[~is_hood], kind, vn)
    hood = [(v, f[is_hood], kind, vn)]

    for side in (1, -1):                                          # a dark backing plate behind each headlight, so no light shows through
        pts = np.concatenate([v[:, :3][(np.sign(v[:, 0]) == side)] for v, f, k, vn in head_whole])
        lo, hi = pts.min(0), pts.max(0)
        x0, x1 = (lo[0] + 0.05, hi[0] - 0.12) if side == 1 else (lo[0] + 0.12, hi[0] - 0.05)      # inside the body outline, not out at the corner
        shell.append(backing(x0, x1, lo[1] + 0.01, hi[1] - 0.02, lo[2] - 0.07, lo[2] - 0.02))
    for side in (1, -1):                                          # close each wheel well off from the engine bay / boot
        for zc in (1.01, -1.175):
            shell.append(wall(side * 0.485, zc - 0.36, zc + 0.36, 0.0, 0.46))
    lines = []
    lines += emit("shell", "body", shell)
    lines += emit("hood", "body", hood, smooth=False)
    lines += emit("glass", "body", glass, smooth=False, double=True)
    lines += emit("headlights", "root", head, smooth=True)
    lines += emit("taillights", "root", tail, smooth=True)
    with open(os.path.join(HERE, "dacia_model.txt"), "w", newline="\n") as f:
        f.writelines(lines)
    P = paint()
    return P


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    main(sys.argv[1])
