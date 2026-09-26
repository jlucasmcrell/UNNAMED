"""Blender: the production UV layout for a reconstructed character - a face chart projected from the concept camera,
body charts cut by body region, texel priority for the face, head and hands, one packed atlas. The old layout rides
along as the second UV set so the bakes can carry the reconstruction's maps across.

Why: the image-to-3D unwrapper's layout is ~950 sliver charts (the largest 488 faces) that pack to 51-59% of the
atlas at one uniform texel density - the face got ~4% of a 2048 texture - and its seams run across the face.

  * Face chart: the front of the head and neck that the concept camera sees, as ONE chart whose UVs are the
    camera's projection, so the concept's face pixels land on it without resampling across seams.
  * Body charts: each face goes to a body region by its dominant bone (torso, head, arm, hand, leg, foot per side),
    each region is cut in two along its bone axis (front/back; left/right for the head, palm/back for a hand,
    top/sole for a foot) so every chart is a disk, and the charts are flattened with Blender's minimum-stretch
    (SLIM) unwrap. The reconstruction's surface normals are too noisy for angle-based charting (Smart UV Project
    gives 3,000-6,500 islands on this mesh).
  * Texel priority: every chart scaled to one density, then the face chart by --face-density, the rest of the head
    by --head-density and the hands by --hand-density, packed together.

    blender --background --factory-startup --python uv_layout.py -- --input rigged.glb --camera camera.json --out out.glb
        [--face-density 2.5] [--head-density 1.4] [--hand-density 1.4] [--margin 0.003] [--report report.json]
"""
import argparse
import json
import math
import sys
from collections import Counter

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

# humanoid20 bones -> (region, primary bone chain start, end, split)
REGIONS = {
    "root": "torso", "hips": "torso", "spine": "torso", "chest": "torso", "shoulder.L": "torso", "shoulder.R": "torso",
    "neck": "head", "head": "head",
    "upper_arm.L": "arm.L", "forearm.L": "arm.L", "hand.L": "hand.L",
    "upper_arm.R": "arm.R", "forearm.R": "arm.R", "hand.R": "hand.R",
    "thigh.L": "leg.L", "shin.L": "leg.L", "foot.L": "foot.L",
    "thigh.R": "leg.R", "shin.R": "leg.R", "foot.R": "foot.R",
}
FORWARD, SIDE, UP = Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))  # Blender axes of a model facing glTF +Z


def args():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--camera", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--face-density", type=float, default=2.5)
    ap.add_argument("--head-density", type=float, default=1.4)
    ap.add_argument("--hand-density", type=float, default=1.4)
    ap.add_argument("--inner-density", type=float, default=0.35, help="surfaces facing into the body (garment linings)")
    ap.add_argument("--face-facing", type=float, default=0.2, help="min cos between a face's normal and the view ray")
    ap.add_argument("--margin", type=float, default=0.003)
    ap.add_argument("--min-chart-faces", type=int, default=40)
    ap.add_argument("--report")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:])


def to_blender(p):  # glTF (x, y, z) -> Blender (x, -z, y)
    return Vector((p[0], -p[2], p[1]))


def to_gltf(v):
    return np.array([v.x, v.z, -v.y])


def main():
    a = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.input)
    # The importer may add a bone-display shape; the character is the skinned mesh.
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups]
    assert len(meshes) == 1, f"expected one skinned mesh, found {len(meshes)}"
    obj = meshes[0]
    arm = obj.find_armature()
    me = obj.data
    cam = json.load(open(a.camera))
    R, t, f, c = np.array(cam["R"]), np.array(cam["t"]), cam["f"], np.array(cam["c"])
    W, H = cam["width"], cam["height"]
    centre = to_blender(-R.T @ t)
    mw = obj.matrix_world
    bones = {b.name: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones}
    groups = {g.index: g.name for g in obj.vertex_groups}

    bm = bmesh.new()
    bm.from_mesh(me)
    # Welded by position: glTF splits vertices at every UV seam (the importer's own merge also needs equal normals),
    # which would leave the surface cut into the old charts. UVs are per corner, so the old layout survives the weld.
    welded = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    welded -= len(bm.verts)
    for fc in bm.faces:
        fc.smooth = True
    bm.faces.ensure_lookup_table()
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    deform = bm.verts.layers.deform.active
    uv_old = bm.loops.layers.uv.active

    world = bm.copy()
    world.transform(mw)
    world.normal_update()
    world.faces.ensure_lookup_table()
    bvh = BVHTree.FromBMesh(world)

    def face_bones(fc):
        s = Counter()
        for v in fc.verts:
            for gi, w in v[deform].items():
                s[groups[gi]] += w
        return s

    scores = [face_bones(fc) for fc in bm.faces]
    dominant = [s.most_common(1)[0][0] if s else "root" for s in scores]

    def neighbours(fi):
        return {l.face.index for e in bm.faces[fi].edges for l in e.link_loops if l.face.index != fi}

    # --- the face chart ---
    def visible(fi):
        p = world.faces[fi].calc_center_median()
        d = p - centre
        dist = d.length
        hit = bvh.ray_cast(centre, d.normalized(), dist + 1e-3)
        return hit[0] is not None and (hit[2] == fi or hit[3] >= dist - 2e-3)

    cand = set()
    for fc in bm.faces:
        s = scores[fc.index]
        total = sum(s.values()) or 1.0
        if (s["head"] + s["neck"]) / total < 0.5:
            continue
        wf = world.faces[fc.index]
        if wf.normal.dot((centre - wf.calc_center_median()).normalized()) < a.face_facing:
            continue
        if visible(fc.index):
            cand.add(fc.index)
    for _ in range(4):
        cand |= {n for fi in cand for n in neighbours(fi) if n not in cand and len(neighbours(n) & cand) >= 2}
    for _ in range(3):
        cand = {fi for fi in cand if len(neighbours(fi) & cand) >= 2}
    comps, seen = [], set()
    for fi in cand:
        if fi in seen:
            continue
        stack, comp = [fi], set()
        while stack:
            x = stack.pop()
            if x not in comp:
                comp.add(x)
                stack.extend(n for n in neighbours(x) if n in cand and n not in comp)
        seen |= comp
        comps.append(comp)
    face_chart = max(comps, key=len)
    # A grown face turned away from the camera would fold the projection over; it goes to the head charts.
    face_chart = {fi for fi in face_chart
                  if world.faces[fi].normal.dot((centre - world.faces[fi].calc_center_median()).normalized()) > 0.02}

    # --- body regions, each cut in two along its axis ---
    def axis(region):
        side = region.split(".")[-1] if "." in region else ""
        if region == "torso":
            return bones["hips"][0], bones["neck"][0], FORWARD
        if region == "head":
            return bones["neck"][0], bones["head"][1], SIDE
        if region.startswith("arm"):
            return bones[f"upper_arm.{side}"][0], bones[f"hand.{side}"][0], FORWARD
        if region.startswith("hand"):
            return bones[f"hand.{side}"][0], bones[f"hand.{side}"][1], SIDE
        if region.startswith("leg"):
            return bones[f"thigh.{side}"][0], bones[f"foot.{side}"][0], FORWARD
        return bones[f"foot.{side}"][0], bones[f"foot.{side}"][1], UP

    axes = {}
    for region in set(REGIONS.values()):
        p0, p1, split = axis(region)
        d = (p1 - p0).normalized()
        v = (split - d * split.dot(d)).normalized()
        axes[region] = (p0, d, v)

    label = {}
    for fc in bm.faces:
        if fc.index in face_chart:
            label[fc.index] = "face"
            continue
        region = REGIONS.get(dominant[fc.index], "torso")
        p0, d, v = axes[region]
        pc = world.faces[fc.index].calc_center_median()
        rel = pc - p0
        rel -= d * rel.dot(d)
        # The reconstruction's garments are closed thin shells: a lining faces into the body. Outer and inner
        # surfaces get separate charts (each region half is then a disk) and linings a low texel density.
        facing = "o" if world.faces[fc.index].normal.dot(rel.normalized()) >= 0 else "i"
        label[fc.index] = f"{region}|{'a' if rel.dot(v) >= 0 else 'b'}|{facing}"

    # Smooth the labels (speckles and tiny islands join their neighbours) so every chart is one clean piece.
    for _ in range(3):
        changes = {}
        for fi, lab in label.items():
            if lab == "face":
                continue
            votes = Counter(label[n] for n in neighbours(fi) if label[n] != "face")
            if votes and votes.most_common(1)[0][1] >= 2 and votes.most_common(1)[0][0] != lab:
                changes[fi] = votes.most_common(1)[0][0]
        label.update(changes)
    for _ in range(4):
        seen, merged = set(), 0
        for fi in list(label):
            if fi in seen or label[fi] == "face":
                continue
            lab = label[fi]
            stack, comp = [fi], set()
            while stack:
                x = stack.pop()
                if x not in comp:
                    comp.add(x)
                    stack.extend(n for n in neighbours(x) if label[n] == lab and n not in comp)
            seen |= comp
            if len(comp) < a.min_chart_faces:
                border = Counter(label[n] for x in comp for n in neighbours(x) if label[n] != lab and label[n] != "face")
                if border:
                    for x in comp:
                        label[x] = border.most_common(1)[0][0]
                    merged += 1
        if not merged:
            break

    # Seams on every label boundary; the UVs of the new layer: the projection on the face chart, SLIM elsewhere.
    for e in bm.edges:
        fs = [l.face.index for l in e.link_loops]
        e.seam = len(fs) != 2 or label[fs[0]] != label[fs[1]]
    uv_new = bm.loops.layers.uv.new("UV_new")
    for fc in bm.faces:
        for l in fc.loops:
            l[uv_new].uv = l[uv_old].uv.copy()
    for fi in face_chart:
        for l in bm.faces[fi].loops:
            q = R @ to_gltf(mw @ l.vert.co) + t
            l[uv_new].uv = Vector(((f * q[0] / q[2] + c[0]) / W, 1.0 - (f * q[1] / q[2] + c[1]) / H))
    for fc in bm.faces:
        fc.select_set(fc.index not in face_chart)
    bm.to_mesh(me)
    world.free()
    labels = dict(label)
    bm.free()

    me.uv_layers.active = me.uv_layers["UV_new"]
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    bpy.ops.uv.unwrap(method="MINIMUM_STRETCH", fill_holes=True, margin=0.0, iterations=20)
    bpy.ops.object.mode_set(mode="OBJECT")

    # Charts: faces joined across a non-seam edge whose two loops agree in UV.
    def chart_stats():
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        uvl = bm.loops.layers.uv["UV_new"]
        parent = list(range(len(bm.faces)))

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        for e in bm.edges:
            if len(e.link_loops) != 2:
                continue
            l1, l2 = e.link_loops
            if (l1[uvl].uv - l2.link_loop_next[uvl].uv).length < 1e-6 and (l1.link_loop_next[uvl].uv - l2[uvl].uv).length < 1e-6:
                parent[find(l1.face.index)] = find(l2.face.index)
        charts = {}
        for fc in bm.faces:
            charts.setdefault(find(fc.index), []).append(fc.index)

        def signed_uv(fc):
            pts = [l[uvl].uv for l in fc.loops]
            return sum(pts[i].x * pts[(i + 1) % len(pts)].y - pts[(i + 1) % len(pts)].x * pts[i].y for i in range(len(pts))) / 2

        ws = bm.copy()
        ws.transform(mw)
        ws.faces.ensure_lookup_table()
        stats = {}
        for r, fis in charts.items():
            signed = [signed_uv(bm.faces[fi]) for fi in fis]
            positive = sum(1 for s in signed if s > 0)
            stats[r] = (sum(ws.faces[fi].calc_area() for fi in fis), sum(abs(s) for s in signed),
                        min(positive, len(signed) - positive) / len(signed))
        ws.free()
        bm.free()
        return charts, stats

    # A chart SLIM could not flatten (collapsed, or folded over itself) is flattened again conformally (LSCM).
    charts, stats = chart_stats()
    ratios = sorted(math.sqrt(a3 / auv) for a3, auv, _ in stats.values() if auv > 1e-12 and a3 > 1e-12)
    median = ratios[len(ratios) // 2]
    bad = [fi for r, fis in charts.items() if not (fis[0] in face_chart)
           and (stats[r][1] <= 1e-12 or math.sqrt(stats[r][0] / stats[r][1]) > 3 * median or stats[r][2] > 0.05) for fi in fis]
    refit = len({r for r, fis in charts.items() if fis[0] in set(bad)})
    if bad:
        badset = set(bad)
        for poly in me.polygons:
            poly.select = poly.index in badset
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.uv.unwrap(method="CONFORMAL", fill_holes=True, margin=0.0)
        bpy.ops.object.mode_set(mode="OBJECT")
        charts, stats = chart_stats()
    # Still folded (hair clumps are no disk for any flattening): charted by angle instead - many small charts, which
    # suits a surface whose texture is uniform strands.
    still = {fi for r, fis in charts.items() if fis[0] not in face_chart and stats[r][2] > 0.05 for fi in fis}
    report_angle = len(still)
    if still:
        for poly in me.polygons:
            poly.select = poly.index in still
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, area_weight=0.0, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        charts, stats = chart_stats()
    areas = {r: (s[0], s[1]) for r, s in stats.items()}
    folded = sum(len(charts[r]) for r, s in stats.items() if s[2] > 0.05)
    ratios = sorted(math.sqrt(a3 / auv) for a3, auv in areas.values() if auv > 1e-12 and a3 > 1e-12)
    median = ratios[len(ratios) // 2]
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    uvl = bm.loops.layers.uv["UV_new"]
    kinds = Counter()
    for r, fis in charts.items():
        a3, auv = areas[r]
        if auv <= 1e-12 or a3 <= 1e-12:
            continue
        lab = Counter(labels[fi] for fi in fis).most_common(1)[0][0]
        region = lab.split("|")[0]
        kind, k = ("face", a.face_density) if lab == "face" else ("inner", a.inner_density) if lab.endswith("|i") else \
            ("head", a.head_density) if region == "head" else ("hand", a.hand_density) if region.startswith("hand") else ("body", 1.0)
        kinds[kind] += len(fis)
        # A chart whose UVs are nearly collapsed would get an enormous scale and squeeze every other chart in the pack.
        s = k * min(max(math.sqrt(a3 / auv), median / 3), median * 3)
        loops = [l for fi in fis for l in bm.faces[fi].loops]
        cx = sum(l[uvl].uv.x for l in loops) / len(loops)
        cy = sum(l[uvl].uv.y for l in loops) / len(loops)
        for l in loops:
            u = l[uvl].uv
            l[uvl].uv = Vector(((u.x - cx) * s, (u.y - cy) * s))
    bm.to_mesh(me)
    bm.free()

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(udim_source="CLOSEST_UDIM", rotate=True, rotate_method="ANY", scale=True,
                            margin_method="FRACTION", margin=a.margin, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")

    # Layer order for the export: TEXCOORD_0 = the new layout, TEXCOORD_1 = the old one.
    n = len(me.loops)
    new = np.zeros(n * 2, np.float32)
    old = np.zeros(n * 2, np.float32)
    me.uv_layers["UV_new"].data.foreach_get("uv", new)
    old_name = [l.name for l in me.uv_layers if l.name != "UV_new"][0]
    me.uv_layers[old_name].data.foreach_get("uv", old)
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UV_new").data.foreach_set("uv", new)
    me.uv_layers.new(name="UV_old").data.foreach_set("uv", old)
    me.uv_layers.active_index = 0

    uvn = new.reshape(-1, 2)
    used = 0.0
    for poly in me.polygons:
        pts = uvn[list(poly.loop_indices)]
        used += abs(sum(pts[i, 0] * pts[(i + 1) % len(pts), 1] - pts[(i + 1) % len(pts), 0] * pts[i, 1] for i in range(len(pts)))) / 2
    report = {"faces": len(me.polygons), "welded_vertices": welded, "face_chart_faces": len(face_chart), "charts": len(charts),
              "labels": len(set(labels.values())), "conformal_refits": refit, "angle_charted_faces": report_angle, "faces_in_folded_charts": folded,
              "faces_by_kind": dict(kinds), "atlas_used": round(float(used), 4),
              "face_density": a.face_density, "head_density": a.head_density, "hand_density": a.hand_density, "inner_density": a.inner_density, "margin": a.margin}
    bpy.ops.export_scene.gltf(filepath=a.out, export_format="GLB", use_selection=False, export_apply=False, export_skins=True)
    if a.report:
        json.dump(report, open(a.report, "w"), indent=1)
    print("UV_LAYOUT_RESULT " + json.dumps(report))


main()
