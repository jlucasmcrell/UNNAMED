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
repair_spacing = '--creator-staging-offsets' in sys.argv[3:]
with open(src, "rb") as f:
    magic, version, _ = struct.unpack("<III", f.read(12))
    length, _ = struct.unpack("<II", f.read(8))
    gltf = json.loads(f.read(length))
    blength, _ = struct.unpack("<II", f.read(8))
    binary = bytearray(f.read(blength))


meshes_by_node = {n["name"]: n["mesh"] for n in gltf["nodes"] if "mesh" in n and "name" in n}
fixed = 0
for name, mesh in meshes_by_node.items():
    match = re.fullmatch(r"(.+)_LOD([1-9])(_lining)?", name)
    base_name = match.group(1) + '_LOD0' + (match.group(3) or '') if match else ''
    if not match or base_name not in meshes_by_node:
        continue
    base = gltf["meshes"][meshes_by_node[base_name]]["primitives"]
    if len(base) != len(gltf["meshes"][mesh]["primitives"]):
        raise ValueError(f"{name}: LOD surface count differs from LOD0; cannot map materials safely")
    for index, primitive in enumerate(gltf["meshes"][mesh]["primitives"]):
        if repair_spacing:
            # Repair only the measured HumGen creator preview-layout error, never
            # an arbitrary mesh or a legitimately displaced garment. Morph targets
            # are deltas and retain their original data; joints/weights/rests stay exact.
            position = gltf['accessors'][primitive['attributes']['POSITION']]
            basis = gltf['accessors'][base[index]['attributes']['POSITION']]
            offset = 2 * (int(match.group(2)) + 1)
            shifts = [position[edge][2] - basis[edge][2] for edge in ('min', 'max')]
            same_xy = all(abs(position[edge][axis] - basis[edge][axis]) < .1 for edge in ('min', 'max') for axis in (0, 1))
            if same_xy and all(abs(shift + offset) < .1 for shift in shifts):
                assert position['componentType'] == 5126 and position['type'] == 'VEC3' and 'sparse' not in position
                view = gltf['bufferViews'][position['bufferView']]
                start = view.get('byteOffset', 0) + position.get('byteOffset', 0)
                for vertex in range(position['count']):
                    at = start + vertex * view.get('byteStride', 12) + 8
                    struct.pack_into('<f', binary, at, struct.unpack_from('<f', binary, at)[0] + offset)
                for edge in ('min', 'max'):
                    position[edge][2] += offset
        # Native eyes/hair have their own atlas and UVs at each level. Keep
        # those baked maps; only the procedural garment fallback needs LOD0.
        material = gltf["materials"][primitive["material"]] if "material" in primitive else {}
        texture = material.get("pbrMetallicRoughness", {}).get("baseColorTexture")
        if texture is not None:
            image = gltf["images"][gltf["textures"][texture["index"]]["source"]]
            if image.get("name", "").endswith("_LOD" + match.group(2)):
                continue
        # HumGen's lower garment bands retain procedural colour-group masks.
        # With the baked LOD0 atlas these are not vertex albedo colours.
        for attribute in list(primitive['attributes']):
            if attribute.startswith('COLOR_') and attribute not in base[index]['attributes']:
                del primitive['attributes'][attribute]
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
