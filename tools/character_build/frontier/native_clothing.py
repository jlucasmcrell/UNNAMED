"""Native authoring cleanup and real opening tags; no fitting/drape solver."""
import bpy,bmesh

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
