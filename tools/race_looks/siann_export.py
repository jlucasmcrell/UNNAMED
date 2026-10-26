"""Siann bodies with long platinum hair: Claude's copy of DeepSeek's race_bodies_production.py (same recipe), adding a hair
lengthening pass and a hair colour, for the Siann look (owner ruling 2026-10-25).

Production bodies: the six approved race bodies and the three canon-people NPCs, through the cast's own recipe.

Same recipe as the approved 16-body cast: paid HumGen presets, the body/face slider values frozen from the approved round-2
spec, FACS expressions loaded, catalogue hair, an accepted-catalogue outfit worn through the wear API, and the Godot export
recipe with its four LODs (the export carries the same 59-joint skeleton the cast exported). Differences from the cast, both
required by the brief: the outfit goes on through the wear API rather than a loose import, and each body gets a per-vertex
follow check plus front/side/walk stills before the export.

The three named NPCs keep their own preset, age, skin and existing hair so their identity survives the change of people; only
the body proportions and a half-strength version of the people's face direction are applied, and their clothes are job
outfits recoloured to frontier tones through the clothing material's own colour control.

usage: blender -b --python race_bodies_production.py -- <spec json> <out dir> [only-ids]
"""
import json
import math
import dataclasses
import os
import random
import sys
import time
import traceback
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

from HumGen3D.human.human import Human
from HumGen3D.human.hair import haircards
from HumGen3D.human.process.settings import ExportSettings, QualitySettings

args = sys.argv[sys.argv.index("--") + 1:]
SPEC = json.loads(Path(args[0]).read_text(encoding="utf-8"))
OUT = Path(os.path.abspath(args[1]))
ONLY = args[2].split(",") if len(args) > 2 and args[2] else None
# --no-renders: export without the three review stills. The export is CPU work; the stills need the GPU lease, so this
# lets the export path be proven (and cast_verify run) while the GPU queue is held by other agents.
RENDERS = "--no-renders" not in args
# --no-export: stills only, for a short GPU lease after the CPU export has run.
EXPORT = "--no-export" not in args
# --render-cpu: render the stills with Cycles on the CPU. EEVEE needs the GPU lease; when the queue is held by other agents for
# hours, a CPU render still gets the review pictures made without touching the GPU.
CYCLES = "--render-cpu" in args
(OUT / "renders").mkdir(parents=True, exist_ok=True)
(OUT / "records").mkdir(parents=True, exist_ok=True)

BODIES = [body for body in SPEC["bodies"] if ONLY is None or body["id"] in ONLY]
LODS = [
    QualitySettings(body="original", clothing="high", eyes="high", teeth="medium", haircards="ultra"),
    QualitySettings(body="lower_face", clothing="medium", eyes="medium", teeth="medium", haircards="medium"),
    QualitySettings(body="quarter", clothing="low", eyes="low", teeth="low", haircards="low"),
    QualitySettings(body="quarter", clothing="low", eyes="low", teeth="low", haircards="haircap_only"),
]
FRONT_ORTHO, CENTRE_Z = 2.15, 1.00
ARM_X, MID, HEAD = 0.30, 0.45, 0.85


def world_points(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(deps)
    mesh = evaluated.to_mesh()
    count = len(mesh.vertices)
    coordinates = np.empty(count * 3, dtype=np.float64)
    mesh.vertices.foreach_get("co", coordinates)
    matrix = np.array(evaluated.matrix_world, dtype=np.float64)
    points = coordinates.reshape(count, 3) @ matrix[:3, :3].T + matrix[:3, 3]
    evaluated.to_mesh_clear()
    return points.astype(np.float32)


def nearest_index(source, target, chunk=256):
    index = np.empty(len(source), dtype=np.int64)
    for start in range(0, len(source), chunk):
        block = source[start:start + chunk]
        square = ((block[:, None, :] - target[None, :, :]) ** 2).sum(axis=2)
        index[start:start + chunk] = square.argmin(axis=1)
    return index


def pose(rig, rotations):
    for bone in rig.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()
    for name, degrees in rotations.items():
        bone = rig.pose.bones.get(name)
        if bone is not None:
            bone.rotation_euler = (math.radians(degrees), 0.0, 0.0)
    bpy.context.view_layer.update()


def follow(rig, body, pieces):
    """Region ratio and per-vertex correspondence on the evaluated mesh, masks off (they cut the skin under cloth)."""
    for modifier in body.modifiers:
        if modifier.type == "MASK":
            modifier.show_viewport = False
            modifier.show_render = False
    bpy.context.view_layer.update()
    body_rest = world_points(body)
    body_min, body_max = float(body_rest[:, 2].min()), float(body_rest[:, 2].max())
    height = body_max - body_min
    centre_x = float((body_rest[:, 0].min() + body_rest[:, 0].max()) / 2)
    regions = {"arm": [i for i, p in enumerate(body_rest) if abs(p[0] - centre_x) > ARM_X],
               "torso": [i for i, p in enumerate(body_rest) if body_min + height * MID <= p[2] <= body_min + height * HEAD],
               "leg": [i for i, p in enumerate(body_rest) if p[2] < body_min + height * MID],
               "head": [i for i, p in enumerate(body_rest) if p[2] > body_min + height * HEAD]}
    rest = {obj.name: world_points(obj) for obj in pieces}
    samples = {obj.name: np.linspace(0, len(rest[obj.name]) - 1,
                                     min(3000, len(rest[obj.name]))).astype(int) for obj in pieces}
    neighbours = {obj.name: nearest_index(rest[obj.name][samples[obj.name]], body_rest) for obj in pieces}
    out = {}
    for name, rotations, region in (("arm", {"upper_arm.R": 60.0}, "arm"),
                                    ("torso", {"spine.003": 25.0}, "torso"),
                                    ("leg", {"thigh.L": 30.0}, "leg"),
                                    ("head", {"head": 30.0}, "head")):
        pose(rig, rotations)
        body_now = world_points(body)
        body_move = np.linalg.norm(body_now - body_rest, axis=1)
        body_region = float(body_move[regions[region]].mean()) if regions[region] else 0.0
        entry = {}
        for obj in pieces:
            now = world_points(obj)
            index = samples[obj.name]
            points = rest[obj.name][index]
            move = np.linalg.norm(now[index] - points, axis=1)
            if region == "arm":
                selection = [i for i, p in enumerate(points) if abs(p[0] - centre_x) > ARM_X]
            elif region == "torso":
                selection = [i for i, p in enumerate(points)
                             if body_min + height * MID <= p[2] <= body_min + height * HEAD]
            elif region == "leg":
                selection = [i for i, p in enumerate(points) if p[2] < body_min + height * MID]
            else:
                selection = [i for i, p in enumerate(points) if p[2] > body_min + height * HEAD]
            garment_region = float(move[selection].mean()) if selection else None
            underlying = body_move[neighbours[obj.name]]
            usable = underlying > 0.005
            ratio = move[usable] / underlying[usable]
            entry[obj.name] = {"verts_region": len(selection),
                               "region_ratio": round(garment_region / body_region, 3)
                               if garment_region is not None and body_region > 1e-6 else None,
                               "correspondence_median": round(float(np.median(ratio)), 3) if usable.any() else None}
        out[region] = entry
    pose(rig, {})
    return out


def decisive(follow_report, pieces):
    out = {}
    for obj in pieces:
        measured = [(region, entry[obj.name]) for region, entry in follow_report.items()
                    if entry.get(obj.name) and entry[obj.name].get("correspondence_median") is not None]
        if measured:
            basis, entry = max(measured, key=lambda pair: pair[1]["verts_region"])
            value = entry["correspondence_median"]
        else:
            basis, entry = ("none", {})
            value = None
        out[obj.name] = {"basis": basis, "region_ratio": entry.get("region_ratio"), "correspondence_median": value}
    return out


def stage():
    scene = bpy.context.scene
    if scene.world is None:
        scene.world = bpy.data.worlds.new("ProdWorld")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs[0].default_value = (0.52, 0.54, 0.57, 1.0)
        background.inputs[1].default_value = 1.3
    for name, kind, energy, rotation, location in (
            ("ProdKey", "SUN", 3.2, (math.radians(55.0), 0.0, math.radians(35.0)), None),
            ("ProdFill", "AREA", 200.0, (math.radians(70.0), 0.0, math.radians(40.0)), (1.6, -2.0, 1.7))):
        if name in bpy.data.objects:
            continue
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, kind))
        light.data.energy = energy
        light.rotation_euler = rotation
        if location:
            light.location = location
            light.data.size = 3.0
        scene.collection.objects.link(light)
    scene.render.engine = "CYCLES" if CYCLES else "BLENDER_EEVEE"
    if CYCLES:
        scene.cycles.device = "CPU"
        scene.cycles.samples = 48
        scene.cycles.use_denoising = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    camera = bpy.data.objects.get("ProdCam") or bpy.data.objects.new("ProdCam", bpy.data.cameras.new("ProdCam"))
    if camera.name not in scene.objects:
        scene.collection.objects.link(camera)
    scene.camera = camera
    return camera


def shoot(camera, path, resolution, ortho=None, location=None, rotation=None):
    scene = bpy.context.scene
    scene.render.resolution_x, scene.render.resolution_y = resolution
    if ortho is not None:
        camera.data.type = "ORTHO"
        camera.data.ortho_scale = ortho
    if location is not None:
        camera.location = location
    if rotation is not None:
        camera.rotation_euler = rotation
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def pose_walk(rig):
    for name, degrees in (("thigh.L", 25.0), ("thigh.R", -25.0), ("calf.L", -18.0), ("calf.R", 10.0),
                          ("upper_arm.L", -18.0), ("upper_arm.R", 18.0), ("forearm.L", -12.0), ("forearm.R", -12.0)):
        bone = rig.pose.bones.get(name)
        if bone is not None:
            bone.rotation_mode = "XYZ"
            bone.rotation_euler = (math.radians(degrees), 0.0, 0.0)
    bpy.context.view_layer.update()


def set_iris(body, rgb):
    material = None
    for obj in bpy.data.objects:
        if obj.type != "MESH" or "eye" not in obj.name.lower():
            continue
        for slot in obj.material_slots:
            candidate = slot.material
            if candidate and candidate.node_tree and "HG_Eye_Color" in candidate.node_tree.nodes:
                material = candidate
                break
        if material is not None:
            break
    if material is None:
        return "no eye material"
    mix = material.node_tree.nodes["HG_Eye_Color"]
    socket = next((socket for socket in mix.inputs if socket.name == "B" and socket.type == "RGBA"), None)
    if socket is None:
        return "no colour socket"
    socket.default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    return f"{material.name}: iris {rgb}"


def recolour_objects(objects, colour):
    """Recolour garments through the clothing colour control, falling back to the material's base colour.

    Footwear in the catalogue does not always carry HG_Control, so a boot colour that only tried the control socket would
    silently do nothing - the same class of failure as the look shader that reported success while doing nothing.
    """
    touched = []
    for obj in objects:
        if obj.type != "MESH":
            continue
        for slot in obj.material_slots:
            material = slot.material
            if material is None or material.node_tree is None:
                continue
            applied = False
            control = material.node_tree.nodes.get("HG_Control")
            if control is not None:
                for socket in control.inputs:
                    if socket.name.startswith("Main Color"):
                        socket.default_value = (*colour, 1.0)
                        touched.append(f"{material.name}.{socket.name}")
                        applied = True
            if applied:
                continue
            principled = next((node for node in material.node_tree.nodes
                               if node.bl_idname == "ShaderNodeBsdfPrincipled"), None)
            if principled is not None and not principled.inputs["Base Color"].is_linked:
                principled.inputs["Base Color"].default_value = (*colour, 1.0)
                touched.append(f"{material.name}.BaseColor")
    return touched

def neutralise_buttons(objects):
    """Warm-neutral every garment button.

    The catalogue shirts keep a separate `Buttons` colour input that the main-colour recolour does not touch, so the buttons
    stay cyan - the same defect Codex found on the pool shirts. Codex used #6D6455; that is the value set here.
    """
    touched = []
    for obj in objects:
        if obj.type != "MESH":
            continue
        for slot in obj.material_slots:
            material = slot.material
            if material is None or material.node_tree is None:
                continue
            control = material.node_tree.nodes.get("HG_Control")
            if control is None:
                continue
            for socket in control.inputs:
                if "button" in socket.name.lower() and socket.type == "RGBA":
                    socket.default_value = (0.150, 0.122, 0.086, 1.0)
                    touched.append(f"{material.name}.{socket.name}")
    return touched


def recolour_matching(objects, pieces):
    """The frontier set's recolour (frontier_bodies.py): each piece by name, through its colour group or its BSDF."""
    done = {}
    for piece in pieces:
        rgb = tuple(c ** 2.2 for c in piece["rgb"])
        touched = []
        for obj in [o for o in objects if o.type == "MESH" and piece["match"] in o.name.lower()]:
            for slot in obj.material_slots:
                material = slot.material
                if material is None or not material.use_nodes:
                    continue
                socket = next((node.inputs.get(name) for node in material.node_tree.nodes if node.type == "GROUP" and node.node_tree
                               for name in ("Main Color_C0", "Main Color") if node.inputs.get(name) is not None
                               and node.inputs.get(name).type == "RGBA"), None)
                if socket is not None:
                    socket.default_value = (*rgb, 1.0)
                    touched.append(material.name)
                elif (principled := next((n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)) is not None:
                    principled.inputs["Base Color"].default_value = (*rgb, 1.0)
                    touched.append(material.name)
        done[piece["name"]] = touched
    return done


def recolour_pieces(objects, mapping):
    """Colour garment pieces separately so a top and a bottom cannot read as one suit.

    Pieces are classified by where they sit: the piece reaching highest is the top (shirt), the lower pieces are the bottom
    (trousers or skirt). Claude's note on Kera was that a shirt and trousers in the same colour read as a onesie, so the
    fix has to be per piece, not per outfit.
    """
    pieces = [obj for obj in objects if obj.type == "MESH" and obj.material_slots]
    top = None
    if pieces:
        reach = {obj.name: max((obj.matrix_world @ vertex.co).z for vertex in obj.data.vertices) for obj in pieces}
        top = max(reach, key=reach.get)
    touched = []
    for obj in pieces:
        role = "shirt" if obj.name == top else "trousers"
        colour = mapping.get(role)
        if colour is None:
            continue
        for slot in obj.material_slots:
            material = slot.material
            if material is None or material.node_tree is None:
                continue
            control = material.node_tree.nodes.get("HG_Control")
            if control is None:
                continue
            for socket in control.inputs:
                if socket.name.startswith("Main Color"):
                    socket.default_value = (*colour, 1.0)
                    touched.append(f"{role}:{obj.name}.{socket.name}")
    return touched


def extend_hair(human, end_fraction, seed=7):
    """Lengthen the head hair's guide strands so they fall straight past the waist (the Siann canon).

    Everything is in the body object's space, as Blender's own hair-key accessors use it (co_object / co_object_set),
    with the torso cross-sections taken from the evaluated mesh in the same space and from torso-weighted vertices only,
    so the T-posed arms do not count. Keys around the head keep their groom; from the chin down each strand's keys are
    laid along a near-vertical fall to end_fraction of the body's height, pushed out of the torso along the strand's own
    side so the hair drapes over the shoulders in front and behind. Children follow their guides, and HumGen's own
    export turns the result into hair cards.
    """
    rng = random.Random(seed)
    body = human.objects.body
    deps = bpy.context.evaluated_depsgraph_get()
    evaluated = body.evaluated_get(deps)
    mesh = evaluated.to_mesh()
    torso_groups = {g.index for g in body.vertex_groups
                    if any(t in g.name.lower() for t in ("spine", "chest", "hips", "pelvis", "neck", "breast", "shoulder", "clavicle"))}
    head_groups = {g.index for g in body.vertex_groups if g.name.lower() in ("head", "spine.006")}
    zs = [v.co.z for v in mesh.vertices]
    floor, top = min(zs), max(zs)
    end_z = floor + (top - floor) * end_fraction
    bins, head_low = {}, top
    for v in mesh.vertices:
        if not v.groups:
            continue
        best = max(v.groups, key=lambda g: g.weight)
        if best.group in head_groups and best.weight > .9:
            head_low = min(head_low, v.co.z)
        if best.group not in torso_groups:
            continue
        key = int(v.co.z / .02)
        x0, y0, y1 = bins.get(key, (0.0, 1e9, -1e9))
        bins[key] = (max(x0, abs(v.co.x)), min(y0, v.co.y), max(y1, v.co.y))
    chin_z = head_low + .02
    face = [v.co for v in mesh.vertices if v.groups and max(v.groups, key=lambda g: g.weight).group in head_groups and chin_z < v.co.z < chin_z + .12]
    head_cy = sum(c.y for c in face) / max(len(face), 1)
    face_half = max((abs(c.x) for c in face if c.y < head_cy), default=.07) + .035

    def outside(x, y, z):
        b = bins.get(int(z / .02))
        if b is None:
            return x, y
        rx, cy, ry = b[0] + .02, (b[1] + b[2]) / 2, (b[2] - b[1]) / 2 + .02
        dx, dy = x / rx, (y - cy) / ry
        r = (dx * dx + dy * dy) ** .5
        if r >= 1 or r < 1e-6:
            return x, y
        return x / r, cy + (y - cy) / r

    moved = 0
    for mod in [m for m in body.modifiers if m.type == "PARTICLE_SYSTEM"]:
        psys = mod.particle_system
        if psys.settings.type != "HAIR" or "eye" in psys.name.lower():
            continue
        # The keys are the original object's (writes to the evaluated copy are lost on the next evaluation), but the hair
        # matrix comes from the evaluated object and modifier: the original modifier has no mesh in a background session.
        emod = evaluated.modifiers[mod.name]
        for particle in psys.particles:
            keys = [k.co_object(evaluated, emod, particle) for k in particle.hair_keys]
            # Only the strands that already reach the jaw grow on; the crown keeps its own layer instead of curtaining the face.
            if min(k.z for k in keys) > chin_z + .06:
                continue
            m = next((j for j, k in enumerate(keys) if k.z < chin_z), len(keys))
            m = max(2, min(m, len(keys) - 2))
            start = keys[m - 1]
            end = end_z + rng.uniform(-.04, .04) * (top - floor)
            n = len(keys) - m
            x, y = start.x * 1.05, start.y
            if y < head_cy and abs(x) < face_half:
                x = math.copysign(face_half, x if abs(x) > 1e-4 else keys[0].x or 1.0)
            # strands from the front of the scalp fall in front of the shoulders, framing the face as the canon shows
            if keys[0].y < head_cy - .02:
                y = min(y, head_cy - .05)
            for j in range(1, n + 1):
                z = start.z - (start.z - end) * j / n
                x, y = outside(x, y, z)
                particle.hair_keys[m - 1 + j].co_object_set(evaluated, emod, particle, Vector((x + rng.uniform(-.004, .004), y, z)))
            moved += 1
    evaluated.to_mesh_clear()
    bpy.context.view_layer.update()
    return {"strands": moved, "end_z": round(end_z, 3), "chin_z": round(chin_z, 3), "height": round(top - floor, 3)}


def sleek_hair(body, sleek):
    """Smooth, dense sheets instead of ropes for the lengthened scalp hair.

    The groom's children sit within 1 cm of their guide, which fills the gaps at shoulder length; at the waist the guides
    have fanned out and each one hangs as its own rope. A slightly wider child radius closes the sheet, more and thicker
    children fill the cards, and no random roughness or clumping keeps it straight and sleek.
    """
    touched = []
    for mod in [m for m in body.modifiers if m.type == "PARTICLE_SYSTEM" and m.show_viewport]:
        psys = mod.particle_system
        settings = psys.settings
        if settings.type != "HAIR" or "eye" in psys.name.lower() or settings.hair_length < .02:
            continue
        settings.child_radius = float(sleek["child_radius"])
        settings.roughness_2 = float(sleek["roughness"])
        settings.roughness_endpoint = 0.0
        settings.roughness_1 = 0.0
        settings.clump_factor = 0.0
        settings.clump_shape = 0.0
        settings.use_clump_curve = False
        settings.rendered_child_count = int(settings.rendered_child_count * float(sleek["children"]))
        # thicker drawn hairs, full to the tip: the card atlas is painted from them, and thin, tapering hairs leave the long
        # cards mostly transparent
        settings.radius_scale *= float(sleek["thickness"])
        settings.tip_radius = max(settings.tip_radius, settings.root_radius * .7)
        settings.child_percent = settings.rendered_child_count
        touched.append(psys.name)
    bpy.context.view_layer.update()
    return touched


def set_hair_colour(human, colour):
    rh = human.hair.regular_hair
    applied = {}
    for name, value in colour.items():
        control = getattr(rh, name)
        control.value = float(value)
        applied[name] = round(float(control.value), 3)
    return applied


def align_teeth(entry):
    """Restore the lower teeth to the closed bite after loading the facial rig.

    Measured on these builds: the upper teeth stay where they belong (1.6121-1.6572 m against eyes at 1.6912-1.7193 m) but
    `load_facial_rig()` drops the LOWER row to 1.5382-1.5655 m - the throat, which is the white denture Claude saw on Tavar.
    The first repair moved both rows up to a target derived from a stale eye measurement, which put them at eye level and
    emptied the mouth during open-mouth expressions (proven in crops/teeth_compare/old_surprise_front.png). This one moves
    only the lower row, and only far enough to restore the closed bite against the untouched upper row, so an open-mouth
    expression still has teeth in it (crops/teeth_compare/new_surprise_front.png).
    """
    upper = bpy.data.objects.get("HG_TeethUpper")
    lower = bpy.data.objects.get("HG_TeethLower")
    if upper is None or lower is None:
        return None

    def evaluated_z(obj):
        deps = bpy.context.evaluated_depsgraph_get()
        deps.update()
        evaluated = obj.evaluated_get(deps)
        mesh = evaluated.to_mesh()
        values = [(evaluated.matrix_world @ vertex.co).z for vertex in mesh.vertices]
        evaluated.to_mesh_clear()
        return min(values), max(values)

    upper_bottom = evaluated_z(upper)[0]
    lower_top = evaluated_z(lower)[1]
    delta = (upper_bottom + 0.030) - lower_top
    moved = []
    if abs(delta) >= 0.005:
        up = lower.matrix_world.inverted().to_3x3() @ Vector((0.0, 0.0, delta))
        keys = lower.data.shape_keys
        if keys is None:
            for vertex in lower.data.vertices:
                vertex.co += up
        else:
            for block in keys.key_blocks:
                for point in block.data:
                    point.co += up
        lower.data.update()
        bpy.context.view_layer.update()
        moved.append(f"HG_TeethLower moved {delta * 1000:+.1f} mm onto the closed bite "
                     f"(upper bottom {upper_bottom:.4f} m)")
    entry["teeth_aligned"] = moved
    entry["teeth_z"] = {"upper": [round(value, 4) for value in evaluated_z(upper)],
                        "lower": [round(value, 4) for value in evaluated_z(lower)]}
    return moved

report = {"outdir": str(OUT), "bodies": [], "minutes": None}
started_all = time.time()
for body_spec in BODIES:
    identifier = body_spec["id"]
    entry = {"id": identifier, "kind": body_spec.get("kind"), "people": body_spec.get("people"),
             "sex": body_spec["sex"], "preset": body_spec["preset"], "frozen": body_spec}
    item_started = time.time()
    try:
        bpy.ops.wm.read_homefile(use_empty=True)
        human = Human.from_preset(*[os.path.join("models", *body_spec["preset"].split("/"))])
        human.name = identifier
        human.height.set(float(body_spec["height_cm"]))
        human.age.set(int(body_spec["age"]))
        human.expression.load_facial_rig()
        bpy.context.view_layer.update()

        applied, missing = {}, []
        for name, value in (body_spec.get("sliders") or {}).items():
            key = next((key for key in human.body.keys if key.name == name), None)
            if key is None:
                missing.append(name)
                continue
            key.value = float(value)
            applied[name] = round(float(key.value), 3)
        face_scale = float(body_spec.get("face_strength", 1.0))
        face_applied = {}
        for name, value in (body_spec.get("face_sliders") or {}).items():
            key = next((key for key in human.face.keys if key.name == name), None)
            if key is None:
                missing.append(name)
                continue
            key.value = float(value) * face_scale
            face_applied[name] = round(float(key.value), 3)
        human.keys.update_human_from_key_change()
        bpy.context.view_layer.update()
        entry["sliders"] = applied
        entry["face_sliders"] = face_applied
        entry["face_strength"] = face_scale
        entry["sliders_missing"] = missing

        if body_spec.get("skin"):
            human.skin.texture.set(body_spec["skin"])
            entry["skin"] = body_spec["skin"]
        if body_spec.get("hair"):
            human.hair.regular_hair.set(body_spec["hair"])
            entry["hair"] = body_spec["hair"]
        if body_spec.get("hair_colour"):
            entry["hair_colour"] = set_hair_colour(human, body_spec["hair_colour"])
        if body_spec["sex"] == "m" and body_spec.get("face_hair"):
            human.hair.face_hair.set(body_spec["face_hair"])
            entry["face_hair"] = body_spec["face_hair"]
        bpy.context.view_layer.update()

        body = human.objects.body
        rig = human.objects.rig
        points = world_points(body)
        entry["stature_m"] = round(float(human.height.meters), 3)
        entry["bbox_height_m"] = round(float(points[:, 2].max() - points[:, 2].min()), 3)
        entry["built"] = {"verts": len(body.data.vertices),
                          "bones": len(rig.data.bones),
                          "shapes": len(body.data.shape_keys.key_blocks) if body.data.shape_keys else 0}

        outfit_path = body_spec["outfit"]
        human.clothing.outfit.set(outfit_path)
        # the frontier set is worn bare-headed (frontier_bodies.py drops the catalogue hat the same way)
        for obj in [o for o in human.clothing.outfit.objects if any(part in o.name.lower() for part in body_spec.get("outfit_drop", []))]:
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.context.view_layer.update()
        entry["outfit"] = outfit_path
        entry["outfit_pieces"] = [obj.name for obj in human.clothing.outfit.objects]
        entry["buttons_neutralised"] = neutralise_buttons(human.clothing.outfit.objects)
        if body_spec.get("outfit_piece_colours"):
            touched = recolour_pieces(human.clothing.outfit.objects, body_spec["outfit_piece_colours"])
            entry["recoloured_pieces"] = touched
            if not touched:
                raise RuntimeError("outfit_piece_colours requested but no piece colour socket was found")
        if body_spec.get("outfit_colour"):
            touched = recolour_objects(human.clothing.outfit.objects, body_spec["outfit_colour"])
            entry["recoloured"] = touched
            # Fail loudly: a requested frontier recolour that found no colour socket means the body would export in the
            # catalogue's own bright colour, which is exactly what the attire ruling forbids.
            if not touched:
                raise RuntimeError("outfit_colour requested but no HG_Control main-colour socket was found on "
                                   + ", ".join(entry["outfit_pieces"]))
        if body_spec.get("footwear"):
            human.clothing.footwear.set(body_spec["footwear"])
            entry["footwear"] = body_spec["footwear"]
            if body_spec.get("footwear_colour"):
                entry["footwear_recoloured"] = recolour_objects(
                    human.clothing.footwear.objects, body_spec["footwear_colour"])
                if not entry["footwear_recoloured"]:
                    raise RuntimeError("footwear_colour requested but no colour socket was found")
        if body_spec.get("recolour"):
            entry["recolour"] = recolour_matching(list(human.clothing.outfit.objects) + list(human.clothing.footwear.objects), body_spec["recolour"])
        # Independent of the outfit: the teeth sit at the throat on these bodies, so they are hidden when misplaced.
        entry["teeth_aligned"] = align_teeth(entry)
        if body_spec.get("eye_colour"):
            eye_result = set_iris(body, body_spec["eye_colour"])
            entry["eye"] = eye_result
            if not eye_result.startswith(".HG_Eyes") and "iris" not in eye_result.split(":")[-1]:
                raise RuntimeError("eye colour requested but not applied: " + eye_result)
        bpy.context.view_layer.update()

        pieces = [obj for obj in human.clothing.outfit.objects if obj.type == "MESH"]
        if body_spec.get("footwear"):
            pieces += [obj for obj in human.clothing.footwear.objects if obj.type == "MESH"]
        follow_report = follow(rig, body, pieces)
        per_piece = decisive(follow_report, pieces)
        entry["follow"] = per_piece
        entry["follow_ratio"] = min((piece["correspondence_median"] if piece["correspondence_median"] is not None
                                     else (piece["region_ratio"] if piece["region_ratio"] is not None else 0.0))
                                    for piece in per_piece.values())

        if body_spec.get("hair_end_fraction"):
            # Last, after the outfit and footwear: HumGen rebuilds the hair systems when the body changes.
            entry["hair_extended"] = extend_hair(human, float(body_spec["hair_end_fraction"]))
            if body_spec.get("hair_sleek"):
                entry["hair_sleek"] = sleek_hair(body, body_spec["hair_sleek"])

        if RENDERS:
            camera = stage()
            stem = OUT / "renders" / identifier
            camera.location = (0.0, -4.0, CENTRE_Z)
            camera.rotation_euler = (math.radians(90.0), 0.0, 0.0)
            shoot(camera, f"{stem}_front.png", (640, 1000), ortho=FRONT_ORTHO,
                  location=(0.0, -4.0, CENTRE_Z), rotation=(math.radians(90.0), 0.0, 0.0))
            shoot(camera, f"{stem}_side.png", (640, 1000), ortho=FRONT_ORTHO,
                  location=(4.0, 0.0, CENTRE_Z), rotation=(math.radians(90.0), 0.0, math.radians(90.0)))
            pose_walk(rig)
            camera.data.type = "PERSP"
            camera.data.lens = 70.0
            camera.location = (1.6, -3.3, CENTRE_Z + 0.1)
            camera.rotation_euler = (math.radians(88.0), 0.0, math.radians(26.0))
            shoot(camera, f"{stem}_walk.png", (640, 1000))
            pose(rig, {})
            entry["render_engine"] = "Cycles CPU" if CYCLES else "EEVEE"
            bpy.context.view_layer.update()

        if EXPORT:
            export_started = time.time()
            if body_spec.get("hair_card_match"):
                # HumGen darkens and saturates the card and haircap colours to match its particle renders (value 0.26 on the
                # cards); that turns the lightest particle hair beige, so platinum hair sets its own [value, saturation].
                value, saturation = body_spec["hair_card_match"]
                haircards.SCALP_SETTINGS = dataclasses.replace(haircards.SCALP_SETTINGS, color_match=dataclasses.replace(
                    haircards.SCALP_SETTINGS.color_match, value=value, saturations=(saturation, saturation)))
                haircards.HAIRCAP_COLOR_MATCH = dataclasses.replace(haircards.HAIRCAP_COLOR_MATCH, value=value * 1.3,
                                                                    saturations=(saturation, saturation))
            if body_spec.get("hair_cards"):
                cards = body_spec["hair_cards"]
                haircards.SCALP_SETTINGS = dataclasses.replace(
                    haircards.SCALP_SETTINGS, min_half_width=cards["min_half_width"], max_half_width=cards["max_half_width"],
                    triangle_budgets={**haircards.SCALP_SETTINGS.triangle_budgets, "ultra": cards["ultra_triangles"]})
            settings = ExportSettings.from_recipe("godot")
            if body_spec.get("texture_resolution"):
                settings.textures.resolution = dict(body_spec["texture_resolution"])
            settings.lods = list(LODS)
            settings.output.folder = str(OUT)
            settings.output.name = identifier
            result = human.process.run(settings)
            entry["export"] = {"files": [os.path.basename(path) for path in getattr(result, "files", [])],
                               "triangles": getattr(result, "triangles", None),
                               "minutes": round((time.time() - export_started) / 60, 2),
                               "warnings": [str(warning) for warning in getattr(result, "warnings", [])][:4]}
        entry["ok"] = True
    except Exception:  # noqa: BLE001
        entry.update({"ok": False, "error": traceback.format_exc()[-2000:]})
    entry["minutes"] = round((time.time() - item_started) / 60, 2)
    report["bodies"].append(entry)
    # Merge with the record already on disk: a stills-only pass must not erase the export evidence from an earlier run.
    record_path = OUT / "records" / f"{identifier}.json"
    if record_path.exists():
        previous = json.loads(record_path.read_text(encoding="utf-8-sig"))
        merged = dict(previous)
        merged.update({key: value for key, value in entry.items() if value is not None})
        entry = merged
    record_path.write_text(json.dumps(entry, indent=2), encoding="utf-8")
    print("PROD_BODY " + json.dumps({"id": identifier, "ok": entry.get("ok"), "minutes": entry["minutes"],
                                     "follow": entry.get("follow_ratio")}), flush=True)

report["minutes"] = round((time.time() - started_all) / 60, 2)
(OUT / "race_bodies_production_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("PROD_DONE " + json.dumps({"bodies": len(report["bodies"]),
                                 "ok": sum(1 for entry in report["bodies"] if entry.get("ok")),
                                 "minutes": report["minutes"]}))
