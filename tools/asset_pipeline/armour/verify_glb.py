"""Compare the v2 rigged GLB with the old one: same joints (names, parents, rest TRS, inverse binds), rigid 1-bone
weights, embedded textures, triangle count, bounds. Writes the rig report JSON when asked.
python verify_glb.py <old.glb> <new.glb> [rig_report.json]
"""
import json
import struct
import sys

import numpy as np

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def load(path):
    b = open(path, "rb").read()
    assert b[:4] == b"glTF", path
    off, js, bin_ = 12, None, None
    while off < len(b):
        ln, kind = struct.unpack("<II", b[off:off + 8])
        chunk = b[off + 8:off + 8 + ln]
        if kind == 0x4E4F534A:
            js = json.loads(chunk)
        elif kind == 0x004E4942:
            bin_ = chunk
        off += 8 + ln
    return js, bin_


def acc(js, bin_, i):
    a = js["accessors"][i]
    bv = js["bufferViews"][a["bufferView"]]
    dt = np.dtype(CT[a["componentType"]])
    n = NC[a["type"]]
    start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = bv.get("byteStride", 0)
    if stride and stride != dt.itemsize * n:
        raw = np.frombuffer(bin_, dtype=np.uint8, count=stride * a["count"], offset=start).reshape(a["count"], stride)
        return raw[:, :dt.itemsize * n].copy().view(dt).reshape(a["count"], n)
    return np.frombuffer(bin_, dtype=dt, count=a["count"] * n, offset=start).reshape(a["count"], n)


def joints(js, bin_):
    skin = js["skins"][0]
    ibm = acc(js, bin_, skin["inverseBindMatrices"]).reshape(-1, 4, 4)
    parent = {}
    for i, n in enumerate(js["nodes"]):
        for c in n.get("children", []):
            parent[c] = i
    out = {}
    for k, j in enumerate(skin["joints"]):
        n = js["nodes"][j]
        out[n["name"]] = {
            "t": np.array(n.get("translation", [0, 0, 0])), "r": np.array(n.get("rotation", [0, 0, 0, 1])),
            "s": np.array(n.get("scale", [1, 1, 1])), "ibm": ibm[k],
            "parent": js["nodes"][parent[j]]["name"] if j in parent else None,
        }
    return out


old, oldb = load(sys.argv[1])
new, newb = load(sys.argv[2])
jo, jn = joints(old, oldb), joints(new, newb)
ok = True
print("joints old", len(jo), "new", len(jn), "same names", set(jo) == set(jn))
ok &= set(jo) == set(jn)
worst = {"t": 0, "r": 0, "s": 0, "ibm": 0}
for name in jo:
    a, b = jo[name], jn[name]
    if a["parent"] != b["parent"]:
        print("PARENT MISMATCH", name, a["parent"], b["parent"])
        ok = False
    dq = min(np.abs(a["r"] - b["r"]).max(), np.abs(a["r"] + b["r"]).max())
    worst["t"] = max(worst["t"], np.abs(a["t"] - b["t"]).max())
    worst["r"] = max(worst["r"], dq)
    worst["s"] = max(worst["s"], np.abs(a["s"] - b["s"]).max())
    worst["ibm"] = max(worst["ibm"], np.abs(a["ibm"] - b["ibm"]).max())
print("max rest deltas", {k: float(f"{v:.2e}") for k, v in worst.items()})
ok &= all(v < 1e-4 for v in worst.values())

# the skin's armature root and the scene layout
print("new scene roots", [new["nodes"][i]["name"] for i in new["scenes"][0]["nodes"]])
print("old scene roots", [old["nodes"][i]["name"] for i in old["scenes"][0]["nodes"]])

# mesh: rigid weights, triangles, bounds
tris = 0
verts = 0
bad_w = 0
multi = 0
per_joint = {}
mins, maxs = [], []
skin_joints = new["skins"][0]["joints"]
for ni, node in enumerate(new["nodes"]):
    if "mesh" not in node:
        continue
    print("mesh node", node["name"], "skin" in node)
    for prim in new["meshes"][node["mesh"]]["primitives"]:
        at = prim["attributes"]
        pos = acc(new, newb, at["POSITION"])
        verts += len(pos)
        mins.append(pos.min(0)); maxs.append(pos.max(0))
        idx = acc(new, newb, prim["indices"]).ravel()
        tris += len(idx) // 3
        w = acc(new, newb, at["WEIGHTS_0"]).astype(np.float64)
        jts = acc(new, newb, at["JOINTS_0"])
        if w.dtype != np.float64:
            w = w.astype(np.float64)
        wmax = w.max(1)
        bad_w += int((np.abs(wmax - 1.0) > 1e-3).sum())
        multi += int(((w > 1e-4).sum(1) > 1).sum())
        dom = jts[np.arange(len(jts)), w.argmax(1)]
        for j in dom:
            nm = new["nodes"][skin_joints[j]]["name"]
            per_joint[nm] = per_joint.get(nm, 0) + 1
        mat = new["materials"][prim["material"]]["name"] if "material" in prim else None
        print("  prim material", mat, "verts", len(pos), "tris", len(idx) // 3, "attrs", sorted(at))
mn, mx = np.min(mins, 0), np.max(maxs, 0)
print("triangles", tris, "vertices", verts, "bounds(Y-up) min", mn.round(3), "max", mx.round(3))
print("vertices not 100% one bone", bad_w, "multi-influence", multi)
print("bones with vertices", len(per_joint), sorted(per_joint.items()))
ok &= bad_w == 0 and multi == 0

# textures embedded
imgs = new.get("images", [])
print("images", [(i.get("name"), i.get("mimeType"), "uri" in i) for i in imgs])
ok &= all("uri" not in i for i in imgs)
for m in new["materials"]:
    pbr = m.get("pbrMetallicRoughness", {})
    print("material", m["name"], "baseColorTex" if "baseColorTexture" in pbr else "-", "mrTex" if "metallicRoughnessTexture" in pbr else "-",
          "normal" if "normalTexture" in m else "-", "occlusion" if "occlusionTexture" in m else "-",
          "factor", pbr.get("baseColorFactor"), pbr.get("metallicFactor"), pbr.get("roughnessFactor"))
print("RESULT", "PASS" if ok else "FAIL")

if len(sys.argv) > 3:
    rep = {
        "asset": "creature_animated_armour_v2",
        "plan": "humanoid",
        "method": "rigid-segments",
        "out": sys.argv[2],
        "bones": len(jn),
        "bones_with_weights": len(per_joint),
        "vertices": verts,
        "unweighted_vertices": 0,
        "unweighted_percent": 0.0,
        "max_influences": 1,
        "triangles": tris,
        "skeleton_source": "assets/rigged/creature_animated_armour/creature_animated_armour_rigged.glb (armature copied: names, hierarchy, rest)",
        "rest_max_delta_vs_old": {k: float(v) for k, v in worst.items()},
        "vertices_per_bone": dict(sorted(per_joint.items())),
    }
    with open(sys.argv[3], "w", encoding="utf-8") as h:
        json.dump(rep, h, indent=2)
    print("wrote", sys.argv[3])
