"""A garment authored ONCE onto its archetype's canonical body (charstd): driven entirely by its descriptor (garments/<id>.json) - the
source fitted to the canonical body, an optional cut (the part of a larger garment kept), its material (base colour, normal,
roughness), an optional layer offset (the standard's layer offsets: padding 0.008, mail 0.016, plate 0.024 m), and any small authored
edits the descriptor lists. Saved as <out>/<id>.blend (the garment object alone, world space, images packed) with <id>_meta.json.

    blender -b --python author_garment.py -- --archetype archetype.blend --id <garment id> --out <dir> [--assets <asset root>]

Sources ("source.kind"): mpfb_clothes (an MPFB/MakeHuman wardrobe asset, fitted by its own mhclo); blend (a mesh already fitted to
the canonical body in another .blend - a sourced or generated garment adapted by hand). Texture paths in the descriptor are relative
to the asset root (default G:/UNNAMED_PHASEB/assets); "mpfb:<file>" means a file in the MPFB asset's own folder.
"""
import hashlib
import json
import os
import sys

import bpy
import bmesh
import addon_utils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
gid = arg("--id")
out = os.path.abspath(arg("--out"))
assets = os.path.abspath(arg("--assets", "G:/UNNAMED_PHASEB/assets"))
os.makedirs(out, exist_ok=True)
desc = common.descriptor(gid)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--archetype")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import HumanService, LocationService, ObjectService  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
src = desc["source"]
folder = None
if src["kind"] == "mpfb_clothes":
    folder = os.path.join(LocationService.get_user_data(), "clothes", src["asset"])
    mhclo = next(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".mhclo"))
    g = HumanService.add_mhclo_asset(mhclo, body, asset_type="Clothes", subdiv_levels=0, material_type="MAKESKIN")
elif src["kind"] == "blend":
    g = common.append(os.path.join(assets, src["blend"]), src["object"])
else:
    raise SystemExit(f"unknown source kind {src['kind']}")

# World space, no parent, no modifiers: the garment as fitted.
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
fitted = g.evaluated_get(dg).to_mesh()
co = [g.matrix_world @ v.co for v in fitted.vertices]
g.evaluated_get(dg).to_mesh_clear()
for m in list(g.modifiers):
    g.modifiers.remove(m)
g.parent = None
g.matrix_world.identity()
if g.data.shape_keys:
    g.shape_key_clear()
for v, c in zip(g.data.vertices, co):
    v.co = c

# The cut: the faces the kept part's bones carry, and everything below a height (a one-piece garment worn as its lower half).
cut = desc.get("cut")
if cut:
    groups = {x.index: x.name for x in g.vertex_groups}
    rename = common.load(os.path.join(common.HERE, "skeleton_humanoid_a.json"))["rename_from_mpfb_game_engine"]
    bm = bmesh.new()
    bm.from_mesh(g.data)
    dl = bm.verts.layers.deform.active
    doomed = []
    for f in bm.faces:
        tot = {}
        for v in f.verts:
            for gi, w in v[dl].items():
                n = rename.get(groups.get(gi, ""), groups.get(gi, ""))
                tot[n] = tot.get(n, 0) + w
        dom = max(tot, key=tot.get) if tot else ""
        keep = dom.startswith(tuple(cut.get("keep_bones", [])))
        low = f.calc_center_median().z < cut.get("keep_below_m", -1e9) and dom.startswith(tuple(cut.get("keep_below_bones", [""])))
        if not keep and not low:
            doomed.append(f)
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(g.data)
    bm.free()
for x in list(g.vertex_groups):
    g.vertex_groups.remove(x)   # weights come from the instance's body at build time (common.skin)

# The layer offset: outward along the normals.
off = float(desc.get("offset_m", 0.0))
if off:
    g.data.update()
    for v in g.data.vertices:
        v.co += v.normal * off

# The material: one per garment, by the standard's naming, its images packed.
mat_desc = desc["material"]


def texture(path):
    if path is None:
        return None
    if path.startswith("mpfb:"):
        return os.path.join(folder, path[5:])
    return os.path.join(assets, path)


mat = bpy.data.materials.new(f"MAT_{gid}_base")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
bc = nt.nodes.new("ShaderNodeTexImage")
bc.image = bpy.data.images.load(texture(mat_desc["base_color"]))
nt.links.new(bc.outputs["Color"], bsdf.inputs["Base Color"])
if mat_desc.get("roughness_map"):
    rt = nt.nodes.new("ShaderNodeTexImage")
    rt.image = bpy.data.images.load(texture(mat_desc["roughness_map"]))
    rt.image.colorspace_settings.name = "Non-Color"
    nt.links.new(rt.outputs["Color"], bsdf.inputs["Roughness"])
else:
    bsdf.inputs["Roughness"].default_value = float(mat_desc.get("roughness", 0.8))
if mat_desc.get("normal"):
    nm = nt.nodes.new("ShaderNodeTexImage")
    nm.image = bpy.data.images.load(texture(mat_desc["normal"]))
    nm.image.colorspace_settings.name = "Non-Color"
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nmap.inputs["Strength"].default_value = float(mat_desc.get("normal_strength", 1.0))
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
if mat_desc.get("uv_scale"):
    # A tiling material (a sourced PBR set) on the garment's own UVs.
    uvn = nt.nodes.new("ShaderNodeTexCoord")
    mapn = nt.nodes.new("ShaderNodeMapping")
    mapn.inputs["Scale"].default_value = (mat_desc["uv_scale"], mat_desc["uv_scale"], 1)
    nt.links.new(uvn.outputs["UV"], mapn.inputs["Vector"])
    for n in nt.nodes:
        if n.type == "TEX_IMAGE":
            nt.links.new(mapn.outputs["Vector"], n.inputs["Vector"])
g.data.materials.clear()
g.data.materials.append(mat)
for p in g.data.polygons:
    p.material_index = 0
g.name = gid
g.data.name = gid + "_mesh"

# Save the garment alone.
for o in list(bpy.data.objects):
    if o is not g:
        bpy.data.objects.remove(o)
bpy.ops.file.pack_all()
blend = os.path.join(out, gid + ".blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
tris = sum(len(p.vertices) - 2 for p in g.data.polygons)
meta = {"id": gid, "archetype": desc["archetype"], "tris": tris, "verts": len(g.data.vertices),
        "bbox_m": [[round(min(v.co[i] for v in g.data.vertices), 4) for i in range(3)],
                   [round(max(v.co[i] for v in g.data.vertices), 4) for i in range(3)]],
        "blend_sha256": hashlib.sha256(open(blend, "rb").read()).hexdigest()}
json.dump(meta, open(os.path.join(out, gid + "_meta.json"), "w"), indent=1)
print("AUTHOR_GARMENT", json.dumps(meta))
