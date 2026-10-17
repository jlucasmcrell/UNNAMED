"""WP17 outfit v8 boots: finish the carried Viking boots (Rehman Polanski's BootsViking, CC0, carried by bl_hm08_carry.py) into
the player boot - smooth, with leather thickness, and a scan-leather upper in place of the item's 1K tile.

blender -b --factory-startup --python bl_finish_boots.py -- --source <carried .glb> --spec <carried .json> --scan <leather dir>
    --out <dir> [--tile-m 0.25] [--size 2048]

The item's upper is unwrapped as one disc (the welt and its stitching round the rim, the shaft top squeezed into the centre), so
its tiled leather smears into blocks up the shaft. Here:
1. weld the carried faces (the carry writes them flat-split) and shade smooth, 3 mm Solidify outward
   (the inner surface stays where the carry put it) so the shaft top reads as leather, not a sheet;
2. a new UV layout (Smart UV Project) at one texel density, and a 2K albedo and normal baked onto it (Cycles, CPU) from:
   the scan leather tiled at --tile-m metres (colour-matched to the item's own leather) over the upper and the strap, and the
   item's own painted welt, stitching and sole tread where they are (the upper disc's rim, 7 % of the texture, and the sole);
Writes <out>/<id>.glb and <id>.json (the carried spec, provenance extended).
"""
import argparse
import glob
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("--source", required=True)
ap.add_argument("--spec", required=True)
ap.add_argument("--scan", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--tile-m", type=float, default=0.25)
ap.add_argument("--size", type=int, default=2048)
a = ap.parse_args(argv)
RIM = 0.045  # the welt band round the upper disc, in UV units (the item's painted welt and stitching)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=a.source)
g = next(o for o in bpy.data.objects if o.type == "MESH")
spec = json.load(open(a.spec))
src_img = next(n.image for n in g.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE")

# 1. geometry
bm = bmesh.new()
bm.from_mesh(g.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
bm.to_mesh(g.data)
bm.free()
for p in g.data.polygons:
    p.use_smooth = True
bpy.context.view_layer.objects.active = g
g.select_set(True)
sol = g.modifiers.new("solid", "SOLIDIFY")
sol.thickness, sol.offset, sol.use_rim = 0.003, 1.0, True
for m in list(g.modifiers):
    bpy.ops.object.modifier_apply(modifier=m.name)

# the item's islands: which texels keep its own paint (welt band, sole) and which take the scan (upper, strap)
S = src_img.size[0]
own = np.zeros((S, S), bool)
uv0 = g.data.uv_layers[0]
OLD_UV = uv0.name
coords = np.zeros(len(g.data.loops) * 2)
uv0.data.foreach_get("uv", coords)
coords = coords.reshape(-1, 2)
upper = np.zeros((S, S), bool)
for p in g.data.polygons:
    c = coords[list(p.loop_indices)]
    if c[:, 1].max() < 0.71:   # the upper disc lies below v 0.71, the sole and strap above it
        lo, hi = np.floor(c.min(0) * S).astype(int), np.ceil(c.max(0) * S).astype(int)
        upper[max(lo[1], 0):min(hi[1], S), max(lo[0], 0):min(hi[0], S)] = True
inner = upper.copy()
for _ in range(int(RIM * S)):
    inner[1:-1, 1:-1] &= inner[:-2, 1:-1] & inner[2:, 1:-1] & inner[1:-1, :-2] & inner[1:-1, 2:]
strap = np.zeros((S, S), bool)
strap[int(0.718 * S):int(0.86 * S), int(0.52 * S):] = True
scan_mask = inner | strap
mask_img = bpy.data.images.new("scan_mask", S, S, float_buffer=False)
px = np.repeat(scan_mask.astype(np.float32)[..., None], 4, -1)
px[..., 3] = 1.0
mask_img.pixels.foreach_set(px.ravel())
mask_img.colorspace_settings.name = "Non-Color"

# colour match: the scan's mean onto the item's own upper leather
own_px = np.array(src_img.pixels[:]).reshape(S, S, 4)[..., :3]
target = own_px[inner].mean(0)
albedo = bpy.data.images.load(glob.glob(os.path.join(a.scan, "*albedo*4k*")) [0] if glob.glob(os.path.join(a.scan, "*albedo*4k*"))
                              else glob.glob(os.path.join(a.scan, "*diff*4k*"))[0])
normal = bpy.data.images.load(glob.glob(os.path.join(a.scan, "*nor_gl*4k*"))[0])
normal.colorspace_settings.name = "Non-Color"
small = albedo.copy()
small.scale(256, 256)
scan_mean = np.array(small.pixels[:]).reshape(-1, 4)[:, :3].mean(0)
gain = target / np.maximum(scan_mean, 1e-4)

# 2. new layout and bake
area = sum(p.area for p in g.data.polygons)
new_uv = g.data.uv_layers.new(name="bake")
g.data.uv_layers.active = new_uv
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004)
bpy.ops.object.mode_set(mode="OBJECT")
uv_area = 0.0
nc = np.zeros(len(g.data.loops) * 2)
new_uv.data.foreach_get("uv", nc)
nc = nc.reshape(-1, 2)
for p in g.data.polygons:
    c = nc[list(p.loop_indices)]
    uv_area += 0.5 * abs(sum(c[i, 0] * c[i - 1, 1] - c[i - 1, 0] * c[i, 1] for i in range(len(c))))
metres_per_uv = math.sqrt(area / uv_area)

mat = bpy.data.materials.new("bake")
mat.use_nodes = True
nt = mat.node_tree
N = nt.nodes
L = nt.links
bsdf = N["Principled BSDF"]
uv_old, uv_new = N.new("ShaderNodeUVMap"), N.new("ShaderNodeUVMap")
uv_old.uv_map, uv_new.uv_map = OLD_UV, "bake"
tile = N.new("ShaderNodeMapping")
tile.inputs["Scale"].default_value = (metres_per_uv / a.tile_m,) * 2 + (1.0,)
L.new(uv_new.outputs["UV"], tile.inputs["Vector"])
t_own, t_mask, t_alb, t_nor = (N.new("ShaderNodeTexImage") for _ in range(4))
t_own.image, t_mask.image, t_alb.image, t_nor.image = src_img, mask_img, albedo, normal
for t in (t_own, t_mask):
    L.new(uv_old.outputs["UV"], t.inputs["Vector"])
for t in (t_alb, t_nor):
    L.new(tile.outputs["Vector"], t.inputs["Vector"])
tint = N.new("ShaderNodeMix")
tint.data_type, tint.blend_type = "RGBA", "MULTIPLY"
tint.inputs["Factor"].default_value = 1.0
L.new(t_alb.outputs["Color"], tint.inputs[6])
tint.inputs[7].default_value = (*gain, 1.0)
mix = N.new("ShaderNodeMix")
mix.data_type = "RGBA"
L.new(t_mask.outputs["Color"], mix.inputs["Factor"])
L.new(t_own.outputs["Color"], mix.inputs[6])
L.new(tint.outputs[2], mix.inputs[7])
nmap_own = N.new("ShaderNodeNormalMap")   # the item has no normal map: flat where its paint stays
nmap_scan = N.new("ShaderNodeNormalMap")
nmap_scan.uv_map = "bake"
L.new(t_nor.outputs["Color"], nmap_scan.inputs["Color"])
nmix = N.new("ShaderNodeMix")
nmix.data_type = "VECTOR"
L.new(t_mask.outputs["Color"], nmix.inputs["Factor"])
L.new(nmap_own.outputs["Normal"], nmix.inputs[4])
L.new(nmap_scan.outputs["Normal"], nmix.inputs[5])
L.new(nmix.outputs[1], bsdf.inputs["Normal"])
emit = N.new("ShaderNodeEmission")
L.new(mix.outputs[2], emit.inputs["Color"])
out_node = N["Material Output"]
g.data.materials.clear()
g.data.materials.append(mat)

sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.device = "CPU"
sc.cycles.samples = 4
sc.render.bake.margin = 8
gid = spec["id"]
os.makedirs(a.out, exist_ok=True)
baked = {}
for kind in ("albedo", "normal"):
    img = bpy.data.images.new(f"{gid}_{kind}", a.size, a.size)
    if kind == "normal":
        img.colorspace_settings.name = "Non-Color"
    target_node = N.new("ShaderNodeTexImage")
    target_node.image = img
    N.active = target_node
    if kind == "albedo":
        L.new(emit.outputs["Emission"], out_node.inputs["Surface"])
        bpy.ops.object.bake(type="EMIT", uv_layer="bake")
    else:
        L.new(bsdf.outputs["BSDF"], out_node.inputs["Surface"])
        bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", uv_layer="bake")
    img.filepath_raw = os.path.join(a.out, f"{gid}_{kind}.png")
    img.file_format = "PNG"
    img.save()
    baked[kind] = img
    N.remove(target_node)

# the game material: the baked maps on the new layout only
g.data.uv_layers.remove(g.data.uv_layers[OLD_UV])
g.data.uv_layers["bake"].name = "UVMap"
game = bpy.data.materials.new(f"{gid}_leather")
game.use_nodes = True
gb = game.node_tree.nodes["Principled BSDF"]
ta, tn = game.node_tree.nodes.new("ShaderNodeTexImage"), game.node_tree.nodes.new("ShaderNodeTexImage")
ta.image, tn.image = baked["albedo"], baked["normal"]
nm = game.node_tree.nodes.new("ShaderNodeNormalMap")
game.node_tree.links.new(ta.outputs["Color"], gb.inputs["Base Color"])
game.node_tree.links.new(tn.outputs["Color"], nm.inputs["Color"])
game.node_tree.links.new(nm.outputs["Normal"], gb.inputs["Normal"])
gb.inputs["Roughness"].default_value = 0.72
gb.inputs["Metallic"].default_value = 0.0
g.data.materials.clear()
g.data.materials.append(game)
bpy.ops.object.select_all(action="DESELECT")
g.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.join(a.out, f"{gid}.glb"), use_selection=True, export_format="GLB", export_animations=False)
spec["provenance"] += (f" Finished by tools/character_build/bl_finish_boots.py: welded and smoothed, 3 mm leather thickness, re-unwrapped,"
                       f" upper and strap in {os.path.basename(os.path.normpath(a.scan))} (Poly Haven, CC0) at {a.tile_m} m,"
                       " the item's own welt, stitching and sole kept.")
json.dump(spec, open(os.path.join(a.out, f"{gid}.json"), "w"), indent=1)
print("BOOTS", gid, len(g.data.vertices), "vertices", sum(len(p.vertices) - 2 for p in g.data.polygons), "tris",
      "metres/uv", round(metres_per_uv, 3), "gain", gain.round(3).tolist())
