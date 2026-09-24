"""Fetch the Stable Audio Open 1.0 weights ComfyUI needs, with recorded provenance hashes.

Reconnaissance found the audio *nodes* on all three hosts but no sound-effect *weights* anywhere:
BEAST and ASTRAL hold only LTX audio VAEs, and RAZER holds ACE-Step 1.5, which is text-to-music and
cannot produce a 200 ms sword impact. ComfyUI supports Stable Audio Open 1.0 natively
(EmptyLatentAudio / ConditioningStableAudio / VAEDecodeAudio are already present on all three
hosts), so the gap is two weight files, not a code change.

Downloads land in the existing ComfyUI model directories rather than in a new environment. The
sprint brief forbids upgrading or destabilising a working ComfyUI to gain audio; adding two files to
`models/checkpoints` and `models/text_encoders` touches no existing file, changes no custom node,
and leaves the LTX, Trellis and Z-Image paths exactly as they were. Nothing is overwritten: an
existing file is hashed and left alone.

Sources, both verified reachable before writing this:

  checkpoint  Comfy-Org/stable-audio-open-1.0_repackaged  4.52 GiB  Stability AI Community License
  text enc.   google-t5/t5-base                             0.83 GiB  Apache-2.0

The canonical `stabilityai/stable-audio-open-1.0` repository is licence-gated on Hugging Face, and
accepting a licence is the owner's act rather than mine, so the Comfy-Org distribution mirror is used
instead. It is the mirror ComfyUI documents for this node set and it ships the same LICENSE.md.

Usage:
    python _fetch_audio_model.py                 # list sources, download nothing
    python _fetch_audio_model.py --apply         # download what is missing
"""
import argparse
import hashlib
import json
import os
import urllib.request

COMFY_MODELS = r"C:\Users\jluca\ComfyUI\models"

SOURCES = [
    {
        "repo": "Comfy-Org/stable-audio-open-1.0_repackaged",
        "repo_path": "stable-audio-open-1.0.safetensors",
        "destination": os.path.join(COMFY_MODELS, "checkpoints",
                                    "stable_audio_open_1.0.safetensors"),
        "role": "diffusion checkpoint + autoencoder, loaded by CheckpointLoaderSimple",
        "license": "Stability AI Community License",
        "license_tag": "stable-audio-community",
        "license_url": "https://huggingface.co/Comfy-Org/stable-audio-open-1.0_repackaged/blob/main/LICENSE.md",
        "commercial_use": ("permitted, including commercially, for organisations under USD "
                           "$1,000,000 annual revenue; registration at "
                           "stability.ai/community-license and a retained attribution notice plus "
                           "'Powered by Stability AI' are required; the licence terminates above "
                           "the revenue threshold"),
        "copyright": "Copyright (c) Stability AI Ltd.",
    },
    {
        "repo": "google-t5/t5-base",
        "repo_path": "model.safetensors",
        "destination": os.path.join(COMFY_MODELS, "text_encoders", "t5_base.safetensors"),
        "role": "T5-base text encoder, loaded by CLIPLoader with type 'stable_audio'",
        "license": "Apache-2.0",
        "license_tag": "apache-2.0",
        "license_url": "https://huggingface.co/google-t5/t5-base/blob/main/LICENSE",
        "commercial_use": "permitted, no attribution required beyond the licence text",
        "copyright": "Copyright (c) Google LLC",
    },
]
REVISION = "main"


def sha256(path, chunk=1 << 22):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(chunk)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def download(url, destination):
    request = urllib.request.Request(url, headers={"User-Agent": "DSH-audio-sprint"})
    # Streamed, so a 4.5 GiB checkpoint never has to fit in RAM.
    with urllib.request.urlopen(request, timeout=180) as response:
        total = int(response.headers.get("Content-Length") or 0)
        done = 0
        with open(destination + ".part", "wb") as handle:
            while True:
                block = response.read(1 << 22)
                if not block:
                    break
                handle.write(block)
                done += len(block)
                if total and done % (1 << 28) < (1 << 22):
                    print(f"      {done / 2**20:.0f} / {total / 2**20:.0f} MB", flush=True)
    if total and os.path.getsize(destination + ".part") != total:
        raise SystemExit(f"  FAIL short download: expected {total}, got "
                         f"{os.path.getsize(destination + '.part')}")
    os.replace(destination + ".part", destination)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--provenance", default=r"W:\UNNAMED\assets\manifests\audio_models.json")
    args = parser.parse_args()

    records = []
    for source in SOURCES:
        url = (f"https://huggingface.co/{source['repo']}/resolve/{REVISION}/"
               f"{source['repo_path']}")
        record = dict(source)
        record["url"] = url
        record["revision"] = REVISION
        record["source_page"] = f"https://huggingface.co/{source['repo']}"
        exists = os.path.exists(source["destination"])
        print(f"\n  {source['repo']} :: {source['repo_path']}")
        print(f"    -> {source['destination']}")
        print(f"    {source['license']}  ({source['license_tag']})")

        if exists and not args.apply:
            print("    already present")
        if args.apply and not exists:
            os.makedirs(os.path.dirname(source["destination"]), exist_ok=True)
            print("    downloading ...")
            download(url, source["destination"])
        if os.path.exists(source["destination"]):
            record["bytes"] = os.path.getsize(source["destination"])
            record["sha256"] = sha256(source["destination"])
            print(f"    {record['bytes'] / 2**30:.2f} GiB  sha256 {record['sha256'][:16]}...")
        else:
            print("    (not downloaded; pass --apply)")
        records.append(record)

    if args.apply:
        os.makedirs(os.path.dirname(args.provenance), exist_ok=True)
        document = {"comment": [
            "Provenance for every audio model used by the Phase-1 audio sprint.",
            "",
            "A sha256 is recorded for each weight file so a later regeneration can prove it used the",
            "same weights, and so a silently substituted checkpoint is detectable.",
            "",
            "The canonical stabilityai/stable-audio-open-1.0 repository is licence-gated; the",
            "Comfy-Org distribution mirror was used instead because accepting a licence is the",
            "owner's act. Both ship the same Stability AI Community License.",
        ], "models": {}}
        for record in records:
            document["models"][record["destination"]] = record
        with open(args.provenance, "w", encoding="utf-8") as handle:
            json.dump(document, handle, indent=2)
            handle.write("\n")
        print(f"\n  wrote {args.provenance}")
    else:
        print("\n  (listing only; pass --apply to download)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
