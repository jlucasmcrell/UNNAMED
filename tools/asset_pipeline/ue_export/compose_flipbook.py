"""Build a looping flipbook atlas from bake_vdb.py's linear-HDR frames (fire on black, drawn additively in Godot).

  python compose_flipbook.py <bake dir> <out.png> --grid 8 8 --cell-w 448 [--overlap 16] [--start 0] [--white 0.999]

Frames: --start .. --start + cols*rows + overlap. The last `overlap` frames of the book are cross-faded into the frames
just before the book's first one, so the book loops without a jump. Linear radiance is scaled so the --white quantile
of the brightest channel maps to 1.0, then sRGB-encoded; alpha = that brightness through a soft curve (the game draws
it with blend add, where alpha weights rgb). Cells are the union of every frame's lit area, the fire's base at the
cell bottom, centred left to right; the cell height follows the lit area's aspect.
"""
import argparse
import glob
import json
import os
import struct
import zlib

import cv2
import numpy as np

SHOULDER = 1.6


def read_exr(path):
    """Unreal's ExportRenderTarget EXR (ZIP compression, half channels A B G R, scanlines) -> float32 RGB.
    This OpenCV build has no OpenEXR codec."""
    b = open(path, "rb").read()
    i, attrs = 8, {}
    while b[i] != 0:
        name_end = b.index(b"\0", i)
        type_end = b.index(b"\0", name_end + 1)
        size = struct.unpack_from("<i", b, type_end + 1)[0]
        attrs[b[i:name_end].decode()] = b[type_end + 5:type_end + 5 + size]
        i = type_end + 5 + size
    assert attrs["compression"][0] == 3, "only ZIP-compressed EXR"
    x0, y0, x1, y1 = struct.unpack("<4i", attrs["dataWindow"])
    w, h = x1 - x0 + 1, y1 - y0 + 1
    names, c = [], attrs["channels"]
    while c[0] != 0:
        end = c.index(b"\0")
        names.append(c[:end].decode())
        assert struct.unpack_from("<i", c, end + 1)[0] == 1, "only half channels"
        c = c[end + 17:]
    offsets = struct.unpack_from(f"<{(h + 15) // 16}Q", b, i + 1)
    out = np.zeros((h, len(names), w), np.float16)
    for off in offsets:
        y, size = struct.unpack_from("<ii", b, off)
        t = np.frombuffer(zlib.decompress(b[off + 8:off + 8 + size]), np.uint8)
        # EXR's predictor (each byte stored as the difference to the previous + 128), then its two interleaved halves.
        t = (np.cumsum(t, dtype=np.int64) - 128 * np.arange(len(t))).astype(np.uint8)
        raw = np.empty_like(t)
        half = (len(t) + 1) // 2
        raw[0::2], raw[1::2] = t[:half], t[half:]
        rows = len(raw) // (2 * len(names) * w)
        out[y - y0:y - y0 + rows] = raw.view(np.float16).reshape(rows, len(names), w)
    return np.stack([out[:, names.index(ch)] for ch in "RGB"], axis=2).astype(np.float32)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("bake")
    p.add_argument("out")
    p.add_argument("--grid", type=int, nargs=2, default=(8, 8))
    p.add_argument("--cell-w", type=int, default=448)
    p.add_argument("--overlap", type=int, default=16)
    p.add_argument("--start", type=int, default=0)
    p.add_argument("--white", type=float, default=0.999)
    p.add_argument("--threshold", type=float, default=0.02, help="fraction of white below which a pixel is unlit (crop)")
    a = p.parse_args()

    cols, rows = a.grid
    n = cols * rows
    files = sorted(glob.glob(os.path.join(a.bake, "f*.exr")))[a.start:a.start + n + a.overlap]
    if len(files) < n + a.overlap:
        raise SystemExit(f"need {n + a.overlap} frames from {a.start}, have {len(files)}")
    # Two passes over the (2048 px, mostly black) frames: the brightest channel of each, for the white point and the
    # lit box; then each frame again, cropped.
    peak = np.stack([read_exr(f).max(axis=2).astype(np.float16) for f in files])
    white = float(np.quantile(peak[peak > 0].astype(np.float32), a.white))
    lit = (peak > white * a.threshold).any(axis=0)
    del peak
    # The fire is the largest lit body; the volumes also leave thin bright streaks along their top bound, apart from
    # it - those are cut away (mask) along with anything else not joined to the body.
    n_lab, labels, stats, _ = cv2.connectedComponentsWithStats(lit.astype(np.uint8), connectivity=8)
    body = labels == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    ys, xs = np.nonzero(body)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    # A margin on every side (a little under the base too), so mip levels do not bleed one cell into the next.
    pad = int(0.03 * (x1 - x0))
    x0, x1, y0, y1 = max(0, x0 - pad), min(lit.shape[1], x1 + pad), max(0, y0 - pad), min(lit.shape[0], y1 + pad // 2)
    mask = cv2.GaussianBlur(cv2.dilate(body.astype(np.float32), np.ones((2 * pad + 1, 2 * pad + 1), np.uint8)), (0, 0), pad / 2)
    mask = np.clip(mask[y0:y1, x0:x1], 0, 1)[:, :, None]
    frames = [read_exr(f)[y0:y1, x0:x1] * mask for f in files]
    cw = a.cell_w
    ch = int(round(cw * (y1 - y0) / (x1 - x0) / 4)) * 4

    # Loop: book frame k plays source k + overlap; the last `overlap` book frames fade into the source frames before it.
    book = []
    for k in range(n):
        f = frames[k + a.overlap]
        if k >= n - a.overlap:
            w = (k - (n - a.overlap) + 1) / (a.overlap + 1)
            f = (1 - w) * f + w * frames[k + a.overlap - n]
        book.append(f)

    atlas = np.zeros((rows * ch, cols * cw, 4), np.uint8)
    for k, f in enumerate(book):
        # A soft shoulder instead of a hard clip: the hottest light keeps its shape and runs from orange towards yellow
        # (the red channel saturates first), where a clip would flatten it into one orange plateau.
        c = (1 - np.exp(-SHOULDER * f / white)) / (1 - np.exp(-SHOULDER))
        c = cv2.resize(np.clip(c, 0, 1), (cw, ch), interpolation=cv2.INTER_AREA)
        srgb = np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055)
        alpha = np.clip(srgb.max(axis=2) * 1.6, 0, 1)
        r, col = divmod(k, cols)
        atlas[r * ch:(r + 1) * ch, col * cw:(col + 1) * cw, :3] = np.round(srgb * 255)
        atlas[r * ch:(r + 1) * ch, col * cw:(col + 1) * cw, 3] = np.round(alpha * 255)
    cv2.imwrite(a.out, cv2.cvtColor(atlas, cv2.COLOR_RGBA2BGRA))
    bake = json.load(open(os.path.join(a.bake, "_bake.json")))
    cm_per_px = bake["ortho_width_cm"] / bake["res"]
    info = {"atlas": os.path.basename(a.out), "bp": bake["bp"], "yaw": bake.get("yaw", 0), "atlas_px": [cols * cw, rows * ch], "cell_px": [cw, ch], "grid": [cols, rows],
            "frames": n, "fps": bake["frame_rate"] / bake.get("step", 1), "loop_crossfade_frames": a.overlap,
            "source_frames": [os.path.basename(files[0]), os.path.basename(files[-1])],
            "crop_px": [int(x0), int(y0), int(x1), int(y1)], "cell_m": [round((x1 - x0) * cm_per_px / 100, 3),
                                                                         round((y1 - y0) * cm_per_px / 100, 3)],
            "white_linear": white, "white_quantile": a.white}
    json.dump(info, open(os.path.splitext(a.out)[0] + "_compose.json", "w"), indent=1)
    print(json.dumps(info))


if __name__ == "__main__":
    main()
