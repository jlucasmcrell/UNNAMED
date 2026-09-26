"""A character's identity kit (charstd): what makes an instance of the archetype a particular person, portable to any build of the same
archetype - the head's shape as an offset per body vertex (in the body's own unscaled space, so it scales with the instance's height),
the skin's albedo and normal maps (the archetype's UV layout), the iris texture, and the hair (a mesh in the same body-local space,
bound rigidly to the head).

    blender -b --python identity_kit.py -- --base base.blend --shaped shaped.blend --hair-from hybrid.blend --textures <tex dir>
                                            --out <kit dir> [--hair-object Human.hair]

base/shaped: the same MPFB body before and after its identity shaping (e.g. charprod2's assembled.blend and wrapped.blend); the
offset is their difference. Either may be the same file (no head shaping: a zero offset).
"""
import json
import os
import shutil
import sys

import bpy
import numpy as np
import addon_utils

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
out = os.path.abspath(arg("--out"))
os.makedirs(out, exist_ok=True)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)


def body_local(path):
    bpy.ops.wm.open_mainfile(filepath=os.path.abspath(path))
    addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
    from bl_ext.user_default.mpfb.services import ObjectService
    body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
    for m in body.modifiers:
        if m.type in ("MASK", "ARMATURE"):
            m.show_viewport = False
    e = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = e.to_mesh()
    co = np.array([tuple(v.co) for v in me.vertices])
    e.to_mesh_clear()
    return co, np.array(body.matrix_world)


a, Ma = body_local(arg("--base"))
b, _ = body_local(arg("--shaped"))
np.save(os.path.join(out, "head_offset_local.npy"), (b - a).astype(np.float32))

# The hair, into the body's unscaled space (the rig's world transform undone).
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--hair-from")))
hair = bpy.data.objects[arg("--hair-object", "Human.hair")]
rig = hair.parent
inv = rig.matrix_world.inverted()
M = inv @ hair.matrix_world
for v in hair.data.vertices:
    v.co = M @ v.co
hair.parent = None
hair.modifiers.clear()
hair.matrix_world.identity()
hair.name = "hair"
for o in list(bpy.data.objects):
    if o is not hair:
        bpy.data.objects.remove(o)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "hair.blend"))

tex = os.path.abspath(arg("--textures"))
for src, dst in (("body_albedo.png", "skin_albedo.png"), ("body_normal.png", "skin_normal.png"), ("eye.png", "iris.png")):
    if os.path.exists(os.path.join(tex, src)):
        shutil.copyfile(os.path.join(tex, src), os.path.join(out, dst))
kit = {"head_offset_local": "head_offset_local.npy", "max_offset_m": round(float(np.linalg.norm(b - a, axis=1).max() * Ma[0][0]), 4),
       "skin_albedo": "skin_albedo.png", "skin_normal": "skin_normal.png", "iris": "iris.png", "hair": {"blend": "hair.blend", "object": "hair",
       "bone": "head"}, "sources": {"base": arg("--base"), "shaped": arg("--shaped"), "hair": arg("--hair-from"), "textures": tex}}
json.dump(kit, open(os.path.join(out, "identity.json"), "w"), indent=1)
print("IDENTITY_KIT", out, kit["max_offset_m"])
