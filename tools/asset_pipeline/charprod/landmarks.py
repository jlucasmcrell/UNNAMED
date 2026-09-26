"""Face landmarks from a character concept: iris centres and radii and the eyelid openings, in the concept's pixels
(MediaPipe Face Landmarker, Apache-2.0, run offline; nothing of it ships). Run on the head crop when there is one - the
face is ~150 px wide in a full-body concept - and mapped back into the concept's frame.

    python landmarks.py <image> <model.task> <out.json> [--crop crop.json]
"""
import argparse
import json

import numpy as np
from PIL import Image

import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

# Subject's right eye (image left) and left eye: the 16-point lid contour, the iris centre and its ring.
EYES = {
    "R": {"contour": [33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246], "iris": 468, "ring": [469, 470, 471, 472]},
    "L": {"contour": [263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466], "iris": 473, "ring": [474, 475, 476, 477]},
}
MOUTH = [61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image")
    ap.add_argument("model")
    ap.add_argument("out")
    ap.add_argument("--crop")
    a = ap.parse_args()
    img = np.asarray(Image.open(a.image).convert("RGB"))
    h, w = img.shape[:2]
    det = vision.FaceLandmarker.create_from_options(vision.FaceLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=a.model), num_faces=1))
    res = det.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(img)))
    if not res.face_landmarks:
        raise SystemExit("LANDMARKS no face found")
    pts = np.array([[p.x * w, p.y * h] for p in res.face_landmarks[0]])
    scale, off = 1.0, np.zeros(2)
    if a.crop:
        c = json.load(open(a.crop))
        scale, off = c["size"] / c["out"], np.array([c["x0"], c["y0"]])
    pts = pts * scale + off
    out = {"source": a.image, "eyes": {}}
    for side, e in EYES.items():
        ring = pts[e["ring"]]
        centre = pts[e["iris"]]
        out["eyes"][side] = {"iris_centre": centre.tolist(), "iris_radius": float(np.linalg.norm(ring - centre, axis=1).mean()),
                             "contour": pts[e["contour"]].tolist()}
    out["mouth"] = pts[MOUTH].tolist()
    out["all"] = pts.tolist()
    json.dump(out, open(a.out, "w"), indent=1)
    print("LANDMARKS " + json.dumps({k: {"centre": [round(x, 1) for x in v["iris_centre"]], "radius": round(v["iris_radius"], 2)}
                                     for k, v in out["eyes"].items()}))


if __name__ == "__main__":
    main()
