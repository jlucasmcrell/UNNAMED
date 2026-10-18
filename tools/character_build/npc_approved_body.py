"""Native identity module: retain the approved player's body, skin and rig bytes.
Add native identity meshes/joints only; never rebuild or reshape player anatomy.
"""
import json,struct,copy,hashlib,shutil,argparse
from pathlib import Path
ap=argparse.ArgumentParser();ap.add_argument('--id',required=True);ap.add_argument('--source-id');ap.add_argument('--sex',choices=['m','f'],required=True);ap.add_argument('--workspace',type=Path,required=True);ap.add_argument('--player-root',type=Path,required=True);args=ap.parse_args()
w=args.workspace
rid=args.id;source_id=args.source_id or rid
player=(args.player_root/'rigged')/('mh_veth_'+args.sex)/('mh_veth_'+args.sex+'_rigged.glb')
native=w/'assembled'/('mh_'+source_id)/('mh_'+source_id+'_rigged.glb')
def read(p):
 b=p.read_bytes();n=struct.unpack_from('<I',b,12)[0];j=json.loads(b[20:20+n]);off=20+n;size=struct.unpack_from('<I',b,off)[0];return j,b[off+8:off+8+size]
j,bb=read(player);s,sb=read(native);original=copy.deepcopy(j)
keys=['accessors','bufferViews','images','textures','samplers','materials','meshes','skins']
off={k:len(j.get(k,[])) for k in keys};bo=len(bb);assert bo%4==0
names={j['nodes'][i]['name']:i for skin in j['skins'] for i in skin['joints']}
root=j['scenes'][0]['nodes'][0];sr=s['scenes'][0]['nodes'][0]
# Keep all approved body LODs and shadow, remove only the old identity meshes.
removed={i for i,n in enumerate(j['nodes']) if 'mesh' in n and any(x in n.get('name','') for x in ['_head','_hair','_eyebrows'])}
for i in removed:
 j['nodes'][i].pop('mesh',None);j['nodes'][i].pop('skin',None)
for n in j['nodes']:
 if 'children'in n:n['children']=[i for i in n['children'] if i not in removed]
keep={i for sk in s['skins'] for i in sk['joints']}
keep.update(i for i,n in enumerate(s['nodes']) if 'mesh'in n and any(x in n.get('name','') for x in ['_head_LOD','_hair_group','_eyebrows_group']))
mapping={sr:root};new=[]
for i in sorted(keep):
 n=s['nodes'][i]
 if 'mesh'not in n and n.get('name')in names and not n.get('name','').startswith('FACIAL_'):mapping[i]=names[n['name']]
 else:mapping[i]=len(j['nodes'])+len(new);new.append(i)
for i in new:
 n=copy.deepcopy(s['nodes'][i]);n.pop('children',None)
 if n.get('name') in names:n['name']='identity_'+n['name']
 for key,arr in [('mesh','meshes'),('skin','skins')]:
  if key in n:n[key]+=off[arr]
 j['nodes'].append(n)
for i,n in enumerate(s['nodes']):
 if i not in mapping:continue
 target=j['nodes'][mapping[i]]
 for c in n.get('children',[]):
  if c in mapping and c in new:
   target.setdefault('children',[]).append(mapping[c])
def texture_refs(x):
 if isinstance(x,dict):
  for k,v in x.items():
   if k.endswith('Texture')and isinstance(v,dict)and 'index'in v:v['index']+=off['textures']
   else:texture_refs(v)
 elif isinstance(x,list):
  for v in x:texture_refs(v)
for key in keys:
 for item in s.get(key,[]):
  v=copy.deepcopy(item)
  if key=='bufferViews':v['byteOffset']=v.get('byteOffset',0)+bo;v['buffer']=0
  elif key=='accessors':
   if 'bufferView'in v:v['bufferView']+=off['bufferViews']
   if 'sparse'in v:
    for x in ['indices','values']:v['sparse'][x]['bufferView']+=off['bufferViews']
  elif key=='images':
   if 'bufferView'in v:v['bufferView']+=off['bufferViews']
  elif key=='textures':
   if 'source'in v:v['source']+=off['images']
   if 'sampler'in v:v['sampler']+=off['samplers']
  elif key=='materials':texture_refs(v)
  elif key=='meshes':
   for p in v['primitives']:
    p['attributes']={k:i+off['accessors'] for k,i in p['attributes'].items()}
    if 'indices'in p:p['indices']+=off['accessors']
    if 'material'in p:p['material']+=off['materials']
    for t in p.get('targets',[]):
     for k in t:t[k]+=off['accessors']
  elif key=='skins':
   v['joints']=[mapping[i] for i in v['joints']]
   if 'skeleton'in v:v['skeleton']=mapping[v['skeleton']]
   if 'inverseBindMatrices'in v:v['inverseBindMatrices']+=off['accessors']
  j.setdefault(key,[]).append(v)
for k in ['extensionsUsed','extensionsRequired']:
 if k in s:j[k]=sorted(set(j.get(k,[])+s[k]))
bin=bb+sb;j['buffers']=[{'byteLength':len(bin)}]
# Original body mesh attributes, skin, inverse bind matrices, materials and buffers remain verbatim.
assert j['skins'][0]==original['skins'][0]
assert [{k:v for k,v in n.items() if k!='children'} for n in [j['nodes'][i] for i in original['skins'][0]['joints']]]==[{k:v for k,v in n.items() if k!='children'} for n in [original['nodes'][i] for i in original['skins'][0]['joints']]]
assert j['meshes'][:len(original['meshes'])]==original['meshes']
assert bin[:len(bb)]==bb
from gltf_active_resources import prune
prune(j)
assert j['meshes'][:4]==original['meshes'][:4]
assert j['materials'][0]==original['materials'][0]
jb=json.dumps(j,separators=(',',':')).encode();jb+=b' '*((-len(jb))%4);bin+=b'\0'*((-len(bin))%4)
out=w/'approved_models'/('mh_'+rid);out.mkdir(parents=True,exist_ok=True)
p=out/('mh_'+rid+'_rigged.glb');p.write_bytes(struct.pack('<III',0x46546c67,2,28+len(jb)+len(bin))+struct.pack('<II',len(jb),0x4e4f534a)+jb+struct.pack('<II',len(bin),0x004e4942)+bin)
(out/'source_integrity.json').write_text(json.dumps({'player':str(player),'player_sha256':hashlib.sha256(player.read_bytes()).hexdigest(),'native_face':str(native),'body_mesh_skin_inverse_bind_material_binary_unchanged':True,'original_player_joint_nodes_unchanged':len(original['skins'][0]['joints']),'addition':'Native identity meshes and extra joints only; original body base layer kept; no wings/harness','status':'exact approved-body method; paired visual and clothing review required','uniform_body_scale_only':True,'body_preset_build_or_measurement_changes':False,'face_eye_binds_separate_from_player_eye_helpers':True},indent=2))
shutil.copytree(player.parent/'textures',out/'textures',dirs_exist_ok=True)
companion=player.parent/player.name.replace('_rigged.glb','_correctives.json')
if not companion.exists():raise RuntimeError('Approved player corrective companion missing')
shutil.copy2(companion,out/p.name.replace('_rigged.glb','_correctives.json'))
print(p)
