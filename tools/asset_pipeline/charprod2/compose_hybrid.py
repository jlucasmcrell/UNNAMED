"""The hybrid player's final textures from hybrid_bake.py's bakes: each garment keeps the source's texture wherever the bake landed on
the same kind of surface (the per-material ID), and its dyed MPFB texture (garment_textures.py), re-toned to the source's, where it
did not; the body is the toned skin with the projected face (composite_skin.py's body_albedo.png), the source's arms and shoulders
laid over it, its head and neck (behind the face with --face tps), and its scalp (its colour pulled toward the hair's).

    python compose_hybrid.py --work <dir>      (writes <dir>/tex_hybrid/)
"""
import argparse
import os

import cv2
import numpy as np

ID = {"skin": (1, 0, 0), "cloth_heavy": (0, 0, 1), "cloth": (0, 1, 0), "leather": (1, 1, 0), "hair": (1, 0, 1)}
KEEP = {"vest": ("cloth",), "trousers": ("cloth_heavy", "leather"), "boots": ("leather",)}


def read(path, size=None, flags=cv2.IMREAD_COLOR):
    img = cv2.imread(path, flags)
    if img is None:
        raise SystemExit(f"missing {path}")
    if size and img.shape[0] != size:
        img = cv2.resize(img, (size, size), interpolation=cv2.INTER_CUBIC)
    return img


def id_mask(id_bgr, kinds):
    rgb = id_bgr[..., ::-1].astype(np.float32) / 255
    m = np.zeros(rgb.shape[:2], np.float32)
    for k in kinds:
        m = np.maximum(m, (np.abs(rgb - np.array(ID[k], np.float32)).max(-1) < 0.25).astype(np.float32))
    return m


def feather(mask, erode=2, blur=3):
    m = cv2.erode(mask, np.ones((3, 3), np.uint8), iterations=erode) if erode else mask
    return cv2.GaussianBlur(m, (0, 0), blur)[..., None]


def tone_to(src_bgr, ref_bgr, src_mask, ref_mask):
    """src re-toned (Lab mean and spread) to ref's statistics, each measured where its mask is set."""
    ls = cv2.cvtColor(src_bgr, cv2.COLOR_BGR2LAB).astype(np.float32)
    lr = cv2.cvtColor(ref_bgr, cv2.COLOR_BGR2LAB).astype(np.float32)
    s, r = ls[src_mask > 0.5], lr[ref_mask > 0.5]
    if len(s) < 200 or len(r) < 200:
        return src_bgr
    out = (ls - s.mean(0)) * (r.std(0) + 1e-3) / (s.std(0) + 1e-3) + r.mean(0)
    return cv2.cvtColor(np.clip(out, 0, 255).astype(np.uint8), cv2.COLOR_LAB2BGR)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--work", required=True)
    ap.add_argument("--scalp-tint", type=float, default=0.6, help="how far the scalp's colour moves toward the hair's")
    ap.add_argument("--ao-strength", type=float, default=0.7, help="how much of the baked occlusion is multiplied into the skin")
    ap.add_argument("--face", choices=["bake", "tps", "none"], default="bake",
                    help="bake: the head bake everywhere (the head is wrapped onto the source's, so it registers); tps: the "
                         "landmark-warped face over the whole face mask, the head bake only behind it; none: the base skin's own face "
                         "(composite_skin.py --no-face), the head bake only behind it")
    a = ap.parse_args()
    bake, tex, out = (os.path.join(a.work, d) for d in ("bake", "tex", "tex_hybrid"))
    os.makedirs(out, exist_ok=True)
    report = {}
    for name, kinds in KEEP.items():
        baked = read(os.path.join(bake, f"{name}_albedo.png"))
        n = baked.shape[0]
        keep = id_mask(read(os.path.join(bake, f"{name}_id.png"), n), kinds)
        if name == "vest":
            # The source's own texture has skin painted onto its top at the neckline (the chest defect the owner saw): not kept.
            keep *= (cv2.cvtColor(baked, cv2.COLOR_BGR2LAB)[..., 1] < 136).astype(np.float32)
        if name == "trousers":
            # ...and the top's pale ribbed hem painted over the trousers' waist where the two overlapped on the source.
            keep *= (cv2.cvtColor(baked, cv2.COLOR_BGR2LAB)[..., 0] < 150).astype(np.float32)
        fallback = read(os.path.join(tex, f"{name}.png"), n)
        fallback = tone_to(fallback, baked, np.ones(keep.shape, np.float32), keep)
        alpha = feather(keep)
        cv2.imwrite(os.path.join(out, f"{name}.png"), np.clip(baked * alpha + fallback * (1 - alpha), 0, 255).astype(np.uint8))
        report[name] = round(float(keep.mean()), 3)

    for name in ("hair_albedo", "hair_normal", "hair_rough"):
        img = cv2.imread(os.path.join(bake, name + ".png"), cv2.IMREAD_UNCHANGED)
        cv2.imwrite(os.path.join(out, name + ".png"), img)

    facefit = os.path.join(a.work, "facefit")
    skin = read(os.path.join(facefit, "body_albedo.png")).astype(np.float32)
    n = skin.shape[0]
    masks = read(os.path.join(bake, "body_masks.png"), n).astype(np.float32) / 255
    arms_region, scalp_region, head_region = masks[..., 2], masks[..., 1], masks[..., 0]
    face = cv2.GaussianBlur(read(os.path.join(facefit, "face_proj_mask.png"), n, cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255, (0, 0), 6)
    # The landmark-warped face only where it faces the camera squarely (eyes, brows, nose, mouth, where registration matters); the
    # jaw, cheeks, ears and neck come from the head-registered bake, which sees them from every side.
    # tps: the landmark-warped face over the whole face mask (an unwrapped head's baked features would sit in the wrong places);
    # bake: the head is wrapped onto the source's, so its own bake registers everywhere.
    features = face if a.face in ("tps", "none") else np.zeros_like(face)

    arm_src = read(os.path.join(bake, "body_albedo.png"), n)
    arm_ok = id_mask(read(os.path.join(bake, "body_id.png"), n), ("skin",))
    # The arms proper (not the shoulder, where the two figures' armholes differ), re-toned to the skin they join.
    arm_core = np.clip((arms_region - 0.55) / 0.35, 0, 1)
    arm_src = tone_to(arm_src, skin.astype(np.uint8), arm_ok * (arm_core > 0.5), arms_region > 0.5).astype(np.float32)
    alpha = feather(arm_ok, 2, 4) * arm_core[..., None]
    skin = arm_src * alpha + skin * (1 - alpha)

    scalp_src = read(os.path.join(bake, "body_head_albedo.png"), n)
    head_id = read(os.path.join(bake, "body_head_id.png"), n)
    # Skin-toned only: the source's top's collar, zoned as skin on the source, would otherwise ring the neck.
    head_ok = id_mask(head_id, ("skin",)) * (cv2.cvtColor(scalp_src, cv2.COLOR_BGR2LAB)[..., 1] > 134).astype(np.float32)
    # An unwrapped head (tps, none) takes the source's head bake only on the scalp: anywhere on the face it lands misregistered
    # (the side-facing nose and eye sockets, where the projection's facing mask is weak, showed the source's features as blotches).
    where = head_region if a.face == "bake" else scalp_region
    alpha = feather(head_ok, 2, 4) * (where * (1 - features))[..., None]
    skin = scalp_src.astype(np.float32) * alpha + skin * (1 - alpha)

    scalp_ok = (head_id.max(-1) > 40).astype(np.float32)
    hair = read(os.path.join(bake, "hair_albedo.png"))
    hair_lab = cv2.cvtColor(hair, cv2.COLOR_BGR2LAB).astype(np.float32)[hair.max(-1) > 20]
    lab = cv2.cvtColor(scalp_src, cv2.COLOR_BGR2LAB).astype(np.float32)
    lab[..., 1:] += a.scalp_tint * (hair_lab[:, 1:].mean(0) - lab[..., 1:])
    scalp = cv2.cvtColor(np.clip(lab, 0, 255).astype(np.uint8), cv2.COLOR_LAB2BGR).astype(np.float32)
    alpha = feather(scalp_ok, 2, 4) * (scalp_region * (1 - features))[..., None]
    skin = scalp * alpha + skin * (1 - alpha)
    ao_path = os.path.join(bake, "body_ao.png")
    if os.path.exists(ao_path):
        # Occlusion multiplied in (the runtime's skin material takes albedo only): nostrils and lip corners stop reading as flat.
        ao = read(ao_path, n, cv2.IMREAD_GRAYSCALE).astype(np.float32) / 255
        skin = skin * (1 - a.ao_strength * (1 - ao))[..., None]
    cv2.imwrite(os.path.join(out, "body_albedo.png"), np.clip(skin, 0, 255).astype(np.uint8))
    # The head's baked normals where the head bake is used (on an unwrapped head they shade the source's features in the wrong
    # places: dark shards on the nose and sockets); flat elsewhere.
    hn = read(os.path.join(bake, "body_head_normal.png"), n).astype(np.float32)
    flat = np.zeros_like(hn)
    flat[...] = (255, 128, 128)   # BGR of (0.5, 0.5, 1.0)
    alpha = feather(head_ok, 2, 4) * (where * (1 - features))[..., None]   # the source's normals only where its bake registers
    nrm = (hn / 127.5 - 1) * alpha + (flat / 127.5 - 1) * (1 - alpha)
    nrm /= np.maximum(np.linalg.norm(nrm, axis=-1, keepdims=True), 1e-6)
    cv2.imwrite(os.path.join(out, "body_normal.png"), np.clip((nrm + 1) * 127.5, 0, 255).astype(np.uint8))
    report["arms_px"] = int((arms_region > 0.5).sum())
    report["scalp_px"] = int((scalp_region > 0.5).sum())
    print("COMPOSE_HYBRID", report)


if __name__ == "__main__":
    main()
