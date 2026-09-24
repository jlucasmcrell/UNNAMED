"""Ground-truth size audit: GLB world bounds vs the two caches that describe it.

`_audit_semantic_scale.py` reads assets/catalog.json, which `_catalog_assets.py` builds from each
asset's _meta.json. Those are two caches in a row, and either can go stale after a rescale, which
makes the audit compare an out-of-date number against the expectation table and hands
`_rescale_glb.py` a factor that scales already-correct geometry a second time.

This reads the geometry itself - accessor POSITION bounds composed through the node hierarchy, so a
GLB with a node scale is measured in world space - and reports every disagreement.

Usage:
    python _glb_truth_check.py                 # summary + first 40 disagreements
    python _glb_truth_check.py --all           # every disagreement
"""
import argparse
import json
import os
import struct

ASSETS = r"W:\UNNAMED\assets"
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942


def read_glb(path):
    data = open(path, "rb").read()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        raise ValueError("not a GLB")
    offset, gltf, binary = 12, None, None
    while offset < length:
        clen, ctype = struct.unpack_from("<II", data, offset)
        offset += 8
        payload = data[offset:offset + clen]
        if ctype == JSON_CHUNK:
            gltf = json.loads(payload.decode("utf-8"))
        elif ctype == BIN_CHUNK:
            binary = payload
        offset += clen
    return gltf, binary


def world_bounds(gltf):
    """POSITION accessor bounds composed through node TRS, in world space."""
    def trs(node):
        if "matrix" in node:
            m = node["matrix"]
            return [[m[0], m[4], m[8], m[12]], [m[1], m[5], m[9], m[13]],
                    [m[2], m[6], m[10], m[14]], [m[3], m[7], m[11], m[15]]]
        t = node.get("translation", [0, 0, 0])
        x, y, z, w = node.get("rotation", [0, 0, 0, 1])
        s = node.get("scale", [1, 1, 1])
        rot = [
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
        ]
        return [[rot[i][j] * s[j] for j in range(3)] + [t[i]] for i in range(3)] + [[0, 0, 0, 1]]

    def mul(a, b):
        return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]

    nodes = gltf.get("nodes", [])
    mats = {}
    identity = [[1.0 if i == j else 0.0 for j in range(4)] for i in range(4)]

    def walk(index, parent):
        world = mul(parent, trs(nodes[index]))
        mats[index] = world
        for child in nodes[index].get("children", []):
            walk(child, world)

    for scene in gltf.get("scenes", []):
        for root in scene.get("nodes", []):
            walk(root, identity)

    lo = [1e9] * 3
    hi = [-1e9] * 3
    for index, node in enumerate(nodes):
        if "mesh" not in node:
            continue
        m = mats.get(index, identity)
        for prim in gltf["meshes"][node["mesh"]].get("primitives", []):
            acc_index = prim.get("attributes", {}).get("POSITION")
            if acc_index is None:
                continue
            acc = gltf["accessors"][acc_index]
            for cx in (acc["min"][0], acc["max"][0]):
                for cy in (acc["min"][1], acc["max"][1]):
                    for cz in (acc["min"][2], acc["max"][2]):
                        p = [cx, cy, cz, 1.0]
                        w = [sum(m[i][k] * p[k] for k in range(4)) for i in range(3)]
                        for k in range(3):
                            lo[k] = min(lo[k], w[k])
                            hi[k] = max(hi[k], w[k])
    return lo, hi


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--all", action="store_true")
    args = parser.parse_args()

    catalog = json.load(open(os.path.join(ASSETS, "catalog.json"), encoding="utf-8"))
    rows = []
    no_glb = 0
    node_scaled = []
    for entry in catalog["assets"]:
        asset_id = entry["id"]
        glb = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}.glb")
        if not os.path.exists(glb):
            no_glb += 1
            continue
        gltf, _ = read_glb(glb)
        lo, hi = world_bounds(gltf)
        if lo[0] > 1e8:
            no_glb += 1
            continue
        glb_max = max(hi[k] - lo[k] for k in range(3))
        for node in gltf.get("nodes", []):
            s = node.get("scale")
            if s and any(abs(v - 1.0) > 1e-6 for v in s):
                node_scaled.append((asset_id, s))
                break
        meta_path = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}_meta.json")
        meta_max = None
        if os.path.exists(meta_path):
            dims = (json.load(open(meta_path, encoding="utf-8")).get("transform") or {}).get("dimensions")
            if dims:
                meta_max = max(dims)
        cat_max = max(entry["dimensions_m"]) if entry.get("dimensions_m") else None
        meta_off = meta_max is not None and abs(meta_max - glb_max) > 0.005
        cat_off = cat_max is not None and abs(cat_max - glb_max) > 0.005
        if meta_off or cat_off:
            rows.append((asset_id, glb_max, meta_max, cat_max))

    print(f"assets in catalog        : {len(catalog['assets'])}")
    print(f"measured from GLB        : {len(catalog['assets']) - no_glb}")
    print(f"no measurable GLB        : {no_glb}")
    print(f"GLBs with a node scale   : {len(node_scaled)}  {node_scaled[:6]}")
    print(f"catalog/meta disagree with GLB : {len(rows)}")
    print()
    if rows:
        print(f"  {'asset':<44} {'GLB':>9} {'meta':>9} {'catalog':>9}")
        print("  " + "-" * 76)
        for asset_id, glb_max, meta_max, cat_max in (rows if args.all else rows[:40]):
            m = "-" if meta_max is None else f"{meta_max:.4f}"
            c = "-" if cat_max is None else f"{cat_max:.4f}"
            print(f"  {asset_id:<44} {glb_max:>9.4f} {m:>9} {c:>9}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
