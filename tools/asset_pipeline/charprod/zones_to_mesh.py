"""Carry a concept's material-zone labels (segment_zones.py) onto a character mesh: each face the concept sees takes
the label under it (through its part's own camera, like the projection); a face it cannot see (the back, a hidden side)
takes the wrapping zone at its pixel (--mask: accessories that do not wrap round the body left out); what is still
unlabelled takes the nearest labelled face across the surface (a breadth-first spread over the welded face adjacency). Writes, per face in the GLB's triangle order, the zone index, and the face centroids so a later
stage can match faces after an import reorders them.

    python zones_to_mesh.py --mesh layout.glb --labels zones_concept.png --zones zones.json --camera camera.json
        [--view MATERIALS CAMERA CROP_JSON] --out zones_mesh.json
"""
import argparse
import json
from collections import deque

import numpy as np
from PIL import Image
from scipy.sparse import coo_matrix

from glb import Glb
from rast import rasterize


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mesh", required=True)
    ap.add_argument("--labels", required=True)
    ap.add_argument("--zones", required=True)
    ap.add_argument("--camera", required=True)
    ap.add_argument("--view", nargs=3, action="append", default=[], metavar=("MATERIALS", "CAMERA", "CROP"),
                    help="a grafted part seen through its own camera onto a crop of the concept (crop json: x0 y0 size out)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--min-facing", type=float, default=0.15)
    ap.add_argument("--mask", help="the figure mask: with it, faces the concept cannot see take the wrapping zone at their pixel")
    a = ap.parse_args()
    m = Glb(a.mesh).mesh()
    P, F = m["POSITION"].astype(np.float64), m["faces"]
    mat = m["material"]
    labels = np.asarray(Image.open(a.labels))
    zones = json.load(open(a.zones))["zones"]
    # A zone marked "fill" (the garment that is most of the figure: a long coat, a robe) takes every figure pixel no
    # mask claimed.
    fill = next((zi for zi, z in enumerate(zones, start=1) if z.get("fill")), 0)
    if fill and a.mask:
        figure0 = np.asarray(Image.open(a.mask).convert("L")) > 127
        labels = np.where((labels == 0) & figure0, fill, labels)
    cen = P[F].mean(1)
    n = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12

    face_label = np.zeros(len(F), np.int32)
    # For the faces the concept cannot see: the label a garment would have behind what is seen - the label image with
    # the zones that do not wrap round the body (a satchel, a strap, a buckle: "wraps": false) taken out and every
    # figure pixel filled with its nearest wrapping zone. The back of a dress is dress where the front is; the back of
    # the body behind a satchel is not satchel.
    wrap_img = None
    if a.mask:
        import cv2
        figure = np.asarray(Image.open(a.mask).convert("L")) > 127
        wl = labels.copy()
        for zi, z in enumerate(zones, start=1):
            if not z.get("wraps", True):
                wl[wl == zi] = 0
        empty = (wl == 0).astype(np.uint8)
        if empty.any() and (wl > 0).any():
            _, idx = cv2.distanceTransformWithLabels(empty, cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL)
            ys, xs = np.nonzero(wl > 0)
            lut = np.zeros(idx.max() + 1, np.int32)
            lut[idx[wl > 0]] = wl[ys, xs]
            wl = np.where(wl > 0, wl, lut[idx])
        wrap_img = np.where(figure, wl, 0)
    behind = np.zeros(len(F), np.int32)
    views = [({int(x) for x in mats.split(",")}, cam, crop) for mats, cam, crop in a.view]
    claimed = set().union(*[v[0] for v in views]) if views else set()
    views.append((set(np.unique(mat).tolist()) - claimed, a.camera, None))
    for mats, cam_path, crop_path in views:
        sel = np.isin(mat, list(mats))
        cam = json.load(open(cam_path))
        R, t, f, c = np.array(cam["R"]), np.array(cam["t"]), cam["f"], np.array(cam["c"])
        W, H = cam["width"], cam["height"]
        q = P @ R.T + t
        xy = f * q[:, :2] / q[:, 2:3] + c
        tri, _, _ = rasterize(xy, q[:, 2].astype(np.float32), F, W, H)
        seen = np.zeros(len(F), bool)
        seen[np.unique(tri[tri >= 0])] = True
        view = (-R.T @ t) - cen
        view /= np.linalg.norm(view, axis=1, keepdims=True)
        ok = sel & seen & ((n * view).sum(1) > a.min_facing)
        qc = cen[sel] @ R.T + t
        px = f * qc[:, :2] / qc[:, 2:3] + c
        if crop_path:
            cr = json.load(open(crop_path))
            px = px * cr["size"] / cr["out"] + np.array([cr["x0"], cr["y0"]])
        ix = np.clip(np.floor(px).astype(int), 0, [labels.shape[1] - 1, labels.shape[0] - 1])
        idx_sel = np.nonzero(sel)[0]
        seen_sel = ok[sel]
        face_label[idx_sel[seen_sel]] = labels[ix[seen_sel, 1], ix[seen_sel, 0]]
        if wrap_img is not None:
            behind[idx_sel] = wrap_img[ix[:, 1], ix[:, 0]]
    direct = int((face_label > 0).sum())
    unseen = face_label == 0
    face_label[unseen] = behind[unseen]
    through = int((unseen & (face_label > 0)).sum())

    # Spread over the welded surface.
    _, inv = np.unique(P, axis=0, return_inverse=True)
    Fw = inv.ravel()[F]
    edges = np.sort(np.concatenate([Fw[:, [0, 1]], Fw[:, [1, 2]], Fw[:, [2, 0]]]), 1)
    fid = np.tile(np.arange(len(F)), 3)
    key = edges[:, 0] * (Fw.max() + 1) + edges[:, 1]
    order = np.argsort(key)
    key, fid = key[order], fid[order]
    same = np.nonzero(key[1:] == key[:-1])[0]
    A = coo_matrix((np.ones(len(same)), (fid[same], fid[same + 1])), shape=(len(F), len(F))).tocsr()
    A = (A + A.T).tocsr()
    queue = deque(np.nonzero(face_label > 0)[0].tolist())
    while queue:
        x = queue.popleft()
        for y in A.indices[A.indptr[x]:A.indptr[x + 1]]:
            if face_label[y] == 0:
                face_label[y] = face_label[x]
                queue.append(y)
    counts = {zones[i - 1]["name"]: int((face_label == i).sum()) for i in range(1, len(zones) + 1)}
    out = {"zones": zones, "face_zone": face_label.tolist(), "centroids": np.round(cen, 6).tolist(),
           "directly_seen": direct, "labelled_through": through, "unlabelled": int((face_label == 0).sum()), "faces_by_zone": counts}
    json.dump(out, open(a.out, "w"))
    print("ZONES_TO_MESH " + json.dumps({k: v for k, v in out.items() if k not in ("face_zone", "centroids", "zones")}))


if __name__ == "__main__":
    main()
