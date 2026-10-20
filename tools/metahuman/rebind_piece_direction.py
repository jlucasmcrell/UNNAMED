"""Rest-pose rebind: retarget one armour piece from the pack's tall-male rest pose onto a target body.

The DevonLux sets are bound to the pack's own MetaHuman. Bone names match, rest positions do not, so
binding by name distorts dense areas (feet, hands). This stage poses the piece's armature to the
target's rest pose in hierarchy order, bakes the deformation, and parents the mesh to the target's
armature with the author's weights kept.

  blender --background --factory-startup --python rebind_piece.py -- <piece.glb> <body.glb> <out.glb>
"""

import sys

import bpy
from mathutils import Matrix


def order_by_hierarchy(armature):
    ordered = []

    def walk(bone):
        ordered.append(bone.name)
        for child in bone.children:
            walk(child)

    for bone in armature.data.bones:
        if bone.parent is None:
            walk(bone)
    return ordered


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    piece_path, body_path, out_path = args[0], args[1], args[2]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=piece_path)
    piece_arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    piece_meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not piece_arms or not piece_meshes:
        print("REBIND missing armature or mesh in %s" % piece_path)
        return
    piece_arm = piece_arms[0]

    bpy.ops.import_scene.gltf(filepath=body_path)
    body_arms = [o for o in bpy.context.scene.objects
                 if o.type == "ARMATURE" and o is not piece_arm]
    if not body_arms:
        print("REBIND no target armature in %s" % body_path)
        return
    body_arm = body_arms[-1]

    body_arm.animation_data_clear()
    for bone in body_arm.pose.bones: bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    # Pose the piece's armature to the target's rest pose, parents before children.
    moved = 0
    for name in order_by_hierarchy(piece_arm):
        target_bone = body_arm.pose.bones.get(name)
        source_bone = piece_arm.pose.bones.get(name)
        if target_bone is None or source_bone is None:
            continue
        source_world = piece_arm.matrix_world @ source_bone.matrix
        target_world = body_arm.matrix_world @ body_arm.data.bones[name].matrix_local
        children = [b for b in source_bone.children if b.name in body_arm.data.bones]
        preferred = {'pelvis':'spine_01'}
        for side in ['l','r']:
            preferred.update({'thigh_'+side:'calf_'+side,'calf_'+side:'foot_'+side,'foot_'+side:'ball_'+side})
        child = next((b for b in children if b.name == preferred.get(name)), None)
        if child is None:
            child = next((b for b in children if 'twist' not in b.name and 'corrective' not in b.name), None)
        rotation = Matrix.Identity(4)
        if child is not None:
            source_direction = (piece_arm.matrix_world @ child.matrix).translation - source_world.translation
            target_direction = (body_arm.matrix_world @ body_arm.data.bones[child.name].matrix_local).translation - target_world.translation
            if source_direction.length > 1e-7 and target_direction.length > 1e-7:
                rotation = source_direction.rotation_difference(target_direction).to_matrix().to_4x4()
        posed_world = Matrix.Translation(target_world.translation) @ rotation @ Matrix.Translation(-source_world.translation) @ source_world
        source_bone.matrix = piece_arm.matrix_world.inverted() @ posed_world
        bpy.context.view_layer.update()
        moved += 1

    # Bake the deformation into the mesh before the piece's armature goes away.
    for mesh in piece_meshes:
        bpy.context.view_layer.objects.active = mesh
        mesh.select_set(True)
        for modifier in list(mesh.modifiers):
            if modifier.type == "ARMATURE":
                bpy.ops.object.modifier_apply(modifier=modifier.name)
        mesh.select_set(False)

    # Re-parent to the target armature, keeping the vertex groups.
    for mesh in piece_meshes:
        mesh.parent = body_arm
        mesh.matrix_parent_inverse = body_arm.matrix_world.inverted()
        modifier = mesh.modifiers.new("Armature", "ARMATURE")
        modifier.object = body_arm

    bpy.data.objects.remove(piece_arm, do_unlink=True)
    for extra in list(bpy.context.scene.objects):
        if extra.type == "MESH" and extra not in piece_meshes:
            bpy.data.objects.remove(extra, do_unlink=True)

    bpy.ops.object.select_all(action="DESELECT")
    for mesh in piece_meshes:
        mesh.select_set(True)
    body_arm.select_set(True)
    bpy.context.view_layer.objects.active = body_arm
    bpy.ops.export_scene.gltf(filepath=out_path, use_selection=True, export_skins=True,
                              export_apply=False)
    print("REBIND ok pieces=%d bones_posed=%d -> %s" % (len(piece_meshes), moved, out_path))


main()
