"""Render an exported rigged GLB posed by a clip GLB's action at several frames, from several views.
blender -b --factory-startup -P render_poses.py -- <model.glb> <clip.glb> <frames,comma> <views,comma> <outdir> <prefix> [WxH] [mode]
Views: names from armour_render.VIEWS or az:el. The camera frames the posed figure's bounds at each frame.
mode: tex (default) or wire (clay + wireframe).
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import bpy
import armour_render as R

argv = sys.argv[sys.argv.index("--") + 1:]
model, clip, frames, views, outdir, prefix = argv[:6]
res = tuple(int(x) for x in argv[6].split("x")) if len(argv) > 6 else (480, 620)
mode = argv[7] if len(argv) > 7 else "tex"
if os.environ.get("HDRI_NAME"):
    pass
R.reset()
objs = R.import_glb(model)
R.scene_setup(res=res)
hdr = os.environ.get("HDRI", "studio.exr")
for node in bpy.context.scene.world.node_tree.nodes:
    if node.type == "TEX_ENVIRONMENT":
        node.image = bpy.data.images.load(os.path.join(os.path.dirname(node.image.filepath), hdr))
meshes = [o for o in objs if o.type == "MESH"]
arm = next(o for o in objs if o.type == "ARMATURE")
if mode == "wire":
    m = R.clay_material(wire=True)
    for o in meshes:
        for i in range(len(o.data.materials)):
            o.data.materials[i] = m

before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=clip)
new = [o for o in bpy.data.objects if o not in before]
carm = next(o for o in new if o.type == "ARMATURE")
act = carm.animation_data.action
for o in new:
    bpy.data.objects.remove(o, do_unlink=True)
arm.animation_data_create()
arm.animation_data.action = act
if hasattr(arm.animation_data, "action_slot") and act.slots:
    arm.animation_data.action_slot = act.slots[0]
print("ACTION", act.name, tuple(act.frame_range))
os.makedirs(outdir, exist_ok=True)
for fr in frames.split(","):
    f = float(fr)
    bpy.context.scene.frame_set(int(f), subframe=f - int(f))
    mn, mx = R.bounds(meshes)
    center = (mn + mx) / 2
    h = max(mx.z - mn.z, (mx.x - mn.x) * res[1] / res[0], (mx.y - mn.y) * res[1] / res[0]) + 0.15
    if os.environ.get("FOCUS"):   # close-up on a posed bone's head: "bone,height"
        bname, fh = os.environ["FOCUS"].split(",")
        center = arm.matrix_world @ arm.pose.bones[bname].head
        h = float(fh)
    for v in views.split(","):
        if ":" in v:
            az, el = (float(x) for x in v.split(":"))
            R.VIEWS[v] = (az, el)
        for o in list(bpy.data.objects):
            if o.type == "CAMERA":
                bpy.data.objects.remove(o, do_unlink=True)
        R.camera(v, center, h)
        R.render_to(os.path.join(outdir, f"{prefix}_{fr.replace('.', '_')}_{v.replace(':', '_')}.png"))
    print("FRAME", f, "min", tuple(round(x, 3) for x in mn), "max", tuple(round(x, 3) for x in mx))
