import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[2] / 'tools'))
spec = importlib.util.spec_from_file_location('receipt', Path(__file__).parents[2] / 'tools/manifest_barrowworks_scene_cache.py')
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class NativeReceiptTests(unittest.TestCase):
    def test_corrupt_source_stamp_is_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            path = root / 'barrowworks_native/engine/v6/scenes/ready/test.scn'
            path.parent.mkdir(parents=True)
            path.write_bytes(b'test native scene')
            source = root / 'source.glb'
            source.write_bytes(b'source bytes')
            Path(str(path) + '.source.json').write_text(json.dumps({'source': str(source), 'stamp': 'v6 engine BAD'}))
            Path(str(path) + '.sha256').write_text('v6 engine BAD')
            with patch.object(publisher, 'owner_check'), self.assertRaisesRegex(ValueError, 'source/stamp mismatch'):
                publisher.receipt(root, 'v6')

    def test_missing_cache_cannot_be_reported_as_complete(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(publisher, 'owner_check'):
            with self.assertRaisesRegex(ValueError, 'No complete v6'):
                publisher.receipt(Path(folder), 'v6')


if __name__ == '__main__':
    unittest.main()
