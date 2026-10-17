"""WP17 step 2: the Veth builds - five per sex - as their own models, made from the archived Creator presets (plan 2.18 step 1).

python build_bodies.py --sex m --shapes <shapes_m.npz> --archive <creator_archive> --assembled <work/assembled> --out <dir>
    [--builds builds.json]

The presets carried onto the Veth skeleton (bl_preset_shapes.py) differ from the Veth body only in soft tissue. Per vertex, that
difference is fitted linearly on the Creator's own sliders for each preset (Fat, Muscularity, Masculine/Feminine, from the archive's
recipes). Because every vertex shares one design matrix, each slider's direction is one fixed weighted blend of the presets - the
weights are printed - so a build is still a mix of real Creator bodies, now moved along the Creator's Fat and Muscularity axes from
the owner's blend (Blend_M = Victor x Trey, Blend_F = Sunita x Tuya), never scaled.

A build is (fat, muscularity) away from the Average body (builds.json). Hands and feet keep the Average shape (sockets, grips and
boots), faded in over the wrist and ankle; the field is smoothed a little so one preset's quirks do not print through. The skeleton is
the Average's, unchanged: fat and muscle move no joint, every clip and corrective plays as is, and the build's rest offsets are zero
(recorded in the body content so a later people with other proportions can carry its own).

Each build's model is the Average's assembled .glb with new vertex positions and normals - body LOD0-3, the shadow proxy and the
neck of the head (the head presets' own blend, below the jaw, taking the body's move where the head's rim lies on it) - written into the same bytes: textures, face
shapes, grooms and skin weights are the Average's. Textures and the correctives are hard links to the Average's files.
Out: <out>/mh_veth_<sex>_<build>/ like an assembled asset, plus <asset>_build.json (sliders, girths, max move).
"""
import argparse
import json
import os
import shutil
import sys

import numpy as np
from scipy.spatial import cKDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb import Glb  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument("--sex", required=True, choices=["m", "f"])
ap.add_argument("--shapes", required=True)
ap.add_argument("--heads", required=True, help="bl_preset_shapes.py --part head output, for the neck")
ap.add_argument("--archive", required=True, help="creator_archive (recipes/stock_<preset>.json)")
ap.add_argument("--assembled", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--builds", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "builds.json"))
ap.add_argument("--only", default="", help="comma list of build ids (default: every build but average)")
a = ap.parse_args()

REGIONS = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "regions.json")))["regions"]
SLIDERS = ("Fat", "Muscularity", "Masculine/Feminine")
KEEP_SHAPE = ("hand_l", "hand_r", "foot_l", "foot_r")


def sliders(name):
    m = json.load(open(os.path.join(a.archive, "recipes", f"stock_{name}.json")))["measurements"]
    return [m[k] for k in SLIDERS]


def link(src, dst):
    """A hard link to the Average's file (same drive), else a copy."""
    try:
        os.link(src, dst)
    except OSError:
        shutil.copy2(src, dst)


def neighbours(tris, n):
    e = np.vstack([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]])
    e = np.vstack([e, e[:, ::-1]])
    deg = np.bincount(e[:, 0], minlength=n)
    return e, np.maximum(deg, 1)


def smooth(field, edges, deg, iterations, lam=0.5):
    for _ in range(iterations):
        avg = np.zeros_like(field)
        np.add.at(avg, edges[:, 0], field[edges[:, 1]])
        avg /= deg[:, None] if field.ndim == 2 else deg
        field = field + lam * (avg - field)
    return field


def vertex_normals(pos, tris):
    """Area-weighted normals, welded by position (glTF splits vertices at seams)."""
    _, weld = np.unique(np.round(pos / 1e-6).astype(np.int64), axis=0, return_inverse=True)
    weld = weld.ravel()
    face = np.cross(pos[tris[:, 1]] - pos[tris[:, 0]], pos[tris[:, 2]] - pos[tris[:, 0]])
    acc = np.zeros((weld.max() + 1, 3))
    for k in range(3):
        np.add.at(acc, weld[tris[:, k]], face)
    n = acc[weld]
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)


def rotate(vectors, n_from, n_to):
    """Rotate each vector by the rotation taking n_from to n_to (Rodrigues), so authored normals and tangents keep their detail."""
    axis = np.cross(n_from, n_to)
    s = np.linalg.norm(axis, axis=1, keepdims=True)
    c = np.sum(n_from * n_to, axis=1, keepdims=True)
    k = axis / np.maximum(s, 1e-12)
    v = vectors
    out = v * c + np.cross(k, v) * s + k * np.sum(k * v, axis=1, keepdims=True) * (1 - c)
    return np.where(s > 1e-9, out, v)


def cross2(u, v):
    return u[0] * v[1] - u[1] * v[0]


def girths(pos, joint):
    """Tape girths (cm) of the body mesh - convex hull of a horizontal slice of the trunk's (or left thigh's) region vertices - at heights
    found from the skeleton: chest (largest between spine_04 and spine_05), waist (smallest between pelvis and spine_03), hips
    (largest from 10 cm under the hip joints to the pelvis) and thigh (largest 4-10 cm under the hip joint)."""
    def hull(p):
        p = np.unique(np.round(p, 5), axis=0)
        p = p[np.lexsort((p[:, 1], p[:, 0]))]

        def half(points):
            out = []
            for q in points:
                while len(out) >= 2 and cross2(out[-1] - out[-2], q - out[-2]) <= 0:
                    out.pop()
                out.append(q)
            return out
        h = np.array(half(p)[:-1] + half(p[::-1])[:-1])
        return float(np.linalg.norm(np.roll(h, -1, 0) - h, axis=1).sum())

    def torso(z, band=0.006):
        return hull(pos[TORSO & (np.abs(pos[:, 2] - z) < band)][:, :2])

    def thigh(z):
        return hull(pos[THIGH & (np.abs(pos[:, 2] - z) < 0.006)][:, :2])

    zs = lambda lo, hi: np.linspace(lo, hi, 24)
    hip = (joint["thigh_l"][2] + joint["thigh_r"][2]) / 2
    out = {"chest": max(torso(z) for z in zs(joint["spine_04"][2], joint["spine_05"][2])),
           "waist": min(torso(z) for z in zs(joint["pelvis"][2] + 0.04, joint["spine_03"][2])),
           "hips": max(torso(z) for z in zs(hip - 0.10, joint["pelvis"][2])),
           "thigh": max(thigh(z) for z in zs(hip - 0.10, hip - 0.04))}
    return {k: round(v * 100, 1) for k, v in out.items()}


shapes = np.load(a.shapes)
target = shapes["target"].astype(np.float64)
tris = shapes["tris"].astype(np.int64)
names = [str(n) for n in shapes["names"]]
use = [i for i, n in enumerate(names) if not n.startswith("Blend_")]
X = (shapes["carried"][use].astype(np.float64) - target).reshape(len(use), -1)
A = np.array([[1.0] + sliders(names[i]) for i in use])
mix = np.linalg.pinv(A)                      # rows: intercept, Fat, Muscularity, M/F - each a fixed blend of the presets
B = mix @ X
D = {"fat": B[1].reshape(-1, 3), "muscularity": B[2].reshape(-1, 3)}
blend_weights = {k: {names[i]: round(float(w), 3) for i, w in zip(use, mix[r])} for k, r in (("fat", 1), ("muscularity", 2))}

src_asset = f"mh_veth_{a.sex}"
src_dir = os.path.join(a.assembled, src_asset)
src_glb = os.path.join(src_dir, f"{src_asset}_rigged.glb")
base = Glb(src_glb)
body_prim = base.primitives(f"{src_asset}_body")[0]
lod0 = base.read(body_prim["attributes"]["POSITION"])
regions = np.rint(base.read(body_prim["attributes"]["TEXCOORD_1"])[:, 0]).astype(int)

# Blender world (Z up, the FBX the presets came in) -> the glb's mesh space: one rigid map, fitted on the shared vertex order
ct, cp = target.mean(0), lod0.mean(0)
U, S, Vt = np.linalg.svd((target - ct).T @ (lod0 - cp))
R = Vt.T @ np.diag([1, 1, np.sign(np.linalg.det(Vt.T @ U.T))]) @ U.T
fit_residual = float(np.abs((target - ct) @ R.T + cp - lod0).max())
if fit_residual > 1e-4:
    raise SystemExit(f"{src_asset}: the FBX body and the glb body are not the same mesh (residual {fit_residual:.4f} m)")

TORSO = np.isin(regions, [REGIONS[r] for r in ("upper_chest", "belly", "back", "pelvis", "glutes", "upper_chest_top", "back_top")])
THIGH = regions == REGIONS["thigh_l"]
# The body is several shells whose seams share positions (1857 vertices): fields are smoothed on the welded surface, so a seam
# never opens into a crease.
_, weld = np.unique(np.round(target / 1e-5).astype(np.int64), axis=0, return_inverse=True)
weld = weld.ravel()
n_weld = weld.max() + 1
count = np.bincount(weld, minlength=n_weld).astype(np.float64)


def welded(field):
    acc = np.zeros((n_weld,) + field.shape[1:])
    np.add.at(acc, weld, field)
    return acc / (count[:, None] if field.ndim == 2 else count)


edges, deg = neighbours(weld[tris], n_weld)
keep = welded(np.isin(regions, [REGIONS[r] for r in KEEP_SHAPE]).astype(np.float64))
mask = 1.0 - np.clip(smooth(np.ceil(keep), edges, deg, 12, 1.0) * 1.6, 0, 1)
mask[keep > 0] = 0.0
# Every Creator body is mirror-symmetric about x = 0 (in the FBX frame): the fields are made so too, so no one preset's
# asymmetry (a larger breast, a lower shoulder) is carried into a build.
FLIP = np.array([-1.0, 1.0, 1.0])


def symmetric(field, pos):
    mirror = cKDTree(pos).query(pos * FLIP)[1]
    return (field + field[mirror] * FLIP) / 2


for k in D:
    D[k] = (smooth(welded(symmetric(D[k], target)), edges, deg, 16) * mask[:, None])[weld]

# The neck lies on the head mesh: the head presets' own blend along the same sliders, kept to the head's neck regions (neck,
# upper_chest_top, back_top) and softened over the jaw line, so the face and the mouth's inside never change.
heads = np.load(a.heads)
h_target = heads["target"].astype(np.float64)
hX = (heads["carried"][[list(heads["names"]).index(names[i]) for i in use]].astype(np.float64) - h_target).reshape(len(use), -1)
hB = mix @ hX
head_pos = (h_target - ct) @ R.T + cp
head_skin = base.primitives(f"{src_asset}_head")[0]["attributes"]
h_dist, h_idx = cKDTree(base.read(head_skin["POSITION"])).query(head_pos)
h_region = np.where(h_dist < 1e-4, np.rint(base.read(head_skin["TEXCOORD_1"])[:, 0]).astype(int)[h_idx], REGIONS["head"])
_, h_weld = np.unique(np.round(h_target / 1e-5).astype(np.int64), axis=0, return_inverse=True)
h_weld = h_weld.ravel()
h_count = np.bincount(h_weld).astype(np.float64)
h_edges, h_deg = neighbours(h_weld[heads["tris"].astype(np.int64)], h_weld.max() + 1)
neck_regions = np.isin(h_region, [REGIONS[r] for r in ("neck", "upper_chest_top", "back_top")]).astype(np.float64)
nw = np.zeros(h_weld.max() + 1)
np.add.at(nw, h_weld, neck_regions)
neck = np.clip(smooth(nw / h_count, h_edges, h_deg, 8, 1.0), 0, 1)[h_weld]
HD = {}
for k, r in (("fat", 1), ("muscularity", 2)):
    f = np.zeros((h_weld.max() + 1, 3))
    np.add.at(f, h_weld, symmetric(hB[r].reshape(-1, 3), h_target))
    HD[k] = (smooth(f / h_count[:, None], h_edges, h_deg, 30)[h_weld]) * neck[:, None]
# where the head's rim lies on the body (it overlaps the collar) it takes the body's own move, handing over to the neck's blend
# over 4 cm up from the body's surface
body_dist, body_idx = cKDTree(lod0).query(head_pos, k=4)
body_w = 1.0 / np.maximum(body_dist, 1e-6)
on_body = np.clip(1.0 - (body_dist[:, 0] - 0.003) / 0.04, 0.0, 1.0)
on_body = on_body * on_body * (3 - 2 * on_body)
builds = json.load(open(a.builds))
joint = {str(n): h.astype(np.float64) for n, h in zip(shapes["joint_names"], shapes["joint_heads"])}
os.makedirs(a.out, exist_ok=True)
summary = {"sex": a.sex, "source": src_asset, "presets": [names[i] for i in use], "blend_weights": blend_weights,
           "average_girths_cm": girths(target, joint), "builds": {}}
wanted = [b for b in builds["builds"] if b["id"] != "average" and (not a.only or b["id"] in a.only.split(","))]
for build in wanted:
    fat, musc = build["sliders"][a.sex]["fat"], build["sliders"][a.sex]["muscularity"]
    delta_b = fat * D["fat"] + musc * D["muscularity"]
    shaped_b = target + delta_b
    delta = delta_b @ R.T                      # glb mesh space
    asset = f"{src_asset}_{build['id']}"
    # one skin field in glb space: the body exactly, the head's neck (its own preset blend, faded out under the jaw, equal to the
    # body's where the head's rim lies on it); every other mesh - LODs, shadow proxy, head parts, beard and hair cards - follows its nearest points
    hd = (fat * HD["fat"] + musc * HD["muscularity"]) @ R.T
    from_body = (delta[body_idx] * body_w[..., None]).sum(1) / body_w.sum(1, keepdims=True)
    hd = on_body[:, None] * from_body + (1 - on_body[:, None]) * hd
    field_pos = np.vstack([lod0, head_pos])
    field = np.vstack([delta, hd])
    tree = cKDTree(field_pos)
    g = Glb(src_glb)
    for node in g.doc["nodes"]:
        name = node.get("name", "")
        if "mesh" not in node or not name.startswith(src_asset):
            continue
        for prim in g.doc["meshes"][node["mesh"]]["primitives"]:
            at = prim["attributes"]
            pos = g.read(at["POSITION"])
            if name == f"{src_asset}_body":
                d = delta
            else:
                dist, idx = tree.query(pos, k=4)
                w = 1.0 / np.maximum(dist, 1e-6)
                d = (field[idx] * w[..., None]).sum(1) / w.sum(1, keepdims=True)
            if not np.any(np.abs(d) > 1e-7):
                continue
            new = pos + d
            tri = g.read(prim["indices"]).astype(np.int64).reshape(-1, 3)
            n0, n1 = vertex_normals(pos, tri), vertex_normals(new, tri)
            g.write(at["POSITION"], new)
            if "NORMAL" in at:
                g.write(at["NORMAL"], rotate(g.read(at["NORMAL"]), n0, n1))
            if "TANGENT" in at:
                t = g.read(at["TANGENT"])
                t[:, :3] = rotate(t[:, :3], n0, n1)
                g.write(at["TANGENT"], t)
    folder = os.path.join(a.out, asset)
    os.makedirs(folder, exist_ok=True)
    for sub in ("textures", "eye_shader"):
        for root, _, files in os.walk(os.path.join(src_dir, sub)):
            dest = os.path.join(folder, os.path.relpath(root, src_dir))
            os.makedirs(dest, exist_ok=True)
            for f in files:
                if not os.path.exists(os.path.join(dest, f)):
                    link(os.path.join(root, f), os.path.join(dest, f))
    for n in g.doc["nodes"]:
        if n.get("name", "").startswith(src_asset):
            n["name"] = asset + n["name"][len(src_asset):]
    g.save(os.path.join(folder, f"{asset}_rigged.glb"))
    corr = os.path.join(src_dir, f"{src_asset}_correctives.json")
    if os.path.exists(corr) and not os.path.exists(os.path.join(folder, f"{asset}_correctives.json")):
        link(corr, os.path.join(folder, f"{asset}_correctives.json"))
    move = np.linalg.norm(delta_b, axis=1)
    info = {"asset": asset, "build": build["id"], "label": build["label"], "sliders_from_average": {"fat": fat, "muscularity": musc},
            "girths_cm": girths(shaped_b, joint), "max_move_mm": round(float(move.max()) * 1000, 1),
            "mean_move_mm": round(float(move.mean()) * 1000, 1), "skeleton": f"{src_asset}'s, unchanged (rest offsets zero)",
            "source": f"{src_asset}_rigged.glb + Creator preset blend along Fat/Muscularity (build_bodies.py)"}
    json.dump(info, open(os.path.join(folder, f"{asset}_build.json"), "w"), indent=1)
    summary["builds"][build["id"]] = info
    print("BUILD", asset, info["girths_cm"], "max move", info["max_move_mm"], "mm")
json.dump(summary, open(os.path.join(a.out, f"builds_{a.sex}.json"), "w"), indent=1)
print("BLEND", json.dumps(blend_weights))
print("AVERAGE", summary["average_girths_cm"])
