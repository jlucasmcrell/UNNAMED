"""Fit an MPFB character's face to a reference face through MPFB's own face targets (the identity stays parametric): Blender side.

    render: the head straight on through an orthographic camera (face.png + camera in probe.json)
    probe:  the render's MediaPipe landmarks cast onto the mesh (surface points), and every face target's displacement there
            (probe.npz) - for solve_face_fit.py (outside Blender: MediaPipe and scipy)
    apply:  the solved target weights set through MPFB (TargetService), proxies and clothes refitted, the .blend saved

    blender -b --python mpfb_face_fit.py -- --blend base.blend --out <dir> --mode render|probe|apply [--landmarks lm.json]
                                           [--weights w.json] [--save fitted.blend]
"""
import glob
import json
import math
import os
import sys

import bpy
import addon_utils
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
blend, out, mode = arg("--blend"), os.path.abspath(arg("--out")), arg("--mode")
os.makedirs(out, exist_ok=True)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=blend)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import TargetService, HumanService, ObjectService  # noqa: E402

basemesh = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
FACE_GROUPS = ["head", "nose", "mouth", "eyes", "ears", "chin", "cheek", "forehead", "eyebrows"]
TARGETS_DIR = os.path.join(os.path.dirname(sys.modules["bl_ext.user_default.mpfb"].__file__), "data", "targets")
RES = 1024
SCALE = 0.34   # ortho width (m) round the head


def head_frame():
    """The face's centre (world): the eyeballs' midpoint, dropped to mid-face; and the front of the head (least y)."""
    eyes = ObjectService.find_object_of_type_amongst_nearest_relatives(basemesh, "Eyes")
    ep = [eyes.matrix_world @ v.co for v in eyes.data.vertices]
    ec = Vector((sum(p.x for p in ep) / len(ep), sum(p.y for p in ep) / len(ep), sum(p.z for p in ep) / len(ep)))
    front = min((basemesh.matrix_world @ v.co).y for v in basemesh.data.vertices)
    c = Vector((ec.x, ec.y, ec.z - 0.03))
    return c, Vector((c.x, front, c.z)), c


def camera():
    c, lo, hi = head_frame()
    cam = bpy.data.objects.get("fitcam") or bpy.data.objects.new("fitcam", bpy.data.cameras.new("fitcam"))
    if cam.name not in bpy.context.scene.collection.objects:
        bpy.context.scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = SCALE
    # The face looks along -Y: the camera stands in front of it and looks along +Y.
    cam.location = Vector((c.x, lo.y - 1.0, c.z + 0.01))
    cam.rotation_euler = (math.radians(90), 0, 0)
    bpy.context.scene.camera = cam
    return cam, {"center": [c.x, cam.location.y, c.z + 0.01], "scale": SCALE, "res": RES}


def to_world(cam_info, px, py):
    cx, cy, cz = cam_info["center"]
    s = cam_info["scale"]
    return Vector((cx + (px / RES - 0.5) * s, cy, cz - (py / RES - 0.5) * s))


if mode == "render":
    scene = bpy.context.scene
    for o in bpy.data.objects:
        if o.type == "MESH" and o is not basemesh and ObjectService.get_object_type(o) in ("Hair", "Clothes"):
            o.hide_render = True
    cam, info = camera()
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = RES
    world = bpy.data.worlds.new("fitw")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.8, 0.8, 0.8, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    scene.world = world
    sun = bpy.data.objects.new("fitsun", bpy.data.lights.new("fitsun", "SUN"))
    sun.data.energy = 2.5
    sun.rotation_euler = (math.radians(70), 0, 0)
    scene.collection.objects.link(sun)
    scene.render.filepath = os.path.join(out, "face.png")
    bpy.ops.render.render(write_still=True)
    json.dump({"camera": info}, open(os.path.join(out, "probe.json"), "w"), indent=1)
    print("FIT_RENDER", os.path.join(out, "face.png"))

elif mode == "probe":
    info = json.load(open(os.path.join(out, "probe.json")))["camera"]
    lm = json.load(open(arg("--landmarks")))["landmarks"]   # render pixels [[x, y, z], ...]
    # Evaluate the basemesh with its modifiers off (the mask hides helper geometry but keeps indices): vertex index = base index.
    saved = [(m, m.show_viewport) for m in basemesh.modifiers]
    for m, _ in saved:
        m.show_viewport = False
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = basemesh.evaluated_get(dg)
    mesh = ev.to_mesh()
    verts = np.array([basemesh.matrix_world @ v.co for v in mesh.vertices])
    tris = []
    mesh.calc_loop_triangles()
    for t in mesh.loop_triangles:
        tris.append(tuple(t.vertices))
    tris = np.array(tris)
    bvh = BVHTree.FromPolygons([tuple(v) for v in verts], [tuple(t) for t in tris])
    hits = []
    for x, y, _z in lm:
        o = to_world(info, x, y)
        loc, nrm, idx, dist = bvh.ray_cast(o, Vector((0, 1, 0)))
        if loc is None:
            hits.append(None)
            continue
        a, b, c = (verts[i] for i in tris[idx])
        p = np.array(loc)
        v0, v1, v2 = b - a, c - a, p - a
        d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        wb = (d11 * d20 - d01 * d21) / den
        wc = (d00 * d21 - d01 * d20) / den
        hits.append((int(idx), [1 - wb - wc, wb, wc]))
    ev.to_mesh_clear()
    # Every face target's displacement at the hit points (targets loaded at weight 0, read as shape keys).
    names, disp = [], []
    ok = [i for i, h in enumerate(hits) if h is not None]
    tri_v = np.array([tris[hits[i][0]] for i in ok])
    bary = np.array([hits[i][1] for i in ok])
    basis = np.array([v.co for v in basemesh.data.vertices])
    mw = np.array(basemesh.matrix_world)[:3, :3]
    for group in FACE_GROUPS:
        for f in sorted(glob.glob(os.path.join(TARGETS_DIR, group, "*.target.gz"))):
            name = os.path.basename(f)[:-len(".target.gz")]
            key = TargetService.load_target(basemesh, f, weight=0.0, name="fit_" + name)
            kb = basemesh.data.shape_keys.key_blocks.get("fit_" + name)
            if kb is None:
                continue
            co = np.empty(len(basemesh.data.vertices) * 3)
            kb.data.foreach_get("co", co)
            d = (co.reshape(-1, 3) - basis) @ mw.T
            at = (d[tri_v] * bary[:, :, None]).sum(1)
            if np.abs(at).max() < 1e-6:
                basemesh.shape_key_remove(kb)
                continue
            names.append(f"{group}/{name}")
            disp.append(at)
            basemesh.shape_key_remove(kb)
    pts = (verts[tri_v] * bary[:, :, None]).sum(1)
    np.savez(os.path.join(out, "probe.npz"), landmark_index=np.array(ok), points=pts, disp=np.array(disp), names=np.array(names))
    print("FIT_PROBE", len(ok), "landmarks on the mesh,", len(names), "targets")

elif mode == "apply":
    weights = json.load(open(arg("--weights")))["weights"]
    for name, w in weights.items():
        if abs(w) < 1e-3:
            continue
        group, target = name.split("/")
        # Loaded at its weight under MPFB's own name, so the character serializes and edits like any MPFB human.
        TargetService.load_target(basemesh, os.path.join(TARGETS_DIR, group, target + ".target.gz"), weight=float(w))
    HumanService.refit(basemesh)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(arg("--save")))
    print("FIT_APPLY", len([w for w in weights.values() if abs(w) >= 1e-3]), "targets set")
