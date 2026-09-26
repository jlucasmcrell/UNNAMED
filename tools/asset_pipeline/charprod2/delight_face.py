"""A face reference with its lighting taken out, for projection onto another head (warp_face.py): the low-frequency part of the
lightness (the source's own eye sockets, cheek and nose shadows, baked into its texture) replaced by the face's mean, the detail
(stubble, pores, dirt, colour) kept. Without this the source's shadows read as make-up on a head of a different shape.

    python delight_face.py --reference face.png --out face_delit.png [--sigma 0.035] [--strength 0.8]
"""
import argparse

import cv2
import numpy as np


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--reference", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--sigma", type=float, default=0.035, help="the shading's scale, as a fraction of the image width")
    ap.add_argument("--strength", type=float, default=0.8)
    a = ap.parse_args()
    img = cv2.imread(a.reference, cv2.IMREAD_UNCHANGED)
    alpha = img[..., 3].astype(np.float32) / 255 if img.shape[2] == 4 else np.ones(img.shape[:2], np.float32)
    lab = cv2.cvtColor(img[..., :3], cv2.COLOR_BGR2LAB).astype(np.float32)
    s = a.sigma * img.shape[1]
    # Normalised convolution: the background (transparent) does not darken the face's edges.
    low = cv2.GaussianBlur(lab[..., 0] * alpha, (0, 0), s) / np.maximum(cv2.GaussianBlur(alpha, (0, 0), s), 1e-3)
    # The level the face keeps: its centre's (the fit camera frames the face centred), not the whole cut-out's, which the hair darkens.
    h, w = alpha.shape
    yy, xx = np.mgrid[:h, :w]
    centre = ((yy - h / 2) ** 2 + (xx - w / 2) ** 2 < (0.18 * w) ** 2) & (alpha > 0.5)
    mean = float(np.median(low[centre]))
    lab[..., 0] = np.clip(lab[..., 0] - a.strength * (low - mean), 0, 255)
    out = cv2.cvtColor(lab.astype(np.uint8), cv2.COLOR_LAB2BGR)
    if img.shape[2] == 4:
        out = np.dstack([out, img[..., 3]])
    cv2.imwrite(a.out, out)
    print("DELIGHT", a.out, "mean L", round(mean, 1))


if __name__ == "__main__":
    main()
