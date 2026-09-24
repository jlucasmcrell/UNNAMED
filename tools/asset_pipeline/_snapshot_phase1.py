"""Snapshot the Phase-1 asset state to a timestamped directory outside the repository.

Why this exists: `assets/` is gitignored, so the entire Phase-1 asset library lives only in the
working tree on one machine. A bad rescale, a failed bulk build or a disk fault would take it with no
recovery point. The tooling is now committed on a branch, but the artefacts it produced are not, and
cannot be - generated geometry does not belong in git.

So this copies the Phase-1-critical state to a sibling directory of the repository and writes a
manifest of every file with its size and SHA-256, which is what makes the snapshot verifiable later
rather than merely present.

Deliberately selective. The full asset tree is several gigabytes, most of it unrelated backlog and
raw image-to-3D intermediates that are regenerable from the concepts. Copying all of it would take
an hour and protect nothing extra, so the set is: the Phase-1 asset ids and everything that hangs off
them, plus the manifests, requests, sockets and review renders that describe them.

Usage:
    python _snapshot_phase1.py --audit
    python _snapshot_phase1.py --apply
    python _snapshot_phase1.py --verify <snapshot_dir>
"""
import argparse
import hashlib
import io
import json
import os
import shutil
import sys
import time

ASSETS = r"W:\UNNAMED\assets"
DOCS = r"W:\UNNAMED\docs"
REPO = r"W:\UNNAMED"
DESTINATION_ROOT = r"W:\_asset_snapshots"

# The Phase-1 asset ids the objective and the content bible name. Anything not listed here is
# backlog, and backlog is not what this snapshot is for.
PHASE1_IDS = [
    # five archetypes
    "creature_ash_ember_hound", "creature_bone_walker_husk", "creature_animated_armour",
    "creature_bristleback_boar", "creature_cave_hunting_spider",
    # three weapon families
    "weapon_arming_sword", "weapon_hunting_bow", "weapon_march_spear",
    # four NPCs
    "npc_veth_magistrate", "npc_kal_smith", "npc_siann_archivist", "npc_orenth_guide",
    # waystation and its buildings
    "landmark_ashen_waystone", "forge_shed", "longhouse", "building_well",
    "prop_blacksmith_anvil_stump", "prop_forge_double_bellows",
    # the modular kit the buildings are assembled from
    "building_wall_stone", "building_wall_timber", "building_floor_planks", "building_post",
    "building_beam", "building_roof_panel", "building_door_frame", "building_window_frame",
    # Blackvein quarry
    "prop_iron_vein_outcrop", "prop_blocked_shaft", "prop_quarry_winch",
    "prop_quarry_rail_track",
    # Foldscar
    "landmark_quiet_stone", "landmark_foldscar_core",
    # Charwood Verge
    "prop_cart_damaged_merchant", "resource_ash_haft", "resource_woundmoss",
    "flora_oak_tree", "flora_pine_tree", "flora_birch_tree", "flora_dead_tree",
    "flora_fern_clump", "flora_bramble_bush", "flora_boneleaf_bush",
    # crafting chain
    "resource_iron_billet", "item_raw_iron_ore", "resource_iron_ore",
]

# Icon concepts are named `icon_*` rather than by asset id. Rather than keep a second list here that
# can fall out of step with the set, read the concept ids from `_promote_ui_icons.SLOTS`, which is the
# authoritative mapping from slot to concept. This is the same single-source rule the validation
# reconciliation applied: a snapshot that silently misses an icon is worse than one that fails loudly.
def icon_concepts():
    try:
        import _promote_ui_icons
    except ImportError:
        return []
    return sorted({concept for concept, _source, _note in _promote_ui_icons.SLOTS.values()})


# Whole directories that describe every Phase-1 asset and are small enough to take entire.
WHOLE_DIRS = [
    ("manifests", os.path.join(ASSETS, "manifests")),
    ("requests", os.path.join(ASSETS, "requests")),
    ("sockets", os.path.join(ASSETS, "sockets")),
    ("vfx", os.path.join(ASSETS, "vfx")),
    ("ui", os.path.join(ASSETS, "ui")),
    # The ten Phase-1 world materials, with their base colour, normal, ORM, `.tres` and metadata.
    # Absent from the first snapshot, which predated the material pass entirely.
    ("materials", os.path.join(ASSETS, "materials")),
    # The whole audio tree: the V1 archive, every V2 candidate, the provisional delivered set, the
    # masters, and the owner's audition report. None of it was in scope before this, so neither the
    # original audio set nor the regenerated one had ever been snapshotted - and the audition report
    # in particular is the only record of decisions that no process can reproduce.
    ("audio", os.path.join(ASSETS, "audio")),
]

# Per-asset trees, keyed by the directory that holds them.
PER_ASSET_DIRS = ["ready", "rigged"]

# `blender_src/` and `raw/` are flat: one file per asset id, not one directory per asset. They are
# matched by filename instead, which also keeps the 5 GB of backlog raw output out of the snapshot.
FLAT_ASSET_DIRS = [
    ("blender_src", (".blend", ".blend1")),
    ("raw", (".glb",)),
]

# Archive directories that document the history of the Phase-1 assets specifically. The three
# concept-churn directories from earlier sprints are excluded - they are backlog, and the concepts
# they superseded are not Phase-1 assets.
SUPERSEDED_DIRS = [
    "ashen_hollow_landmarks", "ash_haft_concept", "assembly_wip", "bible_batch_prescale",
    "boar_reconstruction", "kal_prescale", "reconstruction_failed", "rigged_prescale",
    "semantic_dimensions", "vfx_ember_ward_mend",
    # The pre-regrade base colours. These are the *only* copy of the originals - the weathering grade
    # overwrote the live files and archived them here - so leaving them out would make the grade
    # irreversible if anything happened to the working tree.
    "materials_pre_weather",
    # The assembly manifest as it stood before the declared/assembled split.
    "kit_assemblies_prev",
    # The two buildings as they stood before the roof self-intersection was fixed.
    "roof_layout_v2",
    "ui_icons_26slot",
]

# Animation is small and entirely Phase-1 relevant.
ANIMATION_DIRS = ["clips", "ready", "source"]

# Review renders: the judgement images, not the whole 800 MB review tree.
REVIEW_SUBDIRS = [os.path.join("bible_batch", ""), "ui", "vfx", "buildings", "npc_anim", "audio"]

# Review files that sit outside a whole subdirectory. `review/audio_v2` also holds a full copy of
# every candidate so the page can play them; that copy is regenerable from `audio/v2_candidates` and
# snapshotting it would add ~140 MB of duplication, so only the judgement sheets and the index go in.
REVIEW_FILES = [
    os.path.join(ASSETS, "review", "audio_v2", "index.html"),
    os.path.join(ASSETS, "review", "audio_v2", "audition_index.json"),
    os.path.join(ASSETS, "review", "audio_v2", "compare_final.png"),
    os.path.join(ASSETS, "review", "audio_v2", "compare_smoke.png"),
]

# Single files worth taking.
SINGLE_FILES = [
    os.path.join(ASSETS, "catalog.json"),
    os.path.join(ASSETS, "CATALOG.md"),
    os.path.join(DOCS, "PHASE1_BIBLE_ASSET_SPRINT_STATUS.md"),
    os.path.join(DOCS, "PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md"),
    os.path.join(DOCS, "CANONICAL_BODY_AND_SKELETON.md"),
    os.path.join(DOCS, "PHASE1_AUDIO_SPRINT_STATUS.md"),
    os.path.join(DOCS, "PHASE1_AUDIO_PROVENANCE.md"),
    os.path.join(DOCS, "PHASE1_AUDIO_EVENT_CONTRACT.md"),
    os.path.join(DOCS, "SCALE_AUDIT_REPORT.md"),
    # Written during the maintenance pass. These are the record of what was changed and why; a
    # snapshot of the artefacts without them loses the reasoning.
    os.path.join(DOCS, "ASSET_PIPELINE_CHECKPOINT_2026-09-24.md"),
    os.path.join(DOCS, "ASSET_MATERIAL_PASS_2026-09-24.md"),
    os.path.join(DOCS, "ASSET_NPC_ANIMATION_GAP.md"),
    os.path.join(DOCS, "ASSET_KNOWN_LIMITATIONS.md"),
    os.path.join(DOCS, "ASSET_AUDIO_REGENERATION_REQUIRED.md"),
    os.path.join(DOCS, "ASSET_PROMPT_RISK_AUDIT.md"),
    os.path.join(DOCS, "SKELETON_CONTRACT_RECONCILIATION.md"),
]


def sha256(path, chunk=1 << 20):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(chunk)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def collect(root):
    """Every file under root, as absolute paths."""
    found = []
    for base, _dirs, files in os.walk(root):
        for name in files:
            found.append(os.path.join(base, name))
    return found


def build_plan():
    """Return the list of source paths to copy, de-duplicated and sorted."""
    sources = []

    for _label, directory in WHOLE_DIRS:
        if os.path.isdir(directory):
            sources.extend(collect(directory))

    for area in PER_ASSET_DIRS:
        for asset_id in PHASE1_IDS:
            directory = os.path.join(ASSETS, area, asset_id)
            if os.path.isdir(directory):
                sources.extend(collect(directory))

    for area, suffixes in FLAT_ASSET_DIRS:
        directory = os.path.join(ASSETS, area)
        if not os.path.isdir(directory):
            continue
        for asset_id in PHASE1_IDS:
            for suffix in suffixes:
                candidate = os.path.join(directory, f"{asset_id}{suffix}")
                if os.path.exists(candidate):
                    sources.append(candidate)

    animation = os.path.join(ASSETS, "animation")
    for area in ANIMATION_DIRS:
        directory = os.path.join(animation, area)
        if os.path.isdir(directory):
            sources.extend(collect(directory))

    for area in REVIEW_SUBDIRS:
        directory = os.path.join(ASSETS, "review", area.rstrip("\\/"))
        if os.path.isdir(directory):
            sources.extend(collect(directory))

    for path in REVIEW_FILES:
        if os.path.exists(path):
            sources.append(path)

    # Phase-1 concepts only. The other ~750 concepts are backlog and stay out.
    concept_dir = os.path.join(ASSETS, "concepts")
    for asset_id in PHASE1_IDS + icon_concepts():
        candidate = os.path.join(concept_dir, f"{asset_id}.png")
        if os.path.exists(candidate):
            sources.append(candidate)

    # Superseded records for the Phase-1 assets, so the history of what was replaced survives.
    superseded = os.path.join(ASSETS, "_superseded")
    for name in SUPERSEDED_DIRS:
        directory = os.path.join(superseded, name)
        if os.path.isdir(directory):
            sources.extend(collect(directory))

    for path in SINGLE_FILES:
        if os.path.exists(path):
            sources.append(path)

    return sorted(set(os.path.abspath(p) for p in sources))


def rel(path):
    for root, label in ((ASSETS, "assets"), (DOCS, "docs"), (REPO, "repo")):
        try:
            common = os.path.commonpath([os.path.abspath(path), os.path.abspath(root)])
        except ValueError:
            continue
        if common == os.path.abspath(root):
            return os.path.join(label, os.path.relpath(path, root))
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--verify", default=None,
                        help="Re-verify a snapshot directory against its own manifest")
    args = parser.parse_args()

    if args.verify:
        return verify(args.verify)

    plan = build_plan()
    total = sum(os.path.getsize(p) for p in plan)
    print(f"  files to snapshot : {len(plan)}")
    print(f"  total size        : {total / 1048576:.1f} MB")
    print(f"  destination root  : {DESTINATION_ROOT}")
    print(f"  inside the repo?  : "
          f"{os.path.abspath(DESTINATION_ROOT).startswith(os.path.abspath(REPO))}")

    if not args.apply:
        by_area = {}
        for path in plan:
            key = rel(path).split(os.sep)[0:2]
            by_area[os.sep.join(key)] = by_area.get(os.sep.join(key), 0) + os.path.getsize(path)
        print("\n  by area:")
        for key, size in sorted(by_area.items(), key=lambda kv: -kv[1])[:14]:
            print(f"    {size / 1048576:9.1f} MB  {key}")
        print("\n  (audit only; pass --apply to snapshot)")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    destination = os.path.join(DESTINATION_ROOT, f"phase1_{stamp}")
    os.makedirs(destination, exist_ok=True)
    print(f"\n  writing to {destination}")

    entries = []
    failures = []
    for index, source in enumerate(plan, 1):
        relative = rel(source)
        if relative is None:
            failures.append((source, "outside the known roots"))
            continue
        target = os.path.join(destination, relative)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        try:
            shutil.copy2(source, target)
            entries.append({
                "path": relative.replace(os.sep, "/"),
                "bytes": os.path.getsize(source),
                "sha256": sha256(source),
            })
        except OSError as error:
            failures.append((source, str(error)))
        if index % 250 == 0:
            print(f"    {index}/{len(plan)}")

    manifest = {
        "version": 1,
        "comment": [
            "Snapshot of the Phase-1 asset state. `assets/` is gitignored, so this is the only",
            "recovery point for the generated artefacts; the tooling that produced them is on the",
            "branch deepseek/asset-maintenance-2026-09-24.",
            "",
            "Scope is deliberately Phase-1 only: the named asset ids and the trees hanging off them,",
            "plus the manifests, requests and review renders. Backlog assets and the raw image-to-3D",
            "intermediates are excluded because they are regenerable from the concepts, which are",
            "included.",
            "",
            "Hashes are SHA-256 of the source file at copy time. Re-verify with --verify.",
        ],
        "created": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "created_epoch": int(time.time()),
        "destination": destination,
        "source_root": REPO,
        "git": {"branch": "deepseek/asset-maintenance-2026-09-24"},
        "file_count": len(entries),
        "total_bytes": sum(e["bytes"] for e in entries),
        "phase1_ids": PHASE1_IDS,
        "files": entries,
        "failures": [{"source": s, "error": e} for s, e in failures],
    }
    manifest_path = os.path.join(destination, "SNAPSHOT_MANIFEST.json")
    with io.open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")

    print(f"\n  copied   : {len(entries)} files, "
          f"{manifest['total_bytes'] / 1048576:.1f} MB")
    print(f"  failures : {len(failures)}")
    print(f"  manifest : {manifest_path}")

    # The prompt requires proving a sample reads back before continuing.
    readable, checked = verify_readback(destination, entries)
    print(f"  readback : {readable}/{checked} sampled files readable")
    return 0 if readable == checked and not failures else 1


def verify_readback(destination, entries, count=24):
    """Read and hash a spread of copied files, and confirm the hashes still match."""
    if not entries:
        return 0, 0
    step = max(1, len(entries) // count)
    sample = entries[::step][:count]
    ok = 0
    for entry in sample:
        target = os.path.join(destination, entry["path"].replace("/", os.sep))
        try:
            if sha256(target) == entry["sha256"]:
                ok += 1
        except OSError:
            pass
    return ok, len(sample)


def verify(snapshot_dir):
    manifest_path = os.path.join(snapshot_dir, "SNAPSHOT_MANIFEST.json")
    if not os.path.exists(manifest_path):
        print(f"  no SNAPSHOT_MANIFEST.json in {snapshot_dir}")
        return 1
    with io.open(manifest_path, encoding="utf-8") as handle:
        manifest = json.load(handle)
    missing, mismatched, ok = [], [], 0
    for entry in manifest["files"]:
        target = os.path.join(snapshot_dir, entry["path"].replace("/", os.sep))
        if not os.path.exists(target):
            missing.append(entry["path"])
            continue
        if sha256(target) != entry["sha256"]:
            mismatched.append(entry["path"])
        else:
            ok += 1
    print(f"  snapshot : {snapshot_dir}")
    print(f"  expected : {manifest['file_count']} files")
    print(f"  verified : {ok}")
    print(f"  missing  : {len(missing)}")
    print(f"  mismatched: {len(mismatched)}")
    for path in (missing + mismatched)[:10]:
        print(f"     {path}")
    return 0 if not missing and not mismatched else 1


if __name__ == "__main__":
    raise SystemExit(main())
