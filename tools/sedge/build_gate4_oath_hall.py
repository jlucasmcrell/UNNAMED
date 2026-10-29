"""Bounded, dry outer record hall; native Sedge kit, ordinary collision/nav.
Run once against a Gate-4 primary/content snapshot. Deep dungeon stays sealed.
"""
from pathlib import Path
import copy, json, math, sys
import numpy as np
import trimesh, yaml

primary, content = map(Path, sys.argv[1:3])
file = content / 'regions/sedge_levels.yaml'
text = file.read_text(encoding='utf8')
r = yaml.load(text, Loader=yaml.CSafeLoader)
assert not any(v.get('id','').startswith('sedge_oath_outer') for v in r['walkable_surfaces'])
d = json.loads((primary/'dressing.json').read_text(encoding='utf8'))
b = json.loads((primary/'art_bindings.json').read_text(encoding='utf8'))
def place(model,x,z,y=2.45,yaw=0,scale=None):
    v=dict(model=model,at=[x,z],y=y,rotation_deg=[0,yaw,0],authored_transform=True)
    if scale: v['scale']=scale
    d['places'].append(v)
for x in (5014.21,5017.79):
    for z in (36,40,44): place('sedge_review_walk',x,z)
for x in (5005.73,5010.19): place('sedge_review_walk',x,40,yaw=90,scale=[1,1,1.115])
# Existing ruin-kit wall surfaces keep their carved geometry and native maps.
walls=[(5016,34,0,1.33),(5016,46,0,1.33),(5020,37,90,1),(5020,43,90,1),
       (5012,35.5,90,.5),(5012,44.5,90,.5)]
for i,(x,z,yaw,sx) in enumerate(walls):
    place('sedge_belfry_wall',x,z,yaw=yaw,scale=[sx,1,1])
    if yaw==0: box=[x-3*sx,z-.62,x+3*sx,z+.62]
    else: box=[x-.62,z-3*sx,x+.62,z+3*sx]
    key=f'sedge_oath_outer_wall_{i}'
    r['structures'].append(dict(id=key,box_m=box,height_m=6))
    b['structures'][key]=dict(hidden=True,camera_collision_off=True)
# Reuse native tarred timber for suspended tally-staves, lintels and sealed stair.
source=trimesh.load(primary/'ready/sedge_stilt_house/sedge_stilt_house.glb',force='scene')
material=next(copy.deepcopy(g.visual.material) for g in source.geometry.values()
              if g.visual.material.name=='tarred_timber_dark')
scene=trimesh.Scene()
def timber(size,at):
    g=trimesh.creation.box(extents=size);g.apply_translation(at)
    g.visual=trimesh.visual.TextureVisuals(uv=g.vertices[:,[0,1]]/[1,2],material=material)
    scene.add_geometry(g)
timber([7.5,.22,.24],[5016,5.15,35.15])
for i in range(11):
    x=5012.8+i*.64;length=.85+(i%4)*.16
    timber([.07,length,.055],[x,5.04-length/2,35.15])
    # Small transverse cuts give each stave a distinct tally silhouette.
    for j in range(1+i%3):timber([.13,.035,.08],[x,4.35-j*.13,35.15])
for z in (35.15,44.8):
    for x in (5012.8,5019.2): timber([.2,2.7,.2],[x,3.8,z])
timber([6.6,.25,.25],[5016,5.15,44.8])
# A visibly barred descending stair, beyond the outer hall's collision boundary.
for i in range(5):timber([2.2,.18,.45],[5016,2.4-i*.22,46+i*.43])
for x in np.linspace(5014.9,5017.1,8):timber([.09,2.4,.09],[x,3.65,45.4])
timber([2.5,.14,.15],[5016,4.6,45.4]);timber([2.5,.14,.15],[5016,2.8,45.4])
# Local coordinates avoid large-coordinate mesh precision loss.
scene.apply_translation([-5016,-2.45,-40])
key='sedge_oath_outer_tallies';out=primary/'ready'/key;out.mkdir(exist_ok=True)
scene.export(out/(key+'.glb'),include_normals=True);place(key,5016,40)
key='sedge_oath_outer_seal';r['structures'].append(dict(id=key,box_m=[5014.7,45.25,5017.3,45.55],height_m=6));b['structures'][key]=dict(hidden=True,camera_collision_off=True)
rects=[(5012,34,5020,46),(5003.5,38.25,5012,41.75)]
for i,(x0,z0,x1,z1) in enumerate(rects):
    cols=round((x1-x0)/.5)+1;rows=round((z1-z0)/.5)+1
    r['walkable_surfaces'].append(dict(id=f'sedge_oath_outer_{i}',origin_m=[x0,z0],spacing_m=.5,columns=cols,rows=rows,heights_m=[[2.45]*cols for _ in range(rows)]))
def subtract(box,c):
    x,z,xx,zz=box;a,b,cx,cz=c;ix,iz,ex,ez=max(x,a),max(z,b),min(xx,cx),min(zz,cz)
    if ix>=ex or iz>=ez:return [box]
    return [list(p) for p in [(x,z,ix,zz),(ex,z,xx,zz),(ix,z,ex,iz),(ix,ez,ex,zz)] if p[2]-p[0]>.001 and p[3]-p[1]>.001]
structures=[]
for v in r['structures']:
    if not v['id'].startswith('sedge_water_'):structures.append(v);continue
    pieces=[v['box_m']]
    for rect in rects:pieces=[p for old in pieces for p in subtract(old,rect)]
    if pieces==[v['box_m']]:structures.append(v);continue
    for i,box in enumerate(pieces):
        key=v['id']+'_oath_'+str(i);structures.append(dict(id=key,box_m=box,height_m=100));b['structures'][key]=dict(hidden=True,camera_collision_off=True)
r['structures']=structures
new=yaml.safe_dump(dict(structures=structures),sort_keys=False,width=130,default_flow_style=None)
text=text[:text.index('structures:\n')]+new+text[text.index('doors: []'):]
idx=text.index('tidal_water:\n');text=text[:idx]+yaml.safe_dump(r['walkable_surfaces'][-2:],sort_keys=False,width=130,default_flow_style=None)+text[idx:]
file.write_text(text,encoding='utf8')
art=json.loads((primary/'sedge_art_gate3.json').read_text(encoding='utf8'))
art.setdefault('signs',[]).append(dict(at=[5016,4.15,45.12],yaw=180,text='DEEP STAIR SEALED\nDROWNED OATH-HOUSE\nLevel 30+ danger beyond\nSurvey ends in this hall.'))
for name,value in [('dressing.json',d),('art_bindings.json',b),('sedge_art_gate3.json',art)]:
    (primary/name).write_text(json.dumps(value,indent=2),encoding='utf8')
print('Outer record hall: 96m2, dry approach, native masonry, tally-staves, sealed deep stair.')
