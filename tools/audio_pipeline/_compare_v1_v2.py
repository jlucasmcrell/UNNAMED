"""V1 versus V2 audio comparison sheet, so the regeneration can be looked at rather than only measured.

The owner listens to decide whether a sound is convincing, and nothing here substitutes for that. This
answers the narrower question measurement can answer: does V2 differ from V1 in the traits that were
actual measurable defects - a sub-bass thud where a mid-range tick was wanted, or a broadband noise
wash where a vocalisation was wanted. A number cannot show a formant band appearing; a picture can.

Reuses `_audio_review.py`'s own spectrogram, waveform and colour ramp so V2 sheets are drawn the same
way as the V1 review sheets already in `assets/review/audio/`, and so this adds no dependency the
project does not already have.

Usage:
    python _compare_v1_v2.py --ids sfx.creature.boar.attack.01 ... --out sheet.png
    python _compare_v1_v2.py --family creature --out creature.png
"""
import argparse
import importlib.util
import io
import json
import os

import numpy as np
import soundfile as sf
from PIL import Image, ImageDraw

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS = r"W:\UNNAMED\assets"
V1 = os.path.join(ASSETS, "audio", "v1_stable_audio_open", "delivered")
V2 = os.path.join(ASSETS, "audio", "v2_candidates")
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")

LABELS = ("a", "b", "c")


def load_review_tool():
    path = os.path.join(TOOL_DIR, "_audio_review.py")
    spec = importlib.util.spec_from_file_location("_audio_review", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read(path):
    data, rate = sf.read(path, always_2d=True, dtype="float64")
    return (data.mean(axis=1) if data.shape[1] > 1 else data[:, 0]), rate


def find_v1(audio_id):
    for suffix in (".wav", ".flac"):
        path = os.path.join(V1, audio_id + suffix)
        if os.path.exists(path):
            return path
    return None


def cell(review, path, label):
    """One labelled tile: waveform strip beside a spectrogram, matching the V1 review style.

    `waveform` and `spectrogram` both return grayscale arrays and `colourise` returns an RGB array, so
    each has to be wrapped before PIL will place it.
    """
    mono, rate = read(path)
    wave = Image.fromarray(review.colourise(review.waveform(mono, review.WAVE_W, review.ROW_H))
                           .astype(np.uint8))
    spec_gray, _ = review.spectrogram(mono, rate)
    spec = Image.fromarray(review.colourise(spec_gray).astype(np.uint8)).resize(
        (review.SPEC_W, review.ROW_H), Image.BILINEAR)
    tile = Image.new("RGB", (review.WAVE_W + review.SPEC_W, review.ROW_H + review.LABEL_H),
                     (12, 12, 16))
    tile.paste(wave, (0, review.LABEL_H))
    tile.paste(spec, (review.WAVE_W, review.LABEL_H))
    draw = ImageDraw.Draw(tile)
    draw.text((6, 6), label, fill=(235, 235, 240))
    return tile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ids", nargs="*", default=None)
    parser.add_argument("--family", default=None,
                        help="take every id whose name contains this string")
    parser.add_argument("--out", required=True)
    parser.add_argument("--limit", type=int, default=8)
    parser.add_argument("--columns", type=int, default=4)
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)

    ids = args.ids
    if not ids:
        ids = [e["audio_id"] for e in spec["sounds"]]
        if args.family:
            ids = [i for i in ids if args.family in i]
    ids = ids[:args.limit]

    review = load_review_tool()
    rows = []
    for audio_id in ids:
        v1 = find_v1(audio_id)
        candidates = [os.path.join(V2, audio_id, f"candidate_{c}.flac") for c in LABELS]
        candidates = [c for c in candidates if os.path.exists(c)]
        if v1 and candidates:
            rows.append((audio_id, v1, candidates))

    if not rows:
        print("  nothing to compare: no V1 file, or no V2 candidates yet")
        return 1

    columns = 1 + max(len(r[2]) for r in rows)
    tile_w = review.WAVE_W + review.SPEC_W
    tile_h = review.ROW_H + review.LABEL_H
    sheet = Image.new("RGB", (tile_w * columns, tile_h * len(rows)), (12, 12, 16))
    for row_index, (audio_id, v1, candidates) in enumerate(rows):
        sheet.paste(cell(review, v1, f"V1  {audio_id}"), (0, row_index * tile_h))
        for column, candidate in enumerate(candidates, start=1):
            sheet.paste(cell(review, candidate, f"V2-{chr(64+column)}"),
                        (column * tile_w, row_index * tile_h))
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    sheet.save(args.out)
    print(f"  wrote {args.out}  ({len(rows)} ids x {columns} columns)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
