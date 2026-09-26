"""Fit the concept's camera to the reconstructed mesh: the perspective camera under which the mesh's silhouette best
matches the concept's figure mask (IoU), so the concept's own pixels can be projected back onto the surface.

The image-to-3D step (Pixal3D) is pixel-aligned: its mesh re-projects onto its input image under one perspective
camera. That camera is recovered here by search rather than re-derived from the template's crop/FOV conventions,
so the same fit also holds after cleanup's rescale and the rig's re-centring (a similarity of the raw mesh).

    python fit_camera.py <mesh.glb> <concept_mask.png> <camera.json> [--overlay concept.png overlay.jpg]

camera.json: {"width", "height", "f" (px), "c" [px], "R" (world->camera 3x3), "t" (3), "iou"}.
Camera convention: x right, y down, z forward (into the image); pixel = f * (x, y) / z + c.
"""
import argparse
import json

import cv2
import numpy as np
from PIL import Image
from scipy.optimize import minimize

from glb import Glb


def rotation(yaw, pitch, roll):
    cy, sy, cp, sp, cr, sr = np.cos(yaw), np.sin(yaw), np.cos(pitch), np.sin(pitch), np.cos(roll), np.sin(roll)
    ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])
    rz = np.array([[cr, -sr, 0], [sr, cr, 0], [0, 0, 1]])
    return rz @ rx @ ry


# World (glTF: +Y up, the model faces +Z) to a camera on +Z looking back at it: x right, y down, z forward.
BASE = np.array([[1.0, 0, 0], [0, -1, 0], [0, 0, -1]])


def camera(p):
    f, tx, ty, tz, yaw, pitch, roll = p
    R = rotation(yaw, pitch, roll) @ BASE
    return f, R, np.array([tx, ty, tz])


def project(points, f, R, t, c):
    q = points @ R.T + t
    return f * q[:, :2] / q[:, 2:3] + c, q[:, 2]


def silhouette(points, faces, f, R, t, c, size, scale):
    uv, z = project(points, f, R, t, c)
    img = np.zeros((size[1], size[0]), np.uint8)
    if (z <= 0.01).any():
        return img
    tris = np.round(uv[faces] * scale * 16).astype(np.int32)
    # One convex fill per triangle: a single fillPoly over all of them is even-odd, so overlaps would cancel.
    for tri in tris:
        cv2.fillConvexPoly(img, tri, 1, lineType=cv2.LINE_8, shift=4)
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mesh")
    ap.add_argument("mask")
    ap.add_argument("out")
    ap.add_argument("--overlay", nargs=2)
    args = ap.parse_args()

    m = Glb(args.mesh).mesh()
    pts, faces = m["POSITION"].astype(np.float64), m["faces"]
    mask_full = np.asarray(Image.open(args.mask).convert("L")) > 127
    H, W = mask_full.shape
    work = 384
    scale = work / max(W, H)
    size = (int(round(W * scale)), int(round(H * scale)))
    target = cv2.resize(mask_full.astype(np.uint8), size, interpolation=cv2.INTER_AREA) > 0
    c = np.array([W / 2, H / 2])

    ys, xs = np.nonzero(mask_full)
    lo, hi = pts.min(0), pts.max(0)
    height_px = ys.max() - ys.min()
    fov = np.radians(49.13)
    f0 = (W / 2) / np.tan(fov / 2)
    dist = f0 * (hi[1] - lo[1]) / height_px
    centre_px = np.array([(xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2])
    centre_w = (lo + hi) / 2
    # Solve t so the bbox centre lands on the mask centre at distance dist.
    off = (centre_px - c) * dist / f0
    t0 = np.array([off[0] - centre_w[0], off[1] + centre_w[1], dist + centre_w[2]])
    p0 = np.array([f0, *t0, 0.0, 0.0, 0.0])

    def loss(p):
        f, R, t = camera(p)
        s = silhouette(pts, faces, f, R, t, c, size, scale) > 0
        inter = (s & target).sum()
        union = (s | target).sum()
        return 1.0 - inter / max(union, 1)

    best = p0
    steps = np.array([f0 * 0.05, 0.05, 0.05, 0.3, 0.03, 0.03, 0.02])
    for _ in range(3):
        simplex = np.vstack([best] + [best + np.eye(7)[i] * steps[i] for i in range(7)])
        r = minimize(loss, best, method="Nelder-Mead", options={"initial_simplex": simplex, "maxiter": 1500, "xatol": 1e-4, "fatol": 1e-5})
        best, steps = r.x, steps * 0.3
    # Final IoU at full resolution.
    f, R, t = camera(best)
    full = silhouette(pts, faces, f, R, t, c, (W, H), 1.0) > 0
    iou = (full & mask_full).sum() / (full | mask_full).sum()
    out = {"width": W, "height": H, "f": float(f), "c": c.tolist(), "R": R.tolist(), "t": t.tolist(), "iou": float(iou),
           "params": best.tolist(), "mesh": args.mesh}
    json.dump(out, open(args.out, "w"), indent=1)
    print(f"FIT_CAMERA iou {iou:.4f} f {f:.1f} (fov {np.degrees(2 * np.arctan(W / 2 / f)):.2f} deg) t {np.round(t, 3)} "
          f"yaw/pitch/roll {np.round(np.degrees(best[4:]), 2)}")
    if args.overlay:
        concept = np.asarray(Image.open(args.overlay[0]).convert("RGB")).copy()
        edges = cv2.Canny(full.astype(np.uint8) * 255, 50, 150) > 0
        concept[edges] = (255, 0, 255)
        miss = mask_full & ~full
        extra = full & ~mask_full
        concept[miss] = concept[miss] // 2 + np.array([0, 100, 0], np.uint8)
        concept[extra] = concept[extra] // 2 + np.array([100, 0, 0], np.uint8)
        Image.fromarray(concept).save(args.overlay[1], quality=88)


if __name__ == "__main__":
    main()
