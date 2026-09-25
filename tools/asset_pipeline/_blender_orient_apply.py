"""Level a static LOD0 in Blender with the rotation _orient_asset.py measured, and render the evidence.

The rotation is baked into the mesh data (vertex positions, and normals = R @ the source file's glTF
NORMAL accessor, see level()), never into a node transform, so the exported mesh node stays identity. Material, UVs and embedded images are carried through the glTF round trip
unchanged. After rotating, the mesh is re-grounded (min Y = 0) and its X/Z bounding-box centre put on
the origin, matching _blender_cleanup.normalize_transform. Interaction sockets (SOCK_* empties) are
re-placed by the same bounds rule _author_interaction_sockets.py used, on the levelled bounds; their
orientation is left as authored (it is axis-aligned in the export frame by that rule).

Reads assets/ready/<id>/<id>.glb and assets/_staging/orient/<id>/<id>_orientation.json; writes only
under assets/_staging/orient/<id>/.

Run headless:
  blender --background --factory-startup --python _blender_orient_apply.py -- --asset <id> --mode apply
  blender --background --factory-startup --python _blender_orient_apply.py -- --asset <id> --mode candidates
Modes:
  apply       rotate + ground + export <id>.glb, then render <id>_before_after.png (front and side, one scale)
  candidates  render the four 90-degree yaw candidates of a rigid correction from +Z (the game's forward)
  render      re-render <id>_before_after.png from the staged GLB without re-exporting
"""
import argparse
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True    # leave no .pyc beside the tools
from _orient_asset import accessor, read_glb  # noqa: E402  (plain glTF reader, numpy only)

ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", "assets"))
STAGING = os.path.join(ASSETS, "_staging", "orient")
# glTF (x, y, z) -> Blender (x, -z, y), the importer's axis conversion.
G2B = np.array([[1.0, 0, 0], [0, 0, -1.0], [0, 1.0, 0]])
TILE = 1024


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--asset", required=True)
    p.add_argument("--mode", choices=["apply", "candidates", "render"], default="apply")
    p.add_argument("--source", help="GLB to level (default: ready/<id>/<id>.glb)")
    return p.parse_args(argv)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path, merge_vertices=False)
    return [o for o in bpy.data.objects if o not in before]


def to_blender(R):
    return G2B @ np.asarray(R, float) @ G2B.T


def mat4(R3=None, t=None):
    m = Matrix.Identity(4)
    if R3 is not None:
        for i in range(3):
            for j in range(3):
                m[i][j] = float(R3[i][j])
    if t is not None:
        m.translation = Vector([float(x) for x in t])
    return m


def mesh_world_coords(objs):
    pts = []
    for o in objs:
        if o.type != "MESH":
            continue
        co = np.empty(len(o.data.vertices) * 3)
        o.data.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        mw = np.array(o.matrix_world)
        pts.append(co @ mw[:3, :3].T + mw[:3, 3])
    return np.vstack(pts)


def source_vertex_normals(source, o):
    """glTF NORMAL of the source vertex behind each Blender vertex of object o (glTF frame), or None.

    Blender's decoded corner normals are not usable: its importer stores them as INT16_2D lnor
    offsets and decodes some vertices back as (0,0,0) (flora_willow_tree vertex 9011), and rotating
    re-decodes a few corners up to ~3 deg off. So the normals are read from the file itself.
    Vertex order follows io_scene_gltf2 imp/mesh.py do_primitives with merge_vertices=False: per
    primitive, np.unique of the triangle indices, primitives concatenated. The positions are
    compared vertex by vertex, so a changed importer fails loudly instead of misassigning normals."""
    js, binary = read_glb(source)
    nodes = [n for n in js.get("nodes", []) if "mesh" in n and n.get("name") == o.name]
    if not nodes:
        nodes = [n for n in js.get("nodes", []) if "mesh" in n and js["meshes"][n["mesh"]].get("name") == o.data.name]
    if len(nodes) != 1:
        raise RuntimeError(f"{o.name}: cannot identify its glTF mesh node ({len(nodes)} candidates)")
    prims = js["meshes"][nodes[0]["mesh"]]["primitives"]
    has = ["NORMAL" in p["attributes"] for p in prims]
    if not any(has):
        return None
    if not all(has):
        raise RuntimeError(f"{o.name}: some primitives carry NORMAL and some do not; refusing to guess")
    P, N = [], []
    for p in prims:
        if p.get("mode", 4) != 4:
            raise RuntimeError(f"{o.name}: primitive mode {p.get('mode')} is not triangles")
        pos = accessor(js, binary, p["attributes"]["POSITION"]).astype(float)
        idx = (np.unique(accessor(js, binary, p["indices"]).ravel()) if "indices" in p else np.arange(len(pos)))
        P.append(pos[idx])
        N.append(accessor(js, binary, p["attributes"]["NORMAL"]).astype(float)[idx])
    P, N = np.vstack(P), np.vstack(N)
    co = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    if len(co) != len(P):
        raise RuntimeError(f"{o.name}: {len(co)} Blender vertices vs {len(P)} glTF vertices; mapping unknown")
    gap = float(np.abs(co - P @ G2B.T).max())
    if gap > 1e-5:
        raise RuntimeError(f"{o.name}: Blender vertex order does not match the glTF vertices (max gap {gap:.3g} m)")
    return N


def level(objs, R_gltf, source):
    """Bake R into mesh data, then translate so min Z(up) = 0 and X/Y(depth) bbox centre = 0.
    Normals are written as R @ the source glTF NORMAL (see source_vertex_normals)."""
    meshes = [o for o in objs if o.type == "MESH"]
    for o in meshes:
        if not np.allclose(np.array(o.matrix_world), np.eye(4), atol=1e-6):
            raise RuntimeError(f"{o.name}: mesh object has a non-identity transform; refusing to guess its frame")
    Rb = to_blender(R_gltf)
    normals = []
    for o in meshes:
        me = o.data
        vn = source_vertex_normals(source, o)
        me.transform(mat4(Rb))
        if vn is not None:
            # glTF normals are per vertex and the import keeps one Blender vertex per glTF vertex
            # (merge_vertices=False), so they go back as free float normals per vertex (POINT),
            # which the exporter writes verbatim and without splitting any vertex.
            if "custom_normal" in me.attributes:
                me.attributes.remove(me.attributes["custom_normal"])
            free = me.attributes.new("custom_normal", "FLOAT_VECTOR", "POINT")
            free.data.foreach_set("vector", (vn @ G2B.T @ Rb.T).ravel())
            normals.append({"object": o.name, "vertices": int(len(vn)), "source": "glTF NORMAL accessor of the source file",
                            "source_normals_equal_0_1_0": int(np.all(vn == [0.0, 1.0, 0.0], 1).sum()),
                            "source_normals_not_unit": int((np.abs(np.linalg.norm(vn, axis=1) - 1) > 1e-3).sum())})
        me.update()
    P = mesh_world_coords(meshes)
    lo, hi = P.min(0), P.max(0)
    t = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])
    for o in meshes:
        o.data.transform(mat4(t=t))
        o.data.update()
    bpy.context.view_layer.update()
    return Rb, t, normals


def gltf_bounds(objs):
    P = mesh_world_coords(objs) @ G2B      # Blender -> glTF: v_g = G2B^T v_b, row form v_b @ G2B
    return P.min(0), P.max(0)


def replace_sockets(objs, Rb, t):
    """Socket empties: record the rigidly carried position, then place them by the authoring rule on
    the levelled bounds (their rule is 'fraction of the bbox', which the tilted bbox had corrupted)."""
    sockets = [o for o in objs if o.type == "EMPTY" and o.name.startswith("SOCK_")]
    if not sockets:
        return []
    sys.path.insert(0, HERE)
    sys.dont_write_bytecode = True    # leave no .pyc beside another track's tool
    from _author_interaction_sockets import ROLES  # read-only use of the authoring rule
    lo, hi = gltf_bounds(objs)
    out = []
    for s in sockets:
        old = np.array(s.matrix_world.translation)
        rigid = Rb @ old + t
        entry = {"name": s.name, "old_translation_gltf": (G2B.T @ old).round(5).tolist(),
                 "rigidly_carried_gltf": (G2B.T @ rigid).round(5).tolist()}
        role = ROLES.get(s.name)
        if role:
            g = lo + np.array(role["at"]) * (hi - lo)
            s.location = Vector((G2B @ g).tolist())
            entry["new_translation_gltf"] = g.round(5).tolist()
            entry["rule"] = {"at": role["at"], "source": "_author_interaction_sockets.ROLES"}
        else:
            s.location = Vector(rigid.tolist())
            entry["new_translation_gltf"] = entry["rigidly_carried_gltf"]
            entry["rule"] = "no authoring rule for this name; carried rigidly"
        out.append(entry)
    bpy.context.view_layer.update()
    return out


def export(objs, path):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        if o.type in ("MESH", "EMPTY"):
            o.select_set(True)
    bpy.context.view_layer.objects.active = next(o for o in objs if o.type == "MESH")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_apply=False,
        export_yup=True, export_normals=True, export_materials="EXPORT", export_texcoords=True,
        export_tangents=False, export_extras=True)


# ----------------------------------------------------------------------------------------- render
def flat_material(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    return m


def box(name, center, size, mat):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.data.materials.append(mat)
    return o


def caption(cam, body, depth, mat, line_frac=0.028, margin_frac=0.02):
    """Caption pinned to the camera's top-left corner at `depth` in front of it (between camera and
    model), line height line_frac of the frame width, shrunk until its widest line fits the frame.
    Placed in camera space from the camera's own field of view, so no caption runs off a tile edge."""
    if cam.data.type == "ORTHO":
        half = cam.data.ortho_scale / 2
    else:
        half = depth * cam.data.sensor_width / 2 / cam.data.lens    # square tiles: same both ways
    cam.data.clip_start = min(cam.data.clip_start, depth * 0.05)
    bpy.ops.object.text_add()
    o = bpy.context.active_object
    o.data.body = body
    o.data.size = 2 * half * line_frac
    o.data.materials.append(mat)
    o.parent = cam
    o.rotation_euler = (0, 0, 0)       # camera space: text in its XY plane, facing the camera
    bpy.context.view_layer.update()
    avail = 2 * half * (1 - 2 * margin_frac)
    k = min(1.0, avail / max(o.dimensions.x, 1e-9))
    o.data.size *= k
    m = 2 * half * margin_frac
    o.location = (-half + m, half - m - o.data.size, -depth)
    bpy.context.view_layer.update()
    return o


def setup_render():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_shadows = False
    sc.render.resolution_x = TILE
    sc.render.resolution_y = TILE
    sc.render.film_transparent = False
    if sc.world is None:
        sc.world = bpy.data.worlds.new("W")
    sc.world.color = (0.82, 0.82, 0.84)
    sc.view_settings.view_transform = "Standard"
    sc.render.image_settings.file_format = "PNG"
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def aim(cam, view, frame):
    """view: 'front' looks from glTF +Z (Blender -Y); 'side' from glTF +X. frame = (scale, zc)."""
    scale, zc = frame
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = scale
    cam.data.clip_end = 1000
    d = scale * 5
    if view == "front":
        cam.location = (0, -d, zc)
        cam.rotation_euler = (math.pi / 2, 0, 0)
    else:
        cam.location = (d, 0, zc)
        cam.rotation_euler = (math.pi / 2, 0, math.pi / 2)


def helpers(cam, view, frame, label):
    """Ground slab at Z=0, a thin vertical reference line through the origin (in front of the model),
    and a caption. Returns the objects so they can be removed."""
    scale, zc = frame
    dark, red, ink = flat_material("h_dark", (0.12, 0.12, 0.12)), flat_material("h_red", (0.85, 0.05, 0.05)), flat_material("h_ink", (0.0, 0.0, 0.0))
    line = scale / TILE * 2.5
    near = -scale * 2 if view == "front" else scale * 2
    objs = []
    if view == "front":
        objs.append(box("h_ground", (0, 0, -line / 2), (scale * 2, scale * 2, line), dark))
        objs.append(box("h_vert", (0, near, zc), (line, line, scale * 2), red))
    else:
        objs.append(box("h_ground", (0, 0, -line / 2), (scale * 2, scale * 2, line), dark))
        objs.append(box("h_vert", (near, 0, zc), (line, line, scale * 2), red))
    # camera sits 5 x scale out (aim()); 2.5 x scale is in front of the red line and the model
    objs.append(caption(cam, label, scale * 2.5, ink))
    return objs


def render_to(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def compose(tiles, cols, out):
    """Paste equal-size PNG tiles (row-major, top row first) into one PNG using numpy."""
    rows = (len(tiles) + cols - 1) // cols
    W, H = TILE * cols, TILE * rows
    canvas = np.ones((H, W, 4), np.float32)
    for i, p in enumerate(tiles):
        img = bpy.data.images.load(p)
        px = np.empty(img.size[0] * img.size[1] * 4, np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(img.size[1], img.size[0], 4)       # bottom row first
        r, c = divmod(i, cols)
        y0 = H - (r + 1) * TILE
        canvas[y0:y0 + TILE, c * TILE:(c + 1) * TILE] = px
        bpy.data.images.remove(img)
    out_img = bpy.data.images.new("sheet", W, H, alpha=True)
    out_img.pixels.foreach_set(canvas.ravel())
    out_img.filepath_raw = out
    out_img.file_format = "PNG"
    out_img.save()
    for p in tiles:
        os.remove(p)


def dims(objs):
    lo, hi = gltf_bounds(objs)
    return (hi - lo)


def render_before_after(asset, before_path, after_path, outdir, rec):
    reset()
    cam = setup_render()
    before = import_glb(before_path)
    db = dims(before)
    for o in before:
        o.hide_render = True
    after = import_glb(after_path)
    da = dims(after)
    for o in after:
        o.hide_render = True
    height = max(db[1], da[1])
    widths = {"front": max(db[0], da[0]), "side": max(db[2], da[2])}
    span = max(height, widths["front"], widths["side"]) * 1.18
    frame = (span, height / 2 + span * 0.02)
    corr = rec.get("correction", {})
    tiles = []
    for view in ("front", "side"):
        for tag, objs, d in (("BEFORE (ready LOD0)", before, db), ("AFTER (levelled)", after, da)):
            for o in before + after:
                o.hide_render = True
            for o in objs:
                o.hide_render = o.type != "MESH"
            aim(cam, view, frame)
            vtxt = "front: from +Z" if view == "front" else "side: from +X, +Z (front) at left"
            extra = f"   rotation {corr.get('total_rotation_deg')} deg" if tag.startswith("AFTER") else f"   measured tilt {corr.get('level_tilt_deg')} deg"
            label = f"{asset}  {tag}  {vtxt}\nbbox x {d[0]:.3f}  y(up) {d[1]:.3f}  z {d[2]:.3f} m{extra}\nred line = vertical through origin; dark bar = ground Y=0"
            h = helpers(cam, view, frame, label)
            p = os.path.join(outdir, f"_tile_{view}_{len(tiles)}.png")
            render_to(p)
            tiles.append(p)
            for o in h:
                bpy.data.objects.remove(o, do_unlink=True)
    out = os.path.join(outdir, f"{asset}_before_after.png")
    compose(tiles, 2, out)
    return out, db.round(4).tolist(), da.round(4).tolist()


def render_candidates(asset, source, outdir, rec):
    """Four yaw candidates, each levelled and viewed from +Z (the game's forward) and slightly above."""
    tiles = []
    for c in rec["candidates"]:
        reset()
        cam = setup_render()
        objs = import_glb(source)
        level(objs, c["R"], source)
        for o in objs:
            o.hide_render = o.type != "MESH"
        d = dims(objs)
        span = max(d) * 1.5
        cam.data.type = "PERSP"
        cam.data.lens = 50
        el = math.radians(22)
        dist = span * 2.2
        cam.location = (0, -dist * math.cos(el), d[1] / 2 + dist * math.sin(el))
        cam.rotation_euler = (math.pi / 2 - el, 0, 0)
        ink = flat_material("h_ink", (0, 0, 0))
        dark = flat_material("h_dark", (0.35, 0.35, 0.35))
        box("h_ground", (0, 0, -0.005 * span), (span * 3, span * 3, 0.01 * span), dark)
        # arrow on the ground pointing to +Z glTF (Blender -Y), toward the camera
        box("h_arrow", (0, -d[2] / 2 - span * 0.12, 0.002 * span), (span * 0.02, span * 0.2, 0.004 * span), flat_material("h_red", (0.85, 0.05, 0.05)))
        caption(cam, f"{asset} candidate {c['index']}: yaw +{c['yaw_after_level_deg']} after level, rot {c['rotation_deg']} deg\n"
                     f"camera on +Z (game forward), 22 deg above; red arrow = +Z", dist * 0.35, ink)
        p = os.path.join(outdir, f"_cand_{c['index']}.png")
        render_to(p)
        tiles.append(p)
    out = os.path.join(outdir, f"{asset}_yaw_candidates.png")
    compose(tiles, 2, out)
    return out


def main():
    args = parse_args()
    outdir = os.path.join(STAGING, args.asset)
    rec_path = os.path.join(outdir, f"{args.asset}_orientation.json")
    rec = json.load(open(rec_path, encoding="utf-8"))
    source = args.source or os.path.join(ASSETS, "ready", args.asset, f"{args.asset}.glb")
    staged = os.path.join(outdir, f"{args.asset}.glb")

    if args.mode == "candidates":
        out = render_candidates(args.asset, source, outdir, rec)
        print("ORIENT_CANDIDATES " + out)
        return

    if args.mode == "apply":
        if rec.get("status") == "refused" and not rec.get("override"):
            raise SystemExit(f"{args.asset}: correction refused ({rec.get('flags')}); not applied")
        reset()
        objs = import_glb(source)
        R = rec["correction"]["R"]
        Rb, t, normals = level(objs, R, source)
        sockets = replace_sockets(objs, Rb, t)
        export(objs, staged)
        rec["apply"] = {
            "blender": bpy.app.version_string, "source": source, "output": staged,
            "rotation_blender_frame": np.round(Rb, 7).tolist(),
            "translation_blender_frame": np.round(t, 6).tolist(),
            "translation_gltf_frame": np.round(G2B.T @ t, 6).tolist(),
            "baked_into": "mesh data (Mesh.transform); mesh node transform left identity",
            "normals": normals,
            "sockets": sockets,
            "export": {"export_yup": True, "export_normals": True, "export_materials": "EXPORT",
                       "export_texcoords": True, "export_tangents": False, "export_extras": True},
        }
        json.dump(rec, open(rec_path, "w", encoding="utf-8"), indent=1)

    out, db, da = render_before_after(args.asset, source, staged, outdir, rec)
    rec = json.load(open(rec_path, encoding="utf-8"))
    rec["renders"] = {"before_after": out, "tile_px": TILE, "dims_before_gltf_xyz": db, "dims_after_gltf_xyz": da}
    json.dump(rec, open(rec_path, "w", encoding="utf-8"), indent=1)
    print("ORIENT_APPLY " + json.dumps({"asset": args.asset, "glb": staged, "sheet": out, "dims_before": db, "dims_after": da}))


if __name__ == "__main__":
    main()
