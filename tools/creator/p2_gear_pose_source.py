"""Place approved real armour geometry using anatomical source/native frames.

Legacy weights are used only for this authoring placement. The caller must run
HumGen's native clothing conversion/body weighting before exporting the piece.
"""
import bpy,re
import numpy as np
from mathutils import Vector,Matrix
from mathutils.kdtree import KDTree

def posed_source(human,row,prepared):
    before=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=row['source'])
    imported=set(bpy.data.objects)-before
    original=next(o for o in imported if o.type=='MESH' and o.name==row['source_object'])
    source_rig=next(m.object for m in original.modifiers if m.type=='ARMATURE')
    sh={b.name:source_rig.matrix_world@b.head for b in source_rig.pose.bones}
    rig=human.objects.rig;th={b.name:rig.matrix_world@b.head for b in rig.pose.bones}
    source_spine=[sh[n] for n in ['pelvis','spine_01','spine_02','spine_03','spine_04','spine_05','neck_01']]
    target_spine=[th[n] for n in ['spine','spine.001','spine.002','spine.003','neck']]
    def spinal(point):
        height=(point.z-source_spine[0].z)/(source_spine[-1].z-source_spine[0].z)
        z=target_spine[0].z+height*(target_spine[-1].z-target_spine[0].z)
        for a,b in zip(target_spine,target_spine[1:]):
            if z<=b.z:return a.lerp(b,(z-a.z)/(b.z-a.z))
        return target_spine[-1]+Vector((0,0,z-target_spine[-1].z))
    def basis(origin,end):
        axis=end-origin;length=axis.length;assert length>1e-5
        axis.normalize();front=Vector((0,-1,0))
        if abs(axis.dot(front))>.98:front=Vector((1,0,0))
        across=front.cross(axis).normalized();normal=axis.cross(across).normalized()
        return np.array([across,axis*length,normal],dtype=float).T
    transforms={}
    for index,name in enumerate(['pelvis','spine_01','spine_02','spine_03','spine_04','spine_05']):
        origin,end=source_spine[index:index+2];target,target_end=spinal(origin),spinal(end)
        transforms[name]=(origin,target,basis(target,target_end)@np.linalg.inv(basis(origin,end)))
    for side in ('l','r'):
        target_side=side.upper()
        for source,target,end_source,end_target in [
            ('thigh','thigh','calf','shin'),('calf','shin','foot','foot'),
            ('clavicle','shoulder','upperarm','upper_arm'),('upperarm','upper_arm','lowerarm','forearm'),
            ('lowerarm','forearm','hand','hand')]:
            origin,end=sh[source+'_'+side],sh[end_source+'_'+side]
            to,te=th[target+'.'+target_side],th[end_target+'.'+target_side]
            transforms[source+'_'+side]=(origin,to,basis(to,te)@np.linalg.inv(basis(origin,end)))
        # Foot orientation uses ankle/ball/knee landmarks, never FBX bone roll.
        origin,ball,knee=[sh[n+'_'+side] for n in ('foot','ball','calf')]
        to,toe,shin=[th[n+'.'+target_side] for n in ('foot','toe','shin')]
        def foot_basis(a,b,k):return np.array([(b-a).cross(k-a).normalized(),b-a,k-a],dtype=float).T
        transform=foot_basis(to,toe,shin)@np.linalg.inv(foot_basis(origin,ball,knee))
        transforms['foot_'+side]=(origin,to,transform);transforms['ball_'+side]=(ball,toe,transform)
        origin,end=sh['hand_'+side],sh['middle_01_'+side]
        to,te=th['hand.'+target_side],th['f_middle.01.'+target_side]
        transforms['hand_'+side]=(origin,to,basis(to,te)@np.linalg.inv(basis(origin,end)))
    for name in ('neck_01','neck_02','head'):
        origin=sh[name];to=spinal(origin)
        transforms[name]=(origin,to,np.eye(3))
    def key(name):
        if name in transforms:return name
        if name.startswith('spine_'):return name[:8]
        if name.startswith('wrist_'):return 'hand_'+name[-1]
        if name.startswith('ankle_'):return 'foot_'+name[-1]
        for prefix in ('thigh','calf','foot','ball','clavicle','upperarm','lowerarm','hand'):
            if name.startswith(prefix):return prefix+'_'+name[-1]
        raise ValueError('Unmapped source influence '+name)
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get()
    source_mesh=original.evaluated_get(deps).data
    assert len(source_mesh.vertices)==len(original.data.vertices),'Source modifier changed vertex identity'
    source_points=[original.matrix_world@v.co for v in source_mesh.vertices]
    posed=[]
    for vertex,point in zip(original.data.vertices,source_points):
        fitted=Vector();total=0.
        for influence in vertex.groups:
            if influence.weight<1e-6:continue
            origin,to,transform=transforms[key(original.vertex_groups[influence.group].name)]
            fitted+=(to+Vector(transform@np.array(point-origin)))*influence.weight;total+=influence.weight
        assert total>.99,('Incomplete source weights',vertex.index,total)
        posed.append(fitted/total)
    tree=KDTree(len(source_points))
    for index,point in enumerate(source_points):tree.insert(point,index)
    tree.balance()
    for old in imported:bpy.data.objects.remove(old,do_unlink=True)
    before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(prepared));imported=set(bpy.data.objects)-before
    obj=next(o for o in imported if o.type=='MESH')
    obj.data.transform(obj.matrix_world);obj.parent=None;obj.matrix_world=Matrix.Identity(4)
    source_indices=[];maximum=0.
    for vertex in obj.data.vertices:
        _,index,distance=tree.find(vertex.co);maximum=max(maximum,distance)
        assert distance<.0002,('Prepared source no longer matches original source geometry',distance)
        source_indices.append(index);vertex.co=posed[index]
    for old in imported:
        if old!=obj:bpy.data.objects.remove(old,do_unlink=True)
    obj.name=row['art_id'];obj.data.update()
    return obj,dict(method='Anatomical joint-head frames; source skin used only for placement, final weights must come from native body',
        source_vertex_correspondence=source_indices,prepared_source_match_max_m=maximum)
