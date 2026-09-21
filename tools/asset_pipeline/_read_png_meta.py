"""Dump the generation metadata embedded in a ComfyUI-generated PNG.

ComfyUI writes the API prompt and the UI workflow into tEXt chunks. Those record the
exact text, sampler settings and model stack used, which is far better evidence than
guessing from the image.
"""
import json
import struct
import sys

path = sys.argv[1]
with open(path, "rb") as handle:
    data = handle.read()

offset = 8
while offset < len(data):
    length = struct.unpack(">I", data[offset:offset + 4])[0]
    chunk_type = data[offset + 4:offset + 8].decode("latin-1")
    chunk = data[offset + 8:offset + 8 + length]
    if chunk_type in ("tEXt", "iTXt"):
        keyword, _, value = chunk.partition(b"\x00")
        name = keyword.decode("latin-1", "replace")
        print(f"=== {name}  ({len(value)} bytes) ===")
        try:
            payload = json.loads(value.decode("utf-8", "replace"))
        except ValueError:
            print(value.decode("utf-8", "replace")[:2000])
            print()
            offset += 12 + length
            continue
        print(json.dumps(payload, indent=2)[:6000])
        print()
    offset += 12 + length
    if chunk_type == "IEND":
        break
