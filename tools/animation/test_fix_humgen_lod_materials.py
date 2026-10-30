"""Regression: garment repair preserves sparse face buffers and native LOD atlases."""
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest


class LodMaterialTest(unittest.TestCase):
    def test_creator_staging_spacing_and_lining_are_repaired_without_changing_morph_deltas(self):
        positions = [(0., 1., 0.), (0., 1., -4.), (0., 1., -.003), (0., 1., -4.003), (.01, .02, .03)]
        binary = b''.join(struct.pack('<fff', *p) for p in positions)
        doc = dict(asset=dict(version='2.0'), buffers=[dict(byteLength=len(binary))],
                   bufferViews=[dict(buffer=0, byteOffset=i*12, byteLength=12) for i in range(5)],
                   accessors=[dict(bufferView=i, componentType=5126, count=1, type='VEC3', min=list(p), max=list(p)) for i,p in enumerate(positions)],
                   nodes=[dict(name=n, mesh=i) for i,n in enumerate(['Shirt_LOD0', 'Shirt_LOD1', 'Shirt_LOD0_lining', 'Shirt_LOD1_lining'])],
                   meshes=[dict(primitives=[dict(attributes=dict(POSITION=i, **({'COLOR_0':4} if i%2 else {})), material=i%2,
                                                targets=[dict(POSITION=4)])]) for i in range(4)],
                   materials=[dict(name='baked'), dict(name='procedural mask')], textures=[], images=[])
        text = json.dumps(doc).encode();text += b' ' * (-len(text) % 4)
        raw = (struct.pack('<III', 0x46546c67, 2, 28+len(text)+len(binary))
               + struct.pack('<II', len(text), 0x4E4F534A) + text
               + struct.pack('<II', len(binary), 0x004E4942) + binary)
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory)/'in.glb', Path(directory)/'out.glb'
            source.write_bytes(raw)
            subprocess.run([sys.executable, str(Path(__file__).with_name('fix_humgen_lod_materials.py')),
                            str(source), str(target), '--creator-staging-offsets'], check=True, capture_output=True)
            result = target.read_bytes()
            subprocess.run([sys.executable, str(Path(__file__).with_name('fix_humgen_lod_materials.py')),
                            str(target), str(target), '--creator-staging-offsets'], check=True, capture_output=True)
            self.assertEqual(result, target.read_bytes(), 'A second repair must not translate an already aligned garment')
        size=struct.unpack_from('<I', result, 12)[0]
        after, packed=json.loads(result[20:20+size]),result[28+size:]
        self.assertEqual(packed[48:60], binary[48:60])
        for i,z in enumerate([0., 0., -.003, -.003]):
            self.assertAlmostEqual(struct.unpack_from('<fff',packed,i*12)[2],z,places=5)
            primitive=after['meshes'][i]['primitives'][0]
            self.assertEqual(primitive['material'],0)
            self.assertNotIn('COLOR_0',primitive['attributes'])
            self.assertEqual(primitive['targets'],[dict(POSITION=4)])

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
                   nodes=[dict(name='Shirt_LOD0', mesh=0), dict(name='Shirt_LOD1', mesh=1),
                          dict(name='Eyes_LOD0', mesh=2), dict(name='Eyes_LOD1', mesh=3)],
                   meshes=[dict(primitives=[dict(attributes=dict(POSITION=0), material=i)]) for i in range(4)],
                   materials=[dict(pbrMetallicRoughness=dict(baseColorTexture=dict(index=i))) for i in range(4)],
                   textures=[dict(source=i) for i in range(4)],
                   images=[dict(name='baked', bufferView=4), dict(name='HG_TSHIRT_Material.001_BaseColor', bufferView=1),
                           dict(name='EyesInner_BaseColor', bufferView=4), dict(name='Eyes_BaseColor_LOD1', bufferView=4)])
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
        self.assertEqual([p['material'] for m in after['meshes'] for p in m['primitives']], [0, 0, 1, 2])
        self.assertEqual(len(after['images']), 3)
        sparse = after['accessors'][0]['sparse']
        for holder, expected in [(sparse['indices'], blobs[2]), (sparse['values'], blobs[3])]:
            view = after['bufferViews'][holder['bufferView']]
            self.assertEqual(packed[view['byteOffset']:view['byteOffset']+view['byteLength']], expected)


if __name__ == '__main__':
    unittest.main()
