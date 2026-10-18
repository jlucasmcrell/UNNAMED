"""Standard garment rebind to the fixed runtime rig; original geometry bytes kept.

Blender's glTF roundtrip slightly changes bone bases. Canonical inverse binds and
joint frames are taken from the approved avatar, never fitted in the other direction.
"""
from pathlib import Path
import copy
import re
import hashlib
import json
import sys
import numpy as np
from frontier_region_masks import REGIONS
from scipy.spatial.transform import Rotation
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from glb import Glb

root=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
fit=sys.argv[1] if len(sys.argv)>1 else 'veth_f'
body_id='mh_'+fit
body_path=root/'approved_rig_extensions'/body_id/(body_id+'_rigged.glb')
body_hash=hashlib.sha256(body_path.read_bytes()).hexdigest()
body=Glb(body_path)

def frames(doc):
    parents={child:index for index,node in enumerate(doc['nodes']) for child in node.get('children',[])}
    cache={}
    def world(index):
        if index not in cache:
            node=doc['nodes'][index]
            if 'matrix' in node:
                local=np.array(node['matrix']).reshape(4,4).T
            else:
                local=np.eye(4)
                local[:3,:3]=Rotation.from_quat(node.get('rotation',[0,0,0,1])).as_matrix()@np.diag(node.get('scale',[1,1,1]))
                local[:3,3]=node.get('translation',[0,0,0])
            cache[index]=(world(parents[index])@local) if index in parents else local
        return cache[index]
    return parents,world

_,body_world=frames(body.doc)
canonical={body.doc['nodes'][index]['name']:body_world(index) for skin in body.doc['skins'] for index in skin['joints']}
binds={}
for skin in body.doc['skins']:
    for index,matrix in zip(skin['joints'],body.read(skin['inverseBindMatrices'])):
        name=body.doc['nodes'][index]['name']
        if name in binds:
            assert np.array_equal(binds[name],matrix)
        binds[name]=matrix
specs={
 'base_shirt':('frontier_linen_shirt',1,1,['belly','pelvis','glutes','upper_chest','back','upper_chest_top','back_top','neck','armpit_l','armpit_r','upperarm_l','upperarm_r']),
 'trousers':('frontier_wool_trousers',2,4,['belly','pelvis','glutes','thigh_l','thigh_r','calf_l','calf_r','ankle_l','ankle_r']),
 'vest':('frontier_sealed_vest',3,8,['belly','upper_chest','back','upper_chest_top','back_top','armpit_l','armpit_r']),
 'gloves':('frontier_smith_gloves',4,32,['hand_l','hand_r','forearm_l','forearm_r']),
 'boots':('frontier_sealed_boots',4,2,['foot_l','foot_r','ankle_l','ankle_r','calf_l','calf_r']),
 'standing_cap':('frontier_standing_cap',3,16,['head']),
 'lame_coif':('frontier_lame_coif',5,256,['head']),
 'lamework_jack':('frontier_lamework_jack',5,128,['belly','upper_chest','back','upper_chest_top','back_top']),
 'lame_sabatons':('frontier_lame_sabatons',5,512,['foot_l','foot_r']),
 # A loose cloak must expose the body/lower clothes when it swings away.
 'cloak':('frontier_wool_cloak',6,0,['back','back_top'])}

def append(glb,values,kind):
    values=np.asarray(values,dtype='<f4')
    glb.bin.extend(b'\0'*(-len(glb.bin)%4))
    offset=len(glb.bin)
    raw=values.tobytes()
    glb.bin.extend(raw)
    view=len(glb.doc['bufferViews'])
    glb.doc['bufferViews'].append({'buffer':0,'byteOffset':offset,'byteLength':len(raw)})
    accessor=len(glb.doc['accessors'])
    glb.doc['accessors'].append({'bufferView':view,'componentType':5126,'type':kind,'count':len(values)})
    return accessor

reports=[]
entries=[(part,base,layer,root/'sources/fits'/fit/part/(base+'_native.glb'),None)
         for part,(base,layer,_,_) in specs.items()]
for path in (root/'combined_sources'/fit).glob('*_native.glb'):
    base=path.name.removesuffix('_native.glb')
    entries.append((base,base,1,path,json.loads(path.with_name(base+'.json').read_text())['hide_regions']))
for part,base,layer,source,hidden in entries:
    if not source.exists():
        continue
    garment=Glb(source)
    old_bytes=bytes(garment.bin)
    assert not garment.doc.get('animations'),'Garments must not replace accepted animation'
    parents,world=frames(garment.doc)
    joint_indices={index for skin in garment.doc['skins'] for index in skin['joints']}
    for skin in garment.doc['skins']:
        for index,matrix in zip(skin['joints'],garment.read(skin['inverseBindMatrices'])):
            name=garment.doc['nodes'][index]['name']
            if name not in canonical:
                assert name.startswith('cloth_'),('Missing anatomy joint',source,name)
                canonical[name]=world(index).copy()
                binds[name]=matrix.copy()
    original_frame_error=0.
    for index in joint_indices:
        node=garment.doc['nodes'][index]
        name=node['name']
        assert name in canonical,(source,name)
        original_frame_error=max(original_frame_error,float(np.abs(canonical[name]-world(index)).max()))
        if index in parents:
            parent=parents[index]
            parent_name=garment.doc['nodes'][parent].get('name')
            parent_frame=canonical[parent_name] if parent in joint_indices else world(parent)
        else:
            parent_frame=np.eye(4)
        local=np.linalg.inv(parent_frame)@canonical[name]
        for key in ('translation','rotation','scale'):
            node.pop(key,None)
        node['matrix']=local.T.flatten().tolist()
    assert original_frame_error<.001,(source,original_frame_error)
    for skin in garment.doc['skins']:
        matrices=[binds[garment.doc['nodes'][index]['name']] for index in skin['joints']]
        skin['inverseBindMatrices']=append(garment,matrices,'MAT4')
    _,check_world=frames(garment.doc)
    error=max(float(np.abs(check_world(index)-canonical[garment.doc['nodes'][index]['name']]).max()) for index in joint_indices)
    assert error<1e-10,(source,error)
    asset=base+'__'+body_id
    for node in garment.doc['nodes']:
        if node.get('name','').startswith(base):
            node['name']=re.sub(r'(_LOD[1-3])\.\d+$',r'\1',node['name']).replace(base,asset,1)
    garment.doc['buffers'][0]['byteLength']=len(garment.bin)
    assert garment.bin[:len(old_bytes)]==old_bytes,'Original garment vertex/weight/texture bytes changed'
    dest=root/'game_candidate/primary/rigged'/asset
    dest.mkdir(parents=True,exist_ok=True)
    garment.save(dest/(asset+'_rigged.glb'))
    if hidden is None:
        hidden=REGIONS[part]
    metadata={'schema_version':'1','id':asset,'asset_id':asset,'body':body_id,'layer':layer,'hides':[],'hides_mask':0,
              'hide_regions':hidden,
              'chains':{'cloth_cape':{'stiffness':.8,'drag':.5,'gravity':.8,'radius':.075}} if part=='cloak' and any(n.get('name','').startswith('cloth_cape') for n in garment.doc['nodes']) else {},
              'provenance':{'construction':'Authored once on feminine Average; native Mesh Deform wrap and Data Transfer; fixed approved bodies; source PBR preserved',
                            'fixed_body_sha256':body_hash,'source':str(source)},
              'acceptance':'PENDING: personally viewed turnaround and run'}
    (dest/(asset+'_garment.json')).write_text(json.dumps(metadata,indent=2))
    reports.append({'asset':asset,'source_roundtrip_frame_delta':original_frame_error,'canonical_frame_delta':error,
                    'original_garment_binary_prefix_exact':True})
assert hashlib.sha256(body_path.read_bytes()).hexdigest()==body_hash
(root/'review'/('staged_garments_'+fit+'.json')).write_text(json.dumps(reports,indent=2))
print('STAGED_CANONICAL_GARMENTS',fit,len(reports))
