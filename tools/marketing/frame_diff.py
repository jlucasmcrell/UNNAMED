"""Check native film images AND the camera used for each rendered frame. No interpolated/duplicated delivery frames.

python tools/marketing/frame_diff.py --plan <plan.json> --frames <film-output> --output <proof.json>
Requires Pillow and numpy; runs on CPU. Moving fixed-world orbit/overhead shots are the regression proof.
"""
import argparse
import csv
import json
from pathlib import Path

import numpy as np
from PIL import Image


def check(folder, shot, fps=60):
    folder = Path(folder)
    receipt = json.loads((folder / 'shot_done.json').read_text(encoding='utf-8-sig'))
    with (folder / 'camera_frames.csv').open(newline='', encoding='utf-8-sig') as stream:
        rows = list(csv.DictReader(stream))
    n = receipt['frames']
    if n < 3 or len(rows) != n or receipt['fps'] != fps:
        raise ValueError(f'{folder}: needs >=3 frames, matching telemetry and native {fps} fps')
    field = lambda key: np.array([float(r[key]) for r in rows])
    actual = np.column_stack((field('x'), field('y'), field('z')))
    t = np.arange(n) / (n - 1)
    camera = shot['camera']
    if camera.get('anchor') == 'player' or camera['mode'] == 'gameplay':
        raise ValueError('Use a moving fixed-world camera for this regression check')
    if camera['mode'] == 'orbit':
        centre = np.array(camera['centre'])[np.array([0, 2])]
        angle = np.deg2rad(camera.get('from_deg', 0) + (camera.get('to_deg', 10) - camera.get('from_deg', 0)) * t)
        radius = camera.get('radius', 10) + (camera.get('radius_to', camera.get('radius', 10)) - camera.get('radius', 10)) * t
        xz = centre + np.column_stack((np.sin(angle) * radius, np.cos(angle) * radius))
        height = field('y')[0] + (camera.get('height_to', camera.get('height', 3)) - camera.get('height', 3)) * t
        aim_xz = np.repeat(centre[None, :], n, axis=0)
    else:
        start = np.array(camera['from']['pos'])[np.array([0, 2])]
        end = np.array(camera.get('to', camera['from'])['pos'])[np.array([0, 2])]
        xz = start + (end - start) * t[:, None]
        height = field('y')[0] + (field('y')[-1] - field('y')[0]) * t
        aim_start = np.array(camera['from']['aim'])[np.array([0, 2])]
        aim_end = np.array(camera.get('to', camera['from'])['aim'])[np.array([0, 2])]
        aim_xz = aim_start + (aim_end - aim_start) * t[:, None]
    expected = np.column_stack((xz[:, 0], height, xz[:, 1]))
    aim_y = field('aim_y')[0] + (field('aim_y')[-1] - field('aim_y')[0]) * t
    aim = np.column_stack((aim_xz[:, 0], aim_y, aim_xz[:, 1]))
    forward = aim - expected
    forward /= np.linalg.norm(forward, axis=1)[:, None]
    qx, qy, qz, qw = (field('q' + axis) for axis in ('x', 'y', 'z', 'w'))
    drawn_forward = np.column_stack((-2 * (qx*qz + qw*qy), -2 * (qy*qz - qw*qx), -(1 - 2*(qx*qx + qy*qy))))
    drawn_forward /= np.linalg.norm(drawn_forward, axis=1)[:, None]
    rotation_errors = np.arccos(np.clip((forward * drawn_forward).sum(axis=1), -1, 1))
    fov = camera.get('fov', 50) + (camera.get('fov_to', camera.get('fov', 50)) - camera.get('fov', 50)) * t
    fov_error = float(np.max(np.abs(field('fov') - fov)))
    ideal_steps = np.linalg.norm(np.diff(expected, axis=0), axis=1)
    if ideal_steps.min() <= .003:
        raise ValueError('The regression camera must move more than 3 mm per sample in X/Z')
    position_errors = np.linalg.norm(actual - expected, axis=1)
    step_errors = np.linalg.norm(np.diff(actual, axis=0) - np.diff(expected, axis=0), axis=1)
    transform_repeats = np.flatnonzero(np.linalg.norm(np.diff(actual, axis=0), axis=1) < .00001) + 1
    render_gaps = np.flatnonzero(np.diff(field('render_frame')) != 1) + 1
    index_gaps = np.flatnonzero(field('frame') != np.arange(n))
    clock_error = float(np.max(np.abs(field('seconds') - np.arange(n) / fps)))
    ticks = np.diff(field('sim_tick'))
    expected_ticks = (n - 1) * 20 / fps
    image_repeats, mae = [], []
    previous = None
    for i in range(n):
        file = folder / f'f_{i:05d}.{shot.get("format", "jpg")}'
        with Image.open(file) as image:
            pixels = np.asarray(image.convert('RGB'), dtype=np.int16)
        if previous is not None:
            difference = np.abs(pixels - previous)
            mae.append(float(difference.mean()))
            if not difference.any():
                image_repeats.append(i)
        previous = pixels
    passed = (not image_repeats and not transform_repeats.size and not render_gaps.size and not index_gaps.size
              and position_errors.max() <= .003 and step_errors.max() <= .003 and clock_error < 1e-6
              and rotation_errors.max() < .0005 and fov_error < .0001 and abs(ticks.sum() - expected_ticks) <= 1
              and bool((ticks == 0).any()) and bool(((ticks >= 0) & (ticks <= 1)).all()))
    return {'shot': shot['name'], 'passed': bool(passed), 'frames': n, 'native_fps': fps,
            'repeated_images': image_repeats, 'repeated_camera_positions': transform_repeats.tolist(),
            'skipped_render_frames': render_gaps.tolist(), 'wrong_frame_indices': index_gaps.tolist(),
            'max_camera_position_error_m': float(position_errors.max()),
            'max_camera_step_error_m': float(step_errors.max()), 'max_clock_error_s': clock_error,
            'max_camera_rotation_error_deg': float(np.rad2deg(rotation_errors.max())), 'max_fov_error_deg': fov_error,
            'simulation_ticks': int(ticks.sum()), 'expected_simulation_ticks': expected_ticks,
            'render_frames_without_sim_tick': int((ticks == 0).sum()),
            'pixel_mae': {'min': min(mae), 'mean': float(np.mean(mae)), 'max': max(mae)}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--plan', type=Path, required=True)
    parser.add_argument('--frames', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    plan = json.loads(args.plan.read_text(encoding='utf-8-sig'))
    checks = [check(args.frames / shot['name'], shot) for shot in plan['shots']]
    proof = {'passed': all(c['passed'] for c in checks), 'checks': checks}
    args.output.write_text(json.dumps(proof, indent=2), encoding='utf-8')
    print(json.dumps(proof, indent=2))
    return 0 if proof['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
