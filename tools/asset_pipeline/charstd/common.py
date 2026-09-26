"""Blender-side helpers shared by the character standard's tools (charstd; CHARACTER_ASSET_STANDARD.md). Every garment goes through the
same three generic steps - conform (to an instance's body), skin (weights from the body) and region hiding (the body's own regions,
named by the garment's descriptor) - so a new garment needs a descriptor and, at most, a small authored edit; never new code.
"""
import json
import os
from collections import Counter

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))


def load(path):
    return json.load(open(path, encoding="utf-8"))


def descriptor(garment_id):
    return load(os.path.join(HERE, "garments", garment_id + ".json"))


def shaped(obj):
    """World positions of an object's vertices as posed at rest with its shape keys, its masks off (its own vertex indices)."""
    masks = [m for m in obj.modifiers if m.type == "MASK" and m.show_viewport]
    for m in masks:
        m.show_viewport = False
    e = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = e.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    e.to_mesh_clear()
    for m in masks:
        m.show_viewport = True
    M = np.array(obj.matrix_world)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


def append(blend, name):
    """An object from another .blend, linked into this scene (its materials and packed images with it)."""
    with bpy.data.libraries.load(blend, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n == name]
    if not dst.objects:
        raise SystemExit(f"{name} not in {blend}")
    obj = dst.objects[0]
    bpy.context.scene.collection.objects.link(obj)
    return obj


def face_regions(body, regions):
    """Each face's region: the region most of its vertices belong to (the exported body is split by exactly this)."""
    of = {}
    for r, idx in regions["regions"].items():
        for i in idx:
            of[i] = r
    out = []
    for p in body.data.polygons:
        names = [of[v] for v in p.vertices if v in of]
        out.append(Counter(names).most_common(1)[0][0] if names else None)
    return out


def conform(garment, canonical_body_co, body):
    """The garment (fitted once to the archetype's canonical body) carried onto this instance's body: a Surface Deform bound on the
    canonical body, then the body morphed to the instance's shape (same topology, so a shape key). World space throughout."""
    polys = [tuple(p.vertices) for p in body.data.polygons]
    me = bpy.data.meshes.new("conform_ref")
    me.from_pydata([tuple(c) for c in canonical_body_co], [], polys)
    ref = bpy.data.objects.new("conform_ref", me)
    bpy.context.scene.collection.objects.link(ref)
    ref.shape_key_add(name="Basis")
    key = ref.shape_key_add(name="instance")
    inst = shaped(body)
    key.data.foreach_set("co", inst.astype(np.float32).ravel())
    key.value = 0.0
    garment.parent = None
    mod = garment.modifiers.new("conform", "SURFACE_DEFORM")
    mod.target = ref
    bpy.context.view_layer.objects.active = garment
    with bpy.context.temp_override(object=garment, active_object=garment):
        bpy.ops.object.surfacedeform_bind(modifier=mod.name)
    key.value = 1.0
    bpy.context.view_layer.update()
    with bpy.context.temp_override(object=garment, active_object=garment):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    moved = float(np.abs(inst - canonical_body_co).max())
    bpy.data.objects.remove(ref)
    return moved


def skin(garment, body, rig, max_influences=4):
    """The garment's bone weights from the body's skin under it (nearest face, interpolated), limited and normalised, and the garment
    bound to the rig - the standard weight transfer; exceptional joints are an authored clean-up, not code."""
    for g in list(garment.vertex_groups):
        garment.vertex_groups.remove(g)
    deform = [b.name for b in rig.data.bones if b.use_deform]
    for n in deform:
        if n in body.vertex_groups:
            garment.vertex_groups.new(name=n)
    masks = [m for m in body.modifiers if m.type == "MASK" and m.name != "Hide helpers" and m.show_viewport]
    for m in masks:
        m.show_viewport = False
    dt = garment.modifiers.new("weights", "DATA_TRANSFER")
    dt.object = body
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    bpy.context.view_layer.objects.active = garment
    with bpy.context.temp_override(object=garment, active_object=garment):
        bpy.ops.object.modifier_apply(modifier=dt.name)
    for m in masks:
        m.show_viewport = True
    for g in list(garment.vertex_groups):
        if g.name not in deform:
            garment.vertex_groups.remove(g)
    with bpy.context.temp_override(object=garment, active_object=garment):
        bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=max_influences)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    world = garment.matrix_world.copy()
    garment.parent = rig
    garment.matrix_world = world
    arm = garment.modifiers.new("Armature", "ARMATURE")
    arm.object = rig


def hide_regions(body, regions, hidden):
    """The body with the named regions' faces removed (what the game does by hiding those region meshes)."""
    import bmesh
    if not hidden:
        return 0
    which = face_regions(body, regions)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.faces.ensure_lookup_table()
    doomed = [bm.faces[i] for i, r in enumerate(which) if r in hidden]
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bm.to_mesh(body.data)
    bm.free()
    return len(doomed)


def tag_variant_faces(body, regions, hides, variants):
    """Outfit variants (one character, several outfits the game switches between, e.g. the player's base top and the hide vest): each
    face of a region some variants hide and others do not gets the bitmask of the variants that SHOW it in the face attribute
    `outfit_show` (bit i = variants[i]); the export turns each distinct mask into its own body surface and the runtime hides the
    surfaces the current variant does not show. Faces every variant hides must already be removed; untouched faces carry all bits."""
    which = face_regions(body, regions)
    full = (1 << len(variants)) - 1
    attr = body.data.attributes.get("outfit_show") or body.data.attributes.new("outfit_show", "INT", "FACE")
    masks = []
    for r in which:
        m = full
        for i, v in enumerate(variants):
            if r in hides[v]:
                m &= ~(1 << i)
        masks.append(m)
    attr.data.foreach_set("value", masks)
    body["outfit_variants"] = ",".join(variants)
    return Counter(masks)

