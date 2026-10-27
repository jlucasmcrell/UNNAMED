"""Point HumGen LOD1-3 garment primitives at their LOD0 baked material, then drop unreferenced materials/textures/images.

HumGen's Godot recipe bakes only the LOD0 garment materials; the LOD1+ copies keep the procedural cloth shader and the glTF
exporter writes its first image node (the colour-group mask, pure blue) as base colour. The decimated LODs keep LOD0's UV
layout, so LOD0's baked material is the correct one. Geometry, skin and node data are unchanged.
usage: python fix_lod_materials.py <in.glb> <out.glb>
"""
import json
import re
import struct
import sys

src, dst = sys.argv[1], sys.argv[2]
with open(src, "rb") as f:
    magic, version, _ = struct.unpack("<III", f.read(12))
    length, _ = struct.unpack("<II", f.read(8))
    gltf = json.loads(f.read(length))
    blength, _ = struct.unpack("<II", f.read(8))
    binary = f.read(blength)


meshes_by_node = {n["name"]: n["mesh"] for n in gltf["nodes"] if "mesh" in n and "name" in n}
fixed = 0
for name, mesh in meshes_by_node.items():
    match = re.fullmatch(r"(.+)_LOD([1-9])", name)
    if not match or match.group(1) + "_LOD0" not in meshes_by_node:
        continue
    base = gltf["meshes"][meshes_by_node[match.group(1) + "_LOD0"]]["primitives"]
    if len(base) != len(gltf["meshes"][mesh]["primitives"]):
        raise ValueError(f"{name}: LOD surface count differs from LOD0; cannot map materials safely")
    for index, primitive in enumerate(gltf["meshes"][mesh]["primitives"]):
        # Native eyes/hair have their own atlas and UVs at each level. Keep
        # those baked maps; only the procedural garment fallback needs LOD0.
        material = gltf["materials"][primitive["material"]] if "material" in primitive else {}
        texture = material.get("pbrMetallicRoughness", {}).get("baseColorTexture")
        if texture is not None:
            image = gltf["images"][gltf["textures"][texture["index"]]["source"]]
            if image.get("name", "").endswith("_LOD" + match.group(2)):
                continue
        if "material" in base[index] and primitive.get("material") != base[index]["material"]:
            primitive["material"] = base[index]["material"]
            fixed += 1


def prune(kind, used, references):
    keep = sorted(used)
    remap = {old: new for new, old in enumerate(keep)}
    gltf[kind] = [gltf[kind][i] for i in keep]
    for holder, key in references:
        holder[key] = remap[holder[key]]


material_refs = [(p, "material") for m in gltf["meshes"] for p in m["primitives"] if "material" in p]
prune("materials", {h[k] for h, k in material_refs}, material_refs)
texture_refs = []


def walk(node):
    for key, value in node.items():
        if isinstance(value, dict):
            if key.endswith("Texture") and "index" in value:
                texture_refs.append((value, "index"))
            walk(value)


for material in gltf["materials"]:
    walk(material)
prune("textures", {h[k] for h, k in texture_refs}, texture_refs)
image_refs = [(t, "source") for t in gltf["textures"]]
prune("images", {h[k] for h, k in image_refs}, image_refs)

def buffer_view_refs(node):
    # Sparse accessor indices/values and extension payloads also reference views.
    # Pruning only the top-level accessor view corrupts facial blend shapes.
    if isinstance(node, dict):
        for key, value in node.items():
            if key == "bufferView":
                yield node, key
            elif isinstance(value, (dict, list)):
                yield from buffer_view_refs(value)
    elif isinstance(node, list):
        for value in node:
            yield from buffer_view_refs(value)


view_refs = list(buffer_view_refs(gltf))
used_views = {holder[key] for holder, key in view_refs}
old_views = gltf["bufferViews"]
keep = sorted(used_views)
remap = {old: new for new, old in enumerate(keep)}
packed = bytearray()
views = []
for old in keep:
    view = dict(old_views[old])
    start = view.get("byteOffset", 0)
    data = binary[start:start + view["byteLength"]]
    packed += b"\0" * (-len(packed) % 4)
    view["byteOffset"] = len(packed)
    packed += data
    views.append(view)
for holder, key in view_refs:
    holder[key] = remap[holder[key]]
gltf["bufferViews"] = views
packed += b"\0" * (-len(packed) % 4)
gltf["buffers"][0]["byteLength"] = len(packed)

text = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
text += b" " * (-len(text) % 4)
with open(dst, "wb") as f:
    f.write(struct.pack("<III", magic, version, 12 + 8 + len(text) + 8 + len(packed)))
    f.write(struct.pack("<II", len(text), 0x4E4F534A) + text)
    f.write(struct.pack("<II", len(packed), 0x004E4942) + packed)
print(f"{src} -> {dst}: {fixed} LOD primitives re-materialed, {len(gltf['materials'])} materials, {len(gltf['images'])} images kept")
