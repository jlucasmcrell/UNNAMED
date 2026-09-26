"""A body's albedo from its base skin and the projected reference face: the base skin (MPFB's, upscaled to the bake's size) re-toned to
the reference's skin (mean and spread in Lab, measured where the projection is confident and the pixel reads as skin, not brow,
eye or hair), then the projected face laid over it by the bake's mask.

    python composite_skin.py --dir <facefit dir> --base skin_diffuse.png --out body_albedo.png
"""
import argparse
import os

import cv2
import numpy as np


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True)
    ap.add_argument("--base", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--strength", type=float, default=0.9, help="how far the base skin moves to the reference's tone")
    ap.add_argument("--no-face", action="store_true", help="the toned base skin only: no projected face laid over it")
    ap.add_argument("--face-opacity", type=float, default=1.0, help="how strongly the projected face covers the base skin")
    a = ap.parse_args()
    proj = cv2.imread(os.path.join(a.dir, "face_proj_color.png"), cv2.IMREAD_COLOR)
    mask = cv2.imread(os.path.join(a.dir, "face_proj_mask.png"), cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255
    n = proj.shape[0]
    base = cv2.resize(cv2.imread(a.base, cv2.IMREAD_COLOR), (n, n), interpolation=cv2.INTER_CUBIC)
    lab_p = cv2.cvtColor(proj, cv2.COLOR_BGR2LAB).astype(np.float32)
    lab_b = cv2.cvtColor(base, cv2.COLOR_BGR2LAB).astype(np.float32)
    # Skin pixels in the projection: confident, neither dark (brows, lashes, stubble shadow) nor desaturated (eyes, hair highlights).
    L, A, B = lab_p[..., 0], lab_p[..., 1], lab_p[..., 2]
    skin = (mask > 0.8) & (L > 70) & (L < 215) & (A > 135)
    base_region = (mask > 0.8) & (lab_b[..., 0] > 30)
    if skin.sum() < 500 or base_region.sum() < 500:
        raise SystemExit("too little skin to measure")
    mp, sp = lab_p[skin].mean(0), lab_p[skin].std(0) + 1e-3
    mb, sb = lab_b[base_region].mean(0), lab_b[base_region].std(0) + 1e-3
    toned = (lab_b - mb) * (sp / sb) + mp
    toned = lab_b + a.strength * (toned - lab_b)
    toned_bgr = cv2.cvtColor(np.clip(toned, 0, 255).astype(np.uint8), cv2.COLOR_LAB2BGR).astype(np.float32)
    m = cv2.GaussianBlur(mask, (0, 0), 3)[..., None] * (0.0 if a.no_face else a.face_opacity)
    outimg = toned_bgr * (1 - m) + proj.astype(np.float32) * m
    cv2.imwrite(a.out, np.clip(outimg, 0, 255).astype(np.uint8))
    print("COMPOSITE", a.out, "skin px", int(skin.sum()), "ref Lab mean", np.round(mp, 1), "base Lab mean", np.round(mb, 1))


if __name__ == "__main__":
    main()
