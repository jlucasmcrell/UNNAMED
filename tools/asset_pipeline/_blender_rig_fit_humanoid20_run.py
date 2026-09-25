"""Stage re-fitted 20-bone rigs, their rebuilt clips, verification and renders - never the library.

Thin driver around `_blender_rig_fit_humanoid20.py`, `_make_creature_motion.py` and
`_blender_anim_creature.py`. The existing clip builders (`_build_npc_anims.py`,
`_build_creature_anims.py`) write straight into assets/animation and assets/rigged; this wrapper
reuses their clip tables and the same two tools, but every file lands under
assets/_staging/rigs/<asset id>/:

  <id>_rigged.glb                       the fitted, heat-weighted rig (mesh untouched)
  <id>_rig.json                         the library's rig report shape, plus the fit
  <id>_rigged_skeleton.json             bones in the export frame (glTF, Y up, +Z forward)
  <id>_fit_report.json                  per-joint fit, weights, clip and render checks, control
  animation/source/...                  the generated motion
  animation/ready/<family>/anim.*.glb   clips rebuilt against the fitted rest pose
  animation/clips/anim.*.json           their records
  renders/                              bind vs LOD0, every clip at several frames, sheets

Usage:
  python _blender_rig_fit_humanoid20_run.py npc_kal_smith creature_animated_armour
  python _blender_rig_fit_humanoid20_run.py --skip-render npc_kal_smith
"""
import argparse
import io
import json
import os
import subprocess
import sys

import numpy as np

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOL_DIR)
import _blender_rig_fit_humanoid20 as fit  # noqa: E402  (numpy half; bpy is not needed here)
import _build_creature_anims as creature_anims  # noqa: E402  (tables only; main() is guarded)
import _build_npc_anims as npc_anims  # noqa: E402

REPO = os.path.abspath(os.path.join(TOOL_DIR, "..", ".."))
ASSETS = os.path.join(REPO, "assets")
STAGING = os.path.join(ASSETS, "_staging", "rigs")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
FIT_TOOL = os.path.join(TOOL_DIR, "_blender_rig_fit_humanoid20.py")
MOTION_TOOL = os.path.join(TOOL_DIR, "_make_creature_motion.py")
ANIM_TOOL = os.path.join(TOOL_DIR, "_blender_anim_creature.py")
CONTROL = "npc_veth_magistrate"
# The control rig frozen before Renn's own body was re-fitted (2026-09-25): the template's names, order,
# hierarchy and roll every fit is checked against, and the rig the control numbers compare the fitter with.
CONTROL_RIG = os.path.join(ASSETS, "rigs", "humanoid20_control", f"{CONTROL}_rigged.glb")
FPS = 30

# What each asset plays. Kera's NPC set is rebuilt under her own ids: the shared anim.npc.* files
# carry the 1.8 m rig's rest translations and would pull her joints back to the old template.
ASSET_CLIPS = {
    "npc_kal_smith": {
        "family": "npc", "folder": "npc", "plan": "humanoid",
        "kinds": ["work_forge", "talk", "handover"],
        "clip_id": lambda kind: f"anim.npc.kal_smith.{kind}",
        "motion_name": lambda kind: f"npc_kal_smith_{kind}.json",
    },
    "creature_animated_armour": {
        "family": "creature", "folder": "creatures", "plan": "humanoid",
        "kinds": ["idle", "walk", "run", "attack", "hit", "death"],
        "clip_id": lambda kind: f"anim.creature.animated_armour.{kind}",
        "motion_name": lambda kind: f"animated_armour_{kind}.json",
    },
}


def run(cmd, timeout=1800):
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    return result


def tagged(stdout, tag):
    for line in (stdout or "").splitlines():
        if line.startswith(tag + " "):
            return json.loads(line[len(tag) + 1:])
    return None


def blender(*args):
    return run([BLENDER, "--background", "--factory-startup", "--python", *args])


# ------------------------------------------------------------------------------------------
# verification of the exported rig

def reference_rig():
    return fit.glb_skeleton(CONTROL_RIG)


def vertex_attributes(mesh):
    """Every vertex as (position, normal, uv), for comparing two exports of the same surface."""
    doc, blob = mesh["doc"], mesh["blob"]
    rows = []
    for node in doc["nodes"]:
        if "mesh" not in node:
            continue
        for prim in doc["meshes"][node["mesh"]]["primitives"]:
            attrs = prim["attributes"]
            parts = [fit.accessor(doc, blob, attrs[k]).astype(np.float64)
                     for k in ("POSITION", "NORMAL", "TEXCOORD_0") if k in attrs]
            rows.append(np.concatenate(parts, axis=1))
    return np.concatenate(rows)


def attribute_mismatch(reference, other, tol=1e-3):
    """Vertices of `other` with no vertex of `reference` at the same position carrying the same
    normal and uv (to `tol`), and the largest normal and uv difference found."""
    ref = vertex_attributes(reference)
    table = {}
    for row in ref:
        table.setdefault(tuple(np.round(row[:3], 5)), []).append(row[3:])
    off, worst_n, worst_uv = 0, 0.0, 0.0
    for row in vertex_attributes(other):
        cands = np.array(table.get(tuple(np.round(row[:3], 5)), []))
        if not len(cands):
            off += 1
            continue
        dn = np.abs(cands[:, :3] - row[3:6]).max(1)
        duv = np.abs(cands[:, 3:5] - row[6:8]).max(1) if cands.shape[1] >= 5 else np.zeros(len(cands))
        k = int(np.argmin(dn + duv))
        worst_n, worst_uv = max(worst_n, float(dn[k])), max(worst_uv, float(duv[k]))
        off += int(dn[k] + duv[k] > tol)
    return {"count": off, "max_normal_delta": round(worst_n, 5), "max_uv_delta": round(worst_uv, 6)}


def triangle_set(mesh):
    """Triangles as rows of their three corner positions, corners in a canonical order."""
    corners = np.round(mesh["verts"][mesh["tris"]], 5)            # (n, 3, 3)
    order = np.lexsort((corners[:, :, 2], corners[:, :, 1], corners[:, :, 0]), axis=1)
    canon = np.take_along_axis(corners, order[:, :, None], axis=1).reshape(len(corners), -1)
    return np.unique(canon, axis=0)


def verify_rig(asset, rigged_path, bones=None):
    lod0 = fit.glb_mesh(os.path.join(ASSETS, "ready", asset, f"{asset}.glb"))
    rig = fit.glb_mesh(rigged_path, skinned=True)
    skel = fit.glb_skeleton(rigged_path)
    ref = reference_rig()

    same_count = len(lod0["verts"]) == len(rig["verts"]) and len(lod0["tris"]) == len(rig["tris"])
    geometry = {
        "lod0_vertices": int(len(lod0["verts"])), "rigged_vertices": int(len(rig["verts"])),
        "lod0_triangles": int(len(lod0["tris"])), "rigged_triangles": int(len(rig["tris"])),
    }
    if same_count:
        geometry["max_position_delta_m"] = float(np.abs(lod0["verts"] - rig["verts"]).max())
        geometry["index_buffers_identical"] = bool(np.array_equal(lod0["tris"], rig["tris"]))
    else:
        # the exporter may reorder and split vertices; compare as sets
        a = np.round(lod0["verts"], 5)
        b = np.round(rig["verts"], 5)
        geometry["point_sets_identical"] = bool(np.array_equal(np.unique(a, axis=0), np.unique(b, axis=0)))
    # the surface as drawn: every (position, normal, uv) of the rigged file against LOD0's, and
    # the same count for the library's current rig, which went through the same exporter
    geometry["vertices_with_normal_or_uv_off"] = attribute_mismatch(lod0, rig)
    library = os.path.join(ASSETS, "rigged", asset, f"{asset}_rigged.glb")
    if os.path.exists(library):
        geometry["library_rig_vertices_with_normal_or_uv_off"] = attribute_mismatch(
            lod0, fit.glb_mesh(library, skinned=True))
    geometry["triangle_sets_identical"] = bool(np.array_equal(triangle_set(lod0), triangle_set(rig)))

    structure = {
        "bone_count": len(skel["names"]),
        "names_match_control": skel["names"] == ref["names"],
        "parents_match_control": skel["parents"] == ref["parents"],
        "joint_order_matches_control": skel["names"] == ref["names"],
        "bind_vs_rest_maxabs": round(skel["bind_vs_rest"], 8),
    }

    names = skel["names"]
    joints = skel["positions"]
    check, H = fit.fit_check(joints, names, lod0["verts"], lod0["tris"])
    tolerance = fit.FIT_TOLERANCE * H

    J, W = rig["joints"], rig["weights"]
    dominated = {n: int(((J == k) & (W > 0.5)).any(1).sum()) for k, n in enumerate(names)}
    influenced = {n: int(((J == k) & (W > 0.0)).any(1).sum()) for k, n in enumerate(names)}
    strongest = J[np.arange(len(J)), W.argmax(1)]
    leading = {n: int((strongest == k).sum()) for k, n in enumerate(names)}
    weights = {
        "unweighted_vertices": int((W.sum(1) <= 1e-6).sum()),
        "max_influences": int((W > 0).sum(1).max()),
        "weight_sum_range": [round(float(W.sum(1).min()), 4), round(float(W.sum(1).max()), 4)],
        "dominated_vertices": dominated,
        "dominated_means": "weight > 0.5 on the vertex (the measure rigmeasure.py used)",
        "strongest_influence_vertices": leading,
        "influenced_vertices": influenced,
    }
    # how much surface each bone has to move: vertices within 0.08 x H of the middle 70% of the
    # bone (a bone whose stretch of body carries no vertex cannot dominate one)
    if bones:
        V = rig["verts"]
        near = {}
        for n in names:
            a, b = np.array(bones[n]["head"]), np.array(bones[n]["tail"])
            ab = b - a
            t = ((V - a) @ ab) / max(float(ab @ ab), 1e-12)
            d = np.linalg.norm(V - (a + np.clip(t, 0, 1)[:, None] * ab), axis=1)
            near[n] = int(((d < 0.08 * float(V[:, 2].max())) & (t > 0.15) & (t < 0.85)).sum())
        weights["vertices_along_bone"] = near
    joints_report = {}
    for n in names:
        entry = dict(check[n])
        entry["deform"] = n != "root"
        entry["dominated_vertices"] = dominated[n]
        entry["strongest_influence_vertices"] = leading[n]
        joints_report[n] = entry
    deform = [n for n in names if n != "root"]
    failures = [n for n in deform if not joints_report[n]["within_tolerance"]]
    empty_limbs = [n for n in fit.LIMB_BONES if dominated[n] == 0]
    return {
        "height_m": round(H, 4),
        "tolerance_m": round(tolerance, 4),
        "geometry": geometry,
        "structure": structure,
        "joints": joints_report,
        "weights": weights,
        "worst_deform_joint": max(deform, key=lambda n: joints_report[n]["effective_m"]),
        "worst_deform_effective_m": max(joints_report[n]["effective_m"] for n in deform),
        "worst_deform_surface_m": max(joints_report[n]["surface_m"] for n in deform),
        "deform_joints_outside_tolerance": failures,
        "limb_bones_dominating_nothing": empty_limbs,
        "root_note": ("root is the ground anchor under the pelvis, as in the template; it is not "
                      "a deform bone and carries no weight, so it is reported but not held to "
                      "the tolerance"),
    }


def before_numbers(asset):
    """The same fit check on the library's current rig, for the before/after comparison."""
    path = os.path.join(ASSETS, "rigged", asset, f"{asset}_rigged.glb")
    lod0 = fit.glb_mesh(os.path.join(ASSETS, "ready", asset, f"{asset}.glb"))
    skel = fit.glb_skeleton(path)
    check, H = fit.fit_check(skel["positions"], skel["names"], lod0["verts"], lod0["tris"])
    deform = [n for n in skel["names"] if n != "root"]
    return {
        "max_nearest_vertex_m_all_joints": max(check[n]["nearest_vertex_m"] for n in skel["names"]),
        "max_nearest_vertex_m_deform": max(check[n]["nearest_vertex_m"] for n in deform),
        "max_effective_m_deform": max(check[n]["effective_m"] for n in deform),
        "deform_joints_outside_tolerance": [n for n in deform if not check[n]["within_tolerance"]],
        "joints": check,
    }


def control_numbers():
    """The fitter run on the control mesh, against the control's own (good) rig."""
    lod0 = fit.glb_mesh(os.path.join(ASSETS, "ready", CONTROL, f"{CONTROL}.glb"))
    bones, measured, notes, _body = fit.fit_skeleton(lod0["verts"], lod0["tris"])
    ref = reference_rig()
    ours = np.array([bones[n][0] for n in ref["names"]])
    delta = np.linalg.norm(ours - ref["positions"], axis=1)
    check_ref, H = fit.fit_check(ref["positions"], ref["names"], lod0["verts"], lod0["tris"])
    check_ours, _H = fit.fit_check(ours, ref["names"], lod0["verts"], lod0["tris"])
    deform = [n for n in ref["names"] if n != "root"]
    return {
        "asset": CONTROL,
        "note": "the fitter run on the control mesh; its rig fits today, so the fitted joints "
                "should land near the library joints and pass the same check",
        "fitted_vs_library_joint_distance_m": {n: round(float(d), 4) for n, d in zip(ref["names"], delta)},
        "fitted_vs_library_max_m": round(float(delta.max()), 4),
        "fitted_vs_library_mean_m": round(float(delta.mean()), 4),
        "library_rig_max_effective_m_deform": max(check_ref[n]["effective_m"] for n in deform),
        "library_rig_max_nearest_vertex_m_deform": max(check_ref[n]["nearest_vertex_m"] for n in deform),
        "fitted_rig_max_effective_m_deform": max(check_ours[n]["effective_m"] for n in deform),
        "tolerance_m": round(fit.FIT_TOLERANCE * H, 4),
        "fit_notes": notes,
        "measured": {k: v for k, v in measured.items() if k != "landmarks"},
    }


# ------------------------------------------------------------------------------------------
# clips

def build_clips(asset, rigged_path, outdir):
    spec = ASSET_CLIPS[asset]
    results = {}
    for kind in spec["kinds"]:
        clip_id = spec["clip_id"](kind)
        motion_path = os.path.join(outdir, "animation", "source", spec["folder"], spec["motion_name"](kind))
        os.makedirs(os.path.dirname(motion_path), exist_ok=True)
        gen = run([sys.executable, MOTION_TOOL, "--plan", spec["plan"], "--kind", kind,
                   "--asset-id", asset, "--out", motion_path], timeout=300)
        if not os.path.exists(motion_path):
            results[clip_id] = {"ok": False, "problem": f"motion failed: {gen.stderr[-200:]}"}
            continue
        glb_path = os.path.join(outdir, "animation", "ready", spec["folder"], f"{clip_id}.glb")
        os.makedirs(os.path.dirname(glb_path), exist_ok=True)
        anim = blender(ANIM_TOOL, "--", "--rigged", rigged_path, "--motion", motion_path,
                       "--out", glb_path, "--fps", str(FPS))
        report = tagged(anim.stdout, "CREATURE_ANIM_RESULT")
        if report is None or not os.path.exists(glb_path):
            results[clip_id] = {"ok": False, "problem": f"animation failed: {(anim.stdout or '')[-300:]}"}
            continue
        record = clip_record(asset, spec, kind, clip_id, report)
        record_path = os.path.join(outdir, "animation", "clips", f"{clip_id}.json")
        os.makedirs(os.path.dirname(record_path), exist_ok=True)
        with io.open(record_path, "w", encoding="utf-8") as handle:
            json.dump(record, handle, indent=2)
            handle.write("\n")
        results[clip_id] = {"ok": not report["bones_missing"], "glb": glb_path, "record": record_path,
                            "frames": report["frames"], "duration_s": report["duration_s"],
                            "bones_keyed": report["bones_keyed"], "bones_missing": report["bones_missing"],
                            "clip_file_check": check_clip_file(glb_path, rigged_path)}
    return results


def clip_record(asset, spec, kind, clip_id, report):
    if spec["family"] == "creature":
        clip_type, category, loop, root_motion, events = creature_anims.CLIP_DEFS[kind]
        plan, family, _short = creature_anims.ENEMIES[asset]
        return {
            "schema_version": "1", "animation_id": clip_id, "skeleton_family": family,
            "category": category, "animation_family": "creature", "clip_type": clip_type,
            "loop": loop, "root_motion": root_motion,
            "duration_s": creature_anims.DURATIONS[kind],
            "events": [{"id": e, "time": t} for e, t in events],
            "hand_profile": {"left": "none", "right": "none"},
            "tags": ["creature", plan] + (["looping"] if loop else []),
        }
    clip_type, category, loop, root_motion, events = npc_anims.CLIP_SPECS[kind]
    return {
        "schema_version": "1", "animation_id": clip_id,
        "skeleton_family": npc_anims.SKELETON_FAMILY, "category": category,
        "animation_family": npc_anims.ANIMATION_FAMILY, "clip_type": clip_type,
        "loop": loop, "root_motion": root_motion,
        "duration_s": (report["frames"] - 1) / float(FPS),
        "events": [{"id": e, "time": t} for e, t in events],
        "source": {"rig": asset, "rig_plan": npc_anims.RIG_PLAN,
                   "tool": "_make_creature_motion.py", "shared": False},
        "applies_to": [asset],
        "reference_pose": "fitted rest pose of " + asset,
        "notes": ("Kera's own copy of the shared NPC " + kind + " motion, baked against her fitted "
                  "20-bone rest pose. The shared anim.npc." + kind + " carries the 1.8 m template's "
                  "rest translations, which would pull her joints back to that template."),
    }


def check_clip_file(clip_path, rigged_path):
    """The clip's bone rest transforms must be the fitted rig's (the game plays every track)."""
    cdoc, _cb = fit.read_glb(clip_path)
    rdoc, _rb = fit.read_glb(rigged_path)
    rnodes = {n.get("name"): n for n in rdoc["nodes"]}
    worst_t, worst_r, names = 0.0, 0.0, []
    for node in cdoc["nodes"]:
        name = node.get("name")
        if name in rnodes and "mesh" not in node:
            ref = rnodes[name]
            worst_t = max(worst_t, float(np.abs(np.subtract(node.get("translation", [0, 0, 0]),
                                                            ref.get("translation", [0, 0, 0]))).max()))
            q1 = np.array(node.get("rotation", [0, 0, 0, 1]))
            q2 = np.array(ref.get("rotation", [0, 0, 0, 1]))
            worst_r = max(worst_r, float(min(np.abs(q1 - q2).max(), np.abs(q1 + q2).max())))
            names.append(name)
    anim = cdoc["animations"][0]
    targets = sorted({cdoc["nodes"][c["target"]["node"]].get("name") for c in anim["channels"]})
    return {"bones_with_tracks": len(targets), "rest_translation_delta_max": round(worst_t, 7),
            "rest_rotation_delta_max": round(worst_r, 7), "has_mesh": bool(cdoc.get("meshes"))}


# ------------------------------------------------------------------------------------------
# renders

def render(asset, rigged_path, clips, outdir, frames=5, subdir="renders"):
    renders = os.path.join(outdir, subdir)
    os.makedirs(renders, exist_ok=True)
    library = os.path.join(ASSETS, "rigged", asset, f"{asset}_rigged.glb")
    baseline = ["--baseline", library] if os.path.abspath(library) != os.path.abspath(rigged_path) else []
    result = blender(FIT_TOOL, "--", "--mode", "render", "--rigged", rigged_path,
                     "--reference", os.path.join(ASSETS, "ready", asset, f"{asset}.glb"), *baseline,
                     "--clips", *clips, "--outdir", renders, "--frames", str(frames), "--size", "1024")
    manifest_path = os.path.join(renders, "render_manifest.json")
    if not os.path.exists(manifest_path):
        return {"ok": False, "problem": (result.stdout or "")[-500:] + (result.stderr or "")[-500:]}
    with open(manifest_path, encoding="utf-8") as handle:
        manifest = json.load(handle)
    manifest["bind_vs_lod0"] = compare_images(manifest["bind"], manifest["reference"])
    if manifest.get("baseline"):
        # the library's current rig through the same renderer: the floor this comparison can reach
        # (coplanar shards of the generative surface z-fight differently with vertex order)
        manifest["library_rig_bind_vs_lod0"] = compare_images(manifest["baseline"], manifest["reference"])
    manifest["sheets"] = contact_sheets(manifest, renders, asset)
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
    return manifest


def compare_images(a_paths, b_paths):
    from PIL import Image
    out = {}
    for a, b in zip(a_paths, b_paths):
        ia = np.asarray(Image.open(a).convert("RGB"), dtype=np.int16)
        ib = np.asarray(Image.open(b).convert("RGB"), dtype=np.int16)
        diff = np.abs(ia - ib)
        out[os.path.basename(a)] = {"max_channel_delta": int(diff.max()),
                                    "mean_channel_delta": round(float(diff.mean()), 5),
                                    "pixels_differing": int((diff.max(2) > 2).sum())}
    return out


def contact_sheets(manifest, renders, asset):
    from PIL import Image, ImageDraw
    sheets = []
    tile = 512
    rows = [("bind (rigged, rest)", manifest["bind"]), ("LOD0 reference", manifest["reference"])]
    sheet = Image.new("RGB", (tile * 3, tile * 2 + 40), "white")
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 8), f"{asset}: bind pose (top) vs ready LOD0 (bottom); front / three-quarter / side", fill=(0, 0, 0))
    for r, (_label, paths) in enumerate(rows):
        for c, p in enumerate(paths):
            sheet.paste(Image.open(p).convert("RGB").resize((tile, tile)), (c * tile, 40 + r * tile))
    path = os.path.join(renders, f"sheet_{asset}_bind_vs_lod0.jpg")
    sheet.save(path, quality=90)
    sheets.append(path)
    if manifest.get("weights"):
        sheet = Image.new("RGB", (tile * len(manifest["weights"]), tile + 40), "white")
        draw = ImageDraw.Draw(sheet)
        draw.text((8, 8), f"{asset}: dominant bone per vertex (front / side / back); left side warm, "
                          f"right side cool, trunk grey, head yellow", fill=(0, 0, 0))
        for c, p in enumerate(manifest["weights"]):
            sheet.paste(Image.open(p).convert("RGB").resize((tile, tile)), (c * tile, 40))
        path = os.path.join(renders, f"sheet_{asset}_weights.jpg")
        sheet.save(path, quality=90)
        sheets.append(path)
    row_kinds = [("_front.png", "textured front"), ("_three_quarter.png", "textured three-quarter"),
                 ("_clay_front.png", "clay front"), ("_clay_three_quarter.png", "clay three-quarter"),
                 ("_clay_side.png", "clay side")]
    for stem, info in manifest["clips"].items():
        rows = []
        for suffix, _label in row_kinds:
            picked = [p for p in info["renders"] if p.endswith(suffix)
                      and not (suffix == "_front.png" and p.endswith("_clay_front.png"))
                      and not (suffix == "_three_quarter.png" and p.endswith("_clay_three_quarter.png"))]
            if picked:
                rows.append(picked)
        cols = len(info["frames"])
        sheet = Image.new("RGB", (tile * cols, tile * len(rows) + 40), "white")
        draw = ImageDraw.Draw(sheet)
        draw.text((8, 8), f"{stem} on {asset}: frames {info['frames']} at 30 fps; rows: "
                          + ", ".join(label for suffix, label in row_kinds[:len(rows)]), fill=(0, 0, 0))
        for r, paths in enumerate(rows):
            for c, p in enumerate(paths):
                sheet.paste(Image.open(p).convert("RGB").resize((tile, tile)), (c * tile, 40 + r * tile))
        path = os.path.join(renders, f"sheet_{stem}.jpg")
        sheet.save(path, quality=88)
        sheets.append(path)
    return sheets


# ------------------------------------------------------------------------------------------

def skeleton_json(asset, fit_blender, height):
    def gltf(p):
        return [round(p[0], 4), round(p[2], 4), round(-p[1], 4)]
    bones = []
    for name, parent in fit.BONES:
        b = fit_blender["bones"][name]
        bones.append({"name": name, "parent": parent, "head": gltf(b["head"]), "tail": gltf(b["tail"]),
                      "deform": b["deform"]})
    return {
        "skeleton_version": "npc_humanoid20_fitted_v1",
        "asset_id": asset,
        "units": "metres",
        "export_frame": "glTF: Y up, +Z forward, base on the ground plane",
        "height_m": height,
        "bone_count": len(bones),
        "deform_bone_count": sum(1 for b in bones if b["deform"]),
        "bone_names_as": CONTROL,
        "fit": "measured landmarks (_blender_rig_fit_humanoid20.py)",
        "bones": bones,
    }


def process(asset, skip_render=False, frames=5):
    outdir = os.path.join(STAGING, asset)
    os.makedirs(outdir, exist_ok=True)
    print(f"== {asset}")
    fitted = blender(FIT_TOOL, "--", "--mode", "fit", "--input",
                     os.path.join(ASSETS, "ready", asset, f"{asset}.glb"),
                     "--outdir", outdir, "--name", asset)
    result = tagged(fitted.stdout, "FIT_RESULT")
    if result is None:
        print((fitted.stdout or "")[-2000:], (fitted.stderr or "")[-2000:])
        raise SystemExit(f"fit failed for {asset}")
    rigged_path = result["out"]
    fit_json = os.path.join(outdir, f"{asset}_fit_blender.json")
    with open(fit_json, encoding="utf-8") as handle:
        fit_blender = json.load(handle)
    os.remove(fit_json)

    verify = verify_rig(asset, rigged_path, fit_blender["bones"])
    print(f"   worst deform joint {verify['worst_deform_joint']} effective "
          f"{verify['worst_deform_effective_m']} m (tolerance {verify['tolerance_m']}); "
          f"outside {verify['deform_joints_outside_tolerance']}; empty limbs "
          f"{verify['limb_bones_dominating_nothing']}")
    clips = build_clips(asset, rigged_path, outdir)
    for cid, c in clips.items():
        print(f"   {cid}: {'ok' if c.get('ok') else 'FAIL'} {c.get('clip_file_check', c.get('problem'))}")
    renders = None
    if not skip_render:
        renders = render(asset, rigged_path, [c["glb"] for c in clips.values() if c.get("ok")], outdir, frames)
        print(f"   renders: {len(renders.get('sheets', []))} sheets; bind vs lod0 "
              f"{renders.get('bind_vs_lod0')}")
        if ASSET_CLIPS[asset]["family"] == "npc":
            # the control, unchanged: the library's rig with the shared clips Kera's are cut from
            shared = [os.path.join(ASSETS, "animation", "ready", "npc", f"anim.npc.{kind}.glb")
                      for kind in ASSET_CLIPS[asset]["kinds"]]
            control = render(CONTROL, CONTROL_RIG,
                             shared, outdir, frames, subdir=os.path.join("renders", f"control_{CONTROL}"))
            renders["control_sheets"] = control.get("sheets", [])

    before = before_numbers(asset)
    report = {
        "asset": asset,
        "staged_rig": rigged_path,
        "bone_names_and_hierarchy_as": CONTROL,
        "fit": {k: fit_blender[k] for k in ("measured", "notes")},
        "bones": fit_blender["bones"],
        "weighting": fit_blender["weighting"],
        "verification": verify,
        "before": {k: v for k, v in before.items() if k != "joints"},
        "before_joints": before["joints"],
        "clips": clips,
        "renders": renders,
    }
    with open(os.path.join(outdir, f"{asset}_fit_report.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    rig_json = {
        "asset": asset, "plan": "humanoid", "method": fit_blender["weighting"]["method"],
        "fit": "measured-landmarks", "out": rigged_path,
        "bones": verify["structure"]["bone_count"],
        "bones_with_weights": sum(1 for v in verify["weights"]["influenced_vertices"].values() if v),
        "vertices": verify["geometry"]["rigged_vertices"],
        "unweighted_vertices": verify["weights"]["unweighted_vertices"],
        "unweighted_percent": round(100.0 * verify["weights"]["unweighted_vertices"]
                                    / max(verify["geometry"]["rigged_vertices"], 1), 2),
        "max_influences": verify["weights"]["max_influences"],
        "max_joint_distance_m": verify["worst_deform_effective_m"],
    }
    with open(os.path.join(outdir, f"{asset}_rig.json"), "w", encoding="utf-8") as handle:
        json.dump(rig_json, handle, indent=2)
    with open(os.path.join(outdir, f"{asset}_rigged_skeleton.json"), "w", encoding="utf-8") as handle:
        json.dump(skeleton_json(asset, fit_blender, verify["height_m"]), handle, indent=2)
    return report


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("assets", nargs="+", choices=sorted(ASSET_CLIPS))
    parser.add_argument("--skip-render", action="store_true")
    parser.add_argument("--frames", type=int, default=5)
    args = parser.parse_args()
    control = control_numbers()
    print(f"control {CONTROL}: fitted vs library joints max {control['fitted_vs_library_max_m']} m, "
          f"mean {control['fitted_vs_library_mean_m']} m")
    for asset in args.assets:
        report = process(asset, args.skip_render, args.frames)
        report["control"] = control
        path = os.path.join(STAGING, asset, f"{asset}_fit_report.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
