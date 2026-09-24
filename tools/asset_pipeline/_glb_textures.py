"""Report the mean colour and size of every image embedded in a GLB.

A building that renders near-white can mean two very different things: the base-colour texture is
genuinely pale, or the preview's lighting is blowing it out. Guessing between them is how a
material problem gets "fixed" by darkening an asset that was never too bright, so this reads the
textures out of the container and measures them.

Also reports how the material references its images, so a texture that is present but unbound is
visible rather than hidden behind a material that looks correct.

Usage:
    python _glb_textures.py <glb> [<glb> ...]
"""
import io
import json
import os
import struct
import sys

from PIL import Image


def containers(path):
    """Yield (json, bin_chunk) from a GLB."""
    data = open(path, "rb").read()
    offset, gltf, blob = 12, None, None
    while offset + 8 <= len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        chunk = data[offset + 8:offset + 8 + length]
        if kind == 0x4E4F534A:
            gltf = json.loads(chunk.decode("utf-8"))
        elif kind == 0x004E4942:
            blob = chunk
        offset += 8 + length + ((4 - length % 4) % 4)
    return gltf, blob


def report(path):
    gltf, blob = containers(path)
    name = os.path.basename(path)
    print(f"{name}  {len(open(path, 'rb').read()) / 1048576:.1f} MiB")
    images = gltf.get("images", [])
    if not images:
        print("    no embedded images")
        return
    means = []
    for index, image in enumerate(images):
        view = gltf["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        raw = blob[start:start + view["byteLength"]]
        try:
            with Image.open(io.BytesIO(raw)) as handle:
                rgb = handle.convert("RGB")
                small = rgb.resize((64, 64))
                pixels = list(small.getdata())
                mean = tuple(round(sum(p[c] for p in pixels) / len(pixels)) for c in range(3))
                means.append(mean)
                print(f"    image {index}  {image.get('mimeType')}  {rgb.size[0]}x{rgb.size[1]}"
                      f"  {len(raw) / 1024:.0f} KiB  mean RGB {mean}")
        except Exception as error:
            print(f"    image {index}  unreadable: {error}")
    if means:
        overall = tuple(round(sum(m[c] for m in means) / len(means)) for c in range(3))
        print(f"    mean across images: {overall}  (0-255; ~128 is mid grey)")
    for material in gltf.get("materials", []):
        pbr = material.get("pbrMetallicRoughness", {})
        keys = []
        if "baseColorTexture" in pbr:
            keys.append(f"baseColor->tex{pbr['baseColorTexture']['index']}")
        if "normalTexture" in pbr:
            keys.append(f"normal->tex{pbr['normalTexture']['index']}")
        if "metallicRoughnessTexture" in pbr:
            keys.append(f"orm->tex{pbr['metallicRoughnessTexture']['index']}")
        factor = pbr.get("baseColorFactor")
        print(f"    material {material.get('name')}: {keys or 'no textures bound'} "
              f"baseColorFactor={factor}")


if __name__ == "__main__":
    for argument in sys.argv[1:]:
        report(argument)
