"""Siann platinum hair: lift the dark strand grain of a HumGen GLB's haircard and haircap base-colour atlases.

HumGen draws the card atlas from its particle render, with deep shadowed strands that read as dirt in pale hair. Sleek
platinum hair (the owner's Siann reference) has little contrast, so every texel moves toward a cool white:
c' = 1 - (1 - c) * keep, times a faint cool tint. Alpha and every other image are untouched.

usage: python siann_hair_lift.py <in.glb> <out.glb> [keep=0.5]
"""
import io
import json
import struct
import sys

import numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
keep = float(sys.argv[3]) if len(sys.argv) > 3 else .5
TINT = np.array([.98, .98, 1.0])

with open(src, "rb") as f:
    magic, version, _ = struct.unpack("<III", f.read(12))
    length, _ = struct.unpack("<II", f.read(8))
    gltf = json.loads(f.read(length))
    blength, _ = struct.unpack("<II", f.read(8))
    binary = f.read(blength)

lifted = {}
for image in gltf["images"]:
    name = image.get("name", "")
    if "bufferView" not in image or not any(part in name for part in ("HairCards_BaseColor", "Haircap_BaseColor")):
        continue
    view = gltf["bufferViews"][image["bufferView"]]
    start = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[start:start + view["byteLength"]])).convert("RGBA")
    pixels = np.asarray(picture).astype(np.float32) / 255
    pixels[..., :3] = np.clip((1 - (1 - pixels[..., :3]) * keep) * TINT, 0, 1)
    out = io.BytesIO()
    Image.fromarray((pixels * 255 + .5).astype(np.uint8), "RGBA").save(out, "PNG")
    lifted[image["bufferView"]] = out.getvalue()
    image["mimeType"] = "image/png"

packed = bytearray()
for index, view in enumerate(gltf["bufferViews"]):
    data = lifted.get(index)
    if data is None:
        start = view.get("byteOffset", 0)
        data = binary[start:start + view["byteLength"]]
    packed += b"\0" * (-len(packed) % 4)
    view["byteOffset"] = len(packed)
    view["byteLength"] = len(data)
    packed += data
packed += b"\0" * (-len(packed) % 4)
gltf["buffers"][0]["byteLength"] = len(packed)
text = json.dumps(gltf, separators=(",", ":")).encode()
text += b" " * (-len(text) % 4)
with open(dst, "wb") as f:
    f.write(struct.pack("<III", magic, version, 12 + 8 + len(text) + 8 + len(packed)))
    f.write(struct.pack("<II", len(text), 0x4E4F534A) + text)
    f.write(struct.pack("<II", len(packed), 0x004E4942) + packed)
print(f"{src} -> {dst}: {len(lifted)} hair atlases lifted (keep {keep})")
