"""Render tree GLBs in an outdoor scene with Eevee for LOD and silhouette review (read-only on the models).

A job file lists shots; each shot places one or more GLBs on a ground plane under a sky and a sun and renders one frame
from a game-like camera (vertical FOV 75 deg, the player camera's, from eye height). Every GLB is imported once and
linked into each shot that uses it. Compositing into sheets is done outside Blender (_tree_review_sheets.py).

  blender --background --factory-startup --python tools/asset_pipeline/_render_tree_lods.py -- --job job.json

job.json: {"res": [1920, 1080], "fov_deg": 75, "samples": 16, "sun": {"elevation_deg": 38, "azimuth_deg": -35},
           "ground": true, "transparent": false,   (no ground + transparent film: the model's own coverage as alpha)
           "shots": [{"out": "a.png", "models": [{"glb": "x.glb", "at": [0, 0], "yaw_deg": 0, "shadow": true}],
                      "cam": {"dist": 30, "height": 1.7, "azimuth_deg": 0, "look_h": 6.5}, "res": [w, h]}]}
Camera azimuth 0 looks at the model's front (glTF +Z, Blender -Y). Prints SHOT <out> per frame and DONE at the end.
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Euler, Vector


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--job", required=True)
    return p.parse_args(argv)


def scene_setup(job):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    ee = sc.eevee
    for k, v in (("taa_render_samples", job.get("samples", 16)), ("use_shadows", True), ("use_raytracing", False)):
        if hasattr(ee, k):
            setattr(ee, k, v)
    if hasattr(ee, "shadow_ray_count"):
        ee.shadow_ray_count = 2
    if hasattr(ee, "shadow_step_count"):
        ee.shadow_step_count = 8
    try:
        sc.view_settings.view_transform = "AgX"
        sc.view_settings.look = "AgX - Base Contrast"
    except TypeError:
        try:
            sc.view_settings.view_transform = "Filmic"
        except TypeError:
            pass
    sc.view_settings.exposure = job.get("exposure", 0.0)
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA" if job.get("transparent") else "RGB"
    sc.render.film_transparent = bool(job.get("transparent"))
    # sky
    w = bpy.data.worlds.new("sky")
    sc.world = w
    nt = w.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    sky = nt.nodes.new("ShaderNodeTexSky")
    sun = job.get("sun", {})
    el = math.radians(sun.get("elevation_deg", 38))
    az = math.radians(sun.get("azimuth_deg", -35))
    for t in ("MULTIPLE_SCATTERING", "SINGLE_SCATTERING", "NISHITA", "HOSEK_WILKIE"):
        try:
            sky.sky_type = t
            break
        except TypeError:
            continue
    if hasattr(sky, "sun_elevation"):
        sky.sun_elevation = el
        sky.sun_rotation = az
    if hasattr(sky, "sun_disc"):
        sky.sun_disc = False
    bg.inputs["Strength"].default_value = job.get("sky_strength", 0.2)
    nt.links.new(sky.outputs[0], bg.inputs[0])
    nt.links.new(bg.outputs[0], out.inputs[0])
    # sun lamp: direction from the same elevation/azimuth (azimuth measured like the sky's rotation)
    ld = bpy.data.lights.new("sun", "SUN")
    ld.energy = sun.get("strength", 5.5)
    ld.angle = math.radians(1.5)
    ld.color = (1.0, 0.96, 0.9)
    lo = bpy.data.objects.new("sun", ld)
    sc.collection.objects.link(lo)
    d = Vector((math.cos(el) * math.sin(az), -math.cos(el) * math.cos(az), math.sin(el)))  # toward the sun
    lo.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    # ground
    me = bpy.data.meshes.new("ground")
    s = 2000.0
    me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new("ground", me)
    if job.get("ground", True):
        sc.collection.objects.link(g)
    gm = bpy.data.materials.new("ground")
    gt = gm.node_tree
    bsdf = gt.nodes.get("Principled BSDF")
    noise = gt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.35
    ramp = gt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.085, 0.095, 0.045, 1)
    ramp.color_ramp.elements[1].color = (0.15, 0.14, 0.075, 1)
    gt.links.new(noise.outputs["Fac"], ramp.inputs[0])
    gt.links.new(ramp.outputs[0], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.95
    for k in ("Specular IOR Level", "Specular"):
        if k in bsdf.inputs:
            bsdf.inputs[k].default_value = 0.0
    me.materials.append(gm)
    cam_d = bpy.data.cameras.new("cam")
    cam_d.sensor_fit = "VERTICAL"
    cam_d.angle_y = math.radians(job.get("fov_deg", 75))
    cam_d.clip_start = 0.1
    cam_d.clip_end = 3000
    cam = bpy.data.objects.new("cam", cam_d)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return sc, cam


_cache = {}


def model(path):
    """Import a GLB once; return its root objects (kept hidden until a shot shows them)."""
    if path in _cache:
        return _cache[path]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        o.hide_render = True
    roots = [o for o in new if o.parent is None]
    _cache[path] = (new, roots)
    return _cache[path]


def main():
    a = args()
    job = json.load(open(a.job, encoding="utf-8"))
    sc, cam = scene_setup(job)
    placed = []
    for shot in job["shots"]:
        for o in placed:
            bpy.data.objects.remove(o, do_unlink=True)
        placed = []
        for m in shot["models"]:
            objs, roots = model(m["glb"])
            # a linked duplicate per placement, so one import serves every shot and every copy
            mapping = {}
            for o in objs:
                c = o.copy()
                c.hide_render = False
                if not m.get("shadow", True):
                    c.visible_shadow = False
                sc.collection.objects.link(c)
                mapping[o] = c
                placed.append(c)
            for o, c in mapping.items():
                if o.parent in mapping:
                    c.parent = mapping[o.parent]
            for r in roots:
                c = mapping[r]
                x, y = m.get("at", [0, 0])
                c.location = Vector((x, y, 0)) + r.location
                c.rotation_euler = Euler((r.rotation_euler.x, r.rotation_euler.y,
                                          r.rotation_euler.z + math.radians(m.get("yaw_deg", 0))))
        cm = shot["cam"]
        az = math.radians(cm.get("azimuth_deg", 0))
        tx, ty = cm.get("target", [0, 0])
        pos = Vector((tx + cm["dist"] * math.sin(az), ty - cm["dist"] * math.cos(az), cm.get("height", 1.7)))
        look = Vector((tx, ty, cm.get("look_h", 6.0)))
        cam.location = pos
        cam.rotation_euler = (look - pos).to_track_quat("-Z", "Y").to_euler()
        if "fov_deg" in cm:
            cam.data.angle_y = math.radians(cm["fov_deg"])
        else:
            cam.data.angle_y = math.radians(job.get("fov_deg", 75))
        res = shot.get("res", job.get("res", [1920, 1080]))
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.resolution_percentage = 100
        sc.render.filepath = shot["out"]
        os.makedirs(os.path.dirname(os.path.abspath(shot["out"])), exist_ok=True)
        bpy.ops.render.render(write_still=True)
        print("SHOT", shot["out"], flush=True)
    print("DONE", flush=True)


if __name__ == "__main__":
    main()
