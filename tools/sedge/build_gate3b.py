"""Gate 3b: kit-built Arch, open drowned belfries and physically tiled walks.

CPU only. Run after the existing convert_ue.py converts the three Dwarven kit
pieces (documented in docs/SEDGE_LEVELS_GATE3B.md). Licensed output stays external.
"""
from pathlib import Path
import argparse, copy, hashlib, json, math, shutil
import numpy as np
import trimesh
from PIL import Image


def dump(path, value):
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--base', type=Path, required=True)
    ap.add_argument('--output', type=Path, required=True)
    ap.add_argument('--converted', type=Path, required=True)
    a = ap.parse_args()
    assert a.base.resolve() != a.output.resolve()
    out = a.output
    shutil.copytree(a.base, out, dirs_exist_ok=True)
    original = json.loads((out/'dressing.json').read_text(encoding='utf-8'))
    places = [p for p in original['places'] if p['model'] not in
              ('sedge_arch_masonry', 'sedge_drowned_bell_tower', 'sedge_tally_tower')]
    credits = json.loads((out/'SEDGE_SOURCE_CREDITS.json').read_text(encoding='utf-8'))

    def export(id, scene):
        d = out/'ready'/id
        d.mkdir(parents=True, exist_ok=True)
        scene.export(d/(id+'.glb'), include_normals=True)

    def place(id, x, z, y=0, scale=(1,1,1), rotation=(0,0,0)):
        places.append(dict(model=id, at=[float(x),float(z)], y=float(y), scale=[float(v) for v in scale],
                           rotation_deg=[float(v) for v in rotation], authored_transform=True))

    def load(id):
        return trimesh.load(a.converted/id/(id+'.glb'), force='scene')

    # Native carved Dwarven rib vault, capital and broken gate column. Do not
    # recreate their relief with boxes, and retain source UV/PBR maps.
    for id in ('sedge_arch_rib', 'sedge_arch_capital', 'sedge_arch_pier'):
        shutil.copytree(a.converted/id, out/'ready'/id, dirs_exist_ok=True)
        meta = json.loads((a.converted/id/(id+'_meta.json')).read_text(encoding='utf-8'))
        credits['sources'].append(dict(id=id, source=meta['source'],
            sha256=meta['source_sha256'], credit=meta['pack'], licence=meta.get('licence', ''),
            metadata=str(a.converted/id/(id+'_meta.json'))))
    rib = load('sedge_arch_rib')
    # Missing outer crown voussoir on the front rib. Rear ribs still bridge the
    # gap: genuine daylight and a broken silhouette, rather than painted damage.
    chipped = rib.copy()
    for g in chipped.geometry.values():
        c = g.triangles_center
        remove = (c[:,0] > 1.20) & (c[:,0] < 1.65) & (c[:,1] > 4.80)
        g.update_faces(~remove);g.remove_unreferenced_vertices()
    export('sedge_arch_rib_broken', chipped)
    for z in (10,30,50):
        place('sedge_arch_rib_broken' if z == 50 else 'sedge_arch_rib', 5000,z,
              scale=(24.3,21,6))
    # Four projecting, already-broken kit pylons frame the springing. The
    # existing accepted pier collision footprint remains the limit of each base.
    for side in (-1,1):
        for z in (18,43):
            place('sedge_arch_pier',5000+side*72,z,-.8,(14,14,8),(0,180 if side<0 else 0,0))
        place('sedge_arch_capital',5000+side*72,60,56,(10,8,5),(0,0,side*8))
    place('sedge_arch_capital',5000,58,113,(5,4.9,5))
    # Fallen carved voussoirs flank, never occupy, the central approach.
    for x,z,yaw in [(4938,98,38),(5075,115,-27),(4915,78,73)]:
        place('sedge_arch_capital',x,z,-2,(4,4,4),(0,yaw,65))

    # Assemble the open upper storey from native rib-vault pieces. Lower walls
    # are the accepted tower's stone, clipped below the belfry; its obsolete
    # solid top and microscopic bell are removed.
    kit = Path(r'D:\Otherreach_Scratch\sedge_20261018\models')
    old = trimesh.load(kit/'drowned_bell_tower.glb',force='scene')
    lower = trimesh.Scene()
    bell = None
    timber = None
    for node in old.graph.nodes_geometry:
        tf, name = old.graph[node];g=copy.deepcopy(old.geometry[name]);g.apply_transform(tf)
        mat = g.visual.material.name
        if mat.startswith('M_Bell'):
            bell=g;continue
        if mat == 'tarred_timber_dark': timber=copy.deepcopy(g.visual.material)
        if mat in ('water_stained_stone','old_rubble_stone_dark','old_rubble_stone','silt','causeway_kerb'):
            v,f,uv=trimesh.intersections.slice_faces_plane(g.vertices,g.faces,[0,-1,0],[0,8.3,0],uv=g.visual.uv)
            material=g.visual.material
            g=trimesh.Trimesh(vertices=v,faces=f,process=False)
            g.visual=trimesh.visual.TextureVisuals(uv=uv,material=material)
            if len(g.faces):lower.add_geometry(g)
    assert bell is not None and timber is not None
    # Preserve the source bell and texture; correct the unit mistake to a 2m
    # mouth. Its open lip and clapper can now be read through the arches.
    bell.apply_translation(-bell.bounds.mean(axis=0))
    bell.apply_scale(2.0/max(bell.extents[0],bell.extents[2]))
    bell.apply_translation([0,10.7,0]);lower.add_geometry(bell,node_name='restored_source_bell')
    for side in range(4):
        tf=trimesh.transformations.rotation_matrix(side*math.pi/2,[0,1,0])
        tf[:3,3]=[math.sin(side*math.pi/2)*2.35,8.0,math.cos(side*math.pi/2)*2.35]
        for node in rib.graph.nodes_geometry:
            nt,name=rib.graph[node];g=copy.deepcopy(rib.geometry[name]);g.apply_transform(nt)
            if side==0:
                c=g.triangles_center
                g.update_faces(~((c[:,0]>.8)&(c[:,1]>4.9)));g.remove_unreferenced_vertices()
            g.apply_scale([.64,.95,.55]);g.apply_transform(tf);lower.add_geometry(g)
    # Timber headstock spans the open chamber; stone cornice fragments leave a
    # broken top, not a closed cap hiding the bell.
    head=trimesh.creation.box([4.6,.35,.35]);head.apply_translation([0,12.25,0])
    head.visual=trimesh.visual.TextureVisuals(uv=head.vertices[:,[0,1]],material=timber)
    lower.add_geometry(head,node_name='bell_headstock')
    top=float(bell.bounds[1,1]);length=12.12-top
    hanger=trimesh.creation.cylinder(radius=.055,height=length+.12,sections=12)
    hanger.apply_transform(trimesh.transformations.rotation_matrix(math.pi/2,[1,0,0]))
    hanger.apply_translation([0,(top+12.12)/2,0])
    hanger.visual=trimesh.visual.TextureVisuals(uv=hanger.vertices[:,[0,1]],material=trimesh.visual.material.PBRMaterial(name='bell_iron_hanger',baseColorFactor=[40,42,38,255],metallicFactor=.8,roughnessFactor=.52))
    lower.add_geometry(hanger,node_name='bell_iron_hanger')
    for x,y,z,yaw in [(-2.4,8.7,-2.4,0),(2.2,8.7,2.2,90),(-2.5,8.7,2.0,23)]:
        frag=load('sedge_arch_capital')
        tf=trimesh.transformations.rotation_matrix(math.radians(yaw),[0,1,0]);tf[:3,3]=[x,y,z]
        frag.apply_scale(.55);frag.apply_transform(tf)
        for node in frag.graph.nodes_geometry:
            nt,name=frag.graph[node];g=copy.deepcopy(frag.geometry[name]);g.apply_transform(nt);lower.add_geometry(g)
    export('sedge_belfry_ruin',lower)
    # Deliberate perspective rhythm: near bell, middle-distance silhouettes and
    # farther roofs dissolve into haze. All towers visibly lean, never the road.
    towers=[(4940,160,1.7,5,-7),(5065,380,2.1,-6,5),(4930,680,1.65,8,-5),
            (5060,1420,1.85,-5,8),(4945,1740,1.6,7,5),(4802,1218,1.75,-8,-4)]
    for i,(x,z,s,rx,rz) in enumerate(towers):
        place('sedge_belfry_ruin',x,z,-1,(s,s,s),(rx,i*31,rz))

    # Four actual metres per module; physical .24m boards. UVs are authored in
    # metres (one source repeat / 1.2m along the grain, / .24m across), so varying
    # route segment lengths only make small joint closure adjustments.
    src=trimesh.load(kit/'plank_walk.glb',force='scene')
    wood_root=Path(r'C:\Otherreach_External_Assets\Sourced_CC0_20261018\cloth_rope_leather_metal\Wood060\2K-JPG')
    albedo=wood_root/'Wood060_2K-JPG_Color.jpg'
    normal=wood_root/'Wood060_2K-JPG_NormalGL.jpg'
    rough=wood_root/'Wood060_2K-JPG_Roughness.jpg'
    ao=wood_root/'Wood060_2K-JPG_AmbientOcclusion.jpg'
    orm=Image.merge('RGB',(Image.open(ao).convert('L'),Image.open(rough).convert('L'),Image.new('L',(2048,2048),0)))
    wood=trimesh.visual.material.PBRMaterial(name='weathered_plank_fullres',baseColorTexture=Image.open(albedo),normalTexture=Image.open(normal),metallicRoughnessTexture=orm,roughnessFactor=1.0,metallicFactor=0.0)
    for f in (albedo,normal,rough,ao):
        credits['sources'].append(dict(id='sedge_review_walk:'+f.stem,source=str(f),sha256=hashlib.sha256(f.read_bytes()).hexdigest(),credit='ambientCG Wood060, CC0; existing Oct-18 BOM source, original 2K maps'))
    boards=[]
    for n in range(16):
        g=trimesh.creation.box([3.6,.12,.238]);g.apply_translation([0,-.06,-2+(n+.5)*.25]);g.unmerge_vertices()
        uv=np.zeros((len(g.vertices),2))
        for face,normal in zip(g.faces,g.face_normals):
            # Source grain runs along texture U; local X is the board's length.
            if abs(normal[1])>.5:uv[face]=g.vertices[face][:,[0,2]]/[1.2,.24]+[0,n*.137]
            else:uv[face]=g.vertices[face][:,[0,1]]/[1.2,.24]
        g.visual=trimesh.visual.TextureVisuals(uv=uv,material=wood);boards.append(g)
    for x in (-1.45,1.45):
        g=trimesh.creation.box([.16,.18,4]);g.apply_translation([x,-.19,0]);g.unmerge_vertices()
        g.visual=trimesh.visual.TextureVisuals(uv=g.vertices[:,[0,2]]/[.48,2],material=wood);boards.append(g)
    export('sedge_review_walk',trimesh.util.concatenate(boards))
    for p in places:
        if p['model']=='sedge_review_walk':p['scale'][2]/=4
    # Add natural reed-clump geometry to submerged lower masonry; texture stain
    # alone cannot supply the requested weed silhouette.
    weed=trimesh.load(kit/'reed_clump_LOD0.glb',force='scene')
    export('sedge_masonry_weed',weed)
    for x,z,s,_,_ in towers:
        for angle in np.arange(0,360,60):
            r=math.radians(angle)
            place('sedge_masonry_weed',x+math.cos(r)*3*s,z+math.sin(r)*3*s,-.35,(.9,1.25,.9),(0,angle,0))
    for side in (-1,1):
        for z in np.arange(7,60,3):
            place('sedge_masonry_weed',5000+side*55.5,float(z),-.4,(1,1.5,1),(0,z*13,0))
    art=json.loads((out/'sedge_art_gate3.json').read_text(encoding='utf-8'))
    art['gate3b']=True
    art['mist_banks']=[[4890,5,1080,170,20,90],[4900,6,550,250,22,110],
                       [5090,5,1130,260,18,95],[5240,6,1290,250,24,110],
                       [5020,7,220,260,24,100],[4980,7,790,300,26,85],
                       [5130,6,1650,240,20,95],[4870,6,365,230,22,90]]
    dump(out/'sedge_art_gate3.json',art)
    atmo=json.loads((out/'atmosphere.json').read_text(encoding='utf-8'))
    atmo.update(fog_density=.00034,fog_aerial_perspective=.64,volumetric_fog_density=.0006,
                volumetric_fog_length=1100,volumetric_fog_anisotropy=.45,volumetric_fog_sky_affect=.8)
    dump(out/'atmosphere.json',atmo)
    dump(out/'dressing.json',dict(places=places))
    credits['gate3b']=dict(arch='Existing Dwarven Citadel native rib vault, carved capital and broken gate column; source PBR retained. Broken front rib is a derivative of the source mesh.',
        towers='Oct-18 stone lower storey, restored Cosmos CS1_Bell_01, open Dwarven belfry arches, broken native capital fragments; leaned at placement.',
        walks='Four-metre plank modules with physical board width and metre-based UVs; original-resolution ambientCG Wood060 scan from the Oct-18 BOM.',
        atmosphere='Native layered FogVolumes and local volumetric spotlights; no generated textures.')
    dump(out/'SEDGE_SOURCE_CREDITS.json',credits)
    print(json.dumps(dict(dressing=len(places),towers=len(towers),accepted_ground_reeds_tides_unchanged=True)))


if __name__=='__main__':main()

