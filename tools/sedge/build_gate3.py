"""Gate-3 art over the accepted Sedge terrain. CPU only; licensed scans stay external.

python tools/sedge/build_gate3.py --base <accepted-primary> --output <new-primary>
The Gate-2 movement/support/tide contracts are retained. Gate 4 owns life/travel.
"""
from pathlib import Path
import argparse, copy, hashlib, json, math, shutil, subprocess
import numpy as np
import trimesh
import yaml
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
EXT = Path(r'C:\Otherreach_External_Assets')
KIT = Path(r'D:\Otherreach_Scratch\sedge_20261018\models')

def dump(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--base',type=Path,required=True);ap.add_argument('--output',type=Path,required=True)
    a=ap.parse_args();out=a.output
    assert a.base.resolve()!=out.resolve(), 'Keep accepted Gate 2 immutable'
    shutil.copytree(a.base,out,dirs_exist_ok=True)
    original=subprocess.check_output(['git','show','c62b5fc1:content/regions/sedge_levels.yaml'],cwd=ROOT,text=True,encoding='utf-8')
    region=yaml.safe_load(original);h=np.array(region['terrain']['heights_m'],dtype=float)
    route=json.loads((ROOT/'content/sedge_gate2_route.json').read_text(encoding='utf-8'))
    path=np.array(route['waypoints']);xx,zz=np.meshgrid(4000+np.arange(401)*5,np.arange(401)*5)
    distance=np.full(h.shape,1e9)
    for p,q in zip(path,path[1:]):
        d=q-p;t=np.clip(((xx-p[0])*d[0]+(zz-p[1])*d[1])/np.dot(d,d),0,1)
        distance=np.minimum(distance,np.hypot(xx-p[0]-t*d[0],zz-p[1]-t*d[1]))
    # Retain support/road banks. Shallow rills below low-water datum retain tidal water even at ebb.
    protected=(distance<9)|(abs(xx-5000)<13)
    relief=.045*np.sin(xx*.41)*np.cos(zz*.37)+.065*np.sin(xx*.075+zz*.12)
    h+=np.where(protected,0,relief)
    channels=np.full(h.shape,1e9)
    for cx,phase in [(4781,0),(4858,1.7),(5130,2.5),(5290,4.2)]:
        centre=cx+22*np.sin(zz/71+phase)+8*np.sin(zz/27+phase)
        channels=np.minimum(channels,abs(xx-centre))
    channel=(channels<12)&(zz>110)&(zz<1880)&~protected
    floor=-.91+.025*np.sin(zz*.12)
    h=np.where(channel,np.minimum(h,floor+np.clip((channels-4)/8,0,1)**1.5*1.6),h)
    # Small salt-pan lagoons and peat pools have contoured shores, not rectangular water decals.
    for x,z,rx,rz in [(4775,1194,38,57),(4830,1118,28,42),(5130,1255,64,33),(5280,1340,49,38),(5100,850,38,55)]:
        r=np.sqrt(((xx-x)/rx)**2+((zz-z)/rz)**2)
        bed=-.83+np.clip((r-.58)/.42,0,1)*1.3
        h=np.where((r<1)&~protected,np.minimum(h,bed),h)
    region['terrain']['heights_m']=np.round(h,3).tolist()
    region['notes']='Gate 3 Sedge art; accepted Gate-2 route/support/tide contracts retained. See docs/SEDGE_LEVELS_GATE3.md.'
    # Keep plain scalar rows compact, as in the accepted file.
    yaml.SafeDumper.add_representer(list,lambda d,v:d.represent_sequence('tag:yaml.org,2002:seq',v,flow_style=all(not isinstance(x,(dict,list)) for x in v)))
    (ROOT/'content/regions/sedge_levels.yaml').write_text(yaml.safe_dump(region,sort_keys=False,width=100000),encoding='utf-8')
    def height(x,z):
        u=np.clip((x-4000)/5,0,399.999);v=np.clip(z/5,0,399.999);i,j=int(u),int(v);u-=i;v-=j
        # Same diagonal as the authoritative grid.
        return float(h[j,i]+(h[j,i+1]-h[j,i])*u+(h[j+1,i+1]-h[j,i+1])*v) if u>=v else float(h[j,i]+(h[j+1,i+1]-h[j+1,i])*u+(h[j+1,i]-h[j,i])*v)
    data=json.loads((a.base/'dressing.json').read_text(encoding='utf-8'));places=data['places']
    places[:]=[p for p in places if p['model'] not in ('sedge_arch_gate2','sedge_arch_crest_threshold','sedge_reed_clump')]
    def place(model,x,z,y=0,yaw=0,scale=(1,1,1),**extra):
        p=dict(model=model,at=[float(x),float(z)],y=float(y),authored_transform=True,rotation_deg=[0,float(yaw),0],scale=list(scale));p.update(extra);places.append(p);return p
    # Preserve all original BOM source attribution, then record every reused scan added by this art pass.
    credits=json.loads((out/'SEDGE_SOURCE_CREDITS.json').read_text(encoding='utf-8'))
    scans={}
    def scan(id,folder,tile):
        files=list(folder.rglob('*'));dest=out/'materials'/id;dest.mkdir(parents=True,exist_ok=True);maps={}
        for role,tokens in [('basecolor',('diff','color','albedo')),('normal',('nor_gl','normalgl')),('orm',('_arm_',))]:
            f=next((f for f in files if f.suffix.lower() in ('.jpg','.png') and any(t in f.name.lower() for t in tokens)),None)
            if f:
                name=('packed_orm' if role=='orm' else role)+f.suffix.lower();shutil.copy2(f,dest/name);maps[role]=name
                credits['sources'].append(dict(id=id+':'+role,source=str(f),sha256=hashlib.sha256(f.read_bytes()).hexdigest(),credit='Existing Oct-18 CC0 sourced library; original attribution retained'))
        assert 'basecolor' in maps and 'normal' in maps,id
        dump(dest/(id+'_material.json'),dict(maps=maps,tile_size_m=tile,pbr={'normal_strength':1.0}))
        scans[id]=(dest,maps)
    lib=EXT/'Sourced_CC0_20261018'
    scan('sedge_wet_peat',lib/'wetland_mud/brown_mud/2k',2)
    scan('sedge_salt_crust',lib/'salt_flats_coast/Ground093A/2K-JPG',3)
    scan('sedge_arch_stone',lib/'medieval_masonry/old_stone_wall_02/2k',3.5)
    def export(id,mesh,material=None):
        if isinstance(mesh,trimesh.Trimesh):
            mesh.unmerge_vertices()
            if material:
                uv=np.zeros((len(mesh.vertices),2))
                for face,n in zip(mesh.faces,mesh.face_normals):
                    axes=([1,2],[0,2],[0,1])[int(np.argmax(abs(n)))];uv[face]=mesh.vertices[face][:,axes]/3
                mesh.visual=trimesh.visual.TextureVisuals(uv=uv,material=material)
        dest=out/'ready'/id/(id+'.glb');dest.parent.mkdir(parents=True,exist_ok=True);mesh.export(dest,include_normals=True)
    # Individual bedded courses and wedge-shaped voussoirs, with actual joint depth and chipped arrises.
    rng=np.random.default_rng(27103)
    material=trimesh.visual.material.PBRMaterial(name='ancient_sedge_masonry',baseColorFactor=[180,185,171,255],roughnessFactor=.87)
    blocks=[]
    for side in (-1,1):
        for y in np.arange(-3,60,2.25):
            for iz in range(4):
                offset=(int(round((y+3)/2.25))%2)*3
                for x in np.arange(-18,18,6):
                    left=max(-18,x-offset);right=min(18,x+6-offset)
                    if right-left<.2:continue
                    b=trimesh.creation.box(extents=[right-left-.10,2.14,12.86])
                    b.vertices+=rng.uniform(-.07,.07,b.vertices.shape)
                    b.apply_translation([side*72+(left+right)/2,y+1.125,-19.5+iz*13]);blocks.append(b)
                # Close the stagger's final half-block at the pier edge.
                if offset:
                    b=trimesh.creation.box(extents=[2.9,2.14,12.86]);b.apply_translation([side*72+16.5,y+1.125,-19.5+iz*13]);blocks.append(b)
    faces=np.array([[0,2,1],[1,2,3],[4,5,6],[5,7,6],[0,1,4],[1,5,4],[2,6,3],[3,6,7],[0,4,2],[2,4,6],[1,3,5],[3,7,5]])
    for depth in range(4):
        for band in range(5):
            inner=band/5;outer=(band+1)/5
            for k in range(86):
                t0=k*math.pi/86+.00065;t1=(k+1)*math.pi/86-.00065
                vertices=[]
                for z in [-26+depth*13+.055,-13+depth*13-.055]:
                    for f,t in [(inner,t0),(outer,t0),(inner,t1),(outer,t1)]:
                        rx=54+36*f;ry=54+14*f;vertices.append([rx*math.cos(t),60+ry*math.sin(t),z])
                b=trimesh.Trimesh(vertices=vertices,faces=faces,process=False);b.fix_normals();blocks.append(b)
    # Raised archivolt courses and tide-worn buttress ribs remain within accepted 180m width.
    for side in (-1,1):
        for z in (-26,26):
            for x in (58,71,85):
                for y in np.arange(1,59,3):
                    b=trimesh.creation.box(extents=[1.8,2.9,1.2]);b.apply_translation([side*x,y,z]);blocks.append(b)
    export('sedge_arch_masonry',trimesh.util.concatenate(blocks),material);place('sedge_arch_masonry',5000,30)
    # Broken midpoint: real eroded stone edges and fallen blocks flank the traversable low-water deck.
    dark=trimesh.visual.material.PBRMaterial(name='seaworn_stone',baseColorFactor=[116,128,119,255],roughnessFactor=.8)
    rubble=[]
    for _ in range(90):
        side=rng.choice([-1,1]);z=rng.uniform(-35,35);x=side*rng.uniform(3.65,6.8)
        b=trimesh.creation.icosphere(subdivisions=1,radius=1);b.apply_scale(rng.uniform(.25,.8,3));b.apply_translation([x,-.40+rng.uniform(-.2,.1),z]);rubble.append(b)
    export('sedge_span_rubble',trimesh.util.concatenate(rubble),dark);place('sedge_span_rubble',5000,1000)
    # Pin caps visibly read bronze/verdigris at close range; use actual small metal geometry, not yellow paint.
    bronze=trimesh.visual.material.PBRMaterial(name='aged_bronze',baseColorFactor=[109,107,58,255],metallicFactor=.72,roughnessFactor=.38)
    pin=trimesh.creation.cylinder(radius=.11,height=.22,sections=12);pin.apply_transform(trimesh.transformations.rotation_matrix(math.pi/2,[1,0,0]));pin.apply_translation([0,.11,0])
    export('sedge_bronze_pin',pin,bronze)
    for z in np.arange(12,1995,8):
        if 954<z<1046:continue
        for x in (4996.65,5003.35):place('sedge_bronze_pin',x,z,2.62)
    for x,z,s in [(4940,160,1.6),(5065,380,1.8),(4930,680,1.45),(5060,1420,1.65),(4945,1740,1.5)]:
        place('sedge_drowned_bell_tower',x,z,-1.0,rng.uniform(-12,12),(s,s,s))
    # Repair opaque weathering under the original geometric thatch. Preserve source rafters, rope and near-detail.
    for name,w,eave,ridge,depth in [('stilt_house',3.65,4.72,7.24,3.0),('tollhouse',6.22,7.27,10.07,4.45)]:
        scene=trimesh.load(KIT/(name+'.glb'),force='scene')
        thatch=next(copy.deepcopy(g.visual.material) for g in scene.geometry.values() if g.visual.material.name=='reed_thatch')
        thatch.alphaMode='OPAQUE';thatch.doubleSided=True
        v=[[-w,eave,-depth],[0,ridge,-depth],[w,eave,-depth],[-w,eave,depth],[0,ridge,depth],[w,eave,depth]]
        roof=trimesh.Trimesh(vertices=v,faces=[[0,3,1],[1,3,4],[1,4,2],[2,4,5]],process=False);roof.fix_normals();roof.unmerge_vertices()
        roof.visual=trimesh.visual.TextureVisuals(uv=roof.vertices[:,[0,2]]/2,material=thatch)
        scene.add_geometry(roof,node_name='opaque_weathered_thatch_underlay')
        export('sedge_'+name,scene)
    # Extra branches connect the existing civic kit. Static punts float with the existing tide presentation.
    for x,z,yaw in [(4972,985,90),(5030,970,0),(4978,1020,90),(5041,1008,0)]:
        place('sedge_punt',x,z,0,yaw,tidal_float_offset_m=.30)
        place('sedge_eel_weir',x+7,z-7,-.45,yaw)
    # The village's waterside basin remains wet at ebb. No ferry/AI invented here.
    # Keep walkable support heights unchanged and carve only beyond the supported corridor.
    for x,z in [(4972,985),(5030,970),(4978,1020),(5041,1008)]:
        r=((xx-x)/7)**2+((zz-z)/10)**2
        h=np.where((r<1)&~protected,np.minimum(h,-.96+np.clip(r,0,1)*.25),h)
    region['terrain']['heights_m']=np.round(h,3).tolist()
    (ROOT/'content/regions/sedge_levels.yaml').write_text(yaml.safe_dump(region,sort_keys=False,width=100000),encoding='utf-8')
    # Short local platforms follow the existing kit's doors, avoiding the main route.
    lamps=[]
    for p in places[:]:
        if p['model'] not in ('sedge_stilt_house','sedge_tollhouse'):continue
        x,z=p['at'];scale=p['scale'][0];yaw=p['rotation_deg'][1];ang=math.radians(yaw)
        # Kit door is +Z. A lantern on the entry platform, with its original post at true deck level.
        dx,dz=math.sin(ang),math.cos(ang)
        lx=x+dx*4.0*scale;lz=z+dz*4.0*scale
        deck=p['y']+2.6*scale
        place('sedge_lantern_post',lx,lz,deck,yaw)
        lamps.append(dict(at=[lx+math.cos(ang)*.36,deck+2.05,lz-math.sin(ang)*.36],range_m=8))
    # Dense zoned reed beds: full geometry nearby, genuine kit LODs at range, no frontier trees.
    # CPU spatial binning makes near-route clearance deterministic without quadratic scatter checks.
    reeds=[]
    for z in np.arange(145,1850,3.2):
        for x in np.arange(4480,5500,3.2):
            px=x+rng.uniform(-1.15,1.15);pz=z+rng.uniform(-1.15,1.15);i=int((px-4000)/5);j=int(pz/5)
            if abs(px-5000)<17 or distance[j,i]<5.6:continue
            y=height(px,pz)
            patch=(math.sin(px*.031+pz*.012)+math.sin(pz*.046-px*.017))*.5
            if not -.52<y<1.8 or patch<-.30 or rng.random()>.76:continue
            band=0 if y<.35 else (1 if y<.85 else 2)
            sy=rng.uniform(.70,1.30)*(1.16 if band==0 else .90)
            reeds.append(dict(at=[round(px,3),round(y-.035,3),round(pz,3)],yaw=round(float(rng.uniform(0,360)),2),scale=[round(float(rng.uniform(.80,1.20)),2),round(sy,2),round(float(rng.uniform(.80,1.20)),2)],band=band))
    for lod in range(3):
        id=f'sedge_reeds_lod{lod}';d=out/'ready'/id;d.mkdir(parents=True,exist_ok=True);shutil.copy2(KIT/f'reed_clump_LOD{lod}.glb',d/(id+'.glb'))
    dump(out/'sedge_reeds.json',dict(instances=reeds,lod_ranges_m=[0,90,220,650],chunk_m=64))
    # Native FogVolumes, existing recipe rain and lamps are configured from this one region marker.
    dump(out/'sedge_art_gate3.json',dict(lamps=lamps,mist_banks=[[4800,2,1160,220,8,80],[4900,3,550,250,10,90],[5090,2,1130,250,7,65],[5240,2,1290,230,9,85],[5020,4,220,240,12,80]],arch_centre=[5000,30],arch_crest_m=.70))
    dump(out/'dressing.json',dict(places=places))
    dump(out/'atmosphere.json',dict(fog_density=.00020,fog_light_colour='#bdccce',fog_aerial_perspective=.48,fog_sun_scatter=.02,volumetric_fog_density=.00035,volumetric_fog_albedo='#cfdbd8',volumetric_fog_length=620,volumetric_fog_anisotropy=.25,volumetric_fog_ambient_inject=.65,ambient_light_energy=.60,sdfgi_energy=.58,tonemap_exposure=1.12,tonemap_contrast=1.18,glow_intensity=.42))
    credits['gate3']=dict(geometry='Authored masonry voussoirs/courses, rubble and bronze pin caps; original kit roofs retain source texture and geometric detail.',textures='Existing Oct-18 CC0 scans, original resolution; no generated images or new purchases.',reeds=len(reeds),arch_blocks=len(blocks),tide='Accepted Gate-2 datum/period/closure unchanged; existing flow-water surface renders new rills at ebb.')
    dump(out/'SEDGE_SOURCE_CREDITS.json',credits)
    # Never silently shift a route or domain support in an art pass.
    before=yaml.safe_load(original)
    assert region['walkable_surfaces']==before['walkable_surfaces'] and region['tidal_water']==before['tidal_water']
    assert np.max(abs(h[protected]-np.array(before['terrain']['heights_m'])[protected]))==0
    print(json.dumps(dict(reeds=len(reeds),arch_blocks=len(blocks),lamps=len(lamps),dressing=len(places),protected_terrain_unchanged=True)))

if __name__=='__main__':main()
