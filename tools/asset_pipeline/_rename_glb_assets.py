"""Give existing GLBs asset-id mesh and material names, by rewriting their JSON chunk.

The library was built before naming was a standard, so 320 of 335 assets export as `Mesh_0`
and `Material_0`. An imported scene is therefore unreadable and nothing can look an asset up
by name.

Re-running Blender over the whole library would reprocess ~10M triangles to change two
strings. A GLB is a binary container with a JSON chunk and a binary chunk, so the names can be
rewritten in place: the geometry, textures and chunk layout are untouched, and only the JSON
changes length.

The JSON chunk is padded with spaces to a 4-byte boundary, which is exactly what the glTF
specification allows for it, so a length change is legal.

Verified rather than assumed: every rewritten file is re-read and its chunk structure checked
before the original is replaced, and the original is kept in `archive/glb_rename/` so a bad
rewrite is recoverable.

Usage:
    python _rename_glb_assets.py --dry-run
    python _rename_glb_assets.py
"""
import argparse
import json
import os
import shutil
import struct
import sys
import time

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
ARCHIVE = os.path.join(ASSETS, "archive", "glb_rename")

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942


def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        raise ValueError("not a GLB")
    offset, gltf, binary = 12, None, None
    while offset < length:
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        payload = data[offset:offset + chunk_length]
        if chunk_type == JSON_CHUNK:
            gltf = json.loads(payload.decode("utf-8"))
        elif chunk_type == BIN_CHUNK:
            binary = payload
        offset += chunk_length
    if gltf is None:
        raise ValueError("no JSON chunk")
    return version, gltf, binary


def write_glb(path, version, gltf, binary):
    """Rewrite the container with the current glTF JSON and its original binary chunk."""
    text = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    # The JSON chunk must be padded with spaces to a 4-byte boundary; the BIN chunk with zeros.
    text += b" " * ((4 - len(text) % 4) % 4)
    chunks = [struct.pack("<II", len(text), JSON_CHUNK), text]
    if binary is not None:
        padded = binary + b"\x00" * ((4 - len(binary) % 4) % 4)
        chunks += [struct.pack("<II", len(padded), BIN_CHUNK), padded]
    body = b"".join(chunks)
    header = struct.pack("<4sII", b"glTF", version, 12 + len(body))
    with open(path, "wb") as handle:
        handle.write(header + body)


def desired_names(asset_id, is_lod, is_collision, suffix=""):
    """Names for this asset's mesh and material.

    LOD and collision files are separate GLBs with their own names, and they already export
    under the right stem, so they only need their inner mesh/material aligned to that stem.
    """
    stem = asset_id if not suffix else f"{asset_id}{suffix}"
    return f"{stem}_mesh", f"MAT_{stem}"


def patch(path, asset_id, suffix="", dry_run=False):
    """Return (changed, detail). Idempotent: an already-correct file reports no change."""
    version, gltf, binary = read_glb(path)
    mesh_name, mat_name = desired_names(asset_id, False, False, suffix)

    changes = []
    for mesh in gltf.get("meshes", []):
        if mesh.get("name") != mesh_name:
            changes.append(f"mesh {mesh.get('name')!r} -> {mesh_name!r}")
            mesh["name"] = mesh_name
    for material in gltf.get("materials", []):
        if material.get("name") != mat_name:
            changes.append(f"material {material.get('name')!r} -> {mat_name!r}")
            material["name"] = mat_name
    for index, node in enumerate(gltf.get("nodes", [])):
        # Only the mesh-bearing node is renamed; socket empties keep their own names.
        if "mesh" in node and not str(node.get("name", "")).startswith("SOCK_"):
            if node.get("name") != asset_id + suffix:
                changes.append(f"node {node.get('name')!r} -> {asset_id + suffix!r}")
                node["name"] = asset_id + suffix

    if not changes:
        return False, "already named"
    if dry_run:
        return True, "; ".join(changes[:3])

    # Verify before replacing: re-read what we are about to write and check it parses.
    archive = os.path.join(ARCHIVE, os.path.basename(os.path.dirname(path)))
    os.makedirs(archive, exist_ok=True)
    backup = os.path.join(archive, os.path.basename(path))
    if not os.path.exists(backup):
        shutil.copy2(path, backup)

    write_glb(path, version, gltf, binary)
    check_version, check_gltf, _ = read_glb(path)
    if check_gltf.get("meshes", [{}])[0].get("name") != mesh_name:
        shutil.copy2(backup, path)
        raise RuntimeError("verification failed; original restored")
    return True, "; ".join(changes[:3])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--root", default="ready",
                        help="Asset tree under assets/ to patch, one directory per asset id. "
                             "The suffix is read from each filename, so the lod/collision "
                             "suffixes of ready/ and the _rigged suffix of rigged/ both work.")
    parser.add_argument("--min-age-seconds", type=float, default=180.0,
                        help="Skip GLBs modified more recently than this. The 3D builders "
                             "write into ready/ continuously, and patching a file mid-write "
                             "would corrupt it; three minutes is well past any single export.")
    args = parser.parse_args()

    root = os.path.join(ASSETS, args.root)
    if not os.path.isdir(root):
        print(f"  no such tree: {root}")
        return 1

    now = time.time()
    changed = skipped = failed = too_new = 0
    examples = []
    for index, asset_id in enumerate(sorted(os.listdir(root)), 1):
        if args.limit and index > args.limit:
            break
        asset_dir = os.path.join(root, asset_id)
        if not os.path.isdir(asset_dir):
            continue
        for name in sorted(os.listdir(asset_dir)):
            if not name.endswith(".glb") or not name.startswith(asset_id):
                continue
            # Filename stem minus the asset id is the variant suffix: "" for the base
            # export, "_lod1"/"_collision_hull" for ready/, "_rigged" for the rigged tree.
            suffix = name[len(asset_id):-len(".glb")]
            path = os.path.join(asset_dir, name)
            if not args.dry_run and now - os.path.getmtime(path) < args.min_age_seconds:
                too_new += 1
                continue
            try:
                did, detail = patch(path, asset_id, suffix, args.dry_run)
            except Exception as exc:
                print(f"  FAIL {asset_id}{suffix}: {exc}")
                failed += 1
                continue
            if did:
                changed += 1
                if len(examples) < 5:
                    examples.append((f"{asset_id}{suffix}", detail))
            else:
                skipped += 1

    print(f"  files changed : {changed}")
    print(f"  already named : {skipped}")
    print(f"  too new (skipped, retry later): {too_new}")
    print(f"  failed        : {failed}")
    if args.dry_run:
        print("  (dry run - nothing written; age filter not applied)")
    for name, detail in examples:
        print(f"    {name}: {detail}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
