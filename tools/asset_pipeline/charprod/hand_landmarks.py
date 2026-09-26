"""Hand landmarks from a character concept: the 21 MediaPipe hand joints (wrist, and four per finger) for each hand, in
the concept's pixels (MediaPipe Hand Landmarker, Apache-2.0, run offline; nothing of it ships). Each hand is found as
the lowest part of the figure's arm on its side, cropped, upscaled (a hand is ~80 px in a full-body concept) and
detected on its own.

    python hand_landmarks.py <concept.png> <figure_mask.png> <model.task> <out.json> [--preview preview.jpg]
"""
import argparse
import json

import numpy as np
from PIL import Image, ImageDraw

import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

FINGERS = {"thumb": [1, 2, 3, 4], "index": [5, 6, 7, 8], "middle": [9, 10, 11, 12], "ring": [13, 14, 15, 16], "pinky": [17, 18, 19, 20]}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("concept")
    ap.add_argument("mask")
    ap.add_argument("model")
    ap.add_argument("out")
    ap.add_argument("--preview")
    ap.add_argument("--hand-fraction", type=float, default=0.12, help="the hand's share of the figure's height")
    a = ap.parse_args()
    img = Image.open(a.concept).convert("RGB")
    m = np.asarray(Image.open(a.mask).convert("L")) > 127
    ys, xs = np.nonzero(m)
    height = ys.max() - ys.min()
    cx = (xs.min() + xs.max()) / 2
    torso_half = 0.16 * height
    det = vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=a.model), num_hands=1, min_hand_detection_confidence=0.2,
        min_hand_presence_confidence=0.2))
    out = {"source": a.concept, "hands": {}}
    for side, sel in (("image_left", xs < cx - torso_half), ("image_right", xs > cx + torso_half)):
        yy, xx = ys[sel], xs[sel]
        # The lowest reach of the arm on this side is the hand (arms hang in the bind pose).
        low = yy > yy.max() - a.hand_fraction * height
        hx, hy = xx[low], yy[low]
        c = np.array([(hx.min() + hx.max()) / 2, (hy.min() + hy.max()) / 2])
        half = int(max(hx.max() - hx.min(), hy.max() - hy.min()) * 0.8) + 10
        box = (int(c[0] - half), int(c[1] - half), int(c[0] + half), int(c[1] + half))
        crop = img.crop(box).resize((4 * 2 * half,) * 2, Image.LANCZOS)
        flipped = False
        res = det.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(np.asarray(crop))))
        if not res.hand_landmarks:
            # The detector is not symmetric: the mirrored crop often reads where the plain one did not.
            res = det.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(np.asarray(crop)[:, ::-1])))
            flipped = True
        if not res.hand_landmarks:
            out["hands"][side] = None
            print(f"HAND {side}: not found")
            continue
        s = (2 * half) / crop.size[0]
        pts = np.array([[(1 - p.x if flipped else p.x) * crop.size[0], p.y * crop.size[1]] for p in res.hand_landmarks[0]]) * s + np.array(box[:2])
        out["hands"][side] = {"points": pts.tolist(), "handedness": res.handedness[0][0].category_name,
                              "score": float(res.handedness[0][0].score), "box": box}
        print(f"HAND {side}: {res.handedness[0][0].category_name} {res.handedness[0][0].score:.2f}")
    # A hand still not found is the other one mirrored across the figure's midline (a bind pose is symmetric).
    found = [k for k, v in out["hands"].items() if v]
    for side, h in list(out["hands"].items()):
        if h is None and found:
            src = out["hands"][found[0]]
            pts = np.array(src["points"])
            pts[:, 0] = 2 * cx - pts[:, 0]
            out["hands"][side] = {"points": pts.tolist(), "handedness": "mirrored", "score": 0.0,
                                  "box": [int(2 * cx - src["box"][2]), src["box"][1], int(2 * cx - src["box"][0]), src["box"][3]]}
            print(f"HAND {side}: mirrored from {found[0]}")
    out["fingers"] = FINGERS
    json.dump(out, open(a.out, "w"), indent=1)
    if a.preview:
        d = ImageDraw.Draw(img)
        for h in out["hands"].values():
            if h:
                p = h["points"]
                for chain in FINGERS.values():
                    d.line([tuple(p[0])] + [tuple(p[i]) for i in chain], fill=(255, 0, 255), width=1)
        crops = [img.crop(h["box"]).resize((360, 360), Image.NEAREST) for h in out["hands"].values() if h]
        sheet = Image.new("RGB", (360 * max(1, len(crops)), 360))
        for i, cimg in enumerate(crops):
            sheet.paste(cimg, (360 * i, 0))
        sheet.save(a.preview)


if __name__ == "__main__":
    main()
