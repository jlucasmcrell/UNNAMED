"""Flag motion-compensated tile changes in REAL-TIME video; flags require review."""
import argparse
import csv
import json
import subprocess
from pathlib import Path

import cv2
import numpy as np


def coherent_cells(rows):
    """Second-stage cell candidates: connected tiles with the same brightness polarity."""
    frames = {}
    for row in rows:
        if abs(float(row["luma_delta"])) >= 12 and float(row["changed"]) >= .4:
            frames.setdefault(int(row["frame"]), []).append(row)
    candidates = []
    for frame, tiles in frames.items():
        for polarity in (-1, 1):
            remaining = {(int(t["tile_x"]), int(t["tile_y"])): t for t in tiles
                         if float(t["luma_delta"]) * polarity > 0}
            while remaining:
                seed = next(iter(remaining))
                stack, component = [seed], []
                while stack:
                    x, y = stack.pop()
                    tile = remaining.pop((x, y), None)
                    if tile is None:
                        continue
                    component.append(tile)
                    stack.extend([(x-1, y), (x+1, y), (x, y-1), (x, y+1)])
                xs = [int(t["tile_x"]) for t in component]
                ys = [int(t["tile_y"]) for t in component]
                width, height = max(xs)-min(xs)+1, max(ys)-min(ys)+1
                occupancy = len(component) / (width * height)
                if len(component) >= 4 and width >= 2 and height >= 2 and occupancy >= .6:
                    candidates.append(dict(frame=frame, seconds=float(component[0]["seconds"]),
                                           bbox=[min(xs), min(ys), width, height], tiles=len(component),
                                           polarity=polarity, occupancy=round(occupancy, 3)))
    return candidates


def scan(video, out, threshold=18.0, fraction=.32, stride=2):
    out.mkdir(parents=True, exist_ok=True)
    capture = cv2.VideoCapture(str(video))
    fps = capture.get(cv2.CAP_PROP_FPS)
    if not fps:
        raise ValueError("No video frame rate")
    flow = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_FAST)
    previous = None
    rows = []
    index = 0
    maximum_frame_std = 0.0
    yy, xx = np.mgrid[:360, :640].astype(np.float32)
    while True:
        ok, frame = capture.read()
        if not ok:
            break
        index += 1
        if (index - 1) % stride:
            continue
        timestamp = capture.get(cv2.CAP_PROP_POS_MSEC) / 1000
        gray = cv2.cvtColor(cv2.resize(frame, (640, 360)), cv2.COLOR_BGR2GRAY)
        maximum_frame_std = max(maximum_frame_std, float(gray.std()))
        if previous is not None:
            # Backward optical flow warps the preceding image into current space.
            backward = flow.calc(gray, previous, None)
            warped = cv2.remap(previous, xx + backward[..., 0], yy + backward[..., 1],
                               cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT)
            residual = cv2.absdiff(gray, warped).astype(np.float32)
            old_edges = cv2.Canny(warped, 60, 120)
            edges = cv2.Canny(gray, 60, 120)
            # Exclude HUD, borders, player silhouette and notices. Static UI is not foliage.
            valid = np.ones(gray.shape, np.uint8)
            valid[:115, :190] = 0
            valid[:130, 450:] = 0
            valid[280:, :] = 0
            valid[160:, 250:390] = 0
            valid[:4, :] = valid[:, :4] = valid[:, -4:] = 0
            for ty in range(32):
                for tx in range(32):
                    ys = slice(ty * 360 // 32, (ty + 1) * 360 // 32)
                    xs = slice(tx * 20, (tx + 1) * 20)
                    mask = valid[ys, xs].astype(bool)
                    if mask.mean() < .9:
                        continue
                    tile = residual[ys, xs][mask]
                    changed = float((tile > threshold).mean())
                    signed = gray[ys, xs].astype(float) - warped[ys, xs]
                    mean_delta = float(signed[mask].mean())
                    edge_delta = float(np.mean(cv2.absdiff(edges[ys, xs], old_edges[ys, xs])[mask]) / 255)
                    # Require coherent brightness change OR substantial structure replacement.
                    if changed >= fraction and (abs(mean_delta) >= 8 or edge_delta >= .16):
                        rows.append(dict(frame=index - 1, seconds=round(timestamp, 4),
                                         tile_x=tx, tile_y=ty, changed=round(changed, 4),
                                         luma_delta=round(mean_delta, 3), edge_delta=round(edge_delta, 4)))
        previous = gray
    capture.release()
    if maximum_frame_std < 2:
        (out / "INVALID_CAPTURE.json").write_text(json.dumps({"video": str(video),
            "reason": "No textured game image: blank/grey capture cannot establish zero pops.",
            "maximum_frame_std": maximum_frame_std}), encoding="utf-8")
        raise ValueError("Rejected blank/grey recording; no valid pop count")
    fields = ["frame", "seconds", "tile_x", "tile_y", "changed", "luma_delta", "edge_delta"]
    with (out / "flagged_tiles.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    # Group adjacent times into candidate events; never call them confirmed cell pops.
    cells = coherent_cells(rows)
    (out / "cell_candidates.json").write_text(json.dumps(cells, indent=2), encoding="utf-8")
    times = sorted(set(r["seconds"] for r in cells))
    events = []
    for t in times:
        if not events or t - events[-1][1] > .5:
            events.append([t, t])
        else:
            events[-1][1] = t
    report = dict(video=str(video.resolve()), fps=fps, decoded_frames=index, grid=[32, 32],
                  analysis_size=[640, 360], stride=stride, threshold=threshold, fraction=fraction,
                  candidate_tile_flags=len(rows), coherent_cell_frames=len(times), candidate_events=events,
                  cell_filter="Same-polarity luma delta >=12, changed fraction >=.4; >=4 connected tiles, bbox >=2x2, occupancy >=.6.",
                  confirmed_cell_pops=None,
                  interpretation="Candidate flags require video review; optical flow, wind, lighting and disocclusion can produce false positives.")
    (out / "detector.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument("video", type=Path)
    parser.add_argument("out", type=Path)
    parser.add_argument("--ffmpeg", type=Path)
    parser.add_argument("--stride", type=int, default=2)
    args = parser.parse_args()
    report = scan(args.video, args.out, stride=args.stride)
    if args.ffmpeg:
        # A crowded diagnostic must not silently omit events: export every candidate group.
        for i, (start, end) in enumerate(report["candidate_events"]):
            subprocess.run([str(args.ffmpeg), "-hide_banner", "-loglevel", "error", "-y",
                            "-ss", str(max(0, start - 1)), "-i", str(args.video),
                            "-t", str(end - max(0, start - 1) + 1), "-an", "-c:v", "libx264",
                            "-preset", "fast", "-crf", "19", str(args.out / f"event_{i:04}.mp4")], check=True)
    print(json.dumps(report))


if __name__ == "__main__":
    main()
