"""Gate 4 Tollpiles service platforms: accepted plank module, source-kit thatch,
normal domain supports/collision and existing service props. All new placements PROPOSED.
Run once against the Gate-3b-based primary and content working tree.
"""
from pathlib import Path
import sys,json,copy,math
import numpy as np
import trimesh,yaml
primary=Path(sys.argv[1]);content=Path(sys.argv[2]);file=content/'regions/sedge_levels.yaml'
s=file.read_text();region=yaml.safe_load(s)
assert not any(p.get('id','').startswith('sedge_civic') for p in region['walkable_surfaces']), 'Civic platforms already authored'
dressing=json.loads((primary/'dressing.json').read_text());binding=json.loads((primary/'art_bindings.json').read_text())
# Roof keeps the accepted kit's native UVs, loose reed fringe and supporting
# roof timbers. Add four real load-bearing posts, never an unsupported canopy.
source=trimesh.load(primary/'ready/sedge_stilt_house/sedge_stilt_house.glb',force='scene');scene=trimesh.Scene();timber=None
for node in source.graph.nodes_geometry:
 transform,key=source.graph[node];g=copy.deepcopy(source.geometry[key]);g.apply_transform(transform);name=g.visual.material.name
 if name=='tarred_timber_dark':timber=copy.deepcopy(g.visual.material)
 if name.startswith('reed_thatch') or name=='tarred_timber_dark':
  if not name.startswith('reed_thatch'):g.update_faces(g.triangles_center[:,1]>5.0);g.remove_unreferenced_vertices()
  g.apply_translation([0,-2.15,0]);scene.add_geometry(g)
for x in (-3.1,3.1):
 for z in (-2.3,2.3):
  g=trimesh.creation.cylinder(radius=.12,height=2.9,sections=12);g.apply_transform(trimesh.transformations.rotation_matrix(math.pi/2,[1,0,0]));g.apply_translation([x,1.45,z]);g.visual=trimesh.visual.TextureVisuals(uv=g.vertices[:,[0,1]]/[.7,2.0],material=timber);scene.add_geometry(g)
p=primary/'ready/sedge_civic_canopy';p.mkdir(exist_ok=True);scene.export(p/'sedge_civic_canopy.glb',include_normals=True)
def place(model,x,z,y=2.45,yaw=0):dressing['places'].append(dict(model=model,at=[x,z],y=y,rotation_deg=[0,yaw,0],authored_transform=True))
platforms=[(4989.,1080.),(5011.,1089.)]
for x,z in platforms:
 for dx in (-1.79,1.79):
  for dz in (-2,2):place('sedge_review_walk',x+dx,z+dz)
 place('sedge_civic_canopy',x,z)
 for dx in (-3.1,3.1):
  for dz in (-2.3,2.3):
   key=f'sedge_civic_post_{int(x)}_{dx}_{dz}';region['structures'].append(dict(id=key,box_m=[x+dx-.12,z+dz-.12,x+dx+.12,z+dz+.12],height_m=6));binding['structures'][key]=dict(hidden=True,camera_collision_off=True)
for x,z in [(4994.5,1080),(5005.5,1089)]:place('sedge_review_walk',x,z,yaw=90)
rectangles=[(4985.5,1076,4992.5,1084),(4992.5,1078.25,4996.5,1081.75),(5007.5,1085,5014.5,1093),(5003.5,1087.25,5007.5,1090.75)]
for i,(x0,z0,x1,z1) in enumerate(rectangles):
 cols=round((x1-x0)/.5)+1;rows=round((z1-z0)/.5)+1
 region['walkable_surfaces'].append(dict(id='sedge_civic_'+str(i),origin_m=[x0,z0],spacing_m=.5,columns=cols,rows=rows,heights_m=[[2.45]*cols for _ in range(rows)]))
# Cut only the new supported footprints out of the old deep-water barrier.
def subtract(b,c):
 x0,z0,x1,z1=b;cx0,cz0,cx1,cz1=c;ix0,iz0,ix1,iz1=max(x0,cx0),max(z0,cz0),min(x1,cx1),min(z1,cz1)
 if ix0>=ix1 or iz0>=iz1:return [b]
 pieces=[(x0,z0,ix0,z1),(ix1,z0,x1,z1),(ix0,z0,ix1,iz0),(ix0,iz1,ix1,z1)]
 return [list(p) for p in pieces if p[2]-p[0]>.001 and p[3]-p[1]>.001]
structures=[]
for original in region['structures']:
 if not original['id'].startswith('sedge_water_'):structures.append(original);continue
 pieces=[original['box_m']]
 for rect in rectangles:pieces=[b for piece in pieces for b in subtract(piece,rect)]
 if pieces==[original['box_m']]:structures.append(original);continue
 for i,piece in enumerate(pieces):
  key=original['id']+'_civic_'+str(i);structures.append(dict(id=key,box_m=piece,height_m=100));binding['structures'][key]=dict(hidden=True,camera_collision_off=True)
region['structures']=structures
# Staff work on lit landings, not in a row obstructing the main road.
for key,at in [('nessa_reed',[4989,1080]),('oren_pin',[5011,1089])]:
 for n in region['npcs']:
  if n['npc_ref']=='npc.sedge.'+key:n['position_m']=at
 p=content/'routines/sedge'/(key+'.yaml');d=yaml.safe_load(p.read_text());d['anchors']['work']['position_m']=at;d['anchors']['home']['position_m']=[at[0],at[1]-2.5];p.write_text(yaml.safe_dump(d,sort_keys=False))
for key,home,work in [('resident_0',[4990,1077],[4990,1082]),('resident_1',[4987,1082],[4987,1079]),('resident_2',[5010,1086],[5012,1091]),('resident_3',[5010,1091],[5010,1086])]:
 p=content/'routines/sedge'/(key+'.yaml');d=yaml.safe_load(p.read_text());d['anchors']['home']['position_m']=home;d['anchors']['work']['position_m']=work;p.write_text(yaml.safe_dump(d,sort_keys=False))
for model,x,z,y,yaw in [('prop_cos_table_tavern_01',4987.4,1080,2.45,0),('prop_cos_basket_mvv_03',4987.4,1080,3.335,0),('prop_bp_bed_cw',4987.4,1077.5,2.45,0),('prop_cos_table_tavern_01',5012.6,1089,2.45,0),('prop_ph_wooden_crate_01',5012.6,1089,3.335,0),('station_bp_anvil',5012.5,1086.5,2.45,0)]:place(model,x,z,y,yaw)
for key,b in [('nessa_counter',[4986.25,1079.35,4988.55,1080.65]),('nessa_berth',[4986.4,1076.85,4988.4,1078.15]),('oren_counter',[5011.45,1088.35,5013.75,1089.65]),('oren_anvil',[5012.1,1086.1,5012.9,1086.9])]:
 id='sedge_civic_'+key;region['structures'].append(dict(id=id,box_m=b,height_m=6));binding['structures'][id]=dict(hidden=True,camera_collision_off=True)
# Keep the inn's existing schema; anchors now identify its actual covered berth.
p=content/'config/adventurers.yaml';t=p.read_text();start=t.index('  - id: sedge_tollpiles');end=t.index('roads:',start);part=t[start:end].replace('[5000, 1105]','[4989, 1082]').replace('[5001.5, 1080]','[4989, 1080]').replace('[5001.5, 1083]','[4991, 1080]').replace('[5001.5, 1086]','[4991, 1082]').replace('[5001.5, 1095]','[4989, 1079]').replace('[5001.5, 1098]','[4989, 1081]').replace('[5001.5, 1101]','[4989, 1077.5]');p.write_text(t[:start]+part+t[end:])
# Add ordinary height-scale workers under the Arch, on the same dry causeway.
for n,(x,z) in enumerate([(5001.3,70),(4998.7,93)]):
 id='routine.sedge.arch_worker_'+str(n);d=dict(id=id,kind='routine',schema=1,display_key=id+'.name',tags=['routine'],notes='PROPOSED causeway maintenance crew; approved full-size bodies supply a true scale cue beneath the 128m Arch.',region_ref='region.sedge_levels',people_ref='people.veth',settlement='sedge_tollpiles',role='woodcutter',roles=['woodcutter'],work_hours=[7,19],anchors=dict(home=dict(position_m=[x,110+4*n],facing_deg=0),work=dict(position_m=[x,z],facing_deg=0)))
 (content/'routines/sedge'/('arch_worker_'+str(n)+'.yaml')).write_text(yaml.safe_dump(d,sort_keys=False))
(primary/'dressing.json').write_text(json.dumps(dressing,indent=2));(primary/'art_bindings.json').write_text(json.dumps(binding,indent=2))
art=json.loads((primary/'sedge_art_gate3.json').read_text());art['lamps'] += [dict(at=[x,4.8,z],range_m=9) for x,z in platforms];(primary/'sedge_art_gate3.json').write_text(json.dumps(art,indent=2))
# Preserve the large accepted heightfield text and existing formatting. Only
# replace structures, append supports, and update the two NPC placements.
new_struct=yaml.safe_dump(dict(structures=region['structures']),sort_keys=False,width=130,default_flow_style=None)
s=s[:s.index('structures:\n')]+new_struct+s[s.index('doors: []'):]
idx=s.index('tidal_water:\n');extra=yaml.safe_dump(region['walkable_surfaces'][-4:],sort_keys=False,width=130,default_flow_style=None);s=s[:idx]+extra+s[idx:]
start=s.index('\nnpcs:\n');s=s[:start]+'\nnpcs:\n'+yaml.safe_dump(region['npcs'],sort_keys=False)
file.write_text(s)
print('Civic platforms, collision, covered services, four work residents and Arch crew authored.')
