"""A facial performance captured from ordinary video as standard channel curves (charstd): MediaPipe Face Landmarker (Apache-2.0) reads
the 52 ARKit-named blendshape coefficients per frame, plus the head's rotation; the result is data keyed by channel name - no mesh,
no vertex indices - so the same performance plays on any face that carries the channel contract (play_face_performance.py).

    python capture_face_performance.py --video clip.mp4 --out performance.json [--start 0] [--end 5.0] [--smooth 2]

performance.json: {"fps", "source", "channels": {name: [value per frame]}, "head_euler_deg": [[x, y, z] per frame]}.
"""
import argparse
import json
import os

import cv2
import numpy as np
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

MODEL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "assets", "_staging", "charprod", "models",
                     "face_landmarker.task")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--video", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--start", type=float, default=0.0)
    ap.add_argument("--end", type=float, default=1e9)
    ap.add_argument("--smooth", type=int, default=2, help="half-width in frames of a moving average (capture jitter)")
    a = ap.parse_args()
    opts = vision.FaceLandmarkerOptions(base_options=mp_python.BaseOptions(model_asset_path=os.path.abspath(MODEL)), num_faces=1,
                                        running_mode=vision.RunningMode.VIDEO, output_face_blendshapes=True,
                                        output_facial_transformation_matrixes=True)
    cap = cv2.VideoCapture(a.video)
    fps = cap.get(cv2.CAP_PROP_FPS) or 24.0
    rows, heads, missing, i = [], [], 0, 0
    with vision.FaceLandmarker.create_from_options(opts) as lm:
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            t = i / fps
            i += 1
            if t < a.start or t > a.end:
                continue
            img = mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
            res = lm.detect_for_video(img, int(t * 1000))
            if not res.face_blendshapes:
                missing += 1
                rows.append(rows[-1] if rows else {})
                heads.append(heads[-1] if heads else [0, 0, 0])
                continue
            rows.append({c.category_name: float(c.score) for c in res.face_blendshapes[0] if c.category_name != "_neutral"})
            m = np.array(res.facial_transformation_matrixes[0])[:3, :3]
            sy = np.hypot(m[0, 0], m[1, 0])
            heads.append([float(np.degrees(np.arctan2(m[2, 1], m[2, 2]))), float(np.degrees(np.arctan2(-m[2, 0], sy))),
                          float(np.degrees(np.arctan2(m[1, 0], m[0, 0])))])
    names = sorted({k for r in rows for k in r})
    k = a.smooth
    kernel = np.ones(2 * k + 1) / (2 * k + 1) if k > 0 else np.ones(1)
    channels = {}
    for n in names:
        v = np.array([r.get(n, 0.0) for r in rows])
        channels[n] = np.convolve(np.pad(v, k, mode="edge"), kernel, mode="valid").round(4).tolist() if k else v.round(4).tolist()
    h = np.array(heads)
    h = h - h[: max(1, len(h) // 10)].mean(0)   # relative to the performance's opening pose
    out = {"fps": fps, "frames": len(rows), "source": os.path.abspath(a.video), "span_s": [a.start, min(a.end, i / fps)],
           "missing_frames": missing, "channels": channels, "head_euler_deg": h.round(2).tolist(),
           "note": "MediaPipe Face Landmarker blendshapes (ARKit names), moving-average smoothed; head rotation relative to the start"}
    json.dump(out, open(a.out, "w"))
    peak = sorted(((max(v), n) for n, v in channels.items()), reverse=True)[:8]
    print("CAPTURE", len(rows), "frames", missing, "missing; strongest channels", [(n, round(p, 2)) for p, n in peak])


if __name__ == "__main__":
    main()
