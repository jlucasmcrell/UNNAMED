"""Fit an MPFB character's face to a reference face: the side outside Blender (MediaPipe Face Landmarker, Apache-2.0; scipy).

    landmarks: 478 landmarks on the render and on the reference (pixels, with MediaPipe's relative depth)
    solve:     the reference's landmarks laid onto the render's by a 3D similarity (so MediaPipe's own biases cancel: both faces are
               read by the same estimator), the displacement each render landmark must make, and the MPFB face-target weights that
               best make it - a bounded, regularised least squares over every target's measured effect (probe.npz from
               mpfb_face_fit.py --mode probe). Depth is weighted half (MediaPipe infers it from one view).

    python solve_face_fit.py landmarks --render face.png --reference ref.png --out <dir>
    python solve_face_fit.py solve --out <dir> [--lam 0.02] [--points3d <dir>/source_points.json]
"""
import argparse
import json
import os

import numpy as np
from PIL import Image

MODEL = os.path.join(os.path.dirname(__file__), "..", "..", "..", "assets", "_staging", "charprod", "models", "face_landmarker.task")
IRIS = set(range(468, 478))


def detect(path):
    import mediapipe as mp
    from mediapipe.tasks import python as mp_python
    from mediapipe.tasks.python import vision
    opts = vision.FaceLandmarkerOptions(base_options=mp_python.BaseOptions(model_asset_path=os.path.abspath(MODEL)), num_faces=1)
    im = Image.open(path).convert("RGB")
    w, h = im.size
    with vision.FaceLandmarker.create_from_options(opts) as det:
        res = det.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.asarray(im)))
    if not res.face_landmarks:
        raise SystemExit(f"no face found in {path}")
    return [[p.x * w, p.y * h, p.z * w] for p in res.face_landmarks[0]], (w, h)


def umeyama(src, dst):
    """Similarity (s, R, t) with dst ~ s R src + t."""
    ms, md = src.mean(0), dst.mean(0)
    a, b = src - ms, dst - md
    u, sig, vt = np.linalg.svd(b.T @ a / len(src))
    d = np.eye(3)
    if np.linalg.det(u @ vt) < 0:
        d[2, 2] = -1
    r = u @ d @ vt
    s = np.trace(np.diag(sig) @ d) / (a ** 2).sum(1).mean()
    return s, r, md - s * r @ ms


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["landmarks", "solve"])
    ap.add_argument("--render")
    ap.add_argument("--reference")
    ap.add_argument("--out", required=True)
    ap.add_argument("--lam", type=float, default=0.02)
    ap.add_argument("--depth-weight", type=float, default=0.5)
    ap.add_argument("--points3d", help="the reference's landmarks as 3D points on its own mesh (lift_landmarks.py): solve against those")
    ap.add_argument("--max-weight", type=float, default=1.0,
                    help="each target's ceiling: stacked targets at their full range caricature a feature (a beak of a nose)")
    ap.add_argument("--exclude", default="upperlip-height,lowerlip-height,mouth-scale-vert,upperlip-middle,lowerlip-middle",
                    help="targets held at zero (substrings): by default those that change where the lips meet, so the neutral mouth "
                         "stays closed")
    a = ap.parse_args()
    if a.mode == "landmarks":
        ren, rs = detect(a.render)
        ref, fs = detect(a.reference)
        json.dump({"landmarks": ren, "size": rs}, open(os.path.join(a.out, "lm_render.json"), "w"))
        json.dump({"landmarks": ref, "size": fs}, open(os.path.join(a.out, "lm_reference.json"), "w"))
        print("LANDMARKS", len(ren), len(ref))
        return
    probe = json.load(open(os.path.join(a.out, "probe.json")))["camera"]
    ren = np.array(json.load(open(os.path.join(a.out, "lm_render.json")))["landmarks"])
    ref = np.array(json.load(open(os.path.join(a.out, "lm_reference.json")))["landmarks"])
    npz = np.load(os.path.join(a.out, "probe.npz"))
    idx = npz["landmark_index"]
    disp = npz["disp"]            # targets x landmarks x 3 (world metres)
    names = [str(n) for n in npz["names"]]
    if a.points3d:
        # The reference as real 3D points on the source's mesh (lift_landmarks.py): a 3D similarity onto the MPFB landmarks' own
        # surface points, and what is left is what the face targets must do. Depth counts fully: it is measured, not inferred.
        src = json.load(open(a.points3d))["points"]
        pts = npz["points"]
        pairs = [k for k, i in enumerate(idx) if i not in IRIS and src[i] is not None]
        P = np.array([src[idx[k]] for k in pairs])
        Q = pts[pairs]
        s, r, t = umeyama(P, Q)
        # Pairs far off the similarity were cast onto the wrong surface (a ray past the jaw's edge onto the neck or an ear): dropped.
        res = np.linalg.norm((s * (r @ P.T)).T + t - Q, axis=1)
        good = res < 3 * np.median(res)
        pairs = [k for k, g in zip(pairs, good) if g]
        P, Q = P[good], Q[good]
        s, r, t = umeyama(P, Q)
        want = np.zeros((len(ren), 3))
        for k, (i, p) in enumerate(zip(idx[pairs], (s * (r @ P.T)).T + t)):
            want[i] = p - Q[k]
        keep = np.array(pairs)
        w_axis = np.array([1.0, 1.0, 1.0])
    else:
        use = np.array([i for i in range(len(ren)) if i not in IRIS])
        s, r, t = umeyama(ref[use], ren[use])
        aligned = (s * (r @ ref.T)).T + t
        f = probe["scale"] / probe["res"]
        d_pix = aligned - ren
        # Pixels to world: x right = +x, image down = -z, MediaPipe depth (smaller = nearer the camera, which looks along +y) = +y.
        want = np.stack([d_pix[:, 0] * f, d_pix[:, 2] * f, -d_pix[:, 1] * f], 1)
        keep = np.array([k for k, i in enumerate(idx) if i not in IRIS])
        w_axis = np.array([1.0, a.depth_weight, 1.0])
    A = (disp[:, keep, :] * w_axis).reshape(len(names), -1).T
    b = (want[idx[keep]] * w_axis).reshape(-1)
    from scipy.optimize import lsq_linear
    reg = np.sqrt(a.lam) * np.eye(len(names)) * np.abs(A).max()
    held = [any(x and x in n for x in a.exclude.split(",")) for n in names]
    upper = np.where(held, 1e-9, a.max_weight)
    sol = lsq_linear(np.vstack([A, reg]), np.concatenate([b, np.zeros(len(names))]), bounds=(np.zeros(len(names)), upper))
    wts = sol.x
    before = np.linalg.norm(want[idx[keep]], axis=1)
    after = np.linalg.norm(want[idx[keep]] - (disp[:, keep, :] * wts[:, None, None]).sum(0), axis=1)
    top = sorted(((round(float(w), 3), n) for w, n in zip(wts, names) if w > 0.02), reverse=True)
    json.dump({"weights": {n: float(w) for n, w in zip(names, wts)}, "similarity_scale": float(s),
               "residual_mm_before": [round(float(np.mean(before)) * 1000, 2), round(float(np.max(before)) * 1000, 2)],
               "residual_mm_after": [round(float(np.mean(after)) * 1000, 2), round(float(np.max(after)) * 1000, 2)],
               "top": top[:25]}, open(os.path.join(a.out, "weights.json"), "w"), indent=1)
    print("SOLVE mean/max residual mm before", round(float(np.mean(before)) * 1000, 2), round(float(np.max(before)) * 1000, 2),
          "after", round(float(np.mean(after)) * 1000, 2), round(float(np.max(after)) * 1000, 2), "active", len(top))
    print("TOP", top[:15])


if __name__ == "__main__":
    main()
