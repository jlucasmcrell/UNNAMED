"""Fetch the Stable Audio 3 Small SFX weights ComfyUI needs, with recorded provenance.

V2 of the audio pipeline. V1 used Stable Audio Open 1.0; this fetches Stable Audio 3 Small SFX, the
sound-effect member of the SA3 family, and the T5Gemma text encoder it requires.

**The post-trained SFX checkpoint, not the base one.** The repository ships both:
`stable_audio_3_small_sfx.safetensors` is the fine-tuned production model and
`stable_audio_3_small_sfx_base.safetensors` is the pretraining base. The brief is explicit that the
fine-tune is the intended direct-generation checkpoint, so the base is recorded as rejected rather
than silently ignored.

**Comfy-Org repackaging, not the canonical Stability repository.** The canonical
`stabilityai/stable-audio-3-small-sfx` repository is licence-gated (`gated: auto`) and contains the
native multi-file layout (`model.safetensors` plus a `t5gemma-b-b-ul2/` folder). ComfyUI loads a single
combined checkpoint and a single text encoder, which is what `Comfy-Org/stable-audio-3` distributes,
and its filenames are the ones ComfyUI's own official blueprint references
(`stable_audio_3_medium.safetensors`, `t5gemma_b_b_ul2.safetensors`). So the Comfy-Org path is used,
and the upstream source it repackages is recorded alongside it.

That repository is **not** gated, so no licence acceptance is needed here - but the weights it carries
derive from the gated Stability model and from Gemma components, and those terms still bind the
output. Both are recorded.

Nothing is overwritten: an existing destination file is hashed and left alone.

Usage:
    python _fetch_sa3_model.py                 # list sources and destinations, download nothing
    python _fetch_sa3_model.py --apply         # download what is missing
"""
import argparse
import hashlib
import io
import json
import os
import time

COMFY_MODELS = r"C:\Users\jluca\ComfyUI\models"
ASSETS = r"W:\UNNAMED\assets"
OUT = os.path.join(ASSETS, "manifests", "audio_models_v2.json")

REPO = "Comfy-Org/stable-audio-3"
REPO_SHA = "75aad8836271cb5a5123fb58304fae5d3bb4b0f9"
UPSTREAM = "stabilityai/stable-audio-3-small-sfx"

SOURCES = [
    {
        "repo": REPO,
        "repo_path": "checkpoints/stable_audio_3_small_sfx.safetensors",
        "destination": os.path.join(COMFY_MODELS, "checkpoints",
                                    "stable_audio_3_small_sfx.safetensors"),
        "expect_sha256": "ed9cf1b6172f1a8c2921a9560c21109ff3239524563ced9dce6dcdef41e2f515",
        "role": "Stable Audio 3 Small SFX - diffusion checkpoint, loaded by CheckpointLoaderSimple",
        "variant": "post-trained SFX (the production checkpoint)",
        "bytes": 2270384940,
        "subfolder": "checkpoints",
    },
    {
        "repo": REPO,
        "repo_path": "text_encoders/t5gemma_b_b_ul2.safetensors",
        "destination": os.path.join(COMFY_MODELS, "text_encoders", "t5gemma_b_b_ul2.safetensors"),
        "expect_sha256": "1e1eba25be8872edb0d3c6335c6658fd6388e7b14b60da6e454e404cfcd8150e",
        "role": "T5Gemma text encoder, loaded by CLIPLoader with type 'stable_audio'",
        "variant": "t5gemma-b-b-ul2",
        "bytes": 1187264003,
        "subfolder": "text_encoders",
    },
]

REJECTED = [
    {
        "repo": "Comfy-Org/stable-audio-3",
        "repo_path": "checkpoints/stable_audio_3_small_sfx_base.safetensors",
        "why": ("The pretraining base checkpoint. The brief requires the post-trained SFX fine-tune for "
                "direct generation; the base is a base model, not the production checkpoint."),
    },
    {
        "repo": "Comfy-Org/stable-audio-3",
        "repo_path": "checkpoints/stable_audio_3_small_music.safetensors",
        "why": "Text-to-music. The brief forbids starting the music sprint in this pass.",
    },
    {
        "repo": "Comfy-Org/stable-audio-3",
        "repo_path": "checkpoints/stable_audio_3_medium.safetensors",
        "why": ("Larger SFX/general checkpoint. Small SFX is the audio SFX member the owner specified "
                "and is lighter; Medium is not needed for this pass."),
    },
]


def sha256_of(path, chunk=1 << 22):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(chunk)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    print(f"  repository : {REPO}  @{REPO_SHA[:12]}")
    print(f"  upstream   : {UPSTREAM}  (licence-gated canonical repository)")
    print("  licence    : Stability AI Community License + Gemma Terms of Use (redistributed)")
    print()

    records = []
    for source in SOURCES:
        destination = source["destination"]
        exists = os.path.exists(destination)
        print(f"  {source['repo_path']}")
        print(f"     -> {destination}")
        print(f"     {'present' if exists else 'missing'}")
        if not exists and args.apply:
            from huggingface_hub import hf_hub_download
            os.makedirs(os.path.dirname(destination), exist_ok=True)
            # Download to the ComfyUI cache first, then place it, so a partial file never appears at
            # the destination path - a truncated checkpoint at the loading path is worse than none.
            local = hf_hub_download(repo_id=source["repo"], filename=source["repo_path"])
            import shutil
            shutil.copy2(local, destination)
            print("     downloaded")

        if os.path.exists(destination):
            digest = sha256_of(destination)
            size = os.path.getsize(destination)
            match = digest == source["expect_sha256"]
            print(f"     sha256 {digest[:24]}...  {size} bytes  matches recorded: {match}")
            records.append({
                "path": destination,
                "repo": source["repo"],
                "repo_path": source["repo_path"],
                "repo_sha": REPO_SHA,
                "upstream_source": UPSTREAM,
                "distribution": "Comfy-Org repackaging (ComfyUI single-file layout)",
                "role": source["role"],
                "variant": source["variant"],
                "sha256": digest,
                "bytes": size,
            })
        print()

    if args.apply:
        document = {
            "version": 2,
            "comment": [
                "Provenance for the Stable Audio 3 Small SFX pass.",
                "",
                "This is a NEW record. The V1 provenance in audio_models.json (Stable Audio Open 1.0 and",
                "t5-base) is deliberately not modified: V1 remains the comparison set and its history is",
                "kept intact.",
                "",
                "A sha256 is recorded for each weight file so a regeneration can prove it used the same",
                "weights and so a silently substituted checkpoint is detectable.",
            ],
            "model": {
                "architecture": "Stable Audio 3 (DiT dit1.0, audio)",
                "name": "Stable Audio 3 Small SFX",
                "checkpoint": "stable_audio_3_small_sfx.safetensors",
                "parameters": 567573761,
                "text_encoder": "t5gemma-b-b-ul2",
                "comfyui_support": "native (comfy.supported_models.StableAudio3, comfy.text_encoders.sa3)",
                "license": "Stability AI Community License",
                "license_tag": "stable-audio-community",
                "redistributed_components": [
                    "T5Gemma / Gemma components, redistributed under the Gemma Terms of Use",
                ],
                "gating": ("The canonical upstream repository is licence-gated and the owner accepted "
                           "its terms. The Comfy-Org distribution used here is not gated and carries "
                           "the same weights."),
            },
            "fetched": time.strftime("%Y-%m-%dT%H:%M:%S"),
            "host": "BEAST",
            "files": records,
            "rejected": REJECTED,
        }
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        with io.open(OUT, "w", encoding="utf-8") as handle:
            json.dump(document, handle, indent=2)
            handle.write("\n")
        print(f"  wrote {OUT}")
    else:
        print("  (list only; pass --apply to download)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
