"""Remove the old non-identity native rest-fit from delivered robe shape deltas.

The four eye controls cannot affect this neck-down garment. Old exports put
the same rest-fit in all four and every b_/s_ control. Require their agreement
before repairing; correctives, base geometry, rigs, UVs and textures stay exact.
Fresh exports use keep_humgen_clothing_keys.py's corrected reference instead.
"""
import json, struct, sys
from pathlib import Path
import numpy as np

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'animation'))
from audit_humgen_transport import Glb

def values(g,index):
    a=g.doc['accessors'][index]
    result=g.accessor(index).copy() if 'bufferView' in a else np.zeros((a['count'],3))
    if 'sparse' in a:
        s=a['sparse'];i=s['indices'];v=s['values']
        iv=g.doc['bufferViews'][i['bufferView']];vv=g.doc['bufferViews'][v['bufferView']]
        ids=np.frombuffer(g.binary,dtype={5121:'u1',5123:'<u2',5125:'<u4'}[i['componentType']],count=s['count'],offset=iv.get('byteOffset',0)+i.get('byteOffset',0))
        result[ids]=np.frombuffer(g.binary,dtype='<f4',count=s['count']*3,offset=vv.get('byteOffset',0)+v.get('byteOffset',0)).reshape((-1,3))
    return result

def main(path):
    g=Glb(path);binary=bytearray(g.binary);rows=[]
    for m in g.doc['meshes']:
        names=m.get('extras',{}).get('targetNames',[])
        for p in m['primitives']:
            eyes=[t for n,t in zip(names,p.get('targets',[])) if n.startswith('s_{eyes}')]
            assert len(eyes)==4,'Repair requires all four independent eye controls'
            reference={attr:values(g,index) for attr,index in eyes[0].items()}
            for eye in eyes:
                assert eye.keys()==reference.keys()
                for attr in reference:assert np.array_equal(values(g,eye[attr]),reference[attr]),'Eye controls disagree: not the known rest-fit fault'
            count=0
            for name,target in zip(names,p['targets']):
                if not name.startswith(('b_','s_')):continue
                for attr in target:
                    fixed=(values(g,target[attr])-reference[attr]).astype('<f4')
                    binary.extend(b'\0'*(-len(binary)%4));offset=len(binary);binary.extend(fixed.tobytes())
                    view=len(g.doc['bufferViews']);g.doc['bufferViews'].append(dict(buffer=0,byteOffset=offset,byteLength=fixed.nbytes))
                    target[attr]=len(g.doc['accessors']);g.doc['accessors'].append(dict(bufferView=view,componentType=5126,count=len(fixed),type='VEC3',min=fixed.min(0).tolist(),max=fixed.max(0).tolist()))
                count+=1
            rows.append(dict(mesh=m['name'],controls=count,max_common_offset_m=float(np.linalg.norm(reference['POSITION'],axis=1).max())))
    g.doc['buffers'][0]['byteLength']=len(binary)
    encoded=json.dumps(g.doc,separators=(',',':')).encode();encoded+=b' '*(-len(encoded)%4)
    Path(path).write_bytes(struct.pack('<III',0x46546c67,2,28+len(encoded)+len(binary))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary)
    Path(path).with_suffix('.shape_reference.json').write_text(json.dumps(rows,indent=2))
    print('Repaired native robe shape references:',len(rows),'surfaces')

if __name__=='__main__':main(Path(sys.argv[1]))
