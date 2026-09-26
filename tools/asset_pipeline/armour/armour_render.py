"""Shared Blender render helpers for the animated-armour v2 sheets (run inside Blender).

Views are named by where the camera stands relative to the figure (the figure faces -Y in Blender, +Z in glTF).
"""
import math
import bpy
from mathutils import Vector

VIEWS = {
    # name: (azimuth deg measured from -Y toward +X, elevation deg)
    "front": (0, 5),
    "left": (90, 5),        # camera on the figure's left (+X)
    "back": (180, 5),
    "right": (-90, 5),
    "three_quarter": (35, 12),   # front-left, the concept's side
    "concept": (28, 10),
    "top": (0, 89),
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        if o.type == "MESH" and o.name.startswith("Icosphere") and not o.users_collection[0].name.startswith("Scene"):
            pass
    # the importer's bone-shape helper
    for o in list(new):
        if o.type == "MESH" and o.name.startswith("Icosphere"):
            bpy.data.objects.remove(o, do_unlink=True)
            new.remove(o)
    return new


def scene_setup(res=(640, 900), engine="BLENDER_EEVEE", bg=(0.62, 0.62, 0.64), samples=32):
    sc = bpy.context.scene
    sc.render.engine = engine
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    if engine == "BLENDER_EEVEE":
        try:
            sc.eevee.taa_render_samples = samples
        except Exception:
            pass
    else:
        sc.cycles.samples = samples
        sc.cycles.device = "GPU"
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "AgX - Base Contrast" if "AgX - Base Contrast" in [i.identifier for i in sc.view_settings.bl_rna.properties["look"].enum_items] else "None"
    world = bpy.data.worlds.new("W")
    sc.world = world
    world.use_nodes = True
    nt = world.node_tree
    bgn = nt.nodes.get("Background")
    bgn.inputs[0].default_value = (*bg, 1)
    bgn.inputs[1].default_value = 0.6
    # lighting and reflections from Blender's bundled courtyard HDRI; the camera sees a plain grey backdrop
    import os
    hdr = os.path.join(os.path.dirname(bpy.app.binary_path), "%d.%d" % bpy.app.version[:2], "datafiles", "studiolights", "world", "courtyard.exr")
    if os.path.exists(hdr):
        env = nt.nodes.new("ShaderNodeTexEnvironment")
        env.image = bpy.data.images.load(hdr)
        benv = nt.nodes.new("ShaderNodeBackground")
        benv.inputs[1].default_value = 0.9
        nt.links.new(env.outputs["Color"], benv.inputs["Color"])
        lp = nt.nodes.new("ShaderNodeLightPath")
        mixs = nt.nodes.new("ShaderNodeMixShader")
        nt.links.new(lp.outputs["Is Camera Ray"], mixs.inputs[0])
        nt.links.new(benv.outputs[0], mixs.inputs[1])
        nt.links.new(bgn.outputs[0], mixs.inputs[2])
        out = nt.nodes.get("World Output")
        nt.links.new(mixs.outputs[0], out.inputs["Surface"])
    # key / fill / rim
    def sun(name, rot, energy, color=(1, 1, 1)):
        l = bpy.data.lights.new(name, "SUN")
        l.energy = energy
        l.color = color
        l.angle = math.radians(3)
        o = bpy.data.objects.new(name, l)
        o.rotation_euler = [math.radians(a) for a in rot]
        sc.collection.objects.link(o)
        return o
    sun("key", (50, 0, 35), 2.6, (1.0, 0.97, 0.92))
    sun("fill", (70, 0, -60), 0.6, (0.85, 0.9, 1.0))
    sun("rim", (60, 0, 180), 1.6)
    # ground
    bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
    g = bpy.context.object
    g.name = "ground"
    m = bpy.data.materials.new("groundmat")
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (0.35, 0.35, 0.36, 1)
    p.inputs["Roughness"].default_value = 0.9
    g.data.materials.append(m)
    return sc


def bounds(objs):
    pts = []
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if o.type != "MESH":
            continue
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        pts += [e.matrix_world @ v.co for v in me.vertices]
        e.to_mesh_clear()
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


def camera(view, center, height, lens=50, dist_scale=1.0, sensor=36):
    az, el = VIEWS[view] if isinstance(view, str) else view
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = lens
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = sensor
    cam = bpy.data.objects.new("cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    sc = bpy.context.scene
    # vertical fov fits the height with margin
    fov_v = 2 * math.atan(sensor / (2 * lens))
    d = (height * 1.12 / 2) / math.tan(fov_v / 2) * dist_scale
    a, e = math.radians(az), math.radians(el)
    direction = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = center + direction * d
    look = (center - cam.location).normalized()
    cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    return cam


def render_to(path):
    bpy.context.scene.render.filepath = path
    bpy.context.scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)


def clay_material(wire=True, color=(0.55, 0.55, 0.56)):
    m = bpy.data.materials.new("clay_wire" if wire else "clay")
    m.use_nodes = True
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = 0.6
    if wire:
        w = nt.nodes.new("ShaderNodeWireframe")
        w.use_pixel_size = True
        w.inputs[0].default_value = 0.8
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.inputs["A"].default_value = (*color, 1)
        mix.inputs["B"].default_value = (0.05, 0.05, 0.08, 1)
        nt.links.new(w.outputs[0], mix.inputs["Factor"])
        nt.links.new(mix.outputs["Result"], p.inputs["Base Color"])
    return m
