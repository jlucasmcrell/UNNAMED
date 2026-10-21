"""MPFB 'Export copy' + Blender glTF export for one character.

Stage 1 uses MPFB's documented Export copy panel settings (bake shapekeys, bake mask
modifiers, delete helpers), stage 2 selects only the export-copy objects and writes a
GLB with all bone influences and applied modifiers.

MPFB's own docs document FBX for the actual file write; the glTF write therefore uses
Blender's bundled glTF exporter after MPFB's documented preparation.
"""

import bpy
import addon_utils
import json
import os
import sys
from pathlib import Path

addon_utils.enable("bl_ext.user_default.mpfb", default_set=True, persistent=True)


def arg(name, default=None):
    if "--" not in sys.argv:
        return default
    rest = sys.argv[sys.argv.index("--") + 1:]
    if name in rest:
        return rest[rest.index(name) + 1]
    return default


blend = arg("--blend")
outglb = arg("--outglb")
report_path = arg("--report")

bpy.ops.wm.open_mainfile(filepath=blend)

scene = bpy.context.scene

# --- MPFB Export copy panel settings ---
# Mask modifiers are removed rather than baked: gate G4 needs an inspectable undressed
# body, and MPFB documents "remove them without applying" as the alternative to baking.
# The delete group itself is still authored in the jerkin MHCLO.
scene.MPFB_EXPO_mask_modifiers = "REMOVE"
scene.MPFB_EXPO_subdiv_modifiers = "REMOVE"
scene.MPFB_EXPO_bake_shapekeys = True
scene.MPFB_EXPO_delete_helpers = True
scene.MPFB_EXPO_remove_basemesh = False
scene.MPFB_EXPO_visemes_meta = False
scene.MPFB_EXPO_visemes_microsoft = False
scene.MPFB_EXPO_faceunits_arkit = False
scene.MPFB_EXPO_interpolate = False
scene.MPFB_EXPO_suffix = "_export_copy"
scene.MPFB_EXPO_collection = True

rig = None
for obj in bpy.data.objects:
    if obj.type == "ARMATURE":
        rig = obj
        break
if rig is None:
    raise RuntimeError("no armature in %s" % blend)

bpy.ops.object.mode_set(mode="OBJECT", toggle=False)
bpy.ops.object.select_all(action="DESELECT")

# Documented Basemesh operation "Delete helpers": strips MPFB's internal helper
# geometry from the body so the exported mesh is a plain character surface. Without
# this, keeping (not baking) the mask modifiers would ship the helper shell.
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

basemesh = None
for obj in bpy.data.objects:
    if obj.type == "MESH" and ObjectService.object_is_basemesh(obj):
        basemesh = obj
        break
if basemesh is None:
    raise RuntimeError("no basemesh in %s" % blend)
bpy.context.view_layer.objects.active = basemesh
basemesh.select_set(True)
res = bpy.ops.mpfb.delete_helpers()
print("EXPORT delete_helpers", res, "body_verts", len(basemesh.data.vertices), flush=True)

bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
res = bpy.ops.mpfb.export_copy()
print("EXPORT copy_operator", res, flush=True)

# Find the export copy collection MPFB created.
copy_coll = None
for coll in bpy.data.collections:
    if coll.name.startswith("export copy") or coll.name.startswith("Export copy"):
        copy_coll = coll
        break
if copy_coll is None:
    raise RuntimeError("export copy collection not found; collections=%s" % [c.name for c in bpy.data.collections])
print("EXPORT collection", copy_coll.name, len(copy_coll.objects), flush=True)

copy_objects = list(copy_coll.all_objects)
copy_rig = None
for obj in copy_objects:
    if obj.type == "ARMATURE":
        copy_rig = obj
        break

inventory = []
for obj in copy_objects:
    entry = {
        "name": obj.name,
        "type": obj.type,
        "verts": len(obj.data.vertices) if obj.type == "MESH" else 0,
        "modifiers": [m.type for m in obj.modifiers],
        "materials": [s.material.name for s in obj.material_slots if s.material] if obj.type == "MESH" else [],
        "scale": list(obj.scale),
        "rotation_euler": list(obj.rotation_euler),
        "location": list(obj.location),
    }
    if obj.type == "MESH" and obj.data.shape_keys:
        entry["shape_keys"] = [k.name for k in obj.data.shape_keys.key_blocks]
    arm = obj.modifiers.get("Armature") if obj.type == "MESH" else None
    entry["armature"] = arm.object.name if arm and arm.object else None
    inventory.append(entry)

bones = [b.name for b in copy_rig.data.bones] if copy_rig else []
bone_deform = {b.name: b.use_deform for b in copy_rig.data.bones} if copy_rig else {}

# --- Select only the export copy geometry, then write the GLB ---
bpy.ops.object.select_all(action="DESELECT")
for obj in copy_objects:
    obj.hide_set(False)
    obj.select_set(True)
bpy.context.view_layer.objects.active = copy_rig if copy_rig else copy_objects[0]

os.makedirs(os.path.dirname(outglb), exist_ok=True)

# Apply the palette after MPFB Export Copy has rebuilt its materials.
recipe=json.loads(Path(os.environ['OTHERREACH_PIPELINE_ROOT'],'mpfb_named','recipes.json').read_text(encoding='utf-8'))
recipe=next(r for r in recipe if Path(outglb).parent.name==r['id'])
def find_principled(tree):
    for node in tree.nodes:
        if node.type=='BSDF_PRINCIPLED':return tree,node
    for node in tree.nodes:
        if node.type=='GROUP' and node.node_tree:
            node.node_tree=node.node_tree.copy()
            found=find_principled(node.node_tree)
            if found:return found
    return None
seen_materials=set()
for obj in copy_objects:
    if obj.type!='MESH':continue
    for mat in obj.data.materials:
        if not mat or not mat.use_nodes or mat.name in seen_materials:continue
        seen_materials.add(mat.name)
        label=mat.name.lower()
        rgb=recipe['skin_rgb'] if 'human.body' in label else recipe['hair_rgb'] if recipe['hair'].lower() in label or 'eyebrow' in label else None
        if rgb is None:continue
        found=find_principled(mat.node_tree)
        if found is None:raise RuntimeError('Palette material has no Principled node: '+mat.name)
        tree,bsdf=found
        socket=bsdf.inputs['Base Color']
        if socket.is_linked:
            original=socket.links[0].from_socket
            mix=tree.nodes.new('ShaderNodeMix');mix.data_type='RGBA';mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1
            a=next(i for i in mix.inputs if i.identifier=='A_Color');b=next(i for i in mix.inputs if i.identifier=='B_Color');b.default_value=(*rgb,1)
            tree.links.new(original,a);tree.links.new(next(o for o in mix.outputs if o.type=='RGBA'),socket)
        else:socket.default_value=(*rgb,1)
        print('PALETTE_NATIVE_MIX',mat.name,rgb,flush=True)

kwargs = dict(
    filepath=outglb,
    export_format="GLB",
    use_selection=True,
    export_apply=True,
    export_yup=True,
    export_all_influences=True,
    export_normals=True,
    export_tangents=True,
    export_skins=True,
    export_def_bones=False,
    export_animations=False,
    export_materials="EXPORT",
    export_image_format="AUTO",
    export_texcoords=True,
    export_extras=False,
    export_cameras=False,
    export_lights=False,
)
bpy.ops.export_scene.gltf(**kwargs)
print("EXPORT glb_written", outglb, os.path.getsize(outglb), flush=True)

report = {
    "blend": blend,
    "glb": outglb,
    "glb_bytes": os.path.getsize(outglb),
    "export_copy_collection": copy_coll.name,
    "objects": inventory,
    "rig": copy_rig.name if copy_rig else None,
    "bones": bones,
    "bone_count": len(bones),
    "non_deform_bones": [n for n, d in bone_deform.items() if not d],
    "gltf_kwargs": {k: v for k, v in kwargs.items() if k != "filepath"},
}
with open(report_path, "w", encoding="utf-8") as fh:
    json.dump(report, fh, indent=2)
print("EXPORT report", report_path, flush=True)
