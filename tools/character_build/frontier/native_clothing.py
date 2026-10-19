"""Native authoring cleanup and real opening tags; no fitting/drape solver."""
import bpy,bmesh

def arm_weights(obj):
    """Native skin groups distinguish sleeves from torso, including new seam vertices."""
    groups={g.index for g in obj.vertex_groups if g.name.startswith(
        ('upperarm','lowerarm','hand','index','middle','ring','pinky','thumb','wrist'))}
    return [sum(g.weight for g in v.groups if g.group in groups) for v in obj.data.vertices]

def approved_surface(fit, include_outfit=False):
    """Read-only authoring copies of BOTH native skin sections, including the head's neck/shoulders."""
    copies=[]
    bpy.ops.object.select_all(action='DESELECT')
    for suffix in (('body','head','outfit') if include_outfit else ('body','head')):
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
    """The hanging mantle follows the spine, so arm lifts cannot flare its hem."""
    _bind_primary(obj,arm,{'spine_01','spine_02','spine_03','spine_04','spine_05','neck_01'})

def round_hood_crown(obj, head):
    """Native source-pattern wrap rounds the peak before adding cloth thickness."""
    top=max((head.matrix_world@v.co).z for v in head.data.vertices)
    rear=max((head.matrix_world@v.co).y for v in head.data.vertices if (head.matrix_world@v.co).z>top-.16)
    # The pointed excess is behind the cranium, not necessarily above it.
    # Fold only that source-pattern tip down, then keep it outside the head.
    tail=obj.vertex_groups.new(name='Fallen rear hood tip');tail_name=tail.name
    for vertex in obj.data.vertices:
        point=obj.matrix_world@vertex.co
        weight=max(0,min(1,(point.y-rear-.015)/.08))*max(0,min(1,(point.z-top+.15)/.06))
        if weight:tail.add([vertex.index],weight,'REPLACE')
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    drop=obj.modifiers.new('Native rear tip fall','DISPLACE');drop.direction='Z';drop.strength=-.18;drop.mid_level=0;drop.vertex_group=tail_name
    bpy.ops.object.modifier_apply(modifier=drop.name)
    outside=obj.modifiers.new('Fold outside approved head','SHRINKWRAP');outside.target=head
    outside.wrap_method='NEAREST_SURFACEPOINT';outside.wrap_mode='OUTSIDE';outside.offset=.025;outside.vertex_group=tail_name
    bpy.ops.object.modifier_apply(modifier=outside.name)
    obj.vertex_groups.remove(obj.vertex_groups[tail_name])
    group=obj.vertex_groups.new(name='Rounded source hood crown')
    for vertex in obj.data.vertices:
        z=(obj.matrix_world@vertex.co).z
        weight=max(0,min(1,(z-top-.01)/.06))
        if weight:group.add([vertex.index],weight,'REPLACE')
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    wrap=obj.modifiers.new('Native rounded crown','SHRINKWRAP');wrap.target=head
    wrap.wrap_method='NEAREST_SURFACEPOINT';wrap.wrap_mode='ON_SURFACE';wrap.offset=.04;wrap.vertex_group=group.name
    group_name=group.name
    bpy.ops.object.modifier_apply(modifier=wrap.name)
    if obj.vertex_groups.get(group_name):obj.vertex_groups.remove(obj.vertex_groups[group_name])
    # Ear recesses make the open head mesh unsuitable as an outside test.
    # A native convex collision envelope covers its outermost skin points.
    edit=bmesh.new()
    for vertex in head.data.vertices:edit.verts.new(head.matrix_world@vertex.co)
    bmesh.ops.convex_hull(edit,input=list(edit.verts),use_existing_faces=False)
    bmesh.ops.delete(edit,geom=[v for v in edit.verts if not v.link_faces],context='VERTS')
    mesh=bpy.data.meshes.new('Read-only head clearance envelope');edit.to_mesh(mesh);edit.free()
    envelope=bpy.data.objects.new(mesh.name,mesh);bpy.context.collection.objects.link(envelope)
    clearance=obj.modifiers.new('Native hood ear clearance','SHRINKWRAP');clearance.target=envelope
    clearance.wrap_method='NEAREST_SURFACEPOINT';clearance.wrap_mode='OUTSIDE';clearance.offset=.018
    bpy.ops.object.modifier_apply(modifier=clearance.name)
    bpy.data.objects.remove(envelope,do_unlink=True);bpy.data.meshes.remove(mesh)
