"""Blender: review renders of a character GLB - face (front, three-quarter) and body (front, back), textured or clay,
Cycles on the GPU, one neutral studio light rig for every stage so stages compare like for like.

    blender --background --factory-startup --python render_views.py -- --input X.glb --outdir DIR --stem name
        [--clay] [--size 900] [--samples 64] [--views face,face34,body,back]
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

# (ortho scale, aim height) as fractions of the model's height; the azimuth in degrees (0 = the model's front).
VIEWS = {"face": (0.2, 0.925, 0), "face34": (0.2, 0.925, 35), "body": (1.15, 0.5, 0), "back": (1.15, 0.5, 180),
         "hands": (0.42, 0.52, 0), "neck": (0.36, 0.84, 20), "neckback": (0.36, 0.84, 160)}


def parse():
    p = argparse.ArgumentParser()
    p.add_argument("--input", required=True)
    p.add_argument("--outdir", required=True)
    p.add_argument("--stem", required=True)
    p.add_argument("--size", type=int, default=900)
    p.add_argument("--samples", type=int, default=64)
    p.add_argument("--clay", action="store_true")
    p.add_argument("--views", default="face,face34,body,back")
    p.add_argument("--no-normal", action="store_true", help="normal maps disconnected (to tell shading from colour)")
    return p.parse_args(sys.argv[sys.argv.index("--") + 1:])


def gpu():
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = kind
            prefs.get_devices()
            if any(d.type == kind for d in prefs.devices):
                for d in prefs.devices:
                    d.use = d.type == kind
                return kind
        except TypeError:
            continue
    return "CPU"


def main():
    a = parse()
    os.makedirs(a.outdir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.input, disable_bone_shape=True)
    # LOD0 only: an authored chain's _LOD1/_LOD2 meshes sit on the same skeleton and would show through it.
    for o in [o for o in bpy.context.scene.objects if o.type == "MESH" and o.name.rsplit("_LOD", 1)[-1].isdigit()]:
        bpy.data.objects.remove(o)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in meshes:
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    height = hi.z - lo.z
    cx, cy = (lo.x + hi.x) / 2, (lo.y + hi.y) / 2

    if a.clay:
        clay = bpy.data.materials.new("clay")
        clay.use_nodes = True
        bsdf = clay.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (0.62, 0.6, 0.57, 1)
        bsdf.inputs["Roughness"].default_value = 0.6
        for o in meshes:
            o.data.materials.clear()
            o.data.materials.append(clay)

    if a.no_normal:
        for mat in bpy.data.materials:
            if mat.use_nodes:
                for node in mat.node_tree.nodes:
                    if node.type == "BSDF_PRINCIPLED":
                        for link in list(node.inputs["Normal"].links):
                            mat.node_tree.links.remove(link)
    scene = bpy.context.scene
    scene.render.resolution_x = scene.render.resolution_y = a.size
    world = bpy.data.worlds.new("studio")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.3, 0.3, 0.32, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    for name, energy, pitch, yaw in (("key", 3.2, 50, -35), ("fill", 1.2, 65, 140), ("rim", 1.6, 20, 180)):
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
        light.data.energy = energy
        light.data.angle = math.radians(8)
        light.rotation_euler = (math.radians(pitch), 0, math.radians(yaw))
        scene.collection.objects.link(light)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.engine = "CYCLES"
    device = gpu()
    scene.cycles.device = "GPU" if device != "CPU" else "CPU"
    scene.cycles.samples = a.samples
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = "AgX"

    for view in a.views.split(","):
        scale, aim, azimuth = VIEWS[view]
        cam_data.ortho_scale = scale * height
        target = Vector((cx, cy, lo.z + aim * height))
        az = math.radians(azimuth)
        direction = Vector((math.sin(az), -math.cos(az), 0.0))  # the model faces -Y
        cam.location = target + direction * 4.0 * height + Vector((0, 0, 0.05 * height))
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(a.outdir, f"{a.stem}_{view}.png")
        bpy.ops.render.render(write_still=True)
    print(f"RENDER_VIEWS_DONE {a.stem} {device} {a.views}")


main()
