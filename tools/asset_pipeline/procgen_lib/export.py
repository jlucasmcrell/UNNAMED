"""glTF export, final PBR material, sockets, provenance and the staging-path guard.

Extracted from the gltf_output_group / final_material / flat_material(s) / add_socket / export / provenance blocks
that all eight pre-gate generators carried, with one size policy for embedded maps: base colour and ORM are JPEG
(quality 90), the normal map PNG, so a 2048 atlas embeds in ~2-4 MB instead of 10-30 MB of PNG.

API
    REPO, ASSETS, STAGING (assets/_staging/procgen)
    guard_output(path, asset_id=None) -> abs path    refuses assets/ready|rigged|animation and anything in assets/
        outside assets/_staging
    default_out_dir(asset_id) -> assets/_staging/procgen/<asset_id>
    gltf_output_group(), final_material(objs, paths, name, single_sided=True), flat_materials(obj, materials)
    add_socket(parent, name, location, display_size=0.15) -> empty child (location in the parent's frame)
    export_glb(path, image_quality=90) -> path        +Y up, named nodes, children kept, no extras, no tangents
    gltf_vec(v)                                         Blender Z-up -> glTF Y-up (x, z, -y)
    sha256_file(path), library_sha256(), code_lines(path) (non-blank, non-comment, docstrings excluded)
    write_provenance(path, record) -> path              JSON, keys in a fixed order
"""
import ast
import glob
import hashlib
import io
import json
import os
import tokenize

LIB_DIR = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(LIB_DIR)
REPO = os.path.dirname(os.path.dirname(TOOLS))
ASSETS = os.path.join(REPO, "assets")
STAGING = os.path.join(ASSETS, "_staging", "procgen")


def default_out_dir(asset_id):
    return os.path.join(STAGING, asset_id)


def guard_output(path, asset_id=None):
    """The library never writes the shipped library: assets/ready, rigged and animation are refused, and inside
    assets/ only assets/_staging is allowed."""
    p = os.path.normcase(os.path.abspath(path))
    assets = os.path.normcase(ASSETS)
    for forbidden in ("ready", "rigged", "animation"):
        root = os.path.join(assets, forbidden)
        if p == root or p.startswith(root + os.sep):
            raise SystemExit(f"refusing to write into assets/{forbidden}: {path}")
    if p.startswith(assets + os.sep) and not p.startswith(os.path.join(assets, "_staging") + os.sep):
        raise SystemExit(f"refusing to write inside assets/ outside assets/_staging: {path}")
    return os.path.abspath(path)


def gltf_output_group():
    import bpy
    ng = bpy.data.node_groups.get("glTF Material Output")
    if ng is None:
        ng = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
        ng.interface.new_socket(name="Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
        ng.nodes.new("NodeGroupInput")
    return ng


def final_material(objs, paths, name, single_sided=True):
    """One Principled material reading the baked maps (ORM through the glTF occlusion group), on every face of objs."""
    import bpy
    import numpy as np
    mat = bpy.data.materials.new(name)
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    out = tree.nodes.new("ShaderNodeOutputMaterial")
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    tree.links.new(bsdf.outputs[0], out.inputs["Surface"])

    def tex(key, colourspace):
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(paths[key], check_existing=True)
        node.image.colorspace_settings.name = colourspace
        return node
    col = tex("basecolor", "sRGB")
    tree.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    orm = tex("orm", "Non-Color")
    sep = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(orm.outputs["Color"], sep.inputs[0])
    tree.links.new(sep.outputs[1], bsdf.inputs["Roughness"])
    tree.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
    grp = tree.nodes.new("ShaderNodeGroup")
    grp.node_tree = gltf_output_group()
    tree.links.new(sep.outputs[0], grp.inputs["Occlusion"])
    nrm = tex("normal", "Non-Color")
    nmap = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    tree.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    mat.use_backface_culling = single_sided      # closed solids: single-sided in glTF
    for obj in objs:
        me = obj.data
        me.materials.clear()
        me.materials.append(mat)
        me.polygons.foreach_set("material_index", np.zeros(len(me.polygons), np.int32))
        me.update()
    return mat


def flat_materials(obj, materials, single_sided=True):
    """--no-bake: plain colours per slot, for a fast geometry review."""
    import bpy
    import numpy as np
    me = obj.data
    idx = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", idx)
    me.materials.clear()
    for m in materials:
        mat = bpy.data.materials.new(f"MAT_flat_{m.name}")
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = tuple(m.flat_colour) + (1.0,)
        bsdf.inputs["Roughness"].default_value = 0.8
        bsdf.inputs["Metallic"].default_value = m.flat_metal
        mat.use_backface_culling = single_sided
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", idx)
    me.update()


def add_socket(parent, name, location, display_size=0.15, basis=None):
    """basis: optional 3x3 (columns = the socket's local x, y, z in Blender space); glTF local +Y is Blender local +Z."""
    import bpy
    from mathutils import Matrix
    s = bpy.data.objects.new(name, None)
    s.empty_display_type = "ARROWS"
    s.empty_display_size = display_size
    s.location = tuple(float(v) for v in location)
    if basis is not None:
        s.rotation_mode = "QUATERNION"
        s.rotation_quaternion = Matrix([[float(x) for x in row] for row in basis]).to_quaternion()
    bpy.context.scene.collection.objects.link(s)
    s.parent = parent
    s.matrix_parent_inverse = Matrix.Identity(4)
    return s


def export_glb(path, image_quality=90):
    """Whole scene to a GLB: +Y up, node names kept, empties (sockets) exported as child nodes, images embedded in
    their source format (the bake writes JPEG base colour/ORM and a PNG normal)."""
    import bpy
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_materials="EXPORT", export_yup=True,
                              export_apply=True, export_normals=True, export_texcoords=True, export_tangents=False,
                              export_image_format="AUTO", export_image_quality=image_quality, use_selection=False,
                              export_extras=False)
    # The importer reuses a group of this name and expects its full socket set; rename ours so a read-back import
    # (validate.glb_gate) in the same session builds its own.
    ng = bpy.data.node_groups.get("glTF Material Output")
    if ng is not None:
        ng.name = "_occlusion_export_group"
    return path


def gltf_vec(v):
    """Blender Z-up to glTF Y-up (x, z, -y)."""
    return [round(float(v[0]), 5), round(float(v[2]), 5), round(float(-v[1]), 5)]


def sha256_file(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def library_sha256():
    """One digest over every procgen_lib module, so a provenance record pins the library version it was built with."""
    h = hashlib.sha256()
    for p in sorted(glob.glob(os.path.join(LIB_DIR, "*.py"))):
        h.update(os.path.basename(p).encode())
        with open(p, "rb") as handle:
            h.update(handle.read())
    return h.hexdigest()


def code_lines(path):
    """Lines of code: non-blank, not only a comment, not inside a docstring (the cost rule's asset-specific lines)."""
    src = open(path, encoding="utf-8").read()
    doc_lines = set()
    for node in ast.walk(ast.parse(src)):
        if isinstance(node, (ast.Module, ast.FunctionDef, ast.ClassDef, ast.AsyncFunctionDef)) and node.body:
            first = node.body[0]
            if isinstance(first, ast.Expr) and isinstance(getattr(first, "value", None), ast.Constant) \
                    and isinstance(first.value.value, str):
                doc_lines.update(range(first.lineno, first.end_lineno + 1))
    code = set()
    for tok in tokenize.generate_tokens(io.StringIO(src).readline):
        if tok.type not in (tokenize.COMMENT, tokenize.NL, tokenize.NEWLINE, tokenize.INDENT, tokenize.DEDENT,
                            tokenize.ENDMARKER):
            code.update(range(tok.start[0], tok.end[0] + 1))
    return len(code - doc_lines)


PROVENANCE_ORDER = ("asset_id", "method", "archetype", "template", "self_test", "script", "script_sha256",
                    "library", "library_sha256", "command", "blender", "seed", "date", "concept", "concept_sha256",
                    "parameters", "design_notes", "nodes", "frame", "dimensions_m", "triangles", "triangle_budget",
                    "checks", "materials", "outputs", "glb_bytes", "glb_sha256", "cost", "build_seconds")


def write_provenance(path, record):
    ordered = {k: record[k] for k in PROVENANCE_ORDER if k in record}
    ordered.update({k: v for k, v in record.items() if k not in ordered})
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(ordered, handle, indent=2, default=lambda o: o.tolist() if hasattr(o, "tolist") else str(o))
    return path
