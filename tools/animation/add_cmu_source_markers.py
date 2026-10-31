"""Make an animation-only glTF importable by Epic without touching its recorded channels or rig.

Tiny source-only witness triangles reference the acquired unused skin. This is the
same technical role as the CMU bow import's preview markers; none is shipped as art.
"""
import copy
import hashlib
from pathlib import Path
import sys

import numpy as np
from scipy.spatial.transform import Rotation

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'character_build'))
from glb import Glb


def prepare(path):
    source = Glb(path)
    original_nodes = copy.deepcopy(source.doc['nodes'])
    original_skin = copy.deepcopy(source.doc['skins'])
    original_animation = copy.deepcopy(source.doc['animations'])
    original_bytes = bytes(source.bin)
    parents = {c: i for i, n in enumerate(source.doc['nodes']) for c in n.get('children', [])}

    def world(index):
        node = source.doc['nodes'][index]
        if 'matrix' in node:
            local = np.array(node['matrix']).reshape(4,4).T
        else:
            local = np.eye(4)
            local[:3,:3] = Rotation.from_quat(node.get('rotation',[0,0,0,1])).as_matrix() @ np.diag(node.get('scale',[1,1,1]))
            local[:3,3] = node.get('translation',[0,0,0])
        return world(parents[index]) @ local if index in parents else local

    skin = source.doc['skins'][0]
    binds = source.read(skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
    mesh_world = world(skin['joints'][0]) @ binds[0]
    inverse = np.linalg.inv(mesh_world)
    positions, indices = [], []
    for bone, node in enumerate(skin['joints']):
        assert np.allclose(world(node) @ binds[bone], mesh_world, atol=1e-4)
        centre = world(node)[:3,3]
        for offset in ((-.002,0,0),(.002,0,0),(0,.002,0)):
            positions.append((inverse @ np.r_[centre+offset,1])[:3])
            indices.append([bone,0,0,0])

    def add(values, dtype, kind, component, bounds=False):
        values = np.asarray(values,dtype=dtype)
        source.bin.extend(b'\0'*(-len(source.bin)%4))
        offset = len(source.bin);source.bin.extend(values.tobytes())
        view = len(source.doc['bufferViews'])
        source.doc['bufferViews'].append({'buffer':0,'byteOffset':offset,'byteLength':values.nbytes})
        accessor = {'bufferView':view,'componentType':component,'count':len(values),'type':kind}
        if bounds: accessor.update(min=values.min(0).tolist(),max=values.max(0).tolist())
        source.doc['accessors'].append(accessor)
        return len(source.doc['accessors'])-1

    attributes = {'POSITION':add(positions,'<f4','VEC3',5126,True),
                  'JOINTS_0':add(indices,'<u2','VEC4',5123),
                  'WEIGHTS_0':add([[1,0,0,0]]*len(positions),'<f4','VEC4',5126)}
    mesh = len(source.doc.setdefault('meshes',[]))
    source.doc['meshes'].append({'name':'CMU_source_witness_DO_NOT_SHIP','primitives':[{'attributes':attributes}]})
    node = len(source.doc['nodes'])
    source.doc['nodes'].append({'name':'CMU_source_witness_DO_NOT_SHIP','mesh':mesh,'skin':0,'matrix':mesh_world.T.reshape(-1).tolist()})
    source.doc['scenes'][source.doc.get('scene',0)]['nodes'].append(node)
    source.doc['buffers'][0]['byteLength'] = len(source.bin)
    output = path.with_name(path.stem+'_source_preview.glb')
    source.save(output)
    check = Glb(output)
    assert check.doc['nodes'][:len(original_nodes)] == original_nodes
    assert check.doc['skins'] == original_skin and check.doc['animations'] == original_animation
    assert bytes(check.bin[:len(original_bytes)]) == original_bytes
    print(path.name, 'source-only markers; original rig and every recorded channel retained', hashlib.sha256(output.read_bytes()).hexdigest())


if __name__ == '__main__':
    for name in sys.argv[1:]: prepare(Path(name))
