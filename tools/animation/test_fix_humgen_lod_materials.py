"""Regression: material pruning must preserve sparse facial morph buffers exactly."""
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest


class LodMaterialTest(unittest.TestCase):
    def test_sparse_accessor_views_survive_pruning(self):
        blobs = [b'base', b'gone', b'\x00', b'morph-values', b'baked']
        binary = bytearray()
        views = []
        for blob in blobs:
            binary += b'\0' * (-len(binary) % 4)
            views.append(dict(buffer=0, byteOffset=len(binary), byteLength=len(blob)))
            binary += blob
        binary += b'\0' * (-len(binary) % 4)
        doc = dict(asset=dict(version='2.0'), buffers=[dict(byteLength=len(binary))], bufferViews=views,
                   accessors=[dict(bufferView=0, componentType=5126, count=1, type='SCALAR',
                                   sparse=dict(count=1, indices=dict(bufferView=2, componentType=5121),
                                               values=dict(bufferView=3)))],
                   nodes=[dict(name='Shirt_LOD0', mesh=0), dict(name='Shirt_LOD1', mesh=1)],
                   meshes=[dict(primitives=[dict(attributes=dict(POSITION=0), material=i)]) for i in (0, 1)],
                   materials=[dict(pbrMetallicRoughness=dict(baseColorTexture=dict(index=i))) for i in (0, 1)],
                   textures=[dict(source=i) for i in (0, 1)],
                   images=[dict(name='baked', bufferView=4), dict(name='HG_TSHIRT_Material.001_BaseColor', bufferView=1)])
        text = json.dumps(doc).encode()
        text += b' ' * (-len(text) % 4)
        raw = (struct.pack('<III', 0x46546c67, 2, 28 + len(text) + len(binary))
               + struct.pack('<II', len(text), 0x4E4F534A) + text
               + struct.pack('<II', len(binary), 0x004E4942) + binary)
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory)/'in.glb', Path(directory)/'out.glb'
            source.write_bytes(raw)
            subprocess.run([sys.executable, str(Path(__file__).with_name('fix_humgen_lod_materials.py')),
                            str(source), str(target)], check=True, capture_output=True)
            result = target.read_bytes()
        size = struct.unpack_from('<I', result, 12)[0]
        after, packed = json.loads(result[20:20+size]), result[28+size:]
        self.assertEqual([p['material'] for m in after['meshes'] for p in m['primitives']], [0, 0])
        self.assertEqual(len(after['images']), 1)
        sparse = after['accessors'][0]['sparse']
        for holder, expected in [(sparse['indices'], blobs[2]), (sparse['values'], blobs[3])]:
            view = after['bufferViews'][holder['bufferView']]
            self.assertEqual(packed[view['byteOffset']:view['byteOffset']+view['byteLength']], expected)


if __name__ == '__main__':
    unittest.main()
