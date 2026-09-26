"""The hybrid player assembled (charprod2): hybrid_fit.py's .blend back in its rest pose, the registered source removed, the composited
textures (compose_hybrid.py, <work>/tex_hybrid) on the body and garments, and the source hair's own material (baked albedo, normal,
roughness on its repacked UVs, now its only UV layer).

    blender -b --python hybrid_finish.py -- --blend fitted.blend --work <dir> --spec specs/player.json --out hybrid.blend
                                             [--eye-saturation 0.35]
"""
import json
import os
import sys

import bpy
import numpy as np
import addon_utils

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
work = os.path.abspath(arg("--work"))
spec = json.load(open(arg("--spec")))
prefix = spec.get("material_prefix", "MAT_" + spec["id"])
tex = os.path.join(work, "tex_hybrid")
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for pb in rig.pose.bones:
    pb.matrix_basis.identity()
for name in ("source", "source_head"):
    if name in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[name])


def swap_image(obj, path):
    img = bpy.data.images.load(path)
    for m in obj.data.materials:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image and n.image.colorspace_settings.name != "Non-Color":
                n.image = img
                return


body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
swap_image(body, os.path.join(tex, "body_albedo.png"))
bm = body.data.materials[0]
bsdf_b = next(n for n in bm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
if not bsdf_b.inputs["Normal"].is_linked and os.path.exists(os.path.join(tex, "body_normal.png")):
    nimg = bm.node_tree.nodes.new("ShaderNodeTexImage")
    nimg.image = bpy.data.images.load(os.path.join(tex, "body_normal.png"))
    nimg.image.colorspace_settings.name = "Non-Color"
    nmap_b = bm.node_tree.nodes.new("ShaderNodeNormalMap")
    bm.node_tree.links.new(nimg.outputs["Color"], nmap_b.inputs["Color"])
    bm.node_tree.links.new(nmap_b.outputs["Normal"], bsdf_b.inputs["Normal"])
for k in ("vest", "trousers", "boots"):
    obj = bpy.data.objects.get(f"Human.{k}")
    if obj is not None:
        swap_image(obj, os.path.join(tex, f"{k}.png"))

# The iris toward grey (the source's eyes are grey-blue; MPFB's blue alone reads saturated), written as a texture so it exports.
eye_sat = float(arg("--eye-saturation", 0.35))
for o in bpy.data.objects:
    if o.type == "MESH" and ObjectService.get_object_type(o) == "Eyes":
        for m in o.data.materials:
            for n in m.node_tree.nodes if m and m.node_tree else []:
                if n.type == "TEX_IMAGE" and n.image and n.image.name.lower().endswith("_eye.png"):
                    px = np.empty(n.image.size[0] * n.image.size[1] * 4, np.float32)
                    n.image.pixels.foreach_get(px)
                    px = px.reshape(-1, 4)
                    grey = px[:, :3] @ np.array([0.299, 0.587, 0.114], np.float32)
                    px[:, :3] = grey[:, None] + eye_sat * (px[:, :3] - grey[:, None])
                    img = bpy.data.images.new("eye_hybrid", n.image.size[0], n.image.size[1], alpha=True)
                    img.pixels.foreach_set(px.ravel())
                    img.filepath_raw = os.path.join(tex, "eye.png")
                    img.file_format = "PNG"
                    img.save()
                    # Loaded back from its file: a generated image's pixels are not kept in the .blend.
                    n.image = bpy.data.images.load(os.path.join(tex, "eye.png"), check_existing=False)
                    bpy.data.images.remove(img)

hair = bpy.data.objects["Human.hair_source"]
for uv in [uv for uv in hair.data.uv_layers if uv.name != "hairUV"]:
    hair.data.uv_layers.remove(uv)
mat = bpy.data.materials.new(prefix + "_hair")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]


def image_node(name, non_color=False):
    n = nt.nodes.new("ShaderNodeTexImage")
    n.image = bpy.data.images.load(os.path.join(tex, name + ".png"))
    if non_color:
        n.image.colorspace_settings.name = "Non-Color"
    return n


nt.links.new(image_node("hair_albedo").outputs["Color"], bsdf.inputs["Base Color"])
rough = image_node("hair_rough", True)
sep = nt.nodes.new("ShaderNodeSeparateColor")
nt.links.new(rough.outputs["Color"], sep.inputs[0])
nt.links.new(sep.outputs[0], bsdf.inputs["Roughness"])
nmap = nt.nodes.new("ShaderNodeNormalMap")
nt.links.new(image_node("hair_normal", True).outputs["Color"], nmap.inputs["Color"])
nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
hair.data.materials.clear()
hair.data.materials.append(mat)
for p in hair.data.polygons:
    p.material_index = 0
hair.name = "Human.hair"
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(arg("--out")))
print("HYBRID_FINISH", os.path.abspath(arg("--out")))
