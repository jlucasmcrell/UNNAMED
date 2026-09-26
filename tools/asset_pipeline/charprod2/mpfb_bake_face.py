"""The warped reference face (warp_face.py) projected from the fit camera onto an MPFB body and baked into its own UV layout: the
colour, and a mask (the face region times how squarely each surface faces the camera). Also the skin's own diffuse texture, for
the composite that follows (composite_skin.py).

    blender -b --python mpfb_bake_face.py -- --blend base.blend --dir <facefit dir> [--size 4096]
"""
import json
import math
import os
import sys

import bpy
import addon_utils
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
blend, folder, size = arg("--blend"), os.path.abspath(arg("--dir")), int(arg("--size", 4096))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=blend)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
info = json.load(open(os.path.join(folder, "probe.json")))["camera"]
cx, cy, cz = info["center"]
cam = bpy.data.objects.new("projcam", bpy.data.cameras.new("projcam"))
bpy.context.scene.collection.objects.link(cam)
cam.data.type = "ORTHO"
cam.data.ortho_scale = info["scale"]
cam.location = Vector((cx, cy, cz))
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam
scene = bpy.context.scene
scene.render.resolution_x = scene.render.resolution_y = info["res"]

main_uv = body.data.uv_layers.active.name
proj = body.data.uv_layers.new(name="proj")
body.data.uv_layers.active = body.data.uv_layers[main_uv]
mod = body.modifiers.new("proj", "UV_PROJECT")
mod.uv_layer = "proj"
mod.projector_count = 1
mod.projectors[0].object = cam
mod.aspect_x = mod.aspect_y = 1.0

skin_image = None
for m in body.data.materials:
    if m and m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image and "diffuse" in n.image.name.lower():
                skin_image = n.image
if skin_image is not None:
    skin_image.filepath_raw = os.path.join(folder, "skin_diffuse_src.png")
    skin_image.file_format = "PNG"
    skin_image.save()

bake_mat = bpy.data.materials.new("projbake")
bake_mat.use_nodes = True
nt = bake_mat.node_tree
nt.nodes.clear()
outn = nt.nodes.new("ShaderNodeOutputMaterial")
emit = nt.nodes.new("ShaderNodeEmission")
uvn = nt.nodes.new("ShaderNodeUVMap")
uvn.uv_map = "proj"
img = nt.nodes.new("ShaderNodeTexImage")
img.image = bpy.data.images.load(os.path.join(folder, "face_warped.png"))
img.extension = "EXTEND"
msk = nt.nodes.new("ShaderNodeTexImage")
msk.image = bpy.data.images.load(os.path.join(folder, "face_mask.png"))
msk.image.colorspace_settings.name = "Non-Color"
msk.extension = "CLIP"
geo = nt.nodes.new("ShaderNodeNewGeometry")
dot = nt.nodes.new("ShaderNodeVectorMath")
dot.operation = "DOT_PRODUCT"
dot.inputs[1].default_value = (0, -1, 0)
facing = nt.nodes.new("ShaderNodeMapRange")
facing.inputs["From Min"].default_value = 0.35
facing.inputs["From Max"].default_value = 0.8
mul = nt.nodes.new("ShaderNodeMath")
mul.operation = "MULTIPLY"
nt.links.new(uvn.outputs["UV"], img.inputs["Vector"])
nt.links.new(uvn.outputs["UV"], msk.inputs["Vector"])
nt.links.new(geo.outputs["Normal"], dot.inputs[0])
nt.links.new(dot.outputs["Value"], facing.inputs["Value"])
nt.links.new(facing.outputs["Result"], mul.inputs[0])
nt.links.new(msk.outputs["Color"], mul.inputs[1])
nt.links.new(emit.outputs["Emission"], outn.inputs["Surface"])
target = nt.nodes.new("ShaderNodeTexImage")
nt.nodes.active = target

originals = list(body.data.materials)
body.data.materials.clear()
body.data.materials.append(bake_mat)
for p in body.data.polygons:
    p.material_index = 0
for o in bpy.data.objects:
    if o is not body:
        o.hide_render = True
scene.render.engine = "CYCLES"
scene.cycles.device = "GPU"
scene.cycles.samples = 1
scene.render.bake.margin = 24
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
bpy.context.view_layer.objects.active = body
for name, source in (("face_proj_color", img.outputs["Color"]), ("face_proj_mask", mul.outputs["Value"])):
    image = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
    if name.endswith("mask"):
        image.colorspace_settings.name = "Non-Color"
    target.image = image
    nt.links.new(source, emit.inputs["Color"])
    bpy.ops.object.bake(type="EMIT", uv_layer=main_uv, use_clear=True, margin=24)
    image.filepath_raw = os.path.join(folder, name + ".png")
    image.file_format = "PNG"
    image.save()
print("BAKE_FACE", main_uv, skin_image.name if skin_image else None, size)
