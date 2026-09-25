"""Promote a verified remediation into the asset library, keeping what it replaces.

Every file it overwrites is first copied, hash-checked, to
  G:/UNNAMED_HISTORY/asset_remediation/superseded/<id>/<stamp>/<same relative path>
with a REASON.md, so nothing is lost and --rollback <stamp> restores it. Drawn GLBs (a base,
its LODs, a rigged skin, a clip) must pass _verify_glb.py after the copy or the promotion is
rolled back. The asset's meta.json gains a 'remediation' entry: the reason, the source files
and their hashes, the staged provenance record, the visual-review verdict and the role and
quality tier from assets/manifests/asset_roles.json.

  python _promote_remediated.py --id prop_cart_damaged_merchant \
      --file assets/_staging/procedural/prop_cart_damaged_merchant/prop_cart_damaged_merchant.glb=ready/prop_cart_damaged_merchant/prop_cart_damaged_merchant.glb \
      --lods-from assets/_staging/lods/prop_cart_damaged_merchant \
      --provenance assets/_staging/procedural/prop_cart_damaged_merchant/prop_cart_damaged_merchant_provenance.json \
      --reason "procedural replacement: image-to-3D fails open-frame carts" --review "pass: ..." --refresh-geometry
  python _promote_remediated.py --id <id> --rollback <stamp>
"""
import argparse
import datetime
import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
ASSETS = os.environ.get("UNNAMED_ASSETS", os.path.join(REPO, "assets"))
HISTORY = os.environ.get("UNNAMED_HISTORY", r"G:\UNNAMED_HISTORY\asset_remediation\superseded")
VERIFIER = os.path.join(TOOL_DIR, "_verify_glb.py")
REFRESH = os.path.join(TOOL_DIR, "_blender_refresh_collision.py")
ROLES = os.path.join(REPO, "assets", "manifests", "asset_roles.json")
BLENDER = os.environ.get("UNNAMED_BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def triangles(path):
    with open(path, "rb") as handle:
        data = handle.read()
    length = struct.unpack_from("<I", data, 8)[0]
    offset = 12
    while offset < length:
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        if chunk_type == 0x4E4F534A:
            gltf = json.loads(data[offset + 8:offset + 8 + chunk_len])
            return sum(gltf["accessors"][p["indices"]]["count"] // 3
                       for m in gltf.get("meshes", []) for p in m["primitives"] if "indices" in p)
        offset += 8 + chunk_len
    return 0


def verify(path):
    check = subprocess.run([sys.executable, VERIFIER, path], capture_output=True, text=True)
    last = (check.stdout or "").strip().splitlines()[-1:] or ["unreadable"]
    return "RESULT: complete" in (check.stdout or ""), last[0].strip()


def drawn(relative):
    # A clip (animation/...) carries no mesh; collision proxies are never drawn.
    name = os.path.basename(relative)
    return name.endswith(".glb") and "_collision_" not in name and not relative.replace("\\", "/").startswith("animation/")


def rollback(asset_id, stamp):
    folder = os.path.join(HISTORY, asset_id, stamp)
    with open(os.path.join(folder, "manifest.json"), encoding="utf-8") as handle:
        manifest = json.load(handle)
    for entry in manifest["replaced"]:
        target = os.path.join(ASSETS, entry["path"])
        if entry["sha256"] is None:
            if os.path.exists(target):
                os.remove(target)
            continue
        kept = os.path.join(folder, entry["path"])
        if sha256(kept) != entry["sha256"]:
            raise SystemExit(f"superseded copy {kept} does not match its recorded hash; not restoring")
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copy2(kept, target)
    print(f"ROLLED BACK {asset_id} to the files kept at {folder}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--id", required=True)
    parser.add_argument("--file", action="append", default=[],
                        help="<source>=<destination relative to assets/>; repeatable")
    parser.add_argument("--lods-from", default=None, help="folder holding <id>_lod1..3.glb (and its lod report)")
    parser.add_argument("--provenance", default=None, help="the staged provenance JSON")
    parser.add_argument("--reason", default=None)
    parser.add_argument("--review", default=None, help="the visual-review verdict, as recorded")
    parser.add_argument("--refresh-geometry", action="store_true",
                        help="rebuild collision proxies and the measured transform from the new base")
    parser.add_argument("--remove", action="append", default=[],
                        help="a file (relative to assets/) the new version retires: kept like the rest, then deleted")
    parser.add_argument("--lod-policy", choices=["none"], default=None,
                        help="record that the asset has no LODs by design (e.g. a building of 2k triangles)")
    parser.add_argument("--rollback", default=None, help="restore the files kept under this stamp")
    parser.add_argument("--stamp", default=None)
    args = parser.parse_args()

    if args.rollback:
        rollback(args.id, args.rollback)
        return 0
    if not args.reason:
        raise SystemExit("--reason is required: say why the library copy is replaced")

    asset_id = args.id
    pairs = []
    for spec in args.file:
        source, _, destination = spec.partition("=")
        pairs.append((os.path.abspath(source), destination.replace("\\", "/")))
    if args.lods_from:
        for level in (1, 2, 3):
            name = f"{asset_id}_lod{level}.glb"
            pairs.append((os.path.join(args.lods_from, name), f"ready/{asset_id}/{name}"))
    for source, destination in pairs:
        if not os.path.isfile(source):
            raise SystemExit(f"no source file {source}")
        if drawn(destination):
            ok, result = verify(source)
            if not ok:
                raise SystemExit(f"{source} fails the verifier ({result}); nothing promoted")

    stamp = args.stamp or datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    kept = os.path.join(HISTORY, asset_id, stamp)
    meta_rel = f"ready/{asset_id}/{asset_id}_meta.json"
    removed = [r.replace("\\", "/") for r in args.remove]
    touched = [d for _, d in pairs] + [meta_rel] + removed
    if args.refresh_geometry:
        touched += [f"ready/{asset_id}/{asset_id}_collision_hull.glb", f"ready/{asset_id}/{asset_id}_collision_box.glb"]
    manifest = {"asset_id": asset_id, "stamp": stamp, "reason": args.reason, "replaced": []}
    for relative in dict.fromkeys(touched):
        target = os.path.join(ASSETS, relative)
        entry = {"path": relative, "sha256": None}
        if os.path.exists(target):
            copy = os.path.join(kept, relative)
            os.makedirs(os.path.dirname(copy), exist_ok=True)
            shutil.copy2(target, copy)
            entry["sha256"] = sha256(target)
            if sha256(copy) != entry["sha256"]:
                raise SystemExit(f"superseded copy of {relative} does not match; nothing promoted")
        manifest["replaced"].append(entry)
    os.makedirs(kept, exist_ok=True)
    with open(os.path.join(kept, "manifest.json"), "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
    with open(os.path.join(kept, "REASON.md"), "w", encoding="utf-8") as handle:
        handle.write(f"# {asset_id}: superseded {stamp}\n\n{args.reason}\n\nReplaced by:\n")
        for source, destination in pairs:
            handle.write(f"- {destination} <- {source} (sha256 {sha256(source)})\n")
        handle.write("\nRestore with: python tools/asset_pipeline/_promote_remediated.py "
                     f"--id {asset_id} --rollback {stamp}\n")

    try:
        for relative in removed:
            if os.path.exists(os.path.join(ASSETS, relative)):
                os.remove(os.path.join(ASSETS, relative))
        for source, destination in pairs:
            target = os.path.join(ASSETS, destination)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copy2(source, target)
            if drawn(destination):
                ok, result = verify(target)
                if not ok:
                    raise RuntimeError(f"{destination} fails the verifier after the copy ({result})")

        meta_path = os.path.join(ASSETS, meta_rel)
        meta = {}
        if os.path.exists(meta_path):
            with open(meta_path, encoding="utf-8") as handle:
                meta = json.load(handle)
        base = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}.glb")
        if args.refresh_geometry:
            run = subprocess.run([BLENDER, "--background", "--factory-startup", "--python", REFRESH, "--",
                                  "--input", base, "--outdir", os.path.dirname(base), "--name", asset_id],
                                 capture_output=True, text=True)
            line = next((l for l in (run.stdout or "").splitlines() if l.startswith("REFRESH_RESULT ")), None)
            if line is None:
                raise RuntimeError("collision refresh failed: " + "\n".join((run.stdout + run.stderr).splitlines()[-8:]))
            refreshed = json.loads(line[len("REFRESH_RESULT "):])
            previous = meta.get("target_size_m")
            meta["transform"] = refreshed["transform"]
            meta["target_size_m"] = max(refreshed["transform"]["dimensions"])
            meta["base"] = refreshed["base"]
            meta["collision"] = refreshed["collision"]
            if previous is not None and abs(previous - meta["target_size_m"]) > 1e-4:
                meta["target_size_m_before_remediation"] = previous
            if meta.get("collision_policy") == "box":  # modules carry a box proxy only
                hull = os.path.join(os.path.dirname(base), f"{asset_id}_collision_hull.glb")
                if os.path.exists(hull):
                    os.remove(hull)
                meta["collision"].pop("convex_hull_faces", None)
        if args.lods_from:
            meta["lods"] = {f"{asset_id}_lod{n}": {"faces": triangles(os.path.join(ASSETS, "ready", asset_id, f"{asset_id}_lod{n}.glb"))}
                            for n in (1, 2, 3)}
            meta["lod_status"] = "rebuilt: textured, gated against LOD0 (_rebuild_lods.py)"
            report = os.path.join(args.lods_from, f"{asset_id}_lod_report.json")
            if os.path.exists(report):
                with open(report, encoding="utf-8") as handle:
                    meta["lod_report"] = {k: v for k, v in json.load(handle).items() if k in ("passed", "lods", "tool", "lod0_sha1")}
        with open(ROLES, encoding="utf-8") as handle:
            roles = json.load(handle)
        role = roles["assets"].get(asset_id)
        if role:
            meta["quality_tier"] = role["tier"]
            meta["role"] = {k: role[k] for k in ("role", "view_m", "method", "why")}
        record = {
            "stamp": stamp, "reason": args.reason, "visual_review": args.review,
            "files": [{"path": d, "source": os.path.relpath(s, REPO).replace("\\", "/") if s.startswith(REPO) else s,
                       "sha256": sha256(s)} for s, d in pairs],
            "superseded": kept,
        }
        if args.provenance:
            with open(args.provenance, encoding="utf-8") as handle:
                record["provenance"] = json.load(handle)
        meta.setdefault("remediation", []).append(record)
        if args.review:
            meta["visual_review"] = args.review
        if args.lod_policy:
            meta["lod_policy"] = args.lod_policy
            meta.pop("lods", None)
        with open(meta_path, "w", encoding="utf-8") as handle:
            json.dump(meta, handle, indent=2)
    except Exception as error:  # restore what was kept, then report
        rollback(asset_id, stamp)
        raise SystemExit(f"promotion of {asset_id} failed and was rolled back: {error}")

    print(f"PROMOTED {asset_id} ({len(pairs)} file(s)); superseded copies at {kept}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
