"""A built character exported for the game (charstd): MPFB's helper geometry removed from the body (vertex deletion, so the face
channels survive - applying the mask modifier would drop every shape key), the identity and macro targets baked into the basis, the
rig in its rest pose, and the whole character as one .glb with skins and morph targets (the face channels by their ARKit / viseme
names only).

    blender -b --python export_character.py -- --blend character.blend --out character.glb
"""
import os
import sys

import bpy
import bmesh
import numpy as np
import addon_utils

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services.faceservice import ARKIT_FACEUNITS, META_VISEMES, MICROSOFT_VISEMES  # noqa: E402

CHANNELS = set(ARKIT_FACEUNITS) | set(META_VISEMES) | set(MICROSOFT_VISEMES)
for o in bpy.data.objects:
    if o.type != "MESH":
        continue
    keep = o.vertex_groups.get("body")   # MPFB's own group of the real body; everything else is helper geometry and joint cubes
    for m in [m for m in o.modifiers if m.type == "MASK"]:
        o.modifiers.remove(m)
    if keep is not None:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        dl = bm.verts.layers.deform.active
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if keep.index not in v[dl]], context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
    # The identity and macro targets are baked into the basis at their values (they are the character's shape, not animation);
    # only the face channels stay morph targets.
    if o.data.shape_keys:
        blocks = o.data.shape_keys.key_blocks
        read = lambda kb: np.array([tuple(d.co) for d in kb.data], np.float64)  # noqa: E731
        base = read(blocks[0])
        baked = [k for k in blocks[1:] if k.name not in CHANNELS]
        total = sum(((read(k) - base) * k.value for k in baked), np.zeros_like(base))   # every delta against the original basis
        for k in [blocks[0]] + [k for k in blocks[1:] if k.name in CHANNELS]:
            k.data.foreach_set("co", (read(k) + total).astype(np.float32).ravel())
        for k in baked:
            o.shape_key_remove(k)
        for k in o.data.shape_keys.key_blocks[1:]:
            k.value = 0.0
# Materials as the game draws them: skin, eyes, teeth and tongue opaque; hair, brows and lashes alpha-tested (MPFB's materials
# carry a linked alpha everywhere, which glTF exports as BLEND - sorted, see-through skin in the engine).
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402
def images(tree, seen=None):
    """Every image in a node tree, groups included (MPFB's enhanced skin hides its textures inside node groups)."""
    for n in tree.nodes:
        if n.type == "TEX_IMAGE" and n.image:
            yield n.image
        elif n.type == "GROUP" and n.node_tree:
            yield from images(n.node_tree)


def simple_material(name, colour, normal=None, roughness=0.6, cutout=False):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = colour
    nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    if cutout:
        gt = nt.nodes.new("ShaderNodeMath")
        gt.operation = "GREATER_THAN"
        gt.inputs[1].default_value = 0.5
        nt.links.new(t.outputs["Alpha"], gt.inputs[0])
        nt.links.new(gt.outputs[0], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = roughness
    if normal is not None:
        ni = nt.nodes.new("ShaderNodeTexImage")
        ni.image = normal
        ni.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(ni.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return m


# The skin as one plain material (the enhanced skin's node groups do not translate to glTF: the exporter picked a wrong image and
# exported it BLEND): its diffuse texture and normal map, one material for every slot of the body.
for o in bpy.data.objects:
    if o.type == "MESH" and ObjectService.object_is_basemesh(o):
        imgs = {i.name: i for m in o.data.materials if m and m.node_tree for i in images(m.node_tree)}
        diffuse = next((i for n, i in imgs.items() if "diffuse" in n.lower() or "albedo" in n.lower()), None)
        normal = next((i for n, i in imgs.items() if "normal" in n.lower() or "nrm" in n.lower()), None)
        if diffuse is not None:
            skin = simple_material(o.name.split(".")[0] + "_skin", diffuse, normal, 0.55)
            o.data.materials.clear()
            o.data.materials.append(skin)
            for poly in o.data.polygons:
                poly.material_index = 0
    elif o.type == "MESH" and ObjectService.get_object_type(o) == "Eyes":
        # The eyes likewise: the iris/sclera texture on a plain glossy material, alpha-tested (the cornea shell's texels are clear).
        iris = next((i for m in o.data.materials if m and m.node_tree for i in images(m.node_tree) if i.name.lower().endswith("_eye.png")
                     or "eye" in i.name.lower()), None)
        if iris is not None:
            eye = simple_material(o.name.split(".")[0] + "_eyes", iris, None, 0.15, cutout=True)   # the cornea shell is clear
            o.data.materials.clear()
            o.data.materials.append(eye)
            for poly in o.data.polygons:
                poly.material_index = 0
for o in bpy.data.objects:
    if o.type != "MESH":
        continue
    cutout = ObjectService.get_object_type(o) in ("Hair", "Eyebrows", "Eyelashes", "Eyes")
    for m in o.data.materials:
        if not (m and m.node_tree):
            continue
        bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None or not bsdf.inputs["Alpha"].is_linked:
            continue
        src = bsdf.inputs["Alpha"].links[0].from_socket
        if src.node.type == "MATH" and src.node.operation == "GREATER_THAN":
            continue   # already alpha-tested (the eyes' plain material)
        m.node_tree.links.remove(bsdf.inputs["Alpha"].links[0])
        if cutout:
            gt = m.node_tree.nodes.new("ShaderNodeMath")
            gt.operation = "GREATER_THAN"
            gt.inputs[1].default_value = 0.5
            m.node_tree.links.new(src, gt.inputs[0])
            m.node_tree.links.new(gt.outputs[0], bsdf.inputs["Alpha"])
        else:
            bsdf.inputs["Alpha"].default_value = 1.0
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for pb in rig.pose.bones:
    pb.matrix_basis.identity()
out = os.path.abspath(arg("--out"))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_skins=True, export_morph=True, export_morph_normal=False,
                          export_apply=False, export_animations=False, export_yup=True)
print("EXPORT_CHARACTER", out, os.path.getsize(out))
