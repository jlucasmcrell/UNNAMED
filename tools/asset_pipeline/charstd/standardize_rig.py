"""An MPFB character's rig made the production skeleton (charstd, skeleton_humanoid_a.json): the game-engine rig's bones renamed one to
one (the skinned meshes' vertex groups with them), the eye bones added at the eyeballs' centres with the eyeballs bound to them, and
the hand sockets added on the knuckle line.

    blender -b --python standardize_rig.py -- --blend in.blend --out out.blend [--skeleton skeleton_humanoid_a.json]
"""
import json
import os
import sys

import bpy
import addon_utils
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
skeleton = json.load(open(arg("--skeleton", os.path.join(HERE, "skeleton_humanoid_a.json"))))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
rename = skeleton["rename_from_mpfb_game_engine"]
skinned = [o for o in bpy.data.objects if o.type == "MESH" and any(m.type == "ARMATURE" and m.object is rig for m in o.modifiers)]
missing = [b for b in rename if b not in rig.data.bones]
if missing:
    raise SystemExit(f"not an MPFB game-engine rig: missing {missing}")
for old, new in rename.items():
    rig.data.bones[old].name = new
    for o in skinned:   # Blender renames the groups of meshes it deforms, but not every file keeps that link: made sure
        g = o.vertex_groups.get(old)
        if g is not None:
            g.name = new

# The eye bones (at the eyeballs' centres, pointing forward) and the eyeballs bound to them.
eyes = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.get_object_type(o) == "Eyes")
dg = bpy.context.evaluated_depsgraph_get()
em = eyes.evaluated_get(dg).to_mesh()
pts = [eyes.matrix_world @ v.co for v in em.vertices]
eyes.evaluated_get(dg).to_mesh_clear()
centre = {"L": sum((p for p in pts if p.x >= 0), Vector()) / max(1, sum(1 for p in pts if p.x >= 0)),
          "R": sum((p for p in pts if p.x < 0), Vector()) / max(1, sum(1 for p in pts if p.x < 0))}
Winv = rig.matrix_world.inverted()
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
eb = rig.data.edit_bones
for side in ("L", "R"):
    b = eb.new(f"eye.{side}")
    b.head = Winv @ centre[side]
    b.tail = Winv @ (centre[side] + Vector((0, -0.02, 0)))
    b.parent = eb["head"]
    b.use_deform = True
# The hand sockets: on the knuckle line's midpoint, 2.5 cm into the palm; Y along the grip (pinky knuckle to index knuckle), Z the palm.
for side in ("L", "R"):
    index, pinky, middle, thumb = (eb[f"{n}.{side}"] for n in ("index_01", "pinky_01", "middle_01", "thumb_02"))
    along = (index.head - pinky.head).normalized()
    fingers = (middle.tail - middle.head).normalized()
    palm = along.cross(fingers).normalized()
    if palm.dot(thumb.head - (index.head + pinky.head) / 2) < 0:   # into the palm: the side the thumb opposes from
        palm = -palm
    at = (index.head + pinky.head) / 2 + palm * 0.025 * rig.matrix_world.to_scale().x ** -1
    s = eb.new(f"SOCK_hand.{side}")
    s.head = at
    s.tail = at + along * 0.05
    s.align_roll(palm)
    s.parent = eb[f"hand.{side}"]
    s.use_deform = False
bpy.ops.object.mode_set(mode="OBJECT")
for g in list(eyes.vertex_groups):
    eyes.vertex_groups.remove(g)
left = [i for i, p in enumerate(pts) if p.x >= 0]
right = [i for i, p in enumerate(pts) if p.x < 0]
eyes.vertex_groups.new(name="eye.L").add(left, 1.0, "REPLACE")
eyes.vertex_groups.new(name="eye.R").add(right, 1.0, "REPLACE")
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(arg("--out")))
print("STANDARDIZE_RIG", len(rig.data.bones), "bones", {s: [round(c, 3) for c in centre[s]] for s in centre})
