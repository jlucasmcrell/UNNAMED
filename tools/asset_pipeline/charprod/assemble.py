"""Blender: assemble a production character GLB - the laid-out skinned mesh (TEXCOORD_0 only), its materials built
from the baked maps, tangents written (the MikkTSpace basis the normal map was baked in), the skeleton unchanged.

Materials: one per zone when --zones is given (a JSON {material name: {"faces": [face indices], ...params}} from the
zoning stage), else one. Every material samples the same atlas maps; zones differ in their parameters (the engine
side upgrades them by name - skin, hair, eye, cloth, leather, metal).

    blender --background --factory-startup --python assemble.py -- --input layout.glb --albedo a.png --normal n.png
        --orm orm.png --out character_rigged.glb [--name MAT] [--zones zones.json]
"""
import argparse
import json
import sys

import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree


def args():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--albedo", required=True)
    ap.add_argument("--normal", required=True)
    ap.add_argument("--orm", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--name", default="MAT_character")
    ap.add_argument("--zones")
    ap.add_argument("--eyes", help="eyes.py's eyeball GLB: joined in, each ball on its own bone under the head")
    ap.add_argument("--eye-texture")
    ap.add_argument("--head-bone", default="head")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:])


def add_eyes(a, obj):
    """Eye bones (eye.L / eye.R, children of the head, pointing along each gaze) and the eyeballs bound to them."""
    arm = obj.find_armature()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=a.eyes)
    balls = [o for o in set(bpy.data.objects) - before if o.type == "MESH"]
    for o in set(bpy.data.objects) - before:
        if o.type != "MESH":
            bpy.data.objects.remove(o)
    eye = bpy.data.materials.new("eye")
    eye.use_nodes = True
    nt = eye.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(a.eye_texture)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.08
    bsdf.inputs["Metallic"].default_value = 0.0
    bpy.context.view_layer.objects.active = arm
    for o in bpy.data.objects:
        o.select_set(o is arm)
    bpy.ops.object.mode_set(mode="EDIT")
    inv = arm.matrix_world.inverted()
    for ball in balls:
        side = ball.name.split(".")[-1][:1]
        centre = Vector(ball["centre"])
        gaze = Vector(ball["gaze"])
        bone = arm.data.edit_bones.new(f"eye.{side}")
        bone.head = inv @ centre
        bone.tail = inv @ (centre + gaze * 0.02)
        bone.parent = arm.data.edit_bones[a.head_bone]
    bpy.ops.object.mode_set(mode="OBJECT")
    for ball in balls:
        side = ball.name.split(".")[-1][:1]
        group = ball.vertex_groups.new(name=f"eye.{side}")
        group.add(list(range(len(ball.data.vertices))), 1.0, "REPLACE")
        ball.data.materials.clear()
        ball.data.materials.append(eye)
        ball.data.uv_layers[0].name = obj.data.uv_layers[0].name
    for o in bpy.data.objects:
        o.select_set(o in balls or o is obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.join()
    return len(balls)


def material(name, maps, params):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]

    def tex(path, colour):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = maps.setdefault(path, bpy.data.images.load(path))
        n.image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        return n

    albedo = tex(maps["albedo"], True)
    nt.links.new(albedo.outputs["Color"], bsdf.inputs["Base Color"])
    orm = tex(maps["orm"], False)
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(orm.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    # glTF occlusion: the exporter picks the R channel up through a glTF Material Output group.
    group = bpy.data.node_groups.get("glTF Material Output") or bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
    if "Occlusion" not in [s.name for s in group.interface.items_tree]:
        group.interface.new_socket("Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
    gnode = nt.nodes.new("ShaderNodeGroup")
    gnode.node_tree = group
    nt.links.new(sep.outputs["Red"], gnode.inputs["Occlusion"])
    normal = tex(maps["normal"], False)
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(normal.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    if "roughness_scale" in params:
        mul = nt.nodes.new("ShaderNodeMath")
        mul.operation = "MULTIPLY"
        mul.inputs[1].default_value = params["roughness_scale"]
        nt.links.new(sep.outputs["Green"], mul.inputs[0])
        nt.links.new(mul.outputs[0], bsdf.inputs["Roughness"])
    if params.get("metallic") is not None:
        for l in list(bsdf.inputs["Metallic"].links):
            nt.links.remove(l)
        bsdf.inputs["Metallic"].default_value = params["metallic"]
    return mat


def main():
    a = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.input)
    obj = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups][0]
    for o in [o for o in bpy.data.objects if o.type == "MESH" and o is not obj]:
        bpy.data.objects.remove(o)
    me = obj.data
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[-1])
    maps = {"albedo": a.albedo, "normal": a.normal, "orm": a.orm}
    # Materials: one per zone (zones_to_mesh.py), named by the zone's material kind - the engine upgrades them by
    # that name (skin, hair, cloth, leather, metal) - all on the same atlas maps. Faces are matched to their zone by
    # centroid (an import may reorder them). Slots are assigned in place: clearing them resets every face's index.
    if a.zones:
        zm = json.load(open(a.zones))
        kinds = []
        for z in zm["zones"]:
            if z.get("material", z["name"]) not in kinds:
                kinds.append(z.get("material", z["name"]))
        kd = KDTree(len(zm["centroids"]))
        for i, c in enumerate(zm["centroids"]):
            kd.insert(Vector((c[0], -c[2], c[1])), i)  # glTF -> Blender axes
        kd.balance()
        zone_kind = [kinds.index(z.get("material", z["name"])) for z in zm["zones"]]
        face_kind = []
        for poly in me.polygons:
            _, i, _ = kd.find(poly.center)
            zi = zm["face_zone"][i]
            face_kind.append(zone_kind[zi - 1] if zi > 0 else 0)
        names = [f"{a.name}_{k}" for k in kinds]
    else:
        face_kind, names = [0] * len(me.polygons), [a.name]
    mats = [material(n, dict(maps), {}) for n in names]
    if not me.materials:
        me.materials.append(mats[0])
    while len(me.materials) < len(mats):
        me.materials.append(mats[len(me.materials)])
    for i, mt in enumerate(mats):
        me.materials[i] = mt
    for poly, k in zip(me.polygons, face_kind):
        poly.material_index = k
    balls = add_eyes(a, obj) if a.eyes else 0
    me = obj.data
    bpy.ops.export_scene.gltf(filepath=a.out, export_format="GLB", use_selection=False, export_apply=False, export_skins=True,
                              export_tangents=True, export_image_format="AUTO")
    print(f"ASSEMBLE_RESULT {{\"out\": \"{a.out}\", \"materials\": {len(me.materials)}, \"faces\": {len(me.polygons)}, \"eyes\": {balls}}}")


main()
