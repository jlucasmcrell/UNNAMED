"""Blender: the authored LOD chain of a production character - skinned copies of the finished mesh, decimated, in the same
GLB on the same skeleton, materials and atlas (no texture is duplicated), named <mesh>_LOD1 / _LOD2. The face's skin
and, in part, the hands are protected (a vertex group biases the collapse away from them); the eyeballs stay in LOD1 and leave
LOD2 (they are a few pixels there). The engine picks a level by distance (visibility ranges, CharacterMaterials /
character_materials.json "lods"), keeping LOD0 at dialogue and third-person distance.

The Phase-A people had LOD files built and gated but never drawn: SkinnedModel had no LOD path, so every NPC at any
distance drew its full mesh.

    blender --background --factory-startup --python lods.py -- --input character_rigged.glb --out character_rigged.glb
        [--ratios 0.3,0.09] [--protect head,neck,hand.L,hand.R] [--report r.json]
"""
import argparse
import json
import sys

import bpy


def args():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--ratios", default="0.3,0.09")
    ap.add_argument("--protect", default="head,neck,hand.L,hand.R,eye.L,eye.R")
    ap.add_argument("--protect-strength", type=float, default=1.0)
    ap.add_argument("--report")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:])


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def protected_tris(obj, keep):
    vs = obj.data.vertices
    n = 0
    for p in obj.data.polygons:
        w = sum(max((g.weight for g in vs[i].groups if g.group == keep.index), default=0.0) for i in p.vertices) / len(p.vertices)
        n += (len(p.vertices) - 2) if w > 0.5 else 0
    return n


def main():
    a = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.input)
    obj = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups][0]
    for o in [o for o in bpy.data.objects if o.type == "MESH" and o is not obj]:
        bpy.data.objects.remove(o)
    # Protected: the face's skin fully (head-weighted faces of a skin material) and the hands in part; the hair and
    # everything else decimate freely (a hair shell is a third of a character's triangles and is the first to go).
    protect = set(a.protect.split(","))
    heads = {g.index for g in obj.vertex_groups if g.name in ("head", "neck")}
    hands = {g.index for g in obj.vertex_groups if g.name in protect and g.name.startswith("hand")}
    skin_mats = {i for i, m in enumerate(obj.data.materials) if m and (m.name.endswith("_skin") or m.name == "eye")}
    face_verts = {v for p in obj.data.polygons if p.material_index in skin_mats for v in p.vertices}
    keep = obj.vertex_groups.new(name="lod_keep")
    for v in obj.data.vertices:
        head_w = sum(g.weight for g in v.groups if g.group in heads)
        hand_w = sum(g.weight for g in v.groups if g.group in hands)
        w = max(head_w if v.index in face_verts else 0.0, 0.6 * hand_w)
        if w > 0:
            keep.add([v.index], min(1.0, w), "REPLACE")
    eye_mat = next((i for i, m in enumerate(obj.data.materials) if m and m.name == "eye"), None)
    report = {"lod0": {"triangles": tris(obj), "protected": protected_tris(obj, keep)}, "levels": []}

    def make(level, ratio, invert):
        copy = obj.copy()
        copy.data = obj.data.copy()
        bpy.context.scene.collection.objects.link(copy)
        if level >= 2 and eye_mat is not None:
            import bmesh
            bm = bmesh.new()
            bm.from_mesh(copy.data)
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index == eye_mat], context="FACES")
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
            bm.to_mesh(copy.data)
            bm.free()
        mod = copy.modifiers.new("lod", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = True
        mod.vertex_group = "lod_keep"
        mod.vertex_group_factor = a.protect_strength
        mod.invert_vertex_group = invert
        bpy.context.view_layer.objects.active = copy
        for o in bpy.data.objects:
            o.select_set(o is copy)
        # the armature modifier must stay after the decimation: move the decimation first, then apply it
        while copy.modifiers.find("lod") > 0:
            bpy.ops.object.modifier_move_up(modifier="lod")
        bpy.ops.object.modifier_apply(modifier="lod")
        return copy

    # Which way the vertex group biases the collapse is measured, not assumed: the protected share kept higher wins.
    probe = []
    for invert in (False, True):
        c = make(1, float(a.ratios.split(",")[0]), invert)
        probe.append((protected_tris(c, c.vertex_groups["lod_keep"]) / max(tris(c), 1), invert))
        bpy.data.objects.remove(c)
    invert = max(probe)[1]
    report["protect_invert"] = invert
    report["protect_share_probe"] = [round(x, 3) for x, _ in probe]
    for level, ratio in enumerate([float(r) for r in a.ratios.split(",")], start=1):
        c = make(level, ratio, invert)
        c.name = f"{obj.name}_LOD{level}"
        c.data.name = f"{obj.data.name}_LOD{level}"
        c.vertex_groups.remove(c.vertex_groups["lod_keep"])
        report["levels"].append({"name": c.name, "triangles": tris(c)})
    obj.vertex_groups.remove(keep)
    bpy.ops.export_scene.gltf(filepath=a.out, export_format="GLB", use_selection=False, export_apply=False, export_skins=True,
                              export_tangents=True)
    if a.report:
        json.dump(report, open(a.report, "w"), indent=1)
    print("LODS_RESULT " + json.dumps(report))


main()
