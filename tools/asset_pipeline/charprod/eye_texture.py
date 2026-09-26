"""An eyeball's colour map in the UV eyes.py gives it (the front hemisphere projected along the gaze onto a disk; the
rim of the disk is the eyeball's equator): sclera, a limbal ring, an iris of radial fibres in the concept's own iris
colour, and the pupil. Procedural (seeded) apart from the iris colour, which is sampled inside the concept's irises.

    python eye_texture.py <concept.png> <landmarks.json> <out.png> [--size 512] [--iris-mm 6.0] [--eye-mm 12.0]
"""
import argparse
import json

import numpy as np
from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("concept")
    ap.add_argument("landmarks")
    ap.add_argument("out")
    ap.add_argument("--size", type=int, default=512)
    ap.add_argument("--iris-mm", type=float, default=6.0)
    ap.add_argument("--eye-mm", type=float, default=12.0)
    ap.add_argument("--seed", type=int, default=7)
    a = ap.parse_args()
    concept = np.asarray(Image.open(a.concept).convert("RGB")).astype(np.float32) / 255
    marks = json.load(open(a.landmarks))
    samples = []
    for e in marks["eyes"].values():
        cx, cy = e["iris_centre"]
        r = e["iris_radius"]
        ys, xs = np.mgrid[int(cy - r) - 1:int(cy + r) + 2, int(cx - r) - 1:int(cx + r) + 2]
        d = np.hypot(xs + 0.5 - cx, ys + 0.5 - cy) / r
        ring = (d > 0.4) & (d < 0.85)
        samples.append(concept[ys[ring], xs[ring]])
    s = np.concatenate(samples)
    lum = s @ np.array([0.2126, 0.7152, 0.0722])
    # neither the pupil nor a highlight, nor the lids' skin that a ~5 px iris ring always catches
    keep = (lum > np.percentile(lum, 20)) & (lum < np.percentile(lum, 90)) & (s[:, 0] < s[:, 2] * 1.12)
    if keep.sum() < 8:
        keep = (lum > np.percentile(lum, 20)) & (lum < np.percentile(lum, 90))
    iris = s[keep].mean(0)
    # A painted concept iris is dim under its lids' shadow: lift it to a plausible iris luminance, keeping the hue.
    iris = np.clip(iris * max(1.0, 0.32 / max(float(iris @ np.array([0.2126, 0.7152, 0.0722])), 1e-3)), 0, 1)

    n = a.size
    rng = np.random.default_rng(a.seed)
    v, u = (np.mgrid[0:n, 0:n] + 0.5) / n - 0.5
    r = np.hypot(u, v)                         # 0 .. 0.5 (the equator)
    ang = np.arctan2(v, u)
    ri = 0.5 * a.iris_mm / a.eye_mm            # the iris edge in UV
    rp = ri * 0.32                             # the pupil
    img = np.zeros((n, n, 3), np.float32)
    sclera = np.array([0.86, 0.82, 0.78])
    img[:] = sclera * (1 - 0.25 * np.clip((r - 0.3) / 0.2, 0, 1))[..., None]
    # Faint vessels from the equator inward.
    vessel = np.zeros((n, n), np.float32)
    for _ in range(14):
        th = rng.uniform(-np.pi, np.pi)
        rr, width = 0.5, rng.uniform(0.0015, 0.003)
        stop = rng.uniform(ri + 0.05, 0.4)
        while rr > stop:
            d = np.hypot(u - rr * np.cos(th), v - rr * np.sin(th))
            vessel = np.maximum(vessel, np.exp(-(d / width) ** 2) * (rr - stop) / (0.5 - stop))
            th += rng.normal(0, 0.012)
            rr -= 0.0015
    img = img * (1 - 0.3 * vessel)[..., None] + np.array([0.6, 0.15, 0.12]) * 0.3 * vessel[..., None]
    # Iris: radial fibres (noise over the angle, slowly varying with the radius), a lighter collarette, a dark limbus.
    k = np.arange(1, 96)
    phase = rng.uniform(0, 2 * np.pi, (len(k), 2))
    amp = rng.uniform(0.2, 1.0, len(k)) / np.sqrt(k)
    t = (r - rp) / (ri - rp)
    fib = sum(amp[i] * np.sin(k[i] * ang + phase[i, 0] + 3.0 * np.sin(2 * np.pi * t + phase[i, 1]) * 0.3) for i in range(len(k)))
    fib = fib / np.abs(fib).max()
    shade = 0.8 + 0.28 * fib + 0.18 * np.exp(-((t - 0.3) / 0.12) ** 2) - 0.35 * np.clip((t - 0.8) / 0.2, 0, 1)
    iris_px = iris[None, None, :] * shade[..., None]
    inside = r < ri
    edge = np.clip((ri - r) / 0.01, 0, 1)[..., None]
    img = np.where(inside[..., None], iris_px * edge + img * (1 - edge), img)
    limbus = np.exp(-((r - ri) / 0.012) ** 2)[..., None]
    img = img * (1 - 0.6 * limbus)
    pupil = np.clip((rp - r) / 0.006 + 0.5, 0, 1)[..., None]
    img = img * (1 - pupil) + np.array([0.015, 0.012, 0.012]) * pupil
    Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)).save(a.out)
    print("EYE_TEXTURE " + json.dumps({"iris_rgb": [round(float(x), 3) for x in iris], "iris_uv_radius": ri, "pupil_uv_radius": rp}))


if __name__ == "__main__":
    main()
