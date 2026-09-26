"""Render the armour build (.blend) or an exported GLB from named views.
blender -b --factory-startup -P render_blend.py -- <file.blend|file.glb> <outdir> <prefix> <mode:tex|clay|wire|kinds> <views> [WxH] [clip.glb@frame]
mode kinds = flat colours per build material (steel/rim/brass/leather/cloth/void).
views: comma list of names from armour_render.VIEWS or az:el pairs like 20:10.
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import bpy, math
import armour_render as R

argv = sys.argv[sys.argv.index("--") + 1:]
src, outdir, prefix, mode, views = argv[:5]
res = tuple(int(x) for x in argv[5].split("x")) if len(argv) > 5 and "x" in argv[5] else (560, 800)
pose = argv[6] if len(argv) > 6 else None

if src.endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=src)
    for o in list(bpy.data.objects):
        if o.type in ("CAMERA", "LIGHT"):
            bpy.data.objects.remove(o, do_unlink=True)
    objs = list(bpy.data.objects)
else:
    R.reset()
    objs = R.import_glb(src)
R.scene_setup(res=res)
meshes = [o for o in objs if o.type == "MESH" and o.name != "ground"]
arm = next((o for o in objs if o.type == "ARMATURE"), None)

if pose and arm:
    clip, frame = pose.rsplit("@", 1)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=clip)
    new = [o for o in bpy.data.objects if o not in before]
    carm = next(o for o in new if o.type == "ARMATURE")
    act = carm.animation_data.action
    for o in new:
        if o is not carm:
            bpy.data.objects.remove(o, do_unlink=True)
    arm.animation_data_create()
    arm.animation_data.action = act
    if hasattr(arm.animation_data, "action_slot") and act.slots:
        arm.animation_data.action_slot = act.slots[0]
    bpy.data.objects.remove(carm, do_unlink=True)
    f = float(frame)
    bpy.context.scene.frame_set(int(f), subframe=f - int(f))

if mode in ("clay", "wire"):
    m = R.clay_material(wire=(mode == "wire"))
    for o in meshes:
        for i in range(len(o.data.materials)):
            o.data.materials[i] = m
elif mode == "kinds":
    cols = {"k_steel": (0.5, 0.52, 0.56), "k_rim": (0.85, 0.85, 0.8), "k_brass": (0.8, 0.55, 0.15),
            "k_leather": (0.45, 0.22, 0.1), "k_cloth": (0.12, 0.12, 0.2), "k_void": (0.6, 0.05, 0.05)}
    for o in meshes:
        for i, mm in enumerate(o.data.materials):
            if mm and mm.name in cols:
                n = bpy.data.materials.new("flat_" + mm.name)
                n.use_nodes = True
                n.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*cols[mm.name], 1)
                n.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.5
                o.data.materials[i] = n

mn, mx = R.bounds(meshes)
center = (mn + mx) / 2
h = max(mx.z - mn.z, (mx.x - mn.x) * res[1] / res[0], (mx.y - mn.y) * res[1] / res[0])
if os.environ.get("FOCUS") and arm:   # close-up on a posed bone's head: "bone,height"
    bname, fh = os.environ["FOCUS"].split(",")
    center, h = arm.matrix_world @ arm.pose.bones[bname].head, float(fh) - 0.12
if os.environ.get("FRAME"):   # close-up: "x,y,z,height" framing override
    fx, fy, fz, fh = (float(v) for v in os.environ["FRAME"].split(","))
    from mathutils import Vector as _V
    center, h = _V((fx, fy, fz)), fh - 0.12
if os.environ.get("HDRI"):
    for node in bpy.context.scene.world.node_tree.nodes:
        if node.type == "TEX_ENVIRONMENT":
            node.image = bpy.data.images.load(os.path.join(os.path.dirname(node.image.filepath), os.environ["HDRI"]))
os.makedirs(outdir, exist_ok=True)
for v in views.split(","):
    if ":" in v:
        az, el = (float(x) for x in v.split(":"))
        R.VIEWS[v] = (az, el)
    R.camera(v, center, h + 0.12)
    R.render_to(os.path.join(outdir, f"{prefix}_{v.replace(':', '_')}.png"))
print("DONE", tuple(round(x, 3) for x in mn), tuple(round(x, 3) for x in mx))
