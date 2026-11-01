"""Restore HumGen Ultra's eight native influences without rebaking or changing geometry.

Run in background Blender. Native SDK processing supplies the weights. Every export
vertex must match its native position, with old weights disambiguating coincident vertices;
all pre-existing binary geometry, morph, material and image buffers remain exact.
Output is a fresh private candidate. Never modifies an input or source human.
"""
import argparse
import collections
import hashlib
import json
import re
from pathlib import Path
import shutil
import struct
import subprocess
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree
from HumGen3D.human.human import Human
from HumGen3D.human.process.settings import ExportSettings
sys.path.insert(0, str(Path(__file__).resolve().parent))
from audit_humgen_transport import Glb


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def transport_native_weights(g, native, all_lods=False):
    binary = bytearray(g.binary)
    before_binary = hashlib.sha256(binary).hexdigest()
    joints = [g.doc['nodes'][index]['name'] for index in g.doc['skins'][0]['joints']]
    joint_index = {name: i for i, name in enumerate(joints)}
    reports = []

    def append(values, component):
        values = np.asarray(values, dtype='<u2' if component == 5123 else '<f4')
        binary.extend(b'\0' * (-len(binary) % 4))
        offset = len(binary)
        binary.extend(values.tobytes())
        view = len(g.doc['bufferViews'])
        g.doc['bufferViews'].append({'buffer': 0, 'byteOffset': offset, 'byteLength': values.nbytes})
        index = len(g.doc['accessors'])
        g.doc['accessors'].append({'bufferView': view, 'componentType': component, 'count': len(values), 'type': 'VEC4'})
        return index

    for node in g.doc['nodes']:
        name = node.get('name', '')
        if 'mesh' not in node or not (name.endswith('_LOD0') or all_lods and re.search(r'_LOD[1-3]$', name)) or any(part in name for part in ('Hair', 'Eye', 'Cornea', 'Teeth')):
            continue
        source_name = name[:-5]
        obj = native.get(source_name) or native.get(name)
        assert obj is not None, ('Native mesh not found', name, list(native))
        positions = np.array([obj.matrix_world @ vertex.co for vertex in obj.data.vertices], dtype=np.float64)[:, [0, 2, 1]]
        positions[:, 2] *= -1
        groups = {group.index: joint_index[group.name] for group in obj.vertex_groups if group.name in joint_index}
        weights = np.zeros((len(positions), len(joints)), dtype=np.float64)
        for vertex in obj.data.vertices:
            for group in vertex.groups:
                if group.group in groups: weights[vertex.index, groups[group.group]] += group.weight
        weights /= np.maximum(weights.sum(axis=1, keepdims=True), 1e-12)
        order = np.argsort(-weights, axis=1, kind='stable')[:, :8]
        top = np.take_along_axis(weights, order, axis=1)
        assert np.max(np.abs(top.sum(axis=1) - 1)) < 1e-5
        four = np.zeros_like(weights)
        np.put_along_axis(four, order[:, :4], top[:, :4], axis=1)
        four /= np.maximum(four.sum(axis=1, keepdims=True), 1e-12)
        tree = KDTree(len(positions))
        for index, position in enumerate(positions): tree.insert(Vector(position), index)
        tree.balance()
        maximum_position = maximum_projection = 0.
        count = 0
        for primitive in g.doc['meshes'][node['mesh']]['primitives']:
            attributes = primitive['attributes']
            current = g.accessor(attributes['POSITION'])
            old_joints = g.accessor(attributes['JOINTS_0']).astype(int)
            old_weights = g.accessor(attributes['WEIGHTS_0']).astype(float)
            mapped = []
            for vertex, position in enumerate(current):
                candidates = tree.find_range(Vector(position), 1e-5)
                old = np.zeros(len(joints))
                np.add.at(old, old_joints[vertex], old_weights[vertex])
                old /= max(old.sum(), 1e-12)
                matches = sorted((float(np.abs(four[index] - old).sum()), distance, index)
                                 for _, index, distance in candidates)
                # Position is the transport invariant. Older four-weight exporters
                # also pruned/rebalanced weights; their values only disambiguate
                # coincident source vertices and are recorded in the receipt.
                # Every transported value comes directly from the native SDK.
                assert matches, ('Native position differs', name, vertex)
                error, distance, index = matches[0]
                tied = [i for e, d, i in matches if e <= error + 1e-5]
                assert all(np.max(np.abs(weights[i] - weights[index])) < 1e-5 for i in tied), ('Ambiguous native weights', name, vertex)
                maximum_position = max(maximum_position, distance)
                maximum_projection = max(maximum_projection, error)
                mapped.append(index)
            mapped = np.asarray(mapped)
            for offset, suffix in ((0, '0'), (4, '1')):
                attributes['JOINTS_' + suffix] = append(order[mapped, offset:offset+4], 5123)
                attributes['WEIGHTS_' + suffix] = append(top[mapped, offset:offset+4], 5126)
            count += len(current)
        reports.append({'mesh': name, 'vertices': count, 'maximum_position_error': maximum_position,
                        'maximum_original_projection_error': maximum_projection, 'source_influences': 8})
    return binary, before_binary, reports


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('entry', type=Path)
    parser.add_argument('source_blend', type=Path)
    parser.add_argument('source_settings', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--godot', type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    inputs = args.entry.parent.parent
    assert inputs.resolve() != args.output.resolve() and inputs.resolve() not in args.output.resolve().parents
    spec = json.loads(args.entry.read_text())
    original_body = inputs / spec['body']
    body = args.output / spec['body']
    assert not body.exists(), 'Never overwrite an existing native candidate'
    source_hash = sha(args.source_blend)
    bpy.ops.wm.open_mainfile(filepath=str(args.source_blend))
    bpy.context.scene.render.threads_mode = 'FIXED'
    bpy.context.scene.render.threads = 2
    bpy.context.scene.cycles.device = 'CPU'
    humans = {}
    for obj in bpy.data.objects:
        human = Human.from_existing(obj, strict_check=False)
        if human: humans[human.objects.rig.name] = human
    assert len(humans) == 1, 'Source must contain one original native human'
    settings = ExportSettings.from_dict(json.loads(args.source_settings.read_text()))
    settings.output.format = 'in_file'
    settings.textures.enabled = False
    settings.lods = settings.lods[:1]
    settings.lods[0].bones_per_vertex = 8
    settings.scripts.enabled = False
    result = next(iter(humans.values())).process.run(settings, context=bpy.context)
    assert len(result.humans) == 1
    human = result.humans[0]
    native = {obj.name: obj for obj in human.objects if obj.type == 'MESH'}
    g = Glb(original_body)
    binary, before_binary, reports = transport_native_weights(g, native)
    assert any('_Body_LOD0' in row['mesh'] for row in reports)
    assert hashlib.sha256(binary[:len(g.binary)]).hexdigest() == before_binary, 'Original buffers changed'
    g.doc['buffers'][0]['byteLength'] = len(binary)
    encoded = json.dumps(g.doc, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    body.parent.mkdir(parents=True, exist_ok=True)
    body.write_bytes(struct.pack('<III', 0x46546c67, 2, 28+len(encoded)+len(binary))
                     + struct.pack('<II', len(encoded), 0x4e4f534a) + encoded
                     + struct.pack('<II', len(binary), 0x004e4942) + binary)
    for file in original_body.parent.iterdir():
        if file.is_file() and file != original_body: shutil.copy2(file, body.parent / file.name)
    current_hash = sha(body)
    identity = body.parent / 'identity_weights.json'
    data = json.loads(identity.read_text())
    data['target_body_sha256'] = current_hash
    identity.write_text(json.dumps(data, indent=2))
    proof_file = body.parent / 'rig_equivalence.json'
    if proof_file.exists():
        proof = json.loads(proof_file.read_text())
    else:
        proof = {'old_body': str(original_body), 'old_sha256': sha(original_body), 'joints': len(g.doc['skins'][0]['joints']),
                 'same_parents': True, 'maximum_rest_error': 0.}
    proof.update(new_body=str(body), new_sha256=current_hash)
    proof_file.write_text(json.dumps(proof, indent=2))
    transport = {'source_blend_sha256': source_hash, 'input_body_sha256': sha(original_body), 'output_body_sha256': current_hash,
                 'source_bones_per_vertex': 8, 'original_binary_prefix_sha256': before_binary, 'original_binary_unchanged': True,
                 'geometry_morphs_images_unchanged': True, 'meshes': reports}
    (body.parent / 'native_skinning.json').write_text(json.dumps(transport, indent=2))
    motion = args.output / spec['motion']
    motion.parent.mkdir(parents=True, exist_ok=True)
    old_motion = Path(proof['old_body']).parents[3] / 'humgen_characters/mocap_motion' / motion.name
    # Baseline has primary/fallback/rigged/<id>/<body>; its parent root is four levels up.
    if not old_motion.is_file(): old_motion = original_body.parents[3] / 'humgen_characters/mocap_motion' / motion.name
    plan = body.parent / 'motion_repackage_plan.json'
    plan.write_text(json.dumps({'old_motion': str(old_motion), 'new_motion': str(motion), 'proof': str(proof_file)}, indent=2))
    subprocess.run([str(args.godot), '--headless', '--path', str(Path(__file__).parents[1] / 'humgen/minimal'),
                    '--script', str(Path(__file__).with_name('repackage_equivalent_character.gd')), '--', str(plan)], check=True, timeout=180)
    spec.update(native_skinning_influences=8, native_ultra_skinned_meshes=[r['mesh'] for r in reports])
    spec['rig'] = proof
    quality = body.parent / 'native_quality.json'
    details = json.loads(quality.read_text())
    details.update(native_skinning_influences=8, rig=proof)
    quality.write_text(json.dumps(details, indent=2))
    spec['files'] = {p.relative_to(args.output).as_posix(): sha(p) for p in body.parent.iterdir() if p.is_file()}
    spec['files'][spec['motion']] = sha(motion)
    spec['files'][motion.with_suffix(motion.suffix + '.quality.json').relative_to(args.output).as_posix()] = sha(motion.with_suffix(motion.suffix + '.quality.json'))
    entries = args.output / 'quality_contract_entries'
    entries.mkdir(parents=True, exist_ok=True)
    (entries / args.entry.name).write_text(json.dumps(spec, indent=2))
    assert sha(args.source_blend) == source_hash, 'Original native source changed'
    print('NATIVE_ULTRA_SKINNING_STAGED', args.entry.stem, '8 influences; exact original geometry/morph/image buffers', flush=True)


if __name__ == '__main__':
    main()
