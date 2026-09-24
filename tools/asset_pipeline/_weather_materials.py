"""Grade the ten Phase-1 world materials from washed-out to weathered.

Measured before this pass, the base colours were uniformly bright and several were almost entirely
flat:

    material_limestone_ashlar    mean L 202   contrast (p5..p95)  19
    material_plaster_lath_wall   mean L 202   contrast            45
    material_oak_plank_floor     mean L 169   contrast            95
    material_loose_gravel        mean L 180   contrast           144

A dressed limestone ashlar wall with a contrast of 19 is a grey card, not stone. That is what makes
the assembled buildings read as a paper model rather than a frontier waystation: the geometry is
correct - floor, posts, walls, door, windows, beams and a two-slope roof - but every surface is flat
and pale, so nothing reads as a material.

Why the de-lighting caused it: `_make_pbr_materials.py` removes the lighting the concept was rendered
under so the base colour carries albedo rather than illumination. That is correct, and it also removes
the shadows that were carrying most of the apparent contrast, leaving a bright low-contrast average
behind. Recovery has to come from grading, not from re-adding baked lighting.

What this does, per material class:

  * scales luminance so the mean lands on a documented weathered target,
  * expands contrast around that mean so surface relief reads,
  * keeps hue by scaling the three channels by one factor rather than pushing toward grey,
  * clips at 1-2% to avoid crushing and to leave a little headroom above the brightest stone.

Every operation is per-pixel, so **tileability is preserved exactly** - the wrap test compares opposite
edges and a pointwise grade cannot change whether they match. Normals, ORM, roughness and AO are not
touched at all. No geometry, footprint, doorway or collision interface is altered.

Originals are archived before anything is written, and `--restore` puts them back.

Usage:
    python _weather_materials.py --audit
    python _weather_materials.py --apply
    python _weather_materials.py --restore
"""
import argparse
import io
import json
import os
import shutil

import numpy as np
from PIL import Image

ASSETS = r"W:\UNNAMED\assets"
MATERIALS = os.path.join(ASSETS, "materials")
ARCHIVE = os.path.join(ASSETS, "_superseded", "materials_pre_weather")

# Per class: the mean luminance a weathered surface should sit at, and how hard to stretch the
# remaining range. `fraction` clips the extreme percentiles so a few stray pixels cannot set the range.
GRADE = {
    "ground":  {"mean": 112.0, "stretch": 1.22, "fraction": 0.015},
    "timber":  {"mean": 118.0, "stretch": 1.40, "fraction": 0.010},
    "plaster": {"mean": 156.0, "stretch": 2.30, "fraction": 0.010},
    "stone":   {"mean": 122.0, "stretch": 1.45, "fraction": 0.010},
    "roof":    {"mean": 112.0, "stretch": 1.30, "fraction": 0.010},
    "metal":   {"mean": 100.0, "stretch": 1.30, "fraction": 0.015},
}

LUMA = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def luminance(array):
    return array @ LUMA


def load_material(name):
    path = os.path.join(MATERIALS, name, f"{name}_material.json")
    if not os.path.exists(path):
        return None
    with io.open(path, encoding="utf-8") as handle:
        return json.load(handle)


def grade_image(array, spec):
    """Pointwise luminance reshape that leaves hue alone.

    Order matters and the first attempt got it wrong. Stretching the clipped percentiles onto the full
    0..1 range *overrides* any brightness target set before it, because the remap fixes the output's
    range from the input's distribution rather than from a chosen mean - the audit showed every
    material coming out **brighter** than it went in, which is the opposite of the intent. So the
    contrast stretch happens first and the brightness scale is applied last, to the already-stretched
    image, where nothing after it can undo the target.
    """
    before = luminance(array)
    current = float(before.mean())
    if current <= 1e-6:
        return array, {}

    # 1. Contrast: clip the tails and remap, which is what restores surface relief.
    low = float(np.percentile(array, spec["fraction"] * 100))
    high = float(np.percentile(array, 100 - spec["fraction"] * 100))
    stretched = array if high - low <= 1e-6 else (array - low) / (high - low)

    # 2. Brightness: one multiplicative factor on all channels, so hue and saturation survive. Applied
    #    after the stretch, so the target mean is actually reached.
    stretched = np.clip(stretched, 0.0, 1.0)
    stretched_mean = float(luminance(stretched).mean())
    if stretched_mean > 1e-6:
        stretched = stretched * (spec["mean"] / 255.0) / stretched_mean

    graded = np.clip(stretched, 0.0, 1.0)
    after = luminance(graded)
    return graded, {
        "mean_before": round(float(before.mean()) * 255, 1),
        "mean_after": round(float(after.mean()) * 255, 1),
        "contrast_before": round(float(np.percentile(before, 95) - np.percentile(before, 5)) * 255, 0),
        "contrast_after": round(float(np.percentile(after, 95) - np.percentile(after, 5)) * 255, 0),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--restore", action="store_true")
    args = parser.parse_args()

    names = sorted(os.listdir(MATERIALS)) if os.path.isdir(MATERIALS) else []
    names = [n for n in names if os.path.isdir(os.path.join(MATERIALS, n))]

    if args.restore:
        restored = 0
        for name in names:
            source = os.path.join(ARCHIVE, name, f"{name}_basecolor.png")
            target = os.path.join(MATERIALS, name, f"{name}_basecolor.png")
            if os.path.exists(source):
                shutil.copy2(source, target)
                restored += 1
        print(f"  restored {restored} base colour(s) from {ARCHIVE}")
        return 0

    report = []
    for name in names:
        meta = load_material(name)
        if not meta:
            continue
        material_class = meta.get("material_class", "stone")
        spec = GRADE.get(material_class)
        if not spec:
            print(f"  no grade for class '{material_class}' ({name}); skipped")
            continue

        path = os.path.join(MATERIALS, name, f"{name}_basecolor.png")
        image = Image.open(path).convert("RGB")
        array = np.asarray(image, dtype=np.float32) / 255.0

        graded, stats = grade_image(array, spec)

        print(f"  {name:<32} class {material_class:<8} "
              f"mean {stats['mean_before']:>5} -> {stats['mean_after']:<5} "
              f"contrast {stats['contrast_before']:>3.0f} -> {stats['contrast_after']:<3.0f}")
        report.append({"material": name, "class": material_class, **stats,
                       "mean_target": spec["mean"], "stretch": spec["stretch"]})

        if args.apply:
            archived = os.path.join(ARCHIVE, name)
            os.makedirs(archived, exist_ok=True)
            if not os.path.exists(os.path.join(archived, f"{name}_basecolor.png")):
                shutil.copy2(path, os.path.join(archived, f"{name}_basecolor.png"))
            out = Image.fromarray((graded * 255.0 + 0.5).astype(np.uint8), mode="RGB")
            out.save(path)
            with io.open(os.path.join(ARCHIVE, name, "grade.json"), "w", encoding="utf-8") as handle:
                json.dump({"material": name, "grade": spec, "result": stats}, handle, indent=2)

    if args.apply:
        with io.open(os.path.join(MATERIALS, "WEATHERING.json"), "w", encoding="utf-8") as handle:
            json.dump({
                "version": 1,
                "applied": "2026-09-24",
                "why": ("The ten Phase-1 world materials graded from washed-out to weathered. "
                        "De-lighting had left them bright and flat - limestone ashlar measured a "
                        "contrast of 19, effectively a grey card - which made the assembled buildings "
                        "read as a paper model."),
                "scope": ("basecolour only, pointwise, so tileability is unchanged; normals, ORM, "
                          "roughness and AO untouched; no geometry or interface contract altered"),
                "restore": "python _weather_materials.py --restore",
                "grades": GRADE,
                "materials": report,
            }, handle, indent=2)
        print(f"\n  graded {len(report)} material(s); originals archived to {ARCHIVE}")
    else:
        print("\n  (audit only; pass --apply to write)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
