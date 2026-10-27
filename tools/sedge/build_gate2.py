"""Recover the Oct-18 Sedge field and make the Gate-2 content/art package. No GPU.
Usage: python tools/sedge/build_gate2.py --output D:/.../primary
Source kits remain external and unchanged; generated licensed assets never enter Git.
"""
from pathlib import Path
import argparse, json, math, shutil, hashlib
import numpy as np
import yaml
import trimesh

def represent_list(dumper, value):
    return dumper.represent_sequence('tag:yaml.org,2002:seq', value, flow_style=all(not isinstance(v,(list,dict)) for v in value))
yaml.SafeDumper.add_representer(list, represent_list)

ROOT=Path(__file__).resolve().parents[2]
EXT=Path(r'C:\Otherreach_External_Assets')
def dump(p,d):
    p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,indent=2),encoding='utf-8')
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--output',type=Path,required=True);a=ap.parse_args();out=a.output
    old=EXT/'Sedge_20261018';src=old/'region_full/heights_full.npy';h=np.load(src)
    assert h.shape==(400,400)
    # Old samples span 1995 m. Repeat the last sample to cover the true 2000 m boundary.
    h=np.pad(h,((0,1),(0,1)),mode='edge');original=h.copy()
    # x_old -550..1450 -> world 4000..6000; z_old -575..1425 -> world 0..2000.
    shift=np.array([4550.,575.])
    route=np.array([[5430,1265],[5345,1275],[5250,1273],[5162,1275],[5106,1271],[5058,1271],[5020,1268],[5000,1275],[5000,1050],[4994.6,1050],[4994.6,950],[5000,950],[5000,895],[5000,815],[5000,735],[5000,685],[4950,707],[4880,723],[4802,877],[4760,925],[4740,1005],[4746,1313],[4754,1343],[4830,1385],[5000,1400],[5200,1390],[5430,1265]],dtype=float)
    # Resample the walk proof at <= 4 m; supports and water exclusions come from this same route.
    spans=[];walk=[]
    for p,q in zip(route,route[1:]):
        n=math.ceil(np.linalg.norm(q-p)/4)
        for i in range(n):
            x=p+(q-p)*i/n;y=p+(q-p)*(i+1)/n
            spans.append((x,y));walk.append(x.tolist())
    walk.append(route[-1].tolist())
    supports=[];places=[];structures=[];bindings={};credits=[]
    def box(id,b,height=100):
        structures.append(dict(id=id,box_m=[round(float(v),3) for v in b],height_m=height));bindings[id]={'hidden':True,'camera_collision_off':id.startswith('sedge_water_')}
    def place(id,x,z,y=0,yaw=0,scale=(1,1,1)):
        places.append(dict(model=id,at=[float(x),float(z)],y=float(y),authored_transform=True,rotation_deg=[0,float(yaw),0],scale=list(scale)))
    # Keep the Causeway exactly 2000 m. The 80 m sunken middle is -0.30 m; no 690 m bolt-on approach.
    for j in range(401):
        z=j*5
        for i in (199,200,201): h[j,i]= (-.30 if 980<=z<=1020 else (2.45-(z-960)*2.75/20 if 960<=z<980 else -.30+(z-1020)*2.75/20)) if 960<=z<=1040 else 2.45
    # These support surfaces are the ordinary domain support grids, also used by the camera collider.
    for x,y in spans:
        mid=(x+y)/2
        # The main road itself already has a terrain surface. The raised detour is a review-stage plank route.
        if abs(mid[0]-5000)<1 and not 950<=mid[1]<=1050: continue
        ix=int((mid[0]-4000)/5);iz=int(mid[1]/5)
        level=max(2.55,float(h[max(0,min(400,iz)),max(0,min(400,ix))])+0.08)
        lo=np.floor(np.minimum(x,y)-2.0);hi=np.ceil(np.maximum(x,y)+2.0)
        size=max(hi-lo);supports.append(dict(origin_m=lo.tolist(),spacing_m=float(size),columns=2,rows=2,heights_m=[[round(level,3)]*2]*2))
        d=y-x;length=float(np.linalg.norm(d));place('sedge_review_walk',*mid,y=level,yaw=math.degrees(math.atan2(d[0],d[1])),scale=(1,1,length+0.06))
    # Deep water is a real solid footprint, never a visible wall. Exclude supported walkway and road.
    xs=4000+np.arange(400)*5+2.5;zs=np.arange(400)*5+2.5
    xx,zz=np.meshgrid(xs,zs);distance=np.full(xx.shape,1e9)
    for p,q in zip(route,route[1:]):
        dx,dz=q-p;t=np.clip(((xx-p[0])*dx+(zz-p[1])*dz)/(dx*dx+dz*dz),0,1)
        distance=np.minimum(distance,np.hypot(xx-(p[0]+t*dx),zz-(p[1]+t*dz)))
    deep=(h[:-1,:-1]<0.9)&(distance>5)&(np.abs(xx-5000)>5)
    for j,row in enumerate(deep):
        edges=np.flatnonzero(np.diff(np.r_[False,row,False].astype(int)))
        for k in range(0,len(edges),2): box(f'sedge_water_{j}_{k}',[4000+edges[k]*5,j*5,4000+edges[k+1]*5,(j+1)*5])
    # Real roadside rails prevent cutting through the wet margin; water still has an ordinary domain footprint.
    for side in (-1,1): box(f'sedge_arch_pier_{side}',[5000+side*72-18,4,5000+side*72+18,56],60)
    # Recover all accepted kit placements except the old causeway; rebuild its full length below.
    layout=json.loads((old/'layout.json').read_text(encoding='utf-8-sig'))
    sources={}
    for e in layout['placements']:
        if e['group'].startswith('causeway'):continue
        file=Path(e['source'].get('path',''))
        if not file.is_file() or file.suffix.lower()!='.glb':continue
        id='sedge_'+e['asset'];sources[id]=file
        pos=e['position_m'];s=e.get('scale',1);local=e.get('scale_local',[1,1,1]);scale=[s*v for v in local]
        # Kit forward is +X; old game heading is clockwise from +Z.
        place(id,pos[0]+shift[0],pos[2]+shift[1],pos[1],90-e['rotation_deg'][1],scale)
        if e['asset'] in ('stilt_house','peat_cottage','tollhouse'):
            # Gate-2 exterior collision, explicitly not an authored interior/doorway.
            bounds=trimesh.load(file,force='scene').bounds
            corners=np.array([[xx,zz] for xx in bounds[:,0] for zz in bounds[:,2]])*np.array([scale[0],scale[2]])
            angle=math.radians(90-e['rotation_deg'][1]);rot=np.array([[math.cos(angle),math.sin(angle)],[-math.sin(angle),math.cos(angle)]])
            corners=corners@rot.T+np.array([pos[0]+shift[0],pos[2]+shift[1]])
            lo=corners.min(axis=0);hi=corners.max(axis=0)
            box('sedge_house_'+e['id'],[*lo,*hi],8)

    sources['sedge_causeway_segment']=Path(r'D:\Otherreach_Scratch\sedge_20261018\models\causeway_segment.glb')
    for start,end in [(0,960),(1040,2000)]:
        for z0 in np.arange(start,end,12):
            length=min(12,end-z0)
            place('sedge_causeway_segment',5000,z0+length,0,90,(length/12.05,1,1))
    sources['sedge_sunk_span']=Path(r'D:\Otherreach_Scratch\sedge_20261018\models\causeway_sunk_span.glb')
    # The source span carries its own elevation; terrain and new deck agree exactly at -0.30 m.
    # A dedicated review slab avoids importing the source's mismatched width/axis.
    # Standard trimesh GLB export for clearly labelled blockout masses; Gate 3 replaces hero art.
    stone=trimesh.visual.material.PBRMaterial(baseColorFactor=[110,123,116,255],roughnessFactor=.9)
    timber=trimesh.visual.material.PBRMaterial(baseColorFactor=[58,47,31,255],roughnessFactor=.85)
    def export(id,mesh,material):
        mesh.visual=trimesh.visual.TextureVisuals(material=material)
        f=out/'ready'/id/(id+'.glb');f.parent.mkdir(parents=True,exist_ok=True);mesh.export(f)
    export('sedge_review_walk',trimesh.creation.box(extents=[3.6,.18,1],transform=trimesh.transformations.translation_matrix([0,-.09,0])),timber)
    vertices=[];faces=[]
    for z,y in [(-40,2.45),(-20,-.30),(20,-.30),(40,2.45)]:
        vertices.extend([[-3.5,y,z],[3.5,y,z],[-3.5,y-.5,z],[3.5,y-.5,z]])
    for i in range(3):
        b=i*4
        for a0,a1,b0,b1 in [(0,1,4,5),(2,3,6,7),(0,2,4,6),(1,3,5,7)]:
            faces.extend([[b+a0,b+b0,b+a1],[b+a1,b+b0,b+b1]])
    ramp=trimesh.Trimesh(vertices=vertices,faces=faces);ramp.fix_normals()
    export('sedge_sunk_deck',ramp,stone)
    place('sedge_sunk_deck',5000,1000)
    # Monumental arch: 108 m clear span, spring 60 m, crown 128 m. 26 m deep structure, 180 m total width.
    verts=[];faces=[]
    for i in range(65):
        t=i*math.pi/64
        for depth in (-26,26):
            for rx,ry in [(54,54),(90,68)]:verts.append([rx*math.cos(t),60+ry*math.sin(t),depth])
    for i in range(64):
        b=i*4;n=b+4
        for a0,a1,b0,b1 in [(0,1,4,5),(2,3,6,7),(0,2,4,6),(1,3,5,7)]:
            faces.extend([[b+a0,b+b0,b+a1],[b+a1,b+b0,b+b1]])
    ring=trimesh.Trimesh(vertices=verts,faces=faces,process=True);ring.fix_normals()
    piers=[trimesh.creation.box(extents=[36,60,52],transform=trimesh.transformations.translation_matrix([side*72,30,0])) for side in (-1,1)]
    export('sedge_arch_gate2',trimesh.util.concatenate([ring]+piers),stone);place('sedge_arch_gate2',5000,30)
    portal=trimesh.Trimesh(vertices=[[-53,0,0],[53,0,0],[53,58,0]]+[ [53*math.cos(t),58+53*math.sin(t),0] for t in np.linspace(0,math.pi,48) ]+[[-53,0,0]],faces=[[0,i,i+1] for i in range(1,50)],process=True)
    other=trimesh.visual.material.PBRMaterial(baseColorFactor=[76,22,137,88],emissiveFactor=[.19,.035,.42],alphaMode='BLEND',doubleSided=True,roughnessFactor=.6)
    export('sedge_arch_crest_threshold',portal,other);place('sedge_arch_crest_threshold',5000,57,2.45)
    places[-1]['tidal_visible_above_m']=.70

    # Reeds: reuse the real kit clump, never frontier tree scatter; a bounded near-view field for Gate 2.
    sources['sedge_reed_clump']=Path(r'D:\Otherreach_Scratch\sedge_20261018\models\reed_clump_LOD0.glb')
    rng=np.random.default_rng(18)
    for _ in range(1800):
        x=rng.uniform(4580,5460);z=rng.uniform(660,1410);i=int((x-4000)/5);j=int(z/5)
        if -.15<h[j,i]<1.6 and abs(x-5000)>150:
            near=min(np.linalg.norm(np.array([x,z])-p) for p in route)
            if 7<near<75:place('sedge_reed_clump',x,z,max(.0,h[j,i]),rng.uniform(0,360),(1,1,1))
    for id,srcfile in sources.items():
        dest=out/'ready'/id/(id+'.glb');dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(srcfile,dest)
        credits.append(dict(id=id,source=str(srcfile),sha256=hashlib.sha256(srcfile.read_bytes()).hexdigest(),credit='Oct-18 Sedge kit; original BOM attribution retained'))
    # Bind all terrain cells to existing scanned wet mud / peat / salt-silt materials, not frontier flora.
    terrain={}
    for z in range(20):
        for x in range(20):terrain[f'r_2_0:c_{x:02d}_{z:02d}']='sedge_peat' if x<9 else 'sedge_silt'
    bindings_json=dict(terrain=terrain,structures=bindings)
    dump(out/'atmosphere.json',dict(fog_density=.00048,fog_light_colour='#b5c5c6',fog_aerial_perspective=.65,volumetric_fog_density=.0009,volumetric_fog_length=180,ambient_light_energy=.45,tonemap_exposure=1.05));dump(out/'art_bindings.json',bindings_json);dump(out/'dressing.json',dict(places=places));dump(out/'scatter_rules.json',{'kinds':{},'rules':[],'paths':{}})
    # Material export uses source scans at original resolution. CPU cache preparation is a separate standard step.
    from PIL import Image
    mats=[('sedge_peat',EXT/'Sourced_CC0_20261018/wetland_mud/brown_mud_dry/2k'),('sedge_silt',EXT/'Sourced_CC0_20261018/wetland_mud/Ground054/2K-JPG')]
    for id,folder in mats:
        files=list(folder.rglob('*'));files=[f for f in files if f.suffix.lower() in ('.jpg','.png')]
        albedo=next(f for f in files if any(t in f.name.lower() for t in ('diff','color','albedo')))
        norm=next((f for f in files if any(t in f.name.lower() for t in ('nor_gl','normalgl','normal'))),None)
        d=out/'materials'/id;d.mkdir(parents=True,exist_ok=True);shutil.copy2(albedo,d/'albedo.jpg')
        maps={'basecolor':'albedo.jpg'}
        if norm:shutil.copy2(norm,d/'normal.jpg');maps['normal']='normal.jpg'
        dump(d/(id+'_material.json'),dict(maps=maps,tile_size_m=3,pbr={'normal_strength':.7}))
    region=dict(id='region.sedge_levels',kind='region',schema=1,display_key='region.sedge_levels.name',tags=['region'],notes='Gate 2 PROPOSED Sedge walkable blockout; recovered Oct-18 samples. Canon source and proposals in docs/SEDGE_LEVELS_GATE2.md.',region_key='r_2_0',cells=list(terrain),bounds_m={'min':[4000,0],'max':[6000,2000]},spawn={'position_m':[5430,1265],'facing_deg':270},generation={'terrain':{'base_height_mm':0,'amplitude_mm':0,'samples_per_axis':2}},terrain={'origin_m':[4000,0],'spacing_m':5,'columns':401,'rows':401,'heights_m':np.round(h,3).tolist()},structures=structures,doors=[],walkable_surfaces=supports,tidal_water=dict(low_m=-.6,high_m=.75,period_ticks=28800,phase_ticks=0,passages=[dict(key='sunk_middle_span',id='sedge_tidal_span',box_m=[4996.5,960,5003.5,1040],height_m=100,dry_at_m=-.30)]))
    p=ROOT/'content/regions/sedge_levels.yaml';p.write_text(yaml.safe_dump(region,sort_keys=False,width=100000),encoding='utf-8')
    # Recovery proof retains the exact original nested field independently of the route support work.
    np.save(out/'sedge_recovered_loop.npy',original[115:286,110:291])
    dump(ROOT/'content/sedge_gate2_route.json',dict(points=walk,waypoints=route.tolist(),source_sha256=hashlib.sha256(src.read_bytes()).hexdigest(),length_m=sum(np.linalg.norm(q-p) for p,q in zip(route,route[1:])),notes='Temporary PROPOSED raised inspection walk replaces old unimplemented ferry legs at Gate 2; ferry life/travel remains Gate 4.'))
    dump(out/'SEDGE_SOURCE_CREDITS.json',dict(sources=credits,original_bom=json.loads((old/'bom.json').read_text(encoding='utf-8-sig')),arch='PROPOSED Gate-2 massing: 128m crown, 180m total, 108m opening. Canon fixes no dimensions; no new history. Bell remains slack-water canon, not crest ringing.'))
    for id,name,x,z in [('tollpiles','Tollpiles',5000,1000),('pellow_cuts','Pellow Cuts',4802,877),('orr_camp','Orr Winter Camp',4754,1343),('causeway_arch','Causeway Arch',5000,80)]:
        p=ROOT/'content/locations/sedge'/f'{id}.yaml';p.parent.mkdir(parents=True,exist_ok=True)
        p.write_text(yaml.safe_dump(dict(id='location.sedge_'+id,kind='location',schema=1,display_key='location.sedge_'+id+'.name',tags=['location'],name=name,region_ref='region.sedge_levels',anchor={'world':[x,0,z]},kind_of_place='landmark',discovery={'methods':['visited'],'radius_m':20,'xp':0},map={'reveals_radius_m':100,'marker':'none'},fast_travel={'node':False}),sort_keys=False),encoding='utf-8')
    print(json.dumps(dict(placements=len(places),water_footprints=len(structures),supports=len(supports),route_points=len(walk),region=str(p))))
if __name__=='__main__':main()

