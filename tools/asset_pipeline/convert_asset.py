"""Turn one third-party source asset into an Otherreach ready/<id>/ folder: <id>.glb, <id>_lod1.glb, <id>_lod2.glb
[, _lod3], optional collision proxies, and <id>_meta.json. See RUNBOOK.txt beside this file.

Runs inside Blender 5.2:

  blender --background --factory-startup --python convert_asset.py -- ^
      --source <zip|fbx|gltf|glb|blend> --asset-id <id> --res 2048 --category prop [options]

Sources it understands:
  Cosmos_Premium\\Assets\\*.zip   Meshes\\*.fbx (Unreal, centimetres) + Textures\\T_<set>_<px>_<suffix>.png. Uses
                                  _B (base colour; _C is curvature, _G gloss, _S specular - measured, not guessed), _OpenGL_N (normal;
                                  _N is the DirectX twin and gets its green flipped when it is the only one),
                                  _ORM (occlusion/roughness/metallic). --mesh picks one FBX when the zip holds several.
  Poly Haven *.gltf              textures resolved by basename when the .gltf points at a textures/ folder that is
                                  not there; the _arm map is already ORM, so its R becomes occlusion.
  BlenderKit *.blend             instances and modifiers are realised; any material input that is not a plain image
                                  (curves, mapping, procedurals) is baked with Cycles onto the asset's UVs.

What it writes matches the shipped library (ArtLibrary.cs, finalize.py, the Cairnwold metas):
  * metres, glTF +Y up, one node named <id>, mesh <id>_mesh, origin at the footprint centre with the base on Y = 0
  * per material: base colour (sRGB, alpha only when the asset is cut out), normal (OpenGL), and ONE ORM image bound
    to both occlusionTexture and metallicRoughnessTexture (ArtLibrary reads AO=R, roughness=G, metallic=B)
  * every LOD level keeps its materials and textures (ArtLibrary stops the LOD chain at an untextured level), with the
    texture side halved per level
  * the meta's triangle counts, bounds and texture sizes are read back out of the exported GLBs, not from intent
"""
import argparse
import datetime
import glob
import hashlib
import json
import math
import os
import re
import shutil
import struct
import sys
import zipfile

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

TOOL = "tools/asset_pipeline/convert_asset.py"
FOLIAGE = ("foliage", "tree")
HULL_FACES = 64
MIN_LOD_PX = 256

LICENCES = {
    "polyhaven": ("CC0 1.0 Universal (Public Domain Dedication) - Poly Haven", "https://polyhaven.com/license"),
    "cosmos": ("UNVERIFIED - Cosmos Premium account download; the zip ships no licence text. Fill licence and "
               "licence_evidence from the Cosmos terms before promotion.", ""),
    "blenderkit": ("UNVERIFIED - BlenderKit asset (free assets are Royalty Free or CC0 per asset page). Fill from the "
                   "asset page before promotion.", "https://www.blenderkit.com/docs/licenses/"),
    "other": ("UNVERIFIED - fill before promotion", ""),
}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(prog="convert_asset.py")
    p.add_argument("--source", required=True, help="zip, fbx, gltf, glb or blend")
    p.add_argument("--asset-id", required=True)
    p.add_argument("--res", type=int, default=2048, help="LOD0 texture side: 4096 hero, 2048 standard, 1024 small")
    p.add_argument("--category", default="prop",
                   help="prop, rock, environment, structure, weapon, foliage or tree (foliage/tree are double-sided)")
    p.add_argument("--out", default=r"C:\Otherreach_Recovery\asset_work\ready", help="writes <out>/<asset-id>/")
    p.add_argument("--work", default=r"C:\Otherreach_Recovery\asset_work\_work", help="extraction and texture scratch")
    p.add_argument("--mesh", default="", help="substring picking one FBX in a zip, or objects in a glTF/blend")
    p.add_argument("--lods", default="0.5,0.2", help="decimate ratios of LOD0 for lod1[,lod2[,lod3]]")
    p.add_argument("--max-tris", type=int, default=0, help="decimate LOD0 down to this many triangles (0 = keep)")
    p.add_argument("--target-height", type=float, default=0.0, help="uniform scale so the height is this (m)")
    p.add_argument("--scale", type=float, default=1.0, help="extra uniform scale after unit conversion")
    p.add_argument("--yaw", type=float, default=0.0, help="degrees about up, applied before origining")
    p.add_argument("--collision", choices=("none", "box", "hull"), default="none")
    p.add_argument("--image-format", choices=("JPEG", "PNG"), default="JPEG",
                   help="embedded map encoding; a cut-out base colour is always PNG")
    p.add_argument("--jpeg-quality", type=int, default=92)
    p.add_argument("--licence", default="")
    p.add_argument("--licence-evidence", default="")
    p.add_argument("--source-url", default="")
    p.add_argument("--region", default="")
    p.add_argument("--note", default="")
    p.add_argument("--preview", action="store_true", help="render a 4-view textured sheet of LOD0 to <id>_preview.png")
    p.add_argument("--save-blend", action="store_true", help="keep the normalised scene as <work>/<id>/<id>.blend")
    a = p.parse_args(argv)
    a.source, a.out, a.work = os.path.abspath(a.source), os.path.abspath(a.out), os.path.abspath(a.work)
    if not re.fullmatch(r"[a-z0-9_]+", a.asset_id):
        raise SystemExit(f"asset id '{a.asset_id}' must be lower-case letters, digits and underscores")
    return a


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for block in iter(lambda: fh.read(1 << 22), b""):
            h.update(block)
    return h.hexdigest()


def family(path):
    p = path.lower()
    if "polyhaven" in p:
        return "polyhaven"
    if "cosmos" in p:
        return "cosmos"
    if "blenderkit" in p or p.endswith(".blend"):
        return "blenderkit"
    return "other"


# -- source loading ------------------------------------------------------------------------------------------------

TEXTURE_RE = re.compile(r"^T_(?P<set>.+?)_(?P<px>\d{3,5})_(?P<suffix>[A-Za-z_]+)\.(png|jpg|jpeg|tga)$", re.I)


def load_cosmos_zip(a, prov):
    """Extract one FBX and its texture set from a Cosmos zip; returns the texture sets {set: {suffix: path}}."""
    dest = os.path.join(a.work, a.asset_id, "src")
    shutil.rmtree(dest, ignore_errors=True)
    os.makedirs(dest)
    with zipfile.ZipFile(a.source) as z:
        names = z.namelist()
        fbx = [n for n in names if n.lower().endswith(".fbx") and a.mesh.lower() in os.path.basename(n).lower()]
        if len(fbx) != 1:
            raise SystemExit(f"--mesh must pick exactly one FBX; matched {len(fbx)}: "
                             + ", ".join(os.path.basename(n) for n in names if n.lower().endswith(".fbx")))
        wanted = {"B", "OPENGL_N", "N", "ORM", "AO", "R", "M", "OPACITY", "A"}
        tex = [n for n in names if (m := TEXTURE_RE.match(os.path.basename(n))) and m["suffix"].upper() in wanted]
        for n in fbx + tex:
            target = os.path.join(dest, os.path.basename(n))
            with z.open(n) as src, open(target, "wb") as dst:
                shutil.copyfileobj(src, dst)
            prov["files"].append({"path": n, "sha256": sha256(target), "bytes": os.path.getsize(target)})
    bpy.ops.import_scene.fbx(filepath=os.path.join(dest, os.path.basename(fbx[0])))
    prov["source_file"] = os.path.basename(fbx[0])
    return texture_sets(dest)


def texture_sets(folder):
    sets = {}
    for path in glob.glob(os.path.join(folder, "*")):
        m = TEXTURE_RE.match(os.path.basename(path))
        if m:
            sets.setdefault(m["set"], {})[m["suffix"].upper()] = path
    return sets


def load_gltf(a, prov):
    bpy.ops.import_scene.gltf(filepath=a.source)
    root = os.path.dirname(a.source)
    found = {os.path.basename(p).lower(): p for p in glob.glob(os.path.join(root, "**", "*"), recursive=True)}
    for img in bpy.data.images:
        path = bpy.path.abspath(img.filepath)
        if img.source == "FILE" and not os.path.exists(path) and os.path.basename(path).lower() in found:
            img.filepath = found[os.path.basename(path).lower()]
            img.reload()
    for img in bpy.data.images:
        path = bpy.path.abspath(img.filepath)
        if img.source == "FILE" and os.path.exists(path):
            prov["files"].append({"path": os.path.relpath(path, root), "sha256": sha256(path),
                                  "bytes": os.path.getsize(path)})
    prov["source_file"] = os.path.basename(a.source)


def load_blend(a, prov):
    bpy.ops.wm.open_mainfile(filepath=a.source)
    for o in bpy.data.objects:
        if "blenderkit" in o.keys():
            data = o["blenderkit"].to_dict() if hasattr(o["blenderkit"], "to_dict") else dict(o["blenderkit"])
            prov["blenderkit"] = {k: data.get(k) for k in ("name", "id", "asset_base_id", "is_free", "description", "tags")}
    view = bpy.context.view_layer
    for o in list(bpy.context.scene.objects):
        if o.hide_render or not o.visible_get(view_layer=view):
            continue
        if o.instance_type in ("COLLECTION", "VERTS", "FACES") or (o.type == "MESH" and o.particle_systems):
            bpy.ops.object.select_all(action="DESELECT")
            o.select_set(True)
            view.objects.active = o
            bpy.ops.object.duplicates_make_real()
    convertible = [o for o in bpy.context.scene.objects
                   if o.type in ("MESH", "CURVE", "SURFACE", "META", "FONT") and not o.hide_render
                   and o.visible_get(view_layer=view)]
    bpy.ops.object.select_all(action="DESELECT")
    for o in convertible:
        o.select_set(True)
    if convertible:
        view.objects.active = convertible[0]
        bpy.ops.object.convert(target="MESH")
    prov["source_file"] = os.path.basename(a.source)


def join_meshes(a):
    view = bpy.context.view_layer
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH" and not o.hide_render
              and o.visible_get(view_layer=view) and len(o.data.polygons)]
    if a.mesh and not a.source.lower().endswith(".zip"):
        meshes = [o for o in meshes if a.mesh.lower() in o.name.lower()]
    if not meshes:
        raise SystemExit("no visible mesh in the source" + (f" matching --mesh {a.mesh}" if a.mesh else ""))
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in list(bpy.context.scene.objects):
        if o not in meshes:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.hide_set(False)
        o.select_set(True)
        o.modifiers.clear()
    view.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = view.objects.active
    obj.data = obj.data.copy() if obj.data.users > 1 else obj.data
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for i in range(len(obj.data.uv_layers) - 1, 0, -1):
        if not obj.data.uv_layers[i].active_render:
            obj.data.uv_layers.remove(obj.data.uv_layers[i])
    return obj


# -- measurement -----------------------------------------------------------------------------------------------------

def coords(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def tri_count(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def normalise(obj, a, report):
    co = coords(obj)
    report["source_dimensions_m"] = np.round(np.ptp(co, axis=0), 4).tolist()
    m = Matrix.Rotation(math.radians(a.yaw), 4, "Z") @ Matrix.Scale(a.scale, 4)
    obj.data.transform(m)
    co = coords(obj)
    if a.target_height > 0:
        k = a.target_height / max(float(np.ptp(co[:, 2])), 1e-9)
        obj.data.transform(Matrix.Scale(k, 4))
        report["rescaled_by"] = "target_height"
        report["scale_factor"] = round(a.scale * k, 6)
    else:
        report["scale_factor"] = round(a.scale, 6)
    co = coords(obj)
    lo, hi = co.min(0), co.max(0)
    obj.data.transform(Matrix.Translation(Vector((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]))))
    obj.data.update()


# -- materials -------------------------------------------------------------------------------------------------------

CHANNEL = {"Red": 0, "R": 0, "Green": 1, "G": 1, "Blue": 2, "B": 2}


def follow(socket):
    """The output socket feeding an input, through reroutes; None when unlinked."""
    while socket.is_linked:
        out = socket.links[0].from_socket
        if out.node.type == "REROUTE":
            socket = out.node.inputs[0]
            continue
        return out
    return None


def plain_uv(image_node):
    """True when the image samples the mesh UVs unchanged (no mapping offset/scale, no procedural coordinates)."""
    out = follow(image_node.inputs["Vector"])
    if out is None:
        return True
    node = out.node
    if node.type == "UVMAP" or (node.type == "TEX_COORD" and out.name == "UV"):
        return True
    if node.type == "MAPPING":
        ins = node.inputs
        identity = (ins["Location"].is_linked is False and Vector(ins["Location"].default_value).length < 1e-6
                    and Vector(ins["Rotation"].default_value).length < 1e-6
                    and (Vector(ins["Scale"].default_value) - Vector((1, 1, 1))).length < 1e-6) \
            if "Location" in ins else False
        return identity and plain_uv(node)
    return False


def trace(socket):
    """What drives a shader input: ('const', value) | ('image', image, channel, factor) | ('bake',).

    channel is None for the full colour, 0..2 for one channel, 3 for alpha."""
    out = follow(socket)
    if out is None:
        v = socket.default_value
        return ("const", tuple(v) if hasattr(v, "__len__") else float(v))
    node = out.node
    if node.type == "TEX_IMAGE" and node.image and plain_uv(node):
        return ("image", node.image, 3 if out.name == "Alpha" else None, 1.0)
    if node.type in ("SEPARATE_COLOR", "SEPRGB") and getattr(node, "mode", "RGB") == "RGB":
        inner = trace(node.inputs[0])
        if inner[0] == "image" and inner[2] is None:
            return ("image", inner[1], CHANNEL[out.name], inner[3])
        return ("bake",)
    if node.type == "MATH" and node.operation == "MULTIPLY":
        a_, b_ = node.inputs[0], node.inputs[1]
        for img_side, k_side in ((a_, b_), (b_, a_)):
            if img_side.is_linked and not k_side.is_linked:
                inner = trace(img_side)
                if inner[0] == "image":
                    return ("image", inner[1], inner[2], inner[3] * float(k_side.default_value))
        return ("bake",)
    if node.type in ("MIX", "MIX_RGB"):
        blend = getattr(node, "blend_type", "")
        fac = node.inputs[0]
        if blend == "MULTIPLY" and not fac.is_linked:
            sockets = [s for s in node.inputs if s.type == "RGBA" and s.enabled]
            if len(sockets) == 2:
                for img_side, k_side in ((sockets[0], sockets[1]), (sockets[1], sockets[0])):
                    if img_side.is_linked and not k_side.is_linked and all(
                            abs(c - 1.0) < 1e-4 for c in tuple(k_side.default_value)[:3]):
                        return trace(img_side)
        return ("bake",)
    if node.type == "NORMAL_MAP":
        if node.space != "TANGENT" or node.inputs["Strength"].is_linked or abs(node.inputs["Strength"].default_value - 1) > 1e-4:
            return ("bake",)
        return trace(node.inputs["Color"])
    return ("bake",)


def principled(mat):
    if not mat or not mat.node_tree:
        return None
    out = next((n for n in mat.node_tree.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output), None)
    if out and out.inputs["Surface"].is_linked and out.inputs["Surface"].links[0].from_node.type == "BSDF_PRINCIPLED":
        return out.inputs["Surface"].links[0].from_node
    return next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)


def gltf_occlusion(mat):
    for n in mat.node_tree.nodes:
        if n.type == "GROUP" and n.node_tree and "gltf" in n.node_tree.name.lower() and "Occlusion" in n.inputs:
            return n.inputs["Occlusion"]
    return None


def harvest_material(mat):
    """The channels of one source material as traces; the bake list names inputs that need a Cycles bake."""
    bsdf = principled(mat)
    if bsdf is None:
        return {"base": ("bake",), "alpha": ("const", 1.0), "rough": ("const", 0.8), "metal": ("const", 0.0),
                "normal": None, "ao": ("const", 1.0), "bsdf": None}
    ch = {"bsdf": bsdf, "base": trace(bsdf.inputs["Base Color"]), "alpha": trace(bsdf.inputs["Alpha"]),
          "rough": trace(bsdf.inputs["Roughness"]), "metal": trace(bsdf.inputs["Metallic"])}
    ch["normal"] = trace(bsdf.inputs["Normal"]) if bsdf.inputs["Normal"].is_linked else None
    occ = gltf_occlusion(mat)
    if occ is not None and occ.is_linked:
        ch["ao"] = trace(occ)
    elif ch["rough"][0] == "image" and ch["rough"][2] == 1 and re.search(r"(arm|orm)", ch["rough"][1].name + os.path.basename(ch["rough"][1].filepath), re.I):
        # Poly Haven's _arm and an Unreal _ORM are occlusion/roughness/metallic in R/G/B; the glTF importer wires only G/B.
        ch["ao"] = ("image", ch["rough"][1], 0, 1.0)
    else:
        ch["ao"] = ("const", 1.0)
    return ch


def harvest_cosmos(mat, sets, report):
    """A Cosmos FBX material carries no images; bind the zip's texture set by name, or the only set there is."""
    if not sets:
        raise SystemExit("the zip has no T_<set>_<px>_<suffix> textures")
    key = mat.name.lower().replace("mi_", "").replace("m_", "") if mat else ""
    names = sorted(sets)
    pick = next((s for s in names if s.lower().split("_")[-1] in key or key in s.lower()), None)
    if pick is None:
        if len(names) > 1:
            raise SystemExit(f"cannot tell which texture set material '{mat.name if mat else None}' uses: {names}")
        pick = names[0]
    t = sets[pick]
    report.setdefault("texture_sets", {})[mat.name if mat else "none"] = pick

    def img(suffix, colour):
        im = bpy.data.images.load(t[suffix], check_existing=True)
        im.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        return im

    # Cosmos suffixes, measured on MET_Bucket01: B is the albedo (the only coloured map), C a grey curvature map,
    # ORM is AO/roughness/metallic (R matches _AO, G matches _R), G gloss, S specular, H height.
    if "B" not in t:
        raise SystemExit(f"texture set {pick} has no _B base colour")
    ch = {"bsdf": None, "base": ("image", img("B", True), None, 1.0), "alpha": ("const", 1.0)}
    if "OPACITY" in t or "A" in t:
        ch["alpha"] = ("image", img("OPACITY" if "OPACITY" in t else "A", False), 0, 1.0)
    elif img("B", True).depth == 32:
        ch["alpha"] = ("image", img("B", True), 3, 1.0)
    if "OPENGL_N" in t:
        ch["normal"] = ("image", img("OPENGL_N", False), None, 1.0)
    elif "N" in t:
        ch["normal"] = ("image", img("N", False), None, 1.0)
        ch["flip_green"] = True
    else:
        ch["normal"] = None
    if "ORM" in t:
        orm = img("ORM", False)
        ch.update(ao=("image", orm, 0, 1.0), rough=("image", orm, 1, 1.0), metal=("image", orm, 2, 1.0))
    else:
        ch["ao"] = ("image", img("AO", False), 0, 1.0) if "AO" in t else ("const", 1.0)
        ch["rough"] = ("image", img("R", False), 0, 1.0) if "R" in t else ("const", 0.8)
        ch["metal"] = ("image", img("M", False), 0, 1.0) if "M" in t else ("const", 0.0)
    return ch


# -- pixels ----------------------------------------------------------------------------------------------------------

_pixel_cache = {}


def srgb_encode(x):
    return np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(np.clip(x, 0, None), 1 / 2.4) - 0.055)


def pixels(img, size):
    """(h, w, 4) float32 of an image resized to size=(w, h), in the values the file stores (sRGB stays encoded)."""
    key = (img.name, size)
    if key in _pixel_cache:
        return _pixel_cache[key]
    if img.size[0] == 0:
        img.reload()
    if img.size[0] == 0:
        raise SystemExit(f"image {img.name} ({img.filepath}) could not be read")
    work = img.copy() if tuple(img.size) != size else img
    if work is not img:
        work.scale(*size)
    arr = np.empty(size[0] * size[1] * 4, dtype=np.float32)
    work.pixels.foreach_get(arr)
    arr = arr.reshape(size[1], size[0], 4)
    if img.is_float and img.colorspace_settings.name == "sRGB":
        arr[..., :3] = srgb_encode(arr[..., :3])
    if work is not img:
        bpy.data.images.remove(work)
    _pixel_cache[key] = arr
    return arr


def channel(trace_, size, default):
    if trace_ is None or trace_[0] == "const":
        v = default if trace_ is None else trace_[1]
        v = v[0] if isinstance(v, tuple) else v
        return np.full((size[1], size[0]), v, dtype=np.float32)
    _, img, ch, k = trace_
    arr = pixels(img, size)
    return np.clip(arr[..., 0 if ch is None else ch] * k, 0, 1)


def save_image(name, arr, path, colour, fmt, quality):
    h, w = arr.shape[:2]
    alpha = arr.shape[2] == 4 and float(arr[..., 3].min()) < 0.999
    img = bpy.data.images.new(name, w, h, alpha=alpha)
    img.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1), np.float32)], axis=2)
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    # Image.save writes the buffer as stored; save_render would push it through the view transform.
    img.file_format = "PNG" if (alpha or fmt == "PNG") else "JPEG"
    path = os.path.splitext(path)[0] + (".png" if img.file_format == "PNG" else ".jpg")
    img.filepath_raw = path
    img.save(quality=quality)
    bpy.data.images.remove(img)
    out = bpy.data.images.load(path)
    out.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    return out


# -- baking ----------------------------------------------------------------------------------------------------------

def bake_material(obj, mat, ch, size, folder):
    """Bake every input the trace could not read as a plain image onto the object's render UVs."""
    needs = [k for k in ("base", "alpha", "rough", "metal") if ch[k][0] == "bake"]
    normal_bake = ch["normal"] is not None and ch["normal"][0] == "bake"
    if not needs and not normal_bake:
        return
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.cycles.device = "CPU"
    scene.render.bake.margin = 16
    if not obj.data.uv_layers:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(island_margin=0.003)
        bpy.ops.object.mode_set(mode="OBJECT")
    others = [m for m in obj.data.materials if m and m != mat]
    nt = mat.node_tree
    bsdf = ch["bsdf"]
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output)
    surface = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].is_linked else None
    target = nt.nodes.new("ShaderNodeTexImage")
    emit = nt.nodes.new("ShaderNodeEmission")
    # Other materials on the same object must not receive this bake: give them a throwaway active image.
    dummy = bpy.data.images.new("_bake_dummy", 8, 8)
    other_nodes = []
    for m in others:
        n = m.node_tree.nodes.new("ShaderNodeTexImage")
        n.image = dummy
        m.node_tree.nodes.active = n
        other_nodes.append((m, n))
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    names = {"base": "Base Color", "alpha": "Alpha", "rough": "Roughness", "metal": "Metallic"}
    for key in needs:
        img = bpy.data.images.new(f"_bake_{key}", size[0], size[1], float_buffer=True)
        img.colorspace_settings.name = "Non-Color"
        target.image = img
        nt.nodes.active = target
        src = follow(bsdf.inputs[names[key]]) if bsdf else None
        for l in list(emit.inputs["Color"].links):
            nt.links.remove(l)
        if src is not None:
            nt.links.new(src, emit.inputs["Color"])
        else:
            emit.inputs["Color"].default_value = (0.8, 0.8, 0.8, 1) if key == "base" else (0, 0, 0, 1)
        nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
        bpy.ops.object.bake(type="EMIT", margin=16)
        arr = np.empty(size[0] * size[1] * 4, dtype=np.float32)
        img.pixels.foreach_get(arr)
        arr = arr.reshape(size[1], size[0], 4)
        if key == "base":
            # EMIT bakes scene-linear; the base colour map stores sRGB.
            arr[..., :3] = srgb_encode(np.clip(arr[..., :3], 0, 1))
        arr_path = os.path.join(folder, f"_baked_{mat.name}_{key}.npy")
        np.save(arr_path, arr)
        ch[key] = ("baked", arr_path)
        bpy.data.images.remove(img)
    if surface is not None:
        nt.links.new(surface, out.inputs["Surface"])
    if normal_bake:
        img = bpy.data.images.new("_bake_normal", size[0], size[1], float_buffer=True)
        img.colorspace_settings.name = "Non-Color"
        target.image = img
        nt.nodes.active = target
        bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", margin=16)
        arr = np.empty(size[0] * size[1] * 4, dtype=np.float32)
        img.pixels.foreach_get(arr)
        arr_path = os.path.join(folder, f"_baked_{mat.name}_normal.npy")
        np.save(arr_path, arr.reshape(size[1], size[0], 4))
        ch["normal"] = ("baked", arr_path)
        bpy.data.images.remove(img)
    nt.nodes.remove(target)
    nt.nodes.remove(emit)
    for m, n in other_nodes:
        m.node_tree.nodes.remove(n)
    bpy.data.images.remove(dummy)


def baked_or(trace_, size, default, index=0):
    if trace_ is not None and trace_[0] == "baked":
        return np.load(trace_[1])[..., index]
    return channel(trace_, size, default)


# -- output material -------------------------------------------------------------------------------------------------

def settings_group():
    """The node group the glTF exporter reads occlusion from."""
    name = "glTF Material Output"
    group = bpy.data.node_groups.get(name)
    if group is None:
        group = bpy.data.node_groups.new(name, "ShaderNodeTree")
        group.interface.new_socket("Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
        group.nodes.new("NodeGroupInput")
    return group


def build_material(name, maps, double_sided):
    """Base colour -> Base Color (+ alpha clip), ORM -> AO/R/M through one image, normal -> Normal Map."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    base = nt.nodes.new("ShaderNodeTexImage")
    base.image = maps["base"]
    nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    if maps["cutout"]:
        clip = nt.nodes.new("ShaderNodeMath")
        clip.operation = "ROUND"
        nt.links.new(base.outputs["Alpha"], clip.inputs[0])
        nt.links.new(clip.outputs["Value"], bsdf.inputs["Alpha"])
    orm = nt.nodes.new("ShaderNodeTexImage")
    orm.image = maps["orm"]
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(orm.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    occ = nt.nodes.new("ShaderNodeGroup")
    occ.node_tree = settings_group()
    nt.links.new(sep.outputs["Red"], occ.inputs["Occlusion"])
    if maps.get("normal") is not None:
        nimg = nt.nodes.new("ShaderNodeTexImage")
        nimg.image = maps["normal"]
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nimg.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    mat.use_backface_culling = not double_sided
    return mat


def material_maps(obj, a, sets, report, folder):
    """Write LOD0's map files per material slot; returns [(source material, arrays)] for re-use at lower levels."""
    out = []
    report["materials"] = []
    for slot_i, slot in enumerate(obj.material_slots):
        mat = slot.material
        ch = harvest_cosmos(mat, sets, report) if sets is not None else harvest_material(mat)
        imgs = [t[1] for t in (ch["base"], ch["rough"], ch["metal"], ch["ao"], ch["alpha"], ch["normal"] or ("const",))
                if t[0] == "image"]
        native = max((max(i.size) for i in imgs if i.size[0]), default=0)
        side = min(a.res, native) if native and not any(ch[k][0] == "bake" for k in ("base", "rough", "metal", "alpha")) else a.res
        ref = ch["base"][1] if ch["base"][0] == "image" else None
        if ref is not None and ref.size[0] and ref.size[0] != ref.size[1]:
            w, h = ref.size
            size = (side, max(1, round(side * h / w))) if w >= h else (max(1, round(side * w / h)), side)
        else:
            size = (side, side)
        if mat is not None and sets is None:
            bake_material(obj, mat, ch, size, folder)
        base = np.ones((size[1], size[0], 4), np.float32)
        if ch["base"][0] == "baked":
            base[..., :3] = np.load(ch["base"][1])[..., :3]
        elif ch["base"][0] == "image":
            base[..., :3] = pixels(ch["base"][1], size)[..., :3]
        else:
            base[..., :3] = np.array(ch["base"][1][:3], np.float32)
        base[..., 3] = baked_or(ch["alpha"], size, 1.0)
        cutout = float(base[..., 3].min()) < 0.5
        if not cutout:
            base = base[..., :3]
        orm = np.stack([baked_or(ch["ao"], size, 1.0), baked_or(ch["rough"], size, 0.8),
                        baked_or(ch["metal"], size, 0.0)], axis=2)
        normal = None
        if ch["normal"] is not None:
            normal = (np.load(ch["normal"][1]) if ch["normal"][0] == "baked" else pixels(ch["normal"][1], size))[..., :3].copy()
            if ch.get("flip_green"):
                normal[..., 1] = 1.0 - normal[..., 1]
        arrays = {"base": base, "orm": orm, "normal": normal, "cutout": cutout}
        record = {"slot": slot_i, "source_material": mat.name if mat else None, "native_px": native,
                  "lod0_px": list(size), "cutout": cutout,
                  "channels": {k: (ch[k][0] if ch[k] else "none") for k in ("base", "alpha", "rough", "metal", "ao", "normal")}}
        report["materials"].append(record)
        out.append((mat, arrays))
    return out


def apply_level_materials(obj, a, sources, level, folder, report):
    """Fresh materials and map files for one level; the texture side halves per level, never below MIN_LOD_PX."""
    double = a.category in FOLIAGE
    mats = []
    for i, (src, arrays) in enumerate(sources):
        h, w = arrays["base"].shape[:2]
        k = 1 if level == 0 else 2 ** level
        size = (max(min(w, MIN_LOD_PX), w // k), max(min(h, MIN_LOD_PX), h // k))
        tag = f"{a.asset_id}_m{i}" + (f"_lod{level}" if level else "")

        def resized(arr):
            if arr is None:
                return None
            if (arr.shape[1], arr.shape[0]) == size:
                return arr
            tmp = bpy.data.images.new("_resize", arr.shape[1], arr.shape[0], alpha=True, float_buffer=True)
            tmp.colorspace_settings.name = "Non-Color"
            full = arr if arr.shape[2] == 4 else np.concatenate([arr, np.ones(arr.shape[:2] + (1,), np.float32)], 2)
            tmp.pixels.foreach_set(np.ascontiguousarray(full, np.float32).ravel())
            tmp.scale(*size)
            res = np.empty(size[0] * size[1] * 4, np.float32)
            tmp.pixels.foreach_get(res)
            bpy.data.images.remove(tmp)
            return res.reshape(size[1], size[0], 4)[..., :arr.shape[2]]

        maps = {"cutout": arrays["cutout"],
                "base": save_image(f"{tag}_basecolor", resized(arrays["base"]), os.path.join(folder, f"{tag}_basecolor"),
                                   True, a.image_format, a.jpeg_quality),
                "orm": save_image(f"{tag}_orm", resized(arrays["orm"]), os.path.join(folder, f"{tag}_orm"),
                                  False, a.image_format, a.jpeg_quality)}
        if arrays["normal"] is not None:
            maps["normal"] = save_image(f"{tag}_normal", resized(arrays["normal"]), os.path.join(folder, f"{tag}_normal"),
                                        False, a.image_format, a.jpeg_quality)
        mats.append(build_material(f"MAT_{a.asset_id}_{i}" + (f"_lod{level}" if level else ""), maps, double))
    for i, m in enumerate(mats):
        obj.material_slots[i].material = m
    return [m.name for m in mats]


# -- geometry levels -------------------------------------------------------------------------------------------------

def decimate(obj, ratio):
    mod = obj.modifiers.new("lod", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = max(min(ratio, 1.0), 0.0005)
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def weld(obj, size):
    """Merge coincident vertices so a collapse decimator can cross UV seams and open scan shells (cw_boulder_a meta)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=max(size * 2e-5, 1e-5))
    bm.to_mesh(obj.data)
    bm.free()


def reshade(obj, a):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if obj.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    if a.category in ("rock", "environment") + FOLIAGE:
        bpy.ops.object.shade_smooth()
    else:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40), keep_sharp_edges=True)


def export(obj, path, materials=True):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    props = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
    kw = dict(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_apply=True,
              export_normals=True, export_texcoords=True, export_tangents=False,
              export_materials="EXPORT" if materials else "NONE", export_image_format="AUTO",
              export_animations=False, export_skins=False, export_morph=False, export_cameras=False,
              export_lights=False, export_extras=False)
    bpy.ops.export_scene.gltf(**{k: v for k, v in kw.items() if k in props or k == "filepath"})


# -- reading the GLB back --------------------------------------------------------------------------------------------

def image_dims(blob):
    if blob[:8] == b"\x89PNG\r\n\x1a\n":
        return list(struct.unpack(">II", blob[16:24])), "png"
    if blob[:2] == b"\xff\xd8":
        i = 2
        while i < len(blob):
            marker, length = blob[i + 1], struct.unpack(">H", blob[i + 2:i + 4])[0]
            if marker in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", blob[i + 5:i + 9])
                return [w, h], "jpeg"
            i += 2 + length
    return None, "unknown"


def glb_record(path):
    """Triangles, bounds, materials and image sizes from the GLB itself."""
    blob = open(path, "rb").read()
    off, doc, binary = 12, None, b""
    while off < len(blob):
        length, kind = struct.unpack_from("<II", blob, off)
        chunk = blob[off + 8:off + 8 + length]
        if kind == 0x4E4F534A:
            doc = json.loads(chunk)
        elif kind == 0x004E4942:
            binary = chunk
        off += 8 + length
    tris, lo, hi = 0, [1e9] * 3, [-1e9] * 3
    for mesh in doc.get("meshes", []):
        for prim in mesh["primitives"]:
            tris += doc["accessors"][prim.get("indices", prim["attributes"]["POSITION"])]["count"] // 3
            acc = doc["accessors"][prim["attributes"]["POSITION"]]
            lo = [min(lo[i], acc["min"][i]) for i in range(3)]
            hi = [max(hi[i], acc["max"][i]) for i in range(3)]
    images = []
    for im in doc.get("images", []):
        view = doc["bufferViews"][im["bufferView"]]
        data = binary[view.get("byteOffset", 0):view.get("byteOffset", 0) + view["byteLength"]]
        dims, kind = image_dims(data)
        images.append({"name": im.get("name"), "px": dims, "format": kind, "bytes": view["byteLength"]})
    orm_shared = all(m.get("occlusionTexture", {}).get("index") ==
                     m.get("pbrMetallicRoughness", {}).get("metallicRoughnessTexture", {}).get("index", -1)
                     for m in doc.get("materials", []))
    return {"triangles": tris, "min": [round(v, 4) for v in lo], "max": [round(v, 4) for v in hi],
            "materials": [m.get("name") for m in doc.get("materials", [])], "images": images,
            "orm_shared": orm_shared and bool(doc.get("materials")),
            "nodes": [n.get("name") for n in doc.get("nodes", [])],
            "mesh_names": [m.get("name") for m in doc.get("meshes", [])]}


# -- preview ---------------------------------------------------------------------------------------------------------

def render_preview(obj, path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.world = bpy.data.worlds.new("w")
    co = coords(obj)
    lo, hi = co.min(0), co.max(0)
    centre, extent = Vector(((lo + hi) / 2).tolist()), float(np.max(hi - lo))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = extent * 1.2
    scene.collection.objects.link(cam)
    scene.camera = cam
    tiles = []
    folder = os.path.dirname(path)
    for i, d in enumerate(((0, -1, 0.35), (1, 0, 0.35), (0, 1, 0.35), (-1, 0, 0.35))):
        cam.location = centre + Vector(d).normalized() * extent * 3
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"_view{i}.png")
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(scene.render.filepath)
        arr = np.empty(512 * 512 * 4, np.float32)
        im.pixels.foreach_get(arr)
        tiles.append(arr.reshape(512, 512, 4))
        bpy.data.images.remove(im)
        os.remove(scene.render.filepath)
    sheet = np.concatenate(tiles, axis=1)
    img = bpy.data.images.new("_sheet", sheet.shape[1], sheet.shape[0])
    img.pixels.foreach_set(sheet.ravel())
    img.filepath_raw, img.file_format = path, "PNG"
    img.save()
    bpy.data.objects.remove(cam, do_unlink=True)


# -- main ------------------------------------------------------------------------------------------------------------

def main():
    a = parse_args()
    started = datetime.datetime.now()
    out_dir = os.path.join(a.out, a.asset_id)
    folder = os.path.join(a.work, a.asset_id, "maps")
    shutil.rmtree(folder, ignore_errors=True)
    os.makedirs(folder)
    os.makedirs(out_dir, exist_ok=True)
    for stale in glob.glob(os.path.join(out_dir, f"{a.asset_id}*.glb")):
        os.remove(stale)

    fam = family(a.source)
    prov = {"files": []}
    report = {}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = os.path.splitext(a.source)[1].lower()
    sets = None
    if ext == ".zip":
        sets = load_cosmos_zip(a, prov)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=a.source)
        sets = texture_sets(os.path.join(os.path.dirname(os.path.dirname(a.source)), "Textures")) or None
        prov["source_file"] = os.path.basename(a.source)
    elif ext in (".gltf", ".glb"):
        load_gltf(a, prov)
    elif ext == ".blend":
        load_blend(a, prov)
    else:
        raise SystemExit(f"unsupported source {ext}")
    obj = join_meshes(a)
    if not obj.material_slots:
        obj.data.materials.append(None)
    report["source_triangles"] = tri_count(obj)
    normalise(obj, a, report)
    if a.max_tris and tri_count(obj) > a.max_tris:
        weld(obj, float(np.max(np.ptp(coords(obj), axis=0))))
        decimate(obj, a.max_tris / tri_count(obj))
        reshade(obj, a)
        report["lod0_decimated_to"] = tri_count(obj)

    obj.name = a.asset_id
    obj.data.name = f"{a.asset_id}_mesh"
    obj.data.uv_layers.active_index = next((i for i, u in enumerate(obj.data.uv_layers) if u.active_render), 0)
    sources = material_maps(obj, a, sets, report, folder)
    material_names = apply_level_materials(obj, a, sources, 0, folder, report)
    co = coords(obj)
    size = float(np.max(np.ptp(co, axis=0)))

    base_path = os.path.join(out_dir, f"{a.asset_id}.glb")
    export(obj, base_path)
    if a.preview:
        render_preview(obj, os.path.join(out_dir, f"{a.asset_id}_preview.png"))
    if a.save_blend:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(a.work, a.asset_id, f"{a.asset_id}.blend"), compress=True)

    collision = {"mode": a.collision}
    if a.collision != "none":
        lo, hi = co.min(0), co.max(0)
        bm = bmesh.new()
        if a.collision == "box":
            bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.scale(bm, vec=Vector((hi - lo).tolist()), verts=bm.verts)
            bmesh.ops.translate(bm, vec=Vector(((lo + hi) / 2).tolist()), verts=bm.verts)
        else:
            for p in co[:: max(1, len(co) // 20000)]:
                bm.verts.new(p.tolist())
            res = bmesh.ops.convex_hull(bm, input=bm.verts)
            bmesh.ops.delete(bm, geom=[g for g in res["geom_interior"] if isinstance(g, bmesh.types.BMVert)], context="VERTS")
        me = bpy.data.meshes.new(f"{a.asset_id}_collision_{a.collision}_mesh")
        bm.to_mesh(me)
        bm.free()
        proxy = bpy.data.objects.new(f"{a.asset_id}_collision_{a.collision}", me)
        bpy.context.scene.collection.objects.link(proxy)
        if a.collision == "hull" and len(me.polygons) > HULL_FACES:
            decimate(proxy, HULL_FACES / len(me.polygons))
        cpath = os.path.join(out_dir, f"{a.asset_id}_collision_{a.collision}.glb")
        export(proxy, cpath, materials=False)
        collision["file"] = os.path.basename(cpath)
        collision["dimensions_m"] = [round(float(v), 4) for v in (hi - lo)]

    ratios = [float(r) for r in a.lods.split(",") if r.strip()][:3]
    lods = {}
    base_tris = tri_count(obj)
    for level, ratio in enumerate(ratios, start=1):
        lod = obj.copy()
        lod.data = obj.data.copy()
        bpy.context.scene.collection.objects.link(lod)
        weld(lod, size)
        decimate(lod, ratio * base_tris / max(tri_count(lod), 1))
        reshade(lod, a)
        lod.name = f"{a.asset_id}_lod{level}"
        lod.data.name = f"{a.asset_id}_lod{level}_mesh"
        apply_level_materials(lod, a, sources, level, folder, report)
        path = os.path.join(out_dir, f"{a.asset_id}_lod{level}.glb")
        export(lod, path)
        lods[f"{a.asset_id}_lod{level}"] = {"ratio": ratio, "path": os.path.basename(path)}
        bpy.data.objects.remove(lod, do_unlink=True)

    # Everything below is read back from the files written.
    base = glb_record(base_path)
    outputs = []
    for p in sorted(glob.glob(os.path.join(out_dir, f"{a.asset_id}*.glb"))):
        outputs.append({"file": os.path.basename(p), "bytes": os.path.getsize(p), "sha256": sha256(p)})
    for name, entry in lods.items():
        rec = glb_record(os.path.join(out_dir, entry["path"]))
        sizes = [i["px"] for i in rec["images"] if i["px"]]
        entry.update(triangles=rec["triangles"], texture_px=max(sizes, key=lambda s: s[0] * s[1]) if sizes else None,
                     orm_shared=rec["orm_shared"], materials=rec["materials"])

    lic_default, lic_url = LICENCES[fam]
    licence = a.licence or lic_default
    unverified = licence.startswith("UNVERIFIED")
    dims = [round(base["max"][i] - base["min"][i], 4) for i in range(3)]
    meta = {
        "asset_id": a.asset_id,
        "name": a.asset_id,
        "category": a.category,
        "region": a.region or None,
        "source": a.source,
        "source_file": prov.get("source_file"),
        "source_sha256": sha256(a.source),
        "source_bytes": os.path.getsize(a.source),
        "source_files": prov["files"],
        "source_url": a.source_url or None,
        "pack": {"polyhaven": "Poly Haven", "cosmos": "Cosmos Premium", "blenderkit": "BlenderKit"}.get(fam, "other"),
        "blenderkit": prov.get("blenderkit"),
        "licence": licence,
        "licence_url": lic_url or None,
        "licence_evidence": a.licence_evidence or ("polyhaven.com/license: all Poly Haven assets are CC0" if fam == "polyhaven" else None),
        "modification": "permitted (CC0 waives all rights)" if fam == "polyhaven" else None,
        "built_by": f"{TOOL} (Blender {bpy.app.version_string})",
        "built": started.strftime("%Y-%m-%d %H:%M:%S"),
        "command": " ".join(sys.argv[sys.argv.index("--") + 1:]) if "--" in sys.argv else "",
        "triangles": base["triangles"],
        "source_triangles": report["source_triangles"],
        "transform": {
            "dimensions_m": {"x": dims[0], "y": dims[1], "z": dims[2]},
            "min": base["min"], "max": base["max"],
            "source_dimensions_m": report["source_dimensions_m"],
            "scale_factor": report["scale_factor"],
            "rescaled_by": report.get("rescaled_by", "source units (FBX unit scale applied)" if ext in (".zip", ".fbx") else "source units"),
            "yaw_deg": a.yaw,
            "units": "metres, 1 unit = 1 m, no node scale",
            "glb_up_axis": "+Y",
            "glb_forward_axis": "+Z (Blender -Y)",
            "glb_axes": "Y-up, one node, transforms applied, no residual scale or rotation",
            "glb_origin": "footprint centre, base on the ground plane (min Y = 0)",
        },
        "materials": material_names,
        "material_records": report["materials"],
        "texture_sets": report.get("texture_sets"),
        "textures": base["images"],
        "texture_resolution": a.res,
        "native_texture_resolution": max((m["native_px"] for m in report["materials"]), default=0),
        "orm": "one image per material bound to occlusionTexture and metallicRoughnessTexture (R=AO, G=roughness, B=metallic)",
        "orm_shared": base["orm_shared"],
        "lod_method": "Blender collapse decimate of the welded LOD0 at the stated ratio; textures halved per level, all levels textured",
        "lod_status": "present" if lods else "none",
        "lods": lods,
        "collision_status": collision["mode"],
        "collision": collision,
        "collision_note": "ArtLibrary never reads _collision_* files; the game sizes blockers from the mesh footprint "
                          "(ArtLibrary.TurnedFootprint). Proxies are written only when asked for.",
        "rigged": False,
        "sockets": [],
        "outputs": outputs,
        "status": "licence_unverified" if unverified else "export_ready",
        "game_integration": "NOT PROMOTED",
        "visual_acceptance": "UNREVIEWED",
        "notes": a.note,
    }
    meta = {k: v for k, v in meta.items() if v is not None}
    with open(os.path.join(out_dir, f"{a.asset_id}_meta.json"), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, indent=1, ensure_ascii=False)
    print("CONVERT_RESULT " + json.dumps({
        "asset_id": a.asset_id, "out": out_dir, "triangles": base["triangles"],
        "lods": {k: v["triangles"] for k, v in lods.items()}, "dimensions_m": dims,
        "textures": [(i["name"], i["px"], i["format"]) for i in base["images"]], "orm_shared": base["orm_shared"],
        "status": meta["status"], "seconds": round((datetime.datetime.now() - started).total_seconds(), 1)}))


if __name__ == "__main__":
    try:
        main()
    except SystemExit as e:
        if e.code not in (0, None):
            print(f"CONVERT_ERROR {e.code}")
        sys.stdout.flush()
        os._exit(1 if e.code not in (0, None) else 0)
