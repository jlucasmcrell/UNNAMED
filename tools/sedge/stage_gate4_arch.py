"""Fresh accepted primary -> high deck/chains and exact existing NPC looks. Args: BASE OUTPUT FALLBACK."""
from pathlib import Path
import json,copy,shutil,math,sys
import numpy as np
import trimesh
base,out,fallback=map(Path,sys.argv[1:4])
shutil.copytree(base,out)

d=json.loads((out/'dressing.json').read_text());places=d['places']
# Reuse accepted 4m boards at their authored size. Two high bridge remnants
# grow out of the carved abutments; suspension links terminate at the ribs.
for side in (-1,1):
 for j in range(10):
  places.append(dict(model='sedge_review_walk',at=[5000+side*(32+j*4),30],y=72,scale=[1,1,1],rotation_deg=[0,90,0],authored_transform=True))
mat=trimesh.visual.material.PBRMaterial(name='weathered_bronze_chain',baseColorFactor=[85,78,48,255],roughnessFactor=.82,metallicFactor=.55)
meshes=[]
for side in (-1,1):
 for z in (28.4,31.6):
  a=np.array([5000+side*29.8,72.,z]);b=np.array([5000+side*11.,124.,z]);vec=b-a;length=np.linalg.norm(vec);direction=vec/length
  for i in range(math.ceil(length/.55)):
   u=(i+.5)/math.ceil(length/.55);p=a+vec*u;p[1]-=math.sin(u*math.pi)*1.4
   # Elliptical interlocked rings, alternating 90 degrees around the line.
   g=trimesh.creation.torus(major_radius=.28,minor_radius=.045,major_sections=14,minor_sections=6);g.apply_scale([1,1.28,1])
   # Torus default normal Z; its plane includes the chain's length axis.
   up=direction;normal=np.cross(up,[0,0,1]);normal/=np.linalg.norm(normal)
   if i%2:normal=np.cross(up,normal)
   x=np.cross(up,normal);tf=np.eye(4);tf[:3,:3]=np.column_stack((x,up,normal));tf[:3,3]=p-np.array([5000,0,30]);g.apply_transform(tf);meshes.append(g)
g=trimesh.util.concatenate(meshes);g.visual=trimesh.visual.TextureVisuals(uv=g.vertices[:,[0,1]],material=mat)
s=trimesh.Scene(g);p=out/'ready/sedge_arch_suspension';p.mkdir(exist_ok=True);s.export(p/'sedge_arch_suspension.glb',include_normals=True)
places.append(dict(model='sedge_arch_suspension',at=[5000,30],y=0,authored_transform=True))
(out/'dressing.json').write_text(json.dumps(d,indent=2))
# Exact existing people/garments; copy only a look, never body geometry/rest.
basebindings=json.loads((fallback/'bindings.json').read_text());bindings=json.loads((out/'art_bindings.json').read_text());bindings.setdefault('people',{})
for key,people,sex in [('nessa_reed','veth','f'),('oren_pin','veth','m'),('sella_tally','siann','f'),('iren_orr','orenth','m'),('keeper_of_names','mor','f'),('tess_ferry','veth','f')]:
 candidates=basebindings['resident_people']['people.'+people]
 candidate=next((k for k in candidates if (basebindings['people'][k].get('outfit_body') or basebindings['people'][k]['model']).endswith('_'+sex)),candidates[0])
 bindings['people']['npc.sedge.'+key]=copy.deepcopy(basebindings['people'][candidate])
(out/'art_bindings.json').write_text(json.dumps(bindings,indent=2));print(out,len(places),len(meshes))
