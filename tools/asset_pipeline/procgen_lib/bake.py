"""Cycles bake harness: material recipes baked into one atlas as base colour (sRGB), tangent normal (OpenGL +Y) and
ORM (R occlusion, G roughness, B metallic).

Extracted from _procgen_crate.py / _procgen_chest.py / _procgen_cart.py (set_device byte-identical in all three,
bake_material, run_bake with GPU retry and CPU fallback, bake_textures with a temporary ground plane and lifted
parts, to_srgb, save_png) and the world baker's GPU-failure fallback. Bakes are seeded (scene.cycles.seed), use
fixed samples and no denoiser, so a rerun with the same seed, samples and device reproduces the maps.

API
    set_device(scene, device='auto'|'cpu') -> 'OPTIX'|'CUDA'|'CPU'
    setup_cycles(scene, samples, seed=0, device='auto') -> device
    run_bake(kind, devices_used, tries=3, margin=16)          one pass; the GPU is shared, so retry then use the CPU
    bake_material(name, material, image, ao_distance, bevel_radius) -> (bpy material, sockets)
    lifted(obj, lifts)            context manager: groups raised by dz during the bake (occlusion of open boxes)
    ground_plane(size)            context manager: a temporary plane at z = 0 so occlusion includes the ground
    bake_maps(obj, materials, res, samples, device, work_dir, stem, seed=0, ground=True, lifts=None,
              ao_distance=0.35, bevel_radius=0.004) -> (paths, stats)
        materials: [Material] indexed by the mesh's material_index; writes <stem>_basecolor.jpg (q90),
        <stem>_orm.jpg (q90) and <stem>_normal.png in work_dir
    to_srgb(x), save_image(arr, path, colourspace, fmt='PNG'|'JPEG', quality=90)
"""
import contextlib
import os
import time

import numpy as np

from .shadergraph import Graph, surface_context


def set_device(scene, device="auto"):
    import bpy
    scene.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    if device != "cpu":
        for dt in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = dt
                prefs.get_devices()
                if any(d.type == dt for d in prefs.devices):
                    for d in prefs.devices:
                        d.use = d.type == dt
                    scene.cycles.device = "GPU"
                    return dt
            except TypeError:
                continue
    prefs.compute_device_type = "NONE"
    scene.cycles.device = "CPU"
    return "CPU"


def setup_cycles(scene, samples, seed=0, device="auto"):
    dev = set_device(scene, device)
    scene.cycles.samples = samples
    scene.cycles.use_adaptive_sampling = False
    scene.cycles.use_denoising = False
    scene.cycles.seed = seed
    scene.render.bake.use_selected_to_active = False
    return dev


def run_bake(kind, devices_used, tries=3, margin=16):
    """Bake one pass into the active image nodes; the GPU is shared with other jobs, so retry a failed GPU bake, then
    fall back to the CPU (same shader, only slower; the device is recorded)."""
    import bpy
    scene = bpy.context.scene

    def go():
        if kind == "normal":
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y",
                                normal_b="POS_Z", margin=margin, margin_type="EXTEND", use_clear=True,
                                target="IMAGE_TEXTURES", uv_layer="UVMap")
        else:
            bpy.ops.object.bake(type="EMIT", margin=margin, margin_type="EXTEND", use_clear=True,
                                target="IMAGE_TEXTURES", uv_layer="UVMap")
    for attempt in range(tries):
        try:
            go()
            return
        except RuntimeError as err:
            if scene.cycles.device != "GPU":
                if attempt == tries - 1:
                    raise
                print(f"  CPU bake failed ({str(err)[:120]}); retrying", flush=True)
                time.sleep(5)
                continue
            print(f"  GPU bake failed ({str(err)[:120]}), attempt {attempt + 1}/{tries}", flush=True)
            time.sleep(5 * (attempt + 1))
    print("  falling back to the CPU", flush=True)
    set_device(scene, "cpu")
    devices_used.add("CPU")
    go()


def bake_material(name, material, image, ao_distance=0.35, bevel_radius=0.004):
    """A bake-only material: the recipe's base colour and ORM as emission, its height through a Bump node on a
    diffuse BSDF for the normal pass, and the target image node active."""
    import bpy
    mat = bpy.data.materials.new(name)
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    g = Graph(tree)
    c = surface_context(g, ao_distance, bevel_radius)
    res = material.build(g, c)
    out = g.node("ShaderNodeOutputMaterial")
    e_col = g.node("ShaderNodeEmission")
    g.put(e_col.inputs["Color"], res["base"])
    g.put(e_col.inputs["Strength"], 1.0)
    e_orm = g.node("ShaderNodeEmission")
    g.put(e_orm.inputs["Color"], g.vec(g.pow(c.ao, 1.15), g.sat(res["rough"]), g.sat(res.get("metal", 0.0))))
    g.put(e_orm.inputs["Strength"], 1.0)
    bump = g.node("ShaderNodeBump")
    g.put(bump.inputs["Strength"], 1.0)
    g.put(bump.inputs["Distance"], 1.0)
    g.put(bump.inputs["Filter Width"], 1.0)
    g.put(bump.inputs["Height"], res["height"])
    diffuse = g.node("ShaderNodeBsdfDiffuse")
    tree.links.new(bump.outputs["Normal"], diffuse.inputs["Normal"])
    img = g.node("ShaderNodeTexImage")
    img.image = image
    tree.nodes.active = img
    return mat, {"out": out, "colour": e_col.outputs[0], "orm": e_orm.outputs[0], "normal": diffuse.outputs[0],
                 "tree": tree, "nodes": g.count}


@contextlib.contextmanager
def ground_plane(size=8.0):
    import bpy
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, 0))
    plane = bpy.context.active_object
    plane.name = "_ao_ground"
    try:
        yield plane
    finally:
        bpy.data.objects.remove(plane, do_unlink=True)


@contextlib.contextmanager
def lifted(obj, lifts):
    """lifts: {group index: dz}. Raises those groups' vertices (and records pzoff so the shaders' height above the
    ground stays true), restores them afterwards."""
    from .meshbuild import group_vertex_mask
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    moved = co.copy()
    zoff = np.zeros(len(me.polygons), np.float32)
    for gi, dz in (lifts or {}).items():
        mask, fg = group_vertex_mask(me, gi)
        moved[mask, 2] += dz
        zoff[fg == gi] = dz
    me.vertices.foreach_set("co", moved.ravel())
    me.attributes["pzoff"].data.foreach_set("value", zoff)
    me.update()
    try:
        yield
    finally:
        me.vertices.foreach_set("co", co.ravel())
        me.attributes["pzoff"].data.foreach_set("value", np.zeros(len(me.polygons), np.float32))
        me.update()


def to_srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def save_image(arr, path, colourspace, fmt="PNG", quality=90):
    """Save an (H, W, 3) array of stored 0..1 values (rows bottom-up, as Blender keeps them)."""
    import bpy
    h, w = arr.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    img.colorspace_settings.name = colourspace
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = np.clip(arr, 0.0, 1.0)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = fmt
    img.save(filepath=path, quality=quality)      # stored values as they are: no view transform
    bpy.data.images.remove(img)


def bake_maps(obj, materials, res, samples, device, work_dir, stem, seed=0, ground=True, lifts=None,
              ao_distance=0.35, bevel_radius=0.004, formats=None):
    """Bake every material slot of obj into one res x res atlas (the UVs are already packed). Returns the three map
    paths and stats (seconds per pass, devices, coverage, decoded normal z)."""
    import bpy
    from .meshbuild import select_only
    formats = formats or {"basecolor": "JPEG", "orm": "JPEG", "normal": "PNG"}
    scene = bpy.context.scene
    dev = setup_cycles(scene, samples, seed, device)
    devices_used = {dev}
    image = bpy.data.images.new("bake_atlas", res, res, alpha=False, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"
    mats, shaders = [], []
    for i, m in enumerate(materials):
        mat, sh = bake_material(f"bake_{i}_{m.name}", m, image, ao_distance, bevel_radius)
        mats.append(mat)
        shaders.append(sh)
    me = obj.data
    keep = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", keep)
    me.materials.clear()                 # clearing the slots resets every face's index, so restore it
    for mat in mats:
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", keep)
    me.update()
    out, timings = {}, {}
    with contextlib.ExitStack() as stack:
        if ground:
            stack.enter_context(ground_plane())
        stack.enter_context(lifted(obj, lifts))
        select_only(obj)
        for pass_ in ("colour", "orm", "normal"):
            for sh in shaders:
                tree = sh["tree"]
                for link in list(sh["out"].inputs["Surface"].links):
                    tree.links.remove(link)
                tree.links.new(sh[pass_], sh["out"].inputs["Surface"])
            t0 = time.time()
            run_bake(pass_, devices_used)
            timings[pass_] = round(time.time() - t0, 1)
            px = np.empty(res * res * 4, dtype=np.float32)
            image.pixels.foreach_get(px)
            out[pass_] = px.reshape(res, res, 4)[:, :, :3].copy()
            print(f"  baked {pass_} in {timings[pass_]} s", flush=True)
    col, orm, nrm = out["colour"], out["orm"], out["normal"]
    mask = (col.sum(axis=2) > 1e-6) | (orm.sum(axis=2) > 1e-6)   # texels the emit bakes wrote (islands + margin)
    for arr, fill in ((col, None), (orm, None), (nrm, (0.5, 0.5, 1.0))):
        arr[~mask] = fill if fill is not None else arr[mask].mean(axis=0)
    ext = {"PNG": ".png", "JPEG": ".jpg"}
    paths = {k: os.path.join(work_dir, f"{stem}_{k}{ext[formats[k]]}") for k in ("basecolor", "orm", "normal")}
    save_image(to_srgb(col), paths["basecolor"], "sRGB", formats["basecolor"])
    save_image(orm, paths["orm"], "Non-Color", formats["orm"])
    save_image(nrm, paths["normal"], "Non-Color", formats["normal"])
    bpy.data.images.remove(image)
    for mat in mats:
        bpy.data.materials.remove(mat)
    stats = {"samples": samples, "seed": seed, "devices": sorted(devices_used), "seconds": timings,
             "coverage": round(float(mask.mean()), 4), "ao_distance_m": ao_distance,
             "normal_mean_z_decoded": round(float((nrm[mask][:, 2] * 2 - 1).mean()), 4),
             "normal_min_z_decoded": round(float((nrm[mask][:, 2] * 2 - 1).min()), 4),
             "shader_nodes": {m.name: sh["nodes"] for m, sh in zip(materials, shaders)}}
    return paths, stats
