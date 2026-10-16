"""Compose render_gait_sheet.py frames into one labelled sheet: a row per clip and view, a column per frame.

    python gait_sheet.py FRAMES_DIR OUT.png [--title "..."] [--facts gait_facts.json]

--facts: {label: "text"} printed beside each clip's rows (stance, pace).
"""
import argparse
import json
import os

from PIL import Image, ImageDraw, ImageFont


def main():
    p = argparse.ArgumentParser()
    p.add_argument("frames")
    p.add_argument("out")
    p.add_argument("--title", default="")
    p.add_argument("--facts")
    a = p.parse_args()
    meta = json.load(open(os.path.join(a.frames, "frames.json"), encoding="utf-8"))
    facts = json.load(open(a.facts, encoding="utf-8")) if a.facts else {}
    n = meta["frames"]
    sample = Image.open(os.path.join(a.frames, f"{next(iter(meta['clips']))}_front_0.png"))
    w, h = sample.size
    label_w, title_h = 300, 60
    rows = [(label, view) for label in meta["clips"] for view in ("front", "side")]
    sheet = Image.new("RGB", (label_w + n * w, title_h + len(rows) * h), (245, 245, 240))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("arial.ttf", 20)
        small = ImageFont.truetype("arial.ttf", 15)
    except OSError:
        font = small = ImageFont.load_default()
    d.text((10, 15), a.title, fill=(0, 0, 0), font=font)
    for r, (label, view) in enumerate(rows):
        y = title_h + r * h
        d.text((10, y + 10), f"{label}  ({view})", fill=(0, 0, 0), font=font)
        if view == "front" and label in facts:
            for j, line in enumerate(facts[label].split("\n")):
                d.text((10, y + 45 + j * 20), line, fill=(40, 40, 40), font=small)
        if view == "front":
            times = meta["clips"][label]["times"]
            d.text((10, y + h - 30), "t = " + ", ".join(f"{t:.2f}" for t in times), fill=(90, 90, 90), font=small)
        for i in range(n):
            sheet.paste(Image.open(os.path.join(a.frames, f"{label}_{view}_{i}.png")).convert("RGB"), (label_w + i * w, y))
        d.line([(0, y), (sheet.width, y)], fill=(180, 180, 180), width=1)
    sheet.save(a.out)
    print("sheet", a.out, sheet.size)


if __name__ == "__main__":
    main()
