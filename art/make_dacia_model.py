"""The atlas, the painter and the file format of the Dacia model. import_ref_logan.py builds on this module; running this file directly
regenerates the OLD hand-built model over dacia_model.txt, which is no longer the shipped one.

Builds the first, hand-built Dacia Logan for Half Grip: geometry, paint-zone mask and detail texture.

Writes, next to this file:
  dacia_model.txt     the meshes, flat shaded, one block per mesh:   mesh <name> <space>  /  v x y z nx ny nz u v  /  t a b c
  dacia_mask.png      which of the car shader's three paint zones every texel belongs to (R accent, G main, B tertiary)
  dacia_base.png      the detail layer (panel lines, grille slats, numbers) the shader multiplies the paint with
  dacia_model_preview.png   4 views drawn with the game's three zones in lime / blue / white (not shipped)

Space "body": the car's local frame as the game's 2nd car model sees it (x right, y up, z forward, the stock body spans
x +-0.72, y -0.02..0.88, z -1.86..1.76, the wheels are at x +-0.625, y 0.096, z 1.01 and -1.175). The plugin turns it into
the stock child meshes' frame. Space "root": the same frame, for the light units, which get an identity transform.

The paint belongs to the player: the zones only say WHERE a colour goes, never which colour. The Logan's livery decides the
zones: blue upper body = main (G), lime bumpers / skirts / mirrors / roof band = accent (R), black plastics and number
panels = tertiary (B).
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.dirname(os.path.abspath(__file__))

# ---- the game's car ---------------------------------------------------------------------------------------------------
ZF, ZR = 1.60, -1.80            # nose and tail (shorter nose than the stock body, so the hood stays out of the driver's view)
WHEELS = (1.01, -1.175)
WHEEL_Y = 0.096
ARCH_R = 0.31
YS = 0.02                       # underside
ZCOWL = 0.90                    # where the hood ends
ZWS_B = 0.87                    # where the windscreen starts, a short cowl step behind the hood
ZDECK = -1.50                   # where the rear window ends and the boot lid starts
ZW_T = 0.30                     # top of the windscreen / front of the roof
ZR_T = -1.00                    # back of the roof / top of the rear window
NOSE_R, NOSE_X = 0.20, 0.17     # plan view corner of the nose (length, width)
TAIL_R, TAIL_X = 0.18, 0.15

ACCENT, MAIN, TERT = (255, 0, 0), (0, 255, 0), (0, 0, 255)


def lerp(a, b, t): return a + (b - a) * t
def smooth(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def norm(a):
    n = math.sqrt(dot(a, a)) or 1.0
    return (a[0] / n, a[1] / n, a[2] / n)


def table(tab, z):
    """Piecewise linear lookup in a table sorted from the nose to the tail (z descending)."""
    if z >= tab[0][0]: return tab[0][1]
    for (za, va), (zb, vb) in zip(tab, tab[1:]):
        if zb <= z <= za:
            return lerp(va, vb, (z - za) / (zb - za))
    return tab[-1][1]


# ---- the Logan's plan and profile ----------------------------------------------------------------------------------------
WIDTH = [(1.60, 0.700), (1.40, 0.715), (1.01, 0.725), (0.20, 0.725), (-0.60, 0.720), (-1.20, 0.715), (-1.58, 0.700), (-1.80, 0.680)]
TOP = [(1.60, 0.410), (1.40, 0.450), (1.20, 0.470), (1.05, 0.495), (0.93, 0.520), (0.90, 0.530), (0.87, 0.590),
       (-1.50, 0.590), (-1.58, 0.585), (-1.72, 0.570), (-1.80, 0.560)]
BELT_Y = 0.595


def plan_w(z):
    w = table(WIDTH, z)
    rn, rx = NOSE_R, NOSE_X        # nose corner
    if z > ZF - rn:
        t = (z - (ZF - rn)) / rn
        w -= rx * (1 - math.sqrt(max(0.0, 1 - t * t)))
    rt, rtx = TAIL_R, TAIL_X       # tail corner
    if z < ZR + rt:
        t = (ZR + rt - z) / rt
        w -= rtx * (1 - math.sqrt(max(0.0, 1 - t * t)))
    return w


def flare(z):
    f = 0.0
    for zc in WHEELS:
        d = abs(z - zc)
        if d < ARCH_R + 0.14:
            f = max(f, 0.032 * (0.5 + 0.5 * math.cos(math.pi * d / (ARCH_R + 0.14))))
    return f


def arch(z):
    a = YS
    for zc in WHEELS:
        if abs(z - zc) < ARCH_R:
            a = max(a, WHEEL_Y + math.sqrt(ARCH_R ** 2 - (z - zc) ** 2))
    return a


def profile(z):
    """The right half of the wall at z, from the underside to the door ledge: 7 points (x, y)."""
    w, t, f, a = plan_w(z), table(TOP, z), flare(z), arch(z)
    pts = [(w - 0.13, YS), (w - 0.03 + 0.5 * f, 0.08), (w + f, min(0.22, 0.5 * t)), (w + 0.008 + f, min(0.42, t - 0.075)),
           (w + 0.5 * f, t - 0.03), (w - 0.04, t), (w - 0.11, t - 0.003)]
    return [(x, max(y, a) if i <= 3 else y) for i, (x, y) in enumerate(pts)]


def wall_zs():
    zs = {round(z, 4) for z, _ in WIDTH} | {round(z, 4) for z, _ in TOP}
    for k in range(0, 9):
        t = k / 8
        zs.add(round(ZF - NOSE_R * (1 - math.cos(t * math.pi / 2)), 4))
        zs.add(round(ZR + TAIL_R * (1 - math.cos(t * math.pi / 2)), 4))
    zs |= {round(ZF - NOSE_R * (1 - math.sin(t * math.pi / 2)), 4) for t in (0.3, 0.6, 0.85, 0.95)}
    for zc in WHEELS:
        for k in range(-12, 13):
            zs.add(round(zc + ARCH_R * math.sin(k / 12 * math.pi / 2), 4))
        zs.add(round(zc + ARCH_R + 0.14, 4))
        zs.add(round(zc - ARCH_R - 0.14, 4))
    z = ZR
    while z < ZF:
        zs.add(round(z, 4))
        z += 0.14
    zs.add(ZF)
    zs.add(ZR)
    return sorted((z for z in zs if ZR <= z <= ZF), reverse=True)


# ---- faces and meshes ----------------------------------------------------------------------------------------------------
INTERIOR = (0.0, 0.45, -0.05)


class Mesh:
    def __init__(self, name, space="body"):
        self.name, self.space = name, space
        self.faces = []     # (points, kind) kind = 'proj' or a swatch name

    def face(self, pts, kind="proj", ref=None, both=False, inward=False, up=False):
        pts = list(pts)
        n = (0.0, 0.0, 0.0)
        for i in range(1, len(pts) - 1):
            n = add(n, cross(sub(pts[i], pts[0]), sub(pts[i + 1], pts[0])))
        if dot(n, n) < 1e-14:
            return
        centre = mul(tuple(map(sum, zip(*pts))), 1.0 / len(pts))
        out = n[1] if up else dot(n, sub(centre, ref if ref is not None else INTERIOR))
        if (out < 0) != inward:
            pts.reverse()
        front = n if out >= 0 else mul(n, -1)
        self.faces.append((pts, kind, front))
        if both:
            self.faces.append((list(reversed(pts)), kind, front))

    def quad(self, a, b, c, d, kind="proj", **kw):
        self.face([a, b, c, d], kind, **kw)

    def frustum(self, r0, r1, kind):
        """Two quads (4 points each, same order) joined: a box, a wedge, a tapered pod."""
        centre = mul(tuple(map(sum, zip(*(r0 + r1)))), 1.0 / 8)
        for i in range(4):
            j = (i + 1) % 4
            self.face([r0[i], r0[j], r1[j], r1[i]], kind, ref=centre)
        self.face(r0, kind, ref=centre)
        self.face(r1, kind, ref=centre)

    def box(self, x0, x1, y0, y1, z0, z1, kind):
        r0 = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)]
        r1 = [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        self.frustum(r0, r1, kind)

    def tube(self, p0, p1, r, kind, sides=6, r1=None):
        d = norm(sub(p1, p0))
        s = norm(cross(d, (0, 1, 0))) if abs(d[1]) < 0.95 else (1.0, 0.0, 0.0)
        u = norm(cross(s, d))
        r1 = r if r1 is None else r1
        ring = lambda p, rr: [add(p, add(mul(s, rr * math.cos(2 * math.pi * k / sides)), mul(u, rr * math.sin(2 * math.pi * k / sides)))) for k in range(sides)]
        a, b = ring(p0, r), ring(p1, r1)
        centre = mul(add(p0, p1), 0.5)
        for k in range(sides):
            j = (k + 1) % sides
            self.face([a[k], a[j], b[j], b[k]], kind, ref=centre)
        self.face(a, kind, ref=centre)
        self.face(b, kind, ref=centre)

    def grid(self, fn, nu, nv, kind="proj", both=False, ref=None, up=False):
        """A surface from fn(u, v) with u, v in 0..1."""
        pts = [[fn(i / nu, j / nv) for j in range(nv + 1)] for i in range(nu + 1)]
        for i in range(nu):
            for j in range(nv):
                self.face([pts[i][j], pts[i + 1][j], pts[i + 1][j + 1], pts[i][j + 1]], kind, both=both, ref=ref, up=up)


def mirror_x(p): return (-p[0], p[1], p[2])


# ---- the body ------------------------------------------------------------------------------------------------------------
RF = 1.075          # roof height
ROOF_W = 0.545      # half width of the roof
NOSE_W = 0.51       # half width the nose's bulge is measured against
TAIL_W = 0.53
NOSE_B, TAIL_B = 0.050, 0.035
BELT, TOPG = 0.595, 1.060   # bottom and top of the side glass


def wall_x(y):
    """The outside of the cabin at height y."""
    return 0.685 - 0.14 * (y - 0.665) / (RF - 0.665) + 0.004


def hood_dy(s, f):
    """How far the hood's surface rises above the wall's top edge at width fraction s and length fraction f (1 at the nose)."""
    ridge = 0.005 * math.exp(-((abs(s) - 0.46) / 0.10) ** 2) * smooth(f * 1.5)     # the Logan's two long bonnet creases
    return 0.006 * (1 - s * s) + ridge


def deck_dy(s, f):
    """Same for the boot lid, f = 0 at the tail."""
    return 0.012 * (1 - s * s) + 0.010 * (1 - f) ** 2


def nose_z(x):
    """The nose surface in front view: it bulges forward in the middle."""
    u = max(-1.0, min(1.0, x / NOSE_W))
    return ZF + NOSE_B * (1 - u * u)


def tail_z(x):
    u = max(-1.0, min(1.0, x / TAIL_W))
    return ZR - TAIL_B * (1 - u * u)


def build_shell():
    m = Mesh("shell")
    zs = wall_zs()
    profs = {z: profile(z) for z in zs}

    for side in (1, -1):
        for z0, z1 in zip(zs, zs[1:]):
            a, b = profs[z0], profs[z1]
            for i in range(6):
                P = lambda pr, k, z: (side * pr[k][0], pr[k][1], z)
                m.quad(P(a, i, z0), P(a, i + 1, z0), P(b, i + 1, z1), P(b, i, z1))
        # the wheel arches: a dark tunnel so the wheel sits in a hole and not behind a wall
        for zc in WHEELS:
            za = [zc + ARCH_R * math.sin(k / 12 * math.pi / 2) for k in range(-12, 13)]
            for z0, z1 in zip(za, za[1:]):
                A0, A1 = arch(z0), arch(z1)
                w0, w1 = profile(z0)[2][0], profile(z1)[2][0]
                m.quad((side * w0, A0, z0), (side * (w0 - 0.24), A0, z0), (side * (w1 - 0.24), A1, z1), (side * w1, A1, z1), "DARK", ref=(side * 0.6, WHEEL_Y, zc), inward=True)
                m.quad((side * (w0 - 0.24), 0.0, z0), (side * (w0 - 0.24), A0, z0), (side * (w1 - 0.24), A1, z1), (side * (w1 - 0.24), 0.0, z1), "DARK", ref=(side * 0.6, WHEEL_Y, zc))

    # nose and tail: rows of the profile joined across the width, bulging forward in the middle
    def cap(zend, fz, dy):
        prof = profile(zend)[:6]
        y0, y5 = prof[0][1], prof[5][1]
        N = 20
        for i in range(5):
            for k in range(N):
                sa, sb = -1 + 2 * k / N, -1 + 2 * (k + 1) / N
                # the top row rides up with the hood (or boot lid) edge so there is no slit between them
                pt = lambda s, r: (s * prof[r][0], prof[r][1] + dy(s) * (prof[r][1] - y0) / (y5 - y0), fz(s))
                m.quad(pt(sa, i), pt(sb, i), pt(sb, i + 1), pt(sa, i + 1))
    cap(ZF, lambda s: ZF + NOSE_B * (1 - s * s), lambda s: hood_dy(s, 1.0))
    cap(ZR, lambda s: ZR - TAIL_B * (1 - s * s), lambda s: deck_dy(s, 0.0))

    # firewall: the stock body has a solid cowl under the hood line, so the driver never looks into the engine bay
    zfw = ZCOWL - 0.015
    xfw = plan_w(zfw) - 0.04
    for k in range(10):
        xa, xb = -xfw + 2 * xfw * k / 10, -xfw + 2 * xfw * (k + 1) / 10
        m.quad((xa, YS, zfw), (xb, YS, zfw), (xb, table(TOP, ZCOWL) - 0.012, zfw), (xa, table(TOP, ZCOWL) - 0.012, zfw), "DARK", both=True)

    # floor
    for z0, z1 in zip(zs, zs[1:]):
        a, b = profs[z0][0][0], profs[z1][0][0]
        m.quad((-a, YS, z0), (a, YS, z0), (b, YS, z1), (-b, YS, z1), "DARK", ref=(0, 1, 0))

    # boot lid: short and high, from the tail's top edge to the rear window
    zrows = [z for z in zs if ZR <= z <= ZDECK][::-1]
    def deck_pt(zr, s):
        xr = plan_w(zr) - 0.04
        f = (zr - ZR) / (ZDECK - ZR)
        zz = zr - TAIL_B * (1 - s * s) * (1 - f) ** 2
        y = table(TOP, zr) + deck_dy(s, f)
        return (s * xr, y, zz)
    N = 14
    for z0, z1 in zip(zrows, zrows[1:]):
        for k in range(N):
            sa, sb = -1 + 2 * k / N, -1 + 2 * (k + 1) / N
            m.quad(deck_pt(z0, sa), deck_pt(z0, sb), deck_pt(z1, sb), deck_pt(z1, sa), up=True)

    # cabin: a long upright greenhouse set forward, with a slightly domed roof
    def roof_pt(u, v):
        s = -1 + 2 * u
        return (s * ROOF_W, RF + 0.026 * (1 - s * s), lerp(ZW_T, ZR_T, v))
    m.grid(roof_pt, 10, 10, up=True)
    for side in (1, -1):
        m.tube((side * 0.675, BELT_Y, ZWS_B), (side * ROOF_W, RF, ZW_T), 0.040, "MAIN", 6)             # A
        m.tube((side * 0.69, BELT_Y - 0.005, -0.38), (side * ROOF_W, RF, -0.38), 0.036, "MAIN", 6)              # B
        m.tube((side * ROOF_W, RF, ZR_T), (side * 0.665, BELT_Y, ZDECK), 0.056, "MAIN", 6, 0.062)      # C
        m.tube((side * ROOF_W, RF, ZW_T), (side * ROOF_W, RF, ZR_T), 0.026, "MAIN", 6)                # roof rail
    m.tube((-ROOF_W, RF, ZW_T), (ROOF_W, RF, ZW_T), 0.030, "MAIN", 6)
    m.tube((-ROOF_W, RF, ZR_T), (ROOF_W, RF, ZR_T), 0.032, "MAIN", 6)

    # the sail panel in front of each door glass: body colour, so the A pillar has no hole behind it
    for side in (1, -1):
        q = lambda z, y: (side * (wall_x(y) - 0.002), y, z)
        m.face([q(ZWS_B - 0.005, BELT), q(0.50, BELT), q(0.34, TOPG), q(ZW_T + 0.03, TOPG)], "MAIN", both=True)

    for side in (1, -1):
        # door mirrors (accent), stalk and head
        m.tube((side * 0.67, 0.64, 0.38), (side * 0.78, 0.685, 0.37), 0.013, "DARK", 4)
        m.frustum([(side * 0.77, 0.655, 0.43), (side * 0.77, 0.655, 0.31), (side * 0.77, 0.75, 0.32), (side * 0.77, 0.75, 0.43)],
                  [(side * 0.88, 0.66, 0.41), (side * 0.88, 0.66, 0.325), (side * 0.88, 0.74, 0.33), (side * 0.88, 0.74, 0.41)], "ACCENT")
        # door handles
        for zh in (-0.22, -0.72):
            m.box(min(side * 0.726, side * 0.745), max(side * 0.726, side * 0.745), 0.52, 0.55, zh - 0.06, zh + 0.06, "DARK")

    # the roof antenna and the roof-end wing of the race car
    m.tube((0.30, RF + 0.02, -0.55), (0.335, RF + 0.62, -0.60), 0.007, "DARK", 4, 0.004)
    m.box(0.27, 0.33, RF, RF + 0.025, -0.59, -0.51, "DARK")
    wz0, wz1, wy = ZR_T - 0.05, ZR_T - 0.22, RF + 0.045
    m.frustum([(-0.52, wy, wz0), (0.52, wy, wz0), (0.52, wy + 0.018, wz0 - 0.01), (-0.52, wy + 0.018, wz0 - 0.01)],
              [(-0.52, wy - 0.01, wz1), (0.52, wy - 0.01, wz1), (0.52, wy + 0.032, wz1), (-0.52, wy + 0.032, wz1)], "DARK")
    for side in (1, -1):
        m.box(min(side * 0.52, side * 0.548), max(side * 0.52, side * 0.548), RF - 0.01, RF + 0.12, wz1, wz0, "DARK")
        m.tube((side * 0.38, RF + 0.01, ZR_T), (side * 0.38, wy, ZR_T - 0.10), 0.011, "DARK", 4)

    # front splitter and rear lip
    m.frustum([(-0.46, 0.025, 1.48), (0.46, 0.025, 1.48), (0.46, 0.05, 1.48), (-0.46, 0.05, 1.48)],
              [(-0.42, 0.025, 1.70), (0.42, 0.025, 1.70), (0.42, 0.05, 1.70), (-0.42, 0.05, 1.70)], "DARK")
    m.box(-0.40, 0.40, 0.025, 0.06, -1.86, -1.78, "DARK")

    # the Dacia shield, on the grille: an accent rim and a tertiary face
    def shield(zf, k, kind):
        pts = [(-0.045 * k, 0.088 * k), (0.045 * k, 0.088 * k), (0.045 * k, 0.030 * k), (0.0, -0.020 * k), (-0.045 * k, 0.030 * k)]
        return [(x, 0.310 + y, zf) for x, y in pts]
    zb = nose_z(0.0) + 0.002
    outer, inner = shield(zb, 1.0, "ACCENT"), shield(zb + 0.012, 0.74, "DARK")
    for i in range(5):
        j = (i + 1) % 5
        m.face([outer[i], outer[j], (outer[j][0], outer[j][1], zb + 0.012), (outer[i][0], outer[i][1], zb + 0.012)], "ACCENT", ref=(0, 0.31, zb - 0.1))
    m.face([(x, y, zb + 0.012) for x, y, _ in outer], "ACCENT", ref=(0, 0.31, zb - 0.1))
    m.face([(x, y, zb + 0.016) for x, y, _ in inner], "DARK", ref=(0, 0.31, zb - 0.1))
    return m


def build_hood():
    m = Mesh("hood")
    zrows = [z for z in wall_zs() if ZCOWL <= z <= ZF]
    N = 20
    def pt(zr, s):
        f = (zr - ZCOWL) / (ZF - ZCOWL)
        xr = plan_w(zr) - 0.04
        zz = zr + NOSE_B * (1 - s * s) * f ** 2
        return (s * xr, table(TOP, zr) + hood_dy(s, f), zz)
    for z0, z1 in zip(zrows, zrows[1:]):
        for k in range(N):
            sa, sb = -1 + 2 * k / N, -1 + 2 * (k + 1) / N
            m.quad(pt(z0, sa), pt(z0, sb), pt(z1, sb), pt(z1, sa), up=True)
    # the cowl: a dark strip closing the step from the hood's back edge up to the windscreen's foot
    xr = plan_w(ZCOWL) - 0.04
    for k in range(N):
        sa, sb = -1 + 2 * k / N, -1 + 2 * (k + 1) / N
        lo = lambda s_: (s_ * xr, table(TOP, ZCOWL) + hood_dy(s_, 0.0), ZCOWL)
        hi = lambda s_: (s_ * xr, BELT_Y, ZWS_B)
        m.quad(lo(sa), lo(sb), hi(sb), hi(sa), "DARK", ref=(0, 0.2, 2.0))
    return m


def build_glass():
    m = Mesh("glass")
    def screen(u, s):
        z, y = lerp(ZWS_B - 0.005, ZW_T + 0.02, s), lerp(BELT_Y + 0.005, RF - 0.010, s)
        hw = lerp(0.655, 0.515, s)
        return ((2 * u - 1) * hw, y, z + 0.024 * (1 - (2 * u - 1) ** 2))
    m.grid(screen, 8, 4, "GLASS", both=True)
    def rear(u, s):
        z, y = lerp(ZR_T, ZDECK, s), lerp(RF - 0.010, BELT_Y + 0.005, s)
        hw = lerp(0.515, 0.640, s)
        return ((2 * u - 1) * hw, y, z - 0.022 * (1 - (2 * u - 1) ** 2))
    m.grid(rear, 8, 4, "GLASS", both=True)
    for side in (1, -1):
        p = lambda z, y: (side * wall_x(y), y, z)
        m.face([p(0.50, BELT), p(0.34, TOPG), p(-0.35, TOPG), p(-0.35, BELT)], "GLASS", both=True)
        m.face([p(-0.41, BELT), p(-0.41, TOPG), p(-1.01, TOPG), p(-1.44, BELT)], "GLASS", both=True)
    return m


def prism(m, outline, z_of, depth, kind, ref):
    """An extruded outline (x, y) laid on a surface z_of(x): a front face, a back face and the rim."""
    front = [(x, y, z_of(x) + depth) for x, y in outline]
    back = [(x, y, z_of(x)) for x, y in outline]
    cx = sum(x for x, _ in outline) / len(outline)
    cy = sum(y for _, y in outline) / len(outline)
    c = (cx, cy, z_of(cx) + depth)
    for i in range(len(front)):
        j = (i + 1) % len(front)
        m.face([c, front[i], front[j]], kind, ref=ref)
        m.face([back[i], back[j], front[j], front[i]], kind, ref=ref)


def build_headlights():
    """The Logan's big wedge lamps, low at the grille and sweeping up into the fender."""
    m = Mesh("headlights", "root")
    outline = [(0.255, 0.20), (0.255, 0.275), (0.33, 0.32), (0.42, 0.355), (0.495, 0.392), (0.51, 0.31), (0.49, 0.225), (0.40, 0.20)]
    for side in (1, -1):
        prism(m, [(side * x, y) for x, y in outline], lambda x: nose_z(x) + 0.004 - 0.0 * x, 0.020, "LIGHT", (0, 0.4, 0.5))
    return m


def build_taillights():
    """Tall vertical units that wrap the corners of the tail."""
    m = Mesh("taillights", "root")
    outline = [(0.36, 0.28), (0.36, 0.50), (0.50, 0.52), (0.535, 0.49), (0.535, 0.32), (0.50, 0.285)]
    for side in (1, -1):
        pts = [(side * x, y) for x, y in outline]
        front = [(x, y, tail_z(x) - 0.022) for x, y in pts]
        back = [(x, y, tail_z(x) + 0.004) for x, y in pts]
        cx = sum(x for x, _ in pts) / len(pts)
        cy = sum(y for _, y in pts) / len(pts)
        c = (cx, cy, tail_z(cx) - 0.022)
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            m.face([c, front[i], front[j]], "LIGHT", ref=(0, 0.4, 0.0))
            m.face([back[i], back[j], front[j], front[i]], "LIGHT", ref=(0, 0.4, 0.0))
    return m


# ---- texture atlas ---------------------------------------------------------------------------------------------------------
W, H = 2048, 1024
S = 256.0     # pixels per game unit


class View:
    def __init__(self, name, rect, a_fn, b_fn, a0, b_top):
        self.name, self.rect, self.a_fn, self.b_fn, self.a0, self.b_top = name, rect, a_fn, b_fn, a0, b_top

    def px(self, p):
        return (self.rect[0] + (self.a_fn(p) - self.a0) * S, self.rect[1] + (self.b_top - self.b_fn(p)) * S)

    def uv(self, p):
        x, y = self.px(p)
        return (x / W, 1 - y / H)

    def pix(self, a, b):
        """Pixel of the view for plane coordinates (a, b) as the view's a_fn and b_fn read them."""
        return (self.rect[0] + (a - self.a0) * S, self.rect[1] + (self.b_top - b) * S)


V_SIDE_R = View("side+", (0, 0), lambda p: p[2], lambda p: p[1], -1.95, 1.15)
V_SIDE_L = View("side-", (0, 340), lambda p: -p[2], lambda p: p[1], -1.85, 1.15)
V_FRONT = View("front", (0, 680), lambda p: -p[0], lambda p: p[1], -0.80, 1.15)
V_REAR = View("rear", (420, 680), lambda p: p[0], lambda p: p[1], -0.80, 1.15)
V_TOP = View("top", (1024, 0), lambda p: p[0], lambda p: p[2], -0.80, 1.85)
SWATCH = {"MAIN": (1500, 0), "ACCENT": (1580, 0), "DARK": (1660, 0), "GLASS": (1740, 0), "LIGHT": (1820, 0)}


def swatch_uv(name):
    x, y = SWATCH[name]
    return ((x + 32) / W, 1 - (y + 32) / H)


def face_view(n):
    ax = max(range(3), key=lambda i: abs(n[i]))
    if ax == 0: return V_SIDE_R if n[0] > 0 else V_SIDE_L
    if ax == 2: return V_FRONT if n[2] > 0 else V_REAR
    return V_TOP if n[1] > 0 else None


def triangulate(mesh):
    verts, tris = [], []
    for pts, kind, front in mesh.faces:
        n = (0.0, 0.0, 0.0)
        for i in range(1, len(pts) - 1):
            n = add(n, cross(sub(pts[i], pts[0]), sub(pts[i + 1], pts[0])))
        n = norm(n)
        if kind == "proj":
            view = face_view(norm(front))
            if view is None:
                kind = "DARK"
        if kind == "proj":
            uvs = [view.uv(p) for p in pts]
        elif mesh.name == "glass":
            uvs = [(0.5 + 0.25 * p[0], 0.5 + 0.25 * p[2]) for p in pts]
        else:
            uvs = [swatch_uv("MAIN" if kind in ("MAIN",) else kind)] * len(pts)
        base = len(verts)
        for p, uv in zip(pts, uvs):
            verts.append((p, n, uv))
        for i in range(1, len(pts) - 1):
            tris.append((base, base + i, base + i + 1))
    return verts, tris


# ---- painting --------------------------------------------------------------------------------------------------------------
def load_font(size):
    for name in ("arialbd.ttf", "arial.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


class Painter:
    def __init__(self):
        self.mask = Image.new("RGB", (W, H), MAIN)
        self.base = Image.new("L", (W, H), 214)
        self.dm = ImageDraw.Draw(self.mask)
        self.db = ImageDraw.Draw(self.base)

    def rect(self, view, a0, a1, b0, b1, zone=None, base=None, outline=None):
        (x0, y0), (x1, y1) = view.pix(a0, b1), view.pix(a1, b0)
        box = [min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)]
        if zone is not None: self.dm.rectangle(box, fill=zone)
        if base is not None: self.db.rectangle(box, fill=base)
        if outline is not None: self.db.rectangle(box, outline=outline)

    def poly(self, view, pts, zone=None, base=None):
        px = [view.pix(a, b) for a, b in pts]
        if zone is not None: self.dm.polygon(px, fill=zone)
        if base is not None: self.db.polygon(px, fill=base)

    def ellipse(self, view, a, b, r, zone=None, base=None):
        (x0, y0), (x1, y1) = view.pix(a - r, b + r), view.pix(a + r, b - r)
        box = [min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)]
        if zone is not None: self.dm.ellipse(box, fill=zone)
        if base is not None: self.db.ellipse(box, fill=base)

    def line(self, view, a0, b0, a1, b1, value=120, width=1):
        self.db.line([view.pix(a0, b0), view.pix(a1, b1)], fill=value, width=width)

    def text(self, view, a, b, s, size, zone, base_v, rotate=0, anchor_h=0.5):
        """Text centred on plane coordinates (a, b); drawn through a small layer so it can be turned."""
        font = load_font(size)
        tw, th = self.db.textbbox((0, 0), s, font=font)[2:]
        layer_m = Image.new("L", (tw + 8, th + 8), 0)
        ImageDraw.Draw(layer_m).text((4, 4), s, fill=255, font=font)
        if rotate:
            layer_m = layer_m.rotate(rotate, expand=True)
        cx, cy = view.pix(a, b)
        pos = (int(cx - layer_m.width / 2), int(cy - layer_m.height / 2))
        if zone is not None:
            self.mask.paste(Image.new("RGB", layer_m.size, zone), pos, layer_m)
        self.base.paste(Image.new("L", layer_m.size, base_v), pos, layer_m)


def paint():
    P = Painter()
    sides = (V_SIDE_R, V_SIDE_L)

    for v in sides:
        sgn = 1 if v is V_SIDE_R else -1     # plane 'a' is z for the right side and -z for the left
        A = lambda z: z * sgn
        span = lambda z0, z1: (A(z0), A(z1)) if sgn == 1 else (A(z1), A(z0))
        # accent: skirts, front fender / bumper, rear bumper
        P.poly(v, [(A(z), y) for z, y in ((-1.42, -0.2), (-1.42, 0.20), (1.24, 0.20), (1.24, -0.2))], ACCENT, 190)
        P.poly(v, [(A(z), y) for z, y in ((1.20, -0.2), (1.20, 0.24), (1.36, 0.36), (1.80, 0.36), (1.80, -0.2))], ACCENT, 190)
        P.poly(v, [(A(z), y) for z, y in ((-1.34, -0.2), (-1.34, 0.36), (-1.90, 0.40), (-1.90, -0.2))], ACCENT, 190)
        # race flares: a dark ring round both wheel arches
        for zc in WHEELS:
            P.ellipse(v, A(zc), WHEEL_Y, ARCH_R + 0.05, TERT, 70)
            P.ellipse(v, A(zc), WHEEL_Y, ARCH_R + 0.036, MAIN, None)
        # panel lines: doors, sills, the swage crease
        for z in (0.46, -0.37, -1.11):
            P.line(v, A(z), 0.21, A(z), 0.59, 105)
        P.line(v, A(0.46), 0.59, A(-1.11), 0.59, 120)
        P.line(v, A(0.46), 0.21, A(-1.11), 0.21, 120)
        P.line(v, A(1.30), 0.40, A(-1.40), 0.40, 160)
        P.line(v, A(1.30), 0.405, A(-1.40), 0.405, 235)
        # the race number, painted straight on the door in the accent colour (no plate behind it)
        P.text(v, A(0.09), 0.40, "300", 74, ACCENT, 214)
        # sill under the glass
        P.line(v, A(0.46), 0.60, A(-1.11), 0.60, 100)

    # front: the Logan's face
    v = V_FRONT
    P.rect(v, -0.80, 0.80, -0.2, 0.20, ACCENT, 190)                       # bumper
    # grille: an accent swoosh frame round a dark, slatted opening, shield on top
    P.poly(v, [(-0.45, 0.385), (0.45, 0.385), (0.31, 0.175), (-0.31, 0.175)], ACCENT, 200)
    P.poly(v, [(-0.40, 0.362), (0.40, 0.362), (0.285, 0.198), (-0.285, 0.198)], TERT, 60)
    for k in range(1, 6):
        y = 0.198 + k * 0.027
        w = lerp(0.285, 0.40, (y - 0.198) / 0.164)
        P.line(v, -w, y, w, y, 170, 2)
    P.poly(v, [(-0.05, 0.385), (0.05, 0.385), (0.05, 0.30), (-0.05, 0.30)], ACCENT, 200)
    # lower intake, fog lamps, plate
    P.rect(v, -0.46, 0.46, 0.035, 0.115, TERT, 70)
    for k in range(1, 9):
        P.line(v, -0.46 + k * 0.1022, 0.035, -0.46 + k * 0.1022, 0.115, 150)
    for sx in (-1, 1):
        P.ellipse(v, sx * 0.54, 0.115, 0.055, ACCENT, 190)
        P.ellipse(v, sx * 0.54, 0.115, 0.038, TERT, 60)
    P.text(v, 0.0, 0.155, "H&R", 36, MAIN, 214)
    P.line(v, -0.80, 0.20, 0.80, 0.20, 120)

    # rear
    v = V_REAR
    P.rect(v, -0.80, 0.80, -0.2, 0.26, ACCENT, 190)
    P.text(v, 0.0, 0.40, "300", 52, ACCENT, 214)
    for sx in (-1, 1):
        P.rect(v, min(sx * 0.34, sx * 0.56), max(sx * 0.34, sx * 0.56), 0.27, 0.53, TERT, 90)
    P.line(v, -0.80, 0.26, 0.80, 0.26, 120)
    P.line(v, -0.80, 0.55, 0.80, 0.55, 120)

    # top: hood, roof, boot
    v = V_TOP
    # hood: the V of creases, number panel (text turned to read from the front), vent
    P.text(v, 0.0, 1.24, "300", 110, ACCENT, 214, rotate=180)
    P.line(v, -0.64, 0.90, -0.64, 1.58, 110)
    P.line(v, 0.64, 0.90, 0.64, 1.58, 110)
    P.line(v, -0.64, 0.90, 0.64, 0.90, 110)
    P.line(v, -0.24, 0.94, -0.30, 1.58, 150)
    P.line(v, 0.24, 0.94, 0.30, 1.58, 150)
    # accent band on the front of the roof, like the screen banner
    P.rect(v, -0.60, 0.60, 0.02, 0.30, ACCENT, 200)
    P.line(v, -0.60, -1.00, 0.60, -1.00, 110)
    # boot lid seams
    P.line(v, -0.66, -1.50, 0.66, -1.50, 110)
    P.line(v, -0.66, -1.50, -0.66, -1.78, 110)
    P.line(v, 0.66, -1.50, 0.66, -1.78, 110)

    # swatches
    for name, zone in (("MAIN", MAIN), ("ACCENT", ACCENT), ("DARK", TERT), ("GLASS", MAIN), ("LIGHT", MAIN)):
        x, y = SWATCH[name]
        P.dm.rectangle([x, y, x + 63, y + 63], fill=zone)
        P.db.rectangle([x, y, x + 63, y + 63], fill=120 if name == "DARK" else 214)

    P.mask.save(os.path.join(OUT, "dacia_mask.png"))
    P.base.save(os.path.join(OUT, "dacia_base.png"))
    return P


# ---- output and preview ----------------------------------------------------------------------------------------------------
def write(meshes, path):
    with open(path, "w", newline="\n") as f:
        for m in meshes:
            verts, tris = triangulate(m)
            f.write("mesh %s %s\n" % (m.name, m.space))
            for p, n, uv in verts:
                f.write("v %.4f %.4f %.4f %.3f %.3f %.3f %.5f %.5f\n" % (p + n + uv))
            for t in tris:
                f.write("t %d %d %d\n" % t)
            print("%-11s %6d vertices %6d triangles" % (m.name, len(verts), len(tris)))


def preview(meshes, mask, base, path, gaps=False):
    pal = {0: np.array([60, 230, 90]), 1: np.array([42, 111, 214]), 2: np.array([235, 235, 235])}
    mk, bs = np.asarray(mask).astype(float), np.asarray(base).astype(float) / 255.0
    items = []
    for m in meshes:
        verts, tris = triangulate(m)
        items.append((m, verts, tris))
    views = [("rear 3/4", 35, 14), ("side", 90, 4), ("front 3/4", 215, 14), ("top", 20, 80)]
    if gaps:
        views = [("front-left", 215, 12), ("front-right", 145, 12), ("rear-left", 320, 12), ("rear-right", 40, 12)]
    VW, VH = 900, 520
    sheet = Image.new("RGB", (VW * 2, VH * 2), (58, 62, 72))
    light = np.array(norm((0.35, 0.85, 0.4)))
    for vi, (name, yaw, pitch) in enumerate(views):
        ya, pa = math.radians(yaw), math.radians(pitch)
        def proj(p):
            x, y, z = p
            xr, zr = x * math.cos(ya) - z * math.sin(ya), x * math.sin(ya) + z * math.cos(ya)
            yr, zr2 = y * math.cos(pa) + zr * math.sin(pa), -y * math.sin(pa) + zr * math.cos(pa)
            return xr, yr, zr2
        scale = 190
        img = np.zeros((VH, VW, 3)); img[:] = (58, 62, 72)
        zbuf = np.full((VH, VW), 1e9)
        for m, verts, tris in items:
            for a, b, c in tris:
                P3 = [proj(verts[k][0]) for k in (a, b, c)]
                sx = [VW / 2 + p[0] * scale for p in P3]
                sy = [VH / 2 + 30 - (p[1] - 0.45) * scale for p in P3]
                x0, x1 = int(max(0, min(sx))), int(min(VW - 1, max(sx)) + 1)
                y0, y1 = int(max(0, min(sy))), int(min(VH - 1, max(sy)) + 1)
                if x1 <= x0 or y1 <= y0: continue
                xs, ys = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
                d = (sy[1] - sy[2]) * (sx[0] - sx[2]) + (sx[2] - sx[1]) * (sy[0] - sy[2])
                if abs(d) < 1e-9: continue
                l0 = ((sy[1] - sy[2]) * (xs - sx[2]) + (sx[2] - sx[1]) * (ys - sy[2])) / d
                l1 = ((sy[2] - sy[0]) * (xs - sx[2]) + (sx[0] - sx[2]) * (ys - sy[2])) / d
                l2 = 1 - l0 - l1
                inside = (l0 >= -0.001) & (l1 >= -0.001) & (l2 >= -0.001)
                if not inside.any(): continue
                depth = l0 * P3[0][2] + l1 * P3[1][2] + l2 * P3[2][2]
                n = np.array(verts[a][1])
                shade = 0.50 + 0.50 * max(0.0, float(n @ light))
                if gaps and m.name in ("glass", "headlights", "taillights"):
                    continue
                away = proj(tuple(verts[a][1]))[2] > 0.02       # the face turns its back to the camera
                if gaps and away:
                    col = np.full((ys.shape[0], ys.shape[1], 3), (255, 140, 0))
                elif m.name == "glass":
                    col = np.full((ys.shape[0], ys.shape[1], 3), (150, 195, 225)) * shade
                elif m.name in ("headlights", "taillights"):
                    col = np.full((ys.shape[0], ys.shape[1], 3), (255, 245, 200) if m.name == "headlights" else (200, 30, 30)) * 1.0
                else:
                    u = l0 * verts[a][2][0] + l1 * verts[b][2][0] + l2 * verts[c][2][0]
                    v = l0 * verts[a][2][1] + l1 * verts[b][2][1] + l2 * verts[c][2][1]
                    tx = np.clip((u * W).astype(int), 0, W - 1); ty = np.clip(((1 - v) * H).astype(int), 0, H - 1)
                    mc = mk[ty, tx] / 255.0
                    zone = mc[..., 0:1] * pal[0] + mc[..., 1:2] * pal[1] + mc[..., 2:3] * pal[2]
                    col = zone * bs[ty, tx][..., None] * 1.15 * shade
                upd = inside & (depth < zbuf[y0:y1, x0:x1])
                zbuf[y0:y1, x0:x1] = np.where(upd, depth, zbuf[y0:y1, x0:x1])
                region = img[y0:y1, x0:x1]
                region[upd] = col[upd]
        im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
        ImageDraw.Draw(im).text((8, 6), name, fill=(255, 255, 255))
        sheet.paste(im, ((vi % 2) * VW, (vi // 2) * VH))
    sheet.save(path)


def main():
    meshes = [build_shell(), build_hood(), build_glass(), build_headlights(), build_taillights()]
    P = paint()
    write(meshes, os.path.join(OUT, "dacia_model.txt"))
    preview(meshes, P.mask, P.base, os.path.join(OUT, "dacia_model_preview.png"))
    preview(meshes, P.mask, P.base, os.path.join(OUT, "dacia_gaps.png"), gaps=True)


if __name__ == "__main__":
    main()
