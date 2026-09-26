"""Ground-cover grass as geometry (Phase B remediation, the grass_blade_clump archetype): a clump of individual blades, each a curved
tapered strip that leans out from the clump and droops toward its tip - real volume from every side, so no card ever shows. The
colour is a small strip texture (dark at the root, lighter toward the tip, eight hue columns: green to straw); vertex colour red is
the height along the blade (the wind shader bends by it) and green a per-blade phase. Three levels of detail, fewer and simpler
blades each. Project-owned; no source asset.

    blender -b --python _procgen_grass_blades.py -- --out G:/UNNAMED_PHASEB/assets/ready [--only veg_gen_grass_a]
"""
import json
import math
import os
import random
import sys

import bpy
import numpy as np

VARIANTS = {
    # blades, radius (m), height range (m), width range (m), lean (deg), columns (hue), seed
    "veg_gen_grass_a": dict(blades=110, radius=0.22, height=(0.10, 0.30), width=(0.007, 0.013), lean=(4, 26), columns=(0, 1, 2, 3), seed=11,
                            note="short dense turf"),
    "veg_gen_grass_b": dict(blades=80, radius=0.24, height=(0.20, 0.48), width=(0.007, 0.014), lean=(6, 30), columns=(1, 2, 3, 4), seed=23,
                            note="medium meadow grass"),
    "veg_gen_grass_c": dict(blades=44, radius=0.16, height=(0.38, 0.72), width=(0.005, 0.011), lean=(3, 20), columns=(2, 3, 4, 5), seed=37,
                            note="tall wispy grass"),
    "veg_gen_grass_d": dict(blades=70, radius=0.22, height=(0.14, 0.40), width=(0.007, 0.012), lean=(8, 34), columns=(4, 5, 6, 7), seed=41,
                            note="dry straw-yellow grass"),
}
LEVELS = [(1.0, 4), (0.5, 3), (0.25, 2)]   # share of blades, segments per blade
COLUMNS = 8


def strip_texture(path):
    """64 x 256: eight hue columns (deep green to straw), each darkening to the root and lightening to the tip."""
    tips = [(0.34, 0.47, 0.14), (0.40, 0.52, 0.16), (0.45, 0.55, 0.20), (0.52, 0.56, 0.22), (0.58, 0.58, 0.28), (0.66, 0.60, 0.33),
            (0.72, 0.64, 0.40), (0.78, 0.70, 0.47)]
    w, h = 64, 256
    img = np.zeros((h, w, 4), np.float32)
    rng = np.random.default_rng(5)
    for c, tip in enumerate(tips):
        x0, x1 = c * w // COLUMNS, (c + 1) * w // COLUMNS
        for y in range(h):
            t = 1 - y / (h - 1)                 # image top is the blade tip (v = 1)
            shade = 0.58 + 0.42 * (t ** 0.7)
            col = np.array(tip) * shade
            col = col * (1 + 0.05 * rng.standard_normal())
            img[y, x0:x1, :3] = np.clip(col, 0, 1)
        # a faint midrib down each column
        img[:, (x0 + x1) // 2, :3] *= 1.08
    img[..., 3] = 1
    im = bpy.data.images.new("grass_strip", w, h, alpha=True)
    im.pixels = np.flipud(img).ravel().tolist()
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im


def clump(name, spec, share, segments, image):
    rng = random.Random(spec["seed"])
    verts, faces, uvs, cols, norms = [], [], [], [], []
    n_blades = max(3, int(round(spec["blades"] * share)))
    for i in range(n_blades):
        r = spec["radius"] * math.sqrt(rng.random())
        a = rng.random() * math.tau
        base = np.array([math.cos(a) * r, math.sin(a) * r, 0.0])
        out = np.array([math.cos(a), math.sin(a), 0.0]) if r > 1e-4 else np.array([1.0, 0, 0])
        az = a + rng.uniform(-0.6, 0.6)
        dirh = np.array([math.cos(az), math.sin(az), 0.0])
        lean = math.radians(rng.uniform(*spec["lean"]))
        h = rng.uniform(*spec["height"])
        # Shorter blades nearer the rim.
        h *= 0.75 + 0.25 * (1 - r / spec["radius"])
        width = rng.uniform(*spec["width"])
        droop = rng.uniform(0.15, 0.55)
        twist = rng.uniform(-0.8, 0.8)
        column = rng.choice(spec["columns"])
        u0 = (column + 0.15) / COLUMNS
        u1 = (column + 0.85) / COLUMNS
        phase = rng.random()
        start = len(verts)
        for k in range(segments + 1):
            t = k / segments
            # The spine: up, leaning out, drooping more toward the tip (a parabola in the lean direction).
            up = h * (t - 0.5 * droop * t * t) * math.cos(lean)
            side = h * (t * math.sin(lean) + droop * t * t * 0.9)
            spine = base + dirh * side + np.array([0, 0, up])
            tangent = dirh * (math.sin(lean) + 1.8 * droop * t) + np.array([0, 0, math.cos(lean) * (1 - droop * t)])
            tangent /= np.linalg.norm(tangent)
            across = np.cross(tangent, np.array([0, 0, 1.0]))
            if np.linalg.norm(across) < 1e-6:
                across = np.cross(tangent, np.array([1.0, 0, 0]))
            across /= np.linalg.norm(across)
            ang = twist * t
            across = across * math.cos(ang) + np.cross(tangent, across) * math.sin(ang)
            half = width * 0.5 * (1 - t) ** 0.85
            face_n = np.cross(across, tangent)
            n = face_n / (np.linalg.norm(face_n) + 1e-9)
            # Normals bent toward the sky: the blades shade softly together, like a lawn, not like mirrors.
            n = 0.45 * n + 0.75 * np.array([0, 0, 1.0]) + 0.2 * out
            n /= np.linalg.norm(n)
            if k < segments:
                verts += [spine - across * half, spine + across * half]
                uvs += [(u0, t), (u1, t)]
                cols += [(t, phase, 0, 1)] * 2
                norms += [n, n]
            else:
                verts.append(spine)
                uvs.append(((u0 + u1) / 2, 1.0))
                cols.append((1.0, phase, 0, 1))
                norms.append(n)
        for k in range(segments - 1):
            a0, a1, b0, b1 = start + 2 * k, start + 2 * k + 1, start + 2 * k + 2, start + 2 * k + 3
            faces += [(a0, a1, b1), (a0, b1, b0)]
        tip = start + 2 * segments
        faces.append((start + 2 * (segments - 1), start + 2 * (segments - 1) + 1, tip))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    uv = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            uv.data[li].uv = uvs[mesh.loops[li].vertex_index]
    col = mesh.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="POINT")
    for i, c in enumerate(cols):
        col.data[i].color = c
    mesh.color_attributes.active_color = col
    mesh.normals_split_custom_set_from_vertices([tuple(n) for n in norms])
    mat = bpy.data.materials.get("grass_blades") or bpy.data.materials.new("grass_blades")
    if not mat.node_tree or not any(n.type == "TEX_IMAGE" for n in mat.node_tree.nodes):
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.85
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj, len(faces)


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_vertex_color="ACTIVE",
                              export_normals=True, export_texcoords=True, export_image_format="AUTO")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out = argv[argv.index("--out") + 1]
    only = argv[argv.index("--only") + 1] if "--only" in argv else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tmp = os.path.join(out, "_grass_strip.png")
    image = strip_texture(tmp)
    image.pack()
    for vid, spec in VARIANTS.items():
        if only and vid != only:
            continue
        folder = os.path.join(out, vid)
        os.makedirs(folder, exist_ok=True)
        tris = []
        for li, (share, segments) in enumerate(LEVELS):
            name = vid if li == 0 else f"{vid}_lod{li}"
            obj, n = clump(name, spec, share, segments, image)
            export(obj, os.path.join(folder, name + ".glb"))
            tris.append(n)
            bpy.data.objects.remove(obj)
        json.dump({"asset_id": vid, "name": vid, "role": "phase_b_instanced_foliage", "category": "vegetation", "status": "export_ready",
                   "method": "procedural (tools/asset_pipeline/_procgen_grass_blades.py, archetype grass_blade_clump)",
                   "variant": spec["note"], "seed": spec["seed"], "triangles_by_level": tris, "license": "project-owned",
                   "attribution": "none"}, open(os.path.join(folder, vid + "_meta.json"), "w"), indent=2)
        print("GRASS", vid, tris)
    os.remove(tmp)


main()
