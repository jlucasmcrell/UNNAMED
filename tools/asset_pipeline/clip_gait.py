"""Gait facts of in-place locomotion clips (animation GLBs as _retarget_clip.py writes them): stance width, pace, foot skate and
loop seam. numpy only (no Blender); the clip is sampled with exact glTF semantics (_retarget_clip.Scene).

  * stance width: the lateral distance between the two ankle joints, along the rig's left-right axis (the thighs at rest).
    "mid-stance" is the frame where the supporting (lower) foot passes under the hips (its forward offset from the thigh midpoint crosses
    zero); the cycle mean is over every frame.
  * pace: the planted foot's horizontal speed (planted = within 2 cm of the lowest point either ankle reaches), the ground speed
    at which that foot would sit still (charprod2/clip_pace.py's definition). skate: the planted foot's speed spread
    (p10 / p90 over the median) - a clean plant reads close to 1.
  * loop seam: the largest joint rotation and ankle position change from the last frame back to the first.

    python clip_gait.py CLIP.glb [CLIP.glb ...] [--feet foot.L,foot.R] [--thighs thigh.L,thigh.R] [--json out.json]
"""
import argparse
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _retarget_clip import Scene, ortho, rot_angle_deg  # noqa: E402

CONTACT_M = 0.03


def analyse(path, feet, thighs, fps=60.0):
    s = Scene(path)
    tracks = s.tracks(s.clip_names()[0])
    duration = Scene.duration(tracks)
    n = int(round(duration * fps)) + 1
    times = [min(i / fps, duration) for i in range(n)]
    rest = s.world(s.local())
    lat = s.pos(rest, thighs[0]) - s.pos(rest, thighs[1])
    lat[1] = 0
    lat /= np.linalg.norm(lat)
    fwd = np.cross(lat, [0, 1.0, 0])        # a rig facing +Z with its left at +X: forward = left x up
    toe = s.pos(rest, feet[0])
    worlds = [s.world(s.local(tracks, t)) for t in times]
    ank = np.array([[s.pos(w, f) for f in feet] for w in worlds])          # frame, foot, xyz
    mid = np.array([(s.pos(w, thighs[0]) + s.pos(w, thighs[1])) / 2 for w in worlds])
    lateral = ank @ lat                                                    # frame, foot
    forward = (ank - mid[:, None, :]) @ fwd
    sep = np.abs(lateral[:, 0] - lateral[:, 1])
    planted = ank[:, :, 1] < ank[:, :, 1].min(axis=0) + CONTACT_M          # each foot against its own lowest
    support = ank[:, :, 1] <= ank[:, ::-1, 1]                             # the lower foot carries the body
    mids, speeds = [], []
    for k in range(2):
        for i in range(n - 1):
            if planted[i, k] and planted[i + 1, k]:
                d = ank[i + 1, k] - ank[i, k]
                speeds.append(-float(d @ fwd) * fps)
            if support[i, k] and np.sign(forward[i, k]) != np.sign(forward[i + 1, k]):
                a = forward[i, k] / (forward[i, k] - forward[i + 1, k])
                mids.append((1 - a) * sep[i] + a * sep[i + 1])
    speeds = np.array(speeds)
    pace = float(np.median(speeds)) if len(speeds) else None
    seam_rot = max(rot_angle_deg(ortho(worlds[0][j][:3, :3]).T @ ortho(worlds[-1][j][:3, :3]))
                   for j in (s.index[name] for name in s.joints))
    seam_pos = float(np.max(np.linalg.norm(ank[0] - ank[-1], axis=1)))
    rest_sep = abs(float((s.pos(rest, feet[0]) - s.pos(rest, feet[1])) @ lat))
    return {
        "clip": os.path.basename(path), "duration_s": round(duration, 4), "frames": n,
        "stance_mid_m": round(float(np.mean(mids)), 4) if mids else None, "stance_mid_samples": len(mids),
        "stance_mean_m": round(float(sep.mean()), 4), "stance_min_m": round(float(sep.min()), 4),
        "stance_max_m": round(float(sep.max()), 4), "rest_ankle_sep_m": round(rest_sep, 4),
        "hip_width_m": round(float(np.linalg.norm(s.pos(rest, thighs[0]) - s.pos(rest, thighs[1]))), 4),
        "pace_m_s": round(pace, 4) if pace else None,
        "skate_p10_p90": [round(float(np.percentile(speeds, 10)) / pace, 3), round(float(np.percentile(speeds, 90)) / pace, 3)]
        if pace else None,
        "planted_share": round(float(planted.mean()), 3),
        "loop_seam_rot_deg": round(seam_rot, 3), "loop_seam_ankle_m": round(seam_pos, 4),
        "ankle_height_rest_m": round(float(toe[1]), 4),
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("clips", nargs="+")
    p.add_argument("--feet", default="foot.L,foot.R")
    p.add_argument("--thighs", default="thigh.L,thigh.R")
    p.add_argument("--json")
    a = p.parse_args()
    out = [analyse(c, a.feet.split(","), a.thighs.split(",")) for c in a.clips]
    for r in out:
        print(json.dumps(r))
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump(out, fh, indent=1)


if __name__ == "__main__":
    main()
