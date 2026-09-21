"""Quality-check concept images before they are sent to the 3D generator.

The 3D step is faithful but not clever: Pixal3D reconstructs whatever is in the
image, so a flawed concept becomes flawed geometry. Reviewing hundreds by hand does
not scale, and the failures that actually break reconstruction are structural ones.

Segmentation note: the generated backdrops are a **vignette**, not a flat colour -
corners land near RGB 130 and edges near 179. A global background threshold therefore
fails on every image; subject pixels are instead found by local contrast against a
heavily blurred estimate of the backdrop.

What this catches:
  - subject cropped by the frame (the 3D step truncates or invents geometry there)
  - more than one object, or stray marks, which merge into one mesh
  - subject too small or filling the frame with no margin
  - subject badly off centre

What this deliberately does NOT catch:
  - a structurally clean image of the wrong thing. tool_iron_shovel.png is a single
    centred uncropped object, and it is still not a shovel. Only a semantic check can
    see that, and no usable local vision model is available.

Usage:
    python _qc_concepts.py                      # audit all concepts, write report
    python _qc_concepts.py --explain tool_iron_shovel
    python _qc_concepts.py --only tool_ weapon_
"""
import argparse
import glob
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

ASSETS = r"W:\UNNAMED\assets"

# Loose on purpose: catch breakage, not style.
MIN_FILL = 0.02
MAX_FILL = 0.85
MAX_BLOBS = 1
EDGE_MARGIN = 2
MAX_OFFCENTRE = 0.20
CONTRAST_DELTA = 18


def analyse(path):
    image = Image.open(path).convert("RGB")
    data = np.asarray(image).astype(np.float32)
    height, width = data.shape[:2]
    grey = data.mean(axis=2)

    backdrop = ndimage.uniform_filter(grey, size=161)
    subject = (grey - backdrop) < -CONTRAST_DELTA
    subject = ndimage.binary_closing(subject, np.ones((3, 3)))
    subject = ndimage.binary_opening(subject, np.ones((2, 2)))
    subject = ndimage.binary_fill_holes(subject)

    labels, count = ndimage.label(subject)
    if count == 0:
        return {"path": path, "fatal": "no subject found", "blobs": 0,
                "edge": True, "fill": 0.0, "offcentre": 1.0}

    sizes = np.array(ndimage.sum(subject, labels, range(1, count + 1)))
    main = labels == (int(np.argmax(sizes)) + 1)
    blobs = int((sizes > 0.002 * subject.size).sum())

    edge = bool(main[:EDGE_MARGIN, :].any() or main[-EDGE_MARGIN:, :].any()
                or main[:, :EDGE_MARGIN].any() or main[:, -EDGE_MARGIN:].any())

    rows, cols = np.nonzero(main)
    fill = float(main.sum()) / (height * width)
    offcentre = float(max(abs(cols.mean() / width - 0.5), abs(rows.mean() / height - 0.5)))

    return {
        "path": path,
        "blobs": blobs,
        "edge": edge,
        "fill": round(fill, 4),
        "offcentre": round(offcentre, 4),
        "aspect": round((cols.max() - cols.min() + 1) / max(rows.max() - rows.min() + 1, 1), 2),
    }


def verdict(result):
    if result.get("fatal"):
        return False, [result["fatal"]]
    reasons = []
    if result["blobs"] > MAX_BLOBS:
        reasons.append(f"{result['blobs']} separate blobs (stray marks or extra objects)")
    if result["edge"]:
        reasons.append("subject touches the frame (cropped)")
    if result["fill"] < MIN_FILL:
        reasons.append(f"subject fills only {result['fill']:.1%}")
    if result["fill"] > MAX_FILL:
        reasons.append(f"subject fills {result['fill']:.1%}, no margin")
    if result["offcentre"] > MAX_OFFCENTRE:
        reasons.append(f"off centre by {result['offcentre']:.2f}")
    return not reasons, reasons


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets", default=ASSETS)
    parser.add_argument("--only", nargs="*", default=None)
    parser.add_argument("--explain", default=None)
    parser.add_argument("--json-out", default=None)
    args = parser.parse_args()

    concepts_dir = os.path.join(args.assets, "concepts")
    paths = sorted(glob.glob(os.path.join(concepts_dir, "*.png")))
    if args.only:
        paths = [p for p in paths if os.path.basename(p).startswith(tuple(args.only))]

    if args.explain:
        target = os.path.join(concepts_dir, f"{args.explain}.png")
        result = analyse(target)
        ok, reasons = verdict(result)
        print(os.path.basename(target))
        for key in ("blobs", "edge", "fill", "offcentre", "aspect"):
            print(f"  {key:<10} {result.get(key)}")
        print(f"  verdict    {'PASS' if ok else 'REJECT'}" + (f"  {reasons}" if reasons else ""))
        return 0

    results = []
    rejected = []
    for path in paths:
        result = analyse(path)
        ok, reasons = verdict(result)
        result["ok"] = ok
        result["reasons"] = reasons
        results.append(result)
        if not ok:
            rejected.append(result)

    passed = len(results) - len(rejected)
    print(f"checked {len(results)} concept(s): {passed} pass, {len(rejected)} reject")
    if rejected:
        print()
        by_reason = {}
        for result in rejected:
            for reason in result["reasons"]:
                key = reason.split(" (")[0][:40]
                by_reason.setdefault(key, []).append(os.path.basename(result["path"])[:-4])
        for reason, names in sorted(by_reason.items(), key=lambda kv: -len(kv[1])):
            print(f"  {len(names):>3}  {reason}")
            for name in names[:6]:
                print(f"         {name}")
            if len(names) > 6:
                print(f"         ...and {len(names) - 6} more")

    out = args.json_out or os.path.join(args.assets, "concept_qc.json")
    with open(out, "w", encoding="utf-8") as handle:
        json.dump({"checked": len(results), "passed": passed,
                   "rejected": [r["path"] for r in rejected],
                   "results": results}, handle, indent=2)
    print(f"\nreport -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
