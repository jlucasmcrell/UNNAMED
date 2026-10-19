"""Hand-drawn vector icons for the items and System Menu rows that had none (task 15, 2026-10-18).

The shipped icons (ui/icons/*, ui_icons.json) are painted tiles from the asset pipeline's image model, made on the GPU. This script is
the CPU-only, no-AI alternative the brief allows: simple flat vector drawings in the same house style as the older icon set - a dark
outline, flat fills with one highlight and one shade, a soft shadow, on an opaque grey tile - drawn with Pillow at 1024 px and reduced
to the manifest's 256 px master and 128 px sprite. Nothing here reads any Fab, Cosmos, MetaHuman or WildMesh content.

    python make_missing_icons.py --out D:\\Otherreach_Scratch\\ui_icons_missing          # draw, write the manifest additions and a contact sheet
    python make_missing_icons.py --out ... --install <asset root>                          # also merge into <root>/manifests/ui_icons.json

The art bindings (src/Presentation/Art/art_bindings.json, "icons") already name these ids; an asset root without them draws the same
text-only rows as before.
"""
import argparse
import json
import math
import os
import shutil

from PIL import Image, ImageDraw, ImageFilter

S = 1024
U = S / 100.0
OUT = (20, 17, 15)

# the palette: (base, light, shade)
WOOD = ((142, 94, 52), (176, 124, 72), (96, 62, 34))
LEATHER = ((120, 78, 48), (156, 106, 66), (78, 48, 30))
HIDE = ((176, 134, 88), (208, 168, 118), (128, 92, 58))
GREYHIDE = ((120, 124, 130), (156, 160, 166), (84, 88, 96))
DARKHIDE = ((84, 66, 56), (116, 92, 78), (56, 44, 38))
IRON = ((112, 116, 122), (164, 168, 174), (68, 72, 80))
DARKIRON = ((66, 68, 74), (104, 106, 114), (40, 42, 48))
BRASS = ((190, 150, 62), (236, 202, 112), (132, 98, 36))
PAPER = ((226, 212, 176), (246, 236, 208), (176, 158, 120))
CLOTH = ((98, 104, 116), (134, 140, 154), (66, 70, 82))
WOOL = ((150, 126, 96), (184, 160, 126), (104, 86, 62))
GREEN = ((92, 132, 72), (132, 176, 104), (58, 88, 48))
MOSS = ((74, 112, 62), (112, 156, 88), (46, 74, 40))
BONE = ((232, 224, 204), (252, 248, 236), (180, 170, 148))
EMBER = ((236, 120, 40), (255, 190, 80), (170, 60, 20))
SALVE = ((196, 70, 56), (232, 112, 88), (136, 40, 34))
GLASS = ((120, 190, 200), (200, 240, 244), (70, 130, 150))
STONE = ((150, 140, 124), (186, 176, 158), (102, 94, 82))
SILK = ((226, 228, 232), (255, 255, 255), (170, 174, 184))
RED = ((150, 52, 46), (190, 84, 70), (100, 30, 28))


def pt(p):
    return (p[0] * U, p[1] * U)


def smooth(points, closed=True, steps=10):
    """Catmull-Rom through the control points (0-100 units): organic outlines from a few points."""
    n = len(points)
    out = []
    rng = range(n) if closed else range(n - 1)
    for i in rng:
        p0 = points[(i - 1) % n] if closed or i > 0 else points[0]
        p1 = points[i]
        p2 = points[(i + 1) % n]
        p3 = points[(i + 2) % n] if closed or i + 2 < n else points[-1]
        for s in range(steps):
            t = s / steps
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[k]) + (-p0[k] + p2[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2
                                    + (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t3) for k in (0, 1)))
    if not closed:
        out.append(points[-1])
    return out


class Canvas:
    def __init__(self):
        self.img = Image.new("RGBA", (S, S))
        self.d = ImageDraw.Draw(self.img)

    # -- shapes: each draws a dark outline, then the fill
    def poly(self, pts, fill, ow=2.0, curve=False):
        pts = smooth(pts) if curve else pts
        px = [pt(p) for p in pts]
        self.d.polygon(px, fill=fill, outline=OUT, width=int(ow * U))
        return pts

    def ellipse(self, box, fill, ow=2.0):
        x0, y0, x1, y1 = [v * U for v in box]
        self.d.ellipse((x0, y0, x1, y1), fill=fill, outline=OUT, width=int(ow * U))

    def rrect(self, box, r, fill, ow=2.0):
        x0, y0, x1, y1 = [v * U for v in box]
        self.d.rounded_rectangle((x0, y0, x1, y1), radius=r * U, fill=fill, outline=OUT, width=int(ow * U))

    def line(self, pts, color, w, ow=1.6, cap=True):
        """A thick line with round ends and a dark edge."""
        px = [pt(p) for p in pts]
        for width, c in ((w + 2 * ow, OUT), (w, color)):
            self.d.line(px, fill=c, width=int(width * U), joint="curve")
            if cap:
                r = width * U / 2
                for x, y in (px[0], px[-1]):
                    self.d.ellipse((x - r, y - r, x + r, y + r), fill=c)

    def plain(self, pts, fill):
        self.d.polygon([pt(p) for p in pts], fill=fill)

    def shine(self, pts, color=(255, 255, 255, 70)):
        layer = Image.new("RGBA", (S, S))
        ImageDraw.Draw(layer).polygon([pt(p) for p in pts], fill=color)
        self.img.alpha_composite(layer)

    def dot(self, x, y, r, fill):
        self.d.ellipse(((x - r) * U, (y - r) * U, (x + r) * U, (y + r) * U), fill=fill)


def tile(draw_icon, bgtone=0, overlay=None):
    """The opaque grey tile with the soft floor shadow, and the drawing on it."""
    base = Image.new("RGBA", (S, S))
    px = base.load()
    for y in range(S):
        g = 70 + int(22 * (y / S)) + bgtone
        for x in range(S):
            px[x, y] = (g, g, g + 2, 255)
    shadow = Image.new("RGBA", (S, S))
    ImageDraw.Draw(shadow).ellipse((20 * U, 80 * U, 80 * U, 90 * U), fill=(0, 0, 0, 110))
    base.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)))
    c = Canvas()
    if overlay is not None:
        c.img.alpha_composite(overlay)
    else:
        draw_icon(c)
    # a faint dark halo behind the drawing lifts it off the tile
    halo = Image.new("RGBA", (S, S))
    halo.putalpha(c.img.getchannel("A").filter(ImageFilter.GaussianBlur(10)).point(lambda a: int(a * 0.45)))
    base.alpha_composite(halo)
    base.alpha_composite(c.img)
    return base.convert("RGB")


def shaded(c, pts, tones, curve=False, ow=2.0, light=None, shade=None):
    """A filled shape with a highlight and a shade strip drawn inside it."""
    drawn = c.poly(pts, tones[0], ow, curve)
    if light:
        c.shine(light, tones[1] + (150,))
    if shade:
        c.shine(shade, tones[2] + (140,))
    return drawn


# ---------------------------------------------------------------- the icons: each draws into a 100 x 100 unit canvas

def cloak(c):
    shaded(c, [(50, 12), (66, 18), (74, 42), (84, 80), (60, 86), (50, 80), (40, 86), (16, 80), (26, 42), (34, 18)], CLOTH, True,
           light=[(44, 22), (52, 20), (50, 76), (40, 80)], shade=[(66, 40), (80, 78), (66, 82), (62, 50)])
    shaded(c, [(36, 18), (50, 8), (64, 18), (58, 34), (50, 38), (42, 34)], CLOTH, True, shade=[(52, 36), (60, 30), (62, 20), (54, 14)])
    c.ellipse((46, 38, 54, 46), BRASS[0], 1.6)
    c.dot(50, 41, 1.2, BRASS[1])


def hide_boots(c):
    shaded(c, [(32, 14), (56, 14), (58, 50), (80, 62), (84, 80), (24, 80), (28, 50)], LEATHER, False, light=[(34, 16), (42, 16), (40, 76), (30, 76)],
           shade=[(48, 18), (56, 18), (58, 50), (72, 60), (70, 78), (48, 78)])
    c.rrect((24, 76, 86, 85), 2, LEATHER[2], 1.8)
    c.rrect((32, 14, 56, 22), 1.5, HIDE[0], 1.6)
    c.line([(34, 36), (54, 36)], BONE[0], 1.4, 1.0)
    c.line([(35, 44), (53, 44)], BONE[0], 1.4, 1.0)


def smith_gloves(c):
    shaded(c, [(30, 52), (30, 24), (36, 22), (38, 44), (40, 14), (46, 12), (47, 42), (50, 10), (56, 10), (56, 42), (60, 16), (66, 18), (64, 48), (72, 38),
               (76, 42), (68, 66), (62, 80), (36, 80)], LEATHER, False, light=[(34, 54), (36, 30), (40, 50), (38, 76)],
           shade=[(52, 52), (66, 50), (60, 78), (50, 78)])
    c.rrect((32, 74, 66, 88), 2, DARKIRON[0], 1.8)
    c.line([(40, 52), (64, 52)], LEATHER[2], 1.6, 0.8)


def timber_shield(c):
    c.ellipse((14, 14, 86, 86), WOOD[0], 2.4)
    for x in (29, 43, 57, 71):
        c.line([(x, 17 + 2 * abs(x - 50) / 8), (x, 83 - 2 * abs(x - 50) / 8)], WOOD[2], 0.8, 0.0, cap=False)
    c.shine([(20, 30), (40, 18), (46, 22), (26, 44)])
    for a in (0, 90, 180, 270):
        c.line([(50 + 12 * math.cos(math.radians(a)), 50 + 12 * math.sin(math.radians(a))), (50 + 26 * math.cos(math.radians(a)), 50 + 26 * math.sin(math.radians(a)))], IRON[2], 1.6, 0.0)
    c.ellipse((38, 38, 62, 62), IRON[0], 2.2)
    c.ellipse((43, 42, 55, 54), IRON[1], 1.2)
    c.d.ellipse((14 * U, 14 * U, 86 * U, 86 * U), outline=OUT, width=int(2.4 * U))


def wool_trousers(c):
    shaded(c, [(30, 12), (70, 12), (78, 52), (84, 86), (62, 86), (50, 44), (38, 86), (16, 86), (22, 52)], WOOL, False,
           light=[(30, 14), (42, 14), (36, 50), (22, 84), (18, 84), (24, 52)], shade=[(56, 46), (62, 84), (78, 84), (74, 52), (68, 16), (66, 16)])
    c.rrect((30, 12, 70, 22), 1.5, LEATHER[0], 1.8)
    c.line([(50, 20), (50, 44)], WOOL[2], 1.2, 0.8)
    c.line([(18, 78), (38, 78)], WOOL[2], 1.2, 0.0)
    c.line([(62, 78), (82, 78)], WOOL[2], 1.2, 0.0)


def roast_meat(c):
    c.line([(60, 56), (82, 80)], BONE[0], 7, 1.8)
    c.ellipse((74, 74, 90, 90), BONE[0], 2.0)
    shaded(c, [(14, 40), (24, 22), (46, 14), (66, 24), (72, 46), (62, 66), (40, 72), (20, 62)], (HIDE[2], HIDE[1], HIDE[2]), True)
    c.poly([(18, 42), (26, 26), (46, 18), (62, 28), (66, 46), (58, 62), (40, 66), (24, 58)], (170, 96, 52), 1.0, True)
    c.shine([(26, 28), (44, 20), (52, 24), (34, 38)], (255, 220, 160, 110))
    for x, y in ((32, 50), (48, 44), (44, 58), (56, 36)):
        c.line([(x, y), (x + 6, y - 4)], (96, 52, 28), 2.2, 0.0)


def salve_ember(c):
    c.rrect((28, 30, 72, 82), 9, GLASS[2], 2.2)
    c.rrect((32, 46, 68, 78), 7, SALVE[0], 1.0)
    c.shine([(34, 48), (42, 48), (42, 72), (34, 72)], (255, 255, 255, 80))
    c.rrect((34, 16, 66, 32), 2, WOOD[0], 2.0)
    c.line([(38, 22), (62, 22)], WOOD[2], 1.2, 0.0)
    # the ember
    c.poly([(50, 34), (58, 46), (50, 58), (42, 46)], EMBER[0], 1.6)
    c.poly([(50, 42), (54, 48), (50, 54), (46, 48)], EMBER[1], 0.0)


def pelt(c, tones, spots=None, small=False):
    k = 0.8 if small else 1.0
    cx, cy = 50, 52

    def m(x, y):
        return (cx + (x - cx) * k, cy + (y - cy) * k)
    outline = [m(x, y) for x, y in [(16, 24), (34, 20), (50, 12), (66, 20), (84, 26), (76, 44), (88, 58), (74, 66), (80, 86), (60, 78), (50, 90),
                                    (40, 78), (20, 86), (26, 66), (12, 58), (24, 44)]]
    c.poly(outline, tones[0], 2.2, True)
    c.shine([m(30, 28), m(48, 22), m(44, 56), m(28, 52)], tones[1] + (140,))
    c.shine([m(56, 50), m(74, 54), m(70, 76), m(56, 70)], tones[2] + (150,))
    for x, y in (spots or []):
        sx, sy = m(x, y)
        c.dot(sx, sy, 3.2 * k, tones[2])


def barrowback_hide(c):
    pelt(c, DARKHIDE, [(40, 40), (58, 46), (46, 62), (64, 68), (34, 56)])
    c.line([(50, 24), (50, 74)], (30, 24, 22), 2.4, 0.0)


def deer_hide(c):
    pelt(c, HIDE, [(36, 36), (56, 44), (42, 58), (62, 62), (50, 74), (30, 52), (66, 34)])
    for x, y in ((36, 36), (56, 44), (42, 58), (62, 62), (50, 74), (30, 52), (66, 34)):
        c.dot(x, y, 2.6, (242, 232, 214))


def fellhorn_hide(c):
    pelt(c, GREYHIDE)
    c.line([(36, 32), (44, 54)], (60, 62, 68), 1.8, 0.0)
    c.line([(58, 30), (52, 56)], (60, 62, 68), 1.8, 0.0)
    c.line([(46, 58), (50, 78)], (60, 62, 68), 1.8, 0.0)


def small_hide(c):
    pelt(c, HIDE, small=True)


def bog_cotton(c):
    c.line([(50, 90), (48, 40)], MOSS[0], 3.2, 1.4)
    c.line([(50, 70), (34, 56)], MOSS[0], 2.4, 1.2)
    for x, y, r in ((50, 26, 12), (36, 40, 9), (64, 38, 9), (30, 24, 7), (70, 24, 7)):
        c.ellipse((x - r, y - r, x + r, y + r), SILK[0], 1.8)
        c.dot(x - r * 0.3, y - r * 0.3, r * 0.35, SILK[1])
    c.dot(50, 30, 2, SILK[2])


def boneleaf(c):
    shaded(c, [(50, 10), (70, 24), (74, 50), (62, 76), (50, 90), (38, 76), (26, 50), (30, 24)], BONE, True, light=[(40, 24), (48, 16), (46, 60), (36, 52)],
           shade=[(58, 30), (70, 40), (66, 66), (56, 76)])
    c.line([(50, 14), (50, 88)], BONE[2], 2.2, 0.0)
    for y in (32, 46, 60):
        c.line([(50, y), (38, y - 8)], BONE[2], 1.4, 0.0)
        c.line([(50, y), (62, y - 8)], BONE[2], 1.4, 0.0)


def cinder_sac(c):
    shaded(c, [(36, 18), (64, 18), (60, 30), (76, 46), (80, 70), (66, 86), (34, 86), (20, 70), (24, 46), (40, 30)], DARKHIDE, True,
           light=[(30, 50), (38, 38), (42, 72), (30, 74)], shade=[(60, 50), (74, 56), (70, 80), (58, 82)])
    c.line([(38, 28), (62, 28)], LEATHER[0], 4.5, 1.4)
    c.line([(44, 24), (44, 18)], LEATHER[2], 2, 0.8)
    c.line([(56, 24), (56, 18)], LEATHER[2], 2, 0.8)
    for x, y in ((38, 56), (54, 50), (46, 68), (62, 64)):
        c.dot(x, y, 3.6, EMBER[2])
        c.dot(x, y, 2.2, EMBER[1])


def feathers(c):
    def feather(base, tip, w, tone):
        bx, by = base
        tx, ty = tip
        dx, dy = tx - bx, ty - by
        ln = math.hypot(dx, dy)
        nx, ny = -dy / ln, dx / ln
        pts = [(bx, by)]
        for i in range(1, 8):
            t = i / 8
            wi = w * math.sin(math.pi * min(1, t * 1.15) ** 0.8)
            pts.append((bx + dx * t + nx * wi, by + dy * t + ny * wi))
        pts.append((tx, ty))
        for i in range(7, 0, -1):
            t = i / 8
            wi = w * math.sin(math.pi * min(1, t * 1.15) ** 0.8)
            pts.append((bx + dx * t - nx * wi, by + dy * t - ny * wi))
        c.poly(pts, tone[0], 1.8, True)
        c.line([base, (bx + dx * 0.96, by + dy * 0.96)], tone[2], 1.0, 0.0)
        c.line([base, (bx + dx * 0.2, by + dy * 0.2)], BONE[0], 1.8, 0.8)
    feather((46, 88), (22, 14), 9, GREYHIDE)
    feather((52, 88), (50, 8), 10, BONE)
    feather((58, 88), (82, 18), 9, DARKHIDE)


def herb_woundmoss(c):
    shaded(c, [(14, 62), (22, 46), (40, 38), (58, 36), (78, 44), (88, 60), (80, 76), (56, 82), (30, 80)], MOSS, True,
           light=[(26, 52), (44, 42), (56, 42), (40, 54)], shade=[(60, 62), (82, 62), (76, 76), (56, 80)])
    for x, y in ((30, 62), (44, 52), (60, 56), (72, 66), (50, 70), (36, 72)):
        c.dot(x, y, 3.4, MOSS[1])
    for x, y in ((46, 48), (66, 60), (34, 66)):
        c.dot(x, y, 2.6, SALVE[1])


def spider_silk(c):
    for r, rot in ((26, 0), (20, 40), (14, 80)):
        c.d.arc((50 * U - r * U * 1.25, 50 * U - r * U, 50 * U + r * U * 1.25, 50 * U + r * U), rot, rot + 300, fill=OUT, width=int(5 * U))
    for r, rot in ((26, 0), (20, 40), (14, 80)):
        c.d.arc((50 * U - r * U * 1.25, 50 * U - r * U, 50 * U + r * U * 1.25, 50 * U + r * U), rot, rot + 300, fill=SILK[0], width=int(2.6 * U))
    c.ellipse((40, 40, 60, 60), SILK[0], 1.8)
    c.line([(60, 58), (84, 80)], SILK[0], 2.6, 1.2)
    c.shine([(42, 42), (50, 42), (46, 50)], (255, 255, 255, 140))


def timber(c):
    def log(cx, cy, r):
        c.poly([(cx - 34, cy - r), (cx + 6, cy - r), (cx + 6, cy + r), (cx - 34, cy + r)], WOOD[0], 2.0)
        c.ellipse((cx - r + 6 - r * 0.3, cy - r, cx + r + 6 - r * 0.3, cy + r), (206, 160, 104), 2.0)
        c.ellipse((cx - r * 0.55 + 6 - r * 0.3, cy - r * 0.55, cx + r * 0.55 + 6 - r * 0.3, cy + r * 0.55), (176, 126, 76), 1.0)
        c.dot(cx + 6 - r * 0.3, cy, 2, WOOD[2])
    log(40, 62, 13)
    log(66, 62, 13)
    log(53, 38, 13)
    for cx, cy in ((40, 62), (66, 62), (53, 38)):
        c.line([(cx - 30, cy - 7), (cx - 8, cy - 7)], WOOD[2], 1.0, 0.0)


def codex_fragment(c):
    shaded(c, [(18, 30), (46, 14), (74, 20), (84, 46), (70, 68), (78, 84), (44, 88), (24, 72), (14, 52)], STONE, False,
           light=[(24, 32), (46, 20), (40, 40), (22, 50)], shade=[(60, 54), (80, 52), (74, 82), (56, 84)])
    for pts in ([(36, 40), (50, 40), (50, 52)], [(40, 62), (56, 56), (62, 66)], [(58, 34), (68, 38), (66, 48)]):
        c.line(pts, STONE[2], 3.2, 0.0, cap=False)
    c.dot(54, 52, 2.4, EMBER[0])


def folded_note(c):
    # a letter folded in thirds, with a wax seal
    shaded(c, [(14, 30), (74, 18), (86, 62), (28, 80)], PAPER, False, light=[(16, 32), (34, 28), (42, 76), (30, 78)], shade=[(60, 22), (74, 20), (86, 62), (72, 66)])
    c.line([(34, 27), (46, 75)], PAPER[2], 1.4, 0.0, cap=False)
    c.line([(54, 23), (66, 71)], PAPER[2], 1.4, 0.0, cap=False)
    for k in (0, 1):
        c.line([(20 + k * 0, 40 + k * 8), (30, 38 + k * 8)], (120, 100, 78), 1.2, 0.0)
    c.ellipse((46, 44, 62, 60), RED[0], 2.0)
    c.ellipse((50, 48, 58, 56), RED[1], 0.8)
    c.line([(54, 50), (54, 54)], RED[2], 1.0, 0.0, cap=False)


def lantern_foot(c):
    # the brass foot of a lantern: a flared base with a ring socket
    c.rrect((18, 74, 82, 88), 3, BRASS[0], 2.2)
    c.shine([(20, 76), (46, 76), (46, 80), (20, 80)], BRASS[1] + (200,))
    shaded(c, [(36, 74), (40, 44), (60, 44), (64, 74)], BRASS, False, light=[(38, 72), (42, 48), (48, 48), (46, 72)], shade=[(56, 48), (60, 46), (62, 72), (56, 72)])
    c.rrect((32, 36, 68, 48), 3, BRASS[0], 2.0)
    c.d.ellipse((38 * U, 16 * U, 62 * U, 38 * U), outline=BRASS[0], width=int(4 * U))
    c.d.ellipse((36 * U, 14 * U, 64 * U, 40 * U), outline=OUT, width=int(1.6 * U))
    c.d.ellipse((42 * U, 20 * U, 58 * U, 34 * U), outline=OUT, width=int(1.6 * U))


def book(c, cover, mark=None, ribbon=None):
    c.poly([(24, 14), (76, 14), (80, 18), (80, 86), (76, 88), (24, 88)], cover[0], 2.2)
    c.poly([(24, 14), (34, 14), (34, 88), (24, 88)], cover[2], 1.6)
    c.shine([(38, 18), (46, 18), (46, 84), (38, 84)], cover[1] + (110,))
    c.poly([(76, 20), (80, 18), (80, 86), (76, 88)], PAPER[0], 1.2)
    c.line([(34, 26), (70, 26)], BRASS[0], 1.8, 0.8)
    c.line([(34, 76), (70, 76)], BRASS[0], 1.8, 0.8)
    if ribbon:
        c.poly([(62, 86), (70, 86), (70, 96), (66, 92), (62, 96)], ribbon, 1.4)
    if mark:
        mark(c)


def lantern_ledger(c):
    book(c, ((84, 62, 44), (124, 92, 62), (56, 40, 28)), ribbon=RED[0])
    c.rrect((44, 38, 66, 62), 2, PAPER[0], 1.4)
    for y in (45, 50, 55):
        c.line([(48, y), (62, y)], (120, 100, 78), 1.0, 0.0, cap=False)


def bench_patterns(c):
    def hammer(c):
        c.line([(46, 62), (60, 38)], WOOD[0], 4.2, 1.2)
        c.poly([(46, 34), (66, 32), (68, 42), (50, 46)], IRON[0], 1.5)
    book(c, ((70, 82, 100), (106, 120, 142), (46, 56, 72)), hammer)


def herbal_remedies(c):
    def leaf(c):
        c.poly([(54, 36), (66, 44), (62, 62), (52, 66), (44, 56), (46, 42)], GREEN[0], 1.6, True)
        c.line([(52, 40), (52, 66)], GREEN[2], 1.2, 0.0)
    book(c, ((64, 100, 74), (96, 140, 106), (40, 66, 48)), leaf)


def fitting_wedge(c):
    shaded(c, [(14, 70), (74, 36), (86, 44), (86, 76), (14, 76)], IRON, False, light=[(18, 72), (72, 40), (76, 44), (30, 72)], shade=[(60, 54), (86, 46), (86, 76), (60, 76)])
    c.rrect((74, 34, 90, 78), 2, DARKIRON[0], 1.8)
    c.line([(28, 70), (66, 50)], IRON[2], 1.2, 0.0, cap=False)


def brass_key(c):
    c.d.ellipse((14 * U, 14 * U, 46 * U, 46 * U), fill=OUT)
    c.d.ellipse((16 * U, 16 * U, 44 * U, 44 * U), fill=BRASS[0])
    c.d.ellipse((24 * U, 24 * U, 36 * U, 36 * U), fill=OUT)
    c.d.ellipse((26 * U, 26 * U, 34 * U, 34 * U), fill=(88, 88, 90))
    c.line([(40, 40), (82, 82)], BRASS[0], 7, 1.6)
    c.line([(70, 70), (78, 62)], BRASS[0], 5, 1.4)
    c.line([(78, 78), (86, 70)], BRASS[0], 5, 1.4)
    c.shine([(20, 18), (34, 16), (28, 24)], BRASS[1] + (200,))
    c.line([(44, 42), (76, 74)], BRASS[1], 1.6, 0.0)


def glass_marble(c):
    c.ellipse((16, 16, 84, 84), GLASS[0], 2.4)
    c.shine([(26, 40), (30, 28), (44, 22), (40, 34)], (255, 255, 255, 190))
    c.shine([(52, 62), (74, 60), (68, 74), (54, 76)], GLASS[2] + (150,))
    # a twist of colour inside the glass
    c.line([(32, 56), (44, 46), (60, 52), (70, 40)], (216, 112, 62), 3.6, 0.0)
    c.line([(32, 66), (50, 58), (68, 64)], (236, 190, 70), 2.6, 0.0)
    c.d.ellipse((16 * U, 16 * U, 84 * U, 84 * U), outline=OUT, width=int(2.4 * U))


def stopped_watch(c):
    c.d.arc((40 * U, 6 * U, 60 * U, 24 * U), 180, 360, fill=OUT, width=int(5 * U))
    c.d.arc((42 * U, 8 * U, 58 * U, 22 * U), 180, 360, fill=BRASS[0], width=int(2.2 * U))
    c.rrect((44, 16, 56, 26), 2, BRASS[0], 1.8)
    c.ellipse((14, 20, 86, 90), BRASS[0], 2.4)
    c.ellipse((20, 26, 80, 84), PAPER[0], 1.8)
    for i in range(12):
        a = i * math.pi / 6
        x0, y0 = 50 + math.sin(a) * 26, 55 - math.cos(a) * 26
        x1, y1 = 50 + math.sin(a) * 22, 55 - math.cos(a) * 22
        c.line([(x0, y0), (x1, y1)], OUT, 1.4, 0.0, cap=False)
    c.line([(50, 55), (50, 36)], OUT, 2.0, 0.0)
    c.line([(50, 55), (62, 62)], OUT, 2.0, 0.0)
    c.dot(50, 55, 2.4, BRASS[0])
    c.shine([(24, 32), (44, 28), (30, 46)], (255, 255, 255, 110))
    # the crack: a stopped watch has taken a knock
    c.line([(60, 32), (56, 44), (64, 52)], (60, 50, 44), 1.0, 0.0, cap=False)


def unreadable_page(c):
    shaded(c, [(20, 14), (68, 14), (80, 28), (80, 88), (20, 88)], PAPER, False, light=[(22, 16), (34, 16), (34, 86), (22, 86)], shade=[(66, 20), (78, 28), (78, 86), (66, 86)])
    c.poly([(68, 14), (80, 28), (68, 28)], PAPER[2], 1.6)
    for y in (36, 44, 52, 60, 68, 76):
        w = 52 if y % 16 else 40
        c.line([(28, y), (28 + w * 0.8, y)], (122, 104, 82), 1.7, 0.0)
        c.line([(28 + 6, y + 1.4), (28 + w * 0.8 - 4, y + 1.4)], (170, 150, 120), 0.9, 0.0, cap=False)
    # a blot over the lines
    c.poly([(44, 46), (58, 42), (66, 54), (56, 66), (42, 62)], (74, 56, 46), 0.0, True)


def panel_chat(c):
    shaded(c, [(10, 14), (66, 14), (66, 56), (36, 56), (22, 70), (24, 56), (10, 56)], CLOTH, False, light=[(12, 16), (64, 16), (64, 24), (12, 24)])
    for y, w in ((28, 40), (38, 30)):
        c.line([(18, y), (18 + w, y)], PAPER[0], 2.6, 0.0)
    shaded(c, [(48, 40), (90, 40), (90, 76), (82, 76), (84, 90), (68, 76), (48, 76)], BRASS, False, light=[(50, 42), (88, 42), (88, 48), (50, 48)])
    for y, w in ((54, 28), (62, 20)):
        c.line([(56, y), (56 + w, y)], OUT, 2.4, 0.0)


def panel_reputation(c):
    # a standard: a banner on a pole, over a pair of scales' balance beam
    c.line([(50, 10), (50, 90)], WOOD[0], 4.2, 1.4)
    c.ellipse((46, 6, 54, 14), BRASS[0], 1.6)
    shaded(c, [(50, 18), (82, 18), (82, 54), (66, 46), (50, 54)], RED, False, light=[(52, 20), (62, 20), (62, 50), (52, 52)], shade=[(72, 20), (82, 20), (82, 54), (72, 49)])
    c.line([(22, 62), (78, 62)], BRASS[0], 3.2, 1.4)
    for x in (24, 76):
        c.line([(x, 62), (x - 8, 80)], BRASS[2], 1.2, 0.8, cap=False)
        c.line([(x, 62), (x + 8, 80)], BRASS[2], 1.2, 0.8, cap=False)
        c.poly([(x - 12, 80), (x + 12, 80), (x + 8, 86), (x - 8, 86)], BRASS[0], 1.8)
    c.rrect((40, 84, 60, 90), 1.5, WOOD[2], 1.6)


def panel_credits(c):
    # a scroll with a star: who made it
    c.rrect((24, 20, 76, 80), 2, PAPER[0], 2.2)
    for y in (66,):
        c.line([(32, y), (68, y)], (130, 110, 84), 1.6, 0.0)
    c.ellipse((16, 12, 34, 30), PAPER[2], 2.0)
    c.ellipse((66, 70, 84, 88), PAPER[2], 2.0)
    c.d.arc((18 * U, 14 * U, 32 * U, 28 * U), 20, 300, fill=OUT, width=int(1.2 * U))
    c.d.arc((68 * U, 72 * U, 82 * U, 86 * U), 20, 300, fill=OUT, width=int(1.2 * U))
    star = []
    for i in range(10):
        r = 15 if i % 2 == 0 else 6.5
        a = -math.pi / 2 + i * math.pi / 5
        star.append((50 + r * math.cos(a), 42 + r * math.sin(a)))
    c.poly(star, BRASS[0], 1.8)
    c.shine([(50, 28), (54, 40), (46, 40)], BRASS[1] + (190,))
    c.line([(34, 58), (66, 58)], (130, 110, 84), 1.6, 0.0)


# the icon id -> (art binding key, drawing, what it is, note)
def skill_bow(c):
    c.line([(30, 14), (22, 50), (30, 86)], WOOD[0], 6, 1.8)
    c.line([(30, 14), (30, 86)], PAPER[2], 1.2, 0.0, cap=False)
    c.line([(30, 50), (82, 50)], WOOD[2], 2.6, 1.0)
    c.poly([(82, 44), (92, 50), (82, 56)], IRON[0], 1.6)
    c.poly([(30, 46), (22, 42), (28, 50), (22, 58), (30, 54)], PAPER[0], 1.2)


def skill_polearm(c):
    c.line([(18, 86), (72, 28)], WOOD[0], 5, 1.8)
    c.poly([(66, 34), (80, 8), (90, 30), (80, 40)], IRON[0], 1.8)
    c.shine([(70, 32), (80, 14), (82, 30)], IRON[1] + (200,))
    c.line([(60, 40), (74, 44)], DARKIRON[0], 3, 1.0)


def effect_fed(c):
    c.ellipse((16, 52, 84, 86), WOOD[0], 2.2)
    c.ellipse((22, 54, 78, 72), HIDE[1], 1.2)
    c.poly([(30, 56), (44, 30), (60, 34), (70, 56)], (176, 104, 58), 2.0, True)
    c.shine([(38, 52), (46, 38), (54, 40)], (255, 255, 255, 90))
    c.line([(40, 24), (44, 12)], BONE[2], 1.6, 0.0)
    c.line([(54, 26), (58, 14)], BONE[2], 1.6, 0.0)


def effect_ember_mending(c):
    c.poly([(50, 10), (72, 44), (64, 78), (50, 90), (36, 78), (28, 44)], EMBER[0], 2.2, True)
    c.poly([(50, 36), (60, 58), (50, 80), (40, 58)], EMBER[1], 1.0, True)
    c.line([(50, 50), (50, 72)], BONE[1], 2.4, 0.0)
    c.line([(40, 61), (60, 61)], BONE[1], 2.4, 0.0)


ICONS = {
    "ui.skill.bow": ("skill.bow", skill_bow, "Bow skill."),
    "ui.skill.polearm": ("skill.polearm", skill_polearm, "Polearm skill."),
    "ui.status.fed": ("effect.fed", effect_fed, "Fed effect."),
    "ui.status.ember_mending": ("effect.ember_mending", effect_ember_mending, "Ember mending effect."),
    "ui.item.cloak": ("item.armor.cloak", cloak, "Travelling cloak."),
    "ui.item.hide_boots": ("item.armor.hide_boots", hide_boots, "Hide boots."),
    "ui.item.smith_gloves": ("item.armor.smith_gloves", smith_gloves, "Smith's gloves."),
    "ui.item.timber_shield": ("item.armor.timber_shield", timber_shield, "Round timber shield."),
    "ui.item.wool_trousers": ("item.armor.wool_trousers", wool_trousers, "Wool trousers."),
    "ui.item.roast_meat": ("item.consumable.roast_meat", roast_meat, "Roast meat."),
    "ui.item.salve_ember": ("item.consumable.salve_ember", salve_ember, "Ember salve."),
    "ui.item.barrowback_hide": ("item.material.barrowback_hide", barrowback_hide, "Barrowback hide."),
    "ui.item.bog_cotton": ("item.material.bog_cotton", bog_cotton, "Bog cotton."),
    "ui.item.boneleaf": ("item.material.boneleaf", boneleaf, "Boneleaf."),
    "ui.item.cinder_sac": ("item.material.cinder_sac", cinder_sac, "Cinder sac."),
    "ui.item.deer_hide": ("item.material.deer_hide", deer_hide, "Deer hide."),
    "ui.item.feathers": ("item.material.feathers", feathers, "Feathers."),
    "ui.item.fellhorn_hide": ("item.material.fellhorn_hide", fellhorn_hide, "Fellhorn hide."),
    "ui.item.woundmoss": ("item.material.herb_woundmoss", herb_woundmoss, "Woundmoss."),
    "ui.item.small_hide": ("item.material.small_hide", small_hide, "Small hide."),
    "ui.item.spider_silk": ("item.material.spider_silk", spider_silk, "Spider silk."),
    "ui.item.timber": ("item.material.timber", timber, "Timber."),
    "ui.item.codex_fragment": ("item.quest.barrowworks_codex_fragment", codex_fragment, "Barrowworks codex fragment."),
    "ui.item.folded_note": ("item.story.folded_note", folded_note, "Folded Note."),
    "ui.item.lantern_foot": ("item.artefact.lantern_foot", lantern_foot, "Lantern foot."),
    "ui.item.lantern_ledger": ("item.story.lantern_ledger", lantern_ledger, "Lantern ledger."),
    "ui.item.bench_patterns": ("item.tome.bench_patterns", bench_patterns, "Bench patterns."),
    "ui.item.herbal_remedies": ("item.tome.herbal_remedies", herbal_remedies, "Herbal remedies."),
    "ui.item.fitting_wedge": ("item.tool.fitting_wedge", fitting_wedge, "Fitting wedge."),
    "ui.item.brass_key": ("item.story.brass_key", brass_key, "Brass key."),
    "ui.item.glass_marble": ("item.story.glass_marble", glass_marble, "Glass marble."),
    "ui.item.stopped_watch": ("item.story.stopped_watch", stopped_watch, "Stopped watch."),
    "ui.item.unreadable_page": ("item.story.unreadable_page", unreadable_page, "Unreadable page."),
    "ui.panel.chat": ("panel.chat", panel_chat, "System Menu: Chat."),
    "ui.panel.reputation": ("panel.reputation", panel_reputation, "System Menu: Reputation."),
    "ui.panel.credits": ("panel.credits", panel_credits, "System Menu: Credits."),
}


RENDERS = {
    "rusted_sword": ("ui.item.rusted_sword", "Arming sword, rendered from its in-game model weapon_real_arming_sword."),
    "hunting_bow": ("ui.item.hunting_bow", "Longbow, rendered from its in-game model weapon_real_longbow (also Halda's Bow)."),
    "march_spear": ("ui.item.march_spear", "Hunting spear, rendered from its in-game model weapon_real_hunting_spear."),
}

# Rendered from the staged P0 models (tools/p0_bind/stage_p0_assets.py): icon id -> staged model, so the tile shows what the item drops as.
P0_RENDERS = {
    "wolf_hide": ("wolf_hide_folded", "Wolf hide"), "deer_hide": ("deer_hide_folded", "Deer hide"), "small_hide": ("small_hide_folded", "Small hide"),
    "fellhorn_hide": ("fellhorn_hide_folded", "Fellhorn hide"), "barrowback_hide": ("barrowback_hide_folded", "Barrowback hide"),
    "hide_boots": ("hide_boots_pair", "Hide boots"), "timber_shield": ("timber_shield_equipped", "Timber shield"),
    "cinder_sac": ("cinder_sac", "Cinder sac"), "iron_ingot": ("iron_ingot", "Iron ingot"), "spider_silk": ("spider_silk_hank", "Spider silk hank"),
    "wolf_fang": ("wolf_fang", "Wolf fang"), "torch": ("item_torch", "Torch"), "water_flask": ("item_water_flask", "Water flask"),
    "salve_ember": ("item_salve_ember", "Ember salve pot"), "brass_key": ("brass_key", "Brass key"), "glass_marble": ("glass_marble", "Glass marble"),
    "lantern_ledger": ("lantern_ledger", "Lantern ledger"), "stopped_watch": ("stopped_watch", "Stopped watch"), "unreadable_page": ("unreadable_page", "Unreadable page"),
}
for _icon, (_model, _what) in P0_RENDERS.items():
    RENDERS["p0_" + _icon] = ("ui.item." + _icon, f"{_what}, rendered from its in-game model {_model}.")


def fit_render(path):
    """A transparent model render, cropped to its pixels and scaled to fill most of the tile."""
    im = Image.open(path).convert("RGBA")
    im = im.crop(im.getchannel("A").getbbox())
    k = 0.86 * S / max(im.size)
    im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)
    out = Image.new("RGBA", (S, S))
    out.alpha_composite(im, ((S - im.width) // 2, (S - im.height) // 2))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--renders", help="a folder of transparent model renders (render_item_icons.gd): <name>.png -> RENDERS[name]")
    ap.add_argument("--install", help="an asset root: merge the icons into its ui/icons and manifests/ui_icons.json")
    args = ap.parse_args()
    icons_dir = os.path.join(args.out, "ui", "icons")
    os.makedirs(icons_dir, exist_ok=True)
    entries = {}
    sheet_cols = 8
    rows = math.ceil(len(ICONS) / sheet_cols)
    jobs = [(icon_id, note, draw, None, "hand-drawn vector") for icon_id, (_key, draw, note) in ICONS.items()]
    if args.renders:
        for name, (icon_id, note) in RENDERS.items():
            jobs.append((icon_id, note, None, fit_render(os.path.join(args.renders, name + ".png")), "render of the owned model"))
    rows = math.ceil(len(jobs) / sheet_cols)
    sheet = Image.new("RGB", (sheet_cols * 256, rows * 256), (40, 40, 40))
    for n, (icon_id, note, draw, overlay, how) in enumerate(jobs):
        img = tile(draw, overlay=overlay)
        stem = "ui_" + icon_id[3:].replace(".", "_")
        big = img.resize((256, 256), Image.LANCZOS)
        small = img.resize((128, 128), Image.LANCZOS)
        big.save(os.path.join(icons_dir, stem + "_256.png"))
        small.save(os.path.join(icons_dir, stem + "_128.png"))
        sheet.paste(big, ((n % sheet_cols) * 256, (n // sheet_cols) * 256))
        entries[icon_id] = {
            "slot": icon_id, "concept": ("render_" if overlay is not None else "hand_drawn_vector_") + stem[3:], "source": "model-render" if overlay is not None else "hand-drawn",
            "sprite": f"ui/icons/{stem}_128.png", "sprite_px": 128, "master": f"ui/icons/{stem}_256.png", "master_px": 256,
            "alpha": "none", "note": note + f" {how} (tools/ui_icons); no generative AI.",
        }
    sheet.save(os.path.join(args.out, "contact_sheet.png"))
    with open(os.path.join(args.out, "ui_icons_additions.json"), "w", encoding="utf-8") as f:
        json.dump({"icons": entries}, f, indent=2)
    print(f"drew {len(entries)} icons into {icons_dir}")
    if args.install:
        manifest_path = os.path.join(args.install, "manifests", "ui_icons.json")
        with open(manifest_path, encoding="utf-8") as f:
            manifest = json.load(f)
        shutil.copyfile(manifest_path, manifest_path + ".before_missing_icons_20261018")
        manifest["icons"].update(entries)
        manifest["summary"] = {**manifest.get("summary", {}), "slots": len(manifest["icons"])}
        os.makedirs(os.path.join(args.install, "ui", "icons"), exist_ok=True)
        for name in os.listdir(icons_dir):
            shutil.copyfile(os.path.join(icons_dir, name), os.path.join(args.install, "ui", "icons", name))
        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2)
        print(f"installed into {args.install}")


if __name__ == "__main__":
    main()
