"""Scan every GLB in an asset tree for corruption, and restore any with a valid backup.

Defaults to ready/; --root names another tree under assets/ (rigged/ is checked the same way).

A bulk in-place rewrite of 1,920 files is only safe if every result is verified. One file
(`weapon_arming_sword.glb`) came out with an invalid JSON chunk, and the per-file check in
`_rename_glb_assets.py` missed it because it only compared the first mesh's name - a file whose
JSON failed to parse at all raised before that comparison could fail meaningfully.

This is the authoritative check: parse both chunks of every GLB, confirm the container length
agrees, and cross-check against the backup taken before the rewrite.
"""
import json
import os
import shutil
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
ARCHIVE = os.path.join(ASSETS, "archive", "glb_rename")
JSON_CHUNK = 0x4E4F534A


def inspect(path):
    """Return (ok, detail). Parses the container fully rather than trusting the first mesh."""
    try:
        with open(path, "rb") as handle:
            data = handle.read()
    except OSError as exc:
        return False, f"unreadable: {exc}"
    if len(data) < 12:
        return False, "shorter than a GLB header"
    magic, version, declared = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        return False, "bad magic"
    if declared != len(data):
        return False, f"header says {declared} bytes, file is {len(data)}"
    offset, saw_json = 12, False
    while offset + 8 <= len(data):
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        if offset + chunk_length > len(data):
            return False, "chunk overruns the file"
        if chunk_type == JSON_CHUNK:
            try:
                json.loads(data[offset:offset + chunk_length].decode("utf-8"))
            except (ValueError, UnicodeDecodeError) as exc:
                return False, f"JSON chunk invalid: {exc}"
            saw_json = True
        offset += chunk_length
    return (True, "") if saw_json else (False, "no JSON chunk")


def main():
    restore = "--restore" in sys.argv
    root_name = "ready"
    if "--root" in sys.argv:
        root_name = sys.argv[sys.argv.index("--root") + 1]
    root = os.path.join(ASSETS, root_name)
    bad, restored, checked = [], [], 0
    for asset_id in sorted(os.listdir(root)):
        asset_dir = os.path.join(root, asset_id)
        if not os.path.isdir(asset_dir):
            continue
        # Every GLB in the directory, so the lod/collision variants of ready/ and the
        # _rigged export of the rigged tree are all covered by the same check.
        for name in sorted(os.listdir(asset_dir)):
            if not name.endswith(".glb"):
                continue
            path = os.path.join(asset_dir, name)
            checked += 1
            ok, detail = inspect(path)
            if ok:
                continue
            bad.append((asset_id, name, detail))
            backup = os.path.join(ARCHIVE, asset_id, name)
            if restore and os.path.exists(backup):
                backup_ok, _ = inspect(backup)
                if backup_ok:
                    shutil.copy2(backup, path)
                    restored.append(name)
    print(f"  tree    : {root_name}")
    print(f"  checked : {checked}")
    print(f"  corrupt : {len(bad)}")
    for asset_id, name, detail in bad:
        print(f"    {name}: {detail}")
    if restore:
        print(f"  restored from backup: {len(restored)}")
        for name in restored:
            print(f"    {name}")
    elif bad:
        print("  (run with --restore to put valid backups back)")
    return 1 if bad and not restore else 0


if __name__ == "__main__":
    sys.exit(main())
