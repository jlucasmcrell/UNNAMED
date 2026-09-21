"""Fetch the native Trellis.2 / Pixal3D asset set into the ComfyUI models tree.

Layout and filenames come from the official template
templates/3d_pixal3d_trellis2_image_to_model.json.
"""
import os
import sys

from huggingface_hub import hf_hub_download

MODELS = os.environ.get("UNNAMED_COMFY_MODELS", r"C:\Users\jluca\ComfyUI\models")

# (repo_id, path_in_repo, destination subfolder)
FILES = [
    ("Comfy-Org/Pixal3D", "diffusion_models/pixal3d_int8_convrot.safetensors", "diffusion_models"),
    ("Comfy-Org/TRELLIS.2", "diffusion_models/trellis_2_int8_convrot.safetensors", "diffusion_models"),
    ("Comfy-Org/Pixal3D", "vae/trellis_2_shape_vae_bf16.safetensors", "vae"),
    ("Comfy-Org/Pixal3D", "vae/trellis_2_texture_vae_bf16.safetensors", "vae"),
    ("Comfy-Org/Pixal3D", "clip_vision/dino_v3_L_naf_fp32.safetensors", "clip_vision"),
    ("Comfy-Org/BiRefNet", "background_removal/birefnet.safetensors", "background_removal"),
    ("Comfy-Org/MoGe", "geometry_estimation/moge_2_vitl_normal_fp16.safetensors", "geometry_estimation"),
]


def main():
    for repo_id, path_in_repo, subfolder in FILES:
        target_dir = os.path.join(MODELS, subfolder)
        os.makedirs(target_dir, exist_ok=True)
        filename = os.path.basename(path_in_repo)
        final_path = os.path.join(target_dir, filename)

        if os.path.exists(final_path):
            size_gb = os.path.getsize(final_path) / 1e9
            print(f"SKIP  {subfolder}/{filename} ({size_gb:.2f} GB already present)", flush=True)
            continue

        print(f"GET   {repo_id}/{path_in_repo}", flush=True)
        try:
            hf_hub_download(repo_id=repo_id, filename=path_in_repo, local_dir=target_dir)
        except Exception as exc:
            print(f"FAIL  {path_in_repo}: {exc}", flush=True)
            return 1

        # hf_hub_download mirrors the repo subfolder (e.g. vae/foo.safetensors);
        # ComfyUI loads these folders flat, so move the file up and drop the stub.
        mirrored = os.path.join(target_dir, path_in_repo)
        if os.path.exists(mirrored) and os.path.abspath(mirrored) != os.path.abspath(final_path):
            os.replace(mirrored, final_path)
            parent = os.path.dirname(mirrored)
            while os.path.abspath(parent) != os.path.abspath(target_dir):
                if not os.listdir(parent):
                    os.rmdir(parent)
                    parent = os.path.dirname(parent)
                else:
                    break

        size_gb = os.path.getsize(final_path) / 1e9
        print(f"OK    {subfolder}/{filename} ({size_gb:.2f} GB)", flush=True)

    print("ALL_DOWNLOADS_COMPLETE", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
