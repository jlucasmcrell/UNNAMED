"""Per-zone surface response in a character's ORM atlas: each zone's roughness (G) re-centred on the value its material
kind should have, keeping the reconstruction's local variation around it, and its metalness (B) set by kind (the
reconstruction's metal channel is noise on skin and cloth). Occlusion (R) is kept.

The image-to-3D textures carry one roughness field for everything - on the Phase-A player skin, hair and cloth all
sat around 0.6 with the grafted head's skin near 0.35 (a wet sheen) - which is part of why skin read as wax.

    python orm_zones.py --mesh layout.glb --zones zones_mesh.json --orm orm_transfer.png --out orm.png [--report r.json]
"""
import argparse
import json

import numpy as np
from PIL import Image

from glb import Glb
from rast import rasterize

# kind: (target roughness, variation kept around it, metalness)
KINDS = {
    "skin": (0.55, 0.5, 0.0),
    "hair": (0.5, 0.6, 0.0),
    "cloth": (0.9, 0.5, 0.0),
    "cloth_heavy": (0.78, 0.6, 0.0),
    "leather": (0.58, 0.7, 0.0),
    "metal": (0.35, 0.8, None),  # None: keep the map's metalness
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mesh", required=True)
    ap.add_argument("--zones", required=True)
    ap.add_argument("--orm", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--albedo", help="the albedo atlas: skin highlights the concept's studio light left in it are soft-clipped")
    ap.add_argument("--albedo-out")
    ap.add_argument("--report")
    a = ap.parse_args()
    m = Glb(a.mesh).mesh()
    F, UV = m["faces"], m["TEXCOORD_0"]
    zm = json.load(open(a.zones))
    face_zone = np.array(zm["face_zone"])
    orm = np.asarray(Image.open(a.orm).convert("RGB")).astype(np.float32) / 255.0
    S = orm.shape[0]
    tri, _, _ = rasterize(UV * S, np.zeros(len(UV), np.float32), F, S, S)
    zone_px = np.where(tri >= 0, face_zone[np.maximum(tri, 0)], 0)
    out = orm.copy()
    report = {}
    for zi, zone in enumerate(zm["zones"], start=1):
        kind = zone.get("material", zone["name"])
        if kind not in KINDS:
            continue
        target, keep, metal = KINDS[kind]
        sel = zone_px == zi
        if not sel.any():
            continue
        g = orm[..., 1][sel]
        mean = float(g.mean())
        out[..., 1][sel] = np.clip(target + keep * (g - mean), 0.05, 1.0)
        if metal is not None:
            out[..., 2][sel] = metal
        report[zone["name"]] = {"kind": kind, "roughness_before": round(mean, 3), "roughness_after": round(float(out[..., 1][sel].mean()), 3)}
    Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(a.out)
    if a.albedo:
        # Skin: luminance well above its neighbourhood (a key or rim light's highlight) is soft-clipped toward the
        # neighbourhood's colour, so the lit concept does not paint shine into the albedo.
        import cv2
        alb = np.asarray(Image.open(a.albedo).convert("RGB")).astype(np.float32) / 255.0
        skin = np.isin(zone_px, [zi for zi, z in enumerate(zm["zones"], start=1) if z.get("material", z["name"]) == "skin"])
        lum = alb @ np.array([0.2126, 0.7152, 0.0722], np.float32)
        wsum = cv2.GaussianBlur(skin.astype(np.float32), (0, 0), 24)
        local = cv2.GaussianBlur(lum * skin, (0, 0), 24) / np.maximum(wsum, 1e-4)
        local_c = cv2.GaussianBlur(alb * skin[..., None], (0, 0), 24) / np.maximum(wsum, 1e-4)[..., None]
        knee = local * 1.15
        over = np.clip(lum - knee, 0, None)
        clipped = np.where(lum > knee, knee + over * 0.25, lum)
        k = np.clip((lum - knee) / np.maximum(0.25 * local, 1e-3), 0, 1)[..., None]
        scaled = alb * (clipped / np.maximum(lum, 1e-4))[..., None]
        fixed = scaled * (1 - 0.5 * k) + local_c * (clipped / np.maximum(local, 1e-4))[..., None] * 0.5 * k
        alb = np.where(skin[..., None], np.clip(fixed, 0, 1), alb)
        Image.fromarray((alb * 255 + 0.5).astype(np.uint8)).save(a.albedo_out)
        report["skin_highlights_clipped_share"] = round(float((skin & (lum > knee)).sum() / max(skin.sum(), 1)), 4)
    if a.report:
        json.dump(report, open(a.report, "w"), indent=1)
    print("ORM_ZONES " + json.dumps(report))


if __name__ == "__main__":
    main()
