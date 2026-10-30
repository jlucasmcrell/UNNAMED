"""Regression checks for the exposed-skin holes seen in the native creator."""
import json,tempfile,unittest
from pathlib import Path
import numpy as np
from PIL import Image
import bake_body_coverage as coverage

class CoverageTests(unittest.TestCase):
    def render(self,data):
        with tempfile.TemporaryDirectory() as directory:
            folder=Path(directory);(folder/'body_coverage.json').write_text(json.dumps(data))
            old=coverage.SIZE
            try:
                coverage.SIZE=32;coverage.bake(folder)
                return np.array(Image.open(folder/'body_coverage.png'))
            finally:coverage.SIZE=old

    def test_hem_padding_does_not_wrap_onto_opposite_atlas_edge(self):
        image=self.render({'uv_triangles':[[.2,0,.8,0,.5,.06]]})
        self.assertTrue(image[-1].any())
        self.assertFalse(image[:3].any())

    def test_seam_padding_keeps_exposed_skin_clear(self):
        image=self.render({'uv_triangles':[[0,0,1,0,0,1]],
                           'protected_uv_triangles':[[.4,.4,.65,.4,.4,.65]]})
        self.assertEqual(image[16,16],0)
        self.assertEqual(image[28,3],255)

if __name__=='__main__':unittest.main()
