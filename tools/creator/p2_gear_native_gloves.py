"""Fit an original paired glove mesh to native wrists and finger landmarks.

CPU Blender: -- <gear work> <creator work> <female|male> A09|A12.
Source skin weights only place the source pose. HumGen supplies final native
body weights, controls and export; no legacy skeleton or binds are retained.
"""
import bpy,json,sys,re
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
from HumGen3D.human.human import Human
sys.path.insert(0,str(Path(__file__).resolve().parent))
from p2_gear_native_export import export_candidate
from p2_gear_native_cuffs import fit_cuffs
from p2_gear_native_cuff_finish import finish_native_cuffs
args=sys.argv[sys.argv.index('--')+1:]
work,creator=map(Path,args[:2]);sex,review=args[2:4]
final_chest_root=Path(args[4]) if len(args)>4 else None
assert sex in ('female','male') and review in ('A09','A12','A22')
row=next(r for r in json.loads((work/'selected_manifest_armour_native_v1.json').read_text()) if r['review_id']==review)
row={**row,'native_candidate_root':'armour_native_gloves_v2'}
if final_chest_root is not None:row['native_candidate_root']=str(Path(args[5]) if len(args)>5 else final_chest_root.parent/'armour_native_gloves_v3')
bpy.ops.wm.open_mainfile(filepath=str(creator/sex/'native_unchanged.blend'))
human=Human.from_existing(next(o for o in bpy.data.objects if o.type=='ARMATURE'))
human.clothing.outfit.remove();human.clothing.footwear.remove()
rig=human.objects.rig;body=human.objects.body
bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get()
evaluated=body.evaluated_get(deps)
skin=BVHTree.FromPolygons([evaluated.matrix_world@v.co for v in evaluated.data.vertices],[list(p.vertices) for p in evaluated.data.polygons])
source=Path(row['source']);before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(source));imported=set(bpy.data.objects)-before
obj=next(o for o in imported if o.type=='MESH' and o.name==row['source_object'])
source_rig=next(m.object for m in obj.modifiers if m.type=='ARMATURE')
source_heads={b.name:source_rig.matrix_world@b.head_local for b in source_rig.data.bones}
target_heads={b.name:rig.matrix_world@b.head for b in rig.pose.bones}

def frame(origin,end,normal):
    axis=end-origin;length=axis.length;axis.normalize()
    across=normal.cross(axis).normalized();out=axis.cross(across).normalized()
    return np.array([across,axis*length,out],dtype=float).T

transforms={}
for side in ('l','r'):
    target_side=side.upper()
    s=lambda name:source_heads[name+'_'+side]
    t=lambda name:target_heads[name+'.'+target_side]
    sn=(s('index_01')-s('hand')).cross(s('pinky_01')-s('hand')).normalized()
    tn=(t('f_index.01')-t('hand')).cross(t('f_pinky.01')-t('hand')).normalized()
    mappings={'hand':('middle_01','hand','f_middle.01'), 'lowerarm':('hand','forearm','hand')}
    for finger in ('index','middle','ring','pinky','thumb'):
        prefix='thumb' if finger=='thumb' else 'f_'+finger
        for segment in (1,2,3):
            name=f'{finger}_0{segment}';target=f'{prefix}.0{segment}'
            so=s(name);to=t(target)
            se=s(f'{finger}_0{segment+1}') if segment<3 else so+(so-s(f'{finger}_02'))
            te=t(f'{prefix}.0{segment+1}') if segment<3 else to+(to-t(f'{prefix}.02'))
            transforms[name+'_'+side]=(so,to,frame(to,te,tn)@np.linalg.inv(frame(so,se,sn)))
        # The source metacarpal helpers follow the native palm between the wrist
        # and the corresponding knuckle, rather than an arbitrary FBX bone roll.
        so=s('hand');to=t('hand');se=s(f'{finger}_01');te=t(f'{prefix}.01')
        transforms[finger+'_metacarpal_'+side]=(so,to,frame(to,te,tn)@np.linalg.inv(frame(so,se,sn)))
    for name,(end,start_t,end_t) in mappings.items():
        so=s('hand' if name=='hand' else 'lowerarm');to=t(start_t)
        transforms[name+'_'+side]=(so,to,frame(to,t(end_t),tn)@np.linalg.inv(frame(so,s(end),sn)))

def group_key(name):
    side=name[-1:]
    match=re.match(r'^(index|middle|ring|pinky|thumb)_(0[123]|metacarpal)',name)
    if match:return match[0]+'_'+side
    if name.startswith(('lowerarm','wrist')):return ('lowerarm' if name.startswith('lowerarm') else 'hand')+'_'+side
    if name.startswith('hand'):return 'hand_'+side
    raise ValueError('Unmapped source glove influence '+name)

points=[obj.matrix_world@v.co for v in obj.data.vertices]
assert min(p.x for p in points)<-.2 and max(p.x for p in points)>.2,'Original paired source gloves required'
posed=[]
for vertex,point in zip(obj.data.vertices,points):
    result=Vector();total=0.
    for influence in vertex.groups:
        if influence.weight<1e-6:continue
        key=group_key(obj.vertex_groups[influence.group].name)
        origin,target,transform=transforms[key]
        result+=(target+Vector(transform@np.array(point-origin)))*influence.weight;total+=influence.weight
    assert total>.99,('Unweighted source glove vertex',vertex.index,total)
    posed.append(result/total)
mesh=obj.data.copy();clean=bpy.data.objects.new(row['art_id'],mesh);bpy.context.collection.objects.link(clean)
for vertex,point in zip(mesh.vertices,posed):vertex.co=point
for old in imported:bpy.data.objects.remove(old,do_unlink=True)
# Native, source-authored PBR maps from the approved static material pipeline;
# geometry remains the full original pair, never the single-hand icon mesh.
before=set(bpy.data.objects);material_id=row.get('material_reference_art_id',row['art_id']);material_source=work/'game_art_v2/ready'/material_id/(material_id+'.glb')
bpy.ops.import_scene.gltf(filepath=str(material_source));material_objects=set(bpy.data.objects)-before
material_mesh=next(o for o in material_objects if o.type=='MESH');assert len(material_mesh.data.materials)==1
mesh.materials.clear();mesh.materials.append(material_mesh.data.materials[0])
for polygon in mesh.polygons:polygon.material_index=0
for old in material_objects:bpy.data.objects.remove(old,do_unlink=True)
source_position=mesh.attributes.new('p2_hand_source_position','FLOAT_VECTOR','POINT')
for vertex in mesh.vertices:source_position.data[vertex.index].vector=vertex.co
adjusted=0
for vertex in mesh.vertices:
    point,normal,_,distance=skin.find_nearest(vertex.co)
    if (vertex.co-point).dot(normal)<.0035:
        vertex.co=point+normal*.0035;adjusted+=1
mesh.update();bpy.context.view_layer.update()
cuff_audit=fit_cuffs(human,clean,skin,work,sex,final_chest_root,row.get('native_cuff_authoring_gap_m',.006))
solver=human.clothing.outfit.add_obj(clean,'full',context=bpy.context,progress=lambda f:print('P2_NATIVE_CONVERSION',round(f,3),flush=True))
native_finish=finish_native_cuffs(human,human.clothing.outfit.objects[0],work,sex,final_chest_root) if row.get('native_finish_after_binding') else None
export_candidate(human,row,work,creator,sex,source,solver,adjusted,
    dict(method='Original paired source gloves posed from wrist and individual finger head landmarks; final native body weights via HumGen',
         original_pair_vertices=len(posed),fitted_pair_vertices=len(mesh.vertices),native_weighted_cuff_finish=native_finish,clearance_between_layers_m=.0035,source_icon_geometry_not_used=True,cuff_layering=cuff_audit))
