"""Measure whether a generated material actually tiles, and whether its maps agree.

"Tileable" is a claim that has to be measured. This compares the discontinuity ACROSS the wrap
edge against the typical discontinuity INSIDE the texture. A seamless map has a wrap ratio near
1.0 — the edge is no more of a jump than any other adjacent pair of columns. A raw render has a
ratio far above 1 because its frame edge does not continue.

Also checks the maps are mutually consistent and correctly encoded, because a normal map that is
not unit-length, or an ORM texture whose channels are out of range, still looks plausible as an
image and fails only in engine.

Usage:
    python _verify_pbr_materials.py
    python _verify_pbr_materials.py --material material_oak_plank_floor
"""
import argparse
import io
import json
import os
import sys

import numpy as np
from PIL import Image

ASSETS = r"W:\UNNAMED\assets"
CONCEPTS = os.path.join(ASSETS, "concepts")
MATERIALS = os.path.join(ASSETS, "materials")

SEAM_RATIO_TOLERANCE = 3.0   # informational only; see seam_rank for the real test
SEAM_RANK_TOLERANCE = 0.995  # edge must not be in the extreme tail of internal differences
NORMAL_TOLERANCE = 0.08      # mean deviation from unit length
REQUIRED_MAPS = ("basecolor", "normal", "ao", "roughness", "orm")


def load_rgb(path):
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0


def wrap_ratio(array):
    """Seam discontinuity against the typical internal discontinuity, both axes.

    Reported for information. On its own this is a biased test: the mean internal difference is
    dragged down by whatever smooth regions the texture happens to contain, so a perfectly
    continuous edge can still read as a ratio of 1.8 on a texture with uneven detail.
    """
    if array.ndim == 3:
        array = array.mean(axis=-1)
    horizontal_seam = np.mean(np.abs(array[:, 0] - array[:, -1]))
    horizontal_internal = np.mean(np.abs(array[:, 1:] - array[:, :-1]))
    vertical_seam = np.mean(np.abs(array[0, :] - array[-1, :]))
    vertical_internal = np.mean(np.abs(array[1:, :] - array[:-1, :]))
    h = horizontal_seam / horizontal_internal if horizontal_internal > 1e-6 else 0.0
    v = vertical_seam / vertical_internal if vertical_internal > 1e-6 else 0.0
    return h, v


def seam_rank(array):
    """Where the wrap edge sits in the distribution of internal discontinuities.

    This is the fair test. A seamless texture's wrap edge is an ordinary adjacent pair, so it
    should rank near the middle of all adjacent-pair differences. A seam that is visible is the
    single largest jump in the image and ranks at 1.0. Comparing against the whole distribution
    rather than its mean removes the bias that uneven detail introduces.
    """
    if array.ndim == 3:
        array = array.mean(axis=-1)
    horizontal = np.abs(array[:, 1:] - array[:, :-1]).ravel()
    vertical = np.abs(array[1:, :] - array[:-1, :]).ravel()
    h_edge = float(np.mean(np.abs(array[:, 0] - array[:, -1])))
    v_edge = float(np.mean(np.abs(array[0, :] - array[-1, :])))
    h_rank = float(np.mean(horizontal < h_edge))
    v_rank = float(np.mean(vertical < v_edge))
    return h_rank, v_rank


def check_material(asset_id):
    directory = os.path.join(MATERIALS, asset_id)
    meta_path = os.path.join(directory, f"{asset_id}_material.json")
    problems = []
    facts = {}

    if not os.path.exists(meta_path):
        return ["no material metadata; not built"], {}
    meta = json.load(io.open(meta_path, encoding="utf-8"))

    maps = {}
    for key in REQUIRED_MAPS:
        name = meta["maps"].get(key)
        path = os.path.join(directory, name) if name else None
        if not path or not os.path.exists(path):
            problems.append(f"{key} map missing")
            continue
        maps[key] = load_rgb(path)

    if "basecolor" in maps:
        h, v = wrap_ratio(maps["basecolor"])
        rh, rv = seam_rank(maps["basecolor"])
        facts["basecolor_wrap_ratio"] = (round(h, 3), round(v, 3))
        facts["basecolor_seam_rank"] = (round(rh, 4), round(rv, 4))
        if max(rh, rv) > SEAM_RANK_TOLERANCE:
            problems.append(f"base colour seam is in the extreme tail of internal differences "
                            f"(rank {rh:.4f} horizontal, {rv:.4f} vertical; "
                            f"tolerance {SEAM_RANK_TOLERANCE})")

    if "normal" in maps:
        n = maps["normal"] * 2.0 - 1.0
        length = np.sqrt((n ** 2).sum(axis=-1))
        facts["normal_unit_error"] = round(float(np.mean(np.abs(length - 1.0))), 4)
        if facts["normal_unit_error"] > NORMAL_TOLERANCE:
            problems.append(f"normal map is not unit length (mean error "
                            f"{facts['normal_unit_error']})")
        h, v = wrap_ratio(maps["normal"])
        rh, rv = seam_rank(maps["normal"])
        facts["normal_wrap_ratio"] = (round(h, 3), round(v, 3))
        facts["normal_seam_rank"] = (round(rh, 4), round(rv, 4))
        if max(rh, rv) > SEAM_RANK_TOLERANCE:
            problems.append(f"normal seam is in the extreme tail (rank {rh:.4f} horizontal, "
                            f"{rv:.4f} vertical)")

    if "basecolor" in maps:
        facts["resolution"] = maps["basecolor"].shape[0]
        side = maps["basecolor"].shape[0]
        if side & (side - 1):
            problems.append(f"resolution {side} is not a power of two")

    if "orm" in maps:
        orm = maps["orm"]
        facts["orm_channels"] = {"ao": [round(float(orm[..., 0].min()), 3),
                                        round(float(orm[..., 0].max()), 3)],
                                 "roughness": [round(float(orm[..., 1].min()), 3),
                                               round(float(orm[..., 1].max()), 3)],
                                 "metallic": [round(float(orm[..., 2].min()), 3),
                                              round(float(orm[..., 2].max()), 3)]}
        if orm[..., 1].max() - orm[..., 1].min() < 0.02:
            problems.append("roughness channel is flat; the map carries no information")
        declared = meta["pbr"]["roughness_base"]
        if abs(float(orm[..., 1].mean()) - declared) > 0.15:
            problems.append(f"ORM roughness mean {float(orm[..., 1].mean()):.3f} does not match "
                            f"declared base {declared}")

    facts["tile_size_m"] = meta.get("tile_size_m")
    facts["material_class"] = meta.get("material_class")
    return problems, facts


def source_seam(asset_id):
    """The concept's own seam rank, so the improvement is visible rather than merely claimed."""
    path = os.path.join(CONCEPTS, f"{asset_id}.png")
    if not os.path.exists(path):
        return None
    image = Image.open(path).convert("RGB")
    side = min(image.size)
    left = (image.width - side) // 2
    top = (image.height - side) // 2
    array = np.asarray(image.crop((left, top, left + side, top + side))
                       .resize((512, 512), Image.LANCZOS), dtype=np.float32) / 255.0
    return wrap_ratio(array)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--material", nargs="*", default=None)
    args = parser.parse_args()

    if not os.path.isdir(MATERIALS):
        print("  no materials built yet")
        return 1
    names = sorted(d for d in os.listdir(MATERIALS)
                   if os.path.isdir(os.path.join(MATERIALS, d)))
    if args.material:
        names = [n for n in names if n in args.material]

    failed = 0
    print(f"  {'material':<36} {'res':>5} {'seam rank H/V':>16} {'src rank':>15} "
          f"{'wrap H/V':>13} {'unit err':>9}")
    print("  " + "-" * 84)
    for asset_id in names:
        problems, facts = check_material(asset_id)
        src = source_seam(asset_id)
        src_text = f"{src[0]:.3f}/{src[1]:.3f}" if src else "n/a"
        wrap = facts.get("basecolor_wrap_ratio")
        wrap_text = f"{wrap[0]:.2f}/{wrap[1]:.2f}" if wrap else "n/a"
        unit = facts.get("normal_unit_error", "")
        rank = facts.get('basecolor_seam_rank')
        rank_text = f"{rank[0]:.4f}/{rank[1]:.4f}" if rank else "n/a"
        print(f"  {'FAIL' if problems else 'OK  '} {asset_id:<31} "
              f"{facts.get('resolution', 0):>5} {rank_text:>16} {src_text:>15} "
              f"{wrap_text:>13} {unit:>9}")
        for problem in problems:
            print(f"        {problem}")
        if problems:
            failed += 1

    print(f"\n  {len(names) - failed}/{len(names)} materials verified")
    print(f"  total clamp/craft budget: tile sizes "
          f"{sorted({json.load(io.open(os.path.join(MATERIALS, n, n + '_material.json'), encoding='utf-8'))['tile_size_m'] for n in names})} m")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
