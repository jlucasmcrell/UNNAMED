"""One command: concept images -> game-ready assets on BEAST.

Chains the two halves of the pipeline with no manual steps:
  1. ComfyUI native Pixal3D/Trellis.2 turns each concept into a raw textured GLB
  2. Blender cleans it: normalise scale/origin, single-sided, LODs, collision

Writes a combined manifest covering both stages.

Usage:
    python _make_assets.py C:\\concepts --out C:\\assets\\ready
    python _make_assets.py C:\\concepts --category weapon --faces 25000 --trellis2
    python _make_assets.py C:\\concepts --skip-generate   # re-clean existing GLBs
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
GENERATOR = os.path.join(TOOL_DIR, "_run_3d_asset.py")
HEALTH = os.path.join(TOOL_DIR, "_comfy_health.py")
CLEANUP = os.path.join(TOOL_DIR, "_blender_cleanup.py")
VERIFIER = os.path.join(TOOL_DIR, "_verify_glb.py")
CONCEPT_EXTENSIONS = (".png", ".jpg", ".jpeg", ".webp")
PYTHON = sys.executable

# Override with UNNAMED_COMFY_OUTPUT / UNNAMED_BLENDER when this moves off BEAST.
COMFY_OUTPUT = os.environ.get("UNNAMED_COMFY_OUTPUT", r"C:\Users\jluca\ComfyUI\output")
BLENDER = os.environ.get(
    "UNNAMED_BLENDER",
    r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
RIGGER = os.path.join(TOOL_DIR, "_blender_rig.py")

# Which body plan to rig a given category with. Creatures vary too much to assume,
# so they get the quadruped skeleton unless their id says otherwise.
RIG_BY_CATEGORY = {"character": "humanoid", "creature": "quadruped"}
RIG_ID_HINTS = (
    ("serpent", "worm"), ("snake", "worm"), ("wyrm", "worm"), ("worm", "worm"),
    ("drake", "quadruped"), ("wolf", "quadruped"), ("hound", "quadruped"),
    ("boar", "quadruped"), ("bear", "quadruped"), ("lizard", "quadruped"),
    ("tortoise", "quadruped"), ("stag", "quadruped"), ("ox", "quadruped"),
    ("raven", "quadruped"), ("hawk", "quadruped"), ("owl", "quadruped"),
    ("vulture", "quadruped"), ("bat", "quadruped"), ("mantis", "quadruped"),
    ("spider", "quadruped"), ("construct", "humanoid"), ("armour", "humanoid"),
    ("revenant", "humanoid"), ("wight", "humanoid"), ("servitor", "humanoid"),
    ("guardian", "humanoid"), ("walker", "humanoid"),
)


def rig_plan_for(stem, category):
    """Pick a body plan, letting the asset id override the category default."""
    lowered = stem.lower()
    for hint, plan in RIG_ID_HINTS:
        if hint in lowered:
            return plan
    return RIG_BY_CATEGORY.get(category)

# Folders are often organised by category, and the asset itself usually says what
# it is. Inferring from the stem keeps one run from forcing every asset to a single
# target size, which silently produced a 0.5 m tall character.
#
# Order matters: it is first match wins, so narrower tool names must be tested
# before the broader weapon list ("hammer" would otherwise read as a war hammer).
# The generic prefixes are tested first because ids are written as
# "<category>_<name>", which is a stronger signal than any keyword in the name.
CATEGORY_KEYWORDS = [
    # Characters are the tall case: a human is ~1.8 m, and mistaking one for a prop
    # scales it to half height. EVERY character prefix must appear here and this rule
    # must stay first. A missing prefix does not fail loudly - it falls through to the
    # prop default and yields a correctly built character at 0.5 m. That happened to
    # seven race2_ assets before race2_, racebody_ and raceclass_ were added.
    ("character", ("race_", "race2_", "racebody_", "raceclass_", "npc_",
                   "char_", "character_")),
    # Modular components. Sized absolutely by their socket definition at the modular stage,
    # so the category only needs to be distinct rather than a size default.
    ("weapon_component", ("weaponcomp_",)),
    ("armour", ("armour_", "armor_")),
    ("magic_component", ("magiccomp_",)),
    ("weapon", ("weapon_",)),
    ("tool", ("tool_",)),
    ("prop", ("prop_",)),
    ("creature", ("creature_",)),
    ("icon", ("icon_",)),
    ("item", ("item_",)),
    ("herb", ("herb_",)),
    ("flora", ("flora_",)),
    ("reagent", ("reagent_",)),
    ("resource", ("resource_",)),
    ("building", ("building_",)),
    ("material", ("material_",)),
    ("creature", ("dragon", "wolf", "beast", "monster", "animal", "spider", "bear")),
    ("building", ("house", "tower", "hut", "cabin", "gate", "bridge")),
    ("tool", ("pick", "shovel", "blacksmith", "tongs", "anvil", "saw", "chisel")),
    ("shield", ("shield", "buckler")),
    ("weapon", ("sword", "axe", "dagger", "bow", "mace", "spear", "staff", "hammer", "blade", "hatchet")),
    ("icon", ("emblem", "crest", "sigil")),
]


def asset_is_complete(ready_dir, stem):
    """True when a previous run already produced this asset's full file set."""
    asset_dir = os.path.join(ready_dir, stem)
    for suffix in (".glb", "_lod1.glb", "_collision_hull.glb", "_meta.json"):
        if not os.path.exists(os.path.join(asset_dir, f"{stem}{suffix}")):
            return False
    return True


def rig_asset(stem, category, asset_dir, base_glb, args):
    """Bind a skeleton to a finished asset and export a rigged GLB.

    Returns a record with the body plan, the weighting method and the weight report.
    An asset whose category has no sensible body plan is skipped rather than guessed
    at, so props and weapons do not acquire meaningless skeletons.
    """
    plan = rig_plan_for(stem, category)
    if not plan:
        return {"ok": True, "skipped": True, "plan": "none", "method": "none",
                "unweighted_vertices": 0,
                "note": f"no body plan for category {category}"}

    out_dir = os.path.join(args.out, "rigged", stem)
    os.makedirs(out_dir, exist_ok=True)
    command = [BLENDER, "--background", "--factory-startup",
               "--python", RIGGER, "--",
               "--input", base_glb, "--outdir", out_dir,
               "--name", stem, "--rig", plan]
    result = subprocess.run(command, capture_output=True, text=True)
    output = (result.stdout or "") + (result.stderr or "")

    # Read the report file rather than parsing stdout: Blender wraps long console
    # lines, which splits the JSON across lines and breaks naive parsing.
    report_path = os.path.join(out_dir, f"{stem}_rig.json")
    payload = None
    if os.path.exists(report_path):
        try:
            with open(report_path, encoding="utf-8") as handle:
                payload = json.load(handle)
        except (OSError, ValueError):
            payload = None
    if payload is None:
        tail = "\n".join(output.strip().splitlines()[-6:])
        return {"ok": False, "plan": plan, "method": "failed",
                "problem": tail or "no rig report written"}

    payload["ok"] = payload.get("unweighted_vertices", 1) == 0
    return payload


def write_manifest(records, args, batch_started):
    """Write this run's manifest, rewriting the same file as assets complete.

    The name is stable per run rather than timestamped per write, so a long stage
    produces one manifest that grows instead of a pile of partial files. That means
    a stage killed part way through still leaves every completed asset recorded.
    """
    manifests_dir = os.path.join(args.out, "manifests")
    os.makedirs(manifests_dir, exist_ok=True)
    label = (args.only or "all").strip().rstrip("_").replace(" ", "_")
    manifest_path = os.path.join(
        manifests_dir, f"run_{args.run_id}_{label}.json")

    ok = sum(1 for r in records if r.get("verified"))
    manifest = {
        "settings": {
            "model": "Trellis.2" if args.trellis2 else "Pixal3D",
            "faces": args.faces,
            "texture_size": args.texture_size,
            "category": args.category,
            "lod_faces": args.lod_faces,
            "seed": args.seed,
            "only": args.only,
        },
        "total_seconds": round(time.time() - batch_started, 1),
        "verified": ok,
        "attempted": len(records),
        "assets": records,
    }
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
    return manifest_path


def infer_category(stem, fallback):
    """Pick a target-size category from the asset name, falling back when unsure."""
    lowered = stem.lower()
    for category, keywords in CATEGORY_KEYWORDS:
        for keyword in keywords:
            if keyword in lowered:
                return category
    return fallback


def find_concepts(folder):
    found = []
    for root, _dirs, files in os.walk(folder):
        for name in sorted(files):
            if name.lower().endswith(CONCEPT_EXTENSIONS):
                found.append(os.path.join(root, name))
    return found


def parse_artifact(stdout):
    """Read the runner's structured ARTIFACT lines.

    Deliberately strict: guessing at output lines by shape previously matched a
    progress line instead of the real artifact.
    """
    artifacts = []
    status = nodes = None
    for line in stdout.splitlines():
        stripped = line.strip()
        if stripped.startswith("final status:"):
            status = stripped.split(":", 1)[1].strip().split()[0]
        elif stripped.startswith("nodes reporting output:"):
            nodes = stripped.split(":", 1)[1].strip()
        elif stripped.startswith("ARTIFACT "):
            parts = stripped.split()
            if len(parts) >= 3 and parts[1].lower().endswith(".glb"):
                artifacts.append((parts[1], int(parts[2])))
    return artifacts, status, nodes


def parse_cleanup(stdout):
    for line in stdout.splitlines():
        if line.startswith("CLEANUP_RESULT "):
            return json.loads(line[len("CLEANUP_RESULT "):])
    return None


def run(command, label, log):
    started = time.time()
    result = subprocess.run(command, capture_output=True, text=True)
    elapsed = time.time() - started
    output = (result.stdout or "") + (result.stderr or "")
    log.append({"stage": label, "seconds": round(elapsed, 1),
                "returncode": result.returncode})
    return output, elapsed, result.returncode


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("concepts", nargs="?", default=None,
                        help="Folder of concept images (default: <out>/concepts)")
    parser.add_argument("--out", default=r"W:\UNNAMED\assets",
                        help="Project asset root; raw/ and ready/ are created inside it")
    parser.add_argument("--raw-dir", default=None,
                        help="Override where raw generated GLBs are kept (default <out>/raw)")
    parser.add_argument("--category", default="auto",
                        help="Target-size category, or 'auto' to infer per asset from its name")
    parser.add_argument("--faces", type=int, default=25000,
                        help="ComfyUI decimate target before Blender cleanup")
    parser.add_argument("--texture-size", type=int, default=2048)
    parser.add_argument("--target-size", type=float, default=None,
                        help="Override cleanup target size in metres")
    parser.add_argument("--lod-faces", default="8000,2500,600")
    parser.add_argument("--trellis2", action="store_true")
    parser.add_argument("--seed", type=int, default=None)
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--only", default=None, help="Only process concepts whose name contains this")
    parser.add_argument("--max-seconds", type=int, default=None,
                        help="Stop cleanly once this many seconds have elapsed")
    parser.add_argument("--steps", type=int, default=None,
                        help="3D sampler steps; template default is 12/20/12")
    parser.add_argument("--bake-resolution", type=int, default=None,
                        help="Normal and AO bake resolution; template default 2048/1024")
    parser.add_argument("--upsample-resolution", type=int, default=None,
                        help="Trellis2 shape upsample target; lower keeps the intermediate "
                             "mesh small enough not to exhaust memory")
    parser.add_argument("--ao-samples", type=int, default=None,
                        help="Ambient occlusion samples; template default 64")
    parser.add_argument("--rig", action="store_true",
                        help="After building, bind a skeleton and export a rigged GLB")
    parser.add_argument("--rig-only", action="store_true",
                        help="Skip generation; rig assets already in ready/")
    parser.add_argument("--run-id", default=None,
                        help="Label for this run's manifest; defaults to a timestamp")
    parser.add_argument("--force", action="store_true",
                        help="Rebuild assets that already exist, e.g. to apply higher quality settings")
    parser.add_argument("--skip-existing", action="store_true",
                        help="Skip assets whose full file set already exists in ready/")
    parser.add_argument("--skip-generate", action="store_true",
                        help="Skip ComfyUI and re-clean raw GLBs already in raw/")
    args = parser.parse_args()
    if not args.run_id:
        args.run_id = time.strftime("%Y%m%d_%H%M%S")

    if not os.path.exists(BLENDER):
        print(f"Blender not found at {BLENDER}")
        return 1

    ready_dir = os.path.join(args.out, "ready")
    raw_dir = args.raw_dir or os.path.join(args.out, "raw")
    concepts_dir = args.concepts or os.path.join(args.out, "concepts")

    concepts = find_concepts(concepts_dir)
    if args.only:
        # Prefix match, not substring: "prop" must not also select "prop_*" style
        # ids from other categories, and callers pass a category prefix.
        selected = os.path.normcase(args.only)
        concepts = [c for c in concepts
                    if os.path.normcase(os.path.basename(c)).startswith(selected)]
    if args.limit:
        concepts = concepts[:args.limit]
    if not concepts:
        print(f"no concept images found under {concepts_dir}")
        return 1

    os.makedirs(raw_dir, exist_ok=True)
    os.makedirs(ready_dir, exist_ok=True)
    print(f"=== {len(concepts)} asset(s) ===")
    print(f"concepts : {concepts_dir}")
    print(f"raw      : {raw_dir}")
    print(f"ready    : {ready_dir}")
    print(f"model={'Trellis.2' if args.trellis2 else 'Pixal3D'} "
          f"faces={args.faces} texture={args.texture_size} "
          f"category={args.category} lods={args.lod_faces}")
    print()

    records = []
    batch_started = time.time()
    hit_time_cap = False

    for index, concept in enumerate(concepts, 1):
        stem = os.path.splitext(os.path.basename(concept))[0]
        print(f"[{index}/{len(concepts)}] {stem}")
        record = {"concept": concept, "stem": stem, "stages": []}

        # Stop cleanly at the cap rather than being killed mid-asset, so the
        # manifest and the asset file set stay consistent.
        if args.max_seconds and (time.time() - batch_started) >= args.max_seconds:
            print(f"    time cap of {args.max_seconds}s reached; "
                  f"{len(concepts) - index + 1} asset(s) left for the next run")
            hit_time_cap = True
            break

        # A finished asset is its full file set. Skipping lets an interrupted stage
        # be resumed without re-rendering everything it already produced.
        if args.skip_existing and not args.force and asset_is_complete(ready_dir, stem):
            # Read the existing meta back so the manifest still carries real
            # category and geometry figures, otherwise a resumed run reports every
            # skipped asset as unknown and the status summary goes blind.
            meta_path = os.path.join(ready_dir, stem, f"{stem}_meta.json")
            meta = {}
            try:
                with open(meta_path, encoding="utf-8") as meta_handle:
                    meta = json.load(meta_handle)
            except (OSError, ValueError):
                pass
            print("    already built, skipped")
            record = {
                "stem": stem,
                "skipped": True,
                # Count as verified: the file set is complete, which is the same
                # condition the verifier checks. Otherwise a stage whose work was
                # all done by an earlier run would report failure.
                "verified": True,
                "category": meta.get("category", infer_category(stem, "prop")),
                "cleanup": {
                    "base_faces": (meta.get("base") or {}).get("faces"),
                    "base_tris": (meta.get("base") or {}).get("triangles"),
                },
                "asset_dir": os.path.join(ready_dir, stem),
            }
            # Rigging is additive: an asset built by an earlier, unrigged run can still
            # be rigged now without rebuilding the mesh.
            if args.rig:
                asset_dir = os.path.join(ready_dir, stem)
                base_glb = os.path.join(asset_dir, f"{stem}.glb")
                record["rig"] = rig_asset(stem, record["category"], asset_dir, base_glb, args)
                if record["rig"].get("ok"):
                    note = ("skipped" if record["rig"].get("skipped")
                            else f"{record['rig']['plan']} via {record['rig']['method']}, "
                                 f"{record['rig'].get('unweighted_vertices')} unweighted")
                    print(f"    rig      {note}")
                else:
                    print(f"    rig      FAILED: {record['rig'].get('problem', '')[:70]}")
            records.append(record)
            continue

        # ---- Stage 1: generate raw GLB with ComfyUI ----
        raw_glb = None
        if args.skip_generate:
            candidate = os.path.join(raw_dir, f"{stem}.glb")
            if os.path.exists(candidate):
                raw_glb = candidate
                print("    generate: skipped (reusing existing raw GLB)")
            else:
                record["error"] = f"skip-generate requested but no raw GLB at {candidate}"
                print(f"    FAIL  {record['error']}")
                records.append(record)
                continue
        else:
            # A hung ComfyUI answers nothing but keeps accepting prompts, which makes
            # every subsequent asset fail with a misleading client timeout. Checking
            # first stops one hang from consuming the whole run.
            #
            # Reaching here means the previous asset wedged the server, so the previous
            # asset is the suspect and this one is innocent. Failing the stage would
            # discard the whole batch because of one bad source image, so record it and
            # move on; the unbuildable one ends up in the manifest for a retry pass.
            health, _elapsed, health_code = run(
                [PYTHON, HEALTH], "health", record["stages"])
            if health_code != 0:
                # A server that is merely restarting is not a reason to skip the rest of the
                # batch. `_comfy_health.py` already waits for a starting server to come back,
                # so reaching here means it was down for longer than that wait. Skipping
                # onwards would fail every remaining asset in about two seconds each, which
                # is how one guard restart cost seven assets in a row. Stop the stage instead
                # and let the supervisor's next pass pick it up once the server is healthy.
                record["error"] = f"ComfyUI unusable: {health.strip()[-400:]}"
                print("    FAIL  ComfyUI unhealthy and did not recover; stopping this stage "
                      "so the remaining assets are not skipped")
                records.append(record)
                write_manifest(records, args, batch_started)
                return 1

            prefix = f"3d/raw/{stem}"
            command = [PYTHON, GENERATOR, concept,
                       "--faces", str(args.faces),
                       "--texture-size", str(args.texture_size),
                       "--prefix", prefix]
            if args.trellis2:
                command.append("--trellis2")
            if args.seed is not None:
                command.extend(["--seed", str(args.seed)])
            if args.steps is not None:
                command.extend(["--steps", str(args.steps)])
            if args.bake_resolution is not None:
                command.extend(["--bake-resolution", str(args.bake_resolution)])
            if args.ao_samples is not None:
                command.extend(["--ao-samples", str(args.ao_samples)])
            if args.upsample_resolution is not None:
                command.extend(["--upsample-resolution", str(args.upsample_resolution)])

            output, elapsed, code = run(command, "generate", record["stages"])
            artifacts, status, nodes = parse_artifact(output)
            record["generate"] = {"status": status, "seconds": round(elapsed, 1)}
            if not artifacts:
                tail = "\n".join(output.strip().splitlines()[-10:])
                record["error"] = f"generation produced no GLB: {tail}"
                print(f"    FAIL  generate ({elapsed:.0f}s)")
                records.append(record)
                continue
            # ComfyUI writes into its own output tree; move the raw mesh into the
            # project's raw folder so the project stays self-contained.
            produced = artifacts[0][0]
            raw_glb = os.path.join(raw_dir, f"{stem}.glb")
            if os.path.abspath(produced) != os.path.abspath(raw_glb):
                shutil.copy2(produced, raw_glb)
                os.remove(produced)
            print(f"    generate {elapsed:6.1f}s  -> raw\\{os.path.basename(raw_glb)}")

        # ---- Stage 2: Blender cleanup ----
        category = args.category
        if category == "auto":
            category = infer_category(stem, "prop")
        record["category"] = category

        asset_dir = os.path.join(ready_dir, stem)
        command = [BLENDER, "--background", "--factory-startup",
                   "--python", CLEANUP, "--",
                   "--input", raw_glb, "--outdir", asset_dir,
                   "--name", stem, "--category", category,
                   "--lod-faces", args.lod_faces]
        if args.target_size is not None:
            command.extend(["--target-size", str(args.target_size)])

        output, elapsed, code = run(command, "cleanup", record["stages"])
        cleanup = parse_cleanup(output)
        if cleanup is None:
            tail = "\n".join(output.strip().splitlines()[-10:])
            record["error"] = f"cleanup failed: {tail}"
            print(f"    FAIL  cleanup ({elapsed:.0f}s)")
            records.append(record)
            continue
        record["cleanup"] = cleanup
        print(f"    cleanup  {elapsed:6.1f}s  [{category}] {cleanup['base_faces']} faces, "
              f"{cleanup['dimensions']} m, {len(cleanup['lods'])} LODs")

        # ---- Stage 3: verify the base GLB is engine-usable ----
        base_glb = os.path.join(asset_dir, f"{stem}.glb")
        output, _elapsed, _code = run([PYTHON, VERIFIER, base_glb], "verify", record["stages"])
        record["verified"] = "RESULT: complete" in output
        record["asset_dir"] = asset_dir
        record["base_glb"] = base_glb
        record["raw_glb"] = raw_glb
        if not record["verified"]:
            record["verify_output"] = output.strip()

        records.append(record)
        print(f"    verify   {'OK' if record['verified'] else 'INCOMPLETE'}")

        # ---- Stage 4: optional rigging ----
        if args.rig and record["verified"]:
            record["rig"] = rig_asset(stem, record.get("category", "prop"),
                                      asset_dir, base_glb, args)
            if record["rig"].get("ok"):
                print(f"    rig      {record['rig']['plan']} via {record['rig']['method']}  "
                      f"{record['rig'].get('unweighted_vertices')} unweighted")
            else:
                print(f"    rig      FAILED: {record['rig'].get('problem', '')[:70]}")

        write_manifest(records, args, batch_started)

    total = time.time() - batch_started
    ok = sum(1 for r in records if r.get("verified"))
    manifest_path = write_manifest(records, args, batch_started)

    print()
    print(f"{ok}/{len(records)} verified in {total / 60:.1f} min "
          f"({total / max(len(records), 1):.0f}s per asset)")
    print(f"manifest -> {manifest_path}")

    # Refresh the catalog so the game side always sees a current index.
    catalog = os.path.join(TOOL_DIR, "_catalog_assets.py")
    if os.path.exists(catalog):
        print()
        subprocess.run([PYTHON, catalog, "--assets", args.out])

    # Reaching the time cap is a planned stop, not a failure: everything attempted
    # was still verified. Reporting it as failure made the queue mark a healthy
    # stage as FAILED, which is misleading in the run log and the status summary.
    if hit_time_cap:
        return 0
    return 0 if ok == len(records) else 1


if __name__ == "__main__":
    sys.exit(main())
