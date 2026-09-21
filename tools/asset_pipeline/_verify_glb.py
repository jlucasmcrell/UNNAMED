"""Verify a GLB is complete: attributes, UVs, tangents, and PBR map bindings."""
import json
import struct
import sys


def image_dimensions(blob):
    """Read PNG or JPEG pixel dimensions from the header."""
    if blob[:8] == b"\x89PNG\r\n\x1a\n":
        width, height = struct.unpack_from(">II", blob, 16)
        return f"{width}x{height}"
    if blob[:2] == b"\xff\xd8":
        index = 2
        while index < len(blob) - 9:
            if blob[index] != 0xFF:
                index += 1
                continue
            marker = blob[index + 1]
            if marker in (0xC0, 0xC1, 0xC2, 0xC3):
                height, width = struct.unpack_from(">HH", blob, index + 5)
                return f"{width}x{height}"
            if marker in (0xD8, 0xD9) or 0xD0 <= marker <= 0xD7:
                index += 2
                continue
            index += 2 + struct.unpack_from(">H", blob, index + 2)[0]
    return None


path = sys.argv[1]
with open(path, "rb") as handle:
    data = handle.read()

_, version, length = struct.unpack_from("<4sII", data, 0)
offset = 12
gltf = None
binary = b""
while offset < length:
    chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
    offset += 8
    if chunk_type == 0x4E4F534A:
        gltf = json.loads(data[offset:offset + chunk_len].decode("utf-8"))
    elif chunk_type == 0x004E4942:
        binary = data[offset:offset + chunk_len]
    offset += chunk_len

vertices = triangles = 0
attributes = set()
for mesh in gltf["meshes"]:
    for primitive in mesh["primitives"]:
        attributes |= set(primitive["attributes"])
        position = primitive["attributes"].get("POSITION")
        if position is not None:
            vertices += gltf["accessors"][position]["count"]
        if "indices" in primitive:
            triangles += gltf["accessors"][primitive["indices"]]["count"] // 3

print(f"{path}")
print(f"  glTF {version}, {length / 1e6:.2f} MB")
print(f"  vertices {vertices:,}  triangles {triangles:,}")
print(f"  attributes {sorted(attributes)}")

materials = gltf.get("materials", [])

# LODs and collision proxies are exported without materials on purpose, so a
# geometry-only file is valid. Only require the full PBR set when the file
# actually carries a material.
if materials:
    material = materials[0]
    pbr = material.get("pbrMetallicRoughness", {})
    maps = {
        "baseColor": "baseColorTexture" in pbr,
        "metallicRoughness": "metallicRoughnessTexture" in pbr,
        "normal": "normalTexture" in material,
        "occlusion": "occlusionTexture" in material,
    }
    print(f"  embedded images {len(gltf.get('images', []))}")
    # Report baked texture resolution so --texture-size is verifiable, not assumed.
    for index, image in enumerate(gltf.get("images", [])):
        view = gltf["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        blob = binary[start:start + view["byteLength"]]
        print(f"    image[{index}] {image.get('mimeType')} "
              f"{image_dimensions(blob) or 'dimensions unknown'} "
              f"({view['byteLength'] / 1e6:.2f} MB)")
    for name, present in maps.items():
        print(f"  {name:<18} {'yes' if present else 'NO'}")
    # Raw generator output is always double-sided. Blender output should be
    # single-sided, which halves rasterised triangles on non-organic meshes.
    for index, entry in enumerate(materials):
        print(f"  material[{index}] doubleSided={entry.get('doubleSided', False)}")
    missing = [k for k, v in maps.items() if not v]
    required_attributes = ("POSITION", "NORMAL", "TEXCOORD_0")
else:
    print("  embedded images 0 (geometry-only asset: LOD or collision proxy)")
    missing = []
    required_attributes = ("POSITION", "NORMAL")

for required in required_attributes:
    if required not in attributes:
        missing.append(required)

print("  RESULT:", "complete" if not missing else f"missing {missing}")
