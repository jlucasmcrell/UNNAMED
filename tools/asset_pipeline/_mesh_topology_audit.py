"""Measure mesh topology on GLB files: closedness, degenerate faces, normal consistency, shell count.

Written to separate two explanations for the same visual symptom. Crinkled, foil-like surfaces with
hard bright edges and visible gaps at joints can come from two very different defects, and they have
different fixes:

  **Thin shells** - the reconstruction produced surfaces with no thickness, so a timber is two facing
  sheets with an open edge rather than a solid beam. Two-sided shading on an open edge is what produces
  the bright rim. This is a source defect; no export change fixes it.

  **Broken normals** - the geometry is fine but vertex normals are inconsistent or inverted, so lighting
  breaks across a surface that is actually sound. This is an export defect and is repairable.

The distinguishing measurements are the boundary-edge count (open edges mean shells), the winding
consistency between adjacent faces on shared edges (opposite winding on a shared edge means inverted
normals), and the degenerate-triangle count.

Usage:
    python _mesh_topology_audit.py --paths <glb> [<glb> ...]
    python _mesh_topology_audit.py --compare
"""
import argparse
import json
import os
import struct
import sys
from collections import defaultdict

ASSETS = r"W:\UNNAMED\assets"
COMPONENT = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2),
             5125: ("I", 4), 5126: ("f", 4)}
COMPONENT_COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def read_glb(path):
    with open(path, "rb") as handle:
        blob = handle.read()
    if blob[:4] != b"glTF":
        return None
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
        offset += 8 + length
        offset += (-length) % 4
    return document, binary


def read_accessor(doc, binary, index):
    accessor = doc["accessors"][index]
    view = doc["bufferViews"][accessor["bufferView"]]
    fmt, size = COMPONENT[accessor["componentType"]]
    count = COMPONENT_COUNT[accessor["type"]]
    stride = view.get("byteStride") or (size * count)
    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    values = []
    for i in range(accessor["count"]):
        base = start + i * stride
        values.append(struct.unpack_from("<" + fmt * count, binary, base))
    return values


def triangulate(doc, binary, primitive):
    """Returns (positions, triangles) with triangles as index triples."""
    position_index = primitive["attributes"].get("POSITION")
    if position_index is None:
        return [], []
    positions = read_accessor(doc, binary, position_index)

    if "indices" in primitive:
        flat = [v[0] for v in read_accessor(doc, binary, primitive["indices"])]
    else:
        flat = list(range(len(positions)))

    mode = primitive.get("mode", 4)
    triangles = []
    if mode == 4:
        for i in range(0, len(flat) - 2, 3):
            triangles.append((flat[i], flat[i + 1], flat[i + 2]))
    elif mode == 5:  # TRIANGLE_STRIP
        for i in range(len(flat) - 2):
            tri = (flat[i], flat[i + 1], flat[i + 2])
            triangles.append(tri if i % 2 == 0 else (tri[1], tri[0], tri[2]))
    return positions, triangles


def analyse(path):
    loaded = read_glb(path)
    if loaded is None:
        return {"path": path, "error": "not a GLB"}
    doc, binary = loaded
    if doc is None:
        return {"path": path, "error": "no JSON chunk"}

    positions = []
    triangles = []
    for mesh in doc.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            pos, tris = triangulate(doc, binary, primitive)
            base = len(positions)
            positions += pos
            triangles += [(a + base, b + base, c + base) for a, b, c in tris]

    # A shared edge seen twice with the same winding means the two faces disagree about which way is
    # out. That is the signature of inverted normals rather than of open geometry.
    directed = defaultdict(int)
    undirected = defaultdict(int)
    degenerate = 0
    for a, b, c in triangles:
        if a == b or b == c or a == c:
            degenerate += 1
            continue
        pa, pb, pc = positions[a], positions[b], positions[c]
        # Zero-area triangles shading as garbage.
        ux, uy, uz = (pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2])
        vx, vy, vz = (pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2])
        cx, cy, cz = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
        if cx * cx + cy * cy + cz * cz < 1e-14:
            degenerate += 1
            continue
        for edge in ((a, b), (b, c), (c, a)):
            directed[edge] += 1
            undirected[tuple(sorted(edge))] += 1

    boundary = sum(1 for e, n in undirected.items() if n == 1)
    nonmanifold = sum(1 for e, n in undirected.items() if n > 2)
    # Same-direction duplicate on a shared edge = inconsistent winding.
    flipped = sum(1 for e, n in directed.items() if n > 1)

    return {
        "path": path,
        "vertices": len(positions),
        "triangles": len(triangles),
        "degenerate_triangles": degenerate,
        "degenerate_pct": round(100.0 * degenerate / max(len(triangles), 1), 2),
        "boundary_edges": boundary,
        "boundary_pct": round(100.0 * boundary / max(len(undirected), 1), 2),
        "nonmanifold_edges": nonmanifold,
        "duplicate_directed_edges": flipped,
        "closed": boundary == 0,
    }


CASES = [
    ("barrel (good)", r"ready\container_barrel_oak\container_barrel_oak.glb"),
    ("cauldron (good)", r"ready\magic_cauldron_small\magic_cauldron_small.glb"),
    ("iron billet (good)", r"ready\resource_iron_billet\resource_iron_billet.glb"),
    ("waystone (good)", r"ready\landmark_ashen_waystone\landmark_ashen_waystone.glb"),
    ("hound (good)", r"ready\creature_ash_ember_hound\creature_ash_ember_hound.glb"),
    ("spear (good)", r"ready\weapon_march_spear\weapon_march_spear.glb"),
    ("winch (FAILED)", r"ready\prop_quarry_winch\prop_quarry_winch.glb"),
    ("blocked shaft (FAILED)", r"ready\prop_blocked_shaft\prop_blocked_shaft.glb"),
    ("cart (weak)", r"ready\prop_cart_damaged_merchant\prop_cart_damaged_merchant.glb"),
    ("woundmoss (weak)", r"ready\resource_woundmoss\resource_woundmoss.glb"),
    ("rail track (weak)", r"ready\prop_quarry_rail_track\prop_quarry_rail_track.glb"),
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--paths", nargs="*", default=None)
    parser.add_argument("--compare", action="store_true")
    parser.add_argument("--json", default=None)
    args = parser.parse_args()

    targets = []
    if args.compare:
        targets = [(label, os.path.join(ASSETS, rel)) for label, rel in CASES]
    else:
        targets = [(os.path.basename(p), p) for p in (args.paths or [])]

    results = []
    print(f"  {'case':<24} {'tris':>7} {'verts':>7} {'degen%':>7} {'boundary%':>10} "
          f"{'nonmanif':>9} {'dup-dir':>8} {'closed':>7}")
    for label, path in targets:
        if not os.path.exists(path):
            print(f"  {label:<24} MISSING")
            continue
        r = analyse(path)
        if r.get("error"):
            print(f"  {label:<24} ERROR {r['error']}")
            continue
        r["label"] = label
        results.append(r)
        print(f"  {label:<24} {r['triangles']:>7} {r['vertices']:>7} "
              f"{r['degenerate_pct']:>7} {r['boundary_pct']:>10} "
              f"{r['nonmanifold_edges']:>9} {r['duplicate_directed_edges']:>8} "
              f"{str(r['closed']):>7}")

    if args.json and results:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(results, handle, indent=2)
            handle.write("\n")
        print(f"\n  wrote {args.json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
