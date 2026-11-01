import json
import struct
import tempfile
import unittest
from pathlib import Path

import numpy as np

from glb import Glb


class SparseAccessorTests(unittest.TestCase):
    def test_sparse_overlay_with_strided_and_implicit_zero_bases(self):
        binary = struct.pack('<4f4fH2x3f', 1, 2, 3, 99, 4, 5, 6, 99, 1, 7, 8, 9)
        sparse = {'count': 1, 'indices': {'bufferView': 1, 'componentType': 5123},
                  'values': {'bufferView': 2}}
        base = {'componentType': 5126, 'count': 2, 'type': 'VEC3'}
        doc = {'asset': {'version': '2.0'}, 'buffers': [{'byteLength': len(binary)}],
               'bufferViews': [{'buffer': 0, 'byteLength': 32, 'byteStride': 16},
                               {'buffer': 0, 'byteOffset': 32, 'byteLength': 2},
                               {'buffer': 0, 'byteOffset': 36, 'byteLength': 12}],
               'accessors': [dict(base, bufferView=0), dict(base, bufferView=0, sparse=sparse),
                             dict(base, sparse=sparse)]}
        encoded = json.dumps(doc).encode()
        encoded += b' ' * (-len(encoded) % 4)
        raw = (struct.pack('<4sII', b'glTF', 2, 28+len(encoded)+len(binary))
               + struct.pack('<I4s', len(encoded), b'JSON') + encoded
               + struct.pack('<I4s', len(binary), b'BIN\0') + binary)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'accessors.glb'
            path.write_bytes(raw)
            model = Glb(path)
            for index, expected in enumerate([[[1, 2, 3], [4, 5, 6]],
                                              [[1, 2, 3], [7, 8, 9]],
                                              [[0, 0, 0], [7, 8, 9]]]):
                np.testing.assert_array_equal(model.read(index), expected)
            self.assertEqual(bytes(model.bin), binary)
            self.assertEqual(path.read_bytes(), raw)


if __name__ == '__main__':
    unittest.main()
