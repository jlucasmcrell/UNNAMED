"""Fit real glove cuffs outside the native shirt and reserved chest sleeve layer."""
import bpy,bmesh,json
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from p2_gear_native_layers import default_office,fitted_native_surface

def fit_cuffs(human,obj,body_tree,work,sex,final_chest_root=None,authoring_gap_m=.006):
    source_required=authoring_gap_m-.0015;source_minimum=authoring_gap_m-.0016
    pants,upper,hem,source=default_office(human,work,sex)
    surfaces=[];reference_points=[]
    for piece in upper:
        # The production Creator renderer grows these accepted outer surfaces
        # 4.5mm. Reserve another 3.5mm for chest sleeves, then 3.5mm to the glove.
        points=[v.co+v.normal*.0045 for v in piece.data.vertices]
        surfaces.append(BVHTree.FromPolygons(points,[list(p.vertices) for p in piece.data.polygons]))
        reference_points.extend(list(v.co) for v in piece.data.vertices)
    chest_references=[];chest_receipts=[]
    if final_chest_root is not None:
        for stem in ('sealed_road_jack','quilted_ward_jack','river_lame_jack','repaired_proof_jack'):
            reference,receipt=fitted_native_surface(final_chest_root,'gear_'+stem+'__humgen_creator_'+sex)
            chest_references.append(reference);chest_receipts.append(receipt)
            surfaces.append(BVHTree.FromPolygons([v.co.copy() for v in reference.data.vertices],[list(p.vertices) for p in reference.data.polygons]))
            reference_points.extend(list(v.co) for v in reference.data.vertices)
    reference_points=np.array(reference_points)
    arms={}
    for side in ('L','R'):
        rig=human.objects.rig
        elbow=rig.matrix_world@rig.pose.bones['forearm.'+side].head
        wrist=rig.matrix_world@rig.pose.bones['hand.'+side].head
        axis=wrist-elbow;length2=axis.length_squared
        delta=reference_points-np.array(elbow)
        t=delta@np.array(axis)/length2
        radial=delta-t[:,None]*np.array(axis)
        radius=np.linalg.norm(radial,axis=1)
        keep=(t>-.2)&(t<1.2)&(radius<.12)
        arms[side]=(elbow,axis,t[keep],radius[keep])
    body=human.objects.body;evaluated=body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    native_bones=set(human.objects.rig.data.bones.keys())
    bone_names={g.index:g.name for g in body.vertex_groups if g.name in native_bones}
    body_regions=[];region_tree=KDTree(len(evaluated.data.vertices))
    for vertex in evaluated.data.vertices:
        weights=[(g.weight,bone_names[g.group]) for g in vertex.groups if g.group in bone_names]
        body_regions.append(max(weights,default=(0,''))[1])
        region_tree.insert(evaluated.matrix_world@vertex.co,vertex.index)
    region_tree.balance()
    def is_cuff_vertex(vertex):
        source=obj.data.attributes['p2_hand_source_position'].data[vertex.index].vector
        elbow,axis,_,_=arms['L' if source.x>0 else 'R']
        if (source-elbow).dot(axis)/axis.length_squared<1.15:return True
        _,index,_=region_tree.find(source)
        name=body_regions[index]
        return not (name.startswith('thumb.') or name.startswith('f_'))
    adjusted=set()
    directions={}
    for vertex in obj.data.vertices:
        if not is_cuff_vertex(vertex):continue
        elbow,axis,ts,radii=arms['L' if vertex.co.x>0 else 'R']
        t=(vertex.co-elbow).dot(axis)/axis.length_squared
        center=elbow+axis*t;radial=vertex.co-center
        if -.1<t<1.15 and 1e-5<radial.length<.12:
            near=np.abs(ts-t)<.10
            if not near.any():near=np.abs(ts-ts[np.argmin(abs(ts-t))])<.03
            # A radial envelope avoids the ambiguous inward normal on an open
            # sleeve cuff. Include runtime grow and the two authored clearances.
            required=float(radii[near].max())+(source_required if final_chest_root is not None else .0045+.007)
            direction=radial.normalized();directions[vertex.index]=direction
            if radial.length<required:
                vertex.co=center+direction*required;adjusted.add(vertex.index)
    if final_chest_root is not None:
        # Some authored bracers end just past the wrist. Include only nearby
        # palm-root vertices beyond the ordinary cuff; do not inflate the hand
        # to the forearm envelope or change its finger placement.
        for vertex in obj.data.vertices:
            if vertex.index in directions or not is_cuff_vertex(vertex):continue
            elbow,axis,_,_=arms['L' if vertex.co.x>0 else 'R']
            t=(vertex.co-elbow).dot(axis)/axis.length_squared
            radial=vertex.co-elbow-axis*t
            if -.15<t<1.35 and 1e-5<radial.length<.13 and min(surface.find_nearest(vertex.co)[3] for surface in surfaces)<.025:
                directions[vertex.index]=radial.normalized()
    for iteration in range(10):
        for vertex in obj.data.vertices:
            if vertex.index not in directions:continue
            for surface in surfaces:
                _,_,_,distance=surface.find_nearest(vertex.co)
                required=source_required if final_chest_root is not None else .007
                if distance<required:
                    vertex.co+=directions[vertex.index]*(required-distance+.0002);adjusted.add(vertex.index)
    short=0;minimum=None;short_details=[]
    for vertex in obj.data.vertices:
        for surface_index,surface in enumerate(surfaces):
            at,normal,_,distance=surface.find_nearest(vertex.co)
            if distance<.025:
                gap=distance
                minimum=gap if minimum is None else min(minimum,gap)
                too_close=gap<(source_minimum if final_chest_root is not None else .0069);short+=too_close
                if too_close:
                    elbow,axis,_,_=arms['L' if vertex.co.x>0 else 'R'];t=(vertex.co-elbow).dot(axis)/axis.length_squared
                    short_details.append(dict(vertex=vertex.index,point=list(vertex.co),forearm_t=t,radius_m=(vertex.co-elbow-axis*t).length,has_direction=vertex.index in directions,surface_index=surface_index,gap=gap))
    if short:(work/'armour_native'/('CUFF_GAP_FAILURE_'+obj.name+'_'+sex+'.json')).write_text(json.dumps(short_details,indent=2))
    assert short==0,('Cuff layering did not converge',short,minimum)
    caps=[]
    for face in obj.data.polygons:
        points=[obj.data.vertices[i].co.copy() for i in face.vertices]
        center=sum(points,Vector())/len(points)
        elbow,axis,ts,radii=arms['L' if center.x>0 else 'R']
        t=(center-elbow).dot(axis)/axis.length_squared
        radial=(center-elbow-axis*t).length
        normal=(points[1]-points[0]).cross(points[2]-points[0]).normalized()
        near=np.abs(ts-t)<.10
        if not near.any():continue
        # Source caps seal the bore of a bracer across the wearer's arm. They
        # are concealed inner faces, identified geometrically and recorded by
        # original face index; no rig weights or body triangles are cut.
        longest=max((a-b).length for a in points for b in points)
        axial=abs(normal.dot(axis.normalized()))
        if .15<t<1.1 and axial>.85 and longest>.04 and (radial<float(radii[near].max())*.8 or (longest>.075 and axial>.95)):
            caps.append(face.index)
    if caps:
        edit=bmesh.new();edit.from_mesh(obj.data);edit.faces.ensure_lookup_table()
        bmesh.ops.delete(edit,geom=[edit.faces[i] for i in caps],context='FACES_ONLY')
        bmesh.ops.delete(edit,geom=[v for v in edit.verts if not v.link_faces],context='VERTS')
        edit.to_mesh(obj.data);edit.free();obj.data.update()
    def fit_actual_cuff_rays():
        for vertex in obj.data.vertices:
            if not is_cuff_vertex(vertex):continue
            elbow,axis,_,_=arms['L' if vertex.co.x>0 else 'R']
            t=(vertex.co-elbow).dot(axis)/axis.length_squared;center=elbow+axis*t;radial=vertex.co-center
            if not(-.15<t<1.15 and 1e-5<radial.length<.18):continue
            if min(surface.find_nearest(vertex.co)[3] for surface in surfaces)>.05:continue
            direction=radial.normalized();outer=[];misses=[]
            for surface in surfaces:
                origin=center+direction*.24;remaining=.24;found=False
                for ray_step in range(8):
                    hit,normal,_,distance=surface.ray_cast(origin,-direction,remaining)
                    if hit is None:break
                    radius=(hit-center).dot(direction)
                    if 0<radius<.13:outer.append((radius,hit,normal));found=True;break
                    remaining-=distance+.0001;origin=hit-direction*.0001
                    if remaining<=0:break
                if not found:misses.append(surface)
            if outer:
                _,point,normal=max(outer,key=lambda hit:hit[0])
                if normal.dot(direction)<0:normal=-normal
                gap=(vertex.co-point).dot(normal)
                if gap<authoring_gap_m:vertex.co+=normal*(authoring_gap_m-gap)
            for surface in misses:
                point,_,_,distance=surface.find_nearest(vertex.co)
                if 1e-7<distance<authoring_gap_m:vertex.co=point+(vertex.co-point).normalized()*authoring_gap_m
            # Preserve the wearer's hand clearance after a sleeve-end correction.
            updated_t=(vertex.co-elbow).dot(axis)/axis.length_squared;center=elbow+axis*updated_t
            direction=(vertex.co-center).normalized()
            hit,_,_,_=body_tree.ray_cast(center+direction*.24,-direction,.24)
            if hit is not None:
                body_radius=(hit-center).dot(direction)
                current=(vertex.co-center).dot(direction)
                if current<body_radius+.004:vertex.co+=direction*(body_radius+.004-current)
        obj.data.update()
    chord_refinement=[]
    if final_chest_root is not None:
        from p2_gear_surface_fit import clear_body_triangles
        for iteration in range(3):
            for index,surface in enumerate(surfaces):
                history=clear_body_triangles(obj,surface,clearance=authoring_gap_m,minimum_gap=source_minimum,local_only=True,project_to_surface=False,refit=fit_actual_cuff_rays,triangulate_refined=True,max_steps=10,diagnostic=work/'armour_native'/('CUFF_SURFACE_FAILURE_'+obj.name+'_'+sex+'_'+str(index)+'.json'))
                chord_refinement.append(dict(iteration=iteration,surface=index,history=history))
            body_history=clear_body_triangles(obj,body_tree,clearance=.0008,edge_limit=.002,local_only=True,triangulate_refined=True,max_steps=10,diagnostic=work/'armour_native'/('CUFF_BODY_FAILURE_'+obj.name+'_'+sex+'.json'))
            garment=BVHTree.FromPolygons([v.co.copy() for v in obj.data.vertices],[list(p.vertices) for p in obj.data.polygons])
            if not any(garment.overlap(surface) for surface in surfaces):break
        minimum=min(surface.find_nearest(v.co)[3] for v in obj.data.vertices for surface in surfaces)
        assert minimum>=source_minimum-1e-6,('Refined cuff gap below target',minimum)
    garment=BVHTree.FromPolygons([v.co.copy() for v in obj.data.vertices],[list(p.vertices) for p in obj.data.polygons])
    overlaps=[pair for surface in surfaces for pair in garment.overlap(surface)]
    intersections=len(overlaps)
    if intersections:
        details=[]
        for index in sorted({pair[0] for pair in overlaps}):
            face=obj.data.polygons[index];points=[obj.data.vertices[i].co.copy() for i in face.vertices]
            center=sum(points,Vector())/len(points)
            elbow,axis,_,_=arms['L' if center.x>0 else 'R']
            normal=(points[1]-points[0]).cross(points[2]-points[0]).normalized()
            details.append(dict(source_face=index,source_vertices=list(face.vertices),center=list(center),
                forearm_t=(center-elbow).dot(axis)/axis.length_squared,normal_axis_dot=normal.dot(axis.normalized()),
                longest_edge_m=max((a-b).length for a in points for b in points)))
        (work/'armour_native'/('CUFF_FAILURE_'+obj.name+'_'+sex+'.json')).write_text(json.dumps(details,indent=2))
    assert intersections==0,('Cuff intersects current native sleeves',intersections)
    for reference in [pants,*upper,*chest_references]:bpy.data.objects.remove(reference,do_unlink=True)
    obj.data.update()
    return dict(source=str(source),source_vertices_adjusted=sorted(adjusted),
        reference_runtime_grow_m=.0045,glove_clearance_from_grown_shirt_m=source_required if final_chest_root is not None else .007,
        reserved_chest_sleeve_layer_m=0 if final_chest_root is not None else .0035,clearance_between_chest_and_glove_m=.0035,
        authoring_cuff_gap_target_m=authoring_gap_m,actual_native_chest_references=chest_receipts,cuff_triangle_refinement=chord_refinement,
        finger_web_body_projection_m=.0011,
        minimum_measured_near_sleeve_gap_m=minimum,rest_sleeve_triangle_intersections=intersections,
        concealed_original_cuff_cap_source_faces=caps,
        method='Anatomical forearm radial envelope of actual native sleeves; original source vertices, no bone-weight masks')
