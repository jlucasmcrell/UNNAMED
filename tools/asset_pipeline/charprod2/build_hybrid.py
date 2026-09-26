"""The hybrid player end to end (charprod2): the MPFB base (mpfb_base.py, run once) dressed, its head wrapped onto the source
character's, the source registered and baked across, its face projected, the textures composited and the result assembled.

    python build_hybrid.py --spec specs/player.json --work <dir> --source source.glb --skin <MPFB skin diffuse .png>
                           [--blender <blender.exe>] [--from assemble|wrap|fit|bake|face|compose|finish|keys]

<dir> holds mpfb_base.blend and tex/ (the dyed fallbacks, garment_textures.py). Writes assembled.blend, wrapped.blend, fitted.blend,
facefit/, bake/, tex_hybrid/, hybrid.blend.
"""
import argparse
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
STAGES = ["assemble", "wrap", "fit", "bake", "face", "compose", "finish", "keys"]


def run(cmd):
    print(">", " ".join(cmd), flush=True)
    r = subprocess.run(cmd, capture_output=True, text=True)
    tail = "\n".join(line for line in (r.stdout + r.stderr).splitlines()
                     if line[:1].isalpha() and line.split(" ")[0].replace("_", "").isupper() or "Error" in line or "Traceback" in line)
    print(tail, flush=True)
    if r.returncode != 0 or "Traceback" in r.stdout + r.stderr:
        sys.exit(f"stage failed: {cmd[0]}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec", required=True)
    ap.add_argument("--work", required=True)
    ap.add_argument("--source", required=True)
    ap.add_argument("--skin", required=True)
    ap.add_argument("--blender", default=r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
    ap.add_argument("--from", dest="start", choices=STAGES, default="assemble")
    a = ap.parse_args()
    w = os.path.abspath(a.work)
    f = os.path.join(w, "facefit")
    py = sys.executable
    blender = lambda script, *args: [a.blender, "-b", "--python", os.path.join(HERE, script), "--", *args]  # noqa: E731
    todo = STAGES[STAGES.index(a.start):]
    if "assemble" in todo:
        run(blender("mpfb_assemble.py", "--blend", os.path.join(w, "mpfb_base.blend"), "--spec", a.spec, "--work", w,
                    "--out", os.path.join(w, "assembled.blend")))
    if "wrap" in todo:
        wd = os.path.join(w, "wrap")
        common = ["--blend", os.path.join(w, "assembled.blend"), "--source", a.source, "--dir", wd]
        run(blender("hybrid_wrap.py", "--mode", "render", *common))
        run([py, os.path.join(HERE, "solve_face_fit.py"), "landmarks", "--render", os.path.join(wd, "mpfb_front.png"),
             "--reference", os.path.join(wd, "source_front.png"), "--out", wd])
        run(blender("hybrid_wrap.py", "--mode", "apply", *common, "--out", os.path.join(w, "wrapped.blend")))
    if "fit" in todo:
        run(blender("hybrid_fit.py", "--blend", os.path.join(w, "wrapped.blend"), "--source", a.source, "--out", os.path.join(w, "fitted.blend")))
    if "bake" in todo:
        # The face fit's camera and render on the wrapped head first: the bake renders the source's face through that camera.
        run(blender("mpfb_face_fit.py", "--blend", os.path.join(w, "wrapped.blend"), "--out", f, "--mode", "render"))
        run(blender("hybrid_bake.py", "--blend", os.path.join(w, "fitted.blend"), "--work", w))
    if "face" in todo:
        ref, delit = os.path.join(f, "source_face.png"), os.path.join(f, "source_face_delit.png")
        run([py, os.path.join(HERE, "solve_face_fit.py"), "landmarks", "--render", os.path.join(f, "face.png"), "--reference", ref, "--out", f])
        run([py, os.path.join(HERE, "delight_face.py"), "--reference", ref, "--out", delit])
        run([py, os.path.join(HERE, "warp_face.py"), "--dir", f, "--reference", delit])
        run(blender("mpfb_bake_face.py", "--blend", os.path.join(w, "wrapped.blend"), "--dir", f))
        run([py, os.path.join(HERE, "composite_skin.py"), "--dir", f, "--base", a.skin, "--out", os.path.join(f, "body_albedo.png")])
    if "compose" in todo:
        run([py, os.path.join(HERE, "compose_hybrid.py"), "--work", w])
    if "finish" in todo:
        run(blender("hybrid_finish.py", "--blend", os.path.join(w, "fitted.blend"), "--work", w, "--spec", a.spec,
                    "--out", os.path.join(w, "hybrid.blend")))
    if "keys" in todo:
        run(blender("proxy_face_keys.py", "--blend", os.path.join(w, "hybrid.blend"), "--out", os.path.join(w, "hybrid.blend")))
    print("BUILD_HYBRID", os.path.join(w, "hybrid.blend"))


if __name__ == "__main__":
    main()
