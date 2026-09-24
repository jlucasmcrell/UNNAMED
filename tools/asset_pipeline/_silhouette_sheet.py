"""Build a solid-black silhouette sheet from existing concept images.

RACES.md makes the solid-black silhouette test the acceptance criterion for race design:
if two races are hard to tell apart in neutral poses at gameplay distance, the
proportions or posture need another pass.

Asking the image model to draw silhouettes does not test anything - it produced seven
near-identical human outlines, because a silhouette prompt carries no anatomy. Deriving
the silhouettes from the actual concepts instead measures what the concepts really look
like once colour, texture and detail are removed, which is the whole point of the test.

Usage:
    python _silhouette_sheet.py --out review\\silhouettes.jpg ^
        race_kal_representative race_ondrek_representative ...
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageOps

CONCEPTS = r"W:\UNNAMED\assets\concepts"


def background_level(gray):
    """Estimate the backdrop brightness from the image's own histogram.

    The generated backdrops are a vignette, so corner sampling disagrees with the
    centre (one image measured 139 at a corner and 171 in the middle). The backdrop is
    always the bright end of the distribution because the subject occupies the minority
    of the frame, so the 85th percentile is a stable estimate across all of them.
    """
    histogram = gray.histogram()
    total = sum(histogram)
    accumulated = 0
    for value, count in enumerate(histogram):
        accumulated += count
        if accumulated >= total * 0.85:
            return value
    return 255


def silhouette(path, height=420, margin=30):
    """Return a black-on-white silhouette of the subject in a concept image.

    Concepts sit on a plain light backdrop, so anything meaningfully darker than the
    estimated backdrop is the subject. A blur before thresholding removes speckle, and a
    median filter keeps the outline readable rather than noisy.
    """
    gray = ImageOps.autocontrast(Image.open(path).convert("L"))
    level = background_level(gray) - margin
    gray = gray.filter(ImageFilter.GaussianBlur(2))
    mask = gray.point(lambda p: 0 if p < level else 255)
    mask = mask.filter(ImageFilter.MedianFilter(5))

    # Crop to the subject so every figure is scaled by its own real extent.
    subject = ImageOps.invert(mask)
    bbox = subject.getbbox()
    if bbox:
        mask = mask.crop(bbox)

    scale = height / max(mask.height, 1)
    panel = mask.resize((max(int(mask.width * scale), 1), height), Image.LANCZOS)

    # Composite onto white so a stray dark patch reads as background, not as figure.
    canvas = Image.new("L", panel.size, 255)
    canvas.paste(panel, (0, 0))
    return canvas


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("assets", nargs="+")
    parser.add_argument("--out", required=True)
    parser.add_argument("--height", type=int, default=420)
    args = parser.parse_args()

    panels = []
    for asset in args.assets:
        path = os.path.join(CONCEPTS, f"{asset}.png")
        if not os.path.exists(path):
            print(f"  missing: {asset}")
            continue
        panels.append((asset, silhouette(path, args.height)))
    if not panels:
        print("nothing to draw")
        return 1

    gap = 40
    label = 46
    width = sum(p.width for _n, p in panels) + gap * (len(panels) + 1)
    height = args.height + label + gap
    sheet = Image.new("L", (width, height), 245)
    draw = ImageDraw.Draw(sheet)

    x = gap
    for name, panel in panels:
        sheet.paste(panel, (x, gap))
        short = name.replace("race_", "").replace("_representative", "")
        draw.text((x, gap + args.height + 8), short, fill=60)
        x += panel.width + gap

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    sheet.save(args.out, quality=92)
    print(f"wrote {args.out}  ({len(panels)} silhouettes, {sheet.size})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
