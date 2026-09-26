"""LOD chains for the procedural trees (_procgen_tree.py): <id>_lod1..3.glb beside the base, built from the tree itself.

The tree is regrown from its species preset and seed (the grower is deterministic) and the regrown LOD0 is checked
against the base GLB (triangles per material, bounds) before anything is written, so the levels are cut from exactly
the skeleton and cards the base was built from. Every level takes the base's own embedded textures (extracted from the
GLB, not re-baked) at a lower size, the same material names and conventions (bark, wood, foliage alphaMode MASK at
0.5), the same frame and origin, and stays inside the base's bounds (the game centres a model's whole bounds).

  lod1  ~50 %: twigs and the least important branches dropped first, tubes with ~60 % of their sides and every other
        ring; ~80 % of the cards kept, thinned evenly through the crown (per 1.5 m cell, so it never goes hollow), the
        kept cards grown from their outer edge inward to keep the crown's coverage and silhouette.
  lod2  ~20 %: trunk, limbs and the larger branches as few-sided tubes; ~half the cards, grown more.
  lod3  impostor: four planes crossed on the trunk axis (0/45/90/135 deg), each two single-sided quads 2 cm apart
        facing opposite ways, every quad textured from an orthographic Cycles render of the base from its own side
        (eight views): albedo darkened by the crown's own sky occlusion, tangent-space normals (so the game's sun
        still shades it; their view-facing part flattened, see impostor()), and cut-out alpha; the albedo is then
        scaled so its lit colour matches the base's (calibrate()). Single-sided like the base's cards and never
        coplanar, so a renderer that draws back faces (the game's wind shader) has no two faces fighting for a depth.

Each level is then re-imported from its file and rendered as an orthographic silhouette from eight sides against the
base (intersection over union, coverage ratio) - numbers for the report; the visual review is separate.

Usage (inside Blender):
  blender --background --factory-startup --python tools/asset_pipeline/_procgen_tree_lods.py -- --species oak
          [--seed N] [--base <base.glb>] [--out-dir DIR] [--work-dir DIR]
Defaults: --base assets/ready/<id>/<id>.glb, --out-dir assets/_staging/lods/<id>. Writes <id>_lod1..3.glb and
<id>_lod_report.json; the last stdout line is "RESULT {json}".
"""
import argparse
import copy
import hashlib
import json
import math
import os
import struct
import sys
import tempfile
import time

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _procgen_tree as pt  # noqa: E402  (bpy is loaded)

TOOL = "tools/asset_pipeline/_procgen_tree_lods.py"
LEVELS = {
    1: {"tri_ratio": 0.5, "card_keep": 0.8, "cell_m": 1.5, "sides": 0.6, "ring_step": 2, "trunk_sides": 14,
        "trunk_keep_z": (0.0, 0.10, 0.28, 0.55, 1.0), "tex": 1024},
    2: {"tri_ratio": 0.2, "card_keep": 0.5, "cell_m": 2.0, "sides": 0.4, "ring_step": 3, "trunk_sides": 9,
        "trunk_keep_z": (0.0, 0.18, 0.55), "tex": 512},
}
# Views every 45 deg: the four planes (0/45/90/135) each carry the view from either side on its own single-sided quad.
IMPOSTOR = {"azimuths_deg": (0, 45, 90, 135, 180, 225, 270, 315), "cell": 256, "render": 512, "samples": 64,
            "ao_samples": 128, "ao_floor": 0.25, "gap_m": 0.01, "flatten": 0.85, "lift": 0.15}
SIL_PX = 256
# impostor albedo calibration: views between and on the planes, the review renders' sun (elevation, azimuth deg)
CAL_VIEWS = (0, 22.5, 67.5, 112.5, 157.5, 202.5, 247.5, 292.5, 337.5)
CAL_SUN = (38.0, -35.0)
# silhouettes every 22.5 deg over a half turn (an orthographic view's silhouette from behind is its mirror)
SIL_VIEWS = (0, 22.5, 45, 67.5, 90, 112.5, 135, 157.5)


# ----------------------------------------------------------------------------------------------------------- base GLB

def read_glb(path):
    data = open(path, "rb").read()
    length = struct.unpack_from("<I", data, 8)[0]
    off, gltf, binc = 12, None, b""
    while off < length:
        n, t = struct.unpack_from("<II", data, off)
        if t == 0x4E4F534A:
            gltf = json.loads(data[off + 8:off + 8 + n])
        elif t == 0x004E4942:
            binc = data[off + 8:off + 8 + n]
        off += 8 + n
    return gltf, binc


def glb_facts(path):
    """Triangles per material, bounds (Blender frame: x, -z, y of glTF), alpha modes, image sizes, file size."""
    g, b = read_glb(path)
    tris, lo, hi = {}, [1e9] * 3, [-1e9] * 3
    for m in g["meshes"]:
        for p in m["primitives"]:
            name = g["materials"][p["material"]]["name"] if "material" in p else None
            tris[name] = tris.get(name, 0) + g["accessors"][p["indices"]]["count"] // 3
            a = g["accessors"][p["attributes"]["POSITION"]]
            for i in range(3):
                lo[i], hi[i] = min(lo[i], a["min"][i]), max(hi[i], a["max"][i])
    bl_lo = (lo[0], -hi[2], lo[1])
    bl_hi = (hi[0], -lo[2], hi[1])
    images = {}
    for im in g.get("images", []):
        v = g["bufferViews"][im["bufferView"]]
        blob = b[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]
        w, h = struct.unpack_from(">II", blob, 16) if blob[:8] == b"\x89PNG\r\n\x1a\n" else (0, 0)
        images[im.get("name")] = [w, h]
    mats = {m["name"]: {"alphaMode": m.get("alphaMode", "OPAQUE"), "doubleSided": m.get("doubleSided", False),
                        "baseColorTexture": "baseColorTexture" in m.get("pbrMetallicRoughness", {}),
                        "normalTexture": "normalTexture" in m}
            for m in g.get("materials", [])}
    return {"triangles": sum(tris.values()), "triangles_by_material": tris, "bounds_min": bl_lo,
            "bounds_max": bl_hi, "materials": mats, "images": images, "bytes": os.path.getsize(path)}


def extract_images(path, out_dir):
    """The base's embedded images, written as PNG files named as they were ('<id>_<material>_<map>.png')."""
    g, b = read_glb(path)
    out = {}
    os.makedirs(out_dir, exist_ok=True)
    for im in g["images"]:
        v = g["bufferViews"][im["bufferView"]]
        blob = b[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]
        p = os.path.join(out_dir, im["name"] + ".png")
        with open(p, "wb") as fh:
            fh.write(blob)
        out[im["name"]] = p
    return out


def scaled_copy(src, dst, size):
    img = bpy.data.images.load(src)
    if img.size[0] > size:
        img.scale(size, size)
    img.filepath_raw = dst
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    return dst


# ----------------------------------------------------------------------------------------------------------- skeleton

def importance(br):
    return (sum(br.rad) / len(br.rad)) * br.length


def simplified(br, cfg, H):
    nb = copy.copy(br)
    n = len(br.pts)
    if br.level == 0:
        keep_z = cfg["trunk_keep_z"]
        base = [i for i, p in enumerate(br.pts) if any(abs(p.z - z) < 1e-6 for z in keep_z)]
        above = [i for i, p in enumerate(br.pts) if p.z > max(keep_z) + 1e-6]
        idx = sorted(set(base + above[cfg["ring_step"] - 1::cfg["ring_step"]] + [n - 1]))
        nb.sides = cfg["trunk_sides"]
    else:
        idx = sorted(set([0, 1] + list(range(1 + cfg["ring_step"], n - 1, cfg["ring_step"])) + [n - 1]))
        nb.sides = max(3, int(round(br.sides * cfg["sides"])))
    nb.pts = [br.pts[i] for i in idx]
    nb.rad = [br.rad[i] for i in idx]
    nb.finish()
    return nb


def tube_tris(br):
    return 2 * br.sides * len(br.pts)


def reduce_bark(branches, cfg, budget, H):
    """Simplify every tube, then drop branches (and everything grown from them), least important first, until the
    bark fits the budget. The trunk and its first-order limbs go last."""
    simp = {id(br): simplified(br, cfg, H) for br in branches}
    total = sum(tube_tris(s) for s in simp.values())
    children = {}
    for br in branches:
        if br.parent is not None:
            children.setdefault(id(br.parent), []).append(br)

    def subtree(br):
        out, stack = [], [br]
        while stack:
            x = stack.pop()
            out.append(x)
            stack.extend(children.get(id(x), []))
        return out
    dropped = set()
    order = sorted((br for br in branches if br.level >= 1),
                   key=lambda br: (br.level <= 1 and br.kind != "stub", importance(br)))
    for br in order:
        if total <= budget:
            break
        if id(br) in dropped:
            continue
        for x in subtree(br):
            if id(x) not in dropped:
                dropped.add(id(x))
                total -= tube_tris(simp[id(x)])
    kept = [simp[id(br)] for br in branches if id(br) not in dropped]
    return kept, total, len(dropped)


# ----------------------------------------------------------------------------------------------------------- cards

def card_corners(c, size=None, anchor=None):
    A = c["A"] if anchor is None else anchor
    D, h = c["D"], c["size"] if size is None else size
    n0 = c["n"]
    n0 = (n0 - D * n0.dot(D)).normalized()
    S = D.cross(n0)
    drop = 0.07
    return [A - S * (h / 2) - D * (h * drop), A + S * (h / 2) - D * (h * drop),
            A + S * (h / 2) + D * (h * (1 - drop)), A - S * (h / 2) + D * (h * (1 - drop))]


def inside(p, lo, hi, tol=1e-4):
    return all(lo[i] - tol <= p[i] <= hi[i] + tol for i in range(3))


def thin_cards(cards, keep, cell_m, crown_c, crown_ax):
    """Keep `keep` of the cards, spread evenly: per cell of the crown the same share is kept, the largest and the
    outermost first (branch tips and the leader always stay)."""
    cells = {}
    for c in cards:
        ctr = c["A"] + c["D"] * (c["size"] * 0.43)
        key = tuple(int(math.floor(ctr[i] / cell_m)) for i in range(3))
        r = math.sqrt(sum(((ctr[i] - crown_c[i]) / crown_ax[i]) ** 2 for i in range(3)))
        score = c["size"] * (0.4 + min(1.0, r)) * (1.5 if c["prio"][0] == 0 else 1.0)
        cells.setdefault(key, []).append((score, c))
    total = sum(len(v) for v in cells.values())
    target = int(round(keep * total))
    quota = {k: keep * len(v) for k, v in cells.items()}
    alloc = {k: int(math.floor(q)) for k, q in quota.items()}
    rest = target - sum(alloc.values())
    for k in sorted(quota, key=lambda k: -(quota[k] - alloc[k]))[:max(0, rest)]:
        alloc[k] += 1
    out = []
    for k, v in cells.items():
        v.sort(key=lambda t: -t[0])
        out += [c for _, c in v[:alloc[k]]]
        out += [c for _, c in v[alloc[k]:] if c.get("br") == 0]  # leader and top cards
    return out


def grow_cards(cards, scale, lo, hi, crossed, roll):
    """Each card `scale` times larger, its outer edge held where it was (it grows back toward its branch); a card
    that would then leave the base's bounds keeps its own size."""
    out = []
    for c in cards:
        h = c["size"]
        h2 = h * scale
        A2 = c["A"] + c["D"] * ((h - h2) * 0.93)
        c2 = dict(c)
        planes = [dict(c, n=n) for n in pt.card_normals(c, crossed, roll)]
        if all(inside(p, lo, hi) for q in planes for p in card_corners(q, h2, A2)):
            c2["A"], c2["size"] = A2, h2
        out.append(c2)
    return out


# ----------------------------------------------------------------------------------------------------------- levels

def build_level(sp, branches, cards, cfg, lod0_tris, lod0_card_tris, lo, hi, crown_c, crown_ax, res):
    fo = sp["foliage"]
    b = pt.Builder()
    kept_cards = []
    card_tris = 0
    if fo and cards:
        kept_cards = thin_cards(cards, cfg["card_keep"], cfg["cell_m"], crown_c, crown_ax)
        frac = len(kept_cards) / max(1, len(cards))
        kept_cards = grow_cards(kept_cards, min(1.45, (1.0 / max(frac, 0.2)) ** 0.5), lo, hi, fo["crossed"],
                                fo.get("roll", 0.5))
        card_tris = len(kept_cards) * 4 * (2 if fo["crossed"] else 1)
    budget = max(0.0, cfg["tri_ratio"] * lod0_tris - card_tris)
    tubes, bark_tris, n_dropped = reduce_bark(branches, cfg, budget, sp["height"])
    for br in tubes:
        pt.tube(b, br, 0.5)
    if kept_cards:
        cells = pt.atlas_cells(res)
        for c in kept_cards:
            for n in pt.card_normals(c, fo["crossed"], fo.get("roll", 0.5)):
                cc = dict(c)
                cc["n"] = n
                pt.card_geo(b, cc, cells, None)
    return b, {"tubes": len(tubes), "tubes_dropped": n_dropped, "cards": len(kept_cards), "card_tris": card_tris,
               "bark_tris": sum(len(f) - 2 for f, m in zip(b.faces, b.fmat) if m != pt.LEAF)}


def level_materials(asset, images, size, work, has_foliage):
    mats = []
    os.makedirs(work, exist_ok=True)
    for key in ("bark", "wood") + (("foliage",) if has_foliage else ()):
        paths = {}
        for mp in ("basecolor", "orm", "normal"):
            name = f"{asset}_{key}_{mp}"
            paths[mp] = scaled_copy(images[name], os.path.join(work, name + ".png"), size)
        mats.append(pt.pbr_material(f"MAT_{asset}_{key}", paths, cutout=0.5 if key == "foliage" else None))
    return mats


def clear_level(obj, mats):
    """Drop a written level's object, materials and images, so the next level's carry the same names (not .001)."""
    imgs = {n.image for m in mats for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}
    bpy.data.objects.remove(obj, do_unlink=True)
    for m in mats:
        bpy.data.materials.remove(m)
    for im in imgs:
        bpy.data.images.remove(im)
    for me in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(me)


def export_object(obj, path, tangents=False):
    pt.select_only(obj)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_materials="EXPORT", export_yup=True,
                              export_apply=True, export_normals=True, export_texcoords=True,
                              export_tangents=tangents, export_image_format="AUTO", use_selection=True)


def finish_level_object(b, name, mats, crown_c, crown_ax, bend, clump):
    targets = pt.clump_targets(b, clump, crown_c, crown_ax) if clump and b.card_verts else None
    obj = pt.make_object(b, name)
    pt.finish_topology(obj, crown_c, crown_ax, bend, targets)
    me = obj.data
    idx = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", idx)
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", idx)
    me.attributes.remove(me.attributes["fol"])
    me.update()
    return obj


# ----------------------------------------------------------------------------------------------------------- impostor

def emit_material(name, imgs, mode, cutout):
    """An override for the impostor renders: emission of the albedo, of the world-space shading normal, or a white
    diffuse (lit by a uniform white sky: its own sky occlusion); cut out like the base where it is foliage."""
    m = bpy.data.materials.new(name)
    t = m.node_tree
    for n in list(t.nodes):
        t.nodes.remove(n)
    out = t.nodes.new("ShaderNodeOutputMaterial")
    col = t.nodes.new("ShaderNodeTexImage")
    col.image = imgs["basecolor"]
    nrm = t.nodes.new("ShaderNodeTexImage")
    nrm.image = imgs["normal"]
    nmap = t.nodes.new("ShaderNodeNormalMap")
    t.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    if mode == "albedo":
        sh = t.nodes.new("ShaderNodeEmission")
        t.links.new(col.outputs["Color"], sh.inputs["Color"])
    elif mode == "normal":
        sh = t.nodes.new("ShaderNodeEmission")
        ma = t.nodes.new("ShaderNodeVectorMath")
        ma.operation = "MULTIPLY_ADD"
        ma.inputs[1].default_value = (0.5, 0.5, 0.5)
        ma.inputs[2].default_value = (0.5, 0.5, 0.5)
        t.links.new(nmap.outputs["Normal"], ma.inputs[0])
        t.links.new(ma.outputs["Vector"], sh.inputs["Color"])
    else:
        sh = t.nodes.new("ShaderNodeBsdfDiffuse")
        sh.inputs["Color"].default_value = (1, 1, 1, 1)
        t.links.new(nmap.outputs["Normal"], sh.inputs["Normal"])
    final = sh.outputs[0]
    if cutout:
        lt = t.nodes.new("ShaderNodeMath")
        lt.operation = "GREATER_THAN"
        lt.inputs[1].default_value = 0.5
        t.links.new(col.outputs["Alpha"], lt.inputs[0])
        tr = t.nodes.new("ShaderNodeBsdfTransparent")
        mix = t.nodes.new("ShaderNodeMixShader")
        t.links.new(lt.outputs[0], mix.inputs["Fac"])
        t.links.new(tr.outputs[0], mix.inputs[1])
        t.links.new(final, mix.inputs[2])
        final = mix.outputs[0]
    # Back faces see through, as the game culls them: a card is two quads back to back, and Cycles would otherwise
    # hit the rear one about half the time, whose shading normal (flipped toward the ray) points into the crown.
    geo = t.nodes.new("ShaderNodeNewGeometry")
    see = t.nodes.new("ShaderNodeBsdfTransparent")
    cull = t.nodes.new("ShaderNodeMixShader")
    t.links.new(geo.outputs["Backfacing"], cull.inputs["Fac"])
    t.links.new(final, cull.inputs[1])
    t.links.new(see.outputs[0], cull.inputs[2])
    t.links.new(cull.outputs[0], out.inputs["Surface"])
    return m


def render_exr(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)


def ortho_camera(scene, az_deg, scale, height_c, dist=60.0):
    az = math.radians(az_deg)
    d = Vector((math.sin(az), -math.cos(az), 0.0))
    cam = scene.camera
    cam.location = d * dist + Vector((0, 0, height_c))
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = scale
    cam.data.clip_start = 1.0
    cam.data.clip_end = dist * 2 + 40
    fwd = -d
    right = fwd.cross(Vector((0, 0, 1)))
    return right.normalized(), d


def setup_cycles(scene, px, samples):
    pt.set_device(scene, "auto")
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.transparent_max_bounces = 128
    scene.cycles.max_bounces = 2
    scene.cycles.diffuse_bounces = 1
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = px
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "OPEN_EXR"
    scene.render.image_settings.color_depth = "32"
    scene.render.image_settings.color_mode = "RGBA"
    try:
        scene.view_settings.view_transform = "Standard"
    except TypeError:
        pass
    if scene.camera is None:
        cd = bpy.data.cameras.new("_imp_cam")
        co = bpy.data.objects.new("_imp_cam", cd)
        scene.collection.objects.link(co)
        scene.camera = co


def impostor(asset, base_objs, images_full, lo, hi, work, species_has_foliage):
    """Four crossed planes of two back-to-back quads, each textured from an orthographic render of the base."""
    scene = bpy.context.scene
    setup_cycles(scene, IMPOSTOR["render"], IMPOSTOR["samples"])
    world = bpy.data.worlds.new("_imp_world")
    wt = world.node_tree
    for n in list(wt.nodes):
        wt.nodes.remove(n)
    bg = wt.nodes.new("ShaderNodeBackground")
    bg.inputs["Color"].default_value = (1, 1, 1, 1)
    bg.inputs["Strength"].default_value = 1.0
    wt.links.new(bg.outputs[0], wt.nodes.new("ShaderNodeOutputWorld").inputs[0])
    scene.world = world
    imgs = {}
    for key in ("bark", "wood", "foliage"):
        if f"{asset}_{key}_basecolor" not in images_full:
            continue
        d = {}
        for mp in ("basecolor", "normal"):
            im = bpy.data.images.load(images_full[f"{asset}_{key}_{mp}"])
            im.colorspace_settings.name = "sRGB" if mp == "basecolor" else "Non-Color"
            if mp == "basecolor":
                im.alpha_mode = "CHANNEL_PACKED"
            d[mp] = im
        imgs[key] = d
    overrides = {mode: {key: emit_material(f"_imp_{mode}_{key}", imgs[key], mode, key == "foliage")
                        for key in imgs} for mode in ("albedo", "normal", "ao")}
    slots = []
    for o in base_objs:
        for i, ms in enumerate(o.material_slots):
            key = next((k for k in imgs if ms.material and ms.material.name.endswith("_" + k)), None)
            if key:
                slots.append((o, i, key, ms.material))
    # one square frame for every view: the tree's height, or its widest extent through the trunk axis
    verts = []
    for o in base_objs:
        mw = o.matrix_world
        verts += [mw @ v.co for v in o.data.vertices]
    V = np.array([tuple(v) for v in verts])
    reach = 0.0
    for az in IMPOSTOR["azimuths_deg"]:
        a = math.radians(az)
        u = V[:, 0] * math.cos(a) + V[:, 1] * math.sin(a)
        reach = max(reach, float(np.abs(u).max()))
    top = float(V[:, 2].max())
    S = max(top, 2 * reach) * 1.02
    R, C = IMPOSTOR["render"], IMPOSTOR["cell"]
    views = []
    for az in IMPOSTOR["azimuths_deg"]:
        right, d = ortho_camera(scene, az, S, S / 2)
        layers = {}
        for mode in ("albedo", "normal", "ao"):
            for o, i, key, _ in slots:
                o.material_slots[i].material = overrides[mode][key]
            scene.cycles.samples = IMPOSTOR["ao_samples"] if mode == "ao" else IMPOSTOR["samples"]
            scene.cycles.use_denoising = mode == "ao"
            layers[mode] = render_exr(scene, os.path.join(work, f"imp_{int(round(az * 10)):04d}_{mode}.exr"))
        views.append((az, right, layers))
    for o, i, key, mat in slots:
        o.material_slots[i].material = mat
    # atlas: 4 x 2 cells of C px (row 0 = bottom, as Blender stores pixels)
    NC = 4
    NR = -(-len(views) // NC)
    col = np.zeros((NR * C, NC * C, 3), np.float32)
    alpha = np.zeros((NR * C, NC * C), np.float32)
    nrm = np.zeros((NR * C, NC * C, 3), np.float32)
    A = [NC * C, NR * C]
    quads = []
    k_down = R // C
    for k, (az, right, L) in enumerate(views):
        def down(x):
            return x.reshape(C, k_down, C, k_down, -1).mean((1, 3))
        a_hi = L["albedo"][..., 3]
        a = down(a_hi[..., None])[..., 0]

        def straight(layer):
            # Cycles writes associated (premultiplied) colour: back to straight colour, weighted by the albedo's cover
            return layer[..., :3] / np.maximum(layer[..., 3:4], 1e-6) * a_hi[..., None]
        alb = down(straight(L["albedo"])) / np.maximum(a[..., None], 1e-6)
        n_straight = straight(L["normal"]) / np.maximum(a_hi[..., None], 1e-6) * 2.0 - 1.0
        nw = down(n_straight * a_hi[..., None]) / np.maximum(a[..., None], 1e-6)
        # world normal -> the quad's tangent frame: x along the quad's U (the view's right), y up, z toward the view.
        # Most of each normal's part toward its own view is taken out (and replaced by a lift toward the sky): a quad
        # also shows from up to 67 deg off its view, and a normal that faces that view makes the whole quad light as
        # a wall facing it - one plane dark, the next a bright strip. What stays is the crown's left/right/up/down
        # shading, the same whichever quad shows it.
        up = Vector((0, 0, 1))
        toward = right.cross(up)
        tw = np.array(tuple(toward))
        nw = nw - IMPOSTOR["flatten"] * (nw @ tw)[..., None] * tw + IMPOSTOR["lift"] * np.array((0.0, 0.0, 1.0))
        nw /= np.maximum(np.linalg.norm(nw, axis=2, keepdims=True), 1e-6)
        nn = np.stack([nw @ np.array(tuple(right)), nw @ np.array(tuple(up)), nw @ tw], axis=-1)
        ao = down(straight(L["ao"])[..., :1])[..., 0] / np.maximum(a, 1e-6)
        ao = np.clip(ao, 0.0, 1.0)
        shade = IMPOSTOR["ao_floor"] + (1.0 - IMPOSTOR["ao_floor"]) * ao
        alb = alb * shade[..., None]
        nn /= np.maximum(np.linalg.norm(nn, axis=2, keepdims=True), 1e-6)
        wgt = (a >= 0.5).astype(np.float32) * a
        alb = pt.pushpull(alb, wgt)
        nn = pt.pushpull(nn, wgt)
        nn /= np.maximum(np.linalg.norm(nn, axis=2, keepdims=True), 1e-6)
        i, j = k % NC, k // NC
        ys, xs = slice(j * C, (j + 1) * C), slice(i * C, (i + 1) * C)
        col[ys, xs] = alb
        alpha[ys, xs] = a
        nrm[ys, xs] = nn * 0.5 + 0.5
        # the quad: the view's opaque extent (plus a texel), clamped into the base's bounds
        on = a >= 0.5
        cols = np.where(on.any(axis=0))[0]
        rows = np.where(on.any(axis=1))[0]
        px_m = S / C
        u0 = (cols.min() - 1) * px_m - S / 2
        u1 = (cols.max() + 2) * px_m - S / 2
        v1 = min(S, (rows.max() + 2) * px_m)
        gap = IMPOSTOR["gap_m"]
        for ax in (0, 1):
            r = right[ax]
            if abs(r) > 1e-6:
                lim = sorted(((lo[ax] + 2 * gap) / r, (hi[ax] - 2 * gap) / r))
                u0, u1 = max(u0, lim[0]), min(u1, lim[1])
        v1 = min(v1, hi[2])
        quads.append((az, right, u0, u1, v1, (i / NC, j / NR)))
    stem = os.path.join(work, f"{asset}_impostor")
    paths = {"basecolor": stem + "_basecolor.png", "orm": stem + "_orm.png", "normal": stem + "_normal.png"}
    pt.save_png(pt.to_srgb(col), paths["basecolor"], "sRGB", alpha=alpha)
    pt.save_png(nrm, paths["normal"], "Non-Color")
    orm = np.zeros((32, 32, 3), np.float32)
    orm[..., 0], orm[..., 1] = 1.0, 0.9
    pt.save_png(orm, paths["orm"], "Non-Color")
    mat = pt.pbr_material(f"MAT_{asset}_impostor", paths, cutout=0.5)
    b = pt.Builder()
    b.part("impostor", "card")
    for az, right, u0, u1, v1, (cu, cv) in quads:
        off = right.cross(Vector((0, 0, 1))) * IMPOSTOR["gap_m"]  # toward this quad's own view
        pts = [right * u0 + off, right * u1 + off, right * u1 + Vector((0, 0, v1)) + off,
               right * u0 + Vector((0, 0, v1)) + off]
        uv = [(cu + ((u + S / 2) / S) / NC, cv + (v / S) / NR) for u, v in ((u0, 0), (u1, 0), (u1, v1), (u0, v1))]
        idx = [b.v(p) for p in pts]
        b.f(idx, uv, 0)
    obj = pt.make_object(b, f"{asset}_lod3")
    me = obj.data
    me.attributes.remove(me.attributes["fol"])
    bm_tris = sum(len(f) - 2 for f in b.faces)
    me.materials.append(mat)
    select_quads = [q[:5] for q in quads]
    return obj, {"planes": len(quads), "atlas_px": A, "cell_px": C, "frame_m": round(S, 3), "triangles": bm_tris,
                 "quads": [{"azimuth_deg": az, "u_m": [round(u0, 3), round(u1, 3)], "top_m": round(v1, 3)}
                           for az, _, u0, u1, v1 in select_quads],
                 "alpha_coverage": round(float((alpha >= 0.5).mean()), 3)}, (col, alpha, paths["basecolor"], mat)


def lit_mean(scene, show, S, work):
    """Mean lit colour (straight, linear) of the shown objects over CAL_VIEWS."""
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in show
    means = []
    for az in CAL_VIEWS:
        ortho_camera(scene, az, S, S / 2)
        px = render_exr(scene, os.path.join(work, "_cal.exr"))
        a = px[..., 3]
        m = a > 0.5
        means.append((px[..., :3][m] / a[m][:, None]).mean(0))
    return np.mean(means, axis=0)


def calibrate(scene, base_objs, imp_obj, S, work):
    """The impostor's albedo is baked unlit and cannot shadow itself as the crown does, so its lit colour drifts from
    the base's. Both are lit alike (Eevee, the review renders' sky and sun without shadows, as past the game's shadow
    range; back faces culled as in the game) and the albedo is scaled per channel so the impostor's mean lit colour
    matches the base's."""
    scene.render.engine = "BLENDER_EEVEE"
    if hasattr(scene.eevee, "taa_render_samples"):
        scene.eevee.taa_render_samples = 16
    scene.render.resolution_x = scene.render.resolution_y = 512
    world = bpy.data.worlds.new("_cal_sky")
    wt = world.node_tree
    for n in list(wt.nodes):
        wt.nodes.remove(n)
    sky = wt.nodes.new("ShaderNodeTexSky")
    for t in ("MULTIPLE_SCATTERING", "SINGLE_SCATTERING", "NISHITA", "HOSEK_WILKIE"):
        try:
            sky.sky_type = t
            break
        except TypeError:
            continue
    el, az = math.radians(CAL_SUN[0]), math.radians(CAL_SUN[1])
    if hasattr(sky, "sun_elevation"):
        sky.sun_elevation, sky.sun_rotation = el, az
    if hasattr(sky, "sun_disc"):
        sky.sun_disc = False
    bg = wt.nodes.new("ShaderNodeBackground")
    bg.inputs["Strength"].default_value = 0.2
    wt.links.new(sky.outputs[0], bg.inputs[0])
    wt.links.new(bg.outputs[0], wt.nodes.new("ShaderNodeOutputWorld").inputs[0])
    scene.world = world
    ld = bpy.data.lights.new("_cal_sun", "SUN")
    ld.energy, ld.angle, ld.color = 5.5, math.radians(1.5), (1.0, 0.96, 0.9)
    ld.use_shadow = False  # the impostor's distances are past the game's sun-shadow range (60-120 m by tier)
    lo_ = bpy.data.objects.new("_cal_sun", ld)
    scene.collection.objects.link(lo_)
    d = Vector((math.cos(el) * math.sin(az), -math.cos(el) * math.cos(az), math.sin(el)))
    lo_.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    m0 = lit_mean(scene, base_objs, S, work)
    m3 = lit_mean(scene, [imp_obj], S, work)
    return m0, m3, lo_


# ----------------------------------------------------------------------------------------------------------- checks

def silhouettes(path, scene, S, work, px=SIL_PX):
    """Alpha of the file, re-imported, from SIL_VIEWS (orthographic; Cycles, its own materials)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    others = [o for o in bpy.data.objects if o not in new and o.type == "MESH" and not o.hide_render]
    for o in others:
        o.hide_render = True
    scene.render.resolution_x = scene.render.resolution_y = px
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.world = None
    out = []
    for az in SIL_VIEWS:
        ortho_camera(scene, az, S, S / 2)
        out.append(render_exr(scene, os.path.join(work, "_sil.exr"))[..., 3] >= 0.5)
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    for o in others:
        o.hide_render = False
    return out


def compare(sil0, sil):
    ious, cov = [], []
    for a, b in zip(sil0, sil):
        inter, union = np.logical_and(a, b).sum(), np.logical_or(a, b).sum()
        ious.append(inter / max(1, union))
        cov.append(b.sum() / max(1, a.sum()))
    return {"iou_min": round(float(min(ious)), 3), "iou_mean": round(float(np.mean(ious)), 3),
            "coverage_ratio_min": round(float(min(cov)), 3), "coverage_ratio_max": round(float(max(cov)), 3)}


# ----------------------------------------------------------------------------------------------------------- main

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--species", required=True, choices=sorted(pt.SPECIES))
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--base", default=None)
    ap.add_argument("--out-dir", default=None)
    ap.add_argument("--work-dir", default=None)
    ap.add_argument("--flatten", type=float, default=IMPOSTOR["flatten"])
    ap.add_argument("--lift", type=float, default=IMPOSTOR["lift"])
    ap.add_argument("--planes", type=int, default=len(IMPOSTOR["azimuths_deg"]) // 2)
    ap.add_argument("--ao-floor", type=float, default=IMPOSTOR["ao_floor"])
    args = ap.parse_args(argv)
    IMPOSTOR["flatten"], IMPOSTOR["lift"], IMPOSTOR["ao_floor"] = args.flatten, args.lift, args.ao_floor
    IMPOSTOR["azimuths_deg"] = tuple(180.0 * k / args.planes for k in range(2 * args.planes))
    sp = pt.SPECIES[args.species]
    asset = sp["asset_id"]
    seed = sp["seed"] if args.seed is None else args.seed
    base = os.path.abspath(args.base or os.path.join(pt.REPO, "assets", "ready", asset, asset + ".glb"))
    out_dir = os.path.abspath(args.out_dir or os.path.join(pt.REPO, "assets", "_staging", "lods", asset))
    work = os.path.abspath(args.work_dir or os.path.join(tempfile.gettempdir(), asset + "_lods"))
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(work, exist_ok=True)
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)

    # regrow, and prove it is the base
    res = 2048
    (branches, cards_all, trunk), (b0, bark0, n_cards, n_planned), _ = pt.grow_standing(
        sp, args.species, seed, res, pt.TRI_BUDGET - 600)
    kept0 = b0.kept_cards
    facts0 = glb_facts(base)
    lod0_tris = sum(len(f) - 2 for f in b0.faces)
    xs, ys, zs = ([v[i] for v in b0.verts] for i in range(3))
    lo = (min(xs), min(ys), min(zs))
    hi = (max(xs), max(ys), max(zs))
    by_mat = {}
    names = [f"MAT_{asset}_bark", f"MAT_{asset}_wood", f"MAT_{asset}_foliage"]
    for f, m in zip(b0.faces, b0.fmat):
        by_mat[names[m]] = by_mat.get(names[m], 0) + len(f) - 2
    same = (lod0_tris == facts0["triangles"] and by_mat == facts0["triangles_by_material"]
            and all(abs(a - c) < 2e-3 for a, c in zip(lo + hi, facts0["bounds_min"] + facts0["bounds_max"])))
    print("REGROWN", json.dumps({"tris": lod0_tris, "base_tris": facts0["triangles"], "by_mat": by_mat,
                                 "base_by_mat": facts0["triangles_by_material"], "same": same}), flush=True)
    if not same:
        raise SystemExit(f"the regrown tree does not match {base}: not cutting LODs from a different tree")
    bounds_centre = ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2)
    crown_c, crown_ax = pt.crown_frame(sp, bounds_centre)
    fo = sp["foliage"]
    bend = fo["bend"] if fo else 0.0
    clump = fo.get("clump") if fo else None
    lod0_card_tris = facts0["triangles_by_material"].get(f"MAT_{asset}_foliage", 0)

    images = extract_images(base, os.path.join(work, "base_images"))
    report = {"asset_id": asset, "species": args.species, "seed": seed, "tool": TOOL,
              "base": os.path.relpath(base, pt.REPO).replace("\\", "/") if base.startswith(pt.REPO) else base,
              "lod0_sha1": hashlib.sha1(open(base, "rb").read()).hexdigest(),
              "lod0": {"triangles": lod0_tris, "cards": len(kept0), "tubes": len(branches)},
              "levels": LEVELS, "impostor_settings": IMPOSTOR, "lods": {}}
    files = {}
    for n in (1, 2):
        cfg = LEVELS[n]
        b, info = build_level(sp, branches, kept0, cfg, lod0_tris, lod0_card_tris, lo, hi, crown_c, crown_ax, res)
        mats = level_materials(asset, images, cfg["tex"], os.path.join(work, f"lod{n}"), bool(fo))
        obj = finish_level_object(b, f"{asset}_lod{n}", mats, crown_c, crown_ax, bend, clump)
        path = os.path.join(out_dir, f"{asset}_lod{n}.glb")
        export_object(obj, path)
        files[n] = path
        report["lods"][f"{asset}_lod{n}"] = dict(info)
        print(f"LOD{n}", json.dumps(info), flush=True)
        clear_level(obj, mats)

    # the impostor renders the base itself. The grower's minimal "glTF Material Output" group (occlusion only) would
    # be reused by the importer, which expects its own full group: drop it first (the levels using it are written).
    ng = bpy.data.node_groups.get("glTF Material Output")
    if ng is not None:
        bpy.data.node_groups.remove(ng)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=base)
    base_objs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    for o in bpy.data.objects:
        if o.type == "MESH" and o not in base_objs:
            o.hide_render = True
    obj3, info3, (col3, alpha3, colour_path, mat3) = impostor(asset, base_objs, images, lo, hi, work, bool(fo))
    m0, m3, sun = calibrate(bpy.context.scene, base_objs, obj3, info3["frame_m"], work)
    gain, m3b = np.ones(3), m3
    for _ in range(3):  # the lit colour is not linear in the albedo (the sky's specular is not scaled): a few steps
        gain = np.clip(gain * m0 / np.maximum(m3b, 1e-6), 0.4, 1.8)
        pt.save_png(pt.to_srgb(col3 * gain), colour_path, "sRGB", alpha=alpha3)
        for n in mat3.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image and \
                    os.path.normcase(bpy.path.abspath(n.image.filepath)) == os.path.normcase(colour_path):
                n.image.reload()
        m3b = lit_mean(bpy.context.scene, [obj3], info3["frame_m"], work)
    bpy.data.objects.remove(sun, do_unlink=True)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = False
    info3["albedo_calibration"] = {"base_lit_mean": [round(float(v), 4) for v in m0],
                                   "impostor_lit_mean_before": [round(float(v), 4) for v in m3],
                                   "gain": [round(float(v), 3) for v in gain],
                                   "impostor_lit_mean_after": [round(float(v), 4) for v in m3b]}
    pt.set_device(bpy.context.scene, "auto")
    path3 = os.path.join(out_dir, f"{asset}_lod3.glb")
    export_object(obj3, path3, tangents=True)
    files[3] = path3
    report["lods"][f"{asset}_lod3"] = info3
    print("LOD3", json.dumps(info3), flush=True)

    # measure the files as written; silhouettes against the base
    for o in list(bpy.data.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    scene = bpy.context.scene
    S = info3["frame_m"]
    sil0 = silhouettes(base, scene, S, work)
    ok = True
    for n, path in files.items():
        f = glb_facts(path)
        key = f"{asset}_lod{n}"
        e = report["lods"][key]
        e["faces"] = f["triangles"]
        e["triangles_by_material"] = f["triangles_by_material"]
        e["ratio"] = round(f["triangles"] / lod0_tris, 4)
        e["bytes"] = f["bytes"]
        e["images"] = f["images"]
        e["materials"] = f["materials"]
        e["bounds_inside_base"] = all(f["bounds_min"][i] >= lo[i] - 2e-3 and f["bounds_max"][i] <= hi[i] + 2e-3
                                      for i in range(3))
        e["bounds_min"] = [round(v, 3) for v in f["bounds_min"]]
        e["bounds_max"] = [round(v, 3) for v in f["bounds_max"]]
        e["silhouette_vs_lod0"] = compare(sil0, silhouettes(path, scene, S, work))
        textured = all(m["baseColorTexture"] for m in f["materials"].values())
        foliage_mask = all(m["alphaMode"] == "MASK" for k, m in f["materials"].items()
                           if k.endswith("_foliage") or k.endswith("_impostor"))
        e["checks"] = {"textured": textured, "foliage_mask": foliage_mask, "bounds": e["bounds_inside_base"],
                       "below_level_above": f["triangles"] < (lod0_tris if n == 1 else
                                                              report["lods"][f"{asset}_lod{n - 1}"]["faces"])}
        ok = ok and all(e["checks"].values())
        print(f"FILE{n}", json.dumps({k: e[k] for k in ("faces", "ratio", "bytes", "silhouette_vs_lod0", "checks")}),
              flush=True)
    report["passed"] = ok
    report["bounds_base_min"] = [round(v, 3) for v in lo]
    report["bounds_base_max"] = [round(v, 3) for v in hi]
    report["build_seconds"] = round(time.time() - t0, 1)
    with open(os.path.join(out_dir, f"{asset}_lod_report.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2, default=lambda o: list(o) if isinstance(o, tuple) else str(o))
    print("RESULT", json.dumps({"asset": asset, "passed": ok, "lod0": lod0_tris,
                                "lods": {k: {"faces": v["faces"], "ratio": v["ratio"],
                                             "iou_min": v["silhouette_vs_lod0"]["iou_min"]}
                                         for k, v in report["lods"].items()}}), flush=True)


if __name__ == "__main__":
    main()
