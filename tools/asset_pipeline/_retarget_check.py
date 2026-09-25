"""Measure a retargeted clip on its target body against the source it came from (plain Python: numpy + PIL).

The target is played with the GAME's semantics (`_skin_deform_check.pose`: ArtLibrary.Retarget, rest-relative
by bone name) from the exported GLB, not from the retarget's own numbers, so the export round trip is part
of what is measured. The source is sampled from its pack file exactly as `_retarget_clip.py` reads it and
laid into the target's frame (facing turned, scaled by the record's leg ratio). Everything at 60 Hz.

  foot slide     planted = a source contact point (ankle or toe joint) moving slower than PLANT_SPEED
                 for at least PLANT_MIN_S; on those samples the target's matching point (ankle joint, or the
                 toe of the skinned foot) is tracked: worst horizontal drift within a planted run, and
                 how far its height strays from the source's plant (each against its own rest height:
                 on the ground, or on the ledge). The source's own drift on the same runs is the floor.
  root motion    the target root's net travel, path length, mean and peak speed, next to the source's scaled.
  hands          each hand relative to its shoulder joint, in the body's frame, normalised by arm length
                 (shoulder -> elbow -> wrist), target against source: direction error and reach ratio;
                 at the source's longest forward reach (a bow arm at full draw) the extended hand's height
                 and reach, the other hand to the head, the hand spread; each hand at its slowest source
                 sample (a grip) and on runs held still, target against source.
  skin           `_skin_deform_check.measure` at 60 Hz: max edge stretch, folds, lowest skin point; against
                 the rig's own authored clips (--baseline) for what is normal on this mesh.
  joints         elbow and knee flexion in the upper bone's own frame (elbows forward, knees back):
                 the most hyperextended and most bent sample and the worst sideways bend.
  overlay        4 samples (one is the longest reach): front and side with the source (orange) over the
                 retarget (blue) at the leg ratio, and side by side at equal head height for pose shape.

  python _retarget_check.py --id player.veth_wanderer.ext_bow_draw ^
      --source <pack.zip> --member <file in zip> --clip Ranged_Bow_Draw ^
      --map retarget_maps\\kaykit_rig_medium.json --target <rigged.glb> [--baseline <native clip.glb> ...]
"""
import argparse
import json
import math
import os
import shutil
import sys
import tempfile

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _retarget_clip as rc  # noqa: E402
import _skin_deform_check as sk  # noqa: E402

HZ = 60.0
PLANT_SPEED = 0.20     # m/s (target scale): slower than this, a contact point counts as planted
PLANT_MIN_S = 0.10     # a planted run at least this long
HYPEREXTENSION_OK_DEG = -10.0

SRC_COLOUR, TGT_COLOUR = (230, 120, 20), (40, 90, 220)


class TargetPlayer:
    """The exported clip on the rigged body, with the game's semantics."""

    def __init__(self, rig_path, clip_path):
        self.rig = sk.Rig(rig_path)
        self.clip = sk.Clip(clip_path)
        self.index = {n: j for n, j in zip(self.rig.names, self.rig.joints)}
        self.parent = {n: self.rig.doc["nodes"][self.rig.parent[j]]["name"]
                       for n, j in self.index.items() if self.rig.parent.get(j) in self.rig.joints}
        rest = self.rig.world(self.rig.rest_local())
        self.rest = {n: rest[j] for n, j in self.index.items()}
        # the toe of the skinned foot, carried in the foot bone's frame
        skin_rest = self.rig.skin(self.rig.rest_local())
        dominant = np.array([self.rig.names[int(self.rig.J[v][np.argmax(self.rig.W[v])])]
                             for v in range(len(self.rig.V))])
        self.toe_local = {}
        for foot in ("foot.L", "foot.R"):
            V = skin_rest[dominant == foot]
            front = V[V[:, 2] >= np.percentile(V[:, 2], 85)]
            toe = np.array([front[:, 0].mean(), V[:, 1].min(), front[:, 2].mean(), 1.0])
            self.toe_local[foot] = np.linalg.inv(self.rest[foot]) @ toe

    def worlds(self, time):
        w = self.rig.world(sk.pose(self.rig, self.clip, time))
        return {n: w[j] for n, j in self.index.items()}


def lay_source(rt, worlds, root_mode, scale=None):
    """Source joint positions in the target's frame: turned, scaled about the root, root motion kept or not."""
    src, k = rt.src, rt.k if scale is None else scale
    s_root0 = rt.S0[src.index[rt.root_src]][:3, 3]
    t_root0 = rt.T0[rt.tgt.index["root"]][:3, 3]
    if root_mode == "inplace":
        r = src.index[rt.root_src]
        undo = rt.S0[r] @ np.linalg.inv(worlds[r])
        worlds = [undo @ m for m in worlds]
    return {name: t_root0 + k * (rt.Q @ (worlds[i][:3, 3] - s_root0)) for name, i in src.index.items()}


def runs(mask, min_len):
    out, start = [], None
    for i, m in enumerate(list(mask) + [False]):
        if m and start is None:
            start = i
        elif not m and start is not None:
            if i - start >= min_len:
                out.append((start, i))
            start = None
    return out


def speeds(P, dt):
    v = np.linalg.norm(np.diff(P, axis=0), axis=1) / dt
    return np.r_[v[:1], np.maximum(v[:-1], v[1:]), v[-1:]] if len(v) > 1 else np.zeros(len(P))


def horizontal(v):
    return np.linalg.norm(np.asarray(v)[..., [0, 2]], axis=-1)


def limb_angles(upper_rest_rot, e_rest, flex_dir, upper_rot, lower_dir):
    """Flexion (toward flex_dir) and sideways bend of the lower segment in the upper bone's frame, degrees."""
    e = upper_rest_rot.T @ e_rest
    e /= np.linalg.norm(e)
    f = upper_rest_rot.T @ flex_dir
    f -= np.dot(f, e) * e
    f /= np.linalg.norm(f)
    n = np.cross(e, f)
    d = upper_rot.T @ (lower_dir / np.linalg.norm(lower_dir))
    return math.degrees(math.atan2(np.dot(d, f), np.dot(d, e))), math.degrees(math.asin(max(-1, min(1, np.dot(d, n)))))


def draw_overlay(path, frames, times, rows, src_edges, tgt_edges, title, extra_lines):
    """rows: (label, axis, source frames, target frames, src anchor, tgt anchor); an anchor of None overlays
    the two in one frame, a joint name draws them side by side, each centred on that joint."""
    from PIL import Image, ImageDraw
    cols, pw, ph, head = len(frames), 300, 380, 24
    img = Image.new("RGB", (cols * pw, len(rows) * ph + head), (250, 250, 248))
    dr = ImageDraw.Draw(img)
    dr.text((8, 6), title, fill=(20, 20, 20))
    for row, (label, axis, sfr, tfr, s_anchor, t_anchor) in enumerate(rows):
        beside = s_anchor is not None
        placed = []
        for c in range(cols):
            s, t = dict(sfr[c]), dict(tfr[c])
            if beside:
                sx, tx = s[s_anchor][axis], t[t_anchor][axis]
                s = {k: np.r_[v[:axis], v[axis] - sx, v[axis + 1:]] for k, v in s.items()}
                t = {k: np.r_[v[:axis], v[axis] - tx, v[axis + 1:]] for k, v in t.items()}
            placed.append((s, t))
        allp = np.array([p for s, t in placed for p in list(s.values()) + list(t.values())])
        lo_x, hi_x = allp[:, axis].min(), allp[:, axis].max()
        lo_y, hi_y = min(0.0, allp[:, 1].min()), allp[:, 1].max()
        room_x = 0.42 * pw if beside else pw - 40
        scale = min(room_x / max(hi_x - lo_x, 1e-3), (ph - 50) / max(hi_y - lo_y, 1e-3))
        for c, f in enumerate(frames):
            ox, oy = c * pw, head + row * ph
            dr.rectangle([ox + 2, oy + 2, ox + pw - 3, oy + ph - 3], outline=(200, 200, 200))
            for which, (pts, edges, colour, w) in enumerate(((placed[c][0], src_edges, SRC_COLOUR, 4),
                                                              (placed[c][1], tgt_edges, TGT_COLOUR, 3))):
                if beside:
                    cx = ox + pw * (0.3 if which == 0 else 0.7)
                else:
                    cx = ox + 20 + (0 - lo_x) * scale

                def to_px(p, cx=cx):
                    return (cx + p[axis] * scale, oy + ph - 25 - (p[1] - lo_y) * scale)
                if which == 0:
                    gy = to_px([0, 0, 0])[1]
                    dr.line([ox + 5, gy, ox + pw - 5, gy], fill=(150, 150, 150), width=1)
                    for y, name in ([] if beside else extra_lines):
                        ly = to_px([0, y, 0])[1]
                        dr.line([ox + 5, ly, ox + pw - 5, ly], fill=(190, 160, 160), width=1)
                        dr.text((ox + 6, ly - 12), name, fill=(150, 110, 110))
                for a, b in edges:
                    if a in pts and b in pts:
                        dr.line([to_px(pts[a]), to_px(pts[b])], fill=colour, width=w)
                for p in pts.values():
                    x, y = to_px(p)
                    dr.ellipse([x - 2, y - 2, x + 2, y + 2], fill=colour)
            dr.text((ox + 8, oy + 6), f"{label}", fill=(40, 40, 40))
            dr.text((ox + 8, oy + 20), f"sample {f}  t={times[f]:.2f}s", fill=(90, 90, 90))
    img.save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id", required=True)
    ap.add_argument("--source", required=True)
    ap.add_argument("--member")
    ap.add_argument("--clip", required=True)
    ap.add_argument("--map", required=True)
    ap.add_argument("--target", required=True)
    ap.add_argument("--assets", default=rc.DEFAULT_ASSETS)
    ap.add_argument("--baseline", action="append", default=[], help="native clips of the same rig")
    ap.add_argument("--frames", type=int, nargs=4, help="the 4 overlay samples (60 Hz); default spread")
    ap.add_argument("--out")
    args = ap.parse_args()

    animation_id = args.id if args.id.startswith("anim.") else "anim." + args.id
    clip_glb = os.path.join(args.assets, "animation", "ready", rc.family_dir(animation_id), animation_id + ".glb")
    record_path = os.path.join(args.assets, "animation", "clips", animation_id + ".json")
    with open(record_path, encoding="utf-8") as handle:
        record = json.load(handle)
    with open(args.map, encoding="utf-8") as handle:
        bmap = json.load(handle)
    out_dir = args.out or os.path.join(args.assets, "animation", "review", "retarget", animation_id)
    os.makedirs(out_dir, exist_ok=True)
    ret = record["retarget"]

    workdir = tempfile.mkdtemp(prefix="retarget_check_")
    try:
        src_path, _facts = rc.open_source(args.source, args.member, workdir)
        source, target = rc.Scene(src_path), rc.Scene(args.target)
        rt = rc.Retarget(source, target, bmap, ret["scale_mode"])
        tracks = source.tracks(args.clip)
    finally:
        shutil.rmtree(workdir, ignore_errors=True)
    player = TargetPlayer(args.target, clip_glb)
    duration = min(rc.Scene.duration(tracks), player.clip.t_end)
    times = np.arange(0.0, duration + 1e-9, 1.0 / HZ)
    dt = 1.0 / HZ
    root_mode = ret["root_mode"]

    S, T, TW = [], [], []
    for t in times:
        S.append(lay_source(rt, source.world(source.local(tracks, t)), root_mode))
        w = player.worlds(t)
        TW.append(w)
        T.append({n: m[:3, 3].copy() for n, m in w.items()})
    report = {"animation_id": animation_id, "clip_glb": clip_glb, "samples": len(times), "hz": HZ,
              "duration_s": round(float(duration), 4), "scale": rt.k, "root_mode": root_mode, "feet": ret.get("feet")}

    # ---- foot slide on planted samples
    feet = {}
    min_len = int(round(PLANT_MIN_S * HZ))
    S_rest = lay_source(rt, rt.S0, "keep")
    for foot, points in bmap["contacts"].items():
        entry = {}
        planted_any = np.zeros(len(times), bool)
        for kind, src_joint in points.items():
            sp = np.array([s[src_joint] for s in S])
            if kind == "ankle":
                tp = np.array([T[i][foot] for i in range(len(times))])
                t_rest_y = player.rest[foot][1, 3]
            else:
                tp = np.array([(TW[i][foot] @ player.toe_local[foot])[:3] for i in range(len(times))])
                t_rest_y = (player.rest[foot] @ player.toe_local[foot])[1]
            planted = speeds(sp, dt) < PLANT_SPEED
            planted_any |= planted
            worst_t = worst_s = worst_h = 0.0
            for a, b in runs(planted, min_len):
                worst_t = max(worst_t, float(horizontal(tp[a:b] - tp[a]).max()))
                worst_s = max(worst_s, float(horizontal(sp[a:b] - sp[a]).max()))
                # height against the source's plant (each relative to its own rest height): on the ground
                # or on the ledge where the source stood
                lift = (tp[a:b, 1] - t_rest_y) - (sp[a:b, 1] - S_rest[src_joint][1])
                worst_h = max(worst_h, float(np.abs(lift).max()))
            entry[kind] = {"planted_samples": int(sum(b - a for a, b in runs(planted, min_len))),
                           "planted_runs_s": [[round(float(times[a]), 3), round(float(times[b - 1]), 3)]
                                              for a, b in runs(planted, min_len)],
                           "target_max_drift_cm": round(100 * worst_t, 2),
                           "source_max_drift_cm": round(100 * worst_s, 2),
                           "target_height_off_source_plant_cm": round(100 * worst_h, 2)}
        entry["planted_share"] = round(float(planted_any.mean()), 3)
        feet[foot] = entry
    report["foot_slide"] = feet

    # ---- root motion
    def motion(P):
        seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
        return {"net_m": [round(float(x), 4) for x in P[-1] - P[0]], "path_m": round(float(seg.sum()), 4),
                "mean_speed_m_s": round(float(seg.sum() / duration), 4),
                "peak_speed_m_s": round(float(seg.max() / dt), 4) if len(seg) else 0.0,
                "rise_m": round(float(P[-1][1] - P[0][1]), 4)}
    report["root_motion"] = {"target": motion(np.array([t["root"] for t in T])),
                             "source_scaled": motion(np.array([s[rt.root_src] for s in S])),
                             "recorded_travel_m": ret.get("root_travel_m")}

    # ---- hands and shoulders
    arms = {}
    hand_src = {arm[3]: rt.entry[arm[3]]["source"] for arm in bmap["target_arms"]}
    ua_src = {arm[3]: rt.entry[arm[1]]["source"] for arm in bmap["target_arms"]}
    fa_src = {arm[3]: rt.entry[arm[2]]["source"] for arm in bmap["target_arms"]}

    def arm_len(P, a, b, c):
        return np.linalg.norm(P[b] - P[a]) + np.linalg.norm(P[c] - P[b])
    S0 = lay_source(rt, rt.S0, "keep")
    T0 = {n: m[:3, 3] for n, m in player.rest.items()}
    for arm in bmap["target_arms"]:
        _sh, ua, fa, hand = arm
        Lt = arm_len(T0, ua, fa, hand)
        Ls = arm_len(S0, ua_src[hand], fa_src[hand], hand_src[hand])
        dirs, ratio = [], []
        for i in range(len(times)):
            vt = (T[i][hand] - T[i][ua]) / Lt
            vs = (S[i][hand_src[hand]] - S[i][ua_src[hand]]) / Ls
            dirs.append(rc.angle_deg(vt, vs))
            ratio.append(np.linalg.norm(vt) / max(np.linalg.norm(vs), 1e-9))
        # aligned-bone self-check: the upper arm and forearm point where the source's do
        bone_err = max(max(rc.angle_deg(T[i][fa] - T[i][ua], S[i][fa_src[hand]] - S[i][ua_src[hand]]),
                           rc.angle_deg(T[i][hand] - T[i][fa], S[i][hand_src[hand]] - S[i][fa_src[hand]]))
                       for i in range(len(times)))
        arms[hand] = {"arm_length_target_m": round(float(Lt), 4), "arm_length_source_scaled_m": round(float(Ls), 4),
                      "hand_dir_error_deg_max": round(max(dirs), 2), "hand_dir_error_deg_mean": round(float(np.mean(dirs)), 2),
                      "reach_ratio_range": [round(min(ratio), 3), round(max(ratio), 3)],
                      "upper_and_forearm_dir_error_deg_max": round(bone_err, 2)}
        # hands held still in the source (a grip): where the target's hand is on those samples
        sp = np.array([s[hand_src[hand]] for s in S])
        tp = np.array([t[hand] for t in T])
        sp_speed = speeds(sp, dt)
        slow = int(np.argmin(sp_speed))
        arms[hand]["slowest_source_sample"] = {
            "t": round(float(times[slow]), 3), "source_speed_m_s": round(float(sp_speed[slow]), 3),
            "source_height_scaled_m": round(float(sp[slow, 1]), 3), "target_height_m": round(float(tp[slow, 1]), 3),
            "target_minus_source_m": [round(float(x), 3) for x in tp[slow] - sp[slow]]}
        held = runs(sp_speed < PLANT_SPEED, min_len)
        arms[hand]["held_runs"] = [{"t": [round(float(times[a]), 3), round(float(times[b - 1]), 3)],
                                    "source_height_m": round(float(sp[a:b, 1].mean()), 3),
                                    "target_height_m": round(float(tp[a:b, 1].mean()), 3),
                                    "target_minus_source_m": [round(float(x), 3) for x in (tp[a:b] - sp[a:b]).mean(0)],
                                    "target_drift_cm": round(100 * float(np.linalg.norm(tp[a:b] - tp[a], axis=1).max()), 2)}
                                   for a, b in held]
    hands = [arm[3] for arm in bmap["target_arms"]]
    fwd = rt.Q @ np.array([0, 0, 1.0])
    # the key sample: the source's longest forward reach of either hand from its shoulder (a bow arm at
    # full draw, hands reaching for a ledge)
    reach = {h: [float(np.dot(S[i][hand_src[h]] - S[i][ua_src[h]], fwd)) for i in range(len(times))] for h in hands}
    ext = max(hands, key=lambda h: max(reach[h]))
    full = int(np.argmax(reach[ext]))
    other = [h for h in hands if h != ext][0]
    spread = [np.linalg.norm(S[i][hand_src[ext]] - S[i][hand_src[other]]) for i in range(len(times))]
    Lt, Ls = arms[ext]["arm_length_target_m"], arms[ext]["arm_length_source_scaled_m"]
    rel_t = T[full][ext] - T[full][[a for a in bmap["target_arms"] if a[3] == ext][0][1]]
    rel_s = S[full][hand_src[ext]] - S[full][ua_src[ext]]
    head_src = rt.entry["head"]["source"]
    report["max_reach"] = {
        "sample": full, "t": round(float(times[full]), 3), "extended_hand": ext,
        "extended_hand_height_above_shoulder_m": {"target": round(float(rel_t[1]), 3),
                                                  "source_at_target_arm_length": round(float(rel_s[1] * Lt / Ls), 3)},
        "extended_hand_forward_reach_m": {"target": round(float(np.dot(rel_t, fwd)), 3),
                                          "source_at_target_arm_length": round(float(np.dot(rel_s, fwd) * Lt / Ls), 3)},
        "extended_hand_world_height_m": {"target": round(float(T[full][ext][1]), 3),
                                         "source_scaled": round(float(S[full][hand_src[ext]][1]), 3)},
        "hand_direction_error_deg": round(rc.angle_deg(rel_t, rel_s), 2),
        "other_hand_to_head_m": {"target": round(float(np.linalg.norm(T[full][other] - T[full]["head"])), 3),
                                 "source_scaled": round(float(np.linalg.norm(S[full][hand_src[other]] - S[full][head_src])), 3)},
        "other_hand_to_head_per_arm_length": {
            "target": round(float(np.linalg.norm(T[full][other] - T[full]["head"]) / Lt), 3),
            "source": round(float(np.linalg.norm(S[full][hand_src[other]] - S[full][head_src]) / Ls), 3)},
        "hand_spread_m": {"target": round(float(np.linalg.norm(T[full][ext] - T[full][other])), 3),
                          "source_at_target_arm_length": round(float(spread[full] * Lt / Ls), 3)},
    }
    report["arms"] = arms

    # ---- skin
    skin, _dumps = sk.measure(player.rig, player.clip, fps=HZ, intersections=False)
    report["skin"] = {k: skin[k] for k in ("max_edge_stretch_m", "max_edges_over_5cm", "max_edges_over_10cm",
                                           "max_edges_compressed_50pct", "max_folded_edges", "min_y_range_m",
                                           "frames_below_ground_2cm", "worst_edge")}
    base = []
    for b in args.baseline:
        m, _d = sk.measure(player.rig, sk.Clip(b), fps=HZ, intersections=False)
        base.append({"clip": os.path.basename(b), "max_edge_stretch_m": m["max_edge_stretch_m"],
                     "max_edges_over_5cm": m["max_edges_over_5cm"], "max_folded_edges": m["max_folded_edges"]})
    if base:
        report["skin"]["baseline_native_clips"] = base
        report["skin"]["baseline_max_edge_stretch_m"] = max(b["max_edge_stretch_m"] for b in base)

    # ---- elbows and knees
    joints = {}
    limbs = [(a[1], a[2], a[3], fwd) for a in bmap["target_arms"]] + \
            [(l[0], l[1], l[2], -fwd) for l in bmap["target_legs"]]
    for up, lo, end, flex_dir in limbs:
        name = {"forearm": "elbow", "shin": "knee"}[lo.split(".")[0]] + "." + lo.split(".")[1]
        su, sl, se = (rt.entry[x]["source"] for x in (up, lo, end))
        tr_rest = rc.ortho(player.rest[up][:3, :3])
        te_rest = T0[lo] - T0[up]
        sr_rest = rc.ortho(rt.S0[source.index[su]][:3, :3])
        se_rest = rt.S0[source.index[sl]][:3, 3] - rt.S0[source.index[su]][:3, 3]
        tf, tl, sf, sl_ = [], [], [], []
        for i, t in enumerate(times):
            f, lat = limb_angles(tr_rest, te_rest, flex_dir, rc.ortho(TW[i][up][:3, :3]), T[i][end] - T[i][lo])
            tf.append(f)
            tl.append(abs(lat))
            ws = source.world(source.local(tracks, t))
            f, lat = limb_angles(sr_rest, se_rest, rt.Q.T @ flex_dir, rc.ortho(ws[source.index[su]][:3, :3]),
                                 ws[source.index[se]][:3, 3] - ws[source.index[sl]][:3, 3])
            sf.append(f)
            sl_.append(abs(lat))
        joints[name] = {"target_flex_deg": [round(min(tf), 1), round(max(tf), 1)],
                        "source_flex_deg": [round(min(sf), 1), round(max(sf), 1)],
                        "target_max_sideways_deg": round(max(tl), 1), "source_max_sideways_deg": round(max(sl_), 1),
                        "bends_the_right_way": bool(min(tf) >= HYPEREXTENSION_OK_DEG)}
    report["joints"] = joints

    # ---- overlay
    n = len(times)
    frames = list(args.frames) if args.frames else [0, n // 3, (2 * n) // 3, n - 1]
    if not args.frames and full not in frames:      # the widest hand spread is always one of the four
        frames[1 if abs(full - frames[1]) < abs(full - frames[2]) else 2] = full
        frames = sorted(frames)
    tgt_edges = [(p, c) for c, p in player.parent.items() if p != "root"]   # the root is not a limb
    src_names = {b: rt.entry[b]["source"] for b in rt.bones if rt.entry[b].get("source")}
    src_edges = []
    for b in src_names:
        p = rt.tparent[b]
        while p is not None and p not in src_names:
            p = rt.tparent[p]
        if p is not None and p != "root":
            src_edges.append((src_names[p], src_names[b]))
    for foot, pts in bmap["contacts"].items():
        src_edges.append((pts["ankle"], pts["toe"]))
    keep = (set(src_names.values()) - {rt.root_src}) | {p for pts in bmap["contacts"].values() for p in pts.values()}
    src_frames = [{k: v for k, v in S[f].items() if k in keep} for f in frames]
    tgt_frames = [{k: v for k, v in T[f].items() if k != "root"} for f in frames]
    # the pose-shape row: the source at the target's head height, beside the target, each on its hips
    s_head = rt.S0[source.index[head_src]][1, 3] - rt.S0[source.index[rt.root_src]][1, 3]
    head_scale = float(T0["head"][1] / s_head)
    src_tall = [{k: v for k, v in lay_source(rt, source.world(source.local(tracks, times[f])), root_mode,
                                             head_scale).items() if k in keep} for f in frames]
    hips_src = rt.entry["hips"]["source"]
    extra = []
    if root_mode == "keep" and abs(report["root_motion"]["target"]["rise_m"]) > 0.05:
        extra.append((report["root_motion"]["target"]["rise_m"], "scaled ledge"))
    overlay = os.path.join(out_dir, "overlay_source_vs_retarget.png")
    rows = [(f"front, source x{rt.k:.2f} (leg ratio)", 0, src_frames, tgt_frames, None, None),
            (f"side, source x{rt.k:.2f} (leg ratio)", 2, src_frames, tgt_frames, None, None),
            (f"side by side, source x{head_scale:.2f} (head ht)", 2, src_tall, tgt_frames, hips_src, "hips")]
    draw_overlay(overlay, frames, times, rows, src_edges, tgt_edges,
                 f"{animation_id}  <-  {args.clip}    orange = source (turned onto the target's facing)    "
                 f"blue = retarget on the target, game playback", extra)
    report["overlay"] = overlay
    report["overlay_samples"] = frames

    out_json = os.path.join(out_dir, "check.json")
    with open(out_json, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=1)
    print(json.dumps({k: report[k] for k in ("foot_slide", "root_motion", "max_reach", "skin", "joints")}, indent=1))
    print("RETARGET_CHECK " + out_json)
    return 0


if __name__ == "__main__":
    sys.exit(main())
