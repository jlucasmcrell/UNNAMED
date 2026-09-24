"""Freeze the Stable Audio Open 1.0 audio set as V1 before the Stable Audio 3 pass replaces it.

The brief requires an explicit archive boundary rather than relying on Git, because `assets/` is
intentionally not tracked: a `git checkout` restores nothing here, and the V2 pass writes to the same
delivered paths the game already resolves. Without this, regenerating in place would destroy the only
copy of V1 and with it the side-by-side comparison the owner asked for.

What is preserved, and why each part matters:

  delivered WAVs      the 173 shipping files the event contract resolves today
  FLAC masters        the untouched generator output, which is what a regeneration is compared against
  manifests           ids, prompts, seeds, settings, per-sound measurements and validation state
  spectrograms        the V1 review images, so V1 and V2 can be compared structurally as well as by ear
  provenance          audio_models.json and the V1 model records

Hashes are recorded for every copied file **from the source**, before anything is written, so the V1
record is of the original bytes rather than of a copy. The copy is then re-read and re-hashed, because
a preservation step that silently copies nothing is the exact failure it exists to prevent.

Idempotent: re-running leaves an existing V1 archive untouched and only reports on it.

Usage:
    python _preserve_v1_audio.py --audit
    python _preserve_v1_audio.py --apply
    python _preserve_v1_audio.py --verify
"""
import argparse
import hashlib
import io
import json
import os
import shutil

ASSETS = r"W:\UNNAMED\assets"
AUDIO = os.path.join(ASSETS, "audio")
REVIEW = os.path.join(ASSETS, "review", "audio")

ARCHIVE_AUDIO = os.path.join(AUDIO, "v1_stable_audio_open")
ARCHIVE_REVIEW = os.path.join(REVIEW, "v1_stable_audio_open")
MANIFEST = os.path.join(ASSETS, "manifests", "playable_prototype_audio.json")
ARCHIVE_MANIFEST = os.path.join(ARCHIVE_AUDIO, "V1_MANIFEST.json")

MANIFESTS_TO_KEEP = ["playable_prototype_audio.json", "audio_spec.json", "audio_qa.json",
                     "audio_models.json"]
DOCS_TO_KEEP = ["PHASE1_AUDIO_SPRINT_STATUS.md", "PHASE1_AUDIO_PROVENANCE.md",
                "PHASE1_AUDIO_EVENT_CONTRACT.md"]
DOCS = r"W:\UNNAMED\docs"


def sha256_of(path, chunk=1 << 20):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(chunk)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def collect_sources():
    """Everything V1 owns, as (source_path, archive_relative_path) pairs."""
    pairs = []
    with io.open(MANIFEST, encoding="utf-8") as handle:
        manifest = json.load(handle)

    for sound in manifest["sounds"]:
        delivered = sound.get("delivered")
        if delivered:
            source = os.path.join(ASSETS, delivered.replace("/", os.sep))
            if os.path.exists(source):
                pairs.append((source, os.path.join("delivered", os.path.basename(source))))

    masters = os.path.join(AUDIO, "masters")
    if os.path.isdir(masters):
        for name in sorted(os.listdir(masters)):
            pairs.append((os.path.join(masters, name), os.path.join("masters", name)))

    for name in MANIFESTS_TO_KEEP:
        path = os.path.join(ASSETS, "manifests", name)
        if os.path.exists(path):
            pairs.append((path, os.path.join("manifests", name)))

    for name in DOCS_TO_KEEP:
        path = os.path.join(DOCS, name)
        if os.path.exists(path):
            pairs.append((path, os.path.join("docs", name)))

    return pairs


def collect_review():
    pairs = []
    if os.path.isdir(REVIEW):
        for name in sorted(os.listdir(REVIEW)):
            path = os.path.join(REVIEW, name)
            if os.path.isfile(path):
                pairs.append((path, name))
    return pairs


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()

    if args.verify:
        return verify()

    pairs = collect_sources()
    review = collect_review()
    total = sum(os.path.getsize(s) for s, _ in pairs + review)
    print(f"  V1 files to preserve : {len(pairs) + len(review)}")
    print(f"  total size           : {total / 1048576:.1f} MB")
    print(f"  audio archive        : {ARCHIVE_AUDIO}")
    print(f"  review archive       : {ARCHIVE_REVIEW}")
    print(f"  already archived     : {os.path.exists(ARCHIVE_MANIFEST)}")

    kinds = {}
    for _source, relative in pairs:
        kinds[relative.split(os.sep)[0]] = kinds.get(relative.split(os.sep)[0], 0) + 1
    print()
    for kind, count in sorted(kinds.items()):
        print(f"    {count:>4}  {kind}")

    if not args.apply:
        print("\n  (audit only; pass --apply to archive)")
        return 0

    if os.path.exists(ARCHIVE_MANIFEST):
        print("\n  V1 archive already exists; leaving it untouched. Use --verify to check it.")
        return verify()

    records = []
    for source, relative in pairs + review:
        # Review images go to the review archive; everything else to the audio archive.
        target = os.path.join(ARCHIVE_REVIEW, relative) if relative.endswith(".png") \
            else os.path.join(ARCHIVE_AUDIO, relative)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        digest = sha256_of(source)
        shutil.copy2(source, target)
        records.append({
            "archive_path": os.path.relpath(target, ASSETS).replace(os.sep, "/"),
            "source_path": os.path.relpath(source, ASSETS).replace(os.sep, "/"),
            "bytes": os.path.getsize(source),
            "sha256": digest,
        })

    document = {
        "version": 1,
        "label": "V1 - Stable Audio Open 1.0",
        "comment": [
            "The complete Phase-1 audio set as generated with Stable Audio Open 1.0, frozen before the",
            "Stable Audio 3 Small SFX regeneration replaced the delivered files.",
            "",
            "Hashes are of the ORIGINAL source bytes, taken before copying, so this record describes V1",
            "rather than a copy of it. `--verify` re-reads the archive and compares.",
            "",
            "Kept because assets/ is not tracked by Git: an in-place regeneration would otherwise have",
            "destroyed the only copy and with it the V1-versus-V2 comparison.",
        ],
        "frozen": "2026-09-24",
        "model": "Stable Audio Open 1.0 + t5-base",
        "file_count": len(records),
        "total_bytes": sum(r["bytes"] for r in records),
        "files": records,
    }
    with io.open(ARCHIVE_MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")

    print(f"\n  archived : {len(records)} files, {document['total_bytes'] / 1048576:.1f} MB")
    print(f"  manifest : {ARCHIVE_MANIFEST}")
    return verify()


def verify():
    """Re-read the archive and compare against the hashes taken from the source."""
    if not os.path.exists(ARCHIVE_MANIFEST):
        print("  no V1 archive to verify")
        return 1
    with io.open(ARCHIVE_MANIFEST, encoding="utf-8") as handle:
        document = json.load(handle)
    ok = missing = mismatched = 0
    for record in document["files"]:
        path = os.path.join(ASSETS, record["archive_path"].replace("/", os.sep))
        if not os.path.exists(path):
            missing += 1
            continue
        if sha256_of(path) == record["sha256"]:
            ok += 1
        else:
            mismatched += 1
    print()
    print(f"  V1 archive verify: {ok} ok, {missing} missing, {mismatched} mismatched "
          f"(of {document['file_count']})")
    return 0 if not missing and not mismatched else 1


if __name__ == "__main__":
    raise SystemExit(main())
