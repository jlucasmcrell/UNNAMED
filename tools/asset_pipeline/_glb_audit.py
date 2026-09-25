"""Read GLB files and report geometry, materials, textures and LOD state.

Written for the regression audit. Nothing here generates or modifies an asset; it only reads what is
already on disk, because the audit's first job is to establish what actually exists rather than what
was intended.

A GLB is a 12-byte header, then chunks. The first chunk is JSON describing the scene; the second is
the binary buffer. Parsing the JSON chunk gives the counts directly, and the buffer views give the
byte sizes of the embedded images, which is how a texture's real resolution is recovered when the
file embeds it rather than pointing at a sibling PNG.

Usage:
    python _glb_audit.py --paths <dir_or_glb> [<dir_or_glb> ...]
    python _glb_audit.py --library --group-by-dir
    python _glb_audit.py --paths ... --json out.json
"""
import argparse
import io
import json
import os
import re
import struct
import sys
from collections import defaultdict

ASSETS = r"W:\UNNAMED\assets"

# GLB component types, for reading accessor min/max without decoding the whole buffer.
COMPONENT = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2),
             5125: ("I", 4), 5126: ("f", 4)}
COMPONENT_COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}

# PNG IHDR sits at byte 16; JPEG SOF markers have to be walked. Only the header is read.
PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


def png_size(data):
    if data[:8] != PNG_MAGIC or len(data) < 24:
        return None
    width, height = struct.unpack(">II", data[16:24])
    return width, height


def jpeg_size(data):
    if data[:2] != b"\xff\xd8":
        return None
    index = 2
    while index < len(data) - 9:
        if data[index] != 0xFF:
            index += 1
            continue
        marker = data[index + 1]
        if marker in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7,
                      0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
            height, width = struct.unpack(">HH", data[index + 5:index + 9])
            return width, height
        if marker in (0xD8, 0xD9) or 0xD0 <= marker <= 0xD7:
            index += 2
            continue
        length = struct.unpack(">H", data[index + 2:index + 4])[0]
        index += 2 + length
    return None


def read_glb(path):
    with open(path, "rb") as handle:
        blob = handle.read()
    if blob[:4] != b"glTF":
        return None
    version, total = struct.unpack("<II", blob[4:12])
    offset = 12
    document = None
    binary = b""
    while offset < len(blob) - 8:
        length, kind = struct.unpack("<II", blob[offset:offset + 8])
        chunk = blob[offset + 8:offset + 8 + length]
        if kind == 0x4E4F534A:
            document = json.loads(chunk.decode("utf-8"))
        elif kind == 0x004E4942:
            binary = chunk
        offset += 8 + length + ((4 - length % 4) % 4) * 0
        offset += (-length) % 4
    return {"version": version, "bytes": total, "json": document, "bin": binary,
            "file_bytes": len(blob)}


def analyse(path):
    glb = read_glb(path)
    if glb is None or glb["json"] is None:
        return {"path": path, "error": "not a readable GLB"}

    doc = glb["json"]
    meshes = doc.get("meshes", [])
    accessors = doc.get("accessors", [])
    materials = doc.get("materials", [])
    images = doc.get("images", [])
    textures = doc.get("textures", [])
    nodes = doc.get("nodes", [])
    skins = doc.get("skins", [])
    animations = doc.get("animations", [])

    primitives = [p for m in meshes for p in m.get("primitives", [])]
    triangles = 0
    vertices = 0
    for prim in primitives:
        index = prim.get("indices")
        if index is not None and index < len(accessors):
            triangles += accessors[index].get("count", 0) // 3
        position = prim.get("attributes", {}).get("POSITION")
        if position is not None and position < len(accessors):
            vertices += accessors[position].get("count", 0)

    # Bounding box from POSITION accessor min/max, which is where a wrong scale or a floating,
    # ungrounded origin shows up.
    low = [float("inf")] * 3
    high = [float("-inf")] * 3
    for prim in primitives:
        position = prim.get("attributes", {}).get("POSITION")
        if position is None or position >= len(accessors):
            continue
        accessor = accessors[position]
        if "min" in accessor and "max" in accessor:
            low = [min(a, b) for a, b in zip(low, accessor["min"])]
            high = [max(a, b) for a, b in zip(high, accessor["max"])]

    # Which materials are actually referenced by a primitive. A material defined but never used, or a
    # primitive with no material at all, is exactly the "missing materials" report.
    used = set()
    unbound = 0
    for prim in primitives:
        if "material" in prim:
            used.add(prim["material"])
        else:
            unbound += 1

    images_out = []
    for image in images:
        entry = {"name": image.get("name"), "mime": image.get("mimeType")}
        if "bufferView" in image:
            view = doc.get("bufferViews", [])[image["bufferView"]]
            start = view.get("byteOffset", 0)
            length = view.get("byteLength", 0)
            payload = glb["bin"][start:start + length]
            size = png_size(payload) or jpeg_size(payload)
            entry["embedded_bytes"] = length
            entry["resolution"] = f"{size[0]}x{size[1]}" if size else "unknown"
        elif "uri" in image:
            entry["uri"] = image["uri"]
            candidate = os.path.join(os.path.dirname(path), image["uri"])
            if os.path.exists(candidate):
                with open(candidate, "rb") as handle:
                    payload = handle.read(64)
                size = png_size(payload) or jpeg_size(payload)
                entry["resolution"] = f"{size[0]}x{size[1]}" if size else "unknown"
                entry["external_bytes"] = os.path.getsize(candidate)
            else:
                entry["resolution"] = "missing file"
        images_out.append(entry)

    # A material with no base colour texture and no non-default factor is the "everything is
    # untextured grey" failure.
    textured = 0
    plain = 0
    for material in materials:
        pbr = material.get("pbrMetallicRoughness", {})
        if "baseColorTexture" in pbr:
            textured += 1
        else:
            plain += 1

    return {
        "path": path,
        "file_bytes": glb["file_bytes"],
        "generator": doc.get("asset", {}).get("generator"),
        "meshes": len(meshes),
        "primitives": len(primitives),
        "triangles": triangles,
        "vertices": vertices,
        "materials": len(materials),
        "materials_used": len(used),
        "materials_unused": len(materials) - len(used),
        "primitives_without_material": unbound,
        "materials_textured": textured,
        "materials_untextured": plain,
        "images": len(images_out),
        "image_detail": images_out,
        "textures": len(textures),
        "nodes": len(nodes),
        "skins": len(skins),
        "animations": len(animations),
        "bounds_min": [round(v, 4) for v in low] if low[0] != float("inf") else None,
        "bounds_max": [round(v, 4) for v in high] if high[0] != float("-inf") else None,
    }


def classify(name):
    """LOD and companion parts, from the naming convention the pipeline actually used."""
    base = name[:-4] if name.lower().endswith(".glb") else name
    if base.endswith("_lod1"):
        return base[:-5], "lod1"
    if base.endswith("_lod2"):
        return base[:-5], "lod2"
    if base.endswith("_collision_box"):
        return base[:-14], "collision_box"
    if base.endswith("_collision_hull"):
        return base[:-15], "collision_hull"
    return base, "main"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--paths", nargs="*", default=None)
    parser.add_argument("--library", action="store_true")
    parser.add_argument("--group-by-dir", action="store_true")
    parser.add_argument("--json", default=None)
    args = parser.parse_args()

    targets = []
    if args.library:
        for root, _dirs, files in os.walk(os.path.join(ASSETS, "ready")):
            targets += [os.path.join(root, f) for f in files if f.lower().endswith(".glb")]
        for root, _dirs, files in os.walk(os.path.join(ASSETS, "rigged")):
            targets += [os.path.join(root, f) for f in files if f.lower().endswith(".glb")]
    for path in (args.paths or []):
        if os.path.isdir(path):
            for root, _dirs, files in os.walk(path):
                targets += [os.path.join(root, f) for f in files if f.lower().endswith(".glb")]
        elif path.lower().endswith(".glb"):
            targets.append(path)

    targets = sorted(set(targets))
    results = [analyse(t) for t in targets]

    if args.json:
        with io.open(args.json, "w", encoding="utf-8") as handle:
            json.dump(results, handle, indent=2)
            handle.write("\n")
        print(f"  wrote {args.json}  ({len(results)} entries)")

    if args.group_by_dir:
        groups = defaultdict(list)
        for entry in results:
            groups[os.path.basename(os.path.dirname(entry["path"]))].append(entry)
        print(f"  {'asset':<42} {'part':<15} {'tris':>9} {'verts':>9} {'mat':>4} {'tex':>4} "
              f"{'unbound':>7} {'img':>4} {'res':<12}")
        for name in sorted(groups):
            for entry in sorted(groups[name], key=lambda e: e["path"]):
                if entry.get("error"):
                    print(f"  {name:<42} ERROR {entry['error']}")
                    continue
                part = classify(os.path.basename(entry["path"]))[1]
                res = ",".join(sorted({i.get("resolution", "?") for i in entry["image_detail"]})) or "-"
                print(f"  {name:<42} {part:<15} {entry['triangles']:>9} {entry['vertices']:>9} "
                      f"{entry['materials']:>4} {entry['textures']:>4} "
                      f"{entry['primitives_without_material']:>7} {entry['images']:>4} {res:<12}")
    else:
        for entry in results:
            print(json.dumps(entry, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
