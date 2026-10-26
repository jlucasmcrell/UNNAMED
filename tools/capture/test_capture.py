import json
from pathlib import Path
import tempfile
import unittest

from capture import ENCODER_FILTER, stage_payload, validate_plan


class CaptureChecks(unittest.TestCase):
    def test_manifests_are_private_and_overlap_is_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source"
            source.mkdir()
            (source / "manifest.json").write_text('{"version":1}')
            (source / "image.png").write_bytes(b"immutable")
            receipt = stage_payload(source, root / "private")
            (root / "private/manifest.json").write_text('{"version":2}')
            self.assertEqual(json.loads((source / "manifest.json").read_text()), {"version": 1})
            self.assertEqual(receipt["copied"], 1)
            with self.assertRaises(ValueError):
                stage_payload(source, source / "nested")

    def test_invalid_placement_and_console_wrapper_are_rejected(self):
        plan = dict.fromkeys(("exe", "payload", "ffmpeg", "ffprobe", "pwsh", "gpu_lock", "owner_flag"), str(Path.cwd() / "file"))
        plan.update(x=450, z=737, yaw=45, pitch=-0.1, distance=2)
        validate_plan(plan)
        for change in ({"x": float("nan")}, {"distance": 0}, {"seconds": 1}, {"size": [0, 1080]},
                       {"exe": str(Path.cwd() / "godot_console.exe")}):
            with self.assertRaises(ValueError):
                validate_plan(plan | change)
        self.assertIn("pad=ceil(iw/2)*2:ceil(ih/2)*2", ENCODER_FILTER)


if __name__ == "__main__":
    unittest.main()
