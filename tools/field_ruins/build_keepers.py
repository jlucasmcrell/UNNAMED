"""The Stopped Gate's keepers (docs/FIELD_RUINS.md): the gate tender and the Gate Warden, built on the golem's rig.

The golem (creature_golem_stan, ShyrAssets, CC BY 4.0) is fifteen rigid blocks, one per bone. Each block is replaced by a piece of
the Spill's own iron (the converted Red Echo modules in the field_ruins depot) fitted to the block's box and weighted wholly to its
bone, and the skeleton is scaled to the keeper's height, so the golem's six clips drive it unchanged (ArtLibrary.Retarget matches by
bone name and scales root travel by height). A violet core sits in the open back, where the weak point is.

  blender --background --factory-startup --python tools/field_ruins/build_keepers.py -- --out D:/Otherreach_Depot/field_ruins
"""
import math
import os
import sys

import bpy
import mathutils
import numpy as np

GOLEM = r"C:\Otherreach_Recovery\asset_work\creatures\ready_new\creature_golem_stan\creature_golem_stan.glb"
DEPOT = r"D:\Otherreach_Depot\field_ruins\ready"   # --depot: the pieces the keepers are built from
MAX_TEXTURE = 1024   # --max-texture
GOLEM_HEIGHT = 4.0
VIOLET = (0.62, 0.22, 1.0)

# Which piece of iron stands for which block: the rock-capped modules, so a keeper reads as stone and iron rather than panelling.
PARTS = {
    "Body": "ruin_fs_pylon_b", "Center": "ruin_fs_stub_b", "Head": "ruin_fs_pylon_b",
    "UpperArm_L": "ruin_fs_pylon_c", "UpperArm_R": "ruin_fs_pylon_c", "LowerArm_L": "ruin_fs_pylon_c", "LowerArm_R": "ruin_fs_pylon_c",
    "Hand_L": "ruin_fs_pylon_b", "Hand_R": "ruin_fs_pylon_b", "Leg_L": "ruin_fs_pylon_c", "Leg_R": "ruin_fs_pylon_c",
    "LowerLeg_L": "ruin_fs_pylon_a", "LowerLeg_R": "ruin_fs_pylon_a", "Feet_L": "ruin_fs_stub_a", "Feet_R": "ruin_fs_stub_a",
}
KEEPERS = {
    # id: (height m, limb thickness against the golem's blocks, core size against the body)
    "creature_gate_tender": (3.0, 0.72, 0.35),
    "creature_gate_warden": (8.0, 1.0, 0.45),
}


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def block_boxes(mesh):
    """The golem's block for each bone: the world-space box of the vertices weighted mostly to it."""
    names = {g.index: g.name for g in mesh.vertex_groups}
    boxes = {}
    for v in mesh.data.vertices:
        if not v.groups:
            continue
        g = max(v.groups, key=lambda e: e.weight)
        p = mesh.matrix_world @ v.co
        lo, hi = boxes.get(names[g.group], (p.copy(), p.copy()))
        boxes[names[g.group]] = (mathutils.Vector(map(min, lo, p)), mathutils.Vector(map(max, hi, p)))
    return boxes


def piece(asset):
    """A fresh copy of a depot piece, joined into one mesh with its transforms applied."""
    objs = [o for o in import_glb(os.path.join(DEPOT, asset, asset + ".glb")) if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def fit(ob, lo, hi, thickness):
    """Lay the piece's longest side along the block's, then stretch it to the block (narrowed across by thickness)."""
    size = hi - lo
    co = [v.co for v in ob.data.vertices]
    dims = [max(c[i] for c in co) - min(c[i] for c in co) for i in range(3)]
    order_piece = sorted(range(3), key=lambda i: dims[i])
    order_block = sorted(range(3), key=lambda i: size[i])
    # A rotation that carries the piece's axes, shortest to longest, onto the block's.
    m = mathutils.Matrix.Identity(3)
    for a, b in zip(order_piece, order_block):
        m[b] = mathutils.Vector([1.0 if i == a else 0.0 for i in range(3)])
    if m.determinant() < 0:
        m[order_block[0]] = -m[order_block[0]]
    ob.data.transform(m.to_4x4())
    ob.data.update()
    # From the vertices themselves: an object's bound_box is cached and still describes the mesh before the turn above.
    co = [v.co for v in ob.data.vertices]
    plo = mathutils.Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    phi = mathutils.Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    psize = phi - plo
    across = [i for i in range(3) if i != order_block[2]]
    scale = mathutils.Vector([size[i] / max(psize[i], 1e-4) * (thickness if i in across else 1.0) for i in range(3)])
    centre = (lo + hi) / 2
    pc = (plo + phi) / 2
    ob.data.transform(mathutils.Matrix.Translation(-pc))
    ob.data.transform(mathutils.Matrix.Diagonal((*scale, 1.0)))
    ob.data.transform(mathutils.Matrix.Translation(centre))
    ob.data.update()


def core(body_lo, body_hi, size_frac):
    """The open back's violet core: a glowing drum set into the back of the body block."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=1.0, depth=1.0)
    ob = bpy.context.active_object
    size = body_hi - body_lo
    r = min(size.x, size.z) * size_frac / 2
    ob.scale = (r, r * 0.6, r)
    ob.rotation_euler = (math.radians(90), 0, 0)
    # glTF's +Z forward is Blender's -Y: the golem faces -Y, so its back is +Y.
    ob.location = ((body_lo.x + body_hi.x) / 2, body_hi.y - r * 0.25, body_lo.z + size.z * 0.55)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    mat = bpy.data.materials.new("keeper_core_violet")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*VIOLET, 1)
    bsdf.inputs["Emission Color"].default_value = (*VIOLET, 1)
    bsdf.inputs["Emission Strength"].default_value = 6.0
    bsdf.inputs["Roughness"].default_value = 0.3
    ob.data.materials.append(mat)
    return ob


def build(asset_id, height, thickness, core_frac, out):
    fresh()
    imported = import_glb(GOLEM)
    rig = next(o for o in imported if o.type == "ARMATURE")
    meshes = [o for o in imported if o.type == "MESH"]
    body = next(o for o in meshes if not o.name.endswith(("_LOD1", "_LOD2")))
    boxes = block_boxes(body)
    for o in meshes:
        bpy.data.objects.remove(o, do_unlink=True)
    parts = []
    for bone, asset in PARTS.items():
        lo, hi = boxes[bone]
        ob = piece(asset)
        fit(ob, lo, hi, thickness)
        ob.vertex_groups.new(name=bone).add(range(len(ob.data.vertices)), 1.0, "REPLACE")
        parts.append(ob)
    c = core(*boxes["Body"], core_frac)
    c.vertex_groups.new(name="Body").add(range(len(c.data.vertices)), 1.0, "REPLACE")
    parts.append(c)
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    keeper = bpy.context.view_layer.objects.active
    keeper.name = keeper.data.name = asset_id
    # No image larger than MAX_TEXTURE on a side: a keeper is seen at combat range, and each of its parts brings its own maps.
    for img in bpy.data.images:
        if img.size[0] > MAX_TEXTURE:
            img.scale(MAX_TEXTURE, int(img.size[1] * MAX_TEXTURE / img.size[0]))
    # The Red Echo trim is a warm brown that reads as timber on a moving body; the keepers are the Spill's dark iron, so their base
    # colour is pulled toward a cool, dark grey (the luminance kept, most of the hue taken out).
    colour_maps = [img for img in bpy.data.images if "_basecolor" in img.name]   # a re-imported piece names its copy _basecolor.001
    for img in colour_maps:
        px = np.empty(len(img.pixels), dtype=np.float32)
        img.pixels.foreach_get(px)
        rgba = px.reshape(-1, 4)
        lum = rgba[:, :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
        rgba[:, :3] = (lum[:, None] + (rgba[:, :3] - lum[:, None]) * 0.25) * 0.62 * np.array([0.92, 0.94, 1.05], dtype=np.float32)
        img.pixels.foreach_set(rgba.ravel())
        img.update()
        img.pack()   # the export takes the packed bytes, so the edited pixels are packed back in
    keeper.parent = rig
    mod = keeper.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    # The keeper's height, baked into the skeleton's rest (the clips retarget relative to rest; travel scales with height).
    s = height / GOLEM_HEIGHT
    rig.scale = (s, s, s)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    keeper.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    rig.name = asset_id + "_rig"
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    folder = os.path.join(out, "rigged", asset_id)
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, asset_id + "_rigged.glb")
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    keeper.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_animations=False,
                              export_image_format="JPEG", export_jpeg_quality=92, export_yup=True)
    dims = keeper.dimensions
    print(f"KEEPER {asset_id} {path} {dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f} m, {len(keeper.data.polygons)} faces")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[argv.index("--out") + 1] if "--out" in argv else r"D:\Otherreach_Depot\field_ruins"
    global DEPOT, MAX_TEXTURE
    if "--depot" in argv:
        DEPOT = argv[argv.index("--depot") + 1]
    if "--max-texture" in argv:
        MAX_TEXTURE = int(argv[argv.index("--max-texture") + 1])
    for asset_id, (height, thickness, core_frac) in KEEPERS.items():
        build(asset_id, height, thickness, core_frac, out)


main()
