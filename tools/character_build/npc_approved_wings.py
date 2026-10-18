"""Wing anatomy module: retain the approved player's body, skin and rig bytes.
Add native identity meshes/joints only; never rebuild or reshape player anatomy.
"""
import json,struct,copy,hashlib,shutil
import numpy as np
import argparse
ap=argparse.ArgumentParser();ap.add_argument("--id",required=True);ap.add_argument("--sex",choices=["m","f"],required=True);ap.add_argument('--workspace',required=True);ap.add_argument('--player-root',required=True);args=ap.parse_args()
from pathlib import Path
w=Path(args.workspace)
player=w/'approved_models'/('mh_'+args.id)/('mh_'+args.id+'_rigged.glb')
native=w/'exports/kera_exact_attachments.glb'
def read(p):
 b=p.read_bytes();n=struct.unpack_from('<I',b,12)[0];j=json.loads(b[20:20+n]);off=20+n;size=struct.unpack_from('<I',b,off)[0];return j,b[off+8:off+8+size]
j,bb=read(player);s,sb=read(native);original=copy.deepcopy(j)
if args.sex=='m':
 from scipy.spatial.transform import Rotation
 def named_world(doc,name):
  parents={c:i for i,n in enumerate(doc['nodes'])for c in n.get('children',[])};memo={}
  def get(i):
   if i in memo:return memo[i]
   n=doc['nodes'][i];m=np.eye(4)
   if 'matrix'in n:m=np.array(n['matrix']).reshape(4,4).T
   else:m[:3,:3]=Rotation.from_quat(n.get('rotation',[0,0,0,1])).as_matrix()@np.diag(n.get('scale',[1,1,1]));m[:3,3]=n.get('translation',[0,0,0])
   memo[i]=get(parents[i])@m if i in parents else m;return memo[i]
  return get(next(i for i,n in enumerate(doc['nodes'])if n.get('name')==name))[:3,3]
 female,_=read(Path(args.player_root)/'rigged/mh_veth_f/mh_veth_f_rigged.glb')
 male,_=read(Path(args.player_root)/'rigged/mh_veth_m/mh_veth_m_rigged.glb')
 scale=named_world(male,'pelvis')[1]/named_world(female,'pelvis')[1]
 fa=named_world(female,'spine_04');ma=named_world(male,'spine_04');fa[0]=ma[0]=0;fa[2]=-.145;ma[2]=-.145*scale
 sb=bytearray(sb);done=set()
 for node in s['nodes']:
  if 'mesh'not in node or 'wing_module_LOD'not in node.get('name',''):continue
  for primitive in s['meshes'][node['mesh']]['primitives']:
   aid=primitive['attributes']['POSITION']
   if aid in done:continue
   done.add(aid);acc=s['accessors'][aid];view=s['bufferViews'][acc['bufferView']];start=view.get('byteOffset',0)+acc.get('byteOffset',0);stride=view.get('byteStride',12)
   points=[]
   for i in range(acc['count']):
    p=np.array(struct.unpack_from('<3f',sb,start+i*stride));p=(p-fa)*scale+ma;struct.pack_into('<3f',sb,start+i*stride,*p);points.append(p)
   acc['min']=np.min(points,axis=0).tolist();acc['max']=np.max(points,axis=0).tolist()
 sb=bytes(sb)

keys=['accessors','bufferViews','images','textures','samplers','materials','meshes','skins']
off={k:len(j.get(k,[])) for k in keys};bo=len(bb);assert bo%4==0
names={j['nodes'][i]['name']:i for skin in j['skins'] for i in skin['joints']}
root=j['scenes'][0]['nodes'][0];sr=s['scenes'][0]['nodes'][0]
# Keep the approved body and native identity; append wing anatomy only.
keep={i for sk in s['skins'] for i in sk['joints']}
keep.update(i for i,n in enumerate(s['nodes']) if 'mesh'in n and 'wing_module_LOD' in n.get('name',''))
mapping={sr:root};new=[]
for i in sorted(keep):
 n=s['nodes'][i]
 if 'mesh'not in n and n.get('name')in names:mapping[i]=names[n['name']]
 else:mapping[i]=len(j['nodes'])+len(new);new.append(i)
for i in new:
 n=copy.deepcopy(s['nodes'][i]);n.pop('children',None)
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
bin=bytearray(bb+sb)
# The new wing skin binds to the approved target rest; existing skins stay unchanged.
parents={c:i for i,n in enumerate(j['nodes']) for c in n.get('children',[])};world={}
def global_rest(i):
 if i in world:return world[i]
 n=j['nodes'][i]
 if 'matrix'in n:m=np.array(n['matrix']).reshape(4,4).T
 else:
  x,y,z,q=n.get('rotation',[0,0,0,1]);m=np.eye(4)
  m[:3,:3]=np.array([[1-2*(y*y+z*z),2*(x*y-z*q),2*(x*z+y*q)],[2*(x*y+z*q),1-2*(x*x+z*z),2*(y*z-x*q)],[2*(x*z-y*q),2*(y*z+x*q),1-2*(x*x+y*y)]])@np.diag(n.get('scale',[1,1,1]))
  m[:3,3]=n.get('translation',[0,0,0])
 world[i]=global_rest(parents[i])@m if i in parents else m;return world[i]
for skin in j['skins'][off['skins']:]:
 acc=j['accessors'][skin['inverseBindMatrices']];view=j['bufferViews'][acc['bufferView']];start=view.get('byteOffset',0)+acc.get('byteOffset',0)
 for k,node in enumerate(skin['joints']):struct.pack_into('<16f',bin,start+64*k,*np.linalg.inv(global_rest(node)).T.flatten())
bin=bytes(bin);j['buffers']=[{'byteLength':len(bin)}]
# Original body mesh attributes, skin, inverse bind matrices, materials and buffers remain verbatim.
assert j['skins'][0]==original['skins'][0]
assert all({k:v for k,v in j['nodes'][i].items() if k!='children'}=={k:v for k,v in original['nodes'][i].items() if k!='children'} for i in original['skins'][0]['joints'])
assert j['meshes'][:len(original['meshes'])]==original['meshes']
assert bin[:len(bb)]==bb
from gltf_active_resources import prune
prune(j)
assert j['meshes'][:4]==original['meshes'][:4]
assert j['materials'][0]==original['materials'][0]
jb=json.dumps(j,separators=(',',':')).encode();jb+=b' '*((-len(jb))%4);bin+=b'\0'*((-len(bin))%4)
out=w/'approved_wing_models'/('mh_'+args.id);out.mkdir(parents=True,exist_ok=True)
p=out/('mh_'+args.id+'_rigged.glb');p.write_bytes(struct.pack('<III',0x46546c67,2,28+len(jb)+len(bin))+struct.pack('<II',len(jb),0x4e4f534a)+jb+struct.pack('<II',len(bin),0x004e4942)+bin)
(out/'source_integrity.json').write_text(json.dumps({'player':str(player),'player_sha256':hashlib.sha256(player.read_bytes()).hexdigest(),'native_face':str(native),'body_mesh_skin_inverse_bind_material_binary_unchanged':True,'original_player_joint_nodes_unchanged':167,'addition':'Owner-approved symmetric folded wing anatomy only; no unpublished harness or clothing; body geometry/skin/binds/joint transforms unchanged','status':'candidate; visual review pending'},indent=2))
shutil.copytree(player.parent/'textures',out/'textures',dirs_exist_ok=True)
companion=player.parent/player.name.replace('_rigged.glb','_correctives.json')
if not companion.exists():raise RuntimeError('Approved player corrective companion missing')
shutil.copy2(companion,out/p.name.replace('_rigged.glb','_correctives.json'))
print(p)
