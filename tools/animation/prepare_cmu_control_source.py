"""Prepare owned CMU glTF takes without changing their recorded channels or rigs.

The source-only witness permits Epic's skeletal importer to read animation-only skins.
One scene wrapper converts this archive's centimetres to glTF metres. Neither goes in game.
"""
import argparse
import copy
import hashlib
import json
import sys
import zipfile
from pathlib import Path

import numpy as np
from audit_humgen_transport import Glb as Measure

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'character_build'))
from glb import Glb


def prepare(archive, output, clips):
    output.mkdir(parents=True, exist_ok=True)
    sources = []
    with zipfile.ZipFile(archive) as pack:
        for clip in clips:
            member = 'Anims_Only_glTF_V1/glTF/' + clip + '.glb'
            original = output / (clip + '.glb')
            original.write_bytes(pack.read(member))
            source = Measure(original)
            rest = source.globals()
            model = Glb(original)
            nodes, skins, animations = (copy.deepcopy(model.doc[k]) for k in ('nodes', 'skins', 'animations'))
            binary = bytes(model.bin)
            positions, joints, weights = [], [], []
            for joint, node in enumerate(skins[0]['joints']):
                centre = rest[model.doc['nodes'][node]['name']][:3, 3]
                for delta in ((.04, 0, 0), (0, .04, 0), (0, 0, .04)):
                    positions.append(centre + delta)
                    joints.append([joint, 0, 0, 0])
                    weights.append([1, 0, 0, 0])
            attributes = {}
            for name, values, dtype, width, kind in (
                    ('POSITION', positions, '<f4', 3, 5126),
                    ('JOINTS_0', joints, '<u2', 4, 5123),
                    ('WEIGHTS_0', weights, '<f4', 4, 5126)):
                array = np.array(values, dtype=dtype)
                view = len(model.doc['bufferViews'])
                model.doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(model.bin), 'byteLength': array.nbytes})
                model.bin.extend(array.tobytes())
                accessor = {'bufferView': view, 'componentType': kind, 'count': len(array), 'type': 'VEC' + str(width)}
                if name == 'POSITION':
                    accessor.update(min=array.min(0).tolist(), max=array.max(0).tolist())
                attributes[name] = len(model.doc['accessors'])
                model.doc['accessors'].append(accessor)
            mesh = len(model.doc.get('meshes', []))
            model.doc.setdefault('meshes', []).append({'name': 'CMU_source_witness_DO_NOT_SHIP', 'primitives': [{'attributes': attributes}]})
            node = len(model.doc['nodes'])
            model.doc['nodes'].append({'name': 'CMU_source_witness_DO_NOT_SHIP', 'mesh': mesh, 'skin': 0})
            model.doc['scenes'][model.doc.get('scene', 0)]['nodes'].append(node)
            for scene in model.doc['scenes']:
                node = len(model.doc['nodes'])
                model.doc['nodes'].append({'name': 'CMU_centimetres_to_metres_SOURCE_ONLY', 'scale': [.01] * 3, 'children': scene['nodes'][:]})
                scene['nodes'] = [node]
            prepared = output / (clip + '_metres_source_preview.glb')
            model.save(prepared)
            readback = Glb(prepared)
            assert readback.doc['nodes'][:len(nodes)] == nodes and readback.doc['skins'] == skins
            assert readback.doc['animations'] == animations and bytes(readback.bin[:len(binary)]) == binary
            reference = Measure(prepared).globals()
            height = 100 * (reference['hip'][1, 3] - min(m[1, 3] for name, m in reference.items()
                                                       if 'foot' in name.lower() or 'toe' in name.lower()))
            assert 50 < height < 130, 'Reference calibration is not in adult centimetres'
            sources.append({'id': 'cmu_control_metres_' + clip, 'path': str(prepared),
                            'sha256': hashlib.sha256(prepared.read_bytes()).hexdigest(),
                            'original_sha256': hashlib.sha256(original.read_bytes()).hexdigest(),
                            'archive': str(archive), 'member': member, 'rest_root_height_cm': float(height),
                            'original_nodes_skins_channels_and_binary_prefix_unchanged': True})
    return {'status': 'DONE', 'scope': 'Owned CMU/cgspeed/Rancidmilk source preparation only; retain acquisition credits/terms. Not game art or rendered acceptance.', 'sources': sources}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('archive', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('clips', nargs='+')
    args = parser.parse_args()
    (args.output / 'CMU_SOURCES_PREPARED.json').write_text(json.dumps(prepare(args.archive, args.output, args.clips), indent=2))
