"""CPU negative controls for the frame-diff checker; actual acceptance uses packaged-game captures."""
import csv
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

from frame_diff import check


class FrameDiffTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.shot = {'name': 'overhead', 'format': 'png', 'camera': {'mode': 'keys',
                     'from': {'pos': [0, 40, 0], 'aim': [16, 40, 12]}, 'to': {'pos': [12, 40, 6], 'aim': [16, 40, 12]}}}
        (self.folder / 'shot_done.json').write_text(json.dumps({'frames': 12, 'fps': 60}))
        self.rows = []
        for i in range(12):
            pixels = np.zeros((8, 32, 3), dtype=np.uint8)
            pixels[:, i:i+4] = [30 + i, 150, 220]
            Image.fromarray(pixels).save(self.folder / f'f_{i:05d}.png')
            yaw = np.arctan2(-(16 - 12*i/11), -(12 - 6*i/11))
            self.rows.append({'frame': i, 'render_frame': 100 + i, 'sim_tick': 20 + i // 3,
                              'seconds': i / 60, 'x': 12 * i / 11, 'y': 40, 'z': 6 * i / 11,
                              'qx': 0, 'qy': np.sin(yaw/2), 'qz': 0, 'qw': np.cos(yaw/2),
                              'fov': 50, 'aim_x': 16, 'aim_y': 40, 'aim_z': 12})
        self.write_rows()

    def write_rows(self):
        with (self.folder / 'camera_frames.csv').open('w', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=list(self.rows[0]))
            writer.writeheader(); writer.writerows(self.rows)

    def test_smooth_render_samples_pass_despite_twenty_hertz_simulation(self):
        self.assertTrue(check(self.folder, self.shot)['passed'])

    def test_a_held_image_is_rejected(self):
        (self.folder / 'f_00005.png').write_bytes((self.folder / 'f_00004.png').read_bytes())
        proof = check(self.folder, self.shot)
        self.assertFalse(proof['passed']); self.assertEqual([5], proof['repeated_images'])

    def test_a_skipped_camera_step_is_rejected_even_when_every_image_changes(self):
        self.rows[5]['x'] = self.rows[6]['x']; self.rows[5]['z'] = self.rows[6]['z']; self.write_rows()
        proof = check(self.folder, self.shot)
        self.assertFalse(proof['passed']); self.assertGreater(proof['max_camera_step_error_m'], .003)

    def test_a_missing_render_sample_is_rejected(self):
        self.rows[5]['render_frame'] += 1; self.write_rows()
        self.assertFalse(check(self.folder, self.shot)['passed'])

    def test_a_held_rotation_is_rejected_even_when_positions_and_images_advance(self):
        for key in ('qx', 'qy', 'qz', 'qw'):
            self.rows[5][key] = self.rows[4][key]
        self.write_rows()
        self.assertFalse(check(self.folder, self.shot)['passed'])

    def test_a_paused_simulation_cannot_pass_as_a_normal_film(self):
        for row in self.rows:
            row['sim_tick'] = 20
        self.write_rows()
        self.assertFalse(check(self.folder, self.shot)['passed'])

    def test_a_tilted_orbit_at_world_coordinates_passes(self):
        self.shot['camera'] = {'mode': 'orbit', 'centre': [427, 1.2, 758], 'radius': 12,
                               'height': 3, 'from_deg': 285, 'to_deg': 325, 'fov': 45}
        for i, row in enumerate(self.rows):
            angle = np.deg2rad(285 + 40 * i / 11)
            x, z = 427 + np.sin(angle) * 12, 758 + np.cos(angle) * 12
            yaw, pitch = np.arctan2(-(427-x), -(758-z)), np.arctan2(-3, 12)
            sy, cy, sx, cx = np.sin(yaw/2), np.cos(yaw/2), np.sin(pitch/2), np.cos(pitch/2)
            row.update(x=x, y=11.2, z=z, qx=cy*sx, qy=sy*cx, qz=-sy*sx, qw=cy*cx,
                       fov=45, aim_x=427, aim_y=8.2, aim_z=758)
        self.write_rows()
        self.assertTrue(check(self.folder, self.shot)['passed'])


if __name__ == '__main__':
    unittest.main()
