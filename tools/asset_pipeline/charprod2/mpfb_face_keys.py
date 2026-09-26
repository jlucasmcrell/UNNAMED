"""The face units carried onto the pieces that sit on the face - brows, lashes, teeth, tongue (and any MHCLO garment) - by MPFB's own
FaceService.interpolate_targets (its MHCLO vertex correspondence), replacing charprod2/proxy_face_keys.py (an in-house transfer written
before the facial audit found MPFB already does this).

    blender -b --python mpfb_face_keys.py -- --blend in.blend --out out.blend
"""
import os
import sys

import bpy
import addon_utils

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402
from bl_ext.user_default.mpfb.services.faceservice import FaceService  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
FaceService.interpolate_targets(body)
made = {o.name: len(o.data.shape_keys.key_blocks) - 1 for o in bpy.data.objects
        if o.type == "MESH" and o is not body and o.data.shape_keys}
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(arg("--out")))
print("MPFB_FACE_KEYS", made)
