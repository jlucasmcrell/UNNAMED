"""Downscale the embedded textures inside a creature GLB to 1024, so a packaged build will accept it.

The game refuses a packaged asset whose texture is 2K or larger and uncompressed (Main.Start treats it as fatal), which is what stopped the
farm run on the moose. This rewrites each image inside the GLB's binary chunk: images are resized and re-encoded, every bufferView is rebuilt
with new offsets, and the JSON chunk is written back with the same padding rules the earlier pitch fix used.
"""
import io
import json
import struct
import sys
from pathlib import Path

from PIL import Image

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
LIMIT = 1024


def read_glb(path):
    data = path.read_bytes()
    magic, version, length = struct.unpack_from('<III', data, 0)
    assert magic == 0x46546C67, 'not a glb'
    offset, chunks = 12, []
    while offset < length:
        size, kind = struct.unpack_from('<II', data, offset)
        chunks.append((kind, data[offset + 8:offset + 8 + size]))
        offset += 8 + size
    return chunks


def write_glb(path, chunks):
    body = b''
    for kind, payload in chunks:
        fill = b' ' if kind == JSON_CHUNK else b'\0'
        padded = payload + fill * ((4 - len(payload) % 4) % 4)
        body += struct.pack('<II', len(padded), kind) + padded
    path.write_bytes(struct.pack('<III', 0x46546C67, 2, 12 + len(body)) + body)


def shrink(path):
    chunks = read_glb(path)
    doc = json.loads(chunks[0][1].decode('utf-8'))
    binary = chunks[1][1]
    views = doc['bufferViews']
    images = {image['bufferView']: image for image in doc.get('images', []) if 'bufferView' in image}

    rebuilt = bytearray()
    resized = []
    for index, view in enumerate(views):
        start = view.get('byteOffset', 0)
        payload = binary[start:start + view['byteLength']]
        if index in images:
            picture = Image.open(io.BytesIO(payload))
            if max(picture.size) > LIMIT:
                scale = LIMIT / max(picture.size)
                small = picture.resize((max(1, int(picture.width * scale)), max(1, int(picture.height * scale))), Image.LANCZOS)
                buffer = io.BytesIO()
                small.save(buffer, format='PNG', optimize=True)
                payload = buffer.getvalue()
                resized.append((images[index].get('name', index), picture.size, small.size, len(payload)))
        while len(rebuilt) % 4:
            rebuilt.append(0)
        view['byteOffset'] = len(rebuilt)
        view['byteLength'] = len(payload)
        rebuilt.extend(payload)

    doc['buffers'][0]['byteLength'] = len(rebuilt)
    chunks[0] = (JSON_CHUNK, json.dumps(doc, separators=(',', ':')).encode('utf-8'))
    chunks[1] = (BIN_CHUNK, bytes(rebuilt))
    write_glb(path, chunks)
    return resized


for target in sys.argv[1:]:
    for name, before, after, size in shrink(Path(target)):
        print(f'{Path(target).name}: {name} {before[0]}x{before[1]} -> {after[0]}x{after[1]} ({size} bytes)')
