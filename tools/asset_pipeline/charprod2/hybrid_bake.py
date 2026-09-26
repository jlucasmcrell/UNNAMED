"""The hybrid player's texture transfer (charprod2): the source character's surface (hybrid_fit.py registered it) baked onto the MPFB
body's own UV layouts - Cycles selected-to-active, the source's albedo and a per-material ID (so a composite keeps only what landed on
the right kind of surface), and the region masks the composite needs. Also the source's face rendered straight on through the face
fit's camera, as the reference for the landmark warp (solve_face_fit.py, warp_face.py, mpfb_bake_face.py).

    blender -b --python hybrid_bake.py -- --blend fitted.blend --work <dir> [--body-size 4096] [--size 2048]

Writes <work>/bake/: hair_{albedo,normal,rough}.png; body_head_normal.png (the head's sculpt detail, tangent space); {vest,trousers,boots}_{albedo,id}.png; body_{albedo,id}.png (the torso-registered
source: arms), body_head_{albedo,id}.png (the head-registered source: scalp), body_masks.png (R arms and shoulders, G scalp, B head and neck), body_ao.png; and
<work>/facefit/source_face.png.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
import addon_utils
from mathutils import Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
work = os.path.abspath(arg("--work"))
body_size, size = int(arg("--body-size", 4096)), int(arg("--size", 2048))
bake_dir = os.path.join(work, "bake")
os.makedirs(bake_dir, exist_ok=True)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
src = bpy.data.objects["source"]
hair = bpy.data.objects["Human.hair_source"]
fit = json.load(open(os.path.splitext(os.path.abspath(arg("--blend")))[0] + "_fit.json"))
garments = {k: bpy.data.objects[f"Human.{k}"] for k in ("vest", "trousers", "boots") if f"Human.{k}" in bpy.data.objects}

# The head-registered copy of the source (the hair was cut from it; the scalp and the face reference come from it).
head_src = src.copy()
head_src.data = src.data.copy()
head_src.name = "source_head"
bpy.context.scene.collection.objects.link(head_src)
head_src.data.transform(Matrix(fit["head_T"]))

ID = {"skin": (1, 0, 0), "cloth_heavy": (0, 0, 1), "cloth": (0, 1, 0), "leather": (1, 1, 0), "hair": (1, 0, 1), "eye": (0, 1, 1)}


def kind_of(mat_name):
    n = mat_name.lower()
    return next((k for k in ID if k in n), "skin")


def image_named(mat, key):
    for n in mat.node_tree.nodes:
        if n.type == "TEX_IMAGE" and n.image and key in n.image.name.lower():
            return n.image
    return None


def base_image(mat):
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf and bsdf.inputs["Base Color"].is_linked:
        n = bsdf.inputs["Base Color"].links[0].from_node
        while n.type != "TEX_IMAGE" and n.inputs and any(i.is_linked for i in n.inputs):
            n = next(i for i in n.inputs if i.is_linked).links[0].from_node
        if n.type == "TEX_IMAGE":
            return n.image
    return image_named(mat, "albedo")


def emission(name, image=None, colour=None, channel=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(em.outputs[0], out.inputs[0])
    if image is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        if channel is None:
            nt.links.new(tex.outputs["Color"], em.inputs["Color"])
        else:
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            nt.links.new(tex.outputs["Color"], sep.inputs[0])
            nt.links.new(sep.outputs[channel], em.inputs["Color"])
    else:
        em.inputs["Color"].default_value = (*colour, 1)
    return m


originals = {o.name: list(o.data.materials) for o in (src, head_src)}


def dress(obj, mode):
    mats = originals[obj.name]
    for i, m in enumerate(mats):
        if mode == "original":
            new = m
        elif mode == "albedo":
            new = emission(f"bk_alb_{m.name}", image=base_image(m), colour=(0, 0, 0))
        elif mode == "id":
            new = emission(f"bk_id_{m.name}", colour=ID[kind_of(m.name)])
        elif mode == "rough":
            orm = image_named(m, "orm")
            new = emission(f"bk_r_{m.name}", image=orm, colour=(0.6, 0.6, 0.6), channel=1)
        obj.data.materials[i] = new


scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "GPU"
scene.cycles.samples = 1


def target_image(obj, name, res, non_color=False):
    img = bpy.data.images.new(name, res, res, alpha=False, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    for m in obj.data.materials:
        node = m.node_tree.nodes.new("ShaderNodeTexImage")
        node.name = "bake_target"
        node.image = img
        m.node_tree.nodes.active = node
    return img


def clear_targets(obj):
    for m in obj.data.materials:
        for n in [n for n in m.node_tree.nodes if n.name.startswith("bake_target")]:
            m.node_tree.nodes.remove(n)


def bake(target, source, name, bake_type="EMIT", res=2048, non_color=False, uv=None, extrusion=0.02, distance=0.06):
    img = target_image(target, name, res, non_color)
    bpy.ops.object.select_all(action="DESELECT")
    kwargs = {"type": bake_type, "margin": 8, "use_clear": True}
    if source is not None:
        source.hide_render = False
        source.select_set(True)
        kwargs.update(use_selected_to_active=True, cage_extrusion=extrusion, max_ray_distance=distance)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    if uv:
        kwargs["uv_layer"] = uv
    if bake_type == "NORMAL":
        kwargs["normal_space"] = "TANGENT"
    bpy.ops.object.bake(**kwargs)
    img.filepath_raw = os.path.join(bake_dir, name + ".png")
    img.file_format = "PNG"
    img.save()
    clear_targets(target)
    if source is not None:
        source.hide_render = True
    print("BAKED", name)


hide = [o for o in bpy.data.objects if o.type == "MESH"]
for o in hide:
    o.hide_render = True
for o in [body, hair, *garments.values()]:
    o.hide_render = False

# Hair: its own faces, so the rays are exact.
dress(head_src, "original")
bake(hair, head_src, "hair_normal", "NORMAL", size, non_color=True, uv="hairUV", extrusion=0.002, distance=0.01)
dress(head_src, "albedo")
bake(hair, head_src, "hair_albedo", res=size, uv="hairUV", extrusion=0.002, distance=0.01)
dress(head_src, "rough")
bake(hair, head_src, "hair_rough", res=size, non_color=True, uv="hairUV", extrusion=0.002, distance=0.01)

# The head's fine sculpt (the source's own normal map and the detail the wrap's resolution cannot carry) on the body's UVs.
dress(head_src, "original")
bake(body, head_src, "body_head_normal", "NORMAL", body_size, non_color=True, extrusion=0.006, distance=0.012)

# Garments and the arms from the torso-registered source; the scalp from the head-registered one.
for mode in ("albedo", "id"):
    dress(src, mode)
    dress(head_src, mode)
    for k, g in garments.items():
        bake(g, src, f"{k}_{mode}", res=size, extrusion=0.05, distance=0.10)
    bake(body, src, f"body_{mode}", res=body_size, extrusion=0.05, distance=0.10)
    bake(body, head_src, f"body_head_{mode}", res=body_size, extrusion=0.015, distance=0.04)

# Region masks on the body's own UVs: R the arms (shoulders, upper arms, forearms, hands), B the head and neck, G the scalp (MPFB's scalp group and behind
# the ears, the hairline's own region; the face projection masks the face later).
groups = {g.index: g.name for g in body.vertex_groups}
arm_names = ("upperarm", "lowerarm", "hand", "clavicle")
# The body as shaped (MPFB shapes it through shape keys; the base mesh's own coordinates are the neutral figure), at rest, masks off.
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
posed = {pb.name: pb.matrix_basis.copy() for pb in rig.pose.bones}
for pb in rig.pose.bones:
    pb.matrix_basis.identity()
masks_on = [m for m in body.modifiers if m.type == "MASK" and m.show_viewport]
for m in masks_on:
    m.show_viewport = False
bpy.context.view_layer.update()
e = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
me = e.to_mesh()
co = np.array([tuple(body.matrix_world @ v.co) for v in me.vertices])
e.to_mesh_clear()
for m in masks_on:
    m.show_viewport = True
for pb in rig.pose.bones:
    pb.matrix_basis = posed[pb.name]
bpy.context.view_layer.update()
top = co[:, 2].max()
head_y = (rig.matrix_world @ rig.data.bones["head"].head_local).y
attr = body.data.color_attributes.new("bake_masks", "FLOAT_COLOR", "POINT")
for v in body.data.vertices:
    arm = sum(g.weight for g in v.groups if groups.get(g.group, "").startswith(arm_names))
    head = sum(g.weight for g in v.groups if groups.get(g.group, "") == "head")
    z, y = co[v.index, 2], co[v.index, 1]
    # The scalp: MPFB's own authored "scalp" group, and the back of the head behind the ears (a height plane reached the brows).
    scalp_w = sum(g.weight for g in v.groups if groups.get(g.group, "") == "scalp")
    behind = np.clip((y - (head_y + 0.02)) / 0.02, 0, 1) * np.clip((z - (top - 0.20)) / 0.02, 0, 1)
    scalp = max(scalp_w, head * float(behind))
    neck = sum(g.weight for g in v.groups if groups.get(g.group, "").startswith("neck"))
    attr.data[v.index].color = (min(arm, 1.0), min(scalp, 1.0), min(head + neck, 1.0), 1)
mm = bpy.data.materials.new("bk_masks")
mm.use_nodes = True
nt = mm.node_tree
nt.nodes.clear()
o_ = nt.nodes.new("ShaderNodeOutputMaterial")
em = nt.nodes.new("ShaderNodeEmission")
ca = nt.nodes.new("ShaderNodeVertexColor")
ca.layer_name = "bake_masks"
nt.links.new(ca.outputs["Color"], em.inputs["Color"])
nt.links.new(em.outputs[0], o_.inputs[0])
body_mats = list(body.data.materials)
for i in range(len(body_mats)):
    body.data.materials[i] = mm
bake(body, None, "body_masks", res=body_size, non_color=True)
# Ambient occlusion on the body's own UVs (the standard's occlusion map): nostrils, lip corners, eye sockets, ears.
for o in bpy.data.objects:
    if o.type == "MESH" and o is not body:
        o.hide_render = True
scene.cycles.samples = 64
bake(body, None, "body_ao", "AO", res=body_size, non_color=True)
scene.cycles.samples = 1
for i, m in enumerate(body_mats):
    body.data.materials[i] = m

# The face reference: the head-registered source, unlit albedo, through the face fit's orthographic camera.
dress(head_src, "albedo")
info = json.load(open(os.path.join(work, "facefit", "probe.json")))["camera"]
cam = bpy.data.objects.new("refcam", bpy.data.cameras.new("refcam"))
scene.collection.objects.link(cam)
cam.data.type = "ORTHO"
cam.data.ortho_scale = info["scale"]
cam.location = info["center"]
cam.rotation_euler = (math.radians(90), 0, 0)
scene.camera = cam
scene.render.resolution_x = scene.render.resolution_y = info["res"]
scene.render.film_transparent = True
scene.view_settings.view_transform = "Standard"
for o in bpy.data.objects:
    if o.type == "MESH":
        o.hide_render = o is not head_src
scene.cycles.samples = 16
scene.render.filepath = os.path.join(work, "facefit", "source_face.png")
bpy.ops.render.render(write_still=True)
print("HYBRID_BAKE", bake_dir)
