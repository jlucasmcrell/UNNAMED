"""Classical mesh contact correctives on the owned frontier shirt, without altering its base skin.

The original topology, UVs, embedded maps, 59-joint bind and base vertex data remain.
Added glTF morphs solve the recorded posed cloth against the floor and native body.
This is an offline candidate authoring step, requiring native visual review.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree

sys.path.insert(0, str(Path(__file__).parents[1]/'character_build'))
from glb import Glb
from audit_humgen_transport import Glb as Motion


def matrix_weights(model, primitive, skin, pose):
    attributes = primitive['attributes']
    joints = model.read(attributes['JOINTS_0']).astype(int)
    weights = model.read(attributes['WEIGHTS_0'])
    bind = model.read(skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    names = [model.doc['nodes'][i]['name'] for i in skin['joints']]
    matrices = np.asarray([pose[name] @ bind[i] for i, name in enumerate(names)])
    return sum(weights[:, k, None, None]*matrices[joints[:, k]] for k in range(4))


def normals(points, triangles):
    result = np.zeros_like(points)
    vectors = np.cross(points[triangles[:, 1]]-points[triangles[:, 0]], points[triangles[:, 2]]-points[triangles[:, 0]])
    for corner in range(3):
        np.add.at(result, triangles[:, corner], vectors)
    return result/np.maximum(np.linalg.norm(result, axis=1)[:, None], 1e-12)


def solve(points, triangles, mobility, body, body_normal):
    edges = np.unique(np.sort(np.concatenate([triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]]]), axis=1), axis=0)
    seams = np.asarray(list(cKDTree(points).query_pairs(1e-6)), dtype=int).reshape(-1, 2)
    if len(seams):
        edges = np.unique(np.r_[edges, seams], axis=0)
    a, b = edges.T
    lengths = np.linalg.norm(points[b]-points[a], axis=1)
    mass = mobility[a]+mobility[b]
    active = mass > 0
    a, b, lengths, mass = a[active], b[active], lengths[active], mass[active]
    degree = np.bincount(np.r_[a, b], minlength=len(points))
    tree = cKDTree(body)
    result = points.copy()
    for iteration in range(100):
        difference = result[b]-result[a]
        distance = np.linalg.norm(difference, axis=1)
        correction = difference*((distance-lengths)/np.maximum(distance, 1e-9)/mass)[:, None]
        offsets = np.zeros_like(result)
        np.add.at(offsets, a, correction*mobility[a, None])
        np.add.at(offsets, b, -correction*mobility[b, None])
        result += offsets/np.maximum(degree, 1)[:, None]*.8
        if iteration % 4 == 0:
            nearest_distance, nearest = tree.query(result)
            n = body_normal[nearest]
            signed = np.sum((result-body[nearest])*n, axis=1)
            push = np.clip(.002-signed, 0, .025)
            push[(nearest_distance > .065) | (mobility == 0)] = 0
            result += push[:, None]*n
        result[:, 1] = np.maximum(result[:, 1], np.where(mobility > 0, .004, result[:, 1]))
    stretch = np.abs(np.linalg.norm(result[b]-result[a], axis=1)[lengths > 1e-5]/lengths[lengths > 1e-5]-1)
    return result, dict(min_y=float(result[:, 1].min()), maximum_displacement=float(np.linalg.norm(result-points, axis=1).max()), edge_strain_p95=float(np.quantile(stretch, .95)))


def bake(spec):
    source = Path(spec['shirt'])
    model = Glb(source)
    original_bytes = len(model.bin)
    original_binary_sha = hashlib.sha256(model.bin).hexdigest()
    body = Glb(Path(spec['contact_body']))
    selected = [(node, model.doc['meshes'][node['mesh']]) for node in model.doc['nodes']
                if 'mesh' in node and 'Shirt_LOD' in node.get('name', '')+model.doc['meshes'][node['mesh']].get('name', '')]
    assert len(selected) == 4, [node.get('name') for node, _ in selected]
    original_weights = {id(mesh): list(node.get('weights', mesh.get('weights', []))) for node, mesh in selected}
    original_names = {id(mesh): list(mesh.get('extras', {}).get('targetNames', [])) for _, mesh in selected}
    for _, mesh in selected:
        assert len(original_weights[id(mesh)]) == len(original_names[id(mesh)])
        for primitive in mesh['primitives']:
            assert len(primitive.get('targets', [])) == len(original_weights[id(mesh)])
            primitive.setdefault('targets', [])

    def accessor(values):
        values = np.asarray(values, dtype='<f4')
        model.bin.extend(b'\0'*(-len(model.bin) % 4))
        view = len(model.doc['bufferViews'])
        model.doc['bufferViews'].append(dict(buffer=0, byteOffset=len(model.bin), byteLength=values.nbytes))
        model.bin.extend(values.tobytes())
        index = len(model.doc['accessors'])
        model.doc['accessors'].append(dict(bufferView=view, componentType=5126, count=len(values), type='VEC3', min=values.min(0).tolist(), max=values.max(0).tolist()))
        return index

    targets, tracks, reports = [], {}, []
    for entry in spec['clips']:
        if entry['state'] not in ['prone_fall', 'prone_idle', 'prone_rise']:
            continue
        motion = Motion(Path(entry['path']))
        duration = max(float(motion.accessor(s['input'])[-1, 0]) for s in motion.doc['animations'][0]['samplers'])
        rate = abs(entry.get('source_rate_scale', 1))
        first = entry.get('start_seconds', 0)*rate
        last = entry.get('end_seconds', duration/rate)*rate
        count = 7 if entry['state'] == 'prone_idle' else 13
        tracks[entry['state']] = []
        for sample, at in enumerate(np.linspace(first, last, count)):
            pose = motion.globals(float(at))
            root = pose['Root'][:3, 3].copy()
            height = np.interp(at, *np.asarray(entry['root_height_samples']).T)
            for matrix in pose.values():
                matrix[:3, 3] += [-root[0], height, -root[2]]
            body_points, body_normals = [], []
            for node in body.doc['nodes']:
                if 'mesh' not in node or 'Body_LOD0' not in node.get('name', '')+body.doc['meshes'][node['mesh']].get('name', ''):
                    continue
                for primitive in body.doc['meshes'][node['mesh']]['primitives']:
                    attributes = primitive['attributes']
                    skinning = matrix_weights(body, primitive, body.doc['skins'][node['skin']], pose)
                    points = body.read(attributes['POSITION'])
                    body_points.extend(np.einsum('nij,nj->ni', skinning, np.c_[points, np.ones(len(points))])[:, :3])
                    n = np.einsum('nji,nj->ni', np.linalg.inv(skinning[:, :3, :3]), body.read(attributes['NORMAL']))
                    body_normals.extend(n/np.maximum(np.linalg.norm(n, axis=1)[:, None], 1e-12))
            body_points, body_normals = np.asarray(body_points), np.asarray(body_normals)
            assert len(body_points) > 1000
            target = f"cmb_floor_{entry['state']}_{sample:02d}"
            targets.append(target)
            tracks[entry['state']].append(dict(seconds=float((at-first)/rate), target=target))
            for node, mesh in selected:
                for primitive in mesh['primitives']:
                    attributes = primitive['attributes']
                    points = model.read(attributes['POSITION'])
                    base_normal = model.read(attributes['NORMAL'])
                    for weight, original_target in zip(original_weights[id(mesh)], primitive['targets']):
                        if weight:
                            points += weight*model.read(original_target['POSITION'])
                            if 'NORMAL' in original_target:
                                base_normal += weight*model.read(original_target['NORMAL'])
                    triangles = model.read(primitive['indices']).astype(int).reshape(-1, 3)
                    skinning = matrix_weights(model, primitive, model.doc['skins'][node['skin']], pose)
                    posed = np.einsum('nij,nj->ni', skinning, np.c_[points, np.ones(len(points))])[:, :3]
                    mobility = ((points[:, 1] < 1.10) | (posed[:, 1] < .04)).astype(float)
                    corrected, report = solve(posed, triangles, mobility, body_points, body_normals)
                    rest = points+np.einsum('nij,nj->ni', np.linalg.inv(skinning[:, :3, :3]), corrected-posed)
                    assert report['maximum_displacement'] < .4, report
                    primitive['targets'].append(dict(POSITION=accessor(rest-points), NORMAL=accessor(normals(rest, triangles)-base_normal)))
                    reports.append(dict(target=target, mesh=node.get('name'), **report))
            print(target, flush=True)
    for node, mesh in selected:
        mesh['weights'] = original_weights[id(mesh)]+[0.0]*len(targets)
        mesh.setdefault('extras', {})['targetNames'] = original_names[id(mesh)]+targets
        node['weights'] = mesh['weights']
    model.doc['buffers'][0]['byteLength'] = len(model.bin)
    output = Path(spec['output'])
    output.parent.mkdir(parents=True, exist_ok=True)
    model.save(output)
    check = Glb(output)
    assert hashlib.sha256(check.bin[:original_bytes]).hexdigest() == original_binary_sha
    proof = dict(status='OFFLINE_CONTACT_CANDIDATE_REQUIRES_NATIVE_REVIEW', source=str(source), source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(), output_sha256=hashlib.sha256(output.read_bytes()).hexdigest(), original_binary_prefix_bytes=original_bytes, original_binary_prefix_sha256=original_binary_sha, original_morphs=[dict(mesh=node.get('name'), names=original_names[id(mesh)], weights=original_weights[id(mesh)]) for node, mesh in selected], clips=tracks, targets=targets, reports=reports, scope='Added classical cloth contact morphs; source base mesh, maps, UVs and native59-joint binds preserved. No rendered fit acceptance.')
    output.with_suffix('.contact.json').write_text(json.dumps(proof, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    args = parser.parse_args()
    bake(json.loads(args.manifest.read_text()))
