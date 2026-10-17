"""WP17 body acceptance test (owner, 2026-10-17): the torso's front-view width every 2 cm from the shoulder to the hip, normalised to
its shoulder width, for a body at rest or posed, against the unmodified MetaHuman posed the same way in UE.

python torso_profile.py --plot <png> --json <out json> <label>=<glb>[@<pose json>|@<posed obj>] ...

  <glb>                 a skinned body (the assembled game body, or the UE export's full rig as glb); its LOD0 body mesh
  @<pose json>          ue_pose_record.py's record: every joint's local as Epic's rig posed it (UE frame, cm), applied as the change
                        from the record's own rest, mirrored into the glTF frame, then linear blend skinning
  @<posed obj>          mh_gate.py --dump's posed body (the game's own posing: the clip, correctives and splay)

Torso vertices are those whose skin weight on the arm chains (clavicle outward) is under 0.5. The profile is taken square to the
hips (the thigh joints' axis), from the shoulder (the torso's highest vertex under the neck band) down to the hip joints.
"""
import argparse
import json
import re
import sys

import numpy as np

sys.path.insert(0, __file__.rsplit("\\", 1)[0] if "\\" in __file__ else __file__.rsplit("/", 1)[0])
from glb import Glb  # noqa: E402

ARM = re.compile(r"^(upperarm|lowerarm|hand|thumb|index|middle|ring|pinky|wrist)\w*_[lr]$")
STEP = 0.02


def quat_matrix(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def body(path):
    g = Glb(path)
    d = g.doc
    node = next(n for n in d["nodes"] if "mesh" in n and "skin" in n and re.search(r"(_body|Body_LOD0)$", n.get("name", "")))
    prim = d["meshes"][node["mesh"]]["primitives"][0]
    at = prim["attributes"]
    J, W = g.read(at["JOINTS_0"]).astype(int), g.read(at["WEIGHTS_0"])
    if "JOINTS_1" in at:
        J, W = np.hstack([J, g.read(at["JOINTS_1"]).astype(int)]), np.hstack([W, g.read(at["WEIGHTS_1"])])
    skin = d["skins"][node["skin"]]
    return g, g.read(at["POSITION"]), J, W / W.sum(1, keepdims=True), skin


def posed_by_record(path, record):
    g, P, J, W, skin = body(path)
    d = g.doc
    rec = json.load(open(record))
    cm = any(abs(n.get("scale", [1, 1, 1])[0] - 0.01) < 1e-4 for n in d["nodes"])   # the export's rig (Blender's FBX import) is in cm
    parent = {c: i for i, n in enumerate(d["nodes"]) for c in n.get("children", [])}
    local = {}
    for i, n in enumerate(d["nodes"]):
        t = np.array(n.get("translation", [0, 0, 0]), float)
        q = tuple(n.get("rotation", [0, 0, 0, 1]))
        name = n.get("name")
        if name in rec["posed"] and name != "pelvis" and name in rec["rest"]:
            (tr, qr), (tp, qp) = rec["rest"][name], rec["posed"][name]
            dq = qmul((-qr[0], -qr[1], -qr[2], qr[3]), tuple(qp))
            q = qmul(q, (-dq[0], dq[1], -dq[2], dq[3]))
            dt = np.array([tp[0] - tr[0], -(tp[1] - tr[1]), tp[2] - tr[2]])
            t = t + dt * (1.0 if cm else 0.01)
        elif name == "pelvis" and name in rec["rest"]:
            (tr, qr), (tp, qp) = rec["rest"][name], rec["posed"][name]
            dq = qmul((-qr[0], -qr[1], -qr[2], qr[3]), tuple(qp))
            q = qmul(q, (-dq[0], dq[1], -dq[2], dq[3]))
        m = np.eye(4)
        m[:3, :3] = quat_matrix(q) * np.array(n.get("scale", [1, 1, 1]))
        m[:3, 3] = t
        local[i] = m
    world = {}

    def g_of(i):
        if i not in world:
            world[i] = (g_of(parent[i]) if i in parent else np.eye(4)) @ local[i]
        return world[i]

    ibm = g.read(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
    mats = np.stack([g_of(j) for j in skin["joints"]]) @ ibm
    homo = np.hstack([P, np.ones((len(P), 1))])
    out = np.zeros_like(P)
    for k in range(J.shape[1]):
        out += W[:, k:k + 1] * np.einsum("nij,nj->ni", mats[J[:, k]], homo)[:, :3]
    return out, J, W, skin, g


def arm_weight(J, W, skin, g):
    names = [g.doc["nodes"][j].get("name", "") for j in skin["joints"]]
    arm = np.array([bool(ARM.match(n)) for n in names])
    return (W * arm[J]).sum(1)


def joint_world(g, name):
    d = g.doc
    parent = {c: i for i, n in enumerate(d["nodes"]) for c in n.get("children", [])}
    i = next(k for k, n in enumerate(d["nodes"]) if n.get("name") == name)
    m = np.eye(4)
    while True:
        n = d["nodes"][i]
        l = np.eye(4)
        l[:3, :3] = quat_matrix(n.get("rotation", [0, 0, 0, 1])) * np.array(n.get("scale", [1, 1, 1]))
        l[:3, 3] = n.get("translation", [0, 0, 0])
        m = l @ m
        if i not in parent:
            return m[:3, 3]
        i = parent[i]


def profile(P, torso, thighs):
    """Width every STEP down from the shoulder line to the hips, square to the hips; glTF frame (Y up). Returns (widths, shoulder)."""
    across = thighs[0] - thighs[1]
    yaw = np.arctan2(across[2], across[0])
    c, s = np.cos(-yaw), np.sin(-yaw)
    Q = P.copy()
    Q[:, 0], Q[:, 2] = P[:, 0] * c - P[:, 2] * s, P[:, 0] * s + P[:, 2] * c
    T = Q[torso]
    hip = (thighs[0][1] + thighs[1][1]) / 2
    top = np.percentile(T[:, 1], 99.0)          # the shoulder's top (the neck band is head, not body)
    shoulder = top - 0.04
    rows = []
    y = shoulder
    while y > hip:
        m = np.abs(T[:, 1] - y) < STEP / 2
        rows.append((round(float(shoulder - y), 3), float(T[m, 0].max() - T[m, 0].min()) if m.sum() > 10 else float("nan")))
        y -= STEP
    widths = np.array([w for _, w in rows])
    return rows, widths / widths[0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--plot", required=True)
    ap.add_argument("--json", required=True)
    ap.add_argument("items", nargs="+")
    a = ap.parse_args()
    from PIL import Image, ImageDraw
    out = {}
    lines = []
    for item in a.items:
        label, rest = item.split("=", 1)
        path, _, pose = rest.partition("@")
        g, P, J, W, skin = body(path)
        scale = 1.0
        if pose.endswith(".json"):
            P, J, W, skin, g = posed_by_record(path, pose)
        elif pose.endswith(".obj"):
            P = np.array([[float(x) for x in l.split()[1:4]] for l in open(pose) if l.startswith("v ")])[:len(P)]
        thighs = [joint_world(g, f"thigh_{s}") for s in "lr"]
        if pose.endswith(".obj"):   # the game's posed dump: the hips' joints are not in it; square by the posed pelvis band
            band = P[(P[:, 1] > 0.85) & (P[:, 1] < 1.0)]
            left = band[band[:, 0] > band[:, 0].mean()].mean(0)
            right = band[band[:, 0] <= band[:, 0].mean()].mean(0)
            thighs = [left, right]
        torso = arm_weight(J, W, skin, g) < 0.5
        rows, norm = profile(P * scale, torso, [t * scale for t in thighs])
        out[label] = {"rows_m": rows, "normalised": [round(float(x), 4) for x in norm], "shoulder_width_cm": round(rows[0][1] * 100, 1),
                      "widest_below_shoulder_at_cm": round(float(rows[int(np.nanargmax([w for _, w in rows[1:]])) + 1][0]) * 100, 1)}
        lines.append((f"{label} (shoulder {rows[0][1] * 100:.1f} cm)", norm, [r[0] * 100 for r in rows]))
    # x: width / shoulder width 0.5-1.2; y: 0-50 cm below the shoulder line
    W, H, L, T = 900, 900, 90, 40
    im = Image.new("RGB", (W, H + 40 * len(lines) + 40), "white")
    dr = ImageDraw.Draw(im)
    px = lambda x: L + (x - 0.5) / 0.7 * (W - L - 30)  # noqa: E731
    py = lambda y: T + y / 50 * (H - T - 60)  # noqa: E731
    for x in np.arange(0.5, 1.21, 0.1):
        dr.line([(px(x), py(0)), (px(x), py(50))], fill=(225, 225, 225))
        dr.text((px(x) - 10, py(50) + 6), f"{x:.1f}", fill="black")
    for y in range(0, 51, 10):
        dr.line([(px(0.5), py(y)), (px(1.2), py(y))], fill=(225, 225, 225))
        dr.text((10, py(y) - 6), f"{y} cm", fill="black")
    dr.text((L, H - 25), "torso width / shoulder width (front view, arms excluded); down: cm below the shoulder line", fill="black")
    colours = [(30, 90, 200), (220, 60, 40), (40, 160, 70), (150, 60, 180), (230, 150, 20), (20, 160, 170)]
    for k, (label, norm, ys) in enumerate(lines):
        pts = [(px(x), py(y)) for x, y in zip(norm, ys) if np.isfinite(x)]
        dr.line(pts, fill=colours[k % len(colours)], width=3)
        dr.text((L, H + 10 + 40 * k), label, fill=colours[k % len(colours)])
    im.save(a.plot)
    json.dump(out, open(a.json, "w"), indent=1)
    for k, v in out.items():
        print(k, "shoulder", v["shoulder_width_cm"], "widest below at", v["widest_below_shoulder_at_cm"], "profile", v["normalised"][:12])


if __name__ == "__main__":
    main()
