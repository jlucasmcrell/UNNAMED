"""Add import-only bone-marker geometry to owned CMU animation-only glTF files.

Animation buffers and source rest transforms remain byte-for-byte intact. The
archive uses centimetres; one container scale converts it to glTF metres.
"""
import argparse
import json
from pathlib import Path
import struct
import zipfile
import numpy as np
from audit_humgen_transport import Glb

def prepare(archive, output):
    archive, output = archive.resolve(), output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    with zipfile.ZipFile(archive) as source:
        for member in source.namelist():
            if not member.lower().endswith('.glb'):
                continue
            identifier = 'cmu_' + Path(member).stem
            receipt = output / (identifier + '.json')
            if receipt.exists():
                row = json.loads(receipt.read_text())
                row['path'] = str(output / (identifier + '.glb'))
                native = Glb(output / (identifier + '_original.glb')).globals()
                pelvis = next(name for name in native if name.lower() == 'hip')
                row['source_root_offset_cm'] = float(native[pelvis][1,3] - min(matrix[1,3] for name,matrix in native.items() if name.lower().endswith('foot')))
                receipt.write_text(json.dumps(row,indent=2),encoding='utf-8')
                rows.append(row)
                continue
            data = source.read(member)
            length = struct.unpack_from('<I', data, 12)[0]
            document = json.loads(data[20:20+length])
            binary = bytearray(data[28+length:])
            skin = document['skins'][0]
            assert not document.get('meshes'), 'Use the original source mesh when one exists'
            # Read native rest/first-frame transforms through the existing glTF sampler.
            original = output / (identifier + '_original.glb')
            original.write_bytes(data)
            glb = Glb(original)
            rest = glb.globals()
            hip = next(name for name in rest if name.lower() == 'hip')
            vertices, joints, weights = [], [], []
            for joint, node in enumerate(skin['joints']):
                centre = rest[document['nodes'][node]['name']][:3, 3]
                for offset in ((-.5,0,0),(.5,0,0),(0,0,1)):
                    vertices.append(centre + np.array(offset))
                    joints.append((joint,0,0,0))
                    weights.append((1,0,0,0))
            def accessor(array, component, kind):
                while len(binary) % 4: binary.append(0)
                start = len(binary)
                binary.extend(array.tobytes())
                view = len(document['bufferViews'])
                document['bufferViews'].append(dict(buffer=0, byteOffset=start, byteLength=array.nbytes))
                index = len(document['accessors'])
                item = dict(bufferView=view, componentType=component, count=len(array), type=kind)
                if kind == 'VEC3':
                    item.update(min=array.min(axis=0).tolist(), max=array.max(axis=0).tolist())
                document['accessors'].append(item)
                return index
            positions = accessor(np.array(vertices,dtype='<f4'),5126,'VEC3')
            bone_indices = accessor(np.array(joints,dtype='<u2'),5123,'VEC4')
            bone_weights = accessor(np.array(weights,dtype='<f4'),5126,'VEC4')
            indices = accessor(np.arange(len(vertices),dtype='<u2'),5123,'SCALAR')
            document['meshes'] = [dict(primitives=[dict(attributes=dict(POSITION=positions,JOINTS_0=bone_indices,WEIGHTS_0=bone_weights),indices=indices)])]
            container = document['scenes'][0]['nodes'][0]
            document['nodes'][container]['scale'] = [.01,.01,.01]
            # Add a real source root joint; existing joint indices and keys stay unchanged.
            root = len(document['nodes'])
            document['nodes'].append(dict(name='root', children=list(document['nodes'][container]['children'])))
            document['nodes'][container]['children'] = [root, root+1]
            document['nodes'].append(dict(name='NativeSourceMarkers',mesh=0,skin=0))
            skin['joints'].append(root)
            inverse = glb.accessor(skin['inverseBindMatrices'])
            skin['inverseBindMatrices'] = accessor(np.vstack([inverse,np.eye(4).reshape(1,16)]).astype('<f4'),5126,'MAT4')
            while len(binary) % 4: binary.append(0)
            document['buffers'][0]['byteLength'] = len(binary)
            encoded = json.dumps(document,separators=(',',':')).encode()
            encoded += b' ' * (-len(encoded)%4)
            target = output / (identifier+'.glb')
            target.write_bytes(struct.pack('<III',0x46546c67,2,28+len(encoded)+len(binary))
                + struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary)
            row = dict(id=identifier,source=str(archive)+'!'+member,path=str(target),status='SOURCE_EXPORTED_NOT_RETARGETED',
                source_root_offset_cm=float(rest[hip][1,3] - min(matrix[1,3] for name,matrix in rest.items() if name.lower().endswith('foot'))),
                takes=[a.get('name') for a in document['animations']])
            receipt.write_text(json.dumps(row,indent=2),encoding='utf-8')
            rows.append(row)
    (output/'inventory.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
    print('CMU_SOURCES_PREPARED',len(rows))

if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('archive',type=Path)
    parser.add_argument('output',type=Path)
    args=parser.parse_args()
    prepare(args.archive,args.output)
