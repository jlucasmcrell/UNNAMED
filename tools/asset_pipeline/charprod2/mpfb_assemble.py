"""An MPFB character dressed and textured to its reference (charprod2): the composited skin (composite_skin.py), the iris, the re-dyed
hair and garments (garment_textures.py); the one-piece casual suit split by its own bone weights into trousers (its top dropped; a separate tank is worn), and the
skin the dropped parts hid shown again. Materials are named by kind
(<prefix>_skin, _hair, _cloth, _cloth_heavy, _leather; eye) for the game's character materials.

    blender -b --python mpfb_assemble.py -- --blend base.blend --spec specs/player.json --work <dir> --out assembled.blend
"""
import json
import math
import os
import sys

import bpy
import addon_utils
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
spec = json.load(open(arg("--spec")))
work = os.path.abspath(arg("--work"))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService, LocationService  # noqa: E402

prefix = spec.get("material_prefix", "MAT_" + spec["id"])
mp = spec["mpfb"]
body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
kind = {o: ObjectService.get_object_type(o) for o in bpy.data.objects if o.type == "MESH"}


def principled(name, image_path, roughness=0.6, alpha=False, normal_from=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(image_path)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = roughness
    if alpha:
        nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        mat.blend_method = "CLIP" if hasattr(mat, "blend_method") else None
    if normal_from and os.path.exists(normal_from):
        nimg = nt.nodes.new("ShaderNodeTexImage")
        nimg.image = bpy.data.images.load(normal_from)
        nimg.image.colorspace_settings.name = "Non-Color"
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nimg.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def replace_all(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.material_index = 0


mpfb_data = LocationService.get_user_data()
facefit = os.path.join(work, "facefit")
replace_all(body, principled(prefix + "_skin", os.path.join(facefit, "body_albedo.png"), roughness=0.65))
tex = os.path.join(work, "tex")
for o, k in kind.items():
    if k == "Eyes" and mp.get("eye_texture"):
        for m in o.data.materials:
            if m and m.node_tree:
                for n in m.node_tree.nodes:
                    if n.type == "TEX_IMAGE" and n.image and "_eye" in n.image.name:
                        n.image = bpy.data.images.load(os.path.join(mpfb_data, mp["eye_texture"]))
                m.name = "eye" if "cornea" not in m.name.lower() else m.name
    elif k == "Hair":
        replace_all(o, principled(prefix + "_hair", os.path.join(tex, "hair.png"), roughness=0.5, alpha=True,
                                  normal_from=os.path.join(mpfb_data, "hair", mp["hair"], "short_messy_objnorm.png")))

# The garments by the spec's "wear": each asset's part kept ("legs": only what the pelvis and leg bones carry, "all": everything), dyed, and
# named by kind. Then the skin: shown wherever no kept garment now covers it (a one-piece suit worn as trousers only).
LEG = ("thigh", "calf", "foot", "ball", "pelvis")   # pelvis: the waistband and seat, so the trousers meet the top's hem


def keep_legs(obj, top_z):
    """Faces the pelvis and legs carry, and any face below top_z (the trousers rise under the top's hem, so no skin shows between)."""
    groups = {g.index: g.name for g in obj.vertex_groups}
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    dl = bm.verts.layers.deform.active
    doomed = []
    for f in bm.faces:
        tot = {}
        for v in f.verts:
            for gi, w in v[dl].items():
                n = groups.get(gi, "")
                tot[n] = tot.get(n, 0) + w
        if not (tot and max(tot, key=tot.get).startswith(LEG)) and (obj.matrix_world @ f.calc_center_median()).z >= top_z:
            doomed.append(f)
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()


def shaped(obj):
    """World positions and normals of an object's vertices as posed at rest with its shape keys (MPFB shapes the body through
    them; the base mesh's own coordinates are the neutral MakeHuman figure), its masks off so the indices are its own."""
    masks = [m for m in obj.modifiers if m.type == "MASK" and m.show_viewport]
    for m in masks:
        m.show_viewport = False
    dg = bpy.context.evaluated_depsgraph_get()
    e = obj.evaluated_get(dg)
    me = e.to_mesh()
    nm = obj.matrix_world.to_3x3()
    co = [obj.matrix_world @ v.co for v in me.vertices]
    nrm = [(nm @ v.normal).normalized() for v in me.vertices]
    polys = [tuple(p.vertices) for p in me.polygons]
    e.to_mesh_clear()
    for m in masks:
        m.show_viewport = True
    return co, nrm, polys


def wear_of(o):
    asset = next((a for a in mp.get("wear", {}) if o.name.endswith(a)), None)
    return asset, (mp["wear"][asset] if asset else None)


# The hem of the tops worn whole (garments whose middle is above the body's): the lowest point of any, plus the overlap.
body_co, body_n, _ = shaped(body)
mid = (min(c.z for c in body_co) + max(c.z for c in body_co)) / 2
tops = [sorted(c.z for c in shaped(o)[0]) for o, k in kind.items() if k == "Clothes" and wear_of(o)[1] and wear_of(o)[1]["part"] == "all"]
hem = min((z[0] for z in tops if z[len(z) // 2] > mid), default=-1e9)
worn = []
for o, k in kind.items():
    if k != "Clothes":
        continue
    asset, w = wear_of(o)
    if asset is None:
        continue
    if w["part"] == "legs":
        keep_legs(o, hem + 0.04)
    normal = next((os.path.join(mpfb_data, "clothes", asset, f) for f in os.listdir(os.path.join(mpfb_data, "clothes", asset))
                   if "normal" in f.lower() and f.endswith(".png")), None)
    replace_all(o, principled(f"{prefix}_{w['kind']}", os.path.join(tex, w["texture"] + ".png"), roughness=w["roughness"], normal_from=normal))
    o.name = f"Human.{w['texture']}"
    worn.append((asset, o, w["part"]))

cover = [BVHTree.FromPolygons(*shaped(o)[::2]) for _, o, _ in worn]


def covered(p, n):
    # A garment covers the skin where a ray out along the skin's normal meets it within 2.5 cm on a surface lying over the skin
    # (roughly parallel to it): skin beside an armhole or hem also meets the garment's binding, but edge-on, not over it.
    for bvh in cover:
        loc, hn, _, _ = bvh.ray_cast(p, n, 0.025)
        if loc is not None and abs(hn.dot(n)) > 0.5:
            return True
    return False


for asset, o, part in worn:
    dg = body.vertex_groups.get("Delete." + asset)
    if dg is None:
        continue
    # MPFB's authored masks hide more than a garment covers where it is cut or does not fit this body exactly (ragged holes at
    # armholes and hems): the skin is hidden only where some kept garment actually covers it.
    freed = []
    for v in body.data.vertices:
        if any(g.group == dg.index for g in v.groups):
            n = body_n[v.index]
            if not covered(body_co[v.index] - n * 0.002, n):
                freed.append(v.index)
    dg.remove(freed)
    print("SKIN_SHOWN", asset, len(freed))

# The mask drops every face that touches a hidden vertex, so skin beside a garment's edge was lost with its covered neighbour
# (ragged holes between the skin and the garment's edge): the shown skin grows two rings in under the garments' open edges (hems,
# armholes, necklines) - only there, so the skin under the middle of a garment stays hidden and cannot poke through it.
edge_pts = []
for _, o, _ in worn:
    oco = shaped(o)[0]
    bm = bmesh.new()
    bm.from_mesh(o.data)
    edge_pts += [oco[e.verts[0].index].lerp(oco[e.verts[1].index], t) for e in bm.edges if e.is_boundary for t in (0.0, 0.5)]
    bm.free()
kd = KDTree(len(edge_pts))
for i, p in enumerate(edge_pts):
    kd.insert(p, i)
kd.balance()
near_edge = {i for i, c in enumerate(body_co) if kd.find(c)[2] < 0.03}
masks = [body.vertex_groups["Delete." + a] for a, _, _ in worn if "Delete." + a in body.vertex_groups]
hidden = {v.index for v in body.data.vertices if any(g.group in {m.index for m in masks} for g in v.groups)}
grown = set()
for ring in range(2):
    edge = {a for e in body.data.edges for a, b in (e.vertices, tuple(reversed(e.vertices)))
            if a in hidden and b not in hidden and a in near_edge}
    hidden -= edge
    grown |= edge
for m in masks:
    m.remove(sorted(grown))
print("SKIN_RINGS", len(grown))

bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(arg("--out")))
print("ASSEMBLED", os.path.abspath(arg("--out")))
