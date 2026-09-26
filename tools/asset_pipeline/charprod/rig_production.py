"""Blender: the production humanoid rig on a character already skinned to the fitted humanoid20 rig - the same 20 bones
(names, rest, the fitted weights the clips and gameplay code already rely on) plus the bones a contemporary RPG body
needs: a middle spine (spine_mid, three spine segments like the UE-mannequin/UAL skeleton), a toe per foot, and three
bones per finger with the thumb (thumb/index/middle/ring/pinky _01.._03 .L/.R). The eye bones eyes.py/assemble.py add
are kept. Mesh and garments are not touched: the rig is fitted to them.

  * Finger joints: the concept's hand landmarks (hand_landmarks.py, MediaPipe's 21 per hand) ray-cast onto the hand
    through the concept camera and pushed into the finger by its half-thickness; the hand the detector missed is the
    found one mirrored across the body's midplane. The landmark wrist is shifted onto the rig's wrist.
  * Each finger bone is rolled so its local X is the finger's bend axis (palm normal x finger direction), so a grip is a
    rotation about X.
  * Weights are redistributed, never re-solved: a hand's weight moves onto the finger segments along each finger past
    its knuckle; spine and chest weight is shared out with spine_mid by height; a foot's weight moves onto the toe past
    the ball of the foot. Four influences, normalised.

    blender --background --factory-startup --python rig_production.py -- --input character_rigged.glb --camera camera.json
        --hands hands.json --out character_prod_rigged.glb [--report r.json]
"""
import argparse
import json
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

FINGERS = ("thumb", "index", "middle", "ring", "pinky")
# MediaPipe hand indices: wrist 0; thumb 1-4 (cmc, mcp, ip, tip); the others mcp, pip, dip, tip.
LM = {"thumb": [1, 2, 3, 4], "index": [5, 6, 7, 8], "middle": [9, 10, 11, 12], "ring": [13, 14, 15, 16], "pinky": [17, 18, 19, 20]}
THICK = {"wrist": 0.02, "thumb": 0.009, "index": 0.008, "middle": 0.008, "ring": 0.0075, "pinky": 0.007}


def args():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--camera", required=True)
    ap.add_argument("--hands", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--ball", type=float, default=0.68, help="the ball of the foot, as a fraction from ankle to toe tip")
    ap.add_argument("--report")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:])


def to_blender(p):
    return Vector((p[0], -p[2], p[1]))


def main():
    a = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.input)
    obj = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups][0]
    for o in [o for o in bpy.data.objects if o.type == "MESH" and o is not obj]:
        bpy.data.objects.remove(o)
    arm = obj.find_armature()
    me = obj.data
    mw = obj.matrix_world
    cam = json.load(open(a.camera))
    R, t, f, c = np.array(cam["R"]), np.array(cam["t"]), cam["f"], np.array(cam["c"])
    C = to_blender(-R.T @ t)
    groups = {g.name: g.index for g in obj.vertex_groups}
    report = {}

    def ray(px):
        d = R.T @ np.array([(px[0] - c[0]) / f, (px[1] - c[1]) / f, 1.0])
        return to_blender(d / np.linalg.norm(d))

    bones = {b.name: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones}
    x_mid = bones["hips"][0].x

    # Vertex positions (bind pose) and weights.
    P = np.array([mw @ v.co for v in me.vertices])
    Wd = {}
    for v in me.vertices:
        for g in v.groups:
            Wd.setdefault(obj.vertex_groups[g.group].name, np.zeros(len(me.vertices)))[v.index] = g.weight

    # --- finger joints from the hand landmarks ---
    hands = json.load(open(a.hands))["hands"]
    joints = {}
    lifted = None
    for img_side, rig_side in (("image_left", "R"), ("image_right", "L")):
        h = hands.get(img_side)
        if not h or h.get("handedness") == "mirrored":
            continue
        hand_w = Wd.get(f"hand.{rig_side}", np.zeros(len(P)))
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.transform(mw)
        bm.verts.ensure_lookup_table()
        keep = {i for i in np.nonzero(hand_w > 0.3)[0]}
        bmesh.ops.delete(bm, geom=[fc for fc in bm.faces if not all(v.index in keep for v in fc.verts)], context="FACES")
        bvh = BVHTree.FromBMesh(bm)
        pts = []
        for i, px in enumerate(h["points"]):
            d = ray(px)
            hit = bvh.ray_cast(C, d, 10.0)
            name = "wrist" if i == 0 else next(k for k, ids in LM.items() if i in ids)
            if hit[0] is None:
                # past the silhouette: the point on the ray nearest the hand's surface
                best = None
                for s in np.linspace(1.0, 4.0, 400):
                    q = C + d * s
                    near = bvh.find_nearest(q)
                    if near[0] is not None and (best is None or near[3] < best[1]):
                        best = (near[0], near[3])
                pts.append(best[0] + d * THICK[name])
            else:
                pts.append(hit[0] + d * THICK[name])
        bm.free()
        pts = np.array([list(p) for p in pts])
        shift = np.array(list(bones[f"hand.{rig_side}"][0])) - pts[0]
        report[f"hand.{rig_side}_landmark_wrist_offset_m"] = round(float(np.linalg.norm(shift)), 4)
        pts = pts + shift
        joints[rig_side] = pts
        lifted = rig_side
    assert lifted, "no hand landmarks to fit fingers to"
    other = "L" if lifted == "R" else "R"
    if other not in joints:
        m = joints[lifted].copy()
        m[:, 0] = 2 * x_mid - m[:, 0]
        m = m + (np.array(list(bones[f"hand.{other}"][0])) - m[0])
        joints[other] = m
        report["mirrored_hand"] = other

    # --- new bones ---
    bpy.context.view_layer.objects.active = arm
    for o in bpy.data.objects:
        o.select_set(o is arm)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    inv = arm.matrix_world.inverted()
    spine, chest = eb["spine"], eb["chest"]
    mid = eb.new("spine_mid")
    mid.head = spine.head.lerp(chest.head, 0.5)
    mid.tail = chest.head.copy()
    mid.parent = spine
    spine.tail = mid.head.copy()
    chest.parent = mid
    chest.use_connect = False
    for side in ("L", "R"):
        J = [inv @ Vector(p) for p in joints[side]]
        wrist = J[0]
        palm = (J[5] - wrist).cross(J[17] - wrist).normalized()
        if side == "L":
            palm = -palm
        hand = eb[f"hand.{side}"]
        for name in FINGERS:
            ids = LM[name]
            chain = [J[i] for i in ids]
            parent = hand
            for k in range(3):
                bone = eb.new(f"{name}_0{k + 1}.{side}")
                bone.head = chain[k]
                bone.tail = chain[k + 1] if (chain[k + 1] - chain[k]).length > 1e-4 else chain[k] + (chain[k] - chain[max(k - 1, 0)]).normalized() * 0.01
                bone.parent = parent
                bone.use_connect = k > 0
                axis = (bone.tail - bone.head).normalized()
                bend = palm.cross(axis)
                if bend.length > 1e-6:
                    bone.align_roll(bend.normalized().cross(axis))
                parent = bone
        foot = eb[f"foot.{side}"]
        toe = eb.new(f"toe.{side}")
        toe.head = foot.head.lerp(foot.tail, a.ball)
        toe.head.z = foot.tail.z + 0.012
        toe.tail = foot.tail.copy()
        toe.tail.z = toe.head.z
        toe.parent = foot
    bpy.ops.object.mode_set(mode="OBJECT")
    new_bones = {b.name: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones}

    # --- weights ---
    def seg_dist(p, h, tl):
        h, tl = np.array(list(h)), np.array(list(tl))
        ab = tl - h
        s = np.clip(((p - h) @ ab) / max(ab @ ab, 1e-12), 0, 1)
        return np.linalg.norm(p - (h + s[:, None] * ab), axis=1), s

    add = {}
    # spine / spine_mid / chest by height (a partition of unity over the three segments' mid-heights)
    zs = [(new_bones[b][0].z + new_bones[b][1].z) / 2 for b in ("spine", "spine_mid", "chest")]
    total = Wd.get("spine", 0) + Wd.get("chest", 0)
    z = P[:, 2]
    w_sp = np.clip((zs[1] - z) / (zs[1] - zs[0]), 0, 1)
    w_ch = np.clip((z - zs[1]) / (zs[2] - zs[1]), 0, 1)
    w_md = 1 - w_sp - w_ch
    add["spine"], add["spine_mid"], add["chest"] = total * w_sp, total * w_md, total * w_ch
    moved = {"spine": int((total > 0).sum())}
    # hands -> fingers. Smooth, topology-agnostic shares (a reconstruction often fuses the four fingers into one blade):
    # the thumb against the rest by which chain is nearer; across index..pinky by the position along the knuckle
    # line; along a finger, palm and the three segments by the distance past each joint; then smoothed over the mesh.
    # Adjacency over position-welded vertices: the imported mesh is split at every UV seam, and smoothing on the split
    # graph would give coincident vertices different weights (the skin would crack along every seam).
    _, weld = np.unique(np.round(P, 6), axis=0, return_inverse=True)
    weld = weld.ravel()
    adj_w = {}
    for e in me.edges:
        i, j = weld[e.vertices[0]], weld[e.vertices[1]]
        if i != j:
            adj_w.setdefault(i, set()).add(j)
            adj_w.setdefault(j, set()).add(i)
    for side in ("L", "R"):
        hw = Wd.get(f"hand.{side}", np.zeros(len(P)))
        idx = np.nonzero(hw > 0)[0]
        if not len(idx):
            continue
        Q = P[idx]
        J = joints[side]
        polyline = {name: [np.array(list(new_bones[f"{name}_0{k}.{side}"][0])) for k in (1, 2, 3)]
                    + [np.array(list(new_bones[f"{name}_03.{side}"][1]))] for name in FINGERS}

        def chain_dist(name):
            pts = polyline[name]
            return np.min([seg_dist(Q, pts[k], pts[k + 1])[0] for k in range(3)], 0)

        d_thumb = chain_dist("thumb")
        d_blade = np.min([chain_dist(n) for n in FINGERS[1:]], 0)
        thumbness = 1 / (1 + np.exp(-(d_blade - d_thumb) / 0.004))
        # lateral position along the knuckle line (index mcp -> pinky mcp)
        k0, k1 = np.array(polyline["index"][0]), np.array(polyline["pinky"][0])
        lat_axis = (k1 - k0) / np.linalg.norm(k1 - k0)
        lat = (Q - k0) @ lat_axis
        centres = [(np.array(polyline[n][0]) - k0) @ lat_axis for n in FINGERS[1:]]
        spread = max(abs(centres[-1] - centres[0]) / 3, 0.006)
        lw = np.stack([np.exp(-((lat - c0) / spread) ** 2) for c0 in centres], 1)
        lw /= lw.sum(1, keepdims=True) + 1e-9

        def along(name):
            # metres past this finger's knuckle along its chain, and the chain's joint distances
            pts = polyline[name]
            lens = [np.linalg.norm(pts[k + 1] - pts[k]) for k in range(3)]
            best_d, best_s = None, None
            for k in range(3):
                d, sk = seg_dist(Q, pts[k], pts[k + 1])
                s_abs = sum(lens[:k]) + sk * lens[k]
                if k == 0:
                    ab = pts[1] - pts[0]
                    s_abs = (Q - pts[0]) @ (ab / np.linalg.norm(ab))  # may be negative: in the palm
                best_d = d if best_d is None else np.minimum(best_d, d)
                best_s = s_abs if best_s is None else np.where(d <= best_d, s_abs, best_s)
            return best_s, np.cumsum([0] + lens)

        def split(s, cuts, ramp=0.008):
            # palm, seg1, seg2, seg3 shares from the distance s along the chain (joints at cuts[0..2])
            r = [np.clip((s - cuts[k]) / ramp + 0.5, 0, 1) for k in range(3)]
            return [1 - r[0], r[0] - r[1], r[1] - r[2], r[2]]

        shares = {f"hand.{side}": np.zeros(len(idx))}
        for fi, name in enumerate(FINGERS):
            s, cuts = along(name)
            part = split(s, cuts)
            mass = thumbness if name == "thumb" else (1 - thumbness) * lw[:, fi - 1]
            shares[f"hand.{side}"] += mass * part[0]
            for k in range(3):
                shares[f"{name}_0{k + 1}.{side}"] = mass * part[k + 1]
        # smooth the shares over the welded surface (neighbours in the hand region), then back to every split copy
        names = list(shares)
        S = np.stack([shares[n] for n in names], 1)
        wid = weld[idx]
        uw, first = np.unique(wid, return_index=True)
        Sw = S[first]
        wpos = {w: i for i, w in enumerate(uw)}
        for _ in range(4):
            S2 = Sw.copy()
            for i, w in enumerate(uw):
                nb = [wpos[u] for u in adj_w.get(w, ()) if u in wpos]
                if nb:
                    S2[i] = 0.5 * Sw[i] + 0.5 * Sw[nb].mean(0)
            Sw = S2
        S = Sw[np.searchsorted(uw, wid)]
        S /= S.sum(1, keepdims=True) + 1e-9
        for n, col in zip(names, S.T):
            add.setdefault(n, np.zeros(len(P)))[idx] = hw[idx] * col
        moved[f"hand.{side}"] = int((S[:, 0] < 0.5).sum())
        # foot -> toe
        fw = Wd.get(f"foot.{side}", np.zeros(len(P)))
        fh, ft = new_bones[f"foot.{side}"]
        th = new_bones[f"toe.{side}"][0]
        fwd = np.array(list(ft - fh))
        fwd[2] = 0
        fwd /= np.linalg.norm(fwd)
        along = (P - np.array(list(th))) @ fwd
        tw = fw * np.clip(along / 0.02 + 0.5, 0, 1)
        add[f"toe.{side}"] = tw
        add[f"foot.{side}"] = fw - tw
        moved[f"toe.{side}"] = int((tw > 0.5).sum())
    for name, w in add.items():
        g = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
        for i in range(len(P)):
            if w[i] > 1e-4:
                g.add([i], float(w[i]), "REPLACE")
            elif name in Wd and Wd[name][i] > 0:
                g.remove([i])
    # four influences, normalised
    for v in me.vertices:
        gs = sorted(((g.group, g.weight) for g in v.groups), key=lambda x: -x[1])
        for gi, _ in gs[4:]:
            obj.vertex_groups[gi].remove([v.index])
        top = gs[:4]
        s = sum(w for _, w in top) or 1.0
        for gi, w in top:
            obj.vertex_groups[gi].add([v.index], w / s, "REPLACE")
    report.update(bones=len(arm.data.bones), weights_moved=moved)
    bpy.ops.export_scene.gltf(filepath=a.out, export_format="GLB", use_selection=False, export_apply=False, export_skins=True,
                              export_tangents=True)
    if a.report:
        json.dump(report, open(a.report, "w"), indent=1)
    print("RIG_PRODUCTION_RESULT " + json.dumps(report))


main()
