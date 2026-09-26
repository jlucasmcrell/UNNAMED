"""A character's identity skin texture from its reference portrait (charstd): the existing generic tools run in order - the built body's
face rendered through the fit camera (charprod2/mpfb_face_fit.py render), the portrait's landmarks matched to it (solve_face_fit.py
landmarks), the portrait's lighting taken out (delight_face.py), warped onto the body's own features (warp_face.py), baked into
the body's UV layout (mpfb_bake_face.py) and laid over the character's stock skin at a set opacity (composite_skin.py). Per face this
is configuration only: a reference image and an opacity.

    python identity_texture.py --blend character.blend --reference portrait.png --skin <MPFB skin diffuse> --out <dir> [--opacity 0.7]

Writes <dir>/skin_albedo.png (point the character file's "skin_texture" at it).
"""
import argparse
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.join(HERE, "..", "charprod2")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0 or "Traceback" in r.stdout + r.stderr:
        sys.exit(f"failed: {' '.join(cmd[:4])}\n{(r.stdout + r.stderr)[-1500:]}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--blend", required=True)
    ap.add_argument("--reference", required=True)
    ap.add_argument("--skin", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--opacity", type=float, default=0.7)
    a = ap.parse_args()
    f = os.path.abspath(a.out)
    os.makedirs(f, exist_ok=True)
    blender = lambda s, *args: [BLENDER, "-b", "--python", os.path.join(TOOLS, s), "--", *args]  # noqa: E731
    run(blender("mpfb_face_fit.py", "--blend", os.path.abspath(a.blend), "--out", f, "--mode", "render"))
    ref = os.path.join(f, "reference.png")
    shutil.copyfile(a.reference, ref)
    run([sys.executable, os.path.join(TOOLS, "solve_face_fit.py"), "landmarks", "--render", os.path.join(f, "face.png"), "--reference", ref,
         "--out", f])
    run([sys.executable, os.path.join(TOOLS, "delight_face.py"), "--reference", ref, "--out", os.path.join(f, "reference_delit.png")])
    run([sys.executable, os.path.join(TOOLS, "warp_face.py"), "--dir", f, "--reference", os.path.join(f, "reference_delit.png")])
    run(blender("mpfb_bake_face.py", "--blend", os.path.abspath(a.blend), "--dir", f))
    run([sys.executable, os.path.join(TOOLS, "composite_skin.py"), "--dir", f, "--base", a.skin, "--out", os.path.join(f, "skin_albedo.png"),
         "--face-opacity", str(a.opacity)])
    print("IDENTITY_TEXTURE", os.path.join(f, "skin_albedo.png"))


if __name__ == "__main__":
    main()
