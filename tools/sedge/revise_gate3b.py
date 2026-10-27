"""Rejected Gate3b revision: existing audited kit geometry, original PBR maps."""
from pathlib import Path
import argparse,json,copy,shutil,math,hashlib
import numpy as np
import trimesh
from PIL import Image
parser=argparse.ArgumentParser()
parser.add_argument('--base',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--converted',type=Path,required=True)
args=parser.parse_args()
BASE,OUT,KIT=args.base,args.output,args.converted
assert BASE.resolve()!=OUT.resolve()
shutil.copytree(BASE,OUT,dirs_exist_ok=True)
places=json.loads((OUT/'dressing.json').read_text())['places']
places=[p for p in places if not p['model'].startswith(('sedge_arch_','sedge_belfry_'))]
credits=json.loads((OUT/'SEDGE_SOURCE_CREDITS.json').read_text())
def save(id,s):
 d=OUT/'ready'/id;d.mkdir(exist_ok=True,parents=True);s.export(d/(id+'.glb'),include_normals=True)
def load(id):return trimesh.load(KIT/id/(id+'.glb'),force='scene')
def add(dst,src,scale=(1,1,1),at=(0,0,0),yaw=0):
 tf=trimesh.transformations.rotation_matrix(math.radians(yaw),[0,1,0]);tf[:3,3]=at
 for n in src.graph.nodes_geometry:
  nt,key=src.graph[n];g=copy.deepcopy(src.geometry[key]);g.apply_transform(nt);g.apply_scale(scale);g.apply_transform(tf);dst.add_geometry(g)
def place(id,x,z,y=0,scale=(1,1,1),rot=(0,0,0)):
 places.append(dict(model=id,at=[x,z],y=y,scale=list(scale),rotation_deg=list(rot),authored_transform=True))
for id in ['sedge_arch_norse','sedge_arch_column','sedge_belfry_wall','sedge_arch_bridge']:
 shutil.copytree(KIT/id,OUT/'ready'/id,dirs_exist_ok=True)
 meta=json.loads((KIT/id/(id+'_meta.json')).read_text());credits['sources'].append(dict(id=id,source=meta['source'],sha256=meta['source_sha256'],credit=meta['pack'],licence=meta.get('licence','')))
# Seat source voussoirs against their neighbours instead of enlarging kit gaps.
hero=load('sedge_arch_norse')
for g in hero.geometry.values():
 _,ix,inv=np.unique(np.round(g.vertices,4),axis=0,return_index=True,return_inverse=True)
 proxy=trimesh.Trimesh(vertices=g.vertices[ix],faces=inv[g.faces],process=False)
 for component in trimesh.graph.connected_components(proxy.face_adjacency,nodes=np.arange(len(g.faces))):
  ids=np.unique(g.faces[component]);v=g.vertices[ids];centre=(v.max(axis=0)+v.min(axis=0))*.5;g.vertices[ids]=centre+(v-centre)*1.045
bronze=trimesh.visual.material.PBRMaterial(name='weathered_bronze_cramps',baseColorFactor=[125,105,62,255],metallicFactor=.65,roughnessFactor=.72)
for x,y in [(0,15.55),(-3.7,8.05),(3.7,8.05),(-3.7,4.1),(3.7,4.1)]:
 g=trimesh.creation.box([.30,.065,.12]);g.apply_translation([x,y,1.5]);g.visual=trimesh.visual.TextureVisuals(uv=g.vertices[:,[0,1]],material=bronze);hero.add_geometry(g)
save('sedge_arch_norse',hero)
# Crown is intrinsic to the source arch, never an unattached extra capital.
# 140m span / 128m rise; three separate shadowing ribs. Feet remain in old abutments.
for z in (10,30,50):place('sedge_arch_norse',5000,z,-.9,(15.5,8,3.4))
for side in (-1,1):
 for z in (12,46):place('sedge_arch_column',5000+side*69,z,-1,(6,6.7,3.2))
# Existing broken bridge kit provides a grounded fractured approach at each abutment.
for side in (-1,1):place('sedge_arch_bridge',5000+side*75,77,-1,(1.2,1.2,1.2),(0,side*20,0))
# Belfry shafts assembled from masonry wall modules, with physical slit openings.
wall=load('sedge_belfry_wall');arch=load('sedge_arch_norse');col=load('sedge_arch_column')
tower=trimesh.Scene()
for side in range(4):
 yaw=side*90;r=math.radians(yaw)
 def at(x,y):return (math.cos(r)*x+math.sin(r)*2.3,y,-math.sin(r)*x+math.cos(r)*2.3)
 add(tower,wall,(.78,.95,.65),at(0,-.8),yaw)
 # Two piers leave a narrow tall slit; lintel closes each window at y5.5.
 for x in (-1.38,1.38):add(tower,wall,(.32,1.25,.65),at(x,2),yaw)
 add(tower,wall,(.78,.30,.65),at(0,5.65),yaw)
 add(tower,arch,(.52,.43,.27),at(0,6.45),yaw)
for x in (-2.2,2.2):
 for z in (-2.2,2.2):add(tower,col,(.24,.73,.24),(x,-.8,z),0)
# Retain the real Cosmos bell/headstock/hanger from the last assembly.
old=trimesh.load(BASE/'ready/sedge_belfry_ruin/sedge_belfry_ruin.glb',force='scene')
for n in old.graph.nodes_geometry:
 tf,key=old.graph[n];g=old.geometry[key]
 if 'bell' in str(n).lower() or g.visual.material.name.startswith(('M_Bell','bell_iron','tarred_timber')):
  g=copy.deepcopy(g);g.apply_transform(tf);g.apply_translation([0,-1.35,0]);tower.add_geometry(g)
# A missing front crown section leaves a genuinely broken open top.
for g in tower.geometry.values():
 c=g.triangles_center;cut=(c[:,0]>.5)&(c[:,1]>12.15)&(c[:,2]>1.8);g.update_faces(~cut);g.remove_unreferenced_vertices()
save('sedge_belfry_ruin',tower)
for i,(x,z,s,rx,rz) in enumerate([(4940,160,1.7,5,-7),(5065,380,2.1,-6,5),(4930,680,1.65,8,-5),(5060,1420,1.85,-5,8),(4945,1740,1.6,7,5),(4802,1218,1.75,-8,-4)]):place('sedge_belfry_ruin',x,z,-1,(s,s,s),(rx,i*31,rz))
# Real crossboards: 35mm joints, staggered ends, iron nail heads and full depth piles.
w=Path(r'C:\Otherreach_External_Assets\Sourced_CC0_20261018\wood_misc_ground\wood_planks_grey\2k')
files=[w/('wood_planks_grey_'+k+'_2k.jpg') for k in ['diff','nor_gl','arm']]
wood=trimesh.visual.material.PBRMaterial(name='wet_grey_weathered_boards',baseColorTexture=Image.open(files[0]),normalTexture=Image.open(files[1]),metallicRoughnessTexture=Image.open(files[2]),baseColorFactor=[125,135,130,255],roughnessFactor=.78,metallicFactor=0)
iron=trimesh.visual.material.PBRMaterial(name='oxidized_nail_heads',baseColorFactor=[45,36,27,255],metallicFactor=.55,roughnessFactor=.7)
walk=trimesh.Scene()
def box(size,at,mat):
 g=trimesh.creation.box(size);g.apply_translation(at);g.unmerge_vertices();g.visual=trimesh.visual.TextureVisuals(uv=g.vertices[:,[0,2]]/[2.0,1.0],material=mat);walk.add_geometry(g)
for i in range(16):
 z=-2+(i+.5)*.25;box((3.58-(i%3)*.025,.16,.215),(0,-.08,z),wood)
 for x in (-1.4,1.4):
  for dz in (-.065,.065):
   g=trimesh.creation.cylinder(radius=.022,height=.012,sections=8);g.apply_transform(trimesh.transformations.rotation_matrix(math.pi/2,[1,0,0]));g.apply_translation([x,.003,z+dz]);g.visual=trimesh.visual.TextureVisuals(uv=g.vertices[:,[0,2]],material=iron);walk.add_geometry(g)
for x in (-1.45,1.45):box((.22,.3,4),(x,-.3,0),wood)
for x in (-1.83,1.83):
 for z in (-1.45,1.45):
  g=trimesh.creation.cylinder(radius=.115,height=3.7,sections=10);g.apply_transform(trimesh.transformations.rotation_matrix(math.pi/2,[1,0,0]));g.apply_translation([x,-1.65,z]);g.visual=trimesh.visual.TextureVisuals(uv=g.vertices[:,[0,1]]/[.7,2],material=wood);walk.add_geometry(g)
groups={}
for g in walk.geometry.values():groups.setdefault(g.visual.material.name,[]).append(g)
batched=trimesh.Scene()
for name,gs in groups.items():batched.add_geometry(trimesh.util.concatenate(gs),node_name=name)
save('sedge_review_walk',batched)
for f in files:credits['sources'].append(dict(id='sedge_walk_revision',source=str(f),sha256=hashlib.sha256(f.read_bytes()).hexdigest(),credit='Poly Haven wood_planks_grey, CC0, original Oct18 2K source'))
(OUT/'dressing.json').write_text(json.dumps(dict(places=places),indent=2))
atmo=json.loads((OUT/'atmosphere.json').read_text());atmo['volumetric_fog_sky_affect']=.25;atmo['volumetric_fog_density']=.00045
(OUT/'atmosphere.json').write_text(json.dumps(atmo,indent=2))
credits['revision']='Claude05:45: grounded native curved arch, masonry slit-window belfries, physical plank joints/nails/piles. Original resolution maps.'
(OUT/'SEDGE_SOURCE_CREDITS.json').write_text(json.dumps(credits,indent=2))
print(OUT,len(places))
