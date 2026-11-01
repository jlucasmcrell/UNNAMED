"""Finish a native weighted cuff in its actual evaluated pose, preserving controls."""
import bpy,numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from p2_gear_native_layers import default_office,fitted_native_surface

def finish_native_cuffs(human,obj,work,sex,chest_root,required=.0035):
    assert all(m.type=='ARMATURE' or not m.show_viewport for m in obj.modifiers)
    keys=list(obj.data.shape_keys.key_blocks) if obj.data.shape_keys else []
    assert keys,'Native garment controls are required'
    def coordinates():
        bpy.context.view_layer.update()
        evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        return np.array([list(obj.matrix_world@v.co) for v in evaluated.data.vertices]),[list(p.vertices) for p in evaluated.data.polygons],[list(e.vertices) for e in evaluated.data.edges]
    positions,faces,edges=coordinates();count=len(positions)
    saved=[]
    for key in keys:
        data=np.empty(count*3);key.data.foreach_get('co',data);saved.append(data.reshape(count,3))
    def write(offset):
        for key,base in zip(keys,saved):key.data.foreach_set('co',(base+offset).ravel())
        obj.data.shape_keys.update_tag();obj.data.update();bpy.context.view_layer.update()
    # The armature's actual per-vertex linear response includes native weights,
    # object transforms and preserve-volume mode; no guessed bind matrices.
    columns=[]
    for axis in range(3):
        offset=np.zeros((count,3));offset[:,axis]=.001;write(offset)
        moved,_,_=coordinates();columns.append((moved-positions)/.001)
    write(np.zeros((count,3)));jacobian=np.stack(columns,axis=2)
    assert np.min(abs(np.linalg.det(jacobian)))>.01,'Degenerate native cuff response'
    inverse=np.linalg.inv(jacobian);offset=np.zeros((count,3))
    pants,upper,_,_=default_office(human,work,sex);references=[(o,.0045) for o in upper]
    for stem in ('sealed_road_jack','quilted_ward_jack','river_lame_jack','repaired_proof_jack'):
        ref,_=fitted_native_surface(chest_root,'gear_'+stem+'__humgen_creator_'+sex);references.append((ref,0.))
    trees=[(o.name,BVHTree.FromPolygons([v.co+v.normal*grow for v in o.data.vertices],[list(p.vertices) for p in o.data.polygons])) for o,grow in references]
    body=human.objects.body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    skin=BVHTree.FromPolygons([body.matrix_world@v.co for v in body.data.vertices],[list(p.vertices) for p in body.data.polygons])
    axes={}
    for side in ('L','R'):
        rig=human.objects.rig;start=rig.matrix_world@rig.pose.bones['forearm.'+side].head
        axis=rig.matrix_world@rig.pose.bones['hand.'+side].head-start;axes[side]=(start,axis)
    history=[];touched=set()
    for step in range(16):
        positions,faces,edges=coordinates();tree=BVHTree.FromPolygons([Vector(p) for p in positions],faces)
        probes=[(Vector(p),[i]) for i,p in enumerate(positions)]
        probes.extend((sum((Vector(positions[i]) for i in face),Vector())/len(face),face) for face in faces)
        probes.extend(((Vector(positions[a])+Vector(positions[b]))*.5,[a,b]) for a,b in edges)
        sums=np.zeros((count,3));weights=np.zeros(count);bad=[]
        for name,reference in trees:
            near=0
            for probe,indices in probes:
                point,normal,_,distance=reference.find_nearest(probe)
                if distance>=required-1e-6:continue
                near+=1;start,axis=axes['L' if probe.x>0 else 'R']
                radial=probe-start-axis*((probe-start).dot(axis)/axis.length_squared)
                if normal.dot(radial)<0:normal=-normal
                delta=normal*(.006-(probe-point).dot(normal))
                if delta.length>.012:delta=delta.normalized()*.012
                for i in indices:sums[i]+=np.array(delta);weights[i]+=1
            overlaps=tree.overlap(reference)
            bad.append(dict(reference=name,near=near,intersections=len(overlaps)))
            for index,_ in overlaps:
                face=faces[index];probe=sum((Vector(positions[i]) for i in face),Vector())/len(face)
                point,normal,_,_=reference.find_nearest(probe);start,axis=axes['L' if probe.x>0 else 'R']
                radial=probe-start-axis*((probe-start).dot(axis)/axis.length_squared)
                if normal.dot(radial)<0:normal=-normal
                delta=normal*(.006-(probe-point).dot(normal))
                for i in face:sums[i]+=np.array(delta);weights[i]+=1
        body_overlaps=tree.overlap(skin)
        for index,_ in body_overlaps:
            for i in faces[index]:
                probe=Vector(positions[i]);point,normal,_,_=skin.find_nearest(probe)
                delta=normal*(.0015-(probe-point).dot(normal));sums[i]+=np.array(delta);weights[i]+=1
        history.append(dict(step=step,layers=bad,body_intersections=len(body_overlaps)))
        print('NATIVE_WEIGHTED_CUFF_FINISH',sex,history[-1],flush=True)
        if not body_overlaps and not any(r['near'] or r['intersections'] for r in bad):
            for ref,_ in references:bpy.data.objects.remove(ref,do_unlink=True)
            bpy.data.objects.remove(pants,do_unlink=True)
            return dict(method='Native garment pose-space vertex finishing after HumGen binding. Actual armature response inverted; identical local offsets applied to Basis and every native control, preserving morph deltas and all weights. No body edits or topology changes.',required_garment_gap_m=required,vertices_adjusted=len(touched),history=history)
        active=weights>0
        assert active.any(),'Native finish has no correction direction'
        delta=np.zeros((count,3));delta[active]=sums[active]/weights[active,None]
        offset+=np.einsum('nij,nj->ni',inverse,delta);touched.update(np.where(active)[0].tolist());write(offset)
    raise AssertionError(('Native weighted cuff finishing did not converge',sex,history))
