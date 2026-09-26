"""Review sheets for the procedural trees and their LOD chains, rendered with Eevee (_render_tree_lods.py) and composed
here.

Per tree, one JPEG:
  row 1  the tree as the player meets it: 10 m with LOD0, 30 m with LOD1, 80 m with LOD3 (the impostor)
  row 2  each switch at the recommended bands (LOD0|LOD1 at 25 m, LOD1|LOD2 at 50 m, LOD2|LOD3 at 80 m): one camera,
         the left half of the frame from the nearer level, the right half from the farther
  row 3  the same switches where the game's ArtLibrary.ModelWithLods makes them today (4x / 10x / 22x the model's
         largest extent)
Every panel says its distance, level, triangles and the scale it is shown at (1.0x = the pixels a 1920x1080 player
camera with a 75 deg vertical field of view gives it). Plus a lineup of all the trees (LOD0 at 30 m, LOD3 at 80 m) and a
small grove. JPEGs are kept under --max-kb.

  python tools/asset_pipeline/_tree_review_sheets.py --ids flora_oak_tree,flora_pine_tree,... \
      --lods assets/_staging/lods_procgen --out docs/phase_b/demo/trees
"""
import argparse
import io
import json
import math
import os
import struct
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
BLENDER = os.environ.get("UNNAMED_BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
RES = (1920, 1080)
FOV = 75.0
EYE = 1.7
CAM_AZ = 20.0
PANEL = 440
BANDS = (25.0, 50.0, 80.0)
# the game's sun casts shadows out to 60-120 m (RenderTiers.cs); panels this far or farther are rendered without them
NO_SHADOW_M = 75.0


def glb_info(path):
    data = open(path, "rb").read()
    n = struct.unpack_from("<I", data, 12)[0]
    g = json.loads(data[20:20 + n])
    tris, lo, hi = 0, [1e9] * 3, [-1e9] * 3
    for m in g["meshes"]:
        for p in m["primitives"]:
            tris += g["accessors"][p["indices"]]["count"] // 3
            a = g["accessors"][p["attributes"]["POSITION"]]
            lo = [min(x, y) for x, y in zip(lo, a["min"])]
            hi = [max(x, y) for x, y in zip(hi, a["max"])]
    # Blender frame (x, -z, y)
    return {"tris": tris, "lo": (lo[0], -hi[2], lo[1]), "hi": (hi[0], -lo[2], hi[1]),
            "size": max(h - l for h, l in zip(hi, lo))}


def camera(dist, look_h, az=CAM_AZ, target=(0.0, 0.0)):
    return {"dist": dist, "height": EYE, "look_h": look_h, "azimuth_deg": az, "target": list(target)}


def look_height(dist, top):
    """Aim so a tree of height `top` fits: at 10 m the player looks up into the crown."""
    return min(top * 0.55, EYE + dist * 0.55) if dist < 20 else top * 0.45


def project(cam, p):
    az = math.radians(cam["azimuth_deg"])
    tx, ty = cam["target"]
    pos = (tx + cam["dist"] * math.sin(az), ty - cam["dist"] * math.cos(az), cam["height"])
    look = (tx, ty, cam["look_h"])
    f = [look[i] - pos[i] for i in range(3)]
    fl = math.sqrt(sum(x * x for x in f))
    f = [x / fl for x in f]
    r = (f[1] * 1 - f[2] * 0, f[2] * 0 - f[0] * 1, 0.0)
    rl = math.sqrt(sum(x * x for x in r))
    r = [x / rl for x in r]
    u = (r[1] * f[2] - r[2] * f[1], r[2] * f[0] - r[0] * f[2], r[0] * f[1] - r[1] * f[0])
    v = [p[i] - pos[i] for i in range(3)]
    xc, yc, zc = (sum(v[i] * r[i] for i in range(3)), sum(v[i] * u[i] for i in range(3)),
                  sum(v[i] * f[i] for i in range(3)))
    t = math.tan(math.radians(FOV) / 2)
    return RES[0] / 2 + xc / zc / t * RES[1] / 2, RES[1] / 2 - yc / zc / t * RES[1] / 2


def crop_box(cam, info, at=(0.0, 0.0), margin=0.06):
    lo, hi = info["lo"], info["hi"]
    pts = [project(cam, (at[0] + x, at[1] + y, z)) for x in (lo[0], hi[0]) for y in (lo[1], hi[1])
           for z in (lo[2], hi[2])]
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    side = min(RES[1], max(max(xs) - min(xs), max(ys) - min(ys)) * (1 + 2 * margin))
    # a square inside the frame (up close the tree is larger than the frame: the frame's own height is shown)
    cx = min(max(cx, side / 2), RES[0] - side / 2)
    cy = min(max(cy, side / 2), RES[1] - side / 2)
    return cx - side / 2, cy - side / 2, cx + side / 2, cy + side / 2


def font(size):
    for f in ("arialbd.ttf", "arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            continue
    return ImageFont.load_default()


def label(im, lines, pos=(8, 6), size=15):
    d = ImageDraw.Draw(im)
    f = font(size)
    y = pos[1]
    for ln in lines:
        w = d.textlength(ln, font=f)
        d.rectangle((pos[0] - 3, y - 1, pos[0] + w + 4, y + size + 3), fill=(0, 0, 0))
        d.text((pos[0], y), ln, fill=(255, 255, 255), font=f)
        y += size + 5


def panel(path, box, text, split_right=None, split_labels=None):
    im = Image.open(path).convert("RGB")
    if split_right is not None:
        right = Image.open(split_right).convert("RGB")
        mid = int(round((box[0] + box[2]) / 2))
        im.paste(right.crop((mid, 0, RES[0], RES[1])), (mid, 0))
    side = box[2] - box[0]
    scale = PANEL / side
    sub = im.crop(tuple(int(round(v)) for v in box))
    sub = sub.resize((PANEL, PANEL), Image.NEAREST if scale > 1.6 else Image.LANCZOS)
    if split_right is not None:
        d = ImageDraw.Draw(sub)
        d.line((PANEL // 2, 0, PANEL // 2, PANEL), fill=(255, 220, 0), width=1)
        f = font(14)
        x = PANEL // 2 - 8 - d.textlength(split_labels[0], font=f)
        d.text((x, PANEL - 22), split_labels[0], fill=(255, 220, 0), font=f)
        d.text((PANEL // 2 + 8, PANEL - 22), split_labels[1], fill=(255, 220, 0), font=f)
    label(sub, text + [f"shown {scale:.2f}x"])
    return sub


def save_jpeg(im, path, max_kb):
    for q in (90, 86, 82, 78, 74, 70, 65, 60):
        buf = io.BytesIO()
        im.save(buf, "JPEG", quality=q, optimize=True, progressive=True)
        if buf.tell() <= max_kb * 1024:
            break
    with open(path, "wb") as fh:
        fh.write(buf.getvalue())
    return q, buf.tell() // 1024


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ids", required=True)
    ap.add_argument("--lods", default=os.path.join(REPO, "assets", "_staging", "lods_procgen"))
    ap.add_argument("--out", default=os.path.join(REPO, "docs", "phase_b", "demo", "trees"))
    ap.add_argument("--work", default=os.path.join(tempfile.gettempdir(), "tree_review_sheets"))
    ap.add_argument("--max-kb", type=int, default=580)
    ap.add_argument("--skip-render", action="store_true")
    a = ap.parse_args()
    ids = a.ids.split(",")
    os.makedirs(a.out, exist_ok=True)
    os.makedirs(a.work, exist_ok=True)
    trees = {}
    for i in ids:
        ready = os.path.join(REPO, "assets", "ready", i, i + ".glb")
        staged = os.path.join(REPO, "assets", "_staging", "procedural", i, i + ".glb")
        lod = [ready if os.path.exists(ready) else staged] + \
              [os.path.join(a.lods, i, f"{i}_lod{n}.glb") for n in (1, 2, 3)]
        trees[i] = {"glb": lod, "info": [glb_info(p) for p in lod]}
    shots, plan = [], {}
    for i, t in trees.items():
        info0 = t["info"][0]
        top = info0["hi"][2]
        size = info0["size"]
        game = (max(8.0, 4 * size), max(20.0, 10 * size), max(45.0, 22 * size))
        p = plan[i] = {"row1": [], "row2": [], "row3": [], "game": game}
        for dist, lvl in ((10.0, 0), (30.0, 1), (80.0, 3)):
            cam = camera(dist, look_height(dist, top))
            out = os.path.join(a.work, f"{i}_r1_{int(dist)}_l{lvl}.png")
            shots.append({"out": out, "models": [{"glb": t["glb"][lvl], "shadow": dist < NO_SHADOW_M}], "cam": cam})
            p["row1"].append((out, cam, dist, lvl))
        for row, dists in (("row2", BANDS), ("row3", game)):
            for n, dist in enumerate(dists):
                cam = camera(dist, look_height(dist, top))
                pair = []
                for lvl in (n, n + 1):
                    out = os.path.join(a.work, f"{i}_{row}_{int(dist)}_l{lvl}.png")
                    shots.append({"out": out, "models": [{"glb": t["glb"][lvl], "shadow": dist < NO_SHADOW_M}],
                                  "cam": cam})
                    pair.append(out)
                p[row].append((pair, cam, dist, n))
        for dist, lvl in ((30.0, 0), (80.0, 3)):
            out = os.path.join(a.work, f"{i}_line_{int(dist)}_l{lvl}.png")
            cam = camera(dist, look_height(dist, top))
            shots.append({"out": out, "models": [{"glb": t["glb"][lvl], "shadow": dist < NO_SHADOW_M}], "cam": cam})
            p.setdefault("line", []).append((out, cam, dist, lvl))
    # a small grove: every tree once, LOD0, loosely spaced
    grove_at = [(-33, 34), (-19, 22), (-7, 40), (5, 26), (17, 44), (27, 30), (-26, 52), (37, 50)]
    grove = [{"glb": trees[i]["glb"][0], "at": list(grove_at[k % len(grove_at)]), "yaw_deg": 40 * k}
             for k, i in enumerate(ids)]
    grove_out = os.path.join(a.work, "grove.png")
    shots.append({"out": grove_out, "models": grove, "cam": {"dist": 42, "height": EYE, "look_h": 6.0,
                                                             "azimuth_deg": 0, "target": [0, 30]}})
    job = os.path.join(a.work, "job.json")
    json.dump({"res": list(RES), "fov_deg": FOV, "shots": shots}, open(job, "w"), indent=1)
    if not a.skip_render:
        r = subprocess.run([BLENDER, "--background", "--factory-startup", "--python",
                            os.path.join(TOOL_DIR, "_render_tree_lods.py"), "--", "--job", job],
                           capture_output=True, text=True)
        if "DONE" not in r.stdout:
            print(r.stdout[-3000:], r.stderr[-3000:])
            raise SystemExit("render failed")
    results = {}
    for i, t in trees.items():
        p = plan[i]
        tris = [x["tris"] for x in t["info"]]
        head = Image.new("RGB", (4 * PANEL, 64), (24, 26, 30))
        d = ImageDraw.Draw(head)
        d.text((10, 6), i, fill=(255, 255, 255), font=font(24))
        d.text((10, 38), f"triangles LOD0-3: {' / '.join(f'{x:,}' for x in tris)}    height "
                         f"{t['info'][0]['hi'][2]:.1f} m    largest extent {t['info'][0]['size']:.1f} m",
               fill=(210, 210, 210), font=font(16))
        sheet = Image.new("RGB", (4 * PANEL, 64 + 3 * PANEL), (24, 26, 30))
        sheet.paste(head, (0, 0))
        for k, (out, cam, dist, lvl) in enumerate(p["row1"]):
            box = crop_box(cam, t["info"][0])
            sheet.paste(panel(out, box, [f"{dist:g} m  LOD{lvl}  {tris[lvl]:,} tris"]), (k * PANEL, 64))
        for r_i, row in enumerate(("row2", "row3")):
            for k, (pair, cam, dist, n) in enumerate(p[row]):
                box = crop_box(cam, t["info"][0])
                title = "recommended switch" if row == "row2" else "game's switch today"
                sheet.paste(panel(pair[0], box, [f"{dist:.0f} m  {title}"], split_right=pair[1],
                                  split_labels=(f"LOD{n}", f"LOD{n + 1}")), (k * PANEL, 64 + (r_i + 1) * PANEL))
        side = Image.new("RGB", (PANEL, PANEL * 3), (24, 26, 30))
        d = ImageDraw.Draw(side)
        f = font(14)
        notes = ["row 1: the matching level at 10/30/80 m",
                 "row 2: switches at the recommended", "bands: LOD0 <25 m, LOD1 25-50 m,", "LOD2 50-80 m, LOD3 >80 m",
                 "(left half nearer level, right farther)",
                 "row 3: the same switches where", "ArtLibrary.ModelWithLods puts them", "today: " +
                 " / ".join(f"{g:.0f}" for g in p["game"]) + " m",
                 "1.0x = 1920x1080 player camera,", "75 deg vertical FOV, eye 1.7 m;", "zoomed panels use nearest",
                 "pixels (true aliasing)",
                 f"from {NO_SHADOW_M:.0f} m no sun shadows (the game's", "shadow range: 60-120 m by render tier)"]
        for k, ln in enumerate(notes):
            d.text((12, 12 + k * 19), ln, fill=(220, 220, 220), font=f)
        sheet.paste(side.crop((0, 0, PANEL, PANEL)), (3 * PANEL, 64))
        blank = side.crop((0, PANEL, PANEL, 3 * PANEL))
        sheet.paste(blank, (3 * PANEL, 64 + PANEL))
        # the right column of rows 2-3: the 80 m LOD2 and LOD3 whole, unsplit, for the impostor's own look
        pair, cam, dist, n = p["row2"][2]
        box = crop_box(cam, t["info"][0])
        sheet.paste(panel(pair[0], box, [f"{dist:.0f} m  LOD2 whole"]), (3 * PANEL, 64 + PANEL))
        sheet.paste(panel(pair[1], box, [f"{dist:.0f} m  LOD3 whole"]), (3 * PANEL, 64 + 2 * PANEL))
        path = os.path.join(a.out, f"{i}_lods.jpg")
        results[path] = save_jpeg(sheet, path, a.max_kb)
    # lineup
    W = 300
    line = Image.new("RGB", (W * len(ids), 50 + 2 * (W + 60) + 480), (24, 26, 30))
    d = ImageDraw.Draw(line)
    title = "Charwood trees: LOD0 at 30 m (top), LOD3 impostor at 80 m (middle), all eight as a grove (bottom)"
    d.text((10, 12), title, fill=(255, 255, 255), font=font(20))
    for k, i in enumerate(ids):
        t = trees[i]
        for r_i, (out, cam, dist, lvl) in enumerate(plan[i]["line"]):
            # one metric scale per row, so heights compare: a 16 m square at the tree
            box = crop_box(cam, {"lo": (-8, -8, 0), "hi": (8, 8, 16)}, margin=0.0)
            im = Image.open(out).convert("RGB").crop(tuple(int(round(v)) for v in box))
            sc = W / im.width
            im = im.resize((W, W), Image.NEAREST if sc > 1.6 else Image.LANCZOS)
            y = 50 + r_i * (W + 60)
            line.paste(im, (k * W, y))
            d.text((k * W + 6, y + W + 4), i.replace("flora_", ""), fill=(230, 230, 230), font=font(15))
            d.text((k * W + 6, y + W + 24), f"LOD{lvl} {t['info'][lvl]['tris']:,} tris  {sc:.2f}x",
                   fill=(180, 180, 180), font=font(13))
    g = Image.open(grove_out).convert("RGB")
    g = g.crop((0, int(g.height * 0.28), g.width, int(g.height * 0.72)))
    g = g.resize((W * len(ids), int(g.height * W * len(ids) / g.width)), Image.LANCZOS)
    line = line.crop((0, 0, line.width, 50 + 2 * (W + 60) + g.height))
    line.paste(g, (0, 50 + 2 * (W + 60)))
    path = os.path.join(a.out, "trees_lineup.jpg")
    results[path] = save_jpeg(line, path, a.max_kb)
    for pth, (q, kb) in results.items():
        print(f"SHEET {pth} quality {q} {kb} KB")


if __name__ == "__main__":
    sys.exit(main())
