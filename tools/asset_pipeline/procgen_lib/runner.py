"""run_template: everything a template does not do itself, so a template file is only geometry plus parameters.

    parse Blender argv -> reuse gate -> seed -> build -> centre -> validate (builder) -> pack UVs (bands checked)
    -> mesh -> triangulate/normals -> geometry gate, UV and triangle checks, clashes -> bake (or flat colours)
    -> child nodes and sockets -> GLB -> glb_gate + _verify_glb.py -> provenance -> optional review render

A template is a module with PARAMS and build(b, P, rng) that ends with

    if __name__ == "__main__":
        run_template(build, PARAMS, "<asset_id>", archetype="<archetype>", concept="assets/concepts/<id>.png")

build(b, P, rng) adds parts to the Builder b (see primitives) in Blender Z-up, front toward -Y, and returns a dict:
    materials   [materials.Material]  indexed by the slot numbers the parts use (required)
    nodes       {node name: {"group": part group, "pivot": (x, y, z)}}   groups split into child nodes (lid, door)
    sockets     [{"name": "SOCK_...", "parent": asset_id or a node name, "at": (x, y, z), "basis": 3x3 or None}]
    bake_lift   {group: dz}           groups lifted clear during the bake (the inside of a box is baked open)
    no_clash    [(group, group)]      group pairs whose triangles must not overlap
    notes, info, frame                free text / values recorded in the provenance
    emissive_strength  float          glow of the baked emissive map (recipes that return 'emit'; default 1)
    vertex_colours     (n_verts, 3)   linear rgb per Builder vertex, multiplied into the base colour (COLOR_0)
    tiled              {"tile_m": T, "material": Material}   a large surface: no atlas; the faces' corner UVs are
                                      taken as world-scale metres and divided by T, and the material is baked once as
                                      a seamless tile (bake.bake_tile; a periodic recipe on the Graph's U, V)
Coordinates in nodes/sockets are in build space; the runner shifts them with the model (centred on X/Y, lowest
point at z = 0).

Command line (after Blender's "--"): --out-dir DIR (default assets/_staging/procgen/<id>) --work-dir DIR --seed N
--res 2048 --samples 32 --device auto|cpu --no-bake --review [--review-size 1536] --param key=json (dotted keys
reach nested params; repeatable) --tokens N --wall-minutes M (production cost of this asset, for the cost rule).
The last stdout line is "PROCGEN_RESULT <provenance path>".

API
    run_template(build_fn, params, asset_id, *, archetype=None, concept=None, tri_budget=20000, res=2048,
                 samples=32, px_per_m=512, margin_px=3, bands=None, band_of=None, ao_distance=0.35,
                 self_test=False, argv=None, instance_files=None) -> provenance dict
        instance_files: a template instance's own files (its parameter JSON, any instance-only code); when given,
        cost.asset_specific_lines counts them and cost.template_code_lines records the template
    parse_args(argv, defaults) -> argparse.Namespace;  apply_overrides(params, ["a.b=1", ...]) -> params
"""
import argparse
import copy
import datetime
import json
import os
import subprocess
import sys
import tempfile
import time

import numpy as np

from . import bake, export, meshbuild, uv, validate

VERIFY_GLB = os.path.join(export.TOOLS, "_verify_glb.py")
REVIEW_BATCH = os.path.join(export.TOOLS, "_review_render_batch.py")


def log(msg):
    print(f"[procgen] {msg}", flush=True)


def parse_args(argv, defaults):
    argv = argv if argv is not None else (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", default=defaults["out_dir"])
    ap.add_argument("--work-dir", default=defaults["work_dir"])
    ap.add_argument("--seed", type=int, default=defaults["seed"])
    ap.add_argument("--res", type=int, default=defaults["res"])
    ap.add_argument("--samples", type=int, default=defaults["samples"])
    ap.add_argument("--device", default="auto", choices=("auto", "cpu"))
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--review", action="store_true")
    ap.add_argument("--review-size", type=int, default=1536)
    ap.add_argument("--param", action="append", default=[])
    ap.add_argument("--tokens", type=int, default=None)
    ap.add_argument("--wall-minutes", type=float, default=None)
    return ap.parse_args(argv)


def apply_overrides(params, overrides):
    P = copy.deepcopy(params)
    for item in overrides:
        key, _, raw = item.partition("=")
        try:
            value = json.loads(raw)
        except json.JSONDecodeError:
            value = raw
        d = P
        parts = key.split(".")
        for k in parts[:-1]:
            d = d[k]
        if parts[-1] not in d:
            raise SystemExit(f"--param {key}: no such parameter")
        d[parts[-1]] = value
    return P


def _rel(path):
    try:
        return os.path.relpath(path, export.REPO).replace("\\", "/")
    except ValueError:
        return path


def run_template(build_fn, params, asset_id, *, archetype=None, concept=None, tri_budget=20000, res=2048,
                 samples=32, px_per_m=512, margin_px=3, bands=None, band_of=None, ao_distance=0.35, self_test=False,
                 argv=None, instance_files=None):
    import bpy
    t_start = time.time()
    template = os.path.abspath(build_fn.__code__.co_filename)
    args = parse_args(argv, {"out_dir": export.default_out_dir(asset_id),
                             "work_dir": os.path.join(tempfile.gettempdir(), asset_id + "_procgen"),
                             "seed": int(params.get("seed", 2609)), "res": res, "samples": samples})
    out_dir = export.guard_output(args.out_dir, asset_id)
    work_dir = os.path.abspath(args.work_dir)
    if not self_test:
        sys.path.insert(0, export.TOOLS)
        from _reuse_gate import require
        log(require(asset_id, "procedural"))
    P = apply_overrides(params, args.param)
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(work_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)

    # ---- build
    rng = np.random.default_rng(args.seed)
    b = meshbuild.Builder(rng)
    spec = build_fn(b, P, rng)
    materials = spec["materials"]
    if max(b.fslot) >= len(materials):
        raise SystemExit(f"a part uses material slot {max(b.fslot)} but only {len(materials)} materials are given")
    lo, hi = b.bounds()
    shift = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])
    b.shift(shift)
    lo, hi = b.bounds()
    dims = hi - lo
    log(f"built {len(b.parts)} parts, {len(b.faces)} faces, {dims.round(4).tolist()} m")

    # ---- validate the parts, pack the atlas
    topo = validate.validate_builder(b)
    log(f"TOPOLOGY {json.dumps({k: v for k, v in topo.items() if k != 'examples'})}")
    if not topo["ok"]:
        raise SystemExit(f"builder validation failed: {topo}")
    t_pack = time.time()
    tiled = spec.get("tiled")
    if tiled:
        loop_uv = np.concatenate([np.asarray(f, np.float64) for f in b.fuv]) / float(tiled["tile_m"])
        pack = {"mode": "tiled", "tile_m": tiled["tile_m"], "px_per_m": round(args.res / tiled["tile_m"], 1),
                "islands": len(b.islands()), "band_check": {"ok": True, "tiled": True}}
    else:
        loop_uv, pack = uv.pack(b, args.res, px_per_m, margin_px, bands, band_of)
    uvf = validate.uv_face_checks(b, loop_uv, args.res)
    log(f"packed {pack['islands']} islands at {pack['px_per_m']} px/m in {time.time() - t_pack:.1f} s; {uvf}")
    if uvf["uv_zero_area_faces"]:
        raise SystemExit(f"zero-area UV faces: {uvf}")
    clashes = validate.clash_report(b, [tuple(p) for p in spec.get("no_clash", [])]) if spec.get("no_clash") else {}
    if any(clashes.values()):
        raise SystemExit(f"parts clash: {clashes}")

    # ---- mesh
    obj = meshbuild.make_object(b, asset_id, loop_uv)
    vcol = spec.get("vertex_colours")
    if vcol is not None:
        meshbuild.set_colours(obj, vcol)
    meshbuild.finish_topology(obj)
    tris = len(obj.data.polygons)
    gate = validate.geometry_gate(obj)
    a3, a2 = validate.mesh_checks(obj.data)
    mesh_report = {"triangles": tris, "degenerate_triangles": int((a3 < 1e-10).sum()),
                   "uv_zero_area_triangles": int((a2 < 1e-12).sum()),
                   "uv_smallest_triangle_px2": round(float(a2.min() * args.res ** 2), 4),
                   "px_per_m_measured": uv.builder_density(b, loop_uv, args.res)}
    log(f"GEOMETRY_GATE {json.dumps({k: v for k, v in gate.items() if k != 'examples'})}; {mesh_report}")
    if not gate["ok"] or mesh_report["degenerate_triangles"] or mesh_report["uv_zero_area_triangles"]:
        raise SystemExit(f"mesh gate failed: {gate} {mesh_report}")
    if tris > tri_budget:
        raise SystemExit(f"{tris} triangles over the budget {tri_budget}")

    # ---- bake
    lifts = {b.groups.index(g): dz for g, dz in spec.get("bake_lift", {}).items() if g in b.groups}
    bake_stats, textures = None, {}
    if args.no_bake:
        export.flat_materials(obj, materials)
    else:
        if tiled:
            paths, bake_stats = bake.bake_tile(tiled["material"], args.res, args.samples, args.device, work_dir,
                                               asset_id + "_tile", seed=args.seed, size_m=tiled["tile_m"])
        else:
            paths, bake_stats = bake.bake_maps(obj, materials, args.res, args.samples, args.device, work_dir,
                                               asset_id, seed=args.seed, lifts=lifts, ao_distance=ao_distance)
        export.final_material([obj], paths, f"MAT_{asset_id}", emissive_strength=spec.get("emissive_strength", 1.0),
                              vertex_colour="Col" if vcol is not None else None)
        textures = {k: os.path.basename(v) for k, v in paths.items()}

    # ---- nodes and sockets
    objects = {asset_id: obj}
    nodes_out = {asset_id: "root"}
    for name, nd in spec.get("nodes", {}).items():
        pivot = np.asarray(nd["pivot"], np.float64) + shift
        objects[name] = meshbuild.split_group(obj, b.groups.index(nd["group"]), name, pivot)
        nodes_out[name] = {"parent": asset_id, "group": nd["group"], "origin_gltf": export.gltf_vec(pivot)}
    for o in objects.values():
        meshbuild.remove_attributes(o)
    for s in spec.get("sockets", []):
        parent = objects[s.get("parent", asset_id)]
        at = np.asarray(s["at"], np.float64) + shift - np.asarray(parent.location)
        export.add_socket(parent, s["name"], at, basis=s.get("basis"))
        nodes_out[s["name"]] = {"parent": s.get("parent", asset_id),
                                "position_gltf": export.gltf_vec(np.asarray(s["at"]) + shift)}
        if s.get("basis") is not None:
            cols = np.asarray(s["basis"], np.float64).T
            nodes_out[s["name"]].update({"x_gltf": export.gltf_vec(cols[0]), "y_gltf": export.gltf_vec(cols[2])})

    # ---- export and check the GLB as the engine reads it
    glb = os.path.join(out_dir, asset_id + ".glb")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work_dir, asset_id + ".blend"))
    export.export_glb(glb)
    glb_report = validate.glb_gate(glb)
    verify = subprocess.run([sys.executable, VERIFY_GLB, glb], capture_output=True, text=True)
    verify_lines = verify.stdout.strip().splitlines()
    log("VERIFY_GLB " + (verify_lines[-1].strip() if verify_lines else verify.stderr.strip()[-200:]))
    if not glb_report["ok"] or (verify.returncode != 0 and not args.no_bake):   # --no-bake has no maps to verify
        raise SystemExit(f"GLB checks failed: {glb_report} / {verify.stdout} {verify.stderr}")

    # ---- provenance
    concept_abs = os.path.join(export.REPO, concept) if concept else None
    lines = export.code_lines(template)
    if instance_files:      # a template instance: its cost is its parameter file(s), the template is the archetype's
        inst = sum(sum(1 for ln in open(f, encoding="utf-8") if ln.strip()) if not f.endswith(".py")
                   else export.code_lines(f) for f in instance_files)
    prov = {
        "asset_id": asset_id,
        "method": "procedural (procgen_lib template)",
        "archetype": archetype,
        "template": _rel(template),
        "self_test": self_test or None,
        "script": _rel(template),
        "script_sha256": export.sha256_file(template),
        "library": "tools/asset_pipeline/procgen_lib",
        "library_sha256": export.library_sha256(),
        "command": f"blender --background --factory-startup --python {_rel(template)} -- --seed {args.seed} "
                   f"--res {args.res} --samples {args.samples}" + (" --no-bake" if args.no_bake else "")
                   + "".join(f" --param {p}" for p in args.param),
        "blender": bpy.app.version_string,
        "seed": args.seed,
        "date": datetime.datetime.now().isoformat(timespec="seconds"),
        "concept": concept,
        "concept_sha256": export.sha256_file(concept_abs) if concept_abs and os.path.exists(concept_abs) else None,
        "parameters": P,
        "design_notes": spec.get("notes", []),
        "info": spec.get("info"),
        "nodes": nodes_out,
        "frame": spec.get("frame", "glTF +Y up, front +Z, centred on X/Z, lowest point y = 0"),
        "dimensions_m": {"x": round(float(dims[0]), 4), "y_height": round(float(dims[2]), 4),
                         "z": round(float(dims[1]), 4)},
        "triangles": tris,
        "triangle_budget": tri_budget,
        "checks": {"builder": topo, "uv_faces": uvf, "uv_bands": pack["band_check"], "clashes": clashes,
                   "geometry_gate": gate, "mesh": mesh_report, "glb_gate": glb_report,
                   "verify_glb": verify_lines[-1].strip() if verify_lines else None},
        "materials": {"slots": [m.describe() for m in materials], "material": f"MAT_{asset_id}",
                      "single_sided": True, "atlas": None if tiled else args.res,
                      "tile": {"tile_m": tiled["tile_m"], "res": args.res, "recipe": tiled["material"].describe()}
                      if tiled else None,
                      "uv": {k: v for k, v in pack.items() if k != "band_check"},
                      "maps": "base colour (sRGB, JPEG q90), normal (tangent, OpenGL +Y, PNG), ORM (R occlusion, "
                              "G roughness, B metallic; JPEG q90)"
                              + ("; emissive (sRGB, JPEG q90)" if "emissive" in textures else ""),
                      "emissive_strength": spec.get("emissive_strength", 1.0) if "emissive" in textures else None,
                      "vertex_colours": "COLOR_0 multiplies the base colour" if vcol is not None else None,
                      "textures": textures, "bake": bake_stats},
        "outputs": [asset_id + ".glb", asset_id + "_provenance.json"],
        "glb_bytes": os.path.getsize(glb),
        "glb_sha256": export.sha256_file(glb),
        "cost": {"asset_specific_lines": inst if instance_files else lines, "agent_tokens": args.tokens,
                 "wall_minutes": args.wall_minutes,
                 "template_code_lines": lines if instance_files else None,
                 "instance_files": [_rel(f) for f in instance_files] if instance_files else None,
                 "note": ("asset_specific_lines = non-blank lines of the instance files (template_code_lines is the "
                          "archetype's cost)" if instance_files else
                          "asset_specific_lines = code lines of the template file (procgen_lib excluded)")
                         + "; tokens and minutes are the production effort, supplied with --tokens/--wall-minutes"},
        "build_seconds": round(time.time() - t_start, 1),
    }
    prov_path = export.write_provenance(os.path.join(out_dir, asset_id + "_provenance.json"), prov)
    log(f"wrote {glb} ({tris} tris, {prov['glb_bytes'] / 1e6:.2f} MB, dims {prov['dimensions_m']})")
    if args.review:
        cmd = [sys.executable, REVIEW_BATCH, glb, "--out-root", os.path.join(out_dir, "review"),
               "--size", str(args.review_size), "--passes", "all"]
        log("review: " + " ".join(cmd))
        subprocess.run(cmd)
    print(f"PROCGEN_RESULT {prov_path}", flush=True)
    return prov
