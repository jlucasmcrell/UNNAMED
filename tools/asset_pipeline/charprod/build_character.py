"""The character production pipeline, end to end, from a character spec (specs/<name>.json): a fitted, skinned
reconstruction and its concept in; a production character out (assets/rigged/<id>/<id>_rigged.glb) - the enclosed
inner shell gone, a high-detail head grafted on, real eyes, a face-priority atlas carrying the concept's own pixels,
material zones, a production rig with fingers, an authored LOD chain - and review renders.

Each stage writes into the spec's work folder and is skipped when its output is there already (a run resumes where it
stopped); --from STAGE redoes that stage and every one after it. GPU stages go to the ComfyUI rig (mask, head
reconstruction) and to the rig's embedded Python (zone segmentation); the geometry runs in Blender.

    python build_character.py specs/player.json [--from graft] [--only project]

Spec (paths under the asset root unless absolute):
    id, rigged (the humanoid20-fitted body) - or body_concept (+ body_name, height, arm_reach) to reconstruct and fit one -,
    concept, work, seed, material_prefix,
    head_crop (fraction of the figure's height the head crop spans), cut_below_chin, zones (segment_zones.py's list),
    atlas, lod_ratios, uv_margin
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PIPE = os.path.dirname(HERE)
ASSETS = os.path.normpath(os.path.join(PIPE, "..", "..", "assets"))
BLENDER = os.environ.get("UNNAMED_BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
RIG_PYTHON = os.environ.get("UNNAMED_RIG_PYTHON", r"G:\ComfyUI_LTX25\python_embeded\python.exe")
MODELS = os.path.join(ASSETS, "_staging", "charprod", "models")
COMFY_ENV = {
    "UNNAMED_COMFY_SERVER": os.environ.get("UNNAMED_COMFY_SERVER", "http://127.0.0.1:8190"),
    "UNNAMED_COMFY_OUTPUT": os.environ.get("UNNAMED_COMFY_OUTPUT", r"G:\ComfyUI_LTX25\ComfyUI\output"),
    "UNNAMED_3D_TEMPLATE": os.environ.get("UNNAMED_3D_TEMPLATE", r"G:\ComfyUI_LTX25\python_embeded\Lib\site-packages"
                                          r"\comfyui_workflow_templates_json\templates\3d_pixal3d_trellis2_image_to_model.json"),
}

STAGES = ["body_recon", "body_clean", "body_fit", "concept", "mask", "camera", "head_crop", "landmarks", "head_recon", "head_camera", "clean", "graft", "eyes",
          "layout", "maps", "bake", "project", "zones", "zone_maps", "eye_texture", "assemble", "hands", "rig", "lods",
          "install", "renders"]


def path(p):
    return p if os.path.isabs(p) else os.path.join(ASSETS, p)


def run(cmd, log, env=None, cwd=HERE):
    with open(log, "w", encoding="utf-8") as fh:
        r = subprocess.run(cmd, stdout=fh, stderr=subprocess.STDOUT, cwd=cwd, env={**os.environ, **(env or {})})
    text = open(log, encoding="utf-8", errors="replace").read()
    if r.returncode != 0 or "Traceback" in text:
        raise SystemExit(f"stage failed ({' '.join(os.path.basename(str(c)) for c in cmd[:4])}...): see {log}\n{text[-2500:]}")
    return text


def blender(script, args, log):
    return run([BLENDER, "--background", "--factory-startup", "--python", script if os.path.isabs(script) else os.path.join(HERE, script), "--"] + args, log)


def extract_maps(glb_path, out_dir, material=0):
    sys.path.insert(0, HERE)
    from glb import Glb
    os.makedirs(out_dir, exist_ok=True)
    g = Glb(glb_path)
    m = g.json["materials"][material]
    p = m["pbrMetallicRoughness"]
    for name, idx in (("albedo", p["baseColorTexture"]["index"]), ("orm", p["metallicRoughnessTexture"]["index"]),
                      ("normal", m["normalTexture"]["index"])):
        g.texture_image(idx).convert("RGB").save(os.path.join(out_dir, name + ".png"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("spec")
    ap.add_argument("--from", dest="start", choices=STAGES)
    ap.add_argument("--only", choices=STAGES)
    a = ap.parse_args()
    spec = json.load(open(a.spec))
    W = path(spec["work"])
    os.makedirs(os.path.join(W, "logs"), exist_ok=True)
    os.makedirs(os.path.join(W, "bake"), exist_ok=True)
    os.makedirs(os.path.join(W, "renders"), exist_ok=True)
    f = lambda name: os.path.join(W, name)  # noqa: E731
    log = lambda name: os.path.join(W, "logs", name + ".log")  # noqa: E731
    redo = set(STAGES[STAGES.index(a.start):]) if a.start else set()
    if a.only:
        redo = {a.only}

    def stage(name, outputs):
        if a.only and name != a.only:
            return False
        if name in redo:
            return True
        return not all(os.path.exists(f(o)) for o in outputs)

    rigged = path(spec["rigged"]) if spec.get("rigged") else f("body/rigged/body_rigged.glb")
    prefix = spec.get("material_prefix", "MAT_" + spec["id"])

    # A body from a (new) concept: the full-body reconstruction at the hq tier, the project's cleanup to the character's
    # height, the humanoid20 fit. (Renn and Tavar: the Phase-A bodies fused their sleeves and coat into the torso.)
    if spec.get("body_concept"):
        name = spec.get("body_name", "body")
        body = f("body")
        os.makedirs(body, exist_ok=True)
        if stage("body_recon", ["body/raw.glb"]):
            text = run([sys.executable, "-u", os.path.join(PIPE, "_run_3d_asset.py"), path(spec["body_concept"]), "--seed", str(spec.get("seed", 888)),
                        "--faces", "40000", "--texture-size", "4096", "--bake-resolution", "4096", "--steps", "30",
                        "--upsample-resolution", "1024", "--ao-samples", "256", "--prefix", f"3d/raw/charprod_{spec['id']}_body"],
                       log("body_recon"), COMFY_ENV, cwd=PIPE)
            art = [ln.split()[1] for ln in text.splitlines() if ln.startswith("ARTIFACT") and ln.split()[1].endswith(".glb")]
            if not art:
                raise SystemExit(f"body reconstruction produced no GLB: see {log('body_recon')}")
            shutil.copy(art[-1], f("body/raw.glb"))
        if stage("body_clean", [f"body/ready/{name}.glb"]):
            blender(os.path.join(PIPE, "_blender_cleanup.py"), ["--input", f("body/raw.glb"), "--outdir", f("body/ready"), "--name", name,
                                                                "--category", "character", "--target-size", str(spec.get("height", 1.8)),
                                                                "--lod-faces", ""], log("body_clean"))
        if stage("body_fit", ["body/rigged/body_rigged.glb"]):
            fit = [os.path.join(PIPE, "_blender_rig_fit_humanoid20.py"), ["--mode", "fit", "--input", f(f"body/ready/{name}.glb"),
                   "--outdir", f("body/rigged"), "--name", name] + (["--arm-reach", str(spec["arm_reach"])] if spec.get("arm_reach") else [])]
            blender(fit[0], fit[1], log("body_fit"))
            if name != "body":
                shutil.copy(f(f"body/rigged/{name}_rigged.glb"), f("body/rigged/body_rigged.glb"))

    if stage("concept", ["concept.png"]):
        shutil.copy(path(spec["concept"]), f("concept.png"))
    if stage("mask", ["concept_mask.png"]):
        run([sys.executable, "figure_mask.py", f("concept.png"), f("concept_mask.png")], log("mask"), COMFY_ENV)
    if stage("camera", ["camera.json"]):
        print(run([sys.executable, "fit_camera.py", rigged, f("concept_mask.png"), f("camera.json"), "--overlay", f("concept.png"),
                   f("camera_overlay.jpg")], log("camera")).strip().splitlines()[-1])
    if stage("head_crop", ["head_crop.png", "head_crop.json"]):
        # The head: the top of the figure, centred on the mask's top band (a face in a full-body concept can be too small
        # for the landmark detector; it runs on this upscaled crop).
        m = np.asarray(Image.open(f("concept_mask.png")).convert("L")) > 127
        ys, xs = np.nonzero(m)
        height = ys.max() - ys.min()
        top = ys.min()
        size = int(round(spec.get("head_crop", 0.27) * height))
        band = m[top:top + int(0.08 * height)]
        cx = int(round(np.nonzero(band.any(0))[0].mean()))
        x0, y0 = cx - size // 2, max(0, top - int(0.015 * height))
        Image.open(f("concept.png")).convert("RGB").crop((x0, y0, x0 + size, y0 + size)).resize((1024, 1024), Image.LANCZOS).save(f("head_crop.png"))
        json.dump({"x0": int(x0), "y0": int(y0), "size": size, "out": 1024}, open(f("head_crop.json"), "w"))
    if stage("landmarks", ["landmarks.json", "landmarks_headcrop.json"]):
        run([sys.executable, "landmarks.py", f("head_crop.png"), os.path.join(MODELS, "face_landmarker.task"), f("landmarks.json"),
             "--crop", f("head_crop.json")], log("landmarks"))
        run([sys.executable, "landmarks.py", f("head_crop.png"), os.path.join(MODELS, "face_landmarker.task"), f("landmarks_headcrop.json")],
            log("landmarks_headcrop"))
    if stage("head_recon", ["head_raw.glb"]):
        prefix3d = f"3d/raw/charprod_{spec['id']}_head"
        text = run([sys.executable, "-u", os.path.join(PIPE, "_run_3d_asset.py"), f("head_crop.png"), "--seed", str(spec.get("seed", 888)),
                    "--faces", "80000", "--texture-size", "2048", "--bake-resolution", "4096", "--steps", "30",
                    "--upsample-resolution", "1024", "--ao-samples", "128", "--prefix", prefix3d], log("head_recon"), COMFY_ENV, cwd=PIPE)
        art = [ln.split()[1] for ln in text.splitlines() if ln.startswith("ARTIFACT") and ln.split()[1].endswith(".glb")]
        if not art:
            raise SystemExit(f"head reconstruction produced no GLB: see {log('head_recon')}")
        shutil.copy(art[-1], f("head_raw.glb"))
    if stage("head_camera", ["head_camera.json"]):
        run([sys.executable, "figure_mask.py", f("head_crop.png"), f("head_crop_mask.png")], log("head_mask"), COMFY_ENV)
        print(run([sys.executable, "fit_camera.py", f("head_raw.glb"), f("head_crop_mask.png"), f("head_camera.json")],
                  log("head_camera")).strip().splitlines()[-1])
    if stage("clean", ["clean.glb"]):
        blender("clean_mesh.py", ["--input", rigged, "--out", f("clean.glb"), "--report", f("clean.json")], log("clean"))
    if stage("graft", ["grafted.glb", "head_camera_world.json"]):
        blender("head_graft.py", ["--body", f("clean.glb"), "--head", f("head_raw.glb"), "--camera", f("camera.json"),
                                  "--head-camera", f("head_camera.json"), "--landmarks", f("landmarks.json"), "--crop", f("head_crop.json"),
                                  "--out", f("grafted.glb"), "--head-camera-out", f("head_camera_world.json"), "--report", f("graft.json"),
                                  "--cut-below-chin", str(spec.get("cut_below_chin", 0.035))], log("graft"))
    if stage("eyes", ["grafted_eyes.glb", "eyeballs.glb"]):
        blender("eyes.py", ["--input", f("grafted.glb"), "--camera", f("head_camera_world.json"), "--landmarks", f("landmarks_headcrop.json"),
                            "--out", f("grafted_eyes.glb"), "--eyes-out", f("eyeballs.glb"), "--report", f("eyes.json")], log("eyes"))
    if stage("layout", ["uv_layout.glb"]):
        blender("uv_layout.py", ["--input", f("grafted_eyes.glb"), "--camera", f("camera.json"), "--out", f("uv_layout.glb"),
                                 "--report", f("uv_layout.json"), "--margin", str(spec.get("uv_margin", 0.0015))], log("layout"))
    if stage("maps", ["src_maps/albedo.png", "head_maps/albedo.png"]):
        extract_maps(rigged, f("src_maps"))
        extract_maps(f("head_raw.glb"), f("head_maps"))
    if stage("bake", ["bake/albedo_transfer.png", "bake/normal_transfer.png", "bake/orm_transfer.png"]):
        blender("bake_transfer.py", ["--input", f("uv_layout.glb"), "--outdir", f("bake"), "--size", str(spec.get("atlas", 4096)),
                                     "--source", "body_src", f("src_maps/albedo.png"), f("src_maps/normal.png"), f("src_maps/orm.png"),
                                     "--source", "head_src", f("head_maps/albedo.png"), f("head_maps/normal.png"), f("head_maps/orm.png")],
                log("bake"))
    if stage("project", ["bake/albedo.png"]):
        run([sys.executable, "project.py", "--mesh", f("uv_layout.glb"), "--albedo", f("bake/albedo_transfer.png"), "--concept", f("concept.png"),
             "--mask", f("concept_mask.png"), "--camera", f("camera.json"), "--view", "1", f("head_crop.png"), f("head_crop_mask.png"),
             f("head_camera_world.json"), "--out", f("bake/albedo.png"), "--weights", f("bake/proj_weights.png"),
             "--size", str(spec.get("atlas", 4096))], log("project"))
    if stage("zones", ["zones_mesh.json"]):
        json.dump({"zones": spec["zones"]}, open(f("zones.json"), "w"), indent=1)
        run([RIG_PYTHON, "segment_zones.py", f("concept.png"), f("zones.json"), f("zones_concept.png"), "--preview",
             f("zones_concept_preview.jpg"), "--mask", f("concept_mask.png")], log("segment"))
        run([sys.executable, "zones_to_mesh.py", "--mesh", f("uv_layout.glb"), "--labels", f("zones_concept.png"), "--zones", f("zones.json"),
             "--camera", f("camera.json"), "--view", "1", f("head_camera_world.json"), f("head_crop.json"), "--mask", f("concept_mask.png"),
             "--out", f("zones_mesh.json")],
            log("zones_to_mesh"))
    if stage("zone_maps", ["bake/orm.png", "bake/albedo_final.png"]):
        run([sys.executable, "orm_zones.py", "--mesh", f("uv_layout.glb"), "--zones", f("zones_mesh.json"), "--orm", f("bake/orm_transfer.png"),
             "--out", f("bake/orm.png"), "--albedo", f("bake/albedo.png"), "--albedo-out", f("bake/albedo_final.png"),
             "--concept", f("concept.png"), "--labels", f("zones_concept.png"), "--weights", f("bake/proj_weights.png"),
             "--report", f("orm_zones.json")], log("zone_maps"))
    if stage("eye_texture", ["bake/eye.png"]):
        run([sys.executable, "eye_texture.py", f("head_crop.png"), f("landmarks_headcrop.json"), f("bake/eye.png")], log("eye_texture"))
    if stage("assemble", ["assembled.glb"]):
        blender("assemble.py", ["--input", f("uv_layout.glb"), "--albedo", f("bake/albedo_final.png"), "--normal", f("bake/normal_transfer.png"),
                                "--orm", f("bake/orm.png"), "--out", f("assembled.glb"), "--name", prefix, "--zones", f("zones_mesh.json"),
                                "--eyes", f("eyeballs.glb"), "--eye-texture", f("bake/eye.png")], log("assemble"))
    if stage("hands", ["hands.json"]):
        run([sys.executable, "hand_landmarks.py", f("concept.png"), f("concept_mask.png"), os.path.join(MODELS, "hand_landmarker.task"),
             f("hands.json"), "--preview", f("hands_preview.png")], log("hands"))
    if stage("rig", ["prod_rigged.glb"]):
        blender("rig_production.py", ["--input", f("assembled.glb"), "--camera", f("camera.json"), "--hands", f("hands.json"),
                                      "--out", f("prod_rigged.glb"), "--report", f("rig.json")], log("rig"))
    if stage("lods", ["final_rigged.glb"]):
        blender("lods.py", ["--input", f("prod_rigged.glb"), "--out", f("final_rigged.glb"), "--ratios", spec.get("lod_ratios", "0.3,0.09"),
                            "--report", f("lods.json")], log("lods"))
    target = os.path.join(ASSETS, "rigged", spec["id"], spec["id"] + "_rigged.glb")
    if stage("install", []) or not os.path.exists(target) or os.path.getmtime(target) < os.path.getmtime(f("final_rigged.glb")):
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copy(f("final_rigged.glb"), target)
        print(f"INSTALLED {target}")
    if stage("renders", ["renders/final_face.png"]):
        blender("render_views.py", ["--input", f("final_rigged.glb"), "--outdir", f("renders"), "--stem", "final",
                                    "--views", "face,face34,body,back", "--size", "800", "--samples", "48"], log("renders"))
    print("BUILD_CHARACTER_DONE " + spec["id"])


if __name__ == "__main__":
    main()
