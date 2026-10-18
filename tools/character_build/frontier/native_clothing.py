"""Native authoring cleanup and real opening tags; no fitting/drape solver."""
import bpy,bmesh

def arm_weights(obj):
    """Native skin groups distinguish sleeves from torso, including new seam vertices."""
    groups={g.index for g in obj.vertex_groups if g.name.startswith(
        ('upperarm','lowerarm','hand','index','middle','ring','pinky','thumb','wrist'))}
    return [sum(g.weight for g in v.groups if g.group in groups) for v in obj.data.vertices]

def approved_surface(fit):
    """Read-only authoring copies of BOTH native skin sections, including the head's neck/shoulders."""
    copies=[]
    bpy.ops.object.select_all(action='DESELECT')
    for suffix in ('body','head'):
        original=bpy.data.objects['native_'+fit+'_'+suffix]
        duplicate=original.copy();duplicate.data=original.data.copy()
        duplicate.name='fitting_surface_'+suffix
        bpy.context.collection.objects.link(duplicate)
        duplicate.select_set(True);copies.append(duplicate)
    bpy.context.view_layer.objects.active=copies[0]
    bpy.ops.object.join()
    surface=copies[0];surface.name='fitting_surface_body_and_head'
    bpy.ops.object.select_all(action='DESELECT')
    return surface

def weld(obj):
    edit=bmesh.new();edit.from_mesh(obj.data)
    bmesh.ops.remove_doubles(edit,verts=list(edit.verts),dist=.000001)
    bmesh.ops.recalc_face_normals(edit,faces=list(edit.faces))
    edit.to_mesh(obj.data);edit.free();obj.data.update()


def _bind_primary(obj, arm, allowed, affected=None):
    allowed=set(allowed)&set(arm.data.bones.keys())
    names={g.index:g.name for g in obj.vertex_groups}
    mapping={}
    for name in names.values():
        bone=arm.data.bones.get(name)
        while bone and bone.name not in allowed:bone=bone.parent
        mapping[name]=bone.name if bone else 'spine_04'
    weights=[]
    for vertex in obj.data.vertices:
        values={}
        for influence in vertex.groups:
            source=names[influence.group]
            name=mapping[source] if affected is None or vertex.index in affected else source
            values[name]=values.get(name,0.)+influence.weight
        total=sum(values.values());assert total>0
        weights.append({name:value/total for name,value in values.items() if value>0})
    obj.vertex_groups.clear()
    for name in sorted({name for values in weights for name in values}):obj.vertex_groups.new(name=name)
    for index,values in enumerate(weights):
        for name,value in values.items():obj.vertex_groups[name].add([index],value,'REPLACE')


def bind_mantle_torso(obj, arm):
    """A short mantle follows primary torso/shoulder bones, not skin corrective helpers."""
    _bind_primary(obj,arm,{'spine_01','spine_02','spine_03','spine_04','spine_05','clavicle_l','clavicle_r','neck_01'})
