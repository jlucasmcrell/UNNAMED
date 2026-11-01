"""Fit accepted real boots outside all actual refined native leg candidates.

CPU Blender: -- <gear work> <creator work> <female|male> <boot review id>.
Retains the accepted v4 authoring fit and tucked native backing trousers.
"""
import bpy,bmesh,json,sys,numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from HumGen3D.human.human import Human
sys.path.insert(0,str(Path(__file__).resolve().parent))
from p2_gear_leg_fit import fit_leg_geometry
from p2_gear_native_export import export_candidate
from p2_gear_surface_fit import clear_body_triangles
from p2_gear_layer_envelope import Envelope
from p2_gear_source_faces import tag_source_faces

args=sys.argv[sys.argv.index('--')+1:];work,creator=map(Path,args[:2]);sex,review=args[2:4]
rows=json.loads((work/'selected_manifest_armour_native_v1.json').read_text(encoding='utf-8'))
row=next(r for r in rows if r['review_id']==review);assert row['slot']=='feet'
identifier=row['art_id']+'__humgen_creator_'+sex
old=work/'armour_native_boot_v4';contract=json.loads((old/'primary/rigged'/identifier/'creator_part.json').read_text())
bpy.ops.wm.open_mainfile(filepath=str(old/'sources'/identifier/'native_outfit.blend'))
human=Human.from_existing(bpy.data.objects['humgen_creator_'+sex])
boots,=human.clothing.footwear.objects;pants,=human.clothing.outfit.objects
deps=bpy.context.evaluated_depsgraph_get()
mesh=bpy.data.meshes.new_from_object(boots.evaluated_get(deps),depsgraph=deps)
mesh.transform(boots.matrix_world)
bpy.data.objects.remove(boots,do_unlink=True)
boots=bpy.data.objects.new(row['art_id'],mesh);bpy.context.collection.objects.link(boots)
original_faces=tag_source_faces(boots,list(range(len(boots.data.vertices))))
references=[];receipts=[]
for leg in (r for r in rows if r['slot']=='legs'):
    reference,receipt,_=fit_leg_geometry(human,leg,work,sex)
    references.append(reference);receipts.append(dict(item=leg['item_id'],fit=receipt))
trees=[BVHTree.FromPolygons([v.co.copy() for v in ref.data.vertices],[list(p.vertices) for p in ref.data.polygons]) for ref in references]
original_tree=BVHTree.FromPolygons([v.co.copy() for v in boots.data.vertices],[list(p.vertices) for p in boots.data.polygons])
original_intersections=[len(original_tree.overlap(tree)) for tree in trees]
print('P2_BOOT_ORIGINAL_ACTUAL_LEG_INTERSECTIONS',review,sex,original_intersections,flush=True)
if '--audit-only' in args:
    (work/'armour_native'/('ORIGINAL_BOOT_LAYERS_'+review+'_'+sex+'.json')).write_text(json.dumps(dict(intersections=original_intersections,
        boot_z_range=[min(v.co.z for v in boots.data.vertices),max(v.co.z for v in boots.data.vertices)],
        leg_z_ranges=[[min(v.co.z for v in ref.data.vertices),max(v.co.z for v in ref.data.vertices)] for ref in references]),indent=2))
    raise SystemExit(0)
adjusted=set();cuffs=contract['fit_audit']['boot_cuffs_m']
envelopes={}
for side,sign in [('L',1),('R',-1)]:
    points=np.array([list(v.co) for ref in references for v in ref.data.vertices if v.co.x*sign>0 and v.co.z<cuffs[side]+.06])
    shin=human.objects.rig.matrix_world@human.objects.rig.pose.bones['shin.'+side].head
    envelopes[side]=Envelope(points,Vector((shin.x,shin.y,0)),Vector((0,0,1)),float(points[:,2].min())-.015,cuffs[side]+.03,.3,band=.04)
def fit_envelopes():
    for vertex in boots.data.vertices:
        side='L' if vertex.co.x>0 else 'R';before=vertex.co.copy()
        vertex.co=envelopes[side].fit(vertex.co,.007)
        if (vertex.co-before).length>1e-7:adjusted.add(vertex.index)
fit_envelopes()
boots.data.update()
points=[];faces=[]
for reference in references:
    offset=len(points);points.extend(v.co.copy() for v in reference.data.vertices)
    faces.extend([offset+i for i in p.vertices] for p in reference.data.polygons)
inner=BVHTree.FromPolygons(points,faces)
opened_caps=[]
def open_boot_bores(only_faces):
    selected=set();tags=boots.data.attributes['p2_source_face'].data
    debug=[]
    for index in only_faces:
        face=boots.data.polygons[index];coords=[boots.data.vertices[i].co for i in face.vertices]
        center=sum(coords,Vector())/len(coords);side='L' if center.x>0 else 'R'
        ankle=human.objects.rig.matrix_world@human.objects.rig.pose.bones['foot.'+side].head
        if len(debug)<8:debug.append(dict(center=list(center),ankle_z=ankle.z,normal_z=face.normal.z,z_span=max(p.z for p in coords)-min(p.z for p in coords),inside_delta=(envelopes[side].fit(center,0)-center).length))
        if center.z<ankle.z+.03 or abs(face.normal.z)<.90 or max(p.z for p in coords)-min(p.z for p in coords)>.01:continue
        if (envelopes[side].fit(center,0)-center).length<.001:continue
        selected.add(tags[index].value)
    if not selected:
        print('P2_BOOT_CAP_SELECTION_DIAGNOSTIC',debug,flush=True)
        return []
    indices=[p.index for p in boots.data.polygons if tags[p.index].value in selected]
    assert len(indices)<len(boots.data.polygons)*.05,('Boot bore cap selection too broad',len(indices))
    edit=bmesh.new();edit.from_mesh(boots.data);edit.faces.ensure_lookup_table()
    bmesh.ops.delete(edit,geom=[edit.faces[i] for i in indices],context='FACES_ONLY')
    bmesh.ops.delete(edit,geom=[v for v in edit.verts if not v.link_faces],context='VERTS')
    edit.to_mesh(boots.data);edit.free();boots.data.update()
    records=[dict(prepared_source_face=i,prepared_source_vertices=original_faces[i]) for i in sorted(selected)]
    opened_caps.extend(records);print('P2_OPEN_CONCEALED_BOOT_BORE_CAPS',review,sex,len(indices),flush=True)
    return records
surface_fit=clear_body_triangles(boots,inner,clearance=.0042,refit=fit_envelopes,project_to_surface=False,
    diagnostic=work/'armour_native'/('FINAL_BOOT_FAILURE_'+review+'_'+sex+'.json'),open_bores=open_boot_bores,
    triangulate_refined=True,max_steps=10)
tree=BVHTree.FromPolygons([v.co.copy() for v in boots.data.vertices],[list(p.vertices) for p in boots.data.polygons])
intersections=[len(tree.overlap(inner)) for inner in trees]
audit={**contract['fit_audit'],'final_actual_leg_references':receipts,'final_boot_to_leg_target_m':.007,
       'minimum_required_m':.0035,'actual_leg_static_triangle_intersections':intersections,
       'final_adjusted_vertices':sorted(adjusted),'surface_fit':surface_fit,'concealed_source_boot_bore_caps':opened_caps,
       'status':'REQUIRES_NEW_NATIVE_MIXED_SET_MOVEMENT_REVIEW'}
(work/'armour_native'/('FINAL_BOOT_PREFLIGHT_'+review+'_'+sex+'.json')).write_text(json.dumps(audit,indent=2))
assert not any(intersections),('Boot intersects actual refined leg',review,sex,intersections)
for reference in references:bpy.data.objects.remove(reference,do_unlink=True)
solver=human.clothing.footwear.add_obj(boots,context=bpy.context)
row={**row,'native_candidate_root':r'E:\Otherreach_Scratch\p2_gear_20261031_native_authoring\armour_native_boot_v5'}
processed=export_candidate(human,row,work,creator,sex,Path(row['source']),solver,len(adjusted),audit)
under={**row,'art_id':row['art_id']+'_underlayer','slot':'legs'}
export_candidate(human,under,work,creator,sex,Path(contract['fit_audit']['original_native_trouser_source']),contract['solver'],0,
                 {**audit,'kind':'associated_native_trouser_underlayer_not_an_additional_game_item'},processed=processed)
