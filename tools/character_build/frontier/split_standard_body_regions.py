"""Partition the native skin into ordinary whole-region meshes. Keep native vertex/skin/morph buffers."""
import argparse,copy,json,shutil,sys,os
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from glb import Glb
root=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
target=Path(os.environ.get('OTHERREACH_REGION_CANDIDATE',str(root/'region_candidate')))/'primary/rigged'
parser=argparse.ArgumentParser()
parser.add_argument('fits',nargs='*')
parser.add_argument('--source-root',type=Path,default=root/'approved_rig_extensions')
parser.add_argument('--target-root',type=Path,default=target)
args=parser.parse_args()
regions=['head_neck','head_neck','torso','torso','torso','hips','hips',
         'upper_arms','upper_arms','forearms','forearms','hands','hands',
         'thighs','thighs','calves','calves','feet','feet','torso','torso',
         'torso','torso','feet','feet']
for fit in args.fits or ['mh_veth_f','mh_veth_m','mh_veth_f_lean','mh_veth_m_lean']:
    source=args.source_root/fit/(fit+'_rigged.glb')
    dest=args.target_root/fit;shutil.copytree(source.parent,dest,dirs_exist_ok=True)
    glb=Glb(source);original=bytes(glb.bin)
    for node in list(glb.doc['nodes']):
        name=node.get('name','');base=name.split('_LOD')[0]
        if base.endswith(('_hair','_hair_group')):
            node['name']=base+'__region_hair'+name[len(base):]
            continue
        if 'mesh' not in node or not base.endswith(('_body','_head','_shadow')):continue
        mesh=glb.doc['meshes'][node['mesh']];by_region={}
        # Native NPC identity heads stay whole; only approved body surfaces
        # carry the shared body-region atlas. These slots never hide a face.
        if any('TEXCOORD_1' not in p['attributes'] for p in mesh['primitives']):continue
        for primitive in mesh['primitives']:
            uv=glb.read(primitive['attributes']['TEXCOORD_1'])
            labels=np.array([regions[int(x)] for x in uv[:,0]])
            indices=glb.read(primitive['indices']).astype(int).reshape(-1,3)
            parts=labels[indices]
            labels=np.where(parts[:,1]==parts[:,2],parts[:,1],parts[:,0])
            for region in np.unique(labels):
                selected=indices[labels==region].astype('<u4').ravel()
                glb.bin.extend(b'\0'*(-len(glb.bin)%4));offset=len(glb.bin);raw=selected.tobytes();glb.bin.extend(raw)
                view=len(glb.doc['bufferViews']);glb.doc['bufferViews'].append({'buffer':0,'byteOffset':offset,'byteLength':len(raw),'target':34963})
                accessor=len(glb.doc['accessors']);glb.doc['accessors'].append({'bufferView':view,'componentType':5125,'type':'SCALAR','count':len(selected)})
                part=copy.deepcopy(primitive);part['indices']=accessor
                by_region.setdefault(str(region),[]).append(part)
        for region,primitives in by_region.items():
            newmesh=copy.deepcopy(mesh);newmesh['primitives']=primitives
            newname=base.removesuffix('_shadow')+'__region_'+region+('_shadow' if base.endswith('_shadow') else name[len(base):])
            newmesh['name']=newname
            child={'name':newname,'mesh':len(glb.doc['meshes']),'skin':node['skin']}
            if 'weights' in node:child['weights']=node['weights']
            glb.doc['meshes'].append(newmesh)
            node.setdefault('children',[]).append(len(glb.doc['nodes']));glb.doc['nodes'].append(child)
        node.pop('mesh');node.pop('skin');node.pop('weights',None)
    glb.doc['buffers'][0]['byteLength']=len(glb.bin)
    assert glb.bin[:len(original)]==original,'Approved body buffers must remain unchanged'
    glb.save(dest/source.name)
    print('STANDARD_BODY_REGIONS',fit)
