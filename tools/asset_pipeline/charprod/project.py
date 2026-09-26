"""Project the concept's own pixels onto a character's production atlas: every texel whose surface the concept
camera sees (facing it, not occluded, inside the figure) takes the concept's colour, feathered into the
reconstruction's texture (colour-matched to the concept) where the concept cannot see.

This is the correction for the Phase-A face: the image-to-3D texture stage re-synthesises colour in a voxel field
(its face has dark blobs for eyes), while the concept - the same camera, pixel-aligned - has the actual face.

De-lighting: the concept is lit (a studio key light and rim lights). A shading field s = a + b.n + rim lobes (1 - n.v)^2 toward
the camera's left, right and up is
fitted to the concept's luminance against the surface normals and view angles over what it sees and divided out
(--delight 0..1), so neither the key light's gradient nor the rim lights' bright edges are painted into the albedo.
Crevice darkening is left in (the AO map carries the same).

    python project.py --mesh layout.glb --albedo albedo_transfer.png --concept concept.png --mask concept_mask.png
        --camera camera.json --out albedo.png [--weights weights.png] [--delight 0.7] [--size 4096]
"""
import argparse
import json

import cv2
import numpy as np
from PIL import Image

from glb import Glb
from rast import interpolate, rasterize


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mesh", required=True)
    ap.add_argument("--albedo", required=True)
    ap.add_argument("--concept", required=True, help="the view for every material without a --view of its own")
    ap.add_argument("--mask", required=True)
    ap.add_argument("--camera", required=True)
    ap.add_argument("--view", nargs=4, action="append", default=[], metavar=("MATERIALS", "IMAGE", "MASK", "CAMERA"),
                    help="a grafted part seen through its own camera: comma-separated material indices, its image, mask, camera")
    ap.add_argument("--out", required=True)
    ap.add_argument("--weights")
    ap.add_argument("--size", type=int, default=4096)
    ap.add_argument("--delight", type=float, default=0.7)
    ap.add_argument("--facing", type=float, nargs=2, default=(0.2, 0.55), help="facing ramp (cos) for the projection weight")
    ap.add_argument("--depth-tolerance", type=float, default=0.006, help="metres behind the nearest surface still 'seen'")
    ap.add_argument("--mask-erode", type=int, default=5, help="px of the figure mask's edge not trusted")
    ap.add_argument("--rim-guard", type=float, default=0.0, help="bright pixels this near the silhouette (image fraction) are not projected (0: off)")
    args = ap.parse_args()

    g = Glb(args.mesh)
    m = g.mesh()
    P, N, F, UV = m["POSITION"].astype(np.float64), m["NORMAL"].astype(np.float64), m["faces"], m["TEXCOORD_0"]
    S = args.size

    # UV-space surface: which point each texel stands for.
    tri, bary, _ = rasterize(UV * S, np.zeros(len(UV), np.float32), F, S, S)
    covered = tri >= 0
    pos = interpolate(P.astype(np.float32), F, tri, bary)[covered].astype(np.float64)
    nrm = interpolate(N.astype(np.float32), F, tri, bary)[covered].astype(np.float64)
    nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-12
    part = m["material"][tri[covered]]

    views = [(set(int(x) for x in mats.split(",")), image, mask, camera) for mats, image, mask, camera in args.view]
    claimed = set().union(*[v[0] for v in views]) if views else set()
    views.append(({int(x) for x in np.unique(part)} - claimed, args.concept, args.mask, args.camera))
    weight = np.zeros(len(pos))
    colour = np.zeros((len(pos), 3))
    fits = []
    for mats, image, mask_path, camera in views:
        sel = np.isin(part, list(mats))
        if not sel.any():
            continue
        cam = json.load(open(camera))
        R, t, f, c = np.array(cam["R"]), np.array(cam["t"]), cam["f"], np.array(cam["c"])
        W, H = cam["width"], cam["height"]
        # The camera's depth buffer over the whole mesh (a part can be hidden by another).
        q = P @ R.T + t
        xy = f * q[:, :2] / q[:, 2:3] + c
        _, _, zbuf = rasterize(xy, q[:, 2].astype(np.float32), F, W, H)
        ps, ns = pos[sel], nrm[sel]
        qc = ps @ R.T + t
        px = f * qc[:, :2] / qc[:, 2:3] + c
        view = -R.T @ t - ps
        view /= np.linalg.norm(view, axis=1, keepdims=True)
        facing = (ns * view).sum(1)
        ix = np.clip(np.floor(px).astype(int), 0, [W - 1, H - 1])
        on = (px[:, 0] >= 0) & (px[:, 0] < W) & (px[:, 1] >= 0) & (px[:, 1] < H)
        seen = qc[:, 2] <= zbuf[ix[:, 1], ix[:, 0]] + args.depth_tolerance
        mask = np.asarray(Image.open(mask_path).convert("L")) > 127
        if args.mask_erode:
            mask = cv2.erode(mask.astype(np.uint8), np.ones((2 * args.mask_erode + 1,) * 2, np.uint8)) > 0
        w = smoothstep(*args.facing, facing) * seen * mask[ix[:, 1], ix[:, 0]] * on
        img = np.asarray(Image.open(image).convert("RGB")).astype(np.float32) / 255.0
        if args.rim_guard:
            # A rim light clips the concept to near-white along the silhouette; no shading model divides that out.
            # Bright pixels within --rim-guard (a fraction of the image) of the figure's edge are not projected: the
            # part's own texture fills them.
            edge = cv2.distanceTransform(mask.astype(np.uint8), cv2.DIST_L2, 5)
            ilum = img @ np.array([0.2126, 0.7152, 0.0722], np.float32)
            bright = ilum > np.percentile(ilum[mask], 85) * 1.1
            guard = (edge < args.rim_guard * max(W, H)) & bright
            guard = cv2.dilate(guard.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
            w = w * ~guard[ix[:, 1], ix[:, 0]]
        # remap wants a 2-D map under 32767 on a side: the sample list is laid out in rows of 4096.
        n = len(px)
        rows = -(-n // 4096)
        mx = np.zeros(rows * 4096, np.float32)
        my = np.zeros(rows * 4096, np.float32)
        mx[:n], my[:n] = px[:, 0] - 0.5, px[:, 1] - 0.5
        col = cv2.remap(img, mx.reshape(rows, 4096), my.reshape(rows, 4096), cv2.INTER_CUBIC,
                        borderMode=cv2.BORDER_REPLICATE).reshape(-1, 3)[:n]
        col = np.clip(col, 0, 1)
        # De-light: fit luminance ~ a + b.n over confident samples, divide the fitted field out (normalised to its mean).
        # A studio concept also has rim lights, bright where the surface turns away from the camera: the shading
        # model carries a rim term r.(1 - n.v)^2 beside the key light's a + b.n, and both are divided out.
        lum = col @ np.array([0.2126, 0.7152, 0.0722])
        sure_v = w > 0.05
        # Rim lights come from a side: the grazing term is split into lobes toward the camera's left, right and up.
        graze = (1 - np.clip(facing, 0, 1)) ** 2
        side = ns @ R.T  # normals in camera axes: x right, y down
        basis = np.c_[np.ones(len(ns)), ns, graze * np.clip(side[:, 0], 0, None), graze * np.clip(-side[:, 0], 0, None),
                      graze * np.clip(-side[:, 1], 0, None)]
        coef, *_ = np.linalg.lstsq(basis[sure_v] * w[sure_v, None], lum[sure_v] * w[sure_v], rcond=None)
        coef[4:] = np.clip(coef[4:], 0.0, None)
        shade = np.clip((basis @ coef) / (basis[w > 0.8] @ coef).mean(), 0.4, 2.0)
        col = np.clip(col / (1 + args.delight * (shade - 1))[:, None], 0, 1)
        weight[sel] = w
        colour[sel] = col
        fits.append({"materials": sorted(mats), "image": image, "shading_fit": coef.tolist(), "projected": float((w > 0.5).mean())})
    sure = weight > 0.8

    # Colour-match the reconstruction's texture to the concept: per channel, mean and spread over the confident overlap
    # (the two textures do not agree texel by texel - the reconstruction's is re-synthesised - so no per-texel fit).
    # Per source material: a grafted part's own texture has its own tones.
    base_img = np.asarray(Image.open(args.albedo).convert("RGB").resize((S, S), Image.LANCZOS)).astype(np.float32) / 255.0
    base = base_img[covered]
    gains = {}
    for mat in np.unique(part):
        sel = part == mat
        ref = sure & sel if (sure & sel).sum() >= 2000 else sure
        g = []
        for ch in range(3):
            x, y = base[ref, ch], colour[ref, ch]
            k = float(np.clip(y.std() / max(x.std(), 1e-4), 0.6, 1.6))
            b = float(np.clip(y.mean() - k * x.mean(), -0.2, 0.2))
            g.append((k, b))
            base[sel, ch] = np.clip(k * base[sel, ch] + b, 0, 1)
        gains[int(mat)] = g

    out = base_img.copy()
    blend = weight[:, None]
    out[covered] = base * (1 - blend) + colour * blend
    # Edge padding: every empty texel takes its nearest chart's colour (mips must not bleed black into charts).
    empty = ~covered
    if empty.any():
        _, idx = cv2.distanceTransformWithLabels(empty.astype(np.uint8), cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL)
        ys, xs = np.nonzero(covered)
        lut = np.zeros((idx.max() + 1, 2), np.int64)
        lut[idx[covered]] = np.c_[ys, xs]
        src = lut[idx[empty]]
        out[empty] = out[src[:, 0], src[:, 1]]
    Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8)).save(args.out)
    if args.weights:
        wimg = np.zeros((S, S), np.float32)
        wimg[covered] = weight
        Image.fromarray((wimg * 255).astype(np.uint8)).save(args.weights)
    report = {"texels": int(covered.sum()), "projected_share": float((weight > 0.5).mean()), "views": fits,
              "colour_match": gains, "delight": args.delight}
    print("PROJECT_RESULT " + json.dumps(report))


if __name__ == "__main__":
    main()
