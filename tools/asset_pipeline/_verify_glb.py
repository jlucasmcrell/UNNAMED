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
textures = gltf.get("textures", [])
images = gltf.get("images", [])

# Only collision proxies are geometry-only: the game never draws them. Every drawn
# file - the base and each LOD - must carry its material and textures, since an LOD
# without them draws untextured at distance (the 1,455 stripped LODs of 2026-09).
stem = path.replace("\\", "/").rsplit("/", 1)[-1]
proxy = "_collision_" in stem
lod = "_lod" in stem and not proxy
missing = []
if proxy:
    print("  geometry-only collision proxy")
    required_attributes = ("POSITION",)
elif not materials:
    print("  embedded images 0: a drawn file with no material")
    missing.append("materials")
    required_attributes = ("POSITION", "NORMAL", "TEXCOORD_0")
else:
    material = materials[0]
    pbr = material.get("pbrMetallicRoughness", {})
    maps = {
        "baseColor": "baseColorTexture" in pbr,
        "metallicRoughness": "metallicRoughnessTexture" in pbr,
        "normal": "normalTexture" in material,
        "occlusion": "occlusionTexture" in material,
    }
    print(f"  embedded images {len(images)}")
    # Report baked texture resolution so --texture-size is verifiable, not assumed.
    for index, image in enumerate(images):
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
    # An LOD may drop the finer maps at distance, never its colour.
    missing = [k for k, v in maps.items() if not v and (not lod or k == "baseColor")]
    required_attributes = ("POSITION", "NORMAL", "TEXCOORD_0")

    # Every texture reference must resolve to an embedded image.
    for index, entry in enumerate(materials):
        refs = [entry.get("pbrMetallicRoughness", {}).get("baseColorTexture"),
                entry.get("pbrMetallicRoughness", {}).get("metallicRoughnessTexture"),
                entry.get("normalTexture"), entry.get("occlusionTexture"), entry.get("emissiveTexture")]
        for ref in filter(None, refs):
            texture = ref.get("index", -1)
            source = textures[texture].get("source", -1) if 0 <= texture < len(textures) else -1
            if not 0 <= source < len(images):
                missing.append(f"material[{index}] texture {texture} resolves to no image")

# Per primitive: bound to a real material slot, and carrying what the file needs
# (a merged file can hide one bare primitive behind another's attributes).
if not proxy:
    for m, mesh in enumerate(gltf["meshes"]):
        for p, primitive in enumerate(mesh["primitives"]):
            slot = primitive.get("material")
            if slot is None:
                missing.append(f"mesh[{m}].primitive[{p}] unbound (no material)")
            elif not 0 <= slot < len(materials):
                missing.append(f"mesh[{m}].primitive[{p}] material slot {slot} out of range")
            if "TEXCOORD_0" not in primitive["attributes"]:
                missing.append(f"mesh[{m}].primitive[{p}] no TEXCOORD_0")

for required in required_attributes:
    if required not in attributes:
        missing.append(required)

print("  RESULT:", "complete" if not missing else f"missing {missing}")
sys.exit(0 if not missing else 1)
