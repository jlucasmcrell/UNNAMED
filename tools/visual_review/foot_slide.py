"""Foot sliding measured from a showcase's per-frame state log (--state-log, columns foot_l / foot_r = the ankles' world position): once
per stride a planted foot comes to rest, and how fast it still travels at its stillest moment is the slide. Per foot, the ankle's speed
(3D, averaged over five frames) is followed while the body moves; each dip below half the body's speed that is the lowest within 0.3 s
either side is one stride's stillest moment. Reported per clip state: the median and 90th-percentile of those minima (m/s) and the number of strides.
Slopes do not matter (a height threshold on a hill picks the swing of a foot on the lower step), and neither does the captured roll of a
real stance (the ankle swings over the heel and the toe, but between the two the foot is flat and still).

    python foot_slide.py states.csv [states2.csv ...]
"""
import csv
import statistics
import sys
from collections import defaultdict


def analyse(path):
    rows = list(csv.DictReader(open(path, encoding="utf-8")))
    if not rows or "foot_l" not in rows[0]:
        return {"error": "no foot columns"}
    t = [float(r["t_s"]) for r in rows]
    state = [r["player_state"] for r in rows]
    body = [float(r["player_speed"]) for r in rows]
    per = defaultdict(list)
    for key in ("foot_l", "foot_r"):
        pts = [tuple(float(v) for v in r[key].split()) if r[key] != "-" else None for r in rows]
        raw = [None] * len(pts)
        for i in range(1, len(pts) - 1):
            a, b = pts[i - 1], pts[i + 1]
            if a and b and t[i + 1] > t[i - 1]:
                raw[i] = sum((b[k] - a[k]) ** 2 for k in range(3)) ** 0.5 / (t[i + 1] - t[i - 1])
        speed = [None] * len(pts)
        for i in range(2, len(pts) - 2):
            w = [s for s in raw[i - 2:i + 3] if s is not None]
            speed[i] = sum(w) / len(w) if len(w) == 5 else None
        for i in range(3, len(pts) - 3):
            s = speed[i]
            if s is None or body[i] < 0.25 or s >= 0.5 * body[i]:
                continue
            near = [j for j in range(max(0, i - 40), min(len(pts), i + 41)) if abs(t[j] - t[i]) <= 0.3 and speed[j] is not None]
            if any(speed[j] < s or (speed[j] == s and j < i) for j in near if j != i):
                continue
            per[state[i]].append(s)
    out = {}
    for s, v in sorted(per.items()):
        v.sort()
        out[s] = {"median": round(statistics.median(v), 3), "p90": round(v[int(0.9 * (len(v) - 1))], 3), "strides": len(v)}
    return out


for path in sys.argv[1:]:
    print(path)
    for s, r in analyse(path).items():
        print(f"  {s:16s} {r}")
