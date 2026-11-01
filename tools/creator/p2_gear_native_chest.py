"""Fit owned chest armour outside current native clothing and all four leg fits.

CPU Blender: -- <gear work> <creator work> <female|male> A02|A06|A11|A17 <fresh output root>.
References are the same source-vertex leg fits used by the leg exporter. No
reference garment or another body is exported with the chest piece.
"""
import bpy,bmesh,json,sys
import numpy as np
from pathlib import Path
from mathutils.bvhtree import BVHTree
from HumGen3D.human.human import Human
sys.path.insert(0,str(Path(__file__).resolve().parent))
from p2_gear_native_export import export_candidate
from p2_gear_pose_source import posed_source
from p2_gear_native_layers import default_office
from p2_gear_leg_fit import fit_leg_geometry
from p2_gear_layer_envelope import fit_chest_envelopes
from p2_gear_surface_fit import clear_body_triangles

args=sys.argv[sys.argv.index('--')+1:];work,creator=map(Path,args[:2]);sex,review=args[2:4]
rows=json.loads((work/'selected_manifest_armour_native_v1.json').read_text())
row=next(r for r in rows if r['review_id']==review)
assert row['slot']=='chest' and sex in ('female','male')
# Reproducible final recipes from the approved native candidate audit.
# Write to a new output root; never overwrite a captured or delivered fit.
output=Path(args[4])
row={**row,'native_candidate_root':str(output),'replaces_creator_sweater':True}
padding=.014 if review=='A11' else .007
skin_regions=review=='A17' and sex=='female'
all_layer_gaps=review=='A02' and sex=='male'
preserve_source_depth=True;proper_regions=True
bpy.ops.wm.open_mainfile(filepath=str(creator/sex/'native_unchanged.blend'))
human=Human.from_existing(bpy.data.objects['humgen_creator_'+sex])
human.clothing.outfit.remove();human.clothing.footwear.remove()
obj,audit=posed_source(human,row,work/'game_art_v2/ready'/row['art_id']/(row['art_id']+'.glb'))
attribute=obj.data.attributes.new('p2_source_face','INT','FACE')
attribute.data.foreach_set('value',list(range(len(obj.data.polygons))))
opened_caps=[]
references=[];leg_receipts=[]
for leg in (r for r in rows if r['slot']=='legs'):
    reference,receipt,_=fit_leg_geometry(human,leg,work,sex)
    references.append((reference,0.));leg_receipts.append(dict(item=leg['item_id'],source=leg['source'],fit=receipt))
pants,upper,hem,office=default_office(human,work,sex)
references.extend((reference,.0045) for reference in [pants,*upper])
cache=output/'prefit'/(review+'_'+sex)
cache.mkdir(parents=True,exist_ok=True)
assert not (cache/'native_prefit.blend').exists(),'Use a fresh output root'
(cache/'PREFIT.json').write_text(json.dumps(dict(source=row['source'],garment=obj.name,audit=audit,
    references=[(r.name,g) for r,g in references],leg_receipts=leg_receipts,office=str(office)),indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(cache/'native_prefit.blend'),compress=True)
replace_sweater=True
replaced_references=[]
if replace_sweater:
    replaced_references=[reference for reference,grow in references if 'sweater' in reference.name.lower()]
    assert len(replaced_references)==1,'Expected the replaceable Office outer sweater'
    references=[(reference,grow) for reference,grow in references if reference not in replaced_references]
    for reference in replaced_references:bpy.data.objects.remove(reference,do_unlink=True)
    audit['replaces_creator_sweater']=True
    audit['retained_creator_underlayers']='Native shirt and trousers; actual leg armour replaces trousers through the existing slot rule'
deps=bpy.context.evaluated_depsgraph_get();body=human.objects.body.evaluated_get(deps)
skin=BVHTree.FromPolygons([body.matrix_world@v.co for v in body.data.vertices],[list(p.vertices) for p in body.data.polygons])
trees=[]
for reference,grow in references:
    trees.append(BVHTree.FromPolygons([v.co+v.normal*grow for v in reference.data.vertices],[list(p.vertices) for p in reference.data.polygons]))
points=[[body.matrix_world@v.co for v in body.data.vertices]]
points.extend([[v.co+v.normal*grow for v in reference.data.vertices] for reference,grow in references])
points=np.array([list(p) for group in points for p in group])
def finish_wrist_cuffs():
    if review!='A02':return
    rig=human.objects.rig;changed=0
    for vertex in obj.data.vertices:
        side='L' if vertex.co.x>0 else 'R'
        elbow=rig.matrix_world@rig.pose.bones['forearm.'+side].head
        axis=rig.matrix_world@rig.pose.bones['hand.'+side].head-elbow
        t=(vertex.co-elbow).dot(axis)/axis.length_squared
        radius=(vertex.co-elbow-axis*t).length
        if .75<t<1.4 and radius<.13:
            # The source cuff extended 20% of a forearm onto the hand. Seat its
            # complete original surface at the native wrist; retain every UV
            # and face, with a smooth axial compression of the distal band.
            fitted=.75+(t-.75)*(.25/.452)
            vertex.co+=axis*(fitted-t);changed+=1
    obj.data.update()
    audit['native_wrist_cuff_adjusted_vertices']=changed
    audit['native_wrist_cuff_end_forearm_t']=1.0

refinement=[];envelope_audit=None
for step in range(5):
    envelope_audit=fit_chest_envelopes(human,obj,points,preserve_source_depth,proper_regions,padding,skin_regions)
    finish_wrist_cuffs()
    body_fit=clear_body_triangles(obj,skin,clearance=.006,minimum_gap=.0035,
        diagnostic=work/'armour_native'/('CHEST_BODY_FAILURE_'+review+'_'+sex+'.json'),
        triangulate_refined=True,max_steps=10)
    if all_layer_gaps:
        layer_gap_history=[]
        for reference,surface in zip(references,trees):
            history=clear_body_triangles(obj,surface,clearance=.006,minimum_gap=.0035,local_only=True,triangulate_refined=True,max_steps=10)
            layer_gap_history.append(dict(layer=reference[0].name,history=history))
        body_fit.extend(clear_body_triangles(obj,skin,clearance=.006,minimum_gap=.0035,triangulate_refined=True,max_steps=10))
        audit['measured_inner_layer_gap_checks']=layer_gap_history
    garment=BVHTree.FromPolygons([v.co.copy() for v in obj.data.vertices],[list(p.vertices) for p in obj.data.polygons])
    pairs=[garment.overlap(surface) for surface in trees]
    intersections=[len(p) for p in pairs]
    refinement.append(dict(step=step,vertices=len(obj.data.vertices),intersections=intersections))
    print('P2_CHEST_LAYER_AUDIT',review,sex,refinement[-1],flush=True)
    if not any(intersections):break
    if step==4:break
    faces={a for group in pairs for a,b in group}
    edit=bmesh.new();edit.from_mesh(obj.data);edit.faces.ensure_lookup_table()
    tag=edit.faces.layers.int['p2_source_face']
    affected={edit.faces[index][tag] for index in faces}
    edges={e for index in faces for e in edit.faces[index].edges if e.calc_length()>.015}
    if edges:bmesh.ops.subdivide_edges(edit,edges=list(edges),cuts=1,use_grid_fill=True)
    local_faces=[f for f in edit.faces if f[tag] in affected]
    vertices={v for f in local_faces for v in f.verts}
    # Cross-section envelopes clear the garment as a whole. A few tiny source
    # sleeve-wall facets can still cross an underlying sweater fold between
    # their corners; resolve only those measured intersecting source faces.
    for vertex in vertices:
        for surface,count in zip(trees,intersections):
            if not count:continue
            point,normal,_,distance=surface.find_nearest(vertex.co)
            if distance<.10 and (vertex.co-point).dot(normal)<.007:
                vertex.co=point+normal*.007
    bmesh.ops.triangulate(edit,faces=[f for f in edit.faces if f[tag] in affected and len(f.verts)>3])
    edit.to_mesh(obj.data);edit.free();obj.data.update()
if padding!=.007 or skin_regions:
    vertices=[v.co.copy() for v in obj.data.vertices];probes=list(vertices)
    probes.extend(sum((vertices[i] for i in p.vertices),vertices[0]*0)/len(p.vertices) for p in obj.data.polygons)
    probes.extend((vertices[e.vertices[0]]+vertices[e.vertices[1]])*.5 for e in obj.data.edges)
    measured=[]
    for (reference,grow),surface in zip(references,trees):
        distances=np.array([surface.find_nearest(point)[3] for point in probes])
        result=dict(reference=reference.name,minimum_sampled_gap_m=float(distances.min()),below_required=int((distances<.0035-1e-6).sum()))
        measured.append(result);print('P2_FINAL_LAYER_MINIMUM',result,flush=True)
    audit['measured_layer_gaps_before_native_conversion']=measured
    assert not any(r['below_required'] for r in measured),('Actual inner-layer gap below3.5mm',review,sex,measured)
adjusted=envelope_audit['adjusted_source_vertices']
audit.update(clearance_between_layers_m=.0035,current_office_source=str(office),concealed_original_sleeve_cap_faces=opened_caps,
    original_source_faces_preserved=not bool(opened_caps),
    reference_office_runtime_grow_m=.0045,leg_fit_references=leg_receipts,
    reference_static_triangle_intersections=intersections,envelope_fit=envelope_audit,layer_refinement=refinement,body_triangle_fit=body_fit,
    status='CANDIDATE_REQUIRES_NATIVE_MOVEMENT_AND_LAYER_REVIEW')
audit_path=work/'armour_native'/('CHEST_FINAL_PREFLIGHT_'+review+'_'+sex+'.json')
audit_path.write_text(json.dumps(audit,indent=2))
if any(intersections):
    details=[]
    for (reference,_),surface,group in zip(references,trees,pairs):
        for face_index in sorted({a for a,b in group})[:16]:
            face=obj.data.polygons[face_index];coords=[obj.data.vertices[i].co.copy() for i in face.vertices]
            center=sum(coords,coords[0]*0)/len(coords);near,normal,_,gap=surface.find_nearest(center)
            details.append(dict(layer=reference.name,face=face_index,original_face=obj.data.attributes['p2_source_face'].data[face_index].value,
                points=[list(p) for p in coords],center=list(center),nearest=list(near),normal=list(normal),gap=gap))
    (work/'armour_native'/('CHEST_LAYER_FAILURE_'+review+'_'+sex+'.json')).write_text(json.dumps(details,indent=2))
assert not any(intersections),('Chest intersects an actual inner layer',review,sex,intersections)
for reference,_ in references:bpy.data.objects.remove(reference,do_unlink=True)
solver=human.clothing.outfit.add_obj(obj,'full',context=bpy.context,progress=lambda f:print('P2_NATIVE_CONVERSION',round(f,3),flush=True))
export_candidate(human,row,work,creator,sex,Path(row['source']),solver,len(adjusted),audit)
