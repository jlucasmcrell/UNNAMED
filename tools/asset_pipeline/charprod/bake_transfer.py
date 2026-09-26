"""Blender: carry a reconstruction's maps from its own UV layout onto the production layout - albedo, ORM and the
tangent-space normal map - on the same mesh (TEXCOORD_0 = the new layout, TEXCOORD_1 = the old one), so nothing is
ray-cast and nothing is resampled twice.

The colour and ORM maps are baked as emission through the old UVs; the normal map is decoded in the old layout's
tangent space and re-encoded in the new one's (Cycles' MikkTSpace, the tangent basis Godot also derives), OpenGL
convention (+Y), as glTF and Godot read it.

    blender --background --factory-startup --python bake_transfer.py -- --input layout.glb --outdir DIR
        (--albedo a.png --normal n.png --orm orm.png | --source MATERIAL a.png n.png orm.png ...) [--size 4096] [--margin 16]

A grafted part (head_graft.py's "head_src") reads its own maps: one --source per material slot.
"""
import argparse
import os
import sys

import bpy


def args():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--albedo", help="the maps of every material slot without a --source of its own")
    ap.add_argument("--normal")
    ap.add_argument("--orm")
    ap.add_argument("--source", nargs=4, action="append", default=[], metavar=("MATERIAL", "ALBEDO", "NORMAL", "ORM"),
                    help="the maps the faces of one material slot read (a grafted part keeps its own)")
    ap.add_argument("--outdir", required=True)
    ap.add_argument("--size", type=int, default=4096)
    ap.add_argument("--margin", type=int, default=16)
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:])


def gpu(scene):
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = kind
            prefs.get_devices()
            if any(d.type == kind for d in prefs.devices):
                for d in prefs.devices:
                    d.use = d.type == kind
                scene.cycles.device = "GPU"
                return kind
        except TypeError:
            continue
    return "CPU"


def main():
    a = args()
    os.makedirs(a.outdir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.input)
    obj = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups][0]
    me = obj.data
    names = [l.name for l in me.uv_layers]
    assert len(names) >= 2, f"expected the new and old UV sets, found {names}"
    new_uv, old_uv = names[0], names[1]
    me.uv_layers.active = me.uv_layers[new_uv]
    for l in me.uv_layers:
        l.active_render = l.name == new_uv

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    device = gpu(scene)
    scene.cycles.samples = 4
    scene.render.bake.margin = a.margin
    scene.render.bake.margin_type = "EXTEND"
    # Bakes see the pose the mesh is in; the armature's rest pose is the bind pose, so nothing moves.

    sources = {m: (al, no, orm) for m, al, no, orm in a.source}
    images = {}
    nets = []  # per slot: (node tree, output, emission, bsdf, albedo, orm, target)

    def network(maps):
        mat = bpy.data.materials.new("transfer")
        mat.use_nodes = True
        nt = mat.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        uvn = nt.nodes.new("ShaderNodeUVMap")
        uvn.uv_map = old_uv

        def texture(path, colour):
            n = nt.nodes.new("ShaderNodeTexImage")
            if path not in images:
                images[path] = bpy.data.images.load(path)
                images[path].colorspace_settings.name = "sRGB" if colour else "Non-Color"
            n.image = images[path]
            n.interpolation = "Cubic"
            nt.links.new(uvn.outputs["UV"], n.inputs["Vector"])
            return n

        albedo, normal, orm = texture(maps[0], True), texture(maps[1], False), texture(maps[2], False)
        emit = nt.nodes.new("ShaderNodeEmission")
        bsdf = nt.nodes.new("ShaderNodeBsdfDiffuse")
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nmap.space = "TANGENT"
        nmap.uv_map = old_uv
        nt.links.new(normal.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        target = nt.nodes.new("ShaderNodeTexImage")
        nets.append((nt, out, emit, bsdf, {"albedo": albedo, "orm": orm}, target))
        return mat

    default = (a.albedo, a.normal, a.orm)
    slots = [m.name if m else "" for m in me.materials] or [""]
    mats = []
    for name in slots:
        maps = sources.get(name, default)
        assert all(maps), f"no maps for material slot {name!r}"
        mats.append(network(maps))
    # In place: clearing the slots would reset every face's material index to 0.
    if not me.materials:
        me.materials.append(mats[0])
    for i, m in enumerate(mats):
        me.materials[i] = m
    for o in bpy.data.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    def bake(name, kind, source, colour):
        img = bpy.data.images.new(name, a.size, a.size, alpha=False, float_buffer=False)
        img.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        for nt, out, emit, bsdf, texs, target in nets:
            target.image = img
            nt.nodes.active = target
            for l in list(out.inputs["Surface"].links):
                nt.links.remove(l)
            if kind == "EMIT":
                nt.links.new(texs[source].outputs["Color"], emit.inputs["Color"])
                nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
            else:
                nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
        if kind == "EMIT":
            bpy.ops.object.bake(type="EMIT", use_clear=True, margin=a.margin)
        else:
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", use_clear=True, margin=a.margin)
        img.filepath_raw = os.path.join(a.outdir, f"{name}.png")
        img.file_format = "PNG"
        img.save()

    bake("albedo_transfer", "EMIT", "albedo", True)
    bake("orm_transfer", "EMIT", "orm", False)
    bake("normal_transfer", "NORMAL", None, False)
    print(f"BAKE_TRANSFER_DONE {device} {a.size} slots {slots} -> {a.outdir}")


main()
