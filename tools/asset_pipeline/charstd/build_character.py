"""A character instance built from the standard (charstd, CHARACTER_ASSET_STANDARD.md / CHARACTER_FACE_PIPELINE_BAKEOFF.md): the
archetype's MPFB body at the character's own macros (a controlled morph), its face by an identity preset (bounded MPFB modifier
weights - the values a character creator's sliders would set), a stock MPFB hair, skin, eyebrows and iris, the ARKit face units and
visemes carried onto the face's components by MPFB itself, the production skeleton, and an outfit of the archetype's garments with
their regions hidden. Everything is configuration: no per-character code.

    blender -b --python build_character.py -- --character characters/<id>.json --out <dir> [--garments <dir>]

Writes <dir>/<id>.blend. The character file: {"id", "archetype", "mpfb": {macro overrides, "skin", "hair", "eyebrows", "eyes"},
"identity_preset": identities/<file>, "hair_colour": [r, g, b], "iris": "<MPFB eye material>", "skin_texture": <path, optional>,
"outfit": [garment ids]}.
"""
import json
import os
import sys

import bpy
import numpy as np
import addon_utils

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import common  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
char = common.load(os.path.join(HERE, arg("--character")))
arch = common.load(os.path.join(HERE, "archetypes", char["archetype"] + ".json"))
out = os.path.abspath(arg("--out"))
os.makedirs(out, exist_ok=True)
garments_dir = os.path.abspath(arg("--garments", "G:/UNNAMED_PHASEB/assets/_staging/charstd/garments"))

addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import (HumanService, TargetService, LocationService, AssetService,  # noqa: E402
                                               ObjectService)
from bl_ext.user_default.mpfb.services.faceservice import FaceService  # noqa: E402

bpy.ops.wm.read_factory_settings(use_empty=True)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
AssetService.update_all_asset_lists()
spec = {**arch["mpfb"], **char.get("mpfb", {})}
macro = TargetService.get_default_macro_info_dict()
for k, v in {**arch["mpfb"]["macro"], **char.get("mpfb", {}).get("macro", {})}.items():
    if k == "race":
        macro["race"].update(v)
    else:
        macro[k] = v
body = HumanService.create_human(mask_helpers=True, detailed_helpers=True, extra_vertex_groups=True, feet_on_ground=True,
                                 scale=0.1, macro_detail_dict=macro)
data = LocationService.get_user_data()
targets_dir = os.path.join(os.path.dirname(sys.modules["bl_ext.user_default.mpfb"].__file__), "data", "targets")

# The identity: bounded modifier weights, loaded under MPFB's own names (the character stays an ordinary, editable MPFB human).
preset = common.load(os.path.join(HERE, char["identity_preset"]))
limit = float(preset.get("max_weight", 0.5))
unknown = [n for n in preset["weights"] if not os.path.exists(os.path.join(targets_dir, *n.split("/")) + ".target.gz")]
if unknown:
    raise SystemExit(f"not MPFB modifier targets: {unknown}")
for name, w in preset["weights"].items():
    if abs(w) > limit:
        raise SystemExit(f"{name} {w} is outside the preset's safe range ({limit})")
    group, target = name.split("/")
    TargetService.load_target(body, os.path.join(targets_dir, group, target + ".target.gz"), weight=float(w))
rig = HumanService.add_builtin_rig(body, spec.get("rig", "game_engine"), import_weights=True)


def asset(kind, name, ext=".mhclo"):
    folder = os.path.join(data, kind, name)
    return next(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(ext))


HumanService.add_mhclo_asset(asset("eyes", spec.get("eyes", "high-poly")), body, asset_type="Eyes", subdiv_levels=0,
                             material_type="MAKESKIN")
for kind, typ in (("eyebrows", "Eyebrows"), ("eyelashes", "Eyelashes"), ("teeth", "Teeth"), ("tongue", "Tongue"), ("hair", "Hair")):
    if spec.get(kind):
        HumanService.add_mhclo_asset(asset(kind, spec[kind]), body, asset_type=typ, subdiv_levels=0, material_type="MAKESKIN")
if spec.get("skin"):
    HumanService.set_character_skin(asset("skins", spec["skin"], ".mhmat"), body, skin_type="ENHANCED")
FaceService.load_targets(body, load_microsoft_visemes=True, load_meta_visemes=True, load_arkit_faceunits=True)
FaceService.interpolate_targets(body)

# Height (top of the head as rendered), then the production skeleton.
bpy.context.view_layer.update()
top = max(c[2] for c in common.shaped(body))
s = float(char.get("height_m", spec.get("height_m", 1.8))) / top
rig.scale = (s, s, s)
bpy.context.view_layer.update()
blend = os.path.join(out, char["id"] + "_base.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
import subprocess  # noqa: E402
blender = bpy.app.binary_path
final = os.path.join(out, char["id"] + ".blend")
subprocess.run([blender, "-b", "--python", os.path.join(HERE, "standardize_rig.py"), "--", "--blend", blend, "--out", final], check=True,
               capture_output=True)
bpy.ops.wm.open_mainfile(filepath=final)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")

# Hair colour and iris (material parameters, per character: "hair_colour" dyes the hair and brows; "iris" names one of MPFB's
# eye materials, eyes/materials/<iris>_eye.png).
for o in bpy.data.objects:
    if o.type != "MESH":
        continue
    k = ObjectService.get_object_type(o)
    for m in o.data.materials:
        if not (m and m.node_tree):
            continue
        if k == "Eyes" and char.get("iris"):
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image and n.image.name.lower().endswith("_eye.png"):
                    n.image = bpy.data.images.load(os.path.join(data, "eyes", "materials", char["iris"] + "_eye.png"))
        if k in ("Hair", "Eyebrows") and char.get("hair_colour"):
            # The texture's own light and dark kept, its colour replaced: colour x (luminance / the texture's mean luminance), so a
            # dark source can still be dyed light (grey) - a plain multiply only darkens. Written as a new image (exports as-is).
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image and n.outputs["Color"].is_linked:
                    src = n.image
                    px = np.empty(src.size[0] * src.size[1] * 4, np.float32)
                    src.pixels.foreach_get(px)
                    px = px.reshape(-1, 4)
                    lum = px[:, :3] @ np.array([0.2126, 0.7152, 0.0722], np.float32)
                    mean = max(float(lum[px[:, 3] > 0.5].mean()) if (px[:, 3] > 0.5).any() else float(lum.mean()), 0.02)
                    px[:, :3] = np.clip(np.array(char["hair_colour"], np.float32)[None] * (lum / mean)[:, None], 0, 1)
                    dyed = bpy.data.images.new(f"{char['id']}_{o.name.split('.')[-1]}_dyed", src.size[0], src.size[1], alpha=True)
                    dyed.pixels.foreach_set(px.ravel())
                    dyed.filepath_raw = os.path.join(out, dyed.name + ".png")
                    dyed.file_format = "PNG"
                    dyed.save()
                    n.image = bpy.data.images.load(dyed.filepath_raw, check_existing=False)
                    bpy.data.images.remove(dyed)
                    break

# An identity skin texture (identity_texture.py), where the character has one: it replaces the stock skin's diffuse image.
if char.get("skin_texture"):
    def tree_images(tree):
        for n in tree.nodes:
            if n.type == "TEX_IMAGE" and n.image:
                yield n.image
            elif n.type == "GROUP" and n.node_tree:
                yield from tree_images(n.node_tree)
    for m in body.data.materials:
        for img in (i for i in (tree_images(m.node_tree) if m and m.node_tree else []) if "diffuse" in i.name.lower()):
            img.filepath = os.path.abspath(os.path.join(HERE, char["skin_texture"]))
            img.reload()

# The outfit: garments fitted once to the archetype's canonical body, conformed to this body, skinned, their regions hidden.
regions = common.load(os.path.join(HERE, "archetypes", char["archetype"] + ".regions.json"))
canon = None
for m in body.modifiers:
    if m.type == "MASK" and m.name != "Hide helpers":
        body.modifiers.remove(m)
hidden = set()
if char.get("outfit"):
    # The canonical body as the garments were fitted to it: the archetype's own body and rig (its world transform included).
    ref = os.path.abspath(os.path.join(garments_dir, "..", "archetypes", char["archetype"], "archetype.blend"))
    before = set(bpy.data.objects)
    with bpy.data.libraries.load(ref, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in ("Human", "Human.rig")]
    loaded = [o for o in bpy.data.objects if o not in before]
    for o in loaded:
        bpy.context.scene.collection.objects.link(o)
    ref_obj = next(o for o in loaded if o.type == "MESH")
    bpy.context.view_layer.update()
    canon = common.shaped(ref_obj)
    for o in loaded:
        bpy.data.objects.remove(o)
    for gid in char["outfit"]:
        g = common.append(os.path.join(garments_dir, gid, gid + ".blend"), gid)
        common.conform(g, canon, body)
        common.skin(g, body, rig)
        hidden |= set(common.descriptor(gid)["hides"])
    common.hide_regions(body, regions, hidden)
bpy.ops.wm.save_as_mainfile(filepath=final)
print("BUILD_CHARACTER", final, {"identity_modifiers": len(preset["weights"]), "outfit": char.get("outfit", []), "hidden": sorted(hidden)})
