"""Render a rigged character in poses measured by _skin_deform_check.py, from every side, textured and clay.

The poses are the skinned vertex positions `_skin_deform_check.py --dump` wrote (the game's own playback:
rest-relative retarget by bone name, glTF skinning), so what is rendered is what was measured - including a
clip borrowed from another skeleton. Views are named from the model's point of view: right, left, front,
frontright, frontleft, backright, back, backleft. Each view is rendered full-body and as a torso close-up,
textured (Eevee) and clay (Workbench, cavity on); shade `weights` colours each vertex by its dominant bone
(left side warm, right side cool, trunk grey, head yellow; _blender_rig_fit_humanoid20.WEIGHT_COLOURS); shade
`defects` paints what the check found at that frame: blue a fold, red a new self-intersection; shade
`bone:<name>[,<name>...]` paints the summed weight of those bones (black 0, blue, green, yellow, red 1).
`--hide-desaturated S` removes faces whose base colour
is greyer than saturation S (iron plates, soot-grey leather) so the skin under them can be judged on its
own; `--hide-box` limits that to a box (Blender axes, metres).

  blender --background --factory-startup --python _render_pose_views.py -- \
      --rigged R.glb --poses poses.npz [--keys "anim.x.talk|31" ...] --out DIR \
      [--views back backright right] [--crops torso] [--shades tex clay] [--size 900] \
      [--hide-desaturated 0.22 --hide-box -0.6 0 -0.3 0.3 0.7 1.3] [--tag nopauldron]
"""
import argparse
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

VIEWS = {  # camera direction from the model (Blender axes: the model faces -Y, its right is -X)
    "right": (-1.0, 0.0), "left": (1.0, 0.0), "front": (0.0, -1.0),
    "frontright": (-0.7071, -0.7071), "frontleft": (0.7071, -0.7071),
    "backright": (-0.7071, 0.7071), "back": (0.0, 1.0), "backleft": (0.7071, 0.7071),
}
CROPS = {"full": (1.2, 0.5), "torso": (0.5, 0.7)}   # (ortho scale, aim height) as shares of the height


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:]
    p = argparse.ArgumentParser()
    p.add_argument("--rigged", required=True)
    p.add_argument("--poses", help="npz from _skin_deform_check.py --dump; omit for the bind pose")
    p.add_argument("--keys", nargs="*", help="'<clip stem>|<frame>' entries of the npz (default: all)")
    p.add_argument("--out", required=True)
    p.add_argument("--views", nargs="+", default=list(VIEWS))
    p.add_argument("--crops", nargs="+", default=list(CROPS))
    p.add_argument("--shades", nargs="+", default=["tex", "clay"])
    p.add_argument("--size", type=int, default=900)
    p.add_argument("--hide-desaturated", type=float)
    p.add_argument("--hide-box", type=float, nargs=6)
    p.add_argument("--tag", default="")
    return p.parse_args(argv)


def to_blender(p):
    return np.c_[p[:, 0], -p[:, 2], p[:, 1]]


def base_colour_image(mesh):
    for slot in mesh.material_slots:
        mat = slot.material
        if not (mat and mat.use_nodes):
            continue
        for node in mat.node_tree.nodes:
            if node.type == "BSDF_PRINCIPLED" and node.inputs["Base Color"].is_linked:
                src = node.inputs["Base Color"].links[0].from_node
                if src.type == "TEX_IMAGE" and src.image:
                    return src.image
    return None


def desaturated_faces(mesh, threshold, box):
    image = base_colour_image(mesh)
    if image is None:
        raise SystemExit("no base colour image to classify faces by")
    w, h = image.size
    px = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)[:, :, :3]
    uv = mesh.data.uv_layers.active.data
    mw = mesh.matrix_world
    hide = []
    for poly in mesh.data.polygons:
        u = sum(uv[i].uv[0] for i in poly.loop_indices) / poly.loop_total
        v = sum(uv[i].uv[1] for i in poly.loop_indices) / poly.loop_total
        c = px[min(int(v * h), h - 1) % h, min(int(u * w), w - 1) % w]
        sat = (c.max() - c.min()) / max(c.max(), 1e-6)
        if sat >= threshold:
            continue
        if box:
            ctr = mw @ poly.center
            if not (box[0] <= ctr.x <= box[1] and box[2] <= ctr.y <= box[3] and box[4] <= ctr.z <= box[5]):
                continue
        hide.append(poly.index)
    return hide


def weight_colours(mesh):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import _blender_rig_fit_humanoid20 as fit
    names = {g.index: g.name for g in mesh.vertex_groups}
    attr = mesh.data.color_attributes.new("dominant_bone", "FLOAT_COLOR", "POINT")
    for v in mesh.data.vertices:
        best = max(v.groups, key=lambda g: g.weight, default=None)
        colour = fit.WEIGHT_COLOURS.get(names.get(best.group), (1, 0, 1)) if best else (1, 0, 1)
        attr.data[v.index].color = (*colour, 1.0)
    mat = bpy.data.materials.new("weights")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeAttribute")
    node.attribute_name = "dominant_bone"
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def main():
    a = parse()
    a.out = os.path.abspath(a.out)      # Blender resolves a relative render path against its own directory
    os.makedirs(a.out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    bpy.ops.import_scene.gltf(filepath=a.rigged)
    rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    for m in list(mesh.modifiers):
        mesh.modifiers.remove(m)            # the dumped positions are already skinned
    rig.hide_render = True
    mw = mesh.matrix_world.copy()
    mwi = mw.inverted()
    rest_co = [v.co.copy() for v in mesh.data.vertices]
    height = max((mw @ c).z for c in rest_co) - min((mw @ c).z for c in rest_co)

    poses, defects = {}, {}
    if a.poses:
        data = np.load(a.poses)
        rest = to_blender(data["rest"].astype(np.float64))
        tree = KDTree(len(rest))
        for i, p in enumerate(rest):
            tree.insert(p, i)
        tree.balance()
        index, worst = [], 0.0
        for c in rest_co:
            _co, i, d = tree.find(mw @ c)
            index.append(i)
            worst = max(worst, d)
        if worst > 1e-4:
            raise SystemExit(f"the dumped rest does not match the rigged mesh (max {worst:.5f} m)")
        index = np.array(index)
        keys = a.keys or [k for k in data.files if k != "rest" and not k.startswith("defects|")]
        for k in keys:
            poses[k] = to_blender(data[k].astype(np.float64))[index]
            if f"defects|{k}" in data.files:
                defects[k] = data[f"defects|{k}"][index]
    else:
        poses["bind|0"] = None

    if a.hide_desaturated is not None:
        import bmesh
        hide = set(desaturated_faces(mesh, a.hide_desaturated, a.hide_box))
        bm = bmesh.new()
        bm.from_mesh(mesh.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in hide], context="FACES_ONLY")
        bm.to_mesh(mesh.data)
        bm.free()
        print(f"hidden {len(hide)} desaturated faces")

    scene = bpy.context.scene
    scene.render.resolution_x = scene.render.resolution_y = a.size
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.32, 0.32, 0.34, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    scene.collection.objects.link(sun)
    fill = bpy.data.objects.new("fill", bpy.data.lights.new("fill", "SUN"))
    fill.data.energy = 1.5
    fill.rotation_euler = (math.radians(60), 0, math.radians(150))   # lights the back
    scene.collection.objects.link(fill)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    scene.collection.objects.link(cam)
    scene.camera = cam
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    eevee = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
    textured = [s.material for s in mesh.material_slots]
    weights = weight_colours(mesh) if "weights" in a.shades else None
    defect_mat = None
    written = 0
    for key, P in poses.items():
        stem, frame = key.split("|")
        if P is not None:
            verts = mesh.data.vertices
            for i, v in enumerate(verts):
                v.co = mwi @ Vector(P[i]) if i < len(P) else v.co
            mesh.data.update()
        for shade in a.shades:
            if shade.startswith("bone:"):
                # the summed weight of the named bones (comma-separated): black 0, then blue, green, yellow, red 1
                attr = mesh.data.color_attributes.get(shade) or mesh.data.color_attributes.new(
                    shade, "FLOAT_COLOR", "POINT")
                wanted = {g.index for g in mesh.vertex_groups if g.name in shade[5:].split(",")}
                ramp = np.array([[0.05, 0.05, 0.05], [0.1, 0.2, 1.0], [0.1, 0.9, 0.3], [1.0, 0.9, 0.1],
                                 [1.0, 0.1, 0.05]])
                for v in mesh.data.vertices:
                    w = min(sum(g.weight for g in v.groups if g.group in wanted), 1.0) * 4.0
                    k = min(int(w), 3)
                    c = ramp[k] + (ramp[k + 1] - ramp[k]) * (w - k)
                    attr.data[v.index].color = (*c, 1.0)
                bone_mat = bpy.data.materials.new(shade)
                bone_mat.use_nodes = True
                node = bone_mat.node_tree.nodes.new("ShaderNodeAttribute")
                node.attribute_name = shade
                bone_mat.node_tree.links.new(node.outputs["Color"],
                                             bone_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
                for slot in mesh.material_slots:
                    slot.material = bone_mat
            if shade == "defects":
                attr = mesh.data.color_attributes.get("defects") or mesh.data.color_attributes.new(
                    "defects", "FLOAT_COLOR", "POINT")
                mask = defects.get(key, np.zeros(len(mesh.data.vertices), dtype=np.uint8))
                palette = {0: (0.7, 0.68, 0.64), 1: (0.1, 0.35, 1.0), 2: (1.0, 0.1, 0.05), 3: (1.0, 0.0, 1.0)}
                for i, v in enumerate(mesh.data.vertices):
                    attr.data[i].color = (*palette[int(mask[i]) if i < len(mask) else 0], 1.0)
                if defect_mat is None:
                    defect_mat = bpy.data.materials.new("defects")
                    defect_mat.use_nodes = True
                    node = defect_mat.node_tree.nodes.new("ShaderNodeAttribute")
                    node.attribute_name = "defects"
                    defect_mat.node_tree.links.new(node.outputs["Color"],
                                                   defect_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
            if not shade.startswith("bone:"):
                for i, slot in enumerate(mesh.material_slots):
                    slot.material = (weights if shade == "weights" else defect_mat if shade == "defects"
                                     else textured[i])
            if shade in ("tex", "weights", "defects") or shade.startswith("bone:"):
                scene.render.engine = eevee
            else:
                scene.render.engine = "BLENDER_WORKBENCH"
                scene.display.shading.light = "STUDIO"
                scene.display.shading.color_type = "SINGLE"
                scene.display.shading.single_color = (0.75, 0.72, 0.68)
                scene.display.shading.show_cavity = True
                scene.display.shading.show_backface_culling = False
            for view in a.views:
                dx, dy = VIEWS[view]
                for crop in a.crops:
                    scale, aim_h = CROPS[crop]
                    cam.data.ortho_scale = scale * height
                    target = Vector((0.0, 0.0, aim_h * height))
                    cam.location = target + Vector((dx * 4.0, dy * 4.0, 0.2 * height))
                    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
                    label = shade.replace(":", "-").replace(",", "+")
                    name = f"{stem}_f{int(frame):03d}_{view}_{crop}_{label}{('_' + a.tag) if a.tag else ''}.png"
                    scene.render.filepath = os.path.join(a.out, name)
                    bpy.ops.render.render(write_still=True)
                    written += 1
    print(f"RENDER_POSE_VIEWS_DONE {written} images in {a.out}")


main()
