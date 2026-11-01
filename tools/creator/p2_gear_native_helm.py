"""Fit source-approved real helms, then run HumGen's normal custom-clothing export.

Blender CPU: -- <private gear work> <creator work> <female|male> [Axx ...]
These are review candidates, never item bindings or a worn-fit certificate.
"""
import bpy,bmesh,json,sys,hashlib
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
from HumGen3D.human.human import Human
from HumGen3D.human.process.settings import ExportSettings,QualitySettings,ScriptSettings
from HumGen3D.human.process.shape_keys import livekey_group
from HumGen3D.backend.preferences.preference_func import get_prefs

sys.path.insert(0,str(Path(__file__).resolve().parent))
from p2_gear_native_export import export_candidate

args=sys.argv[sys.argv.index('--')+1:]
work,creator=map(Path,args[:2]);sex=args[2]
variant='armour_native_headcontrols_v2'
if '--variant' in args:
    index=args.index('--variant');variant=args[index+1];del args[index:index+2]
assert variant.replace('_','').isalnum() and not variant.startswith('.')
only=set(args[3:])
assert sex in ('female','male')
rows=[r for r in json.loads((work/'selected_manifest_final_release.json').read_text()) if r.get('slot')=='head' and (not only or r['review_id'] in only)]
tools=Path(__file__).resolve().parents[1]/'animation'
sys.path.insert(0,str(tools))
from audit_humgen_transport import Glb

for row in rows:
    row={**row,'native_candidate_root':variant}
    base='humgen_creator_'+sex
    identifier=row['art_id']+'__'+base
    folder=work/'armour_native/primary/rigged'/identifier
    folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(creator/sex/'native_unchanged.blend'))
    bpy.context.scene.render.threads_mode='FIXED';bpy.context.scene.render.threads=6
    human=Human.from_existing(next(o for o in bpy.data.objects if o.type=='ARMATURE'))
    human.clothing.outfit.remove();human.clothing.footwear.remove()
    body=human.objects.body;rig=human.objects.rig
    bpy.context.view_layer.update()
    evaluated=body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    points=[evaluated.matrix_world@v.co for v in evaluated.data.vertices]
    head_pivot=rig.matrix_world@rig.pose.bones['head'].head
    head=[p for p in points if p.z>head_pivot.z-.055]
    top=max(p.z for p in head)
    source=work/'game_art_v2/ready'/row['art_id']/(row['art_id']+'.glb')
    old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(source))
    obj=next(o for o in set(bpy.data.objects)-old if o.type=='MESH')
    obj.data.transform(obj.matrix_world);obj.parent=None;obj.matrix_world=Matrix.Identity(4)
    # The original MetaHuman helms already surround a real 1.81592m head.
    # The two origin-centred BlenderKit helmets are placed at the same crown.
    zs=[v.co.z for v in obj.data.vertices]
    if max(zs)<1:
        shift=1.83644-max(zs)
        for v in obj.data.vertices:v.co.z+=shift
    scale=max(.90,min(1.10,(top-head_pivot.z)/(.19017)))
    source_pivot=Vector((0,-.006631,1.625751))
    for v in obj.data.vertices:v.co=head_pivot+(v.co-source_pivot)*scale
    # Resolve actual cranium/ear clearance on the garment, never on the body.
    # A convex head envelope avoids the ear recesses attracting helmet vertices.
    edit=bmesh.new()
    for p in head:edit.verts.new(p)
    bmesh.ops.convex_hull(edit,input=list(edit.verts),use_existing_faces=False)
    bmesh.ops.delete(edit,geom=[v for v in edit.verts if not v.link_faces],context='VERTS')
    edit.verts.ensure_lookup_table();edit.faces.ensure_lookup_table()
    hull=BVHTree.FromBMesh(edit)
    adjusted=0
    for v in obj.data.vertices:
        nearest,normal,_,distance=hull.find_nearest(v.co)
        signed=(v.co-nearest).dot(normal)
        if signed<.0035:
            v.co=nearest+normal*.0035;adjusted+=1
    edit.free();obj.data.update()
    obj.name=row['art_id']
    obj.vertex_groups.clear();obj.vertex_groups.new(name='head').add(list(range(len(obj.data.vertices))),1,'REPLACE')
    solver=human.clothing.outfit.add_obj(obj,'full',recalculate_weights=False)
    print('P2_NATIVE_CUSTOM_CLOTHING',identifier,solver,'clearance_vertices',adjusted,flush=True)
    export_candidate(human,row,work,creator,sex,source,solver,adjusted,dict(method='Source helmet placement plus actual native cranium/ear clearance envelope'))
