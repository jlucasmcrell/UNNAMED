"""Author a real boot pair and its native tucked-trouser underlayer.

CPU Blender: -- <gear work> <creator work> <female|male> A01
Initial single-pair proof. No item bindings or accepted-fit claim are written.
"""
import bpy,bmesh,json,sys
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
from HumGen3D.human.human import Human
sys.path.insert(0,str(Path(__file__).resolve().parent))
from p2_gear_native_export import export_candidate
from p2_gear_native_layers import default_office

args=sys.argv[sys.argv.index('--')+1:]
work,creator=map(Path,args[:2]);sex=args[2];review=args[3]
rows=json.loads((work/'selected_manifest_armour_native_v1.json').read_text())
row=next(r for r in rows if r['review_id']==review)
assert row['slot']=='feet', 'The boot pipeline requires a real source boot pair.'
row={**row,'native_candidate_root':'armour_native_boot_v4'}
bpy.ops.wm.open_mainfile(filepath=str(creator/sex/'native_unchanged.blend'))
bpy.context.scene.render.threads_mode='FIXED';bpy.context.scene.render.threads=6
human=Human.from_existing(next(o for o in bpy.data.objects if o.type=='ARMATURE'))
body,rig=human.objects.body,human.objects.rig
human.clothing.outfit.remove();human.clothing.footwear.remove()
bpy.context.view_layer.update()
evaluated=body.evaluated_get(bpy.context.evaluated_depsgraph_get())
skin=[evaluated.matrix_world@v.co for v in evaluated.data.vertices]

def bvh(points,polygons):return BVHTree.FromPolygons(points,polygons,all_triangles=False)
skin_bvh=bvh(skin,[list(p.vertices) for p in evaluated.data.polygons])
def imported_surface(path,predicate):
    before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path))
    objects=set(bpy.data.objects)-before
    candidates=[o for o in objects if o.type=='MESH' and predicate(o)]
    assert len(candidates)==1,[(o.name,len(o.data.vertices)) for o in candidates]
    original=candidates[0];deps=bpy.context.evaluated_depsgraph_get()
    mesh=bpy.data.meshes.new_from_object(original.evaluated_get(deps),depsgraph=deps)
    mesh.transform(original.matrix_world)
    clean=bpy.data.objects.new('P2_authoring_surface',mesh);bpy.context.collection.objects.link(clean)
    for obj in objects:bpy.data.objects.remove(obj,do_unlink=True)
    return clean

pants,upper,shirt_hem,pants_source=default_office(human,work,sex)
for reference in upper:bpy.data.objects.remove(reference,do_unlink=True)
pants.name=row['art_id']+'_tucked_underlayer'
source=work/'game_art_v2/ready'/row['art_id']/(row['art_id']+'.glb')
boots=imported_surface(source,lambda o:True);boots.name=row['art_id']
inspection=json.loads((work/'armour_native/inspection.json').read_text())
if review not in inspection:
    inspection.update(json.loads((work/'armour_native/source_inspection_remaining.json').read_text()))
source_rigs=inspection[review]['rigs']
original=next(r for r in source_rigs.values() if all(name in r for name in ('foot_l','calf_l','ball_l','foot_r','calf_r','ball_r')))
native={side:{name:rig.matrix_world@rig.pose.bones[name+'.'+side].head for name in ('shin','foot','toe')} for side in ('L','R')}
source_points=[v.co.copy() for v in boots.data.vertices]
cuffs={}
for side,suffix,sign in [('L','l',1),('R','r',-1)]:
    a,k,t=[Vector(original[name+'_'+suffix][0]) for name in ('foot','calf','ball')]
    target=native[side];ta,tk,tt=target['foot'],target['shin'],target['toe']
    forward=t-a;up=k-a;right=forward.cross(up).normalized()
    target_forward=tt-ta;target_up=tk-ta;target_right=target_forward.cross(target_up).normalized()
    basis=np.array([right,forward,up],dtype=float).T
    target_basis=np.array([target_right,target_forward,target_up],dtype=float).T
    transform=target_basis@np.linalg.inv(basis)
    indices=[v.index for v in boots.data.vertices if v.co.x*sign>0]
    bottom=min(source_points[i].z for i in indices)
    barefoot=min(p.z for p in skin if p.x*sign>0 and p.z<ta.z+.02)
    sole=barefoot-.0035
    for index in indices:
        point=source_points[index]
        fitted=ta+Vector(transform@np.array(point-a))
        # Keep the ankle at its actual joint and fit the sole to the native foot.
        # The source has a 30mm platform below its bare foot; it is tailored here.
        if point.z<a.z:
            fraction=(point.z-bottom)/(a.z-bottom)
            fitted.z=sole+fraction*(ta.z-sole)
        boots.data.vertices[index].co=fitted
    cuff=max(boots.data.vertices[i].co.z for i in indices)
    ceiling=tk.z-.018
    if cuff>ceiling:
        for index in indices:
            point=boots.data.vertices[index].co
            if point.z>ta.z:point.z=ta.z+(point.z-ta.z)*(ceiling-ta.z)/(cuff-ta.z)
        cuff=ceiling
    cuffs[side]=cuff

# Explicit inside-out authoring. Tailor the native trouser's concealed lower leg
# to skin +3.5mm before fitting the boot outside that layer by another 3.5mm.
for vertex in pants.data.vertices:
    cuff=cuffs['L' if vertex.co.x>0 else 'R']
    if vertex.co.z>cuff+.06:continue
    at,normal,_,distance=skin_bvh.find_nearest(vertex.co)
    target=at+normal*.0035
    blend=max(0,min(1,(cuff+.06-vertex.co.z)/.06))
    vertex.co=vertex.co.lerp(target,blend)
pants.data.update()
pants_bvh=bvh([v.co.copy() for v in pants.data.vertices],[list(p.vertices) for p in pants.data.polygons])
adjusted=0
for vertex in boots.data.vertices:
    for surface in (skin_bvh,pants_bvh):
        at,normal,_,distance=surface.find_nearest(vertex.co)
        if surface is pants_bvh and (distance>.06 or abs(vertex.co.z-at.z)>.035):continue
        if (vertex.co-at).dot(normal)<.0035:
            vertex.co=at+normal*.0035;adjusted+=1
boots.data.update()
cuffs={side:max(v.co.z for v in boots.data.vertices if v.co.x*sign>0) for side,sign in [('L',1),('R',-1)]}

# Source-vertex coverage mask, never a rig-weight cut: its edge stays 40mm down
# the shaft, safely beneath the real outer boot in all review poses.
masked=[v.index for v in pants.data.vertices if v.co.z<cuffs['L' if v.co.x>0 else 'R']-.04 or v.co.z>shirt_hem+.08]
edit=bmesh.new();edit.from_mesh(pants.data);edit.verts.ensure_lookup_table()
masked_set=set(masked)
fully_covered=[face for face in edit.faces if all(v.index in masked_set for v in face.verts)]
bmesh.ops.delete(edit,geom=fully_covered,context='FACES_ONLY')
bmesh.ops.delete(edit,geom=[v for v in edit.verts if not v.link_faces],context='VERTS')
edit.to_mesh(pants.data);edit.free();pants.data.update()
print('P2_BOOT_PREFIT',sex,'pants_masked_source_vertices',len(masked),'boot_clearance_adjustments',adjusted,flush=True)
solver_pants=human.clothing.outfit.add_obj(pants,'pants',context=bpy.context)
solver_boots=human.clothing.footwear.add_obj(boots,context=bpy.context)
audit=dict(method='Source anatomical ankle/ball/knee placement; current native office trousers fitted through HumGen library API, native skin and trouser clearance; body unchanged',clearance_between_layers_m=.0035,inside_boot_overlap_m=.04,undershirt_overlap_m=.08,undershirt_hem_m=shirt_hem,masked_trouser_source_vertices=masked,original_native_trouser_source=str(pants_source),boot_cuffs_m=cuffs)
processed=export_candidate(human,row,work,creator,sex,source,solver_boots,adjusted,audit)
underlayer={**row,'art_id':row['art_id']+'_underlayer','slot':'legs','item_id':row['item_id']}
export_candidate(human,underlayer,work,creator,sex,pants_source,solver_pants,len(masked),{**audit,'kind':'associated_native_trouser_underlayer_not_an_additional_game_item'},processed=processed)
