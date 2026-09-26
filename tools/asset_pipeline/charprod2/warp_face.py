"""The reference face bent onto an MPFB head's own features: a thin-plate spline through the 478 MediaPipe landmark pairs (the
render's and the reference's, from solve_face_fit.py landmarks) maps every pixel of the render's frame to the reference, so the
reference's eyes, brows, nose and mouth land exactly where the mesh has them. Also a feathered face mask in the same frame.

    python warp_face.py --dir <facefit dir> --reference ref.png [--size 1024]
"""
import argparse
import json
import os

import cv2
import numpy as np
from scipy.interpolate import RBFInterpolator

IRIS = set(range(468, 478))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True)
    ap.add_argument("--reference", required=True)
    ap.add_argument("--size", type=int, default=1024)
    a = ap.parse_args()
    ren = np.array(json.load(open(os.path.join(a.dir, "lm_render.json")))["landmarks"])[:, :2]
    ref = np.array(json.load(open(os.path.join(a.dir, "lm_reference.json")))["landmarks"])[:, :2]
    keep = [i for i in range(len(ren)) if i not in IRIS]
    ren, ref = ren[keep], ref[keep]
    # Render frame -> reference frame, smooth (thin-plate) with a little regularisation against landmark jitter.
    rbf = RBFInterpolator(ren, ref, kernel="thin_plate_spline", smoothing=2.0, degree=1)
    n = a.size
    ys, xs = np.mgrid[0:n, 0:n]
    grid = np.stack([xs.ravel(), ys.ravel()], 1).astype(np.float64)
    mapped = rbf(grid).astype(np.float32).reshape(n, n, 2)
    src = cv2.imread(a.reference, cv2.IMREAD_COLOR)
    warped = cv2.remap(src, mapped[..., 0], mapped[..., 1], cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
    cv2.imwrite(os.path.join(a.dir, "face_warped.png"), warped)
    # The face region: the landmarks' hull, grown upward to the hairline band and feathered.
    hull = cv2.convexHull(ren.astype(np.int32))
    mask = np.zeros((n, n), np.uint8)
    cv2.fillConvexPoly(mask, hull, 255)
    mask = cv2.dilate(mask, np.ones((25, 25), np.uint8))
    mask = cv2.GaussianBlur(mask, (0, 0), 14)
    cv2.imwrite(os.path.join(a.dir, "face_mask.png"), mask)
    print("WARP", os.path.join(a.dir, "face_warped.png"))


if __name__ == "__main__":
    main()
