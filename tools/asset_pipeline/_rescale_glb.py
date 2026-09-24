"""Restore correct real-world size to assets the pipeline normalised by longest axis.

The 3D pipeline scaled every asset so its LONGEST AXIS matched a per-category default (`prop`
0.5 m, `weapon` 1.2 m, `creature` 1.8 m). Shape is therefore intact and only size is wrong, so the
correction is a uniform scale by `expected_longest_m / measured_longest_m`. That is the inverse of
the operation that broke it, which is why it needs no Blender re-export: the geometry, UVs,
textures, materials, LODs and collision all stay exactly as they are.

Rewrites the POSITION accessor data in place and updates its `min`/`max`, then re-seats the result
so the base is back on the ground plane and the footprint is centred, because scaling about the
origin is only correct if the origin was already at the base.

Refuses to touch a skinned mesh: a skinned GLB carries inverse bind matrices expressed in the old
scale, and scaling only the vertices would silently break every joint. Characters are handled by
the bind pipeline instead.

Usage:
    python _rescale_glb.py --audit --family flora prop travel container   # dry run
    python _rescale_glb.py --apply --family flora prop
    python _rescale_glb.py --apply --asset prop_iron_banded_oak_door
"""
import argparse
import json
import os
import shutil
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
AUDIT = os.path.join(ASSETS, "manifests", "scale_audit.json")
ARCHIVE = os.path.join(ASSETS, "archive", "rescale")
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
FLOAT = 5126

VARIANTS = ("", "_lod1", "_lod2", "_lod3", "_collision_hull", "_collision_box")


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
            binary = bytearray(payload)
        offset += chunk_length
    if gltf is None or binary is None:
        raise ValueError("missing JSON or BIN chunk")
    return version, gltf, binary


def write_glb(path, version, gltf, binary):
    text = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    text += b" " * ((4 - len(text) % 4) % 4)
    chunks = [struct.pack("<II", len(text), JSON_CHUNK), text]
    padded = bytes(binary) + b"\x00" * ((4 - len(binary) % 4) % 4)
    chunks += [struct.pack("<II", len(padded), BIN_CHUNK), padded]
    body = b"".join(chunks)
    with open(path, "wb") as handle:
        handle.write(struct.pack("<4sII", b"glTF", version, 12 + len(body)) + body)


def position_views(gltf):
    """Every accessor index used as POSITION, with its buffer view and stride."""
    use = set()
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            index = prim.get("attributes", {}).get("POSITION")
            if index is not None:
                use.add(index)
    return use


def mesh_bounds(gltf):
    lows = [1e9] * 3
    highs = [-1e9] * 3
    for index in position_views(gltf):
        acc = gltf["accessors"][index]
        if "min" in acc and "max" in acc:
            for axis in range(3):
                lows[axis] = min(lows[axis], acc["min"][axis])
                highs[axis] = max(highs[axis], acc["max"][axis])
    return lows, highs


def transform_positions(gltf, binary, scale, offset):
    """Apply v -> v * scale + offset to every POSITION accessor, in place."""
    touched = 0
    for index in position_views(gltf):
        acc = gltf["accessors"][index]
        if acc["componentType"] != FLOAT or acc["type"] != "VEC3":
            raise ValueError(f"accessor {index} is not float VEC3")
        view = gltf["bufferViews"][acc["bufferView"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        stride = view.get("byteStride") or 12
        for i in range(acc["count"]):
            base = start + i * stride
            x, y, z = struct.unpack_from("<fff", binary, base)
            struct.pack_into("<fff", binary, base,
                             x * scale + offset[0], y * scale + offset[1], z * scale + offset[2])
            touched += 1
        if "min" in acc:
            acc["min"] = [acc["min"][a] * scale + offset[a] for a in range(3)]
            acc["max"] = [acc["max"][a] * scale + offset[a] for a in range(3)]
    return touched


def process(path, scale, dry_run):
    """Scale one GLB, re-seating it on the ground. Returns (ok, detail)."""
    version, gltf, binary = read_glb(path)
    if gltf.get("skins"):
        return False, "skinned: refusing to scale vertices under inverse bind matrices"
    lows, highs = mesh_bounds(gltf)
    if lows[0] > 1e8:
        return False, "no position bounds"
    # Scale about the origin, then re-seat: base on the ground, footprint centred.
    scaled_lows = [v * scale for v in lows]
    scaled_highs = [v * scale for v in highs]
    offset = [
        -(scaled_lows[0] + scaled_highs[0]) / 2.0,
        -scaled_lows[1],
        -(scaled_lows[2] + scaled_highs[2]) / 2.0,
    ]
    if dry_run:
        return True, (f"{max(highs[a] - lows[a] for a in range(3)):.4f} m -> "
                      f"{max(highs[a] - lows[a] for a in range(3)) * scale:.4f} m "
                      f"(x{scale:.3f})")
    archive = os.path.join(ARCHIVE, os.path.basename(os.path.dirname(path)))
    os.makedirs(archive, exist_ok=True)
    backup = os.path.join(archive, os.path.basename(path))
    if not os.path.exists(backup):
        shutil.copy2(path, backup)
    vertices = transform_positions(gltf, binary, scale, offset)
    write_glb(path, version, gltf, binary)
    # Verify by re-reading: a rewrite that does not parse is worse than a wrong size.
    _v, check, _b = read_glb(path)
    new_lows, new_highs = mesh_bounds(check)
    got = max(new_highs[a] - new_lows[a] for a in range(3))
    want = max(highs[a] - lows[a] for a in range(3)) * scale
    if abs(got - want) > max(0.002, want * 0.01):
        shutil.copy2(backup, path)
        return False, f"verification failed ({got:.4f} vs {want:.4f}); original restored"
    if abs(new_lows[1]) > 0.01:
        shutil.copy2(backup, path)
        return False, f"base not on the ground after scaling (Y={new_lows[1]:.4f}); restored"
    return True, f"{vertices} verts, longest axis -> {got:.4f} m"


def sync_meta(directory, asset_id, dry_run):
    """Rewrite the meta's dimensions from the scaled GLB.

    The catalog and the scale audit both read `_meta.json`, so leaving it stale makes a correctly
    rescaled 14 m tree report as 0.5 m and look unfixed. The meta records Blender's Z-up frame,
    so the export-frame bounds convert back: x = X, y = -Z, z = Y.
    """
    meta_path = os.path.join(directory, f"{asset_id}_meta.json")
    glb_path = os.path.join(directory, f"{asset_id}.glb")
    if not (os.path.exists(meta_path) and os.path.exists(glb_path)):
        return False
    _version, gltf, _binary = read_glb(glb_path)
    lows, highs = mesh_bounds(gltf)
    if lows[0] > 1e8:
        return False
    blender_dims = [highs[0] - lows[0], highs[2] - lows[2], highs[1] - lows[1]]
    blender_min = [lows[0], -highs[2], lows[1]]
    blender_max = [highs[0], -lows[2], highs[1]]
    if dry_run:
        return True
    meta = json.load(open(meta_path, encoding="utf-8"))
    meta["target_size_m"] = round(max(blender_dims), 4)
    meta.setdefault("transform", {})
    meta["transform"]["dimensions"] = [round(v, 4) for v in blender_dims]
    meta["transform"]["min"] = [round(v, 4) for v in blender_min]
    meta["transform"]["max"] = [round(v, 4) for v in blender_max]
    meta["transform"]["rescaled_by"] = "semantic_scale_correction"
    if "collision" in meta:
        meta["collision"]["box_dimensions"] = [round(v, 4) for v in blender_dims]
    with open(meta_path, "w", encoding="utf-8") as handle:
        json.dump(meta, handle, indent=2)
    return True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true", help="Report only (default)")
    parser.add_argument("--apply", action="store_true", help="Rewrite the files")
    parser.add_argument("--family", nargs="*", default=None)
    parser.add_argument("--asset", nargs="*", default=None)
    parser.add_argument("--min-ratio", type=float, default=1.05,
                        help="Skip assets already within this factor of expectation")
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--resync", action="store_true",
                        help="Do not scale; just rewrite _meta.json from the current geometry. "
                             "Use after a rescale, because the scale factor is then ~1.0 and the "
                             "assets would otherwise be skipped as already correct.")
    args = parser.parse_args()
    dry_run = not args.apply

    audit = json.load(open(AUDIT, encoding="utf-8"))
    results = {r["asset_id"]: r for r in audit["results"]}

    targets = []
    for asset_id, record in sorted(results.items()):
        if args.asset and asset_id not in args.asset:
            continue
        if args.family and asset_id.split("_")[0] not in args.family:
            continue
        expected = record.get("expected_longest_m")
        measured = record.get("measured_longest_m")
        if not expected or not measured:
            continue
        factor = expected / measured
        if not args.resync and 1.0 / args.min_ratio <= factor <= args.min_ratio:
            continue
        targets.append((asset_id, measured, expected, factor))
    if args.limit:
        targets = targets[:args.limit]

    print(f"  {'asset':<44} {'from':>8} {'to':>8} {'factor':>8}  result")
    print("  " + "-" * 92)
    changed = skipped = failed = 0
    for asset_id, measured, expected, factor in targets:
        directory = os.path.join(READY, asset_id)
        if not os.path.isdir(directory):
            continue
        details = []
        if args.resync:
            if sync_meta(directory, asset_id, dry_run):
                changed += 1
                print(f"  {asset_id:<44} meta resynced from geometry")
            else:
                skipped += 1
            continue
        for variant in VARIANTS:
            path = os.path.join(directory, f"{asset_id}{variant}.glb")
            if not os.path.exists(path):
                continue
            try:
                ok, detail = process(path, factor, dry_run)
            except (ValueError, OSError, struct.error) as exc:
                ok, detail = False, f"{type(exc).__name__}: {exc}"
            if not ok:
                failed += 1
                print(f"  {asset_id:<44} {'':>8} {'':>8} {factor:>8.3f}  FAIL {variant}: {detail}")
                break
            details.append(f"{variant or 'base'}:{detail}")
        else:
            if sync_meta(directory, asset_id, dry_run):
                changed += 1
                print(f"  {asset_id:<44} {measured:>8.4f} {expected:>8.4f} {factor:>8.3f}  "
                      f"{details[0] if details else 'no files'}")
            else:
                skipped += 1

    print()
    print(f"  {'would rescale' if dry_run else 'rescaled'}: {changed}   failed: {failed}")
    if dry_run:
        print("  (dry run; pass --apply to rewrite. Originals are archived either way.)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
